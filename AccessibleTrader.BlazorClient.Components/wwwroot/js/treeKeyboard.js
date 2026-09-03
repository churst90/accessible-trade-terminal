// Auto-wires ARIA-tree keyboard navigation on any element with role="tree".
//
// Standard WAI-ARIA tree pattern:
//   ArrowDown : focus next visible treeitem
//   ArrowUp   : focus previous visible treeitem
//   ArrowRight: if focused on a collapsed group → expand. If already expanded or
//               the focused item is a leaf → move focus to first child (if any).
//   ArrowLeft : if focused on an expanded group → collapse. Otherwise → move
//               focus to the parent treeitem.
//   Home      : focus first visible treeitem.
//   End       : focus last visible treeitem.
//   Enter     : activate the treeitem's primary action (click the label / summary).
//   Space     : toggle expand/collapse on a group; activate on a leaf.
//
// The handler is a single window-level delegator. Each treeitem needs
// `tabindex="-1"` (or "0" for the initially-focused one); the component is
// responsible for setting one visible treeitem to tabindex="0" so keyboard
// users can enter the tree via Tab.
//
// Expand/collapse detection, IN THIS ORDER:
//   - <details open> owned by the treeitem (ObjectTreeModal uses
//     <details><summary role="treeitem">…</summary>…</details> at the pane level and
//     <div role="treeitem"><details>…</details></div> at the series level).
//   - aria-expanded="true" | "false" on the treeitem itself (ConditionTreeEditor, which
//     has no <details> at all).
// The order is load-bearing — see isExpanded.
//
// Expand/collapse dispatch:
//   1. Fire a synthetic click on any descendant button with data-tree-toggle="true".
//   2. Fall back to toggling the nearest <details> element's `open` attribute.
//   3. Fall back to firing click on the treeitem itself (many components treat
//      that as select+toggle in one).

(function () {
    if (window.accessibleTrader === undefined) window.accessibleTrader = {};
    if (window.accessibleTrader._treeKeyboardInstalled) return;
    window.accessibleTrader._treeKeyboardInstalled = true;

    function findTree(el) {
        while (el && el !== document.body) {
            if (el.getAttribute && el.getAttribute('role') === 'tree') return el;
            el = el.parentElement;
        }
        return null;
    }

    function findTreeitem(el, tree) {
        while (el && el !== tree) {
            if (el.getAttribute && el.getAttribute('role') === 'treeitem') return el;
            el = el.parentElement;
        }
        return null;
    }

    function visibleTreeitems(tree) {
        const all = Array.prototype.slice.call(tree.querySelectorAll('[role="treeitem"]'));
        return all.filter(function (el) {
            // Two checks, and each one is here for a reason the other cannot cover.
            //
            // The ancestor <details> check STAYS. The argument for deleting it was that a
            // closed <details> puts its children in display:none so they already fail the
            // rects test — true of the old UA stylesheet, but Chromium hides closed
            // content through `::details-content { content-visibility: hidden }` now, and
            // "skipped contents" is not the same question as "generates no box". Nothing
            // in this repo measures which is true in the WebView2 build: treeKeyboard.js
            // has no JS tests, and the bUnit suite has no layout at all. An unmeasured
            // claim is not a reason to delete a cheap, certain check — if it is wrong,
            // every series under a collapsed pane rejoins the arrow-key walk while the
            // user cannot see them.
            //
            // The aria-expanded clause is GONE, and that one was measured. Both consumers
            // make it unreachable — ObjectTreeModal's collapsed children are inside a
            // closed <details>, and ConditionTreeEditor omits collapsed children from the
            // DOM entirely (ConditionTreeEditor.razor:383) — and once ObjectTreeModal
            // started rendering aria-expanded it became actively dangerous: `toggle` is
            // queued, so a value stale by one task turn would have dropped a VISIBLE
            // series' components out of the walk. A visibility test that can disagree with
            // the layout is worse than not having it.
            //
            // The walk starts ABOVE the <details> a <summary> treeitem belongs to. A closed
            // <details> hides its content, not its summary — the summary is the one thing
            // that stays visible, and it is the control that re-opens it. Starting the walk
            // at the summary's own parent removed the header of every collapsed pane from the
            // walk: with one pane, ArrowLeft on the header collapsed it and every arrow key
            // was then dead (an empty items list returns early), so the pane could be
            // collapsed and never re-opened by arrows; with two, the collapsed pane's header
            // could not be arrowed back to, and Home skipped it. Found by tree-tests.mjs on
            // the day the file got tests.
            let p = el.parentElement;
            if (el.tagName === 'SUMMARY' && p && p.tagName === 'DETAILS') p = p.parentElement;
            while (p && p !== tree) {
                if (p.tagName === 'DETAILS' && !p.open) return false;
                p = p.parentElement;
            }

            // getClientRects(), not offsetParent: offsetParent is null for a
            // position:fixed element and for anything inside one in some engines, and
            // this tree lives inside a fixed modal overlay. That is the same mistake
            // keyboard.js's focusable scan made until 2026-09-02 — offsetParent answers
            // "what do I lay out against", not "am I visible".
            return el.getClientRects().length > 0;
        });
    }

    // Finds a <details> element that belongs to this treeitem — either the
    // treeitem is a <summary> inside it, or the treeitem has a <details> as a
    // direct child (used by ObjectTreeModal's series-level treeitem-wrapping-a-details).
    function findOwnedDetails(treeitem) {
        if (treeitem.tagName === 'SUMMARY' && treeitem.parentElement
            && treeitem.parentElement.tagName === 'DETAILS') return treeitem.parentElement;
        for (let i = 0; i < treeitem.children.length; i++) {
            if (treeitem.children[i].tagName === 'DETAILS') return treeitem.children[i];
        }
        return null;
    }

    function isGroup(treeitem) {
        const exp = treeitem.getAttribute('aria-expanded');
        if (exp === 'true' || exp === 'false') return true;
        if (findOwnedDetails(treeitem)) return true;
        return !!treeitem.querySelector('[role="group"]');
    }

    function isExpanded(treeitem) {
        // The <details> is the SOURCE OF TRUTH; aria-expanded is only its projection.
        //
        // This order matters and the reverse of it is a keyboard trap. ObjectTreeModal
        // renders aria-expanded from C# and re-renders it when the browser's `toggle`
        // event arrives — and `toggle` is queued, not synchronous, so for at least one
        // task turn after ArrowLeft the attribute still says "true" while the details is
        // already closed. Reading the attribute first, ArrowRight would then take the
        // "already expanded, move to first child" branch, find no child, and do nothing:
        // the pane could be collapsed and never re-opened, and ArrowLeft would be the key
        // that expands it. Read from the DOM and a stale attribute can only mislead the
        // screen reader for one tick; read from the attribute and it inverts the keys.
        const d = findOwnedDetails(treeitem);
        if (d) return d.open;
        const exp = treeitem.getAttribute('aria-expanded');
        return exp === 'true';
    }

    function toggleExpand(treeitem) {
        // Prefer an explicit toggle button.
        const btn = treeitem.querySelector('[data-tree-toggle="true"]');
        if (btn) { btn.click(); return; }
        // Toggle an owned <details> (summary-as-treeitem or treeitem-wrapping-details).
        const d = findOwnedDetails(treeitem);
        if (d) { d.open = !d.open; return; }
        // Last resort: click the treeitem itself.
        treeitem.click();
    }

    function focusTreeitem(treeitem) {
        if (!treeitem) return;
        // WAI-ARIA roving tabindex: set tabindex 0 on the focused item, -1 on
        // the others inside the same tree.
        const tree = findTree(treeitem);
        if (tree) {
            const all = tree.querySelectorAll('[role="treeitem"]');
            for (let i = 0; i < all.length; i++) all[i].setAttribute('tabindex', '-1');
        }
        treeitem.setAttribute('tabindex', '0');
        treeitem.focus();
    }

    function findParent(treeitem, tree) {
        // An ancestor treeitem, or — for ObjectTreeModal's pane level, where the pane's
        // treeitem is the <summary> and the series sit in a SIBLING role="group" rather
        // than inside it — the summary of an enclosing <details>. Without the second rule
        // ArrowLeft on a collapsed series did nothing: there was no ancestor treeitem to
        // move to, and the pane header it should have gone to is not an ancestor.
        let p = treeitem.parentElement;
        while (p && p !== tree) {
            if (p.getAttribute && p.getAttribute('role') === 'treeitem') return p;
            if (p.tagName === 'DETAILS') {
                for (let i = 0; i < p.children.length; i++) {
                    const c = p.children[i];
                    if (c.tagName === 'SUMMARY' && c !== treeitem
                        && c.getAttribute && c.getAttribute('role') === 'treeitem') return c;
                }
            }
            p = p.parentElement;
        }
        return null;
    }

    function findFirstChild(treeitem) {
        // Descendants strictly inside this treeitem — or, for a <summary> treeitem, inside
        // the <details> it heads, whose role="group" is the summary's SIBLING. Searching the
        // summary alone found nothing, so ArrowRight on an open pane header never entered
        // the pane (the third place the sibling-group shape bit; see findParent).
        let scope = treeitem;
        if (treeitem.tagName === 'SUMMARY' && treeitem.parentElement
            && treeitem.parentElement.tagName === 'DETAILS') scope = treeitem.parentElement;
        const items = scope.querySelectorAll('[role="treeitem"]');
        for (let i = 0; i < items.length; i++) if (items[i] !== treeitem) return items[i];
        return null;
    }

    function activatePrimary(treeitem) {
        // Simulate Enter/Space on the label. Preference order:
        //   1. A child <summary> (for <details> trees).
        //   2. A child button marked data-tree-activate="true".
        //   3. The treeitem's first button child.
        //   4. Click the treeitem itself.
        if (treeitem.tagName === 'SUMMARY') { treeitem.click(); return; }
        const summary = treeitem.querySelector('summary');
        if (summary) { summary.click(); return; }
        const primary = treeitem.querySelector('[data-tree-activate="true"]');
        if (primary) { primary.click(); return; }
        const firstBtn = treeitem.querySelector('button');
        if (firstBtn) { firstBtn.click(); return; }
        treeitem.click();
    }

    window.addEventListener('keydown', function (e) {
        // A MODIFIED arrow is somebody else's key. Shift+Arrow nudges the focused drawing's
        // anchor (since 2026-09-03 the dispatcher allows that under the Object Tree, because
        // the tree is where a drawing is focused); Ctrl/Alt/Meta+Arrow are OS and screen-reader
        // chords. The tree model is plain arrows only, so a shifted press must neither move the
        // tree focus nor be preventDefault'd here — the chart's own handler owns it.
        if (e.shiftKey || e.ctrlKey || e.altKey || e.metaKey) return;
        const tree = findTree(e.target);
        if (!tree) return;
        const current = findTreeitem(e.target, tree);
        const items = visibleTreeitems(tree);
        if (items.length === 0) return;
        const idx = current ? items.indexOf(current) : -1;

        switch (e.key) {
            case 'ArrowDown': {
                e.preventDefault();
                const next = idx >= 0 ? items[Math.min(idx + 1, items.length - 1)] : items[0];
                focusTreeitem(next);
                return;
            }
            case 'ArrowUp': {
                e.preventDefault();
                const prev = idx >= 0 ? items[Math.max(idx - 1, 0)] : items[items.length - 1];
                focusTreeitem(prev);
                return;
            }
            case 'Home': {
                e.preventDefault();
                focusTreeitem(items[0]);
                return;
            }
            case 'End': {
                e.preventDefault();
                focusTreeitem(items[items.length - 1]);
                return;
            }
            case 'ArrowRight': {
                if (!current) return;
                e.preventDefault();
                if (isGroup(current) && !isExpanded(current)) {
                    toggleExpand(current);
                    return;
                }
                // Already expanded OR is a leaf → move to first child.
                const child = findFirstChild(current);
                if (child) focusTreeitem(child);
                return;
            }
            case 'ArrowLeft': {
                if (!current) return;
                e.preventDefault();
                if (isGroup(current) && isExpanded(current)) {
                    toggleExpand(current);
                    return;
                }
                const parent = findParent(current, tree);
                if (parent) focusTreeitem(parent);
                return;
            }
            case 'Enter': {
                if (!current) return;
                e.preventDefault();
                activatePrimary(current);
                return;
            }
            case ' ': {
                if (!current) return;
                e.preventDefault();
                if (isGroup(current)) toggleExpand(current);
                else activatePrimary(current);
                return;
            }
        }
    }, false);
})();
