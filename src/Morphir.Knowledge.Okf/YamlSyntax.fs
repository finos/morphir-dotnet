// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Okf

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open YamlDotNet.Core
open YamlDotNet.Core.Events
open YamlDotNet.Core.Tokens

[<RequireQualifiedAccess>]
module internal YamlSyntax =
    let private style = function
        | ScalarStyle.SingleQuoted -> YamlScalarStyle.SingleQuoted
        | ScalarStyle.DoubleQuoted -> YamlScalarStyle.DoubleQuoted
        | ScalarStyle.Literal -> YamlScalarStyle.Literal
        | ScalarStyle.Folded -> YamlScalarStyle.Folded
        | _ -> YamlScalarStyle.Plain

    let private read (text: string) offset =
        use reader = new StringReader(text)
        let parser = Parser reader
        let anchors = HashSet<string>(StringComparer.Ordinal)
        let coordinate (mark: Mark) = offset + int (Math.Clamp(mark.Index, 0L, int64 text.Length))
        let range mark1 mark2 =
            let first = coordinate mark1
            SourceSyntax.span first (max first (coordinate mark2))

        let nodeFields (node: NodeEvent) finish kind =
            {
                Kind = kind
                Span = range node.Start finish
                Tag = if node.Tag.IsEmpty then None else Some node.Tag.Value
                Anchor = if node.Anchor.IsEmpty then None else Some node.Anchor.Value
            }

        let fail (item: ParsingEvent | null) message =
            match item with
            | null -> raise (YamlException message)
            | event ->
                let first = event.Start
                let last = event.End
                raise (YamlException(&first, &last, message))

        let registerAnchor (node: NodeEvent) =
            if not node.Anchor.IsEmpty then
                anchors.Add node.Anchor.Value |> ignore

        let rec node depth =
            let validDepth =
                if depth > 128 then
                    fail parser.Current "YAML nesting exceeds 128 levels."
                depth + 1

            match parser.Consume<ParsingEvent>() with
            | :? Events.Scalar as scalar ->
                registerAnchor scalar
                nodeFields scalar scalar.End (YamlKind.Scalar { Value = scalar.Value; Style = style scalar.Style })
            | :? Events.AnchorAlias as alias ->
                if not (anchors.Contains alias.Value.Value) then
                    fail alias $"The YAML anchor '{alias.Value.Value}' has not been declared."
                { Kind = YamlKind.Alias alias.Value.Value; Span = range alias.Start alias.End; Tag = None; Anchor = None }
            | :? MappingStart as start ->
                registerAnchor start
                let entries = ImmutableArray.CreateBuilder<YamlEntry>()
                while not (parser.Current :? MappingEnd) do
                    let key = node validDepth
                    entries.Add { Key = key; Value = node validDepth }

                let close = parser.Consume<MappingEnd>()
                nodeFields start close.End (YamlKind.Mapping(entries.ToImmutable()))
            | :? SequenceStart as start ->
                registerAnchor start
                let entries = ImmutableArray.CreateBuilder<YamlNode>()
                while not (parser.Current :? SequenceEnd) do
                    entries.Add(node validDepth)

                let close = parser.Consume<SequenceEnd>()
                nodeFields start close.End (YamlKind.Sequence(entries.ToImmutable()))
            | other -> fail other "Expected one YAML value."

        parser.Consume<Events.StreamStart>() |> ignore
        if parser.Current :? Events.StreamEnd then
            { Kind = YamlKind.Scalar { Value = ""; Style = YamlScalarStyle.Plain }
              Span = SourceSyntax.span offset offset
              Tag = None
              Anchor = None }
        else
            parser.Consume<Events.DocumentStart>() |> ignore
            let root = node 0
            parser.Consume<Events.DocumentEnd>() |> ignore
            parser.Consume<Events.StreamEnd>() |> ignore
            root

    let private lexical (text: string) offset =
        use reader = new StringReader(text)
        let scanner = Scanner(reader, skipComments = false)
        let tokens = ResizeArray<SyntaxNode>()
        while scanner.MoveNext() do
            match scanner.Current with
            | null -> ()
            | token ->
                let first = int (Math.Clamp(token.Start.Index, 0L, int64 text.Length))
                let finish = int (Math.Clamp(token.End.Index, int64 first, int64 text.Length))
                let last =
                    if first = finish && first < text.Length then
                        // Some YAML scanner marks point at a flow punctuation
                        // character without including it in the token width.
                        match token, text[first] with
                        | (:? FlowMappingStart), '{'
                        | (:? FlowMappingEnd), '}'
                        | (:? FlowSequenceStart), '['
                        | (:? FlowSequenceEnd), ']' -> finish + 1
                        | _ -> finish
                    else finish
                if last > first then
                    let kind =
                        match token with
                        | :? Tokens.Comment -> YamlTokenKind.Comment
                        | :? Tokens.Scalar -> YamlTokenKind.Scalar
                        | :? Anchor -> YamlTokenKind.Anchor
                        | :? Tokens.AnchorAlias -> YamlTokenKind.Alias
                        | :? Tag -> YamlTokenKind.Tag
                        | :? VersionDirective
                        | :? TagDirective -> YamlTokenKind.Directive
                        | _ -> YamlTokenKind.Indicator
                    tokens.Add(SourceSyntax.leaf (SyntaxKind.YamlToken kind) (offset + first) (offset + last))

        tokens |> Seq.sortBy (fun token -> token.Span.Start.Value) |> Seq.toArray

    let private concrete text (tokens: SyntaxNode array) (value: YamlNode) =
        let mutable index = 0
        let takeBefore endExclusive (children: ResizeArray<SyntaxNode>) =
            while index < tokens.Length && tokens[index].Span.EndExclusive.Value <= endExclusive do
                children.Add tokens[index]
                index <- index + 1

        let rec render (source: YamlNode) =
            let first = source.Span.Start.Value
            let finish = source.Span.EndExclusive.Value
            match source.Kind with
            | YamlKind.Mapping entries ->
                let children = ResizeArray<SyntaxNode>()
                for entry in entries do
                    let start = entry.Key.Span.Start.Value
                    takeBefore start children
                    let inner = ResizeArray<SyntaxNode>()
                    inner.Add(render entry.Key)
                    takeBefore entry.Value.Span.Start.Value inner
                    inner.Add(render entry.Value)
                    let last = max entry.Key.Span.EndExclusive.Value entry.Value.Span.EndExclusive.Value
                    children.Add(SourceSyntax.branch text SyntaxKind.YamlPair start last inner)
                takeBefore finish children
                SourceSyntax.branch text SyntaxKind.YamlMapping first finish children
            | YamlKind.Sequence entries ->
                let children = ResizeArray<SyntaxNode>()
                for entry in entries do
                    takeBefore entry.Span.Start.Value children
                    children.Add(render entry)
                takeBefore finish children
                SourceSyntax.branch text SyntaxKind.YamlSequence first finish children
            | YamlKind.Scalar _
            | YamlKind.Alias _ ->
                let children = ResizeArray<SyntaxNode>()
                takeBefore finish children
                let kind =
                    match source.Kind with
                    | YamlKind.Alias _ -> YamlTokenKind.Alias
                    | _ -> YamlTokenKind.Scalar
                if children.Count = 1 && children[0].Span = source.Span then children[0]
                else SourceSyntax.branch text (SyntaxKind.YamlToken kind) first finish children

        let bounds = ResizeArray<SyntaxNode>()
        takeBefore value.Span.Start.Value bounds
        bounds.Add(render value)
        takeBefore Int32.MaxValue bounds
        bounds

    let parse (text: string) start finish =
        let yaml = text.Substring(start, finish - start)
        try
            let node = read yaml start
            let syntax = concrete text (lexical yaml start) node
            Frontmatter.Parsed node, syntax, ImmutableArray<Diagnostic>.Empty
        with :? YamlException as error ->
            let first = max start (min finish (start + int error.Start.Index))
            let last = max first (min finish (start + int error.End.Index))
            let diagnostics =
                ImmutableArray.Create
                    { Code = "okf.invalid_yaml"
                      Severity = DiagnosticSeverity.Error
                      Message = error.Message
                      Span = ValueSome(SourceSyntax.span first last) }
            Frontmatter.Invalid(SourceSyntax.span start finish),
            ResizeArray([ SourceSyntax.leaf SyntaxKind.Error start finish ]),
            diagnostics
