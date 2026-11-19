// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.TestSupport.Drivers;

/// <summary>Named in-memory document cases for a knowledge bundle validation scenario.</summary>
public static class KnowledgeBundleMother
{
    public const string MalformedConceptName = "broken.md";

    public static IReadOnlyDictionary<string, string> ProjectDocumentation { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md"] = "# Project documentation\n\nThis Markdown file is project documentation.\n",
        };

    public static IReadOnlyDictionary<string, string> MalformedFrontmatter { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MalformedConceptName] = "---\ntype: [\n---\n# Still present\n",
        };

    public static IReadOnlyDictionary<string, string> UnresolvedLink { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["revenue.md"] = "---\ntype: Metric\n---\n# Revenue\n\n[Unknown](missing.md)\n",
        };

    public static WorkspaceDirectory Write(IScenarioWorkspace workspace, IReadOnlyDictionary<string, string> documents)
    {
        var bundle = workspace.MakeDirectory("knowledge bundle");
        foreach (var (path, content) in documents)
            workspace.WriteText(bundle.File(path.Split('/')), content);
        return bundle;
    }
}
