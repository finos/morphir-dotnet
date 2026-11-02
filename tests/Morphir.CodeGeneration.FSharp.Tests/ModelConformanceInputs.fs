module Morphir.CodeGeneration.FSharp.Tests.ModelConformanceInputs

open System.Text.Json
open Morphir.IR
open Morphir.IR.Model

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
