// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Svg

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Xml
open System.Xml.Linq

/// The kind of problem encountered while reading SVG input.
[<RequireQualifiedAccess>]
type SvgReadErrorKind =
    /// The input declares a document type, which the reader refuses.
    | DocumentTypeDeclaration
    /// The input is not well-formed XML.
    | MalformedXml
    /// The input has more characters than the limit.
    | LimitExceeded
    /// The root element is not an SVG svg element.
    | NotSvg

    /// A stable diagnostic code.
    member this.Code =
        match this with
        | SvgReadErrorKind.DocumentTypeDeclaration -> "svg.doctype"
        | SvgReadErrorKind.MalformedXml -> "svg.malformed-xml"
        | SvgReadErrorKind.LimitExceeded -> "svg.limit-exceeded"
        | SvgReadErrorKind.NotSvg -> "svg.not-svg"

    override this.ToString() = this.Code

/// A one-based line and column in SVG input.
[<Struct>]
type SvgTextLocation =
    { /// The one-based line.
      Line: int
      /// The one-based column.
      Column: int }

    override this.ToString() = $"{this.Line}:{this.Column}"

/// An SVG input diagnostic, with its location when the XML reader knows it.
type SvgReadError =
    { /// The kind of problem.
      Kind: SvgReadErrorKind
      /// An explanation of the problem.
      Message: string
      /// The line and column, for F# callers.
      Location: SvgTextLocation voption }

    /// Gets the location if present. For C# callers.
    member this.TryGetLocation([<Out>] location: byref<SvgTextLocation>) =
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

/// The content of an SVG document's root metadata element.
[<Sealed>]
type SvgMetadataContent internal (elements: ImmutableArray<XElement>, text: string) =
    /// The child elements, still attached to the parsed document, so they keep their namespace bindings.
    member _.Elements = elements

    /// The text directly inside the metadata element, exactly as written, including whitespace between elements.
    member _.Text = text

    /// The child elements in one namespace.
    member _.ElementsIn(namespaceUri: string) =
        ArgumentNullException.ThrowIfNull namespaceUri

        elements
        |> Seq.filter (fun element -> element.Name.NamespaceName = namespaceUri)
        |> ImmutableArray.CreateRange

    override _.ToString() = $"SVG metadata with {elements.Length} element(s) and {text.Length} character(s) of text"

/// Whether an SVG document has a root metadata element.
[<RequireQualifiedAccess>]
type SvgMetadataResult =
    /// The document has a metadata element with this content.
    | Found of SvgMetadataContent
    /// The document has no metadata element. This is not an error.
    | NotFound

    /// For C# callers: every case needs a function.
    member this.Match(found: Func<SvgMetadataContent, 'T>, notFound: Func<'T>) =
        match this with
        | SvgMetadataResult.Found content -> found.Invoke content
        | SvgMetadataResult.NotFound -> notFound.Invoke()

    /// Gets the content when the document has metadata. For C# callers.
    member this.TryGetContent([<Out; MaybeNullWhen(false)>] content: byref<SvgMetadataContent>) =
        match this with
        | SvgMetadataResult.Found value ->
            content <- value
            true
        | SvgMetadataResult.NotFound ->
            content <- Unchecked.defaultof<_>
            false

    override this.ToString() =
        match this with
        | SvgMetadataResult.Found content -> content.ToString()
        | SvgMetadataResult.NotFound -> "no SVG metadata"

/// Reads the root metadata element of SVG input. Document type declarations are refused, no
/// external entity or resource is ever resolved, and input beyond a character limit is refused.
[<AbstractClass; Sealed>]
type SvgMetadataReader private () =
    static let svgNamespace = "http://www.w3.org/2000/svg"

    static let error kind message line column =
        Error
            { Kind = kind
              Message = message
              Location = if line > 0 then ValueSome { Line = line; Column = column } else ValueNone }

    /// The input's characters, in the encoding its byte order mark names, else UTF-8.
    static let decode (span: ReadOnlySpan<byte>) =
        if span.Length >= 2 && span[0] = 0xFFuy && span[1] = 0xFEuy then Encoding.Unicode.GetString(span.Slice 2)
        elif span.Length >= 2 && span[0] = 0xFEuy && span[1] = 0xFFuy then Encoding.BigEndianUnicode.GetString(span.Slice 2)
        else Encoding.UTF8.GetString span

    static let characterCount (span: ReadOnlySpan<byte>) =
        if span.Length >= 2 && span[0] = 0xFFuy && span[1] = 0xFEuy then Encoding.Unicode.GetCharCount(span.Slice 2)
        elif span.Length >= 2 && span[0] = 0xFEuy && span[1] = 0xFFuy then Encoding.BigEndianUnicode.GetCharCount(span.Slice 2)
        else Encoding.UTF8.GetCharCount span

    static let stream (source: ReadOnlyMemory<byte>) : Stream =
        match MemoryMarshal.TryGetArray source with
        | true, segment ->
            match segment.Array with
            | null -> new MemoryStream(source.ToArray(), false)
            | array -> new MemoryStream(array, segment.Offset, segment.Count, false)
        | _ -> new MemoryStream(source.ToArray(), false)

    /// The location of the document type declaration, when the decoded prolog shows it.
    static let doctype (text: string) =
        let index = text.IndexOf("<!DOCTYPE", StringComparison.Ordinal)

        if index < 0 then None
        else
            let line = 1 + (text.AsSpan(0, index).Count '\n')
            let column = index - (if index = 0 then 0 else text.LastIndexOf('\n', index - 1) + 1) + 1
            Some(line, column)

    /// Reads the prolog alone. A prohibited document type fails there without a position; every
    /// other prolog error has one.
    static let prologDeclaresDocumentType (source: ReadOnlyMemory<byte>) (settings: XmlReaderSettings) =
        try
            use reader = XmlReader.Create(stream source, settings)
            let mutable inProlog = true

            while inProlog && reader.Read() do
                if reader.NodeType = XmlNodeType.Element then inProlog <- false

            false
        with :? XmlException as failure ->
            failure.LineNumber = 0

    /// The default character limit: 16 million characters.
    static member DefaultMaxCharacters = 16_000_000L

    /// Reads SVG input with a character limit. For F# callers.
    static member Read(source: ReadOnlyMemory<byte>, maxCharacters: int64) : Result<SvgMetadataResult, SvgReadError> =
        if maxCharacters <= 0L then
            raise (ArgumentOutOfRangeException(nameof maxCharacters, maxCharacters, "The limit must be positive."))

        let characters = int64 (characterCount source.Span)

        if characters > maxCharacters then
            error SvgReadErrorKind.LimitExceeded $"The input has {characters} characters, more than the limit of {maxCharacters}." 0 0
        else
            let settings =
                XmlReaderSettings(
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 1024L,
                    MaxCharactersInDocument = maxCharacters,
                    CloseInput = true
                )

            if prologDeclaresDocumentType source settings then
                match doctype (decode source.Span) with
                | Some(line, column) ->
                    error SvgReadErrorKind.DocumentTypeDeclaration "The input declares a document type, which SVG input may not use." line column
                | None -> error SvgReadErrorKind.DocumentTypeDeclaration "The input declares a document type, which SVG input may not use." 0 0
            else
                try
                    use reader = XmlReader.Create(stream source, settings)
                    let document = XDocument.Load(reader, LoadOptions.SetLineInfo ||| LoadOptions.PreserveWhitespace)

                    match document.Root with
                    | null -> error SvgReadErrorKind.NotSvg "The input has no root element." 0 0
                    | root when root.Name <> XName.Get("svg", svgNamespace) ->
                        let info = root :> IXmlLineInfo
                        error SvgReadErrorKind.NotSvg $"The root element is '{root.Name.LocalName}', not an SVG svg element." info.LineNumber info.LinePosition
                    | root ->
                        match root.Element(XName.Get("metadata", svgNamespace)) with
                        | null -> Ok SvgMetadataResult.NotFound
                        | metadata ->
                            let text =
                                metadata.Nodes()
                                |> Seq.choose (fun node ->
                                    match node with
                                    | :? XText as value -> Some value.Value
                                    | _ -> None)
                                |> String.concat ""

                            Ok(SvgMetadataResult.Found(SvgMetadataContent(ImmutableArray.CreateRange(metadata.Elements()), text)))
                with :? XmlException as failure ->
                    error SvgReadErrorKind.MalformedXml failure.Message failure.LineNumber failure.LinePosition

    /// Reads SVG input with the default character limit. For F# callers.
    static member Read(source: ReadOnlyMemory<byte>) =
        SvgMetadataReader.Read(source, SvgMetadataReader.DefaultMaxCharacters)

    /// Reads SVG input with a character limit. For C# callers.
    static member TryRead
        (
            source: ReadOnlyMemory<byte>,
            maxCharacters: int64,
            [<Out; MaybeNullWhen(false)>] result: byref<SvgMetadataResult>,
            [<Out; MaybeNullWhen(true)>] failure: byref<SvgReadError>
        ) =
        match SvgMetadataReader.Read(source, maxCharacters) with
        | Ok value ->
            result <- value
            failure <- Unchecked.defaultof<_>
            true
        | Error value ->
            result <- Unchecked.defaultof<_>
            failure <- value
            false

    /// Reads SVG input with the default character limit. For C# callers.
    static member TryRead
        (
            source: ReadOnlyMemory<byte>,
            [<Out; MaybeNullWhen(false)>] result: byref<SvgMetadataResult>,
            [<Out; MaybeNullWhen(true)>] failure: byref<SvgReadError>
        ) =
        SvgMetadataReader.TryRead(source, SvgMetadataReader.DefaultMaxCharacters, &result, &failure)
