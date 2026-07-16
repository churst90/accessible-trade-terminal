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
// Expand/collapse detection:
//   - aria-expanded="true" | "false" on the treeitem itself (ConditionTreeEditor).
//   - <details open> ancestor on/inside the treeitem (ObjectTreeModal uses
//     <details><summary role="treeitem">…</summary>…</details>).
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
            // Hidden when any ancestor treeitem has aria-expanded="false" or an
            // ancestor <details> is not open.
            let p = el.parentElement;
            while (p && p !== tree) {
                if (p.tagName === 'DETAILS' && !p.open) return false;
                if (p.getAttribute && p.getAttribute('aria-expanded') === 'false') {
                    // The expanded-false ancestor itself stays visible; only its
                    // descendants hide. If this element equals the expanded-false
                    // ancestor, keep it.
                    if (p !== el) return false;
                }
                p = p.parentElement;
            }
            return el.offsetParent !== null;
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
        const exp = treeitem.getAttribute('aria-expanded');
        if (exp === 'true') return true;
        if (exp === 'false') return false;
        const d = findOwnedDetails(treeitem);
        if (d) return d.open;
        return false;
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
        let p = treeitem.parentElement;
        while (p && p !== tree) {
            if (p.getAttribute && p.getAttribute('role') === 'treeitem') return p;
            p = p.parentElement;
        }
        return null;
    }

    function findFirstChild(treeitem) {
        // Only descendants strictly inside this treeitem count.
        const items = treeitem.querySelectorAll('[role="treeitem"]');
        return items.length > 0 ? items[0] : null;
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
