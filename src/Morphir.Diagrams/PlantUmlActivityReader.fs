// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Text.RegularExpressions

/// Builds a swimlane model from activity statements: steps join the open tails of the flow.
type internal ActivityBuilder() =
    let steps = ResizeArray<SwimlaneStep>()
    let lanes = ResizeArray<SwimlaneLane>()
    let laneIds = HashSet<DiagramId>()
    let connectors = ResizeArray<SwimlaneConnector>()
    let mutable lane: DiagramId option = None
    let mutable direction = FlowDirection.TopToBottom

    /// Ends of the flow waiting for the next step, each with the label of the connector to it.
    member val Tails: (DiagramId * string option) list = [] with get, set

    /// The label and visibility that the next connector takes from an arrow statement.
    member val Arrow: string option * bool = (None, true) with get, set

    member _.Direction with get () = direction and set value = direction <- value

    member _.Lane(name: string, parent: DiagramId option) =
        let id = DiagramId.Create name

        if laneIds.Add id then lanes.Add(SwimlaneLane(id, name, parent))
        lane <- Some id

    member _.CurrentLane with get () = lane and set value = lane <- value

    member _.Connect(source: DiagramId, target: DiagramId, label: string option, line: FlowLine, head: bool) =
        connectors.Add(SwimlaneConnector(source, target, label, line, head))

    /// A new step, joined from every open tail; it becomes the only tail.
    member this.Step(label: string, role: SwimlaneRole, shape: FlowShape) =
        let id = DiagramId.Create $"step-{steps.Count + 1}"
        steps.Add(SwimlaneStep(id, label, role, lane, shape))
        let arrowLabel, visible = this.Arrow

        for tail, label in this.Tails do
            if visible then this.Connect(tail, id, Option.orElse arrowLabel label, FlowLine.Solid, true)

        this.Arrow <- (None, true)
        this.Tails <- [ id, None ]
        id

    /// A step that joins nothing, such as a note.
    member _.Detached(label: string, role: SwimlaneRole, shape: FlowShape) =
        let id = DiagramId.Create $"step-{steps.Count + 1}"
        steps.Add(SwimlaneStep(id, label, role, lane, shape))
        id

    member _.Build() =
        Diagram.Swimlane(SwimlaneDiagram(direction, Reading.immutable lanes, Reading.immutable steps, Reading.immutable connectors))

/// Reads the body of a PlantUML activity diagram in either dialect, following PlantUML 1.2026.9.
[<RequireQualifiedAccess>]
module internal PlantUmlActivityReader =
    type private Block =
        | If of decision: DiagramId * ends: ResizeArray<DiagramId * string option> * hasElse: bool ref
        | Loop of decision: DiagramId * exits: ResizeArray<DiagramId * string option> * backward: DiagramId option ref
        | Repeat of start: DiagramId * exits: ResizeArray<DiagramId * string option> * backward: DiagramId option ref
        | Fork of bar: DiagramId * ends: ResizeArray<DiagramId * string option>
        | Group

    let private options = RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase
    let private condition = Regex(@"^(?<word>if|elseif|while|switch|case|else)\s*(?:\((?<test>.*?)\))?\s*(?:(?:then|is|equals)\s*\((?<label>[^)]*)\))?\s*(?:then)?\s*;?$", options)
    let private closing = Regex(@"^(?<word>endif|endwhile|endswitch|end\s+(?:fork|merge|split|group)|endfork|endsplit|endmerge)\s*(?:\((?<label>[^)]*)\))?\s*(?:\{[^}]*\})?\s*;?$", options)
    let private repeatWhile = Regex(@"^repeat\s*while\s*(?:\((?<test>.*?)\))?\s*(?:is\s*\((?<yes>[^)]*)\))?\s*(?:not\s*\((?<no>[^)]*)\))?\s*;?$", options)
    let private arrowLine = Regex(@"^-+(?:\[(?<style>[^\]]*)\])?-*>\s*(?<label>[^;]*);?$", options)
    let private laneLine = Regex(@"^\|(?:#[^|]*\|)?(?<name>[^|]+)\|(?<label>.*)$", options)
    let private grouping = Regex(@"^(?<word>partition|group|package|rectangle|card)\s+(?<name>""[^""]*""|[^{]+?)\s*(?:#\S+)?\s*\{?$", options)
    let private note = Regex(@"^(?:floating\s+)?note\s*(?<side>left|right)?\s*(?::(?<text>.*))?$", options)

    let private ignored = [ "skinparam"; "link "; "title"; "header"; "footer"; "caption"; "scale"; "hide"; "!pragma"; "allowmixing"; "legend"; "<style>"; "label "; "goto " ]

    /// The shape an action's closing character asks for: `;` box, `|` `<` `>` `/` `]` `}` SDL forms.
    let private actionShape (closing: char) =
        match closing with
        | '<'
        | '>' -> FlowShape.Asymmetric
        | '/' -> FlowShape.Parallelogram
        | '|' -> FlowShape.Subroutine
        | '}' -> FlowShape.Hexagon
        | _ -> FlowShape.Rounded

    let private externalAsset = Regex(@"<img:|<\$|\{\{", options)

    /// One statement per entry: a multi-line action `:… ;` and a note block are joined.
    let private statements (legacy: bool) (body: (Reading.Line * int) seq) =
        let result = ResizeArray<string * SourceSpan>()
        // The statement in progress, with the test that ends it.
        let mutable pending: (Text.StringBuilder * SourceSpan * (string -> bool)) option = None
        let terminated (text: string) = text.Length > 0 && ";|<>/]}".Contains text[text.Length - 1]
        let depth = ref 0

        for line, start in body do
            let text = line.Text.Substring(start).Trim()
            let span = line.Span(start, line.Text.Length)

            match pending with
            | Some(builder, opening, finished) ->
                builder.Append('\n').Append(text) |> ignore

                if finished text then
                    result.Add(builder.ToString(), opening)
                    pending <- None
            | None ->
                let lower = text.ToLowerInvariant()

                let endNote (line: string) = let lower = line.ToLowerInvariant() in lower = "end note" || lower = "endnote"

                // A brace block ends at the brace that balances its opening one.
                let balanced (line: string) =
                    depth.Value <- depth.Value + (line |> Seq.filter ((=) '{') |> Seq.length) - (line |> Seq.filter ((=) '}') |> Seq.length)
                    depth.Value <= 0

                let text = Regex.Replace(text, @"\s*<<[^>]*>>\s*$", "")

                if text.StartsWith ':' && not (text.Length > 1 && terminated text) then
                    pending <- Some(Text.StringBuilder(text), span, terminated)
                elif not legacy && text.StartsWith '-' && Regex.IsMatch(text, @"^-+(\[[^\]]*\])?-*>\s*\S") && not (text.EndsWith ';') then
                    pending <- Some(Text.StringBuilder(text), span, (fun line -> line.EndsWith ';'))
                elif (lower.StartsWith "note " || lower = "note" || lower.StartsWith "floating note") && not (text.Contains ':') then
                    pending <- Some(Text.StringBuilder(text), span, endNote)
                elif lower.StartsWith "<style>" && not (lower.Contains "</style>") then
                    pending <- Some(Text.StringBuilder(text), span, (fun line -> line.ToLowerInvariant().Contains "</style>"))
                elif lower.StartsWith "skinparam" && text.EndsWith '{' then
                    depth.Value <- 1
                    pending <- Some(Text.StringBuilder(text), span, balanced)
                else
                    result.Add(text, span)

        match pending with
        | Some(builder, opening, _) -> result.Add(builder.ToString(), opening)
        | None -> ()

        result

    let private isLegacy (all: ResizeArray<string * SourceSpan>) =
        all |> Seq.exists (fun (text, _) -> text.Contains "(*)" || Regex.IsMatch(text, @"===[^=]+===") || Regex.IsMatch(text, @"^""[^""]*""(?:\s+as\s+\w+)?\s*-+>"))

    let private isModern (all: ResizeArray<string * SourceSpan>) =
        all |> Seq.exists (fun (text, _) -> text.StartsWith ':' || text = "start" || text = "stop")

    let private modern (all: ResizeArray<string * SourceSpan>) =
        let builder = ActivityBuilder()
        let blocks = Stack<Block>()
        let errors = ResizeArray<Diagnostic>()
        let fail code message span = errors.Add(Reading.error $"plantuml/{code}" message (Some span))
        let label (text: string) = if String.IsNullOrWhiteSpace text then None else Some(text.Trim())
        let mutable lastStep: DiagramId option = None

        let step text role shape =
            let id = builder.Step(text, role, shape)
            lastStep <- Some id
            id

        let close role =
            let id = step "" role (if role = SwimlaneRole.Stop then FlowShape.FramedCircle else FlowShape.CrossedCircle)
            builder.Tails <- []
            id

        for text, span in all do
            // A label of the branch that ends may precede the keyword: `(no) elseif (…)`.
            let text = Regex.Replace(text, @"^\([^)]*\)\s*(?=(?:elseif|else|endif)\b)", "", options)
            let lower = text.ToLowerInvariant()

            if errors.Count >= Reading.diagnosticLimit then
                ()
            elif text.StartsWith '\'' || text.Length = 0 then
                ()
            elif ignored |> List.exists lower.StartsWith then
                Reading.warn "plantuml/unapplied-presentation" "Presentation and jumps are kept in the source but not drawn." (Some span)
            elif externalAsset.IsMatch text then
                fail "external-asset" "Labels that load images or sprites are refused; no file or network is read." span
            elif lower = "start" then
                step "" SwimlaneRole.Start FlowShape.FilledCircle |> ignore
            elif lower = "stop" || lower = "end" then
                close SwimlaneRole.Stop |> ignore
            elif lower = "kill" || lower = "detach" then
                close SwimlaneRole.End |> ignore
            elif text.StartsWith ':' then
                let closingChar = text[text.Length - 1]
                step (text.Substring(1, text.Length - 2).Trim()) SwimlaneRole.Action (actionShape closingChar) |> ignore
            elif lower.StartsWith "backward" then
                // The action of a repeat's loop back; it is drawn on that connector's way.
                match blocks.TryPeek() with
                | true, Repeat(_, _, backward)
                | true, Loop(_, _, backward) ->
                    let body = text.Substring(text.IndexOf ':' + 1).TrimEnd(';').Trim()
                    backward.Value <- Some(builder.Detached(body, SwimlaneRole.Action, FlowShape.Rounded))
                | _ -> fail "misplaced-backward" "`backward` belongs inside `repeat`." span
            elif arrowLine.IsMatch text then
                let found = arrowLine.Match text
                let hidden = found.Groups["style"].Value.Contains("hidden", StringComparison.OrdinalIgnoreCase)
                builder.Arrow <- (label found.Groups["label"].Value, not hidden)
            elif laneLine.IsMatch text then
                builder.Lane(laneLine.Match(text).Groups["name"].Value.Trim(), None)
            elif Regex.IsMatch(text, @"^(?:#\S+\s*:\s*)?\(\w+\)$") then
                // A spot: a connector circle that a goto or a page break joins.
                let name = Regex.Match(text, @"\((\w+)\)").Groups[1].Value
                step name SwimlaneRole.Merge FlowShape.SmallCircle |> ignore
            elif Regex.IsMatch(text, @"^(?:\*+|-)\s*\S") then
                // A list item, `- Action` or `* Action`, is an action of its own.
                step (text.TrimStart('*', '-').Trim()) SwimlaneRole.Action FlowShape.Rounded |> ignore
            elif (lower = "repeat" || lower.StartsWith "repeat " || lower.StartsWith "repeat:") && not (lower.StartsWith "repeat while" || lower.StartsWith "repeatwhile") then
                // `repeat :label;` labels the repeat's own start, where the loop returns.
                let rest = text.Substring(6).Trim()
                let label = if rest.StartsWith ':' then rest.Substring(1).TrimEnd(';').Trim() else ""
                let start = step label SwimlaneRole.Merge (if label = "" then FlowShape.SmallCircle else FlowShape.Rounded)
                blocks.Push(Repeat(start, ResizeArray(), ref None))
            elif repeatWhile.IsMatch(lower.Replace("repeatwhile", "repeat while")) then
                let found = repeatWhile.Match(text.Replace("repeatwhile", "repeat while"))

                match blocks.TryPop() with
                | true, Repeat(start, exits, backward) ->
                    let decision = step found.Groups["test"].Value SwimlaneRole.Decision FlowShape.Diamond

                    match backward.Value with
                    | Some back ->
                        builder.Connect(decision, back, label found.Groups["yes"].Value, FlowLine.Solid, true)
                        builder.Connect(back, start, None, FlowLine.Solid, true)
                    | None -> builder.Connect(decision, start, label found.Groups["yes"].Value, FlowLine.Solid, true)

                    builder.Tails <- (decision, label found.Groups["no"].Value) :: List.ofSeq exits
                | _ -> fail "misplaced-repeat" "`repeat while` closes no `repeat`." span
            elif lower = "break" then
                match blocks |> Seq.tryPick (function Loop(_, exits, _) | Repeat(_, exits, _) -> Some exits | _ -> None) with
                | Some exits ->
                    exits.AddRange builder.Tails
                    builder.Tails <- []
                | None -> fail "misplaced-break" "`break` belongs inside a loop." span
            elif lower = "fork" || lower = "split" then
                let bar = step "" SwimlaneRole.Fork FlowShape.Bar
                blocks.Push(Fork(bar, ResizeArray()))
            elif lower = "fork again" || lower = "split again" then
                match blocks.TryPeek() with
                | true, Fork(bar, ends) ->
                    ends.AddRange builder.Tails
                    builder.Tails <- [ bar, None ]
                | _ -> fail "misplaced-fork" $"`{text}` continues no fork." span
            elif closing.IsMatch text then
                let found = closing.Match text
                let word = Regex.Replace(found.Groups["word"].Value.ToLowerInvariant(), @"\s+", "")

                match blocks.TryPop() with
                | true, If(decision, ends, hasElse) when word = "endif" || word = "endswitch" ->
                    ends.AddRange builder.Tails

                    // With no else, the test's other outcome goes straight to where the branches meet.
                    if not hasElse.Value && word = "endif" then ends.Add(decision, None)

                    let joined = List.ofSeq ends
                    builder.Tails <- joined
                    if not joined.IsEmpty then step "" SwimlaneRole.Merge FlowShape.Diamond |> ignore
                | true, Loop(decision, exits, backward) when word = "endwhile" ->
                    for tail, tailLabel in builder.Tails do
                        match backward.Value with
                        | Some back -> builder.Connect(tail, back, tailLabel, FlowLine.Solid, true)
                        | None -> builder.Connect(tail, decision, tailLabel, FlowLine.Solid, true)

                    backward.Value |> Option.iter (fun back -> builder.Connect(back, decision, None, FlowLine.Solid, true))

                    builder.Tails <- (decision, label found.Groups["label"].Value) :: List.ofSeq exits
                | true, Fork(_, ends) when word.StartsWith "end" ->
                    ends.AddRange builder.Tails
                    builder.Tails <- List.ofSeq ends
                    step "" SwimlaneRole.Join FlowShape.Bar |> ignore
                | true, Group when word = "endgroup" -> builder.CurrentLane <- None
                | true, other ->
                    blocks.Push other
                    fail "mismatched-end" $"`{text}` does not close the open block." span
                | false, _ -> fail "misplaced-end" $"`{text}` closes no block." span
            elif condition.IsMatch text then
                let found = condition.Match text
                let test, branch = found.Groups["test"].Value, label found.Groups["label"].Value

                match found.Groups["word"].Value.ToLowerInvariant() with
                | "if"
                | "switch" ->
                    if blocks.Count >= Reading.depthLimit then
                        errors.Add(Reading.limit "plantuml" "depth" $"Blocks nest deeper than {Reading.depthLimit} levels." (Some span))
                    else
                        let decision = step test SwimlaneRole.Decision FlowShape.Diamond
                        blocks.Push(If(decision, ResizeArray(), ref false))
                        builder.Tails <- [ decision, branch ]
                | "while" ->
                    let decision = step test SwimlaneRole.Decision FlowShape.Diamond
                    blocks.Push(Loop(decision, ResizeArray(), ref None))
                    builder.Tails <- [ decision, branch ]
                | word ->
                    match blocks.TryPeek() with
                    | true, If(decision, ends, hasElse) ->
                        ends.AddRange builder.Tails
                        if word = "else" then hasElse.Value <- true
                        let branchLabel =
                            if word = "case" || word = "elseif" then label test |> Option.orElse branch
                            elif word = "else" then label test |> Option.orElse branch
                            else branch
                        builder.Tails <- [ decision, branchLabel ]
                    | _ -> fail "misplaced-branch" $"`{word}` continues no `if` or `switch`." span
            elif lower = "}" then
                match blocks.TryPop() with
                | true, Group -> builder.CurrentLane <- None
                | true, other ->
                    blocks.Push other
                    fail "mismatched-end" "`}` does not close the open block." span
                | false, _ -> fail "misplaced-end" "`}` closes no group." span
            elif grouping.IsMatch text then
                let found = grouping.Match text
                builder.Lane(Reading.unquote found.Groups["name"].Value, builder.CurrentLane)
                blocks.Push Group
            elif note.IsMatch(text.Split('\n')[0]) then
                let found = note.Match(text.Split('\n')[0])

                let body =
                    if found.Groups["text"].Success then found.Groups["text"].Value.Trim()
                    else
                        let parts = text.Split('\n')
                        String.Join("\n", parts[1 .. parts.Length - 2] |> Array.map (fun part -> part.Trim()))

                let noteId = builder.Detached(body, SwimlaneRole.Note, FlowShape.Note)

                match lastStep with
                | Some step when not (text.StartsWith("floating", StringComparison.OrdinalIgnoreCase)) ->
                    builder.Connect(noteId, step, None, FlowLine.Dotted, false)
                | _ -> ()
            else
                fail "unknown-statement" "Expected an action, a block, a lane, a note or an arrow." span

        // Ends of a branch that a stop terminated leave no open tail; blocks still open are reported.
        if blocks |> Seq.exists (function Group -> false | _ -> true) then
            errors.Add(Reading.error "plantuml/unclosed-block" "A block has no closing statement." None)

        if errors.Count > 0 then Error(List.ofSeq errors) else Ok(builder.Build())

    let private legacy (all: ResizeArray<string * SourceSpan>) =
        let builder = ActivityBuilder()
        let errors = ResizeArray<Diagnostic>()
        let named = Dictionary<string, DiagramId>(StringComparer.Ordinal)
        let branches = Stack<DiagramId * ResizeArray<DiagramId * string option> * bool ref>()
        let mutable last: DiagramId option = None
        let mutable start: DiagramId option = None
        let mutable finish: DiagramId option = None
        let fail code message span = errors.Add(Reading.error $"plantuml/{code}" message (Some span))

        let endpoint = @"\(\*\)(?:\s*top)?|""[^""]*""(?:\s+as\s+\w+)?|={2,}[^=]+={2,}|\w+"
        let inlineIf = Regex($@"^(?:(?<a>{endpoint})\s*)?-+[^>]*>\s*if\s+""(?<test>[^""]*)""\s+then$", options)
        let edge = Regex($@"^(?:(?<a>{endpoint})\s*)?-+(?:\[(?<style>[^\]]*)\])?(?:left|right|up|down|l|r|u|d)?-*>\s*(?:\[(?<label>[^\]]*)\]\s*)?(?<b>{endpoint})$", options)

        let node (text: string) asTarget =
            let text = text.Trim()

            let make key label role shape =
                match named.TryGetValue key with
                | true, id -> id
                | false, _ ->
                    let id = builder.Detached(label, role, shape)
                    named[key] <- id
                    id

            if text.StartsWith "(*)" then
                if asTarget then
                    finish <- Some(finish |> Option.defaultWith (fun () -> builder.Detached("", SwimlaneRole.Stop, FlowShape.FramedCircle)))
                    finish.Value
                else
                    start <- Some(start |> Option.defaultWith (fun () -> builder.Detached("", SwimlaneRole.Start, FlowShape.FilledCircle)))
                    start.Value
            elif text.StartsWith "==" then
                make text "" SwimlaneRole.Fork FlowShape.Bar
            elif text.StartsWith '"' then
                let close = text.IndexOf('"', 1)
                let label = text.Substring(1, close - 1).Replace("\\n", "\n")
                let alias = text.Substring(close + 1).Trim()
                let key = if alias.StartsWith("as ", StringComparison.OrdinalIgnoreCase) then alias.Substring(3).Trim() else label
                let id = make key label SwimlaneRole.Action FlowShape.Rounded
                if key <> label then named[label] <- id
                id
            else
                make text text SwimlaneRole.Action FlowShape.Rounded

        let connect source target label =
            match branches.TryPeek() with
            | true, (decision, _, _) when source = decision -> builder.Connect(source, target, label, FlowLine.Solid, true)
            | _ -> builder.Connect(source, target, label, FlowLine.Solid, true)

        for text, span in all do
            let lower = text.ToLowerInvariant()

            if text.StartsWith '\'' || text.Length = 0 then
                ()
            elif ignored |> List.exists lower.StartsWith then
                Reading.warn "plantuml/unapplied-presentation" "Presentation is kept in the source but not drawn." (Some span)
            elif externalAsset.IsMatch text then
                fail "external-asset" "Labels that load images or sprites are refused; no file or network is read." span
            elif lower.StartsWith "if " || inlineIf.IsMatch text then
                let found = inlineIf.Match text

                if found.Success && found.Groups["a"].Success then
                    last <- Some(node found.Groups["a"].Value false)

                let test = Regex.Match(text, @"""([^""]*)""").Groups[1].Value
                let decision = builder.Detached(test, SwimlaneRole.Decision, FlowShape.Diamond)

                match last with
                | Some previous -> builder.Connect(previous, decision, None, FlowLine.Solid, true)
                | None -> ()

                branches.Push(decision, ResizeArray(), ref false)
                last <- Some decision
            elif lower = "else" || lower.StartsWith "else " then
                match branches.TryPeek() with
                | true, (decision, ends, _) ->
                    last |> Option.iter (fun id -> ends.Add(id, None))
                    last <- Some decision
                | false, _ -> fail "misplaced-branch" "`else` continues no `if`." span
            elif lower = "endif" then
                // As PlantUML does, the flow goes on from the last activity; the legacy dialect has no merge.
                match branches.TryPop() with
                | true, _ -> ()
                | false, _ -> fail "misplaced-end" "`endif` closes no `if`." span
            elif laneLine.IsMatch text then
                builder.Lane(laneLine.Match(text).Groups["name"].Value.Trim(), None)
            elif lower = "}" || lower.StartsWith "end partition" then
                builder.CurrentLane <- None
            elif grouping.IsMatch text then
                builder.Lane(Reading.unquote (grouping.Match(text).Groups["name"].Value), None)
            elif note.IsMatch(text.Split('\n')[0]) then
                let found = note.Match(text.Split('\n')[0])
                let body = if found.Groups["text"].Success then found.Groups["text"].Value.Trim() else String.Join("\n", text.Split('\n') |> Array.skip 1 |> Array.truncate (max 0 (text.Split('\n').Length - 2)))
                let noteId = builder.Detached(body.Trim(), SwimlaneRole.Note, FlowShape.Note)
                last |> Option.iter (fun id -> builder.Connect(noteId, id, None, FlowLine.Dotted, false))
            elif edge.IsMatch text then
                let found = edge.Match text
                let hidden = found.Groups["style"].Value.Contains("hidden", StringComparison.OrdinalIgnoreCase)

                let source =
                    if found.Groups["a"].Success then Some(node found.Groups["a"].Value false) else last

                let target = node found.Groups["b"].Value true

                match source with
                | Some source when not hidden ->
                    connect source target (if found.Groups["label"].Success then Some(found.Groups["label"].Value.Replace("\\n", "\n")) else None)
                | Some _ -> ()
                | None -> fail "missing-source" "An arrow with no source needs an earlier activity." span

                last <- Some target
            else
                fail "unknown-statement" "Expected an arrow, `if`, `else`, `endif`, a partition or a note." span

        if branches.Count > 0 then errors.Add(Reading.error "plantuml/unclosed-block" "An `if` has no `endif`." None)
        if errors.Count > 0 then Error(List.ofSeq errors) else Ok(builder.Build())

    let read (body: (Reading.Line * int) seq) : Result<Diagram, Diagnostic list> =
        let legacyLines =
            body |> Seq.exists (fun (line, start) -> let text = line.Text.Substring start in text.Contains "(*)" || Regex.IsMatch(text, @"={2,}[^=]+={2,}|^\s*""[^""]*""(?:\s+as\s+\w+)?\s*-+>"))

        let all = statements legacyLines body

        match isLegacy all, isModern all with
        | true, true ->
            Error [ Reading.error "plantuml/mixed-activity-dialect" "The body mixes the legacy `(*)` dialect with modern `:action;` statements." None ]
        | true, false -> legacy all
        | false, _ -> modern all
