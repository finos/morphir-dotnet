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

let private fromJson (name: string) =
    IonIrWriter.readJson (ReadOnlyMemory(Fixtures.bytes name))
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

let private pairNames =
    Fixtures.names
    |> Array.filter (fun name -> name.StartsWith "pairs/" && name.EndsWith ".json" && not (name.Contains "/inputs/"))
    |> Array.map (fun name -> name.Substring("pairs/".Length, name.Length - "pairs/".Length - ".json".Length))
    |> List.ofArray

let private treeFiles (name: string) =
    let prefix = $"trees/{name}/"

    Fixtures.names
    |> Array.filter (fun file -> file.StartsWith prefix)
    |> Array.map (fun file -> file.Substring prefix.Length, Fixtures.text file)
    |> Array.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
    |> List.ofArray

let private written (files: (string * byte array) list) =
    files
    |> List.map (fun (location, bytes) -> location, text bytes)

let private treeOf (files: (string * byte array) list) =
    files
    |> List.map (fun (location, bytes) -> VFile.ofBytes location (ReadOnlyMemory bytes))
    |> VFileTree.fromFiles

let private v4Library (package: string) (modules: string) =
    $"{{\"formatVersion\":4,\"distribution\":{{\"Library\":{{\"packageName\":\"{package}\",\"dependencies\":{{}},\"def\":{{\"modules\":{{{modules}}}}}}}}}}}"

let private readV4Json (json: string) =
    IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json))
    |> ok "Reading the JSON"

[<Tests>]
let tests =
    testList "Ion IR writer" [
        testList "Canonical text" [
            testCase "The empty v3 library is the draft's example, byte for byte"
            <| fun () ->
                let document = fromJson "records/v3-library.json"

                let empty =
                    match document with
                    | ClassicIr(version, Morphir.IR.Classic.Distribution.Library(package, _, _)) ->
                        ClassicIr(version, Morphir.IR.Classic.Distribution.Library(package, Map.empty, { Modules = Map.empty }))
                    | _ -> failtest "The vector is a v3 library"

                let expected =
                    "morphir::{\n  ionVersion: \"0.1.0-draft.1\",\n  formatVersion: \"3.0.0\",\n  kind: library,\n  packageName: \"example\",\n}\nmorphir_footer::{\n}\n"

                let actual = IonIrWriter.write empty |> ok "Writing" |> text
                let packageName = Regex.Match(actual, "packageName: \"([^\"]*)\"").Groups[1].Value
                Expect.equal actual (expected.Replace("\"example\"", $"\"{packageName}\"")) "The canonical empty library"

            testCase "An S-expression puts each operand on its own line, with no commas"
            <| fun () ->
                let json =
                    v4Library
                        "p"
                        "\"m\":{\"Public\":{\"types\":{},\"values\":{\"v\":{\"Public\":{\"ExpressionBody\":{\"inputTypes\":{\"a\":\"morphir/SDK:basics#int\",\"b\":\"morphir/SDK:basics#int\"},\"outputType\":\"morphir/SDK:basics#int\",\"body\":{\"Apply\":{\"function\":{\"Variable\":\"a\"},\"argument\":{\"Variable\":\"b\"}}}}}}}}}"

                let actual = readV4Json json |> IonIrWriter.write |> ok "Writing" |> text

                Expect.stringContains actual "      body: (\n        apply\n        a\n        b\n      ),\n" "(apply a b)"
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
                        let reference = Fixtures.text $"pairs/{name}.ion" |> normalize |> structuredBare
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
                let document = fromJson "trees/inputs/v4-escaped-names.input.json"

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
                let long = String.replicate 30 "word-"
                let name = long + "end"

                let json =
                    v4Library
                        "p"
                        $"\"m\":{{\"Public\":{{\"types\":{{\"{name}\":{{\"Public\":{{\"TypeAliasDefinition\":{{\"typeParams\":[],\"typeExp\":\"morphir/SDK:basics#int\"}}}}}}}},\"values\":{{}}}}}}"

                let document = readV4Json json

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

            testCase "A module path over the budget is refused"
            <| fun () ->
                let segment = String.replicate 12 "segment-" + "end"
                let json = v4Library "p" $"\"{segment}\":{{\"Public\":{{\"types\":{{}},\"values\":{{}}}}}}"

                match IonIrWriter.writeTreeFiles 64 (readV4Json json) with
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
                let json = ReadOnlyMemory(Fixtures.bytes "pairs/v4-specs.json")

                match IonIrWriting.TryWriteJson json with
                | true, ion, _ -> Expect.equal (text ion) (fromJson "pairs/v4-specs.json" |> IonIrWriter.write |> ok "Writing" |> text) "The same bytes"
                | false, _, failure -> failtestf "%s" failure.Message

            testCase "TryWriteJson gives the failure of JSON that is not IR"
            <| fun () ->
                match IonIrWriting.TryWriteJson(ReadOnlyMemory(Encoding.UTF8.GetBytes "{")) with
                | true, _, _ -> failtest "Not JSON"
                | false, _, failure -> Expect.equal failure.Code "invalid_json" "The code"

            testCase "TryWriteJsonTree writes the files that writeTreeFiles gives"
            <| fun () ->
                let json = ReadOnlyMemory(Fixtures.bytes "pairs/v4-application.json")

                match IonIrWriting.TryWriteJsonTree(json, 4000) with
                | true, files, _ ->
                    let expected = fromJson "pairs/v4-application.json" |> IonIrWriter.writeTreeFiles 4000 |> ok "Writing"
                    Expect.sequenceEqual (files |> Seq.map (fun pair -> pair.Key, text pair.Value)) (written expected) "The same files"
                | false, _, failure -> failtestf "%s" failure.Message
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

            testCase "A v3 attribute that is not a type has no Ion spelling"
            <| fun () ->
                let json =
                    "{\"formatVersion\":3,\"distribution\":[\"Library\",[[\"p\"]],[],{\"modules\":[[[[\"m\"]],{\"access\":\"Public\",\"value\":{\"types\":[],\"values\":[[[\"v\"],{\"access\":\"Public\",\"value\":{\"doc\":\"\",\"value\":{\"inputTypes\":[],\"outputType\":[\"Unit\",{}],\"body\":[\"Unit\",{\"x\":1}]}}}]],\"doc\":null}}]]}]}"

                match IonIrWriter.write (readV4Json json) with
                | Ok _ -> failtest "The attribute is refused"
                | Result.Error error -> Expect.equal error.Code "unrepresentable_attribute" "The code"
        ]
    ]
