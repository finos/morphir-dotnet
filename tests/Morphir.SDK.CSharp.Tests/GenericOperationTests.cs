using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

/// <summary>
/// The generic overloads of Basics for <c>T : INumber&lt;T&gt;</c> (the class <c>number</c>) and the order of
/// <c>List&lt;T&gt;</c> as <c>IComparable&lt;List&lt;T&gt;&gt;</c> (the class <c>comparable</c>).
/// </summary>
public sealed class GenericOperationTests
{
    private static T Sum<T>(T a, T b) where T : System.Numerics.INumber<T> => Basics.Add(a, b);

    [Test]
    public void CheckedGenericAdditionThrowsOverflowExceptionAsTheIntOverloadDoes()
    {
        Should.Throw<OverflowException>(() => Basics.Add(int.MaxValue, 1));
        Should.Throw<OverflowException>(() => Sum(int.MaxValue, 1));
        Sum(2, 3).ShouldBe(5);
        Sum(1.5, 2.25).ShouldBe(3.75);
    }

    [Test]
    public void EachGenericNumericOperationGivesTheResultOfItsIntAndFloatOverloads()
    {
        Basics.Subtract<int>(7, 9).ShouldBe(Basics.Subtract(7, 9));
        Basics.Subtract<double>(7.5, 9d).ShouldBe(Basics.Subtract(7.5, 9d));
        Basics.Multiply<int>(6, 7).ShouldBe(Basics.Multiply(6, 7));
        Basics.Multiply<double>(1.5, 4d).ShouldBe(Basics.Multiply(1.5, 4d));
        Basics.Negate<int>(5).ShouldBe(Basics.Negate(5));
        Basics.Negate<double>(2.5).ShouldBe(Basics.Negate(2.5));
        Basics.Abs<int>(-5).ShouldBe(Basics.Abs(-5));
        Basics.Abs<double>(-2.5).ShouldBe(Basics.Abs(-2.5));
        Basics.Clamp<int>(1, 10, 42).ShouldBe(Basics.Clamp(1, 10, 42));
        Basics.Clamp<double>(1d, 10d, -3d).ShouldBe(Basics.Clamp(1d, 10d, -3d));
        Basics.Power<int>(3, 4).ShouldBe(Basics.Power(3, 4));
        Basics.Power<double>(2d, 0.5).ShouldBe(Basics.Power(2d, 0.5));
    }

    [Test]
    public void GenericIntOperationsKeepTheirOverflowFailure()
    {
        Should.Throw<OverflowException>(() => Basics.Subtract<int>(int.MinValue, 1));
        Should.Throw<OverflowException>(() => Basics.Multiply<int>(int.MaxValue, 2));
        Should.Throw<OverflowException>(() => Basics.Negate<int>(int.MinValue));
        Should.Throw<OverflowException>(() => Basics.Abs<int>(int.MinValue));
        Should.Throw<OverflowException>(() => Basics.Power<int>(2, 31));
        Should.Throw<NotSupportedException>(() => Basics.Power<int>(2, -1));
    }

    private static T Largest<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;

    [Test]
    public void AListIsComparableWithTheMorphirOrder()
    {
        var shorter = List<int>.Singleton(1);
        var longer = List<int>.FromEnumerable([1, 0]);
        shorter.CompareTo(longer).ShouldBeLessThan(0);
        longer.CompareTo(shorter).ShouldBeGreaterThan(0);
        shorter.CompareTo(List<int>.Singleton(1)).ShouldBe(0);
        Largest(shorter, longer).ShouldBe(longer);
    }

    [Test]
    public void TheOrderOfAListOfStringsIsOrdinalAsInMorphirComparers()
    {
        // Ordinal: "B" (U+0042) is before "a" (U+0061). The culture order of string.CompareTo puts "a" first.
        var upper = List<string>.Singleton("B");
        var lower = List<string>.Singleton("a");
        upper.CompareTo(lower).ShouldBeLessThan(0);
        Math.Sign(upper.CompareTo(lower)).ShouldBe(Math.Sign(MorphirComparers.List(MorphirComparers.String).Compare(upper, lower)));
    }

    [Test]
    public void AListOfAnOrderingOutsideTheDomainFailsExplicitly()
    {
        Should.Throw<NotSupportedException>(() => List<bool>.Singleton(true).CompareTo(List<bool>.Singleton(false)));
        Should.Throw<ArgumentNullException>(() => List<int>.Empty.CompareTo(null));
    }
}
