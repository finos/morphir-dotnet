// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Globalization
open System.Text

/// The classic preset of archify v3.2 by tt-a1i (MIT), as diagrams.mbt adapts it.
[<RequireQualifiedAccess>]
module internal Theme =
    /// An sRGB colour. Alpha is the opacity in percent.
    [<Struct>]
    type Color =
        {
            R: int
            G: int
            B: int
            Alpha: int
        }

    let hex (value: int) =
        {
            R = (value >>> 16) &&& 0xff
            G = (value >>> 8) &&& 0xff
            B = value &&& 0xff
            Alpha = 100
        }

    let rgba r g b alpha =
        {
            R = r
            G = g
            B = b
            Alpha = alpha
        }

    let private opacity percent =
        if percent <= 0 then "0"
        elif percent >= 100 then "1"
        elif percent % 10 = 0 then $"0.{percent / 10}"
        elif percent < 10 then $"0.0{percent}"
        else $"0.{percent}"

    let css color =
        if color.Alpha >= 100 then
            String.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", color.R, color.G, color.B)
        else
            $"rgba({color.R}, {color.G}, {color.B}, {opacity color.Alpha})"

    let private round (value: float) =
        int (Math.Round(value, MidpointRounding.AwayFromZero))

    /// The opaque colour of `top` painted over the opaque colour `bottom`.
    let over top bottom =
        let a = float top.Alpha / 100.0
        let mix t u = round (float t * a + float u * (1.0 - a))

        {
            R = mix top.R bottom.R
            G = mix top.G bottom.G
            B = mix top.B bottom.B
            Alpha = 100
        }

    let private luminance color =
        let channel c =
            let v = float c / 255.0

            if v <= 0.03928 then
                v / 12.92
            else
                Math.Pow((v + 0.055) / 1.055, 2.4)

        0.2126 * channel color.R + 0.7152 * channel color.G + 0.0722 * channel color.B

    /// The WCAG 2 contrast ratio of two opaque colours.
    let contrast a b =
        let la, lb = luminance a, luminance b
        (max la lb + 0.05) / (min la lb + 0.05)

    /// Lowers HSL lightness in 0.01 steps, keeping hue and saturation, until the
    /// colour has 4.5:1 contrast against each background.
    let textShade accent (backgrounds: Color list) =
        let passes c =
            backgrounds |> List.forall (fun bg -> contrast c bg >= 4.5)

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

            let shade step =
                let l = max 0.0 (light - float step * 2.55)
                let chroma = (255.0 - abs (2.0 * l - 255.0)) * saturation
                let baseline = l - chroma / 2.0

                let channel c =
                    let offset = if delta = 0.0 then 0.0 else (float c - lo) / delta * chroma
                    round (baseline + offset)

                rgba (channel accent.R) (channel accent.G) (channel accent.B) 100

            seq { 1..100 }
            |> Seq.map shade
            |> Seq.tryFind passes
            |> Option.defaultValue (hex 0)

    /// A colour family of the classic preset.
    type Family =
        | Frontend
        | Backend
        | Database
        | Cloud
        | Security
        | MessageBus
        | External

        member this.Name =
            match this with
            | Frontend -> "frontend"
            | Backend -> "backend"
            | Database -> "database"
            | Cloud -> "cloud"
            | Security -> "security"
            | MessageBus -> "messagebus"
            | External -> "external"

    let families =
        [ Frontend; Backend; Database; Cloud; Security; MessageBus; External ]

    type FamilyColors =
        {
            Fill: Color
            Stroke: Color
            Ink: Color
        }

    type Palette =
        {
            Background: Color
            Text: Color
            Muted: Color
            Mask: Color
            Arrow: Color
            Families: Map<Family, FamilyColors>
        }

    /// Family ink is the stroke, shaded until it reads on the background and on masks.
    let private palette background text muted mask arrow (colors: (Family * Color * int) list) =
        let background, mask = hex background, hex mask

        let family (name, fill, stroke) =
            let stroke = hex stroke

            name,
            {
                Fill = fill
                Stroke = stroke
                Ink = textShade stroke [ background; mask ]
            }

        {
            Background = background
            Text = hex text
            Muted = hex muted
            Mask = mask
            Arrow = hex arrow
            Families = colors |> List.map family |> Map.ofList
        }

    let dark =
        palette
            0x020617
            0xffffff
            0x94a3b8
            0x0f172a
            0x94a3b8
            [
                Frontend, rgba 8 51 68 40, 0x22d3ee
                Backend, rgba 6 78 59 40, 0x34d399
                Database, rgba 76 29 149 40, 0xa78bfa
                Cloud, rgba 120 53 15 30, 0xfbbf24
                Security, rgba 136 19 55 40, 0xfb7185
                MessageBus, rgba 251 146 60 30, 0xfb923c
                External, rgba 30 41 59 50, 0x94a3b8
            ]

    let light =
        palette
            0xf4f5f7
            0x111827
            0x5b6474
            0xffffff
            0x64748b
            [
                Frontend, rgba 34 211 238 15, 0x0891b2
                Backend, rgba 52 211 153 18, 0x059669
                Database, rgba 167 139 250 20, 0x7c3aed
                Cloud, rgba 251 191 36 18, 0xd97706
                Security, rgba 251 113 133 15, 0xe11d48
                MessageBus, rgba 251 146 60 15, 0xea580c
                External, rgba 148 163 184 18, 0x64748b
            ]

    /// Node labels sit directly on a translucent family fill, so their ink must
    /// also pass over that fill composed on the background.
    let onFills (p: Palette) =
        let shade _ colors =
            { colors with
                Ink = textShade colors.Ink [ p.Background; over colors.Fill p.Background ]
            }

        { p with Families = Map.map shade p.Families }

    /// The font stack of archify. The SVG embeds no font data.
    let fontStack =
        "'JetBrains Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, 'DejaVu Sans Mono', 'Liberation Mono', monospace"

    let bg = "var(--bg)"
    let text = "var(--text)"
    let muted = "var(--muted)"
    let mask = "var(--mask)"
    let arrow = "var(--arrow)"
    let fill (family: Family) = $"var(--{family.Name}-fill)"
    let stroke (family: Family) = $"var(--{family.Name}-stroke)"
    let ink (family: Family) = $"var(--{family.Name}-ink)"

    let private variables (p: Palette) (indent: string) (output: StringBuilder) =
        let put (name: string) color =
            output.Append(indent).Append("--").Append(name).Append(": ").Append(css color).Append(";\n")
            |> ignore

        put "bg" p.Background
        put "text" p.Text
        put "muted" p.Muted
        put "mask" p.Mask
        put "arrow" p.Arrow

        for family in families do
            let colors = p.Families[family]
            put $"{family.Name}-fill" colors.Fill
            put $"{family.Name}-stroke" colors.Stroke
            put $"{family.Name}-ink" colors.Ink

    /// Dark is the default. A light viewer preference selects light, and a
    /// `data-theme` attribute on the root overrides both.
    let styleSheet (selector: string) (dark: Palette) (light: Palette) =
        let output = StringBuilder()
        output.Append('\n').Append(selector).Append(" {\n") |> ignore
        variables dark "  " output

        output.Append("}\n@media (prefers-color-scheme: light) {\n  ").Append(selector).Append(" {\n")
        |> ignore

        variables light "    " output
        output.Append("  }\n}\n").Append(selector).Append("[data-theme=\"dark\"] {\n") |> ignore
        variables dark "  " output
        output.Append("}\n").Append(selector).Append("[data-theme=\"light\"] {\n") |> ignore
        variables light "  " output
        output.Append("}\n").ToString()

    /// A stable 64-bit FNV-1a hash, for identifiers derived from output.
    let fingerprint (value: string) =
        let mutable hash = 0xcbf29ce484222325UL

        for character in value do
            hash <- (hash ^^^ uint64 character) * 0x100000001b3UL

        hash.ToString("x16", CultureInfo.InvariantCulture)
