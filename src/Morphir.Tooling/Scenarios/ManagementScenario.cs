using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morphir.Extensions;
using Morphir.Tooling.Features.Extensions;

namespace Morphir.Tooling.Scenarios;

/// <summary>
/// Scenario for Morphir artifact management (dist, tools, extensions).
/// Provides commands for managing distributions, tools, and extensions with global-first and local override support.
/// </summary>
public class ManagementScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder)
    {
        // Static registration only: each source is a known type, and the host identity comes from the entry point.
        builder.ConfigureServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensionSource, BackendExtensionSource>());
            // The process's WASM runtime, registered once (task 4.2, design RPT-1): the registry, extension list, the
            // loader and extension runtimes read this one value, which probes the engine only when one of them needs it.
            // The worker, which runs before this composition exists, reads the same WasmRuntime.Process.
            services.TryAddSingleton(WasmRuntime.Process);
            services.TryAddSingleton<ExtensionRegistry>();
            // The configured WASM module loader (design CFG-1). No command uses it yet (design review question 5).
            services.TryAddSingleton<IWasmModuleLoader, WasmModuleLoader>();
        });

        builder.AddCommand(rootCommand =>
        {
            // Create top-level dist, tool, and extension commands
            var distCommand = CreateDistCommand();
            var toolCommand = CreateToolCommand();
            var extensionCommand = CreateExtensionCommand();

            rootCommand.Subcommands.Add(distCommand);
            rootCommand.Subcommands.Add(toolCommand);
            rootCommand.Subcommands.Add(extensionCommand);
        });

        builder.ConfigureHostedCommands((rootCommand, services) =>
        {
            var extension = rootCommand.Subcommands.Single(command => command.Name == "extension");
            extension.Subcommands.Insert(0, new ExtensionListCommand(services));
            extension.Subcommands.Insert(1, new ExtensionRuntimesCommand(services));
        });
    }

    private static Command CreateDistCommand()
    {
        var distCommand = new Command("dist", "Manage Morphir distributions");

        // dist list
        var listCommand = new Command("list", "List installed distributions (not implemented yet)");
        var listPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var listLocalOption = new Option<bool>("--local") { Description = "List local (project) installations only" };
        listCommand.Options.Add(listPlatformOption);
        listCommand.Options.Add(listLocalOption);
        listCommand.SetAction(_ => NotImplemented("dist", "list"));
        distCommand.Subcommands.Add(listCommand);

        // dist install
        var installCommand = new Command("install", "Install a distribution (not implemented yet)");
        var installUrlArgument = new Argument<string>("url") { Description = "Source URL to download from" };
        var installVersionArgument = new Argument<string>("version") { Description = "Version to install" };
        var installPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var installLocalOption = new Option<bool>("--local") { Description = "Install locally (project-specific)" };
        installCommand.Arguments.Add(installUrlArgument);
        installCommand.Arguments.Add(installVersionArgument);
        installCommand.Options.Add(installPlatformOption);
        installCommand.Options.Add(installLocalOption);
        installCommand.SetAction(_ => NotImplemented("dist", "install"));
        distCommand.Subcommands.Add(installCommand);

        // dist use
        var useCommand = new Command("use", "Set active distribution version (not implemented yet)");
        var useVersionArgument = new Argument<string>("version") { Description = "Version to use" };
        var usePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var useLocalOption = new Option<bool>("--local") { Description = "Set in local (project) scope" };
        useCommand.Arguments.Add(useVersionArgument);
        useCommand.Options.Add(usePlatformOption);
        useCommand.Options.Add(useLocalOption);
        useCommand.SetAction(_ => NotImplemented("dist", "use"));
        distCommand.Subcommands.Add(useCommand);

        // dist remove
        var removeCommand = new Command("remove", "Remove an installed distribution (not implemented yet)");
        var removeVersionArgument = new Argument<string>("version") { Description = "Version to remove" };
        var removePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var removeLocalOption = new Option<bool>("--local") { Description = "Remove from local (project) scope" };
        removeCommand.Arguments.Add(removeVersionArgument);
        removeCommand.Options.Add(removePlatformOption);
        removeCommand.Options.Add(removeLocalOption);
        removeCommand.SetAction(_ => NotImplemented("dist", "remove"));
        distCommand.Subcommands.Add(removeCommand);

        // dist which
        var whichCommand = new Command("which", "Show active distribution (not implemented yet)");
        var whichPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        whichCommand.Options.Add(whichPlatformOption);
        whichCommand.SetAction(_ => NotImplemented("dist", "which"));
        distCommand.Subcommands.Add(whichCommand);

        return distCommand;
    }

    private static Command CreateToolCommand()
    {
        var toolCommand = new Command("tool", "Manage Morphir tools");

        // tool list
        var listCommand = new Command("list", "List installed tools (not implemented yet)");
        var listPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var listLocalOption = new Option<bool>("--local") { Description = "List local (project) installations only" };
        listCommand.Options.Add(listPlatformOption);
        listCommand.Options.Add(listLocalOption);
        listCommand.SetAction(_ => NotImplemented("tool", "list"));
        toolCommand.Subcommands.Add(listCommand);

        // tool install
        var installCommand = new Command("install", "Install a tool (not implemented yet)");
        var installNameArgument = new Argument<string>("name") { Description = "Tool name" };
        var installUrlArgument = new Argument<string>("url") { Description = "Source URL to download from" };
        var installVersionArgument = new Argument<string>("version") { Description = "Version to install" };
        var installPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var installLocalOption = new Option<bool>("--local") { Description = "Install locally (project-specific)" };
        installCommand.Arguments.Add(installNameArgument);
        installCommand.Arguments.Add(installUrlArgument);
        installCommand.Arguments.Add(installVersionArgument);
        installCommand.Options.Add(installPlatformOption);
        installCommand.Options.Add(installLocalOption);
        installCommand.SetAction(_ => NotImplemented("tool", "install"));
        toolCommand.Subcommands.Add(installCommand);

        // tool use
        var useCommand = new Command("use", "Set active tool version (not implemented yet)");
        var useNameArgument = new Argument<string>("name") { Description = "Tool name" };
        var useVersionArgument = new Argument<string>("version") { Description = "Version to use" };
        var usePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var useLocalOption = new Option<bool>("--local") { Description = "Set in local (project) scope" };
        useCommand.Arguments.Add(useNameArgument);
        useCommand.Arguments.Add(useVersionArgument);
        useCommand.Options.Add(usePlatformOption);
        useCommand.Options.Add(useLocalOption);
        useCommand.SetAction(_ => NotImplemented("tool", "use"));
        toolCommand.Subcommands.Add(useCommand);

        // tool remove
        var removeCommand = new Command("remove", "Remove an installed tool (not implemented yet)");
        var removeNameArgument = new Argument<string>("name") { Description = "Tool name" };
        var removeVersionArgument = new Argument<string>("version") { Description = "Version to remove" };
        var removePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var removeLocalOption = new Option<bool>("--local") { Description = "Remove from local (project) scope" };
        removeCommand.Arguments.Add(removeNameArgument);
        removeCommand.Arguments.Add(removeVersionArgument);
        removeCommand.Options.Add(removePlatformOption);
        removeCommand.Options.Add(removeLocalOption);
        removeCommand.SetAction(_ => NotImplemented("tool", "remove"));
        toolCommand.Subcommands.Add(removeCommand);

        // tool which
        var whichCommand = new Command("which", "Show active tool version (not implemented yet)");
        var whichNameArgument = new Argument<string>("name") { Description = "Tool name" };
        var whichPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        whichCommand.Arguments.Add(whichNameArgument);
        whichCommand.Options.Add(whichPlatformOption);
        whichCommand.SetAction(_ => NotImplemented("tool", "which"));
        toolCommand.Subcommands.Add(whichCommand);

        return toolCommand;
    }

    private static Command CreateExtensionCommand()
    {
        var extensionCommand = new Command("extension", "Manage Morphir extensions");

        // extension list and extension runtimes need the registry and the WASM runtime, so ConfigureHostedCommands
        // inserts them first, after the host is built.

        // extension install
        var installCommand = new Command("install", "Install an extension (not implemented yet)");
        var installNameArgument = new Argument<string>("name") { Description = "Extension name" };
        var installUrlArgument = new Argument<string>("url") { Description = "Source URL to download from" };
        var installVersionArgument = new Argument<string>("version") { Description = "Version to install" };
        var installPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var installLocalOption = new Option<bool>("--local") { Description = "Install locally (project-specific)" };
        installCommand.Arguments.Add(installNameArgument);
        installCommand.Arguments.Add(installUrlArgument);
        installCommand.Arguments.Add(installVersionArgument);
        installCommand.Options.Add(installPlatformOption);
        installCommand.Options.Add(installLocalOption);
        installCommand.SetAction(_ => NotImplemented("extension", "install"));
        extensionCommand.Subcommands.Add(installCommand);

        // extension use
        var useCommand = new Command("use", "Set active extension version (not implemented yet)");
        var useNameArgument = new Argument<string>("name") { Description = "Extension name" };
        var useVersionArgument = new Argument<string>("version") { Description = "Version to use" };
        var usePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var useLocalOption = new Option<bool>("--local") { Description = "Set in local (project) scope" };
        useCommand.Arguments.Add(useNameArgument);
        useCommand.Arguments.Add(useVersionArgument);
        useCommand.Options.Add(usePlatformOption);
        useCommand.Options.Add(useLocalOption);
        useCommand.SetAction(_ => NotImplemented("extension", "use"));
        extensionCommand.Subcommands.Add(useCommand);

        // extension remove
        var removeCommand = new Command("remove", "Remove an installed extension (not implemented yet)");
        var removeNameArgument = new Argument<string>("name") { Description = "Extension name" };
        var removeVersionArgument = new Argument<string>("version") { Description = "Version to remove" };
        var removePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var removeLocalOption = new Option<bool>("--local") { Description = "Remove from local (project) scope" };
        removeCommand.Arguments.Add(removeNameArgument);
        removeCommand.Arguments.Add(removeVersionArgument);
        removeCommand.Options.Add(removePlatformOption);
        removeCommand.Options.Add(removeLocalOption);
        removeCommand.SetAction(_ => NotImplemented("extension", "remove"));
        extensionCommand.Subcommands.Add(removeCommand);

        // extension which
        var whichCommand = new Command("which", "Show active extension version (not implemented yet)");
        var whichNameArgument = new Argument<string>("name") { Description = "Extension name" };
        var whichPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        whichCommand.Arguments.Add(whichNameArgument);
        whichCommand.Options.Add(whichPlatformOption);
        whichCommand.SetAction(_ => NotImplemented("extension", "which"));
        extensionCommand.Subcommands.Add(whichCommand);

        return extensionCommand;
    }

    // The dist, tool and extension handlers exist in Features/Management but are not wired to
    // the CLI yet. Until they are, fail clearly instead of reporting a success that did nothing.
    private static readonly Dictionary<string, int> TrackingIssues = new()
    {
        ["dist"] = 490,
        ["tool"] = 491,
        ["extension"] = 492,
    };

    private static int NotImplemented(string group, string subcommand)
    {
        Console.Error.WriteLine(
            $"Error: morphir {group} {subcommand} is not implemented yet. " +
            $"Track progress at https://github.com/finos/morphir-dotnet/issues/{TrackingIssues[group]}");
        return 1;
    }
}
