// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Toolchain

open System
open System.Text

/// <summary>
/// Unicode Normalization Form C with this library's own Unicode 17.0.0 tables (UAX #15): canonical decomposition,
/// canonical ordering by combining class, then canonical composition outside Full_Composition_Exclusion, with Hangul
/// by arithmetic. It does not use the operating system's ICU or .NET's <c>String.Normalize</c>, so a plan digest is
/// the same on every machine and in invariant globalization mode.
/// </summary>
/// <remarks>
/// Code points that Unicode 17.0.0 does not assign, and noncharacters such as U+FFFE, pass through unchanged with
/// combining class 0: they have no decomposition and compose with nothing. An unpaired surrogate is not a scalar
/// value; it passes through unchanged as a starter that composes with nothing. The document readers never produce
/// one, because they decode strict UTF-8, and the canonical encoder's UTF-8 step is what then decides its bytes.
/// </remarks>
module Nfc =
    /// The Unicode version of the tables.
    let unicodeVersion = NfcTables.UnicodeVersion

    // Hangul syllable arithmetic (Unicode chapter 3.12).
    let private sBase = 0xAC00
    let private lBase = 0x1100
    let private vBase = 0x1161
    let private tBase = 0x11A7
    let private lCount = 19
    let private vCount = 21
    let private tCount = 28
    let private nCount = vCount * tCount
    let private sCount = lCount * nCount

    /// The canonical combining class of a code point; 0 for every code point the table does not list.
    let combiningClass (cp: int) =
        let table = NfcTables.combiningClasses
        let mutable low = 0
        let mutable high = table.Length - 1
        let mutable found = 0

        while low <= high do
            let middle = (low + high) >>> 1
            let key = int (table[middle] >>> 8)

            if key < cp then low <- middle + 1
            elif key > cp then high <- middle - 1
            else
                found <- int (table[middle] &&& 0xFFu)
                low <- high + 1

        found

    let private decompositionIndex (cp: int) =
        let index = Array.BinarySearch(NfcTables.decompositionKeys, cp)
        if index >= 0 then index else -1

    /// The primary composite of a pair, or -1.
    let private compose (first: int) (second: int) =
        if first >= lBase && first < lBase + lCount && second >= vBase && second < vBase + vCount then
            sBase + ((first - lBase) * vCount + (second - vBase)) * tCount
        elif
            first >= sBase
            && first < sBase + sCount
            && (first - sBase) % tCount = 0
            && second > tBase
            && second < tBase + tCount
        then
            first + (second - tBase)
        else
            let key = (uint64 first <<< 21) ||| uint64 second
            let index = Array.BinarySearch(NfcTables.compositionKeys, key)
            if index >= 0 then NfcTables.compositionValues[index] else -1

    /// Appends the full canonical decomposition of a code point.
    let private decompose (output: ResizeArray<int>) (cp: int) =
        if cp >= sBase && cp < sBase + sCount then
            let index = cp - sBase
            output.Add(lBase + index / nCount)
            output.Add(vBase + (index % nCount) / tCount)

            if index % tCount <> 0 then
                output.Add(tBase + index % tCount)
        else
            match decompositionIndex cp with
            | -1 -> output.Add cp
            | index ->
                for at in NfcTables.decompositionStarts[index] .. NfcTables.decompositionStarts[index + 1] - 1 do
                    output.Add NfcTables.decompositionData[at]

    /// Code points of the text; an unpaired surrogate is its own code unit value.
    let private codePoints (text: string) =
        let output = ResizeArray<int>(text.Length)
        let mutable index = 0

        while index < text.Length do
            let c = text[index]

            if Char.IsHighSurrogate c && index + 1 < text.Length && Char.IsLowSurrogate text[index + 1] then
                decompose output (Char.ConvertToUtf32(c, text[index + 1]))
                index <- index + 2
            else
                decompose output (int c)
                index <- index + 1

        output

    /// <summary>Canonical ordering: a stable sort of each run of non-starters by combining class. A short run is
    /// sorted by insertion; a long one by counting over the 256 classes, so the cost is linear in the run whatever
    /// the order of its classes.</summary>
    let private reorder (points: ResizeArray<int>) =
        let sortRun (start: int) (finish: int) =
            if finish - start <= 32 then
                for index in start + 1 .. finish - 1 do
                    let current = points[index]
                    let currentClass = combiningClass current
                    let mutable at = index

                    while at > start && combiningClass points[at - 1] > currentClass do
                        points[at] <- points[at - 1]
                        at <- at - 1

                    points[at] <- current
            else
                let classes = Array.init (finish - start) (fun offset -> combiningClass points[start + offset])
                let starts = Array.zeroCreate<int> 257

                for c in classes do
                    starts[c + 1] <- starts[c + 1] + 1

                for c in 1..256 do
                    starts[c] <- starts[c] + starts[c - 1]

                let sorted = Array.zeroCreate<int> (finish - start)

                for offset in 0 .. classes.Length - 1 do
                    let c = classes[offset]
                    sorted[starts[c]] <- points[start + offset]
                    starts[c] <- starts[c] + 1

                for offset in 0 .. sorted.Length - 1 do
                    points[start + offset] <- sorted[offset]

        let mutable index = 0

        while index < points.Count do
            if combiningClass points[index] = 0 then
                index <- index + 1
            else
                let start = index

                while index < points.Count && combiningClass points[index] <> 0 do
                    index <- index + 1

                sortRun start index
    /// Canonical composition, in place; returns the new length.
    let private composeAll (points: ResizeArray<int>) =
        if points.Count = 0 then
            0
        else
            let mutable starterAt = 0
            let mutable starter = points[0]
            let mutable lastClass = combiningClass starter

            // A text that starts with a non-starter has no starter to compose with yet.
            if lastClass <> 0 then
                lastClass <- 256

            let mutable written = 1

            for index in 1 .. points.Count - 1 do
                let cp = points[index]
                let cpClass = combiningClass cp
                let composite = compose starter cp

                // A character composes with the last starter unless something between them blocks it: a character of
                // the same or a higher class, or a starter.
                if composite >= 0 && (lastClass < cpClass || lastClass = 0) then
                    points[starterAt] <- composite
                    starter <- composite
                else
                    if cpClass = 0 then
                        starterAt <- written
                        starter <- cp

                    lastClass <- cpClass
                    points[written] <- cp
                    written <- written + 1

            written

    /// <summary>The NFC of the text. Text whose UTF-16 code units are all below U+0300 is already NFC and is
    /// returned as it is, without a copy.</summary>
    let normalize (text: string) =
        let mutable below = true
        let mutable index = 0

        while below && index < text.Length do
            if int text[index] >= 0x300 then below <- false
            index <- index + 1

        if below then
            text
        else
            let points = codePoints text
            reorder points
            let length = composeAll points
            let builder = StringBuilder(text.Length)

            for at in 0 .. length - 1 do
                let cp = points[at]

                if cp > 0xFFFF then
                    builder.Append(Char.ConvertFromUtf32 cp) |> ignore
                else
                    builder.Append(char cp) |> ignore

            builder.ToString()

    /// Whether the text is already NFC.
    let isNormalized (text: string) =
        String.Equals(normalize text, text, StringComparison.Ordinal)
