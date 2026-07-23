using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pix.Launcher;

internal sealed record HealthSnapshot(string Status, bool AgentRunning, int RunningAgentCount, string? StartedAt);
internal sealed record AppUpdatePayload(string StagedDir, string BackupDir, string FromVersion, string ToVersion);
internal sealed record RestartMarker(string Reason, DateTimeOffset RequestedAt, AppUpdatePayload? AppUpdate);
internal sealed record BalanceSnapshot(string Currency, decimal Total, decimal Granted, decimal ToppedUp);
internal sealed record QuotaTierSnapshot(string Name, string Label, double RemainingPercent, string? ResetsAt);
internal sealed record ProviderAccountSnapshot(
    string Id,
    string DisplayName,
    string Status,
    bool? IsAvailable,
    IReadOnlyList<BalanceSnapshot>? Balances,
    IReadOnlyList<QuotaTierSnapshot>? Tiers,
    string? Message);
internal sealed record LauncherStatusSnapshot(IReadOnlyList<ProviderAccountSnapshot> Providers, string UpdatedAt);

/// <summary>Owns the Pi Web Node process and its authenticated control API.</summary>
internal sealed class PiWebProcessManager : IDisposable
{
    private readonly HttpClient httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private WindowsJobObject? job;
    private Process? process;
    private string? launcherToken;
    private string? serviceLogPath;
    private string? webRoot;

    public int? ProcessId => IsRunning ? process!.Id : null;
    public int? Port { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public bool IsRunning => process is { HasExited: false };
    public Uri? Url => Port is int port ? new Uri($"http://127.0.0.1:{port}") : null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        webRoot = FindWebRoot();
        var cliPath = Path.Combine(webRoot, "bin", "pi-web.js");
        if (!Directory.Exists(Path.Combine(webRoot, ".next")))
        {
            throw new InvalidOperationException("Pi Web build artifacts were not found. Run npm run build before using the launcher.");
        }

        var nodePath = FindExecutableOnPath("node.exe")
            ?? throw new InvalidOperationException("Node.js was not found on PATH.");
        launcherToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Port = ReserveLoopbackPort();
        StartedAt = DateTimeOffset.UtcNow;
        var logDirectory = Path.Combine(webRoot, "logs");
        Directory.CreateDirectory(logDirectory);
        serviceLogPath = Path.Combine(logDirectory, "launcher-service.log");
        File.WriteAllText(serviceLogPath, "");

        job = new WindowsJobObject();
        process = job.StartProcess(
            nodePath,
            $"\"{cliPath}\" --port {Port} --hostname 127.0.0.1 --no-open",
            webRoot,
            new Dictionary<string, string?>
            {
                ["PI_WEB_LAUNCHER_TOKEN"] = launcherToken,
                ["PI_WEB_STARTED_AT"] = StartedAt.Value.ToString("O"),
                ["PI_WEB_NO_OPEN"] = "1",
                ["PI_WEB_LOG_PATH"] = serviceLogPath,
            });

        try
        {
            await WaitUntilHealthyAsync(cancellationToken);
        }
        catch
        {
            job.Terminate();
            ResetProcessState();
            throw;
        }
    }

    public async Task<HealthSnapshot?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || Url is null) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            return await httpClient.GetFromJsonAsync<HealthSnapshot>(new Uri(Url, "/api/health"), timeout.Token);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    public async Task<LauncherStatusSnapshot?> GetLauncherStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || Url is null || launcherToken is null) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Url, "/api/launcher/status"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", launcherToken);
            using var response = await httpClient.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<LauncherStatusSnapshot>(timeout.Token);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    /// <summary>
    /// Restart the service after an in-place update. The Node process has
    /// already exited itself (it wrote a restart marker then process.exit); we
    /// only need to drop the stale Job/process references and start fresh on a
    /// new port. The marker is consumed separately via TryConsumeRestartMarker.
    /// </summary>
    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        // Terminate is harmless for an already-exited process; disposing the job
        // closes our handle. webRoot is intentionally kept so the marker path and
        // build artifacts remain resolvable across the restart.
        job?.Terminate();
        ResetProcessState();
        await StartAsync(cancellationToken);
    }

    /// <summary>
    /// Read and delete the restart marker written by the web service before it
    /// exited. Consuming (deleting) it here guarantees a failed restart cannot
    /// trigger a restart loop. Returns false when no marker exists (e.g. a plain
    /// crash), in which case the launcher must NOT auto-restart.
    /// </summary>
    public bool TryConsumeRestartMarker(out RestartMarker? marker)
    {
        marker = null;
        if (webRoot is null) return false;
        var path = Path.Combine(webRoot, "logs", "pix-restart.json");
        if (!File.Exists(path)) return false;
        try
        {
            var json = File.ReadAllText(path);
            marker = JsonSerializer.Deserialize<RestartMarker>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            marker = null;
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
        return marker is not null;
    }

    /// <summary>
    /// Run bin/apply-update.js to swap staged app files into the web root (or to
    /// restore from a backup when <paramref name="restore"/> is true). Called by
    /// the launcher while the Node service is stopped, before RestartAsync. A
    /// non-zero exit throws so the caller can fall back to a rollback.
    /// </summary>
    public void ApplyAppUpdate(AppUpdatePayload payload, bool restore = false)
    {
        if (webRoot is null) throw new InvalidOperationException("Web root is not initialized.");
        var nodePath = FindExecutableOnPath("node.exe")
            ?? throw new InvalidOperationException("Node.js was not found on PATH.");
        var script = Path.Combine(webRoot, "bin", "apply-update.js");
        var source = restore ? payload.BackupDir : payload.StagedDir;
        var arguments = restore
            ? $"\"{script}\" \"{source}\" --no-cleanup"
            : $"\"{script}\" \"{source}\"";

        var startInfo = new ProcessStartInfo(nodePath, arguments)
        {
            WorkingDirectory = webRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var output = new StringBuilder();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start apply-update.js");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string tail;
            lock (output)
            {
                tail = string.Join(Environment.NewLine, output.ToString().Split('\n').TakeLast(12));
            }
            throw new InvalidOperationException(
                $"apply-update.js exited with {process.ExitCode}:{Environment.NewLine}{tail}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            ResetProcessState();
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Url!, "/api/launcher/shutdown"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", launcherToken);
            using var response = await httpClient.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            await process!.WaitForExitAsync(timeout.Token);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // The Job Object is the bounded fallback when graceful cleanup or
            // the Node parent process does not finish within the deadline.
            job?.Terminate();
        }
        finally
        {
            ResetProcessState();
        }
    }

    public void Dispose()
    {
        job?.Terminate();
        ResetProcessState();
        httpClient.Dispose();
    }

    private async Task WaitUntilHealthyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process!.HasExited)
            {
                throw CreateStartupException("Pi Web exited during startup.");
            }

            if (await GetHealthAsync(cancellationToken) is { Status: "ok" }) return;
            await Task.Delay(250, cancellationToken);
        }

        throw CreateStartupException("Pi Web did not become healthy within 30 seconds.");
    }

    private InvalidOperationException CreateStartupException(string reason)
    {
        var details = ReadServiceLogTail();
        return new InvalidOperationException(string.IsNullOrWhiteSpace(details)
            ? reason
            : $"{reason}{Environment.NewLine}{Environment.NewLine}{details}");
    }

    private string ReadServiceLogTail()
    {
        if (serviceLogPath is null || !File.Exists(serviceLogPath)) return "";
        try
        {
            return string.Join(Environment.NewLine, File.ReadLines(serviceLogPath).TakeLast(12));
        }
        catch
        {
            return "";
        }
    }

    private void ResetProcessState()
    {
        process?.Dispose();
        process = null;
        job?.Dispose();
        job = null;
        launcherToken = null;
        serviceLogPath = null;
        Port = null;
        StartedAt = null;
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindWebRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("PI_WEB_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) && IsWebRoot(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot);
        }

        foreach (var startingPoint in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(startingPoint);
            while (directory is not null)
            {
                if (IsWebRoot(directory.FullName)) return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Pi Web installation was not found. Set PI_WEB_ROOT to its directory.");
    }

    private static bool IsWebRoot(string path) =>
        File.Exists(Path.Combine(path, "package.json"))
        && File.Exists(Path.Combine(path, "bin", "pi-web.js"));

    private static string? FindExecutableOnPath(string executableName)
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => Path.Combine(path.Trim('"'), executableName))
            .FirstOrDefault(File.Exists);
    }
}
