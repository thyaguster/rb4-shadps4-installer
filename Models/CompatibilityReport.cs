namespace RB4ShadPS4Installer.Models;

public enum CompatibilitySeverity { Ready, Attention, Blocked }

public sealed record CompatibilityReportItem(
    string Label,
    string Source,
    string SourceType,
    string Region,
    string ContentType,
    string ContentId,
    string DuplicateState,
    string Requirement,
    string Confidence,
    CompatibilitySeverity Severity,
    string Finding)
{
    public string Status => Severity switch
    {
        CompatibilitySeverity.Ready => "READY",
        CompatibilitySeverity.Attention => "VERIFY",
        _ => "BLOCKED"
    };
    public string StatusColor => Severity switch
    {
        CompatibilitySeverity.Ready => "#43D17A",
        CompatibilitySeverity.Attention => "#E7B36C",
        _ => "#F07A8F"
    };
    public string Summary => $"{SourceType} • {Region} • {ContentType} • {DuplicateState}";
    public string Metadata => $"{ContentId} • {Confidence} • {Requirement}";
}

public sealed record CompatibilityReport(IReadOnlyList<CompatibilityReportItem> Items)
{
    public int Blocked => Items.Count(item => item.Severity == CompatibilitySeverity.Blocked);
    public int Attention => Items.Count(item => item.Severity == CompatibilitySeverity.Attention);
    public int Ready => Items.Count(item => item.Severity == CompatibilitySeverity.Ready);
    public string Summary => $"{Ready:N0} ready • {Attention:N0} need verification • {Blocked:N0} blocked";
}
