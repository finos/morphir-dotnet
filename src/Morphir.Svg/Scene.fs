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

/// Why an SVG name could not be read.
[<RequireQualifiedAccess>]
type SvgNameError =
    /// The name was absent.
    | Missing
    /// The name was not an XML NCName.
    | Invalid
    /// The prefix of a qualified name was absent or not an XML NCName.
    | InvalidPrefix
    /// The prefix was xmlns, the prefix xml named another namespace, or the XML namespace had another prefix.
    | ReservedPrefix
    /// A qualified name had no namespace IRI.
    | MissingNamespace

    override this.ToString() =
        match this with
        | SvgNameError.Missing -> "An SVG name is required."
        | SvgNameError.Invalid -> "An SVG name must be an unqualified XML name."
        | SvgNameError.InvalidPrefix -> "A namespace prefix must be an XML NCName."
        | SvgNameError.ReservedPrefix ->
            "The prefix xmlns is reserved, and the prefix xml is bound only to the XML namespace."
        | SvgNameError.MissingNamespace -> "A qualified name needs a namespace IRI."

/// An element or attribute name using ordinal comparison. An unqualified name is in the SVG
/// namespace for elements and in no namespace for attributes. A qualified name carries a prefix
/// and a namespace IRI.
[<CustomEquality; CustomComparison>]
type SvgName =
    private
    | SvgName of prefix: string * local: string * namespaceUri: string

    static let ncName (text: string) =
        try
            Some(XmlConvert.VerifyNCName text)
        with :? XmlException ->
            None

    static let xmlNamespace = "http://www.w3.org/XML/1998/namespace"
    static let xmlnsNamespace = "http://www.w3.org/2000/xmlns/"

    /// The name as written: the local name, or prefix:local for a qualified name.
    member this.Value =
        let (SvgName(prefix, local, _)) = this
        if prefix = "" then local else prefix + ":" + local

    /// The local part of the name.
    member this.LocalName =
        let (SvgName(_, local, _)) = this
        local

    /// The prefix of a qualified name, or null for an unqualified name.
    member this.Prefix: string | null =
        let (SvgName(prefix, _, _)) = this
        if prefix = "" then null else prefix

    /// The namespace IRI of a qualified name, or null for an unqualified name.
    member this.NamespaceUri: string | null =
        let (SvgName(_, _, namespaceUri)) = this
        if namespaceUri = "" then null else namespaceUri

    /// Whether the name has a prefix and a namespace.
    member this.IsQualified = this.Prefix <> null

    member internal this.Parts =
        let (SvgName(prefix, local, namespaceUri)) = this
        prefix, local, namespaceUri

    /// Reads an unqualified XML name. For F# callers.
    static member Parse(text: string | null) =
        match text with
        | null
        | "" -> Error SvgNameError.Missing
        | value ->
            match ncName value with
            | Some name -> Ok(SvgName("", name, ""))
            | None -> Error SvgNameError.Invalid

    /// Reads a name qualified by a prefix and a namespace IRI. For F# callers.
    static member ParseQualified(prefix: string | null, localName: string | null, namespaceUri: string | null) =
        match localName, prefix, namespaceUri with
        | (null | ""), _, _ -> Error SvgNameError.Missing
        | local, _, _ when (ncName local).IsNone -> Error SvgNameError.Invalid
        | _, (null | ""), _ -> Error SvgNameError.InvalidPrefix
        | _, prefix, _ when (ncName prefix).IsNone -> Error SvgNameError.InvalidPrefix
        | _, _, (null | "") -> Error SvgNameError.MissingNamespace
        | local, prefix, namespaceUri ->
            if prefix.Equals("xmlns", StringComparison.Ordinal) || namespaceUri = xmlnsNamespace then
                Error SvgNameError.ReservedPrefix
            elif (prefix = "xml") <> (namespaceUri = xmlNamespace) then
                Error SvgNameError.ReservedPrefix
            else
                Ok(SvgName(prefix, local, namespaceUri))

    /// Reads a qualified name. For C# callers.
    static member TryParseQualified
        (
            prefix: string | null,
            localName: string | null,
            namespaceUri: string | null,
            [<Out; MaybeNullWhen(false)>] result: byref<SvgName>,
            [<Out; MaybeNullWhen(true)>] failure: byref<SvgNameError>
        ) =
        match SvgName.ParseQualified(prefix, localName, namespaceUri) with
        | Ok name ->
            result <- name
            failure <- Unchecked.defaultof<_>
            true
        | Error error ->
            result <- Unchecked.defaultof<_>
            failure <- error
            false

    /// Creates a qualified name from source-code literals. Throws if they are invalid.
    static member Qualified(prefix: string, localName: string, namespaceUri: string) =
        match SvgName.ParseQualified(prefix, localName, namespaceUri) with
        | Ok name -> name
        | Error error ->
            let parameter =
                match error with
                | SvgNameError.Missing
                | SvgNameError.Invalid -> nameof localName
                | SvgNameError.MissingNamespace -> nameof namespaceUri
                | SvgNameError.InvalidPrefix
                | SvgNameError.ReservedPrefix -> nameof prefix

            invalidArg parameter (error.ToString())

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
        | :? SvgName as name -> this.Parts = name.Parts
        | _ -> false

    override this.GetHashCode() =
        let prefix, local, namespaceUri = this.Parts
        HashCode.Combine(StringComparer.Ordinal.GetHashCode prefix, StringComparer.Ordinal.GetHashCode local, StringComparer.Ordinal.GetHashCode namespaceUri)

    interface IComparable with
        member this.CompareTo(other: obj | null) =
            match other with
            | null -> 1
            | :? SvgName as name ->
                let byValue = String.CompareOrdinal(this.Value, name.Value)
                if byValue <> 0 then byValue
                else String.CompareOrdinal(this.NamespaceUri, name.NamespaceUri)
            | _ -> invalidArg (nameof other) "Expected an SvgName."

/// An immutable XML attribute in an SVG scene.
[<Sealed>]
type SvgAttribute private (name: SvgName, value: string) =
    /// The attribute name: unqualified (no namespace) or qualified by a prefix and namespace.
    member _.Name = name

    /// Value before XML escaping.
    member _.Value = value

    /// Creates an attribute from an unqualified or qualified name and XML text.
    static member Create(name: SvgName, value: string) =
        ArgumentNullException.ThrowIfNull name
        ArgumentNullException.ThrowIfNull value

        if not name.IsQualified && name.Value = "xmlns" then
            invalidArg (nameof name) "SVG namespace declarations are handled by the serializer."

        SvgAttribute(name, XmlConvert.VerifyXmlChars value)

    /// Creates an attribute from a literal name and XML text.
    static member Create(literalName: string, value: string) =
        SvgAttribute.Create(SvgName.Create literalName, value)

    override _.ToString() = name.Value

/// Why an element could not be created.
[<RequireQualifiedAccess; CustomEquality; NoComparison>]
type SvgSceneError =
    /// Two attributes have the same namespace and local name.
    | DuplicateAttribute of SvgName
    /// One namespace is bound to several prefixes at one element.
    | PrefixesForNamespace of namespaceUri: string * prefixes: ImmutableArray<string>
    /// One prefix is bound to several namespaces at one element.
    | NamespacesForPrefix of prefix: string * namespaces: ImmutableArray<string>

    /// For C# callers: every case needs a function.
    member this.Match
        (
            duplicateAttribute: Func<SvgName, 'T>,
            prefixesForNamespace: Func<string, ImmutableArray<string>, 'T>,
            namespacesForPrefix: Func<string, ImmutableArray<string>, 'T>
        ) =
        match this with
        | SvgSceneError.DuplicateAttribute name -> duplicateAttribute.Invoke name
        | SvgSceneError.PrefixesForNamespace(namespaceUri, prefixes) -> prefixesForNamespace.Invoke(namespaceUri, prefixes)
        | SvgSceneError.NamespacesForPrefix(prefix, namespaces) -> namespacesForPrefix.Invoke(prefix, namespaces)

    override this.ToString() =
        let quoted (values: ImmutableArray<string>) = values |> Seq.map (fun value -> $"'{value}'") |> String.concat " and "

        match this with
        | SvgSceneError.DuplicateAttribute name -> $"Duplicate SVG attribute '{name.Value}'."
        | SvgSceneError.PrefixesForNamespace(namespaceUri, prefixes) ->
            $"The namespace '{namespaceUri}' is bound to the prefixes {quoted prefixes} at one element."
        | SvgSceneError.NamespacesForPrefix(prefix, namespaces) ->
            $"The prefix '{prefix}' is bound to the namespaces {quoted namespaces} at one element."

    override this.Equals(other: obj | null) =
        match this, other with
        | SvgSceneError.DuplicateAttribute left, (:? SvgSceneError as SvgSceneError.DuplicateAttribute right) -> left.Equals right
        | SvgSceneError.PrefixesForNamespace(leftNamespace, left), (:? SvgSceneError as SvgSceneError.PrefixesForNamespace(rightNamespace, right)) ->
            leftNamespace = rightNamespace && Seq.forall2 (=) left right && left.Length = right.Length
        | SvgSceneError.NamespacesForPrefix(leftPrefix, left), (:? SvgSceneError as SvgSceneError.NamespacesForPrefix(rightPrefix, right)) ->
            leftPrefix = rightPrefix && left.Length = right.Length && Seq.forall2 (=) left right
        | _ -> false

    override this.GetHashCode() =
        match this with
        | SvgSceneError.DuplicateAttribute name -> name.GetHashCode()
        | SvgSceneError.PrefixesForNamespace(namespaceUri, prefixes) -> HashCode.Combine(namespaceUri, prefixes.Length)
        | SvgSceneError.NamespacesForPrefix(prefix, namespaces) -> HashCode.Combine(prefix, namespaces.Length)

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

    /// Creates an element, or returns why it cannot: duplicate attributes, or a namespace with two
    /// prefixes or a prefix with two namespaces at this element. Collections are copied. For F# callers.
    static member ValidateElement
        (name: SvgName, attributes: IReadOnlyList<SvgAttribute>, children: IReadOnlyList<SvgNode>)
        : Result<SvgNode, SvgSceneError> =
        ArgumentNullException.ThrowIfNull name
        ArgumentNullException.ThrowIfNull attributes
        ArgumentNullException.ThrowIfNull children
        let attributeValues = ImmutableArray.CreateRange<SvgAttribute> attributes
        let childNodes = ImmutableArray.CreateRange<SvgNode> children

        for attribute in attributeValues do
            ArgumentNullException.ThrowIfNull attribute

        for child in childNodes do
            ArgumentNullException.ThrowIfNull child

        let expanded (attribute: SvgAttribute) =
            let _, local, namespaceUri = attribute.Name.Parts
            namespaceUri, local

        let duplicate =
            attributeValues
            |> Seq.groupBy expanded
            |> Seq.tryPick (fun (_, group) -> if Seq.length group > 1 then Some (Seq.head group).Name else None)

        let bindings =
            [ yield name.Parts
              for attribute in attributeValues do
                  yield attribute.Name.Parts ]
            |> List.choose (fun (prefix, _, namespaceUri) -> if prefix = "" then None else Some(prefix, namespaceUri))
            |> List.distinct

        let sorted values = values |> Seq.distinct |> Seq.sortWith (fun left right -> String.CompareOrdinal(left, right)) |> ImmutableArray.CreateRange

        let prefixesForNamespace =
            bindings
            |> List.groupBy snd
            |> List.tryPick (fun (namespaceUri, group) ->
                if group.Length > 1 then Some(SvgSceneError.PrefixesForNamespace(namespaceUri, sorted (List.map fst group))) else None)

        let namespacesForPrefix =
            bindings
            |> List.groupBy fst
            |> List.tryPick (fun (prefix, group) ->
                if group.Length > 1 then Some(SvgSceneError.NamespacesForPrefix(prefix, sorted (List.map snd group))) else None)

        match duplicate, prefixesForNamespace, namespacesForPrefix with
        | Some attribute, _, _ -> Error(SvgSceneError.DuplicateAttribute attribute)
        | None, Some error, _
        | None, None, Some error -> Error error
        | None, None, None -> Ok(SvgNode.Element(name, attributeValues, childNodes))

    /// Creates an element, or returns why it cannot. For C# callers.
    static member TryCreateElement
        (
            name: SvgName,
            attributes: IReadOnlyList<SvgAttribute>,
            children: IReadOnlyList<SvgNode>,
            [<Out; MaybeNullWhen(false)>] element: byref<SvgNode>,
            [<Out; MaybeNullWhen(true)>] error: byref<SvgSceneError>
        ) =
        match SvgNode.ValidateElement(name, attributes, children) with
        | Ok node ->
            element <- node
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            element <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Creates an element. Throws when ValidateElement would return an error. Supplied collections are copied.
    static member CreateElement
        (name: SvgName, attributes: IReadOnlyList<SvgAttribute>, children: IReadOnlyList<SvgNode>)
        =
        match SvgNode.ValidateElement(name, attributes, children) with
        | Ok node -> node
        | Error error ->
            let parameter =
                match error with
                | SvgSceneError.DuplicateAttribute _ -> nameof attributes
                | SvgSceneError.PrefixesForNamespace _
                | SvgSceneError.NamespacesForPrefix _ when not name.IsQualified -> nameof attributes
                | _ -> nameof name

            invalidArg parameter (error.ToString())

    /// Creates an element with a literal name.
    static member CreateElement
        (
            literalName: string,
            attributes: IReadOnlyList<SvgAttribute>,
            children: IReadOnlyList<SvgNode>
        ) =
        SvgNode.CreateElement(SvgName.Create literalName, attributes, children)

    /// Sets an attribute on an element, retaining the positions of existing attributes.
    /// Sets an attribute on an element, replacing the attribute with the same namespace and local
    /// name in its position. Throws when the result would fail ValidateElement.
    member this.WithAttribute(attribute: SvgAttribute) =
        ArgumentNullException.ThrowIfNull attribute

        match this with
        | SvgNode.Text _ -> invalidOp "SVG text children do not have attributes."
        | SvgNode.Element(name, attributes, children) ->
            let expanded (current: SvgAttribute) =
                let _, local, namespaceUri = current.Name.Parts
                namespaceUri, local

            let index =
                attributes |> Seq.tryFindIndex (fun current -> expanded current = expanded attribute)

            let updated =
                match index with
                | Some index -> attributes.SetItem(index, attribute)
                | None -> attributes.Add attribute

            match SvgNode.ValidateElement(name, updated, children) with
            | Ok node -> node
            | Error error -> invalidArg (nameof attribute) (error.ToString())

    override this.ToString() =
        match this with
        | SvgNode.Element(name, _, _) -> name.Value
        | SvgNode.Text value -> value

type private WriteAction =
    | WriteNode of node: SvgNode * insideForeign: bool
    | CloseElement

module private Bindings =
    /// Every prefix binding that qualified names in a subtree use, except the xml prefix.
    let rec collect (node: SvgNode) (found: Dictionary<string, string>) =
        match node with
        | SvgNode.Text _ -> ()
        | SvgNode.Element(name, attributes, children) ->
            let add (prefix, _, namespaceUri) =
                if prefix <> "" && prefix <> "xml" && not (found.ContainsKey prefix) then found.Add(prefix, namespaceUri)

            add name.Parts

            for attribute in attributes do
                add attribute.Name.Parts

            for child in children do
                collect child found

/// An SVG document with one SVG root element and an optional metadata element.
[<Sealed>]
type SvgDocument private (root: SvgNode, metadata: SvgNode option) =
    /// The root element and its children. The metadata element, when present, is the first child.
    member _.Root =
        match metadata, root with
        | Some element, SvgNode.Element(name, attributes, children) -> SvgNode.Element(name, attributes, children.Insert(0, element))
        | _ -> root

    /// Whether the document carries a metadata element.
    member _.HasMetadata = metadata.IsSome

    /// The metadata element, or null when the document has none.
    member _.Metadata: SvgNode | null = Option.toObj metadata

    /// A copy whose metadata element holds the content: foreign elements, text, or both. The
    /// metadata element is written as the root's first child.
    member _.WithMetadata(content: IReadOnlyList<SvgNode>) =
        ArgumentNullException.ThrowIfNull content
        SvgDocument(root, Some(SvgNode.CreateElement(SvgName.Create "metadata", [||], content)))

    /// Creates a document from root attributes and children.
    static member Create
        (attributes: IReadOnlyList<SvgAttribute>, children: IReadOnlyList<SvgNode>)
        =
        SvgDocument(SvgNode.CreateElement("svg", attributes, children), None)

    /// Serializes this scene into SVG markup with invariant line endings. Each namespace is
    /// declared once, at the outermost element that uses it.
    member this.Render() =
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
        remaining.Push(WriteNode(this.Root, false))

        while remaining.Count > 0 do
            match remaining.Pop() with
            | CloseElement -> writer.WriteEndElement()
            | WriteNode(SvgNode.Text content, _) -> writer.WriteString content
            | WriteNode(SvgNode.Element(name, attributes, children) as element, insideForeign) ->
                match name.Parts with
                | "", local, _ -> writer.WriteStartElement(local, "http://www.w3.org/2000/svg")
                | prefix, local, namespaceUri -> writer.WriteStartElement(prefix, local, namespaceUri)

                // The outermost element of foreign content declares every prefix its subtree uses, so
                // siblings below it share one declaration.
                let foreign = insideForeign || name.IsQualified

                if name.IsQualified && not insideForeign then
                    let bindings = Dictionary<string, string>(StringComparer.Ordinal)
                    Bindings.collect element bindings

                    for pair in bindings |> Seq.sortBy (fun pair -> pair.Key) do
                        if writer.LookupPrefix pair.Value <> pair.Key then
                            writer.WriteAttributeString("xmlns", pair.Key, "http://www.w3.org/2000/xmlns/", pair.Value)

                for attribute in attributes do
                    match attribute.Name.Parts with
                    | "", local, _ -> writer.WriteAttributeString(local, attribute.Value)
                    | prefix, local, namespaceUri -> writer.WriteAttributeString(prefix, local, namespaceUri, attribute.Value)

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
                    remaining.Push(WriteNode(children[index], foreign))

        writer.Flush()
        output.ToString()

    override _.ToString() = "SVG document"
