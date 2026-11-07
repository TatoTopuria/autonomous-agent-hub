using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;
using Xunit;

namespace AutonomousAgentHub.Tests;

public sealed class RotationLogicTests
{
    private static List<RepositoryEntry> CreateSampleAllowlist() =>
    [
        new RepositoryEntry { Owner = "TatoTopuria", Name = "repo-dotnet", Enabled = true },
        new RepositoryEntry { Owner = "TatoTopuria", Name = "repo-spring", Enabled = true },
        new RepositoryEntry { Owner = "TatoTopuria", Name = "repo-python", Enabled = true },
        new RepositoryEntry { Owner = "TatoTopuria", Name = "repo-node", Enabled = true }
    ];

    [Fact]
    public void SelectNextRepository_StartsAtIndexZero_WhenLastProcessedIsNegativeOne()
    {
        List<RepositoryEntry> repos = CreateSampleAllowlist();
        RotationState state = new() { LastProcessedIndex = -1 };

        SelectedRepositoryTarget selected = StateLedgerManager.SelectNextRepository(repos, state);

        Assert.Equal(0, selected.RotationIndex);
        Assert.Equal("TatoTopuria/repo-dotnet", selected.Repository.FullName);
        Assert.False(selected.IsManualOverride);
    }

    [Fact]
    public void SelectNextRepository_RotatesSequentially_AndWrapsAroundToZero()
    {
        List<RepositoryEntry> repos = CreateSampleAllowlist();
        RotationState state = new() { LastProcessedIndex = -1 };

        string[] expectedSequence =
        [
            "TatoTopuria/repo-dotnet",
            "TatoTopuria/repo-spring",
            "TatoTopuria/repo-python",
            "TatoTopuria/repo-node",
            "TatoTopuria/repo-dotnet",
            "TatoTopuria/repo-spring"
        ];

        foreach (string expectedFullName in expectedSequence)
        {
            SelectedRepositoryTarget target = StateLedgerManager.SelectNextRepository(repos, state);
            Assert.Equal(expectedFullName, target.Repository.FullName);
            state.LastProcessedIndex = target.RotationIndex;
        }
    }

    [Fact]
    public void SelectNextRepository_SingleRepository_AlwaysSelectsIndexZero()
    {
        List<RepositoryEntry> singleRepo =
        [
            new RepositoryEntry { Owner = "TatoTopuria", Name = "solo-service", Enabled = true }
        ];

        RotationState state = new() { LastProcessedIndex = 0 };
        SelectedRepositoryTarget selected = StateLedgerManager.SelectNextRepository(singleRepo, state);

        Assert.Equal(0, selected.RotationIndex);
        Assert.Equal("TatoTopuria/solo-service", selected.Repository.FullName);
    }

    [Fact]
    public void SelectNextRepository_EmptyAllowlist_ThrowsInvalidOperationException()
    {
        List<RepositoryEntry> emptyRepos = [];
        RotationState state = new() { LastProcessedIndex = -1 };

        Assert.Throws<InvalidOperationException>(() =>
            StateLedgerManager.SelectNextRepository(emptyRepos, state));
    }

    [Fact]
    public void SelectNextRepository_SkipsDisabledRepositories()
    {
        List<RepositoryEntry> repos =
        [
            new RepositoryEntry { Owner = "TatoTopuria", Name = "repo-enabled-1", Enabled = true },
            new RepositoryEntry { Owner = "TatoTopuria", Name = "repo-disabled", Enabled = false },
            new RepositoryEntry { Owner = "TatoTopuria", Name = "repo-enabled-2", Enabled = true }
        ];

        RotationState state = new() { LastProcessedIndex = 0 };
        SelectedRepositoryTarget selected = StateLedgerManager.SelectNextRepository(repos, state);

        Assert.Equal(1, selected.RotationIndex);
        Assert.Equal("TatoTopuria/repo-enabled-2", selected.Repository.FullName);
    }

    [Fact]
    public void SelectNextRepository_WithManualOverride_SelectsRequestedRepository()
    {
        List<RepositoryEntry> repos = CreateSampleAllowlist();
        RotationState state = new() { LastProcessedIndex = 0 };

        SelectedRepositoryTarget selected = StateLedgerManager.SelectNextRepository(
            repos,
            state,
            repositoryOverride: "TatoTopuria/repo-python");

        Assert.True(selected.IsManualOverride);
        Assert.Equal(2, selected.RotationIndex);
        Assert.Equal("TatoTopuria/repo-python", selected.Repository.FullName);
    }

    [Fact]
    public async Task StateLedgerManager_PersistsRotationState_AndRepositoryLedger()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"hub-test-{Guid.NewGuid():N}");
        try
        {
            StateLedgerManager manager = new(tempDir);
            RepositoryEntry repo = new() { Owner = "TatoTopuria", Name = "repo-dotnet" };
            SelectedRepositoryTarget target = new(repo, RotationIndex: 2, IsManualOverride: false);

            RotationState updatedState = await manager.AdvanceRotationStateAsync(target);
            Assert.Equal(2, updatedState.LastProcessedIndex);
            Assert.Equal("TatoTopuria/repo-dotnet", updatedState.LastRepo);
            Assert.Equal(1, updatedState.TotalRuns);

            await manager.RecordCompletedProposalAsync(repo, new LedgerProposalEntry
            {
                TaskTitle = "Harden CancellationToken propagation",
                TaskSlug = "harden-cancellationtoken-propagation",
                BranchName = "agent/evolution-20261107-harden-cancellationtoken-propagation",
                PullRequestNumber = 42,
                PullRequestUrl = "https://github.com/TatoTopuria/repo-dotnet/pull/42",
                QualityScore = 94,
                GoverningAuthority = "Microsoft Learn",
                ModifiedFiles = ["src/Service.cs", "tests/ServiceTests.cs"]
            });

            RepoStateLedger reloadedLedger = await manager.LoadRepoLedgerAsync(repo);
            Assert.Single(reloadedLedger.CompletedProposals);
            Assert.Equal(42, reloadedLedger.CompletedProposals[0].PullRequestNumber);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProductionRepositoriesConfig_ContainsAllTwentyTwoTargetRepositories_AndRotatesCleanly()
    {
        string configPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "config", "repositories.json"));

        HubConfig config = await HubConfig.LoadAsync(configPath);

        Assert.Equal(22, config.Repositories.Count);
        Assert.Equal("TatoTopuria/wisewallet-infra", config.Repositories[0].FullName);
        Assert.Equal("TatoTopuria/EventHub.Booking", config.Repositories[6].FullName);
        Assert.Equal("TatoTopuria/sdet-demonstration", config.Repositories[19].FullName);
        Assert.Equal("TatoTopuria/ChildAdvisor", config.Repositories[20].FullName);
        Assert.Equal("TatoTopuria/BabyDoctor", config.Repositories[21].FullName);

        RotationState stateAtEnd = new() { LastProcessedIndex = 21 };
        SelectedRepositoryTarget wrappedTarget = StateLedgerManager.SelectNextRepository(config.Repositories, stateAtEnd);

        Assert.Equal(0, wrappedTarget.RotationIndex);
        Assert.Equal("TatoTopuria/wisewallet-infra", wrappedTarget.Repository.FullName);
    }
}

