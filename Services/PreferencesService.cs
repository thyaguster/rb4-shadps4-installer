using System.Text.Json;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class PreferencesService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RB4ShadPS4Installer", "settings.json");

    public static InstallerPreferences? Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<InstallerPreferences>(File.ReadAllText(SettingsPath))
                : null;
        }
        catch { return null; }
    }

    public static void Save(InstallerPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }
}
