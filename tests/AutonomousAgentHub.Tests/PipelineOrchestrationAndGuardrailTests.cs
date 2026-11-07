using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AutonomousAgentHub.Core;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;
using AutonomousAgentHub.Core.Pipeline;
using AutonomousAgentHub.Core.Serialization;
using Xunit;

namespace AutonomousAgentHub.Tests;

public sealed class PipelineOrchestrationAndGuardrailTests
{
    [Fact]
    public async Task SevenStageOrchestrator_FullSevenStageHappyPath_OpensPrUpdatesLedgerAndEmitsTelemetry()
    {
        using TempTestDirectory temp = new();
        HubConfig config = CreateTestHubConfig(temp.WorkspaceRootPath);
        StateLedgerManager stateManager = new(temp.StatePath);
        PipelineTelemetryLogger telemetryLogger = new(temp.StatePath);
        FakeGitWorkspaceManager gitWorkspace = new(temp.WorkspaceRootPath);
        FakeBuildTestRunner buildRunner = new(passOnAttempt: 1);
        FakePullRequestClient prClient = new();
        FakeGeminiClient geminiClient = FakeGeminiClient.CreateDefaultSuccessScenario();

        SevenStageOrchestrator orchestrator = CreateOrchestrator(
            config,
            stateManager,
            gitWorkspace,
            buildRunner,
            prClient,
            geminiClient,
            telemetryLogger);

        PipelineExecutionSummary summary = await orchestrator.RunAsync(isDryRun: false);

        Assert.True(summary.Succeeded);
        Assert.Equal("Stage7Deployer", summary.FinalStageReached);
        Assert.NotNull(summary.PullRequest);
        Assert.Equal(101, summary.PullRequest.Number);

        RepoStateLedger ledger = await stateManager.LoadRepoLedgerAsync(config.Repositories[0]);
        Assert.Single(ledger.CompletedProposals);
        Assert.Equal(101, ledger.CompletedProposals[0].PullRequestNumber);

        List<PipelineTelemetryRecord> telemetry = await telemetryLogger.ReadAllRecordsAsync();
        Assert.Single(telemetry);
        Assert.True(telemetry[0].Succeeded);
        Assert.Equal("Stage7Deployer", telemetry[0].FinalStageReached);
        Assert.Equal(94, telemetry[0].AuditQualityScore);
    }

    [Fact]
    public async Task SevenStageOrchestrator_DryRunMode_DoesNotCloneOrModifyAndEmitsDryRunTelemetry()
    {
        using TempTestDirectory temp = new();
        HubConfig config = CreateTestHubConfig(temp.WorkspaceRootPath);
        StateLedgerManager stateManager = new(temp.StatePath);
        PipelineTelemetryLogger telemetryLogger = new(temp.StatePath);
        FakeGitWorkspaceManager gitWorkspace = new(temp.WorkspaceRootPath);
        FakeBuildTestRunner buildRunner = new(passOnAttempt: 1);
        FakePullRequestClient prClient = new();
        FakeGeminiClient geminiClient = FakeGeminiClient.CreateDefaultSuccessScenario();

        SevenStageOrchestrator orchestrator = CreateOrchestrator(
            config,
            stateManager,
            gitWorkspace,
            buildRunner,
            prClient,
            geminiClient,
            telemetryLogger);

        PipelineExecutionSummary summary = await orchestrator.RunAsync(isDryRun: true);

        Assert.True(summary.Succeeded);
        Assert.True(summary.IsDryRun);
        Assert.Equal("DryRunVerified", summary.FinalStageReached);
        Assert.Equal(0, gitWorkspace.CloneCount);

        List<PipelineTelemetryRecord> telemetry = await telemetryLogger.ReadAllRecordsAsync();
        Assert.Single(telemetry);
        Assert.True(telemetry[0].IsDryRun);
        Assert.Equal("DryRunVerified", telemetry[0].FinalStageReached);
    }

    [Fact]
    public async Task SevenStageOrchestrator_HaltsAtStage2_WhenFinderReturnsNoVerifiableEvidence()
    {
        using TempTestDirectory temp = new();
        HubConfig config = CreateTestHubConfig(temp.WorkspaceRootPath);
        StateLedgerManager stateManager = new(temp.StatePath);
        FakeGitWorkspaceManager gitWorkspace = new(temp.WorkspaceRootPath);
        FakeBuildTestRunner buildRunner = new(passOnAttempt: 1);
        FakePullRequestClient prClient = new();
        FakeGeminiClient geminiClient = FakeGeminiClient.CreateDefaultSuccessScenario();

        // Override Stage 2 with unofficial/empty evidence
        geminiClient.Stage2Response = new Stage2FinderArtifact
        {
            RepositoryFullName = "TatoTopuria/dotnet-clean-architecture-sample",
            TargetStack = "DotNet",
            EvidenceBlocks =
            [
                new EvidenceBlock
                {
                    Id = "EV-BAD",
                    IssuingAuthority = "Random Blog",
                    DocumentTitle = "Blog Post",
                    PublicationYear = 2025,
                    OfficialUrl = "https://medium.com/@author/post",
                    GoverningExcerpt = "Unverified opinion.",
                    RelevanceToRepo = "None"
                }
            ]
        };

        SevenStageOrchestrator orchestrator = CreateOrchestrator(
            config,
            stateManager,
            gitWorkspace,
            buildRunner,
            prClient,
            geminiClient,
            telemetryLogger: null);

        PipelineExecutionSummary summary = await orchestrator.RunAsync(isDryRun: false);

        Assert.False(summary.Succeeded);
        Assert.Equal("Stage2Finder", summary.FinalStageReached);

        RepoStateLedger ledger = await stateManager.LoadRepoLedgerAsync(config.Repositories[0]);
        Assert.Empty(ledger.CompletedProposals);
        Assert.Single(ledger.AbortedAttempts);
        Assert.Equal("Stage2Finder", ledger.AbortedAttempts[0].StageAbortedAt);
    }

    [Fact]
    public async Task SevenStageOrchestrator_HaltsAtStage5AndResetsWorkspace_WhenAllSelfHealingAttemptsFail()
    {
        using TempTestDirectory temp = new();
        HubConfig config = CreateTestHubConfig(temp.WorkspaceRootPath);
        StateLedgerManager stateManager = new(temp.StatePath);
        FakeGitWorkspaceManager gitWorkspace = new(temp.WorkspaceRootPath);
        FakeBuildTestRunner buildRunner = new(passOnAttempt: 99); // Never passes within 3 attempts
        FakePullRequestClient prClient = new();
        FakeGeminiClient geminiClient = FakeGeminiClient.CreateDefaultSuccessScenario();

        SevenStageOrchestrator orchestrator = CreateOrchestrator(
            config,
            stateManager,
            gitWorkspace,
            buildRunner,
            prClient,
            geminiClient,
            telemetryLogger: null);

        PipelineExecutionSummary summary = await orchestrator.RunAsync(isDryRun: false);

        Assert.False(summary.Succeeded);
        Assert.Equal("Stage5Tester", summary.FinalStageReached);
        Assert.Equal(1, gitWorkspace.ResetCount);

        RepoStateLedger ledger = await stateManager.LoadRepoLedgerAsync(config.Repositories[0]);
        Assert.Single(ledger.AbortedAttempts);
        Assert.Equal("Stage5Tester", ledger.AbortedAttempts[0].StageAbortedAt);
    }

    [Fact]
    public async Task SevenStageOrchestrator_HaltsAtStage6AndResetsWorkspace_WhenAuditorRejectsScore()
    {
        using TempTestDirectory temp = new();
        HubConfig config = CreateTestHubConfig(temp.WorkspaceRootPath);
        StateLedgerManager stateManager = new(temp.StatePath);
        FakeGitWorkspaceManager gitWorkspace = new(temp.WorkspaceRootPath);
        FakeBuildTestRunner buildRunner = new(passOnAttempt: 1);
        FakePullRequestClient prClient = new();
        FakeGeminiClient geminiClient = FakeGeminiClient.CreateDefaultSuccessScenario();
        geminiClient.Stage6Response = new Stage6AuditVerdictArtifact
        {
            Approved = false,
            QualityScore = 62,
            BlockingIssues = ["Missing null-safety guard in edge case."],
            ExecutiveSummary = "Score below 85 threshold."
        };

        SevenStageOrchestrator orchestrator = CreateOrchestrator(
            config,
            stateManager,
            gitWorkspace,
            buildRunner,
            prClient,
            geminiClient,
            telemetryLogger: null);

        PipelineExecutionSummary summary = await orchestrator.RunAsync(isDryRun: false);

        Assert.False(summary.Succeeded);
        Assert.Equal("Stage6Auditor", summary.FinalStageReached);
        Assert.Equal(1, gitWorkspace.ResetCount);
    }

    [Fact]
    public async Task Stage5TesterAgent_SelfHealsOnSecondAttempt_AfterInitialFailure()
    {
        using TempTestDirectory temp = new();
        HubConfig config = CreateTestHubConfig(temp.WorkspaceRootPath);
        RepositoryEntry repo = config.Repositories[0];
        FakeGitWorkspaceManager gitWorkspace = new(temp.WorkspaceRootPath);
        string workspacePath = await gitWorkspace.CloneOrPrepareWorkspaceAsync(repo);

        FakeBuildTestRunner buildRunner = new(passOnAttempt: 2);
        FakeGeminiClient geminiClient = FakeGeminiClient.CreateDefaultSuccessScenario();
        Stage4ExecutorAgent executor = new(geminiClient);
        Stage5TesterAgent tester = new(buildRunner, gitWorkspace, executor, maxSelfHealingAttempts: 3);

        (Stage5TestReportArtifact report, Stage4ExecutionArtifact finalExec) =
            await tester.ExecuteWithSelfHealingAsync(
                repo,
                workspacePath,
                geminiClient.Stage1Response,
                geminiClient.Stage3Response,
                geminiClient.Stage4Response);

        Assert.True(report.Passed);
        Assert.Equal(2, report.TotalAttempts);
        Assert.False(report.HardResetTriggered);
        Assert.Equal(2, finalExec.AttemptNumber);
        Assert.Equal(0, gitWorkspace.ResetCount);
    }

    [Fact]
    public async Task Stage4ExecutorAgent_ApplyFileMutations_PreventsPathTraversalOutsideWorkspace()
    {
        using TempTestDirectory temp = new();
        List<FileMutation> maliciousMutations =
        [
            new FileMutation
            {
                RelativePath = "../../escaped-file.txt",
                Operation = "Create",
                UpdatedContent = "compromised",
                SummaryOfChanges = "Path traversal attempt"
            }
        ];

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Stage4ExecutorAgent.ApplyFileMutationsToWorkspaceAsync(temp.WorkspaceRootPath, maliciousMutations));

        Assert.Contains("Security violation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stage6AuditorAgent_RejectsDiffContainingHardcodedSecretToken()
    {
        using TempTestDirectory temp = new();
        FakeGitWorkspaceManager gitWorkspace = new(temp.WorkspaceRootPath)
        {
            UnifiedDiffToReturn = "+ string token = \"ghp_1234567890abcdefSECRET\";\n"
        };
        FakeGeminiClient geminiClient = FakeGeminiClient.CreateDefaultSuccessScenario();
        Stage6AuditorAgent auditor = new(geminiClient, gitWorkspace, minimumQualityScore: 85);

        Stage5TestReportArtifact passedTestReport = new()
        {
            Passed = true,
            TotalAttempts = 1,
            DetectedStack = "DotNet"
        };

        Stage6AuditVerdictArtifact verdict = await auditor.ExecuteAsync(
            temp.WorkspaceRootPath,
            geminiClient.Stage1Response,
            geminiClient.Stage2Response,
            geminiClient.Stage3Response,
            geminiClient.Stage4Response,
            passedTestReport);

        Assert.False(verdict.Approved);
        Assert.Equal(0, verdict.QualityScore);
        Assert.Contains(verdict.BlockingIssues, issue => issue.Contains("ghp_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stage6AuditorAgent_RejectsEmptyUnifiedDiff()
    {
        using TempTestDirectory temp = new();
        FakeGitWorkspaceManager gitWorkspace = new(temp.WorkspaceRootPath)
        {
            UnifiedDiffToReturn = "   "
        };
        FakeGeminiClient geminiClient = FakeGeminiClient.CreateDefaultSuccessScenario();
        Stage6AuditorAgent auditor = new(geminiClient, gitWorkspace, minimumQualityScore: 85);

        Stage5TestReportArtifact passedTestReport = new()
        {
            Passed = true,
            TotalAttempts = 1,
            DetectedStack = "DotNet"
        };

        Stage6AuditVerdictArtifact verdict = await auditor.ExecuteAsync(
            temp.WorkspaceRootPath,
            geminiClient.Stage1Response,
            geminiClient.Stage2Response,
            geminiClient.Stage3Response,
            geminiClient.Stage4Response,
            passedTestReport);

        Assert.False(verdict.Approved);
        Assert.Equal(0, verdict.DiffLineCount);
        Assert.NotEmpty(verdict.BlockingIssues);
    }

    [Fact]
    public void GeminiFreeTierClient_TryParseRetryAfterHeader_ParsesDeltaSecondsAndHttpDate()
    {
        using HttpResponseMessage deltaResponse = new(HttpStatusCode.TooManyRequests);
        deltaResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(25));

        TimeSpan? parsedDelta = GeminiFreeTierClient.TryParseRetryAfterHeader(deltaResponse);
        Assert.NotNull(parsedDelta);
        Assert.Equal(25, parsedDelta.Value.TotalSeconds);

        using HttpResponseMessage cappedResponse = new(HttpStatusCode.TooManyRequests);
        cappedResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(600));

        TimeSpan? parsedCapped = GeminiFreeTierClient.TryParseRetryAfterHeader(cappedResponse);
        Assert.NotNull(parsedCapped);
        Assert.Equal(120, parsedCapped.Value.TotalSeconds);

        using HttpResponseMessage emptyResponse = new(HttpStatusCode.TooManyRequests);
        Assert.Null(GeminiFreeTierClient.TryParseRetryAfterHeader(emptyResponse));
    }

    [Fact]
    public void HubConfig_Parse_SupportsBothObjectAndTopLevelArrayJsonFormats()
    {
        const string objectJson =
            """
            {
              "hubName": "CustomHub",
              "minimumAuditScore": 90,
              "repositories": [
                { "owner": "TatoTopuria", "name": "service-a", "enabled": true }
              ]
            }
            """;

        HubConfig parsedObject = HubConfig.Parse(objectJson);
        Assert.Equal("CustomHub", parsedObject.HubName);
        Assert.Equal(90, parsedObject.MinimumAuditScore);
        Assert.Single(parsedObject.Repositories);

        const string arrayJson =
            """
            [
              { "owner": "TatoTopuria", "name": "service-b", "enabled": true }
            ]
            """;

        HubConfig parsedArray = HubConfig.Parse(arrayJson);
        Assert.Single(parsedArray.Repositories);
        Assert.Equal("TatoTopuria/service-b", parsedArray.Repositories[0].FullName);
    }

    [Fact]
    public void Program_ParseArguments_ParsesDryRunRepoConfigAndStateFlags()
    {
        Program.CliOptions options = Program.ParseArguments(
        [
            "--dry-run",
            "--repo", "TatoTopuria/python-clinical-analytics-api",
            "--config", "/custom/repositories.json",
            "--state-dir", "/custom/state"
        ]);

        Assert.True(options.IsDryRun);
        Assert.Equal("TatoTopuria/python-clinical-analytics-api", options.RepositoryOverride);
        Assert.Equal("/custom/repositories.json", options.ConfigPath);
        Assert.Equal("/custom/state", options.StateDirectoryPath);
    }

    [Fact]
    public void HubJsonSerializerContext_SourceGeneratedSerialization_RoundTripsAllContracts()
    {
        RotationState state = new()
        {
            LastProcessedIndex = 2,
            LastRepo = "TatoTopuria/python-clinical-analytics-api",
            TotalRuns = 7
        };

        string json = JsonSerializer.Serialize(state, HubJsonSerializerContext.Default.RotationState);
        RotationState? deserialized = JsonSerializer.Deserialize(json, HubJsonSerializerContext.Default.RotationState);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.LastProcessedIndex);
        Assert.Equal("TatoTopuria/python-clinical-analytics-api", deserialized.LastRepo);
        Assert.Equal(7, deserialized.TotalRuns);
    }

    private static HubConfig CreateTestHubConfig(string workspaceRootPath) =>
        new()
        {
            HubName = "TestAutonomousAgentHub",
            WorkspaceRootPath = workspaceRootPath,
            MinimumAuditScore = 85,
            MaxSelfHealingAttempts = 3,
            Repositories =
            [
                new RepositoryEntry
                {
                    Owner = "TatoTopuria",
                    Name = "dotnet-clean-architecture-sample",
                    DefaultBranch = "main",
                    PrimaryStack = "DotNet",
                    Domain = "Enterprise Microservices",
                    Enabled = true,
                    WhitelistedAuthorities = ["Microsoft Learn", "OWASP Foundation"]
                }
            ]
        };

    private static SevenStageOrchestrator CreateOrchestrator(
        HubConfig config,
        StateLedgerManager stateManager,
        IGitWorkspaceManager gitWorkspace,
        IBuildTestRunner buildRunner,
        IGitHubPullRequestClient prClient,
        IGeminiClient geminiClient,
        PipelineTelemetryLogger? telemetryLogger)
    {
        Stage1DiscoveryAgent stage1 = new(geminiClient, buildRunner, gitWorkspace);
        Stage2FinderAgent stage2 = new(geminiClient);
        Stage3PlannerAgent stage3 = new(geminiClient);
        Stage4ExecutorAgent stage4 = new(geminiClient);
        Stage5TesterAgent stage5 = new(buildRunner, gitWorkspace, stage4, config.MaxSelfHealingAttempts);
        Stage6AuditorAgent stage6 = new(geminiClient, gitWorkspace, config.MinimumAuditScore);
        Stage7DeployerAgent stage7 = new(gitWorkspace, prClient, stateManager, config.MinimumAuditScore);

        return new SevenStageOrchestrator(
            config,
            stateManager,
            gitWorkspace,
            stage1,
            stage2,
            stage3,
            stage4,
            stage5,
            stage6,
            stage7,
            telemetryLogger);
    }

    private sealed class FakeGeminiClient : IGeminiClient
    {
        public Stage1DiscoveryArtifact Stage1Response { get; set; } = new();
        public Stage2FinderArtifact Stage2Response { get; set; } = new();
        public Stage3PlannerArtifact Stage3Response { get; set; } = new();
        public Stage4ExecutionArtifact Stage4Response { get; set; } = new();
        public Stage6AuditVerdictArtifact Stage6Response { get; set; } = new();

        public static FakeGeminiClient CreateDefaultSuccessScenario() =>
            new()
            {
                Stage1Response = new Stage1DiscoveryArtifact
                {
                    RepositoryFullName = "TatoTopuria/dotnet-clean-architecture-sample",
                    DetectedStack = "DotNet",
                    DomainPurpose = "Enterprise Microservices",
                    ArchitecturalStyle = "Clean Architecture",
                    CriticalBusinessInvariants = ["Preserve API contracts"],
                    CandidateFilesForInspection =
                    [
                        new InspectedFileSnapshot
                        {
                            RelativePath = "src/SampleService.cs",
                            ReasonForSelection = "Core service",
                            ContentExcerpt = "public sealed class SampleService {}"
                        }
                    ]
                },
                Stage2Response = new Stage2FinderArtifact
                {
                    RepositoryFullName = "TatoTopuria/dotnet-clean-architecture-sample",
                    TargetStack = "DotNet",
                    DomainFocus = "Enterprise Microservices",
                    SearchQueriesExecuted = ["Microsoft Learn CancellationToken async"],
                    EvidenceBlocks =
                    [
                        new EvidenceBlock
                        {
                            Id = "EV-01",
                            IssuingAuthority = "Microsoft Learn",
                            DocumentTitle = "Cancellation in Managed Threads",
                            PublicationYear = 2025,
                            OfficialUrl = "https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads",
                            GoverningExcerpt = "Propagate CancellationToken to all asynchronous operations.",
                            RelevanceToRepo = "Hardens async cancellation handling."
                        }
                    ]
                },
                Stage3Response = new Stage3PlannerArtifact
                {
                    TaskTitle = "Propagate CancellationToken in SampleService",
                    TaskSlug = "propagate-cancellationtoken-sampleservice",
                    ImprovementCategory = "Resilience",
                    EngineeringAndBusinessRationale = "Prevents orphaned async work on request abort.",
                    LinkedEvidenceIds = ["EV-01"],
                    TargetFiles =
                    [
                        new PlannedFileChange
                        {
                            RelativePath = "src/SampleService.cs",
                            Operation = "Modify",
                            ChangeDescription = "Add CancellationToken parameter",
                            IsTestFile = false
                        }
                    ],
                    AcceptanceCriteria = ["CancellationToken propagated cleanly"]
                },
                Stage4Response = new Stage4ExecutionArtifact
                {
                    TaskTitle = "Propagate CancellationToken in SampleService",
                    AttemptNumber = 1,
                    ImplementationSummary = "Added CancellationToken support.",
                    FileMutations =
                    [
                        new FileMutation
                        {
                            RelativePath = "src/SampleService.cs",
                            Operation = "Modify",
                            UpdatedContent = "public sealed class SampleService { public Task RunAsync(CancellationToken ct = default) => Task.CompletedTask; }",
                            SummaryOfChanges = "Added RunAsync with CancellationToken",
                            IsTestFile = false
                        }
                    ]
                },
                Stage6Response = new Stage6AuditVerdictArtifact
                {
                    Approved = true,
                    QualityScore = 94,
                    ComplianceChecklist =
                    [
                        new AuditChecklistItem
                        {
                            Criterion = "100% Green Build & Tests",
                            Passed = true,
                            Notes = "All tests passed."
                        }
                    ],
                    BlockingIssues = [],
                    ExecutiveSummary = "Approved with high confidence."
                }
            };

        public Task<T> GenerateStructuredAsync<T>(
            string systemInstruction,
            string userPrompt,
            bool useGoogleSearchGrounding = false,
            CancellationToken cancellationToken = default) where T : class
        {
            object response = typeof(T) switch
            {
                Type t when t == typeof(Stage1DiscoveryArtifact) => Stage1Response,
                Type t when t == typeof(Stage2FinderArtifact) => Stage2Response,
                Type t when t == typeof(Stage3PlannerArtifact) => Stage3Response,
                Type t when t == typeof(Stage4ExecutionArtifact) => Stage4Response,
                Type t when t == typeof(Stage6AuditVerdictArtifact) => Stage6Response,
                _ => throw new NotSupportedException($"Unsupported fake contract type: {typeof(T).Name}")
            };

            return Task.FromResult((T)response);
        }
    }

    private sealed class FakeGitWorkspaceManager(string workspaceRootPath) : IGitWorkspaceManager
    {
        public int CloneCount { get; private set; }
        public int ResetCount { get; private set; }
        public string UnifiedDiffToReturn { get; set; } =
            "diff --git a/src/SampleService.cs b/src/SampleService.cs\n+public sealed class SampleService {}\n";

        public string ResolveWorkspacePath(RepositoryEntry repository) =>
            Path.Combine(workspaceRootPath, repository.WorkspaceFolderName);

        public Task<string> CloneOrPrepareWorkspaceAsync(
            RepositoryEntry repository,
            CancellationToken cancellationToken = default)
        {
            CloneCount++;
            string path = ResolveWorkspacePath(repository);
            Directory.CreateDirectory(Path.Combine(path, "src"));
            File.WriteAllText(Path.Combine(path, "Sample.sln"), "Microsoft Visual Studio Solution File");
            File.WriteAllText(Path.Combine(path, "src", "SampleService.cs"), "public sealed class SampleService {}");
            return Task.FromResult(path);
        }

        public Task<List<string>> GetRecentCommitsAsync(
            string workspacePath,
            int count = 15,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<List<string>>(["0763989 Initial commit"]);

        public Task<string> GetUnifiedDiffAsync(
            string workspacePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(UnifiedDiffToReturn);

        public Task ResetAndCleanWorkspaceAsync(
            string workspacePath,
            CancellationToken cancellationToken = default)
        {
            ResetCount++;
            return Task.CompletedTask;
        }

        public Task<string> CreateBranchCommitAndPushAsync(
            string workspacePath,
            RepositoryEntry repository,
            string taskTitle,
            string taskSlug,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"agent/evolution-20261107-{taskSlug}");
    }

    private sealed class FakeBuildTestRunner(int passOnAttempt) : IBuildTestRunner
    {
        public PolyglotStackDescriptor DetectStack(string workspacePath, RepositoryEntry? repoOverrides = null) =>
            new(
                PolyglotStackType.DotNet,
                ".NET / C#",
                "dotnet build",
                "dotnet test",
                ["Sample.sln"]);

        public Task<TestExecutionAttempt> RunBuildAndTestsAsync(
            string workspacePath,
            PolyglotStackDescriptor stackDescriptor,
            int attemptNumber,
            CancellationToken cancellationToken = default)
        {
            bool passed = attemptNumber >= passOnAttempt;
            return Task.FromResult(new TestExecutionAttempt
            {
                AttemptNumber = attemptNumber,
                BuildSucceeded = passed,
                TestsSucceeded = passed,
                ExitCode = passed ? 0 : 1,
                DurationMs = 120,
                StandardOutput = passed ? "Passed!" : "Build/Test error",
                StandardError = passed ? string.Empty : "CS0103: Name does not exist",
                FailureSummary = passed ? null : "Simulated failure"
            });
        }
    }

    private sealed class FakePullRequestClient : IGitHubPullRequestClient
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
            Task.FromResult(new PullRequestCreationResult(
                Number: 101,
                HtmlUrl: $"https://github.com/{repository.FullName}/pull/101",
                Title: $"feat(autonomous-agent): {plan.TaskTitle}",
                BranchName: headBranch));
    }

    private sealed class TempTestDirectory : IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), $"aah-orch-test-{Guid.NewGuid():N}");
        public string WorkspaceRootPath => Path.Combine(RootPath, "workspaces");
        public string StatePath => Path.Combine(RootPath, "state");

        public TempTestDirectory()
        {
            Directory.CreateDirectory(WorkspaceRootPath);
            Directory.CreateDirectory(StatePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

