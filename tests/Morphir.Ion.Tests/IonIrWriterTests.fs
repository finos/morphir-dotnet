/// The canonical Ion writer. Every written file must read back through IonIr and IonIrTree as the distribution it came
/// from, and its bytes must follow the canonical text of the Morphir Ion draft. The reference CLI files under `pairs/`
/// and `trees/` are the byte reference, after the two spellings the draft does not use are normalized away.
module Morphir.Ion.Tests.IonIrWriterTests

open System
open System.Text
open System.Text.RegularExpressions
open Expecto
open Morphir.Files
open Morphir.Ion
open Morphir.Ion.Tests

let private limits = IonReadLimits.defaults

let private text (bytes: byte array) = Encoding.UTF8.GetString bytes

let private ok what result =
    match result with
    | Ok value -> value
    | Result.Error(error: IonIrError) -> failtestf "%s failed: %s: %s" what error.Code error.Message

/// A catalogued upstream IR JSON document by its path below MorphirIon, such as pairs/v3-specs.json.
let private catalogued (name: string) =
    Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.Documents
    |> Seq.tryFind (fun entry -> Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.NameOf entry = name)
    |> Option.defaultWith (fun () -> failwithf "FixtureCatalog.UpstreamIr has no document %s." name)

let private catalogBytes name =
    Morphir.TestSupport.Fixtures.FixtureCatalog.ReadBytes(catalogued name)

let private catalogText name =
    Morphir.TestSupport.Fixtures.FixtureCatalog.ReadText(catalogued name)

let private fromJson (name: string) =
    IonIrWriter.readJson (ReadOnlyMemory(catalogBytes name))
    |> ok $"Reading {name}"

let private readIon (bytes: byte array) =
    IonIr.read limits (ReadOnlyMemory bytes)
    |> ok "Reading the written Ion"

let private expectSame (actual: IrDocument) (expected: IrDocument) =
    match actual, expected with
    | ClassicIr(_, a), ClassicIr(_, b) ->
        Expect.equal
            (ClassicComparison.comparable a)
            (ClassicComparison.comparable b)
            "The Ion reader builds the classic value that was written"
    | V4Ir(_, a), V4Ir(_, b) -> Expect.equal a b "The Ion reader builds the v4 value that was written"
    | _ -> failtest "The written family differs from the one read"

/// The reference CLI writes two spellings that the draft's canonical text does not: spaces between an annotation and a
/// list item's struct, and the empty v3 constructor argument list. Both read the same.
let private normalize (reference: string) =
    let joined = Regex.Replace(reference, @"::[ ]+(?=[\{\[\(])", "::")
    Regex.Replace(joined, @"\n[ ]*args: \[\n[ ]*\],", "")

/// The reference CLI writes an annotation with no arguments as <c>{ name }</c> when its source did; the IR holds no
/// such choice, and the canonical writer uses the compact string.
let private structuredBare (reference: string) =
    Regex.Replace(reference, @"\n([ ]*)\{\n[ ]*name: (""[^""#]*#[^""]*""),\n[ ]*\},", "\n$1$2,")
    // The reference CLI writes a document number with a positive exponent as text. The draft makes it an Ion decimal
    // whenever the reader's lexeme rule gives the lexeme back, which it does for 1e+2 and 1e+5.
    |> fun text -> text.Replace("number::\"1e+2\"", "1.d+2").Replace("number::\"1e+5\"", "1.d+5")

let private pairNames =
    Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.Documents
    |> Seq.map Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.NameOf
    |> Seq.filter (fun name -> name.StartsWith "pairs/" && name.EndsWith ".json")
    |> Seq.map (fun name -> name.Substring("pairs/".Length, name.Length - "pairs/".Length - ".json".Length))
    |> List.ofSeq

/// The reference Ion text of a catalogued file, by its path below MorphirIon, such as pairs/v3-specs.ion.
let private referenceText (name: string) =
    Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.IonReferences
    |> Seq.tryFind (fun entry -> Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.NameOf entry = name)
    |> Option.map Morphir.TestSupport.Fixtures.FixtureCatalog.ReadText
    |> Option.defaultWith (fun () -> failwithf "FixtureCatalog.UpstreamIr has no reference Ion file %s." name)

/// The files of a catalogued reference tree, by their location in it, in ordinal order.
let private treeFiles (name: string) =
    let prefix = $"trees/{name}/"

    Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.IonReferences
    |> Seq.map (fun entry -> Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.NameOf entry, entry)
    |> Seq.filter (fun (file, _) -> file.StartsWith prefix)
    |> Seq.map (fun (file, entry) -> file.Substring prefix.Length, Morphir.TestSupport.Fixtures.FixtureCatalog.ReadText entry)
    |> Seq.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
    |> List.ofSeq

let private written (files: (string * byte array) list) =
    files
    |> List.map (fun (location, bytes) -> location, text bytes)

let private treeOf (files: (string * byte array) list) =
    files
    |> List.map (fun (location, bytes) -> VFile.ofBytes location (ReadOnlyMemory bytes))
    |> VFileTree.fromFiles

let private parseVersion (text: string) =
    match Morphir.IR.Versioning.parse text with
    | Ok version -> version
    | Result.Error error -> failwithf "%A" error

let private version31 () = parseVersion "3.1.0"

/// Reads JSON text that the test writes by hand, because the IR it holds is not valid or not a model a builder makes.
let private readJsonText (json: string) =
    IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json))
    |> ok "Reading the JSON"

/// Named cases: a model the test builds by intent, and the reference bytes the vendored draft states for it.
module private Cases =
    open Morphir.Models.TestSupport.DistributionBuilder
    open Morphir.TestSupport.Fixtures

    /// A classic model of the builder as the Ion writer takes it: through the classic JSON writer at IR 3, which
    /// leaves out an absent doc, and IonIrWriter.readJson.
    let classic (distribution: Morphir.IR.Classic.Distribution.Distribution<unit, Morphir.IR.Classic.Type<unit>>) =
        match Morphir.Json.Classic.ClassicJsonWriter.writeTyped Morphir.Json.Classic.ClassicWireProfile.v3 false distribution with
        | Ok json ->
            match IonIrWriter.readJson (ReadOnlyMemory json) with
            | Ok document -> document
            | Result.Error error -> failwithf "%s: %s" error.Code error.Message
        | Result.Error error -> failwithf "%s: %s" error.Rule error.Message

    /// The draft's empty v3 library: package example, no dependencies and no modules.
    let emptyV3Library = aV3Library (packageName "example") |> buildV3 |> classic

    /// A v3 library whose alias t has no documentation, the classic model's WithoutDocumentation.
    let undocumentedV3Alias =
        aV3Library (packageName "example") |> withModule (modulePath "m") (withType (name "t") (alias Sdk.int)) |> buildV3 |> classic

    /// The same alias with the empty documentation, WithDocumentation("", ...).
    let emptyDocumentedV3Alias =
        aV3Library (packageName "example")
        |> withModule (modulePath "m") (withType (name "t") (alias Sdk.int) >> withTypeDoc (name "t") "")
        |> buildV3
        |> classic

    /// The fence after "the canonical empty v3 library is:" in the pinned draft.
    let draftEmptyV3Library =
        let draft = FixtureCatalog.ReadText FixtureCatalog.UpstreamIr.IonDraft
        let fence = Regex.Match(draft, "the canonical empty v3 library is:\\s*```ion\\n(.*?)```", RegexOptions.Singleline)

        if not fence.Success then
            failwith "The pinned draft has no canonical empty v3 library."

        fence.Groups[1].Value

    /// A v4 library whose value greet is the string literal hello.
    let greeting =
        V4Ir(
            "4.0.0",
            aV4Library (packageName "p")
            |> withModule (modulePath "m") (withValue (name "greet") (Constant(TextValue "hello")))
            |> buildV4
        )

    /// A v4 library with one alias type whose name is too long for a stem in a budget of 64, and the name.
    let longTypeName =
        let typeName = String.replicate 30 "word-" + "end"
        typeName, V4Ir("4.0.0", aV4Library (packageName "p") |> withModule (modulePath "m") (withType (name typeName) (alias Sdk.int)) |> buildV4)

    /// A v4 library with one empty module whose path alone is longer than a budget of 64.
    let longModulePath =
        V4Ir("4.0.0", aV4Library (packageName "p") |> withModule (modulePath (String.replicate 12 "segment-" + "end")) id |> buildV4)

    /// A v4 library with two alias types whose cut stems in a budget of 64 have one prefix and one digest, 905bb72b.
    let collidingStems =
        let first, second = "commonprefixcommonprefixcommonprefixcommon-n58445", "commonprefixcommonprefixcommonprefixcommon-n91859"

        V4Ir(
            "4.0.0",
            aV4Library (packageName "p")
            |> withModule (modulePath "m") (withType (name first) (alias Sdk.int) >> withType (name second) (alias Sdk.int))
            |> buildV4
        )

[<Tests>]
let tests =
    testList "Ion IR writer" [
        testList "Canonical text" [
            testCase "The empty v3 library is the draft's example, byte for byte"
            <| fun () ->
                Expect.equal
                    (IonIrWriter.write Cases.emptyV3Library |> ok "Writing" |> text)
                    Cases.draftEmptyV3Library
                    "The canonical empty library of the vendored draft"

            testCase "An S-expression puts each operand on its own line, with no commas"
            <| fun () ->
                let actual = IonIrWriter.write Cases.greeting |> ok "Writing" |> text

                Expect.stringContains actual "      body: (\n        string\n        \"hello\"\n      ),\n" "(string \"hello\")"
                Expect.stringContains actual "  values: [\n    public::def::value::{\n" "Annotations touch the struct"

            testCase "Symbols are bare only when Ion reads them back as the same symbol"
            <| fun () ->
                let bare = [ "library"; "start"; "_"; "a1"; "$meta" ]
                let quoted = [ ""; "on event"; "value-in-USD"; "null"; "true"; "false"; "nan"; "$10"; "1a" ]

                for symbol in bare do
                    Expect.isTrue (IonTextWriter.isBareSymbol symbol) $"{symbol} is bare"

                for symbol in quoted do
                    Expect.isFalse (IonTextWriter.isBareSymbol symbol) $"'{symbol}' is quoted"

            testCase "Quoted text uses the draft's escapes and keeps every other scalar as UTF-8"
            <| fun () ->
                let value = IonText.Text "a\u0000\a\b\t\n\u000B\f\r\\\"'é😀\u0001"

                Expect.equal
                    (IonTextWriter.render [ value ])
                    "\"a\\0\\a\\b\\t\\n\\v\\f\\r\\\\\\\"\\'é😀\\x01\"\n"
                    "The escapes"

            testCase "A float is its shortest spelling with an exponent"
            <| fun () ->
                Expect.equal (IonTextWriter.floatText 1.5) "1.5e0" "1.5"
                Expect.equal (IonTextWriter.floatText 1e20) "1e20" "1e20"
                Expect.equal (IonTextWriter.floatText 1.5e-5) "1.5e-5" "1.5e-5"
                Expect.equal (IonTextWriter.floatText -0.0) "-0e0" "Negative zero"
                Expect.equal (IonTextWriter.floatText 100.0) "1e2" "100"
                Expect.equal (IonTextWriter.floatText 0.001) "1e-3" "0.001"
                Expect.equal (IonTextWriter.floatText 1234.5) "1.2345e3" "1234.5"
                Expect.equal (IonTextWriter.floatText -2.5e-7) "-2.5e-7" "-2.5e-7"
                Expect.equal (IonTextWriter.floatText 0.0) "0e0" "Zero"

                for value in [ 0.1; 1.0 / 3.0; Double.MaxValue; Double.Epsilon; -123456.789e100 ] do
                    let text = IonTextWriter.floatText value
                    Expect.equal (Double.Parse(text, Globalization.CultureInfo.InvariantCulture)) value $"{text} reads back"

            testCase "The file ends with one line feed and has no byte-order mark"
            <| fun () ->
                let bytes = fromJson "pairs/v4-library-nodes.json" |> IonIrWriter.write |> ok "Writing"
                Expect.notEqual bytes[0] 0xEFuy "No byte-order mark"
                Expect.equal bytes[bytes.Length - 1] (byte '\n') "A final line feed"
                Expect.notEqual bytes[bytes.Length - 2] (byte '\n') "Only one"
        ]

        testList "Single file" [
            for name in pairNames do
                testCase $"{name} reads back as the distribution it came from"
                <| fun () ->
                    let document = fromJson $"pairs/{name}.json"
                    let bytes = IonIrWriter.write document |> ok "Writing"
                    expectSame (readIon bytes) document

                // The reference CLI spells the initialism of complete-example as words (u-s for US), which is not the
                // canonical name, and the classic model keeps the members of a v3 module in name order, not the source
                // order the reference CLI kept.
                if not (List.contains name [ "complete-example"; "v3-specs"; "v3-library-nodes" ]) then
                    testCase $"{name} gives the bytes of the reference file"
                    <| fun () ->
                        let actual = fromJson $"pairs/{name}.json" |> IonIrWriter.write |> ok "Writing" |> text
                        let reference = referenceText $"pairs/{name}.ion" |> normalize |> structuredBare
                        Expect.equal actual reference "The canonical bytes"

                testCase $"{name} is a fixed point of the writer"
                <| fun () ->
                    let bytes = fromJson $"pairs/{name}.json" |> IonIrWriter.write |> ok "Writing"
                    let again = readIon bytes |> IonIrWriter.write |> ok "Writing again"
                    Expect.equal (text again) (text bytes) "Writing what was read gives the same bytes"
        ]

        testList "Document tree" [
            for name in pairNames do
                testCase $"{name} reads back as the distribution it came from"
                <| fun () ->
                    let document = fromJson $"pairs/{name}.json"

                    let tree =
                        IonIrWriter.writeTree IonIrWriter.DefaultPathBudget document
                        |> ok "Writing the tree"

                    match IonIrTree.read limits tree with
                    | Ok read -> expectSame read document
                    | Result.Error error -> failtestf "The tree was refused: %s: %s %A" error.Code error.Message error.Path

            // The reference CLI keeps the source spelling of an annotation with no arguments, which the IR does not
            // hold, so v4-specs and v4-library-nodes are compared after it is normalized. It spells initialisms as
            // words (u-s for US) where the canonical name and TreeStems write US and _us, and a classic constructor
            // map has no source order, so complete-example and the v3 cases with constructors are read back but not
            // compared byte for byte.
            for name in [ "v3-daily-rate"; "v4-application"; "v4-empty-library"; "v4-specs"; "v4-library-nodes"; "books-and-records-example" ] do
                testCase $"{name} gives the files of the reference tree"
                <| fun () ->
                    let files =
                        IonIrWriter.writeTreeFiles IonIrWriter.DefaultPathBudget (fromJson $"pairs/{name}.json")
                        |> ok "Writing the tree"
                        |> written

                    let expected =
                        treeFiles name
                        |> List.map (fun (location, content) -> location, normalize content |> structuredBare)

                    Expect.sequenceEqual (List.map fst files) (List.map fst expected) "The same files"

                    for (location, actual), (_, reference) in List.zip files expected do
                        Expect.equal actual reference $"The bytes of {location}"

            testCase "Windows device names, initialisms and tree file names give the reference tree"
            <| fun () ->
                let document =
                    IonIrWriter.readJson (
                        ReadOnlyMemory(
                            Morphir.TestSupport.Fixtures.FixtureCatalog.ReadBytes
                                Morphir.TestSupport.Fixtures.FixtureCatalog.UpstreamIr.EscapedNamesInput
                        )
                    )
                    |> ok "Reading the escaped-names input"

                let files =
                    IonIrWriter.writeTreeFiles IonIrWriter.DefaultPathBudget document
                    |> ok "Writing the tree"

                let expected =
                    treeFiles "v4-escaped-names"
                    |> List.map (fun (location, content) -> location, normalize content |> structuredBare)

                Expect.sequenceEqual (List.map fst files) (List.map fst expected) "The same files"

                for (location, actual), (_, reference) in List.zip (written files) expected do
                    Expect.equal actual reference $"The bytes of {location}"

                match IonIrTree.read limits (treeOf files) with
                | Ok read -> expectSame read document
                | Result.Error error -> failtestf "The tree was refused: %s: %s" error.Code error.Message

            testCase "A path over the budget has its stem cut, and the file states its name"
            <| fun () ->
                let name, document = Cases.longTypeName

                let files =
                    IonIrWriter.writeTreeFiles 64 document
                    |> ok "Writing the tree"
                    |> written

                let location, content =
                    files
                    |> List.find (fun (location, _) -> location.EndsWith ".type.ion")

                Expect.isLessThanOrEqual location.Length 64 "The path fits the budget"
                Expect.isTrue (Regex.IsMatch(location, @"__[0-9a-f]{8}\.type\.ion$")) $"{location} is a cut stem"
                Expect.stringContains content $"  name: \"{name}\",\n" "The file states the name"

                match IonIrTree.read limits (treeOf (IonIrWriter.writeTreeFiles 64 document |> ok "Writing")) with
                | Ok read -> expectSame read document
                | Result.Error error -> failtestf "The tree was refused: %s: %s" error.Code error.Message

            testCase "Two cut stems that share a prefix and a digest are refused, not written to one path"
            <| fun () ->
                match IonIrWriter.writeTreeFiles 64 Cases.collidingStems with
                | Ok files -> failtestf "The tree has one file for two types: %A" (List.map fst files)
                | Result.Error error ->
                    Expect.equal error.Code "stem_collision" error.Message
                    Expect.stringContains error.Message "__905bb72b.type.ion" "The message names the path"

            testCase "A module path over the budget is refused"
            <| fun () ->
                match IonIrWriter.writeTreeFiles 64 Cases.longModulePath with
                | Ok _ -> failtest "A module file that does not fit is refused"
                | Result.Error error -> Expect.equal error.Code "path_over_budget" "The code"

            testCase "A path budget below the minimum is refused"
            <| fun () ->
                match IonIrWriter.writeTreeFiles 63 (fromJson "pairs/v4-empty-library.json") with
                | Ok _ -> failtest "A budget under 64 is refused"
                | Result.Error error -> Expect.equal error.Code "invalid_path_budget" "The code"
        ]

        testList "C# members" [
            testCase "TryWriteJson writes the datagram that write gives"
            <| fun () ->
                let json = ReadOnlyMemory(catalogBytes "pairs/v4-specs.json")

                match IonIrWriting.TryWriteJson json with
                | true, ion, _ -> Expect.equal (text ion) (fromJson "pairs/v4-specs.json" |> IonIrWriter.write |> ok "Writing" |> text) "The same bytes"
                | false, _, failure -> failtestf "%s" failure.Message

            testCase "TryWriteJson keeps the format version that the JSON states"
            <| fun () ->
                let json (version: string) =
                    ReadOnlyMemory(Encoding.UTF8.GetBytes((catalogText "pairs/v3-daily-rate.json").Replace("{\"formatVersion\":3,", $"{{\"formatVersion\":{version},")))

                for version, release in [ "3", "3.0.0"; "\"3.0.0\"", "3.0.0"; "\"3.1.0\"", "3.1.0" ] do
                    match IonIrWriting.TryWriteJson(json version) with
                    | true, ion, _ -> Expect.stringContains (text ion) $"  formatVersion: \"{release}\",\n" $"{version} states {release}"
                    | false, _, failure -> failtestf "%s: %s %s" version failure.Code failure.Message

            for version in [ "3.0.1"; "3.1.1" ] do
                testCase $"TryWriteJson refuses formatVersion {version} instead of relabelling it"
                <| fun () ->
                    let json = (catalogText "pairs/v3-daily-rate.json").Replace("{\"formatVersion\":3,", $"{{\"formatVersion\":\"{version}\",")

                    match IonIrWriting.TryWriteJson(ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                    | true, ion, _ -> failtestf "%s was written as: %s" version ((text ion).Split('\n')[2])
                    | false, _, failure -> Expect.equal failure.Code "unsupported_format_version_minor" failure.Message

                testCase $"readJson refuses formatVersion {version}, so no release can relabel it"
                <| fun () ->
                    let json =
                        ReadOnlyMemory(
                            Encoding.UTF8.GetBytes(
                                (catalogText "pairs/v3-daily-rate.json").Replace("{\"formatVersion\":3,", $"{{\"formatVersion\":\"{version}\",")
                            )
                        )

                    match IonIrWriter.readJson json with
                    | Ok document -> failtestf "%s was read as %A" version document
                    | Result.Error error -> Expect.equal error.Code "unsupported_format_version_minor" error.Message

                    match IonIrWriter.ofJsonAs (version31 ()) json with
                    | Ok bytes -> failtestf "%s was relabelled: %s" version ((text bytes).Split('\n')[2])
                    | Result.Error error -> Expect.equal error.Code "unsupported_format_version_minor" error.Message

                    match IonIrWriting.TryWriteJson(json, version31 ()) with
                    | true, ion, _ -> failtestf "%s was relabelled: %s" version ((text ion).Split('\n')[2])
                    | false, _, failure -> Expect.equal failure.Code "unsupported_format_version_minor" failure.Message

            testCase "TryWriteJson with a stated release writes that release"
            <| fun () ->
                let json = ReadOnlyMemory(catalogBytes "pairs/v3-daily-rate.json")

                match IonIrWriting.TryWriteJson(json, version31 ()) with
                | true, ion, _ -> Expect.stringContains (text ion) "  formatVersion: \"3.1.0\",\n" "The stated release"
                | false, _, failure -> failtestf "%s %s" failure.Code failure.Message

                match IonIrWriting.TryWriteJson(json, parseVersion "4.0.0") with
                | true, _, _ -> failtest "A v4 release is not a classic release"
                | false, _, failure -> Expect.equal failure.Code "unsupported_format_version_major" failure.Message

                match IonIrWriting.TryWriteJson(json, parseVersion "2") with
                | true, _, _ -> failtest "IR 2 is not carried"
                | false, _, failure -> Expect.equal failure.Code "unsupported_format_version_major" failure.Message

            testCase "TryWriteJson refuses a null format version as an argument error"
            <| fun () ->
                Expect.throwsT<ArgumentNullException>
                    (fun () ->
                        IonIrWriting.TryWriteJson(ReadOnlyMemory(catalogBytes "pairs/v3-daily-rate.json"), Unchecked.defaultof<Morphir.IR.Versioning.FormatVersion>)
                        |> ignore
                    )
                    "A null version is a caller error, not a decode failure"

            for version in [ "3.0.1"; "3.1.1" ] do
                testCase $"formatVersion {version} with a null distribution is refused before the distribution is read"
                <| fun () ->
                    let json = ReadOnlyMemory(Encoding.UTF8.GetBytes $"{{\"formatVersion\":\"{version}\",\"distribution\":null}}")

                    match IonIrWriter.readJson json with
                    | Ok _ -> failtest "Read"
                    | Result.Error error -> Expect.equal error.Code "unsupported_format_version_minor" $"readJson: {error.Message}"

                    match IonIrWriter.ofJson json with
                    | Ok _ -> failtest "Written"
                    | Result.Error error -> Expect.equal error.Code "unsupported_format_version_minor" $"ofJson: {error.Message}"

                    match IonIrWriter.ofJsonAs (version31 ()) json with
                    | Ok _ -> failtest "Relabelled"
                    | Result.Error error -> Expect.equal error.Code "unsupported_format_version_minor" $"ofJsonAs: {error.Message}"

                    match IonIrWriter.ofJsonTree 4000 json with
                    | Ok _ -> failtest "Written as a tree"
                    | Result.Error error -> Expect.equal error.Code "unsupported_format_version_minor" $"ofJsonTree: {error.Message}"

            for name, json in
                [
                    "a supported version first", "{\"formatVersion\":3,\"formatVersion\":\"3.0.1\",\"distribution\":null}"
                    "an unsupported version first", "{\"formatVersion\":\"3.0.1\",\"formatVersion\":3,\"distribution\":null}"
                ] do
                testCase $"A repeated formatVersion with {name} is refused"
                <| fun () ->
                    let bytes = ReadOnlyMemory(Encoding.UTF8.GetBytes json)

                    match IonIrWriter.readJson bytes with
                    | Ok _ -> failtest "Read"
                    | Result.Error error -> Expect.equal error.Code "duplicate_format_version" error.Message

                    match IonIrWriting.TryWriteJson(bytes, version31 ()) with
                    | true, _, _ -> failtest "Relabelled"
                    | false, _, failure -> Expect.equal failure.Code "duplicate_format_version" failure.Message

            for spelling, code in
                [
                    "4.0", "invalid_format_version_type"
                    "-3", "invalid_format_version_type"
                    "1e2", "invalid_format_version_type"
                    "true", "invalid_format_version_type"
                    "0", "invalid_format_version_syntax"
                    "\"2.0.0\"", "invalid_format_version_syntax"
                    "\"3.1\"", "invalid_format_version_syntax"
                    "\"03.0.0\"", "invalid_format_version_syntax"
                    "\"3.1.0-alpha\"", "invalid_format_version_syntax"
                    "\" 3.0.0\"", "invalid_format_version_syntax"
                    "\"3.4294967296.0\"", "format_version_out_of_range"
                    "4294967296", "format_version_out_of_range"
                    "1", "unsupported_format_version_major"
                    "5", "unsupported_format_version_major"
                    "\"4.0.1\"", "unsupported_format_version_minor"
                ] do
                testCase $"formatVersion {spelling} is {code}, by the format-version grammar before Ion support"
                <| fun () ->
                    let json = ReadOnlyMemory(Encoding.UTF8.GetBytes $"{{\"formatVersion\":{spelling},\"distribution\":null}}")

                    match IonIrWriter.readJson json with
                    | Ok _ -> failtest "Read"
                    | Result.Error error -> Expect.equal error.Code code error.Message

            testCase "A v3 library with a member the classic model cannot keep is refused, not dropped"
            <| fun () ->
                let original = catalogText "pairs/v3-daily-rate.json"
                let json = original.Replace("{\"formatVersion\":3,", "{\"formatVersion\":3,\"extra\":true,")
                Expect.notEqual json original "The vector gains a root member"

                match IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                | Ok _ -> failtest "The member was dropped"
                | Result.Error error -> Expect.equal error.Code "unknown_member" error.Message

            testCase "A formatVersion string with a lone surrogate is invalid_format_version_syntax at $.formatVersion"
            <| fun () ->
                match IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"formatVersion\":\"3.0.\\uD800\",\"distribution\":null}")) with
                | Ok _ -> failtest "Read"
                | Result.Error error ->
                    Expect.equal error.Code "invalid_format_version_syntax" error.Message
                    Expect.equal error.Location (Some "$.formatVersion") "The location"

            for name, edit in
                [
                    "a repeated root distribution",
                    fun (text: string) ->
                        let json = text.TrimEnd()
                        let at = json.IndexOf ",\"distribution\":"
                        json.Substring(0, json.Length - 1) + json.Substring(at, json.Length - 1 - at) + "}"
                    "a repeated member of a nested object",
                    fun (json: string) -> json.Replace("{\"access\":\"Public\",", "{\"access\":\"Public\",\"access\":\"Private\",")
                ] do
                testCase $"A v3 document with {name} is refused, not read with one of its values"
                <| fun () ->
                    let original = catalogText "pairs/v3-daily-rate.json"
                    let json = edit original
                    Expect.notEqual json original "The vector gains a repeated member"

                    match IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                    | Ok _ -> failtest "A repeated member was read"
                    | Result.Error error -> Expect.equal error.Code "duplicate_member" error.Message

            for name, edit, location in
                [
                    "a repeated root distribution",
                    (fun (text: string) ->
                        let json = text.TrimEnd()
                        let at = json.IndexOf ",\"distribution\":"
                        json.Substring(0, json.Length - 1) + json.Substring(at, json.Length - 1 - at) + "}"),
                    "$.distribution"
                    "a nested member repeated by an escaped key",
                    (fun (json: string) ->
                        json.Replace(
                            "\"inputTypes\":{\"x\":\"morphir/SDK:basics#int\"}",
                            "\"inputTypes\":{\"x\":\"morphir/SDK:basics#int\",\"\\u0078\":\"morphir/SDK:basics#int\"}"
                        )),
                    "$.distribution.Application.dependencies[\"my-org/shared\"].modules.util.Public.values.identity.Public.ExpressionBody.inputTypes.x"
                ] do
                testCase $"A v4 document with {name} is refused at the repeated member"
                <| fun () ->
                    let original = catalogText "pairs/v4-application.json"
                    let json = edit original
                    Expect.notEqual json original "The vector gains a repeated member"

                    match IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                    | Ok _ -> failtest "A repeated member was read"
                    | Result.Error error ->
                        Expect.equal error.Code "duplicate_member" error.Message
                        Expect.equal error.Location (Some location) "The location of the second member"

            for name, json, location in
                [
                    "a repeated root key distribution.foo",
                    "{\"formatVersion\":4,\"distribution.foo\":1,\"distribution.foo\":2}",
                    "$[\"distribution.foo\"]"
                    "a repeated key foo inside distribution",
                    "{\"formatVersion\":4,\"distribution\":{\"foo\":1,\"foo\":2}}",
                    "$.distribution.foo"
                    "a repeated key with a quote and a backslash",
                    "{\"formatVersion\":4,\"a\\\"b\\\\c\":1,\"a\\\"b\\\\c\":2}",
                    "$[\"a\\\"b\\\\c\"]"
                    "a repeated key a-b, which RFC 9535 shorthand does not allow",
                    "{\"formatVersion\":4,\"a-b\":1,\"a-b\":2}",
                    "$[\"a-b\"]"
                    "a repeated key 123, which starts with a digit",
                    "{\"formatVersion\":4,\"123\":1,\"123\":2}",
                    "$[\"123\"]"
                    "a repeated key _a1, which RFC 9535 shorthand allows",
                    "{\"formatVersion\":4,\"_a1\":1,\"_a1\":2}",
                    "$._a1"
                ] do
                testCase $"The location of {name} names its key without ambiguity"
                <| fun () ->
                    match IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                    | Ok _ -> failtest "A repeated member was read"
                    | Result.Error error ->
                        Expect.equal error.Code "duplicate_member" error.Message
                        Expect.equal error.Location (Some location) "The location"

            for name, key, location in
                [
                    "a-b", "a-b", "$[\"a-b\"]"
                    "123", "123", "$[\"123\"]"
                    "a key with a quote", "a\\\"b", "$[\"a\\\"b\"]"
                ] do
                testCase $"An unknown v4 root key {name} is located by an RFC 9535 member"
                <| fun () ->
                    let original = catalogText "pairs/v4-empty-library.json"
                    let json = original.Replace("{\"formatVersion\":4,", $"{{\"formatVersion\":4,\"{key}\":1,")

                    match IonIrWriting.TryWriteJson(ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                    | true, _, _ -> failtest "An unknown member was written"
                    | false, _, failure ->
                        Expect.equal failure.Code "unknown_member" (failure.ToString())
                        Expect.equal failure.Location location "The location"

                testCase $"An unknown v3 root key {name} is located by an RFC 9535 member"
                <| fun () ->
                    let original = catalogText "pairs/v3-daily-rate.json"
                    let json = original.Replace("{\"formatVersion\":3,", $"{{\"formatVersion\":3,\"{key}\":1,")

                    match IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                    | Ok _ -> failtest "An unknown member was read"
                    | Result.Error error ->
                        Expect.equal error.Code "unknown_member" (error.ToString())
                        Expect.equal error.Location (Some location) "The location"

            for name, version in [ "Carries", 0; "Release", 1; "JsonWriterVersion", 2 ] do
                testCase $"IonIrWriting.{name} refuses a null version as an argument error"
                <| fun () ->
                    let none = Unchecked.defaultof<Morphir.IR.Versioning.FormatVersion>

                    Expect.throwsT<ArgumentNullException>
                        (fun () ->
                            match version with
                            | 0 -> IonIrWriting.Carries none |> ignore
                            | 1 -> IonIrWriting.Release none |> ignore
                            | _ -> IonIrWriting.JsonWriterVersion none |> ignore
                        )
                        "A null version is a caller error"

            testCase "IonIrReading.TryRead refuses null limits as an argument error"
            <| fun () ->
                let bytes = IonIrWriter.write Cases.emptyV3Library |> ok "Writing"

                Expect.throwsT<ArgumentNullException>
                    (fun () -> IonIrReading.TryRead(Unchecked.defaultof<IonReadLimits>, ReadOnlyMemory bytes) |> ignore)
                    "Null limits are a caller error, not a decode failure"

            testCase "IonReadLimits writes its three limits as text"
            <| fun () ->
                let limits: IonReadLimits = { MaxDecodedBytes = 1024; MaxDepth = 8; MaxNodes = 100 }
                Expect.equal (limits.ToString()) "1024 bytes, depth 8, 100 nodes" "ToString"

            testCase "IonIrReading.TryRead gives the document, or the failure with text locations"
            <| fun () ->
                let bytes = IonIrWriter.write Cases.emptyV3Library |> ok "Writing"

                match IonIrReading.TryRead(ReadOnlyMemory bytes) with
                | true, document, _ -> Expect.equal document.FormatVersion "3.0.0" "The document"
                | false, _, failure -> failtestf "%O" failure

                match IonIrReading.TryRead(ReadOnlyMemory(Encoding.UTF8.GetBytes "morphir::{")) with
                | true, _, _ -> failtest "Not Ion"
                | false, _, failure -> Expect.equal failure.Code "malformed_ion" (failure.ToString())

            testCase "A repeated member deep in nested arrays is found without a path string for every node"
            <| fun () ->
                let depth = 8000
                let json = "{\"formatVersion\":4,\"distribution\":" + String('[', depth) + "{\"a\":1,\"a\":2}" + String(']', depth) + "}"
                let bytes = ReadOnlyMemory(Encoding.UTF8.GetBytes json)
                IonIrWriter.readJson bytes |> ignore
                // The scan runs on this thread, and other tests run beside it, so count this thread's allocations only.
                let before = GC.GetAllocatedBytesForCurrentThread()
                let result = IonIrWriter.readJson bytes
                let allocated = GC.GetAllocatedBytesForCurrentThread() - before

                match result with
                | Ok _ -> failtest "A repeated member was read"
                | Result.Error error ->
                    Expect.equal error.Code "duplicate_member" error.Message
                    Expect.equal error.Location (Some("$.distribution" + String.replicate depth "[0]" + ".a")) "The full path, made once"

                // A path string for each of the 8,000 levels would allocate about 190 MB; the parse and the scan need a few.
                Expect.isLessThan allocated (24L * 1024L * 1024L) $"{allocated} bytes allocated"

            testCase "A root key with a lone surrogate does not hide a formatVersion refusal"
            <| fun () ->
                let json = "{\"formatVersion\":\"3.0.\\uD800\",\"\\uD800\":null,\"distribution\":null}"

                match IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                | Ok _ -> failtest "Read"
                | Result.Error error ->
                    Expect.equal error.Code "invalid_format_version_syntax" error.Message
                    Expect.equal error.Location (Some "$.formatVersion") "The location"

            testCase "The C# failure gives text locations and a readable ToString"
            <| fun () ->
                match IonIrWriting.TryWriteJson(ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"formatVersion\":\"3.0.1\",\"distribution\":null}")) with
                | true, _, _ -> failtest "Written"
                | false, _, failure ->
                    let location: string | null = failure.Location
                    let path: string | null = failure.Path
                    Expect.equal location "$.formatVersion" "A string location"
                    Expect.isNull path "No Ion path"
                    Expect.equal (failure.ToString()) "unsupported_format_version_minor at $.formatVersion: The Ion IR format carries formatVersion 3.0.0, 3.1.0 and 4.0.0, not 3.0.1." "ToString"

            testCase "A document with no formatVersion is refused"
            <| fun () ->
                match IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"distribution\":null}")) with
                | Ok _ -> failtest "Read"
                | Result.Error error -> Expect.equal error.Code "missing_format_version" error.Message

            testCase "TryWriteJson gives the failure of JSON that is not IR"
            <| fun () ->
                match IonIrWriting.TryWriteJson(ReadOnlyMemory(Encoding.UTF8.GetBytes "{")) with
                | true, _, _ -> failtest "Not JSON"
                | false, _, failure -> Expect.equal failure.Code "invalid_json" "The code"

            testCase "TryWriteJsonTree writes the files that writeTreeFiles gives"
            <| fun () ->
                let json = ReadOnlyMemory(catalogBytes "pairs/v4-application.json")

                match IonIrWriting.TryWriteJsonTree(json, 4000) with
                | true, files, _ ->
                    let expected = fromJson "pairs/v4-application.json" |> IonIrWriter.writeTreeFiles 4000 |> ok "Writing"
                    Expect.sequenceEqual (files |> Seq.map (fun pair -> pair.Key, text pair.Value)) (written expected) "The same files"
                | false, _, failure -> failtestf "%s" failure.Message

            testCase "TryWriteJsonTree with a stated release returns a VFileTree stating that release"
            <| fun () ->
                let json = ReadOnlyMemory(catalogBytes "pairs/v3-daily-rate.json")

                match IonIrWriting.TryWriteJsonTree(json, version31 (), 4000) with
                | true, tree, _ ->
                    let expected = IonIrWriter.ofJsonTreeAs (version31 ()) 4000 json |> ok "Tree"
                    let actualFiles =
                        VFileTree.toFileMap tree
                        |> Map.toList
                        |> List.map (fun (loc, file) -> loc, VFile.readText file)

                    let expectedFiles =
                        VFileTree.toFileMap expected
                        |> Map.toList
                        |> List.map (fun (loc, file) -> loc, VFile.readText file)
                    Expect.equal actualFiles expectedFiles "The same Ion files"

                    match IonIrTree.read limits tree with
                    | Ok(ClassicIr(ver, _)) -> Expect.equal ver "3.1.0" "Stated release in Ion tree"
                    | other -> failtestf "Expected ClassicIr 3.1.0, got %A" other
                | false, _, failure -> failtestf "%s" failure.Message

            testCase "TryWriteJsonTree with a null formatVersion throws ArgumentNullException"
            <| fun () ->
                let json = ReadOnlyMemory(catalogBytes "pairs/v3-daily-rate.json")
                Expect.throwsT<ArgumentNullException>
                    (fun () ->
                        IonIrWriting.TryWriteJsonTree(json, Unchecked.defaultof<Morphir.IR.Versioning.FormatVersion>, 4000)
                        |> ignore)
                    "Null formatVersion"
        ]

        testList "Lexemes and boundaries" [
            let readOne (text: string) =
                match IonReader.read limits (ReadOnlyMemory(Encoding.UTF8.GetBytes text)) with
                | Ok [ value ] -> value
                | other -> failtestf "Expected one Ion value: %A" other

            for lexeme, spelling in
                [
                    "9223372036854775807", "9223372036854775807"
                    "-9223372036854775808", "-9223372036854775808"
                    "9223372036854775808", "number::\"9223372036854775808\""
                    "-9223372036854775809", "number::\"-9223372036854775809\""
                    "0.10", "0.10"
                    "-0.0", "-0.0"
                    "12.50", "12.50"
                    "0.000001", "0.000001"
                    "5e-7", "5.d-7"
                    "1e-1000", "1.d-1000"
                    "1.5e-999", "1.5d-999"
                    "1e-1001", "number::\"1e-1001\""
                    "1.25e-999", "number::\"1.25e-999\""
                    "1e+1001", "1.d+1001"
                    "1.5e-7", "1.5d-7"
                    "1e+2", "1.d+2"
                    "1e+5", "1.d+5"
                    "0.0000001", "number::\"0.0000001\""
                    "1.5e+2", "number::\"1.5e+2\""
                    "1e2", "number::\"1e2\""
                    "1E-7", "number::\"1E-7\""
                ] do
                testCase $"The document number {lexeme} is {spelling} and reads back as its lexeme"
                <| fun () ->
                    let number = Morphir.IR.V4.DocumentNumber lexeme
                    let text = IonTextWriter.render [ V4IonWriter.document number ]
                    Expect.equal text (spelling + "\n") "The canonical spelling"
                    Expect.equal (V4IonTypes.documentValue IonPath.Root (readOne text)) number "The lexeme reads back"

            let bound = Numerics.BigInteger.Pow(Numerics.BigInteger 2, 127)

            for number, spelling in
                [
                    bound - Numerics.BigInteger.One, (bound - Numerics.BigInteger.One).ToString()
                    -bound, (-bound).ToString()
                    bound, $"(\n  int\n  \"{bound}\"\n)"
                    -bound - Numerics.BigInteger.One, $"(\n  int\n  \"{-bound - Numerics.BigInteger.One}\"\n)"
                ] do
                testCase $"The integer literal {number} is written within the 128-bit Ion int, or as its digits"
                <| fun () ->
                    let literal = Morphir.IR.V4.Value.Literal(Morphir.IR.V4.ValueAttributes.empty, Morphir.IR.V4.IntegerLiteral number)
                    let text = IonTextWriter.render [ V4IonWriter.value literal ]
                    Expect.equal text (spelling + "\n") "The canonical spelling"
                    Expect.equal (V4IonValues.value IonPath.Root (readOne text)) literal "It reads back"

            testCase "A document number beyond the reader's decimal scale writes a datagram that IonIr.read reads back"
            <| fun () ->
                let original = catalogText "pairs/v4-library-nodes.json"
                Expect.stringContains original "\"ratio\":0.10" "The vector has a document number"
                let document = readJsonText (original.Replace("\"ratio\":0.10", "\"ratio\":1e-1001"))
                let bytes = IonIrWriter.write document |> ok "Writing"
                Expect.stringContains (text bytes) "ratio: number::\"1e-1001\"," "Text, not a decimal of scale 1001"
                expectSame (readIon bytes) document

            testCase "A float lexeme that does not spell the float's value is refused"
            <| fun () ->
                let value =
                    match Morphir.IR.V4.FloatValue.create 1.0 (Some "2.0") with
                    | Ok value -> value
                    | Result.Error message -> failtest message

                let literal = Morphir.IR.V4.Value.Literal(Morphir.IR.V4.ValueAttributes.empty, Morphir.IR.V4.FloatLiteral value)

                Expect.throwsC
                    (fun () -> V4IonWriter.value literal |> ignore)
                    (fun error ->
                        match error with
                        | IonIrFailure failure -> Expect.equal failure.Code "invalid_literal" failure.Message
                        | other -> failtestf "Expected an Ion IR failure: %s" other.Message
                    )

            testCase "A float lexeme that spells the value is kept"
            <| fun () ->
                let value =
                    match Morphir.IR.V4.FloatValue.create 4.0 (Some "4e+0") with
                    | Ok value -> value
                    | Result.Error message -> failtest message

                let text =
                    IonTextWriter.render [ V4IonWriter.value (Morphir.IR.V4.Value.Literal(Morphir.IR.V4.ValueAttributes.empty, Morphir.IR.V4.FloatLiteral value)) ]

                Expect.equal text "(\n  float\n  \"4e+0\"\n)\n" "The lexeme"

            testCase "A control character without a named escape is written as \\x and reads back"
            <| fun () ->
                let text = IonTextWriter.render [ IonText.Text "a\u001bb\u007f" ]
                Expect.equal text "\"a\\x1bb\\x7f\"\n" "The \\x escapes"

                match (readOne text).Data with
                | IonData.String read -> Expect.equal read "a\u001bb\u007f" "The same text"
                | other -> failtestf "Expected a string: %A" other
        ]

        testList "Format versions" [
            let classic version =
                match fromJson "records/v3-library.json" with
                | ClassicIr(_, distribution) -> ClassicIr(version, distribution)
                | _ -> failtest "The vector is a v3 library"

            let v4 version =
                match fromJson "pairs/v4-empty-library.json" with
                | V4Ir(_, distribution) -> V4Ir(version, distribution)
                | _ -> failtest "The vector is a v4 library"

            let specs version =
                match fromJson "pairs/v3-specs.json" with
                | ClassicIr(_, distribution) -> ClassicIr(version, distribution)
                | _ -> failtest "The vector is a v3 specs distribution"

            for name, document, release in
                [
                    "A v3 library at 3", classic "3", "3.0.0"
                    "A v3 library at 3.0.0", classic "3.0.0", "3.0.0"
                    "A v3 library at 3.1.0", classic "3.1.0", "3.1.0"
                    "A v3 specs distribution at 3.1.0", specs "3.1.0", "3.1.0"
                    "A v4 library at 4", v4 "4", "4.0.0"
                    "A v4 library at 4.0.0", v4 "4.0.0", "4.0.0"
                ] do
                testCase $"{name} states {release}"
                <| fun () ->
                    let written = document |> IonIrWriter.write |> ok release |> text
                    Expect.stringContains written $"  formatVersion: \"{release}\",\n" "The exact release"

            for name, document, code in
                [
                    "3.99.0", classic "3.99.0", "unsupported_format_version_minor"
                    "3.0", classic "3.0", "unsupported_format_version_minor"
                    "4.0.0 on classic IR", classic "4.0.0", "unsupported_format_version_major"
                    "2.0.0", classic "2.0.0", "unsupported_format_version_major"
                    "4.0", v4 "4.0", "unsupported_format_version_minor"
                    "4.1.0-draft.1", v4 "4.1.0-draft.1", "unsupported_format_version_minor"
                    "3.0.0 on v4 IR", v4 "3.0.0", "unsupported_format_version_major"
                    "3.0.0 specs", specs "3.0.0", "specs_before_3_1"
                ] do
                testCase $"{name} is refused with {code}"
                <| fun () ->
                    match IonIrWriter.write document with
                    | Ok _ -> failtest "The version is not carried"
                    | Result.Error error -> Expect.equal error.Code code "The code"

            testCase "The callers' check carries the same versions"
            <| fun () ->
                let parse (text: string) =
                    match Morphir.IR.Versioning.parse text with
                    | Ok version -> version
                    | Result.Error error -> failtestf "%A" error

                for text in [ "3"; "3.0.0"; "3.1.0"; "4"; "4.0.0" ] do
                    Expect.isTrue (IonIrWriting.Carries(parse text)) $"{text} is carried"

                for text in [ "1"; "2"; "3.99.0"; "4.1.0-draft.1"; "5" ] do
                    Expect.isFalse (IonIrWriting.Carries(parse text)) $"{text} is not carried"
        ]

        testList "Documentation" [
            testCase "An absent v3 doc has no Ion spelling apart from the empty one, so it is refused"
            <| fun () ->
                match IonIrWriter.write Cases.undocumentedV3Alias with
                | Ok bytes -> failtestf "The absent doc was written: %s" (text bytes)
                | Result.Error error -> Expect.equal error.Code "unrepresentable_documentation" error.Message

            testCase "The empty v3 doc reads back as the empty doc, and writes the same bytes again"
            <| fun () ->
                let bytes = IonIrWriter.write Cases.emptyDocumentedV3Alias |> ok "Writing"

                match readIon bytes with
                | ClassicIr(_, Morphir.IR.Classic.Distribution.Library(_, _, definition)) ->
                    let entry = definition.Modules |> Map.toList |> List.exactlyOne |> snd
                    let alias = entry.Value.Types |> Map.toList |> List.exactlyOne |> snd

                    match alias.Value with
                    | Morphir.IR.Classic.Documented.WithDocumentation("", _) -> ()
                    | other -> failtestf "Expected the empty doc: %A" other
                | other -> failtestf "Expected a v3 library: %A" other

                Expect.equal (readIon bytes |> IonIrWriter.write |> ok "Writing again" |> text) (text bytes) "A fixed point"
        ]

        testList "Refusals" [
            testCase "IR before 3 has no Ion spelling"
            <| fun () ->
                let document =
                    match fromJson "records/v3-library.json" with
                    | ClassicIr(_, distribution) -> ClassicIr("2.0.0", distribution)
                    | _ -> failtest "The vector is a v3 library"

                match IonIrWriter.write document with
                | Ok _ -> failtest "IR 2 is refused"
                | Result.Error error -> Expect.equal error.Code "unsupported_format_version_major" "The code"

            testCase "A v3 value attribute [] would read back as the unit type, so it is refused"
            <| fun () ->
                let original = catalogText "pairs/v3-daily-rate.json"
                let literal = "\"body\":[\"Literal\",[\"Reference\",{},[[[\"morphir\"],[\"s\",\"d\",\"k\"]],[[\"basics\"]],[\"int\"]],[]],"
                Expect.stringContains original literal "The vector has a typed literal"
                let json = original.Replace(literal, "\"body\":[\"Literal\",[],")

                match IonIrWriter.write (readJsonText json) with
                | Ok bytes -> failtestf "[] was written: %s" (text bytes)
                | Result.Error error -> Expect.equal error.Code "unrepresentable_attribute" "The code"

                // The vector as it stands is written, read and written again to the same bytes.
                let bytes = fromJson "pairs/v3-daily-rate.json" |> IonIrWriter.write |> ok "Writing"
                Expect.equal (readIon bytes |> IonIrWriter.write |> ok "Writing again" |> text) (text bytes) "A fixed point"

            for name, attribute in
                [
                    "an empty variable name", "[\"Variable\",{},[]]"
                    "an empty word", "[\"Variable\",{},[\"\"]]"
                    "a word that is not canonical", "[\"Variable\",{},[\"A\"]]"
                    "an empty package path", "[\"Reference\",{},[[],[[\"basics\"]],[\"int\"]],[]]"
                    "an empty module path", "[\"Reference\",{},[[[\"morphir\"]],[],[\"int\"]],[]]"
                    "an empty record field name", "[\"Record\",{},[{\"name\":[],\"tpe\":[\"Unit\",{}]}]]"
                ] do
                testCase $"A v3 inferred type with {name} is refused before it is written"
                <| fun () ->
                    let original = catalogText "pairs/v3-daily-rate.json"
                    let literal = "\"body\":[\"Literal\",[\"Reference\",{},[[[\"morphir\"],[\"s\",\"d\",\"k\"]],[[\"basics\"]],[\"int\"]],[]],"
                    Expect.stringContains original literal "The vector has a typed literal"
                    let json = original.Replace(literal, $"\"body\":[\"Literal\",{attribute},")

                    match IonIrWriting.TryWriteJson(ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                    | true, ion, _ ->
                        let read =
                            match IonIrReading.TryRead(ReadOnlyMemory ion) with
                            | true, _, _ -> "it reads back"
                            | false, _, failure -> failure.ToString()

                        failtestf "The attribute was written, and %s" read
                    | false, _, failure -> Expect.equal failure.Code "unrepresentable_attribute" (failure.ToString())

            testCase "A v3 attribute that is not a type has no Ion spelling"
            <| fun () ->
                let original = catalogText "pairs/v3-daily-rate.json"
                let literal = "\"body\":[\"Literal\",[\"Reference\",{},[[[\"morphir\"],[\"s\",\"d\",\"k\"]],[[\"basics\"]],[\"int\"]],[]],"
                Expect.stringContains original literal "The vector has a typed literal"

                match IonIrWriter.write (readJsonText (original.Replace(literal, "\"body\":[\"Literal\",{\"x\":1},"))) with
                | Ok _ -> failtest "The attribute is refused"
                | Result.Error error -> Expect.equal error.Code "unrepresentable_attribute" "The code"
        ]
    ]
