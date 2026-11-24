// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Canvas

open System
open System.Globalization
open Morphir.Diagrams
open Morphir.Diagrams.Layout

/// Formatting shared by the canvas renderers, so that equal layouts give equal bytes on every platform.
[<RequireQualifiedAccess>]
module internal CanvasWriting =
    /// A distance in CSS pixels, with at most two decimal places and no trailing zeros.
    let pixels (distance: Distance) =
        Math.Round(decimal distance.QuarterEmu / 38_100m, 2, MidpointRounding.ToEven).ToString("0.##", CultureInfo.InvariantCulture)

    /// A number with at most four decimal places and no trailing zeros.
    let number (value: float) =
        Math.Round(value, 4, MidpointRounding.ToEven).ToString("0.####", CultureInfo.InvariantCulture)

    /// A colour as #rrggbb, ignoring its opacity.
    let hex (color: Color) =
        String.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", color.R, color.G, color.B)

    /// A 32-bit FNV-1a hash of text, for seeds and nonces that derive from identifiers.
    let fnv (text: string) =
        let mutable hash = 2166136261u

        for character in text do
            hash <- (hash ^^^ uint32 character) * 16777619u

        hash

