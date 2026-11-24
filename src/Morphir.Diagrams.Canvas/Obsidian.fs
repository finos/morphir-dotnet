// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Canvas

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open Morphir.Diagrams.Export
open Morphir.Diagrams.Layout

[<RequireQualifiedAccess>]
module internal ObsidianWriting =
    /// A line that ends like a block reference, which the plugin would read as an entry of its own.
    let blockReference = Regex(@" \^[A-Za-z0-9]{8}$", RegexOptions.CultureInvariant)

/// Writes a layout as a Markdown note that the Obsidian Excalidraw plugin opens: front matter that marks it as an
/// Excalidraw file, the text elements, and the Excalidraw scene in a JSON drawing block. A note is one file, so images
/// are not declared: paths and icons reach it as rectangles.
[<Sealed>]
type ObsidianExcalidrawRenderer(roughness: int) =
    inherit DiagramRenderer()
    do ExcalidrawWriting.checkRoughness roughness

    static let features = ExcalidrawWriting.features.Remove LayoutFeature.SvgImages

    /// A renderer with no hand-drawn roughness.
    new() = ObsidianExcalidrawRenderer(0)

    /// The hand-drawn roughness of every shape and arrow: 0, 1 or 2.
    member _.Roughness = roughness

    /// text/markdown.
    override _.MediaType = "text/markdown"

    /// .excalidraw.md.
    override _.FileExtension = ".excalidraw.md"

    /// The Excalidraw features except SVG images.
    override _.NativeFeatures = features

    /// Writes the plan's page as an Obsidian Excalidraw note.
    override this.RenderAsync(plan: RenderPlan, output: Stream, cancellationToken: CancellationToken) =
        ArgumentNullException.ThrowIfNull plan
        ArgumentNullException.ThrowIfNull output
        plan.EnsureFor this

        let json = new MemoryStream()

        let scene =
            use writer = new Utf8JsonWriter(json, ExcalidrawWriting.options)
            ExcalidrawWriting.write writer plan.Pages[0] roughness

        let note = StringBuilder()
        note.Append("---\n\nexcalidraw-plugin: parsed\ntags: [excalidraw]\n\n---\n\n# Excalidraw Data\n\n## Text Elements\n") |> ignore

        for element in scene.Elements do
            match element.Text with
            | Some(text, _, _, _) when not (text.Split('\n') |> Array.exists ObsidianWriting.blockReference.IsMatch) ->
                note.Append(text).Append(" ^").Append(element.Id).Append("\n\n") |> ignore
            | _ -> ()

        note.Append("%%\n## Drawing\n```json\n").Append(Encoding.UTF8.GetString(json.ToArray())).Append("\n```\n%%\n") |> ignore
        json.Dispose()
        let buffer = new MemoryStream(UTF8Encoding(false).GetBytes(note.ToString()))
        ExcalidrawWriting.copy buffer output cancellationToken
