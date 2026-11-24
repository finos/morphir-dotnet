// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Globalization
open System.Text
open System.Xml
open Morphir.Svg

[<RequireQualifiedAccess>]
module internal SvgWriting =
    let number (value: int) =
        value.ToString(CultureInfo.InvariantCulture)

    /// Diagram text with each character that XML cannot hold, a control character or an unpaired
    /// surrogate, replaced by U+FFFD. Text that XML can hold is returned unchanged.
    let legible (value: string) =
        let rec valid index =
            if index >= value.Length then true
            elif Char.IsHighSurrogate value[index] && index + 1 < value.Length && Char.IsLowSurrogate value[index + 1] then valid (index + 2)
            elif XmlConvert.IsXmlChar value[index] then valid (index + 1)
            else false

        if valid 0 then
            value
        else
            let copy = StringBuilder(value.Length)
            let mutable index = 0

            while index < value.Length do
                if Char.IsHighSurrogate value[index] && index + 1 < value.Length && Char.IsLowSurrogate value[index + 1] then
                    copy.Append(value, index, 2) |> ignore
                    index <- index + 2
                else
                    copy.Append(if XmlConvert.IsXmlChar value[index] then value[index] else '\uFFFD') |> ignore
                    index <- index + 1

            copy.ToString()

    /// A text node of diagram text, legible in XML.
    let text (value: string) = SvgNode.CreateText(legible value)

    let private attributes (pairs: (string * string) list) =
        pairs |> List.map (fun (name, value) -> SvgAttribute.Create(name, legible value)) |> List.toArray

    type SceneBuilder(prefix: string, options: SvgRenderOptions) =
        let root = ResizeArray<SvgNode>()
        let connectors = ResizeArray<DiagramConnector>()
        let mutable current = root

        /// Namespace of the generated identifiers of this diagram.
        member _.Prefix = prefix

        member _.Add node = current.Add node

        member _.Element (name: string) pairs body =
            let enclosing = current
            let children = ResizeArray<SvgNode>()
            current <- children

            try
                body ()
            finally
                current <- enclosing

            enclosing.Add(SvgNode.CreateElement(name, attributes pairs, children))

        member _.BindConnector source target data reversed =
            connectors.Add(DiagramConnector(connectors.Count, source, target, data, reversed))

        member _.Build(pairs, metadata: SvgNode list option) =
            let document = SvgDocument.Create(attributes pairs, root)

            let document =
                match metadata with
                | Some content -> document.WithMetadata(List.toArray content)
                | None -> document

            DiagramScene(document, Reading.immutable connectors, options, ImmutableArray.Empty, [])

    let element (writer: SceneBuilder) name pairs body = writer.Element name pairs body

    let empty writer name attributes = element writer name attributes ignore

    /// Centred monospace text with its baseline at `y`. Text with line breaks becomes one line per row, centred
    /// vertically on `y`.
    let label writer x y size weight fill (value: string) =
        let lines = value.Split '\n'
        let step = size + 3

        element
            writer
            "text"
            [
                "x", number x
                "y", number (y - (lines.Length - 1) * step / 2)
                "text-anchor", "middle"
                "font-family", Theme.fontStack
                "font-size", number size
                "font-weight", number weight
                "fill", fill
            ]
            (fun () ->
                if lines.Length = 1 then
                    writer.Add(text value)
                else
                    lines
                    |> Array.iteri (fun index line ->
                        element
                            writer
                            "tspan"
                            [ "x", number x; "dy", (if index = 0 then "0" else number step) ]
                            (fun () -> writer.Add(text line))))

    /// The estimated advance of monospace text, in whole pixels.
    let textWidth size (value: string) =
        (value.Length * size * 62 + 99) / 100

    let rectangle writer x y width height radius fill stroke =
        empty
            writer
            "rect"
            [
                "x", number x
                "y", number y
                "width", number width
                "height", number height
                "rx", number radius
                "fill", fill
                if stroke <> "none" then
                    "stroke", stroke
                    "stroke-width", "1.5"
            ]

    /// An opaque chip behind a label, so that lines and tints do not reduce its contrast.
    let chip writer x y size fill (value: string) =
        let lines = value.Split '\n'
        let width = (lines |> Array.map (textWidth size) |> Array.max) + 12
        let extra = (lines.Length - 1) * (size + 3)

        empty
            writer
            "rect"
            [
                "class", "mask"
                "x", number (x - width / 2)
                "y", number (y - size - 3 - extra / 2)
                "width", number width
                "height", number (size + 9 + extra)
                "rx", "3"
                "fill", fill
            ]

    let circle writer x y radius fill stroke =
        empty
            writer
            "circle"
            [
                "cx", number x
                "cy", number y
                "r", number radius
                "fill", fill
                "stroke", stroke
                "stroke-width", "1.5"
            ]

    let path writer data attributes =
        empty writer "path" (("d", data) :: attributes)

    let point x y = number x + "," + number y

    let private markerId (writer: SceneBuilder) (head: EdgeHead) (stroke: string) =
        let paint = stroke.Replace("var(--", "").Replace(")", "").Replace("#", "")
        $"{writer.Prefix}-{head.WireValue}-{paint}"

    let private marker writer (head: EdgeHead) stroke =
        element
            writer
            "marker"
            [
                "id", markerId writer head stroke
                "viewBox", "-10 -6 16 12"
                "refX", "0"
                "refY", "0"
                "markerWidth", "16"
                "markerHeight", "12"
                "markerUnits", "userSpaceOnUse"
                "orient", "auto-start-reverse"
            ]
            (fun () ->
                let symbol geometry filled =
                    path
                        writer
                        geometry
                        [
                            "fill", (if filled then stroke else "none")
                            "stroke", stroke
                            "stroke-width", "1.4"
                        ]

                match head with
                | EdgeHead.Arrow -> symbol "M-9,-4 L0,0 L-9,4 Z" true
                | EdgeHead.OpenArrow -> symbol "M-9,-4 L0,0 L-9,4" false
                | EdgeHead.Async
                | EdgeHead.HalfUpper -> symbol "M-9,-4 L0,0" false
                | EdgeHead.HalfLower -> symbol "M-9,4 L0,0" false
                | EdgeHead.HalfUpperFilled -> symbol "M-9,-4 L0,0 L-9,0 Z" true
                | EdgeHead.HalfLowerFilled -> symbol "M-9,4 L0,0 L-9,0 Z" true
                | EdgeHead.Cross -> symbol "M-4,-4 L4,4 M-4,4 L4,-4" false
                | EdgeHead.Circle -> symbol "M-8,0 A4,4 0 1 0 0,0 A4,4 0 1 0 -8,0 Z" true
                | EdgeHead.None -> ()
            )

    /// A connector path whose heads take the colour of its stroke.
    let connector writer data source target stroke dash (strokeWidth: string) extra =
        path
            writer
            data
            [
                yield! extra
                if source <> EdgeHead.None then
                    "marker-start", $"url(#{markerId writer source stroke})"
                if target <> EdgeHead.None then
                    "marker-end", $"url(#{markerId writer target stroke})"
                "fill", "none"
                "stroke", stroke
                "stroke-width", strokeWidth
                match dash with
                | Some pattern -> "stroke-dasharray", pattern
                | None -> ()
            ]

    /// Writes a themed diagram. `heads` lists each head with the stroke it is painted in.
    /// Labels on fills get inks that also read over those fills. Generated identifiers
    /// derive from the output, so that inline copies of different diagrams do not share
    /// markers or styles.
    let document
        kind
        title
        width
        height
        (heads: seq<EdgeHead * string>)
        (options: SvgRenderOptions)
        (metadata: SvgNode list option)
        onFills
        (render: SceneBuilder -> unit)
        =
        let heads =
            heads
            |> Seq.filter (fun (head, _) -> head <> EdgeHead.None)
            |> Seq.distinct
            |> Seq.toArray

        let draw prefix =
            let writer = SceneBuilder(prefix, options)
            let id = prefix + "-diagram"
            let style = Theme.styleSheet id onFills options
            element writer "title" [] (fun () -> writer.Add(text title))

            element
                writer
                "defs"
                []
                (fun () ->
                    element writer "style" [] (fun () -> writer.Add(text style))

                    for head, stroke in heads do
                        marker writer head stroke
                )

            rectangle writer 0 0 width height 0 Theme.bg "none"
            render writer

            writer.Build(
                [
                    "id", id
                    "role", "img"
                    "aria-label", title
                    "data-diagram", kind
                    "viewBox", $"0 0 {number width} {number height}"
                    "width", number width
                    "height", number height
                ],
                metadata
            )

        let provisional = (draw kind).Document.Render()
        draw $"{kind}-{Theme.fingerprint provisional}"
