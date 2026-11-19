// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>ZenUML read into the shared sequence model, checked against the pinned ZenUML core 3.47.9 semantics.</summary>
public class ZenUmlTests
{
    [Test]
    public void EveryReferenceExampleGivesTheReferenceParticipantsAndMessages()
    {
        using var reference = JsonDocument.Parse(PinnedCorpus.File(FixtureCatalog.DiagramsMbt.ZenUml, "/semantics.json"));
        var mismatches = new List<string>();
        var count = 0;

        foreach (var example in reference.RootElement.GetProperty("examples").EnumerateArray())
        {
            count++;
            var name = example.GetProperty("id").GetString();

            if (!Mermaid.TryParse(example.GetProperty("source").GetString(), out var document, out var diagnostics))
            {
                mismatches.Add($"{name}: {string.Join(", ", diagnostics)}");
                continue;
            }

            var sequence = DiagramMother.SequenceOf(document);
            var participants = example.GetProperty("participants").EnumerateArray().Select(participant => participant.GetProperty("id").GetString());

            var messages = example.GetProperty("messages").EnumerateArray().Select(message =>
            {
                var kind = message.GetProperty("kind").GetString();
                var dashed = kind is "return" or "assignment-return";
                return $"{message.GetProperty("from").GetString()}>{message.GetProperty("to").GetString()}:{message.GetProperty("label").GetString()}:{dashed}";
            });

            var ours = sequence.Messages.Select(message => $"{message.Sender}>{message.Receiver}:{message.Text}:{message.IsDashed}");

            if (!sequence.Participants.Select(participant => participant.Id.Value).SequenceEqual(participants) || !ours.SequenceEqual(messages))
            {
                mismatches.Add($"{name}: [{string.Join(", ", sequence.Participants)}] {string.Join(" | ", ours)}");
            }
        }

        count.ShouldBe(25);
        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void TryCatchAndFinallyAreOneFrameWithThreeSections()
    {
        Mermaid.TryParse("zenuml\n  try {\n    A.save()\n  } catch(e) {\n    A.log()\n  } finally {\n    A.close()\n  }\n", out var document, out _)
            .ShouldBeTrue();

        var frame = DiagramMother.SequenceOf(document).Events.ShouldHaveSingleItem().Match(
            message: _ => null!, note: _ => null!, activation: _ => null!, frame: frame => frame);

        frame.Kind.ShouldBe(SequenceFrameKind.Try);
        frame.Sections.Select(section => section.Label).ShouldBe(["try", "catch(e)", "finally"]);
    }

    [Test]
    public void AnnotatorsChooseTheParticipantSymbol()
    {
        Mermaid.TryParse("zenuml\n  @Actor Alice\n  @Database Store as Orders\n  Alice->Store: save\n", out var document, out _).ShouldBeTrue();

        DiagramMother.SequenceOf(document).Participants.Select(participant => (participant.Kind.ToString(), participant.Label))
            .ShouldBe([("actor", "Alice"), ("database", "Orders")]);
    }

    [Test]
    public void ACallWithABodyActivatesItsReceiverForTheBody()
    {
        Mermaid.TryParse("zenuml\n  A.method() {\n    B.nested()\n  }\n", out var document, out _).ShouldBeTrue();

        var activations = DiagramMother.SequenceOf(document).Events.Select(sequenceEvent => sequenceEvent.ToString()).ToList();
        activations.Count.ShouldBe(4);
    }

    [Test]
    public void EveryMalformedStatementIsReportedWithItsLine()
    {
        Mermaid.TryParse("zenuml\n  A->B: ok\n  ???\n  A->B: fine\n  }\n", out _, out var diagnostics).ShouldBeFalse();

        diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Span!.Start.Line))
            .ShouldBe([("zenuml/unknown-statement", 3), ("zenuml/unexpected-brace", 5)]);
    }

    [Test]
    public void PrintingWhatWasPrintedGivesTheSameZenUmlForEveryReferenceExample()
    {
        using var reference = JsonDocument.Parse(PinnedCorpus.File(FixtureCatalog.DiagramsMbt.ZenUml, "/semantics.json"));
        var failures = new List<string>();

        foreach (var example in reference.RootElement.GetProperty("examples").EnumerateArray())
        {
            Mermaid.TryParse(example.GetProperty("source").GetString(), out var document, out _).ShouldBeTrue();

            if (!Mermaid.TryPrintZenUml(document!.Diagram, out var first, out var diagnostics))
            {
                failures.Add($"{example.GetProperty("id")}: {string.Join(", ", diagnostics)}");
            }
            else if (!Mermaid.TryParse(first, out var reread, out var errors) || !Mermaid.TryPrintZenUml(reread!.Diagram, out var second, out _) || second != first)
            {
                failures.Add($"{example.GetProperty("id")}: {string.Join(", ", errors)}\n{first}");
            }
            else if (DiagramMother.SequenceOf(reread).Messages.Length != DiagramMother.SequenceOf(document).Messages.Length)
            {
                failures.Add($"{example.GetProperty("id")}: messages differ\n{first}");
            }
        }

        failures.ShouldBeEmpty();
    }
}
