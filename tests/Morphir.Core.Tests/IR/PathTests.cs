using System.Collections.Immutable;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Morphir.IR;

public class PathTests
{
    [Test]
    public async Task Empty_Should_Return_Path_With_No_Names()
    {
        var empty = Path.Empty;
        using (Assert.Multiple())
        {
            await Assert.That(empty.Names).IsEmpty();
            await Assert.That(empty.ToList()).IsEmpty();
        }
    }

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

    [Test]
    public async Task FromList_Should_Create_Path_With_Single_Name()
    {
        var name = Name.FromString("SingleName");
        var path = Path.FromList(name);
        using (Assert.Multiple())
        {
            await Assert.That(path.Names).HasCount().EqualTo(1);
            await Assert.That(path.Names[0]).IsEqualTo(name);
        }
    }

    [Test]
    public async Task FromList_Should_Create_Path_With_Multiple_Names()
    {
        var name1 = Name.FromString("First");
        var name2 = Name.FromString("Second");
        var name3 = Name.FromString("Third");
        var path = Path.FromList(name1, name2, name3);
        using (Assert.Multiple())
        {
            await Assert.That(path.Names).HasCount().EqualTo(3);
            await Assert.That(path.Names[0]).IsEqualTo(name1);
            await Assert.That(path.Names[1]).IsEqualTo(name2);
            await Assert.That(path.Names[2]).IsEqualTo(name3);
        }
    }

    [Test]
    public async Task FromList_Should_Create_Empty_Path_With_No_Arguments()
    {
        var path = Path.FromList();
        using (Assert.Multiple())
        {
            await Assert.That(path.Names).IsEmpty();
            await Assert.That(path).IsEqualTo(Path.Empty);
        }
    }

    [Test]
    public async Task ToList_Should_Return_ImmutableList_Of_Names()
    {
        var name1 = Name.FromString("Morphir");
        var name2 = Name.FromString("IR");
        var path = Path.FromList(name1, name2);
        var list = path.ToList();
        using (Assert.Multiple())
        {
            await Assert.That(list).IsEquivalentTo(ImmutableList<Name>.Empty.Add(name1).Add(name2));
            await Assert.That(list).HasCount().EqualTo(2);
        }
    }

    [Test]
    public async Task Static_ToList_Should_Return_ImmutableList_Of_Names()
    {
        var name1 = Name.FromString("Test");
        var name2 = Name.FromString("Path");
        var path = Path.FromList(name1, name2);
        var list = Path.ToList(path);
        using (Assert.Multiple())
        {
            await Assert.That(list).IsEquivalentTo(ImmutableList<Name>.Empty.Add(name1).Add(name2));
            await Assert.That(list).HasCount().EqualTo(2);
        }
    }

    [Test]
    public async Task ToSeq_Should_Return_Seq_Of_Names()
    {
        var name1 = Name.FromString("A");
        var name2 = Name.FromString("B");
        var path = Path.FromList(name1, name2);
        var seq = path.ToSeq();
        using (Assert.Multiple())
        {
            await Assert.That(seq.Count).IsEqualTo(2);
            await Assert.That(seq.Head).IsEqualTo(name1);
            await Assert.That(seq.Tail.Head).IsEqualTo(name2);
        }
    }

    [Test]
    public async Task Head_Extension_Should_Return_First_Name()
    {
        var name1 = Name.FromString("First");
        var name2 = Name.FromString("Second");
        var path = Path.FromList(name1, name2);
        await Assert.That(path.Head).IsEqualTo(name1);
    }

    [Test]
    public async Task Head_Extension_Should_Return_Null_For_Empty_Path()
    {
        var path = Path.Empty;
        await Assert.That(path.Head).IsEqualTo(null);
    }

    [Test]
    public async Task Curried_ToString_Function_Should_Format_Path()
    {
        var path = Path.FromString("foo.bar");
        var formatter = Path.toString(Name.ToTitleCase)(".");
        var result = formatter(path);
        await Assert.That(result).IsEqualTo("Foo.Bar");
    }

    [Test]
    public async Task Curried_ToString_Function_Should_Support_Different_Separators()
    {
        var path = Path.FromString("one/two/three");
        using (Assert.Multiple())
        {
            var withSlash = Path.toString(Name.ToKebabCase)("/")(path);
            await Assert.That(withSlash).IsEqualTo("one/two/three");

            var withDot = Path.toString(Name.ToKebabCase)(".")(path);
            await Assert.That(withDot).IsEqualTo("one.two.three");

            var withHyphen = Path.toString(Name.ToKebabCase)("-")(path);
            await Assert.That(withHyphen).IsEqualTo("one-two-three");
        }
    }

    [Test]
    public async Task Equality_Tests()
    {
        var path1 = Path.FromString("A.B.C");
        var path2 = Path.FromString("A.B.C");
        var path3 = Path.FromString("A.B");
        var path4 = Path.FromList(Name.FromString("A"), Name.FromString("B"), Name.FromString("C"));
        using (Assert.Multiple())
        {
            await Assert.That(path1).IsEqualTo(path2);
            await Assert.That(path1).IsEqualTo(path4);
            await Assert.That(path1).IsNotEqualTo(path3);
            await Assert.That(Path.Empty).IsEqualTo(Path.Empty);
        }
    }

}
