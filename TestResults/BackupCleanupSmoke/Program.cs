using RB4ShadPS4Installer.Services;

var temp = Path.Combine(Path.GetTempPath(), "rb4-cleanup-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    var now = DateTimeOffset.Now;
    var usaOld = Backup(Path.Combine(temp, "usa-old"), "CUSA02084", now.AddDays(-2), 1024);
    var usaNew = Backup(Path.Combine(temp, "usa-new"), "CUSA02084", now, 2048);
    var euOnly = Backup(Path.Combine(temp, "eu-only"), "CUSA02901", now.AddDays(-1), 4096);
    var candidates = BackupService.FindCleanupCandidates([usaNew, euOnly, usaOld]);
    Assert(candidates.Count == 1 && ReferenceEquals(candidates[0], usaOld), "cleanup must retain newest backup per region");
    Assert(usaOld.StoredBytes == 1024 && usaNew.DisplayDetails.Contains("2.0 KB"), "actual stored sizes should be reported");
    Assert(BackupService.FormatBytes(0) == "0 B" && BackupService.FormatBytes(1024 * 1024) == "1.0 MB", "byte formatting should be stable");
    Console.WriteLine("Backup cleanup smoke tests passed.");
}
finally
{
    if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
}

static BackupInfo Backup(string root, string title, DateTimeOffset date, int bytes)
{
    var file = Path.Combine(root, "files", "00000000.bak");
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllBytes(file, new byte[bytes]);
    return new(root, new BackupManifest(2, title, date, "Complete",
        [new BackupEntry("destination", "files\\00000000.bak", true, bytes, new string('A', 64))]));
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
