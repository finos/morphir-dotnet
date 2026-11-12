// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using Morphir.TestSupport.Packaging;

namespace Morphir.TestSupport.Drivers;

/// <summary>
/// A copy of the folder of a host with each WASM engine library deleted: a host that starts without its engine
/// library. <paramref name="Deleted"/> is empty when the host carries no engine library file (the engine is linked
/// into a Native AOT executable, or a single-file bundle embeds it).
/// </summary>
/// <param name="Source">The file of the host that was copied.</param>
/// <param name="Artifact">The file of the host in the copy.</param>
/// <param name="Deleted">The engine library files deleted from the copy, relative to its folder.</param>
public sealed record HostCopy(string Source, string Artifact, IReadOnlyList<string> Deleted);

/// <summary>
/// The host copies of one run. Each host file is copied once, the first time a test asks, below a folder of the run
/// that <see cref="Dispose"/> removes. The real build output is never changed.
/// </summary>
public sealed class HostCopies : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<HostCopy>> _copies = new(StringComparer.Ordinal);
    private readonly Lazy<string> _root = new(() =>
        CanonicalPath.Of(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"morphir-host-copies-{Guid.NewGuid():N}")).FullName));

    /// <summary>The copy of the folder of <paramref name="artifact"/> with each engine library file deleted.</summary>
    public HostCopy WithoutEngineLibraries(string artifact) =>
        _copies.GetOrAdd(artifact, source => new Lazy<HostCopy>(() => Copy(source, Path.Combine(_root.Value, $"host {Guid.NewGuid():N}")))).Value;

    private static HostCopy Copy(string artifact, string to)
    {
        var from = Path.GetDirectoryName(Path.GetFullPath(artifact))!;
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));

        var deleted = new List<string>();
        foreach (var file in Directory.EnumerateFiles(to, "*", SearchOption.AllDirectories)
                     .Where(file => PackagedEngineLayout.IsEngineLibrary(Path.GetFileName(file))))
        {
            File.Delete(file);
            deleted.Add(Path.GetRelativePath(to, file).Replace('\\', '/'));
        }

        return new HostCopy(artifact, Path.Combine(to, Path.GetFileName(artifact)), deleted);
    }

    public void Dispose()
    {
        if (_root.IsValueCreated) TolerantDirectory.TryDelete(_root.Value);
    }
}
