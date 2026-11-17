// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Morphir.Files;
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
/// A compacted input, a file whose top level has a <c>$schema</c> member (the compaction envelope of
/// <c>ir/name-compaction</c>), is refused with <c>model.unsupported_context</c> until compacted files are read.
/// </remarks>
public sealed class DiskModelConversionStore : IModelConversionStore
{
    /// <summary>The depth of JSON that a single file may have; the codecs guard their own recursion.</summary>
    private const int MaxDepth = 4096;

    private const string SchemaMember = "$schema";

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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
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
                return ModelRead.Refused(ModelConvertNames.Compacted, Compacted(source.Given, schema));
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
        if (StoredModel.TryOfTreeRead(DocumentTreeStore.Read(ReadOptionsModule.defaults, files), out var model, out var notices, out var problems))
            return new ModelRead(model, ModelConvertNames.Plain, [], [.. notices.Select(notice => ConvertDiagnostic.Of(notice, source.Given))]);

        // A compacted file is not a tree file to the store, which refuses it; the envelope is the reason to report.
        var compacted = CompactedFiles(source.FullPath);
        return compacted.Count > 0
            ? ModelRead.Refused(ModelConvertNames.Compacted, [.. compacted.Select(file => Compacted(ModelConvertPaths.Join(source.Given, file.Path), file.Schema))])
            : ModelRead.Refused(ModelConvertNames.Plain, [.. problems.Select(problem => ConvertDiagnostic.Of(problem, source.Given))]);
    }

    private static ConvertDiagnostic Compacted(string location, string schema) =>
        ConvertDiagnostic.Failure(ModelConvertCodes.UnsupportedContext,
            $"The file is compacted (its $schema is '{schema}'). This version of morphir does not read compacted files yet.", location);

    /// <summary>The <c>$schema</c> of a compaction envelope at the top of <paramref name="root"/>, or null.</summary>
    private static string? Envelope(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(SchemaMember, out var schema)
            ? schema.ValueKind == JsonValueKind.String ? schema.GetString() ?? "" : schema.GetRawText()
            : null;

    /// <summary>The JSON files below <paramref name="root"/> that are compaction envelopes, by path from it, in path order.</summary>
    private static List<(string Path, string Schema)> CompactedFiles(string root)
    {
        var found = new List<(string, string)>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = MaxDepth });
                if (Envelope(document.RootElement) is { } schema)
                    found.Add((Path.GetRelativePath(root, path).Replace('\\', '/'), schema));
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                // The store has already reported this file.
            }
        }
        return found;
    }

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
        var staging = StagingPath(target.FullPath, ".tmp");
        try
        {
            File.WriteAllBytes(staging, bytes);
            File.Move(staging, target.FullPath, overwrite: false);
            return new ModelWrite([], []);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ModelWrite.Refused([ConvertDiagnostic.Failure(ModelConvertCodes.WriteFailed, error.Message, target.Given)]);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private static ModelWrite WriteTree(StoredModel model, ModelTarget target)
    {
        if (!StoredModel.TryGetWritten(DocumentTreeStore.Write(StoredModel.WriteOptions(target.Compact), model.Tree), out var tree, out var problems))
            return ModelWrite.Refused([.. problems.Select(problem => ConvertDiagnostic.Of(problem, target.Given))]);
        var staging = StagingPath(target.FullPath, "");
        try
        {
            var options = new PublishOptions(staging, ManifestMode.NoManifest);
            var published = VFileTreeModule.MatchPublish<(IReadOnlyList<string>? Files, string? Failure)>(options, tree,
                result => ([.. result.Written.Select(location => location.Replace('\\', '/'))], null),
                failure => (null, TreeWriteErrorModule.describe(failure)));
            if (published.Files is not { } files)
                return ModelWrite.Refused([ConvertDiagnostic.Failure(ModelConvertCodes.WriteFailed, published.Failure!, target.Given)]);
            // The output is absent or an empty directory, which the command checked before the read.
            if (Directory.Exists(target.FullPath)) Directory.Delete(target.FullPath);
            Directory.Move(staging, target.FullPath);
            return new ModelWrite(files, []);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ModelWrite.Refused([ConvertDiagnostic.Failure(ModelConvertCodes.WriteFailed, error.Message, target.Given)]);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>A new path beside <paramref name="output"/> for its staged content; the parent directory is created.</summary>
    private static string StagingPath(string output, string extension)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(output))!;
        Directory.CreateDirectory(parent);
        return Path.Combine(parent, $".{Path.GetFileName(Path.TrimEndingDirectorySeparator(output))}.convert-{Guid.NewGuid():N}{extension}");
    }
}
