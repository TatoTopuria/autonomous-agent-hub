using System.Text.Json.Serialization;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Serialization;

/// <summary>
/// Compile-time <see cref="System.Text.Json"/> source generation context for all pipeline contracts,
/// configuration models, state ledgers, and telemetry records. Eliminates runtime reflection overhead
/// and guarantees AOT/trimming compatibility on .NET 8+.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HubConfig))]
[JsonSerializable(typeof(List<RepositoryEntry>))]
[JsonSerializable(typeof(RotationState))]
[JsonSerializable(typeof(RepoStateLedger))]
[JsonSerializable(typeof(Stage1DiscoveryArtifact))]
[JsonSerializable(typeof(Stage2FinderArtifact))]
[JsonSerializable(typeof(Stage3PlannerArtifact))]
[JsonSerializable(typeof(Stage4ExecutionArtifact))]
[JsonSerializable(typeof(Stage5TestReportArtifact))]
[JsonSerializable(typeof(Stage6AuditVerdictArtifact))]
[JsonSerializable(typeof(PipelineTelemetryRecord))]
public partial class HubJsonSerializerContext : JsonSerializerContext
{
}
