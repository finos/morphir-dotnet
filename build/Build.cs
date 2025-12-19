using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using SemVersion;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Morphir .NET build orchestration using Nuke
/// Migrated from justfile + C# scripts to provide strongly-typed, cross-platform build automation
/// </summary>
partial class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Release'")]
    readonly string Configuration = "Release";

    [Parameter("Version string for packages and executables (overrides CHANGELOG.md)")]
    readonly string? VersionOverride;

    [Parameter("Output directory for packages")]
    readonly AbsolutePath OutputDir = RootDirectory / "artifacts" / "packages";

    [Parameter("Output directory for executables")]
    readonly AbsolutePath ExecutablesDir = RootDirectory / "artifacts" / "executables";

    [Parameter("Output directory for single-file executables")]
    readonly AbsolutePath SingleFileDir = RootDirectory / "artifacts" / "single-file";

    [Parameter("Output directory for untrimmed single-file executables")]
    readonly AbsolutePath SingleFileUntrimmedDir = RootDirectory / "artifacts" / "single-file-untrimmed";

    [Parameter("Output directory for tool DLLs")]
    readonly AbsolutePath ToolDllDir = RootDirectory / "artifacts" / "tool-dll";

    [Parameter("Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)")]
    readonly string Rid;

    [Parameter("NuGet source URL")]
    readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("NuGet API key for publishing")]
    readonly string ApiKey;

    [Parameter("Local NuGet feed directory")]
    readonly AbsolutePath LocalSource = RootDirectory / "artifacts" / "local-feed";

    [Parameter("Install tool globally (true) or locally (false)")]
    readonly bool Global = false;

    [Parameter("Executable type for E2E tests: aot, trimmed, untrimmed, or all")]
    readonly string ExecutableType = "all";

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ScriptsDirectory => RootDirectory / "scripts";
    AbsolutePath SolutionFile => RootDirectory / "Morphir.slnx";
    AbsolutePath ChangelogFile => RootDirectory / "CHANGELOG.md";

    /// <summary>
    /// Gets the version from CHANGELOG.md (or uses VersionOverride if provided)
    /// </summary>
    SemanticVersion Version => VersionOverride != null 
        ? SemanticVersion.Parse(VersionOverride) 
        : new FileInfo(ChangelogFile).GetVersionFromChangelog();

    /// <summary>
    /// Gets the release notes for the latest release from CHANGELOG.md
    /// </summary>
    string ReleaseNotes => new FileInfo(ChangelogFile).GetReleaseNotes();

    // Project paths
    AbsolutePath MorphirCoreProject => SourceDirectory / "Morphir.Core" / "Morphir.Core.csproj";
    AbsolutePath MorphirToolingProject => SourceDirectory / "Morphir.Tooling" / "Morphir.Tooling.csproj";
    AbsolutePath MorphirProject => SourceDirectory / "Morphir" / "Morphir.csproj";
    AbsolutePath MorphirToolProject => SourceDirectory / "Morphir.Tool" / "Morphir.Tool.csproj";
    AbsolutePath MorphirCoreTestsProject => TestsDirectory / "Morphir.Core.Tests" / "Morphir.Core.Tests.csproj";
    AbsolutePath MorphirToolingTestsProject => TestsDirectory / "Morphir.Tooling.Tests" / "Morphir.Tooling.Tests.csproj";
    AbsolutePath MorphirE2ETestsProject => TestsDirectory / "Morphir.E2E.Tests" / "Morphir.E2E.Tests.csproj";

    /// <summary>
    /// Clean build artifacts and output directories
    /// Removes all bin/ and obj/ folders and recreates output directories
    /// </summary>
    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            OutputDir.CreateOrCleanDirectory();
            ExecutablesDir.CreateOrCleanDirectory();
            SingleFileDir.CreateOrCleanDirectory();
            SingleFileUntrimmedDir.CreateOrCleanDirectory();
        });

    /// <summary>
    /// Restore NuGet packages for the solution
    /// Uses the .slnx solution file
    /// </summary>
    Target Restore => _ => _
        .Executes(() =>
        {
            // Restore using .slnx solution file
            DotNetRestore(s => s
                .SetProjectFile(SolutionFile));
        });

    /// <summary>
    /// Compile all projects in the solution
    /// Note: VBCSCompiler processes are cleaned up on Windows before build to prevent file locking issues.
    /// Parallel builds are now enabled since the GenerateWolverineCode MSBuild target was removed.
    /// </summary>
    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            // Kill VBCSCompiler on Windows to prevent file locking issues
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    // Kill any lingering VBCSCompiler processes that might lock DLLs
                    Process.GetProcessesByName("VBCSCompiler").ForEach(p =>
                    {
                        try
                        {
                            Serilog.Log.Debug($"Killing VBCSCompiler process {p.Id}");
                            p.Kill();
                            p.WaitForExit();
                        }
                        catch
                        {
                            // Ignore if process already terminated
                        }
                    });
                }
                catch
                {
                    // Ignore if no processes found
                }
            }

            // Build using .slnx solution file
            // Parallel builds now enabled after removing GenerateWolverineCode MSBuild target
            DotNetBuild(s => s
                .SetProjectFile(SolutionFile)
                .SetConfiguration(Configuration)
                .SetNoRestore(true));
        });

    /// <summary>
    /// Format code using dotnet format (applies formatting changes)
    /// </summary>
    Target Format => _ => _
        .Description("Format code (applies formatting changes)")
        .Executes(() =>
        {
            DotNet($"format");
        });

    /// <summary>
    /// Verify code formatting without making changes (linting)
    /// </summary>
    Target Lint => _ => _
        .Description("Run linting/formatting checks (verifies without making changes)")
        .Executes(() =>
        {
            DotNet($"format --verify-no-changes");
        });

    /// <summary>
    /// Run pre-commit checks (lint)
    /// </summary>
    Target Check => _ => _
        .DependsOn(Lint)
        .Description("Check task that runs lint");

    /// <summary>
    /// Alias for Check target (pre-commit hook)
    /// </summary>
    Target Precommit => _ => _
        .DependsOn(Lint)
        .Description("Pre-commit hook task (runs lint)");

    /// <summary>
    /// Complete CI pipeline: restore, compile, test, and check
    /// </summary>
    Target CI => _ => _
        .DependsOn(Restore, Compile, Test, Check)
        .Description("Full CI pipeline: restore, build, test, and check")
        .Executes(() =>
        {
            Serilog.Log.Information("CI pipeline completed successfully");
        });

    // Publishing targets for executables (AOT, single-file, etc.)
    // Note: Packaging and publishing for NuGet packages are in Build.Packaging.cs and Build.Publishing.cs

    /// <summary>
    /// Publish platform-specific executable for the specified runtime identifier (RID)
    /// Requires: --rid parameter (e.g., linux-x64, win-x64, osx-arm64)
    /// Output: artifacts/executables/{rid}/
    /// </summary>
    Target PublishExecutable => _ => _
        .Description("Publish single-file executable for a specific platform (requires --rid parameter)")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(Rid))
            {
                throw new Exception("RID parameter is required. Use --rid <rid> (e.g., --rid linux-x64)");
            }

            var ridOutputDir = ExecutablesDir / Rid;
            ridOutputDir.CreateOrCleanDirectory();

            var publishSettings = new DotNetPublishSettings()
                .SetProject(MorphirProject)
                .SetConfiguration(Configuration)
                .SetRuntime(Rid)
                .SetSelfContained(true)
                .SetProperty("PublishSingleFile", "true")
                .SetProperty("PublishTrimmed", "true")
                .SetProperty("PublishAot", "true")
                .SetProperty("Version", Version.ToString())
                .SetOutput(ridOutputDir);

            Serilog.Log.Information($"Publishing single-file executable for {Rid}...");
            DotNetPublish(publishSettings);

            var exeName = GetExecutableName(Rid, isAot: true);
            var exePath = ridOutputDir / exeName;

            if (File.Exists(exePath))
            {
                var size = GetFileSize(exePath);
                Serilog.Log.Information($"✓ Created: {exePath}");
                Serilog.Log.Information($"  Size: {size}");
            }
            else
            {
                throw new Exception($"✗ Error: Executable not found at {exePath}");
            }
        });

    /// <summary>
    /// Publish single-file executable without AOT (managed .NET runtime) with trimming
    /// Requires: --rid parameter (e.g., linux-x64, win-x64, osx-arm64)
    /// Output: artifacts/single-file/{rid}/
    /// Note: Generates Wolverine code before publishing
    /// </summary>
    Target PublishSingleFile => _ => _
        .DependsOn(GenerateWolverineCode)
        .Description("Publish single-file executable without AOT (managed .NET runtime) with trimming (requires --rid parameter)")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(Rid))
            {
                throw new Exception("RID parameter is required. Use --rid <rid> (e.g., --rid linux-x64)");
            }

            var ridOutputDir = SingleFileDir / Rid;
            ridOutputDir.CreateOrCleanDirectory();

            var publishSettings = new DotNetPublishSettings()
                .SetProject(MorphirProject)
                .SetConfiguration(Configuration)
                .SetRuntime(Rid)
                .SetSelfContained(true)
                .SetProperty("PublishSingleFile", "true")
                .SetProperty("PublishTrimmed", "true")
                .SetProperty("TrimMode", "partial")
                .SetProperty("TreatWarningsAsErrors", "false") // Temporarily disable to allow IL2026 warning for ConfigureWolverineCodeGeneration
                .SetProperty("Version", Version.ToString())
                .SetOutput(ridOutputDir);

            Serilog.Log.Information($"Publishing single-file executable (managed, trimmed) for {Rid}...");
            DotNetPublish(publishSettings);

            var exeName = GetExecutableName(Rid, isAot: false);
            var exePath = ridOutputDir / exeName;

            if (File.Exists(exePath))
            {
                var size = GetFileSize(exePath);
                Serilog.Log.Information($"✓ Created: {exePath}");
                Serilog.Log.Information($"  Size: {size}");
            }
            else
            {
                throw new Exception($"✗ Error: Executable not found at {exePath}");
            }
        });

    /// <summary>
    /// Publish single-file executable without AOT and without trimming
    /// Requires: --rid parameter (e.g., linux-x64, win-x64, osx-arm64)
    /// Output: artifacts/single-file-untrimmed/{rid}/
    /// Note: Generates Wolverine code before publishing
    /// </summary>
    Target PublishSingleFileUntrimmed => _ => _
        .DependsOn(GenerateWolverineCode)
        .Description("Publish single-file executable without AOT and without trimming (requires --rid parameter)")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(Rid))
            {
                throw new Exception("RID parameter is required. Use --rid <rid> (e.g., --rid linux-x64)");
            }

            var ridOutputDir = SingleFileUntrimmedDir / Rid;
            ridOutputDir.CreateOrCleanDirectory();

            var publishSettings = new DotNetPublishSettings()
                .SetProject(MorphirProject)
                .SetConfiguration(Configuration)
                .SetRuntime(Rid)
                .SetSelfContained(true)
                .SetProperty("PublishSingleFile", "true")
                .SetProperty("PublishTrimmed", "false")
                .SetProperty("Version", Version.ToString())
                .SetOutput(ridOutputDir);

            Serilog.Log.Information($"Publishing single-file executable (managed, untrimmed) for {Rid}...");
            DotNetPublish(publishSettings);

            var exeName = GetExecutableName(Rid, isAot: false);
            var exePath = ridOutputDir / exeName;

            if (File.Exists(exePath))
            {
                var size = GetFileSize(exePath);
                Serilog.Log.Information($"✓ Created: {exePath}");
                Serilog.Log.Information($"  Size: {size}");
            }
            else
            {
                throw new Exception($"✗ Error: Executable not found at {exePath}");
            }
        });

    // Helper methods

    int RunCommand(string command, params string[] args)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null) Serilog.Log.Information(e.Data);
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null) Serilog.Log.Error(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return process.ExitCode;
    }

    string GetExecutableName(string rid, bool isAot = false)
    {
        var baseName = isAot ? "Morphir" : "morphir";
        if (rid.StartsWith("win-"))
        {
            return $"{baseName}.exe";
        }
        return baseName;
    }

    string GetFileSize(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var bytes = fileInfo.Length;

        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    string GetCurrentRid()
    {
        var os = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64" :
                              RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-x64" : "linux-x64",
            Architecture.Arm64 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-arm64" :
                                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-arm64" : "linux-arm64",
            _ => throw new NotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
        };
        return os;
    }
}
