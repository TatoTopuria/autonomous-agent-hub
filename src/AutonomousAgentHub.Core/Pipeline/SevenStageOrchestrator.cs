using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Summary outcome returned by <see cref="SevenStageOrchestrator"/> after a pipeline run.
/// </summary>
public sealed record PipelineExecutionSummary(
    bool Succeeded,
    bool IsDryRun,
    string SelectedRepository,
    int RotationIndex,
    string FinalStageReached,
    string StatusMessage,
    PullRequestCreationResult? PullRequest = null);

/// <summary>
/// Master state machine coordinating Stages 1 through 7 across polyglot target repositories.
/// </summary>
public sealed class SevenStageOrchestrator(
    HubConfig config,
    StateLedgerManager stateLedgerManager,
    IGitWorkspaceManager gitWorkspaceManager,
    Stage1DiscoveryAgent stage1Discovery,
    Stage2FinderAgent stage2Finder,
    Stage3PlannerAgent stage3Planner,
    Stage4ExecutorAgent stage4Executor,
    Stage5TesterAgent stage5Tester,
    Stage6AuditorAgent stage6Auditor,
    Stage7DeployerAgent stage7Deployer)
{
    public async Task<PipelineExecutionSummary> RunAsync(
        bool isDryRun = false,
        string? repositoryOverride = null,
        CancellationToken cancellationToken = default)
    {
        RotationState currentState = await stateLedgerManager.LoadRotationStateAsync(cancellationToken).ConfigureAwait(false);
        SelectedRepositoryTarget selectedTarget = StateLedgerManager.SelectNextRepository(
            config.Repositories,
            currentState,
            repositoryOverride);

        RepositoryEntry repo = selectedTarget.Repository;
        RepoStateLedger ledger = await stateLedgerManager.LoadRepoLedgerAsync(repo, cancellationToken).ConfigureAwait(false);

        Console.WriteLine("================================================================================");
        Console.WriteLine($"[Orchestrator] Hub: {config.HubName}");
        Console.WriteLine($"[Orchestrator] Mode: {(isDryRun ? "DRY-RUN (Read-Only Simulation)" : "PRODUCTION DAILY EVOLUTION")}");
        Console.WriteLine($"[Orchestrator] Selected Target: {repo.FullName} (Rotation Index: {selectedTarget.RotationIndex}, Stack Hint: {repo.PrimaryStack ?? "Auto"})");
        Console.WriteLine($"[Orchestrator] Previous Completed Proposals for Target: {ledger.CompletedProposals.Count}");
        Console.WriteLine("================================================================================");

        if (isDryRun)
        {
            return ExecuteDryRunVerification(selectedTarget, currentState, ledger);
        }

        // Clone target repository into isolated workspace (/tmp/workspace/{owner}_{repo})
        string workspacePath = await gitWorkspaceManager.CloneOrPrepareWorkspaceAsync(repo, cancellationToken).ConfigureAwait(false);

        // Stage 1: Discovery (Strictly Read-Only)
        Stage1DiscoveryArtifact discovery = await stage1Discovery.ExecuteAsync(
            repo,
            workspacePath,
            cancellationToken).ConfigureAwait(false);

        // Stage 2: Finder (Official Authoritative Research with Google Search Grounding)
        Stage2FinderArtifact finder = await stage2Finder.ExecuteAsync(
            repo,
            discovery,
            cancellationToken).ConfigureAwait(false);

        if (!finder.HasVerifiableEvidence)
        {
            const string reason = "Stage 2 Finder found zero verifiable sources from whitelisted official authorities. Pipeline halted cleanly.";
            Console.WriteLine($"[Orchestrator] {reason}");

            await stateLedgerManager.RecordAbortedAttemptAsync(
                repo,
                taskTitle: "Unplanned (Halted at Stage 2 Finder)",
                stageAbortedAt: "Stage2Finder",
                reason: reason,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await stateLedgerManager.AdvanceRotationStateAsync(selectedTarget, cancellationToken).ConfigureAwait(false);

            return new PipelineExecutionSummary(
                Succeeded: false,
                IsDryRun: false,
                SelectedRepository: repo.FullName,
                RotationIndex: selectedTarget.RotationIndex,
                FinalStageReached: "Stage2Finder",
                StatusMessage: reason);
        }

        // Stage 3: Planner (Safe High-Leverage Task Formulation)
        Stage3PlannerArtifact plan = await stage3Planner.ExecuteAsync(
            discovery,
            finder,
            ledger,
            cancellationToken).ConfigureAwait(false);

        // Stage 4: Executor (Initial Code & Test Implementation - Attempt 1)
        Stage4ExecutionArtifact initialExecution = await stage4Executor.ExecuteAsync(
            workspacePath,
            discovery,
            plan,
            attemptNumber: 1,
            previousFailureTrace: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Stage 5: Tester (Polyglot Build, Regression Test & 3x Self-Healing Loop)
        (Stage5TestReportArtifact testReport, Stage4ExecutionArtifact finalExecution) =
            await stage5Tester.ExecuteWithSelfHealingAsync(
                repo,
                workspacePath,
                discovery,
                plan,
                initialExecution,
                cancellationToken).ConfigureAwait(false);

        if (!testReport.Passed)
        {
            string reason = $"Stage 5 Tester failed after {testReport.TotalAttempts} self-healing attempts. Workspace hard-reset cleanly.";
            Console.WriteLine($"[Orchestrator] {reason}");

            await stateLedgerManager.RecordAbortedAttemptAsync(
                repo,
                taskTitle: plan.TaskTitle,
                stageAbortedAt: "Stage5Tester",
                reason: reason,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await stateLedgerManager.AdvanceRotationStateAsync(selectedTarget, cancellationToken).ConfigureAwait(false);

            return new PipelineExecutionSummary(
                Succeeded: false,
                IsDryRun: false,
                SelectedRepository: repo.FullName,
                RotationIndex: selectedTarget.RotationIndex,
                FinalStageReached: "Stage5Tester",
                StatusMessage: reason);
        }

        // Stage 6: Auditor (Independent Quality & Compliance Gate)
        Stage6AuditVerdictArtifact auditVerdict = await stage6Auditor.ExecuteAsync(
            workspacePath,
            discovery,
            finder,
            plan,
            finalExecution,
            testReport,
            cancellationToken).ConfigureAwait(false);

        if (!auditVerdict.MeetsDeploymentGate(config.MinimumAuditScore))
        {
            string reason =
                $"Stage 6 Auditor rejected deployment (Approved={auditVerdict.Approved}, QualityScore={auditVerdict.QualityScore}/{config.MinimumAuditScore}). " +
                $"Issues: {string.Join("; ", auditVerdict.BlockingIssues)}";
            Console.WriteLine($"[Orchestrator] {reason}");

            await gitWorkspaceManager.ResetAndCleanWorkspaceAsync(workspacePath, cancellationToken).ConfigureAwait(false);

            await stateLedgerManager.RecordAbortedAttemptAsync(
                repo,
                taskTitle: plan.TaskTitle,
                stageAbortedAt: "Stage6Auditor",
                reason: reason,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await stateLedgerManager.AdvanceRotationStateAsync(selectedTarget, cancellationToken).ConfigureAwait(false);

            return new PipelineExecutionSummary(
                Succeeded: false,
                IsDryRun: false,
                SelectedRepository: repo.FullName,
                RotationIndex: selectedTarget.RotationIndex,
                FinalStageReached: "Stage6Auditor",
                StatusMessage: reason);
        }

        // Stage 7: Deployer (Branch Push, PR Creation & State Ledger Update)
        PullRequestCreationResult prResult = await stage7Deployer.ExecuteAsync(
            selectedTarget,
            workspacePath,
            discovery,
            finder,
            plan,
            finalExecution,
            testReport,
            auditVerdict,
            cancellationToken).ConfigureAwait(false);

        return new PipelineExecutionSummary(
            Succeeded: true,
            IsDryRun: false,
            SelectedRepository: repo.FullName,
            RotationIndex: selectedTarget.RotationIndex,
            FinalStageReached: "Stage7Deployer",
            StatusMessage: $"Successfully opened Pull Request #{prResult.Number}: {prResult.HtmlUrl}",
            PullRequest: prResult);
    }

    private PipelineExecutionSummary ExecuteDryRunVerification(
        SelectedRepositoryTarget selectedTarget,
        RotationState currentState,
        RepoStateLedger ledger)
    {
        RepositoryEntry repo = selectedTarget.Repository;
        string plannedWorkspace = gitWorkspaceManager.ResolveWorkspacePath(repo);

        Console.WriteLine($"[Dry-Run] Current rotation last_processed_index: {currentState.LastProcessedIndex}");
        Console.WriteLine($"[Dry-Run] Next resolved rotation index: {selectedTarget.RotationIndex} -> '{repo.FullName}'");
        Console.WriteLine($"[Dry-Run] Target clone URL: {repo.CloneUrl} (Branch: {repo.DefaultBranch})");
        Console.WriteLine($"[Dry-Run] Isolated Workspace Path: {plannedWorkspace}");
        Console.WriteLine($"[Dry-Run] Ledger File Path: {stateLedgerManager.GetLedgerFilePath(repo)}");
        Console.WriteLine($"[Dry-Run] Enforced Whitelisted Authorities ({repo.WhitelistedAuthorities.Count}): {string.Join(", ", repo.WhitelistedAuthorities)}");
        Console.WriteLine($"[Dry-Run] Rate-Limit Pacing Delay: {config.InterStageDelaySeconds}s | Minimum Auditor Score: {config.MinimumAuditScore}/100 | Self-Healing Max Attempts: {config.MaxSelfHealingAttempts}");
        Console.WriteLine("[Dry-Run] All 7 Stage Agents initialized and verified ready.");

        return new PipelineExecutionSummary(
            Succeeded: true,
            IsDryRun: true,
            SelectedRepository: repo.FullName,
            RotationIndex: selectedTarget.RotationIndex,
            FinalStageReached: "DryRunVerified",
            StatusMessage: $"Dry-run verification succeeded for target '{repo.FullName}' at rotation index {selectedTarget.RotationIndex}.");
    }
}

