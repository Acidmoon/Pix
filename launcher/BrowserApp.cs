using System.Diagnostics;
using Microsoft.Win32;

namespace Pix.Launcher;

/// <summary>Runs Pi Web in an isolated Chromium app window owned by a Job Object.</summary>
internal sealed class BrowserApp : IDisposable
{
    private WindowsJobObject? job;
    private Process? process;
    private string? profileDirectory;

    public bool IsOpen => process is { HasExited: false };

    public void Open(Uri url)
    {
        Stop();
        var browser = FindBrowser();
        if (browser is null)
        {
            // A normal browser tab is a compatibility fallback and cannot be
            // reliably closed by the launcher because of browser isolation.
            Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
            return;
        }

        profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pix",
            "BrowserProfiles",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profileDirectory);

        job = new WindowsJobObject();
        process = job.StartProcess(
            browser,
            $"--app=\"{url}\" --user-data-dir=\"{profileDirectory}\" --no-first-run --no-default-browser-check",
            Path.GetDirectoryName(browser)!);
    }

    public void Stop()
    {
        if (process is { HasExited: false })
        {
            process.CloseMainWindow();
            if (!process.WaitForExit(800)) job?.Terminate();
        }

        process?.Dispose();
        process = null;
        job?.Dispose();
        job = null;

        if (profileDirectory is not null)
        {
            try { Directory.Delete(profileDirectory, recursive: true); }
            catch { /* Chromium can briefly retain profile files after exit. */ }
            profileDirectory = null;
        }
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
}
