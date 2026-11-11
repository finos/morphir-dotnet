using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Morphir.Compilation.Mep;
using Morphir.Extensions;
using Morphir.IR.Model;
using Morphir.IR.Pipeline;
using Morphir.Mep;

namespace Morphir.Tooling.Features.Generate;

/// <summary>
/// How the host starts an external backend guest: the executable and its arguments (no shell), the target that
/// the guest must claim, the deadline for initialize, each request and the end of the session, where the guest's
/// stderr goes, and the host identity that the session sends.
/// </summary>
public sealed record MepBackendOptions(string Executable, IReadOnlyList<string> Arguments, TargetId Target)
{
    /// <summary>The working directory of the guest, or null for the current directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>The deadline for initialize, for each request and for the end of the session.</summary>
    public TimeSpan Deadline { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Where the guest's stderr goes, line by line.</summary>
    public TextWriter Stderr { get; init; } = TextWriter.Null;

    /// <summary>The host name that the session sends.</summary>
    public string HostName { get; init; } = "morphir-dotnet";

    /// <summary>The host version that the session sends.</summary>
    public string HostVersion { get; init; } = "0.0.0";

    /// <summary>
    /// The model-transport limits that the host offers. The default fits in one 64 MiB frame: 60 MiB of base64,
    /// 45 MiB decoded, depth 4096 and 50,000,000 nodes. The host admits each model against the agreed limits before
    /// it sends it.
    /// </summary>
    public ModelTransportLimits OfferLimits { get; init; } = MepGenerationBackend.DefaultLimits;

    /// <summary>The whole environment of the guest, or null for the host's. When it is given, the guest inherits nothing.</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

/// <summary>
/// An external backend: a MEP 0.2.0-draft.1 guest process that serves <c>morphir.backend.generateModel</c>, as a
/// generation backend. <see cref="ConnectAsync"/> starts the guest and initializes a session; the descriptor then
/// comes from the guest's identity and the agreed tuples, so the backend is named by the guest's own extension id,
/// never as a built-in. The host sends the model's JSON bytes in the agreed tuple of its IR version, checks every
/// artifact path the guest returns, and keeps the guest's artifact order. Dispose the backend to end the session.
/// </summary>
/// <remarks>
/// Nothing registers this backend. A selection surface connects it and resolves over it alone, for example with
/// <c>new RegisteredBackendResolver([backend])</c>, so the default resolution of a target stays the built-ins'.
/// </remarks>
public sealed class MepGenerationBackend : IExternalGenerationBackend
{

    /// <summary>The default host limits, which fit in one 64 MiB frame, as the frontend adapter offers.</summary>
    public static ModelTransportLimits DefaultLimits { get; } = new(60 * 1024 * 1024, 45 * 1024 * 1024, 4096, 50_000_000);

    /// <summary>
    /// The most artifacts that the host accepts in one generateModel result. A result with more is a protocol error,
    /// so a guest cannot make the host build an unbounded file tree; one frame (64 MiB) still bounds the bytes.
    /// </summary>
    public const int MaxArtifacts = 10_000;

    /// <summary>The UTF-8 byte order mark, which is not part of the JSON that the host sends.</summary>
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>The bytes kept free in a frame for the JSON-RPC members around the request's params.</summary>
    private const int FrameMargin = 1024;

    private readonly MepGuestProcess _guest;
    private readonly IReadOnlyList<ModelTransportTuple> _agreed;
    private readonly ModelTransportLimits _limits;

    private sealed record PassedOptions(ImmutableDictionary<string, JsonElement> Values) : ValidatedBackendOptions;

    private MepGenerationBackend(MepGuestProcess guest, BackendDescriptor descriptor, string version, IReadOnlyList<ModelTransportTuple> agreed,
        ModelTransportLimits limits)
    {
        _guest = guest;
        Descriptor = descriptor;
        ExtensionVersion = version;
        _agreed = agreed;
        _limits = limits;
    }

    /// <summary>The host offer: JSON IR, IR 4.0.0 first, then classic 3.0.0, with the given limits.</summary>
    private static ModelTransportSupport OfferWith(ModelTransportLimits limits) => new(MepGenerationTransport.Tuples, limits, []);

    /// <inheritdoc />
    public BackendDescriptor Descriptor { get; }

    /// <summary>The guest's own release version, as it gave it.</summary>
    public string ExtensionVersion { get; }

    /// <summary>The transport: <c>mep</c>, a MEP 0.2.0-draft.1 guest process.</summary>
    public string Transport => "mep";

    /// <summary>The guest's extension id and version, for example <c>org.example.backend 1.0.0</c>.</summary>
    public string Identity => $"{Descriptor.Id.Value} {ExtensionVersion}";

    /// <summary>The agreed generateModel tuples, in host order.</summary>
    public IReadOnlyList<ModelTransportTuple> AgreedTuples => _agreed;

    /// <summary>True when the guest process has exited.</summary>
    public bool HasExited => _guest.HasExited;

    /// <summary>
    /// Starts the guest and initializes a session. Fails when the guest does not start or initialize, when it does
    /// not claim the target, or when it agrees no generateModel tuple with the host: that guest is installed but not
    /// ready, and the failure names the host's offer and the guest's claimed tuples.
    /// </summary>
    public static async Task<Outcome<MepGenerationBackend>> ConnectAsync(MepBackendOptions options, CancellationToken ct)
    {
        var guestOptions = MepGuestOptions.Create(options.Executable, options.Arguments, options.WorkingDirectory, options.Deadline,
            options.Stderr, options.HostName, options.HostVersion, options.Environment);
        var offer = OfferWith(options.OfferLimits);
        var start = await MepGuestProcess.StartCheckedAsync(guestOptions, offer, ct);
        var guest = start.Match<MepGuestProcess?>(started => started, _ => null);
        if (guest is null)
            return start.Match(_ => throw new InvalidOperationException(), failed => Fail<MepGenerationBackend>(GenerationFailureKind.Operational, failed.Code, failed.Message));

        // The connect owns the guest until it succeeds: a refusal, a cancellation or any other exception disposes
        // it, so its process tree never outlives a failed connect.
        var owned = true;
        try
        {
            var outcome = await FromSession(guest, options, offer, ct);
            owned = outcome is not Outcome<MepGenerationBackend>.Success;
            return outcome;
        }
        finally
        {
            if (owned) await ((IAsyncDisposable)guest).DisposeAsync();
        }
    }

    /// <summary>The backend over a started guest, or why it cannot serve the target. It does not dispose the guest.</summary>
    private static async Task<Outcome<MepGenerationBackend>> FromSession(MepGuestProcess guest, MepBackendOptions options,
        ModelTransportSupport offer, CancellationToken ct)
    {
        var session = guest.Session;
        var extension = session.Extension;
        var label = $"{extension.Id} {extension.Version}";

        static Outcome<MepGenerationBackend> Refuse(string code, string message) =>
            Fail<MepGenerationBackend>(GenerationFailureKind.Operational, code, message);

        if (!ExtensionId.TryParse(extension.Id, out var id, out _))
            return Refuse("extension.unavailable", $"Extension {label}: its id is not a valid extension id.");
        // A guest must not pass as a built-in backend, now or when new built-ins ship.
        if (id.ReservedForBuiltIns)
            return Refuse("extension.unavailable",
                $"Extension {label} reported the extension id {id.Value}, which is reserved for built-in frontends and backends (every id that starts with {ReservedExtensionIds.Prefix}). An extension must have its own id.");

        var backend = session.Capabilities.BackendOrNull();
        if (backend is null || !backend.Targets.Contains(options.Target.Value, StringComparer.Ordinal))
        {
            var targets = backend is null ? "none" : string.Join(", ", backend.Targets);
            return Refuse("extension.unavailable", $"Extension {label} does not claim the backend target {options.Target.Value}. It claims: {targets}.");
        }

        var agreed = ModelTransportExtensions.AgreedTuples(session.Capabilities, ModelOperation.GenerateModel);
        if (agreed.Count == 0)
        {
            var described = await guest.DescribeAsync(ct);
            string claimed;
            try
            {
                claimed = described.Match(
                    claims => claims.Capabilities.TryGetValue("modelTransport", out var transport)
                        ? Tuples(JsonSerializer.Deserialize(transport, MepJson.Default.ModelTransportSupport)?.Tuples ?? [])
                        : "none",
                    failure => $"unknown ({failure.Message})");
            }
            catch (JsonException error)
            {
                return Refuse("extension.protocol_error", $"Extension {label}: its describe claims are malformed: the modelTransport claim is not a model transport ({error.Message}).");
            }
            return Refuse("extension.tuple_unavailable",
                $"Extension {label} is installed but not ready: it agreed no morphir.backend.generateModel tuple with the host. "
                + $"The host offered: {Tuples(offer.Tuples)}. The extension claims: {claimed}.");
        }

        var name = ExtensionName.TryParse(extension.Name, out var parsedName, out _) ? parsedName : ExtensionName.Create(id.Value);
        var description = extension.DescriptionOrNull() is { } text && ExtensionDescription.TryParse(text, out var parsedDescription, out _)
            ? parsedDescription : null;

        var capabilities = new List<BackendCapability>();
        if (agreed.Contains(MepGenerationTransport.V4Tuple))
        {
            // The guest's claims do not name distribution kinds; the guest decides each model.
            foreach (var kind in new[] { DistributionKind.Library, DistributionKind.Specs, DistributionKind.Application })
                capabilities.Add(new(ModelFamily.V4, SourceFormatVersions.Create("4", "4.0.0"), kind));
        }
        if (agreed.Contains(MepGenerationTransport.ClassicTuple))
        {
            foreach (var kind in new[] { DistributionKind.Library, DistributionKind.Specs })
                capabilities.Add(new(ModelFamily.Classic, SourceFormatVersions.Create("3", "3.0.0"), kind));
        }

        var descriptor = new BackendDescriptor(id, name, options.Target, capabilities) { Description = description };
        // The host admits each model against the agreed limits, which are at most its own offer.
        var limits = session.Capabilities.ModelTransportOrNull()?.Limits ?? options.OfferLimits;
        return new Outcome<MepGenerationBackend>.Success(new(guest, descriptor, extension.Version, agreed, limits));
    }

    /// <summary>Passes every option to the guest, which validates them; a refusal comes back from generation.</summary>
    public Outcome<ValidatedBackendOptions> ValidateOptions(BackendOptions options) =>
        new Outcome<ValidatedBackendOptions>.Success(new PassedOptions(options.Values));

    /// <summary>
    /// Sends the model in its agreed tuple and maps the guest's answer: artifacts in the guest's order after the
    /// host's own path check, the guest's diagnostics, a failed result as a semantic failure, and a refusal, a
    /// protocol error, an ended connection or a missed deadline as failures that name the guest.
    /// </summary>
    public async ValueTask<Outcome<GenerationArtifacts>> GenerateAsync(BackendGenerationRequest request, CancellationToken ct)
    {
        var source = request.Model.Original.SourcePath;
        Outcome<GenerationArtifacts> Fail(GenerationFailureKind kind, string code, string message) =>
            new Outcome<GenerationArtifacts>.Failure(GenerationFailure.One(kind, code, message, source));

        if (ct.IsCancellationRequested) return Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled.");
        if (!request.Target.Equals(Descriptor.Target) || request.Options is not PassedOptions options)
            return Fail(GenerationFailureKind.Invocation, "invalid-backend-request", $"Extension {Identity}: the request does not contain this backend's options and target.");

        var model = request.Model;
        // The family decision is MepGenerationTransport's (F#, an exhaustive match on the family).
        var label = $"{ModelLabels.Family(model.Model.Family)} {model.Model.SourceVersion.Text}";
        var offered = $"The host offers: {Tuples(MepGenerationTransport.Tuples)}. The session agreed: {Tuples(_agreed)}.";
        if (!MepGenerationTransport.TryTupleFor(model.Descriptor.Family, model.Descriptor.Version.Text, out var tuple))
            return Fail(GenerationFailureKind.Operational, "extension.tuple_unavailable",
                $"Extension {Identity}: the model ({label}) has no generateModel tuple: the host sends JSON IR only for IR 4.0.0 and 3.0.0. {offered}");
        if (!_agreed.Contains(tuple))
            return Fail(GenerationFailureKind.Operational, "extension.tuple_unavailable",
                $"Extension {Identity}: the model ({label}) needs the generateModel tuple {tuple}, which the session did not agree. {offered}");
        if (model.Representation != ModelRepresentation.Json || model.Original is not RetainedInput.File file)
            return Fail(GenerationFailureKind.Operational, "extension.tuple_unavailable",
                $"Extension {Identity}: the host sends JSON IR, and this model was read as {model.Representation}. Generate from a JSON model.");

        var bytes = file.Entry.Memory;
        if (bytes.Span.StartsWith(Utf8Bom)) bytes = bytes[Utf8Bom.Length..];
        // The envelope, then its admission against the agreed encoded, decoded, depth and node limits: the host sends
        // only what the session admits. Both are F# results, read through their Match extension.
        MorphirModel? envelope = null;
        var refusal = ModelTransport.CreateDerivedEnvelope(tuple, "sha256:" + Sha256Digest.Compute(bytes.Span).Value, bytes).Match<MorphirModel, EnvelopeFailure, Outcome<GenerationArtifacts>?>(
            made => ModelTransport.Admit(tuple, _limits, made).Match<AdmittedModel, AdmissionFailure, Outcome<GenerationArtifacts>?>(
                _ =>
                {
                    envelope = made;
                    return null;
                },
                refused => Fail(GenerationFailureKind.Invocation, "extension.not_admitted",
                    $"Extension {Identity}: the model is not sent, because it is over the limits that the session agreed ({refused.Code}): {refused}")),
            failure => Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: the model envelope cannot be made: {failure}"));
        if (refusal is not null || envelope is null)
            return refusal ?? Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: the model envelope cannot be made.");

        var serialized = JsonSerializer.SerializeToUtf8Bytes(
            new GenerateModelRequest(envelope, tuple, Descriptor.Target.Value, ContextDigest(Descriptor.Target, options.Values), [], options.Values),
            MepJson.Default.GenerateModelRequest);
        if (serialized.Length > ContentLengthFraming.MaxBodyBytes - FrameMargin)
            return Fail(GenerationFailureKind.Invocation, "extension.not_admitted",
                $"Extension {Identity}: the generateModel request is {serialized.Length} bytes, over the {ContentLengthFraming.MaxBodyBytes}-byte frame limit.");
        using var document = JsonDocument.Parse(serialized);
        var parameters = document.RootElement;

        var reply = await _guest.SendAsync(Method.GenerateModel, parameters, ct);
        return reply.Match(
            result => FromResult(result, source),
            error => error.Code switch
            {
                (int)ErrorCode.InvalidParams => Fail(GenerationFailureKind.Invocation, "extension.not_admitted", $"Extension {Identity} refused the request: {error.Message}"),
                // -32800 is a cancellation only when the host asked for one; otherwise the guest broke the protocol.
                (int)ErrorCode.RequestCancelled when ct.IsCancellationRequested => Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled."),
                (int)ErrorCode.RequestCancelled => Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity} answered -32800 (request cancelled), but the host did not cancel the request."),
                _ => Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: generateModel failed with {error.Code}: {error.Message}")
            },
            failed => Fail(GenerationFailureKind.Operational, failed.Code, failed.Message));
    }

    /// <summary>Maps a generate result: a failure with its diagnostics, or the artifacts after the path check.</summary>
    private Outcome<GenerationArtifacts> FromResult(JsonElement element, string source)
    {
        Outcome<GenerationArtifacts> Fail(GenerationFailureKind kind, string code, string message) =>
            new Outcome<GenerationArtifacts>.Failure(GenerationFailure.One(kind, code, message, source));

        GenerateResult? result;
        try { result = JsonSerializer.Deserialize(element, MepJson.Default.GenerateResult); }
        catch (JsonException error) { return Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: the generateModel result is not a generate result: {error.Message}"); }
        if (result is null) return Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: the generateModel result is null.");

        var diagnostics = result.Diagnostics.Select(ToDiagnostic).ToImmutableArray();
        if (!result.Success)
        {
            if (result.Artifacts.Count > 0)
                return Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: a failed generateModel result has artifacts.");
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == "error"))
                diagnostics = diagnostics.Add(new("extension.protocol_error", $"Extension {Identity} reported a failed generation without an error diagnostic.", source));
            return new Outcome<GenerationArtifacts>.Failure(new(GenerationFailureKind.Semantic, diagnostics));
        }

        if (result.Artifacts.Count > MaxArtifacts)
            return Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity} returned {result.Artifacts.Count} artifacts, more than the {MaxArtifacts} that the host accepts in one result.");

        // The whole set is checked before any file is made, so a refusal publishes nothing.
        if (UnsafePaths(result.Artifacts.Select(artifact => artifact.Path)) is { } unsafePath)
            return Fail(GenerationFailureKind.Operational, "extension.unsafe_artifact", $"Extension {Identity} returned the artifact path '{unsafePath.Path}': {unsafePath.Reason}");

        var files = new List<KeyValuePair<string, VFile>>();
        foreach (var artifact in result.Artifacts)
        {
            if (artifact.Binary)
            {
                byte[] content;
                try { content = Convert.FromBase64String(artifact.Content); }
                catch (FormatException) { return Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: the binary artifact '{artifact.Path}' is not base64."); }
                files.Add(KeyValuePair.Create(artifact.Path, VFileModule.ofBytes(artifact.Path, content)));
            }
            else
                files.Add(KeyValuePair.Create(artifact.Path, VFileModule.ofText(artifact.Path, artifact.Content)));
        }

        // The guest's order is the declared order.
        var tree = VFileTree.OfFiles(files);
        return new Outcome<GenerationArtifacts>.Success(new(tree, diagnostics));
    }

    /// <summary>
    /// Why an artifact path is not safe to publish, or null: the path must be relative, with <c>/</c> as the only
    /// separator, no empty, <c>.</c> or <c>..</c> segment, no <c>\</c>, <c>:</c> or control character, no segment
    /// that ends with a period or a space, and no reserved Windows device name as a segment, so that it names the
    /// same file on every platform. The host applies this to every path a guest returns and does not rely on the
    /// guest's own check.
    /// </summary>
    public static string? UnsafePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "the path is empty.";
        if (path.IndexOfAny(['\\', ':']) >= 0 || path.Any(char.IsControl)) return "the path has a \\, a : or a control character.";
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or "..")
                return $"the path has the segment '{segment}'; it must be relative, with no empty, . or .. segment.";
            // Windows drops a trailing period or space, so A.fs. and A.fs would be one file.
            if (segment.EndsWith('.') || segment.EndsWith(' '))
                return $"the segment '{segment}' ends with a period or a space, which Windows removes.";
            if (IsReservedDeviceName(segment))
                return $"the segment '{segment}' is a reserved Windows device name.";
        }
        return null;
    }

    /// <summary>
    /// The Windows device names that a file or directory cannot have, with or without an extension (NUL.txt is NUL):
    /// CON, PRN, AUX, NUL, COM1 to COM9, LPT1 to LPT9, and COM and LPT with the superscript digits ¹, ² and ³, as
    /// "Naming Files, Paths, and Namespaces" (Microsoft Learn) lists them.
    /// </summary>
    private static readonly FrozenSet<string> ReservedDeviceNames = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsReservedDeviceName(string segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');
        return ReservedDeviceNames.Contains(stem);
    }

    /// <summary>
    /// The first path of a set that is not safe to publish, with the reason, or null. Each path must pass
    /// <see cref="UnsafePath"/>. The set must not name one file twice, compared without case, and no path may be both
    /// a file and the directory of another path (<c>a</c> and <c>a/b.fs</c>), which would fail part way through
    /// publication.
    /// </summary>
    public static (string Path, string Reason)? UnsafePaths(IEnumerable<string?> paths)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (UnsafePath(path) is { } reason) return (path ?? "", reason);
            if (!files.Add(path!)) return (path!, "the path is there twice, compared without case.");
            var segments = path!.Split('/');
            for (var count = 1; count < segments.Length; count++)
                directories.Add(string.Join('/', segments[..count]));
        }
        var conflict = files.FirstOrDefault(directories.Contains);
        return conflict is null ? null : (conflict, "the path is both a file and a directory of another path.");
    }

    private static string Tuples(IEnumerable<ModelTransportTuple> tuples) =>
        tuples.Any() ? string.Join("; ", tuples.Select(tuple => tuple.ToString())) : "none";

    /// <summary>The SHA-256 of the RFC 8785 canonical JSON of the target and the options: the generation context.</summary>
    private static Sha256Digest ContextDigest(TargetId target, ImmutableDictionary<string, JsonElement> options)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("target", target.Value);
            writer.WritePropertyName("options");
            writer.WriteStartObject();
            foreach (var (key, value) in options.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return Sha256Digest.Compute(Encoding.UTF8.GetBytes(CompileModel.CanonicalJson(document.RootElement)));
    }

    /// <summary>A MEP diagnostic of the guest as a generation diagnostic.</summary>
    private static GenerationDiagnostic ToDiagnostic(Morphir.Mep.Diagnostic diagnostic)
    {
        var location = diagnostic.LocationOrNull();
        var logical = diagnostic.DataOrNull() is { ValueKind: JsonValueKind.Object } data
            && data.TryGetProperty("location", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        var range = location is null ? null : new DiagnosticRange(
            new(Position(location.Range.Start.Line), Position(location.Range.Start.Character)),
            new(Position(location.Range.End.Line), Position(location.Range.End.Character)));
        return new(diagnostic.CodeOrNull() ?? "extension.diagnostic", diagnostic.Message, location?.Uri, logical, diagnostic.Severity.WireValue, range);
    }

    /// <summary>A MEP position (an unsigned number) as a generation position: above Int32.MaxValue it is Int32.MaxValue.</summary>
    private static int Position(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

    private static Outcome<T> Fail<T>(GenerationFailureKind kind, string code, string message) =>
        new Outcome<T>.Failure(GenerationFailure.One(kind, code, message));

    /// <summary>Ends the session with shutdown and exit, or stops the guest's process tree after the deadline.</summary>
    public ValueTask DisposeAsync() => ((IAsyncDisposable)_guest).DisposeAsync();

    /// <summary>The guest's identity.</summary>
    public override string ToString() => $"External backend {Identity}";
}
