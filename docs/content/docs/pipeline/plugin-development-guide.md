---
title: "Plugin Development Guide"
description: "Comprehensive guide to developing plugins for the Morphir IR pipeline"
weight: 30
---

# Plugin Development Guide

This guide covers everything you need to know to develop plugins for the Morphir IR pipeline. Plugins allow you to extend the pipeline with custom validation, transformation, and diagnostic capabilities.

## Introduction to Plugin Patterns

The Morphir pipeline supports three core plugin patterns:

1. **Validation Plugins** - Verify IR correctness and report errors
2. **Transformation Plugins** - Modify IR to optimize or rewrite code
3. **Diagnostic Plugins** - Generate reports and documentation

Each plugin follows a consistent interface but can implement very different behaviors.

## Plugin Anatomy

Every plugin must implement the `Plugin` interface:

```fsharp
type Plugin =
    { Name: string
      Configure: MorphirProcessor -> MorphirProcessor
      Transform: obj -> MorphirFile -> (obj option * MorphirFile) }
```

### Plugin Components

**Name** - Unique identifier for the plugin (used in logging and diagnostics)

**Configure** - Hook to modify processor settings before execution
- Receives the current `MorphirProcessor`
- Returns updated `MorphirProcessor` with new configuration
- Most plugins return the processor unchanged: `Configure = fun proc -> proc`

**Transform** - Core plugin logic that processes IR nodes
- Receives: IR node (as `obj`) and current `MorphirFile`
- Returns: Transformed node (or `None` to remove) and updated `MorphirFile`
- Use `MorphirFile` to accumulate errors, warnings, and diagnostic data

### MorphirFile API

Plugins communicate results through `MorphirFile`:

```fsharp
// Add diagnostic messages
let file1 = file |> MorphirFile.error "Type mismatch" (Some location)
let file2 = file |> MorphirFile.warn "Unused variable" None
let file3 = file |> MorphirFile.info "Optimization applied"

// Store plugin data
let file4 = file |> MorphirFile.setData "key" (box value)
let data = file.Data.TryFind "key"
```

## Simple Plugin Pattern: Validation

The **TypeValidator** plugin demonstrates validation without IR transformation.

### Example: Type Validator

```fsharp
[<RequireQualifiedAccess>]
module TypeValidator =

    // Type environment for variable lookups
    type TypeEnvironment = Map<Name, Type<unit>>

    let emptyEnv: TypeEnvironment = Map.empty

    // Infer the type of a literal value
    let inferLiteralType (lit: Literal): Type<unit> =
        match lit with
        | Literal.BoolLiteral _ ->
            Type.Reference((), FQName.fromString "Morphir.SDK:Basics:Bool" ":", [])
        | Literal.StringLiteral _ ->
            Type.Reference((), FQName.fromString "Morphir.SDK:Basics:String" ":", [])
        | Literal.WholeNumberLiteral _ ->
            Type.Reference((), FQName.fromString "Morphir.SDK:Basics:Int" ":", [])
        // ... other cases

    // Check structural type equality
    let rec typesEqual (t1: Type<unit>) (t2: Type<unit>): bool =
        match t1, t2 with
        | Type.Variable(_, n1), Type.Variable(_, n2) -> n1 = n2
        | Type.Reference(_, fq1, args1), Type.Reference(_, fq2, args2) ->
            fq1 = fq2 && List.length args1 = List.length args2
            && List.forall2 typesEqual args1 args2
        | Type.Function(_, input1, output1), Type.Function(_, input2, output2) ->
            typesEqual input1 input2 && typesEqual output1 output2
        // ... other cases
        | _ -> false

    // Infer value type and accumulate errors
    let rec inferValueType (env: TypeEnvironment) (value: Value<unit, unit>) (file: MorphirFile):
        (Type<unit> option * MorphirFile) =
        match value with
        | Value.Literal(_, lit) ->
            (Some (inferLiteralType lit), file)

        | Value.Variable(_, name) ->
            match Map.tryFind name env with
            | Some typ -> (Some typ, file)
            | None ->
                let errorMsg = sprintf "Undefined variable: %s" (Name.toTitleCase name)
                (None, file |> MorphirFile.error errorMsg None)

        | Value.IfThenElse(_, condition, thenBranch, elseBranch) ->
            let (condType, file1) = inferValueType env condition file
            let boolType = Type.Reference((), FQName.fromString "Morphir.SDK:Basics:Bool" ":", [])

            // Validate condition is Bool
            let file2 =
                match condType with
                | Some t when typesEqual t boolType -> file1
                | Some t ->
                    let errorMsg = sprintf "If condition must be Bool, got %A" t
                    file1 |> MorphirFile.error errorMsg None
                | None -> file1

            // Infer both branches
            let (thenType, file3) = inferValueType env thenBranch file2
            let (elseType, file4) = inferValueType env elseBranch file3

            // Validate branches have same type
            match thenType, elseType with
            | Some thenT, Some elseT ->
                if typesEqual thenT elseT then
                    (Some thenT, file4)
                else
                    let errorMsg = sprintf "If branches have different types: then = %A, else = %A" thenT elseT
                    (None, file4 |> MorphirFile.error errorMsg None)
            | _ -> (None, file4)

        // ... other cases

    // Create the plugin
    let create(): Plugin =
        {
            Name = "type-validator"
            Configure = fun proc -> proc
            Transform = fun node file ->
                // For now, just report execution
                // Full integration will cast node to Value<unit, unit>
                let msg = "Type validation plugin executed (full validation pending IR integration)"
                (Some node, file |> MorphirFile.info msg)
        }
```

### Key Validation Patterns

**Error Accumulation** - Continue validation even after errors to report all problems:

```fsharp
let (types, finalFile) =
    elements
    |> List.fold (fun (accTypes, accFile) elem ->
        let (elemType, newFile) = inferValueType env elem accFile
        match elemType with
        | Some t -> (t :: accTypes, newFile)
        | None -> (accTypes, newFile)  // Continue despite error
    ) ([], file)
```

**Structural Equality** - Compare types structurally, not by reference:

```fsharp
let rec typesEqual (t1: Type<unit>) (t2: Type<unit>): bool =
    match t1, t2 with
    | Type.Tuple(_, elems1), Type.Tuple(_, elems2) ->
        List.length elems1 = List.length elems2
        && List.forall2 typesEqual elems1 elems2
    // ... other cases
```

## Transformation Plugin Pattern

The **Optimizer** plugin demonstrates IR transformation with statistics tracking.

### Example: IR Optimizer

```fsharp
[<RequireQualifiedAccess>]
module Optimizer =

    // Track applied optimizations
    type OptimizationStats =
        { ConstantFolds: int
          DeadCodeEliminations: int
          IdentityEliminations: int }

    let emptyStats: OptimizationStats =
        { ConstantFolds = 0
          DeadCodeEliminations = 0
          IdentityEliminations = 0 }

    // Constant folding for binary operations
    let tryFoldBinary (funcName: string) (left: Literal) (right: Literal): Literal option =
        match funcName, left, right with
        // Integer arithmetic
        | "add", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
            Some (Literal.WholeNumberLiteral (a + b))
        | "divide", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b when b <> 0L ->
            Some (Literal.WholeNumberLiteral (a / b))

        // Boolean logic
        | "and", Literal.BoolLiteral a, Literal.BoolLiteral b ->
            Some (Literal.BoolLiteral (a && b))
        | "or", Literal.BoolLiteral a, Literal.BoolLiteral b ->
            Some (Literal.BoolLiteral (a || b))

        // Comparison
        | "equal", a, b when a = b ->
            Some (Literal.BoolLiteral true)
        | "equal", _, _ ->
            Some (Literal.BoolLiteral false)

        | _ -> None

    // Recursive optimization with stats
    let rec optimizeValue (value: Value<unit, unit>) (file: MorphirFile) (stats: OptimizationStats):
        (Value<unit, unit> * MorphirFile * OptimizationStats) =
        match value with
        // Constant folding: add(2, 3) → 5
        | Value.Apply(attrs, Value.Apply(_, Value.Reference(_, funcName), Value.Literal(_, leftLit)), Value.Literal(_, rightLit)) ->
            let funcLocalName = funcName.LocalName |> Name.toCamelCase
            match tryFoldBinary funcLocalName leftLit rightLit with
            | Some result ->
                let msg = sprintf "Constant folding: %s(%A, %A) → %A" funcLocalName leftLit rightLit result
                let newStats = { stats with ConstantFolds = stats.ConstantFolds + 1 }
                (Value.Literal(attrs, result), file |> MorphirFile.info msg, newStats)
            | None ->
                (value, file, stats)

        // Dead code elimination: if true → then branch
        | Value.IfThenElse(attrs, Value.Literal(_, Literal.BoolLiteral condition), thenBranch, elseBranch) ->
            if condition then
                let msg = "Dead code elimination: if true → then branch"
                let newStats = { stats with DeadCodeEliminations = stats.DeadCodeEliminations + 1 }
                optimizeValue thenBranch (file |> MorphirFile.info msg) newStats
            else
                let msg = "Dead code elimination: if false → else branch"
                let newStats = { stats with DeadCodeEliminations = stats.DeadCodeEliminations + 1 }
                optimizeValue elseBranch (file |> MorphirFile.info msg) newStats

        // Identity elimination: true && x → x
        | Value.Apply(attrs, Value.Apply(_, Value.Reference(_, funcName), Value.Literal(_, Literal.BoolLiteral leftBool)), rightExpr) ->
            let funcLocalName = funcName.LocalName |> Name.toCamelCase
            match funcLocalName, leftBool with
            | "and", true ->
                let msg = "Identity elimination: true && x → x"
                let newStats = { stats with IdentityEliminations = stats.IdentityEliminations + 1 }
                optimizeValue rightExpr (file |> MorphirFile.info msg) newStats
            | "or", false ->
                let msg = "Identity elimination: false || x → x"
                let newStats = { stats with IdentityEliminations = stats.IdentityEliminations + 1 }
                optimizeValue rightExpr (file |> MorphirFile.info msg) newStats
            | _ -> (value, file, stats)

        // Recursively optimize compound expressions
        | Value.Tuple(attrs, elements) ->
            let (optimizedElems, finalFile, finalStats) =
                elements
                |> List.fold (fun (accElems, accFile, accStats) elem ->
                    let (optElem, newFile, newStats) = optimizeValue elem accFile accStats
                    (optElem :: accElems, newFile, newStats)
                ) ([], file, stats)
            (Value.Tuple(attrs, List.rev optimizedElems), finalFile, finalStats)

        | Value.Apply(attrs, func, arg) ->
            let (optFunc, file1, stats1) = optimizeValue func file stats
            let (optArg, file2, stats2) = optimizeValue arg file1 stats1
            (Value.Apply(attrs, optFunc, optArg), file2, stats2)

        // Base cases: no optimization
        | _ -> (value, file, stats)

    // Multi-pass optimization
    let optimizeValueDefinition (def: ValueDefinition<unit, unit>) (file: MorphirFile):
        (ValueDefinition<unit, unit> * MorphirFile) =
        let (optimizedBody, finalFile, finalStats) = optimizeValue def.Body file emptyStats
        let summaryMsg = sprintf "Optimization complete: %d constant folds, %d dead code eliminations, %d identity eliminations"
                            finalStats.ConstantFolds finalStats.DeadCodeEliminations finalStats.IdentityEliminations
        let updatedFile = finalFile |> MorphirFile.info summaryMsg
        ({ def with Body = optimizedBody }, updatedFile)

    // Create plugin with pass count
    let createWithPasses (passes: int): Plugin =
        {
            Name = sprintf "optimizer-%d-passes" passes
            Configure = fun proc -> proc
            Transform = fun node file ->
                let msg = sprintf "Optimizer with %d passes executed" passes
                (Some node, file |> MorphirFile.info msg)
        }

    let create(): Plugin =
        createWithPasses 1
```

### Key Transformation Patterns

**Bottom-Up Recursion** - Optimize children first, then parent:

```fsharp
let rec optimizeValue value file stats =
    match value with
    | Value.Apply(attrs, func, arg) ->
        let (optFunc, file1, stats1) = optimizeValue func file stats  // Recurse first
        let (optArg, file2, stats2) = optimizeValue arg file1 stats1
        (Value.Apply(attrs, optFunc, optArg), file2, stats2)  // Then construct
```

**Statistics Threading** - Thread stats through recursive calls:

```fsharp
let (optimizedElems, finalFile, finalStats) =
    elements
    |> List.fold (fun (accElems, accFile, accStats) elem ->
        let (optElem, newFile, newStats) = optimizeValue elem accFile accStats
        (optElem :: accElems, newFile, newStats)
    ) ([], file, stats)
```

**Semantics Preservation** - Never change program behavior:

```fsharp
// Safe: 2 + 3 → 5
| "add", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
    Some (Literal.WholeNumberLiteral (a + b))

// Unsafe: Don't fold divide by zero!
| "divide", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b when b <> 0L ->
    Some (Literal.WholeNumberLiteral (a / b))
```

## Diagnostic Plugin Pattern

The **PrettyPrinter** plugin demonstrates generating human-readable output.

### Example: Pretty Printer

```fsharp
[<RequireQualifiedAccess>]
module PrettyPrinter =

    // ANSI color codes
    module Colors =
        let reset = "\x1b[0m"
        let keyword = "\x1b[35m"    // Magenta
        let literal = "\x1b[33m"    // Yellow
        let variable = "\x1b[36m"   // Cyan
        let typeName = "\x1b[32m"   // Green

    // Configuration
    type Config =
        { IndentWidth: int
          ShowTypes: bool
          UseColors: bool
          MaxLineLength: int }

    let defaultConfig: Config =
        { IndentWidth = 2
          ShowTypes = true
          UseColors = false
          MaxLineLength = 80 }

    // Configuration builders
    let withColors (config: Config): Config =
        { config with UseColors = true }

    let withIndent (width: int) (config: Config): Config =
        { config with IndentWidth = width }

    let withoutTypes (config: Config): Config =
        { config with ShowTypes = false }

    // Colorization helper
    let colorize (config: Config) (color: string) (text: string): string =
        if config.UseColors then
            sprintf "%s%s%s" color text Colors.reset
        else
            text

    // Indentation helper
    let indent (level: int) (config: Config): string =
        String.replicate (level * config.IndentWidth) " "

    // Format literal
    let formatLiteral (config: Config) (lit: Literal): string =
        let text =
            match lit with
            | Literal.BoolLiteral b -> if b then "true" else "false"
            | Literal.CharLiteral c -> sprintf "'%c'" c
            | Literal.StringLiteral s -> sprintf "\"%s\"" s
            | Literal.WholeNumberLiteral n -> n.ToString()
            | Literal.FloatLiteral f -> sprintf "%g" f
            | Literal.DecimalLiteral d -> d.ToString()
        colorize config Colors.literal text

    // Format value expression
    let rec formatValue (config: Config) (level: int) (value: Value<unit, unit>): string =
        match value with
        | Value.Literal(_, lit) ->
            formatLiteral config lit

        | Value.Variable(_, name) ->
            colorize config Colors.variable (Name.toCamelCase name)

        | Value.Lambda(_, pattern, body) ->
            sprintf "%s%s %s %s"
                (colorize config Colors.keyword "\\")
                (formatPattern config level pattern)
                (colorize config Colors.keyword "->")
                (formatValue config (level + 1) body)

        | Value.IfThenElse(_, condition, thenBranch, elseBranch) ->
            let sb = StringBuilder()
            sb.Append(colorize config Colors.keyword "if") |> ignore
            sb.Append(" ") |> ignore
            sb.Append(formatValue config level condition) |> ignore
            sb.Append(" ") |> ignore
            sb.Append(colorize config Colors.keyword "then") |> ignore
            sb.AppendLine() |> ignore
            sb.Append(indent (level + 1) config) |> ignore
            sb.Append(formatValue config (level + 1) thenBranch) |> ignore
            sb.AppendLine() |> ignore
            sb.Append(indent level config) |> ignore
            sb.Append(colorize config Colors.keyword "else") |> ignore
            sb.AppendLine() |> ignore
            sb.Append(indent (level + 1) config) |> ignore
            sb.Append(formatValue config (level + 1) elseBranch) |> ignore
            sb.ToString()

        // ... other cases

    // Format value definition
    let formatValueDefinition (config: Config) (name: string) (def: ValueDefinition<unit, unit>): string =
        let sb = StringBuilder()
        sb.Append(colorize config Colors.keyword "let") |> ignore
        sb.Append(" ") |> ignore
        sb.Append(colorize config Colors.variable name) |> ignore

        // Format parameters
        for (paramName, _, paramType) in def.InputTypes do
            sb.Append(" ") |> ignore
            sb.Append(sprintf "(%s" (colorize config Colors.variable (Name.toCamelCase paramName))) |> ignore
            if config.ShowTypes then
                sb.Append(" : ") |> ignore
                sb.Append(formatType config paramType) |> ignore
            sb.Append(")") |> ignore

        // Format return type
        if config.ShowTypes then
            sb.Append(" : ") |> ignore
            sb.Append(formatType config def.OutputType) |> ignore

        sb.Append(" =") |> ignore
        sb.AppendLine() |> ignore
        sb.Append(indent 1 config) |> ignore
        sb.Append(formatValue config 1 def.Body) |> ignore
        sb.ToString()

    // Create plugin
    let createWithConfig (config: Config): Plugin =
        {
            Name = "pretty-printer"
            Configure = fun proc -> proc
            Transform = fun node file ->
                let formatted = sprintf "-- Pretty printed output (indent: %d, colors: %b) --"
                                    config.IndentWidth config.UseColors
                let updatedFile =
                    file
                    |> MorphirFile.info "Pretty printer executed"
                    |> MorphirFile.setData "pretty-printed" (box formatted)
                (Some node, updatedFile)
        }

    let create(): Plugin =
        createWithConfig defaultConfig
```

### Key Diagnostic Patterns

**Builder Pattern** - Fluent configuration API:

```fsharp
let config =
    PrettyPrinter.defaultConfig
    |> PrettyPrinter.withColors
    |> PrettyPrinter.withIndent 4
    |> PrettyPrinter.withoutTypes
```

**StringBuilder for Complex Output** - More efficient than string concatenation:

```fsharp
let sb = StringBuilder()
sb.Append(colorize config Colors.keyword "if") |> ignore
sb.Append(" ") |> ignore
sb.Append(formatValue config level condition) |> ignore
sb.ToString()
```

**Data Storage** - Store results in MorphirFile for later retrieval:

```fsharp
let updatedFile =
    file
    |> MorphirFile.setData "pretty-printed" (box formatted)

// Later retrieve:
match file.Data.TryFind "pretty-printed" with
| Some value -> unbox<string> value
| None -> ""
```

## Advanced Patterns

### Plugin Configuration

Use `Configure` to modify processor settings:

```fsharp
let createVerbose(): Plugin =
    {
        Name = "verbose-validator"
        Configure = fun proc ->
            // Modify processor settings
            { proc with Timeout = Some (TimeSpan.FromMinutes 5.0) }
        Transform = fun node file ->
            // ... validation logic
    }
```

### Plugin Composition

Chain multiple related plugins:

```fsharp
let createOptimizationSuite(): Plugin list =
    [
        Optimizer.create()
        DeadCodeEliminator.create()
        InlineExpander.create()
    ]

// Usage in pipeline:
let pipeline =
    Pipeline.immutable {
        for plugin in OptimizationSuite.createOptimizationSuite() do
            uses plugin
    }
```

### Data Sharing Between Plugins

Use `MorphirFile.Data` for plugin communication:

```fsharp
// Plugin 1: Store analysis results
let analyzePlugin(): Plugin =
    {
        Name = "analyzer"
        Configure = fun proc -> proc
        Transform = fun node file ->
            let analysis = performAnalysis node
            let updatedFile = file |> MorphirFile.setData "analysis-results" (box analysis)
            (Some node, updatedFile)
    }

// Plugin 2: Use analysis results
let optimizePlugin(): Plugin =
    {
        Name = "optimizer"
        Configure = fun proc -> proc
        Transform = fun node file ->
            match file.Data.TryFind "analysis-results" with
            | Some data ->
                let analysis = unbox<AnalysisResults> data
                let optimized = optimizeWithAnalysis node analysis
                (Some optimized, file)
            | None ->
                (Some node, file |> MorphirFile.warn "No analysis data available" None)
    }
```

## Testing Strategies

### Unit Testing Plugins

Test plugins in isolation using Expecto:

```fsharp
[<Tests>]
let optimizerTests =
    testList "Optimizer" [
        test "constant folding should fold addition" {
            let result = Optimizer.tryFoldBinary "add"
                (Literal.WholeNumberLiteral 2L)
                (Literal.WholeNumberLiteral 3L)

            match result with
            | Some (Literal.WholeNumberLiteral n) ->
                Expect.equal n 5L "2 + 3 should equal 5"
            | _ ->
                failtest "expected folded WholeNumberLiteral"
        }

        test "plugin should report optimization stats" {
            let plugin = Optimizer.create()
            let file = MorphirFile.empty
            let value = createTestValue()

            let (resultValue, resultFile) = plugin.Transform (box value) file

            Expect.hasLength resultFile.Messages 1 "should have optimization message"
            Expect.isSome resultValue "should return transformed value"
        }
    ]
```

### Integration Testing

Test plugins in a full pipeline:

```fsharp
[<Tests>]
let pipelineIntegrationTests =
    testList "Pipeline Integration" [
        test "optimization pipeline should apply all transformations" {
            let pipeline =
                Pipeline.immutable {
                    uses (TypeValidator.create())
                    uses (Optimizer.create())
                    uses (PrettyPrinter.create())
                }

            let processor = pipeline.Processor
            let input = createComplexIR()

            let result = processor.Process input

            // Verify all plugins executed
            Expect.hasLength result.Messages 3 "should have 3 plugin messages"
            Expect.isTrue (result.Data.ContainsKey "pretty-printed") "should have pretty-printed output"
        }
    ]
```

### Property-Based Testing

Use FsCheck for semantic preservation:

```fsharp
[<Tests>]
let optimizerPropertyTests =
    testList "Optimizer Properties" [
        testProperty "optimization should preserve semantics" <| fun (input: Value<unit, unit>) ->
            let (optimized, _) = Optimizer.optimizeValue input MorphirFile.empty Optimizer.emptyStats

            // Both should evaluate to same result
            let originalResult = evaluate input
            let optimizedResult = evaluate optimized

            originalResult = optimizedResult
    ]
```

## Best Practices

### 1. Error Accumulation

**Don't** fail on first error:

```fsharp
// BAD: Stops at first error
let validateList elements =
    elements
    |> List.map validateElement
    |> List.find (fun result -> match result with | Error _ -> true | _ -> false)
```

**Do** collect all errors:

```fsharp
// GOOD: Reports all errors
let validateList elements file =
    elements
    |> List.fold (fun accFile elem ->
        let (_, newFile) = validateElement elem accFile
        newFile
    ) file
```

### 2. Immutability

**Don't** mutate shared state:

```fsharp
// BAD: Mutable state
let mutable errorCount = 0

let validate value =
    if isInvalid value then
        errorCount <- errorCount + 1  // Dangerous!
```

**Do** thread state through return values:

```fsharp
// GOOD: Functional state
let validate value (errorCount: int) =
    if isInvalid value then
        (false, errorCount + 1)
    else
        (true, errorCount)
```

### 3. Informative Messages

**Don't** use generic errors:

```fsharp
// BAD: Unhelpful
file |> MorphirFile.error "Validation failed" None
```

**Do** provide context:

```fsharp
// GOOD: Actionable
let errorMsg = sprintf "Type mismatch in function '%s': expected %s, got %s"
                   functionName expectedType actualType
file |> MorphirFile.error errorMsg (Some location)
```

### 4. Performance

**Don't** traverse IR multiple times:

```fsharp
// BAD: Multiple passes
let validate value =
    let hasErrors = checkForErrors value
    let hasWarnings = checkForWarnings value
    (hasErrors, hasWarnings)
```

**Do** combine traversals:

```fsharp
// GOOD: Single pass
let validate value =
    let rec check v =
        match v with
        | ValidCase -> (false, false)
        | ErrorCase -> (true, false)
        | WarningCase -> (false, true)
        | Compound children ->
            children |> List.fold (fun (e, w) child ->
                let (e', w') = check child
                (e || e', w || w')
            ) (false, false)
    check value
```

### 5. Documentation

Document plugin behavior comprehensively:

```fsharp
/// <summary>
/// Type validation plugin that checks Morphir IR type correctness.
/// Reports type errors with source positions and accumulates all errors.
/// </summary>
/// <remarks>
/// This plugin performs full type inference and checking for Morphir IR values.
/// It validates:
/// - Variable references are in scope
/// - Function applications have matching argument types
/// - If-then-else branches have consistent types
/// - List elements have homogeneous types
/// </remarks>
[<RequireQualifiedAccess>]
module TypeValidator =
    // ...
```

## Summary

The Morphir plugin system provides a flexible foundation for extending IR processing:

- **Validation plugins** verify correctness and report comprehensive errors
- **Transformation plugins** optimize and rewrite IR while preserving semantics
- **Diagnostic plugins** generate reports and documentation

Key principles:
- Accumulate all errors, don't fail fast
- Thread state immutably through MorphirFile
- Provide informative, actionable error messages
- Test plugins in isolation and integration
- Document behavior and assumptions

## Next Steps

- Explore the [TypeValidator source](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.IR.Pipeline.Plugins/TypeValidator.fs)
- Review [Optimizer examples](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.IR.Pipeline.Plugins/Optimizer.fs)
- Study [PrettyPrinter formatting](https://github.com/finos/morphir-dotnet/blob/main/src/Morphir.IR.Pipeline.Plugins/PrettyPrinter.fs)
- Read the [Pipeline Guide](../pipeline-guide) for integration patterns
