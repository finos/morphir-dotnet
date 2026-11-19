// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Globalization
open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
module internal SequenceReader =
    type private SectionBuilder =
        {
            Label: string option
            Events: ResizeArray<SequenceEvent>
        }

    type private FrameBuilder =
        {
            Kind: SequenceFrameKind
            Color: string option
            Opening: SourceSpan
            Sections: ResizeArray<SectionBuilder>
        }

    let private frameKinds =
        [
            SequenceFrameKind.Loop
            SequenceFrameKind.Alternative
            SequenceFrameKind.Optional
            SequenceFrameKind.Parallel
            SequenceFrameKind.ParallelOver
            SequenceFrameKind.Critical
            SequenceFrameKind.Break
            SequenceFrameKind.Rectangle
        ]
        |> List.map (fun kind -> kind.WireValue, kind)
        |> Map.ofList

    let private acceptsSection kind keyword =
        match kind, keyword with
        | SequenceFrameKind.Alternative, "else"
        | SequenceFrameKind.Parallel, "and"
        | SequenceFrameKind.ParallelOver, "and"
        | SequenceFrameKind.Critical, "option" -> true
        | _ -> false

    let private arrows =
        [
            for shaft, dashed in [ "-", false; "--", true ] do
                yield shaft + ">>", (dashed, EdgeHead.None, EdgeHead.Arrow)

                yield "<<" + shaft + ">>", (dashed, EdgeHead.Arrow, EdgeHead.Arrow)

                yield shaft + ">", (dashed, EdgeHead.None, EdgeHead.OpenArrow)

                yield shaft + "x", (dashed, EdgeHead.None, EdgeHead.Cross)

                yield shaft + ")", (dashed, EdgeHead.None, EdgeHead.Async)

                yield shaft + "|\\", (dashed, EdgeHead.None, EdgeHead.HalfUpperFilled)

                yield shaft + "|/", (dashed, EdgeHead.None, EdgeHead.HalfLowerFilled)

                yield shaft + "\\\\", (dashed, EdgeHead.None, EdgeHead.HalfUpper)

                yield shaft + "//", (dashed, EdgeHead.None, EdgeHead.HalfLower)

                yield "/|" + shaft, (dashed, EdgeHead.HalfUpperFilled, EdgeHead.None)

                yield "\\|" + shaft, (dashed, EdgeHead.HalfLowerFilled, EdgeHead.None)

                yield "//" + shaft, (dashed, EdgeHead.HalfUpper, EdgeHead.None)

                yield "\\\\" + shaft, (dashed, EdgeHead.HalfLower, EdgeHead.None)
        ]

    let private arrowByText = Map.ofList arrows

    let private arrowPattern =
        let alternatives =
            arrows
            |> List.map fst
            |> List.sortByDescending (fun value -> value.Length)
            |> List.map Regex.Escape

        Regex(String.concat "|" alternatives, RegexOptions.CultureInvariant)

    let private aliasPattern = Regex(@"\s+as\s+", RegexOptions.CultureInvariant)

    let private numberPattern =
        Regex(@"\A[0-9]*(?:\.[0-9]{1,2})?\z", RegexOptions.CultureInvariant)

    let private tryNumber (text: string) =
        match
            Decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
        with
        | true, number when numberPattern.IsMatch text -> Some number
        | _ -> None

    let private notePattern =
        Regex(
            @"^note\s+(?:(?<left>left)\s+of|(?<right>right)\s+of|over)\s+(?<participants>[^:]+):(?<text>.*)$",
            RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase
        )

    let private readDeclaration kind (text: string) =
        let alias = aliasPattern.Match text

        let id, name =
            if alias.Success then
                text.Substring(0, alias.Index).Trim(),
                Reading.unquote (text.Substring(alias.Index + alias.Length))
            else
                text.Trim(), text.Trim()

        match DiagramId.Parse id with
        | Ok identity when not (String.IsNullOrWhiteSpace name || id.Contains "@{") ->
            Ok(Participant(identity, name, kind))
        | _ -> Error "Expected a participant name with an optional 'as' alias."

    let private readMessage (text: string) number span =
        let colon = text.IndexOf ':'

        let arrow =
            if colon > 0 then
                Some(arrowPattern.Match(text, 0, colon))
            else
                None

        match arrow with
        | Some matched when matched.Success ->
            let sender = text.Substring(0, matched.Index).Trim()

            let receiverStart = matched.Index + matched.Length

            let target = text.Substring(receiverStart, colon - receiverStart).TrimStart()

            let receiver, activation =
                match if target.Length = 0 then '\u0000' else target[0] with
                | '+' -> target.Substring(1).Trim(), Some SequenceActivationKind.Activate
                | '-' -> target.Substring(1).Trim(), Some SequenceActivationKind.Deactivate
                | _ -> target.TrimEnd(), None

            let message = text.Substring(colon + 1).Trim()

            match DiagramId.Parse sender, DiagramId.Parse receiver with
            | Ok _, Ok _ when
                Reading.startsWith "()" receiver || sender.EndsWith("()", StringComparison.Ordinal)
                ->
                Error "This sequence message uses an unsupported central connection."
            | Ok fromId, Ok toId ->
                let dashed, senderHead, receiverHead = arrowByText[matched.Value]

                Ok(
                    SequenceMessage(
                        fromId,
                        toId,
                        message,
                        dashed,
                        senderHead,
                        receiverHead,
                        number,
                        span
                    ),
                    activation
                )
            | _ -> Error "Expected a sender and a receiver around the message arrow."
        | _ -> Error "Expected a participant declaration or a sequence message."

    let read (lines: seq<Reading.Line * int>) =
        let participants = ResizeArray<Participant>()
        let indices = Dictionary<DiagramId, int>()
        let events = ResizeArray<SequenceEvent>()
        let frames = Stack<FrameBuilder>()
        let active = Dictionary<DiagramId, int>()
        let mutable messageCount = 0
        let mutable statementCount = 0
        let mutable numbering = false
        let mutable nextNumber = Some 1m
        let mutable numberStep = 1m
        let nestingLimit = 32

        let addEvent event =
            if frames.Count = 0 then
                events.Add event
            else
                let sections = frames.Peek().Sections
                sections[sections.Count - 1].Events.Add event

        let fail code span message : Result<unit, Diagnostic> =
            Error(Reading.error code message (Some span))

        let statementError span message =
            Reading.error "mermaid/unknown-statement" message (Some span)

        let addParticipant (participant: Participant) declared =
            match indices.TryGetValue participant.Id with
            | true, index ->
                if declared then
                    participants[index] <- participant

                Ok()
            | _ when participants.Count >= Reading.nodeLimit ->
                Error $"Sequence exceeds {Reading.nodeLimit} participants."
            | _ ->
                indices.Add(participant.Id, participants.Count)
                participants.Add participant
                Ok()

        let addImplicit (id: DiagramId) =
            addParticipant (Participant(id, id.Value, ParticipantKind.Participant)) false

        let addActivation kind (id: DiagramId) span =
            addImplicit id
            |> Result.mapError (statementError span)
            |> Result.bind (fun () ->
                let _, depth = active.TryGetValue id

                match kind with
                | SequenceActivationKind.Activate when depth >= nestingLimit ->
                    fail
                        "mermaid/activation-limit"
                        span
                        $"Participant {id} exceeds {nestingLimit} nested activations."
                | SequenceActivationKind.Deactivate when depth = 0 ->
                    fail "mermaid/inactive-deactivation" span $"Participant {id} is not active."
                | _ ->
                    let delta = if kind = SequenceActivationKind.Activate then 1 else -1
                    active[id] <- depth + delta
                    addEvent (SequenceEvent.Activation(SequenceActivation(id, kind, span)))
                    Ok()
            )

        let addNote (matched: Match) span =
            let placement =
                if matched.Groups["left"].Success then
                    NotePlacement.Left
                elif matched.Groups["right"].Success then
                    NotePlacement.Right
                else
                    NotePlacement.Over

            let ids =
                matched.Groups["participants"].Value.Split(',', StringSplitOptions.TrimEntries)

            let limit = if placement = NotePlacement.Over then 2 else 1

            if ids.Length > limit then
                Error
                    "Expected a note anchored to one participant, or an 'over' note anchored to one or two."
            else
                ids
                |> Array.fold
                    (fun result id ->
                        result
                        |> Result.bind (fun participants ->
                            match DiagramId.Parse id with
                            | Ok identity ->
                                addImplicit identity
                                |> Result.map (fun () -> identity :: participants)
                            | Error failure -> Error failure.Message
                        )
                    )
                    (Ok [])
                |> Result.map (fun participants ->
                    addEvent (
                        SequenceEvent.Note(
                            SequenceNote(
                                placement,
                                participants |> List.rev |> Reading.immutable,
                                matched.Groups["text"].Value.Trim(),
                                messageCount,
                                span
                            )
                        )
                    )
                )

        let setNumbering (text: string) span =
            let malformed () =
                fail
                    "mermaid/invalid-autonumber"
                    span
                    "`autonumber` takes a start, a step, or `off`."

            let enable start step =
                numbering <- true

                if start <> 0m then
                    nextNumber <- Some start

                if step <> 0m then
                    numberStep <- step

                Ok()

            match text.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) with
            | [||] -> enable 0m 0m
            | [| mode |] when String.Equals(mode, "off", StringComparison.OrdinalIgnoreCase) ->
                numbering <- false
                Ok()
            | [| start |] ->
                match tryNumber start with
                | Some start -> enable start 0m
                | None -> malformed ()
            | [| start; step |] ->
                match tryNumber start, tryNumber step with
                | Some start, Some step -> enable start step
                | _ -> malformed ()
            | _ -> malformed ()

        let addMessage text span =
            let number = if numbering then nextNumber else None

            if numbering && number.IsNone then
                fail
                    "mermaid/autonumber-limit"
                    span
                    "The next message number exceeds the supported range."
            else
                readMessage text number span
                |> Result.bind (fun (message, kind) ->
                    addImplicit message.Sender
                    |> Result.bind (fun () -> addImplicit message.Receiver)
                    |> Result.map (fun () -> message, kind)
                )
                |> Result.mapError (statementError span)
                |> Result.bind (fun (message, kind) ->
                    addEvent (SequenceEvent.Message message)
                    messageCount <- messageCount + 1

                    if numbering then
                        nextNumber <-
                            number
                            |> Option.bind (fun current ->
                                if current <= Decimal.MaxValue - numberStep then
                                    Some(current + numberStep)
                                else
                                    None
                            )

                    match kind with
                    | Some SequenceActivationKind.Activate ->
                        addActivation SequenceActivationKind.Activate message.Receiver span
                    | Some SequenceActivationKind.Deactivate ->
                        addActivation SequenceActivationKind.Deactivate message.Sender span
                    | None -> Ok()
                )

        let makeSection label =
            {
                Label = label
                Events = ResizeArray()
            }

        let openFrame kind (label: string option) span =
            if frames.Count >= nestingLimit then
                fail "mermaid/frame-limit" span $"Sequence exceeds {nestingLimit} nested frames."
            else
                let color, label =
                    if kind = SequenceFrameKind.Rectangle then
                        label, None
                    else
                        None, label

                frames.Push
                    {
                        Kind = kind
                        Color = color
                        Opening = span
                        Sections = ResizeArray([ makeSection label ])
                    }

                Ok()

        let readSection keyword label span =
            if frames.Count > 0 && acceptsSection (frames.Peek().Kind) keyword then
                frames.Peek().Sections.Add(makeSection label)
                Ok()
            else
                fail
                    "mermaid/misplaced-section"
                    span
                    $"`{keyword}` needs an enclosing frame that allows it."

        let closeFrame span =
            if frames.Count = 0 then
                fail "mermaid/unexpected-end" span "`end` has no open frame."
            else
                let frame = frames.Pop()

                let sections =
                    frame.Sections
                    |> Seq.map (fun section ->
                        SequenceFrameSection(section.Label, Reading.immutable section.Events)
                    )
                    |> Reading.immutable

                addEvent (
                    SequenceEvent.Frame(
                        SequenceFrame(
                            frame.Kind,
                            sections,
                            frame.Color,
                            SourceSpan(frame.Opening.Start, span.End)
                        )
                    )
                )

                Ok()

        let readLine ((line: Reading.Line), start) =
            let text = line.Text.Substring(start).TrimEnd()
            let span = line.Span(start, start + text.Length)
            let cursor = Reading.Cursor(line, start)
            let keyword = cursor.ReadWhile(fun character -> not (Char.IsWhiteSpace character))
            let rest = cursor.Remaining().Trim()
            let label = if rest.Length = 0 then None else Some rest
            let keyword = keyword.ToLowerInvariant()
            statementCount <- statementCount + 1

            let result =
                if statementCount > Reading.edgeLimit then
                    fail
                        "mermaid/sequence-limit"
                        span
                        $"Sequence exceeds {Reading.edgeLimit} statements."
                else
                    match keyword with
                    | "participant"
                    | "actor" ->
                        let kind =
                            if keyword = "actor" then
                                ParticipantKind.Actor
                            else
                                ParticipantKind.Participant

                        readDeclaration kind rest
                        |> Result.bind (fun participant -> addParticipant participant true)
                        |> Result.mapError (statementError span)
                    | "activate"
                    | "deactivate" ->
                        match DiagramId.Parse rest with
                        | Ok id ->
                            let kind =
                                if keyword = "activate" then
                                    SequenceActivationKind.Activate
                                else
                                    SequenceActivationKind.Deactivate

                            addActivation kind id span
                        | Error failure -> fail "mermaid/invalid-activation" span failure.Message
                    | "else"
                    | "and"
                    | "option" -> readSection keyword label span
                    | "end" when label.IsNone -> closeFrame span
                    | "autonumber" -> setNumbering rest span
                    | _ ->
                        match Map.tryFind keyword frameKinds with
                        | Some kind -> openFrame kind label span
                        | None ->
                            let note = notePattern.Match text

                            if note.Success then
                                addNote note span |> Result.mapError (statementError span)
                            else
                                addMessage text span

            match result with
            | Ok() -> None
            | Error diagnostic -> Some diagnostic

        match lines |> Seq.tryPick readLine with
        | Some diagnostic -> Error diagnostic
        | None when frames.Count > 0 ->
            let frame = frames.Peek()

            Error(
                Reading.error
                    "mermaid/missing-end"
                    $"The `{frame.Kind}` frame has no `end`."
                    (Some frame.Opening)
            )
        | None ->
            Ok(
                Diagram.Sequence(
                    SequenceDiagram(Reading.immutable participants, Reading.immutable events)
                )
            )
