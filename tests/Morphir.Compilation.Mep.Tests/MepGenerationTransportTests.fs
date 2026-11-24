// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The host side of morphir.backend.generateModel model transport: the tuples a host offers and which one a model
/// needs. The decision is in F#, with an exhaustive match on the model family, so C# callers do not test cases.
module Morphir.Compilation.Mep.Tests.MepGenerationTransportTests

open System.Reflection
open Expecto
open Morphir.Compilation.Mep
open Morphir.IR.Model
open Morphir.Mep

let private tupleOf (family: ModelFamily) (version: string) =
    match MepGenerationTransport.TryTupleFor(family, version) with
    | true, tuple -> Some tuple
    | false, _ -> None

[<Tests>]
let tests =
    testList "MEP generation transport" [
        test "The host offers the JSON generateModel tuples for IR 4.0.0 and 3.0.0, 4.0.0 first" {
            let offered =
                MepGenerationTransport.Tuples
                |> List.ofSeq

            Expect.equal
                [
                    for tuple in offered ->
                        tuple.Operation,
                        tuple.Profile,
                        tuple.Codec,
                        tuple.EnvelopeVersion.Value,
                        tuple.IrVersion.Value
                ]
                [
                    ModelOperation.GenerateModel,
                    "morphir-ir",
                    ModelCodec.MorphirJson,
                    "0.1.0-draft.1",
                    "4.0.0"
                    ModelOperation.GenerateModel,
                    "morphir-ir",
                    ModelCodec.MorphirJson,
                    "0.1.0-draft.1",
                    "3.0.0"
                ]
                "the offer"

            Expect.equal
                offered
                [ MepGenerationTransport.V4Tuple; MepGenerationTransport.ClassicTuple ]
                "the named tuples"
        }

        test "A model needs the tuple of its family and IR version; others have none" {
            for family, version, expected in
                [
                    ModelFamily.V4, "4", Some MepGenerationTransport.V4Tuple
                    ModelFamily.V4, "4.0.0", Some MepGenerationTransport.V4Tuple
                    ModelFamily.Classic, "3", Some MepGenerationTransport.ClassicTuple
                    ModelFamily.Classic, "3.0.0", Some MepGenerationTransport.ClassicTuple
                    ModelFamily.Classic, "3.1.0", None
                    ModelFamily.Classic, "2", None
                    ModelFamily.Classic, "1", None
                    ModelFamily.Classic, "4", None
                    ModelFamily.V4, "3", None
                    ModelFamily.V4, "4.1.0", None
                ] do
                Expect.equal (tupleOf family version) expected $"{family} {version}"
        }

        // A class `static let` value is a static field behind an F# init check. When two threads start the type
        // initialisers of two types that need each other, the CLR breaks the deadlock by letting one thread see a type
        // whose initialiser has not finished, and the init check then throws (FailStaticInit). Offers made by functions
        // leave the types without a static field, so concurrent first use has no initialiser to meet.
        test "The offer types have no static field for concurrent first use to meet" {
            for offerType in [ typeof<MepGenerationTransport>; typeof<MepFrontend> ] do
                let fields =
                    offerType.GetFields(
                        BindingFlags.Static
                        ||| BindingFlags.Public
                        ||| BindingFlags.NonPublic
                        ||| BindingFlags.DeclaredOnly
                    )

                Expect.isEmpty
                    [ for field in fields -> field.Name ]
                    $"the static fields of {offerType.Name}"
        }

        // The same rule for every source file of the assembly: no file initialiser starts the initialiser of a type
        // of its file while the file has a second route in (TypeInitialisation of Morphir.TestSupport).
        test
            "No source file of the assembly has a static initialiser that two threads can deadlock on" {
            let contended =
                Morphir.TestSupport.TypeInitialisation.ContendedFiles typeof<MepFrontend>.Assembly
                |> List.ofSeq

            Expect.equal contended [] "the contended files"
        }

        // The guard must still find a contended file: PlantedContention.fs of this test assembly is one on purpose.
        // If code generation changes so that the guard no longer sees it, this test fails instead of every guard
        // passing while it checks nothing.
        test "The guard finds the planted contended file of the test assembly" {
            let contended =
                Morphir.TestSupport.TypeInitialisation.ContendedFiles
                    typeof<PlantedContention.PlantedHolder>.Assembly
                |> List.ofSeq

            Expect.equal
                contended
                [
                    "PlantedContention: routes [Morphir.Compilation.Mep.Tests.PlantedContention, "
                    + "Morphir.Compilation.Mep.Tests.PlantedContention+PlantedHolder] start "
                    + "[Morphir.Compilation.Mep.Tests.PlantedContention+PlantedHolder]"
                ]
                "only the planted file, with both routes and the class it starts"
        }
    ]

let private requireOk =
    function
    | Ok result -> result
    | Result.Error errors -> failtestf "Expected success, got %A" errors

let private admittedOrders () =
    let prefix =
        Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Directory
        + "v4/plain-json/"

    let files =
        Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Files
        |> Seq.filter (fun entry ->
            entry.RelativePath.StartsWith(prefix, System.StringComparison.Ordinal)
        )
        |> Seq.map (fun entry ->
            let location = entry.RelativePath.Substring(prefix.Length)
            let text = Morphir.TestSupport.Fixtures.FixtureCatalog.ReadText entry
            location, Morphir.Files.VFile.ofText location text
        )
        |> Map.ofSeq
        |> Morphir.Files.VFileTree.fromFileMap
        |> Morphir.Files.VFileProviders.asFileProvider

    let startMessage =
        Morphir.TestSupport.Fixtures.FixtureCatalog.ReadText
            Morphir.TestSupport.Fixtures.FixtureCatalog.Mep.BoundedGenerationSession
        |> fun text -> text.Split('\n')
        |> Array.find (fun line ->
            line.Contains("\"method\":\"morphir.backend.generateModel.start\"")
        )

    use document = System.Text.Json.JsonDocument.Parse startMessage

    let start =
        System.Text.Json.JsonSerializer.Deserialize(
            document.RootElement.GetProperty("params"),
            MepJson.Default.ModelAccessStart
        )
        |> nonNull

    let snapshot =
        Morphir.IR.Trees.DocumentTreeStore.openSnapshot
            Morphir.IR.Trees.ReadOptions.defaults
            (fun () -> "orders-r1")
            files
        |> requireOk

    snapshot, start

[<Tests>]
let snapshotTests =
    testList "MEP scoped snapshot delivery" [
        test "Maps inventory and original document requests in order within one round" {
            let snapshot, start = admittedOrders ()

            let view =
                MepGenerationSnapshot.Bind(snapshot, start)
                |> requireOk

            let document = "pkg/acme/orders/domain/total-in-_usd.value"
            let mutable decodedBytes = -1L

            let advance =
                view.PrepareHostAdvance(
                    4,
                    [|
                        ModelAccessRequest.Inventory(
                            "inventory",
                            "pkg/acme/orders/domain/module",
                            2,
                            None
                        )
                        ModelAccessRequest.Document("final-value", document)
                    |],
                    &decodedBytes
                )
                |> requireOk

            Expect.equal decodedBytes 257L "Only the original document bytes count, excluding inventory"

            Expect.equal
                (advance.SessionId, advance.Round)
                ("orders-generation", 4)
                "Retain the announced session and round"

            match
                advance.Deliveries
                |> List.ofSeq
            with
            | [ ModelAccessDelivery.Inventory("inventory", "orders-snapshot", entries, Some _)
                ModelAccessDelivery.Document("final-value",
                                             "orders-snapshot",
                                             actual,
                                             length,
                                             digest,
                                             content) ] ->
                match
                    entries
                    |> List.ofSeq
                with
                | [ ModelInventoryEntry.Type typeEntry; ModelInventoryEntry.Value valueEntry ] ->
                    Expect.equal
                        (typeEntry.Name, valueEntry.Name)
                        ("order", "order")
                        "Map the single-key inventory variants"

                    Expect.equal valueEntry.Role ModelInventoryRole.Definition "Preserve the role"
                | other -> failtestf "Wrong member inventory: %A" other

                Expect.equal actual document "Delivery identifies the selected document"
                Expect.equal length 257 "The declared length comes from the original bytes"

                Expect.equal
                    digest.Value
                    "f4e5dcae87eb9b022bf446711b4173cd04839da7cf4e2f122ca1600143acf9d4"
                    "The transcript's fixed digest"

                Expect.equal
                    (System.Convert.FromBase64String(content).Length)
                    length
                    "The base64 wraps the original bytes"
            | other -> failtestf "Wrong deliveries: %A" other
        }

        test "Serves a bounded page for a larger valid wire request" {
            let snapshot, start = admittedOrders ()

            let view =
                MepGenerationSnapshot.Bind(snapshot, start)
                |> requireOk

            let mutable decodedBytes = -1L

            let advance =
                view.PrepareHostAdvance(
                    1,
                    [| ModelAccessRequest.Inventory("inventory", "root", 257, None) |],
                    &decodedBytes
                )
                |> requireOk

            match
                advance.Deliveries
                |> List.ofSeq
            with
            | [ ModelAccessDelivery.Inventory("inventory", "orders-snapshot", entries, None) ] ->
                Expect.equal entries.Count 3 "Return both own modules and the dependency module"
                Expect.equal decodedBytes 0L "Inventory contains no decoded document bytes"
            | other -> failtestf "Wrong deliveries: %A" other
        }

        test "Refuses an unmatched reader tuple or revision" {
            let snapshot, start = admittedOrders ()

            let wrongCodec =
                { start with
                    Input =
                        { start.Input with
                            Codec = ModelAccessCodec.MorphirYaml
                        }
                }

            let foreignRevision =
                { start with
                    Snapshot =
                        { start.Snapshot with
                            Revision = "other"
                        }
                }

            Expect.isError
                (MepGenerationSnapshot.Bind(snapshot, wrongCodec))
                "The selected codec must match"

            Expect.isError
                (MepGenerationSnapshot.Bind(snapshot, foreignRevision))
                "The revision is fixed at admission"
        }

        test "Refuses a batch that exceeds the encoded budget cumulatively" {
            let snapshot, start = admittedOrders ()

            let limits =
                { start.Limits with
                    MaxEncodedBytesPerExchange = 671
                }

            let view =
                MepGenerationSnapshot.Bind(snapshot, { start with Limits = limits })
                |> requireOk

            let requests = [|
                ModelAccessRequest.Document("final", "pkg/acme/orders/domain/total-in-_usd.value")
                ModelAccessRequest.Document("order", "pkg/acme/orders/domain/order.value")
            |]

            let mutable decodedBytes = -1L
            Expect.isError (view.PrepareHostAdvance(4, requests, &decodedBytes)) "The batch requires 672 encoded bytes"
            Expect.equal decodedBytes 0L "A refused batch exposes no partial document count"
        }

        test "TupleFor maps every version, syntax and contextual encoding combination and Bind accepts contextual trees" {
            for version, expectedIr in [ "v3.1", "3.1.0"; "v4", "4.0.0" ] do
                for profile, expectedCodec, expectedEncoding in
                    [
                        "plain-json", ModelAccessCodec.MorphirJson, ModelAccessEncoding.Plain
                        "plain-yaml", ModelAccessCodec.MorphirYaml, ModelAccessEncoding.Plain
                        "contextual-json", ModelAccessCodec.MorphirJson, ModelAccessEncoding.ContextualDraft1
                        "contextual-yaml", ModelAccessCodec.MorphirYaml, ModelAccessEncoding.ContextualDraft1
                    ] do
                    let prefix =
                        Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Directory
                        + version + "/" + profile + "/"
                    let files =
                        Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.Files
                        |> Seq.filter (fun entry -> entry.RelativePath.StartsWith(prefix, System.StringComparison.Ordinal))
                        |> Seq.map (fun entry ->
                            let location = entry.RelativePath.Substring(prefix.Length)
                            let text = Morphir.TestSupport.Fixtures.FixtureCatalog.ReadText entry
                            location, Morphir.Files.VFile.ofText location text)
                        |> Map.ofSeq
                        |> Morphir.Files.VFileTree.fromFileMap
                        |> Morphir.Files.VFileProviders.asFileProvider
                    let snapshot =
                        Morphir.IR.Trees.DocumentTreeStore.openDetectedSnapshot(
                            Morphir.IR.Trees.ReadOptions.defaults, System.Func<string>(fun () -> "orders-r1"), files)
                        |> requireOk
                    let tuple = MepGenerationSnapshot.TupleFor snapshot
                    Expect.equal tuple.ProtocolVersion ModelAccess.protocolVersion $"{version}/{profile} protocol"
                    Expect.equal tuple.AccessVersion ModelAccess.accessVersion $"{version}/{profile} access"
                    Expect.equal tuple.Operation ModelOperation.GenerateModel $"{version}/{profile} op"
                    Expect.equal tuple.IrVersion.Value expectedIr $"{version}/{profile} ir"
                    Expect.equal tuple.Codec expectedCodec $"{version}/{profile} codec"
                    Expect.equal tuple.ContextualEncoding expectedEncoding $"{version}/{profile} encoding"
                    Expect.equal tuple.Layout "document-tree" $"{version}/{profile} layout"

                    let _, baseStart = admittedOrders ()
                    let start = { baseStart with Input = tuple }
                    let view = MepGenerationSnapshot.Bind(snapshot, start) |> requireOk
                    let advance =
                        view.PrepareHostAdvance(
                            4,
                            [| ModelAccessRequest.Document("final", "pkg/acme/orders/domain/total-in-_usd.value") |])
                        |> requireOk
                    Expect.equal advance.Deliveries.Count 1 $"{version}/{profile} delivered"
        }

        test "TryWriteSingleDocumentJson serializes V4 and Classic models losslessly and keeps draft.1 single-document layout" {
            let v4Bytes = Morphir.TestSupport.Fixtures.FixtureCatalog.ReadBytes Morphir.TestSupport.Fixtures.FixtureCatalog.BoundedOrders.ExpandedV4
            use v4Doc = System.Text.Json.JsonDocument.Parse v4Bytes
            let v4Dist =
                match Morphir.Json.V4.V4Json.readDistribution v4Doc.RootElement with
                | Ok doc -> doc.Distribution
                | Result.Error err -> failtest $"v4 fixture failed: {err}"
            let v4Model: VersionedModel =
                { SourceVersion = SourceFormatVersion.Create "4.0.0"
                  Distribution = DistributionModel.V4 v4Dist }
            let mutable bytes = Unchecked.defaultof<byte array>
            let mutable error = Unchecked.defaultof<string>
            Expect.isTrue (MepGenerationTransport.TryWriteSingleDocumentJson(v4Model, &bytes, &error)) "v4 write succeeds"
            let revision = "sha256:" + Sha256Digest.Compute(bytes).Value
            match ModelTransport.createDerivedEnvelope MepGenerationTransport.V4Tuple revision (System.ReadOnlyMemory bytes) with
            | Ok envelope ->
                Expect.equal envelope.Payload.Layout ModelLayout.SingleDocument "draft.1 wire layout stays single-document"
                Expect.equal envelope.Payload.Layout.WireValue "single-document" "wire spelling stays single-document"
                Expect.equal envelope.Revision revision "deterministic revision from converted bytes"
            | Result.Error failure -> failtest $"Envelope failed: {failure}"

            let classicBytes = Morphir.TestSupport.Fixtures.FixtureCatalog.ReadBytes Morphir.TestSupport.Fixtures.FixtureCatalog.Models.V3Library
            use classicDoc = System.Text.Json.JsonDocument.Parse classicBytes
            let classicDist =
                match Morphir.Json.Classic.ClassicJson.readDistribution classicDoc.RootElement with
                | Ok doc -> doc.Distribution
                | Result.Error err -> failtest $"classic fixture failed: {err}"
            let classicModel: VersionedModel =
                { SourceVersion = SourceFormatVersion.Create "3.0.0"
                  Distribution = DistributionModel.Classic classicDist }
            Expect.isTrue (MepGenerationTransport.TryWriteSingleDocumentJson(classicModel, &bytes, &error)) "classic write succeeds"
            let classicRevision = "sha256:" + Sha256Digest.Compute(bytes).Value
            match ModelTransport.createDerivedEnvelope MepGenerationTransport.ClassicTuple classicRevision (System.ReadOnlyMemory bytes) with
            | Ok envelope ->
                Expect.equal envelope.Payload.Layout ModelLayout.SingleDocument "draft.1 classic wire layout stays single-document"
                Expect.equal envelope.Payload.Layout.WireValue "single-document" "wire spelling stays single-document"
                Expect.equal envelope.Revision classicRevision "deterministic classic revision"
            | Result.Error failure -> failtest $"Classic envelope failed: {failure}"
        }
    ]
