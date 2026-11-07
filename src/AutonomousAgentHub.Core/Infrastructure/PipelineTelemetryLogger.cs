using System.Text.Json;
using System.Text.Json.Serialization;
using AutonomousAgentHub.Core.Serialization;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Represents a single append-only JSONL telemetry record emitted after a pipeline run.
/// </summary>
public sealed class PipelineTelemetryRecord
{
    [JsonPropertyName("runId")]
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("selectedRepository")]
    public string SelectedRepository { get; init; } = string.Empty;

    [JsonPropertyName("rotationIndex")]
    public int RotationIndex { get; init; }

    [JsonPropertyName("isDryRun")]
    public bool IsDryRun { get; init; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("finalStageReached")]
    public string FinalStageReached { get; init; } = string.Empty;

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    [JsonPropertyName("selfHealingAttempts")]
    public int SelfHealingAttempts { get; init; }

    [JsonPropertyName("auditQualityScore")]
    public int? AuditQualityScore { get; init; }

    [JsonPropertyName("pullRequestUrl")]
    public string? PullRequestUrl { get; init; }

    [JsonPropertyName("statusMessage")]
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Appends structured JSON Lines (<c>.jsonl</c>) execution telemetry to <c>state/telemetry/pipeline-runs.jsonl</c>.
/// </summary>
public sealed class PipelineTelemetryLogger
{
    private readonly string _telemetryFilePath;

    public PipelineTelemetryLogger(string stateDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectoryPath);
        _telemetryFilePath = Path.Combine(stateDirectoryPath, "telemetry", "pipeline-runs.jsonl");
    }

    public string TelemetryFilePath => _telemetryFilePath;

    /// <summary>
    /// Appends a single <see cref="PipelineTelemetryRecord"/> line to the JSONL audit log.
    /// </summary>
    public async Task AppendRecordAsync(
        PipelineTelemetryRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        string? directory = Path.GetDirectoryName(_telemetryFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string line = JsonSerializer.Serialize(record, HubJsonSerializerContext.Default.PipelineTelemetryRecord);
        await File.AppendAllTextAsync(_telemetryFilePath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads all recorded telemetry entries from the JSONL file.
    /// </summary>
    public async Task<List<PipelineTelemetryRecord>> ReadAllRecordsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_telemetryFilePath))
        {
            return [];
        }

        string[] lines = await File.ReadAllLinesAsync(_telemetryFilePath, cancellationToken).ConfigureAwait(false);
        List<PipelineTelemetryRecord> records = [];

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            PipelineTelemetryRecord? item = JsonSerializer.Deserialize(
                line,
                HubJsonSerializerContext.Default.PipelineTelemetryRecord);

            if (item is not null)
            {
                records.Add(item);
            }
        }

        return records;
    }
}
