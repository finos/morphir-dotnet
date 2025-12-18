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

    [Parameter("Version string for packages and executables")]
    readonly string Version;

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

    // Project paths
    AbsolutePath MorphirCoreProject => SourceDirectory / "Morphir.Core" / "Morphir.Core.csproj";
    AbsolutePath MorphirToolingProject => SourceDirectory / "Morphir.Tooling" / "Morphir.Tooling.csproj";
    AbsolutePath MorphirProject => SourceDirectory / "Morphir" / "Morphir.csproj";
    AbsolutePath MorphirToolProject => SourceDirectory / "Morphir.Tool" / "Morphir.Tool.csproj";
    AbsolutePath MorphirCoreTestsProject => TestsDirectory / "Morphir.Core.Tests" / "Morphir.Core.Tests.csproj";
    AbsolutePath MorphirToolingTestsProject => TestsDirectory / "Morphir.Tooling.Tests" / "Morphir.Tooling.Tests.csproj";
    AbsolutePath MorphirE2ETestsProject => TestsDirectory / "Morphir.E2E.Tests" / "Morphir.E2E.Tests.csproj";

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

    Target Restore => _ => _
        .Executes(() =>
        {
            // Restore using .slnx solution file
            DotNetRestore(s => s
                .SetProjectFile(SolutionFile));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            // Build using .slnx solution file
            DotNetBuild(s => s
                .SetProjectFile(SolutionFile)
                .SetConfiguration(Configuration)
                .SetNoRestore(true));
        });

    Target Format => _ => _
        .Description("Format code (applies formatting changes)")
        .Executes(() =>
        {
            DotNet($"format");
        });

    Target Lint => _ => _
        .Description("Run linting/formatting checks (verifies without making changes)")
        .Executes(() =>
        {
            DotNet($"format --verify-no-changes");
        });

    Target Check => _ => _
        .DependsOn(Lint)
        .Description("Check task that runs lint");

    Target Precommit => _ => _
        .DependsOn(Lint)
        .Description("Pre-commit hook task (runs lint)");

    Target CI => _ => _
        .DependsOn(Restore, Compile, Test, Check)
        .Description("Full CI pipeline: restore, build, test, and check")
        .Executes(() =>
        {
            Serilog.Log.Information("CI pipeline completed successfully");
        });

    // Publishing targets for executables (AOT, single-file, etc.)
    // Note: Packaging and publishing for NuGet packages are in Build.Packaging.cs and Build.Publishing.cs

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
                .SetOutput(ridOutputDir);

            if (!string.IsNullOrEmpty(Version))
            {
                publishSettings = publishSettings.SetProperty("Version", Version);
            }

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

    Target PublishSingleFile => _ => _
        .DependsOn(Compile)
        .Description("Publish single-file executable without AOT (managed .NET runtime) with trimming (requires --rid parameter)")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(Rid))
            {
                throw new Exception("RID parameter is required. Use --rid <rid> (e.g., --rid linux-x64)");
            }

            // Generate Wolverine code before publishing
            Serilog.Log.Information("Generating Wolverine code...");
            DotNet($"run --project {MorphirProject} --configuration {Configuration} --no-build -- codegen write");

            var generatedDir = SourceDirectory / "Morphir.Tooling" / "Internal" / "Generated";
            if (Directory.Exists(generatedDir))
            {
                var fileCount = Directory.GetFiles(generatedDir, "*.cs", SearchOption.AllDirectories).Length;
                Serilog.Log.Information($"✓ Found generated code directory with {fileCount} files");
            }
            else
            {
                Serilog.Log.Warning("⚠ Warning: Generated code directory not found");
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
                .SetOutput(ridOutputDir);

            if (!string.IsNullOrEmpty(Version))
            {
                publishSettings = publishSettings.SetProperty("Version", Version);
            }

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

    Target PublishSingleFileUntrimmed => _ => _
        .DependsOn(Compile)
        .Description("Publish single-file executable without AOT and without trimming (requires --rid parameter)")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(Rid))
            {
                throw new Exception("RID parameter is required. Use --rid <rid> (e.g., --rid linux-x64)");
            }

            // Generate Wolverine code before publishing
            Serilog.Log.Information("Generating Wolverine code...");
            DotNet($"run --project {MorphirProject} --configuration {Configuration} --no-build -- codegen write");

            var ridOutputDir = SingleFileUntrimmedDir / Rid;
            ridOutputDir.CreateOrCleanDirectory();

            var publishSettings = new DotNetPublishSettings()
                .SetProject(MorphirProject)
                .SetConfiguration(Configuration)
                .SetRuntime(Rid)
                .SetSelfContained(true)
                .SetProperty("PublishSingleFile", "true")
                .SetProperty("PublishTrimmed", "false")
                .SetOutput(ridOutputDir);

            if (!string.IsNullOrEmpty(Version))
            {
                publishSettings = publishSettings.SetProperty("Version", Version);
            }

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
