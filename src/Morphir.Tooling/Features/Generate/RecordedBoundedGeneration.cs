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
using Morphir.Tooling.Features.Compile;
using Morphir.Tooling.Features.Extensions;
using Morphir.Tooling.Features.TaskRecords;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Runs a process or WASM generation guest over a recorded or explicit JSON/YAML document tree.</summary>
internal static class RecordedBoundedGeneration
{
    /// <summary>True when <paramref name="path"/> is a directory containing a JSON or YAML manifest (and not an Ion tree).</summary>
    internal static bool IsJsonOrYamlDocumentTree(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!Directory.Exists(path)) return false;
            if (File.Exists(Path.Combine(path, "manifest.ion"))) return false;
            return File.Exists(Path.Combine(path, "manifest.json"))
                || File.Exists(Path.Combine(path, "manifest.yaml"))
                || File.Exists(Path.Combine(path, "manifest.yml"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static async Task<Outcome<GenerationSuccess>> RunExplicitTreeAsync(BackendExtensionRequest request,
        GenerationSettings settings, MepBackendExtensionConnector connector, IGenerationPublisher publisher,
        IEnumerable<IGenerationBackend> registered, CancellationToken ct,
        Func<LoadedModel, Task<Outcome<GenerationSuccess>>>? fallbackToDraft1 = null)
    {
        var target = request.Target;
        var inputPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.Input.Path));
        (string Id, string Version, string Transport)? backendIdentity = null;
        string? stagingParent = null;

        try
        {
            var outputPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.Publication.OutputDirectory));
            if (GenerationPublisher.ContainsPath(inputPath, outputPath) || GenerationPublisher.ContainsPath(outputPath, inputPath))
                return new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                    "input-output-overlap", "Input and output paths must not overlap.", outputPath));

            var readerLimits = RecordedGenerationAdmission.ReaderLimits(settings.BoundedProfile, settings.Limits);
            var captured = await new BoundedModelCapture().CaptureAsync(settings.Input, readerLimits, ct);
            if (captured is Outcome<RetainedInput>.Failure captureFailure)
                return new Outcome<GenerationSuccess>.Failure(captureFailure.Error);
            if (((Outcome<RetainedInput>.Success)captured).Value is not RetainedInput.Tree capturedTree)
                return new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Invocation,
                    "unsupported-input-entry", "The explicit document-tree input is not a directory.", inputPath));

            var files = new CapturedTreeFiles(capturedTree);
            var originalSha256 = ModelUpgrade.SnapshotOf(capturedTree).Hex;
            var revision = "sha256:" + originalSha256;
            var limits = RecordedGenerationAdmission.Offer(settings.BoundedProfile, readerLimits, request.WasmModule is not null);
            // Pre-negotiation detection reads only the manifest so unselected and late-mutated member bodies stay lazy.
            var opened = DocumentTreeStore.OpenManifestSnapshot(
                    new ReadOptions(readerLimits.MaxEntries, readerLimits.MaxBytes),
                    readerLimits.MaxDepth, readerLimits.MaxNodes, limits.MaxSessionWorkUnits, () => revision, files, ct)
                .Match<BoundedTreeSnapshot, IReadOnlyList<StoreError>, Outcome<BoundedTreeSnapshot>>(
                    value => new Outcome<BoundedTreeSnapshot>.Success(value),
                    problems => new Outcome<BoundedTreeSnapshot>.Failure(new(GenerationFailureKind.Operational,
                        problems.Select(problem => new GenerationDiagnostic(problem.Code, problem.Message, problem.FileOrNull)).ToImmutableArray())));
            if (opened is Outcome<BoundedTreeSnapshot>.Failure refusedSnapshot)
                return new Outcome<GenerationSuccess>.Failure(refusedSnapshot.Error);
            var snapshot = ((Outcome<BoundedTreeSnapshot>.Success)opened).Value;
            var json = snapshot.Syntax.Equals(BoundedTreeSyntax.Json);
            if (settings.Input.Format == InputFormat.Json && !json)
                return new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Semantic,
                    "unsupported-format", "YAML document trees do not match --input-format json.", inputPath));
            if (settings.Input.Format == InputFormat.Yaml && json)
                return new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Semantic,
                    "unsupported-format", "JSON document trees do not match --input-format yaml.", inputPath));

            var offeredTuples = OfferedTuples(snapshot, settings.BoundedProfile);
            var selectedTuple = offeredTuples[0];
            var connected = await connector.ConnectModelAccessAsync(request, new ModelAccessSupport(offeredTuples, limits), ct);
            if (connected is Outcome<ModelAccessConnection>.Failure refusedGuest)
            {
                if (fallbackToDraft1 is not null && settings.BoundedProfile != BoundedGenerationProfile.OrdersScale
                    && IsModelAccessNotAgreed(refusedGuest.Error))
                    return await FallbackToDraft1Async(inputPath, null, null, null, capturedTree, readerLimits,
                        limits.MaxSessionWorkUnits, ct, fallbackToDraft1);
                return new Outcome<GenerationSuccess>.Failure(refusedGuest.Error);
            }
            var connection = ((Outcome<ModelAccessConnection>.Success)connected).Value;
            var guest = connection.Guest;
            var negotiated = guest.Session;
            var label = negotiated.Extension;

            BoundedArtifactStage? stage = null;
            var fallbackRequested = false;
            try
            {
                Outcome<StagedGenerationOutput>? result = null;
                await using ((IAsyncDisposable)guest)
                {
                    if (!ExtensionId.TryParse(label.Id, out var id, out _) || id.ReservedForBuiltIns
                        || registered.Any(backend => backend.Descriptor.Id.Equals(id)))
                        return Refuse("extension.unavailable", $"Extension {label.Id} cannot identify itself as a built-in or invalid backend.");
                    backendIdentity = (label.Id, label.Version, guest.Transport);
                    if (negotiated.Capabilities.BackendOrNull() is not { } backend
                        || !backend.Targets.Contains(target.Value, StringComparer.Ordinal))
                        return Refuse("extension.unavailable", $"Extension {label.Id} does not claim target {target.Value}.");
                    if (!negotiated.ProtocolVersion.Equals(offeredTuples[0].ProtocolVersion)
                        || negotiated.Capabilities.ModelAccessOrNull() is not { } access)
                    {
                        if (fallbackToDraft1 is not null && settings.BoundedProfile != BoundedGenerationProfile.OrdersScale)
                        {
                            fallbackRequested = true;
                        }
                        else
                        {
                            return Refuse("extension.tuple_unavailable", $"Extension {label.Id} did not agree to bounded generation with {offeredTuples[0]}.");
                        }
                    }
                    else if (offeredTuples.FirstOrDefault(candidate => access.Tuples.Contains(candidate)) is not { } agreedTuple)
                    {
                        return Refuse("extension.tuple_unavailable", $"Extension {label.Id} did not agree to bounded generation with {offeredTuples[0]}.");
                    }
                    else
                    {
                        selectedTuple = agreedTuple;
                        var discoveryReservation = access.Limits.MaxHostCacheBytes / 4L;
                        var requestIdReservation = RecordedGenerationAdmission.RequestIdReservation(
                            access.Limits.MaxHostCacheBytes);
                        if (!guest.HostSession.TryReserveSingleGenerationRequestCache(requestIdReservation))
                            return Refuse("extension.bounded_cache_limit", "The request-ID reservation cannot be installed for this generation session.");
                        var indexed = DocumentTreeStore.OpenSnapshotWithDiscoveryCacheFallbackAndEncoding(
                                new ReadOptions(readerLimits.MaxEntries, RecordedGenerationAdmission.DocumentByteLimit(readerLimits, access.Limits)),
                                EncodingFor(selectedTuple), discoveryReservation, () => revision, files)
                            .Match<BoundedTreeSnapshot, IReadOnlyList<StoreError>, Outcome<BoundedTreeSnapshot>>(
                                value => new Outcome<BoundedTreeSnapshot>.Success(value),
                                problems => new Outcome<BoundedTreeSnapshot>.Failure(new(GenerationFailureKind.Operational,
                                    problems.Select(problem => new GenerationDiagnostic(
                                        problem.Code, problem.Message, problem.FileOrNull)).ToImmutableArray())));
                        if (indexed is Outcome<BoundedTreeSnapshot>.Failure refusedIndex)
                            return Failed(refusedIndex.Error);
                        snapshot = ((Outcome<BoundedTreeSnapshot>.Success)indexed).Value;
                        ct.ThrowIfCancellationRequested();

                        stagingParent = Path.Combine(StagingTempRoot(request.Environment),
                            "morphir-bounded-stage-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(stagingParent);
                        stage = new BoundedArtifactStage(stagingParent, access.Limits,
                            new GenerationSessionDiskBudget(access.Limits.MaxSessionDiskBytes), discoveryReservation, requestIdReservation);
                        var start = Start(snapshot, selectedTuple, target, settings.Options, access.Limits);
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
                            return Failed(new GenerationFailure(GenerationFailureKind.Operational,
                            [new("extension.bounded_deadline", "The generation deadline expired during guest shutdown.", inputPath),
                             new("extension.bounded_io", cleanup.Message, inputPath)]));
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
                }

                if (fallbackRequested)
                    return await FallbackToDraft1Async(inputPath, null, null, null, capturedTree, readerLimits,
                        limits.MaxSessionWorkUnits, ct, fallbackToDraft1!);

                if (result is Outcome<StagedGenerationOutput>.Failure failure)
                    return Failed(failure.Error);
                ct.ThrowIfCancellationRequested();
                var staged = ((Outcome<StagedGenerationOutput>.Success)result!).Value;
                var stagedFiles = staged.Artifacts
                    .Select(artifact => KeyValuePair.Create(artifact.Path,
                        VFileModule.ofDiskFile(artifact.Path, Path.Combine(staged.Directory, artifact.Path))))
                    .ToList();
                var artifacts = new GenerationArtifacts(VFileTree.OfFiles(stagedFiles), staged.Diagnostics);
                var published = await publisher.PublishAsync(artifacts, settings.Input, settings.Publication, ct);
                if (published is Outcome<GenerationPublication>.Failure publicationFailure)
                    return Failed(publicationFailure.Error);
                var receipt = ((Outcome<GenerationPublication>.Success)published).Value;
                var family = snapshot.IrVersion == "4.0.0" ? ModelFamily.V4 : ModelFamily.Classic;
                return new Outcome<GenerationSuccess>.Success(new(target.Value, label.Id, inputPath,
                    settings.Publication.OutputDirectory, snapshot.IrVersion, ModelLabels.Family(family), snapshot.KindName,
                    "DocumentTree", receipt, staged.Diagnostics)
                {
                    BackendTransport = guest.Transport,
                    BackendVersion = label.Version,
                    Provider = connection.ObserveProvider?.Invoke(selectedTuple),
                });
            }
            finally
            {
                stage?.Dispose();
                if (stagingParent is not null && Directory.Exists(stagingParent))
                {
                    try { Directory.Delete(stagingParent, recursive: true); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Failed(GenerationFailure.One(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled."));
        }
        catch (Exception fault) when (fault is IOException or UnauthorizedAccessException)
        {
            return Failed(Refusal("extension.bounded_io", fault.Message));
        }

        GenerationFailure Refusal(string code, string message) =>
            GenerationFailure.One(GenerationFailureKind.Operational, code, message, inputPath);

        Outcome<GenerationSuccess> Refuse(string code, string message) =>
            Failed(Refusal(code, message));

        Outcome<GenerationSuccess> Failed(GenerationFailure failure) =>
            new Outcome<GenerationSuccess>.Failure(backendIdentity is { } identity
                ? failure with { Backend = identity.Id, BackendVersion = identity.Version, BackendTransport = identity.Transport }
                : failure);
    }

    public static async Task<Outcome<GenerationSuccess>> RunAsync(CompileTaskOutput producer, BackendExtensionRequest request,
        GenerationSettings settings, MepBackendExtensionConnector connector, TaskStoreOptions taskOptions,
        IEnumerable<IGenerationBackend> registered, CancellationToken ct,
        Func<LoadedModel, IGenerationPublisher, Task<Outcome<GenerationSuccess>>>? fallbackToDraft1 = null)
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
            // Pre-negotiation detection on normal runs reads only the manifest so unselected and late-mutated
            // member bodies remain lazy; draft.2 re-opens with its negotiated encoding and document ceiling below.
            var opened = (settings.BoundedProfile == BoundedGenerationProfile.OrdersScale
                    ? DocumentTreeStore.OpenPlainSnapshot(
                        new ReadOptions(readerLimits.MaxEntries, RecordedGenerationAdmission.DocumentByteLimit(readerLimits, limits)),
                        () => revision, files)
                    : DocumentTreeStore.OpenManifestSnapshot(
                        new ReadOptions(readerLimits.MaxEntries, readerLimits.MaxBytes),
                        readerLimits.MaxDepth, readerLimits.MaxNodes, limits.MaxSessionWorkUnits, () => revision, files, ct))
                .Match<BoundedTreeSnapshot, IReadOnlyList<StoreError>, Outcome<BoundedTreeSnapshot>>(
                    value => new Outcome<BoundedTreeSnapshot>.Success(value),
                    problems => settings.BoundedProfile == BoundedGenerationProfile.OrdersScale
                        && problems.Any(problem => problem.Code is "tree.unsupported_context" or "context.unexpected_context")
                        ? new Outcome<BoundedTreeSnapshot>.Failure(Refusal("generate.invalid_bounded_profile",
                            "orders-scale requires a plain JSON v4 recorded tree."))
                        : new Outcome<BoundedTreeSnapshot>.Failure(new(GenerationFailureKind.Operational,
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

            if (settings.BoundedProfile == BoundedGenerationProfile.OrdersScale
                && (!json || snapshot.IrVersion != "4.0.0" || !snapshot.NameEncoding.Equals(BoundedTreeEncoding.Plain)))
                return Refuse("generate.invalid_bounded_profile", "orders-scale requires a plain JSON v4 recorded tree.");

            if (!TaskId.TryParse($"generate/{target.Value}", out var task, out var invalid))
                return Refuse("extension.bounded_output", invalid ?? "The generation task name is invalid.");
            var location = producer.Location.Root.Task(producer.Location.Member, task);
            // The caller still owns the shared compile lease. Always acquire in compile -> generation order.
            using var taskRun = await RecordedGenerationTask.OpenAsync(location, outputPath, taskOptions, ct);

            var offeredTuples = OfferedTuples(snapshot, settings.BoundedProfile);
            var selectedTuple = offeredTuples[0];
            var connected = await connector.ConnectModelAccessAsync(request, new ModelAccessSupport(offeredTuples, limits), ct);
            if (connected is Outcome<ModelAccessConnection>.Failure refusedGuest)
            {
                if (fallbackToDraft1 is not null && settings.BoundedProfile != BoundedGenerationProfile.OrdersScale
                    && IsModelAccessNotAgreed(refusedGuest.Error))
                {
                    var taskPublisher = new RecordedTaskGenerationPublisher(taskRun, producer.IrPath);
                    return await FallbackToDraft1Async(producer.IrPath, producer.Location.Record, producer.Ir.Version,
                        producer.Ir.Format, null, readerLimits, limits.MaxSessionWorkUnits, ct,
                        model => fallbackToDraft1(model, taskPublisher));
                }
                return new Outcome<GenerationSuccess>.Failure(refusedGuest.Error);
            }
            var connection = ((Outcome<ModelAccessConnection>.Success)connected).Value;
            var guest = connection.Guest;
            var negotiated = guest.Session;
            var label = negotiated.Extension;

            BoundedArtifactStage? stage = null;
            var fallbackRequested = false;
            try
            {
                Outcome<StagedGenerationOutput>? result = null;
                await using ((IAsyncDisposable)guest)
                {
                    if (!ExtensionId.TryParse(label.Id, out var id, out _) || id.ReservedForBuiltIns
                        || registered.Any(backend => backend.Descriptor.Id.Equals(id)))
                        return Refuse("extension.unavailable", $"Extension {label.Id} cannot identify itself as a built-in or invalid backend.");
                    backendIdentity = (label.Id, label.Version, guest.Transport);
                    if (negotiated.Capabilities.BackendOrNull() is not { } backend
                        || !backend.Targets.Contains(target.Value, StringComparer.Ordinal))
                        return Refuse("extension.unavailable", $"Extension {label.Id} does not claim target {target.Value}.");
                    if (!negotiated.ProtocolVersion.Equals(offeredTuples[0].ProtocolVersion)
                        || negotiated.Capabilities.ModelAccessOrNull() is not { } access)
                    {
                        if (fallbackToDraft1 is not null && settings.BoundedProfile != BoundedGenerationProfile.OrdersScale)
                        {
                            fallbackRequested = true;
                        }
                        else
                        {
                            return Refuse("extension.tuple_unavailable", $"Extension {label.Id} did not agree to bounded generation with {offeredTuples[0]}.");
                        }
                    }
                    else if (offeredTuples.FirstOrDefault(candidate => access.Tuples.Contains(candidate)) is not { } agreedTuple)
                    {
                        return Refuse("extension.tuple_unavailable", $"Extension {label.Id} did not agree to bounded generation with {offeredTuples[0]}.");
                    }
                    else
                    {
                        selectedTuple = agreedTuple;
                        // Reserve a bounded quarter of the negotiated cache for the active package's
                        // paths before any discovery. The stage can use only the remaining bytes.
                        var discoveryReservation = access.Limits.MaxHostCacheBytes / 4L;
                        var requestIdReservation = RecordedGenerationAdmission.RequestIdReservation(
                            access.Limits.MaxHostCacheBytes);
                        if (!guest.HostSession.TryReserveSingleGenerationRequestCache(requestIdReservation))
                            return Refuse("extension.bounded_cache_limit", "The request-ID reservation cannot be installed for this generation session.");
                        var indexed = DocumentTreeStore.OpenSnapshotWithDiscoveryCacheFallbackAndEncoding(
                                new ReadOptions(readerLimits.MaxEntries, RecordedGenerationAdmission.DocumentByteLimit(readerLimits, access.Limits)),
                                EncodingFor(selectedTuple), discoveryReservation, () => revision, files)
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
                            new GenerationSessionDiskBudget(access.Limits.MaxSessionDiskBytes), discoveryReservation, requestIdReservation);
                        var start = Start(snapshot, selectedTuple, target, settings.Options, access.Limits);
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
                }

                if (fallbackRequested)
                {
                    var taskPublisher = new RecordedTaskGenerationPublisher(taskRun, producer.IrPath);
                    return await FallbackToDraft1Async(producer.IrPath, producer.Location.Record, producer.Ir.Version,
                        producer.Ir.Format, null, readerLimits, limits.MaxSessionWorkUnits, ct,
                        model => fallbackToDraft1!(model, taskPublisher));
                }

                if (result is Outcome<StagedGenerationOutput>.Failure failure)
                    return Failed(failure.Error);
                ct.ThrowIfCancellationRequested();
                var staged = ((Outcome<StagedGenerationOutput>.Success)result!).Value;
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
                    Provider = connection.ObserveProvider?.Invoke(selectedTuple),
                    BoundedAccess = settings.BoundedProfile == BoundedGenerationProfile.OrdersScale
                        ? AccessObservation(readerLimits, negotiated.Capabilities.ModelAccessOrNull()!.Limits,
                            ConfiguredWasmModule.ClosedLinearMemory(connection.WasmChannel),
                            staged.MaxInputBatchBytes, stage!.MaxOutputBatchBytes,
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

    private static ModelAccessTuple[] OfferedTuples(BoundedTreeSnapshot snapshot, BoundedGenerationProfile profile)
    {
        var baseTuple = MepGenerationSnapshot.TupleFor(snapshot);
        if (snapshot.NameEncoding.Equals(BoundedTreeEncoding.ContextualDraft1) || profile == BoundedGenerationProfile.OrdersScale)
            return [baseTuple];
        var contextualTuple = new ModelAccessTuple(baseTuple.ProtocolVersion, baseTuple.AccessVersion, baseTuple.Operation,
            baseTuple.IrVersion, baseTuple.Profile, baseTuple.Codec, baseTuple.Layout, ModelAccessEncoding.ContextualDraft1);
        return [contextualTuple, baseTuple];
    }

    private static BoundedTreeEncoding EncodingFor(ModelAccessTuple tuple) =>
        tuple.ContextualEncoding.Equals(ModelAccessEncoding.ContextualDraft1)
            ? BoundedTreeEncoding.ContextualDraft1
            : BoundedTreeEncoding.Plain;

    private static string StagingTempRoot(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is not null)
        {
            foreach (var key in new[] { "TMPDIR", "TEMP", "TMP" })
            {
                if (environment.TryGetValue(key, out var configured) && !string.IsNullOrWhiteSpace(configured))
                {
                    Directory.CreateDirectory(configured);
                    return CompilePaths.RealPath(configured);
                }
            }
        }
        return CompilePaths.RealPath(Path.GetTempPath());
    }

    private static bool IsModelAccessNotAgreed(GenerationFailure failure) =>
        failure.Diagnostics.Any(diagnostic => diagnostic.Code == "extension.unavailable"
            && string.Equals(diagnostic.Message, MepChannelGuest.ModelAccessNotAgreedMessage, StringComparison.Ordinal));

    private static async Task<Outcome<GenerationSuccess>> FallbackToDraft1Async(string sourcePath,
        string? recordPath, SourceFormatVersion? declaredVersion, IrStorageFormat? declaredFormat,
        RetainedInput.Tree? capturedOriginal, ModelReadLimits limits, long maxWorkUnits, CancellationToken ct,
        Func<LoadedModel, Task<Outcome<GenerationSuccess>>> fallbackToDraft1)
    {
        ct.ThrowIfCancellationRequested();
        RetainedInput.Tree originalTree;
        if (capturedOriginal is not null)
        {
            originalTree = capturedOriginal;
        }
        else
        {
            var captured = await new BoundedModelCapture().CaptureAsync(new ModelInput(sourcePath, InputFormat.Auto), limits, ct);
            if (captured is Outcome<RetainedInput>.Failure captureFailure)
                return new Outcome<GenerationSuccess>.Failure(captureFailure.Error);
            if (((Outcome<RetainedInput>.Success)captured).Value is not RetainedInput.Tree tree)
                return new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Operational,
                    "extension.bounded_io", "The recorded document tree could not be captured.", sourcePath));
            originalTree = tree;
        }

        ct.ThrowIfCancellationRequested();
        // Hash, check losslessness, and join strictly from the single immutable captured originalTree so disk edits
        // cannot split the reported digest from the converted bytes and no sidecar file can bypass loss checks.
        var originalSha256 = ModelUpgrade.SnapshotOf(originalTree).Hex;
        var capturedRevision = "sha256:" + originalSha256;
        var capturedFiles = new CapturedTreeFiles(originalTree);
        var reopened = DocumentTreeStore.OpenDetectedSnapshotBounded(
                new ReadOptions(limits.MaxEntries, limits.MaxBytes), limits.MaxDepth, limits.MaxNodes, maxWorkUnits,
                () => capturedRevision, capturedFiles, ct)
            .Match<BoundedTreeSnapshot, IReadOnlyList<StoreError>, Outcome<BoundedTreeSnapshot>>(
                value => new Outcome<BoundedTreeSnapshot>.Success(value),
                problems => new Outcome<BoundedTreeSnapshot>.Failure(new(GenerationFailureKind.Operational,
                    problems.Select(problem => new GenerationDiagnostic(problem.Code, problem.Message, problem.FileOrNull)).ToImmutableArray())));
        if (reopened is Outcome<BoundedTreeSnapshot>.Failure reopenFailure)
            return new Outcome<GenerationSuccess>.Failure(reopenFailure.Error);
        var capturedSnapshot = ((Outcome<BoundedTreeSnapshot>.Success)reopened).Value;

        // Re-verify the captured snapshot's IR version and storage format against the compile record rather than
        // trusting the earlier pre-negotiation probe or overwriting the captured source version.
        if (declaredVersion is not null
            && !FormatVersion.SameVersion(declaredVersion.Version, SourceFormatVersion.Create(capturedSnapshot.IrVersion).Version))
        {
            var recordLabel = recordPath ?? sourcePath;
            return new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Operational,
                "compile.record_version_mismatch",
                $"The compile record at {recordLabel} names IR {declaredVersion.Text}, but {sourcePath} declares {capturedSnapshot.IrVersion}.",
                sourcePath));
        }

        if (declaredFormat is { } expectedFormat)
        {
            var capturedFormat = capturedSnapshot.Syntax.Equals(BoundedTreeSyntax.Json)
                ? IrStorageFormat.Json
                : IrStorageFormat.Yaml;
            if (capturedFormat != expectedFormat)
                return new Outcome<GenerationSuccess>.Failure(GenerationFailure.One(GenerationFailureKind.Operational,
                    "compile.record_format_mismatch",
                    $"The manifest at {sourcePath} does not match its recorded {IrStorageNames.Name(expectedFormat)} format.",
                    sourcePath));
        }

        var originalFilePaths = originalTree.Entries.Select(entry => entry.RelativePath).ToArray();
        if (!DocumentTreeStore.TryJoinSnapshot(capturedSnapshot, originalFilePaths, limits.MaxEntries, limits.MaxDepth,
            limits.MaxBytes, limits.MaxBytes, ct, out var versioned, out _, out var problems))
        {
            var kind = problems.Any(problem => problem.Code == "data.lossy_conversion")
                ? GenerationFailureKind.Semantic
                : GenerationFailureKind.Operational;
            return new Outcome<GenerationSuccess>.Failure(new GenerationFailure(kind,
                problems.Select(problem => new GenerationDiagnostic(problem.Code, problem.Message, problem.FileOrNull)).ToImmutableArray()));
        }

        ct.ThrowIfCancellationRequested();
        var fromCodec = capturedSnapshot.Syntax.Equals(BoundedTreeSyntax.Json) ? "morphir-json" : "morphir-yaml";
        var origin = new ModelConversionOrigin(capturedSnapshot.PackageId, "morphir-ir", fromCodec,
            capturedSnapshot.IrVersion, capturedRevision, originalSha256);
        var loaded = new LoadedModel(versioned, ModelDescriptor.From(versioned), ModelRepresentation.Json, originalTree)
        {
            ConversionOrigin = origin,
        };
        ct.ThrowIfCancellationRequested();
        return await fallbackToDraft1(loaded);
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

    private sealed class CapturedTreeFiles : IFileProvider
    {
        private readonly Dictionary<string, RetainedEntry> _files;
        private readonly Dictionary<string, ImmutableArray<IFileInfo>> _directories;

        public CapturedTreeFiles(RetainedInput.Tree tree)
        {
            _files = new Dictionary<string, RetainedEntry>(StringComparer.Ordinal);
            var dirs = new Dictionary<string, Dictionary<string, IFileInfo>>(StringComparer.Ordinal)
            {
                [""] = new Dictionary<string, IFileInfo>(StringComparer.Ordinal),
            };

            foreach (var entry in tree.Entries)
            {
                var normalized = Normalize(entry.RelativePath);
                if (normalized.Length == 0) continue;
                _files[normalized] = entry;

                var segments = normalized.Split('/');
                var current = "";
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    var segment = segments[i];
                    var next = current.Length == 0 ? segment : current + "/" + segment;
                    if (!dirs.TryGetValue(current, out var parentMap))
                        dirs[current] = parentMap = new Dictionary<string, IFileInfo>(StringComparer.Ordinal);
                    if (!parentMap.ContainsKey(segment))
                        parentMap[segment] = new DirectoryEntryInfo(segment);
                    if (!dirs.ContainsKey(next))
                        dirs[next] = new Dictionary<string, IFileInfo>(StringComparer.Ordinal);
                    current = next;
                }

                if (!dirs.TryGetValue(current, out var leafParent))
                    dirs[current] = leafParent = new Dictionary<string, IFileInfo>(StringComparer.Ordinal);
                var name = segments[^1];
                leafParent[name] = new FileEntryInfo(name, entry);
            }

            _directories = dirs.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => item.Value)
                    .ToImmutableArray(),
                StringComparer.Ordinal);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var normalized = Normalize(subpath);
            if (_files.TryGetValue(normalized, out var entry))
            {
                var slash = normalized.LastIndexOf('/');
                var name = slash < 0 ? normalized : normalized[(slash + 1)..];
                return new FileEntryInfo(name, entry);
            }
            return new NotFoundFileInfo(subpath ?? "");
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            var normalized = Normalize(subpath);
            return _directories.TryGetValue(normalized, out var entries)
                ? new CapturedDirectoryContents(entries)
                : NotFoundDirectoryContents.Singleton;
        }

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

        private static string Normalize(string? subpath)
        {
            if (string.IsNullOrEmpty(subpath)) return "";
            var normalized = subpath.Replace('\\', '/').Trim('/');
            return normalized == "." ? "" : normalized;
        }

        private sealed class FileEntryInfo(string name, RetainedEntry entry) : IFileInfo
        {
            public bool Exists => true;
            public long Length => entry.Length;
            public string? PhysicalPath => null;
            public string Name => name;
            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
            public bool IsDirectory => false;
            public Stream CreateReadStream() => entry.OpenRead();
        }

        private sealed class DirectoryEntryInfo(string name) : IFileInfo
        {
            public bool Exists => true;
            public long Length => -1;
            public string? PhysicalPath => null;
            public string Name => name;
            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
            public bool IsDirectory => true;
            public Stream CreateReadStream() => throw new InvalidOperationException("Cannot open a read stream for a directory.");
        }

        private sealed class CapturedDirectoryContents(ImmutableArray<IFileInfo> entries) : IDirectoryContents
        {
            public bool Exists => true;
            public IEnumerator<IFileInfo> GetEnumerator() => ((IEnumerable<IFileInfo>)entries).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    private sealed class RecordedTaskGenerationPublisher(RecordedGenerationTask taskRun, string sourcePath) : IGenerationPublisher
    {
        public async ValueTask<Outcome<GenerationPublication>> PublishAsync(GenerationArtifacts artifacts, ModelInput input,
            PublicationPolicy policy, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                taskRun.Prepare();
                taskRun.CommitFiles(artifacts.Tree, ct);
                var installed = await taskRun.InstallAsync(ct);
                if (installed is Outcome<InstallReport>.Failure refused)
                    return new Outcome<GenerationPublication>.Failure(refused.Error);
                var install = ((Outcome<InstallReport>.Success)installed).Value;
                return new Outcome<GenerationPublication>.Success(
                    new GenerationPublication(install.Copied, install.Written, install.Unchanged, install.Removed));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new Outcome<GenerationPublication>.Failure(
                    GenerationFailure.One(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled.", sourcePath));
            }
            catch (Exception fault) when (fault is IOException or UnauthorizedAccessException)
            {
                var failure = GenerationFailure.One(GenerationFailureKind.Operational, "extension.bounded_io", fault.Message, sourcePath);
                foreach (var key in new[] { "generation.promotion_cleanup", "generation.stage_cleanup" })
                    if (fault.Data[key] is { } cleanup)
                        failure = failure with
                        {
                            Diagnostics = failure.Diagnostics.Add(new("extension.artifact_cleanup",
                                cleanup is Exception error ? error.Message : cleanup.ToString()!))
                        };
                return new Outcome<GenerationPublication>.Failure(failure);
            }
        }
    }
}
