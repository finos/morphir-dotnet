module Morphir.IR.Tests.V4.ModelTests

open Expecto
open Morphir.IR
open Morphir.IR.V4

let private ok result =
    match result with
    | Ok value -> value
    | Error error -> failtestf "Expected a value, got %A" error

let private orderedMap entries =
    OrderedMap.ofList entries
    |> ok

let private decimalOf lexeme =
    DecimalLiteral(ok (DecimalLexeme.create lexeme))

let private floatOf (value: float) lexeme =
    ok (FloatValue.create value (Some lexeme))

let private intType =
    Type.Reference(
        TypeAttributes.empty,
        {
            PackagePath = PackageName(Path [ Name [ "morphir" ] ])
            ModulePath = ModulePath(Path [ Name [ "basics" ] ])
            LocalName = Name [ "int" ]
        },
        []
    )

let private unitValue = Value.Unit ValueAttributes.empty

[<Tests>]
let tests =
    testList "IR v4 model" [
        testList "OrderedMap" [
            testCase "keeps insertion order"
            <| fun () ->
                let map = orderedMap [ "b", 1; "a", 2; "c", 3 ]

                Expect.equal
                    (OrderedMap.toList map)
                    [ "b", 1; "a", 2; "c", 3 ]
                    "Entries stay in order"

                Expect.equal (OrderedMap.keys map) [ "b"; "a"; "c" ] "Keys stay in order"
                Expect.equal (OrderedMap.count map) 3 "It counts its entries"

            testCase "rejects a duplicate key and names it"
            <| fun () ->
                Expect.equal
                    (OrderedMap.ofList [ "a", 1; "b", 2; "a", 3 ])
                    (Error "a")
                    "The first repeated key is the error"

            testCase "equality ignores order"
            <| fun () ->
                let left = orderedMap [ "a", 1; "b", 2 ]
                let right = orderedMap [ "b", 2; "a", 1 ]
                Expect.equal left right "The same entries are equal"
                Expect.equal (hash left) (hash right) "Equal maps hash alike"

            testCase "equality compares values and keys"
            <| fun () ->
                Expect.notEqual (orderedMap [ "a", 1 ]) (orderedMap [ "a", 2 ]) "Values count"
                Expect.notEqual (orderedMap [ "a", 1 ]) (orderedMap [ "b", 1 ]) "Keys count"

                Expect.notEqual
                    (orderedMap [ "a", 1 ])
                    (orderedMap [ "a", 1; "b", 2 ])
                    "Size counts"

            testCase "finds an entry"
            <| fun () ->
                let map = orderedMap [ "a", 1 ]
                Expect.equal (OrderedMap.tryFind "a" map) (Some 1) "Present"
                Expect.equal (OrderedMap.tryFind "b" map) None "Absent"
                Expect.equal (OrderedMap.count OrderedMap.empty<string, int>) 0 "Empty"
        ]

        testCase "Decimal identity"
        <| fun () ->
            Expect.notEqual (decimalOf "10.50") (decimalOf "10.5") "Two lexemes are two literals"
            Expect.equal (decimalOf "10.50") (decimalOf "10.50") "One lexeme is one literal"

        testList "Decimal lexemes" [
            for lexeme in [ "0"; "-0.00"; "+1"; "12."; ".5"; "1e-7"; "1E5"; "123456789.987654321" ] do
                testCase $"accepts '{lexeme}'"
                <| fun () ->
                    Expect.equal
                        (DecimalLexeme.value (ok (DecimalLexeme.create lexeme)))
                        lexeme
                        "Kept as written"

            for lexeme in
                [
                    ""
                    "ten"
                    "NaN"
                    "Infinity"
                    "1_000"
                    " 1"
                    "1 "
                    "."
                    "e5"
                    "1e"
                    "+"
                    "0x1F"
                    "1.2.3"
                ] do
                testCase $"refuses '{lexeme}'"
                <| fun () -> Expect.isError (DecimalLexeme.create lexeme) "Not a decimal lexeme"
        ]

        testCase "Float meaning"
        <| fun () ->
            let left = floatOf 4.0 "4.0"
            let right = floatOf 4.0 "4e+0"

            Expect.equal
                (FloatLiteral left)
                (FloatLiteral right)
                "Semantic comparison ignores the lexeme"

            Expect.equal (hash left) (hash right) "Equal floats hash alike"
            Expect.equal left.Lexeme (Some "4.0") "The first lexeme stays available"
            Expect.equal right.Lexeme (Some "4e+0") "The second lexeme stays available"
            Expect.equal left.Value 4.0 "The value stays available"

        testCase "A float literal must be finite"
        <| fun () ->
            Expect.isError (FloatValue.create nan None) "NaN"
            Expect.isError (FloatValue.create infinity None) "Infinity"
            Expect.isError (FloatValue.create -infinity None) "Negative infinity"

        testCase "A float's lexeme is optional"
        <| fun () ->
            Expect.equal (ok (FloatValue.create 1.5 None)).Lexeme None "None retained"

            Expect.notEqual
                (ok (FloatValue.create 1.5 None))
                (ok (FloatValue.create 2.5 None))
                "Values count"

        testCase "An integer literal holds any size"
        <| fun () ->
            let big = System.Numerics.BigInteger.Parse "18446744073709551616"
            Expect.equal (IntegerLiteral big) (IntegerLiteral big) "Stays whole"

        testCase "Document numbers compare by lexeme"
        <| fun () ->
            Expect.notEqual (DocumentNumber "1.0") (DocumentNumber "1") "The lexeme is the value"

        testCase "Document objects keep order and compare without it"
        <| fun () ->
            let entries = [ "b", DocumentNull; "a", DocumentBool true ]
            let left = DocumentObject(orderedMap entries)
            let right = DocumentObject(orderedMap (List.rev entries))
            Expect.equal left right "Member order is not semantic"

            match left with
            | DocumentObject members ->
                Expect.equal (OrderedMap.keys members) [ "b"; "a" ] "Document order stays"
            | other -> failtestf "Expected an object, got %A" other

        testCase "Parameter order"
        <| fun () ->
            let parameters = [ Name [ "a" ], intType; Name [ "b" ], intType ]

            let definition inputs : ValueDefinition = {
                InputTypes = inputs
                Body = ExpressionBody(intType, unitValue)
            }

            Expect.notEqual
                (definition parameters)
                (definition (List.rev parameters))
                "Parameter order counts"

        testCase "Module order"
        <| fun () ->
            let modulePath word = ModulePath(Path [ Name [ word ] ])

            let specification = {
                Annotations = []
                Types = OrderedMap.empty
                Values = OrderedMap.empty
                Doc = None
            }

            let distribution modules =
                Specs(
                    PackageName(Path [ Name [ "example" ] ]),
                    OrderedMap.empty,
                    {
                        Modules =
                            orderedMap [ for word in modules -> modulePath word, specification ]
                    }
                )

            let left = distribution [ "a"; "b" ]
            let right = distribution [ "b"; "a" ]
            Expect.equal left right "Module order does not count"

            let listed value =
                match value with
                | Specs(_, _, specification) -> OrderedMap.keys specification.Modules
                | other -> failtestf "Expected Specs, got %A" other

            Expect.equal
                (listed left)
                [ modulePath "a"; modulePath "b" ]
                "The left document's order stays"

            Expect.equal
                (listed right)
                [ modulePath "b"; modulePath "a" ]
                "The right document's order stays"

        testCase "Record field order counts"
        <| fun () ->
            let fields = [ Name [ "a" ], intType; Name [ "b" ], intType ]

            Expect.notEqual
                (Type.Record(TypeAttributes.empty, fields))
                (Type.Record(TypeAttributes.empty, List.rev fields))
                "Field order counts"

        testCase "Entry point order counts"
        <| fun () ->
            let entry: EntryPoint = {
                Target = {
                    PackagePath = PackageName(Path [ Name [ "p" ] ])
                    ModulePath = ModulePath(Path [ Name [ "m" ] ])
                    LocalName = Name [ "run" ]
                }
                Kind = Main
                Doc = None
            }

            let application points =
                Application(
                    PackageName(Path [ Name [ "p" ] ]),
                    OrderedMap.empty,
                    { Modules = OrderedMap.empty },
                    points
                )

            Expect.notEqual
                (application [ "a", entry; "b", entry ])
                (application [ "b", entry; "a", entry ])
                "Entry point order counts"
    ]
