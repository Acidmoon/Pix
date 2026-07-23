using System.Diagnostics;
using Microsoft.Win32;

namespace Pix.Launcher;

/// <summary>Opens Pi Web as a standalone app window in the user's browser profile.</summary>
internal sealed class BrowserApp : IDisposable
{
    private Process? launchProcess;

    // Chromium forwards a second launch into its existing browser process, so
    // this handle is not a reliable representation of the app window itself.
    public bool IsOpen => false;

    /// <summary>The user-owned browser window is intentionally not tracked.</summary>
    public bool TryGetWindowBounds(out Rectangle bounds)
    {
        bounds = default;
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

        // Deliberately omit --user-data-dir: Chromium then uses the installed
        // browser's normal user-data directory. --profile-directory=Default
        // selects the profile already used by the local Edge installation,
        // preserving extensions, login state, cookies, and site permissions.
        launchProcess = Process.Start(new ProcessStartInfo(
            browser,
            $"--app=\"{url}\" --profile-directory=Default --no-first-run --no-default-browser-check --hide-crash-restore-bubble --test-type")
        {
            UseShellExecute = false,
        });
    }

    public void Stop()
    {
        // The launch process normally hands off to an existing Chromium
        // process. Do not close it: it may share the user's browser session.
        launchProcess?.Dispose();
        launchProcess = null;
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
