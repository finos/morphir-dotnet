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
    // The reference CLI writes a document number with a positive exponent as text. The draft makes it an Ion decimal
    // whenever the reader's lexeme rule gives the lexeme back, which it does for 1e+2 and 1e+5.
    |> fun text -> text.Replace("number::\"1e+2\"", "1.d+2").Replace("number::\"1e+5\"", "1.d+5")

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

let private parseVersion (text: string) =
    match Morphir.IR.Versioning.parse text with
    | Ok version -> version
    | Result.Error error -> failwithf "%A" error

let private version31 () = parseVersion "3.1.0"

let private readV4Json (json: string) =
    IonIrWriter.readJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json))
    |> ok "Reading the JSON"

/// Named cases: a model the test builds by intent, and the reference bytes the vendored draft states for it.
module private Cases =
    open Morphir.IR

    /// The draft's empty v3 library: package example, no dependencies and no modules.
    let emptyV3Library =
        ClassicIr(
            "3.0.0",
            Morphir.IR.Classic.Distribution.Library(PackageName(Path [ Name [ "example" ] ]), Map.empty, { Modules = Map.empty })
        )

    /// The fence after "the canonical empty v3 library is:" in the vendored draft, ion-draft.md.
    let draftEmptyV3Library =
        let draft = Fixtures.text "ion-draft.md"
        let fence = Regex.Match(draft, "the canonical empty v3 library is:\\s*```ion\\n(.*?)```", RegexOptions.Singleline)

        if not fence.Success then
            failwith "The vendored draft has no canonical empty v3 library."

        fence.Groups[1].Value

    /// Two type names whose cut stems in a budget of 64 have one prefix and one digest, 905bb72b.
    let collidingTypes =
        "commonprefixcommonprefixcommonprefixcommon-n58445", "commonprefixcommonprefixcommonprefixcommon-n91859"

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

            testCase "Two cut stems that share a prefix and a digest are refused, not written to one path"
            <| fun () ->
                let first, second = Cases.collidingTypes

                let alias name =
                    $"\"{name}\":{{\"Public\":{{\"TypeAliasDefinition\":{{\"typeParams\":[],\"typeExp\":\"morphir/SDK:basics#int\"}}}}}}"

                let json = v4Library "p" $"\"m\":{{\"Public\":{{\"types\":{{{alias first},{alias second}}},\"values\":{{}}}}}}"

                match IonIrWriter.writeTreeFiles 64 (readV4Json json) with
                | Ok files -> failtestf "The tree has one file for two types: %A" (List.map fst files)
                | Result.Error error ->
                    Expect.equal error.Code "stem_collision" error.Message
                    Expect.stringContains error.Message "__905bb72b.type.ion" "The message names the path"

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

            testCase "TryWriteJson keeps the format version that the JSON states"
            <| fun () ->
                let json (version: string) =
                    ReadOnlyMemory(Encoding.UTF8.GetBytes((Fixtures.text "pairs/v3-daily-rate.json").Replace("{\"formatVersion\":3,", $"{{\"formatVersion\":{version},")))

                for version, release in [ "3", "3.0.0"; "\"3.0.0\"", "3.0.0"; "\"3.1.0\"", "3.1.0" ] do
                    match IonIrWriting.TryWriteJson(json version) with
                    | true, ion, _ -> Expect.stringContains (text ion) $"  formatVersion: \"{release}\",\n" $"{version} states {release}"
                    | false, _, failure -> failtestf "%s: %s %s" version failure.Code failure.Message

            for version in [ "3.0.1"; "3.1.1" ] do
                testCase $"TryWriteJson refuses formatVersion {version} instead of relabelling it"
                <| fun () ->
                    let json = (Fixtures.text "pairs/v3-daily-rate.json").Replace("{\"formatVersion\":3,", $"{{\"formatVersion\":\"{version}\",")

                    match IonIrWriting.TryWriteJson(ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                    | true, ion, _ -> failtestf "%s was written as: %s" version ((text ion).Split('\n')[2])
                    | false, _, failure -> Expect.equal failure.Code "unsupported_format_version_minor" failure.Message

                testCase $"readJson refuses formatVersion {version}, so no release can relabel it"
                <| fun () ->
                    let json =
                        ReadOnlyMemory(
                            Encoding.UTF8.GetBytes(
                                (Fixtures.text "pairs/v3-daily-rate.json").Replace("{\"formatVersion\":3,", $"{{\"formatVersion\":\"{version}\",")
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
                let json = ReadOnlyMemory(Fixtures.bytes "pairs/v3-daily-rate.json")

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
                        IonIrWriting.TryWriteJson(ReadOnlyMemory(Fixtures.bytes "pairs/v3-daily-rate.json"), Unchecked.defaultof<Morphir.IR.Versioning.FormatVersion>)
                        |> ignore
                    )
                    "A null version is a caller error, not a decode failure"

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

        testList "Lexemes and boundaries" [
            let readOne (text: string) =
                match IonReader.read limits (Fixtures.utf8 text) with
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
                let original = Fixtures.text "pairs/v3-daily-rate.json"
                let literal = "\"body\":[\"Literal\",[\"Reference\",{},[[[\"morphir\"],[\"s\",\"d\",\"k\"]],[[\"basics\"]],[\"int\"]],[]],"
                Expect.stringContains original literal "The vector has a typed literal"
                let json = original.Replace(literal, "\"body\":[\"Literal\",[],")

                match IonIrWriter.write (readV4Json json) with
                | Ok bytes -> failtestf "[] was written: %s" (text bytes)
                | Result.Error error -> Expect.equal error.Code "unrepresentable_attribute" "The code"

                // The vector as it stands is written, read and written again to the same bytes.
                let bytes = fromJson "pairs/v3-daily-rate.json" |> IonIrWriter.write |> ok "Writing"
                Expect.equal (readIon bytes |> IonIrWriter.write |> ok "Writing again" |> text) (text bytes) "A fixed point"

            testCase "A v3 attribute that is not a type has no Ion spelling"
            <| fun () ->
                let json =
                    "{\"formatVersion\":3,\"distribution\":[\"Library\",[[\"p\"]],[],{\"modules\":[[[[\"m\"]],{\"access\":\"Public\",\"value\":{\"types\":[],\"values\":[[[\"v\"],{\"access\":\"Public\",\"value\":{\"doc\":\"\",\"value\":{\"inputTypes\":[],\"outputType\":[\"Unit\",{}],\"body\":[\"Unit\",{\"x\":1}]}}}]],\"doc\":null}}]]}]}"

                match IonIrWriter.write (readV4Json json) with
                | Ok _ -> failtest "The attribute is refused"
                | Result.Error error -> Expect.equal error.Code "unrepresentable_attribute" "The code"
        ]
    ]
