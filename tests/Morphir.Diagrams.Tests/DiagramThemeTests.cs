// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;

namespace Morphir.Diagrams.Tests;

public partial class DiagramThemeTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private const string Flowchart = "flowchart LR\n  A[Read] --> B{Valid?}";
    private const string Sequence = "sequenceDiagram\n  Client->>API: Submit";

    public static IEnumerable<Func<(string Theme, ThemeMode Mode)>> BuiltInModes() =>
        from theme in DiagramThemes.All
        from mode in new[] { ThemeMode.Dark, ThemeMode.Light }
        select (Func<(string, ThemeMode)>)(() => (theme.Name, mode));

    [Test]
    public void BuiltInThemesAreListedWithArchifyAsTheDefault()
    {
        DiagramThemes.All.Select(theme => theme.Name)
            .ShouldBe(["Archify", "Monokai", "Cobalt", "Solarized", "Gruvbox", "Ocean", "Darcula", "Material"]);
        DiagramThemes.All.ShouldBe([
            DiagramThemes.Archify, DiagramThemes.Monokai, DiagramThemes.Cobalt, DiagramThemes.Solarized,
            DiagramThemes.Gruvbox, DiagramThemes.Ocean, DiagramThemes.Darcula, DiagramThemes.Material]);
        SvgRenderOptions.Default.Theme.ShouldBeSameAs(DiagramThemes.Archify);
        SvgRenderOptions.Default.Mode.ShouldBe(ThemeMode.Auto);
        Mermaid.RenderSvg(Flowchart, SvgRenderOptions.Default).Output
            .ShouldBe(Mermaid.RenderSvg(Flowchart).Output);
    }

    [Test]
    [MethodDataSource(nameof(BuiltInModes))]
    public void BuiltInThemePassesContrastForTextAndInks(string name, ThemeMode mode)
    {
        var theme = DiagramThemes.All.Single(candidate => candidate.Name == name);
        var options = SvgRenderOptions.Create(theme, mode);

        // Flowchart labels sit on family fills over the background.
        var flow = Variables(Mermaid.RenderSvg(Flowchart, options).Output);
        var background = flow["bg"];
        Contrast(flow["text"], background).ShouldBeGreaterThanOrEqualTo(4.5, "text");
        Contrast(flow["muted"], background).ShouldBeGreaterThanOrEqualTo(4.5, "muted");
        foreach (var family in ColorFamily.All)
        {
            var ink = flow[$"{family.WireValue}-ink"];
            var fill = flow[$"{family.WireValue}-fill"].CompositeOver(background);
            Contrast(ink, background).ShouldBeGreaterThanOrEqualTo(4.5, $"{family} ink on bg");
            Contrast(ink, fill).ShouldBeGreaterThanOrEqualTo(4.5, $"{family} ink on fill");
        }

        // Sequence labels sit on opaque mask chips.
        var sequence = Variables(Mermaid.RenderSvg(Sequence, options).Output);
        foreach (var family in ColorFamily.All)
        {
            var ink = sequence[$"{family.WireValue}-ink"];
            Contrast(ink, sequence["bg"]).ShouldBeGreaterThanOrEqualTo(4.5, $"{family} ink on bg");
            Contrast(ink, sequence["mask"]).ShouldBeGreaterThanOrEqualTo(4.5, $"{family} ink on mask");
        }
    }

    [Test]
    public void ContrastRatioFollowsWcag()
    {
        Color.Create("#000000").ContrastRatio(Color.Create("#ffffff")).ShouldBe(21, 0.001);
        Color.Create("#777777").ContrastRatio(Color.Create("#ffffff")).ShouldBe(4.48, 0.01);
        Color.Create("rgba(255, 255, 255, 0.5)").CompositeOver(Color.Create("#000000"))
            .ShouldBe(Color.Create("#808080"));
    }

    [Test]
    [Arguments("#ABC", "#aabbcc")]
    [Arguments("#1a2B3c", "#1a2b3c")]
    [Arguments("rgb(1,2,3)", "#010203")]
    [Arguments("rgba(1, 2, 3, 0.25)", "rgba(1, 2, 3, 0.25)")]
    [Arguments(" rgba( 10 , 20 , 30 , 1 ) ", "#0a141e")]
    public void ColorParsesHexAndRgbText(string text, string css)
    {
        Color.TryParse(text, out var color, out _).ShouldBeTrue();
        color.ToCss().ShouldBe(css);
        color.ToString().ShouldBe(css);
    }

    [Test]
    [Arguments(null)]
    [Arguments("red")]
    [Arguments("#12345")]
    [Arguments("rgba(256, 0, 0, 1)")]
    [Arguments("rgba(0, 0, 0, 1.5)")]
    [Arguments("rgb(0, 0)")]
    public void ColorRejectsUnsupportedText(string? text)
    {
        Color.TryParse(text, out _, out var failure).ShouldBeFalse();
        failure.Code.ShouldBe("theme/invalid-color");
        Should.Throw<ArgumentException>(() => Color.Create(text ?? ""));
    }

    [Test]
    public void CustomThemeComesThroughToTheSvgVariablesUnchanged()
    {
        var dark = Palette("#102030", "#f0f0f0", "#a0a8b0", "#0c1824", "#8090a0")
            .WithFamily(ColorFamily.Frontend,
                FamilyColors.Create(Color.Create("rgba(1, 2, 3, 0.25)"), Color.Create("#44ccff"), Color.Create("#abcdef")));
        var light = Palette("#fdfcfb", "#202020", "#5a5a5a", "#ffffff", "#606060");
        var theme = DiagramTheme.Create("Harbour", dark, light);

        var style = Style(Mermaid.RenderSvg(Flowchart, SvgRenderOptions.Default.WithTheme(theme)).Output);
        var blocks = Blocks(style);
        blocks.Length.ShouldBe(4);
        blocks[0]["bg"].ShouldBe("#102030");
        blocks[0]["mask"].ShouldBe("#0c1824");
        blocks[0]["arrow"].ShouldBe("#8090a0");
        blocks[0]["frontend-fill"].ShouldBe("rgba(1, 2, 3, 0.25)");
        blocks[0]["frontend-stroke"].ShouldBe("#44ccff");
        blocks[0]["frontend-ink"].ShouldBe("#abcdef");
        blocks[1]["bg"].ShouldBe("#fdfcfb");
        blocks[1]["text"].ShouldBe("#202020");
        blocks[2].ShouldBe(blocks[0], ignoreOrder: true);
        blocks[3].ShouldBe(blocks[1], ignoreOrder: true);
    }

    [Test]
    [Arguments(true, "#193549")]
    [Arguments(false, "#f2f6fa")]
    public void FixedModeEmitsASingleVariableBlock(bool dark, string background)
    {
        var mode = dark ? ThemeMode.Dark : ThemeMode.Light;
        var svg = XElement.Parse(Mermaid.RenderSvg(Sequence, SvgRenderOptions.Create(DiagramThemes.Cobalt, mode)).Output.ShouldNotBeNull());
        var style = svg.Descendants(Svg + "style").ShouldHaveSingleItem().Value;

        style.ShouldNotContain("@media");
        style.ShouldNotContain("data-theme");
        var block = Blocks(style).ShouldHaveSingleItem();
        block["bg"].ShouldBe(background);
        style.ShouldStartWith($"\n:where(svg#{(string?)svg.Attribute("id")}) {{");
    }

    [Test]
    public void TryCreateRejectsLowContrastPalettesAndEmptyNames()
    {
        var murky = Palette("#222222", "#333333", "#a0a0a0", "#1a1a1a", "#808080");
        var light = Palette("#fdfcfb", "#202020", "#5a5a5a", "#ffffff", "#606060");

        DiagramTheme.TryCreate("Murky", murky, light, out var theme, out var diagnostics).ShouldBeFalse();
        theme.ShouldBeNull();
        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("theme/text-contrast");
        diagnostic.Message.ShouldContain("dark");

        DiagramTheme.TryCreate(" ", light, light, out _, out diagnostics).ShouldBeFalse();
        diagnostics.ShouldHaveSingleItem().Code.ShouldBe("theme/name");
        Should.Throw<ArgumentException>(() => DiagramTheme.Create("Murky", murky, light));
        Should.Throw<ArgumentException>(() => DiagramTheme.Create(" ", light, light)).ParamName.ShouldBe("name");

        DiagramTheme.TryCreate("Clear", light, light, out theme, out diagnostics).ShouldBeTrue();
        theme.Name.ShouldBe("Clear");
        diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void DeriveLightGivesAReadablePaletteFromADarkOne()
    {
        var light = ThemePalette.DeriveLight(DiagramThemes.Monokai.Dark);
        Contrast(light.Text, light.Background).ShouldBeGreaterThanOrEqualTo(4.5);
        Contrast(light.Muted, light.Background).ShouldBeGreaterThanOrEqualTo(4.5);
        foreach (var family in ColorFamily.All)
            Contrast(light.Family(family).Stroke, light.Background).ShouldBeGreaterThanOrEqualTo(4.5, family.ToString());

        DiagramTheme.TryCreate("Monokai only", DiagramThemes.Monokai.Dark, light, out _, out _).ShouldBeTrue();
    }

    [Test]
    public void PlayerChromeTakesBackgroundAndTextFromTheTheme()
    {
        var dark = Mermaid.RenderAnimated(Sequence, SvgRenderOptions.Create(DiagramThemes.Cobalt, ThemeMode.Dark)).Output.ShouldNotBeNull();
        dark.ShouldContain("color-scheme: dark;");
        dark.ShouldContain("--diagram-page-bg: #193549;");
        dark.ShouldContain("--diagram-page-text: #ffffff;");

        var adaptive = Mermaid.RenderAnimated(Sequence, SvgRenderOptions.Default.WithTheme(DiagramThemes.Monokai)).Output.ShouldNotBeNull();
        adaptive.ShouldContain("color-scheme: light dark;");
        adaptive.ShouldContain("--diagram-page-bg: light-dark(#fafaf8, #272822);");
        adaptive.ShouldContain("--diagram-page-text: light-dark(#272822, #f8f8f2);");
    }

    private static ThemePalette Palette(string background, string text, string muted, string mask, string arrow)
    {
        FamilyColors Family(string stroke) =>
            FamilyColors.Create(Color.Create($"rgba({Hex(stroke, 1)}, {Hex(stroke, 3)}, {Hex(stroke, 5)}, 0.2)"), Color.Create(stroke));

        return ThemePalette.Create(
            background: Color.Create(background), text: Color.Create(text), muted: Color.Create(muted),
            mask: Color.Create(mask), arrow: Color.Create(arrow),
            frontend: Family("#2299cc"), backend: Family("#229966"), database: Family("#7755dd"),
            cloud: Family("#bb7711"), security: Family("#dd3355"), messageBus: Family("#dd6622"),
            external: Family("#667788"));

        static int Hex(string value, int start) => Convert.ToInt32(value.Substring(start, 2), 16);
    }

    private static double Contrast(Color first, Color second) => first.ContrastRatio(second);

    private static string Style(string? svg) =>
        XElement.Parse(svg.ShouldNotBeNull()).Descendants(Svg + "style").ShouldHaveSingleItem().Value;

    private static Dictionary<string, Color> Variables(string? svg) =>
        Blocks(Style(svg)).First().ToDictionary(pair => pair.Key, pair => Color.Create(pair.Value));

    private static Dictionary<string, string>[] Blocks(string style) =>
        BlockPattern().Matches(style)
            .Select(block => VariablePattern().Matches(block.Groups["body"].Value)
                .ToDictionary(variable => variable.Groups["name"].Value, variable => variable.Groups["value"].Value))
            .ToArray();

    [GeneratedRegex(@"\{(?<body>[^{}]*--bg:[^{}]*)\}")]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"--(?<name>[a-z-]+): (?<value>[^;]+);")]
    private static partial Regex VariablePattern();
}
