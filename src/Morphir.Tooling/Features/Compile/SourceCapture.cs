// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using System.Text;
using Morphir.Compilation;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>Reads the selected documents (the effect edge before the frontend).</summary>
public interface ISourceCapture
{
    /// <summary>Reads every document of <paramref name="sources"/>, in order, as UTF-8 text.</summary>
    Task<Checked<ImmutableArray<CapturedDocument>>> CaptureAsync(SelectedSources sources, CancellationToken ct);
}

/// <summary>The limits of source capture.</summary>
public sealed record SourceCaptureLimits(long MaxDocumentBytes = 16 * 1024 * 1024, long MaxTotalBytes = 64 * 1024 * 1024,
    int MaxDocuments = 10_000, int MaxPathLength = 4000)
{
    /// <summary>The default limits.</summary>
    public static SourceCaptureLimits Default { get; } = new();
}

/// <summary>
/// Reads source documents as strict UTF-8 under byte, document and path limits. It refuses links and special
/// files, and it reports each refusal at its document. All documents are checked, so one run reports every
/// refused document.
/// </summary>
public sealed class FileSourceCapture(SourceCaptureLimits limits) : ISourceCapture
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>A capture with the default limits.</summary>
    public FileSourceCapture() : this(SourceCaptureLimits.Default) { }

    /// <inheritdoc />
    public async Task<Checked<ImmutableArray<CapturedDocument>>> CaptureAsync(SelectedSources sources, CancellationToken ct)
    {
        var documents = sources.Selection.Documents;
        var errors = new List<Diagnostic>();
        if (documents.Count > limits.MaxDocuments)
            return Fail([Error(null, CompileCodes.TooLarge, $"The source set has {documents.Count} documents. The limit is {limits.MaxDocuments}.")]);

        var captured = ImmutableArray.CreateBuilder<CapturedDocument>(documents.Count);
        long total = 0;
        foreach (var uri in documents)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(sources.AbsoluteRoot, uri));
            if (path.Length > limits.MaxPathLength)
            {
                errors.Add(Error(uri, CompileCodes.TooLarge, $"The path of the document '{uri}' is longer than {limits.MaxPathLength} characters."));
                continue;
            }

            FileInfo info = new(path);
            if (!info.Exists)
            {
                errors.Add(Error(uri, CompileCodes.MissingSource, $"The document '{uri}' does not exist."));
                continue;
            }
            if (info.LinkTarget is not null)
            {
                errors.Add(Error(uri, CompileCodes.LinkedSource, $"The document '{uri}' is a link. Links are not followed; name the target file."));
                continue;
            }
            if ((info.Attributes & (FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            {
                errors.Add(Error(uri, CompileCodes.LinkedSource, $"The document '{uri}' is not a regular file."));
                continue;
            }
            if (info.Length > limits.MaxDocumentBytes || total + info.Length > limits.MaxTotalBytes)
            {
                errors.Add(Error(uri, CompileCodes.TooLarge, $"The document '{uri}' is larger than the capture limit."));
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(path, ct);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                errors.Add(Error(uri, CompileCodes.ReadFailed, $"The document '{uri}' could not be read: {error.Message}"));
                continue;
            }
            total += bytes.LongLength;

            try
            {
                var text = Strict.GetString(bytes);
                captured.Add(new CapturedDocument(uri, text.Length > 0 && text[0] == '﻿' ? text[1..] : text));
            }
            catch (DecoderFallbackException)
            {
                errors.Add(Error(uri, CompileCodes.InvalidEncoding, $"The document '{uri}' is not valid UTF-8."));
            }
        }

        return errors.Count > 0 ? Fail(errors) : Checked<ImmutableArray<CapturedDocument>>.Pass(captured.MoveToImmutable());
    }

    private static Checked<ImmutableArray<CapturedDocument>> Fail(IEnumerable<Diagnostic> errors) =>
        Checked<ImmutableArray<CapturedDocument>>.Fail(errors);

    private static Diagnostic Error(string? uri, DiagnosticCode code, string message) =>
        Diagnostic.Create(Severity.Error, code, message, uri is null ? null : DocumentLocation(uri));

    /// <summary>The start of a document, for a diagnostic about the whole document.</summary>
    internal static Location DocumentLocation(string uri)
    {
        var start = Position.Create(0, 0);
        return Location.Create(uri, Morphir.Compilation.Range.Create(start, start));
    }
}
