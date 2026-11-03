using FluentAssertions;
using Morphir.Configuration;
using Morphir.Configuration.IO;

namespace Morphir.Configuration.IO.Tests;

public sealed class ReaderTests
{
    [Test]
    public void Json5RetainsStringAndIntegerAsDifferentTypedValues()
    {
        var input = ConfigurationReader.ReadText("morphir.json5", "{text:'4', count:4,}");
        var document = ((SourceContent.Available)input.Content).Item;
        var fields = ((ConfigValue.Object)document.Values).Item;
        fields["text"].Should().Be(ConfigValue.NewString("4"));
        fields["count"].Should().Be(ConfigValue.NewInteger(4));
    }

    [Test]
    public void Json5AcceptsTheCompleteExtendedLexicalGrammar()
    {
        const string text = "\uFEFF{/*comment*/ unquoted:'x', $key:0x2A, \\u0061:+.5, trailing:5., exp:-2e+2, inf:+Infinity, nan:-NaN, escaped:'\\x41\\u0042\\v\\0\\q', continued:'a\\\r\nb', unicode:'\u2028', list:[true,null,],}//end";
        var document = ((SourceContent.Available)ConfigurationReader.ReadText("morphir.json", text).Content).Item;
        var fields = ((ConfigValue.Object)document.Values).Item;
        fields["$key"].Should().Be(ConfigValue.NewInteger(42));
        fields["a"].Should().Be(ConfigValue.NewNumber(.5));
        fields["trailing"].Should().Be(ConfigValue.NewNumber(5));
        fields["exp"].Should().Be(ConfigValue.NewNumber(-200));
        ((ConfigValue.Number)fields["inf"]).Item.Should().Be(double.PositiveInfinity);
        double.IsNaN(((ConfigValue.Number)fields["nan"]).Item).Should().BeTrue();
        fields["escaped"].Should().Be(ConfigValue.NewString("AB\v\0q"));
        fields["continued"].Should().Be(ConfigValue.NewString("ab"));
        document.Positions.Should().NotBeEmpty();
    }

    [Test]
    public void ClassicManifestRetainsRawFieldsAndDerivedDefaults()
    {
        var document = ((SourceContent.Available)ConfigurationReader.ReadText("morphir.json",
            "{name:'Acme.Orders',sourceDirectory:'elm-src',exposedModules:['Orders.Api'],dependencies:['legacy'],localDependencies:[],decorations:{test:'ignored'}}").Content).Item;
        var project = ((ConfigValue.Object)((ConfigValue.Object)document.Values).Item["project"]).Item;
        project["name"].Should().Be(ConfigValue.NewString("Acme.Orders"));
        project["source_directory"].Should().Be(ConfigValue.NewString("elm-src"));
        ((ConfigValue.Object)document.Raw).Item.Should().ContainKey("sourceDirectory");
        document.LegacyFields.ToArray().Should().Contain(item => item.CanonicalPath != null
            && item.CanonicalPath.Value.SequenceEqual(new[] { "pipeline", "backend" })
            && item.Value.Equals(ConfigValue.NewString("checkpoint")));
        document.Diagnostics.Count(item => item.Code == "config.legacy_unsupported").Should().Be(2);
    }
}
