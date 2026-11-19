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
    const annotationLayer = create("g", { class: "annotation-layer" });

    seek.max = String(duration);
    // One tick for each scheduled start, so the timeline shows where connections begin.
    const visits = data.visits ?? [];
    const starts = [...data.steps, ...visits].map(item => item.startMs);
    const ticks = duration === 0 ? [] : [...new Set(starts)].map(start => {
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
        // A visit highlights its element for the visit's interval; the visited element stays in its place.
        for (const visit of visits) {
            const on = time >= visit.startMs && time < visit.startMs + visit.durationMs;
            for (const element of elementsOf(visit.ref)) element.classList.toggle("is-visited", on);
        }
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
        // Motion plays under the annotation markers, so a moving signal never covers one.
        if (overlayLayer.parentNode === null) svg.insertBefore(overlayLayer, annotationLayer.parentNode === svg ? annotationLayer : null);
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
        status.textContent = playing ? data.authored ? "Playing authored order." : "Playing explicit scenario."
            : !data.repeat && duration > 0 && elapsed === duration ? "Finished."
            : reduced.matches ? "Paused. Reduced motion: use Step or Position."
            : data.authored ? "Authored order illustration. Paused." : "Explicit scenario. Paused.";
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
        for (const start of starts) {
            if (start > time && start < next) next = start;
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
    // A player with raster exports has a format picker; the images were made at the diagram's natural size, so a
    // download ignores zoom and pan and leaves playback as it is.
    const formats = root.querySelector("[data-raster]");
    const rasters = formats ? JSON.parse(formats.textContent) : {};
    control("download").addEventListener("click", () => {
        const picked = control("download-format")?.value ?? "svg";
        const raster = rasters[picked];
        const blob = raster
            ? new Blob([Uint8Array.from(atob(raster.data), c => c.charCodeAt(0))], { type: raster.type })
            : new Blob([data.svg], { type: "image/svg+xml;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = `diagram.${raster ? raster.extension : "svg"}`;
        root.append(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    });

    // Annotation layer: a marker on each element that has comments, links or statements, and a
    // panel that lists them. Every text from the data is set with textContent, never as markup,
    // and only http and https targets become links.
    const annotations = data.annotations ?? [];
    const canvas = root.querySelector("[data-viewport-root]");
    const kinds = {
        node: "Node", group: "Group", edge: "Edge", participant: "Participant",
        message: "Message", note: "Note", frame: "Frame", step: "Step"
    };
    const sections = [["comment", "Comments"], ["link", "Links"], ["property", "Statements"]];
    const plural = { comment: ["comment", "comments"], link: ["link", "links"], property: ["statement", "statements"] };
    const timeFormat = new Intl.DateTimeFormat(undefined, {
        year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit",
        timeZone: "UTC", timeZoneName: "short"
    });
    const targets = new Map();
    const highlights = [];
    let panel = null;
    let current = null;

    function html(name, attributes = {}, text) {
        const element = document.createElement(name);
        for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, value);
        if (text !== undefined) element.textContent = text;
        return element;
    }

    function decode(key) {
        try { return decodeURIComponent(key); } catch { return key; }
    }

    function describe(reference) {
        const split = reference.indexOf("=");
        const kind = reference.slice(0, split);
        const parts = reference.slice(split + 1).split("/").map(decode);
        const ordinal = Number(parts.at(-1));
        const title = kind === "edge" || kind === "message"
            ? `${parts[0]} to ${parts[1]}${ordinal > 0 ? ` (${ordinal + 1})` : ""}`
            : kind === "note" || kind === "frame" || kind === "step" ? `${kinds[kind]} ${ordinal + 1}`
            : parts.join("/");
        return { title, kind: kinds[kind] ?? kind };
    }

    function elementsOf(reference) {
        return [...svg.querySelectorAll("[data-ref]")].filter(element => element.dataset.ref === reference);
    }

    // The box of an element in the coordinates of the annotation layer, whatever transforms lie between.
    function frame(element) {
        const box = element.getBBox();
        const matrix = annotationLayer.getScreenCTM().inverse().multiply(element.getScreenCTM());
        const corners = [[box.x, box.y], [box.x + box.width, box.y], [box.x, box.y + box.height], [box.x + box.width, box.y + box.height]]
            .map(([x, y]) => new DOMPoint(x, y).matrixTransform(matrix));
        const xs = corners.map(point => point.x);
        const ys = corners.map(point => point.y);
        return { x: Math.min(...xs), y: Math.min(...ys), width: Math.max(...xs) - Math.min(...xs), height: Math.max(...ys) - Math.min(...ys) };
    }

    // A connection carries its marker at the middle of its longest line; any other element at its top right corner.
    function anchor(element, kind) {
        const box = frame(element);
        if (kind === "edge" || kind === "message") {
            const lines = [...element.querySelectorAll("path, line, polyline")];
            const longest = lines.reduce((best, line) => !best || line.getTotalLength() > best.getTotalLength() ? line : best, null);
            if (longest) {
                const matrix = annotationLayer.getScreenCTM().inverse().multiply(longest.getScreenCTM());
                return longest.getPointAtLength(longest.getTotalLength() / 2).matrixTransform(matrix);
            }
        }
        return { x: box.x + box.width, y: box.y };
    }

    function summary(items) {
        return sections.map(([kind]) => {
            const count = items.filter(item => item.kind === kind).length;
            return count === 0 ? null : `${count} ${plural[kind][count === 1 ? 0 : 1]}`;
        }).filter(Boolean).join(", ");
    }

    function makeMarker(entry) {
        const elements = elementsOf(entry.ref);
        if (elements.length === 0) return;
        const kind = entry.ref.slice(0, entry.ref.indexOf("="));
        const point = anchor(elements[0], kind);
        const { title } = describe(entry.ref);
        const count = String(entry.items.length);
        const width = 12 + count.length * 7;
        const marker = create("g", {
            class: "annotation-marker", role: "button", tabindex: "0", "aria-expanded": "false",
            "aria-label": `${title}, ${describe(entry.ref).kind.toLowerCase()}: ${summary(entry.items)}`
        });
        marker.dataset.annotationRef = entry.ref;
        marker.append(
            create("rect", { class: "annotation-marker__hit", x: String(-width / 2 - 6), y: "-16", width: String(width + 12), height: "32", rx: "16" }),
            create("rect", { class: "annotation-marker__pill", x: String(-width / 2), y: "-10", width: String(width), height: "20", rx: "10" }),
            Object.assign(create("text", { class: "annotation-marker__count", x: "0", y: "0", "text-anchor": "middle", "dominant-baseline": "central" }), { textContent: count })
        );
        marker.addEventListener("click", () => toggle(entry, marker));
        marker.addEventListener("keydown", event => {
            if (event.key !== "Enter" && event.key !== " ") return;
            event.preventDefault();
            toggle(entry, marker);
        });
        annotationLayer.append(marker);
        targets.set(entry.ref, { entry, marker, elements, point });
    }

    // Markers keep one size on screen, whatever the fitted size or zoom of the diagram.
    function placeMarkers() {
        const size = 1 / Math.max(svg.getScreenCTM()?.a ?? 1, 1e-6);
        for (const { marker, point } of targets.values()) {
            marker.setAttribute("transform", `translate(${point.x} ${point.y}) scale(${size})`);
        }
    }

    function highlight(references) {
        for (const shape of highlights.splice(0)) shape.remove();
        for (const reference of references) {
            for (const element of targets.get(reference)?.elements ?? elementsOf(reference)) {
                const box = frame(element);
                const shape = create("rect", {
                    class: "annotation-highlight", "aria-hidden": "true",
                    x: String(box.x - 6), y: String(box.y - 6), width: String(box.width + 12), height: String(box.height + 12), rx: "8"
                });
                annotationLayer.insertBefore(shape, annotationLayer.firstChild);
                highlights.push(shape);
            }
        }
    }

    // A link or statement value: a navigable link only for http and https, plain text otherwise.
    function value(text, isWeb) {
        if (!isWeb) return html("span", { class: "annotation-iri" }, text);
        return html("a", { class: "annotation-iri", href: text, target: "_blank", rel: "noopener noreferrer" }, text);
    }

    // The predicate IRI in full: its namespace quiet, its local name in the text colour.
    function predicate(iri) {
        const cut = Math.max(iri.lastIndexOf("#"), iri.lastIndexOf("/")) + 1;
        const term = html("p", { class: "annotation-predicate" });
        if (cut > 0 && cut < iri.length) term.append(html("span", { class: "annotation-predicate__namespace" }, iri.slice(0, cut)));
        term.append(html("span", { class: "annotation-predicate__name" }, cut > 0 && cut < iri.length ? iri.slice(cut) : iri));
        return term;
    }

    function time(text) {
        const date = new Date(text);
        return html("time", { datetime: text }, Number.isNaN(date.getTime()) ? text : timeFormat.format(date));
    }

    // The other elements that a comment or link with this identity also targets.
    function alsoOn(item, reference) {
        if (item.id === undefined) return [reference];
        return annotations.filter(entry => entry.items.some(other => other.kind === item.kind && other.id === item.id)).map(entry => entry.ref);
    }

    function renderItem(item, reference) {
        const entry = html("li", { class: `annotation-item annotation-item--${item.kind}`, tabindex: "0" });
        if (item.kind === "comment") {
            const meta = html("p", { class: "annotation-meta" });
            meta.append(html("span", { class: "annotation-author" }, item.author), time(item.time));
            entry.append(meta, html("p", { class: "annotation-body" }, item.body));
        } else {
            entry.append(predicate(item.predicate), value(item.kind === "link" ? item.target : item.value, item.isWeb));
        }
        const related = alsoOn(item, reference);
        const others = related.filter(other => other !== reference).map(other => describe(other).title);
        if (others.length > 0) entry.append(html("p", { class: "annotation-also" }, `Also on ${others.join(", ")}`));
        const show = () => highlight(related);
        const restore = event => { if (!entry.contains(event.relatedTarget)) highlight([reference]); };
        entry.addEventListener("focusin", show);
        entry.addEventListener("pointerenter", show);
        entry.addEventListener("focusout", restore);
        entry.addEventListener("pointerleave", () => { if (!entry.contains(document.activeElement)) highlight([reference]); });
        return entry;
    }

    function makePanel() {
        panel = html("aside", { class: "annotation-panel", hidden: "" });
        const header = html("div", { class: "annotation-panel__header" });
        const heading = html("div", { class: "annotation-panel__heading" });
        heading.append(html("h2", { class: "annotation-panel__title", tabindex: "-1" }), html("p", { class: "annotation-panel__kind" }));
        const close = html("button", { type: "button", class: "icon-button annotation-panel__close", "aria-label": "Close notes" });
        const icon = create("svg", { class: "icon", viewBox: "0 0 24 24", "aria-hidden": "true", focusable: "false" });
        icon.append(create("path", { d: "M6 6l12 12M18 6L6 18" }));
        close.append(icon);
        close.addEventListener("click", () => closePanel(true));
        header.append(heading, close);
        panel.append(header, html("div", { class: "annotation-panel__body" }));
        canvas.append(panel);
    }

    function openPanel(entry, marker) {
        if (panel === null) makePanel();
        current?.marker.setAttribute("aria-expanded", "false");
        current = { entry, marker };
        marker.setAttribute("aria-expanded", "true");
        const { title, kind } = describe(entry.ref);
        panel.setAttribute("aria-label", `Notes on ${title}`);
        panel.querySelector(".annotation-panel__title").textContent = title;
        panel.querySelector(".annotation-panel__kind").textContent = `${kind}: ${summary(entry.items)}`;
        const body = panel.querySelector(".annotation-panel__body");
        body.replaceChildren();
        for (const [kind, label] of sections) {
            const items = entry.items.filter(item => item.kind === kind);
            if (items.length === 0) continue;
            const section = html("section", { class: "annotation-section" });
            const list = html("ul", { class: "annotation-list" });
            list.append(...items.map(item => renderItem(item, entry.ref)));
            section.append(html("h3", { class: "annotation-section__title" }, label), list);
            body.append(section);
        }
        panel.hidden = false;
        canvas.dataset.annotationOpen = "true";
        highlight([entry.ref]);
        panel.querySelector(".annotation-panel__title").focus({ preventScroll: true });
        panel.scrollIntoView({ block: "nearest" });
    }

    function closePanel(returnFocus) {
        if (current === null) return;
        const { marker } = current;
        marker.setAttribute("aria-expanded", "false");
        current = null;
        panel.hidden = true;
        delete canvas.dataset.annotationOpen;
        highlight([]);
        if (returnFocus) marker.focus({ preventScroll: true });
    }

    function toggle(entry, marker) {
        if (current?.marker === marker) closePanel(true);
        else openPanel(entry, marker);
    }

    if (annotations.length > 0) {
        svg.append(annotationLayer);
        for (const entry of annotations) makeMarker(entry);
        placeMarkers();
        // The viewport resizes and zooms the diagram through the style of its content.
        let placing = null;
        new MutationObserver(() => {
            placing ??= requestAnimationFrame(() => { placing = null; placeMarkers(); });
        }).observe(root.querySelector("[data-viewport-content]"), { attributes: true, attributeFilter: ["style"] });
        // Esc closes the panel before it would close the expanded view.
        root.addEventListener("keydown", event => {
            if (event.key !== "Escape" || current === null) return;
            event.preventDefault();
            event.stopPropagation();
            closePanel(true);
        }, true);
    }

    // Structural exploration: breadth-first walks in connection order, as DiagramExploration does in F#.
    const explorer = root.querySelector("details[data-explore]");
    if (explorer) {
        const field = name => explorer.querySelector(`[data-explore="${name}"]`);
        const result = field("result");
        const label = index => data.nodes[index].id;

        function walk(origin, upstream) {
            const undirected = field("undirected").checked;
            const neighbours = data.nodes.map(() => []);
            for (const connection of data.connections) {
                const plain = undirected && !connection.forward && !connection.backward;
                let along = connection.forward || plain, against = connection.backward || plain;
                if (upstream) [along, against] = [against, along];
                if (along) neighbours[connection.source].push(connection.target);
                if (against) neighbours[connection.target].push(connection.source);
            }
            const previous = new Map([[origin, -1]]);
            const order = [];
            for (let queue = [origin]; queue.length > 0 && order.length <= 10000;) {
                const current = queue.shift();
                order.push(current);
                for (const next of neighbours[current]) {
                    if (!previous.has(next)) { previous.set(next, current); queue.push(next); }
                }
            }
            return { previous, order, exhausted: order.length > 10000 };
        }

        function show(nodes, text) {
            const hits = new Set(nodes.map(index => data.nodes[index].ref));
            for (let index = 1; index < nodes.length; index++) {
                const [a, b] = [nodes[index - 1], nodes[index]];
                const joining = data.connections.find(connection =>
                    connection.source === a && connection.target === b || connection.source === b && connection.target === a);
                if (joining) hits.add(joining.ref);
            }
            for (const element of svg.querySelectorAll("[data-ref]")) element.classList.toggle("explore-hit", hits.has(element.dataset.ref));
            svg.classList.toggle("is-exploring", nodes.length > 0);
            result.textContent = text;
        }

        function reach(upstream) {
            const origin = Number(field("from").value);
            const { order, exhausted } = walk(origin, upstream);
            if (exhausted) { show([], "More than 10,000 elements: the walk stopped."); return; }
            const hits = new Set(order.map(index => data.nodes[index].ref));
            for (const connection of data.connections) {
                if (order.includes(connection.source) && order.includes(connection.target)) hits.add(connection.ref);
            }
            for (const element of svg.querySelectorAll("[data-ref]")) element.classList.toggle("explore-hit", hits.has(element.dataset.ref));
            svg.classList.add("is-exploring");
            result.textContent = `Structurally ${upstream ? "upstream of" : "downstream of"} ${label(origin)}: ${order.map(label).join(", ")}.`;
        }

        field("to").selectedIndex = Math.min(1, data.nodes.length - 1);
        field("route").addEventListener("click", () => {
            const origin = Number(field("from").value), goal = Number(field("to").value);
            const { previous, exhausted } = walk(origin, false);
            if (!previous.has(goal)) {
                show([], exhausted ? "More than 10,000 elements: the walk stopped." : `${label(goal)} is unreachable from ${label(origin)}.`);
                return;
            }
            const route = [];
            for (let current = goal; current >= 0; current = previous.get(current)) route.unshift(current);
            show(route, `Structural route: ${route.map(label).join(" → ")}. Conditions are not evaluated.`);
        });
        field("upstream").addEventListener("click", () => reach(true));
        field("downstream").addEventListener("click", () => reach(false));
        field("clear").addEventListener("click", () => show([], "Pick elements to see structural routes. Conditions are not evaluated."));
    }

    reflectState();
    draw();
})();
