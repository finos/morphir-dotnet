namespace Morphir.IR;

public class PlaceHolderTests
{
    [Test]
    public async Task It_Should_Support_Json_Serialization()
    {
        var sut = PlaceHolder.Default;
        var actual = MorphirJson.EncodeAsString(sut);
        await Assert.That(actual).IsEqualTo("{}");       
    }

    [Test]
    public async Task It_Should_Support_Json_Deserialization()
    {
        var sut = PlaceHolder.Default;
        var actual = MorphirJson.DecodeFromString<PlaceHolder>("{}");
        await Assert.That(actual).IsEqualTo(sut);       
    }
}
