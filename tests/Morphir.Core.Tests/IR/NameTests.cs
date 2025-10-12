namespace Morphir.IR;

public class NameTests
{
    [Test]
    public async Task Name_Should_Be_Creatable_Using_From()
    {
        var actual = Name.From("Hello");
        await Assert.That(actual).IsEqualTo(Name.From("Hello"));
    }
}
