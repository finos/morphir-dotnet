namespace Morphir.Internals;

public class StringExtensionsTests
{
    [Test]
    [Arguments("foo", "Foo")]
    public async Task Capitalize_Tests(String input, String expected)
    {
        await Assert.That(input.Capitalize()).IsEqualTo(expected);
    }
    [Test]
    [Arguments("fooBar_baz 123", new[] { "foo", "Bar", "baz", "123" })]
    public async Task ToMorphirWords_Should_Split_A_String_Appropriately(String input, string[] expected)
    {
        await Assert.That(input.ToMorphirWords()).IsEquivalentTo(expected);
    }
}
