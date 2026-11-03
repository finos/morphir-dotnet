using Reqnroll;
using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

[Binding]
public sealed class ImmutableRuntimeSteps
{
    private (string[] Executed, string[] Passed, string[] Excluded, string[] Failures) _report;

    [When("a C# consumer maps present, absent, success and error values")]
    public void MapVariants()
    {
        new VariantTests().MaybeBranchesInvokeOnlyThePresentCallback();
        new VariantTests().ResultPreservesErrorFirstSuccessSecond();
    }
    [Then("only present and successful values invoke their callbacks")]
    public void CallbackBranches() => Maybe<int>.Nothing.AndThen<int>(_ => throw new InvalidOperationException()).IsJust.ShouldBeFalse();
    [Then("string errors and integer successes retain their types")]
    public void ResultTypes() => Result<string, int>.Err("failure").Match(e => e, v => v.ToString()).ShouldBe("failure");
    [When("a C# consumer updates persistent SDK collections")]
    public void UpdateCollections() => new CollectionTests().SortedCollectionsUseExplicitMorphirOrderingAndFirstUnionWins();
    [Then("retained operands remain unchanged")]
    public void Preserved() => new CollectionTests().ListUpdatesRetainOldValues();
    [Then("independently constructed nested values have equal hashes")]
    public void Hashes() => new CollectionTests().NestedValuesAndRecordsHaveStructuralEqualityAndHashes();
    [When("the C# adapter executes the frozen required SDK cases")]
    public void RunCorpus() => _report = RuntimeAdapter.RunAll();
    [Then("all {int} required case IDs pass without skips")]
    public void Required(int count)
    {
        _report.Failures.ShouldBeEmpty();
        _report.Executed.Length.ShouldBe(count);
        _report.Passed.ShouldBe(_report.Executed);
    }
    [Then("the eight representation exclusions are reported separately")]
    public void Exclusions() => _report.Excluded.Length.ShouldBe(8);
}
