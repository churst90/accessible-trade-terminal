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

window.accessibleTrader = {

    // Exposed for tests; the keydown trap calls the module-scope function directly.
    _isSpaceActivationTarget: isSpaceActivationTarget,

    // Count of currently-open modals. Set by `setModalOpen(true|false)` from ModalBase
    // on every open/close. When > 0, the Tab trap is armed; the modal element itself
    // is discovered at trap time via `role="dialog"` lookup so we don't require each
    // modal to thread a selector string through.
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

    /**
     * Called from .NET (CommandDispatcher / ChartFocusService) when the focus ring
     * enters or leaves the chart region. When false, single-letter chart commands
     * are skipped in the keydown trap; modifier chords and function keys still fire.
     */
    setChartFocused: function (focused) {
        this._chartFocused = !!focused;
    },

    /**
     * Called from ModalBase whenever any modal opens (isOpen=true) or closes
     * (isOpen=false). Arms/disarms the Tab trap based on whether any modal is still
     * visible. Uses a counter rather than a boolean so nested/stacked modals don't
     * prematurely disarm the trap when one of several closes.
     */
    setModalOpen: function (isOpen) {
        if (isOpen) this._openModalCount++;
        else        this._openModalCount = Math.max(0, this._openModalCount - 1);
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
        window.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab') return;
            if (self._openModalCount <= 0) return;
            const dialogs = Array.prototype.slice.call(document.querySelectorAll('[role="dialog"]'))
                .filter(el => el.offsetParent !== null);
            if (dialogs.length === 0) return;
            const modal = dialogs[dialogs.length - 1];

            const focusableSelector =
                'a[href], area[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), ' +
                'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
            const focusables = Array.prototype.slice.call(modal.querySelectorAll(focusableSelector))
                .filter(el => !el.hasAttribute('disabled') && el.offsetParent !== null);
            if (focusables.length === 0) return;

            const first = focusables[0];
            const last  = focusables[focusables.length - 1];
            const active = document.activeElement;

            if (e.shiftKey && active === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && active === last) {
                e.preventDefault();
                first.focus();
            } else if (active && !modal.contains(active)) {
                // Focus somehow escaped the modal (e.g. clicked outside). Rehome it.
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

            const isModified = e.ctrlKey || e.altKey;
            const isShifted = e.shiftKey;
            const isTrapped = trappedKeys.includes(e.key);

            if (!isTrapped && !isModified) return;

            // Allow normal keyboard use inside form controls unless a modifier is held.
            // Escape is the exception: it's never a text-input character, and it must always reach
            // the dispatcher so it can close the open modal — otherwise a form-heavy modal (e.g. the
            // Sound Designer) can't be Escaped out of while focus sits on a <select>/<input>/<textarea>.
            const tag = e.target.tagName;
            const isFormControl = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
            const isEditable = e.target.isContentEditable === true;
            if ((isFormControl || isEditable) && !isModified && e.key !== 'Escape') return;

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

    focusElement: function (elementId) {
        const el = document.getElementById(elementId);
        if (el) {
            el.focus();
        }
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
