using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Stage 7: Branch Pusher, Pull Request Creator &amp; Central State Updater Agent (Deployer).
/// Executes ONLY when Stage 5 Tester passed 100% green AND Stage 6 Auditor approved with QualityScore &gt;= 85.
/// </summary>
public sealed class Stage7DeployerAgent(
    IGitWorkspaceManager gitWorkspaceManager,
    IGitHubPullRequestClient pullRequestClient,
    StateLedgerManager stateLedgerManager,
    int minimumQualityScore = 85)
{
    public async Task<PullRequestCreationResult> ExecuteAsync(
        SelectedRepositoryTarget selectedTarget,
        string workspacePath,
        Stage1DiscoveryArtifact discovery,
        Stage2FinderArtifact finder,
        Stage3PlannerArtifact plan,
        Stage4ExecutionArtifact execution,
        Stage5TestReportArtifact testReport,
        Stage6AuditVerdictArtifact auditVerdict,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(testReport);
        ArgumentNullException.ThrowIfNull(auditVerdict);

        if (!testReport.Passed)
        {
            throw new InvalidOperationException("Stage 7 Deployer refused execution: Stage 5 test suite did not pass.");
        }

        if (!auditVerdict.MeetsDeploymentGate(minimumQualityScore))
        {
            throw new InvalidOperationException(
                $"Stage 7 Deployer refused execution: Stage 6 Auditor did not approve (Approved={auditVerdict.Approved}, Score={auditVerdict.QualityScore}/{minimumQualityScore}).");
        }

        RepositoryEntry repository = selectedTarget.Repository;
        Console.WriteLine($"[Stage 7: Deployer] Creating branch, committing, and pushing verified changes to '{repository.FullName}'...");

        string branchName = await gitWorkspaceManager.CreateBranchCommitAndPushAsync(
            workspacePath,
            repository,
            plan.TaskTitle,
            plan.TaskSlug,
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"[Stage 7: Deployer] Opening Pull Request on '{repository.FullName}' from branch '{branchName}'...");

        PullRequestCreationResult prResult = await pullRequestClient.CreatePullRequestAsync(
            repository,
            branchName,
            discovery,
            finder,
            plan,
            execution,
            testReport,
            auditVerdict,
            cancellationToken).ConfigureAwait(false);

        string governingAuthority = plan.PrimaryEvidenceBlock?.IssuingAuthority
            ?? finder.EvidenceBlocks.FirstOrDefault()?.IssuingAuthority
            ?? "Official Engineering Standard";

        LedgerProposalEntry ledgerEntry = new()
        {
            TaskTitle = plan.TaskTitle,
            TaskSlug = plan.TaskSlug,
            BranchName = branchName,
            PullRequestUrl = prResult.HtmlUrl,
            PullRequestNumber = prResult.Number,
            QualityScore = auditVerdict.QualityScore,
            GoverningAuthority = governingAuthority,
            ModifiedFiles = [.. execution.FileMutations.Select(m => m.RelativePath)],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await stateLedgerManager.RecordCompletedProposalAsync(
            repository,
            ledgerEntry,
            cancellationToken).ConfigureAwait(false);

        await stateLedgerManager.AdvanceRotationStateAsync(
            selectedTarget,
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"[Stage 7: Deployer] Successfully opened PR #{prResult.Number} ({prResult.HtmlUrl}) and updated rotation state.");

        return prResult;
    }
}

