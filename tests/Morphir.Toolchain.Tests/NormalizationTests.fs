// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// Morphir.Toolchain's own NFC against the Unicode 17.0.0 conformance file, all of it: for every line,
/// c2 == NFC(c1) == NFC(c2) == NFC(c3) and c4 == NFC(c4) == NFC(c5). Every scalar value that Part 1 of the file does
/// not list must be its own NFC.
/// </summary>
module Morphir.Toolchain.Tests.NormalizationTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Expecto
open Morphir.Toolchain

let private directory =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Unicode", "17.0.0"))

let private file name = Path.Combine(directory, name)

/// One data line of NormalizationTest.txt: its part, line number and five columns as strings.
type private Line =
    { Part: string
      Number: int
      Columns: string array }

let private scalars (field: string) =
    let builder = StringBuilder()

    for item in field.Split(' ', StringSplitOptions.RemoveEmptyEntries) do
        builder.Append(Char.ConvertFromUtf32(Convert.ToInt32(item, 16))) |> ignore

    builder.ToString()

let private lines =
    lazy
        [ let mutable part = ""

          for number, text in File.ReadAllLines(file "NormalizationTest.txt") |> Array.indexed do
              if text.StartsWith "@Part" then
                  part <- text.Split(' ')[0]
              elif text <> "" && not (text.StartsWith "#") then
                  let fields = text.Split ';'
                  yield { Part = part; Number = number + 1; Columns = fields[0..4] |> Array.map scalars } ]

let private show (text: string) =
    text.EnumerateRunes() |> Seq.map (fun rune -> $"%04X{rune.Value}") |> String.concat " "

/// The lines of one part whose NFC invariants fail, described.
let private failures (part: string) =
    [ for line in lines.Value do
          if line.Part = part then
              let c = line.Columns
              let nfc = Nfc.normalize

              let holds =
                  c[1] = nfc c[0] && c[1] = nfc c[1] && c[1] = nfc c[2] && c[3] = nfc c[3] && c[3] = nfc c[4]

              if not holds then
                  yield $"line {line.Number}: NFC({show c[0]}) = {show (nfc c[0])}, expected {show c[1]}" ]

[<Tests>]
let tests =
    testList "NFC" [
        testCase "the vendored Unicode 17.0.0 files have their recorded SHA-256" (fun () ->
            for name, sha in
                [ "NormalizationTest.txt", "5019ffd530751a741900c849c0e010332f142a3612234639bd200b82138a87db"
                  "LICENSE.txt", "e7a93b009565cfce55919a381437ac4db883e9da2126fa28b91d12732bc53d96" ] do
                Expect.equal (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file name)))) sha name)

        testCase "the tables are Unicode 17.0.0, and so is the conformance file" (fun () ->
            Expect.equal Nfc.unicodeVersion "17.0.0" "the tables"
            Expect.stringStarts (File.ReadLines(file "NormalizationTest.txt") |> Seq.head) "# NormalizationTest-17.0.0.txt" "the file")

        testList "NormalizationTest.txt" [
            for part in [ "@Part0"; "@Part1"; "@Part2"; "@Part3" ] ->
                testCase part (fun () ->
                    let failed = failures part
                    Expect.isNonEmpty (lines.Value |> List.filter (fun line -> line.Part = part)) $"{part} has lines"
                    Expect.isEmpty (List.truncate 10 failed) $"{failed.Length} lines of {part} fail")
        ]

        testCase "every scalar value that Part 1 does not list is its own NFC" (fun () ->
            let listed =
                lines.Value
                |> List.filter (fun line -> line.Part = "@Part1")
                |> List.map (fun line -> Char.ConvertToUtf32(line.Columns[0], 0))
                |> set

            let failed =
                [ for cp in 0 .. 0x10FFFF do
                      if not (cp >= 0xD800 && cp <= 0xDFFF) && not (listed.Contains cp) then
                          let text = Char.ConvertFromUtf32 cp

                          if Nfc.normalize text <> text then
                              yield $"U+%04X{cp}" ]

            Expect.isEmpty (List.truncate 10 failed) $"{failed.Length} unlisted scalars change")

        testCase "text below U+0300 is returned as it is, without a copy" (fun () ->
            let text = "toolchains/recipes/fsharp/1.0.0.json \u00E9\u00FF"
            Expect.isTrue (Object.ReferenceEquals(Nfc.normalize text, text)) "the same string")

        testCase "no composition takes a second code point below U+0300, so the fast path is exact" (fun () ->
            // With every scalar below U+0300 its own NFC (checked above), text below U+0300 changes only if two of its
            // characters compose, and no composition's second character is below U+0300.
            let low = NfcTables.compositionKeys |> Array.filter (fun key -> (key &&& 0x1FFFFFUL) < 0x300UL)
            Expect.isEmpty low "compositions whose second character is below U+0300")
        testCase "unassigned code points pass through with combining class 0" (fun () ->
            // U+0378 is unassigned in 17.0.0, and U+FFFE and U+10FFFF are noncharacters.
            for cp in [ 0x0378; 0xFFFE; 0x10FFFF ] do
                let text = "e" + Char.ConvertFromUtf32 cp + string (char 0x0301)
                Expect.equal (Nfc.normalize text) text $"U+%04X{cp} stops nothing and composes with nothing")

        testCase "an unpaired surrogate passes through as a starter that composes with nothing" (fun () ->
            let lone = string (char 0xD800)
            let decomposed = "e" + string (char 0x0301)
            let text = lone + decomposed + string (char 0xDC00)
            Expect.equal (Nfc.normalize text) (lone + string (char 0x00E9) + string (char 0xDC00)) "only the pair composes")

        testCase "Hangul syllables compose and decompose by arithmetic" (fun () ->
            // U+1100 U+1161 U+11A8 is U+AC01; U+AC00 U+11A8 is U+AC01 too.
            Expect.equal (Nfc.normalize "\u1100\u1161\u11A8") "\uAC01" "L V T"
            Expect.equal (Nfc.normalize "\uAC00\u11A8") "\uAC01" "LV T"
            Expect.equal (Nfc.normalize "\uAC01\u11A8") "\uAC01\u11A8" "an LVT syllable takes no second T")
    ]
