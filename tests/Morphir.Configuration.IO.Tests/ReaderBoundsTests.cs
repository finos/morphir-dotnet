using System.Text;
using FluentAssertions;

namespace Morphir.Configuration.IO.Tests;

public sealed class ReaderBoundsTests
{
    [Test]
    public void Json5NestingLimitReturnsADiagnostic()
    {
        var text = string.Concat(Enumerable.Repeat("{child:", 130)) + "0" + new string('}', 130);
        var input = ConfigurationReader.ReadText("deep.json5", text);
        input.Content.Should().BeOfType<SourceContent.Invalid>();
        ((SourceContent.Invalid)input.Content).Item[0].Code.Should().Be("config.depth_limit");
    }

    [Test]
    public void YamlAliasExpansionIsBoundedWithoutExecutingAnything()
    {
        var text = new StringBuilder("a0: &a0 [0]\n");
        for (var level = 1; level <= 18; level++) text.Append($"a{level}: &a{level} [*a{level - 1}, *a{level - 1}]\n");
        var input = ConfigurationReader.ReadText("expansion.yaml", text.ToString());
        input.Content.Should().BeOfType<SourceContent.Invalid>();
        ((SourceContent.Invalid)input.Content).Item[0].Code.Should().Be("config.yaml_expansion_limit");
    }
}
