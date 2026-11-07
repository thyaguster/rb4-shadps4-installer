using System.Text.Json;
using System.Text.Json.Nodes;

namespace RB4ShadPS4Installer.Services;

internal static class ShadPs4ManagedConfig
{
    public static void SetPaths(string userDirectory, string? gameLibrary, string? addonDirectory)
    {
        Directory.CreateDirectory(userDirectory);
        var configPath = Path.Combine(userDirectory, "config.json");
        JsonObject root;
        try
        {
            root = File.Exists(configPath)
                ? JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException)
        {
            return;
        }

        var general = root["General"] as JsonObject ?? new JsonObject();
        root["General"] = general;
        if (!string.IsNullOrWhiteSpace(gameLibrary))
        {
            general["install_dirs"] = new JsonArray(new JsonObject
            {
                ["enabled"] = true,
                ["path"] = gameLibrary
            });
        }
        if (!string.IsNullOrWhiteSpace(addonDirectory))
            general["addon_install_dir"] = addonDirectory;

        var temporary = configPath + ".rb4-installer.tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, configPath, overwrite: true);
    }
}
