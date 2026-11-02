namespace Morphir.CodeGeneration.FSharp

open Morphir.IR.V4
open Morphir.IR.Pipeline.Plugins

/// Exact folds over native v4 nodes; leaves unproved arithmetic and all control flow intact.
module internal V4Optimizer =
    let private mapValues convert map =
        match
            OrderedMap.toList map
            |> List.map (fun (key, value) -> key, convert value)
            |> OrderedMap.ofList
        with
        | Ok result -> result
        | Error _ -> invalidOp "Mapping values cannot introduce a duplicate key."

    let private operation q =
        if not (Emit.sdk q) then
            None
        else
            match Emit.sdkName q with
            | "basics.add" -> Some ArithmeticOperation.Add
            | "basics.subtract" -> Some ArithmeticOperation.Subtract
            | "basics.multiply" -> Some ArithmeticOperation.Multiply
            | "basics.divide" -> Some ArithmeticOperation.Divide
            | _ -> None

    let private fold q left right =
        operation q
        |> Option.bind (fun operation ->
            match left, right with
            | IntegerLiteral left, IntegerLiteral right ->
                ExactScalarFolding.tryInt32 operation left right
                |> Option.map IntegerLiteral
            | FloatLiteral left, FloatLiteral right ->
                ExactScalarFolding.tryFloat operation left.Value right.Value
                |> Option.bind (fun value ->
                    match FloatValue.create value None with
                    | Ok value -> Some(FloatLiteral value)
                    | Error _ -> None
                )
            | _ -> None
        )

    let rec private expression value =
        let updated =
            match value with
            | Value.Tuple(a, elements) -> Value.Tuple(a, List.map expression elements)
            | Value.List(a, elements) -> Value.List(a, List.map expression elements)
            | Value.Record(a, fields) ->
                Value.Record(
                    a,
                    fields
                    |> List.map (fun (name, value) -> name, expression value)
                )
            | Value.Field(a, target, name) -> Value.Field(a, expression target, name)
            | Value.Apply(a, fn, arg) -> Value.Apply(a, expression fn, expression arg)
            | Value.Lambda(a, p, body) -> Value.Lambda(a, p, expression body)
            | Value.LetDefinition(a, name, value, body) ->
                Value.LetDefinition(
                    a,
                    name,
                    { value with
                        Value = definition value.Value
                    },
                    expression body
                )
            | Value.LetRecursion(a, values, body) ->
                Value.LetRecursion(
                    a,
                    mapValues
                        (fun value ->
                            { value with
                                Value = definition value.Value
                            }
                        )
                        values,
                    expression body
                )
            | Value.Destructure(a, p, target, body) ->
                Value.Destructure(a, p, expression target, expression body)
            | Value.IfThenElse(a, condition, yes, no) ->
                Value.IfThenElse(a, expression condition, expression yes, expression no)
            | Value.PatternMatch(a, target, cases) ->
                Value.PatternMatch(
                    a,
                    expression target,
                    cases
                    |> List.map (fun (p, body) -> p, expression body)
                )
            | Value.UpdateRecord(a, target, fields) ->
                Value.UpdateRecord(
                    a,
                    expression target,
                    fields
                    |> List.map (fun (name, value) -> name, expression value)
                )
            | Value.Literal _
            | Value.Constructor _
            | Value.Variable _
            | Value.Reference _
            | Value.FieldFunction _
            | Value.Unit _
            | Value.Hole _ -> value

        match updated with
        | Value.Apply(a,
                      Value.Apply(_, Value.Reference(_, q), Value.Literal(_, left)),
                      Value.Literal(_, right)) ->
            match fold q left right with
            | Some literal -> Value.Literal(a, literal)
            | None -> updated
        | _ -> updated

    and private definition value =
        match value.Body with
        | ExpressionBody(output, body) ->
            { value with
                Body = ExpressionBody(output, expression body)
            }
        | _ -> value

    let optimize =
        function
        | Library(package, dependencies, value) ->
            let modules =
                value.Modules
                |> mapValues (fun controlled ->
                    let values =
                        controlled.Value.Values
                        |> mapValues (fun controlled ->
                            { controlled with
                                Value =
                                    { controlled.Value with
                                        Value = definition controlled.Value.Value
                                    }
                            }
                        )

                    { controlled with
                        Value =
                            { controlled.Value with
                                Values = values
                            }
                    }
                )

            Library(package, dependencies, { value with Modules = modules })
        | distribution -> distribution
