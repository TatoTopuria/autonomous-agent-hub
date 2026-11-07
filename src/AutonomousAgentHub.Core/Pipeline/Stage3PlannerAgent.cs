using System.Text;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Stage 3: Safe High-Leverage Task Formulation Agent (Planner).
/// Synthesizes discovery context, verified authoritative evidence, the per-repository memory ledger
/// (both completed proposals and aborted attempts), and experiential heuristics to design a single atomic,
/// non-duplicate improvement plan (<see cref="Stage3PlannerArtifact"/>).
/// </summary>
public sealed class Stage3PlannerAgent(
    IGeminiClient geminiClient,
    ISkillRegistry? skillRegistry = null,
    ExperientialMemoryManager? experientialMemoryManager = null,
    IMcpToolManager? mcpToolManager = null)
{
    private static readonly SkillRegistry DefaultSkillRegistry = new();

    public async Task<Stage3PlannerArtifact> ExecuteAsync(
        Stage1DiscoveryArtifact discovery,
        Stage2FinderArtifact finder,
        RepoStateLedger ledger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentNullException.ThrowIfNull(ledger);

        if (!finder.HasVerifiableEvidence)
        {
            throw new InvalidOperationException(
                "Stage 3 Planner cannot proceed because Stage 2 Finder returned zero verifiable official EvidenceBlocks.");
        }

        Console.WriteLine($"[Stage 3: Planner] Formulating atomic improvement plan for '{discovery.RepositoryFullName}'...");

        string baseSystemInstruction =
            """
            You are Stage 3 (Planner Agent) of the Autonomous Evolution Hub.
            Given the repository discovery map, verified official EvidenceBlocks, and historical RepoStateLedger:
            1. Formulate EXACTLY ONE cohesive, high-leverage, safe atomic improvement task (such as domain validation hardening,
               resilience/cancellation handling, query/memory optimization, edge-case bug fix, or unit test coverage expansion).
            2. DEDUPLICATION MANDATE: Never repeat any taskTitle or taskSlug listed in CompletedProposals OR AbortedAttempts.
            3. EVIDENCE GROUNDING: Ground the proposal directly in at least one EvidenceBlock from Stage 2.
            4. INVARIANT PROTECTION: Ensure every critical business invariant from Stage 1 is preserved.
            5. TEST COVERAGE: Include at least one test file (Modified or Created) in targetFiles to verify the improvement.
            Return ONLY valid JSON matching the Stage3PlannerArtifact contract.
            """;

        string? experientialInjection = experientialMemoryManager?.BuildStagePromptInjection("Stage3Planner", discovery.DetectedStack);
        string systemInstruction = (skillRegistry ?? DefaultSkillRegistry).ComposeStageSystemInstruction(
            baseSystemInstruction,
            "stage3-planner-safety",
            discovery.DetectedStack,
            experientialInjection);

        string? mcpSummary = mcpToolManager?.BuildStageMcpContextSummary("Stage3Planner", string.Empty);
        string userPrompt = BuildPlannerUserPrompt(discovery, finder, ledger, experientialInjection, mcpSummary);

        Stage3PlannerArtifact rawPlan = await geminiClient.GenerateStructuredAsync<Stage3PlannerArtifact>(
            systemInstruction,
            userPrompt,
            useGoogleSearchGrounding: false,
            cancellationToken).ConfigureAwait(false);

        EvidenceBlock primaryEvidence = rawPlan.PrimaryEvidenceBlock
            ?? finder.EvidenceBlocks.FirstOrDefault(e => rawPlan.LinkedEvidenceIds.Contains(e.Id, StringComparer.OrdinalIgnoreCase))
            ?? finder.EvidenceBlocks[0];

        string resolvedSlug = GitWorkspaceManager.NormalizeBranchSlug(
            string.IsNullOrWhiteSpace(rawPlan.TaskSlug) ? rawPlan.TaskTitle : rawPlan.TaskSlug);

        List<string> linkedIds = rawPlan.LinkedEvidenceIds.Count > 0
            ? rawPlan.LinkedEvidenceIds
            : [primaryEvidence.Id];

        List<PlannedFileChange> targetFiles = EnsureTestFilePairing(rawPlan.TargetFiles, discovery);

        return new Stage3PlannerArtifact
        {
            TaskTitle = rawPlan.TaskTitle,
            TaskSlug = resolvedSlug,
            ImprovementCategory = string.IsNullOrWhiteSpace(rawPlan.ImprovementCategory) ? "Reliability & Standards Compliance" : rawPlan.ImprovementCategory,
            EngineeringAndBusinessRationale = rawPlan.EngineeringAndBusinessRationale,
            LinkedEvidenceIds = linkedIds,
            PrimaryEvidenceBlock = primaryEvidence,
            TargetFiles = targetFiles,
            AcceptanceCriteria = rawPlan.AcceptanceCriteria,
            RegressionTestStrategy = rawPlan.RegressionTestStrategy,
            InvariantsPreserved = rawPlan.InvariantsPreserved.Count > 0
                ? rawPlan.InvariantsPreserved
                : discovery.CriticalBusinessInvariants
        };
    }

    /// <summary>
    /// Builds the structured user prompt for Stage 3 Planner including completed proposals, aborted attempts,
    /// experiential memory guardrails, and verified evidence blocks.
    /// </summary>
    public static string BuildPlannerUserPrompt(
        Stage1DiscoveryArtifact discovery,
        Stage2FinderArtifact finder,
        RepoStateLedger ledger,
        string? experientialInjection = null,
        string? mcpContext = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentNullException.ThrowIfNull(ledger);

        StringBuilder prompt = new();
        prompt.AppendLine($"Repository: {discovery.RepositoryFullName}");
        prompt.AppendLine($"Detected Stack: {discovery.DetectedStack}");
        prompt.AppendLine($"Domain Purpose: {discovery.DomainPurpose}");
        prompt.AppendLine($"Architectural Style: {discovery.ArchitecturalStyle}");
        prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(mcpContext))
        {
            prompt.AppendLine(mcpContext);
            prompt.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(experientialInjection))
        {
            prompt.AppendLine(experientialInjection);
            prompt.AppendLine();
        }

        prompt.AppendLine("Critical Business Invariants (Must Not Be Violated):");
        foreach (string inv in discovery.CriticalBusinessInvariants)
        {
            prompt.AppendLine($"- {inv}");
        }
        prompt.AppendLine();

        prompt.AppendLine("Historical Completed Proposals (DO NOT REPEAT THESE):");
        if (ledger.CompletedProposals.Count == 0)
        {
            prompt.AppendLine("- (None yet)");
        }
        else
        {
            foreach (LedgerProposalEntry past in ledger.CompletedProposals)
            {
                string statusTag = string.IsNullOrWhiteSpace(past.OutcomeStatus) ? "OPEN" : past.OutcomeStatus;
                prompt.AppendLine($"- [{past.CreatedAtUtc:yyyy-MM-dd} | Status: {statusTag}] {past.TaskTitle} (Slug: {past.TaskSlug}, Files: {string.Join(", ", past.ModifiedFiles)})");
            }
        }
        prompt.AppendLine();

        prompt.AppendLine("Previously Aborted or Rejected Tasks (DO NOT REPEAT THESE FAILING APPROACHES):");
        if (ledger.AbortedAttempts.Count == 0)
        {
            prompt.AppendLine("- (None yet)");
        }
        else
        {
            foreach (LedgerAbortedEntry aborted in ledger.AbortedAttempts.TakeLast(15))
            {
                prompt.AppendLine($"- [{aborted.AbortedAtUtc:yyyy-MM-dd} | HaltedAt: {aborted.StageAbortedAt}] {aborted.TaskTitle} -> Reason: {aborted.Reason}");
            }
        }
        prompt.AppendLine();

        prompt.AppendLine("Verified Authoritative EvidenceBlocks (Stage 2):");
        foreach (EvidenceBlock block in finder.EvidenceBlocks)
        {
            prompt.AppendLine($"- ID: {block.Id} | Authority: {block.IssuingAuthority} | Title: {block.DocumentTitle} ({block.PublicationYear})");
            prompt.AppendLine($"  URL: {block.OfficialUrl}");
            prompt.AppendLine($"  Excerpt: {block.GoverningExcerpt}");
            prompt.AppendLine($"  Relevance: {block.RelevanceToRepo}");
        }
        prompt.AppendLine();

        prompt.AppendLine("Available Candidate Files & Excerpts:");
        foreach (InspectedFileSnapshot snap in discovery.CandidateFilesForInspection)
        {
            prompt.AppendLine($"--- {snap.RelativePath} ---");
            prompt.AppendLine(snap.ContentExcerpt);
            prompt.AppendLine();
        }

        return prompt.ToString();
    }

    private static List<PlannedFileChange> EnsureTestFilePairing(
        List<PlannedFileChange> plannedFiles,
        Stage1DiscoveryArtifact discovery)
    {
        List<PlannedFileChange> result = [.. plannedFiles];

        bool hasTestFile = result.Any(f =>
            f.IsTestFile ||
            f.RelativePath.Contains("test", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.Contains("spec", StringComparison.OrdinalIgnoreCase));

        if (!hasTestFile && discovery.ExistingTestFiles.Count > 0)
        {
            string fallbackTestPath = discovery.ExistingTestFiles[0];
            result.Add(new PlannedFileChange
            {
                RelativePath = fallbackTestPath,
                Operation = "Modify",
                ChangeDescription = "Add deterministic unit/regression test coverage verifying the planned improvement.",
                IsTestFile = true
            });
        }

        return result;
    }
}
