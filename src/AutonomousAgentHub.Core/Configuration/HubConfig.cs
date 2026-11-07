using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutonomousAgentHub.Core.Configuration;

/// <summary>
/// Root configuration model loaded from <c>config/repositories.json</c> and environment overrides.
/// </summary>
public sealed class HubConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    [JsonPropertyName("hubName")]
    public string HubName { get; init; } = "AutonomousAgentHub";

    [JsonPropertyName("defaultModel")]
    public string DefaultModel { get; init; } = "gemini-2.5-flash";

    [JsonPropertyName("fallbackModel")]
    public string FallbackModel { get; init; } = "gemini-2.5-flash";

    [JsonPropertyName("interStageDelaySeconds")]
    public int InterStageDelaySeconds { get; init; } = 6;

    [JsonPropertyName("minimumAuditScore")]
    public int MinimumAuditScore { get; init; } = 85;

    [JsonPropertyName("maxSelfHealingAttempts")]
    public int MaxSelfHealingAttempts { get; init; } = 3;

    [JsonPropertyName("workspaceRootPath")]
    public string WorkspaceRootPath { get; init; } = "/tmp/workspace";

    [JsonPropertyName("repositories")]
    public List<RepositoryEntry> Repositories { get; init; } = [];

    /// <summary>
    /// Loads the hub configuration from a JSON file path. Supports both object wrapper and top-level array formats.
    /// </summary>
    public static async Task<HubConfig> LoadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Hub configuration file was not found at '{configPath}'.", configPath);
        }

        string rawJson = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        return Parse(rawJson);
    }

    /// <summary>
    /// Parses a JSON string into a <see cref="HubConfig"/> instance.
    /// </summary>
    public static HubConfig Parse(string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);

        string trimmed = rawJson.TrimStart();
        if (trimmed.StartsWith('['))
        {
            List<RepositoryEntry>? entries = JsonSerializer.Deserialize<List<RepositoryEntry>>(rawJson, SerializerOptions);
            return new HubConfig
            {
                Repositories = entries ?? []
            };
        }

        HubConfig? config = JsonSerializer.Deserialize<HubConfig>(rawJson, SerializerOptions);
        return config ?? new HubConfig();
    }
}

