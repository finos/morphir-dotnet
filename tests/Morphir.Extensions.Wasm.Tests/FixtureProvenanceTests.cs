using System.Text.Json;
using System.Text.RegularExpressions;

namespace Morphir.Extensions.Wasm.Tests;

/// <summary>
/// Task 0.2 of add-wasm-extension-host (design PIN-1): the pinned contract, corpus and modules keep
/// their upstream bytes, and each module is the one the corpus records.
/// </summary>
public partial class FixtureProvenanceTests
{
    // Counted in spec/mep/mck/wasm-host.cases.json at finos/morphir 7b049c223 (MepWasmFixtures.PinnedCommit).
    // Change them only when the pin moves; a smaller number here means the corpus was truncated.
    private const int PinnedCaseCount = 56;
    private const int PinnedModuleCount = 49;

    [GeneratedRegex(@"(?m)^\| `([^`]+)` \| `[^`]+` \| `([0-9a-f]{64})` \|\r?$")]
    private static partial Regex ProvenanceRow();

    private static Dictionary<string, string> ProvenanceEntries(string manifest)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match row in ProvenanceRow().Matches(manifest))
        {
            entries.Add(row.Groups[1].Value, row.Groups[2].Value);
        }

        return entries;
    }

    [Test]
    public async Task Every_recorded_file_keeps_its_recorded_SHA256()
    {
        var entries = ProvenanceEntries(await File.ReadAllTextAsync(MepWasmFixtures.Provenance));
        await Assert.That(entries.Count).IsGreaterThan(0);

        var mismatches = new List<string>();
        foreach (var (file, expected) in entries)
        {
            var path = MepWasmFixtures.FullPath(file);
            if (!File.Exists(path))
            {
                mismatches.Add($"{file}: missing");
                continue;
            }

            var actual = MepWasmFixtures.Sha256(path);
            if (actual != expected)
            {
                mismatches.Add($"{file}: recorded {expected}, found {actual}");
            }
        }

        await Assert.That(mismatches).IsEmpty();
    }

    [Test]
    public async Task Every_pinned_file_has_a_provenance_row()
    {
        var entries = ProvenanceEntries(await File.ReadAllTextAsync(MepWasmFixtures.Provenance));

        var unrecorded = Directory
            .GetFiles(MepWasmFixtures.Root, "*", SearchOption.AllDirectories)
            .Select(MepWasmFixtures.RelativeName)
            .Where(name => name != "PROVENANCE.md" && !entries.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(unrecorded).IsEmpty();
    }

    [Test]
    public async Task Provenance_names_the_pinned_commit()
    {
        var manifest = await File.ReadAllTextAsync(MepWasmFixtures.Provenance);

        await Assert.That(manifest).Contains($"`{MepWasmFixtures.PinnedCommit}`");
    }

    [Test]
    public async Task Provenance_rows_parse_the_same_with_either_line_ending()
    {
        var manifest = (await File.ReadAllTextAsync(MepWasmFixtures.Provenance)).Replace("\r\n", "\n");
        var lf = ProvenanceEntries(manifest);
        var crlf = ProvenanceEntries(manifest.Replace("\n", "\r\n"));

        await Assert.That(crlf).IsEquivalentTo(lf);
    }

    [Test]
    public async Task Every_module_has_the_digest_the_corpus_records()
    {
        using var corpus = JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Corpus));
        var corpusDirectory = Path.GetDirectoryName(MepWasmFixtures.Corpus)!;

        var mismatches = new List<string>();
        var checkedModules = 0;
        foreach (var module in corpus.RootElement.GetProperty("modules").EnumerateObject())
        {
            // The corpus records each module as { "wat", "wasm", "sha256" }, with paths relative to the corpus file.
            var wasm = Path.Combine(corpusDirectory, module.Value.GetProperty("wasm").GetString()!);
            var wat = Path.Combine(corpusDirectory, module.Value.GetProperty("wat").GetString()!);
            var expected = module.Value.GetProperty("sha256").GetString()!;
            checkedModules++;

            if (!File.Exists(wat))
            {
                mismatches.Add($"{module.Name}: source {MepWasmFixtures.RelativeName(wat)} missing");
            }

            if (!File.Exists(wasm))
            {
                mismatches.Add($"{module.Name}: module {MepWasmFixtures.RelativeName(wasm)} missing");
                continue;
            }

            var actual = MepWasmFixtures.Sha256(wasm);
            if (actual != expected)
            {
                mismatches.Add($"{module.Name}: corpus records {expected}, module is {actual}");
            }
        }

        await Assert.That(mismatches).IsEmpty();
        await Assert.That(checkedModules).IsEqualTo(PinnedModuleCount);
    }

    [Test]
    public async Task Corpus_has_every_pinned_case_and_module()
    {
        using var corpus = JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Corpus));
        var root = corpus.RootElement;

        var modules = root.GetProperty("modules").EnumerateObject().Select(module => module.Name).ToHashSet(StringComparer.Ordinal);
        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        var unknownModules = cases
            .Select(@case => @case.GetProperty("module").GetString()!)
            .Where(name => !modules.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await Assert.That(cases.Count).IsEqualTo(PinnedCaseCount);
        await Assert.That(modules.Count).IsEqualTo(PinnedModuleCount);
        await Assert.That(unknownModules).IsEmpty();
    }

    [Test]
    public async Task Profile_contract_loads_as_JSON()
    {
        using var contract = JsonDocument.Parse(await File.ReadAllBytesAsync(MepWasmFixtures.Contract));

        await Assert.That(contract.RootElement.ValueKind).IsEqualTo(JsonValueKind.Object);
    }
}
