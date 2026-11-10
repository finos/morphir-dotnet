// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Microsoft.Extensions.DependencyInjection;
using Morphir.Extensions;
using Morphir.Tooling.Scenarios;

namespace Morphir.Tooling;

/// <summary>
/// The shared command layer of every Morphir .NET entry point (DEP-1): the <c>extension worker</c> mode, the
/// scenarios, the <c>info</c> command and the <c>--version</c> path. An entry point passes its host identity and its own composition, which runs after the
/// shared one. The AOT entry point <c>morphir</c> passes a composition without the F# compiler service; the managed
/// entry points pass the managed composition in <c>Morphir.Tooling.Managed</c>. This assembly never references the
/// F# compiler service.
/// </summary>
public static class MorphirApplication
{
    /// <summary>Runs the CLI as the given host.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="host">The host identity.</param>
    /// <param name="extend">The host's own composition, applied after the shared composition.</param>
    public static int Run(string[] args, HostIdentity host, Action<MorphirCliBuilder>? extend = null)
    {
        // morphir extension worker (add-wasm-extension-host, task 4.1, design WRK-2) runs before the CLI host is
        // built: stdout carries only MEP frames, the worker starts fast, and its Ctrl+C handler is the only one.
        // Every entry point starts here, so each one runs the worker with its own host identity and none of its
        // composition.
        if (Features.Extensions.ExtensionWorkerProcess.IsInvocation(args))
        {
            return Features.Extensions.ExtensionWorkerProcess.Run(args, host);
        }

        // Handle --version flag explicitly to output only the version string
        // This must be done before creating the builder to avoid console redirection
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            // Console redirection happens in MorphirCli.CreateBuilder, so we need to do it manually here
            Infrastructure.ConsoleOutputHelper.RedirectConsoleToStderr();
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout(host.Version.ToString());
            return 0;
        }

        return CreateApp(args, host, extend).Run();
    }

    /// <summary>
    /// The shared composition of every host, then the host's own composition. The host identity is registered
    /// as a singleton.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="host">The host identity.</param>
    /// <param name="extend">The host's own composition, applied after the shared composition.</param>
    public static MorphirCliApp CreateApp(string[] args, HostIdentity host, Action<MorphirCliBuilder>? extend = null)
    {
        var builder = MorphirCli
            .CreateBuilder(args, "Morphir command line")
            .ConfigureServices(services => services.AddSingleton(host))
            .AddScenario<IrToolingScenario>()
            .AddScenario<GenerationScenario>()
            .AddScenario<CompilationScenario>()
            .AddScenario<ManagementScenario>()
            .AddScenario<ValidationScenario>()
            .AddScenario<ConfigurationScenario>()
            .AddScenario<ProjectScenario>()
            .AddScenario<WorkspaceScenario>()
            // Experimental and internal: morphir mck wasm-host run and report-check (add-wasm-extension-host 1.5).
            .AddScenario<MckScenario>()
            .AddCommand(CreateInfoCommand());
        extend?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>
    /// Creates the 'info' command for workspace/project information.
    /// </summary>
    private static System.CommandLine.Command CreateInfoCommand()
    {
        var infoCommand = new System.CommandLine.Command("info", "Get information about the workspace/project");
        infoCommand.SetAction(parseResult =>
        {
            Infrastructure.ConsoleOutputHelper.WriteLineToStdout("Morphir command line info:");
        });
        return infoCommand;
    }
}
