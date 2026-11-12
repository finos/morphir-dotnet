// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of <c>validate</c>.</summary>
public sealed record ValidationOptions
{
    /// <summary><c>--json</c>: the report as one JSON document. Default true. False gives the text table.</summary>
    public bool Json { get; init; } = true;
}

/// <summary>The <c>validate</c> command as an intent: check a model with the built-in analyzers.</summary>
public interface IValidationDriver
{
    Task<CommandResult> ValidateAsync(WorkspaceFile model, ValidationOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>The validation driver on <see cref="ICliDriver"/>.</summary>
public sealed class ValidationDriver(ICliDriver cli) : IValidationDriver
{
    /// <summary>The arguments of <c>validate</c> for <paramref name="model"/>.</summary>
    public static IReadOnlyList<string> Arguments(WorkspaceFile model, ValidationOptions? options = null) =>
        (options ?? new ValidationOptions()).Json ? ["validate", "--input", model.Path, "--json"] : ["validate", "--input", model.Path];

    public Task<CommandResult> ValidateAsync(WorkspaceFile model, ValidationOptions? options = null, CancellationToken cancellation = default) =>
        cli.RunAsync(Arguments(model, options), cancellation: cancellation);
}

/// <summary>One diagnostic of a validation report.</summary>
public sealed record ValidationDiagnostic(string Severity, string Code, string? Extension);

/// <summary>One analyzer of a validation report, with its status, for example <c>ran</c> or <c>not-run</c>.</summary>
public sealed record ValidationAnalyzer(string Id, string Status);

/// <summary>
/// The JSON report of <c>validate --json</c>: <c>exitCode</c>, <c>success</c>, <c>host</c>, <c>diagnostics</c> and
/// <c>analyzers</c>. The report requires each of these members.
/// </summary>
public sealed record ValidationReport(
    CommandResult Result,
    int ExitCode,
    bool Success,
    string Host,
    ImmutableArray<ValidationDiagnostic> Diagnostics,
    ImmutableArray<ValidationAnalyzer> Analyzers)
{
    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static ValidationReport Of(CommandResult result)
    {
        var root = result.ShouldWriteOneJsonDocument();
        try
        {
            return new ValidationReport(
                result,
                root.GetProperty("exitCode").GetInt32(),
                root.GetProperty("success").GetBoolean(),
                root.GetProperty("host").GetString()!,
                [.. root.GetProperty("diagnostics").EnumerateArray().Select(diagnostic => new ValidationDiagnostic(
                    diagnostic.GetProperty("severity").GetString()!,
                    diagnostic.GetProperty("code").GetString()!,
                    diagnostic.TryGetProperty("extension", out var extension) ? extension.GetString() : null))],
                [.. root.GetProperty("analyzers").EnumerateArray().Select(analyzer => new ValidationAnalyzer(
                    analyzer.GetProperty("id").GetString()!, analyzer.GetProperty("status").GetString()!))]);
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException)
        {
            throw new CommandAssertionException($"Standard output is not a validation report: {error.Message}", result);
        }
    }

    /// <summary>The report states <paramref name="exitCode"/>, and <c>success</c> is true exactly when it is 0.</summary>
    public ValidationReport ShouldHaveTheExitCode(int exitCode) =>
        ExitCode == exitCode && Success == (exitCode == 0)
            ? this
            : throw new CommandAssertionException($"The validation report has exitCode {ExitCode} and success {Success}, not exitCode {exitCode}.", Result);

    /// <summary>The count of diagnostics with the severity and the code, and with the extension when it is given.</summary>
    public int Count(string severity, string code, string? extension = null) =>
        Diagnostics.Count(diagnostic => diagnostic.Severity == severity && diagnostic.Code == code && (extension is null || diagnostic.Extension == extension));

    /// <summary>
    /// Null when the reports of two hosts are equal apart from the member <c>host</c>, which must differ; otherwise
    /// what differs. The exit codes of the two commands must be equal.
    /// </summary>
    public static string? DifferenceApartFromTheHost(CommandResult mine, CommandResult theirs)
    {
        if (mine.ExitCode != theirs.ExitCode) return $"The exit codes are {mine.ExitCode} and {theirs.ExitCode}.";
        var first = JsonNode.Parse(mine.StandardOutput)!.AsObject();
        var second = JsonNode.Parse(theirs.StandardOutput)!.AsObject();
        if (first["host"]!.GetValue<string>() == second["host"]!.GetValue<string>()) return "Both reports name the same host.";
        first.Remove("host");
        second.Remove("host");
        var (one, other) = (first.ToJsonString(), second.ToJsonString());
        return one == other ? null : $"The reports differ apart from the host:{Environment.NewLine}{one}{Environment.NewLine}{other}";
    }
}
