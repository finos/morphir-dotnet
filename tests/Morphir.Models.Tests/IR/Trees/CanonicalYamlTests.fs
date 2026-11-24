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

/// One kit case that states both a canonical YAML and a canonical JSON spelling.
type private KitPair = { Name: string; Yaml: string; Json: string }

let private docstring =
    Regex(
        "its canonical YAML spelling is:[ ]*\\n([ ]*)\"\"\"yaml\\n(.*?)\\n[ ]*\"\"\"",
        RegexOptions.Singleline ||| RegexOptions.CultureInvariant
    )

let private inlineYaml =
    Regex(@"its canonical YAML spelling is (?!:)(.+)$", RegexOptions.Multiline ||| RegexOptions.CultureInvariant)

let private canonicalJson =
    Regex(@"its canonical JSON spelling is (.+)$", RegexOptions.Multiline ||| RegexOptions.CultureInvariant)

let private cells (row: string) =
    row.Trim().Trim('|').Split('|')
    |> Array.map (fun cell -> cell.Trim())
    |> List.ofArray

/// The YAML and JSON pairs of a feature file: a scenario's docstring or one-line YAML with its JSON step, and the
/// YAML and JSON rows of an outline's examples.
let private pairsOf (feature: FixtureEntry) : KitPair list =
    let file = Path.GetFileName feature.RelativePath
    let text = FixtureCatalog.ReadText(feature).Replace("\r\n", "\n")

    let scenarios =
        Regex.Split(text, @"\n(?=  Scenario)")
        |> Array.filter (fun block -> block.StartsWith "  Scenario")

    [
        for scenario in scenarios do
            let title = (scenario.Split('\n')[0]).Trim()

            let yamlText =
                let block = docstring.Match scenario

                if block.Success then
                    let margin = block.Groups[1].Value.Length

                    block.Groups[2].Value.Split('\n')
                    |> Array.map (fun line -> if line.Length >= margin then line.Substring margin else line.TrimStart())
                    |> String.concat "\n"
                    |> Some
                else
                    let line = inlineYaml.Match scenario
                    if line.Success then Some(line.Groups[1].Value.Trim()) else None

            match yamlText, canonicalJson.Match scenario with
            | Some yaml, json when json.Success ->
                yield {
                    Name = $"{file}: {title}"
                    Yaml = yaml
                    Json = json.Groups[1].Value.Trim()
                }
            | _ -> ()

            // An outline's examples: one row for each format.
            let rows =
                scenario.Split('\n')
                |> Array.map (fun line -> line.Trim())
                |> Array.filter (fun line -> line.StartsWith "|")
                |> Array.map cells
                |> List.ofArray

            match rows with
            | header :: body when List.contains "format" header && List.contains "spelling" header ->
                let formatAt = List.findIndex ((=) "format") header
                let spellingAt = List.findIndex ((=) "spelling") header

                let spelling format =
                    body
                    |> List.tryFind (fun row -> row[formatAt] = format)
                    |> Option.map (fun row -> row[spellingAt])

                match spelling "YAML", spelling "JSON" with
                | Some yaml, Some json ->
                    yield {
                        Name = $"{file}: {title} (examples)"
                        Yaml = yaml
                        Json = json
                    }
                | _ -> ()
            | _ -> ()
    ]

let private kitPairs =
    FixtureCatalog.UpstreamIr.KitFeatures |> List.ofSeq |> List.collect pairsOf

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
                    "42"; "-7"; "+1"; "1e3"; "1.5"; ".5"; "0x1F"; "0o17"; ".inf"; "-.inf"; ".nan"; ".NaN"
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

            for text in [ "a"; "a:b"; "a#b"; "...suffix"; "a..."; "a---"; "morphir/SDK"; "value-in-USD"; "yes"; "2026-01-15"; "é😀"; "a b" ] do
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

            test "A JSON tree becomes a YAML tree with the .yaml extension" {
                let files = [
                    "manifest.json", ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"formatVersion\": 4}")
                    "pkg/p/m/module.json", ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"doc\": \"x\"}")
                    "pkg/p/m/a.type.json", ReadOnlyMemory(Encoding.UTF8.GetBytes "{\"TypeAliasDefinition\": {}}")
                ]

                match CanonicalYaml.ofJsonTree files with
                | Ok written ->
                    Expect.sequenceEqual
                        (written |> List.map (fun (location, bytes) -> location, Encoding.UTF8.GetString bytes))
                        [
                            "manifest.yaml", "formatVersion: 4\n"
                            "pkg/p/m/module.yaml", "doc: x\n"
                            "pkg/p/m/a.type.yaml", "TypeAliasDefinition: {}\n"
                        ]
                        "Each file at its YAML location"
                | Error message -> failtest message
            }
        ]
    ]
