// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Morphir.Files;
using Morphir.IR.Files;
using Morphir.IR.Trees;
using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.ModelConvert;

/// <summary>The input of a conversion: its full path, the path as the command line gave it, and its layout.</summary>
public sealed record ModelSource(string FullPath, string Given, ModelLayout Layout);

/// <summary>
/// The output of a conversion: its full path, the path as the command line gave it, its layout, and whether it is
/// compacted.
/// </summary>
public sealed record ModelTarget(string FullPath, string Given, ModelLayout Layout, bool Compact);

/// <summary>
/// What a read gave: the model, or the diagnostics that refused it; the input's encoding (<c>plain</c> or
/// <c>compacted</c>), which a refused input also states when it is known; and the notices of content the read dropped.
/// </summary>
public sealed record ModelRead(StoredModel? Model, string? Encoding, IReadOnlyList<ConvertDiagnostic> Diagnostics, IReadOnlyList<ConvertDiagnostic> Notices)
{
    /// <summary>A refused read.</summary>
    public static ModelRead Refused(string? encoding, params ConvertDiagnostic[] diagnostics) => new(null, encoding, diagnostics, []);
}

/// <summary>
/// What a write gave: each file written, as the path from the output with <c>/</c> (empty for a single file, which is
/// the output itself), or the diagnostics that refused the write, in which case nothing is at the output; and the
/// notices of a write that did not refuse it, such as a compaction that no alias paid for.
/// </summary>
public sealed record ModelWrite(IReadOnlyList<string> Files, IReadOnlyList<ConvertDiagnostic> Diagnostics)
{
    /// <summary>The notices of the write; none by default.</summary>
    public IReadOnlyList<ConvertDiagnostic> Notices { get; init; } = [];

    /// <summary>Whether a written file is in a compaction envelope: false for a plain write, or one that no alias paid for.</summary>
    public bool Compacted { get; init; }

    /// <summary>A refused write.</summary>
    public static ModelWrite Refused(IReadOnlyList<ConvertDiagnostic> diagnostics) => new([], diagnostics);
}

/// <summary>
/// Where <c>model convert</c> reads and writes models. The command decides what to convert and reports it; the store
/// does the I/O. A write leaves either the whole output or nothing at the output.
/// </summary>
public interface IModelConversionStore
{
    /// <summary>
    /// Whether the input has a top-level <c>$schema</c> member, the mark of a compaction envelope, declared or not: in
    /// the file itself, or in any tree file of a tree (its manifest, a module file, or a type or value file). It decodes
    /// nothing, so the command can tell that a plain input has nothing to do without reading the model. An input that
    /// cannot be read, or is not JSON, has none.
    /// </summary>
    ValueTask<bool> HasEnvelopeAsync(ModelSource source, CancellationToken ct);

    /// <summary>Reads the model at <paramref name="source"/>.</summary>
    ValueTask<ModelRead> ReadAsync(ModelSource source, CancellationToken ct);

    /// <summary>Writes <paramref name="model"/>, which is in the layout of <paramref name="target"/>, to it.</summary>
    ValueTask<ModelWrite> WriteAsync(StoredModel model, ModelTarget target, CancellationToken ct);
}

/// <summary>
/// The models on disk. A single file is decoded with the single-file codecs, a directory is read as a document tree
/// through <c>DocumentTreeStore</c> over <c>VFileTree.loadFromDisk</c>. A write goes to a staging sibling of the
/// output (a tree through the safe tree publication, a single file as one temporary file) and is renamed into place, so
/// a refused or failed write leaves nothing at the output.
/// </summary>
/// <remarks>
/// A file whose top level has a <c>$schema</c> member is in a compaction envelope (<c>ir/name-compaction</c>). A single
/// file in an envelope is read with <see cref="MorphirFile"/>, which checks the envelope and its context and expands the
/// document; the tree store expands the files of a tree. A refusal of an envelope or of a context keeps the reader's
/// <c>context.</c> code, such as <c>context.undeclared_schema</c> for a schema that this version does not declare, in
/// which case the input is not called compacted.
/// </remarks>
public sealed class DiskModelConversionStore : IModelConversionStore
{
    /// <summary>The depth of JSON that a single file may have; the codecs guard their own recursion.</summary>
    private const int MaxDepth = 4096;

    private const string SchemaMember = "$schema";

    private const string TreeManifest = "manifest.json";

    /// <inheritdoc />
    public ValueTask<bool> HasEnvelopeAsync(ModelSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (source.Layout != ModelLayout.DocumentTree) return ValueTask.FromResult(TopLevelSchemaOf(source.FullPath) is not null);
        foreach (var file in TreeFilesOf(source.FullPath))
        {
            ct.ThrowIfCancellationRequested();
            if (TopLevelSchemaOf(file) is not null) return ValueTask.FromResult(true);
        }
        return ValueTask.FromResult(false);
    }

    /// <summary>
    /// The <c>$schema</c> at the top level of the JSON file at <paramref name="file"/>, or null when it has none, cannot
    /// be read or is not JSON. A <c>$schema</c> that is not a string is given as the empty string, which no declared
    /// schema equals.
    /// </summary>
    private static string? TopLevelSchemaOf(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
            return TopLevelSchema(stream);
        }
        catch (Exception error) when (IsFileSystemFailure(error) || error is JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The <c>$schema</c> member at the top level of the JSON object on <paramref name="stream"/>, as
    /// <see cref="TopLevelSchemaOf"/> gives it. The text is read token by token in blocks, and no other value is decoded.
    /// </summary>
    private static string? TopLevelSchema(Stream stream)
    {
        var buffer = new byte[64 * 1024];
        var held = stream.ReadAtLeast(buffer.AsSpan(0, 3), 3, throwOnEndOfStream: false);
        if (held == 3 && buffer.AsSpan(0, 3).SequenceEqual((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) held = 0;
        var state = new JsonReaderState(new JsonReaderOptions { MaxDepth = MaxDepth });
        var atSchema = false;
        while (true)
        {
            var read = stream.Read(buffer, held, buffer.Length - held);
            var final = read == 0;
            var block = buffer.AsSpan(0, held + read);
            var reader = new Utf8JsonReader(block, final, state);
            while (reader.Read())
            {
                if (atSchema) return reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : "";
                if (reader.CurrentDepth == 0 && reader.TokenType != JsonTokenType.StartObject) return null;
                if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals(SchemaMember)) atSchema = true;
            }
            if (final) return atSchema ? "" : null;
            state = reader.CurrentState;
            var consumed = (int)reader.BytesConsumed;
            block[consumed..].CopyTo(buffer);
            held = block.Length - consumed;
            // A token longer than the buffer: grow it, so that the next block holds the whole token.
            if (held == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
        }
    }

    /// <inheritdoc />
    public ValueTask<ModelRead> ReadAsync(ModelSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A single file is read to a depth of 4096, and its expansion and decoders recurse for each level.
        return source.Layout == ModelLayout.DocumentTree
            ? ValueTask.FromResult(ReadTree(source))
            : new ValueTask<ModelRead>(LargeStack.RunAsync(() => ReadSingleFile(source), "morphir model convert read", ct));
    }

    private static ModelRead ReadSingleFile(ModelSource source)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(source.FullPath);
        }
        catch (Exception error) when (IsFileSystemFailure(error))
        {
            return ModelRead.Refused(null, ConvertDiagnostic.Failure(ModelConvertCodes.ReadFailed, error.Message, source.Given));
        }

        var content = bytes.AsMemory();
        if (content.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) content = content[3..];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException error)
        {
            // A file in an envelope is refused by the reader of Morphir files, which tells a file nested too deeply
            // (depth_limit_exceeded) from one that is not JSON.
            if (EnvelopeOfText(content) is { } envelope) return ReadCompactedSingleFile(source, bytes, envelope);
            return ModelRead.Refused(null, IsJsonAtAnyDepth(content.Span)
                ? ConvertDiagnostic.Failure(DepthLimitExceeded, $"The file is nested more than {MaxDepth} levels deep.", source.Given)
                : ConvertDiagnostic.Failure("invalid_json", error.Message, source.Given));
        }

        using (document)
        {
            var root = document.RootElement;
            return Envelope(root) is { } schema
                ? ReadCompactedSingleFile(source, bytes, schema)
                : ReadPlainSingleFile(source, root, ModelConvertNames.Plain);
        }
    }

    /// <summary>
    /// Reads a single file in an envelope through <see cref="MorphirFile"/>: the envelope and its context are checked
    /// and the document is expanded, so the model is read from the plain document. A refusal of the envelope or of its
    /// context keeps its <c>context.</c> code, and a file nested deeper than <see cref="MaxDepth"/> is
    /// <c>depth_limit_exceeded</c>. The input is compacted when its schema is declared, and its encoding is
    /// not stated otherwise.
    /// </summary>
    private static ModelRead ReadCompactedSingleFile(ModelSource source, byte[] bytes, string schema)
    {
        var encoding = MorphirFile.IsDeclared(schema) ? ModelConvertNames.Compacted : null;
        // A compacted file is read as deep as a plain one, as written and once expanded.
        var expanded = MorphirFile.ReadPlainDocument(ContextLimitsModule.draft1.WithMaxDepth(MaxDepth), source.Given, bytes);
        return expanded.IsOk
            ? ReadPlainSingleFile(source, expanded.ResultValue, encoding)
            : ModelRead.Refused(encoding, [.. expanded.ErrorValue.Select(error => ConvertDiagnostic.Failure(error.Code, error.Message, source.Given))]);
    }

    /// <summary>The code of a file nested more deeply than a reader reads or a writer writes.</summary>
    private const string DepthLimitExceeded = "depth_limit_exceeded";

    /// <summary>
    /// Whether <paramref name="content"/> is JSON once no limit of depth applies: after a parse with a limit failed,
    /// it failed on the depth. A reader keeps no stack per level.
    /// </summary>
    private static bool IsJsonAtAnyDepth(ReadOnlySpan<byte> content)
    {
        try
        {
            var reader = new Utf8JsonReader(content, new JsonReaderOptions { MaxDepth = int.MaxValue });
            while (reader.Read())
            {
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The top-level <c>$schema</c> of JSON text that does not parse whole, if its start shows one.</summary>
    private static string? EnvelopeOfText(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            return TopLevelSchema(stream);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ModelRead ReadPlainSingleFile(ModelSource source, JsonElement root, string? encoding) =>
        StoredModel.TryReadSingleFile(root, out var model, out var problems)
            ? new ModelRead(model, encoding, [], [])
            : ModelRead.Refused(encoding, [.. problems.Select(problem => ConvertDiagnostic.Of(problem, source.Given))]);

    /// <summary>
    /// Reads a tree through the document-tree store, which expands each compacted tree file and lists it in the read's
    /// report. The input is compacted when any of its files is. A refused read lists no compacted file, so its encoding
    /// comes from the envelopes of the tree files: compacted when one is in a declared envelope, not stated when the only
    /// envelopes are undeclared, and plain otherwise.
    /// </summary>
    private static ModelRead ReadTree(ModelSource source)
    {
        var loaded = VFileTreeModule.loadFromDisk(source.FullPath);
        if (loaded.IsError)
            return ModelRead.Refused(null, ConvertDiagnostic.Failure(ModelConvertCodes.ReadFailed, "The directory cannot be read: " + TreeLoadErrorModule.describe(loaded.ErrorValue) + ".", source.Given));
        var read = DocumentTreeStore.Read(ReadOptionsModule.defaults, loaded.ResultValue);
        if (StoredModel.TryOfTreeRead(read, out var model, out var notices, out var compactedFiles, out var problems))
            return new ModelRead(model, compactedFiles.Count > 0 ? ModelConvertNames.Compacted : ModelConvertNames.Plain, [],
                [.. notices.Select(notice => ConvertDiagnostic.Of(notice, source.Given))]);
        var schemas = TreeFilesOf(source.FullPath).Select(TopLevelSchemaOf).OfType<string>().ToArray();
        var encoding = schemas.Any(MorphirFile.IsDeclared) ? ModelConvertNames.Compacted : schemas.Length > 0 ? null : ModelConvertNames.Plain;
        return ModelRead.Refused(encoding, [.. problems.Select(problem => ConvertDiagnostic.Of(problem, source.Given))]);
    }

    /// <summary>
    /// The full path of each tree file below <paramref name="root"/>, in the ordinal order of its path from the root: the
    /// manifest, each module file and each type or value file. A directory that cannot be listed has none; the store
    /// reports it.
    /// </summary>
    private static IReadOnlyList<string> TreeFilesOf(string root)
    {
        try
        {
            return [.. Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(path => (Path: path, File: Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')))
                .Where(file => IsTreeFileName(file.File))
                .OrderBy(file => file.File, StringComparer.Ordinal)
                .Select(file => file.Path)];
        }
        catch (Exception error) when (IsFileSystemFailure(error))
        {
            return [];
        }
    }

    /// <summary>Whether the file at <paramref name="file"/> (a path from a tree's root) is a file of the tree.</summary>
    private static bool IsTreeFileName(string file)
    {
        var name = file[(file.LastIndexOf('/') + 1)..];
        return file == TreeManifest || name == "module.json"
            || name.EndsWith(".type.json", StringComparison.Ordinal) || name.EndsWith(".value.json", StringComparison.Ordinal);
    }

    /// <summary>
    /// The <c>$schema</c> of an envelope at the top of <paramref name="root"/>, or null. A <c>$schema</c> that is not
    /// a string is given as its JSON text, which no declared schema equals.
    /// </summary>
    private static string? Envelope(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(SchemaMember, out var schema)
            ? schema.ValueKind == JsonValueKind.String ? schema.GetString() ?? "" : schema.GetRawText()
            : null;

    /// <inheritdoc />
    public ValueTask<ModelWrite> WriteAsync(StoredModel model, ModelTarget target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // The writer policy walks a single file once for each level of nesting, as a read does.
        return target.Layout == ModelLayout.DocumentTree
            ? ValueTask.FromResult(WriteTree(model, target))
            : new ValueTask<ModelWrite>(LargeStack.RunAsync(() => WriteSingleFile(model, target), "morphir model convert write", ct));
    }

    private static ModelWrite WriteSingleFile(StoredModel model, ModelTarget target)
    {
        if (!model.TryWriteSingleFile(target.Compact, out var bytes, out var compacted, out var problems))
            return ModelWrite.Refused([.. problems.Select(problem => ConvertDiagnostic.Of(problem, target.Given))]);
        IReadOnlyList<ConvertDiagnostic> notices = target.Compact && !compacted
            ? [CompactionDeclined($"No alias was worth its cost under {CompactionPolicy.Id}, so the file is written as its plain document in canonical JSON.", target.Given)]
            : [];
        string? staging = null;
        try
        {
            staging = StagingPath(target.FullPath, ".tmp");
            File.WriteAllBytes(staging, bytes);
            MoveFileIntoPlace(staging, target.FullPath);
            staging = null;
            return new ModelWrite([], []) { Notices = notices, Compacted = compacted };
        }
        catch (Exception error) when (IsFileSystemFailure(error))
        {
            return WriteFailed(target, error);
        }
        finally
        {
            if (staging is not null) DeleteStaging(staging);
        }
    }

    private static ModelWrite WriteTree(StoredModel model, ModelTarget target)
    {
        if (!StoredModel.TryGetWritten(DocumentTreeStore.Write(StoredModel.WriteOptions(target.Compact), model.Tree), out var tree, out var problems))
            return ModelWrite.Refused([.. problems.Select(problem => ConvertDiagnostic.Of(problem, target.Given))]);
        string? staging = null;
        try
        {
            staging = StagingPath(target.FullPath, "");
            var options = new PublishOptions(staging, ManifestMode.NoManifest);
            var published = VFileTreeModule.MatchPublish<(IReadOnlyList<string>? Files, string? Failure)>(options, tree,
                result => ([.. result.Written.Select(location => location.Replace('\\', '/'))], null),
                failure => (null, TreeWriteErrorModule.describe(failure)));
            if (published.Files is not { } files)
                return ModelWrite.Refused([ConvertDiagnostic.Failure(ModelConvertCodes.WriteFailed, published.Failure!, target.Given)]);
            // The output is absent or an empty directory, which the command checked before the read.
            if (Directory.Exists(target.FullPath)) Directory.Delete(target.FullPath);
            MoveTreeIntoPlace(staging, target.FullPath);
            staging = null;
            return new ModelWrite(files, []) { Notices = TreeNotices(tree, files.Count, target), Compacted = AnyCompacted(tree, files.Count, target) };
        }
        catch (Exception error) when (IsFileSystemFailure(error))
        {
            return WriteFailed(target, error);
        }
        finally
        {
            if (staging is not null) DeleteStaging(staging);
        }
    }

    /// <summary>
    /// The notice of a compacted tree with files that no alias paid for: how many of its files are the plain document
    /// in canonical JSON, at the output. None for a plain write, or when every file has an alias worth its cost.
    /// </summary>
    private static IReadOnlyList<ConvertDiagnostic> TreeNotices(VFileTree tree, int files, ModelTarget target)
    {
        var count = target.Compact ? DocumentTreeStore.PlainFilesOf(tree).Count : 0;
        if (count == 0)
            return [];
        return [CompactionDeclined(
            $"No alias was worth its cost under {CompactionPolicy.Id} in {count} of the {files} files, so they are written as their plain documents in canonical JSON.",
            target.Given)];
    }

    /// <summary>Whether a compacted write of a tree put a file in an envelope: some file is not one that it left plain.</summary>
    private static bool AnyCompacted(VFileTree tree, int files, ModelTarget target) =>
        target.Compact && DocumentTreeStore.PlainFilesOf(tree).Count < files;

    private static ConvertDiagnostic CompactionDeclined(string message, string location) =>
        new(ModelConvertCodes.CompactionDeclined, ConvertDiagnostic.Info, message, location);

    /// <summary>
    /// Moves the staged single file at <paramref name="staging"/> to <paramref name="output"/>, which must not exist,
    /// as <see cref="MoveIntoPlace"/> does. <paramref name="afterWait"/> runs after each wait, for tests.
    /// </summary>
    internal static void MoveFileIntoPlace(string staging, string output, Action? afterWait = null) =>
        MoveIntoPlace(() => File.Move(staging, output, overwrite: false), output, afterWait);

    /// <summary>
    /// Moves the staged tree directory at <paramref name="staging"/> to <paramref name="output"/>, which must not exist,
    /// as <see cref="MoveIntoPlace"/> does. A directory move never replaces or merges into a directory at the output.
    /// <paramref name="afterWait"/> runs after each wait, for tests.
    /// </summary>
    internal static void MoveTreeIntoPlace(string staging, string output, Action? afterWait = null) =>
        MoveIntoPlace(() => Directory.Move(staging, output), output, afterWait);

    /// <summary>
    /// Runs <paramref name="move"/>, which puts the staged output at <paramref name="output"/>. On Windows, while another
    /// process holds a staged file open (a virus scanner or a search indexer does, just after a file is written), the
    /// move waits a bounded time (<see cref="FileReplacement"/>), as the publication of a tree's files does. Before each
    /// further attempt it checks again that nothing is at the output, since the wait gives another writer time to put
    /// something there; if something is, the move fails with an <see cref="IOException"/> and leaves it.
    /// </summary>
    private static void MoveIntoPlace(Action move, string output, Action? afterWait)
    {
        var moved = FileReplacement.Replace(move, () =>
        {
            afterWait?.Invoke();
            return !File.Exists(output) && !Directory.Exists(output);
        });
        if (!moved) throw new IOException($"Something appeared at '{output}' while the move of the output waited for another handle; it is left as it is.");
    }

    private static ModelWrite WriteFailed(ModelTarget target, Exception error) =>
        ModelWrite.Refused([ConvertDiagnostic.Failure(ModelConvertCodes.WriteFailed, error.Message, target.Given)]);

    /// <summary>
    /// Deletes the staging file or directory at <paramref name="path"/>, as far as the file system lets it. A delete
    /// that fails (on Windows, a file that another process holds open) is left: the write's own result stands, and the
    /// sibling keeps its <c>.&lt;output&gt;.convert-</c> name.
    /// </summary>
    internal static void DeleteStaging(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Best effort: see the summary.
        }
    }

    /// <summary>A new path beside <paramref name="output"/> for its staged content; the parent directory is created.</summary>
    private static string StagingPath(string output, string extension)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(output);
        var parent = Path.GetDirectoryName(trimmed) ?? throw new IOException($"The output '{output}' has no parent directory to stage it in.");
        Directory.CreateDirectory(parent);
        return Path.Combine(parent, $".{Path.GetFileName(trimmed)}.convert-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>
    /// An exception of a file-system call on a path: the path cannot be used (<see cref="ArgumentException"/>,
    /// <see cref="NotSupportedException"/>), is too long (<see cref="PathTooLongException"/>, an
    /// <see cref="IOException"/>), or the call failed or was denied.
    /// </summary>
    internal static bool IsFileSystemFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
