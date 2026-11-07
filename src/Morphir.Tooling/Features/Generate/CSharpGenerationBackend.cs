using Morphir.Extensions;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.FSharp.Collections;
using Morphir.CodeGeneration.CSharp;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Static C# adapter over typed models. The caller owns loading and publication.</summary>
public sealed class CSharpGenerationBackend : IGenerationBackend
{
    private sealed record Options(int LanguageVersion, NamespaceOverride? Namespace) : ValidatedBackendOptions;

    public BackendDescriptor Descriptor { get; } = new(ExtensionId.Create("morphir.csharp"), ExtensionName.Create("C# generator"), TargetId.Create("csharp"),
    [
        new(ModelFamily.Classic, SourceFormatVersions.Create("1", "2", "3", "3.0.0", "3.1.0"), DistributionKind.Library),
        new(ModelFamily.V4, SourceFormatVersions.Create("4", "4.0.0"), DistributionKind.Library)
    ])
    {
        Description = ExtensionDescription.Create("Generates C# source from a Morphir library model.")
    };

    public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options)
    {
        var language = 14;
        NamespaceOverride? naming = null;
        foreach (var (key, value) in options.Values)
        {
            if (key == "namespace" && value.ValueKind == JsonValueKind.String)
            {
                var parsed = NamespaceOverride.TryCreate(value.GetString()!);
                if (parsed.IsError)
                    return new Outcome<ValidatedBackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                        parsed.ErrorValue.Rule, parsed.ErrorValue.Message, location: parsed.ErrorValue.Location));
                naming = parsed.ResultValue;
            }
            else if (key != "language-version" || value.ValueKind != JsonValueKind.Number ||
                !value.TryGetInt32(out language) || language is not (14 or 15))
                return new Outcome<ValidatedBackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                    "invalid-backend-option", $"C# accepts numeric 'language-version' 14 or 15 and a string 'namespace'; received '{key}'."));
        }
        return new Outcome<ValidatedBackendOptions>.Success(new Options(language, naming));
    }

    public ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
    {
        ValueTask<Outcome<GenerationArtifacts>> Fail(GenerationFailureKind kind, string code, string message) =>
            ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Failure(
                GenerationFailure.One(kind, code, message, request.Model.Original.SourcePath)));
        if (ct.IsCancellationRequested)
            return Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled.");
        if (!request.Target.Equals(Descriptor.Target) || request.Options is not Options options)
            return Fail(GenerationFailureKind.Invocation, "invalid-backend-request", "The request requires validated C# options and target.");
        if (!Descriptor.SupportedSemantics.IsSupersetOf(request.Model.Descriptor.RequiredSemantics))
            return Fail(GenerationFailureKind.Semantic, "unsupported-required-semantics", "C# does not support the required model semantics.");

        var coreOptions = new GenerationOptions(options.LanguageVersion);
        var generated = options.Namespace is null
            ? CSharpGenerator.Generate(request.Model.Model, coreOptions, ct)
            : CSharpGenerator.Generate(request.Model.Model, coreOptions, options.Namespace, ct);
        if (generated.Status.IsCancelled || ct.IsCancellationRequested)
            return Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled.");
        var diagnostics = generated.Diagnostics.Select(item => new GenerationDiagnostic(
            item.Rule, item.Message, request.Model.Original.SourcePath, item.Location)).ToImmutableArray();
        if (generated.Status.IsFailure)
            return ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Failure(
                new(GenerationFailureKind.Semantic, diagnostics)));

        var files = generated.Sources.Select(source => Tuple.Create(source.RelativePath, VFileModule.ofText(source.RelativePath, source.Text)));
        var tree = VFileTreeModule.fromFileMap(MapModule.OfSeq(files));
        tree = VFileTreeModule.set(VFileTreeModule.declaredOrder, ListModule.OfSeq(generated.Sources.Select(source => source.RelativePath)), tree);
        return ValueTask.FromResult<Outcome<GenerationArtifacts>>(new Outcome<GenerationArtifacts>.Success(new(tree, diagnostics)));
    }
}
