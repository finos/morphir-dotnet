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

    let private player =
        lazy
            let resource =
                typeof<DiagramScene>
                    .Assembly.GetManifestResourceStream("Morphir.Diagrams.Player.js")

            match resource with
            | null -> invalidOp "The diagram player resource is missing."
            | stream ->
                use stream = stream
                use reader = new StreamReader(stream, Encoding.UTF8)
                reader.ReadToEnd()

    let private opening =
        """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Diagram playback</title>
<style>
body { font: 1rem system-ui, sans-serif; margin: 1.5rem; }
.diagram-canvas { overflow: auto; border: 1px solid #bac8db; }
.diagram-canvas svg { display: block; max-width: 100%; height: auto; }
.motion-controls { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem; margin: 1rem 0; }
.motion-overlay { pointer-events: none; }
@media print { .motion-controls, [data-control="status"], .motion-overlay { display: none; } }
</style></head><body>
<main data-diagram-viewer aria-label="Diagram playback">
<div class="diagram-canvas">
"""

    let private controls =
        """
</div>
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
                    opening
                    svg
                    controls
                    motionData scene repeat steps svg
                    "</script>\n<script>\n"
                    player.Value
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
