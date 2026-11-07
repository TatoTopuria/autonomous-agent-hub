using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;
using AutonomousAgentHub.Core.Pipeline;

namespace AutonomousAgentHub.Core;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            CliOptions options = ParseArguments(args);
            string hubRoot = ResolveHubRootDirectory();

            string configPath = options.ConfigPath ?? Path.Combine(hubRoot, "config", "repositories.json");
            string stateDir = options.StateDirectoryPath ?? Path.Combine(hubRoot, "state");
            string skillsDir = Path.Combine(hubRoot, "skills");
            string mcpConfigPath = Path.Combine(hubRoot, "config", "mcp-servers.json");

            HubConfig config = await HubConfig.LoadAsync(configPath, cts.Token).ConfigureAwait(false);
            StateLedgerManager stateLedgerManager = new(stateDir);
            PipelineTelemetryLogger telemetryLogger = new(stateDir);
            ExperientialMemoryManager experientialMemoryManager = new(stateDir);
            SkillRegistry skillRegistry = new(skillsDir);
            McpToolManager mcpToolManager = new(McpToolManager.LoadFromFile(mcpConfigPath));

            // Ensure experiential memory state is initialized
            _ = await experientialMemoryManager.LoadMemoryAsync(cts.Token).ConfigureAwait(false);

            if (options.IsSelfEvolveAudit)
            {
                await RunSelfEvolutionDiagnosticAuditAsync(
                    config,
                    stateLedgerManager,
                    telemetryLogger,
                    experientialMemoryManager,
                    skillRegistry,
                    mcpToolManager,
                    cts.Token).ConfigureAwait(false);
                return 0;
            }

            using HttpClient httpClient = new()
            {
                Timeout = TimeSpan.FromMinutes(3)
            };

            using GeminiFreeTierClient geminiClient = new(
                httpClient,
                primaryModel: config.DefaultModel,
                fallbackModel: config.FallbackModel,
                interStageDelaySeconds: config.InterStageDelaySeconds);

            PolyglotBuildTestRunner buildTestRunner = new();
            GitWorkspaceManager gitWorkspaceManager = new(config.WorkspaceRootPath);
            GitHubPullRequestClient prClient = new(httpClient);

            Stage1DiscoveryAgent stage1 = new(geminiClient, buildTestRunner, gitWorkspaceManager, skillRegistry, experientialMemoryManager, mcpToolManager);
            Stage2FinderAgent stage2 = new(geminiClient, skillRegistry, experientialMemoryManager);
            Stage3PlannerAgent stage3 = new(geminiClient, skillRegistry, experientialMemoryManager, mcpToolManager);
            Stage4ExecutorAgent stage4 = new(geminiClient, skillRegistry, experientialMemoryManager, mcpToolManager);
            Stage5TesterAgent stage5 = new(buildTestRunner, gitWorkspaceManager, stage4, config.MaxSelfHealingAttempts);
            Stage6AuditorAgent stage6 = new(geminiClient, gitWorkspaceManager, config.MinimumAuditScore, skillRegistry, experientialMemoryManager, mcpToolManager);
            Stage7DeployerAgent stage7 = new(gitWorkspaceManager, prClient, stateLedgerManager, config.MinimumAuditScore);

            SevenStageOrchestrator orchestrator = new(
                config,
                stateLedgerManager,
                gitWorkspaceManager,
                stage1,
                stage2,
                stage3,
                stage4,
                stage5,
                stage6,
                stage7,
                telemetryLogger,
                experientialMemoryManager,
                prClient);

            PipelineExecutionSummary summary = await orchestrator.RunAsync(
                isDryRun: options.IsDryRun,
                repositoryOverride: options.RepositoryOverride,
                cancellationToken: cts.Token).ConfigureAwait(false);

            Console.WriteLine($"[AutonomousAgentHub] Completed: {summary.StatusMessage}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AutonomousAgentHub] FATAL ERROR: {ex.Message}");
            return 1;
        }
    }

    public static CliOptions ParseArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        bool isDryRun = false;
        bool isSelfEvolveAudit = false;
        string? repoOverride = Environment.GetEnvironmentVariable("TARGET_REPOSITORY");
        string? configPath = null;
        string? stateDirPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                isDryRun = true;
            }
            else if (string.Equals(arg, "--self-evolve-audit", StringComparison.OrdinalIgnoreCase))
            {
                isSelfEvolveAudit = true;
            }
            else if (string.Equals(arg, "--repo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                repoOverride = args[++i];
            }
            else if (arg.StartsWith("--repo=", StringComparison.OrdinalIgnoreCase))
            {
                repoOverride = arg["--repo=".Length..];
            }
            else if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
            else if (string.Equals(arg, "--state-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                stateDirPath = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(repoOverride))
        {
            repoOverride = null;
        }

        return new CliOptions(isDryRun, repoOverride, configPath, stateDirPath, isSelfEvolveAudit);
    }

    private static async Task RunSelfEvolutionDiagnosticAuditAsync(
        HubConfig config,
        StateLedgerManager stateLedgerManager,
        PipelineTelemetryLogger telemetryLogger,
        ExperientialMemoryManager experientialMemoryManager,
        SkillRegistry skillRegistry,
        McpToolManager mcpToolManager,
        CancellationToken cancellationToken)
    {
        RotationState rotation = await stateLedgerManager.LoadRotationStateAsync(cancellationToken).ConfigureAwait(false);
        List<PipelineTelemetryRecord> telemetry = await telemetryLogger.ReadAllRecordsAsync(cancellationToken).ConfigureAwait(false);
        ExperientialMemoryState memory = await experientialMemoryManager.LoadMemoryAsync(cancellationToken).ConfigureAwait(false);

        int totalProposals = 0;
        int totalAborts = 0;
        foreach (RepositoryEntry repo in config.Repositories)
        {
            RepoStateLedger ledger = await stateLedgerManager.LoadRepoLedgerAsync(repo, cancellationToken).ConfigureAwait(false);
            totalProposals += ledger.CompletedProposals.Count;
            totalAborts += ledger.AbortedAttempts.Count;
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine($"[Self-Evolution Diagnostic Audit] Hub: {config.HubName}");
        Console.WriteLine($"[Self-Evolution Diagnostic Audit] Allowlisted Repositories: {config.Repositories.Count} (Enabled: {config.Repositories.Count(r => r.Enabled)})");
        Console.WriteLine($"[Self-Evolution Diagnostic Audit] Rotation State: last_processed_index={rotation.LastProcessedIndex}, total_runs={rotation.TotalRuns}");
        Console.WriteLine($"[Self-Evolution Diagnostic Audit] Telemetry Records: {telemetry.Count} | Completed Proposals: {totalProposals} | Aborted Attempts: {totalAborts}");
        Console.WriteLine($"[Self-Evolution Diagnostic Audit] Experiential Memory: LearnedHeuristics={memory.LearnedHeuristics.Count}, ForbiddenAntiPatterns={memory.ForbiddenAntiPatterns.Count}");
        Console.WriteLine($"[Self-Evolution Diagnostic Audit] Skill Registry Root: {skillRegistry.SkillsDirectoryPath}");
        Console.WriteLine($"[Self-Evolution Diagnostic Audit] Sandboxed MCP Servers ({mcpToolManager.RegistryConfig.Servers.Count}): {string.Join(", ", mcpToolManager.RegistryConfig.Servers.Select(s => s.Name))}");
        Console.WriteLine("================================================================================");
    }

    private static string ResolveHubRootDirectory()
    {
        string current = Directory.GetCurrentDirectory();
        DirectoryInfo? dir = new(current);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AutonomousAgentHub.sln")) ||
                File.Exists(Path.Combine(dir.FullName, "config", "repositories.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return current;
    }

    public sealed record CliOptions(
        bool IsDryRun,
        string? RepositoryOverride,
        string? ConfigPath,
        string? StateDirectoryPath,
        bool IsSelfEvolveAudit = false);
}
