# Implementation continuation notes

This is a working note, not additional acceptance or a scope reduction. The
existing 13 tasks and specification remain authoritative. Current public API is
in Types.fs; production depends on Models and FSharp.Core, with no Roslyn or I/O.

## Current lowering

TypeEmission has independent typed classic/v4 type walkers, transparent alias
expansion, a private Shape/Declaration catalog, deterministic record allocation,
and immutable record/closed case text emission. `prepare` returns catalog and
module text. Public fields use the exact SDK equality/hash protocol.

ValueLowering has independent typed value walkers. Inference uses private fresh
type variables, unification and rigid declared generic parameters. Every top-level
definition is checked before emission. Static methods represent top-level values;
typed nested Func adapters represent function values and partial application.
Fresh lexical target names preserve capture and shadowing. Nonrecursive lets use
typed immediately invoked functions and bind only after their initializer.

LiteralEmission validates bounded numeric/UTF-16 representations and emits explicit
escapes. Source module classes have a Module suffix; path components have safe
prefixes. Unknown semantics and unresolvable target identities produce located
diagnostics with no returned partial sources.

## Pattern and record corpus

The original two test-only drafts are preserved under the owned evidence root
`C:/Temp/morphir-csharp-generator-1d28e9ab-20261103`:

- `FunctionCases.patterns.pending.fs` extends the shared test AST and its two
  independent typed input builders. It is not production code or registered tests.
- `PatternCases.pending.fs` supplies ordered matches, custom constructors, tuples,
  recursive list sum, record fields/updates, anonymous closed record signatures,
  field functions, conditionals and unit. Expected business/ordering observations
  are authored separately in its C# consumer.

After the allocation checkpoint, registered versions of these drafts produced
two actual RED failures in `patterns-red.log`. The implemented shared corpus now
executes under both numeric compilers. Classic record maps remain in key order;
a separate v4 test verifies reversed authored field/update evaluation order.

## Record and pattern implementation

Value checking precedes emission. Resolved closed record shapes from signatures
and expressions receive deterministic synthetic declarations before type/value
emission. Existing nominal cases and transparent aliases retain their identities.
Structural record fields match by name. Field values evaluate in source order
before constructor arguments follow the selected record representation's order.

Typed patterns emit ordered guards followed by fresh bindings. Constructor
payloads retain their declared order. Updates evaluate the target once, then
updates once, then construct a new immutable value. Duplicate binders and
inconsistent branch types fail generation. Compiled recursive list sum and
explicit local-recursion refusals exercise both families. Non-exhaustive patterns
retain a runtime pattern-failure exception, rather than an implementation stub.
SDK constructors now bind the real Maybe/Result factories and Order cases.
Patterns use the public Match/IsJust/IsOk API; no private runtime representation
or null branch encoding is introduced. Constructor value references are admitted.

Cancellation now reaches alias expansion, substitution, shape matching, public
type validation, name allocation, SDK comparer recursion, typed value traversal
and text emission. A final token check precedes the successful whole-model result.
Active alias expansion and million-character strings in both families return
Cancelled with no sources. Business fixtures, trimming, whole-suite coverage and
final handoff remain open.

## SDK bindings and checked domains

SdkMapping holds typed operation signatures and deterministic text bindings for
the 67 frozen operations. Number, comparable and equality constraints validate
after closed-record fields resolve. Collection factories compose the actual
typed SDK comparers. Result remains error first throughout signatures, factories,
callbacks and pattern extraction. The production dependency closure is unchanged.

FrozenSdkCases embeds the original inventory/cases unchanged and builds direct
typed classic/v4 wrapper functions. Its C# observations check all 207 required
IDs, exact inventory membership, errors, input fingerprints and equality/hash
invariants. Eight representation exclusions remain separate. Ordinary Decimal
observations compare values across scale differences; exact arithmetic also
checks canonical bits. Reducible large coefficient/scale inputs reduce exactly
before constructing Decimal96, with no truncation or rounding.

Public function signatures now receive the same private-nominal exposure check
as public type payloads. Repeated generic record bindings compare nested field
sets independently of declaration order. Value evaluation order is retained.
