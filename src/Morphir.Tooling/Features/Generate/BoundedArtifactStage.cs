// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Base64 = System.Buffers.Text.Base64;
using System.Collections.Immutable;
using System.Security.Cryptography;
using Morphir.IR.Trees;
using Morphir.Mep;

namespace Morphir.Tooling.Features.Generate;

internal sealed record StagedGenerationArtifact(string Path, long Bytes, Sha256Digest Sha256);

/// <summary>Private files and verified metadata, valid while the stage is owned by the caller.</summary>
internal sealed record StagedGenerationOutput(string Directory, IEnumerable<StagedGenerationArtifact> Artifacts,
    ImmutableArray<GenerationDiagnostic> Diagnostics);

/// <summary>Stages the admitted replies of one generation session under a caller-owned task directory.</summary>
internal sealed class BoundedArtifactStage : IDisposable
{
    private sealed class Entry(string path, bool isDirectory)
    {
        public string Path { get; } = path;
        public bool IsDirectory { get; } = isDirectory;
        public long Bytes { get; set; }
        public StagedGenerationArtifact? Closed { get; set; }
    }

    private readonly string _parent;
    private readonly ModelAccessLimits _limits;
    private readonly GenerationSessionDiskBudget _disk;
    private readonly Dictionary<string, Entry> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ImmutableArray<GenerationDiagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<GenerationDiagnostic>();
    private long _metadataBytes;
    private long _artifactBytes;
    private long _reservedDiskBytes;
    private int _openArtifacts;
    private bool _settled;
    private bool _deleted;
    private GenerationDiagnostic? _cleanupError;

    public BoundedArtifactStage(string parent, ModelAccessLimits limits, GenerationSessionDiskBudget disk)
    {
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        _limits = limits;
        _disk = disk;
        if (disk.Limit > limits.MaxSessionDiskBytes)
            throw new ArgumentException("The disk budget exceeds the negotiated session disk limit.", nameof(disk));
        if (!System.IO.Directory.Exists(_parent) || GenerationPublisher.LinkedPath(_parent) is not null)
            throw new IOException("The generation staging parent must be an existing, unlinked directory.");

        Directory = Path.Combine(_parent, "bounded-" + Guid.NewGuid().ToString("N"));
        if (Path.Exists(Directory)) throw new IOException("The private generation stage already exists.");
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public Outcome<StagedGenerationOutput?> Apply(ModelAccessStep step, CancellationToken cancellationToken = default,
        BoundedTreeWorkBudget? work = null)
    {
        if (_settled)
            return new Outcome<StagedGenerationOutput?>.Failure(
                Failure("extension.artifact_state", "The generation stage has already settled."));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The session admits reply identity and round before calling the stage.
            return step.Match(
                (_, _, _, chunks, diagnostics) => ApplyExchange(chunks, diagnostics, false, cancellationToken, work),
                (_, chunks, diagnostics) => ApplyExchange(chunks, diagnostics, true, cancellationToken, work),
                (_, diagnostics) => Abort(RetainDiagnostics(diagnostics, cancellationToken, work)
                    ?? new GenerationFailure(GenerationFailureKind.Semantic, _diagnostics.ToImmutable())));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Abort(GenerationFailure.One(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Abort(Failure("extension.artifact_io", error.Message));
        }
    }

    private Outcome<StagedGenerationOutput?> ApplyExchange(
        IReadOnlyList<ModelArtifactChunk> chunks, IReadOnlyList<Diagnostic> diagnostics, bool complete,
        CancellationToken cancellationToken, BoundedTreeWorkBudget? work)
    {
        if (RetainDiagnostics(diagnostics, cancellationToken, work) is { } diagnosticFailure)
            return Abort(diagnosticFailure);

        long encoded = 0;
        long decoded = 0;
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.Content is null || chunk.DecodedByteLength < 0)
                return Abort(Integrity(chunk.Path));
            encoded += chunk.Content.Length;
            if (encoded > _limits.MaxEncodedBytesPerExchange)
                return Abort(Limit("maxEncodedBytesPerExchange", chunk.Path));
            if (work?.TryCharge(1 + BoundedTreeWorkBudget.Blocks(2L * chunk.Content.Length)) is false)
                return Abort(WorkLimit(chunk.Path));
            // Derive the length from the encoded payload without allocating a decoded buffer.
            if (!Base64.IsValid(chunk.Content.AsSpan(), out var length) || length != chunk.DecodedByteLength
                || chunk.Content.Length != 4L * ((length + 2L) / 3) || (!chunk.End && length == 0))
                return Abort(Integrity(chunk.Path));
            if (length > _limits.MaxArtifactChunkBytes)
                return Abort(Limit("maxArtifactChunkBytes", chunk.Path));
            decoded += length;
            if (decoded > _limits.MaxDecodedBytesPerExchange)
                return Abort(Limit("maxDecodedBytesPerExchange", chunk.Path));
        }

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Append(chunk, cancellationToken, work) is { } failure) return Abort(failure);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!complete) return new Outcome<StagedGenerationOutput?>.Success(null);
        if (_openArtifacts != 0)
            return Abort(Failure("extension.artifact_unclosed", "Generation completed with an artifact missing its end marker."));

        _settled = true;
        return new Outcome<StagedGenerationOutput?>.Success(new(Directory, _paths.Values
            .Select(entry => entry.Closed).OfType<StagedGenerationArtifact>(), _diagnostics.ToImmutable()));
    }

    private GenerationFailure? RetainDiagnostics(IReadOnlyList<Diagnostic> diagnostics, CancellationToken cancellationToken,
        BoundedTreeWorkBudget? work)
    {
        if (diagnostics.Count > _limits.MaxDiagnosticsPerExchange)
            return Limit("maxDiagnosticsPerExchange");
        foreach (var diagnostic in diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (work?.TryCharge(1) is false) return WorkLimit();
            var note = MepGenerationBackend.ToDiagnostic(diagnostic);
            // Charge the retained projection, including the record, range, collection slots and UTF-16 text.
            var charge = 256L + 2L * (note.Code.Length + (long)note.Message.Length
                + (note.Path?.Length ?? 0) + (note.Location?.Length ?? 0) + note.Severity.Length);
            if (!ReserveMetadata(charge)) return Limit("maxHostCacheBytes");
            _diagnostics.Add(note);
        }
        return null;
    }

    private GenerationFailure? Append(ModelArtifactChunk chunk, CancellationToken cancellationToken, BoundedTreeWorkBudget? work)
    {
        var path = chunk.Path;
        if (work?.TryCharge(1 + BoundedTreeWorkBudget.Blocks(2L * path.Length)) is false) return WorkLimit(path);
        if (MepGenerationBackend.UnsafePath(path) is { } reason) return Unsafe(path, reason);
        if (path.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
            return Unsafe(path, "The path contains a character that is invalid in a Windows filename.");

        // Count a path and its directories once. Refuse case aliases and file/directory conflicts on every host.
        for (var separator = path.IndexOf('/'); separator >= 0; separator = path.IndexOf('/', separator + 1))
        {
            if (work?.TryCharge(1 + BoundedTreeWorkBudget.Blocks(2L * separator)) is false) return WorkLimit(path);
            var component = path[..separator];
            if (_paths.TryGetValue(component, out var directory))
            {
                if (!directory.IsDirectory || directory.Path != component)
                    return Unsafe(path, "The artifact directory conflicts with an admitted path.");
            }
            else if (!ReservePath(component, true, out _))
                return Limit("maxHostCacheBytes", path);
        }

        var first = !_paths.TryGetValue(path, out var artifact);
        if (first)
        {
            if (!ReservePath(path, false, out artifact)) return Limit("maxHostCacheBytes", path);
            _openArtifacts++;
        }
        else if (artifact!.IsDirectory || artifact.Closed is not null || artifact.Path != path)
            return Unsafe(path, "The artifact is closed or conflicts with an admitted path.");

        var entry = artifact!;
        if (chunk.Offset != entry.Bytes)
            return Failure("extension.artifact_offset", $"Expected offset {entry.Bytes}, received {chunk.Offset}.", path);

        var length = chunk.DecodedByteLength;
        if (length > _limits.MaxArtifactBytes - entry.Bytes) return Limit("maxArtifactBytes", path);
        if (length > _limits.MaxTotalArtifactBytes - _artifactBytes) return Limit("maxTotalArtifactBytes", path);

        var chunkWork = 3 * BoundedTreeWorkBudget.Blocks(length);
        if (chunk.End) chunkWork += BoundedTreeWorkBudget.Blocks(entry.Bytes + length);
        if (work?.TryCharge(chunkWork) is false) return WorkLimit(path);
        var bytes = new byte[length];
        if (!Convert.TryFromBase64String(chunk.Content, bytes, out var written) || written != length
            || Convert.ToBase64String(bytes) != chunk.Content || !Sha256Digest.Compute(bytes).Equals(chunk.Sha256))
            return Integrity(path);

        var fullPath = Path.GetFullPath(Path.Combine(Directory, path));
        if (GenerationPublisher.LinkedPath(fullPath) is not null)
            return Unsafe(path, "The staging path contains a link.");

        if (!_disk.TryReserve(length)) return Limit("maxSessionDiskBytes", path);
        _reservedDiskBytes += length;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var output = new FileStream(fullPath, first ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (output.Length != entry.Bytes)
            return Failure("extension.artifact_io", "The staged file changed before the next chunk.", path);
        output.Position = entry.Bytes;
        output.Write(bytes);
        entry.Bytes += length;
        _artifactBytes += length;

        if (chunk.End)
        {
            output.Position = 0;
            entry.Closed = new(entry.Path, entry.Bytes, HashFile(output, cancellationToken));
            _openArtifacts--;
        }
        return null;
    }

    private static Sha256Digest HashFile(FileStream file, CancellationToken cancellationToken)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[4096];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = file.Read(buffer);
            if (read == 0) return Sha256Digest.OfHash(digest.GetHashAndReset());
            digest.AppendData(buffer[..read]);
        }
    }

    private bool ReservePath(string path, bool isDirectory, out Entry? entry)
    {
        // Reserve space up front for the map, record and full-file digest even for empty artifacts.
        var charge = 512L + path.Length * 2L;
        if (!ReserveMetadata(charge))
        {
            entry = null;
            return false;
        }
        entry = new Entry(path, isDirectory);
        _paths.Add(path, entry);
        return true;
    }

    private bool ReserveMetadata(long bytes)
    {
        if (bytes > _limits.MaxHostCacheBytes - _metadataBytes) return false;
        _metadataBytes += bytes;
        return true;
    }

    private Outcome<StagedGenerationOutput?> Abort(GenerationFailure failure)
    {
        if (Cleanup() is { } error) failure = failure with { Diagnostics = failure.Diagnostics.Add(error) };
        return new Outcome<StagedGenerationOutput?>.Failure(failure);
    }

    /// <summary>Removes the stage and releases its disk reservation. The owner may retry after an I/O failure.</summary>
    public GenerationDiagnostic? Cleanup()
    {
        _settled = true;
        if (_deleted) return null;
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(Directory)) != _parent
                || GenerationPublisher.LinkedPath(Directory) is not null)
                throw new IOException("The generation stage is no longer a private child of the task directory.");
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _cleanupError = new("extension.artifact_cleanup", error.Message, Directory);
            return _cleanupError;
        }

        _disk.Release(_reservedDiskBytes);
        _paths.Clear();
        _diagnostics.Clear();
        _deleted = true;
        _cleanupError = null;
        return null;
    }

    public void Dispose()
    {
        // Apply has already reported an unsuccessful cleanup. Leave retries to the task owner.
        if (_cleanupError is null) _ = Cleanup();
    }

    private static GenerationFailure Limit(string limit, string? path = null) =>
        Failure("extension.artifact_limit", $"Generated output exceeds {limit}.", path);

    private static GenerationFailure Unsafe(string path, string message) =>
        Failure("extension.unsafe_artifact", message, path);

    private static GenerationFailure Integrity(string path) =>
        Failure("extension.artifact_integrity", "The artifact chunk does not match its encoding, length or digest.", path);

    private static GenerationFailure WorkLimit(string? path = null) =>
        Failure("extension.bounded_work_limit", "Processing exceeds the negotiated maxSessionWorkUnits.", path);

    private static GenerationFailure Failure(string code, string message, string? path = null) =>
        GenerationFailure.One(GenerationFailureKind.Operational, code, message, path);
}
