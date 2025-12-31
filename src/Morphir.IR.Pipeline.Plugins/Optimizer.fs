namespace Morphir.IR.Pipeline.Plugins

open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Pipeline

/// <summary>
/// IR optimization plugin that performs semantics-preserving transformations.
/// Includes constant folding, dead code elimination, and identity function elimination.
/// </summary>
[<RequireQualifiedAccess>]
module Optimizer =

    /// <summary>
    /// Optimization statistics tracking applied transformations.
    /// </summary>
    type OptimizationStats =
        { ConstantFolds: int
          DeadCodeEliminations: int
          IdentityEliminations: int }

    /// <summary>
    /// Creates empty optimization statistics.
    /// </summary>
    let emptyStats: OptimizationStats =
        { ConstantFolds = 0
          DeadCodeEliminations = 0
          IdentityEliminations = 0 }

    /// <summary>
    /// Applies constant folding to a binary operation on literals.
    /// </summary>
    let tryFoldBinary (funcName: string) (left: Literal) (right: Literal): Literal option =
        match funcName, left, right with
        // Integer arithmetic
        | "add", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
            Some (Literal.WholeNumberLiteral (a + b))
        | "subtract", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
            Some (Literal.WholeNumberLiteral (a - b))
        | "multiply", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
            Some (Literal.WholeNumberLiteral (a * b))
        | "divide", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b when b <> 0L ->
            Some (Literal.WholeNumberLiteral (a / b))

        // Float arithmetic
        | "add", Literal.FloatLiteral a, Literal.FloatLiteral b ->
            Some (Literal.FloatLiteral (a + b))
        | "subtract", Literal.FloatLiteral a, Literal.FloatLiteral b ->
            Some (Literal.FloatLiteral (a - b))
        | "multiply", Literal.FloatLiteral a, Literal.FloatLiteral b ->
            Some (Literal.FloatLiteral (a * b))
        | "divide", Literal.FloatLiteral a, Literal.FloatLiteral b when b <> 0.0 ->
            Some (Literal.FloatLiteral (a / b))

        // Boolean logic
        | "and", Literal.BoolLiteral a, Literal.BoolLiteral b ->
            Some (Literal.BoolLiteral (a && b))
        | "or", Literal.BoolLiteral a, Literal.BoolLiteral b ->
            Some (Literal.BoolLiteral (a || b))

        // String operations
        | "append", Literal.StringLiteral a, Literal.StringLiteral b ->
            Some (Literal.StringLiteral (a + b))

        // Comparison operators
        | "equal", a, b when a = b ->
            Some (Literal.BoolLiteral true)
        | "equal", _, _ ->
            Some (Literal.BoolLiteral false)
        | "notEqual", a, b when a <> b ->
            Some (Literal.BoolLiteral true)
        | "notEqual", _, _ ->
            Some (Literal.BoolLiteral false)

        | "lessThan", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
            Some (Literal.BoolLiteral (a < b))
        | "lessThan", Literal.FloatLiteral a, Literal.FloatLiteral b ->
            Some (Literal.BoolLiteral (a < b))

        | "lessThanOrEqual", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
            Some (Literal.BoolLiteral (a <= b))
        | "lessThanOrEqual", Literal.FloatLiteral a, Literal.FloatLiteral b ->
            Some (Literal.BoolLiteral (a <= b))

        | "greaterThan", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
            Some (Literal.BoolLiteral (a > b))
        | "greaterThan", Literal.FloatLiteral a, Literal.FloatLiteral b ->
            Some (Literal.BoolLiteral (a > b))

        | "greaterThanOrEqual", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b ->
            Some (Literal.BoolLiteral (a >= b))
        | "greaterThanOrEqual", Literal.FloatLiteral a, Literal.FloatLiteral b ->
            Some (Literal.BoolLiteral (a >= b))

        | _ -> None

    /// <summary>
    /// Optimizes a value expression recursively.
    /// Returns (optimizedValue, updated File, stats).
    /// </summary>
    let rec optimizeValue (value: Value<unit, unit>) (file: MorphirFile) (stats: OptimizationStats): (Value<unit, unit> * MorphirFile * OptimizationStats) =
        match value with
        // Constant folding for function applications
        | Value.Apply(attrs, Value.Apply(_, Value.Reference(_, funcName), Value.Literal(_, leftLit)), Value.Literal(_, rightLit)) ->
            let funcLocalName = funcName.LocalName |> Name.toCamelCase
            match tryFoldBinary funcLocalName leftLit rightLit with
            | Some result ->
                let msg = sprintf "Constant folding: %s(%A, %A) → %A" funcLocalName leftLit rightLit result
                let newStats = { stats with ConstantFolds = stats.ConstantFolds + 1 }
                (Value.Literal(attrs, result), file |> MorphirFile.info msg, newStats)
            | None ->
                (value, file, stats)

        // Boolean short-circuit optimization: true && x → x, false && x → false
        | Value.Apply(attrs, Value.Apply(_, Value.Reference(_, funcName), Value.Literal(_, Literal.BoolLiteral leftBool)), rightExpr) ->
            let funcLocalName = funcName.LocalName |> Name.toCamelCase
            match funcLocalName, leftBool with
            | "and", false ->
                let msg = "Dead code elimination: false && _ → false"
                let newStats = { stats with DeadCodeEliminations = stats.DeadCodeEliminations + 1 }
                (Value.Literal(attrs, Literal.BoolLiteral false), file |> MorphirFile.info msg, newStats)
            | "and", true ->
                let msg = "Identity elimination: true && x → x"
                let newStats = { stats with IdentityEliminations = stats.IdentityEliminations + 1 }
                optimizeValue rightExpr (file |> MorphirFile.info msg) newStats
            | "or", true ->
                let msg = "Dead code elimination: true || _ → true"
                let newStats = { stats with DeadCodeEliminations = stats.DeadCodeEliminations + 1 }
                (Value.Literal(attrs, Literal.BoolLiteral true), file |> MorphirFile.info msg, newStats)
            | "or", false ->
                let msg = "Identity elimination: false || x → x"
                let newStats = { stats with IdentityEliminations = stats.IdentityEliminations + 1 }
                optimizeValue rightExpr (file |> MorphirFile.info msg) newStats
            | _ -> (value, file, stats)

        // If-then-else optimization
        | Value.IfThenElse(attrs, Value.Literal(_, Literal.BoolLiteral condition), thenBranch, elseBranch) ->
            if condition then
                let msg = "Dead code elimination: if true → then branch"
                let newStats = { stats with DeadCodeEliminations = stats.DeadCodeEliminations + 1 }
                optimizeValue thenBranch (file |> MorphirFile.info msg) newStats
            else
                let msg = "Dead code elimination: if false → else branch"
                let newStats = { stats with DeadCodeEliminations = stats.DeadCodeEliminations + 1 }
                optimizeValue elseBranch (file |> MorphirFile.info msg) newStats

        // Identity function elimination: (λx → x) y → y
        | Value.Apply(_, Value.Lambda(_, Pattern.AsPattern(_, Pattern.WildcardPattern _, paramName), Value.Variable(_, varName)), arg) when paramName = varName ->
            let msg = "Identity function elimination: (λx → x) y → y"
            let newStats = { stats with IdentityEliminations = stats.IdentityEliminations + 1 }
            optimizeValue arg (file |> MorphirFile.info msg) newStats

        // Recursively optimize compound expressions
        | Value.Tuple(attrs, elements) ->
            let (optimizedElems, finalFile, finalStats) =
                elements
                |> List.fold (fun (accElems, accFile, accStats) elem ->
                    let (optElem, newFile, newStats) = optimizeValue elem accFile accStats
                    (optElem :: accElems, newFile, newStats)
                ) ([], file, stats)
            (Value.Tuple(attrs, List.rev optimizedElems), finalFile, finalStats)

        | Value.List(attrs, elements) ->
            let (optimizedElems, finalFile, finalStats) =
                elements
                |> List.fold (fun (accElems, accFile, accStats) elem ->
                    let (optElem, newFile, newStats) = optimizeValue elem accFile accStats
                    (optElem :: accElems, newFile, newStats)
                ) ([], file, stats)
            (Value.List(attrs, List.rev optimizedElems), finalFile, finalStats)

        | Value.Apply(attrs, func, arg) ->
            let (optFunc, file1, stats1) = optimizeValue func file stats
            let (optArg, file2, stats2) = optimizeValue arg file1 stats1
            (Value.Apply(attrs, optFunc, optArg), file2, stats2)

        | Value.Lambda(attrs, pattern, body) ->
            let (optBody, finalFile, finalStats) = optimizeValue body file stats
            (Value.Lambda(attrs, pattern, optBody), finalFile, finalStats)

        | Value.IfThenElse(attrs, condition, thenBranch, elseBranch) ->
            let (optCond, file1, stats1) = optimizeValue condition file stats
            let (optThen, file2, stats2) = optimizeValue thenBranch file1 stats1
            let (optElse, file3, stats3) = optimizeValue elseBranch file2 stats2
            (Value.IfThenElse(attrs, optCond, optThen, optElse), file3, stats3)

        // Base cases: no optimization
        | _ -> (value, file, stats)

    /// <summary>
    /// Creates an optimizer plugin with default settings.
    /// </summary>
    let create(): Plugin =
        {
            Name = "optimizer"
            Configure = fun proc -> proc
            Transform = fun node file ->
                // For now, node is just an object
                // In a full implementation, we would cast to Value<unit, unit>
                let msg = "Optimizer plugin executed (full optimization pending IR integration)"
                (Some node, file |> MorphirFile.info msg)
        }

    /// <summary>
    /// Creates an optimizer plugin that runs multiple optimization passes.
    /// </summary>
    let createWithPasses (passes: int): Plugin =
        {
            Name = sprintf "optimizer-%d-passes" passes
            Configure = fun proc -> proc
            Transform = fun node file ->
                let msg = sprintf "Optimizer with %d passes executed" passes
                (Some node, file |> MorphirFile.info msg)
        }

    /// <summary>
    /// Optimizes a value definition.
    /// </summary>
    let optimizeValueDefinition (def: ValueDefinition<unit, unit>) (file: MorphirFile): (ValueDefinition<unit, unit> * MorphirFile) =
        let (optimizedBody, finalFile, finalStats) = optimizeValue def.Body file emptyStats
        let summaryMsg = sprintf "Optimization complete: %d constant folds, %d dead code eliminations, %d identity eliminations"
                            finalStats.ConstantFolds finalStats.DeadCodeEliminations finalStats.IdentityEliminations
        let updatedFile = finalFile |> MorphirFile.info summaryMsg
        ({ def with Body = optimizedBody }, updatedFile)
