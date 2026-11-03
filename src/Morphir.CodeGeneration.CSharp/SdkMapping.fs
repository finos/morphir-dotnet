// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.CodeGeneration.CSharp

open Morphir.IR
open System.Threading
open TypeEmission

/// Typed runtime bindings, separate from host packages and compiler services.
module internal SdkMapping =
    type Constraint =
        | Number
        | Comparable
        | Equatable

    type Operation = {
        Module: string
        Method: string
        Inputs: Shape list
        Output: Shape
    }

    let signature (ct: CancellationToken) operation =
        ct.ThrowIfCancellationRequested()
        List.foldBack (fun input output -> Arrow(input, output)) operation.Inputs operation.Output

    let tryResolve fresh constrain name =
        let scalar moduleName local =
            Named(LiteralEmission.sdk moduleName local, [])

        let boolType = scalar "basics" "bool"
        let intType = scalar "basics" "int"
        let floatType = scalar "basics" "float"
        let orderType = scalar "basics" "order"
        let decimalType = scalar "decimal" "decimal"

        let list t =
            Named(LiteralEmission.sdk "list" "list", [ t ])

        let maybe t =
            Named(LiteralEmission.sdk "maybe" "maybe", [ t ])

        let result e t =
            Named(LiteralEmission.sdk "result" "result", [ e; t ])

        let operation moduleName methodName inputs output =
            Some
                {
                    Module = moduleName
                    Method = methodName
                    Inputs = inputs
                    Output = output
                }

        match sdk name with
        | Some("basics", (("add" | "subtract" | "multiply" | "power") as local)) ->
            let n = fresh ()
            constrain Number n

            operation
                "Basics"
                (Map.find
                    local
                    (Map.ofList [
                        "add", "Add"
                        "subtract", "Subtract"
                        "multiply", "Multiply"
                        "power", "Power"
                    ]))
                [ n; n ]
                n
        | Some("basics", (("negate" | "abs") as local)) ->
            let n = fresh ()
            constrain Number n
            operation "Basics" (if local = "abs" then "Abs" else "Negate") [ n ] n
        | Some("basics", "divide") -> operation "Basics" "Divide" [ floatType; floatType ] floatType
        | Some("basics", (("integerdivide" | "modby" | "remainderby") as local)) ->
            operation
                "Basics"
                (Map.find
                    local
                    (Map.ofList [
                        "integerdivide", "IntegerDivide"
                        "modby", "ModBy"
                        "remainderby", "RemainderBy"
                    ]))
                [ intType; intType ]
                intType
        | Some("basics", "tofloat") -> operation "Basics" "ToFloat" [ intType ] floatType
        | Some("basics", (("round" | "floor" | "ceiling" | "truncate") as local)) ->
            operation
                "Basics"
                (local.Substring(0, 1).ToUpperInvariant()
                 + local.Substring(1))
                [ floatType ]
                intType
        | Some("basics", (("isnan" | "isinfinite") as local)) ->
            operation
                "Basics"
                (if local = "isnan" then "IsNaN" else "IsInfinite")
                [ floatType ]
                boolType
        | Some("basics",
               (("compare" | "lessthan" | "lessthanorequal" | "greaterthan" | "greaterthanorequal" | "min" | "max") as local)) ->
            let a = fresh ()
            constrain Comparable a

            let methodName =
                Map.find
                    local
                    (Map.ofList [
                        "compare", "Compare"
                        "lessthan", "LessThan"
                        "lessthanorequal", "LessThanOrEqual"
                        "greaterthan", "GreaterThan"
                        "greaterthanorequal", "GreaterThanOrEqual"
                        "min", "Min"
                        "max", "Max"
                    ])

            let output =
                if local = "compare" then
                    orderType
                elif
                    local = "min"
                    || local = "max"
                then
                    a
                else
                    boolType

            operation "Basics" methodName [ a; a ] output
        | Some("basics", "clamp") ->
            let n = fresh ()
            constrain Number n
            operation "Basics" "Clamp" [ n; n; n ] n
        | Some("basics", (("equal" | "notequal") as local)) ->
            let a = fresh ()
            constrain Equatable a
            operation "Basics" (if local = "equal" then "Equal" else "NotEqual") [ a; a ] boolType
        | Some("list", (("singleton" | "cons" | "append" | "head" | "tail" | "length") as local)) ->
            let a = fresh ()

            match local with
            | "singleton" -> operation "List" "Singleton" [ a ] (list a)
            | "cons" -> operation "List" "Cons" [ a; list a ] (list a)
            | "append" -> operation "List" "Append" [ list a; list a ] (list a)
            | "head" -> operation "List" "Head" [ list a ] (maybe a)
            | "tail" -> operation "List" "Tail" [ list a ] (maybe (list a))
            | _ -> operation "List" "Length" [ list a ] intType
        | Some("list", "map") ->
            let a, b = fresh (), fresh ()
            operation "List" "Map" [ Arrow(a, b); list a ] (list b)
        | Some("maybe", (("map" | "andthen") as local)) ->
            let a, b = fresh (), fresh ()

            operation
                "Maybe"
                (if local = "map" then "Map" else "AndThen")
                [ Arrow(a, (if local = "map" then b else maybe b)); maybe a ]
                (maybe b)
        | Some("result", (("map" | "andthen") as local)) ->
            let e, a, b = fresh (), fresh (), fresh ()

            operation
                "Result"
                (if local = "map" then "Map" else "AndThen")
                [ Arrow(a, (if local = "map" then b else result e b)); result e a ]
                (result e b)
        | Some("dict", local) ->
            let k, v = fresh (), fresh ()
            let dict = Named(LiteralEmission.sdk "dict" "dict", [ k; v ])
            constrain Comparable k

            match local with
            | "empty" -> operation "Dict" "Empty" [] dict
            | "singleton" -> operation "Dict" "Singleton" [ k; v ] dict
            | "fromlist" ->
                operation
                    "Dict"
                    "FromEnumerable"
                    [
                        list (
                            Product[k
                                    v]
                        )
                    ]
                    dict
            | "get" -> operation "Dict" "Get" [ k; dict ] (maybe v)
            | "insert" -> operation "Dict" "Insert" [ k; v; dict ] dict
            | "update" -> operation "Dict" "Update" [ k; Arrow(maybe v, maybe v); dict ] dict
            | "remove" -> operation "Dict" "Remove" [ k; dict ] dict
            | "tolist" ->
                operation
                    "Dict"
                    "ToList"
                    [ dict ]
                    (list (
                        Product[k
                                v]
                    ))
            | "union" -> operation "Dict" "Union" [ dict; dict ] dict
            | _ -> None
        | Some("set", local) ->
            let a = fresh ()
            let set = Named(LiteralEmission.sdk "set" "set", [ a ])
            constrain Comparable a

            match local with
            | "empty" -> operation "Set" "Empty" [] set
            | "singleton" -> operation "Set" "Singleton" [ a ] set
            | "fromlist" -> operation "Set" "FromEnumerable" [ list a ] set
            | "insert" -> operation "Set" "Insert" [ a; set ] set
            | "remove" -> operation "Set" "Remove" [ a; set ] set
            | "tolist" -> operation "Set" "ToList" [ set ] (list a)
            | "union" -> operation "Set" "Union" [ set; set ] set
            | _ -> None
        | Some("decimal", local) ->
            match local with
            | "add"
            | "sub"
            | "mul" ->
                operation
                    "Decimal"
                    (local.Substring(0, 1).ToUpperInvariant()
                     + local.Substring(1))
                    [ decimalType; decimalType ]
                    decimalType
            | "div" -> operation "Decimal" "Div" [ decimalType; decimalType ] (maybe decimalType)
            | "compare" -> operation "Decimal" "Compare" [ decimalType; decimalType ] orderType
            | "tostring" ->
                operation "Decimal" "ToString" [ decimalType ] (scalar "string" "string")
            | _ -> None
        | _ -> None

    type Case = {
        TypeName: FQName
        Parameters: Name list
        Method: string
        Fields: (Name * Shape) list
    }

    let tryCase name =
        let a, e = Name.fromString "a", Name.fromString "e"
        let field parameter = [ Name.fromString "value", Parameter parameter ]

        let case moduleName typeName ps methodName fields =
            Some
                {
                    TypeName = LiteralEmission.sdk moduleName typeName
                    Parameters = ps
                    Method = methodName
                    Fields = fields
                }

        match sdk name with
        | Some("maybe", "just") -> case "maybe" "maybe" [ a ] "Just" (field a)
        | Some("maybe", "nothing") -> case "maybe" "maybe" [ a ] "Nothing" []
        | Some("result", "ok") -> case "result" "result" [ e; a ] "Ok" (field a)
        | Some("result", "err") -> case "result" "result" [ e; a ] "Err" (field e)
        | Some("basics", (("lt" | "eq" | "gt") as local)) ->
            case "basics" "order" [] (local.ToUpperInvariant()) []
        | _ -> None

    let rec private comparer (ct: CancellationToken) location shape =
        ct.ThrowIfCancellationRequested()
        let root = "global::Morphir.SDK.CSharp.MorphirComparers."

        match shape with
        | Named(name, []) when sdk name = Some("basics", "int") ->
            root
            + "Int"
        | Named(name, []) when sdk name = Some("basics", "float") ->
            root
            + "Float"
        | Named(name, []) when sdk name = Some("char", "char") ->
            root
            + "Char"
        | Named(name, []) when sdk name = Some("string", "string") ->
            root
            + "String"
        | Named(name, [ item ]) when sdk name = Some("list", "list") ->
            root
            + "List("
            + comparer ct location item
            + ")"
        | Product items when
            items.Length = 2
            || items.Length = 3
            ->
            root
            + "Tuple("
            + String.concat ", " (List.map (comparer ct location) items)
            + ")"
        | _ ->
            Admission.fail
                "unsupported-comparable"
                location
                "No structural SDK comparer exists for this key type."

    let emit
        (ct: CancellationToken)
        render
        resolve
        location
        (operation: Operation)
        (args: string list)
        =
        ct.ThrowIfCancellationRequested()

        let call target inputs =
            target
            + "("
            + String.concat ", " inputs
            + ")"

        let instance index inputs =
            call
                (args[index]
                 + "."
                 + operation.Method)
                inputs

        let factory inputs =
            call
                (render operation.Output
                 + "."
                 + operation.Method)
                inputs

        match operation.Module, operation.Method with
        | ("Basics" | "Decimal"), methodName ->
            call
                ("global::Morphir.SDK.CSharp."
                 + operation.Module
                 + "."
                 + methodName)
                args
        | "List", "Singleton" -> factory args
        | "List", "Map"
        | "Maybe", _
        | "Result", _
        | "List", "Cons" -> instance 1 [ args[0] ]
        | "List", "Length" ->
            args[0]
            + ".Length"
        | "List", "Head"
        | "List", "Tail"
        | "Dict", "ToList"
        | "Set", "ToList" -> instance 0 []
        | ("Dict" | "Set"), ("Empty" | "Singleton" | "FromEnumerable") ->
            let key =
                match resolve operation.Output with
                | Named(_, key :: _) -> key
                | _ ->
                    Admission.fail
                        "type-mismatch"
                        location
                        "A collection factory requires its concrete output type."

            factory (
                args
                @ [ comparer ct location key ]
            )
        | "Dict", "Get"
        | "Dict", "Remove"
        | "Set", "Insert"
        | "Set", "Remove" -> instance 1 [ args[0] ]
        | "Dict", "Insert"
        | "Dict", "Update" -> instance 2 [ args[0]; args[1] ]
        | _ -> instance 0 [ args[1] ]
