using System.Collections.Concurrent;
using System.Text;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Abstraction for dynamically composing stage-specific, stack-specific, and experiential skill packs
/// into agent system instructions.
/// </summary>
public interface ISkillRegistry
{
    string ComposeStageSystemInstruction(
        string baseInstruction,
        string stageKey,
        string detectedStack,
        string? experientialInjection = null);
}

/// <summary>
/// Loads modular Markdown skill packs from <c>skills/stages/*.md</c>, <c>skills/stacks/*.md</c>,
/// and <c>skills/learned-heuristics.md</c> and composes them into stage system instructions within a bounded token budget.
/// </summary>
public sealed class SkillRegistry : ISkillRegistry
{
    private readonly string _skillsDirectoryPath;
    private readonly int _maxInjectedCharacters;
    private readonly ConcurrentDictionary<string, string> _fileCache = new(StringComparer.OrdinalIgnoreCase);

    public SkillRegistry(string? skillsDirectoryPath = null, int maxInjectedCharacters = 6000)
    {
        _skillsDirectoryPath = !string.IsNullOrWhiteSpace(skillsDirectoryPath)
            ? skillsDirectoryPath
            : ResolveDefaultSkillsDirectory();
        _maxInjectedCharacters = Math.Clamp(maxInjectedCharacters, 1000, 24000);
    }

    public string SkillsDirectoryPath => _skillsDirectoryPath;

    /// <summary>
    /// Appends the matching stage skill pack, polyglot stack skill pack, learned heuristics playbook,
    /// and runtime experiential memory injection to <paramref name="baseInstruction"/>.
    /// </summary>
    public string ComposeStageSystemInstruction(
        string baseInstruction,
        string stageKey,
        string detectedStack,
        string? experientialInjection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseInstruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageKey);

        StringBuilder supplemental = new();

        string stageFileSlug = ResolveStageFileSlug(stageKey);
        string stageFilePath = Path.Combine(_skillsDirectoryPath, "stages", $"{stageFileSlug}.md");
        string? stageContent = TryReadSkillFile(stageFilePath);
        if (!string.IsNullOrWhiteSpace(stageContent))
        {
            supplemental.AppendLine();
            supplemental.AppendLine(stageContent.Trim());
        }

        string? stackFileSlug = ResolveStackFileSlug(detectedStack);
        if (!string.IsNullOrWhiteSpace(stackFileSlug))
        {
            string stackFilePath = Path.Combine(_skillsDirectoryPath, "stacks", $"{stackFileSlug}.md");
            string? stackContent = TryReadSkillFile(stackFilePath);
            if (!string.IsNullOrWhiteSpace(stackContent))
            {
                supplemental.AppendLine();
                supplemental.AppendLine(stackContent.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(experientialInjection))
        {
            supplemental.AppendLine();
            supplemental.AppendLine(experientialInjection.Trim());
        }
        else
        {
            string learnedHeuristicsPath = Path.Combine(_skillsDirectoryPath, "learned-heuristics.md");
            string? heuristicsContent = TryReadSkillFile(learnedHeuristicsPath);
            if (!string.IsNullOrWhiteSpace(heuristicsContent))
            {
                supplemental.AppendLine();
                supplemental.AppendLine(heuristicsContent.Trim());
            }
        }

        string combinedSupplemental = supplemental.ToString().Trim();
        if (string.IsNullOrWhiteSpace(combinedSupplemental))
        {
            return baseInstruction;
        }

        if (combinedSupplemental.Length > _maxInjectedCharacters)
        {
            combinedSupplemental = combinedSupplemental[.._maxInjectedCharacters];
        }

        return $"{baseInstruction.Trim()}\n\n{combinedSupplemental}";
    }

    public static string ResolveStageFileSlug(string stageKey)
    {
        if (string.IsNullOrWhiteSpace(stageKey))
        {
            return "stage1-discovery-ast";
        }

        string normalized = stageKey.Trim();
        if (normalized.StartsWith("stage", StringComparison.OrdinalIgnoreCase) && normalized.Contains('-'))
        {
            return normalized.ToLowerInvariant();
        }

        if (normalized.Contains("Stage1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Discovery", StringComparison.OrdinalIgnoreCase))
        {
            return "stage1-discovery-ast";
        }

        if (normalized.Contains("Stage2", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Finder", StringComparison.OrdinalIgnoreCase))
        {
            return "stage2-finder-grounding";
        }

        if (normalized.Contains("Stage3", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Planner", StringComparison.OrdinalIgnoreCase))
        {
            return "stage3-planner-safety";
        }

        if (normalized.Contains("Stage4", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Executor", StringComparison.OrdinalIgnoreCase))
        {
            return "stage4-executor-clean-diff";
        }

        if (normalized.Contains("Stage5", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Tester", StringComparison.OrdinalIgnoreCase))
        {
            return "stage5-tester-self-heal";
        }

        if (normalized.Contains("Stage6", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Auditor", StringComparison.OrdinalIgnoreCase))
        {
            return "stage6-auditor-adversarial";
        }

        return normalized.ToLowerInvariant();
    }

    public static string? ResolveStackFileSlug(string? detectedStack)
    {
        if (string.IsNullOrWhiteSpace(detectedStack))
        {
            return null;
        }

        if (detectedStack.Contains("DotNet", StringComparison.OrdinalIgnoreCase) ||
            detectedStack.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
            detectedStack.Contains("C#", StringComparison.OrdinalIgnoreCase))
        {
            return "dotnet";
        }

        if (detectedStack.Contains("Java", StringComparison.OrdinalIgnoreCase) ||
            detectedStack.Contains("Spring", StringComparison.OrdinalIgnoreCase) ||
            detectedStack.Contains("Gradle", StringComparison.OrdinalIgnoreCase) ||
            detectedStack.Contains("Maven", StringComparison.OrdinalIgnoreCase))
        {
            return "java-spring";
        }

        if (detectedStack.Contains("Python", StringComparison.OrdinalIgnoreCase))
        {
            return "python";
        }

        if (detectedStack.Contains("Node", StringComparison.OrdinalIgnoreCase) ||
            detectedStack.Contains("TypeScript", StringComparison.OrdinalIgnoreCase))
        {
            return "nodejs";
        }

        if (detectedStack.Contains("DevOps", StringComparison.OrdinalIgnoreCase) ||
            detectedStack.Contains("Infra", StringComparison.OrdinalIgnoreCase))
        {
            return "devops-infra";
        }

        return null;
    }

    private string? TryReadSkillFile(string fullPath)
    {
        if (_fileCache.TryGetValue(fullPath, out string? cached))
        {
            return cached;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            string content = File.ReadAllText(fullPath);
            _fileCache[fullPath] = content;
            return content;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveDefaultSkillsDirectory()
    {
        string current = Directory.GetCurrentDirectory();
        DirectoryInfo? dir = new(current);

        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "skills");
            if (Directory.Exists(candidate) || File.Exists(Path.Combine(dir.FullName, "AutonomousAgentHub.sln")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "skills");
    }
}
