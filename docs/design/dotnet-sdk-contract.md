# Shared .NET SDK contract

Contract ID `morphir-dotnet-sdk-v1`, fixture schema version `1`, profile
`dotnet-sdk-v1`. [Issue #460](https://github.com/finos/morphir-dotnet/issues/460)
tracks this contract. The [fixture README](../../tests/Fixtures/MorphirSDK/README.md)
is the adapter interface; [inventory.json](../../tests/Fixtures/MorphirSDK/inventory.json)
contains the exact signatures and required case IDs. These files are prerequisites
for both SDK initiatives. They do not implement or certify either runtime.

## Admitted operations

The initial inventory contains 67 qualified operations. Each operation has ordered,
named parameters, type-variable constraints, a result type, a profile, pinned
evidence and an exact source declaration. Operation names are logical Morphir SDK
names, independent of C#/F# method casing or currying. A nullary constructor is an
operation with no arguments. Lists also have literal construction through the
`list` tag; there is no invented Morphir `List.fromList` operation.

| Module | Admitted names |
| --- | --- |
| Basics | add, subtract, multiply, divide, integerDivide, modBy, remainderBy, negate, abs, power, toFloat, round, floor, ceiling, truncate, isNaN, isInfinite, compare, lessThan, lessThanOrEqual, greaterThan, greaterThanOrEqual, min, max, clamp, equal, notEqual, LT, EQ, GT |
| List | singleton, cons, map, append, head, tail, length |
| Maybe | Just, Nothing, map, andThen |
| Result | Ok, Err, map, andThen |
| Dict | empty, singleton, fromList, get, insert, update, remove, toList, union |
| Set | empty, singleton, fromList, insert, remove, toList, union |
| Decimal | add, sub, mul, div, compare, toString |

For example, `Result.map` is `Fn<a,b> -> Result<e,a> -> Result<e,b>`.
`Result<e,a>` always means error type first, success type second. F#'s
`Result<a,e>` must map those meanings explicitly. `Dict.update` is
`k -> Fn<Maybe<v>,Maybe<v>> -> Dict<k,v> -> Dict<k,v>`, with `k` comparable.
`Basics.divide` accepts Float only. Integer division is `Basics.integerDivide`.
`Decimal.div` returns `Maybe<Decimal>`, with `Nothing` for a zero divisor.

The inventory is a bound on this delivery, not a claim about all SDK modules.
Dates/times, transcendental functions and the unlisted collection, parsing and
conversion operations require a reviewed extension. Validators reject unknown
operations rather than silently ignoring them.

## Scalars and errors

Int uses signed `System.Int32`, from `-2147483648` to `2147483647`. Arithmetic
computes the exact mathematical result before checking that range. It must not
wrap. Overflow maps to the observation error `Overflow`, irrespective of the
host exception type. Division truncates toward zero. Integer zero divisors map
to `DivisionByZero` by an explicitly selected .NET extension. Python/Elm
`integerDivide(7,0)` returns `0`; the admitted extension follows MoonBit's exact
integer SDK and is not a claim of Elm parity. `minInt / -1` overflows, while
`remainderBy(-1,minInt)` and `modBy(-1,minInt)` return `0`. Remainder has dividend
sign, modulus divisor sign. Integer power requires a nonnegative exponent and
checks its exact result. Negative exponents have retained, excluded reference
observations because their fractional results do not inhabit Int.

Float uses IEEE 754 binary64 `System.Double`. JSON stores decimal round-trip
text for finite values and explicit `NaN`, `+Infinity`, `-Infinity`, `+0`, `-0`
symbols. An adapter compares parsed finite binary64 values exactly, including
the smallest subnormal. It compares zero signs by bits and NaN by classification,
without requiring a payload. Division by floating zero produces infinity or NaN.
Overflow produces infinity and underflow preserves zero sign. `round` resolves
halves toward positive infinity, so `round(-2.5) = -2`. Float-to-Int rounding
operations report `NonFiniteConversion` for NaN/infinity and `Overflow` if the
rounded result lies outside Int32. They never silently clamp.

Decimal uses the exact subset representable by `System.Decimal`: signed
coefficient magnitude at most `79228162514264337593543950335`, scale `0..28`.
The observation is `coefficient * 10^-scale`. Inputs may retain authored scale;
canonical results remove trailing coefficient zeros while scale is positive,
and zero has coefficient `0`, scale `0`. Numeric equality and equal hashes ignore
scale. `toString` uses invariant positional notation with no fractional trailing
zeros. No exponent, group separator or culture is involved.

An exact add/sub/mul result beyond Decimal96 magnitude reports `Overflow`.
A result needing a nonzero digit below scale 28 is a representation exclusion;
rounding that digit away cannot pass. Nonterminating division is also excluded.
The retained Python `1/3` observation has 50 significant digits; MoonBit's
classic API truncates at exponent -32. Neither observation becomes .NET output.
`Decimal.div(1,0)` still requires `Nothing`; it is not an exception outcome.
Decimal parsing, Float conversion and rounding are outside this inventory.

String accepts well-formed UTF-16, including supplementary characters. Equality
uses exact text with no normalization. Ordering is ordinal UTF-16 code-unit
order, as in Elm. Python instead compares Unicode scalar values, so
`"\U00010000" < "\uE000"` is an explicit selected disagreement. Char accepts one
non-surrogate BMP scalar in `System.Char`. Supplementary Char is excluded, while
the same scalar in String is admitted. Unpaired surrogates are outside the
well-formed fixture text domain. No casing, collation or culture defaults apply.

## Collections, comparison and value semantics

Comparable types are Int, finite Float, Char and String, plus recursively
comparable homogeneous lists and 2/3-tuples. Lists and tuples compare
lexicographically, with a shorter equal prefix first. Float signed zeros compare
equal. NaN/infinity are admitted numeric observations but excluded as ordered
keys. Bool, Unit, Order, Decimal, Maybe, Result, Dict, Set and function values do
not acquire a comparator from the CLR. Decimal has its separate compare API.

Dict and Set project in strictly ascending key/element order, with no duplicates.
Dict construction from a list keeps the last value for a repeated key.
`Dict.union(left,right)` keeps the first dictionary's value for overlap.
Dict update receives `Just current` or `Nothing`, then inserts/replaces `Just`
or removes `Nothing`. Removing an absent entry changes no observable value.
Set construction/union deduplicates by the admitted comparison.

Values are immutable. The adapter retains the original operands while producing
new values, and verifies every `preserveArguments` snapshot after the call.
List/Dict/Set equality and hashing are structural and independent of allocation
and insertion history. `equalTo` and `hashEqualTo` invariants require separately
constructed equivalent values to compare equal and have equal in-process hashes.
No cross-process numeric hash code is prescribed. Nested Maybe/Result and
collection payloads retain these semantics. NaN remains unequal to itself in
`Basics.equal`, while signed zero is equal. Callbacks are a closed, typed fixture
vocabulary, not arbitrary source code or general comparable/equatable values.

## Evidence and readiness

[PROVENANCE.md](../../tests/Fixtures/MorphirSDK/PROVENANCE.md) identifies all four
upstream revisions, full license/notice files, the historical Elm oracle's
different source revision and the distinction between source-derived cases and
.NET extensions. The validator checks SHA-256 bytes and exact declarations.
The expectations were curated from that evidence, not emitted by either .NET SDK.

Named gaps `FS-DICT-UNION`, `FS-INT-CHECKED`, `FS-DECIMAL-DIV` and
`FS-DECIMAL-TEXT` belong to `introduce-fsharp-sdk-package`. They remain required
cases. This change validates their classification and provenance; it does not
fix the existing F# runtime or mark any gap as passing. The F# worker must execute
and resolve every covered mismatch. It must also map canonical names and Result
generic meanings to the preserved F# API without breaking existing consumers.

The C# and F# workers consume the same case IDs and expected observations, create
their own adapters and report every required ID as executed and passing. An
exclusion is reported separately with its precise reason. A skip, known gap or
unclassified case cannot increase the passing count. The coordinator may dispatch
these workers only after this contract PR is merged into main and its actual
landed SHA is verified. The open implementation PR is not that gate.
