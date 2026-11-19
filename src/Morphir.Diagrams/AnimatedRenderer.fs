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

    /// The page chrome takes its colours from the palette that the diagram uses. The accent
    /// is the readable ink of the cloud family, the colour of the moving signal.
    let private chrome (options: SvgRenderOptions) =
        let theme = options.Theme

        let pick (role: ThemePalette -> Color) =
            match options.Mode with
            | ThemeMode.Dark -> (role theme.Dark).ToCss()
            | ThemeMode.Light -> (role theme.Light).ToCss()
            | ThemeMode.Auto -> $"light-dark({(role theme.Light).ToCss()}, {(role theme.Dark).ToCss()})"

        let accent palette =
            Theme.resolveInk false palette ColorFamily.Cloud

        let scheme =
            match options.Mode with
            | ThemeMode.Auto -> "light dark"
            | mode -> mode.WireValue

        String.concat
            " "
            [
                $":root {{ color-scheme: {scheme};"
                $"--diagram-page-bg: {pick _.Background};"
                $"--diagram-page-text: {pick _.Text};"
                $"--diagram-page-muted: {pick _.Muted};"
                $"--diagram-page-surface: {pick _.Mask};"
                $"--diagram-page-border: {pick _.Muted};"
                $"--diagram-page-accent: {pick accent}; }}\n"
            ]

    let private opening =
        """body { margin: 0; padding: clamp(.75rem, 3vw, 2rem); background: var(--diagram-page-bg); color: var(--diagram-page-text); }
.diagram-player {
    --player-font: 'JetBrains Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, 'DejaVu Sans Mono', 'Liberation Mono', monospace;
    --player-radius: var(--diagram-viewer-control-radius, 12px);
    --player-line: color-mix(in srgb, var(--diagram-page-muted) 40%, transparent);
    --player-hover: color-mix(in srgb, var(--diagram-page-text) 12%, transparent);
    --player-focus: var(--diagram-viewer-focus, var(--diagram-page-accent));
    display: block; max-width: 90rem; margin: 0 auto; font: 13px/1.4 var(--player-font); color: var(--diagram-page-text);
}
.diagram-player *, .diagram-player *::before, .diagram-player *::after { box-sizing: border-box; }
.visually-hidden { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; border: 0; }
.diagram-player :focus-visible { outline: 2px solid var(--player-focus); outline-offset: 2px; }
.diagram-canvas { position: relative; overflow: auto; border: 1px solid var(--diagram-viewer-border, var(--player-line)); border-bottom-width: 0; border-radius: var(--player-radius) var(--player-radius) 0 0; background: var(--diagram-viewer-canvas-bg, var(--diagram-page-bg)); }
.diagram-canvas svg { display: block; max-width: 100%; height: auto; }
.diagram-viewport:focus-visible { outline-offset: -2px; border-radius: inherit; }
[data-viewport-ready] .diagram-viewport { position: relative; overflow: hidden; block-size: var(--diagram-viewer-height, clamp(18rem, 60vh, 48rem)); }
[data-viewport-ready] .diagram-content { position: absolute; top: 50%; left: 50%; }
[data-viewport-ready] .diagram-content svg { width: 100%; height: 100%; max-width: none; }
.diagram-viewport--pannable { cursor: grab; touch-action: none; }
.diagram-viewport--pannable:active { cursor: grabbing; user-select: none; }
.diagram-toolbar { position: absolute; z-index: 2; top: .625rem; right: .625rem; display: flex; align-items: center; gap: 2px; padding: 3px; border: 1px solid var(--player-line); border-radius: 999px; background: var(--diagram-viewer-toolbar-bg, var(--diagram-page-surface)); }
.diagram-toolbar[hidden] { display: none; }
.toolbar-divider { inline-size: 1px; block-size: 18px; margin: 0 3px; background: var(--player-line); }
.icon-button { display: inline-grid; place-items: center; inline-size: 40px; block-size: 40px; padding: 0; border: 0; border-radius: 999px; background: transparent; color: var(--diagram-page-text); font: inherit; cursor: pointer; }
.diagram-toolbar .icon-button { inline-size: 34px; block-size: 34px; }
.icon-button:hover:not(:disabled) { background: var(--player-hover); }
.icon-button:disabled, .play-button:disabled, .motion-controls :is(input, select):disabled { opacity: .38; cursor: default; }
.icon { inline-size: 20px; block-size: 20px; fill: none; stroke: currentColor; stroke-width: 1.75; stroke-linecap: round; stroke-linejoin: round; }
.icon--solid { fill: currentColor; stroke: none; }
.readout { min-inline-size: 5ch; color: var(--diagram-page-text); font-variant-numeric: tabular-nums; text-align: center; white-space: nowrap; }
[aria-pressed="true"] > [data-icon="play"], [aria-pressed="false"] > [data-icon="pause"],
[aria-pressed="true"] > [data-icon="fullscreen"], [aria-pressed="false"] > [data-icon="exit-fullscreen"] { display: none; }
.diagram-canvas:fullscreen, .diagram-fullscreen[open] > .diagram-canvas { width: 100%; height: 100%; border: 0; border-radius: 0; }
.diagram-canvas:fullscreen .diagram-viewport, .diagram-fullscreen[open] .diagram-viewport { block-size: 100%; }
.diagram-fullscreen { width: 100%; height: 100%; max-width: none; max-height: none; padding: 0; border: 0; background: var(--diagram-page-bg); }
body:has(.diagram-fullscreen[open]) { overflow: hidden; }
.motion-controls { display: flex; flex-wrap: wrap; align-items: center; gap: .25rem .5rem; padding: .5rem .75rem .5rem .5rem; border: 1px solid var(--player-line); border-radius: 0 0 var(--player-radius) var(--player-radius); background: var(--diagram-page-surface); }
.play-button { display: inline-grid; place-items: center; inline-size: 44px; block-size: 44px; padding: 0; border: 0; border-radius: 50%; background: var(--diagram-page-accent); color: var(--diagram-page-bg); cursor: pointer; }
.play-button .icon { inline-size: 22px; block-size: 22px; }
.play-button [data-icon="play"] { translate: 1px 0; }
.motion-timeline { --progress: 0; position: relative; display: flex; flex: 1 1 12rem; align-items: center; min-inline-size: 8rem; block-size: 44px; margin: 0 .25rem; }
.motion-ticks { position: absolute; inset: 50% 7px auto; pointer-events: none; }
.motion-ticks > span { position: absolute; inline-size: 2px; block-size: 10px; margin: -5px 0 0 -1px; border-radius: 1px; background: var(--diagram-page-muted); opacity: .55; }
.motion-ticks > span.is-passed { background: var(--diagram-page-accent); opacity: 1; }
.motion-timeline input { position: relative; inline-size: 100%; block-size: 44px; margin: 0; background: transparent; cursor: pointer; appearance: none; -webkit-appearance: none; }
.motion-timeline input::-webkit-slider-runnable-track { block-size: 4px; border-radius: 2px; background: linear-gradient(to right, var(--diagram-page-accent) calc(var(--progress) * 100%), var(--player-line) 0); }
.motion-timeline input::-moz-range-track { block-size: 4px; border-radius: 2px; background: linear-gradient(to right, var(--diagram-page-accent) calc(var(--progress) * 100%), var(--player-line) 0); }
.motion-timeline input::-webkit-slider-thumb { inline-size: 14px; block-size: 14px; margin-top: -5px; border: 2px solid var(--diagram-page-surface); border-radius: 50%; background: var(--diagram-page-accent); appearance: none; -webkit-appearance: none; }
.motion-timeline input::-moz-range-thumb { inline-size: 10px; block-size: 10px; border: 2px solid var(--diagram-page-surface); border-radius: 50%; background: var(--diagram-page-accent); }
.motion-timeline input:focus-visible { outline-offset: 0; border-radius: 4px; }
.motion-speed { position: relative; display: inline-flex; align-items: center; }
.motion-speed select, .download-button { block-size: 32px; border: 1px solid var(--player-line); border-radius: 999px; background: transparent; color: var(--diagram-page-text); font: inherit; }
.motion-speed select { padding: 0 28px 0 12px; cursor: pointer; appearance: none; -webkit-appearance: none; }
.motion-speed__chevron { position: absolute; right: 8px; inline-size: 14px; block-size: 14px; pointer-events: none; }
.download-button { display: inline-flex; gap: 6px; inline-size: auto; padding: 0 12px 0 9px; }
.download-button .icon { inline-size: 16px; block-size: 16px; }
.motion-status { max-inline-size: 70ch; margin: .625rem .25rem 0; color: var(--diagram-page-muted); }
.motion-overlay { pointer-events: none; }
.annotation-marker { cursor: pointer; }
.annotation-marker:focus-visible { outline: none; }
.annotation-marker__hit { fill: transparent; }
.annotation-marker:focus-visible .annotation-marker__hit { fill: none; stroke: var(--player-focus); stroke-width: 2; }
.annotation-marker__pill { fill: var(--text, var(--diagram-page-text)); stroke: var(--bg, var(--diagram-page-bg)); stroke-width: 2; }
.annotation-marker:hover .annotation-marker__pill { fill: color-mix(in srgb, var(--text, var(--diagram-page-text)) 82%, var(--bg, var(--diagram-page-bg))); }
.annotation-marker[aria-expanded="true"] .annotation-marker__pill { fill: var(--diagram-page-accent); }
.annotation-marker__count { fill: var(--bg, var(--diagram-page-bg)); font: 600 11px var(--player-font); pointer-events: none; }
.annotation-highlight { fill: color-mix(in srgb, var(--text, var(--diagram-page-text)) 7%, transparent); stroke: var(--text, var(--diagram-page-text)); stroke-width: 1.5; stroke-dasharray: 5 4; vector-effect: non-scaling-stroke; pointer-events: none; }
.diagram-canvas[data-annotation-open] .diagram-viewport { margin-inline-end: calc(min(22rem, 100% - 1.25rem) + 1.25rem); }
.annotation-panel { position: absolute; z-index: 3; top: 3.75rem; right: .625rem; bottom: .625rem; display: flex; flex-direction: column; inline-size: min(22rem, calc(100% - 1.25rem)); border: 1px solid var(--player-line); border-radius: var(--player-radius); background: var(--diagram-page-surface); color: var(--diagram-page-text); box-shadow: 0 16px 40px -24px color-mix(in srgb, var(--diagram-page-text) 45%, transparent); }
.annotation-panel[hidden] { display: none; }
.annotation-panel__header { display: flex; align-items: flex-start; gap: .5rem; padding: .75rem .375rem .625rem 1rem; border-bottom: 1px solid var(--player-line); }
.annotation-panel__heading { flex: 1; min-inline-size: 0; }
.annotation-panel__title { margin: .25rem 0 0; font-size: 15px; font-weight: 600; line-height: 1.3; overflow-wrap: anywhere; }
.annotation-panel__title:focus-visible { outline: none; }
.annotation-panel__kind { margin: .125rem 0 0; color: var(--diagram-page-muted); }
.annotation-panel__close { flex: none; inline-size: 36px; block-size: 36px; }
.annotation-panel__body { overflow: auto; overscroll-behavior: contain; padding: 0 0 .75rem; }
.annotation-section__title { margin: .875rem 1rem .25rem; color: var(--diagram-page-muted); font-size: 12px; font-weight: 600; }
.annotation-list { margin: 0; padding: 0; list-style: none; }
.annotation-item { margin: 0 .375rem; padding: .5rem .625rem; border-radius: 8px; }
.annotation-item:hover, .annotation-item:focus-within { background: var(--player-hover); }
.annotation-item:focus-visible { outline-offset: -2px; }
.annotation-item p { margin: 0; }
.annotation-meta { display: flex; flex-wrap: wrap; gap: 0 .75rem; color: var(--diagram-page-muted); font-size: 12px; }
.annotation-author { overflow-wrap: anywhere; color: var(--diagram-page-text); }
.annotation-item .annotation-body { margin-top: .25rem; white-space: pre-wrap; overflow-wrap: anywhere; }
.annotation-predicate { font-size: 12px; overflow-wrap: anywhere; }
.annotation-predicate__namespace { color: var(--diagram-page-muted); }
.annotation-iri { display: block; margin-top: .125rem; overflow-wrap: anywhere; color: var(--diagram-page-text); }
a.annotation-iri { color: var(--diagram-page-accent); text-decoration: underline; text-underline-offset: 2px; }
.annotation-item .annotation-also { margin-top: .25rem; color: var(--diagram-page-muted); font-size: 12px; }
@media (prefers-reduced-motion: no-preference) {
    .icon-button, .play-button, .motion-speed select { transition: background-color .15s ease, scale .15s ease; }
    .play-button:active:not(:disabled) { scale: .94; }
    .annotation-panel:not([hidden]) { animation: annotation-open .16s ease-out; }
    @keyframes annotation-open { from { opacity: 0; translate: .75rem 0; } }
}
@media (max-width: 36rem) {
    .motion-controls { display: grid; grid-template-columns: auto auto auto 1fr auto auto; grid-template-areas: "play restart step . speed download" "seek seek seek seek position position"; }
    [data-control="play"] { grid-area: play; }
    [data-control="restart"] { grid-area: restart; }
    [data-control="step"] { grid-area: step; }
    .motion-speed { grid-area: speed; }
    [data-control="download"] { grid-area: download; }
    .motion-timeline { grid-area: seek; min-inline-size: 0; }
    [data-control="position"] { grid-area: position; justify-self: end; }
    .diagram-canvas[data-annotation-open] .diagram-viewport { margin-inline-end: 0; }
    .annotation-panel { position: static; inline-size: auto; border-width: 1px 0 0; border-radius: 0; box-shadow: none; }
    .diagram-canvas:fullscreen, .diagram-fullscreen[open] > .diagram-canvas { display: flex; flex-direction: column; }
    .diagram-canvas:fullscreen .diagram-viewport, .diagram-fullscreen[open] .diagram-viewport { flex: 1 1 0; block-size: auto; min-block-size: 0; }
    .diagram-canvas:fullscreen .annotation-panel, .diagram-fullscreen[open] .annotation-panel { flex: none; max-block-size: 50%; }
    @keyframes annotation-open { from { opacity: 0; translate: 0 .75rem; } }
}
@media (max-width: 24rem) {
    .motion-controls { gap: .25rem; padding-inline: .375rem .5rem; }
    .motion-speed select { padding: 0 22px 0 10px; }
    .motion-speed__chevron { right: 6px; }
    .download-button { inline-size: 32px; padding: 0; justify-content: center; }
    .download-button > span { display: none; }
}
@media print {
    .diagram-toolbar, .motion-controls, [data-control="status"], .motion-overlay, .annotation-layer, .annotation-panel { display: none; }
    .diagram-canvas { border: 0; }
    [data-viewport-ready] .diagram-viewport { block-size: auto; overflow: visible; }
    [data-viewport-ready] .diagram-content { position: static; width: auto !important; height: auto !important; transform: none !important; }
    [data-viewport-ready] .diagram-content svg { height: auto; }
    .diagram-fullscreen[open] { position: static; height: auto; }
}
</style></head><body>
<main class="diagram-player" data-diagram-viewer aria-label="Diagram playback">
<div class="diagram-canvas" data-viewport-root>
<div class="diagram-toolbar" role="group" aria-label="Diagram view" data-viewport-toolbar hidden>
<button type="button" class="icon-button" data-view-control="zoom-out" aria-label="Zoom out"><svg class="icon" data-icon="zoom-out" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M5 12h14"/></svg></button>
<output class="readout" data-view-control="scale" aria-label="Diagram scale">100%</output>
<button type="button" class="icon-button" data-view-control="zoom-in" aria-label="Zoom in"><svg class="icon" data-icon="zoom-in" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M12 5v14M5 12h14"/></svg></button>
<span class="toolbar-divider" aria-hidden="true"></span>
<button type="button" class="icon-button" data-view-control="fit" aria-label="Fit diagram"><svg class="icon" data-icon="fit" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M4 9V5h4M20 9V5h-4M4 15v4h4M20 15v4h-4"/><rect x="9" y="9" width="6" height="6" rx="1"/></svg></button>
<button type="button" class="icon-button" data-view-control="fullscreen" aria-pressed="false" aria-label="Fullscreen"><svg class="icon" data-icon="fullscreen" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M14 4h6v6M10 20H4v-6M20 4l-6 6M4 20l6-6"/></svg><svg class="icon" data-icon="exit-fullscreen" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M20 10h-6V4M4 14h6v6M14 10l6-6M10 14l-6 6"/></svg></button>
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
<button type="button" class="play-button" data-control="play" aria-pressed="false" aria-label="Play" disabled><svg class="icon icon--solid" data-icon="play" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M8 5.5v13l11-6.5z"/></svg><svg class="icon icon--solid" data-icon="pause" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><rect x="6.5" y="5" width="4" height="14" rx="1"/><rect x="13.5" y="5" width="4" height="14" rx="1"/></svg></button>
<button type="button" class="icon-button" data-control="restart" aria-label="Restart" disabled><svg class="icon" data-icon="restart" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M4.5 12a7.5 7.5 0 1 0 2.2-5.3"/><path d="M4.5 4.5v4h4"/></svg></button>
<button type="button" class="icon-button" data-control="step" aria-label="Step to the next connection" disabled><svg class="icon" data-icon="step" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M6 6.5v11l8-5.5z"/><path d="M18 6v12"/></svg></button>
<label class="motion-timeline" data-motion-timeline><span class="visually-hidden">Position</span><span class="motion-ticks" data-motion-ticks aria-hidden="true"></span><input data-control="seek" type="range" min="0" max="0" value="0" step="1" disabled></label>
<output class="readout" data-control="position">0.0 / 0.0 s</output>
<label class="motion-speed"><span class="visually-hidden">Speed</span><select data-control="speed" disabled>
<option value=".25">0.25×</option><option value=".5">0.5×</option><option value="1" selected>1×</option>
<option value="1.5">1.5×</option><option value="2">2×</option><option value="4">4×</option>
</select><svg class="icon motion-speed__chevron" data-icon="chevron" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M7 10l5 5 5-5"/></svg></label>
<button type="button" class="icon-button download-button" data-control="download" aria-label="Download SVG" disabled><svg class="icon" data-icon="download" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M12 4v11M7 10l5 5 5-5M5 20h14"/></svg><span aria-hidden="true">SVG</span></button>
</section>
<p class="motion-status" data-control="status" role="status">Authored order illustration. Paused.</p>
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
        json.WriteStartArray("annotations")

        for reference, items in scene.Annotations do
            json.WriteStartObject()
            json.WriteString("ref", reference)
            json.WriteStartArray("items")

            for item in items do
                json.WriteStartObject()
                json.WriteString("kind", item.Kind)

                match item.Kind with
                | "comment" ->
                    json.WriteString("id", item.Id)
                    json.WriteString("author", item.Author)
                    json.WriteString("time", item.Time)
                    json.WriteString("body", item.Body)
                | "link" ->
                    json.WriteString("id", item.Id)
                    json.WriteString("predicate", item.Predicate)
                    json.WriteString("target", item.Target)
                    json.WriteBoolean("isWeb", item.IsWeb)
                | _ ->
                    json.WriteString("predicate", item.Predicate)
                    json.WriteString("value", item.Value)
                    json.WriteBoolean("isWeb", item.IsWeb)

                json.WriteEndObject()

            json.WriteEndArray()
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

        RenderedDiagram("text/html", Some html, scene.Diagnostics)

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
