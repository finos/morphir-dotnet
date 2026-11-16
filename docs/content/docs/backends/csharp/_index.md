---
title: "C# generation"
---

The C# backend writes a Morphir library model as C# 15 source. It is part of `Morphir.Lang.CSharp`: the frontend that
reads C#, the passes that run before the backend, and the backend share one package, and all three use Roslyn. The
backend builds each file as a Roslyn syntax tree with `SyntaxFactory`, and a layout step writes the text. It uses no
Roslyn Workspaces assembly, and it is a pure function of the model and the options: it reads no file and starts no process.

The backend is the `csharp` target of `morphir generate` and of `morphir compile --target csharp`. In code, call
`CSharpBackend.Generate(model, options, cancellationToken)` of `Morphir.Lang.CSharp.Emit`.
To generate C# when a C# project builds, use the source generator or the MSBuild target of the package: see
[C# generation in a build](build-integration.md).

> **Breaking change.** The package `Morphir.CodeGeneration.CSharp` and its text backend are gone. The package
> `Morphir.Lang.CSharp` replaces it, and the API of the backend is `CSharpBackend` in `Morphir.Lang.CSharp.Emit`.
> Generation in a user build (the MSBuild target and the source generator) is not in the package yet. The new backend writes C# 15 only: a request for
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
there. Select it with `--option float-equality=ieee`, with `float_equality = "ieee"` in `[codegen.csharp]`, or with the
attribute `[MorphirEquality(FloatEquality.Ieee)]` in the C# of the model (see [Handling options](options.md)). In code,
`CSharpBackend.GenerateWith` also takes the types that get it. `Morphir.SDK.CSharp` accepts both: its `Basics.Equal` and its collections compare a plain record or union with its
own equality. A `double` in an SDK list, dictionary, `Maybe`, `Result` or tuple compares as a double.

The SDK uses no reflection for equality and hashing, so `Basics.Equal` works under Native AOT. It knows scalars, tuples,
its own values and `IMorphirValue`; any other value uses its own `Equals` and `GetHashCode`. The SDK has no function
check at run time. The backend refuses an equality on a type that holds a function with `unsupported-equality` when it
generates the code (see [Generic functions](#generic-functions)). In C# that people write, a delegate compares by
reference, as in .NET.

## Generic functions

A type variable of a function can have a value class, as in Elm core. The backend finds the class from one of two
sources. The first source that gives a class wins:

1. The carrier in the IR: the `extensions` of the type attributes of the type variable in the signature, as
   `{"morphir-dotnet": {"classes": ["comparable"]}}`. The class words are `equality`, `comparable`, `number`,
   `appendable` and `compappend`. The C# frontend writes this carrier.
2. The first word of the Morphir name of the type variable. `comparable`, `comparable-1` and `comparable-key` have the
   class `comparable`, and `number` has the class `number`.

A model with no carrier still generates: then only the name gives a class.

| Class | Generated C# | The type at each call |
| --- | --- | --- |
| `comparable` | `where T : global::System.IComparable<T>` | Int, Float, Char, String, or a list or a 2- or 3-tuple of comparable types. Any other type gives `unsupported-comparable`. |
| `number` | `where T : global::System.Numerics.INumber<T>` | Int or Float. Any other type gives `unsupported-numeric`. |
| `equality` | `where T : notnull`, no other constraint | A type that holds no function. |
| `appendable`, `compappend` | None: the function is refused with `unsupported-appendable`. Use `String` or `List`. | |

```csharp
// maximum : comparable -> comparable -> comparable
public static TComparable Maximum<TComparable>(TComparable a, TComparable b)
    where TComparable : global::System.IComparable<TComparable> => global::Morphir.SDK.CSharp.Basics.Max(a, b);

// twice : number -> number
public static TNumber Twice<TNumber>(TNumber x)
    where TNumber : global::System.Numerics.INumber<TNumber> => global::Morphir.SDK.CSharp.Basics.Add(x, x);
```

The SDK has overloads of `Basics` for `T : INumber<T>` (`Add`, `Subtract`, `Multiply`, `Negate`, `Abs`, `Clamp` and
`Power`) with checked arithmetic, so Int keeps its overflow failure. The order of a comparable type stays the order of the
SDK, with ordinal strings. An SDK list implements `IComparable<List<T>>` with the same order. A dictionary or a set with
a key of a type variable is refused with `unsupported-comparable`: the SDK has no comparer for it.

A type variable with no class takes no ordering and no arithmetic: they give `unsupported-comparable` and
`unsupported-numeric`.

### Equality through a type variable

The backend finds the type variables that reach an equality: in the body of the function, or through a call of another
function of the package. At each call, it checks the type that the call gives to each of these type variables. A type
that holds a function, also in a field of a record, a field of a case or the record of a union case, gives
`unsupported-equality` at the call. The message gives the path to the function type, for example:

```text
Functions have no admitted Morphir equality: Same compares the values of its type variable TA, and the compared type holds a function type at Callback.
```

The check gives diagnostics only. It does not change the generated code. A type variable that does not reach an equality
takes a function type.

## Options

Give an option with `--option KEY=VALUE` on `morphir generate` or `morphir compile --target csharp`, or as a key of
`[codegen.csharp]` in `morphir.toml`. A key of the table has `_` where the option has `-`.

| Option | `[codegen.csharp]` key | Values | Default |
| --- | --- | --- | --- |
| `language-version` | `language_version` | `15` | `15` |
| `namespace` | `namespace` | A dotted C# namespace | The package path |
| `profile` | `profile` | `imperative`, `functional` | `imperative` |
| `traversal` | `traversal` | `loop`, `functional` | `loop` for `imperative`, `functional` for `functional` |
| `tail-recursion` | `tail_recursion` | `loop`, `local-function` | `loop` |
| `float-equality` | `float_equality` | `clr`, `ieee` | `clr` |

```toml
[codegen.csharp]
profile = "functional"
float_equality = "ieee"
```

`profile`, `traversal`, `tail-recursion` and `float-equality` are handling options. They also have C# attributes, and the
model keeps the attribute values. See [Handling options](options.md) for the attributes and the order in which the
carriers apply. In short: `--option` wins over an attribute, and an attribute wins over a `[codegen.csharp]` key. For
`language-version` and `namespace`, `--option` wins over the key.

`profile` and `traversal` select the C# form of the list functions, and `tail-recursion` selects the form of a tail
call. See [Profiles](#profiles).

A repeated option uses its last occurrence. An unknown option, a value of the wrong JSON type, a word that is not a value
of the option, or a namespace with source syntax fails the invocation with the code `invalid-backend-option` (exit code
2) before any file is written. `[codegen.csharp]` is a closed table: an unknown key or a word that is not a value fails
when the configuration loads, with exit code 2, before any file is written.

## Profiles

The profile selects the C# form of the list functions and of the local recursion of the model. The IR is the same in
each profile: the C# frontend reads the C# of each profile back to the same IR, with source locations left out.

| Morphir IR | `imperative` (the default) | `functional` |
| --- | --- | --- |
| `List.map` | A `foreach` that adds each value to a `List<T>` | `Select` |
| `List.filter` | A `foreach` with an `if` that adds the element | `Where` |
| `List.filterMap` | A `foreach` with an `if` that adds a value | `FilterMap` |
| `List.foldl` | A `foreach` that sets accumulator locals | `Aggregate` |
| `LetRecursion`, tail call | A `while` loop with state locals | A `while` loop with state locals |
| `LetRecursion`, other | A local recursive function | A local recursive function |

These rules apply:

- `Select`, `Where`, `FilterMap` and `Aggregate` are methods of `Morphir.SDK.CSharp.List<T>`, so each result is an SDK
  list or the state of the fold. `List.filterMap` has its own form, because `Where` then `Select` is the form of
  `List.map` over `List.filter`.
- A fold with two or more state values has a tuple state. The imperative profile writes one tuple assignment, such as
  `(sum, count) = (sum + x, count + 1);`, so all new values come from the old values.
- The imperative profile has no loop form for a `List.filterMap` whose function is not `if c then Just e else Nothing`,
  or for a fold with a tuple state whose initial value or new value is not a tuple. It writes the functional form for
  them.
- The option `traversal` changes the form of the list functions of one value or of all values, and the profile gives
  its default: `loop` for `imperative` and `functional` for `functional`.
- A tail-recursive `LetRecursion` that the code after it calls once is a `while` loop with no function: the parameters
  are the state locals, and one tuple assignment gives them their new values. The name of the function does not stay
  in the C#, so the frontend names the function `loop` when it reads the loop. Any other tail-recursive function is a
  local function with a `while (true)` loop in it. With `tail-recursion=local-function`, a tail call is a call of a
  local recursive function, as for a call that is not a tail call.

### Example

With this `morphir.toml` in the project directory:

```toml
[project]
name = "acme/demo"

[frontend]
language = "csharp"
```

and this source `src/Totals.cs`:

```csharp
namespace Acme.Demo;

public static class Totals
{
    public static IReadOnlyList<int> Doubled(IReadOnlyList<int> amounts)
    {
        var result = new List<int>();
        foreach (var amount in amounts) { result.Add(amount * 2); }
        return result;
    }

    public static int Sum(IReadOnlyList<int> amounts)
    {
        var total = 0;
        foreach (var amount in amounts) { total += amount; }
        return total;
    }

    public static int Factorial(int n)
    {
        var acc = 1;
        while (n > 1) { acc *= n; n--; }
        return acc;
    }
}
```

compile the project, then generate C# with the functional profile. Run both commands in the project directory. The
compile writes the path of the published IR to stdout, and `generate` writes the generated files:

```bash
$ morphir compile
/work/demo/.morphir/out/compile.dest/morphir-ir.json
$ morphir generate --target csharp --output gen --option profile=functional
Morphir.Polyfill.g.cs
p_Acme.Demo/m_TotalsModule.g.cs
```

The file `p_Acme.Demo/m_TotalsModule.g.cs` has the functional forms:

```csharp
namespace Acme.Demo;
public static class Totals
{
    public static global::Morphir.SDK.CSharp.List<int> Doubled(global::Morphir.SDK.CSharp.List<int> amounts) => amounts.Select<int>(amount => global::Morphir.SDK.CSharp.Basics.Multiply(amount, 2));
    public static int Factorial(int n)
    {
        int acc = 1;
        int n_2 = n;
        while (global::Morphir.SDK.CSharp.Basics.GreaterThan(n_2, 1))
        {
            (acc, n_2) = (global::Morphir.SDK.CSharp.Basics.Multiply(acc, n_2), global::Morphir.SDK.CSharp.Basics.Subtract(n_2, 1));
        }

        return acc;
    }

    public static int Sum(global::Morphir.SDK.CSharp.List<int> amounts) => amounts.Aggregate<int>(0, (total, amount) => global::Morphir.SDK.CSharp.Basics.Add(total, amount));
}
```

With no `profile` option, the imperative profile writes loops:

```csharp
namespace Acme.Demo;
public static class Totals
{
    public static global::Morphir.SDK.CSharp.List<int> Doubled(global::Morphir.SDK.CSharp.List<int> amounts)
    {
        global::System.Collections.Generic.List<int> result = new global::System.Collections.Generic.List<int>();
        foreach (var amount in amounts)
        {
            result.Add(global::Morphir.SDK.CSharp.Basics.Multiply(amount, 2));
        }

        return global::Morphir.SDK.CSharp.List<int>.FromEnumerable(result);
    }

    public static int Factorial(int n)
    {
        int acc = 1;
        int n_2 = n;
        while (global::Morphir.SDK.CSharp.Basics.GreaterThan(n_2, 1))
        {
            (acc, n_2) = (global::Morphir.SDK.CSharp.Basics.Multiply(acc, n_2), global::Morphir.SDK.CSharp.Basics.Subtract(n_2, 1));
        }

        return acc;
    }

    public static int Sum(global::Morphir.SDK.CSharp.List<int> amounts)
    {
        int total = 0;
        foreach (var amount in amounts)
        {
            total = global::Morphir.SDK.CSharp.Basics.Add(total, amount);
        }

        return total;
    }
}
```

In the C# that the frontend reads, `amount => Multiply(amount, 2)` and the loop that adds `amount * 2` give the same IR.
A method parameter that a loop changes, such as `n`, is also a parameter of the loop, so the backend gives the state
local the name `n_2`.

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
| Basics | Checked Int32/IEEE Float arithmetic (also generic for `INumber<T>`), conversion and rounding, comparisons, min, max, clamp, Order cases and structural equality |
| List | singleton, cons, map, filter, filterMap and foldl (in the form of the [profile](#profiles)), append, head, tail and length |
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
