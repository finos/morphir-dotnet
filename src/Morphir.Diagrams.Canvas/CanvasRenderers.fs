// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Canvas

open Morphir.Diagrams.Export

/// The canvas renderers of this package.
[<AbstractClass; Sealed>]
type CanvasRenderers private () =
    /// A registry with every canvas renderer of this package, by file extension.
    static member Registry =
        DiagramRenderers.Empty.Add(DrawIoRenderer()).Add(ExcalidrawRenderer()).Add(ObsidianExcalidrawRenderer())
