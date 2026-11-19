// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Bpmn

open System
open System.Collections.Immutable

/// The namespaces of BPMN 2.0.2 and its diagram interchange.
[<AbstractClass; Sealed>]
type BpmnNamespaces private () =
    /// The BPMN semantic model.
    static member Model = "http://www.omg.org/spec/BPMN/20100524/MODEL"

    /// BPMN diagram interchange: diagrams, planes, shapes, edges and labels.
    static member Di = "http://www.omg.org/spec/BPMN/20100524/DI"

    /// Diagram definition common types: bounds, points and fonts.
    static member Dc = "http://www.omg.org/spec/DD/20100524/DC"

    /// Diagram definition interchange: waypoints.
    static member DdDi = "http://www.omg.org/spec/DD/20100524/DI"

/// Which part of a document a finding is about.
[<RequireQualifiedAccess>]
type BpmnFindingKind =
    /// The XML itself: well-formedness and prohibited declarations.
    | Xml
    /// The BPMN structure: the root element, required fields and identifiers.
    | Structure
    /// References between elements.
    | Reference

    /// The kind as documentation writes it.
    member this.WireValue =
        match this with
        | BpmnFindingKind.Xml -> "xml"
        | BpmnFindingKind.Structure -> "structure"
        | BpmnFindingKind.Reference -> "reference"

    override this.ToString() = this.WireValue

/// A finding about a BPMN document, at a line and column of its source.
[<Sealed>]
type BpmnDiagnostic internal (code: string, kind: BpmnFindingKind, message: string, line: int, column: int) =
    /// A stable code such as `xml/prohibited-declaration` or `bpmn/duplicate-id`.
    member _.Code = code

    /// Which part of the document the finding is about.
    member _.Kind = kind

    /// What is wrong, in a sentence.
    member _.Message = message

    /// The 1-based line, or 0 when the finding has no place.
    member _.Line = line

    /// The 1-based column, or 0 when the finding has no place.
    member _.Column = column

    override _.ToString() =
        if line > 0 then $"{code} {message} ({line}:{column})" else $"{code} {message}"

/// What a node of the element tree is.
[<RequireQualifiedAccess>]
type BpmnNodeKind =
    /// The `<?xml …?>` declaration.
    | Declaration
    /// An element with its attributes and children.
    | Element
    /// Character data.
    | Text
    /// A CDATA section.
    | CData
    /// A comment.
    | Comment
    /// A processing instruction.
    | ProcessingInstruction
    /// White space between markup.
    | Whitespace

    /// The kind as documentation writes it.
    member this.WireValue =
        match this with
        | BpmnNodeKind.Declaration -> "declaration"
        | BpmnNodeKind.Element -> "element"
        | BpmnNodeKind.Text -> "text"
        | BpmnNodeKind.CData -> "cdata"
        | BpmnNodeKind.Comment -> "comment"
        | BpmnNodeKind.ProcessingInstruction -> "processing-instruction"
        | BpmnNodeKind.Whitespace -> "whitespace"

    override this.ToString() = this.WireValue

/// An attribute as written, with its namespace. Namespace declarations are attributes in the XMLNS namespace.
[<Sealed>]
type BpmnAttribute internal (prefix: string, localName: string, namespaceUri: string, value: string) =
    /// The prefix as written, or empty.
    member _.Prefix = prefix

    /// The local name.
    member _.LocalName = localName

    /// The namespace URI, or empty for an unqualified attribute.
    member _.NamespaceUri = namespaceUri

    /// The value exactly as the XML gives it after entity decoding; numbers keep their text.
    member _.Value = value

    /// The name as written: `prefix:local` or `local`.
    member _.Name = if prefix = "" then localName else $"{prefix}:{localName}"

    override this.ToString() = $"{this.Name}=\"{value}\""

/// A node of the lossless element tree: every element, attribute, text, comment and instruction in source order.
[<Sealed>]
type BpmnNode
    internal
    (
        kind: BpmnNodeKind,
        prefix: string,
        localName: string,
        namespaceUri: string,
        value: string,
        attributes: ImmutableArray<BpmnAttribute>,
        children: ImmutableArray<BpmnNode>,
        line: int,
        column: int
    ) =
    /// What the node is.
    member _.Kind = kind

    /// An element's prefix as written, or empty.
    member _.Prefix = prefix

    /// An element's local name, or a processing instruction's target; empty otherwise.
    member _.LocalName = localName

    /// An element's namespace URI; empty otherwise.
    member _.NamespaceUri = namespaceUri

    /// The text of a text, CDATA, comment, whitespace or declaration node, or an instruction's data.
    member _.Value = value

    /// An element's attributes in source order, namespace declarations included.
    member _.Attributes = attributes

    /// An element's children in source order.
    member _.Children = children

    /// The 1-based line where the node starts.
    member _.Line = line

    /// The 1-based column where the node starts.
    member _.Column = column

    /// An element's name as written.
    member _.Name = if prefix = "" then localName else $"{prefix}:{localName}"

    /// The value of the attribute with this local name and no namespace, or null.
    member _.Attribute(name: string) : string | null =
        attributes
        |> Seq.tryFind (fun attribute -> attribute.LocalName = name && attribute.NamespaceUri = "")
        |> Option.map (fun attribute -> attribute.Value)
        |> Option.toObj

    override this.ToString() =
        match kind with
        | BpmnNodeKind.Element -> $"<{this.Name}>"
        | _ -> $"{kind} {value}"
