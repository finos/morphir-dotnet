---
title: "C# handling options"
linkTitle: "Handling options"
weight: 20
description: "Select how Morphir handles a C# model with attributes in the C# source or with keys in morphir.toml, and the order in which they apply."
---

A handling option selects one of the handlings that the C# subset supports: the equality of a `double`, the form of a
list traversal, the form of a tail call, and the lowering of a loop. Each option has two carriers with the same values:
a C# attribute and a key. Use the carrier that you prefer. A project that has only attributes needs no `morphir.toml`.

## The options

| Option | Kind | Attribute | `--option` | `morphir.toml` key | Values | Default |
| --- | --- | --- | --- | --- | --- | --- |
| Float equality | Backend | `[MorphirEquality(FloatEquality.X)]` | `float-equality` | `[codegen.csharp]` `float_equality` | `Clr`, `Ieee` (`clr`, `ieee`) | `Clr` |
| Traversal form | Backend | `[MorphirEmit(Traversal = TraversalForm.X)]` | `traversal` | `[codegen.csharp]` `traversal` | `Loop`, `Functional` (`loop`, `functional`) | The profile |
| Tail recursion form | Backend | `[MorphirEmit(TailRecursion = TailRecursionForm.X)]` | `tail-recursion` | `[codegen.csharp]` `tail_recursion` | `Loop`, `LocalFunction` (`loop`, `local-function`) | `Loop` |
| Profile | Backend | None | `profile` | `[codegen.csharp]` `profile` | `imperative`, `functional` | `imperative` |
| Loop lowering | Frontend | `[MorphirLoop(LoopLowering.X)]` | None | `[frontend.csharp]` `loop_lowering` | `Traversal`, `Recursion` (`traversal`, `recursion`) | `Traversal` |
| Class of a type parameter | Frontend | `[MorphirValueClass(ValueClass.X)]` | None | None | `Equality`, `Comparable`, `Number`, `Appendable`, `CompAppend` | The constraints and the uses |
| Model project | Analyzer | `[assembly: MorphirModel]` | None | The MSBuild property `MorphirModel` | `true` | Not a model project |

The attributes are in the namespace `Morphir`, in the attribute assembly of the package `Morphir.Lang.CSharp`, which
targets `netstandard2.0`. `morphir compile` has the assembly in its fixed references.

- A **backend** option changes only the C# text that the backend writes. The IR stays the same.
- A **frontend** option changes the IR that the frontend gives. A loop that the frontend refuses stays refused with each
  value of an option: an option selects between supported handlings, and it does not make refused code accepted.
- The profile gives the default traversal form: `imperative` writes loops, and `functional` writes `Select`, `Where`,
  `Aggregate` and `FilterMap`. A tail call is a `while` loop in each profile, and `tail-recursion=local-function` makes
  it a call of a local recursive function. See [Profiles](_index.md#profiles).
- `[MorphirLoop(LoopLowering.Recursion)]` and `loop_lowering = "recursion"` lower a `foreach` that changes locals to a
  `LetRecursion` over the list, not to `List.foldl`. A loop that fills a list stays `List.map`, `List.filter` or `List.filterMap`, and a loop that is
  already a recursion does not change. See [Loops](../../frontends/csharp/_index.md#loops).

- The model project switch is an option of the analyzer in the IDE and in `dotnet build`, which do not read
  `morphir.toml`. Its key is the MSBuild property `<MorphirModel>true</MorphirModel>` in the project file.
  `morphir compile` takes each C# source that it compiles as code of a model project.

## The nearest carrier wins

```text
command line  >  attribute on the member  >  on the type  >  on the assembly  >  morphir.toml  >  profile default
```

- An attribute on a member is on a method, a property or a positional parameter of a record.
- An attribute on a type is on a record, a union or a static class. For a case record of a closed record, the case comes
  first, then the closed record, then each type that holds it.
- An attribute on the assembly has the form `[assembly: MorphirEquality(FloatEquality.Ieee)]`.

For example, the type has IEEE equality, and one field has plain equality:

```csharp
[MorphirEquality(FloatEquality.Ieee)]
public partial record Quote([MorphirEquality(FloatEquality.Clr)] double Bid, double Ask);
```

`Bid` compares as plain C# does (`NaN` is equal to `NaN`), and `Ask` has IEEE equality (`NaN` is not equal to `NaN`).

## Equality needs `partial`

An attribute cannot change how compiled C# compares two records. A record or a union with IEEE equality from an
attribute (on the type, a closed base, a type that holds it, the assembly or one of its members) must be `partial`,
because the equality members are added to it. A type that is not `partial` gets the error `MORPHIR1009`
(`csharp.unsupported_non_partial_equality`) in the IDE, in `dotnet build` and in `morphir compile`.

## Backend options travel with the model

`morphir generate` reads an IR file and has no C# source. So the frontend writes each backend option of the attributes
into the model: in the `extensions` of the type attributes, as `{"morphir-dotnet": {"options": {"traversal":
"functional"}}}`. A function has it on its output type. Float equality is on the type of each field. The backend reads
the options from the model, applies them, and writes the attribute back. An option that comes only from `--option` or
from `[codegen.csharp]` writes no attribute.

## Example

Make a directory with one file, `Quote.cs`, and no `morphir.toml`:

```csharp
using Morphir;

[assembly: MorphirModel]

namespace Acme.Trading.Quotes;

[MorphirEquality(FloatEquality.Ieee)]
public partial record Quote([MorphirEquality(FloatEquality.Clr)] double Bid, double Ask);

public static class Pricing
{
    [MorphirEmit(Traversal = TraversalForm.Functional)]
    public static double Spread(Quote quote) => quote.Ask - quote.Bid;
}
```

Compile it and generate C# from the model in one command:

```bash
morphir compile --language csharp --input Quote.cs --package-name acme/trading --ir-version 4 --target csharp --generate-output gen
```

The generated `Quote` has the attributes again and the members of IEEE equality, and `Spread` has
`[global::Morphir.MorphirEmit(Traversal = global::Morphir.TraversalForm.Functional)]`. Generate again from the IR file,
with plain equality from the command line:

```bash
morphir generate --input .morphir/out/compile.dest/morphir-ir.json --target csharp --output gen-plain --option float-equality=clr
```

The command line wins over the attributes, so this `Quote` has no equality members. Without the option, the same
command gives the files of `gen` again: the model kept the attribute values.

To set an option for a project without attributes, add the key to `morphir.toml`. An attribute wins over the key:

```toml
[codegen.csharp]
float_equality = "ieee"
traversal = "functional"
```

A malformed value fails before any file is written, with exit code 2: `--option float-equality=maybe` fails with
`invalid-backend-option`, and `float_equality = "maybe"` fails when the configuration loads.

## Frontend options in `morphir.toml`

A frontend option changes the IR, so its key is in `[frontend.csharp]`, not in `[codegen.csharp]`. `morphir compile`
gives the key to the C# frontend in the compile request. An attribute on the member, the type or the assembly wins over
the key. Make a directory with this `Totals.cs`:

```csharp
using System.Collections.Generic;

namespace Acme.Demo;

public static class Totals
{
    public static int Sum(IReadOnlyList<int> amounts)
    {
        var total = 0;
        foreach (var amount in amounts) { total += amount; }
        return total;
    }
}
```

and this `morphir.toml`:

```toml
[frontend.csharp]
loop_lowering = "recursion"
```

Then compile it:

```bash
morphir compile --language csharp --input Totals.cs --package-name acme/demo --ir-version 4
```

The `Sum` function of the IR is a `LetRecursion` over the list. Without the key, it is `List.foldl`. The attribute
`[assembly: MorphirLoop(LoopLowering.Recursion)]` in `Totals.cs`, with no key, gives the same IR.

`[frontend.csharp]` has the keys `extension`, `project_file` and `loop_lowering`, and no other key. A malformed key
fails when the configuration loads, before the frontend runs, with exit code 2: `loop_lowering = "sideways"` and
`traversal = "functional"` (a backend key in the frontend table) both fail.

## Not yet

- A `morphir.toml` key for the class of a type parameter. A key must name one type parameter of one declaration, and
  the address of such a node is not agreed upstream. Use the attribute `[MorphirValueClass]`.
