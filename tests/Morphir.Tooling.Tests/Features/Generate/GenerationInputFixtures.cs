using Eleven19.IonDotnet.Builders;
using System.Text;
using System.Text.Json;
using Morphir.Models.TestSupport;
using Morphir.TestSupport.Fixtures;
using Morphir.Testing;

namespace Morphir.Tooling.Tests.Features.Generate;

internal static class GenerationInputFixtures
{
    private static readonly FixtureEntry V3DailyRatePair =
        FixtureCatalog.UpstreamIr.Documents.Single(d => FixtureCatalog.UpstreamIr.NameOf(d) == "pairs/v3-daily-rate.json");

    public static string DailyRate(string directory, string version, string format)
    {
        Directory.CreateDirectory(directory);
        if (format is "json" or "yaml" or "json-tree" or "yaml-tree")
        {
            var json = ReadCatalogDailyRateJson(version);
            return WriteJsonOrYamlRepresentation(directory, version, format, json);
        }
        if (format == "tree")
        {
            var path = Path.Combine(directory, "tree");
            foreach (var file in FixtureCatalog.UpstreamIr.V3DailyRateIonTree.Files)
            {
                var target = Path.Combine(path, file.Location.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, FixtureCatalog.ReadText(file.Entry).Replace("3.0.0", version));
            }
            return path;
        }
        var text = FixtureCatalog.ReadText(FixtureCatalog.UpstreamIr.V3DailyRateIon).Replace("3.0.0", version);
        return WriteIon(directory, format, text);
    }

    public static string Scalar(string directory, string version, string format, GenerationScalars.Case scalar)
    {
        var isDecimal = scalar.Input is GenerationScalars.Scalar.Decimal;
        var payload = scalar.Input switch
        {
            GenerationScalars.Scalar.Integer integer => integer.Item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GenerationScalars.Scalar.Decimal decimalValue => JsonSerializer.Serialize(decimalValue.Item),
            _ => throw new ArgumentException("This wire adapter implements integer and decimal rows.")
        };
        var type = isDecimal ? "morphir/SDK:decimal#decimal" : "morphir/SDK:basics#int";
        var body = $"({(isDecimal ? "decimal" : "int")} {payload})";
        var header = $"morphir::{{ionVersion:\"0.1.0-draft.1\",formatVersion:\"{version}\",kind:library,packageName:\"car/rental\"}}";
        var value = $"outputType:\"{type}\",body:{body}";
        Directory.CreateDirectory(directory);
        if (format == "tree")
        {
            File.WriteAllText(Path.Combine(directory, "manifest.ion"), header);
            var module = Path.Combine(directory, "pkg", "car", "rental", "literals"); Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "module.ion"), "public::def::module::{}");
            File.WriteAllText(Path.Combine(module, "value.value.ion"), $"public::def::value::{{{value}}}");
            return directory;
        }
        if (format is "json" or "yaml" or "json-tree" or "yaml-tree")
        {
            var jsonPath = DailyRate(directory, version, "json");
            var json = File.ReadAllText(jsonPath).Replace("inventory", "literals").Replace("daily-rate", "value").Replace("[\"daily\",\"rate\"]", "[\"value\"]");
            if (version.StartsWith('4')) json = json.Replace("\"WholeNumberLiteral\": 5000", $"\"{(isDecimal ? "DecimalLiteral" : "WholeNumberLiteral")}\": {payload}");
            else json = json.Replace("[\"WholeNumberLiteral\",5000]", $"[\"{(isDecimal ? "DecimalLiteral" : "WholeNumberLiteral")}\",{(isDecimal ? ((GenerationScalars.Scalar.Decimal)scalar.Input).Item : payload)}]");
            if (isDecimal) json = json.Replace("morphir/SDK:basics#int", type).Replace("[[\"basics\"]],[\"int\"]", "[[\"decimal\"]],[\"decimal\"]");
            return WriteJsonOrYamlRepresentation(directory, version, format, json);
        }
        return WriteIon(directory, format, header + $" public::def::module::{{name:\"literals\"}} public::def::value::{{module:\"literals\",name:\"value\",{value}}} morphir_footer::{{}}");
    }

    private static string ReadCatalogDailyRateJson(string version)
    {
        if (version.StartsWith('4'))
        {
            var v4 = FixtureCatalog.ReadText(FixtureCatalog.Models.V4DailyRate);
            return version.Contains('.') ? v4.Replace("\"formatVersion\": 4", $"\"formatVersion\": \"{version}\"").Replace("\"formatVersion\":4", $"\"formatVersion\":\"{version}\"") : v4;
        }

        var v3 = FixtureCatalog.ReadText(V3DailyRatePair);
        if (version is "1" or "2")
            return DocumentTreeMother.YamlDocuments.ProjectClassicJsonToVersion(version, v3);

        return version.Contains('.') ? v3.Replace("\"formatVersion\":3", $"\"formatVersion\":\"{version}\"") : v3;
    }

    private static string WriteJsonOrYamlRepresentation(string directory, string version, string format, string json)
    {
        if (format == "json")
        {
            var path = Path.Combine(directory, "model.json");
            File.WriteAllText(path, json);
            return path;
        }

        if (format == "yaml")
        {
            var path = Path.Combine(directory, "model.yaml");
            File.WriteAllText(path, JsonToYaml(json));
            return path;
        }

        var treeFiles = DocumentTreeMother.YamlDocuments.SplitJsonModelToTreeFiles(version, json);
        var treeRoot = Path.Combine(directory, format);
        Directory.CreateDirectory(treeRoot);
        var isYamlTree = format == "yaml-tree";

        foreach (var entry in treeFiles)
        {
            var relativePath = isYamlTree ? Path.ChangeExtension(entry.Key, ".yaml") : entry.Key;
            var targetPath = Path.Combine(treeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, isYamlTree ? JsonToYaml(entry.Value) : entry.Value);
        }

        return treeRoot;
    }

    private static string JsonToYaml(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = new StringBuilder();
        WriteYamlValue(builder, document.RootElement, 0);
        builder.Append('\n');
        return builder.ToString();
    }

    private static void WriteYamlValue(StringBuilder builder, JsonElement element, int indent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var properties = element.EnumerateObject().ToArray();
                    if (properties.Length == 0)
                    {
                        builder.Append("{}");
                        return;
                    }

                    for (var i = 0; i < properties.Length; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append('\n');
                            builder.Append(' ', indent);
                        }

                        builder.Append(JsonSerializer.Serialize(properties[i].Name));
                        builder.Append(':');
                        if (IsBlockContainer(properties[i].Value))
                        {
                            builder.Append('\n');
                            builder.Append(' ', indent + 2);
                            WriteYamlValue(builder, properties[i].Value, indent + 2);
                        }
                        else
                        {
                            builder.Append(' ');
                            WriteYamlValue(builder, properties[i].Value, indent + 2);
                        }
                    }
                    return;
                }
            case JsonValueKind.Array:
                {
                    var items = element.EnumerateArray().ToArray();
                    if (items.Length == 0)
                    {
                        builder.Append("[]");
                        return;
                    }

                    for (var i = 0; i < items.Length; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append('\n');
                            builder.Append(' ', indent);
                        }

                        builder.Append('-');
                        if (IsBlockContainer(items[i]))
                        {
                            builder.Append('\n');
                            builder.Append(' ', indent + 2);
                            WriteYamlValue(builder, items[i], indent + 2);
                        }
                        else
                        {
                            builder.Append(' ');
                            WriteYamlValue(builder, items[i], indent + 2);
                        }
                    }
                    return;
                }
            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(element.GetString()!));
                return;
            case JsonValueKind.Number:
                builder.Append(element.GetRawText());
                return;
            case JsonValueKind.True:
                builder.Append("true");
                return;
            case JsonValueKind.False:
                builder.Append("false");
                return;
            case JsonValueKind.Null:
                builder.Append("null");
                return;
            default:
                throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}");
        }
    }

    private static bool IsBlockContainer(JsonElement element) =>
        (element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any())
        || (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0);

    private static string WriteIon(string directory, string format, string text)
    {
        var input = Path.Combine(directory, "model.unusual");
        Directory.CreateDirectory(directory);
        if (format == "text") File.WriteAllText(input, text);
        else
        {
            using var reader = IonReaderBuilder.Build(new MemoryStream(Encoding.UTF8.GetBytes(text)));
            using var output = new MemoryStream();
            using var writer = IonBinaryWriterBuilder.Build(output);
            writer.WriteValues(reader); writer.Finish();
            File.WriteAllBytes(input, output.ToArray());
        }
        return input;
    }
}
