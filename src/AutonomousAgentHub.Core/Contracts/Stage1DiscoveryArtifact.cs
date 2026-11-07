using System.Text.Json.Serialization;

namespace AutonomousAgentHub.Core.Contracts;

/// <summary>
/// Read-only discovery output produced by <c>Stage1DiscoveryAgent</c>.
/// Captures repository topology, detected stack, architectural style, and business invariants.
/// </summary>
public sealed class Stage1DiscoveryArtifact
{
    [JsonPropertyName("repositoryFullName")]
    public string RepositoryFullName { get; init; } = string.Empty;

    [JsonPropertyName("detectedStack")]
    public string DetectedStack { get; init; } = string.Empty;

    [JsonPropertyName("domainPurpose")]
    public string DomainPurpose { get; init; } = string.Empty;

    [JsonPropertyName("architecturalStyle")]
    public string ArchitecturalStyle { get; init; } = string.Empty;

    [JsonPropertyName("dataAndControlFlows")]
    public List<string> DataAndControlFlows { get; init; } = [];

    [JsonPropertyName("criticalBusinessInvariants")]
    public List<string> CriticalBusinessInvariants { get; init; } = [];

    [JsonPropertyName("directoryTreeSummary")]
    public List<string> DirectoryTreeSummary { get; init; } = [];

    [JsonPropertyName("buildAndDependencyManifests")]
    public List<string> BuildAndDependencyManifests { get; init; } = [];

    [JsonPropertyName("keyArchitecturalEntrypoints")]
    public List<string> KeyArchitecturalEntrypoints { get; init; } = [];

    [JsonPropertyName("existingTestFiles")]
    public List<string> ExistingTestFiles { get; init; } = [];

    [JsonPropertyName("recentGitCommits")]
    public List<string> RecentGitCommits { get; init; } = [];

    [JsonPropertyName("candidateFilesForInspection")]
    public List<InspectedFileSnapshot> CandidateFilesForInspection { get; init; } = [];
}

/// <summary>
/// Represents a source or test file inspected during read-only discovery.
/// </summary>
public sealed class InspectedFileSnapshot
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("reasonForSelection")]
    public string ReasonForSelection { get; init; } = string.Empty;

    [JsonPropertyName("contentExcerpt")]
    public string ContentExcerpt { get; init; } = string.Empty;
}

