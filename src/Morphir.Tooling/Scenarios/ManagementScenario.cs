using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morphir.Tooling.Configuration;

namespace Morphir.Tooling.Scenarios;

/// <summary>
/// Scenario for Morphir artifact management (dist, tools, extensions).
/// Provides commands for managing distributions, tools, and extensions with global-first and local override support.
/// </summary>
public class ManagementScenario : IMorphirCliScenario
{
    public void Configure(MorphirCliBuilder builder)
    {
        // Register services
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<WorkspaceDiscovery>();
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
    }

    private static Command CreateDistCommand()
    {
        var distCommand = new Command("dist", "Manage Morphir distributions");

        // dist list
        var listCommand = new Command("list", "List installed distributions");
        var listPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var listLocalOption = new Option<bool>("--local") { Description = "List local (project) installations only" };
        listCommand.Options.Add(listPlatformOption);
        listCommand.Options.Add(listLocalOption);
        listCommand.SetAction(parseResult => HandleDistList(
            parseResult.GetValue(listPlatformOption),
            parseResult.GetValue(listLocalOption)));
        distCommand.Subcommands.Add(listCommand);

        // dist install
        var installCommand = new Command("install", "Install a distribution");
        var installUrlArgument = new Argument<string>("url") { Description = "Source URL to download from" };
        var installVersionArgument = new Argument<string>("version") { Description = "Version to install" };
        var installPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var installLocalOption = new Option<bool>("--local") { Description = "Install locally (project-specific)" };
        installCommand.Arguments.Add(installUrlArgument);
        installCommand.Arguments.Add(installVersionArgument);
        installCommand.Options.Add(installPlatformOption);
        installCommand.Options.Add(installLocalOption);
        installCommand.SetAction(parseResult => HandleDistInstall(
            parseResult.GetValue(installUrlArgument)!,
            parseResult.GetValue(installVersionArgument)!,
            parseResult.GetValue(installPlatformOption),
            parseResult.GetValue(installLocalOption)));
        distCommand.Subcommands.Add(installCommand);

        // dist use
        var useCommand = new Command("use", "Set active distribution version");
        var useVersionArgument = new Argument<string>("version") { Description = "Version to use" };
        var usePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var useLocalOption = new Option<bool>("--local") { Description = "Set in local (project) scope" };
        useCommand.Arguments.Add(useVersionArgument);
        useCommand.Options.Add(usePlatformOption);
        useCommand.Options.Add(useLocalOption);
        useCommand.SetAction(parseResult => HandleDistUse(
            parseResult.GetValue(useVersionArgument)!,
            parseResult.GetValue(usePlatformOption),
            parseResult.GetValue(useLocalOption)));
        distCommand.Subcommands.Add(useCommand);

        // dist remove
        var removeCommand = new Command("remove", "Remove an installed distribution");
        var removeVersionArgument = new Argument<string>("version") { Description = "Version to remove" };
        var removePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var removeLocalOption = new Option<bool>("--local") { Description = "Remove from local (project) scope" };
        removeCommand.Arguments.Add(removeVersionArgument);
        removeCommand.Options.Add(removePlatformOption);
        removeCommand.Options.Add(removeLocalOption);
        removeCommand.SetAction(parseResult => HandleDistRemove(
            parseResult.GetValue(removeVersionArgument)!,
            parseResult.GetValue(removePlatformOption),
            parseResult.GetValue(removeLocalOption)));
        distCommand.Subcommands.Add(removeCommand);

        // dist which
        var whichCommand = new Command("which", "Show active distribution");
        var whichPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        whichCommand.Options.Add(whichPlatformOption);
        whichCommand.SetAction(parseResult => HandleDistWhich(
            parseResult.GetValue(whichPlatformOption)));
        distCommand.Subcommands.Add(whichCommand);

        return distCommand;
    }

    private static Command CreateToolCommand()
    {
        var toolCommand = new Command("tool", "Manage Morphir tools");

        // tool list
        var listCommand = new Command("list", "List installed tools");
        var listPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var listLocalOption = new Option<bool>("--local") { Description = "List local (project) installations only" };
        listCommand.Options.Add(listPlatformOption);
        listCommand.Options.Add(listLocalOption);
        listCommand.SetAction(parseResult => HandleToolList(
            parseResult.GetValue(listPlatformOption),
            parseResult.GetValue(listLocalOption)));
        toolCommand.Subcommands.Add(listCommand);

        // tool install
        var installCommand = new Command("install", "Install a tool");
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
        installCommand.SetAction(parseResult => HandleToolInstall(
            parseResult.GetValue(installNameArgument)!,
            parseResult.GetValue(installUrlArgument)!,
            parseResult.GetValue(installVersionArgument)!,
            parseResult.GetValue(installPlatformOption),
            parseResult.GetValue(installLocalOption)));
        toolCommand.Subcommands.Add(installCommand);

        // tool use
        var useCommand = new Command("use", "Set active tool version");
        var useNameArgument = new Argument<string>("name") { Description = "Tool name" };
        var useVersionArgument = new Argument<string>("version") { Description = "Version to use" };
        var usePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var useLocalOption = new Option<bool>("--local") { Description = "Set in local (project) scope" };
        useCommand.Arguments.Add(useNameArgument);
        useCommand.Arguments.Add(useVersionArgument);
        useCommand.Options.Add(usePlatformOption);
        useCommand.Options.Add(useLocalOption);
        useCommand.SetAction(parseResult => HandleToolUse(
            parseResult.GetValue(useNameArgument)!,
            parseResult.GetValue(useVersionArgument)!,
            parseResult.GetValue(usePlatformOption),
            parseResult.GetValue(useLocalOption)));
        toolCommand.Subcommands.Add(useCommand);

        // tool remove
        var removeCommand = new Command("remove", "Remove an installed tool");
        var removeNameArgument = new Argument<string>("name") { Description = "Tool name" };
        var removeVersionArgument = new Argument<string>("version") { Description = "Version to remove" };
        var removePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var removeLocalOption = new Option<bool>("--local") { Description = "Remove from local (project) scope" };
        removeCommand.Arguments.Add(removeNameArgument);
        removeCommand.Arguments.Add(removeVersionArgument);
        removeCommand.Options.Add(removePlatformOption);
        removeCommand.Options.Add(removeLocalOption);
        removeCommand.SetAction(parseResult => HandleToolRemove(
            parseResult.GetValue(removeNameArgument)!,
            parseResult.GetValue(removeVersionArgument)!,
            parseResult.GetValue(removePlatformOption),
            parseResult.GetValue(removeLocalOption)));
        toolCommand.Subcommands.Add(removeCommand);

        // tool which
        var whichCommand = new Command("which", "Show active tool version");
        var whichNameArgument = new Argument<string>("name") { Description = "Tool name" };
        var whichPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        whichCommand.Arguments.Add(whichNameArgument);
        whichCommand.Options.Add(whichPlatformOption);
        whichCommand.SetAction(parseResult => HandleToolWhich(
            parseResult.GetValue(whichNameArgument)!,
            parseResult.GetValue(whichPlatformOption)));
        toolCommand.Subcommands.Add(whichCommand);

        return toolCommand;
    }

    private static Command CreateExtensionCommand()
    {
        var extensionCommand = new Command("extension", "Manage Morphir extensions");

        // extension list
        var listCommand = new Command("list", "List installed extensions");
        var listPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var listLocalOption = new Option<bool>("--local") { Description = "List local (project) installations only" };
        listCommand.Options.Add(listPlatformOption);
        listCommand.Options.Add(listLocalOption);
        listCommand.SetAction(parseResult => HandleExtensionList(
            parseResult.GetValue(listPlatformOption),
            parseResult.GetValue(listLocalOption)));
        extensionCommand.Subcommands.Add(listCommand);

        // extension install
        var installCommand = new Command("install", "Install an extension");
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
        installCommand.SetAction(parseResult => HandleExtensionInstall(
            parseResult.GetValue(installNameArgument)!,
            parseResult.GetValue(installUrlArgument)!,
            parseResult.GetValue(installVersionArgument)!,
            parseResult.GetValue(installPlatformOption),
            parseResult.GetValue(installLocalOption)));
        extensionCommand.Subcommands.Add(installCommand);

        // extension use
        var useCommand = new Command("use", "Set active extension version");
        var useNameArgument = new Argument<string>("name") { Description = "Extension name" };
        var useVersionArgument = new Argument<string>("version") { Description = "Version to use" };
        var usePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var useLocalOption = new Option<bool>("--local") { Description = "Set in local (project) scope" };
        useCommand.Arguments.Add(useNameArgument);
        useCommand.Arguments.Add(useVersionArgument);
        useCommand.Options.Add(usePlatformOption);
        useCommand.Options.Add(useLocalOption);
        useCommand.SetAction(parseResult => HandleExtensionUse(
            parseResult.GetValue(useNameArgument)!,
            parseResult.GetValue(useVersionArgument)!,
            parseResult.GetValue(usePlatformOption),
            parseResult.GetValue(useLocalOption)));
        extensionCommand.Subcommands.Add(useCommand);

        // extension remove
        var removeCommand = new Command("remove", "Remove an installed extension");
        var removeNameArgument = new Argument<string>("name") { Description = "Extension name" };
        var removeVersionArgument = new Argument<string>("version") { Description = "Version to remove" };
        var removePlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        var removeLocalOption = new Option<bool>("--local") { Description = "Remove from local (project) scope" };
        removeCommand.Arguments.Add(removeNameArgument);
        removeCommand.Arguments.Add(removeVersionArgument);
        removeCommand.Options.Add(removePlatformOption);
        removeCommand.Options.Add(removeLocalOption);
        removeCommand.SetAction(parseResult => HandleExtensionRemove(
            parseResult.GetValue(removeNameArgument)!,
            parseResult.GetValue(removeVersionArgument)!,
            parseResult.GetValue(removePlatformOption),
            parseResult.GetValue(removeLocalOption)));
        extensionCommand.Subcommands.Add(removeCommand);

        // extension which
        var whichCommand = new Command("which", "Show active extension version");
        var whichNameArgument = new Argument<string>("name") { Description = "Extension name" };
        var whichPlatformOption = new Option<string?>("--platform") { Description = "Platform RID (defaults to current)" };
        whichCommand.Arguments.Add(whichNameArgument);
        whichCommand.Options.Add(whichPlatformOption);
        whichCommand.SetAction(parseResult => HandleExtensionWhich(
            parseResult.GetValue(whichNameArgument)!,
            parseResult.GetValue(whichPlatformOption)));
        extensionCommand.Subcommands.Add(whichCommand);

        return extensionCommand;
    }

    // Handler implementations
    private static int HandleDistList(string? platform, bool local)
    {
        // Simple implementation without DI - for now just print message
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Listing distributions (platform: {platform ?? "current"}, local: {local})");
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout("No distributions installed.");
        return 0;
    }

    private static int HandleDistInstall(string url, string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Installing distribution version {version} from {url} (platform: {platform ?? "current"}, local: {local})");
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout("Installation would occur here (not implemented in this handler stub).");
        return 0;
    }

    private static int HandleDistUse(string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Setting active distribution to version {version} (platform: {platform ?? "current"}, local: {local})");
        return 0;
    }

    private static int HandleDistRemove(string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Removing distribution version {version} (platform: {platform ?? "current"}, local: {local})");
        return 0;
    }

    private static int HandleDistWhich(string? platform)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Active distribution (platform: {platform ?? "current"}): none set");
        return 0;
    }

    private static int HandleToolList(string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Listing tools (platform: {platform ?? "current"}, local: {local})");
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout("No tools installed.");
        return 0;
    }

    private static int HandleToolInstall(string name, string url, string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Installing tool {name} version {version} from {url} (platform: {platform ?? "current"}, local: {local})");
        return 0;
    }

    private static int HandleToolUse(string name, string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Setting active tool {name} to version {version} (platform: {platform ?? "current"}, local: {local})");
        return 0;
    }

    private static int HandleToolRemove(string name, string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Removing tool {name} version {version} (platform: {platform ?? "current"}, local: {local})");
        return 0;
    }

    private static int HandleToolWhich(string name, string? platform)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Active tool {name} (platform: {platform ?? "current"}): none set");
        return 0;
    }

    private static int HandleExtensionList(string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Listing extensions (platform: {platform ?? "current"}, local: {local})");
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout("No extensions installed.");
        return 0;
    }

    private static int HandleExtensionInstall(string name, string url, string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Installing extension {name} version {version} from {url} (platform: {platform ?? "current"}, local: {local})");
        return 0;
    }

    private static int HandleExtensionUse(string name, string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Setting active extension {name} to version {version} (platform: {platform ?? "current"}, local: {local})");
        return 0;
    }

    private static int HandleExtensionRemove(string name, string version, string? platform, bool local)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Removing extension {name} version {version} (platform: {platform ?? "current"}, local: {local})");
        return 0;
    }

    private static int HandleExtensionWhich(string name, string? platform)
    {
        Infrastructure.ConsoleOutputHelper.WriteLineToStdout($"Active extension {name} (platform: {platform ?? "current"}): none set");
        return 0;
    }
}
