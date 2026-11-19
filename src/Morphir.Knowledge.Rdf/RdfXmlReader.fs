// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Rdf

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq
open Morphir.Knowledge

/// The kind of problem encountered while reading RDF/XML.
[<RequireQualifiedAccess>]
type RdfXmlReadErrorKind =
    /// The input declares a document type, which the reader refuses.
    | DocumentTypeDeclaration
    /// The input is not well-formed XML.
    | MalformedXml
    /// The XML does not follow the RDF/XML grammar.
    | InvalidSyntax
    /// A reference does not resolve to an absolute IRI.
    | InvalidIri
    /// A literal or its language tag is not valid.
    | InvalidLiteral

    /// A stable diagnostic code.
    member this.Code =
        match this with
        | RdfXmlReadErrorKind.DocumentTypeDeclaration -> "rdfxml.doctype"
        | RdfXmlReadErrorKind.MalformedXml -> "rdfxml.malformed-xml"
        | RdfXmlReadErrorKind.InvalidSyntax -> "rdfxml.syntax"
        | RdfXmlReadErrorKind.InvalidIri -> "rdfxml.invalid-iri"
        | RdfXmlReadErrorKind.InvalidLiteral -> "rdfxml.invalid-literal"

    override this.ToString() = this.Code

/// A one-based line and column in XML input.
[<Struct>]
type XmlTextLocation =
    { /// The one-based line.
      Line: int
      /// The one-based column.
      Column: int }

    override this.ToString() = $"{this.Line}:{this.Column}"

/// An RDF/XML diagnostic, with its location when the XML reader knows it.
type RdfXmlReadError =
    { /// The kind of problem.
      Kind: RdfXmlReadErrorKind
      /// An explanation of the problem.
      Message: string
      /// The line and column, for F# callers.
      Location: XmlTextLocation voption }

    /// Gets the location if present. For C# callers.
    member this.TryGetLocation([<Out>] location: byref<XmlTextLocation>) =
        match this.Location with
        | ValueSome value ->
            location <- value
            true
        | ValueNone ->
            location <- Unchecked.defaultof<_>
            false

    override this.ToString() =
        match this.Location with
        | ValueSome location -> $"{this.Kind.Code} at {location}: {this.Message}"
        | ValueNone -> $"{this.Kind.Code}: {this.Message}"

/// RFC 3986 reference resolution, without the normalization that System.Uri applies.
module internal IriReferences =
    let private parts =
        Regex(@"\A(?:([^:/?#]+):)?(?://([^/?#]*))?([^?#]*)(?:\?([^#]*))?(?:#(.*))?\z", RegexOptions.CultureInvariant ||| RegexOptions.Singleline)

    type private Reference =
        { Scheme: string option
          Authority: string option
          Path: string
          Query: string option
          Fragment: string option }

    let private parse text =
        let found = parts.Match text
        let group (index: int) = if found.Groups[index].Success then Some found.Groups[index].Value else None

        { Scheme = group 1
          Authority = group 2
          Path = found.Groups[3].Value
          Query = group 4
          Fragment = group 5 }

    let private removeDotSegments (path: string) =
        let output = ResizeArray<string>()
        let mutable input = path

        while input.Length > 0 do
            if input.StartsWith "../" then input <- input.Substring 3
            elif input.StartsWith "./" then input <- input.Substring 2
            elif input.StartsWith "/./" then input <- input.Substring 2
            elif input = "/." then input <- "/"
            elif input.StartsWith "/../" then
                input <- input.Substring 3
                if output.Count > 0 then output.RemoveAt(output.Count - 1)
            elif input = "/.." then
                input <- "/"
                if output.Count > 0 then output.RemoveAt(output.Count - 1)
            elif input = "." || input = ".." then input <- ""
            else
                let start = if input.StartsWith "/" then 1 else 0
                let next = input.IndexOf('/', start)
                let segment = if next < 0 then input else input.Substring(0, next)
                output.Add segment
                input <- if next < 0 then "" else input.Substring next

        String.Concat output

    let private recompose (reference: Reference) =
        let builder = StringBuilder()
        reference.Scheme |> Option.iter (fun scheme -> builder.Append(scheme).Append(':') |> ignore)
        reference.Authority |> Option.iter (fun authority -> builder.Append("//").Append(authority) |> ignore)
        builder.Append reference.Path |> ignore
        reference.Query |> Option.iter (fun query -> builder.Append('?').Append(query) |> ignore)
        reference.Fragment |> Option.iter (fun fragment -> builder.Append('#').Append(fragment) |> ignore)
        builder.ToString()

    /// Resolves a reference against an absolute base IRI, or returns None when the base is missing.
    let resolve (baseIri: string option) (reference: string) =
        let r = parse reference

        if r.Scheme.IsSome then
            Some(recompose { r with Path = removeDotSegments r.Path })
        else
            match baseIri with
            | None -> None
            | Some baseText ->
                let b = parse baseText

                let target =
                    if r.Authority.IsSome then
                        { r with Scheme = b.Scheme; Path = removeDotSegments r.Path }
                    elif r.Path = "" then
                        { b with Query = (if r.Query.IsSome then r.Query else b.Query); Fragment = r.Fragment }
                    elif r.Path.StartsWith "/" then
                        { r with Scheme = b.Scheme; Authority = b.Authority; Path = removeDotSegments r.Path }
                    else
                        let merged =
                            if b.Authority.IsSome && b.Path = "" then "/" + r.Path
                            else
                                let last = b.Path.LastIndexOf '/'
                                (if last < 0 then "" else b.Path.Substring(0, last + 1)) + r.Path

                        { r with Scheme = b.Scheme; Authority = b.Authority; Path = removeDotSegments merged }

                Some(recompose target)

    /// The IRI without its fragment.
    let withoutFragment (iri: string) =
        let hash = iri.IndexOf '#'
        if hash < 0 then iri else iri.Substring(0, hash)

exception internal RdfXmlFailure of RdfXmlReadError

/// The RDF 1.1 XML Syntax grammar over a parsed XML tree.
module internal RdfXmlGrammar =
    let private rdf = RdfNamespaces.Rdf
    let private xmlNamespace = XNamespace.Xml.NamespaceName
    let private rdfIri local = Iri.Create(rdf + local)
    let private coreSyntaxTerms = set [ "RDF"; "ID"; "about"; "parseType"; "resource"; "nodeID"; "datatype" ]
    let private oldTerms = set [ "aboutEach"; "aboutEachPrefix"; "bagID" ]
    let private xmlLiteral = Iri.Create(rdf + "XMLLiteral")

    let private location (node: XObject) =
        match node :> obj with
        | :? IXmlLineInfo as info when info.HasLineInfo() -> ValueSome { Line = info.LineNumber; Column = info.LinePosition }
        | _ -> ValueNone

    let fail kind message (node: XObject) =
        raise (RdfXmlFailure { Kind = kind; Message = message; Location = location node })

    let private isRdf (name: XName) local = name.NamespaceName = rdf && name.LocalName = local

    type Context =
        { Base: string option
          Language: string option }

    type private State =
        { Quads: ResizeArray<Quad>
          BlankNodes: Dictionary<string, BlankNodeId>
          Ids: HashSet<string> }

    let private emit (state: State) subject predicate object =
        state.Quads.Add { Subject = subject; Predicate = predicate; Object = object; Graph = GraphLocation.DefaultGraph }

    let private iri (node: XObject) (text: string) =
        match Iri.Parse text with
        | Ok value -> value
        | Error _ -> fail RdfXmlReadErrorKind.InvalidIri $"'{text}' is not an absolute IRI." node

    let private resolve (context: Context) (node: XObject) (reference: string) =
        match IriReferences.resolve context.Base reference with
        | Some resolved -> iri node resolved
        | None -> fail RdfXmlReadErrorKind.InvalidIri $"The relative reference '{reference}' has no base IRI." node

    /// The base and language of an element, from its xml:base and xml:lang attributes.
    let enter (context: Context) (element: XElement) =
        let baseAttribute = element.Attribute(XNamespace.Xml + "base")
        let languageAttribute = element.Attribute(XNamespace.Xml + "lang")

        let baseIri =
            match baseAttribute with
            | null -> context.Base
            | attribute ->
                match IriReferences.resolve context.Base attribute.Value with
                | Some resolved -> Some(IriReferences.withoutFragment resolved)
                | None -> fail RdfXmlReadErrorKind.InvalidIri $"xml:base '{attribute.Value}' has no base IRI to resolve against." attribute

        let language =
            match languageAttribute with
            | null -> context.Language
            | attribute when attribute.Value = "" -> None
            | attribute -> Some attribute.Value

        { Base = baseIri; Language = language }

    let private literal (node: XObject) (result: Result<RdfLiteral, LiteralError>) =
        match result with
        | Ok value -> RdfObject.Literal value
        | Error error -> fail RdfXmlReadErrorKind.InvalidLiteral (error.ToString()) node

    let private plainLiteral (context: Context) (node: XObject) (text: string) =
        match context.Language with
        | Some language -> literal node (RdfLiteral.LanguageTagged(text, language))
        | None -> literal node (RdfLiteral.String text)

    let private elementIri (element: XElement) =
        if element.Name.NamespaceName = "" then
            fail RdfXmlReadErrorKind.InvalidSyntax $"The element '{element.Name.LocalName}' has no namespace, so it is not an IRI." element

        iri element (element.Name.NamespaceName + element.Name.LocalName)

    /// The attributes that carry RDF meaning: not namespace declarations and not xml* names.
    let private meaningful (element: XElement) =
        element.Attributes()
        |> Seq.filter (fun attribute ->
            not attribute.IsNamespaceDeclaration
            && attribute.Name.NamespaceName <> xmlNamespace
            && not (attribute.Name.NamespaceName = "" && attribute.Name.LocalName.StartsWith("xml", StringComparison.OrdinalIgnoreCase)))
        |> Seq.toList

    let private ncName (node: XObject) (value: string) =
        if value.Length = 0 then
            fail RdfXmlReadErrorKind.InvalidSyntax "An empty value is not an XML NCName." node

        try
            XmlConvert.VerifyNCName value |> ignore
        with :? XmlException ->
            fail RdfXmlReadErrorKind.InvalidSyntax $"'{value}' is not an XML NCName." node

    let private blankNode (state: State) (node: XObject) (label: string) =
        ncName node label

        match state.BlankNodes.TryGetValue label with
        | true, value -> value
        | false, _ ->
            let value = BlankNodeId.New()
            state.BlankNodes.Add(label, value)
            value

    let private idIri (state: State) (context: Context) (attribute: XAttribute) =
        ncName attribute attribute.Value
        let value = resolve context attribute ("#" + attribute.Value)

        if not (state.Ids.Add value.Value) then
            fail RdfXmlReadErrorKind.InvalidSyntax $"rdf:ID '{attribute.Value}' is used twice with the same base." attribute

        value

    let private checkPropertyAttribute (attribute: XAttribute) =
        let name = attribute.Name

        if name.NamespaceName = "" then
            fail RdfXmlReadErrorKind.InvalidSyntax $"The attribute '{name.LocalName}' has no namespace." attribute

        if name.NamespaceName = rdf
           && (coreSyntaxTerms.Contains name.LocalName || oldTerms.Contains name.LocalName || name.LocalName = "li" || name.LocalName = "Description") then
            fail RdfXmlReadErrorKind.InvalidSyntax $"rdf:{name.LocalName} cannot be used as a property attribute here." attribute

    let private propertyAttributes (state: State) (context: Context) subject (attributes: XAttribute list) =
        for attribute in attributes do
            checkPropertyAttribute attribute

            if isRdf attribute.Name "type" then
                emit state subject (rdfIri "type") (RdfObject.Iri(resolve context attribute attribute.Value))
            else
                emit state subject (iri attribute (attribute.Name.NamespaceName + attribute.Name.LocalName)) (plainLiteral context attribute attribute.Value)

    let private reify (state: State) (statement: Iri) subject predicate object =
        let node = RdfSubject.Iri statement
        emit state node (rdfIri "type") (RdfObject.Iri(rdfIri "Statement"))

        emit state node (rdfIri "subject") (
            match subject with
            | RdfSubject.Iri value -> RdfObject.Iri value
            | RdfSubject.BlankNode value -> RdfObject.BlankNode value
        )

        emit state node (rdfIri "predicate") (RdfObject.Iri predicate)
        emit state node (rdfIri "object") object

    let private toObject subject =
        match subject with
        | RdfSubject.Iri value -> RdfObject.Iri value
        | RdfSubject.BlankNode value -> RdfObject.BlankNode value

    /// Child elements, after checking that every other child is whitespace, a comment or a processing instruction.
    let private childElements (element: XElement) =
        for node in element.Nodes() do
            match node with
            | :? XText as text when not (String.IsNullOrWhiteSpace text.Value) ->
                fail RdfXmlReadErrorKind.InvalidSyntax "Only whitespace may appear between elements here." text
            | _ -> ()

        element.Elements() |> Seq.toList

    /// Exclusive XML canonicalization of element content, the lexical form of rdf:XMLLiteral.
    module private XmlLiteral =
        let private text (builder: StringBuilder) (value: string) =
            for ch in value do
                match ch with
                | '&' -> builder.Append "&amp;" |> ignore
                | '<' -> builder.Append "&lt;" |> ignore
                | '>' -> builder.Append "&gt;" |> ignore
                | '\r' -> builder.Append "&#xD;" |> ignore
                | ch -> builder.Append ch |> ignore

        let private attributeValue (builder: StringBuilder) (value: string) =
            for ch in value do
                match ch with
                | '&' -> builder.Append "&amp;" |> ignore
                | '<' -> builder.Append "&lt;" |> ignore
                | '"' -> builder.Append "&quot;" |> ignore
                | '\t' -> builder.Append "&#x9;" |> ignore
                | '\n' -> builder.Append "&#xA;" |> ignore
                | '\r' -> builder.Append "&#xD;" |> ignore
                | ch -> builder.Append ch |> ignore

        let private prefixOf (element: XElement) (ns: XNamespace) =
            if ns = XNamespace.None then ""
            elif ns = XNamespace.Xml then "xml"
            else
                match element.GetPrefixOfNamespace ns with
                | null -> ""
                | prefix -> prefix

        let private qualified prefix (local: string) = if prefix = "" then local else prefix + ":" + local

        let rec private node (builder: StringBuilder) (rendered: Map<string, string>) (child: XNode) =
            match child with
            | :? XText as value -> text builder value.Value
            | :? XProcessingInstruction as instruction ->
                builder.Append("<?").Append(instruction.Target) |> ignore
                if instruction.Data <> "" then builder.Append(' ').Append(instruction.Data) |> ignore
                builder.Append "?>" |> ignore
            | :? XElement as element ->
                let elementPrefix = prefixOf element element.Name.Namespace
                let attributes = element.Attributes() |> Seq.filter (fun attribute -> not attribute.IsNamespaceDeclaration) |> Seq.toList

                let utilized =
                    [ yield elementPrefix, element.Name.NamespaceName
                      for attribute in attributes do
                          if attribute.Name.Namespace <> XNamespace.None && attribute.Name.Namespace <> XNamespace.Xml then
                              yield prefixOf element attribute.Name.Namespace, attribute.Name.NamespaceName ]
                    |> List.distinct

                let declarations =
                    utilized
                    |> List.filter (fun (prefix, ns) ->
                        match rendered.TryFind prefix with
                        | Some existing -> existing <> ns
                        | None -> not (prefix = "" && ns = ""))
                    |> List.sortBy fst

                let name = qualified elementPrefix element.Name.LocalName
                builder.Append('<').Append(name) |> ignore

                for prefix, ns in declarations do
                    builder.Append(if prefix = "" then " xmlns=\"" else " xmlns:" + prefix + "=\"") |> ignore
                    attributeValue builder ns
                    builder.Append '"' |> ignore

                for attribute in attributes |> List.sortBy (fun attribute -> attribute.Name.NamespaceName, attribute.Name.LocalName) do
                    builder.Append(' ').Append(qualified (prefixOf element attribute.Name.Namespace) attribute.Name.LocalName).Append("=\"") |> ignore
                    attributeValue builder attribute.Value
                    builder.Append '"' |> ignore

                builder.Append '>' |> ignore
                let inner = declarations |> List.fold (fun (map: Map<string, string>) (prefix, ns) -> map.Add(prefix, ns)) rendered

                for grandchild in element.Nodes() do
                    node builder inner grandchild

                builder.Append("</").Append(name).Append('>') |> ignore
            | _ -> ()

        let content (element: XElement) =
            let builder = StringBuilder()

            for child in element.Nodes() do
                node builder Map.empty child

            builder.ToString()

    let rec private nodeElement (state: State) (context: Context) (element: XElement) : RdfSubject =
        let name = element.Name

        if name.NamespaceName = rdf && (coreSyntaxTerms.Contains name.LocalName || oldTerms.Contains name.LocalName || name.LocalName = "li") then
            fail RdfXmlReadErrorKind.InvalidSyntax $"rdf:{name.LocalName} cannot be a node element." element

        let context = enter context element
        let attributes = meaningful element
        let named local = attributes |> List.tryFind (fun attribute -> isRdf attribute.Name local)
        let identifiers = [ named "ID"; named "nodeID"; named "about" ] |> List.choose id

        if identifiers.Length > 1 then
            fail RdfXmlReadErrorKind.InvalidSyntax "A node element has at most one of rdf:ID, rdf:nodeID and rdf:about." element

        let subject =
            match identifiers with
            | [ attribute ] when isRdf attribute.Name "ID" -> RdfSubject.Iri(idIri state context attribute)
            | [ attribute ] when isRdf attribute.Name "nodeID" -> RdfSubject.BlankNode(blankNode state attribute attribute.Value)
            | [ attribute ] -> RdfSubject.Iri(resolve context attribute attribute.Value)
            | _ -> RdfSubject.BlankNode(BlankNodeId.New())

        if not (isRdf name "Description") then
            emit state subject (rdfIri "type") (RdfObject.Iri(elementIri element))

        attributes
        |> List.filter (fun attribute -> not (List.exists (fun (identifier: XAttribute) -> identifier = attribute) identifiers))
        |> propertyAttributes state context subject

        let mutable counter = 1

        for child in childElements element do
            counter <- propertyElement state context subject counter child

        subject

    /// Processes one property element and returns the next rdf:li number.
    and private propertyElement (state: State) (context: Context) (subject: RdfSubject) (counter: int) (element: XElement) =
        let name = element.Name

        if name.NamespaceName = rdf && (coreSyntaxTerms.Contains name.LocalName || oldTerms.Contains name.LocalName || name.LocalName = "Description") then
            fail RdfXmlReadErrorKind.InvalidSyntax $"rdf:{name.LocalName} cannot be a property element." element

        let predicate, next =
            if isRdf name "li" then rdfIri ("_" + counter.ToString(Globalization.CultureInfo.InvariantCulture)), counter + 1 else elementIri element, counter

        let context = enter context element
        let attributes = meaningful element
        let named local = attributes |> List.tryFind (fun attribute -> isRdf attribute.Name local)
        let others = attributes |> List.filter (fun attribute -> not (isRdf attribute.Name "ID"))
        let reification = named "ID" |> Option.map (idIri state context)

        let complete object =
            emit state subject predicate object
            reification |> Option.iter (fun statement -> reify state statement subject predicate object)

        let only (allowed: string list) =
            for attribute in others do
                if not (allowed |> List.exists (isRdf attribute.Name)) then
                    fail RdfXmlReadErrorKind.InvalidSyntax $"'{attribute.Name.LocalName}' is not allowed on this property element." attribute

        match named "parseType" with
        | Some parseType when parseType.Value = "Resource" ->
            only [ "parseType" ]
            let node = RdfSubject.BlankNode(BlankNodeId.New())
            complete (toObject node)
            let mutable inner = 1

            for child in childElements element do
                inner <- propertyElement state context node inner child
        | Some parseType when parseType.Value = "Collection" ->
            only [ "parseType" ]
            let items = childElements element |> List.map (nodeElement state context)

            match items with
            | [] -> complete (RdfObject.Iri(rdfIri "nil"))
            | _ ->
                let cells = items |> List.map (fun _ -> BlankNodeId.New())
                complete (RdfObject.BlankNode cells.Head)

                for index, (cell, item) in List.indexed (List.zip cells items) do
                    emit state (RdfSubject.BlankNode cell) (rdfIri "first") (toObject item)

                    let rest = if index + 1 < cells.Length then RdfObject.BlankNode cells[index + 1] else RdfObject.Iri(rdfIri "nil")
                    emit state (RdfSubject.BlankNode cell) (rdfIri "rest") rest
        | Some _ ->
            only [ "parseType" ]
            complete (literal element (RdfLiteral.Typed(XmlLiteral.content element, xmlLiteral)))
        | None ->
            let children = if element.HasElements then childElements element else []

            if not children.IsEmpty then
                only []

                match children with
                | [ child ] -> complete (toObject (nodeElement state context child))
                | _ -> fail RdfXmlReadErrorKind.InvalidSyntax "A property element holds at most one node element." element
            elif element.Nodes() |> Seq.exists (fun node -> node :? XText) then
                only [ "datatype" ]

                match named "datatype" with
                | Some datatype -> complete (literal element (RdfLiteral.Typed(element.Value, resolve context datatype datatype.Value)))
                | None -> complete (plainLiteral context element element.Value)
            else
                let resource = named "resource"
                let nodeId = named "nodeID"
                let datatype = named "datatype"

                let properties =
                    others
                    |> List.filter (fun attribute -> not (isRdf attribute.Name "resource" || isRdf attribute.Name "nodeID" || isRdf attribute.Name "datatype"))

                if resource.IsSome && nodeId.IsSome then
                    fail RdfXmlReadErrorKind.InvalidSyntax "A property element has at most one of rdf:resource and rdf:nodeID." element

                match datatype with
                | Some value when resource.IsNone && nodeId.IsNone && properties.IsEmpty ->
                    complete (literal element (RdfLiteral.Typed("", resolve context value value.Value)))
                | Some value -> fail RdfXmlReadErrorKind.InvalidSyntax "rdf:datatype applies only to a literal." value
                | None ->
                    if resource.IsNone && nodeId.IsNone && properties.IsEmpty then
                        complete (plainLiteral context element "")
                    else
                        let objectNode =
                            match resource, nodeId with
                            | Some attribute, _ -> RdfSubject.Iri(resolve context attribute attribute.Value)
                            | _, Some attribute -> RdfSubject.BlankNode(blankNode state attribute attribute.Value)
                            | None, None -> RdfSubject.BlankNode(BlankNodeId.New())

                        propertyAttributes state context objectNode properties
                        complete (toObject objectNode)

        next

    /// The context of an element that may sit inside a larger document: its ancestors' xml:base and xml:lang.
    let private inherited (baseIri: string option) (element: XElement) =
        element.Ancestors()
        |> Seq.rev
        |> Seq.fold enter { Base = baseIri |> Option.map IriReferences.withoutFragment; Language = None }

    /// The deepest element nesting the grammar accepts; deeper input is refused rather than
    /// exhausting the stack.
    let maxDepth = 1000

    /// Checks the nesting depth without recursion.
    let private checkDepth (root: XElement) =
        let pending = Stack<XElement * int>()
        pending.Push(root, 1)

        while pending.Count > 0 do
            let element, depth = pending.Pop()

            if depth > maxDepth then
                fail RdfXmlReadErrorKind.InvalidSyntax $"Elements are nested more than {maxDepth} deep." element

            for child in element.Elements() do
                pending.Push(child, depth + 1)

    /// Reads the triples of an rdf:RDF element, or of a single node element.
    let read (baseIri: string option) (element: XElement) =
        checkDepth element

        let state =
            { Quads = ResizeArray()
              BlankNodes = Dictionary(StringComparer.Ordinal)
              Ids = HashSet(StringComparer.Ordinal) }

        let context = inherited baseIri element

        if isRdf element.Name "RDF" then
            let context = enter context element

            for attribute in meaningful element do
                fail RdfXmlReadErrorKind.InvalidSyntax $"rdf:RDF has no attribute '{attribute.Name.LocalName}'." attribute

            for child in childElements element do
                nodeElement state context child |> ignore
        else
            nodeElement state context element |> ignore

        state.Quads |> Seq.fold (fun (dataset: RdfDataset) quad -> dataset.Add quad) RdfDataset.Empty

/// Loads XML with document types prohibited and no resolver, so no external resource is ever read.
module internal RdfXmlInput =
    let settings () =
        XmlReaderSettings(
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024L,
            IgnoreComments = false,
            CloseInput = false
        )

    let private failure kind message line column =
        RdfXmlFailure
            { Kind = kind
              Message = message
              Location = if line > 0 then ValueSome { Line = line; Column = column } else ValueNone }

    /// The line of the first document type declaration, if any, before the XML failed.
    let private doctypeLine (source: ReadOnlyMemory<byte>) =
        let text = Encoding.UTF8.GetString source.Span
        let index = text.IndexOf("<!DOCTYPE", StringComparison.Ordinal)

        if index < 0 then None
        else
            let line = 1 + (text.AsSpan(0, index).Count '\n')
            let column = index - (text.LastIndexOf('\n', max 0 (index - 1)) + 1) + 1
            Some(line, column)

    let load (source: ReadOnlyMemory<byte>) =
        try
            use stream = new MemoryStream(source.ToArray(), false)
            use reader = XmlReader.Create(stream, settings ())
            XDocument.Load(reader, LoadOptions.SetLineInfo ||| LoadOptions.PreserveWhitespace)
        with :? XmlException as error ->
            match doctypeLine source with
            // A prohibited document type is reported without a position, so locate it here.
            | Some(line, column) when error.LineNumber = 0 || line <= error.LineNumber ->
                raise (failure RdfXmlReadErrorKind.DocumentTypeDeclaration "The input declares a document type, which RDF/XML input may not use." line column)
            | _ -> raise (failure RdfXmlReadErrorKind.MalformedXml error.Message error.LineNumber error.LinePosition)
