// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Export

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Morphir.Diagrams
open Morphir.Diagrams.Layout

/// What a renderer draws.
[<RequireQualifiedAccess>]
type RenderMode =
    /// The drawables of lowered pages. The default.
    | Drawables
    /// Each page's original SVG, unlowered. A renderer in this mode declares no features.
    | OriginalSvg

    /// For C# callers: every case needs a function.
    member this.Match(drawables: Func<'T>, originalSvg: Func<'T>) =
        match this with
        | RenderMode.Drawables -> drawables.Invoke()
        | RenderMode.OriginalSvg -> originalSvg.Invoke()

    override this.ToString() =
        match this with
        | RenderMode.Drawables -> "drawables"
        | RenderMode.OriginalSvg -> "original SVG"

/// How a list of pages renders for a renderer.
[<RequireQualifiedAccess; CustomEquality; NoComparison>]
type Assessment =
    /// No fallback applies.
    | Native
    /// Fallbacks apply; each degradation names its feature and the drawables it affects.
    | Degraded of degradations: ImmutableArray<Degradation>
    /// The renderer cannot draw the pages at all, for want of these features.
    | Unsupported of features: ImmutableArray<LayoutFeature>

    /// For C# callers: every case needs a function.
    member this.Match(native: Func<'T>, degraded: Func<ImmutableArray<Degradation>, 'T>, unsupported: Func<ImmutableArray<LayoutFeature>, 'T>) =
        match this with
        | Assessment.Native -> native.Invoke()
        | Assessment.Degraded degradations -> degraded.Invoke degradations
        | Assessment.Unsupported features -> unsupported.Invoke features

    override this.ToString() =
        match this with
        | Assessment.Native -> "native"
        | Assessment.Degraded degradations ->
            let listed = degradations |> Seq.map string |> String.concat "; "
            $"degraded: {listed}"
        | Assessment.Unsupported features ->
            let listed = features |> Seq.map string |> String.concat ", "
            $"unsupported: {listed}"

    override this.Equals(other: obj | null) =
        match other with
        | :? Assessment as other ->
            match this, other with
            | Assessment.Native, Assessment.Native -> true
            | Assessment.Degraded first, Assessment.Degraded second ->
                first.Length = second.Length && Seq.forall2 (fun (a: Degradation) b -> a.Equals b) first second
            | Assessment.Unsupported first, Assessment.Unsupported second -> Seq.toList first = Seq.toList second
            | _ -> false
        | _ -> false

    override this.GetHashCode() =
        match this with
        | Assessment.Native -> 0
        | Assessment.Degraded degradations -> HashCode.Combine(1, degradations.Length)
        | Assessment.Unsupported features -> HashCode.Combine(2, features.Length)

/// What a render does when the assessment is degraded.
[<RequireQualifiedAccess>]
type DegradationPolicy =
    /// Writes nothing and returns the degradations as errors.
    | Strict
    /// Writes the lowered pages and returns each degradation as a layout/degraded warning.
    | AllowDegraded
    /// Replaces each degraded page with one SVG image of it, when the renderer declares SVG images, with a
    /// layout/pictured warning; otherwise as AllowDegraded.
    | PictureFallback

    override this.ToString() =
        match this with
        | DegradationPolicy.Strict -> "strict"
        | DegradationPolicy.AllowDegraded -> "allow degraded"
        | DegradationPolicy.PictureFallback -> "picture fallback"

/// Draws render plans into a format. Subclass it in any assembly; the library creates the plans.
[<AbstractClass>]
type DiagramRenderer() =
    /// The media type of the output, such as application/vnd.jgraph.mxfile.
    abstract MediaType: string

    /// The file extension of the output, with its dot, such as .drawio. It may have several parts, such as .excalidraw.md.
    abstract FileExtension: string

    /// The layout features the renderer draws natively. Everything else is lowered before it receives a plan.
    abstract NativeFeatures: LayoutFeatures

    /// What the renderer draws: drawables by default, or each page's original SVG.
    abstract Mode: RenderMode
    default _.Mode = RenderMode.Drawables

    /// Writes a plan to the output and returns the renderer's own diagnostics. The output need not seek and is not
    /// closed. Call plan.EnsureFor(this) first.
    abstract RenderAsync: plan: RenderPlan * output: Stream * cancellationToken: CancellationToken -> Task<ImmutableArray<Diagnostic>>

    override this.ToString() = $"{this.GetType().Name} ({this.FileExtension})"

/// The pages to render, lowered for one renderer, with their assessment. Only the library creates plans.
and [<Sealed>] RenderPlan internal (pages: ImmutableArray<DiagramLayout>, assessment: Assessment, mode: RenderMode, features: LayoutFeatures) =
    /// The pages: lowered in the drawables mode, unlowered in the original-SVG mode.
    member _.Pages = pages

    /// How the pages render for the renderer the plan was made for.
    member _.Assessment = assessment

    /// The mode the plan was made for.
    member _.Mode = mode

    /// The features the pages were lowered to.
    member _.Features = features

    /// Throws an argument error unless the plan fits the renderer: the same mode, and features that the renderer
    /// declares. Renderers call it before writing.
    member _.EnsureFor(renderer: DiagramRenderer) =
        ArgumentNullException.ThrowIfNull renderer

        if renderer.Mode <> mode then
            invalidArg (nameof renderer) $"The plan was made for the {mode} mode, and the renderer draws {renderer.Mode}."

        if not (features.IsSubsetOf renderer.NativeFeatures) then
            let missing = features.Except renderer.NativeFeatures
            invalidArg (nameof renderer) $"The plan was lowered for features that the renderer does not declare: {missing}."

    override _.ToString() = $"plan of {pages.Length} pages, {assessment}"

/// What a render did: its assessment, every diagnostic of the assessment, the policy and the renderer, and whether
/// anything was written.
[<Sealed>]
type RenderResult internal (assessment: Assessment, diagnostics: ImmutableArray<Diagnostic>, written: bool) =
    /// How the pages rendered for the renderer.
    member _.Assessment = assessment

    /// Every diagnostic, in order.
    member _.Diagnostics = diagnostics

    /// Whether the renderer was called to write the output.
    member _.Written = written

    /// Whether any diagnostic is an error.
    member _.HasErrors = diagnostics |> Seq.exists (fun diagnostic -> diagnostic.Severity = DiagnosticSeverity.Error)

    override _.ToString() =
        let wrote = if written then "written" else "not written"
        $"{wrote}, {assessment}, {diagnostics.Length} diagnostics"

/// Visits drawables. Override the methods for the kinds you draw; elements, containers and groups visit their
/// children by default.
[<AbstractClass>]
type LayoutVisitor() =
    /// Visits a shape.
    abstract VisitShape: drawable: Drawable * shape: LayoutShape -> unit
    default _.VisitShape(_, _) = ()

    /// Visits a line.
    abstract VisitLine: drawable: Drawable * line: LayoutLine -> unit
    default _.VisitLine(_, _) = ()

    /// Visits free text.
    abstract VisitText: drawable: Drawable * text: LayoutText -> unit
    default _.VisitText(_, _) = ()

    /// Visits an image.
    abstract VisitImage: drawable: Drawable * image: LayoutImage -> unit
    default _.VisitImage(_, _) = ()

    /// Visits a composite element; by default, its children.
    abstract VisitElement: drawable: Drawable * children: ImmutableArray<Drawable> -> unit
    default this.VisitElement(_, children) = for child in children do this.Visit child

    /// Visits a container; by default, its children.
    abstract VisitContainer: drawable: Drawable * container: LayoutContainer -> unit
    default this.VisitContainer(_, container) = for child in container.Children do this.Visit child

    /// Visits a group; by default, its children.
    abstract VisitGroup: drawable: Drawable * children: ImmutableArray<Drawable> -> unit
    default this.VisitGroup(_, children) = for child in children do this.Visit child

    /// Calls the method for the drawable's kind.
    member this.Visit(drawable: Drawable) =
        ArgumentNullException.ThrowIfNull drawable

        match drawable.Kind with
        | DrawableKind.Shape shape -> this.VisitShape(drawable, shape)
        | DrawableKind.Line line -> this.VisitLine(drawable, line)
        | DrawableKind.Text text -> this.VisitText(drawable, text)
        | DrawableKind.Image image -> this.VisitImage(drawable, image)
        | DrawableKind.Element children -> this.VisitElement(drawable, children)
        | DrawableKind.Container container -> this.VisitContainer(drawable, container)
        | DrawableKind.Group children -> this.VisitGroup(drawable, children)

    /// Visits each root drawable of a layout.
    member this.Visit(layout: DiagramLayout) =
        ArgumentNullException.ThrowIfNull layout
        for drawable in layout.Drawables do this.Visit drawable

/// An immutable registry of renderers by file extension. Nothing is discovered by reflection.
[<Sealed>]
type DiagramRenderers private (renderers: ImmutableList<DiagramRenderer>) =
    static let empty = DiagramRenderers(ImmutableList.Empty)

    /// The renderers, in the order they were registered.
    member _.Renderers: IReadOnlyList<DiagramRenderer> = renderers

    /// The registry with a renderer added. A renderer for the same extension, ignoring case, is replaced.
    member _.Add(renderer: DiagramRenderer) =
        ArgumentNullException.ThrowIfNull renderer

        let kept =
            renderers.RemoveAll(fun existing -> String.Equals(existing.FileExtension, renderer.FileExtension, StringComparison.OrdinalIgnoreCase))

        DiagramRenderers(kept.Add renderer)

    /// The renderer whose extension is the longest that ends the file name, ignoring case, or null.
    member _.Find(fileName: string | null) : DiagramRenderer | null =
        match fileName with
        | null -> null
        | name ->
            renderers
            |> Seq.filter (fun renderer -> name.EndsWith(renderer.FileExtension, StringComparison.OrdinalIgnoreCase))
            |> Seq.sortByDescending (fun renderer -> renderer.FileExtension.Length)
            |> Seq.tryHead
            |> Option.toObj

    /// Finds the renderer for a file name, for C# callers. The renderer is set only when this returns true.
    member this.TryFind(fileName: string | null, [<Out; MaybeNullWhen(false)>] renderer: byref<DiagramRenderer>) =
        match this.Find fileName with
        | null ->
            renderer <- Unchecked.defaultof<_>
            false
        | found ->
            renderer <- found
            true

    override _.ToString() =
        let extensions = renderers |> Seq.map (fun renderer -> renderer.FileExtension) |> String.concat ", "
        $"renderers for {extensions}"

    /// The empty registry.
    static member Empty = empty
