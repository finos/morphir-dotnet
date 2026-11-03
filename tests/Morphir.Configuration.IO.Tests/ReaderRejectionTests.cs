using FluentAssertions;

namespace Morphir.Configuration.IO.Tests;

public sealed class ReaderRejectionTests
{
    [Test]
    [Arguments("duplicate.json", "{project:{name:'a', name:'b'}}")]
    [Arguments("escaped.json5", "{name:1,\\u006eame:2}")]
    [Arguments("duplicate.yaml", "project:\n  name: a\n  name: b\n")]
    [Arguments("duplicate.toml", "[project]\nname='a'\nname='b'\n")]
    public void DuplicateKeysAreRejectedWithTheirSource(string path, string text)
    {
        var input = ConfigurationReader.ReadText(path, text);
        input.Content.Should().BeOfType<SourceContent.Invalid>();
        var diagnostic = ((SourceContent.Invalid)input.Content).Item[0];
        diagnostic.Source.Path.Should().Be(path);
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.Position.Should().NotBeNull();
    }

    [Test]
    [Arguments("malformed.json5", "{project:")]
    [Arguments("malformed.yaml", "project: [unterminated\n")]
    [Arguments("malformed.toml", "[project\n")]
    public void MalformedInputsReturnStructuredSourcePositions(string path, string text)
    {
        var input = ConfigurationReader.ReadText(path, text);
        input.Content.Should().BeOfType<SourceContent.Invalid>();
        var diagnostic = ((SourceContent.Invalid)input.Content).Item[0];
        diagnostic.Source.Path.Should().Be(path);
        diagnostic.Position.Should().NotBeNull();
        diagnostic.Position.Value.Line.Should().BeGreaterThan(0);
        diagnostic.Position.Value.Column.Should().BeGreaterThan(0);
    }

    [Test]
    [Arguments("{a:01}")]
    [Arguments("{a:0x}")]
    [Arguments("{a:1e}")]
    [Arguments("{a:'\\8'}")]
    [Arguments("{a:'\\01'}")]
    [Arguments("{a:1,,}")]
    [Arguments("{a:'\\u12'}")]
    [Arguments("{/*unterminated")]
    public void Json5RejectsInvalidGrammar(string text) =>
        ConfigurationReader.ReadText("invalid.json5", text).Content.Should().BeOfType<SourceContent.Invalid>();

    [Test]
    public void UnsupportedExplicitExtensionNamesItsInput()
    {
        var input = ConfigurationReader.ReadText("settings.ini", "[project]\nname=Acme");
        var diagnostic = ((SourceContent.Invalid)input.Content).Item[0];
        diagnostic.Code.Should().Be("config.unsupported_format");
        diagnostic.Source.Path.Should().Be("settings.ini");
        diagnostic.Message.Should().Contain("settings.ini");
    }
}
