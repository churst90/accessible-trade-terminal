// Accessible presentation for the Blazor circuit reconnect overlay.
//
// ── Why this file exists ───────────────────────────────────────────────────────────
//
// Blazor ships a default reconnect overlay: a plain div, no live region, no focus
// management. Until 2026-09-21 this repository defined no `#components-reconnect-modal`
// of its own, so that default is what a user got — and when the circuit dropped, a blind
// trader was told NOTHING. The `#blazor-error-ui` element right beside it in App.razor
// carries `role="alert" aria-live="assertive"`, so the accessible treatment was clearly
// deliberate for unhandled errors and simply never extended to reconnects.
//
// ── Why the markup alone is not enough ─────────────────────────────────────────────
//
// A live region that is already in the DOM and merely UNHIDDEN does not reliably
// re-announce; screen readers announce CHANGES to a region's text, and showing a node
// whose text never changed is not always one. Worse, the interesting transitions here
// (attempt 1 → 2 → 3 → failed) are exactly the ones a user needs and exactly the ones a
// show/hide carries no information about.
//
// So the overlay is presentational and this observer owns the announcement: it watches
// the framework's own class changes and REWRITES the text of a dedicated status node, so
// every transition is a genuine text change with a full sentence in it.
//
// ── What the sentences say ─────────────────────────────────────────────────────────
//
// For a trader the urgent question when the screen goes quiet is whether an order or a
// layout was lost. The copy answers it rather than leaving them to guess, and it
// distinguishes "the server is restarting" (which the proxy tells us via a 503 carrying
// {"error":"terminal_restarting"}) from "your connection dropped", because those call for
// different reactions and only one of them is the user's problem.

// Self-starting: the overlay element is in the document before this script runs (it sits above
// the script tags in App.razor), so there is nothing to wait for but the parser. The retry
// timing the overlay reports is boot.js's back-off, not the framework default.
window.terminalReconnect = (function () {
    'use strict';

    // 'resume-failed' is .NET 10's: the circuit came back but the session could not be resumed,
    // which to the user is the same news as 'rejected'.
    var STATES = ['show', 'hide', 'failed', 'rejected', 'resume-failed', 'refused'];

    // ── Two voices, and why ──────────────────────────────────────────────────────────
    //
    // The framework rewrites the attempt counter's text once a SECOND while it waits (it is a
    // countdown), and until 2026-09-24 every rewrite was re-announced: the whole ~25-word
    // sentence, assertively, each one cutting off the last, for as long as the retries ran.
    // With the back-off that is about seven minutes. An accessibility review measured it at 7
    // rewrites in 8 ticks.
    //
    // So: the SENTENCE for each state change goes to #reconnect-status (assertive), once. The
    // PROGRESS ("still reconnecting, attempt 6 of 30") goes to #reconnect-progress (polite), and
    // only when the attempt number has changed and a minute has passed since the last one.
    var PROGRESS_INTERVAL_MS = 60000;

    function node(id) { return document.getElementById(id); }

    function attempts() {
        var cur = node('components-reconnect-current-attempt');
        var max = node('components-reconnect-max-retries');
        return {
            current: cur && cur.textContent ? cur.textContent.trim() : '',
            max: max && max.textContent ? max.textContent.trim() : ''
        };
    }

    function stateOf(el) {
        for (var i = 0; i < STATES.length; i++) {
            if (el.classList.contains('components-reconnect-' + STATES[i])) return STATES[i];
        }
        return null;
    }

    function sentenceFor(state) {
        switch (state) {
            case 'show':
                // "Nothing has been lost" is the answer to the question a trader is actually
                // asking. The circuit carries UI state, not orders: anything already sent to a
                // venue is at the venue.
                return 'Connection to the terminal was lost. Reconnecting.' +
                       ' Your chart and any orders already placed are unaffected.';
            case 'failed':
                return 'Could not reconnect to the terminal. The server may be restarting. ' +
                       'Press the Retry now button to try again, or wait and it will retry on its own.';
            case 'rejected':
            case 'resume-failed':
                return 'Your session ended while you were disconnected. ' +
                       'Press the Reload the terminal button to start a new session. ' +
                       'Orders already placed are held at the venue and are not affected.';
            case 'refused':
                return 'The terminal refused the reconnection. Reload the page to start a new session.';
            case 'hide':
                return 'Reconnected to the terminal.';
            default:
                return '';
        }
    }

    // Writes text so that it is a CHANGE even when it equals what the node already holds (a
    // second drop after "Reconnected" is a different sentence, but a failed-then-failed is
    // not): empty the node, then fill it on the next frame.
    function write(id, text) {
        var n = node(id);
        if (!n || !text) return;
        if (n.textContent === text) {
            n.textContent = '';
            (window.requestAnimationFrame || setTimeout)(function () { n.textContent = text; });
        } else {
            n.textContent = text;
        }
    }

    return {
        PROGRESS_INTERVAL_MS: PROGRESS_INTERVAL_MS,
        start: function () {
            var modal = node('components-reconnect-modal');
            if (!modal || typeof MutationObserver === 'undefined') return;

            var lastState = null;
            var lastAttempt = '';
            var lastProgressAt = 0;

            var observer = new MutationObserver(function () {
                var state = stateOf(modal);
                if (state !== lastState) {
                    lastState = state;
                    lastAttempt = attempts().current;
                    lastProgressAt = Date.now();
                    write('reconnect-status', sentenceFor(state));
                    return;
                }
                if (state !== 'show') return;

                // Same state: only the countdown ticked, or the attempt number moved.
                var a = attempts();
                if (!a.current || a.current === lastAttempt) return;
                lastAttempt = a.current;
                if (Date.now() - lastProgressAt < PROGRESS_INTERVAL_MS) return;
                lastProgressAt = Date.now();
                write('reconnect-progress', 'Still reconnecting. Attempt ' + a.current +
                      (a.max ? ' of ' + a.max : '') + '.');
            });

            // The class attribute on the modal is what the framework drives; the attempt
            // counter is a separate text node it rewrites every second.
            observer.observe(modal, {
                attributes: true,
                attributeFilter: ['class'],
                subtree: true,
                childList: true,
                characterData: true
            });
        }
    };
})();

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () { window.terminalReconnect.start(); });
} else {
    window.terminalReconnect.start();
}
