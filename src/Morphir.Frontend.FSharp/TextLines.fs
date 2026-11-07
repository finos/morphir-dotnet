// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open System

/// <summary>One ordered F# source document: the URI that the caller gave, and its text.</summary>
type internal SourceDocument = { Uri: string; Text: string }

/// <summary>
/// The line starts of a text, in UTF-16 code units. F# ends a line at <c>'\n'</c>, so a CRLF line ends at
/// its <c>'\n'</c> and the <c>'\r'</c> stays in the line.
/// </summary>
[<Sealed>]
type internal LineIndex(text: string) =
    let starts =
        let found = ResizeArray [ 0 ]

        for index in
            0 .. text.Length
                 - 1 do
            if text[index] = '\n' then
                found.Add(index + 1)

        found.ToArray()

    /// <summary>The text length in UTF-16 code units.</summary>
    member _.Length = text.Length

    /// <summary>The offset of an F# compiler position: a one-based line and a zero-based UTF-16 column.</summary>
    member _.Offset(line: int, column: int) =
        let line = Math.Clamp(line, 1, starts.Length)

        Math.Min(
            starts[line - 1]
            + Math.Max(column, 0),
            text.Length
        )

    /// <summary>The F# compiler position (one-based line, zero-based column) of an offset.</summary>
    member _.Position(offset: int) =
        let offset = Math.Clamp(offset, 0, text.Length)
        let index = Array.BinarySearch(starts, offset)

        let line =
            if index >= 0 then
                index
            else
                ~~~index
                - 1

        struct (line + 1,
                offset
                - starts[line])
