// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Okf

open System
open System.Collections.Immutable
open System.Text.RegularExpressions
open Markdig
open Markdig.Extensions.Footnotes
open Markdig.Extensions.Tables
open Markdig.Syntax
open Markdig.Syntax.Inlines
open Morphir.Knowledge

[<RequireQualifiedAccess>]
module internal MarkdownSyntax =
    let private citation =
        Regex(@"\[\^([^\s\[\]]+)\]", RegexOptions.CultureInvariant ||| RegexOptions.NonBacktracking)

    let private optional (value: string | null) =
        match value with
        | null -> None
        | text -> Some text

    let private alignment (column: TableColumnDefinition) =
        match column.Alignment.GetValueOrDefault() with
        | _ when not column.Alignment.HasValue -> MarkdownTableAlignment.Unspecified
        | TableColumnAlign.Left -> MarkdownTableAlignment.Left
        | TableColumnAlign.Right -> MarkdownTableAlignment.Right
        | TableColumnAlign.Center -> MarkdownTableAlignment.Center
        | _ -> MarkdownTableAlignment.Unspecified

    let private infoString (code: FencedCodeBlock) =
        match optional code.Info, optional code.Arguments with
        | Some language, Some arguments when arguments.Length > 0 -> Some(language + " " + arguments)
        | language, _ -> language

    let parse (source: OkfSource) start =
        let text = source.Text
        let bodyText = text.Substring start
        let footnotes = ResizeArray<Footnote>()
        let parser = FootnoteParser()
        parser.add_Closed(
            Markdig.Parsers.ProcessBlockDelegate(fun _ block ->
                match block with
                | :? Footnote as footnote -> footnotes.Add footnote
                | _ -> ())
        )
        let builder = MarkdownPipelineBuilder()
        builder.BlockParsers.Insert(0, parser)
        let pipeline =
            builder
                .UseFootnotes()
                .UsePipeTables(PipeTableOptions(UseGfmRules = true))
                .UsePreciseSourceLocation()
                .EnableTrackTrivia()
                .Build()
        let markdown = Markdown.Parse(bodyText, pipeline)

        let bound relative = start + Math.Clamp(relative, 0, bodyText.Length)
        let absolute (span: Markdig.Syntax.SourceSpan) =
            let first = bound span.Start
            SourceSyntax.span first (max first (bound (span.End + 1)))

        let destinationSpan (parserSpan: Markdig.Syntax.SourceSpan) =
            let span = absolute parserSpan
            if span.Start < span.EndExclusive then ValueSome span else ValueNone

        let ast kind first last children =
            MarkdownNode(kind, SourceSyntax.span first last, children, source)

        let terminal kind first last =
            ast kind first last ImmutableArray<MarkdownNode>.Empty,
            SourceSyntax.leaf (SyntaxKind.Markdown kind) first last

        let link destination span title image =
            { Destination = defaultArg (optional destination) ""
              DestinationSpan = span
              Title = optional title
              IsImage = image }

        let label (name: string | null) =
            match name with
            | null -> ""
            | value when value.StartsWith '^' -> value.Substring 1
            | value -> value

        let rec project parentStart parentEnd (item: MarkdownObject) =
            let span = absolute item.Span
            let itemStart =
                match item with
                | :? Footnote as footnote -> bound (footnote.LabelSpan.Start - 1)
                | _ -> span.Start.Value
            let first = Math.Clamp(itemStart, parentStart, parentEnd)
            let last = Math.Clamp(span.EndExclusive.Value, first, parentEnd)
            let kind =
                match item with
                | :? HeadingBlock as heading -> MarkdownKind.Heading heading.Level
                | :? ParagraphBlock -> MarkdownKind.Paragraph
                | :? QuoteBlock -> MarkdownKind.Quote
                | :? ListBlock as list ->
                    let number =
                        if list.IsOrdered then
                            match Int32.TryParse(list.OrderedStart, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
                            | true, startNumber -> ValueSome startNumber
                            | _ -> ValueNone
                        else ValueNone
                    MarkdownKind.List(list.IsOrdered, number, not list.IsLoose)
                | :? ListItemBlock -> MarkdownKind.ListItem
                | :? ThematicBreakBlock -> MarkdownKind.ThematicBreak
                | :? FencedCodeBlock as code -> MarkdownKind.CodeBlock(infoString code, code.Lines.ToString())
                | :? CodeBlock as code -> MarkdownKind.CodeBlock(None, code.Lines.ToString())
                | :? HtmlBlock as html -> MarkdownKind.HtmlBlock(html.Lines.ToString())
                | :? Table as table -> MarkdownKind.Table(ImmutableArray.CreateRange(table.ColumnDefinitions |> Seq.map alignment))
                | :? TableRow as row -> MarkdownKind.TableRow row.IsHeader
                | :? TableCell -> MarkdownKind.TableCell
                | :? Footnote as footnote -> MarkdownKind.FootnoteDefinition(label footnote.Label)
                | :? FootnoteLink as note -> MarkdownKind.FootnoteReference(label note.Footnote.Label)
                | :? LinkReferenceDefinition as definition ->
                    MarkdownKind.ReferenceDefinition(
                        defaultArg (optional definition.Label) "",
                        link definition.Url (destinationSpan definition.UrlSpan) definition.Title false
                    )
                | :? LinkInline as inlineLink ->
                    let span =
                        match inlineLink.Reference with
                        | null -> destinationSpan inlineLink.UrlSpan
                        | definition -> destinationSpan definition.UrlSpan
                    MarkdownKind.Link(link inlineLink.Url span inlineLink.Title inlineLink.IsImage)
                | :? AutolinkInline as autoLink ->
                    let target = if autoLink.IsEmail then "mailto:" + autoLink.Url else autoLink.Url
                    let destination =
                        if last - first >= 2 then ValueSome(SourceSyntax.span (first + 1) (last - 1))
                        else ValueNone
                    MarkdownKind.Link(link target destination null false)
                | :? LiteralInline as literal -> MarkdownKind.Text(literal.Content.ToString())
                | :? EmphasisInline as emphasis -> MarkdownKind.Emphasis(emphasis.DelimiterChar, emphasis.DelimiterCount)
                | :? CodeInline as code -> MarkdownKind.CodeSpan code.Content
                | :? HtmlInline as html -> MarkdownKind.HtmlInline html.Tag
                | :? HtmlEntityInline as entity -> MarkdownKind.CharacterReference(entity.Transcoded.ToString())
                | :? LineBreakInline as lineBreak -> MarkdownKind.LineBreak lineBreak.IsHard
                | _ -> MarkdownKind.Text(text.Substring(first, last - first))

            let nodes =
                match item with
                | :? ContainerBlock as blocks ->
                    blocks
                    |> Seq.choose (function
                        | :? EmptyBlock -> None
                        | block -> Some(project first last block))
                    |> Seq.toArray
                | :? LeafBlock as block ->
                    match block.Inline with
                    | null -> Array.empty
                    | inlines -> projectInlines first last inlines
                | :? ContainerInline as inlines -> projectInlines first last inlines
                | _ -> Array.empty

            let concrete = ResizeArray<SyntaxNode>(nodes |> Seq.map snd)
            match item, kind with
            | (:? FencedCodeBlock as code), _ ->
                let delimiter (first: int) (last: int) (count: int) =
                    if count > 0 then
                        let position = text.IndexOf(code.FencedChar, first, last - first)
                        if position >= first && position + count <= last then
                            concrete.Add(SourceSyntax.leaf SyntaxKind.CodeDelimiter position (position + count))
                let struct (openingLineEnd, _) = SourceSyntax.line text first
                delimiter first (min openingLineEnd last) code.OpeningFencedCharCount
                if code.ClosingFencedCharCount > 0 then
                    let newline c = c = '\r' || c = '\n'
                    let mutable endOfClosing = last
                    while first < endOfClosing && newline text[endOfClosing - 1] do
                        endOfClosing <- endOfClosing - 1
                    let mutable startOfClosing = endOfClosing
                    while first < startOfClosing && not (newline text[startOfClosing - 1]) do
                        startOfClosing <- startOfClosing - 1
                    delimiter startOfClosing endOfClosing code.ClosingFencedCharCount
            | (:? CodeInline as code), _ when code.DelimiterCount * 2 <= last - first ->
                concrete.Add(SourceSyntax.leaf SyntaxKind.CodeDelimiter first (first + code.DelimiterCount))
                concrete.Add(SourceSyntax.leaf SyntaxKind.CodeDelimiter (last - code.DelimiterCount) last)
            | (:? LiteralInline as literal), _ when literal.IsFirstCharacterEscaped && first < last && text[first] = '\\' ->
                concrete.Add(SourceSyntax.leaf (SyntaxKind.Punctuation '\\') first (first + 1))
            | (:? Table as table), _ when table.Count > 0 ->
                let struct (_, rowStart) = SourceSyntax.line text (bound (table[0].Span.End + 1))
                let struct (rowEnd, _) = SourceSyntax.line text rowStart
                if first <= rowStart && rowStart < rowEnd && rowEnd <= last then
                    concrete.Add(SourceSyntax.leaf SyntaxKind.TableDelimiterRow rowStart rowEnd)
            | (:? Footnote as note), _ ->
                let footnoteLabel = absolute note.LabelSpan
                concrete.Add(SourceSyntax.leaf SyntaxKind.FootnoteLabel footnoteLabel.Start.Value footnoteLabel.EndExclusive.Value)
            | _, MarkdownKind.Link link
            | _, MarkdownKind.ReferenceDefinition(_, link) ->
                match link.DestinationSpan with
                | ValueSome target when first <= target.Start.Value && target.EndExclusive.Value <= last ->
                    concrete.Add(SourceSyntax.leaf SyntaxKind.LinkDestination target.Start.Value target.EndExclusive.Value)
                | _ -> ()
            | _ -> ()

            let cst =
                match kind with
                | MarkdownKind.Text _
                | MarkdownKind.CharacterReference _
                | MarkdownKind.HtmlInline _
                | MarkdownKind.HtmlBlock _ when concrete.Count = 0 ->
                    SourceSyntax.leaf (SyntaxKind.Markdown kind) first last
                | _ -> SourceSyntax.branch text (SyntaxKind.Markdown kind) first last concrete
            ast kind first last (ImmutableArray.CreateRange(nodes |> Seq.map fst)), cst

        and projectInlines first last (inlines: ContainerInline) =
            let items = inlines |> Seq.toArray
            let nodes = ResizeArray<MarkdownNode * SyntaxNode>()
            let mutable index = 0
            while index < items.Length do
                match items[index] with
                | :? FootnoteLink as note when note.IsBackLink ->
                    // Backlinks are generated by Markdig for HTML rendering.
                    index <- index + 1
                | :? LiteralInline ->
                    let runStart = index
                    let runFirst = max first (bound items[index].Span.Start)
                    let mutable runLast = min last (bound (items[index].Span.End + 1))
                    index <- index + 1
                    // An unresolved reference is split across literal nodes (the
                    // opening bracket and the text that follows it).
                    while index < items.Length && (items[index] :? LiteralInline) && bound items[index].Span.Start = runLast do
                        runLast <- min last (bound (items[index].Span.End + 1))
                        index <- index + 1

                    let escaped (position: int) =
                        let mutable firstSlash = position
                        while firstSlash > runFirst && text[firstSlash - 1] = '\\' do
                            firstSlash <- firstSlash - 1
                        (position - firstSlash) % 2 = 1

                    let mutable remaining = runFirst
                    let mutable candidate = citation.Match(text, runFirst, max 0 (runLast - runFirst))
                    while candidate.Success do
                        let candidateEnd = candidate.Index + candidate.Length
                        if candidateEnd <= runLast && not (escaped candidate.Index) then
                            if remaining < candidate.Index then
                                nodes.Add(terminal (MarkdownKind.Text(text.Substring(remaining, candidate.Index - remaining))) remaining candidate.Index)
                            nodes.Add(terminal (MarkdownKind.FootnoteReference candidate.Groups[1].Value) candidate.Index candidateEnd)
                            remaining <- candidateEnd
                        candidate <- candidate.NextMatch()
                    if remaining = runFirst then
                        for original = runStart to index - 1 do
                            nodes.Add(project first last items[original])
                    elif remaining < runLast then
                        nodes.Add(terminal (MarkdownKind.Text(text.Substring(remaining, runLast - remaining))) remaining runLast)
                | inlineNode ->
                    nodes.Add(project first last inlineNode)
                    index <- index + 1

            nodes.ToArray()

        let nodes =
            seq {
                for block in markdown do
                    match block with
                    | :? FootnoteGroup
                    | :? EmptyBlock -> ()
                    | :? LinkReferenceDefinitionGroup as references ->
                        for definition in references do
                            match definition with
                            | :? LinkReferenceDefinition as item when optional item.Label |> Option.exists (fun name -> name.StartsWith '^') -> ()
                            | item -> yield project start text.Length item
                    | item -> yield project start text.Length item
                for note in footnotes do
                    yield project start text.Length note
            }
            |> Seq.sortBy (fun (node, _) -> node.Span.Start.Value)
            |> Seq.toArray

        ast MarkdownKind.Body start text.Length (ImmutableArray.CreateRange(nodes |> Seq.map fst)),
        SourceSyntax.branch text (SyntaxKind.Markdown MarkdownKind.Body) start text.Length (nodes |> Seq.map snd)
