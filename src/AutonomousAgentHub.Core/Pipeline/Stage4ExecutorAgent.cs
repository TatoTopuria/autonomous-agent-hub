using System.Text;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Stage 4: Code &amp; Test Implementation Agent (Executor).
/// Reads full target file contents from the cloned workspace, generates updated production and test code,
/// validates against truncation placeholders and path traversal, applies the changes safely to disk,
/// and supports self-healing iterations driven by Stage 5 failure traces.
/// </summary>
public sealed class Stage4ExecutorAgent(
    IGeminiClient geminiClient,
    ISkillRegistry? skillRegistry = null,
    ExperientialMemoryManager? experientialMemoryManager = null,
    IMcpToolManager? mcpToolManager = null)
{
    private static readonly SkillRegistry DefaultSkillRegistry = new();

    private static readonly string[] ForbiddenTruncationPlaceholders =
    [
        "// ... existing code",
        "// ... rest of",
        "// ... remaining",
        "/* ... existing",
        "/* ... rest of",
        "# ... existing code",
        "# ... rest of",
        "// TODO: implement remaining"
    ];

    public async Task<Stage4ExecutionArtifact> ExecuteAsync(
        string workspacePath,
        Stage1DiscoveryArtifact discovery,
        Stage3PlannerArtifact plan,
        int attemptNumber = 1,
        string? previousFailureTrace = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(plan);

        Console.WriteLine(
            $"[Stage 4: Executor] Executing code & test generation (Attempt {attemptNumber}) for task '{plan.TaskTitle}'...");

        Dictionary<string, string> currentFileContents = await LoadTargetFilesAsync(
            workspacePath,
            discovery,
            plan,
            cancellationToken).ConfigureAwait(false);

        string baseSystemInstruction =
            """
            You are Stage 4 (Executor Agent) of the Autonomous Evolution Hub.
            You write production-grade code and comprehensive unit/regression tests for the planned improvement task.
            STRICT ENGINEERING RULES:
            1. Provide COMPLETE, COMPILABLE file contents in `updatedContent` for every modified or created file (never use placeholders like `// ... existing code ...`).
            2. Preserve all existing copyright headers, public contracts, and critical business invariants.
            3. Include unit tests that thoroughly verify the new behavior and edge cases.
            4. If `previousFailureTrace` is provided from a prior test attempt, diagnose the exact compiler or test failure and fix it cleanly.
            Return ONLY valid JSON matching the Stage4ExecutionArtifact contract.
            """;

        string? experientialInjection = experientialMemoryManager?.BuildStagePromptInjection("Stage4Executor", discovery.DetectedStack);
        string systemInstruction = (skillRegistry ?? DefaultSkillRegistry).ComposeStageSystemInstruction(
            baseSystemInstruction,
            "stage4-executor-clean-diff",
            discovery.DetectedStack,
            experientialInjection);

        StringBuilder prompt = new();
        prompt.AppendLine($"Repository: {discovery.RepositoryFullName} ({discovery.DetectedStack})");
        prompt.AppendLine($"Attempt Number: {attemptNumber}");
        prompt.AppendLine($"Planned Task Title: {plan.TaskTitle}");
        prompt.AppendLine($"Rationale: {plan.EngineeringAndBusinessRationale}");
        if (plan.PrimaryEvidenceBlock is not null)
        {
            prompt.AppendLine(
                $"Governing Standard: [{plan.PrimaryEvidenceBlock.IssuingAuthority}] {plan.PrimaryEvidenceBlock.DocumentTitle} - {plan.PrimaryEvidenceBlock.GoverningExcerpt}");
        }

        if (mcpToolManager is not null)
        {
            string mcpSummary = mcpToolManager.BuildStageMcpContextSummary("Stage4Executor", workspacePath);
            if (!string.IsNullOrWhiteSpace(mcpSummary))
            {
                prompt.AppendLine();
                prompt.AppendLine(mcpSummary);
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Acceptance Criteria:");
        foreach (string criterion in plan.AcceptanceCriteria)
        {
            prompt.AppendLine($"- {criterion}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Planned Target Files:");
        foreach (PlannedFileChange target in plan.TargetFiles)
        {
            prompt.AppendLine($"- {target.RelativePath} ({target.Operation}, IsTest={target.IsTestFile}): {target.ChangeDescription}");
        }

        if (!string.IsNullOrWhiteSpace(previousFailureTrace))
        {
            prompt.AppendLine();
            prompt.AppendLine("=== COMPILER / TEST FAILURE FEEDBACK FROM PREVIOUS ATTEMPT (MUST FIX) ===");
            prompt.AppendLine(previousFailureTrace);
            prompt.AppendLine("=== END FAILURE FEEDBACK ===");
        }

        prompt.AppendLine();
        prompt.AppendLine("=== FULL CURRENT FILE CONTENTS ===");
        foreach ((string relPath, string content) in currentFileContents)
        {
            prompt.AppendLine($"<<<FILE_START: {relPath}>>>");
            prompt.AppendLine(content);
            prompt.AppendLine($"<<<FILE_END: {relPath}>>>");
            prompt.AppendLine();
        }

        Stage4ExecutionArtifact artifact = await geminiClient.GenerateStructuredAsync<Stage4ExecutionArtifact>(
            systemInstruction,
            prompt.ToString(),
            useGoogleSearchGrounding: false,
            cancellationToken).ConfigureAwait(false);

        await ApplyFileMutationsToWorkspaceAsync(workspacePath, artifact.FileMutations, cancellationToken).ConfigureAwait(false);

        return new Stage4ExecutionArtifact
        {
            TaskTitle = string.IsNullOrWhiteSpace(artifact.TaskTitle) ? plan.TaskTitle : artifact.TaskTitle,
            AttemptNumber = attemptNumber,
            ImplementationSummary = artifact.ImplementationSummary,
            FileMutations = artifact.FileMutations,
            SelfHealingNotes = artifact.SelfHealingNotes
        };
    }

    /// <summary>
    /// Validates that no <see cref="FileMutation"/> contains lazy truncation placeholders before writing to disk.
    /// </summary>
    public static void ValidateMutationsBeforeWrite(IReadOnlyList<FileMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);

        foreach (FileMutation mutation in mutations)
        {
            if (string.IsNullOrWhiteSpace(mutation.RelativePath))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(mutation.UpdatedContent))
            {
                throw new InvalidOperationException(
                    $"Stage 4 validation failed: FileMutation for '{mutation.RelativePath}' produced empty UpdatedContent.");
            }

            foreach (string placeholder in ForbiddenTruncationPlaceholders)
            {
                if (mutation.UpdatedContent.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Stage 4 validation failed: FileMutation for '{mutation.RelativePath}' contains forbidden truncation placeholder '{placeholder}'. Full file content is required.");
                }
            }
        }
    }

    public static async Task ApplyFileMutationsToWorkspaceAsync(
        string workspacePath,
        IReadOnlyList<FileMutation> mutations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(mutations);

        ValidateMutationsBeforeWrite(mutations);

        string normalizedWorkspaceRoot = Path.GetFullPath(workspacePath);

        foreach (FileMutation mutation in mutations)
        {
            if (string.IsNullOrWhiteSpace(mutation.RelativePath))
            {
                continue;
            }

            string sanitizedRelative = mutation.RelativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            string fullTargetPath = Path.GetFullPath(Path.Combine(normalizedWorkspaceRoot, sanitizedRelative));

            // Security check: prevent path traversal outside the cloned workspace
            if (!fullTargetPath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Security violation: Attempted to write file '{mutation.RelativePath}' outside target workspace '{normalizedWorkspaceRoot}'.");
            }

            string? parentDir = Path.GetDirectoryName(fullTargetPath);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            await File.WriteAllTextAsync(fullTargetPath, mutation.UpdatedContent, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<Dictionary<string, string>> LoadTargetFilesAsync(
        string workspacePath,
        Stage1DiscoveryArtifact discovery,
        Stage3PlannerArtifact plan,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

        foreach (PlannedFileChange planned in plan.TargetFiles)
        {
            if (string.IsNullOrWhiteSpace(planned.RelativePath))
            {
                continue;
            }

            string fullPath = Path.Combine(workspacePath, planned.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                files[planned.RelativePath] = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            }
        }

        // Also include existing test files from discovery context if fewer than 3 files were loaded
        foreach (string testRel in discovery.ExistingTestFiles.Take(2))
        {
            if (files.ContainsKey(testRel))
            {
                continue;
            }

            string fullPath = Path.Combine(workspacePath, testRel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                files[testRel] = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            }
        }

        return files;
    }
}
