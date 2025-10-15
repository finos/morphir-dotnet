using Morphir.IR.Codecs;

namespace Morphir.IR;

public class MorphirJsonTests
{
    [Test]
    [Arguments("Foo", "2", """["foo"]""")]
    [Arguments("Foo", "3.0-Experimental", "\"foo\"")]
    public async Task It_Should_Encode_Name_Appropriately(string nameStr, string version, string expected)
    {
        var name = Name.FromString(nameStr);
        var formatVersion = MorphirFormatVersion.Parse(version, MorphirFormatVersion.V2);
        var options = new MorphirJsonOptions(formatVersion);
        var actual = MorphirJson.EncodeAsString(name, options);
        await Assert.That(actual).IsEqualTo(expected);
    }
}
