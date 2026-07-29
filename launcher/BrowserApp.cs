using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pix.Launcher;

/// <summary>Runs Pi Web in a dedicated Chromium app window owned by the launcher.</summary>
internal sealed class BrowserApp : IDisposable
{
    private WindowsJobObject? job;
    private Process? process;

    public bool IsOpen => GetOwnedProcessIds().Count > 0;

    /// <summary>Return the owned app window bounds once Chromium has created it.</summary>
    public bool TryGetWindowBounds(out Rectangle bounds)
    {
        bounds = default;
        foreach (var processId in GetOwnedProcessIds())
        {
            try
            {
                using var ownedProcess = Process.GetProcessById(processId);
                ownedProcess.Refresh();
                var handle = ownedProcess.MainWindowHandle;
                if (handle == IntPtr.Zero || IsIconic(handle)) continue;
                if (!GetWindowRect(handle, out var rect)) continue;
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0) continue;
                bounds = new Rectangle(rect.Left, rect.Top, width, height);
                return true;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Chromium processes can exit while the job is being inspected.
            }
        }
        return false;
    }

    public void Open(Uri url)
    {
        Stop();
        var browser = FindBrowser();
        if (browser is null)
        {
            // Use the system URL handler as a compatibility fallback.
            Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
            return;
        }

        // A persistent Pix-only profile keeps site state and permissions while
        // forcing Chromium to create a process tree that the launcher can own.
        // Reusing Default hands the request to the user's existing browser and
        // leaves an untracked window pointing at a dead random port on restart.
        var profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pix",
            "BrowserProfile");
        Directory.CreateDirectory(profileDirectory);

        var nextJob = new WindowsJobObject();
        try
        {
            process = nextJob.StartProcess(
                browser,
                $"--app=\"{url}\" --user-data-dir=\"{profileDirectory}\" --no-first-run --no-default-browser-check --hide-crash-restore-bubble --use-fake-ui-for-media-stream --test-type",
                Path.GetDirectoryName(browser)!);
            job = nextJob;
        }
        catch
        {
            nextJob.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        foreach (var processId in GetOwnedProcessIds())
        {
            try
            {
                using var ownedProcess = Process.GetProcessById(processId);
                if (ownedProcess.MainWindowHandle != IntPtr.Zero) ownedProcess.CloseMainWindow();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Chromium already exited or is shutting down.
            }
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(1500);
        while (IsOpen && DateTimeOffset.UtcNow < deadline) Thread.Sleep(50);
        if (IsOpen) job?.Terminate();

        process?.Dispose();
        process = null;
        job?.Dispose();
        job = null;
    }

    private IReadOnlyList<int> GetOwnedProcessIds()
    {
        try { return job?.GetProcessIds() ?? Array.Empty<int>(); }
        catch (Win32Exception) { return Array.Empty<int>(); }
    }

    public void Dispose() => Stop();

    private static string? FindBrowser()
    {
        foreach (var executableName in new[] { "msedge.exe", "chrome.exe" })
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
                if (key?.GetValue(null) is string path && File.Exists(path)) return path;
            }
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
