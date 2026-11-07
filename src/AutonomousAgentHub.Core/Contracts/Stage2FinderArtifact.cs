using System.Text.Json.Serialization;

namespace AutonomousAgentHub.Core.Contracts;

/// <summary>
/// Authoritative research output produced by <c>Stage2FinderAgent</c> using Google Search Grounding.
/// </summary>
public sealed class Stage2FinderArtifact
{
    [JsonPropertyName("repositoryFullName")]
    public string RepositoryFullName { get; init; } = string.Empty;

    [JsonPropertyName("targetStack")]
    public string TargetStack { get; init; } = string.Empty;

    [JsonPropertyName("domainFocus")]
    public string DomainFocus { get; init; } = string.Empty;

    [JsonPropertyName("searchQueriesExecuted")]
    public List<string> SearchQueriesExecuted { get; init; } = [];

    [JsonPropertyName("evidenceBlocks")]
    public List<EvidenceBlock> EvidenceBlocks { get; init; } = [];

    [JsonIgnore]
    public bool HasVerifiableEvidence =>
        EvidenceBlocks.Count > 0 &&
        EvidenceBlocks.All(block =>
            !string.IsNullOrWhiteSpace(block.IssuingAuthority) &&
            !string.IsNullOrWhiteSpace(block.DocumentTitle) &&
            !string.IsNullOrWhiteSpace(block.OfficialUrl) &&
            !string.IsNullOrWhiteSpace(block.GoverningExcerpt));
}

/// <summary>
/// Verifiable citation from a whitelisted official engineering or domain authority.
/// </summary>
public sealed class EvidenceBlock
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "EV-01";

    [JsonPropertyName("issuingAuthority")]
    public string IssuingAuthority { get; init; } = string.Empty;

    [JsonPropertyName("documentTitle")]
    public string DocumentTitle { get; init; } = string.Empty;

    [JsonPropertyName("publicationYear")]
    public int PublicationYear { get; init; } = DateTime.UtcNow.Year;

    [JsonPropertyName("officialUrl")]
    public string OfficialUrl { get; init; } = string.Empty;

    [JsonPropertyName("governingExcerpt")]
    public string GoverningExcerpt { get; init; } = string.Empty;

    [JsonPropertyName("relevanceToRepo")]
    public string RelevanceToRepo { get; init; } = string.Empty;
}

