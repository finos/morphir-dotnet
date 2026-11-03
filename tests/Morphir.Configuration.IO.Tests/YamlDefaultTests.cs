using FluentAssertions;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO.Tests;

public sealed class YamlDefaultTests
{
    private static ConfigurationDocument Read(string text) => ((SourceContent.Available)ConfigurationReader.ReadText("default.yaml", text).Content).Item;
    private static ConfigValue At(ConfigurationDocument document, params string[] path) => ConfigTree.tryFind(ListModule.OfSeq(path), document.Values).Value;

    [Test]
    public void DefaultProfileExpandsAliasesAndPreservesUsePositions()
    {
        var document = Read("a: &x {value: 42}\nb: *x\n");
        At(document, "a").Should().Be(At(document, "b"));
        document.Positions[ListModule.OfSeq(new[] { "b", "value" })].Should().Be(new SourcePosition(2, 4));
    }

    [Test]
    public void DefaultProfileRetainsPinnedScalarAndTagSemantics()
    {
        var document = Read("a: null\nb: ~\nc: yes\nd: 2026-09-28\ne: '42'\nf: 0x2A\ng: 0o52\nh: -0x2A\ni: +0o52\nj: !custom 42\nk: !!str true\nl: !!int -0o52\nm: .inf\nn: -.inf\no: .NaN\np: Null\nq: NaN\nr: Infinity\ns: !!int '42'\nt: !!bool TRUE\nu: !!float 1.5\nv: !!null null\n");
        At(document, "a").Should().Be(ConfigValue.Null);
        At(document, "b").Should().Be(ConfigValue.Null);
        foreach (var pair in new[] { ("c", "yes"), ("d", "2026-09-28"), ("e", "42"), ("h", "-0x2A"), ("i", "+0o52"), ("j", "42"), ("k", "true"), ("p", "Null"), ("q", "NaN"), ("r", "Infinity"), ("s", "42") })
            At(document, pair.Item1).Should().Be(ConfigValue.NewString(pair.Item2));
        At(document, "f").Should().Be(ConfigValue.NewInteger(42));
        At(document, "g").Should().Be(ConfigValue.NewInteger(42));
        At(document, "l").Should().Be(ConfigValue.NewInteger(-42));
        At(document, "t").Should().Be(ConfigValue.NewBoolean(true));
        At(document, "u").Should().Be(ConfigValue.NewNumber(1.5));
        At(document, "v").Should().Be(ConfigValue.Null);
        double.IsPositiveInfinity(((ConfigValue.Number)At(document, "m")).Item).Should().BeTrue();
        double.IsNegativeInfinity(((ConfigValue.Number)At(document, "n")).Item).Should().BeTrue();
        double.IsNaN(((ConfigValue.Number)At(document, "o")).Item).Should().BeTrue();
    }

    [Test]
    public void DefaultProfileAllowsEmptyStreamAndCustomContainerTags()
    {
        Read("").Values.Should().Be(DocumentDefaults.emptyValue);
        var document = Read("a: !mapping {x: 1}\nb: !sequence [1, 2]\n'': kept\n");
        At(document, "a", "x").Should().Be(ConfigValue.NewInteger(1));
        At(document, "").Should().Be(ConfigValue.NewString("kept"));
    }

    [Test]
    [Arguments("<<: a\n")]
    [Arguments("a: &x 1\nb:\n  <<: *x\n")]
    [Arguments("a: 1\n---\nb: 2\n")]
    [Arguments("42: value\n")]
    [Arguments("true: value\n")]
    [Arguments("[a, b]: value\n")]
    [Arguments("- 1\n")]
    [Arguments("a: &x {self: *x}\n")]
    [Arguments("a: !!bool nope\n")]
    [Arguments("a: !!null NULL\n")]
    public void DefaultProfileRejectsUnsupportedOrInvalidStructures(string text) =>
        ConfigurationReader.ReadText("invalid.yaml", text).Content.Should().BeOfType<SourceContent.Invalid>();
}
