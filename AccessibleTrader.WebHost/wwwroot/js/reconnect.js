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
// the script tags in App.razor), so there is nothing to wait for but the parser. Started here
// rather than from a Blazor.start() callback because the boot path is deliberately left at its
// default — see the note in App.razor.
window.terminalReconnect = (function () {
    'use strict';

    var STATES = ['show', 'hide', 'failed', 'rejected', 'refused'];

    function statusNode() {
        return document.getElementById('reconnect-status');
    }

    function attemptText() {
        var cur = document.getElementById('components-reconnect-current-attempt');
        var max = document.getElementById('components-reconnect-max-retries');
        var c = cur && cur.textContent ? cur.textContent.trim() : '';
        var m = max && max.textContent ? max.textContent.trim() : '';
        if (c && m) return ' Attempt ' + c + ' of ' + m + '.';
        if (c) return ' Attempt ' + c + '.';
        return '';
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
                return 'Connection to the terminal was lost. Reconnecting.' + attemptText() +
                       ' Your chart and any orders already placed are unaffected.';
            case 'failed':
                return 'Could not reconnect to the terminal. The server may be restarting. ' +
                       'Press the Retry now button to try again, or wait and it will retry on its own.';
            case 'rejected':
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

    var lastAnnounced = '';

    function announce(text) {
        var node = statusNode();
        if (!node || !text) return;
        // An IDENTICAL string written twice is not a change and may not be announced. A
        // reconnect attempt that stalls and retries at the same attempt number is a real case,
        // so nudge it with a trailing space rather than letting the update be swallowed.
        if (text === lastAnnounced) text += ' ';
        lastAnnounced = text;
        node.textContent = text;
    }

    return {
        start: function () {
            var modal = document.getElementById('components-reconnect-modal');
            if (!modal || typeof MutationObserver === 'undefined') return;

            var lastState = null;
            var observer = new MutationObserver(function () {
                var state = stateOf(modal);
                if (state === lastState && state !== 'show') return;
                lastState = state;
                announce(sentenceFor(state));
            });

            // The class attribute on the modal is what the framework drives; the attempt
            // counters are separate text nodes it also updates, and a retry that only bumps the
            // counter must still be announced — hence subtree/characterData as well.
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
