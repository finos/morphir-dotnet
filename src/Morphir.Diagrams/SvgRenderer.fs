// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable

/// The family roles that a diagram's model gives its elements, for the layout.
[<RequireQualifiedAccess>]
module internal LayoutRoles =
    open Morphir.Diagrams.Layout

    let ofDiagram (diagram: Diagram) : (string * FamilyRole) list =
        match diagram with
        | Diagram.State model ->
            [
                for state in model.States do
                    let role =
                        match state.Role with
                        | StateRole.Initial -> Some FamilyRole.Initial
                        | StateRole.Final -> Some FamilyRole.Final
                        | StateRole.Choice -> Some FamilyRole.Choice
                        | StateRole.Fork
                        | StateRole.Join -> Some FamilyRole.ForkJoin
                        | StateRole.ShallowHistory
                        | StateRole.DeepHistory -> Some FamilyRole.History
                        | _ -> None

                    match role with
                    | Some role -> (DiagramElementRef.Node state.Id).ToString(), role
                    | None -> ()

                // The projection draws each [*] as its own marker node.
                match diagram.Graph with
                | Some graph ->
                    for node in graph.Nodes do
                        match node.Shape with
                        | FlowShape.FilledCircle -> (DiagramElementRef.Node node.Id).ToString(), FamilyRole.Initial
                        | FlowShape.FramedCircle -> (DiagramElementRef.Node node.Id).ToString(), FamilyRole.Final
                        | _ -> ()
                | None -> ()
            ]
        | _ -> []

/// Produces standalone, script-free SVG from a diagram model.
[<AbstractClass; Sealed>]
type SvgRenderer private () =
    /// The scene and its warnings, or the errors that prevent rendering.
    static member internal TryBuild(diagram: Diagram, options: SvgRenderOptions) : Result<DiagramScene, ImmutableArray<Diagnostic>> =
        ArgumentNullException.ThrowIfNull diagram
        ArgumentNullException.ThrowIfNull options

        // Mermaid accepts the reserved state `root` as an endpoint but draws no node for it.
        let reserved =
            match diagram with
            | Diagram.State model ->
                let states = model.States |> Seq.map (fun state -> state.Id) |> HashSet

                model.Transitions
                |> Seq.collect (fun transition -> [ transition.Source; transition.Target ])
                |> Seq.filter (fun endpoint -> endpoint.Value = "root" && not (states.Contains endpoint))
                |> Seq.truncate 1
                |> Seq.map (fun _ ->
                    Diagnostic("state/reserved-id", DiagnosticSeverity.Error, "The state identifier root is reserved for the top-level region.", None))
                |> Seq.toList
            | _ -> []

        match reserved @ RailroadSvg.validate diagram options, Carriage.prepare diagram options with
        | (_ :: _) as errors, _ -> Error(ImmutableArray.CreateRange errors)
        | [], Error errors -> Error(ImmutableArray.CreateRange errors)
        | [], Ok(metadata, warnings) ->
            let scene =
                match diagram, diagram.Graph with
                | Diagram.Sequence model, _ -> SequenceSvg.render options metadata model
                | Diagram.Railroad model, _ -> RailroadSvg.render options metadata model
                | Diagram.DataTree model, _ -> DataTreeSvg.render options metadata model
                | Diagram.Bpmn model, _ -> BpmnSvg.render options metadata model
                | _, Some graph -> FlowchartSvg.render options metadata graph
                | _, None -> invalidOp "Every family other than sequences has a graph."

            Ok(((scene.WithDiagnostics warnings).WithAnnotations(Carriage.project diagram options)).WithStructure(diagram).WithRoles(LayoutRoles.ofDiagram diagram))

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
