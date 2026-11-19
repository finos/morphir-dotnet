// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Query

open System
open System.Collections.Generic
open System.Collections.Immutable
open Morphir.Bpmn

/// A node of a queried tree: the document, an element, text, a comment or a processing instruction.
[<Sealed>]
type QueryNode
    internal
    (
        ordinal: int,
        depth: int,
        namespaceUri: string,
        localName: string,
        category: string,
        attributes: ImmutableArray<string * string * string>,
        text: string option,
        line: int,
        column: int,
        source: obj
    ) =
    let mutable parent: QueryNode option = None
    let mutable children = ImmutableArray<QueryNode>.Empty

    member internal _.SetChildren(nodes: ImmutableArray<QueryNode>) = children <- nodes
    member internal _.SetParent(owner: QueryNode) = parent <- Some owner

    /// The node's position in preorder, from 0 for the document.
    member _.Ordinal = ordinal

    /// The number of ancestors.
    member _.Depth = depth

    /// An element's namespace URI; empty otherwise.
    member _.NamespaceUri = namespaceUri

    /// An element's local name, or a processing instruction's target; empty otherwise.
    member _.LocalName = localName

    /// The element's kind as `{namespace}local`, or the category of any other node.
    member _.Kind = if localName = "" || category = "processing-instruction" then category else $"{{{namespaceUri}}}{localName}"

    /// The category: `document`, `comment`, `processing-instruction`, `text`, or for an element `activity`, `event`,
    /// `gateway`, `connection`, `data`, `artifact`, `participant`, `lane`, `choreography-activity`, `conversation`,
    /// `conversation-link`, `model`, `diagram-interchange` or `extension`.
    member _.Category = category

    /// The value of the unqualified attribute with this local name, or null.
    member _.Attribute(name: string) : string | null =
        attributes |> Seq.tryFind (fun (ns, local, _) -> ns = "" && local = name) |> Option.map (fun (_, _, value) -> value) |> Option.toObj

    /// The value of the attribute in this namespace with this local name, or null.
    member _.Attribute(namespaceUri: string, name: string) : string | null =
        attributes |> Seq.tryFind (fun (ns, local, _) -> ns = namespaceUri && local = name) |> Option.map (fun (_, _, value) -> value) |> Option.toObj

    member internal _.Attributes = attributes

    /// The `id` attribute, or null.
    member this.Id: string | null = this.Attribute "id"

    /// The text of a text, comment or processing-instruction node, or null.
    member _.Text: string | null = Option.toObj text

    /// The 1-based line where the node starts, or 0 for the document.
    member _.Line = line

    /// The 1-based column where the node starts, or 0 for the document.
    member _.Column = column

    /// The parent node, or null for the document.
    member _.Parent: QueryNode | null = Option.toObj parent

    /// The child nodes in order.
    member _.Children = children

    /// The adapter's own node, such as a `BpmnNode`, or the `BpmnDocument` for the document node.
    member _.Source = source

    override this.ToString() =
        match this.Id with
        | null -> $"#{ordinal} {this.Kind}"
        | id -> $"#{ordinal} {this.Kind} {id}"

/// A tree to query: its nodes in preorder. Only containment connects nodes; references never do.
[<Sealed>]
type QueryTree internal (nodes: ImmutableArray<QueryNode>) =
    /// The document node.
    member _.Root = nodes[0]

    /// Every node in preorder.
    member _.Nodes = nodes

    /// The first node in preorder with this `id`, or null.
    member _.Find(id: string | null) : QueryNode | null =
        nodes |> Seq.tryFind (fun node -> not (isNull id) && node.Id = id) |> Option.toObj

    override _.ToString() = $"Query tree of {nodes.Length} nodes"

/// Builds query trees from BPMN documents.
[<AbstractClass; Sealed>]
type BpmnQueryTree private () =
    static let activities =
        set [ "task"; "userTask"; "serviceTask"; "sendTask"; "receiveTask"; "manualTask"; "businessRuleTask"; "scriptTask"; "callActivity"; "subProcess"; "adHocSubProcess"; "transaction" ]

    static let category (node: BpmnNode) =
        match node.Kind with
        | BpmnNodeKind.Comment -> "comment"
        | BpmnNodeKind.ProcessingInstruction -> "processing-instruction"
        | BpmnNodeKind.Text
        | BpmnNodeKind.CData -> "text"
        | _ when node.NamespaceUri = BpmnNamespaces.Model ->
            match node.LocalName with
            | kind when activities.Contains kind -> "activity"
            | "startEvent" | "endEvent" | "intermediateCatchEvent" | "intermediateThrowEvent" | "boundaryEvent" | "implicitThrowEvent" -> "event"
            | "exclusiveGateway" | "inclusiveGateway" | "parallelGateway" | "complexGateway" | "eventBasedGateway" -> "gateway"
            | "sequenceFlow" | "messageFlow" | "association" | "dataInputAssociation" | "dataOutputAssociation" -> "connection"
            | "dataObject" | "dataObjectReference" | "dataStore" | "dataStoreReference" | "dataInput" | "dataOutput" -> "data"
            | "textAnnotation" | "group" -> "artifact"
            | "participant" -> "participant"
            | "lane" -> "lane"
            | "choreographyTask" | "subChoreography" | "callChoreography" -> "choreography-activity"
            | "conversation" | "subConversation" | "callConversation" -> "conversation"
            | "conversationLink" -> "conversation-link"
            | _ -> "model"
        | _ when node.NamespaceUri = BpmnNamespaces.Di || node.NamespaceUri = BpmnNamespaces.Dc || node.NamespaceUri = BpmnNamespaces.DdDi -> "diagram-interchange"
        | _ -> "extension"

    /// The tree of a document: the document node, then its comments, instructions and root element, each element
    /// with its text, comment and instruction children. White space and the XML declaration are left out.
    static member Create(document: BpmnDocument) =
        ArgumentNullException.ThrowIfNull document
        let all = ResizeArray<QueryNode>()
        let root = QueryNode(0, 0, "", "", "document", ImmutableArray.Empty, None, 0, 0, document :> obj)
        all.Add root
        let kept (node: BpmnNode) = node.Kind <> BpmnNodeKind.Whitespace && node.Kind <> BpmnNodeKind.Declaration
        let made = Dictionary<BpmnNode, QueryNode>(HashIdentity.Reference)
        let stack = Stack<BpmnNode * QueryNode * int>()

        for node in Seq.rev document.Nodes do
            if kept node then stack.Push(node, root, 1)

        let childrenOf = Dictionary<QueryNode, ResizeArray<QueryNode>>(HashIdentity.Reference)
        childrenOf[root] <- ResizeArray()

        while stack.Count > 0 do
            let node, owner, depth = stack.Pop()

            let text =
                match node.Kind with
                | BpmnNodeKind.Element -> None
                | _ -> Some node.Value

            let attributes =
                node.Attributes
                |> Seq.filter (fun attribute -> attribute.NamespaceUri <> "http://www.w3.org/2000/xmlns/")
                |> Seq.map (fun attribute -> attribute.NamespaceUri, attribute.LocalName, attribute.Value)
                |> ImmutableArray.CreateRange

            let created =
                QueryNode(all.Count, depth, node.NamespaceUri, node.LocalName, category node, attributes, text, node.Line, node.Column, node :> obj)

            all.Add created
            made[node] <- created
            childrenOf[owner].Add created
            childrenOf[created] <- ResizeArray()
            created.SetParent owner

            for child in Seq.rev node.Children do
                if kept child then stack.Push(child, created, depth + 1)

        for KeyValue(owner, list) in childrenOf do
            owner.SetChildren(ImmutableArray.CreateRange list)

        QueryTree(ImmutableArray.CreateRange all)
