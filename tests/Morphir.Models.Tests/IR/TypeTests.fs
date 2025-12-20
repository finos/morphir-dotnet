module Morphir.IR.Tests.TypeTests

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "Type" [
        testList "Field" [
            testCase "Creates field from name and type"
            <| fun _ ->
                let name = Name.fromList [ "age" ]
                let typ = Type.variable () (Name.fromList [ "int" ])
                let field = Type.field name typ

                field.Name
                |> Expect.equal name

                field.Type
                |> Expect.equal typ
        ]

        testList "Type Expressions" [
            testList "variable" [
                testCase "Creates Variable type"
                <| fun _ ->
                    let name = Name.fromList [ "a" ]
                    let typ = Type.variable () name

                    match typ with
                    | Type.Variable(attrs, n) ->
                        n |> Expect.equal name
                    | _ -> failwith "Expected Variable type"
            ]

            testList "reference" [
                testCase "Creates Reference type without type arguments"
                <| fun _ ->
                    let fqName =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                            (Path.fromList [ Name.fromList [ "string" ] ])
                            (Name.fromList [ "string" ])
                    let typ = Type.reference () fqName []

                    match typ with
                    | Type.Reference(attrs, fn, args) ->
                        fn |> Expect.equal fqName
                        args |> Expect.equal []
                    | _ -> failwith "Expected Reference type"

                testCase "Creates Reference type with type arguments"
                <| fun _ ->
                    let fqName =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                            (Path.fromList [ Name.fromList [ "list" ] ])
                            (Name.fromList [ "list" ])
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let listType = Type.reference () fqName [ intType ]

                    match listType with
                    | Type.Reference(attrs, fn, args) ->
                        fn |> Expect.equal fqName
                        args.Length |> Expect.equal 1
                    | _ -> failwith "Expected Reference type"
            ]

            testList "tuple" [
                testCase "Creates Tuple type with multiple elements"
                <| fun _ ->
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let tupleType = Type.tuple () [ intType; stringType ]

                    match tupleType with
                    | Type.Tuple(attrs, elements) ->
                        elements.Length |> Expect.equal 2
                    | _ -> failwith "Expected Tuple type"
            ]

            testList "record" [
                testCase "Creates Record type with fields"
                <| fun _ ->
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let fields = [
                        Type.field (Name.fromList [ "firstName" ]) stringType
                        Type.field (Name.fromList [ "age" ]) intType
                    ]
                    let recordType = Type.record () fields

                    match recordType with
                    | Type.Record(attrs, fs) ->
                        fs.Length |> Expect.equal 2
                    | _ -> failwith "Expected Record type"
            ]

            testList "extensibleRecord" [
                testCase "Creates ExtensibleRecord type"
                <| fun _ ->
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let variableName = Name.fromList [ "a" ]
                    let fields = [
                        Type.field (Name.fromList [ "name" ]) stringType
                    ]
                    let extensibleType = Type.extensibleRecord () variableName fields

                    match extensibleType with
                    | Type.ExtensibleRecord(attrs, vn, fs) ->
                        vn |> Expect.equal variableName
                        fs.Length |> Expect.equal 1
                    | _ -> failwith "Expected ExtensibleRecord type"
            ]

            testList "functionType" [
                testCase "Creates Function type"
                <| fun _ ->
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let funcType = Type.functionType () intType stringType

                    match funcType with
                    | Type.Function(attrs, arg, ret) ->
                        arg |> Expect.equal intType
                        ret |> Expect.equal stringType
                    | _ -> failwith "Expected Function type"

                testCase "Creates curried Function type"
                <| fun _ ->
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let boolType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "bool" ]))
                            []
                    // Int -> Int -> Bool
                    let curriedFunc = Type.functionType () intType (Type.functionType () intType boolType)

                    match curriedFunc with
                    | Type.Function(attrs, arg1, innerFunc) ->
                        arg1 |> Expect.equal intType
                        match innerFunc with
                        | Type.Function(attrs2, arg2, ret) ->
                            arg2 |> Expect.equal intType
                            ret |> Expect.equal boolType
                        | _ -> failwith "Expected nested Function type"
                    | _ -> failwith "Expected Function type"
            ]

            testList "unit" [
                testCase "Creates Unit type"
                <| fun _ ->
                    let unitType = Type.unit ()

                    match unitType with
                    | Type.Unit attrs -> ()
                    | _ -> failwith "Expected Unit type"
            ]
        ]

        testList "Type Specifications" [
            testList "typeAliasSpecification" [
                testCase "Creates TypeAliasSpecification"
                <| fun _ ->
                    let typeParams = [ Name.fromList [ "a" ] ]
                    let aliasedType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "list" ] ])
                                (Name.fromList [ "list" ]))
                            []
                    let spec = Type.typeAliasSpecification typeParams aliasedType

                    match spec with
                    | Type.TypeAliasSpecification(typeParams', typ) ->
                        typeParams' |> Expect.equal typeParams
                        typ |> Expect.equal aliasedType
                    | _ -> failwith "Expected TypeAliasSpecification"
            ]

            testList "opaqueTypeSpecification" [
                testCase "Creates OpaqueTypeSpecification"
                <| fun _ ->
                    let typeParams = [ Name.fromList [ "a" ] ]
                    let spec = Type.opaqueTypeSpecification typeParams

                    match spec with
                    | Type.OpaqueTypeSpecification typeParams' ->
                        typeParams' |> Expect.equal typeParams
                    | _ -> failwith "Expected OpaqueTypeSpecification"
            ]

            testList "customTypeSpecification" [
                testCase "Creates CustomTypeSpecification with constructors"
                <| fun _ ->
                    let typeParams = []
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let constructors =
                        Map.empty
                        |> Map.add (Name.fromList [ "ok" ]) [ (Name.fromList [ "value" ], intType) ]
                        |> Map.add (Name.fromList [ "err" ]) [ (Name.fromList [ "error" ], intType) ]
                    let spec = Type.customTypeSpecification typeParams constructors

                    match spec with
                    | Type.CustomTypeSpecification(typeParams', ctors) ->
                        typeParams' |> Expect.equal typeParams
                        ctors.Count |> Expect.equal 2
                    | _ -> failwith "Expected CustomTypeSpecification"
            ]

            testList "derivedTypeSpecification" [
                testCase "Creates DerivedTypeSpecification"
                <| fun _ ->
                    let typeParams = []
                    let baseType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let fromBase =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                            (Path.fromList [ Name.fromList [ "date" ] ])
                            (Name.fromList [ "fromString" ])
                    let toBase =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                            (Path.fromList [ Name.fromList [ "date" ] ])
                            (Name.fromList [ "toString" ])
                    let details: Type.DerivedTypeDetails<unit> = {
                        BaseType = baseType
                        FromBaseType = fromBase
                        ToBaseType = toBase
                    }
                    let spec = Type.derivedTypeSpecification typeParams details

                    match spec with
                    | Type.DerivedTypeSpecification(typeParams', dets) ->
                        typeParams' |> Expect.equal typeParams
                        dets.BaseType |> Expect.equal baseType
                        dets.FromBaseType |> Expect.equal fromBase
                        dets.ToBaseType |> Expect.equal toBase
                    | _ -> failwith "Expected DerivedTypeSpecification"
            ]
        ]

        testList "Type Definitions" [
            testList "typeAliasDefinition" [
                testCase "Creates TypeAliasDefinition"
                <| fun _ ->
                    let typeParams = [ Name.fromList [ "a" ] ]
                    let aliasedType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "list" ] ])
                                (Name.fromList [ "list" ]))
                            []
                    let def = Type.typeAliasDefinition typeParams aliasedType

                    match def with
                    | Type.TypeAliasDefinition(typeParams', typ) ->
                        typeParams' |> Expect.equal typeParams
                        typ |> Expect.equal aliasedType
                    | _ -> failwith "Expected TypeAliasDefinition"
            ]

            testList "customTypeDefinition" [
                testCase "Creates CustomTypeDefinition with Public constructors"
                <| fun _ ->
                    let typeParams = []
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let constructors =
                        Map.empty
                        |> Map.add (Name.fromList [ "ok" ]) [ (Name.fromList [ "value" ], intType) ]
                    let accessControlled = AccessControlled.public' constructors
                    let def = Type.customTypeDefinition typeParams accessControlled

                    match def with
                    | Type.CustomTypeDefinition(typeParams', ctors) ->
                        typeParams' |> Expect.equal typeParams
                        ctors.Access |> Expect.equal AccessControlled.Public
                    | _ -> failwith "Expected CustomTypeDefinition"

                testCase "Creates CustomTypeDefinition with Private constructors"
                <| fun _ ->
                    let typeParams = []
                    let constructors = Map.empty
                    let accessControlled = AccessControlled.private' constructors
                    let def = Type.customTypeDefinition typeParams accessControlled

                    match def with
                    | Type.CustomTypeDefinition(typeParams', ctors) ->
                        typeParams' |> Expect.equal typeParams
                        ctors.Access |> Expect.equal AccessControlled.Private
                    | _ -> failwith "Expected CustomTypeDefinition"
            ]
        ]
    ]

