// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Collections.Immutable

/// What a step of a swimlane or activity diagram does in the flow of control.
[<RequireQualifiedAccess>]
type SwimlaneRole =
    /// An ordinary action.
    | Action
    /// Where the flow starts.
    | Start
    /// Where the whole flow stops.
    | Stop
    /// Where one branch of the flow ends while others go on.
    | End
    /// A decision that chooses one outgoing branch.
    | Decision
    /// Where alternative branches meet again.
    | Merge
    /// Where the flow splits into concurrent branches.
    | Fork
    /// Where concurrent branches wait for each other.
    | Join
    /// A note.
    | Note

    /// The role as documentation writes it.
    member this.WireValue =
        match this with
        | SwimlaneRole.Action -> "action"
        | SwimlaneRole.Start -> "start"
        | SwimlaneRole.Stop -> "stop"
        | SwimlaneRole.End -> "end"
        | SwimlaneRole.Decision -> "decision"
        | SwimlaneRole.Merge -> "merge"
        | SwimlaneRole.Fork -> "fork"
        | SwimlaneRole.Join -> "join"
        | SwimlaneRole.Note -> "note"

    override this.ToString() = this.WireValue

/// One step of the flow, in at most one lane.
[<Sealed>]
type SwimlaneStep internal (id: DiagramId, label: string, role: SwimlaneRole, lane: DiagramId option, shape: FlowShape) =
    /// Identifier as authored, or as generated for a PlantUML step.
    member _.Id = id

    /// Display text.
    member _.Label = label

    /// What the step does.
    member _.Role = role

    /// The lane that holds the step, or null when it is outside every lane.
    member _.Lane: DiagramId | null = Option.toObj lane

    member internal _.Shape = shape

    override _.ToString() = $"{id} ({role})"

/// A lane: a named band of steps.
[<Sealed>]
type SwimlaneLane internal (id: DiagramId, label: string, parent: DiagramId option) =
    /// Identifier of the lane.
    member _.Id = id

    /// Title shown on the band.
    member _.Label = label

    /// The lane this lane is nested in, or null.
    member _.Parent: DiagramId | null = Option.toObj parent

    override _.ToString() = label

/// A connector between two steps, which may carry a branch label such as a guard.
[<Sealed>]
type SwimlaneConnector internal (source: DiagramId, target: DiagramId, label: string option, line: FlowLine, head: bool) =
    /// Identifier of the step the flow leaves.
    member _.Source = source

    /// Identifier of the step the flow enters.
    member _.Target = target

    /// Branch text, or null.
    member _.Label: string | null = Option.toObj label

    /// Stroke style.
    member _.Line = line

    /// Whether the connector has an arrowhead at its target.
    member _.HasHead = head

    override _.ToString() = $"{source} to {target}"

/// A swimlane diagram, read from Mermaid `swimlane-beta` or a PlantUML activity diagram.
[<Sealed>]
type SwimlaneDiagram
    internal
    (
        direction: FlowDirection,
        lanes: ImmutableArray<SwimlaneLane>,
        steps: ImmutableArray<SwimlaneStep>,
        connectors: ImmutableArray<SwimlaneConnector>
    ) =
    static let span = SourceSpan(SourcePosition(0, 1, 1), SourcePosition(0, 1, 1))

    let projection =
        lazy
            (let nodes =
                steps
                |> Seq.map (fun step ->
                    let tone = if step.Role = SwimlaneRole.Note then Some "external" else None
                    FlowNode(step.Id, step.Label, step.Shape, span, tone, None))

             let groups =
                 lanes
                 |> Seq.map (fun lane ->
                     let members = steps |> Seq.filter (fun step -> step.Lane = lane.Id) |> Seq.map (fun step -> step.Id)
                     FlowGroup(lane.Id, lane.Label, Option.ofObj lane.Parent, ImmutableArray.CreateRange members, span))

             let edges =
                 connectors
                 |> Seq.map (fun connector ->
                     let head = if connector.HasHead then EdgeHead.Arrow else EdgeHead.None
                     FlowEdge(connector.Source, connector.Target, Option.ofObj connector.Label, connector.Line, EdgeHead.None, head, span))

             Flowchart(direction, ImmutableArray.CreateRange nodes, ImmutableArray.CreateRange edges, ImmutableArray.CreateRange groups, true))

    /// Layout direction.
    member _.Direction = direction

    /// Lanes in declaration order.
    member _.Lanes = lanes

    /// Steps in first-mention order.
    member _.Steps = steps

    /// Connectors in source order.
    member _.Connectors = connectors

    member internal _.Projection = projection.Value

    override _.ToString() = $"Swimlane diagram with {lanes.Length} lanes and {steps.Length} steps"
