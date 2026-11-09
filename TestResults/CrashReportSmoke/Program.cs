using System.Text;
using RB4ShadPS4Installer.Services;

var root = Path.Combine(Path.GetTempPath(), "rb4-crash-report-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    for (var index = 0; index < 12; index++)
    {
        var old = Path.Combine(root, $"crash-20000101-0000{index:00}-000-1.log");
        File.WriteAllText(old, "old report");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(index - 20));
    }

    var report = CrashReportService.Create(
        new InvalidOperationException("deliberate smoke-test failure"),
        "Crash report smoke test",
        root);
    Assert(File.Exists(report), "the current crash report should be written");
    var bytes = File.ReadAllBytes(report);
    Assert(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
        "the report should be plain UTF-8 without a BOM");
    var text = File.ReadAllText(report, Encoding.UTF8);
    Assert(text.Contains("Crash report smoke test") && text.Contains("deliberate smoke-test failure"),
        "the report should include its source and exception details");
    Assert(Directory.EnumerateFiles(root, "crash-*.log").Count() == 10,
        "crash-report retention should keep the current report and nine previous reports");
    Console.WriteLine("Crash-report smoke tests passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
