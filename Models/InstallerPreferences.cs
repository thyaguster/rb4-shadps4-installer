namespace RB4ShadPS4Installer.Models;

public sealed record InstallerPreferences(
    string? ShadPs4Directory,
    string? UserDirectory,
    string? GameLibrary,
    string? AddonDirectory,
    string? PackageExtractor,
    string? TitleId,
    bool MaintainLauncherShortcut = false,
    bool MaintainGameShortcut = false,
    bool AutoCheckForUpdates = true,
    string? Rb4EmulatorReleaseTag = null,
    string? OfficialEmulatorReleaseTag = null);

public sealed record InstalledGame(
    string TitleId,
    string GameLibrary,
    bool DeluxeInstalled,
    string? Version,
    int AddonCount);
