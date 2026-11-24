// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Export

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Threading
open System.Threading.Tasks
open Morphir.Diagrams
open Morphir.Diagrams.Layout

/// Lays out diagrams, assesses layouts for renderers and renders them.
[<AbstractClass; Sealed>]
type DiagramExport private () =
    /// Lays out a diagram with render options, in the palette of their mode; auto reads as light, because editable
    /// targets open on a white canvas. A diagram that the SVG renderer rejects gives no layout and the same errors.
    static member Layout(diagram: Diagram, options: SvgRenderOptions) =
        ArgumentNullException.ThrowIfNull diagram
        ArgumentNullException.ThrowIfNull options
        let options = if options.Mode = ThemeMode.Auto then options.WithMode ThemeMode.Light else options

        match SvgRenderer.TryBuild(diagram, options) with
        | Error errors -> LayoutResult(None, errors)
        | Ok scene ->
            let layout, warnings = LayoutSink.layout scene
            LayoutResult(Some layout, scene.Diagnostics.AddRange warnings)

    /// Lays out a diagram with the default options, in the light palette.
    static member Layout(diagram: Diagram) = DiagramExport.Layout(diagram, SvgRenderOptions.Default)

    static member private Check(pages: IReadOnlyList<DiagramLayout>, renderer: DiagramRenderer) =
        ArgumentNullException.ThrowIfNull pages
        ArgumentNullException.ThrowIfNull renderer
        if pages.Count = 0 then invalidArg (nameof pages) "At least one page is required."
        if pages |> Seq.exists (fun page -> obj.ReferenceEquals(page, null)) then invalidArg (nameof pages) "A page is null."

    /// The plan for a renderer: the pages lowered to its features, and their assessment.
    static member internal Plan(pages: IReadOnlyList<DiagramLayout>, renderer: DiagramRenderer) : RenderPlan * ImmutableArray<Degradation> list =
        DiagramExport.Check(pages, renderer)

        match renderer.Mode with
        | RenderMode.OriginalSvg -> RenderPlan(ImmutableArray.CreateRange pages, Assessment.Native, RenderMode.OriginalSvg, LayoutFeatures.None), []
        | RenderMode.Drawables ->
            let features = renderer.NativeFeatures
            let lowered = pages |> Seq.map (Lowering.lower features) |> Seq.toList
            let perPage = lowered |> List.map (snd >> ImmutableArray.CreateRange)
            let all = perPage |> Seq.collect id |> ImmutableArray.CreateRange

            let assessment =
                if pages.Count > 1 && not (features.Contains LayoutFeature.Pages) then Assessment.Unsupported(ImmutableArray.Create LayoutFeature.Pages)
                elif all.IsEmpty then Assessment.Native
                else Assessment.Degraded all

            RenderPlan(lowered |> List.map fst |> ImmutableArray.CreateRange, assessment, RenderMode.Drawables, features), perPage

    /// How the pages would render for the renderer: native, degraded with each fallback, or unsupported.
    static member Assess(pages: IReadOnlyList<DiagramLayout>, renderer: DiagramRenderer) =
        (fst (DiagramExport.Plan(pages, renderer))).Assessment

    /// Assesses and lowers the pages for the renderer, applies the policy, and renders into the output, which need not
    /// seek and is flushed but not closed. Strict renders of degraded pages, and every render of unsupported pages,
    /// write nothing and return errors.
    static member RenderAsync
        (
            pages: IReadOnlyList<DiagramLayout>,
            renderer: DiagramRenderer,
            output: Stream,
            policy: DegradationPolicy,
            cancellationToken: CancellationToken
        ) : Task<RenderResult> =
        DiagramExport.Check(pages, renderer)
        ArgumentNullException.ThrowIfNull output
        ArgumentNullException.ThrowIfNull policy

        if cancellationToken.IsCancellationRequested then
            Task.FromCanceled<RenderResult> cancellationToken
        else
            let plan, perPage = DiagramExport.Plan(pages, renderer)

            let error (degradation: Degradation) = Diagnostic.Error("layout/degraded", degradation.ToString())
            let warning (degradation: Degradation) = Diagnostic.Warning("layout/degraded", degradation.ToString())

            let refuse (errors: Diagnostic seq) =
                Task.FromResult(RenderResult(plan.Assessment, ImmutableArray.CreateRange errors, false))

            match plan.Assessment, policy with
            | Assessment.Unsupported features, _ ->
                refuse [ for feature in features -> Diagnostic.Error("layout/unsupported", $"The renderer does not declare {feature}, which the pages need.") ]
            | Assessment.Degraded degradations, DegradationPolicy.Strict -> refuse (degradations |> Seq.map error)
            | assessment, _ ->
                let plan, notes =
                    match assessment, policy with
                    | Assessment.Degraded _, DegradationPolicy.PictureFallback when renderer.NativeFeatures.Contains LayoutFeature.SvgImages ->
                        let pictured =
                            List.zip (List.ofSeq pages) perPage
                            |> List.mapi (fun index (page, degradations) ->
                                if degradations.IsEmpty then
                                    plan.Pages[index], []
                                else
                                    let image: Drawable =
                                        { id = DrawableId.Create "page"
                                          reference = None
                                          paintOrder = 1
                                          kind = DrawableKind.Image(LayoutImage(page.Bounds, page.Svg))
                                          links = ImmutableArray.Empty }

                                    page.WithDrawables(ImmutableArray.Create image),
                                    [ Diagnostic.Warning("layout/pictured", $"Page {index + 1} is drawn as one SVG image, because it would be degraded.") ])

                        RenderPlan(pictured |> List.map fst |> ImmutableArray.CreateRange, assessment, plan.Mode, plan.Features), pictured |> List.collect snd
                    | Assessment.Degraded degradations, _ -> plan, degradations |> Seq.map warning |> Seq.toList
                    | _ -> plan, []

                task {
                    let! diagnostics = renderer.RenderAsync(plan, output, cancellationToken)
                    do! output.FlushAsync(cancellationToken)
                    return RenderResult(plan.Assessment, ImmutableArray.CreateRange(Seq.append notes diagnostics), true)
                }
