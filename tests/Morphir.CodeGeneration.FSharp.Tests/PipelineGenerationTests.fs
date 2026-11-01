module Morphir.CodeGeneration.FSharp.Tests.PipelineGenerationTests

open System.IO
open System.Text
open Expecto
open Morphir.CodeGeneration.FSharp
open Morphir.IR.Pipeline
open Morphir.Testing
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

module F = Morphir.CodeGeneration.FSharp.Tests.Fixtures

let private simpleModel = ModelResources.json "daily-rate-v3.json"
let private generate = Morphir.CodeGeneration.FSharp.Transpiler.generate
let private streamOf (bytes: byte[]) = StreamSource(fun () -> new MemoryStream(bytes) :> Stream)

let private locations (tree: VFileTree) =
    tree |> VFileTree.toFileMap |> Map.toList |> List.map fst

[<Tests>]
let treeTests =
    testList "Generation through the pipeline" [
        test "a model should become a tree with one file per module" {
            let tree = generate (VFile.ofText "car-rental-v3.json" (ModelResources.json "car-rental-v3.json"))

            Expect.isFalse (VFileTree.hasErrors tree) "should succeed"
            Expect.equal (VFileTree.fileCount tree) 7 "seven modules"

            Expect.equal
                (locations tree)
                [ "Billing.fs"; "Domain.fs"; "Inventory.fs"; "Pickup.fs"; "Reservation.fs"; "Return.fs"; "Workflow.fs" ]
                "the same relative locations as before"
        }

        test "nested module paths should become directories" {
            let json =
                F.model [
                    "domain.types", [], [ F.binding "a" [] F.intType (F.integer 1) ]
                    "domain.rules", [], [ F.binding "b" [] F.intType (F.integer 2) ]
                ]

            let tree = generate (VFile.ofText "nested.json" json)

            Expect.equal (locations tree) [ "Domain/Rules.fs"; "Domain/Types.fs" ] "file locations"
            Expect.equal (VFileTree.directoryCount tree) 1 "one Domain directory"
        }

        test "stream content should generate the same output as text" {
            let fromText = generate (VFile.ofText "m.json" simpleModel)
            let fromStream = generate (VFile.ofStream "m.json" (streamOf (Encoding.UTF8.GetBytes simpleModel)))

            Expect.equal (modulesOf fromStream) (modulesOf fromText) "same modules"
        }

        test "bytes content should generate the same output as text" {
            let fromText = generate (VFile.ofText "m.json" simpleModel)
            let fromBytes = generate (VFile.create "m.json" (Encoding.UTF8.GetBytes simpleModel))

            Expect.equal (modulesOf fromBytes) (modulesOf fromText) "same modules"
        }

        test "a model with a UTF-8 byte order mark should generate" {
            let bytes = Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] (Encoding.UTF8.GetBytes simpleModel)

            Expect.isFalse (VFileTree.hasErrors (generate (VFile.ofStream "m.json" (streamOf bytes)))) "should succeed"
        }

        test "a model with a UTF-16 byte order mark should still generate" {
            let bytes = Array.append [| 0xFFuy; 0xFEuy |] (Encoding.Unicode.GetBytes simpleModel)

            Expect.isFalse (VFileTree.hasErrors (generate (VFile.ofStream "m.json" (streamOf bytes)))) "should succeed"
        }

        test "the model should stay usable after the JSON document is gone" {
            // Emission reads attributes that point into the JSON, so this fails if the parser
            // returns elements from a disposed document.
            let tree = generate (VFile.ofText "car-rental-v3.json" (ModelResources.json "car-rental-v3.json"))

            Expect.isNonEmpty (modulesOf tree) "should emit from attributes"
        }

        test "a stream source should not be held open" {
            let path = Path.GetTempFileName()

            try
                File.WriteAllText(path, simpleModel)
                generate (VFile.fromDisk path) |> ignore
                File.Delete path

                Expect.isFalse (File.Exists path) "should be able to delete the file after generating"
            finally
                if File.Exists path then File.Delete path
        }
    ]

[<Tests>]
let orderTests =
    testList "Generation order" [
        test "the manifest should declare modules in dependency order" {
            let tree = generate (VFile.ofText "car-rental-v3.json" (ModelResources.json "car-rental-v3.json"))
            let names = modulesOf tree |> List.map (fun m -> m.ModulePath)
            let index name = List.findIndex ((=) name) names

            Expect.isLessThan (index "Domain") (index "Inventory") "Domain comes first"
            Expect.isLessThan (index "Inventory") (index "Reservation") "Inventory before Reservation"
            Expect.equal names.[names.Length - 1] "Workflow" "Workflow depends on the others"
        }

        test "the declared order should list each file exactly once" {
            let tree = generate (VFile.ofText "car-rental-v3.json" (ModelResources.json "car-rental-v3.json"))
            let order = VFileTree.tryGet VFileTree.declaredOrder tree |> Option.get

            Expect.equal (List.sort order) (locations tree) "same set of locations"
            Expect.equal order (modulesOf tree |> List.map (fun m -> m.RelativePath)) "same order as the manifest"
        }

        test "order should be able to cross directories" {
            let z = F.reference "z.first" "a"

            let json =
                F.model [
                    "a.second", [], [ F.binding "b" [] F.intType z ]
                    "z.first", [], [ F.binding "a" [] F.intType (F.integer 1) ]
                ]

            let tree = generate (VFile.ofText "order.json" json)

            Expect.equal
                (VFileTree.tryGet VFileTree.declaredOrder tree)
                (Some [ "Z/First.fs"; "A/Second.fs" ])
                "the dependency comes first, against directory order"
        }

        test "generation should be deterministic" {
            let run () = modulesOf (generate (VFile.ofText "m.json" (ModelResources.json "car-rental-v3.json")))

            Expect.equal (run ()) (run ()) "same output every time"
        }

        test "the tree should be ready to publish" {
            let tree = generate (VFile.ofText "m.json" (ModelResources.json "car-rental-v3.json"))
            let options = { OutputRoot = Path.Combine(Path.GetTempPath(), "unused"); Manifest = ManifestMode.Manifest "manifest.json" }

            Expect.equal (VFileTree.validatePublish options tree) (Ok ()) "should pass validation"
        }
    ]

[<Tests>]
let validationTests =
    testList "Generation validation" [
        testList "supported fixtures should produce no warnings or errors" [
            for version in [ 1; 2; 3 ] do
                test $"car rental version {version}" {
                    let tree = generate (VFile.ofText "m.json" (ModelResources.json $"car-rental-v{version}.json"))
                    let noisy =
                        VFileTree.collectMessages tree
                        |> List.filter (fun message -> message.Severity <> MessageSeverity.Info)

                    Expect.isEmpty noisy "should be quiet"
                }
        ]
    ]

[<Tests>]
let optimizationTests =
    let yes = F.literal "BoolLiteral" (box true)
    let x = F.variable "x"
    let sourceOf (tree: VFileTree) = (modulesOf tree).Head.Source

    testList "Generation optimization" [
        test "SDK addition of two literals should fold and be recorded" {
            let json = F.model [ "rules", [], [ F.binding "total" [] F.intType (F.op "add" (F.integer 40) (F.integer 2)) ] ]
            let tree = VFile.ofText "m.json" json |> generate

            Expect.stringContains (sourceOf tree) "42" "the generated source should hold the folded literal"

            let info = tree.Messages |> List.filter (fun message -> message.Severity = MessageSeverity.Info)

            Expect.isNonEmpty info "should record an informational diagnostic"
            Expect.equal info.Head.Location (Some "Rules.total") "should name the value"
        }

        test "a user function named add should not fold" {
            let json =
                F.model [
                    "rules",
                    [],
                    [
                        F.binding "add" [ "a", F.intType; "b", F.intType ] F.intType (F.variable "a")
                        F.binding "total" [] F.intType (F.apply (F.reference "rules" "add") [ F.integer 40; F.integer 2 ])
                    ]
                ]

            let source = sourceOf (VFile.ofText "m.json" json |> generate)

            Expect.isFalse (source.Contains "42") "the user's function must run"
        }

        test "a sum out of range should not fold" {
            let json =
                F.model [ "rules", [], [ F.binding "total" [] F.intType (F.op "add" (F.integer 2147483647L) (F.integer 1)) ] ]

            Expect.isFalse ((sourceOf (VFile.ofText "m.json" json |> generate)).Contains "2147483648") "should not fold"
        }

        test "an input out of range should stay rejected even if the result is in range" {
            let json =
                F.model [ "rules", [], [ F.binding "total" [] F.intType (F.op "add" (F.integer 3000000000L) (F.integer -2000000000L)) ] ]

            let tree = VFile.ofText "m.json" json |> generate

            Expect.equal (diagnosticsOf tree).Head.Category "literal-range" "folding must not accept it"
        }

        test "the unmapped SDK and should stay rejected with a literal first argument" {
            let json =
                F.model [ "rules", [], [ F.binding "total" [ "x", F.boolType ] F.boolType (F.op "and" yes x) ] ]

            let tree = VFile.ofText "m.json" json |> generate

            Expect.equal (diagnosticsOf tree).Head.Category "unsupported-sdk" "should not start generating"
        }

        test "an unsupported reference in a dead branch should stay rejected" {
            let deadBranch = F.a [ box "IfThenElse"; null; yes; F.integer 1; F.sdkReference "basics" "unmapped_function" ]
            let json = F.model [ "rules", [], [ F.binding "total" [] F.intType deadBranch ] ]

            let tree = VFile.ofText "m.json" json |> generate

            Expect.equal (diagnosticsOf tree).Head.Category "unsupported-sdk" "should not start generating"
        }
    ]

[<Tests>]
let failureTests =
    let expectRule rule (tree: VFileTree) =
        Expect.isEmpty (VFileTree.allFiles tree) "should produce no files"
        Expect.isTrue (VFileTree.hasErrors tree) "should report an error"

        let first = (diagnosticsOf tree).Head

        Expect.equal first.Category rule "should keep the rule"
        Expect.equal first.InputPath "bad.json" "should name the input"
        Expect.isNotEmpty first.Location "should have a location"
        Expect.isNotEmpty first.Message "should have a message"

    testList "Generation failures" [
        test "an unsupported version should keep its rule and produce no files" {
            simpleModel.Replace("\"formatVersion\":3", "\"formatVersion\":4")
            |> VFile.ofText "bad.json"
            |> generate
            |> expectRule "unsupported-version"
        }

        test "malformed JSON should keep the parse rule" {
            VFile.ofText "bad.json" "{ not json" |> generate |> expectRule "parse"
        }

        test "a failure while compiling should keep its rule" {
            let cycle =
                F.model [
                    "first", [], [ F.binding "x" [] F.intType (F.reference "second" "y") ]
                    "second", [], [ F.binding "y" [] F.intType (F.reference "first" "x") ]
                ]

            VFile.ofText "bad.json" cycle |> generate |> expectRule "module-cycle"
        }

        test "an unbound variable should keep its rule and name the value" {
            let json = F.model [ "rules", [], [ F.binding "total" [] F.intType (F.variable "missing") ] ]
            let tree = VFile.ofText "bad.json" json |> generate

            expectRule "unbound-variable" tree
            Expect.equal (diagnosticsOf tree).Head.Location "Rules.total" "should name the value"
        }

        test "a definite type mismatch should stop generation and name the value" {
            let json = F.model [ "rules", [], [ F.binding "total" [] F.intType (F.str "not a number") ] ]
            let tree = VFile.ofText "bad.json" json |> generate

            expectRule "type-mismatch" tree
            Expect.equal (diagnosticsOf tree).Head.Location "Rules.total" "should name the value"
        }

        test "a model alias over a literal should not be a type error" {
            let alias = F.typeAlias "cents" [] F.intType

            let json =
                F.model [ "rules", [ alias ], [ F.binding "rate" [] (F.tRef (F.own "rules" "cents") []) (F.integer 5000) ] ]

            let tree = VFile.ofText "alias.json" json |> generate

            Expect.isFalse (VFileTree.hasErrors tree) "an alias is unknown, so it cannot conflict"
        }

        test "a missing file should report its path" {
            let missing = Path.Combine(Path.GetTempPath(), "morphir-missing-" + System.Guid.NewGuid().ToString("N") + ".json")
            let tree = generate (VFile.fromDisk missing)

            Expect.isEmpty (VFileTree.allFiles tree) "should produce no files"
            Expect.equal (diagnosticsOf tree).Head.InputPath missing "should name the file"
        }

        test "a file with no content should report an error" {
            let tree = generate (VFile.fromPath "bad.json")

            Expect.isTrue (VFileTree.hasErrors tree) "there is nothing to parse"
        }

        test "a failed tree should describe its errors in the build's format" {
            let tree =
                simpleModel.Replace("\"formatVersion\":3", "\"formatVersion\":4")
                |> VFile.ofText "bad.json"
                |> generate

            let description = tree.Messages |> List.map VMessage.describe |> List.head

            Expect.stringStarts description "bad.json: unsupported-version at $.formatVersion: " "path, rule and location"
        }
    ]
