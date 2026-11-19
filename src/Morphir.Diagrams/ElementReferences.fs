// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Runtime.InteropServices
open System.Text
open Morphir.Knowledge

/// The kind of a diagram element.
[<RequireQualifiedAccess>]
type DiagramElementKind =
    /// A flowchart node.
    | Node
    /// A flowchart subgraph.
    | Group
    /// A flowchart link.
    | Edge
    /// A sequence lifeline.
    | Participant
    /// A sequence message.
    | Message
    /// A sequence note.
    | Note
    /// A sequence frame such as loop or alt.
    | Frame
    /// A playback step.
    | Step

    /// The kind as a reference writes it, such as node in node=Billing.
    member this.WireValue =
        match this with
        | DiagramElementKind.Node -> "node"
        | DiagramElementKind.Group -> "group"
        | DiagramElementKind.Edge -> "edge"
        | DiagramElementKind.Participant -> "participant"
        | DiagramElementKind.Message -> "message"
        | DiagramElementKind.Note -> "note"
        | DiagramElementKind.Frame -> "frame"
        | DiagramElementKind.Step -> "step"

    override this.ToString() = this.WireValue

/// Why a reference or an element IRI could not be read or formed.
[<RequireQualifiedAccess>]
type DiagramReferenceError =
    /// The reference was absent or empty.
    | Empty
    /// The text before = is not a known element kind.
    | UnknownKind of kind: string
    /// The key does not fit its kind: a bad identifier, ordinal or percent-encoding.
    | InvalidKey of reference: string
    /// The document IRI already has a fragment, which the element reference must own.
    | DocumentHasFragment of document: Iri
    /// The IRI is not an element IRI of the document.
    | NotInDocument of iri: Iri

    /// For C# callers: every case needs a function.
    member this.Match
        (
            empty: Func<'T>,
            unknownKind: Func<string, 'T>,
            invalidKey: Func<string, 'T>,
            documentHasFragment: Func<Iri, 'T>,
            notInDocument: Func<Iri, 'T>
        ) =
        match this with
        | DiagramReferenceError.Empty -> empty.Invoke()
        | DiagramReferenceError.UnknownKind kind -> unknownKind.Invoke kind
        | DiagramReferenceError.InvalidKey reference -> invalidKey.Invoke reference
        | DiagramReferenceError.DocumentHasFragment document -> documentHasFragment.Invoke document
        | DiagramReferenceError.NotInDocument iri -> notInDocument.Invoke iri

    override this.ToString() =
        match this with
        | DiagramReferenceError.Empty -> "A diagram element reference is required."
        | DiagramReferenceError.UnknownKind kind -> $"'{kind}' is not a diagram element kind."
        | DiagramReferenceError.InvalidKey reference -> $"'{reference}' is not a valid diagram element reference."
        | DiagramReferenceError.DocumentHasFragment document ->
            $"The document IRI <{document.Value}> has a fragment; the element reference must own the fragment."
        | DiagramReferenceError.NotInDocument iri -> $"<{iri.Value}> is not an element IRI of the document."

module internal ReferenceEncoding =
    let private allowedAscii = "-._~!$&'()*+,;:@?"

    /// Characters an RFC 3987 ifragment admits, except %, / and =, which separate reference parts.
    let private allowed (rune: Rune) =
        let value = rune.Value

        if value < 0x80 then
            Char.IsAsciiLetterOrDigit(char value) || allowedAscii.Contains(char value)
        else
            value >= 0xA0 && value <= 0xD7FF
            || value >= 0xF900 && value <= 0xFDCF
            || value >= 0xFDF0 && value <= 0xFFEF
            || value >= 0x10000 && value <= 0xEFFFD && (value &&& 0xFFFF) < 0xFFFE

    let encode (text: string) =
        let builder = StringBuilder()

        for rune in text.EnumerateRunes() do
            if allowed rune then
                builder.Append(rune.ToString()) |> ignore
            else
                let bytes = Array.zeroCreate<byte> 4
                let count = rune.EncodeToUtf8(Span bytes)

                for index in 0 .. count - 1 do
                    builder.Append('%').Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture)) |> ignore

        builder.ToString()

    let private strict = UTF8Encoding(false, true)

    let decode (text: string) =
        let bytes = ResizeArray<byte>()
        let mutable index = 0
        let mutable valid = true

        while valid && index < text.Length do
            if text[index] = '%' then
                if index + 2 < text.Length + 0 && index + 2 <= text.Length - 1
                   && Uri.IsHexDigit text[index + 1] && Uri.IsHexDigit text[index + 2] then
                    bytes.Add(Byte.Parse(text.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                    index <- index + 3
                else
                    valid <- false
            else
                let count = if Char.IsHighSurrogate text[index] && index + 1 < text.Length then 2 else 1
                bytes.AddRange(Encoding.UTF8.GetBytes(text.Substring(index, count)))
                index <- index + count

        if not valid then None
        else
            try
                Some(strict.GetString(bytes.ToArray()))
            with :? DecoderFallbackException ->
                None

/// The reference of one element of a diagram, such as node=Billing or edge=Cart/Billing/0. It
/// depends only on authored identifiers and on source order among elements of the same kind.
/// Equality and order are ordinal over the canonical text.
[<CustomEquality; CustomComparison>]
type DiagramElementRef =
    private
    | NodeRef of DiagramId
    | GroupRef of DiagramId
    | EdgeRef of source: DiagramId * target: DiagramId * ordinal: int
    | ParticipantRef of DiagramId
    | MessageRef of sender: DiagramId * receiver: DiagramId * ordinal: int
    | NoteRef of int
    | FrameRef of int
    | StepRef of int

    static let ordinalText (ordinal: int) = ordinal.ToString(CultureInfo.InvariantCulture)

    static let checkOrdinal (ordinal: int) =
        if ordinal < 0 then
            raise (ArgumentOutOfRangeException(nameof ordinal, ordinal, "An ordinal starts at 0."))

    /// The kind of element.
    member this.Kind =
        match this with
        | NodeRef _ -> DiagramElementKind.Node
        | GroupRef _ -> DiagramElementKind.Group
        | EdgeRef _ -> DiagramElementKind.Edge
        | ParticipantRef _ -> DiagramElementKind.Participant
        | MessageRef _ -> DiagramElementKind.Message
        | NoteRef _ -> DiagramElementKind.Note
        | FrameRef _ -> DiagramElementKind.Frame
        | StepRef _ -> DiagramElementKind.Step

    /// For C# callers: every kind needs a function.
    member this.Match
        (
            node: Func<DiagramId, 'T>,
            group: Func<DiagramId, 'T>,
            edge: Func<DiagramId, DiagramId, int, 'T>,
            participant: Func<DiagramId, 'T>,
            message: Func<DiagramId, DiagramId, int, 'T>,
            note: Func<int, 'T>,
            frame: Func<int, 'T>,
            step: Func<int, 'T>
        ) =
        match this with
        | NodeRef id -> node.Invoke id
        | GroupRef id -> group.Invoke id
        | EdgeRef(source, target, ordinal) -> edge.Invoke(source, target, ordinal)
        | ParticipantRef id -> participant.Invoke id
        | MessageRef(sender, receiver, ordinal) -> message.Invoke(sender, receiver, ordinal)
        | NoteRef ordinal -> note.Invoke ordinal
        | FrameRef ordinal -> frame.Invoke ordinal
        | StepRef ordinal -> step.Invoke ordinal

    /// The reference text, with reserved characters percent-encoded.
    override this.ToString() =
        let id (value: DiagramId) = ReferenceEncoding.encode value.Value
        let kind = this.Kind.WireValue

        match this with
        | NodeRef value
        | GroupRef value
        | ParticipantRef value -> $"{kind}={id value}"
        | EdgeRef(first, second, ordinal)
        | MessageRef(first, second, ordinal) -> $"{kind}={id first}/{id second}/{ordinalText ordinal}"
        | NoteRef ordinal
        | FrameRef ordinal
        | StepRef ordinal -> $"{kind}={ordinalText ordinal}"

    override this.Equals(other: obj | null) =
        match other with
        | :? DiagramElementRef as reference -> String.Equals(this.ToString(), reference.ToString(), StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode(this.ToString())

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | null -> 1
            | :? DiagramElementRef as reference -> String.CompareOrdinal(this.ToString(), reference.ToString())
            | _ -> invalidArg (nameof other) "Expected a DiagramElementRef."

    /// A flowchart node.
    static member Node(id: DiagramId) =
        ArgumentNullException.ThrowIfNull id
        NodeRef id

    /// A flowchart subgraph.
    static member Group(id: DiagramId) =
        ArgumentNullException.ThrowIfNull id
        GroupRef id

    /// The link at an ordinal among links from source to target, counting from 0 in source order.
    static member Edge(source: DiagramId, target: DiagramId, ordinal: int) =
        ArgumentNullException.ThrowIfNull source
        ArgumentNullException.ThrowIfNull target
        checkOrdinal ordinal
        EdgeRef(source, target, ordinal)

    /// A sequence lifeline.
    static member Participant(id: DiagramId) =
        ArgumentNullException.ThrowIfNull id
        ParticipantRef id

    /// The message at an ordinal among messages from sender to receiver, counting from 0 in source order.
    static member Message(sender: DiagramId, receiver: DiagramId, ordinal: int) =
        ArgumentNullException.ThrowIfNull sender
        ArgumentNullException.ThrowIfNull receiver
        checkOrdinal ordinal
        MessageRef(sender, receiver, ordinal)

    /// The note at an ordinal among the diagram's notes, in source order.
    static member Note(ordinal: int) =
        checkOrdinal ordinal
        NoteRef ordinal

    /// The frame at an ordinal among the diagram's frames, in source order, outer frames first.
    static member Frame(ordinal: int) =
        checkOrdinal ordinal
        FrameRef ordinal

    /// The playback step at an ordinal, in authored order.
    static member Step(ordinal: int) =
        checkOrdinal ordinal
        StepRef ordinal

    /// Reads a reference such as edge=Cart/Billing/0 in its canonical form, so that one element has
    /// exactly one reference text: uppercase escapes, and only the characters that need escaping.
    /// For F# callers.
    static member Parse(text: string | null) : Result<DiagramElementRef, DiagramReferenceError> =
        match DiagramElementRef.ParseAny text with
        | Ok reference when not (String.Equals(reference.ToString(), text, StringComparison.Ordinal)) ->
            Error(DiagramReferenceError.InvalidKey(text |> Option.ofObj |> Option.defaultValue ""))
        | result -> result

    static member private ParseAny(text: string | null) : Result<DiagramElementRef, DiagramReferenceError> =
        let invalid () = Error(DiagramReferenceError.InvalidKey(text |> Option.ofObj |> Option.defaultValue ""))

        let ordinal (value: string) =
            if value.Length > 0 && value.Length <= 9 && Seq.forall Char.IsAsciiDigit value && (value = "0" || value[0] <> '0') then
                Some(Int32.Parse(value, CultureInfo.InvariantCulture))
            else
                None

        let identifier (value: string) =
            ReferenceEncoding.decode value
            |> Option.bind (fun decoded ->
                match DiagramId.Parse decoded with
                | Ok id -> Some id
                | Error _ -> None)

        match text with
        | null
        | "" -> Error DiagramReferenceError.Empty
        | text ->
            match text.IndexOf '=' with
            | -1 -> Error(DiagramReferenceError.UnknownKind text)
            | separator ->
                let kind = text.Substring(0, separator)
                let key = text.Substring(separator + 1)
                let parts = key.Split '/'

                let single make =
                    match parts with
                    | [| value |] -> identifier value |> Option.map make |> Option.map Ok |> Option.defaultWith invalid
                    | _ -> invalid ()

                let pair make =
                    match parts with
                    | [| first; second; number |] ->
                        match identifier first, identifier second, ordinal number with
                        | Some first, Some second, Some number -> Ok(make (first, second, number))
                        | _ -> invalid ()
                    | _ -> invalid ()

                let numbered make =
                    match parts with
                    | [| number |] -> ordinal number |> Option.map make |> Option.map Ok |> Option.defaultWith invalid
                    | _ -> invalid ()

                match kind with
                | "node" -> single NodeRef
                | "group" -> single GroupRef
                | "participant" -> single ParticipantRef
                | "edge" -> pair EdgeRef
                | "message" -> pair MessageRef
                | "note" -> numbered NoteRef
                | "frame" -> numbered FrameRef
                | "step" -> numbered StepRef
                | other -> Error(DiagramReferenceError.UnknownKind other)

    /// Reads a reference. For C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] reference: byref<DiagramElementRef>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramReferenceError>
        ) =
        match DiagramElementRef.Parse text with
        | Ok value ->
            reference <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            reference <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Reads a reference from a source-code literal. Throws for an invalid literal.
    static member Create(literal: string) =
        match DiagramElementRef.Parse literal with
        | Ok reference -> reference
        | Error error -> invalidArg (nameof literal) (error.ToString())

    /// The element IRI: the document IRI with the reference as its fragment. A document IRI that
    /// already has a fragment is an error. For F# callers.
    member this.ToIri(document: Iri) : Result<Iri, DiagramReferenceError> =
        ArgumentNullException.ThrowIfNull document

        if document.Value.Contains '#' then
            Error(DiagramReferenceError.DocumentHasFragment document)
        else
            Ok(Iri.Create(document.Value + "#" + this.ToString()))

    /// The element IRI. For C# callers.
    member this.TryToIri
        (
            document: Iri,
            [<Out; MaybeNullWhen(false)>] iri: byref<Iri>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramReferenceError>
        ) =
        match this.ToIri document with
        | Ok value ->
            iri <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            iri <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Reads the reference of an element IRI of the document. For F# callers.
    static member ParseIri(document: Iri, iri: Iri) : Result<DiagramElementRef, DiagramReferenceError> =
        ArgumentNullException.ThrowIfNull document
        ArgumentNullException.ThrowIfNull iri
        let prefix = document.Value + "#"

        if document.Value.Contains '#' then
            Error(DiagramReferenceError.DocumentHasFragment document)
        elif not (iri.Value.StartsWith(prefix, StringComparison.Ordinal)) then
            Error(DiagramReferenceError.NotInDocument iri)
        else
            DiagramElementRef.Parse(iri.Value.Substring prefix.Length)

    /// Reads the reference of an element IRI of the document. For C# callers.
    static member TryParseIri
        (
            document: Iri,
            iri: Iri,
            [<Out; MaybeNullWhen(false)>] reference: byref<DiagramElementRef>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramReferenceError>
        ) =
        match DiagramElementRef.ParseIri(document, iri) with
        | Ok value ->
            reference <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            reference <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Every element of the diagram, by kind in a fixed order and then in source order. Flowcharts
    /// list nodes, groups, edges and steps; sequence diagrams list participants, messages, notes,
    /// frames and steps.
    static member Enumerate(diagram: Diagram) : ImmutableArray<DiagramElementRef> =
        ArgumentNullException.ThrowIfNull diagram
        ElementRefs.enumerate diagram

/// The references of a diagram's elements, aligned with the model, for painters and enumeration.
and [<RequireQualifiedAccess>] internal ElementRefs =
    /// The ordinal of each item among items with the same key, in order.
    static member private Ordinals(keys: seq<'Key>) =
        let counts = Dictionary<'Key, int>()

        [| for key in keys do
               let count = match counts.TryGetValue key with | true, value -> value | false, _ -> 0
               counts[key] <- count + 1
               yield count |]

    /// The reference of each edge, aligned with Flowchart.Edges.
    static member Edges(model: Flowchart) =
        let ordinals = ElementRefs.Ordinals(model.Edges |> Seq.map (fun edge -> edge.Source, edge.Target))
        model.Edges |> Seq.mapi (fun index edge -> EdgeRef(edge.Source, edge.Target, ordinals[index])) |> Seq.toArray

    /// Whether playback binds the edge: visible with at least one head.
    static member IsStep(edge: FlowEdge) =
        edge.Line <> FlowLine.Invisible && (edge.StartHead <> EdgeHead.None || edge.EndHead <> EdgeHead.None)

    /// The reference of each message, keyed by the message object.
    static member Messages(model: SequenceDiagram) =
        let ordinals = ElementRefs.Ordinals(model.Messages |> Seq.map (fun message -> message.Sender, message.Receiver))
        let result = Dictionary<SequenceMessage, DiagramElementRef>(HashIdentity.Reference)

        model.Messages |> Seq.iteri (fun index message -> result.Add(message, MessageRef(message.Sender, message.Receiver, ordinals[index])))
        result

    /// The reference of each note, keyed by the note object.
    static member Notes(model: SequenceDiagram) =
        let result = Dictionary<SequenceNote, DiagramElementRef>(HashIdentity.Reference)
        model.Notes |> Seq.iteri (fun index note -> result.Add(note, NoteRef index))
        result

    /// The frames in source order, outer frames first.
    static member FrameOrder(model: SequenceDiagram) =
        let frames = ResizeArray<SequenceFrame>()

        let rec visit (events: ImmutableArray<SequenceEvent>) =
            for event in events do
                match event with
                | SequenceEvent.Frame frame ->
                    frames.Add frame

                    for section in frame.Sections do
                        visit section.Events
                | _ -> ()

        visit model.Events
        frames

    /// The reference of each frame, keyed by the frame object.
    static member Frames(model: SequenceDiagram) =
        let result = Dictionary<SequenceFrame, DiagramElementRef>(HashIdentity.Reference)
        ElementRefs.FrameOrder model |> Seq.iteri (fun index frame -> result.Add(frame, FrameRef index))
        result

    static member internal enumerate(diagram: Diagram) =
        let steps count = Seq.init count StepRef

        match diagram, diagram.Graph with
        | _, Some model ->
            seq {
                yield! model.Nodes |> Seq.map (fun node -> NodeRef node.Id)
                yield! model.Groups |> Seq.map (fun group -> GroupRef group.Id)
                yield! ElementRefs.Edges model
                yield! steps (model.Edges |> Seq.filter ElementRefs.IsStep |> Seq.length)
            }
        | Diagram.Sequence model, None ->
            seq {
                yield! model.Participants |> Seq.map (fun participant -> ParticipantRef participant.Id)
                yield! model.Messages |> Seq.map (fun message -> (ElementRefs.Messages model)[message])
                yield! model.Notes |> Seq.mapi (fun index _ -> NoteRef index)
                yield! ElementRefs.FrameOrder model |> Seq.mapi (fun index _ -> FrameRef index)
                yield! steps model.Messages.Length
            }
        | _, None -> Seq.empty
        |> ImmutableArray.CreateRange
