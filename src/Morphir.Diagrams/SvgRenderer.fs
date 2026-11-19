// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Collections.Immutable

/// Produces standalone, script-free SVG from a diagram model.
[<AbstractClass; Sealed>]
type SvgRenderer private () =
    /// Lays out a diagram and retains bindings to its rendered connector paths.
    static member BuildScene(diagram: Diagram) =
        match diagram with
        | Diagram.Flowchart model -> FlowchartSvg.render model
        | Diagram.Sequence model -> SequenceSvg.render model

    /// Renders a supported diagram. The SVG embeds its own presentation.
    static member Render(diagram: Diagram) =
        let scene = SvgRenderer.BuildScene diagram

        RenderedDiagram(
            "image/svg+xml",
            Some(scene.Document.Render()),
            ImmutableArray<Diagnostic>.Empty
        )
