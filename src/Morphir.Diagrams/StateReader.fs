// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Text.RegularExpressions

/// Builds a state model statement by statement; shared by the Mermaid and PlantUML readers.
type internal StateBuilder(language: string) =
    let states = Dictionary<DiagramId, string * StateRole * string * ResizeArray<string>>()
    let order = ResizeArray<DiagramId>()
    let regions = ResizeArray<string * DiagramId option>([ "root", None ])
    let transitions = ResizeArray<StateTransition>()
    let notes = ResizeArray<StateNote>()
    let stack = Stack<DiagramId * int>()
    let errors = ResizeArray<Diagnostic>()
    let mutable direction = FlowDirection.TopToBottom

    member _.Errors = errors

    member _.Fail(code: string, message: string, span) = errors.Add(Reading.error $"{language}/{code}" message span)

    member _.Region =
        if stack.Count = 0 then "root" else let composite, index = stack.Peek() in $"{composite}::region-{index}"

    member _.Depth = stack.Count

    member _.Direction with set value = direction <- value

    member this.Has(id: DiagramId) = states.ContainsKey id

    member this.RoleOf(id: DiagramId) = let _, role, _, _ = states[id] in role

    /// Mentions a state: a new state joins the current region; a mention inside a composite moves it there.
    member this.Mention(id: DiagramId, role: StateRole) =
        match states.TryGetValue id with
        | true, (label, existing, region, descriptions) ->
            let region = if this.Region <> "root" then this.Region else region
            let role = if role = StateRole.Composite then role else existing
            states[id] <- (label, role, region, descriptions)
        | false, _ ->
            order.Add id
            states[id] <- (id.Value, role, this.Region, ResizeArray())

    member this.Label(id: DiagramId, label: string) =
        let existing, role, region, descriptions = states[id]
        if existing = id.Value then states[id] <- (label, role, region, descriptions)

    member this.Describe(id: DiagramId, text: string) =
        let _, _, _, descriptions = states[id]
        descriptions.Add text

    /// The region-scoped initial or final state of `[*]`.
    member this.Star(asSource: bool) =
        let id = DiagramId.Create(this.Region + (if asSource then "::initial" else "::final"))
        this.Mention(id, (if asSource then StateRole.Initial else StateRole.Final))
        id

    member this.Open(composite: DiagramId) =
        this.Mention(composite, StateRole.Composite)
        stack.Push(composite, 0)
        regions.Add($"{composite}::region-0", Some composite)

    member this.Divide() =
        let composite, index = stack.Pop()
        stack.Push(composite, index + 1)
        regions.Add($"{composite}::region-{index + 1}", Some composite)

    member _.Close() = stack.Pop() |> ignore

    member _.Transition(source, target, label) = transitions.Add(StateTransition(source, target, label))

    member _.Note(target, text, placement) = notes.Add(StateNote(target, text, placement))

    member _.Build() =
        let built =
            order
            |> Seq.map (fun id ->
                let label, role, region, descriptions = states[id]
                DiagramState(id, label, role, region, Reading.immutable descriptions))

        let regions =
            regions |> Seq.filter (fun (id, owner) -> owner.IsSome || id = "root") |> Seq.map (fun (id, owner) -> StateRegion(id, owner))

        Diagram.State(StateDiagram(direction, Reading.immutable built, Reading.immutable regions, Reading.immutable transitions, Reading.immutable notes))

/// Reads a Mermaid `stateDiagram` or `stateDiagram-v2` body, as Mermaid 12.2.0 does.
[<RequireQualifiedAccess>]
module internal StateReader =
    let private ignoreCase = RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase
    let private identifier = @"(?:\[\*\]|[^\s:{}""<>\[\]]+)"
    let private aliasFirst = Regex(@"^state\s+""(?<label>[^""]*)""\s+as\s+(?<id>[^\s{:]+)\s*(?:(?<open>\{)|:(?<description>.*))?$", ignoreCase ||| RegexOptions.Singleline)
    let private aliasLast = Regex(@"^state\s+(?<id>[^\s{""]+)\s+as\s+""(?<label>[^""]*)""\s*(?<open>\{)?$", ignoreCase ||| RegexOptions.Singleline)
    let private stereotype = Regex(@"^state\s+(?<id>[^\s{<\[]+)\s*(?:<<(?<role>\w+)>>|\[\[(?<role>\w+)\]\])$", ignoreCase)
    let private composite = Regex(@"^state\s+(?<id>[^\s{]+)\s*\{$", ignoreCase)
    let private bare = Regex(@"^state\s+(?<id>[^\s{]+)$", ignoreCase)
    let private transition = Regex($@"^(?<source>{identifier})\s*-->\s*(?<target>{identifier})\s*(?::(?<label>.*))?$", RegexOptions.CultureInvariant)
    let private description = Regex($@"^(?<id>{identifier})\s*:(?<text>.*)$", RegexOptions.CultureInvariant)
    let private note = Regex(@"^note\s+(?<side>left|right)\s+of\s+(?<id>[^\s:]+)\s*(?::(?<text>.*))?$", ignoreCase ||| RegexOptions.Singleline)
    let private direction = Regex(@"^direction\s+(?<value>TB|TD|BT|LR|RL)$", ignoreCase)

    let private presentation = [ "classdef "; "class "; "style "; "click "; "acctitle"; "accdescr"; "title "; "scale "; "hide "; "skinparam " ]
    let private classes = Regex(@":::[\w-]+", RegexOptions.CultureInvariant)

    /// One statement per entry: multi-line quoted labels, note blocks and `accDescr { … }` blocks are joined, and
    /// inline composite bodies `state X { … }` are split at their braces.
    let private statements (lines: seq<Reading.Line * int>) =
        let result = ResizeArray<string * SourceSpan>()
        let mutable pending: (Text.StringBuilder * SourceSpan * string) option = None

        for line, start in lines do
            let text = line.Text.Substring(start).TrimEnd()
            let span = line.Span(start, line.Text.Length)

            match pending with
            | Some(builder, opening, closing) ->
                builder.Append('\n').Append(text) |> ignore

                let finished =
                    match closing with
                    | "\"" -> (builder.ToString() |> Seq.filter ((=) '"') |> Seq.length) % 2 = 0
                    | _ -> String.Equals(text.Trim(), closing, StringComparison.OrdinalIgnoreCase)

                if finished then
                    result.Add(builder.ToString(), opening)
                    pending <- None
            | None ->
                let lower = text.ToLowerInvariant()

                if (text |> Seq.filter ((=) '"') |> Seq.length) % 2 = 1 then
                    pending <- Some(Text.StringBuilder(text), span, "\"")
                elif lower.StartsWith "note " && not (text.Contains ':') && not (text.Contains '"') then
                    pending <- Some(Text.StringBuilder(text), span, "end note")
                elif lower.StartsWith "accdescr" && text.EndsWith '{' then
                    pending <- Some(Text.StringBuilder(text), span, "}")
                elif lower.StartsWith "state " && text.Contains '{' && text.EndsWith '}' && text.IndexOf '{' < text.Length - 1 then
                    let opening = text.IndexOf '{'
                    result.Add(text.Substring(0, opening + 1).Trim(), span)

                    for inner in text.Substring(opening + 1, text.Length - opening - 2).Split(';', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries) do
                        result.Add(inner, span)

                    result.Add("}", span)
                else
                    result.Add(text, span)

        match pending with
        | Some(builder, opening, _) -> result.Add(builder.ToString(), opening)
        | None -> ()

        result

    let read (lines: seq<Reading.Line * int>) : Result<Diagram, Diagnostic list> =
        let builder = StateBuilder "mermaid"

        let id (text: string) = DiagramId.Create text

        for text, span in statements lines do
            let span = Some span
            let trimmed = text.Trim()
            let comment = trimmed.IndexOf("%%", StringComparison.Ordinal)
            let trimmed = if comment > 0 then trimmed.Substring(0, comment).TrimEnd() else trimmed

            let trimmed =
                if classes.IsMatch trimmed && not (trimmed.StartsWith("note", StringComparison.OrdinalIgnoreCase)) then
                    Reading.warn "mermaid/unapplied-presentation" "Classes are kept in the source; the theme decides the colours." span
                    classes.Replace(trimmed, "")
                else
                    trimmed

            let lower = trimmed.ToLowerInvariant()

            let declare (found: Match) =
                let identity = id found.Groups["id"].Value
                builder.Mention(identity, StateRole.Simple)
                builder.Label(identity, found.Groups["label"].Value.Replace("\r\n", "\n"))

                if found.Groups["description"].Success then
                    builder.Describe(identity, found.Groups["description"].Value.Trim())

                if found.Groups["open"].Success then
                    if builder.Depth >= Reading.depthLimit then
                        builder.Errors.Add(Reading.limit "mermaid" "depth" $"Composite states nest deeper than {Reading.depthLimit} levels." span)
                    else
                        builder.Open identity

            if builder.Errors.Count >= Reading.diagnosticLimit then
                ()
            elif presentation |> List.exists lower.StartsWith then
                Reading.warn "mermaid/unapplied-presentation" "Classes, styles and accessibility text are kept in the source but not drawn." span
            elif trimmed = "[*]" || trimmed.StartsWith '#' then
                ()
            elif trimmed = "}" then
                if builder.Depth = 0 then builder.Fail("unexpected-brace", "This `}` closes no composite state.", span) else builder.Close()
            elif trimmed = "--" then
                if builder.Depth = 0 then builder.Fail("misplaced-region", "`--` divides the regions of a composite state.", span) else builder.Divide()
            elif direction.IsMatch trimmed then
                builder.Direction <-
                    match direction.Match(trimmed).Groups["value"].Value.ToUpperInvariant() with
                    | "BT" -> FlowDirection.BottomToTop
                    | "LR" -> FlowDirection.LeftToRight
                    | "RL" -> FlowDirection.RightToLeft
                    | _ -> FlowDirection.TopToBottom
            elif aliasFirst.IsMatch trimmed then
                declare (aliasFirst.Match trimmed)
            elif aliasLast.IsMatch trimmed then
                declare (aliasLast.Match trimmed)
            elif stereotype.IsMatch trimmed then
                let found = stereotype.Match trimmed
                let identity = id found.Groups["id"].Value

                match found.Groups["role"].Value.ToLowerInvariant() with
                | "entrypoint"
                | "exitpoint" ->
                    Reading.warn "mermaid/unapplied-presentation" "Mermaid does not draw entry and exit points; this one is left out." span
                | role when not (builder.Has identity) ->
                    let role =
                        match role with
                        | "choice" -> Some StateRole.Choice
                        | "fork" -> Some StateRole.Fork
                        | "join" -> Some StateRole.Join
                        | _ -> None

                    match role with
                    | Some role -> builder.Mention(identity, role)
                    | None ->
                        let name = found.Groups["role"].Value
                        builder.Fail("unknown-stereotype", $"<<{name}>> is not a state stereotype.", span)
                | _ -> ()
            elif composite.IsMatch trimmed then
                if builder.Depth >= Reading.depthLimit then
                    builder.Errors.Add(Reading.limit "mermaid" "depth" $"Composite states nest deeper than {Reading.depthLimit} levels." span)
                else
                    builder.Open(id (composite.Match(trimmed).Groups["id"].Value))
            elif bare.IsMatch trimmed then
                builder.Mention(id (bare.Match(trimmed).Groups["id"].Value), StateRole.Simple)
            elif lower.StartsWith "note " then
                let found = note.Match((trimmed.Split('\n')[0]).Trim())

                if found.Success then
                    let target = id found.Groups["id"].Value

                    let text =
                        if found.Groups["text"].Success then found.Groups["text"].Value.Trim()
                        else
                            let body = trimmed.Split('\n')
                            String.Join("\n", body[1 .. body.Length - 2] |> Array.map (fun line -> line.Trim()))

                    builder.Mention(target, StateRole.Simple)
                    builder.Note(target, text, (if found.Groups["side"].Value.ToLowerInvariant() = "left" then NotePlacement.Left else NotePlacement.Right))
                elif trimmed.Contains " as " then
                    Reading.warn "mermaid/unapplied-presentation" "Floating notes are kept in the source but not drawn." span
                else
                    builder.Fail("invalid-note", "Expected `note left of` or `note right of` a state.", span)
            elif transition.IsMatch trimmed then
                let found = transition.Match trimmed

                let endpoint (text: string) asSource =
                    if text = "[*]" then builder.Star asSource
                    else
                        let identity = id text
                        if text <> "root" then builder.Mention(identity, StateRole.Simple)
                        identity

                let source = endpoint found.Groups["source"].Value true
                let target = endpoint found.Groups["target"].Value false
                builder.Transition(source, target, (if found.Groups["label"].Success then found.Groups["label"].Value.Trim() else ""))
            elif description.IsMatch trimmed then
                let found = description.Match trimmed
                let identity = id found.Groups["id"].Value

                if builder.Has identity && builder.RoleOf identity = StateRole.Composite then
                    builder.Fail("group-description", $"Composite state {identity} can only have a label.", span)
                else
                    builder.Mention(identity, StateRole.Simple)
                    builder.Describe(identity, found.Groups["text"].Value.Trim())
            elif Regex.IsMatch(trimmed, @"^[^\s:{}""]+$") then
                builder.Mention(id trimmed, StateRole.Simple)
            else
                builder.Fail("unknown-statement", "Expected a state, a transition `-->`, a note or a composite.", span)

        if builder.Depth > 0 then
            builder.Fail("unclosed-composite", "A composite state has no closing `}`.", None)

        if builder.Errors.Count > 0 then Error(List.ofSeq builder.Errors) else Ok(builder.Build())
