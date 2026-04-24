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

            // Kick off an initial report after the current layout pass settles.
            scheduleReport();
        },
        stop: function () {
            if (!started) return;
            started = false;
            window.removeEventListener('resize', scheduleReport);
            if (ro) { try { ro.disconnect(); } catch (e) { } ro = null; }
            dotnetRef = null;
        }
    };
})();
