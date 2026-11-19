// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

(() => {
    "use strict";
    const root = document.currentScript.closest("[data-diagram-viewer]");
    const control = name => root.querySelector(`[data-control="${name}"]`);
    const data = JSON.parse(root.querySelector("[data-motion-data]").textContent);
    const svg = root.querySelector("[data-viewport-content] svg");
    const timeline = root.querySelector("[data-motion-timeline]");
    const play = control("play");
    const seek = control("seek");
    const speed = control("speed");
    const status = control("status");
    const reduced = matchMedia("(prefers-reduced-motion: reduce)");
    const duration = data.durationMs;
    const ns = "http://www.w3.org/2000/svg";
    // The signal takes the theme accent, the colour of the play control.
    const accent = "var(--diagram-page-accent, #2563eb)";
    let playing = false;
    let inspected = false;
    let elapsed = 0;
    let rate = 1;
    let previous = null;
    let request = null;
    const overlays = new Map();
    const overlayLayer = create("g", { "aria-hidden": "true" });

    seek.max = String(duration);
    // One tick for each scheduled start, so the timeline shows where connections begin.
    const ticks = duration === 0 ? [] : [...new Set(data.steps.map(step => step.startMs))].map(start => {
        const tick = document.createElement("span");
        tick.style.left = `${start / duration * 100}%`;
        tick.dataset.start = String(start);
        return tick;
    });
    root.querySelector("[data-motion-ticks]").append(...ticks);
    for (const button of [play, seek, speed, control("step"), control("restart")]) {
        button.disabled = duration === 0;
    }
    control("download").disabled = false;

    function create(name, attributes) {
        const element = document.createElementNS(ns, name);
        for (const [key, value] of Object.entries(attributes)) {
            element.setAttribute(key, value);
        }
        return element;
    }

    function draw() {
        seek.value = String(Math.floor(elapsed));
        const seconds = time => (time / 1000).toFixed(1);
        control("position").value = `${seconds(elapsed)} / ${seconds(duration)} s`;
        timeline.style.setProperty("--progress", String(duration === 0 ? 0 : elapsed / duration));
        for (const tick of ticks) tick.classList.toggle("is-passed", Number(tick.dataset.start) <= elapsed && inspected);
        if (duration === 0 || !inspected) return;

        const time = data.repeat ? elapsed % duration : elapsed;
        const steps = data.repeat ? [data.steps[Math.floor(time / data.steps[0].durationMs)]] : data.steps;
        const active = new Set();
        let nextGroup = overlayLayer.firstElementChild;
        for (const step of steps) {
            if (time < step.startMs || time >= step.startMs + step.durationMs) continue;
            active.add(step.ordinal);
            const binding = data.paths[step.connector];
            const progress = (time - step.startMs) / step.durationMs;
            const overlay = overlays.get(step.ordinal) ?? makeOverlay(step, binding, nextGroup);
            // Restore schedule order even when a seek reactivates an earlier step.
            if (overlay.group !== nextGroup && overlay.group.nextElementSibling !== nextGroup) {
                overlayLayer.insertBefore(overlay.group, nextGroup);
            }
            nextGroup = overlay.group.nextElementSibling;
            const point = overlay.path.getPointAtLength(overlay.length * (binding.reversed ? 1 - progress : progress));
            overlay.dot.setAttribute("cx", String(point.x));
            overlay.dot.setAttribute("cy", String(point.y));
            overlay.path.setAttribute("stroke-dashoffset", String((binding.reversed ? 1 : -1) * overlay.length * progress));
            overlay.group.dataset.progress = String(progress);
            overlay.group.dataset.reversed = String(binding.reversed);
        }
        for (const [ordinal, overlay] of overlays) {
            if (!active.has(ordinal)) {
                overlay.group.remove();
                overlays.delete(ordinal);
            }
        }
    }

    function makeOverlay(step, binding, nextGroup) {
        const group = create("g", { class: "motion-overlay", "aria-hidden": "true" });
        group.dataset.ordinal = String(step.ordinal);
        group.dataset.connector = String(binding.ordinal);
        const path = create("path", {
            d: binding.d, fill: "none", stroke: accent,
            "stroke-width": "3", "stroke-opacity": ".5", "stroke-dasharray": "12 8"
        });
        const dot = create("circle", { r: "4.5", fill: accent, stroke: "var(--diagram-page-bg, #ffffff)", "stroke-width": "2" });
        group.append(path, dot);
        if (overlayLayer.parentNode === null) svg.append(overlayLayer);
        overlayLayer.insertBefore(group, nextGroup);
        const overlay = { group, path, dot, length: path.getTotalLength() };
        overlays.set(step.ordinal, overlay);
        return overlay;
    }

    function advance(now) {
        if (previous !== null) {
            const time = elapsed + (now - previous) * rate;
            elapsed = data.repeat ? time % duration : Math.min(time, duration);
            if (!data.repeat && elapsed >= duration) {
                playing = false;
                reflectState();
            }
        }
        previous = now;
    }

    function tick(now) {
        request = null;
        if (!playing || document.hidden || reduced.matches) return;
        advance(now);
        draw();
        if (playing) request = requestAnimationFrame(tick);
    }

    function schedule() {
        if (request !== null) cancelAnimationFrame(request);
        request = null;
        previous = null;
        if (playing && !document.hidden && !reduced.matches) request = requestAnimationFrame(tick);
    }

    function reflectState() {
        play.setAttribute("aria-label", playing ? "Pause" : "Play");
        play.setAttribute("aria-pressed", String(playing));
        status.textContent = playing ? data.repeat ? "Playing authored order." : "Playing explicit scenario."
            : !data.repeat && duration > 0 && elapsed === duration ? "Finished."
            : reduced.matches ? "Paused. Reduced motion: use Step or Position."
            : data.repeat ? "Authored order illustration. Paused." : "Explicit scenario. Paused.";
    }

    function inspect(time) {
        playing = false;
        inspected = true;
        elapsed = time;
        schedule();
        reflectState();
        draw();
    }

    function step() {
        if (duration === 0) return;
        const time = data.repeat ? elapsed % duration : elapsed;
        let next = duration;
        for (const step of data.steps) {
            if (step.startMs > time && step.startMs < next) next = step.startMs;
        }
        inspect(data.repeat ? next % duration : next);
    }

    play.addEventListener("click", () => {
        if (duration === 0) return;
        if (reduced.matches) { step(); return; }
        if (!playing && !data.repeat && elapsed >= duration) elapsed = 0;
        const requested = !playing;
        if (playing && previous !== null) advance(performance.now());
        playing = requested;
        inspected = true;
        schedule();
        reflectState();
        draw();
    });
    control("step").addEventListener("click", step);
    control("restart").addEventListener("click", () => inspect(0));
    seek.addEventListener("input", () => {
        elapsed = Number(seek.value);
        inspected = true;
        schedule();
        draw();
        reflectState();
    });
    speed.addEventListener("change", () => {
        if (playing && previous !== null) advance(performance.now());
        rate = Number(speed.value);
        schedule();
        draw();
    });
    document.addEventListener("visibilitychange", schedule);
    reduced.addEventListener("change", () => {
        if (reduced.matches) playing = false;
        schedule();
        reflectState();
    });
    control("download").addEventListener("click", () => {
        const url = URL.createObjectURL(new Blob([data.svg], { type: "image/svg+xml;charset=utf-8" }));
        const link = document.createElement("a");
        link.href = url;
        link.download = "diagram.svg";
        root.append(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    });

    reflectState();
    draw();
})();
