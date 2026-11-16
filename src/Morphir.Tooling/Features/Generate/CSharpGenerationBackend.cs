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

    /// <summary>The only language version that the backend writes: the core decides it.</summary>
    private static int SupportedLanguage => Morphir.Lang.CSharp.Emit.CSharpBackend.LanguageVersion;

    public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options)
    {
        // The keys of [codegen.csharp] come first, so that a command-line language version or namespace replaces them. The
        // handling options of the two stay apart, because the carrier in the model is between them (OPT-1). The core
        // decides on the handling options and the language version.
        var language = SupportedLanguage;
        NamespaceOverride? naming = null;
        var commandLine = new List<KeyValuePair<string, string>>();
        var configured = new List<KeyValuePair<string, string>>();
        Outcome<ValidatedBackendOptions> Fail(string code, string message, string? location = null) =>
            new Outcome<ValidatedBackendOptions>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation, code, message, location: location));
        Outcome<ValidatedBackendOptions> Invalid(string key, bool fromConfiguration) => Fail("invalid-backend-option",
            $"C# accepts numeric 'language-version' {SupportedLanguage}, a string 'namespace', and the string handling options {Morphir.Lang.CSharp.Emit.CSharpBackend.HandlingOptionText}; received '{key}'{(fromConfiguration ? " from [codegen.csharp]" : "")}.");

        var entries = options.Configured.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value, FromConfiguration: true))
            .Concat(options.Values.Select(pair => (pair.Key, pair.Value, FromConfiguration: false)));
        foreach (var (key, value, fromConfiguration) in entries)
        {
            if (key == "namespace" && value.ValueKind == JsonValueKind.String)
            {
                var parsed = NamespaceOverride.TryCreate(value.GetString()!);
                if (parsed.IsError)
                    return Fail(parsed.ErrorValue.Rule, parsed.ErrorValue.Message, parsed.ErrorValue.Location);
                naming = parsed.ResultValue;
            }
            else if (Morphir.Lang.CSharp.Emit.CSharpBackend.HandlingOptionKeys.Contains(key) && value.ValueKind == JsonValueKind.String)
                (fromConfiguration ? configured : commandLine).Add(new(key, value.GetString()!));
            else if (key == "language-version" && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var version))
                language = version;
            else
                return Invalid(key, fromConfiguration);
        }

        return Morphir.Lang.CSharp.Emit.CSharpBackend.TryCreateOptions(language, commandLine, configured, out var created, out var failure)
            ? new Outcome<ValidatedBackendOptions>.Success(new Options(created, naming))
            : Fail(failure.Rule, failure.Message, failure.Location);
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
