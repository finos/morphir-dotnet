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

The frontend is available through its library API:
`Morphir.Frontend.FSharp.FSharpFrontend.registration` is an `IFrontend` from
`Morphir.Compilation`. The `morphir compile` command and the frontend extension
for other Morphir hosts are planned in the
[`add-fsharp-compile`](https://github.com/finos/morphir-dotnet/issues/499)
change.

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
