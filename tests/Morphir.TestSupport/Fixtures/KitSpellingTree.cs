// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// A whole tree that holds the one file whose spelling a kit case pins (<see cref="KitFileSpelling"/>), so that a test
/// of a tool that reads and writes whole trees can check the spelling: it converts the tree to a single file and back,
/// and compares the file that it wrote with the case's canonical spelling.
/// </summary>
/// <param name="Spelling">The kit case.</param>
/// <param name="Files">Each file of the tree at its logical path (no extension), with its JSON text.</param>
/// <param name="SpelledFile">The logical path of the file whose spelling the case pins.</param>
public sealed record KitSpellingTree(KitFileSpelling Spelling, IReadOnlyDictionary<string, string> Files, string SpelledFile);

/// <summary>
/// Builds a <see cref="KitSpellingTree"/> from the kit's own data only. A manifest case is a tree of that manifest
/// alone. A module case is a tree with the manifest of the kit case of the same format version, the module file at
/// its place below <c>pkg/</c>, and a definition file for each name that the module lists. Each definition file is a
/// definition file of a kit tree case with its <c>name</c> changed: a type is <c>document-tree-0003</c>'s type, and a
/// value is <c>document-tree-0009</c>'s value <c>run</c>.
/// </summary>
public static partial class KitSpellingTrees
{
    /// <summary>The extension of the JSON profile of a tree file.</summary>
    public const string JsonExtension = ".json";

    /// <summary>The tree for <paramref name="spelling"/>, with the file in its canonical spelling.</summary>
    public static KitSpellingTree Canonical(KitFileSpelling spelling) => Of(spelling, spelling.CanonicalJson);

    /// <summary>The tree for <paramref name="spelling"/>, with the file in its one accepted spelling.</summary>
    public static KitSpellingTree Accepted(KitFileSpelling spelling) =>
        spelling.AcceptedSpellings.Count == 1
            ? Of(spelling, spelling.AcceptedSpellings[0])
            : throw new InvalidOperationException($"The kit case {spelling.Id} has {spelling.AcceptedSpellings.Count} accepted spellings, not one.");

    private static KitSpellingTree Of(KitFileSpelling spelling, string text)
    {
        switch (spelling.LogicalPath)
        {
            case "manifest":
                return new KitSpellingTree(spelling, new Dictionary<string, string>(StringComparer.Ordinal) { ["manifest"] = text }, "manifest");
            case "module":
                var canonical = JsonNode.Parse(spelling.CanonicalJson)!.AsObject();
                var manifest = ManifestOfVersion(spelling, canonical["formatVersion"]!);
                var package = JsonNode.Parse(manifest)!["package"]!.GetValue<string>();
                var directory = $"pkg/{package}/{canonical["path"]!.GetValue<string>()}/";
                var files = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["manifest"] = manifest,
                    [directory + "module"] = text,
                };
                foreach (var type in Names(canonical, "types"))
                    files[directory + Stem(type) + ".type"] = Renamed(Definition("document-tree-0003", ".type"), type);
                foreach (var value in Names(canonical, "values"))
                    files[directory + Stem(value) + ".value"] = Renamed(Definition("document-tree-0009", ".value"), value);
                return new KitSpellingTree(spelling, files, directory + "module");
            default:
                throw new InvalidOperationException($"The kit case {spelling.Id} pins the spelling of '{spelling.LogicalPath}', which no tree of this type holds.");
        }
    }

    // The canonical manifest of the manifest case whose formatVersion is the module's.
    private static string ManifestOfVersion(KitFileSpelling module, JsonNode formatVersion) =>
        FixtureCatalog.Kit.FileSpellings
            .Where(spelling => spelling.LogicalPath == "manifest")
            .Select(spelling => spelling.CanonicalJson)
            .FirstOrDefault(manifest => JsonNode.DeepEquals(JsonNode.Parse(manifest)!["formatVersion"], formatVersion))
        ?? throw new InvalidOperationException($"The kit has no manifest case of the format version of {module.Id}, {formatVersion.ToJsonString()}.");

    private static IEnumerable<string> Names(JsonObject module, string member) =>
        module[member]!.AsArray().Select(name => name!.GetValue<string>());

    // The text of the only definition file with this suffix in a kit tree case.
    private static string Definition(string id, string suffix)
    {
        var definitions = FixtureCatalog.Kit.Case(id).Files.Where(file => file.Key.EndsWith(suffix, StringComparison.Ordinal)).ToArray();
        return definitions.Length > 0
            ? definitions[0].Value
            : throw new InvalidOperationException($"The kit case {id} has no file that ends with {suffix}.");
    }

    private static string Renamed(string definition, string name)
    {
        var document = JsonNode.Parse(definition)!.AsObject();
        document["name"] = name;
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// The file stem of a name: the name itself when it is lower-case words with hyphens, which the escaping keeps;
    /// otherwise the stem of a kit tree file whose <c>name</c> is the name, as <c>document-tree-0003</c> gives
    /// <c>user-_id</c> for <c>user-ID</c>. The rules of escaping are the parent's; this type does not repeat them.
    /// </summary>
    private static string Stem(string name)
    {
        if (PlainName().IsMatch(name)) return name;
        foreach (var kitCase in FixtureCatalog.Kit.DocumentTree)
        {
            foreach (var (logicalPath, json) in kitCase.Files)
            {
                var file = logicalPath[(logicalPath.LastIndexOf('/') + 1)..];
                var dot = file.IndexOf('.');
                if (dot < 0) continue;
                if (JsonNode.Parse(json)?["name"] is JsonValue value && value.TryGetValue<string>(out var named) && named == name) return file[..dot];
            }
        }

        throw new InvalidOperationException($"No kit tree file is named '{name}', so its stem is not known.");
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex PlainName();
}
