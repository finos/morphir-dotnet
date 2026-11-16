---
title: "PRD: C# language support"
description: "A roadmap for Morphir.Lang.CSharp: a C# frontend, a middle end that the host runs, and a C# backend, all on Roslyn and built into both hosts."
status: In Progress
created: 2026-11-15
last_updated: 2026-11-15
---

# C# language support

This PRD is a roadmap. It sets the decisions that each step must keep and the order of delivery. Each step becomes its own OpenSpec change with its own design, specs and tasks. No step in this PRD is approved for implementation until its OpenSpec change is approved. The product owner approved this PRD on 2026-11-15 as the base for those changes.

## Why

A team that writes its business rules in C# cannot make a Morphir model from them today. morphir-dotnet reads F# and writes C#, but no product code reads C#.

Three facts make this the time to change that.

- The C# backend writes text with `String.concat`. It has no Roslyn dependency, so the frontend and the backend cannot share one model of C#. The product owner called this a mistake on 2026-11-15.
- The decision of 2026-11-07 kept a C# frontend out of the Native AOT `morphir` because of size. The [Roslyn AOT spike](../spikes/extension-model/roslyn-aot.md) had already shown that Roslyn publishes and runs under Native AOT. The measurements of 2026-11-15 in [Evidence](#evidence) show that the real cost is lower than that spike estimated.
- `morphir generate` goes from load directly to the backend. Validation and rewriting of Morphir IR happen inside each backend, where no other backend and no user can see them.

## Goals

- Read C# source into Morphir IR with `morphir compile`, in both hosts, with no extension to install.
- Report C# that Morphir cannot read in the IDE and in `dotnet build`, with the same rules that `morphir compile` uses.
- Run validation, transformation and rewriting of Morphir IR as a stage that the host runs, with passes that `morphir extension list` shows.
- Write C# from Morphir IR with Roslyn syntax trees. The written code uses C# 15 `closed` types and unions, and compares values as plain C# does, unless an option selects IEEE equality.
- Make each frontend transform have a matching backend transform, so that Morphir IR to C# to Morphir IR gives the same IR.
- Generate C# in a user build with an MSBuild target and with a Roslyn source generator, from one backend.
- Pass the trimming and Native AOT checks of `morphir` in each step.

## Non-goals

- The Roslyn Workspaces layer in a host. `Formatter`, `SyntaxGenerator` and `MSBuildWorkspace` fail under Native AOT.
- User package references in a model project. The references are fixed, as they are for the F# frontend.
- Rewrite rules that a user writes in a script language.
- A downgrade of Morphir IR to an older format version.
- Decorators, IR document trees and incremental compile. This PRD sets constraints for them in [Planned features](#planned-features) and builds none of them.

## Anti-goals

The work fails, even if it runs, when any of these is true:

- The IDE reports a construct that `morphir compile` accepts, or the reverse.
- Generated C# gives a different result than the authored C# model for an equality test.
- A middle-end contract or a language package contract names an IR format version.
- A rewrite changes a model and the host has no record of the rule and the node.
- A host loads `Microsoft.CodeAnalysis.Workspaces`, `Microsoft.CodeAnalysis.Scripting` or `System.Composition`.
- A step writes logs or diagnostics to stdout. Stdout carries command output only.

## Decisions

These decisions come from the product owner, all on 2026-11-15. Each step must keep them.

| # | Area | Decision |
| --- | --- | --- |
| D1 | Package | One package, `Morphir.Lang.CSharp`, holds the frontend, the target-side middle end and the backend. All three use Roslyn. The package also carries the analyzers, the rewriters, the MSBuild target and the source generator. |
| D2 | Middle end | The middle end is where validation, transformation and rewriting of Morphir IR happen. The host runs it. Each pass is a registry entry. |
| D3 | Rewrites | A rewrite is compiled code, not a script. A rule has catalog data, and the engine records each application. |
| D4 | IR version | Rules match the current IR. An older model goes up to the current IR first. The pattern is "previous to latest". No contract names a format version. V4 is the current IR today. |
| D5 | Languages | The assemblies that the compiler or the IDE loads are C# on `netstandard2.0`. The analyzer assembly depends on Roslyn only. The main assembly, with the lowering, the passes and the backend, is F#. |
| D6 | Recursion | A tail-recursive `LetRecursion` becomes a `while` loop in each backend profile. A local recursive function is an explicit option only. |
| D7 | Language level | C# 15 only. Unions and `closed` types give sum types that the compiler checks for exhaustive matches. |
| D8 | Dialect | Authors write plain records, and the backend writes plain records. Equality is the equality of plain C# by default. IEEE equality, where `NaN` is not equal to `NaN`, is an option (D16); only then the backend adds equality members. Revised on 2026-11-15. |
| D9 | Modules | Types directly in a namespace belong to the Morphir module of that namespace. A static class is a module below the namespace and holds values and nested types. |
| D10 | Build integration | An MSBuild target and a Roslyn source generator, on one F# core that also builds for `netstandard2.0`. Both are in scope now. |
| D11 | Planned features | The design keeps three constraints for decorators, IR document trees and incremental compile. |
| D12 | SDK | .NET SDK 11 is required to develop and build a project. The project can target a lower framework. |
| D13 | Delivery | C# is a built-in in all offerings, the Native AOT `morphir` included. This replaces the decision of 2026-11-07. |
| D14 | Sum type form | The analyzer reports a sum type that is not in the preferred form, and gives a rewriter that changes it to the preferred form. |
| D15 | Unions | The frontend accepts a C# `union` that can convert to a Morphir custom type. A union that cannot convert gets a diagnostic. |
| D16 | Options in code | A handling option has two carriers: a C# attribute and a configuration key. A user can select the handling in code and needs no configuration file for it. |

## Concepts

### The pipeline

The middle end has two positions. One is after a frontend and is the same for all languages. One is before a backend and is specific to the target.

```text
morphir compile                           morphir generate
C# source                                 Morphir IR file, any version
   │                                           │
   ▼                                           ▼
┌─ frontend ─────────────────┐              load
│ parse and bind (Roslyn)    │                 │
│ admission analyzers  ◄──── the same rules in the IDE and dotnet build
│ lowering (loops to folds)  │                 ▼
└────────────┬───────────────┘           upgrade to the current IR
             ▼                                 │
┌─ middle end, source side ──┐                 ▼
│ validate                   │   ┌─ middle end, target side ─────┐
│ transform (all languages)  │   │ validate for C# and profile   │
└────────────┬───────────────┘   │ rewrite to shapes C# can hold │
             ▼                   └───────────────┬───────────────┘
       Morphir IR file                           ▼
                                 ┌─ backend ─────────────────────┐
                                 │ plan, syntax, layout          │
                                 └───────────────┬───────────────┘
                                                 ▼
                                             .cs files
```

`Morphir.Lang.CSharp` owns the frontend, the target-side middle end for the target `csharp`, and the backend. The source-side middle end is general code.

Today the host has `IFrontend`, `IGenerationBackend` and `IModelAnalyzer`. `IModelAnalyzer` reads a model and only `morphir validate` runs it. The registry has the extension type `transform`, but no claim and no in-process contract for it. `morphir generate` runs no middle end.

**One pipeline function, three callers.** The upgrade, the target-side middle end and the backend are one pure function in a library with a small dependency set. The CLI calls it. The MSBuild target and the source generator call it too, and they have no CLI host. "The host" in D2 means the caller that runs this function. The contracts, the engine, the upgrade steps and the passes for `csharp` thus also build for `netstandard2.0`. This layering is a builder's call.

### Middle-end passes

A pass that changes a model has a new contract beside `IModelAnalyzer`. The names in this sketch are a builder's call.

```fsharp
/// The IR that the middle end and the language packages work on.
/// It moves when a newer IR lands. This is the one place that names a version.
type CurrentModel = Morphir.IR.V4.Distribution

/// A middle-end pass that changes a model.
type IModelTransform =
    abstract Entry: ExtensionEntry        // identity and the `transform` claim
    abstract Stage: TransformStage        // AfterFrontend | BeforeBackend of TargetId
    abstract TransformAsync:
        unit: ModuleUnit * package: IPackageView * ct: CancellationToken -> Task<Checked<ModuleUnit>>
```

- A pass takes the current IR, so it cannot receive an old model. The type prevents it.
- The unit of work is one module. The pass reads other modules through `IPackageView`, which is read-only. See [Planned features](#planned-features).
- `morphir extension list` shows each pass. Configuration can turn one off, as it can for an analyzer.
- An external extension can add a pass with the same contract when MEP has a transform payload.

### Rewrite rules

`Optimizer.fs` is one recursive function today, with the rewrites inline and switches in an `Options` record. The new form has three levels.

```text
rule      one local rewrite: match a node, give the replacement    code and catalog data
pass      a set of rules and a walk order; one registry entry      IModelTransform
engine    walks the tree, applies rules, records each application  host code, written once
```

A rule follows the style of the F# admission catalog, where each rule has a code, a message and an example.

```fsharp
/// One local rewrite. It returns None when the node does not match.
type RewriteRule =
    { Id: RuleId                    // an absolute IRI
      Title: string
      Example: RewriteExample       // IR before and after; also the test of the rule
      Removes: IrFeature list       // IR features that the rule consumes
      Adds: IrFeature list          // IR features that the rule produces
      Apply: RewriteContext -> Value -> Value option }   // Value of the current IR

let identityLambda =
    rule "https://morphir.finos.org/rules/ir/identity-lambda"
        "An identity lambda becomes its argument"
        (example "(\\x -> x) price" "price")
        (fun _ value ->
            match value with
            | Apply(_, Lambda(_, AsPattern(_, WildcardPattern _, x), Variable(_, y)), argument)
                when x = y -> Some argument
            | _ -> None)

type Strategy = BottomUp | TopDown | Fixpoint of Strategy * maxRounds: int
```

- The snippet is a sketch. `Value` is the value type of the current IR, and the pattern shows the idea, not the exact cases.
- The test suite runs each `Example`. A rule cannot ship without one.
- `Removes` and `Adds` let the host check the order of passes. A backend that cannot take a feature needs a pass that removes it.
- A profile or an option selects rules by id.

The engine records each application.

```fsharp
type RewriteApplication =
    { Rule: RuleId
      Before: NodeAddress           // the node that the rule matched, pinned to the input model of the pipeline
      After: NodeAddress            // the replacement node in the output
      Origin: SourceLocation option }
```

`NodeAddress` is the semantic node address of upstream; see [Planned features](#planned-features). It is not a type that this PRD invents.

`Before` is always an address in the input model of the pipeline, also after more than one pass. When a second pass replaces a replacement, the engine keeps the first origin. `Before` is pinned to the snapshot of that input, because an address that is not pinned resolves against the output.

The replacement node takes the attributes of the node that it replaces, so a later diagnostic still points to the source of the user. The pass result is the changed module and the list of applications. `--json` reports the list. Upstream `morphir-moonbit` has the same need: its rich source units reject transforms that have no metadata lineage.

### The current IR and upgrade steps

```fsharp
/// One upgrade step, from one format version to the next.
type IIrUpgrade =
    abstract Id: RuleId                   // an absolute IRI; the rule of each record that the step emits
    abstract From: FormatVersion
    abstract To: FormatVersion
    abstract Upgrade: VersionedModel -> Checked<VersionedModel * RewriteApplication list>
```

An upgrade is a transform. A step thus gives the same application list as a rewrite pass, with one record for each node that it moves. A node that is not in the list keeps its path.

The host joins the steps from the version of the model to the current IR. A new IR adds one step and moves `CurrentModel`. The old steps stay as they are.

- A step gives `Checked`, because a model can hold nodes that the next version cannot express.
- The C# frontend writes the current IR only. The C# backend claims the current IR only. A classic model reaches the backend through the upgrade.
- The repository has no classic to V4 conversion today. The upgrade step is that conversion. A `morphir migrate` command can use the same step later.

These calls are the builder's.

- `morphir generate` upgrades a model when the selected backend claims the current IR only. The F# backend reads classic models directly and stays as it is.
- A pass runs on the current IR only. When a command has a model of an older family and no upgrade applies, it skips the passes and reports one diagnostic that says so. `morphir validate` does the same for external analyzers.
- `Optimizer.fs` and `TypeValidator` work on classic IR. A port to the current IR is a different change. The source-side middle end ships with its contract and with no general pass.
- A claim lists the format versions that an extension reads, as data. The rule of D4 covers types and members: none is specific to one version.

Upstream uses the same idea. `morphir-moonbit` defines "Current IR" as the latest supported model, and `morphir migrate` in `finos/morphir` has `--target-version latest` as its default.

### Package layout

```text
Morphir.Lang.CSharp   (one NuGet package)
├─ Morphir.Lang.CSharp.Analyzers.dll   C#, netstandard2.0, depends on Roslyn only
│     admission rules, loop recognizer, sum type diagnostics
│     loaded by the compiler, the IDE and the frontend
├─ Morphir.Lang.CSharp.CodeFixes.dll   C#, netstandard2.0, depends on the Workspaces layer
│     rewriters; loaded by the IDE only, never by a host
├─ Morphir.Lang.CSharp.dll             F#, net11.0;net10.0;netstandard2.0
│     frontend lowering, target-side passes, backend
└─ Morphir.Lang.CSharp.Generator.dll   C#, netstandard2.0
      the source generator adapter
```

The compiler loads the analyzer assembly, so it cannot depend on `FSharp.Core` or the Morphir IR libraries. A Roslyn code fix needs the Workspaces layer, so the rewriters are in an assembly that only the IDE loads. The `netstandard2.0` build of the main assembly exists for the source generator.

### The C# model dialect

A model project uses C# 15 and builds with .NET SDK 11 (D7, D12).

**Modules (D9).** Types directly in a namespace belong to the module of that namespace. A static class is a module below it.

```csharp
namespace Acme.Trading;                          // module Acme.Trading: types only
public enum Side { Buy, Sell }
public closed record Order;
public sealed record Market(Side Side, decimal Quantity) : Order;
public sealed record Limit(Side Side, decimal Quantity, decimal Price) : Order;

public static class Pricing                      // module Acme.Trading.Pricing: values
{
    public static decimal Notional(Order order, decimal last) => order switch
    {
        Market m => m.Quantity * last,
        Limit l => l.Quantity * l.Price,
    };
}
```

The backend writes a module that has only types at namespace level, and each other module as a static class with nested types.

C# cannot have a namespace and a type with the same name. So a module below a static-class module is a nested static class, also when it has only types. The frontend reads a nested static class as a child module. This rule is a builder's call.

**Sum types (D7, D14, D15).** Two C# forms give a Morphir custom type.

| C# form | Morphir custom type | Use |
| --- | --- | --- |
| `closed record Price;` with `sealed record Quote(double Value) : Price;` | `type Price = Quote Float` | A new sum type. A case exists only as a constructor, and the constructor holds the fields. This is the preferred form. |
| `union Shape(Circle, Rect);` | `type Shape = Circle Circle \| Rect Rect` | The cases are types that the model also uses by themselves. A constructor wraps one such type. |

For the two forms, the compiler accepts a `switch` with no discard arm and reports `CS8509` when an arm is missing.

The analyzer reports an older encoding of a sum type, such as an `abstract record` with a private constructor and nested `sealed record` cases, and the rewriter changes it to the preferred form. The frontend refuses the older encoding with the same diagnostic.

**Unions (D15).** A union converts when these rules hold. Each rule that fails is one admission diagnostic, with the reason.

- Each case type lowers to a Morphir type reference: a record, a custom type, a different union, or an SDK type such as `int`, `string`, `int?` or `List<int>`. The constructor takes the local name of that reference and has that type as its one argument.
- A case with no name is refused: a type parameter (`union Opt<T>(T, Nothing)`) or a tuple.
- The constructor names of a module are different from each other, as upstream requires. Two unions in one module thus cannot share a case type, and `union X(List<int>, List<string>)` is refused.
- Code that reaches the state with no case is refused: `default` of a union, a `null` arm, the `Value` property and `IUnion`. A Morphir custom type has no such state.

```csharp
public sealed record Circle(double Radius);
public sealed record Rect(double W, double H);
public union Shape(Circle, Rect);

public static class Geometry
{
    public static Shape Unit() => new Circle(1.0);          // Apply (Constructor Shape.Circle) (the record)
    public static double Area(Shape s) => s switch          // PatternMatch
    {
        Circle c => 3.14 * c.Radius * c.Radius,             // ConstructorPattern Circle [ AsPattern c ]
        Rect r   => r.W * r.H,                              // ConstructorPattern Rect [ AsPattern r ]
    };
}
```

| C# | Roslyn | Morphir IR |
| --- | --- | --- |
| `Shape s = circle;`, `(Shape)circle` | `IConversionOperation` with a `Union` conversion | `Apply (Constructor Shape.Circle) circle` |
| `new Shape(circle)` | `IObjectCreationOperation` of the union | The same |
| `Circle c => ...` on a `Shape` | A pattern with the union as input type and the case as matched type | `ConstructorPattern Circle [ AsPattern c ]` |
| `s is Circle` | `IIsPatternOperation` | A `PatternMatch` that gives a `Bool` |

The backend writes a `union` for a custom type in which each constructor has one argument, and that argument is a reference with the local name of the constructor. The `closed` form cannot express this shape: the case record `Circle` and the type `Circle` are two types with one name. The backend writes the `closed` form for each other custom type. The round trip is thus exact in the two directions. A generated union is plain by default, as a generated record is (D8); with the IEEE option it takes the equality members in its body. A custom type that mixes the two constructor shapes takes the `closed` form, and the backend refuses it when a case name is equal to a type name of the module. This last rule is a builder's call.

**Equality (D8).** The default is the equality of plain C#. Authors write plain records, and the backend writes the same plain records with no added member.

| Test | Plain C# (the default) | IEEE option |
| --- | --- | --- |
| `double.NaN == double.NaN` | `false` | `false` |
| Two records with a `NaN` field | Equal | Not equal |
| Two records with `0.0` and `-0.0` | Equal, with equal hashes | Equal, with equal hashes |
| Two unions with the same case and value | Equal with `Equals`; a plain union has no `==` | Equal with `Equals` and `==` |

The rule that `NaN` is not equal to `NaN` in a record is a morphir-dotnet choice of 2026-11-03 (`Morphir.SDK.CSharp`, `ValueSemantics.cs`). No upstream rule was found. The V4 schema refuses `NaN` as a literal and says that numeric comparison is the choice of a consumer.

With the option `FloatEquality.Ieee`, on a type, on the assembly or in the configuration, the backend adds four members to a record, and the same members with `==` and `!=` to a union.

```csharp
[MorphirEquality(FloatEquality.Ieee)]
public closed record Price;
public sealed record Quote(double Value) : Price, IMorphirValue
{
    public bool MorphirEquals(IMorphirValue other) => other is Quote q && Value == q.Value;
    public int MorphirHashCode() => Value.GetHashCode();
    public bool Equals(Quote? other) => other is not null && MorphirEquals(other);
    public override int GetHashCode() => MorphirHashCode();
}
```

The default has these effects.

- The backend writes `a == b` for two records and `a.Equals(b)` for two unions. The frontend reads the two forms as the equality function of Morphir.
- `Morphir.SDK.CSharp` throws today for a value that is not a scalar, an SDK value, a tuple or an `IMorphirValue`. It must accept a plain record and a plain union, in `Basics.Equal` and in its collections.
- The SDK fails at run time today for a field with a function type and for `null`. The frontend refuses the two at compile time.
- A model that comes from Elm and compares a record that holds `NaN` gives a different result than Elm, unless the IEEE option is on.

The frontend reads the two shapes, so generated code can go back to IR. The `abstract class` shape that the backend writes today goes away.

**Lower target frameworks (D12).** The compiler accepts the C# 15 syntax for an older target. Only five runtime types are missing, and the compiler finds them by name. The backend writes one polyfill file into each generated assembly, with `internal` types behind guards.

| Type | Guard |
| --- | --- |
| `IsExternalInit` | `#if !NET5_0_OR_GREATER` |
| `CompilerFeatureRequiredAttribute` | `#if !NET7_0_OR_GREATER` |
| `UnionAttribute`, `IUnion`, `IsClosedTypeAttribute` | `#if !NET11_0_OR_GREATER` |

The consumer project must set `LangVersion` to 15, because the default follows the target framework. The MSBuild target sets it. `Morphir.SDK.CSharp` targets `net11.0;net10.0` today, so `net10.0` is the lowest target that works now.

### The frontend

```text
documents ─► parse ─► bind (CSharpCompilation, fixed references)
                        │  C# errors ─► diagnostics, stop
                        ▼
             admission (Analyzers.dll: the same rules as the IDE)
                        │  refused constructs ─► diagnostics, stop
                        ▼
             lowering (F#): symbols to types, IOperation to values
                        │  recognizer: loop to fold or recursion
                        ▼
             current IR, with a source location on each node
```

- The host gives the documents. The frontend never reads a project file. The existing project file provider already matches `*.*proj`. Step 1 checks that it gives the default `**/*.cs` items of an SDK-style project.
- The references are fixed. They are the framework reference assemblies and `Morphir.SDK.CSharp`, embedded in the package. The Native AOT host has no runtime assembly files, so they must be embedded.
- The frontend uses `CSharpCompilation`, `SemanticModel`, `ISymbol` and `IOperation`. It does not call `Emit`, `Assembly.Load` or `AnalyzerFileReference`.
- The frontend does not use `Enum.ToString()` on a Roslyn enum to make IR or a diagnostic code. Some values have two names, and Native AOT and JIT can give different names.

**The same result in the IDE and in `morphir compile`.** The IDE compiles a project with the options and the references of the project. `morphir compile` uses fixed options and fixed references. Two things keep the results equal, and both are a builder's call.

- The package gives a model project its compile options in an MSBuild props file: `LangVersion`, `Nullable` and the implicit usings. `morphir compile` uses the same values.
- An admission rule refuses a symbol from an assembly outside the fixed reference set. The IDE then reports the use of a package that `morphir compile` cannot bind.

**Admission analyzers.** Each rule for unsupported C# is a Roslyn `DiagnosticAnalyzer`. The package puts the analyzer assembly in `analyzers/dotnet/cs`, so the compiler and the IDE load it. `morphir compile` runs the same analyzers in process. A Roslyn id is a short code such as `MORPHIR1001`. A rule has the tag `NotConfigurable`, because a refused construct cannot become a model: `#pragma warning disable`, `.editorconfig` and `NoWarn` do not hide it, and the rule `MORPHIR1008` refuses a `SuppressMessage` attribute that names a rule. `morphir compile` refuses a construct even when the IDE does not report it. The absolute IRI of the rule goes in `DiagnosticDescriptor.HelpLinkUri`. The analyzer reports only in a Morphir model project.

**Loop lowering.** The frontend changes a loop to a collection traversal or to recursion where it can. One recognizer gives one answer to the analyzer and to the lowering.

```csharp
// Analyzers.dll. One answer for the IDE diagnostic and for the lowering.
public abstract record LoopShape
{
    public sealed record Map(IOperation Source, IOperation Projection) : LoopShape;
    public sealed record Fold(IOperation Source, ImmutableArray<ILocalSymbol> State) : LoopShape;
    public sealed record Recursion(ImmutableArray<ISymbol> State, int Exits) : LoopShape;
    public sealed record Refused(DiagnosticDescriptor Rule, Location At) : LoopShape;
    // Filter and FilterMap follow the same pattern.
}
```

| C# loop shape | Morphir target |
| --- | --- |
| `foreach`, body is `list.Add(f(x))` | `List.map` |
| `foreach`, body is `if (p(x)) list.Add(x)` | `List.filter` |
| `foreach`, body is `if (p(x)) list.Add(f(x))` | `List.filterMap` |
| `foreach`, writes one local | `List.foldl` |
| `foreach`, writes two or more locals | `List.foldl` with a tuple state |
| `while` or `for`, writes locals only | `LetRecursion`; the written locals become parameters |
| Loop with `break` or `return` | `LetRecursion`; the exit becomes the base case |
| Loop that writes a field, a property or an array element | Refused |

`SemanticModel.AnalyzeDataFlow` gives the loop state, `AnalyzeControlFlow` on the loop body gives the exits, and the `IOperation` tree shows writes to something other than a local or a parameter. Admission already refuses a call to code outside the model, so an admitted loop body has no hidden effect. The loop diagnostic means that no transform applies, and it gives the reason.

The F# frontend refuses all loops today (`fsharp.unsupported_loop`). The C# frontend accepts more than the F# frontend does.

### The backend

```text
checked current IR
      │
      ▼
   plan      names, files, SDK calls, the profile and the options
      │
      ▼
   syntax    SyntaxFactory: one CompilationUnitSyntax for each file
      │
      ▼
   layout    NormalizeWhitespace, then the Morphir layout pass
      │
      ▼
   text      the host publishes the files
```

- The checks of the backend of today (`ValueLowering.check`, the collision checks in `Foundation.sources`) become target-side passes in the middle end. Its lowering becomes `plan`. `syntax` and `layout` replace its text emission.
- `layout` is Morphir code. `NormalizeWhitespace` alone puts the close brace of a switch expression on the last arm line.
- The backend core is a pure function with no file or process access. Its dependency set is `Morphir.Models`, `FSharp.Core` and Roslyn. The CLI, the MSBuild target and the source generator all call it.

**Profiles and options.** Upstream `morphir-moonbit` defines a frontend profile as "a named, versioned contract defining an accepted source subset and its lowering semantics", selected with `[frontend] profile`. That is a different thing from a configuration profile, which is a named overlay of settings. The C# backend takes the same idea. The names here are a builder's call.

```toml
[codegen.csharp]
profile = "csharp-imperative-v1"   # or "csharp-functional-v1"
traversal = "functional"           # an option overrides one rule of the profile
```

| Morphir IR | Loop form | Functional form |
| --- | --- | --- |
| `List.map` | `foreach` with `list.Add(f(x))` | `Select` |
| `List.filter` | `foreach` with `if` and `list.Add(x)` | `Where` |
| `List.filterMap` | `foreach` with `if` and `list.Add(f(x))` | A `FilterMap` call of `Morphir.SDK.CSharp` |
| `List.foldl` | `foreach` with accumulator locals | `Aggregate` |
| `LetRecursion`, tail call | `while` with state locals | `while` with state locals (D6) |
| `LetRecursion`, other | Local recursive function | Local recursive function |

Three rules make the transforms match.

- The frontend lowers the loop form and the functional form to the same IR. The profile changes only the C# text.
- The frontend also reads `Select`, `Where`, `Aggregate` and local recursive functions.
- Each written form lowers back to one IR shape. `Where` then `Select` is the written form of `List.map` over `List.filter`, so `List.filterMap` has its own form.
- For each row and each profile, IR to C# to IR gives the same IR. The test suite proves it. The comparison is between the input of the backend, which is the IR after the target-side passes, and the IR that the frontend gives for the written code. It leaves out source locations.

The backend of today refuses `LetRecursion` (rule `local-recursion`) and maps only `List.map` of these functions.

### Options in code

Some users prefer code to configuration (D16). Each handling option thus has two carriers with the same values: a C# attribute and a `morphir.toml` key. The two feed one option record, so no rule has two implementations.

```csharp
[assembly: MorphirModel]                                   // this project is a Morphir model project
[assembly: MorphirEquality(FloatEquality.Clr)]             // the default; shown here only as an example

[MorphirEquality(FloatEquality.Ieee)]                      // for this type, NaN is not equal to NaN
public partial record Quote(double Value);

public static class Pricing
{
    [MorphirLoop(LoopLowering.Recursion)]                  // frontend: this loop becomes recursion, not a fold
    public static decimal Total(IReadOnlyList<decimal> prices)
    {
        decimal sum = 0;
        foreach (var p in prices) sum += p;
        return sum;
    }

    [MorphirEmit(Traversal = TraversalForm.Functional)]    // backend: keep Select and Where for this value
    public static IReadOnlyList<decimal> Taxed(IReadOnlyList<decimal> prices) => ...;
}
```

| Option | It changes | Attribute | Configuration key |
| --- | --- | --- | --- |
| Model project switch | If the analyzer reports | `[assembly: MorphirModel]` | An MSBuild property |
| Loop lowering | The IR that the frontend gives | `[MorphirLoop]` | A `[frontend]` key |
| Traversal form, tail recursion form | Only the C# text that the backend writes | `[MorphirEmit]` | `[codegen.csharp]` keys |
| Float equality | The equality members of a record or a union | `[MorphirEquality]` | A `[codegen.csharp]` key |

The nearest carrier wins.

```text
command line  >  attribute on the member  >  on the type  >  on the assembly  >  morphir.toml  >  profile default
```

These rules apply.

- **Two kinds of option.** A frontend option changes the IR. A backend option changes only the C# text, and the IR stays the same. The table says which kind each option is.
- **The analyzer reads attributes directly.** An attribute is a part of the compilation, so the compiler, the IDE and `morphir compile` see the same value with no more work. A configuration file reaches an analyzer only when the build gives it as an additional file.
- **A backend option goes with the model.** `morphir generate` reads an IR file and has no C# source. The frontend thus writes a backend option as metadata of the declaration, and the backend writes the attribute back. The round trip stays exact. The current IR has annotations on specifications only, so the carrier for a definition is an open point.
- **No option makes refused code accepted.** An attribute selects between supported handlings. A loop that the recognizer refuses stays refused.
- **Equality in authored code.** An attribute cannot change how compiled C# compares two records. For a `partial` record with `[MorphirEquality(FloatEquality.Ieee)]`, the source generator adds the equality members. A record that is not `partial` gets a diagnostic.
- **The attributes need a reference.** They are in a small `netstandard2.0` assembly of the package. The attribute names, the assembly and the order of the carriers are a builder's call.

### Generation in a user build

```text
            ┌────────── backend core: F#, pure, no file or process access ─────────┐
            │   current IR + options ─► files (path, syntax tree)                  │
            └──────▲───────────────────────▲─────────────────────────▲─────────────┘
                   │                       │                         │
          morphir generate          MSBuild target            source generator
          (CLI, Native AOT)         (runs the backend         (the netstandard2.0
                                    before CoreCompile)        build of the core)
```

**MSBuild target.** The package carries a target that runs the backend before `CoreCompile`, writes the files under `obj/`, and adds them to `Compile`. It sets `LangVersion` to 15. It gives a clear diagnostic when the SDK is older than 11. An item with `Generator="MSBuild:Compile"` makes Visual Studio run the target when the model file is saved. The repository has the pattern: `Morphir.Myriad.Build` has an MSBuild task.

**Source generator.** A thin C# adapter reads each IR file from `AdditionalFiles` and calls the same core. The generator references the Roslyn of SDK 11. An older compiler refuses to load it, and that is an accepted limit (D12). The generator assembly targets `netstandard2.0`, which is a Roslyn rule. It reads its input as text, so JSON and Ion text work and binary Ion does not.

Visual Studio uses its own Roslyn, not the Roslyn of the SDK. "SDK 11" also means the Visual Studio version that ships that Roslyn.

### Planned features

Decorators, file-based IR, the IR document tree and incremental compile are not in the codebase yet. The design keeps three constraints for them (D11).

- **Stable node identity, for decorators.** Upstream defines the identity of a node: the semantic node address of `finos/morphir`, `docs/spec/ir/node-addresses.md` (draft `0.1.0-draft.1`). This design uses that contract and adds no scheme of its own. A decorator sidecar has such addresses as its keys. The details are below this list.
- **Module-sized work, for document trees.** A pass does not need the full model in one value. The engine walks one module document at a time. `morphir compile` already reserves `ir.layout = "document-tree"` and `ir.format = "ion"`. They fail today with `compile.unsupported_setting`.
- **Digest keys and recorded reads, for incremental compile.** Each stage result has a key, which is a digest of its inputs. Each stage records what it read. A rule reads other modules through `IPackageView`, so the view can record each lookup. An IR document tree on disk is then the store, and a change rewrites only the files of the changed modules.

Incremental compile is a headline feature, as it is in morphir-elm. It does not need the mechanisms of morphir-elm.

**Node addresses.** An address identifies a position in the normalized model. It does not change with the file, with the JSON or YAML syntax, or with the document tree layout. A Morphir URI is the text form of an address, and it is an absolute IRI.

```text
morphir://ir/pkg/acme/orders?format=4.0.0#/module/domain/value/calculate-total/body/apply/argument
```

```fsharp
/// A sketch of the upstream address model. The step cases come from the upstream role table.
type NodeAddress =
    { Artifact: ArtifactSelector        // a package path or a workspace alias
      Format: FormatVersion             // the semantic contract that reads the steps
      Root: NodeRoot                    // distribution, package, dependency, entry point, module, type or value
      Steps: NodeStep list              // typed roles, for example Body, ApplyArgument, RecordField of Name
      Revision: Revision                // Current, or Pinned of a SHA-256 digest
      Guard: Fingerprint option }       // necessary for an ordered child in an address that is not pinned
```

The contract has these effects on this design.

- **No new IR field.** An address is a semantic position, not an object identity. The current IR stays as it is.
- **A source position is not a part of an address.** It is a location that a result can carry. The diagnostics of the frontend thus keep `SourceLocation`, and a diagnostic of the middle end can carry the URI of its node.
- **A rewrite can make an address stale.** A path with only names follows the node across an edit, but only while each constructor on the path stays. A path through an ordered child, such as a tuple element or a match case, has a guard, which is a fingerprint of the selected children. A rewrite that changes a constructor on the path, or a child below a guarded step, gives `stale_target`. The application log holds the address before and the address after, so a backend can find a decoration through the origin of a node.
- **The format version is a part of an address.** It is data in the address, so D4 holds: no contract of the middle end names a version. But an upgrade step changes the format, and a sidecar with `format=3.0.0` keys does not resolve in the upgraded model. The application list of the upgrade step converts such a key. To resolve the old path against the new model is not sufficient: it cannot tell the same node from the same position. Upstream defines a conversion today only for the V3 `NodeID` strings of morphir-elm.
- **`let` and let-recursion share their roles.** A pass that changes a `LetDefinition` to a `LetRecursion` keeps the address of the named definition.
- **Metadata from C# attributes.** A V4 specification has annotations, and an annotation argument has an address (`/annotation/entry/n/argument/n`). An annotation is thus the first home for a C# attribute. A sidecar with address keys and the proposed V4 `4.1.0` linked metadata (`attributes.facts`) are the other two.

**Proposed upstream changes.** The design needs three changes to the node address draft. They are filed as `finos/morphir` issues [#1178](https://github.com/finos/morphir/issues/1178), [#1179](https://github.com/finos/morphir/issues/1179) and [#1180](https://github.com/finos/morphir/issues/1180) (2026-11-15). Upstream has not accepted them.

1. **`format` names the address line, not the release** (#1178). A line is a major release, written as its first release: the value is `4.0.0` for each 4.x release. Today the reference compares the release exactly, so a `format=4.0.0` key fails on a 4.0.1 artifact, and one node has two IRIs after a minor release. A minor release can only add roles; a changed role meaning starts a new line. The line has one fingerprint rule, the rule of the proposed `4.1.0` revision, and no guard profiles.
2. **Lineage is a declared predicate** (#1179). The replacement node points at the address of the node that it replaces, pinned to the input snapshot. The fact is in `attributes.facts` for a Type or a Value node, and in `$meta.@graph` for a pattern, a definition or a module. The predicate is in a declared vocabulary; the conceptual layer already has the name `derivedFrom`. An upgrade step emits the same list, and an address conversion reads it. The conversion is safe only when the list of the step is complete. A model that is not stored gets no addresses.
3. **The result of a resolve carries the source location** (#1180). The spec already permits it; the reference and the protocol of the Morphir Compatibility Kit do not have it. The V4 `SourceLocation` gets a document reference: on the definition, with an override on a node (a C# partial type is in more than one file), and relative to a declared source root.

```text
before:  body = Apply( Lambda(x, x), Variable price )        a decoration is on .../net/body/apply/argument
after:   body = Variable price                               that address gives stale_target

the replacement node .../net/body states its origin:
derivedFrom = morphir://ir/pkg/acme/orders?format=4.0.0&rev=sha256:<input snapshot>#/module/domain/value/net/body/apply/argument
```

Until upstream decides, the application list (`Before`, `After`, `Rule`) holds the data of change 2 for the passes and for the upgrade steps.

morphir-dotnet has no code for node addresses today. Step 2 adds the typed address and the walk that gives each node its address, because the application list of an upgrade step needs them. Step 3 uses them for the passes. The resolver, the guards, the sidecar format and the `node-address` suite of the Morphir Compatibility Kit belong to the decorators change. This split is a builder's call.

### Delivery

C# is a built-in in all four offerings (D13). The publish gate of `src/Morphir/Morphir.csproj` makes each trim and AOT warning an error at its call site only for `NuGet.*` and `Newtonsoft.Json`. Other packages give one summary warning. Roslyn gives one `IL2104` for `Microsoft.CodeAnalysis`, and the publish passes.

The closure check of the Native AOT executable allows `Microsoft.CodeAnalysis` and `Microsoft.CodeAnalysis.CSharp`. It rejects `Microsoft.CodeAnalysis.Workspaces*`, `Microsoft.CodeAnalysis.Scripting` and `System.Composition*`.

## Delivery steps

The order is a builder's call, and the product owner can change it. The product owner decided on 2026-11-15 to deliver steps 1 to 7 in one OpenSpec change, [add-csharp-language-support](../../../../../openspec/changes/add-csharp-language-support/proposal.md), and one pull request. The step order stays, and each step is a task group that lands as its own commit.

### Step 0: Spike record

- Done on 2026-11-15: the probes are in `spikes/csharp-language-support/`, and the [spike report](../spikes/csharp-language-support.md) has the reproduction steps.
- Open: test the source generator in Visual Studio, Rider and VS Code.
- Open: test generated code on a `net8.0` runtime, if a target below `net10.0` is wanted.

### Step 1: Frontend thin slice

- `Morphir.Lang.CSharp` with the analyzer assembly and the main assembly.
- The frontend reads records, `closed record` sum types, enums and static functions with expression bodies, and writes the current IR.
- The built-in registers in both hosts. `morphir compile` selects it for `.cs` files.
- The Native AOT closure check and the size check of `morphir` change.

### Step 2: Upgrade to the current IR

- `IIrUpgrade`, and the classic to V4 step.
- The typed node address of upstream and the walk that gives each node its address, because the step gives an application list. The resolver, the guards and the sidecars are not in this step.
- `morphir generate` upgrades a classic model before the C# backend. The backend of today already reads the current IR.

### Step 3: Middle-end contracts

- `CurrentModel`, `IModelTransform`, the rule catalog and the engine with its application log.
- `morphir generate` and `morphir compile` run the middle end.
- The checks of the C# backend become the first target-side pass.

### Step 4: Backend on Roslyn

- `plan`, `syntax` and `layout` in `Morphir.Lang.CSharp`. The output has the new dialect: C# 15, plain records, the module shapes and the polyfill file. `Morphir.SDK.CSharp` accepts plain records and plain unions in its equality.
- The target id `csharp` stays. `Morphir.CodeGeneration.CSharp` goes away when the new backend passes its tests.
- `LetRecursion` and the list functions of the transform table.

### Step 5: Loop lowering and matching transforms

- The loop recognizer, the lowering and the loop diagnostics.
- The backend profiles and the round-trip tests for each row of the transform table.
- The option record with its two carriers (attributes and configuration), and the attribute assembly.

### Step 6: Sum type diagnostics and rewriters

- The diagnostics for older encodings of a sum type, and the code fix assembly.
- Unions: the conversion rules, their diagnostics, and the `union` output of the backend.

### Step 7: Generation in a user build

- The `netstandard2.0` build of the core.
- The MSBuild target and the source generator.

### Later

- Decorators, IR document trees and incremental compile, each as its own change.
- A `morphir migrate` command on the upgrade steps.

## Feature status tracking

| Feature | Status | Step | Completion evidence |
| --- | --- | --- | --- |
| Spike record | Done | 0 | The probes are in `spikes/csharp-language-support/`; the [spike record](../spikes/csharp-language-support.md) has the reproduction steps |
| Frontend thin slice | Done | 1 | `morphir compile` makes IR from a C# model in the Native AOT `morphir` and in `dotnet-morphir`: the `frontend-csharp` corpus qualifies on both hosts with equal models and diagnostics (see [Native AOT and dotnet-morphir conformance](#native-aot-and-dotnet-morphir-conformance-2026-11-15)) |
| Upgrade to the current IR | Done | 2 | A classic model generates C# through the upgrade: `ModelUpgradeTests` and the documented `morphir generate --target csharp` example of the middle-end page |
| Middle-end contracts | Done | 3 | `morphir extension list` shows the pass `morphir-dotnet.csharp.admission` with its stage; `--json` reports the passes that ran; BDD scenarios for the passes in `compile` and `generate` |
| Backend on Roslyn | Done | 4 | Generated models compile and run (C# 15, plain records, `closed` and `union` types); the round trip IR to C# to IR passes; `Morphir.CodeGeneration.CSharp` is removed and `Morphir.Lang.CSharp` replaces it |
| Loop lowering and matching transforms | Done | 5 | The round-trip tests pass for each row of the transform table and each profile; the handling options have their attribute and `morphir.toml` carriers |
| Sum type diagnostics and rewriters | Done, IDE not tested | 6 | `MORPHIR1009` reports an older sum type encoding; the code fix test gives a `closed record` that compiles; no host loads the code fix or Workspaces assembly. The fix in a real IDE is not tested |
| Generation in a user build | Done, IDE not tested | 7 | A consumer project builds with the MSBuild target and with the generator on SDK 11.0.100, with the same bytes as the CLI; SDK 10 stops with `MORPHIR2010`. Visual Studio, Rider and VS Code are not tested |

Open items after this change:

- The source generator and the code fix in an IDE (Visual Studio, Rider, VS Code) are not tested.
- A `net8.0` runtime for generated code is not tested. The polyfill file covers targets below `net11.0`, and the tests use `net10.0`.

## Testing approach

Use the `test-manager` skill for the class and the place of each test.

- **Rule examples.** The suite runs the `Example` of each rewrite rule and of each admission rule.
- **Same diagnostics.** For each admission rule, the compiler run and `morphir compile` give the same diagnostic, with the same id and range.
- **Round trip.** For each row of the transform table and each profile, IR to C# to IR gives the same IR.
- **Equality.** Generated code and the authored model agree on equality, for nested values too. With the IEEE option, a record that holds `NaN` is not equal to itself.
- **Native AOT equals JIT.** The frontend and the backend give the same output in the Native AOT `morphir` and in `dotnet-morphir`.
- **Closure and size.** The Native AOT executable holds no Workspaces assembly, and its size stays in a recorded limit.
- **Conformance.** The corpora are Gherkin with the closed step vocabulary of the Morphir Compatibility Kit.

## Open points

| Point | Who decides | Note |
| --- | --- | --- |
| The `[codegen.csharp]` keys `profile` and the rule options | morphir-dotnet proposes, upstream accepts | `[codegen.<target>]` has only `extension` today. A new `morphir.toml` member needs agreement with `finos/morphir`. |
| The node address contract | Upstream decides, morphir-dotnet follows | `docs/spec/ir/node-addresses.md` is a draft (`0.1.0-draft.1`), not a released format. It can change. A `Diagnostic.node` IRI is a planned upstream issue, and the Morphir URI is its natural value. The V4 `SourceLocation` has no file name. |
| The three proposed changes to the node address draft | Upstream decides | See [Planned features](#planned-features). Filed as `finos/morphir` #1178, #1179 and #1180 on 2026-11-15. The design works without them: the application list of the passes and of the upgrade steps holds the lineage. The statements about the fingerprint rules come from the reference code and the spec text; no digest was computed. |
| A `double` in an SDK collection | Step 4 | The SDK compares two `double` elements with `==` today, so `NaN` in a list is not equal to `NaN`. A record field compares with `Equals` by default. The step decides if the collections follow the default. |
| The metadata carrier of a backend option on a definition | Step 5 | The current IR has annotations on specifications only. The proposed linked metadata of V4 `4.1.0` is a possible carrier. |
| The document reference of a source location | Upstream decides | The V4 `SourceLocation` has four numbers and no document. Until it has one, the frontend keeps the file of a node outside the IR. |
| The source generator in Visual Studio | Step 0 | `FSharp.Core` ships beside the generator. A conflict in the .NET Framework process of Visual Studio is possible and not tested. |
| A target framework below `net10.0` | Product owner | The polyfill guards are ready. `Morphir.SDK.CSharp` must target it too. Execution on a `net8.0` runtime is not tested. |
| `LangVersion` above the default of a target framework | Accepted risk | Microsoft does not support it. It works with SDK 11.0.100, with no guarantee. |
| The size limit of the Native AOT `morphir` | Step 1 | Closed on 2026-11-16. The limit is 100,000,000 bytes, in `.agents/aot-optimization.md` and in `NativeAotHostGate.SizeLimitBytes`. `CheckNativeAotHost` checks it in the Linux job of each pull request. The executable with C# is 85,103,768 bytes on `osx-arm64` and 90,773,848 bytes on `linux-x64`. |
| The shared middle end and the F# frontend | Step 2 | The F# frontend can use the source-side middle end. Loop lowering for F# is a different decision. |
| C# in a model project that is not part of the model | Product owner | The rules cannot be suppressed. A declaration-level opt-out, such as a `[MorphirIgnore]` attribute that the analyzer skips and the frontend leaves out (a call to it gives `unresolved-reference`), or an attribute that keeps the signature in the model with an external body (`body.external`), are possible forms. Neither is in the specs. |

## Calls left to the builder

- The names of the contracts, the profiles and the options.
- The extension ids of the built-in and of its passes.
- The Roslyn ids and the IRI scheme of the rules.
- The order of the steps, and how to split a step that is too large for one OpenSpec change.
- The switch that marks a project as a Morphir model project for the analyzer. `[assembly: MorphirModel]` is the proposal; see [Options in code](#options-in-code).

## Evidence

All measurements are from 2026-11-15, on macOS arm64, with .NET SDK 11.0.100 and `Microsoft.CodeAnalysis.CSharp` 5.12.0. The probes are in `spikes/csharp-language-support/`, and the [spike report](../spikes/csharp-language-support.md) has the steps to run them again. All of them ran again from that folder on 2026-11-15, with the same results.

**Roslyn in the real Native AOT `morphir`, `osx-arm64`.**

| Build | Size | `--version` start |
| --- | --- | --- |
| `morphir` today | 47.9 MB | 12.5 ms |
| `morphir` with Roslyn | 76.0 MB | 15.2 ms |

The publish passed with one `IL2104` for `Microsoft.CodeAnalysis`. The probe ran parse and bind of C# 15, one analyzer, data-flow and control-flow analysis, and `SyntaxFactory` output in the host, in 29 ms. The embedded reference assemblies are not in this number. The spike of 2026-11-07 measured 6.3 MB for them. That spike gives 18.1 MB for `morphir`; it is 47.9 MB now.

**Probes, Native AOT against JIT.**

| Probe | IL warnings | Size | Output |
| --- | --- | --- | --- |
| Generate with `SyntaxFactory` and parse back | 0 | 12.0 MB | Same as JIT |
| Generate, parse and bind | 4 `IL2091` | 32.1 MB | Same as JIT |
| One admission analyzer in process, 4 rules | 4 `IL2091` | 34.6 MB | Same 7 diagnostics as JIT |
| Loop classification, 10 loops | 4 `IL2091` | 33.2 MB | Same as JIT |

**C# 15.** `closed record` and `union` compile. A `switch` with no discard arm is exhaustive, and a missing arm gives `CS8509`. C# 14 rejects the two forms. Roslyn has `ITypeSymbol.IsClosed`, `IsUnion`, `UnionCaseTypes`, `SyntaxFactory.UnionDeclaration` and `SyntaxKind.ClosedKeyword`. A `closed record` with its own `Equals` and `GetHashCode` says that `NaN` is not equal to `NaN`.

**Plain equality.** A program on .NET 11.0.100 (JIT) gave these results. `double.NaN == double.NaN` is `false`. Two records with a `NaN` field are equal. Two records with `0.0` and `-0.0` are equal and have equal hashes. Two plain unions with the same case and value are equal with `Equals` and with `EqualityComparer<T>.Default`, and have equal hashes; two with different cases are not equal. `==` on two plain unions is the compile error `CS0019`. Native AOT was not tested for this.

**Unions.** A probe with Roslyn 5.12.0 gave these results. A union is a `struct` with `IsUnion`, `UnionCaseTypes`, a `Value` property and the interface `IUnion`. A conversion from a case to its union is an `IConversionOperation` whose conversion is `Union`. `new Shape(circle)` and `(Shape)circle` compile. A pattern on a union has the union as its input type and the case as its matched type. A missing arm gives `CS8509`. `default` of a union and a `null` arm compile, so the frontend must refuse them. A union with the cases `int` and `string`, with `int?`, with a tuple, with a type parameter and with a different union compiles. A case type that is written two times is a compile error (`CS0111`). A union compiles with an interface list, equality members, the operators `==` and `!=`, and a `switch` on `this` in its body. Upstream (`finos/morphir`, `spec/conceptual/language/scopes/fixtures`): a type and its constructor can have one name, and two custom types of a module cannot share a constructor name.

**Lower targets.** With SDK 11.0.100 and `LangVersion` 15, a `union` and a `closed record` compile for `net8.0` and `netstandard2.0` when the consumer assembly has the `internal` polyfill types. A `net10.0` library and a `net10.0` application, each with its own polyfill types, compile with 0 warnings. The `switch` on the union and on the closed record of the library stays exhaustive in the application. The program runs on .NET 10.0.14.

**Source generator.** The sources of `Morphir.Models` (13,968 lines) and of the C# backend (4,324 lines) build for `netstandard2.0` with one polyfill file, one `open` line in 10 files, and a `System.Text.Json` package reference. A generator with that core and `FSharp.Core` loads and generates under `dotnet build` in SDK 10.0.100 (Roslyn 5.0.0), SDK 10.0.403 (Roslyn 5.9) and SDK 11.0.100 (Roslyn 5.12). The generated code compiles and runs. A generator that is compiled against Roslyn 5.12 does not load in the Roslyn 5.9 compiler.

**Repository.**

- `src/Morphir.CodeGeneration.CSharp`: the backend of today, with text emission and no Roslyn dependency.
- `tests/Morphir.CodeGeneration.CSharp.Tests/Tests.fs`: `InMemoryAdapter`, the only source generator, for tests.
- `src/Morphir.Frontend.FSharp`: the precedent for fixed references, the admission catalog and host-supplied documents.
- [`add-fsharp-morphir-analyzers`](../../../../../openspec/changes/add-fsharp-morphir-analyzers/proposal.md): the same shared rule catalog for F#.
- [Roslyn AOT spike](../spikes/extension-model/roslyn-aot.md) and the [extension model PRD](./morphir-extension-model.md).

**Upstream** (read 2026-11-15).

- `finos/morphir-moonbit`, `CONTEXT.md`: "Current IR", "Frontend profile" and "Configuration profile".
- `finos/morphir-moonbit`, `apps/morphir/README.md`: `[frontend] profile`, `--frontend-profile`, and transforms that need metadata lineage.
- `finos/morphir`, `docs/user-guides/cli-tools/ir-migrate.md`: `morphir migrate` with `--target-version latest`.
- `finos/morphir`, `docs/spec/ir/node-addresses.md` and `docs/spec/ir/fixtures/node-addresses-draft.json`: the semantic node address, the Morphir URI, the positional guard, the V3 `NodeID` conversion and the sidecar envelope. Issue #957 is closed. The fixture has the status "draft with executable subset".
- `finos/morphir-elm`, `src/Morphir/IR/NodeId.elm`: the V3 `NodeID` (`TypeID`, `ValueID`, `ModuleID`, with `ChildByName` and `ChildByIndex` steps).

### Native AOT and dotnet-morphir conformance, 2026-11-15

This section is the evidence of task 9.2. All runs are on macOS arm64 with .NET SDK 11.0.100, for the product of commit `18b8fb20` (version `0.5.0-alpha.3+18b8fb20ecedcd0441c19c79f940cceda35fbe8b`). The loop lowering of tasks 6.2 to 6.4 is not in this commit. Run the checks again when that work is complete.

**Frontend.** `tests/Morphir.Lang.CSharp.Tests/CliAdapterTests.fs` runs the `frontend-csharp` suite through the MCK `cli` adapter. The adapter writes a `.csproj` for the `project file` rows (`CliAdapter.csharpProjectFile`). The suite has 20 cases and 60 variants: 3 input kinds for each case, IR version 4. The test compares the model and the diagnostics of each variant with the `library` adapter. The Native AOT test also compares each variant with the `dotnet-morphir` of the development build.

| Adapter | Implementation | SHA-256 of the implementation | Variants | Verdicts |
| --- | --- | --- | --- | --- |
| `library` | `Morphir.Lang.CSharp` in process | `17f1bf81050c…` | 60 | 60 passed (6 successes, 54 refusals), qualified |
| `cli` | `dotnet-morphir compile` (tool host, development build) | `76af951899f5…` | 60 | 60 passed, qualified |
| `cli` | Native AOT `morphir compile`, `osx-arm64`, 84,689,880 bytes | `6e5033567a42…` | 60 | 60 passed, qualified |

The three reports have the same models and diagnostics for each variant. The only difference is the layout of the JSON text: the CLI writes indented JSON. A normalized list of the observations (each model parsed, the diagnostics and the success flag) has the SHA-256 `2e54e48f4c3411a0f0ad0c1d87ee82bb97e22481ec0792bdc5ee2c52331eb1a4` for each of the three.

**Backend.** The scenarios of `tests/Morphir.BlackBox.Tests/Integration/CSharpGenerationCommand.feature` ran on three targets. The scenario "The generated C# of the Acme.Trading model is the reviewed C#" passed on each of them. The outline "Both hosts write the same C# bytes" now has the Acme.Trading model as a second row. It compares the native host with `dotnet-morphir`, so a pull request checks the full model on the two hosts.

| Target | Host | Scenarios |
| --- | --- | --- |
| Development build | native (default) | 9 passed |
| Development build | `dotnet-morphir` | 9 passed |
| Native AOT executable | native | 7 passed, 2 skipped (the two rows of "Both hosts write the same C# bytes" need all hosts) |

A direct check gave the same result: `morphir generate` (Native AOT) and `dotnet-morphir generate` wrote the same 3 files with the same bytes for `tests/Fixtures/CSharpGeneration/acme-trading/model.json`. The files are equal to `tests/Fixtures/CSharpGeneration/acme-trading/generated`.

Reproduce on macOS arm64:

```console
./build.sh --target PublishExecutable --rid osx-arm64 --configuration Release
dotnet build tests/Morphir.Lang.CSharp.Tests -c Release
dotnet build tests/Morphir.BlackBox.Tests -c Release
MORPHIR_MCK_REPORT_DIR=/tmp/mck-tool \
  dotnet tests/Morphir.Lang.CSharp.Tests/bin/Release/net11.0/Morphir.Lang.CSharp.Tests.dll \
  --sequenced --filter-test-list "C# corpus through the MCK cli adapter (task 9.2)"
MORPHIR_TEST_TARGET=aot MORPHIR_MCK_REPORT_DIR=/tmp/mck-aot \
  dotnet tests/Morphir.Lang.CSharp.Tests/bin/Release/net11.0/Morphir.Lang.CSharp.Tests.dll \
  --sequenced --filter-test-list "C# corpus through the MCK cli adapter (task 9.2)"
FILTER='/*/*/GenerateCThroughTheExistingCommandFeature/*'
dotnet exec tests/Morphir.BlackBox.Tests/bin/Release/net11.0/Morphir.BlackBox.Tests.dll --treenode-filter "$FILTER"
MORPHIR_TEST_HOST=tool dotnet exec tests/Morphir.BlackBox.Tests/bin/Release/net11.0/Morphir.BlackBox.Tests.dll --treenode-filter "$FILTER"
MORPHIR_TEST_TARGET=aot MORPHIR_TEST_LANE=published \
  dotnet exec tests/Morphir.BlackBox.Tests/bin/Release/net11.0/Morphir.BlackBox.Tests.dll --treenode-filter "$FILTER"
```

**Run again with the loop lowering, 2026-11-16.** The same commands ran on commit `d52a9ea2` plus the commits of this evidence, which has the loop lowering of tasks 6.2 to 6.4 and the handling options. The suite now has 21 cases and 63 variants (case `frontend-csharp-0009` refuses a loop that writes an array element).

| Run | Result |
| --- | --- |
| `library` adapter | 63 of 63, qualified |
| `dotnet-morphir` (tool host, development build) | 63 of 63, qualified, the same models and diagnostics as the library |
| Native AOT `morphir`, `osx-arm64` (85,103,768 bytes) | 63 of 63, qualified, the same models and diagnostics as `dotnet-morphir` |
| C# generation feature, native host and `dotnet-morphir` | 9 of 9 on each |
| C# generation feature, Native AOT | 7 passed, 2 skipped (the rows that need all hosts) |
