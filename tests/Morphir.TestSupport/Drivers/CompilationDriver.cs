// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The form of the result of <c>compile</c> on standard output.</summary>
public enum CompilationOutput
{
    /// <summary>One JSON report (<c>--json</c>).</summary>
    Json,

    /// <summary>One JSON document for each line (<c>--json-lines</c>).</summary>
    JsonLines,

    /// <summary>Text for a person: no format option.</summary>
    Text,
}

/// <summary>The options of <c>compile</c>. A null option is not on the command line.</summary>
public sealed record CompilationOptions
{
    public CompilationOutput Output { get; init; } = CompilationOutput.Json;

    /// <summary>The project directory (<c>--project</c>).</summary>
    public WorkspaceDirectory? Project { get; init; }

    /// <summary>The source files, in order (<c>--input</c>).</summary>
    public IReadOnlyList<WorkspaceFile> Inputs { get; init; } = [];

    /// <summary>The project file that gives the ordered sources (<c>--project-file</c>).</summary>
    public WorkspaceFile? ProjectFile { get; init; }

    /// <summary>The source language (<c>--language</c>), for example <c>fsharp</c>.</summary>
    public string? Language { get; init; }

    /// <summary>The package name (<c>--package-name</c>), for example <c>acme/demo</c>.</summary>
    public string? PackageName { get; init; }

    /// <summary>The IR version to write (<c>--ir-version</c>).</summary>
    public string? IrVersion { get; init; }

    /// <summary>The directory to which the compile installs its IR (<c>--output</c>).</summary>
    public WorkspaceDirectory? Install { get; init; }

    /// <summary>The directory of the compile record and the published IR (<c>--out-dir</c>).</summary>
    public WorkspaceDirectory? OutDir { get; init; }

    /// <summary>The target of a generation after the compile (<c>--target</c>).</summary>
    public string? Target { get; init; }

    /// <summary>The output directory of that generation (<c>--generate-output</c>).</summary>
    public WorkspaceDirectory? GenerateOutput { get; init; }

    /// <summary>The installed frontend extension to use (<c>--extension-id</c>).</summary>
    public string? ExtensionId { get; init; }

    /// <summary>
    /// More arguments, after the others and in their order. For a scenario whose subject is an option that this
    /// record does not name, for example a combination of options that the command refuses.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

/// <summary>The <c>compile</c> command as an intent: compile the sources of a project to a Morphir IR.</summary>
public interface ICompilationDriver
{
    Task<CommandResult> CompileAsync(CompilationOptions options, CancellationToken cancellation = default);
}

/// <summary>Builds the command line of <c>compile</c> and gives it to the CLI driver.</summary>
public sealed class CompilationDriver(ICliDriver cli) : ICompilationDriver
{
    /// <summary>The time limit of a compile: the F# frontend starts the F# compiler service.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(180);

    public Task<CommandResult> CompileAsync(CompilationOptions options, CancellationToken cancellation = default)
    {
        var arguments = new List<string> { "compile" };
        if (options.Output == CompilationOutput.Json) arguments.Add("--json");
        if (options.Output == CompilationOutput.JsonLines) arguments.Add("--json-lines");
        Add(arguments, "--project", options.Project?.Path);
        if (options.Inputs.Count > 0)
        {
            arguments.Add("--input");
            arguments.AddRange(options.Inputs.Select(input => input.Path));
        }

        Add(arguments, "--project-file", options.ProjectFile?.Path);
        Add(arguments, "--language", options.Language);
        Add(arguments, "--package-name", options.PackageName);
        Add(arguments, "--ir-version", options.IrVersion);
        Add(arguments, "--output", options.Install?.Path);
        Add(arguments, "--out-dir", options.OutDir?.Path);
        Add(arguments, "--target", options.Target);
        Add(arguments, "--generate-output", options.GenerateOutput?.Path);
        Add(arguments, "--extension-id", options.ExtensionId);
        arguments.AddRange(options.Arguments);
        return cli.RunAsync(arguments, new CliRunOptions { Timeout = Timeout }, cancellation);
    }

    private static void Add(List<string> arguments, string option, string? value)
    {
        if (value is null) return;
        arguments.Add(option);
        arguments.Add(value);
    }
}

/// <summary>
/// The JSON report of <c>compile --json</c>. It states success in the field <c>success</c>, which must agree with the
/// exit code of the command, and it has the list <c>diagnostics</c>.
/// </summary>
public sealed class CompilationReport : JsonCommandReport
{
    private CompilationReport(CommandResult result) : base(result, "compilation")
    {
        if (Member("success") is not { ValueKind: JsonValueKind.True or JsonValueKind.False } success)
            throw new CommandAssertionException("The compilation report has no boolean field success.", result);
        if (Member("diagnostics") is not { ValueKind: JsonValueKind.Array })
            throw new CommandAssertionException("The compilation report has no list diagnostics.", result);

        Success = success.GetBoolean();
        if (Success != (result.ExitCode == 0))
            throw new CommandAssertionException($"The compilation report disagrees with the command: success is {(Success ? "true" : "false")}, but the exit code is {result.ExitCode}.", result);
    }

    public bool Success { get; }

    /// <summary>The IR version that the report states (<c>irVersion</c>).</summary>
    public string? IrVersion => Text("irVersion");

    /// <summary>The frontend that compiled the sources, for example <c>morphir-dotnet.fsharp (in-process)</c>.</summary>
    public string? Frontend => Text("frontend");

    /// <summary>The kind of a failure, for example <c>source</c>; null for a success.</summary>
    public string? FailureKind => Text("failureKind");

    /// <summary>The path of the IR that the compile wrote, or null.</summary>
    public string? Output => Text("output");

    /// <summary>The source documents, in the order of the compile (<c>sources.documents</c>).</summary>
    public IReadOnlyList<string> SourceDocuments => Texts("sources", "documents");

    /// <summary>The modules of the IR (<c>modules</c>).</summary>
    public IReadOnlyList<string> Modules => Texts("modules");

    /// <summary>The backend of the generation after the compile (<c>generation.backend</c>), or null.</summary>
    public string? GenerationBackend => Text("generation", "backend");

    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static CompilationReport Of(CommandResult result) => new(result);

    public CompilationReport ShouldSucceed()
    {
        Check(Success, $"The compilation report says that the compile failed: success is false, failureKind is {FailureKind ?? "null"}. Diagnostics: {DescribeDiagnostics()}");
        return this;
    }

    /// <summary>The report has a diagnostic with <paramref name="code"/> at the location <paramref name="uri"/>.</summary>
    public void ShouldHaveDiagnosticIn(string code, string uri) =>
        Check(Diagnostics.Any(diagnostic => diagnostic.Code == code && diagnostic.Location == uri),
            $"The compilation report has no diagnostic {code} in {uri}. It has: {DescribeDiagnostics()}.");

    private IReadOnlyList<string> Texts(params string[] path) =>
        Member(path) is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText())]
            : throw new CommandAssertionException($"The compilation report has no list {string.Join('.', path)}.", Result);
}
