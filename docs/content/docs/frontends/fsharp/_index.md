---
title: "F# frontend"
linkTitle: "F#"
weight: 10
description: "Compile a subset of F# into a Morphir IR model."
---

The F# frontend compiles F# source documents into a Morphir IR model. It writes
IR versions 1, 2 and 3 (the classic format) and version 4. It uses the F#
compiler service to parse and type-check the source, so a model has the types
that F# infers.

The frontend accepts a subset of F#: immutable data and pure functions. It
refuses every other construct with an error diagnostic. The
[coverage reference](coverage/) lists each refused construct, with its code and
an example.

## Status

The frontend has two forms:

- **In process.** `Morphir.Frontend.FSharp.FSharpFrontend.registration` is an
  `IFrontend` from `Morphir.Compilation`. The `dotnet-morphir` tool uses it.
- **MEP extension.** `morphir-fsharp-frontend` is a Morphir Extension Protocol
  (MEP) `0.2.0-draft.1` guest. Hosts that cannot load the F# compiler service,
  such as the AOT `morphir` executable, start it as a process. See
  [Run the frontend as a MEP extension](#run-the-frontend-as-a-mep-extension).

To compile F# from the command line, see
[compile]({{< relref "/docs/cli/compile" >}}).

## Input

A compilation gets:

- a package name, such as `acme/car-rental`;
- the modules that the package exposes;
- the source documents, in compilation order, as F# requires.

Each F# module, nested modules included, becomes one Morphir module. The module
path is the namespace and module names without the package path.

```fsharp
namespace Acme.CarRental

module Pricing =
    type Car = { Plate: string; Seats: int }

    type Status =
        | Available
        | Rented of days: int

    let dailyRate (car: Car) : int = if car.Seats > 4 then 60 else 40

    let charge (car: Car) (status: Status) : int =
        match status with
        | Available -> 0
        | Rented days -> dailyRate car * days
```

## Supported subset

| Area | Supported |
| --- | --- |
| Types | Type abbreviations, records, unions with named or positional fields, generic parameters, tuples, functions, `unit`, `bool`, `int`, `int64`, `float`, `decimal`, `char`, `string`, `option`, `Result` and `list` |
| Values | Literals, variables, references, constructor application, tuples, lists, record construction, field access, record update, application, lambdas, `let`, `let rec` and `let rec ... and`, `if`/`then`/`else`, and `match` |
| Patterns | Wildcard, variable, literal, tuple, constructor, empty list, cons, `as` and unit, in `match` and in function and `let` parameters |
| Operators and library functions | Arithmetic, comparison, equality, Boolean operators, `\|>`, `<\|`, `>>` and `<<`, and mapped functions of `List`, `Option`, `Result` and `String` |

The frontend keeps the order of fields, constructor arguments and match cases,
and the currying and evaluation structure of the source. A pipe or a
composition becomes a direct application, not a function in the model.

Operators and library functions map to Morphir SDK operations through an
explicit table. The type of an operand selects the operation: `a + b` on
`decimal` values is SDK `Decimal.add`. A reference with no entry in the table
gives `fsharp.unsupported_reference` and names the F# symbol.

## Names and access

F# names become Morphir names: `TaxRate` and `taxRate` are both `tax rate`.
Two declarations in one scope with the same Morphir name give
`fsharp.name_collision`. To give a declaration a different Morphir name, use the
`[<MorphirName("...")>]` attribute from the `Morphir.FSharp.Annotations`
package (namespace `Morphir`). The name is in canonical Morphir syntax.

A module is public when the list of exposed modules names it, or when the
compilation has no such list. An empty list exposes no module. An exposed module
that matches no compiled module gives `compile.unknown_exposed_module`. A
`private` or `internal` declaration is private.

## Diagnostics

Diagnostics use the MEP diagnostic shape: a severity, a stable code, a message
and a location. Positions are zero-based, and characters count UTF-16 code
units. An error stops the compilation, and the frontend writes no model.
Warnings and information diagnostics come with a successful model.

F# compiler errors have the code `fsharp.compiler`. Refused constructs have the
codes in the [coverage reference](coverage/).

## Run the frontend as a MEP extension

Install the guest as a dotnet tool. It needs the .NET runtime.

```sh
dotnet tool install --global Morphir.Frontend.FSharp.Extension
```

The same guest also has a self-contained folder delivery that carries its own
.NET runtime, for machines without .NET. See
[Compile without .NET installed]({{< relref "/docs/getting-started/compile-fsharp#compile-without-net-installed" >}}).
Both deliveries compile with the same references (the .NET framework,
FSharp.Core and `Morphir.FSharp.Annotations`), so they give the same IR and
diagnostics.

The guest speaks only MEP `0.2.0-draft.1`, over stdio with `Content-Length`
framing. It serves `morphir.frontend.compileModel` and claims:

| Claim | Value |
| --- | --- |
| Claims version | `0.1.0-draft.2` |
| Language | `fsharp`, with the file extension `.fs` |
| Several documents in one request | yes (`multiDocument: true`) |
| Legacy `morphir.frontend.compile` | no |
| Incremental compilation | no |
| Model tuples | `morphir.frontend.compileModel`, profile `morphir-ir`, codec `morphir-json`, envelope `0.1.0-draft.1`, IR `4.0.0` and `3.0.0` |

Classic IR versions 1 and 2 have no tuple. For them, the host compiles at
`3.0.0`, reads the classic model, and writes it at the requested version. The
model does not change family. A host that agrees only `4.0.0` with a guest
refuses a classic request before it sends one.

To use the guest from the `morphir` executable, give its path with
`--extension`, or set it in the project configuration:

```sh
morphir compile --extension "$(which morphir-fsharp-frontend)"
```

```toml
[frontend.fsharp]
extension = "morphir-fsharp-frontend"
```

The host starts the guest with explicit arguments and no shell, and passes the
guest's stderr to its own stderr. Before it sends a request, it checks that the
session admits the language, the number of documents and the IR version. It
always ends the session with `shutdown` and `exit`, or stops the process after
a deadline. Guest output on stdout that is not a frame fails the compile with
`extension.protocol_error`.

The model in a successful result is a verified envelope: canonical base64 of
the IR JSON, its exact length and its SHA-256. The host checks them before it
reads the IR. For the same documents, the IR and the diagnostics from the guest
equal the in-process results; a test compares them for every case of the
conformance corpus.

Upstream qualification: the MEP guest kit of `finos/morphir`
(`generic-json-library-v1`) still needs a legacy `0.1` stage and a backend, so
the guest is not qualified with it yet. Its conformance comes from the pinned
`0.2.0-draft.1` contract, the pinned session transcripts and the
model-transport cases. See [deviations](https://github.com/finos/morphir-dotnet/blob/main/conformance/deviations.md).

## How the frontend is checked

- The [conformance corpus](https://github.com/finos/morphir-dotnet/tree/main/conformance)
  has cases for IR versions 1 to 4 and three input kinds. Each refused
  construct has its own case. The cases do not depend on the implementation.
- Behavior cases call the compiled values with JSON arguments and compare the
  results.
- A round trip generates F# from each compiled model with the F# backend,
  compiles that F# again, and requires the same model.

## Prior art

The frontend uses the design of two F# projects, but it does not depend on
them.

| Project | What the frontend uses | What it does not use, and why |
| --- | --- | --- |
| [Fable](https://fable.io/) (MIT) | Design patterns: how decision trees appear in the typed tree, a replacement table for core-library calls, how inline values and witnesses appear in the typed tree, and a resolver interface for project cracking. | `Fable.Transforms` as a library. It carries Fable's own syntax tree and replacements for JavaScript, Python and Rust targets. |
| [Fantomas](https://fsprojects.github.io/fantomas/) (Apache-2.0) | The F# backend formats generated code with `Fantomas.Core`. The round-trip check uses that backend. | `Fantomas.FCS` in the frontend. Its syntax tree is not binary-compatible with the F# compiler service, and the frontend needs the compiler service for type checking. A second parser would give a second set of ranges. |

If code is adapted from these projects, and not only its design, the source file
names its origin and the `NOTICE` file gives the attribution.
