using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Contracts;

namespace AutonomousAgentHub.Core.Infrastructure;

/// <summary>
/// Supported polyglot technology stacks recognized by <see cref="PolyglotBuildTestRunner"/>.
/// </summary>
public enum PolyglotStackType
{
    Unknown = 0,
    DotNet = 1,
    JavaMaven = 2,
    JavaGradle = 3,
    Python = 4,
    NodeJs = 5
}

/// <summary>
/// Describes the detected technology stack, manifest files, and resolved build/test commands for a repository.
/// </summary>
public sealed record PolyglotStackDescriptor(
    PolyglotStackType StackType,
    string DisplayName,
    string BuildCommand,
    string TestCommand,
    IReadOnlyList<string> DetectedManifests);

/// <summary>
/// Abstraction for running polyglot builds and test suites in target workspaces.
/// </summary>
public interface IBuildTestRunner
{
    PolyglotStackDescriptor DetectStack(string workspacePath, RepositoryEntry? repoOverrides = null);

    Task<TestExecutionAttempt> RunBuildAndTestsAsync(
        string workspacePath,
        PolyglotStackDescriptor stackDescriptor,
        int attemptNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Auto-detects .NET/C#, Java (Maven/Gradle), Python, and Node.js/TypeScript repositories
/// and executes their build and test pipelines in an isolated child process.
/// </summary>
public sealed class PolyglotBuildTestRunner : IBuildTestRunner
{
    private readonly TimeSpan _commandTimeout;

    public PolyglotBuildTestRunner(TimeSpan? commandTimeout = null)
    {
        _commandTimeout = commandTimeout ?? TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// Inspects the target repository directory to determine its primary technology stack and commands.
    /// </summary>
    public PolyglotStackDescriptor DetectStack(string workspacePath, RepositoryEntry? repoOverrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        if (!Directory.Exists(workspacePath))
        {
            throw new DirectoryNotFoundException($"Target workspace directory not found: '{workspacePath}'.");
        }

        List<string> allFiles = EnumerateRepositoryFiles(workspacePath, maxDepth: 4);
        List<string> relativeFiles = [.. allFiles.Select(f => Path.GetRelativePath(workspacePath, f).Replace('\\', '/'))];

        // 1. Check .NET / C# (*.sln, *.csproj)
        List<string> dotnetManifests =
        [
            .. relativeFiles.Where(f =>
                f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        ];

        if (dotnetManifests.Count > 0)
        {
            return new PolyglotStackDescriptor(
                PolyglotStackType.DotNet,
                ".NET / C#",
                repoOverrides?.CustomBuildCommand ?? "dotnet restore && dotnet build --no-restore --configuration Release",
                repoOverrides?.CustomTestCommand ?? "dotnet test --no-build --configuration Release --verbosity normal",
                dotnetManifests);
        }

        // 2. Check Java / Spring — Maven (pom.xml)
        List<string> mavenManifests =
        [
            .. relativeFiles.Where(f =>
                string.Equals(Path.GetFileName(f), "pom.xml", StringComparison.OrdinalIgnoreCase))
        ];

        if (mavenManifests.Count > 0)
        {
            return new PolyglotStackDescriptor(
                PolyglotStackType.JavaMaven,
                "Java / Maven",
                repoOverrides?.CustomBuildCommand ?? "mvn test-compile -B",
                repoOverrides?.CustomTestCommand ?? "mvn test -B",
                mavenManifests);
        }

        // 3. Check Java / Spring — Gradle (build.gradle, build.gradle.kts)
        List<string> gradleManifests =
        [
            .. relativeFiles.Where(f =>
                string.Equals(Path.GetFileName(f), "build.gradle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(f), "build.gradle.kts", StringComparison.OrdinalIgnoreCase))
        ];

        if (gradleManifests.Count > 0)
        {
            bool hasWrapper = File.Exists(Path.Combine(workspacePath, "gradlew")) ||
                              File.Exists(Path.Combine(workspacePath, "gradlew.bat"));
            string gradleExecutable = hasWrapper
                ? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "gradlew.bat" : "./gradlew")
                : "gradle";

            return new PolyglotStackDescriptor(
                PolyglotStackType.JavaGradle,
                "Java / Gradle",
                repoOverrides?.CustomBuildCommand ?? $"{gradleExecutable} testClasses --no-daemon",
                repoOverrides?.CustomTestCommand ?? $"{gradleExecutable} test --no-daemon",
                gradleManifests);
        }

        // 4. Check Python (pyproject.toml, requirements.txt, setup.py)
        List<string> pythonManifests =
        [
            .. relativeFiles.Where(f =>
            {
                string fileName = Path.GetFileName(f);
                return string.Equals(fileName, "pyproject.toml", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fileName, "requirements.txt", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fileName, "setup.py", StringComparison.OrdinalIgnoreCase);
            })
        ];

        if (pythonManifests.Count > 0)
        {
            string defaultPythonBuild =
                "python3 -m venv .venv && . .venv/bin/activate && pip install --upgrade pip && " +
                "(if [ -f requirements.txt ]; then pip install -r requirements.txt; fi) && " +
                "(if [ -f pyproject.toml ]; then pip install -e . || true; fi) && " +
                "pip install pytest";

            string defaultPythonTest =
                ". .venv/bin/activate && (pytest || python3 -m unittest discover)";

            return new PolyglotStackDescriptor(
                PolyglotStackType.Python,
                "Python",
                repoOverrides?.CustomBuildCommand ?? defaultPythonBuild,
                repoOverrides?.CustomTestCommand ?? defaultPythonTest,
                pythonManifests);
        }

        // 5. Check Node.js / JS / TS (package.json)
        List<string> nodeManifests =
        [
            .. relativeFiles.Where(f =>
                string.Equals(Path.GetFileName(f), "package.json", StringComparison.OrdinalIgnoreCase))
        ];

        if (nodeManifests.Count > 0)
        {
            return new PolyglotStackDescriptor(
                PolyglotStackType.NodeJs,
                "Node.js / TypeScript",
                repoOverrides?.CustomBuildCommand ?? "(npm ci || npm install) && npm run build --if-present",
                repoOverrides?.CustomTestCommand ?? "npm test",
                nodeManifests);
        }

        return new PolyglotStackDescriptor(
            PolyglotStackType.Unknown,
            "Unknown Stack",
            repoOverrides?.CustomBuildCommand ?? "echo 'No build command configured'",
            repoOverrides?.CustomTestCommand ?? "echo 'No test command configured'",
            []);
    }

    /// <summary>
    /// Executes the build and test commands sequentially inside the target workspace and returns a structured report.
    /// </summary>
    public async Task<TestExecutionAttempt> RunBuildAndTestsAsync(
        string workspacePath,
        PolyglotStackDescriptor stackDescriptor,
        int attemptNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(stackDescriptor);

        Stopwatch stopwatch = Stopwatch.StartNew();

        ProcessResult buildResult = await RunShellCommandAsync(
            workspacePath,
            stackDescriptor.BuildCommand,
            _commandTimeout,
            cancellationToken).ConfigureAwait(false);

        if (buildResult.ExitCode != 0)
        {
            stopwatch.Stop();
            return new TestExecutionAttempt
            {
                AttemptNumber = attemptNumber,
                BuildSucceeded = false,
                TestsSucceeded = false,
                ExitCode = buildResult.ExitCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                StandardOutput = TruncateOutput(buildResult.StandardOutput),
                StandardError = TruncateOutput(buildResult.StandardError),
                FailureSummary = $"Build failed with exit code {buildResult.ExitCode}."
            };
        }

        ProcessResult testResult = await RunShellCommandAsync(
            workspacePath,
            stackDescriptor.TestCommand,
            _commandTimeout,
            cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        bool testsPassed = testResult.ExitCode == 0;

        string combinedStdout = $"{buildResult.StandardOutput}\n{testResult.StandardOutput}".Trim();
        string combinedStderr = $"{buildResult.StandardError}\n{testResult.StandardError}".Trim();

        return new TestExecutionAttempt
        {
            AttemptNumber = attemptNumber,
            BuildSucceeded = true,
            TestsSucceeded = testsPassed,
            ExitCode = testResult.ExitCode,
            DurationMs = stopwatch.ElapsedMilliseconds,
            StandardOutput = TruncateOutput(combinedStdout),
            StandardError = TruncateOutput(combinedStderr),
            FailureSummary = testsPassed
                ? null
                : $"Test execution failed with exit code {testResult.ExitCode}."
        };
    }

    internal static async Task<ProcessResult> RunShellCommandAsync(
        string workingDirectory,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string shellFile = isWindows ? "cmd.exe" : "/bin/bash";

        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shellFile,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (isWindows)
        {
            process.StartInfo.ArgumentList.Add("/c");
            process.StartInfo.ArgumentList.Add(command);
        }
        else
        {
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add(command);
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

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore cleanup failure when killing timed-out child process tree
            }

            throw;
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static List<string> EnumerateRepositoryFiles(string rootPath, int maxDepth)
    {
        List<string> results = [];
        HashSet<string> ignoredDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", ".venv", "venv", "target", "build", "dist", ".gradle", ".idea", ".vs"
        };

        void Traverse(string currentDir, int depth)
        {
            if (depth > maxDepth)
            {
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(currentDir))
                {
                    results.Add(file);
                }

                foreach (string subDir in Directory.EnumerateDirectories(currentDir))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (!ignoredDirectories.Contains(dirName))
                    {
                        Traverse(subDir, depth + 1);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible directories
            }
        }

        Traverse(rootPath, 0);
        return results;
    }

    private static string TruncateOutput(string text, int maxChars = 16000)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        return string.Concat(
            text.AsSpan(0, maxChars / 2),
            "\n... [TRUNCATED MIDDLE OUTPUT] ...\n",
            text.AsSpan(text.Length - (maxChars / 2)));
    }

    internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

