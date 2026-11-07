using System.Text.Json;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Represents the selected target repository and its resolved rotation index.
/// </summary>
public sealed record SelectedRepositoryTarget(
    RepositoryEntry Repository,
    int RotationIndex,
    bool IsManualOverride);

/// <summary>
/// Manages persistence of <c>state/rotation-state.json</c> and per-repository ledgers
/// in <c>state/ledgers/{owner}__{repo}.json</c>.
/// </summary>
public sealed class StateLedgerManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _rotationStateFilePath;
    private readonly string _ledgersDirectoryPath;

    public StateLedgerManager(string stateDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectoryPath);
        _rotationStateFilePath = Path.Combine(stateDirectoryPath, "rotation-state.json");
        _ledgersDirectoryPath = Path.Combine(stateDirectoryPath, "ledgers");
    }

    public string RotationStateFilePath => _rotationStateFilePath;

    public string LedgersDirectoryPath => _ledgersDirectoryPath;

    /// <summary>
    /// Selects the next repository to process using deterministic Round-Robin rotation
    /// (with wrap-around) or an explicit manual repository override.
    /// </summary>
    public static SelectedRepositoryTarget SelectNextRepository(
        IReadOnlyList<RepositoryEntry> repositories,
        RotationState currentState,
        string? repositoryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(currentState);

        List<RepositoryEntry> enabledRepos = [.. repositories.Where(r => r.Enabled)];

        if (!string.IsNullOrWhiteSpace(repositoryOverride))
        {
            string target = repositoryOverride.Trim();
            RepositoryEntry? matched = repositories.FirstOrDefault(r =>
                string.Equals(r.FullName, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Name, target, StringComparison.OrdinalIgnoreCase));

            if (matched is null)
            {
                throw new InvalidOperationException(
                    $"Repository override '{repositoryOverride}' was not found in the configured allowlist.");
            }

            int enabledIndex = enabledRepos.FindIndex(r =>
                string.Equals(r.FullName, matched.FullName, StringComparison.OrdinalIgnoreCase));

            return new SelectedRepositoryTarget(
                matched,
                enabledIndex >= 0 ? enabledIndex : currentState.LastProcessedIndex,
                IsManualOverride: true);
        }

        if (enabledRepos.Count == 0)
        {
            throw new InvalidOperationException(
                "No enabled repositories are configured in config/repositories.json.");
        }

        int nextIndex;
        if (currentState.LastProcessedIndex < 0 || currentState.LastProcessedIndex >= enabledRepos.Count - 1)
        {
            nextIndex = 0;
        }
        else
        {
            nextIndex = (currentState.LastProcessedIndex + 1) % enabledRepos.Count;
        }

        return new SelectedRepositoryTarget(
            enabledRepos[nextIndex],
            nextIndex,
            IsManualOverride: false);
    }

    /// <summary>
    /// Loads <c>state/rotation-state.json</c> or returns an initialized default state if the file does not exist.
    /// </summary>
    public async Task<RotationState> LoadRotationStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_rotationStateFilePath))
        {
            return new RotationState();
        }

        string json = await File.ReadAllTextAsync(_rotationStateFilePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RotationState();
        }

        return JsonSerializer.Deserialize<RotationState>(json, JsonOptions) ?? new RotationState();
    }

    /// <summary>
    /// Persists updated rotation state to <c>state/rotation-state.json</c>.
    /// </summary>
    public async Task SaveRotationStateAsync(RotationState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? parentDir = Path.GetDirectoryName(_rotationStateFilePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        string json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(_rotationStateFilePath, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the rotation state to the specified index and repository and writes it to disk.
    /// </summary>
    public async Task<RotationState> AdvanceRotationStateAsync(
        SelectedRepositoryTarget selectedTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedTarget);

        RotationState state = await LoadRotationStateAsync(cancellationToken).ConfigureAwait(false);
        if (!selectedTarget.IsManualOverride || selectedTarget.RotationIndex >= 0)
        {
            state.LastProcessedIndex = selectedTarget.RotationIndex;
        }

        state.LastRepo = selectedTarget.Repository.FullName;
        state.LastRunUtc = DateTimeOffset.UtcNow;
        state.TotalRuns += 1;

        await SaveRotationStateAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    /// <summary>
    /// Resolves the file path for a repository's memory ledger (<c>state/ledgers/{owner}__{repo}.json</c>).
    /// </summary>
    public string GetLedgerFilePath(RepositoryEntry repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return Path.Combine(_ledgersDirectoryPath, $"{repository.LedgerFileSlug}.json");
    }

    /// <summary>
    /// Loads a repository's historical memory ledger or creates a new empty ledger if none exists yet.
    /// </summary>
    public async Task<RepoStateLedger> LoadRepoLedgerAsync(
        RepositoryEntry repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        string filePath = GetLedgerFilePath(repository);
        if (!File.Exists(filePath))
        {
            return new RepoStateLedger
            {
                RepositoryFullName = repository.FullName
            };
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RepoStateLedger
            {
                RepositoryFullName = repository.FullName
            };
        }

        return JsonSerializer.Deserialize<RepoStateLedger>(json, JsonOptions) ?? new RepoStateLedger
        {
            RepositoryFullName = repository.FullName
        };
    }

    /// <summary>
    /// Saves the updated repository memory ledger to <c>state/ledgers/{owner}__{repo}.json</c>.
    /// </summary>
    public async Task SaveRepoLedgerAsync(
        RepositoryEntry repository,
        RepoStateLedger ledger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(ledger);

        Directory.CreateDirectory(_ledgersDirectoryPath);
        string filePath = GetLedgerFilePath(repository);
        string json = JsonSerializer.Serialize(ledger, JsonOptions);
        await File.WriteAllTextAsync(filePath, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends a completed PR proposal to the repository's ledger and saves it.
    /// </summary>
    public async Task RecordCompletedProposalAsync(
        RepositoryEntry repository,
        LedgerProposalEntry proposal,
        CancellationToken cancellationToken = default)
    {
        RepoStateLedger existing = await LoadRepoLedgerAsync(repository, cancellationToken).ConfigureAwait(false);
        RepoStateLedger updated = new()
        {
            RepositoryFullName = repository.FullName,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            CompletedProposals = [.. existing.CompletedProposals, proposal],
            AbortedAttempts = [.. existing.AbortedAttempts]
        };

        await SaveRepoLedgerAsync(repository, updated, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends an aborted run record to the repository's ledger and saves it.
    /// </summary>
    public async Task RecordAbortedAttemptAsync(
        RepositoryEntry repository,
        string taskTitle,
        string stageAbortedAt,
        string reason,
        CancellationToken cancellationToken = default)
    {
        RepoStateLedger existing = await LoadRepoLedgerAsync(repository, cancellationToken).ConfigureAwait(false);
        LedgerAbortedEntry abortedEntry = new()
        {
            TaskTitle = taskTitle,
            StageAbortedAt = stageAbortedAt,
            Reason = reason,
            AbortedAtUtc = DateTimeOffset.UtcNow
        };

        RepoStateLedger updated = new()
        {
            RepositoryFullName = repository.FullName,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            CompletedProposals = [.. existing.CompletedProposals],
            AbortedAttempts = [.. existing.AbortedAttempts, abortedEntry]
        };

        await SaveRepoLedgerAsync(repository, updated, cancellationToken).ConfigureAwait(false);
    }
}

