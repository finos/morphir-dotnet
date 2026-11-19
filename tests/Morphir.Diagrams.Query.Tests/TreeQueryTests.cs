// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using Morphir.Bpmn;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Query.Tests;

/// <summary>Compiling and running version 1 queries over BPMN documents.</summary>
public partial class TreeQueryTests
{
    private static IReadOnlyDictionary<string, string> Corpus()
    {
        using var document = JsonDocument.Parse(FixtureCatalog.ReadText(FixtureCatalog.DiagramsMbt.Bpmn));
        return document.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
            file => file.GetProperty("path").GetString()!.Split("tests/")[1],
            file => file.GetProperty("text").GetString()!,
            StringComparer.Ordinal);
    }

    private static QueryTree Tree(string source)
    {
        BpmnDocument.TryRead(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return BpmnQueryTree.Create(document);
    }

    private static TreeQuery Compile(string source, QueryLimits? limits = null)
    {
        TreeQuery.TryCompile(source, null, limits ?? QueryLimits.Default, out var query, out var diagnostic).ShouldBeTrue(diagnostic?.ToString());
        return query;
    }

    /// <summary>Every match and the final status, read in batches of a size.</summary>
    private static List<string> Read(QueryCursor cursor, int batch)
    {
        var rows = new List<string>();

        while (true)
        {
            var read = cursor.Next(batch);
            rows.AddRange(read.Matches.Select(match => $"match {match.PatternIndex}: " + string.Join(" ", match.Captures.Select(capture => $"{capture.Name}=#{capture.Node.Ordinal}"))));

            if (!read.Status.Equals(QueryStatus.More))
            {
                rows.Add(read.Status.Equals(QueryStatus.Complete) ? "end complete" : $"end failed {read.Status}");
                return rows;
            }
        }
    }

    private const string Process = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" targetNamespace="urn:q">
          <bpmn:process id="P">
            <bpmn:userTask id="A" name="Approve"/><bpmn:userTask id="B" name="Reject"/><bpmn:serviceTask id="S"/>
            <bpmn:sequenceFlow id="F" sourceRef="A" targetRef="S"/>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>The cases of the pinned diagrams.mbt query corpus whose output this library produces: matches, statuses and errors.</summary>
    [Test]
    public void TheQueryCorpusGivesTheSameMatchesAndErrors()
    {
        var corpus = Corpus();
        static IEnumerable<(string Name, string Body)> Split(string text) =>
            CaseHeader().Split(text).Skip(1).Select(part => (part[..part.IndexOf('\n')].Trim(), part[(part.IndexOf('\n') + 1)..]));
        var cases = Split(corpus["query-corpus/fixtures/cases.txt"]).ToDictionary(item => item.Name, item => item.Body);
        var mismatches = new List<string>();
        var compared = 0;

        foreach (var (name, expected) in Split(corpus["query-corpus/fixtures/expected.txt"]))
        {
            var body = cases[name];
            string? Key(string key) => Regex.Match(body, $"(?m)^{key}: (.*)$") is { Success: true } found ? found.Groups[1].Value.Trim() : null;
            var document = Key("document")!;

            // The generated deep document, projections and output sizes belong to upstream's harness.
            if (document == "generated:deep" || name.Contains("output", StringComparison.Ordinal) || name.Contains("projection", StringComparison.Ordinal) || name.StartsWith("work-limit", StringComparison.Ordinal))
            {
                continue;
            }

            compared++;
            var source = Key("query-json") is { } json ? JsonSerializer.Deserialize<string>(json)! : body[(body.IndexOf("query:\n", StringComparison.Ordinal) + 7)..];
            var namespaces = NamespaceLine().Matches(body).ToDictionary(found => found.Groups[1].Value, found => found.Groups[2].Value);
            List<string> ours;

            QueryLimits? limits = QueryLimits.Default;
            if (Key("limits") is { } text)
            {
                var values = text.Split(' ').Select(long.Parse).ToArray();
                limits = values.Take(3).All(value => value > 0) ? QueryLimits.Create(65_536, 256, 64, values[0], (int)values[1], 64, (int)values[2]) : null;
            }

            if (limits is null)
            {
                ours = ["end failed invalid-limits"];
            }
            else if (!TreeQuery.TryCompile(source, namespaces, limits, out var query, out var diagnostic))
            {
                ours = [$"error {diagnostic.Code} {diagnostic.Start}-{diagnostic.End}"];
            }
            else
            {
                var tree = Tree(corpus[$"query-corpus/fixtures/{document}"]);
                ours = Read(query.Run(tree, Key("root-id") is { } root ? tree.Find(root) : null), 3)
                    .ToList();
            }

            var theirs = expected.Split('\n').Select(line =>
                    line.StartsWith("match ", StringComparison.Ordinal) ? CaptureDetail().Replace(line, "=#$1")
                    : line.StartsWith("error", StringComparison.Ordinal) ? string.Join(" ", line.Split(' ').Take(3))
                    : line.Trim())
                .Where(line => line.StartsWith("match", StringComparison.Ordinal) || line.StartsWith("end", StringComparison.Ordinal) || line.StartsWith("error", StringComparison.Ordinal))
                .ToList();

            if (!ours.SequenceEqual(theirs))
            {
                mismatches.Add($"{name}: {string.Join(" | ", ours)}");
            }
        }

        compared.ShouldBe(34);
        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void UserTasksNamedApproveAreCapturedAsTask()
    {
        var matches = Compile("((bpmn.userTask) @task (#attr-eq? @task \"name\" \"Approve\"))").Run(Tree(Process), null).Next(10).Matches;

        matches.ShouldHaveSingleItem().Captures.ShouldHaveSingleItem().Name.ShouldBe("task");
        matches[0].Captures[0].Node.Id.ShouldBe("A");
    }

    [Test]
    public void AQuantifierFailsToCompileAtItsPosition()
    {
        TreeQuery.TryCompile("(bpmn.task)+", null, QueryLimits.Default, out _, out var diagnostic).ShouldBeFalse();

        (diagnostic.Code, diagnostic.Start, diagnostic.End).ShouldBe(("query/unsupported-quantifier", 11, 12));
    }

    [Test]
    public void TwoAlternativesThatMatchOneNodeGiveTwoMatchesInPatternOrder() =>
        Read(Compile("[(bpmn.userTask) @first (category.activity) @second]").Run(Tree(Process), null), 64)
            .Take(2).ShouldBe(["match 0: first=#3", "match 0: second=#3"]);

    [Test]
    public void BatchSizesOf1And64And256GiveTheSameMatches()
    {
        var tree = Tree(Corpus()["traversal-corpus/fixtures/process.bpmn"]);
        var query = Compile("(_) @node\n(bpmn.process (category.activity) @a (category.event) @e)");

        var single = Read(query.Run(tree, null), 1);
        Read(query.Run(tree, null), 64).ShouldBe(single);
        Read(query.Run(tree, null), 256).ShouldBe(single);
        single.Count.ShouldBeGreaterThan(30);
    }

    [Test]
    public void ALimitIsCumulativeAcrossBatchesAndNamesItsBound()
    {
        var query = Compile("(category.activity) @a", QueryLimits.Create(65_536, 256, 64, 10_000, 100, 64, 2));

        Read(query.Run(Tree(Process), null), 1).ShouldBe(["match 0: a=#3", "match 0: a=#4", "end failed limit results"]);
    }

    [Test]
    public void AReferenceAttributeDoesNotMakeAChild() =>
        Read(Compile("(bpmn.sequenceFlow (_) @target)").Run(Tree(Process), null), 64).ShouldBe(["end complete"]);

    [GeneratedRegex("(?m)^=== ")]
    private static partial Regex CaseHeader();

    [GeneratedRegex("(?m)^namespaces: (\\S+) (\\S+)$")]
    private static partial Regex NamespaceLine();

    [GeneratedRegex(@"=[^ ]*?#(\d+)\{.*?\}(?= [a-z]+=|$)")]
    private static partial Regex CaptureDetail();
}
