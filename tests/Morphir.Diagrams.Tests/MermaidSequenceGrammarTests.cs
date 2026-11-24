// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>The Mermaid 12.2.0 sequence grammar beyond the first subset, checked against the pinned examples.</summary>
public class MermaidSequenceGrammarTests
{
    private const string Lifecycle = """
        sequenceDiagram
          participant A
          create participant B
          A->>B: new
          A->>B: work
          destroy B
          A->>B: stop
        """;

    [Test]
    public void EveryPinnedExampleIsReadWithTheReferenceParticipantsAndMessages()
    {
        var files = DiagramCorpus.Files(FixtureCatalog.DiagramsMbt.Sequence);
        var mismatches = new List<string>();

        for (var number = 1; number <= 38; number++)
        {
            var source = files[$"tests/corpus/mermaid-12.2.0/examples/{number:00}.mmd"];
            using var reference = JsonDocument.Parse(files[$"tests/corpus/mermaid-12.2.0/reference/{number:00}.json"]);
            var model = reference.RootElement.GetProperty("model");
            var actors = model.GetProperty("actors").EnumerateArray().Select(actor => actor.GetProperty("id").GetString()).ToList();
            var messages = model.GetProperty("messages").EnumerateArray().Count(message =>
                message.GetProperty("from").ValueKind == JsonValueKind.String
                && message.GetProperty("to").ValueKind == JsonValueKind.String
                && message.GetProperty("type").GetInt32() != 2);

            if (!Mermaid.TryParse(source, out var document, out var diagnostics))
            {
                mismatches.Add($"{number:00}: {string.Join(", ", diagnostics)}");
                continue;
            }

            var sequence = DiagramMother.SequenceOf(document);

            if (!sequence.Participants.Select(participant => participant.Id.Value).SequenceEqual(actors) || sequence.Messages.Length != messages)
            {
                mismatches.Add($"{number:00}: {sequence}");
            }
        }

        mismatches.ShouldBeEmpty();
    }

    [Test]
    public void CreationAndDestructionAreTheMessagesAfterTheirStatements()
    {
        var b = DiagramMother.SequenceOf(Parsed(Lifecycle)).Participants[1];

        b.CreatedBy.ShouldBe(0);
        b.DestroyedBy.ShouldBe(2);
    }

    [Test]
    public void ADestroyedParticipantCannotBeUsedAgain()
    {
        Mermaid.TryParse(Lifecycle + "\n  B->>A: late\n", out _, out var diagnostics).ShouldBeFalse();

        var refusal = diagnostics.ShouldHaveSingleItem();
        refusal.Code.ShouldBe("mermaid/destroyed-participant");
        refusal.Span.ShouldNotBeNull().Start.Line.ShouldBe(8);
    }

    [Test]
    [Arguments("participant Q@{ \"type\": \"queue\" }", "queue", "Q")]
    [Arguments("participant API@{ \"type\": \"boundary\", \"alias\": \"Public API\" }", "boundary", "Public API")]
    [Arguments("participant DB@{ \"type\": \"database\", \"alias\": \"Inner\" } as Outer", "database", "Outer")]
    [Arguments("actor U", "actor", "U")]
    public void ParticipantMetadataSetsTheKindAndLabel(string declaration, string kind, string label)
    {
        var participant = DiagramMother.SequenceOf(Parsed($"sequenceDiagram\n  {declaration}\n")).Participants.ShouldHaveSingleItem();

        participant.Kind.ToString().ShouldBe(kind);
        participant.Label.ShouldBe(label);
    }

    [Test]
    public void BoxesGroupTheParticipantsDeclaredInThem()
    {
        var sequence = DiagramMother.SequenceOf(Parsed("sequenceDiagram\n  box Purple Alice & John\n  participant A\n  participant J\n  end\n  box Other\n  participant B\n  end\n  A->>B: hi\n"));

        sequence.Boxes.Select(box => (box.Label, box.Color, string.Join(",", box.Participants.Select(id => id.Value))))
            .ShouldBe([("Alice & John", "Purple", "A,J"), ("Other", null, "B")]);
    }

    [Test]
    public void CentralConnectionsAreReadAtEitherEnd()
    {
        var messages = DiagramMother.SequenceOf(Parsed("sequenceDiagram\n  A->>()B: in\n  A()->>B: out\n  B()->>()A: both\n")).Messages;

        messages.Select(message => (message.StartsAtCentre, message.EndsAtCentre)).ShouldBe([(false, true), (true, false), (true, true)]);
    }

    [Test]
    public void LinksAreKeptAsUnappliedPresentation()
    {
        var document = Parsed("sequenceDiagram\n  participant A\n  link A: Wiki @ https://example.org\n");

        document.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("mermaid/unapplied-presentation");
    }

    private static DiagramDocument Parsed(string source)
    {
        Mermaid.TryParse(source, out var document, out var diagnostics).ShouldBeTrue(string.Join(", ", diagnostics));
        return document!;
    }
}
