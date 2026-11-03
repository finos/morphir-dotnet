using System.Text;
using System.Globalization;
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
                node.TryGetDateOnly(out var date);
                return ConfigValue.NewTemporal("LocalDate", date.ToString("O", CultureInfo.InvariantCulture));
            case TomlValueType.LocalTime:
                node.TryGetTimeOnly(out var time);
                return ConfigValue.NewTemporal("LocalTime", time.ToString("O", CultureInfo.InvariantCulture));
            case TomlValueType.LocalDateTime:
                node.TryGetDateTime(out var local);
                return ConfigValue.NewTemporal("LocalDateTime", local.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
            case TomlValueType.OffsetDateTime:
                node.TryGetDateTimeOffset(out var offset);
                return ConfigValue.NewTemporal("OffsetDateTime", offset.ToString("O", CultureInfo.InvariantCulture));
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
