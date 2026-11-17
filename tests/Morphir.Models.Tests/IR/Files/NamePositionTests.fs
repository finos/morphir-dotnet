/// The typed name positions of expansion: a reference at each position of the v4 and the classic JSON resolves through
/// its own table, in the spelling of its position, and a reference where the JSON holds only text stays as written.
/// Each compacted document is derived from a plain document of NamePositionMother by replacing one spelling with the
/// reference {"$name": "a"}.
module Morphir.IR.Tests.Files.NamePositionTests

open System
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.IR
open Morphir.IR.Files
open Morphir.IR.Trees
open Morphir.IR.Tests.Trees.StoreTestKit
open Morphir.Json.V4
open Morphir.Models.TestSupport
open Morphir.Models.TestSupport.NamePositionMother

module Mother = NamePositionMother
module Driver = DocumentTreeStoreDriver

/// <summary>
/// A context that defines the id of the reference in every table: the entry in the position's own table, and in each
/// other table a decoy that no document spells, so a reference resolved through the wrong table gives other text.
/// </summary>
let private contextFor (entry: ContextEntry) : FileContext =
    // The mother's names are lowercase words, so the printed spelling is the one the table would hold.
    let one print value =
        Map.ofList [ Mother.referenceId, { Spelling = print value; Value = value } ]

    let name = one CanonicalNames.nameToString
    let path = one CanonicalNames.pathToString
    let fqName = one CanonicalNames.fqNameToString
    let decoyPath = DistributionBuilder.path "decoy/path"

    let everyTable = {
        NameContext.empty [] with
            Names = name (DistributionBuilder.name "decoy")
            PackagePaths = path decoyPath
            ModulePaths = path decoyPath
            FqNames = fqName (DistributionBuilder.fqName "decoy/package" "decoy" "decoy")
    }

    match entry with
    | NameEntry n -> { everyTable with Names = name n }
    | PackagePathEntry p -> { everyTable with PackagePaths = path p }
    | ModulePathEntry p -> { everyTable with ModulePaths = path p }
    | FqNameEntry f -> { everyTable with FqNames = fqName f }

/// A context that defines the id in every table, with decoys only.
let private decoys = contextFor (NameEntry(DistributionBuilder.name "decoy"))

/// The text of a JSON document in one form: compact, members in their order.
let private normal (text: string) = Mother.textOf (Mother.parse text)

let private expandText family (documentId: string) context (text: string) =
    use document = JsonDocument.Parse(text, JsonDocumentOptions(MaxDepth = V4JsonDecoding.MaxDepth))

    ContextExpansion.expand ContextLimits.draft1 family documentId context document.RootElement
    |> Result.map (fun expanded -> normal (expanded.GetRawText()))

let private documentsHolding (model: PositionModel) (spelling: JsonNode) =
    [
        for document in model.Documents do
            let compacted, count = Mother.withReference spelling document.Text

            if count > 0 then
                yield document, compacted
    ]

let private positionTests (model: PositionModel) =
    testList model.Id [
        for position in model.Positions ->
            let held = documentsHolding model (Mother.spellingOf position)

            testList position.Intent [
                yield
                    test "is held by a document of the model" {
                        Expect.isNonEmpty held "the mother spells the position in at least one document"
                    }

                for document, compacted in held ->
                    test document.Id {
                        match expandText model.Family document.Id (contextFor position.Entry) compacted with
                        | Ok expanded -> Expect.equal expanded (normal document.Text) "the reference expands to the spelling of the position"
                        | Error errors -> failtestf "The reference did not resolve: %A" errors
                    }
            ]
    ]

let private literalTests (model: PositionModel) =
    testList model.Id [
        for literal in model.Literals ->
            let held = documentsHolding model literal.Value

            testList literal.Intent [
                yield
                    test "is held by a document of the model" {
                        Expect.isNonEmpty held "the mother spells the literal in at least one document"
                    }

                for document, compacted in held ->
                    test document.Id {
                        match expandText model.Family document.Id decoys compacted with
                        | Ok expanded -> Expect.equal expanded (normal compacted) "the reference stays exactly as written"
                        | Error errors -> failtestf "%A" errors
                    }
            ]
    ]

let private distributionOf (text: string) =
    use document = JsonDocument.Parse text

    match V4Json.readDistribution document.RootElement with
    | Ok read -> read.Distribution
    | Error error -> failtestf "The document did not read: %A" error

/// The documents of a model whose ids start with the prefix, as the files of a tree.
let private filesUnder (prefix: string) (model: PositionModel) =
    model.Documents
    |> List.filter (fun document -> document.Id.StartsWith(prefix, StringComparison.Ordinal))
    |> List.map (fun document -> document.Id.Substring prefix.Length, document.Text)

/// The limits with the depth of a plain single file of model convert.
let private deepLimits = ContextLimits.draft1.WithMaxDepth 4096

/// The nesting of JSON bytes, read to any depth: the levels of the deepest container.
let private nesting (bytes: byte array) =
    let mutable reader = Utf8JsonReader(ReadOnlySpan bytes, JsonReaderOptions(MaxDepth = Int32.MaxValue))
    let mutable deepest = 0

    while reader.Read() do
        deepest <- max deepest reader.CurrentDepth

    deepest + 1

/// The result of <paramref name="work"/> on a thread of its own with a stack of <paramref name="size"/> bytes.
let private onStack (size: int) (work: unit -> 'a) : 'a =
    let mutable result = None
    let mutable failure = None

    let thread =
        Threading.Thread(
            (fun () ->
                try
                    result <- Some(work ())
                with error ->
                    failure <- Some error),
            size
        )

    thread.Start()
    thread.Join()

    match result, failure with
    | Some value, _ -> value
    | _, Some error -> raise (Exception("The work failed on its thread.", error))
    | None, None -> failwith "The work gave no result."

/// The containers around the most deeply nested value: what JsonDocumentOptions.MaxDepth limits.
let private depthOf (text: string) =
    let mutable reader = Utf8JsonReader(Text.Encoding.UTF8.GetBytes text, JsonReaderOptions(MaxDepth = 4096))
    let mutable deepest = 0

    while reader.Read() do
        deepest <- max deepest reader.CurrentDepth

    deepest

[<Tests>]
let tests =
    testList "Name positions" [
        testList "A reference at each typed name position resolves" [
            for model in Mother.v4Models @ Mother.classicModels -> positionTests model
        ]

        testList "A literal that looks like an alias stays a literal" [
            for model in Mother.v4Models @ Mother.classicModels do
                if not model.Literals.IsEmpty then
                    yield literalTests model
        ]

        testList "The derived forms are forms the decoders read" [
            for model in Mother.v4Models do
                yield
                    test $"{model.Id}: the legacy tagged arrays read as the single file's model" {
                        let single = model.Documents |> List.find (fun document -> document.Id = "single file")
                        let legacy = model.Documents |> List.find (fun document -> document.Id = "single file, legacy form")

                        Expect.notEqual (normal legacy.Text) (normal single.Text) "the legacy form is another spelling"
                        Expect.equal (distributionOf legacy.Text) (distributionOf single.Text) "the same model"
                    }

                yield
                    test $"{model.Id}: the inline module entries read as the tree's model" {
                        let read files =
                            match Driver.read (Driver.ofTexts files) with
                            | Ok(tree, _) -> tree
                            | Error errors -> failtestf "The tree did not read: %A" errors

                        let inlineFiles = filesUnder "inline/" model
                        Expect.isLessThan inlineFiles.Length (filesUnder "tree/" model).Length "the type and value files moved inline"
                        Expect.equal (read inlineFiles) (read (filesUnder "tree/" model)) "the same model"
                    }
        ]

        test "A reference that expands past the depth limit is refused" {
            let compactedWith extraLevels =
                let plain, position = Mother.deeplyNested extraLevels
                position, Mother.withReference (Mother.spellingOf position) plain

            // The deepest document that fits in an envelope: one level below the limit of the readers.
            let _, (shallow, _) = compactedWith 0
            let position, (compacted, count) = compactedWith (V4JsonDecoding.MaxDepth - 1 - depthOf shallow)

            Expect.equal count 1 "one reference"
            Expect.equal (depthOf compacted) (V4JsonDecoding.MaxDepth - 1) "the compacted document fits in an envelope"

            // The classic FQName that replaces the reference is two levels deeper than it.
            match expandText IrFamily.Classic3 "deep.json" (contextFor position.Entry) compacted with
            | Ok _ -> failtest "The expanded document is deeper than the limit."
            | Error errors ->
                Expect.equal (codesAndFiles errors) [ "depth_limit_exceeded", Some "deep.json" ] "the named diagnostic"
        }

        testList "A compacted file is read to the depth of its limits" [
            // Deeper than the readers of a tree (512), within the depth of a plain single file of model convert.
            let bytes, plain = Mother.deeplyNestedCompactedFile 300
            let read limits = MorphirFile.readPlainDocument limits "deep.json" (ReadOnlyMemory bytes)

            test "A file deeper than its limits is depth_limit_exceeded, not invalid_json" {
                Expect.isGreaterThan (nesting bytes) ContextLimits.draft1.MaxDepth "the file is deeper than draft1"
                Expect.equal (read ContextLimits.draft1 |> refusal |> codesAndFiles) [ "depth_limit_exceeded", Some "deep.json" ] "the named diagnostic"
            }

            test "The same file within deeper limits expands" {
                match read deepLimits with
                | Ok document -> Expect.equal (document.GetRawText()) plain "the plain document"
                | Error errors -> failtestf "%A" errors
            }

            test "Text that is not JSON stays invalid_json" {
                let broken = Array.append (Array.sub bytes 0 (bytes.Length - 1)) "]"B
                Expect.equal (MorphirFile.readPlainDocument ContextLimits.draft1 "deep.json" (ReadOnlyMemory broken) |> refusal |> List.map (fun e -> e.Code)) [ "invalid_json" ] "a deep file that is also broken"
            }

            // Near the depth of a plain single file of model convert (4096), and just past it.
            let nearTheLimit, nearTheLimitPlain = Mother.deeplyNestedCompactedFile 2000
            let pastTheLimit, _ = Mother.deeplyNestedCompactedFile 2060

            test "A file near the deeper limit expands on a stack that holds its depth" {
                Expect.isGreaterThan (nesting nearTheLimit) 4000 "the file is near the limit"
                Expect.isLessThan (nesting nearTheLimit) deepLimits.MaxDepth "the file is within the limit"

                match onStack (64 * 1024 * 1024) (fun () -> MorphirFile.readPlainDocument deepLimits "deep.json" (ReadOnlyMemory nearTheLimit)) with
                | Ok document -> Expect.equal (document.GetRawText()) nearTheLimitPlain "the plain document"
                | Error errors -> failtestf "%A" errors
            }

            test "A file near the deeper limit on a stack that cannot hold it is too deep, and the process goes on" {
                let read = onStack (256 * 1024) (fun () -> MorphirFile.readPlainDocument deepLimits "deep.json" (ReadOnlyMemory nearTheLimit))
                Expect.equal (read |> refusal |> codesAndFiles) [ "depth_limit_exceeded", Some "deep.json" ] "a refusal, not an exception"
            }

            test "A file just past the deeper limit is depth_limit_exceeded" {
                Expect.isGreaterThan (nesting pastTheLimit) deepLimits.MaxDepth "the file is past the limit"
                let read = onStack (64 * 1024 * 1024) (fun () -> MorphirFile.readPlainDocument deepLimits "deep.json" (ReadOnlyMemory pastTheLimit))
                Expect.equal (read |> refusal |> codesAndFiles) [ "depth_limit_exceeded", Some "deep.json" ] "the named diagnostic"
            }
        ]

        test "Context bytes count toward the file size limit" {
            let files = MorphirFileMother.tree 4 MorphirFileMother.ValueFileOnly
            let location = "pkg/my-org/billing/invoicing/line-total.value.json"
            let size = files |> List.find (fst >> (=) location) |> snd |> Text.Encoding.UTF8.GetByteCount |> int64

            use compacted = JsonDocument.Parse(files |> List.find (fst >> (=) location) |> snd)
            let documentSize = compacted.RootElement.GetProperty("document").GetRawText() |> Text.Encoding.UTF8.GetByteCount |> int64
            Expect.isLessThan documentSize (size - 1L) "the document alone is under the limit"

            let readWith limit =
                DocumentTreeStore.read { ReadOptions.defaults with MaxFileBytes = limit } (Driver.ofTexts files)

            Expect.isOk (readWith size) "a file of exactly the limit is read"
            Expect.equal (readWith (size - 1L) |> refusal |> codesAndFiles) [ "tree.file_too_large", Some location ] "the context's bytes count"
        }
    ]
