// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Globalization
open System.Text

/// Writes a theme as the CSS custom properties that the painters reference.
[<RequireQualifiedAccess>]
module internal Theme =
    /// The font stack of archify. The SVG embeds no font data.
    let fontStack =
        "'JetBrains Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, 'DejaVu Sans Mono', 'Liberation Mono', monospace"

    let bg = "var(--bg)"
    let text = "var(--text)"
    let muted = "var(--muted)"
    let mask = "var(--mask)"
    let arrow = "var(--arrow)"
    let frame = "var(--frame)"
    let fill (family: ColorFamily) = $"var(--{family.WireValue}-fill)"
    let stroke (family: ColorFamily) = $"var(--{family.WireValue}-stroke)"
    let ink (family: ColorFamily) = $"var(--{family.WireValue}-ink)"

    /// The text colour of a family. An explicit ink is kept. A derived ink is the stroke
    /// shaded for the background and masks; on fills it must also read over the fill.
    let resolveInk onFills (palette: ThemePalette) (family: ColorFamily) =
        let colors = palette.Family family

        match colors.InkOption with
        | Some ink -> ink
        | None ->
            let ink = ColorMath.textShade colors.Stroke [ palette.Background; palette.Mask ]

            if onFills then
                ColorMath.textShade ink [ palette.Background; colors.Fill.CompositeOver palette.Background ]
            else
                ink

    let private variables onFills (palette: ThemePalette) (indent: string) (output: StringBuilder) =
        let put (name: string) (color: Color) =
            output.Append(indent).Append("--").Append(name).Append(": ").Append(color.ToCss()).Append(";\n")
            |> ignore

        put "bg" palette.Background
        put "text" palette.Text
        put "muted" palette.Muted
        put "mask" palette.Mask
        put "arrow" palette.Arrow
        put "frame" palette.Frame

        for family in ColorFamily.All do
            let colors = palette.Family family
            put $"{family.WireValue}-fill" colors.Fill
            put $"{family.WireValue}-stroke" colors.Stroke
            put $"{family.WireValue}-ink" (resolveInk onFills palette family)

    /// The style sheet of a diagram whose root has the id `id`. Selectors have zero
    /// specificity, so any host rule that matches the root overrides a variable.
    /// Auto mode writes four blocks: dark, the light preference, and the two
    /// `data-theme` overrides. A fixed mode writes one block.
    let styleSheet (id: string) onFills (options: SvgRenderOptions) =
        let theme = options.Theme
        let output = StringBuilder()

        let block (selector: string) palette =
            output.Append(selector).Append(" {\n") |> ignore
            variables onFills palette "  " output
            output.Append("}\n") |> ignore

        output.Append('\n') |> ignore

        match options.Mode with
        | ThemeMode.Dark -> block $":where(svg#{id})" theme.Dark
        | ThemeMode.Light -> block $":where(svg#{id})" theme.Light
        | ThemeMode.Auto ->
            block $":where(svg#{id})" theme.Dark

            output.Append("@media (prefers-color-scheme: light) {\n  :where(svg#").Append(id).Append(") {\n")
            |> ignore

            variables onFills theme.Light "    " output
            output.Append("  }\n}\n") |> ignore
            block $":where(svg#{id}[data-theme=\"dark\"])" theme.Dark
            block $":where(svg#{id}[data-theme=\"light\"])" theme.Light

        output.ToString()

    /// A stable 64-bit FNV-1a hash, for identifiers derived from output.
    let fingerprint (value: string) =
        let mutable hash = 0xcbf29ce484222325UL

        for character in value do
            hash <- (hash ^^^ uint64 character) * 0x100000001b3UL

        hash.ToString("x16", CultureInfo.InvariantCulture)
