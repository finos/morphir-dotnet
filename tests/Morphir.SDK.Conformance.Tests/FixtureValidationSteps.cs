using System.Text.Json.Nodes;
using Reqnroll;
using Shouldly;

namespace Morphir.SDK.Conformance.Tests;

[Binding]
public sealed class FixtureValidationSteps
{
    private JsonObject _corpus = null!;
    private IReadOnlyList<string> _errors = [];

    [Given("the checked-in SDK corpus")]
    public void GivenCorpus() => _corpus = JsonNode.Parse(File.ReadAllText(FixtureTests.CorpusPath))!.AsObject();

    [Given("I introduce the fixture defect {string}")]
    public void GivenDefect(string defect)
    {
        var first = _corpus["cases"]![0]!.AsObject();
        switch (defect)
        {
            case "duplicate case": _corpus["cases"]!.AsArray().Add(first.DeepClone()); break;
            case "missing expectation": first.Remove("expected"); break;
            case "unknown operation": first["operation"] = "Morphir.SDK.Unknown.operation"; break;
            case "invalid tag": first["arguments"]![0]!["tag"] = "opaque"; break;
            case "incomplete error": first["expected"] = new JsonObject { ["error"] = new JsonObject { ["code"] = "Overflow" } }; break;
            case "unclassified case": first.Remove("classification"); break;
            default: throw new ArgumentException("Unknown test defect", nameof(defect));
        }
    }

    [When("I validate the corpus offline")]
    public void WhenValidate() => _errors = FixtureValidator.Validate(FixtureTests.FixtureRoot, _corpus.ToJsonString());

    [Then("there are no conformance fixture errors")]
    public void ThenNoErrors() => _errors.ShouldBeEmpty();

    [Then("a conformance fixture error contains {string}")]
    public void ThenError(string field)
    {
        _errors.ShouldContain(error => error.Contains("Basics.add/int", StringComparison.Ordinal) && error.Contains(field, StringComparison.Ordinal));
    }
}
