// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TufProbe;

/// <summary>
/// The canonical JSON the finos/morphir fixtures serve and sign: no whitespace, NFC strings with only <c>"</c> and
/// <c>\</c> escaped, members sorted by the UTF-8 bytes of their quoted keys, integers in plain decimal. This is the
/// harness's encoder, used to serve and re-sign fixture metadata; it is not the library's.
/// </summary>
static class Canonical
{
    public static byte[] Encode(JsonNode? node)
    {
        using var output = new MemoryStream();
        Write(output, node);
        return output.ToArray();
    }

    static byte[] Quote(string text)
    {
        var escaped = text.Normalize(NormalizationForm.FormC).Replace("\\", "\\\\").Replace("\"", "\\\"");
        return Encoding.UTF8.GetBytes("\"" + escaped + "\"");
    }

    static void Write(MemoryStream output, JsonNode? node)
    {
        switch (node)
        {
            case null:
                output.Write("null"u8);
                break;
            case JsonObject obj:
                output.WriteByte((byte)'{');
                var first = true;
                foreach (var (key, value) in obj
                             .Select(p => (Key: Quote(p.Key), p.Value))
                             .OrderBy(p => p.Key, ByteOrder.Instance))
                {
                    if (!first) output.WriteByte((byte)',');
                    first = false;
                    output.Write(key);
                    output.WriteByte((byte)':');
                    Write(output, value);
                }
                output.WriteByte((byte)'}');
                break;
            case JsonArray array:
                output.WriteByte((byte)'[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0) output.WriteByte((byte)',');
                    Write(output, array[i]);
                }
                output.WriteByte((byte)']');
                break;
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        output.Write(Quote(value.GetValue<string>()));
                        break;
                    case JsonValueKind.Number:
                        var spelling = value.ToJsonString();
                        if (spelling.Any(c => c is '.' or 'e' or 'E' or '-'))
                            throw new InvalidOperationException($"not a plain integer: {spelling}");
                        output.Write(Encoding.ASCII.GetBytes(spelling));
                        break;
                    case JsonValueKind.True:
                        output.Write("true"u8);
                        break;
                    case JsonValueKind.False:
                        output.Write("false"u8);
                        break;
                    default:
                        output.Write("null"u8);
                        break;
                }
                break;
        }
    }

    sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();
        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
    }
}

/// <summary>RFC 6901 JSON pointers over <see cref="JsonNode"/>.</summary>
static class Pointer
{
    public static void Set(JsonNode root, string pointer, JsonNode? value)
    {
        var tokens = pointer.Split('/').Skip(1).Select(t => t.Replace("~1", "/").Replace("~0", "~")).ToArray();
        var parent = root;
        foreach (var token in tokens[..^1])
        {
            parent = parent is JsonArray a ? a[int.Parse(token)]! : parent[token]!;
        }

        if (parent is JsonArray array)
            array[int.Parse(tokens[^1])] = value;
        else
            parent[tokens[^1]] = value;
    }
}
