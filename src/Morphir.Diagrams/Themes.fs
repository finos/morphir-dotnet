// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Runtime.InteropServices
open System.Text.RegularExpressions

/// An sRGB colour with an opacity from 0 to 1.
[<Sealed>]
type Color internal (r: int, g: int, b: int, opacity: decimal) =
    static let hex =
        Regex(@"\A#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})\z", RegexOptions.CultureInvariant)

    static let functional =
        Regex(
            @"\Argba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*(\d*\.?\d+)\s*)?\)\z",
            RegexOptions.CultureInvariant
        )

    static let failure text =
        Diagnostic(
            "theme/invalid-color",
            DiagnosticSeverity.Error,
            $"Cannot read colour '{text}'. Use #rgb, #rrggbb, rgb(r, g, b) or rgba(r, g, b, a).",
            None
        )

    /// Red channel, from 0 to 255.
    member _.R = r

    /// Green channel, from 0 to 255.
    member _.G = g

    /// Blue channel, from 0 to 255.
    member _.B = b

    /// Opacity, from 0 (transparent) to 1 (opaque).
    member _.Opacity = opacity

    /// CSS text: `#rrggbb` when opaque, else `rgba(r, g, b, a)`.
    member _.ToCss() =
        if opacity >= 1m then
            String.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", r, g, b)
        else
            let alpha = opacity.ToString("0.############################", CultureInfo.InvariantCulture)
            $"rgba({r}, {g}, {b}, {alpha})"

    /// The opaque colour of this colour painted over `background`. The background is treated as opaque.
    member _.CompositeOver(background: Color) =
        ArgumentNullException.ThrowIfNull background
        let a = float opacity

        let mix top bottom =
            int (Math.Round(float top * a + float bottom * (1.0 - a), MidpointRounding.AwayFromZero))

        Color(mix r background.R, mix g background.G, mix b background.B, 1m)

    member internal _.Luminance =
        let channel c =
            let v = float c / 255.0

            if v <= 0.03928 then
                v / 12.92
            else
                Math.Pow((v + 0.055) / 1.055, 2.4)

        0.2126 * channel r + 0.7152 * channel g + 0.0722 * channel b

    /// The WCAG 2 contrast ratio, from 1 to 21. Both colours are treated as opaque;
    /// composite a translucent colour with CompositeOver first.
    member this.ContrastRatio(other: Color) =
        ArgumentNullException.ThrowIfNull other
        let a, b = this.Luminance, other.Luminance
        (max a b + 0.05) / (min a b + 0.05)

    override this.ToString() = this.ToCss()

    override _.Equals(other: obj | null) =
        match other with
        | :? Color as color ->
            color.R = r && color.G = g && color.B = b && color.Opacity = opacity
        | _ -> false

    override _.GetHashCode() = HashCode.Combine(r, g, b, opacity)

    /// An opaque colour. Each channel is from 0 to 255.
    static member FromRgb(r: int, g: int, b: int) = Color.FromRgba(r, g, b, 1m)

    /// A colour with an opacity from 0 to 1. Each channel is from 0 to 255.
    static member FromRgba(r: int, g: int, b: int, opacity: decimal) =
        for value, name in [ r, nameof r; g, nameof g; b, nameof b ] do
            if value < 0 || value > 255 then
                raise (ArgumentOutOfRangeException(name, value, "A channel is from 0 to 255."))

        if opacity < 0m || opacity > 1m then
            raise (ArgumentOutOfRangeException(nameof opacity, opacity, "Opacity is from 0 to 1."))

        Color(r, g, b, opacity)

    /// Reads `#rgb`, `#rrggbb`, `rgb(r, g, b)` or `rgba(r, g, b, a)`. For F# callers.
    static member Parse(text: string | null) : Result<Color, Diagnostic> =
        match text with
        | null -> Error(failure "")
        | text when text.Length > 64 -> Error(failure (text.Substring(0, 64)))
        | text ->
            let trimmed = text.Trim()
            let channel (value: string) = Int32.Parse(value, CultureInfo.InvariantCulture)

            if hex.IsMatch trimmed then
                let digits =
                    if trimmed.Length = 4 then
                        String(trimmed.Substring(1).ToCharArray() |> Array.collect (fun c -> [| c; c |]))
                    else
                        trimmed.Substring 1

                let part start =
                    Int32.Parse(digits.Substring(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)

                Ok(Color(part 0, part 2, part 4, 1m))
            else
                let found = functional.Match trimmed

                if not found.Success then
                    Error(failure trimmed)
                else
                    let r, g, b =
                        channel found.Groups[1].Value, channel found.Groups[2].Value, channel found.Groups[3].Value

                    let opacity =
                        if found.Groups[4].Success then
                            Decimal.Parse(found.Groups[4].Value, CultureInfo.InvariantCulture)
                        else
                            1m

                    if r > 255 || g > 255 || b > 255 || opacity > 1m then
                        Error(failure trimmed)
                    else
                        Ok(Color(r, g, b, opacity))

    /// Reads colour text. For C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] color: byref<Color>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Diagnostic>
        ) =
        match Color.Parse text with
        | Ok parsed ->
            color <- parsed
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            color <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Reads a colour literal from source code. Throws on unsupported text.
    static member Create(literal: string) =
        match Color.Parse literal with
        | Ok color -> color
        | Error error -> invalidArg (nameof literal) error.Message

[<RequireQualifiedAccess>]
module internal ColorMath =
    /// Moves HSL lightness in 0.01 steps, keeping hue and saturation, until the colour
    /// has 4.5:1 contrast with each background. Returns the accent when it passes already.
    let shade (darken: bool) (accent: Color) (backgrounds: Color list) =
        let passes (c: Color) =
            backgrounds |> List.forall (fun bg -> c.ContrastRatio bg >= 4.5)

        if passes accent then
            accent
        else
            let hi = float (max accent.R (max accent.G accent.B))
            let lo = float (min accent.R (min accent.G accent.B))
            let delta = hi - lo
            let light = (hi + lo) / 2.0

            let saturation =
                if delta = 0.0 then
                    0.0
                else
                    delta / (255.0 - abs (2.0 * light - 255.0))

            let step index =
                let moved = float index * 2.55
                let l = if darken then max 0.0 (light - moved) else min 255.0 (light + moved)
                let chroma = (255.0 - abs (2.0 * l - 255.0)) * saturation
                let baseline = l - chroma / 2.0

                let channel c =
                    let offset = if delta = 0.0 then 0.0 else (float c - lo) / delta * chroma
                    int (Math.Round(baseline + offset, MidpointRounding.AwayFromZero))

                Color(channel accent.R, channel accent.G, channel accent.B, 1m)

            seq { 1..100 }
            |> Seq.map step
            |> Seq.tryFind passes
            |> Option.defaultValue (if darken then Color(0, 0, 0, 1m) else Color(255, 255, 255, 1m))

    /// Darkens an accent that is darker than its backgrounds and lightens one that is lighter.
    let textShade (accent: Color) (backgrounds: Color list) =
        let average =
            backgrounds |> List.averageBy (fun bg -> bg.Luminance)

        shade (accent.Luminance <= average) accent backgrounds

/// A colour family. Each diagram element takes the colours of one family.
[<RequireQualifiedAccess>]
type ColorFamily =
    /// Clients, actors and documents. Cyan in Archify.
    | Frontend
    /// Services and ordinary process steps. Green in Archify.
    | Backend
    /// Stores and data. Violet in Archify.
    | Database
    /// Decisions and resources. Amber in Archify.
    | Cloud
    /// Boundaries, failures and destruction. Rose in Archify.
    | Security
    /// Queues and events. Orange in Archify.
    | MessageBus
    /// Notes, frames and dotted replies. Grey in Archify.
    | External

    /// The name in CSS variables, for example `frontend` in `--frontend-stroke`.
    member this.WireValue =
        match this with
        | Frontend -> "frontend"
        | Backend -> "backend"
        | Database -> "database"
        | Cloud -> "cloud"
        | Security -> "security"
        | MessageBus -> "messagebus"
        | External -> "external"

    override this.ToString() = this.WireValue

    /// Every family, in the order of the CSS variables.
    static member All: IReadOnlyList<ColorFamily> = ColorFamilies.All

and [<AbstractClass; Sealed>] internal ColorFamilies private () =
    static let all: IReadOnlyList<ColorFamily> =
        ImmutableArray.Create(
            ColorFamily.Frontend,
            ColorFamily.Backend,
            ColorFamily.Database,
            ColorFamily.Cloud,
            ColorFamily.Security,
            ColorFamily.MessageBus,
            ColorFamily.External
        )

    static member All = all

/// The fill, stroke and text colour of one family.
[<Sealed>]
type FamilyColors internal (fill: Color, stroke: Color, ink: Color option) =
    /// Fill of shapes in this family. Usually translucent.
    member _.Fill = fill

    /// Outline and line colour.
    member _.Stroke = stroke

    /// Text colour, or null when the renderer derives it from the stroke with 4.5:1 contrast.
    member _.Ink: Color | null = Option.toObj ink

    member internal _.InkOption = ink

    override _.ToString() =
        let text = ink |> Option.map _.ToCss() |> Option.defaultValue "derived"
        $"fill {fill}, stroke {stroke}, ink {text}"

    /// Colours whose text colour the renderer derives from the stroke.
    static member Create(fill: Color, stroke: Color) =
        ArgumentNullException.ThrowIfNull fill
        ArgumentNullException.ThrowIfNull stroke
        FamilyColors(fill, stroke, None)

    /// Colours with an explicit text colour. A null ink is derived from the stroke.
    static member Create(fill: Color, stroke: Color, ink: Color | null) =
        ArgumentNullException.ThrowIfNull fill
        ArgumentNullException.ThrowIfNull stroke
        FamilyColors(fill, stroke, Option.ofObj ink)

/// The colours of one mode of a theme.
[<Sealed>]
type ThemePalette
    internal
    (background: Color, text: Color, muted: Color, mask: Color, arrow: Color, families: Map<ColorFamily, FamilyColors>) =
    /// Canvas colour. Treated as opaque.
    member _.Background = background

    /// Body text.
    member _.Text = text

    /// Secondary text and group outlines.
    member _.Muted = muted

    /// Opaque colour behind labels and participant boxes.
    member _.Mask = mask

    /// Connector lines and arrowheads.
    member _.Arrow = arrow

    /// The colours of a family.
    member _.Family(family: ColorFamily) = families[family]

    /// A copy with one family's colours replaced.
    member _.WithFamily(family: ColorFamily, colors: FamilyColors) =
        ArgumentNullException.ThrowIfNull colors
        ThemePalette(background, text, muted, mask, arrow, families.Add(family, colors))

    override _.ToString() = $"palette on {background}, text {text}"

    /// Creates a palette with every family.
    static member Create
        (
            background: Color,
            text: Color,
            muted: Color,
            mask: Color,
            arrow: Color,
            frontend: FamilyColors,
            backend: FamilyColors,
            database: FamilyColors,
            cloud: FamilyColors,
            security: FamilyColors,
            messageBus: FamilyColors,
            external: FamilyColors
        ) =
        let colors = [ background; text; muted; mask; arrow ]

        for color in colors do
            ArgumentNullException.ThrowIfNull color

        let families =
            [
                ColorFamily.Frontend, frontend
                ColorFamily.Backend, backend
                ColorFamily.Database, database
                ColorFamily.Cloud, cloud
                ColorFamily.Security, security
                ColorFamily.MessageBus, messageBus
                ColorFamily.External, external
            ]

        for _, family in families do
            ArgumentNullException.ThrowIfNull family

        ThemePalette(background, text, muted, mask, arrow, Map.ofList families)

    /// Derives a light palette from a dark one, for custom themes that supply one mode.
    /// The background is a light tint of the dark background; text and strokes are
    /// darkened until they have 4.5:1 contrast. Built-in themes do not use it.
    static member DeriveLight(dark: ThemePalette) =
        ArgumentNullException.ThrowIfNull dark
        let white = Color(255, 255, 255, 1m)
        let tint (color: Color) opacity = Color(color.R, color.G, color.B, opacity)
        let background = (tint dark.Background 0.06m).CompositeOver white
        let readable color = ColorMath.shade true color [ background; white ]

        let family (name: ColorFamily) =
            let source = dark.Family name
            let fill = tint source.Stroke 0.16m
            let stroke = ColorMath.shade true source.Stroke [ background; white; fill.CompositeOver background ]
            name, FamilyColors(fill, stroke, None)

        ThemePalette(
            background,
            readable dark.Background,
            readable dark.Muted,
            white,
            readable dark.Arrow,
            ColorFamily.All |> Seq.map family |> Map.ofSeq
        )

/// Which palette of a theme the SVG uses.
[<RequireQualifiedAccess>]
type ThemeMode =
    /// The viewer's colour-scheme preference selects the palette. Dark is the fallback,
    /// and a `data-theme="dark"` or `data-theme="light"` attribute on the root overrides it.
    | Auto
    /// Always the dark palette.
    | Dark
    /// Always the light palette.
    | Light

    /// Lowercase name of the mode.
    member this.WireValue =
        match this with
        | Auto -> "auto"
        | Dark -> "dark"
        | Light -> "light"

    override this.ToString() = this.WireValue

/// A named theme with a dark and a light palette.
[<Sealed>]
type DiagramTheme internal (name: string, dark: ThemePalette, light: ThemePalette) =
    static let validate (name: string | null) (dark: ThemePalette) (light: ThemePalette) =
        ArgumentNullException.ThrowIfNull dark
        ArgumentNullException.ThrowIfNull light

        let error code message =
            Diagnostic(code, DiagnosticSeverity.Error, message, None)

        [
            match name with
            | null -> error "theme/name" "A theme needs a name."
            | name when String.IsNullOrWhiteSpace name -> error "theme/name" "A theme needs a name."
            | _ -> ()

            for mode, palette in [ "dark", dark; "light", light ] do
                let ratio = palette.Text.ContrastRatio palette.Background

                if ratio < 4.5 then
                    let shown = ratio.ToString("0.00", CultureInfo.InvariantCulture)

                    error
                        "theme/text-contrast"
                        $"The {mode} palette's text has {shown}:1 contrast with its background; at least 4.5:1 is required."
        ]

    /// Display name.
    member _.Name = name

    /// The palette of dark mode.
    member _.Dark = dark

    /// The palette of light mode.
    member _.Light = light

    override _.ToString() = name

    /// Creates a theme, or returns why it cannot. Rejects an empty name and a
    /// palette whose text has less than 4.5:1 contrast with its background. For F# callers.
    static member Validate
        (name: string | null, dark: ThemePalette, light: ThemePalette)
        : Result<DiagramTheme, ImmutableArray<Diagnostic>> =
        match validate name dark light, name with
        | [], NonNull name -> Ok(DiagramTheme(name, dark, light))
        | diagnostics, _ -> Error(ImmutableArray.CreateRange diagnostics)

    /// Creates a theme. For C# callers. Diagnostics is empty on success.
    static member TryCreate
        (
            name: string | null,
            dark: ThemePalette,
            light: ThemePalette,
            [<Out; MaybeNullWhen(false)>] theme: byref<DiagramTheme>,
            [<Out>] diagnostics: byref<ImmutableArray<Diagnostic>>
        ) =
        match DiagramTheme.Validate(name, dark, light) with
        | Ok created ->
            theme <- created
            diagnostics <- ImmutableArray<Diagnostic>.Empty
            true
        | Error errors ->
            theme <- Unchecked.defaultof<_>
            diagnostics <- errors
            false

    /// Creates a theme from values in source code. Throws when they are rejected.
    static member Create(name: string, dark: ThemePalette, light: ThemePalette) =
        match DiagramTheme.Validate(name, dark, light) with
        | Ok theme -> theme
        | Error errors ->
            let parameter =
                if errors |> Seq.exists (fun error -> error.Code = "theme/name") then
                    nameof name
                else
                    nameof dark

            invalidArg parameter (errors |> Seq.map _.Message |> String.concat " ")

/// The built-in themes. Each has a hand-tuned dark and light palette.
[<AbstractClass; Sealed>]
type DiagramThemes private () =
    static let color (value: int) =
        Color((value >>> 16) &&& 0xff, (value >>> 8) &&& 0xff, value &&& 0xff, 1m)

    static let rgba r g b opacity = Color(r, g, b, opacity)

    static let palette background text muted mask arrow families =
        let families =
            families
            |> List.map (fun (family, fill, stroke) -> family, FamilyColors(fill, color stroke, None))
            |> Map.ofList

        ThemePalette(color background, color text, color muted, color mask, color arrow, families)

    /// Fills are the accent of each family at a fixed opacity.
    static let accents opacity background text muted mask arrow (strokes: (int * int) list) =
        let families =
            List.zip (List.ofSeq ColorFamily.All) strokes
            |> List.map (fun (family, (fill, stroke)) ->
                let source = color fill
                family, rgba source.R source.G source.B opacity, stroke
            )

        palette background text muted mask arrow families

    static let archify =
        DiagramTheme(
            "Archify",
            palette
                0x020617
                0xffffff
                0x94a3b8
                0x0f172a
                0x94a3b8
                [
                    ColorFamily.Frontend, rgba 8 51 68 0.4m, 0x22d3ee
                    ColorFamily.Backend, rgba 6 78 59 0.4m, 0x34d399
                    ColorFamily.Database, rgba 76 29 149 0.4m, 0xa78bfa
                    ColorFamily.Cloud, rgba 120 53 15 0.3m, 0xfbbf24
                    ColorFamily.Security, rgba 136 19 55 0.4m, 0xfb7185
                    ColorFamily.MessageBus, rgba 251 146 60 0.3m, 0xfb923c
                    ColorFamily.External, rgba 30 41 59 0.5m, 0x94a3b8
                ],
            palette
                0xf4f5f7
                0x111827
                0x5b6474
                0xffffff
                0x64748b
                [
                    ColorFamily.Frontend, rgba 34 211 238 0.15m, 0x0891b2
                    ColorFamily.Backend, rgba 52 211 153 0.18m, 0x059669
                    ColorFamily.Database, rgba 167 139 250 0.2m, 0x7c3aed
                    ColorFamily.Cloud, rgba 251 191 36 0.18m, 0xd97706
                    ColorFamily.Security, rgba 251 113 133 0.15m, 0xe11d48
                    ColorFamily.MessageBus, rgba 251 146 60 0.15m, 0xea580c
                    ColorFamily.External, rgba 148 163 184 0.18m, 0x64748b
                ]
        )

    // Light strokes are the dark accents darkened by hand until they pass 4.5:1
    // on the background, the white mask and their own fill.
    static let monokai =
        DiagramTheme(
            "Monokai",
            accents
                0.18m
                0x272822
                0xf8f8f2
                0x938e78
                0x1e1f1c
                0x938e78
                [
                    0x66d9ef, 0x66d9ef
                    0xa6e22e, 0xa6e22e
                    0xae81ff, 0xae81ff
                    0xe6db74, 0xe6db74
                    0xf92672, 0xf92672
                    0xfd971f, 0xfd971f
                    0x75715e, 0x75715e
                ],
            accents
                0.16m
                0xfafaf8
                0x272822
                0x75715e
                0xffffff
                0x75715e
                [
                    0x66d9ef, 0x0e768a
                    0xa6e22e, 0x547610
                    0xae81ff, 0x7a2fff
                    0xe6db74, 0x786f16
                    0xf92672, 0xc3054a
                    0xfd971f, 0xa05701
                    0x75715e, 0x676353
                ]
        )

    static let cobalt =
        DiagramTheme(
            "Cobalt",
            accents
                0.18m
                0x193549
                0xffffff
                0x8fa5b8
                0x122738
                0x8fa5b8
                [
                    0x9effff, 0x9effff
                    0x3ad900, 0x3ad900
                    0xfb94ff, 0xfb94ff
                    0xffc600, 0xffc600
                    0xff628c, 0xff628c
                    0xff9d00, 0xff9d00
                    0x0088ff, 0x0088ff
                ],
            accents
                0.16m
                0xf2f6fa
                0x193549
                0x57728a
                0xffffff
                0x57728a
                [
                    0x9effff, 0x007a7a
                    0x3ad900, 0x207800
                    0xfb94ff, 0xb600bd
                    0xffc600, 0x806300
                    0xff628c, 0xc80036
                    0xff9d00, 0x945b00
                    0x0088ff, 0x0062b8
                ]
        )

    static let solarized =
        DiagramTheme(
            "Solarized",
            accents
                0.18m
                0x002b36
                0x93a1a1
                0x839496
                0x073642
                0x839496
                [
                    0x2aa198, 0x2aa198
                    0x859900, 0x859900
                    0x6c71c4, 0x6c71c4
                    0xb58900, 0xb58900
                    0xdc322f, 0xdc322f
                    0xcb4b16, 0xcb4b16
                    0x657b83, 0x657b83
                ],
            accents
                0.16m
                0xfdf6e3
                0x073642
                0x5e737a
                0xfffdf6
                0x5e737a
                [
                    0x2aa198, 0x1d706a
                    0x859900, 0x5d6b00
                    0x6c71c4, 0x5358ba
                    0xb58900, 0x7d5f00
                    0xdc322f, 0xb9221f
                    0xcb4b16, 0xa63d12
                    0x657b83, 0x53656c
                ]
        )

    static let gruvbox =
        DiagramTheme(
            "Gruvbox",
            accents
                0.18m
                0x282828
                0xebdbb2
                0xa89984
                0x1d2021
                0xa89984
                [
                    0x8ec07c, 0x8ec07c
                    0xb8bb26, 0xb8bb26
                    0xd3869b, 0xd3869b
                    0xfabd2f, 0xfabd2f
                    0xfb4934, 0xfb4934
                    0xfe8019, 0xfe8019
                    0x928374, 0x928374
                ],
            accents
                0.16m
                0xfbf1c7
                0x3c3836
                0x766a5f
                0xf9f5d7
                0x766a5f
                [
                    0x8ec07c, 0x3b6e4f
                    0xb8bb26, 0x6b670c
                    0xd3869b, 0x8f3f71
                    0xfabd2f, 0x8c5b0f
                    0xfb4934, 0x9d0006
                    0xfe8019, 0xaf3a03
                    0x928374, 0x685d54
                ]
        )

    static let all: IReadOnlyList<DiagramTheme> =
        ImmutableArray.Create(archify, monokai, cobalt, solarized, gruvbox)

    /// The classic preset of archify v3.2 by tt-a1i (MIT), as diagrams.mbt adapts it. The default.
    static member Archify = archify

    /// Warm accents on olive charcoal, with a hand-tuned light variant.
    static member Monokai = monokai

    /// Bright accents on deep blue, with a hand-tuned light variant.
    static member Cobalt = cobalt

    /// Low-contrast cyan, olive and violet accents on blue-green or cream, with a hand-tuned light variant.
    static member Solarized = solarized

    /// Earthy, warm accents on brown-grey or parchment, with a hand-tuned light variant.
    static member Gruvbox = gruvbox

    /// Every built-in theme, Archify first.
    static member All = all

/// Presentation choices for SVG and playback output.
[<Sealed>]
type SvgRenderOptions internal (theme: DiagramTheme, mode: ThemeMode) =
    static let defaults = SvgRenderOptions(DiagramThemes.Archify, ThemeMode.Auto)

    /// The colour theme. Archify by default.
    member _.Theme = theme

    /// Which palette of the theme to use. Auto by default.
    member _.Mode = mode

    /// A copy with another theme.
    member _.WithTheme(theme: DiagramTheme) =
        ArgumentNullException.ThrowIfNull theme
        SvgRenderOptions(theme, mode)

    /// A copy with another mode.
    member _.WithMode(mode: ThemeMode) =
        ArgumentNullException.ThrowIfNull mode
        SvgRenderOptions(theme, mode)

    override _.ToString() = $"{theme.Name} theme, {mode.WireValue} mode"

    /// Archify in Auto mode.
    static member Default = defaults

    /// Options with a theme and a mode.
    static member Create(theme: DiagramTheme, mode: ThemeMode) =
        ArgumentNullException.ThrowIfNull theme
        ArgumentNullException.ThrowIfNull mode
        SvgRenderOptions(theme, mode)
