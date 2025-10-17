using System.Text.Json;
using Json.More;
using Morphir.Classic.IR;
using Morphir.IR;

namespace Morphir.StepDefinitions;

[Binding]
public class MorphirJsonSerializationStepDefinitions(ClassicMorphirIrSerializationTestDriver driver)
{
    public ClassicMorphirIrSerializationTestDriver Driver { get; } = driver;

    [Given("we are using FormatVersion {string}")]
    public void GivenWeAreUsingFormatVersion(string formatVersion)
    {
        Driver.WithFormatVersion(formatVersion);
    }

    [Given(@"a classic Morphir IR Type encoded as:")]
    public void GivenAClassicMorphirIrTypeEncodedAs(string json)
    {
        Driver.InputJson = json;
    }

    [When("we deserialize it")]
    public void WhenWeDeserializeIt()
    {
        Driver.DeserializedType = MorphirJson.DecodeFromString<Type<IR.PlaceHolder>>(Driver.InputJson, Driver.Options);
    }

    [When("we serialize it back")]
    public void WhenWeSerializeItBack()
    {
        Driver.ExpectedJson = MorphirJson.EncodeAsString(Driver.DeserializedType!, Driver.Options);
    }

    [Then("the result should match the original input")]
    public async Task ThenTheResultShouldMatchTheOriginalInput()
    {
        await Assert.That(Driver.ExpectedJson).IsEqualTo(Driver.InputJson);
    }

    [Then("the result should be equivalent to the original input")]
    public async Task ThenTheResultShouldBeEquivalentToTheOriginalInput()
    {
        // Parse both JSON strings into JsonElement for structural comparison
        using var expectedDoc = JsonDocument.Parse(Driver.ExpectedJson);
        using var inputDoc = JsonDocument.Parse(Driver.InputJson);

        // Use Json.More.Net's IsEquivalentTo extension method
        var areEquivalent = expectedDoc.RootElement.IsEquivalentTo(inputDoc.RootElement);

        if (!areEquivalent)
        {
            throw new InvalidOperationException(
                $"JSON structures are not equivalent.\nExpected: {Driver.InputJson}\nActual: {Driver.ExpectedJson}");
        }

        await Assert.That(areEquivalent).IsTrue();
    }

    [Given("a classic Morphir IR Type encoded as JSON text: {string}")]
    public void GivenAClassicMorphirIrTypeEncodedAsJsonText(string json)
    {
        Driver.InputJson = json;
    }
}
