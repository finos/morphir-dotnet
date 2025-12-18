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
    Target PackLibs => _ => _
        .DependsOn(Compile)
        .Description("Pack library projects as NuGet packages")
        .Executes(() =>
        {
            OutputDir.CreateOrCleanDirectory();

            var packSettings = new DotNetPackSettings()
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDir);

            if (!string.IsNullOrEmpty(Version))
            {
                packSettings = packSettings.SetProperty("Version", Version);
            }

            Serilog.Log.Information("Packing Morphir.Core...");
            DotNetPack(s => packSettings
                .SetProject(MorphirCoreProject));

            Serilog.Log.Information("Packing Morphir.Tooling...");
            DotNetPack(s => packSettings
                .SetProject(MorphirToolingProject));

            Serilog.Log.Information("Packing Morphir...");
            DotNetPack(s => packSettings
                .SetProject(MorphirProject));
        });

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

            var packSettings = new DotNetPackSettings()
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDir)
                .SetProperty("PackAsTool", "true")
                .SetProperty("ToolCommandName", "morphir")
                .SetProperty("IsPackable", "true");

            if (!string.IsNullOrEmpty(Version))
            {
                packSettings = packSettings.SetProperty("Version", Version);
            }

            Serilog.Log.Information("Packing Morphir.Tool CLI as dotnet tool...");
            DotNetPack(s => packSettings
                .SetProject(MorphirToolProject));
        });

    Target PackAll => _ => _
        .DependsOn(PackLibs, PackTool)
        .Description("Pack all projects (libraries and tool)")
        .Executes(() =>
        {
            Serilog.Log.Information("All packages created successfully");
        });
}
