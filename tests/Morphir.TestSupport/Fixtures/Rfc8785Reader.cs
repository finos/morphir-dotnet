// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// One row of RFC 8785 Table 1 (Appendix B): an IEEE 754 double and the JSON text that JCS writes for it.
/// </summary>
/// <param name="Ieee754">The double's 64 bits, as the RFC gives them in hexadecimal.</param>
/// <param name="Json">The JSON form, or <see langword="null"/> where the RFC leaves it empty: NaN and Infinity, which a
/// JCS writer refuses.</param>
/// <param name="Comment">The row's comment, without its note number, or empty.</param>
public sealed record Rfc8785NumberSample(ulong Ieee754, string? Json, string Comment)
{
    /// <summary>The double that <see cref="Ieee754"/> holds.</summary>
    public double Value => BitConverter.Int64BitsToDouble(unchecked((long)Ieee754));
}

/// <summary>The examples of RFC 8785, as <see cref="FixtureCatalog.Rfc8785"/> reads them from the RFC's text.</summary>
/// <param name="Numbers">Each row of Table 1, in the RFC's order.</param>
/// <param name="PrimitiveSample">The JSON object of Section 3.2.2 ("Assume the following JSON object is parsed").</param>
/// <param name="PrimitiveSampleCanonical">The UTF-8 bytes of its canonical form, from the hexadecimal of Section 3.2.4.</param>
/// <param name="SortingSample">The JSON object of Section 3.2.3 for checking the order of properties.</param>
/// <param name="SortedValues">Its values in the order that sorting its property names gives.</param>
public sealed record Rfc8785Vectors(
    IReadOnlyList<Rfc8785NumberSample> Numbers,
    string PrimitiveSample,
    byte[] PrimitiveSampleCanonical,
    string SortingSample,
    IReadOnlyList<string> SortedValues);

/// <summary>
/// Reads the test vectors of RFC 8785 from its plain text. Each vector is the indented block after a fixed sentence of
/// the RFC; a text without that sentence or block is refused, so a change to the pinned file cannot pass silently.
/// </summary>
public static class Rfc8785Reader
{
    /// <summary>The vectors of the RFC's text.</summary>
    public static Rfc8785Vectors Read(string text)
    {
        var lines = text.TrimStart('﻿').Split('\n');

        return new Rfc8785Vectors(
            NumberSamples(lines),
            string.Join("\n", BlockAfter(lines, "Assume the following JSON object is parsed:")),
            HexBytes(BlockAfter(lines, "following bytes, here shown in hexadecimal notation:")),
            string.Join("\n", BlockAfter(lines, "correctness of the sorting scheme in a JCS implementation:")),
            BlockAfter(lines, "Expected argument order after sorting property strings:")
                .Select(line => line.Trim())
                .Select(line => line.Length >= 2 && line[0] == '"' && line[^1] == '"'
                    ? line[1..^1]
                    : throw new FormatException($"RFC 8785: '{line}' is not a quoted value of the sorting sample."))
                .ToArray());
    }

    /// <summary>The indented lines after the line that ends with <paramref name="sentence"/>, up to the next blank line.</summary>
    private static IReadOnlyList<string> BlockAfter(string[] lines, string sentence)
    {
        var at = Array.FindIndex(lines, line => line.TrimEnd().EndsWith(sentence, StringComparison.Ordinal));
        if (at < 0) throw new FormatException($"RFC 8785: no line ends with '{sentence}'.");

        var block = lines.Skip(at + 1).SkipWhile(string.IsNullOrWhiteSpace).TakeWhile(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return block.Length > 0 ? block : throw new FormatException($"RFC 8785: no block follows '{sentence}'.");
    }

    private static byte[] HexBytes(IReadOnlyList<string> block) =>
        block.SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(pair => byte.Parse(pair, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture))
            .ToArray();

    private static IReadOnlyList<Rfc8785NumberSample> NumberSamples(string[] lines)
    {
        var start = Array.FindIndex(lines, line => line.StartsWith("Appendix B.", StringComparison.Ordinal));
        if (start < 0) throw new FormatException("RFC 8785: no Appendix B.");

        var samples = lines.Skip(start)
            .TakeWhile(line => !line.Contains("Table 1:", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|') && !line.Contains("IEEE 754", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .Select(cells => cells.Length == 5
                ? new Rfc8785NumberSample(
                    ulong.Parse(cells[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
                    cells[2].Length == 0 ? null : cells[2],
                    WithoutNote(cells[3]))
                : throw new FormatException($"RFC 8785: '{string.Join("|", cells)}' is not a row of Table 1."))
            .ToArray();

        return samples.Length > 0 ? samples : throw new FormatException("RFC 8785: Table 1 has no rows.");
    }

    /// <summary>A comment such as <c>Max pos int    (1)</c> without its note number.</summary>
    private static string WithoutNote(string comment)
    {
        var note = comment.LastIndexOf('(');
        return note >= 0 && comment.EndsWith(')') ? comment[..note].TrimEnd() : comment;
    }
}
