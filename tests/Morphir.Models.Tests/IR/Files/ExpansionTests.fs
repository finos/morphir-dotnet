/// Reading compacted Morphir files: the parent's name-context examples (FixtureCatalog.Context, through
/// MorphirFileMother) read, expanded and refused as the parent says, single files and the files of a tree.
module Morphir.IR.Tests.Files.ExpansionTests

open System
open System.Text.Json
open Expecto
open Morphir.IR
open Morphir.IR.Files
open Morphir.IR.Trees
open Morphir.IR.Tests.Trees.StoreTestKit
open Morphir.Models.TestSupport
open Morphir.TestSupport.Fixtures

module Mother = MorphirFileMother
module Driver = DocumentTreeStoreDriver

let private familyOf (example: ContextExample) =
    if example.Ir = 3 then IrFamily.Classic3 else IrFamily.V4

let private fileName (example: ContextExample) = IO.Path.GetFileName example.File.RelativePath

let private read (example: ContextExample) =
    MorphirFile.readEnvelope ContextLimits.draft1 (fileName example) (ReadOnlyMemory(Mother.bytesOf example.Id))

/// The plain document of an example, read and expanded, or a failure.
let private expanded (id: string) =
    let example = Mother.example id

    match read example |> Result.bind (MorphirFile.expand ContextLimits.draft1 (familyOf example) (fileName example)) with
    | Ok document -> document
    | Error errors -> failtestf "%s did not expand: %A" id errors

let private refusedWith (id: string) =
    let example = Mother.example id

    read example
    |> Result.bind (MorphirFile.expand ContextLimits.draft1 (familyOf example) (fileName example))
    |> refusal

let private json (element: JsonElement) = compact (element.GetRawText())

/// The plain twin of a valid example, as the parent writes it.
let private twin (id: string) =
    use document = JsonDocument.Parse(Mother.expandedTextOf id)
    document.RootElement.Clone()

/// The element at a path of member names and array indexes.
let rec private at (path: obj list) (element: JsonElement) =
    match path with
    | [] -> element
    | (:? string as name) :: rest -> at rest (element.GetProperty name)
    | (:? int as index) :: rest -> at rest element[index]
    | other :: _ -> failtestf "%A is not a member name or an index" other

let private readTree ir compacted =
    match Driver.read (Driver.ofTexts (Mother.tree ir compacted)) with
    | Ok result -> result
    | Error errors -> failtestf "The v%d tree did not read: %A" ir errors

/// A tree's content, as the files the store writes for it: a classic tree holds its attributes as JSON elements,
/// which compare by reference, so trees are compared by what they write.
let private contentOf tree =
    match Driver.write tree with
    | Ok files -> writtenAsKitFiles files
    | Error errors -> failtestf "The tree did not write: %A" errors

[<Tests>]
let tests =
    testList "Compacted Morphir files" [
        testList "Each valid example expands to its plain twin" [
            for example in Mother.validExamples ->
                test example.Id {
                    match read example with
                    | Ok file ->
                        Expect.equal file.Schema (Some(Uri MorphirFile.Draft1Schema)) "the file is an envelope of draft.1"
                        Expect.isSome file.Context "the envelope has a context"
                    | Error errors -> failtestf "%A" errors

                    Expect.equal (json (expanded example.Id)) (compact (Mother.expandedTextOf example.Id)) "the plain twin"
                }
        ]

        testList "The plain document of a file's bytes is its expansion in its own family" [
            for example in Mother.validExamples ->
                test example.Id {
                    match MorphirFile.readPlainDocument ContextLimits.draft1 (fileName example) (ReadOnlyMemory(Mother.bytesOf example.Id)) with
                    | Ok document -> Expect.equal (json document) (json (expanded example.Id)) "readEnvelope, then expand in the document's family"
                    | Error errors -> failtestf "%A" errors
                }
        ]

        test "Only a declared schema URL is declared" {
            Expect.isTrue (MorphirFile.isDeclared MorphirFile.Draft1Schema) "the draft.1 envelope"
            Expect.isFalse (MorphirFile.isDeclared (MorphirFile.Draft1Schema.ToUpperInvariant())) "URLs compare ordinally"
            Expect.isFalse (MorphirFile.isDeclared "https://morphir.finos.org/schemas/ir-context/0.2.0/document.schema.json") "another version"
        }

        test "A classic document is told by its format version" {
            Expect.isTrue (MorphirFile.isClassic (twin "v3-manifest")) "a v3 tree file says 3.1.0"
            Expect.isTrue (MorphirFile.isClassic (twin "v3-single-file")) "a v3 single file says 3"
            Expect.isFalse (MorphirFile.isClassic (twin "v4-manifest")) "a v4 manifest"
        }

        test "A plain file is read as plain" {
            let example = Mother.example "v4-type"

            match MorphirFile.readEnvelope ContextLimits.draft1 "v4-type.json" (ReadOnlyMemory(FixtureCatalog.ReadBytes example.Expanded)) with
            | Ok file ->
                Expect.isNone file.Schema "a plain file has no schema"
                Expect.isNone file.Context "a plain file has no context"

                match MorphirFile.expand ContextLimits.draft1 IrFamily.V4 "v4-type.json" file with
                | Ok document -> Expect.equal (json document) (compact (Mother.expandedTextOf "v4-type")) "the document as written"
                | Error errors -> failtestf "%A" errors
            | Error errors -> failtestf "%A" errors
        }

        test "A compacted v3 single file expands to v3" {
            let document = expanded "v3-single-file"

            Expect.equal (document.GetProperty("formatVersion").GetRawText()) "3" "the format version is not relabelled"
            let package = at [ "distribution"; 1 ] document
            Expect.equal package.ValueKind JsonValueKind.Array "the package is classic arrays"
            Expect.equal (json package) (at [ "distribution"; 1 ] (twin "v3-single-file") |> json) "the package of the twin"
            Expect.equal (json document) (compact (Mother.expandedTextOf "v3-single-file")) "every attribute slot is kept"
        }

        test "A compacted tree value file expands in place" {
            let plainTree, plainReport = readTree 4 Mother.NoFile
            let compactedTree, report = readTree 4 Mother.ValueFileOnly

            Expect.equal compactedTree plainTree "the model equals the one read from the plain tree"
            Expect.isEmpty plainReport.CompactedFiles "a plain tree has no compacted file"

            let lineTotal = Own(ValueDef(ModulePath(Path [ Name [ "invoicing" ] ]), Name [ "line"; "total" ]))
            Expect.equal report.CompactedFiles [ lineTotal ] "the value file is the one compacted file"
        }

        testList "Every file of a compacted tree expands" [
            for ir in [ 3; 4 ] ->
                test $"v{ir}" {
                    let plainTree, _ = readTree ir Mother.NoFile
                    let compactedTree, report = readTree ir Mother.EveryFile

                    Expect.equal (contentOf compactedTree) (contentOf plainTree) "the model equals the one read from the plain tree"
                    Expect.hasLength report.CompactedFiles 4 "the manifest, module, type and value files are listed"
                    Expect.equal report.CompactedFiles.Head Manifest "the manifest is first"
                }
        ]

        test "Classic and v4 slots expand differently" {
            let classic = expanded "v3-type"
            let v4 = expanded "v4-type"

            // Both files alias morphir/SDK:basics#int as i, in the type of the field quantity.
            let classicSlot: obj list = [ "def"; "value"; "value"; 2; 2; 0; "tpe"; 2 ]
            let v4Slot: obj list = [ "def"; "TypeAliasDefinition"; "typeExp"; "Record"; "fields"; "quantity" ]

            Expect.equal (at classicSlot classic).ValueKind JsonValueKind.Array "the classic slot is arrays"
            Expect.equal (at classicSlot classic |> json) (at classicSlot (twin "v3-type") |> json) "the classic arrays of the twin"
            Expect.equal (at v4Slot v4).ValueKind JsonValueKind.String "the v4 slot is a string"
            Expect.equal (at v4Slot v4 |> json) (at v4Slot (twin "v4-type") |> json) "the canonical string of the twin"
            Expect.equal (at [ "name" ] classic).ValueKind JsonValueKind.String "a v3 tree file's envelope member is a canonical string"
        }

        testList "Each invalid example is refused with its diagnostic" [
            for example in Mother.invalidExamples ->
                test example.Id {
                    let errors = refusedWith example.Id
                    Expect.contains (errors |> List.map (fun e -> e.Code)) example.Expected "the parent's diagnostic"
                    Expect.all errors (fun e -> e.File = Some(fileName example)) "each diagnostic names the file"
                }
        ]

        test "An unknown envelope is refused" {
            match refusedWith "undeclared-schema" with
            | [ e ] ->
                Expect.equal e.Code "context.undeclared_schema" "the code"
                Expect.stringContains e.Message "https://morphir.finos.org/schemas/ir-context/0.2.0/document.schema.json" "the URL"
            | other -> failtestf "Expected one diagnostic, got %A" other
        }

        test "Unknown alias" {
            match refusedWith "unknown-alias" with
            | [ e ] ->
                Expect.equal e.Code "context.unknown_alias" "the code"
                Expect.stringContains e.Message "'zz'" "the id"
            | other -> failtestf "Expected one diagnostic, got %A" other
        }

        test "Context after the document" {
            match refusedWith "context-after-document" with
            | [ e ] ->
                Expect.equal e.Code "context.after_document" "the code"
                Expect.stringContains e.Message "context must come first" "the rule"
            | other -> failtestf "Expected one diagnostic, got %A" other
        }

        testList "Each compacted policy case expands to its input" [
            for policyCase in Mother.policyCases |> List.filter (fun policyCase -> policyCase.Compacted) ->
                test policyCase.Id {
                    let file = $"{policyCase.Id}/expected.json"
                    let family = if policyCase.Ir = 3 then IrFamily.Classic3 else IrFamily.V4

                    match
                        MorphirFile.readEnvelope ContextLimits.draft1 file (ReadOnlyMemory(Mother.policyExpectedBytes policyCase.Id))
                        |> Result.bind (MorphirFile.expand ContextLimits.draft1 family file)
                    with
                    | Ok document ->
                        // The expected bytes are canonical JSON, with sorted members, so both sides are compared in that form.
                        Expect.sequenceEqual
                            (Morphir.Json.CanonicalJson.write document)
                            (Morphir.Json.CanonicalJson.write (Mother.policyInput policyCase.Id))
                            "the plain input the policy compacted"
                    | Error errors -> failtestf "%A" errors
                }
        ]

        testList "Expansion with no alias gives the document back" [
            let noAlias = NameContext.empty []

            let documents =
                [
                    for entry in FixtureCatalog.Models.All do
                        if entry.RelativePath.EndsWith(".json", StringComparison.Ordinal) then
                            yield entry.RelativePath, FixtureCatalog.ReadText entry
                    for kitCase in FixtureCatalog.Kit.DocumentTree do
                        for KeyValue(path, text) in kitCase.Files do
                            yield $"{kitCase.Id}/{path}", text
                ]

            for name, text in documents ->
                test name {
                    use document = JsonDocument.Parse text
                    let root = document.RootElement

                    match ContextExpansion.expand ContextLimits.draft1 (MorphirFile.familyOf root) name noAlias root with
                    | Ok copy -> Expect.equal (json copy) (compact text) "every member and item is copied as written"
                    | Error errors -> failtestf "%A" errors
                }
        ]

        test "A refused file in a tree refuses the tree" {
            let files =
                Mother.tree 4 Mother.NoFile
                |> List.map (fun (location, text) ->
                    if location.EndsWith(".type.json", StringComparison.Ordinal) then
                        location, FixtureCatalog.ReadText (Mother.example "unknown-alias").File
                    else
                        location, text
                )

            let errors = Driver.read (Driver.ofTexts files) |> refusal
            Expect.equal (codesAndFiles errors) [ "context.unknown_alias", Some "pkg/my-org/billing/invoicing/invoice-line.type.json" ] "the file and the rule"
        }
    ]
