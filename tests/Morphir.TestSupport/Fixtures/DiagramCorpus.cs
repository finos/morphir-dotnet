// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace Morphir.TestSupport.Fixtures;

/// <summary>One diagram source of a pinned diagrams.mbt corpus.</summary>
/// <param name="Corpus">The corpus that holds the file.</param>
/// <param name="Path">The upstream path of the file below the corpus folder.</param>
/// <param name="Source">The exact text of the file.</param>
public sealed record CorpusDiagram(FixtureEntry Corpus, string Path, string Source)
{
    /// <summary>The corpus file name and the upstream path, which name the diagram in test output.</summary>
    public override string ToString() => $"{System.IO.Path.GetFileName(Corpus.RelativePath)}:{Path}";
}

/// <summary>Reads the pinned diagrams.mbt corpora that <see cref="FixtureCatalog.DiagramsMbt"/> serves.</summary>
public static class DiagramCorpus
{
    private static readonly string[] SourceExtensions = [".mmd", ".puml", ".bpmn"];

    /// <summary>Each file of the corpus as its path and exact text.</summary>
    public static IReadOnlyDictionary<string, string> Files(FixtureEntry corpus)
    {
        using var document = JsonDocument.Parse(FixtureCatalog.ReadText(corpus));

        return document.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
            file => file.GetProperty("path").GetString()!,
            file => file.GetProperty("text").GetString()!,
            StringComparer.Ordinal);
    }

    /// <summary>The text of the one file whose path ends with <paramref name="suffix"/>.</summary>
    public static string File(FixtureEntry corpus, string suffix) =>
        Files(corpus).Single(file => file.Key.EndsWith(suffix, StringComparison.Ordinal)).Value;

    /// <summary>
    /// Every Mermaid, PlantUML and BPMN source file of every pinned corpus, in catalog order and then by path.
    /// Some sources are refusal cases that do not parse; a caller that needs diagrams skips them.
    /// </summary>
    public static IReadOnlyList<CorpusDiagram> Sources() =>
    [
        .. FixtureCatalog.DiagramsMbt.Files.SelectMany(corpus => Files(corpus)
            .Where(file => SourceExtensions.Any(extension => file.Key.EndsWith(extension, StringComparison.Ordinal)))
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => new CorpusDiagram(corpus, file.Key, file.Value))),
    ];
}
