// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
module internal FlowchartReader =
    type private Group =
        {
            Id: DiagramId
            Label: string
            ParentId: DiagramId option
            Nodes: ResizeArray<DiagramId>
            Span: SourceSpan
        }

    let private groupLabel =
        Regex(@"^(?<id>[^\s\[]+)\s*\[(?<label>.*)\]$", RegexOptions.CultureInvariant)

    let private shapes =
        [
            "(((", ")))", FlowShape.DoubleCircle
            "((", "))", FlowShape.Circle
            "([", "])", FlowShape.Stadium
            "[[", "]]", FlowShape.Subroutine
            "[(", ")]", FlowShape.Cylinder
            "{{", "}}", FlowShape.Hexagon
            "[/", "/]", FlowShape.Parallelogram
            "[\\", "\\]", FlowShape.ReverseParallelogram
            ">", "]", FlowShape.Asymmetric
            "(", ")", FlowShape.Rounded
            "[", "]", FlowShape.Rectangle
            "{", "}", FlowShape.Diamond
        ]

    let private edgePattern =
        Regex(
            @"\G(?<source>[<ox]?)(?<line>-{2,}|-\.+-|={2,}|~{3,})(?<target>[>ox]?)|\G(?<line>-\.)",
            RegexOptions.CultureInvariant
        )

    let private edgeHead (text: string) =
        match text with
        | "<"
        | ">" -> EdgeHead.Arrow
        | "o" -> EdgeHead.Circle
        | "x" -> EdgeHead.Cross
        | _ -> EdgeHead.None

    let private readShapeLabel shape opening closing (cursor: Reading.Cursor) =
        if opening = "[/" || opening = "[\\" then
            match cursor.ReadUntilClosing "]" with
            | Some text when text.Length > 0 ->
                let marker = text[text.Length - 1]

                let label = Reading.unquote (text.Substring(0, text.Length - 1))

                match opening, marker with
                | "[/", '/' -> Ok(FlowShape.Parallelogram, label)
                | "[/", '\\' -> Ok(FlowShape.Trapezoid, label)
                | "[\\", '\\' -> Ok(FlowShape.ReverseParallelogram, label)
                | "[\\", '/' -> Ok(FlowShape.ReverseTrapezoid, label)
                | _ -> Error "Expected a closing slash before the bracket."
            | _ -> Error "Unclosed flowchart node label."
        else
            match cursor.ReadUntilClosing closing with
            | Some label -> Ok(shape, label)
            | None -> Error "Unclosed flowchart node label."

    let private readNode (line: Reading.Line) (cursor: Reading.Cursor) =
        cursor.SkipSpace()
        let start = cursor.Index

        let id =
            cursor.ReadWhile(fun character ->
                Char.IsLetterOrDigit character
                || character = '_'
                || character = '.'
                || (character = '-' && not (cursor.StartsWith "--" || cursor.StartsWith "-."))
                || Char.IsSurrogate character
            )

        match DiagramId.Parse id with
        | Error _ -> Error "Expected a flowchart node identifier."
        | Ok identity ->
            cursor.SkipSpace()

            match shapes |> List.tryFind (fun (opening, _, _) -> cursor.StartsWith opening) with
            | Some(opening, closing, shape) ->
                cursor.Advance opening.Length

                readShapeLabel shape opening closing cursor
                |> Result.map (fun (shape, label) ->
                    FlowNode(identity, label, shape, line.Span(start, cursor.Index)), true
                )
            | None ->
                Ok(
                    FlowNode(identity, id, FlowShape.Rectangle, line.Span(start, start + id.Length)),
                    false
                )

    let private readEndpoints line cursor addNode =
        let endpoints = ResizeArray<DiagramId>()
        let mutable failure = None
        let mutable more = true

        while more && Option.isNone failure do
            match readNode line cursor with
            | Error message -> failure <- Some message
            | Ok(node, explicit) ->
                match addNode node explicit with
                | Error message -> failure <- Some message
                | Ok() ->
                    endpoints.Add node.Id
                    cursor.SkipSpace()
                    more <- cursor.Take "&"

        match failure with
        | Some message -> Error message
        | None -> Ok endpoints

    let private readEdge (line: Reading.Line) (cursor: Reading.Cursor) =
        let start = cursor.Index
        let matched = edgePattern.Match(line.Text, start)

        if not matched.Success then
            Error "Expected a flowchart connector."
        else
            let stroke = matched.Groups["line"].Value
            let source = edgeHead matched.Groups["source"].Value
            let target = edgeHead matched.Groups["target"].Value

            let style =
                match stroke[0] with
                | '~' -> FlowLine.Invisible
                | '=' -> FlowLine.Thick
                | _ when stroke.Contains '.' -> FlowLine.Dotted
                | _ -> FlowLine.Solid

            cursor.Advance matched.Length
            cursor.SkipSpace()

            let inlineLabel closing =
                match cursor.ReadUntilClosing closing with
                | Some label when label.Length > 0 ->
                    Ok(style, source, EdgeHead.Arrow, Some label, line.Span(start, cursor.Index))
                | _ -> Error "Expected a flowchart edge label followed by an arrow."

            if stroke = "-." && source = EdgeHead.None && target = EdgeHead.None then
                inlineLabel ".->"
            elif stroke = "-." then
                Error "Expected a complete dotted connector."
            elif cursor.Take "|" then
                match cursor.ReadUntilClosing "|" with
                | Some label ->
                    Ok(style, source, target, Some label, line.Span(start, cursor.Index))
                | None -> Error "Unclosed flowchart edge label."
            elif
                style = FlowLine.Solid
                && stroke = "--"
                && source = EdgeHead.None
                && target = EdgeHead.None
            then
                inlineLabel "-->"
            elif style = FlowLine.Solid && stroke = "--" then
                if target = EdgeHead.None then
                    Error "Expected a complete flowchart connector."
                else
                    Ok(style, source, target, None, line.Span(start, cursor.Index))
            elif style = FlowLine.Thick && stroke = "==" && target = EdgeHead.None then
                inlineLabel "==>"
            else
                Ok(style, source, target, None, line.Span(start, cursor.Index))

    let private readLine addNode addEdges (line: Reading.Line) start =
        let cursor = Reading.Cursor(line, start)
        let mutable failure = None

        let rec readChain sources =
            cursor.SkipSpace()

            if not cursor.AtEnd && not (cursor.StartsWith ";" || cursor.StartsWith "%%") then
                match readEdge line cursor with
                | Error message -> failure <- Some message
                | Ok(style, source, target, label, span) ->
                    match readEndpoints line cursor addNode with
                    | Error message -> failure <- Some message
                    | Ok targets ->
                        match addEdges sources targets label style source target span with
                        | Error message -> failure <- Some message
                        | Ok() -> readChain targets

        cursor.SkipSpace()

        while not cursor.AtEnd && not (cursor.StartsWith "%%") && Option.isNone failure do
            if not (cursor.Take ";") then
                match readEndpoints line cursor addNode with
                | Error message -> failure <- Some message
                | Ok sources -> readChain sources

            cursor.SkipSpace()

        failure
        |> Option.map (fun message ->
            Reading.error
                "mermaid/flowchart-statement"
                message
                (Some(line.Span(start, line.Text.Length)))
        )

    let read direction (lines: seq<Reading.Line * int>) =
        let nodes = ResizeArray<FlowNode>()
        let indices = Dictionary<DiagramId, int>()
        let edges = ResizeArray<FlowEdge>()
        let groups = ResizeArray<Group>()
        let groupIds = HashSet<DiagramId>()
        let openGroups = Stack<Group>()
        let assigned = HashSet<DiagramId>()

        let addNode (node: FlowNode) explicit =
            if groupIds.Contains node.Id then
                Error $"Connections to subgraph '{node.Id}' are unsupported."
            else
                if openGroups.Count > 0 && assigned.Add node.Id then
                    openGroups.Peek().Nodes.Add node.Id

                match indices.TryGetValue node.Id with
                | true, index ->
                    if explicit then
                        nodes[index] <- node

                    Ok()
                | _ when nodes.Count >= Reading.nodeLimit ->
                    Error $"Flowchart exceeds {Reading.nodeLimit} nodes."
                | _ ->
                    indices.Add(node.Id, nodes.Count)
                    nodes.Add node
                    Ok()

        let addEdges
            (sources: ResizeArray<DiagramId>)
            (targets: ResizeArray<DiagramId>)
            label
            style
            source
            target
            span
            =
            if
                int64 sources.Count * int64 targets.Count > int64 (Reading.edgeLimit - edges.Count)
            then
                Error $"Flowchart exceeds {Reading.edgeLimit} edges."
            else
                for fromId in sources do
                    for toId in targets do
                        edges.Add(FlowEdge(fromId, toId, label, style, source, target, span))

                Ok()

        let readStatement ((line: Reading.Line), start) =
            let text = line.Text.Substring(start).TrimEnd()

            let span = line.Span(start, start + text.Length)

            let fail message =
                Some(Reading.error "mermaid/flowchart-group" message (Some span))

            if text = "end" then
                if openGroups.Count = 0 then
                    fail "Unexpected subgraph terminator."
                else
                    openGroups.Pop() |> ignore

                    None
            elif
                Reading.startsWith "subgraph" text && text.Length > 8 && Char.IsWhiteSpace text[8]
            then
                let title = text.Substring(9).Trim()
                let matched = groupLabel.Match title

                let id, label =
                    if matched.Success then
                        matched.Groups["id"].Value, Reading.unquote matched.Groups["label"].Value
                    else
                        let title = Reading.unquote title
                        title, title

                match DiagramId.Parse id with
                | Error _ -> fail "Expected a subgraph name."
                | Ok _ when String.IsNullOrWhiteSpace label -> fail "Expected a subgraph label."
                | Ok _ when groups.Count >= Reading.nodeLimit || openGroups.Count >= 32 ->
                    fail "Flowchart subgraph limit exceeded."
                | Ok id when indices.ContainsKey id || not (groupIds.Add id) ->
                    fail $"Duplicate flowchart identifier '{id}'."
                | Ok id ->
                    let group =
                        {
                            Id = id
                            Label = label
                            ParentId =
                                if openGroups.Count = 0 then
                                    None
                                else
                                    Some(openGroups.Peek().Id)
                            Nodes = ResizeArray()
                            Span = span
                        }

                    openGroups.Push group
                    groups.Add group
                    None
            else
                readLine addNode addEdges line start

        match lines |> Seq.tryPick readStatement with
        | Some diagnostic -> Error diagnostic
        | None when openGroups.Count > 0 ->
            Error(
                Reading.error
                    "mermaid/flowchart-group"
                    "Unclosed subgraph."
                    (Some(openGroups.Peek().Span))
            )
        | None ->
            let groups =
                groups
                |> Seq.map (fun group ->
                    FlowGroup(
                        group.Id,
                        group.Label,
                        group.ParentId,
                        Reading.immutable group.Nodes,
                        group.Span
                    )
                )
                |> Reading.immutable

            Ok(
                Diagram.Flowchart(
                    Flowchart(direction, Reading.immutable nodes, Reading.immutable edges, groups)
                )
            )
