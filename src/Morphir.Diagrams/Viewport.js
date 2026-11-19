// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

(() => {
    "use strict";
    const root = document.currentScript.closest("[data-diagram-viewer]");
    const canvas = root.querySelector("[data-viewport-root]");
    const viewport = canvas.querySelector("[data-viewport]");
    const content = viewport.querySelector("[data-viewport-content]");
    const svg = content.querySelector("svg");
    const toolbar = canvas.querySelector("[data-viewport-toolbar]");
    const dialog = root.querySelector("[data-viewport-dialog]");
    const control = name => toolbar.querySelector(`[data-view-control="${name}"]`);
    const zoomIn = control("zoom-in");
    const zoomOut = control("zoom-out");
    const fullscreen = control("fullscreen");
    const minimum = .25;
    const maximum = 8;
    const box = svg.viewBox.baseVal;
    let scale = 1, x = 0, y = 0, width = 0, height = 0;
    let pointer = null;
    let expanded = null;
    let switching = false;

    const clamp = (value, minimum, maximum) => Math.max(minimum, Math.min(maximum, value));

    function draw() {
        const limitX = Math.max(0, (width * scale - viewport.clientWidth) / 2);
        const limitY = Math.max(0, (height * scale - viewport.clientHeight) / 2);
        x = clamp(x, -limitX, limitX);
        y = clamp(y, -limitY, limitY);
        content.style.transform = `translate(-50%, -50%) translate(${x}px, ${y}px) scale(${scale})`;
        viewport.classList.toggle("diagram-viewport--pannable", scale > 1);
        control("scale").value = `${Math.round(scale * 100)}%`;
        zoomIn.disabled = scale >= maximum;
        zoomOut.disabled = scale <= minimum;
    }

    function measure() {
        const widthAvailable = Math.max(1, viewport.clientWidth - 16);
        const heightAvailable = Math.max(1, viewport.clientHeight - 16);
        const ratio = Math.min(widthAvailable / box.width, heightAvailable / box.height);
        width = box.width * ratio;
        height = box.height * ratio;
        content.style.width = `${width}px`;
        content.style.height = `${height}px`;
        draw();
    }

    function zoom(factor) {
        scale = clamp(Math.round(scale * factor * 10000) / 10000, minimum, maximum);
        draw();
    }

    function reflectFullscreen(open) {
        fullscreen.textContent = open ? "Exit fullscreen" : "Fullscreen";
        fullscreen.setAttribute("aria-pressed", String(open));
    }

    function restorePage() {
        if (!expanded) return;
        const { scrollX, scrollY } = expanded;
        expanded = null;
        reflectFullscreen(false);
        measure();
        fullscreen.focus({ preventScroll: true });
        window.scrollTo({ left: scrollX, top: scrollY, behavior: "instant" });
    }

    async function toggleFullscreen() {
        if (switching) return;
        switching = true;
        try {
            if (document.fullscreenElement === canvas) {
                await document.exitFullscreen();
            } else if (dialog.open) {
                dialog.close();
            } else {
                expanded = { scrollX: window.scrollX, scrollY: window.scrollY };
                if (canvas.requestFullscreen) {
                    try {
                        await canvas.requestFullscreen();
                        return;
                    } catch { /* The dialog provides the same viewport when native fullscreen is denied. */ }
                }
                const placeholder = document.createComment("Diagram viewport");
                canvas.replaceWith(placeholder);
                expanded.placeholder = placeholder;
                dialog.append(canvas);
                dialog.showModal();
                reflectFullscreen(true);
                measure();
                fullscreen.focus({ preventScroll: true });
            }
        } finally {
            switching = false;
        }
    }

    document.addEventListener("fullscreenchange", () => {
        if (document.fullscreenElement === canvas) {
            reflectFullscreen(true);
            measure();
            fullscreen.focus({ preventScroll: true });
        } else if (!dialog.open) {
            restorePage();
        }
    });
    dialog.addEventListener("close", () => {
        expanded?.placeholder?.replaceWith(canvas);
        restorePage();
    });

    zoomIn.addEventListener("click", () => zoom(1.25));
    zoomOut.addEventListener("click", () => zoom(1 / 1.25));
    control("fit").addEventListener("click", () => { scale = 1; x = y = 0; measure(); });
    fullscreen.addEventListener("click", () => void toggleFullscreen());

    const directions = { ArrowLeft: [40, 0], ArrowRight: [-40, 0], ArrowUp: [0, 40], ArrowDown: [0, -40] };
    viewport.addEventListener("keydown", event => {
        const direction = directions[event.key];
        if (event.target !== viewport || scale <= 1 || !direction) return;
        event.preventDefault();
        x += direction[0];
        y += direction[1];
        draw();
    });
    viewport.addEventListener("pointerdown", event => {
        if (scale <= 1 || !event.isPrimary || event.button !== 0 || event.target.closest("a")) return;
        pointer = { id: event.pointerId, startX: event.clientX, startY: event.clientY, x, y };
        viewport.setPointerCapture(event.pointerId);
        viewport.focus({ preventScroll: true });
    });
    viewport.addEventListener("pointermove", event => {
        if (pointer?.id !== event.pointerId) return;
        x = pointer.x + event.clientX - pointer.startX;
        y = pointer.y + event.clientY - pointer.startY;
        draw();
    });
    for (const event of ["pointerup", "pointercancel", "lostpointercapture"]) {
        viewport.addEventListener(event, release => {
            if (pointer?.id === release.pointerId) pointer = null;
        });
    }

    toolbar.hidden = false;
    canvas.dataset.viewportReady = "true";
    new ResizeObserver(measure).observe(viewport);
    measure();
})();
