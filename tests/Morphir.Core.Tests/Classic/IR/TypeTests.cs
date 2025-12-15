using Morphir.IR;

namespace Morphir.Classic.IR;

public class TypeTests
{
    [Test]
    public async Task It_Can_Create_A_Variable_Type()
    {
        var actual = Type.Variable("some-attributes", Name.FromString("TResult"));
        using (Assert.Multiple())
        {
            await Assert.That(actual.Name).IsEqualTo(Name.FromList("t", "result"));
        }
    }
}
