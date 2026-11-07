using System.Text.Json.Serialization;

namespace AutonomousAgentHub.Core.Contracts;

/// <summary>
/// Safe, atomic improvement plan formulated by <c>Stage3PlannerAgent</c>.
/// </summary>
public sealed class Stage3PlannerArtifact
{
    [JsonPropertyName("taskTitle")]
    public string TaskTitle { get; init; } = string.Empty;

    [JsonPropertyName("taskSlug")]
    public string TaskSlug { get; init; } = string.Empty;

    [JsonPropertyName("improvementCategory")]
    public string ImprovementCategory { get; init; } = string.Empty;

    [JsonPropertyName("engineeringAndBusinessRationale")]
    public string EngineeringAndBusinessRationale { get; init; } = string.Empty;

    [JsonPropertyName("linkedEvidenceIds")]
    public List<string> LinkedEvidenceIds { get; init; } = [];

    [JsonPropertyName("primaryEvidenceBlock")]
    public EvidenceBlock? PrimaryEvidenceBlock { get; init; }

    [JsonPropertyName("targetFiles")]
    public List<PlannedFileChange> TargetFiles { get; init; } = [];

    [JsonPropertyName("acceptanceCriteria")]
    public List<string> AcceptanceCriteria { get; init; } = [];

    [JsonPropertyName("regressionTestStrategy")]
    public string RegressionTestStrategy { get; init; } = string.Empty;

    [JsonPropertyName("invariantsPreserved")]
    public List<string> InvariantsPreserved { get; init; } = [];
}

/// <summary>
/// Describes a single file to be modified or created as part of the Stage 3 plan.
/// </summary>
public sealed class PlannedFileChange
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("operation")]
    public string Operation { get; init; } = "Modify";

    [JsonPropertyName("changeDescription")]
    public string ChangeDescription { get; init; } = string.Empty;

    [JsonPropertyName("isTestFile")]
    public bool IsTestFile { get; init; }
}

