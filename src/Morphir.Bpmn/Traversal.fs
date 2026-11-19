// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Bpmn

open System
open System.Collections.Generic
open System.Collections.Immutable

/// What a traversal does after visiting a node.
[<RequireQualifiedAccess>]
type BpmnTraversalControl =
    /// Visit the node's children, then go on.
    | Continue
    /// Do not visit the node's children; go on with its next sibling.
    | SkipChildren
    /// End the traversal.
    | Stop

    /// The control as documentation writes it.
    member this.WireValue =
        match this with
        | BpmnTraversalControl.Continue -> "continue"
        | BpmnTraversalControl.SkipChildren -> "skip-children"
        | BpmnTraversalControl.Stop -> "stop"

    override this.ToString() = this.WireValue

/// The kinds of connection a graph query follows.
[<Flags>]
type BpmnEdgeKinds =
    /// No connection.
    | None = 0
    /// From an element to each of its child elements.
    | Containment = 1
    /// From a sequence flow's source to its target.
    | SequenceFlow = 2
    /// From a message flow's source to its target.
    | MessageFlow = 4
    /// From an association's or data association's source to its target.
    | Association = 8
    /// From an element to each element that a reference field of it names, such as a shape's `bpmnElement`.
    | Reference = 16
    /// Every kind.
    | All = 31

/// Which way a graph query follows connections.
[<RequireQualifiedAccess>]
type BpmnDirection =
    /// From source to target.
    | Forward
    /// From target to source.
    | Reverse
    /// Both ways.
    | Both

    /// The direction as documentation writes it.
    member this.WireValue =
        match this with
        | BpmnDirection.Forward -> "forward"
        | BpmnDirection.Reverse -> "reverse"
        | BpmnDirection.Both -> "both"

    override this.ToString() = this.WireValue

/// The answer of a graph query.
[<RequireQualifiedAccess>]
type BpmnGraphResult =
    /// One shortest route, from the start to the end.
    | Route of ImmutableArray<BpmnElement>
    /// Every reachable element once, the start first, in breadth-first order.
    | Reachable of ImmutableArray<BpmnElement>
    /// No route exists.
    | Unreachable
    /// No element has the identifier: the start, the end, or a reference that the search reached.
    | Missing of string
    /// Several elements share the identifier.
    | Ambiguous of string * ImmutableArray<BpmnElement>
    /// The query visited more elements than its limit allows.
    | LimitReached

    /// Calls the function for the case, for C# callers.
    member this.Match<'T>
        (
            route: Func<ImmutableArray<BpmnElement>, 'T>,
            reachable: Func<ImmutableArray<BpmnElement>, 'T>,
            unreachable: Func<'T>,
            missing: Func<string, 'T>,
            ambiguous: Func<string, ImmutableArray<BpmnElement>, 'T>,
            limitReached: Func<'T>
        ) =
        match this with
        | BpmnGraphResult.Route elements -> route.Invoke elements
        | BpmnGraphResult.Reachable elements -> reachable.Invoke elements
        | BpmnGraphResult.Unreachable -> unreachable.Invoke()
        | BpmnGraphResult.Missing id -> missing.Invoke id
        | BpmnGraphResult.Ambiguous(id, elements) -> ambiguous.Invoke(id, elements)
        | BpmnGraphResult.LimitReached -> limitReached.Invoke()

    override this.ToString() =
        let ids (elements: ImmutableArray<BpmnElement>) =
            elements |> Seq.map (fun element -> match element.Id with | null -> "-" | id -> id) |> String.concat " "

        match this with
        | BpmnGraphResult.Route elements -> $"Route [{ids elements}]"
        | BpmnGraphResult.Reachable elements -> $"Reachable [{ids elements}]"
        | BpmnGraphResult.Unreachable -> "Unreachable"
        | BpmnGraphResult.Missing id -> $"Missing {id}"
        | BpmnGraphResult.Ambiguous(id, _) -> $"Ambiguous {id}"
        | BpmnGraphResult.LimitReached -> "LimitReached"

/// Read-only traversal and bounded graph queries over a document. Nothing here recurses on the call stack.
[<AbstractClass; Sealed>]
type BpmnTraversal private () =
    static let referenceFields =
        [
            "attachedToRef"; "processRef"; "bpmnElement"; "default"; "flowNodeRef"; "messageRef"; "errorRef"; "signalRef"
            "escalationRef"; "dataObjectRef"; "dataStoreRef"; "categoryValueRef"; "calledElement"; "activityRef"
            "participantRef"; "initiatingParticipantRef"; "messageFlowRef"; "calledChoreographyRef"
        ]

    /// Visits every node in document order, the parent before its children. The visitor gets the node and its depth.
    static member Walk(document: BpmnDocument, visitor: Func<BpmnNode, int, BpmnTraversalControl>) =
        ArgumentNullException.ThrowIfNull document
        ArgumentNullException.ThrowIfNull visitor
        let stack = Stack<BpmnNode * int>()

        for node in Seq.rev document.Nodes do
            stack.Push(node, 0)

        let mutable stopped = false

        while not stopped && stack.Count > 0 do
            let node, depth = stack.Pop()

            match visitor.Invoke(node, depth) with
            | BpmnTraversalControl.Stop -> stopped <- true
            | BpmnTraversalControl.SkipChildren -> ()
            | BpmnTraversalControl.Continue ->
                for child in Seq.rev node.Children do
                    stack.Push(child, depth + 1)

    /// The connections of the selected kinds, as (from, to) pairs in document order of the element that makes them,
    /// and the connections whose other end names a shared identifier, from the end that resolves.
    static member private Edges(document: BpmnDocument, kinds: BpmnEdgeKinds) =
        let has (kind: BpmnEdgeKinds) = kinds &&& kind = kind
        let edges = ResizeArray<BpmnElement * BpmnElement>()
        let ambiguous = ResizeArray<BpmnElement * bool * string * ImmutableArray<BpmnElement>>()

        let resolve (value: string) =
            match document.Resolve value with
            | BpmnLookup.Found target -> Some target
            | _ -> None

        let ends (element: BpmnElement) =
            match element.References "sourceRef" |> Seq.tryHead, element.References "targetRef" |> Seq.tryHead with
            | Some source, Some target ->
                match document.Resolve source, document.Resolve target with
                | BpmnLookup.Found source, BpmnLookup.Found target -> edges.Add(source, target)
                | BpmnLookup.Ambiguous shared, BpmnLookup.Found target -> ambiguous.Add(target, false, source, shared)
                | BpmnLookup.Found from, BpmnLookup.Ambiguous shared -> ambiguous.Add(from, true, target, shared)
                | _ -> ()
            | _ -> ()

        for element in document.Elements do
            if has BpmnEdgeKinds.Containment then
                for child in element.Children do
                    edges.Add(element, child)

            if element.IsModel then
                match element.Kind with
                | "sequenceFlow" when has BpmnEdgeKinds.SequenceFlow -> ends element
                | "messageFlow" when has BpmnEdgeKinds.MessageFlow -> ends element
                | "association"
                | "dataInputAssociation"
                | "dataOutputAssociation" when has BpmnEdgeKinds.Association -> ends element
                | _ -> ()

            if has BpmnEdgeKinds.Reference then
                for field in referenceFields do
                    for value in element.References field do
                        match document.Resolve value with
                        | BpmnLookup.Found target -> edges.Add(element, target)
                        | BpmnLookup.Ambiguous shared -> ambiguous.Add(element, true, value, shared)
                        // A reference that names nothing, such as one into an imported document, ends the search there.
                        | BpmnLookup.Missing -> ambiguous.Add(element, true, value, ImmutableArray.Empty)

        edges, ambiguous

    static member private Search(document: BpmnDocument, start: string, target: string option, kinds: BpmnEdgeKinds, direction: BpmnDirection, limit: int) =
        ArgumentNullException.ThrowIfNull document
        ArgumentNullException.ThrowIfNull direction
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero limit

        // Undefined bits or no kind at all would quietly find nothing.
        if kinds = BpmnEdgeKinds.None || (kinds &&& ~~~BpmnEdgeKinds.All) <> BpmnEdgeKinds.None then
            raise (ArgumentOutOfRangeException(nameof kinds, "Select at least one defined edge kind."))

        let find id =
            match document.Find id with
            | BpmnLookup.Found element -> Ok element
            | BpmnLookup.Missing -> Error(BpmnGraphResult.Missing id)
            | BpmnLookup.Ambiguous elements -> Error(BpmnGraphResult.Ambiguous(id, elements))

        match find start, (target |> Option.map find) with
        | Error result, _
        | _, Some(Error result) -> result
        | Ok origin, goal ->
            let goal = goal |> Option.map (function | Ok element -> element | Error _ -> origin)
            let next = Dictionary<BpmnElement, ResizeArray<BpmnElement>>(HashIdentity.Reference)

            let link (from: BpmnElement) (into: BpmnElement) =
                match next.TryGetValue from with
                | true, list -> list.Add into
                | false, _ -> next[from] <- ResizeArray [ into ]

            let edges, ambiguous = BpmnTraversal.Edges(document, kinds)

            // A connection whose far end is a shared identifier makes the answer ambiguous once the search reaches it.
            let blocked = Dictionary<BpmnElement, BpmnGraphResult>(HashIdentity.Reference)

            for known, forward, id, shared in ambiguous do
                match direction with
                | BpmnDirection.Forward when not forward -> ()
                | BpmnDirection.Reverse when forward -> ()
                | _ -> blocked.TryAdd(known, (if shared.IsEmpty then BpmnGraphResult.Missing id else BpmnGraphResult.Ambiguous(id, shared))) |> ignore

            for source, sink in edges do
                match direction with
                | BpmnDirection.Forward -> link source sink
                | BpmnDirection.Reverse -> link sink source
                | BpmnDirection.Both ->
                    link source sink
                    link sink source

            // Breadth-first: each element once, so cycles end; neighbours in document order of their connections.
            let previous = Dictionary<BpmnElement, BpmnElement option>(HashIdentity.Reference)
            let order = ResizeArray<BpmnElement>()
            let queue = Queue<BpmnElement>()
            previous[origin] <- None
            queue.Enqueue origin
            let mutable found = goal = Some origin
            let mutable exceeded = false
            let mutable unclear = None

            while not found && not exceeded && unclear.IsNone && queue.Count > 0 do
                let current = queue.Dequeue()
                order.Add current

                if order.Count > limit then
                    exceeded <- true
                elif blocked.ContainsKey current then
                    unclear <- Some blocked[current]
                else
                    match next.TryGetValue current with
                    | true, neighbours ->
                        for neighbour in neighbours do
                            if not found && not (previous.ContainsKey neighbour) then
                                previous[neighbour] <- Some current
                                queue.Enqueue neighbour
                                if goal = Some neighbour then found <- true
                    | false, _ -> ()

            if exceeded then
                BpmnGraphResult.LimitReached
            elif unclear.IsSome then
                unclear.Value
            else
                match goal with
                | Some goal when found ->
                    let route = ResizeArray<BpmnElement>()
                    let mutable cursor = Some goal

                    while cursor.IsSome do
                        route.Add cursor.Value
                        cursor <- previous[cursor.Value]

                    route.Reverse()
                    BpmnGraphResult.Route(ImmutableArray.CreateRange route)
                | Some _ -> BpmnGraphResult.Unreachable
                | None ->
                    while queue.Count > 0 do
                        order.Add(queue.Dequeue())

                    if order.Count > limit then BpmnGraphResult.LimitReached
                    else BpmnGraphResult.Reachable(ImmutableArray.CreateRange order)

    /// Every element reachable from the start over the selected connections, visiting at most `limit` elements.
    /// A null start is Missing.
    static member Reachable(document: BpmnDocument, start: string | null, kinds: BpmnEdgeKinds, direction: BpmnDirection, limit: int) =
        match start with
        | null -> BpmnGraphResult.Missing ""
        | start -> BpmnTraversal.Search(document, start, None, kinds, direction, limit)

    /// One shortest route from the start to the end over the selected connections, visiting at most `limit` elements.
    /// A null start or end is Missing.
    static member Route(document: BpmnDocument, start: string | null, finish: string | null, kinds: BpmnEdgeKinds, direction: BpmnDirection, limit: int) =
        match start, finish with
        | null, _
        | _, null -> BpmnGraphResult.Missing ""
        | start, finish -> BpmnTraversal.Search(document, start, Some finish, kinds, direction, limit)
