// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Morphir.TestSupport.Fixtures;

/// <summary>
/// Translates the kit's YAML spellings to JSON, for tests only: the product reads JSON. Plain scalars resolve by the
/// YAML 1.2 core schema; a quoted or block scalar is a string. What the kit's YAML profile forbids (versions-0003 to
/// versions-0005: more than one document, anchors, aliases, tags, merge keys, directives, duplicate keys) raises a
/// <see cref="FormatException"/>, as does a number that JSON cannot spell as written.
/// </summary>
public static partial class YamlToJson
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The YAML document as compact JSON text, with keys in their YAML order.</summary>
    public static string Translate(string yaml) => Node(yaml)?.ToJsonString(Compact) ?? "null";

    /// <summary>The JSON text, compact and with the same relaxed escaping as <see cref="Translate"/>.</summary>
    public static string Compacted(string json) => JsonNode.Parse(json)?.ToJsonString(Compact) ?? "null";

    internal static JsonNode? Node(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));
        try
        {
            parser.Consume<StreamStart>();
            if (!parser.TryConsume<DocumentStart>(out var start)) throw Refused("the kit's YAML profile has exactly one document, and this text has none");
            if (start.Version is not null || start.Tags?.Any(IsDeclared) == true) throw Refused("a directive is outside the kit's YAML profile");

            var node = Value(parser);
            parser.Consume<DocumentEnd>();
            if (!parser.TryConsume<StreamEnd>(out _)) throw Refused("the kit's YAML profile has exactly one document, and this text has more");
            return node;
        }
        catch (YamlException error)
        {
            throw new FormatException($"Not valid YAML: {error.Message}", error);
        }
    }

    // The tag handles that every document has without a directive.
    private static bool IsDeclared(YamlDotNet.Core.Tokens.TagDirective directive) =>
        !(directive.Handle == "!" && directive.Prefix == "!") && !(directive.Handle == "!!" && directive.Prefix == "tag:yaml.org,2002:");

    private static JsonNode? Value(IParser parser)
    {
        if (parser.TryConsume<AnchorAlias>(out var alias)) throw Refused($"an alias (*{alias.Value}) is outside the kit's YAML profile");

        if (parser.TryConsume<MappingStart>(out var mapping))
        {
            Plain(mapping);
            var result = new JsonObject();
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                if (parser.Current is not Scalar key) throw Refused("a mapping key that is not a scalar is outside the kit's YAML profile");
                parser.MoveNext();
                Plain(key);
                if (key.Value == "<<" && key.Style == ScalarStyle.Plain) throw Refused("a merge key (<<) is outside the kit's YAML profile");
                if (result.ContainsKey(key.Value)) throw Refused($"the key '{key.Value}' is a duplicate key");
                result[key.Value] = Value(parser);
            }

            return result;
        }

        if (parser.TryConsume<SequenceStart>(out var sequence))
        {
            Plain(sequence);
            var result = new JsonArray();
            while (!parser.TryConsume<SequenceEnd>(out _)) result.Add(Value(parser));
            return result;
        }

        var scalar = parser.Consume<Scalar>();
        Plain(scalar);
        return scalar.Style == ScalarStyle.Plain ? Resolved(scalar.Value) : JsonValue.Create(scalar.Value);
    }

    private static void Plain(NodeEvent node)
    {
        if (!node.Anchor.IsEmpty) throw Refused($"an anchor (&{node.Anchor}) is outside the kit's YAML profile");
        if (!node.Tag.IsEmpty) throw Refused($"a tag ({node.Tag}) is outside the kit's YAML profile");
    }

    // The YAML 1.2 core schema for a plain scalar.
    private static JsonNode? Resolved(string text)
    {
        if (text is "" or "~" or "null" or "Null" or "NULL") return null;
        if (text is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (text is "false" or "False" or "FALSE") return JsonValue.Create(false);
        if (CoreNumber().IsMatch(text))
        {
            if (!JsonNumber().IsMatch(text)) throw Refused($"the number {text} has no JSON number spelling as written");
            return JsonNode.Parse(text);
        }

        return JsonValue.Create(text);
    }

    private static FormatException Refused(string reason) => new(reason);

    [GeneratedRegex(@"^([-+]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+|[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?|[-+]?\.(inf|Inf|INF)|\.(nan|NaN|NAN))$")]
    private static partial Regex CoreNumber();

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][-+]?[0-9]+)?$")]
    private static partial Regex JsonNumber();
}
