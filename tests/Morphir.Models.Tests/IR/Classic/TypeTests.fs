module Morphir.IR.Classic.Tests.TypeTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Testing.Assertions

// The ^-> operator and Arrow extension are now available from AutoOpen TypeExtensions module

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

            testList "toString" [
                testCase "Formats field as name : type"
                <| fun _ ->
                    let name = Name.fromList [ "age" ]
                    let typ = Type.variable () (Name.fromList [ "int" ])
                    let field = Type.field name typ

                    Type.fieldToString field
                    |> Expect.equal "age : int"
            ]
        ]

        testList "toString" [
            testList "Type.toString" [
                testCase "Variable type formats as camelCase name"
                <| fun _ ->
                    let typ = Type.variable () (Name.fromList [ "result" ])
                    Type.toString typ
                    |> Expect.equal "result"

                testCase "Reference type without arguments formats as FQName"
                <| fun _ ->
                    let fqName =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                            (Path.fromList [ Name.fromList [ "basics" ] ])
                            (Name.fromList [ "int" ])
                    let typ = Type.reference () fqName []
                    Type.toString typ
                    |> Expect.equal "Morphir.SDK.Basics.Int"

                testCase "Reference type with arguments formats as FQName followed by space-separated types"
                <| fun _ ->
                    let listFQName =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                            (Path.fromList [ Name.fromList [ "list" ] ])
                            (Name.fromList [ "list" ])
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let listType = Type.reference () listFQName [ intType ]
                    Type.toString listType
                    |> Expect.equal "Morphir.SDK.List.List Morphir.SDK.Basics.Int"

                testCase "Tuple type formats as comma-separated types in parentheses"
                <| fun _ ->
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let tupleType = Type.tuple () [ intType; stringType ]
                    Type.toString tupleType
                    |> Expect.equal "(Morphir.SDK.Basics.Int, Morphir.SDK.String.String)"

                testCase "Record type formats as fields in curly braces"
                <| fun _ ->
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let fields = [
                        Type.field (Name.fromList [ "firstName" ]) stringType
                        Type.field (Name.fromList [ "age" ]) intType
                    ]
                    let recordType = Type.record () fields
                    Type.toString recordType
                    |> Expect.equal "{ firstName : Morphir.SDK.String.String, age : Morphir.SDK.Basics.Int }"

                testCase "ExtensibleRecord type formats as variable | fields in curly braces"
                <| fun _ ->
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let variableName = Name.fromList [ "a" ]
                    let fields = [
                        Type.field (Name.fromList [ "name" ]) stringType
                    ]
                    let extensibleType = Type.extensibleRecord () variableName fields
                    Type.toString extensibleType
                    |> Expect.equal "{ a | name : Morphir.SDK.String.String }"

                testCase "Function type formats as argument -> return"
                <| fun _ ->
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let funcType = Type.functionType () intType stringType
                    Type.toString funcType
                    |> Expect.equal "Morphir.SDK.Basics.Int -> Morphir.SDK.String.String"

                testCase "Unit type formats as ()"
                <| fun _ ->
                    let unitType = Type.unit ()
                    Type.toString unitType
                    |> Expect.equal "()"
            ]
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
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                            (Path.fromList [ Name.fromList [ "list" ] ])
                            (Name.fromList [ "list" ])
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let boolType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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

            testList "^-> operator" [
                testCase "Creates Function type with ^-> operator"
                <| fun _ ->
                    let intType = Type.variable () (Name.fromString "Int")
                    let stringType = Type.variable () (Name.fromString "String")
                    let funcType = intType ^-> stringType

                    match funcType with
                    | Type.Function(attrs, arg, ret) ->
                        arg |> Expect.equal intType
                        ret |> Expect.equal stringType
                    | _ -> failwith "Expected Function type"

                testCase "^-> operator is right-associative"
                <| fun _ ->
                    let intType = Type.variable () (Name.fromString "Int")
                    let charType = Type.variable () (Name.fromString "Char")
                    let stringType = Type.variable () (Name.fromString "String")

                    // intType ^-> charType ^-> stringType should equal intType ^-> (charType ^-> stringType)
                    let chained = intType ^-> charType ^-> stringType
                    let rightAssoc = intType ^-> (charType ^-> stringType)

                    chained |> Expect.equal rightAssoc

                    // Should create Int -> (Char -> String), not (Int -> Char) -> String
                    match chained with
                    | Type.Function(_, arg1, innerFunc) ->
                        arg1 |> Expect.equal intType
                        match innerFunc with
                        | Type.Function(_, arg2, ret) ->
                            arg2 |> Expect.equal charType
                            ret |> Expect.equal stringType
                        | _ -> failwith "Expected nested Function type"
                    | _ -> failwith "Expected Function type"
            ]

            testList "Arrow fluent method" [
                testCase "Creates Function type with .Arrow() method"
                <| fun _ ->
                    let intType = Type.variable () (Name.fromString "Int")
                    let stringType = Type.variable () (Name.fromString "String")
                    let funcType = intType.Arrow(stringType)

                    match funcType with
                    | Type.Function(attrs, arg, ret) ->
                        arg |> Expect.equal intType
                        ret |> Expect.equal stringType
                    | _ -> failwith "Expected Function type"

                testCase ".Arrow() chains correctly"
                <| fun _ ->
                    let intType = Type.variable () (Name.fromString "Int")
                    let charType = Type.variable () (Name.fromString "Char")
                    let stringType = Type.variable () (Name.fromString "String")

                    // Chaining Arrow() should work left-to-right
                    let chained = intType.Arrow(charType).Arrow(stringType)

                    // This creates (Int -> Char) -> String (left-associative)
                    match chained with
                    | Type.Function(_, innerFunc, ret) ->
                        match innerFunc with
                        | Type.Function(_, arg1, arg2) ->
                            arg1 |> Expect.equal intType
                            arg2 |> Expect.equal charType
                        | _ -> failwith "Expected nested Function type"
                        ret |> Expect.equal stringType
                    | _ -> failwith "Expected Function type"
            ]
        ]

        testList "Type Specifications" [
            testList "toString" [
                testCase "TypeAliasSpecification formats as type alias with parameters and type"
                <| fun _ ->
                    let typeParams = [ Name.fromList [ "a" ] ]
                    let aliasedType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "list" ] ])
                                (Name.fromList [ "list" ]))
                            [ Type.variable () (Name.fromList [ "a" ]) ]
                    let spec = Type.typeAliasSpecification typeParams aliasedType
                    Type.TypeSpecification.toString spec
                    |> Expect.equal "type alias a = Morphir.SDK.List.List a"

                testCase "OpaqueTypeSpecification formats as type with parameters"
                <| fun _ ->
                    let typeParams = [ Name.fromList [ "a" ] ]
                    let spec = Type.opaqueTypeSpecification typeParams
                    Type.TypeSpecification.toString spec
                    |> Expect.equal "type a"

                testCase "CustomTypeSpecification formats as type with constructors"
                <| fun _ ->
                    let typeParams = []
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let constructors =
                        Map.empty
                        |> Map.add (Name.fromList [ "ok" ]) [ (Name.fromList [ "value" ], intType) ]
                        |> Map.add (Name.fromList [ "err" ]) [ (Name.fromList [ "error" ], intType) ]
                    let spec = Type.customTypeSpecification typeParams constructors
                    Type.TypeSpecification.toString spec
                    |> Expect.equal "type = Err error | Ok value"  // Sorted alphabetically by constructor name

                testCase "DerivedTypeSpecification formats as type with base type"
                <| fun _ ->
                    let typeParams = []
                    let baseType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let fromBase =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                            (Path.fromList [ Name.fromList [ "date" ] ])
                            (Name.fromList [ "fromString" ])
                    let toBase =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                            (Path.fromList [ Name.fromList [ "date" ] ])
                            (Name.fromList [ "toString" ])
                    let details: Type.DerivedTypeDetails<unit> = {
                        BaseType = baseType
                        FromBaseType = fromBase
                        ToBaseType = toBase
                    }
                    let spec = Type.derivedTypeSpecification typeParams details
                    Type.TypeSpecification.toString spec
                    |> Expect.equal "type derived from Morphir.SDK.String.String"
            ]

            testList "typeAliasSpecification" [
                testCase "Creates TypeAliasSpecification"
                <| fun _ ->
                    let typeParams = [ Name.fromList [ "a" ] ]
                    let aliasedType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let fromBase =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                            (Path.fromList [ Name.fromList [ "date" ] ])
                            (Name.fromList [ "fromString" ])
                    let toBase =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
            testList "toString" [
                testCase "TypeAliasDefinition formats as type alias with parameters and type"
                <| fun _ ->
                    let typeParams = [ Name.fromList [ "a" ] ]
                    let aliasedType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "list" ] ])
                                (Name.fromList [ "list" ]))
                            [ Type.variable () (Name.fromList [ "a" ]) ]
                    let def = Type.typeAliasDefinition typeParams aliasedType
                    Type.TypeDefinition.toString def
                    |> Expect.equal "type alias a = Morphir.SDK.List.List a"

                testCase "CustomTypeDefinition with Public constructors formats as type with constructors"
                <| fun _ ->
                    let typeParams = []
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let constructors =
                        Map.empty
                        |> Map.add (Name.fromList [ "ok" ]) [ (Name.fromList [ "value" ], intType) ]
                    let accessControlled = AccessControlled.public' constructors
                    let def = Type.customTypeDefinition typeParams accessControlled
                    Type.TypeDefinition.toString def
                    |> Expect.equal "type = Ok value"

                testCase "CustomTypeDefinition with Private constructors formats as opaque type"
                <| fun _ ->
                    let typeParams = []
                    let constructors = Map.empty
                    let accessControlled = AccessControlled.private' constructors
                    let def = Type.customTypeDefinition typeParams accessControlled
                    Type.TypeDefinition.toString def
                    |> Expect.equal "type"
            ]

            testList "typeAliasDefinition" [
                testCase "Creates TypeAliasDefinition"
                <| fun _ ->
                    let typeParams = [ Name.fromList [ "a" ] ]
                    let aliasedType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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

