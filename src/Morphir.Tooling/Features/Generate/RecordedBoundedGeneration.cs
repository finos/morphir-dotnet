// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.FSharp.Core;
using Morphir.Compilation;
using Morphir.Compilation.Mep;
using Morphir.Extensions;
using Morphir.Extensions.Wasm;
using Morphir.Files;
using Morphir.IR.Model;
using Morphir.IR.Trees;
using Morphir.Mep;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Runs a process or WASM generation guest over the document tree named by a locked compile result.</summary>
internal static class RecordedBoundedGeneration
{
    public static async Task<Outcome<GenerationSuccess>> RunAsync(CompileTaskOutput producer, BackendExtensionRequest request,
        GenerationSettings settings, MepBackendExtensionConnector connector, IGenerationPublisher publisher,
        IEnumerable<IGenerationBackend> registered, CancellationToken ct)
    {
        var target = request.Target;
        (string Id, string Version, string Transport)? backendIdentity = null;

        try
        {
            var taskRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(producer.Location.Root.Path));
            var outputPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.Publication.OutputDirectory));
            if (GenerationPublisher.ContainsPath(taskRoot, outputPath) || GenerationPublisher.ContainsPath(outputPath, taskRoot))
                return new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                    "extension.task_output_overlap", "The publication directory must be separate from the workspace task output root.", outputPath));

            using var files = new RecordedTreeFiles(producer.IrPath);
            // This identity belongs to the held lease. It is never reused for another snapshot.
            var revision = Guid.NewGuid().ToString("N");
            var limits = OfferLimits(settings.Limits, request.WasmModule is not null);
            var opened = DocumentTreeStore.OpenPlainSnapshot(
                    new ReadOptions(settings.Limits.MaxEntries, limits.MaxDecodedBytesPerExchange), () => revision, files)
                .Match<BoundedTreeSnapshot, IReadOnlyList<StoreError>, Outcome<BoundedTreeSnapshot>>(
                    value => new Outcome<BoundedTreeSnapshot>.Success(value),
                    problems => new Outcome<BoundedTreeSnapshot>.Failure(new(GenerationFailureKind.Operational,
                        problems.Select(problem => new GenerationDiagnostic(problem.Code, problem.Message, problem.FileOrNull)).ToImmutableArray())));
            if (opened is Outcome<BoundedTreeSnapshot>.Failure refusedSnapshot)
                return new Outcome<GenerationSuccess>.Failure(refusedSnapshot.Error);
            var snapshot = ((Outcome<BoundedTreeSnapshot>.Success)opened).Value;
            if (!FormatVersion.SameVersion(producer.Ir.Version.Version, SourceFormatVersion.Create(snapshot.IrVersion).Version))
                return Refuse("compile.record_version_mismatch",
                    $"The compile record at {producer.Location.Record} names IR {producer.Ir.Spelling}, but {producer.IrPath} declares {snapshot.IrVersion}.");
            var json = snapshot.Syntax.Equals(BoundedTreeSyntax.Json);
            if (producer.Ir.Format != (json ? IrStorageFormat.Json : IrStorageFormat.Yaml))
                return Refuse("compile.record_format_mismatch",
                    $"The manifest at {producer.IrPath} does not match its recorded {IrStorageNames.Name(producer.Ir.Format)} format.");

            var tuple = new ModelAccessTuple(ModelAccess.protocolVersion, ModelAccess.accessVersion, ModelOperation.GenerateModel,
                SemanticVersion.Create(snapshot.IrVersion), "morphir-ir",
                json ? ModelAccessCodec.MorphirJson : ModelAccessCodec.MorphirYaml, "document-tree", ModelAccessEncoding.Plain);
            var connected = await connector.ConnectModelAccessAsync(request, new ModelAccessSupport([tuple], limits), ct);
            if (connected is Outcome<IModelAccessGuest>.Failure refusedGuest)
                return new Outcome<GenerationSuccess>.Failure(refusedGuest.Error);
            var guest = ((Outcome<IModelAccessGuest>.Success)connected).Value;
            var negotiated = guest.Session;
            var label = negotiated.Extension;

            BoundedArtifactStage? stage = null;
            try
            {
                Outcome<StagedGenerationOutput> result;
                await using ((IAsyncDisposable)guest)
                {
                    if (!ExtensionId.TryParse(label.Id, out var id, out _) || id.ReservedForBuiltIns
                        || registered.Any(backend => backend.Descriptor.Id.Equals(id)))
                        return Refuse("extension.unavailable", $"Extension {label.Id} cannot identify itself as a built-in or invalid backend.");
                    backendIdentity = (label.Id, label.Version, guest.Transport);
                    if (negotiated.Capabilities.BackendOrNull() is not { } backend
                        || !backend.Targets.Contains(target.Value, StringComparer.Ordinal))
                        return Refuse("extension.unavailable", $"Extension {label.Id} does not claim target {target.Value}.");
                    if (!negotiated.ProtocolVersion.Equals(tuple.ProtocolVersion)
                        || negotiated.Capabilities.ModelAccessOrNull() is not { } access
                        || !access.Tuples.Contains(tuple))
                        return Refuse("extension.tuple_unavailable", $"Extension {label.Id} did not agree to bounded generation with {tuple}.");

                    if (!TaskId.TryParse($"generate/{target.Value}", out var task, out var invalid))
                        return Refuse("extension.bounded_output", invalid ?? "The generation task name is invalid.");
                    var location = producer.Location.Root.Task(producer.Location.Member, task);
                    if (GenerationPublisher.LinkedPath(location.Dest) is not null)
                        return Refuse("unsafe-publication-path", $"The task directory {location.Dest} must not be a link.");
                    Directory.CreateDirectory(location.Dest);
                    // Reserve a bounded quarter of the negotiated cache for the active package's
                    // paths before any discovery. The stage can use only the remaining bytes.
                    var discoveryReservation = access.Limits.MaxHostCacheBytes / 4L;
                    var indexed = DocumentTreeStore.OpenPlainSnapshotWithDiscoveryCache(
                            new ReadOptions(settings.Limits.MaxEntries, access.Limits.MaxDecodedBytesPerExchange),
                            discoveryReservation, () => revision, files)
                        .Match<BoundedTreeSnapshot, IReadOnlyList<StoreError>, Outcome<BoundedTreeSnapshot>>(
                            value => new Outcome<BoundedTreeSnapshot>.Success(value),
                            problems => new Outcome<BoundedTreeSnapshot>.Failure(new(GenerationFailureKind.Operational,
                                problems.Select(problem => new GenerationDiagnostic(
                                    problem.Code, problem.Message, problem.FileOrNull)).ToImmutableArray())));
                    if (indexed is Outcome<BoundedTreeSnapshot>.Failure refusedIndex)
                        return Failed(refusedIndex.Error);
                    snapshot = ((Outcome<BoundedTreeSnapshot>.Success)indexed).Value;
                    stage = new BoundedArtifactStage(location.Dest, access.Limits,
                        new GenerationSessionDiskBudget(access.Limits.MaxSessionDiskBytes), discoveryReservation);
                    var start = Start(snapshot, tuple, target, settings.Options, access.Limits);
                    using var sessionDeadline = new CancellationTokenSource(access.Limits.MaxSessionMilliseconds);
                    result = await BoundedGenerationSession.RunAsync(guest.HostSession, snapshot, start, stage,
                        guest.TerminateModelAccessAsync, ct, sessionDeadline.Token);
                    using var closeDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct, sessionDeadline.Token);
                    closeDeadline.CancelAfter(access.Limits.MaxCallMilliseconds);
                    FSharpResult<Unit, MepGuestFailure> closed;
                    try
                    {
                        closed = await guest.CloseModelAccessAsync(closeDeadline.Token);
                    }
                    catch (Exception cleanup) when (sessionDeadline.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        // Expiry remains the primary cause. Retain the independent cleanup failure as evidence.
                        return Failed(new GenerationFailure(GenerationFailureKind.Operational,
                        [new("extension.bounded_deadline", "The generation deadline expired during guest shutdown.", producer.IrPath),
                         new("extension.bounded_io", cleanup.Message, producer.IrPath)]));
                    }
                    ct.ThrowIfCancellationRequested();
                    if (result is Outcome<StagedGenerationOutput>.Success)
                    {
                        if (sessionDeadline.IsCancellationRequested)
                            return Refuse("extension.bounded_deadline", "The generation deadline expired during guest shutdown.");
                        var closeFailure = closed.Match<Unit, MepGuestFailure, GenerationFailure?>(
                            _ => null, error => Refusal(error.Code, error.Message));
                        if (closeFailure is not null) return Failed(closeFailure);
                    }
                }

                if (result is Outcome<StagedGenerationOutput>.Failure failure)
                    return Failed(failure.Error);
                ct.ThrowIfCancellationRequested();
                var staged = ((Outcome<StagedGenerationOutput>.Success)result).Value;
                // The publisher streams these files from disk. The VFile tree contains metadata and source paths.
                var tree = VFileTree.OfFiles(staged.Artifacts.Select(file => KeyValuePair.Create(file.Path,
                    VFileModule.ofDiskFile(file.Path, RecordPath.Parse(file.Path).Under(staged.Directory)))));
                var published = await publisher.PublishAsync(new GenerationArtifacts(tree, staged.Diagnostics), settings.Input, settings.Publication, ct);
                if (published is Outcome<GenerationPublication>.Failure refused)
                    return Failed(refused.Error);
                var receipt = ((Outcome<GenerationPublication>.Success)published).Value;
                var family = snapshot.IrVersion == "4.0.0" ? ModelFamily.V4 : ModelFamily.Classic;
                return new Outcome<GenerationSuccess>.Success(new(target.Value, label.Id, producer.IrPath,
                    settings.Publication.OutputDirectory, producer.Ir.Version.Text, ModelLabels.Family(family), snapshot.KindName,
                    "DocumentTree", receipt, staged.Diagnostics)
                { BackendTransport = guest.Transport, BackendVersion = label.Version });
            }
            finally
            {
                stage?.Dispose();
            }
        }
        catch (Exception fault) when (fault is IOException or UnauthorizedAccessException)
        {
            return Refuse("extension.bounded_io", fault.Message);
        }

        GenerationFailure Refusal(string code, string message) =>
            GenerationFailure.One(GenerationFailureKind.Operational, code, message, producer.IrPath);

        Outcome<GenerationSuccess> Refuse(string code, string message) =>
            Failed(Refusal(code, message));

        Outcome<GenerationSuccess> Failed(GenerationFailure failure) =>
            new Outcome<GenerationSuccess>.Failure(backendIdentity is { } identity
                ? failure with { Backend = identity.Id, BackendVersion = identity.Version, BackendTransport = identity.Transport }
                : failure);
    }

    private static ModelAccessStart Start(BoundedTreeSnapshot snapshot, ModelAccessTuple tuple, TargetId target,
        BackendOptions options, ModelAccessLimits limits)
    {
        var context = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(context))
        {
            writer.WriteStartObject();
            writer.WriteString("target", target.Value);
            writer.WriteString("irVersion", snapshot.IrVersion);
            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach (var dependency in snapshot.DependencyIds.Order(StringComparer.Ordinal))
                writer.WriteStringValue(dependency);
            writer.WriteEndArray();
            WriteOptions(writer, "options", options.Values);
            WriteOptions(writer, "configured", options.Configured);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(context.WrittenMemory);
        var session = Guid.NewGuid().ToString("N");
        return new ModelAccessStart(session,
            new ModelAccessSnapshot(Guid.NewGuid().ToString("N"), snapshot.PackageId, snapshot.RevisionId,
                Sha256Digest.Compute(context.WrittenSpan), session),
            tuple, target.Value, document.RootElement.GetProperty("options").Clone(), [], limits);
    }

    private static void WriteOptions(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, JsonElement> values)
    {
        writer.WriteStartObject(name);
        foreach (var (key, value) in values.OrderBy(option => option.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            value.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static ModelAccessLimits OfferLimits(ModelReadLimits limits, bool wasm)
    {
        var frameBodyBytes = ContentLengthFraming.MaxBodyBytes;
        if (wasm)
        {
            var adapter = SessionLimits.Default;
            var frameBytes = Math.Min(adapter.RequestFrameBytes, adapter.ResponseFrameBytes);
            // The adapter counts the Content-Length header. MEP's body ceiling excludes it.
            var headerBytes = System.Text.Encoding.ASCII.GetByteCount($"Content-Length: {frameBytes}\r\n\r\n");
            frameBodyBytes = (int)Math.Min(frameBodyBytes, frameBytes - headerBytes);
        }
        return new(frameBodyBytes, 4 * 1024 * 1024, (int)Math.Min(limits.MaxBytes, 3 * 1024 * 1024),
            64, limits.MaxDepth, limits.MaxNodes, 64, 64, 64 * 1024 * 1024, 16 * 1024 * 1024, 1024 * 1024,
            4L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024,
            1_000_000, 10_000, 30_000, 120_000);
    }

    private sealed class RecordedTreeFiles : IFileProvider, IDisposable
    {
        private readonly PhysicalFileProvider _provider;

        public RecordedTreeFiles(string root)
        {
            RejectLink(root);
            if (!Directory.Exists(root))
                throw new IOException($"The recorded document tree at {root} is missing.");
            _provider = new PhysicalFileProvider(root);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var file = _provider.GetFileInfo(subpath);
            if (file.Exists && file.PhysicalPath is { } path)
            {
                RejectLink(path);
                if (File.GetAttributes(path).HasFlag(FileAttributes.Device) || !UnixInputFile.IsRegular(path))
                    throw new IOException("Only regular input files are supported.");
            }
            return file;
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            RejectLink(Path.Combine(_provider.Root, subpath));
            return _provider.GetDirectoryContents(subpath);
        }

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

        public void Dispose() => _provider.Dispose();

        private static void RejectLink(string path)
        {
            if (GenerationPublisher.LinkedPath(path) is { } link)
                throw new IOException($"The recorded tree contains a link at {link}.");
        }
    }
}
