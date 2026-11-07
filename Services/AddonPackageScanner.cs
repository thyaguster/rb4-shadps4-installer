namespace RB4ShadPS4Installer.Services;

public static class AddonPackageScanner
{
    public static IReadOnlyList<string> FindPackages(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return [];

        return Directory.EnumerateFiles(directory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            })
            .Where(PackageExtractor.IsPackage)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
