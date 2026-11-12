// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The data integrity of the fixed F# readiness workflow case, the checks the readiness workflow page lists under
/// "Checks in this repository". It reads committed files only: it compiles and generates nothing. It replaces
/// finos/morphir's <c>readiness_workflow.rs</c> (crates/morphir-toolchain-contract, at <c>dde74f94</c>), reading the
/// recipes and the receipt with Morphir.Toolchain's readers. Schema validation and the listed digests are in
/// <see cref="SchemaTests.workflowCaseTests"/>.
/// </summary>
module Morphir.Toolchain.Tests.WorkflowCaseTests

open System.IO
open System.Text
open System.Text.Json
open Expecto
open Morphir.Toolchain

let private case () =
    JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures.workflow, "case.json")))

let private str (e: JsonElement) (name: string) = nonNull (e.GetProperty(name).GetString())

let private stepOf (root: JsonElement) (id: string) =
    root.GetProperty("steps").EnumerateArray()
    |> Seq.tryFind (fun step -> str step "id" = id)
    |> Option.defaultWith (fun () -> failtestf "the case has no step %s" id)

let private providers (step: JsonElement) = [ for p in step.GetProperty("providers").EnumerateArray() -> p ]

let private coveredBy (step: JsonElement) (delivery: string) =
    providers step
    |> List.tryFind (fun p -> str p "delivery" = delivery)
    |> Option.defaultWith (fun () -> failtestf "step %s covers no %s delivery" (str step "id") delivery)

let private sets (step: JsonElement) =
    match step.GetProperty("expected").TryGetProperty "sets" with
    | true, sets -> [ for set in sets.EnumerateArray() -> set ]
    | false, _ -> []

let private caseBytes (relative: string) = Fixtures.bytesIn Fixtures.workflow relative

let private modelText (profile: string) (codec: string) (irVersion: string) = $"{profile}/{codec}/{irVersion}"

let private caseModel (model: JsonElement) = modelText (str model "profile") (str model "codec") (str model "irVersion")

let private caseTuple (tuple: JsonElement) =
    [ "operation"; "profile"; "codec"; "envelopeVersion"; "irVersion" ] |> List.map (str tuple) |> String.concat "/"

let private recipeTuple (protocol: Protocol) =
    let tuple = protocol.Tuple

    [ ModelOperation.text tuple.Operation
      tuple.Profile.Text
      tuple.Codec.Text
      tuple.EnvelopeVersion.Text
      tuple.IrVersion.Text ]
    |> String.concat "/"

/// Every provider a recipe can select for an operation of the case is one the case covers, with the same identity,
/// protocol and model, so whatever a generation selected, the case runs with that provider.
let private coversRecipe (recipe: Recipe) =
    use document = case ()
    let root = document.RootElement
    Expect.equal recipe.Preset.Text (str root "preset") "the case's preset"

    for operation in recipe.Operations do
        let id, builtInMember =
            match operation with
            | Operation.Compile _ -> "compile", "modelOutput"
            | Operation.Generate _ -> "generate", "modelInput"

        let step = stepOf root id

        for host in recipe.Hosts do
            match host.BuiltIns |> List.tryFind (fun b -> b.Operation = operation) with
            | Some builtIn ->
                let covered = coveredBy step "built-in"
                Expect.equal builtIn.ExtensionId.Text (str covered "extensionId") $"{id}: the built-in's identity"
                let model = builtIn.Model
                Expect.equal (modelText model.Profile.Text model.Codec.Text model.IrVersion.Text) (caseModel (covered.GetProperty builtInMember)) $"{id}: the built-in's model"
            | None -> ()

        for provider in recipe.Providers |> List.filter (fun p -> p.Operation = operation) do
            let delivery = Delivery.name (RecipeProvider.delivery provider)
            let covered = coveredBy step delivery
            Expect.equal (ProtocolVersion.text provider.Protocol.Version) (str covered "protocolVersion") $"{id} {delivery}: the MEP version"
            Expect.equal (recipeTuple provider.Protocol) (caseTuple (covered.GetProperty "modelTuple")) $"{id} {delivery}: the model tuple"

let private recipeIn (bytes: byte array) (what: string) =
    match RecipeDocument.read bytes with
    | Ok recipe -> recipe
    | Error refusal -> failtestf "%s does not read: %A" what refusal

[<Tests>]
let tests =
    testList "The readiness workflow case's data" [
        testCase "every listed file is at a portable path inside the case, with its stated SHA-256 and length" (fun () ->
            use document = case ()
            let root = document.RootElement

            let checkedFile (relative: string) (digest: string) =
                Expect.isOk (TargetPath.tryParse relative) $"{relative} is a portable path"
                Expect.isFalse (relative.Split('/') |> Array.contains "..") $"{relative} stays inside the case"
                let bytes = caseBytes relative
                Expect.equal (Sha256.ofBytes bytes).Text digest $"{relative} has its stated SHA-256"
                bytes

            for source in root.GetProperty("sources").EnumerateArray() do
                let bytes = checkedFile (str source "file") (str source "sha256")
                Expect.isTrue (try UTF8Encoding(false, true).GetString bytes |> ignore; true with _ -> false) "sources are UTF-8"
                Expect.isOk (TargetPath.tryParse (str source "uri")) "the source uri is a portable path"

            let ir = (stepOf root "compile").GetProperty("expected").GetProperty "ir"
            checkedFile (str ir "file") (str ir "sha256") |> ignore

            for step in root.GetProperty("steps").EnumerateArray() do
                for set in sets step do
                    for file in set.GetProperty("files").EnumerateArray() do
                        let bytes = checkedFile (str file "file") (str file "sha256")
                        Expect.equal (int64 bytes.Length) (file.GetProperty("bytes").GetInt64()) (str file "file" + " has its stated length")
                        Expect.isOk (TargetPath.tryParse (str file "path")) "the generated path is portable")

        testCase "the expected IR is an IR v4 library distribution of the package, at the tuple's IR version" (fun () ->
            use document = case ()
            let root = document.RootElement
            let compile = stepOf root "compile"
            let entry = compile.GetProperty("expected").GetProperty "ir"
            use ir = JsonDocument.Parse(caseBytes (str entry "file"))
            let formatVersion = ir.RootElement.GetProperty("formatVersion").GetInt32()
            Expect.equal formatVersion (entry.GetProperty("formatVersion").GetInt32()) "formatVersion is the declared one"

            let tupleVersion =
                match SemVer.tryParse (str ((coveredBy compile "process").GetProperty "modelTuple") "irVersion") with
                | Ok version -> version
                | Error _ -> failtest "the tuple's irVersion is not SemVer"

            // Every covered provider exchanges the same IR version.
            for provider in providers compile do
                let model = if str provider "delivery" = "built-in" then provider.GetProperty "modelOutput" else provider.GetProperty "modelTuple"
                Expect.equal (str model "irVersion") tupleVersion.Text (str provider "delivery" + " exchanges the same IR version")

            Expect.equal (bigint formatVersion) tupleVersion.SemVersion.Major "formatVersion is the tuple's IR major version"
            Expect.equal formatVersion 4 "the IR v4 shape is checked here"

            let distribution = ir.RootElement.GetProperty "distribution"
            Expect.equal [ for p in distribution.EnumerateObject() -> p.Name ] [ "Library" ] "a library distribution"
            let library = distribution.GetProperty "Library"
            Expect.equal (str library "packageName") (str (root.GetProperty "package") "name") "the package's name"
            Expect.equal (library.GetProperty("dependencies").ValueKind) JsonValueKind.Object "dependencies is an object"

            // The exposed modules are single words, whose IR v4 module names are their lowercase spelling.
            let modules = [ for p in library.GetProperty("def").GetProperty("modules").EnumerateObject() -> p.Name ]
            let exposed = [ for m in (root.GetProperty "package").GetProperty("exposedModules").EnumerateArray() -> (nonNull (m.GetString())).ToLowerInvariant() ]
            Expect.equal modules exposed "the exposed modules"

            // The provenance names the corpus variant the case reuses.
            let basis = root.GetProperty "basis"
            Expect.equal (str (entry.GetProperty "provenance") "commit") (str basis "commit") "the provenance's commit is the basis'"
            Expect.equal (str (basis.GetProperty "variant") "input") (str compile "input") "the variant's input"
            Expect.equal (str (basis.GetProperty "variant") "version") (string tupleVersion.SemVersion.Major) "the variant's version")

        testCase "the generate step has one unformatted LF set for every delivery, with honest provenance" (fun () ->
            // The decision of 9 November 2026: one unformatted set, LF line endings, produced with formatting off by
            // every delivery, compared byte for byte.
            use document = case ()
            let generate = stepOf document.RootElement "generate"
            Expect.equal (str generate "formatting") "off" "formatting off"
            let expected = generate.GetProperty "expected"
            Expect.equal (str expected "status") "frozen" "frozen"

            match sets generate with
            | [ set ] ->
                Expect.isFalse (set.TryGetProperty "appliesTo" |> fst) "the set covers every delivery"
                Expect.equal (str set "comparison") "exact-bytes" "compared byte for byte"

                for file in set.GetProperty("files").EnumerateArray() do
                    let bytes = caseBytes (str file "file")
                    UTF8Encoding(false, true).GetString bytes |> ignore
                    // Bytes, not culture-sensitive text: StartsWith with a string ignores a zero-width U+FEFF.
                    Expect.isFalse (bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy) "no byte order mark"
                    Expect.isFalse (Array.contains 13uy bytes) "LF line endings only"
                    Expect.isTrue (bytes.Length > 0 && bytes[bytes.Length - 1] = 10uy && (bytes.Length = 1 || bytes[bytes.Length - 2] <> 10uy)) "exactly one final line feed"

                // The producers are not released and have been observed on one platform; a squash-merging repository
                // drops branch commits, so the provenance names the pull request.
                let provenance = set.GetProperty "provenance"
                Expect.equal (str provenance "kind") "generated" "generated"
                Expect.equal (str provenance "repository") "finos/morphir-dotnet" "the producing repository"

                // Since finos/morphir 837164580 the provenance names the pull request, as its schema requires; the pinned
                // case, from before it, names a branch. Each corpus is held to the rule its own schema states.
                let schemaText = File.ReadAllText(Path.Combine(Fixtures.schemas, "toolchain-workflow-case-0.1.0-draft.1.schema.json"))

                if schemaText.Contains "\"pullRequest\"" then
                    Expect.equal (provenance.GetProperty("pullRequest").GetInt32()) 578 "the producing pull request"
                    Expect.isFalse (provenance.TryGetProperty "branch" |> fst) "no branch"
                else
                    Expect.isTrue (provenance.TryGetProperty "branch" |> fst) "the pinned case names a branch"
                Expect.isFalse (provenance.GetProperty("released").GetBoolean()) "not released"
                Expect.equal [ for p in provenance.GetProperty("observedOn").EnumerateArray() -> p.GetString() ] [ "win-arm64" ] "observed on win-arm64"
                let producers = [ for p in provenance.GetProperty("producers").EnumerateArray() -> str p "delivery" ]

                for provider in providers generate do
                    Expect.contains producers (str provider "delivery") "a producer for every covered delivery"
            | other -> failtestf "the generate step has %d sets, not one" other.Length)

        testCase "each step names a delivery once, and its frozen sets name disjoint deliveries" (fun () ->
            use document = case ()

            for step in document.RootElement.GetProperty("steps").EnumerateArray() do
                let deliveries = providers step |> List.map (fun p -> str p "delivery")
                Expect.equal (List.distinct deliveries) deliveries (str step "id" + ": each delivery once")
                let all = sets step
                let covered = System.Collections.Generic.HashSet<string>()

                for set in all do
                    match set.TryGetProperty "appliesTo" with
                    | false, _ -> Expect.equal all.Length 1 "a set for every delivery stands alone"
                    | true, applies ->
                        for delivery in applies.GetProperty("delivery").EnumerateArray() do
                            let delivery = nonNull (delivery.GetString())
                            Expect.contains deliveries delivery $"{delivery} is a covered delivery"
                            Expect.isTrue (covered.Add delivery) $"{delivery} is in one set only")

        testCase "every provider the signed F# recipe can select is covered, and only the managed offerings compile in process" (fun () ->
            let recipe = recipeIn (SignedCaseTests.targetBytes "toolchains/recipes/fsharp/1.0.0.json") "the signed F# recipe"
            coversRecipe recipe

            for host in recipe.Hosts do
                let compiles = host.BuiltIns |> List.exists (fun b -> match b.Operation with Operation.Compile _ -> true | _ -> false)
                Expect.equal compiles (host.Deployment.Text <> "native-aot") $"{host.Deployment.Text}: compiles in process exactly when managed")

        testCase "every provider the synthetic F# recipe can select is covered" (fun () ->
            coversRecipe (recipeIn (Fixtures.bytes "recipe/fsharp-synthetic.json") "recipe/fsharp-synthetic.json"))

        testCase "the qualified receipt names this workflow and the built-in backend it selects" (fun () ->
            match ReceiptDocument.read (Fixtures.bytes "receipt/win-x64-qualified.json") with
            | Ok receipt ->
                use document = case ()
                Expect.equal receipt.Workflow.Workflow.Text (str document.RootElement "workflow") "the workflow id"

                let builtInBackend =
                    receipt.Providers
                    |> List.exists (fun p ->
                        (match p.Identity.Delivery with LockedDelivery.BuiltIn _ -> true | _ -> false) && p.Identity.ExtensionId.Text = "morphir.fsharp")

                Expect.isTrue builtInBackend "the built-in morphir.fsharp backend"
            | Error refusal -> failtestf "the receipt does not read: %A" refusal)
    ]
    |> testLabel "PlatformSpecific"
