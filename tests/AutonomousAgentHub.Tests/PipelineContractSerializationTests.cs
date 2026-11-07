using System.Text.Json;
using System.Text.Json.Nodes;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;
using AutonomousAgentHub.Core.Pipeline;
using Xunit;

namespace AutonomousAgentHub.Tests;

public sealed class PipelineContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void AllSevenStageContracts_SerializeAndDeserializeRoundTrip()
    {
        // Stage 1: Discovery
        Stage1DiscoveryArtifact stage1 = new()
        {
            RepositoryFullName = "TatoTopuria/dotnet-clean-architecture-sample",
            DetectedStack = "DotNet",
            DomainPurpose = "Enterprise Order & Payment Processing",
            ArchitecturalStyle = "Clean Architecture + CQRS",
            DataAndControlFlows = ["Controller -> MediatR Command -> Domain Entity -> EF Core Repository"],
            CriticalBusinessInvariants = ["Order total must equal sum of line items", "Never allow negative payment amounts"],
            DirectoryTreeSummary = ["src/Domain/Order.cs", "tests/Domain.Tests/OrderTests.cs"],
            BuildAndDependencyManifests = ["Sample.sln", "src/Domain/Domain.csproj"],
            KeyArchitecturalEntrypoints = ["src/Api/Program.cs"],
            ExistingTestFiles = ["tests/Domain.Tests/OrderTests.cs"],
            RecentGitCommits = ["a1b2c3d Initial clean architecture setup"],
            CandidateFilesForInspection =
            [
                new InspectedFileSnapshot
                {
                    RelativePath = "src/Domain/Order.cs",
                    ReasonForSelection = "Core aggregate root",
                    ContentExcerpt = "public sealed class Order { }"
                }
            ]
        };

        string stage1Json = JsonSerializer.Serialize(stage1, JsonOptions);
        Stage1DiscoveryArtifact? stage1RoundTrip = JsonSerializer.Deserialize<Stage1DiscoveryArtifact>(stage1Json, JsonOptions);
        Assert.NotNull(stage1RoundTrip);
        Assert.Equal(stage1.RepositoryFullName, stage1RoundTrip.RepositoryFullName);
        Assert.Single(stage1RoundTrip.CandidateFilesForInspection);

        // Stage 2: Finder
        Stage2FinderArtifact stage2 = new()
        {
            RepositoryFullName = stage1.RepositoryFullName,
            TargetStack = "DotNet",
            DomainFocus = stage1.DomainPurpose,
            SearchQueriesExecuted = ["Microsoft Learn EF Core AsNoTracking performance", "OWASP input validation guidelines"],
            EvidenceBlocks =
            [
                new EvidenceBlock
                {
                    Id = "EV-01",
                    IssuingAuthority = "Microsoft Learn",
                    DocumentTitle = "Tracking vs. No-Tracking Queries - EF Core",
                    PublicationYear = 2025,
                    OfficialUrl = "https://learn.microsoft.com/en-us/ef/core/querying/tracking",
                    GoverningExcerpt = "No-tracking queries are quicker to execute because they do not set up change tracking information.",
                    RelevanceToRepo = "Read-only order lookup queries should enforce AsNoTracking()."
                }
            ]
        };

        string stage2Json = JsonSerializer.Serialize(stage2, JsonOptions);
        Stage2FinderArtifact? stage2RoundTrip = JsonSerializer.Deserialize<Stage2FinderArtifact>(stage2Json, JsonOptions);
        Assert.NotNull(stage2RoundTrip);
        Assert.True(stage2RoundTrip.HasVerifiableEvidence);
        Assert.Equal("Microsoft Learn", stage2RoundTrip.EvidenceBlocks[0].IssuingAuthority);

        // Stage 3: Planner
        Stage3PlannerArtifact stage3 = new()
        {
            TaskTitle = "Enforce AsNoTracking and CancellationToken on read-only Order queries",
            TaskSlug = "enforce-asnotracking-order-queries",
            ImprovementCategory = "Performance & Resilience",
            EngineeringAndBusinessRationale = "Eliminates EF Core change tracker overhead on read-only queries.",
            LinkedEvidenceIds = ["EV-01"],
            PrimaryEvidenceBlock = stage2.EvidenceBlocks[0],
            TargetFiles =
            [
                new PlannedFileChange
                {
                    RelativePath = "src/Infrastructure/OrderQueries.cs",
                    Operation = "Modify",
                    ChangeDescription = "Add AsNoTracking() and pass CancellationToken",
                    IsTestFile = false
                },
                new PlannedFileChange
                {
                    RelativePath = "tests/Domain.Tests/OrderQueriesTests.cs",
                    Operation = "Create",
                    ChangeDescription = "Verify read-only query behavior and cancellation",
                    IsTestFile = true
                }
            ],
            AcceptanceCriteria = ["All read queries use AsNoTracking()", "Unit tests pass 100%"],
            RegressionTestStrategy = "Run full xUnit suite via dotnet test",
            InvariantsPreserved = stage1.CriticalBusinessInvariants
        };

        string stage3Json = JsonSerializer.Serialize(stage3, JsonOptions);
        Stage3PlannerArtifact? stage3RoundTrip = JsonSerializer.Deserialize<Stage3PlannerArtifact>(stage3Json, JsonOptions);
        Assert.NotNull(stage3RoundTrip);
        Assert.Equal(2, stage3RoundTrip.TargetFiles.Count);

        // Stage 4: Executor
        Stage4ExecutionArtifact stage4 = new()
        {
            TaskTitle = stage3.TaskTitle,
            AttemptNumber = 1,
            ImplementationSummary = "Updated OrderQueries with AsNoTracking() and added unit tests.",
            FileMutations =
            [
                new FileMutation
                {
                    RelativePath = "src/Infrastructure/OrderQueries.cs",
                    Operation = "Modify",
                    UpdatedContent = "// Updated C# source",
                    SummaryOfChanges = "Added .AsNoTracking() to GetOrderByIdAsync",
                    IsTestFile = false
                }
            ]
        };

        string stage4Json = JsonSerializer.Serialize(stage4, JsonOptions);
        Stage4ExecutionArtifact? stage4RoundTrip = JsonSerializer.Deserialize<Stage4ExecutionArtifact>(stage4Json, JsonOptions);
        Assert.NotNull(stage4RoundTrip);
        Assert.Single(stage4RoundTrip.FileMutations);

        // Stage 5: Tester
        Stage5TestReportArtifact stage5 = new()
        {
            Passed = true,
            TotalAttempts = 1,
            DetectedStack = "DotNet",
            BuildCommandExecuted = "dotnet build",
            TestCommandExecuted = "dotnet test",
            AttemptLogs =
            [
                new TestExecutionAttempt
                {
                    AttemptNumber = 1,
                    BuildSucceeded = true,
                    TestsSucceeded = true,
                    ExitCode = 0,
                    DurationMs = 1420,
                    StandardOutput = "Passed! - Failed: 0, Passed: 18"
                }
            ],
            HardResetTriggered = false
        };

        string stage5Json = JsonSerializer.Serialize(stage5, JsonOptions);
        Stage5TestReportArtifact? stage5RoundTrip = JsonSerializer.Deserialize<Stage5TestReportArtifact>(stage5Json, JsonOptions);
        Assert.NotNull(stage5RoundTrip);
        Assert.True(stage5RoundTrip.Passed);

        // Stage 6: Auditor
        Stage6AuditVerdictArtifact stage6 = new()
        {
            Approved = true,
            QualityScore = 95,
            ComplianceChecklist =
            [
                new AuditChecklistItem
                {
                    Criterion = "100% Green Tests",
                    Passed = true,
                    Notes = "All 18 tests passed on first attempt."
                }
            ],
            BlockingIssues = [],
            ExecutiveSummary = "High-precision optimization backed by official Microsoft Learn EF Core guidance.",
            DiffLineCount = 34
        };

        string stage6Json = JsonSerializer.Serialize(stage6, JsonOptions);
        Stage6AuditVerdictArtifact? stage6RoundTrip = JsonSerializer.Deserialize<Stage6AuditVerdictArtifact>(stage6Json, JsonOptions);
        Assert.NotNull(stage6RoundTrip);
        Assert.True(stage6RoundTrip.MeetsDeploymentGate(85));

        // Stage 7: Ledger & PR Dossier
        string dossierMarkdown = GitHubPullRequestClient.BuildPullRequestMarkdownDossier(
            stage1RoundTrip,
            stage2RoundTrip,
            stage3RoundTrip,
            stage4RoundTrip,
            stage5RoundTrip,
            stage6RoundTrip);

        Assert.Contains("Discovery & Architectural Context", dossierMarkdown);
        Assert.Contains("Authoritative Evidence Dossier", dossierMarkdown);
        Assert.Contains("https://learn.microsoft.com/en-us/ef/core/querying/tracking", dossierMarkdown);
        Assert.Contains("Independent Auditor Sign-Off & Quality Score", dossierMarkdown);
    }

    [Theory]
    [InlineData("https://learn.microsoft.com/en-us/dotnet/fundamentals/", true)]
    [InlineData("https://owasp.org/www-project-top-ten/", true)]
    [InlineData("https://www.who.int/publications/i/item/9789241549929", true)]
    [InlineData("https://medium.com/@someblogger/my-opinion-123", false)]
    [InlineData("https://www.reddit.com/r/csharp/comments/123", false)]
    [InlineData("https://stackoverflow.com/questions/12345", false)]
    public void Stage2FinderAgent_ValidatesOfficialEvidenceUrls(string url, bool expectedValid)
    {
        EvidenceBlock block = new()
        {
            Id = "EV-TEST",
            IssuingAuthority = "Authority",
            DocumentTitle = "Standard",
            PublicationYear = 2025,
            OfficialUrl = url,
            GoverningExcerpt = "Normative excerpt.",
            RelevanceToRepo = "Direct relevance."
        };

        bool actual = Stage2FinderAgent.IsVerifiableOfficialEvidence(block);
        Assert.Equal(expectedValid, actual);
    }

    [Fact]
    public void GeminiFreeTierClient_BuildsSearchGroundingPayload_AndExtractsFencedJson()
    {
        using HttpClient httpClient = new();
        GeminiFreeTierClient client = new(httpClient, apiKey: "test-key", interStageDelaySeconds: 0);

        JsonObject groundedPayload = client.BuildRequestPayload(
            "System instruction",
            "User prompt",
            useGoogleSearchGrounding: true);

        Assert.NotNull(groundedPayload["tools"]);
        Assert.NotNull(groundedPayload["tools"]?[0]?["google_search"]);

        string fencedResponse =
            """
            ```json
            {
              "approved": true,
              "qualityScore": 91
            }
            ```
            """;

        string extracted = GeminiFreeTierClient.ExtractCleanJsonPayload(fencedResponse);
        Stage6AuditVerdictArtifact? parsed = JsonSerializer.Deserialize<Stage6AuditVerdictArtifact>(extracted, JsonOptions);

        Assert.NotNull(parsed);
        Assert.True(parsed.Approved);
        Assert.Equal(91, parsed.QualityScore);
    }
}

