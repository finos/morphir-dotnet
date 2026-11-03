using Eleven19.IonDotnet.Builders;
using System.Text.Json;
using Morphir.Testing;

namespace Morphir.Tooling.Tests.Features.Generate;

internal static class GenerationInputFixtures
{
    public static string DailyRate(string directory, string version, string format)
    {
        var fixtures = Path.Combine(GenerateSteps.RepositoryRoot(), "tests", "Fixtures", "MorphirIon");
        Directory.CreateDirectory(directory);
        if (format == "json")
        {
            var json = version.StartsWith('4') ? GenerateSteps.V4Model :
                version is "1" or "2" ? File.ReadAllText(Path.Combine(GenerateSteps.RepositoryRoot(), "examples", "car-rental", "models", $"car-rental-v{version}.json")) :
                File.ReadAllText(Path.Combine(fixtures, "pairs", "v3-daily-rate.json"));
            if (version.Contains('.')) json = json.Replace("\"formatVersion\":3", $"\"formatVersion\":\"{version}\"").Replace("\"formatVersion\":4", $"\"formatVersion\":\"{version}\"");
            var path = Path.Combine(directory, "model.json"); File.WriteAllText(path, json); return path;
        }
        if (format == "tree")
        {
            var path = Path.Combine(directory, "tree");
            var source = Path.Combine(fixtures, "trees", "v3-daily-rate");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(path, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, File.ReadAllText(file).Replace("3.0.0", version));
            }
            return path;
        }
        var text = File.ReadAllText(Path.Combine(fixtures, "pairs", "v3-daily-rate.ion")).Replace("3.0.0", version);
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
        if (format == "json")
        {
            var path = DailyRate(directory, version, "json");
            var json = File.ReadAllText(path).Replace("inventory", "literals").Replace("daily-rate", "value").Replace("[\"daily\",\"rate\"]", "[\"value\"]");
            if (version.StartsWith('4')) json = json.Replace("\"WholeNumberLiteral\": 5000", $"\"{(isDecimal ? "DecimalLiteral" : "WholeNumberLiteral")}\": {payload}");
            else json = json.Replace("[\"WholeNumberLiteral\",5000]", $"[\"{(isDecimal ? "DecimalLiteral" : "WholeNumberLiteral")}\",{(isDecimal ? ((GenerationScalars.Scalar.Decimal)scalar.Input).Item : payload)}]");
            if (isDecimal) json = json.Replace("morphir/SDK:basics#int", type).Replace("[[\"basics\"]],[\"int\"]", "[[\"decimal\"]],[\"decimal\"]");
            File.WriteAllText(path, json); return path;
        }
        return WriteIon(directory, format, header + $" public::def::module::{{name:\"literals\"}} public::def::value::{{module:\"literals\",name:\"value\",{value}}} morphir_footer::{{}}");
    }

    private static string WriteIon(string directory, string format, string text)
    {
        var input = Path.Combine(directory, "model.unusual");
        Directory.CreateDirectory(directory);
        if (format == "text") File.WriteAllText(input, text);
        else
        {
            using var reader = IonReaderBuilder.Build(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));
            using var output = new MemoryStream();
            using var writer = IonBinaryWriterBuilder.Build(output);
            writer.WriteValues(reader); writer.Finish();
            File.WriteAllBytes(input, output.ToArray());
        }
        return input;
    }
}
