// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Okf

open System
open System.Collections.Immutable
open Morphir.Knowledge

[<RequireQualifiedAccess>]
module internal SourceSyntax =
    let span start finish =
        SourceSpan.Create(TextOffset.Create start, TextOffset.Create finish)

    let leaf kind start finish =
        SyntaxNode(kind, span start finish, ImmutableArray<SyntaxNode>.Empty)

    // Returns the end of the line's text, followed by the start of the next line.
    let line (text: string) start =
        let mutable finish = start
        while finish < text.Length && text[finish] <> '\r' && text[finish] <> '\n' do
            finish <- finish + 1

        let next =
            if finish = text.Length then finish
            elif text[finish] = '\r' && finish + 1 < text.Length && text[finish + 1] = '\n' then finish + 2
            else finish + 1
        struct (finish, next)

    let private lexicalKind character =
        match character with
        | '\r'
        | '\n' -> SyntaxKind.Newline
        | value when Char.IsWhiteSpace value -> SyntaxKind.Whitespace
        | value when Char.IsPunctuation value || Char.IsSymbol value -> SyntaxKind.Punctuation value
        | _ -> SyntaxKind.Text

    let private gap (nodes: ImmutableArray<SyntaxNode>.Builder) (text: string) first last =
        let mutable current = first
        while current < last do
            let kind = lexicalKind text[current]
            let mutable finish = current + 1
            match kind with
            | SyntaxKind.Newline ->
                if text[current] = '\r' && finish < last && text[finish] = '\n' then
                    finish <- finish + 1
            | _ ->
                while finish < last && lexicalKind text[finish] = kind do
                    finish <- finish + 1

            nodes.Add(leaf kind current finish)
            current <- finish

    // All source intervals between recognized children become concrete lexical
    // leaves. Unexpected sibling overlap in parser positions is clipped, so
    // trivia from two adjacent parser objects can never be emitted twice.
    let rec branch text kind first last (meaningful: seq<SyntaxNode>) =
        let children = ImmutableArray.CreateBuilder<SyntaxNode>()
        let mutable current = first
        for item in meaningful |> Seq.sortBy (fun child -> child.Span.Start.Value) do
            let childStart = max current item.Span.Start.Value
            let childEnd = min last item.Span.EndExclusive.Value
            if childStart < childEnd then
                gap children text current childStart
                let child =
                    if childStart = item.Span.Start.Value && childEnd = item.Span.EndExclusive.Value then item
                    elif item.Children.IsEmpty then leaf item.Kind childStart childEnd
                    else branch text item.Kind childStart childEnd item.Children
                children.Add child
                current <- childEnd

        gap children text current last
        SyntaxNode(kind, span first last, children.ToImmutable())
