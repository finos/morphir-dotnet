// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Bpmn

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Text.RegularExpressions
open System.Xml

/// Reads XML into the lossless tree without a DTD, an entity expansion or a resolver.
[<RequireQualifiedAccess>]
module internal XmlTree =
    let private settings =
        XmlReaderSettings(
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            CheckCharacters = true,
            MaxCharactersFromEntities = 0L,
            CloseInput = true
        )

    let private declaration = Regex(@"<!(DOCTYPE|ENTITY)\b", RegexOptions.CultureInvariant)
    let private comments = Regex(@"<!--.*?-->", RegexOptions.Singleline ||| RegexOptions.CultureInvariant)

    type private Frame =
        {
            Prefix: string
            LocalName: string
            NamespaceUri: string
            Attributes: ImmutableArray<BpmnAttribute>
            Children: ResizeArray<BpmnNode>
            Line: int
            Column: int
        }

    /// The top-level nodes of the source: the declaration, comments, instructions and the root element, in order.
    let read (source: string) : Result<ImmutableArray<BpmnNode>, BpmnDiagnostic> =
        // A DOCTYPE or entity declaration is refused before any parsing, so nothing is expanded.
        let found = declaration.Match(comments.Replace(source, ""))

        if found.Success then
            Error(BpmnDiagnostic("xml/prohibited-declaration", BpmnFindingKind.Xml, $"A <!{found.Groups[1].Value} declaration is prohibited: DTDs and entities are not processed.", 0, 0))
        else
            try
                use reader = XmlReader.Create(new StringReader(source), settings)
                let info =
                    match box reader with
                    | :? IXmlLineInfo as info -> info
                    | _ -> invalidOp "The XML reader gives no line information."
                let top = ResizeArray<BpmnNode>()
                let stack = Stack<Frame>()

                let add (node: BpmnNode) =
                    if stack.Count = 0 then top.Add node else stack.Peek().Children.Add node

                let finish (frame: Frame) =
                    BpmnNode(
                        BpmnNodeKind.Element,
                        frame.Prefix,
                        frame.LocalName,
                        frame.NamespaceUri,
                        "",
                        frame.Attributes,
                        ImmutableArray.CreateRange frame.Children,
                        frame.Line,
                        frame.Column
                    )

                let leaf kind name value =
                    BpmnNode(kind, "", name, "", value, ImmutableArray.Empty, ImmutableArray.Empty, info.LineNumber, info.LinePosition)

                while reader.Read() do
                    match reader.NodeType with
                    | XmlNodeType.Element ->
                        let line, column = info.LineNumber, info.LinePosition
                        let prefix, localName, namespaceUri = reader.Prefix, reader.LocalName, reader.NamespaceURI
                        let empty = reader.IsEmptyElement
                        let attributes = ResizeArray<BpmnAttribute>()

                        if reader.MoveToFirstAttribute() then
                            let mutable more = true

                            while more do
                                attributes.Add(BpmnAttribute(reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value))
                                more <- reader.MoveToNextAttribute()

                            reader.MoveToElement() |> ignore

                        let frame =
                            {
                                Prefix = prefix
                                LocalName = localName
                                NamespaceUri = namespaceUri
                                Attributes = ImmutableArray.CreateRange attributes
                                Children = ResizeArray()
                                Line = line
                                Column = column
                            }

                        if empty then add (finish frame) else stack.Push frame
                    | XmlNodeType.EndElement -> add (finish (stack.Pop()))
                    | XmlNodeType.Text -> add (leaf BpmnNodeKind.Text "" reader.Value)
                    | XmlNodeType.CDATA -> add (leaf BpmnNodeKind.CData "" reader.Value)
                    | XmlNodeType.Whitespace
                    | XmlNodeType.SignificantWhitespace -> add (leaf BpmnNodeKind.Whitespace "" reader.Value)
                    | XmlNodeType.Comment -> add (leaf BpmnNodeKind.Comment "" reader.Value)
                    | XmlNodeType.ProcessingInstruction -> add (leaf BpmnNodeKind.ProcessingInstruction reader.Name reader.Value)
                    | XmlNodeType.XmlDeclaration -> add (leaf BpmnNodeKind.Declaration "" reader.Value)
                    | _ -> ()

                Ok(ImmutableArray.CreateRange top)
            with :? XmlException as error ->
                let code = if error.Message.Contains("DTD", StringComparison.Ordinal) then "xml/prohibited-declaration" else "xml/malformed"
                Error(BpmnDiagnostic(code, BpmnFindingKind.Xml, error.Message, error.LineNumber, error.LinePosition))
