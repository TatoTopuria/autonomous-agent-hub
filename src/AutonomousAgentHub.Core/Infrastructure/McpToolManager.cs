using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AutonomousAgentHub.Core.Serialization;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Root configuration contract for <c>config/mcp-servers.json</c>.
/// </summary>
public sealed class McpRegistryConfig
{
    [JsonPropertyName("mcpProtocolVersion")]
    public string McpProtocolVersion { get; init; } = "2024-11-05";

    [JsonPropertyName("enforceZeroCostLocalStdioOnly")]
    public bool EnforceZeroCostLocalStdioOnly { get; init; } = true;

    [JsonPropertyName("defaultProbeTimeoutMs")]
    public int DefaultProbeTimeoutMs { get; init; } = 5000;

    [JsonPropertyName("servers")]
    public List<McpServerDescriptor> Servers { get; init; } = [];
}

/// <summary>
/// Describes a single local <c>stdio</c> Model Context Protocol (MCP) server and its stage capability permissions.
/// </summary>
public sealed class McpServerDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("packageId")]
    public string PackageId { get; init; } = string.Empty;

    [JsonPropertyName("transport")]
    public string Transport { get; init; } = "stdio";

    [JsonPropertyName("command")]
    public string Command { get; init; } = "npx";

    [JsonPropertyName("args")]
    public List<string> Args { get; init; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("zeroCostVerified")]
    public bool ZeroCostVerified { get; init; } = true;

    [JsonPropertyName("requiresWorkspaceScope")]
    public bool RequiresWorkspaceScope { get; init; } = true;

    [JsonPropertyName("allowedReadStages")]
    public List<string> AllowedReadStages { get; init; } = [];

    [JsonPropertyName("allowedWriteStages")]
    public List<string> AllowedWriteStages { get; init; } = [];

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Abstraction over the sandboxed local <c>stdio</c> Model Context Protocol (MCP) tool manager.
/// </summary>
public interface IMcpToolManager
{
    McpRegistryConfig RegistryConfig { get; }

    bool IsToolAllowedForStage(string serverName, string stageName, bool requiresWrite = false);

    string ValidateWorkspaceScopedPath(string workspacePath, string candidatePath);

    string BuildStageMcpContextSummary(string stageName, string workspacePath);

    Task<string?> TryProbeStdioServerInitializeAsync(
        string serverName,
        string workspacePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements a zero-cost, local <c>stdio</c> Model Context Protocol (MCP) capability governor and JSON-RPC 2.0 client
/// per the official MCP specification (<c>https://modelcontextprotocol.io/specification</c>).
/// Enforces per-stage read/write whitelists, workspace path confinement, and graceful native fallback.
/// </summary>
public sealed class McpToolManager : IMcpToolManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        TypeInfoResolver = HubJsonSerializerContext.Default
    };

    private readonly McpRegistryConfig _config;

    public McpToolManager(McpRegistryConfig? config = null)
    {
        _config = config ?? LoadDefaultConfig();
        ValidateSecurityPolicy(_config);
    }

    public McpRegistryConfig RegistryConfig => _config;

    /// <summary>
    /// Loads <c>config/mcp-servers.json</c> from disk and validates all servers against the zero-cost local <c>stdio</c> policy.
    /// </summary>
    public static McpRegistryConfig LoadFromFile(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);

        if (!File.Exists(configFilePath))
        {
            return CreateDefaultBuiltInConfig();
        }

        string rawJson = File.ReadAllText(configFilePath);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return CreateDefaultBuiltInConfig();
        }

        McpRegistryConfig config = JsonSerializer.Deserialize<McpRegistryConfig>(rawJson, JsonOptions)
            ?? CreateDefaultBuiltInConfig();

        ValidateSecurityPolicy(config);
        return config;
    }

    /// <summary>
    /// Verifies that every enabled MCP server uses local <c>stdio</c> transport and is verified $0-cost.
    /// </summary>
    public static void ValidateSecurityPolicy(McpRegistryConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        foreach (McpServerDescriptor server in config.Servers.Where(s => s.Enabled))
        {
            if (!string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"MCP Security Policy violation: Server '{server.Name}' specifies unsupported transport '{server.Transport}'. Only local 'stdio' transport is permitted.");
            }

            if (config.EnforceZeroCostLocalStdioOnly && !server.ZeroCostVerified)
            {
                throw new InvalidOperationException(
                    $"MCP Security Policy violation: Server '{server.Name}' is not marked zeroCostVerified=true.");
            }
        }
    }

    /// <summary>
    /// Checks whether the specified MCP server is permitted for <paramref name="stageName"/> under read-only or write mode.
    /// </summary>
    public bool IsToolAllowedForStage(string serverName, string stageName, bool requiresWrite = false)
    {
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(stageName))
        {
            return false;
        }

        McpServerDescriptor? server = _config.Servers.FirstOrDefault(s =>
            s.Enabled && string.Equals(s.Name, serverName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            return false;
        }

        if (requiresWrite)
        {
            return server.AllowedWriteStages.Any(stage =>
                stage.Equals(stageName, StringComparison.OrdinalIgnoreCase) ||
                stageName.Contains(stage, StringComparison.OrdinalIgnoreCase));
        }

        return server.AllowedReadStages.Any(stage =>
                   stage.Equals(stageName, StringComparison.OrdinalIgnoreCase) ||
                   stageName.Contains(stage, StringComparison.OrdinalIgnoreCase)) ||
               server.AllowedWriteStages.Any(stage =>
                   stage.Equals(stageName, StringComparison.OrdinalIgnoreCase) ||
                   stageName.Contains(stage, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Confines a candidate path strictly inside the isolated target workspace (<c>/tmp/workspace/{owner}_{repo}</c>).
    /// Throws <see cref="InvalidOperationException"/> on any path traversal attempt (`../` or rooted outside workspace).
    /// </summary>
    public string ValidateWorkspaceScopedPath(string workspacePath, string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        string normalizedWorkspaceRoot = Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string combined = Path.IsPathRooted(candidatePath)
            ? Path.GetFullPath(candidatePath)
            : Path.GetFullPath(Path.Combine(normalizedWorkspaceRoot, candidatePath));

        string workspacePrefixWithSlash = normalizedWorkspaceRoot + Path.DirectorySeparatorChar;
        bool isInsideWorkspace =
            string.Equals(combined, normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase) ||
            combined.StartsWith(workspacePrefixWithSlash, StringComparison.OrdinalIgnoreCase);

        if (!isInsideWorkspace)
        {
            throw new InvalidOperationException(
                $"MCP Workspace Sandbox Violation: Candidate path '{candidatePath}' resolved to '{combined}', which escapes isolated workspace '{normalizedWorkspaceRoot}'.");
        }

        return combined;
    }

    /// <summary>
    /// Builds a structured Markdown descriptor of allowed MCP capabilities and sandboxing constraints for the given stage.
    /// </summary>
    public string BuildStageMcpContextSummary(string stageName, string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);

        List<McpServerDescriptor> allowedServers =
        [
            .. _config.Servers.Where(s => s.Enabled && IsToolAllowedForStage(s.Name, stageName, requiresWrite: false))
        ];

        if (allowedServers.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new();
        sb.AppendLine($"### Active MCP Capability Governance (Protocol {_config.McpProtocolVersion}, Transport: stdio)");
        foreach (McpServerDescriptor server in allowedServers)
        {
            bool canWrite = IsToolAllowedForStage(server.Name, stageName, requiresWrite: true);
            string accessMode = canWrite ? "READ-WRITE (Workspace-Confined)" : "STRICTLY READ-ONLY";
            string scope = server.RequiresWorkspaceScope && !string.IsNullOrWhiteSpace(workspacePath)
                ? $" | Scope: `{workspacePath}`"
                : string.Empty;

            sb.AppendLine($"- **MCP `{server.Name}` (`{server.PackageId}`):** Mode=`{accessMode}`{scope} — {server.Description}");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Sends a JSON-RPC 2.0 <c>initialize</c> handshake over <c>stdio</c> to an allowed MCP server with a bounded timeout,
    /// returning the server's initialize response JSON or <c>null</c> on graceful fallback.
    /// </summary>
    public async Task<string?> TryProbeStdioServerInitializeAsync(
        string serverName,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        McpServerDescriptor? server = _config.Servers.FirstOrDefault(s =>
            s.Enabled && string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));

        if (server is null)
        {
            return null;
        }

        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(_config.DefaultProbeTimeoutMs, 500, 15000)));

            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = server.Command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string rawArg in server.Args)
            {
                string resolvedArg = rawArg.Replace("{WORKSPACE_PATH}", workspacePath, StringComparison.Ordinal);
                process.StartInfo.ArgumentList.Add(resolvedArg);
            }

            if (!process.Start())
            {
                return null;
            }

            JsonObject initRpc = BuildJsonRpcInitializeRequest(_config.McpProtocolVersion);
            await process.StandardInput.WriteLineAsync(initRpc.ToJsonString()).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            string? responseLine = await process.StandardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore best-effort process cleanup
            }

            return responseLine;
        }
        catch
        {
            // Graceful fallback to native .NET 8 BCL file/git handlers when npx or MCP server binary is absent
            return null;
        }
    }

    public static JsonObject BuildJsonRpcInitializeRequest(string protocolVersion = "2024-11-05") =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = protocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "AutonomousAgentHub",
                    ["version"] = "2.0.0"
                }
            }
        };

    private static McpRegistryConfig LoadDefaultConfig()
    {
        string current = Directory.GetCurrentDirectory();
        DirectoryInfo? dir = new(current);

        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "config", "mcp-servers.json");
            if (File.Exists(candidate))
            {
                return LoadFromFile(candidate);
            }

            dir = dir.Parent;
        }

        return CreateDefaultBuiltInConfig();
    }

    public static McpRegistryConfig CreateDefaultBuiltInConfig() =>
        new()
        {
            McpProtocolVersion = "2024-11-05",
            EnforceZeroCostLocalStdioOnly = true,
            DefaultProbeTimeoutMs = 5000,
            Servers =
            [
                new McpServerDescriptor
                {
                    Name = "filesystem",
                    PackageId = "@modelcontextprotocol/server-filesystem",
                    Transport = "stdio",
                    Command = "npx",
                    Args = ["-y", "@modelcontextprotocol/server-filesystem", "{WORKSPACE_PATH}"],
                    Enabled = true,
                    ZeroCostVerified = true,
                    RequiresWorkspaceScope = true,
                    AllowedReadStages = ["Stage1Discovery", "Stage3Planner", "Stage4Executor", "Stage6Auditor"],
                    AllowedWriteStages = ["Stage4Executor"],
                    Description = "Official MCP reference filesystem server confined strictly to the isolated target clone workspace."
                },
                new McpServerDescriptor
                {
                    Name = "git",
                    PackageId = "@modelcontextprotocol/server-git",
                    Transport = "stdio",
                    Command = "npx",
                    Args = ["-y", "@modelcontextprotocol/server-git", "--repository", "{WORKSPACE_PATH}"],
                    Enabled = true,
                    ZeroCostVerified = true,
                    RequiresWorkspaceScope = true,
                    AllowedReadStages = ["Stage1Discovery", "Stage6Auditor"],
                    AllowedWriteStages = [],
                    Description = "Official MCP reference Git server for read-only commit history and diff inspection."
                },
                new McpServerDescriptor
                {
                    Name = "sequential-thinking",
                    PackageId = "@modelcontextprotocol/server-sequential-thinking",
                    Transport = "stdio",
                    Command = "npx",
                    Args = ["-y", "@modelcontextprotocol/server-sequential-thinking"],
                    Enabled = true,
                    ZeroCostVerified = true,
                    RequiresWorkspaceScope = false,
                    AllowedReadStages = ["Stage3Planner", "Stage6Auditor"],
                    AllowedWriteStages = [],
                    Description = "Official MCP reference sequential-thinking server for structured reasoning."
                },
                new McpServerDescriptor
                {
                    Name = "memory",
                    PackageId = "@modelcontextprotocol/server-memory",
                    Transport = "stdio",
                    Command = "npx",
                    Args = ["-y", "@modelcontextprotocol/server-memory"],
                    Enabled = true,
                    ZeroCostVerified = true,
                    RequiresWorkspaceScope = false,
                    AllowedReadStages = ["Stage1Discovery", "Stage3Planner", "Stage6Auditor"],
                    AllowedWriteStages = [],
                    Description = "Official MCP reference local knowledge-graph memory server."
                }
            ]
        };
}
