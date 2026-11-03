# SDK conformance fixtures

Contract `morphir-dotnet-sdk-v1`, schema version `1`, profile `dotnet-sdk-v1`.
The [contract](../../../docs/design/dotnet-sdk-contract.md) defines the selected
behavior and all scalar boundaries. The checked-in corpus has 67 operations and
177 cases. Both SDK adapters must consume these identities and expectations.

## Files and offline command

| File | Purpose |
| --- | --- |
| `inventory.json` | Ordered signatures, type constraints, scalar profile, callbacks, required case IDs, discrepancies and named F# gaps |
| `cases.json` | Tagged arguments, reference expectations, profile classification and persistence/equality/hash invariants |
| `provenance.json` | Immutable upstream revisions, original-byte SHA-256 hashes, license/notice links and source paths |
| `*.schema.json` | Closed JSON Schema 2020-12 shapes for these three documents |
| `evidence/` | Original pinned upstream source, documentation, tests, cached observations, licenses and notices |
| `author-corpus.py` | Optional maintainer authoring aid containing the reviewed literal cases; never invoked by validation |

From the repository root with the SDK in `global.json`:

```powershell
dotnet build tests/Morphir.SDK.Conformance.Tests/Morphir.SDK.Conformance.Tests.csproj -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet exec tests/Morphir.SDK.Conformance.Tests/bin/Debug/net10.0/Morphir.SDK.Conformance.Tests.dll --maximum-parallel-tests 2
```

After the build, the second command validates and tests offline. It performs no
network access and launches no Python, Elm, MoonBit or SDK process. Schema fetches
are forbidden. It checks source bytes/declarations, document shapes, type
arguments, callback identities/types, canonical projections, numeric/text limits,
ordered collections, coverage and known gaps. `dotnet test`'s default VSTest path
is unsuitable for the repository's .NET 10 MTP suites; `dotnet exec` is also the
Nuke runner's supported path. Nuke `Test` includes this suite sequentially.

## Case interface

Each case requires `caseId`, `operation`, `typeArguments`, `arguments`, `expected`,
`profile`, `classification`, `evidence`, `basis`, `knownGaps` and `invariants`.
`expected` contains exactly one of `value` or `error`. An error requires `code`
and nonempty `detail`; codes normalize host exceptions, not their English text.
`basis` distinguishes `source-derived` from an explicit `dotnet-extension`.

`classification` is `required` or `representation-excluded`. The latter requires
`exclusion.code` and a precise `exclusion.reason`, and the validator verifies
that the stated limit is demonstrated by the arguments or reference result.
An excluded reference expectation stays intact and is never counted as a pass.
`knownGaps` contains registered IDs only. A known gap is attached to a required
case, never an exclusion, skip or expected failure. Every required ID appears
in its operation's `requiredCaseIds`, and every inventory ID resolves back to
the correct required case. Unknown operations fail.

Types use `Int`, `Float`, `Decimal`, `String`, `Char`, `Bool`, `Unit`, `Order`,
`List<a>`, `Maybe<a>`, `Result<e,a>`, `Dict<k,v>`, `Set<a>`, `Tuple<a,b>` or
`Tuple<a,b,c>`. `Fn<a,b>` appears only in callback parameters. Type-variable
bindings must match the signature exactly. `Result` always puts the error first.

| Tag | Fields after `tag` | Projection |
| --- | --- | --- |
| `int` | `value`: canonical base-10 integer string | Int32 in the admitted profile |
| `float` | `value`: finite text or `NaN`, `+Infinity`, `-Infinity`, `+0`, `-0` | Binary64; zero sign retained, NaN payload ignored |
| `decimal` | `coefficient`: signed integer text, `scale`: integer | Exact coefficient times 10 to negative scale |
| `string`, `char` | `value`: text | Exact, without normalization |
| `bool` | `value`: JSON boolean | Boolean |
| `order` | `value`: `LT`, `EQ` or `GT` | Logical order constructor |
| `unit`, `nothing` | none | Nullary value |
| `just`, `ok`, `err` | `value`: recursively tagged payload | Constructor plus payload |
| `list`, `tuple` | `items`: tagged array | Order preserved; tuple arity 2 or 3 |
| `dict` | `entries`: array of `[taggedKey,taggedValue]` | Unique keys, strictly ascending |
| `set` | `items`: tagged array | Unique elements, strictly ascending |
| `callback` | `id`: registered callback | Typed adapter-local function, never source evaluation |

An empty list/Maybe carries its element type in `typeArguments`, not a fabricated
payload. Decimal canonical results remove redundant scale; authored input scale
may remain. Float finite text is parsed invariantly and compared by binary64
value. Adapters render signed zeros and exceptional values explicitly. Dict/Set
inputs are canonical values; unordered construction inputs use `List<Tuple<...>>`
or `List<...>` instead. Sorting malformed canonical inputs in the adapter would
hide a fixture error.

All value operands must remain observable and unchanged. `preserveArguments`
lists indexes where adapters must explicitly compare retained before/after values.
`equalTo` means independently construct the tagged equivalent and compare it to
the actual result. `hashEqualTo` additionally compares their in-process hashes.
These checks make insertion history/allocation irrelevant without prescribing a
numeric hash implementation. Use structural SDK equality rather than BCL
collection reference equality.

## Extension example

To add `Maybe.withDefault`, first read the pinned definition and signature. Add
an inventory operation with parameters `fallback:a`, `value:Maybe<a>`, result
`a`, type parameter `a:value`, exact source binding, evidence and required IDs.
Add both a `Just` and a `Nothing` case, for example:

```json
{
  "caseId": "Maybe.withDefault/nothing",
  "operation": "Morphir.SDK.Maybe.withDefault",
  "typeArguments": { "a": "Int" },
  "arguments": [ { "tag": "int", "value": "7" }, { "tag": "nothing" } ],
  "expected": { "value": { "tag": "int", "value": "7" } },
  "profile": "dotnet-sdk-v1",
  "classification": "required",
  "evidence": [ "python:packages/morphir/src/morphir/sdk/maybe.py" ],
  "basis": "source-derived",
  "knownGaps": [],
  "invariants": {}
}
```

This example is not yet admitted; inserting it alone must fail as an unknown
operation. Update source evidence and attribution if needed, record discrepancies
or representation exclusions, review literal expectations independently, update
the authoring aid and coverage list, then run schema/fixture/regression checks.
Document changes and update the readiness manifest. Incompatible type, tag,
profile or error semantics require a new contract/schema version; do not mutate
the meaning of an existing case ID to accommodate an implementation.

## Runtime handoff checklist

- Read the contract, inventory, profile, callbacks, provenance and discrepancies
  at the coordinator-verified landed commit on main.
- Consume all required IDs for the bounded inventory and the same expectations.
  Preserve inputs, canonical traversal and nested structural equality/hashing.
- Map the logical error codes and Result payload meanings explicitly. Implement
  the closed callback vocabulary locally with the exact declared types.
- Report required/executed/passed case-ID sets, separately from exclusions and
  known gaps. No skips or expected failures satisfy required coverage.
- The F# worker resolves every named covered gap, preserves API/package
  compatibility and tests generated consumers. The C# worker tests the immutable
  public API and packaged consumers. This validator does not certify either.
- If evidence contradicts a case, report the case ID, pinned declaration and
  proposed correction to the coordinator. Land a reviewed contract correction;
  do not rewrite expectations in an adapter or silently omit the operation.
