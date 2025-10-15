namespace Morphir.IR;

public class PackageNameTests
{
    [Test]
    public async Task It_Should_Be_Implicitly_Convertable_To_A_Path()
    {
        PackageName packageName = PackageName.FromString("Morphir.IR");
        Path actual = packageName;
        using (Assert.Multiple())
        {
            await Assert.That(actual.Names).IsEquivalentTo(packageName.Names);
        }
    }
}
