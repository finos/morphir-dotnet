// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Raster

open System
open System.Collections.Generic
open System.Collections.Immutable
open Morphir.Diagrams

/// A still image format.
[<RequireQualifiedAccess>]
type RasterFormat =
    /// PNG with alpha. The default.
    | Png
    /// Baseline JPEG on an opaque background.
    | Jpeg

    /// The format as documentation writes it.
    member this.WireValue =
        match this with
        | RasterFormat.Png -> "png"
        | RasterFormat.Jpeg -> "jpeg"

    override this.ToString() = this.WireValue

/// Which palette of an adaptive SVG an export uses. A fixed-mode SVG has one palette, which both choose.
[<RequireQualifiedAccess>]
type RasterTheme =
    /// The dark palette.
    | Dark
    /// The light palette.
    | Light

    override this.ToString() =
        match this with
        | RasterTheme.Dark -> "dark"
        | RasterTheme.Light -> "light"

/// An opaque colour.
[<Sealed>]
type RasterColor private (r: int, g: int, b: int) =
    /// A colour from 8-bit channels.
    static member Create(red: int, green: int, blue: int) =
        for value in [ red; green; blue ] do
            if value < 0 || value > 255 then raise (ArgumentOutOfRangeException(nameof value, "A channel is 0 to 255."))

        RasterColor(red, green, blue)

    /// White.
    static member White = RasterColor(255, 255, 255)

    /// The red channel, 0 to 255.
    member _.Red = r
    /// The green channel, 0 to 255.
    member _.Green = g
    /// The blue channel, 0 to 255.
    member _.Blue = b
    member internal _.Rgba = { R = r; G = g; B = b; A = 255 }
    override _.ToString() = $"#{r:x2}{g:x2}{b:x2}"

/// A finding that stops an export: invalid sizes, a limit, a missing glyph or unreadable SVG.
[<Sealed>]
type RasterDiagnostic internal (code: string, message: string) =
    /// A stable code such as `raster/pixel-limit` or `raster/missing-glyph`.
    member _.Code = code
    /// What went wrong.
    member _.Message = message
    override _.ToString() = $"{code} {message}"

/// Options for a still export. Width, height and scale keep the aspect ratio; the last one set wins.
[<Sealed>]
type RasterOptions private (format: RasterFormat, size: Choice<int, int, double>, quality: int, background: RasterColor option, theme: RasterTheme) =
    /// PNG at the diagram's natural size, in its first palette.
    static member Png = RasterOptions(RasterFormat.Png, Choice3Of3 1.0, 90, None, RasterTheme.Dark)

    /// JPEG at a quality from 1 to 100, on white unless a background is set.
    static member Jpeg(quality: int) =
        if quality < 1 || quality > 100 then raise (ArgumentOutOfRangeException(nameof quality, "JPEG quality is 1 to 100."))
        RasterOptions(RasterFormat.Jpeg, Choice3Of3 1.0, quality, None, RasterTheme.Dark)

    /// A copy whose width is fixed in pixels.
    member _.WithWidth(width: int) =
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero width
        RasterOptions(format, Choice1Of3 width, quality, background, theme)

    /// A copy whose height is fixed in pixels.
    member _.WithHeight(height: int) =
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero height
        RasterOptions(format, Choice2Of3 height, quality, background, theme)

    /// A copy at a scale of the natural size, above 0 and at most 16.
    member _.WithScale(scale: double) =
        if not (scale > 0.0 && scale <= 16.0) then raise (ArgumentOutOfRangeException(nameof scale, "The scale is above 0 and at most 16."))
        RasterOptions(format, Choice3Of3 scale, quality, background, theme)

    /// A copy painted over an opaque background.
    member _.WithBackground(color: RasterColor) =
        ArgumentNullException.ThrowIfNull color
        RasterOptions(format, size, quality, Some color, theme)

    /// A copy that uses a palette of an adaptive SVG.
    member _.WithTheme(value: RasterTheme) =
        ArgumentNullException.ThrowIfNull value
        RasterOptions(format, size, quality, background, value)

    /// The image format.
    member _.Format = format
    /// The JPEG quality, 1 to 100; PNG ignores it.
    member _.Quality = quality
    /// The opaque background, or null for the image as drawn (JPEG then uses white).
    member _.Background: RasterColor | null = Option.toObj background
    /// The palette of an adaptive SVG.
    member _.Theme = theme
    member internal _.Size = size
    override _.ToString() = $"{format}, {theme} theme"

/// Options for an animated GIF.
[<Sealed>]
type GifOptions private (duration: int, rate: int, scale: double, background: RasterColor, loop: bool, theme: RasterTheme) =
    /// A GIF over `durationMilliseconds` (1 to 60,000) at `framesPerSecond` (1 to 50), looping, at natural size.
    static member Create(durationMilliseconds: int, framesPerSecond: int) =
        if durationMilliseconds < 1 || durationMilliseconds > 60_000 then raise (ArgumentOutOfRangeException(nameof durationMilliseconds, "The interval is 1 to 60,000 ms."))
        if framesPerSecond < 1 || framesPerSecond > 50 then raise (ArgumentOutOfRangeException(nameof framesPerSecond, "The frame rate is 1 to 50."))
        GifOptions(durationMilliseconds, framesPerSecond, 1.0, RasterColor.White, true, RasterTheme.Dark)

    /// A copy at a scale, above 0 and at most 4.
    member _.WithScale(value: double) =
        if not (value > 0.0 && value <= 4.0) then raise (ArgumentOutOfRangeException(nameof value, "The scale is above 0 and at most 4."))
        GifOptions(duration, rate, value, background, loop, theme)

    /// A copy on another opaque background.
    member _.WithBackground(color: RasterColor) =
        ArgumentNullException.ThrowIfNull color
        GifOptions(duration, rate, scale, color, loop, theme)

    /// A copy that plays once.
    member _.PlayOnce() = GifOptions(duration, rate, scale, background, false, theme)

    /// A copy that uses a palette of an adaptive SVG.
    member _.WithTheme(value: RasterTheme) =
        ArgumentNullException.ThrowIfNull value
        GifOptions(duration, rate, scale, background, loop, value)

    /// The interval the frames sample, in milliseconds.
    member _.DurationMilliseconds = duration
    /// Frames per second of logical time.
    member _.FramesPerSecond = rate
    /// Whether the GIF loops forever.
    member _.Loop = loop
    member internal _.Scale = scale
    member internal _.BackgroundColor = background
    member internal _.ThemeValue = theme
    override _.ToString() = $"GIF over {duration} ms at {rate} frames a second"

/// An encoded image, or the diagnostics that stopped it.
[<Sealed>]
type RasterImage internal (mediaType: string, width: int, height: int, bytes: byte[] option, diagnostics: ImmutableArray<RasterDiagnostic>) =
    /// `image/png`, `image/jpeg` or `image/gif`.
    member _.MediaType = mediaType
    /// The width in pixels, or 0 on failure.
    member _.Width = width
    /// The height in pixels, or 0 on failure.
    member _.Height = height
    /// The encoded bytes; empty on failure.
    member _.Bytes = match bytes with | Some value -> ImmutableArray.Create<byte>(value) | None -> ImmutableArray<byte>.Empty
    /// Whether an image was made.
    member _.Succeeded = bytes.IsSome
    /// Diagnostics that stopped the export.
    member _.Diagnostics = diagnostics
    override _.ToString() = if bytes.IsSome then $"{mediaType} {width}x{height}" else $"{mediaType}, {diagnostics.Length} diagnostics"

/// An offline player page with SVG, PNG, JPEG and GIF downloads, and the diagnostics of any export it left out.
[<Sealed>]
type RasterPlayer internal (html: string, diagnostics: ImmutableArray<RasterDiagnostic>) =
    /// The HTML page.
    member _.Html = html
    /// Why an export is missing, such as a missing glyph; the page then offers the formats that worked.
    member _.Diagnostics = diagnostics
    override _.ToString() = $"Player page, {html.Length} UTF-16 code units"

/// Converts the library's SVG to PNG and JPEG, and a playback plan to an animated GIF, in managed code. Equal input
/// gives identical bytes on every platform.
[<AbstractClass; Sealed>]
type DiagramRaster private () =
    static let pixelLimit = 40_000_000L
    static let workLimit = 400_000_000L
    static let frameLimit = 600

    static let failed mediaType code message =
        RasterImage(mediaType, 0, 0, None, ImmutableArray.Create(RasterDiagnostic(code, message)))

    /// Rasterises SVG text into a canvas of a size, or a failure.
    static let raster (source: string) (sizeOf: double -> double -> int * int) (dark: bool) =
        let document =
            try
                Ok(SvgPainter.load source)
            with error ->
                Error("raster/unreadable-svg", error.Message)

        match document with
        | Error failure -> Error failure
        | Ok document ->
            let x, y, width, height = SvgPainter.viewBox document

            if width <= 0.0 || height <= 0.0 then
                Error("raster/no-size", "The SVG has no viewBox or size.")
            else
                let pixelsWide, pixelsHigh = sizeOf width height

                if pixelsWide < 1 || pixelsHigh < 1 then
                    Error("raster/no-size", "The image would have no pixels.")
                elif int64 pixelsWide * int64 pixelsHigh > pixelLimit then
                    Error("raster/pixel-limit", $"{pixelsWide} by {pixelsHigh} pixels is more than the limit of {pixelLimit}.")
                else
                    let canvas = Canvas(pixelsWide, pixelsHigh)
                    let device = Affine.Scale(float pixelsWide / width, float pixelsHigh / height).Then(Affine.Translate(-x, -y))
                    let work = ref 0L

                    try
                        SvgPainter.paint document canvas device (SvgPainter.variables document dark) work workLimit
                        Ok(canvas, document, device)
                    with RasterFailure(code, message) ->
                        Error(code, message)

    /// Exports SVG written by Morphir.Diagrams as a still image.
    static member Render(svg: string | null, options: RasterOptions) =
        ArgumentNullException.ThrowIfNull options
        let mediaType = if options.Format = RasterFormat.Jpeg then "image/jpeg" else "image/png"

        match svg with
        | null -> failed mediaType "raster/unreadable-svg" "No SVG was given."
        | svg ->
            let sizeOf (width: double) (height: double) =
                let round (value: double) = int (Math.Min(1e9, Math.Round(value, MidpointRounding.ToEven)))

                match options.Size with
                | Choice1Of3 fixedWidth -> fixedWidth, max 1 (round (height * float fixedWidth / width))
                | Choice2Of3 fixedHeight -> max 1 (round (width * float fixedHeight / height)), fixedHeight
                | Choice3Of3 scale -> max 1 (round (width * scale)), max 1 (round (height * scale))

            match raster svg sizeOf (options.Theme = RasterTheme.Dark) with
            | Error(code, message) -> failed mediaType code message
            | Ok(canvas, _, _) ->
                let background = match options.Background with | null -> None | color -> Some color.Rgba

                let bytes =
                    match options.Format with
                    | RasterFormat.Jpeg -> Encoders.jpeg canvas options.Quality (background |> Option.defaultValue RasterColor.White.Rgba)
                    | RasterFormat.Png ->
                        match background with
                        | Some color ->
                            let flat = Canvas(canvas.Width, canvas.Height)
                            flat.Clear color
                            // Composite the image over the background.
                            for index in 0 .. canvas.Width * canvas.Height - 1 do
                                let at = index * 4
                                let a = int canvas.Pixels[at + 3]
                                for channel in 0..2 do
                                    flat.Pixels[at + channel] <- byte ((int canvas.Pixels[at + channel] * a + int flat.Pixels[at + channel] * (255 - a) + 127) / 255)

                            Encoders.png flat
                        | None -> Encoders.png canvas

                RasterImage(mediaType, canvas.Width, canvas.Height, Some bytes, ImmutableArray.Empty)

    /// Exports a rendered scene as a still image.
    static member Render(scene: DiagramScene, options: RasterOptions) =
        ArgumentNullException.ThrowIfNull scene
        DiagramRaster.Render(scene.Document.Render(), options)

    /// Encodes the scene's playback as an animated GIF: frames sampled at logical times over the interval, each step
    /// drawn as a signal moving along its connector, so repeated and concurrent steps all show.
    static member RenderGif(scene: DiagramScene, steps: IReadOnlyList<PlaybackStep>, options: GifOptions) =
        ArgumentNullException.ThrowIfNull scene
        ArgumentNullException.ThrowIfNull steps
        ArgumentNullException.ThrowIfNull options
        let frames = int ((int64 options.DurationMilliseconds * int64 options.FramesPerSecond + 999L) / 1000L)

        if frames > frameLimit then
            failed "image/gif" "raster/frame-limit" $"{frames} frames is more than the limit of {frameLimit}."
        else
            let sizeOf (width: double) (height: double) =
                max 1 (int (Math.Round(width * options.Scale, MidpointRounding.ToEven))), max 1 (int (Math.Round(height * options.Scale, MidpointRounding.ToEven)))

            let source = scene.Document.Render()

            match raster source sizeOf (options.ThemeValue = RasterTheme.Dark) with
            | Error(code, message) -> failed "image/gif" code message
            | Ok(canvas, document, device) ->
                if int64 canvas.Width * int64 canvas.Height * int64 frames > pixelLimit * 4L then
                    failed "image/gif" "raster/pixel-limit" "The frames hold more pixels than the limit allows."
                else
                    let vars = SvgPainter.variables document (options.ThemeValue = RasterTheme.Dark)
                    let accent = match vars.TryGetValue "cloud-stroke" with | true, value -> value | _ -> "#f5b301"
                    let width, height = canvas.Width, canvas.Height
                    let ns = "http://www.w3.org/2000/svg"
                    let x, y, w, h = SvgPainter.viewBox document

                    try
                        let images =
                            [
                                for frame in 0 .. frames - 1 do
                                    let time = int64 frame * 1000L / int64 options.FramesPerSecond
                                    let copy = Canvas(width, height)
                                    Array.blit canvas.Pixels 0 copy.Pixels 0 canvas.Pixels.Length

                                    // Each active step: the travelled part of its path, and a dot at its front.
                                    let overlay = Text.StringBuilder($"<svg xmlns=\"{ns}\" viewBox=\"{x} {y} {w} {h}\">")

                                    for step in steps do
                                        if time >= step.StartMilliseconds && time < step.StartMilliseconds + step.DurationMilliseconds then
                                            let progress = float (time - step.StartMilliseconds) / float step.DurationMilliseconds
                                            let lines = Geometry.parse step.Connector.PathData |> Seq.filter (fun line -> line.Points.Count >= 2) |> Seq.toList

                                            match lines with
                                            | line :: _ ->
                                                let points = List.ofSeq line.Points
                                                let points = if step.Connector.IsReversed then List.rev points else points
                                                let lengths = points |> List.pairwise |> List.map (fun ((ax, ay), (bx, by)) -> Math.Sqrt((bx - ax) ** 2.0 + (by - ay) ** 2.0))
                                                let total = List.sum lengths
                                                let mutable target = total * progress
                                                let mutable front = points.Head

                                                for ((ax, ay), (bx, by)), length in List.zip (List.pairwise points) lengths do
                                                    if target > 0.0 then
                                                        let t = if length = 0.0 then 1.0 else min 1.0 (target / length)
                                                        front <- ax + (bx - ax) * t, ay + (by - ay) * t
                                                        target <- target - length

                                                let data = "M" + (points |> List.map (fun (px, py) -> FormattableString.Invariant($"{px},{py}")) |> String.concat " L")
                                                let shown = FormattableString.Invariant($"{total * progress} {total + 1.0}")
                                                overlay.Append($"<path d=\"{data}\" fill=\"none\" stroke=\"{accent}\" stroke-width=\"3\" stroke-opacity=\"0.6\" stroke-dasharray=\"{shown}\"/>") |> ignore
                                                let fx, fy = front
                                                overlay.Append(FormattableString.Invariant($"<circle cx=\"{fx}\" cy=\"{fy}\" r=\"4.5\" fill=\"{accent}\"/>")) |> ignore
                                            | [] -> ()

                                    overlay.Append("</svg>") |> ignore
                                    SvgPainter.paint (SvgPainter.load (overlay.ToString())) copy device vars (ref 0L) workLimit
                                    copy
                            ]

                        let bytes = Encoders.gif images (max 2 (100 / options.FramesPerSecond)) options.Loop options.BackgroundColor.Rgba
                        RasterImage("image/gif", width, height, Some bytes, ImmutableArray.Empty)
                    with RasterFailure(code, message) ->
                        failed "image/gif" code message

    /// Encodes the scene's authored playback, one connector a second, as an animated GIF.
    static member RenderGif(scene: DiagramScene, options: GifOptions) =
        ArgumentNullException.ThrowIfNull scene
        let steps = scene.Connectors |> Seq.mapi (fun index connector -> PlaybackStep.Create(connector, int64 index * 1000L, 1000L)) |> Seq.toArray
        DiagramRaster.RenderGif(scene, steps, options)

    /// The offline player of the scene's authored playback, with a format picker beside its download button. The PNG,
    /// JPEG and GIF are made here at the diagram's natural size, so downloads ignore zoom and pan and never change
    /// playback.
    static member RenderPlayer(scene: DiagramScene) =
        ArgumentNullException.ThrowIfNull scene
        let rendered = AnimatedRenderer.Render scene

        match rendered.Output with
        | null -> RasterPlayer("", ImmutableArray.Create(RasterDiagnostic("raster/no-player", "The scene has no player.")))
        | html ->
            let exports =
                [
                    "png", "image/png", DiagramRaster.Render(scene, RasterOptions.Png)
                    "jpeg", "image/jpeg", DiagramRaster.Render(scene, RasterOptions.Jpeg(90))
                    "gif", "image/gif", DiagramRaster.RenderGif(scene, GifOptions.Create(max 1000 (min 60_000 (scene.Connectors.Length * 1000)), 10))
                ]

            let made = exports |> List.filter (fun (_, _, image) -> image.Succeeded)
            let diagnostics = exports |> List.collect (fun (_, _, image) -> List.ofSeq image.Diagnostics) |> ImmutableArray.CreateRange

            if made.IsEmpty then
                RasterPlayer(html, diagnostics)
            else
                let data =
                    made
                    |> List.map (fun (name, mediaType, image) ->
                        let extension = if name = "jpeg" then "jpg" else name
                        $"\"{name}\":{{\"type\":\"{mediaType}\",\"extension\":\"{extension}\",\"data\":\"{Convert.ToBase64String(Seq.toArray image.Bytes)}\"}}")
                    |> String.concat ","

                let options =
                    made |> List.map (fun (name, _, _) -> $"<option value=\"{name}\">{name.ToUpperInvariant()}</option>") |> String.concat ""

                let picker =
                    "<label class=\"download-format\"><span class=\"visually-hidden\">Download format</span><select data-control=\"download-format\"><option value=\"svg\">SVG</option>"
                    + options
                    + "</select><svg class=\"icon motion-speed__chevron\" data-icon=\"chevron\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M7 10l5 5 5-5\"/></svg></label>"

                let style =
                    "<style>.downloads{display:inline-flex;gap:6px;align-items:center}.download-format{position:relative;display:inline-flex;align-items:center}"
                    + ".download-format select{block-size:32px;padding:0 28px 0 12px;border:1px solid var(--player-line);border-radius:999px;background:transparent;color:var(--diagram-page-text);font:inherit;cursor:pointer;appearance:none;-webkit-appearance:none}"
                    + "@media (max-width:36rem){.downloads{grid-area:download;justify-self:end;gap:4px}.downloads .download-button{inline-size:32px;padding:0;justify-content:center}.downloads .download-button>span{display:none}.download-format select{padding:0 22px 0 10px}}@media print{.downloads{display:none}}</style>"

                let button = html.IndexOf("<button type=\"button\" class=\"icon-button download-button\" data-control=\"download\"", StringComparison.Ordinal)
                let close = html.IndexOf("</button>", button, StringComparison.Ordinal) + "</button>".Length
                let original = html.Substring(button, close - button)
                let relabelled = original.Replace("aria-label=\"Download SVG\"", "aria-label=\"Download\"").Replace("<span aria-hidden=\"true\">SVG</span>", "<span aria-hidden=\"true\">Download</span>")

                let page =
                    html.Substring(0, button)
                    + "<span class=\"downloads\">" + picker + relabelled + "</span>"
                    + html.Substring(close)

                // The data comes before the player's script, which reads it as it starts.
                let marker = "<script type=\"application/json\" data-motion-data>"
                let at = page.IndexOf(marker, StringComparison.Ordinal)
                let page = page.Substring(0, at) + "<script type=\"application/json\" data-raster>{" + data + "}</script>\n" + style + "\n" + page.Substring(at)
                RasterPlayer(page, diagnostics)
