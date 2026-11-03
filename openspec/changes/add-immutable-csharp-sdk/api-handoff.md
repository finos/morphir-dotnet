# C# runtime API handoff

This is the downstream contract for the additive `Morphir.SDK.CSharp` package.
The canonical behavioral authority remains `morphir-dotnet-sdk-v1`, schema 1,
profile `dotnet-sdk-v1`, with 67 operations and all 207 required IDs passing.
The eight representation exclusions remain unchanged and are never passes.

## Package and profiles

Namespace/package/assembly: `Morphir.SDK.CSharp`. Runtime target: net10.0.
Runtime dependencies: framework BCL and System.Collections.Immutable only.
The packed nuspec has an empty net10.0 dependency group. The library has no
FSharp.Core, Models, Tooling, CLI, compiler or Roslyn dependency. SDK-managed
ILLink.Tasks is private build-time analysis, not part of consumer closure.

One API serves numeric C#14.0 and C#15.0 consumers. Verified exact SDK/compiler
builds and commands are in [validation](validation.md) and the
[compatibility guide](../../../docs/content/docs/sdk/csharp/compatibility.md).
Neither native C# union syntax nor a source-generator adapter is required or
delivered. The runtime need not be loaded into a future Roslyn host.

## Construction and calls

Use C# PascalCase names and instance combinators for the logical Morphir names.
The complete admitted operation mapping and examples are in the
[API guide](../../../docs/content/docs/sdk/csharp/api.md).

| Runtime type | Construction and observations |
| --- | --- |
| `Maybe<T>` | readonly struct; default/Nothing is absent; `Just(T)` is present; `IsJust`; `Match(just,nothing)`; `Map` and `AndThen` |
| `Result<E,V>` | sealed immutable class; `Ok(V)` and `Err(E)`; error type first, success second; `IsOk`; `Match(err,ok)`; `Map` and `AndThen` |
| `Unit` / `Order` | `Unit.Value`/default; `Order.LT`, `EQ`, `GT`, also Basics constants |
| `List<T>` | `Empty`, `Singleton(T)`, `FromEnumerable(IEnumerable<T>)`; `Cons`, `Map`, `Append`, `Head`, `Tail`, `Length`; read-only indexing/enumeration |
| `Dict<K,V>` | `K : notnull`; `Empty(comparer)`, `Singleton(key,value,comparer)`, `FromEnumerable(IEnumerable<(K,V)>,comparer)`; `Get`, `Insert`, `Update`, `Remove`, `ToList`, `Union` |
| `Set<T>` | `Empty(comparer)`, `Singleton(value,comparer)`, `FromEnumerable(values,comparer)`; `Insert`, `Remove`, `ToList`, `Union` |

There is no null branch encoding. Constructors reject null payloads. Nothing and
Err bypass callbacks; called callback exceptions propagate. Head returns
Maybe<T>; Tail returns Maybe<List<T>>, including Just empty for a singleton.
Dict.ToList returns List<(K,V)> and Set.ToList returns List<T>, in ascending
Morphir order. FromEnumerable copies its input. No mutable backing value or
builder is public. Callers remain responsible for their own payload immutability.

Generated code chooses explicit MorphirComparers.Int/Float/Char/String and
composes List(elementComparer) or Tuple(fieldComparers) for 2/3-tuples.
MorphirComparer<T> cannot be constructed with a caller-defined CLR fallback.
Value validates boxed admitted comparable data recursively. Lists must be
homogeneous, including nested shapes; empty nested lists act as shape wildcards.
Bool, Decimal, Unit, Order, variants, records, Dict and Set have no admitted
ordering. Float keys must be finite; String order is ordinal UTF-16.

Dictionary list construction keeps the last duplicate value; Union keeps the
left dictionary's value. Update receives Just current or Nothing and inserts
Just value or removes Nothing. Insert retains the supplied replacement payload,
including a new floating zero sign. Old collections remain unchanged.

## Generated record and variant protocol

The emitter must generate explicit `IMorphirValue` implementations. Check the
exact type/case and compare every field with Basics.Equal. Hash exactly those
fields through MorphirValue.Hash. Route typed Equals/GetHashCode to the same
implementation so C# native and SDK equality agree. A complete example appears
in the API guide and `tests/Morphir.SDK.CSharp.Tests/consumer.cs.txt`.

```csharp
public bool MorphirEquals(IMorphirValue other) => other is MyCase x
    && Basics.Equal(Field1, x.Field1) && Basics.Equal(Field2, x.Field2);
public int MorphirHashCode() => MorphirValue.Hash((Field1, Field2));
public bool Equals(MyCase? other) => other is not null && MorphirEquals(other);
public override int GetHashCode() => MorphirHashCode();
```

This is a caller contract, not proof of physical immutability or correct custom
methods. Ordinary CLR IEquatable objects, arbitrary object identity, arrays and
delegates do not acquire SDK equality. No reflection/serialization discovery
exists. The explicit protocol survives boxing, tuples and SDK collections and
preserves recursive NaN inequality. Signed zeros compare equal and hash equally;
Decimal equality/hashes ignore authored scale. Hash numbers are process-local.

## Scalars and failure mapping

Int is checked System.Int32; Float is IEEE binary64; Decimal is the exact
System.Decimal subset. Integer overflow raises OverflowException; integer zero
division raises DivideByZeroException. Float-to-Int NaN/infinity raises the public
NonFiniteConversionException; out-of-range rounded values overflow. Map these
to frozen Overflow/DivisionByZero/NonFiniteConversion observations at boundaries.
Do not wrap, silently clamp or convert callback errors into variant branches.

Decimal.Div(a,0) returns Nothing. Arithmetic removes redundant scale and canonical
zero has no sign/scale. Nonterminating or scale-above-28 results fail explicitly
with NotSupportedException; overflow remains OverflowException. Unsupported
comparison/equality domains and negative Int exponents also fail explicitly.

## Verification and delivery ownership

All required IDs run through the public runtime adapter with operand snapshots,
callback errors and separately constructed equality/hash values. Expected outputs
come from the frozen corpus. Packaged ordinary consumers use the same source and
only the local SDK package, and representative trimmed/NativeAOT executables run
the same assertions. Native publication is verified on Windows arm64 only.

Runtime source remains the verified checkpoint
`0b01ff037f0496071e8ba4689c785376658a8f30`: 2,772 supported-suite tests, 15 cold
package checks, 40 package/build tests and 318/333 production lines covered,
95.4954954954955%. Later profile/handoff work changes verification/docs only.
The [validation record](validation.md) retains exact commands, toolchains,
failed tooling attempts, successful evidence, limitations and ownership.

PR readiness is a handoff milestone. The root coordinator owns check/review
assessment, landing, distinct landed-SHA verification, spec sync/archive and
cleanup of the retained clean worktree and owned temporary evidence. No child
watch, merge, branch deletion or release publication is authorized here.
