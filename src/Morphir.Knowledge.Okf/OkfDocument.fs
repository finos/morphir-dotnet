// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Okf

open System
open System.Collections.Immutable
open System.Runtime.InteropServices
open System.Text
open Morphir.Knowledge

[<RequireQualifiedAccess>]
module private DocumentParser =
    let utf8 = UTF8Encoding(encoderShouldEmitUTF8Identifier = false, throwOnInvalidBytes = true)

    let private isFence (text: string) start finish =
        finish - start = 3 && text[start] = '-' && text[start + 1] = '-' && text[start + 2] = '-'

    let private fences text start =
        let struct (firstEnd, contentStart) = SourceSyntax.line text start
        if not (isFence text start firstEnd) then ValueNone
        else
            let rec find current =
                let struct (lineEnd, next) = SourceSyntax.line text current
                if isFence text current lineEnd then ValueSome(struct (contentStart, current, next))
                elif current = next then ValueNone
                else find next
            find contentStart

    let parse (source: OkfSource) =
        let text = source.Text
        let beginning = if text.StartsWith '\uFEFF' then 1 else 0
        let children = ResizeArray<SyntaxNode>()
        if beginning > 0 then
            children.Add(SourceSyntax.leaf SyntaxKind.ByteOrderMark 0 beginning)

        let frontmatter, bodyStart, diagnostics =
            match fences text beginning with
            | ValueSome(struct (start, stop, next)) ->
                let frontmatter, yamlSyntax, diagnostics = YamlSyntax.parse text start stop
                let parts = seq {
                    yield SourceSyntax.leaf SyntaxKind.OpeningFence beginning (beginning + 3)
                    yield! yamlSyntax
                    yield SourceSyntax.leaf SyntaxKind.ClosingFence stop (stop + 3)
                }
                children.Add(SourceSyntax.branch text SyntaxKind.Frontmatter beginning next parts)
                frontmatter, next, diagnostics
            | ValueNone -> Frontmatter.Absent, beginning, ImmutableArray<Diagnostic>.Empty

        let body, bodySyntax = MarkdownSyntax.parse source bodyStart
        children.Add bodySyntax
        { Frontmatter = frontmatter; Body = body },
        SourceSyntax.branch text SyntaxKind.Document 0 text.Length children,
        diagnostics

/// One UTF-8 OKF document with a full syntax tree and semantic structure.
[<Sealed>]
type OkfDocument private (source: OkfSource, cst: SyntaxNode, ast: OkfDocumentAst) =
    /// The immutable original source shared by both trees.
    member _.Source = source
    /// The concrete syntax root and its original ranges.
    member _.Syntax = cst
    /// The semantic root and its children.
    member _.Ast = ast

    /// Creates a cursor over the concrete tree.
    member _.SyntaxCursor =
        Cursor(cst, (fun node -> node.Children), (fun node -> ValueSome node.Span), None, ImmutableArray.Create cst, 0)

    /// Creates a cursor over the semantic tree.
    member _.AstCursor =
        let root = AstNode.Document ast
        let children = function
            | AstNode.Document tree -> ImmutableArray.Create(AstNode.Frontmatter tree.Frontmatter, AstNode.Markdown tree.Body)
            | AstNode.Frontmatter(Frontmatter.Parsed yaml) -> ImmutableArray.Create(AstNode.Yaml yaml)
            | AstNode.Frontmatter _ -> ImmutableArray<AstNode>.Empty
            | AstNode.Yaml yaml ->
                match yaml.Kind with
                | YamlKind.Mapping entries ->
                    let result = ImmutableArray.CreateBuilder<AstNode>(entries.Length * 2)
                    for entry in entries do
                        result.Add(AstNode.Yaml entry.Key)
                        result.Add(AstNode.Yaml entry.Value)
                    result.MoveToImmutable()
                | YamlKind.Sequence values -> ImmutableArray.CreateRange(values |> Seq.map AstNode.Yaml)
                | _ -> ImmutableArray<AstNode>.Empty
            | AstNode.Markdown markdown -> ImmutableArray.CreateRange(markdown.Children |> Seq.map AstNode.Markdown)

        let location = function
            | AstNode.Document _ -> ValueSome cst.Span
            | AstNode.Frontmatter Frontmatter.Absent -> ValueNone
            | AstNode.Frontmatter _ ->
                let frontmatter = cst.Children |> Seq.find (fun node -> node.Kind = SyntaxKind.Frontmatter)
                ValueSome frontmatter.Span
            | AstNode.Yaml node -> ValueSome node.Span
            | AstNode.Markdown node -> ValueSome node.Span

        Cursor(root, children, location, None, ImmutableArray.Create root, 0)

    /// Reconstructs the unedited UTF-8 document by emitting concrete leaves in order.
    member this.Reconstruct() =
        let text = StringBuilder(source.Text.Length)
        this.SyntaxCursor.Visit(fun cursor ->
            let node = cursor.Node
            if node.Children.IsEmpty then
                let span = node.Span
                text.Append(source.Text, span.Start.Value, span.EndExclusive.Value - span.Start.Value) |> ignore
            VisitControl.Continue)
        DocumentParser.utf8.GetBytes(text.ToString()) |> ImmutableCollectionsMarshal.AsImmutableArray

    static member private ParseSource(source: OkfSource) =
        let ast, cst, diagnostics = DocumentParser.parse source
        OkfParseResult.ParsedDocument(OkfDocument(source, cst, ast), diagnostics)

    /// Decodes an independent snapshot of the supplied UTF-8 bytes.
    static member Parse(bytes: ReadOnlyMemory<byte>) =
        let owned = bytes.ToArray()
        try
            let text = DocumentParser.utf8.GetString owned
            OkfDocument.ParseSource(OkfSource(text, ImmutableCollectionsMarshal.AsImmutableArray owned))
        with :? DecoderFallbackException ->
            OkfParseResult.UnreadableSource
                { Code = "okf.invalid_utf8"
                  Severity = DiagnosticSeverity.Error
                  Message = "The document contains an invalid UTF-8 sequence."
                  Span = ValueNone }

    /// Parses a string, with the UTF-8 encoding of its text as original bytes.
    static member Parse(text: string | null) =
        match text with
        | null ->
            OkfParseResult.UnreadableSource
                { Code = "okf.missing_source"
                  Severity = DiagnosticSeverity.Error
                  Message = "The document source is missing."
                  Span = ValueNone }
        | text ->
            try
                let bytes = DocumentParser.utf8.GetBytes text
                OkfDocument.ParseSource(OkfSource(text, ImmutableCollectionsMarshal.AsImmutableArray bytes))
            with :? EncoderFallbackException ->
                OkfParseResult.UnreadableSource
                    { Code = "okf.invalid_utf8"
                      Severity = DiagnosticSeverity.Error
                      Message = "The document contains invalid Unicode text."
                      Span = ValueNone }

    override _.ToString() = "OKF document"

/// The result of parsing an OKF source, including a recoverable partial document.
and [<RequireQualifiedAccess>] OkfParseResult =
    | ParsedDocument of document: OkfDocument * diagnostics: ImmutableArray<Diagnostic>
    | UnreadableSource of Diagnostic

    /// For C# callers, handles both possible outcomes.
    member this.Match<'Result>
        (parsed: Func<OkfDocument, ImmutableArray<Diagnostic>, 'Result>, unreadable: Func<Diagnostic, 'Result>) =
        match this with
        | ParsedDocument(document, diagnostics) -> parsed.Invoke(document, diagnostics)
        | UnreadableSource finding -> unreadable.Invoke finding

    override this.ToString() =
        match this with
        | ParsedDocument(_, diagnostics) -> $"OKF document ({diagnostics.Length} findings)"
        | UnreadableSource finding -> finding.ToString()

/// Starts and visits immutable cursor subtrees.
[<RequireQualifiedAccess>]
module Cursor =
    /// Creates a cursor on the CST root.
    let syntax (document: OkfDocument) = document.SyntaxCursor
    /// Creates a cursor on the AST root.
    let ast (document: OkfDocument) = document.AstCursor
    /// Visits the selected subtree in depth-first preorder.
    let walk visit (cursor: Cursor<'Node>) = cursor.Visit visit
