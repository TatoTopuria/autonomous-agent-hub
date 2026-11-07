using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AutonomousAgentHub.Core.Configuration;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Abstraction over target repository Git workspace operations.
/// </summary>
public interface IGitWorkspaceManager
{
    string ResolveWorkspacePath(RepositoryEntry repository);

    Task<string> CloneOrPrepareWorkspaceAsync(
        RepositoryEntry repository,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetRecentCommitsAsync(
        string workspacePath,
        int count = 15,
        CancellationToken cancellationToken = default);

    Task<string> GetUnifiedDiffAsync(
        string workspacePath,
        CancellationToken cancellationToken = default);

    Task ResetAndCleanWorkspaceAsync(
        string workspacePath,
        CancellationToken cancellationToken = default);

    Task<string> CreateBranchCommitAndPushAsync(
        string workspacePath,
        RepositoryEntry repository,
        string taskTitle,
        string taskSlug,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages cloning, branching, diff inspection, hard safety resets, and authenticated branch pushes
/// for target repositories in <c>/tmp/workspace/{owner}_{repo}</c>.
/// </summary>
public sealed partial class GitWorkspaceManager : IGitWorkspaceManager
{
    private readonly string _workspaceRootPath;
    private readonly string _gitHubPat;

    public GitWorkspaceManager(string workspaceRootPath = "/tmp/workspace", string? gitHubPat = null)
    {
        _workspaceRootPath = string.IsNullOrWhiteSpace(workspaceRootPath)
            ? Path.Combine(Path.GetTempPath(), "workspace")
            : workspaceRootPath;

        _gitHubPat = !string.IsNullOrWhiteSpace(gitHubPat)
            ? gitHubPat
            : Environment.GetEnvironmentVariable("GH_ORCHESTRATOR_PAT") ?? string.Empty;
    }

    public string ResolveWorkspacePath(RepositoryEntry repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return Path.GetFullPath(Path.Combine(_workspaceRootPath, repository.WorkspaceFolderName));
    }

    /// <summary>
    /// Clones a fresh copy of the target repository into <c>/tmp/workspace/{owner}_{repo}</c>.
    /// </summary>
    public async Task<string> CloneOrPrepareWorkspaceAsync(
        RepositoryEntry repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        Directory.CreateDirectory(_workspaceRootPath);
        string targetPath = ResolveWorkspacePath(repository);

        if (Directory.Exists(targetPath))
        {
            DeleteDirectoryWithReadOnlyFiles(targetPath);
        }

        string authenticatedCloneUrl = BuildAuthenticatedCloneUrl(repository);

        await ExecuteGitAsync(
            _workspaceRootPath,
            ["clone", "--branch", repository.DefaultBranch, "--depth", "50", authenticatedCloneUrl, targetPath],
            cancellationToken).ConfigureAwait(false);

        await ExecuteGitAsync(
            targetPath,
            ["config", "user.name", "Autonomous Evolution Hub Bot"],
            cancellationToken).ConfigureAwait(false);

        await ExecuteGitAsync(
            targetPath,
            ["config", "user.email", "autonomous-agent-hub@users.noreply.github.com"],
            cancellationToken).ConfigureAwait(false);

        return targetPath;
    }

    /// <summary>
    /// Reads the last <paramref name="count"/> commits from the target repository via <c>git log -n 15</c>.
    /// </summary>
    public async Task<List<string>> GetRecentCommitsAsync(
        string workspacePath,
        int count = 15,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        string output = await ExecuteGitAsync(
            workspacePath,
            ["log", $"-n{Math.Clamp(count, 1, 50)}", "--oneline", "--no-decorate"],
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ];
    }

    /// <summary>
    /// Stages untracked intent temporarily (`git add -N .`) and produces the complete unified `git diff`.
    /// </summary>
    public async Task<string> GetUnifiedDiffAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        await ExecuteGitAsync(workspacePath, ["add", "-N", "."], cancellationToken).ConfigureAwait(false);
        string diff = await ExecuteGitAsync(workspacePath, ["diff", "--unified=3"], cancellationToken).ConfigureAwait(false);
        return diff;
    }

    /// <summary>
    /// Executes hard safety revert (<c>git reset --hard</c> and <c>git clean -fd</c>) on the target workspace.
    /// Note: Invoked only on isolated target clone workspace after failed self-healing or failed audit.
    /// </summary>
    public async Task ResetAndCleanWorkspaceAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        if (!Directory.Exists(workspacePath))
        {
            return;
        }

        await ExecuteGitAsync(workspacePath, ["reset", "--hard"], cancellationToken).ConfigureAwait(false);
        await ExecuteGitAsync(workspacePath, ["clean", "-fd"], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates branch <c>agent/evolution-YYYYMMDD-{slug}</c>, commits all verified changes, and pushes to origin.
    /// </summary>
    public async Task<string> CreateBranchCommitAndPushAsync(
        string workspacePath,
        RepositoryEntry repository,
        string taskTitle,
        string taskSlug,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskTitle);

        string normalizedSlug = NormalizeBranchSlug(string.IsNullOrWhiteSpace(taskSlug) ? taskTitle : taskSlug);
        string dateStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        string branchName = $"agent/evolution-{dateStamp}-{normalizedSlug}";

        await ExecuteGitAsync(workspacePath, ["checkout", "-B", branchName], cancellationToken).ConfigureAwait(false);
        await ExecuteGitAsync(workspacePath, ["add", "-A"], cancellationToken).ConfigureAwait(false);

        string commitMessage = $"feat(autonomous-agent): {taskTitle.Trim()}";
        await ExecuteGitAsync(workspacePath, ["commit", "-m", commitMessage], cancellationToken).ConfigureAwait(false);

        string remoteUrl = BuildAuthenticatedCloneUrl(repository);
        await ExecuteGitAsync(workspacePath, ["remote", "set-url", "origin", remoteUrl], cancellationToken).ConfigureAwait(false);
        await ExecuteGitAsync(workspacePath, ["push", "-u", "origin", branchName, "--force-with-lease"], cancellationToken).ConfigureAwait(false);

        return branchName;
    }

    public static string NormalizeBranchSlug(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return "autonomous-improvement";
        }

        string lower = rawText.Trim().ToLowerInvariant();
        string hyphenated = NonAlphanumericRegex().Replace(lower, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(hyphenated))
        {
            return "autonomous-improvement";
        }

        return hyphenated.Length <= 42 ? hyphenated : hyphenated[..42].TrimEnd('-');
    }

    private string BuildAuthenticatedCloneUrl(RepositoryEntry repository)
    {
        if (string.IsNullOrWhiteSpace(_gitHubPat))
        {
            return repository.CloneUrl;
        }

        return $"https://x-access-token:{Uri.EscapeDataString(_gitHubPat)}@github.com/{repository.Owner}/{repository.Name}.git";
    }

    private async Task<string> ExecuteGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        StringBuilder stdout = new();
        StringBuilder stderr = new();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            string safeArgs = RedactSecret(string.Join(' ', arguments));
            string safeErr = RedactSecret(stderr.ToString().Trim());
            throw new InvalidOperationException(
                $"git {safeArgs} failed with exit code {process.ExitCode}: {safeErr}");
        }

        return stdout.ToString();
    }

    private string RedactSecret(string text)
    {
        if (string.IsNullOrWhiteSpace(_gitHubPat) || string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text
            .Replace(_gitHubPat, "***REDACTED_PAT***", StringComparison.Ordinal)
            .Replace(Uri.EscapeDataString(_gitHubPat), "***REDACTED_PAT***", StringComparison.Ordinal);
    }

    private static void DeleteDirectoryWithReadOnlyFiles(string path)
    {
        DirectoryInfo dirInfo = new(path);
        if (!dirInfo.Exists)
        {
            return;
        }

        foreach (FileSystemInfo info in dirInfo.GetFileSystemInfos("*", SearchOption.AllDirectories))
        {
            info.Attributes = FileAttributes.Normal;
        }

        dirInfo.Delete(recursive: true);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();
}

