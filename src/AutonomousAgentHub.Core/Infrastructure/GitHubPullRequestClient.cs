using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Result returned after opening a Pull Request on GitHub.
/// </summary>
public sealed record PullRequestCreationResult(
    int Number,
    string HtmlUrl,
    string Title,
    string BranchName);

/// <summary>
/// Represents the current lifecycle state of a previously opened Pull Request on GitHub.
/// </summary>
public sealed record PullRequestOutcomeStatus(
    int Number,
    string OutcomeStatus,
    string? ReviewSummary);

/// <summary>
/// Abstraction for opening and inspecting Pull Requests on target GitHub repositories.
/// </summary>
public interface IGitHubPullRequestClient
{
    Task<PullRequestCreationResult> CreatePullRequestAsync(
        RepositoryEntry repository,
        string headBranch,
        Stage1DiscoveryArtifact discovery,
        Stage2FinderArtifact finder,
        Stage3PlannerArtifact plan,
        Stage4ExecutionArtifact execution,
        Stage5TestReportArtifact testReport,
        Stage6AuditVerdictArtifact auditVerdict,
        CancellationToken cancellationToken = default);

    Task<PullRequestOutcomeStatus?> GetPullRequestOutcomeAsync(
        RepositoryEntry repository,
        int pullRequestNumber,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PullRequestOutcomeStatus?>(null);
}

/// <summary>
/// GitHub REST API v3 client that opens rich, evidence-backed Pull Requests using <c>GH_ORCHESTRATOR_PAT</c>.
/// </summary>
public sealed class GitHubPullRequestClient : IGitHubPullRequestClient
{
    private readonly HttpClient _httpClient;
    private readonly string _gitHubPat;

    public GitHubPullRequestClient(HttpClient httpClient, string? gitHubPat = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;

        _gitHubPat = !string.IsNullOrWhiteSpace(gitHubPat)
            ? gitHubPat
            : Environment.GetEnvironmentVariable("GH_ORCHESTRATOR_PAT") ?? string.Empty;
    }

    public async Task<PullRequestCreationResult> CreatePullRequestAsync(
        RepositoryEntry repository,
        string headBranch,
        Stage1DiscoveryArtifact discovery,
        Stage2FinderArtifact finder,
        Stage3PlannerArtifact plan,
        Stage4ExecutionArtifact execution,
        Stage5TestReportArtifact testReport,
        Stage6AuditVerdictArtifact auditVerdict,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(headBranch);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(testReport);
        ArgumentNullException.ThrowIfNull(auditVerdict);

        if (string.IsNullOrWhiteSpace(_gitHubPat))
        {
            throw new InvalidOperationException(
                "GH_ORCHESTRATOR_PAT is not configured. Set the GH_ORCHESTRATOR_PAT environment variable to open Pull Requests.");
        }

        string prTitle = $"feat(autonomous-agent): {plan.TaskTitle}";
        string prBody = BuildPullRequestMarkdownDossier(
            discovery,
            finder,
            plan,
            execution,
            testReport,
            auditVerdict);

        string endpoint = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/pulls";

        JsonObject payload = new()
        {
            ["title"] = prTitle,
            ["head"] = headBranch,
            ["base"] = repository.DefaultBranch,
            ["body"] = prBody,
            ["maintainer_can_modify"] = true
        };

        using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _gitHubPat);
        request.Headers.UserAgent.ParseAdd("AutonomousAgentHub/1.0 (+https://github.com/TatoTopuria/autonomous-agent-hub)");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub Pull Request creation failed for '{repository.FullName}' with HTTP {(int)response.StatusCode}: {responseJson}",
                null,
                response.StatusCode);
        }

        JsonNode? root = JsonNode.Parse(responseJson);
        int prNumber = root?["number"]?.GetValue<int>() ?? 0;
        string htmlUrl = root?["html_url"]?.GetValue<string>() ?? $"https://github.com/{repository.FullName}/pulls";

        return new PullRequestCreationResult(prNumber, htmlUrl, prTitle, headBranch);
    }

    /// <summary>
    /// Queries GitHub REST API v3 (<c>GET /repos/{owner}/{name}/pulls/{pull_number}</c>) to determine whether a previously
    /// opened Pull Request is <c>OPEN</c>, <c>MERGED</c>, or <c>CLOSED_UNMERGED</c>. Returns <c>null</c> gracefully when
    /// <c>GH_ORCHESTRATOR_PAT</c> is not configured or on transient network failure.
    /// </summary>
    public async Task<PullRequestOutcomeStatus?> GetPullRequestOutcomeAsync(
        RepositoryEntry repository,
        int pullRequestNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (pullRequestNumber <= 0 || string.IsNullOrWhiteSpace(_gitHubPat))
        {
            return null;
        }

        try
        {
            string endpoint = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/pulls/{pullRequestNumber}";
            using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _gitHubPat);
            request.Headers.UserAgent.ParseAdd("AutonomousAgentHub/1.0 (+https://github.com/TatoTopuria/autonomous-agent-hub)");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            JsonNode? root = JsonNode.Parse(responseJson);
            if (root is null)
            {
                return null;
            }

            bool isMerged = root["merged"]?.GetValue<bool>() ?? false;
            string state = root["state"]?.GetValue<string>() ?? "open";
            string? title = root["title"]?.GetValue<string>();

            string outcomeStatus = isMerged
                ? "MERGED"
                : string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase)
                    ? "CLOSED_UNMERGED"
                    : "OPEN";

            string? reviewSummary = outcomeStatus switch
            {
                "MERGED" => $"Pull Request #{pullRequestNumber} ({title}) was merged into {repository.DefaultBranch}.",
                "CLOSED_UNMERGED" => $"Pull Request #{pullRequestNumber} ({title}) was closed without merging.",
                _ => null
            };

            return new PullRequestOutcomeStatus(pullRequestNumber, outcomeStatus, reviewSummary);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Formats the 5-section Pull Request dossier containing discovery context, authoritative evidence,
    /// code changes, test execution report, and independent auditor sign-off.
    /// </summary>
    public static string BuildPullRequestMarkdownDossier(
        Stage1DiscoveryArtifact discovery,
        Stage2FinderArtifact finder,
        Stage3PlannerArtifact plan,
        Stage4ExecutionArtifact execution,
        Stage5TestReportArtifact testReport,
        Stage6AuditVerdictArtifact auditVerdict)
    {
        StringBuilder sb = new();

        sb.AppendLine("## Autonomous Evolution Dossier");
        sb.AppendLine();
        sb.AppendLine($"> **Task:** `{plan.TaskTitle}`  ");
        sb.AppendLine($"> **Category:** `{plan.ImprovementCategory}` | **Detected Stack:** `{discovery.DetectedStack}` | **Auditor Score:** `{auditVerdict.QualityScore}/100`");
        sb.AppendLine();

        // Section 1: Discovery & Architectural Context
        sb.AppendLine("### 1. Discovery & Architectural Context");
        sb.AppendLine($"- **Repository:** `{discovery.RepositoryFullName}`");
        sb.AppendLine($"- **Domain Purpose:** {discovery.DomainPurpose}");
        sb.AppendLine($"- **Architectural Style:** {discovery.ArchitecturalStyle}");
        if (discovery.CriticalBusinessInvariants.Count > 0)
        {
            sb.AppendLine("- **Protected Business Invariants:**");
            foreach (string invariant in discovery.CriticalBusinessInvariants)
            {
                sb.AppendLine($"  - {invariant}");
            }
        }
        sb.AppendLine();

        // Section 2: Authoritative Evidence Dossier
        sb.AppendLine("### 2. Authoritative Evidence Dossier");
        foreach (EvidenceBlock evidence in finder.EvidenceBlocks)
        {
            sb.AppendLine($"#### [{evidence.Id}] {evidence.DocumentTitle} ({evidence.PublicationYear})");
            sb.AppendLine($"- **Issuing Authority:** {evidence.IssuingAuthority}");
            sb.AppendLine($"- **Official Reference:** {evidence.OfficialUrl}");
            sb.AppendLine($"- **Governing Excerpt:** > *{evidence.GoverningExcerpt}*");
            sb.AppendLine($"- **Relevance to Repository:** {evidence.RelevanceToRepo}");
            sb.AppendLine();
        }

        // Section 3: Summary of Code & Logic Changes
        sb.AppendLine("### 3. Summary of Code & Logic Changes");
        sb.AppendLine($"**Engineering & Business Rationale:** {plan.EngineeringAndBusinessRationale}");
        sb.AppendLine();
        sb.AppendLine("**Implementation Summary:**");
        sb.AppendLine(execution.ImplementationSummary);
        sb.AppendLine();
        sb.AppendLine("| File Path | Operation | Role | Summary |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");
        foreach (FileMutation mutation in execution.FileMutations)
        {
            string role = mutation.IsTestFile ? "Test Suite" : "Production";
            sb.AppendLine($"| `{mutation.RelativePath}` | `{mutation.Operation}` | {role} | {mutation.SummaryOfChanges} |");
        }
        sb.AppendLine();

        // Section 4: Test Execution Report
        sb.AppendLine("### 4. Test Execution Report");
        sb.AppendLine($"- **Status:** {(testReport.Passed ? "PASSED (100% Green)" : "FAILED")}");
        sb.AppendLine($"- **Self-Healing Iterations Used:** `{testReport.TotalAttempts}`");
        sb.AppendLine($"- **Build Command:** `{testReport.BuildCommandExecuted}`");
        sb.AppendLine($"- **Test Command:** `{testReport.TestCommandExecuted}`");
        TestExecutionAttempt? finalAttempt = testReport.AttemptLogs.LastOrDefault();
        if (finalAttempt is not null)
        {
            sb.AppendLine($"- **Execution Duration:** `{finalAttempt.DurationMs} ms` (Exit Code: `{finalAttempt.ExitCode}`)");
        }
        sb.AppendLine();

        // Section 5: Independent Auditor Sign-Off & Quality Score
        sb.AppendLine("### 5. Independent Auditor Sign-Off & Quality Score");
        sb.AppendLine($"- **Verdict:** `{(auditVerdict.Approved ? "APPROVED" : "REJECTED")}`");
        sb.AppendLine($"- **Composite Quality Score:** `{auditVerdict.QualityScore}/100`");
        sb.AppendLine($"- **Executive Summary:** {auditVerdict.ExecutiveSummary}");
        if (auditVerdict.ComplianceChecklist.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Compliance Gate Checklist:**");
            foreach (AuditChecklistItem check in auditVerdict.ComplianceChecklist)
            {
                string mark = check.Passed ? "[x]" : "[ ]";
                sb.AppendLine($"- {mark} **{check.Criterion}**: {check.Notes}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Generated autonomously by **AutonomousAgentHub** (7-Stage Verified Evolution Pipeline).*");

        return sb.ToString();
    }
}

