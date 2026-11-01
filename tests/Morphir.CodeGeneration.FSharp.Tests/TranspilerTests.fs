module Morphir.CodeGeneration.FSharp.Tests.TranspilerTests

open Expecto
open Morphir.CodeGeneration.FSharp
open System
open System.Diagnostics
open System.IO
open System.Text.Json.Nodes
open Morphir.Testing
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

module F = Morphir.CodeGeneration.FSharp.Tests.Fixtures

let executeWithReferences references (modules: GeneratedModule list) code =
    let directory =
        Path.Combine(
            Path.GetTempPath(),
            "morphir-backend-"
            + Guid.NewGuid().ToString("N")
        )

    Directory.CreateDirectory(directory)
    |> ignore

    try
        for m in modules do
            let file = Path.Combine(directory, m.RelativePath)

            Directory.CreateDirectory(Path.GetDirectoryName file)
            |> ignore

            File.WriteAllText(file, m.Source)

        let loads =
            modules
            |> List.map (fun m ->
                "#load @\""
                + Path.Combine(directory, m.RelativePath)
                + "\""
            )
            |> String.concat "\n"

        let script = Path.Combine(directory, "consumer.fsx")

        File.WriteAllText(
            script,
            (references
             |> List.map (fun reference ->
                 "#r @\""
                 + reference
                 + "\""
             )
             |> String.concat "\n")
            + "\n"
            + loads
            + "\n"
            + code
        )

        let dotnetHost =
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            |> Option.ofObj
            |> Option.defaultWith (fun () ->
                Path.GetFullPath(
                    Path.Combine(
                        System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                        "../../..",
                        if OperatingSystem.IsWindows() then
                            "dotnet.exe"
                        else
                            "dotnet"
                    )
                )
            )

        let start = ProcessStartInfo(dotnetHost)
        start.ArgumentList.Add("fsi")
        start.ArgumentList.Add("--warnaserror+")
        start.ArgumentList.Add("--exec")
        start.ArgumentList.Add(script)
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        use child = Process.Start(start)
        let output = child.StandardOutput.ReadToEndAsync()
        let error = child.StandardError.ReadToEndAsync()

        if not (child.WaitForExit(60000)) then
            child.Kill(true)
            failtest "compiler timeout"

        Expect.equal
            child.ExitCode
            0
            (error.Result
             + "\n"
             + output.Result)

        output.Result.Trim()
    finally
        Directory.Delete(directory, true)

let execute modules code = executeWithReferences [] modules code

let simpleModel = ModelResources.json "daily-rate-v3.json"

[<Tests>]
let tests =
    testList "Classic model generation" [
        testCase "a daily rate is emitted in its module"
        <| fun _ ->
            match Transpiler.Generate("car-rental.json", simpleModel) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal modules.Length 1 "one module"
                Expect.equal modules.Head.ModulePath "Inventory" "module identity"
                Expect.stringContains modules.Head.Source "dailyRate" "generated function"
                Expect.stringContains modules.Head.Source "5000" "model behavior"
        testCase "future versions fail rather than becoming version 3"
        <| fun _ ->
            match
                Transpiler.Generate(
                    "future.json",
                    simpleModel.Replace("\"formatVersion\":3", "\"formatVersion\":4")
                )
            with
            | Ok _ -> failtest "version 4 must not generate"
            | Error errors ->
                Expect.equal
                    errors.Head.Category
                    "unsupported-version"
                    "explicit version diagnostic"
        testCase "the package controls generated qualification"
        <| fun _ ->
            match
                Transpiler.Generate(
                    "other.json",
                    simpleModel.Replace("[\"car\"],[\"rental\"]", "[\"other\"],[\"domain\"]")
                )
            with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.stringContains
                    modules.Head.Source
                    "Other.Domain.Inventory"
                    "package namespace"
        testCase "typed records and curried billing execute across modules"
        <| fun _ ->
            let json = ModelResources.read "record-billing-v3.json"

            match Transpiler.Generate("record-billing.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "printfn \"%d\" (Car.Rental.Workflow.bill 3 Car.Rental.Workflow.car)")
                    "15000"
                    "generated billing behavior"
        testCase "the complete car rental model generates executable modules"
        <| fun _ ->
            let json = ModelResources.read "car-rental-v3.json"

            match Transpiler.Generate("car-rental-v3.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal modules.Length 7 "one file per model module"
                let output = execute modules "printfn \"%A\" (Car.Rental.Workflow.rent 3)"
                Expect.stringContains output "15000" "three days at 5000 cents"
        for version in [ 1; 2 ] do
            testCase $"classic version {version} executes the same rental"
            <| fun _ ->
                let json = ModelResources.read $"car-rental-v{version}.json"

                match Transpiler.Generate($"car-rental-v{version}.json", json) with
                | Error errors -> failtestf "%A" errors
                | Ok modules ->
                    Expect.stringContains
                        (execute modules "printfn \"%A\" (Car.Rental.Workflow.rent 3)")
                        "15000"
                        "equivalent rental"
        testCase "case-insensitive module file collisions fail"
        <| fun _ ->
            let root = JsonNode.Parse(simpleModel)
            let modules = root.["distribution"].[3].["modules"].AsArray()
            let duplicate = modules.[0].DeepClone()
            modules.[0].[0] <- System.Text.Json.JsonSerializer.SerializeToNode(F.p "a_bc")
            duplicate.[0] <- System.Text.Json.JsonSerializer.SerializeToNode(F.p "ab_c")
            modules.Add(duplicate)

            match Transpiler.Generate("collision.json", root.ToJsonString()) with
            | Ok _ -> failtest "module file collision must fail before publication"
            | Error errors ->
                Expect.equal errors.Head.Category "name-collision" "path collision category"
        testCase "independent generic functions remain polymorphic"
        <| fun _ ->
            let json =
                F.model [
                    "generic",
                    [],
                    [
                        F.binding
                            "identity"
                            [ "value", F.tVar "a" ]
                            (F.tVar "a")
                            (F.variable "value")
                        F.binding
                            "an_int"
                            []
                            F.intType
                            (F.apply (F.reference "generic" "identity") [ F.integer 3 ])
                        F.binding
                            "a_string"
                            []
                            F.stringType
                            (F.apply (F.reference "generic" "identity") [ F.str "hello" ])
                    ]
                ]

            match Transpiler.Generate("generic.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "printfn \"%d %s\" Car.Rental.Generic.anInt Car.Rental.Generic.aString")
                    "3 hello"
                    "generic use at distinct types"
        testCase "module references remain distinct from same-named parameters"
        <| fun _ ->
            let json =
                F.model [
                    "capture",
                    [],
                    [
                        F.binding "rate" [] F.intType (F.integer 5000)
                        F.binding
                            "charge"
                            [ "rate", F.intType ]
                            F.intType
                            (F.op "add" (F.reference "capture" "rate") (F.variable "rate"))
                    ]
                ]

            match Transpiler.Generate("capture.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute modules "printfn \"%d\" (Car.Rental.Capture.charge 7)")
                    "5007"
                    "qualified references retain module identity while variables retain lexical identity"
        testCase "module references survive every lexical binding form"
        <| fun _ ->
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
                    F.a [
                        F.a [ F.n "rate"; F.definition [ "n", F.intType ] F.intType recursiveBody ]
                    ]
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

            match Transpiler.Generate("capture.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute modules "printfn \"%A\" Car.Rental.Capture.locals")
                    "(5007, 5007, 5007, 5007, 5000)"
                    "local lets, lambdas, matches, destructuring and recursion preserve reference identity"
        testCase "unbound variables are generation errors"
        <| fun _ ->
            let json =
                F.model [ "scope", [], [ F.binding "broken" [] F.intType (F.variable "missing") ] ]

            match Transpiler.Generate("scope.json", json) with
            | Ok _ -> failtest "an unbound variable must not become successful source"
            | Error errors ->
                Expect.equal errors.Head.Category "unbound-variable" "scope diagnostic"
        testCase "local recursion and lexical shadowing execute"
        <| fun _ ->
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
                    F.a [
                        F.a [ F.n "loop"; F.definition [ "n", F.intType ] F.intType countdownBody ]
                    ]
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

            match Transpiler.Generate("locals.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "printfn \"%d %d\" (Car.Rental.Locals.countdown 5) (Car.Rental.Locals.shadow 2)")
                    "0 9"
                    "recursion and lexical binding"
        testCase "anonymous records and field functions execute"
        <| fun _ ->
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

            match Transpiler.Generate("records.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "let old = Car.Rental.Records.item\nlet changed = Car.Rental.Records.change old\nprintfn \"%d %d\" (Car.Rental.Records.accessor old) changed.value")
                    "3 7"
                    "anonymous record remains immutable"
        testCase "SDK list Maybe and Result mappings preserve function and type argument order"
        <| fun _ ->
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
                            (map
                                "maybe"
                                (F.apply (F.constructor (F.sdk "maybe" "just")) [ F.integer 3 ]))
                        F.binding
                            "result"
                            []
                            (F.tRef (F.sdk "result" "result") [ F.stringType; F.intType ])
                            (map
                                "result"
                                (F.apply (F.constructor (F.sdk "result" "ok")) [ F.integer 4 ]))
                    ]
                ]

            match Transpiler.Generate("mappings.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "printfn \"%A %A %A\" Car.Rental.Mappings.numbers Car.Rental.Mappings.maybe Car.Rental.Mappings.result")
                    "[2; 3] Some 4 Ok 5"
                    "SDK behavior"
        testCase "non-function initialization cycles fail before returning source"
        <| fun _ ->
            let json =
                F.model [
                    "cycle",
                    [],
                    [
                        F.binding "a" [] F.intType (F.reference "cycle" "b")
                        F.binding "b" [] F.intType (F.reference "cycle" "a")
                    ]
                ]

            match Transpiler.Generate("cycle.json", json) with
            | Ok _ -> failtest "initialization cycles must fail"
            | Error errors ->
                Expect.equal errors.Head.Category "initialization-cycle" "cycle diagnostic"
        testCase "generated rental source matches reviewed snapshots"
        <| fun _ ->
            let json = ModelResources.read "car-rental-v3.json"

            match Transpiler.Generate("car-rental-v3.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                let source =
                    modules
                    |> List.map (fun m ->
                        "// "
                        + m.RelativePath
                        + "\n"
                        + m.Source
                    )
                    |> String.concat "\n"

                VerifyExpecto.Verifier
                    .Verify("CarRentalSource", source)
                    .UseDirectory("Snapshots")
                    .GetAwaiter()
                    .GetResult()
                |> ignore
        for description, json, category in
            [
                "missing version",
                simpleModel.Replace("\"formatVersion\":3,", ""),
                "unsupported-version"
                "string version",
                simpleModel.Replace("\"formatVersion\":3", "\"formatVersion\":\"3\""),
                "unsupported-version"
                "fractional version",
                simpleModel.Replace("\"formatVersion\":3", "\"formatVersion\":2.5"),
                "unsupported-version"
                "zero version",
                simpleModel.Replace("\"formatVersion\":3", "\"formatVersion\":0"),
                "unsupported-version"
                "negative version",
                simpleModel.Replace("\"formatVersion\":3", "\"formatVersion\":-1"),
                "unsupported-version"
                "malformed JSON", ModelResources.read "malformed-model.txt", "parse"
                "unsupported distribution",
                simpleModel.Replace("\"Library\"", "\"DocumentTree\""),
                "unsupported-distribution"
                "unmapped SDK",
                F.model [
                    "unknown",
                    [],
                    [ F.binding "bad" [] F.intType (F.sdkReference "unknown" "operation") ]
                ],
                "unsupported-sdk"
                "unknown local reference",
                F.model [
                    "unknown",
                    [],
                    [ F.binding "bad" [] F.intType (F.reference "unknown" "missing") ]
                ],
                "unresolved-reference"
                "unknown external reference",
                F.model [
                    "unknown",
                    [],
                    [
                        F.binding
                            "bad"
                            []
                            F.intType
                            (F.a [
                                box "Reference"
                                null
                                F.a [ F.p "external.package"; F.p "module"; F.n "missing" ]
                            ])
                    ]
                ],
                "unresolved-reference"
                "integer narrowing", simpleModel.Replace("5000", "2147483648"), "literal-range"
                "open record",
                F.model [
                    "rows",
                    [
                        F.typeAlias
                            "open_row"
                            []
                            (F.a [ box "ExtensibleRecord"; null; F.n "row"; F.a [] ])
                    ],
                    []
                ],
                "unsupported-open-record"
            ] do
            testCase $"reject {description}"
            <| fun _ ->
                match Transpiler.Generate("bad-model.json", json) with
                | Ok _ -> failtest $"Expected rejection for {description}"
                | Error errors ->
                    Expect.equal errors.Head.Category category "diagnostic category"
                    Expect.equal errors.Head.InputPath "bad-model.json" "input source retained"
                    Expect.isNotEmpty errors.Head.Location "available location"
        testCase "module dependency cycles fail"
        <| fun _ ->
            let json =
                F.model [
                    "left", [], [ F.binding "a" [] F.intType (F.reference "right" "b") ]
                    "right", [], [ F.binding "b" [] F.intType (F.reference "left" "a") ]
                ]

            match Transpiler.Generate("cycle.json", json) with
            | Ok _ -> failtest "a cycle across separate module files is unsupported"
            | Error errors ->
                Expect.equal errors.Head.Category "module-cycle" "module cycle category"
        testCase "empty libraries and modules are valid"
        <| fun _ ->
            Expect.equal (Transpiler.Generate("empty.json", F.model [])) (Ok []) "empty manifest"

            match Transpiler.Generate("empty-module.json", F.model [ "empty", [], [] ]) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal (execute modules "printfn \"ok\"") "ok" "empty module compiles"
        testCase "F# reserved names preserve their identity"
        <| fun _ ->
            let json =
                F.model [
                    "keywords",
                    [],
                    [ F.binding "type" [ "match", F.intType ] F.intType (F.variable "match") ]
                ]

            match Transpiler.Generate("keywords.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute modules "printfn \"%d\" (Car.Rental.Keywords.``type`` 4)")
                    "4"
                    "escaped keywords compile"
        testCase "tuple aliases destructuring literal patterns and unit patterns execute"
        <| fun _ ->
            let tupleType = F.tTuple [ F.intType; F.intType ]

            let pairPattern =
                F.a [ box "TuplePattern"; null; F.a [ F.bind "first"; F.bind "second" ] ]

            let aliasPattern = F.a [ box "AsPattern"; null; pairPattern; F.n "whole" ]

            let literalPattern =
                F.a [ box "LiteralPattern"; null; F.a [ box "WholeNumberLiteral"; box 0 ] ]

            let classify =
                F.matchCases (F.variable "n") [
                    literalPattern, F.integer 9
                    F.wildcard, F.integer 4
                ]

            let destructure =
                F.a [
                    box "Destructure"
                    null
                    aliasPattern
                    F.variable "pair"
                    F.op "add" (F.variable "first") (F.variable "second")
                ]

            let unitPattern = F.a [ box "UnitPattern"; null ]
            let unitValue = F.a [ box "Unit"; null ]

            let json =
                F.model [
                    "patterns",
                    [ F.typeAlias "pair" [] tupleType ],
                    [
                        F.binding
                            "sum"
                            [ "pair", F.tRef (F.own "patterns" "pair") [] ]
                            F.intType
                            destructure
                        F.binding "classify" [ "n", F.intType ] F.intType classify
                        F.binding
                            "unit_case"
                            [ "x", F.unitType ]
                            F.unitType
                            (F.matchCases (F.variable "x") [ unitPattern, unitValue ])
                    ]
                ]

            match Transpiler.Generate("patterns.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "Car.Rental.Patterns.unitCase ()\nprintfn \"%d %d %d\" (Car.Rental.Patterns.sum (2, 5)) (Car.Rental.Patterns.classify 0) (Car.Rental.Patterns.classify 1)")
                    "7 9 4"
                    "pattern branches and bindings"
        testCase "generic records and curried recursive union constructors compile"
        <| fun _ ->
            let tree = F.tRef (F.own "trees" "tree") []

            let leaf n =
                F.apply (F.constructor (F.own "trees" "leaf")) [ F.integer n ]

            let json =
                F.model [
                    "trees",
                    [
                        F.typeAlias "box" [ "a" ] (F.tRecord [ "item", F.tVar "a" ])
                        F.customType "tree" [] [
                            "leaf", [ "value", F.intType ]
                            "branch", [ "left", tree; "right", tree ]
                        ]
                    ],
                    [
                        F.binding
                            "boxed"
                            []
                            (F.tRef (F.own "trees" "box") [ F.intType ])
                            (F.a [ box "Record"; null; F.a [ F.a [ F.n "item"; F.integer 6 ] ] ])
                        F.binding
                            "left_branch"
                            []
                            (F.tFunction tree tree)
                            (F.apply (F.constructor (F.own "trees" "branch")) [ leaf 1 ])
                        F.binding
                            "tree"
                            []
                            tree
                            (F.apply (F.reference "trees" "left_branch") [ leaf 2 ])
                    ]
                ]

            match Transpiler.Generate("trees.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "printfn \"%d %A\" Car.Rental.Trees.boxed.item Car.Rental.Trees.tree")
                    "6 Branch (Leaf 1, Leaf 2)"
                    "generic record and constructor currying"
        testCase "all literal families retain invariant syntax and escaping"
        <| fun _ ->
            let floatType = F.tRef (F.sdk "basics" "float") []
            let charType = F.tRef (F.sdk "char" "char") []
            let decimalType = F.tRef (F.sdk "decimal" "decimal") []

            let types =
                F.tTuple [ F.boolType; charType; F.stringType; F.intType; floatType; decimalType ]

            let body =
                F.a [
                    box "Tuple"
                    null
                    F.a [
                        F.literal "BoolLiteral" (box true)
                        F.literal "CharLiteral" (box "'")
                        F.str "a\n\"b"
                        F.integer Int32.MinValue
                        F.literal "FloatLiteral" (box 1.5)
                        F.literal "DecimalLiteral" (box 2.25M)
                    ]
                ]

            match
                Transpiler.Generate(
                    "literals.json",
                    F.model [ "literals", [], [ F.binding "values" [] types body ] ]
                )
            with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "let a,b,c,d,e,f = Car.Rental.Literals.values\nprintfn \"%b %d %d %d %.1f %.2M\" a (int b) c.Length d e f")
                    "true 39 4 -2147483648 1.5 2.25"
                    "literal values"
        testCase "unit-array record attributes do not become type annotations"
        <| fun _ ->
            let recordType = F.tRecord [ "value", F.intType ]

            let rawRecord =
                F.a [ box "Record"; F.a []; F.a [ F.a [ F.n "value"; F.integer 8 ] ] ]

            match
                Transpiler.Generate(
                    "raw-record.json",
                    F.model [ "raw", [], [ F.binding "item" [] recordType rawRecord ] ]
                )
            with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute modules "printfn \"%d\" Car.Rental.Raw.item.value")
                    "8"
                    "raw attributes are ignored"
        testCase "object metadata can carry a nested nominal record type"
        <| fun _ ->
            let vehicle = F.tRef (F.own "metadata" "vehicle") []
            let holder = F.tRef (F.own "metadata" "holder") []

            let record =
                F.a [
                    box "Record"
                    box {| ``type`` = vehicle |}
                    F.a [ F.a [ F.n "value"; F.integer 8 ] ]
                ]

            let json =
                F.model [
                    "metadata",
                    [
                        F.typeAlias "vehicle" [] (F.tRecord [ "value", F.intType ])
                        F.customType "holder" [] [ "holder", [ "vehicle", vehicle ] ]
                    ],
                    [
                        F.binding
                            "item"
                            []
                            holder
                            (F.apply (F.constructor (F.own "metadata" "holder")) [ record ])
                    ]
                ]

            match Transpiler.Generate("metadata.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute modules "printfn \"%A\" Car.Rental.Metadata.item")
                    "Holder { value = 8 }"
                    "nominal nested record annotation"
        testCase "known nominal record types propagate through containers and applications"
        <| fun _ ->
            let vehicle = F.tRef (F.own "expected" "vehicle") []
            let holder = F.tRef (F.own "expected" "holder") []

            let record =
                F.a [
                    box "Record"
                    box {| note = "untyped" |}
                    F.a [ F.a [ F.n "value"; F.integer 8 ] ]
                ]

            let json =
                F.model [
                    "expected",
                    [
                        F.typeAlias "vehicle" [] (F.tRecord [ "value", F.intType ])
                        F.customType "holder" [] [ "holder", [ "vehicle", vehicle ] ]
                        F.typeAlias "cell" [ "a" ] (F.tRecord [ "value", F.tVar "a" ])
                    ],
                    [
                        F.binding
                            "items"
                            []
                            (F.listType vehicle)
                            (F.a [ box "List"; null; F.a [ record ] ])
                        F.binding
                            "pair"
                            []
                            (F.tTuple [ vehicle; F.intType ])
                            (F.a [ box "Tuple"; null; F.a [ record; F.integer 2 ] ])
                        F.binding
                            "held"
                            []
                            holder
                            (F.apply (F.constructor (F.own "expected" "holder")) [ record ])
                        F.binding
                            "identity_vehicle"
                            [ "vehicle", vehicle ]
                            vehicle
                            (F.variable "vehicle")
                        F.binding
                            "via_call"
                            []
                            vehicle
                            (F.apply (F.reference "expected" "identity_vehicle") [ record ])
                        F.binding
                            "generic"
                            []
                            (F.tRef (F.own "expected" "cell") [ vehicle ])
                            (F.a [ box "Record"; null; F.a [ F.a [ F.n "value"; record ] ] ])
                    ]
                ]

            match Transpiler.Generate("expected.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                Expect.equal
                    (execute
                        modules
                        "let pair,_ = Car.Rental.Expected.pair\nprintfn \"%d %d %d %d %A\" Car.Rental.Expected.items.Head.value pair.value Car.Rental.Expected.viaCall.value Car.Rental.Expected.generic.value.value Car.Rental.Expected.held")
                    "8 8 8 8 Holder { value = 8 }"
                    "nominal record context"
    ]

[<Tests>]
let contractTests =
    testList "Generation contracts" [
        testCase "unrenderable model declarations return a source diagnostic"
        <| fun _ ->
            let json = F.model [ "empty_union", [ F.customType "impossible" [] [] ], [] ]

            match Transpiler.Generate("empty-union.json", json) with
            | Ok _ -> failtest "An unrenderable declaration returned successful source"
            | Error diagnostics ->
                Expect.equal
                    diagnostics.Head.Category
                    "fsharp-generation"
                    "formatter failures have a stable category"

                Expect.equal diagnostics.Head.InputPath "empty-union.json" "model path retained"
                Expect.isNotEmpty diagnostics.Head.Message "rendering reason retained"
        testCase "SDK Order uses its documented runtime reference"
        <| fun _ ->
            let orderType = F.tRef (F.sdk "basics" "order") []

            let json =
                F.model [
                    "ordering",
                    [],
                    [
                        F.binding "less" [] orderType (F.constructor (F.sdk "basics" "l_t"))
                        F.binding
                            "compare"
                            [ "left", F.intType; "right", F.intType ]
                            orderType
                            (F.apply (F.sdkReference "basics" "compare") [
                                F.variable "left"
                                F.variable "right"
                            ])
                    ]
                ]

            match Transpiler.Generate("order.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                let runtime = typeof<Morphir.SDK.Order>.Assembly.Location

                let code =
                    "printfn \"%b\" (Car.Rental.Ordering.compare 1 2 = Car.Rental.Ordering.less)"

                Expect.equal
                    (executeWithReferences [ runtime ] modules code)
                    "true"
                    "Order comparison semantics"
        testCase "nested module paths retain separate files in dependency order"
        <| fun _ ->
            let json =
                F.model [
                    "domain.rules",
                    [],
                    [
                        F.binding
                            "total"
                            []
                            F.intType
                            (F.op "multiply" (F.reference "domain.types" "rate") (F.integer 3))
                    ]
                    "other.types", [], [ F.binding "rate" [] F.intType (F.integer 9) ]
                    "domain.types", [], [ F.binding "rate" [] F.intType (F.integer 5000) ]
                ]

            match Transpiler.Generate("nested.json", json) with
            | Error errors -> failtestf "%A" errors
            | Ok modules ->
                let paths =
                    modules
                    |> List.map (fun module' -> module'.RelativePath)

                Expect.isLessThan
                    (List.findIndex ((=) "Domain/Types.fs") paths)
                    (List.findIndex ((=) "Domain/Rules.fs") paths)
                    "dependency precedes dependent"

                Expect.contains paths "Other/Types.fs" "same leaf name remains distinct"

                Expect.equal
                    (execute modules "printfn \"%d\" Car.Rental.Domain.Rules.total")
                    "15000"
                    "nested qualified reference"
        testCase "duplicate definition maps are rejected before source emission"
        <| fun _ ->
            for duplicateModule in [ true; false ] do
                let json = JsonNode.Parse(simpleModel)
                let modules = json.["distribution"].[3].["modules"].AsArray()

                if duplicateModule then
                    modules.Add(modules.[0].DeepClone())
                else
                    let values = modules.[0].[1].["value"].["values"].AsArray()
                    values.Add(values.[0].DeepClone())

                match Transpiler.Generate("duplicates.json", json.ToJsonString()) with
                | Ok _ -> failtest "A duplicate dictionary key generated source"
                | Error diagnostics ->
                    Expect.equal
                        diagnostics.Head.Category
                        "duplicate-key"
                        "duplicates have a model diagnostic"
        testCase "empty library and module layouts match reviewed snapshots"
        <| fun _ ->
            let generate json =
                match Transpiler.Generate("empty.json", json) with
                | Ok modules -> modules
                | Error errors -> failtestf "%A" errors

            let emptyLibrary = generate (F.model [])
            let emptyModule = generate (F.model [ "empty", [], [] ])

            let source =
                $"Empty library module count: {emptyLibrary.Length}\n\n"
                + (emptyModule
                   |> List.map (fun m ->
                       "// "
                       + m.RelativePath
                       + "\n"
                       + m.Source
                   )
                   |> String.concat "\n")

            VerifyExpecto.Verifier
                .Verify("EmptyLayouts", source)
                .UseDirectory("Snapshots")
                .GetAwaiter()
                .GetResult()
            |> ignore
        testCase "source is stable across classic versions paths cultures and map order"
        <| fun _ ->
            let source version =
                ModelResources.read $"car-rental-v{version}.json"

            let generate path json =
                match Transpiler.Generate(path, json) with
                | Ok modules -> modules
                | Error errors -> failtestf "%A" errors

            let expected = generate "first/model.json" (source 3)
            Expect.equal (generate "version1.json" (source 1)) expected "v1 declaration equivalence"
            Expect.equal (generate "version2.json" (source 2)) expected "v2 declaration equivalence"
            let model = JsonNode.Parse(source 3)
            let modules = model.["distribution"].[3].["modules"].AsArray()

            let reversed =
                modules
                |> Seq.rev
                |> Seq.map (fun node -> node.DeepClone())
                |> Seq.toArray

            modules.Clear()

            reversed
            |> Array.iter modules.Add

            let previousCulture = System.Globalization.CultureInfo.CurrentCulture

            try
                System.Globalization.CultureInfo.CurrentCulture <-
                    System.Globalization.CultureInfo.GetCultureInfo("tr-TR")

                Expect.equal
                    (generate "another directory/model.json" (model.ToJsonString()))
                    expected
                    "stable source and ordered manifest"
            finally
                System.Globalization.CultureInfo.CurrentCulture <- previousCulture
        testCase "private values constructors and modules stay inaccessible to another assembly"
        <| fun _ ->
            let hiddenType = F.tRef (F.own "privacy" "secret") []

            let json =
                JsonNode.Parse(
                    F.model [
                        "privacy",
                        [ F.customType "secret" [] [ "hidden", [ "value", F.intType ] ] ],
                        [
                            F.binding "secret_value" [] F.intType (F.integer 7)
                            F.binding "reveal" [] F.intType (F.reference "privacy" "secret_value")
                            F.binding
                                "make"
                                [ "value", F.intType ]
                                hiddenType
                                (F.apply (F.constructor (F.own "privacy" "hidden")) [
                                    F.variable "value"
                                ])
                        ]
                        "hidden", [], [ F.binding "number" [] F.intType (F.integer 9) ]
                    ]
                )

            let definitions = json.["distribution"].[3].["modules"].AsArray()

            definitions.[0].[1].["value"].["types"].[0].[1].["value"].[2].["access"] <-
                JsonValue.Create("Private")

            definitions.[0].[1].["value"].["values"].[0].[1].["access"] <-
                JsonValue.Create("Private")

            definitions.[1].[1].["access"] <- JsonValue.Create("Private")

            let modules =
                match Transpiler.Generate("privacy.json", json.ToJsonString()) with
                | Ok modules -> modules
                | Error errors -> failtestf "%A" errors

            let directory =
                Path.Combine(
                    Path.GetTempPath(),
                    "morphir privacy "
                    + Guid.NewGuid().ToString("N")
                )

            Directory.CreateDirectory(directory)
            |> ignore

            let dotnet =
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                |> Option.ofObj
                |> Option.defaultValue "dotnet"

            let run arguments =
                let start = ProcessStartInfo(dotnet)
                start.WorkingDirectory <- directory
                start.RedirectStandardOutput <- true
                start.RedirectStandardError <- true

                for argument in arguments do
                    start.ArgumentList.Add(argument)

                use child = Process.Start(start)
                let output = child.StandardOutput.ReadToEndAsync()
                let error = child.StandardError.ReadToEndAsync()

                if not (child.WaitForExit(60000)) then
                    child.Kill(true)
                    failtest "privacy compiler timeout"

                child.ExitCode,
                output.Result
                + error.Result

            try
                for module' in modules do
                    File.WriteAllText(Path.Combine(directory, module'.RelativePath), module'.Source)

                let compile =
                    modules
                    |> List.map (fun m -> $"<Compile Include=\"{m.RelativePath}\" />")
                    |> String.concat ""

                File.WriteAllText(
                    Path.Combine(directory, "Privacy.fsproj"),
                    $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>{compile}</ItemGroup></Project>"
                )

                let outputDirectory = Path.Combine(directory, "compiled")

                let code, diagnostics =
                    run [
                        "build"
                        "Privacy.fsproj"
                        "-c"
                        "Release"
                        "-o"
                        outputDirectory
                        "--nologo"
                    ]

                Expect.equal code 0 diagnostics
                let assembly = Path.Combine(outputDirectory, "Privacy.dll")

                let compileConsumer body =
                    let script = Path.Combine(directory, "Consumer.fsx")
                    File.WriteAllText(script, $"#r @\"{assembly}\"\n{body}")
                    run [ "fsi"; "--exec"; script ]

                let publicCode, publicOutput =
                    compileConsumer "printfn \"%d\" Car.Rental.Privacy.reveal"

                Expect.equal publicCode 0 publicOutput
                Expect.stringContains publicOutput "7" "public value can use private helper"

                for expression in
                    [
                        "Car.Rental.Privacy.secretValue"
                        "Car.Rental.Privacy.Hidden 1"
                        "Car.Rental.Hidden.number"
                    ] do
                    let privateCode, privateOutput = compileConsumer ($"let value = {expression}")

                    Expect.notEqual
                        privateCode
                        0
                        ($"Private declaration escaped: {expression}\n{privateOutput}")
            finally
                Directory.Delete(directory, true)
    ]

[<Tests>]
let optimizationEquivalenceTests =
    let generated optimize (path: string, json: string) =
        match Transpiler.GenerateWith optimize (path, json) with
        | Ok modules -> modules
        | Error errors -> failtestf "%A" errors

    let sourceText (modules: GeneratedModule list) =
        modules |> List.map (fun m -> m.Source) |> String.concat "\n"

    let floatType = F.tRef (F.sdk "basics" "float") []
    let flt (value: float) = F.literal "FloatLiteral" (box value)

    let foldable =
        F.model [
            "folding",
            [],
            [
                F.binding "added" [] F.intType (F.op "add" (F.integer 40) (F.integer 2))
                F.binding "nested" [] F.intType (F.op "multiply" (F.op "add" (F.integer 1) (F.integer 2)) (F.integer 4))
                F.binding "subtracted" [] F.intType (F.op "subtract" (F.integer 2) (F.integer 5))
                F.binding "truncated" [] F.intType (F.op "divide" (F.integer -7) (F.integer 2))
                F.binding "overflowing" [] F.intType (F.op "add" (F.integer 2147483647) (F.integer 1))
                F.binding "floats" [] floatType (F.op "divide" (flt 1.0) (flt 4.0))
                F.binding "less" [] F.boolType (F.op "less_than" (F.integer 1) (F.integer 2))
                F.binding "not_less" [] F.boolType (F.op "greater_than_or_equal" (F.integer 1) (F.integer 2))
                F.binding "same" [] F.boolType (F.op "equal" (F.str "a") (F.str "a"))
                F.binding "different" [] F.boolType (F.op "not_equal" (F.str "a") (F.str "a"))
            ]
        ]

    let printAll =
        [ "added"; "nested"; "subtracted"; "truncated"; "overflowing"; "floats"; "less"; "notLess"; "same"; "different" ]
        |> List.map (fun name -> $"printfn \"{name} %%A\" Car.Rental.Folding.{name}")
        |> String.concat "\n"

    testList "Optimization equivalence" [
        testCase "folded expressions should behave the same as the unoptimized build"
        <| fun _ ->
            let optimized = generated true ("folding.json", foldable)
            let plain = generated false ("folding.json", foldable)

            Expect.notEqual (sourceText optimized) (sourceText plain) "folding should change the source"

            let optimizedOutput = execute optimized printAll
            let plainOutput = execute plain printAll

            Expect.equal optimizedOutput plainOutput "same results with optimization on and off"
            Expect.stringContains optimizedOutput "added 42" "the fixture should have run"
            Expect.stringContains optimizedOutput "truncated -3" "division should truncate"
        for version in [ 1; 2; 3 ] do
            testCase $"car rental version {version} should behave the same with optimization on and off"
            <| fun _ ->
                let input = ($"car-rental-v{version}.json", ModelResources.read $"car-rental-v{version}.json")
                let consumer = "printfn \"%A\" (Car.Rental.Workflow.rent 3)"
                let optimizedOutput = execute (generated true input) consumer
                let plainOutput = execute (generated false input) consumer

                Expect.equal optimizedOutput plainOutput "same results with optimization on and off"
                Expect.stringContains optimizedOutput "15000" "three days at 5000 cents"
    ]

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs
        []
        argv
        (testList "Backend" [
            tests
            contractTests
            optimizationEquivalenceTests
            PipelineGenerationTests.treeTests
            PipelineGenerationTests.orderTests
            PipelineGenerationTests.validationTests
            PipelineGenerationTests.optimizationTests
            PipelineGenerationTests.failureTests
        ])
