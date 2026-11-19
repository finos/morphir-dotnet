// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable

/// Produces standalone, script-free SVG from a diagram model.
[<AbstractClass; Sealed>]
type SvgRenderer private () =
    /// The scene and its warnings, or the errors that prevent rendering.
    static member internal TryBuild(diagram: Diagram, options: SvgRenderOptions) : Result<DiagramScene, ImmutableArray<Diagnostic>> =
        ArgumentNullException.ThrowIfNull diagram
        ArgumentNullException.ThrowIfNull options

        match Carriage.prepare diagram options with
        | Error errors -> Error(ImmutableArray.CreateRange errors)
        | Ok(metadata, warnings) ->
            let scene =
                match diagram with
                | Diagram.Flowchart model -> FlowchartSvg.render options metadata model
                | Diagram.Sequence model -> SequenceSvg.render options metadata model

            Ok(scene.WithDiagnostics warnings)

    /// Lays out a diagram in the Archify theme and retains bindings to its rendered connector paths.
    static member BuildScene(diagram: Diagram) =
        SvgRenderer.BuildScene(diagram, SvgRenderOptions.Default)

    /// Lays out a diagram with a theme, mode and optional metadata, and retains bindings to its rendered
    /// connector paths. The scene's Diagnostics hold warnings. Throws when the metadata cannot be
    /// embedded; Render reports the same errors as diagnostics instead.
    static member BuildScene(diagram: Diagram, options: SvgRenderOptions) =
        match SvgRenderer.TryBuild(diagram, options) with
        | Ok scene -> scene
        | Error errors -> invalidArg (nameof options) (errors |> Seq.map (fun error -> error.ToString()) |> String.concat " ")

    /// Renders a supported diagram in the Archify theme. The SVG embeds its own presentation.
    static member Render(diagram: Diagram) =
        SvgRenderer.Render(diagram, SvgRenderOptions.Default)

    /// Renders a supported diagram with a theme, mode and optional metadata. Errors give no output.
    static member Render(diagram: Diagram, options: SvgRenderOptions) =
        match SvgRenderer.TryBuild(diagram, options) with
        | Ok scene -> RenderedDiagram("image/svg+xml", Some(scene.Document.Render()), scene.Diagnostics)
        | Error errors -> RenderedDiagram("image/svg+xml", None, errors)
