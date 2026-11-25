// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// A kit case that states both a canonical YAML and a canonical JSON spelling of one value: a vector of the canonical
/// YAML writer. <see cref="Yaml"/> has no final line feed.
/// </summary>
/// <param name="Feature">The feature file, such as <c>values.feature</c>.</param>
/// <param name="Id">The kit id, such as <c>values-0001</c>.</param>
/// <param name="Title">The scenario title after its id; an outline's example rows add <c>(examples)</c>.</param>
/// <param name="Yaml">The canonical YAML spelling.</param>
/// <param name="Json">The canonical JSON spelling.</param>
public sealed record KitCanonicalSpelling(string Feature, string Id, string Title, string Yaml, string Json)
{
    /// <summary>A name for the case that is unique in the kit, such as <c>values.feature: values-0001 Integer literal spelling</c>.</summary>
    public string Name => $"{Feature}: {Id} {Title}";

    /// <summary>The name of the case.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// Reads the canonical YAML and JSON spellings of a kit feature file as reference data: a scenario's YAML doc string
/// or one-line YAML step with its one-line JSON step, and the <c>YAML</c> and <c>JSON</c> rows of an outline's
/// <c>format</c> and <c>spelling</c> examples. It is not a kit runner, and a scenario with no such pair gives nothing.
/// </summary>
public static partial class KitSpellingReader
{
    /// <summary>The pairs of one feature file, in the kit's order.</summary>
    public static IReadOnlyList<KitCanonicalSpelling> Read(string feature, string text)
    {
        var spellings = new List<KitCanonicalSpelling>();
        var scenarios = ScenarioStart().Split(text.Replace("\r\n", "\n")).Where(block => block.StartsWith("  Scenario", StringComparison.Ordinal));

        foreach (var scenario in scenarios)
        {
            var heading = scenario.Split('\n')[0].Trim();
            var title = heading[(heading.IndexOf(':') + 1)..].Trim();
            var separator = title.IndexOf(' ');
            if (separator <= 0) throw new FormatException($"{feature}: a scenario title starts with its kit id: '{title}'.");
            var id = title[..separator];
            var rest = title[(separator + 1)..];

            var yaml = YamlOf(scenario);
            var json = CanonicalJson().Match(scenario);
            if (yaml is not null && json.Success) spellings.Add(new(feature, id, rest, yaml, json.Groups[1].Value.Trim()));

            var rows = scenario.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith('|')).Select(Cells).ToList();
            if (rows.Count > 1 && rows[0].Contains("format") && rows[0].Contains("spelling"))
            {
                var formatAt = rows[0].IndexOf("format");
                var spellingAt = rows[0].IndexOf("spelling");
                string? Spelling(string format) => rows.Skip(1).FirstOrDefault(row => row[formatAt] == format)?[spellingAt];
                if (Spelling("YAML") is { } exampleYaml && Spelling("JSON") is { } exampleJson)
                    spellings.Add(new(feature, id, rest + " (examples)", exampleYaml, exampleJson));
            }
        }

        return spellings;
    }

    private static string? YamlOf(string scenario)
    {
        var block = YamlDocString().Match(scenario);
        if (block.Success)
        {
            var margin = block.Groups[1].Value.Length;
            return string.Join('\n', block.Groups[2].Value.Split('\n').Select(line => line.Length >= margin ? line[margin..] : line.TrimStart()));
        }

        var inline = InlineYaml().Match(scenario);
        return inline.Success ? inline.Groups[1].Value.Trim() : null;
    }

    private static List<string> Cells(string row) => [.. row.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];

    [GeneratedRegex(@"\n(?=  Scenario)")]
    private static partial Regex ScenarioStart();

    [GeneratedRegex("its canonical YAML spelling is:[ ]*\\n([ ]*)\"\"\"yaml\\n(.*?)\\n[ ]*\"\"\"", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex YamlDocString();

    [GeneratedRegex(@"its canonical YAML spelling is (?!:)(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex InlineYaml();

    [GeneratedRegex(@"its canonical JSON spelling is (.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalJson();
}
