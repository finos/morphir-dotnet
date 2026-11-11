using Morphir.Extensions;
using System.Collections.Immutable;
using System.Text.Json;
using Morphir.CodeGeneration.FSharp;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Static F# provider over semantic models. Publication belongs to its caller.</summary>
public sealed class FSharpGenerationBackend : IGenerationBackend
{
    private sealed record Options(bool Optimize, bool Format) : ValidatedBackendOptions;

    public BackendDescriptor Descriptor { get; } = new(ExtensionId.Create("morphir.fsharp"), ExtensionName.Create("F# generator"), TargetId.Create("fsharp"),
    [
        new(ModelFamily.Classic, SourceFormatVersions.Create("1", "2", "3", "3.0.0", "3.1.0"), DistributionKind.Library),
        new(ModelFamily.V4, SourceFormatVersions.Create("4", "4.0.0"), DistributionKind.Library)
    ])
    {
        Description = ExtensionDescription.Create("Generates F# source from a Morphir library model.")
    };

    public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options)
    {
        // optimize: exact constant folding. format: Fantomas formatting when the host can run it; false writes the
        // emitter's own text with LF line endings and one final LF. Both are Boolean and true when absent.
        var optimize = true;
        var format = true;
        foreach (var (key, value) in options.Values)
        {
            if (key is not ("optimize" or "format") || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new Outcome<ValidatedBackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                    "invalid-backend-option", $"F# accepts only the Boolean options 'optimize' and 'format'; received '{key}'."));
            if (key == "optimize") optimize = value.GetBoolean();
            else format = value.GetBoolean();
        }
        return new Outcome<ValidatedBackendOptions>.Success(new Options(optimize, format));
    }

    public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
    {
        Outcome<GenerationArtifacts> Fail(GenerationFailureKind kind, string code, string message) =>
            new Outcome<GenerationArtifacts>.Failure(GenerationFailure.One(kind, code, message, request.Model.Original.SourcePath));
        if (ct.IsCancellationRequested)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled."));
        if (!request.Target.Equals(Descriptor.Target) || request.Options is not Options options)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Invocation, "invalid-backend-request", "The request does not contain validated F# options and target."));

        var context = VFileModule.fromPath(request.Model.Original.SourcePath);
        context = FSharpGeneration.WithRequiredSemantics(context, request.Model.Descriptor.RequiredSemantics);
        var tree = Transpiler.generateModelWith(GenerationOptions.Default.WithOptimize(options.Optimize).WithFormat(options.Format), request.Model.Model, context);
        var diagnostics = tree.Messages.Select(message => new GenerationDiagnostic(
            message.RuleId?.Value ?? "generation", message.Message, message.Path?.Value, message.Location?.Value,
            message.Severity.IsError || message.Severity.IsFatal ? "error" : message.Severity.IsWarning ? "warning" : "info")).ToImmutableArray();
        if (ct.IsCancellationRequested)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled."));
        return ValueTask.FromResult<Outcome<GenerationArtifacts>>(VFileTreeModule.hasErrors(tree)
            ? new Outcome<GenerationArtifacts>.Failure(new(GenerationFailureKind.Semantic, diagnostics))
            : new Outcome<GenerationArtifacts>.Success(new(tree, diagnostics) { Formatting = FSharpGeneration.TryGetFormatting(tree, out var formatting) ? formatting.WireValue : null }));
    }
}
