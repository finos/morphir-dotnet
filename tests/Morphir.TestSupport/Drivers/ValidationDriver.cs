// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of <c>validate</c>.</summary>
public sealed record ValidationOptions
{
    /// <summary>Write one JSON report to standard output.</summary>
    public bool Json { get; init; }

    /// <summary>More arguments, after the others. For a scenario whose subject is an option that the command refuses.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

/// <summary>The <c>validate</c> command as an intent: run the analyzers of the host on a Morphir model.</summary>
public interface IModelValidationDriver
{
    /// <summary>Validates the model in <paramref name="input"/>, in the work area of the workspace.</summary>
    Task<CommandResult> ValidateAsync(WorkspaceFile input, ValidationOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>Builds the command line of <c>validate</c> and gives it to the CLI driver.</summary>
public sealed class ModelValidationDriver(ICliDriver cli) : IModelValidationDriver
{
    public Task<CommandResult> ValidateAsync(WorkspaceFile input, ValidationOptions? options = null, CancellationToken cancellation = default)
    {
        options ??= new ValidationOptions();
        var arguments = new List<string> { "validate", "--input", input.Path };
        arguments.AddRange(options.Arguments);
        if (options.Json) arguments.Add("--json");
        return cli.RunAsync(arguments, cancellation: cancellation);
    }
}

/// <summary>
/// The JSON report of <c>validate --json</c>. It states success in the field <c>success</c>, which must agree with
/// the exit code of the command, as its field <c>exitCode</c> must.
/// </summary>
public sealed class ValidationReport : JsonCommandReport
{
    private ValidationReport(CommandResult result) : base(result, "validation")
    {
        if (Member("success") is { ValueKind: JsonValueKind.True or JsonValueKind.False } success)
        {
            Success = success.GetBoolean();
            if (Success != (result.ExitCode == 0))
                throw new CommandAssertionException($"The validation report disagrees with the command: success is {(Success ? "true" : "false")}, but the exit code is {result.ExitCode}.", result);
        }
        else if (result.ExitCode != 2)
        {
            // Only the report of a usage error (exit code 2) has no field success.
            throw new CommandAssertionException("The validation report has no boolean field success.", result);
        }

        if (Member("exitCode") is not { ValueKind: JsonValueKind.Number })
            throw new CommandAssertionException("The validation report has no field exitCode.", result);
    }

    /// <summary>False also for a report of a usage error, which has no field <c>success</c>.</summary>
    public bool Success { get; }

    /// <summary>The command name of the host that validated: <c>morphir</c> or <c>dotnet-morphir</c>.</summary>
    public string? Host => Text("host");

    /// <summary>The path of the model, as the command line gave it.</summary>
    public string? Input => Text("input");

    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static ValidationReport Of(CommandResult result) => new(result);
}
