// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using Morphir.Diagrams.Export;
using Morphir.Diagrams.Layout;
using Morphir.TestSupport.Fixtures;
using Shouldly;

namespace Morphir.Diagrams.Slides.Tests;

/// <summary>The scenarios of the revealjs-export specification.</summary>
public class RevealJsRendererTests
{
    private static string Deck(RevealJsRenderer renderer, params DiagramLayout[] pages)
    {
        var bytes = SlideRendering.Render(renderer, pages);
        bytes.AsSpan(0, 3).ToArray().ShouldNotBe(new byte[] { 0xEF, 0xBB, 0xBF });
        return Encoding.UTF8.GetString(bytes);
    }

    private static IReadOnlyList<string> Sections(string deck) =>
        Regex.Matches(deck, "<section>(.*?)</section>", RegexOptions.Singleline).Select(match => match.Groups[1].Value).ToList();

    [Test]
    public void TwoPagesGiveTwoSections()
    {
        var deck = Deck(new RevealJsRenderer(), SlideMother.Layout(SlideMother.Wide), SlideMother.Layout(SlideMother.Message));
        var sections = Sections(deck);

        sections.Count.ShouldBe(2);
        sections[0].ShouldContain("data-diagram=\"flowchart\"");
        sections[1].ShouldContain("data-diagram=\"sequence\"");
        deck.ShouldStartWith("<!DOCTYPE html>");
        new RevealJsRenderer().FileExtension.ShouldBe(".reveal.html");
        new RevealJsRenderer().MediaType.ShouldBe("text/html");
    }

    [Test]
    public void ALargeDiagramFitsTheSlide()
    {
        var corpus = DiagramCorpus.Representatives().Single(item => item.Family == "bpmn").Diagram.Source;
        var section = Sections(Deck(new RevealJsRenderer(), SlideMother.Layout(corpus))).Single();

        // reveal.js sizes an r-stretch element to the slide's remaining height, and the view box keeps its aspect.
        Regex.IsMatch(section, "<svg [^>]*class=\"r-stretch\"").ShouldBeTrue();
        section.ShouldContain("viewBox=");
    }

    [Test]
    public void TheSameDiagramTwiceHasUniqueIdentifiers()
    {
        var layout = SlideMother.Layout("flowchart LR\n  A[fa:fa-user Person] --> B[(Orders)]\n  B -.-> C{{Check}}\n");
        var deck = Deck(new RevealJsRenderer(), layout, layout);
        var ids = Regex.Matches(deck, "\\sid=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToList();
        var sections = Sections(deck);

        ids.ShouldNotBeEmpty();
        ids.Distinct().Count().ShouldBe(ids.Count);

        for (var index = 0; index < sections.Count; index++)
        {
            var own = Regex.Matches(sections[index], "\\sid=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var references = Regex.Matches(sections[index], "(?:url\\(#|href=\"#)([^)\"]+)").Select(match => match.Groups[1].Value).ToList();
            var selectors = Regex.Matches(sections[index], "svg#([A-Za-z0-9_-]+)").Select(match => match.Groups[1].Value).ToList();

            references.ShouldNotBeEmpty();
            references.ShouldAllBe(reference => own.Contains(reference));
            selectors.ShouldNotBeEmpty();
            selectors.ShouldAllBe(selector => own.Contains(selector));
            own.ShouldAllBe(id => id.StartsWith($"s{index + 1}-", StringComparison.Ordinal));
        }
    }

    [Test]
    public void ALocalCopyCanBeUsed()
    {
        var deck = Deck(new RevealJsRenderer(RevealJsOptions.Default.WithBaseAddress("./reveal/")), SlideMother.Layout(SlideMother.Wide));

        deck.ShouldContain("<script src=\"./reveal/dist/reveal.js\"></script>");
        deck.ShouldContain("<link rel=\"stylesheet\" href=\"./reveal/dist/reveal.css\">");
        Deck(new RevealJsRenderer(), SlideMother.Layout(SlideMother.Wide)).ShouldContain("https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/reveal.js");
        Should.Throw<ArgumentException>(() => RevealJsOptions.Default.WithBaseAddress(" "));
    }

    [Test]
    public void AnyDiagramIsNativeAndUnlowered()
    {
        var renderer = new RevealJsRenderer();
        renderer.Mode.ShouldBe(RenderMode.OriginalSvg);
        renderer.NativeFeatures.ShouldBe(LayoutFeatures.None);

        foreach (var source in DiagramCorpus.Sources().Where((_, index) => index % 5 == 0))
        {
            if (!DiagramSource.TryParse(source.Source, out var document, out _) || DiagramExport.Layout(document.Diagram).Layout is not { } layout) continue;
            DiagramExport.Assess([layout], renderer).Match(native: () => true, degraded: _ => false, unsupported: _ => false).ShouldBeTrue(source.ToString());
        }

        // The deck holds the drawing pass's own SVG, which lowering for a feature-less target would have changed.
        var curved = SlideMother.Layout("flowchart LR\n  A --> B\n  A --> C\n", SvgRenderOptions.Default.WithBridges(true));
        var svg = Regex.Match(Deck(renderer, curved), "<svg .*?</svg>", RegexOptions.Singleline).Value;
        svg.ShouldContain("<path");
        svg.ShouldContain("data-diagram=\"flowchart\"");
    }

    [Test]
    public void ATitleWithMarkupStaysText()
    {
        var deck = Deck(new RevealJsRenderer(RevealJsOptions.Default.WithTitle("<script>x</script>")), SlideMother.Layout(SlideMother.Wide));

        deck.ShouldContain("<title>&lt;script&gt;x&lt;/script&gt;</title>");
        Regex.Matches(deck, "<script").Count.ShouldBe(2);
        deck.ShouldContain("Reveal.initialize(");
    }

    [Test]
    public void EqualInputGivesEqualBytes()
    {
        var layout = SlideMother.Layout(SlideMother.Wide);
        SlideRendering.Render(new RevealJsRenderer(), layout).ShouldBe(SlideRendering.Render(new RevealJsRenderer(), layout));
    }
}
