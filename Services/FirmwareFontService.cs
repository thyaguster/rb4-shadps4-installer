namespace RB4ShadPS4Installer.Services;

internal sealed record FirmwareFontStatus(
    string Directory,
    int FontFiles,
    int Font2Files)
{
    public int TotalFiles => FontFiles + Font2Files;
    public bool Complete => FontFiles > 0 && Font2Files > 0;
}

internal sealed record FirmwareFontImportPreview(
    string? FontDirectory,
    int FontFiles,
    string? Font2Directory,
    int Font2Files)
{
    public int TotalFiles => FontFiles + Font2Files;
}

internal sealed record FirmwareFontImportResult(int ImportedFontFiles, int ImportedFont2Files)
{
    public int TotalImported => ImportedFontFiles + ImportedFont2Files;
}

internal static class FirmwareFontService
{
    public const string OfficialGuideUrl =
        "https://github.com/shadps4-emu/shadPS4/wiki/I.-Quick-start-%5BUsers%5D#6-dumping-firmware-fonts";

    public static FirmwareFontStatus GetStatus(string userDirectory)
    {
        var root = Path.Combine(userDirectory, "fonts");
        return new FirmwareFontStatus(root,
            CountFiles(Path.Combine(root, "font")),
            CountFiles(Path.Combine(root, "font2")));
    }

    public static FirmwareFontImportPreview Inspect(string selectedDirectory)
    {
        var selectedName = new DirectoryInfo(selectedDirectory).Name;
        string? font = null;
        string? font2 = null;
        if (selectedName.Equals("font", StringComparison.OrdinalIgnoreCase)) font = selectedDirectory;
        else if (selectedName.Equals("font2", StringComparison.OrdinalIgnoreCase)) font2 = selectedDirectory;
        else
        {
            font = FindNamedDirectory(selectedDirectory, "font");
            font2 = FindNamedDirectory(selectedDirectory, "font2");
        }
        var fontFiles = font is null ? 0 : CountFiles(font);
        var font2Files = font2 is null ? 0 : CountFiles(font2);
        return new FirmwareFontImportPreview(
            fontFiles > 0 ? font : null, fontFiles,
            font2Files > 0 ? font2 : null, font2Files);
    }

    public static FirmwareFontImportResult Import(string selectedDirectory, string userDirectory)
    {
        var preview = Inspect(selectedDirectory);
        if (preview.TotalFiles == 0)
            throw new InvalidDataException("No populated font or font2 folder was found.");
        var destination = Path.Combine(userDirectory, "fonts");
        var fontImported = preview.FontDirectory is null
            ? 0 : CopyTree(preview.FontDirectory, Path.Combine(destination, "font"));
        var font2Imported = preview.Font2Directory is null
            ? 0 : CopyTree(preview.Font2Directory, Path.Combine(destination, "font2"));
        return new FirmwareFontImportResult(fontImported, font2Imported);
    }

    private static string? FindNamedDirectory(string root, string name)
    {
        var direct = Path.Combine(root, name);
        if (Directory.Exists(direct)) return direct;
        try
        {
            return Directory.EnumerateDirectories(root, name, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            }).FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static int CopyTree(string source, string destination)
    {
        var imported = 0;
        foreach (var file in EnumerateFiles(source))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!Path.GetFullPath(file).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(file, target, overwrite: true);
            imported++;
        }
        return imported;
    }

    private static int CountFiles(string directory) => EnumerateFiles(directory).Count;

    private static IReadOnlyList<string> EnumerateFiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];
        try
        {
            return Directory.EnumerateFiles(directory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            }).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
