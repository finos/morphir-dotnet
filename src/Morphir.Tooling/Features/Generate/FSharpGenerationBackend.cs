using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.FSharp.Collections;
using Morphir.CodeGeneration.FSharp;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Static F# provider over semantic models. Publication belongs to its caller.</summary>
public sealed class FSharpGenerationBackend : IGenerationBackend
{
    private sealed record Options(bool Optimize) : ValidatedBackendOptions;

    public BackendDescriptor Descriptor { get; } = new("morphir.fsharp", new("fsharp"),
    [
        new(ModelFamily.Classic, ["1", "2", "3", "3.0.0", "3.1.0"], DistributionKind.Library),
        new(ModelFamily.V4, ["4", "4.0.0"], DistributionKind.Library)
    ]);

    public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options)
    {
        var optimize = true;
        foreach (var (key, value) in options.Values)
        {
            if (key != "optimize" || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new Outcome<ValidatedBackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                    "invalid-backend-option", $"F# accepts only the Boolean option 'optimize'; received '{key}'."));
            optimize = value.GetBoolean();
        }
        return new Outcome<ValidatedBackendOptions>.Success(new Options(optimize));
    }

    public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
    {
        Outcome<GenerationArtifacts> Fail(GenerationFailureKind kind, string code, string message) =>
            new Outcome<GenerationArtifacts>.Failure(GenerationFailure.One(kind, code, message, request.Model.Original.SourcePath));
        if (ct.IsCancellationRequested)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled."));
        if (request.Target != Descriptor.Target || request.Options is not Options options)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Invocation, "invalid-backend-request", "The request does not contain validated F# options and target."));

        var context = VFileModule.fromPath(request.Model.Original.SourcePath);
        context = VFileModule.set(Transpiler.requiredSemantics, SetModule.OfSeq(request.Model.Descriptor.RequiredSemantics), context);
        var tree = Transpiler.generateModelWith(new GenerationOptions(options.Optimize), request.Model.Model, context);
        var diagnostics = tree.Messages.Select(message => new GenerationDiagnostic(
            message.RuleId?.Value ?? "generation", message.Message, message.Path?.Value, message.Location?.Value,
            message.Severity.IsError || message.Severity.IsFatal ? "error" : message.Severity.IsWarning ? "warning" : "info")).ToImmutableArray();
        if (ct.IsCancellationRequested)
            return ValueTask.FromResult(Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled."));
        return ValueTask.FromResult<Outcome<GenerationArtifacts>>(VFileTreeModule.hasErrors(tree)
            ? new Outcome<GenerationArtifacts>.Failure(new(GenerationFailureKind.Semantic, diagnostics))
            : new Outcome<GenerationArtifacts>.Success(new(tree, diagnostics)));
    }
}
