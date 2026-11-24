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

    public static string LossyDailyRateIonWithUnknownHeader(string directory, string version)
    {
        var text = Morphir.TestSupport.Fixtures.FixtureCatalog.ReadText(
            Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.V3DailyRateIon).Replace("3.0.0", version);
        var idx = text.IndexOf("morphir::{", StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("Ion fixture did not begin with morphir::{.");
        var mutated = text.Insert(idx + "morphir::{".Length, "customMeta: \"dropped-by-json\", ");
        return WriteIon(directory, "text", mutated);
    }

    public static string LossyDailyRateIonWithPathBudget(string directory, string version, bool annotated)
    {
        var text = Morphir.TestSupport.Fixtures.FixtureCatalog.ReadText(
            Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.V3DailyRateIon).Replace("3.0.0", version);
        var idx = text.IndexOf("morphir::{", StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("Ion fixture did not begin with morphir::{.");
        var field = annotated ? "pathBudget: note::4000, " : "pathBudget: 4000, ";
        return WriteIon(directory, "text", text.Insert(idx + "morphir::{".Length, field));
    }

    public static string LossyDailyRateIonWithAnnotatedDependencyFragments(string directory, string version)
    {
        var datagram =
            $$"""
            morphir::{ionVersion: "0.1.0-draft.1", formatVersion: "{{version}}", kind: library, packageName: "car/rental"}
            package::spec::{name: "acme/dep"}
            module::spec::{package: "acme/dep", name: "util", doc: "same", types: [public::spec::opaque::type::{name: "t"}]}
            module::spec::{package: "acme/dep", name: "util", doc: note::"same", values: [public::spec::value::{name: "v", output: "morphir/SDK:basics#int"}]}
            public::def::module::{name: "inventory", values: [public::def::value::{name: "daily-rate", outputType: "morphir/SDK:basics#int", body: 5000}]}
            morphir_footer::{}
            """;
        return WriteIon(directory, "text", datagram);
    }

    public static string LossyDailyRateIonTreeWithAnnotatedDependencyFragments(string directory, string version)
    {
        var treeRoot = Path.Combine(directory, "tree");
        Directory.CreateDirectory(treeRoot);
        File.WriteAllText(Path.Combine(treeRoot, "manifest.ion"),
            $$"""
            morphir::{ionVersion: "0.1.0-draft.1", formatVersion: "{{version}}", kind: library, packageName: "car/rental", pathBudget: 4000}
            package::spec::{name: "acme/dep"}
            """);
        var depDir = Path.Combine(treeRoot, "deps", "acme", "dep", "@", "util");
        Directory.CreateDirectory(depDir);
        File.WriteAllText(Path.Combine(depDir, "module.ion"),
            "module::spec::{doc: \"same\"}\nmodule::spec::{doc: note::\"same\"}");
        File.WriteAllText(Path.Combine(depDir, "t.type.ion"), "public::spec::opaque::type::{}");
        var modDir = Path.Combine(treeRoot, "pkg", "car", "rental", "inventory");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "module.ion"), "public::def::module::{}");
        File.WriteAllText(Path.Combine(modDir, "daily-rate.value.ion"),
            "public::def::value::{outputType: \"morphir/SDK:basics#int\", body: 5000}");
        return treeRoot;
    }

    public static string LossyIonWithAnnotatedInputTypesMap(string directory, string version, bool tree)
    {
        if (!tree)
        {
            var datagram =
                $$"""
                morphir::{ionVersion: "0.1.0-draft.1", formatVersion: "{{version}}", kind: library, packageName: "car/rental"}
                public::def::module::{name: "inventory", values: [public::def::value::{name: "daily-rate", inputTypes: unit::{x: "morphir/SDK:basics#int"}, outputType: "morphir/SDK:basics#int", body: x}]}
                morphir_footer::{}
                """;
            return WriteIon(directory, "text", datagram);
        }

        var treeRoot = Path.Combine(directory, "tree");
        Directory.CreateDirectory(treeRoot);
        File.WriteAllText(Path.Combine(treeRoot, "manifest.ion"),
            $$"""morphir::{ionVersion: "0.1.0-draft.1", formatVersion: "{{version}}", kind: library, packageName: "car/rental", pathBudget: 4000}""");
        var modDir = Path.Combine(treeRoot, "pkg", "car", "rental", "inventory");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "module.ion"), "public::def::module::{}");
        File.WriteAllText(Path.Combine(modDir, "daily-rate.value.ion"),
            "public::def::value::{inputTypes: unit::{x: \"morphir/SDK:basics#int\"}, outputType: \"morphir/SDK:basics#int\", body: x}");
        return treeRoot;
    }

    public static string IonWithDocumentNumberUnderNameField(string directory, string version, bool tree)
    {
        if (!tree)
        {
            var datagram =
                $$"""
                morphir::{ionVersion: "0.1.0-draft.1", formatVersion: "{{version}}", kind: library, packageName: "car/rental"}
                public::def::module::{name: "inventory", values: [public::def::value::{name: "daily-rate", outputType: "morphir/SDK:basics#int", body: (document {name: number::"1.5"})}]}
                morphir_footer::{}
                """;
            return WriteIon(directory, "text", datagram);
        }

        var treeRoot = Path.Combine(directory, "tree");
        Directory.CreateDirectory(treeRoot);
        File.WriteAllText(Path.Combine(treeRoot, "manifest.ion"),
            $$"""morphir::{ionVersion: "0.1.0-draft.1", formatVersion: "{{version}}", kind: library, packageName: "car/rental", pathBudget: 4000}""");
        var modDir = Path.Combine(treeRoot, "pkg", "car", "rental", "inventory");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "module.ion"), "public::def::module::{}");
        File.WriteAllText(Path.Combine(modDir, "daily-rate.value.ion"),
            "public::def::value::{outputType: \"morphir/SDK:basics#int\", body: (document {name: number::\"1.5\"})}");
        return treeRoot;
    }

    public static string DeepIonSingleDocument(string directory, string version, int nestingLayers)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < nestingLayers; i++) sb.Append("(list ");
        sb.Append("1");
        for (var i = 0; i < nestingLayers; i++) sb.Append(')');
        var datagram =
            $$"""
            morphir::{ionVersion: "0.1.0-draft.1", formatVersion: "{{version}}", kind: library, packageName: "car/rental"}
            public::def::module::{name: "inventory", values: [public::def::value::{name: "daily-rate", outputType: "morphir/SDK:basics#int", body: {{sb}}}]}
            morphir_footer::{}
            """;
        return WriteIon(directory, "text", datagram);
    }

    public static string OrdersTreeWithContextPrefixes(string directory, string versionDir, string profile)
    {
        CopyBoundedOrdersTree(versionDir, profile, directory);
        var isYaml = profile.Contains("yaml", StringComparison.Ordinal);
        var ext = isYaml ? ".yaml" : ".json";
        var targetPath = Path.Combine(directory, "pkg", "acme", "orders", "domain", "order.value" + ext);
        var text = File.ReadAllText(targetPath);
        if (isYaml)
        {
            var idx = text.IndexOf("context:", StringComparison.Ordinal);
            if (idx < 0) throw new InvalidOperationException($"Expected contextual YAML envelope in {targetPath}.");
            text = text.Insert(idx + "context:".Length, "\n  prefixes:\n    o: acme/orders");
        }
        else
        {
            var idx = text.IndexOf("\"context\":{", StringComparison.Ordinal);
            if (idx < 0) idx = text.IndexOf("\"context\": {", StringComparison.Ordinal);
            if (idx < 0) throw new InvalidOperationException($"Expected contextual JSON envelope in {targetPath}.");
            var brace = text.IndexOf('{', idx);
            text = text.Insert(brace + 1, "\"prefixes\":{\"o\":\"acme/orders\"},");
        }
        File.WriteAllText(targetPath, text);
        return directory;
    }

    public static string PlainV4OrdersTreeWithJsonDepth(string directory, int targetJsonDepth)
    {
        CopyBoundedOrdersTree("v4", "plain-json", directory);
        var orderPath = Path.Combine(directory, "pkg", "acme", "orders", "domain", "order.value.json");
        var original = File.ReadAllText(orderPath);
        var baseDepth = MeasureMaxJsonDepth(original);
        var layers = Math.Max(1, (targetJsonDepth - baseDepth + 1) / 2);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < layers; i++) sb.Append("{\"List\":[");
        sb.Append("{\"Literal\":{\"IntegerLiteral\":73}}");
        for (var i = 0; i < layers; i++) sb.Append("]}");
        var updated = original.Replace("{ \"Literal\": { \"IntegerLiteral\": 73 } }", sb.ToString(), StringComparison.Ordinal);
        File.WriteAllText(orderPath, updated);
        return directory;
    }

    private static int MeasureMaxJsonDepth(string json)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json), new JsonReaderOptions { MaxDepth = int.MaxValue });
        var max = 0;
        while (reader.Read())
            if (reader.CurrentDepth + 1 > max) max = reader.CurrentDepth + 1;
        return max;
    }

    public static string LossyDailyRateIonTreeWithIgnoredFile(string directory, string version, string ignoredFileName = "README.md")
    {
        var treeRoot = DailyRate(directory, version, "tree");
        var sidecar = Path.Combine(treeRoot, ignoredFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
        File.WriteAllText(sidecar, "ignored sidecar content\n");
        return treeRoot;
    }

    public static string LossyDailyRateIonTreeWithAnnotatedManifestPathBudget(string directory, string version)
    {
        var treeRoot = DailyRate(directory, version, "tree");
        var manifest = Path.Combine(treeRoot, "manifest.ion");
        var text = File.ReadAllText(manifest).Replace("pathBudget: 4000", "pathBudget: note::4000", StringComparison.Ordinal);
        File.WriteAllText(manifest, text);
        return treeRoot;
    }

    public static string OrdersTreeWithDroppedManifestVersion(string directory, string versionDir, string profile)
    {
        CopyBoundedOrdersTree(versionDir, profile, directory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        var manifestJson = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, manifestJson.Replace("\"formatVersion\": 4,", "\"formatVersion\": 4, \"version\": \"1.2.3\",", StringComparison.Ordinal));
        return directory;
    }

    public static string OrdersTreeWithPackageMetadata(string directory, string versionDir, string profile)
    {
        CopyBoundedOrdersTree(versionDir, profile, directory);
        var pkgMeta = Path.Combine(directory, "pkg", "acme", "orders", "package.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pkgMeta)!);
        File.WriteAllText(pkgMeta, """{"owner":"acme","notes":"package metadata dropped by single-document JSON"}""");
        return directory;
    }

    public static string PlainV4OrdersTreeWithLargeMember(string directory, int paddingBytes)
    {
        CopyBoundedOrdersTree("v4", "plain-json", directory);
        var orderPath = Path.Combine(directory, "pkg", "acme", "orders", "domain", "order.value.json");
        var padding = new string('a', paddingBytes);
        var original = File.ReadAllText(orderPath);
        File.WriteAllText(orderPath, original.Replace("{ \"IntegerLiteral\": 73 }", $"{{ \"StringLiteral\": \"{padding}\" }}", StringComparison.Ordinal));
        return directory;
    }

    public static string PlainV4OrdersTreeWithDeepNesting(string directory, int listDepth)
    {
        CopyBoundedOrdersTree("v4", "plain-json", directory);
        var orderPath = Path.Combine(directory, "pkg", "acme", "orders", "domain", "order.value.json");
        var original = File.ReadAllText(orderPath);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < listDepth; i++) sb.Append("{\"List\":[");
        sb.Append("{\"Literal\":{\"IntegerLiteral\":73}}");
        for (var i = 0; i < listDepth; i++) sb.Append("]}");
        File.WriteAllText(orderPath, original.Replace("{ \"Literal\": { \"IntegerLiteral\": 73 } }", sb.ToString(), StringComparison.Ordinal));
        return directory;
    }

    public static string ClassicIonWithLossyDecimalScale(string directory, bool inPattern)
    {
        const string scale29 = "1.00000000000000000000000000000";
        var body = inPattern
            ? $"(match x [(decimal \"{scale29}\"), 1] [_, 0])"
            : $"(decimal \"{scale29}\")";
        var inputs = inPattern ? ", inputTypes: [ { name: \"x\", type: \"morphir/SDK:decimal#decimal\" } ]" : "";
        var outputType = inPattern ? "\"morphir/SDK:basics#int\"" : "\"morphir/SDK:decimal#decimal\"";
        var text =
            $$"""
            morphir::{ ionVersion: "0.1.0-draft.1", formatVersion: "3.0.0", kind: library, packageName: "car/rental" }
            public::def::module::{ name: "pricing", values: [ public::def::value::{ name: "rate"{{inputs}}, outputType: {{outputType}}, body: {{body}} } ] }
            morphir_footer::{}
            """;
        return WriteIon(directory, "text", text);
    }

    public static string PlainV4MultiModuleTree(string directory, int moduleCount)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            """{"formatVersion":4,"distribution":"Library","package":"acme/orders","pathBudget":240,"dependencies":[]}""");
        for (var i = 0; i < moduleCount; i++)
        {
            var modName = $"mod-{i:D2}";
            var modDir = Path.Combine(directory, "pkg", "acme", "orders", modName);
            Directory.CreateDirectory(modDir);
            File.WriteAllText(Path.Combine(modDir, "module.json"),
                $$"""{"formatVersion":4,"path":"{{modName}}","access":"Public","types":[],"values":[]}""");
        }
        return directory;
    }

    public static string PlainV4LongPathMultiModuleTree(string directory, int moduleCount)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            """{"formatVersion":4,"distribution":"Library","package":"acme/flat","pathBudget":4000,"dependencies":[]}""");
        var seg1 = new string('a', 100);
        var seg2 = new string('b', 100);
        var seg3 = new string('c', 100);
        var seg4 = new string('d', 100);
        var prefix = $"{seg1}/{seg2}/{seg3}/{seg4}";
        for (var i = 0; i < moduleCount; i++)
        {
            var modName = $"mod-{i:D4}";
            var modulePath = $"{prefix}/{modName}";
            var modDir = Path.Combine(directory, "pkg", "acme", "flat", seg1, seg2, seg3, seg4, modName);
            Directory.CreateDirectory(modDir);
            File.WriteAllText(Path.Combine(modDir, "module.json"),
                $$"""{"formatVersion":4,"path":"{{modulePath}}","access":"Public","types":[],"values":[]}""");
        }
        return directory;
    }

    public static string ClassicOrdersTreeWithDecimal(string directory, string profile, string lexeme, bool inPattern)
    {
        CopyBoundedOrdersTree("v3.1", profile, directory);
        var isYaml = profile.EndsWith("yaml", StringComparison.Ordinal);
        var orderPath = Path.Combine(directory, "pkg", "acme", "orders", "domain", isYaml ? "order.value.yaml" : "order.value.json");
        const string decimalTypeJson = """["Reference", {}, [[["morphir"], ["s", "d", "k"]], [["decimal"]], ["decimal"]], []]""";
        const string intTypeJson = """["Reference", {}, [[["morphir"], ["s", "d", "k"]], [["basics"]], ["int"]], []]""";
        string content;
        if (isYaml)
        {
            content = inPattern
                ? $$"""
                formatVersion: 3.1.0
                name: order
                def:
                  access: Public
                  value:
                    doc: ""
                    value:
                      inputTypes:
                        - - [x]
                          - - Reference
                            - {}
                            - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                            - []
                          - - Reference
                            - {}
                            - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                            - []
                      outputType:
                        - Reference
                        - {}
                        - [[[morphir], [s, d, k]], [[basics]], [int]]
                        - []
                      body:
                        - PatternMatch
                        - - Reference
                          - {}
                          - [[[morphir], [s, d, k]], [[basics]], [int]]
                          - []
                        - - Variable
                          - - Reference
                            - {}
                            - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                            - []
                          - [x]
                        - - - - LiteralPattern
                              - - Reference
                                - {}
                                - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                                - []
                              - [DecimalLiteral, "{{lexeme}}"]
                            - - Literal
                              - - Reference
                                - {}
                                - [[[morphir], [s, d, k]], [[basics]], [int]]
                                - []
                              - [WholeNumberLiteral, 1]
                          - - - WildcardPattern
                              - - Reference
                                - {}
                                - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                                - []
                            - - Literal
                              - - Reference
                                - {}
                                - [[[morphir], [s, d, k]], [[basics]], [int]]
                                - []
                              - [WholeNumberLiteral, 0]
                """
                : $$"""
                formatVersion: 3.1.0
                name: order
                def:
                  access: Public
                  value:
                    doc: ""
                    value:
                      inputTypes: []
                      outputType:
                        - Reference
                        - {}
                        - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                        - []
                      body:
                        - Literal
                        - - Reference
                          - {}
                          - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                          - []
                        - [DecimalLiteral, "{{lexeme}}"]
                """;
        }
        else
        {
            content = inPattern
                ? $$"""
                {
                  "formatVersion": "3.1.0",
                  "name": "order",
                  "def": {
                    "access": "Public",
                    "value": { "doc": "", "value": {
                      "inputTypes": [[["x"], {{decimalTypeJson}}, {{decimalTypeJson}}]],
                      "outputType": {{intTypeJson}},
                      "body": ["PatternMatch", {{intTypeJson}}, ["Variable", {{decimalTypeJson}}, ["x"]], [
                        [["LiteralPattern", {{decimalTypeJson}}, ["DecimalLiteral", "{{lexeme}}"]], ["Literal", {{intTypeJson}}, ["WholeNumberLiteral", 1]]],
                        [["WildcardPattern", {{decimalTypeJson}}], ["Literal", {{intTypeJson}}, ["WholeNumberLiteral", 0]]]
                      ]]
                    } }
                  }
                }
                """
                : $$"""
                {
                  "formatVersion": "3.1.0",
                  "name": "order",
                  "def": {
                    "access": "Public",
                    "value": { "doc": "", "value": {
                      "inputTypes": [],
                      "outputType": {{decimalTypeJson}},
                      "body": ["Literal", {{decimalTypeJson}}, ["DecimalLiteral", "{{lexeme}}"]]
                    } }
                  }
                }
                """;
        }
        File.WriteAllText(orderPath, content);
        return directory;
    }

    public static string ClassicOrdersTreeWithOpaqueAttributeAndDecimal(string directory, string profile, string lexeme, bool inPattern)
    {
        CopyBoundedOrdersTree("v3.1", profile, directory);
        var isYaml = profile.EndsWith("yaml", StringComparison.Ordinal);
        var orderPath = Path.Combine(directory, "pkg", "acme", "orders", "domain", isYaml ? "order.value.yaml" : "order.value.json");
        const string decimalTypeJson = """["Reference", {}, [[["morphir"], ["s", "d", "k"]], [["decimal"]], ["decimal"]], []]""";
        const string intTypeJson = """["Reference", {}, [[["morphir"], ["s", "d", "k"]], [["basics"]], ["int"]], []]""";
        const string opaqueAttrJson = """["Literal", {}, ["DecimalLiteral", "-0.00"]]""";
        string content;
        if (isYaml)
        {
            content = inPattern
                ? $$"""
                formatVersion: 3.1.0
                name: order
                def:
                  access: Public
                  value:
                    doc: ""
                    value:
                      inputTypes:
                        - - [x]
                          - [Literal, {}, [DecimalLiteral, "-0.00"]]
                          - - Reference
                            - {}
                            - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                            - []
                      outputType:
                        - Reference
                        - {}
                        - [[[morphir], [s, d, k]], [[basics]], [int]]
                        - []
                      body:
                        - PatternMatch
                        - [Literal, {}, [DecimalLiteral, "-0.00"]]
                        - - Variable
                          - [Literal, {}, [DecimalLiteral, "-0.00"]]
                          - [x]
                        - - - - LiteralPattern
                              - [Literal, {}, [DecimalLiteral, "-0.00"]]
                              - [DecimalLiteral, "{{lexeme}}"]
                            - - Literal
                              - [Literal, {}, [DecimalLiteral, "-0.00"]]
                              - [WholeNumberLiteral, 1]
                          - - - WildcardPattern
                              - [Literal, {}, [DecimalLiteral, "-0.00"]]
                            - - Literal
                              - [Literal, {}, [DecimalLiteral, "-0.00"]]
                              - [WholeNumberLiteral, 0]
                """
                : $$"""
                formatVersion: 3.1.0
                name: order
                def:
                  access: Public
                  value:
                    doc: ""
                    value:
                      inputTypes: []
                      outputType:
                        - Reference
                        - {}
                        - [[[morphir], [s, d, k]], [[decimal]], [decimal]]
                        - []
                      body:
                        - Literal
                        - [Literal, {}, [DecimalLiteral, "-0.00"]]
                        - [DecimalLiteral, "{{lexeme}}"]
                """;
        }
        else
        {
            content = inPattern
                ? $$"""
                {
                  "formatVersion": "3.1.0",
                  "name": "order",
                  "def": {
                    "access": "Public",
                    "value": { "doc": "", "value": {
                      "inputTypes": [[["x"], {{opaqueAttrJson}}, {{decimalTypeJson}}]],
                      "outputType": {{intTypeJson}},
                      "body": ["PatternMatch", {{opaqueAttrJson}}, ["Variable", {{opaqueAttrJson}}, ["x"]], [
                        [["LiteralPattern", {{opaqueAttrJson}}, ["DecimalLiteral", "{{lexeme}}"]], ["Literal", {{opaqueAttrJson}}, ["WholeNumberLiteral", 1]]],
                        [["WildcardPattern", {{opaqueAttrJson}}], ["Literal", {{opaqueAttrJson}}, ["WholeNumberLiteral", 0]]]
                      ]]
                    } }
                  }
                }
                """
                : $$"""
                {
                  "formatVersion": "3.1.0",
                  "name": "order",
                  "def": {
                    "access": "Public",
                    "value": { "doc": "", "value": {
                      "inputTypes": [],
                      "outputType": {{decimalTypeJson}},
                      "body": ["Literal", {{opaqueAttrJson}}, ["DecimalLiteral", "{{lexeme}}"]]
                    } }
                  }
                }
                """;
        }
        File.WriteAllText(orderPath, content);
        return directory;
    }

    public static (string ProjectRoot, string OutputDir) WriteDraft1Project(string projectRoot, string outputDir, string wasmPath, string projectName)
    {
        Directory.CreateDirectory(projectRoot);
        File.WriteAllBytes(wasmPath, [0x00, 0x61, 0x73, 0x6D]);
        File.WriteAllText(Path.Combine(projectRoot, "morphir.toml"),
            $"""
            [project]
            name = "{projectName}"

            [codegen.text]
            extension = "draft1"

            [extensions.draft1]
            path = "{wasmPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"
            """);
        return (projectRoot, outputDir);
    }

    private static void CopyBoundedOrdersTree(string versionDir, string profile, string destination)
    {
        var prefix = Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Directory + versionDir + "/" + profile + "/";
        foreach (var entry in Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Files
            .Where(f => f.RelativePath.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var relative = entry.RelativePath[prefix.Length..];
            var path = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Morphir.TestSupport.Fixtures.FixtureCatalog.ReadBytes(entry));
        }
    }

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
