/// The canonical YAML writer. The vendored kit states the canonical YAML and the canonical JSON of each case, so the
/// kit decides the bytes; the profile's reader, TreeYaml, decides that the text means the JSON it came from.
module Morphir.IR.Tests.Trees.CanonicalYamlTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open Morphir.IR.Trees
open Morphir.TestSupport.Fixtures

let private jsonOptions = JsonDocumentOptions(MaxDepth = 4096)

let private element (json: string) =
    use document = JsonDocument.Parse(json, jsonOptions)
    document.RootElement.Clone()

let private yaml (json: string) = CanonicalYaml.write (element json)

/// A JSON value as text that keeps member order and number lexemes, so two values compare as the profile does.
let rec private exact (value: JsonElement) : string =
    match value.ValueKind with
    | JsonValueKind.Object ->
        value.EnumerateObject()
        |> Seq.map (fun property -> JsonSerializer.Serialize property.Name + ":" + exact property.Value)
        |> String.concat ","
        |> sprintf "{%s}"
    | JsonValueKind.Array ->
        value.EnumerateArray()
        |> Seq.map exact
        |> String.concat ","
        |> sprintf "[%s]"
    | JsonValueKind.String -> JsonSerializer.Serialize(value.GetString())
    | _ -> value.GetRawText()

/// Reads canonical YAML with the profile's reader. Its root must be a mapping.
let private readBack (text: string) =
    match TreeYaml.read 4096 "document.yaml" (Encoding.UTF8.GetBytes text) with
    | Ok root -> root
    | Error errors -> failtestf "The reader refused the canonical YAML: %A\n%s" errors text

/// The kit cases that state both a canonical YAML and a canonical JSON spelling, as the catalog serves them.
let private kitPairs = FixtureCatalog.Kit.CanonicalSpellings |> List.ofSeq

/// The migrated IR JSON pairs, by their file name.
let private pairFiles =
    FixtureCatalog.UpstreamIr.Documents
    |> Seq.filter (fun entry -> FixtureCatalog.UpstreamIr.NameOf(entry).StartsWith "pairs/")
    |> Seq.map (fun entry -> Path.GetFileName entry.RelativePath, FixtureCatalog.ReadText entry)
    |> List.ofSeq

[<Tests>]
let tests =
    testList "Canonical YAML" [
        testList "Kit spellings" [
            test "The kit states canonical YAML for many cases" {
                Expect.isGreaterThan kitPairs.Length 40 "The parser finds the kit's YAML and JSON pairs"
            }

            for pair in kitPairs do
                test pair.Name {
                    Expect.equal (yaml pair.Json) (pair.Yaml + "\n") "The canonical YAML of the canonical JSON"
                }
        ]

        testList "Rule 1: the root" [
            test "A scalar root is the scalar and a line feed" {
                Expect.equal (yaml "\"value-in-USD\"") "value-in-USD\n" "A plain string"
                Expect.equal (yaml "4") "4\n" "A number"
                Expect.equal (yaml "\"4\"") "\"4\"\n" "A string that reads as a number"
            }

            test "A sequence root follows rule 3" {
                Expect.equal (yaml "[1, \"a\"]") "[1, a]\n" "A flow sequence"
                Expect.equal (yaml "[{\"a\": 1}, 2]") "- a: 1\n- 2\n" "A block sequence"
                Expect.equal (yaml "[]") "[]\n" "An empty sequence"
            }

            test "A mapping root is a block mapping" {
                Expect.equal (yaml "{\"a\": {\"b\": 1}}") "a:\n  b: 1\n" "Two-space indentation"
                Expect.equal (yaml "{}") "{}\n" "An empty mapping"
            }
        ]

        testList "Rule 2: mappings" [
            test "Members keep their order, and empty containers are inline" {
                Expect.equal
                    (yaml "{\"z\": 1, \"a\": {}, \"m\": [], \"b\": {\"c\": [1]}}")
                    "z: 1\na: {}\nm: []\nb:\n  c: [1]\n"
                    "The writer's order"
            }

            test "A key is a scalar under rule 4" {
                Expect.equal (yaml "{\"$meta\": 1}") "$meta: 1\n" "A key such as $meta is plain"
                Expect.equal (yaml "{\"true\": 1, \"a: b\": 2}") "\"true\": 1\n\"a: b\": 2\n" "Keys that need quotes"
            }
        ]

        testList "Rule 3: sequences" [
            test "A sequence with no mapping at any depth is flow" {
                Expect.equal (yaml "{\"just\": [[\"value\", \"a\"]]}") "just: [[value, a]]\n" "Nested flow"
            }

            test "A sequence with a mapping at any depth is block, and a mapping item starts on its line" {
                Expect.equal
                    (yaml "{\"Tuple\": [{\"Variable\": \"x\"}, {\"Literal\": {\"IntegerLiteral\": 1}}]}")
                    "Tuple:\n  - Variable: x\n  - Literal:\n      IntegerLiteral: 1\n"
                    "values-0005"

                Expect.equal (yaml "{\"a\": [[{\"b\": 1}], 2]}") "a:\n  - - b: 1\n  - 2\n" "A nested block sequence"
                Expect.equal (yaml "{\"a\": [{}, []]}") "a:\n  - {}\n  - []\n" "Empty items stay inline"
            }
        ]

        testList "Rule 4: strings" [
            test "An FQName is plain except in a flow sequence" {
                Expect.equal (yaml "{\"a\": \"morphir/SDK:basics#int\"}") "a: morphir/SDK:basics#int\n" "A mapping value"

                Expect.equal
                    (yaml "{\"Reference\": [\"morphir/SDK:list#list\", \"a\"]}")
                    "Reference: [\"morphir/SDK:list#list\", a]\n"
                    "A flow item"
            }

            for text in
                [
                    ""
                    "true"; "True"; "TRUE"; "false"; "False"; "FALSE"; "null"; "Null"; "NULL"; "~"
                    "42"; "-7"; "+1"; "1e3"; "1.5"; ".5"; "0x1F"; "0x10"; "0o17"; "0o7"; "-0x1"; ".inf"; "-.inf"; ".nan"; ".NaN"
                    "- a"; "?"; ":a"; ",a"; "[a"; "]a"; "{a"; "}a"; "#a"; "&a"; "*a"; "!a"; "|a"; ">a"; "'a"; "\"a"; "%a"; "@a"; "`a"
                    " a"; "a "; "a:"; "a: b"; "a #b"; "a\nb"; "a\tb"; "a\u0001b"; "..."; "... x"; "---"; "--- x"
                    "a\u2028b"; "a\u2029b"; "a\u0085b"; "\uFEFFa"
                ] do
                test $"{JsonSerializer.Serialize text} is double-quoted" {
                    let json = JsonSerializer.Serialize(dict [ "k", text ])
                    let written = yaml json
                    Expect.stringStarts written "k: \"" "Quoted"
                    Expect.equal (exact (readBack written)) (exact (element json)) "It reads back as the same string"
                }

            for text in [ "a"; "a:b"; "a#b"; "0xylophone"; "0o8"; "0x"; "0b101"; "...suffix"; "a..."; "a---"; "morphir/SDK"; "value-in-USD"; "yes"; "2026-01-15"; "é😀"; "a b" ] do
                test $"{JsonSerializer.Serialize text} is plain" {
                    let json = JsonSerializer.Serialize(dict [ "k", text ])
                    let written = yaml json
                    Expect.equal written $"k: {text}\n" "Plain"
                    Expect.equal (exact (readBack written)) (exact (element json)) "It reads back as the same string"
                }

            test "Line and paragraph separators are escaped, so the profile's reader takes them" {
                Expect.equal (yaml "{\"k\": \"a\\u2028b\\u2029c\"}") "k: \"a\\u2028b\\u2029c\"\n" "JSON escapes"
            }

            test "A whole surrogate pair stays UTF-8, and half of one is refused" {
                match CanonicalYaml.ofJson (ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"k\": \"a\\ud800b\"}")) with
                | Ok bytes -> failtestf "Half a pair was written: %s" (Encoding.UTF8.GetString bytes)
                | Error message -> Expect.isNotEmpty message "The reason"

                Expect.equal (yaml "{\"k\": \"\\ud83d\\ude00\"}") "k: \U0001F600\n" "A whole pair"
            }

            test "Inside a flow sequence, flow indicators force quotes" {
                Expect.equal
                    (yaml "{\"a\": [\"x,y\", \"x:y\", \"x#y\", \"[x\", \"x}\", \"plain\"]}")
                    "a: [\"x,y\", \"x:y\", \"x#y\", \"[x\", \"x}\", plain]\n"
                    "Flow restrictions"
            }

            test "A multi-line string is double-quoted with JSON escapes, never a block scalar" {
                Expect.equal (yaml "{\"doc\": \"one\\ntwo \\\"q\\\" \\\\\"}") "doc: \"one\\ntwo \\\"q\\\" \\\\\"\n" "Escapes"
            }
        ]

        testList "Rule 5: numbers, booleans and null" [
            test "Numbers keep their lexeme" {
                let json = "{\"a\": 1.50, \"b\": 1e3, \"c\": -0, \"d\": 123456789012345678901234567890, \"e\": 1.5E+2}"
                let written = yaml json
                Expect.equal written "a: 1.50\nb: 1e3\nc: -0\nd: 123456789012345678901234567890\ne: 1.5E+2\n" "The lexemes"
                Expect.equal (exact (readBack written)) (exact (element json)) "The reader keeps them"
            }

            test "Booleans and null are true, false and null" {
                Expect.equal (yaml "{\"a\": true, \"b\": false, \"c\": null}") "a: true\nb: false\nc: null\n" "Keywords"
            }
        ]

        testList "Rule 6: output" [
            test "The text ends with one line feed and has no markers, tags, anchors or comments" {
                for _, json in pairFiles do
                    let written = yaml json
                    Expect.stringEnds written "\n" "A line feed"
                    Expect.isFalse (written.EndsWith "\n\n") "Only one"
                    Expect.isFalse (written.StartsWith "---" || written.Contains "\n---" || written.Contains "\n...") "No markers"
                    Expect.isFalse (Regex.IsMatch(written, @"(^|[ \[,])[!&*]\w")) "No tags, anchors or aliases"
            }
        ]

        testList "Round trip" [
            for name, text in pairFiles do
                test $"{name} reads back as its JSON" {
                    let json = element text
                    let written = CanonicalYaml.write json
                    Expect.equal (exact (readBack written)) (exact json) "TreeYaml reads the same value"
                }

            for pair in kitPairs do
                if pair.Json.TrimStart().StartsWith "{" then
                    test $"{pair.Name} reads back as its JSON" {
                        Expect.equal (exact (readBack (yaml pair.Json))) (exact (element pair.Json)) "The same value"
                    }
        ]

        testList "Keys and scalars beyond rule 4" [
            let roundTrip (json: string) =
                let written = yaml json
                Expect.equal (exact (readBack written)) (exact (element json)) "The profile's reader gives the value back"
                written

            test "A key of 1,024 characters stays an implicit key" {
                let key = String('k', 1024)
                let written = roundTrip $"{{\"{key}\": 1}}"
                Expect.equal written $"{key}: 1\n" "Implicit"
            }

            test "A plain key of 1,025 characters is an explicit key" {
                let key = String('k', 1025)
                let written = roundTrip $"{{\"{key}\": 1, \"b\": {{\"c\": 2}}}}"
                Expect.equal written $"? {key}\n: 1\nb:\n  c: 2\n" "Explicit, then implicit"
            }

            test "A key that escapes expand past 1,024 characters is an explicit key" {
                let json = "{\"" + String.replicate 200 "\\u0001" + "\": {\"c\": [{\"d\": 1}]}}"
                let written = roundTrip json
                Expect.stringStarts written "? \"\\u0001" "A quoted explicit key"
                Expect.stringContains written "\n:\n  c:\n    - d: 1\n" "Its value is a block on the next lines"
            }

            test "An explicit key inside a sequence item starts on its - line" {
                let key = String('k', 1025)
                let written = roundTrip $"{{\"a\": [{{\"{key}\": 1, \"z\": 2}}]}}"
                Expect.equal written $"a:\n  - ? {key}\n    : 1\n    z: 2\n" "Explicit in a block sequence"
            }

            test "U+FFFE and U+FFFF are escaped, and read back" {
                let json = "{\"k\": \"a\\ufffeb\\uffffc\"}"
                let written = roundTrip json
                Expect.equal written "k: \"a\\ufffeb\\uffffc\"\n" "JSON escapes"
            }

            test "A mapping with a repeated member has no spelling in the profile, so the write fails" {
                match CanonicalYaml.ofJson (ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"a\": {\"x\": 1, \"x\": 2}}")) with
                | Ok bytes -> failtestf "The repeated member was written: %s" (Encoding.UTF8.GetString bytes)
                | Error message -> Expect.stringContains message "x" "The reason names the member"

                Expect.throwsT<ArgumentException> (fun () -> yaml "[{\"y\": 1, \"y\": 1}]" |> ignore) "write raises"
            }

            test "The same name in two mappings is not a repeat" {
                roundTrip "{\"a\": {\"x\": 1}, \"b\": {\"x\": 2}}" |> ignore
            }
        ]

        testList "Refusals and limits" [
            test "A merge key has no spelling in the profile, so the write fails" {
                match CanonicalYaml.ofJson (ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"a\": {\"<<\": 1}}")) with
                | Ok bytes -> failtestf "The merge key was written: %s" (Encoding.UTF8.GetString bytes)
                | Error message -> Expect.stringContains message "<<" "The reason names the key"

                Expect.throwsT<ArgumentException> (fun () -> yaml "{\"<<\": 1}" |> ignore) "write raises"
            }

            test "The profile's reader refuses a quoted merge key, which is why the writer does" {
                match TreeYaml.read 32 "document.yaml" (Encoding.UTF8.GetBytes "\"<<\": 1\n") with
                | Ok _ -> failtest "The reader takes a quoted merge key"
                | Error errors -> Expect.isNonEmpty errors "Refused"
            }

            test "A deeply nested sequence is written without overflowing the stack" {
                let depth = 300_000
                let json = "{\"a\": " + String('[', depth) + "1" + String(']', depth) + "}"

                match CanonicalYaml.ofJson (ReadOnlyMemory(Encoding.UTF8.GetBytes json)) with
                | Ok bytes ->
                    let text = Encoding.UTF8.GetString bytes
                    Expect.equal text ("a: " + String('[', depth) + "1" + String(']', depth) + "\n") "One flow sequence"
                | Error message -> failtest message
            }
        ]

        testList "Bytes and trees" [
            test "Bytes are UTF-8 with no byte-order mark" {
                match CanonicalYaml.ofJson (ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"a\": \"é\"}")) with
                | Ok bytes -> Expect.sequenceEqual bytes (Encoding.UTF8.GetBytes "a: é\n") "The bytes"
                | Error message -> failtest message
            }

            test "Text that is not JSON is refused" {
                match CanonicalYamlWriter.TryWriteJson(ReadOnlyMemory(Encoding.UTF8.GetBytes "{")) with
                | true, _, _ -> failtest "Not JSON"
                | false, _, failure -> Expect.isNotEmpty failure "The parser's message"
            }

            test "A JSON tree becomes a YAML tree with the .yaml extension, and each file reads back as its JSON" {
                // The acme/demo tree of the catalog, with each file at its location in the tree.
                let tree = FixtureCatalog.Trees.AcmeDemo |> List.ofSeq
                let locationOf (entry: FixtureEntry) = entry.RelativePath.Substring FixtureCatalog.Trees.AcmeDemoDirectory.Length

                let files =
                    tree |> List.map (fun entry -> locationOf entry, ReadOnlyMemory(FixtureCatalog.ReadBytes entry))

                match CanonicalYaml.ofJsonTree files with
                | Ok written ->
                    Expect.sequenceEqual
                        (written |> List.map fst)
                        (tree |> List.map (locationOf >> fun location -> location.Substring(0, location.Length - ".json".Length) + ".yaml"))
                        "Each file at its YAML location"

                    for (location, yamlBytes), entry in List.zip written tree do
                        Expect.equal
                            (exact (readBack (Encoding.UTF8.GetString yamlBytes)))
                            (exact (element (FixtureCatalog.ReadText entry)))
                            $"{location} reads back as its JSON"
                | Error message -> failtest message
            }

            test "DocumentTreeStore.TryWriteYamlTree converts a JSON VFileTree into a YAML VFileTree preserving declared order" {
                let tree = FixtureCatalog.Trees.AcmeDemo |> List.ofSeq
                let locationOf (entry: FixtureEntry) = entry.RelativePath.Substring FixtureCatalog.Trees.AcmeDemoDirectory.Length

                let files =
                    tree
                    |> List.map (fun entry -> Morphir.Files.VFile.ofBytes (locationOf entry) (ReadOnlyMemory(FixtureCatalog.ReadBytes entry)))

                let declared = tree |> List.map locationOf

                let jsonVTree =
                    files
                    |> List.map (fun file -> file.Path.Value, file)
                    |> Map.ofList
                    |> Morphir.Files.VFileTree.fromFileMap
                    |> Morphir.Files.VFileTree.set Morphir.Files.VFileTree.declaredOrder declared

                let mutable yamlTree = Morphir.Files.VFileTree.empty
                let mutable failure = ""

                if Morphir.IR.Trees.DocumentTreeStore.tryWriteYamlTree(jsonVTree, &yamlTree, &failure) then
                    let expectedOrder = declared |> List.map CanonicalYaml.treeLocation

                    Expect.equal
                        (Morphir.Files.VFileTree.tryGet Morphir.Files.VFileTree.declaredOrder yamlTree)
                        (Some expectedOrder)
                        "Declared order is mapped to YAML paths"

                    let fileMap = Morphir.Files.VFileTree.toFileMap yamlTree

                    for yamlLocation, entry in List.zip expectedOrder tree do
                        let file = Map.find yamlLocation fileMap

                        let yamlText =
                            match Morphir.Files.VFile.readText file with
                            | Ok value -> value
                            | Error err -> failtestf "Read failed: %A" err

                        Expect.equal
                            (exact (readBack yamlText))
                            (exact (element (FixtureCatalog.ReadText entry)))
                            $"{yamlLocation} reads back as its JSON"
                else
                    failtest failure
            }
        ]
    ]
