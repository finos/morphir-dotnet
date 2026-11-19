// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Globalization
open Morphir.Svg

[<RequireQualifiedAccess>]
module internal SvgWriting =
    let ink = "#283650"
    let fill = "#edf3fc"
    let background = "#ffffff"

    let number (value: int) =
        value.ToString(CultureInfo.InvariantCulture)

    let private attributes (pairs: (string * string) list) =
        pairs |> List.map (fun (name, value) -> SvgAttribute.Create(name, value)) |> List.toArray

    type SceneBuilder() =
        let root = ResizeArray<SvgNode>()
        let connectors = ResizeArray<DiagramConnector>()
        let mutable current = root

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
            DiagramScene(SvgDocument.Create(attributes pairs, root), Reading.immutable connectors)

    let element (writer: SceneBuilder) name pairs body = writer.Element name pairs body

    let empty writer name attributes = element writer name attributes ignore

    let text writer x y (value: string) =
        element
            writer
            "text"
            [
                "x", number x
                "y", number y
                "fill", ink
                "text-anchor", "middle"
                "font-family", "sans-serif"
                "font-size", "14"
            ]
            (fun () -> writer.Add(SvgNode.CreateText value))

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
                "stroke", stroke
            ]

    let circle writer x y radius fill stroke =
        empty
            writer
            "circle"
            [ "cx", number x; "cy", number y; "r", number radius; "fill", fill; "stroke", stroke ]

    let path writer data attributes =
        empty writer "path" (("d", data) :: attributes)

    let point x y = number x + "," + number y

    let private marker writer (head: EdgeHead) =
        element
            writer
            "marker"
            [
                "id", "head-" + head.WireValue
                "viewBox", "-11 -6 13 12"
                "refX", "0"
                "refY", "0"
                "markerWidth", "13"
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
                            "fill", (if filled then ink else "none")
                            "stroke", ink
                            "stroke-width", "1.5"
                        ]

                match head with
                | EdgeHead.Arrow -> symbol "M-9,-4 L0,0 L-9,4 Z" true
                | EdgeHead.OpenArrow -> symbol "M-9,-4 L0,0 L-9,4" false
                | EdgeHead.Async
                | EdgeHead.HalfUpper -> symbol "M-9,-4 L0,0" false
                | EdgeHead.HalfLower -> symbol "M-9,4 L0,0" false
                | EdgeHead.HalfUpperFilled -> symbol "M-9,-4 L0,0 L-9,0 Z" true
                | EdgeHead.HalfLowerFilled -> symbol "M-9,4 L0,0 L-9,0 Z" true
                | EdgeHead.Cross -> symbol "M-8,-4 L0,4 M-8,4 L0,-4" false
                | EdgeHead.Circle -> circle writer -4 0 3 background ink
                | EdgeHead.None -> ()
            )

    let connector writer data source target dashed strokeWidth extra =
        let markerAttributes =
            [
                if source <> EdgeHead.None then
                    "marker-start", $"url(#head-{source.WireValue})"
                if target <> EdgeHead.None then
                    "marker-end", $"url(#head-{target.WireValue})"
            ]

        path
            writer
            data
            [
                yield! extra
                yield! markerAttributes
                "fill", "none"
                "stroke", ink
                "stroke-width", number strokeWidth
                if dashed then
                    "stroke-dasharray", "5 4"
            ]

    let document title width height heads render =
        let writer = SceneBuilder()
        element writer "title" [] (fun () -> writer.Add(SvgNode.CreateText title))
        rectangle writer 0 0 width height 0 background "none"

        element
            writer
            "defs"
            []
            (fun () ->
                for head in Seq.distinct heads do
                    if head <> EdgeHead.None then
                        marker writer head
            )

        render writer

        writer.Build
            [
                "role", "img"
                "aria-label", title
                "viewBox", $"0 0 {number width} {number height}"
                "width", number width
                "height", number height
            ]
