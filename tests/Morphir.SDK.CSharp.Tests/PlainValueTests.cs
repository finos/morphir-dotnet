using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

/// <summary>Plain C# records and plain C# unions are Morphir values, and compare with their own equality.</summary>
public sealed class PlainValueTests
{
    public sealed record Circle(double Radius);
    public sealed record Rect(double W, double H);
    public union Shape(Circle, Rect);
    public sealed record WithFunction(Func<int, int> Callback);
    public sealed record Wrapper(WithFunction Inner);

    [Test]
    public void TwoListsOfEqualPlainRecordsAreEqualWithEqualHashes()
    {
        var left = List<Circle>.FromEnumerable([new Circle(1d), new Circle(2d)]);
        var right = List<Circle>.FromEnumerable([new Circle(1d), new Circle(2d)]);
        Basics.Equal(left, right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
        Basics.Equal(new Circle(1d), new Circle(1d)).ShouldBeTrue();
        Basics.Equal(new Circle(1d), new Circle(2d)).ShouldBeFalse();
        MorphirValue.Hash(new Circle(1d)).ShouldBe(MorphirValue.Hash(new Circle(1d)));
    }

    [Test]
    public void TwoPlainUnionsWithTheSameCaseAndValueAreEqualAndDifferentCasesAreNot()
    {
        var a = new Shape(new Circle(1d));
        var b = new Shape(new Circle(1d));
        var c = new Shape(new Rect(1d, 2d));
        Basics.Equal(a, b).ShouldBeTrue();
        MorphirValue.Hash(a).ShouldBe(MorphirValue.Hash(b));
        Basics.Equal(a, c).ShouldBeFalse();
        Basics.Equal(List<Shape>.Singleton(a), List<Shape>.Singleton(b)).ShouldBeTrue();
    }

    [Test]
    public void APlainRecordKeepsItsOwnEqualityForNaN()
    {
        // The record compares its fields as plain C# does: NaN is equal to NaN. A double in an SDK value compares as a double.
        Basics.Equal(new Circle(double.NaN), new Circle(double.NaN)).ShouldBeTrue();
        Basics.Equal(List<double>.Singleton(double.NaN), List<double>.Singleton(double.NaN)).ShouldBeFalse();
        Basics.Equal(double.NaN, double.NaN).ShouldBeFalse();
    }

    [Test]
    public void AFunctionTypedFieldComparesItsDelegateByReferenceAsDotNetDoes()
    {
        // The SDK has no function check at run time: the backend refuses equality on a type that holds a function.
        // Code that people write gets the equality of .NET, so the same delegate is equal and two delegates are not.
        Func<int, int> callback = x => x;
        Func<int, int> other = x => x + 1;
        var a = new WithFunction(callback);
        Basics.Equal(a, new WithFunction(callback)).ShouldBeTrue();
        MorphirValue.Hash(a).ShouldBe(MorphirValue.Hash(new WithFunction(callback)));
        Basics.Equal(a, new WithFunction(other)).ShouldBeFalse();
        Basics.Equal(new Wrapper(a), new Wrapper(a)).ShouldBeTrue();
        Basics.Equal(List<WithFunction>.Singleton(a), List<WithFunction>.Singleton(a)).ShouldBeTrue();
    }

    [Test]
    public void ATupleThatHoldsAPlainRecordComparesTheRecordWithItsOwnEquality()
    {
        Basics.Equal((new Circle(1d), 2), (new Circle(1d), 2)).ShouldBeTrue();
        MorphirValue.Hash((new Circle(1d), 2)).ShouldBe(MorphirValue.Hash((new Circle(1d), 2)));
        Basics.Equal((new Circle(1d), 2), (new Circle(3d), 2)).ShouldBeFalse();
    }
}
