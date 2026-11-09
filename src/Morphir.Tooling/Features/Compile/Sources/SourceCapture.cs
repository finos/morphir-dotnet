using System.Collections.Immutable;
using System.Text;
using System.Text.Unicode;
using Morphir.Compilation;
using Morphir.Tooling.Features.Generate;
using Morphir.Tooling.Infrastructure;
using CompilationRange = Morphir.Compilation.Range;

namespace Morphir.Tooling.Features.Compile.Sources;

/// <summary>The budgets of one source capture. All the documents of a selection share them.</summary>
/// <param name="MaxBytes">The total bytes of all documents.</param>
/// <param name="MaxDocuments">The number of documents.</param>
/// <param name="MaxPathLength">The length of the absolute path of each document.</param>
public sealed record SourceReadLimits(long MaxBytes = 64 * 1024 * 1024, int MaxDocuments = 10_000, int MaxPathLength = 4000)
{
    /// <summary>The default budgets, the same as the budgets of <c>generate</c> input capture.</summary>
    public static SourceReadLimits Default { get; } = new();
}

/// <summary>One captured source document: its owned bytes and its UTF-8 text.</summary>
/// <param name="Entry">The owned bytes. <see cref="RetainedEntry.RelativePath"/> is the root-relative path.</param>
/// <param name="Text">The decoded text, without a UTF-8 byte order mark.</param>
public sealed record CapturedDocument(RetainedEntry Entry, string Text)
{
    /// <summary>The root-relative path, with '/' separators, as the selection gives it.</summary>
    public string Path => Entry.RelativePath;
}

/// <summary>The documents of a selection, captured in selection order.</summary>
public sealed record CapturedSources(SourceSelection Selection, ImmutableArray<CapturedDocument> Documents);

/// <summary>The diagnostic codes of source capture.</summary>
public static class SourceCaptureCodes
{
    /// <summary>A budget is zero or negative.</summary>
    public static readonly DiagnosticCode InvalidLimits = DiagnosticCode.Create("sources.invalid_limits");
    /// <summary>The documents exceed a budget. Capture stops.</summary>
    public static readonly DiagnosticCode LimitExceeded = DiagnosticCode.Create("sources.limit_exceeded");
    /// <summary>A document or one of its ancestors is a link, a device or another special entry.</summary>
    public static readonly DiagnosticCode UnsupportedEntry = DiagnosticCode.Create("sources.unsupported_entry");
    /// <summary>A document could not be read.</summary>
    public static readonly DiagnosticCode Unreadable = DiagnosticCode.Create("sources.unreadable");
    /// <summary>A document path is not valid on this system.</summary>
    public static readonly DiagnosticCode InvalidPath = DiagnosticCode.Create("sources.invalid_path");
    /// <summary>A document is not valid UTF-8.</summary>
    public static readonly DiagnosticCode InvalidUtf8 = DiagnosticCode.Create("sources.invalid_utf8");
}

/// <summary>
/// Reads the documents of a <see cref="SourceSelection"/> through the shared bounded capture core: one byte
/// budget for all documents, document and path-length budgets, and refusal of links and special entries (also
/// in ancestor directories). It decodes each document as strict UTF-8. Explicit inputs, directories and project
/// files all come here as a selection, so the same rules apply to each.
/// </summary>
/// <remarks>
/// A problem with one document does not stop the capture of the others, so one run reports every such
/// problem. A budget overflow stops the capture. Each diagnostic has a location whose URI is the
/// root-relative document path. Cancellation throws <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed class SourceCapture
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Func<string, bool> _isRegularFile;

    public SourceCapture() : this(UnixInputFile.IsRegular) { }

    internal SourceCapture(Func<string, bool> isRegularFile) => _isRegularFile = isRegularFile;

    /// <summary>Captures the documents of <paramref name="selection"/>.</summary>
    public async Task<Checked<CapturedSources>> CaptureAsync(SourceSelection selection, SourceReadLimits limits,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(limits);
        ct.ThrowIfCancellationRequested();
        var captureLimits = new CaptureLimits(limits.MaxBytes, limits.MaxDocuments, limits.MaxPathLength);
        if (captureLimits.Check(selection.Root) is not null)
            return Checked<CapturedSources>.Fail([
                Diagnostic.Create(DiagnosticSeverity.Error, SourceCaptureCodes.InvalidLimits,
                    "Source budgets must all be positive.", null)
            ]);

        var capture = new BoundedCapture(captureLimits, _isRegularFile, ct);
        var paths = selection.Documents;
        var documents = ImmutableArray.CreateBuilder<CapturedDocument>(paths.Count);
        var diagnostics = new List<Diagnostic>();
        foreach (var relative in paths)
        {
            try
            {
                capture.CountEntry("The selection exceeds the document budget.");
                var path = Path.Combine(selection.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                var attributes = capture.Open(path, out var fullPath);
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    diagnostics.Add(Error(SourceCaptureCodes.UnsupportedEntry, relative,
                        $"The source document '{relative}' is a directory."));
                    continue;
                }

                var entry = await capture.ReadFileAsync(fullPath, relative);
                if (Decode(entry) is { } text) documents.Add(new CapturedDocument(entry, text));
                else diagnostics.Add(InvalidUtf8(entry));
            }
            catch (Exception error) when (capture.TryClassify(error, out var failure))
            {
                switch (failure.Kind)
                {
                    case CaptureFailureKind.Cancelled:
                        throw;
                    case CaptureFailureKind.Limit:
                        diagnostics.Add(Error(SourceCaptureCodes.LimitExceeded, relative,
                            $"{failure.Message} The budget was exceeded at the source document '{relative}'."));
                        return Checked<CapturedSources>.Fail(diagnostics);
                    default:
                        diagnostics.Add(Error(CodeOf(failure.Kind), relative,
                            $"The source document '{relative}' was refused. {failure.Message}"));
                        break;
                }
            }
        }

        return diagnostics.Count == 0
            ? Checked<CapturedSources>.Pass(new CapturedSources(selection, documents.MoveToImmutable()))
            : Checked<CapturedSources>.Fail(diagnostics);
    }

    private static DiagnosticCode CodeOf(CaptureFailureKind kind) => kind switch
    {
        CaptureFailureKind.UnsupportedEntry => SourceCaptureCodes.UnsupportedEntry,
        CaptureFailureKind.Read => SourceCaptureCodes.Unreadable,
        CaptureFailureKind.InvalidPath => SourceCaptureCodes.InvalidPath,
        CaptureFailureKind.InvalidLimits => SourceCaptureCodes.InvalidLimits,
        CaptureFailureKind.Limit => SourceCaptureCodes.LimitExceeded,
        _ => throw new System.Diagnostics.UnreachableException(),
    };

    /// <summary>The text without a byte order mark, or null when the bytes are not valid UTF-8.</summary>
    private static string? Decode(RetainedEntry entry)
    {
        var bytes = entry.Memory.Span;
        if (bytes.StartsWith(Encoding.UTF8.Preamble)) bytes = bytes[Encoding.UTF8.Preamble.Length..];
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>An error at the first invalid byte. The position counts UTF-16 code units of the valid prefix.</summary>
    private static Diagnostic InvalidUtf8(RetainedEntry entry)
    {
        var bytes = entry.Memory.Span;
        if (bytes.StartsWith(Encoding.UTF8.Preamble)) bytes = bytes[Encoding.UTF8.Preamble.Length..];
        var chars = new char[bytes.Length];
        Utf8.ToUtf16(bytes, chars, out _, out var written, replaceInvalidSequences: false);
        var start = Position.FromOffset(new string(chars, 0, written), written);
        var range = CompilationRange.Create(start, Position.Create(start.Line, start.Character + 1));
        return Diagnostic.Create(DiagnosticSeverity.Error, SourceCaptureCodes.InvalidUtf8,
            $"The source document '{entry.RelativePath}' is not valid UTF-8. The first invalid byte is at {start.Line + 1}:{start.Character + 1}.",
            Location.Create(entry.RelativePath, range));
    }

    private static Diagnostic Error(DiagnosticCode code, string document, string message) =>
        Diagnostic.Create(DiagnosticSeverity.Error, code, message,
            Location.Create(document, CompilationRange.Create(Position.Create(0, 0), Position.Create(0, 0))));
}
