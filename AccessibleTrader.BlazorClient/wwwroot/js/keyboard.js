window.accessibleTrader = {

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
        // Capture phase: run before any bubble-phase handler, and before WebView2/browser
        // tries to consume reserved chords like Ctrl+Shift+T, Ctrl+Shift+N, Ctrl+Shift+P.
        // stopImmediatePropagation is used on modifier chords so no downstream handler fires.
        window.addEventListener('keydown', function (e) {
            const trappedKeys = [
                'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown',
                'Home', 'End', 'PageUp', 'PageDown',
                'Delete', 'Escape',
                'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7',
                'F9', 'F10', 'F11', 'F12',
                ' ', '[', ']', '{', '}', '\\', '-', '=', '_', '+',
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
            const tag = e.target.tagName;
            const isFormControl = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
            if (isFormControl && !isModified) return;

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

        el.addEventListener('mousedown', function (e) {
            const rect = el.getBoundingClientRect();
            dotnetHelper.invokeMethodAsync('OnMouseEvent', 
                e.clientX - rect.left, e.clientY - rect.top, 'MouseDown', rect.width, rect.height);
        });

        el.addEventListener('mousemove', function (e) {
            if (e.buttons > 0) {
                const rect = el.getBoundingClientRect();
                dotnetHelper.invokeMethodAsync('OnMouseEvent', 
                    e.clientX - rect.left, e.clientY - rect.top, 'MouseMove', rect.width, rect.height);
            }
        });

        el.addEventListener('mouseup', function (e) {
            const rect = el.getBoundingClientRect();
            dotnetHelper.invokeMethodAsync('OnMouseEvent', 
                e.clientX - rect.left, e.clientY - rect.top, 'MouseUp', rect.width, rect.height);
        });
    },

    /**
     * Moves keyboard focus to an element by its id.
     * Called by AddIndicatorModal when it opens so screen readers discover the dialog.
     */
    focusElement: function (elementId) {
        const el = document.getElementById(elementId);
        if (el) {
            el.focus();
        }
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
