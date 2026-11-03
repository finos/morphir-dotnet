using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

public sealed class ScalarTests
{
    [Test]
    public void CheckedIntegersAndCallbacksDoNotWrap()
    {
        Should.Throw<OverflowException>(() => Basics.Add(int.MaxValue, 1));
        Should.Throw<OverflowException>(() => Maybe<int>.Just(int.MaxValue).Map(x => Basics.Add(x, 1)));
        Basics.RemainderBy(-1, int.MinValue).ShouldBe(0);
        Basics.ModBy(-3, 7).ShouldBe(-2);
        Basics.IntegerDivide(-7, 3).ShouldBe(-2);
        Basics.Power(-2, 3).ShouldBe(-8);
        Should.Throw<DivideByZeroException>(() => Basics.IntegerDivide(7, 0));
    }

    [Test]
    public void FloatRoundingAndClampFollowMorphirBranches()
    {
        Basics.Round(-2.5).ShouldBe(-2);
        Basics.Round(2.5).ShouldBe(3);
        Should.Throw<NonFiniteConversionException>(() => Basics.Floor(double.NaN));
        Basics.Clamp(10, 0, 5).ShouldBe(10);
        double.IsNaN(Basics.Clamp(0d, 1d, double.NaN)).ShouldBeTrue();
        BitConverter.DoubleToInt64Bits(Basics.Clamp(-0d, +0d, -0d)).ShouldBe(long.MinValue);
        Basics.Equal(double.NaN, double.NaN).ShouldBeFalse();
        Basics.Equal(-0d, +0d).ShouldBeTrue();
    }

    [Test]
    public void DecimalArithmeticIsExactOrFailsExplicitly()
    {
        Decimal.ToString(Decimal.Add(1.50m, 0.25m)).ShouldBe("1.75");
        Decimal.Div(1m, 8m).ShouldBe(Maybe<decimal>.Just(0.125m));
        Decimal.Div(1m, 0m).ShouldBe(Maybe<decimal>.Nothing);
        Should.Throw<OverflowException>(() => Decimal.Mul(decimal.MaxValue, 10m));
        Should.Throw<NotSupportedException>(() => Decimal.Div(1m, 3m));
        Should.Throw<NotSupportedException>(() => Decimal.Mul(0.0000000000000000000000000001m, 0.1m));
    }
}
