using Morphir.Tooling.Infrastructure;

namespace Morphir.Tooling.Features.Generate;

/// <summary>Captures owned bytes before any codec runs. Enumeration and allocation share finite budgets.</summary>
public sealed class BoundedModelCapture
{
    private readonly Func<string, bool> _isRegularFile;
    public BoundedModelCapture() : this(UnixInputFile.IsRegular) { }
    internal BoundedModelCapture(Func<string, bool> isRegularFile) => _isRegularFile = isRegularFile;

    public async ValueTask<Outcome<RetainedInput>> CaptureAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct)
    {
        var captureLimits = new CaptureLimits(limits.MaxBytes, limits.MaxEntries, limits.MaxPathLength);
        if (ct.IsCancellationRequested)
            return Fail(new(CaptureFailureKind.Cancelled, "Input capture was cancelled.", input.Path));
        if (limits.MaxDepth <= 0 || limits.MaxNodes <= 0)
            return Fail(new(CaptureFailureKind.InvalidLimits, BoundedCapture.InvalidLimitsMessage, input.Path));
        if (captureLimits.Check(input.Path) is { } invalid) return Fail(invalid);

        var capture = new BoundedCapture(captureLimits, _isRegularFile, ct);
        try
        {
            var attributes = capture.Open(input.Path, out var root);
            if (!attributes.HasFlag(FileAttributes.Directory))
                return new Outcome<RetainedInput>.Success(
                    new RetainedInput.File(root, await capture.ReadFileAsync(root, Path.GetFileName(root))));
            return new Outcome<RetainedInput>.Success(
                new RetainedInput.Tree(root, await capture.ReadTreeAsync(root, limits.MaxDepth)));
        }
        catch (Exception error) when (capture.TryClassify(error, out var failure))
        {
            return Fail(failure);
        }
    }

    private static Outcome<RetainedInput> Fail(CaptureFailure failure)
    {
        var (kind, code) = failure.Kind switch
        {
            CaptureFailureKind.InvalidLimits => (GenerationFailureKind.Invocation, "invalid-input-limits"),
            CaptureFailureKind.Limit => (GenerationFailureKind.Semantic, "input-limit"),
            CaptureFailureKind.UnsupportedEntry => (GenerationFailureKind.Semantic, "unsupported-input-entry"),
            CaptureFailureKind.Read => (GenerationFailureKind.Operational, "input-read"),
            CaptureFailureKind.InvalidPath => (GenerationFailureKind.Invocation, "invalid-input-path"),
            CaptureFailureKind.Cancelled => (GenerationFailureKind.Cancelled, "cancelled"),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
        return new Outcome<RetainedInput>.Failure(GenerationFailure.One(kind, code, failure.Message, failure.Path));
    }
}
