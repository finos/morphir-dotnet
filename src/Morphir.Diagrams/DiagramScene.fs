// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System.Collections.Immutable
open Morphir.Svg

/// A connector occurrence bound to the exact path emitted by layout.
[<Sealed>]
type DiagramConnector
    internal (ordinal: int, source: DiagramId, target: DiagramId, data: string, reversed: bool) =
    /// Zero-based index of the connector in paint order.
    member _.Ordinal = ordinal

    /// Identifier of the authored first endpoint.
    member _.Source = source

    /// Identifier of the authored second endpoint.
    member _.Target = target

    /// SVG path data in the static diagram.
    member _.PathData = data

    /// Whether authored-order playback traverses the path from its end.
    member _.IsReversed = reversed

    override _.ToString() =
        $"Connector {ordinal} from {source} to {target}"

/// An SVG document and its connector occurrences.
[<Sealed>]
type DiagramScene
    internal
    (
        document: SvgDocument,
        connectors: ImmutableArray<DiagramConnector>,
        options: SvgRenderOptions,
        diagnostics: ImmutableArray<Diagnostic>
    ) =
    /// Warnings found while building the scene, such as metadata about elements this diagram lacks.
    member _.Diagnostics = diagnostics

    member internal _.WithDiagnostics(found: Diagnostic seq) =
        DiagramScene(document, connectors, options, ImmutableArray.CreateRange found)

    /// Script-free static document.
    member _.Document = document

    /// The theme and mode that the document was rendered with.
    member _.Options = options

    /// Connector bindings eligible for authored-order playback.
    member _.Connectors = connectors

    override _.ToString() =
        $"SVG scene with {connectors.Length} connector bindings"
