using Morphir.DotNet.IR;

namespace Morphir.Dotnet.IR;

public class NameTests
{
    [Fact]
    public void Name_Should_Be_Creatable_Using_From()
    {
        var actual = Name.From("Hello");
        actual.Should().Be(Name.From("Hello"));
    }
}
