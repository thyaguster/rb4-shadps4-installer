namespace RB4ShadPS4Installer.Models;

public enum HealthCheckStatus
{
    Passed,
    Warning,
    Error,
    Information
}

public sealed record HealthCheckResult(
    HealthCheckStatus Status,
    string Title,
    string Detail,
    string? Recommendation = null)
{
    public string RecommendationText => string.IsNullOrWhiteSpace(Recommendation)
        ? string.Empty
        : "Next: " + Recommendation;

    public string Icon => Status switch
    {
        HealthCheckStatus.Passed => "✓",
        HealthCheckStatus.Warning => "!",
        HealthCheckStatus.Error => "×",
        _ => "i"
    };

    public string StatusColor => Status switch
    {
        HealthCheckStatus.Passed => "#43D17A",
        HealthCheckStatus.Warning => "#E7B36C",
        HealthCheckStatus.Error => "#FF5B72",
        _ => "#73A7FF"
    };
}

public sealed record InstallationHealthReport(
    DateTimeOffset CheckedAt,
    IReadOnlyList<HealthCheckResult> Results)
{
    public int Passed => Results.Count(result => result.Status == HealthCheckStatus.Passed);
    public int Warnings => Results.Count(result => result.Status == HealthCheckStatus.Warning);
    public int Errors => Results.Count(result => result.Status == HealthCheckStatus.Error);
}
