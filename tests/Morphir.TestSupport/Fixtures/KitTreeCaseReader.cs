// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// Reads the tree cases of a kit feature file as reference data: each scenario's id, title, tree file fences,
/// canonical single-file spelling and expected diagnostic. It is not a kit runner and knows no more of the kit's
/// step grammar than that. A step in a form it does not know raises a <see cref="FormatException"/> rather than
/// being skipped, as <c>tests/Morphir.Models.Tests/Json/V4/KitCases.fs</c> does, so a kit change cannot drop an
/// expectation unnoticed.
/// </summary>
public static partial class KitTreeCaseReader
{
    /// <summary>The cases of the feature text that have a tree or a diagnostic.</summary>
    public static IReadOnlyList<KitTreeCase> Read(string feature, string text) => ReadFeature(feature, text).Trees;

    /// <summary>
    /// Every case of the feature text: those with a tree or a diagnostic, and those that pin the spelling of one tree
    /// file (such as <c>document-tree-0001</c>). A scenario that is neither raises.
    /// </summary>
    public static KitFeature ReadFeature(string feature, string text)
    {
        var cases = new List<KitTreeCase>();
        var spellings = new List<KitFileSpelling>();
        Draft? current = null;
        var tags = new List<string>();
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('@'))
            {
                tags.AddRange(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                continue;
            }

            if (line.StartsWith("Feature:", StringComparison.Ordinal)) tags.Clear();
            if (line.StartsWith("\"\"\"", StringComparison.Ordinal))
                throw new FormatException($"{feature}:{index + 1}: a doc string that follows no step that takes one.");
            if (line.StartsWith("Scenario Outline:", StringComparison.Ordinal) || line.StartsWith("Scenario Template:", StringComparison.Ordinal)
                || line.StartsWith("Background:", StringComparison.Ordinal) || line.StartsWith("Rule:", StringComparison.Ordinal)
                || line.StartsWith("Examples:", StringComparison.Ordinal) || line.StartsWith('|'))
                throw new FormatException($"{feature}:{index + 1}: the tree case reader does not know '{line}' (a Scenario Outline, a table, a Background or a Rule).");

            if (line.StartsWith("Scenario:", StringComparison.Ordinal))
            {
                current?.Build(cases, spellings);
                var title = line["Scenario:".Length..].Trim();
                var separator = title.IndexOf(' ');
                if (separator <= 0) throw new FormatException($"{feature}:{index + 1}: a scenario title starts with its kit id: '{title}'.");
                var node = tags.Where(tag => tag.StartsWith("@node:", StringComparison.Ordinal)).Select(tag => tag["@node:".Length..]).LastOrDefault();
                current = new Draft(feature, title[..separator], title[(separator + 1)..], node);
                tags.Clear();
                continue;
            }

            var keyword = StepKeyword().Match(line);
            if (!keyword.Success) continue; // A tag, the feature line, a description line or a blank line.
            if (current is null) throw new FormatException($"{feature}:{index + 1}: a step outside a scenario: '{line}'.");

            var step = keyword.Groups[2].Value;
            string? DocString(string expected)
            {
                var opening = index + 1 < lines.Length ? lines[index + 1] : "";
                var column = opening.IndexOf("\"\"\"", StringComparison.Ordinal);
                if (column < 0 || opening[..column].Trim().Length > 0)
                    throw new FormatException($"{current.Id}: the step '{step}' takes a doc string, and none follows.");
                var kind = opening[(column + 3)..].Trim();
                if (kind != expected && !(expected == "*" && kind is "yaml" or "json"))
                    throw new FormatException($"{current.Id}: the step '{step}' has a doc string of kind '{kind}', not {(expected == "*" ? "yaml or json" : expected)}.");

                var body = new List<string>();
                for (index += 2; index < lines.Length; index++)
                {
                    var content = lines[index];
                    if (content.Trim() == "\"\"\"") return Content(current.Id, kind, body);
                    var indent = content.Length - content.TrimStart(' ').Length;
                    body.Add(content[Math.Min(indent, column)..]);
                }

                throw new FormatException($"{current.Id}: the doc string of the step '{step}' has no end.");
            }

            Match match;
            if ((match = TreeFile().Match(step)).Success)
                current.AddFile(match.Groups[3].Value, match.Groups[2].Value, match.Groups[1].Success, DocString("*")!);
            else if ((match = ReadsBack().Match(step)).Success)
                current.ReadsBack(match.Groups[1].Value);
            else if (CanonicalYaml().IsMatch(step))
                current.CanonicalYaml = DocString("yaml");
            else if ((match = CanonicalJson().Match(step)).Success)
                current.CanonicalJson = Json(current.Id, match.Groups[1].Value);
            else if ((match = AcceptedJson().Match(step)).Success)
                current.Accepted.Add(Json(current.Id, match.Groups[1].Value));
            else if ((match = RejectedJson().Match(step)).Success)
                current.Reject(Json(current.Id, match.Groups[1].Value), match.Groups[2].Value);
            else
                throw new FormatException($"{current.Id}: the tree case reader does not know the step '{step}'.");
        }

        current?.Build(cases, spellings);
        return new KitFeature(cases, spellings);
    }

    // The name in a tree of the one file whose spelling a case pins, by the case's node tag.
    private static string FileOf(string id, string? node) => node switch
    {
        "DistributionManifestFile" => "manifest",
        "ModuleManifestFile" => "module",
        _ => throw new FormatException($"{id}: a case that pins one tree file's spelling has the node tag of a manifest file, not '{node ?? "none"}'."),
    };

    private static string Content(string id, string kind, List<string> body)
    {
        var text = string.Join("\n", body);
        return kind == "yaml" ? YamlToJson.Translate(text) : Json(id, text);
    }

    private static string Json(string id, string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return text;
        }
        catch (JsonException error)
        {
            throw new FormatException($"{id}: the JSON spelling is not JSON: {text}", error);
        }
    }

    private sealed class Draft(string feature, string id, string title, string? node)
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
        private readonly List<string> order = [];
        private string? set;
        private bool? readOnly;
        private bool readsBack;
        private string? rejected;
        private string? diagnostic;

        public string Id => id;
        public string? CanonicalYaml { get; set; }
        public string? CanonicalJson { get; set; }
        public List<string> Accepted { get; } = [];

        public void AddFile(string fileSet, string path, bool isReadOnly, string json)
        {
            if (set is not null && set != fileSet) throw new FormatException($"{id}: the tree case reader knows one file set for each scenario, and this one has '{set}' and '{fileSet}'.");
            if (readOnly is { } mode && mode != isReadOnly) throw new FormatException($"{id}: the set '{fileSet}' mixes read-only files and other files.");
            if (!files.TryAdd(path, json)) throw new FormatException($"{id}: the tree file '{path}' is given twice.");
            set = fileSet;
            readOnly = isReadOnly;
            order.Add(path);
        }

        public void ReadsBack(string fileSet)
        {
            if (fileSet != set) throw new FormatException($"{id}: the step reads back the set '{fileSet}', and the scenario's set is '{set}'.");
            readsBack = true;
        }

        public void Reject(string document, string code)
        {
            if (diagnostic is not null) throw new FormatException($"{id}: the tree case reader knows one rejected document for each scenario.");
            rejected = document;
            diagnostic = code;
        }

        public void Build(List<KitTreeCase> cases, List<KitFileSpelling> spellings)
        {
            if (files.Count > 0 && !readsBack) throw new FormatException($"{id}: a scenario with tree files reads back the tree as the canonical form, and this one does not.");
            if (readsBack && CanonicalYaml is null && CanonicalJson is null) throw new FormatException($"{id}: a tree that reads back as the canonical form has a canonical spelling.");
            if (CanonicalYaml is not null && CanonicalJson is not null && YamlToJson.Compacted(CanonicalJson) != CanonicalYaml)
                throw new FormatException($"{id} in {feature}: its canonical YAML and JSON spellings disagree.");

            var canonical = CanonicalJson ?? CanonicalYaml;
            if (files.Count == 0 && diagnostic is null)
            {
                // A case that pins the spelling of one tree file.
                if (canonical is null) throw new FormatException($"{id}: the scenario has no tree, no diagnostic and no canonical spelling.");
                spellings.Add(new KitFileSpelling(id, title, FileOf(id, node), canonical, [.. Accepted]));
                return;
            }

            if (Accepted.Count > 0) throw new FormatException($"{id}: the tree case reader knows an accepted spelling only in a case that pins one tree file's spelling.");
            var tree = order.ToDictionary(path => path, path => files[path], StringComparer.Ordinal);
            cases.Add(new KitTreeCase(id, title, tree, files.Count > 0 ? canonical : null, diagnostic)
            {
                ReadOnly = readOnly == true,
                RejectedDocument = rejected,
            });
        }
    }

    [GeneratedRegex(@"^(Given|When|Then|And|But) (.*)$")]
    private static partial Regex StepKeyword();

    [GeneratedRegex("^the (read-only )?tree file \"([^\"]+)\" in set \"([^\"]+)\":$")]
    private static partial Regex TreeFile();

    [GeneratedRegex("^the \"([^\"]+)\" tree reads back as the canonical form$")]
    private static partial Regex ReadsBack();

    [GeneratedRegex("^its canonical YAML spelling is:$")]
    private static partial Regex CanonicalYaml();

    [GeneratedRegex("^its canonical JSON spelling is (.+)$")]
    private static partial Regex CanonicalJson();

    [GeneratedRegex("^a reader of JSON accepts (.+)$")]
    private static partial Regex AcceptedJson();

    [GeneratedRegex(@"^a reader of JSON rejects (.+) with (\w+)$")]
    private static partial Regex RejectedJson();
}
