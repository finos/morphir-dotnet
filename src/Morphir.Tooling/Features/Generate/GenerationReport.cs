using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Wire report uses serialization-neutral fields and one source-generated JSON contract.</summary>
public sealed record GenerationReport(bool Success, int ExitCode, string? Target, string? Backend, string? Input,
    string? Output, string? SourceVersion, string? Family, string? DistributionKind, string? Representation,
    string[] Artifacts, string[] Written, string[] Unchanged, string[] Removed, GenerationDiagnostic[] Diagnostics, string? FailureKind)
{
    /// <summary>
    /// How the selected backend extension ran, for example <c>mep</c>. Written only when an extension was selected,
    /// so a report of a built-in backend keeps its members.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackendTransport { get; init; }

    /// <summary>The version of the selected backend extension, as it gave it. Written only for an extension.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackendVersion { get; init; }

    public static GenerationReport From(Outcome<GenerationSuccess> outcome, GenerateOverrides invocation) => outcome switch
    {
        Outcome<GenerationSuccess>.Success success => new(true, 0, success.Value.Target, success.Value.Backend, success.Value.InputPath,
            success.Value.OutputDirectory, success.Value.SourceVersion, success.Value.Family, success.Value.DistributionKind, success.Value.Representation,
            success.Value.Publication.Artifacts.ToArray(), success.Value.Publication.Written.ToArray(), success.Value.Publication.Unchanged.ToArray(),
            success.Value.Publication.Removed.ToArray(), success.Value.Diagnostics.ToArray(), null)
        {
            BackendTransport = success.Value.BackendTransport,
            BackendVersion = success.Value.BackendVersion,
        },
        Outcome<GenerationSuccess>.Failure failure => new(false, Exit(failure.Error.Kind), invocation.Target, failure.Error.Backend, invocation.Input, invocation.Output,
            null, null, null, null, [], [], [], [], failure.Error.Diagnostics.ToArray(), failure.Error.Kind.ToString().ToLowerInvariant())
        {
            BackendTransport = failure.Error.BackendTransport,
            BackendVersion = failure.Error.BackendVersion,
        },
        _ => throw new ArgumentException("Unknown generation outcome.", nameof(outcome))
    };

    private static int Exit(GenerationFailureKind kind) => kind switch
    {
        GenerationFailureKind.Semantic => 1,
        GenerationFailureKind.Cancelled => 130,
        _ => 2
    };

    public string ToJson() => JsonSerializer.Serialize(this, GenerationJsonContext.Default.GenerationReport);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(GenerationReport))]
internal partial class GenerationJsonContext : JsonSerializerContext;
