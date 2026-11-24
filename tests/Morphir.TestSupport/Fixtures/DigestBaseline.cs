// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// A fixture of named SHA-256 digests that pins output which must not change. A test computes the digests of the
/// current output and compares them with the fixture. To record a new fixture on unchanged code, set
/// <c>MORPHIR_RECORD_BASELINES</c> to a directory: the test then writes the file there under its fixture name and
/// fails, so that a variable left set never turns a comparison into a pass.
/// </summary>
public static class DigestBaseline
{
    /// <summary>The lowercase hex SHA-256 of UTF-8 text.</summary>
    public static string Digest(string text) => Digest(Encoding.UTF8.GetBytes(text));

    /// <summary>The lowercase hex SHA-256 of bytes.</summary>
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>The digests that a fixture pins, by name.</summary>
    public static IReadOnlyDictionary<string, string> Read(FixtureEntry fixture)
    {
        using var document = JsonDocument.Parse(FixtureCatalog.ReadText(fixture));
        return document.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.Ordinal);
    }

    /// <summary>The fixture text for digests: one JSON object, names in ordinal order, one name per line.</summary>
    public static string Format(IReadOnlyDictionary<string, string> digests)
    {
        var output = new StringBuilder("{\n");
        var names = digests.Keys.Order(StringComparer.Ordinal).ToArray();

        for (var index = 0; index < names.Length; index++)
        {
            output.Append("  ").Append(JsonSerializer.Serialize(names[index])).Append(": ").Append(JsonSerializer.Serialize(digests[names[index]]));
            output.Append(index + 1 < names.Length ? ",\n" : "\n");
        }

        return output.Append("}\n").ToString();
    }

    /// <summary>
    /// When <c>MORPHIR_RECORD_BASELINES</c> is set, writes the text as the fixture's file in that directory and
    /// returns a message for the test to fail with; otherwise returns null.
    /// </summary>
    public static string? Record(FixtureEntry fixture, string text)
    {
        var record = Environment.GetEnvironmentVariable("MORPHIR_RECORD_BASELINES");
        if (string.IsNullOrEmpty(record)) return null;

        var path = Path.Combine(record, Path.GetFileName(fixture.RelativePath));
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return $"Recorded {path}; unset MORPHIR_RECORD_BASELINES to compare.";
    }

    /// <summary>
    /// The names whose digest differs from the fixture, is missing from it or is not in the current output. When
    /// <c>MORPHIR_RECORD_BASELINES</c> is set, writes the current digests instead and reports that as the difference.
    /// </summary>
    public static IReadOnlyList<string> Differences(FixtureEntry fixture, IReadOnlyDictionary<string, string> current)
    {
        if (Record(fixture, Format(current)) is { } recorded) return [recorded];

        var pinned = Read(fixture);

        return
        [
            .. current.Where(item => !pinned.TryGetValue(item.Key, out var digest) || digest != item.Value).Select(item => item.Key),
            .. pinned.Keys.Where(name => !current.ContainsKey(name)).Select(name => name + " (no longer produced)"),
        ];
    }
}
