// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic

/// Lays out and paints data trees: each object or array is a table of rows, and a row that holds a container links
/// to that container's table to its right. Highlights restyle rows; they never move them.
[<RequireQualifiedAccess>]
module internal DataTreeSvg =
    let private size = 13
    let private lineHeight = 17
    let private padding = 10
    let private n = SvgWriting.number
    let private p = SvgWriting.point

    /// Lines of at most `width` characters, broken at spaces where possible; nothing is cut off.
    let private wrap width (text: string) =
        [
            for paragraph in text.Split '\n' do
                let mutable line = ""

                for word in paragraph.Split ' ' do
                    let mutable word = word

                    while word.Length > width do
                        if line <> "" then
                            yield line
                            line <- ""

                        yield word.Substring(0, width)
                        word <- word.Substring width

                    if line = "" then line <- word
                    elif line.Length + 1 + word.Length <= width then line <- line + " " + word
                    else
                        yield line
                        line <- word

                yield line
        ]

    type private Row =
        {
            Id: string
            Key: string list
            Value: string list
            Data: DataValue
        }

    type private Table =
        {
            Id: string
            Value: DataValue
            Rows: Row list
            KeyWidth: int
            ValueWidth: int
            Height: int
        }

    let private shown (value: DataValue) =
        match value.Kind with
        | DataValueKind.Object when value.Members.IsEmpty -> "{ }"
        | DataValueKind.Array when value.Items.IsEmpty -> "[ ]"
        | DataValueKind.Object
        | DataValueKind.Array -> ""
        | DataValueKind.String when value.Text = "" -> ""
        | _ -> value.Text

    let private rowHeight (row: Row) = max row.Key.Length row.Value.Length * lineHeight + 9

    let private ink (value: DataValue) =
        match value.Kind with
        | DataValueKind.Number -> Theme.stroke ColorFamily.Cloud
        | DataValueKind.Boolean
        | DataValueKind.Null -> Theme.stroke ColorFamily.MessageBus
        | DataValueKind.Object
        | DataValueKind.Array -> Theme.muted
        | DataValueKind.String -> Theme.text

    let private isContainer (value: DataValue) =
        (value.Kind = DataValueKind.Object && not value.Members.IsEmpty) || (value.Kind = DataValueKind.Array && not value.Items.IsEmpty)

    let render (options: SvgRenderOptions) (metadata: Morphir.Svg.SvgNode list option) (model: DataTreeDiagram) =
        let ids = Dictionary<DataValue, string>(HashIdentity.Reference)

        let occurrences = model.Occurrences

        for id, _, value in occurrences do
            ids[value] <- id

        let highlighted = HashSet<string>(model.HighlightedIds, StringComparer.Ordinal)

        let table (value: DataValue) =
            let entries =
                match value.Kind with
                | DataValueKind.Object -> value.Members |> Seq.map (fun item -> Some item.Key, item.Value) |> Seq.toList
                | DataValueKind.Array -> value.Items |> Seq.map (fun item -> None, item) |> Seq.toList
                | _ -> []
                // A scalar or an empty container shows itself in one row.
                |> fun entries -> if entries.IsEmpty then [ None, value ] else entries

            let rows =
                entries
                |> List.map (fun (key, child) ->
                    {
                        Id = ids[child]
                        Key = key |> Option.map (wrap 28) |> Option.defaultValue []
                        Value = wrap 40 (shown child)
                        Data = child
                    })

            let widthOf lines = lines |> List.fold (fun widest line -> max widest (SvgWriting.textWidth size line)) 0
            let keyWidth = if value.Kind = DataValueKind.Object then (rows |> List.fold (fun widest row -> max widest (widthOf row.Key)) 0) + 2 * padding else 0
            let valueWidth = max 44 ((rows |> List.fold (fun widest row -> max widest (widthOf row.Value)) 0) + 2 * padding)

            {
                Id = ids[value]
                Value = value
                Rows = rows
                KeyWidth = keyWidth
                ValueWidth = valueWidth
                Height = rows |> List.sumBy rowHeight
            }

        // Tree placement: a table at the top of the column of its children's tables.
        let placed = ResizeArray<Table * int * int>()
        let links = ResizeArray<string * int * int * string * int * int>()
        let gap = 16
        let column = 56
        let margin = 24
        let top = margin + (if isNull model.Title then 0 else 30)

        let rec place (value: DataValue) x y =
            let laid = table value
            placed.Add(laid, x, y)
            let childX = x + laid.KeyWidth + laid.ValueWidth + column
            let mutable childY = y
            let mutable rowY = y

            for row in laid.Rows do
                if isContainer row.Data then
                    let height = place row.Data childX childY
                    links.Add(laid.Id, x + laid.KeyWidth + laid.ValueWidth, rowY + rowHeight row / 2, row.Id, childX, childY + rowHeight (table row.Data).Rows.Head / 2)
                    childY <- childY + height + gap

                rowY <- rowY + rowHeight row

            max laid.Height (childY - y - gap)

        let total = place model.Root margin top

        let width =
            placed |> Seq.fold (fun widest (laid, x, _) -> max widest (x + laid.KeyWidth + laid.ValueWidth)) 0
            |> fun right ->
                match model.Title with
                | null -> right + margin
                | value -> max (right + margin) (SvgWriting.textWidth 15 value + 2 * margin)

        let height = top + total + margin
        let edgeRefs = ElementRefs.Edges model.Projection
        let edgeIndex = Dictionary<string * string, int>()

        model.Projection.Edges |> Seq.iteri (fun index edge -> edgeIndex[(edge.Source.Value, edge.Target.Value)] <- index)

        let text writer x y (lines: string list) fill weight =
            SvgWriting.element
                writer
                "text"
                [ "x", n x; "y", n y; "font-family", Theme.fontStack; "font-size", n size; "font-weight", n weight; "fill", fill ]
                (fun () ->
                    lines
                    |> List.iteri (fun index line ->
                        SvgWriting.element
                            writer
                            "tspan"
                            [ "x", n x; "dy", (if index = 0 then "0" else n lineHeight) ]
                            (fun () -> writer.Add(SvgWriting.text line))))

        let title = if model.Format = DataFormat.Json then "JSON data tree" else "YAML data tree"

        SvgWriting.document
            "datatree"
            title
            width
            height
            []
            options
            metadata
            false
            (fun writer ->
                match model.Title with
                | null -> ()
                | value ->
                    SvgWriting.element
                        writer
                        "text"
                        [ "x", n margin; "y", n (margin + 14); "font-family", Theme.fontStack; "font-size", "15"; "font-weight", "700"; "fill", Theme.text ]
                        (fun () -> writer.Add(SvgWriting.text value))

                for source, x1, y1, target, x2, y2 in links do
                    let middle = x1 + column / 2
                    let reference = edgeRefs[edgeIndex[(source, target)]]

                    SvgWriting.element
                        writer
                        "g"
                        [ "class", "edge"; "data-ref", reference.ToString() ]
                        (fun () ->
                            SvgWriting.circle writer x1 y1 3 Theme.arrow Theme.arrow
                            let route = if y1 = y2 then $"M{p x1 y1} H{n x2}" else $"M{p x1 y1} H{n (middle - 6)} Q{p middle y1} {p middle (y1 + sign (y2 - y1) * 6)} V{n (y2 - sign (y2 - y1) * 6)} Q{p middle y2} {p (middle + 6) y2} H{n x2}"
                            SvgWriting.path writer route [ "fill", "none"; "stroke", Theme.arrow; "stroke-width", "1.5" ])

                for laid, x, y in placed do
                    let tone = if laid.Value.Kind = DataValueKind.Array then ColorFamily.Database else ColorFamily.Frontend
                    let tableWidth = laid.KeyWidth + laid.ValueWidth

                    SvgWriting.element
                        writer
                        "g"
                        [ "class", $"table f-{tone.WireValue}"; "data-ref", (DiagramElementRef.Node(DiagramId.Create laid.Id)).ToString(); "data-kind", laid.Value.Kind.WireValue ]
                        (fun () ->
                            let mutable rowY = y

                            for row in laid.Rows do
                                let rowHeight = rowHeight row
                                let marked = highlighted.Contains row.Id
                                let keyFill = if marked then Theme.fill ColorFamily.Cloud else Theme.fill tone
                                let valueFill = if marked then Theme.fill ColorFamily.Cloud else Theme.mask
                                let edge = if marked then Theme.stroke ColorFamily.Cloud else Theme.frame
                                let edgeWidth = if marked then "1.5" else "1"

                                SvgWriting.element
                                    writer
                                    "g"
                                    [
                                        "class", (if marked then "row highlighted" else "row")
                                        if row.Id <> laid.Id then "data-ref", (DiagramElementRef.Node(DiagramId.Create row.Id)).ToString()
                                    ]
                                    (fun () ->
                                        if laid.KeyWidth > 0 then
                                            SvgWriting.empty writer "rect" [ "class", "box"; "x", n x; "y", n rowY; "width", n laid.KeyWidth; "height", n rowHeight; "fill", keyFill; "stroke", edge; "stroke-width", edgeWidth ]
                                            text writer (x + padding) (rowY + 17) row.Key Theme.text 600

                                        SvgWriting.empty
                                            writer
                                            "rect"
                                            [ "class", "box"; "x", n (x + laid.KeyWidth); "y", n rowY; "width", n laid.ValueWidth; "height", n rowHeight; "fill", valueFill; "stroke", edge; "stroke-width", edgeWidth ]

                                        text writer (x + laid.KeyWidth + padding) (rowY + 17) row.Value (ink row.Data) 400)

                                rowY <- rowY + rowHeight

                            SvgWriting.empty
                                writer
                                "rect"
                                [ "class", "outline"; "x", n x; "y", n y; "width", n tableWidth; "height", n laid.Height; "rx", "3"; "fill", "none"; "stroke", Theme.stroke tone; "stroke-width", "1.5" ]))
