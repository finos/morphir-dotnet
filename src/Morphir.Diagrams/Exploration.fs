// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable

/// The elements and directed connections that exploration walks, with their element references.
type internal Structure =
    {
        Nodes: (DiagramId * string) array
        /// Source index, target index, follows forward, follows backward, element reference.
        Connections: (int * int * bool * bool * string) array
    }

[<RequireQualifiedAccess>]
module internal Structures =
    let ofDiagram (diagram: Diagram) =
        let nodes = ResizeArray<DiagramId * string>()
        let index = Dictionary<DiagramId, int>()

        let add id (reference: DiagramElementRef) =
            if not (index.ContainsKey id) then
                index[id] <- nodes.Count
                nodes.Add(id, reference.ToString())

        let connections =
            match diagram, diagram.Graph with
            | _, Some graph ->
                for node in graph.Nodes do add node.Id (DiagramElementRef.Node node.Id)
                for group in graph.Groups do add group.Id (DiagramElementRef.Group group.Id)
                let references = ElementRefs.Edges graph

                [|
                    for position in 0 .. graph.Edges.Length - 1 do
                        let edge = graph.Edges[position]

                        if edge.Line <> FlowLine.Invisible then
                            yield index[edge.Source], index[edge.Target], edge.EndHead <> EdgeHead.None, edge.StartHead <> EdgeHead.None, references[position].ToString()
                |]
            | Diagram.Sequence model, None ->
                for participant in model.Participants do add participant.Id (DiagramElementRef.Participant participant.Id)
                let references = ElementRefs.Messages model

                [|
                    for message in model.Messages do
                        yield index[message.Sender], index[message.Receiver], message.ReceiverHead <> EdgeHead.None, message.SenderHead <> EdgeHead.None, references[message].ToString()
                |]
            | _ -> [||]

        { Nodes = nodes.ToArray(); Connections = connections }

    /// Visits beyond this many elements end a walk with a limit result.
    let visitLimit = 10_000

    /// Breadth-first search in connection order. Each connection is followed from source to target when it has an
    /// end head, the other way when it has a start head, and both ways when it has neither and the walk is undirected.
    /// An upstream walk follows every connection against those directions.
    let walk (structure: Structure) origin upstream undirected =
        let neighbours = Array.init structure.Nodes.Length (fun _ -> ResizeArray<int>())

        for source, target, forward, backward, _ in structure.Connections do
            let plain = undirected && not forward && not backward
            let along, against = (forward || plain), (backward || plain)
            let along, against = if upstream then against, along else along, against
            if along then neighbours[source].Add target
            if against then neighbours[target].Add source

        let previous = Array.create structure.Nodes.Length -2
        let order = ResizeArray<int>()
        let queue = Queue<int>()
        previous[origin] <- -1
        queue.Enqueue origin
        let mutable exhausted = false

        while queue.Count > 0 && not exhausted do
            let current = queue.Dequeue()
            order.Add current

            if order.Count > visitLimit then
                exhausted <- true
            else
                for next in neighbours[current] do
                    if previous[next] = -2 then
                        previous[next] <- current
                        queue.Enqueue next

        exhausted, order, previous

/// The result of a structural exploration: no guard, condition or timer is evaluated.
[<RequireQualifiedAccess>]
type ExplorationResult =
    /// The elements of one shortest directed route, from the first to the last.
    | Route of ImmutableArray<DiagramId>
    /// The elements reachable from the origin, in breadth-first order, the origin first.
    | Reachable of ImmutableArray<DiagramId>
    /// No route joins the two elements in the followed directions.
    | Unreachable
    /// An element is not part of the diagram.
    | Invalid of reason: string
    /// The walk visited more elements than the limit allows.
    | LimitExhausted

    /// For C# callers: every case needs a function.
    member this.Match
        (
            route: Func<ImmutableArray<DiagramId>, 'T>,
            reachable: Func<ImmutableArray<DiagramId>, 'T>,
            unreachable: Func<'T>,
            invalid: Func<string, 'T>,
            limitExhausted: Func<'T>
        ) =
        match this with
        | ExplorationResult.Route elements -> route.Invoke elements
        | ExplorationResult.Reachable elements -> reachable.Invoke elements
        | ExplorationResult.Unreachable -> unreachable.Invoke()
        | ExplorationResult.Invalid reason -> invalid.Invoke reason
        | ExplorationResult.LimitExhausted -> limitExhausted.Invoke()

    override this.ToString() =
        let join (elements: ImmutableArray<DiagramId>) = elements |> Seq.map (fun id -> id.Value) |> String.concat " → "

        match this with
        | ExplorationResult.Route elements -> $"Structural route {join elements}"
        | ExplorationResult.Reachable elements -> $"Structurally reachable {join elements}"
        | ExplorationResult.Unreachable -> "Unreachable"
        | ExplorationResult.Invalid reason -> reason
        | ExplorationResult.LimitExhausted -> $"More than {Structures.visitLimit} elements"

/// Structural routes and reachability between the elements of a diagram, as the offline player shows them.
[<AbstractClass; Sealed>]
type DiagramExploration private () =
    static let locate (structure: Structure) (id: DiagramId) =
        structure.Nodes |> Array.tryFindIndex (fun (node, _) -> node = id)

    /// One shortest route from source to target that follows arrowheads, and links without heads when undirected.
    static member Route(diagram: Diagram, source: DiagramId, target: DiagramId, undirected: bool) =
        ArgumentNullException.ThrowIfNull diagram
        ArgumentNullException.ThrowIfNull source
        ArgumentNullException.ThrowIfNull target
        let structure = Structures.ofDiagram diagram

        match locate structure source, locate structure target with
        | None, _ -> ExplorationResult.Invalid $"{source} is not an element of the diagram."
        | _, None -> ExplorationResult.Invalid $"{target} is not an element of the diagram."
        | Some origin, Some goal ->
            let exhausted, _, previous = Structures.walk structure origin false undirected

            if previous[goal] = -2 then
                if exhausted then ExplorationResult.LimitExhausted else ExplorationResult.Unreachable
            else
                let path = ResizeArray<DiagramId>()
                let mutable current = goal

                while current >= 0 do
                    path.Add(fst structure.Nodes[current])
                    current <- previous[current]

                path.Reverse()
                ExplorationResult.Route(ImmutableArray.CreateRange path)

    /// Every element reachable from the origin downstream (following arrowheads) or upstream (against them).
    static member Reachable(diagram: Diagram, origin: DiagramId, downstream: bool, undirected: bool) =
        ArgumentNullException.ThrowIfNull diagram
        ArgumentNullException.ThrowIfNull origin
        let structure = Structures.ofDiagram diagram

        match locate structure origin with
        | None -> ExplorationResult.Invalid $"{origin} is not an element of the diagram."
        | Some start ->
            match Structures.walk structure start (not downstream) undirected with
            | true, _, _ -> ExplorationResult.LimitExhausted
            | false, order, _ -> ExplorationResult.Reachable(order |> Seq.map (fun index -> fst structure.Nodes[index]) |> ImmutableArray.CreateRange)
