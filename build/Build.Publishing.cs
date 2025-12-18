using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// Build targets for publishing packages to NuGet.org and local feeds
/// </summary>
partial class Build
{
    // NuGet Publishing targets

    Target PublishLibs => _ => _
        .DependsOn(PackLibs)
        .Description("Publish library NuGet packages to NuGet.org")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                throw new Exception("API_KEY is required for publishing. Use --api-key parameter.");
            }

            var corePackage = OutputDir.GlobFiles("Morphir.Core.*.nupkg").FirstOrDefault();
            if (corePackage != null)
            {
                Serilog.Log.Information($"Publishing Morphir.Core: {corePackage}");
                DotNetNuGetPush(s => s
                    .SetTargetPath(corePackage)
                    .SetSource(NuGetSource)
                    .SetApiKey(ApiKey)
                    .SetSkipDuplicate(true));
            }
            else
            {
                throw new Exception($"Morphir.Core package not found in {OutputDir}");
            }

            var toolingPackage = OutputDir.GlobFiles("Morphir.Tooling.*.nupkg").FirstOrDefault();
            if (toolingPackage != null)
            {
                Serilog.Log.Information($"Publishing Morphir.Tooling: {toolingPackage}");
                DotNetNuGetPush(s => s
                    .SetTargetPath(toolingPackage)
                    .SetSource(NuGetSource)
                    .SetApiKey(ApiKey)
                    .SetSkipDuplicate(true));
            }
            else
            {
                throw new Exception($"Morphir.Tooling package not found in {OutputDir}");
            }

            // Publish Morphir executable package (not the Tool package)
            var morphirPackage = OutputDir.GlobFiles("Morphir.0.*.nupkg")
                .Where(p => !p.ToString().Contains("Morphir.Core") && 
                           !p.ToString().Contains("Morphir.Tooling") && 
                           !p.ToString().Contains("Morphir.Tool"))
                .FirstOrDefault();
            if (morphirPackage != null)
            {
                Serilog.Log.Information($"Publishing Morphir: {morphirPackage}");
                DotNetNuGetPush(s => s
                    .SetTargetPath(morphirPackage)
                    .SetSource(NuGetSource)
                    .SetApiKey(ApiKey)
                    .SetSkipDuplicate(true));
            }
            else
            {
                throw new Exception($"Morphir package not found in {OutputDir}");
            }
        });

    Target PublishTool => _ => _
        .DependsOn(PackTool)
        .Description("Publish the Morphir CLI tool package to NuGet.org")
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                throw new Exception("API_KEY is required for publishing. Use --api-key parameter.");
            }

            var toolPackage = OutputDir.GlobFiles("Morphir.Tool.*.nupkg")
                .FirstOrDefault();

            if (toolPackage != null)
            {
                Serilog.Log.Information($"Publishing Morphir.Tool CLI tool: {toolPackage}");
                DotNetNuGetPush(s => s
                    .SetTargetPath(toolPackage)
                    .SetSource(NuGetSource)
                    .SetApiKey(ApiKey)
                    .SetSkipDuplicate(true));
            }
            else
            {
                throw new Exception($"Morphir.Tool package not found in {OutputDir}");
            }
        });

    Target PublishAll => _ => _
        .DependsOn(PublishLibs, PublishTool)
        .Description("Publish all packages (libraries and tool)")
        .Executes(() =>
        {
            Serilog.Log.Information("All packages published successfully");
        });

    // Local Publishing targets

    Target PublishLocalLibs => _ => _
        .DependsOn(PackLibs)
        .Description("Publish library packages to a local NuGet source")
        .Executes(() =>
        {
            LocalSource.CreateOrCleanDirectory();

            // Add local source if it doesn't exist
            try
            {
                DotNet($"nuget list source | grep {LocalSource}");
            }
            catch
            {
                Serilog.Log.Information($"Adding local NuGet source: {LocalSource}");
                DotNet($"nuget add source {LocalSource} --name local-feed");
            }

            var corePackage = OutputDir.GlobFiles("Morphir.Core.*.nupkg").FirstOrDefault();
            if (corePackage != null)
            {
                Serilog.Log.Information("Publishing Morphir.Core to local feed...");
                DotNetNuGetPush(s => s
                    .SetTargetPath(corePackage)
                    .SetSource(LocalSource)
                    .SetSkipDuplicate(true));
            }

            var toolingPackage = OutputDir.GlobFiles("Morphir.Tooling.*.nupkg").FirstOrDefault();
            if (toolingPackage != null)
            {
                Serilog.Log.Information("Publishing Morphir.Tooling to local feed...");
                DotNetNuGetPush(s => s
                    .SetTargetPath(toolingPackage)
                    .SetSource(LocalSource)
                    .SetSkipDuplicate(true));
            }

            var morphirPackage = OutputDir.GlobFiles("Morphir.0.*.nupkg")
                .Where(p => !p.ToString().Contains("Morphir.Core") && 
                           !p.ToString().Contains("Morphir.Tooling") && 
                           !p.ToString().Contains("Morphir.Tool"))
                .FirstOrDefault();
            if (morphirPackage != null)
            {
                Serilog.Log.Information("Publishing Morphir to local feed...");
                DotNetNuGetPush(s => s
                    .SetTargetPath(morphirPackage)
                    .SetSource(LocalSource)
                    .SetSkipDuplicate(true));
            }

            Serilog.Log.Information($"Libraries published to local feed: {LocalSource}");
        });

    Target PublishLocalTool => _ => _
        .DependsOn(PackTool)
        .Description("Install the Morphir CLI tool locally from the package")
        .Executes(() =>
        {
            var toolPackage = OutputDir.GlobFiles("Morphir.Tool.*.nupkg")
                .FirstOrDefault();

            if (toolPackage == null)
            {
                throw new Exception($"Morphir.Tool package not found in {OutputDir}. Please run PackTool first.");
            }

            var installCommand = Global
                ? "dotnet tool install --global --add-source"
                : "dotnet tool install --add-source";

            var updateCommand = Global
                ? "dotnet tool update --global --add-source"
                : "dotnet tool update --add-source";

            try
            {
                Serilog.Log.Information($"Installing Morphir CLI tool {(Global ? "globally" : "locally")} from: {toolPackage}");
                DotNet($"tool install {(Global ? "--global" : "")} --add-source {OutputDir} Morphir.Tool");
            }
            catch
            {
                Serilog.Log.Information("Tool already installed, updating...");
                DotNet($"tool update {(Global ? "--global" : "")} --add-source {OutputDir} Morphir.Tool");
            }

            Serilog.Log.Information("Morphir CLI tool installed successfully");
        });

    Target PublishLocalAll => _ => _
        .DependsOn(PublishLocalLibs, PublishLocalTool)
        .Description("Publish all packages locally (libraries to local feed, tool installed locally)")
        .Executes(() =>
        {
            Serilog.Log.Information("All packages published locally successfully");
        });
}
