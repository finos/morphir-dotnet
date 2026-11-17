// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Morphir.Files;
using Morphir.IR.Files;
using Morphir.IR.Trees;

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
/// the output itself), or the diagnostics that refused the write, in which case nothing is at the output.
/// </summary>
public sealed record ModelWrite(IReadOnlyList<string> Files, IReadOnlyList<ConvertDiagnostic> Diagnostics)
{
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
    /// the file itself, or in a tree's <c>manifest.json</c>. It decodes nothing, so the command can tell that a plain
    /// input has nothing to do without reading the model. An input that cannot be read, or is not JSON, has none.
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
/// A file whose top level has a <c>$schema</c> member is in a compaction envelope (<c>ir/name-compaction</c>). An
/// envelope of a declared schema is a compacted input, refused with <c>model.unsupported_context</c> until
/// <c>convert</c> expands compacted files (add-model-convert 7.5); any other <c>$schema</c> is refused with
/// <c>context.undeclared_schema</c>, and the input is not called compacted. The tree store already expands a compacted
/// tree file, so a tree's compacted files are found before the store reads it, and a tree that has one is refused
/// whether or not the store reads it.
/// </remarks>
public sealed class DiskModelConversionStore : IModelConversionStore
{
    /// <summary>The depth of JSON that a single file may have; the codecs guard their own recursion.</summary>
    private const int MaxDepth = 4096;

    private const string SchemaMember = "$schema";

    private const string TreeManifest = "manifest.json";

    /// <summary>
    /// The envelope schema URLs that this version declares: those of the model store's reader of Morphir files
    /// (<c>ir-context</c> 0.1.0-draft.1 of the parent's <c>ir/name-compaction</c>).
    /// </summary>
    private static readonly string[] DeclaredSchemas = [.. MorphirFile.declaredSchemas.Select(url => url.OriginalString)];

    /// <inheritdoc />
    public ValueTask<bool> HasEnvelopeAsync(ModelSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var file = source.Layout == ModelLayout.DocumentTree ? Path.Combine(source.FullPath, TreeManifest) : source.FullPath;
        return ValueTask.FromResult(TopLevelSchemaOf(file) is not null);
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
        return ValueTask.FromResult(source.Layout == ModelLayout.DocumentTree ? ReadTree(source) : ReadSingleFile(source));
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
            return ModelRead.Refused(null, ConvertDiagnostic.Failure("invalid_json", error.Message, source.Given));
        }

        using (document)
        {
            var root = document.RootElement;
            if (Envelope(root) is { } schema)
                return IsDeclared(schema)
                    ? ModelRead.Refused(ModelConvertNames.Compacted, Compacted(source.Given, schema))
                    : ModelRead.Refused(null, Undeclared(source.Given, schema));
            return StoredModel.TryReadSingleFile(root, out var model, out var problems)
                ? new ModelRead(model, ModelConvertNames.Plain, [], [])
                : ModelRead.Refused(ModelConvertNames.Plain, [.. problems.Select(problem => ConvertDiagnostic.Of(problem, source.Given))]);
        }
    }

    private static ModelRead ReadTree(ModelSource source)
    {
        var loaded = VFileTreeModule.loadFromDisk(source.FullPath);
        if (loaded.IsError)
            return ModelRead.Refused(null, ConvertDiagnostic.Failure(ModelConvertCodes.ReadFailed, "The directory cannot be read: " + TreeLoadErrorModule.describe(loaded.ErrorValue) + ".", source.Given));
        var files = loaded.ResultValue;
        var compactedFiles = CompactedTreeFiles(source.FullPath);
        if (StoredModel.TryOfTreeRead(DocumentTreeStore.Read(ReadOptionsModule.defaults, files), out var model, out var notices, out var problems))
            return compactedFiles.Count == 0
                ? new ModelRead(model, ModelConvertNames.Plain, [], [.. notices.Select(notice => ConvertDiagnostic.Of(notice, source.Given))])
                : RefusedTree(source, [], compactedFiles);
        return RefusedTree(source, problems, compactedFiles);
    }

    /// <summary>
    /// The tree files below <paramref name="root"/> in an envelope of a declared schema, each as its path from the root
    /// with <c>/</c> and its schema, in ordinal order of the path. A tree file is the manifest, a module file or a type
    /// or value file. The tree store expands such a file; this version of <c>convert</c> refuses it.
    /// </summary>
    private static IReadOnlyList<(string File, string Schema)> CompactedTreeFiles(string root)
    {
        IEnumerable<string> candidates;
        try
        {
            candidates = [.. Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)];
        }
        catch (Exception error) when (IsFileSystemFailure(error))
        {
            // The store reports a tree that cannot be read.
            return [];
        }
        var found = new List<(string File, string Schema)>();
        foreach (var path in candidates)
        {
            var file = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (!IsTreeFileName(file)) continue;
            if (TopLevelSchemaOf(path) is { } schema && IsDeclared(schema)) found.Add((file, schema));
        }
        found.Sort((left, right) => string.CompareOrdinal(left.File, right.File));
        return found;
    }

    /// <summary>Whether the file at <paramref name="file"/> (a path from a tree's root) is a file of the tree.</summary>
    private static bool IsTreeFileName(string file)
    {
        var name = file[(file.LastIndexOf('/') + 1)..];
        return file == TreeManifest || name == "module.json"
            || name.EndsWith(".type.json", StringComparison.Ordinal) || name.EndsWith(".value.json", StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal of a tree. Each file of <paramref name="compactedFiles"/> is reported once, as compacted. For each
    /// other file that the store's errors name and that is in an envelope, the envelope is the reason to report, once
    /// for the file. The errors that name no enveloped file are kept. The input is compacted when a file is in a
    /// declared envelope, and its encoding is not stated when the only envelopes are undeclared.
    /// </summary>
    private static ModelRead RefusedTree(
        ModelSource source, IReadOnlyList<ConversionProblem> problems, IReadOnlyList<(string File, string Schema)> compactedFiles)
    {
        var envelopes = new Dictionary<string, string?>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<ConvertDiagnostic>();
        var compacted = compactedFiles.Count > 0;
        var undeclared = false;
        foreach (var (file, schema) in compactedFiles)
        {
            envelopes[file] = schema;
            reported.Add(file);
            diagnostics.Add(Compacted(ModelConvertPaths.Join(source.Given, file), schema));
        }
        foreach (var problem in problems)
        {
            if (problem.File is { } file && EnvelopeOfTreeFile(source.FullPath, file, envelopes) is { } schema)
            {
                if (!reported.Add(file)) continue;
                var location = ModelConvertPaths.Join(source.Given, file);
                if (IsDeclared(schema))
                {
                    compacted = true;
                    diagnostics.Add(Compacted(location, schema));
                }
                else
                {
                    undeclared = true;
                    diagnostics.Add(Undeclared(location, schema));
                }
                continue;
            }
            diagnostics.Add(ConvertDiagnostic.Of(problem, source.Given));
        }
        var encoding = compacted ? ModelConvertNames.Compacted : undeclared ? null : ModelConvertNames.Plain;
        return new ModelRead(null, encoding, diagnostics, []);
    }

    /// <summary>
    /// The <c>$schema</c> of the envelope of the tree file at <paramref name="file"/> (a path from
    /// <paramref name="root"/>), or null when it has none or cannot be read. Each file is read once.
    /// </summary>
    private static string? EnvelopeOfTreeFile(string root, string file, Dictionary<string, string?> envelopes)
    {
        if (envelopes.TryGetValue(file, out var known)) return known;
        string? schema = null;
        try
        {
            var path = Path.GetFullPath(Path.Combine(root, file));
            if (path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = MaxDepth });
                schema = Envelope(document.RootElement);
            }
        }
        catch (Exception error) when (IsFileSystemFailure(error) || error is JsonException)
        {
            // The store has already reported this file.
        }
        envelopes[file] = schema;
        return schema;
    }

    private static bool IsDeclared(string schema) => DeclaredSchemas.Contains(schema, StringComparer.Ordinal);

    private static ConvertDiagnostic Compacted(string location, string schema) =>
        ConvertDiagnostic.Failure(ModelConvertCodes.UnsupportedContext,
            $"The file is compacted (its $schema is '{schema}'). This version of morphir does not read compacted files yet.", location);

    private static ConvertDiagnostic Undeclared(string location, string schema) =>
        ConvertDiagnostic.Failure(ModelConvertCodes.UndeclaredSchema,
            $"The file has a top-level $schema '{schema}', which this version of morphir does not declare as a compaction envelope.", location);

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
        return ValueTask.FromResult(target.Layout == ModelLayout.DocumentTree ? WriteTree(model, target) : WriteSingleFile(model, target));
    }

    private static ModelWrite WriteSingleFile(StoredModel model, ModelTarget target)
    {
        if (!model.TryWriteSingleFile(target.Compact, out var bytes, out var problems))
            return ModelWrite.Refused([.. problems.Select(problem => ConvertDiagnostic.Of(problem, target.Given))]);
        string? staging = null;
        try
        {
            staging = StagingPath(target.FullPath, ".tmp");
            File.WriteAllBytes(staging, bytes);
            File.Move(staging, target.FullPath, overwrite: false);
            staging = null;
            return new ModelWrite([], []);
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
            Directory.Move(staging, target.FullPath);
            staging = null;
            return new ModelWrite(files, []);
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
