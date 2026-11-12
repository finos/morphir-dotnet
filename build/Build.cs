using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Morphir.Build.Helpers;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Utilities.Collections;
using SemVersion;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Morphir .NET build orchestration using Fallout (https://fallout.build/).
/// Migrated from Nuke, which replaced the justfile and C# scripts.
/// </summary>
partial class Build : FalloutBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://fallout.build/resharper
    ///   - JetBrains Rider            https://fallout.build/rider
    ///   - Microsoft VisualStudio     https://fallout.build/visualstudio
    ///   - Microsoft VSCode           https://fallout.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Release'")]
    readonly string Configuration = "Release";

    [Parameter("Output directory for packages")]
    readonly AbsolutePath OutputDir = RootDirectory / "artifacts" / "packages";

    [Parameter("Output directory for executables")]
    readonly AbsolutePath ExecutablesDir = RootDirectory / "artifacts" / "executables";

    [Parameter("Output directory for single-file executables")]
    readonly AbsolutePath SingleFileDir = RootDirectory / "artifacts" / "single-file";

    [Parameter("Output directory for untrimmed single-file executables")]
    readonly AbsolutePath SingleFileUntrimmedDir = RootDirectory / "artifacts" / "single-file-untrimmed";

    [Parameter("Output directory for self-contained extension process folders and archives")]
    readonly AbsolutePath ExtensionProcessesDir = RootDirectory / "artifacts" / "extension-processes";

    [Parameter("Output directory for tool DLLs")]
    readonly AbsolutePath ToolDllDir = RootDirectory / "artifacts" / "tool-dll";

    [Parameter("Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)")]
    readonly string? Rid;

    [Parameter("NuGet source URL")]
    readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("NuGet API key for publishing")]
    readonly string? ApiKey;

    [Parameter("Local NuGet feed directory")]
    readonly AbsolutePath LocalSource = RootDirectory / "artifacts" / "local-feed";

    [Parameter("Install tool globally (true) or locally (false)")]
    readonly bool Global = false;

    [Parameter("Executable type for E2E tests: aot, trimmed, untrimmed, tool or all (those four); native-aot, self-contained, framework-dependent or dotnet-tool (a packaged release profile); or offerings (all four packaged)")]
    readonly string ExecutableType = "all";

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ScriptsDirectory => RootDirectory / "scripts";
    AbsolutePath SolutionFile => RootDirectory / "Morphir.slnx";
    AbsolutePath ChangelogFile => RootDirectory / "CHANGELOG.md";

    /// <summary>
    /// Gets the release version: the latest released version in CHANGELOG.md.
    /// Ionide.KeepAChangelog.Tasks stamps it on every src project, so build and pack targets
    /// pass no Version; an explicit Version would win over the changelog (see src/Directory.Build.props).
    /// </summary>
    SemanticVersion Version => new FileInfo(ChangelogFile).GetVersionFromChangelog();

    /// <summary>
    /// Gets the release notes for the latest release from CHANGELOG.md
    /// </summary>
    string ReleaseNotes => new FileInfo(ChangelogFile).GetReleaseNotes();

    // Project paths
    AbsolutePath MorphirCoreProject => SourceDirectory / "Morphir.Core" / "Morphir.Core.csproj";
    AbsolutePath MorphirToolingProject => SourceDirectory / "Morphir.Tooling" / "Morphir.Tooling.csproj";
    AbsolutePath MorphirProject => SourceDirectory / "Morphir" / "Morphir.csproj";
    AbsolutePath MorphirToolProject => SourceDirectory / "Morphir.Tool" / "Morphir.Tool.csproj";
    /// <summary>The F# frontend MEP guest, packed as the dotnet tool morphir-fsharp-frontend.</summary>
    AbsolutePath FSharpFrontendExtensionProject => SourceDirectory / "Morphir.Frontend.FSharp.Extension" / "Morphir.Frontend.FSharp.Extension.fsproj";
    /// <summary>The F# backend MEP guest, packed as the dotnet tool morphir-fsharp-backend.</summary>
    AbsolutePath FSharpBackendExtensionProject => SourceDirectory / "Morphir.CodeGeneration.FSharp.Extension" / "Morphir.CodeGeneration.FSharp.Extension.fsproj";
    AbsolutePath MorphirSDKProject => SourceDirectory / "Morphir.SDK" / "Morphir.SDK.fsproj";
    AbsolutePath MorphirSDKFSharpProject => SourceDirectory / "Morphir.SDK.FSharp" / "Morphir.SDK.FSharp.fsproj";
    AbsolutePath MorphirSDKCSharpProject => SourceDirectory / "Morphir.SDK.CSharp" / "Morphir.SDK.CSharp.csproj";
    AbsolutePath MorphirModelsProject => SourceDirectory / "Morphir.Models" / "Morphir.Models.fsproj";
    AbsolutePath MorphirCodeGenerationCSharpProject => SourceDirectory / "Morphir.CodeGeneration.CSharp" / "Morphir.CodeGeneration.CSharp.fsproj";

    /// <summary>
    /// Library packages in publish order: each package follows the Morphir packages it depends on,
    /// so every dependency of a published package is itself published.
    /// HasLibrary is false for a dependency-only package with no assemblies of its own.
    /// </summary>
    (string PackageId, AbsolutePath Project, bool HasLibrary)[] LibraryPackages => new[]
    {
        ("Morphir.Core", MorphirCoreProject, true),
        // No dependency: the attributes that F# source uses for the Morphir F# frontend.
        ("Morphir.FSharp.Annotations", SourceDirectory / "Morphir.FSharp.Annotations" / "Morphir.FSharp.Annotations.csproj", true),
        ("Morphir.Configuration", SourceDirectory / "Morphir.Configuration" / "Morphir.Configuration.fsproj", true),
        ("Morphir.Configuration.IO", SourceDirectory / "Morphir.Configuration.IO" / "Morphir.Configuration.IO.csproj", true),
        ("Morphir.Models", MorphirModelsProject, true),
        ("Morphir.Compilation", SourceDirectory / "Morphir.Compilation" / "Morphir.Compilation.fsproj", true),
        ("Morphir.IR.Pipeline", SourceDirectory / "Morphir.IR.Pipeline" / "Morphir.IR.Pipeline.fsproj", true),
        ("Morphir.IR.Pipeline.Plugins", SourceDirectory / "Morphir.IR.Pipeline.Plugins" / "Morphir.IR.Pipeline.Plugins.fsproj", true),
        ("Morphir.Ion", SourceDirectory / "Morphir.Ion" / "Morphir.Ion.fsproj", true),
        ("Morphir.CodeGeneration.FSharp", SourceDirectory / "Morphir.CodeGeneration.FSharp" / "Morphir.CodeGeneration.FSharp.fsproj", true),
        // Myriad plugin packages have no lib/; PackLibs checks their layout with ValidateMyriadPluginPackage.
        ("Morphir.Myriad", SourceDirectory / "Morphir.Myriad" / "Morphir.Myriad.fsproj", false),
        ("Morphir.Myriad.SystemTextJson.Annotations", SourceDirectory / "Morphir.Myriad.SystemTextJson.Annotations" / "Morphir.Myriad.SystemTextJson.Annotations.csproj", true),
        ("Morphir.Myriad.SystemTextJson", SourceDirectory / "Morphir.Myriad.SystemTextJson" / "Morphir.Myriad.SystemTextJson.fsproj", false),
        ("Morphir.Mep", SourceDirectory / "Morphir.Mep" / "Morphir.Mep.fsproj", true),
        ("Morphir.Compilation.Mep", SourceDirectory / "Morphir.Compilation.Mep" / "Morphir.Compilation.Mep.fsproj", true),
        ("Morphir.CodeGeneration.CSharp", MorphirCodeGenerationCSharpProject, true),
        ("Morphir.Extensions", SourceDirectory / "Morphir.Extensions" / "Morphir.Extensions.fsproj", true),
        ("Morphir.Analyzers", SourceDirectory / "Morphir.Analyzers" / "Morphir.Analyzers.fsproj", true),
        // The WASM extension runtime (Wasmtime and the embedded Extism kernel) and the compatibility kit runner;
        // Morphir.Tooling depends on both, so both are published before it.
        ("Morphir.Extensions.Wasm", SourceDirectory / "Morphir.Extensions.Wasm" / "Morphir.Extensions.Wasm.csproj", true),
        ("Morphir.Conformance", SourceDirectory / "Morphir.Conformance" / "Morphir.Conformance.fsproj", true),
        ("Morphir.Tooling", MorphirToolingProject, true),
        ("Morphir", MorphirProject, true),
        ("Morphir.SDK.FSharp", MorphirSDKFSharpProject, true),
        ("Morphir.SDK", MorphirSDKProject, false),
        ("Morphir.SDK.CSharp", MorphirSDKCSharpProject, true),
    };

    /// <summary>The Myriad plugin packages in LibraryPackages, with the file name of their MSBuild task assembly.</summary>
    (string PackageId, string TaskAssembly)[] MyriadPluginPackages => new[]
    {
        ("Morphir.Myriad", "Morphir.Myriad.Build.dll"),
        ("Morphir.Myriad.SystemTextJson", "Morphir.Myriad.SystemTextJson.Build.dll"),
    };

    AbsolutePath MorphirCoreTestsProject => TestsDirectory / "Morphir.Core.Tests" / "Morphir.Core.Tests.csproj";
    AbsolutePath MorphirToolingTestsProject => TestsDirectory / "Morphir.Tooling.Tests" / "Morphir.Tooling.Tests.csproj";
    AbsolutePath MorphirModelsTestsProject => TestsDirectory / "Morphir.Models.Tests" / "Morphir.Models.Tests.fsproj";
    AbsolutePath MorphirIRPipelineTestsProject => TestsDirectory / "Morphir.IR.Pipeline.Tests" / "Morphir.IR.Pipeline.Tests.fsproj";
    AbsolutePath MorphirE2ETestsProject => TestsDirectory / "Morphir.E2E.Tests" / "Morphir.E2E.Tests.csproj";
    AbsolutePath MorphirBuildTestsProject => TestsDirectory / "Morphir.Build.Tests" / "Morphir.Build.Tests.csproj";

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
                .SetProjectFile(SolutionFile)
                .AddProcessAdditionalArguments("-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false"));
        });

    /// <summary>
    /// Compile all projects in the solution
    /// Note: VBCSCompiler processes are cleaned up on Windows before build to prevent file locking issues.
    /// Builds use one MSBuild node with shared compilation disabled.
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
            // Bound build resources for concurrent delivery work.
            // Pass no Version: every src project takes it from CHANGELOG.md, and a Version
            // here would win over the changelog and also rebuild the running build project.
            DotNetBuild(s => s
                .SetProjectFile(SolutionFile)
                .SetConfiguration(Configuration)
                .SetNoRestore(true)
                .AddProcessAdditionalArguments("-m:1", "-p:UseSharedCompilation=false", "-nodeReuse:false"));
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

    // Publishing targets for executables. Their publish settings come from the release profile model in
    // build/Helpers/ReleaseProfiles.cs. The named release profiles are in Build.ReleaseProfiles.cs; these three
    // targets keep their names and output folders for the workflows and scripts/run-e2e-tests.cs that call them.
    // Note: Packaging and publishing for NuGet packages are in Build.Packaging.cs and Build.Publishing.cs

    /// <summary>
    /// Publish the Native AOT executable (the native-aot profile's settings) for the specified runtime identifier (RID)
    /// Requires: --rid parameter (e.g., linux-x64, win-x64, osx-arm64)
    /// Output: artifacts/executables/{rid}/ (used by the E2E executable type "aot")
    /// </summary>
    Target PublishExecutable => _ => _
        .Description("Publish the Native AOT executable for a specific platform (requires --rid parameter)")
        .Executes(() => PublishLegacyLayout(Morphir.Build.Helpers.Deployment.NativeAot.Project,
            TargetSettings.PublishExecutable, ExecutablesDir, "Native AOT"));

    /// <summary>
    /// Publish the legacy release asset: a single-file executable without AOT (managed .NET runtime) with trimming.
    /// Releases ship this as morphir-{rid}[.exe] and morphir-{rid}-v{version}.tar.gz (LegacyAsset.TrimmedSingleFile).
    /// Requires: --rid parameter (e.g., linux-x64, win-x64, osx-arm64)
    /// Output: artifacts/single-file/{rid}/
    /// Note: Foundatio.Mediator source generators run automatically during build
    /// </summary>
    Target PublishSingleFile => _ => _
        .DependsOn(Compile)
        .Description("Publish the legacy single-file executable (managed, trimmed) that releases ship (requires --rid parameter)")
        .Executes(() => PublishLegacyLayout(Morphir.Build.Helpers.LegacyAsset.TrimmedSingleFile.Project,
            TargetSettings.PublishSingleFile, SingleFileDir, "managed, trimmed"));

    /// <summary>
    /// Publish single-file executable without AOT and without trimming: the manual AOT workflow's baseline, never released
    /// Requires: --rid parameter (e.g., linux-x64, win-x64, osx-arm64)
    /// Output: artifacts/single-file-untrimmed/{rid}/
    /// Note: Foundatio.Mediator source generators run automatically during build
    /// </summary>
    Target PublishSingleFileUntrimmed => _ => _
        .DependsOn(Compile)
        .Description("Publish single-file executable without AOT and without trimming (requires --rid parameter)")
        .Executes(() => PublishLegacyLayout(Morphir.Build.Helpers.LegacyAsset.UntrimmedSingleFile.Project,
            TargetSettings.PublishSingleFileUntrimmed, SingleFileUntrimmedDir, "managed, untrimmed"));

    /// <summary>
    /// Publishes <paramref name="project"/> for <see cref="Rid"/> into <c>{outputRoot}/{rid}</c> with the given MSBuild
    /// properties and the release version, then checks that the <c>morphir</c> executable is there. The publish is
    /// RID-specific, so it writes its NuGet lock files under obj/publish-lock/ (see TargetSettings.RidSpecificPublishProperty).
    /// </summary>
    void PublishLegacyLayout(string project, ImmutableSortedDictionary<string, string> properties, AbsolutePath outputRoot, string kind)
    {
        if (string.IsNullOrEmpty(Rid))
        {
            throw new Exception("RID parameter is required. Use --rid <rid> (e.g., --rid linux-x64)");
        }

        var ridOutputDir = outputRoot / Rid;
        ridOutputDir.CreateOrCleanDirectory();

        var publishSettings = ApplyBuildProperties(new DotNetPublishSettings()
                .SetProject(RootDirectory / project)
                .SetConfiguration(Configuration)
                .SetRuntime(Rid), TargetSettings.ForRidSpecificPublish(properties))
            .SetProperty("Version", Version.ToString())
            .SetOutput(ridOutputDir);

        Serilog.Log.Information($"Publishing executable ({kind}) for {Rid}...");
        DotNetPublish(publishSettings);

        var exePath = ridOutputDir / (Rid.StartsWith("win-", StringComparison.Ordinal) ? "morphir.exe" : "morphir");
        if (!File.Exists(exePath))
        {
            throw new Exception($"✗ Error: Executable not found at {exePath}");
        }

        Serilog.Log.Information($"✓ Created: {exePath}");
        Serilog.Log.Information($"  Size: {GetFileSize(exePath)}");
    }

    /// <summary>
    /// Applies profile properties to a publish. <c>SelfContained</c> goes through the publish option, as the
    /// command-line <c>--self-contained</c> does; every other property is an MSBuild property.
    /// </summary>
    static DotNetPublishSettings ApplyBuildProperties(DotNetPublishSettings settings, IReadOnlyDictionary<string, string> properties)
    {
        foreach (var (key, value) in properties)
        {
            settings = key == "SelfContained"
                ? settings.SetSelfContained(bool.Parse(value))
                : settings.SetProperty(key, value);
        }
        return settings;
    }

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
