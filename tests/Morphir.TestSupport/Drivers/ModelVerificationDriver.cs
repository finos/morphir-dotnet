// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of <c>model verify</c>.</summary>
public sealed record VerificationOptions
{
    /// <summary>The schema version to check against. Without it, the command detects the version.</summary>
    public int? SchemaVersion { get; init; }

    /// <summary>
    /// The <c>--json</c> argument, or null for the text of a person. The driver puts it before the path of the model,
    /// so the path is the argument after a bare <c>--json</c>.
    /// </summary>
    public JsonOutputArgument? Json { get; init; }

    /// <summary>Write no output; the exit code is the result.</summary>
    public bool Quiet { get; init; }
}

/// <summary>The <c>model verify</c> command as an intent: verify a Morphir model file against its schema.</summary>
public interface IModelVerificationDriver
{
    Task<CommandResult> VerifyAsync(WorkspaceFile model, VerificationOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>Builds the command line of <c>model verify</c> and gives it to the CLI driver.</summary>
public sealed class ModelVerificationDriver(ICliDriver cli) : IModelVerificationDriver
{
    public Task<CommandResult> VerifyAsync(WorkspaceFile model, VerificationOptions? options = null, CancellationToken cancellation = default)
    {
        var arguments = new List<string> { "model", "verify" };
        if (options?.Json is { } json) arguments.AddRange(json.Words);
        arguments.Add(model.Path);
        if (options?.SchemaVersion is { } version)
        {
            arguments.Add("--schema-version");
            arguments.Add(version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (options?.Quiet == true) arguments.Add("--quiet");
        if (options?.Json is { } trailing) arguments.AddRange(trailing.Trailing);
        return cli.RunAsync(arguments, cancellation: cancellation);
    }
}

/// <summary>One error of a verification report.</summary>
public sealed record VerificationError(string Path, string Message, string? Expected, string? Found);

/// <summary>
/// The JSON report of <c>model verify --json</c>. It states validity in the field <c>IsValid</c> and the exit code of
/// the run in <c>exitCode</c>, so a usage failure (2) is told from a model that is not valid (1). The meaning of the
/// report belongs here, not to the general checks of a command result.
/// </summary>
public sealed class VerificationReport
{
    private readonly CommandResult _result;

    private VerificationReport(CommandResult result, bool isValid, string? schemaVersion, string? detectionMethod, IReadOnlyList<VerificationError> errors)
    {
        _result = result;
        Result = result;
        IsValid = isValid;
        SchemaVersion = schemaVersion;
        DetectionMethod = detectionMethod;
        Errors = errors;
    }

    public bool IsValid { get; }

    /// <summary>The result of the command that wrote the report.</summary>
    public CommandResult Result { get; }

    /// <summary>The full path of the file that the command verified (<c>FilePath</c>), or null when the report has none.</summary>
    public string? FilePath { get; private init; }

    public string? SchemaVersion { get; }

    /// <summary><c>auto</c> or <c>manual</c>.</summary>
    public string? DetectionMethod { get; }

    public IReadOnlyList<VerificationError> Errors { get; }

    /// <summary>The exit code that the report states (<c>exitCode</c>), or null when it states none.</summary>
    public int? ExitCode { get; private init; }

    /// <summary>Reads the report from standard output, in either form of <see cref="JsonOutputAssertions"/>.</summary>
    public static VerificationReport Of(CommandResult result)
    {
        var document = result.ReportOfEither();
        if (document.ValueKind != JsonValueKind.Object
            || !document.TryGetProperty("IsValid", out var isValid)
            || isValid.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new CommandAssertionException("The verification report has no boolean field IsValid.", result);
        }

        var errors = document.TryGetProperty("Errors", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(error => new VerificationError(
                Text(error, "Path") ?? "", Text(error, "Message") ?? "", Text(error, "Expected"), Text(error, "Found"))).ToArray()
            : [];
        return new VerificationReport(result, isValid.GetBoolean(), Text(document, "SchemaVersion"), Text(document, "DetectionMethod"), errors)
        {
            FilePath = Text(document, "FilePath"),
            ExitCode = document.TryGetProperty("exitCode", out var exitCode) && exitCode.ValueKind == JsonValueKind.Number && exitCode.TryGetInt32(out var code)
                ? code
                : null,
        };
    }

    /// <summary>The report states <paramref name="exitCode"/>, and it is the exit code of the run.</summary>
    public VerificationReport ShouldStateTheExitCode(int exitCode)
    {
        if (ExitCode != exitCode)
            throw new CommandAssertionException($"The verification report states the exit code {ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}, not {exitCode}.", _result);
        if (_result.ExitCode != exitCode)
            throw new CommandAssertionException($"The verification report states the exit code {exitCode}, and the run exits with {_result.ExitCode}.", _result);
        return this;
    }

    public VerificationReport ShouldBeValid() =>
        IsValid
            ? this
            : throw new CommandAssertionException(
                $"The verification report says that the model is not valid: IsValid is false. Errors: {string.Join("; ", Errors.Select(error => error.Message))}", _result);

    public VerificationReport ShouldBeInvalid() =>
        IsValid ? throw new CommandAssertionException("The verification report says that the model is valid: IsValid is true.", _result) : this;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
