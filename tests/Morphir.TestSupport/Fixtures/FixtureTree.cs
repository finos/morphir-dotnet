// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using Morphir.TestSupport.Drivers;

namespace Morphir.TestSupport.Fixtures;

/// <summary>One file of a <see cref="FixtureTree"/>: its location in the tree and the catalog entry that holds its bytes.</summary>
/// <param name="Location">The file's path from the root of the tree, with <c>/</c>.</param>
/// <param name="Entry">The entry of <see cref="FixtureCatalog"/> whose bytes the file has.</param>
public sealed record FixtureTreeFile(string Location, FixtureEntry Entry);

/// <summary>
/// A document tree that <see cref="FixtureCatalog"/> serves: files of the catalog, each at its location in the tree. A
/// test writes it into its workspace with <see cref="FixtureTrees.WriteTree"/>. A tree that differs from another in one
/// file is that tree <see cref="Without"/> the file or <see cref="WithFile"/> another entry, so the catalog pins only
/// the file that differs.
/// </summary>
public sealed class FixtureTree
{
    private FixtureTree(string intent, IEnumerable<FixtureTreeFile> files)
    {
        Intent = intent;
        Files = [.. files.OrderBy(file => file.Location, StringComparer.Ordinal)];
        var duplicate = Files.GroupBy(file => file.Location, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"The tree '{intent}' has two files at {duplicate.Key}.", nameof(files));
    }

    /// <summary>What the tree is for, in words.</summary>
    public string Intent { get; }

    /// <summary>The files, in the ordinal order of their locations.</summary>
    public IReadOnlyList<FixtureTreeFile> Files { get; }

    /// <summary>
    /// The tree of the entries below <paramref name="directory"/> of <c>tests/Fixtures</c>: each entry's location is its
    /// path below that directory.
    /// </summary>
    public static FixtureTree Below(string intent, string directory, IEnumerable<FixtureEntry> entries) =>
        new(intent, entries.Select(entry => entry.RelativePath.StartsWith(directory, StringComparison.Ordinal)
            ? new FixtureTreeFile(entry.RelativePath[directory.Length..], entry)
            : throw new ArgumentException($"The entry {entry.RelativePath} is not below {directory}.", nameof(entries))));

    /// <summary>The tree of <paramref name="files"/>, each a catalog entry at its location.</summary>
    public static FixtureTree Of(string intent, IEnumerable<FixtureTreeFile> files) => new(intent, files);

    /// <summary>The same files without the file at <paramref name="location"/>, which must be one of them.</summary>
    public FixtureTree Without(string location, string intent) =>
        Files.Any(file => file.Location == location)
            ? new FixtureTree(intent, Files.Where(file => file.Location != location))
            : throw new ArgumentException($"The tree '{Intent}' has no file at {location}.", nameof(location));

    /// <summary>The same files with the file at <paramref name="location"/>, which must be one of them, replaced by <paramref name="entry"/>.</summary>
    public FixtureTree WithFile(string location, FixtureEntry entry, string intent) =>
        Files.Any(file => file.Location == location)
            ? new FixtureTree(intent, Files.Select(file => file.Location == location ? new FixtureTreeFile(location, entry) : file))
            : throw new ArgumentException($"The tree '{Intent}' has no file at {location}.", nameof(location));

    /// <summary>The same files and one more: <paramref name="entry"/> at <paramref name="location"/>, which none of them is at.</summary>
    public FixtureTree With(string location, FixtureEntry entry, string intent) =>
        Files.All(file => file.Location != location)
            ? new FixtureTree(intent, [.. Files, new FixtureTreeFile(location, entry)])
            : throw new ArgumentException($"The tree '{Intent}' already has a file at {location}.", nameof(location));

    public override string ToString() => Intent;
}

/// <summary>
/// Where a faulty case of the catalog is wrong, so a test checks that a report names that place: the file of a tree,
/// the JSON location in the file, and the words that the report names.
/// </summary>
/// <param name="File">The file's location in the tree, or null for a single file.</param>
/// <param name="JsonPointer">The JSON Pointer (RFC 6901) of the faulty node in the file, or null when the fault is a
/// file and not a node in one.</param>
/// <param name="Names">The words that a report of the fault names, such as the name of a missing value.</param>
public sealed record FixtureFault(string? File, string? JsonPointer, IReadOnlyList<string> Names);

/// <summary>A tree of the catalog that has one fault, and where the fault is.</summary>
public sealed record FaultyFixtureTree(FixtureTree Tree, FixtureFault Fault);

/// <summary>A single-file model of the catalog that has one fault, and where the fault is.</summary>
public sealed record FaultyFixtureModel(FixtureEntry Entry, FixtureFault Fault);

/// <summary>Writes the trees of the catalog into a scenario workspace.</summary>
public static class FixtureTrees
{
    /// <summary>
    /// Writes each file of <paramref name="tree"/> below the directory <paramref name="segments"/> of the work area, with
    /// its bytes as pinned, and returns the directory.
    /// </summary>
    public static WorkspaceDirectory WriteTree(this IScenarioWorkspace workspace, FixtureTree tree, params string[] segments)
    {
        var directory = workspace.MakeDirectory(segments);
        foreach (var file in tree.Files)
            workspace.WriteBytes(directory.File(file.Location.Split('/')), FixtureCatalog.ReadBytes(file.Entry));
        return directory;
    }

    /// <summary>
    /// Writes the files of a kit tree (each logical path, such as <c>pkg/my-org/my-project/domain/module</c>, with its
    /// JSON text) below the directory <paramref name="segments"/> of the work area, each with the extension of the
    /// JSON profile, and returns the directory.
    /// </summary>
    public static WorkspaceDirectory WriteKitTree(this IScenarioWorkspace workspace, IReadOnlyDictionary<string, string> files, params string[] segments)
    {
        var directory = workspace.MakeDirectory(segments);
        foreach (var (logicalPath, json) in files)
            workspace.WriteText(directory.File((logicalPath + KitSpellingTrees.JsonExtension).Split('/')), json);
        return directory;
    }
}
