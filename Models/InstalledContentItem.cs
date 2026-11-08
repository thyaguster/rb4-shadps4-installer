namespace RB4ShadPS4Installer.Models;

public sealed record InstalledContentItem(
    string Name,
    string ContentId,
    string Folder,
    string? Version)
{
    public bool IsCustomSong => Name.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase);
    public string Kind => IsCustomSong ? "Custom song" : "DLC / add-on";

    public string Details => string.IsNullOrWhiteSpace(Version)
        ? ContentId
        : $"{ContentId}  •  Version {Version}";
}
