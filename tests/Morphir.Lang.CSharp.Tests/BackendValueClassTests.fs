// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.BackendValueClassTests

open Expecto
open Morphir.IR.V4
open Morphir.Lang.CSharp.Tests.BackendSupport

// The value classes of type variables (EQL-2): the constraints of comparable and number, the refusal of appendable, and
// the check at generation time that no equality reaches a function type.

let private fn (input: Type) (output: Type) = Type.Function(ta, input, output)

let private documentObject (members: (string * DocumentValue) list) =
    match OrderedMap.ofList members with
    | Ok map -> DocumentObject map
    | Error key -> failwith $"duplicate key {key}"

/// A type variable with the classes in the carrier of EQL-2: the extensions of its type attributes.
let classified (text: string) (classes: string list) =
    let carrier =
        documentObject [ "morphir-dotnet", documentObject [ "classes", DocumentArray [ for c in classes -> DocumentString c ] ] ]

    Type.Variable({ ta with Extensions = Some carrier }, name text)

let private values (definitions: (string * ValueDefinition) list) = library [ "values", moduleOf [] definitions ]
let private withTypes (types: (string * TypeDefinition) list) (definitions: (string * ValueDefinition) list) = library [ "values", moduleOf types definitions ]

let private max' = sdkRef "basics#max"
let private add = sdkRef "basics#add"
let private equal = sdkRef "basics#equal"
let private float (value: float) =
    match FloatValue.create value None with
    | Ok v -> Value.Literal(va, FloatLiteral v)
    | Error message -> failwith message

let private maximum (t: Type) = definition [ "a", t; "b", t ] t (call2 max' (variable "a") (variable "b"))
let private twice (t: Type) = definition [ "x", t ] t (call2 add (variable "x") (variable "x"))
let private same = definition [ "x", variableT "a"; "y", variableT "a" ] boolT (call2 equal (variable "x") (variable "y"))
let private withCallback = recordT [ "callback", fn intT intT ]

let private refusal (model: Morphir.IR.Model.VersionedModel) =
    match refuse model with
    | [ diagnostic ] -> diagnostic
    | other -> failtestf "one diagnostic is expected, not %A" other

[<Tests>]
let tests =
    testList "C# backend on Roslyn: value classes" [
        test "a generic function over comparable has the IComparable constraint and a call gives the model result" {
            let files =
                generate (values [ "maximum", maximum (variableT "comparable"); "largest", definition [] intT (call2 (reference "values#maximum") (int 3) (int 7)) ])

            Expect.stringContains files["p_Acme.Geo/m_ValuesModule.g.cs"] "where TComparable : global::System.IComparable<TComparable>" "the constraint of comparable"
            Expect.equal (run files "Acme.Geo.Values" "Largest" []) "7" "maximum 3 7"
        }

        test "the class of the carrier wins over the name" {
            let key = classified "key" [ "comparable" ]
            let files = generate (values [ "pick", maximum key ])
            Expect.stringContains files["p_Acme.Geo/m_ValuesModule.g.cs"] "where TKey : global::System.IComparable<TKey>" "the constraint from the carrier"
            Expect.equal (compileErrors files) [] "the generic function compiles"
        }

        test "a generic function over number has the INumber constraint and keeps Int and Float results" {
            let files =
                generate (
                    values [
                        "twice", twice (variableT "number")
                        "twice-int", definition [] intT (apply (reference "values#twice") (int 21))
                        "twice-float", definition [] floatT (apply (reference "values#twice") (float 1.25))
                    ]
                )

            Expect.stringContains files["p_Acme.Geo/m_ValuesModule.g.cs"] "where TNumber : global::System.Numerics.INumber<TNumber>" "the constraint of number"
            Expect.equal (run files "Acme.Geo.Values" "TwiceInt" []) "42" "twice 21"
            Expect.equal (run files "Acme.Geo.Values" "TwiceFloat" []) "2.5" "twice 1.25"
        }

        test "a call of a number function with a type that is not Int or Float is refused at the call" {
            let diagnostic =
                refusal (values [ "twice", twice (variableT "number"); "bad", definition [ "s", stringT ] stringT (apply (reference "values#twice") (variable "s")) ])

            Expect.equal diagnostic.Rule "unsupported-numeric" "the rule"
            Expect.stringContains diagnostic.Location "/values/Bad/" "at the call"
        }

        test "a call of a comparable function with a type that is not comparable is refused at the call" {
            let diagnostic =
                refusal (values [ "maximum", maximum (variableT "comparable"); "bad", definition [ "b", boolT ] boolT (call2 (reference "values#maximum") (variable "b") (variable "b")) ])

            Expect.equal diagnostic.Rule "unsupported-comparable" "the rule"
            Expect.stringContains diagnostic.Location "/values/Bad/" "at the call"
        }

        test "a type variable with no class is still refused for an ordering and for arithmetic" {
            Expect.equal (refusal (values [ "maximum", maximum (variableT "a") ])).Rule "unsupported-comparable" "an ordering on a"
            Expect.equal (refusal (values [ "twice", twice (variableT "a") ])).Rule "unsupported-numeric" "arithmetic on a"
        }

        test "a dictionary with a key of a comparable type variable is refused: the SDK has no comparer for it" {
            let key = variableT "comparable"
            let dict = sdk "dict#dict" [ key; intT ]
            let diagnostic = refusal (values [ "one", definition [ "k", key ] dict (call2 (sdkRef "dict#singleton") (variable "k") (int 1)) ])
            Expect.equal diagnostic.Rule "unsupported-comparable" "the rule"
        }

        test "a generic function over appendable or compappend is refused" {
            let first t = definition [ "a", t; "b", t ] t (variable "a")
            Expect.equal (refusal (values [ "first", first (variableT "appendable") ])).Rule "unsupported-appendable" "appendable by name"
            Expect.equal (refusal (values [ "first", first (variableT "compappend") ])).Rule "unsupported-appendable" "compappend by name"
            Expect.equal (refusal (values [ "first", first (classified "x" [ "appendable" ]) ])).Rule "unsupported-appendable" "appendable by carrier"
        }

        test "equality through a type variable on a type that holds a function is refused at the call with the path" {
            let diagnostic =
                refusal (values [ "same", same; "use-same", definition [ "r", withCallback ] boolT (call2 (reference "values#same") (variable "r") (variable "r")) ])

            Expect.equal diagnostic.Rule "unsupported-equality" "the rule"
            Expect.stringContains diagnostic.Location "/values/UseSame/" "at the call"

            Expect.equal
                diagnostic.Message
                "Functions have no admitted Morphir equality: Same compares the values of its type variable TA, and the compared type holds a function type at Callback."
                "the message names the function, its type variable and the path to the function type"
        }

        test "equality reaches a type variable through a chain of calls" {
            let viaSame = definition [ "x", variableT "b"; "y", variableT "b" ] boolT (call2 (reference "values#same") (variable "x") (variable "y"))

            let diagnostic =
                refusal (
                    values [
                        "same", same
                        "via-same", viaSame
                        "use-via", definition [ "f", fn intT intT ] boolT (call2 (reference "values#via-same") (variable "f") (variable "f"))
                    ]
                )

            Expect.equal diagnostic.Rule "unsupported-equality" "the rule"
            Expect.stringContains diagnostic.Location "/values/UseVia/" "at the call of the outer function"
        }

        test "equality through a type variable on a type with no function generates and runs" {
            let files = generate (values [ "same", same; "use-same", definition [] boolT (call2 (reference "values#same") (int 1) (int 2)) ])
            Expect.equal (run files "Acme.Geo.Values" "UseSame" []) "False" "same 1 2"
        }

        test "a type variable that does not reach an equality accepts a function type" {
            let first = definition [ "x", variableT "a"; "y", variableT "a" ] (variableT "a") (variable "x")
            let files = generate (values [ "first", first; "use-first", definition [ "f", fn intT intT ] (fn intT intT) (call2 (reference "values#first") (variable "f") (variable "f")) ])
            Expect.equal (compileErrors files) [] "no equality, so no refusal"
        }

        test "equality on a closed type with a function in a case field is refused with the path" {
            let handler = custom [] [ "on", [ "callback", fn intT intT ]; "off", [] ]

            let diagnostic =
                refusal (withTypes [ "handler", handler ] [ "same", definition [ "h", own "values" "handler" [] ] boolT (call2 equal (variable "h") (variable "h")) ])

            Expect.equal diagnostic.Rule "unsupported-equality" "the rule"
            Expect.stringContains diagnostic.Message "Handler.On.Callback" "the path through the case"
        }

        test "equality on a union with a function in the record of a case is refused with the path" {
            let types = [
                "on", alias [] withCallback
                "off", alias [] (recordT [ "code", intT ])
                "switch", custom [] [ "on", [ "value", own "values" "on" [] ]; "off", [ "value", own "values" "off" [] ] ]
            ]

            let diagnostic =
                refusal (withTypes types [ "same", definition [ "s", own "values" "switch" [] ] boolT (call2 equal (variable "s") (variable "s")) ])

            Expect.equal diagnostic.Rule "unsupported-equality" "the rule"
            Expect.stringContains diagnostic.Message "Switch.On.Value" "the path through the union case"
            Expect.stringContains diagnostic.Message "Callback" "to the function field"
        }
    ]
