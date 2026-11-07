namespace RB4ShadPS4Installer.Models;

public enum InstallMode
{
    Standard,
    Deluxe,
    Content
}

public enum ShadPs4Profile
{
    Standard,
    LlysixRb4
}

public sealed record InstallRequest(
    InstallMode Mode,
    string UserDirectory,
    string GameLibrary,
    string? BaseGameSource,
    string? PatchSource,
    string? DeluxeSource,
    string? PackageExtractor,
    string AddonDirectory,
    IReadOnlyList<string> AddonSources,
    string TitleId,
    bool CreateBackup,
    ShadPs4Profile EmulatorProfile = ShadPs4Profile.Standard);

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record InstallProgress(double Percent, string Message);
