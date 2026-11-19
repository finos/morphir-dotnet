// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>PlantUML JSON and YAML blocks as data trees.</summary>
public class DataTreeTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <summary>Each JSON, YAML and envelope fixture with PlantUML's data, or null where the library refuses it.</summary>
    private static IEnumerable<(string Name, string Source, JsonNode? Data, JsonElement Reference)> Fixtures()
    {
        var files = PinnedCorpus.Files(FixtureCatalog.DiagramsMbt.PlantUml);
        string File(string suffix) => files.Single(file => file.Key.EndsWith(suffix, StringComparison.Ordinal)).Value;

        using var expectations = JsonDocument.Parse(File("/expectations.json"));
        var refusals = expectations.RootElement.EnumerateObject()
            .Where(entry => entry.Value.GetProperty("category").GetString() == "library-refusal")
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        using var reference = JsonDocument.Parse(File("/reference.json"));

        return reference.RootElement.GetProperty("fixtures").EnumerateArray()
            .Where(fixture => fixture.GetProperty("name").GetString() is { } name &&
                (name.StartsWith("json/", StringComparison.Ordinal) || name.StartsWith("yaml/", StringComparison.Ordinal) || name.StartsWith("envelope/", StringComparison.Ordinal)))
            .Select(fixture =>
            {
                var name = fixture.GetProperty("name").GetString()!;
                var data = fixture.TryGetProperty("data", out var found) && found.ValueKind != JsonValueKind.Null && !refusals.Contains(name)
                    ? JsonNode.Parse(found.GetRawText())
                    : null;
                return (name, File("/fixtures/" + name), data, fixture.Clone());
            })
            .ToList();
    }

    /// <summary>Text as the reference writes it: an unpaired surrogate becomes ⟨U+XXXX⟩.</summary>
    private static string Shown(string text)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < text.Length; index++)
        {
            var paired = char.IsSurrogatePair(text, index) || (index > 0 && char.IsSurrogatePair(text, index - 1));
            builder.Append(char.IsSurrogate(text[index]) && !paired ? $"⟨U+{(int)text[index]:X4}⟩" : text[index].ToString());
        }

        return builder.ToString();
    }

    /// <summary>A value in the reference's shape. PlantUML shows an empty YAML flow list as one empty item.</summary>
    private static JsonNode Shape(DataValue value, bool yaml) => value.Kind.WireValue switch
    {
        "object" => new JsonObject { ["object"] = new JsonArray(value.Members.Select(member => (JsonNode)new JsonArray(Shown(member.Key), Shape(member.Value, yaml))).ToArray()) },
        "array" when yaml && value.Items.IsEmpty => JsonNode.Parse("""{"array":[{"string":""}]}""")!,
        "array" => new JsonObject { ["array"] = new JsonArray(value.Items.Select(item => Shape(item, yaml)).ToArray()) },
        "string" => new JsonObject { ["string"] = Shown(value.Text) },
        "number" => new JsonObject { ["number"] = value.Text },
        "boolean" => new JsonObject { ["boolean"] = value.Text == "true" },
        _ => new JsonObject { ["null"] = true },
    };

    /// <summary>PlantUML puts a scalar or empty root in a one-item list; the library keeps the root as it is.</summary>
    private static JsonNode RootShape(DataTreeDiagram tree)
    {
        var root = tree.Root;
        var yaml = tree.Format.Equals(DataFormat.Yaml);

        return root.Kind.WireValue switch
        {
            "object" or "array" when root.Members.IsEmpty && root.Items.IsEmpty => JsonNode.Parse("""{"array":[{"string":""}]}""")!,
            "object" or "array" => Shape(root, yaml),
            _ => new JsonObject { ["array"] = new JsonArray(Shape(root, yaml)) },
        };
    }

    private static DataTreeDiagram Read(string source)
    {
        PlantUml.TryParse(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return DiagramMother.Of<DataTreeDiagram>(document);
    }

    private static DataTreeDiagram Json(string body) => Read($"@startjson\n{body}\n@endjson\n");

    private static DataTreeDiagram Yaml(string body) => Read($"@startyaml\n{body}\n@endyaml\n");

    private static XDocument Render(DataTreeDiagram tree) => XDocument.Parse(SvgRenderer.Render(Diagram.NewDataTree(tree)).Output!);

    [Test]
    public void EveryFixtureIsReadAsPlantUmlReadsItWithTheSameDataHighlightsAndTitle()
    {
        var mismatches = new List<string>();
        var fixtures = Fixtures().ToList();

        foreach (var (name, source, data, reference) in fixtures)
        {
            var read = PlantUml.TryParse(source, out var document, out var diagnostics);

            if (read != data is not null)
            {
                mismatches.Add($"{name}: accepted {read} {string.Join(", ", diagnostics)}");
                continue;
            }

            if (data is null)
            {
                continue;
            }

            var tree = DiagramMother.Of<DataTreeDiagram>(document);
            var highlights = reference.GetProperty("highlights").EnumerateArray()
                .Select(highlight => string.Join(" / ", highlight.GetProperty("path").EnumerateArray().Select(segment => segment.GetString())) + highlight.GetProperty("stereotype").GetString());
            var title = reference.GetProperty("title").ValueKind == JsonValueKind.Null ? null : reference.GetProperty("title").GetString();

            if (!JsonNode.DeepEquals(RootShape(tree), data) || tree.Title != title ||
                (!name.Contains("divergence", StringComparison.Ordinal) && !tree.Highlights.Select(highlight => highlight + highlight.Stereotype).SequenceEqual(highlights)))
            {
                mismatches.Add($"{name}: {RootShape(tree).ToJsonString()}");
            }
        }

        fixtures.Count.ShouldBe(144);
        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void PrintingKeepsTheTreeAndPrintingAgainGivesTheSameText()
    {
        var failures = new List<string>();

        foreach (var (name, source, _, _) in Fixtures().Where(fixture => fixture.Data is not null))
        {
            var tree = Read(source);
            PlantUml.TryPrint(Diagram.NewDataTree(tree), out var first, out var diagnostics).ShouldBeTrue($"{name}: {string.Join(", ", diagnostics)}");
            var again = Read(first!);
            PlantUml.TryPrint(Diagram.NewDataTree(again), out var second, out _).ShouldBeTrue();

            if (second != first || !JsonNode.DeepEquals(Shape(again.Root, false), Shape(tree.Root, false)) || again.Title != tree.Title)
            {
                failures.Add($"{name}: {first}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public void DuplicateMembersStayInOrderWithDistinctIdentities()
    {
        var tree = Json("""{"id": 1, "id": 2}""");

        tree.Root.Members.Select(member => member.Key).ShouldBe(["id", "id"]);
        tree.OccurrenceIds.ShouldBe(["$", "$/id", "$/id~2"]);
        tree.OccurrenceOf(tree.Root.Members[1].Value).ShouldBe("$/id~2");
    }

    [Test]
    public void NumbersKeepTheirLexemeAndQuotedTrueStaysText()
    {
        var tree = Json("""{"n": 123.4567890123456789, "a": "true", "b": true}""");

        tree.Root.Members[0].Value.Text.ShouldBe("123.4567890123456789");
        tree.Root.Members[1].Value.Kind.ShouldBe(DataValueKind.String);
        tree.Root.Members[2].Value.Kind.ShouldBe(DataValueKind.Boolean);
    }

    [Test]
    public void YamlScalarsAreTextAndAliasesAreNotExpanded()
    {
        var tree = Yaml("enabled: true\na: &x value\nb: *x");

        tree.Root.Members.Select(member => $"{member.Value.Kind}:{member.Value.Text}").ShouldBe(["string:true", "string:&x value", "string:*x"]);
    }

    [Test]
    public void AQuotedHighlightSegmentKeepsItsSlash()
    {
        var tree = Json("#highlight \"a/b\" / 0\n{\"a/b\": [1, 2], \"a\": {\"b\": [3]}}");

        tree.HighlightedIds.ShouldBe(["$/a~1b/0"]);
    }

    [Test]
    public void ASelectorThatSelectsNothingIsReported()
    {
        PlantUml.TryParse("@startjson\n#highlight \"missing\"\n{\"a\": 1}\n@endjson\n", out var document, out _).ShouldBeTrue();

        document!.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("plantuml/unresolved-highlight");
    }

    [Test]
    public void ScalarRootsAndEmptyContainersAreKept()
    {
        Json("42").Root.Kind.ShouldBe(DataValueKind.Number);
        var empty = Json("{}");
        empty.Root.Kind.ShouldBe(DataValueKind.Object);
        Render(empty).Descendants(Svg + "tspan").Select(text => text.Value).ShouldContain("{ }");
    }

    [Test]
    public void AnArrayOfThreeObjectsLinksToThreeTablesInOrder()
    {
        var svg = Render(Json("""{"items": [{"a": 1}, {"a": 2}, {"a": 3}]}"""));

        svg.Descendants().Where(element => (string?)element.Attribute("class") == "edge").Select(edge => edge.Attribute("data-ref")!.Value).ShouldBe([
            "edge=$%2Fitems/$%2Fitems%2F0/0", "edge=$%2Fitems/$%2Fitems%2F1/0", "edge=$%2Fitems/$%2Fitems%2F2/0", "edge=$/$%2Fitems/0",
        ], ignoreOrder: true);
        svg.Descendants().Count(element => (string?)element.Attribute("data-kind") == "object").ShouldBe(4);
    }

    [Test]
    public void AHighlightRestylesOnlyItsRowAndMovesNothing()
    {
        static string Geometry(XDocument svg) => string.Join("\n", svg.Descendants()
            .Where(element => element.Name.LocalName is "rect" or "path" or "text" or "tspan")
            .Select(element => string.Join(" ", element.Attributes()
                .Where(attribute => attribute.Name.LocalName is not ("fill" or "stroke" or "stroke-width" or "class"))
                .Select(attribute => attribute.ToString()))));

        const string Body = "server:\n  host: example.org\n  port: 8443";
        var plain = Render(Yaml(Body));
        var marked = Render(Yaml("#highlight \"server\" / \"port\"\n" + Body));

        Geometry(marked).ShouldBe(Geometry(plain));
        marked.Descendants().Where(element => ((string?)element.Attribute("class"))?.Contains("highlighted", StringComparison.Ordinal) == true)
            .ShouldHaveSingleItem().Attribute("data-ref")!.Value.ShouldBe("node=$%2Fserver%2Fport");
    }

    [Test]
    public void ALongValueWrapsAndKeepsAllOfItsText()
    {
        var text = string.Join(" ", Enumerable.Repeat("wrapped", 20));
        var lines = Render(Json($$"""{"note": "{{text}}"}""")).Descendants(Svg + "tspan").Select(line => line.Value).Where(line => line.StartsWith("wrapped", StringComparison.Ordinal)).ToList();

        lines.Count.ShouldBeGreaterThan(1);
        string.Join(" ", lines).ShouldBe(text);
    }

    [Test]
    public void ValuesNestedPastTheDepthLimitAreRefusedWithALimitDiagnostic()
    {
        PlantUml.TryParse($"@startjson\n{new string('[', 70)}{new string(']', 70)}\n@endjson\n", out _, out var diagnostics).ShouldBeFalse();

        diagnostics.ShouldHaveSingleItem().Code.ShouldEndWith("-limit");
    }

    [Test]
    public void ThePlayerVisitsValuesInMemberOrderWithoutConnectorSteps()
    {
        using var data = AnimatedDiagramTests.ReadMotionData(DiagramSource.RenderAnimated("@startjson\n{\"a\": 1, \"b\": [true]}\n@endjson\n").Output!);

        data.RootElement.GetProperty("steps").GetArrayLength().ShouldBe(0);
        data.RootElement.GetProperty("visits").EnumerateArray().Select(visit => visit.GetProperty("ref").GetString())
            .ShouldBe(["node=$", "node=$%2Fa", "node=$%2Fb", "node=$%2Fb%2F0"]);
    }
}
