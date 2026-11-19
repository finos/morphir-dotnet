// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Svg

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text
open System.Xml

/// Why an unqualified SVG name could not be read.
[<RequireQualifiedAccess>]
type SvgNameError =
    /// The name was absent.
    | Missing
    /// The name was not an XML NCName.
    | Invalid

    override this.ToString() =
        match this with
        | SvgNameError.Missing -> "An SVG name is required."
        | SvgNameError.Invalid -> "An SVG name must be an unqualified XML name."

/// An unqualified element or attribute name using ordinal comparison.
[<CustomEquality; CustomComparison>]
type SvgName =
    private
    | SvgName of string

    /// Original spelling.
    member this.Value =
        let (SvgName value) = this
        value

    /// Reads an unqualified XML name. For F# callers.
    static member Parse(text: string | null) =
        match text with
        | null
        | "" -> Error SvgNameError.Missing
        | value ->
            try
                Ok(SvgName(XmlConvert.VerifyNCName value))
            with :? XmlException ->
                Error SvgNameError.Invalid

    /// Reads an unqualified XML name. For C# callers.
    static member TryParse
        (
            text: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<SvgName>,
            [<Out; MaybeNullWhen(true)>] failure: byref<SvgNameError>
        ) =
        match SvgName.Parse text with
        | Ok name ->
            result <- name
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Creates a name from a source-code literal. Throws if the literal is invalid.
    static member Create(literal: string) =
        match SvgName.Parse literal with
        | Ok name -> name
        | Error error -> invalidArg (nameof literal) (error.ToString())

    override this.ToString() = this.Value

    override this.Equals(other: obj | null) =
        match other with
        | :? SvgName as name -> String.Equals(this.Value, name.Value, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() =
        StringComparer.Ordinal.GetHashCode this.Value

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | null -> 1
            | :? SvgName as name -> String.CompareOrdinal(this.Value, name.Value)
            | _ -> invalidArg (nameof other) "Expected an SvgName."

/// An immutable XML attribute in an SVG scene.
[<Sealed>]
type SvgAttribute private (name: SvgName, value: string) =
    /// Unqualified XML name.
    member _.Name = name

    /// Value before XML escaping.
    member _.Value = value

    /// Creates an attribute from an unqualified name and XML text.
    static member Create(name: SvgName, value: string) =
        ArgumentNullException.ThrowIfNull name
        ArgumentNullException.ThrowIfNull value

        if name.Value = "xmlns" then
            invalidArg (nameof name) "SVG namespace declarations are handled by the serializer."

        SvgAttribute(name, XmlConvert.VerifyXmlChars value)

    /// Creates an attribute from a literal name and XML text.
    static member Create(literalName: string, value: string) =
        SvgAttribute.Create(SvgName.Create literalName, value)

    override _.ToString() = name.Value

/// An SVG element or a text child.
[<RequireQualifiedAccess>]
type SvgNode =
    private
    | Element of
        name: SvgName *
        attributes: ImmutableArray<SvgAttribute> *
        children: ImmutableArray<SvgNode>
    | Text of string

    /// Attributes in authored order. Text nodes have no attributes.
    member this.Attributes =
        match this with
        | SvgNode.Element(_, attributes, _) -> attributes
        | SvgNode.Text _ -> ImmutableArray<SvgAttribute>.Empty

    /// Children in paint order.
    member this.Children =
        match this with
        | SvgNode.Element(_, _, children) -> children
        | SvgNode.Text _ -> ImmutableArray<SvgNode>.Empty

    /// Exhaustive dispatch over elements and text for C# callers.
    member this.Match<'T>
        (
            element: Func<SvgName, ImmutableArray<SvgAttribute>, ImmutableArray<SvgNode>, 'T>,
            text: Func<string, 'T>
        ) =
        match this with
        | SvgNode.Element(name, attributes, children) -> element.Invoke(name, attributes, children)
        | SvgNode.Text value -> text.Invoke value

    /// Creates a text node. Text is escaped by the serializer.
    static member CreateText(value: string) =
        ArgumentNullException.ThrowIfNull value
        SvgNode.Text(XmlConvert.VerifyXmlChars value)

    /// Creates an element. Attribute names must be unique. Supplied collections are copied.
    static member CreateElement
        (name: SvgName, attributes: IReadOnlyList<SvgAttribute>, children: IReadOnlyList<SvgNode>)
        =
        ArgumentNullException.ThrowIfNull name
        ArgumentNullException.ThrowIfNull attributes
        ArgumentNullException.ThrowIfNull children
        let attributeValues = ImmutableArray.CreateRange<SvgAttribute> attributes
        let childNodes = ImmutableArray.CreateRange<SvgNode> children
        let names = HashSet<SvgName>()

        for attribute in attributeValues do
            ArgumentNullException.ThrowIfNull attribute

            if not (names.Add attribute.Name) then
                invalidArg (nameof attributes) $"Duplicate SVG attribute '{attribute.Name}'."

        for child in childNodes do
            ArgumentNullException.ThrowIfNull child

        SvgNode.Element(name, attributeValues, childNodes)

    /// Creates an element with a literal name.
    static member CreateElement
        (
            literalName: string,
            attributes: IReadOnlyList<SvgAttribute>,
            children: IReadOnlyList<SvgNode>
        ) =
        SvgNode.CreateElement(SvgName.Create literalName, attributes, children)

    /// Sets an attribute on an element, retaining the positions of existing attributes.
    member this.WithAttribute(attribute: SvgAttribute) =
        ArgumentNullException.ThrowIfNull attribute

        match this with
        | SvgNode.Text _ -> invalidOp "SVG text children do not have attributes."
        | SvgNode.Element(name, attributes, children) ->
            let index =
                attributes |> Seq.tryFindIndex (fun current -> current.Name = attribute.Name)

            let updated =
                match index with
                | Some index -> attributes.SetItem(index, attribute)
                | None -> attributes.Add attribute

            SvgNode.Element(name, updated, children)

    override this.ToString() =
        match this with
        | SvgNode.Element(name, _, _) -> name.Value
        | SvgNode.Text value -> value

type private WriteAction =
    | WriteNode of SvgNode
    | CloseElement

/// An SVG document with one SVG root element.
[<Sealed>]
type SvgDocument private (root: SvgNode) =
    /// The root element and its children.
    member _.Root = root

    /// Creates a document from root attributes and children.
    static member Create
        (attributes: IReadOnlyList<SvgAttribute>, children: IReadOnlyList<SvgNode>)
        =
        SvgDocument(SvgNode.CreateElement("svg", attributes, children))

    /// Serializes this scene into SVG markup with invariant line endings.
    member _.Render() =
        let output = StringBuilder()

        let settings =
            XmlWriterSettings(
                OmitXmlDeclaration = true,
                Indent = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Entitize
            )

        use writer = XmlWriter.Create(output, settings)
        let remaining = Stack<WriteAction>()
        remaining.Push(WriteNode root)

        while remaining.Count > 0 do
            match remaining.Pop() with
            | CloseElement -> writer.WriteEndElement()
            | WriteNode(SvgNode.Text content) -> writer.WriteString content
            | WriteNode(SvgNode.Element(name, attributes, children)) ->
                writer.WriteStartElement(name.Value, "http://www.w3.org/2000/svg")

                for attribute in attributes do
                    writer.WriteAttributeString(attribute.Name.Value, attribute.Value)

                // Mixed content must not acquire indentation before its first child.
                if
                    children
                    |> Seq.exists (
                        function
                        | SvgNode.Text _ -> true
                        | SvgNode.Element _ -> false
                    )
                then
                    writer.WriteString ""

                remaining.Push CloseElement

                for index in children.Length - 1 .. -1 .. 0 do
                    remaining.Push(WriteNode children[index])

        writer.Flush()
        output.ToString()

    override _.ToString() = "SVG document"
