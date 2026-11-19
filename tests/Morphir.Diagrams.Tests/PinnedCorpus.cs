// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Morphir.TestSupport.Fixtures;

namespace Morphir.Diagrams.Tests;

/// <summary>The files of a pinned diagrams.mbt corpus, by their path below the corpus folder.</summary>
internal static class PinnedCorpus
{
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
}
