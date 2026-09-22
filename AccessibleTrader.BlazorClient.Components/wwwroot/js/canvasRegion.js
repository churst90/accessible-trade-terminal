// Publishes the chart-interact-zone element's bounding rect (in CSS pixels,
// relative to the WebView viewport) to the .NET host whenever the layout
// changes. Called from ChartArea.razor on mount. Reports once immediately and
// then on every ResizeObserver tick for the chart element plus window resize.
window.canvasRegion = (function () {
    let started = false;
    let dotnetRef = null;
    let ro = null;
    let rafHandle = 0;

    function report() {
        const el = document.getElementById('chart-interact-zone');
        if (!el || !dotnetRef) return;
        const rect = el.getBoundingClientRect();
        const top = Math.max(0, rect.top);
        const bottom = Math.max(0, window.innerHeight - rect.bottom);
        dotnetRef.invokeMethodAsync('OnCanvasRegionChanged', top, bottom);
    }

    function scheduleReport() {
        if (rafHandle) return;
        rafHandle = requestAnimationFrame(() => {
            rafHandle = 0;
            report();
        });
    }

    return {
        start: function (ref) {
            if (started) return;
            started = true;
            dotnetRef = ref;

            const el = document.getElementById('chart-interact-zone');
            if (el && 'ResizeObserver' in window) {
                ro = new ResizeObserver(scheduleReport);
                ro.observe(el);
            }
            window.addEventListener('resize', scheduleReport);

            // SCROLL, and it matters as much as resize: the rect this reports is a VIEWPORT
            // rect, so it moves whenever anything between the chart and the viewport scrolls —
            // not only when something is resized. Before 2026-09-21 nothing scrolled, so the
            // omission was invisible; the chart-area minimum height added that day means the
            // page CAN scroll on a short window, and without this the native Skia canvas on the
            // desktop head would stay where it was painted while the interaction zone slid out
            // from under it. Capture phase, because the scrolling element may be an ancestor and
            // scroll does not bubble. Passive, because this only reads layout.
            window.addEventListener('scroll', scheduleReport, { capture: true, passive: true });

            // Kick off an initial report after the current layout pass settles.
            scheduleReport();
        },
        stop: function () {
            if (!started) return;
            started = false;
            window.removeEventListener('resize', scheduleReport);
            window.removeEventListener('scroll', scheduleReport, { capture: true });
            if (ro) { try { ro.disconnect(); } catch (e) { } ro = null; }
            dotnetRef = null;
        }
    };
})();
