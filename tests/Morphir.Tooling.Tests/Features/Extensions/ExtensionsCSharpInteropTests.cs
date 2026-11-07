using FluentAssertions;
using Morphir.Extensions;

namespace Morphir.Tooling.Tests.Features.Extensions;

/// <summary>
/// Calls the parsed value types the way C# code in the CLI calls them. The project compiles with nullable
/// warnings as errors, so each read below is only possible on the branch where flow analysis knows the value is set.
/// </summary>
public sealed class ExtensionsCSharpInteropTests
{
    [Test]
    [Arguments("morphir.csharp", true)]
    [Arguments("acme.tools.lint-2", true)]
    [Arguments("C#", false)]
    [Arguments("a..b", false)]
    [Arguments("", false)]
    [Arguments(null, false)]
    public void ExtensionIdTryParseSetsExactlyOneOutValue(string? text, bool valid)
    {
        if (ExtensionId.TryParse(text, out var id, out var failure))
        {
            valid.Should().BeTrue();
            id.Value.Should().Be(text);
        }
        else
        {
            valid.Should().BeFalse();
            failure.Field.Should().Be("extension id");
            failure.ToString().Should().NotBeNullOrEmpty();
        }
    }

    [Test]
    public void EveryParsedTypeRefusesNullText()
    {
        TargetId.TryParse(null, out _, out var target).Should().BeFalse();
        target!.Field.Should().Be("target id");
        ExtensionName.TryParse(null, out _, out var name).Should().BeFalse();
        name!.Field.Should().Be("extension name");
        ExtensionDescription.TryParse(null, out _, out var description).Should().BeFalse();
        description!.Field.Should().Be("extension description");
        ReleaseVersion.TryParse(null, out _, out var version).Should().BeFalse();
        version!.Field.Should().Be("release version");
    }

    [Test]
    public void CreateBuildsLiteralsAndRefusesInvalidOnes()
    {
        ExtensionId.Create("morphir.fsharp").ToString().Should().Be("morphir.fsharp");
        TargetId.Create("fsharp").ToString().Should().Be("fsharp");
        ExtensionName.Create("F# generator").ToString().Should().Be("F# generator");
        ExtensionDescription.Create("Generates F# from a Morphir model.").ToString().Should().NotBeEmpty();
        ReleaseVersion.Create("0.4.0-rc.1").ToString().Should().Be("0.4.0-rc.1");

        var refused = () => ExtensionId.Create("Not.Valid");
        refused.Should().Throw<ArgumentException>().WithMessage("*Not.Valid*");
    }

    [Test]
    public void NonEmptySetIsBuiltWithoutFSharpFunctions()
    {
        var targets = NonEmptySet<TargetId>.Create(TargetId.Create("fsharp"), TargetId.Create("csharp"));
        targets.Select(target => target.Value).Should().Equal("csharp", "fsharp");
        NonEmptySet<TargetId>.TryCreate([], out _).Should().BeFalse();
        targets.Map(target => target.Value.Length).Count.Should().Be(1);
    }
}
