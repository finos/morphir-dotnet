// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// The search path and the variables of one routed command. The product gets exactly these folders as its
/// <c>PATH</c>, so a scenario controls where it finds a tool.
/// </summary>
public sealed record ToolRoutingOptions
{
    /// <summary>The folders of <c>PATH</c>, in order. No folder: the product finds no tool on <c>PATH</c>.</summary>
    public IReadOnlyList<WorkspaceDirectory> Path { get; init; } = [];

    /// <summary>Put the current directory (<c>.</c>) on <c>PATH</c> after the folders.</summary>
    public bool CurrentDirectoryOnPath { get; init; }

    /// <summary>Variables for the command, for example <c>MORPHIR_MCK</c> or a <c>MCK_STUB_</c> control of the stub.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    /// <summary>Text for standard input.</summary>
    public string? StandardInput { get; init; }
}

/// <summary>What the stub <c>morphir-mck</c> reported on its standard output: its arguments, its directory and its input.</summary>
public sealed record StubReport(IReadOnlyList<string> Arguments, string WorkingDirectory, string? StandardInput)
{
    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static StubReport Of(CommandResult result)
    {
        var root = result.ShouldWriteOneJsonDocument();
        try
        {
            return new StubReport(
                [.. root.GetProperty("args").EnumerateArray().Select(argument => argument.GetString()!)],
                root.GetProperty("cwd").GetString()!,
                root.GetProperty("stdin").GetString());
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException)
        {
            throw new CommandAssertionException($"Standard output is not a report of the stub morphir-mck: {error.Message}", result);
        }
    }
}

/// <summary>
/// The routing of a command of the product to an external tool, as <c>morphir mck</c> runs <c>morphir-mck</c>: the
/// driver installs the stub of the tool in a folder of the workspace, runs the product with the search path of the
/// scenario, and runs the stub alone, as a user runs the standalone tool.
/// </summary>
public interface IToolRoutingDriver
{
    /// <summary>Copies the stub <c>morphir-mck</c> into <paramref name="folder"/> as <paramref name="name"/>, and returns its executable.</summary>
    WorkspaceFile InstallStub(WorkspaceDirectory folder, string name = "morphir-mck");

    /// <summary>The file name of the executable of a tool on this machine: <c>morphir-mck.exe</c> on Windows, else <c>morphir-mck</c>.</summary>
    string ExecutableName(string tool);

    /// <summary>Runs the product with <paramref name="arguments"/> and the search path of <paramref name="options"/>.</summary>
    Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, ToolRoutingOptions options, CancellationToken cancellation = default);

    /// <summary>Runs the stub alone with <paramref name="arguments"/>, in the same environment.</summary>
    Task<CommandResult> RunStubAsync(WorkspaceFile stub, IReadOnlyList<string> arguments, ToolRoutingOptions options, CancellationToken cancellation = default);
}

/// <summary>The routing driver on the CLI driver and on the built stub of the development build.</summary>
public sealed class ToolRoutingDriver(ICliDriver cli, IScenarioWorkspace workspace, ExtensionArtifacts? built = null) : IToolRoutingDriver
{
    /// <summary>The time limit of one routed command.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static readonly string Suffix = OperatingSystem.IsWindows() ? ".exe" : "";

    /// <summary>The root of the runtime that runs the tests, for the apphost of the stub.</summary>
    public static string DotnetRoot { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));

    public WorkspaceFile InstallStub(WorkspaceDirectory folder, string name = "morphir-mck")
    {
        var assembly = (built ?? ExtensionArtifacts.None).PathOf(BuiltExtension.MckStub);
        var output = System.IO.Path.GetDirectoryName(assembly)!;
        workspace.CreateDirectory(folder);
        foreach (var file in new[] { "morphir-mck" + Suffix, "morphir-mck.dll", "morphir-mck.runtimeconfig.json", "morphir-mck.deps.json" })
        {
            var source = System.IO.Path.Combine(output, file);
            if (File.Exists(source)) File.Copy(source, folder.File(file.Replace("morphir-mck", name, StringComparison.Ordinal)).Path, overwrite: true);
        }

        var executable = folder.File(name + Suffix);
        return workspace.Exists(executable)
            ? executable
            : throw new TestSessionException($"The stub morphir-mck has no executable in {output}. Build it: dotnet build tests/Morphir.Mck.Stub/Morphir.Mck.Stub.csproj");
    }

    public string ExecutableName(string tool) => tool + Suffix;

    public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, ToolRoutingOptions options, CancellationToken cancellation = default) =>
        cli.RunAsync(arguments, RunOptions(options), cancellation);

    public async Task<CommandResult> RunStubAsync(WorkspaceFile stub, IReadOnlyList<string> arguments, ToolRoutingOptions options, CancellationToken cancellation = default)
    {
        var info = new ProcessStartInfo(stub.Path);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        workspace.Contain(info);
        foreach (var (name, value) in RunOptions(options).Environment!)
        {
            if (value is null) info.Environment.Remove(name);
            else info.Environment[name] = value;
        }

        var mark = workspace.Mark();
        var result = await ChildProcess.RunAsync(info, Timeout, cancellation, standardInput: options.StandardInput).ConfigureAwait(false);
        return new CommandResult(
            string.Join(' ', new[] { stub.Path }.Concat(arguments)), result.ExitCode, result.StandardOutput, result.StandardError,
            result.ExitedAt - result.StartedAt, workspace.FilesWrittenSince(mark));
    }

    private static CliRunOptions RunOptions(ToolRoutingOptions options)
    {
        var path = options.Path.Select(folder => folder.Path).ToList();
        if (options.CurrentDirectoryOnPath) path.Add(".");
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = string.Join(System.IO.Path.PathSeparator, path),
            ["DOTNET_ROOT"] = DotnetRoot,
            ["MORPHIR_MCK"] = null,
        };
        // The stub reads its controls from the environment; the developer's own values must not reach it.
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(name => name.StartsWith("MCK_STUB_", StringComparison.Ordinal)))
            environment[name] = null;
        foreach (var (name, value) in options.Variables) environment[name] = value;
        return new CliRunOptions { Timeout = Timeout, StandardInput = options.StandardInput, Environment = environment };
    }
}
