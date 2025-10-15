using System.Collections.Immutable;

namespace Morphir.IR;

public class PathTests
{
    [Test]
    public async Task It_Should_Support_Deconstruction_When_Empty()
    {
        var (head, tail) = Path.Empty;
        using (Assert.Multiple())
        {
            await Assert.That(head).IsEqualTo(null);
            await Assert.That(tail).IsEqualTo(ImmutableList<Name>.Empty);
        }
    }
    
    [Test]
    public async Task It_Should_Support_Deconstruction_When_Constructed_From_A_Single_Name()
    {
        var (head, tail) = Path.FromList(Name.FromList("Single"));
        using (Assert.Multiple())
        {
            await Assert.That(head).IsEqualTo(Name.FromList("Single"));
            await Assert.That(tail).IsEqualTo(ImmutableList<Name>.Empty);
        }
    }
    
    [Test]
    public async Task It_Should_Support_Deconstruction_When_Constructed_From_A_Path_With_Two_Names()
    {
        var (head, tail) = Path.FromList(Name.FromList("Morphir"), Name.FromList("IR"));
        using (Assert.Multiple())
        {
            await Assert.That(head).IsEqualTo(Name.FromList("Morphir"));
            await Assert.That(tail).IsEquivalentTo(ImmutableList<Name>.Empty.Add(Name.FromList("IR")));
        }
    }
    
    [Test]
    [Arguments("fooBar.Baz", new[]{"fooBar", "Baz"})]
    [Arguments("foo bar/baz", new[]{"fooBar", "Baz"})]
    public async Task FromString_Tests(string input, string[] expectedNameStrings)
    {
        var expectedNames = expectedNameStrings.Select(Name.FromString).ToImmutableList();
        var expected = new Path(expectedNames);
        var actual = Path.FromString(input);
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments("fooBar/baz","FooBar.Baz", "foo_bar/baz", "foo-bar/baz")]
    [Arguments("Math/DivideByZero","Math.DivideByZero", "math/divide_by_zero", "math/divide-by-zero")]
    public async Task ToString_Tests(string input, string expectedTitleCase, string expectedSnakeCase, string expectedKebabCase)
    {
        var path = Path.FromString(input);
        var actualTitleCase = path.ToString(Name.ToTitleCase, ".");
        var actualSnakeCase = path.ToString(Name.ToSnakeCase, "/");
        var actualKebabCase = path.ToString(Name.ToKebabCase, "/");
        using (Assert.Multiple())
        {
            await Assert.That(actualTitleCase).IsEqualTo(expectedTitleCase);
            await Assert.That(actualSnakeCase).IsEqualTo(expectedSnakeCase);
            await Assert.That(actualKebabCase).IsEqualTo(expectedKebabCase);
        }
    }

    [Test]
    [Arguments("String/ToUpper", "string/to-upper")]
    [Arguments("RuleEngine/BusinessRule", "rule-engine/business-rule")]
    public async Task Parameterless_ToString_Should_Produce_Canonical_String(string input, string expected)
    {
        var path = Path.FromString(input);
        
        using (Assert.Multiple())
        {
            await Assert.That(path.ToString()).IsEqualTo(expected);
            await Assert.That(path.ToString()).IsEqualTo(path.ToCanonicalString());
        }
    }

    [Test]
    [Arguments("Foo.Bar", "Foo", true)]
    [Arguments("Foo", "Foo.Bar", false)]
    [Arguments("Foo.Bar", "Foo.Bar", true)]
    public async Task IsPrefixOf(string pathInput, string prefixInput, bool expected)
    {
        var path = Path.FromString(pathInput);
        var prefix = Path.FromString(prefixInput);
        using (Assert.Multiple())
        {
            await Assert.That(Path.IsPrefixOf(path, prefix)).IsEqualTo(expected);
            await Assert.That(prefix.IsPrefixOf(path)).IsEqualTo(expected);
        }
    }
    
}
