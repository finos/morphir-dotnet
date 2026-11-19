// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Bpmn

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Runtime.InteropServices
open System.Diagnostics.CodeAnalysis

/// The typed view of one element: its BPMN kind, identity and fields, over the node that keeps everything as written.
[<Sealed>]
type BpmnElement internal (node: BpmnNode, ordinal: int, parent: BpmnElement option) =
    let mutable children = ImmutableArray<BpmnElement>.Empty

    member internal _.SetChildren(value: ImmutableArray<BpmnElement>) = children <- value

    /// The node that keeps the element exactly as written.
    member _.Node = node

    /// The element's position in document order, from 0.
    member _.Ordinal = ordinal

    /// The parent element, or null for the root.
    member _.Parent: BpmnElement | null = Option.toObj parent

    member internal _.ParentOption = parent

    /// Child elements in source order.
    member _.Children = children

    /// The local name, such as `task`, `sequenceFlow`, `BPMNShape` or `Bounds`.
    member _.Kind = node.LocalName

    /// The element's namespace URI.
    member _.NamespaceUri = node.NamespaceUri

    /// Whether the element is in the BPMN semantic model namespace.
    member _.IsModel = node.NamespaceUri = BpmnNamespaces.Model

    /// The `id` attribute, or null.
    member _.Id: string | null = node.Attribute "id"

    /// The `name` attribute, or null.
    member _.Name: string | null = node.Attribute "name"

    /// An unqualified attribute's value, or else the text of the first child element with that local name, or null.
    /// Values keep their text: `1e24` stays `1e24`.
    member _.Field(name: string) : string | null =
        match node.Attribute name with
        | null ->
            children
            |> Seq.tryFind (fun child -> child.Kind = name)
            |> Option.map (fun child -> child.Text)
            |> Option.toObj
        | value -> value

    /// The identifiers that a reference field names: the attribute's space-separated values, or the text of each child
    /// element with that local name, such as `flowNodeRef`, `incoming` or a data association's `sourceRef`.
    member _.References(name: string) : ImmutableArray<string> =
        match node.Attribute name with
        | null ->
            children
            |> Seq.filter (fun child -> child.Kind = name)
            |> Seq.map (fun (child: BpmnElement) -> (child.Text: string).Trim())
            |> ImmutableArray.CreateRange
        | value -> value.Split([| ' '; '\t'; '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries) |> ImmutableArray.CreateRange

    /// The element's own text and CDATA, such as an expression's body.
    member _.Text: string =
        node.Children
        |> Seq.filter (fun child -> child.Kind = BpmnNodeKind.Text || child.Kind = BpmnNodeKind.CData || child.Kind = BpmnNodeKind.Whitespace)
        |> Seq.map (fun child -> child.Value)
        |> String.concat ""

    /// The text of the first `documentation` child, or null.
    member this.Documentation: string | null = this.Field "documentation"

    override _.ToString() =
        match node.Attribute "id" with
        | null -> node.Name
        | id -> $"{node.Name} {id}"

/// The result of looking up an identifier.
[<RequireQualifiedAccess>]
type BpmnLookup =
    /// One element has the identifier.
    | Found of BpmnElement
    /// No element has it.
    | Missing
    /// Several elements share it.
    | Ambiguous of ImmutableArray<BpmnElement>

    /// Calls the function for the case, for C# callers.
    member this.Match<'T>(found: Func<BpmnElement, 'T>, missing: Func<'T>, ambiguous: Func<ImmutableArray<BpmnElement>, 'T>) =
        match this with
        | BpmnLookup.Found element -> found.Invoke element
        | BpmnLookup.Missing -> missing.Invoke()
        | BpmnLookup.Ambiguous elements -> ambiguous.Invoke elements

    override this.ToString() =
        match this with
        | BpmnLookup.Found element -> $"Found {element}"
        | BpmnLookup.Missing -> "Missing"
        | BpmnLookup.Ambiguous elements -> $"Ambiguous among {elements.Length}"

/// A BPMN 2.0.2 document: the source, the lossless node tree and the typed element view.
[<Sealed>]
type BpmnDocument private (source: string, nodes: ImmutableArray<BpmnNode>) =
    static let flowNodes =
        set
            [
                "task"; "userTask"; "serviceTask"; "sendTask"; "receiveTask"; "manualTask"; "businessRuleTask"; "scriptTask"
                "subProcess"; "adHocSubProcess"; "transaction"; "callActivity"
                "startEvent"; "endEvent"; "intermediateCatchEvent"; "intermediateThrowEvent"; "boundaryEvent"; "implicitThrowEvent"
                "exclusiveGateway"; "inclusiveGateway"; "parallelGateway"; "complexGateway"; "eventBasedGateway"
                "choreographyTask"; "subChoreography"; "callChoreography"
            ]

    static let activities =
        set [ "task"; "userTask"; "serviceTask"; "sendTask"; "receiveTask"; "manualTask"; "businessRuleTask"; "scriptTask"; "subProcess"; "adHocSubProcess"; "transaction"; "callActivity" ]

    /// Reference fields that validation resolves, with the kinds a target may have (empty for any kind).
    static let referenceFields =
        [
            "sourceRef", Set.empty
            "targetRef", Set.empty
            "attachedToRef", activities
            "processRef", set [ "process" ]
            "bpmnElement", Set.empty
            "default", set [ "sequenceFlow" ]
            "flowNodeRef", Set.empty
            "messageRef", set [ "message" ]
            "errorRef", set [ "error" ]
            "signalRef", set [ "signal" ]
            "escalationRef", set [ "escalation" ]
            "dataObjectRef", set [ "dataObject" ]
            "dataStoreRef", set [ "dataStore" ]
            "categoryValueRef", set [ "categoryValue" ]
        ]

    let elements =
        lazy
            (let all = ResizeArray<BpmnElement>()
             let byNode = Dictionary<BpmnNode, BpmnElement>(HashIdentity.Reference)
             let stack = Stack<BpmnNode * BpmnElement option>()

             for node in Seq.rev nodes do
                 if node.Kind = BpmnNodeKind.Element then stack.Push(node, None)

             while stack.Count > 0 do
                 let node, parent = stack.Pop()
                 let element = BpmnElement(node, all.Count, parent)
                 all.Add element
                 byNode[node] <- element

                 for child in Seq.rev node.Children do
                     if child.Kind = BpmnNodeKind.Element then stack.Push(child, Some element)

             for element in all do
                 element.SetChildren(
                     element.Node.Children
                     |> Seq.filter (fun child -> child.Kind = BpmnNodeKind.Element)
                     |> Seq.map (fun child -> byNode[child])
                     |> ImmutableArray.CreateRange
                 )

             ImmutableArray.CreateRange all)

    let byId =
        lazy
            (let found = Dictionary<string, ResizeArray<BpmnElement>>(StringComparer.Ordinal)

             for element in elements.Value do
                 match element.Id with
                 | null -> ()
                 | id ->
                     match found.TryGetValue id with
                     | true, list -> list.Add element
                     | false, _ -> found[id] <- ResizeArray [ element ]

             found)

    let ofKind (kind: string) =
        elements.Value |> Seq.filter (fun element -> element.IsModel && element.Kind = kind) |> ImmutableArray.CreateRange

    /// Reads BPMN XML. DTDs and entity declarations are refused before parsing, and no resolver is used. Returns a
    /// Result for F# callers.
    static member Read(source: string | null) : Result<BpmnDocument, ImmutableArray<BpmnDiagnostic>> =
        match source with
        | null -> Error(ImmutableArray.Create(BpmnDiagnostic("xml/missing-source", BpmnFindingKind.Xml, "No source was given.", 0, 0)))
        | source ->
            match XmlTree.read source with
            | Error diagnostic -> Error(ImmutableArray.Create diagnostic)
            | Ok nodes ->
                let document = BpmnDocument(source, nodes)

                match document.Root with
                | null -> Error(ImmutableArray.Create(BpmnDiagnostic("xml/malformed", BpmnFindingKind.Xml, "The document has no root element.", 0, 0)))
                | _ -> Ok document

    /// Reads BPMN XML for C# callers: true with the document, or false with the diagnostics.
    static member TryRead
        (
            source: string | null,
            [<Out; MaybeNullWhen(false)>] document: byref<BpmnDocument>,
            [<Out>] diagnostics: byref<ImmutableArray<BpmnDiagnostic>>
        ) =
        match BpmnDocument.Read source with
        | Ok read ->
            document <- read
            diagnostics <- ImmutableArray.Empty
            true
        | Error found ->
            document <- Unchecked.defaultof<_>
            diagnostics <- found
            false

    /// The source exactly as read.
    member _.Source = source

    /// The top-level nodes in order: the declaration, comments, instructions and the root element.
    member _.Nodes = nodes

    /// The root element, normally `definitions`.
    member _.Root: BpmnElement | null =
        if elements.Value.IsEmpty then null else elements.Value[0]

    /// Every element in document order.
    member _.Elements = elements.Value

    /// The `process` elements in document order.
    member _.Processes = ofKind "process"

    /// The `collaboration` elements in document order.
    member _.Collaborations = ofKind "collaboration"

    /// The `choreography` elements in document order.
    member _.Choreographies = ofKind "choreography"

    /// The `BPMNPlane` elements of the diagram interchange, in document order.
    member _.Planes =
        elements.Value |> Seq.filter (fun element -> element.NamespaceUri = BpmnNamespaces.Di && element.Kind = "BPMNPlane") |> ImmutableArray.CreateRange

    /// Looks up an identifier: one element, none, or an ambiguity between elements that share it.
    member _.Find(id: string | null) =
        match id with
        | null -> BpmnLookup.Missing
        | id ->
            match byId.Value.TryGetValue id with
            | true, list when list.Count = 1 -> BpmnLookup.Found list[0]
            | true, list -> BpmnLookup.Ambiguous(ImmutableArray.CreateRange list)
            | false, _ -> BpmnLookup.Missing

    member internal this.Resolve(value: string) =
        match this.Find value with
        | BpmnLookup.Missing when value.Contains ':' -> this.Find(value.Substring(value.LastIndexOf ':' + 1))
        | found -> found

    /// Structure and reference findings: the root element, duplicate identifiers, missing required fields, and
    /// references that are unresolved, ambiguous or name the wrong kind of element. References keep their values.
    member this.Validate() =
        let findings = ResizeArray<BpmnDiagnostic>()
        let at (element: BpmnElement) code kind message = findings.Add(BpmnDiagnostic(code, kind, message, element.Node.Line, element.Node.Column))

        match this.Root with
        | null -> ()
        | root ->
            if not (root.IsModel && root.Kind = "definitions") then
                at root "bpmn/not-definitions" BpmnFindingKind.Structure "The root element is not BPMN definitions."
            elif isNull (root.Node.Attribute "targetNamespace") then
                at root "bpmn/missing-field" BpmnFindingKind.Structure "The definitions have no targetNamespace."

        for KeyValue(id, list) in byId.Value do
            for duplicate in Seq.skip 1 list do
                at duplicate "bpmn/duplicate-id" BpmnFindingKind.Structure $"Duplicate ID: {id}"

        for element in elements.Value do
            if element.IsModel then
                if (flowNodes.Contains element.Kind || element.Kind = "sequenceFlow" || element.Kind = "messageFlow" || element.Kind = "process") && isNull element.Id then
                    at element "bpmn/missing-field" BpmnFindingKind.Structure $"A {element.Kind} has no id."

                let required =
                    match element.Kind with
                    | "sequenceFlow"
                    | "messageFlow"
                    | "association" -> [ "sourceRef"; "targetRef" ]
                    | "boundaryEvent" -> [ "attachedToRef" ]
                    | _ -> []

                for field in required do
                    if element.References(field).IsEmpty then
                        at element "bpmn/missing-field" BpmnFindingKind.Structure $"The {element.Kind} {element.Id} has no {field}."

            for field, kinds in referenceFields do
                // A data association names its source and target in child elements of the same names.
                for value in element.References field do
                    match this.Resolve value with
                    | BpmnLookup.Missing -> at element "bpmn/unresolved-reference" BpmnFindingKind.Reference $"{field} {value} of {element.Id} names no element."
                    | BpmnLookup.Ambiguous _ -> at element "bpmn/ambiguous-reference" BpmnFindingKind.Reference $"Reference target is ambiguous on {element.Id}: {value}"
                    | BpmnLookup.Found target ->
                        let flowEnd = element.IsModel && element.Kind = "sequenceFlow" && (field = "sourceRef" || field = "targetRef")

                        if (not kinds.IsEmpty && not (kinds.Contains target.Kind)) || (flowEnd && not (flowNodes.Contains target.Kind)) then
                            at element "bpmn/wrong-reference-target" BpmnFindingKind.Reference $"{field} of {element.Id} names the {target.Kind} {value}."

        findings |> Seq.sortBy (fun finding -> finding.Line, finding.Column) |> ImmutableArray.CreateRange

    /// Writes the document in a format. Order, identifiers, references, extensions and decimal text are kept.
    member _.Write(format: BpmnWriteFormat) =
        ArgumentNullException.ThrowIfNull format
        XmlWriting.write format nodes

    override this.ToString() =
        match this.Root with
        | null -> "Empty BPMN document"
        | root -> $"BPMN document {root}, {elements.Value.Length} elements"
