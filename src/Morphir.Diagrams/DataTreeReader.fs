// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Text
open System.Text.RegularExpressions

/// Reads PlantUML `@startjson` and `@startyaml` bodies, as PlantUML 1.2026.9 does.
[<RequireQualifiedAccess>]
module internal DataTreeReader =
    exception private Refused of code: string * message: string

    let private refuse code message = raise (Refused(code, message))

    let private scalar kind text =
        DataValue(kind, text, ImmutableArray<DataMember>.Empty, ImmutableArray<DataValue>.Empty)

    let private objectOf (members: DataMember seq) =
        DataValue(DataValueKind.Object, "", ImmutableArray.CreateRange members, ImmutableArray<DataValue>.Empty)

    let private arrayOf (items: DataValue seq) =
        DataValue(DataValueKind.Array, "", ImmutableArray<DataMember>.Empty, ImmutableArray.CreateRange items)

    let private depth level =
        if level > Reading.depthLimit then raise (Refused("depth-limit", $"Values nest deeper than {Reading.depthLimit} levels."))

    // JSON ------------------------------------------------------------------------------------------------------------

    let private number = Regex(@"\G-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?", RegexOptions.CultureInvariant)

    /// Strict JSON, with `//` comments where PlantUML allows them: before an object's member name and after its value.
    let private json (text: string) =
        let mutable index = 0
        let peek () = if index < text.Length then text[index] else '\000'
        let fail () = refuse "json-syntax" "The data is not JSON that PlantUML reads."

        let skip () =
            while index < text.Length && Char.IsWhiteSpace text[index] do
                index <- index + 1

        let skipWithComments () =
            skip ()

            while index + 1 < text.Length && text[index] = '/' && text[index + 1] = '/' do
                while index < text.Length && text[index] <> '\n' do
                    index <- index + 1

                skip ()

        let expect (c: char) =
            if peek () <> c then fail ()
            index <- index + 1

        let readString () =
            expect '"'
            let builder = StringBuilder()

            while peek () <> '"' do
                let c = peek ()

                if c = '\000' && index >= text.Length then refuse "json-string" "A string has no closing quote."
                if c < ' ' then refuse "json-string" "A string holds a control character; write it as an escape."

                if c = '\\' then
                    index <- index + 1

                    match peek () with
                    | '"' -> builder.Append '"' |> ignore
                    | '\\' -> builder.Append '\\' |> ignore
                    | '/' -> builder.Append '/' |> ignore
                    | 'b' -> builder.Append '\b' |> ignore
                    | 'f' -> builder.Append '\012' |> ignore
                    | 'n' -> builder.Append '\n' |> ignore
                    | 'r' -> builder.Append '\r' |> ignore
                    | 't' -> builder.Append '\t' |> ignore
                    | 'u' when index + 4 < text.Length && Regex.IsMatch(text.Substring(index + 1, 4), "^[0-9A-Fa-f]{4}$") ->
                        builder.Append(char (Convert.ToInt32(text.Substring(index + 1, 4), 16))) |> ignore
                        index <- index + 4
                    | _ -> refuse "json-escape" "A string holds an escape that JSON does not define."

                    index <- index + 1
                else
                    builder.Append c |> ignore
                    index <- index + 1

            index <- index + 1
            builder.ToString()

        let rec value level =
            depth level

            match peek () with
            | '{' ->
                index <- index + 1
                skip ()
                let members = ResizeArray<DataMember>()

                if peek () = '}' then
                    index <- index + 1
                else
                    let mutable more = true

                    while more do
                        skipWithComments ()
                        let key = readString ()
                        skipWithComments ()
                        expect ':'
                        skip ()
                        members.Add(DataMember(key, value (level + 1)))
                        skipWithComments ()

                        match peek () with
                        | ',' -> index <- index + 1
                        | '}' ->
                            index <- index + 1
                            more <- false
                        | _ -> fail ()

                objectOf members
            | '[' ->
                index <- index + 1
                skip ()
                let items = ResizeArray<DataValue>()

                if peek () = ']' then
                    index <- index + 1
                else
                    let mutable more = true

                    while more do
                        skip ()
                        items.Add(value (level + 1))
                        skip ()

                        match peek () with
                        | ',' -> index <- index + 1
                        | ']' ->
                            index <- index + 1
                            more <- false
                        | _ -> fail ()

                arrayOf items
            | '"' -> scalar DataValueKind.String (readString ())
            | _ ->
                let literal (word: string) kind =
                    if String.CompareOrdinal(text, index, word, 0, word.Length) = 0 then
                        index <- index + word.Length
                        Some(scalar kind word)
                    else
                        None

                match literal "true" DataValueKind.Boolean
                      |> Option.orElse (literal "false" DataValueKind.Boolean)
                      |> Option.orElse (literal "null" DataValueKind.Null) with
                | Some found -> found
                | None ->
                    let found = number.Match(text, index)
                    if not found.Success || found.Length = 0 then fail ()
                    index <- index + found.Length
                    scalar DataValueKind.Number found.Value

        skip ()
        if index >= text.Length then refuse "json-empty" "A JSON block needs a value."
        let root = value 0
        skip ()
        if index < text.Length then refuse "json-syntax" "A JSON block holds one value."
        root

    // YAML ------------------------------------------------------------------------------------------------------------

    type private YamlLine = { Indent: int; Text: string }

    /// Text before a ` #` comment outside quotes, trimmed. A list item keeps the spaces before the one that starts the
    /// comment, as PlantUML does.
    let private cut (trim: bool) (text: string) =
        let mutable quote = '\000'
        let mutable cut = text.Length
        let mutable index = 0

        while index < cut do
            let c = text[index]

            if quote <> '\000' then
                (if c = quote then quote <- '\000')
            elif c = '"' || c = '\'' then
                quote <- c
            elif c = '#' && (index = 0 || Char.IsWhiteSpace text[index - 1]) then
                cut <- index

            index <- index + 1

        if trim then text.Substring(0, cut).Trim()
        elif cut < text.Length then text.Substring(0, max 0 (cut - 1)).TrimStart()
        else text.Trim()

    let private uncomment = cut true

    /// A scalar as PlantUML shows it: the comment removed, tabs as four spaces, and matching outer quotes removed.
    let private scalarText trim (text: string) =
        let text = (cut trim text).Replace("\t", "    ")

        if text.Length >= 2 && (text[0] = '"' || text[0] = '\'') && text[text.Length - 1] = text[0] then
            text.Substring(1, text.Length - 2)
        else
            text

    let private isItem (text: string) = text = "-" || text.StartsWith("- ", StringComparison.Ordinal)

    /// The key and the rest of a `key: value` line, or none for plain text.
    let private keyed (text: string) =
        if text.StartsWith '"' then
            let close = text.IndexOf('"', 1)

            if close > 0 && close + 1 < text.Length && text[close + 1] = ':' then
                Some(text.Substring(1, close - 1), text.Substring(close + 2).Trim())
            else
                None
        else
            let colon = text.IndexOf ':'
            if colon >= 0 then Some(text.Substring(0, colon).Trim(), text.Substring(colon + 1).Trim()) else None

    /// A flow sequence `[a, "b,c", 'd']`; backslashes escape commas and quotes.
    let private flow (text: string) =
        let inner = text.Substring(1, text.Length - 2)
        let items = ResizeArray<DataValue>()
        let current = StringBuilder()
        let mutable quote = '\000'
        let mutable quoted = false
        let mutable index = 0

        let finish () =
            let value = current.ToString()
            items.Add(scalar DataValueKind.String (if quoted then value else value.Trim()))
            current.Clear() |> ignore
            quoted <- false

        while index < inner.Length do
            let c = inner[index]

            if c = '\\' && index + 1 < inner.Length then
                current.Append inner[index + 1] |> ignore
                index <- index + 1
            elif quote <> '\000' then
                if c = quote then quote <- '\000' else current.Append c |> ignore
            elif (c = '"' || c = '\'') && current.ToString().Trim() = "" then
                current.Clear() |> ignore
                quote <- c
                quoted <- true
            elif c = ',' then
                finish ()
            elif not quoted then
                current.Append c |> ignore

            index <- index + 1

        if inner.Trim() <> "" then finish ()
        arrayOf items

    let private yaml (source: YamlLine list) =
        let lines =
            source
            |> List.filter (fun line -> line.Text <> "")
            |> List.toArray

        let mutable index = 0
        let current () = lines[index]
        let more () = index < lines.Length

        let isComment (line: YamlLine) = line.Text.StartsWith '#'

        let skipComments () =
            while more () && isComment (current ()) do
                index <- index + 1

        let rec value level (indent: int) (inItem: bool) (rest: string) =
            depth level

            if rest = "|" then
                if inItem then refuse "yaml-block" "A block scalar inside a list item is not supported."

                // A literal block runs until a key at or left of its own key.
                let block = StringBuilder()

                while more () && not ((current ()).Indent <= indent && (keyed (current ()).Text).IsSome) do
                    block.Append((current ()).Text).Append('\n') |> ignore
                    index <- index + 1

                scalar DataValueKind.String (block.ToString())
            elif rest.StartsWith '>' then
                refuse "yaml-folded" "Folded block scalars are not supported."
            elif rest = "" || rest.StartsWith '#' then
                skipComments ()

                if more () && isItem (current ()).Text && (current ()).Indent >= indent then
                    list (level + 1) (current ()).Indent
                elif more () && (current ()).Indent > indent && (keyed (current ()).Text).IsSome then
                    map (level + 1) (current ()).Indent false
                elif more () && (current ()).Indent > indent then
                    // Plain lines after an empty value join with spaces, even when a later one moves left.
                    let words = ResizeArray<string>()

                    while more () && not (isItem (current ()).Text) && (keyed (current ()).Text).IsNone do
                        if not (isComment (current ())) then words.Add(scalarText true (current ()).Text)
                        index <- index + 1

                    scalar DataValueKind.String (String.Join(" ", words))
                elif inItem then
                    refuse "yaml-empty-item" "A key inside a list item needs a value."
                else
                    scalar DataValueKind.String ""
            elif rest.StartsWith '[' && (uncomment rest).EndsWith ']' then
                flow (uncomment rest)
            else
                scalar DataValueKind.String (scalarText true rest)

        and map level indent inItem =
            depth level
            let order = ResizeArray<string>()
            let values = Dictionary<string, DataValue>(StringComparer.Ordinal)
            let mutable stop = false

            while not stop && more () do
                let line = current ()

                if isComment line then
                    index <- index + 1
                elif line.Indent < indent then
                    stop <- true
                elif isItem line.Text then
                    refuse "yaml-mixed" "A list item cannot sit among the keys of a map."
                else
                    match keyed line.Text with
                    | None -> refuse "yaml-text" "Plain text cannot sit among the keys of a map."
                    | Some(key, rest) ->
                        index <- index + 1
                        let found = value (level + 1) line.Indent inItem rest

                        // After a scalar, a deeper key or item has no parent.
                        if found.Kind = DataValueKind.String && rest <> "" && rest <> "|" then
                            skipComments ()

                            if more () && (current ()).Indent > line.Indent then
                                refuse "yaml-indent" "A line is indented under a key that already has a value."

                        // A repeated key replaces the value and keeps its first place, as PlantUML does.
                        if not (values.ContainsKey key) then order.Add key
                        values[key] <- found

            objectOf (order |> Seq.map (fun key -> DataMember(key, values[key])))

        and list level indent =
            depth level
            let items = ResizeArray<DataValue>()
            let mutable stop = false

            while not stop && more () do
                let line = current ()

                if isComment line then
                    index <- index + 1
                elif line.Indent <> indent || not (isItem line.Text) then
                    stop <- true
                else
                    let rest = if line.Text = "-" then "" else line.Text.Substring(2).TrimStart()
                    let column = line.Indent + line.Text.Length - rest.Length

                    if rest = "" then
                        index <- index + 1
                        skipComments ()

                        if more () && (current ()).Indent > indent && (keyed (current ()).Text).IsSome && not (isItem (current ()).Text) then
                            items.Add(map (level + 1) (current ()).Indent false)
                        else
                            refuse "yaml-empty-item" "A list item needs a value or a map under it."
                    elif (keyed rest).IsSome && not (isItem rest) && not (rest.StartsWith '[') then
                        // `- a: 1` opens a map whose keys line up with `a`.
                        lines[index] <- { Indent = column; Text = rest }
                        items.Add(map (level + 1) column true)
                    else
                        index <- index + 1
                        items.Add(scalar DataValueKind.String (scalarText false rest))

            arrayOf items

        skipComments ()
        if not (more ()) then refuse "yaml-empty" "A YAML block needs data."
        let first = current ()

        let root =
            if isItem first.Text then list 0 first.Indent
            elif (keyed first.Text).IsSome then map 0 first.Indent false
            else refuse "yaml-text" "A YAML block starts with a key or a list item."

        skipComments ()
        if more () then refuse "yaml-mixed" "A list item cannot sit among the keys of a map."
        root

    // Envelope --------------------------------------------------------------------------------------------------------

    let private highlight (text: string) =
        let body = text.Substring("#highlight".Length).Trim()
        let stereotype = Regex.Match(body, @"\s*(<<.*>>)\s*$")
        let path = if stereotype.Success then body.Substring(0, stereotype.Index) else body

        // `/` separates segments outside double quotes.
        let segments = ResizeArray<string>()
        let current = StringBuilder()
        let mutable quoted = false

        for c in path do
            if c = '"' then quoted <- not quoted
            elif c = '/' && not quoted then
                segments.Add(current.ToString().Trim())
                current.Clear() |> ignore
            else
                current.Append c |> ignore

        segments.Add(current.ToString().Trim())
        DataHighlight(ImmutableArray.CreateRange segments, (if stereotype.Success then Some stereotype.Groups[1].Value else None))

    /// Reads the body of a PlantUML `@startjson` or `@startyaml` block.
    let read (kind: string) (statements: (Reading.Line * int) seq) : Result<Diagram, Diagnostic list> =
        let statements = Seq.toArray statements
        let span = statements |> Array.tryHead |> Option.map (fun (line, start) -> line.Span(start, line.Text.Length))
        let highlights = ResizeArray<DataHighlight>()
        let data = ResizeArray<string>()
        let mutable title = None
        let mutable closing: string option = None

        try
            statements
            |> Array.iteri (fun position (line, start) ->
                let text = line.Text.TrimEnd('\r')
                let trimmed = text.Trim()
                let lower = trimmed.ToLowerInvariant()
                let lineSpan = Some(line.Span(start, line.Text.Length))

                match closing with
                | Some ending -> if lower.StartsWith ending then closing <- None
                | None ->
                    if position = 0 && Regex.IsMatch(trimmed, @"^title\s+\S") then
                        title <- Some(trimmed.Substring(5).Trim())
                    elif lower.StartsWith "<style>" then
                        Reading.warn "plantuml/unapplied-presentation" "Styles are kept in the source but not applied." lineSpan
                        if not (lower.Contains "</style>") then closing <- Some "</style>"
                    elif lower.StartsWith "skinparam" then
                        Reading.warn "plantuml/unapplied-presentation" "Skin parameters are kept in the source but not applied." lineSpan
                        if trimmed.EndsWith '{' then closing <- Some "}"
                    elif start = 0 && trimmed.StartsWith("#highlight", StringComparison.Ordinal) then
                        highlights.Add(highlight trimmed)
                    elif start = 0 && trimmed.StartsWith '#' && kind = "json" then
                        ()
                    elif Regex.IsMatch(text, @"%[A-Za-z_]\w*\(") then
                        refuse "preprocessor-refused" "Preprocessor functions such as %version() are not run."
                    else
                        data.Add text)

            let root =
                if kind = "json" then
                    json (String.Join("\n", data))
                else
                    data
                    |> Seq.map (fun text ->
                        let body = text.TrimStart()
                        { Indent = text.Length - body.Length; Text = body.TrimEnd() })
                    |> List.ofSeq
                    |> yaml

            let format = if kind = "json" then DataFormat.Json else DataFormat.Yaml
            let diagram = DataTreeDiagram(format, root, title, Reading.immutable highlights)

            for unresolved in diagram.Unresolved do
                Reading.warn "plantuml/unresolved-highlight" $"#highlight {unresolved} selects nothing." span

            Ok(Diagram.DataTree diagram)
        with Refused(code, message) ->
            let error = if code = "depth-limit" then Reading.limit "plantuml" "depth" message span else Reading.error $"plantuml/{code}" message span
            Error [ error ]
