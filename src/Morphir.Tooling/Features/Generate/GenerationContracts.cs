using System.Collections.Immutable;
using System.Text.Json;
using Morphir.IR;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;

namespace Morphir.Tooling.Features.Generate;

public enum GenerationFailureKind { Semantic, Invocation, Operational, Cancelled }
public enum InputFormat { Auto, Json, Ion }
public enum ModelRepresentation { Json, IonText, IonBinary, IonTree }

public sealed record GenerationDiagnostic(string Code, string Message, string? Path = null, string? Location = null, string Severity = "error");
public sealed record GenerationFailure(GenerationFailureKind Kind, ImmutableArray<GenerationDiagnostic> Diagnostics)
{
    public static GenerationFailure One(GenerationFailureKind kind, string code, string message, string? path = null, string? location = null) =>
        new(kind, [new(code, message, path, location)]);
}

public abstract record Outcome<T>
{
    private Outcome() { }
    public sealed record Success(T Value) : Outcome<T>;
    public sealed record Failure(GenerationFailure Error) : Outcome<T>;
}

public readonly record struct TargetId(string Value);
public sealed record InvocationContext(string WorkingDirectory);
public sealed record GenerateOverrides(string? Input = null, string? Target = null, string? Output = null,
    string? InputFormat = null, IReadOnlyList<string>? Options = null);
public sealed record ModelInput(string Path, InputFormat Format = InputFormat.Auto);

public sealed record ModelReadLimits(long MaxBytes = 64 * 1024 * 1024, int MaxDepth = 128,
    int MaxNodes = 1_000_000, int MaxEntries = 10_000, int MaxPathLength = 4000)
{
    public static ModelReadLimits Default { get; } = new();
}

public static class GenerationDefaults
{
    public const string OwnershipManifest = ".morphir-generated.json";
}

public sealed record PublicationPolicy(string OutputDirectory, string ManifestName = GenerationDefaults.OwnershipManifest);
public sealed record GenerationSettings(ModelInput Input, TargetId Target, BackendOptions Options,
    PublicationPolicy Publication, ModelReadLimits Limits);

/// <summary>Owns JSON values independently of their source document and the caller's dictionary.</summary>
public sealed class BackendOptions
{
    public static BackendOptions Empty { get; } = new([]);
    public ImmutableDictionary<string, JsonElement> Values { get; }

    public BackendOptions(IEnumerable<KeyValuePair<string, JsonElement>> values) =>
        Values = values.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
}

/// <summary>A backend defines its own immutable validated option type.</summary>
public abstract record ValidatedBackendOptions;

/// <summary>File bytes are privately owned. Returned copies cannot change the captured input.</summary>
public sealed class RetainedEntry
{
    private readonly byte[] _bytes;
    public string RelativePath { get; }
    public long Length => _bytes.LongLength;
    internal ReadOnlyMemory<byte> Memory => _bytes;

    public RetainedEntry(string relativePath, ReadOnlySpan<byte> bytes)
    {
        RelativePath = relativePath;
        _bytes = bytes.ToArray();
    }

    public byte[] CopyBytes() => (byte[])_bytes.Clone();
    public Stream OpenRead() => new MemoryStream(_bytes, writable: false);
}

public abstract record RetainedInput(string SourcePath)
{
    public sealed record File(string Path, RetainedEntry Entry) : RetainedInput(Path);
    public sealed record Tree : RetainedInput
    {
        public ImmutableArray<RetainedEntry> Entries { get; }
        public Tree(string path, IEnumerable<RetainedEntry> entries) : base(path) => Entries = entries.ToImmutableArray();
    }
}

public sealed record ModelDescriptor(SourceFormatVersion Version, ModelFamily Family, DistributionKind Kind)
{
    public ImmutableArray<string> RequiredSemantics { get; init; } = [];
    public static ModelDescriptor From(VersionedModel model) => new(model.SourceVersion, model.Family, model.Kind);
}

public sealed record LoadedModel(VersionedModel Model, ModelDescriptor Descriptor, ModelRepresentation Representation, RetainedInput Original);

public sealed record BackendCapability
{
    public ModelFamily Family { get; }
    public ImmutableHashSet<string> Versions { get; }
    public DistributionKind Kind { get; }
    public BackendCapability(ModelFamily family, IEnumerable<string> versions, DistributionKind kind)
    {
        Family = family;
        Versions = versions.ToImmutableHashSet(StringComparer.Ordinal);
        Kind = kind;
    }
    public bool Supports(ModelDescriptor model) => Family.Equals(model.Family) && Kind.Equals(model.Kind) && Versions.Contains(model.Version.Text);
}

public sealed record BackendDescriptor
{
    public ImmutableHashSet<string> SupportedSemantics { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public string Identity { get; }
    public TargetId Target { get; }
    public ImmutableArray<BackendCapability> Capabilities { get; }
    public BackendDescriptor(string identity, TargetId target, IEnumerable<BackendCapability> capabilities)
    {
        Identity = identity;
        Target = target;
        Capabilities = capabilities.ToImmutableArray();
    }
}

/// <summary>Backends receive no output directory, configuration reader or publication service.</summary>
public sealed record BackendGenerationRequest(LoadedModel Model, TargetId Target, ValidatedBackendOptions Options);
public sealed record GenerationArtifacts(VFileTree Tree, ImmutableArray<GenerationDiagnostic> Diagnostics);

public sealed record GenerationPublication(ImmutableArray<string> Artifacts, ImmutableArray<string> Written,
    ImmutableArray<string> Unchanged, ImmutableArray<string> Removed);

public interface IGenerationPublisher
{
    ValueTask<Outcome<GenerationPublication>> PublishAsync(GenerationArtifacts artifacts, ModelInput input, PublicationPolicy policy, CancellationToken ct);
}

public interface IGenerationSettingsResolver
{
    ValueTask<Outcome<GenerationSettings>> ResolveAsync(GenerateOverrides overrides, InvocationContext context, CancellationToken ct);
}

public interface IModelLoader
{
    ValueTask<Outcome<LoadedModel>> LoadAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct);
}

public interface IGenerationBackend
{
    BackendDescriptor Descriptor { get; }
    Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options);
    ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct);
}

public interface IGenerationBackendResolver
{
    Outcome<IGenerationBackend> Resolve(TargetId target, ModelDescriptor model);
}
