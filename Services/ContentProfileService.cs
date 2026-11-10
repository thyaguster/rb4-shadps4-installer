using System.Text.Json;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer.Services;

internal sealed record ContentProfileSummary(string Name, int EnabledCount)
{
    public string Display => $"{Name}  •  {EnabledCount:N0} enabled";
}

internal sealed record ContentProfilesDocument(
    int FormatVersion,
    Dictionary<string, Dictionary<string, string[]>> Regions);

internal static class ContentProfileService
{
    private const string ProfilesFile = ".rb4-content-profiles.json";
    private const string DisabledFolder = ".rb4-disabled-content";

    public static string DisabledRoot(string addonDirectory, string titleId) =>
        Path.Combine(addonDirectory, DisabledFolder, titleId);

    public static IReadOnlyList<InstalledContentItem> FindAll(string addonDirectory, string titleId) =>
        InstalledContentScanner.Find(addonDirectory, titleId)
            .Concat(InstalledContentScanner.Find(Path.Combine(addonDirectory, DisabledFolder), titleId, enabled: false))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ContentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<ContentProfileSummary> List(string addonDirectory, string titleId)
    {
        var profiles = GetRegion(Load(addonDirectory), titleId);
        return profiles.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new ContentProfileSummary(pair.Key, pair.Value.Length)).ToArray();
    }

    public static void Save(string addonDirectory, string titleId, string name, IEnumerable<InstalledContentItem> items)
    {
        name = NormalizeName(name);
        var document = Load(addonDirectory);
        var region = GetOrCreateRegion(document, titleId);
        var existing = region.Keys.FirstOrDefault(value => value.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null && !existing.Equals(name, StringComparison.Ordinal)) region.Remove(existing);
        region[name] = items.Where(item => item.IsEnabled).Select(item => Path.GetFileName(item.Folder))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        Write(addonDirectory, document);
    }

    public static void Delete(string addonDirectory, string titleId, string name)
    {
        var document = Load(addonDirectory);
        var region = GetOrCreateRegion(document, titleId);
        var key = region.Keys.FirstOrDefault(value => value.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (key is null) return;
        region.Remove(key);
        Write(addonDirectory, document);
    }

    public static int Apply(string addonDirectory, string titleId, string name)
    {
        var region = GetRegion(Load(addonDirectory), titleId);
        var profile = region.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (string.IsNullOrWhiteSpace(profile.Key)) throw new InvalidOperationException("The selected content profile no longer exists.");
        var enabled = new HashSet<string>(profile.Value, StringComparer.OrdinalIgnoreCase);
        var all = FindAll(addonDirectory, titleId);
        return MoveTransactional(addonDirectory, titleId,
            all.Where(item => item.IsEnabled != enabled.Contains(Path.GetFileName(item.Folder))).ToArray());
    }

    public static int SetEnabled(string addonDirectory, string titleId, IReadOnlyList<InstalledContentItem> items, bool enabled) =>
        MoveTransactional(addonDirectory, titleId, items.Where(item => item.IsEnabled != enabled).ToArray());

    private static int MoveTransactional(string addonDirectory, string titleId, IReadOnlyList<InstalledContentItem> items)
    {
        var activeRoot = Path.GetFullPath(Path.Combine(addonDirectory, titleId));
        var disabledRoot = Path.GetFullPath(DisabledRoot(addonDirectory, titleId));
        var moves = new List<(string Source, string Destination)>();
        foreach (var item in items)
        {
            var source = Path.GetFullPath(item.Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var expectedParent = item.IsEnabled ? activeRoot : disabledRoot;
            if (!string.Equals(Path.GetDirectoryName(source), expectedParent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A selected content folder is outside the managed active/disabled locations.");
            var destinationRoot = item.IsEnabled ? disabledRoot : activeRoot;
            var destination = Path.Combine(destinationRoot, Path.GetFileName(source));
            if (Directory.Exists(destination))
                throw new IOException($"Content already exists in the target profile: {Path.GetFileName(source)}");
            moves.Add((source, destination));
        }

        var completed = new List<(string Source, string Destination)>();
        try
        {
            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
                Directory.Move(move.Source, move.Destination);
                completed.Add(move);
            }
            return completed.Count;
        }
        catch
        {
            foreach (var move in completed.AsEnumerable().Reverse())
                if (Directory.Exists(move.Destination) && !Directory.Exists(move.Source))
                    Directory.Move(move.Destination, move.Source);
            throw;
        }
    }

    private static string NormalizeName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60) throw new ArgumentException("Enter a profile name between 1 and 60 characters.");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("The profile name contains invalid characters.");
        return name;
    }

    private static ContentProfilesDocument Load(string addonDirectory)
    {
        var path = Path.Combine(addonDirectory, ProfilesFile);
        if (!File.Exists(path)) return new(1, new(StringComparer.OrdinalIgnoreCase));
        try
        {
            return JsonSerializer.Deserialize<ContentProfilesDocument>(File.ReadAllText(path))
                ?? new(1, new(StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException) { return new(1, new(StringComparer.OrdinalIgnoreCase)); }
    }

    private static Dictionary<string, string[]> GetRegion(ContentProfilesDocument document, string titleId) =>
        document.Regions.FirstOrDefault(pair => pair.Key.Equals(titleId, StringComparison.OrdinalIgnoreCase)).Value
        ?? new Dictionary<string, string[]>(StringComparer.CurrentCultureIgnoreCase);

    private static Dictionary<string, string[]> GetOrCreateRegion(ContentProfilesDocument document, string titleId)
    {
        var existing = document.Regions.FirstOrDefault(pair => pair.Key.Equals(titleId, StringComparison.OrdinalIgnoreCase));
        if (existing.Value is not null) return existing.Value;
        var created = new Dictionary<string, string[]>(StringComparer.CurrentCultureIgnoreCase);
        document.Regions[titleId] = created;
        return created;
    }

    private static void Write(string addonDirectory, ContentProfilesDocument document)
    {
        Directory.CreateDirectory(addonDirectory);
        var path = Path.Combine(addonDirectory, ProfilesFile);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
