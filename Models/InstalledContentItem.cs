namespace RB4ShadPS4Installer.Models;

public sealed record InstalledContentItem(
    string Name,
    string ContentId,
    string Folder,
    string? Version)
{
    public bool IsCustomSong =>
        Name.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase) ||
        Name.StartsWith("Custom Pack", StringComparison.OrdinalIgnoreCase);
    public string Kind => IsCustomSong ? "Custom song / pack" : "DLC / unclassified";

    public string Details => string.IsNullOrWhiteSpace(Version)
        ? ContentId
        : $"{ContentId}  •  Version {Version}";
}
