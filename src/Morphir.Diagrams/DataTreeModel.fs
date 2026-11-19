// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

/// A data tree of a PlantUML JSON or YAML document.
[<Sealed>]
type DataTreeDiagram internal (projection: Flowchart) =
    /// The graph that references, layout and the player use: its nodes, groups and edges are this diagram's elements.
    member internal _.Projection = projection

    override _.ToString() = "Data tree"
