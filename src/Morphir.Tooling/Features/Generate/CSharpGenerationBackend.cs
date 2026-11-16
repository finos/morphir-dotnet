using Morphir.Extensions;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.FSharp.Collections;
using Morphir.Lang.CSharp.Backend;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Static C# adapter over typed models. The caller owns loading and publication.</summary>
public sealed class CSharpGenerationBackend : IGenerationBackend
{
    private sealed record Options(GenerationOptions Core, NamespaceOverride? Namespace) : ValidatedBackendOptions;

    // The backend reads the current IR only. The host upgrades a classic model before the passes and the backend run.
    public BackendDescriptor Descriptor { get; } = new(ExtensionId.Create("morphir.csharp"), ExtensionName.Create("C# generator"), TargetId.Create("csharp"),
    [
        new(ModelFamily.V4, SourceFormatVersions.Create("4", "4.0.0"), DistributionKind.Library)
    ])
    {
        Description = ExtensionDescription.Create("Generates C# source from a Morphir library model.")
    };

    /// <summary>The only language version that the backend writes.</summary>
    private const int SupportedLanguage = 15;

    public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options)
    {
        var language = SupportedLanguage;
        NamespaceOverride? naming = null;
        var handling = new List<KeyValuePair<string, string>>();
        Outcome<ValidatedBackendOptions> Invalid(string key) => new Outcome<ValidatedBackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
            "invalid-backend-option", $"C# accepts numeric 'language-version' {SupportedLanguage}, a string 'namespace', and the string handling options 'profile' (imperative or functional), 'traversal' (loop or functional), 'tail-recursion' (loop or local-function) and 'float-equality' (clr or ieee); received '{key}'."));

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
            else if (HandlingKeys.Contains(key) && value.ValueKind == JsonValueKind.String)
                handling.Add(new(key, value.GetString()!));
            else if (key == "language-version" && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out language))
            {
                if (language != SupportedLanguage)
                    return new Outcome<ValidatedBackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                        "unsupported-language", $"C# {language} is not supported. Use language-version {SupportedLanguage}, the only version that the backend writes."));
            }
            else
                return Invalid(key);
        }

        var created = Morphir.Lang.CSharp.Emit.CSharpBackend.TryCreateOptions(language, handling, []);
        if (created.IsError)
            return new Outcome<ValidatedBackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                created.ErrorValue.Rule, created.ErrorValue.Message, location: created.ErrorValue.Location));
        return new Outcome<ValidatedBackendOptions>.Success(new Options(created.ResultValue, naming));
    }

    /// <summary>The handling options of the C# backend (OPT-1), as keys of <c>--option</c>.</summary>
    private static readonly string[] HandlingKeys = ["profile", "traversal", "tail-recursion", "float-equality"];

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

        var coreOptions = options.Core;
        var generated = options.Namespace is null
            ? Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(request.Model.Model, coreOptions, ct)
            : Morphir.Lang.CSharp.Emit.CSharpBackend.Generate(request.Model.Model, coreOptions, options.Namespace, ct);
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
