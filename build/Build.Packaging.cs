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
    /// Pack library projects as NuGet packages (Morphir.Core, Morphir.Tooling, Morphir, Morphir.SDK)
    /// Output: artifacts/packages/
    /// Parameters: --version-override (optional, overrides CHANGELOG.md version)
    /// </summary>
    Target PackLibs => _ => _
        .DependsOn(Compile)
        .Description("Pack library projects as NuGet packages")
        .Executes(() =>
        {
            OutputDir.CreateOrCleanDirectory();

            var versionString = Version.ToString();
            Serilog.Log.Information($"Packing with version: {versionString}");

            Serilog.Log.Information("Packing Morphir.Core...");
            DotNetPack(s => s
                .SetProject(MorphirCoreProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDir)
                .SetProperty("Version", versionString));

            Serilog.Log.Information("Packing Morphir.Tooling...");
            DotNetPack(s => s
                .SetProject(MorphirToolingProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDir)
                .SetProperty("Version", versionString));

            Serilog.Log.Information("Packing Morphir...");
            DotNetPack(s => s
                .SetProject(MorphirProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDir)
                .SetProperty("Version", versionString));

            Serilog.Log.Information("Packing Morphir.SDK...");
            DotNetPack(s => s
                .SetProject(MorphirSDKProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDir)
                .SetProperty("Version", versionString));

            // Validate library packages
            Serilog.Log.Information("Validating library packages...");
            var libraryProjects = new[] { "Morphir.Core", "Morphir.Tooling", "Morphir", "Morphir.SDK" };
            foreach (var project in libraryProjects)
            {
                var packagePath = PathHelper.GetLibraryPackagePath(project, versionString, OutputDir);
                if (File.Exists(packagePath))
                {
                    var validation = PackageValidator.ValidateLibraryPackage(packagePath);
                    if (!validation.IsValid)
                    {
                        Serilog.Log.Error($"{project} package validation failed:");
                        foreach (var error in validation.Errors)
                        {
                            Serilog.Log.Error($"  - {error}");
                        }
                        throw new Exception($"{project} package validation failed. See errors above.");
                    }
                    Serilog.Log.Information($"✓ {project} package validated successfully");
                }
            }
        });

    /// <summary>
    /// Pack the Morphir CLI as a dotnet tool (standard managed tool)
    /// Output: artifacts/packages/Morphir.Tool.{version}.nupkg
    /// Tool command name: dotnet-morphir
    /// Parameters: --version-override (optional, overrides CHANGELOG.md version)
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
                .SetProperty("Version", versionString)
                .SetProperty("DebugType", "none")  // Don't include PDB files in tool package
                .SetProperty("IncludeSymbols", "false"));

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
    /// Output: artifacts/morphir-{rid}-v{version}.tar.gz
    /// Used by deployment workflow to create GitHub release assets
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

                // Create tarball using cross-platform tar
                var tarExitCode = RunCommand(
                    "tar",
                    "-czf",
                    tarballPath,
                    "-C", ridDir,
                    "."
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
