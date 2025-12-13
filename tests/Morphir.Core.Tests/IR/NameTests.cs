using System.Text.Json;
using Morphir.IR;

namespace Morphir.IR;

public class NameTests
{
    [Test]
    [Arguments("fooBar_baz 123", new[] { "foo", "bar", "baz", "123" })]
    [Arguments("valueInUSD", new[] { "value", "in", "u", "s", "d" })]
    [Arguments("ValueInUSD", new[] { "value", "in", "u", "s", "d" })]
    [Arguments("value_in_USD", new[] { "value", "in", "u", "s", "d" })]
    [Arguments("_-%", new string[] { })]
    public async Task FromStringTests(string input, string[] segments)
    {
        Name actual = Name.FromString(input);
        using (Assert.Multiple())
        {
            await Assert.That(actual).IsEqualTo(Name.FromList(segments));
            await Assert.That(actual.Segments).IsEquivalentTo(segments);
        }
    }

    [Test]
    [Arguments(new[] { "foo", "bar", "baz", "123" }, "fooBarBaz123")]
    [Arguments(new[] { "value", "in", "u", "s", "d" }, "valueInUSD")]
    public async Task ToCamelCase_Tests(string[] segments, string expected)
    {
        var actual = Name.FromList(segments);
        using (Assert.Multiple())
        {
            await Assert.That(actual.ToCamelCase()).IsEqualTo(expected);
            await Assert.That(actual.ToCamelCase()).IsEqualTo(Name.ToCamelCase(actual));
        }
    }

    [Test]
    [Arguments(new[] { "value", "in", "u", "s", "d" }, new[] { "value", "in", "USD" })]
    public async Task ToHumanWords_Tests(string[] segments, string[] expected)
    {
        var actual = Name.FromList(segments);
        await Assert.That(actual.ToHumanWords()).IsEquivalentTo(expected);
    }

    [Test]
    [Arguments(new[] { "value", "in", "u", "s", "d" }, new[] { "Value", "in", "USD" })]
    public async Task ToHumanWordsTitle_Tests(string[] segments, string[] expected)
    {
        var actual = Name.FromList(segments);
        await Assert.That(actual.ToHumanWordsTitle()).IsEquivalentTo(expected);
    }

    [Test]
    [Arguments(new[] { "foo", "bar", "baz", "123" }, "foo_bar_baz_123")]
    [Arguments(new[] { "value", "in", "u", "s", "d" }, "value_in_USD")]
    public async Task ToSnakeCase_Tests(string[] segments, string expected)
    {
        var actual = Name.FromList(segments);
        await Assert.That(actual.ToSnakeCase()).IsEqualTo(expected);
    }

    [Test]
    [Arguments(new[] { "foo", "bar", "baz", "123" }, "FooBarBaz123")]
    [Arguments(new[] { "value", "in", "u", "s", "d" }, "ValueInUSD")]
    public async Task ToTitleCase_Tests(string[] segments, string expected)
    {
        var actual = Name.FromList(segments);
        await Assert.That(actual.ToTitleCase()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_Be_Possible_To_Create()
    {
        Name actual = new(["classic", "name"]);
        await Assert.That(actual).IsEqualTo(Name.FromList("classic", "name"));

    }

    [Test]
    [Arguments<string[]>(["value", "in", "u", "s", "d"])]
    public async Task Should_Serialize_As_Expected_Json(string[] input)
    {
        var name = Name.FromList(input);
        var json = JsonSerializer.Serialize(name);
        Console.WriteLine("JSON: {0}", json);
        await VerifyJson(json).UseStrictJson();
    }

    [Test]
    [Arguments(new[] { "full", "name" }, "FullName")]
    [Arguments(new[] { "value", "in", "u", "s", "d" }, "ValueInUSD")]
    public async Task Should_Convert_ToTitleCase(string[] input, string expected)
    {
        var name = Name.FromList(input);
        await Assert.That(name.ToTitleCase()).IsEqualTo(expected);
    }
}
