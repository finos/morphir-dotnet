// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable

/// What a state is: an ordinary or composite state, or one of the pseudostates.
[<RequireQualifiedAccess>]
type StateRole =
    /// An ordinary state.
    | Simple
    /// A state that owns one or more regions of states.
    | Composite
    /// The initial pseudostate of a region, written `[*]` as a source.
    | Initial
    /// The final state of a region, written `[*]` as a target.
    | Final
    /// A choice pseudostate.
    | Choice
    /// A fork pseudostate.
    | Fork
    /// A join pseudostate.
    | Join
    /// A shallow history pseudostate.
    | ShallowHistory
    /// A deep history pseudostate.
    | DeepHistory
    /// An entry point of a composite.
    | EntryPoint
    /// An exit point of a composite.
    | ExitPoint

    /// The role as documentation writes it, such as `shallow-history`.
    member this.WireValue =
        match this with
        | StateRole.Simple -> "simple"
        | StateRole.Composite -> "composite"
        | StateRole.Initial -> "initial"
        | StateRole.Final -> "final"
        | StateRole.Choice -> "choice"
        | StateRole.Fork -> "fork"
        | StateRole.Join -> "join"
        | StateRole.ShallowHistory -> "shallow-history"
        | StateRole.DeepHistory -> "deep-history"
        | StateRole.EntryPoint -> "entry-point"
        | StateRole.ExitPoint -> "exit-point"

    override this.ToString() = this.WireValue

/// A state or pseudostate in one region of a state diagram.
[<Sealed>]
type DiagramState internal (id: DiagramId, label: string, role: StateRole, region: string, descriptions: ImmutableArray<string>) =
    /// Identifier as authored, or the region-scoped identifier of an implicit initial or final state.
    member _.Id = id

    /// Display label.
    member _.Label = label

    /// What the state is.
    member _.Role = role

    /// Identifier of the region that owns the state, such as `root` or `Work::region-1`.
    member _.Region = region

    /// Description lines, such as `entry / log`, in source order.
    member _.Descriptions = descriptions

    /// A state for a model that a caller builds; check the model with StateDiagram.Validate.
    static member Create(id: DiagramId, label: string, role: StateRole, region: string, descriptions: IReadOnlyList<string>) =
        ArgumentNullException.ThrowIfNull id
        ArgumentNullException.ThrowIfNull label
        ArgumentNullException.ThrowIfNull role
        ArgumentNullException.ThrowIfNull region
        ArgumentNullException.ThrowIfNull descriptions
        DiagramState(id, label, role, region, ImmutableArray.CreateRange descriptions)

    override _.ToString() = $"{id} ({role})"

/// An ordered region of states: the root region, or one region of a composite.
[<Sealed>]
type StateRegion internal (id: string, owner: DiagramId option) =
    /// Identifier, such as `root` or `Work::region-0`.
    member _.Id = id

    /// The composite that owns the region, or null for the root region.
    member _.Owner: DiagramId | null = Option.toObj owner

    /// A region for a model that a caller builds.
    static member Create(id: string, owner: DiagramId | null) =
        ArgumentNullException.ThrowIfNull id
        StateRegion(id, Option.ofObj owner)

    override _.ToString() = id

/// A transition between two states. Event, guard and action are text and never evaluated.
[<Sealed>]
type StateTransition internal (source: DiagramId, target: DiagramId, label: string) =
    let guardStart = label.IndexOf '['
    let guardEnd = if guardStart < 0 then -1 else label.IndexOf(']', guardStart)
    let slash = label.IndexOf('/', max 0 guardEnd)

    /// Identifier of the source state.
    member _.Source = source

    /// Identifier of the target state.
    member _.Target = target

    /// The whole label, such as `start [ready] / begin`.
    member _.Label = label

    /// The event text before any guard or action.
    member _.Event =
        let finish = [ guardStart; slash ] |> List.filter (fun index -> index >= 0) |> List.fold min label.Length
        label.Substring(0, finish).Trim()

    /// The guard text inside brackets, or empty.
    member _.Guard = if guardEnd > guardStart then label.Substring(guardStart + 1, guardEnd - guardStart - 1).Trim() else ""

    /// The action text after the slash, or empty.
    member _.Action = if slash >= 0 then label.Substring(slash + 1).Trim() else ""

    /// A transition for a model that a caller builds.
    static member Create(source: DiagramId, target: DiagramId, label: string) =
        ArgumentNullException.ThrowIfNull source
        ArgumentNullException.ThrowIfNull target
        ArgumentNullException.ThrowIfNull label
        StateTransition(source, target, label)

    override _.ToString() = $"{source} to {target}"

/// A note beside a state.
[<Sealed>]
type StateNote internal (target: DiagramId, text: string, placement: NotePlacement) =
    /// Identifier of the state the note describes.
    member _.Target = target

    /// Note text, which may have several lines.
    member _.Text = text

    /// Left or right of the state.
    member _.Placement = placement

    override _.ToString() = text

/// A state diagram read from Mermaid or PlantUML, independent of its source language.
[<Sealed>]
type StateDiagram
    internal
    (
        direction: FlowDirection,
        states: ImmutableArray<DiagramState>,
        regions: ImmutableArray<StateRegion>,
        transitions: ImmutableArray<StateTransition>,
        notes: ImmutableArray<StateNote>,
        collapsed: Set<DiagramId>
    ) =
    static let span = SourceSpan(SourcePosition(0, 1, 1), SourcePosition(0, 1, 1))

    let projection =
        lazy
            (let byId = Dictionary<DiagramId, DiagramState>()
             for state in states do byId[state.Id] <- state
             let owners = regions |> Seq.map (fun region -> region.Id, Option.ofObj region.Owner) |> dict
             let regionsOf = regions |> Seq.filter (fun region -> not (isNull region.Owner)) |> Seq.groupBy (fun region -> region.Owner) |> Seq.map (fun (owner, found) -> owner, Seq.toList found) |> dict

             // The composite that a state is drawn inside, when one of its ancestors is collapsed: the outermost one.
             let rec ancestors (state: DiagramState) =
                 match owners.TryGetValue state.Region with
                 | true, Some owner when byId.ContainsKey owner -> owner :: ancestors byId[owner]
                 | _ -> []

             let hiddenBy (state: DiagramState) = ancestors state |> List.filter collapsed.Contains |> List.tryLast

             let shown id =
                 match byId.TryGetValue id with
                 | true, state -> hiddenBy state |> Option.defaultValue id
                 | false, _ -> id

             let drawnAsGroup (state: DiagramState) = state.Role = StateRole.Composite && not (collapsed.Contains state.Id)

             let node (state: DiagramState) =
                 let text = String.Join("\n", Seq.append [ state.Label ] state.Descriptions)

                 let shape, label =
                     match state.Role with
                     | StateRole.Simple
                     | StateRole.Composite -> FlowShape.Rounded, text
                     | StateRole.Initial -> FlowShape.FilledCircle, ""
                     | StateRole.Final -> FlowShape.FramedCircle, ""
                     | StateRole.Choice -> FlowShape.Diamond, ""
                     | StateRole.Fork
                     | StateRole.Join -> FlowShape.Bar, ""
                     | StateRole.ShallowHistory -> FlowShape.Circle, "H"
                     | StateRole.DeepHistory -> FlowShape.Circle, "H*"
                     | StateRole.EntryPoint -> FlowShape.SmallCircle, state.Label
                     | StateRole.ExitPoint -> FlowShape.CrossedCircle, state.Label

                 FlowNode(state.Id, label, shape, span)

             let visible = states |> Seq.filter (fun state -> (hiddenBy state).IsNone) |> Seq.toList

             let groupIdOfRegion (region: string) =
                 match owners.TryGetValue region with
                 | true, Some owner when regionsOf.ContainsKey owner && regionsOf[owner].Length > 1 -> Some(DiagramId.Create region)
                 | true, Some owner -> Some owner
                 | _ -> None

             let nodes =
                 [
                     for state in visible do
                         if not (drawnAsGroup state) then yield node state

                     for index, note in Seq.indexed notes do
                         if (match byId.TryGetValue note.Target with | true, state -> (hiddenBy state).IsNone | _ -> true) then
                             yield FlowNode(DiagramId.Create $"{note.Target}::note-{index}", note.Text, FlowShape.Note, span, Some "external", None)
                 ]

             let groups =
                 [
                     for state in visible do
                         if drawnAsGroup state then
                             let parent = groupIdOfRegion state.Region
                             let own = match regionsOf.TryGetValue state.Id with | true, found -> found | false, _ -> []

                             let members region =
                                 visible |> List.filter (fun member' -> member'.Region = region && not (drawnAsGroup member')) |> List.map (fun member' -> member'.Id)

                             if own.Length > 1 then
                                 yield FlowGroup(state.Id, state.Label, parent, ImmutableArray.Empty, span)

                                 for region in own do
                                     yield FlowGroup(DiagramId.Create region.Id, "", Some state.Id, ImmutableArray.CreateRange (members region.Id), span)
                             else
                                 let ids = own |> List.collect (fun region -> members region.Id)
                                 yield FlowGroup(state.Id, state.Label, parent, ImmutableArray.CreateRange ids, span)
                 ]

             let edges =
                 [
                     for transition in transitions do
                         let source, target = shown transition.Source, shown transition.Target

                         // A transition inside a collapsed composite is not drawn; one that crosses its outline ends on it.
                         if not (source = target && source <> transition.Source && target <> transition.Target) then
                             let label = if transition.Label = "" then None else Some transition.Label
                             yield FlowEdge(source, target, label, FlowLine.Solid, EdgeHead.None, EdgeHead.Arrow, span)

                     for index, note in Seq.indexed notes do
                         if (match byId.TryGetValue note.Target with | true, state -> (hiddenBy state).IsNone | _ -> true) then
                             yield FlowEdge(DiagramId.Create $"{note.Target}::note-{index}", note.Target, None, FlowLine.Dotted, EdgeHead.None, EdgeHead.None, span)
                 ]

             Flowchart(direction, ImmutableArray.CreateRange nodes, ImmutableArray.CreateRange edges, ImmutableArray.CreateRange groups))

    internal new(direction, states, regions, transitions, notes) =
        StateDiagram(direction, states, regions, transitions, notes, Set.empty)

    /// Layout direction.
    member _.Direction = direction

    /// States and pseudostates in first-mention order.
    member _.States = states

    /// The root region first, then each composite's regions in order.
    member _.Regions = regions

    /// Transitions in source order; parallel and self transitions are distinct.
    member _.Transitions = transitions

    /// Notes in source order.
    member _.Notes = notes

    /// Composites drawn collapsed.
    member _.Collapsed = collapsed |> Seq.sort |> ImmutableArray.CreateRange

    member internal _.Projection = projection.Value

    /// The same model drawn with a composite collapsed: its descendants are hidden and transitions that cross its
    /// outline end on it. The model's states and transitions are unchanged.
    member _.WithCollapsed(composite: DiagramId) =
        ArgumentNullException.ThrowIfNull composite
        StateDiagram(direction, states, regions, transitions, notes, collapsed.Add composite)

    /// A model that a caller builds. It is not checked: call Validate.
    static member Create
        (
            direction: FlowDirection,
            states: IReadOnlyList<DiagramState>,
            regions: IReadOnlyList<StateRegion>,
            transitions: IReadOnlyList<StateTransition>
        ) =
        ArgumentNullException.ThrowIfNull direction
        ArgumentNullException.ThrowIfNull states
        ArgumentNullException.ThrowIfNull regions
        ArgumentNullException.ThrowIfNull transitions
        StateDiagram(direction, ImmutableArray.CreateRange states, ImmutableArray.CreateRange regions, ImmutableArray.CreateRange transitions, ImmutableArray.Empty)

    /// Structural problems, each with a stable code: duplicate identifiers, missing regions, owners, endpoints and note
    /// targets, ownership cycles, and transitions into an initial state or out of a final state. Nothing is repaired.
    member _.Validate() =
        let found = ResizeArray<Diagnostic>()
        let report code message = found.Add(Diagnostic(code, DiagnosticSeverity.Error, message, None))
        let ids = HashSet<DiagramId>()
        let regionIds = regions |> Seq.map (fun region -> region.Id) |> HashSet
        let byId = Dictionary<DiagramId, DiagramState>()

        for state in states do
            if not (ids.Add state.Id) then report "state/duplicate-id" $"State {state.Id} is declared twice."
            byId[state.Id] <- state
            if not (regionIds.Contains state.Region) then report "state/missing-region" $"State {state.Id} belongs to the missing region {state.Region}."

        for region in regions do
            match region.Owner with
            | null -> ()
            | owner when not (byId.ContainsKey owner) -> report "state/missing-owner" $"Region {region.Id} is owned by the missing state {owner}."
            | owner when byId[owner].Role <> StateRole.Composite -> report "state/owner-not-composite" $"Region {region.Id} is owned by {owner}, which is not composite."
            | owner ->
                // Walk up the chain of owners; meeting a composite twice is a cycle.
                let owners = regions |> Seq.map (fun region -> region.Id, Option.ofObj region.Owner) |> dict
                let visited = HashSet<DiagramId>()
                let mutable composite = Some owner
                let mutable cycle = false

                while composite.IsSome && not cycle do
                    let current = composite.Value

                    if not (visited.Add current) then
                        cycle <- true
                    else
                        composite <-
                            match byId.TryGetValue current with
                            | true, state ->
                                match owners.TryGetValue state.Region with
                                | true, next -> next
                                | false, _ -> None
                            | false, _ -> None

                if cycle then report "state/ownership-cycle" $"Region {region.Id} is inside a cycle of composites."

        for transition in transitions do
            for endpoint in [ transition.Source; transition.Target ] do
                if not (byId.ContainsKey endpoint) then report "state/missing-endpoint" $"Transition {transition} names the missing state {endpoint}."

            match byId.TryGetValue transition.Target with
            | true, state when state.Role = StateRole.Initial -> report "state/into-initial" $"Transition {transition} enters an initial state."
            | _ -> ()

            match byId.TryGetValue transition.Source with
            | true, state when state.Role = StateRole.Final -> report "state/out-of-final" $"Transition {transition} leaves a final state."
            | _ -> ()

        for note in notes do
            if not (byId.ContainsKey note.Target) then report "state/missing-note-target" $"A note describes the missing state {note.Target}."

        ImmutableArray.CreateRange found

    override _.ToString() = $"State diagram with {states.Length} states and {transitions.Length} transitions"
