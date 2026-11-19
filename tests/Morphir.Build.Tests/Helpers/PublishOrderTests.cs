using FluentAssertions;
using Morphir.Build.Helpers;
using TUnit.Core;

namespace Morphir.Build.Tests.Helpers;

/// <summary>The publish order of library packages: each package follows the Morphir packages it references.</summary>
public class PublishOrderTests
{
    private static readonly Dictionary<string, string[]> References = new()
    {
        ["src/Svg/Svg.fsproj"] = [],
        ["src/Knowledge/Knowledge.fsproj"] = [],
        ["src/Diagrams/Diagrams.fsproj"] = ["../Svg/Svg.fsproj", "../Knowledge/Knowledge.fsproj"],
    };

    private static IEnumerable<string> Read(string project) => References[project];

    [Test]
    public void APackageAfterItsDependenciesIsInOrder() =>
        PublishOrder.Violations(
            [("Svg", "src/Svg/Svg.fsproj"), ("Knowledge", "src/Knowledge/Knowledge.fsproj"), ("Diagrams", "src/Diagrams/Diagrams.fsproj")],
            Read).Should().BeEmpty();

    [Test]
    public void APackageBeforeADependencyIsReportedWithBothNames() =>
        PublishOrder.Violations(
            [("Svg", "src/Svg/Svg.fsproj"), ("Diagrams", "src/Diagrams/Diagrams.fsproj"), ("Knowledge", "src/Knowledge/Knowledge.fsproj")],
            Read).Should().Equal("Diagrams is published before Knowledge, which it references.");

    [Test]
    public void APackageThatReferencesAnUnpublishedProjectIsNotAViolation() =>
        PublishOrder.Violations([("Diagrams", "src/Diagrams/Diagrams.fsproj")], Read).Should().BeEmpty();

    [Test]
    public void AReferenceWithAllAssetsPrivateIsNotAPackageDependency()
    {
        var project = Path.Combine(Path.GetTempPath(), $"morphir-publish-order-{Guid.NewGuid():N}.fsproj");
        try
        {
            File.WriteAllText(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Models\Models.fsproj" />
                    <ProjectReference Include="..\Sdk\Sdk.csproj" PrivateAssets="all" />
                  </ItemGroup>
                </Project>
                """);

            PublishOrder.ProjectReferences(project).Should().Equal("../Models/Models.fsproj");
        }
        finally
        {
            File.Delete(project);
        }
    }
}
