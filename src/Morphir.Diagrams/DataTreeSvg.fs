// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

[<RequireQualifiedAccess>]
module internal DataTreeSvg =
    /// Paints the diagram. Until its own layout lands, the projection is drawn as a graph.
    let render (options: SvgRenderOptions) (metadata: Morphir.Svg.SvgNode list option) (model: DataTreeDiagram) =
        FlowchartSvg.render options metadata model.Projection
