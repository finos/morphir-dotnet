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

## Next test corpus

Two test-only drafts are preserved under the owned evidence root
`C:/Temp/morphir-csharp-generator-1d28e9ab-20261103`:

- `FunctionCases.patterns.pending.fs` extends the shared test AST and its two
  independent typed input builders. It is not production code or registered tests.
- `PatternCases.pending.fs` supplies ordered matches, custom constructors, tuples,
  recursive list sum, record fields/updates, anonymous closed record signatures,
  field functions, conditionals and unit. Expected business/ordering observations
  are authored separately in its C# consumer.

After this allocation boundary is committed, register these tests and observe
actual RED behavior before production changes. Both drafts still require compiler
verification. Keep classic record maps in key order and test v4's distinct authored
field order separately, so the shared equivalent corpus has equivalent inputs.

## Planned record/pattern implementation

Split value checking from emission. Collect resolved closed record shapes from
signatures and checked expressions, allocate deterministic synthetic declarations,
then emit types and values using the completed catalog. Preserve existing nominal
custom cases and transparent record aliases. Match structural record fields by
name; evaluate value/update expressions in their input order before ordering
constructor arguments for the selected record representation.

Add typed patterns and ordered first-match emission. Constructor payloads retain
their declared order and Result remains error first. Record updates evaluate the
target once, then updates once in source order, then construct a new immutable
value. Validate duplicate binders, constructor arity and branch types. Add actual
top-level self recursion and explicit local-recursion failures. Do not replace
unsupported bodies or branches with runtime implementation stubs.

Task 3.4 still needs cancellation through expansion/allocation/emission inner
loops, a final whole-model boundary check, and a real nonempty traversal test.
SDK operations beyond the current add slice, business fixtures, frozen compatible
conformance cases, trimming, whole-suite coverage and final handoff remain open.
