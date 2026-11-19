// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Globalization
open Morphir.Svg

[<RequireQualifiedAccess>]
module internal SvgWriting =
    let number (value: int) =
        value.ToString(CultureInfo.InvariantCulture)

    let private attributes (pairs: (string * string) list) =
        pairs |> List.map (fun (name, value) -> SvgAttribute.Create(name, value)) |> List.toArray

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

        member _.Build pairs =
            DiagramScene(SvgDocument.Create(attributes pairs, root), Reading.immutable connectors, options)

    let element (writer: SceneBuilder) name pairs body = writer.Element name pairs body

    let empty writer name attributes = element writer name attributes ignore

    /// Centred monospace text with its baseline at `y`.
    let label writer x y size weight fill (value: string) =
        element
            writer
            "text"
            [
                "x", number x
                "y", number y
                "text-anchor", "middle"
                "font-family", Theme.fontStack
                "font-size", number size
                "font-weight", number weight
                "fill", fill
            ]
            (fun () -> writer.Add(SvgNode.CreateText value))

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
        let width = textWidth size value + 12

        empty
            writer
            "rect"
            [
                "class", "mask"
                "x", number (x - width / 2)
                "y", number (y - size - 3)
                "width", number width
                "height", number (size + 9)
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
            element writer "title" [] (fun () -> writer.Add(SvgNode.CreateText title))

            element
                writer
                "defs"
                []
                (fun () ->
                    element writer "style" [] (fun () -> writer.Add(SvgNode.CreateText style))

                    for head, stroke in heads do
                        marker writer head stroke
                )

            rectangle writer 0 0 width height 0 Theme.bg "none"
            render writer

            writer.Build
                [
                    "id", id
                    "role", "img"
                    "aria-label", title
                    "data-diagram", kind
                    "viewBox", $"0 0 {number width} {number height}"
                    "width", number width
                    "height", number height
                ]

        let provisional = (draw kind).Document.Render()
        draw $"{kind}-{Theme.fingerprint provisional}"
