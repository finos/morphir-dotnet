---
title: "C# generation API and diagnostics"
---

`CSharpGenerator.Generate(VersionedModel, GenerationOptions, CancellationToken)`
is synchronous and has no file, process, configuration or compiler dependency.
`GenerationOptions.LanguageVersion` accepts numeric `14` or `15`; the default is
`14`. Compiler release channel is chosen by the caller independently.

`GenerationResult` returns `Status`, `Family`, exact `SourceVersion`, selected
`LanguageVersion`, `Sources`, `Diagnostics` and `Runtime`. Each source has
`RelativePath` and `Text`. The runtime requirement is `net10.0` plus the public
`Morphir.SDK.CSharp` package. The core package itself does not reference that
runtime. Consumers must resolve a runtime implementing the frozen SDK contract.

`Success` returns the complete source set and no diagnostics. `Failure` returns
the first located refusal and no sources. `Cancelled` returns no sources or
diagnostics, retaining input family/version and the selected profile. A caller
that needs exception-based cancellation can translate this status at its edge.
Cancellation checks cover traversal and emission, with a final token check before
success. Invalid later modules also discard all earlier generated text.

Diagnostics carry stable `Rule`, structural `Location` and human-readable
`Message` fields. Locations identify the version/options/distribution or the
package/module/type/value/pattern/expression being checked. Callers should branch
on `Rule`, not parse messages. The CLI successor can map these categories without
loading a compiler or changing core semantics.

| Category | Rules |
| --- | --- |
| Input/profile boundary | `unsupported-language`, `unsupported-version`, `unsupported-distribution` |
| Target names and portable paths | `invalid-name`, `name-collision`, `path-collision`, `path-length` |
| Type identity and closure | `unresolved-type`, `unmapped-sdk-type`, `unbound-type`, `type-arity`, `tuple-arity`, `recursive-alias`, `ambiguous-type`, `anonymous-record`, `private-type-exposure` |
| Type/representation constraints | `type-mismatch`, `open-record`, `unsupported-constraints`, `unsupported-numeric`, `unsupported-comparable`, `unsupported-equality`, `representation-loss`, `document-literal` |
| Value and constructor resolution | `unbound-variable`, `unresolved-reference`, `unresolved-constructor`, `ambiguous-constructor`, `constructor-arity`, `unmapped-sdk` |
| Unsupported source semantics | `unsupported-body`, `unsupported-expression`, `local-recursion` |
| Patterns and records | `duplicate-pattern-binding`, `empty-match`, `unknown-field` |

Unknown dependencies and SDK operations fail explicitly. No object-erased value,
runtime stub or success-shaped partial artifact substitutes for a refusal.
Admitted value-dependent SDK failures, such as checked overflow, remain execution
errors. An admitted non-exhaustive source pattern raises pattern failure when no
branch matches; every branch is still checked during generation.

Generated records/cases have get-only payloads and explicitly implement the SDK
structural equality/hash protocol. That protocol alone does not establish the
physical immutability of arbitrary externally supplied CLR generic payloads.
The tested Morphir inputs use the admitted typed representations and immutable
SDK collections. No arbitrary CLR `IEquatable` or reflection fallback is emitted.

See [usage, host matrix and fixture scope](_index.md). CLI registration and the
production AdditionalFiles/config/incremental adapter remain gated successors.
