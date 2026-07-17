using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pix.Launcher;

/// <summary>Runs Pi Web in a dedicated Chromium app window owned by a Job Object.</summary>
internal sealed class BrowserApp : IDisposable
{
    private WindowsJobObject? job;
    private Process? process;
    private string? profileDirectory;

    public bool IsOpen => process is { HasExited: false };

    /// <summary>Current bounds of the isolated app window, when it exists and is not minimized.</summary>
    public bool TryGetWindowBounds(out Rectangle bounds)
    {
        bounds = default;
        if (process is not { HasExited: false }) return false;
        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle == IntPtr.Zero || IsIconic(handle)) return false;
        if (!GetWindowRect(handle, out var rect)) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return false;
        bounds = new Rectangle(rect.Left, rect.Top, width, height);
        return true;
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

        // A persistent dedicated profile keeps extensions and settings across
        // launches, so policy-injected extensions only run their first-run
        // behavior once instead of on every start. It stays separate from the
        // user's everyday browser profile, which keeps this process owned by
        // the launcher's Job Object.
        profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pix",
            "BrowserProfile");
        Directory.CreateDirectory(profileDirectory);

        job = new WindowsJobObject();
        process = job.StartProcess(
            browser,
            $"--app=\"{url}\" --user-data-dir=\"{profileDirectory}\" --no-first-run --no-default-browser-check --hide-crash-restore-bubble",
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
        profileDirectory = null;
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
