// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Raster

open System
open System.Collections.Generic

/// A straight-alpha RGBA colour with 8-bit channels.
[<Struct>]
type internal Rgba = { R: int; G: int; B: int; A: int }

/// An RGBA image painted with 4x4 supersampled coverage in fixed-point arithmetic: points snap to 1/16 pixel, and
/// every step after that is integer, so the same shapes give the same bytes everywhere.
[<Sealed>]
type internal Canvas(width: int, height: int) =
    let pixels: byte[] = Array.zeroCreate (width * height * 4)

    member _.Width = width
    member _.Height = height
    member _.Pixels = pixels

    member _.Clear(color: Rgba) =
        for index in 0 .. width * height - 1 do
            pixels[index * 4] <- byte color.R
            pixels[index * 4 + 1] <- byte color.G
            pixels[index * 4 + 2] <- byte color.B
            pixels[index * 4 + 3] <- byte color.A

    /// Fills device-space polygons with a colour and an opacity in 0..255, by the non-zero or even-odd rule.
    member _.Fill(polygons: (double * double) seq seq, color: Rgba, opacity: int, evenOdd: bool) =
        let edges = ResizeArray<struct (int64 * int64 * int64 * int64 * int)>()
        let mutable top = Int64.MaxValue
        let mutable bottom = Int64.MinValue
        let mutable left = Int64.MaxValue
        let mutable right = Int64.MinValue
        let snap (value: double) = int64 (Math.Round(value * 16.0, MidpointRounding.ToEven))

        for polygon in polygons do
            let points = polygon |> Seq.map (fun (x, y) -> snap x, snap y) |> Seq.toArray

            if points.Length >= 3 then
                for index in 0 .. points.Length - 1 do
                    let x1, y1 = points[index]
                    let x2, y2 = points[(index + 1) % points.Length]
                    left <- min left (min x1 x2)
                    right <- max right (max x1 x2)

                    if y1 <> y2 then
                        // Store edges top to bottom with their direction.
                        if y1 < y2 then edges.Add(struct (x1, y1, x2, y2, 1)) else edges.Add(struct (x2, y2, x1, y1, -1))
                        top <- min top (min y1 y2)
                        bottom <- max bottom (max y1 y2)

        if edges.Count > 0 then
            let firstRow = max 0 (int (top / 16L))
            let lastRow = min (height - 1) (int ((bottom + 15L) / 16L))
            let firstColumn = max 0 (int (left / 16L))
            let lastColumn = min (width - 1) (int ((right + 15L) / 16L))

            if firstRow <= lastRow && firstColumn <= lastColumn then
                let span = lastColumn - firstColumn + 1
                let coverage = Array.zeroCreate span
                let crossings = ResizeArray<struct (int64 * int)>()

                for row in firstRow..lastRow do
                    Array.Clear coverage

                    for sub in 0..3 do
                        let sampleY = int64 (row * 16 + sub * 4 + 2)
                        crossings.Clear()

                        for struct (x1, y1, x2, y2, direction) in edges do
                            if sampleY >= y1 && sampleY < y2 then
                                let x = x1 + (sampleY - y1) * (x2 - x1) / (y2 - y1)
                                crossings.Add(struct (x, direction))

                        if crossings.Count > 1 then
                            crossings.Sort(fun (struct (a, _)) (struct (b, _)) -> compare a b)
                            let mutable winding = 0

                            for index in 0 .. crossings.Count - 2 do
                                let struct (x, direction) = crossings[index]
                                winding <- winding + direction
                                let inside = if evenOdd then (index + 1) % 2 = 1 else winding <> 0

                                if inside then
                                    let struct (next, _) = crossings[index + 1]
                                    // Four sample columns per pixel, at 2, 6, 10 and 14 sixteenths; a sample is inside
                                    // when its centre is in [x, next).
                                    let floorDiv (a: int64) (b: int64) = if a >= 0L then a / b else -((-a + b - 1L) / b)
                                    let first = max (-(floorDiv (-(x - 2L)) 4L)) (int64 (firstColumn * 4))
                                    let last = min (floorDiv (next - 3L) 4L) (int64 ((lastColumn + 1) * 4 - 1))
                                    let mutable sample = first

                                    while sample <= last do
                                        let column = int (sample / 4L) - firstColumn
                                        coverage[column] <- coverage[column] + 1
                                        sample <- sample + 1L

                    for column in 0 .. span - 1 do
                        let covered = coverage[column]

                        if covered > 0 then
                            let alpha = (covered * opacity * color.A + 16 * 255 / 2) / (16 * 255)

                            if alpha > 0 then
                                let at = (row * width + firstColumn + column) * 4
                                let da = int pixels[at + 3]
                                let rest = da * (255 - alpha) / 255
                                let outA = alpha + rest

                                if outA > 0 then
                                    let blend source dest = (source * alpha + dest * rest + outA / 2) / outA
                                    pixels[at] <- byte (blend color.R (int pixels[at]))
                                    pixels[at + 1] <- byte (blend color.G (int pixels[at + 1]))
                                    pixels[at + 2] <- byte (blend color.B (int pixels[at + 2]))
                                    pixels[at + 3] <- byte outA
