// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

// Builds the diagram icon catalogue from diagrams.mbt.
//
//   dotnet fsi scripts/build-diagram-icons.fsx <diagrams.mbt checkout at 60a8dd1>
//
// Reads pkgs/diagrams-svg/icons/catalog.json, checks the SHA-256 of every raw source asset against its record, and
// writes src/Morphir.Diagrams/Icons/icons.json.gz: each key's family, attribution, terms and normalized vector tree,
// for the families whose terms allow copying the icon unchanged (AWS, Azure, Google Cloud and the MIT UML symbols).
// Technology logos have no per-icon licence recorded, and `externalsystem` is CC-BY-SA-4.0, so Morphir.Diagrams draws
// its own symbols for those keys (Icons.fs). Prints the SHA-256 of the written file for Icons/PROVENANCE.md.

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text.Json

let root = fsi.CommandLineArgs |> Array.tryItem 1 |> Option.defaultWith (fun () -> failwith "Pass the diagrams.mbt checkout.")
let icons = Path.Combine(root, "pkgs", "diagrams-svg", "icons")
let copied = set [ "aws"; "azure"; "google"; "uml" ]
let hex (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

let catalog = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(icons, "catalog.json"))).RootElement
let records = catalog.GetProperty("records").EnumerateArray() |> Seq.toArray

for record in records do
    let raw = File.ReadAllBytes(Path.Combine(icons, record.GetProperty("raw").GetString()))

    if hex raw <> record.GetProperty("sourceSha256").GetString() then
        failwith $"""The source of {record.GetProperty("key").GetString()} does not match its recorded digest."""

let kept = records |> Array.filter (fun record -> copied.Contains(record.GetProperty("family").GetString()))
let assets = kept |> Array.map (fun record -> record.GetProperty("asset").GetString()) |> Array.distinct |> Array.sort

use buffer = new MemoryStream()

do
    use writer = new Utf8JsonWriter(buffer)
    writer.WriteStartObject()
    writer.WriteString("source", "https://github.com/moonrockz/diagrams.mbt/tree/60a8dd19f386b6fc65e9ddf5ddccec213c622d07/pkgs/diagrams-svg/icons")
    writer.WriteStartObject "keys"

    for record in kept |> Array.sortBy (fun record -> record.GetProperty("key").GetString()) do
        writer.WriteStartArray(record.GetProperty("key").GetString())
        for field in [ "asset"; "family"; "attribution"; "licence" ] do writer.WriteStringValue(record.GetProperty(field).GetString())
        writer.WriteEndArray()

    writer.WriteEndObject()
    writer.WriteStartObject "assets"

    for asset in assets do
        writer.WritePropertyName asset
        catalog.GetProperty("assets").GetProperty(asset).WriteTo writer

    writer.WriteEndObject()
    writer.WriteEndObject()

let output = Path.Combine(__SOURCE_DIRECTORY__, "..", "src", "Morphir.Diagrams", "Icons", "icons.json.gz")
Directory.CreateDirectory(Path.GetDirectoryName output) |> ignore

do
    // A fixed compression level and no file name or time in the header keep the bytes reproducible.
    use file = File.Create output
    use gzip = new GZipStream(file, CompressionLevel.SmallestSize)
    gzip.Write(buffer.ToArray())

printfn $"{kept.Length} keys, {assets.Length} assets, {hex (File.ReadAllBytes output)}"
