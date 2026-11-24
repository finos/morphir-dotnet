// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

using Morphir.Diagrams.Layout;
using Shouldly;

namespace Morphir.Diagrams.Tests;

/// <summary>Distances in quarter EMU, fractions and scales.</summary>
public class LayoutUnitTests
{
    [Test]
    public void PixelsConvertExactly()
    {
        var distance = Distance.FromPixels(100.02);

        distance.QuarterEmu.ShouldBe(3_810_762L);
        distance.ToPixels().ShouldBe(100.02);
    }

    [Test]
    [Arguments(0.01, 381L)]
    [Arguments(1.0, 38_100L)]
    [Arguments(-40.0, -1_524_000L)]
    [Arguments(1.5, 57_150L)]
    public void EveryHundredthOfAPixelIsExact(double pixels, long quarterEmu)
    {
        Distance.FromPixels(pixels).QuarterEmu.ShouldBe(quarterEmu);
        Distance.FromPixels(pixels).ToPixels().ShouldBe(pixels);
    }

    [Test]
    public void PointsInchesCentimetresMillimetresAndEmuAreExact()
    {
        Distance.FromPoints(1).QuarterEmu.ShouldBe(50_800L);
        Distance.FromInches(1).QuarterEmu.ShouldBe(3_657_600L);
        Distance.FromCentimetres(1).QuarterEmu.ShouldBe(1_440_000L);
        Distance.FromMillimetres(1).QuarterEmu.ShouldBe(144_000L);
        Distance.FromEmu(12_700).ShouldBe(Distance.FromPoints(1));
        Distance.FromInches(1).ToPoints().ShouldBe(72.0);
        Distance.FromCentimetres(2.54).ShouldBe(Distance.FromInches(1));
        Distance.FromMillimetres(25.4).ToInches().ShouldBe(1.0);
        Distance.FromMillimetres(3).ToMillimetres().ShouldBe(3.0);
        Distance.FromCentimetres(3).ToCentimetres().ShouldBe(3.0);
    }

    [Test]
    public void OtherValuesRoundToTheNearestUnit()
    {
        Distance.FromEmu(1).QuarterEmu.ShouldBe(4L);
        Distance.FromPixels(0.004).QuarterEmu.ShouldBe(152L);
        Distance.FromPoints(0.00001).QuarterEmu.ShouldBe(1L);
        Distance.FromPoints(-0.00001).QuarterEmu.ShouldBe(-1L);
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity)]
    [Arguments(1e300)]
    public void ANonFiniteOrOutOfRangeLengthIsRefused(double pixels) =>
        Should.Throw<ArgumentException>(() => Distance.FromPixels(pixels));

    [Test]
    public void WholeEmuRoundsHalfToEven()
    {
        Distance.FromQuarterEmu(2).ToEmu().ShouldBe(0L);
        Distance.FromQuarterEmu(6).ToEmu().ShouldBe(2L);
        Distance.FromQuarterEmu(-6).ToEmu().ShouldBe(-2L);
        Distance.FromQuarterEmu(7).ToEmu().ShouldBe(2L);
        Distance.FromQuarterEmu(5).ToEmu().ShouldBe(1L);
    }

    [Test]
    public void ArithmeticIsCheckedAndScalingRoundsHalfToEven()
    {
        var a = Distance.FromPixels(1.5);
        var b = Distance.FromPixels(0.5);

        (a + b).ShouldBe(Distance.FromPixels(2));
        (a - b).ShouldBe(Distance.FromPixels(1));
        (-a).ShouldBe(Distance.FromPixels(-1.5));
        (a * 2.0).ShouldBe(Distance.FromPixels(3));
        Distance.FromQuarterEmu(5).Scale(0.5).QuarterEmu.ShouldBe(2L);
        Distance.FromQuarterEmu(7).Scale(0.5).QuarterEmu.ShouldBe(4L);
        (a / b).ShouldBe(3.0);
        a.CompareTo(b).ShouldBePositive();
        Distance.Max(a, b).ShouldBe(a);
        Distance.Min(a, b).ShouldBe(b);
        Should.Throw<OverflowException>(() => Distance.FromQuarterEmu(long.MaxValue) + Distance.FromQuarterEmu(1));
        Should.Throw<ArgumentException>(() => Distance.FromQuarterEmu(long.MaxValue).Scale(2));
        default(Distance).ShouldBe(Distance.Zero);
        Distance.FromPixels(1.5).ToString().ShouldBe("1.5px");
    }

    [Test]
    public void AnOutOfRangeFractionIsRefused()
    {
        Fraction.Parse(1.5).IsError.ShouldBeTrue();
        Fraction.TryParse(1.5, out var result, out var failure).ShouldBeFalse();
        result.ShouldBeNull();
        failure.Code.ShouldBe("layout/fraction-out-of-range");
        Fraction.TryParse(double.NaN, out _, out _).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => Fraction.Create(-0.1));
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(0.25)]
    [Arguments(1.0)]
    public void FractionsFromZeroToOneAreKept(double value)
    {
        Fraction.Create(value).Value.ShouldBe(value);
        Fraction.TryParse(value, out var result, out var failure).ShouldBeTrue();
        failure.ShouldBeNull();
        result.ShouldBe(Fraction.Create(value));
    }

    [Test]
    public void FractionPointsHoldTwoFractions()
    {
        var point = FractionPoint.Create(Fraction.One, Fraction.Half);

        point.X.ShouldBe(Fraction.One);
        point.Y.Value.ShouldBe(0.5);
        point.ShouldBe(FractionPoint.Create(Fraction.Create(1), Fraction.Create(0.5)));
        point.ToString().ShouldBe("(1, 0.5)");
        Fraction.Zero.Value.ShouldBe(0.0);
    }

    [Test]
    public void ScalesAreFiniteAndGreaterThanZero()
    {
        Scale.Create(2).Value.ShouldBe(2.0);
        Scale.One.Value.ShouldBe(1.0);
        Scale.TryParse(0, out _, out var failure).ShouldBeFalse();
        failure.Code.ShouldBe("layout/scale-out-of-range");
        Scale.Parse(double.PositiveInfinity).IsError.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => Scale.Create(-1));
        Scale.Create(0.5).ShouldBe(Scale.Create(0.5));
    }
}
