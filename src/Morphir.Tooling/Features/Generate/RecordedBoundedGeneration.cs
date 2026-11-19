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
using Morphir.IR.Model;
using Morphir.IR.Trees;
using Morphir.Mep;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Runs a process or WASM generation guest over the document tree named by a locked compile result.</summary>
internal static class RecordedBoundedGeneration
{
    public static async Task<Outcome<GenerationSuccess>> RunAsync(CompileTaskOutput producer, BackendExtensionRequest request,
        GenerationSettings settings, MepBackendExtensionConnector connector, TaskStoreOptions taskOptions,
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
            var readerLimits = RecordedGenerationAdmission.ReaderLimits(settings.BoundedProfile, settings.Limits);
            var limits = RecordedGenerationAdmission.Offer(settings.BoundedProfile, readerLimits, request.WasmModule is not null);
            var opened = DocumentTreeStore.OpenPlainSnapshot(
                    new ReadOptions(readerLimits.MaxEntries, RecordedGenerationAdmission.DocumentByteLimit(readerLimits, limits)), () => revision, files)
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

            if (settings.BoundedProfile == BoundedGenerationProfile.OrdersScale && (!json || snapshot.IrVersion != "4.0.0"))
                return Refuse("generate.invalid_bounded_profile", "orders-scale requires a plain JSON v4 recorded tree.");

            if (!TaskId.TryParse($"generate/{target.Value}", out var task, out var invalid))
                return Refuse("extension.bounded_output", invalid ?? "The generation task name is invalid.");
            var location = producer.Location.Root.Task(producer.Location.Member, task);
            // The caller still owns the shared compile lease. Always acquire in compile -> generation order.
            using var taskRun = await RecordedGenerationTask.OpenAsync(location, outputPath, taskOptions, ct);

            var tuple = new ModelAccessTuple(ModelAccess.protocolVersion, ModelAccess.accessVersion, ModelOperation.GenerateModel,
                SemanticVersion.Create(snapshot.IrVersion), "morphir-ir",
                json ? ModelAccessCodec.MorphirJson : ModelAccessCodec.MorphirYaml, "document-tree", ModelAccessEncoding.Plain);
            var connected = await connector.ConnectModelAccessAsync(request, new ModelAccessSupport([tuple], limits), ct);
            if (connected is Outcome<ModelAccessConnection>.Failure refusedGuest)
                return new Outcome<GenerationSuccess>.Failure(refusedGuest.Error);
            var connection = ((Outcome<ModelAccessConnection>.Success)connected).Value;
            var guest = connection.Guest;
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

                    // Reserve a bounded quarter of the negotiated cache for the active package's
                    // paths before any discovery. The stage can use only the remaining bytes.
                    var discoveryReservation = access.Limits.MaxHostCacheBytes / 4L;
                    var requestIdReservation = RecordedGenerationAdmission.RequestIdReservation(
                        settings.BoundedProfile, access.Limits.MaxHostCacheBytes);
                    if (requestIdReservation is { } reservation && !guest.HostSession.TryReserveSingleGenerationRequestCache(reservation))
                        return Refuse("extension.bounded_cache_limit", "The request-ID reservation cannot be installed for this generation session.");
                    var indexed = DocumentTreeStore.OpenPlainSnapshotWithDiscoveryCache(
                            new ReadOptions(readerLimits.MaxEntries, RecordedGenerationAdmission.DocumentByteLimit(readerLimits, access.Limits)),
                            discoveryReservation, () => revision, files)
                        .Match<BoundedTreeSnapshot, IReadOnlyList<StoreError>, Outcome<BoundedTreeSnapshot>>(
                            value => new Outcome<BoundedTreeSnapshot>.Success(value),
                            problems => new Outcome<BoundedTreeSnapshot>.Failure(new(GenerationFailureKind.Operational,
                                problems.Select(problem => new GenerationDiagnostic(
                                    problem.Code, problem.Message, problem.FileOrNull)).ToImmutableArray())));
                    if (indexed is Outcome<BoundedTreeSnapshot>.Failure refusedIndex)
                        return Failed(refusedIndex.Error);
                    snapshot = ((Outcome<BoundedTreeSnapshot>.Success)indexed).Value;
                    ct.ThrowIfCancellationRequested();
                    taskRun.Prepare();
                    stage = new BoundedArtifactStage(location.Dest, access.Limits,
                        new GenerationSessionDiskBudget(access.Limits.MaxSessionDiskBytes), discoveryReservation, requestIdReservation.GetValueOrDefault());
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
                // The guest has shut down and its owned process/session has settled. Move, clean, record,
                // then install independently through the task ledger. No second private copy of artifact bytes.
                taskRun.Commit(stage!, staged, ct);
                var installed = await taskRun.InstallAsync(ct);
                if (installed is Outcome<InstallReport>.Failure refused)
                    return Failed(refused.Error);
                var install = ((Outcome<InstallReport>.Success)installed).Value;
                var receipt = new GenerationPublication(install.Copied, install.Written, install.Unchanged, install.Removed);
                var family = snapshot.IrVersion == "4.0.0" ? ModelFamily.V4 : ModelFamily.Classic;
                return new Outcome<GenerationSuccess>.Success(new(target.Value, label.Id, producer.IrPath,
                    settings.Publication.OutputDirectory, producer.Ir.Version.Text, ModelLabels.Family(family), snapshot.KindName,
                    "DocumentTree", receipt, staged.Diagnostics)
                {
                    BackendTransport = guest.Transport,
                    BackendVersion = label.Version,
                    BoundedAccess = settings.BoundedProfile == BoundedGenerationProfile.OrdersScale
                        ? AccessObservation(readerLimits, negotiated.Capabilities.ModelAccessOrNull()!.Limits,
                            ConfiguredWasmModule.ClosedLinearMemory(connection.WasmChannel),
                            staged.MaxInputBatchBytes, stage.MaxOutputBatchBytes,
                            snapshot.PeakDiscoveryIndexChargedBytes) : null,
                });
            }
            finally
            {
                stage?.Dispose();
            }
        }
        catch (OperationCanceledException cancelled) when (ct.IsCancellationRequested)
        {
            return Failed(WithPublicationCleanup(GenerationFailure.One(GenerationFailureKind.Cancelled,
                "cancelled", "Generation was cancelled."), cancelled));
        }
        catch (Exception fault) when (fault is IOException or UnauthorizedAccessException)
        {
            return Failed(WithPublicationCleanup(Refusal("extension.bounded_io", fault.Message), fault));
        }

        static GenerationFailure WithPublicationCleanup(GenerationFailure primary, Exception fault)
        {
            foreach (var key in new[] { "generation.promotion_cleanup", "generation.stage_cleanup" })
                if (fault.Data[key] is { } cleanup)
                    primary = primary with
                    {
                        Diagnostics = primary.Diagnostics.Add(new("extension.artifact_cleanup",
                        cleanup is Exception error ? error.Message : cleanup.ToString()!))
                    };
            return primary;
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

    private static JsonElement AccessObservation(ModelReadLimits reader, ModelAccessLimits selected, MemoryUse? memory,
        long maxInputBatchBytes, long maxOutputBatchBytes, long peakDiscoveryIndexChargedBytes)
    {
        var wasm = RecordedGenerationAdmission.WasmLimits(BoundedGenerationProfile.OrdersScale, selected);
        var bytes = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteString("profile", BoundedGenerationProfile.OrdersScale.Name);
            writer.WriteNumber("maxDocumentBytes", RecordedGenerationAdmission.DocumentByteLimit(reader, selected));
            writer.WriteNumber("maxRequestFrameBytes", wasm.RequestFrameBytes);
            writer.WriteNumber("maxResponseFrameBytes", wasm.ResponseFrameBytes);
            writer.WriteNumber("maxInputBatchBytes", maxInputBatchBytes);
            writer.WriteNumber("maxOutputBatchBytes", maxOutputBatchBytes);
            writer.WriteNumber("peakDiscoveryIndexChargedBytes", peakDiscoveryIndexChargedBytes);
            writer.WritePropertyName("limits");
            JsonSerializer.Serialize(writer, selected, MepJson.Default.ModelAccessLimits);
            if (memory is not null)
            {
                writer.WriteStartObject("linearMemory");
                if (memory.GuestBytes is { } guestBytes)
                    writer.WriteNumber("peakGuestLinearMemoryBytes", guestBytes);
                else
                    writer.WriteNull("peakGuestLinearMemoryBytes");
                writer.WriteNumber("peakKernelLinearMemoryBytes", memory.KernelBytes);
                writer.WriteNumber("perMemoryCeilingBytes", memory.CeilingBytes);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(bytes.WrittenMemory);
        return document.RootElement.Clone();
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
