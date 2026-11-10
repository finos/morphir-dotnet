// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Morphir.Conformance.WasmHost;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Mck;

/// <summary>
/// Registers the experimental <c>morphir mck wasm-host</c> commands (OpenSpec change add-wasm-extension-host,
/// task 1.5). They are internal: the compatibility-kit change folds them into its general <c>mck</c> command.
/// </summary>
public sealed class MckCommandProvider : ICommandProvider
{
    private readonly Action<string> _stdout;
    private readonly TextWriter _stderr;

    /// <summary>Writes results to the process's stdout and diagnostics to its stderr.</summary>
    public MckCommandProvider() : this(ConsoleOutputHelper.WriteLineToStdout, Console.Error) { }

    /// <summary>Writes results with <paramref name="stdout"/> and diagnostics to <paramref name="stderr"/>.</summary>
    public MckCommandProvider(Action<string> stdout, TextWriter stderr)
    {
        _stdout = stdout;
        _stderr = stderr;
    }

    /// <summary>The help text that marks a command experimental.</summary>
    internal const string Experimental = "[experimental, internal]";

    public void RegisterCommands(RootCommand rootCommand, IServiceCollection services) => rootCommand.Subcommands.Add(Create(_stdout, _stderr));

    /// <summary>The <c>mck</c> command with its <c>wasm-host</c> subcommands.</summary>
    public static Command Create(Action<string> stdout, TextWriter stderr)
    {
        var wasmHost = new Command("wasm-host", $"{Experimental} Qualify a WASM extension host through its worker mode with the crafted cases of the wasm-host suite");
        wasmHost.Subcommands.Add(new WasmHostRunCommand(stdout, stderr));
        wasmHost.Subcommands.Add(new WasmHostReportCheckCommand(stdout, stderr));
        var mck = new Command("mck", $"{Experimental} Morphir Compatibility Kit runners");
        mck.Subcommands.Add(wasmHost);
        return mck;
    }

    /// <summary>Writes a command result: one JSON document on stdout, each diagnostic on stderr.</summary>
    internal static int Render(WasmHostCommandResult result, Action<string> stdout, TextWriter stderr)
    {
        foreach (var line in result.Diagnostics) stderr.WriteLine(line);
        stdout(result.Output);
        return result.ExitCode;
    }

    /// <summary>The result of an invocation that the parser refused: exit status 2 and the usage error as JSON.</summary>
    internal static int RenderParseErrors(string command, ParseResult parsed, Action<string> stdout, TextWriter stderr)
    {
        var message = string.Join(" ", parsed.Errors.Select(error => error.Message));
        stderr.WriteLine($"Error: mck.usage: {message}");
        stdout(WasmHostCommands.UsageError(command, message).Output);
        return 2;
    }
}

/// <summary><c>morphir mck wasm-host run</c>: runs every case of the kit against a host and writes the report.</summary>
public sealed class WasmHostRunCommand : Command, IMorphirParseErrorHandler
{
    private readonly Action<string> _stdout;
    private readonly TextWriter _stderr;
    private readonly Option<string?> _kit = new("--kit") { Description = "The kit directory, for example conformance/suites/wasm-host" };
    private readonly Option<string?> _host = new("--host") { Description = "The host executable; the runner starts '<host> [host-args] extension worker ...'" };
    private readonly Option<string[]> _hostArgs = new("--host-arg") { Description = "An argument before 'extension worker'; repeatable", AllowMultipleArgumentsPerToken = false };
    private readonly Option<string[]> _profiles = new("--host-profile") { Description = "A profile the host declares it runs (extism-kernel-v1 is always run); repeatable", AllowMultipleArgumentsPerToken = false };
    private readonly Option<string?> _offering = new("--offering") { Description = "The host's offering, for example native-aot or dotnet-tool" };
    private readonly Option<string?> _platform = new("--platform") { Description = "The host's platform (default: this machine's runtime identifier)" };
    private readonly Option<string?> _engine = new("--engine") { Description = "The engine the host reports, for example 'wasmtime 50.0.1'" };
    private readonly Option<string?> _report = new("--report") { Description = "The report file to write" };
    private readonly Option<string?> _deviations = new("--deviations") { Description = "A JSON file of known deviations that explain failures; they never change an outcome" };
    private readonly Option<string?> _guestReport = new("--guest-kit-report") { Description = "The report of a passing generic guest kit run through the same host executable" };
    private readonly Option<string?> _guestHost = new("--guest-kit-host-sha256") { Description = "The host executable digest that the cited guest kit report records" };
    private readonly Option<string?> _guestModule = new("--guest-kit-module-sha256") { Description = "The digest of the published guest module the cited run used" };

    public WasmHostRunCommand(Action<string> stdout, TextWriter stderr)
        : base("run", $"{MckCommandProvider.Experimental} Run the crafted cases of a kit against a host's worker mode and write a report of per-case observations (no qualification verdict)")
    {
        _stdout = stdout;
        _stderr = stderr;
        foreach (var option in new Option[] { _kit, _host, _hostArgs, _profiles, _offering, _platform, _engine, _report, _deviations, _guestReport, _guestHost, _guestModule })
            Options.Add(option);
        SetAction(ExecuteAsync);
    }

    public int HandleParseErrors(ParseResult parsed) => MckCommandProvider.RenderParseErrors("mck wasm-host run", parsed, _stdout, _stderr);

    private async Task<int> ExecuteAsync(ParseResult parsed, CancellationToken ct)
    {
        var result = await WasmHostCommands.RunAsync(
            parsed.GetValue(_kit), parsed.GetValue(_host), parsed.GetValue(_hostArgs) ?? [], parsed.GetValue(_profiles) ?? [],
            parsed.GetValue(_offering), parsed.GetValue(_platform), parsed.GetValue(_engine), parsed.GetValue(_report),
            parsed.GetValue(_deviations), parsed.GetValue(_guestReport), parsed.GetValue(_guestHost), parsed.GetValue(_guestModule),
            _stderr.WriteLine, ct).ConfigureAwait(false);
        return MckCommandProvider.Render(result, _stdout, _stderr);
    }
}

/// <summary><c>morphir mck wasm-host report-check</c>: checks a report against the kit and recomputes its outcomes.</summary>
public sealed class WasmHostReportCheckCommand : Command, IMorphirParseErrorHandler
{
    private readonly Action<string> _stdout;
    private readonly TextWriter _stderr;
    private readonly Option<string?> _kit = new("--kit") { Description = "The kit directory the report was made with" };
    private readonly Option<string?> _report = new("--report") { Description = "The report to check" };
    private readonly Option<string?> _offering = new("--expect-offering") { Description = "The offering the report is cited for; a report of another offering is refused" };
    private readonly Option<string?> _platform = new("--expect-platform") { Description = "The platform the report is cited for; a report of another platform is refused" };

    public WasmHostReportCheckCommand(Action<string> stdout, TextWriter stderr)
        : base("report-check", $"{MckCommandProvider.Experimental} Check a wasm-host report against its kit, recomputing every outcome from the recorded observations")
    {
        _stdout = stdout;
        _stderr = stderr;
        foreach (var option in new Option[] { _kit, _report, _offering, _platform }) Options.Add(option);
        SetAction(Execute);
    }

    public int HandleParseErrors(ParseResult parsed) => MckCommandProvider.RenderParseErrors("mck wasm-host report-check", parsed, _stdout, _stderr);

    private int Execute(ParseResult parsed) => MckCommandProvider.Render(
        WasmHostCommands.CheckReport(parsed.GetValue(_kit), parsed.GetValue(_report), parsed.GetValue(_offering), parsed.GetValue(_platform)),
        _stdout, _stderr);
}
