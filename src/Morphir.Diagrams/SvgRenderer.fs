// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable

/// Produces standalone, script-free SVG from a diagram model.
[<AbstractClass; Sealed>]
type SvgRenderer private () =
    /// Lays out a diagram in the Archify theme and retains bindings to its rendered connector paths.
    static member BuildScene(diagram: Diagram) =
        SvgRenderer.BuildScene(diagram, SvgRenderOptions.Default)

    /// Lays out a diagram with a theme and mode and retains bindings to its rendered connector paths.
    static member BuildScene(diagram: Diagram, options: SvgRenderOptions) =
        ArgumentNullException.ThrowIfNull options

        match diagram with
        | Diagram.Flowchart model -> FlowchartSvg.render options model
        | Diagram.Sequence model -> SequenceSvg.render options model

    /// Renders a supported diagram in the Archify theme. The SVG embeds its own presentation.
    static member Render(diagram: Diagram) =
        SvgRenderer.Render(diagram, SvgRenderOptions.Default)

    /// Renders a supported diagram with a theme and mode. The SVG embeds its own presentation.
    static member Render(diagram: Diagram, options: SvgRenderOptions) =
        let scene = SvgRenderer.BuildScene(diagram, options)

        RenderedDiagram(
            "image/svg+xml",
            Some(scene.Document.Render()),
            ImmutableArray<Diagnostic>.Empty
        )
