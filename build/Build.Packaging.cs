using System;
using System.Linq;
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
    /// Pack library projects as NuGet packages (Morphir.Core, Morphir.Tooling, Morphir)
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
                .SetProperty("Version", versionString));
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
}
