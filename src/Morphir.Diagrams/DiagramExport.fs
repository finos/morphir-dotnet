// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Export

open System
open System.Collections.Immutable
open Morphir.Diagrams
open Morphir.Diagrams.Layout

/// Lays out diagrams, assesses layouts for renderers and renders them.
[<AbstractClass; Sealed>]
type DiagramExport private () =
    /// Lays out a diagram with render options, in the palette of their mode; auto reads as light, because editable
    /// targets open on a white canvas. A diagram that the SVG renderer rejects gives no layout and the same errors.
    static member Layout(diagram: Diagram, options: SvgRenderOptions) =
        ArgumentNullException.ThrowIfNull diagram
        ArgumentNullException.ThrowIfNull options
        let options = if options.Mode = ThemeMode.Auto then options.WithMode ThemeMode.Light else options

        match SvgRenderer.TryBuild(diagram, options) with
        | Error errors -> LayoutResult(None, errors)
        | Ok scene ->
            let layout, warnings = LayoutSink.layout scene
            LayoutResult(Some layout, scene.Diagnostics.AddRange warnings)

    /// Lays out a diagram with the default options, in the light palette.
    static member Layout(diagram: Diagram) = DiagramExport.Layout(diagram, SvgRenderOptions.Default)
