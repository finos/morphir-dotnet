// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

/// A BPMN process or collaboration view.
[<Sealed>]
type BpmnDiagram internal (projection: Flowchart) =
    /// The graph that references, layout and the player use: its nodes, groups and edges are this diagram's elements.
    member internal _.Projection = projection

    override _.ToString() = "BPMN diagram"
