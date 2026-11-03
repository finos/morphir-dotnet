using Shouldly;

namespace Morphir.SDK.CSharp.Tests;

public sealed class ValueDomainTests
{
    [Test]
    public void ExplicitGeneratedValuesRetainSemanticsAfterBoxingAndNesting()
    {
        var a = new GeneratedValue(7d, List<Maybe<int>>.Singleton(Maybe<int>.Just(1)));
        var b = new GeneratedValue(7d, List<Maybe<int>>.Singleton(Maybe<int>.Just(1)));
        Basics.Equal(a, b).ShouldBeTrue();
        Basics.Equal<object>(a, b).ShouldBeTrue();
        Basics.Equal((a, 1), (b, 1)).ShouldBeTrue();
        MorphirValue.Hash((a, 1)).ShouldBe(MorphirValue.Hash((b, 1)));
        List<object>.Singleton(a).ShouldBe(List<object>.Singleton(b));
        Maybe<object>.Just(a).ShouldBe(Maybe<object>.Just(b));
        Result<string, object>.Ok(a).ShouldBe(Result<string, object>.Ok(b));
        var nan = a with { Number = double.NaN };
        Basics.Equal(nan, nan with { }).ShouldBeFalse();
        Basics.Equal<object>(nan, nan with { }).ShouldBeFalse();
        Basics.Equal((nan, 1), (nan with { }, 1)).ShouldBeFalse();
    }

    [Test]
    public void ArbitraryEquatableTypesDoNotBecomeMorphirValues()
    {
        Should.Throw<NotSupportedException>(() => Basics.Equal(new PlainRecord(1), new PlainRecord(1)));
        Should.Throw<NotSupportedException>(() => Basics.Equal(new MutableEquatable(), new MutableEquatable()));
        Should.Throw<NotSupportedException>(() => MorphirValue.Hash(new MutableEquatable()));
    }

    [Test]
    public void BoxedCollectionKeysRejectInconsistentElementTypes()
    {
        Should.Throw<NotSupportedException>(() => Set<object>.Singleton(List<object>.FromEnumerable([1, "x"]), MorphirComparers.Value));
        var mixed = List<object>.FromEnumerable([List<object>.Singleton(1), List<object>.Singleton("x")]);
        Should.Throw<NotSupportedException>(() => Dict<object, int>.Singleton(mixed, 1, MorphirComparers.Value));
        var valid = List<object>.FromEnumerable([List<object>.Empty, List<object>.Singleton(1)]);
        Set<object>.Singleton(valid, MorphirComparers.Value).Count.ShouldBe(1);
        var concealed = List<object>.FromEnumerable([List<object>.Empty, List<object>.Singleton(1), List<object>.Singleton("x")]);
        Should.Throw<NotSupportedException>(() => Set<object>.Singleton(concealed, MorphirComparers.Value));
        var tuples = List<object>.FromEnumerable([(1, "a"), (2, "b")]);
        Set<object>.Singleton(tuples, MorphirComparers.Value).Count.ShouldBe(1);
        Should.Throw<NotSupportedException>(() => Set<object>.Singleton(List<object>.FromEnumerable([(1, "a"), (2, 'b')]), MorphirComparers.Value));
    }

    [Test]
    public void ExplicitComparersPreserveEveryAdmittedCompoundField()
    {
        MorphirComparers.Float.Compare(-0d, 0d).ShouldBe(0);
        MorphirComparers.Char.Compare('a', 'b').ShouldBeLessThan(0);
        var triple = MorphirComparers.Tuple(MorphirComparers.Int, MorphirComparers.String, MorphirComparers.Char);
        triple.Compare((1, "a", 'a'), (2, "a", 'a')).ShouldBeLessThan(0);
        triple.Compare((1, "a", 'a'), (1, "b", 'a')).ShouldBeLessThan(0);
        triple.Compare((1, "a", 'a'), (1, "a", 'b')).ShouldBeLessThan(0);
        Should.Throw<NotSupportedException>(() => MorphirComparers.Float.Compare(double.PositiveInfinity, 1d));
        Should.Throw<NotSupportedException>(() => MorphirComparers.Char.Compare('\uD800', 'a'));
    }

    [Test]
    public void AdapterPreservesAuthoredDecimalBitsAndCanonicalArithmetic()
    {
        var authored = System.Text.Json.Nodes.JsonNode.Parse("{\"tag\":\"decimal\",\"coefficient\":\"100\",\"scale\":2}")!;
        var parsed = (decimal)RuntimeAdapter.Parse(authored);
        ((decimal.GetBits(parsed)[3] >> 16) & 255).ShouldBe(2);
        Decimal.Add(parsed, 0m).ShouldBe(1m);
        ((decimal.GetBits(Decimal.Add(parsed, 0m))[3] >> 16) & 255).ShouldBe(0);
        decimal.GetBits(Decimal.Mul(-0.00m, 10.0m))[3].ShouldBe(0);
        MorphirValue.Hash(parsed).ShouldBe(MorphirValue.Hash(1m));
    }

    private sealed record PlainRecord(int Number);
    private sealed class MutableEquatable : IEquatable<MutableEquatable>
    {
        public bool Equals(MutableEquatable? other) => true;
    }

    private sealed record GeneratedValue(double Number, List<Maybe<int>> Items) : IMorphirValue
    {
        public bool MorphirEquals(IMorphirValue other) => other is GeneratedValue value && Basics.Equal(Number, value.Number) && Basics.Equal(Items, value.Items);
        public int MorphirHashCode() => MorphirValue.Hash((Number, Items));
        public bool Equals(GeneratedValue? other) => other is not null && MorphirEquals(other);
        public override int GetHashCode() => MorphirHashCode();
    }
}
