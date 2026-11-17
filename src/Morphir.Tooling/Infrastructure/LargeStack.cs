// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Tooling.Infrastructure;

/// <summary>
/// Runs work that recurses once or more for each level of a model on a thread with a large stack: schema evaluation
/// and decoding in <c>model verify</c>, and the read of a single file in <c>model convert</c>, whose expansion of a
/// compacted file and whose decoders recurse for each level of nesting.
/// </summary>
internal static class LargeStack
{
    /// <summary>
    /// The stack of the thread. Schema evaluation recurses several frames deep for each level of the model.
    /// Measured on win-arm64 with optimized JIT code (nested <c>Apply</c> values): a 4 MB stack held 300 levels and
    /// overflowed at 400, and 8 MB held 700 and overflowed at 900, so a level takes about 10 to 13 KB, and the
    /// default stack of 1 MB holds fewer than 100 levels. 512 levels need about 7 MB; 64 MB leaves about nine times
    /// that for other platforms and code generators, and holds the 4096 levels of a single file that
    /// <c>model convert</c> reads, whose frames are smaller. The memory is reserved, and committed only as used.
    /// </summary>
    public const int Size = 64 * 1024 * 1024;

    /// <summary>Runs <paramref name="work"/> on a background thread named <paramref name="name"/> with the stack of <see cref="Size"/>.</summary>
    public static Task<T> RunAsync<T>(Func<T> work, string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }, Size)
        {
            IsBackground = true,
            Name = name,
        };
        thread.Start();
        return completion.Task;
    }
}
