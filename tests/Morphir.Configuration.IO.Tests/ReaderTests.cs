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
}
