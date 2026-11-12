// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The options of <c>ir verify</c>.</summary>
public sealed record VerificationOptions
{
    /// <summary>The schema version to check against. Without it, the command detects the version.</summary>
    public int? SchemaVersion { get; init; }

    /// <summary>Write one JSON report to standard output.</summary>
    public bool Json { get; init; }

    /// <summary>Write no output; the exit code is the result.</summary>
    public bool Quiet { get; init; }
}

/// <summary>The <c>ir verify</c> command as an intent: verify a Morphir IR file against its schema.</summary>
public interface IModelVerificationDriver
{
    Task<CommandResult> VerifyAsync(WorkspaceFile model, VerificationOptions? options = null, CancellationToken cancellation = default);
}

/// <summary>Builds the command line of <c>ir verify</c> and gives it to the CLI driver.</summary>
public sealed class ModelVerificationDriver(ICliDriver cli) : IModelVerificationDriver
{
    public Task<CommandResult> VerifyAsync(WorkspaceFile model, VerificationOptions? options = null, CancellationToken cancellation = default)
    {
        var arguments = new List<string> { "ir", "verify", model.Path };
        if (options?.SchemaVersion is { } version)
        {
            arguments.Add("--schema-version");
            arguments.Add(version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (options?.Json == true) arguments.Add("--json");
        if (options?.Quiet == true) arguments.Add("--quiet");
        return cli.RunAsync(arguments, cancellation: cancellation);
    }
}

/// <summary>One error of a verification report.</summary>
public sealed record VerificationError(string Path, string Message, string? Expected, string? Found);

/// <summary>
/// The JSON report of <c>ir verify --json</c>. It states validity in the field <c>IsValid</c>. The meaning of the
/// report belongs here, not to the general checks of a command result.
/// </summary>
public sealed class VerificationReport
{
    private readonly CommandResult _result;

    private VerificationReport(CommandResult result, bool isValid, string? schemaVersion, string? detectionMethod, IReadOnlyList<VerificationError> errors)
    {
        _result = result;
        IsValid = isValid;
        SchemaVersion = schemaVersion;
        DetectionMethod = detectionMethod;
        Errors = errors;
    }

    public bool IsValid { get; }

    public string? SchemaVersion { get; }

    /// <summary><c>auto</c> or <c>manual</c>.</summary>
    public string? DetectionMethod { get; }

    public IReadOnlyList<VerificationError> Errors { get; }

    /// <summary>Reads the report from standard output, which must be exactly one JSON document.</summary>
    public static VerificationReport Of(CommandResult result)
    {
        var document = result.ShouldWriteOneJsonDocument();
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
        return new VerificationReport(result, isValid.GetBoolean(), Text(document, "SchemaVersion"), Text(document, "DetectionMethod"), errors);
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
