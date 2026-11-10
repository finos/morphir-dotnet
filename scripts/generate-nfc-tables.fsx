// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
//
// Generates src/Morphir.Toolchain/NfcTables.fs, the Unicode Normalization Form C data that Morphir.Toolchain
// normalizes plan strings and target paths with, from the Unicode Character Database 17.0.0.
//
// Usage:
//   dotnet fsi scripts/generate-nfc-tables.fsx -- --source <directory>
//
// <directory> holds the three input files below, downloaded unchanged from https://www.unicode.org/Public/17.0.0/ucd/.
// Each must have the recorded SHA-256, so the generated table follows from exactly these bytes. The generator writes
// the table and nothing else; it never downloads, and the build never runs it.
//
// The derived Full_Composition_Exclusion set (the composition exclusions, the singletons and the non-starter
// decompositions) is checked against DerivedNormalizationProps.txt before anything is written.

open System
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text

let unicodeVersion = "17.0.0"
let baseUrl = $"https://www.unicode.org/Public/{unicodeVersion}/ucd/"

let inputs =
    [ "UnicodeData.txt", "2e1efc1dcb59c575eedf5ccae60f95229f706ee6d031835247d843c11d96470c"
      "CompositionExclusions.txt", "2f239196ef3b5b61db5cc476e9bd80f534d15aa1b74e1be1dea5d042a344c85f"
      "DerivedNormalizationProps.txt", "71fd6a206a2c0cdd41feb6b7f656aa31091db45e9cedc926985d718397f9e488" ]

let fail (message: string) =
    eprintfn "generate-nfc-tables: %s" message
    exit 1

let args = fsi.CommandLineArgs |> Array.skip 1 |> Array.filter ((<>) "--") |> List.ofArray

let source =
    match args with
    | [ "--source"; directory ] -> Path.GetFullPath directory
    | _ -> fail "usage: dotnet fsi scripts/generate-nfc-tables.fsx -- --source <directory>"

let repository = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let output = Path.Combine(repository, "src", "Morphir.Toolchain", "NfcTables.fs")

let read (name: string) =
    let path = Path.Combine(source, name)

    if not (File.Exists path) then
        fail $"{path} does not exist; download it from {baseUrl}{name}"

    let bytes = File.ReadAllBytes path
    let actual = Convert.ToHexStringLower(SHA256.HashData bytes)
    let expected = inputs |> List.find (fst >> (=) name) |> snd

    if actual <> expected then
        fail $"{name} has SHA-256 {actual}, not the recorded {expected} of Unicode {unicodeVersion}"

    Encoding.UTF8.GetString(bytes).Split('\n') |> Array.map (fun line -> line.TrimEnd('\r'))

let hex (text: string) = Int32.Parse(text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture)

/// Code points of a data line's first field: one code point or a range a..b.
let codePoints (field: string) =
    match field.Trim().Split("..") with
    | [| one |] -> [ hex one ]
    | [| first; last |] -> [ hex first .. hex last ]
    | _ -> fail $"cannot read the code point field '{field}'"

let dataLines (lines: string array) =
    lines
    |> Array.map (fun line -> match line.IndexOf '#' with -1 -> line | at -> line.Substring(0, at))
    |> Array.filter (fun line -> line.Trim() <> "")

// ---- UnicodeData.txt: combining classes and canonical decomposition mappings ----

let unicodeData = read "UnicodeData.txt"
let combining = Collections.Generic.Dictionary<int, int>()
let mappings = Collections.Generic.Dictionary<int, int list>()

for line in dataLines unicodeData do
    let fields = line.Split ';'
    let cp = hex fields[0]
    let ccc = Int32.Parse(fields[3], CultureInfo.InvariantCulture)

    if ccc <> 0 then
        combining[cp] <- ccc

    // A mapping that starts with a <tag> is a compatibility mapping; NFC uses canonical mappings only.
    let decomposition = fields[5].Trim()

    if decomposition <> "" && not (decomposition.StartsWith "<") then
        mappings[cp] <- decomposition.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.map hex |> List.ofArray

let cccOf cp =
    match combining.TryGetValue cp with
    | true, ccc -> ccc
    | _ -> 0

let rec fullDecomposition cp =
    match mappings.TryGetValue cp with
    | true, mapping -> mapping |> List.collect fullDecomposition
    | _ -> [ cp ]

// ---- Composition exclusions, derived and checked ----

let exclusions = dataLines (read "CompositionExclusions.txt") |> Array.toList |> List.collect codePoints |> set

let derived =
    set
        [ yield! exclusions
          for KeyValue(cp, mapping) in mappings do
              // Singletons, and decompositions of or into a non-starter.
              if List.length mapping = 1 || cccOf cp <> 0 || cccOf (List.head mapping) <> 0 then
                  yield cp ]

let published =
    dataLines (read "DerivedNormalizationProps.txt")
    |> Array.map (fun line -> line.Split ';')
    |> Array.filter (fun fields -> fields.Length > 1 && fields[1].Trim() = "Full_Composition_Exclusion")
    |> Array.toList
    |> List.collect (fun fields -> codePoints fields[0])
    |> set

if derived <> published then
    let missing = Set.difference published derived |> Set.toList |> List.truncate 5
    let extra = Set.difference derived published |> Set.toList |> List.truncate 5
    fail $"the derived Full_Composition_Exclusion differs from DerivedNormalizationProps.txt: missing {missing}, extra {extra}"

// ---- The tables ----

let classes = combining |> Seq.map (fun (KeyValue(cp, ccc)) -> cp, ccc) |> Seq.sortBy fst |> List.ofSeq

let decompositions =
    mappings.Keys
    |> Seq.sort
    |> Seq.map (fun cp -> cp, fullDecomposition cp)
    |> List.ofSeq

let compositions =
    [ for KeyValue(cp, mapping) in mappings do
          match mapping with
          | [ first; second ] when not (derived.Contains cp) -> yield (uint64 first <<< 21) ||| uint64 second, cp
          | _ -> () ]
    |> List.sortBy fst

let lines (items: string list) =
    items
    |> List.chunkBySize 8
    |> List.map (fun chunk -> "          " + String.Join("; ", chunk))
    |> String.concat "\n"

let decompositionStarts =
    decompositions |> List.scan (fun start (_, mapping) -> start + List.length mapping) 0

let text =
    $"""// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
// <auto-generated>
// Generated by scripts/generate-nfc-tables.fsx from the Unicode Character Database {unicodeVersion}. Do not edit.
// Sources, each checked by SHA-256 before generation:
{inputs |> List.map (fun (name, sha) -> $"//   {baseUrl}{name}\n//     sha256 {sha}") |> String.concat "\n"}
// Unicode data is distributed under the Unicode License v3; see NOTICE.
// </auto-generated>
namespace Morphir.Toolchain

/// <summary>Unicode {unicodeVersion} data for Normalization Form C, generated from the Unicode Character Database.
/// Hangul syllables are not listed: they decompose and compose by arithmetic.</summary>
module internal NfcTables =
    /// The Unicode version of this data.
    [<Literal>]
    let UnicodeVersion = "{unicodeVersion}"

    /// Every code point with a non-zero canonical combining class, as (code point &lt;&lt;&lt; 8) ||| class, in
    /// code point order.
    let combiningClasses: uint32 array =
        [|
{classes |> List.map (fun (cp, ccc) -> $"0x%08X{(uint32 cp <<< 8) ||| uint32 ccc}u") |> lines}
        |]

    /// Every code point with a canonical decomposition mapping, in code point order.
    let decompositionKeys: int array =
        [|
{decompositions |> List.map (fun (cp, _) -> $"0x%X{cp}") |> lines}
        |]

    /// Where each key's full canonical decomposition starts in decompositionData; one more entry than the keys.
    let decompositionStarts: int array =
        [|
{decompositionStarts |> List.map string |> lines}
        |]

    /// The full canonical decompositions, mapped recursively, one after another.
    let decompositionData: int array =
        [|
{decompositions |> List.collect snd |> List.map (fun cp -> $"0x%X{cp}") |> lines}
        |]

    /// The primary composites, keyed by (first &lt;&lt;&lt; 21) ||| second and sorted: every canonical pair
    /// decomposition of a code point outside Full_Composition_Exclusion.
    let compositionKeys: uint64 array =
        [|
{compositions |> List.map (fun (key, _) -> $"0x%X{key}UL") |> lines}
        |]

    /// The composite of each pair in compositionKeys.
    let compositionValues: int array =
        [|
{compositions |> List.map (fun (_, cp) -> $"0x%X{cp}") |> lines}
        |]
"""

File.WriteAllText(output, text.Replace("\r\n", "\n"))

let bytes = (classes.Length * 4) + (decompositions.Length * 4) + (decompositionStarts.Length * 4) + ((decompositions |> List.sumBy (snd >> List.length)) * 4) + (compositions.Length * 12)

printfn "Unicode %s: %d combining classes, %d decompositions (%d code points), %d compositions; %d bytes of table data"
    unicodeVersion classes.Length decompositions.Length (decompositions |> List.sumBy (snd >> List.length)) compositions.Length bytes
printfn "Wrote %s" output
