using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

public sealed class VariantTests
{
    [Test]
    public void MaybeBranchesInvokeOnlyThePresentCallback()
    {
        var calls = 0;
        int Increment(int x) { calls++; return x + 1; }
        Maybe<int>.Nothing.Map(Increment).ShouldBe(Maybe<int>.Nothing);
        Maybe<int>.Just(2).Map(Increment).ShouldBe(Maybe<int>.Just(3));
        calls.ShouldBe(1);
    }

    [Test]
    public void ResultPreservesErrorFirstSuccessSecond()
    {
        Result<string, int>.Ok(7).Map(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(Result<string, string>.Ok("7"));
        Result<string, int>.Err("negative").AndThen(x => Result<string, int>.Ok(x + 1))
            .ShouldBe(Result<string, int>.Err("negative"));
    }

    [Test]
    public void UnitAndOrderHaveExplicitValues()
    {
        Unit.Value.ShouldBe(default(Unit));
        Basics.LT.ShouldBe(Order.LT);
        Basics.EQ.ShouldBe(Order.EQ);
        Basics.GT.ShouldBe(Order.GT);
    }
}
