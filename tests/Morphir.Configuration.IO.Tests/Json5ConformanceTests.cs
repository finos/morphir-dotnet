using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Morphir.Configuration.IO.Tests;

[Category("PlatformSpecific")]
public sealed class Json5ConformanceTests
{
    [Test]
    [Arguments(".json")]
    [Arguments(".json5")]
    public void PinnedOfficialGrammarCorpusMatchesTheReaderContract(string extension)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Configuration", "json5-tests");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
        manifest.RootElement.GetProperty("revision").GetString().Should().Be("ceb24d4080137d70833f86c25659c1331b80a387");
        var count = 0;
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relative = entry.GetProperty("path").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(root, relative));
            Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(entry.GetProperty("sha256").GetString(), relative);
            var expected = entry.GetProperty("expected").GetString();
            if (expected == "metadata") continue;
            var text = "{value:\n" + Encoding.UTF8.GetString(bytes) + "\n}";
            var supplied = ConfigurationReader.ReadText(relative + extension, text);
            var accepted = supplied.Content is SourceContent.Available;
            accepted.Should().Be(expected == "accepted", $"official case {relative} under {extension}; result {supplied.Content}");
            count++;
        }
        count.Should().Be(114);
    }
}
