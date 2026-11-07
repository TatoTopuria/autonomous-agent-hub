using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Stage 5: Polyglot Build, Regression Test &amp; 3x Self-Healing Agent (Tester).
/// Runs the repository's build and test suite, extracts structured compiler/test diagnostics via
/// <see cref="StackTraceDiagnosticParser"/>, invokes <see cref="Stage4ExecutorAgent"/> up to 3 total attempts
/// on errors, and triggers a hard workspace reset if all 3 attempts fail.
/// </summary>
public sealed class Stage5TesterAgent(
    IBuildTestRunner buildTestRunner,
    IGitWorkspaceManager gitWorkspaceManager,
    Stage4ExecutorAgent executorAgent,
    int maxSelfHealingAttempts = 3)
{
    private readonly int _maxAttempts = Math.Clamp(maxSelfHealingAttempts, 1, 5);

    public async Task<(Stage5TestReportArtifact TestReport, Stage4ExecutionArtifact FinalExecution)> ExecuteWithSelfHealingAsync(
        RepositoryEntry repository,
        string workspacePath,
        Stage1DiscoveryArtifact discovery,
        Stage3PlannerArtifact plan,
        Stage4ExecutionArtifact initialExecution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(initialExecution);

        PolyglotStackDescriptor stack = buildTestRunner.DetectStack(workspacePath, repository);
        List<TestExecutionAttempt> attemptLogs = [];
        Stage4ExecutionArtifact currentExecution = initialExecution;

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            Console.WriteLine(
                $"[Stage 5: Tester] Running polyglot build & test suite ({stack.DisplayName}, Attempt {attempt}/{_maxAttempts})...");

            TestExecutionAttempt attemptResult = await buildTestRunner.RunBuildAndTestsAsync(
                workspacePath,
                stack,
                attempt,
                cancellationToken).ConfigureAwait(false);

            attemptLogs.Add(attemptResult);

            if (attemptResult.BuildSucceeded && attemptResult.TestsSucceeded)
            {
                Console.WriteLine($"[Stage 5: Tester] Build and all tests PASSED on attempt {attempt} ({attemptResult.DurationMs} ms).");

                Stage5TestReportArtifact successReport = new()
                {
                    Passed = true,
                    TotalAttempts = attempt,
                    DetectedStack = stack.StackType.ToString(),
                    BuildCommandExecuted = stack.BuildCommand,
                    TestCommandExecuted = stack.TestCommand,
                    AttemptLogs = attemptLogs,
                    HardResetTriggered = false
                };

                return (successReport, currentExecution);
            }

            Console.WriteLine(
                $"[Stage 5: Tester] Attempt {attempt}/{_maxAttempts} FAILED: {attemptResult.FailureSummary}");

            if (attempt < _maxAttempts)
            {
                string failureTrace = StackTraceDiagnosticParser.BuildFocusedFailureTrace(attemptResult);

                Console.WriteLine($"[Stage 5: Tester] Triggering Self-Healing Iteration {attempt + 1}/{_maxAttempts} via Stage 4 Executor...");
                currentExecution = await executorAgent.ExecuteAsync(
                    workspacePath,
                    discovery,
                    plan,
                    attemptNumber: attempt + 1,
                    previousFailureTrace: failureTrace,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        Console.WriteLine(
            $"[Stage 5: Tester] All {_maxAttempts} attempts failed. Executing hard safety reset on target workspace...");

        await gitWorkspaceManager.ResetAndCleanWorkspaceAsync(workspacePath, cancellationToken).ConfigureAwait(false);

        Stage5TestReportArtifact failedReport = new()
        {
            Passed = false,
            TotalAttempts = _maxAttempts,
            DetectedStack = stack.StackType.ToString(),
            BuildCommandExecuted = stack.BuildCommand,
            TestCommandExecuted = stack.TestCommand,
            AttemptLogs = attemptLogs,
            HardResetTriggered = true
        };

        return (failedReport, currentExecution);
    }
}
