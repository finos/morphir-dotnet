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

    /// The Mermaid 12.2.0 shape catalogue (`A@{ shape: … }`), by short name and alias, mapped to the nearest
    /// shape that the renderer draws.
    let private catalogue =
        [
            FlowShape.Rectangle, [ "rect"; "proc"; "process"; "rectangle"; "squareRect"; "folder"; "directory"; "console"; "browser"; "notch-rect"; "card"; "notched-rectangle"; "tag-rect"; "tagged-rectangle"; "tag-proc"; "tagged-process"; "image" ]
            FlowShape.Rounded, [ "rounded"; "event"; "roundedRect"; "person"; "cloud"; "state"; "composite"; "icon" ]
            FlowShape.Stadium, [ "stadium"; "terminal"; "pill"; "delay"; "half-rounded-rectangle" ]
            FlowShape.Subroutine, [ "fr-rect"; "subprocess"; "subproc"; "framed-rectangle"; "subroutine"; "lin-rect"; "lined-rectangle"; "lined-process"; "lin-proc"; "shaded-process"; "div-rect"; "div-proc"; "divided-rectangle"; "divided-process"; "win-pane"; "internal-storage"; "window-pane"; "st-rect"; "procs"; "processes"; "stacked-rectangle" ]
            FlowShape.Cylinder, [ "cyl"; "db"; "database"; "cylinder"; "datastore"; "data-store"; "bucket"; "h-cyl"; "das"; "horizontal-cylinder"; "lin-cyl"; "disk"; "lined-cylinder"; "bow-rect"; "stored-data"; "bow-tie-rectangle" ]
            FlowShape.Circle, [ "circle"; "circ" ]
            FlowShape.Diamond, [ "diam"; "decision"; "diamond"; "question"; "choice"; "hourglass"; "collate" ]
            FlowShape.Hexagon, [ "hex"; "hexagon"; "prepare"; "bang"; "curv-trap"; "curved-trapezoid"; "display"; "notch-pent"; "loop-limit"; "notched-pentagon" ]
            FlowShape.Parallelogram, [ "lean-r"; "lean-right"; "in-out"; "lean_right"; "sl-rect"; "manual-input"; "sloped-rectangle" ]
            FlowShape.ReverseParallelogram, [ "lean-l"; "lean-left"; "out-in"; "lean_left" ]
            FlowShape.Trapezoid, [ "trap-b"; "priority"; "trapezoid-bottom"; "trapezoid"; "tri"; "extract"; "triangle" ]
            FlowShape.ReverseTrapezoid, [ "trap-t"; "manual"; "trapezoid-top"; "inv-trapezoid"; "inv_trapezoid"; "flip-tri"; "manual-file"; "flipped-triangle" ]
            FlowShape.DoubleCircle, [ "dbl-circ"; "double-circle"; "doublecircle" ]
            FlowShape.Text, [ "text" ]
            FlowShape.SmallCircle, [ "sm-circ"; "start"; "small-circle"; "stateStart"; "anchor" ]
            FlowShape.FramedCircle, [ "fr-circ"; "stop"; "framed-circle"; "stateEnd" ]
            FlowShape.Bar, [ "fork"; "join"; "forkJoin" ]
            FlowShape.Note, [ "brace"; "comment"; "brace-l"; "brace-r"; "braces"; "note" ]
            FlowShape.Asymmetric, [ "bolt"; "com-link"; "lightning-bolt"; "odd"; "rect_left_inv_arrow" ]
            FlowShape.Document, [ "doc"; "document"; "docs"; "documents"; "st-doc"; "stacked-document"; "tag-doc"; "tagged-document"; "flag"; "paper-tape"; "lin-doc"; "lined-document" ]
            FlowShape.FilledCircle, [ "f-circ"; "junction"; "filled-circle" ]
            FlowShape.CrossedCircle, [ "cross-circ"; "summary"; "crossed-circle" ]
        ]
        |> List.collect (fun (shape, names) -> names |> List.map (fun name -> name, shape))
        |> dict

    let private entity =
        Regex(@"#(?:(?<name>quot|amp|lt|gt|nbsp|apos);|(?<hex>x[0-9a-fA-F]{1,6});|(?<dec>[0-9]{1,7});)|<br\s*/?>", RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase)

    /// The text of a label: Mermaid entity codes and `<br>` decoded, Markdown backticks removed.
    let decode (text: string) =
        let text =
            if text.Length >= 2 && text[0] = '`' && text[text.Length - 1] = '`' then
                Regex.Replace(text.Substring(1, text.Length - 2), @"\r?\n[ \t]*", "\n")
            else
                text

        entity.Replace(
            text,
            MatchEvaluator(fun found ->
                let codePoint (value: string) style =
                    match Int32.TryParse(value, style, Globalization.CultureInfo.InvariantCulture) with
                    | true, value when value <= 0x10FFFF && (value < 0xD800 || value > 0xDFFF) && value >= 0x20 ->
                        Char.ConvertFromUtf32 value
                    | _ -> found.Value

                if found.Groups["name"].Success then
                    match found.Groups["name"].Value with
                    | "quot" -> "\""
                    | "amp" -> "&"
                    | "lt" -> "<"
                    | "gt" -> ">"
                    | "apos" -> "'"
                    | _ -> "\u00A0"
                elif found.Groups["hex"].Success then
                    codePoint (found.Groups["hex"].Value.Substring 1) Globalization.NumberStyles.HexNumber
                elif found.Groups["dec"].Success then
                    codePoint found.Groups["dec"].Value Globalization.NumberStyles.None
                else
                    "\n")
        )

    /// The top-level `key: value` pairs of shape data, separated by commas or line breaks. Entries indented under a
    /// key with no value belong to a nested map and are skipped.
    let private shapeData (body: string) =
        let entries = ResizeArray<int * string>()
        let builder = Text.StringBuilder()
        let mutable quote = '\000'
        let mutable lineIndent = 0
        let mutable entryIndent = 0
        let mutable counting = false

        let flush () =
            entries.Add(entryIndent, builder.ToString().Trim())
            builder.Clear() |> ignore

        for character in body do
            if quote <> '\000' then
                if character = quote then quote <- '\000'
                builder.Append character |> ignore
            elif character = '\n' || character = '\r' then
                flush ()
                counting <- true
                lineIndent <- 0
            elif character = ',' then
                flush ()
                entryIndent <- lineIndent
            elif counting && (character = ' ' || character = '\t') then
                lineIndent <- lineIndent + 1
            else
                if counting then
                    counting <- false
                    entryIndent <- lineIndent

                if character = '"' || character = '\'' then quote <- character
                builder.Append character |> ignore

        flush ()

        let mutable parent = None

        [
            for indent, entry in entries do
                match entry.IndexOf ':' with
                | colon when colon > 0 ->
                    match parent with
                    | Some level when indent > level -> ()
                    | _ ->
                        let value = entry.Substring(colon + 1).Trim()
                        parent <- if value.Length = 0 then Some indent else None
                        yield entry.Substring(0, colon).Trim(), (Reading.unquote value).Trim('\'')
                | _ -> ()
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

                let label = decode (Reading.unquote (text.Substring(0, text.Length - 1)))

                match opening, marker with
                | "[/", '/' -> Ok(FlowShape.Parallelogram, label)
                | "[/", '\\' -> Ok(FlowShape.Trapezoid, label)
                | "[\\", '\\' -> Ok(FlowShape.ReverseParallelogram, label)
                | "[\\", '/' -> Ok(FlowShape.ReverseTrapezoid, label)
                | _ -> Error "Expected a closing slash before the bracket."
            | _ -> Error "Unclosed flowchart node label."
        else
            match cursor.ReadUntilClosing closing with
            | Some label -> Ok(shape, decode label)
            | None -> Error "Unclosed flowchart node label."

    let private readNode (line: Reading.Line) (cursor: Reading.Cursor) =
        cursor.SkipSpace()
        let start = cursor.Index

        let text = line.Text

        // `&` and `:` join an identifier when a word character follows directly, as in `A&B` or `A:B`.
        let joins index =
            index + 1 < text.Length && (Char.IsLetterOrDigit text[index + 1] || text[index + 1] = '_')

        let id =
            cursor.ReadWhile(fun character ->
                Char.IsLetterOrDigit character
                || character = '_'
                || character = '.'
                || (character = '-' && not (cursor.StartsWith "--" || cursor.StartsWith "-."))
                || ((character = '&' || character = ':') && not (cursor.StartsWith ":::") && joins cursor.Index)
                || Char.IsSurrogate character
            )

        let classes (result: Result<FlowNode * bool, string>) =
            if cursor.Take ":::" then
                let first = cursor.Index
                cursor.ReadWhile(fun c -> not (Char.IsWhiteSpace c) && c <> '&' && c <> ';' && c <> '-' && c <> '=') |> ignore

                Reading.warn
                    "mermaid/unapplied-presentation"
                    "Classes are kept in the source; the theme decides the colours."
                    (Some(line.Span(first - 3, cursor.Index)))

            result

        // Shape data after the identifier or the classic shape sets the shape and label; later blocks win.
        let rec data (shape, label) =
            if cursor.StartsWith "@{" then
                cursor.Advance 2

                match cursor.ReadUntilClosing "}" with
                | None -> Error "Unclosed flowchart shape data."
                | Some body ->
                    let pairs = shapeData body
                    let value key = pairs |> List.tryFind (fst >> (=) key) |> Option.map snd

                    match value "shape" |> Option.map (fun name -> name, catalogue.TryGetValue name) with
                    | Some(name, (false, _)) -> Error $"'{name}' is not a Mermaid flowchart shape."
                    | known ->
                        let shape =
                            match known with
                            | Some(_, (true, shape)) -> shape
                            | _ when (value "icon").IsSome || (value "img").IsSome -> FlowShape.Rounded
                            | _ -> shape

                        data (shape, value "label" |> Option.map decode |> Option.defaultValue label)
            else
                Ok(shape, label)

        match DiagramId.Parse id with
        | Error _ -> Error "Expected a flowchart node identifier."
        | Ok identity ->
            let classic =
                if cursor.StartsWith "@{" then
                    Ok(FlowShape.Rectangle, id, true)
                else
                    cursor.SkipSpace()

                    match shapes |> List.tryFind (fun (opening, _, _) -> cursor.StartsWith opening) with
                    | Some(opening, closing, shape) ->
                        cursor.Advance opening.Length
                        readShapeLabel shape opening closing cursor |> Result.map (fun (shape, label) -> shape, label, true)
                    | None -> Ok(FlowShape.Rectangle, id, false)

            classic
            |> Result.bind (fun (shape, label, classic) ->
                let before = cursor.Index

                data (shape, label)
                |> Result.map (fun (shape, label) ->
                    let explicit = classic || cursor.Index > before
                    let span = if explicit then line.Span(start, cursor.Index) else line.Span(start, start + id.Length)
                    FlowNode(identity, label, shape, span), explicit))
            |> classes

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

    let private edgeId = Regex(@"\G(?<id>[^\s@&;]+)@(?=[-=.~<ox])", RegexOptions.CultureInvariant)

    let private readEdge (line: Reading.Line) (cursor: Reading.Cursor) (named: string -> unit) =
        let identified = edgeId.Match(line.Text, cursor.Index)

        if identified.Success then
            named identified.Groups["id"].Value
            cursor.Advance identified.Length

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

            // `A-- text -->B` ends with an arrow; `A-- text ---B` ends open.
            let inlineLabel (closing: string) =
                let rest = line.Text.Substring cursor.Index
                let arrow = rest.IndexOf(closing, StringComparison.Ordinal)
                let openEnd = closing.Substring(0, 2) + string closing[1]
                let open' = rest.IndexOf(openEnd, StringComparison.Ordinal)

                let finish, head, length =
                    if arrow >= 0 && (open' < 0 || arrow <= open') then arrow, EdgeHead.Arrow, closing.Length
                    elif open' >= 0 then open', EdgeHead.None, openEnd.Length
                    else -1, EdgeHead.None, 0

                let label = if finish > 0 then Reading.unquote (rest.Substring(0, finish).Trim()) else ""

                if label.Length > 0 then
                    cursor.Advance(finish + length)

                    while cursor.Peek = closing[1] || cursor.Peek = '>' do
                        cursor.Advance 1

                    Ok(style, source, head, Some(decode label), line.Span(start, cursor.Index))
                else
                    Error "Expected a flowchart edge label followed by an arrow."

            if stroke = "-." && source = EdgeHead.None && target = EdgeHead.None then
                inlineLabel ".->"
            elif stroke = "-." then
                Error "Expected a complete dotted connector."
            elif cursor.Take "|" then
                match cursor.ReadUntilClosing "|" with
                | Some label ->
                    Ok(style, source, target, Some(decode label), line.Span(start, cursor.Index))
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

    let private readLine addNode addEdges named (line: Reading.Line) start =
        let cursor = Reading.Cursor(line, start)
        let mutable failure = None

        let rec readChain sources =
            cursor.SkipSpace()

            if not cursor.AtEnd && not (cursor.StartsWith ";" || cursor.StartsWith "%%") then
                match readEdge line cursor named with
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

    /// Whether a statement continues on the next line: open shape data, an open Markdown label or an `accDescr` block.
    let private unfinished (text: string) =
        let ticks = text |> Seq.filter ((=) '`') |> Seq.length
        let shapeData = text.LastIndexOf("@{", StringComparison.Ordinal)

        let mutable depth = 0
        let mutable quoted = false

        for character in text do
            if character = '"' then quoted <- not quoted
            elif not quoted && (character = '[' || character = '(' || character = '{') then depth <- depth + 1
            elif not quoted && (character = ']' || character = ')' || character = '}') then depth <- max 0 (depth - 1)

        ticks % 2 = 1
        || depth > 0
        || shapeData >= 0 && text.IndexOf('}', shapeData) < 0
        || text.TrimStart().StartsWith("accDescr", StringComparison.Ordinal) && text.Contains '{' && not (text.Contains '}')

    /// Joins the lines of a statement that spans several, keeping every code unit's offset.
    let private statements (lines: seq<Reading.Line * int>) =
        let joined = ResizeArray<Reading.Line * int>()

        for line, start in lines do
            match Seq.tryLast joined with
            | Some(previous, first) when unfinished (previous.Text.Substring first) ->
                let gap = line.Offset - (previous.Offset + previous.Text.Length)

                joined[joined.Count - 1] <-
                    { previous with Text = previous.Text + String('\n', max 1 gap) + line.Text }, first
            | _ -> joined.Add(line, start)

        joined

    let private presentation =
        [ "style"; "classDef"; "class"; "click"; "linkStyle"; "accTitle"; "accDescr"; "title" ]

    let read direction (lines: seq<Reading.Line * int>) =
        let nodes = ResizeArray<FlowNode>()
        let indices = Dictionary<DiagramId, int>()
        let edges = ResizeArray<FlowEdge>()
        let groups = ResizeArray<Group>()
        let groupIds = HashSet<DiagramId>()
        let openGroups = Stack<Group>()
        let assigned = HashSet<DiagramId>()

        let edgeIds = HashSet<string>(StringComparer.Ordinal)
        let explicitIds = HashSet<DiagramId>()

        let addNode (node: FlowNode) explicit =
            if groupIds.Contains node.Id then
                Ok()
            elif edgeIds.Contains node.Id.Value then
                Reading.warn "mermaid/unapplied-presentation" "Edge data is kept in the source; edges keep the theme's style." (Some node.Span)
                Ok()
            else
                if explicit then
                    explicitIds.Add node.Id |> ignore

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
            let text = line.Text.Substring(start).TrimEnd().TrimEnd(';').TrimEnd()

            let span = line.Span(start, start + text.Length)

            let fail message =
                Some(Reading.error "mermaid/flowchart-group" message (Some span))

            let keyword (word: string) =
                text.StartsWith(word, StringComparison.Ordinal)
                && text.Length > word.Length
                && (Char.IsWhiteSpace text[word.Length] || text[word.Length] = ':' || text[word.Length] = '{')

            let words = text.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

            match presentation |> List.tryFind keyword with
            | Some word ->
                Reading.warn
                    "mermaid/unapplied-presentation"
                    "Styles, classes, callbacks, links and accessibility text are kept in the source but not drawn."
                    (Some span)

                // Mermaid makes a node of each element that a style, class or click statement names.
                let targets =
                    match word, words with
                    | ("style" | "click"), _ when words.Length > 1 -> [ words[1] ]
                    | "class", _ when words.Length > 2 -> words[1].Split(',') |> Array.toList
                    | _ -> []

                for target in targets do
                    match DiagramId.Parse target with
                    | Ok id when not (indices.ContainsKey id || groupIds.Contains id || edgeIds.Contains target) && nodes.Count < Reading.nodeLimit ->
                        indices.Add(id, nodes.Count)
                        nodes.Add(FlowNode(id, target, FlowShape.Rectangle, span))
                    | _ -> ()

                None
            | None when keyword "direction" ->
                Reading.warn "mermaid/unapplied-presentation" "A subgraph direction is kept in the source; the diagram's direction lays it out." (Some span)
                None
            | None ->
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
                    | Ok _ when openGroups.Count >= Reading.depthLimit ->
                        Some(Reading.limit "mermaid" "depth" $"Subgraphs nest deeper than {Reading.depthLimit} levels." (Some span))
                    | Ok _ when groups.Count >= Reading.nodeLimit -> fail "Flowchart subgraph limit exceeded."
                    | Ok id when groupIds.Contains id ->
                        // A repeated subgraph adds to the first one, which keeps its title.
                        openGroups.Push(groups |> Seq.find (fun group -> group.Id = id))
                        None
                    | Ok id when explicitIds.Contains id || not (groupIds.Add id) ->
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
                    readLine addNode addEdges (edgeIds.Add >> ignore) line start

        match statements lines |> Seq.tryPick readStatement with
        | Some diagnostic -> Error diagnostic
        | None when openGroups.Count > 0 ->
            Error(
                Reading.error
                    "mermaid/flowchart-group"
                    "Unclosed subgraph."
                    (Some(openGroups.Peek().Span))
            )
        | None ->
            // A node that a later subgraph names becomes that subgraph: links to it attach to the subgraph.
            let nodes = nodes |> Seq.filter (fun node -> not (groupIds.Contains node.Id))

            let groups =
                groups
                |> Seq.map (fun group ->
                    FlowGroup(
                        group.Id,
                        decode group.Label,
                        group.ParentId,
                        group.Nodes |> Seq.filter (groupIds.Contains >> not) |> Reading.immutable,
                        group.Span
                    )
                )
                |> Reading.immutable

            Ok(
                Diagram.Flowchart(
                    Flowchart(direction, Reading.immutable nodes, Reading.immutable edges, groups)
                )
            )
