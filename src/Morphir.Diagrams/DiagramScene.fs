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
        diagnostics: ImmutableArray<Diagnostic>,
        annotations: (string * AnnotationItem list) list,
        structure: Structure,
        elements: Set<string>,
        capture: LayoutCapture option
    ) =
    internal new(document, connectors, options, diagnostics, annotations) =
        DiagramScene(document, connectors, options, diagnostics, annotations, { Nodes = [||]; Connections = [||] }, Set.empty, None)

    /// What the drawing pass recorded for the layout, when it recorded it.
    member internal _.Capture = capture

    /// The scene with family roles for the drawables of these references.
    member internal this.WithRoles(roles: (string * Layout.FamilyRole) list) =
        match capture with
        | Some recorded -> this.WithCapture { recorded with Roles = recorded.Roles @ roles }
        | None -> this

    member internal _.WithCapture(recorded: LayoutCapture) =
        DiagramScene(document, connectors, options, diagnostics, annotations, structure, elements, Some recorded)

    /// Warnings found while building the scene, such as metadata about elements this diagram lacks.
    member _.Diagnostics = diagnostics

    member internal _.WithDiagnostics(found: Diagnostic seq) =
        DiagramScene(document, connectors, options, ImmutableArray.CreateRange found, annotations, structure, elements, capture)

    /// The player's view of the metadata, by element reference.
    member internal _.Annotations = annotations

    member internal _.WithAnnotations(projected: (string * AnnotationItem list) list) =
        DiagramScene(document, connectors, options, diagnostics, projected, structure, elements, capture)

    /// The elements and connections that the player explores.
    member internal _.Structure = structure

    /// The element references of the diagram, which node visits may name.
    member internal _.Elements = elements

    member internal _.WithStructure(diagram: Diagram) =
        let references = DiagramElementRef.Enumerate diagram |> Seq.map (fun reference -> reference.ToString()) |> Set.ofSeq
        DiagramScene(document, connectors, options, diagnostics, annotations, Structures.ofDiagram diagram, references, capture)

    /// Script-free static document.
    member _.Document = document

    /// The theme and mode that the document was rendered with.
    member _.Options = options

    /// Connector bindings eligible for authored-order playback.
    member _.Connectors = connectors

    override _.ToString() =
        $"SVG scene with {connectors.Length} connector bindings"
