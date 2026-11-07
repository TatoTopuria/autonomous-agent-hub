using AutonomousAgentHub.Core.Configuration;
using AutonomousAgentHub.Core.Infrastructure;
using Xunit;

namespace AutonomousAgentHub.Tests;

public sealed class PolyglotDetectorTests
{
    private readonly PolyglotBuildTestRunner _runner = new();

    [Fact]
    public void DetectStack_IdentifiesDotNetSolutionAndProject()
    {
        using TempWorkspace workspace = new();
        workspace.CreateFile("SampleApp.sln", "Microsoft Visual Studio Solution File");
        workspace.CreateFile("src/SampleApp/SampleApp.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        PolyglotStackDescriptor descriptor = _runner.DetectStack(workspace.RootPath);

        Assert.Equal(PolyglotStackType.DotNet, descriptor.StackType);
        Assert.Contains("dotnet build", descriptor.BuildCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet test", descriptor.TestCommand, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(descriptor.DetectedManifests);
    }

    [Fact]
    public void DetectStack_IdentifiesJavaMavenProject()
    {
        using TempWorkspace workspace = new();
        workspace.CreateFile("pom.xml", "<project><modelVersion>4.0.0</modelVersion></project>");
        workspace.CreateFile("src/main/java/com/example/App.java", "package com.example; public class App {}");

        PolyglotStackDescriptor descriptor = _runner.DetectStack(workspace.RootPath);

        Assert.Equal(PolyglotStackType.JavaMaven, descriptor.StackType);
        Assert.Contains("mvn test-compile", descriptor.BuildCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mvn test", descriptor.TestCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectStack_IdentifiesJavaGradleKotlinDslProject()
    {
        using TempWorkspace workspace = new();
        workspace.CreateFile("build.gradle.kts", "plugins { java }");
        workspace.CreateFile("gradlew", "#!/bin/sh");
        workspace.CreateFile("gradlew.bat", "@echo off");

        PolyglotStackDescriptor descriptor = _runner.DetectStack(workspace.RootPath);

        Assert.Equal(PolyglotStackType.JavaGradle, descriptor.StackType);
        Assert.Contains("test", descriptor.TestCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build.gradle.kts", descriptor.DetectedManifests);
    }

    [Fact]
    public void DetectStack_IdentifiesPythonPyprojectAndRequirements()
    {
        using TempWorkspace workspace = new();
        workspace.CreateFile("pyproject.toml", "[project]\nname = \"clinical-api\"");
        workspace.CreateFile("requirements.txt", "fastapi==0.115.0\npytest==8.3.0");
        workspace.CreateFile("app/main.py", "def main(): pass");

        PolyglotStackDescriptor descriptor = _runner.DetectStack(workspace.RootPath);

        Assert.Equal(PolyglotStackType.Python, descriptor.StackType);
        Assert.Contains(".venv", descriptor.BuildCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pytest", descriptor.TestCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectStack_IdentifiesNodeJsPackageJson()
    {
        using TempWorkspace workspace = new();
        workspace.CreateFile("package.json", "{ \"name\": \"gateway\", \"scripts\": { \"test\": \"vitest run\" } }");
        workspace.CreateFile("tsconfig.json", "{ \"compilerOptions\": { \"strict\": true } }");

        PolyglotStackDescriptor descriptor = _runner.DetectStack(workspace.RootPath);

        Assert.Equal(PolyglotStackType.NodeJs, descriptor.StackType);
        Assert.Contains("npm", descriptor.BuildCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("npm test", descriptor.TestCommand);
    }

    [Fact]
    public void DetectStack_HonorsCustomBuildAndTestCommandOverrides()
    {
        using TempWorkspace workspace = new();
        workspace.CreateFile("package.json", "{ \"name\": \"custom-node\" }");

        RepositoryEntry overrides = new()
        {
            Owner = "TatoTopuria",
            Name = "custom-node",
            CustomBuildCommand = "npm run compile:prod",
            CustomTestCommand = "npm run test:ci"
        };

        PolyglotStackDescriptor descriptor = _runner.DetectStack(workspace.RootPath, overrides);

        Assert.Equal(PolyglotStackType.NodeJs, descriptor.StackType);
        Assert.Equal("npm run compile:prod", descriptor.BuildCommand);
        Assert.Equal("npm run test:ci", descriptor.TestCommand);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), $"polyglot-test-{Guid.NewGuid():N}");

        public TempWorkspace()
        {
            Directory.CreateDirectory(RootPath);
        }

        public void CreateFile(string relativePath, string content)
        {
            string fullPath = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(fullPath, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

