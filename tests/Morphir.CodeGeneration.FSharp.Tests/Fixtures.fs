module Morphir.CodeGeneration.FSharp.Tests.Fixtures

open System.Text.Json

let a (items: obj list) : obj = box (List.toArray items)

let n (name: string) =
    a (
        name.Split('_')
        |> Array.map box
        |> Array.toList
    )

let p (path: string) =
    a (
        path.Split('.')
        |> Array.map n
        |> Array.toList
    )

let own moduleName localName =
    a [ p "car.rental"; p moduleName; n localName ]

let sdk moduleName localName =
    a [ p "morphir.s_d_k"; p moduleName; n localName ]

let tRef name arguments =
    a [ box "Reference"; null; name; a arguments ]

let tVar name = a [ box "Variable"; null; n name ]

let tFunction input output =
    a [ box "Function"; null; input; output ]

let tRecord fields =
    a [
        box "Record"
        null
        a (
            fields
            |> List.map (fun (name, typ) -> box {| name = n name; tpe = typ |})
        )
    ]

let tTuple types = a [ box "Tuple"; null; a types ]
let intType = tRef (sdk "basics" "int") []
let stringType = tRef (sdk "string" "string") []
let boolType = tRef (sdk "basics" "bool") []
let unitType = a [ box "Unit"; null ]
let listType typ = tRef (sdk "list" "list") [ typ ]
let variable name = a [ box "Variable"; null; n name ]

let reference moduleName localName =
    a [ box "Reference"; null; own moduleName localName ]

let sdkReference moduleName localName =
    a [ box "Reference"; null; sdk moduleName localName ]

let constructor fq = a [ box "Constructor"; null; fq ]

let literal tag value =
    a [ box "Literal"; null; a [ box tag; value ] ]

let integer value =
    literal "WholeNumberLiteral" (box value)

let str value = literal "StringLiteral" (box value)

let apply fn arguments =
    arguments
    |> List.fold (fun fn arg -> a [ box "Apply"; null; fn; arg ]) fn

let op name left right =
    apply (sdkReference "basics" name) [ left; right ]

let definition inputs output body =
    box
        {|
            inputTypes =
                inputs
                |> List.map (fun (name, typ) -> a [ n name; null; typ ])
                |> a
            outputType = output
            body = body
        |}

let access value =
    box {| access = "Public"; value = value |}

let binding name inputs output body =
    a [ n name; access (definition inputs output body) ]

let typeAlias name parameters typ =
    a [
        n name
        access (
            a [
                box "TypeAliasDefinition"
                a (
                    parameters
                    |> List.map n
                )
                typ
            ]
        )
    ]

let customType name parameters cases =
    a [
        n name
        access (
            a [
                box "CustomTypeDefinition"
                a (
                    parameters
                    |> List.map n
                )
                access (
                    a (
                        cases
                        |> List.map (fun (name, args) ->
                            a [
                                n name
                                a (
                                    args
                                    |> List.map (fun (n', t) -> a [ n n'; t ])
                                )
                            ]
                        )
                    )
                )
            ]
        )
    ]

let wildcard = a [ box "WildcardPattern"; null ]

let bind name =
    a [ box "AsPattern"; null; wildcard; n name ]

let matchCases target cases =
    a [
        box "PatternMatch"
        null
        target
        a (
            cases
            |> List.map (fun (p, v) -> a [ p; v ])
        )
    ]

let model modules =
    let modules =
        modules
        |> List.map (fun (name, types, values) ->
            a [ p name; access (box {| types = a types; values = a values |}) ]
        )
        |> a

    box
        {|
            formatVersion = 3
            distribution = a [ box "Library"; p "car.rental"; a []; box {| modules = modules |} ]
        |}
    |> JsonSerializer.Serialize
