using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Morphir.IR;

public class QNameTests
{
    [Test]
    public async Task Should_Create_QName()
    {
        var module = Path.FromString("My.Module");
        var local = Name.FromString("valueInUSD");
        var actual = new QName(module, local);
        using (Assert.Multiple())
        {
            await Assert.That(actual.ModulePath).IsEqualTo(module);
            await Assert.That(actual.LocalName).IsEqualTo(local);
        }
    }

    [Test]
    public async Task Equality_Tests()
    {
        var m1 = Path.FromString("A.B");
        var m2 = Path.FromString("A.B");
        var n1 = Name.FromString("x");
        var n2 = Name.FromString("y");
        var q1 = new QName(m1, n1);
        var q2 = new QName(m2, n1);
        var q3 = new QName(m1, n2);
        using (Assert.Multiple())
        {
            await Assert.That(q1).IsEqualTo(q2);
            await Assert.That(q1).IsNotEqualTo(q3);
        }
    }

    [Test]
    public async Task FromName_ToTuple_GetModulePath_Tests()
    {
        var module = Path.FromString("M");
        var name = Name.FromString("n");
        var q = QName.FromName(module)(name);
        using (Assert.Multiple())
        {
            await Assert.That(q).IsEqualTo(new QName(module, name));
            await Assert.That(QName.GetModulePath(q)).IsEqualTo(module);
            var tuple = QName.ToTuple(q);
            await Assert.That(tuple.ModulePath).IsEqualTo(module);
            await Assert.That(tuple.LocalName).IsEqualTo(name);
        }
    }

    [Test]
    public async Task ToString_FormatsModuleWithTitleCaseAndLocalAsCamelCase()
    {
        var module = Path.FromString("fooBar.baz");
        var local = Name.FromList(new[] { "a", "name" });
        var q = new QName(module, local);
        await Assert.That(q.ToString()).IsEqualTo("FooBar.Baz:aName");
    }
}
