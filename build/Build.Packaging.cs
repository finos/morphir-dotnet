using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Morphir.Build.Helpers;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Build targets for packaging projects (libraries and tools)
/// </summary>
partial class Build
{
    /// <summary>
    /// Pack library projects and the Morphir.SDK compatibility package as NuGet packages
    /// Output: artifacts/packages/
    /// Version: the latest released version in CHANGELOG.md
    /// </summary>
    Target PackLibs => _ => _
        .DependsOn(Compile)
        .Description("Pack library projects as NuGet packages")
        .Executes(() =>
        {
            OutputDir.CreateOrCleanDirectory();

            var versionString = Version.ToString();
            Serilog.Log.Information($"Packing with version: {versionString}");

            foreach (var (packageId, project, _) in LibraryPackages)
            {
                Serilog.Log.Information($"Packing {packageId}...");
                DotNetPack(s => s
                    .SetProject(project)
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(OutputDir));
            }

            Serilog.Log.Information("Validating library packages...");
            foreach (var (packageId, _, hasLibrary) in LibraryPackages)
            {
                var packagePath = PathHelper.GetLibraryPackagePath(packageId, versionString, OutputDir);
                if (!File.Exists(packagePath))
                {
                    throw new Exception($"{packageId} package not found: {packagePath}");
                }

                if (!hasLibrary)
                {
                    continue;
                }

                var validation = PackageValidator.ValidateLibraryPackage(packagePath);
                if (!validation.IsValid)
                {
                    Serilog.Log.Error($"{packageId} package validation failed:");
                    foreach (var error in validation.Errors)
                    {
                        Serilog.Log.Error($"  - {error}");
                    }
                    throw new Exception($"{packageId} package validation failed. See errors above.");
                }
                Serilog.Log.Information($"✓ {packageId} package validated successfully");
            }

            foreach (var (packageId, taskAssembly) in MyriadPluginPackages)
            {
                var packagePath = PathHelper.GetLibraryPackagePath(packageId, versionString, OutputDir);
                var validation = PackageValidator.ValidateMyriadPluginPackage(packagePath, packageId, taskAssembly);
                if (!validation.IsValid)
                {
                    foreach (var error in validation.Errors)
                    {
                        Serilog.Log.Error($"  - {error}");
                    }
                    throw new Exception($"{packageId} Myriad plugin package validation failed. See errors above.");
                }
                Serilog.Log.Information($"✓ {packageId} Myriad plugin package validated successfully");
            }
        });

    /// <summary>
    /// Pack the Morphir CLI as a dotnet tool (standard managed tool)
    /// Output: artifacts/packages/Morphir.Tool.{version}.nupkg
    /// Tool command name: dotnet-morphir
    /// Version: the latest released version in CHANGELOG.md
    /// </summary>
    Target PackTool => _ => _
        .DependsOn(Compile)
        .After(PackLibs)  // Run after PackLibs to avoid directory cleaning conflicts
        .Description("Pack the Morphir CLI as a dotnet tool (standard managed tool)")
        .Executes(() =>
        {
            // Don't clean directory if PackLibs already ran - just ensure it exists
            if (!OutputDir.DirectoryExists())
            {
                OutputDir.CreateDirectory();
            }

            var versionString = Version.ToString();
            Serilog.Log.Information($"Packing tool with version: {versionString}");

            Serilog.Log.Information("Packing Morphir.Tool CLI as dotnet tool...");
            DotNetPack(s => s
                .SetProject(MorphirToolProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDir)
                .SetProperty("PackAsTool", "true")
                .SetProperty("ToolCommandName", "dotnet-morphir")
                .SetProperty("IsPackable", "true")
                .SetProperty("DebugType", "none")  // Don't include PDB files in tool package
                .SetProperty("IncludeSymbols", "false"));

            // The F# frontend extension: the MEP guest that the native morphir executable runs for F#.
            Serilog.Log.Information("Packing Morphir.Frontend.FSharp.Extension as dotnet tool...");
            DotNetPack(s => s
                .SetProject(FSharpFrontendExtensionProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDir)
                .SetProperty("DebugType", "none")
                .SetProperty("IncludeSymbols", "false"));

            var extensionPackagePath = OutputDir / $"Morphir.Frontend.FSharp.Extension.{versionString}.nupkg";
            if (!File.Exists(extensionPackagePath))
            {
                throw new Exception($"Morphir.Frontend.FSharp.Extension package not found: {extensionPackagePath}");
            }

            // Validate the tool package structure
            var toolPackagePath = PathHelper.GetToolPackagePath(versionString, OutputDir);
            if (File.Exists(toolPackagePath))
            {
                Serilog.Log.Information("Validating tool package structure...");
                var validation = PackageValidator.ValidateToolPackage(toolPackagePath);

                if (!validation.IsValid)
                {
                    Serilog.Log.Error("Tool package validation failed:");
                    foreach (var error in validation.Errors)
                    {
                        Serilog.Log.Error($"  - {error}");
                    }
                    throw new Exception("Tool package validation failed. See errors above.");
                }

                Serilog.Log.Information("✓ Tool package structure validated successfully");
            }
        });

    /// <summary>
    /// Publish the F# frontend extension as a self-contained process for one runtime identifier: a folder with the
    /// guest, the F# compiler service and its own .NET runtime, and a deterministic archive of that folder. The
    /// framework-dependent dotnet tool from PackTool is unchanged.
    /// Requires: --rid (e.g., win-arm64, linux-x64, osx-arm64)
    /// Output: artifacts/extension-processes/morphir-fsharp-frontend/{rid}/ and
    ///         artifacts/extension-processes/morphir-fsharp-frontend-{version}-{rid}.zip (win-*) or .tar.gz
    /// </summary>
    Target PublishFSharpFrontendProcess => _ => _
        .Description("Publish the F# frontend extension as a self-contained process folder and archive (requires --rid)")
        .Requires(() => Rid)
        .Executes(() =>
        {
            PublishExtensionProcess(FSharpFrontendExtensionProject, "morphir-fsharp-frontend", Rid!);
        });

    /// <summary>
    /// Publishes an extension project with a publish profile (by default SelfContainedProcess, which the project
    /// defines) for one runtime identifier, then archives the folder. The archive has one root directory, the
    /// runtime identifier. Returns the folder and the archive; logs the file count and sizes of both.
    /// </summary>
    (AbsolutePath Folder, AbsolutePath Archive) PublishExtensionProcess(
        AbsolutePath project, string name, string rid, string profile = "SelfContainedProcess")
    {
        var versionString = Version.ToString();
        var folder = ExtensionProcessesDir / name / rid;
        folder.CreateOrCleanDirectory();

        Serilog.Log.Information($"Publishing {name} {versionString} for {rid} with the {profile} profile...");
        DotNetPublish(s => s
            .SetProject(project)
            .SetConfiguration(Configuration)
            .SetRuntime(rid)
            .SetProperty("PublishProfile", profile)
            .SetOutput(folder));

        var executable = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? $"{name}.exe" : name;
        if (!File.Exists(folder / executable))
        {
            throw new Exception($"{name}: the executable {executable} is not in {folder}");
        }

        var archive = ExtensionProcessesDir / $"{name}-{versionString}-{rid}{ProcessArchive.ExtensionFor(rid)}";
        ProcessArchive.Create(folder, archive, rid, rid, executable, "createdump");

        var (files, bytes) = ProcessArchive.Measure(folder);
        Serilog.Log.Information($"✓ {folder}: {files} files, {bytes} bytes");
        Serilog.Log.Information($"✓ {archive}: {new FileInfo(archive).Length} bytes");
        return (folder, archive);
    }

    /// <summary>
    /// Pack all projects (libraries and tool)
    /// Equivalent to running PackLibs and PackTool
    /// Output: artifacts/packages/
    /// </summary>
    Target PackAll => _ => _
        .DependsOn(PackLibs, PackTool)
        .Description("Pack all projects (libraries and tool)")
        .Executes(() =>
        {
            Serilog.Log.Information("All packages created successfully");
        });

    /// <summary>
    /// Create platform-specific tarballs from single-file executables
    /// Input: artifacts/single-file/{rid}/morphir[.exe]
    /// Output: artifacts/morphir-{rid}-v{version}.tar.gz containing {rid}/morphir[.exe]
    /// Matches the GitHub release assets the deployment workflow creates for the proto plugin
    /// </summary>
    Target PackPlatformTarballs => _ => _
        .Description("Create platform-specific tarballs for GitHub releases")
        .Executes(() =>
        {
            var singleFileDir = RootDirectory / "artifacts" / "single-file";
            var artifactsDir = RootDirectory / "artifacts";

            if (!Directory.Exists(singleFileDir))
            {
                throw new Exception($"Single-file directory not found: {singleFileDir}. Run build-executables job first.");
            }

            var versionString = Version.ToString();
            Serilog.Log.Information($"Creating platform tarballs for version {versionString}...");

            // Find all RID directories
            var ridDirs = Directory.GetDirectories(singleFileDir)
                .Select(d => new DirectoryInfo(d).Name)
                .Where(name => !name.Equals("staging", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!ridDirs.Any())
            {
                throw new Exception($"No RID directories found in {singleFileDir}");
            }

            foreach (var rid in ridDirs)
            {
                var ridDir = singleFileDir / rid;
                var tarballName = $"morphir-{rid}-v{versionString}.tar.gz";
                var tarballPath = artifactsDir / tarballName;

                Serilog.Log.Information($"Creating tarball for {rid}...");

                // Archive the RID directory itself: the proto plugin strips a {rid}/ prefix
                // (archive_prefix) and expects the executable directly beneath it.
                var tarExitCode = RunCommand(
                    "tar",
                    "-czf",
                    tarballPath,
                    "-C", singleFileDir,
                    rid
                );

                if (tarExitCode != 0)
                {
                    throw new Exception($"Failed to create tarball for {rid} (exit code: {tarExitCode})");
                }

                var size = GetFileSize(tarballPath);
                Serilog.Log.Information($"✓ Created {tarballName} ({size})");
            }

            Serilog.Log.Information($"✓ All platform tarballs created successfully");
        });
}
