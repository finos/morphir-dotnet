# MCK seed step vocabulary 1.0

This is the closed vocabulary for the F# frontend seed. The kit and report
version is `0.1.0-draft.1`, independent of IR, CLI and MEP versions.
`Morphir.Conformance.Corpus.read` refuses other vocabulary versions.

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

The Given/When/Then/And keywords are Gherkin syntax, not part of the step text.
Other steps or argument shapes fail linting. The seed supports scenarios directly
under a feature, not Background or Rule inheritance. Generalization is tracked
in the separate `establish-morphir-compatibility-kit` change.

Every scenario explicitly lists the input × version matrix supplied to the
reader. Examples have `input` and `version` columns. An `@excluded` Examples
block also needs a non-empty `reason` for each row. Missing combinations,
duplicate combinations and reused case ids are errors. Exclusions remain in the
report and never invoke the adapter.

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
