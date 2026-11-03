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

    [Test]
    public void LegacyCanonicalConflictsFailEvenWhenEqualAndIdentifyBothPaths()
    {
        var input = ConfigurationReader.ReadText("conflict.json5",
            "{sourceDirectory:'src',exposedModules:[],project:{source_directory:'src'}}");
        input.Content.Should().BeOfType<SourceContent.Invalid>();
        var diagnostics = ((SourceContent.Invalid)input.Content).Item;
        diagnostics.ToArray().Should().Contain(item => item.Code == "config.legacy_conflict"
            && item.Message.Contains("sourceDirectory") && item.Message.Contains("project.source_directory")
            && item.Source.Path == "conflict.json5" && item.Position != null);
    }

    [Test]
    public void YamlValuePositionsAreExactlyOneBased()
    {
        var input = ConfigurationReader.ReadText("positions.yaml", "project:\n  name: Acme\n");
        var document = ((SourceContent.Available)input.Content).Item;
        document.Positions[Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "project", "name" })]
            .Should().Be(new SourcePosition(2, 9));
    }

    [Test]
    [Arguments("large.json5", "{large:9223372036854775808}")]
    [Arguments("large.yaml", "large: 9223372036854775808\n")]
    public void IntegralValuesBeyondInt64RemainExact(string path, string text)
    {
        var document = ((SourceContent.Available)ConfigurationReader.ReadText(path, text).Content).Item;
        ((ConfigValue.Object)document.Values).Item["large"].Should().Be(
            ConfigValue.NewBigInteger(System.Numerics.BigInteger.Parse("9223372036854775808", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public void TomlDateValuesRetainTemporalKind()
    {
        var document = ((SourceContent.Available)ConfigurationReader.ReadText("date.toml", "custom_date = 2026-11-02\n").Content).Item;
        ((ConfigValue.Object)document.Values).Item["custom_date"].Should().Be(ConfigValue.NewTemporal("LocalDate", "2026-11-02"));
    }

    [Test]
    public void NormalizationMakesOnlyExactSecretReferencesAtomicWithoutExecutingThem()
    {
        var document = ((SourceContent.Available)ConfigurationReader.ReadText("secrets.json5",
            "{custom:{env:{env:'TOKEN'},file:{file:'missing-file'},command:{command:['never-execute-this-program','arg']},keyring:{keyring:{service:'morphir',account:'alice'}},ordinary:{command:['echo'],other:true},invalid:{env:''}}}").Content).Item;
        var fields = ((ConfigValue.Object)((ConfigValue.Object)document.Values).Item["custom"]).Item;
        foreach (var key in new[] { "env", "file", "command", "keyring" }) fields[key].Should().BeOfType<ConfigValue.SecretReference>();
        fields["ordinary"].Should().BeOfType<ConfigValue.Object>();
        fields["invalid"].Should().BeOfType<ConfigValue.Object>();
        ((ConfigValue.Object)((ConfigValue.Object)document.Raw).Item["custom"]).Item["env"].Should().BeOfType<ConfigValue.Object>();
    }

    [Test]
    [Arguments("{name:'Acme'}")]
    [Arguments("{name:'Acme',project:{exposed_modules:[]}}")]
    [Arguments("{name:4,exposedModules:[]}")]
    [Arguments("{sourceDirectory:[],exposedModules:[]}")]
    [Arguments("{exposedModules:[4]}")]
    [Arguments("{name:'Acme',exposedModules:[],project:4}")]
    [Arguments("{name:'Acme',exposedModules:[],frontend:'wrong-shape'}")]
    public void InvalidClassicShapesFailBeforeNormalizationCanHideThem(string text)
    {
        var input = ConfigurationReader.ReadText("legacy.json5", text);
        input.Content.Should().BeOfType<SourceContent.Invalid>();
        ((SourceContent.Invalid)input.Content).Item.ToArray().Should().Contain(item => item.Code == "config.legacy_value");
    }
}
