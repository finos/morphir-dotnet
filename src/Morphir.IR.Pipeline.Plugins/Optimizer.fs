namespace Morphir.IR.Pipeline.Plugins

open System
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Pipeline

/// <summary>
/// IR optimization plugin that performs semantics-preserving transformations. An expression is
/// only simplified when the result matches what generated code would compute at run time.
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
    /// Which rewrites the optimizer applies.
    /// </summary>
    type Options =
        {
            /// <summary>Fold SDK operations on literals</summary>
            FoldConstants: bool
            /// <summary>
            /// Rewrite boolean short-circuits, if on a literal and identity lambdas. These can
            /// remove code a later stage would have rejected, so they can change which models are
            /// accepted.
            /// </summary>
            SimplifyControlFlow: bool
        }

    /// <summary>
    /// Folds constants only. Never changes which models a later stage accepts.
    /// </summary>
    let foldingOnly: Options = { FoldConstants = true; SimplifyControlFlow = false }

    /// <summary>
    /// Applies every rewrite.
    /// </summary>
    let all: Options = { FoldConstants = true; SimplifyControlFlow = true }

    /// <summary>The range of the F# type that Morphir Int maps to.</summary>
    let private inIntRange (value: int64) =
        value >= int64 Int32.MinValue && value <= int64 Int32.MaxValue

    let private wholeResult (compute: int64 -> int64 -> int64) (left: int64) (right: int64) =
        if inIntRange left && inIntRange right then
            let result = compute left right
            if inIntRange result then Some(Literal.WholeNumberLiteral result) else None
        else
            None

    let private floatResult (compute: float -> float -> float) (left: float) (right: float) =
        let result = compute left right
        if Double.IsFinite result then Some(Literal.FloatLiteral result) else None

    let private compareResult (compare: int -> bool) (left: Literal) (right: Literal) =
        match left, right with
        | Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b when inIntRange a && inIntRange b ->
            Some(Literal.BoolLiteral(compare (Operators.compare a b)))
        | Literal.FloatLiteral a, Literal.FloatLiteral b when Double.IsFinite a && Double.IsFinite b ->
            Some(Literal.BoolLiteral(compare (Operators.compare a b)))
        | _ -> None

    let private equalityResult (expected: bool) (left: Literal) (right: Literal) =
        match left, right with
        | Literal.BoolLiteral _, Literal.BoolLiteral _
        | Literal.CharLiteral _, Literal.CharLiteral _
        | Literal.StringLiteral _, Literal.StringLiteral _ -> Some(Literal.BoolLiteral((left = right) = expected))
        | _ -> compareResult (fun order -> (order = 0) = expected) left right

    /// <summary>
    /// Folds an SDK operation on two literals, for operations whose generated form behaves the same.
    /// Returns None for any other function, for mixed literal types, and for a whole number outside
    /// the range of the type Morphir Int maps to, whether an input or the result. Such a literal is
    /// rejected or overflows in generated code, and folding must not change either.
    /// </summary>
    /// <param name="name">The function's fully qualified name</param>
    /// <param name="left">The first argument</param>
    /// <param name="right">The second argument</param>
    let tryFold (name: FQName) (left: Literal) (right: Literal) : Literal option =
        if not (ClassicPackage.isSdk name) then
            None
        else
            match ClassicPackage.sdkName name, left, right with
            | "basics.add", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b -> wholeResult (+) a b
            | "basics.subtract", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b -> wholeResult (-) a b
            | "basics.multiply", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b -> wholeResult (*) a b
            | "basics.divide", Literal.WholeNumberLiteral a, Literal.WholeNumberLiteral b when b <> 0L -> wholeResult (/) a b
            | "basics.add", Literal.FloatLiteral a, Literal.FloatLiteral b -> floatResult (+) a b
            | "basics.subtract", Literal.FloatLiteral a, Literal.FloatLiteral b -> floatResult (-) a b
            | "basics.multiply", Literal.FloatLiteral a, Literal.FloatLiteral b -> floatResult (*) a b
            | "basics.divide", Literal.FloatLiteral a, Literal.FloatLiteral b when b <> 0.0 -> floatResult (/) a b
            | "basics.equal", _, _ -> equalityResult true left right
            | "basics.notequal", _, _ -> equalityResult false left right
            | "basics.lessthan", _, _ -> compareResult (fun order -> order < 0) left right
            | "basics.lessthanorequal", _, _ -> compareResult (fun order -> order <= 0) left right
            | "basics.greaterthan", _, _ -> compareResult (fun order -> order > 0) left right
            | "basics.greaterthanorequal", _, _ -> compareResult (fun order -> order >= 0) left right
            | _ -> None

    /// <summary>
    /// Optimizes a value bottom-up, reaching every nested expression and recording each change on
    /// the file. A rewritten expression keeps the attributes of the expression it replaces.
    /// </summary>
    /// <param name="options">Which rewrites to apply</param>
    /// <param name="location">The value being optimized, for messages</param>
    /// <param name="value">The value to optimize</param>
    /// <param name="file">The file to record changes on</param>
    /// <param name="stats">The statistics so far</param>
    let optimizeValue
        (options: Options)
        (location: string)
        (value: Value<'ta, 'va>)
        (file: VFile)
        (stats: OptimizationStats)
        : Value<'ta, 'va> * VFile * OptimizationStats =
        let note (text: string) (update: OptimizationStats -> OptimizationStats) (currentFile: VFile, currentStats) =
            let message =
                { VMessage.create Info text with
                    Location = Some location
                    Source = Some "optimizer"
                }

            VFile.report message currentFile, update currentStats

        let folded = fun (s: OptimizationStats) -> { s with ConstantFolds = s.ConstantFolds + 1 }
        let deadCode = fun (s: OptimizationStats) -> { s with DeadCodeEliminations = s.DeadCodeEliminations + 1 }
        let identity = fun (s: OptimizationStats) -> { s with IdentityEliminations = s.IdentityEliminations + 1 }

        let simplifyApply attrs fn argument state =
            match fn, argument with
            | Value.Apply(_, Value.Reference(_, name), Value.Literal(_, Literal.BoolLiteral condition)), rest
                when options.SimplifyControlFlow && ClassicPackage.isSdk name ->
                match ClassicPackage.sdkName name, condition with
                | "basics.and", true -> rest, note "Identity elimination: true && x becomes x" identity state
                | "basics.or", false -> rest, note "Identity elimination: false || x becomes x" identity state
                | "basics.and", false ->
                    Value.Literal(attrs, Literal.BoolLiteral false), note "Dead code elimination: false && x becomes false" deadCode state
                | "basics.or", true ->
                    Value.Literal(attrs, Literal.BoolLiteral true), note "Dead code elimination: true || x becomes true" deadCode state
                | _ -> Value.Apply(attrs, fn, argument), state
            | Value.Lambda(_, Pattern.AsPattern(_, Pattern.WildcardPattern _, parameter), Value.Variable(_, used)), rest
                when options.SimplifyControlFlow && parameter = used ->
                rest, note "Identity elimination: an identity lambda becomes its argument" identity state
            | _ -> Value.Apply(attrs, fn, argument), state

        let rec go (current: Value<'ta, 'va>) state =
            match current with
            | Value.Tuple(attrs, items) ->
                let updated, next = many items state
                Value.Tuple(attrs, updated), next
            | Value.List(attrs, items) ->
                let updated, next = many items state
                Value.List(attrs, updated), next
            | Value.Record(attrs, fields) ->
                let updated, next = named fields state
                Value.Record(attrs, updated), next
            | Value.Field(attrs, record, name) ->
                let updated, next = go record state
                Value.Field(attrs, updated, name), next
            | Value.Apply(attrs, fn, argument) ->
                let updatedFn, afterFn = go fn state
                let updatedArgument, afterArgument = go argument afterFn

                match updatedFn, updatedArgument with
                | Value.Apply(_, Value.Reference(_, name), Value.Literal(_, left)), Value.Literal(_, right) when options.FoldConstants ->
                    match tryFold name left right with
                    | Some result ->
                        Value.Literal(attrs, result),
                        note $"Constant folding: {FQName.toString name} of two literals" folded afterArgument
                    | None -> simplifyApply attrs updatedFn updatedArgument afterArgument
                | _ -> simplifyApply attrs updatedFn updatedArgument afterArgument
            | Value.Lambda(attrs, pattern, body) ->
                let updated, next = go body state
                Value.Lambda(attrs, pattern, updated), next
            | Value.LetDefinition(attrs, name, definition, body) ->
                let updatedDefinition, afterDefinition = goDefinition definition state
                let updatedBody, next = go body afterDefinition
                Value.LetDefinition(attrs, name, updatedDefinition, updatedBody), next
            | Value.LetRecursion(attrs, definitions, body) ->
                let updatedDefinitions, afterDefinitions =
                    definitions
                    |> Map.toList
                    |> List.mapFold
                        (fun inner (name, definition) ->
                            let updated, next = goDefinition definition inner
                            (name, updated), next)
                        state

                let updatedBody, next = go body afterDefinitions
                Value.LetRecursion(attrs, Map.ofList updatedDefinitions, updatedBody), next
            | Value.Destructure(attrs, pattern, target, body) ->
                let updatedTarget, afterTarget = go target state
                let updatedBody, next = go body afterTarget
                Value.Destructure(attrs, pattern, updatedTarget, updatedBody), next
            | Value.IfThenElse(attrs, condition, yes, no) ->
                let updatedCondition, afterCondition = go condition state
                let updatedYes, afterYes = go yes afterCondition
                let updatedNo, afterNo = go no afterYes

                match updatedCondition with
                | Value.Literal(_, Literal.BoolLiteral literal) when options.SimplifyControlFlow ->
                    (if literal then updatedYes else updatedNo),
                    note "Dead code elimination: if on a literal becomes its branch" deadCode afterNo
                | _ -> Value.IfThenElse(attrs, updatedCondition, updatedYes, updatedNo), afterNo
            | Value.PatternMatch(attrs, target, cases) ->
                let updatedTarget, afterTarget = go target state

                let updatedCases, next =
                    cases
                    |> List.mapFold
                        (fun inner (pattern, body) ->
                            let updated, afterBody = go body inner
                            (pattern, updated), afterBody)
                        afterTarget

                Value.PatternMatch(attrs, updatedTarget, updatedCases), next
            | Value.UpdateRecord(attrs, target, fields) ->
                let updatedTarget, afterTarget = go target state
                let updatedFields, next = named fields afterTarget
                Value.UpdateRecord(attrs, updatedTarget, updatedFields), next
            | Value.Literal _
            | Value.Constructor _
            | Value.Variable _
            | Value.Reference _
            | Value.FieldFunction _
            | Value.Unit _ -> current, state

        and many items state =
            items |> List.mapFold (fun inner item -> go item inner) state

        and named fields state =
            let updated, next =
                fields
                |> Map.toList
                |> List.mapFold
                    (fun inner (name, item) ->
                        let result, afterItem = go item inner
                        (name, result), afterItem)
                    state

            Map.ofList updated, next

        and goDefinition (definition: ValueDefinition<'ta, 'va>) state =
            let updated, next = go definition.Body state
            { definition with Body = updated }, next

        let result, (finalFile, finalStats) = go value (file, stats)
        result, finalFile, finalStats

    /// <summary>
    /// Creates an optimizer plugin for a classic IR package.
    /// </summary>
    /// <param name="options">Which rewrites to apply</param>
    let create (options: Options) : Plugin<Package.PackageDefinition<'ta, 'va>> =
        Plugin.create "optimizer" (fun package file ->
            let optimized, (finalFile, _) =
                ClassicPackage.mapDefinitions
                    (fun location definition (currentFile, stats) ->
                        let body, nextFile, nextStats = optimizeValue options location definition.Body currentFile stats
                        { definition with Body = body }, (nextFile, nextStats))
                    package
                    (file, emptyStats)

            Some optimized, finalFile)
