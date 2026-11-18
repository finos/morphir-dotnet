// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// One file that <see cref="FixtureCatalog"/> owns and serves.
/// </summary>
/// <param name="Intent">What the file is for, in words: the name a test reaches it by.</param>
/// <param name="RelativePath">The file below <c>tests/Fixtures</c>, with <c>/</c> separators.</param>
/// <param name="Sha256">The SHA-256 of its bytes, in lower-case hex, as its provenance entry records it.</param>
/// <param name="Provenance">The <c>PROVENANCE.md</c> below <c>tests/Fixtures</c> that has the file's row.</param>
public sealed record FixtureEntry(string Intent, string RelativePath, string Sha256, string Provenance);

/// <summary>
/// A directory or a file below <c>tests/Fixtures</c> that a test project owned before the catalog. The guard test
/// accepts each file in it. A new fixture is an entry of the catalog instead.
/// </summary>
/// <param name="RelativePath">The directory (ending in <c>/</c>) or the file, below <c>tests/Fixtures</c>.</param>
/// <param name="Project">The test project below <c>tests/</c> whose tests check the files.</param>
public sealed record FixtureOwner(string RelativePath, string Project);

/// <summary>
/// One tree case of the kit's <c>document-tree.feature</c>, as reference data. The kit is not run: these are its
/// expectations, read through <see cref="FixtureCatalog.Kit"/>.
/// </summary>
/// <param name="Id">The kit id, such as <c>document-tree-0011</c>.</param>
/// <param name="Title">The rest of the scenario title.</param>
/// <param name="Files">The tree: each logical path (no extension) to the file's JSON text. A YAML spelling is
/// translated to JSON; a JSON spelling is kept as the kit writes it.</param>
/// <param name="CanonicalSingleFile">The single-file distribution the tree reads back as: the kit's canonical JSON
/// spelling, or its canonical YAML spelling translated to JSON when the case gives only that.</param>
/// <param name="Diagnostic">The diagnostic code of a case that a reader refuses.</param>
public sealed record KitTreeCase(
    string Id,
    string Title,
    IReadOnlyDictionary<string, string> Files,
    string? CanonicalSingleFile,
    string? Diagnostic)
{
    /// <summary>
    /// The kit marks the files <c>read-only</c>: a reader reads them as the canonical form, and a writer of that form
    /// does not write them back (decision 0014: <c>$meta</c> is ignored and never written).
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>The JSON document that a reader refuses with <see cref="Diagnostic"/>.</summary>
    public string? RejectedDocument { get; init; }
}

/// <summary>
/// One case of the kit's <c>document-tree.feature</c> that pins the spelling of one tree file and has no tree, such
/// as <c>document-tree-0001</c>.
/// </summary>
/// <param name="Id">The kit id.</param>
/// <param name="Title">The rest of the scenario title.</param>
/// <param name="LogicalPath">The file's name in a tree, with no extension: <c>manifest</c> for the distribution
/// manifest, <c>module</c> for a module manifest (the last segment of <c>pkg/&lt;package&gt;/&lt;module&gt;/module</c>).</param>
/// <param name="CanonicalJson">The file as a writer writes it: the kit's canonical JSON spelling, or its canonical YAML
/// spelling translated to JSON when the case gives only that.</param>
/// <param name="AcceptedSpellings">Other JSON spellings that a reader accepts as the same file, such as
/// <c>document-tree-0002</c>'s <c>module</c> for <c>path</c>.</param>
public sealed record KitFileSpelling(
    string Id,
    string Title,
    string LogicalPath,
    string CanonicalJson,
    IReadOnlyList<string> AcceptedSpellings);

/// <summary>The cases of one kit feature file, as <see cref="KitTreeCaseReader"/> reads them.</summary>
/// <param name="Trees">The cases with a tree or a diagnostic.</param>
/// <param name="FileSpellings">The cases that pin the spelling of one tree file.</param>
public sealed record KitFeature(IReadOnlyList<KitTreeCase> Trees, IReadOnlyList<KitFileSpelling> FileSpellings);
