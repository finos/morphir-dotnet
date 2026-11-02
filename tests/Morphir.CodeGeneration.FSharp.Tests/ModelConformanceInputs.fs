module Morphir.CodeGeneration.FSharp.Tests.ModelConformanceInputs

open System.Text.Json
open Morphir.IR
open Morphir.IR.Model
open Morphir.Testing

module F = Morphir.CodeGeneration.FSharp.Tests.Fixtures

let ok =
    function
    | Ok value -> value
    | Result.Error error -> failwithf "%A" error

let name = Name.fromString
let package = PackageName(Path [ name "car"; name "rental" ])
let modulePath text = ModulePath(Path [ name text ])

let own moduleName local = {
    PackagePath = package
    ModulePath = modulePath moduleName
    LocalName = name local
}

let sdk moduleName local = {
    PackagePath = PackageName(Path [ name "morphir"; Name [ "s"; "d"; "k" ] ])
    ModulePath = modulePath moduleName
    LocalName = name local
}

let ordered entries =
    Morphir.IR.V4.OrderedMap.ofList entries
    |> ok

let documented value : Morphir.IR.V4.Documented<_> = { Doc = None; Value = value }

let publicValue value : Morphir.IR.V4.AccessControlled<_> = {
    Access = Morphir.IR.V4.Public
    Value = value
}

let v4 model = {
    SourceVersion =
        SourceFormatVersion.parse "4.0.0"
        |> ok
    Distribution = DistributionModel.V4 model
}

let classic json =
    use document = JsonDocument.Parse(json: string)

    let decoded =
        Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement
        |> ok

    {
        SourceVersion =
            SourceFormatVersion.parse "3"
            |> ok
        Distribution = DistributionModel.Classic decoded.Distribution
    }

let scalar version input =
    let moduleName, localName =
        match input with
        | GenerationScalars.Integer _ -> "basics", "int"
        | GenerationScalars.Decimal _ -> "decimal", "decimal"
        | GenerationScalars.Float _ -> "basics", "float"
        | GenerationScalars.Character _ -> "char", "char"
        | GenerationScalars.Text _ -> "string", "string"
        | GenerationScalars.Boolean _ -> "basics", "bool"

    let reference = sdk moduleName localName

    if version = "3" then
        let attributes = Unchecked.defaultof<JsonElement>

        let literal =
            match input with
            | GenerationScalars.Integer value -> Morphir.IR.Classic.WholeNumberLiteral(int64 value)
            | GenerationScalars.Decimal lexeme ->
                Morphir.IR.Classic.DecimalLiteral(
                    System.Decimal.Parse(
                        lexeme,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                )
            | GenerationScalars.Float value -> Morphir.IR.Classic.FloatLiteral value
            | GenerationScalars.Character value -> Morphir.IR.Classic.CharLiteral value[0]
            | GenerationScalars.Text value -> Morphir.IR.Classic.StringLiteral value
            | GenerationScalars.Boolean value -> Morphir.IR.Classic.BoolLiteral value

        let value: Morphir.IR.Classic.ValueDefinition<JsonElement, JsonElement> = {
            InputTypes = []
            OutputType = Morphir.IR.Classic.Type.Reference(attributes, reference, [])
            Body = Morphir.IR.Classic.Value.Literal(attributes, literal)
        }

        let controlled value : Morphir.IR.Classic.AccessControlled.AccessControlled<_> = {
            Access = Morphir.IR.Classic.AccessControlled.Public
            Value = value
        }

        let module': Morphir.IR.Classic.Module.ModuleDefinition<JsonElement, JsonElement> = {
            Types = Map.empty
            Values =
                Map.ofList[name "value",
                           controlled (Morphir.IR.Classic.Documented.WithoutDocumentation value)]
            Doc = None
        }

        {
            SourceVersion =
                SourceFormatVersion.parse version
                |> ok
            Distribution =
                DistributionModel.Classic(
                    Morphir.IR.Classic.Distribution.Library(
                        package,
                        Map.empty,
                        {
                            Modules = Map.ofList[modulePath "literals", controlled module']
                        }
                    )
                )
        }
    else
        let literal =
            match input with
            | GenerationScalars.Integer value -> Morphir.IR.V4.IntegerLiteral value
            | GenerationScalars.Decimal lexeme ->
                Morphir.IR.V4.DecimalLiteral(
                    Morphir.IR.V4.DecimalLexeme.create lexeme
                    |> ok
                )
            | GenerationScalars.Float value ->
                Morphir.IR.V4.FloatLiteral(
                    Morphir.IR.V4.FloatValue.create value None
                    |> ok
                )
            | GenerationScalars.Character value -> Morphir.IR.V4.CharLiteral value
            | GenerationScalars.Text value -> Morphir.IR.V4.StringLiteral value
            | GenerationScalars.Boolean value -> Morphir.IR.V4.BoolLiteral value

        let value: Morphir.IR.V4.ValueDefinition = {
            InputTypes = []
            Body =
                Morphir.IR.V4.ExpressionBody(
                    Morphir.IR.V4.Type.Reference(Morphir.IR.V4.TypeAttributes.empty, reference, []),
                    Morphir.IR.V4.Value.Literal(Morphir.IR.V4.ValueAttributes.empty, literal)
                )
        }

        let module': Morphir.IR.V4.ModuleDefinition = {
            Types = ordered []
            Values = ordered[name "value", publicValue (documented value)]
            Doc = None
        }

        Morphir.IR.V4.Library(
            package,
            ordered [],
            {
                Modules = ordered[modulePath "literals", publicValue module']
            }
        )
        |> v4

let typeStructure version =
    if version = "3" then
        F.model [
            "types",
            [
                F.typeAlias "box" [ "a" ] (F.tRecord [ "value", F.tVar "a" ])
                F.customType "envelope" [ "a" ] [
                    "wrapped", [ "box", F.tRef (F.own "types" "box") [ F.tVar "a" ] ]
                ]
                F.typeAlias "transform" [ "a" ] (F.tFunction (F.tVar "a") (F.tVar "a"))
                F.typeAlias "unit_payload" [] (F.tTuple [ F.intType; F.unitType ])
                F.a [
                    F.n "hidden"
                    box
                        {|
                            access = "Private"
                            value = F.a [ box "TypeAliasDefinition"; F.a []; F.intType ]
                        |}
                ]
            ],
            []
            "higher", [ F.typeAlias "boxed" [] (F.tRef (F.own "types" "box") [ F.intType ]) ], []
        ]
        |> classic
    else
        let attributes = Morphir.IR.V4.TypeAttributes.empty
        let variable = Morphir.IR.V4.Type.Variable(attributes, name "a")
        let intType = Morphir.IR.V4.Type.Reference(attributes, sdk "basics" "int", [])
        let box = Morphir.IR.V4.Type.Reference(attributes, own "types" "box", [ variable ])

        let definitions = [
            name "box",
            Morphir.IR.V4.TypeAliasDefinition(
                [ name "a" ],
                Morphir.IR.V4.Type.Record(attributes, [ name "value", variable ])
            )
            name "envelope",
            Morphir.IR.V4.CustomTypeDefinition(
                [ name "a" ],
                Morphir.IR.V4.Public,
                ordered[name "wrapped", [ name "box", box ]]
            )
            name "transform",
            Morphir.IR.V4.TypeAliasDefinition(
                [ name "a" ],
                Morphir.IR.V4.Type.Function(attributes, variable, variable)
            )
            name "unitPayload",
            Morphir.IR.V4.TypeAliasDefinition(
                [],
                Morphir.IR.V4.Type.Tuple(
                    attributes,
                    [ intType; Morphir.IR.V4.Type.Unit attributes ]
                )
            )
        ]

        let hidden: Morphir.IR.V4.AccessControlled<_> = {
            Access = Morphir.IR.V4.Private
            Value = documented (Morphir.IR.V4.TypeAliasDefinition([], intType))
        }

        let types: Morphir.IR.V4.ModuleDefinition = {
            Types =
                (name "hidden", hidden)
                :: (definitions
                    |> List.map (fun (name, definition) ->
                        name, publicValue (documented definition)
                    ))
                |> ordered
            Values = ordered []
            Doc = None
        }

        let higher: Morphir.IR.V4.ModuleDefinition = {
            Types =
                ordered[name "boxed",
                        publicValue (
                            documented (
                                Morphir.IR.V4.TypeAliasDefinition(
                                    [],
                                    Morphir.IR.V4.Type.Reference(
                                        attributes,
                                        own "types" "box",
                                        [ intType ]
                                    )
                                )
                            )
                        )]
            Values = ordered []
            Doc = None
        }

        Morphir.IR.V4.Library(
            package,
            ordered [],
            {
                Modules =
                    ordered[modulePath "higher", publicValue higher
                            modulePath "types", publicValue types]
            }
        )
        |> v4

let forVersion version model =
    if version = "3" then
        model
    else
        Morphir.CodeGeneration.FSharp.Tests.ClassicToV4TestAdapter.promote model

let expressionCore version =
    let pattern tag fields =
        F.a (
            box tag
            :: null
            :: fields
        )

    let constructorPattern local patterns =
        pattern "ConstructorPattern" [ F.own "expressions" local; F.a patterns ]

    let record value =
        F.a [ box "Record"; null; F.a [ F.a [ F.n "score"; F.integer value ] ] ]

    let outcome = F.tRef (F.own "expressions" "outcome") []
    let metric = F.tRef (F.own "expressions" "metric") []

    let json =
        F.model [
            "expressions",
            [
                F.customType "outcome" [] [
                    "good", [ "amount", F.intType; "label", F.stringType ]
                    "bad", []
                ]
                F.typeAlias "metric" [] (F.tRecord [ "score", F.intType ])
            ],
            [
                F.binding
                    "choose"
                    [ "n", F.intType ]
                    outcome
                    (F.a [
                        box "IfThenElse"
                        null
                        F.op "greater_than_or_equal" (F.variable "n") (F.integer 0)
                        F.apply (F.constructor (F.own "expressions" "good")) [
                            F.variable "n"
                            F.str "ok"
                        ]
                        F.constructor (F.own "expressions" "bad")
                    ])
                F.binding
                    "partial_good"
                    []
                    (F.tFunction F.stringType outcome)
                    (F.apply (F.constructor (F.own "expressions" "good")) [ F.integer 7 ])
                F.binding
                    "pair"
                    []
                    (F.tTuple [ F.intType; F.stringType ])
                    (F.a [ box "Tuple"; null; F.a [ F.integer 3; F.str "hi" ] ])
                F.binding "unit_value" [] F.unitType (F.a [ box "Unit"; null ])
                F.binding
                    "report"
                    [ "n", F.intType ]
                    F.stringType
                    (F.matchCases (F.apply (F.reference "expressions" "choose") [ F.variable "n" ]) [
                        constructorPattern "good" [
                            pattern "LiteralPattern" [ F.a [ box "WholeNumberLiteral"; box 0 ] ]
                            F.wildcard
                        ],
                        F.str "zero"
                        constructorPattern "good" [ F.wildcard; F.wildcard ], F.str "good"
                        constructorPattern "bad" [], F.str "bad"
                    ])
                F.binding
                    "mapped"
                    []
                    (F.listType F.intType)
                    (F.apply (F.sdkReference "list" "map") [
                        F.a [
                            box "Lambda"
                            null
                            F.bind "x"
                            F.op "add" (F.variable "x") (F.integer 1)
                        ]
                        F.a [ box "List"; null; F.a [ F.integer 1; F.integer 2 ] ]
                    ])
                F.binding "record" [] metric (record 3)
                F.binding "anonymous" [] (F.tRecord [ "score", F.intType ]) (record 4)
                F.binding
                    "score_of"
                    []
                    (F.tFunction metric F.intType)
                    (F.a [ box "FieldFunction"; null; F.n "score" ])
                F.binding
                    "pair_text"
                    [ "pair", F.tTuple [ F.intType; F.stringType ] ]
                    F.stringType
                    (F.matchCases (F.variable "pair") [
                        pattern "TuplePattern" [ F.a [ F.bind "n"; F.bind "label" ] ],
                        F.variable "label"
                    ])
                F.binding
                    "unit_to_zero"
                    [ "u", F.unitType ]
                    F.intType
                    (F.matchCases (F.variable "u") [ pattern "UnitPattern" [], F.integer 0 ])
                F.binding
                    "head_or_zero"
                    [ "items", F.listType F.intType ]
                    F.intType
                    (F.matchCases (F.variable "items") [
                        pattern "EmptyListPattern" [], F.integer 0
                        pattern "HeadTailPattern" [ F.bind "h"; F.wildcard ], F.variable "h"
                    ])
            ]
        ]

    classic json
    |> forVersion version

let recordBilling version =
    Morphir.Testing.ModelResources.read "record-billing-v3.json"
    |> classic
    |> forVersion version

let inferredLiteral consistent =
    let output =
        Morphir.IR.V4.Type.Reference(Morphir.IR.V4.TypeAttributes.empty, sdk "basics" "int", [])

    let inferred =
        if consistent then
            output
        else
            Morphir.IR.V4.Type.Reference(
                Morphir.IR.V4.TypeAttributes.empty,
                sdk "string" "string",
                []
            )

    let attributes =
        { Morphir.IR.V4.ValueAttributes.empty with
            InferredType = Some inferred
            Source =
                Some
                    {
                        StartLine = 7
                        StartColumn = 3
                        EndLine = 7
                        EndColumn = 5
                    }
        }

    let value: Morphir.IR.V4.ValueDefinition = {
        InputTypes = []
        Body =
            Morphir.IR.V4.ExpressionBody(
                output,
                Morphir.IR.V4.Value.Literal(attributes, Morphir.IR.V4.IntegerLiteral 42I)
            )
    }

    let module': Morphir.IR.V4.ModuleDefinition = {
        Types = ordered []
        Values = ordered [ name "value", publicValue (documented value) ]
        Doc = None
    }

    Morphir.IR.V4.Library(
        package,
        ordered [],
        {
            Modules = ordered [ modulePath "literals", publicValue module' ]
        }
    )
    |> v4

let inferredRecord declaredNamed =
    let attributes = Morphir.IR.V4.TypeAttributes.empty
    let intType = Morphir.IR.V4.Type.Reference(attributes, sdk "basics" "int", [])
    let recordType = Morphir.IR.V4.Type.Record(attributes, [ name "score", intType ])

    let aliasType =
        Morphir.IR.V4.Type.Reference(attributes, own "inferred" "metric", [])

    let declared, inferred =
        if declaredNamed then
            aliasType, recordType
        else
            recordType, aliasType

    let valueAttributes =
        { Morphir.IR.V4.ValueAttributes.empty with
            InferredType = Some inferred
        }

    let body =
        Morphir.IR.V4.Value.Record(
            valueAttributes,
            [
                name "score",
                Morphir.IR.V4.Value.Literal(
                    Morphir.IR.V4.ValueAttributes.empty,
                    Morphir.IR.V4.IntegerLiteral 9I
                )
            ]
        )

    let value: Morphir.IR.V4.ValueDefinition = {
        InputTypes = []
        Body = Morphir.IR.V4.ExpressionBody(declared, body)
    }

    let module': Morphir.IR.V4.ModuleDefinition = {
        Types =
            ordered [
                name "metric",
                publicValue (documented (Morphir.IR.V4.TypeAliasDefinition([], recordType)))
            ]
        Values = ordered [ name "value", publicValue (documented value) ]
        Doc = None
    }

    Morphir.IR.V4.Library(
        package,
        ordered [],
        {
            Modules = ordered [ modulePath "inferred", publicValue module' ]
        }
    )
    |> v4

let lexicalCaptureJson () =
    let capture = F.op "add" (F.reference "capture" "rate") (F.variable "rate")

    let argument =
        F.a [ box "AsPattern"; null; F.a [ box "WildcardPattern"; null ]; F.n "rate" ]

    let local =
        F.a [
            box "LetDefinition"
            null
            F.n "rate"
            F.definition [] F.intType (F.integer 7)
            capture
        ]

    let lambda = F.apply (F.a [ box "Lambda"; null; argument; capture ]) [ F.integer 7 ]

    let matched =
        F.a [ box "PatternMatch"; null; F.integer 7; F.a [ F.a [ argument; capture ] ] ]

    let destructured = F.a [ box "Destructure"; null; argument; F.integer 7; capture ]

    let recursiveBody =
        F.a [
            box "IfThenElse"
            null
            F.op "equal" (F.variable "n") (F.integer 0)
            F.reference "capture" "rate"
            F.apply (F.variable "rate") [ F.op "subtract" (F.variable "n") (F.integer 1) ]
        ]

    let recursive =
        F.a [
            box "LetRecursion"
            null
            F.a [ F.a [ F.n "rate"; F.definition [ "n", F.intType ] F.intType recursiveBody ] ]
            F.apply (F.variable "rate") [ F.integer 3 ]
        ]

    let json =
        F.model [
            "capture",
            [],
            [
                F.binding "rate" [] F.intType (F.integer 5000)
                F.binding
                    "locals"
                    []
                    (F.tTuple (List.replicate 5 F.intType))
                    (F.a [
                        box "Tuple"
                        null
                        F.a [ local; lambda; matched; destructured; recursive ]
                    ])
            ]
        ]

    json

let lexicalCapture version =
    lexicalCaptureJson ()
    |> classic
    |> forVersion version


let localRecursionJson () =
    let countdownBody =
        F.a [
            box "IfThenElse"
            null
            F.op "equal" (F.variable "n") (F.integer 0)
            F.integer 0
            F.apply (F.variable "loop") [ F.op "subtract" (F.variable "n") (F.integer 1) ]
        ]

    let body =
        F.a [
            box "LetRecursion"
            null
            F.a [ F.a [ F.n "loop"; F.definition [ "n", F.intType ] F.intType countdownBody ] ]
            F.apply (F.variable "loop") [ F.variable "n" ]
        ]

    let shadow =
        F.a [
            box "LetDefinition"
            null
            F.n "x"
            F.definition [] F.intType (F.integer 9)
            F.variable "x"
        ]

    let json =
        F.model [
            "locals",
            [],
            [
                F.binding "countdown" [ "n", F.intType ] F.intType body
                F.binding "shadow" [ "x", F.intType ] F.intType shadow
            ]
        ]

    json

let localRecursion version =
    localRecursionJson ()
    |> classic
    |> forVersion version


let recordOperationsJson () =
    let recordType = F.tRecord [ "value", F.intType ]

    let record =
        F.a [ box "Record"; recordType; F.a [ F.a [ F.n "value"; F.integer 3 ] ] ]

    let update =
        F.a [
            box "UpdateRecord"
            recordType
            F.variable "record"
            F.a [ F.a [ F.n "value"; F.integer 7 ] ]
        ]

    let accessor = F.a [ box "FieldFunction"; null; F.n "value" ]

    let json =
        F.model [
            "records",
            [],
            [
                F.binding "item" [] recordType record
                F.binding "change" [ "record", recordType ] recordType update
                F.binding "accessor" [] (F.tFunction recordType F.intType) accessor
            ]
        ]

    json

let recordOperations version =
    recordOperationsJson ()
    |> classic
    |> forVersion version

let letRecOrder version =
    let body =
        F.a [
            box "LetRecursion"
            null
            F.a [
                F.a [ F.n "a"; F.definition [] F.intType (F.variable "b") ]
                F.a [ F.n "b"; F.definition [] F.intType (F.integer 7) ]
            ]
            F.variable "a"
        ]

    F.model [ "locals", [], [ F.binding "ordered" [] F.intType body ] ]
    |> classic
    |> forVersion version

let letRecCycle version =
    let body =
        F.a [
            box "LetRecursion"
            null
            F.a [
                F.a [ F.n "a"; F.definition [] F.intType (F.variable "b") ]
                F.a [ F.n "b"; F.definition [] F.intType (F.variable "a") ]
            ]
            F.variable "a"
        ]

    F.model [ "locals", [], [ F.binding "ordered" [] F.intType body ] ]
    |> classic
    |> forVersion version

let sdkMappingsJson () =
    let increment =
        F.a [ box "Lambda"; null; F.bind "x"; F.op "add" (F.variable "x") (F.integer 1) ]

    let numbers = F.a [ box "List"; null; F.a [ F.integer 1; F.integer 2 ] ]

    let map modName value =
        F.apply (F.sdkReference modName "map") [ increment; value ]

    let json =
        F.model [
            "mappings",
            [],
            [
                F.binding "numbers" [] (F.listType F.intType) (map "list" numbers)
                F.binding
                    "maybe"
                    []
                    (F.tRef (F.sdk "maybe" "maybe") [ F.intType ])
                    (map "maybe" (F.apply (F.constructor (F.sdk "maybe" "just")) [ F.integer 3 ]))
                F.binding
                    "result"
                    []
                    (F.tRef (F.sdk "result" "result") [ F.stringType; F.intType ])
                    (map "result" (F.apply (F.constructor (F.sdk "result" "ok")) [ F.integer 4 ]))
            ]
        ]

    json

let sdkMappings version =
    sdkMappingsJson ()
    |> classic
    |> forVersion version
