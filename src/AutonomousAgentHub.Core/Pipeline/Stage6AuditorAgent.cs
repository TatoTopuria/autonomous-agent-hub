using System.Text;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Stage 6: Independent Quality &amp; Compliance Gate Agent (Auditor).
/// Acts as an adversarial Principal Reviewer inspecting the real <c>git diff</c>, test report,
/// authoritative citations, and business invariants before approving or rejecting deployment.
/// </summary>
public sealed class Stage6AuditorAgent(
    IGeminiClient geminiClient,
    IGitWorkspaceManager gitWorkspaceManager,
    int minimumQualityScore = 85,
    ISkillRegistry? skillRegistry = null,
    ExperientialMemoryManager? experientialMemoryManager = null,
    IMcpToolManager? mcpToolManager = null)
{
    private static readonly SkillRegistry DefaultSkillRegistry = new();

    private static readonly string[] SecretPatterns =
    [
        "BEGIN RSA PRIVATE KEY",
        "BEGIN OPENSSH PRIVATE KEY",
        "ghp_",
        "github_pat_",
        "AIzaSy",
        "AKIA"
    ];

    public async Task<Stage6AuditVerdictArtifact> ExecuteAsync(
        string workspacePath,
        Stage1DiscoveryArtifact discovery,
        Stage2FinderArtifact finder,
        Stage3PlannerArtifact plan,
        Stage4ExecutionArtifact execution,
        Stage5TestReportArtifact testReport,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(testReport);

        Console.WriteLine($"[Stage 6: Auditor] Running adversarial Principal Audit on '{discovery.RepositoryFullName}'...");

        if (!testReport.Passed)
        {
            return new Stage6AuditVerdictArtifact
            {
                Approved = false,
                QualityScore = 0,
                ComplianceChecklist =
                [
                    new AuditChecklistItem
                    {
                        Criterion = "100% Green Build & Test Suite",
                        Passed = false,
                        Notes = "Stage 5 Tester did not pass."
                    }
                ],
                BlockingIssues = ["Build and test suite did not pass in Stage 5."],
                ExecutiveSummary = "Rejected immediately because Stage 5 tests failed.",
                DiffLineCount = 0
            };
        }

        string unifiedDiff = await gitWorkspaceManager.GetUnifiedDiffAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        int diffLineCount = string.IsNullOrWhiteSpace(unifiedDiff)
            ? 0
            : unifiedDiff.Split('\n').Length;

        if (diffLineCount == 0)
        {
            return new Stage6AuditVerdictArtifact
            {
                Approved = false,
                QualityScore = 0,
                ComplianceChecklist =
                [
                    new AuditChecklistItem
                    {
                        Criterion = "Non-Empty Git Diff",
                        Passed = false,
                        Notes = "Workspace contains zero modified lines."
                    }
                ],
                BlockingIssues = ["Generated git diff is empty; nothing to deploy."],
                ExecutiveSummary = "Rejected because no effective code changes were produced in the workspace.",
                DiffLineCount = 0
            };
        }

        foreach (string secretMarker in SecretPatterns)
        {
            if (unifiedDiff.Contains(secretMarker, StringComparison.Ordinal))
            {
                return new Stage6AuditVerdictArtifact
                {
                    Approved = false,
                    QualityScore = 0,
                    ComplianceChecklist =
                    [
                        new AuditChecklistItem
                        {
                            Criterion = "Zero Hardcoded Secrets",
                            Passed = false,
                            Notes = $"Detected potential secret token pattern '{secretMarker}' in diff."
                        }
                    ],
                    BlockingIssues = [$"Security violation: Potential hardcoded credential pattern ('{secretMarker}') detected in diff."],
                    ExecutiveSummary = "Rejected by deterministic DevSecOps secret scanner.",
                    DiffLineCount = diffLineCount
                };
            }
        }

        string baseSystemInstruction =
            """
            You are Stage 6 (Independent Principal Auditor Agent) of the Autonomous Evolution Hub.
            You act as an adversarial Principal Systems Architect & Security Reviewer.
            Inspect the unified git diff, Stage 5 test report, Stage 2 official EvidenceBlocks, and Stage 1 business invariants.
            Evaluate strictly across these criteria:
            1. 100% Green Build & Test Execution (including unit test coverage for the change).
            2. Verifiable Official Citations (zero hallucinated or blog/forum sources).
            3. Preservation of Critical Business Invariants (zero breaking side effects or deleted core logic).
            4. DevSecOps Hygiene (zero hardcoded secrets, safe input handling, clean error handling).
            5. Atomic & Cohesive Scope (diff matches the planned task without unrelated formatting churn).

            Assign a `qualityScore` from 0 to 100. Set `approved: true` ONLY if `qualityScore >= 85` and `blockingIssues` is empty.
            Return ONLY valid JSON matching the Stage6AuditVerdictArtifact contract.
            """;

        string? experientialInjection = experientialMemoryManager?.BuildStagePromptInjection("Stage6Auditor", discovery.DetectedStack);
        string systemInstruction = (skillRegistry ?? DefaultSkillRegistry).ComposeStageSystemInstruction(
            baseSystemInstruction,
            "stage6-auditor-adversarial",
            discovery.DetectedStack,
            experientialInjection);

        StringBuilder prompt = new();
        prompt.AppendLine($"Repository: {discovery.RepositoryFullName} ({discovery.DetectedStack})");
        prompt.AppendLine($"Task Title: {plan.TaskTitle}");
        prompt.AppendLine($"Rationale: {plan.EngineeringAndBusinessRationale}");

        if (mcpToolManager is not null)
        {
            string mcpSummary = mcpToolManager.BuildStageMcpContextSummary("Stage6Auditor", workspacePath);
            if (!string.IsNullOrWhiteSpace(mcpSummary))
            {
                prompt.AppendLine();
                prompt.AppendLine(mcpSummary);
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Stage 1 Critical Business Invariants:");
        foreach (string inv in discovery.CriticalBusinessInvariants)
        {
            prompt.AppendLine($"- {inv}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Stage 2 Authoritative Citations:");
        foreach (EvidenceBlock ev in finder.EvidenceBlocks)
        {
            prompt.AppendLine($"- [{ev.Id}] {ev.IssuingAuthority}: {ev.DocumentTitle} ({ev.PublicationYear}) -> {ev.OfficialUrl}");
        }

        prompt.AppendLine();
        prompt.AppendLine($"Stage 5 Test Report: Passed={testReport.Passed}, Attempts={testReport.TotalAttempts}");
        prompt.AppendLine();
        prompt.AppendLine($"=== UNIFIED GIT DIFF ({diffLineCount} lines) ===");
        prompt.AppendLine(BuildPrioritizedDiffExcerpt(unifiedDiff, maxChars: 18000));

        Stage6AuditVerdictArtifact llmVerdict = await geminiClient.GenerateStructuredAsync<Stage6AuditVerdictArtifact>(
            systemInstruction,
            prompt.ToString(),
            useGoogleSearchGrounding: false,
            cancellationToken).ConfigureAwait(false);

        bool finalApproved = llmVerdict.Approved &&
                             llmVerdict.QualityScore >= minimumQualityScore &&
                             llmVerdict.BlockingIssues.Count == 0;

        return new Stage6AuditVerdictArtifact
        {
            Approved = finalApproved,
            QualityScore = Math.Clamp(llmVerdict.QualityScore, 0, 100),
            ComplianceChecklist = llmVerdict.ComplianceChecklist,
            BlockingIssues = llmVerdict.BlockingIssues,
            ExecutiveSummary = llmVerdict.ExecutiveSummary,
            DiffLineCount = diffLineCount
        };
    }

    /// <summary>
    /// Preserves a complete modified-file manifest (<c>diff --git a/... b/...</c>) at the top of the excerpt
    /// and allocates balanced character budgets across all modified production and test file sections so
    /// trailing unit test diffs are never truncated out of existence.
    /// </summary>
    public static string BuildPrioritizedDiffExcerpt(string unifiedDiff, int maxChars = 18000)
    {
        if (string.IsNullOrEmpty(unifiedDiff) || unifiedDiff.Length <= maxChars)
        {
            return unifiedDiff;
        }

        string[] sections = unifiedDiff.Split("diff --git ", StringSplitOptions.RemoveEmptyEntries);
        if (sections.Length <= 1)
        {
            return unifiedDiff[..maxChars];
        }

        List<string> fileDiffBlocks = [.. sections.Select(s => "diff --git " + s)];
        List<string> fileHeaders =
        [
            .. fileDiffBlocks.Select(block =>
            {
                int newline = block.IndexOf('\n');
                return newline > 0 ? block[..newline].Trim() : block.Trim();
            })
        ];

        StringBuilder sb = new();
        sb.AppendLine("--- MODIFIED FILES SUMMARY (COMPLETE DIFF MANIFEST) ---");
        foreach (string header in fileHeaders)
        {
            sb.AppendLine($"* {header}");
        }
        sb.AppendLine("--- BALANCED PER-FILE DIFF HUNKS ---");

        int remainingBudget = Math.Max(2000, maxChars - sb.Length);
        int perFileBudget = Math.Max(600, remainingBudget / fileDiffBlocks.Count);

        foreach (string block in fileDiffBlocks)
        {
            if (block.Length <= perFileBudget)
            {
                sb.AppendLine(block.TrimEnd());
            }
            else
            {
                sb.AppendLine(block[..perFileBudget].TrimEnd());
                sb.AppendLine("... [FILE DIFF HUNK TRUNCATED TO PRESERVE MULTI-FILE AUDIT VISIBILITY] ...");
            }
        }

        string result = sb.ToString();
        return result.Length <= maxChars ? result : result[..maxChars];
    }
}
