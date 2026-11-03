---
title: "C# SDK API"
weight: 20
---

All public runtime types live in `Morphir.SDK.CSharp`. The frozen inventory uses
logical Morphir names such as `List.map`; C# uses PascalCase instance methods
and generic factories. Its parameter/result meanings are unchanged.

| Morphir vocabulary | Public C# API |
| --- | --- |
| Maybe.Just, Nothing | `Maybe<T>.Just(value)`, `Maybe<T>.Nothing` |
| Maybe.map, andThen | `value.Map(callback)`, `value.AndThen(callback)` |
| Result.Ok, Err | `Result<TError,TValue>.Ok(value)`, `.Err(error)` |
| Result.map, andThen | `result.Map(callback)`, `result.AndThen(callback)` |
| Unit; Basics.LT, EQ, GT | `Unit.Value`; `Order.LT`, `.EQ`, `.GT`, also `Basics` constants |
| List literal, singleton, cons | `List<T>.FromEnumerable(values)`, `.Singleton(value)`, `list.Cons(head)` |
| List.map, append, head, tail, length | `list.Map(f)`, `.Append(right)`, `.Head()`, `.Tail()`, `.Length` |
| Dict.empty, singleton, fromList | `Dict<K,V>.Empty(comparer)`, `.Singleton(key,value,comparer)`, `.FromEnumerable(pairs,comparer)` |
| Dict.get, insert, update, remove, toList, union | `dict.Get(key)`, `.Insert(key,value)`, `.Update(key,f)`, `.Remove(key)`, `.ToList()`, `.Union(right)` |
| Set.empty, singleton, fromList | `Set<T>.Empty(comparer)`, `.Singleton(value,comparer)`, `.FromEnumerable(values,comparer)` |
| Set.insert, remove, toList, union | `set.Insert(value)`, `.Remove(value)`, `.ToList()`, `.Union(right)` |
| Basics scalar operations | `Basics.Add`, `Subtract`, `Multiply`, `Divide`, `IntegerDivide`, `ModBy`, `RemainderBy`, `Negate`, `Abs`, `Power`, `ToFloat`, `Round`, `Floor`, `Ceiling`, `Truncate`, `IsNaN`, `IsInfinite`, `Compare`, `LessThan`, `LessThanOrEqual`, `GreaterThan`, `GreaterThanOrEqual`, `Min`, `Max`, `Clamp`, `Equal`, `NotEqual` |
| Decimal operations | `Decimal.Add`, `Sub`, `Mul`, `Div`, `Compare`, `ToString` |

Use an alias such as `using SdkDecimal = Morphir.SDK.CSharp.Decimal;` when
`System.Decimal` is also imported. SDK `List<T>` may similarly need an alias
when System.Collections.Generic is imported outside the SDK namespace.

`Maybe<T>` is a readonly struct whose default is Nothing. `Result<E,V>` is a
sealed immutable class with private construction, error type first and success
type second. Neither uses null to encode a branch. `Match` observes either
variant without throwing for absence. Constructors reject null payloads.
Nothing and Err bypass map/bind callbacks. Called callback exceptions propagate
without translation to Nothing or Err.

```csharp
var text = Result<string, int>.Err("negative")
    .Match(error => error, value => value.ToString());
var absent = Maybe<int>.Nothing.Match(value => value, () => 0);
```

Collection factories copy enumerable inputs into immutable storage. Lists use
ImmutableList, dictionaries ImmutableSortedDictionary, and sets
ImmutableSortedSet. Public observations expose read-only enumeration and no
builder, mutable array or writable backing collection. All updates return new
immutable values. Dictionary construction retains the last duplicate value;
union retains the left dictionary's overlapping value. `Update` receives
Just current or Nothing and either inserts Just value or removes Nothing.

```csharp
using SdkList = Morphir.SDK.CSharp.List<int>;

var original = SdkList.FromEnumerable(new[] { 1, 2 });
var mapped = original.Map(x => Basics.Add(x, 1));
var keys = MorphirComparers.List(MorphirComparers.Int);
var dict = Dict<SdkList, Maybe<SdkList>>.Singleton(
    original, Maybe<SdkList>.Just(mapped), keys);
var equal = Dict<SdkList, Maybe<SdkList>>.Singleton(
    SdkList.FromEnumerable(new[] { 1, 2 }),
    Maybe<SdkList>.Just(SdkList.FromEnumerable(new[] { 2, 3 })), keys);
// dict.Equals(equal) and equal in-process hash codes.
```

Choose explicit `MorphirComparers.Int`, `.Float`, `.Char`, `.String`,
`.List(elementComparer)` or `.Tuple(...)` for 2/3-tuples. `Value` validates boxed
comparable values recursively and supports adapter-style heterogeneous object
storage; individual list/key types must still respect the frozen homogeneous
domain. There is no culture-sensitive CLR fallback. Bool, Decimal, Unit, Order,
Maybe, Result, Dict, Set and callbacks have no admitted ordering. Float ordered
keys must be finite. String ordering is ordinal UTF-16, without normalization.

SDK values implement structural Equals and consistent GetHashCode, including
nested variants, collections and tuples. Decimal scale and signed floating zero
do not distinguish equal hashes. NaN compares unequal, including inside SDK
variants. Hash numbers are not a serialized format or cross-process contract.
Generated records and variants opt in through the small `IMorphirValue` protocol.
Implement `MorphirEquals` by checking the exact case/type and comparing every
field with `Basics.Equal`. Implement `MorphirHashCode` with `MorphirValue.Hash`
over the same fields. For C# record/native equality, route the typed `Equals`
and `GetHashCode` overrides to those methods as well. This avoids CLR NaN
equality and works after boxing or inside tuples, variants and collections.

```csharp
sealed record Amount(double Number, Morphir.SDK.CSharp.List<int> Values)
    : IMorphirValue
{
    public bool MorphirEquals(IMorphirValue other) => other is Amount x
        && Basics.Equal(Number, x.Number) && Basics.Equal(Values, x.Values);
    public int MorphirHashCode() => MorphirValue.Hash((Number, Values));
    public bool Equals(Amount? other) => other is not null && MorphirEquals(other);
    public override int GetHashCode() => MorphirHashCode();
}
```

The protocol is an explicit caller contract. An interface cannot enforce
physical immutability or correct custom equality. Generated code and application
authors must keep payloads immutable and implement all fields consistently.
Ordinary IEquatable objects, arrays, object identity and delegates do not
automatically become Morphir values. `MorphirValue.Hash` is a .NET composition
helper, not an extension to the frozen logical operation inventory.

Integers are checked Int32. Overflow raises `OverflowException`; integer zero
division raises `DivideByZeroException`. `RemainderBy(divisor, dividend)` uses
the dividend sign; `ModBy` uses the divisor sign. `minInt % -1` is zero.
Float is IEEE binary64. Half rounding resolves toward positive infinity.
Float-to-Int conversion raises `NonFiniteConversionException` for NaN/infinity
and OverflowException for an out-of-range rounded value. Clamp tests low first,
then high, and preserves the selected value, including floating zero signs.

Decimal operates on the exact Decimal96 subset. Results remove redundant scale;
`Decimal.ToString` uses invariant positional notation without trailing zeros.
`Decimal.Div(a,0)` returns Nothing. Exact overflow raises OverflowException.
Nonterminating fractions, results requiring scale above 28, negative Int powers,
and unsupported ordering/equality domains raise NotSupportedException. They do
not silently round, wrap, clamp or acquire CLR reference semantics.
