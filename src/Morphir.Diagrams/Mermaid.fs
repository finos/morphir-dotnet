// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices

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

            if line.Number = 1 && cursor.Peek = '\uFEFF' then
                cursor.Advance 1

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

                        Reading.warn
                            "mermaid/unapplied-directive"
                            "Directives such as %%{init}%% are kept in the source but do not change the render options."
                            directive

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
            Error [ Reading.error "mermaid/missing-header" "Expected a Mermaid diagram header." None ]
        else
            let line, start = statements[0]
            let cursor = Reading.Cursor(line, start)

            let header = cursor.ReadWhile(fun c -> not (Char.IsWhiteSpace c) && c <> ';')

            cursor.SkipSpace()

            let fail code message =
                Error [ Reading.error code message (Some(line.Span(start, line.Text.Length))) ]

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
                    FlowchartReader.read flowDirection (body ()) |> Result.mapError List.singleton
                | _ ->
                    fail
                        "mermaid/flowchart-header"
                        "Expected a flowchart direction (TB, TD, BT, LR or RL)."
            | "sequenceDiagram" when cursor.AtEnd || cursor.Take ";" || cursor.StartsWith "%%" ->
                cursor.SkipSpace()
                SequenceReader.read (body ()) |> Result.mapError List.singleton
            | "sequenceDiagram" ->
                fail "mermaid/unexpected-header" "Expected the sequence diagram header on its own."
            | "stateDiagram"
            | "stateDiagram-v2" -> StateReader.read (body ())
            | "swimlane-beta" -> SwimlaneReader.read (body ())
            | "architecture-beta" -> ArchitectureReader.read (body ())
            | "zenuml" -> ZenUmlReader.read (body ())
            | "classDiagram"
            | "classDiagram-v2"
            | "erDiagram"
            | "gantt"
            | "pie"
            | "journey"
            | "gitGraph"
            | "mindmap"
            | "timeline"
            | "quadrantChart"
            | "requirementDiagram"
            | "C4Context"
            | "C4Container"
            | "C4Component"
            | "C4Dynamic"
            | "C4Deployment"
            | "sankey-beta"
            | "xychart-beta"
            | "block-beta"
            | "packet-beta"
            | "kanban"
            | "radar-beta"
            | "treemap-beta" ->
                fail "mermaid/unsupported-kind" $"Mermaid {header} diagrams are not supported."
            | _ ->
                fail
                    "mermaid/unsupported-diagram"
                    "This reader supports Mermaid flowchart, sequence, state, swimlane, architecture and ZenUML diagrams."

    let read source =
        statements source |> Result.mapError List.singleton |> Result.bind readDiagram

/// Reads Mermaid source and produces immutable diagram models.
[<AbstractClass; Sealed>]
type Mermaid private () =
    /// Parses a Mermaid flowchart or sequence diagram. Returns a Result for F# callers.
    static member Parse
        (source: string | null)
        : Result<DiagramDocument, ImmutableArray<Diagnostic>> =
        SourceReading.parse "mermaid" MermaidReader.read source

    /// Writes a flowchart or sequence diagram as canonical Mermaid text. Reading the text gives the same model, and
    /// printing that again gives the same text. Returns a Result for F# callers.
    static member Print(diagram: Diagram) : Result<string, ImmutableArray<Diagnostic>> =
        ArgumentNullException.ThrowIfNull diagram
        MermaidPrinter.print diagram |> Result.mapError ImmutableArray.CreateRange

    /// Writes a flowchart or sequence diagram as canonical Mermaid text for C# callers. Diagnostics is empty on success.
    static member TryPrint
        (
            diagram: Diagram,
            [<Out; MaybeNullWhen(false)>] text: byref<string>,
            [<Out>] diagnostics: byref<ImmutableArray<Diagnostic>>
        ) =
        match Mermaid.Print diagram with
        | Ok printed ->
            text <- printed
            diagnostics <- ImmutableArray<Diagnostic>.Empty
            true
        | Error errors ->
            text <- Unchecked.defaultof<_>
            diagnostics <- errors
            false

    /// Writes a sequence diagram as canonical ZenUML text, with calls printed without bodies. Returns a Result for
    /// F# callers.
    static member PrintZenUml(diagram: Diagram) : Result<string, ImmutableArray<Diagnostic>> =
        ArgumentNullException.ThrowIfNull diagram

        match diagram with
        | Diagram.Sequence model -> MermaidPrinter.zenuml model |> Result.mapError ImmutableArray.CreateRange
        | other -> Error(ImmutableArray.Create(Reading.error "zenuml/unprintable" $"{other} is not a sequence diagram." None))

    /// Writes a sequence diagram as canonical ZenUML text for C# callers. Diagnostics is empty on success.
    static member TryPrintZenUml
        (
            diagram: Diagram,
            [<Out; MaybeNullWhen(false)>] text: byref<string>,
            [<Out>] diagnostics: byref<ImmutableArray<Diagnostic>>
        ) =
        match Mermaid.PrintZenUml diagram with
        | Ok printed ->
            text <- printed
            diagnostics <- ImmutableArray<Diagnostic>.Empty
            true
        | Error errors ->
            text <- Unchecked.defaultof<_>
            diagnostics <- errors
            false

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
        SourceReading.renderSvg (Mermaid.Parse source) options

    /// Parses Mermaid source and renders an offline HTML playback artifact in the Archify theme.
    static member RenderAnimated(source: string | null) =
        Mermaid.RenderAnimated(source, SvgRenderOptions.Default)

    /// Parses Mermaid source and renders an offline HTML playback artifact with a theme and mode.
    /// The page chrome takes its colours from the same palette.
    static member RenderAnimated(source: string | null, options: SvgRenderOptions) =
        SourceReading.renderAnimated (Mermaid.Parse source) options
