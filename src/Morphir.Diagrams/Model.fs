// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Xml

/// A location in diagram source, measured in UTF-16 code units.
[<Sealed>]
type SourcePosition internal (offset: int, line: int, column: int) =
    /// Zero-based offset from the start of the source.
    member _.Offset = offset

    /// One-based line number.
    member _.Line = line

    /// One-based column number.
    member _.Column = column

    override _.ToString() = $"{line}:{column}"

/// A half-open range in diagram source.
[<Sealed>]
type SourceSpan internal (start: SourcePosition, finish: SourcePosition) =
    /// Inclusive start of the range.
    member _.Start = start

    /// Exclusive end of the range.
    member _.End = finish

    override _.ToString() = $"{start}-{finish}"

/// Severity of a diagram diagnostic.
[<RequireQualifiedAccess>]
type DiagnosticSeverity =
    /// The input could not be read or rendered.
    | Error
    /// The operation completed with a finding.
    | Warning

    override this.ToString() =
        match this with
        | DiagnosticSeverity.Error -> "error"
        | DiagnosticSeverity.Warning -> "warning"

/// A stable code, severity, and explanation from a reader or renderer.
[<Sealed>]
type Diagnostic
    internal (code: string, severity: DiagnosticSeverity, message: string, span: SourceSpan option)
    =
    /// Machine-readable diagnostic identifier.
    member _.Code = code

    /// Severity of the finding.
    member _.Severity = severity

    /// Human-readable explanation.
    member _.Message = message

    /// The source range, when available.
    member _.Span: SourceSpan | null = Option.toObj span

    override _.ToString() =
        match span with
        | Some location -> $"{location.Start} {code} {message}"
        | None -> $"{code} {message}"

/// A case-sensitive, nonblank identity within a diagram.
[<CustomEquality; CustomComparison>]
type DiagramId =
    private
    | DiagramId of string

    /// Original identifier spelling.
    member this.Value =
        let (DiagramId value) = this
        value

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? DiagramId as id -> String.Equals(this.Value, id.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | null -> 1
            | :? DiagramId as id -> String.CompareOrdinal(this.Value, id.Value)
            | _ -> invalidArg (nameof other) "Expected a DiagramId."

    /// Parses an identifier for F# callers. Individual formats may constrain its syntax further.
    static member Parse(text: string | null) : Result<DiagramId, Diagnostic> =
        let fail message =
            Error(Diagnostic("diagram/identifier", DiagnosticSeverity.Error, message, None))

        match text with
        | null -> fail "Expected a diagram identifier."
        | value when String.IsNullOrWhiteSpace value || value.Contains '\r' || value.Contains '\n' ->
            fail "A diagram identifier must be nonblank and fit on one line."
        | value ->
            try
                XmlConvert.VerifyXmlChars value |> ignore

                Ok(DiagramId value)
            with :? XmlException ->
                fail "The identifier contains a character that cannot be represented in SVG."

    /// Parses an identifier for C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<DiagramId>,
            [<Out; MaybeNullWhen(true)>] failure: byref<Diagnostic>
        ) =
        match DiagramId.Parse text with
        | Ok identity ->
            result <- identity
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Creates an identifier from a source-code literal. Throws for an invalid literal.
    static member Create(literal: string) =
        match DiagramId.Parse literal with
        | Ok identity -> identity
        | Error error -> invalidArg (nameof literal) error.Message

/// Primary axis and progression direction of a flowchart.
[<RequireQualifiedAccess>]
type FlowDirection =
    /// Ranks advance down the page.
    | TopToBottom
    /// Ranks advance up the page.
    | BottomToTop
    /// Ranks advance to the right.
    | LeftToRight
    /// Ranks advance to the left.
    | RightToLeft

    /// Mermaid spelling of the direction.
    member this.WireValue =
        match this with
        | FlowDirection.TopToBottom -> "TB"
        | FlowDirection.BottomToTop -> "BT"
        | FlowDirection.LeftToRight -> "LR"
        | FlowDirection.RightToLeft -> "RL"

    override this.ToString() = this.WireValue

/// A shape supported by the flowchart renderer.
[<RequireQualifiedAccess>]
type FlowShape =
    /// Square-cornered box.
    | Rectangle
    /// Rounded-corner box.
    | Rounded
    /// Box with semicircular ends.
    | Stadium
    /// Circular node.
    | Circle
    /// Four-point decision shape.
    | Diamond
    /// Box with inset vertical lines.
    | Subroutine
    /// Cylindrical store.
    | Cylinder
    /// Six-sided node.
    | Hexagon
    /// Two concentric circles.
    | DoubleCircle
    /// Box with one pointed side.
    | Asymmetric
    /// Right-leaning parallelogram.
    | Parallelogram
    /// Left-leaning parallelogram.
    | ReverseParallelogram
    /// Trapezoid with a wide base.
    | Trapezoid
    /// Trapezoid with a wide top.
    | ReverseTrapezoid
    /// Small filled circle, such as an initial state.
    | FilledCircle
    /// Filled circle inside a ring, such as a final state.
    | FramedCircle
    /// Thick bar, such as a fork or join.
    | Bar
    /// Box with a folded corner, such as a note.
    | Note
    /// Page with a wavy base, such as a document or data object.
    | Document
    /// Small hollow circle, such as an entry point or a junction.
    | SmallCircle
    /// Circle with a cross, such as an exit point.
    | CrossedCircle
    /// Circle with a thick outline, such as an end event.
    | ThickCircle
    /// Text with no outline.
    | Text

    /// Stable name of the shape.
    member this.WireValue =
        match this with
        | FlowShape.Rectangle -> "rectangle"
        | FlowShape.Rounded -> "rounded"
        | FlowShape.Stadium -> "stadium"
        | FlowShape.Circle -> "circle"
        | FlowShape.Diamond -> "diamond"
        | FlowShape.Subroutine -> "subroutine"
        | FlowShape.Cylinder -> "cylinder"
        | FlowShape.Hexagon -> "hexagon"
        | FlowShape.DoubleCircle -> "double-circle"
        | FlowShape.Asymmetric -> "asymmetric"
        | FlowShape.Parallelogram -> "parallelogram"
        | FlowShape.ReverseParallelogram -> "reverse-parallelogram"
        | FlowShape.Trapezoid -> "trapezoid"
        | FlowShape.ReverseTrapezoid -> "reverse-trapezoid"
        | FlowShape.FilledCircle -> "filled-circle"
        | FlowShape.FramedCircle -> "framed-circle"
        | FlowShape.Bar -> "bar"
        | FlowShape.Note -> "note"
        | FlowShape.Document -> "document"
        | FlowShape.SmallCircle -> "small-circle"
        | FlowShape.CrossedCircle -> "crossed-circle"
        | FlowShape.ThickCircle -> "thick-circle"
        | FlowShape.Text -> "text"

    override this.ToString() = this.WireValue

/// The visible stroke of a flowchart connection.
[<RequireQualifiedAccess>]
type FlowLine =
    /// Continuous stroke.
    | Solid
    /// Broken stroke.
    | Dotted
    /// Wide continuous stroke.
    | Thick
    /// Layout connection with no visible stroke.
    | Invisible

    /// Stable name of the line style.
    member this.WireValue =
        match this with
        | FlowLine.Solid -> "solid"
        | FlowLine.Dotted -> "dotted"
        | FlowLine.Thick -> "thick"
        | FlowLine.Invisible -> "invisible"

    override this.ToString() = this.WireValue

/// Shape drawn at a connection endpoint.
[<RequireQualifiedAccess>]
type EdgeHead =
    /// No endpoint marker.
    | None
    /// Filled arrow.
    | Arrow
    /// Outlined arrow.
    | OpenArrow
    /// Circular marker.
    | Circle
    /// Cross marker.
    | Cross
    /// Asynchronous sequence arrow.
    | Async
    /// Open upper half arrow.
    | HalfUpper
    /// Open lower half arrow.
    | HalfLower
    /// Filled upper half arrow.
    | HalfUpperFilled
    /// Filled lower half arrow.
    | HalfLowerFilled

    /// Stable name of the marker.
    member this.WireValue =
        match this with
        | EdgeHead.None -> "none"
        | EdgeHead.Arrow -> "arrow"
        | EdgeHead.OpenArrow -> "open-arrow"
        | EdgeHead.Circle -> "circle"
        | EdgeHead.Cross -> "cross"
        | EdgeHead.Async -> "async"
        | EdgeHead.HalfUpper -> "half-upper"
        | EdgeHead.HalfLower -> "half-lower"
        | EdgeHead.HalfUpperFilled -> "half-upper-filled"
        | EdgeHead.HalfLowerFilled -> "half-lower-filled"

    override this.ToString() = this.WireValue

/// A declared or implicit flowchart node.
[<Sealed>]
type FlowNode internal (id: DiagramId, label: string, shape: FlowShape, span: SourceSpan, tone: string option, icon: string option) =
    internal new(id, label, shape, span) = FlowNode(id, label, shape, span, None, None)

    /// The colour family that a diagram family chose for this node, overriding the family of its shape.
    member internal _.Tone = tone

    /// The icon key drawn in the node, if any.
    member internal _.Icon = icon

    /// Identifier as authored. Comparisons are ordinal.
    member _.Id = id

    /// Text displayed in the shape.
    member _.Label = label

    /// Node shape.
    member _.Shape = shape

    /// Location of the declaration that supplied the shape and label, or the first implicit occurrence.
    member _.Span = span

    override _.ToString() = id.Value

/// An occurrence of a link between two flowchart nodes.
[<Sealed>]
type FlowEdge
    internal
    (
        source: DiagramId,
        target: DiagramId,
        label: string option,
        line: FlowLine,
        startHead: EdgeHead,
        endHead: EdgeHead,
        span: SourceSpan,
        sides: (char * char) option
    ) =
    internal new(source, target, label, line, startHead, endHead, span) =
        FlowEdge(source, target, label, line, startHead, endHead, span, None)

    /// The sides (L, R, T or B) that the edge leaves its source and enters its target by, when a port fixes them.
    member internal _.Sides = sides

    /// Identifier of the source node.
    member _.Source = source

    /// Identifier of the target node.
    member _.Target = target

    /// Optional text on the connection.
    member _.Label: string | null = Option.toObj label

    /// Stroke style.
    member _.Line = line

    /// Head on the source end.
    member _.StartHead = startHead

    /// Head on the target end.
    member _.EndHead = endHead

    /// Location of the authored link.
    member _.Span = span

    override _.ToString() = $"{source} to {target}"

/// An explicit Mermaid subgraph.
[<Sealed>]
type FlowGroup
    internal
    (
        id: DiagramId,
        label: string,
        parentId: DiagramId option,
        nodeIds: ImmutableArray<DiagramId>,
        span: SourceSpan
    ) =
    /// Identifier used by the enclosing flowchart.
    member _.Id = id

    /// Display label.
    member _.Label = label

    /// Identifier of the parent subgraph, if one was declared.
    member _.ParentId: DiagramId | null = Option.toObj parentId

    /// Nodes directly assigned to this subgraph in source order.
    member _.NodeIds = nodeIds

    /// Location of the subgraph declaration.
    member _.Span = span

    override _.ToString() = id.Value

/// A normalized flowchart model.
[<Sealed>]
type Flowchart
    internal
    (
        direction: FlowDirection,
        nodes: ImmutableArray<FlowNode>,
        edges: ImmutableArray<FlowEdge>,
        groups: ImmutableArray<FlowGroup>,
        lanes: bool
    ) =
    internal new(direction, nodes, edges, groups) = Flowchart(direction, nodes, edges, groups, false)

    /// Whether the top-level groups are drawn as lanes: parallel bands of equal length across the flow.
    member internal _.Lanes = lanes

    /// Direction used by the layout.
    member _.Direction = direction

    /// Nodes in first occurrence order.
    member _.Nodes = nodes

    /// Edges in source order, including parallel edges.
    member _.Edges = edges

    /// Subgraphs in declaration order.
    member _.Groups = groups

    override _.ToString() =
        $"Flowchart with {nodes.Length} nodes and {edges.Length} edges"

/// The symbol associated with a sequence lifeline.
[<RequireQualifiedAccess>]
type ParticipantKind =
    /// Standard participant box.
    | Participant
    /// Actor symbol.
    | Actor
    /// UML boundary symbol: a circle with a bar on its left.
    | Boundary
    /// UML control symbol: a circle with an arrowhead.
    | Control
    /// UML entity symbol: a circle on a line.
    | Entity
    /// A database cylinder.
    | Database
    /// A stack of boxes.
    | Collections
    /// A queue drawn as a horizontal cylinder.
    | Queue

    override this.ToString() =
        match this with
        | ParticipantKind.Participant -> "participant"
        | ParticipantKind.Actor -> "actor"
        | ParticipantKind.Boundary -> "boundary"
        | ParticipantKind.Control -> "control"
        | ParticipantKind.Entity -> "entity"
        | ParticipantKind.Database -> "database"
        | ParticipantKind.Collections -> "collections"
        | ParticipantKind.Queue -> "queue"

/// A sequence lifeline.
[<Sealed>]
type Participant
    internal (id: DiagramId, label: string, kind: ParticipantKind, created: int option, destroyed: int option) =
    internal new(id, label, kind) = Participant(id, label, kind, None, None)

    member internal _.With(created: int option, destroyed: int option) = Participant(id, label, kind, created, destroyed)

    /// The zero-based index of the message that creates the participant, or null when it exists from the start.
    member _.CreatedBy = Option.toNullable created

    /// The zero-based index of the message that destroys the participant, or null when it lives to the end.
    member _.DestroyedBy = Option.toNullable destroyed

    /// Identifier as authored. Comparisons are ordinal.
    member _.Id = id

    /// Display name.
    member _.Label = label

    /// Participant symbol.
    member _.Kind = kind

    override _.ToString() = id.Value

/// A message occurrence in a sequence diagram.
[<Sealed>]
type SequenceMessage
    internal
    (
        sender: DiagramId,
        receiver: DiagramId,
        text: string,
        dashed: bool,
        senderHead: EdgeHead,
        receiverHead: EdgeHead,
        number: decimal option,
        span: SourceSpan,
        central: bool * bool
    ) =
    internal new(sender, receiver, text, dashed, senderHead, receiverHead, number, span) =
        SequenceMessage(sender, receiver, text, dashed, senderHead, receiverHead, number, span, (false, false))

    /// Whether the message starts at a central connection `()` on the sender's lifeline.
    member _.StartsAtCentre = fst central

    /// Whether the message ends at a central connection `()` on the receiver's lifeline.
    member _.EndsAtCentre = snd central

    /// Identifier of the sender.
    member _.Sender = sender

    /// Identifier of the receiver.
    member _.Receiver = receiver

    /// Text displayed above the connection.
    member _.Text = text

    /// Whether the connector is dashed.
    member _.IsDashed = dashed

    /// Head on the sender end.
    member _.SenderHead = senderHead

    /// Head on the receiver end.
    member _.ReceiverHead = receiverHead

    /// Number assigned by Mermaid autonumber, when it is enabled.
    member _.Number = Option.toNullable number

    /// Location of the authored message.
    member _.Span = span

    override _.ToString() = $"{sender} to {receiver}: {text}"

/// Placement of a sequence note relative to its lifelines.
[<RequireQualifiedAccess>]
type NotePlacement =
    /// Place the note on the left of a lifeline.
    | Left
    /// Place the note on the right of a lifeline.
    | Right
    /// Center the note on or between its lifelines.
    | Over

    override this.ToString() =
        match this with
        | NotePlacement.Left -> "left"
        | NotePlacement.Right -> "right"
        | NotePlacement.Over -> "over"

/// An annotation anchored to one or two sequence lifelines.
[<Sealed>]
type SequenceNote
    internal
    (
        placement: NotePlacement,
        participants: ImmutableArray<DiagramId>,
        text: string,
        messageIndex: int,
        span: SourceSpan
    ) =
    /// Placement of the note.
    member _.Placement = placement

    /// Identifiers of the lifelines that anchor the note.
    member _.Participants = participants

    /// Display text of the note.
    member _.Text = text

    /// The note appears before this zero-based message index, or after the last message when equal to the message count.
    member _.MessageIndex = messageIndex

    /// Location of the note declaration.
    member _.Span = span

    override _.ToString() = text

/// The change an activation event makes to a lifeline.
[<RequireQualifiedAccess>]
type SequenceActivationKind =
    /// Add an activation at the current nesting depth.
    | Activate
    /// End the most recent activation.
    | Deactivate

    override this.ToString() =
        match this with
        | SequenceActivationKind.Activate -> "activate"
        | SequenceActivationKind.Deactivate -> "deactivate"

/// An activation change at a particular point in the sequence.
[<Sealed>]
type SequenceActivation
    internal (participant: DiagramId, kind: SequenceActivationKind, span: SourceSpan) =
    /// The lifeline whose activation changes.
    member _.Participant = participant

    /// The change to the lifeline.
    member _.Kind = kind

    /// Location of the activation statement or message shorthand.
    member _.Span = span

    override _.ToString() = $"{kind} {participant}"

/// A sequence fragment operator.
[<RequireQualifiedAccess>]
type SequenceFrameKind =
    /// Repetition.
    | Loop
    /// Conditional paths.
    | Alternative
    /// An optional path.
    | Optional
    /// Parallel paths.
    | Parallel
    /// Parallel paths that share a vertical range in Mermaid.
    | ParallelOver
    /// A critical region with optional alternatives.
    | Critical
    /// A path that ends the sequence early.
    | Break
    /// A rectangular background region.
    | Rectangle
    /// A ZenUML exception region with try, catch and finally sections.
    | Try

    /// Mermaid keyword for the operator.
    member this.WireValue =
        match this with
        | SequenceFrameKind.Try -> "try"
        | SequenceFrameKind.Loop -> "loop"
        | SequenceFrameKind.Alternative -> "alt"
        | SequenceFrameKind.Optional -> "opt"
        | SequenceFrameKind.Parallel -> "par"
        | SequenceFrameKind.ParallelOver -> "par_over"
        | SequenceFrameKind.Critical -> "critical"
        | SequenceFrameKind.Break -> "break"
        | SequenceFrameKind.Rectangle -> "rect"

    override this.ToString() = this.WireValue

/// An ordered element of a sequence diagram or a frame section.
[<RequireQualifiedAccess>]
type SequenceEvent =
    /// A message between lifelines.
    | Message of SequenceMessage
    /// A note attached to one or more lifelines.
    | Note of SequenceNote
    /// An activation change on a lifeline.
    | Activation of SequenceActivation
    /// A frame that contains nested events.
    | Frame of SequenceFrame

    /// Exhaustive dispatch over sequence events for C# callers.
    member this.Match<'T>
        (
            message: Func<SequenceMessage, 'T>,
            note: Func<SequenceNote, 'T>,
            activation: Func<SequenceActivation, 'T>,
            frame: Func<SequenceFrame, 'T>
        ) =
        match this with
        | SequenceEvent.Message value -> message.Invoke value
        | SequenceEvent.Note value -> note.Invoke value
        | SequenceEvent.Activation value -> activation.Invoke value
        | SequenceEvent.Frame value -> frame.Invoke value

    override this.ToString() =
        match this with
        | SequenceEvent.Message message -> message.ToString()
        | SequenceEvent.Note note -> note.ToString()
        | SequenceEvent.Activation activation -> activation.ToString()
        | SequenceEvent.Frame frame -> frame.ToString()

/// A sequence fragment made up of one or more sections.
and [<Sealed>] SequenceFrame
    internal
    (
        kind: SequenceFrameKind,
        sections: ImmutableArray<SequenceFrameSection>,
        color: string option,
        span: SourceSpan
    ) =
    /// Fragment operator.
    member _.Kind = kind

    /// Sections in source order.
    member _.Sections = sections

    /// Authored color for a rectangular background region, when supplied.
    member _.Color: string | null = Option.toObj color

    /// Source range from the opening keyword through the closing end.
    member _.Span = span

    override _.ToString() = kind.WireValue

/// The first body or a subsequent branch of a sequence frame.
and [<Sealed>] SequenceFrameSection
    internal (label: string option, events: ImmutableArray<SequenceEvent>) =
    /// The authored condition or branch label, when supplied.
    member _.Label: string | null = Option.toObj label

    /// Events in source order.
    member _.Events = events

    override _.ToString() =
        defaultArg label $"{events.Length} events"

/// A group of adjacent lifelines drawn inside one labelled box.
[<Sealed>]
type SequenceBox internal (label: string, color: string option, participants: ImmutableArray<DiagramId>) =
    /// Display label, which may be empty.
    member _.Label = label

    /// The colour the source asked for, such as `Purple` or `rgb(33,66,99)`, or null. Themes decide the drawn colour.
    member _.Color: string | null = Option.toObj color

    /// The lifelines in the box, in order.
    member _.Participants = participants

    override _.ToString() = label

/// A normalized sequence diagram.
[<Sealed>]
type SequenceDiagram
    internal
    (
        participants: ImmutableArray<Participant>,
        events: ImmutableArray<SequenceEvent>,
        boxes: ImmutableArray<SequenceBox>
    ) =
    let messages, notes =
        let messages = ImmutableArray.CreateBuilder<SequenceMessage>()
        let notes = ImmutableArray.CreateBuilder<SequenceNote>()

        let rec visit (events: ImmutableArray<SequenceEvent>) =
            for event in events do
                match event with
                | SequenceEvent.Message message -> messages.Add message
                | SequenceEvent.Note note -> notes.Add note
                | SequenceEvent.Frame frame ->
                    for section in frame.Sections do
                        visit section.Events
                | SequenceEvent.Activation _ -> ()

        visit events
        messages.ToImmutable(), notes.ToImmutable()

    internal new(participants, events) = SequenceDiagram(participants, events, ImmutableArray<SequenceBox>.Empty)

    /// Lifelines in display order.
    member _.Participants = participants

    /// Boxes that group lifelines, in source order.
    member _.Boxes = boxes

    /// Top-level events in source order.
    member _.Events = events

    /// Messages in source order, including those in nested frames.
    member _.Messages = messages

    /// Notes in source order, including those in nested frames.
    member _.Notes = notes

    override _.ToString() =
        $"Sequence with {participants.Length} participants and {messages.Length} messages"

