// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable

[<RequireQualifiedAccess>]
module internal Reading =
    type Line =
        {
            Text: string
            Offset: int
            Number: int
        }

        member this.Span(start: int, finish: int) =
            SourceSpan(
                SourcePosition(this.Offset + start, this.Number, start + 1),
                SourcePosition(this.Offset + finish, this.Number, finish + 1)
            )

    let sourceLimit = 262_144
    let nodeLimit = 512
    let edgeLimit = 4096

    let immutable (items: seq<'T>) = ImmutableArray.CreateRange<'T> items

    let error code message span =
        Diagnostic(code, DiagnosticSeverity.Error, message, span)

    let lines (source: string) =
        seq {
            let mutable offset = 0
            let mutable number = 1

            while offset < source.Length do
                let mutable finish = offset

                while finish < source.Length && source[finish] <> '\r' && source[finish] <> '\n' do
                    finish <- finish + 1

                yield
                    {
                        Text = source.Substring(offset, finish - offset)
                        Offset = offset
                        Number = number
                    }

                offset <-
                    if
                        finish + 1 < source.Length
                        && source[finish] = '\r'
                        && source[finish + 1] = '\n'
                    then
                        finish + 2
                    else
                        finish + 1

                number <- number + 1
        }

    let startsWith (prefix: string) (text: string) =
        text.StartsWith(prefix, StringComparison.Ordinal)

    let unquote (text: string) =
        let text = text.Trim()

        if text.Length >= 2 && text[0] = '"' && text[text.Length - 1] = '"' then
            text.Substring(1, text.Length - 2).Replace("\\\"", "\"")
        else
            text

    type Cursor(line: Line, start: int) =
        let text = line.Text
        let mutable index = start

        member _.Index = index

        member _.AtEnd = index >= text.Length

        member _.Peek = if index < text.Length then text[index] else '\u0000'

        member _.StartsWith(value: string) =
            text.AsSpan(index).StartsWith(value.AsSpan(), StringComparison.Ordinal)

        member this.Take(value: string) =
            if this.StartsWith value then
                index <- index + value.Length

                true
            else
                false

        member _.SkipSpace() =
            while index < text.Length && Char.IsWhiteSpace text[index] do
                index <- index + 1

        member _.ReadWhile(accept: char -> bool) =
            let first = index

            while index < text.Length && accept text[index] do
                index <- index + 1

            text.Substring(first, index - first)

        member this.ReadUntilClosing(closing: string) =
            let first = index
            let mutable quoted = false
            let mutable escaped = false

            while index < text.Length && (quoted || not (this.StartsWith closing)) do
                let current = text[index]

                if escaped then
                    escaped <- false
                elif quoted && current = '\\' then
                    escaped <- true
                elif current = '"' then
                    quoted <- not quoted

                index <- index + 1

            let value = text.Substring(first, index - first)

            if this.Take closing then Some(unquote value) else None

        member _.Remaining() = text.Substring index

        member _.Advance(count: int) = index <- index + count
