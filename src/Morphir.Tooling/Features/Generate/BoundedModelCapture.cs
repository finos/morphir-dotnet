namespace Morphir.Tooling.Features.Generate;

/// <summary>Captures owned bytes before any codec runs. Enumeration and allocation share finite budgets.</summary>
public sealed class BoundedModelCapture
{
    private sealed class CaptureException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    public async ValueTask<Outcome<RetainedInput>> CaptureAsync(ModelInput input, ModelReadLimits limits, CancellationToken ct)
    {
        var current = input.Path;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (limits.MaxBytes <= 0 || limits.MaxDepth <= 0 || limits.MaxNodes <= 0 || limits.MaxEntries <= 0 || limits.MaxPathLength <= 0)
                return Fail(GenerationFailureKind.Invocation, "invalid-input-limits", "Input budgets must all be positive.", current);
            var root = Path.GetFullPath(input.Path);
            current = root;
            CheckPath(root);
            // A linked ancestor can redirect an otherwise ordinary file outside its lexical location.
            for (var ancestor = new DirectoryInfo(root).Parent; ancestor is not null; ancestor = ancestor.Parent)
                CheckAttributes(ancestor.Attributes);
            var attributes = File.GetAttributes(root);
            CheckAttributes(attributes);
            long remainingBytes = limits.MaxBytes;
            if (!attributes.HasFlag(FileAttributes.Directory))
                return new Outcome<RetainedInput>.Success(new RetainedInput.File(root, await Read(root, Path.GetFileName(root))));

            var entries = new List<RetainedEntry>();
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root, 0));
            var count = 0;
            while (pending.TryPop(out var directory))
            {
                ct.ThrowIfCancellationRequested();
                current = directory.Path;
                CheckAttributes(File.GetAttributes(directory.Path));
                foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
                {
                    current = path;
                    ct.ThrowIfCancellationRequested();
                    if (++count > limits.MaxEntries) Limit("The tree exceeds the entry budget.");
                    if (directory.Depth + 1 > limits.MaxDepth) Limit("The tree exceeds the depth budget.");
                    CheckPath(path);
                    var entryAttributes = File.GetAttributes(path);
                    CheckAttributes(entryAttributes);
                    if (entryAttributes.HasFlag(FileAttributes.Directory))
                        pending.Push((path, directory.Depth + 1));
                    else
                        entries.Add(await Read(path, Path.GetRelativePath(root, path).Replace('\\', '/')));
                }
            }
            ct.ThrowIfCancellationRequested();
            entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            return new Outcome<RetainedInput>.Success(new RetainedInput.Tree(root, entries));

            void CheckPath(string path)
            {
                if (path.Length > limits.MaxPathLength) Limit("The input exceeds the path-length budget.");
            }
            async Task<RetainedEntry> Read(string path, string relative)
            {
                current = path;
                ct.ThrowIfCancellationRequested();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                CheckAttributes(File.GetAttributes(stream.SafeFileHandle));
                if (!stream.CanSeek) throw new CaptureException("unsupported-input-entry", "Only regular seekable files are supported.");
                var length = stream.Length;
                if (length > remainingBytes || length > Array.MaxLength) Limit("The input exceeds the aggregate byte budget.");
                var bytes = new byte[(int)length];
                await stream.ReadExactlyAsync(bytes, ct);
                if (await stream.ReadAsync(new byte[1], ct) != 0) Limit("The input changed size during capture.");
                remainingBytes -= length;
                return new RetainedEntry(relative, bytes);
            }
        }
        catch (OperationCanceledException) { return Fail(GenerationFailureKind.Cancelled, "cancelled", "Input capture was cancelled.", current); }
        catch (CaptureException error) { return Fail(GenerationFailureKind.Semantic, error.Code, error.Message, current); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return Fail(GenerationFailureKind.Operational, "input-read", "The input entry could not be read.", current); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { return Fail(GenerationFailureKind.Invocation, "invalid-input-path", "The input path is invalid.", current); }
    }

    private static void CheckAttributes(FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new CaptureException("unsupported-input-entry", "Links and special input entries are unsupported.");
    }
    private static void Limit(string message) => throw new CaptureException("input-limit", message);
    private static Outcome<RetainedInput> Fail(GenerationFailureKind kind, string code, string message, string path) =>
        new Outcome<RetainedInput>.Failure(GenerationFailure.One(kind, code, message, path));
}
