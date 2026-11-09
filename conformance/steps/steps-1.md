# MCK seed step vocabulary 1.1

This is the closed vocabulary for the F# frontend seed. The kit and report
version is `0.1.0-draft.1`, independent of IR, CLI and MEP versions.
`Morphir.Conformance.Corpus.read` reads vocabularies 1.0 and 1.1, and refuses
other versions. Version 1.1 adds the call step. Version 1.0 refuses it. The
report records the vocabulary version of the case set.

Features live under `suites/frontend-fsharp/`, declare
`@suite:frontend-fsharp`, and use stable `frontend-fsharp-NNNN` scenario ids.
The runner selects the implementation. Adapter, executable and implementation
selection tags are refused. Features must not name an adapter.

The supported tags are `@suite:frontend-fsharp`, `@semantic`, `@refusal`,
`@basis:hand-authored`, `@basis:spec-derived` and `@excluded`.
`@excluded` is valid only on Examples. Unknown eligibility metadata, including
known-gap tags, is refused until that behavior is implemented by a later
vocabulary version.

| Step text | Argument and meaning |
| --- | --- |
| `the F# package "<package>" exposing "<modules>"` | First step; declares the package and its exposed module. The seed takes one module name. |
| `the source "<uri>":` | A `fsharp` docstring containing the complete source document. URIs are unique portable root-relative paths. Step order is source order. |
| `it is compiled from <input> with IR version <version>` | Exactly one compile step. Both placeholders are supplied by the outline's Examples rows. Input is `files`, `directory` or `project file`; version keeps its exact spelling. |
| `compilation succeeds with no error diagnostics` | Exactly one outcome step. Success must be true, a JSON model must be present, and no error diagnostic is allowed. Non-error diagnostics are allowed by this step. |
| `compilation fails with diagnostics:` | Exactly one outcome step. A `json` docstring holds the expected diagnostic array in the MEP shape. Success must be false, no model may be returned, and diagnostics must match severity, code, URI and range exactly, in document/position order. Messages are not compared. No extra diagnostic is allowed. |
| `the IR equals "<path>"` | Adds a complete expected-IR sidecar, relative to the feature. The path may contain `<version>` and `<input>`. Comparison ignores JSON object-member order; arrays, strings, scalar kinds and number spellings remain exact. No other normalization is applied. |
| `calling "<value>" with <arguments> returns <result>` | Version 1.1. Adds a call observation, for the behavioral verdict. `<value>` is a module name and a value name, such as `Pricing.seats` or `Rates.Daily.rate`. `<arguments>` is a JSON array with one element for each parameter (`[]` for a value without parameters). `<result>` is the expected JSON result. Both use the value encoding below. A case can have more than one call. Calls need the successful outcome step. |

The Given/When/Then/And keywords are Gherkin syntax, not part of the step text.
Other steps or argument shapes fail linting. The seed supports scenarios directly
under a feature, not Background or Rule inheritance. Generalization is tracked
in the separate `establish-morphir-compatibility-kit` change.

Every scenario explicitly lists the input × version matrix supplied to the
reader. Examples have `input` and `version` columns. An `@excluded` Examples
block also needs a non-empty `reason` for each row. Missing combinations,
duplicate combinations and reused case ids are errors. Exclusions remain in the
report and never invoke the adapter.

## Behavioral verdict

A runner gets the behavioral verdict from a call checker. The checker gets the
compile input, the model JSON and the calls of one case variant, and returns the
first difference. It runs only when the structural verdict passes. A case with
calls fails when the run has no call checker, because its behavior was not
checked. The report names the checker. The .NET checker generates F# from the
model with the F# backend, and runs each call in FSI.

## Value encoding

The encoding is directed by the type of the parameter or result. It is the kit's
own encoding. morphir-elm has a different one (`DataCodec`), which no Morphir
specification defines.

| Morphir type | JSON |
| --- | --- |
| `Bool` | `true` or `false` |
| `Int` | an integer number |
| `Float` | a number |
| `Decimal` | a string with the decimal number, such as `"10.50"` |
| `String` | a string |
| `Char` | a string with one Unicode scalar value |
| `Unit` | `[]` |
| `List a` | an array |
| tuple | an array with one element for each item |
| record | an object with one member for each field. The member name is the camelCase field name, such as `"plate"` |
| custom type | externally tagged by the TitleCase constructor name: `"Small"` without arguments, `{"Large": 4}` with one argument, `{"Pair": [1, "x"]}` with more than one |
| `Maybe a` | `"Nothing"` or `{"Just": value}` (the custom type rule) |
| `Result e a` | `{"Ok": value}` or `{"Err": error}` (the custom type rule) |

Equal values compare by value, not by JSON spelling: `"1.10"` and `"1.1"` are
the same `Decimal`. `Dict`, `Set` and function values have no encoding in this
version.

## Diagnostics

Positions are zero-based UTF-16 code units with exclusive ends. The diagnostic
shape is:

```json
{
  "severity": "error",
  "code": "fsharp.unsupported_mutable",
  "location": {
    "uri": "Pricing.fs",
    "range": {
      "start": {"line": 2, "character": 8},
      "end": {"line": 2, "character": 15}
    }
  }
}
```

Reports keep requests, observations, verdicts and the first comparison difference
in plan order. They contain no timestamps. Their case-set SHA-256 includes all
captured artifact bytes, including vocabulary and sidecars, sorted by ordinal
portable path. Each file contributes its UTF-8 path byte length in invariant
decimal followed by a colon, path bytes, content length in the same framing,
then content bytes.

Model observations are stored as exact `modelJson` text, including malformed
payloads, so a failing implementation cannot prevent report serialization.
