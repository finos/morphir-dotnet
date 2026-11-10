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
                var tarballName = LegacyAsset.TrimmedSingleFile.TarballName(rid, versionString);
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
