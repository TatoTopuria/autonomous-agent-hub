using System.Text.Json.Serialization;

namespace AutonomousAgentHub.Core.Configuration;

/// <summary>
/// Represents a single allowlisted target repository in <c>config/repositories.json</c>.
/// </summary>
public sealed class RepositoryEntry
{
    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("defaultBranch")]
    public string DefaultBranch { get; init; } = "main";

    [JsonPropertyName("primaryStack")]
    public string? PrimaryStack { get; init; }

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = "General Software Engineering";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("customBuildCommand")]
    public string? CustomBuildCommand { get; init; }

    [JsonPropertyName("customTestCommand")]
    public string? CustomTestCommand { get; init; }

    [JsonPropertyName("whitelistedAuthorities")]
    public List<string> WhitelistedAuthorities { get; init; } = [];

    [JsonIgnore]
    public string FullName => $"{Owner}/{Name}";

    [JsonIgnore]
    public string LedgerFileSlug => $"{Owner}__{Name}";

    [JsonIgnore]
    public string WorkspaceFolderName => $"{Owner}_{Name}";

    [JsonIgnore]
    public string CloneUrl => $"https://github.com/{Owner}/{Name}.git";
}

