using System.Buffers.Binary;
using System.Text;
using RB4ShadPS4Installer.Models;
using RB4ShadPS4Installer.Services;

var temp = Path.Combine(Path.GetTempPath(), "rb4-audit-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    var addon = Path.Combine(temp, "addcont");
    var root = Path.Combine(addon, "CUSA02084");
    WriteSfo(Path.Combine(root, "good-one", "sce_sys", "param.sfo"), new()
    {
        ["TITLE_ID"] = "CUSA02084", ["CONTENT_ID"] = "UP8802-CUSA02084_00-DUPLICATE000000", ["CATEGORY"] = "ac"
    });
    WriteSfo(Path.Combine(root, "good-two", "sce_sys", "param.sfo"), new()
    {
        ["TITLE_ID"] = "CUSA02084", ["CONTENT_ID"] = "UP8802-CUSA02084_00-DUPLICATE000000", ["CATEGORY"] = "ac"
    });
    WriteSfo(Path.Combine(root, "wrong-region", "sce_sys", "param.sfo"), new()
    {
        ["TITLE_ID"] = "CUSA02901", ["CONTENT_ID"] = "UP8802-CUSA02901_00-WRONGREGION0000", ["CATEGORY"] = "ac"
    });
    var corrupt = Path.Combine(root, "corrupt", "sce_sys", "param.sfo");
    Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
    File.WriteAllText(corrupt, "not an SFO");
    Directory.CreateDirectory(Path.Combine(root, "empty"));

    var report = ContentAuditService.Audit(addon, "CUSA02084", null, CancellationToken.None);
    Assert(report.ScannedFolders == 5, "all top-level content folders should be scanned");
    Assert(report.Findings.Any(f => f.Title == "Duplicate content ID" && f.Severity == ContentAuditSeverity.Error),
        "duplicate IDs should be errors");
    Assert(report.Findings.Any(f => f.Title == "Wrong-region content" && f.Severity == ContentAuditSeverity.Error),
        "wrong-region metadata should be an error");
    Assert(report.Findings.Any(f => f.Title == "Package metadata is unreadable"), "corrupt metadata should be reported");
    Assert(report.Findings.Any(f => f.Title == "Empty content folder"), "empty folders should be reported");
    Assert(report.Findings.Any(f => f.Title == "Package metadata is missing"), "missing metadata should be reported");
    Assert(report.ToPlainText().Contains("CUSA02084") && report.ToPlainText().Contains("Duplicate content ID"),
        "copyable report should contain its region and findings");
    var emptyLibrary = ContentAuditService.Audit(Path.Combine(temp, "unused-addcont"), "CUSA02084", null, CancellationToken.None);
    Assert(emptyLibrary.Passed && emptyLibrary.ScannedFolders == 0 && emptyLibrary.Findings.Single().Severity == ContentAuditSeverity.Info,
        "a missing per-game folder should be informational when no content is installed");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    var cancellationObserved = false;
    try { ContentAuditService.Audit(addon, "CUSA02084", null, cancelled.Token); }
    catch (OperationCanceledException) { cancellationObserved = true; }
    Assert(cancellationObserved, "a cancelled audit should stop before scanning content folders");
    Console.WriteLine("Installed-content audit smoke tests passed.");
}
finally
{
    if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
}

static void WriteSfo(string path, Dictionary<string, string> values)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var entries = values.Select(pair => (Key: Encoding.UTF8.GetBytes(pair.Key + "\0"), Data: Encoding.UTF8.GetBytes(pair.Value + "\0"))).ToArray();
    var keyOffset = 20 + entries.Length * 16;
    var keyLength = entries.Sum(entry => entry.Key.Length);
    var dataOffset = (keyOffset + keyLength + 3) & ~3;
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write(0x46535000u);
    writer.Write(0x00000101u);
    writer.Write((uint)keyOffset);
    writer.Write((uint)dataOffset);
    writer.Write((uint)entries.Length);
    var runningKey = 0;
    var runningData = 0;
    foreach (var entry in entries)
    {
        writer.Write((ushort)runningKey);
        writer.Write((ushort)0x0204);
        writer.Write((uint)entry.Data.Length);
        writer.Write((uint)entry.Data.Length);
        writer.Write((uint)runningData);
        runningKey += entry.Key.Length;
        runningData += entry.Data.Length;
    }
    foreach (var entry in entries) writer.Write(entry.Key);
    while (stream.Position < dataOffset) writer.Write((byte)0);
    foreach (var entry in entries) writer.Write(entry.Data);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
