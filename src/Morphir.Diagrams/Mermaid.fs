// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Xml

[<RequireQualifiedAccess>]
module private MermaidReader =
    let private direction text =
        match text with
        | ""
        | "TB"
        | "TD" -> Some FlowDirection.TopToBottom
        | "BT" -> Some FlowDirection.BottomToTop
        | "LR" -> Some FlowDirection.LeftToRight
        | "RL" -> Some FlowDirection.RightToLeft
        | _ -> None

    let private statements source =
        let results = ResizeArray<Reading.Line * int>()
        let mutable frontMatter = None
        let mutable directive = None

        for line in Reading.lines source do
            let cursor = Reading.Cursor(line, 0)
            cursor.SkipSpace()

            if
                results.Count = 0 && Option.isNone directive && cursor.Remaining().TrimEnd() = "---"
            then
                frontMatter <-
                    if Option.isSome frontMatter then
                        None
                    else
                        Some(line.Span(cursor.Index, cursor.Index + 3))
            elif Option.isNone frontMatter then
                while not cursor.AtEnd && (Option.isSome directive || cursor.StartsWith "%%{") do
                    if Option.isNone directive then
                        directive <- Some(line.Span(cursor.Index, cursor.Index + 3))

                        cursor.Advance 3

                    let finish = line.Text.IndexOf("}%%", cursor.Index, StringComparison.Ordinal)

                    if finish < 0 then
                        cursor.Advance(line.Text.Length - cursor.Index)
                    else
                        directive <- None

                        cursor.Advance(finish + 3 - cursor.Index)

                        cursor.SkipSpace()

                if not (cursor.AtEnd || Option.isSome directive || cursor.StartsWith "%%") then
                    results.Add(line, cursor.Index)

        match frontMatter, directive with
        | Some span, _ ->
            Error(
                Reading.error
                    "mermaid/unclosed-frontmatter"
                    "Unclosed Mermaid front matter."
                    (Some span)
            )
        | _, Some span ->
            Error(
                Reading.error "mermaid/unclosed-directive" "Unclosed Mermaid directive." (Some span)
            )
        | _ -> Ok(results.ToArray())

    let private readDiagram (statements: (Reading.Line * int) array) =
        if statements.Length = 0 then
            Error(Reading.error "mermaid/missing-header" "Expected a Mermaid diagram header." None)
        else
            let line, start = statements[0]
            let cursor = Reading.Cursor(line, start)

            let header = cursor.ReadWhile(fun c -> not (Char.IsWhiteSpace c) && c <> ';')

            cursor.SkipSpace()

            let fail code message =
                Error(Reading.error code message (Some(line.Span(start, line.Text.Length))))

            let body () =
                seq {
                    if not cursor.AtEnd && not (cursor.StartsWith "%%") then
                        yield line, cursor.Index

                    yield! statements |> Seq.skip 1
                }

            match header with
            | "flowchart"
            | "graph"
            | "flowchart-elk" ->
                let rawDirection = cursor.ReadWhile Char.IsAsciiLetter
                cursor.SkipSpace()

                match direction rawDirection with
                | Some flowDirection when cursor.AtEnd || cursor.Take ";" || cursor.StartsWith "%%" ->
                    cursor.SkipSpace()
                    FlowchartReader.read flowDirection (body ())
                | _ ->
                    fail
                        "mermaid/flowchart-header"
                        "Expected a flowchart direction (TB, TD, BT, LR or RL)."
            | "sequenceDiagram" when cursor.AtEnd || cursor.Take ";" || cursor.StartsWith "%%" ->
                cursor.SkipSpace()
                SequenceReader.read (body ())
            | "sequenceDiagram" ->
                fail "mermaid/unexpected-header" "Expected the sequence diagram header on its own."
            | _ ->
                fail
                    "mermaid/unsupported-diagram"
                    "This reader supports Mermaid flowcharts and sequence diagrams."

    let read source =
        statements source |> Result.bind readDiagram

/// Reads Mermaid source and produces immutable diagram models.
[<AbstractClass; Sealed>]
type Mermaid private () =
    /// Parses a Mermaid flowchart or sequence diagram. Returns a Result for F# callers.
    static member Parse
        (source: string | null)
        : Result<DiagramDocument, ImmutableArray<Diagnostic>> =
        let fail code message =
            Error(ImmutableArray.Create(Reading.error code message None))

        match source with
        | null -> fail "mermaid/missing-header" "Expected a Mermaid diagram header."
        | source when source.Length > Reading.sourceLimit ->
            fail "mermaid/source-limit" $"Source exceeds {Reading.sourceLimit} UTF-16 code units."
        | source ->
            try
                XmlConvert.VerifyXmlChars source |> ignore

                MermaidReader.read source
                |> Result.map (fun diagram -> DiagramDocument(source, diagram))
                |> Result.mapError (fun diagnostic -> ImmutableArray.Create diagnostic)
            with :? XmlException ->
                fail
                    "mermaid/invalid-character"
                    "Source contains a character that cannot be represented in SVG."

    /// Parses a Mermaid diagram for C# callers. Diagnostics is empty on success.
    static member TryParse
        (
            source: string | null,
            [<Out; MaybeNullWhen(false)>] document: byref<DiagramDocument>,
            [<Out>] diagnostics: byref<ImmutableArray<Diagnostic>>
        ) =
        match Mermaid.Parse source with
        | Ok parsed ->
            document <- parsed
            diagnostics <- ImmutableArray<Diagnostic>.Empty
            true
        | Error errors ->
            document <- Unchecked.defaultof<_>
            diagnostics <- errors
            false

    /// Parses Mermaid source and renders it to SVG in the Archify theme, returning diagnostics on failure.
    static member RenderSvg(source: string | null) =
        Mermaid.RenderSvg(source, SvgRenderOptions.Default)

    /// Parses Mermaid source and renders it to SVG with a theme and mode, returning diagnostics on failure.
    static member RenderSvg(source: string | null, options: SvgRenderOptions) =
        ArgumentNullException.ThrowIfNull options

        match Mermaid.Parse source with
        | Ok document -> SvgRenderer.Render(document.Diagram, options)
        | Error errors -> RenderedDiagram("image/svg+xml", None, errors)

    /// Parses Mermaid source and renders an offline HTML playback artifact in the Archify theme.
    static member RenderAnimated(source: string | null) =
        Mermaid.RenderAnimated(source, SvgRenderOptions.Default)

    /// Parses Mermaid source and renders an offline HTML playback artifact with a theme and mode.
    /// The page chrome takes its colours from the same palette.
    static member RenderAnimated(source: string | null, options: SvgRenderOptions) =
        ArgumentNullException.ThrowIfNull options

        match Mermaid.Parse source with
        | Ok document -> AnimatedRenderer.Render(SvgRenderer.BuildScene(document.Diagram, options))
        | Error errors -> RenderedDiagram("text/html", None, errors)
