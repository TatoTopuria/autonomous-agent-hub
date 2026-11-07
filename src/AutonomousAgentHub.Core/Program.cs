using AutonomousAgentHub.Core.Configuration;
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

            HubConfig config = await HubConfig.LoadAsync(configPath, cts.Token).ConfigureAwait(false);
            StateLedgerManager stateLedgerManager = new(stateDir);
            PipelineTelemetryLogger telemetryLogger = new(stateDir);

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

            Stage1DiscoveryAgent stage1 = new(geminiClient, buildTestRunner, gitWorkspaceManager);
            Stage2FinderAgent stage2 = new(geminiClient);
            Stage3PlannerAgent stage3 = new(geminiClient);
            Stage4ExecutorAgent stage4 = new(geminiClient);
            Stage5TesterAgent stage5 = new(buildTestRunner, gitWorkspaceManager, stage4, config.MaxSelfHealingAttempts);
            Stage6AuditorAgent stage6 = new(geminiClient, gitWorkspaceManager, config.MinimumAuditScore);
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
                telemetryLogger);

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

        return new CliOptions(isDryRun, repoOverride, configPath, stateDirPath);
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
        string? StateDirectoryPath);
}

