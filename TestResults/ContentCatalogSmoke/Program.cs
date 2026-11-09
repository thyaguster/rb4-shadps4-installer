using System.Text;
using RB4ShadPS4Installer.Models;
using RB4ShadPS4Installer.Services;

var root = Path.Combine(Path.GetTempPath(), "rb4-catalog-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    var content = Path.Combine(root, "content");
    Directory.CreateDirectory(Path.Combine(content, "nested"));
    File.WriteAllBytes(Path.Combine(content, "one.bin"), new byte[5]);
    File.WriteAllBytes(Path.Combine(content, "nested", "two.bin"), new byte[7]);
    var output = Path.Combine(root, "catalog.csv");
    var items = new[]
    {
        new InstalledContentItem("Custom: Song, \"Live\"", "  =DANGEROUS()", content, "1.23")
    };

    await InstalledContentCatalogService.ExportCsvAsync(output, "CUSA02084", items, null, CancellationToken.None);
    var bytes = File.ReadAllBytes(output);
    Assert(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "CSV must include a UTF-8 BOM");
    var csv = File.ReadAllText(output, Encoding.UTF8);
    Assert(csv.Contains("\"Custom: Song, \"\"Live\"\"\""), "commas and quotes must be escaped");
    Assert(csv.Contains("\"'  =DANGEROUS()\""), "formula-like values must be neutralized");
    Assert(csv.Contains("\"12\""), "recursive directory size must be exported");
    Assert(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any() == false, "temporary output must be removed");
    Assert(new InstalledContentItem("Custom: Song - Artist", "id", content, null).IsCustomSong,
        "single-song LibForge titles must be recognized");
    Assert(new InstalledContentItem("Custom Pack with 4 songs", "id", content, null).IsCustomSong,
        "LibForge custom-pack titles must be recognized");
    var ambiguous = new InstalledContentItem("Song - Artist", "UP8802-CUSA02084_00-EXAMPLE", content, null);
    Assert(!ambiguous.IsCustomSong && ambiguous.Kind == "DLC / unclassified",
        "publisher IDs alone must not be treated as proof of custom or official content");
    Console.WriteLine("Installed-content catalog smoke tests passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
