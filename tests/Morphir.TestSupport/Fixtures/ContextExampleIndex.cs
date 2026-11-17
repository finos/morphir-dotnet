// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// One example of the parent's name-context examples (<c>docs/spec/ir/context/examples/cases.json</c>), as reference
/// data: a Morphir file, and what a reader must make of it.
/// </summary>
/// <param name="Id">The case id, such as <c>v4-type</c>.</param>
/// <param name="File">The example file.</param>
/// <param name="Expanded">The plain document a reader must produce, for a valid case.</param>
/// <param name="Ir">The IR version of the enclosed document: 3 or 4.</param>
/// <param name="Kind">The file kind: <c>single-file</c>, <c>manifest</c>, <c>module</c>, <c>type</c> or <c>value</c>.</param>
/// <param name="Expected"><c>valid</c>, or the diagnostic code a reader refuses the file with.</param>
/// <param name="SchemaValid">Whether the envelope's JSON Schema alone accepts the file.</param>
public sealed record ContextExample(
    string Id,
    FixtureEntry File,
    FixtureEntry? Expanded,
    int Ir,
    string Kind,
    string Expected,
    bool SchemaValid)
{
    /// <summary>Whether a reader accepts the file.</summary>
    public bool IsValid => Expected == "valid";
}

/// <summary>The parent's <c>cases.json</c> of the name-context examples.</summary>
/// <param name="SchemaUrl">The envelope's schema URL.</param>
/// <param name="MaxTableEntries">The most entries one context table may hold.</param>
/// <param name="MaxSpellingBytes">The most UTF-8 bytes one expanded spelling may have.</param>
/// <param name="Examples">Every case, in the index's order.</param>
public sealed record ContextExampleIndex(
    string SchemaUrl,
    int MaxTableEntries,
    int MaxSpellingBytes,
    IReadOnlyList<ContextExample> Examples)
{
    /// <summary>
    /// Reads the index, mapping each file it names to its pinned entry. A file the index names and the catalog does
    /// not pin is refused, so the index and the pin cannot drift apart.
    /// </summary>
    public static ContextExampleIndex Read(string json, IReadOnlyList<FixtureEntry> files, string directory)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        FixtureEntry Pinned(string path) =>
            files.FirstOrDefault(entry => entry.RelativePath == directory + path)
            ?? throw new InvalidOperationException($"cases.json names {path}, which the catalog does not pin.");

        var limits = root.GetProperty("limits");

        var examples = root.GetProperty("cases").EnumerateArray()
            .Select(item => new ContextExample(
                item.GetProperty("id").GetString()!,
                Pinned(item.GetProperty("file").GetString()!),
                item.TryGetProperty("expanded", out var expanded) ? Pinned(expanded.GetString()!) : null,
                item.GetProperty("ir").GetInt32(),
                item.GetProperty("kind").GetString()!,
                item.GetProperty("expected").GetString()!,
                item.GetProperty("schemaValid").GetBoolean()))
            .ToArray();

        return new ContextExampleIndex(
            root.GetProperty("schemaUrl").GetString()!,
            limits.GetProperty("maxTableEntries").GetInt32(),
            limits.GetProperty("maxSpellingBytes").GetInt32(),
            examples);
    }
}

/// <summary>
/// One fixed case of the parent's compaction policy (<c>morphir.compact/0.1.0-draft.1</c>), as reference data: a
/// plain document and the bytes a writer of the policy must produce from it.
/// </summary>
/// <param name="Id">The case id, such as <c>name-used-once</c>.</param>
/// <param name="Ir">The IR version of the document: 3 or 4.</param>
/// <param name="Kind">The file kind: <c>single-file</c>, <c>manifest</c>, <c>module</c>, <c>type</c> or <c>value</c>.</param>
/// <param name="Compacted">Whether the policy compacts the document. When it declines, the expected bytes are the
/// plain document in canonical JSON, with no envelope.</param>
/// <param name="Input">The plain document.</param>
/// <param name="Expected">The bytes the writer must produce.</param>
public sealed record PolicyCase(string Id, int Ir, string Kind, bool Compacted, FixtureEntry Input, FixtureEntry Expected);

/// <summary>The parent's <c>cases.json</c> of the compaction policy.</summary>
/// <param name="Policy">The policy's name, <c>morphir.compact/0.1.0-draft.1</c>.</param>
/// <param name="Cases">Every case, in the index's order.</param>
public sealed record PolicyCaseIndex(string Policy, IReadOnlyList<PolicyCase> Cases)
{
    /// <summary>
    /// Reads the index, mapping each case to its pinned <c>cases/&lt;id&gt;/input.json</c> and <c>expected.json</c>. A
    /// case whose files the catalog does not pin is refused.
    /// </summary>
    public static PolicyCaseIndex Read(string json, IReadOnlyList<FixtureEntry> files, string directory)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        FixtureEntry Pinned(string path) =>
            files.FirstOrDefault(entry => entry.RelativePath == directory + path)
            ?? throw new InvalidOperationException($"The policy's cases.json names {path}, which the catalog does not pin.");

        var cases = root.GetProperty("cases").EnumerateArray()
            .Select(item =>
            {
                var id = item.GetProperty("id").GetString()!;
                return new PolicyCase(
                    id,
                    item.GetProperty("ir").GetInt32(),
                    item.GetProperty("kind").GetString()!,
                    item.GetProperty("compacted").GetBoolean(),
                    Pinned($"cases/{id}/input.json"),
                    Pinned($"cases/{id}/expected.json"));
            })
            .ToArray();

        return new PolicyCaseIndex(root.GetProperty("policy").GetString()!, cases);
    }
}
