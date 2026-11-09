using System.Text.Json;
using AutonomousAgentHub.Core;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;
using AutonomousAgentHub.Core.Pipeline;
using AutonomousAgentHub.Core.Serialization;
using Xunit;

namespace AutonomousAgentHub.Tests;

public sealed class SelfEvolutionMemorySkillAndMcpTests
{
    [Fact]
    public async Task ExperientialMemoryManager_DistillsStage5Stage6AndPrRejectionLessons_AndPersistsAtomically()
    {
        string tempStateDir = Path.Combine(Path.GetTempPath(), $"aah-rsi-test-{Guid.NewGuid():N}");
        try
        {
            ExperientialMemoryManager memoryManager = new(tempStateDir);

            // 1. Stage 5 failure distillation
            TestExecutionAttempt failedAttempt = new()
            {
                AttemptNumber = 1,
                BuildSucceeded = false,
                TestsSucceeded = false,
                ExitCode = 1,
                StandardError = "src/OrderService.cs(42,15): error CS0103: The name 'CancellationToken' does not exist in the current context",
                FailureSummary = "Build failed with exit code 1."
            };

            await memoryManager.RecordStage5FailureLessonAsync(
                "TatoTopuria/EventHub.Booking",
                "DotNet",
                "Add CancellationToken to OrderService",
                failedAttempt);

            // 2. Stage 6 Auditor rejection distillation
            Stage6AuditVerdictArtifact rejectedVerdict = new()
            {
                Approved = false,
                QualityScore = 70,
                BlockingIssues = ["Read-only EF Core query in BookingQueries is missing .AsNoTracking()."],
                ExecutiveSummary = "Rejected due to missing AsNoTracking()."
            };

            await memoryManager.RecordStage6RejectionLessonAsync(
                "TatoTopuria/EventHub.Booking",
                "DotNet",
                "Optimize BookingQueries",
                rejectedVerdict);

            // 3. Closed-Unmerged PR rejection distillation
            LedgerProposalEntry closedPr = new()
            {
                TaskTitle = "Refactor internal logging format",
                TaskSlug = "refactor-internal-logging-format",
                PullRequestNumber = 19,
                OutcomeStatus = "CLOSED_UNMERGED",
                ReviewSummary = "Unwanted logging churn"
            };

            await memoryManager.RecordRejectedPullRequestLessonAsync(
                "TatoTopuria/EventHub.Booking",
                "DotNet",
                closedPr);

            ExperientialMemoryState reloaded = await memoryManager.LoadMemoryAsync();
            Assert.True(reloaded.StageFailureCounts["Stage5Tester"] >= 1);
            Assert.True(reloaded.StageFailureCounts["Stage6Auditor"] >= 1);
            Assert.Contains(reloaded.ForbiddenAntiPatterns, a => a.PatternDescription.Contains("error CS0103", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(reloaded.ForbiddenAntiPatterns, a => a.PatternDescription.Contains("AsNoTracking()", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(reloaded.ForbiddenAntiPatterns, a => a.PatternDescription.Contains("Unmerged PR #19", StringComparison.OrdinalIgnoreCase));

            string stage3Injection = memoryManager.BuildStagePromptInjection("Stage3Planner", "DotNet");
            Assert.Contains("Experiential Memory Guardrails", stage3Injection);
            Assert.Contains("Forbidden Anti-Patterns", stage3Injection);

            // Verify source-generated JSON serialization compatibility
            string rawJson = JsonSerializer.Serialize(reloaded, HubJsonSerializerContext.Default.ExperientialMemoryState);
            ExperientialMemoryState? roundTrip = JsonSerializer.Deserialize(rawJson, HubJsonSerializerContext.Default.ExperientialMemoryState);
            Assert.NotNull(roundTrip);
            Assert.Equal(reloaded.LearnedHeuristics.Count, roundTrip.LearnedHeuristics.Count);
        }
        finally
        {
            if (Directory.Exists(tempStateDir))
            {
                Directory.Delete(tempStateDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExperientialMemoryManager_SynchronizeOpenPullRequests_UpdatesLedgerAndRecordsClosedUnmergedLesson()
    {
        string tempStateDir = Path.Combine(Path.GetTempPath(), $"aah-prsync-test-{Guid.NewGuid():N}");
        try
        {
            StateLedgerManager stateManager = new(tempStateDir);
            ExperientialMemoryManager memoryManager = new(tempStateDir);
            RepositoryEntry repo = new()
            {
                Owner = "TatoTopuria",
                Name = "EventHub.Payment",
                PrimaryStack = "DotNet"
            };

            await stateManager.RecordCompletedProposalAsync(repo, new LedgerProposalEntry
            {
                TaskTitle = "Harden idempotency guard",
                TaskSlug = "harden-idempotency-guard",
                PullRequestNumber = 55,
                OutcomeStatus = "OPEN"
            });

            StubOutcomePrClient prClient = new(new PullRequestOutcomeStatus(
                Number: 55,
                OutcomeStatus: "CLOSED_UNMERGED",
                ReviewSummary: "Superseded by upstream changes"));

            await memoryManager.SynchronizeOpenPullRequestsAsync(repo, stateManager, prClient);

            RepoStateLedger updatedLedger = await stateManager.LoadRepoLedgerAsync(repo);
            Assert.Single(updatedLedger.CompletedProposals);
            Assert.Equal("CLOSED_UNMERGED", updatedLedger.CompletedProposals[0].OutcomeStatus);

            ExperientialMemoryState memory = await memoryManager.LoadMemoryAsync();
            Assert.Equal(1, memory.PrOutcomeMetrics.ClosedUnmergedCount);
            Assert.Contains(memory.ForbiddenAntiPatterns, a => a.PatternDescription.Contains("PR #55", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempStateDir))
            {
                Directory.Delete(tempStateDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("Stage1Discovery", "DotNet", "Stage 1", ".NET 8")]
    [InlineData("Stage2Finder", "JavaGradle", "Stage 2", "Spring Boot 3")]
    [InlineData("Stage3Planner", "Python", "Stage 3", "Python 3.11+")]
    [InlineData("Stage4Executor", "NodeJs", "Stage 4", "Node.js 20 LTS")]
    [InlineData("Stage6Auditor", "DevOpsInfra", "Stage 6", "Cloud-Native DevOps")]
    public void SkillRegistry_ComposesStageAndStackSkillPacksDynamically(
        string stageKey,
        string detectedStack,
        string expectedStageMarker,
        string expectedStackMarker)
    {
        string skillsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "skills"));

        SkillRegistry registry = new(skillsPath);
        string composed = registry.ComposeStageSystemInstruction(
            "Base Stage Instruction.",
            stageKey,
            detectedStack);

        Assert.Contains("Base Stage Instruction.", composed);
        Assert.Contains(expectedStageMarker, composed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedStackMarker, composed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void McpToolManager_EnforcesStageSandboxing_WorkspacePathConfinement_AndZeroCostStdioPolicy()
    {
        string mcpConfigPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "config", "mcp-servers.json"));

        McpRegistryConfig config = McpToolManager.LoadFromFile(mcpConfigPath);
        McpToolManager manager = new(config);

        // Stage 1 can read filesystem and git, but cannot write
        Assert.True(manager.IsToolAllowedForStage("filesystem", "Stage1Discovery", requiresWrite: false));
        Assert.False(manager.IsToolAllowedForStage("filesystem", "Stage1Discovery", requiresWrite: true));
        Assert.True(manager.IsToolAllowedForStage("git", "Stage1Discovery", requiresWrite: false));

        // Stage 4 can write to filesystem inside workspace
        Assert.True(manager.IsToolAllowedForStage("filesystem", "Stage4Executor", requiresWrite: true));
        Assert.False(manager.IsToolAllowedForStage("git", "Stage4Executor", requiresWrite: true));

        // Workspace path confinement
        string tempWorkspace = Path.Combine(Path.GetTempPath(), "aah-mcp-workspace");
        string validPath = manager.ValidateWorkspaceScopedPath(tempWorkspace, "src/Domain/Order.cs");
        Assert.StartsWith(Path.GetFullPath(tempWorkspace), validPath, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<InvalidOperationException>(() =>
            manager.ValidateWorkspaceScopedPath(tempWorkspace, "../../etc/passwd"));

        // Non-stdio transport rejection
        McpRegistryConfig invalidTransportConfig = new()
        {
            Servers =
            [
                new McpServerDescriptor
                {
                    Name = "remote-paid",
                    Transport = "sse",
                    Enabled = true,
                    ZeroCostVerified = true
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() => McpToolManager.ValidateSecurityPolicy(invalidTransportConfig));
    }

    [Fact]
    public void StackTraceDiagnosticParser_ExtractsCSharpJavaPythonAndTypeScriptErrors()
    {
        TestExecutionAttempt attempt = new()
        {
            AttemptNumber = 1,
            BuildSucceeded = false,
            TestsSucceeded = false,
            ExitCode = 1,
            StandardOutput =
                """
                Build started...
                src/Booking/BookingHandler.cs(88,19): error CS1061: 'Booking' does not contain a definition for 'Confirm'
                src/main/java/com/wisewallet/Ledger.java:45: error: cannot find symbol
                FAILED tests/test_triage.py::test_infant_fever - AssertionError: expected HIGH urgency
                src/gateway/router.ts(12,5): error TS2322: Type 'string' is not assignable to type 'number'.
                """,
            StandardError = "[ERROR] Failed to execute goal org.apache.maven.plugins:maven-compiler-plugin",
            FailureSummary = "Build failed."
        };

        List<string> diagnostics = StackTraceDiagnosticParser.ExtractKeyDiagnostics(attempt.StandardOutput, attempt.StandardError);
        Assert.Contains(diagnostics, d => d.Contains("error CS1061", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Contains("Ledger.java:45: error:", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Contains("FAILED tests/test_triage.py", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Contains("error TS2322", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Contains("[ERROR]", StringComparison.Ordinal));

        string trace = StackTraceDiagnosticParser.BuildFocusedFailureTrace(attempt);
        Assert.Contains("EXTRACTED HIGH-PRIORITY COMPILER / TEST DIAGNOSTICS", trace);
    }

    [Fact]
    public void PolyglotBuildTestRunner_DetectsDevOpsInfraStack()
    {
        string tempWorkspace = Path.Combine(Path.GetTempPath(), $"aah-devops-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempWorkspace, "deploy", "docker"));
            File.WriteAllText(Path.Combine(tempWorkspace, "docker-compose.yml"), "services: {}");

            PolyglotBuildTestRunner runner = new();
            RepositoryEntry entry = new()
            {
                Owner = "TatoTopuria",
                Name = "wisewallet-infra",
                PrimaryStack = "DevOpsInfra",
                CustomBuildCommand = "docker compose config",
                CustomTestCommand = "echo 'ok'"
            };

            PolyglotStackDescriptor descriptor = runner.DetectStack(tempWorkspace, entry);
            Assert.Equal(PolyglotStackType.DevOpsInfra, descriptor.StackType);
            Assert.Equal("docker compose config", descriptor.BuildCommand);
        }
        finally
        {
            if (Directory.Exists(tempWorkspace))
            {
                Directory.Delete(tempWorkspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Stage3Planner_IncludesAbortedAttempts_AndStage4Executor_RejectsTruncationPlaceholders()
    {
        Stage1DiscoveryArtifact discovery = new()
        {
            RepositoryFullName = "TatoTopuria/EventHub.Booking",
            DetectedStack = "DotNet",
            CriticalBusinessInvariants = ["Preserve seat reservation atomicity"]
        };

        Stage2FinderArtifact finder = new()
        {
            RepositoryFullName = discovery.RepositoryFullName,
            EvidenceBlocks =
            [
                new EvidenceBlock
                {
                    Id = "EV-01",
                    IssuingAuthority = "Microsoft Learn",
                    DocumentTitle = "EF Core Tracking",
                    PublicationYear = 2025,
                    OfficialUrl = "https://learn.microsoft.com/en-us/ef/core/querying/tracking",
                    GoverningExcerpt = "Use AsNoTracking for read-only queries.",
                    RelevanceToRepo = "Direct"
                }
            ]
        };

        RepoStateLedger ledger = new()
        {
            RepositoryFullName = discovery.RepositoryFullName,
            AbortedAttempts =
            [
                new LedgerAbortedEntry
                {
                    TaskTitle = "Broken Saga State Machine Refactor",
                    StageAbortedAt = "Stage5Tester",
                    Reason = "Failed 3 self-healing attempts due to missing state transition."
                }
            ]
        };

        string prompt = Stage3PlannerAgent.BuildPlannerUserPrompt(discovery, finder, ledger);
        Assert.Contains("Previously Aborted or Rejected Tasks", prompt);
        Assert.Contains("Broken Saga State Machine Refactor", prompt);
        Assert.Contains("Stage5Tester", prompt);

        // Stage 4 placeholder validation
        List<FileMutation> truncatedMutations =
        [
            new FileMutation
            {
                RelativePath = "src/BookingService.cs",
                Operation = "Modify",
                UpdatedContent = "public class BookingService {\n // ... existing code ...\n}"
            }
        ];

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            Stage4ExecutorAgent.ValidateMutationsBeforeWrite(truncatedMutations));
        Assert.Contains("truncation placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage6AuditorAgent_BuildPrioritizedDiffExcerpt_PreservesAllModifiedFileHeadersAndTestHunks()
    {
        string largeFile1 = "diff --git a/src/ProductionService.cs b/src/ProductionService.cs\n" + new string('+', 12000) + "\n";
        string largeFile2 = "diff --git a/tests/ProductionServiceTests.cs b/tests/ProductionServiceTests.cs\n+ [Fact] public void Test_EdgeCase() {}\n" + new string('+', 8000);
        string combinedDiff = largeFile1 + largeFile2;

        string prioritized = Stage6AuditorAgent.BuildPrioritizedDiffExcerpt(combinedDiff, maxChars: 6000);

        Assert.True(prioritized.Length <= 6000);
        Assert.Contains("MODIFIED FILES SUMMARY", prioritized);
        Assert.Contains("diff --git a/src/ProductionService.cs b/src/ProductionService.cs", prioritized);
        Assert.Contains("diff --git a/tests/ProductionServiceTests.cs b/tests/ProductionServiceTests.cs", prioritized);
        Assert.Contains("Test_EdgeCase", prioritized);
    }

    [Fact]
    public void Program_ParseArguments_SupportsSelfEvolveAuditFlag()
    {
        Program.CliOptions options = Program.ParseArguments(["--self-evolve-audit"]);
        Assert.True(options.IsSelfEvolveAudit);
    }

    private sealed class StubOutcomePrClient(PullRequestOutcomeStatus outcome) : IGitHubPullRequestClient
    {
        public Task<PullRequestCreationResult> CreatePullRequestAsync(
            RepositoryEntry repository,
            string headBranch,
            Stage1DiscoveryArtifact discovery,
            Stage2FinderArtifact finder,
            Stage3PlannerArtifact plan,
            Stage4ExecutionArtifact execution,
            Stage5TestReportArtifact testReport,
            Stage6AuditVerdictArtifact auditVerdict,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PullRequestOutcomeStatus?> GetPullRequestOutcomeAsync(
            RepositoryEntry repository,
            int pullRequestNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PullRequestOutcomeStatus?>(outcome);
    }
}

