// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>
/// Pins the offline player and the scene that the player and the GIF read, for one diagram of each family, so that
/// moving the writers onto the layout model changes neither. A changed digest is a defect to fix.
/// </summary>
public class PlayerBaselineTests
{
    /// <summary>The scenes: each family's representative, the checkout with embedded metadata, and a railroad trace.</summary>
    public static IReadOnlyList<(string Name, DiagramScene Scene)> Scenes()
    {
        static Diagram Parse(string source) =>
            DiagramSource.TryParse(source, out var document, out var errors) ? document.Diagram : throw new InvalidOperationException(string.Join(", ", errors));

        var scenes = DiagramCorpus.Representatives()
            .Select(item => (item.Family, SvgRenderer.BuildScene(Parse(item.Diagram.Source))))
            .ToList();

        var checkout = DiagramMother.Read(DiagramMother.Checkout);
        scenes.Add(("flowchart-metadata", SvgRenderer.BuildScene(checkout, CarriageMother.Options(CarriageMother.CommentDataset(), MetadataEmbedding.RdfXml))));

        var railroad = (Diagram.Railroad)Parse(DiagramCorpus.Representatives().Single(item => item.Family == "railroad").Diagram.Source);
        var model = railroad.Item;
        var taken = model.OccurrenceIds.Take(2).Select(occurrence => RailroadVisit.Create(occurrence, RailroadVisitResult.Matched));
        var trace = RailroadTrace.Create(model, RailroadOutcome.NotMatched, taken, model.OccurrenceIds[2]);
        scenes.Add(("railroad-trace", SvgRenderer.BuildScene(railroad, SvgRenderOptions.Default.WithRailroadTrace(trace))));

        return scenes;
    }

    /// <summary>The connector bindings of a scene, one line each.</summary>
    public static string Connectors(DiagramScene scene)
    {
        var output = new StringBuilder();

        foreach (var connector in scene.Connectors)
        {
            output.Append(connector.Ordinal.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(connector.Source).Append(' ')
                .Append(connector.Target).Append(' ').Append(connector.IsReversed).Append(' ').Append(connector.PathData).Append('\n');
        }

        return output.ToString();
    }

    [Test]
    public void ThePlayerAndTheSceneOfEachFamilyKeepTheirBaseline()
    {
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, scene) in Scenes())
        {
            digests.Add(name + " player", DigestBaseline.Digest(AnimatedRenderer.Render(scene).Output.ShouldNotBeNull()));
            digests.Add(name + " connectors", DigestBaseline.Digest(Connectors(scene)));
            digests.Add(name + " svg", DigestBaseline.Digest(scene.Document.Render()));
        }

        DigestBaseline.Differences(FixtureCatalog.DiagramExport.PlayerDigests, digests).ShouldBeEmpty();
    }
}
