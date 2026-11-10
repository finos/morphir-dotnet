using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.FSharp.Collections;
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
    private static readonly SemanticVersion Envelope = SemanticVersion.Create("0.1.0-draft.1");

    /// <summary>The tuples the host can send: JSON IR, IR 4.0.0 first, then classic 3.0.0.</summary>
    private static readonly ModelTransportTuple V4Tuple = JsonTuple("4.0.0");
    private static readonly ModelTransportTuple ClassicTuple = JsonTuple("3.0.0");

    /// <summary>The host offer, with bounds that fit in one 64 MiB frame, as the frontend adapter offers.</summary>
    private static readonly ModelTransportSupport Offer = new(
        [V4Tuple, ClassicTuple],
        new ModelTransportLimits(60 * 1024 * 1024, 45 * 1024 * 1024, 4096, 50_000_000),
        []);

    private readonly MepGuestProcess _guest;
    private readonly IReadOnlyList<ModelTransportTuple> _agreed;

    private sealed record PassedOptions(ImmutableDictionary<string, JsonElement> Values) : ValidatedBackendOptions;

    private MepGenerationBackend(MepGuestProcess guest, BackendDescriptor descriptor, string version, IReadOnlyList<ModelTransportTuple> agreed)
    {
        _guest = guest;
        Descriptor = descriptor;
        ExtensionVersion = version;
        _agreed = agreed;
    }

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
            options.Stderr, options.HostName, options.HostVersion);
        var start = await MepGuestProcess.StartCheckedAsync(guestOptions, Offer, ct);
        var guest = start.Match<MepGuestProcess?>(started => started, _ => null);
        if (guest is null)
            return start.Match(_ => throw new InvalidOperationException(), failed => Fail<MepGenerationBackend>(GenerationFailureKind.Operational, failed.Code, failed.Message));

        var session = guest.Session;
        var extension = session.Extension;
        var label = $"{extension.Id} {extension.Version}";

        async Task<Outcome<MepGenerationBackend>> Refuse(string code, string message)
        {
            await ((IAsyncDisposable)guest).DisposeAsync();
            return Fail<MepGenerationBackend>(GenerationFailureKind.Operational, code, message);
        }

        if (!ExtensionId.TryParse(extension.Id, out var id, out _))
            return await Refuse("extension.unavailable", $"Extension {label}: its id is not a valid extension id.");

        var backend = session.Capabilities.BackendOrNull();
        if (backend is null || !backend.Targets.Contains(options.Target.Value, StringComparer.Ordinal))
        {
            var targets = backend is null ? "none" : string.Join(", ", backend.Targets);
            return await Refuse("extension.unavailable", $"Extension {label} does not claim the backend target {options.Target.Value}. It claims: {targets}.");
        }

        var agreed = ModelTransportExtensions.AgreedTuples(session.Capabilities, ModelOperation.GenerateModel);
        if (agreed.Count == 0)
        {
            var described = await guest.DescribeAsync(ct);
            var claimed = described.Match(
                claims => claims.Capabilities.TryGetValue("modelTransport", out var transport)
                    ? Tuples(JsonSerializer.Deserialize(transport, MepJson.Default.ModelTransportSupport)?.Tuples ?? [])
                    : "none",
                failure => $"unknown ({failure.Message})");
            return await Refuse("extension.tuple_unavailable",
                $"Extension {label} is installed but not ready: it agreed no morphir.backend.generateModel tuple with the host. "
                + $"The host offered: {Tuples(Offer.Tuples)}. The extension claims: {claimed}.");
        }

        var name = ExtensionName.TryParse(extension.Name, out var parsedName, out _) ? parsedName : ExtensionName.Create(id.Value);
        var description = extension.DescriptionOrNull() is { } text && ExtensionDescription.TryParse(text, out var parsedDescription, out _)
            ? parsedDescription : null;

        var capabilities = new List<BackendCapability>();
        if (agreed.Contains(V4Tuple))
        {
            // The guest's claims do not name distribution kinds; the guest decides each model.
            foreach (var kind in new[] { DistributionKind.Library, DistributionKind.Specs, DistributionKind.Application })
                capabilities.Add(new(ModelFamily.V4, SourceFormatVersions.Create("4", "4.0.0"), kind));
        }
        if (agreed.Contains(ClassicTuple))
        {
            foreach (var kind in new[] { DistributionKind.Library, DistributionKind.Specs })
                capabilities.Add(new(ModelFamily.Classic, SourceFormatVersions.Create("3", "3.0.0"), kind));
        }

        var descriptor = new BackendDescriptor(id, name, options.Target, capabilities) { Description = description };
        return new Outcome<MepGenerationBackend>.Success(new(guest, descriptor, extension.Version, agreed));
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
        var tuple = TupleFor(model.Descriptor);
        if (tuple is null || !_agreed.Contains(tuple))
            return Fail(GenerationFailureKind.Operational, "extension.tuple_unavailable",
                $"Extension {Identity}: the model ({ModelLabels.Family(model.Model.Family)} {model.Model.SourceVersion.Text}) needs the generateModel tuple "
                + $"{(tuple is null ? "of its IR version, which the host does not send" : tuple.ToString())}. Agreed tuples: {Tuples(_agreed)}.");
        if (model.Representation != ModelRepresentation.Json || model.Original is not RetainedInput.File file)
            return Fail(GenerationFailureKind.Operational, "extension.tuple_unavailable",
                $"Extension {Identity}: the host sends JSON IR, and this model was read as {model.Representation}. Generate from a JSON model.");

        var bytes = file.Entry.Memory;
        if (bytes.Span.StartsWith("﻿"u8)) bytes = bytes[3..];
        var envelope = ModelTransport.CreateDerivedEnvelope(tuple, "sha256:" + Sha256Digest.Compute(bytes.Span).Value, bytes);
        if (envelope.IsError)
            return Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: the model envelope cannot be made: {envelope.ErrorValue}");

        var parameters = JsonSerializer.SerializeToElement(
            new GenerateModelRequest(envelope.ResultValue, tuple, Descriptor.Target.Value, ContextDigest(Descriptor.Target, options.Values), [], options.Values),
            MepJson.Default.GenerateModelRequest);

        var reply = await _guest.SendAsync(Method.GenerateModel, parameters, ct);
        return reply.Match(
            result => FromResult(result, source),
            error => error.Code switch
            {
                (int)ErrorCode.InvalidParams => Fail(GenerationFailureKind.Invocation, "extension.not_admitted", $"Extension {Identity} refused the request: {error.Message}"),
                (int)ErrorCode.RequestCancelled => Fail(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled."),
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

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<Tuple<string, VFile>>();
        foreach (var artifact in result.Artifacts)
        {
            if (UnsafePath(artifact.Path) is { } reason)
                return Fail(GenerationFailureKind.Operational, "extension.unsafe_artifact", $"Extension {Identity} returned the artifact path '{artifact.Path}': {reason}");
            if (!seen.Add(artifact.Path))
                return Fail(GenerationFailureKind.Operational, "extension.unsafe_artifact", $"Extension {Identity} returned the artifact path '{artifact.Path}' twice, compared without case.");

            if (artifact.Binary)
            {
                byte[] content;
                try { content = Convert.FromBase64String(artifact.Content); }
                catch (FormatException) { return Fail(GenerationFailureKind.Operational, "extension.protocol_error", $"Extension {Identity}: the binary artifact '{artifact.Path}' is not base64."); }
                files.Add(Tuple.Create(artifact.Path, VFileModule.ofBytes(artifact.Path, content)));
            }
            else
                files.Add(Tuple.Create(artifact.Path, VFileModule.ofText(artifact.Path, artifact.Content)));
        }

        var tree = VFileTreeModule.fromFileMap(MapModule.OfSeq(files));
        tree = VFileTreeModule.set(VFileTreeModule.declaredOrder, ListModule.OfSeq(files.Select(entry => entry.Item1)), tree);
        return new Outcome<GenerationArtifacts>.Success(new(tree, diagnostics));
    }

    /// <summary>
    /// Why an artifact path is not safe to publish, or null: the path must be relative, with <c>/</c> as the only
    /// separator, no empty, <c>.</c> or <c>..</c> segment, and no <c>\</c>, <c>:</c> or control character. The host
    /// applies this to every path a guest returns and does not rely on the guest's own check.
    /// </summary>
    public static string? UnsafePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "the path is empty.";
        if (path.IndexOfAny(['\\', ':']) >= 0 || path.Any(char.IsControl)) return "the path has a \\, a : or a control character.";
        return path.Split('/').FirstOrDefault(segment => segment is "" or "." or "..") is { } segment
            ? $"the path has the segment '{segment}'; it must be relative, with no empty, . or .. segment."
            : null;
    }

    /// <summary>The agreed tuple that a model needs: IR 4.0.0 for a v4 model, 3.0.0 for a classic version 3 model.</summary>
    private static ModelTransportTuple? TupleFor(ModelDescriptor model) => model.Version.Text switch
    {
        "4" or "4.0.0" when model.Family.IsV4 => V4Tuple,
        "3" or "3.0.0" when model.Family.IsClassic => ClassicTuple,
        _ => null
    };

    private static ModelTransportTuple JsonTuple(string irVersion) =>
        new(ModelOperation.GenerateModel, "morphir-ir", ModelCodec.MorphirJson, Envelope, SemanticVersion.Create(irVersion));

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
            new((int)location.Range.Start.Line, (int)location.Range.Start.Character),
            new((int)location.Range.End.Line, (int)location.Range.End.Character));
        return new(diagnostic.CodeOrNull() ?? "extension.diagnostic", diagnostic.Message, location?.Uri, logical, diagnostic.Severity.WireValue, range);
    }

    private static Outcome<T> Fail<T>(GenerationFailureKind kind, string code, string message) =>
        new Outcome<T>.Failure(GenerationFailure.One(kind, code, message));

    /// <summary>Ends the session with shutdown and exit, or stops the guest's process tree after the deadline.</summary>
    public ValueTask DisposeAsync() => ((IAsyncDisposable)_guest).DisposeAsync();

    /// <summary>The guest's identity.</summary>
    public override string ToString() => $"External backend {Identity}";
}
