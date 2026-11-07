using System.Text.Json;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class ShadPs4Locator
{
    public static string DefaultUserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shadPS4");

    public static string InferUserDirectory(string shadPs4ApplicationDirectory)
    {
        var candidates = new List<string>
        {
            Path.Combine(shadPs4ApplicationDirectory, "user"),
            shadPs4ApplicationDirectory,
            DefaultUserDirectory
        };
        try
        {
            var emulator = Directory.Exists(shadPs4ApplicationDirectory)
                ? Directory.EnumerateFiles(shadPs4ApplicationDirectory, "shadPS4.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (emulator is not null) candidates.Insert(0, Path.Combine(Path.GetDirectoryName(emulator)!, "user"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return candidates.Where(path => !path.Equals(DefaultUserDirectory, StringComparison.OrdinalIgnoreCase))
                   .FirstOrDefault(path => File.Exists(Path.Combine(path, "config.json")))
            ?? (Directory.Exists(candidates[0]) ? candidates[0] : null)
            ?? DefaultUserDirectory;
    }

    public static string? FindLauncherDirectory(string directory)
    {
        var launcher = FindLauncherExecutable(directory);
        return launcher is null ? null : Path.GetDirectoryName(launcher);
    }

    public static string? FindLauncherExecutable(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        try
        {
            return Directory.EnumerateFiles(directory, "shadPS4QtLauncher.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.EnumerateFiles(directory, "shadPS4.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    public static IReadOnlyList<string> FindGameLibraries(string userDirectory)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configPath = Path.Combine(userDirectory, "config.json");
        if (!File.Exists(configPath)) return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            Walk(document.RootElement, found);
        }
        catch (JsonException)
        {
            // The UI will let the user choose the path manually.
        }
        return found.Where(Directory.Exists).ToArray();
    }

    public static string FindAddonDirectory(string userDirectory)
    {
        var fallback = Path.Combine(userDirectory, "addcont");
        var configPath = Path.Combine(userDirectory, "config.json");
        if (!File.Exists(configPath)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("General", out var general) &&
                general.TryGetProperty("addon_install_dir", out var addon) &&
                addon.ValueKind == JsonValueKind.String)
            {
                var value = addon.GetString();
                if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)) return value;
            }
        }
        catch (JsonException) { }
        return fallback;
    }

    public static IReadOnlyList<InstalledGame> FindInstalledGames(string userDirectory, string? preferredLibrary = null)
    {
        var libraries = new HashSet<string>(FindGameLibraries(userDirectory), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(preferredLibrary) && Directory.Exists(preferredLibrary)) libraries.Add(preferredLibrary);
        var addonDirectory = FindAddonDirectory(userDirectory);
        var games = new List<InstalledGame>();
        foreach (var library in libraries)
        {
            foreach (var titleId in new[] { "CUSA02084", "CUSA02901" })
            {
                var game = Path.Combine(library, titleId);
                if (File.Exists(Path.Combine(game, "sce_sys", "param.sfo")) && File.Exists(Path.Combine(game, "eboot.bin")))
                    games.Add(new InstalledGame(
                        titleId,
                        library,
                        IsDeluxeInstalled(userDirectory, library, titleId),
                        FindInstalledVersion(library, titleId),
                        CountInstalledAddons(addonDirectory, titleId)));
            }
        }
        return games;
    }

    private static int CountInstalledAddons(string addonDirectory, string titleId)
    {
        var titleRoot = Path.Combine(addonDirectory, titleId);
        if (!Directory.Exists(titleRoot)) return 0;
        try
        {
            return Directory.EnumerateDirectories(titleRoot, "*", SearchOption.TopDirectoryOnly)
                .Count(directory => File.Exists(Path.Combine(directory, "sce_sys", "param.sfo")) ||
                                    Directory.EnumerateFiles(directory, "param.sfo", SearchOption.AllDirectories).Any());
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static string? FindInstalledVersion(string library, string titleId)
    {
        var candidates = new[]
        {
            Path.Combine(library, titleId + "-patch", "sce_sys", "param.sfo"),
            Path.Combine(library, titleId, "sce_sys", "param.sfo")
        };

        var versions = new List<Version>();
        foreach (var sfoPath in candidates.Where(File.Exists))
        {
            try
            {
                var values = ParamSfoReader.Read(sfoPath);
                var raw = values.GetValueOrDefault("APP_VER") ?? values.GetValueOrDefault("VERSION");
                if (Version.TryParse(raw?.Trim().TrimStart('v', 'V'), out var version))
                    versions.Add(version);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidDataException) { }
        }

        return versions.Count == 0 ? null : versions.Max()!.ToString(2);
    }

    private static bool IsDeluxeInstalled(string userDirectory, string library, string titleId)
    {
        try
        {
            var deluxeRoots = new[]
            {
                Path.Combine(userDirectory, "data", "GoldHEN"),
                Path.Combine(userDirectory, "data", titleId, "GoldHEN")
            };
            var hasData = deluxeRoots.Any(deluxeData => Directory.Exists(deluxeData) &&
                Directory.EnumerateDirectories(deluxeData, "RB4DX*", SearchOption.TopDirectoryOnly).Any());
            var gameFolders = new[]
            {
                Path.Combine(library, titleId),
                Path.Combine(library, titleId + "-patch")
            };
            var hasPlugin = gameFolders.Any(folder =>
            {
                var modules = Path.Combine(folder, "sce_module");
                return Directory.Exists(modules) &&
                       Directory.EnumerateFiles(modules, "*RB4DX-Plugin.prx", SearchOption.TopDirectoryOnly).Any();
            }) || Directory.Exists(Path.Combine(userDirectory, "custom_modules", titleId)) &&
                 Directory.EnumerateFiles(Path.Combine(userDirectory, "custom_modules", titleId),
                     "*RB4DX-Plugin.prx", SearchOption.TopDirectoryOnly).Any();
            return hasData && hasPlugin;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void Walk(JsonElement element, HashSet<string> found)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("path") && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)) found.Add(value);
                }
                else Walk(property.Value, found);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) Walk(item, found);
        }
    }
}
