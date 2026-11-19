// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices

/// The source language of a diagram, as detected from its first meaningful text.
[<RequireQualifiedAccess>]
type DiagramLanguage =
    /// Mermaid text, detected by its header.
    | Mermaid
    /// A PlantUML block, detected by its `@start` line.
    | PlantUml
    /// BPMN 2.0 XML, detected by its markup.
    | Bpmn

    /// The language name as diagnostics write it, such as plantuml.
    member this.WireValue =
        match this with
        | DiagramLanguage.Mermaid -> "mermaid"
        | DiagramLanguage.PlantUml -> "plantuml"
        | DiagramLanguage.Bpmn -> "bpmn"

    override this.ToString() = this.WireValue

/// Reads Mermaid, PlantUML or BPMN source through one entry point and renders it.
[<AbstractClass; Sealed>]
type DiagramSource private () =
    /// The language of the source: markup is BPMN, a first line that starts with @ or a ' comment is PlantUML,
    /// anything else is Mermaid. Leading blanks and a byte order mark are skipped.
    static member Detect(source: string | null) =
        let text = (source |> Option.ofObj |> Option.defaultValue "").TrimStart('﻿', ' ', '\t', '\r', '\n')

        if text.StartsWith('<') then DiagramLanguage.Bpmn
        elif text.StartsWith('@') || text.StartsWith('\'') || text.StartsWith("/'", StringComparison.Ordinal) then
            DiagramLanguage.PlantUml
        else
            DiagramLanguage.Mermaid

    /// Detects the language and parses the source. Returns a Result for F# callers.
    static member Parse(source: string | null) : Result<DiagramDocument, ImmutableArray<Diagnostic>> =
        match DiagramSource.Detect source with
        | DiagramLanguage.Mermaid -> Mermaid.Parse source
        | DiagramLanguage.PlantUml -> PlantUml.Parse source
        | DiagramLanguage.Bpmn -> SourceReading.parse "bpmn" BpmnSource.read source

    /// Detects the language and parses the source for C# callers. Diagnostics is empty on success.
    static member TryParse
        (
            source: string | null,
            [<Out; MaybeNullWhen(false)>] document: byref<DiagramDocument>,
            [<Out>] diagnostics: byref<ImmutableArray<Diagnostic>>
        ) =
        match DiagramSource.Parse source with
        | Ok parsed ->
            document <- parsed
            diagnostics <- ImmutableArray<Diagnostic>.Empty
            true
        | Error errors ->
            document <- Unchecked.defaultof<_>
            diagnostics <- errors
            false

    /// Parses the source and renders it to SVG in the Archify theme, returning diagnostics on failure.
    static member RenderSvg(source: string | null) =
        DiagramSource.RenderSvg(source, SvgRenderOptions.Default)

    /// Parses the source and renders it to SVG with a theme and mode, returning diagnostics on failure.
    static member RenderSvg(source: string | null, options: SvgRenderOptions) =
        SourceReading.renderSvg (DiagramSource.Parse source) options

    /// Parses the source and renders an offline HTML playback artifact in the Archify theme.
    static member RenderAnimated(source: string | null) =
        DiagramSource.RenderAnimated(source, SvgRenderOptions.Default)

    /// Parses the source and renders an offline HTML playback artifact with a theme and mode.
    static member RenderAnimated(source: string | null, options: SvgRenderOptions) =
        SourceReading.renderAnimated (DiagramSource.Parse source) options
