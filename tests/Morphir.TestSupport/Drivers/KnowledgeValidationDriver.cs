// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Drivers;

/// <summary>The optional version, policy and output mode of a knowledge bundle check.</summary>
public sealed record KnowledgeValidationOptions
{
    public string? Version { get; init; }
    public bool MorphirPolicy { get; init; }
    public JsonOutputArgument? Json { get; init; }
}

/// <summary>Validates a knowledge bundle using the host selected by the test.</summary>
public interface IKnowledgeValidationDriver
{
    Task<CommandResult> ValidateAsync(WorkspaceDirectory bundle, KnowledgeValidationOptions? options = null,
        CancellationToken cancellation = default);

    Task<CommandResult> HelpAsync(CancellationToken cancellation = default);
}

/// <summary>Turns the intent to validate a knowledge bundle into CLI arguments.</summary>
public sealed class KnowledgeValidationDriver(ICliDriver cli) : IKnowledgeValidationDriver
{
    public Task<CommandResult> ValidateAsync(WorkspaceDirectory bundle, KnowledgeValidationOptions? options = null,
        CancellationToken cancellation = default)
    {
        var arguments = new List<string> { "knowledge", "validate", bundle.Path };
        if (options?.Version is { } version) arguments.AddRange(["--okf-version", version]);
        if (options?.MorphirPolicy == true) arguments.AddRange(["--policy", "morphir"]);
        if (options?.Json is { } json) arguments.AddRange([.. json.Words, .. json.Trailing]);
        return cli.RunAsync(arguments, cancellation: cancellation);
    }

    public Task<CommandResult> HelpAsync(CancellationToken cancellation = default) =>
        cli.RunAsync(["knowledge", "validate", "--help"], cancellation: cancellation);
}

/// <summary>The structured result of checking a knowledge bundle.</summary>
public sealed class KnowledgeValidationReport : JsonCommandReport
{
    private KnowledgeValidationReport(CommandResult result) : base(result, "knowledge validation") { }

    public string? Version => Text("version");
    public string? Conformance => Text("conformance");

    public static KnowledgeValidationReport Of(CommandResult result) => new(result);

    public void ShouldHaveLocatedFinding(string path) =>
        Check(Required("diagnostics").EnumerateArray().Any(finding =>
                finding.TryGetProperty("path", out var location) && location.GetString() == path
                && finding.TryGetProperty("range", out var range) && range.ValueKind == JsonValueKind.Object),
            $"The knowledge report has no located finding for '{path}'.");
}
