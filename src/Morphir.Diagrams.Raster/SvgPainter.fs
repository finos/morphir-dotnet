// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Raster

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq

exception internal RasterFailure of code: string * message: string

/// The inherited presentation of an element.
type internal Style =
    {
        Fill: string
        Stroke: string
        StrokeWidth: double
        Dash: string
        LineCap: string
        FillRule: string
        Opacity: double
        FillOpacity: double
        StrokeOpacity: double
        FontSize: double
        FontWeight: int
        Anchor: string
        Color: string
        Transform: Affine
    }

/// Paints the SVG that Morphir.Diagrams writes: shapes, paths, text, markers, symbols and the theme's CSS variables.
[<RequireQualifiedAccess>]
module internal SvgPainter =
    let private svg = XNamespace.Get "http://www.w3.org/2000/svg"
    let private xlink = XNamespace.Get "http://www.w3.org/1999/xlink"
    let private invariant = CultureInfo.InvariantCulture

    let private number (text: string | null) fallback =
        match text with
        | null -> fallback
        | text ->
            let trimmed = text.Trim().Replace("px", "")
            match Double.TryParse(trimmed, NumberStyles.Float, invariant) with
            | true, value when Double.IsFinite value -> value
            | _ -> fallback

    let private attribute (element: XElement) (name: string) : string | null =
        match element.Attribute(XName.Get name) with
        | null -> null
        | found -> found.Value

    /// Reads the SVG with DTDs prohibited and no resolver.
    let load (source: string) =
        let settings = XmlReaderSettings(DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null)
        use reader = XmlReader.Create(new StringReader(source), settings)
        XDocument.Load(reader, LoadOptions.None)

    /// The CSS variables of the export theme from the diagram's style sheet: the first block is the dark or fixed
    /// palette; the `data-theme="light"` block is the light palette of an adaptive diagram.
    let variables (document: XDocument) (dark: bool) =
        let style = document.Descendants(svg + "style") |> Seq.map (fun element -> element.Value) |> String.concat "\n"
        let declarations (text: string) = [ for found in Regex.Matches(text, @"--([\w-]+)\s*:\s*([^;]+);") -> found.Groups[1].Value, found.Groups[2].Value.Trim() ]
        let first = style.IndexOf '}'
        let firstBlock = if first < 0 then style else style.Substring(0, first)
        let lightAt = style.IndexOf("data-theme=\"light\"", StringComparison.Ordinal)

        let chosen =
            if dark || lightAt < 0 then firstBlock
            else
                let close = style.IndexOf('}', lightAt)
                style.Substring(lightAt, (if close < 0 then style.Length else close) - lightAt)

        dict (declarations chosen)

    let private named =
        dict [ "black", (0, 0, 0); "white", (255, 255, 255); "red", (255, 0, 0); "green", (0, 128, 0); "blue", (0, 0, 255); "gray", (128, 128, 128); "grey", (128, 128, 128) ]

    /// A colour value, or none for `none` and unknown paint.
    let rec private color (vars: IDictionary<string, string>) (gradients: IDictionary<string, string>) (current: string) (value: string) : Rgba option =
        let value = value.Trim()

        if value = "" || value = "none" || value = "transparent" then None
        elif value = "currentColor" then color vars gradients "black" current
        elif value.StartsWith("var(", StringComparison.Ordinal) then
            let inner = value.Substring(4, value.Length - 5)
            let comma = inner.IndexOf ','
            let name = (if comma >= 0 then inner.Substring(0, comma) else inner).Trim().TrimStart('-')

            match vars.TryGetValue name with
            | true, found -> color vars gradients current found
            | _ -> if comma >= 0 then color vars gradients current (inner.Substring(comma + 1)) else None
        elif value.StartsWith("url(#", StringComparison.Ordinal) then
            match gradients.TryGetValue(value.Substring(5).TrimEnd(')')) with
            | true, stop -> color vars gradients current stop
            | _ -> None
        elif value.StartsWith '#' then
            let hex = value.Substring 1
            let hex = if hex.Length = 3 then String [| hex[0]; hex[0]; hex[1]; hex[1]; hex[2]; hex[2] |] else hex

            if hex.Length = 6 || hex.Length = 8 then
                let channel index = Convert.ToInt32(hex.Substring(index, 2), 16)
                Some { R = channel 0; G = channel 2; B = channel 4; A = (if hex.Length = 8 then channel 6 else 255) }
            else
                None
        elif value.StartsWith("rgb", StringComparison.Ordinal) then
            let parts = value.Substring(value.IndexOf '(' + 1).TrimEnd(')').Split(',') |> Array.map (fun part -> part.Trim())

            if parts.Length >= 3 then
                let channel (text: string) = int (Math.Round(number text 0.0, MidpointRounding.ToEven)) |> max 0 |> min 255
                let alpha = if parts.Length > 3 then int (Math.Round(number parts[3] 1.0 * 255.0, MidpointRounding.ToEven)) else 255
                Some { R = channel parts[0]; G = channel parts[1]; B = channel parts[2]; A = alpha }
            else
                None
        else
            match named.TryGetValue value with
            | true, (r, g, b) -> Some { R = r; G = g; B = b; A = 255 }
            | _ -> None

    let private translucent =
        Regex(@"^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$", RegexOptions.CultureInvariant)

    let private reference = Regex(@"var\(\s*--([\w-]+)\s*(?:,\s*([^()]*))?\)", RegexOptions.CultureInvariant)

    /// An SVG for applications that read no CSS variables, such as office suites. Each `var()` holds its value in the
    /// light palette (the fixed palette overlaid with the `data-theme="light"` block), or its fallback, or `none`; an
    /// `rgba` paint attribute becomes a hex colour with its alpha folded into the paint's opacity attribute; and the
    /// style sheet loses its custom properties. Everything else is kept.
    let portable (source: string) : string =
        let settings = XmlReaderSettings(DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null)
        use reader = XmlReader.Create(new StringReader(source), settings)
        let document = XDocument.Load(reader, LoadOptions.PreserveWhitespace)
        let palette = Dictionary<string, string>(variables document true, StringComparer.Ordinal)

        for pair in variables document false do
            palette[pair.Key] <- pair.Value

        let rec resolve (depth: int) (value: string) =
            if depth > 16 || not (value.Contains "var(") then
                value
            else
                reference.Replace(
                    value,
                    MatchEvaluator(fun found ->
                        match palette.TryGetValue found.Groups[1].Value with
                        | true, value -> resolve (depth + 1) value
                        | _ when found.Groups[2].Success -> resolve (depth + 1) (found.Groups[2].Value.Trim())
                        | _ -> "none")
                )

        let decimalOf (text: string) = Decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)

        for element in document.Descendants() |> Seq.toList do
            for attribute in element.Attributes() |> Seq.toList do
                if attribute.Value.Contains "var(" then attribute.Value <- resolve 0 attribute.Value

            for paint, opacity in [ "fill", "fill-opacity"; "stroke", "stroke-opacity"; "stop-color", "stop-opacity" ] do
                match element.Attribute(XName.Get paint) with
                | null -> ()
                | attribute ->
                    let found = translucent.Match(attribute.Value.Trim())

                    if found.Success then
                        let channel (index: int) = int (Math.Round(decimalOf found.Groups[index].Value, MidpointRounding.ToEven)) |> max 0 |> min 255
                        attribute.Value <- String.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", channel 1, channel 2, channel 3)

                        if found.Groups[4].Success then
                            let existing = match element.Attribute(XName.Get opacity) with | null -> 1m | given -> decimalOf given.Value
                            element.SetAttributeValue(XName.Get opacity, (existing * decimalOf found.Groups[4].Value).ToString(CultureInfo.InvariantCulture))

        for style in document.Descendants(svg + "style") |> Seq.toList do
            style.Value <- Regex.Replace(resolve 0 style.Value, @"--[\w-]+\s*:[^;}]*;?", "")

        match document.Root with
        | null -> source
        | root -> root.ToString(SaveOptions.DisableFormatting)

    /// The paint values of an SVG that this painter approximates: a gradient, which it paints as its first stop, and a
    /// colour it cannot read, such as a CSS name outside its table. The light palette of an adaptive SVG is used.
    let approximations (source: string) : string list =
        let document = load source
        let vars = variables document false
        let noGradients = Dictionary<string, string>() :> IDictionary<_, _>

        [
            for element in document.Descendants() do
                for name in [ "fill"; "stroke"; "stop-color"; "color" ] do
                    match attribute element name with
                    | null -> ()
                    | value ->
                        let value = value.Trim()

                        if value.StartsWith("url(", StringComparison.Ordinal) then yield $"gradient {value}"
                        elif value = "" || value = "none" || value = "transparent" || value = "currentColor" then ()
                        elif (color vars noGradients "black" value).IsNone then yield value
        ]
        |> List.distinct

    let private transform (text: string | null) =
        match text with
        | null -> Affine.Identity
        | text ->
            let mutable result = Affine.Identity

            for found in Regex.Matches(text, @"(\w+)\s*\(([^)]*)\)") do
                let values = found.Groups[2].Value.Split([| ' '; ','; '\t' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun value -> number value 0.0)
                let at index = if index < values.Length then values[index] else 0.0

                let step =
                    match found.Groups[1].Value with
                    | "translate" -> Affine.Translate(at 0, at 1)
                    | "scale" -> Affine.Scale(at 0, (if values.Length > 1 then values[1] else at 0))
                    | "rotate" when values.Length >= 3 -> Affine.Translate(values[1], values[2]).Then(Affine.Rotate(values[0])).Then(Affine.Translate(-values[1], -values[2]))
                    | "rotate" -> Affine.Rotate(at 0)
                    | "matrix" when values.Length = 6 -> { A = values[0]; B = values[1]; C = values[2]; D = values[3]; E = values[4]; F = values[5] }
                    | _ -> Affine.Identity

                result <- result.Then step

            result

    /// Paints the document onto a canvas whose device transform maps the SVG viewport.
    let paint (document: XDocument) (canvas: Canvas) (device: Affine) (vars: IDictionary<string, string>) (work: int64 ref) (workLimit: int64) =
        let ids = Dictionary<string, XElement>(StringComparer.Ordinal)

        for element in document.Descendants() do
            match attribute element "id" with
            | null -> ()
            | id -> ids.TryAdd(id, element) |> ignore

        let gradients = Dictionary<string, string>(StringComparer.Ordinal)

        for element in document.Descendants() do
            if element.Name.LocalName.EndsWith("Gradient", StringComparison.Ordinal) then
                match attribute element "id", element.Elements() |> Seq.tryFind (fun stop -> stop.Name.LocalName = "stop") with
                | null, _
                | _, None -> ()
                | id, Some stop -> gradients[id] <- (attribute stop "stop-color" |> Option.ofObj |> Option.defaultValue "black")

        let spend amount =
            work.Value <- work.Value + amount
            if work.Value > workLimit then raise (RasterFailure("raster/work-limit", "The image needs more work than the limit allows."))

        let derive (element: XElement) (style: Style) =
            let get name fallback = match attribute element name with | null -> fallback | value -> value

            {
                Fill = get "fill" style.Fill
                Stroke = get "stroke" style.Stroke
                StrokeWidth = number (attribute element "stroke-width") style.StrokeWidth
                Dash = get "stroke-dasharray" style.Dash
                LineCap = get "stroke-linecap" style.LineCap
                FillRule = get "fill-rule" style.FillRule
                Opacity = style.Opacity * number (attribute element "opacity") 1.0
                FillOpacity = number (attribute element "fill-opacity") style.FillOpacity
                StrokeOpacity = number (attribute element "stroke-opacity") style.StrokeOpacity
                FontSize = number (attribute element "font-size") style.FontSize
                FontWeight = (match attribute element "font-weight" with | null -> style.FontWeight | "bold" -> 700 | value -> int (number value 400.0))
                Anchor = get "text-anchor" style.Anchor
                Color = get "color" style.Color
                Transform = style.Transform.Then(transform (attribute element "transform"))
            }

        let toDevice (style: Style) (points: (double * double) seq) =
            let full = device.Then style.Transform
            points |> Seq.map (fun (x, y) -> full.Apply(x, y)) |> Seq.toList

        let paintColor (style: Style) (value: string) =
            let value = if value = "currentColor" then style.Color else value
            color vars gradients style.Color value

        let fill (style: Style) (subpaths: Geometry.Subpath seq) =
            match paintColor style style.Fill with
            | Some rgba ->
                let polygons = subpaths |> Seq.filter (fun subpath -> subpath.Points.Count >= 3) |> Seq.map (fun subpath -> toDevice style subpath.Points :> seq<_>) |> Seq.toList
                spend (int64 (polygons |> List.sumBy (fun polygon -> Seq.length polygon)))
                canvas.Fill(polygons, rgba, int (Math.Round(style.Opacity * style.FillOpacity * 255.0, MidpointRounding.ToEven)), style.FillRule = "evenodd")
            | None -> ()

        let strokeShape (style: Style) (subpaths: Geometry.Subpath list) =
            match paintColor style style.Stroke with
            | Some rgba when style.StrokeWidth > 0.0 ->
                let dashes =
                    if style.Dash = "none" || style.Dash = "" then []
                    else style.Dash.Split([| ' '; ',' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun value -> number value 0.0) |> List.ofArray

                let pieces = Geometry.dash dashes subpaths
                let outline = Geometry.stroke style.StrokeWidth (style.LineCap = "round") pieces
                let polygons = outline |> Seq.map (fun polygon -> toDevice style polygon :> seq<_>) |> Seq.toList
                spend (int64 (polygons |> List.sumBy (fun polygon -> Seq.length polygon)))
                canvas.Fill(polygons, rgba, int (Math.Round(style.Opacity * style.StrokeOpacity * 255.0, MidpointRounding.ToEven)), false)
            | _ -> ()

        let rec draw (element: XElement) (style: Style) =
            let style = derive element style

            match element.Name.LocalName with
            | "svg" | "g" | "a" -> for child in element.Elements() do draw child style
            | "rect" ->
                let x, y = number (attribute element "x") 0.0, number (attribute element "y") 0.0
                let width, height = number (attribute element "width") 0.0, number (attribute element "height") 0.0
                let rx = number (attribute element "rx") (number (attribute element "ry") 0.0)
                let ry = number (attribute element "ry") rx

                if width > 0.0 && height > 0.0 then
                    let shape = [ Geometry.rectangle x y width height rx ry ]
                    fill style shape
                    strokeShape style shape
            | "circle" ->
                let r = number (attribute element "r") 0.0

                if r > 0.0 then
                    let shape = [ Geometry.ellipse (number (attribute element "cx") 0.0) (number (attribute element "cy") 0.0) r r ]
                    fill style shape
                    strokeShape style shape
            | "ellipse" ->
                let rx, ry = number (attribute element "rx") 0.0, number (attribute element "ry") 0.0

                if rx > 0.0 && ry > 0.0 then
                    let shape = [ Geometry.ellipse (number (attribute element "cx") 0.0) (number (attribute element "cy") 0.0) rx ry ]
                    fill style shape
                    strokeShape style shape
            | "line" ->
                let points = ResizeArray [ (number (attribute element "x1") 0.0, number (attribute element "y1") 0.0); (number (attribute element "x2") 0.0, number (attribute element "y2") 0.0) ]
                let shape: Geometry.Subpath list = [ { Points = points; Closed = false } ]
                strokeShape style shape
                markers element style shape
            | "polygon" | "polyline" ->
                let values = (attribute element "points" |> Option.ofObj |> Option.defaultValue "").Split([| ' '; ','; '\n'; '\t' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun value -> number value 0.0)
                let points = ResizeArray [ for index in 0 .. values.Length / 2 - 1 -> values[2 * index], values[2 * index + 1] ]
                let shape: Geometry.Subpath list = [ { Points = points; Closed = element.Name.LocalName = "polygon" } ]
                fill style shape
                strokeShape style shape
                markers element style shape
            | "path" ->
                let shape = Geometry.parse (attribute element "d" |> Option.ofObj |> Option.defaultValue "") |> List.ofSeq
                fill style shape
                strokeShape style shape
                markers element style shape
            | "text" -> text element style
            | "use" ->
                let reference = match attribute element "href" with | null -> (match element.Attribute(xlink + "href") with | null -> null | found -> found.Value) | value -> value

                match reference with
                | null -> ()
                | reference ->
                    match ids.TryGetValue(reference.TrimStart '#') with
                    | true, target ->
                        let x, y = number (attribute element "x") 0.0, number (attribute element "y") 0.0
                        let placed = { style with Transform = style.Transform.Then(Affine.Translate(x, y)) }

                        if target.Name.LocalName = "symbol" then
                            let box = (attribute target "viewBox" |> Option.ofObj |> Option.defaultValue "0 0 0 0").Split([| ' '; ',' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun value -> number value 0.0)
                            let width, height = number (attribute element "width") box[2], number (attribute element "height") box[3]
                            let scale = if box.Length = 4 && box[2] > 0.0 && box[3] > 0.0 then min (width / box[2]) (height / box[3]) else 1.0
                            let offsetX = if box.Length = 4 then (width - box[2] * scale) / 2.0 else 0.0
                            let offsetY = if box.Length = 4 then (height - box[3] * scale) / 2.0 else 0.0
                            let inner = { placed with Transform = placed.Transform.Then(Affine.Translate(offsetX, offsetY)).Then(Affine.Scale(scale, scale)).Then(Affine.Translate(-box[0], -box[1])) }
                            let inner = derive target inner

                            for child in target.Elements() do
                                draw child { inner with Transform = inner.Transform }
                        else
                            draw target placed
                    | false, _ -> ()
            | _ -> ()

        and markers (element: XElement) (style: Style) (shape: Geometry.Subpath list) =
            let place (name: string) (atStart: bool) =
                match attribute element name with
                | null -> ()
                | value when value.StartsWith("url(#", StringComparison.Ordinal) ->
                    match ids.TryGetValue(value.Substring(5).TrimEnd(')')), shape |> List.filter (fun subpath -> subpath.Points.Count >= 2) with
                    | (true, marker), (_ :: _ as lines) ->
                        let line = if atStart then lines.Head else List.last lines
                        let points = line.Points
                        let (px, py), (qx, qy) = if atStart then points[0], points[1] else points[points.Count - 1], points[points.Count - 2]
                        let angle = Trig.atan2 (if atStart then qy - py else py - qy) (if atStart then qx - px else px - qx) * 180.0 / Math.PI
                        let orient = attribute marker "orient" |> Option.ofObj |> Option.defaultValue "0"
                        let angle =
                            match orient with
                            | "auto-start-reverse" when atStart -> angle + 180.0
                            | "auto" | "auto-start-reverse" -> angle
                            | fixedAngle -> number fixedAngle 0.0

                        let box = (attribute marker "viewBox" |> Option.ofObj |> Option.defaultValue "0 0 3 3").Split([| ' '; ',' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun value -> number value 0.0)
                        let width, height = number (attribute marker "markerWidth") 3.0, number (attribute marker "markerHeight") 3.0
                        let sx, sy = (if box.Length = 4 && box[2] > 0.0 then width / box[2] else 1.0), (if box.Length = 4 && box[3] > 0.0 then height / box[3] else 1.0)
                        let scaleStroke = if attribute marker "markerUnits" = "userSpaceOnUse" then 1.0 else style.StrokeWidth

                        let markerStyle =
                            {
                                style with
                                    Fill = "black"
                                    Stroke = "none"
                                    StrokeWidth = 1.0
                                    Dash = "none"
                                    Transform =
                                        style.Transform
                                            .Then(Affine.Translate(px, py))
                                            .Then(Affine.Rotate angle)
                                            .Then(Affine.Scale(sx * scaleStroke, sy * scaleStroke))
                                            .Then(Affine.Translate(-(number (attribute marker "refX") 0.0), -(number (attribute marker "refY") 0.0)))
                            }

                        for child in marker.Elements() do
                            draw child markerStyle
                    | _ -> ()
                | _ -> ()

            place "marker-start" true
            place "marker-end" false

        and text (element: XElement) (style: Style) =
            let mutable x = number (attribute element "x") 0.0
            let mutable y = number (attribute element "y") 0.0

            let runs =
                if element.Elements(svg + "tspan") |> Seq.isEmpty then
                    [ element, style, element.Value ]
                else
                    [ for span in element.Elements(svg + "tspan") -> span, derive span style, span.Value ]

            for span, spanStyle, content in runs do
                if not (Object.ReferenceEquals(span, element)) then
                    x <- number (attribute span "x") x
                    y <- number (attribute span "y") y + number (attribute span "dy") 0.0

                let heavy = spanStyle.FontWeight >= 600

                let glyphs =
                    [
                        let mutable index = 0

                        while index < content.Length do
                            let codePoint = Char.ConvertToUtf32(content, index)
                            index <- index + (if Char.IsSurrogatePair(content, index) then 2 else 1)

                            match Fonts.glyph heavy codePoint with
                            | Some found -> found
                            | None when codePoint = 0x20 || Char.IsControl(char (min codePoint 0xFFFF)) -> { Contours = []; Advance = 600.0 }, 1000.0
                            | None ->
                                let shown = Char.ConvertFromUtf32 codePoint
                                raise (RasterFailure("raster/missing-glyph", $"No embedded font has a glyph for U+{codePoint:X4} ({shown})."))
                    ]

                let size = spanStyle.FontSize
                let advance = glyphs |> List.sumBy (fun (glyph, units) -> glyph.Advance * size / units)

                let start =
                    match spanStyle.Anchor with
                    | "middle" -> x - advance / 2.0
                    | "end" -> x - advance
                    | _ -> x

                let mutable pen = start

                for glyph, units in glyphs do
                    let scale = size / units

                    let contours: Geometry.Subpath list =
                        [ for contour in glyph.Contours -> { Points = ResizeArray(contour |> List.map (fun (gx, gy) -> pen + gx * scale, y - gy * scale)); Closed = true } ]

                    fill { spanStyle with Stroke = "none" } contours
                    pen <- pen + glyph.Advance * scale

                x <- start + advance

        let initial =
            {
                Fill = "black"
                Stroke = "none"
                StrokeWidth = 1.0
                Dash = "none"
                LineCap = "butt"
                FillRule = "nonzero"
                Opacity = 1.0
                FillOpacity = 1.0
                StrokeOpacity = 1.0
                FontSize = 16.0
                FontWeight = 400
                Anchor = "start"
                Color = "black"
                Transform = Affine.Identity
            }

        let rec visit (element: XElement) (style: Style) =
            match element.Name.LocalName with
            | "defs" | "title" | "desc" | "style" | "metadata" | "marker" | "symbol" | "clipPath" | "mask" | "filter" | "linearGradient" | "radialGradient" | "script" -> ()
            | "svg" | "g" | "a" ->
                let style = derive element style
                for child in element.Elements() do visit child style
            | _ -> draw element style

        match document.Root with
        | null -> ()
        | root ->
            // The root's own transform attributes do not apply; its viewport maps through the device transform.
            for child in root.Elements() do
                visit child (derive root initial)

    /// The root's viewBox (or width and height) as x, y, width and height.
    let viewBox (document: XDocument) =
        match document.Root with
        | null -> 0.0, 0.0, 0.0, 0.0
        | root ->
            match attribute root "viewBox" with
            | null -> 0.0, 0.0, number (attribute root "width") 0.0, number (attribute root "height") 0.0
            | box ->
                let values = box.Split([| ' '; ',' |], StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun value -> number value 0.0)
                if values.Length = 4 then values[0], values[1], values[2], values[3] else 0.0, 0.0, 0.0, 0.0
