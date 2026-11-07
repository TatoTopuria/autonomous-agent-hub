using System.Text.Json.Serialization;

namespace AutonomousAgentHub.Core.Contracts;

/// <summary>
/// Independent quality and compliance verdict produced by <c>Stage6AuditorAgent</c>.
/// </summary>
public sealed class Stage6AuditVerdictArtifact
{
    [JsonPropertyName("approved")]
    public bool Approved { get; init; }

    [JsonPropertyName("qualityScore")]
    public int QualityScore { get; init; }

    [JsonPropertyName("complianceChecklist")]
    public List<AuditChecklistItem> ComplianceChecklist { get; init; } = [];

    [JsonPropertyName("blockingIssues")]
    public List<string> BlockingIssues { get; init; } = [];

    [JsonPropertyName("executiveSummary")]
    public string ExecutiveSummary { get; init; } = string.Empty;

    [JsonPropertyName("diffLineCount")]
    public int DiffLineCount { get; init; }

    /// <summary>
    /// Determines whether the verdict meets the minimum quality threshold and has zero blocking issues.
    /// </summary>
    public bool MeetsDeploymentGate(int minimumQualityScore = 85) =>
        Approved &&
        QualityScore >= minimumQualityScore &&
        BlockingIssues.Count == 0;
}

/// <summary>
/// Represents an individual verification check in the Stage 6 Auditor checklist.
/// </summary>
public sealed class AuditChecklistItem
{
    [JsonPropertyName("criterion")]
    public string Criterion { get; init; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;
}

