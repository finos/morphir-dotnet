// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of <c>generate</c>.</summary>
public sealed record GenerationOptions
{
    /// <summary>Write one JSON report to standard output. Default true.</summary>
    public bool Json { get; init; } = true;

    /// <summary><c>auto</c>, <c>json</c> or <c>ion</c>. Without it, the command decides.</summary>
    public string? InputFormat { get; init; }

    /// <summary>The backend options, each as <c>KEY=VALUE</c>, in the order of the command line.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>The <c>generate</c> command as an intent: generate the artifacts of a target from a Morphir model.</summary>
public interface IGenerationDriver
{
    /// <summary>Generates from a model that is one file.</summary>
    Task<CommandResult> GenerateAsync(
        WorkspaceFile model, string target, WorkspaceDirectory output, GenerationOptions? options = null, CancellationToken cancellation = default);

    /// <summary>Generates from a model that is a tree of files in a directory.</summary>
    Task<CommandResult> GenerateAsync(
        WorkspaceDirectory modelTree, string target, WorkspaceDirectory output, GenerationOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>Builds the command line of <c>generate</c> and gives it to the CLI driver.</summary>
public sealed class GenerationDriver(ICliDriver cli) : IGenerationDriver
{
    public Task<CommandResult> GenerateAsync(
        WorkspaceFile model, string target, WorkspaceDirectory output, GenerationOptions? options = null, CancellationToken cancellation = default) =>
        GenerateAsync(model.Path, target, output, options, cancellation);

    public Task<CommandResult> GenerateAsync(
        WorkspaceDirectory modelTree, string target, WorkspaceDirectory output, GenerationOptions? options = null, CancellationToken cancellation = default) =>
        GenerateAsync(modelTree.Path, target, output, options, cancellation);

    private Task<CommandResult> GenerateAsync(
        string input, string target, WorkspaceDirectory output, GenerationOptions? options, CancellationToken cancellation)
    {
        options ??= new GenerationOptions();
        var arguments = new List<string> { "generate", "--input", input, "--target", target, "--output", output.Path };
        if (options.Json) arguments.Add("--json");
        if (options.InputFormat is { } format)
        {
            arguments.Add("--input-format");
            arguments.Add(format);
        }

        foreach (var option in options.Options)
        {
            arguments.Add("--option");
            arguments.Add(option);
        }

        return cli.RunAsync(arguments, cancellation: cancellation);
    }
}

/// <summary>One diagnostic of a generation report.</summary>
public sealed record GenerationDiagnostic(string Code, string Message, string? Path, string? Location, string? Severity);

/// <summary>
/// The JSON report of <c>generate --json</c>. It states success in the field <c>success</c>, which must agree with
/// the exit code of the command. The meaning of the report belongs here, not to the general checks of a command
/// result.
/// </summary>
public sealed class GenerationReport
{
    private readonly CommandResult _result;

    private GenerationReport(CommandResult result, JsonElement document, bool success)
    {
        _result = result;
        Success = success;
        FailureKind = Text(document, "failureKind");
        Backend = Text(document, "backend");
        SourceVersion = Text(document, "sourceVersion");
        Artifacts = Texts(result, document, "artifacts");
        Written = Texts(result, document, "written");
        Unchanged = Texts(result, document, "unchanged");
        Removed = Texts(result, document, "removed");
        Diagnostics =
        [
            .. List(result, document, "diagnostics").Select(item => item.ValueKind == JsonValueKind.Object
                ? new GenerationDiagnostic(Text(item, "code") ?? "", Text(item, "message") ?? "", Text(item, "path"), Text(item, "location"), Text(item, "severity"))
                : throw new CommandAssertionException("An item of the list diagnostics of the generation report is not an object.", result)),
        ];
    }

    public bool Success { get; }

    /// <summary><c>invocation</c>, <c>operational</c> or <c>semantic</c>; null for a success.</summary>
    public string? FailureKind { get; }

    public string? Backend { get; }

    public string? SourceVersion { get; }

    /// <summary>The files of the generation, relative to the output directory.</summary>
    public IReadOnlyList<string> Artifacts { get; }

    public IReadOnlyList<string> Written { get; }

    public IReadOnlyList<string> Unchanged { get; }

    public IReadOnlyList<string> Removed { get; }

    public IReadOnlyList<GenerationDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Reads the report from standard output, which must be exactly one JSON document. A report that disagrees
    /// with the exit code of the command is an error, and so is a report in which one of the lists
    /// (<c>artifacts</c>, <c>written</c>, <c>unchanged</c>, <c>removed</c>, <c>diagnostics</c>) is not there or has
    /// an item of another form: a check that a list is empty must not pass because the list is absent.
    /// </summary>
    public static GenerationReport Of(CommandResult result)
    {
        var document = result.ShouldWriteOneJsonDocument();
        if (document.ValueKind != JsonValueKind.Object
            || !document.TryGetProperty("success", out var field)
            || field.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new CommandAssertionException("The generation report has no boolean field success.", result);
        }

        var success = field.GetBoolean();
        if (success != (result.ExitCode == 0))
            throw new CommandAssertionException($"The generation report disagrees with the command: success is {Word(success)}, but the exit code is {result.ExitCode}.", result);
        if (document.TryGetProperty("exitCode", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != result.ExitCode)
            throw new CommandAssertionException($"The generation report disagrees with the command: exitCode is {code.GetInt32()}, but the exit code is {result.ExitCode}.", result);

        return new GenerationReport(result, document, success);
    }

    public GenerationReport ShouldSucceed() =>
        Success
            ? this
            : throw new CommandAssertionException(
                $"The generation report says that the generation failed: success is false, failureKind is {FailureKind ?? "null"}. Diagnostics: {DescribeDiagnostics()}", _result);

    /// <summary>The generation failed with <paramref name="failureKind"/>: <c>invocation</c>, <c>operational</c> or <c>semantic</c>.</summary>
    public GenerationReport ShouldFail(string failureKind)
    {
        if (Success)
            throw new CommandAssertionException("The generation report says that the generation succeeded: success is true.", _result);
        if (!string.Equals(FailureKind, failureKind, StringComparison.Ordinal))
            throw new CommandAssertionException($"The failure kind of the generation report is {FailureKind ?? "null"}, not {failureKind}.", _result);
        return this;
    }

    private string DescribeDiagnostics() =>
        Diagnostics.Count == 0 ? "none" : string.Join("; ", Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string Word(bool value) => value ? "true" : "false";

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<JsonElement> List(CommandResult result, JsonElement document, string name) =>
        document.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
            : throw new CommandAssertionException($"The generation report has no list {name}.", result);

    private static IReadOnlyList<string> Texts(CommandResult result, JsonElement document, string name) =>
    [
        .. List(result, document, name).Select(item => item.ValueKind == JsonValueKind.String
            ? item.GetString()!
            : throw new CommandAssertionException($"An item of the list {name} of the generation report is not a text.", result)),
    ];
}
