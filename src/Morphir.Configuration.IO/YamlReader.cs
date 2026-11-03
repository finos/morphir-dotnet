using System.Globalization;
using System.Numerics;
using Microsoft.FSharp.Collections;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Morphir.Configuration.IO;

/// <summary>Consumes parser events directly, without reflection-based YAML object deserialization.</summary>
internal sealed class YamlReader(string text)
{
    private readonly IParser _parser = new Parser(new StringReader(text));
    private readonly List<Tuple<FSharpList<string>, SourcePosition>> _positions = [];

    internal static ParsedTree Parse(string text)
    {
        var reader = new YamlReader(text);
        reader._parser.Consume<StreamStart>();
        if (reader._parser.TryConsume<StreamEnd>(out _)) return new ParsedTree(ReaderValues.Object([]), []);
        reader._parser.Consume<DocumentStart>();
        var value = reader.Read([], 0);
        reader._parser.Consume<DocumentEnd>();
        if (!reader._parser.TryConsume<StreamEnd>(out _))
            throw new ReaderException("config.yaml_documents", "Only one YAML document is permitted", []);
        return new ParsedTree(value, reader._positions);
    }

    private ConfigValue Read(string[] path, int depth)
    {
        var current = _parser.Current ?? throw new ReaderException("config.parse_error", "Expected YAML value", path);
        var position = new SourcePosition((int)current.Start.Line + 1, (int)current.Start.Column + 1);
        if (depth > 128) throw new ReaderException("config.depth_limit", "Configuration nesting exceeds 128 levels", path, position);
        _positions.Add(Tuple.Create(ReaderValues.List(path), position));
        if (current is NodeEvent node && (!node.Tag.IsEmpty || !node.Anchor.IsEmpty))
            throw new ReaderException("config.yaml_feature", "YAML tags and anchors require an explicit supported reader profile", path, position);
        if (_parser.TryConsume<Scalar>(out var scalar)) return ScalarValue(scalar);
        if (_parser.TryConsume<MappingStart>(out _))
        {
            var fields = new Dictionary<string, ConfigValue>(StringComparer.Ordinal);
            while (!_parser.TryConsume<MappingEnd>(out _))
            {
                if (!_parser.TryConsume<Scalar>(out var key))
                    throw new ReaderException("config.yaml_key", "Configuration keys must be scalar strings", path, position);
                if (!key.Tag.IsEmpty || !key.Anchor.IsEmpty)
                    throw new ReaderException("config.yaml_feature", "YAML tagged or anchored keys are not supported", path, new SourcePosition((int)key.Start.Line + 1, (int)key.Start.Column + 1));
                var child = path.Append(key.Value).ToArray();
                if (fields.ContainsKey(key.Value))
                    throw new ReaderException("config.duplicate_key", $"Duplicate key '{key.Value}'", child,
                        new SourcePosition((int)key.Start.Line + 1, (int)key.Start.Column + 1));
                fields.Add(key.Value, Read(child, depth + 1));
            }
            return ReaderValues.Object(fields);
        }
        if (_parser.TryConsume<SequenceStart>(out _))
        {
            var values = new List<ConfigValue>();
            while (!_parser.TryConsume<SequenceEnd>(out _))
                values.Add(Read(path.Append(values.Count.ToString(CultureInfo.InvariantCulture)).ToArray(), depth + 1));
            return ConfigValue.NewArray(ReaderValues.List(values));
        }
        throw new ReaderException("config.yaml_feature", "Unsupported YAML node", path, position);
    }

    private static ConfigValue ScalarValue(Scalar scalar)
    {
        var value = scalar.Value;
        if (scalar.Style is not ScalarStyle.Plain)
            return ConfigValue.NewString(value);
        if (value is "" or "~" or "null" or "Null" or "NULL") return ConfigValue.Null;
        if (value is "true" or "True" or "TRUE") return ConfigValue.NewBoolean(true);
        if (value is "false" or "False" or "FALSE") return ConfigValue.NewBoolean(false);
        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            return ConfigValue.NewInteger(integer);
        if (value is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF") return ConfigValue.NewNumber(double.PositiveInfinity);
        if (value is "-.inf" or "-.Inf" or "-.INF") return ConfigValue.NewNumber(double.NegativeInfinity);
        if (value is ".nan" or ".NaN" or ".NAN") return ConfigValue.NewNumber(double.NaN);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return ConfigValue.NewNumber(number);
        return ConfigValue.NewString(value);
    }
}
