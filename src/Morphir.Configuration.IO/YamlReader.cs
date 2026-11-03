using System.Globalization;
using System.Numerics;
using Microsoft.FSharp.Collections;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Morphir.Configuration.IO;

/// <summary>Moon-Lander 0.8 Default semantics over reflection-free parser events.</summary>
internal sealed class YamlReader(string text)
{
    private readonly IParser _parser = new Parser(new StringReader(text));
    private readonly List<Tuple<FSharpList<string>, SourcePosition>> _positions = [];
    private readonly Dictionary<string, ConfigValue> _anchors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private int _expandedNodes;

    internal static ParsedTree Parse(string text)
    {
        var reader = new YamlReader(text);
        reader._parser.Consume<StreamStart>();
        if (reader._parser.TryConsume<StreamEnd>(out _)) return new ParsedTree(ReaderValues.Object([]), []);
        reader._parser.Consume<DocumentStart>();
        var value = reader.Read([], 0);
        reader._parser.Consume<DocumentEnd>();
        if (!reader._parser.TryConsume<StreamEnd>(out _))
        {
            var current = reader._parser.Current;
            throw new ReaderException("config.yaml_documents", "Only one YAML document is permitted", [],
                current is null ? null : Position(current));
        }
        return new ParsedTree(value, reader._positions);
    }

    private static SourcePosition Position(ParsingEvent item) => new((int)item.Start.Line, (int)item.Start.Column);

    private ConfigValue Read(string[] path, int depth, bool track = true)
    {
        var current = _parser.Current ?? throw new ReaderException("config.parse_error", "Expected YAML value", path);
        var position = Position(current);
        if (depth > 128) throw new ReaderException("config.depth_limit", "Configuration nesting exceeds 128 levels", path, position);
        if (track) _positions.Add(Tuple.Create(ReaderValues.List(path), position));
        if (_parser.TryConsume<AnchorAlias>(out var alias))
        {
            if (_pending.Contains(alias.Value.Value))
                throw new ReaderException("config.yaml_alias", $"Cyclic alias '{alias.Value.Value}'", path, position);
            if (!_anchors.TryGetValue(alias.Value.Value, out var referenced))
                throw new ReaderException("config.yaml_alias", $"Unknown alias '{alias.Value.Value}'", path, position);
            RecordAlias(referenced, path, position, depth, track);
            return referenced;
        }
        var anchor = current is NodeEvent node && !node.Anchor.IsEmpty ? node.Anchor.Value : null;
        if (anchor is not null) _pending.Add(anchor);
        ConfigValue value;
        if (_parser.TryConsume<Scalar>(out var scalar)) value = ScalarValue(scalar, path);
        else if (_parser.TryConsume<MappingStart>(out _))
        {
            var fields = new Dictionary<string, ConfigValue>(StringComparer.Ordinal);
            while (!_parser.TryConsume<MappingEnd>(out _))
            {
                var keyEvent = _parser.Current;
                var keyPosition = keyEvent is null ? position : Position(keyEvent);
                var keyValue = Read(path, depth + 1, false);
                if (keyValue is not ConfigValue.String stringKey)
                    throw new ReaderException("config.yaml_key", "Configuration keys must be strings", path, keyPosition);
                var key = stringKey.Item;
                var child = path.Append(key).ToArray();
                if (key == "<<")
                    throw new ReaderException("config.yaml_merge", "YAML merge keys are not supported", child, keyPosition);
                if (fields.ContainsKey(key))
                    throw new ReaderException("config.duplicate_key", $"Duplicate key '{key}'", child, keyPosition);
                fields.Add(key, Read(child, depth + 1, track));
            }
            value = ReaderValues.Object(fields);
        }
        else if (_parser.TryConsume<SequenceStart>(out _))
        {
            var values = new List<ConfigValue>();
            while (!_parser.TryConsume<SequenceEnd>(out _))
                values.Add(Read(path.Append(values.Count.ToString(CultureInfo.InvariantCulture)).ToArray(), depth + 1, track));
            value = ConfigValue.NewArray(ReaderValues.List(values));
        }
        else throw new ReaderException("config.yaml_feature", "Unsupported YAML node", path, position);
        if (anchor is not null)
        {
            _pending.Remove(anchor);
            _anchors[anchor] = value;
        }
        return value;
    }

    private void RecordAlias(ConfigValue value, string[] path, SourcePosition position, int depth, bool track)
    {
        if (depth > 128 || ++_expandedNodes > 100_000)
            throw new ReaderException("config.yaml_expansion_limit", "YAML alias expansion exceeds depth 128 or 100000 expanded nodes", path, position);
        if (!track) return;
        _positions.Add(Tuple.Create(ReaderValues.List(path), position));
        if (value is ConfigValue.Object fields)
            foreach (var child in fields.Item) RecordAlias(child.Value, path.Append(child.Key).ToArray(), position, depth + 1, true);
        else if (value is ConfigValue.Array values)
        {
            var index = 0;
            foreach (var child in values.Item)
                RecordAlias(child, path.Append((index++).ToString(CultureInfo.InvariantCulture)).ToArray(), position, depth + 1, true);
        }
    }

    private static ConfigValue ScalarValue(Scalar scalar, string[] path)
    {
        var value = scalar.Value;
        if (scalar.Style is not ScalarStyle.Plain) return ConfigValue.NewString(value);
        if (!scalar.Tag.IsEmpty)
        {
            return scalar.Tag.Value switch
            {
                "tag:yaml.org,2002:bool" => Boolean(value) ?? throw InvalidTag("bool", scalar, path),
                "tag:yaml.org,2002:int" => Integer(value, true) ?? throw InvalidTag("int", scalar, path),
                "tag:yaml.org,2002:float" => Float(value) ?? throw InvalidTag("float", scalar, path),
                "tag:yaml.org,2002:null" when value is "~" or "null" => ConfigValue.Null,
                "tag:yaml.org,2002:null" => throw InvalidTag("null", scalar, path),
                _ => ConfigValue.NewString(value)
            };
        }
        if (value is "" or "~" or "null") return ConfigValue.Null;
        return Boolean(value) ?? Integer(value, false) ?? Float(value) ?? ConfigValue.NewString(value);
    }

    private static ReaderException InvalidTag(string tag, Scalar scalar, string[] path) =>
        new("config.yaml_tag", $"Invalid !!{tag} value '{scalar.Value}'", path, Position(scalar));

    private static ConfigValue? Boolean(string value) => value switch
    {
        "true" or "True" or "TRUE" => ConfigValue.NewBoolean(true),
        "false" or "False" or "FALSE" => ConfigValue.NewBoolean(false),
        _ => null
    };

    private static ConfigValue? Integer(string value, bool tagged)
    {
        var token = value.AsSpan();
        var negative = false;
        if (tagged && token.Length > 0 && token[0] is '+' or '-')
        {
            negative = token[0] == '-';
            token = token[1..];
        }
        if (token.Length > 2 && token[0] == '0' && token[1] is 'x' or 'X' or 'o' or 'O')
        {
            var radix = token[1] is 'x' or 'X' ? 16 : 8;
            var number = BigInteger.Zero;
            foreach (var character in token[2..])
            {
                var digit = character is >= '0' and <= '9' ? character - '0'
                    : character is >= 'a' and <= 'f' ? character - 'a' + 10
                    : character is >= 'A' and <= 'F' ? character - 'A' + 10 : -1;
                if (digit < 0 || digit >= radix) return null;
                number = number * radix + digit;
            }
            return Integral(negative ? -number : number);
        }
        if (BigInteger.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            return Integral(integer);
        return null;
    }

    private static ConfigValue Integral(BigInteger value) => value >= long.MinValue && value <= long.MaxValue
        ? ConfigValue.NewInteger((long)value) : ConfigValue.NewBigInteger(value);

    private static ConfigValue? Float(string value)
    {
        if (value is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF") return ConfigValue.NewNumber(double.PositiveInfinity);
        if (value is "-.inf" or "-.Inf" or "-.INF") return ConfigValue.NewNumber(double.NegativeInfinity);
        if (value is ".nan" or ".NaN" or ".NAN") return ConfigValue.NewNumber(double.NaN);
        return value.Any(char.IsAsciiDigit) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? ConfigValue.NewNumber(number) : null;
    }
}
