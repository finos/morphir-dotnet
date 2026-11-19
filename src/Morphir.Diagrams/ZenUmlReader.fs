// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions

/// Reads a Mermaid `zenuml` body into the shared sequence model, following ZenUML core 3.47.9.
[<RequireQualifiedAccess>]
module internal ZenUmlReader =
    /// The participant that sends top-level synchronous calls and creations.
    let starter = "_STARTER_"

    type private Block =
        | Call of caller: DiagramId * callee: DiagramId * span: SourceSpan
        | Fragment of kind: SequenceFrameKind * sections: ResizeArray<string option * ResizeArray<SequenceEvent>> * span: SourceSpan

    let private identifier = @"[A-Za-z_][A-Za-z0-9_]*|""[^""]+"""

    let private declaration = Regex($@"^(?:@(?<annotator>[A-Za-z0-9_]+)\s+)?(?<id>{identifier})(?:\s+as\s+(?<label>.+))?$", RegexOptions.CultureInvariant)
    let private asyncMessage = Regex($@"^(?<from>{identifier})\s*->\s*(?<to>{identifier})\s*:\s?(?<text>.*)$", RegexOptions.CultureInvariant)

    let private syncMessage =
        Regex(
            $@"^(?:(?<type>{identifier})\s+(?=[A-Za-z_][A-Za-z0-9_]*\s*=))?(?:(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?(?:(?<from>{identifier})\s*->\s*)?(?<to>{identifier})\.(?<method>[A-Za-z_][A-Za-z0-9_]*)(?<args>\(.*\))?\s*(?<open>\{{)?$",
            RegexOptions.CultureInvariant
        )

    let private creation = Regex($@"^new\s+(?<to>{identifier})(?<args>\(.*\))?\s*(?<open>\{{)?$", RegexOptions.CultureInvariant)
    let private fragment = Regex(@"^(?<word>if|while|for|forEach|foreach|loop|opt|par|try)\s*(?:\((?<label>.*)\))?\s*\{$", RegexOptions.CultureInvariant)
    let private continuation = Regex(@"^\}\s*(?<word>else\s+if|else|catch|finally)\s*(?:\((?<label>.*)\))?\s*\{$", RegexOptions.CultureInvariant)

    let private kinds =
        dict [ "Actor", ParticipantKind.Actor; "Database", ParticipantKind.Database; "Boundary", ParticipantKind.Boundary; "Control", ParticipantKind.Control; "Entity", ParticipantKind.Entity; "Collections", ParticipantKind.Collections; "Queue", ParticipantKind.Queue ]

    /// Call arguments as ZenUML labels them: whitespace outside quoted strings is removed.
    let private compact (text: string) =
        let builder = StringBuilder()
        let mutable quoted = false

        for character in text do
            if character = '"' then quoted <- not quoted

            if quoted || not (Char.IsWhiteSpace character) then
                builder.Append character |> ignore

        builder.ToString()

    let private name (text: string) = Reading.unquote text

    let read (statements: (Reading.Line * int) seq) : Result<Diagram, Diagnostic list> =
        let participants = ResizeArray<Participant>()
        let indices = Dictionary<DiagramId, int>()
        let created = Dictionary<DiagramId, int>()
        let root = ResizeArray<SequenceEvent>()
        let blocks = Stack<Block>()
        let errors = ResizeArray<Diagnostic>()
        let mutable messages = 0
        let mutable replyNext = false
        let mutable usesStarter = false

        let add (participant: Participant) explicit =
            match indices.TryGetValue participant.Id with
            | true, index -> if explicit then participants[index] <- participant
            | false, _ ->
                indices.Add(participant.Id, participants.Count)
                participants.Add participant

        let id (text: string) =
            let value = name text
            let identity = DiagramId.Create value

            if value = starter then
                usesStarter <- true
            else
                add (Participant(identity, value, ParticipantKind.Participant)) false

            identity

        // A call's body continues in the innermost fragment section, or at the top level.
        let events () =
            match blocks |> Seq.tryPick (function Fragment(_, sections, _) -> Some sections | Call _ -> None) with
            | Some sections -> snd sections[sections.Count - 1]
            | None -> root

        let context () =
            blocks |> Seq.tryPick (function Call(_, callee, _) -> Some callee | Fragment _ -> None)

        let caller () =
            blocks |> Seq.tryPick (function Call(caller, _, _) -> Some caller | Fragment _ -> None)

        let message (from: DiagramId) (target: DiagramId) text dashed head span =
            let message = SequenceMessage(from, target, text, dashed, EdgeHead.None, head, None, span)
            events().Add(SequenceEvent.Message message)
            messages <- messages + 1

        let activation kind (participant: DiagramId) span =
            events().Add(SequenceEvent.Activation(SequenceActivation(participant, kind, span)))

        let starterId () =
            usesStarter <- true
            DiagramId.Create starter

        for line, start in statements do
            let text = line.Text.Substring(start).Trim()
            let span = Some(line.Span(start, line.Text.Length))
            let fail code message = errors.Add(Reading.error code message span)
            let spanValue = line.Span(start, line.Text.Length)

            if text.StartsWith("//", StringComparison.Ordinal) then
                ()
            elif text.StartsWith("title ", StringComparison.Ordinal) || text = "title" then
                Reading.warn "zenuml/unapplied-presentation" "A ZenUML title is kept in the source but not drawn." span
            elif text = "@return" || text = "@reply" then
                replyNext <- true
            elif blocks.Count >= Reading.depthLimit && text.EndsWith '{' then
                errors.Add(Reading.limit "zenuml" "depth" $"Blocks nest deeper than {Reading.depthLimit} levels." span)
            else
                let matchedAsync = asyncMessage.Match text
                let matchedSync = syncMessage.Match text
                let matchedNew = creation.Match text
                let matchedFragment = fragment.Match text
                let matchedContinuation = continuation.Match text

                if matchedAsync.Success then
                    let from, target = id matchedAsync.Groups["from"].Value, id matchedAsync.Groups["to"].Value
                    let reply = replyNext
                    replyNext <- false
                    message from target (matchedAsync.Groups["text"].Value.TrimEnd()) reply EdgeHead.OpenArrow spanValue
                elif matchedSync.Success then
                    let from =
                        if matchedSync.Groups["from"].Success then id matchedSync.Groups["from"].Value
                        else context () |> Option.defaultWith starterId

                    let target = id matchedSync.Groups["to"].Value

                    let label = matchedSync.Groups["method"].Value + compact matchedSync.Groups["args"].Value
                    message from target label false EdgeHead.Arrow spanValue

                    if matchedSync.Groups["var"].Success then
                        message target from matchedSync.Groups["var"].Value true EdgeHead.OpenArrow spanValue

                    if matchedSync.Groups["open"].Success then
                        activation SequenceActivationKind.Activate target spanValue
                        blocks.Push(Call(from, target, spanValue))
                elif matchedNew.Success then
                    let target = id matchedNew.Groups["to"].Value
                    let from = context () |> Option.defaultWith starterId
                    let args = compact matchedNew.Groups["args"].Value
                    let label = if args.Length > 2 then $"«{args.Substring(1, args.Length - 2)}»" else "«create»"
                    created[target] <- messages
                    message from target label false EdgeHead.Arrow spanValue

                    if matchedNew.Groups["open"].Success then
                        activation SequenceActivationKind.Activate target spanValue
                        blocks.Push(Call(from, target, spanValue))
                elif text.StartsWith("return ", StringComparison.Ordinal) || text = "return" then
                    match context (), caller () with
                    | Some callee, Some back -> message callee back (text.Substring(6).Trim()) true EdgeHead.OpenArrow spanValue
                    | _ -> fail "zenuml/misplaced-return" "`return` needs an enclosing call."
                elif matchedFragment.Success then
                    let label = if matchedFragment.Groups["label"].Success then Some matchedFragment.Groups["label"].Value else None

                    let kind, label =
                        match matchedFragment.Groups["word"].Value with
                        | "if" -> SequenceFrameKind.Alternative, label
                        | "opt" -> SequenceFrameKind.Optional, label
                        | "par" -> SequenceFrameKind.Parallel, label
                        | "try" -> SequenceFrameKind.Try, Some "try"
                        | _ -> SequenceFrameKind.Loop, label

                    blocks.Push(Fragment(kind, ResizeArray [ label, ResizeArray() ], spanValue))
                elif matchedContinuation.Success then
                    match blocks.TryPeek() with
                    | true, Fragment(kind, sections, _) ->
                        let word = Regex.Replace(matchedContinuation.Groups["word"].Value, @"\s+", " ")
                        let label = matchedContinuation.Groups["label"]

                        match kind, word with
                        | SequenceFrameKind.Alternative, ("else if" | "else")
                        | SequenceFrameKind.Try, ("catch" | "finally") ->
                            let text =
                                match word, label.Success with
                                | "else", _ -> "else"
                                | "else if", true -> label.Value
                                | _, true -> $"{word}({label.Value})"
                                | _, false -> word

                            sections.Add(Some text, ResizeArray())
                        | _ -> fail "zenuml/misplaced-section" $"`{word}` does not continue this block."
                    | _ -> fail "zenuml/misplaced-section" $"`{text}` has no block to continue."
                elif text = "}" then
                    match blocks.TryPop() with
                    | true, Call(_, callee, _) -> activation SequenceActivationKind.Deactivate callee spanValue
                    | true, Fragment(kind, sections, opening) ->
                        let frame =
                            SequenceFrame(
                                kind,
                                sections
                                |> Seq.map (fun (label, events) -> SequenceFrameSection(label, Reading.immutable events))
                                |> Reading.immutable,
                                None,
                                SourceSpan(opening.Start, spanValue.End)
                            )

                        events().Add(SequenceEvent.Frame frame)
                    | false, _ -> fail "zenuml/unexpected-brace" "This `}` closes no block."
                else
                    let matchedDeclaration = declaration.Match text

                    if matchedDeclaration.Success then
                        let identity = DiagramId.Create(name matchedDeclaration.Groups["id"].Value)
                        let label = if matchedDeclaration.Groups["label"].Success then name matchedDeclaration.Groups["label"].Value else identity.Value

                        let kind =
                            match matchedDeclaration.Groups["annotator"] with
                            | annotator when annotator.Success ->
                                match kinds.TryGetValue annotator.Value with
                                | true, kind -> kind
                                | false, _ ->
                                    Reading.warn "zenuml/unknown-annotator" $"@{annotator.Value} is drawn as a plain participant." span
                                    ParticipantKind.Participant
                            | _ -> ParticipantKind.Participant

                        add (Participant(identity, label, kind)) true
                    else
                        fail "zenuml/unknown-statement" "Expected a participant, a message, a creation, a return or a block."

        if blocks.Count > 0 then
            let opening = match blocks.Peek() with | Call(_, _, span) | Fragment(_, _, span) -> span
            errors.Add(Reading.error "zenuml/unclosed-block" "This block has no closing `}`." (Some opening))

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            let participants =
                [
                    if usesStarter then
                        yield Participant(DiagramId.Create starter, "", ParticipantKind.Actor)

                    for participant in participants do
                        match created.TryGetValue participant.Id with
                        | true, index -> yield participant.With(Some index, None)
                        | false, _ -> yield participant
                ]

            Ok(Diagram.Sequence(SequenceDiagram(Reading.immutable participants, Reading.immutable root)))
