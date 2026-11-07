using System.Text;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Stage 3: Safe High-Leverage Task Formulation Agent (Planner).
/// Synthesizes discovery context, verified authoritative evidence, and the per-repository memory ledger
/// to design a single atomic, non-duplicate improvement plan (<see cref="Stage3PlannerArtifact"/>).
/// </summary>
public sealed class Stage3PlannerAgent(IGeminiClient geminiClient)
{
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

        string systemInstruction =
            """
            You are Stage 3 (Planner Agent) of the Autonomous Evolution Hub.
            Given the repository discovery map, verified official EvidenceBlocks, and historical RepoStateLedger:
            1. Formulate EXACTLY ONE cohesive, high-leverage, safe atomic improvement task (such as domain validation hardening,
               resilience/cancellation handling, query/memory optimization, edge-case bug fix, or unit test coverage expansion).
            2. DEDUPLICATION MANDATE: Never repeat any taskTitle or taskSlug listed in the historical RepoStateLedger.
            3. EVIDENCE GROUNDING: Ground the proposal directly in at least one EvidenceBlock from Stage 2.
            4. INVARIANT PROTECTION: Ensure every critical business invariant from Stage 1 is preserved.
            5. TEST COVERAGE: Include at least one test file (Modified or Created) in targetFiles to verify the improvement.
            Return ONLY valid JSON matching the Stage3PlannerArtifact contract.
            """;

        StringBuilder prompt = new();
        prompt.AppendLine($"Repository: {discovery.RepositoryFullName}");
        prompt.AppendLine($"Detected Stack: {discovery.DetectedStack}");
        prompt.AppendLine($"Domain Purpose: {discovery.DomainPurpose}");
        prompt.AppendLine($"Architectural Style: {discovery.ArchitecturalStyle}");
        prompt.AppendLine();

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
                prompt.AppendLine($"- [{past.CreatedAtUtc:yyyy-MM-dd}] {past.TaskTitle} (Slug: {past.TaskSlug}, Files: {string.Join(", ", past.ModifiedFiles)})");
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

        Stage3PlannerArtifact rawPlan = await geminiClient.GenerateStructuredAsync<Stage3PlannerArtifact>(
            systemInstruction,
            prompt.ToString(),
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

        return new Stage3PlannerArtifact
        {
            TaskTitle = rawPlan.TaskTitle,
            TaskSlug = resolvedSlug,
            ImprovementCategory = string.IsNullOrWhiteSpace(rawPlan.ImprovementCategory) ? "Reliability & Standards Compliance" : rawPlan.ImprovementCategory,
            EngineeringAndBusinessRationale = rawPlan.EngineeringAndBusinessRationale,
            LinkedEvidenceIds = linkedIds,
            PrimaryEvidenceBlock = primaryEvidence,
            TargetFiles = rawPlan.TargetFiles,
            AcceptanceCriteria = rawPlan.AcceptanceCriteria,
            RegressionTestStrategy = rawPlan.RegressionTestStrategy,
            InvariantsPreserved = rawPlan.InvariantsPreserved.Count > 0
                ? rawPlan.InvariantsPreserved
                : discovery.CriticalBusinessInvariants
        };
    }
}

