using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.FSharp.Collections;

namespace Morphir.Configuration.IO.Tests;

[Category("PlatformSpecific")]
public sealed class FixtureReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Configuration", name);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static ConfigValue At(ConfigValue tree, params string[] path) => ConfigTree.tryFind(ListModule.OfSeq(path), tree).Value;

    [Test]
    [Arguments("json-corpus.json", "caff9243c0afa55d19aa7f2f8f22b1e4392f89ce431f7574a01a8661237daece")]
    [Arguments("toml-corpus.json", "d85b36ab549bc49b965b8aba972a799216400ba5111d8197d55a10eacc9228d6")]
    [Arguments("morphir-config-v1.json", "b5b2f636b8fbbdcc4dc3636ffe7bcee81776d0dbd73810d9c9391f1ee2813901")]
    public void PinnedSnapshotsHaveTheRecordedHash(string file, string hash) =>
        Hash(File.ReadAllBytes(Fixture(file))).Should().Be(hash);

    [Test]
    [Arguments("json-corpus.json", 23)]
    [Arguments("toml-corpus.json", 3)]
    public void EveryPinnedCorpusEntryRetainsValuesInFileAndMemoryHosts(string file, int expectedCount)
    {
        using var corpus = JsonDocument.Parse(File.ReadAllBytes(Fixture(file)));
        corpus.RootElement.GetArrayLength().Should().Be(expectedCount);
        var directory = Path.Combine(Path.GetTempPath(), "morphir-config-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var entry in corpus.RootElement.EnumerateArray())
            {
                var text = entry.GetProperty("text").GetString()!;
                Hash(Encoding.UTF8.GetBytes(text)).Should().Be(entry.GetProperty("sha256").GetString());
                var upstreamPath = entry.GetProperty("path").GetString()!;
                var target = Path.Combine(directory, Guid.NewGuid().ToString("N") + Path.GetExtension(upstreamPath));
                var supplied = ConfigurationReader.ReadText(target, text);
                supplied.Content.Should().BeOfType<SourceContent.Available>();
                var document = ((SourceContent.Available)supplied.Content).Item;
                document.Diagnostics.ToArray().Should().NotContain(item => item.Severity == DiagnosticSeverity.Error);

                File.WriteAllText(target, text);
                var fileDocument = ((SourceContent.Available)ConfigurationReader.ReadFile(supplied.Source).Content).Item;
                fileDocument.Values.Should().Be(document.Values);
                fileDocument.Source.Should().Be(document.Source);
                fileDocument.Positions.Equals(document.Positions).Should().BeTrue();
                Normalization.normalize(document.Source, document.Raw, document.Positions).Values.Should().Be(document.Values);

                if (Path.GetExtension(upstreamPath) == ".json")
                {
                    using var raw = JsonDocument.Parse(text);
                    At(document.Values, "project", "name").Should().Be(ConfigValue.NewString(raw.RootElement.GetProperty("name").GetString()!));
                    At(document.Values, "project", "source_directory").Should().Be(ConfigValue.NewString(raw.RootElement.GetProperty("sourceDirectory").GetString()!));
                    var exposed = raw.RootElement.GetProperty("exposedModules").EnumerateArray().Select(value => ConfigValue.NewString(value.GetString()!));
                    At(document.Values, "project", "exposed_modules").Should().Be(ConfigValue.NewArray(ListModule.OfSeq(exposed)));
                }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
