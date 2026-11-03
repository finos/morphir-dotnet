---
title: "C# generation API and diagnostics"
---

`CSharpGenerator.Generate(VersionedModel, GenerationOptions, CancellationToken)`
is synchronous and has no file, process, configuration or compiler dependency.
`GenerationOptions.LanguageVersion` accepts numeric `14` or `15`; the default is
`14`. Compiler release channel is chosen by the caller independently.

The existing `GenerationOptions` record and three-argument entry point are
unchanged. To choose an emitted namespace, call
`NamespaceOverride.TryCreate(string)`. It returns `Result<NamespaceOverride,
GenerationDiagnostic>`; a successful immutable value exposes `Value`. Pass it to
`CSharpGenerator.Generate(model, options, namespaceOverride, cancellationToken)`.
Both overloads use the same core implementation. An override applies to the
input Library package and all its qualified type, value and constructor
references. Package-derived source paths and the original typed IR remain
unchanged. Separate or concurrent requests never share naming state.

The namespace accepts dotted literal BMP C# identifiers in Unicode Normalization
Form C, including `@`-escaped reserved words. It admits the identifier character
categories defined by the
[C# lexical specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/lexical-structure#643-identifiers).
Formatting characters retain their literal spelling and have the compiler's
identifier-equivalence behavior. Source `\u`/`\U` escape syntax and surrogate
characters are outside this literal-name boundary. Values are validated rather
than normalized or interpreted as source text. Null, empty, malformed and
injection-like input returns `invalid-namespace` at `$options/namespace`.
An explicit null/default namespace value from a C# caller to the four-argument
overload returns that same located refusal and no sources. An already cancelled
caller still receives `Cancelled` first.

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

Diagnostic identities use explicit structural spellings, for example
`Name["car","rental"]`, `ModulePath[Name["inventory"]]` and
`FQName(PackagePath[Name["example"]],ModulePath[Name["data"]],Name["person"])`.
Raw words and path segments remain distinguishable. Strings are quoted, quotes
and backslashes are escaped, null strings are written `null`, and control or
surrogate characters use `\u` followed by four hexadecimal digits. Type mismatch
messages describe `Parameter(Name[...])`, `Named(FQName(...),[...])`,
`Product([...])`, `Fields[Name[...]:...,Name[...]:...]`, `Arrow(...,...)` or `Unit`.
These diagnostic-only spellings avoid structural reflection in NativeAOT.
Module validation locations now use `$modules/ModulePath[...]`. This intentionally
changes diagnostic text and that location spelling while retaining rule IDs,
refusal and cancellation behavior, generated source bytes and paths. Callers
should continue branching on `Rule` rather than parsing human messages.

| Category | Rules |
| --- | --- |
| Input/profile boundary | `unsupported-language`, `unsupported-version`, `unsupported-distribution`, `invalid-namespace` |
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

See [usage, host matrix and fixture scope](_index.md) and the
[generate command](../../cli/generate.md). The production
AdditionalFiles/config/incremental adapter remains deferred.
