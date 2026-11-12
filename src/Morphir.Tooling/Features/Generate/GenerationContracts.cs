using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Morphir.Extensions;
using Morphir.IR;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;

namespace Morphir.Tooling.Features.Generate;

public enum GenerationFailureKind { Semantic, Invocation, Operational, Cancelled }
public enum InputFormat { Auto, Json, Ion }
public enum ModelRepresentation { Json, IonText, IonBinary, IonTree }

/// <summary>A zero-based position: a line, and a character in UTF-16 code units, as in MEP and LSP.</summary>
public sealed record DiagnosticPosition(int Line, int Character);

/// <summary>A source range with an exclusive end.</summary>
public sealed record DiagnosticRange(DiagnosticPosition Start, DiagnosticPosition End);

/// <summary>
/// A generation diagnostic. <paramref name="Range"/> is an optional, additive member: a source range in the
/// document that <paramref name="Path"/> names. Reports leave it out when it is null, so existing reports keep
/// their shape.
/// </summary>
public sealed record GenerationDiagnostic(string Code, string Message, string? Path = null, string? Location = null, string Severity = "error",
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DiagnosticRange? Range = null);
public sealed record GenerationFailure(GenerationFailureKind Kind, ImmutableArray<GenerationDiagnostic> Diagnostics)
{
    public string? Backend { get; init; }

    /// <summary>The transport of an external backend that was selected, for example <c>mep</c>; null for a built-in.</summary>
    public string? BackendTransport { get; init; }

    /// <summary>The version of an external backend that was selected, as the extension gave it; null for a built-in.</summary>
    public string? BackendVersion { get; init; }
    public static GenerationFailure One(GenerationFailureKind kind, string code, string message, string? path = null, string? location = null) =>
        new(kind, [new(code, message, path, location)]);
}

public abstract record Outcome<T>
{
    private Outcome() { }
    public sealed record Success(T Value) : Outcome<T>;
    public sealed record Failure(GenerationFailure Error) : Outcome<T>;
}

/// <summary>
/// The target that an invocation asks for. Text that is not a valid target id names no registered target, so the
/// backend resolver reports it exactly as an unknown target. Only a <see cref="Valid"/> target reaches a backend.
/// </summary>
public abstract record RequestedTarget
{
    private RequestedTarget() { }

    /// <summary>A parsed target id.</summary>
    public sealed record Valid(TargetId Id) : RequestedTarget;

    /// <summary>Invocation text that is not a valid target id. It is kept only for the diagnostic.</summary>
    public sealed record Malformed(string Input) : RequestedTarget;

    /// <summary>Parses invocation text.</summary>
    public static RequestedTarget Parse(string text) =>
        TargetId.TryParse(text, out var id, out _) ? new Valid(id) : new Malformed(text);

    /// <summary>The text that the user gave, for diagnostics.</summary>
    public string Text => this switch
    {
        Valid valid => valid.Id.Value,
        Malformed malformed => malformed.Input,
        _ => throw new UnreachableException()
    };
}
public sealed record InvocationContext(string WorkingDirectory)
{
    /// <summary>The environment of the invocation, or null for the environment of the current process.</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

public sealed record GenerateOverrides(string? Input = null, string? Target = null, string? Output = null,
    string? InputFormat = null, IReadOnlyList<string>? Options = null)
{
    /// <summary><c>--project</c>: the project whose compile record gives the input when <c>--input</c> is absent.</summary>
    public string? Project { get; init; }

    /// <summary><c>--config</c>: an explicitly selected configuration file of that project.</summary>
    public string? Config { get; init; }

    /// <summary><c>--out-dir</c>: the out root that holds the compile record.</summary>
    public string? OutDir { get; init; }

    /// <summary><c>--extension</c>: the backend extension; overrides <c>[codegen.&lt;target&gt;].extension</c>.</summary>
    public string? Extension { get; init; }
}
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
public sealed record GenerationSettings(ModelInput Input, RequestedTarget Target, BackendOptions Options,
    PublicationPolicy Publication, ModelReadLimits Limits)
{
    /// <summary>
    /// The backend extension that <c>--extension</c> or <c>[codegen.&lt;target&gt;].extension</c> names, or null.
    /// When it is set, the run uses only the backend that the extension connector gives; when it is null, the
    /// registered backends of the target are resolved as before.
    /// </summary>
    public Morphir.Tooling.Features.Compile.Setting<string>? Extension { get; init; }
    /// <summary>The selected WASM module from the same effective configuration snapshot.</summary>
    public Morphir.Tooling.Features.Extensions.WasmModuleSource? WasmModule { get; init; }

    /// <summary>
    /// A hold on the input, or null. <c>generate</c> releases it when model loading ends, after it admits the
    /// loaded model through it. The input of a prior compile holds the task's shared lock this way (REC-1).
    /// </summary>
    public ModelInputLease? InputLease { get; init; }
}

/// <summary>
/// A hold on a model input from settings resolution until model loading ends. It can refuse or adjust the loaded
/// model, for example to check the format version that a compile record names.
/// </summary>
public abstract class ModelInputLease : IDisposable
{
    /// <summary>Admits the loaded model. The default admits it unchanged.</summary>
    public virtual Outcome<LoadedModel> Admit(LoadedModel model) => new Outcome<LoadedModel>.Success(model);

    /// <summary>Releases the hold.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the hold.</summary>
    protected abstract void Dispose(bool disposing);
}

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

public sealed record BackendCapability(ModelFamily Family, NonEmptySet<SourceFormatVersion> Versions, DistributionKind Kind)
{
    /// <summary>True when the backend declares the model's family, kind and exact declared version.</summary>
    public bool Supports(ModelDescriptor model) => Family.Equals(model.Family) && Kind.Equals(model.Kind) && Versions.Contains(model.Version);
}

public sealed record BackendDescriptor
{
    public ImmutableHashSet<string> SupportedSemantics { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    /// <summary>The backend's extension id; also the <c>backend</c> value in generation reports.</summary>
    public ExtensionId Id { get; }

    /// <summary>The display name that <c>morphir extension list</c> shows.</summary>
    public ExtensionName Name { get; }

    /// <summary>An optional description for <c>morphir extension list</c>.</summary>
    public ExtensionDescription? Description { get; init; }

    public TargetId Target { get; }

    /// <summary>At least one capability. A backend that supports no model cannot be registered.</summary>
    public ImmutableArray<BackendCapability> Capabilities { get; }

    public BackendDescriptor(ExtensionId id, ExtensionName name, TargetId target, IEnumerable<BackendCapability> capabilities)
    {
        Id = id;
        Name = name;
        Target = target;
        Capabilities = capabilities.ToImmutableArray();
        if (Capabilities.IsEmpty)
            throw new ArgumentException("A backend descriptor needs at least one capability.", nameof(capabilities));
    }
}

/// <summary>Backends receive no output directory, configuration reader or publication service.</summary>
public sealed record BackendGenerationRequest(LoadedModel Model, TargetId Target, ValidatedBackendOptions Options);
public sealed record GenerationArtifacts(VFileTree Tree, ImmutableArray<GenerationDiagnostic> Diagnostics)
{
    /// <summary>
    /// Whether the backend formatted the files: <c>applied</c>, <c>off</c> (its option turned formatting off) or
    /// <c>unavailable</c> (formatting was asked for, but this host cannot run the formatter); null when the backend does
    /// not report it.
    /// </summary>
    public string? Formatting { get; init; }
}

public sealed record GenerationPublication(ImmutableArray<string> Artifacts, ImmutableArray<string> Written,
    ImmutableArray<string> Unchanged, ImmutableArray<string> Removed);

/// <summary>Report labels of model families and kinds. The names are the F# unions' own (WireValue), so C# does not test cases.</summary>
internal static class ModelLabels
{
    public static string Family(ModelFamily family) => family.WireValue;
    public static string Kind(DistributionKind kind) => kind.WireValue;
}

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
    Outcome<IGenerationBackend> Resolve(RequestedTarget target, ModelDescriptor model);
}
