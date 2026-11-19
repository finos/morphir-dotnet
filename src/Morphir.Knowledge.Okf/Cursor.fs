// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Okf

open System
open System.Collections.Immutable
open System.Runtime.InteropServices
open Morphir.Knowledge

/// The next step after a visitor sees a node.
[<RequireQualifiedAccess>]
type VisitControl =
    | Continue
    | SkipChildren
    | Stop

    override this.ToString() =
        match this with
        | Continue -> "continue"
        | SkipChildren -> "skip children"
        | Stop -> "stop"

/// An immutable position within a syntax or semantic tree.
[<Sealed>]
type Cursor<'Node> internal
    (
        node: 'Node,
        children: 'Node -> ImmutableArray<'Node>,
        location: 'Node -> SourceSpan voption,
        parent: Cursor<'Node> option,
        siblings: ImmutableArray<'Node>,
        index: int
    ) =
    /// The selected node.
    member _.Node = node
    /// The source range where it is available.
    member _.Span = location node
    /// For C# callers, writes the node's span when it has a source range.
    member _.TryGetSpan([<Out>] span: byref<SourceSpan>) =
        match location node with
        | ValueSome value ->
            span <- value
            true
        | ValueNone ->
            span <- Unchecked.defaultof<_>
            false
    /// Move to the parent, when this cursor has one.
    member _.Parent = parent
    /// Move to the first child, when this node has children.
    member this.FirstChild =
        let childNodes = children node
        if childNodes.IsEmpty then None
        else Some(Cursor(childNodes[0], children, location, Some this, childNodes, 0))

    /// Move to the next sibling, when there is one.
    member _.NextSibling =
        let next = index + 1
        if next >= siblings.Length then None
        else Some(Cursor(siblings[next], children, location, parent, siblings, next))

    /// Move to the previous sibling, when there is one.
    member _.PreviousSibling =
        if index = 0 then None
        else Some(Cursor(siblings[index - 1], children, location, parent, siblings, index - 1))

    /// For C# callers, the parent or null.
    member this.ParentOrNull: Cursor<'Node> | null = Option.toObj this.Parent
    /// For C# callers, the first child or null.
    member this.FirstChildOrNull: Cursor<'Node> | null = Option.toObj this.FirstChild
    /// For C# callers, the next sibling or null.
    member this.NextSiblingOrNull: Cursor<'Node> | null = Option.toObj this.NextSibling
    /// For C# callers, the previous sibling or null.
    member this.PreviousSiblingOrNull: Cursor<'Node> | null = Option.toObj this.PreviousSibling

    member internal this.Visit(visit: Cursor<'Node> -> VisitControl) =
        let rec nextAtDepth remaining (cursor: Cursor<'Node>) =
            if remaining = 0 then None
            else
                match cursor.NextSibling with
                | Some next -> Some(next, remaining)
                | None -> nextAtDepth (remaining - 1) cursor.Parent.Value

        let rec loop depth cursor =
            let next =
                match visit cursor with
                | VisitControl.Continue ->
                    match cursor.FirstChild with
                    | Some child -> Some(child, depth + 1)
                    | None -> nextAtDepth depth cursor
                | VisitControl.SkipChildren -> nextAtDepth depth cursor
                | VisitControl.Stop -> None
            match next with
            | Some(nextCursor, nextDepth) -> loop nextDepth nextCursor
            | None -> ()

        loop 0 this

    /// Visits the selected subtree in depth-first preorder. For C# callers.
    member this.Walk(visitor: Func<Cursor<'Node>, VisitControl>) = this.Visit visitor.Invoke

    override _.ToString() = "OKF cursor"
