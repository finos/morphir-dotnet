using System.Text.Json.Nodes;
using Shouldly;

namespace Morphir.SDK.Conformance.Tests;

public sealed class FixtureTests
{
    internal static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    internal static string CorpusPath => Path.Combine(FixtureRoot, "cases.json");

    [Test]
    public void UnknownOperationFailsWithLocatedDiagnostic()
    {
        var corpus = JsonNode.Parse(File.ReadAllText(CorpusPath))!;
        corpus["cases"]![0]!["operation"] = "Morphir.SDK.Unknown.operation";
        FixtureValidator.Validate(FixtureRoot, corpus.ToJsonString())
            .ShouldContain(error => error.Contains("Basics.add/int/operation", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("callback", "List.map/normal", "callback")]
    [Arguments("callback type", "List.map/normal", "callback")]
    [Arguments("unordered dict", "Dict.toList/ordered", "entries")]
    [Arguments("duplicate set", "Set.toList/ordered", "items")]
    [Arguments("int range", "Basics.add/int", "Int32")]
    [Arguments("decimal scale", "Decimal.add/exact", "Decimal96")]
    [Arguments("result payload", "Result.Ok/constructor", "String")]
    [Arguments("noncanonical decimal", "Decimal.add/exact", "canonical")]
    [Arguments("false exclusion", "Basics.toFloat/outside-int", "exclusion")]
    [Arguments("unknown error", "Basics.add/int-overflow", "error/code")]
    [Arguments("bad preservation", "List.cons/prepend", "preserveArguments")]
    [Arguments("noncomparable", "Basics.compare/int-lt", "comparable")]
    public void ProfileAndProjectionDefectsFail(string defect, string id, string field)
    {
        var corpus = JsonNode.Parse(File.ReadAllText(CorpusPath))!;
        var entry = corpus["cases"]!.AsArray().Single(x => x!["caseId"]!.ToString() == id)!;
        switch (defect)
        {
            case "callback": entry["arguments"]![0]!["id"] = "unregistered"; break;
            case "callback type": entry["arguments"]![0]!["id"] = "increment"; break;
            case "unordered dict": entry["arguments"]![0]!["entries"]!.AsArray().ReverseInPlace(); break;
            case "duplicate set": entry["arguments"]![0]!["items"]!.AsArray().Add(entry["arguments"]![0]!["items"]![0]!.DeepClone()); break;
            case "int range": entry["arguments"]![0]!["value"] = "2147483648"; break;
            case "decimal scale": entry["arguments"]![0]!["scale"] = 29; break;
            case "result payload": entry["typeArguments"]!["a"] = "String"; break;
            case "noncanonical decimal": entry["expected"]!["value"]!["coefficient"] = "1750"; entry["expected"]!["value"]!["scale"] = 3; break;
            case "false exclusion": entry["exclusion"]!["code"] = "CharOutOfRange"; break;
            case "unknown error": entry["expected"]!["error"]!["code"] = "WhateverPasses"; break;
            case "bad preservation": entry["invariants"]!["preserveArguments"] = new JsonArray(99); break;
            case "noncomparable": entry["typeArguments"]!["a"] = "Maybe<Int>"; break;
        }
        FixtureValidator.Validate(FixtureRoot, corpus.ToJsonString())
            .ShouldContain(error => error.Contains(id, StringComparison.Ordinal) && error.Contains(field, StringComparison.Ordinal));
    }

    [Test]
    public void SourceBindingMustResolveToExactPinnedDeclaration()
    {
        var inventory = JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "inventory.json")))!;
        inventory["operations"]![0]!["sourceBinding"]!["line"] = 1;
        FixtureValidator.Validate(FixtureRoot, inventoryText: inventory.ToJsonString())
            .ShouldContain(error => error.Contains("Morphir.SDK.Basics.add/sourceBinding", StringComparison.Ordinal));
    }

    [Test]
    public void KnownGapCannotBecomeAnUncoveredInformationalNote()
    {
        var corpus = JsonNode.Parse(File.ReadAllText(CorpusPath))!;
        foreach (var entry in corpus["cases"]!.AsArray()) entry!["knownGaps"] = new JsonArray();
        FixtureValidator.Validate(FixtureRoot, corpus.ToJsonString())
            .ShouldContain(error => error.Contains("FS-DICT-UNION", StringComparison.Ordinal) && error.Contains("required", StringComparison.Ordinal));
    }
}

internal static class JsonArrayTestExtensions
{
    internal static void ReverseInPlace(this JsonArray array)
    {
        var copies = array.Select(x => x!.DeepClone()).Reverse().ToArray();
        array.Clear();
        foreach (var copy in copies) array.Add(copy);
    }
}
