using System.Text.Json;

namespace Pix.Launcher;

/// <summary>Persists only the harmless floating-window position.</summary>
internal static class LauncherPlacement
{
    private sealed record StoredPosition(int X, int Y);

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pix",
        "launcher.json");

    public static Point? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var position = JsonSerializer.Deserialize<StoredPosition>(File.ReadAllText(SettingsPath));
            return position is null ? null : new Point(position.X, position.Y);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(Point point)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new StoredPosition(point.X, point.Y)));
        }
        catch
        {
            // Window placement is optional and must never block shutdown.
        }
    }
}
