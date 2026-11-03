using System.Text;
using CsToml;
using CsToml.Values;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO;

internal static class TomlReader
{
    internal static ParsedTree Parse(string text)
    {
        var document = CsTomlSerializer.Deserialize<TomlDocument>(Encoding.UTF8.GetBytes(text));
        return new ParsedTree(Read(document.RootNode, [], 0), []);
    }

    private static ConfigValue Read(TomlDocumentNode node, string[] path, int depth)
    {
        if (depth > 128)
            throw new ReaderException("config.depth_limit", "Configuration nesting exceeds 128 levels", path);
        if (node.HasNodeOnly || node.IsTableHeader || node.ValueType is TomlValueType.Empty or TomlValueType.Table or TomlValueType.InlineTable)
        {
            var fields = new Dictionary<string, ConfigValue>(StringComparer.Ordinal);
            foreach (var child in node)
            {
                var key = child.Key.GetString();
                if (!fields.TryAdd(key, Read(child.Value, path.Append(key).ToArray(), depth + 1)))
                    throw new ReaderException("config.duplicate_key", $"Duplicate key '{key}'", path.Append(key).ToArray());
            }
            return ReaderValues.Object(fields);
        }
        switch (node.ValueType)
        {
            case TomlValueType.String: return ConfigValue.NewString(node.GetString());
            case TomlValueType.Integer: return ConfigValue.NewInteger(node.GetInt64());
            case TomlValueType.Float: return ConfigValue.NewNumber(node.GetDouble());
            case TomlValueType.Boolean: return ConfigValue.NewBoolean(node.GetBool());
            case TomlValueType.LocalDate:
            case TomlValueType.LocalTime:
            case TomlValueType.LocalDateTime:
            case TomlValueType.OffsetDateTime:
                return ConfigValue.NewTemporal(node.ValueType.ToString(), node.GetString());
            case TomlValueType.Array:
                var values = new List<ConfigValue>();
                for (var i = 0; i < node.GetArray().Count; i++)
                    values.Add(Read(node[i], path.Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), depth + 1));
                return ConfigValue.NewArray(ReaderValues.List(values));
            default:
                throw new ReaderException("config.unsupported_value", $"TOML {node.ValueType} is outside the configuration value profile; quote it to retain it as a string", path);
        }
    }
}
