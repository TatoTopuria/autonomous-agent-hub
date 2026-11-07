using System.Text.Json.Serialization;

namespace AutonomousAgentHub.Core.Contracts;

/// <summary>
/// Persisted in <c>state/experiential-memory.json</c> to maintain a closed-loop Recursive Self-Improvement (RSI)
/// memory ledger of PR outcomes, stage failure telemetry, learned heuristics, and forbidden anti-patterns.
/// </summary>
public sealed class ExperientialMemoryState
{
    [JsonPropertyName("lastUpdatedUtc")]
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("prOutcomeMetrics")]
    public PrOutcomeSummary PrOutcomeMetrics { get; set; } = new();

    [JsonPropertyName("stageFailureCounts")]
    public Dictionary<string, int> StageFailureCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("learnedHeuristics")]
    public List<LearnedHeuristicRule> LearnedHeuristics { get; set; } = [];

    [JsonPropertyName("forbiddenAntiPatterns")]
    public List<ForbiddenAntiPatternRule> ForbiddenAntiPatterns { get; set; } = [];
}

/// <summary>
/// Aggregates tracked Pull Request outcomes across target repositories.
/// </summary>
public sealed class PrOutcomeSummary
{
    [JsonPropertyName("totalTracked")]
    public int TotalTracked { get; set; }

    [JsonPropertyName("mergedCount")]
    public int MergedCount { get; set; }

    [JsonPropertyName("closedUnmergedCount")]
    public int ClosedUnmergedCount { get; set; }

    [JsonPropertyName("openCount")]
    public int OpenCount { get; set; }
}

/// <summary>
/// Represents an actionable engineering heuristic distilled from past pipeline executions or architectural audits.
/// </summary>
public sealed class LearnedHeuristicRule
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("targetStack")]
    public string TargetStack { get; init; } = "All";

    [JsonPropertyName("applicableStages")]
    public List<string> ApplicableStages { get; init; } = [];

    [JsonPropertyName("directive")]
    public string Directive { get; init; } = string.Empty;

    [JsonPropertyName("sourceReason")]
    public string SourceReason { get; init; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents a permanent anti-pattern guardrail learned from Stage 5 build/test failures, Stage 6 rejections, or unmerged PRs.
/// </summary>
public sealed class ForbiddenAntiPatternRule
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("targetStack")]
    public string TargetStack { get; init; } = "All";

    [JsonPropertyName("patternDescription")]
    public string PatternDescription { get; init; } = string.Empty;

    [JsonPropertyName("mitigationRule")]
    public string MitigationRule { get; init; } = string.Empty;

    [JsonPropertyName("triggeredByStage")]
    public string TriggeredByStage { get; init; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
