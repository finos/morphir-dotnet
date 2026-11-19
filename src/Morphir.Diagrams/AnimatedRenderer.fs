// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Buffers
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Text
open System.Text.Json

/// One scheduled occurrence of a connector in a finite playback scenario.
[<Sealed>]
type PlaybackStep
    private (connector: DiagramConnector, startMilliseconds: int64, durationMilliseconds: int64) =
    /// The binding captured by the scene that will play this occurrence.
    member _.Connector = connector

    /// Start time relative to the beginning of the scenario.
    member _.StartMilliseconds = startMilliseconds

    /// Positive elapsed time over which to traverse the path.
    member _.DurationMilliseconds = durationMilliseconds

    member internal _.EndMilliseconds = startMilliseconds + durationMilliseconds

    /// Schedules a connector occurrence. Overlapping intervals play concurrently.
    /// Throws if the interval is empty, negative, or exceeds the exact browser clock range.
    static member Create
        (connector: DiagramConnector, startMilliseconds: int64, durationMilliseconds: int64)
        =
        ArgumentNullException.ThrowIfNull connector
        let maximum = 9_007_199_254_740_991L

        if startMilliseconds < 0L then
            invalidArg (nameof startMilliseconds) "The start time must be nonnegative."

        if durationMilliseconds <= 0L || durationMilliseconds > maximum - startMilliseconds then
            invalidArg
                (nameof durationMilliseconds)
                "The duration must be positive and fit the playback clock."

        PlaybackStep(connector, startMilliseconds, durationMilliseconds)

    override _.ToString() =
        $"Connector {connector.Ordinal} at {startMilliseconds} ms for {durationMilliseconds} ms"

[<RequireQualifiedAccess>]
module private PlayerArtifact =
    let stepMilliseconds = 1000L

    let private script name =
        lazy
            let resource =
                typeof<DiagramScene>
                    .Assembly.GetManifestResourceStream($"Morphir.Diagrams.{name}.js")

            match resource with
            | null -> invalidOp $"The {name} resource is missing."
            | stream ->
                use stream = stream
                use reader = new StreamReader(stream, Encoding.UTF8)
                reader.ReadToEnd()

    let private player = script "Player"
    let private viewport = script "Viewport"

    let private head =
        """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Diagram playback</title>
<style>
"""

    /// The page chrome takes its colours from the palette that the diagram uses.
    let private chrome (options: SvgRenderOptions) =
        let theme = options.Theme

        let pick (role: ThemePalette -> Color) =
            match options.Mode with
            | ThemeMode.Dark -> (role theme.Dark).ToCss()
            | ThemeMode.Light -> (role theme.Light).ToCss()
            | ThemeMode.Auto -> $"light-dark({(role theme.Light).ToCss()}, {(role theme.Dark).ToCss()})"

        let scheme =
            match options.Mode with
            | ThemeMode.Auto -> "light dark"
            | mode -> mode.WireValue

        $":root {{ color-scheme: {scheme}; --diagram-page-bg: {pick _.Background}; --diagram-page-text: {pick _.Text}; --diagram-page-surface: {pick _.Mask}; --diagram-page-border: {pick _.Muted}; }}\n"

    let private opening =
        """body { font: 1rem system-ui, sans-serif; margin: 1.5rem; background: var(--diagram-page-bg); color: var(--diagram-page-text); }
.diagram-canvas { overflow: auto; box-sizing: border-box; border: 1px solid var(--diagram-viewer-border, var(--diagram-page-border)); background: var(--diagram-viewer-canvas-bg, var(--diagram-page-bg)); }
.diagram-canvas svg { display: block; max-width: 100%; height: auto; }
.diagram-toolbar { display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: .25rem; padding: .25rem; background: var(--diagram-viewer-toolbar-bg, var(--diagram-page-surface)); }
.diagram-toolbar button, .motion-controls :is(button, select) { color: var(--diagram-page-text); background: var(--diagram-page-surface); border: 1px solid var(--diagram-page-border); }
.diagram-toolbar[hidden] { display: none; }
.diagram-toolbar button { min-width: 44px; min-height: 44px; border-radius: var(--diagram-viewer-control-radius, .35rem); }
.diagram-toolbar output { min-width: 3rem; text-align: center; }
.diagram-viewport:focus-visible, .diagram-toolbar button:focus-visible { outline: 3px solid var(--diagram-viewer-focus, #2563eb); outline-offset: -3px; }
[data-viewport-ready] .diagram-viewport { position: relative; overflow: hidden; block-size: var(--diagram-viewer-height, clamp(18rem, 60vh, 48rem)); }
[data-viewport-ready] .diagram-content { position: absolute; top: 50%; left: 50%; }
[data-viewport-ready] .diagram-content svg { width: 100%; height: 100%; }
.diagram-viewport--pannable { cursor: grab; touch-action: none; }
.diagram-viewport--pannable:active { cursor: grabbing; user-select: none; }
.diagram-canvas:fullscreen, .diagram-fullscreen[open] > .diagram-canvas {
    display: grid; grid-template-rows: auto minmax(0, 1fr); width: 100%; height: 100%;
}
.diagram-canvas:fullscreen .diagram-viewport, .diagram-fullscreen[open] .diagram-viewport { block-size: auto; }
.diagram-fullscreen { box-sizing: border-box; width: 100%; height: 100%; max-width: none; max-height: none; padding: 0; border: 0; }
body:has(.diagram-fullscreen[open]) { overflow: hidden; }
.motion-controls { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem; margin: 1rem 0; }
.motion-overlay { pointer-events: none; }
@media print {
    .diagram-toolbar, .motion-controls, [data-control="status"], .motion-overlay { display: none; }
    [data-viewport-ready] .diagram-viewport { block-size: auto; overflow: visible; }
    [data-viewport-ready] .diagram-content { position: static; width: auto !important; height: auto !important; transform: none !important; }
    [data-viewport-ready] .diagram-content svg { height: auto; }
    .diagram-fullscreen[open] { position: static; height: auto; }
}
</style></head><body>
<main data-diagram-viewer aria-label="Diagram playback">
<div class="diagram-canvas" data-viewport-root>
<div class="diagram-toolbar" role="group" aria-label="Diagram view" data-viewport-toolbar hidden>
<button type="button" data-view-control="zoom-out" aria-label="Zoom out">−</button>
<output data-view-control="scale" aria-label="Diagram scale">100%</output>
<button type="button" data-view-control="zoom-in" aria-label="Zoom in">+</button>
<button type="button" data-view-control="fit">Fit</button>
<button type="button" data-view-control="fullscreen" aria-pressed="false">Fullscreen</button>
</div>
<div class="diagram-viewport" data-viewport tabindex="0" role="region" aria-label="Diagram viewport. Zoom in to pan with arrow keys or drag.">
<div class="diagram-content" data-viewport-content>
"""

    let private controls =
        """
</div>
</div>
</div>
<dialog class="diagram-fullscreen" data-viewport-dialog aria-label="Expanded diagram"></dialog>
<section class="motion-controls" aria-label="Playback controls">
<button type="button" data-control="play" aria-pressed="false" disabled>Play</button>
<button type="button" data-control="restart" disabled>Restart</button>
<button type="button" data-control="step" disabled>Step</button>
<label>Speed <select data-control="speed" disabled>
<option value=".25">0.25×</option><option value=".5">0.5×</option><option value="1" selected>1×</option>
<option value="1.5">1.5×</option><option value="2">2×</option><option value="4">4×</option>
</select></label>
<label>Position <input data-control="seek" type="range" min="0" max="0" value="0" step="1" disabled></label>
<output data-control="position">0 ms</output>
<button type="button" data-control="download" disabled>Download SVG</button>
</section>
<p data-control="status" role="status">Authored order illustration. Paused.</p>
<script type="application/json" data-motion-data>"""

    let private motionData (scene: DiagramScene) repeat (steps: PlaybackStep array) (svg: string) =
        let bytes = ArrayBufferWriter<byte>()
        use json = new Utf8JsonWriter(bytes)
        json.WriteStartObject()
        // The default JSON encoder escapes script terminators and Unicode line separators.
        json.WriteString("svg", svg)
        json.WriteBoolean("repeat", repeat)

        let duration =
            steps |> Array.fold (fun total step -> max total step.EndMilliseconds) 0L

        json.WriteNumber("durationMs", duration)
        json.WriteStartArray("paths")

        for connector in scene.Connectors do
            json.WriteStartObject()
            json.WriteNumber("ordinal", connector.Ordinal)
            json.WriteString("d", connector.PathData)
            json.WriteBoolean("reversed", connector.IsReversed)
            json.WriteEndObject()

        json.WriteEndArray()
        json.WriteStartArray("steps")

        for index in 0 .. steps.Length - 1 do
            let step = steps[index]
            json.WriteStartObject()
            json.WriteNumber("ordinal", index)
            json.WriteNumber("connector", step.Connector.Ordinal)
            json.WriteNumber("startMs", step.StartMilliseconds)
            json.WriteNumber("durationMs", step.DurationMilliseconds)
            json.WriteEndObject()

        json.WriteEndArray()
        json.WriteEndObject()
        json.Flush()
        Encoding.UTF8.GetString bytes.WrittenSpan

    let write (scene: DiagramScene) repeat steps =
        let svg = scene.Document.Render()

        let html =
            String.Concat(
                [|
                    head
                    chrome scene.Options
                    opening
                    svg
                    controls
                    motionData scene repeat steps svg
                    "</script>\n<script>\n"
                    player.Value
                    "\n"
                    viewport.Value
                    "\n</script>\n</main></body></html>\n"
                |]
            )

        RenderedDiagram("text/html", Some html, ImmutableArray<Diagnostic>.Empty)

/// Renders a diagram scene as an offline HTML player.
[<AbstractClass; Sealed>]
type AnimatedRenderer private () =
    /// Plays the bound connectors in authored order. The scene's static SVG is available for download.
    static member Render(scene: DiagramScene) =
        let steps =
            scene.Connectors
            |> Seq.mapi (fun index connector ->
                PlaybackStep.Create(
                    connector,
                    int64 index * PlayerArtifact.stepMilliseconds,
                    PlayerArtifact.stepMilliseconds
                )
            )
            |> Seq.toArray

        PlayerArtifact.write scene true steps

    /// Plays a finite schedule. All of its connector bindings must come from the supplied scene.
    static member Render(scene: DiagramScene, steps: IReadOnlyList<PlaybackStep>) =
        ArgumentNullException.ThrowIfNull steps

        let fail code message =
            RenderedDiagram(
                "text/html",
                None,
                ImmutableArray.Create(Reading.error code message None)
            )

        if steps.Count > Reading.edgeLimit then
            fail "animation/plan-limit" $"A scenario can contain at most {Reading.edgeLimit} steps."
        else
            let steps = steps |> Seq.toArray

            let belongsToScene (step: PlaybackStep) =
                ArgumentNullException.ThrowIfNull step
                let index = step.Connector.Ordinal

                index < scene.Connectors.Length
                && Object.ReferenceEquals(scene.Connectors[index], step.Connector)

            if steps |> Array.forall belongsToScene then
                PlayerArtifact.write scene false steps
            else
                fail
                    "animation/plan-mismatch"
                    "A playback step binds a connector from another diagram scene."
