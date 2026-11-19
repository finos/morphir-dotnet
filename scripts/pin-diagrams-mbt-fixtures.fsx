// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

// Pins the diagrams.mbt test corpora that the Morphir.Diagrams tests read.
//
//   dotnet fsi scripts/pin-diagrams-mbt-fixtures.fsx <diagrams.mbt checkout at 60a8dd1>
//
// Writes one JSON file per corpus to tests/Fixtures/DiagramsMbt/ with each input's upstream path, SHA-256 and exact
// text, and prints the SHA-256 of each written file for PROVENANCE.md and FixtureCatalog.DiagramsMbt. Only test
// inputs and expected results are copied: no grammar or program source (PlantUML's GPL Java, Mermaid's TypeScript).

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

let commit = "60a8dd19f386b6fc65e9ddf5ddccec213c622d07"

let corpora =
    [
        "mermaid-sequence", [ "tests/corpus/mermaid-12.2.0" ]
        "mermaid-flowcharts", [ "tests/corpus/mermaid-flowcharts" ]
        "mermaid-states", [ "tests/corpus/mermaid-states" ]
        "mermaid-swimlanes", [ "tests/corpus/mermaid-swimlanes" ]
        "mermaid-architecture", [ "tests/architecture-corpus" ]
        "zenuml", [ "tests/zenuml-corpus" ]
        "plantuml-activity", [ "tests/corpus/plantuml-activity" ]
        "plantuml-state", [ "tests/corpus/plantuml-state" ]
        "plantuml", [ "tests/corpus/plantuml-1.2026.9" ]
        "bpmn", [ "tests/corpus/bpmn-model"; "tests/traversal-corpus"; "tests/query-corpus" ]
    ]

let kept = set [ ".mmd"; ".puml"; ".bpmn"; ".json"; ".txt"; ".graph" ]

let root = fsi.CommandLineArgs |> Array.tryItem 1 |> Option.defaultWith (fun () -> failwith "Pass the diagrams.mbt checkout.")
let output = Path.Combine(__SOURCE_DIRECTORY__, "..", "tests", "Fixtures", "DiagramsMbt")
Directory.CreateDirectory output |> ignore

let hex (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

for name, folders in corpora do
    let files =
        folders
        |> Seq.collect (fun folder -> Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories))
        |> Seq.filter (fun path -> kept.Contains(Path.GetExtension path))
        |> Seq.map (fun path -> Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes path)
        |> Seq.sortBy fst
        |> Seq.toArray

    use stream = new MemoryStream()

    do
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WriteString("repository", "https://github.com/moonrockz/diagrams.mbt")
        writer.WriteString("commit", commit)
        writer.WriteStartArray "files"

        for path, bytes in files do
            writer.WriteStartObject()
            writer.WriteString("path", path)
            writer.WriteString("sha256", hex bytes)
            writer.WriteString("text", UTF8Encoding(false, true).GetString bytes)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()

    let bytes = Array.append (stream.ToArray()) [| byte '\n' |]
    File.WriteAllBytes(Path.Combine(output, $"{name}.json"), bytes)
    printfn $"| `{name}.json` | {files.Length} | `{hex bytes}` |"
