// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Bpmn.Tests;

/// <summary>Reading, the typed view, validation, writing, traversal and graph queries of BPMN documents.</summary>
public partial class BpmnDocumentTests
{
    private const string Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    /// <summary>The files of the pinned diagrams.mbt BPMN corpora, by their path below <c>tests/</c>.</summary>
    private static IReadOnlyDictionary<string, string> Corpus()
    {
        using var document = JsonDocument.Parse(FixtureCatalog.ReadText(FixtureCatalog.DiagramsMbt.Bpmn));

        return document.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
            file => file.GetProperty("path").GetString()!.Split("tests/")[1],
            file => file.GetProperty("text").GetString()!,
            StringComparer.Ordinal);
    }

    private static BpmnDocument Read(string source)
    {
        BpmnDocument.TryRead(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return document;
    }

    private static string Process(string body, string prefix = "bpmn") =>
        $"""<{prefix}:definitions xmlns:{prefix}="{Model}" id="Defs" targetNamespace="urn:test"><{prefix}:process id="P">{body.Replace("bpmn:", prefix + ":", StringComparison.Ordinal)}</{prefix}:process></{prefix}:definitions>""";

    [Test]
    public void ABillionLaughsDeclarationIsRefusedBeforeAnyExpansion()
    {
        const string Source = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">]>
            <definitions>&lol2;</definitions>
            """;

        BpmnDocument.TryRead(Source, out var document, out var diagnostics).ShouldBeFalse();
        document.ShouldBeNull();
        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("xml/prohibited-declaration");
    }

    [Test]
    public void MalformedXmlIsReportedWithItsPlace()
    {
        BpmnDocument.TryRead(Corpus()["traversal-corpus/fixtures/broken.bpmn"], out _, out var diagnostics).ShouldBeFalse();

        var finding = diagnostics.ShouldHaveSingleItem();
        (finding.Code, finding.Kind, finding.Line).ShouldBe(("xml/malformed", BpmnFindingKind.Xml, 3));
    }

    [Test]
    public void APrefixAliasGivesTheSameTypedDocument()
    {
        static string View(BpmnDocument document) =>
            string.Join(" ", document.Elements.Select(element => $"{element.NamespaceUri}|{element.Kind}|{element.Id}|{element.Parent?.Id}"));

        const string Body = """<bpmn:startEvent id="S"/><bpmn:task id="T" name="Work"/><bpmn:sequenceFlow id="F" sourceRef="S" targetRef="T"/>""";

        View(Read(Process(Body, "b"))).ShouldBe(View(Read(Process(Body))));
    }

    [Test]
    public void DecimalsAndExtensionsAreWrittenAsTheyWereRead()
    {
        const string Source = """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" xmlns:v="urn:vendor" id="D" targetNamespace="urn:t">
              <bpmn:process id="P" v:owner="ops"><bpmn:extensionElements><v:retry count="3"/></bpmn:extensionElements></bpmn:process>
              <di:waypoint x="123.4567890123456789" y="1e3"/>
            </bpmn:definitions>
            """;

        var written = Read(Source).Write(BpmnWriteFormat.Canonical);

        written.ShouldContain("""x="123.4567890123456789" y="1e3""");
        written.ShouldContain("""<bpmn:process id="P" v:owner="ops"><bpmn:extensionElements><v:retry count="3"/></bpmn:extensionElements></bpmn:process>""");
    }

    [Test]
    public void EveryCorpusDocumentHasAStableCanonicalFormThatPrettyWritingKeeps()
    {
        var failures = new List<string>();

        foreach (var (path, source) in Corpus().Where(file => file.Key.EndsWith(".bpmn", StringComparison.Ordinal) && !file.Key.Contains("broken", StringComparison.Ordinal)))
        {
            var canonical = Read(source).Write(BpmnWriteFormat.Canonical);
            var again = Read(canonical).Write(BpmnWriteFormat.Canonical);
            var throughPretty = Read(Read(source).Write(BpmnWriteFormat.Pretty)).Write(BpmnWriteFormat.Canonical);

            if (again != canonical || throughPretty != canonical)
            {
                failures.Add(path);
            }
        }

        failures.ShouldBeEmpty();
    }

    [Test]
    public void TheTypedViewMatchesTheBpmnModdleReferenceOfEachModelDocument()
    {
        // Elements named for their role carry the schema type of the role.
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ioSpecification"] = "InputOutputSpecification",
            ["childLaneSet"] = "LaneSet",
            ["inputDataItem"] = "DataInput",
            ["outputDataItem"] = "DataOutput",
            ["transformation"] = "FormalExpression",
            ["event"] = "ImplicitThrowEvent",
        };
        var corpus = Corpus();
        var mismatches = new List<string>();

        foreach (var name in new[] { "activities", "choreography-conversation", "collaboration", "di-views", "events" })
        {
            var document = Read(corpus[$"corpus/bpmn-model/{name}.bpmn"]);
            using var reference = JsonDocument.Parse(corpus[$"corpus/bpmn-model/{name}.reference.json"]);

            foreach (var node in reference.RootElement.GetProperty("nodes").EnumerateArray().Where(node => !node.GetProperty("key").GetString()!.Contains('/')))
            {
                var id = node.GetProperty("key").GetString()![1..];
                var element = document.Find(id).Match<BpmnElement?>(found => found, () => null, _ => null);

                if (element is null)
                {
                    mismatches.Add($"{name} {id}: not found");
                    continue;
                }

                var type = node.GetProperty("type").GetString()!.Split(':')[1];
                var parent = node.GetProperty("parent").ValueKind == JsonValueKind.String ? node.GetProperty("parent").GetString()![1..] : null;

                if (!string.Equals(roles.GetValueOrDefault(element.Kind, element.Kind), type, StringComparison.OrdinalIgnoreCase) ||
                    (parent is not null && !parent.Contains('/') && element.Parent?.Id != parent))
                {
                    mismatches.Add($"{name} {id}: {element.Kind} in {element.Parent?.Id}");
                }

                foreach (var field in node.GetProperty("fields").EnumerateObject())
                {
                    var value = field.Value.GetProperty("value");
                    var ours = field.Name == "@body" ? element.Text : element.Field(field.Name);
                    var same = field.Value.GetProperty("kind").GetString() switch
                    {
                        "reference" => element.References(field.Name).Select(text => text[(text.LastIndexOf(':') + 1)..])
                            .SequenceEqual(value.EnumerateArray().Select(item => item.GetString()!)),
                        "boolean" => ours == (value.GetBoolean() ? "true" : "false"),
                        "number" => ours is not null && double.Parse(ours, CultureInfo.InvariantCulture) == value.GetDouble(),
                        _ => ours == value.GetString(),
                    };

                    if (!same)
                    {
                        mismatches.Add($"{name} {id}.{field.Name}: {ours}");
                    }
                }
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void DuplicateIdsAreReportedAndTheirLookupIsAmbiguous()
    {
        var document = Read(Corpus()["traversal-corpus/fixtures/duplicates.bpmn"]);

        document.Validate().Select(finding => finding.Code).ShouldBe(["bpmn/duplicate-id", "bpmn/ambiguous-reference"]);
        document.Find("A").ShouldBeOfType<BpmnLookup.Ambiguous>().Item.Length.ShouldBe(2);
    }

    [Test]
    public void AnUnresolvedReferenceKeepsItsValueAndAWrongTargetIsReported()
    {
        var document = Read(Process("""<bpmn:dataObject id="D"/><bpmn:task id="T"/><bpmn:sequenceFlow id="F" sourceRef="Gone" targetRef="D"/>"""));
        var flow = document.Find("F").ShouldBeOfType<BpmnLookup.Found>().Item;

        flow.Field("sourceRef").ShouldBe("Gone");
        document.Validate().Select(finding => (finding.Code, finding.Kind)).ShouldBe([
            ("bpmn/unresolved-reference", BpmnFindingKind.Reference),
            ("bpmn/wrong-reference-target", BpmnFindingKind.Reference),
        ]);
    }

    /// <summary>The route and reachability answers of the pinned traversal corpus over the edge kinds this library has.</summary>
    [Test]
    [Arguments("process")]
    [Arguments("duplicates")]
    [Arguments("choreography")]
    [Arguments("vendor")]
    [Arguments("crlf-nonbmp")]
    public void GraphQueriesGiveTheAnswersOfTheTraversalCorpus(string fixture)
    {
        var corpus = Corpus();
        var document = Read(corpus[$"traversal-corpus/fixtures/{fixture}.bpmn"]);
        var lines = corpus[$"traversal-corpus/expected/{fixture}.txt"].Split('\n');
        var mismatches = new List<string>();
        var checkedCount = 0;

        static IEnumerable<string> Ids(string text) =>
            IdOf().Matches(text).Select(match => match.Groups[1].Value).Where(id => id != "-");

        for (var index = 0; index < lines.Length - 1; index++)
        {
            var query = Query().Match(lines[index]);

            if (!query.Success || (query.Groups["kinds"].Success &&
                query.Groups["kinds"].Value.Split(',').Any(kind => !Enum.TryParse<BpmnEdgeKinds>(kind, out _))))
            {
                continue;
            }

            checkedCount++;
            var kinds = query.Groups["kinds"].Success
                ? query.Groups["kinds"].Value.Split(',').Aggregate(BpmnEdgeKinds.None, (all, kind) => all | Enum.Parse<BpmnEdgeKinds>(kind))
                : BpmnEdgeKinds.SequenceFlow;
            var direction = query.Groups["direction"].Value switch
            {
                "Reverse" => BpmnDirection.Reverse,
                "Both" => BpmnDirection.Both,
                _ => BpmnDirection.Forward,
            };
            var result = query.Groups["verb"].Value == "route"
                ? BpmnTraversal.Route(document, query.Groups["from"].Value, query.Groups["to"].Value, kinds, direction, 10_000)
                : BpmnTraversal.Reachable(document, query.Groups["from"].Value, kinds, direction, 10_000);
            var expected = lines[index + 1].Trim();

            var ours = result switch
            {
                BpmnGraphResult.Route route => "Found " + string.Join(" ", route.Item.Select(element => element.Id)),
                BpmnGraphResult.Reachable reachable => "Complete " + string.Join(" ", reachable.Item.Select(element => element.Id).OfType<string>().Order(StringComparer.Ordinal)),
                BpmnGraphResult.Ambiguous => "Ambiguous",
                BpmnGraphResult.Missing => "InvalidReference",
                _ => result.ToString(),
            };
            var theirs = expected switch
            {
                _ when expected.StartsWith("Found", StringComparison.Ordinal) => "Found " + string.Join(" ", Ids(expected.Split(" edges=")[0])),
                _ when expected.StartsWith("Complete", StringComparison.Ordinal) => "Complete " + string.Join(" ", Ids(expected).Order(StringComparer.Ordinal)),
                _ when expected.StartsWith("Failed Ambiguous", StringComparison.Ordinal) => "Ambiguous",
                _ when expected.StartsWith("Failed InvalidReference", StringComparison.Ordinal) => "InvalidReference",
                _ => expected,
            };

            if (ours != theirs)
            {
                mismatches.Add($"{lines[index]}: {ours} / {theirs}");
            }
        }

        checkedCount.ShouldBeGreaterThan(0);
        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void AReachabilityQueryInsideALoopEndsAndListsEachElementOnce()
    {
        var document = Read(Process("""
            <bpmn:task id="A"/><bpmn:task id="B"/><bpmn:task id="C"/>
            <bpmn:sequenceFlow id="AB" sourceRef="A" targetRef="B"/><bpmn:sequenceFlow id="BC" sourceRef="B" targetRef="C"/><bpmn:sequenceFlow id="CA" sourceRef="C" targetRef="A"/>
            """));

        BpmnTraversal.Reachable(document, "B", BpmnEdgeKinds.SequenceFlow, BpmnDirection.Forward, 100).ToString().ShouldBe("Reachable [B C A]");
        BpmnTraversal.Reachable(document, "B", BpmnEdgeKinds.SequenceFlow, BpmnDirection.Forward, 2).ShouldBe(BpmnGraphResult.LimitReached);
    }

    [Test]
    public void ADocumentThatNests10000ElementsIsTraversedAndWrittenWithoutAStackOverflow()
    {
        var source = new StringBuilder($"""<bpmn:definitions xmlns:bpmn="{Model}" id="D" targetNamespace="urn:deep">""");
        for (var level = 0; level < 10_000; level++) source.Append("<bpmn:extensionElements>");
        for (var level = 0; level < 10_000; level++) source.Append("</bpmn:extensionElements>");
        source.Append("</bpmn:definitions>");
        var document = Read(source.ToString());
        var deepest = 0;
        var visited = 0;

        BpmnTraversal.Walk(document, (node, depth) =>
        {
            visited++;
            deepest = Math.Max(deepest, depth);
            return BpmnTraversalControl.Continue;
        });

        (visited, deepest).ShouldBe((10_001, 10_000));
        document.Write(BpmnWriteFormat.Pretty).Length.ShouldBeGreaterThan(source.Length);
        BpmnTraversal.Reachable(document, "D", BpmnEdgeKinds.Containment, BpmnDirection.Forward, 20_000).ShouldBeOfType<BpmnGraphResult.Reachable>().Item.Length.ShouldBe(10_001);
    }

    [Test]
    public void ATraversalCanSkipChildrenAndStop()
    {
        var document = Read(Process("""<bpmn:subProcess id="S"><bpmn:task id="Inner"/></bpmn:subProcess><bpmn:task id="After"/><bpmn:task id="Last"/>"""));
        var seen = new List<string>();

        BpmnTraversal.Walk(document, (node, _) =>
        {
            if (node.Attribute("id") is { } id) seen.Add(id);
            return node.Attribute("id") switch
            {
                "S" => BpmnTraversalControl.SkipChildren,
                "After" => BpmnTraversalControl.Stop,
                _ => BpmnTraversalControl.Continue,
            };
        });

        seen.ShouldBe(["Defs", "P", "S", "After"]);
    }

    [GeneratedRegex(@"^> (?<verb>route|reachable) id:(?<from>\S+)(?: id:(?<to>\S+))?(?: direction=(?<direction>\S+)| kinds=(?<kinds>\S+))*$")]
    private static partial Regex Query();

    [GeneratedRegex(@"-#\d+\(([^)]*)\)")]
    private static partial Regex IdOf();
}
