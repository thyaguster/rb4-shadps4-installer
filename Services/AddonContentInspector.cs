using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class AddonContentInspector
{
    public static AddonQueueItem Inspect(
        string source,
        string expectedTitleId,
        IReadOnlyList<InstalledContentItem> installed)
    {
        var normalized = PathUtility.Normalize(source);
        var displayName = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (Directory.Exists(normalized))
        {
            var contentId = ReadExtractedContentId(normalized, expectedTitleId);
            return new AddonQueueItem(normalized, displayName, contentId,
                contentId is not null && IsInstalled(contentId, installed), contentId is not null);
        }

        var packageName = Path.GetFileNameWithoutExtension(normalized);
        var matched = installed
            .OrderByDescending(item => item.ContentId.Length)
            .FirstOrDefault(item => ContainsIdentity(packageName, item));
        return new AddonQueueItem(normalized, displayName, matched?.ContentId, matched is not null, MetadataVerified: false);
    }

    private static string? ReadExtractedContentId(string source, string expectedTitleId)
    {
        try
        {
            var sfoFiles = Directory.EnumerateFiles(source, "param.sfo", SearchOption.AllDirectories)
                .Where(file => string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "sce_sys", StringComparison.OrdinalIgnoreCase));
            foreach (var sfo in sfoFiles)
            {
                var values = ParamSfoReader.Read(sfo);
                if (!values.TryGetValue("TITLE_ID", out var titleId) ||
                    !titleId.Equals(expectedTitleId, StringComparison.OrdinalIgnoreCase)) continue;
                if (values.TryGetValue("CONTENT_ID", out var contentId) && !string.IsNullOrWhiteSpace(contentId))
                    return contentId.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        return null;
    }

    private static bool IsInstalled(string contentId, IReadOnlyList<InstalledContentItem> installed) =>
        installed.Any(item => IdentityEquals(contentId, item.ContentId) ||
                              IdentityEquals(contentId, Path.GetFileName(item.Folder)));

    private static bool ContainsIdentity(string packageName, InstalledContentItem item) =>
        ContainsUseful(packageName, item.ContentId) || ContainsUseful(packageName, Path.GetFileName(item.Folder));

    private static bool ContainsUseful(string text, string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity) || identity.Length < 8) return false;
        return text.Contains(identity, StringComparison.OrdinalIgnoreCase) ||
               NormalizeIdentity(text).Contains(NormalizeIdentity(identity), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IdentityEquals(string left, string right) =>
        string.Equals(NormalizeIdentity(left), NormalizeIdentity(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeIdentity(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());
}
