using System.Globalization;
using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

public sealed class CollectionTests
{
    [Test]
    public void ListUpdatesRetainOldValues()
    {
        var old = List<int>.FromEnumerable([2, 3]);
        old.Cons(1).Map(x => x + 1).ShouldBe(List<int>.FromEnumerable([2, 3, 4]));
        old.ShouldBe(List<int>.FromEnumerable([2, 3]));
        old.Append(List<int>.Singleton(4)).Length.ShouldBe(3);
        old.Head().ShouldBe(Maybe<int>.Just(2));
        old.Tail().ShouldBe(Maybe<List<int>>.Just(List<int>.Singleton(3)));
        List<int>.Empty.Tail().ShouldBe(Maybe<List<int>>.Nothing);
    }

    [Test]
    public void TheFunctionalFormsOfTheBackendGiveSdkLists()
    {
        // The functional profile of the C# backend writes Select, Where, FilterMap and Aggregate on an SDK list. The
        // methods of the list come before the extension methods of LINQ, so each call gives an SDK value.
        var numbers = List<int>.FromEnumerable([1, 2, 3, 4]);
        List<int> doubled = numbers.Select(x => x * 2);
        List<int> even = numbers.Where(x => x % 2 == 0);
        List<string> named = numbers.FilterMap(x => x > 2 ? Maybe<string>.Just("n" + x) : Maybe<string>.Nothing);
        string folded = numbers.Aggregate("", (state, x) => state + x);
        doubled.ShouldBe(List<int>.FromEnumerable([2, 4, 6, 8]));
        even.ShouldBe(List<int>.FromEnumerable([2, 4]));
        named.ShouldBe(List<string>.FromEnumerable(["n3", "n4"]));
        folded.ShouldBe("1234");
        numbers.Aggregate(0, (state, x) => state - x).ShouldBe(numbers.Foldl<int>(x => state => state - x, 0));
        numbers.ShouldBe(List<int>.FromEnumerable([1, 2, 3, 4]));
    }

    [Test]
    [Category("PlatformSpecific")]
    public void SortedCollectionsUseExplicitMorphirOrderingAndFirstUnionWins()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var old = Dict<string, int>.FromEnumerable([("b", 2), ("a", 1), ("a", 3)], MorphirComparers.String);
            old.Union(Dict<string, int>.Singleton("a", 99, MorphirComparers.String)).Get("a").ShouldBe(Maybe<int>.Just(3));
            old.Insert("c", 4).Remove("b").ToList().ShouldBe(List<(string, int)>.FromEnumerable([("a", 3), ("c", 4)]));
            old.ToList().ShouldBe(List<(string, int)>.FromEnumerable([("a", 3), ("b", 2)]));
            var ordered = Set<string>.FromEnumerable(["\uE000", "\U00010000", "a"], MorphirComparers.String);
            ordered.ToList().ShouldBe(List<string>.FromEnumerable(["a", "\U00010000", "\uE000"]));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [Test]
    public void NestedValuesAndRecordsHaveStructuralEqualityAndHashes()
    {
        var a = new Box(List<Maybe<decimal>>.Singleton(Maybe<decimal>.Just(1.5m)));
        var b = new Box(List<Maybe<decimal>>.Singleton(Maybe<decimal>.Just(1.50m)));
        Basics.Equal(a, b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        Maybe<double>.Just(double.NaN).Equals(Maybe<double>.Just(double.NaN)).ShouldBeFalse();
        Result<string, double>.Ok(double.NaN).Equals(Result<string, double>.Ok(double.NaN)).ShouldBeFalse();
        var first = Set<int>.FromEnumerable([3, 1, 2], MorphirComparers.Int);
        var second = Set<int>.FromEnumerable([2, 3, 1], MorphirComparers.Int);
        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Test]
    public void UnsupportedOrderingDomainsDoNotAcquireClrComparers()
    {
        Should.Throw<NotSupportedException>(() => Basics.Compare(true, false));
        Should.Throw<NotSupportedException>(() => Basics.Compare(double.NaN, 1d));
        // Equality has no such refusal: a value outside the known kinds uses its own Equals (ValueDomainTests).
        var c = MorphirComparers.List(MorphirComparers.Int);
        c.Compare(List<int>.Singleton(1), List<int>.FromEnumerable([1, 0])).ShouldBeLessThan(0);
        MorphirComparers.Tuple(MorphirComparers.Int, MorphirComparers.String)
            .Compare((1, "a"), (1, "b")).ShouldBeLessThan(0);
    }

    private sealed record Box(List<Maybe<decimal>> Values) : IMorphirValue
    {
        public bool MorphirEquals(IMorphirValue other) => other is Box box && Basics.Equal(Values, box.Values);
        public int MorphirHashCode() => MorphirValue.Hash(Values);
        public bool Equals(Box? other) => other is not null && MorphirEquals(other);
        public override int GetHashCode() => MorphirHashCode();
    }

    [Test]
    public void DictionaryInsertRetainsTheNewPayloadBits()
    {
        var old = Dict<string, double>.Singleton("zero", +0d, MorphirComparers.String);
        var updated = old.Insert("zero", BitConverter.Int64BitsToDouble(long.MinValue));
        updated.Get("zero").Match(BitConverter.DoubleToInt64Bits, () => 1L).ShouldBe(long.MinValue);
        old.Get("zero").Match(BitConverter.DoubleToInt64Bits, () => 1L).ShouldBe(0L);
    }
}
