// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Collections.Generic

/// Reads a Mermaid `swimlane-beta` body: the flowchart grammar, with subgraphs as lanes.
[<RequireQualifiedAccess>]
module internal SwimlaneReader =
    /// The role of a step from its shape.
    let roleOf shape =
        match shape with
        | FlowShape.Diamond -> SwimlaneRole.Decision
        | FlowShape.FilledCircle
        | FlowShape.SmallCircle -> SwimlaneRole.Start
        | FlowShape.FramedCircle -> SwimlaneRole.Stop
        | FlowShape.CrossedCircle -> SwimlaneRole.End
        | FlowShape.Bar -> SwimlaneRole.Fork
        | FlowShape.Note -> SwimlaneRole.Note
        | _ -> SwimlaneRole.Action

    /// The same graph as lanes: groups become lanes and each node a step in its innermost lane.
    let ofFlowchart (flowchart: Flowchart) =
        let laneOf = Dictionary<DiagramId, DiagramId>()

        for group in flowchart.Groups do
            for id in group.NodeIds do
                laneOf[id] <- group.Id

        let lanes = flowchart.Groups |> Seq.map (fun group -> SwimlaneLane(group.Id, group.Label, Option.ofObj group.ParentId))

        let steps =
            flowchart.Nodes
            |> Seq.map (fun node ->
                let lane = match laneOf.TryGetValue node.Id with | true, lane -> Some lane | false, _ -> None
                SwimlaneStep(node.Id, node.Label, roleOf node.Shape, lane, node.Shape))

        let connectors =
            flowchart.Edges
            |> Seq.map (fun edge -> SwimlaneConnector(edge.Source, edge.Target, Option.ofObj edge.Label, edge.Line, edge.EndHead <> EdgeHead.None))

        SwimlaneDiagram(flowchart.Direction, Reading.immutable lanes, Reading.immutable steps, Reading.immutable connectors)

    let read (direction: FlowDirection) (statements: (Reading.Line * int) seq) : Result<Diagram, Diagnostic list> =
        match FlowchartReader.read direction statements with
        | Ok(Diagram.Flowchart flowchart) -> Ok(Diagram.Swimlane(ofFlowchart flowchart))
        | Ok other -> Ok other
        | Error diagnostic -> Error [ diagnostic ]
