using System.Text;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;
using AutonomousAgentHub.Core.Infrastructure;

namespace AutonomousAgentHub.Core.Pipeline;

/// <summary>
/// Stage 1: Strictly Read-Only Repository &amp; Domain Discovery Agent.
/// Inspects the cloned target repository without modifying any file, maps topology and recent Git history,
/// extracts structural AST symbol summaries, auto-detects the polyglot build stack, and synthesizes <see cref="Stage1DiscoveryArtifact"/>.
/// </summary>
public sealed class Stage1DiscoveryAgent(
    IGeminiClient geminiClient,
    IBuildTestRunner buildTestRunner,
    IGitWorkspaceManager gitWorkspaceManager,
    ISkillRegistry? skillRegistry = null,
    ExperientialMemoryManager? experientialMemoryManager = null,
    IMcpToolManager? mcpToolManager = null)
{
    private static readonly SkillRegistry DefaultSkillRegistry = new();

    private static readonly HashSet<string> IgnoredDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".venv", "venv", "target", "build", "dist", ".gradle", ".vs", ".idea"
    };

    private static readonly string[] SourceFileExtensions =
    [
        ".cs", ".java", ".kt", ".kts", ".py", ".ts", ".tsx", ".js", ".mjs", ".json", ".yml", ".yaml", ".xml", ".toml", ".gradle", ".slnx", ".tf", ".sh"
    ];

    public async Task<Stage1DiscoveryArtifact> ExecuteAsync(
        RepositoryEntry repository,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        Console.WriteLine($"[Stage 1: Discovery] Inspecting read-only workspace '{workspacePath}' for '{repository.FullName}'...");

        PolyglotStackDescriptor stack = buildTestRunner.DetectStack(workspacePath, repository);
        List<string> relativeFiles = CollectRelativeFileMap(workspacePath, maxFiles: 300);

        List<string> manifests =
        [
            .. relativeFiles.Where(IsManifestOrConfigFile).Take(30)
        ];

        List<string> entrypoints =
        [
            .. relativeFiles.Where(IsArchitecturalEntrypoint).Take(30)
        ];

        List<string> testFiles =
        [
            .. relativeFiles.Where(IsTestFilePath).Take(40)
        ];

        List<string> recentCommits;
        try
        {
            recentCommits = await gitWorkspaceManager.GetRecentCommitsAsync(workspacePath, 15, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            recentCommits = [];
        }

        List<InspectedFileSnapshot> initialSnapshots = await LoadCandidateSnapshotsAsync(
            workspacePath,
            manifests,
            entrypoints,
            testFiles,
            relativeFiles,
            cancellationToken).ConfigureAwait(false);

        string baseSystemInstruction =
            """
            You are Stage 1 (Discovery Agent) of the Autonomous Evolution Hub.
            You operate in STRICTLY READ-ONLY mode. Your objective is to analyze the target repository's topology,
            manifests, recent commit history, and source code snapshots to produce a comprehensive JSON Stage1DiscoveryArtifact.
            Identify:
            1. domainPurpose: Clear domain purpose of the repository.
            2. architecturalStyle: Architectural pattern in use (e.g., Clean Architecture, Hexagonal, Layered Spring Boot, FastAPI Service, Express/NestJS Gateway, Cloud-Native DevOps).
            3. dataAndControlFlows: Key request/data/control flow paths through the system.
            4. criticalBusinessInvariants: Non-negotiable business and domain invariants that must NEVER be violated by any code change.
            5. candidateFilesForInspection: Up to 8 high-leverage source/test files suitable for safe improvements.
            Return ONLY valid JSON matching the Stage1DiscoveryArtifact contract.
            """;

        string detectedStackName = stack.StackType.ToString();
        string? experientialInjection = experientialMemoryManager?.BuildStagePromptInjection("Stage1Discovery", detectedStackName);
        string systemInstruction = (skillRegistry ?? DefaultSkillRegistry).ComposeStageSystemInstruction(
            baseSystemInstruction,
            "stage1-discovery-ast",
            detectedStackName,
            experientialInjection);

        StringBuilder promptBuilder = new();
        promptBuilder.AppendLine($"Repository: {repository.FullName}");
        promptBuilder.AppendLine($"Configured Domain: {repository.Domain}");
        promptBuilder.AppendLine($"Auto-Detected Stack: {stack.StackType} ({stack.DisplayName})");
        promptBuilder.AppendLine($"Build Command: {stack.BuildCommand}");
        promptBuilder.AppendLine($"Test Command: {stack.TestCommand}");

        if (mcpToolManager is not null)
        {
            string mcpSummary = mcpToolManager.BuildStageMcpContextSummary("Stage1Discovery", workspacePath);
            if (!string.IsNullOrWhiteSpace(mcpSummary))
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine(mcpSummary);
            }
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Directory Tree Summary:");
        foreach (string path in relativeFiles.Take(120))
        {
            promptBuilder.AppendLine($"- {path}");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Recent Git Commits (git log -n 15):");
        foreach (string commit in recentCommits)
        {
            promptBuilder.AppendLine($"- {commit}");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Inspected File Excerpts:");
        foreach (InspectedFileSnapshot snapshot in initialSnapshots)
        {
            promptBuilder.AppendLine($"--- FILE: {snapshot.RelativePath} ({snapshot.ReasonForSelection}) ---");
            promptBuilder.AppendLine(snapshot.ContentExcerpt);
            promptBuilder.AppendLine();
        }

        Stage1DiscoveryArtifact llmArtifact = await geminiClient.GenerateStructuredAsync<Stage1DiscoveryArtifact>(
            systemInstruction,
            promptBuilder.ToString(),
            useGoogleSearchGrounding: false,
            cancellationToken).ConfigureAwait(false);

        // Enrich candidate files by loading actual file contents from disk for any additional file paths requested by Gemini
        List<InspectedFileSnapshot> enrichedSnapshots = await EnrichCandidateSnapshotsAsync(
            workspacePath,
            initialSnapshots,
            llmArtifact.CandidateFilesForInspection,
            cancellationToken).ConfigureAwait(false);

        return new Stage1DiscoveryArtifact
        {
            RepositoryFullName = repository.FullName,
            DetectedStack = stack.StackType.ToString(),
            DomainPurpose = string.IsNullOrWhiteSpace(llmArtifact.DomainPurpose) ? repository.Domain : llmArtifact.DomainPurpose,
            ArchitecturalStyle = string.IsNullOrWhiteSpace(llmArtifact.ArchitecturalStyle) ? "Modular Service Architecture" : llmArtifact.ArchitecturalStyle,
            DataAndControlFlows = llmArtifact.DataAndControlFlows.Count > 0
                ? llmArtifact.DataAndControlFlows
                : ["Entrypoint -> Domain/Application Logic -> Persistence/External IO"],
            CriticalBusinessInvariants = llmArtifact.CriticalBusinessInvariants.Count > 0
                ? llmArtifact.CriticalBusinessInvariants
                : ["Preserve public API contracts and existing test suite behavior."],
            DirectoryTreeSummary = [.. relativeFiles.Take(150)],
            BuildAndDependencyManifests = manifests,
            KeyArchitecturalEntrypoints = entrypoints,
            ExistingTestFiles = testFiles,
            RecentGitCommits = recentCommits,
            CandidateFilesForInspection = enrichedSnapshots
        };
    }

    /// <summary>
    /// Extracts structural type and method signature headers from source code so truncated files retain complete API visibility.
    /// </summary>
    public static string ExtractStructuralSymbolSummary(string content, int maxSignatures = 35)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        List<string> symbols = [];
        string[] lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string line in lines)
        {
            if (symbols.Count >= maxSignatures)
            {
                break;
            }

            if (line.StartsWith("//", StringComparison.Ordinal) ||
                line.StartsWith('#') ||
                line.StartsWith("/*", StringComparison.Ordinal) ||
                line.StartsWith('*'))
            {
                continue;
            }

            if (line.Contains("class ", StringComparison.Ordinal) ||
                line.Contains("interface ", StringComparison.Ordinal) ||
                line.Contains("record ", StringComparison.Ordinal) ||
                line.Contains("enum ", StringComparison.Ordinal) ||
                line.StartsWith("public ", StringComparison.Ordinal) ||
                line.StartsWith("protected ", StringComparison.Ordinal) ||
                line.StartsWith("internal ", StringComparison.Ordinal) ||
                line.StartsWith("def ", StringComparison.Ordinal) ||
                line.StartsWith("async def ", StringComparison.Ordinal) ||
                line.StartsWith("export function ", StringComparison.Ordinal) ||
                line.StartsWith("export async function ", StringComparison.Ordinal))
            {
                string signature = line.TrimEnd('{', ' ');
                if (signature.Length > 180)
                {
                    signature = signature[..180] + "...";
                }

                symbols.Add(signature);
            }
        }

        return symbols.Count == 0
            ? string.Empty
            : "// [STRUCTURAL SYMBOL MAP]\n// " + string.Join("\n// ", symbols) + "\n";
    }

    private static string BuildSnapshotExcerpt(string content, int maxChars)
    {
        if (content.Length <= maxChars)
        {
            return content;
        }

        string symbolHeader = ExtractStructuralSymbolSummary(content);
        int remainingBudget = Math.Max(1000, maxChars - symbolHeader.Length);
        return $"{symbolHeader}{content[..remainingBudget]}";
    }

    private static async Task<List<InspectedFileSnapshot>> LoadCandidateSnapshotsAsync(
        string workspacePath,
        IReadOnlyList<string> manifests,
        IReadOnlyList<string> entrypoints,
        IReadOnlyList<string> testFiles,
        IReadOnlyList<string> allRelativeFiles,
        CancellationToken cancellationToken)
    {
        List<(string Path, string Reason)> candidates = [];

        foreach (string m in manifests.Take(3))
        {
            candidates.Add((m, "Build & Dependency Manifest"));
        }

        foreach (string e in entrypoints.Take(4))
        {
            if (candidates.All(c => !string.Equals(c.Path, e, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add((e, "Architectural Entrypoint / Core Logic"));
            }
        }

        foreach (string t in testFiles.Take(2))
        {
            if (candidates.All(c => !string.Equals(c.Path, t, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add((t, "Existing Test Suite Sample"));
            }
        }

        foreach (string file in allRelativeFiles.Where(f => SourceFileExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).Take(8))
        {
            if (candidates.Count >= 8)
            {
                break;
            }

            if (candidates.All(c => !string.Equals(c.Path, file, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add((file, "Source Module"));
            }
        }

        List<InspectedFileSnapshot> snapshots = [];
        foreach ((string relPath, string reason) in candidates)
        {
            string fullPath = Path.Combine(workspacePath, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            snapshots.Add(new InspectedFileSnapshot
            {
                RelativePath = relPath,
                ReasonForSelection = reason,
                ContentExcerpt = BuildSnapshotExcerpt(content, maxChars: 4000)
            });
        }

        return snapshots;
    }

    private static async Task<List<InspectedFileSnapshot>> EnrichCandidateSnapshotsAsync(
        string workspacePath,
        List<InspectedFileSnapshot> initialSnapshots,
        List<InspectedFileSnapshot> requestedByLlm,
        CancellationToken cancellationToken)
    {
        Dictionary<string, InspectedFileSnapshot> byPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (InspectedFileSnapshot snap in initialSnapshots)
        {
            byPath[snap.RelativePath] = snap;
        }

        foreach (InspectedFileSnapshot req in requestedByLlm)
        {
            if (string.IsNullOrWhiteSpace(req.RelativePath) || byPath.ContainsKey(req.RelativePath))
            {
                continue;
            }

            string fullPath = Path.Combine(workspacePath, req.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                string content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                byPath[req.RelativePath] = new InspectedFileSnapshot
                {
                    RelativePath = req.RelativePath.Replace('\\', '/'),
                    ReasonForSelection = string.IsNullOrWhiteSpace(req.ReasonForSelection)
                        ? "Identified by Discovery Agent for deep inspection"
                        : req.ReasonForSelection,
                    ContentExcerpt = BuildSnapshotExcerpt(content, maxChars: 6000)
                };
            }
        }

        return [.. byPath.Values];
    }

    private static List<string> CollectRelativeFileMap(string workspacePath, int maxFiles)
    {
        List<string> files = [];

        void Walk(string dir, int depth)
        {
            if (depth > 6 || files.Count >= maxFiles)
            {
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(dir))
                {
                    if (files.Count >= maxFiles)
                    {
                        return;
                    }

                    string rel = Path.GetRelativePath(workspacePath, file).Replace('\\', '/');
                    files.Add(rel);
                }

                foreach (string subDir in Directory.EnumerateDirectories(dir))
                {
                    string name = Path.GetFileName(subDir);
                    if (!IgnoredDirNames.Contains(name))
                    {
                        Walk(subDir, depth + 1);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore restricted paths
            }
        }

        Walk(workspacePath, 0);
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private static bool IsManifestOrConfigFile(string relativePath)
    {
        string name = Path.GetFileName(relativePath);
        return relativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".tf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Directory.Packages.props", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "pom.xml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "build.gradle", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "build.gradle.kts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "settings.gradle.kts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "pyproject.toml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "requirements.txt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "setup.py", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "package.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "tsconfig.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "docker-compose.yml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Dockerfile", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("application.", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsArchitecturalEntrypoint(string relativePath)
    {
        string name = Path.GetFileName(relativePath);
        return string.Equals(name, "Program.cs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Startup.cs", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Application.java", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Controller.java", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Service.java", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Handler.cs", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Handler.java", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Repository.java", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Validator.cs", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("router", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".tf", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("prometheus", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "main.py", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "app.py", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "index.ts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "server.ts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "index.js", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestFilePath(string relativePath)
    {
        return relativePath.Contains("test", StringComparison.OrdinalIgnoreCase) ||
               relativePath.Contains("spec", StringComparison.OrdinalIgnoreCase);
    }
}
