using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Morphir.Configuration;
using Morphir.Configuration.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static FSharpList<T> List<T>(IEnumerable<T> items) => ListModule.OfSeq(items);
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static ConfigurationDocument Document(SourceInput input) => input.Content is SourceContent.Available available
    ? available.Item : throw new InvalidOperationException($"Reader rejected {input.Source.Path}");
static ConfigValue At(ConfigValue tree, params string[] path) => ConfigTree.tryFind(List(path), tree).Value;
static ResolutionInput Input(IEnumerable<SourceInput> sources, ProfileSelection selection) =>
    new(List(sources), MapModule.Empty<string, string>(), DocumentDefaults.emptyValue,
        new ProfileOptions(selection, 32, 4096), ProjectScope.Root, FSharpOption<OwningWorkspace>.None, List<string>([]));

try
{
    var syntaxCases = 0;
    var morphirCases = 0;
    if (args.Length != 0)
    {
        Check(args.Length == 2 && args[0] == "--fixtures", "Expected --fixtures <configuration fixture root>");
        var fixtureRoot = Path.GetFullPath(args[1]);
        var grammarRoot = Path.Combine(fixtureRoot, "json5-tests");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(grammarRoot, "manifest.json")));
        Check(manifest.RootElement.GetProperty("revision").GetString() == "ceb24d4080137d70833f86c25659c1331b80a387", "Unexpected grammar revision");
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relative = entry.GetProperty("path").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(grammarRoot, relative));
            Check(Convert.ToHexStringLower(SHA256.HashData(bytes)) == entry.GetProperty("sha256").GetString(), "Grammar hash mismatch: " + relative);
            var expected = entry.GetProperty("expected").GetString();
            if (expected == "metadata") continue;
            foreach (var extension in new[] { ".json", ".json5" })
            {
                var parsed = ConfigurationReader.ReadText(relative + extension, "{value:\n" + Encoding.UTF8.GetString(bytes) + "\n}");
                Check((parsed.Content is SourceContent.Available) == (expected == "accepted"), "Grammar mismatch: " + relative + extension);
                syntaxCases++;
            }
        }
        Check(syntaxCases == 228, "Expected 228 JSON5 syntax checks");
        foreach (var corpus in new[] { "toml-corpus.json", "json-corpus.json" })
        {
            using var entries = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtureRoot, corpus)));
            foreach (var entry in entries.RootElement.EnumerateArray())
            {
                var text = entry.GetProperty("text").GetString()!;
                Check(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))) == entry.GetProperty("sha256").GetString(), "Morphir hash mismatch");
                _ = Document(ConfigurationReader.ReadText(entry.GetProperty("path").GetString()!, text));
                morphirCases++;
            }
        }
        Check(morphirCases == 26, "Expected 26 pinned Morphir documents");
    }
    var json = Document(ConfigurationReader.ReadText("morphir.json5", "{project:{name:'Acme'},custom:{count:4,text:'4',strict:true,list:[1,2,],secret:{env:'TOKEN'}}}"));
    var yaml = Document(ConfigurationReader.ReadText("morphir.yaml", "project:\n  name: Acme\ncustom:\n  count: 4\n  text: '4'\n  strict: true\n  list: [1,2]\n  secret: {env: TOKEN}\n"));
    var toml = Document(ConfigurationReader.ReadText("morphir.toml", "[project]\nname='Acme'\n[custom]\ncount=4\ntext='4'\nstrict=true\nlist=[1,2]\nsecret={env='TOKEN'}\n"));
    Check(json.Values.Equals(yaml.Values) && json.Values.Equals(toml.Values), "Equivalent format values differ");

    var extended = Document(ConfigurationReader.ReadText("extended.json", "\uFEFF{/*x*/ escaped:'\\x41\\u0042',\\u0061:+.5,hex:0x2a,continuation:'a\\\nb',large:9223372036854775808,precise:9007199254740993,inf:+Infinity,nan:-NaN,arr:[true,null,],}"));
    Check(At(extended.Values, "a").Equals(ConfigValue.NewNumber(.5)), "JSON5 leading decimal failed");
    Check(At(extended.Values, "large") is ConfigValue.BigInteger, "JSON5 large integer lost exactness");
    Check(At(extended.Values, "precise").Equals(ConfigValue.NewInteger(9007199254740993)), "JSON5 integer exceeded IEEE exact range");
    Check(double.IsPositiveInfinity(((ConfigValue.Number)At(extended.Values, "inf")).Item), "JSON5 infinity failed");
    Check(double.IsNaN(((ConfigValue.Number)At(extended.Values, "nan")).Item), "JSON5 NaN failed");
    Check(ConfigurationReader.ReadText("duplicate.json", "{name:1,\\u006eame:2}").Content is SourceContent.Invalid, "Decoded duplicate accepted");
    var malformed = ConfigurationReader.ReadText("malformed.yaml", "project: [unterminated");
    Check(malformed.Content is SourceContent.Invalid invalid && invalid.Item[0].Position is not null,
        "Malformed YAML lost source position");

    var classic = Document(ConfigurationReader.ReadText("classic.json", "{name:'Classic',sourceDirectory:'elm-src',exposedModules:['Api'],decorations:{legacy:'retained'}}"));
    Check(At(classic.Values, "project", "source_directory").Equals(ConfigValue.NewString("elm-src")), "Classic normalization failed");
    Check(ConfigTree.tryFind(List(["decorations"]), classic.Raw) is not null, "Classic raw fields lost");
    var resolved = Resolver.resolve(Input([new SourceInput(classic.Source, SourceContent.NewAvailable(classic), true)], ProfileSelection.Disabled));
    Check(At(resolved.Effective, "pipeline", "backend").Equals(ConfigValue.NewString("checkpoint")), "Legacy low-priority checkpoint default missing");

    var normalized = Normalization.normalize(json.Source, json.Raw, json.Positions);
    var first = Resolver.resolve(Input([new SourceInput(json.Source, SourceContent.NewAvailable(json), true)], ProfileSelection.Disabled));
    var second = Resolver.resolve(Input([new SourceInput(json.Source, SourceContent.NewAvailable(normalized), true)], ProfileSelection.Disabled));
    Check(first.Effective.Equals(second.Effective) && first.Origins.Equals(second.Origins), "Typed in-memory host differs from adapter");

    var called = false;
    var bad = new SourceIdentity("inactive.json", DocumentFormat.Json, SourceRole.WorkspaceProfile, 0, FSharpOption<string>.Some("bad"));
    _ = ConfigurationLoader.LoadAndResolve(Input([], ProfileSelection.Disabled), [new ConfigurationCandidate(bad, () =>
    {
        called = true;
        throw new InvalidOperationException("Inactive profile executed");
    })]);
    Check(!called, "Disabled profile reader invoked");

    var postgres = ConfigurationReader.ReadText("postgres.toml", "[custom]\nwinner='member postgres'", SourceRole.MemberProfile, profile: "postgres");
    var metrics = ConfigurationReader.ReadText("metrics.json", "{custom:{winner:'workspace metrics'}}", SourceRole.WorkspaceProfile, profile: "metrics");
    var profileResult = ConfigurationLoader.LoadAndResolve(Input([], ProfileSelection.NewNames(List(["postgres", "metrics"]))),
        [new ConfigurationCandidate(postgres.Source, () => postgres), new ConfigurationCandidate(metrics.Source, () => metrics)]);
    Check(At(profileResult.Effective, "custom", "winner").Equals(ConfigValue.NewString("workspace metrics")), "Profile name precedence failed");
    Console.WriteLine($"{{\"success\":true,\"formats\":3,\"json5\":true,\"normalization\":true,\"profile_loading\":true,\"host_parity\":true,\"syntax_cases\":{syntaxCases},\"morphir_cases\":{morphirCases}}}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
