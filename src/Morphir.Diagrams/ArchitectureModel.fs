// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable

/// A group of services, which may sit in another group.
[<Sealed>]
type ArchitectureGroup internal (id: DiagramId, label: string, icon: string option, parent: DiagramId option) =
    /// Identifier.
    member _.Id = id

    /// Title of the group.
    member _.Label = label

    /// Icon key, or null.
    member _.Icon: string | null = Option.toObj icon

    /// The group this group is in, or null.
    member _.Parent: DiagramId | null = Option.toObj parent

    override _.ToString() = label

/// A service: a box with an icon or icon text, which may sit in a group.
[<Sealed>]
type ArchitectureService
    internal (id: DiagramId, label: string, icon: string option, iconText: string option, parent: DiagramId option) =
    /// Identifier.
    member _.Id = id

    /// Title of the service.
    member _.Label = label

    /// Icon key, such as `database` or `logos:aws`, or null.
    member _.Icon: string | null = Option.toObj icon

    /// Text drawn in place of an icon, or null.
    member _.IconText: string | null = Option.toObj iconText

    /// The group the service is in, or null.
    member _.Parent: DiagramId | null = Option.toObj parent

    override _.ToString() = label

/// A junction: a point where edges meet.
[<Sealed>]
type ArchitectureJunction internal (id: DiagramId, parent: DiagramId option) =
    /// Identifier.
    member _.Id = id

    /// The group the junction is in, or null.
    member _.Parent: DiagramId | null = Option.toObj parent

    override _.ToString() = id.Value

/// An edge between two ports: each end leaves by a side (L, R, T or B), may attach to the outline of its group, and
/// may have an arrowhead.
[<Sealed>]
type ArchitectureEdge
    internal
    (
        source: DiagramId,
        sourceSide: char,
        sourceGroup: bool,
        sourceHead: bool,
        target: DiagramId,
        targetSide: char,
        targetGroup: bool,
        targetHead: bool,
        label: string option
    ) =
    /// The service or junction at the first end.
    member _.Source = source

    /// The side of the first end: L, R, T or B.
    member _.SourceSide = sourceSide

    /// Whether the first end attaches to the outline of its service's group.
    member _.SourceGroup = sourceGroup

    /// Whether the first end has an arrowhead.
    member _.SourceHead = sourceHead

    /// The service or junction at the second end.
    member _.Target = target

    /// The side of the second end: L, R, T or B.
    member _.TargetSide = targetSide

    /// Whether the second end attaches to the outline of its service's group.
    member _.TargetGroup = targetGroup

    /// Whether the second end has an arrowhead.
    member _.TargetHead = targetHead

    /// Edge text, or null.
    member _.Label: string | null = Option.toObj label

    override _.ToString() = $"{source}:{sourceSide} to {targetSide}:{target}"

/// A request that services and junctions line up in a row or a column.
[<Sealed>]
type ArchitectureAlignment internal (row: bool, members: ImmutableArray<DiagramId>) =
    /// Whether the members line up in a row; otherwise in a column.
    member _.IsRow = row

    /// The services and junctions to line up.
    member _.Members = members

    override _.ToString() = (if row then "row " else "column ") + String.Join(" ", members)

/// A Mermaid architecture diagram.
[<Sealed>]
type ArchitectureDiagram
    internal
    (
        title: string option,
        groups: ImmutableArray<ArchitectureGroup>,
        services: ImmutableArray<ArchitectureService>,
        junctions: ImmutableArray<ArchitectureJunction>,
        edges: ImmutableArray<ArchitectureEdge>,
        alignments: ImmutableArray<ArchitectureAlignment>
    ) =
    static let span = SourceSpan(SourcePosition(0, 1, 1), SourcePosition(0, 1, 1))

    let projection =
        lazy
            (let icon (key: string | null) =
                match key with
                | null -> None
                | key -> Some(let colon = key.IndexOf ':' in if colon >= 0 then key.Substring(colon + 1) else key)

             let nodes =
                 [
                     for service in services do
                         let label = match service.IconText with | null -> service.Label | text -> $"{text}\n{service.Label}"
                         FlowNode(service.Id, label, FlowShape.Rectangle, span, None, icon service.Icon)

                     for junction in junctions do
                         FlowNode(junction.Id, "", FlowShape.SmallCircle, span, Some "external", None)
                 ]

             let members (group: DiagramId) =
                 [
                     for service in services do
                         if service.Parent = group then service.Id

                     for junction in junctions do
                         if junction.Parent = group then junction.Id
                 ]

             let groups =
                 groups |> Seq.map (fun group -> FlowGroup(group.Id, group.Label, Option.ofObj group.Parent, ImmutableArray.CreateRange(members group.Id), span))

             let parentOf = Dictionary<DiagramId, DiagramId>()

             for service in services do
                 match service.Parent with
                 | null -> ()
                 | parent -> parentOf[service.Id] <- parent

             let endpoint (id: DiagramId) toGroup =
                 match toGroup, parentOf.TryGetValue id with
                 | true, (true, group) -> group
                 | _ -> id

             let lines =
                 edges
                 |> Seq.map (fun edge ->
                     let head present = if present then EdgeHead.Arrow else EdgeHead.None

                     FlowEdge(
                         endpoint edge.Source edge.SourceGroup,
                         endpoint edge.Target edge.TargetGroup,
                         Option.ofObj edge.Label,
                         FlowLine.Solid,
                         head edge.SourceHead,
                         head edge.TargetHead,
                         span,
                         Some(edge.SourceSide, edge.TargetSide)
                     ))

             // Ports on the left and right lay the services out across the page; top and bottom ports down it.
             let across = edges |> Seq.filter (fun edge -> edge.SourceSide = 'L' || edge.SourceSide = 'R') |> Seq.length
             let direction = if across * 2 >= edges.Length then FlowDirection.LeftToRight else FlowDirection.TopToBottom

             Flowchart(direction, ImmutableArray.CreateRange nodes, ImmutableArray.CreateRange lines, ImmutableArray.CreateRange groups))

    /// Title, or null.
    member _.Title: string | null = Option.toObj title

    /// Groups in declaration order.
    member _.Groups = groups

    /// Services in declaration order.
    member _.Services = services

    /// Junctions in declaration order.
    member _.Junctions = junctions

    /// Edges in source order.
    member _.Edges = edges

    /// Alignments in source order.
    member _.Alignments = alignments

    member internal _.Projection = projection.Value

    override _.ToString() = $"Architecture diagram with {services.Length} services and {edges.Length} edges"
