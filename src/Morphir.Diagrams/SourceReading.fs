// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Xml

/// The checks and rendering steps that every source entry point shares.
[<RequireQualifiedAccess>]
module internal SourceReading =
    /// Checks the source, reads it with a language's reader and keeps the exact text with the model.
    let parse (language: string) (read: string -> Result<Diagram, Diagnostic list>) (source: string | null) =
        let fail code message =
            Error(ImmutableArray.Create(Reading.error code message None))

        match source with
        | null -> fail $"{language}/missing-header" "Expected diagram source."
        | source when source.Length > Reading.sourceLimit ->
            Error(ImmutableArray.Create(Reading.limit language "source" $"Source exceeds {Reading.sourceLimit} UTF-16 code units." None))
        | source ->
            try
                XmlConvert.VerifyXmlChars source |> ignore

                read source
                |> Result.map (fun diagram -> DiagramDocument(source, diagram))
                |> Result.mapError (fun diagnostics -> diagnostics |> List.truncate Reading.diagnosticLimit |> ImmutableArray.CreateRange)
            with :? XmlException ->
                fail $"{language}/invalid-character" "Source contains a character that cannot be represented in SVG."

    let renderSvg (parsed: Result<DiagramDocument, ImmutableArray<Diagnostic>>) (options: SvgRenderOptions) =
        ArgumentNullException.ThrowIfNull options

        match parsed with
        | Ok document -> SvgRenderer.Render(document.Diagram, options)
        | Error errors -> RenderedDiagram("image/svg+xml", None, errors)

    let renderAnimated (parsed: Result<DiagramDocument, ImmutableArray<Diagnostic>>) (options: SvgRenderOptions) =
        ArgumentNullException.ThrowIfNull options

        match parsed with
        | Ok document ->
            match SvgRenderer.TryBuild(document.Diagram, options) with
            | Ok scene -> AnimatedRenderer.Render scene
            | Error errors -> RenderedDiagram("text/html", None, errors)
        | Error errors -> RenderedDiagram("text/html", None, errors)
