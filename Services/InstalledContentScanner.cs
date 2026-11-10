using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal static class InstalledContentScanner
{
    public static IReadOnlyList<InstalledContentItem> Find(string addonDirectory, string titleId, bool enabled = true)
    {
        var root = Path.Combine(addonDirectory, titleId);
        if (!Directory.Exists(root)) return [];

        var items = new List<InstalledContentItem>();
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }

        foreach (var directory in directories)
        {
            var fallbackId = Path.GetFileName(directory);
            var name = fallbackId;
            var contentId = fallbackId;
            string? version = null;
            try
            {
                var sfo = Path.Combine(directory, "sce_sys", "param.sfo");
                if (!File.Exists(sfo))
                    sfo = Directory.EnumerateFiles(directory, "param.sfo", SearchOption.AllDirectories).FirstOrDefault();
                if (sfo is not null)
                {
                    var values = ParamSfoReader.Read(sfo);
                    name = FirstValue(values, "TITLE", "TITLE_00", "CONTENT_NAME") ?? fallbackId;
                    contentId = FirstValue(values, "CONTENT_ID") ?? fallbackId;
                    version = FirstValue(values, "APP_VER", "VERSION");
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidDataException) { }

            items.Add(new InstalledContentItem(name, contentId, directory, version, enabled));
        }

        return items
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ContentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FirstValue(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return null;
    }
}
