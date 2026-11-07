using System.Text.RegularExpressions;

namespace RB4ShadPS4Installer.Services;

internal static partial class PathUtility
{
    [GeneratedRegex(@"^[A-Za-z]:[\\/](?<actual>[A-Za-z]:[\\/].+)$")]
    private static partial Regex DuplicatedDriveRegex();

    public static string Normalize(string? value)
    {
        var path = (value ?? string.Empty).Trim().Trim('"');
        while (DuplicatedDriveRegex().Match(path) is { Success: true } match)
            path = match.Groups["actual"].Value;
        return path;
    }

    public static bool IsUsableAbsolutePath(string? value)
    {
        var path = Normalize(value);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        return path.IndexOf(':', 2) < 0;
    }
}
