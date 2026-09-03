// Widget roles the browser (or the widget's own key handler) activates with Space.
// Native <button> and <summary> are matched by tag; everything else declares a role.
//
// <a href> is deliberately ABSENT: Space does not activate a link in any browser — it
// scrolls — so excluding links would cost the chart-play shortcut and buy no activation.
const SPACE_ACTIVATION_ROLES = [
    'button', 'checkbox', 'switch', 'radio', 'tab', 'option', 'treeitem',
    'menuitem', 'menuitemcheckbox', 'menuitemradio',
];

/**
 * Whether pressing Space on this element is meant to activate it, rather than to
 * reach the chart-playback shortcut. Exported on window for the JS test suite.
 */
function isSpaceActivationTarget(target) {
    if (!target) return false;

    const tag = target.tagName;
    if (tag === 'BUTTON' || tag === 'SUMMARY') return true;

    // A disabled control activates nothing, so the shortcut may still have it.
    if (target.hasAttribute && target.hasAttribute('disabled')) return false;

    const role = target.getAttribute ? target.getAttribute('role') : null;
    return !!role && SPACE_ACTIVATION_ROLES.indexOf(role) >= 0;
}

// Keys that scroll a scrollable container. Trapped for the chart, but released back to the
// browser while a modal owns the keyboard — see the guard in the keydown handler.
const SCROLL_KEYS = [
    'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown',
    'Home', 'End', 'PageUp', 'PageDown',
];

// Composite widgets that consume arrow keys themselves. Focus inside one of these keeps the
// trapped behaviour even while a modal is open, because the widget's own handler needs the
// key and relies on this file to suppress the browser's scroll.
const ARROW_WIDGET_SELECTOR =
    '[role="tablist"], [role="tab"], [role="tree"], [role="treeitem"], ' +
    '[role="listbox"], [role="option"], [role="menu"], [role="menuitem"], ' +
    '[role="radiogroup"], [role="slider"], [role="spinbutton"], [role="grid"]';

window.accessibleTrader = {

    // Exposed for tests; the keydown trap calls the module-scope function directly.
    _isSpaceActivationTarget: isSpaceActivationTarget,

    // Exposed for tests: the scroll-key release must be provably scoped.
    _scrollKeys: SCROLL_KEYS,
    _arrowWidgetSelector: ARROW_WIDGET_SELECTOR,

    // THE ordered modal stack, bottom first, as pushed by MainLayout from the C# ModalStack
    // on every open/close: `[{ name, returnTo }]`. `name` is the ModalName the dialog
    // published and wears as `data-modal-name`; `returnTo` is the element that had focus
    // when the entry was pushed — the control that opened it, because the push arrives
    // before the render that shows the dialog — and is where focus goes back to when that
    // dialog closes while others remain open. When non-empty, the Tab trap is armed, and the
    // top entry's name is resolved to a dialog element at trap time by the ARIA dialog-family
    // lookup (role="dialog" OR role="alertdialog"). Keep that selector and this comment in
    // step — they drifted once already.
    //
    // This replaced a bare counter fed by `setModalOpen(true|false)`. A counter can say
    // whether a modal is open; it cannot say which one is on top, and "top" was being read
    // off DOM order instead — which is MainLayout's constant render order, not open order.
    _modalStack: [],

    // Derived from _modalStack.length. Kept because the browser harness and the scan guards
    // read it as "the app's own count of open modals".
    _openModalCount: 0,

    // Tracks whether the chart element currently has keyboard focus. Driven by
    // ChartArea's @onfocus/@onblur handlers which call setChartFocused(true|false).
    // Used to gate single-letter chart commands (h, m, p, drawing-tool letters)
    // so they only trap keystrokes when the chart is the active focus target.
    // Without this, typing 'h' inside any custom (non-native) input would trigger
    // the "hide" command instead of inserting the letter.
    //
    // Starts FALSE because the app launches with focus on the WebView's banner
    // heading, not the chart — chart commands should not fire until the user has
    // explicitly put focus into the chart (via Tab, mouse click, or
    // Ctrl+Alt+Shift+C).
    _chartFocused: false,

    // True once registerKeyboardHandler has finished wiring the window-level listeners.
    //
    // This exists because "the page has loaded" and "a keystroke reaches the app" are two
    // different moments on the web host: the markup arrives from the server render, then the
    // Blazor circuit connects, and only then does GlobalInputService.InitializeAsync call into
    // here. A keystroke sent in between is silently dropped — there is no listener yet — which
    // on a slow first connect looks exactly like a broken shortcut.
    //
    // The browser harness (AccessibleTrader.BrowserTests) waits on this before pressing
    // anything, so a failure there means the shortcut is wrong rather than early.
    _inputReady: false,

    /**
     * Called from .NET (CommandDispatcher / ChartFocusService) when the focus ring
     * enters or leaves the chart region. When false, single-letter chart commands
     * are skipped in the keydown trap; modifier chords and function keys still fire.
     */
    setChartFocused: function (focused) {
        this._chartFocused = !!focused;
    },

    /**
     * Called from MainLayout with the WHOLE ordered stack of open modal names, bottom
     * first, after every open or close. Replaces what was known before rather than
     * adjusting it, so one dropped call cannot leave this side a modal out of step for
     * the rest of the session.
     *
     * Exactly one entry changes per call in practice, and the diff below assumes at
     * most one push or one removal; anything else (or a disagreement about the entries
     * that should have stayed) rebuilds from the names alone, with no return targets —
     * the trap still works, only the close-time focus return degrades to the heading.
     *
     * On a removal that leaves other modals open, focus is put back where it was when
     * the removed modal opened, provided that element is still rendered and inside the
     * dialog now on top; otherwise the top dialog's labelling heading. When the LAST
     * modal closes nothing is done here — CommandDispatcher publishes
     * RequestChartFocusEvent and the chart takes focus, as before.
     */
    setModalStack: function (names) {
        names = Array.isArray(names) ? names.map(n => (n === null || n === undefined) ? null : String(n)) : [];
        const prev = this._modalStack;
        let next = null;
        let removed = null;

        // First index at which the two sequences disagree (names.length / prev.length when
        // one is a prefix of the other).
        let d = 0;
        while (d < prev.length && d < names.length && prev[d].name === names[d]) d++;

        if (names.length === prev.length + 1 && prev.slice(d).every((e, k) => e.name === names[d + 1 + k])) {
            next = prev.slice(0, d);
            next.push({ name: names[d], returnTo: document.activeElement });
            next = next.concat(prev.slice(d));
        } else if (names.length === prev.length - 1 && prev.slice(d + 1).every((e, k) => e.name === names[d + k])) {
            removed = prev[d];
            next = prev.slice(0, d).concat(prev.slice(d + 1));
        } else if (names.length === prev.length && d === names.length) {
            next = prev;                                    // nothing changed
        } else {
            // Out of step (two changes at once, a dropped call, a re-open that moved an entry to
            // the top): rebuild from the names, keeping the return target of every entry this
            // side still recognises by name. Only entries it has never seen lose theirs.
            next = names.map(n => ({ name: n, returnTo: (prev.find(e => e.name === n) || {}).returnTo || null }));
        }

        this._modalStack = next;
        this._openModalCount = next.length;

        if (removed && next.length > 0) this._returnFocusAfterClose(removed);
    },

    /**
     * After a stacked close: back to the control that opened the closed dialog, if it is
     * still rendered and inside the dialog now on top; else that dialog's heading.
     *
     * The call arrives before the render that removes the closed dialog, so this runs
     * while the old dialog is still in the DOM with focus inside it — which is fine, the
     * element being focused is in the dialog beneath and stays. Without this, the closing
     * render removed the focused element and the browser dropped focus on <body>, inside
     * no dialog, with a dialog still open and aria-modal telling the screen reader not to
     * describe anything outside it.
     */
    _returnFocusAfterClose: function (removed) {
        const dialogs = this._visibleDialogs();
        const top = this._topDialog(dialogs);

        // The removed entry was not the top (a parent closed underneath its child, or a close
        // event arrived late): the user is already in the dialog that is on top, and moving
        // them to its heading would be its own focus bug. Leave them where they are.
        if (top && top.contains(document.activeElement)) return;

        const target = removed.returnTo;
        const usable = target && target.isConnected && target.focus
                    && !(target.hasAttribute && target.hasAttribute('disabled'))
                    && target.getClientRects().length > 0
                    && (top ? top.contains(target) : dialogs.some(dlg => dlg.contains(target)));
        if (usable) {
            target.focus();
            if (document.activeElement === target) return;   // else fall through to the heading
        }
        if (!top) return;
        const labelId = top.getAttribute('aria-labelledby');
        const heading = labelId ? document.getElementById(labelId) : null;
        (heading && heading.focus ? heading : top).focus();
    },

    /** Every rendered element of the ARIA dialog family, in DOM order. */
    _visibleDialogs: function () {
        return Array.prototype.slice.call(
                document.querySelectorAll('[role="dialog"], [role="alertdialog"]'))
            .filter(el => el.getClientRects().length > 0);
    },

    /**
     * The dialog element for the top of the modal stack: the rendered dialog wearing the
     * top entry's name as `data-modal-name`, or — if that entry has no dialog element (a
     * context menu is on the stack but is a role="menu", or the dialog has not rendered
     * yet) — the next entry down that has one. Null when no entry resolves, which is the
     * caller's cue to fall back to containment and then DOM order.
     */
    _topDialog: function (dialogs) {
        const stack = this._modalStack;
        for (let i = stack.length - 1; i >= 0; i--) {
            const name = stack[i].name;
            if (name === null) continue;
            const el = dialogs.find(dlg => dlg.getAttribute('data-modal-name') === name);
            if (el) return el;
        }
        return null;
    },

    /**
     * Registers a global keydown handler that captures navigation and shortcut keys
     * and forwards them to the .NET keyboard pipeline via JSInterop.
     *
     * WHY GLOBAL: Blazor's Razor onkeydown only fires when a specific element is focused.
     * For a keyboard-first accessibility app, F1-F12 and arrow keys must work regardless
     * of which element is focused — including when the user is in the toolbar dropdowns.
     *
     * EXCLUSIONS: Normal text input in <input>/<textarea>/<select> is preserved unless
     * a modifier key (Ctrl/Alt) is held, which is always a shortcut.
     */
    registerKeyboardHandler: function (dotnetHelper) {
        const self = this;

        // ── Tab trap ────────────────────────────────────────────────────────────────
        // Runs in capture phase ahead of the shortcut dispatcher. When a modal is
        // open, keep Tab/Shift+Tab inside the modal's focusable element list rather
        // than letting focus escape to the toolbar behind the overlay. The modal
        // element is discovered at trap time as the last visible `role="dialog"` —
        // last wins so stacked modals trap correctly (the topmost is active).
        //
        // The selector must cover the WHOLE ARIA dialog family, not just role="dialog".
        // ModalContractScanTests was widened to {dialog, alertdialog} on 2026-08-29 after a
        // recorded live miss, and this selector was not widened with it — so the C# scanner
        // knew about alertdialog and the JavaScript did not. The one overlay that used the
        // role was Toolbar's destructive "strip your indicators and drawings" confirmation:
        // it publishes ModalStateChangedEvent, so _openModalCount went above zero and the
        // trap armed, then querySelectorAll found nothing and the trap returned — leaving
        // Tab free to walk out of an unanswered destructive prompt onto the Load button
        // that raised it. A role the trap does not know is a way out of the trap.
        //
        // Widening the selector did NOT trap it, and the commit that widened it said it had.
        // The next line used to filter on `el.offsetParent !== null`, and CSSOM-View defines
        // offsetParent as null for an element that is itself `position: fixed` — which is
        // exactly how Toolbar's alertdialog is styled. ModalBase dialogs survived that filter
        // only because THEIR position:fixed is on the parent .modal-overlay. So the selector
        // found the alertdialog and the filter one line below threw it away; reproduced in a
        // real Chromium, zero dialogs seen. offsetParent is not a visibility test. An element
        // is rendered iff it has at least one layout box, and getClientRects() reports exactly
        // that — empty for display:none (and for anything inside a display:none ancestor),
        // non-empty for fixed, sticky, absolute and static alike.
        //
        // A filter one line below a widened selector is invisible to a scan that reads the
        // selector string. The guard in ChromeAccessibilityScanTests now reads this whole
        // block for `offsetParent`, and keyboard-tests.mjs has a node that is fixed and
        // therefore offsetParent-less, so this cannot regress silently in either direction.
        window.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab') return;
            if (self._modalStack.length === 0) return;
            const dialogs = self._visibleDialogs();
            if (dialogs.length === 0) return;

            // The dialog to keep focus in is the TOP OF THE MODAL STACK — the same stack, in
            // the same open order, that CommandDispatcher aims Escape by. Its top name is
            // resolved to the rendered dialog wearing it as data-modal-name.
            //
            // It used to be `dialogs[dialogs.length - 1]`: DOM order, which is fixed by
            // MainLayout, where HelpModal is rendered before nineteen other modals. F1 is in
            // the dispatcher's allowedWhileModalOpen list, so Settings then F1 stacked Help on
            // top of Settings while Settings was still the last dialog in the document. From
            // anywhere in Help, the first Tab was judged "outside the modal" and this trap
            // itself moved focus INTO Settings — underneath the dialog the user was reading.
            // The interim mitigation (prefer the dialog containing focus) still rehomed a
            // focus that had left every dialog — a click on the overlay — into Settings.
            //
            // The fallbacks are for a stack entry with no dialog element (role="menu" context
            // menus are on the stack; a dialog not yet rendered): the dialog containing focus,
            // then DOM-last, as before.
            const active0 = document.activeElement;
            const modal = self._topDialog(dialogs)
                       || dialogs.find(d => active0 && d.contains(active0))
                       || dialogs[dialogs.length - 1];

            // This list must match the BROWSER'S real tab order, not merely look sensible, because
            // of the `idx === -1` branch below: an element that is inside the dialog and IS a tab
            // stop, but is missing from this selector, gets treated as an escape and snapped back
            // to `first`. Focus then bounces on one element forever, in both directions.
            //
            // `summary` is how that shipped. HelpModal is built from 37 <details> blocks and
            // <summary> is focusable by default, so the browser saw ~19 tab stops and this
            // selector saw 2 — every Tab landed on a summary, hit the snap-back, and returned to
            // the same control. The keyboard reference became unreadable in the same release that
            // released the scroll keys to make it readable. Caught by the CI browser job, which is
            // the only place this file's behaviour can be observed.
            //
            // The others are here for the same reason and not because anything uses them today:
            // each is a default tab stop, so each is a future instance of the identical bug.
            //
            // Every tag clause carries `:not([tabindex="-1"])`, because the browser honours a
            // negative tabindex on ANY element and so must this list. The unconditional
            // `summary` clause that fixed HelpModal opened the mirror-image hole in
            // ObjectTreeModal: its pane headers are <summary role="treeitem"> under a roving
            // tabindex, and treeKeyboard.js sets every treeitem but the current one to -1. On a
            // hosted build nothing focusable precedes the tree, so after Alt+O, Tab, ArrowDown
            // the current series div was index 1 of THIS list (the roved-out summary was
            // index 0), no branch fired, and the browser walked backward past the summary and
            // the heading — both -1 — out of the dialog. A fix that widens a selector needs the
            // same exclusions on every new clause.
            const focusableSelector =
                'a[href]:not([tabindex="-1"]), area[href]:not([tabindex="-1"]), ' +
                'button:not([disabled]):not([tabindex="-1"]), ' +
                'input:not([disabled]):not([type="hidden"]):not([tabindex="-1"]), ' +
                'select:not([disabled]):not([tabindex="-1"]), textarea:not([disabled]):not([tabindex="-1"]), ' +
                'summary:not([tabindex="-1"]), iframe:not([tabindex="-1"]), ' +
                'audio[controls]:not([tabindex="-1"]), video[controls]:not([tabindex="-1"]), ' +
                '[contenteditable]:not([contenteditable="false"]):not([tabindex="-1"]), ' +
                '[tabindex]:not([tabindex="-1"])';
            const focusables = Array.prototype.slice.call(modal.querySelectorAll(focusableSelector))
                .filter(el => !el.hasAttribute('disabled') && el.getClientRects().length > 0);
            if (focusables.length === 0) return;

            const first = focusables[0];
            const last  = focusables[focusables.length - 1];
            const active = document.activeElement;

            // Trap on CONTAINMENT AND POSITION, never on identity with first/last.
            //
            // The previous form tested `active === first` / `active === last`, which has a hole
            // exactly one keystroke wide at the moment every dialog opens. ModalBase focuses the
            // <h2 tabindex="-1">, and `focusableSelector` ends with [tabindex]:not([tabindex="-1"])
            // — so the heading is DELIBERATELY not in `focusables`. On open the heading is neither
            // `first` nor `last`, and modal.contains(active) is true, so no branch fired,
            // preventDefault was never called, and the browser ran its own sequential navigation.
            // Backward from the first element of the dialog means backward OUT of it, because
            // nothing focusable precedes the h2 inside .modal-content.
            //
            // The user is then standing on a background control while the dialog still claims
            // aria-modal="true", so the screen reader has restricted its buffer to the dialog and
            // will not describe where they now are. On the Trading Dashboard the nearest control
            // behind is the toolbar's Load button, which reloads the chart out from under an
            // order form.
            //
            // Tab_never_escapes_an_open_dialog stayed green throughout because it only ever
            // pressed Tab. The forward direction genuinely worked; the guard exercised one half
            // of the rule it stated. Its Shift+Tab twin now covers the other half.
            const inside = active && modal.contains(active);
            const idx = inside ? focusables.indexOf(active) : -1;

            if (!inside || idx === -1) {
                // Either focus escaped the modal (a click outside), or it is on an element inside
                // the modal that is not a tab stop — the opening heading being the case that
                // matters. Both mean the browser's own sequential order would leave the dialog,
                // so the trap owns this keystroke outright and seeds the correct end.
                e.preventDefault();
                (e.shiftKey ? last : first).focus();
            } else if (e.shiftKey && idx === 0) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && idx === focusables.length - 1) {
                e.preventDefault();
                first.focus();
            }
        }, true);
        // Capture phase: run before any bubble-phase handler, and before WebView2/browser
        // tries to consume reserved chords like Ctrl+Shift+T, Ctrl+Shift+N, Ctrl+Shift+P.
        // stopImmediatePropagation is used on modifier chords so no downstream handler fires.
        window.addEventListener('keydown', function (e) {
            const trappedKeys = [
                'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown',
                'Home', 'End', 'PageUp', 'PageDown',
                'Delete', 'Escape',
                // Application/Menu key (the dedicated context-menu key on most Windows
                // keyboards). Bound to OpenDrawingContextMenu in ShortcutManager — keyboard
                // parity with right-click.
                'ContextMenu',
                'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7',
                'F9', 'F10', 'F11', 'F12',
                ' ', '[', ']', '{', '}', '\\', '-', '=', '_', '+',
                // Comma / period: step between chart formations. Trapped so they reach .NET
                // without a modifier, and gated on chart focus below exactly like the single
                // letters — they are ordinary printable characters and must stay typable.
                ',', '.',
                // Single-letter chart commands: H (hide), M (mute), P (properties).
                // These are also trapped so they reach .NET even without a modifier.
                // The form-control guard below (isFormControl && !isModified) still
                // allows the user to type h/m/p freely in <input>, <select>, <textarea>.
                'h', 'H', 'm', 'M', 'p', 'P',
                'r', 'R', 'e', 'E', 'g', 'G',
                'w', 'W', 'b', 'B', 'k', 'K', 'j', 'J',
                'a', 'A', 'i', 'I', 's', 'S',
                // Drawing-tool letters (also lowercase for safety when Shift is held).
                't', 'T', 'v', 'V', 'c', 'C', 'f', 'F', 'l', 'L',
                'n', 'N', 'o', 'O', 'q', 'Q', 'u', 'U', 'x', 'X',
                'y', 'Y', 'z', 'Z', 'd', 'D'
            ];

            // AltGr on Windows reports ctrlKey AND altKey — it is how a US-International or
            // German layout types å, €, @. That is not a chord; treating it as one swallowed
            // those characters in every text field, and with the e.code fallback below it
            // would have resolved AltGr+W (å) to Ctrl+Alt+W and opened Load Workspace over
            // the symbol box. getModifierState('AltGraph') is the one signal that tells the
            // two apart.
            const isAltGr = typeof e.getModifierState === 'function' && e.getModifierState('AltGraph') === true;
            const isModified = (e.ctrlKey || e.altKey) && !isAltGr;
            const isShifted = e.shiftKey;
            const isTrapped = trappedKeys.includes(e.key);

            if (!isTrapped && !isModified) return;

            // Allow normal keyboard use inside form controls unless a modifier is held.
            // Escape and the function keys are the exceptions: neither is ever a text-input
            // character, and both must always reach the dispatcher. Escape so a form-heavy modal
            // (e.g. the Sound Designer) can be Escaped out of while focus sits on a
            // <select>/<input>/<textarea>; F-keys because until 2026-09-02 this guard swallowed
            // them too, so F1 in Settings' search box opened nothing, F2 could not mute while
            // typing, and F12 opened the browser's DevTools from the toolbar's own <select>s —
            // the exact controls the comment above promises function keys work from (WCAG 2.1.1).
            // An F-key that is not in trappedKeys (F8) never reaches this line. Shift+F10 is
            // carved back out: it is the keyboard equivalent of right-click, and in a text
            // field the native context menu (paste, spell-check) is what the user wants —
            // the command it maps to (OpenDrawingContextMenu) is chart-scoped and would be
            // dropped by the dispatcher anyway, so trapping it would only cost the menu.
            const tag = e.target.tagName;
            const isFormControl = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
            const isEditable = e.target.isContentEditable === true;
            const isFunctionKey = /^F\d{1,2}$/.test(e.key) && !(e.key === 'F10' && isShifted);
            if ((isFormControl || isEditable) && !isModified && e.key !== 'Escape' && !isFunctionKey) return;

            // Alt+Shift+Arrow nudges a drawing anchor. It is chart-scoped — the dispatcher drops
            // it unless the chart has focus — so inside a text field the only thing trapping it
            // could do is cost the user the field's own binding: on macOS Option+Shift+Arrow is
            // select-by-word. Leave it to the field. (Ctrl+Alt+Shift+G / B are not arrows and
            // print nothing, so they still go through.)
            const isArrowKey = e.key === 'ArrowLeft' || e.key === 'ArrowRight' ||
                               e.key === 'ArrowUp'   || e.key === 'ArrowDown';
            if ((isFormControl || isEditable) && isArrowKey && e.altKey && isShifted && !e.ctrlKey) return;

            // ── Scroll keys belong to the dialog while a dialog owns the keyboard ───
            //
            // These keys are trapped here because they drive the chart. But CommandDispatcher
            // already refuses every one of the resulting commands while a modal is open — its
            // allowedWhileModalOpen list is Escape plus F1-F4 and nothing else — so calling
            // preventDefault() here bought nothing and cost the user the ability to READ a
            // dialog taller than the viewport.
            //
            // .modal-content is `max-height: calc(100vh - 120px); overflow-y: auto`, and focus
            // opens on the <h2 tabindex="-1">. HelpModal's own tab stops are its <summary>
            // headings and Close, with ~400 lines of guide and ten shortcut tables between them.
            // Down and Page Down did nothing at all, so the keyboard reference could not be read
            // by keyboard. Only Tab moved the viewport, which skips every line of prose that is
            // not a control. (This comment first said "exactly two focusable elements" — that was
            // measured with the focusable selector above BEFORE it knew about <summary>, and
            // believing it is what let the pinning defect through.)
            //
            // Composite widgets are excluded: a tablist, tree, listbox, menu, radiogroup or
            // slider consumes arrows itself, and none of the three NavigateTablistAsync callers
            // calls preventDefault — they rely on this handler for it. Releasing the key there
            // would move the tab AND scroll the dialog behind it.
            if (self._openModalCount > 0 && !isModified && SCROLL_KEYS.indexOf(e.key) >= 0) {
                const owner = e.target.closest ? e.target.closest(ARROW_WIDGET_SELECTOR) : null;
                if (!owner) return;
            }

            // ── Space must still activate whatever has focus ────────────────────────
            //
            // Space is trapped here because it plays the chart (SPACE → PlayChart). The
            // exclusion above covers INPUT/TEXTAREA/SELECT but NOT buttons — and Space is
            // how a button is activated. So e.preventDefault() below was cancelling the
            // activation click on every one of the ~200 buttons in the app, plus every
            // <summary> disclosure in Help and My Data.
            //
            // Enter still worked, which is why this survived; so did NVDA and JAWS in
            // BROWSE mode, because they synthesize a click rather than a real keypress.
            // The people it broke are everyone in focus/forms mode, keyboard-only sighted
            // users, switch access and voice control — for whom Space IS the activation key.
            //
            // Scoped to unmodified, unshifted Space, which is the exact combination the
            // browser activates on. Ctrl+Space (PlayPause) and Shift+Space (PlaySeries)
            // are chart commands with no activation behaviour to preserve, so they still
            // fire from anywhere.
            if (e.key === ' ' && !isModified && !isShifted && isSpaceActivationTarget(e.target)) return;

            // Gate single-letter chart commands on chart focus. Function keys and
            // modifier chords still fire everywhere for accessibility. This stops
            // a letter like 'h' from firing the "hide" command when the user is
            // typing into a custom Blazor input that isn't a native INPUT/TEXTAREA.
            // Comma and period are included: they are printable characters bound to the chart
            // formation jump, and firing them from a custom (non-native) editor would eat the
            // user's punctuation.
            const isSingleLetter = !isModified && !isShifted &&
                e.key.length === 1 && /^[a-zA-Z0-9,.]$/.test(e.key);
            if (isSingleLetter && !self._chartFocused) return;

            // For modifier chords (Ctrl/Alt/Ctrl+Shift), hard-stop the event so the WebView
            // doesn't route it to reserved browser shortcuts (reopen tab / new incognito / etc).
            e.preventDefault();
            if (isModified) e.stopImmediatePropagation();

            // Normalize key names to match what ShortcutManager expects.
            let key = e.key;
            // With Alt held, macOS (Option) reports the TRANSFORMED character: Option+Shift+G
            // is '˝' on a US Mac, so the lookup for Ctrl+Alt+Shift+G found nothing and the
            // chord was dead with no announcement. The physical key is in e.code ('KeyG',
            // 'Digit1'), which NormalizeKey already maps to 'G' / '1'. Never for AltGr (see
            // above), and not for a dead key ('Dead' is not a single character, so it falls
            // through unchanged — no shipped chord sits on a dead key on a US Mac today).
            if (e.altKey && !isAltGr && typeof key === 'string' && key.length === 1 && !/^[A-Za-z0-9]$/.test(key)
                && typeof e.code === 'string' && /^(Key[A-Z]|Digit[0-9])$/.test(e.code)) {
                key = e.code;
            }
            if (key === 'ArrowLeft') key = 'LEFT';
            else if (key === 'ArrowRight') key = 'RIGHT';
            else if (key === 'ArrowUp') key = 'UP';
            else if (key === 'ArrowDown') key = 'DOWN';
            else if (key === ' ') key = 'SPACE';
            else if (key === '[') key = 'OEM4';
            else if (key === ']') key = 'OEM6';
            else if (key === '{') key = 'OEM4';
            else if (key === '}') key = 'OEM6';
            else if (key === '\\') key = 'OEM5';
            else if (key === '-') key = 'OEMMINUS';
            else if (key === '=') key = 'OEMPLUS';
            else if (key === '_') key = 'OEMMINUS';
            else if (key === '+') key = 'OEMPLUS';
            else if (key === 'Delete') key = 'DELETE';
            else if (key === 'Escape') key = 'ESCAPE';
            else if (key === 'ContextMenu') key = 'CONTEXTMENU';

            dotnetHelper.invokeMethodAsync('OnKeyDown',
                key.toUpperCase(), e.shiftKey, e.ctrlKey, e.altKey);
        }, true);  // capture phase — runs before bubble and before WebView reserved chords

        // Stop the sustaining navigation audio voice immediately on key release.
        // Only fires for navigation keys — no need to intercept every keyup.
        const navKeys = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'];
        window.addEventListener('keyup', function (e) {
            if (navKeys.includes(e.key)) {
                dotnetHelper.invokeMethodAsync('OnKeyUp', e.key);
            }
        });

        // Last line on purpose: every listener above is attached before anything is told
        // the pipeline is armed. See the _inputReady comment at the top of this object.
        self._inputReady = true;
    },

    /**
     * Registers mouse handlers on the chart interaction zone for drawing tools.
     */
    registerMouseHandler: function (dotnetHelper, elementId) {
        const el = document.getElementById(elementId);
        if (!el) return;

        // Tracks whether a drag started on the chart. Used so a mouse-up that lands
        // OUTSIDE the chart (window listener below) still forwards a MouseUp to .NET —
        // otherwise a pan/draw drag released off-canvas would never terminate.
        let buttonDown = false;

        el.addEventListener('mousedown', function (e) {
            buttonDown = true;
            const rect = el.getBoundingClientRect();
            dotnetHelper.invokeMethodAsync('OnMouseEvent',
                e.clientX - rect.left, e.clientY - rect.top, 'MouseDown', rect.width, rect.height);
        });

        // MouseMove fires unconditionally (regardless of button state) so the live
        // drawing preview can track the cursor during click-click placement, not just
        // click-drag. Throttled via requestAnimationFrame so at most one .NET dispatch
        // happens per paint tick — keeps the drag-preview smooth without flooding the
        // JS interop bridge on a fast mouse.
        let moveRafHandle = 0;
        let pendingMoveX = 0, pendingMoveY = 0;
        el.addEventListener('mousemove', function (e) {
            const rect = el.getBoundingClientRect();
            pendingMoveX = e.clientX - rect.left;
            pendingMoveY = e.clientY - rect.top;
            if (moveRafHandle) return;
            moveRafHandle = requestAnimationFrame(function () {
                moveRafHandle = 0;
                dotnetHelper.invokeMethodAsync('OnMouseEvent',
                    pendingMoveX, pendingMoveY, 'MouseMove', rect.width, rect.height);
            });
        });

        el.addEventListener('mouseup', function (e) {
            buttonDown = false;
            const rect = el.getBoundingClientRect();
            // Shift+click measures a range (spoken summary) instead of selecting.
            const type = e.shiftKey ? 'ShiftMouseUp' : 'MouseUp';
            dotnetHelper.invokeMethodAsync('OnMouseEvent',
                e.clientX - rect.left, e.clientY - rect.top, type, rect.width, rect.height);
        });

        // Release outside the chart still ends the drag. The element's own mouseup
        // (target phase) fires first and clears buttonDown, so a release over the chart
        // is not double-reported here; only off-canvas releases reach this branch.
        window.addEventListener('mouseup', function (e) {
            if (!buttonDown) return;
            buttonDown = false;
            const rect = el.getBoundingClientRect();
            const type = e.shiftKey ? 'ShiftMouseUp' : 'MouseUp';
            dotnetHelper.invokeMethodAsync('OnMouseEvent',
                e.clientX - rect.left, e.clientY - rect.top, type, rect.width, rect.height);
        });

        // Right-click → suppress the browser context menu and forward the cursor
        // position to .NET. DrawingInteractionManager decides whether to surface a
        // drawing-specific context menu (hit-tests anchors) or no-op.
        el.addEventListener('contextmenu', function (e) {
            e.preventDefault();
            const rect = el.getBoundingClientRect();
            dotnetHelper.invokeMethodAsync('OnContextMenu',
                e.clientX - rect.left, e.clientY - rect.top, rect.width, rect.height);
        });

        // Scroll-wheel zoom. Passive:false so we can preventDefault — otherwise the
        // browser scrolls the page under the chart instead of zooming. Direction is
        // +1 for wheel-up (zoom in) and -1 for wheel-down (zoom out); anchor fraction
        // is the cursor's X position within the chart rect, so the bar under the
        // cursor stays fixed as the viewport expands or contracts.
        //
        // Shift+wheel — or a horizontal trackpad swipe — pans through time instead of
        // zooming (motor-friendly: no click-hold needed). Scroll down/right = newer
        // bars, up/left = older bars.
        el.addEventListener('wheel', function (e) {
            e.preventDefault();
            const rect = el.getBoundingClientRect();
            const horizontal = Math.abs(e.deltaX) > Math.abs(e.deltaY);
            if (e.shiftKey || horizontal) {
                const delta = horizontal ? e.deltaX : e.deltaY;
                if (delta !== 0) {
                    dotnetHelper.invokeMethodAsync('OnWheelPan', delta > 0 ? 1 : -1);
                }
                return;
            }
            const anchorFraction = (e.clientX - rect.left) / Math.max(1, rect.width);
            const direction = e.deltaY < 0 ? 1 : -1;
            dotnetHelper.invokeMethodAsync('OnWheel', direction, anchorFraction);
        }, { passive: false });

        // Double-click on the chart jumps to the live edge (latest bar) — the mouse
        // twin of the keyboard's jump-to-live command.
        el.addEventListener('dblclick', function () {
            dotnetHelper.invokeMethodAsync('OnDoubleClick');
        });

        // Cursor leaving the chart hides the hover crosshair. Forwarded as a distinct
        // mouse type; the drawing pipeline ignores it.
        el.addEventListener('mouseleave', function () {
            const rect = el.getBoundingClientRect();
            dotnetHelper.invokeMethodAsync('OnMouseEvent', -1, -1, 'MouseLeave', rect.width, rect.height);
        });

        // ── Touch gestures (Phase C) ────────────────────────────────────────
        // Synthesizes the SAME .NET bridge calls the mouse produces, so every
        // touch gesture reuses the tested mouse pipelines:
        //   tap        → MouseDown+MouseUp at rest → select bar (spoken + sonified)
        //   drag       → MouseDown / MouseMove… / MouseUp → viewport pan
        //   pinch      → OnWheel(direction, centroidFraction) → anchored zoom
        //   double-tap → OnDoubleClick → jump to live edge
        //   long-press → OnContextMenu → chart / drawing context menu
        // preventDefault on touchstart suppresses the browser's synthetic mouse
        // events so nothing double-fires (touch-action: none on the element is
        // the belt to this brace). When VoiceOver/TalkBack run, the screen
        // reader owns the touchscreen and these rarely fire — the accessible
        // paths are the bar-navigator slider and the touch toolbar buttons.
        const touchState = {
            mode: 'idle',      // idle | pending | drag | pinch | consumed
            startX: 0, startY: 0,
            lastTapAt: 0, lastTapX: 0, lastTapY: 0,
            pressTimer: 0,
            pinchDist: 0,
        };
        const TAP_SLOP_PX = 10;        // finger jitter allowance before a tap becomes a drag
        const LONG_PRESS_MS = 550;
        const DOUBLE_TAP_MS = 300;
        const DOUBLE_TAP_SLOP_PX = 40;
        const PINCH_STEP = 1.08;       // one zoom notch per 8% spread change

        function clearPressTimer() {
            if (touchState.pressTimer) { clearTimeout(touchState.pressTimer); touchState.pressTimer = 0; }
        }

        el.addEventListener('touchstart', function (e) {
            e.preventDefault();
            if (e.touches.length === 1 && window.accessibleTrader._touchExploreMode) {
                // Explore: report the bar under the finger from the very first
                // contact; no long-press timer (context menu is reachable by
                // turning Explore off), no drag threshold.
                const rect = el.getBoundingClientRect();
                touchState.mode = 'explore';
                touchState.startX = e.touches[0].clientX - rect.left;
                touchState.startY = e.touches[0].clientY - rect.top;
                clearPressTimer();
                dotnetHelper.invokeMethodAsync('OnMouseEvent',
                    touchState.startX, touchState.startY, 'TouchExplore', rect.width, rect.height);
                return;
            }
            if (e.touches.length === 1) {
                const rect = el.getBoundingClientRect();
                touchState.mode = 'pending';
                touchState.startX = e.touches[0].clientX - rect.left;
                touchState.startY = e.touches[0].clientY - rect.top;
                clearPressTimer();
                touchState.pressTimer = setTimeout(function () {
                    if (touchState.mode !== 'pending') return;
                    touchState.mode = 'consumed';   // long-press: eat the touchend
                    const r = el.getBoundingClientRect();
                    dotnetHelper.invokeMethodAsync('OnContextMenu',
                        touchState.startX, touchState.startY, r.width, r.height);
                }, LONG_PRESS_MS);
            } else if (e.touches.length === 2) {
                clearPressTimer();
                if (touchState.mode === 'drag') {
                    // Second finger lands mid-drag: close the synthetic mouse sequence.
                    const r = el.getBoundingClientRect();
                    dotnetHelper.invokeMethodAsync('OnMouseEvent',
                        e.touches[0].clientX - r.left, e.touches[0].clientY - r.top,
                        'MouseUp', r.width, r.height);
                }
                touchState.mode = 'pinch';
                touchState.pinchDist = Math.hypot(
                    e.touches[0].clientX - e.touches[1].clientX,
                    e.touches[0].clientY - e.touches[1].clientY);
            }
        }, { passive: false });

        // Drag moves are RAF-throttled like the mouse path so the interop
        // bridge sees at most one dispatch per paint tick.
        let touchMoveRaf = 0;
        let pendingTouch = null;
        el.addEventListener('touchmove', function (e) {
            e.preventDefault();
            const rect = el.getBoundingClientRect();

            if (touchState.mode === 'pinch' && e.touches.length >= 2) {
                const d = Math.hypot(
                    e.touches[0].clientX - e.touches[1].clientX,
                    e.touches[0].clientY - e.touches[1].clientY);
                if (d <= 0 || touchState.pinchDist <= 0) return;
                const centroidFraction =
                    ((e.touches[0].clientX + e.touches[1].clientX) / 2 - rect.left)
                    / Math.max(1, rect.width);
                while (d / touchState.pinchDist >= PINCH_STEP) {           // spread → zoom in
                    dotnetHelper.invokeMethodAsync('OnWheel', 1, centroidFraction);
                    touchState.pinchDist *= PINCH_STEP;
                }
                while (touchState.pinchDist / d >= PINCH_STEP) {           // squeeze → zoom out
                    dotnetHelper.invokeMethodAsync('OnWheel', -1, centroidFraction);
                    touchState.pinchDist /= PINCH_STEP;
                }
                return;
            }

            if (e.touches.length !== 1) return;
            const x = e.touches[0].clientX - rect.left;
            const y = e.touches[0].clientY - rect.top;

            if (touchState.mode === 'explore') {
                pendingTouch = { x: x, y: y, w: rect.width, h: rect.height };
                if (touchMoveRaf) return;
                touchMoveRaf = requestAnimationFrame(function () {
                    touchMoveRaf = 0;
                    if (!pendingTouch || touchState.mode !== 'explore') return;
                    dotnetHelper.invokeMethodAsync('OnMouseEvent',
                        pendingTouch.x, pendingTouch.y, 'TouchExplore', pendingTouch.w, pendingTouch.h);
                });
                return;
            }

            if (touchState.mode === 'pending') {
                if (Math.hypot(x - touchState.startX, y - touchState.startY) < TAP_SLOP_PX) return;
                clearPressTimer();
                touchState.mode = 'drag';
                dotnetHelper.invokeMethodAsync('OnMouseEvent',
                    touchState.startX, touchState.startY, 'MouseDown', rect.width, rect.height);
            }
            if (touchState.mode === 'drag') {
                pendingTouch = { x: x, y: y, w: rect.width, h: rect.height };
                if (touchMoveRaf) return;
                touchMoveRaf = requestAnimationFrame(function () {
                    touchMoveRaf = 0;
                    if (!pendingTouch || touchState.mode !== 'drag') return;
                    dotnetHelper.invokeMethodAsync('OnMouseEvent',
                        pendingTouch.x, pendingTouch.y, 'MouseMove', pendingTouch.w, pendingTouch.h);
                });
            }
        }, { passive: false });

        el.addEventListener('touchend', function (e) {
            e.preventDefault();
            clearPressTimer();
            if (e.touches.length > 0) {
                // Fingers remain (pinch → one finger left). Wait for a clean lift.
                touchState.mode = 'consumed';
                return;
            }
            const rect = el.getBoundingClientRect();
            if (touchState.mode === 'explore') {
                dotnetHelper.invokeMethodAsync('OnMouseEvent', -1, -1, 'MouseLeave', rect.width, rect.height);
            } else if (touchState.mode === 'drag') {
                const t = e.changedTouches[0];
                dotnetHelper.invokeMethodAsync('OnMouseEvent',
                    t.clientX - rect.left, t.clientY - rect.top, 'MouseUp', rect.width, rect.height);
            } else if (touchState.mode === 'pending') {
                const now = Date.now();
                const isDoubleTap = (now - touchState.lastTapAt) < DOUBLE_TAP_MS
                    && Math.hypot(touchState.startX - touchState.lastTapX,
                                  touchState.startY - touchState.lastTapY) < DOUBLE_TAP_SLOP_PX;
                if (isDoubleTap) {
                    touchState.lastTapAt = 0;
                    dotnetHelper.invokeMethodAsync('OnDoubleClick');
                } else {
                    touchState.lastTapAt = now;
                    touchState.lastTapX = touchState.startX;
                    touchState.lastTapY = touchState.startY;
                    // Tap = click-select: full down+up at the rest position.
                    dotnetHelper.invokeMethodAsync('OnMouseEvent',
                        touchState.startX, touchState.startY, 'MouseDown', rect.width, rect.height);
                    dotnetHelper.invokeMethodAsync('OnMouseEvent',
                        touchState.startX, touchState.startY, 'MouseUp', rect.width, rect.height);
                }
            }
            touchState.mode = 'idle';
        }, { passive: false });

        el.addEventListener('touchcancel', function () {
            clearPressTimer();
            if (touchState.mode === 'drag') {
                const rect = el.getBoundingClientRect();
                dotnetHelper.invokeMethodAsync('OnMouseEvent',
                    touchState.startX, touchState.startY, 'MouseUp', rect.width, rect.height);
            }
            touchState.mode = 'idle';
        });
    },

    /**
     * Applies the user's UI text scale (Settings → Appearance → Text size).
     * rem-based typography follows the root font-size; layout px dimensions stay.
     */
    setUiScale: function (percent) {
        const clamped = Math.min(250, Math.max(50, percent | 0));
        document.documentElement.style.fontSize = (clamped * 16 / 100) + 'px';
    },

    /**
     * Reports the chart surface's CSS size and devicePixelRatio so the server
     * can render the chart PNG at native resolution (HiDPI sharpness).
     */
    getChartMetrics: function (elementId) {
        const el = document.getElementById(elementId);
        if (!el) return [0, 0, 1];
        const rect = el.getBoundingClientRect();
        return [rect.width, rect.height, window.devicePixelRatio || 1];
    },

    /**
     * Moves keyboard focus to an element by its id.
     * Called by AddIndicatorModal when it opens so screen readers discover the dialog.
     */
    // Touch Explore mode (Phase C explore-by-touch): when on, a single finger
    // sliding over the chart SPEAKS AND SONIFIES the bars under it instead of
    // panning the viewport. Toggled by the TouchNavBar Explore button; state
    // lives here because the gesture engine below consults it per-event.
    _touchExploreMode: false,
    setTouchExploreMode: function (on) {
        window.accessibleTrader._touchExploreMode = !!on;
    },

    /**
     * Move keyboard focus to an element by id, waiting for it to exist.
     *
     * Callers are almost always a modal that has just set its visibility flag and
     * yielded once. A single yield does NOT guarantee Blazor's render batch has
     * reached the DOM: the batch is applied asynchronously, and how long that takes
     * scales with how much markup the batch contains. Small dialogs won that race and
     * the largest one — the trading dashboard, with its tab strip, order book and
     * three data tables — lost it, so Alt+T opened a dialog that never took focus
     * while every other modal worked. A lookup that returns null then silently does
     * nothing is indistinguishable from a modal with no focus handling at all.
     *
     * So retry across animation frames rather than giving up on the first miss. The
     * budget is ~10 frames (about 160 ms at 60 Hz) — long enough for any batch this
     * app produces, short enough that a genuinely absent id costs nothing visible.
     *
     * Retrying introduces one hazard of its own: focus landing late, after the user
     * has already moved it. Guarded by remembering what was focused when the call
     * started and abandoning the retry if anything else changes it, so a late frame
     * can never yank the user back out of wherever they went.
     */
    focusElement: function (elementId) {
        const startedOn = document.activeElement;
        let framesLeft = 10;

        const attempt = function () {
            const el = document.getElementById(elementId);
            if (el) { el.focus(); return; }
            if (--framesLeft <= 0) return;
            // Something else claimed focus while we were waiting — that is the user or
            // a later render, and either outranks this request.
            if (document.activeElement !== startedOn) return;
            requestAnimationFrame(attempt);
        };

        attempt();
    },

    /**
     * True when the device actually has a touch input. Used to gate the touch
     * navigation toolbar out of the DOM entirely on desktop — CSS media queries
     * alone left it in the accessibility tree for screen-reader users on some
     * desktop browsers.
     */
    isTouchCapable: function () {
        // "Touch device" = coarse PRIMARY pointer AND no fine pointer anywhere.
        // Desktop Linux input stacks (incl. accessibility setups) can misreport
        // the primary pointer as coarse, but a desktop always HAS a fine pointer
        // (the mouse) - any-pointer:fine is the reliable discriminator. Phones
        // have no fine pointer at all. Tablets with a paired mouse fall back to
        // hidden; the Settings "Always show" override covers them.
        if (!window.matchMedia) return false;
        return window.matchMedia('(pointer: coarse)').matches
            && !window.matchMedia('(any-pointer: fine)').matches;
    },

    downloadCsv: function(filename, content) {
        const blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    /**
     * Downloads binary content supplied as a Base64 string.
     * Used for .atpkg zip export and any future binary file downloads.
     */
    downloadBlob: function(filename, base64Data, mimeType) {
        const bytes = atob(base64Data);
        const buffer = new ArrayBuffer(bytes.length);
        const view = new Uint8Array(buffer);
        for (let i = 0; i < bytes.length; i++) view[i] = bytes.charCodeAt(i);
        const blob = new Blob([buffer], { type: mimeType || 'application/octet-stream' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    /**
     * Opens a native file picker and resolves with the selected file's text content.
     * Used for importing settings profiles (.json).
     */
    readFileAsText: function(accept) {
        return new Promise(function(resolve, reject) {
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = accept || '*';
            input.onchange = function(e) {
                const file = e.target.files[0];
                if (!file) { reject('No file selected'); return; }
                const reader = new FileReader();
                reader.onload = function(ev) { resolve(ev.target.result); };
                reader.onerror = function() { reject('Failed to read file'); };
                reader.readAsText(file);
            };
            input.click();
        });
    },

    /**
     * Opens a native file picker and resolves with the selected file's Base64 content.
     * Used for importing binary .atpkg zip packages.
     */
    readFileAsBase64: function(accept) {
        return new Promise(function(resolve, reject) {
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = accept || '*';
            input.onchange = function(e) {
                const file = e.target.files[0];
                if (!file) { reject('No file selected'); return; }
                const reader = new FileReader();
                reader.onload = function(ev) {
                    // Strip the data URL prefix to get raw base64
                    const base64 = ev.target.result.split(',')[1];
                    resolve(base64);
                };
                reader.onerror = function() { reject('Failed to read file'); };
                reader.readAsDataURL(file);
            };
            input.click();
        });
    },

    /**
     * Registers a one-shot keydown capture at the top of the event capture chain.
     * Used by the keyboard rebinding UI in Settings → Keyboard tab.
     * Fires OnKeyCaptured on the .NET helper with the key details, then removes itself.
     */
    /**
     * Applies a theme's CSS custom properties to the document root.
     *
     * The chart canvas is painted by Skia, but every toolbar, dialog and label around it is
     * HTML — so without this a theme change repaints the chart and leaves the frame around it
     * on the old palette. `vars` is a plain { "--name": "value" } object built by
     * ThemeCssBridge; setting them on documentElement means every rule that reads
     * var(--name) updates in one go, with no per-component plumbing.
     *
     * Failure here must be survivable: app.css keeps a full :root fallback block, so if this
     * never runs the application renders in the default palette rather than unstyled.
     */
    applyThemeVariables: function(vars) {
        if (!vars) return;
        var root = document.documentElement;
        for (var name in vars) {
            if (Object.prototype.hasOwnProperty.call(vars, name)) {
                root.style.setProperty(name, vars[name]);
            }
        }
    },

    captureNextKey: function(dotNetHelper) {
        function handler(e) {
            if (e.key === 'Shift' || e.key === 'Control' || e.key === 'Alt' || e.key === 'Meta') return;
            e.preventDefault();
            e.stopImmediatePropagation();
            document.removeEventListener('keydown', handler, true);
            dotNetHelper.invokeMethodAsync('OnKeyCaptured', e.key, e.shiftKey, e.ctrlKey, e.altKey);
        }
        document.addEventListener('keydown', handler, true);
    }
};
