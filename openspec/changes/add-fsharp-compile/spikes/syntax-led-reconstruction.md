# Syntax-led match reconstruction proof, 2026-11-07

Status: revised task 3.1 passes. The maintainer authorized replacing the exact
match-range join while preserving the supported patterns, source order and native
IR builders. [FSF-2](../design.html#fsf-2), the frontend spec, tasks now record that replacement. This is a design proof, not a completed frontend.

## Reproduction

```console
dotnet run --project spikes/FSharpSyntaxLowering -- --sequenced
dotnet fantomas --check spikes/FSharpSyntaxLowering/CheckedSource.fs spikes/FSharpSyntaxLowering/SyntaxMatchEvidence.fs spikes/FSharpSyntaxLowering/SyntaxLoweringTests.fs spikes/FSharpSyntaxLowering/OriginalReplayTests.fs spikes/FSharpSyntaxLowering/Program.fs
```

Result: 34/34 Expecto tests pass on SDK 10.0.403. The standalone project is outside
the solution and normal test target. It uses FCS 43.12.403 with its exact Core
10.1.403 dependency through local package overrides and a checked-in lock file.
Central package pins remain unchanged. Files live in disposable TestSandbox
folders, with no child processes or Morphir configuration access.

## What changed

The [old proof](match-range-join.md) still reproduces the disproved invariant.
This proof enumerates original `SynExpr.Match` and `SynExpr.MatchLambda` nodes,
including matches erased from the typed tree. Original syntax determines clause
order, pattern nesting and full clause-body ranges. Types and symbol identity
come from FCS, with no handwritten F# type inference.

The analysis-only copy adds fresh names. Conceptually, each original clause:

```fsharp
| Some n -> originalBody
```

becomes:

```fsharp
| (Some n as freshSubject) ->
    match originalBody with
    | freshResult -> freshResult
```

The real insertion transform uses parentheses and preserves original multiline
indentation. It does not replace original tokens. A UTF-16 insertion map locates
original symbols in the analysis copy. Fresh-name selection avoids every prefix
already present in the source. The checked subject aliases agree across clauses,
as do the checked result bindings. Missing or ambiguous evidence is rejected.

`FSharpType.Instantiate` specializes generic case fields using the checked subject
arguments. Checked union field count distinguishes `of int * string` from
`of (int * string)`. The original syntax provides the corresponding separate
patterns or single tuple pattern. No decision-tree target is used.

Only original nodes and ranges are reconstructed. The generated bindings never
become model nodes. The builder interface and native classic/v4 implementations
remain the planned destination of the single production lowering.

## Red/green evidence

- A metadata-only reconstruction handled the eleven original probes but failed
  a computed subject, `match (Some x) with ...`, because there was no reliable
  surviving subject expression to query. Fresh subject aliases exposed that type.
- An identity-call result wrapper passed the first 25 hardening cases but changed
  value generalization for a valid original program:

  ```fsharp
  let identity = match () with | () -> fun value -> value
  let number = identity 42
  let word = identity "hello"
  ```

  The original checks; the identity-call analysis copy fails at the string use.
  A nonexpansive match binding preserves the generic function and passes. Signature
  preservation is checked rather than assumed.
- A same-offset nested-body test caught result witnesses closing in the wrong
  order. Sorting co-terminal closing edits from the innermost body outward fixed
  the regression without losing original coordinates.
- A multi-field constructor test failed when source tuple syntax was treated as
  one constructor field. Using the checked case arity fixed it, while the genuine
  tuple-valued field test still preserves a single argument.

## Passing coverage

All eleven original source fixtures are replayed with independently written type
and pattern expectations: wildcard, variable, literal, tuple, constructor,
empty-list, cons, `as`, unit, match-lambda and nested matches. They all pass.
The earlier exact join passes only one of those eleven.

Twenty-three further tests cover:

- explicit subject/pattern/result type assertions, clause order and nested body
  spans for the core patterns;
- a shadowed pattern binding and specialization of a generic constructor field;
- computed and fully reduced empty-list subjects;
- polymorphic match-lambdas and polymorphic values returned by unit matches;
- multi-field versus tuple-valued constructor fields;
- fresh-name collisions, CRLF, and a non-BMP character on the same line before a
  queried symbol, with UTF-16 columns checked against the original source;
- refusal of missing witnesses, duplicate witnesses, changed binding signatures
  and missing mapped source symbols. These tests remove or duplicate real FCS
  evidence after checking; the reconstruction must fail instead of guessing.

## Validation boundary

The prototype reconstructs typed match evidence and source pattern structure.
It does not emit IR, validate builders or run MCK cases. It does not prove every
supported expression, function/let parameter pattern or multi-document case.
Those requirements remain unchanged in tasks 3.2 and 3.6.

Its declaration-signature comparison normalizes generic parameter names but is
not a complete equivalence checker for local binding generalization, generic
constraints or every resolved external symbol. Production must verify these too,
and must cover any additional expression-witness transform through its API.
The proof compares mapped locations within a single file. Production keys also
include original document URI and node identity. Generated source must be bounded,
cancellation-aware and request-local; production diagnostics must use original
URIs and never repeat generated warnings or reveal temporary paths.

The reflection AST walker and exception failures are confined to the spike.
Production uses explicit syntax visitors and `Checked` diagnostics. The existing
refusal code `fsharp.unsupported_match_shape` covers unavailable or changed
checked match evidence. It is not permission to narrow the supported pattern set.

Production FCS/Core compatibility remains unresolved by these local overrides.
The repository's 34.1.1/11.0.100 pairing is not the tested pair. Task 3.2 owns that
integration and reruns this proof with the selected compiler baseline.
