---
title: "Compilation contracts"
linkTitle: "Compilation"
weight: 40
description: "Exact IR versions, checked diagnostics, native IR builders and IR writers in Morphir.Compilation."
---

`Morphir.Compilation` holds the language-neutral parts of `morphir compile`:

- exact IR format versions;
- diagnostics that travel on success and on failure;
- the IR builder interface, which a frontend lowers against once;
- the writers that turn a built model into JSON for one exact version;
- the in-process compile contract that a frontend implements.

The design is in the [add-fsharp-compile design](../../../../openspec/changes/add-fsharp-compile/design.html)
(decisions VER-1, DIAG-1, MID-1, IRW-1 and FE-1).

## Format versions

A format version is never an integer. It is a `Versioning.FormatVersion`: an integer classic version, or a
SemVer version that can have pre-release and build parts. `FormatVersion.sameVersion` is the only
equivalence rule:

| Left | Right | Same version |
| --- | --- | --- |
| `3` | `3.0.0` | yes |
| `4` | `4.0.0+build.7` | yes: build metadata has no effect |
| `4.1.0-draft.1` | `4.1.0-draft.1` | yes |
| `4.1.0-draft.1` | `4.1.0` | no: a pre-release never matches its release |
| `4.1.0-draft.1` | `4.1.0-draft.2` | no |

`IrTarget` equality and hashing follow the same rule. A writer writes the integer `n` for `n` and `n.0.0`,
and the exact text for any other version (`FormatVersion.canonicalText`).

## Checked results

Every stage returns `Checked<'T>`, never `Result<'T, Diagnostic>`:

- a passed result has a value and every warning, info and hint gathered for it;
- a failed result has at least one error, and every diagnostic gathered before it.

Only the severity decides the outcome. In the `check { }` builder, `let!` runs dependent steps and stops at the
first failure; `and!` and `Checked.traverse` run independent steps and keep the diagnostics of all of them.
The builder is named `check` because `checked` is a reserved F# keyword. C# callers use `Checked<T>.Pass`,
`Checked<T>.Fail`, `TryGetValue` and `Match`.

Diagnostics have the MEP shape: a severity, an optional parsed `DiagnosticCode`, a message, an optional
`Location` (zero-based lines, characters in UTF-16 code units, exclusive end), related locations and data.

## IR builders

A frontend writes its lowering once, as an `IBuilderProgram`, against `IIrBuilder<…>`. That interface has one
member for each IR node of the supported subset. Each family implements it natively:

| Builder | Builds | Notes |
| --- | --- | --- |
| `ClassicBuilder` | classic IR, for format versions 1, 2 and 3 | Each value and pattern carries its inferred type as its attribute. Classic IR has no source locations. An integer outside int64, a character outside the Basic Multilingual Plane and a non-finite float are refused with `morphir.unrepresentable`. |
| `V4Builder` | IR v4 | Each value and pattern carries its inferred type and its 1-based source; types carry their source. Record fields, constructors and let-recursion definitions keep source order. |

No code converts a model from one family to another. Whole-program work (names, access, exposed modules,
declaration order) happens in the frontend before building, so it is the same for every family. The builder
contracts are in the `Morphir.Compilation.Building` namespace and are for F# code only.

### Adding a family

1. Implement `IIrBuilder<…>` with the new family's own node types.
2. Add a case to `FamilyModel` for the family's model.
3. Implement `IFamilyBuilder`, and register it in the `FamilyBuilderRegistry`.
4. Add a writer (below) for each format version of the family.

No frontend changes. The compiler makes every builder implement every member.

## IR writers

An `IIrWriter` declares the exact versions it writes. `WriterRegistry.Resolve` selects the one writer whose
declared versions contain the requested version under `sameVersion`. It refuses a version that no writer
declares (`compile.unsupported_ir_version`) and a version that two writers declare
(`compile.ambiguous_ir_writer`). It never picks the nearest version. A new version, including a pre-release, is
one new writer declaration.

| Writer | Declares | Writes |
| --- | --- | --- |
| `ClassicIrWriter` | `1`, `2`, `3` | Classic library JSON through `ClassicJsonWriter`, with the wire profile of the version. |
| `V4IrWriter` | `4` | Canonical compact IR v4 JSON through `V4JsonWriter`, with number lexemes taken from the model. |

The classic writer reads its rules from a `ClassicWireProfile`, a record of data with no behavior:

| Rule | v1 | v2 | v3 |
| --- | --- | --- | --- |
| Type tags | `snake_case` | `PascalCase` | `PascalCase` |
| Value, pattern and literal tags | `snake_case` | `snake_case` | `PascalCase` |
| Access wrapper | `["public", x]` | `{"access": "Public", "value": x}` | `{"access": "Public", "value": x}` |
| Module list | `[{"name": path, "def": module}]` | `[[path, module]]` | `[[path, module]]` |
| Library tag | `library` | `Library` | `Library` |
| Module without documentation | `doc` left out | `doc` left out | `"doc": null` |

## Frontends

A frontend implements `IFrontend`. It has an identity, a transport and a `FrontendCapability`: the languages
and file extensions it reads, the IR targets it produces, and whether one request can hold more than one
document. `CompileAsync` takes a `CompileRequest` and returns a `FrontendOutcome`:

| Outcome | Meaning | Exit code in the CLI |
| --- | --- | --- |
| `Completed` with a passed result | A `CompiledPackage`: the model in the requested family and the module names. Warnings, information and hints travel with it. | `0` |
| `Completed` with a failed result | Source problems, each a diagnostic with a code and a range. | `1` |
| `Failed` | An operational failure, such as a language or target that the frontend does not serve. It keeps the diagnostics gathered before it. | `2` |

Only `CompileRequest.Parse`, `TryParse` and `Create` build a request. They refuse a missing language, a missing
package name, an empty document list, a missing document, a document without a URI or text, a document in
another language and a repeated URI. A language is a parsed `LanguageId`, for example `fsharp`: a lowercase
letter, then lowercase letters, digits, `-`, `_` and `.`. The exposed modules are
absent to expose every public module, or a list (which can be empty) to expose only those modules.

The in-process F# frontend is `FSharpFrontend.registration` in `Morphir.Frontend.FSharp`. It produces every
target for which a builder and a writer are registered: classic versions `1`, `2` and `3`, and `4`. The
conformance runner's `FrontendAdapter.create` runs the Morphir Compatibility Kit against any `IFrontend`.

## Example

This example builds one module with both families and writes classic format version `3.0.0`. A test compiles
and runs the same code.

```fsharp
open System.Numerics
open Morphir.Compilation
open Morphir.Compilation.Building
open Morphir.IR
open Morphir.IR.Model

let here =
    Location.Create("Main.fs", Range.Create(Position.Create(0, 0), Position.Create(0, 4)))

let name (text: string) = Name.fromString text

let path (texts: string list) =
    Path.fromList (
        texts
        |> List.map name
    )

/// One lowering, written once against any family's builder: Main.rate = 60.
let program =
    { new IBuilderProgram with
        member _.Run(builder) = check {
            let intType: FQName = {
                PackagePath = PackageName.packageName (path [ "morphir"; "s-d-k" ])
                ModulePath = ModulePath.modulePath (path [ "basics" ])
                LocalName = name "int"
            }

            let! int = builder.TypeReference(here, intType, [])
            let! sixty = builder.Literal(here, int, LiteralValue.Integer(BigInteger 60))
            let! rate = builder.ValueDefinition(here, [], int, sixty)

            let plan = {
                Path = ModulePath.modulePath (path [ "main" ])
                Access = Access.Public
                Documentation = None
                Location = here
            }

            let rateMember = {
                Name = name "rate"
                Access = Access.Public
                Documentation = Some "The daily rate."
                Definition = rate
                Location = here
            }

            let! main = builder.Module(plan, [], [ rateMember ])

            return!
                builder.Package(
                    {
                        Package = PackageName.packageName (path [ "acme" ])
                        Documentation = None
                    },
                    [ main ]
                )
        }
    }

let builders =
    FamilyBuilderRegistry.Create [ ClassicFamilyBuilder() :> IFamilyBuilder; V4FamilyBuilder() ]

let writers = WriterRegistry.Create [ ClassicIrWriter() :> IIrWriter; V4IrWriter() ]

let written = check {
    let! resolved =
        writers.Resolve(
            Versioning.SemVer
                {
                    Major = 3
                    Minor = 0
                    Patch = 0
                    PreRelease = None
                    BuildMetadata = None
                }
        )

    let! builder = builders.Resolve resolved.Target.Family
    let! model = builder.Build program
    return! resolved.Writer.Write(resolved.Target, WriteOptions.Compact, model)
}

// written passes with the bytes of {"formatVersion":3,"distribution":["Library",[["acme"]],[],...]}
```
