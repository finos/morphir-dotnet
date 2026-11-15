---
title: "C# generation"
---

The C# backend writes a Morphir library model as C# 15 source. It is part of `Morphir.Lang.CSharp`: the frontend that
reads C#, the passes that run before the backend, and the backend share one package, and all three use Roslyn. The
backend builds each file as a Roslyn syntax tree with `SyntaxFactory`, and a layout step writes the text. It uses no
Roslyn Workspaces assembly, and it is a pure function of the model and the options: it reads no file and starts no process.

The backend is the `csharp` target of `morphir generate` and of `morphir compile --target csharp`. In code, call
`CSharpBackend.Generate(model, options, cancellationToken)` of `Morphir.Lang.CSharp.Emit`.

> **Breaking change.** The package `Morphir.CodeGeneration.CSharp` and its text backend are gone, and no package replaces it
> yet: `Morphir.Lang.CSharp` is published with generation in a build. Until then, generate with the `csharp` target of the
> CLI. The API of the backend is `CSharpBackend` in `Morphir.Lang.CSharp.Emit`. The new backend writes C# 15 only: a request for
> `language-version=14` fails with the code `unsupported-language`, and the diagnostic names 15. The shape of the output
> changed: modules are namespaces and static classes (no `...Module` class), a custom type is a `closed record` or a `union`
> (no `abstract class` with nested cases), and records have the equality of plain C# (no `IMorphirValue` members unless you
> ask for them). The consumer needs .NET SDK 11 and `LangVersion` 15, or a `LangVersion` that the compiler accepts for
> `closed` and `union`.

## The output

A model with a custom type `Shape` and a function `size` that matches on it gives one file:

```csharp
namespace Acme.Geo;
public static class Shapes
{
    public closed record Shape;
    public sealed record Circle(double Radius) : global::Acme.Geo.Shapes.Shape;
    public sealed record Rect(double W, double H) : global::Acme.Geo.Shapes.Shape;
    public sealed record Square(double Edge) : global::Acme.Geo.Shapes.Shape;
    public static double Size(global::Acme.Geo.Shapes.Shape s) => s switch
    {
        global::Acme.Geo.Shapes.Circle(var r) => r,
        global::Acme.Geo.Shapes.Square(var e) => e,
        global::Acme.Geo.Shapes.Rect(var w, var h) => w
    };
}
```

The rules that give this shape:

| Morphir | C# |
| --- | --- |
| A module that has only types, with no module above it that has values | A namespace. Its types stand in the namespace. |
| A module that has values | A `static class` named after the module, in the namespace of the modules above it. Its types are nested in it. |
| A module below a module that has values | A nested `static class`, also when it has only types. A class between the two that has no module of its own is `public` when a public module is below it. |
| A record alias | `sealed record Name(Type Field, ...)`, with `where T : notnull` for each type parameter. |
| A custom type with public constructors, in which each constructor has one argument that is a reference with the local name of the constructor, and no two constructors hold the same C# type | `union Name(Case1, Case2)` |
| Any other custom type | `closed record Name` with one `sealed record Case(...) : Name` for each constructor |
| A pattern match | A `switch` expression, with each arm and the closing brace on a line of their own. An arm that cannot match is not written. |
| A value that takes parameters | A `static` method. A saturated call is a direct call. A partial application is a `Func`. |
| A tail-recursive `LetRecursion` | A local function with a `while` loop. Functions of one `LetRecursion` that call each other only as their last step are one local function with a `while` loop and a state for each function; no stack grows. Any other `LetRecursion` is a local function. |

A custom type that mixes wrapper constructors with other constructors takes the closed form. A wrapper custom type with
private constructors also takes the closed form, with `internal` cases: a C# union has public creation members, so it
cannot keep a constructor private. Two wrapper constructors that hold the same C# type, for example two record aliases
with the same fields, also take the closed form.

C# does not allow a member with the name of its enclosing type, so a type, a case or a value that has the name of its
module class gets a trailing underscore. Other names must be different in each namespace and static class: a case of
the closed form, a type, a value and the class or namespace of a module below. When two of them have the same name, or
when a module has the name of the class that holds it, generation fails with `name-collision`, and the diagnostic names
both. A generic type can have the name of a static class or a namespace, because C# allows it. A value can have a name that ends
in `Module`: the module classes have no such suffix.

### The polyfill file

C# 15 output needs runtime types that an older target framework does not have. Each generated output has the file
`Morphir.Polyfill.g.cs` with `internal` copies of these types, each group behind its guard:

| Type | Guard |
| --- | --- |
| `IsExternalInit` | `#if !NET5_0_OR_GREATER` |
| `CompilerFeatureRequiredAttribute` | `#if !NET7_0_OR_GREATER` |
| `UnionAttribute`, `IUnion`, `IsClosedTypeAttribute` | `#if !NET11_0_OR_GREATER` |

The file is the same for every model and every target framework. It is also the text that `morphir compile` adds to the
compilation of a C# model.

## Equality

A record and a union have the equality of plain C# by default. A record with a `NaN` field is equal to its copy. This
differs from Elm, where `NaN` is not equal to `NaN`. The IEEE option writes the Morphir members (`MorphirEquals`,
`MorphirHashCode`, `Equals`, `GetHashCode`, and `==` and `!=` for a union) for a type, so `NaN` is not equal to `NaN`
there. The CLI has no setting for the IEEE option yet: in code, `CSharpBackend.GenerateWith` takes the types that get it. `Morphir.SDK.CSharp` accepts both: its `Basics.Equal` and its collections compare a plain record or union with its
own equality. A `double` in an SDK list, dictionary, `Maybe` or `Result` compares as a double. A plain record or union that
holds a function-typed field fails with `NotSupportedException`: references are not compared.

## Options

| Option | Values | Default |
| --- | --- | --- |
| `language-version` | `15` | `15` |
| `namespace` | A dotted C# namespace | The package path |
| `profile` | `imperative`, `functional` | `imperative` |

The backend checks the value of `profile`, but the two profiles give the same output until the loop forms of the
profiles are written.

A repeated option uses its last occurrence. An unknown option, a value of the wrong JSON type, or a namespace with source
syntax fails the invocation with the code `invalid-backend-option` (exit code 2) before any file is written.

## Checks before the backend

The checks of the backend run first, as the target-side pass `morphir-dotnet.csharp.admission` (see
[the middle end](../../pipeline/middle-end.md)): the type catalog, the lowering and type check of the values, and the name
collisions. A refusal has the code, the message and the location of the check, for example `name-collision`,
`unbound-variable` or `document-literal`, and no file is written. The result reports the pass under `passes`.

## Names, literals and functions

Package paths and module paths become namespaces and class names in Pascal case. Source paths use safe prefixes, such as
`p_Example/m_FunctionsModule.g.cs`, including on Windows. Portable comparisons ignore case. A collision diagnostic
identifies both owners. Generated local identities are unique within each method. Names that collide with a required
protocol, generic parameter or inherited CLR member fail with a located `name-collision`. Malformed names fail with
`invalid-name`.

Literals use culture-independent numeric text. Char requires one non-surrogate BMP scalar. Int literals must fit checked
Int32. A v4 decimal lexeme must hold exactly in Decimal96; rounded or out-of-range values fail with `representation-loss`.
Signed-zero Float bits are preserved, and classic nonfinite Float literals map to the corresponding double constants.
Invalid UTF-16 strings fail explicitly.

Type checking runs before a successful source set is returned. Unknown variables and references, inconsistent applications,
infinite types and unsupported numeric constraints produce located diagnostics. A recursive binding with no parameter fails
with `local-recursion`.

Cancellation returns `Cancelled`, the original typed family and source version, and no sources.

## SDK bindings

Generated code calls the public `Morphir.SDK.CSharp` API. The declared output requirement is `net10.0` or newer plus
`Morphir.SDK.CSharp`.

| Source module | Admitted runtime calls |
| --- | --- |
| Basics | Checked Int32/IEEE Float arithmetic, conversion and rounding, comparisons, min, max, clamp, Order cases and structural equality |
| List | singleton, cons, map, filter, filterMap, foldl, append, head, tail and length |
| Maybe | Just and Nothing constructors and patterns, map and andThen |
| Result | Ok and Err constructors and patterns, map and andThen; the error type first, the value type second |
| Dict | empty, singleton, fromList, get, insert, update, remove, toList and union |
| Set | empty, singleton, fromList, insert, remove, toList and union |
| Decimal | Exact add, sub, mul, div, compare and toString |

Map and andThen use the instance combinators. Nothing and Err bypass callbacks, and callback exceptions propagate.
Dictionary and set factories select `MorphirComparers.Int`, `Float`, `Char` or `String`, composing list and tuple comparers
as needed. Ordering requires concrete admitted comparable shapes, with finite Float values. A factory whose key comparer
cannot be resolved fails with `unsupported-comparable`. Unmapped SDK references fail with `unmapped-sdk`; dependencies
without an implementation fail with `unresolved-reference` or `unresolved-type`.

Value-dependent SDK failures, such as checked overflow or a nonfinite Float-to-Int conversion, remain the execution errors
of the SDK.
