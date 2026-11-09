// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.Compilation;
using Severity = Morphir.Compilation.DiagnosticSeverity;

namespace Morphir.Tooling.Features.Compile;

/// <summary>Publishes the written IR of a successful compile.</summary>
public interface ICompilePublisher
{
    /// <summary>Publishes <paramref name="ir"/>, the exact bytes that the writer wrote.</summary>
    Task<Checked<PublishedCompile>> PublishAsync(CompileSettings settings, ReadOnlyMemory<byte> ir, CancellationToken ct);
}

/// <summary>
/// Writes the IR to <c>&lt;out root&gt;/compile.dest/morphir-ir.json</c> with a replace that other readers never
/// see half done. The task record store (task records, <c>.dest</c> clearing, tombstones, locks and
/// <c>-o</c> installation) replaces this publisher.
/// </summary>
public sealed class OutRootCompilePublisher : ICompilePublisher
{
    /// <summary>The task directory of a compile under the out root.</summary>
    public const string Destination = "compile.dest";

    /// <summary>The file name of the published IR.</summary>
    public const string IrFile = "morphir-ir.json";

    /// <inheritdoc />
    public async Task<Checked<PublishedCompile>> PublishAsync(CompileSettings settings, ReadOnlyMemory<byte> ir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var directory = Path.Combine(settings.OutDirectory, Destination);
        var target = Path.Combine(directory, IrFile);
        var temporary = Path.Combine(directory, $".{IrFile}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temporary, ir, ct);
            File.Move(temporary, target, overwrite: true);
            return Checked<PublishedCompile>.Pass(new PublishedCompile(target));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return Checked<PublishedCompile>.Fail([Diagnostic.Create(Severity.Error, CompileCodes.PublishFailed,
                $"The IR could not be published to '{target}': {error.Message}", null)]);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
