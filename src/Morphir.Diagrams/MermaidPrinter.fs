// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Globalization
open System.Text

/// Writes flowcharts and sequences as canonical Mermaid text. Reading the text gives the same model, so printing
/// it again gives the same text. Presentation that the model does not keep is not printed.
[<RequireQualifiedAccess>]
module internal MermaidPrinter =
    /// Label text as a quoted Mermaid string: quotes and line breaks become entity codes and `<br>`.
    let quote (text: string) =
        "\"" + text.Replace("#", "#35;").Replace("\"", "#quot;").Replace("\n", "<br>") + "\""

    let private direction (value: FlowDirection) =
        match value with
        | FlowDirection.TopToBottom -> "TB"
        | FlowDirection.BottomToTop -> "BT"
        | FlowDirection.LeftToRight -> "LR"
        | FlowDirection.RightToLeft -> "RL"

    let private node (node: FlowNode) =
        let id = node.Id.Value
        let label = quote node.Label
        let data name = Ok $"{id}@{{ shape: {name}, label: {label} }}"

        match node.Shape with
        | FlowShape.Rectangle when node.Label = id -> Ok id
        | FlowShape.Rectangle -> Ok $"{id}[{label}]"
        | FlowShape.Rounded -> Ok $"{id}({label})"
        | FlowShape.Stadium -> Ok $"{id}([{label}])"
        | FlowShape.Subroutine -> Ok $"{id}[[{label}]]"
        | FlowShape.Cylinder -> Ok $"{id}[({label})]"
        | FlowShape.Circle -> Ok $"{id}(({label}))"
        | FlowShape.DoubleCircle -> Ok $"{id}((({label})))"
        | FlowShape.Diamond -> Ok $"{id}{{{label}}}"
        | FlowShape.Hexagon -> Ok $"{id}{{{{{label}}}}}"
        | FlowShape.Asymmetric -> Ok $"{id}>{label}]"
        | FlowShape.Parallelogram -> Ok $"{id}[/{label}/]"
        | FlowShape.ReverseParallelogram -> Ok $"{id}[\\{label}\\]"
        | FlowShape.Trapezoid -> Ok $"{id}[/{label}\\]"
        | FlowShape.ReverseTrapezoid -> Ok $"{id}[\\{label}/]"
        | FlowShape.FilledCircle -> data "f-circ"
        | FlowShape.FramedCircle -> data "fr-circ"
        | FlowShape.Bar -> data "fork"
        | FlowShape.Note -> data "note"
        | FlowShape.Document -> data "doc"
        | FlowShape.SmallCircle -> data "sm-circ"
        | FlowShape.CrossedCircle -> data "cross-circ"
        | FlowShape.Text -> data "text"
        | FlowShape.ThickCircle -> Error $"Node {id} has a shape that Mermaid flowcharts cannot write."

    let private link (edge: FlowEdge) =
        let head side (value: EdgeHead) =
            match value with
            | EdgeHead.None -> Ok ""
            | EdgeHead.Arrow -> Ok(if side then "<" else ">")
            | EdgeHead.Circle -> Ok "o"
            | EdgeHead.Cross -> Ok "x"
            | other -> Error $"A flowchart link cannot have a {other} head."

        match head true edge.StartHead, head false edge.EndHead with
        | Ok start, Ok finish ->
            let shaft =
                match edge.Line, finish with
                | FlowLine.Invisible, _ -> "~~~"
                | FlowLine.Solid, "" -> "---"
                | FlowLine.Solid, _ -> "--"
                | FlowLine.Dotted, "" -> "-.-"
                | FlowLine.Dotted, _ -> "-.-"
                | FlowLine.Thick, "" -> "==="
                | FlowLine.Thick, _ -> "=="

            let text =
                match edge.Label with
                | null -> ""
                | label -> $"|{quote label}|"

            if edge.Line = FlowLine.Invisible then
                Ok $"{edge.Source.Value} ~~~{text} {edge.Target.Value}"
            else
                Ok $"{edge.Source.Value} {start}{shaft}{finish}{text} {edge.Target.Value}"
        | Error message, _
        | _, Error message -> Error message

    let flowchart (model: Flowchart) =
        let lines = ResizeArray<string>()
        let errors = ResizeArray<string>()
        let declared = Collections.Generic.HashSet<DiagramId>()
        let nodes = model.Nodes |> Seq.map (fun node -> node.Id, node) |> dict
        let add indent (result: Result<string, string>) =
            match result with
            | Ok text -> lines.Add(String(' ', 2 * indent) + text)
            | Error message -> errors.Add message

        lines.Add $"flowchart {direction model.Direction}"

        // Explicit stack: nested subgraphs print without recursion.
        let stack = Collections.Generic.Stack<Choice<FlowGroup * int, int>>()

        for group in model.Groups |> Seq.filter (fun group -> isNull group.ParentId) |> Seq.rev do
            stack.Push(Choice1Of2(group, 1))

        while stack.Count > 0 do
            match stack.Pop() with
            | Choice2Of2 indent -> lines.Add(String(' ', 2 * indent) + "end")
            | Choice1Of2(group, indent) ->
                let pad = String(' ', 2 * indent)

                if group.Label = group.Id.Value then
                    lines.Add $"{pad}subgraph {group.Id.Value}"
                else
                    lines.Add $"{pad}subgraph {group.Id.Value} [{quote group.Label}]"

                for id in group.NodeIds do
                    if declared.Add id then add (indent + 1) (node nodes[id])

                stack.Push(Choice2Of2 indent)

                for child in model.Groups |> Seq.filter (fun child -> child.ParentId = group.Id) |> Seq.rev do
                    stack.Push(Choice1Of2(child, indent + 1))

        for node' in model.Nodes do
            if declared.Add node'.Id then add 1 (node node')

        for edge in model.Edges do
            add 1 (link edge)

        if errors.Count > 0 then Error(List.ofSeq errors) else Ok(String.Join("\n", lines) + "\n")

    let private arrow (message: SequenceMessage) =
        let shaft = if message.IsDashed then "--" else "-"

        match message.SenderHead, message.ReceiverHead with
        | EdgeHead.None, EdgeHead.Arrow -> Ok(shaft + ">>")
        | EdgeHead.Arrow, EdgeHead.Arrow -> Ok("<<" + shaft + ">>")
        | EdgeHead.None, EdgeHead.OpenArrow -> Ok(shaft + ">")
        | EdgeHead.None, EdgeHead.Cross -> Ok(shaft + "x")
        | EdgeHead.None, EdgeHead.Async -> Ok(shaft + ")")
        | EdgeHead.None, EdgeHead.HalfUpperFilled -> Ok(shaft + "|\\")
        | EdgeHead.None, EdgeHead.HalfLowerFilled -> Ok(shaft + "|/")
        | EdgeHead.None, EdgeHead.HalfUpper -> Ok(shaft + "\\\\")
        | EdgeHead.None, EdgeHead.HalfLower -> Ok(shaft + "//")
        | EdgeHead.HalfUpperFilled, EdgeHead.None -> Ok("/|" + shaft)
        | EdgeHead.HalfLowerFilled, EdgeHead.None -> Ok("\\|" + shaft)
        | EdgeHead.HalfUpper, EdgeHead.None -> Ok("//" + shaft)
        | EdgeHead.HalfLower, EdgeHead.None -> Ok("\\\\" + shaft)
        | first, second -> Error $"A Mermaid sequence message cannot have the heads {first} and {second}."

    let private number (value: decimal) = value.ToString("0.##", CultureInfo.InvariantCulture)

    let private declaration (participant: Participant) =
        let word = if participant.Kind = ParticipantKind.Actor then "actor" else "participant"

        let kind =
            match participant.Kind with
            | ParticipantKind.Participant
            | ParticipantKind.Actor -> ""
            | other -> $"@{{ \"type\": \"{other}\" }}"

        let alias = if participant.Label = participant.Id.Value then "" else $" as {participant.Label}"
        $"{word} {participant.Id.Value}{kind}{alias}"

    let sequence (model: SequenceDiagram) =
        let lines = ResizeArray<string>()
        let errors = ResizeArray<string>()
        let creators = Collections.Generic.Dictionary<int, Participant>()
        let destroyers = Collections.Generic.Dictionary<int, Participant>()
        let boxed = Collections.Generic.HashSet<DiagramId>()

        lines.Add "sequenceDiagram"

        for participant in model.Participants do
            if participant.CreatedBy.HasValue then creators[participant.CreatedBy.Value] <- participant
            if participant.DestroyedBy.HasValue then destroyers[participant.DestroyedBy.Value] <- participant

        let byId = model.Participants |> Seq.map (fun participant -> participant.Id, participant) |> dict

        for box in model.Boxes do
            let color = match box.Color with | null -> "" | color -> color + " "
            lines.Add($"  box {color}{box.Label}".TrimEnd())

            for id in box.Participants do
                boxed.Add id |> ignore
                lines.Add $"    {declaration byId[id]}"

            lines.Add "  end"

        for participant in model.Participants do
            if not (participant.CreatedBy.HasValue || boxed.Contains participant.Id) then
                lines.Add $"  {declaration participant}"

        let mutable messageIndex = 0
        let mutable numbering: (decimal * decimal) option = None

        // An explicit stack of (events, position, indent) keeps deep frames off the call stack.
        let stack = Collections.Generic.Stack<Choice<Collections.Immutable.ImmutableArray<SequenceEvent> * int * int, string>>()
        stack.Push(Choice1Of2(model.Events, 0, 1))

        while stack.Count > 0 do
            match stack.Pop() with
            | Choice2Of2 line -> lines.Add line
            | Choice1Of2(events, position, indent) when position < events.Length ->
                stack.Push(Choice1Of2(events, position + 1, indent))
                let pad = String(' ', 2 * indent)

                match events[position] with
                | SequenceEvent.Message message ->
                    match creators.TryGetValue messageIndex with
                    | true, created -> lines.Add $"{pad}create {declaration created}"
                    | false, _ -> ()

                    match destroyers.TryGetValue messageIndex with
                    | true, destroyed -> lines.Add $"{pad}destroy {destroyed.Id.Value}"
                    | false, _ -> ()

                    // The reader numbers from its state: the next number and the step. Print a statement
                    // whenever that state would not give this message's number.
                    match Option.ofNullable message.Number, numbering with
                    | None, Some _ ->
                        lines.Add $"{pad}autonumber off"
                        numbering <- None
                    | None, None -> ()
                    | Some value, Some(next, step) when value = next -> numbering <- Some(value + step, step)
                    | Some value, current ->
                        let following =
                            model.Messages
                            |> Seq.skip (messageIndex + 1)
                            |> Seq.tryHead
                            |> Option.bind (fun next -> Option.ofNullable next.Number)

                        let step =
                            match following, current with
                            | Some next, _ when next > value -> next - value
                            | _, Some(_, step) -> step
                            | _ -> 1m

                        lines.Add $"{pad}autonumber {number value} {number step}"
                        numbering <- Some(value + step, step)

                    match arrow message with
                    | Ok shaft ->
                        let sender = if message.StartsAtCentre then message.Sender.Value + "()" else message.Sender.Value
                        let receiver = if message.EndsAtCentre then "()" + message.Receiver.Value else message.Receiver.Value
                        lines.Add $"{pad}{sender}{shaft}{receiver}: {message.Text}"
                    | Error error -> errors.Add error

                    messageIndex <- messageIndex + 1
                | SequenceEvent.Note note ->
                    let anchors = note.Participants |> Seq.map (fun id -> id.Value) |> String.concat ","

                    match note.Placement with
                    | NotePlacement.Left -> lines.Add $"{pad}note left of {anchors}: {note.Text}"
                    | NotePlacement.Right -> lines.Add $"{pad}note right of {anchors}: {note.Text}"
                    | NotePlacement.Over -> lines.Add $"{pad}note over {anchors}: {note.Text}"
                | SequenceEvent.Activation activation ->
                    lines.Add $"{pad}{activation.Kind} {activation.Participant.Value}"
                | SequenceEvent.Frame frame when frame.Kind = SequenceFrameKind.Try ->
                    errors.Add "Mermaid sequence diagrams cannot write a ZenUML try frame."
                | SequenceEvent.Frame frame ->
                    let keyword =
                        match frame.Kind with
                        | SequenceFrameKind.Alternative -> "else"
                        | SequenceFrameKind.Critical -> "option"
                        | _ -> "and"

                    stack.Push(Choice2Of2 $"{pad}end")

                    for index in frame.Sections.Length - 1 .. -1 .. 0 do
                        let section = frame.Sections[index]
                        stack.Push(Choice1Of2(section.Events, 0, indent + 1))

                        let text =
                            if index = 0 then
                                let label =
                                    if frame.Kind = SequenceFrameKind.Rectangle then frame.Color else section.Label

                                match label with
                                | null -> frame.Kind.WireValue
                                | label -> $"{frame.Kind.WireValue} {label}"
                            else
                                match section.Label with
                                | null -> keyword
                                | label -> $"{keyword} {label}"

                        stack.Push(Choice2Of2 $"{pad}{text}")
            | Choice1Of2 _ -> ()

        if errors.Count > 0 then Error(List.ofSeq errors) else Ok(String.Join("\n", lines) + "\n")

    /// Canonical ZenUML text for a sequence. Calls print without bodies, so activations are not written; reading the
    /// text and printing it again gives the same text.
    let zenuml (model: SequenceDiagram) =
        let lines = ResizeArray<string>()
        let errors = ResizeArray<string>()
        let creations = Collections.Generic.Dictionary<int, Participant>()
        let starter = "_STARTER_"

        lines.Add "zenuml"

        for participant in model.Participants do
            if participant.CreatedBy.HasValue then creations[participant.CreatedBy.Value] <- participant

            if participant.Id.Value <> starter then
                let annotator =
                    match participant.Kind with
                    | ParticipantKind.Participant -> ""
                    | kind ->
                        let text = kind.ToString()
                        "@" + string (Char.ToUpperInvariant text[0]) + text.Substring 1 + " "

                let alias = if participant.Label = participant.Id.Value then "" else $" as {participant.Label}"
                lines.Add $"  {annotator}{participant.Id.Value}{alias}"

        let mutable messageIndex = 0
        let stack = Collections.Generic.Stack<Choice<Collections.Immutable.ImmutableArray<SequenceEvent> * int * int, string>>()
        stack.Push(Choice1Of2(model.Events, 0, 1))

        while stack.Count > 0 do
            match stack.Pop() with
            | Choice2Of2 line -> lines.Add line
            | Choice1Of2(events, position, indent) when position < events.Length ->
                stack.Push(Choice1Of2(events, position + 1, indent))
                let pad = String(' ', 2 * indent)

                match events[position] with
                | SequenceEvent.Message message ->
                    let from, target = message.Sender.Value, message.Receiver.Value

                    match creations.TryGetValue messageIndex with
                    | true, created when created.Id = message.Receiver ->
                        let text = message.Text

                        if text = "«create»" then lines.Add $"{pad}new {target}"
                        elif text.StartsWith('«') && text.EndsWith('»') then lines.Add $"{pad}new {target}({text.Substring(1, text.Length - 2)})"
                        else errors.Add $"The creation of {target} has a label that ZenUML cannot write."
                    | _ when message.IsDashed ->
                        lines.Add $"{pad}@return"
                        lines.Add $"{pad}{from}->{target}: {message.Text}"
                    | _ when message.ReceiverHead = EdgeHead.Arrow && from = starter -> lines.Add $"{pad}{target}.{message.Text}"
                    | _ when message.ReceiverHead = EdgeHead.Arrow -> lines.Add $"{pad}{from}->{target}.{message.Text}"
                    | _ -> lines.Add $"{pad}{from}->{target}: {message.Text}"

                    messageIndex <- messageIndex + 1
                | SequenceEvent.Activation _ -> ()
                | SequenceEvent.Note _ -> errors.Add "ZenUML has no notes."
                | SequenceEvent.Frame frame ->
                    let opening (label: string | null) =
                        match frame.Kind, label with
                        | SequenceFrameKind.Loop, null -> Ok "loop {"
                        | SequenceFrameKind.Loop, label -> Ok $"loop({label}) {{"
                        | SequenceFrameKind.Alternative, label -> Ok $"if({label}) {{"
                        | SequenceFrameKind.Optional, _ -> Ok "opt {"
                        | SequenceFrameKind.Parallel, _ -> Ok "par {"
                        | SequenceFrameKind.Try, _ -> Ok "try {"
                        | kind, _ -> Error $"ZenUML has no {kind} fragment."

                    let continuing (label: string | null) =
                        match label with
                        | null
                        | "else" -> "} else {"
                        | label when frame.Kind = SequenceFrameKind.Try -> $"}} {label} {{"
                        | label -> $"}} else if({label}) {{"

                    match opening frame.Sections[0].Label with
                    | Error error -> errors.Add error
                    | Ok first ->
                        stack.Push(Choice2Of2 $"{pad}}}")

                        for index in frame.Sections.Length - 1 .. -1 .. 0 do
                            let section = frame.Sections[index]
                            stack.Push(Choice1Of2(section.Events, 0, indent + 1))
                            stack.Push(Choice2Of2(pad + (if index = 0 then first else continuing section.Label)))
            | Choice1Of2 _ -> ()

        if errors.Count > 0 then
            Error(errors |> List.ofSeq |> List.map (fun message -> Reading.error "zenuml/unprintable" message None))
        else
            Ok(String.Join("\n", lines) + "\n")

    /// Canonical Mermaid text for a state diagram. Each region's block declares its states and the transitions that
    /// use its `[*]`; other transitions follow at the top level, where a mention does not move a state.
    let stateText (plantUml: bool) (model: StateDiagram) =
        let lines = ResizeArray<string>()
        let errors = ResizeArray<string>()
        let byId = model.States |> Seq.map (fun state -> state.Id, state) |> dict
        let language = if plantUml then "PlantUML" else "Mermaid"

        let roleOf (id: DiagramId) = match byId.TryGetValue id with | true, state -> Some state.Role | false, _ -> None

        // A history state of a composite prints as `Owner[H]`; one named after a region prints as `[H]` in that region.
        let historyOwner (id: DiagramId) =
            let value = id.Value
            let cut = value.LastIndexOf "::"
            let owner = if cut > 0 then value.Substring(0, cut) else value
            if byId.ContainsKey(DiagramId.Create owner) then Some owner else None

        // `[*]` and `[H]` name the pseudostates whose identifiers their region or owner gives; others are declared.
        let starForm (id: DiagramId) =
            match byId.TryGetValue id with
            | true, state when state.Role = StateRole.Initial -> id.Value = state.Region + "::initial"
            | true, state when state.Role = StateRole.Final -> id.Value = state.Region + "::final"
            | _ -> false

        let historyForm (id: DiagramId) =
            match roleOf id with
            | Some StateRole.ShallowHistory -> id.Value.EndsWith("::history", StringComparison.Ordinal)
            | Some StateRole.DeepHistory -> id.Value.EndsWith("::deep-history", StringComparison.Ordinal)
            | _ -> false

        let scoped (id: DiagramId) = starForm id || plantUml && historyForm id && (historyOwner id).IsNone

        let regionOfStar (transition: StateTransition) = [ transition.Source; transition.Target ] |> List.tryFind scoped |> Option.map (fun id -> byId[id].Region)

        let endpoint (id: DiagramId) =
            match roleOf id with
            | _ when starForm id -> "[*]"
            | Some StateRole.ShallowHistory when plantUml && historyForm id -> (historyOwner id |> Option.defaultValue "") + "[H]"
            | Some StateRole.DeepHistory when plantUml && historyForm id -> (historyOwner id |> Option.defaultValue "") + "[H*]"
            | _ -> id.Value

        let transition pad (transition: StateTransition) =
            let label = if transition.Label = "" then "" else $" : {transition.Label}"
            lines.Add $"{pad}{endpoint transition.Source} --> {endpoint transition.Target}{label}"

        let quoted (label: string) =
            if label.Contains '"' then errors.Add $"The label {label} has a quote that {language} cannot write."
            "\"" + label + "\""

        lines.Add(if plantUml then "@startuml" else "stateDiagram-v2")

        match model.Direction, plantUml with
        | FlowDirection.TopToBottom, _ -> ()
        | FlowDirection.LeftToRight, true -> lines.Add "left to right direction"
        | other, true -> errors.Add $"PlantUML state diagrams cannot write the direction {direction other}."
        | other, false -> lines.Add $"  direction {direction other}"

        let stack = Collections.Generic.Stack<Choice<string * int, string>>()
        stack.Push(Choice1Of2("root", 1))

        while stack.Count > 0 do
            match stack.Pop() with
            | Choice2Of2 line -> lines.Add line
            | Choice1Of2(region, indent) ->
                let pad = String(' ', 2 * indent)
                let members = model.States |> Seq.filter (fun state -> state.Region = region) |> Seq.toList
                let pending = ResizeArray<Choice<string * int, string>>()

                let named (state: DiagramState) =
                    if state.Label <> state.Id.Value then $"{quoted state.Label} as {state.Id.Value}" else state.Id.Value

                for state in members do
                    match state.Role with
                    | StateRole.Initial
                    | StateRole.Final when starForm state.Id -> ()
                    | StateRole.ShallowHistory
                    | StateRole.DeepHistory when plantUml && historyForm state.Id -> ()
                    | StateRole.Initial when plantUml -> lines.Add $"{pad}state {named state} <<start>>"
                    | StateRole.Final when plantUml -> lines.Add $"{pad}state {named state} <<end>>"
                    | StateRole.ShallowHistory when plantUml -> lines.Add $"{pad}state {named state} <<history>>"
                    | StateRole.DeepHistory when plantUml -> lines.Add $"{pad}state {named state} <<history*>>"
                    | StateRole.Simple ->
                        if state.Label <> state.Id.Value || plantUml then lines.Add $"{pad}state {named state}"
                        else lines.Add $"{pad}{state.Id.Value}"

                        for text in state.Descriptions do lines.Add $"{pad}{state.Id.Value} : {text}"
                    | StateRole.Choice
                    | StateRole.Fork
                    | StateRole.Join -> lines.Add $"{pad}state {named state} <<{state.Role.WireValue}>>"
                    | StateRole.EntryPoint when plantUml -> lines.Add $"{pad}state {named state} <<entryPoint>>"
                    | StateRole.ExitPoint when plantUml -> lines.Add $"{pad}state {named state} <<exitPoint>>"
                    | StateRole.Composite ->
                        let regions = model.Regions |> Seq.filter (fun candidate -> candidate.Owner = state.Id) |> Seq.toList
                        let opening = if state.Label <> state.Id.Value then $"state {quoted state.Label} as {state.Id.Value} {{" else $"state {state.Id.Value} {{"
                        pending.Add(Choice2Of2 $"{pad}{opening}")

                        regions
                        |> List.iteri (fun index inner ->
                            if index > 0 then pending.Add(Choice2Of2 $"{pad}  --")
                            pending.Add(Choice1Of2(inner.Id, indent + 1)))

                        pending.Add(Choice2Of2 $"{pad}}}")
                    | role -> errors.Add $"{language} state diagrams cannot write the {role} state {state.Id}."

                // Transitions of this region's [*] come after its states and nested blocks, so every state they
                // name is already declared in its own region.
                let own = model.Transitions |> Seq.filter (fun transition -> regionOfStar transition = Some region) |> Seq.toList

                for item in List.rev own do
                    let label = if item.Label = "" then "" else $" : {item.Label}"
                    stack.Push(Choice2Of2 $"{pad}{endpoint item.Source} --> {endpoint item.Target}{label}")

                for item in Seq.rev pending do stack.Push item

        // The top-level transitions and the notes close the text.
        for item in model.Transitions do
            match regionOfStar item with
            | None -> transition "  " item
            | Some _ -> ()

        for note in model.Notes do
            let side = if note.Placement = NotePlacement.Left then "left" else "right"

            if note.Text.Contains '\n' then
                lines.Add $"  note {side} of {note.Target.Value}"
                for line in note.Text.Split '\n' do lines.Add $"    {line}"
                lines.Add "  end note"
            else
                lines.Add $"  note {side} of {note.Target.Value} : {note.Text}"

        if plantUml then lines.Add "@enduml"

        if errors.Count > 0 then Error(List.ofSeq errors) else Ok(String.Join("\n", lines) + "\n")

    let state model = stateText false model

    /// Canonical Mermaid text for an architecture diagram.
    let architecture (model: ArchitectureDiagram) =
        let lines = ResizeArray<string>([ "architecture-beta" ])
        let label (text: string) = "[\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"]"
        let icon (key: string | null) = match key with | null -> "" | key -> $"({key})"
        let parent (id: DiagramId | null) = match id with | null -> "" | id -> $" in {id.Value}"

        match model.Title with
        | null -> ()
        | title -> lines.Add $"  title {title}"

        for group in model.Groups do
            lines.Add $"  group {group.Id.Value}{icon group.Icon}{label group.Label}{parent group.Parent}"

        for service in model.Services do
            let text = match service.IconText with | null -> "" | text -> "\"" + text + "\""
            lines.Add $"  service {service.Id.Value}{icon service.Icon}{text}{label service.Label}{parent service.Parent}"

        for junction in model.Junctions do
            lines.Add $"  junction {junction.Id.Value}{parent junction.Parent}"

        for edge in model.Edges do
            let boundary present = if present then "{group}" else ""
            let middle = match edge.Label with | null -> "--" | text -> $"-{label text}-"
            let left = if edge.SourceHead then "<" else ""
            let right = if edge.TargetHead then ">" else ""
            lines.Add $"  {edge.Source.Value}{boundary edge.SourceGroup}:{edge.SourceSide} {left}{middle}{right} {edge.TargetSide}:{edge.Target.Value}{boundary edge.TargetGroup}"

        for alignment in model.Alignments do
            let members = alignment.Members |> Seq.map (fun id -> id.Value) |> String.concat " "
            lines.Add $"""  align {(if alignment.IsRow then "row" else "column")} {members}"""

        Ok(String.Join("\n", lines) + "\n")

    /// Canonical PlantUML activity text in the legacy dialect, which writes any flow graph: `(*)` for starts and
    /// stops, `===bar===` for forks and joins, and `"label" as id` for every other step. Decision and merge roles and
    /// lanes have no legacy form and read back as plain activities outside lanes.
    /// A PlantUML `@startregex` or `@startebnf` block. Capturing and non-capturing groups both print as capturing.
    let railroad (model: RailroadDiagram) =
        let quantifier (item: RailroadItem) =
            match item.Kind, item.Minimum, item.MaximumOption with
            | RailroadKind.Optional, _, _ -> "?"
            | _, 0, None -> "*"
            | _, 1, None -> "+"
            | _, low, None -> $"{{{low},}}"
            | _, low, Some high when low = high -> $"{{{low}}}"
            | _, low, Some high -> $"{{{low},{high}}}"

        let rec regex (item: RailroadItem) =
            match item.Kind with
            | RailroadKind.Terminal -> Text.RegularExpressions.Regex.Replace(item.Text, @"[\\.^$|?*+()\[\]{}/]", "\\$0")
            | RailroadKind.Special -> item.Text
            | RailroadKind.CharacterClass -> $"[{item.Text}]"
            | RailroadKind.Sequence -> item.Children |> Seq.map (fun child -> if child.Kind = RailroadKind.Choice then $"(?:{regex child})" else regex child) |> String.concat ""
            | RailroadKind.Choice -> item.Children |> Seq.map regex |> String.concat "|"
            | RailroadKind.Optional
            | RailroadKind.Repeat ->
                let child = item.Children[0]

                let atom =
                    match child.Kind with
                    | RailroadKind.Sequence
                    | RailroadKind.Choice -> $"(?:{regex child})"
                    | RailroadKind.Terminal when child.Text.Length > 1 -> $"(?:{regex child})"
                    | _ -> regex child

                atom + quantifier item
            | RailroadKind.Group when item.Text <> "" -> $"(?<{item.Text}>{regex item.Children[0]})"
            | RailroadKind.Group -> $"({regex item.Children[0]})"
            | RailroadKind.Assertion -> $"({item.Text}{regex item.Children[0]})"
            | _ -> ""

        let rec ebnf (item: RailroadItem) =
            let atom (child: RailroadItem) =
                match child.Kind with
                | RailroadKind.Sequence
                | RailroadKind.Choice -> $"( {ebnf child} )"
                | _ -> ebnf child

            // A count repeats one factor, so an exception needs parentheses there and nowhere else.
            let factor (child: RailroadItem) =
                if child.Kind = RailroadKind.Exception then $"( {ebnf child} )" else atom child

            match item.Kind with
            | RailroadKind.Terminal -> if item.Text.Contains '"' then $"'{item.Text}'" else $"\"{item.Text}\""
            | RailroadKind.NonTerminal -> item.Text
            | RailroadKind.Special -> $"? {item.Text} ?"
            | RailroadKind.Sequence -> item.Children |> Seq.map atom |> String.concat ", "
            | RailroadKind.Choice -> item.Children |> Seq.map ebnf |> String.concat " | "
            | RailroadKind.Optional -> $"[ {ebnf item.Children[0]} ]"
            | RailroadKind.Repeat ->
                match item.Minimum, item.MaximumOption with
                | 0, None -> $"{{ {ebnf item.Children[0]} }}"
                | 1, None -> $"{{ {ebnf item.Children[0]} }}-"
                | count, _ -> $"{count} * {factor item.Children[0]}"
            | RailroadKind.Group -> $"( {ebnf item.Children[0]} )"
            | RailroadKind.Exception -> $"{atom item.Children[0]} - {atom item.Children[1]}"
            | _ -> ""

        if model.IsRegex then
            Ok($"@startregex\n{regex model.Rules[0].Body}\n@endregex\n")
        else
            let rules = model.Rules |> Seq.map (fun rule -> $"{rule.Name} = {ebnf rule.Body};") |> String.concat "\n"
            Ok($"@startebnf\n{rules}\n@endebnf\n")

    /// A PlantUML `@startjson` or `@startyaml` block: the title first, the highlights, then the data.
    let dataTree (model: DataTreeDiagram) =
        let problems = ResizeArray<string>()

        let quote (text: string) =
            let builder = Text.StringBuilder("\"")

            for c in text do
                match c with
                | '"' -> builder.Append "\\\"" |> ignore
                | '\\' -> builder.Append "\\\\" |> ignore
                | '\n' -> builder.Append "\\n" |> ignore
                | '\r' -> builder.Append "\\r" |> ignore
                | '\t' -> builder.Append "\\t" |> ignore
                | c when c < ' ' || Char.IsSurrogate c -> builder.Append($"\\u{int c:x4}") |> ignore
                | c -> builder.Append c |> ignore

            builder.Append('"').ToString()

        let json (root: DataValue) =
            let output = Text.StringBuilder()

            let rec write (indent: string) (value: DataValue) =
                let inner = indent + "  "

                match value.Kind with
                | DataValueKind.Object when value.Members.IsEmpty -> output.Append "{}" |> ignore
                | DataValueKind.Array when value.Items.IsEmpty -> output.Append "[]" |> ignore
                | DataValueKind.Object ->
                    output.Append "{\n" |> ignore

                    value.Members
                    |> Seq.iteri (fun index item ->
                        output.Append(inner).Append(quote item.Key).Append(": ") |> ignore
                        write inner item.Value
                        output.Append(if index < value.Members.Length - 1 then ",\n" else "\n") |> ignore)

                    output.Append(indent).Append('}') |> ignore
                | DataValueKind.Array ->
                    output.Append "[\n" |> ignore

                    value.Items
                    |> Seq.iteri (fun index item ->
                        output.Append inner |> ignore
                        write inner item
                        output.Append(if index < value.Items.Length - 1 then ",\n" else "\n") |> ignore)

                    output.Append(indent).Append(']') |> ignore
                | DataValueKind.String -> output.Append(quote value.Text) |> ignore
                | _ -> output.Append value.Text |> ignore

            write "" root
            output.Append('\n').ToString()

        // YAML scalars are text; quotes protect text that would read as something else.
        let yamlText (text: string) =
            let risky =
                text <> text.Trim()
                || text.StartsWith '"' || text.StartsWith '\'' || text.StartsWith '[' || text.StartsWith '#' || text.StartsWith '>'
                || text.StartsWith "- " || text = "-" || text = "|" || text.Contains " #" || text.Contains '\t'

            if text.Contains '\n' || text.Contains '\r' then problems.Add "A YAML string with a line break has no PlantUML form here."
            if not risky then text elif text.Contains '"' then $"'{text}'" else $"\"{text}\""

        let yamlKey (key: string) =
            if key.Contains '"' || key.Contains '\n' then problems.Add $"The key {key} has no PlantUML YAML form."
            if key = "" || key.Contains ':' || key <> key.Trim() || key.StartsWith '-' || key.StartsWith '#' || key.StartsWith '\'' then $"\"{key}\"" else key

        let yaml (root: DataValue) =
            let output = Text.StringBuilder()

            let rec members (indent: string) (first: string option) (value: DataValue) =
                value.Members
                |> Seq.iteri (fun index item ->
                    let lead = if index = 0 then defaultArg first indent else indent
                    output.Append(lead).Append(yamlKey item.Key).Append(':') |> ignore
                    entry (indent + "  ") item.Value)

            and entry (inner: string) (value: DataValue) =
                match value.Kind with
                | DataValueKind.Object when value.Members.IsEmpty ->
                    problems.Add "An empty YAML map has no PlantUML form."
                    output.Append '\n' |> ignore
                | DataValueKind.Array when value.Items.IsEmpty -> output.Append " []\n" |> ignore
                | DataValueKind.Object ->
                    output.Append '\n' |> ignore
                    members inner None value
                | DataValueKind.Array when value.Items |> Seq.forall (fun item -> item.Kind = DataValueKind.String) && value.Items |> Seq.exists (fun item -> item.Text = "") ->
                    // A flow list keeps empty items, which a block list cannot hold.
                    let escaped (text: string) = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace(",", "\\,")
                    let flow = value.Items |> Seq.map (fun item -> $"\"{escaped item.Text}\"") |> String.concat ", "
                    output.Append(" [").Append(flow).Append("]\n") |> ignore
                | DataValueKind.Array ->
                    output.Append '\n' |> ignore
                    items inner value
                | _ when value.Text = "" -> output.Append '\n' |> ignore
                | _ when
                    value.Text.EndsWith '\n'
                    && value.Text.TrimEnd('\n').Split('\n') |> Array.forall (fun line -> line <> "" && line = line.Trim())
                    ->
                    // A literal block: each line under the key, with the final line break.
                    output.Append(" |\n") |> ignore

                    for line in value.Text.TrimEnd('\n').Split('\n') do
                        output.Append(inner).Append(line).Append('\n') |> ignore
                | _ -> output.Append(' ').Append(yamlText value.Text).Append('\n') |> ignore

            and items (indent: string) (value: DataValue) =
                for item in value.Items do
                    match item.Kind with
                    | DataValueKind.Object when not item.Members.IsEmpty -> members (indent + "  ") (Some(indent + "- ")) item
                    | DataValueKind.Object
                    | DataValueKind.Array -> problems.Add "A YAML list item that is a list or an empty map has no PlantUML form."
                    | _ when item.Text = "" -> problems.Add "An empty YAML list item has no PlantUML form."
                    | _ -> output.Append(indent).Append("- ").Append(yamlText item.Text).Append('\n') |> ignore

            match root.Kind with
            | DataValueKind.Object when not root.Members.IsEmpty -> members "" None root
            | DataValueKind.Array when not root.Items.IsEmpty -> items "" root
            | _ -> problems.Add "A YAML block needs a map or a list at its root."

            output.ToString()

        let body = if model.Format = DataFormat.Json then json model.Root else yaml model.Root

        let highlights =
            model.Highlights
            |> Seq.map (fun highlight ->
                let path = highlight.Path |> Seq.map (fun segment -> $"\"{segment}\"") |> String.concat " / "
                let stereotype = match highlight.Stereotype with | null -> "" | value -> " " + value
                $"#highlight {path}{stereotype}\n")
            |> String.concat ""

        let title = match model.Title with | null -> "" | value -> $"title {value}\n"
        let kind = model.Format.WireValue

        if problems.Count > 0 then Error(List.ofSeq problems)
        else Ok($"@start{kind}\n{title}{highlights}{body}@end{kind}\n")

    let activity (model: SwimlaneDiagram) =
        let lines = ResizeArray<string>()
        let errors = ResizeArray<string>()
        let steps = model.Steps |> Seq.map (fun step -> step.Id, step) |> dict
        let declared = Collections.Generic.HashSet<DiagramId>()

        lines.Add "@startuml"

        match model.Direction with
        | FlowDirection.TopToBottom -> ()
        | FlowDirection.LeftToRight -> lines.Add "left to right direction"
        | other -> errors.Add $"PlantUML activity diagrams cannot write the direction {direction other}."

        // Names are given in order of first mention, so reading the text back and printing it gives the same names.
        let aliases = Collections.Generic.Dictionary<DiagramId, string>()

        let name (id: DiagramId) =
            let step = steps[id]
            let alias prefix =
                match aliases.TryGetValue id with
                | true, found -> found
                | false, _ ->
                    let found = $"{prefix}{aliases.Count + 1}"
                    aliases[id] <- found
                    found

            match step.Role with
            | SwimlaneRole.Start
            | SwimlaneRole.Stop
            | SwimlaneRole.End -> "(*)"
            | SwimlaneRole.Fork
            | SwimlaneRole.Join -> "===" + alias "bar" + "==="
            | _ when declared.Add id ->
                // A legacy label cannot hold a double quote; it is written as a single one.
                "\"" + step.Label.Replace("\"", "'").Replace("\n", "\\n") + "\" as " + alias "a"
            | _ -> alias "a"

        let notes = model.Steps |> Seq.filter (fun step -> step.Role = SwimlaneRole.Note) |> Seq.map (fun step -> step.Id) |> Collections.Generic.HashSet
        let attached = Collections.Generic.Dictionary<DiagramId, ResizeArray<string>>()

        for connector in model.Connectors do
            if notes.Contains connector.Source then
                match attached.TryGetValue connector.Target with
                | true, found -> found.Add steps[connector.Source].Label
                | false, _ -> attached[connector.Target] <- ResizeArray [ steps[connector.Source].Label ]

        for connector in model.Connectors do
            if not (notes.Contains connector.Source) then
                let label = match connector.Label with | null -> "" | text -> "[" + text.Replace("\n", "\\n") + "] "
                lines.Add $"{name connector.Source} -->{label}{name connector.Target}"

                match attached.TryGetValue connector.Target with
                | true, found ->
                    for text in found do
                        if text.Contains '\n' then
                            lines.Add "note right"
                            lines.AddRange(text.Split '\n')
                            lines.Add "end note"
                        else
                            lines.Add $"note right: {text}"

                    attached.Remove connector.Target |> ignore
                | false, _ -> ()

        lines.Add "@enduml"
        if errors.Count > 0 then Error(List.ofSeq errors) else Ok(String.Join("\n", lines) + "\n")

    /// Canonical Mermaid text for a flowchart or sequence diagram.
    let print (diagram: Diagram) =
        let result =
            match diagram with
            | Diagram.Flowchart model -> flowchart model
            | Diagram.Sequence model -> sequence model
            | Diagram.State model -> state model
            | Diagram.Architecture model -> architecture model
            | Diagram.Swimlane model ->
                flowchart model.Projection
                |> Result.map (fun text -> "swimlane-beta" + text.Substring("flowchart".Length))
            | other -> Error [ $"{other} has no Mermaid printer." ]

        result
        |> Result.mapError (List.map (fun message -> Reading.error "mermaid/unprintable" message None))
