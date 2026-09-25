// Starts the Blazor circuit with a reconnection back-off.
//
// ── Why this exists ─────────────────────────────────────────────────────────────────
//
// The framework's default retry policy (blazor.web.js, .NET 10) is ten attempts with NO
// delay, then ten at 5 s, then 30 s:
//
//     retryIntervalMilliseconds: (n, max) => max && n >= max ? null : n < 10 ? 0 : n < 20 ? 5000 : 30000
//
// That is the storm in the server's logs: 100 of 141 negotiate requests returned 502
// between 7 and 20 September, in bursts of ten-plus within one second, from a browser
// sitting on an open page while the app restarted. Ten zero-delay retries all land inside
// a restart window, so none of them can succeed. They just add load at the worst moment.
//
// ── Why it is a FILE and not an inline <script> ──────────────────────────────────────
//
// The first attempt (2026-09-21) put `Blazor.start({...})` inline in App.razor with
// `autostart="false"`, and the circuit never booted. The cause, established 2026-09-24
// with the browser harness's console capture: the WebHost's CSP is `script-src 'self'`
// (SecurityHeadersPolicy), so the inline start call was refused. With autostart off and
// the start call blocked, nothing started the circuit. A same-origin file is allowed and
// keeps the CSP strict.
//
// ── The policy ───────────────────────────────────────────────────────────────────────
//
// One immediate attempt, because most drops are a network blip and the user should not
// wait for those. Then 1 s, 2 s, 4 s, 8 s, capped at 15 s: the nginx front answers the
// transport with `Retry-After: 15` while the app restarts, so 15 s is the server's own
// answer to "how long". ±20% jitter, so every open tab does not retry on the same beat
// after a restart. Thirty attempts is about seven minutes, after which the overlay offers
// "Retry now". The framework also retries at once when a hidden tab becomes visible again,
// whatever this policy says, so a user coming back to the tab is not kept waiting.

window.terminalBoot = (function () {
    'use strict';

    var MAX_RETRIES = 30;
    var FIRST_DELAY_MS = 1000;
    var CAP_MS = 15000;
    var JITTER = 0.2;

    // previousAttempts: how many attempts have already failed (0 for the first).
    // random: a number in [0, 1); injectable so the policy can be tested without Math.random.
    function retryDelay(previousAttempts, random) {
        if (previousAttempts >= MAX_RETRIES) return null;
        if (previousAttempts <= 0) return 0;
        var base = Math.min(CAP_MS, FIRST_DELAY_MS * Math.pow(2, previousAttempts - 1));
        var r = typeof random === 'number' ? random : Math.random();
        return Math.round(base * (1 + JITTER * (2 * r - 1)));
    }

    var started = false;

    function start() {
        if (started || typeof Blazor === 'undefined') return;
        started = true;
        Blazor.start({
            circuit: {
                reconnectionOptions: {
                    maxRetries: MAX_RETRIES,
                    retryIntervalMilliseconds: function (previousAttempts) {
                        return retryDelay(previousAttempts);
                    }
                }
            }
        });
    }

    return {
        MAX_RETRIES: MAX_RETRIES,
        CAP_MS: CAP_MS,
        retryDelay: retryDelay,
        start: start,
        get started() { return started; }
    };
})();

window.terminalBoot.start();
