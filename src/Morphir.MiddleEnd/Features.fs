// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.MiddleEnd

open Morphir.IR
open Morphir.IR.V4

/// <summary>The kinds of value expression of the current IR.</summary>
[<RequireQualifiedAccess>]
type ValueKind =
    | Literal
    | Constructor
    | Tuple
    | List
    | Record
    | Variable
    | Reference
    | Field
    | FieldFunction
    | Apply
    | Lambda
    | LetDefinition
    | LetRecursion
    | Destructure
    | IfThenElse
    | PatternMatch
    | UpdateRecord
    | Unit
    | Hole

    /// <summary>The kind as text, in kebab case: for example <c>let-recursion</c>.</summary>
    member this.Text =
        match this with
        | Literal -> "literal"
        | Constructor -> "constructor"
        | Tuple -> "tuple"
        | List -> "list"
        | Record -> "record"
        | Variable -> "variable"
        | Reference -> "reference"
        | Field -> "field"
        | FieldFunction -> "field-function"
        | Apply -> "apply"
        | Lambda -> "lambda"
        | LetDefinition -> "let-definition"
        | LetRecursion -> "let-recursion"
        | Destructure -> "destructure"
        | IfThenElse -> "if-then-else"
        | PatternMatch -> "pattern-match"
        | UpdateRecord -> "update-record"
        | Unit -> "unit"
        | Hole -> "hole"

/// <summary>The kinds of pattern of the current IR.</summary>
[<RequireQualifiedAccess>]
type PatternKind =
    | Wildcard
    | As
    | Tuple
    | Constructor
    | EmptyList
    | HeadTail
    | Literal
    | Unit

    /// <summary>The kind as text, in kebab case: for example <c>head-tail</c>.</summary>
    member this.Text =
        match this with
        | Wildcard -> "wildcard"
        | As -> "as"
        | Tuple -> "tuple"
        | Constructor -> "constructor"
        | EmptyList -> "empty-list"
        | HeadTail -> "head-tail"
        | Literal -> "literal"
        | Unit -> "unit"

/// <summary>
/// An IR feature: a kind of node that a model can have and that a backend can take or not take. A rule says which
/// features it removes and adds; a backend says which features it cannot take.
/// </summary>
[<RequireQualifiedAccess>]
type IrFeature =
    | Value of ValueKind
    | Pattern of PatternKind
    | ExtensibleRecordType
    | NativeBody
    | ExternalBody
    | IncompleteBody

    /// <summary>The feature as text, for reports and messages: for example <c>value.let-recursion</c>.</summary>
    member this.Text =
        match this with
        | Value kind -> "value." + kind.Text
        | Pattern kind -> "pattern." + kind.Text
        | ExtensibleRecordType -> "type.extensible-record"
        | NativeBody -> "body.native"
        | ExternalBody -> "body.external"
        | IncompleteBody -> "body.incomplete"

    override this.ToString() = this.Text

/// <summary>
/// Traversal of the value expressions of a current-IR value: the children of a value, each with its relative steps in the
/// node address, so a caller can rewrite a node and know where it is.
/// </summary>
[<RequireQualifiedAccess>]
module ValueTraversal =

    /// <summary>The attributes of a value.</summary>
    let attributesOf (value: Value) : ValueAttributes =
        match value with
        | Value.Literal(a, _)
        | Value.Constructor(a, _)
        | Value.Tuple(a, _)
        | Value.List(a, _)
        | Value.Record(a, _)
        | Value.Variable(a, _)
        | Value.Reference(a, _)
        | Value.Field(a, _, _)
        | Value.FieldFunction(a, _)
        | Value.Apply(a, _, _)
        | Value.Lambda(a, _, _)
        | Value.LetDefinition(a, _, _, _)
        | Value.LetRecursion(a, _, _)
        | Value.Destructure(a, _, _, _)
        | Value.IfThenElse(a, _, _, _)
        | Value.PatternMatch(a, _, _)
        | Value.UpdateRecord(a, _, _)
        | Value.Unit a
        | Value.Hole(a, _, _) -> a

    /// <summary>The same value with other attributes on its root.</summary>
    let withAttributes (a: ValueAttributes) (value: Value) : Value =
        match value with
        | Value.Literal(_, x) -> Value.Literal(a, x)
        | Value.Constructor(_, x) -> Value.Constructor(a, x)
        | Value.Tuple(_, x) -> Value.Tuple(a, x)
        | Value.List(_, x) -> Value.List(a, x)
        | Value.Record(_, x) -> Value.Record(a, x)
        | Value.Variable(_, x) -> Value.Variable(a, x)
        | Value.Reference(_, x) -> Value.Reference(a, x)
        | Value.Field(_, x, y) -> Value.Field(a, x, y)
        | Value.FieldFunction(_, x) -> Value.FieldFunction(a, x)
        | Value.Apply(_, x, y) -> Value.Apply(a, x, y)
        | Value.Lambda(_, x, y) -> Value.Lambda(a, x, y)
        | Value.LetDefinition(_, x, y, z) -> Value.LetDefinition(a, x, y, z)
        | Value.LetRecursion(_, x, y) -> Value.LetRecursion(a, x, y)
        | Value.Destructure(_, x, y, z) -> Value.Destructure(a, x, y, z)
        | Value.IfThenElse(_, x, y, z) -> Value.IfThenElse(a, x, y, z)
        | Value.PatternMatch(_, x, y) -> Value.PatternMatch(a, x, y)
        | Value.UpdateRecord(_, x, y) -> Value.UpdateRecord(a, x, y)
        | Value.Unit _ -> Value.Unit a
        | Value.Hole(_, x, y) -> Value.Hole(a, x, y)

    /// <summary>The kind of a value.</summary>
    let kindOf (value: Value) : ValueKind =
        match value with
        | Value.Literal _ -> ValueKind.Literal
        | Value.Constructor _ -> ValueKind.Constructor
        | Value.Tuple _ -> ValueKind.Tuple
        | Value.List _ -> ValueKind.List
        | Value.Record _ -> ValueKind.Record
        | Value.Variable _ -> ValueKind.Variable
        | Value.Reference _ -> ValueKind.Reference
        | Value.Field _ -> ValueKind.Field
        | Value.FieldFunction _ -> ValueKind.FieldFunction
        | Value.Apply _ -> ValueKind.Apply
        | Value.Lambda _ -> ValueKind.Lambda
        | Value.LetDefinition _ -> ValueKind.LetDefinition
        | Value.LetRecursion _ -> ValueKind.LetRecursion
        | Value.Destructure _ -> ValueKind.Destructure
        | Value.IfThenElse _ -> ValueKind.IfThenElse
        | Value.PatternMatch _ -> ValueKind.PatternMatch
        | Value.UpdateRecord _ -> ValueKind.UpdateRecord
        | Value.Unit _ -> ValueKind.Unit
        | Value.Hole _ -> ValueKind.Hole

    let private patternKindOf (pattern: Pattern) : PatternKind =
        match pattern with
        | WildcardPattern _ -> PatternKind.Wildcard
        | AsPattern _ -> PatternKind.As
        | TuplePattern _ -> PatternKind.Tuple
        | ConstructorPattern _ -> PatternKind.Constructor
        | EmptyListPattern _ -> PatternKind.EmptyList
        | HeadTailPattern _ -> PatternKind.HeadTail
        | LiteralPattern _ -> PatternKind.Literal
        | UnitPattern _ -> PatternKind.Unit

    let rec private patternsOf (pattern: Pattern) : Pattern list =
        pattern
        :: (match pattern with
            | AsPattern(_, inner, _) -> patternsOf inner
            | TuplePattern(_, items)
            | ConstructorPattern(_, _, items) -> items |> List.collect patternsOf
            | HeadTailPattern(_, head, tail) -> patternsOf head @ patternsOf tail
            | WildcardPattern _
            | EmptyListPattern _
            | LiteralPattern _
            | UnitPattern _ -> [])

    let private bodyStep = NodeStep.Body

    let private mapDefinition
        (steps: NodeStep list)
        (f: NodeStep list -> Value -> Value)
        (definition: Documented<ValueDefinition>)
        : Documented<ValueDefinition> =
        match definition.Value.Body with
        | ExpressionBody(outputType, body) ->
            { definition with
                Value = { definition.Value with Body = ExpressionBody(outputType, f (steps @ [ bodyStep ]) body) } }
        | NativeBody _
        | ExternalBody _
        | IncompleteBody _ -> definition

    /// <summary>
    /// The same value with <paramref name="f"/> applied to each child that holds a value expression, and the rebuilt value.
    /// <paramref name="f"/> gets the steps of the child relative to the value, in order. A let definition with an
    /// expression body has its body as a child. The patterns and the types of the value are not children.
    /// </summary>
    let mapChildren (f: NodeStep list -> Value -> Value) (value: Value) : Value =
        let one step child = f [ step ] child

        match value with
        | Value.Literal _
        | Value.Constructor _
        | Value.Variable _
        | Value.Reference _
        | Value.FieldFunction _
        | Value.Unit _
        | Value.Hole _ -> value
        | Value.Tuple(a, items) -> Value.Tuple(a, items |> List.mapi (fun i item -> one (NodeStep.TupleElement i) item))
        | Value.List(a, items) -> Value.List(a, items |> List.mapi (fun i item -> one (NodeStep.ListElement i) item))
        | Value.Record(a, fields) -> Value.Record(a, fields |> List.map (fun (n, v) -> n, one (NodeStep.RecordField n) v))
        | Value.Field(a, target, n) -> Value.Field(a, one NodeStep.FieldSubject target, n)
        | Value.Apply(a, func, argument) -> Value.Apply(a, one NodeStep.ApplyFunction func, one NodeStep.ApplyArgument argument)
        | Value.Lambda(a, p, body) -> Value.Lambda(a, p, one NodeStep.LambdaBody body)
        | Value.LetDefinition(a, n, definition, inValue) ->
            Value.LetDefinition(a, n, mapDefinition [ NodeStep.LetDefinition n ] f definition, one NodeStep.LetBody inValue)
        | Value.LetRecursion(a, definitions, inValue) ->
            let mapped =
                OrderedMap.toList definitions
                |> List.map (fun (n, definition) -> n, mapDefinition [ NodeStep.LetDefinition n ] f definition)

            let rebuilt =
                match OrderedMap.ofList mapped with
                | Ok map -> map
                | Error _ -> definitions

            Value.LetRecursion(a, rebuilt, one NodeStep.LetBody inValue)
        | Value.Destructure(a, p, subject, inValue) ->
            Value.Destructure(a, p, one NodeStep.DestructureValue subject, one NodeStep.DestructureBody inValue)
        | Value.IfThenElse(a, condition, thenBranch, elseBranch) ->
            Value.IfThenElse(
                a,
                one NodeStep.IfCondition condition,
                one NodeStep.IfThen thenBranch,
                one NodeStep.IfElse elseBranch
            )
        | Value.PatternMatch(a, subject, cases) ->
            Value.PatternMatch(
                a,
                one NodeStep.PatternMatchSubject subject,
                cases |> List.mapi (fun i (p, body) -> p, one (NodeStep.PatternMatchCaseBody i) body)
            )
        | Value.UpdateRecord(a, target, fields) ->
            Value.UpdateRecord(
                a,
                one NodeStep.UpdateSubject target,
                fields |> List.map (fun (n, v) -> n, one (NodeStep.UpdateField n) v)
            )

    /// <summary>The value expressions that are direct children of a value, in the order of <see cref="mapChildren"/>.</summary>
    let children (value: Value) : Value list =
        let found = ResizeArray<Value>()

        mapChildren
            (fun _ child ->
                found.Add child
                child)
            value
        |> ignore

        List.ofSeq found

    /// <summary>The features of a value and of all value expressions and patterns below it.</summary>
    let rec features (value: Value) : Set<IrFeature> =
        let own = Set.singleton (IrFeature.Value(kindOf value))

        let patterns =
            match value with
            | Value.Lambda(_, p, _) -> patternsOf p
            | Value.Destructure(_, p, _, _) -> patternsOf p
            | Value.PatternMatch(_, _, cases) -> cases |> List.collect (fst >> patternsOf)
            | _ -> []
            |> List.map (patternKindOf >> IrFeature.Pattern)
            |> Set.ofList

        let definitions =
            match value with
            | Value.LetDefinition(_, _, definition, _) -> [ definition ]
            | Value.LetRecursion(_, definitions, _) -> OrderedMap.toList definitions |> List.map snd
            | _ -> []

        let bodies =
            definitions
            |> List.map (fun definition -> definition.Value.Body)
            |> List.choose (function
                | ExpressionBody _ -> None
                | NativeBody _ -> Some IrFeature.NativeBody
                | ExternalBody _ -> Some IrFeature.ExternalBody
                | IncompleteBody _ -> Some IrFeature.IncompleteBody)
            |> Set.ofList

        // The let definitions are children, so their bodies are counted through the children below.
        let below = children value |> List.map features |> Set.unionMany
        Set.unionMany [ own; patterns; bodies; below ]

    /// <summary>The features of the bodies of the values of a module: the value expressions, and the kinds of body.</summary>
    let featuresOfModule (definition: ModuleDefinition) : Set<IrFeature> =
        OrderedMap.toList definition.Values
        |> List.map (fun (_, access) ->
            match access.Value.Value.Body with
            | ExpressionBody(_, body) -> features body
            | NativeBody _ -> Set.singleton IrFeature.NativeBody
            | ExternalBody _ -> Set.singleton IrFeature.ExternalBody
            | IncompleteBody _ -> Set.singleton IrFeature.IncompleteBody)
        |> Set.unionMany
