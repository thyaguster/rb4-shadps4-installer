namespace RB4ShadPS4Installer.Models;

public enum ContentAuditSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ContentAuditFinding(
    ContentAuditSeverity Severity,
    string Title,
    string Details,
    string? Folder = null)
{
    public string SeverityLabel => Severity.ToString().ToUpperInvariant();
    public string SeverityColor => Severity switch
    {
        ContentAuditSeverity.Error => "#F07A8F",
        ContentAuditSeverity.Warning => "#E7B36C",
        _ => "#43D17A"
    };
}

public sealed record ContentAuditReport(
    string TitleId,
    string Root,
    int ScannedFolders,
    IReadOnlyList<ContentAuditFinding> Findings)
{
    public int ErrorCount => Findings.Count(finding => finding.Severity == ContentAuditSeverity.Error);
    public int WarningCount => Findings.Count(finding => finding.Severity == ContentAuditSeverity.Warning);
    public bool Passed => ErrorCount == 0 && WarningCount == 0;
    public string Summary => Passed
        ? $"{ScannedFolders:N0} content folder(s) checked; no issues found."
        : $"{ScannedFolders:N0} content folder(s) checked; {ErrorCount:N0} error(s) and {WarningCount:N0} warning(s).";

    public string ToPlainText()
    {
        var lines = new List<string>
        {
            $"RB4 installed-content audit — {TitleId}",
            $"Root: {Root}",
            Summary,
            string.Empty
        };
        foreach (var finding in Findings)
        {
            lines.Add($"[{finding.SeverityLabel}] {finding.Title}");
            lines.Add(finding.Details);
            if (!string.IsNullOrWhiteSpace(finding.Folder)) lines.Add("Folder: " + finding.Folder);
            lines.Add(string.Empty);
        }
        return string.Join(Environment.NewLine, lines).TrimEnd();
    }
}
