using System.Text.Json.Serialization;

namespace AutonomousAgentHub.Core.Contracts;

/// <summary>
/// Persisted in <c>state/rotation-state.json</c> to track daily round-robin repository selection.
/// </summary>
public sealed class RotationState
{
    [JsonPropertyName("last_processed_index")]
    public int LastProcessedIndex { get; set; } = -1;

    [JsonPropertyName("last_run_utc")]
    public DateTimeOffset? LastRunUtc { get; set; }

    [JsonPropertyName("last_repo")]
    public string? LastRepo { get; set; }

    [JsonPropertyName("total_runs")]
    public int TotalRuns { get; set; }
}

/// <summary>
/// Persisted in <c>state/ledgers/{owner}__{repo}.json</c> to prevent duplicate PR proposals
/// and maintain an audit trail of autonomous runs per target repository.
/// </summary>
public sealed class RepoStateLedger
{
    [JsonPropertyName("repositoryFullName")]
    public string RepositoryFullName { get; init; } = string.Empty;

    [JsonPropertyName("lastUpdatedUtc")]
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("completedProposals")]
    public List<LedgerProposalEntry> CompletedProposals { get; init; } = [];

    [JsonPropertyName("abortedAttempts")]
    public List<LedgerAbortedEntry> AbortedAttempts { get; init; } = [];
}

/// <summary>
/// Records a successfully verified and submitted Pull Request for deduplication in Stage 3.
/// </summary>
public sealed class LedgerProposalEntry
{
    [JsonPropertyName("taskTitle")]
    public string TaskTitle { get; init; } = string.Empty;

    [JsonPropertyName("taskSlug")]
    public string TaskSlug { get; init; } = string.Empty;

    [JsonPropertyName("branchName")]
    public string BranchName { get; init; } = string.Empty;

    [JsonPropertyName("pullRequestUrl")]
    public string PullRequestUrl { get; init; } = string.Empty;

    [JsonPropertyName("pullRequestNumber")]
    public int PullRequestNumber { get; init; }

    [JsonPropertyName("qualityScore")]
    public int QualityScore { get; init; }

    [JsonPropertyName("governingAuthority")]
    public string GoverningAuthority { get; init; } = string.Empty;

    [JsonPropertyName("modifiedFiles")]
    public List<string> ModifiedFiles { get; init; } = [];

    [JsonPropertyName("outcomeStatus")]
    public string OutcomeStatus { get; init; } = "OPEN";

    [JsonPropertyName("reviewSummary")]
    public string? ReviewSummary { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Records an aborted or rejected run so future runs avoid repeating unviable changes.
/// </summary>
public sealed class LedgerAbortedEntry
{
    [JsonPropertyName("taskTitle")]
    public string TaskTitle { get; init; } = string.Empty;

    [JsonPropertyName("stageAbortedAt")]
    public string StageAbortedAt { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("abortedAtUtc")]
    public DateTimeOffset AbortedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

