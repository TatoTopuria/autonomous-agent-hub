using System.Text;
using System.Text.Json;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Serialization;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Manages atomic persistence and closed-loop Recursive Self-Improvement (RSI) distillation for
/// <c>state/experiential-memory.json</c>. Converts Stage 5 compiler/test failures, Stage 6 Auditor rejections,
/// and closed-unmerged GitHub Pull Requests into permanent stack-aware heuristics and anti-pattern guardrails.
/// </summary>
public sealed class ExperientialMemoryManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        TypeInfoResolver = HubJsonSerializerContext.Default
    };

    private readonly string _memoryFilePath;
    private ExperientialMemoryState? _cachedState;

    public ExperientialMemoryManager(string stateDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectoryPath);
        _memoryFilePath = Path.Combine(stateDirectoryPath, "experiential-memory.json");
    }

    public string MemoryFilePath => _memoryFilePath;

    /// <summary>
    /// Loads <c>state/experiential-memory.json</c> or returns the baseline seeded memory state if the file does not exist.
    /// </summary>
    public async Task<ExperientialMemoryState> LoadMemoryAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_memoryFilePath))
        {
            _cachedState = CreateBaselineSeededMemory();
            return _cachedState;
        }

        string json = await File.ReadAllTextAsync(_memoryFilePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            _cachedState = CreateBaselineSeededMemory();
            return _cachedState;
        }

        _cachedState = JsonSerializer.Deserialize<ExperientialMemoryState>(json, JsonOptions) ?? CreateBaselineSeededMemory();
        return _cachedState;
    }

    /// <summary>
    /// Persists the updated <see cref="ExperientialMemoryState"/> atomically via a temporary sibling file and <see cref="File.Move(string,string,bool)"/>.
    /// </summary>
    public async Task SaveMemoryAsync(ExperientialMemoryState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.LastUpdatedUtc = DateTimeOffset.UtcNow;
        _cachedState = state;

        string json = JsonSerializer.Serialize(state, JsonOptions);
        await WriteFileAtomicallyAsync(_memoryFilePath, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Distills a Stage 5 build/test failure attempt into deduplicated <see cref="ForbiddenAntiPatternRule"/> and
    /// <see cref="LearnedHeuristicRule"/> entries in <c>state/experiential-memory.json</c>.
    /// </summary>
    public async Task RecordStage5FailureLessonAsync(
        string repoFullName,
        string detectedStack,
        string taskTitle,
        TestExecutionAttempt failedAttempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedStack);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskTitle);
        ArgumentNullException.ThrowIfNull(failedAttempt);

        ExperientialMemoryState state = await LoadMemoryAsync(cancellationToken).ConfigureAwait(false);
        IncrementStageFailureCount(state, "Stage5Tester");

        string extractedDiagnostic = ExtractConciseFailureSignature(failedAttempt);
        string patternDescription =
            $"[{detectedStack}] Build/Test failure in '{repoFullName}' during task '{taskTitle}': {extractedDiagnostic}";
        string mitigationRule =
            $"Before finalizing Stage 4 mutations for {detectedStack}, verify all referenced symbols, namespaces/imports, and test assertions compile cleanly against existing signatures ({extractedDiagnostic}).";

        AddAntiPatternIfUnique(
            state,
            targetStack: NormalizeStackName(detectedStack),
            patternDescription: patternDescription,
            mitigationRule: mitigationRule,
            triggeredByStage: "Stage5Tester");

        AddHeuristicIfUnique(
            state,
            targetStack: NormalizeStackName(detectedStack),
            applicableStages: ["Stage3Planner", "Stage4Executor", "Stage5Tester"],
            directive: mitigationRule,
            sourceReason: $"Distilled from Stage 5 Attempt #{failedAttempt.AttemptNumber} failure in {repoFullName}");

        await SaveMemoryAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts Stage 6 Auditor <see cref="Stage6AuditVerdictArtifact.BlockingIssues"/> into permanent
    /// <see cref="ForbiddenAntiPatternRule"/> guardrails.
    /// </summary>
    public async Task RecordStage6RejectionLessonAsync(
        string repoFullName,
        string detectedStack,
        string taskTitle,
        Stage6AuditVerdictArtifact rejectedVerdict,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedStack);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskTitle);
        ArgumentNullException.ThrowIfNull(rejectedVerdict);

        ExperientialMemoryState state = await LoadMemoryAsync(cancellationToken).ConfigureAwait(false);
        IncrementStageFailureCount(state, "Stage6Auditor");

        IReadOnlyList<string> issues = rejectedVerdict.BlockingIssues.Count > 0
            ? rejectedVerdict.BlockingIssues
            : [rejectedVerdict.ExecutiveSummary];

        string normalizedStack = NormalizeStackName(detectedStack);

        foreach (string issue in issues.Where(i => !string.IsNullOrWhiteSpace(i)))
        {
            string cleanIssue = issue.Trim();
            string patternDescription = $"[{normalizedStack}] Auditor rejection in '{repoFullName}' ('{taskTitle}'): {cleanIssue}";
            string mitigationRule = $"Ensure all planned and executed changes strictly prevent Auditor blocking issue: {cleanIssue}";

            AddAntiPatternIfUnique(
                state,
                targetStack: normalizedStack,
                patternDescription: patternDescription,
                mitigationRule: mitigationRule,
                triggeredByStage: "Stage6Auditor");

            AddHeuristicIfUnique(
                state,
                targetStack: normalizedStack,
                applicableStages: ["Stage3Planner", "Stage4Executor", "Stage6Auditor"],
                directive: mitigationRule,
                sourceReason: $"Distilled from Stage 6 Auditor rejection (Score {rejectedVerdict.QualityScore}/100) in {repoFullName}");
        }

        await SaveMemoryAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records an experiential lesson when a previously opened Pull Request is discovered to be <c>CLOSED_UNMERGED</c>.
    /// </summary>
    public async Task RecordRejectedPullRequestLessonAsync(
        string repoFullName,
        string detectedStack,
        LedgerProposalEntry rejectedPr,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedStack);
        ArgumentNullException.ThrowIfNull(rejectedPr);

        ExperientialMemoryState state = await LoadMemoryAsync(cancellationToken).ConfigureAwait(false);
        IncrementStageFailureCount(state, "Stage7PullRequestClosedUnmerged");

        string normalizedStack = NormalizeStackName(detectedStack);
        string reviewNote = string.IsNullOrWhiteSpace(rejectedPr.ReviewSummary)
            ? "Closed without merge by maintainer"
            : rejectedPr.ReviewSummary;

        string patternDescription =
            $"[{normalizedStack}] Unmerged PR #{rejectedPr.PullRequestNumber} in '{repoFullName}': '{rejectedPr.TaskTitle}' ({reviewNote}).";
        string mitigationRule =
            $"Do not re-propose rejected task '{rejectedPr.TaskTitle}' (slug: '{rejectedPr.TaskSlug}') in '{repoFullName}'. Favor higher-leverage, non-intrusive improvements backed by test coverage.";

        AddAntiPatternIfUnique(
            state,
            targetStack: normalizedStack,
            patternDescription: patternDescription,
            mitigationRule: mitigationRule,
            triggeredByStage: "Stage7Deployer");

        AddHeuristicIfUnique(
            state,
            targetStack: normalizedStack,
            applicableStages: ["Stage3Planner"],
            directive: mitigationRule,
            sourceReason: $"Closed-unmerged PR #{rejectedPr.PullRequestNumber} feedback loop in {repoFullName}");

        await SaveMemoryAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Polls GitHub for any <c>OPEN</c> Pull Requests recorded in the target repository's ledger,
    /// updates their <c>OutcomeStatus</c> (<c>MERGED</c> vs. <c>CLOSED_UNMERGED</c>), and feeds rejections into experiential memory.
    /// </summary>
    public async Task SynchronizeOpenPullRequestsAsync(
        RepositoryEntry repository,
        StateLedgerManager stateLedgerManager,
        IGitHubPullRequestClient prClient,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(stateLedgerManager);
        ArgumentNullException.ThrowIfNull(prClient);

        RepoStateLedger ledger = await stateLedgerManager.LoadRepoLedgerAsync(repository, cancellationToken).ConfigureAwait(false);
        if (ledger.CompletedProposals.Count == 0)
        {
            return;
        }

        bool ledgerModified = false;
        List<LedgerProposalEntry> updatedProposals = [];

        foreach (LedgerProposalEntry proposal in ledger.CompletedProposals)
        {
            if (!string.Equals(proposal.OutcomeStatus, "OPEN", StringComparison.OrdinalIgnoreCase) ||
                proposal.PullRequestNumber <= 0)
            {
                updatedProposals.Add(proposal);
                continue;
            }

            PullRequestOutcomeStatus? outcome = await prClient.GetPullRequestOutcomeAsync(
                repository,
                proposal.PullRequestNumber,
                cancellationToken).ConfigureAwait(false);

            if (outcome is null || string.Equals(outcome.OutcomeStatus, "OPEN", StringComparison.OrdinalIgnoreCase))
            {
                updatedProposals.Add(proposal);
                continue;
            }

            LedgerProposalEntry updatedEntry = new()
            {
                TaskTitle = proposal.TaskTitle,
                TaskSlug = proposal.TaskSlug,
                BranchName = proposal.BranchName,
                PullRequestUrl = proposal.PullRequestUrl,
                PullRequestNumber = proposal.PullRequestNumber,
                QualityScore = proposal.QualityScore,
                GoverningAuthority = proposal.GoverningAuthority,
                ModifiedFiles = proposal.ModifiedFiles,
                OutcomeStatus = outcome.OutcomeStatus,
                ReviewSummary = outcome.ReviewSummary ?? proposal.ReviewSummary,
                CreatedAtUtc = proposal.CreatedAtUtc
            };

            updatedProposals.Add(updatedEntry);
            ledgerModified = true;

            if (string.Equals(outcome.OutcomeStatus, "CLOSED_UNMERGED", StringComparison.OrdinalIgnoreCase))
            {
                await RecordRejectedPullRequestLessonAsync(
                    repository.FullName,
                    repository.PrimaryStack ?? "All",
                    updatedEntry,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (ledgerModified)
        {
            RepoStateLedger updatedLedger = new()
            {
                RepositoryFullName = ledger.RepositoryFullName,
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                CompletedProposals = updatedProposals,
                AbortedAttempts = ledger.AbortedAttempts
            };

            await stateLedgerManager.SaveRepoLedgerAsync(repository, updatedLedger, cancellationToken).ConfigureAwait(false);
        }

        // Update aggregate PR outcome metrics for this repository snapshot
        ExperientialMemoryState state = await LoadMemoryAsync(cancellationToken).ConfigureAwait(false);
        int merged = updatedProposals.Count(p => string.Equals(p.OutcomeStatus, "MERGED", StringComparison.OrdinalIgnoreCase));
        int closedUnmerged = updatedProposals.Count(p => string.Equals(p.OutcomeStatus, "CLOSED_UNMERGED", StringComparison.OrdinalIgnoreCase));
        int open = updatedProposals.Count(p => string.Equals(p.OutcomeStatus, "OPEN", StringComparison.OrdinalIgnoreCase));

        state.PrOutcomeMetrics.TotalTracked = Math.Max(state.PrOutcomeMetrics.TotalTracked, updatedProposals.Count);
        state.PrOutcomeMetrics.MergedCount = Math.Max(state.PrOutcomeMetrics.MergedCount, merged);
        state.PrOutcomeMetrics.ClosedUnmergedCount = Math.Max(state.PrOutcomeMetrics.ClosedUnmergedCount, closedUnmerged);
        state.PrOutcomeMetrics.OpenCount = open;

        await SaveMemoryAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a concise Markdown prompt injection block containing active <see cref="LearnedHeuristicRule"/>
    /// and <see cref="ForbiddenAntiPatternRule"/> entries matching <paramref name="stageName"/> and <paramref name="detectedStack"/>.
    /// </summary>
    public string BuildStagePromptInjection(string stageName, string detectedStack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedStack);

        ExperientialMemoryState state = _cachedState ?? LoadMemorySyncFallback();
        return FormatStagePromptInjection(state, stageName, detectedStack);
    }

    /// <summary>
    /// Formats a concise Markdown block of active learned heuristics and forbidden anti-patterns for the given stage and stack.
    /// </summary>
    public static string FormatStagePromptInjection(
        ExperientialMemoryState state,
        string stageName,
        string detectedStack)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedStack);

        string normalizedStack = NormalizeStackName(detectedStack);

        List<LearnedHeuristicRule> matchingHeuristics =
        [
            .. state.LearnedHeuristics
                .Where(h =>
                    IsStackMatch(h.TargetStack, normalizedStack) &&
                    (h.ApplicableStages.Count == 0 ||
                     h.ApplicableStages.Any(s =>
                         s.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains(stageName, StringComparison.OrdinalIgnoreCase) ||
                         stageName.Contains(s, StringComparison.OrdinalIgnoreCase))))
                .TakeLast(8)
        ];

        List<ForbiddenAntiPatternRule> matchingAntiPatterns =
        [
            .. state.ForbiddenAntiPatterns
                .Where(a => IsStackMatch(a.TargetStack, normalizedStack))
                .TakeLast(8)
        ];

        if (matchingHeuristics.Count == 0 && matchingAntiPatterns.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new();
        sb.AppendLine("### Experiential Memory Guardrails (Closed-Loop RSI Ledger)");

        if (matchingHeuristics.Count > 0)
        {
            sb.AppendLine("#### Learned Heuristics (MANDATORY):");
            foreach (LearnedHeuristicRule rule in matchingHeuristics)
            {
                sb.AppendLine($"- **[{rule.Id} | {rule.TargetStack}]** {rule.Directive}");
            }
        }

        if (matchingAntiPatterns.Count > 0)
        {
            sb.AppendLine("#### Forbidden Anti-Patterns (NEVER REPEAT):");
            foreach (ForbiddenAntiPatternRule anti in matchingAntiPatterns)
            {
                sb.AppendLine($"- **[{anti.Id} | {anti.TargetStack}]** Avoid: {anti.PatternDescription} -> *Mitigation:* {anti.MitigationRule}");
            }
        }

        return sb.ToString().Trim();
    }

    public static ExperientialMemoryState CreateBaselineSeededMemory() =>
        new()
        {
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            PrOutcomeMetrics = new PrOutcomeSummary
            {
                TotalTracked = 0,
                MergedCount = 0,
                ClosedUnmergedCount = 0,
                OpenCount = 0
            },
            StageFailureCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Stage2Finder"] = 0,
                ["Stage5Tester"] = 0,
                ["Stage6Auditor"] = 0
            },
            LearnedHeuristics =
            [
                new LearnedHeuristicRule
                {
                    Id = "LH-001",
                    TargetStack = "All",
                    ApplicableStages = ["Stage3Planner", "Stage4Executor"],
                    Directive = "Every production code modification MUST be paired with at least one deterministic unit or regression test file in targetFiles and fileMutations.",
                    SourceReason = "Baseline Hub Invariant — Stage 6 Auditor requires verified unit test coverage.",
                    CreatedAtUtc = new DateTimeOffset(2026, 11, 7, 10, 31, 0, TimeSpan.Zero)
                },
                new LearnedHeuristicRule
                {
                    Id = "LH-002",
                    TargetStack = "DotNet",
                    ApplicableStages = ["Stage1Discovery", "Stage3Planner", "Stage4Executor", "Stage6Auditor"],
                    Directive = "Propagate CancellationToken through all async methods, enforce ConfigureAwait(false) in library/infrastructure code, and apply AsNoTracking() on read-only EF Core queries.",
                    SourceReason = "Microsoft Learn .NET 8 Concurrency & EF Core Performance Standards.",
                    CreatedAtUtc = new DateTimeOffset(2026, 11, 7, 10, 31, 0, TimeSpan.Zero)
                },
                new LearnedHeuristicRule
                {
                    Id = "LH-003",
                    TargetStack = "JavaGradle",
                    ApplicableStages = ["Stage3Planner", "Stage4Executor", "Stage6Auditor"],
                    Directive = "Use constructor injection, @Transactional(readOnly = true) for query methods, Jakarta Bean Validation annotations on request DTOs, and AssertJ in JUnit 5 tests.",
                    SourceReason = "Official Spring Boot 3 & Jakarta EE Specifications.",
                    CreatedAtUtc = new DateTimeOffset(2026, 11, 7, 10, 31, 0, TimeSpan.Zero)
                },
                new LearnedHeuristicRule
                {
                    Id = "LH-004",
                    TargetStack = "DevOpsInfra",
                    ApplicableStages = ["Stage1Discovery", "Stage3Planner", "Stage4Executor", "Stage6Auditor"],
                    Directive = "Enforce non-root container execution (securityContext.runAsNonRoot: true), explicit CPU/memory requests and limits, valid Grafana/OpenTelemetry JSON schemas, and bash -n syntax verification.",
                    SourceReason = "CNCF, CIS Kubernetes Benchmark & OWASP Container Security Guidelines.",
                    CreatedAtUtc = new DateTimeOffset(2026, 11, 7, 10, 31, 0, TimeSpan.Zero)
                }
            ],
            ForbiddenAntiPatterns =
            [
                new ForbiddenAntiPatternRule
                {
                    Id = "AP-001",
                    TargetStack = "All",
                    PatternDescription = "Emitting lazy truncation placeholders such as '// ... existing code ...' or '/* remaining implementation */' in Stage 4 FileMutation.updatedContent.",
                    MitigationRule = "Always emit 100% complete, compilable source files in FileMutation.updatedContent without any omitted sections.",
                    TriggeredByStage = "Stage4Executor",
                    CreatedAtUtc = new DateTimeOffset(2026, 11, 7, 10, 31, 0, TimeSpan.Zero)
                },
                new ForbiddenAntiPatternRule
                {
                    Id = "AP-002",
                    TargetStack = "All",
                    PatternDescription = "Citing unofficial blogs (Medium, Reddit, StackOverflow, dev.to, Baeldung, DZone) as Stage 2 EvidenceBlocks.",
                    MitigationRule = "Cite only official standards bodies and documentation (Microsoft Learn, IETF RFCs, ECMA, OWASP, CNCF, Official Spring/Python/Node Docs, WHO/AAP/PCI-DSS).",
                    TriggeredByStage = "Stage2Finder",
                    CreatedAtUtc = new DateTimeOffset(2026, 11, 7, 10, 31, 0, TimeSpan.Zero)
                }
            ]
        };

    private ExperientialMemoryState LoadMemorySyncFallback()
    {
        try
        {
            if (File.Exists(_memoryFilePath))
            {
                string json = File.ReadAllText(_memoryFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    _cachedState = JsonSerializer.Deserialize<ExperientialMemoryState>(json, JsonOptions);
                    if (_cachedState is not null)
                    {
                        return _cachedState;
                    }
                }
            }
        }
        catch
        {
            // Fall through to baseline seeded memory
        }

        _cachedState = CreateBaselineSeededMemory();
        return _cachedState;
    }

    private static void IncrementStageFailureCount(ExperientialMemoryState state, string stageKey)
    {
        if (state.StageFailureCounts.TryGetValue(stageKey, out int current))
        {
            state.StageFailureCounts[stageKey] = current + 1;
        }
        else
        {
            state.StageFailureCounts[stageKey] = 1;
        }
    }

    private static void AddHeuristicIfUnique(
        ExperientialMemoryState state,
        string targetStack,
        List<string> applicableStages,
        string directive,
        string sourceReason)
    {
        bool exists = state.LearnedHeuristics.Any(h =>
            string.Equals(h.TargetStack, targetStack, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(h.Directive, directive, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            return;
        }

        string nextId = $"LH-{(state.LearnedHeuristics.Count + 1):D3}";
        state.LearnedHeuristics.Add(new LearnedHeuristicRule
        {
            Id = nextId,
            TargetStack = targetStack,
            ApplicableStages = applicableStages,
            Directive = directive,
            SourceReason = sourceReason,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static void AddAntiPatternIfUnique(
        ExperientialMemoryState state,
        string targetStack,
        string patternDescription,
        string mitigationRule,
        string triggeredByStage)
    {
        bool exists = state.ForbiddenAntiPatterns.Any(a =>
            string.Equals(a.TargetStack, targetStack, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.PatternDescription, patternDescription, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            return;
        }

        string nextId = $"AP-{(state.ForbiddenAntiPatterns.Count + 1):D3}";
        state.ForbiddenAntiPatterns.Add(new ForbiddenAntiPatternRule
        {
            Id = nextId,
            TargetStack = targetStack,
            PatternDescription = patternDescription,
            MitigationRule = mitigationRule,
            TriggeredByStage = triggeredByStage,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static string ExtractConciseFailureSignature(TestExecutionAttempt failedAttempt)
    {
        string combined = $"{failedAttempt.StandardError}\n{failedAttempt.StandardOutput}".Trim();
        if (!string.IsNullOrWhiteSpace(combined))
        {
            string[] lines = combined.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string? diagnosticLine = lines.FirstOrDefault(l =>
                l.Contains("error ", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("FAIL ", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(diagnosticLine))
            {
                return diagnosticLine.Length <= 220 ? diagnosticLine : diagnosticLine[..220] + "...";
            }
        }

        return failedAttempt.FailureSummary ?? $"Exit code {failedAttempt.ExitCode}";
    }

    private static string NormalizeStackName(string rawStack)
    {
        if (string.IsNullOrWhiteSpace(rawStack))
        {
            return "All";
        }

        if (rawStack.Contains("DotNet", StringComparison.OrdinalIgnoreCase) ||
            rawStack.Contains("C#", StringComparison.OrdinalIgnoreCase))
        {
            return "DotNet";
        }

        if (rawStack.Contains("Java", StringComparison.OrdinalIgnoreCase) ||
            rawStack.Contains("Spring", StringComparison.OrdinalIgnoreCase) ||
            rawStack.Contains("Gradle", StringComparison.OrdinalIgnoreCase) ||
            rawStack.Contains("Maven", StringComparison.OrdinalIgnoreCase))
        {
            return "JavaGradle";
        }

        if (rawStack.Contains("Python", StringComparison.OrdinalIgnoreCase))
        {
            return "Python";
        }

        if (rawStack.Contains("Node", StringComparison.OrdinalIgnoreCase) ||
            rawStack.Contains("TypeScript", StringComparison.OrdinalIgnoreCase))
        {
            return "NodeJs";
        }

        if (rawStack.Contains("DevOps", StringComparison.OrdinalIgnoreCase) ||
            rawStack.Contains("Infra", StringComparison.OrdinalIgnoreCase))
        {
            return "DevOpsInfra";
        }

        return rawStack.Trim();
    }

    private static bool IsStackMatch(string ruleStack, string targetStack) =>
        string.Equals(ruleStack, "All", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(targetStack, "All", StringComparison.OrdinalIgnoreCase) ||
        ruleStack.Contains(targetStack, StringComparison.OrdinalIgnoreCase) ||
        targetStack.Contains(ruleStack, StringComparison.OrdinalIgnoreCase);

    private static async Task WriteFileAtomicallyAsync(
        string targetFilePath,
        string content,
        CancellationToken cancellationToken)
    {
        string? parentDir = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        string tempFilePath = $"{targetFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempFilePath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempFilePath, targetFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch
                {
                    // Ignore best-effort temp cleanup
                }
            }
        }
    }
}
