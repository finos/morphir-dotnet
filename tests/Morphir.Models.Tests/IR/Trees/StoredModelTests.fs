/// The C# surface of a conversion, StoredModel (add-model-convert 6.1): a single file is read with the codecs of its
/// IR version, split into a tree, joined back and written, and the IR version never changes. The command
/// (Morphir.Tooling, ModelConvert.feature) adds the disk and the report; these tests check the model side alone.
module Morphir.IR.Tests.Trees.StoredModelTests

open System
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.Trees
open Morphir.Json.Classic
open Morphir.Json.V4
open Morphir.Models.TestSupport
open Morphir.TestSupport.Fixtures

/// Reads single-file bytes as the command does.
let private read (bytes: byte array) =
    use document = JsonDocument.Parse(ReadOnlyMemory bytes)
    let mutable model = Unchecked.defaultof<StoredModel>
    let mutable problems = Unchecked.defaultof<_>

    if StoredModel.TryReadSingleFile(document.RootElement, &model, &problems) then
        Ok model
    else
        Error [ for problem in problems -> problem.Code ]

let private readOk bytes =
    match read bytes with
    | Ok model -> model
    | Error codes -> failtestf "Expected the single file to read, got %A" codes

let private convert layout (model: StoredModel) =
    let mutable converted = Unchecked.defaultof<StoredModel>
    let mutable problems = Unchecked.defaultof<_>

    if model.TryConvert(layout, &converted, &problems) then
        converted
    else
        failtestf "Expected the conversion, got %A" [ for problem in problems -> problem.ToString() ]

let private write compact (model: StoredModel) =
    let mutable bytes = Unchecked.defaultof<byte array>
    let mutable problems = Unchecked.defaultof<_>

    if model.TryWriteSingleFile(compact, &bytes, &problems) then
        Ok bytes
    else
        Error [ for problem in problems -> problem.Code ]

let private v4Bytes distribution =
    match V4JsonWriter.writeDistribution true distribution with
    | Ok bytes -> bytes
    | Error error -> failtestf "Expected a v4 write, got %s" error.Message

let private classicBytes profile distribution =
    match ClassicJsonWriter.writeTyped profile true distribution with
    | Ok bytes -> bytes
    | Error error -> failtestf "Expected a classic write, got %s" error.Message

let private formatVersion (bytes: byte array) =
    use document = JsonDocument.Parse(ReadOnlyMemory bytes)
    document.RootElement.GetProperty("formatVersion").GetRawText()

[<Tests>]
let tests =
    testList "StoredModel" [
        test "A v4 single file splits into a tree and joins back to the same bytes" {
            let original = v4Bytes DistributionMother.v4LibraryWithDependency
            let model = readOk original
            Expect.equal model.Layout ModelLayout.SingleFile "a file is a single-file model"
            Expect.equal model.IrVersion "4" "the IR version of a v4 file"

            let tree = convert ModelLayout.DocumentTree model
            Expect.equal tree.Layout ModelLayout.DocumentTree "split gives a tree"
            Expect.equal tree.IrVersion "4" "the split keeps the IR version"

            let joined = convert ModelLayout.SingleFile tree
            Expect.equal (write false joined) (Ok original) "the joined model is written as the original"
        }

        test "A classic Library keeps its integer label and a classic Specs keeps 3.1.0" {
            for profile, distribution, label in
                [ ClassicWireProfile.v3, DistributionMother.v3Library, "3"
                  ClassicWireProfile.v3_1_0, DistributionMother.v3Specs, "\"3.1.0\"" ] do
                let original = classicBytes profile distribution
                let model = readOk original
                Expect.equal model.IrVersion "3" "a classic v3 model is IR 3"

                let back = model |> convert ModelLayout.DocumentTree |> convert ModelLayout.SingleFile

                match write false back with
                | Ok bytes ->
                    Expect.equal (formatVersion bytes) label "the single-file label of the kind"
                    Expect.equal bytes original "the round trip gives the original bytes"
                | Error codes -> failtestf "Expected a write, got %A" codes
        }

        test "A form already in the layout is returned as it is" {
            let model = readOk (v4Bytes DistributionMother.v4LibraryWithOneModule)
            Expect.isTrue (obj.ReferenceEquals(convert ModelLayout.SingleFile model, model)) "no conversion"
        }

        test "A classic v1 or v2 single file is refused" {
            for entry in [ FixtureCatalog.Models.V1EmptyLibrary; FixtureCatalog.Models.V2EmptyLibrary ] do
                Expect.equal
                    (read (FixtureCatalog.ReadBytes entry) |> Result.map ignore)
                    (Error [ "model.unsupported_version" ])
                    entry.Intent
        }

        test "A classic single file with a member that the classic model has no place for is refused, not dropped" {
            use document = JsonDocument.Parse(ReadOnlyMemory DistributionMother.SingleFiles.v3LibraryWithANestedUnknownMember)
            let mutable model = Unchecked.defaultof<StoredModel>
            let mutable problems = Unchecked.defaultof<_>

            Expect.isFalse (StoredModel.TryReadSingleFile(document.RootElement, &model, &problems)) "the file is refused"
            let problem = Expect.wantSome (Seq.tryExactlyOne problems) "one problem"
            Expect.equal problem.Code "unknown_member" "the code"

            Expect.stringStarts
                problem.Message
                (DistributionMother.SingleFiles.v3NestedUnknownMember + ":")
                "the message starts with the member's JSON location"
        }

        test "The C# checks of a single file give the codec's error, or null for a file that reads" {
            let v4Error (bytes: byte array) =
                use document = JsonDocument.Parse(ReadOnlyMemory bytes)
                V4Json.readDistributionError document.RootElement, V4Json.readDistribution document.RootElement

            let classicError (bytes: byte array) =
                use document = JsonDocument.Parse(ReadOnlyMemory bytes)
                ClassicJson.readDistributionStrictError document.RootElement

            let error, read = v4Error (v4Bytes DistributionMother.v4LibraryWithOneModule)
            Expect.isNull error "a v4 file that reads"
            Expect.isOk read "readDistribution reads it"

            let error, read = v4Error (FixtureCatalog.ReadBytes FixtureCatalog.Models.NoFormatVersion)
            Expect.equal (Error(nonNull error)) (read |> Result.map ignore) "the error of readDistribution"

            Expect.isNull (classicError (classicBytes ClassicWireProfile.v3 DistributionMother.v3Library)) "a v3 file that reads"

            let error = nonNull (classicError DistributionMother.SingleFiles.v3LibraryWithANestedUnknownMember)
            Expect.equal error.Rule "unknown_member" "the error of readDistributionStrict"
            Expect.equal error.Location DistributionMother.SingleFiles.v3NestedUnknownMember "at the member's JSON location"
        }

        test "A codec error keeps the codec's rule" {
            match read (FixtureCatalog.ReadBytes FixtureCatalog.Models.NoFormatVersion) with
            | Ok _ -> failtest "Expected a model with no format version to be refused"
            | Error codes ->
                Expect.isNonEmpty codes "a refusal has a code"
                Expect.isFalse (codes |> List.exists (fun code -> code.StartsWith("model.", StringComparison.Ordinal))) "the codec's rule"
        }

        test "A compacted single file stays one file under the policy" {
            let model = readOk (v4Bytes DistributionMother.nameUsedOften)
            let mutable bytes = Unchecked.defaultof<byte array>
            let mutable compacted = false
            let mutable problems = Unchecked.defaultof<_>
            Expect.isTrue (model.TryWriteSingleFile(true, &bytes, &compacted, &problems)) "the file is written"
            Expect.isTrue compacted "an alias is worth its cost"

            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            Expect.equal (document.RootElement.GetProperty("$schema").GetString()) Morphir.IR.Files.MorphirFile.Draft1Schema "one envelope"

            match Morphir.IR.Files.MorphirFile.readPlainDocument Morphir.IR.Files.ContextLimits.draft1 "model.json" (ReadOnlyMemory bytes) with
            | Ok plain ->
                // Canonical JSON sorts the keys of v4 maps, whose order is not part of the model, so the plain files
                // are compared in that form.
                let canonical (bytes: byte array) =
                    use document = JsonDocument.Parse(ReadOnlyMemory bytes)
                    Morphir.Json.CanonicalJson.write document.RootElement

                let written result =
                    match result with
                    | Ok bytes -> canonical bytes
                    | Error codes -> failtestf "Expected a write, got %A" codes

                Expect.sequenceEqual
                    (written (write false (readOk (Text.Encoding.UTF8.GetBytes(plain.GetRawText())))))
                    (written (write false model))
                    "the same model"
            | Error errors -> failtestf "%A" errors
        }

        test "A single file that no alias pays for is written plain in canonical JSON" {
            let model = readOk (v4Bytes DistributionMother.nameUsedOnce)
            let mutable bytes = Unchecked.defaultof<byte array>
            let mutable compacted = true
            let mutable problems = Unchecked.defaultof<_>
            Expect.isTrue (model.TryWriteSingleFile(true, &bytes, &compacted, &problems)) "the file is written"
            Expect.isFalse compacted "no alias is worth its cost"
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            Expect.sequenceEqual bytes (Morphir.Json.CanonicalJson.write document.RootElement) "canonical JSON"
        }

        test "The tree of a single file and the single file of a tree are refused" {
            let model = readOk (v4Bytes DistributionMother.v4LibraryWithOneModule)
            Expect.throwsT<InvalidOperationException> (fun () -> model.Tree |> ignore) "a single file has no tree"

            let tree = convert ModelLayout.DocumentTree model
            Expect.throwsT<InvalidOperationException> (fun () -> write false tree |> ignore) "a tree is written by the store"
        }

        test "A store write gives its files or its errors as problems" {
            let tree = (readOk (v4Bytes DistributionMother.v4LibraryWithOneModule) |> convert ModelLayout.DocumentTree).Tree
            let mutable files = Unchecked.defaultof<Morphir.Files.VFileTree>
            let mutable problems = Unchecked.defaultof<_>

            Expect.isTrue
                (StoredModel.TryGetWritten(DocumentTreeStore.write (StoredModel.WriteOptions false) tree, &files, &problems))
                "a plain tree is written"

            Expect.isTrue
                (StoredModel.TryGetWritten(DocumentTreeStore.write (StoredModel.WriteOptions true) tree, &files, &problems))
                "a compacted tree is written"

            Expect.isSome (Morphir.Files.VFileTree.tryGet DocumentTreeStore.plainFiles files) "under the policy"
        }

        testList "DocumentTreeStore.writeTree gives the files and the version they declare" [
            let writeTree (model: StoredModel) =
                let mutable tree = Unchecked.defaultof<DocumentTreeStore.WrittenTree>
                let mutable problems = Unchecked.defaultof<_>

                if
                    StoredModel.TryGetWritten(
                        DocumentTreeStore.writeTree (StoredModel.WriteOptions false) model.Tree,
                        &tree,
                        &problems
                    )
                then
                    tree
                else
                    failtestf "Expected the tree, got %A" [ for problem in problems -> problem.ToString() ]

            let manifestVersion (tree: DocumentTreeStore.WrittenTree) =
                match Morphir.Files.VFileTree.findFile "manifest.json" tree.Files |> Option.map Morphir.Files.VFile.readText with
                | Some(Ok text) ->
                    use document = JsonDocument.Parse(text)
                    document.RootElement.GetProperty("formatVersion").GetRawText()
                | _ -> failtest "Expected a manifest file"

            test "A v4 tree declares IR 4" {
                let tree = readOk (v4Bytes DistributionMother.v4LibraryWithOneModule) |> convert ModelLayout.DocumentTree |> writeTree
                Expect.equal (Versioning.version tree.FormatVersion) "4" "the version of the v4 files"
                Expect.equal (manifestVersion tree) "4" "the manifest says 4"
            }

            test "A classic v3 tree declares 3.1.0" {
                let tree =
                    readOk (classicBytes ClassicWireProfile.v3 DistributionMother.v3Library)
                    |> convert ModelLayout.DocumentTree
                    |> writeTree

                Expect.equal (Versioning.version tree.FormatVersion) "3.1.0" "every classic tree file says 3.1.0"
                Expect.equal (manifestVersion tree) "\"3.1.0\"" "the manifest says 3.1.0"
            }
        ]
    ]
