using System.Text.RegularExpressions;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static partial class InstallationHealthService
{
    public static InstallationHealthReport Run(
        string applicationDirectory,
        string userDirectory,
        string? gameLibrary,
        string addonDirectory,
        string preferredTitleId)
    {
        var results = new List<HealthCheckResult>();
        var launcher = ShadPs4Locator.FindLauncherExecutable(applicationDirectory);
        results.Add(launcher is null
            ? Error("Emulator application", "No shadPS4 launcher or emulator executable was found.", "Choose or download a valid shadPS4 installation in Settings.")
            : Pass("Emulator application", Path.GetFileName(launcher) + " found at " + Path.GetDirectoryName(launcher)));

        var managedRb4 = launcher is not null && ManagedEmulatorProfile.IsLlysixRb4(applicationDirectory);
        results.Add(managedRb4
            ? Pass("RB4-compatible emulator", "The LlysiX RB4 layout was detected.")
            : Warn("RB4-compatible emulator", "The selected emulator is not recognized as the managed LlysiX RB4 build.", "Use the LlysiX RB4 download in Settings for the tested shader and instrument changes."));

        if (managedRb4)
        {
            try
            {
                var expectedUser = Path.GetFullPath(ManagedEmulatorProfile.GetPortableUserDirectory(applicationDirectory));
                var selectedUser = Path.GetFullPath(userDirectory);
                results.Add(string.Equals(expectedUser, selectedUser, StringComparison.OrdinalIgnoreCase)
                    ? Pass("Portable user profile", "The selected user folder matches the launcher's portable profile.")
                    : Error("Portable user profile", "The selected user folder does not match the LlysiX launcher's portable user folder.", "Return to Settings and select the managed profile before installing content."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(Error("Portable user profile", ex.Message, "Re-download or reselect the managed LlysiX build."));
            }
        }

        results.Add(File.Exists(Path.Combine(userDirectory, "config.json"))
            ? Pass("shadPS4 configuration", "config.json is present in the selected user folder.")
            : Warn("shadPS4 configuration", "config.json was not found in the selected user folder.", "Run the selected shadPS4 launcher once, then rerun this check."));

        var games = ShadPs4Locator.FindInstalledGames(userDirectory, gameLibrary);
        var game = games.FirstOrDefault(value => value.TitleId == preferredTitleId) ?? games.FirstOrDefault();
        if (game is null)
        {
            results.Add(Error("Rock Band 4 installation", "No supported USA or EU Rock Band 4 installation was detected.", "Check the game-library path or install the base game."));
        }
        else
        {
            var gamePath = Path.Combine(game.GameLibrary, game.TitleId);
            results.Add(Pass("Rock Band 4 installation", $"{game.TitleId} detected at {gamePath}."));
            results.Add(string.Equals(game.Version, "2.21", StringComparison.OrdinalIgnoreCase)
                ? Pass("Game update", "Rock Band 4 version 2.21 is installed.")
                : Warn("Game update", string.IsNullOrWhiteSpace(game.Version) ? "The installed game version could not be read." : $"Installed version is {game.Version}; RB4DX expects 2.21.", "Install the matching 2.21 update for this region."));
            results.Add(game.DeluxeInstalled
                ? Pass("Rock Band 4 Deluxe", "RB4DX data and its plugin were both detected.")
                : Warn("Rock Band 4 Deluxe", "RB4DX data or its plugin is missing.", "Install or repair RB4 Deluxe before importing custom songs."));
            results.Add(new HealthCheckResult(HealthCheckStatus.Information, "Installed content",
                game.AddonCount == 1 ? "1 add-on folder is installed." : $"{game.AddonCount:N0} add-on folders are installed."));
        }

        var moduleStatus = FirmwareModuleService.GetStatus(userDirectory);
        results.Add(moduleStatus.SupportedInstalled > 0
            ? Pass("Firmware modules", $"{moduleStatus.SupportedInstalled:N0} of {moduleStatus.SupportedTotal:N0} recognized modules are installed" +
                                       (moduleStatus.AdditionalInstalled > 0 ? $" plus {moduleStatus.AdditionalInstalled:N0} additional module(s)." : "."))
            : Warn("Firmware modules", "No recognized PS4 firmware modules are installed.", "Import decrypted modules dumped from a PS4 you own in Settings."));

        var font = CountFiles(Path.Combine(userDirectory, "fonts", "font"));
        var font2 = CountFiles(Path.Combine(userDirectory, "fonts", "font2"));
        results.Add(font + font2 > 0
            ? Pass("System fonts", $"Found {font:N0} font file(s) and {font2:N0} font2 file(s).")
            : Warn("System fonts", "No dumped PS4 system fonts were found.", "Import your dumped font and font2 folders from Settings."));

        results.Add(Directory.Exists(addonDirectory)
            ? Pass("Additional-content path", addonDirectory)
            : Warn("Additional-content path", "The configured add-on directory does not exist yet: " + addonDirectory, "It will be created during the first content installation, or can be changed in Settings."));

        AddFreeSpaceResult(results, gameLibrary);
        AddLogResults(results, userDirectory);
        return new InstallationHealthReport(DateTimeOffset.Now, results);
    }

    public static string Format(InstallationHealthReport report)
    {
        var lines = new List<string>
        {
            $"RB4 shadPS4 Installer health report — {report.CheckedAt:yyyy-MM-dd HH:mm:ss zzz}",
            $"Passed: {report.Passed}  Warnings: {report.Warnings}  Errors: {report.Errors}",
            string.Empty
        };
        foreach (var result in report.Results)
        {
            lines.Add($"[{result.Status}] {result.Title}");
            lines.Add(result.Detail);
            if (!string.IsNullOrWhiteSpace(result.Recommendation)) lines.Add("Next: " + result.Recommendation);
            lines.Add(string.Empty);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddFreeSpaceResult(List<HealthCheckResult> results, string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root)) return;
            var drive = new DriveInfo(root);
            var gib = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            results.Add(gib >= 10
                ? Pass("Free disk space", $"{gib:N1} GiB is available on {root}.")
                : Warn("Free disk space", $"Only {gib:N1} GiB is available on {root}.", "Free space before extracting large game, update, or content packages."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            results.Add(Warn("Free disk space", "Could not inspect the game-library drive: " + ex.Message));
        }
    }

    private static void AddLogResults(List<HealthCheckResult> results, string userDirectory)
    {
        var log = Path.Combine(userDirectory, "log", "shad_log.txt");
        if (!File.Exists(log))
        {
            results.Add(Warn("Latest launch log", "No shadPS4 log was found in the selected profile.", "Launch Rock Band 4 once and rerun this check."));
            return;
        }

        try
        {
            string? revision = null;
            string? branch = null;
            string? gpu = null;
            var rb4dxStarted = false;
            var seriousErrors = new List<string>();
            foreach (var line in File.ReadLines(log))
            {
                revision ??= Extract(line, "Revision");
                branch ??= Extract(line, "Branch");
                if (gpu is null && (line.Contains("GPU", StringComparison.OrdinalIgnoreCase) || line.Contains("Vulkan", StringComparison.OrdinalIgnoreCase)))
                    gpu = line.Length > 220 ? line[^220..] : line;
                if (line.Contains("Module started : RB4DX-Plugin.prx", StringComparison.OrdinalIgnoreCase)) rb4dxStarted = true;
                if (line.Contains("VK_ERROR_DEVICE_LOST", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("device lost", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("<Fatal>", StringComparison.OrdinalIgnoreCase))
                    seriousErrors.Add(line.Trim());
            }
            var identity = string.Join("  •  ", new[] { branch is null ? null : "Branch " + branch, revision is null ? null : "Revision " + revision }.Where(value => value is not null));
            results.Add(branch?.Contains("rb4", StringComparison.OrdinalIgnoreCase) == true
                ? Pass("Last emulator branch", identity)
                : Warn("Last emulator branch", identity.Length > 0 ? identity : "The last log did not identify an RB4 branch.", "Launch through the managed LlysiX shortcut and check again."));
            results.Add(rb4dxStarted
                ? Pass("RB4DX plugin launch", "The latest log shows RB4DX-Plugin.prx starting successfully.")
                : Warn("RB4DX plugin launch", "The latest log does not show the RB4DX plugin starting.", "Launch RB4 once after installing Deluxe, then rerun this check."));
            results.Add(seriousErrors.Count == 0
                ? Pass("Renderer stability", "No device-loss or fatal renderer error was found in the latest log.")
                : Error("Renderer stability", $"Found {seriousErrors.Count:N0} device-loss or fatal error line(s) in the latest log.", "Open the latest log and include it in a support report."));
            if (!string.IsNullOrWhiteSpace(gpu))
                results.Add(new HealthCheckResult(HealthCheckStatus.Information, "Graphics log", gpu));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            results.Add(Warn("Latest launch log", "Could not inspect the latest log: " + ex.Message));
        }
    }

    private static string? Extract(string line, string key)
    {
        var match = KeyValueRegex().Match(line);
        return match.Success && match.Groups["key"].Value.Equals(key, StringComparison.OrdinalIgnoreCase)
            ? match.Groups["value"].Value.Trim()
            : null;
    }

    private static int CountFiles(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        try { return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count(); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static HealthCheckResult Pass(string title, string detail) => new(HealthCheckStatus.Passed, title, detail);
    private static HealthCheckResult Warn(string title, string detail, string? recommendation = null) => new(HealthCheckStatus.Warning, title, detail, recommendation);
    private static HealthCheckResult Error(string title, string detail, string? recommendation = null) => new(HealthCheckStatus.Error, title, detail, recommendation);

    [GeneratedRegex(@"\b(?<key>Revision|Branch)\s+(?<value>[^\r\n]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueRegex();
}
