// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open Morphir.Bpmn

/// Where a BPMN view's geometry comes from.
[<RequireQualifiedAccess>]
type BpmnGeometry =
    /// The document's diagram interchange when it places every shape and connection of the view; otherwise a full
    /// automatic layout, with a warning. Supplied and automatic geometry are never mixed.
    | PreferSupplied
    /// The document's diagram interchange; a view it does not place completely is an error.
    | RequireSupplied
    /// An automatic layout, whatever the document supplies.
    | Automatic

    /// The policy as documentation writes it.
    member this.WireValue =
        match this with
        | BpmnGeometry.PreferSupplied -> "prefer-supplied"
        | BpmnGeometry.RequireSupplied -> "require-supplied"
        | BpmnGeometry.Automatic -> "automatic"

    override this.ToString() = this.WireValue

/// A BPMN process or collaboration view, placed and ready to draw.
[<Sealed>]
type BpmnDiagram internal (document: BpmnDocument, view: BpmnElement, scene: BpmnScene, warnings: ImmutableArray<Diagnostic>) =
    let projection = lazy (BpmnLayout.projection scene)

    static let fail code message = Diagnostic(code, DiagnosticSeverity.Error, message, None)

    /// Selects a view and places it. A null view selects automatically: the plane, else the collaboration, else the
    /// process, and several candidates of one kind are an ambiguity. Returns a Result for F# callers.
    static member Create(document: BpmnDocument, view: string | null, geometry: BpmnGeometry) : Result<BpmnDiagram, ImmutableArray<Diagnostic>> =
        ArgumentNullException.ThrowIfNull document
        ArgumentNullException.ThrowIfNull geometry

        match BpmnLayout.select document (Option.ofObj view) with
        | Error(code, message) -> Error(ImmutableArray.Create(fail code message))
        | Ok selected ->
            let shapes, lines, problems = BpmnLayout.contents document selected

            if problems.Count > 0 then
                Error(problems |> Seq.map (fail "bpmn/undrawable-element") |> ImmutableArray.CreateRange)
            else
                let supplied () =
                    BpmnLayout.supplied document selected shapes lines
                    |> Option.map (fun (placed, routed, expanded) ->
                        let boxes =
                            placed
                            |> List.map (fun (element: BpmnElement, x, y, width, height) ->
                                let container = element.Kind = "participant" || element.Kind = "lane" || element.Kind = "group" || (BpmnLayout.isSubProcess element.Kind && expanded element)

                                {
                                    Id = (match element.Id with | null -> "" | id -> id)
                                    Element = element
                                    Kind = element.Kind
                                    X = x
                                    Y = y
                                    Width = width
                                    Height = height
                                    Label = BpmnLayout.label element
                                    Container = container
                                    Expanded = container
                                })

                        let connections =
                            routed
                            |> List.map (fun (line: BpmnElement, points) ->
                                let source, target = BpmnLayout.ends document line
                                let name (found: BpmnElement option) = found |> Option.bind (fun element -> Option.ofObj element.Id) |> Option.defaultValue ""

                                {
                                    Id = (match line.Id with | null -> "" | id -> id)
                                    Element = line
                                    Kind = line.Kind
                                    Source = name source
                                    Target = name target
                                    Points = points
                                    Label = BpmnLayout.label line
                                })

                        let ordered = (boxes |> List.filter (fun box -> box.Container)) @ (boxes |> List.filter (fun box -> not box.Container))
                        let right = (boxes |> List.fold (fun widest box -> max widest box.Right) 0.0, connections) ||> List.fold (fun widest line -> line.Points |> List.fold (fun wide (x, _) -> max wide x) widest)
                        let bottom = (boxes |> List.fold (fun lowest box -> max lowest box.Bottom) 0.0, connections) ||> List.fold (fun lowest line -> line.Points |> List.fold (fun low (_, y) -> max low y) lowest)

                        {
                            Boxes = ordered
                            Lines = connections
                            Width = right + 20.0
                            Height = bottom + 20.0
                            Supplied = true
                        })

                let automatic () = BpmnLayout.automatic document selected lines
                let name = match selected.Id with | null -> selected.Kind | id -> id

                match geometry with
                | BpmnGeometry.Automatic -> Ok(BpmnDiagram(document, selected, automatic (), ImmutableArray.Empty))
                | BpmnGeometry.RequireSupplied ->
                    match supplied () with
                    | Some scene -> Ok(BpmnDiagram(document, selected, scene, ImmutableArray.Empty))
                    | None -> Error(ImmutableArray.Create(fail "bpmn/missing-geometry" $"The diagram interchange does not place every shape and connection of {name}."))
                | BpmnGeometry.PreferSupplied ->
                    match supplied () with
                    | Some scene -> Ok(BpmnDiagram(document, selected, scene, ImmutableArray.Empty))
                    | None ->
                        let warning =
                            Diagnostic(
                                "bpmn/automatic-geometry",
                                DiagnosticSeverity.Warning,
                                $"The diagram interchange does not place all of {name}, so the whole view is laid out automatically.",
                                None
                            )

                        Ok(BpmnDiagram(document, selected, automatic (), ImmutableArray.Create warning))

    /// Selects and places a view for C# callers: true with the diagram, or false with the diagnostics.
    static member TryCreate
        (
            document: BpmnDocument,
            view: string | null,
            geometry: BpmnGeometry,
            [<Out; MaybeNullWhen(false)>] diagram: byref<BpmnDiagram>,
            [<Out>] diagnostics: byref<ImmutableArray<Diagnostic>>
        ) =
        match BpmnDiagram.Create(document, view, geometry) with
        | Ok created ->
            diagram <- created
            diagnostics <- created.Diagnostics
            true
        | Error errors ->
            diagram <- Unchecked.defaultof<_>
            diagnostics <- errors
            false

    /// The document the view comes from.
    member _.Document = document

    /// The process or collaboration drawn.
    member _.View = view

    /// Whether the geometry is the document's diagram interchange, kept as given.
    member _.UsesSuppliedGeometry = scene.Supplied

    /// Warnings, such as a fallback to automatic layout.
    member _.Diagnostics = warnings

    member internal _.Scene = scene

    /// The graph that references, layout and the player use: its nodes, groups and edges are this diagram's elements.
    member internal _.Projection = projection.Value

    override _.ToString() =
        let name = match view.Id with | null -> view.Kind | id -> id
        $"BPMN {view.Kind} {name}"
