namespace RB4ShadPS4Installer.Models;

public sealed record AddonQueueItem(
    string Source,
    string DisplayName,
    string? DetectedContentId,
    bool IsInstalled,
    bool MetadataVerified)
{
    public string Details => IsInstalled
        ? $"Already installed{FormatContentId()}"
        : MetadataVerified
            ? $"Ready to install{FormatContentId()}"
            : "Content ID will be verified after PKG extraction";

    public string Status => IsInstalled ? "INSTALLED" : MetadataVerified ? "READY" : "PKG";
    public string StatusColor => IsInstalled ? "#E7B36C" : MetadataVerified ? "#43D17A" : "#73A7FF";

    private string FormatContentId() => string.IsNullOrWhiteSpace(DetectedContentId)
        ? string.Empty
        : "  •  " + DetectedContentId;
}
