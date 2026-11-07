using System.Text.Json;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class ManagedEmulatorProfile
{
    private const string ProfileMarker = ".rb4-installer-profile.json";

    public static ShadPs4Profile Detect(string applicationDirectory) =>
        IsLlysixRb4(applicationDirectory) ? ShadPs4Profile.LlysixRb4 : ShadPs4Profile.Standard;

    public static bool IsLlysixRb4(string applicationDirectory)
    {
        if (string.IsNullOrWhiteSpace(applicationDirectory)) return false;
        try
        {
            var managedRoot = Path.GetFullPath(OfficialDownloads.Rb4ShadPs4Directory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var selected = Path.GetFullPath(applicationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (selected.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase)) return true;

            for (var directory = new DirectoryInfo(applicationDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, ".rb4-shadps4-build.json"))) return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return false;
    }

    public static string GetPortableUserDirectory(string applicationDirectory)
    {
        _ = FindBundledEmulator(applicationDirectory)
            ?? throw new FileNotFoundException("The managed LlysiX package does not contain emulator\\shadPS4.exe.");
        // The RB4 fork resolves its portable folder from the launcher's working
        // directory (std::filesystem::current_path), not the emulator EXE folder.
        return Path.Combine(applicationDirectory, "user");
    }

    public static async Task<ProfileMigrationResult> PreparePortableUserAsync(
        string applicationDirectory,
        string? existingUserDirectory,
        IProgress<InstallProgress>? progress,
        CancellationToken token)
    {
        var portableUser = GetPortableUserDirectory(applicationDirectory);
        Directory.CreateDirectory(portableUser);
        var marker = Path.Combine(portableUser, ProfileMarker);
        if (File.Exists(marker)) return new ProfileMigrationResult(portableUser, false);
        var migrated = false;

        if (!File.Exists(marker) && !string.IsNullOrWhiteSpace(existingUserDirectory) &&
            Directory.Exists(existingUserDirectory) &&
            !Path.GetFullPath(existingUserDirectory).Equals(Path.GetFullPath(portableUser), StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new InstallProgress(98, "Migrating shadPS4 settings and saves into the isolated RB4 profile"));
            await Task.Run(() => MigrateUserData(existingUserDirectory, portableUser, token), token);
            migrated = true;
        }

        await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(new
        {
            profile = "LlysiX RB4",
            createdAtUtc = DateTimeOffset.UtcNow,
            migratedFrom = migrated ? existingUserDirectory : null
        }, new JsonSerializerOptions { WriteIndented = true }), token);
        return new ProfileMigrationResult(portableUser, migrated);
    }

    private static string? FindBundledEmulator(string applicationDirectory)
    {
        if (!Directory.Exists(applicationDirectory)) return null;
        var preferred = Path.Combine(applicationDirectory, "emulator", "shadPS4.exe");
        if (File.Exists(preferred)) return preferred;
        return Directory.EnumerateFiles(applicationDirectory, "shadPS4.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => !Path.GetFileName(path).Contains("QtLauncher", StringComparison.OrdinalIgnoreCase));
    }

    private static void MigrateUserData(string source, string destination, CancellationToken token)
    {
        foreach (var file in new[] { "config.json", "users.json", "keys.json", "imgui.ini", "play_time.txt" })
            CopyFileIfPresent(Path.Combine(source, file), Path.Combine(destination, file));

        foreach (var directory in new[]
                 {
                     "home", "sys_modules", "custom_modules", "input_config", "game_data",
                     "licenses", "patches", "cheats", "custom_configs", "custom_trophy"
                 })
            CopyDirectoryIfPresent(Path.Combine(source, directory), Path.Combine(destination, directory), token);

        var sourceData = Path.Combine(source, "data");
        var rootGoldHen = Path.Combine(sourceData, "GoldHEN");
        if (Directory.Exists(rootGoldHen))
            CopyDirectoryIfPresent(rootGoldHen, Path.Combine(destination, "data", "GoldHEN"), token);

        foreach (var titleId in new[] { "CUSA02084", "CUSA02901" })
        {
            var titleGoldHen = Path.Combine(sourceData, titleId, "GoldHEN");
            if (Directory.Exists(titleGoldHen))
                CopyDirectoryIfPresent(titleGoldHen, Path.Combine(destination, "data", "GoldHEN"), token);
        }
    }

    private static void CopyFileIfPresent(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }

    private static void CopyDirectoryIfPresent(string source, string destination, CancellationToken token)
    {
        if (!Directory.Exists(source)) return;
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var destinationFile = Path.Combine(destination, Path.GetRelativePath(source, sourceFile));
            if (File.Exists(destinationFile)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: false);
        }
    }
}

internal sealed record ProfileMigrationResult(string UserDirectory, bool Migrated);
