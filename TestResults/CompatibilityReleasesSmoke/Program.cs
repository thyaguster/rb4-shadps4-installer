using System.Text;
using System.Text.Json;
using RB4ShadPS4Installer.Models;
using RB4ShadPS4Installer.Services;

using var releases = JsonDocument.Parse("""
[
  {"tag_name":"rb4-new","draft":false,"published_at":"2026-11-10T10:00:00Z","assets":[
    {"name":"shadPS4-RB4-win64-new.zip","browser_download_url":"https://example/new.zip","digest":"sha256:AAAA"}]},
  {"tag_name":"rb4-old","draft":false,"published_at":"2026-10-01T10:00:00Z","assets":[
    {"name":"shadPS4-RB4-win64-old.zip","browser_download_url":"https://example/old.zip","digest":"sha256:BBBB"}]},
  {"tag_name":"unverified","draft":false,"published_at":"2026-12-01T10:00:00Z","assets":[
    {"name":"shadPS4-RB4-win64-bad.zip","browser_download_url":"https://example/bad.zip","digest":null}]}
]
""");
var options = OfficialDownloads.ParseReleaseOptions(releases.RootElement, EmulatorReleaseChannel.LlysixRb4);
Assert(options.Count == 2 && options[0].Tag == "rb4-new" && options[0].IsLatest,
    "release parsing should sort by publication date and exclude unverified RB4 assets");
Assert(options.All(option => option.Digest?.StartsWith("sha256:") == true),
    "every selectable RB4 build must carry a published SHA-256 digest");
using var officialReleases = JsonDocument.Parse("""
[{"tag_name":"nightly-1","draft":false,"published_at":"2026-11-09T10:00:00Z","assets":[
  {"name":"shadPS4-win64.zip","browser_download_url":"https://example/base.zip","digest":null},
  {"name":"shadPS4-qt-win64.zip","browser_download_url":"https://example/qt.zip","digest":"sha256:CCCC"}]}]
""");
var officialOptions = OfficialDownloads.ParseReleaseOptions(officialReleases.RootElement, EmulatorReleaseChannel.Official);
Assert(officialOptions.Single().Asset == "shadPS4-qt-win64.zip",
    "official release parsing should prefer the Qt Windows asset");

var root = Path.Combine(Path.GetTempPath(), "rb4-compatibility-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    var wrong = Path.Combine(root, "wrong");
    WriteSfo(Path.Combine(wrong, "sce_sys", "param.sfo"), new()
    {
        ["TITLE_ID"] = "CUSA02901", ["CONTENT_ID"] = "EP8802-CUSA02901_00-WRONGREGION00001", ["CATEGORY"] = "ac"
    });
    var package = Path.Combine(root, "UP8802-CUSA02084_00-EXAMPLECONTENT01.pkg");
    File.WriteAllText(package, "placeholder");
    var installed = new[] { new InstalledContentItem("Example", "UP8802-CUSA02084_00-EXAMPLECONTENT01", Path.Combine(root, "installed"), null) };
    var request = new InstallRequest(InstallMode.Content, root, root, null, null, null, "extractor.exe", root,
        [package, wrong], "CUSA02084", true);
    var report = CompatibilityReportService.Analyze(request, installed, deluxeInstalled: true);
    Assert(report.Items[0].Severity == CompatibilitySeverity.Attention && report.Items[0].DuplicateState == "Already installed",
        "PKG filename evidence should identify probable duplicates without claiming verified metadata");
    Assert(report.Items[1].Severity == CompatibilitySeverity.Blocked && report.Items[1].Region == "CUSA02901",
        "verified wrong-region extracted content should be blocked");
    Console.WriteLine("Compatibility report and emulator release smoke tests passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void WriteSfo(string path, Dictionary<string, string> values)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var keys = values.Keys.ToArray();
    var keyTable = new List<byte>();
    var dataTable = new List<byte>();
    var entries = new List<(ushort KeyOffset, uint Length, uint MaxLength, uint DataOffset)>();
    foreach (var key in keys)
    {
        var keyOffset = (ushort)keyTable.Count;
        keyTable.AddRange(Encoding.UTF8.GetBytes(key)); keyTable.Add(0);
        var bytes = Encoding.UTF8.GetBytes(values[key] + "\0");
        var dataOffset = (uint)dataTable.Count;
        dataTable.AddRange(bytes);
        while (dataTable.Count % 4 != 0) dataTable.Add(0);
        entries.Add((keyOffset, (uint)bytes.Length, (uint)bytes.Length, dataOffset));
    }
    var keyStart = 20 + entries.Count * 16;
    var dataStart = keyStart + keyTable.Count;
    using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
    writer.Write(0x46535000u); writer.Write(0x00000101u); writer.Write((uint)keyStart); writer.Write((uint)dataStart); writer.Write((uint)entries.Count);
    foreach (var entry in entries) { writer.Write(entry.KeyOffset); writer.Write((ushort)0x0204); writer.Write(entry.Length); writer.Write(entry.MaxLength); writer.Write(entry.DataOffset); }
    writer.Write(keyTable.ToArray()); writer.Write(dataTable.ToArray());
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
