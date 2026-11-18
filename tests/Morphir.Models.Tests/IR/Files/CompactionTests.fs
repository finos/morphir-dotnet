/// Writing compacted Morphir files under the parent's writer policy morphir.compact/0.1.0-draft.1 (add-model-convert
/// 8.3): the policy's fixed cases byte for byte (FixtureCatalog.Context.Policy, through MorphirFileMother), the law
/// expand (compact f) = f over every fixture document, every mother case and generated models, single files and
/// trees, determinism, and the scenarios of model-name-compaction.
module Morphir.IR.Tests.Files.CompactionTests

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open FsCheck
open Morphir.IR
open Morphir.IR.Files
open Morphir.IR.Trees
open Morphir.Json
open Morphir.Json.Classic
open Morphir.Json.V4
open Morphir.Models.TestSupport
open Morphir.TestSupport.Fixtures
open Morphir.IR.Tests.Trees.StoreTestKit

module Mother = MorphirFileMother
module Driver = DocumentTreeStoreDriver
module Trees = DocumentTreeMother

let private config = {
    FsCheckConfig.defaultConfig with
        maxTest = 100
}

let private familyOfIr ir = if ir = 3 then IrFamily.Classic3 else IrFamily.V4

let private parse (bytes: byte array) =
    use document = JsonDocument.Parse(ReadOnlyMemory bytes, JsonDocumentOptions(MaxDepth = 4096))
    document.RootElement.Clone()

let private parseText (text: string) = parse (Encoding.UTF8.GetBytes text)

/// The bytes the policy writes for a document, and whether an alias was worth its cost, or a failure.
let private compacted family (document: JsonElement) =
    match CompactionPolicy.compact family document with
    | Ok result -> result
    | Error errors -> failtestf "The document was not compacted: %A" errors

/// The plain document that bytes written by the policy expand to, read as a reader does.
let private expandedDocument (file: string) (bytes: byte array) =
    let limits = ContextLimits.draft1.WithMaxDepth 4096

    match MorphirFile.readPlainDocument limits file (ReadOnlyMemory bytes) with
    | Ok document -> document
    | Error errors -> failtestf "%s did not expand: %A" file errors

/// Whether a member holds the parameters of a v4 value, whose key order is their order: the test's own reading of the
/// parent's schema, by member name, apart from the policy's grammar.
let private holdsParameters (name: string) = name = "inputTypes" || name = "inputs"

/// <summary>
/// The JSON of a value with the members of each object sorted, except those of an object that holds a value's
/// parameters, which keep their order. Two documents that compare equal in this form are the same model whatever the
/// order of the keys of their maps, and have the same parameters in the same order.
/// </summary>
let private orderOfMeaning (element: JsonElement) : string =
    let rec normalized (keepOrder: bool) (node: JsonNode | null) : JsonNode | null =
        match node with
        | :? JsonObject as o ->
            let members = List.ofSeq o
            let members = if keepOrder then members else members |> List.sortWith (fun a b -> String.CompareOrdinal(a.Key, b.Key))
            let copy = JsonObject()

            for KeyValue(name, value) in members do
                copy[name] <- normalized (holdsParameters name) value

            copy
        | :? JsonArray as a ->
            let copy = JsonArray()

            for item in a do
                copy.Add(normalized false item)

            copy
        | null -> null
        | other -> other.DeepClone()

    (nonNull (normalized false (JsonNode.Parse(element.GetRawText(), documentOptions = JsonDocumentOptions(MaxDepth = 900))))).ToJsonString()

/// Whether the bytes the policy writes for a document expand to the document, in the order of its meaning.
let private lossless family (document: JsonElement) =
    let bytes, _ = compacted family document
    orderOfMeaning (expandedDocument "file" bytes) = orderOfMeaning document

let private expectLossless family name (document: JsonElement) =
    let bytes, _ = compacted family document

    Expect.equal
        (orderOfMeaning (expandedDocument name bytes))
        (orderOfMeaning document)
        $"{name} expands to the document it was written from"

let private isEnvelope (bytes: byte array) =
    let root = parse bytes
    let mutable schema = Unchecked.defaultof<JsonElement>
    root.TryGetProperty("$schema", &schema)

let private v4Bytes (distribution: Morphir.IR.V4.Distribution) =
    match V4JsonWriter.writeDistribution true distribution with
    | Ok bytes -> bytes
    | Error error -> failtestf "Expected a v4 write, got %s at %s: %s" error.Rule error.Location error.Message

/// A classic Library is written at 3, and a Specs at 3.1.0; each value attribute holds the value's type.
let private v3Bytes (distribution: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>>) =
    let profile =
        match distribution with
        | Morphir.IR.Classic.Distribution.Library _ -> ClassicWireProfile.v3
        | Morphir.IR.Classic.Distribution.Specs _ -> ClassicWireProfile.v3_1_0

    match ClassicJsonWriter.writeTyped profile true distribution with
    | Ok bytes -> bytes
    | Error error -> failtestf "Expected a v3 write, got %s at %s: %s" error.Rule error.Location error.Message

/// The v4 model of a single file: its maps compare without regard to key order, and its parameters in order.
let private v4Model (document: JsonElement) =
    match V4Json.readDistribution document with
    | Ok read -> read.Distribution
    | Error error -> failtestf "Expected a v4 model, got %A" error

/// The classic model of a single file written with typed attributes, which compare by value.
let private v3Model (document: JsonElement) =
    match ClassicJson.readTypedDistribution document with
    | Ok read -> read.Distribution
    | Error error -> failtestf "Expected a classic model, got %A" error

/// <summary>
/// A decoded v4 model in the order of its meaning: written again and normalized, so the keys of its maps, which the
/// parent's v4 model does not order (record fields, module members, dependencies, constructors), compare in any order,
/// and its parameters, which it does, compare in order. The .NET model holds record fields as lists, so it cannot be
/// compared with <c>=</c> alone.
/// </summary>
let private v4Meaning (distribution: Morphir.IR.V4.Distribution) = orderOfMeaning (parse (v4Bytes distribution))

/// Whether a v4 single file, written under the policy and expanded, decodes to the same model.
let private sameV4Model (distribution: Morphir.IR.V4.Distribution) =
    let document = parse (v4Bytes distribution)
    let bytes, _ = compacted IrFamily.V4 document
    v4Meaning (v4Model (expandedDocument "model.json" bytes)) = v4Meaning (v4Model document)

/// Whether a classic single file, written under the policy and expanded, decodes to the same model: classic models
/// with typed attributes compare by value, and classic JSON has no map whose order a writer could change.
let private sameV3Model (distribution: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>>) =
    let document = parse (v3Bytes distribution)
    let bytes, _ = compacted IrFamily.Classic3 document
    v3Model (expandedDocument "model.json" bytes) = v3Model document

let private v4Mothers = [
    "v4LibraryWithOneModule", DistributionMother.v4LibraryWithOneModule
    "v4LibraryWithDependency", DistributionMother.v4LibraryWithDependency
    "v4Specs", DistributionMother.v4Specs
    "v4SpecsWithModuleAnnotations", DistributionMother.v4SpecsWithModuleAnnotations
    "v4Application", DistributionMother.v4Application
    "acmeDemo", DistributionMother.acmeDemo
    "dependencyWithoutModules", DistributionMother.dependencyWithoutModules
    "emptyModule", DistributionMother.emptyModule
    "noModules", DistributionMother.noModules
    "nameUsedOnce", DistributionMother.nameUsedOnce
    "nameUsedOften", DistributionMother.nameUsedOften
    "longNameUsedOften", DistributionMother.longNameUsedOften
    "windowsReservedStems", DistributionMother.windowsReservedStems
    "privateAndDocumentedMembers", DistributionMother.privateAndDocumentedMembers
    "v4LibraryWithUnsortedParameters", DistributionMother.v4LibraryWithUnsortedParameters
    "v4SpecsWithUnsortedParameters", DistributionMother.v4SpecsWithUnsortedParameters
]

let private v3Mothers = [
    "v3Library", DistributionMother.v3Library
    "v3Specs", DistributionMother.v3Specs
    "nonAsciiName", DistributionMother.nonAsciiName
    "v3PrivateAndDocumentedMembers", DistributionMother.v3PrivateAndDocumentedMembers
    "v3LongNameUsedOften", DistributionMother.v3LongNameUsedOften
]

/// The tree mother cases that the store writes. nonAsciiName has no canonical spelling, which a tree file needs.
let private treeMothers = [
    "acmeDemo", Trees.acmeDemo
    "v4LibraryWithDependency", Trees.v4LibraryWithDependency
    "privateAndDocumentedMembers", Trees.privateAndDocumentedMembers
    "v4Application", Trees.v4Application
    "emptyModule", Trees.emptyModule
    "noModules", Trees.noModules
    "dependencyWithoutModules", Trees.dependencyWithoutModules
    "v3Library", Trees.v3Library
    "v3LibraryWithDependency", Trees.v3LibraryWithDependency
    "v3Specs", Trees.v3Specs
    "v3PrivateAndDocumentedMembers", Trees.v3PrivateAndDocumentedMembers
    "longNameUsedOften", Trees.treeOf DistributionMother.longNameUsedOften
    "v3LongNameUsedOften", Trees.classicTreeOf (Trees.withNumberedSlots DistributionMother.v3LongNameUsedOften)
    "v4LibraryWithUnsortedParameters", Trees.treeOf DistributionMother.v4LibraryWithUnsortedParameters
    "v4SpecsWithUnsortedParameters", Trees.treeOf DistributionMother.v4SpecsWithUnsortedParameters
]

let private writtenOk (result: Result<Morphir.Files.VFileTree, StoreError list>) =
    match result with
    | Ok files -> files
    | Error errors -> failtestf "Expected the tree to be written, got %A" errors

/// The model of a tree read back: a v4 tree's joined distribution in the order of its meaning (<c>v4Meaning</c>); a
/// classic tree's single file in canonical JSON (its parameters are arrays, and its attribute slots are JSON, which
/// compare by reference).
let private treeModel (files: Morphir.Files.VFileTree) : Choice<string, byte array> option =
    match Driver.read files with
    | Ok(V4Tree tree, _) ->
        match V4DocumentTree.join tree with
        | Ok distribution -> Some(Choice1Of2(v4Meaning distribution))
        | Error _ -> None
    | Ok(ClassicTree tree, _) ->
        match ClassicDocumentTree.join tree with
        | Ok distribution ->
            match ClassicJsonWriter.writeJsonAttributes ClassicWireProfile.v3 false distribution with
            | Ok bytes -> Some(Choice2Of2(CanonicalJson.write (parse bytes)))
            | Error _ ->
                match ClassicJsonWriter.writeJsonAttributes ClassicWireProfile.v3_1_0 false distribution with
                | Ok bytes -> Some(Choice2Of2(CanonicalJson.write (parse bytes)))
                | Error _ -> None
        | Error _ -> None
    | Error _ -> None

/// What a tree written compacted does not keep: each file whose expansion is not its plain file in the order of its
/// meaning, and a tree that does not read back as the same model. Empty when the tree is written losslessly.
let private treeLosses (tree: StoredTree) =
    let plainFiles = writtenOk (Driver.write tree)
    let plain = Driver.texts plainFiles
    let compactedFiles = writtenOk (Driver.writeCompacted tree)
    let texts = Driver.texts compactedFiles

    [
        if Set.ofSeq plain.Keys <> Set.ofSeq texts.Keys then
            yield $"The files differ: {List.ofSeq plain.Keys} and {List.ofSeq texts.Keys}."
        else
            for KeyValue(location, text) in plain do
                let written = texts[location]

                if orderOfMeaning (expandedDocument location (Encoding.UTF8.GetBytes written)) <> orderOfMeaning (parseText text) then
                    yield $"{location} was written as {written}, and its plain file is {text}."

        match treeModel compactedFiles, treeModel plainFiles with
        | Some read, Some expected when read = expected -> ()
        | Some _, Some _ -> yield "The tree read back is another model."
        | None, _ -> yield "The compacted tree does not read back."
        | _, None -> yield "The plain tree does not read back."
    ]

/// Whether a tree written compacted holds, file by file, the plain files that its plain write holds once expanded,
/// and reads back as the same model; a failure names what it lost.
let private treeLossless (tree: StoredTree) =
    match treeLosses tree with
    | [] -> true
    | losses -> failwith (String.concat Environment.NewLine losses)

/// The same document with the members of each object in the reverse order, except a value's parameters, whose order
/// is the model's, and the content of a DocumentLiteral, which the IR carries as it is written: the same model,
/// written otherwise.
let private reversedMembers (element: JsonElement) =
    let rec reverse (keepOrder: bool) (verbatim: bool) (node: JsonNode | null) : JsonNode | null =
        match node with
        | :? JsonObject as o ->
            let members = List.ofSeq o
            let copy = JsonObject()

            for KeyValue(name, value) in (if keepOrder || verbatim then members else List.rev members) do
                copy[name] <- reverse (holdsParameters name) (verbatim || name = "DocumentLiteral") value

            copy
        | :? JsonArray as a ->
            let copy = JsonArray()

            for item in a do
                copy.Add(reverse false verbatim item)

            copy
        | null -> null
        | other -> other.DeepClone()

    parseText ((nonNull (reverse false false (JsonNode.Parse(element.GetRawText())))).ToJsonString())

/// The JSON of a value with no whitespace, its members in their order and its numbers as their lexemes.
let private verbatim (element: JsonElement) =
    use buffer = new IO.MemoryStream()

    do
        use writer =
            new Utf8JsonWriter(buffer, JsonWriterOptions(Encoder = Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping))

        element.WriteTo writer

    Encoding.UTF8.GetString(buffer.ToArray())

/// The content of each document literal of a document, as JSON with no whitespace, in document order.
let private documentLiterals (element: JsonElement) =
    let rec find (element: JsonElement) =
        [
            match element.ValueKind with
            | JsonValueKind.Object ->
                for property in element.EnumerateObject() do
                    if property.Name = "DocumentLiteral" then
                        yield verbatim property.Value
                    else
                        yield! find property.Value
            | JsonValueKind.Array ->
                for item in element.EnumerateArray() do
                    yield! find item
            | _ -> ()
        ]

    find element

/// The raw text of each number token of JSON text, in order.
let private numberLexemes (bytes: byte array) =
    let mutable reader = Utf8JsonReader(ReadOnlySpan bytes)
    let lexemes = ResizeArray()

    while reader.Read() do
        if reader.TokenType = JsonTokenType.Number then
            lexemes.Add(Encoding.UTF8.GetString(reader.ValueSpan))

    List.ofSeq lexemes

/// The parameter names of each value of a written file, in the order the file writes them: the keys of every object
/// that a member inputTypes or inputs holds.
let private parametersOf (bytes: byte array) =
    let rec find (element: JsonElement) =
        [
            match element.ValueKind with
            | JsonValueKind.Object ->
                for property in element.EnumerateObject() do
                    if holdsParameters property.Name && property.Value.ValueKind = JsonValueKind.Object then
                        yield [ for parameter in property.Value.EnumerateObject() -> parameter.Name ]
                    else
                        yield! find property.Value
            | JsonValueKind.Array ->
                for item in element.EnumerateArray() do
                    yield! find item
            | _ -> ()
        ]

    find (parse bytes)

[<Tests>]
let tests =
    testList "Compaction" [
        test "The policy is named morphir.compact/0.1.0-draft.1" {
            Expect.equal CompactionPolicy.id "morphir.compact/0.1.0-draft.1" "the parent's policy"
            Expect.equal CompactionPolicy.id FixtureCatalog.Context.Policy.Name "the policy the fixed cases are for"
        }

        test "The envelope's fixed bytes are 118" {
            Expect.equal CompactionPolicy.envelopeBytes 118 "12 + the 79-byte URL + 13 + 13 + 1"
        }

        test "Alias ids are the bijective base-26 numerals" {
            Expect.equal
                [ for position in [ 1; 2; 26; 27; 28; 52; 53; 702; 703 ] -> CompactionPolicy.aliasId position ]
                [ "a"; "b"; "z"; "aa"; "ab"; "az"; "ba"; "zz"; "aaa" ]
                "a to z, then aa, and on"
        }

        testList "The parent's fixed cases match byte for byte" [
            for policyCase in Mother.policyCases ->
                test policyCase.Id {
                    let bytes, aliased = compacted (familyOfIr policyCase.Ir) (Mother.policyInput policyCase.Id)

                    Expect.equal aliased policyCase.Compacted "whether an alias was worth its cost"
                    Expect.sequenceEqual bytes (Mother.policyExpectedBytes policyCase.Id) "the parent's expected bytes"
                }
        ]

        testList "Expand after compact" [
            testList "each fixture document and kit tree file" [
                let documents = [
                    for entry in FixtureCatalog.Models.All do
                        if entry.RelativePath.EndsWith(".json", StringComparison.Ordinal) then
                            yield entry.RelativePath, FixtureCatalog.ReadText entry
                    for kitCase in FixtureCatalog.Kit.DocumentTree do
                        for KeyValue(path, text) in kitCase.Files do
                            yield $"{kitCase.Id}/{path}", text
                    for policyCase in Mother.policyCases do
                        yield $"{policyCase.Id}/input.json", FixtureCatalog.ReadText policyCase.Input
                    for example in Mother.validExamples do
                        yield $"{example.Id} (plain twin)", Mother.expandedTextOf example.Id
                ]

                for name, text in documents ->
                    test name {
                        let document = parseText text
                        expectLossless (MorphirFile.familyOf document) name document
                    }
            ]

            testList "each mother case as a single file, as a decoded model" [
                for name, distribution in v4Mothers ->
                    test $"v4 {name}" {
                        expectLossless IrFamily.V4 name (parse (v4Bytes distribution))
                        Expect.isTrue (sameV4Model distribution) "the expanded file decodes to the same model"
                    }
                for name, distribution in v3Mothers ->
                    test $"v3 {name}" {
                        expectLossless IrFamily.Classic3 name (parse (v3Bytes distribution))

                        // nonAsciiName has a name that the classic reader refuses, by design; its JSON is compared.
                        if name <> "nonAsciiName" then
                            Expect.isTrue (sameV3Model distribution) "the expanded file decodes to the same model"
                    }
            ]

            testList "each mother case as a tree" [
                for name, tree in treeMothers ->
                    test name { Expect.isTrue (treeLossless tree) "every file expands to its plain file, and the tree reads back" }
            ]

            testPropertyWithConfig config "generated v4 single files, as decoded models"
            <| Prop.forAll (Arb.fromGen Generators.v4Distribution) (fun distribution ->
                lossless IrFamily.V4 (parse (v4Bytes distribution)) && sameV4Model distribution
            )

            testPropertyWithConfig config "generated v3 single files, with typed attributes, as decoded models"
            <| Prop.forAll (Arb.fromGen Generators.v3AnyDistribution) (fun distribution ->
                lossless IrFamily.Classic3 (parse (v3Bytes distribution)) && sameV3Model distribution
            )

            testPropertyWithConfig config "generated v4 trees"
            <| Prop.forAll (Arb.fromGen Generators.v4DistributionWithoutModuleAnnotations) (fun distribution ->
                treeLossless (Trees.treeOf distribution)
            )

            testPropertyWithConfig config "generated v3 trees"
            <| Prop.forAll (Arb.fromGen Generators.v3AnyDistribution) (fun distribution ->
                treeLossless (Trees.classicTreeOf (Trees.withNumberedSlots distribution))
            )

            test "generated models are compacted, not only declined" {
                let aliased =
                    Gen.sample 20 40 Generators.v3AnyDistribution
                    |> List.filter (fun distribution -> snd (compacted IrFamily.Classic3 (parse (v3Bytes distribution))))

                Expect.isNonEmpty aliased "some generated v3 single files have an alias worth its cost"
            }

            test "a legacy record whose field is named fields keeps the field's type" {
                let document = Mother.legacyRecordWithFieldNamedFields
                let bytes, aliased = compacted IrFamily.V4 document
                Expect.isTrue aliased "the field types share an alias"
                expectLossless IrFamily.V4 "legacy record" document
                Expect.isTrue (isEnvelope bytes) "an envelope"
            }
        ]

        testList "Compaction is deterministic" [
            for policyCase in Mother.policyCases ->
                test policyCase.Id {
                    let family = familyOfIr policyCase.Ir
                    let input = Mother.policyInput policyCase.Id
                    let first, _ = compacted family input
                    let second, _ = compacted family input
                    let reordered, _ = compacted family (reversedMembers input)
                    Expect.sequenceEqual second first "compacting twice gives identical bytes"
                    Expect.sequenceEqual reordered first "the same document with its members in another order gives the same bytes"
                }
        ]

        testList "Numbers and document literals are written as in the source" [
            for name, distribution, aliased in
                [ "declined", DistributionMother.exactNumbers, false
                  "compacted", DistributionMother.longNamesWithExactNumbers, true ] do
                yield test $"in a {name} single file" {
                    let plain = v4Bytes distribution
                    let bytes, wasAliased = compacted IrFamily.V4 (parse plain)
                    Expect.equal wasAliased aliased $"the file is {name}"
                    Expect.equal (List.sort (numberLexemes bytes)) (List.sort (numberLexemes plain)) "every number keeps its lexeme"

                    for lexeme in [ "9007199254740993"; "1.0"; "1e2"; "12345678901234567890123"; "-0" ] do
                        Expect.contains (numberLexemes bytes) lexeme $"{lexeme} is written as it is"

                    Expect.equal
                        (documentLiterals (parse bytes))
                        (documentLiterals (parse plain))
                        "the document literal keeps its keys' order and its lexemes"

                    Expect.equal
                        (documentLiterals (expandedDocument "model.json" bytes))
                        (documentLiterals (parse plain))
                        "the expanded document literal is the plain one"

                    Expect.isTrue (sameV4Model distribution) "the expanded file decodes to the same model"
                }

                yield test $"in the files of a {name} tree" {
                    let tree = Trees.treeOf distribution
                    Expect.isTrue (treeLossless tree) "the tree reads back as the same model"
                    let plain = Driver.texts (writtenOk (Driver.write tree))
                    let written = Driver.texts (writtenOk (Driver.writeCompacted tree))

                    for KeyValue(location, text) in plain do
                        let bytes = Encoding.UTF8.GetBytes written[location]
                        Expect.equal (List.sort (numberLexemes bytes)) (List.sort (numberLexemes (Encoding.UTF8.GetBytes text))) $"{location} keeps its numbers' lexemes"
                        Expect.equal (documentLiterals (parse bytes)) (documentLiterals (parseText text)) $"{location} keeps its document literal"
                }
        ]

        testList "A value's parameters keep their order" [
            let library = DistributionMother.v4LibraryWithUnsortedParameters
            let specs = DistributionMother.v4SpecsWithUnsortedParameters
            let order = [ "rate"; "amount" ]

            test "in a single file's value definition" {
                let bytes, _ = compacted IrFamily.V4 (parse (v4Bytes library))
                Expect.equal (parametersOf bytes) [ order ] "the written file keeps rate before amount"
                Expect.isTrue (sameV4Model library) "the expanded file decodes to the same model"
            }

            test "in a single file's value specification" {
                let bytes, _ = compacted IrFamily.V4 (parse (v4Bytes specs))
                Expect.equal (parametersOf bytes) [ order ] "the written file keeps rate before amount"
                Expect.isTrue (sameV4Model specs) "the expanded file decodes to the same model"
            }

            test "in a tree's value files" {
                for tree in [ Trees.treeOf library; Trees.treeOf specs ] do
                    Expect.isTrue (treeLossless tree) "the tree reads back as the same model"

                    for KeyValue(location, text) in Driver.texts (writtenOk (Driver.writeCompacted tree)) do
                        if location.EndsWith(".value.json", StringComparison.Ordinal) then
                            Expect.equal (parametersOf (Encoding.UTF8.GetBytes text)) [ order ] $"{location} keeps rate before amount"
            }
        ]

        test "A name used once stays spelled out" {
            // name-used-once: G is the type of the field loyalty, once, beside F three times.
            let bytes, aliased = compacted IrFamily.V4 (Mother.policyInput "name-used-once")
            let envelope = parse bytes
            let document = envelope.GetProperty "document"
            let input = Mother.policyInput "name-used-once"

            let fieldsOf (root: JsonElement) =
                root.GetProperty("def").GetProperty("TypeAliasDefinition").GetProperty("typeExp").GetProperty("Record").GetProperty("fields")

            let once = (fieldsOf input).GetProperty("loyalty").GetString()
            Expect.isTrue aliased "F is worth its alias"
            Expect.equal ((fieldsOf document).GetProperty("loyalty").GetString()) once "the written file spells the name in place"

            let spellings = [
                for table in envelope.GetProperty("context").EnumerateObject() do
                    for entry in table.Value.EnumerateObject() -> entry.Value.GetString()
            ]

            Expect.isFalse (List.contains once spellings) "the table has no entry for it"
        }

        test "A name used once in a mother case stays spelled out" {
            let bytes, aliased = compacted IrFamily.V4 (parse (v4Bytes DistributionMother.nameUsedOnce))
            Expect.isFalse aliased "no alias is worth its cost"
            Expect.isFalse (isEnvelope bytes) "the plain document"
            Expect.stringContains (Encoding.UTF8.GetString bytes) "\"morphir/SDK:string#string\"" "the name is spelled in place"
        }

        testList "Compaction that would be a loss is not applied" [
            for id in [ "loss"; "loss-boundary-declined"; "v4-manifest"; "v3-manifest" ] do
                yield test id {
                    let input = Mother.policyInput id
                    let bytes, aliased = compacted (MorphirFile.familyOf input) input
                    Expect.isFalse aliased "the result says no alias was worth its cost"
                    Expect.isFalse (isEnvelope bytes) "no envelope"
                    Expect.sequenceEqual bytes (CanonicalJson.write input) "the plain document in canonical JSON"
                }

            yield test "savings one byte above the overhead are applied" {
                let bytes, aliased = compacted IrFamily.V4 (Mother.policyInput "loss-boundary-accepted")
                Expect.isTrue aliased "an alias is worth its cost"
                Expect.equal bytes.Length ((CanonicalJson.write (Mother.policyInput "loss-boundary-accepted")).Length - 1) "one byte smaller"
            }
        ]

        test "Non-ASCII content is measured as UTF-8" {
            let input = Mother.policyInput "non-ascii-content"
            let bytes, _ = compacted IrFamily.V4 input
            let text = Encoding.UTF8.GetString bytes
            Expect.equal (CanonicalJson.byteLength "é") 4 "é is two bytes, with the two quotes"
            Expect.stringContains text "réduit après" "the written file holds é and è unescaped"
            Expect.isFalse (text.Contains("\\u00e9", StringComparison.OrdinalIgnoreCase)) "no escape"
            Expect.sequenceEqual bytes (Mother.policyExpectedBytes "non-ascii-content") "the parent's bytes, sized in UTF-8"
        }

        test "A classic slot that cannot hold a spelling keeps its arrays" {
            // v3-type's field types are F twice, which the policy aliases. With the local name's words as ["US"], an
            // initialism read as one word, the arrays read as US, which reads back as the words u and s: expansion
            // could not give the arrays back, so the policy leaves them.
            let document = Mother.v3TypeWithUnholdableLocalName
            let bytes, aliased = compacted IrFamily.Classic3 document
            Expect.isFalse aliased "no alias is worth its cost without F"
            Expect.sequenceEqual bytes (CanonicalJson.write document) "the arrays are written as they are"
            expectLossless IrFamily.Classic3 "v3 type with an unholdable name" document
        }

        testList "Refusals" [
            test "A document in an envelope is not plain" {
                let envelope = parse (Mother.policyExpectedBytes "v4-type")

                match CompactionPolicy.compact IrFamily.V4 envelope with
                | Error [ e ] ->
                    Expect.equal e.Code "unknown_member" "the codec's rule for a member the document does not define"
                    Expect.stringContains e.Message "$schema" "the member"
                | other -> failtestf "Expected one refusal, got %A" other
            }

            test "A reference at a typed position is not plain" {
                let document = (parse (Mother.policyExpectedBytes "v4-type")).GetProperty "document"

                match CompactionPolicy.compact IrFamily.V4 document with
                | Error [ e ] ->
                    Expect.equal e.Code "invalid_type" "a reference where the spelling goes"
                    Expect.stringContains e.Message "context.fqNames" "the table of the position"
                | other -> failtestf "Expected one refusal, got %A" other
            }

            test "A document deeper than canonical JSON reads is too deep" {
                match CompactionPolicy.compact IrFamily.V4 Mother.v4TypeNestedPastCanonicalDepth with
                | Error [ e ] -> Expect.equal e.Code "depth_limit_exceeded" "the depth"
                | other -> failtestf "Expected one refusal, got %A" other
            }
        ]

        testList "Writing compacted files" [
            test "A compacted tree is written file by file" {
                let files = writtenOk (Driver.writeCompacted (Trees.treeOf DistributionMother.longNameUsedOften))
                let texts = Driver.texts files

                let plainFiles =
                    Morphir.Files.VFileTree.tryGet DocumentTreeStore.plainFiles files
                    |> Option.defaultWith (fun () -> failtest "A compacted write lists the files it left plain")

                Expect.contains plainFiles "manifest.json" "a manifest never repeats a package path"

                for KeyValue(location, text) in texts do
                    let bytes = Encoding.UTF8.GetBytes text
                    Expect.sequenceEqual (CanonicalJson.write (parse bytes)) bytes $"{location} is canonical JSON, with no newline after it"
                    Expect.equal (isEnvelope bytes) (not (List.contains location plainFiles)) $"{location} is an envelope unless it is listed plain"

                match Driver.read files with
                | Ok(_, report) -> Expect.isNonEmpty report.CompactedFiles "the read lists the compacted files"
                | Error errors -> failtestf "%A" errors
            }

            test "A plain write lists no plain files" {
                let files = writtenOk (Driver.write Trees.acmeDemo)
                Expect.isNone (Morphir.Files.VFileTree.tryGet DocumentTreeStore.plainFiles files) "only a compacted write sets it"
            }

        ]
    ]
