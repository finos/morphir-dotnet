module Morphir.CodeGeneration.CSharp.Tests.RefusalCases

open System.Text.Json
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.V4

let name = Name.fromString
let pkg = PackageName.packageNameFromString "example"
let path = ModulePath.modulePathFromString "bad"
let own local = FQName.fqName pkg path (name local)

let sdk modul local =
    FQName.fqName
        (PackageName.packageNameFromString "morphir.s_d_k")
        (ModulePath.modulePathFromString modul)
        (name local)

let a = Unchecked.defaultof<JsonElement>

let version text =
    SourceFormatVersion.parse text
    |> Result.defaultWith (fun e -> failwithf "%A" e)

let classic parameters body =
    let alias = Morphir.IR.Classic.Type.TypeAliasDefinition(parameters, body)

    let modul: Morphir.IR.Classic.Module.ModuleDefinition<JsonElement, JsonElement> = {
        Types =
            Map.ofList [
                name "bad",
                Morphir.IR.Classic.AccessControlled.public' (
                    Morphir.IR.Classic.Documented.WithoutDocumentation alias
                )
            ]
        Values = Map.empty
        Doc = None
    }

    {
        SourceVersion = version "3"
        Distribution =
            DistributionModel.Classic(
                Morphir.IR.Classic.Distribution.Library(
                    pkg,
                    Map.empty,
                    {
                        Modules =
                            Map.ofList [ path, Morphir.IR.Classic.AccessControlled.public' modul ]
                    }
                )
            )
    }

let classicCases =
    let reference fq args =
        Morphir.IR.Classic.Type.Reference(a, fq, args)

    let intType = reference (sdk "basics" "int") []

    let privateExposure =
        let model =
            classic
                []
                (Morphir.IR.Classic.Type.Record(
                    a,
                    [
                        {
                            Name = name "hidden"
                            Type = reference (own "hidden") []
                        }
                    ]
                ))

        match model.Distribution with
        | DistributionModel.Classic(Morphir.IR.Classic.Distribution.Library(pkg, deps, definition)) ->
            let hidden =
                Morphir.IR.Classic.Type.CustomTypeDefinition(
                    [],
                    Morphir.IR.Classic.AccessControlled.public' (Map.ofList [ name "secret", [] ])
                )

            let modules =
                definition.Modules
                |> Map.map (fun _ controlled ->
                    { controlled with
                        Value =
                            { controlled.Value with
                                Types =
                                    controlled.Value.Types
                                    |> Map.add
                                        (name "hidden")
                                        (Morphir.IR.Classic.AccessControlled.private' (
                                            Morphir.IR.Classic.Documented.WithoutDocumentation
                                                hidden
                                        ))
                            }
                    }
                )

            { model with
                Distribution =
                    DistributionModel.Classic(
                        Morphir.IR.Classic.Distribution.Library(pkg, deps, { Modules = modules })
                    )
            }
        | _ -> failwith "Expected classic fixture."

    [
        "private nominal type exposure", "private-type-exposure", privateExposure
        "unmapped SDK type", "unmapped-sdk-type", classic [] (reference (sdk "date" "date") [])
        "unresolved dependency type",
        "unresolved-type",
        classic
            []
            (reference
                (FQName.fqName
                    (PackageName.packageNameFromString "dependency")
                    path
                    (name "missing"))
                [])
        "SDK type argument count", "type-arity", classic [] (reference (sdk "list" "list") [])
        "scalar type argument count",
        "type-arity",
        classic [] (reference (sdk "basics" "int") [ intType ])
        "alias argument count", "type-arity", classic [ name "a" ] (reference (own "bad") [])
        "recursive alias", "recursive-alias", classic [] (reference (own "bad") [])
        "unbound alias type parameter",
        "unbound-type",
        classic [] (Morphir.IR.Classic.Type.Variable(a, name "missing"))
        "unbound record type parameter",
        "unbound-type",
        classic
            []
            (Morphir.IR.Classic.Type.Record(
                a,
                [
                    {
                        Name = name "value"
                        Type = Morphir.IR.Classic.Type.Variable(a, name "missing")
                    }
                ]
            ))
        "one-element tuple",
        "tuple-arity",
        classic [] (Morphir.IR.Classic.Type.Tuple(a, [ intType ]))
        "open record",
        "open-record",
        classic [] (Morphir.IR.Classic.Type.ExtensibleRecord(a, name "row", []))
    ]

let v4Cases =
    let definition body =
        let types =
            Morphir.IR.V4.OrderedMap.ofList [
                name "bad",
                {
                    Morphir.IR.V4.AccessControlled.Access = Morphir.IR.V4.Public
                    Value = {
                        Morphir.IR.V4.Documented.Doc = None
                        Value = body
                    }
                }
            ]
            |> Result.defaultWith (fun _ -> failwith "Duplicate fixture key.")

        let modul: Morphir.IR.V4.ModuleDefinition = {
            Types = types
            Values = Morphir.IR.V4.OrderedMap.empty
            Doc = None
        }

        let modules =
            Morphir.IR.V4.OrderedMap.ofList [
                path,
                {
                    Morphir.IR.V4.AccessControlled.Access = Morphir.IR.V4.Public
                    Value = modul
                }
            ]
            |> Result.defaultWith (fun _ -> failwith "Duplicate fixture key.")

        {
            SourceVersion = version "4.0.0"
            Distribution =
                DistributionModel.V4(
                    Morphir.IR.V4.Library(
                        pkg,
                        Morphir.IR.V4.OrderedMap.empty,
                        { Modules = modules }
                    )
                )
        }

    let a = Morphir.IR.V4.TypeAttributes.empty

    let constrained =
        { a with
            Constraints = Some(Morphir.IR.V4.DocumentNumber "1")
        }

    [
        "v4 incomplete type",
        "unsupported-body",
        definition (
            Morphir.IR.V4.IncompleteTypeDefinition([], Morphir.IR.V4.Incompleteness.Draft, None)
        )
        "v4 type constraints",
        "unsupported-constraints",
        definition (Morphir.IR.V4.TypeAliasDefinition([], Morphir.IR.V4.Type.Unit constrained))
        "v4 open record",
        "open-record",
        definition (
            Morphir.IR.V4.TypeAliasDefinition(
                [],
                Morphir.IR.V4.Type.ExtensibleRecord(a, name "row", [])
            )
        )
    ]
