// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Layout

open System
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Runtime.InteropServices
open Morphir.Diagrams

/// Exact conversions between quarter English Metric Units and decimal values.
module internal Units =
    /// Quarter EMU per CSS pixel: 9,525 EMU per pixel, four quarters each.
    let PerPixel = 38_100m

    /// Quarter EMU per point.
    let PerPoint = 50_800m

    /// Quarter EMU per inch.
    let PerInch = 3_657_600m

    /// Quarter EMU per centimetre.
    let PerCentimetre = 1_440_000m

    /// Quarter EMU per millimetre.
    let PerMillimetre = 144_000m

    /// A decimal rounded half to even to a whole number of quarter EMU, or an argument error when it does not fit.
    let round (name: string) (value: decimal) =
        let rounded = Math.Round(value, MidpointRounding.ToEven)

        if rounded > decimal Int64.MaxValue || rounded < decimal Int64.MinValue then
            invalidArg name $"{value} quarter EMU is outside the range of a distance."

        int64 rounded

    /// A finite double as a decimal of at most 15 significant digits, so that 100.02 is 100.02.
    let exact (name: string) (value: float) =
        if not (Double.IsFinite value) then invalidArg name "A distance must be a finite number."

        try
            decimal value
        with :? OverflowException ->
            invalidArg name $"{value} is outside the range of a distance."

    let scaled (name: string) (value: float) (factor: decimal) =
        let value = exact name value

        try
            round name (value * factor)
        with :? OverflowException ->
            invalidArg name $"{value} is outside the range of a distance."

/// A length or coordinate, stored as a whole number of quarter English Metric Units (3,657,600 per inch). It is
/// exact for CSS pixels with up to two decimal places (381 units per hundredth), points, centimetres, millimetres,
/// inches and whole EMU. Arithmetic is checked, and scaling rounds half to even. The default value is zero.
[<Struct; CustomEquality; CustomComparison>]
type Distance =
    private
        { units: int64 }

    /// The distance as a whole number of quarter EMU.
    member this.QuarterEmu = this.units

    /// The distance in CSS pixels.
    member this.ToPixels() = float this.units / 38_100.0

    /// The distance in points.
    member this.ToPoints() = float this.units / 50_800.0

    /// The distance in inches.
    member this.ToInches() = float this.units / 3_657_600.0

    /// The distance in centimetres.
    member this.ToCentimetres() = float this.units / 1_440_000.0

    /// The distance in millimetres.
    member this.ToMillimetres() = float this.units / 144_000.0

    /// The distance in whole EMU, rounded half to even, so it differs from the distance by at most half an EMU.
    member this.ToEmu() =
        let quotient = this.units / 4L
        let remainder = this.units % 4L

        match abs remainder with
        | 0L
        | 1L -> quotient
        | 3L -> quotient + int64 (sign remainder)
        | _ -> if quotient % 2L = 0L then quotient else quotient + int64 (sign remainder)

    /// The distance multiplied by a finite factor, rounded half to even.
    member this.Scale(factor: float) =
        let factor = Units.exact (nameof factor) factor

        try
            { units = Units.round (nameof factor) (decimal this.units * factor) }
        with :? OverflowException ->
            invalidArg (nameof factor) "The scaled distance is outside the range of a distance."

    /// The distance in CSS pixels, with up to five decimal places and the unit, such as 1.5px.
    override this.ToString() =
        this.ToPixels().ToString("0.#####", CultureInfo.InvariantCulture) + "px"

    override this.Equals(other: obj | null) =
        match other with
        | :? Distance as distance -> distance.units = this.units
        | _ -> false

    override this.GetHashCode() = this.units.GetHashCode()

    /// Whether two distances are equal. C# callers compare distances with this, not with ==.
    member this.Equals(other: Distance) = other.units = this.units

    /// Less than zero when this distance is shorter, zero when equal, more than zero when longer.
    member this.CompareTo(other: Distance) = compare this.units other.units

    interface IEquatable<Distance> with
        member this.Equals(other: Distance) = other.units = this.units

    interface IComparable<Distance> with
        member this.CompareTo(other: Distance) = compare this.units other.units

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | :? Distance as distance -> compare this.units distance.units
            | null -> 1
            | _ -> invalidArg (nameof other) "Expected a Distance."

    /// Zero.
    static member Zero = { units = 0L }

    /// A distance of a whole number of quarter EMU.
    static member FromQuarterEmu(units: int64) = { units = units }

    /// A distance of a whole number of EMU. Throws an overflow error outside the range.
    static member FromEmu(emu: int64) = { units = Checked.(*) emu 4L }

    /// A distance in CSS pixels. Throws an argument error for a value that is not finite or does not fit.
    static member FromPixels(pixels: float) = { units = Units.scaled (nameof pixels) pixels Units.PerPixel }

    /// A distance in points.
    static member FromPoints(points: float) = { units = Units.scaled (nameof points) points Units.PerPoint }

    /// A distance in inches.
    static member FromInches(inches: float) = { units = Units.scaled (nameof inches) inches Units.PerInch }

    /// A distance in centimetres.
    static member FromCentimetres(centimetres: float) =
        { units = Units.scaled (nameof centimetres) centimetres Units.PerCentimetre }

    /// A distance in millimetres.
    static member FromMillimetres(millimetres: float) =
        { units = Units.scaled (nameof millimetres) millimetres Units.PerMillimetre }

    /// A distance in CSS pixels given as SVG number text, such as 100.02. Exact for any decimal text.
    static member internal FromPixelText(text: string) =
        { units = Units.round (nameof text) (Decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) * Units.PerPixel) }

    /// The larger of two distances.
    static member Max(first: Distance, second: Distance) = if first.units >= second.units then first else second

    /// The smaller of two distances.
    static member Min(first: Distance, second: Distance) = if first.units <= second.units then first else second

    /// The sum. Throws an overflow error outside the range.
    static member (+)(first: Distance, second: Distance) = { units = Checked.(+) first.units second.units }

    /// The difference. Throws an overflow error outside the range.
    static member (-)(first: Distance, second: Distance) = { units = Checked.(-) first.units second.units }

    /// The negation.
    static member (~-)(distance: Distance) = { units = Checked.(~-) distance.units }

    /// The distance multiplied by a factor, rounded half to even.
    static member (*)(distance: Distance, factor: float) = distance.Scale factor

    /// The ratio of two distances.
    static member (/)(first: Distance, second: Distance) = float first.units / float second.units

/// A number from 0 to 1 inclusive, such as an anchor across a box or along a line.
[<Sealed>]
type Fraction private (value: float) =
    static let zero = Fraction 0.0
    static let half = Fraction 0.5
    static let one = Fraction 1.0

    /// The number, from 0 to 1.
    member _.Value = value

    override _.ToString() = value.ToString("R", CultureInfo.InvariantCulture)

    override _.Equals(other: obj | null) =
        match other with
        | :? Fraction as fraction -> fraction.Value = value
        | _ -> false

    override _.GetHashCode() = value.GetHashCode()

    /// Zero.
    static member Zero = zero

    /// One half.
    static member Half = half

    /// One.
    static member One = one

    /// A fraction, or the reason the number is not one. For F# callers.
    static member Parse(value: float) : Result<Fraction, Diagnostic> =
        if Double.IsFinite value && value >= 0.0 && value <= 1.0 then
            Ok(Fraction(if value = 0.0 then 0.0 else value))
        else
            Error(Diagnostic("layout/fraction-out-of-range", DiagnosticSeverity.Error, $"A fraction must be from 0 to 1; {value} is not.", None))

    /// A fraction for C# callers. Returns false, with the failure, for a number outside 0 to 1.
    static member TryParse
        (
            value: float,
            [<Out; MaybeNullWhen(false)>] result: byref<Fraction>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Diagnostic>
        ) =
        match Fraction.Parse value with
        | Ok fraction ->
            result <- fraction
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// A fraction from a literal in source code. Throws an argument error for a number outside 0 to 1.
    static member Create(literal: float) =
        match Fraction.Parse literal with
        | Ok fraction -> fraction
        | Error error -> invalidArg (nameof literal) error.Message

/// A point given as fractions across a box: 0 is the left or top edge, 1 the right or bottom edge.
[<Sealed>]
type FractionPoint private (x: Fraction, y: Fraction) =
    /// The fraction across the box's width.
    member _.X = x

    /// The fraction down the box's height.
    member _.Y = y

    override _.ToString() = $"({x}, {y})"

    override _.Equals(other: obj | null) =
        match other with
        | :? FractionPoint as point -> point.X.Equals x && point.Y.Equals y
        | _ -> false

    override _.GetHashCode() = HashCode.Combine(x, y)

    /// The centre of a box.
    static member Centre = FractionPoint(Fraction.Half, Fraction.Half)

    /// A point from two fractions.
    static member Create(x: Fraction, y: Fraction) =
        ArgumentNullException.ThrowIfNull x
        ArgumentNullException.ThrowIfNull y
        FractionPoint(x, y)

/// A finite factor greater than 0.
[<Sealed>]
type Scale private (value: float) =
    static let one = Scale 1.0

    /// The factor.
    member _.Value = value

    override _.ToString() = value.ToString("R", CultureInfo.InvariantCulture)

    override _.Equals(other: obj | null) =
        match other with
        | :? Scale as scale -> scale.Value = value
        | _ -> false

    override _.GetHashCode() = value.GetHashCode()

    /// The factor 1.
    static member One = one

    /// A scale, or the reason the number is not one. For F# callers.
    static member Parse(value: float) : Result<Scale, Diagnostic> =
        if Double.IsFinite value && value > 0.0 then
            Ok(Scale value)
        else
            Error(Diagnostic("layout/scale-out-of-range", DiagnosticSeverity.Error, $"A scale must be finite and greater than 0; {value} is not.", None))

    /// A scale for C# callers. Returns false, with the failure, for a number that is not finite or not above 0.
    static member TryParse
        (
            value: float,
            [<Out; MaybeNullWhen(false)>] result: byref<Scale>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Diagnostic>
        ) =
        match Scale.Parse value with
        | Ok scale ->
            result <- scale
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// A scale from a literal in source code. Throws an argument error for an invalid number.
    static member Create(literal: float) =
        match Scale.Parse literal with
        | Ok scale -> scale
        | Error error -> invalidArg (nameof literal) error.Message
