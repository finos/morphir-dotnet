// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Raster

open System
open System.Collections.Generic
open System.IO

/// A glyph: its outline contours in font units (y up) and its advance width.
type internal Glyph = { Contours: (double * double) list list; Advance: double }

/// A small TrueType reader: `cmap` formats 4 and 12, `head`, `hhea`, `hmtx`, `loca` and `glyf` with simple and
/// composite glyphs. Quadratic curves are flattened.
[<Sealed>]
type internal TrueType(data: byte[]) =
    let u16 offset = (int data[offset] <<< 8) ||| int data[offset + 1]
    let i16 offset = int (int16 (u16 offset))
    let u32 offset = (uint32 (u16 offset) <<< 16) ||| uint32 (u16 (offset + 2))

    let tables =
        let count = u16 4
        dict [ for index in 0 .. count - 1 -> let at = 12 + 16 * index in Text.Encoding.ASCII.GetString(data, at, 4), int (u32 (at + 8)) ]

    let table name =
        match tables.TryGetValue name with
        | true, offset -> offset
        | _ -> invalidOp $"The font has no {name} table."

    let unitsPerEm = float (u16 (table "head" + 18))
    let longLoca = i16 (table "head" + 50) = 1
    let metrics = u16 (table "hhea" + 34)
    let glyphCount = u16 (table "maxp" + 4)

    /// Character to glyph index.
    let map =
        let found = Dictionary<int, int>()
        let cmap = table "cmap"
        let count = u16 (cmap + 2)

        let subtables =
            [ for index in 0 .. count - 1 -> u16 (cmap + 4 + 8 * index), u16 (cmap + 6 + 8 * index), cmap + int (u32 (cmap + 8 + 8 * index)) ]

        // Prefer the full Unicode table (format 12), then the BMP table (format 4).
        let ordered = subtables |> List.sortBy (fun (_, _, at) -> if u16 at = 12 then 0 else 1)

        for _, _, at in ordered do
            match u16 at with
            | 12 ->
                let groups = int (u32 (at + 12))

                for group in 0 .. groups - 1 do
                    let g = at + 16 + 12 * group
                    let first, last, glyph = int (u32 g), int (u32 (g + 4)), int (u32 (g + 8))
                    for code in first..last do found.TryAdd(code, glyph + code - first) |> ignore
            | 4 ->
                let segments = u16 (at + 6) / 2
                let ends = at + 14
                let starts = ends + 2 * segments + 2
                let deltas = starts + 2 * segments
                let offsets = deltas + 2 * segments

                for segment in 0 .. segments - 1 do
                    let last, first = u16 (ends + 2 * segment), u16 (starts + 2 * segment)
                    let delta, rangeOffset = i16 (deltas + 2 * segment), u16 (offsets + 2 * segment)

                    for code in first..last do
                        if code <> 0xFFFF then
                            let glyph =
                                if rangeOffset = 0 then (code + delta) &&& 0xFFFF
                                else
                                    let at = offsets + 2 * segment + rangeOffset + 2 * (code - first)
                                    let raw = u16 at
                                    if raw = 0 then 0 else (raw + delta) &&& 0xFFFF

                            if glyph <> 0 then found.TryAdd(code, glyph) |> ignore
            | _ -> ()

        found

    let advance glyph =
        let hmtx = table "hmtx"
        float (u16 (hmtx + 4 * (min glyph (metrics - 1))))

    let location glyph =
        let loca = table "loca"
        if longLoca then int (u32 (loca + 4 * glyph)), int (u32 (loca + 4 * glyph + 4))
        else 2 * u16 (loca + 2 * glyph), 2 * u16 (loca + 2 * glyph + 2)

    let cache = Dictionary<int, (double * double) list list>()

    /// The contours of a glyph as flattened polygons, composites resolved without recursion beyond eight levels.
    let rec outline (glyph: int) (level: int) : (double * double) list list =
        if glyph < 0 || glyph >= glyphCount || level > 8 then []
        else
            match cache.TryGetValue glyph with
            | true, found -> found
            | _ ->
                let start, finish = location glyph
                let at = table "glyf" + start

                let result =
                    if finish <= start then []
                    else
                        let contours = i16 at

                        if contours >= 0 then
                            let endsAt = [ for index in 0 .. contours - 1 -> u16 (at + 10 + 2 * index) ]
                            let pointCount = if contours = 0 then 0 else List.last endsAt + 1
                            let instructions = u16 (at + 10 + 2 * contours)
                            let mutable cursor = at + 12 + 2 * contours + instructions
                            let flags = Array.zeroCreate pointCount
                            let mutable index = 0

                            while index < pointCount do
                                let flag = data[cursor]
                                cursor <- cursor + 1
                                flags[index] <- flag
                                index <- index + 1

                                if flag &&& 8uy <> 0uy then
                                    let repeat = int data[cursor]
                                    cursor <- cursor + 1

                                    for _ in 1..repeat do
                                        flags[index] <- flag
                                        index <- index + 1

                            let coordinate (short: byte) (same: byte) =
                                let values = Array.zeroCreate pointCount
                                let mutable value = 0

                                for point in 0 .. pointCount - 1 do
                                    let flag = flags[point]

                                    if flag &&& short <> 0uy then
                                        let delta = int data[cursor]
                                        cursor <- cursor + 1
                                        value <- value + (if flag &&& same <> 0uy then delta else -delta)
                                    elif flag &&& same = 0uy then
                                        value <- value + i16 cursor
                                        cursor <- cursor + 2

                                    values[point] <- value

                                values

                            let xs = coordinate 2uy 16uy
                            let ys = coordinate 4uy 32uy
                            let mutable first = 0

                            [
                                for last in endsAt do
                                    let points = [ for point in first..last -> float xs[point], float ys[point], flags[point] &&& 1uy <> 0uy ]
                                    first <- last + 1

                                    if not points.IsEmpty then
                                        // Insert the implied on-curve points between consecutive off-curve points.
                                        let expanded =
                                            [
                                                let count = points.Length

                                                for index in 0 .. count - 1 do
                                                    let x, y, on = points[index]
                                                    let nx, ny, nextOn = points[(index + 1) % count]
                                                    yield x, y, on
                                                    if not on && not nextOn then yield (x + nx) / 2.0, (y + ny) / 2.0, true
                                            ]

                                        let startIndex = expanded |> List.tryFindIndex (fun (_, _, on) -> on) |> Option.defaultValue 0
                                        let rotated = (List.skip startIndex expanded) @ (List.take startIndex expanded)
                                        let sx, sy, _ = rotated.Head
                                        let output = ResizeArray [ (sx, sy) ]
                                        let mutable control = None

                                        for x, y, on in List.tail rotated @ [ rotated.Head ] do
                                            if on then
                                                match control with
                                                | Some(cx, cy) ->
                                                    let px, py = output[output.Count - 1]

                                                    for step in 1..6 do
                                                        let t = float step / 6.0
                                                        let u = 1.0 - t
                                                        output.Add((u * u * px + 2.0 * u * t * cx + t * t * x, u * u * py + 2.0 * u * t * cy + t * t * y))

                                                    control <- None
                                                | None -> output.Add((x, y))
                                            else
                                                control <- Some(x, y)

                                        List.ofSeq output
                            ]
                        else
                            // A composite: components with offsets and optional scales.
                            let mutable cursor = at + 10
                            let mutable more = true
                            let parts = ResizeArray<(double * double) list>()

                            while more do
                                let flags = u16 cursor
                                let part = u16 (cursor + 2)
                                cursor <- cursor + 4

                                let dx, dy =
                                    if flags &&& 1 <> 0 then
                                        let x, y = i16 cursor, i16 (cursor + 2)
                                        cursor <- cursor + 4
                                        float x, float y
                                    else
                                        let x, y = int (sbyte data[cursor]), int (sbyte data[cursor + 1])
                                        cursor <- cursor + 2
                                        float x, float y

                                let f2dot14 offset = float (i16 offset) / 16384.0

                                let a, b, c, d =
                                    if flags &&& 8 <> 0 then
                                        let s = f2dot14 cursor
                                        cursor <- cursor + 2
                                        s, 0.0, 0.0, s
                                    elif flags &&& 64 <> 0 then
                                        let sx, sy = f2dot14 cursor, f2dot14 (cursor + 2)
                                        cursor <- cursor + 4
                                        sx, 0.0, 0.0, sy
                                    elif flags &&& 128 <> 0 then
                                        let values = f2dot14 cursor, f2dot14 (cursor + 2), f2dot14 (cursor + 4), f2dot14 (cursor + 6)
                                        cursor <- cursor + 8
                                        values
                                    else
                                        1.0, 0.0, 0.0, 1.0

                                for contour in outline part (level + 1) do
                                    parts.Add(contour |> List.map (fun (x, y) -> a * x + c * y + dx, b * x + d * y + dy))

                                more <- flags &&& 32 <> 0

                            List.ofSeq parts

                cache[glyph] <- result
                result

    /// Units per em.
    member _.UnitsPerEm = unitsPerEm

    /// The glyph of a code point, or none when the font does not have it.
    member _.Glyph(codePoint: int) =
        match map.TryGetValue codePoint with
        | true, glyph -> Some { Contours = outline glyph 0; Advance = advance glyph }
        | _ -> None

/// The embedded Noto fonts: Sans Mono regular and bold, then Sans Symbols 2 for symbols the mono fonts lack.
[<RequireQualifiedAccess>]
module internal Fonts =
    let private load (name: string) =
        lazy
            (use stream =
                match typeof<TrueType>.Assembly.GetManifestResourceStream $"Morphir.Diagrams.Raster.Fonts.{name}" with
                | null -> invalidOp $"The font {name} is missing."
                | stream -> stream

             use buffer = new MemoryStream()
             stream.CopyTo buffer
             TrueType(buffer.ToArray()))

    let private regular = load "regular.ttf"
    let private bold = load "bold.ttf"
    let private symbols = load "symbols.ttf"
    let private gate = obj ()

    /// The glyph of a code point in the first font that has it, with that font's units per em, or none.
    let glyph (heavy: bool) (codePoint: int) =
        lock gate (fun () ->
            let primary = if heavy then bold.Value else regular.Value

            match primary.Glyph codePoint with
            | Some found -> Some(found, primary.UnitsPerEm)
            | None -> symbols.Value.Glyph codePoint |> Option.map (fun found -> found, symbols.Value.UnitsPerEm))
