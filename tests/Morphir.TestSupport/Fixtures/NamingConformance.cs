// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace Morphir.TestSupport.Fixtures;

/// <summary>One round-trip name of the parent's naming corpus, in the shipped style.</summary>
/// <param name="Name">What the case shows, such as <c>trailing initialism</c>.</param>
/// <param name="Canonical">The canonical spelling, such as <c>value-in-USD</c>.</param>
/// <param name="LegacyArray">The classic v3 word array of the name, such as <c>["value","in","u","s","d"]</c>, or null
/// where the corpus records none (<c>FR2052A</c>, whose word array is ambiguous).</param>
public sealed record NamingNameCase(string Name, string Canonical, IReadOnlyList<string>? LegacyArray);

/// <summary>One input of the parent's naming corpus that some style refuses as a name.</summary>
/// <param name="Input">The text, such as <c>Usd</c>.</param>
/// <param name="ValidAsName">Whether the shipped style accepts the text as a name.</param>
public sealed record NamingRejectCase(string Input, bool ValidAsName);

/// <summary>
/// The parent's naming conformance corpus (<c>docs/spec/ir/fixtures/naming-conformance.json</c>), as reference data,
/// read in its shipped style: the canonical names, paths and FQNames, and the inputs it refuses as names.
/// </summary>
/// <param name="ShippedStyle">The style the corpus ships, <c>uppercase</c>.</param>
/// <param name="Names">Every round-trip case, in the corpus's order.</param>
/// <param name="Paths">Every canonical path, such as <c>US/FR2052A/data-tables</c>.</param>
/// <param name="FqNames">Every canonical FQName, such as <c>morphir/SDK:list#map</c>.</param>
/// <param name="Rejects">Every reject case, with its validity as a name in the shipped style.</param>
public sealed record NamingConformance(
    string ShippedStyle,
    IReadOnlyList<NamingNameCase> Names,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> FqNames,
    IReadOnlyList<NamingRejectCase> Rejects)
{
    /// <summary>Reads the corpus. A case with no spelling or validity in the shipped style is refused.</summary>
    public static NamingConformance Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var style = root.GetProperty("shippedStyle").GetString()!;

        string InStyle(JsonElement item, string member) =>
            item.GetProperty(member).TryGetProperty(style, out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()!
                : throw new FormatException($"A case of the naming corpus has no {member} in its shipped style '{style}'.");

        string[] Canonicals(string section) =>
            [.. root.GetProperty(section).EnumerateArray().Select(item => InStyle(item, "canonical"))];

        var names = root.GetProperty("roundTripCases").EnumerateArray()
            .Select(item => new NamingNameCase(
                item.GetProperty("name").GetString()!,
                InStyle(item, "canonical"),
                item.TryGetProperty("legacyArray", out var words)
                    ? [.. words.EnumerateArray().Select(word => word.GetString()!)]
                    : null))
            .ToArray();

        var rejects = root.GetProperty("rejectCases").EnumerateArray()
            .Select(item => new NamingRejectCase(
                item.GetProperty("input").GetString()!,
                item.GetProperty("validAs").TryGetProperty(style, out var valid) && valid.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? valid.GetBoolean()
                    : throw new FormatException($"A reject case of the naming corpus has no validity in its shipped style '{style}'.")))
            .ToArray();

        return new NamingConformance(style, names, Canonicals("pathCases"), Canonicals("fqNameCases"), rejects);
    }
}
