using System.Text;
using System.Text.RegularExpressions;
using AutonomousAgentHub.Core.Contracts;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Extracts high-signal compiler errors, file/line coordinates, and failing unit test assertions across
/// .NET/C#, Java (Maven/Gradle), Python (<c>pytest</c>), and Node.js/TypeScript outputs so Stage 4 self-healing
/// retries focus immediately on exact root causes instead of wading through raw build logs.
/// </summary>
public static partial class StackTraceDiagnosticParser
{
    /// <summary>
    /// Extracts up to <paramref name="maxDiagnostics"/> structured diagnostic lines from combined stdout/stderr.
    /// </summary>
    public static List<string> ExtractKeyDiagnostics(string? stdout, string? stderr, int maxDiagnostics = 15)
    {
        string combined = $"{stderr}\n{stdout}".Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return [];
        }

        List<string> extracted = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        string[] lines = combined.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string line in lines)
        {
            if (extracted.Count >= maxDiagnostics)
            {
                break;
            }

            if (IsHighSignalDiagnosticLine(line) && seen.Add(line))
            {
                extracted.Add(line.Length <= 320 ? line : line[..320] + "...");
            }
        }

        return extracted;
    }

    /// <summary>
    /// Builds a prioritized failure trace for <c>Stage4ExecutorAgent</c> self-healing retries, placing extracted
    /// compiler/test diagnostics at the very top before raw stdout/stderr excerpts.
    /// </summary>
    public static string BuildFocusedFailureTrace(TestExecutionAttempt attemptResult)
    {
        ArgumentNullException.ThrowIfNull(attemptResult);

        List<string> diagnostics = ExtractKeyDiagnostics(attemptResult.StandardOutput, attemptResult.StandardError, maxDiagnostics: 18);

        StringBuilder sb = new();
        sb.AppendLine($"Attempt {attemptResult.AttemptNumber} failed ({attemptResult.FailureSummary ?? "Build/Test error"}).");
        sb.AppendLine($"Exit Code: {attemptResult.ExitCode} | BuildSucceeded: {attemptResult.BuildSucceeded} | TestsSucceeded: {attemptResult.TestsSucceeded}");

        if (diagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- EXTRACTED HIGH-PRIORITY COMPILER / TEST DIAGNOSTICS (FIX THESE FIRST) ---");
            foreach (string diag in diagnostics)
            {
                sb.AppendLine($"* {diag}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- STDOUT ---");
        sb.AppendLine(attemptResult.StandardOutput);
        sb.AppendLine("--- STDERR ---");
        sb.AppendLine(attemptResult.StandardError);

        return sb.ToString();
    }

    private static bool IsHighSignalDiagnosticLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        // .NET / C# compiler & xUnit diagnostics
        if (DotNetDiagnosticRegex().IsMatch(line) ||
            line.StartsWith("Failed ", StringComparison.Ordinal) ||
            line.Contains("Assert.", StringComparison.Ordinal))
        {
            return true;
        }

        // TypeScript / Node.js diagnostics
        if (TypeScriptDiagnosticRegex().IsMatch(line) ||
            line.StartsWith("FAIL ", StringComparison.Ordinal) ||
            line.StartsWith("AssertionError", StringComparison.Ordinal))
        {
            return true;
        }

        // Java / Maven / Gradle diagnostics
        if (line.StartsWith("[ERROR]", StringComparison.Ordinal) ||
            JavaCompilerDiagnosticRegex().IsMatch(line) ||
            line.Contains("org.opentest4j.AssertionFailedError", StringComparison.Ordinal))
        {
            return true;
        }

        // Python / pytest diagnostics
        if (line.StartsWith("FAILED ", StringComparison.Ordinal) ||
            line.StartsWith("E   ", StringComparison.Ordinal) ||
            line.StartsWith("SyntaxError:", StringComparison.Ordinal) ||
            line.StartsWith("ImportError:", StringComparison.Ordinal) ||
            line.StartsWith("ModuleNotFoundError:", StringComparison.Ordinal) ||
            line.StartsWith("TypeError:", StringComparison.Ordinal) ||
            line.StartsWith("ValidationError:", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"\berror\s+CS\d{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetDiagnosticRegex();

    [GeneratedRegex(@"\berror\s+TS\d{4,5}\b", RegexOptions.IgnoreCase)]
    private static partial Regex TypeScriptDiagnosticRegex();

    [GeneratedRegex(@"\.(java|kt):\d+:\s*error:", RegexOptions.IgnoreCase)]
    private static partial Regex JavaCompilerDiagnosticRegex();
}

