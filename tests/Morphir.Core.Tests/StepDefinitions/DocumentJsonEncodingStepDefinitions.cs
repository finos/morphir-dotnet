using Morphir.IR;
using Shouldly;

namespace Morphir.StepDefinitions;

/// <summary>
/// Step definitions for Document JSON encoding/decoding scenarios.
/// </summary>
[Binding]
public class DocumentJsonEncodingStepDefinitions
{
    private readonly DocumentJsonEncodingTestDriver _driver;

    public DocumentJsonEncodingStepDefinitions(DocumentJsonEncodingTestDriver driver)
    {
        _driver = driver;
    }

    [Given("a Document of type {word}")]
    public void GivenADocumentOfType(string type)
    {
        _driver.Document = type switch
        {
            "Null" => Document.NullDoc,
            "True" => Document.True,
            "False" => Document.False,
            _ => throw new ArgumentException($"Unknown document type: {type}")
        };
    }

    [Given(@"a Document encoded as:")]
    public void GivenADocumentEncodedAs(string json)
    {
        _driver.InputJson = json;
    }

    [Given("a Document encoded as JSON text: {string}")]
    public void GivenADocumentEncodedAsJsonText(string json)
    {
        _driver.InputJson = json;
    }

    [When("we serialize it to JSON")]
    public void WhenWeSerializeItToJson()
    {
        _driver.ExpectedJson = MorphirJson.EncodeAsString(_driver.Document!);
    }

    [When("we deserialize it")]
    [Scope(Feature = "Document JSON Encoding")]
    public void WhenWeDeserializeIt()
    {
        _driver.Document = MorphirJson.DecodeFromString<Document>(_driver.InputJson);
    }

    [When("we serialize it back")]
    [Scope(Feature = "Document JSON Encoding")]
    public void WhenWeSerializeItBack()
    {
        _driver.ExpectedJson = MorphirJson.EncodeAsString(_driver.Document!);
    }

    [Then(@"the JSON should be:")]
    public async Task ThenTheJsonShouldBe(string expectedJson)
    {
        await Assert.That(_driver.ExpectedJson).IsEqualTo(expectedJson);
    }

    [Then("the result should match the original input")]
    [Scope(Feature = "Document JSON Encoding")]
    public async Task ThenTheResultShouldMatchTheOriginalInput()
    {
        await Assert.That(_driver.ExpectedJson).IsEqualTo(_driver.InputJson);
    }

    [Then("the result should be equivalent to the original input")]
    [Scope(Feature = "Document JSON Encoding")]
    public void ThenTheResultShouldBeEquivalentToTheOriginalInput()
    {
        // Semantic JSON comparison: parse both strings as JsonNode for deep equality
        var actual = System.Text.Json.Nodes.JsonNode.Parse(_driver.ExpectedJson);
        var expected = System.Text.Json.Nodes.JsonNode.Parse(_driver.InputJson);

        // Use DeepEquals for value-based comparison (ignores key order, whitespace)
        var areEqual = System.Text.Json.Nodes.JsonNode.DeepEquals(actual, expected);

        areEqual.ShouldBeTrue(
            $"JSON documents should be semantically equivalent.{Environment.NewLine}" +
            $"Expected: {_driver.InputJson}{Environment.NewLine}" +
            $"Actual: {_driver.ExpectedJson}");
    }
}
