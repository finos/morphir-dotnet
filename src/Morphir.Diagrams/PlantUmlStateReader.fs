// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Text.RegularExpressions

/// Reads the body of a PlantUML state diagram into the state model, following PlantUML 1.2026.9.
[<RequireQualifiedAccess>]
module internal PlantUmlStateReader =
    let private options = RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase
    let private name = @"(?:""[^""]+""|[^\s""{}:<>#\-]+)"
    let private endpoint = $@"(?:\[\*\]|\[H\*?\]|{name}(?:\[H\*?\])?)"

    let private declaration =
        Regex(
            $@"^state\s+(?:""(?<label>[^""]*)""\s+as\s+(?<id>[^\s{{<#:]+)|(?<id>[^\s""{{<#:]+)\s+as\s+""(?<label>[^""]*)""|(?<id>{name}))\s*(?<stereotype><<[^>]+>>)?(?:\s*#{{1,2}}\S*)*\s*(?:(?<open>\{{)|:(?<description>.*))?$",
            options
        )

    let private arrow =
        Regex(
            $@"^(?<a>{endpoint})\s*(?<left><)?[xo]?-+(?:\[[^\]]*\])?(?:(?:up|down|left|right|u|d|l|r|do|le|ri|up?|dow?n?)-*)?(?:\[[^\]]*\]-*)?(?<right>>)?[xo]?\s*(?<b>{endpoint})\s*(?::(?<label>.*))?$",
            options
        )

    let private description = Regex($@"^(?<id>{name})\s*:(?<text>.*)$", options)
    let private note = Regex(@"^note\s+(?<side>left|right|top|bottom)\s+of\s+(?<id>[^\s:]+)\s*(?::(?<text>.*))?$", options)

    let private ignored =
        [ "skinparam"; "hide"; "show"; "scale"; "mainframe"; "title"; "header"; "footer"; "caption"; "legend"; "endlegend"; "!pragma"; "allowmixing"; "set "; "<style>"; "</style>" ]

    let private stereotypes =
        dict
            [
                "choice", StateRole.Choice
                "fork", StateRole.Fork
                "join", StateRole.Join
                "start", StateRole.Initial
                "end", StateRole.Final
                "entrypoint", StateRole.EntryPoint
                "exitpoint", StateRole.ExitPoint
                "inputpin", StateRole.EntryPoint
                "expansioninput", StateRole.EntryPoint
                "outputpin", StateRole.ExitPoint
                "expansionoutput", StateRole.ExitPoint
                "history", StateRole.ShallowHistory
                "history*", StateRole.DeepHistory
            ]

    let read (statements: (Reading.Line * int) seq) : Result<Diagram, Diagnostic list> =
        let builder = StateBuilder("plantuml", false)
        let mutable skipping: string option = None
        let mutable noteBlock: (DiagramId option * NotePlacement * Text.StringBuilder) option = None
        let composites = Collections.Generic.HashSet<DiagramId>()

        let unquote (text: string) = Reading.unquote text

        /// `B.C` names state C of composite B when B is a composite and C one of its states.
        let resolve (text: string) =
            let text = unquote text
            let dot = text.LastIndexOf '.'

            if dot > 0 && composites.Contains(DiagramId.Create(text.Substring(0, dot))) then
                DiagramId.Create(text.Substring(dot + 1))
            else
                DiagramId.Create text

        let endpointOf (text: string) asSource =
            let history (suffix: string) (owner: string) role =
                let id = DiagramId.Create($"{owner}::{suffix}")
                builder.Mention(id, role)
                id

            match text with
            | "[*]" -> builder.Star asSource
            | "[H]" -> history "history" builder.Region StateRole.ShallowHistory
            | "[H*]" -> history "deep-history" builder.Region StateRole.DeepHistory
            | text when text.EndsWith("[H*]", StringComparison.Ordinal) ->
                history "deep-history" (resolve(text.Substring(0, text.Length - 4)).Value) StateRole.DeepHistory
            | text when text.EndsWith("[H]", StringComparison.Ordinal) ->
                history "history" (resolve(text.Substring(0, text.Length - 3)).Value) StateRole.ShallowHistory
            | text ->
                let id = resolve text
                builder.Mention(id, StateRole.Simple)
                id

        for line, start in statements do
            let text = line.Text.Substring(start).Trim()
            let span = Some(line.Span(start, line.Text.Length))
            let lower = text.ToLowerInvariant()

            match skipping, noteBlock with
            | Some closing, _ ->
                if lower.StartsWith closing then skipping <- None
            | None, Some(target, placement, body) ->
                if lower = "end note" || lower = "endnote" then
                    match target with
                    | Some id -> builder.Note(id, body.ToString(), placement)
                    | None -> ()

                    noteBlock <- None
                else
                    (if body.Length > 0 then body.Append('\n') else body).Append(text) |> ignore
            | None, None ->
                if text.StartsWith('\'') then
                    ()
                elif lower = "left to right direction" then
                    builder.Direction <- FlowDirection.LeftToRight
                elif lower = "top to bottom direction" then
                    builder.Direction <- FlowDirection.TopToBottom
                elif lower.StartsWith "<style>" then
                    if not (lower.Contains "</style>") then skipping <- Some "</style>"
                elif lower.StartsWith "skinparam" && text.EndsWith '{' then
                    skipping <- Some "}"
                elif ignored |> List.exists lower.StartsWith then
                    Reading.warn "plantuml/unapplied-presentation" "Presentation settings are kept in the source but not drawn." span
                elif lower = "legend" || lower.StartsWith "legend " then
                    skipping <- Some "endlegend"
                elif text = "}" then
                    if builder.Depth = 0 then builder.Fail("unexpected-brace", "This `}` closes no composite state.", span) else builder.Close()
                elif text = "--" || text = "||" then
                    if builder.Depth = 0 then builder.Fail("misplaced-region", $"`{text}` divides the regions of a composite state.", span) else builder.Divide()
                elif lower.StartsWith "note " then
                    let found = note.Match text

                    if found.Success then
                        let target = resolve found.Groups["id"].Value
                        builder.Mention(target, StateRole.Simple)
                        let placement = if found.Groups["side"].Value.ToLowerInvariant() = "left" then NotePlacement.Left else NotePlacement.Right

                        if found.Groups["text"].Success then builder.Note(target, found.Groups["text"].Value.Trim(), placement)
                        else noteBlock <- Some(Some target, placement, Text.StringBuilder())
                    elif lower.StartsWith "note on link" || not (text.Contains '"') then
                        Reading.warn "plantuml/unapplied-presentation" "Notes on links and floating notes are kept in the source but not drawn." span
                        if not (text.Contains ':') then noteBlock <- Some(None, NotePlacement.Right, Text.StringBuilder())
                    else
                        Reading.warn "plantuml/unapplied-presentation" "Floating notes are kept in the source but not drawn." span
                elif lower.StartsWith "state " then
                    let found = declaration.Match text

                    if not found.Success then
                        builder.Fail("invalid-state", "Expected `state Name`, an alias, a stereotype, a description or `{`.", span)
                    else
                        let id = DiagramId.Create(unquote found.Groups["id"].Value)

                        let role =
                            match found.Groups["stereotype"].Value.Trim('<', '>').ToLowerInvariant() with
                            | "" -> StateRole.Simple
                            | stereotype ->
                                match stereotypes.TryGetValue stereotype with
                                | true, role -> role
                                | false, _ -> StateRole.Simple

                        if found.Groups["open"].Success then
                            if builder.Depth >= Reading.depthLimit then
                                builder.Errors.Add(Reading.limit "plantuml" "depth" $"Composite states nest deeper than {Reading.depthLimit} levels." span)
                            else
                                composites.Add id |> ignore
                                builder.Open id
                        else
                            builder.Mention(id, role)

                        if found.Groups["label"].Success then builder.Label(id, found.Groups["label"].Value)
                        if found.Groups["description"].Success then builder.Describe(id, found.Groups["description"].Value.Trim())
                elif arrow.IsMatch text then
                    let found = arrow.Match text
                    let first, second = found.Groups["a"].Value, found.Groups["b"].Value
                    let reversed = found.Groups["left"].Success && not found.Groups["right"].Success
                    let sourceText, targetText = if reversed then second, first else first, second
                    let source = endpointOf sourceText true
                    let target = endpointOf targetText false
                    builder.Transition(source, target, (if found.Groups["label"].Success then found.Groups["label"].Value.Trim() else ""))
                elif description.IsMatch text then
                    let found = description.Match text
                    let id = resolve found.Groups["id"].Value
                    builder.Mention(id, StateRole.Simple)
                    builder.Describe(id, found.Groups["text"].Value.Trim())
                elif Regex.IsMatch(text, $@"^{name}$") then
                    builder.Mention(resolve text, StateRole.Simple)
                else
                    builder.Fail("unknown-statement", "Expected a state, a transition, a note or a composite.", span)

        if builder.Depth > 0 then builder.Fail("unclosed-composite", "A composite state has no closing `}`.", None)

        if builder.Errors.Count > 0 then
            Error(List.ofSeq builder.Errors)
        else
            match builder.Build() with
            | Diagram.State model ->
                // PlantUML refuses a transition between two concurrent regions of one composite.
                let regionOf = model.States |> Seq.map (fun state -> state.Id, state.Region) |> dict
                let ownerOf = model.Regions |> Seq.map (fun region -> region.Id, region.Owner) |> dict

                let crossing =
                    model.Transitions
                    |> Seq.tryFind (fun transition ->
                        match regionOf.TryGetValue transition.Source, regionOf.TryGetValue transition.Target with
                        | (true, first), (true, second) when first <> second ->
                            not (isNull ownerOf[first]) && ownerOf[first] = ownerOf[second]
                        | _ -> false)

                match crossing with
                | Some transition ->
                    Error [ Reading.error "plantuml/cross-region" $"Transition {transition} crosses between concurrent regions." None ]
                | None -> Ok(Diagram.State model)
            | other -> Ok other
