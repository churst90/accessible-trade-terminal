# Modal specialist review — keyboard.js, commits bc52e652 + 553960f7

Review only; nothing in the tree was edited. Reproduction script: `trap-scenarios.mjs` in this
directory (loads the real keyboard.js in a vm sandbox exactly as tools/jstests/keyboard-tests.mjs
does; output in `trap-scenarios.out`). Line numbers are the CURRENT keyboard.js at 553960f7.

Escape handling and focus return: NEITHER commit touched them (checked both diffs) — out of scope.

## VERDICT: NO-SHIP as "3.1 closed"

The trap rewrite is a real improvement (single-dialog Shift+Tab from the heading is fixed, the
`<summary>` pinning is fixed), but the alertdialog fix the first commit is named for does not work
in Chromium, and the second commit's unconditional `summary` clause opened a new Shift+Tab escape in
ObjectTreeModal on the hosted/demo builds. Three one-line fixes below.

---

## CONFIRMED defects

### Critical: The Toolbar alertdialog is still invisible to the trap — `position:fixed` makes `offsetParent` null
- **WCAG:** 2.4.3 Focus Order (A); 4.1.2 (aria-modal="true" overclaims)
- **Confidence:** high (code trace + CSSOM-View spec; node repro Q1i)
- **Impact:** From the destructive "Switching to analytics" prompt, Tab/Shift+Tab walk out onto the
  toolbar behind it while `aria-modal="true"` tells the screen reader not to describe it — exactly
  the defect bc52e652 says it closed.
- **Location:** `AccessibleTrader.BlazorClient.Components/Toolbar.razor:430-432` (`role="alertdialog"`
  with inline `style="position:fixed; …"` ON THE ROLE ELEMENT) vs
  `AccessibleTrader.BlazorClient.Components/wwwroot/js/keyboard.js:137-140`
  (`.filter(el => el.offsetParent !== null)` → `if (dialogs.length === 0) return;`).

CSSOM-View `offsetParent` returns null when the element's computed `position` is `fixed`
(Chromium, WebView2, Safari; Firefox returns `<body>`). Every ModalBase dialog survives the filter
only because `position:fixed` sits on the PARENT `.modal-overlay` (app.css:402-403) and
`role="dialog"` is on the child `.modal-content`. The alertdialog is the one dialog-family element
that is itself fixed, so the widened selector finds it and the very next line discards it.

Why every gate is green: `ChromeAccessibilityScanTests.TheJsTabTrapCoversTheWholeAriaDialogFamily`
(:99-127) is a presence check on the selector STRING; the jstest "the trap sees role=alertdialog"
gives its fake node `offsetParent: {}`; and the alertdialog is not a `ModalRoute` at all
(`AccessibleTrader.BrowserTests/ModalRoute.cs`). Worse, the browser probe in
`ModalBrowserContractTests.cs:199-204` uses the SAME filter and returns `inside = true` when
`dialogs.length === 0`, so routing the alertdialog through it would pass vacuously.

Fix (one line, either side): filter on `el.getClientRects().length > 0` instead of `offsetParent`
— or move `position:fixed` off the role element onto a wrapper. Fix the browser probe with it.

### Serious: ObjectTreeModal — Shift+Tab escapes after one ArrowDown in the tree (REGRESSION from 553960f7)
- **WCAG:** 2.4.3 Focus Order (A)
- **Confidence:** high (code trace + node repro Q1h)
- **Impact:** Alt+O, Tab, ArrowDown, Shift+Tab: focus leaves the dialog onto the chart/toolbar with
  the overlay still up. Hosted and demo builds only (`DemoPolicy.cs:203`:
  `AllowStrategies => Mode == HostMode.Full`, so no Manage Strategies button precedes the tree).
- **Location:** `keyboard.js:159` (`summary` with no `:not([tabindex="-1"])`);
  `ObjectTreeModal.razor:46-48` (`<summary role="treeitem" tabindex="@(firstItem ? 0 : -1)">`);
  `treeKeyboard.js:111-122` (`focusTreeitem` sets every treeitem to `tabindex="-1"` in JS).

Chain: on open the pane summary has tabindex 0. ArrowDown → treeKeyboard roves it to `-1` and the
series `<div role="treeitem">` to `0`. Shift+Tab: `focusables` = [pane-summary (matched by the
bare `summary` clause), series-div, …, Close]; `idx` of the series div is 1, not 0, so no branch
fires and no preventDefault. The browser walks backward: pane summary (-1) skipped, h2 (-1)
skipped, nothing else precedes → out of the dialog. Blazor never restores the summary's tabindex
because its diff tracks its own render tree, not the JS-mutated attribute. Before 553960f7 the
summary was not in the list, so the series div was idx 0 and wrapped — this is new.

The same divergence exists for `<button role="tab" tabindex="-1">` (SettingsModal.razor:99,
matched by `button:not([disabled])`), harmless only because those tabs are never first or last.

Fix (one line): append `:not([tabindex="-1"])` to EVERY tag clause in `focusableSelector`
(`a[href]:not([tabindex="-1"]), button:not([disabled]):not([tabindex="-1"]), … summary:not([tabindex="-1"])`)
— `tabindex="-1"` removes any element from sequential focus regardless of tag.

### Serious: Stacked dialogs — the trap yanks focus from the top dialog into the one underneath
- **WCAG:** 2.4.3 Focus Order (A)
- **Confidence:** high (code trace + node repro Q2a; this is audit 3.1(c), confirmed precisely)
- **Impact:** Open any dialog rendered after HelpModal in MainLayout, press F1 (OpenHelp is in
  `allowedWhileModalOpen`, CommandDispatcher.cs:202): the FIRST Tab or Shift+Tab from Help's
  heading — or from ANY control in Help — is preventDefault'ed and focus is moved into the
  underlying dialog (Settings' search box, or its Close on Shift+Tab). Help's overlay is still on
  top and `aria-modal` on Help means the screen reader will not describe where the user now is.
- **Location:** `keyboard.js:141` — `const modal = dialogs[dialogs.length - 1];` — DOM order, which
  is the constant render order at `Layout/MainLayout.razor:102-149`. Help is line 105; Journal,
  Settings, SoundDesigner, TradingDashboard, OrderBook, ApiKeys, Wallet, Strategy, Alerts,
  CustomScripts, AIAnalyst, Save/LoadWorkspace, MyData, Watchlist, LevelReport, AssetDossier,
  ThemeEditor and LabelText all render AFTER it. Then `keyboard.js:190-199`: `modal.contains(active)`
  is false → `(e.shiftKey ? last : first).focus()` on the WRONG dialog.

Can focus move from the top dialog into the underlying one? YES — the trap does it itself, on the
first keystroke, from every focus position inside the top dialog (Q2a: h2, summary, Close all →
settings-search). ThemeEditor-over-Settings (Q2b) works only because ThemeEditor happens to render
later (line 148 vs 113).

Fix (one line, mitigation): `const modal = dialogs.find(d => d.contains(document.activeElement)) ?? dialogs[dialogs.length - 1];`
— the real fix is the ordered modal stack shared with CommandDispatcher (already on the NEXT list).

---

## UNVERIFIED concerns

1. **Context menus (audit 3.1(b), "menu" half) are still untrapped and untracked.**
   `ChartContextMenu.razor:131` / `DrawingContextMenu.razor:122` publish
   `ModalStateChangedEvent(true)` so `_openModalCount > 0`, but `role="menu"` is not in the
   selector → `dialogs.length === 0 → return` (node repro Q2c: Tab not prevented). Neither menu has
   `@onfocusout`/`onblur` (grep: none), so Tab leaves focus on the background with the menu still
   open and chart commands still refused. Not touched by these commits; the commit message only
   claims alertdialog. Verify: browser test opening ChartContextMenu (ContextMenu key), Tab, assert
   either the menu closed or focus stayed inside. Fix: close the menu on focusout, or add
   `[role="menu"]` to the selector.
2. **Closed `<details>` content in Chromium.** Chromium renders closed details content with
   `content-visibility:hidden`; the trap relies on `offsetParent === null` to exclude those
   descendants. CI's green `Tab_never_escapes` on HelpModal (37 details) is evidence it holds,
   but no test asserts it. Verify: in the browser probe, count focusables inside a closed details.
3. **Non-heading `tabindex="-1"` element focused inside a dialog gives wrong ORDER, not escape**
   (node Q1c/Q1d): Tab → dialog's `first`, Shift+Tab → dialog's `last`, instead of next/previous.
   Reachable if ConditionTreeEditor's `tabindex="@(isSelected ? 0 : -1)"` (`:351`) re-renders while
   focus and selection differ, or a focused button becomes `disabled` mid-submit. 2.4.3, minor.
   Verify: bUnit/browser test that focuses a treeitem, changes selection, presses Tab.
4. **Radio groups**: the browser tabs to the CHECKED radio only; the selector lists all. No dialog
   currently contains `<input type="radio">` (only `SpeechOutputPrompt.razor`, a `role="region"`,
   not a dialog), so this is a future instance of the summary bug, not a live one.

---

## Checked and found CORRECT

- **Q1 single-dialog positions** (node Q1a-Q1g, all against the real file):
  h2 (`idx -1`, inside) → Tab seeds first, Shift+Tab seeds last, both prevented;
  `<summary>` mid-list → browser owns it (not prevented), at the end wraps to first;
  disabled control focused → contained (seeds first/last);
  `tabindex="-1"` non-heading → contained;
  focus on `<body>`, on a background button, or `activeElement === null` → contained, correct end;
  exactly ONE focusable → pins on it in both directions;
  ZERO focusables → NOT prevented (browser leaves) — but every dialog-family element in the
  tree has at least one unconditional `<button @onclick="Close|Cancel…">` (grep across all 26
  `role="dialog|alertdialog"` files: min count 1), so no live instance. Worth a comment, not a fix.
- **Q3 focusableSelector vs the app's markup**: `<details>/<summary>` — covered (with the tabindex
  caveat above). `<object>`, `<embed>`, `<area>`, `<audio>`, `<video>`, `<iframe>`, `<dialog>`,
  `contenteditable` — none exist in any .razor/.cshtml (grep of both projects). `<a>` without href
  — one, `WebHost/Pages/Account/Security.cshtml:88`, outside the Blazor app. `tabindex="0"` ×9 and
  the four `@(… ? 0 : -1)` roving sites — all handled by `[tabindex]:not([tabindex="-1"])`.
  `<fieldset disabled>`, `inert`, `visibility:hidden` — none inside any dialog (the only
  `visibility:hidden` is ChartArea.razor:186).
- **Scroll-key release** (`keyboard.js:277-280`): unmodified and Shift+arrows on a plain control in
  a modal → released, nothing sent to .NET; Ctrl+arrow → still trapped and sent; arrow on
  `role="option"` inside a listbox → still trapped (owner found); no modal → chart navigation
  untouched. Every `@onkeydown` arrow consumer in a dialog sits on `role="tablist"`,
  `role="listbox"` or a `role="tree"` (grep), all in `ARROW_WIDGET_SELECTOR`. Correct.
- **ModalBase dialogs and the offsetParent filter**: `.modal-overlay` is the fixed element
  (app.css:402), `role="dialog"` is on its child `.modal-content` (no `position` rule, app.css:420,
  506) → non-null offsetParent. Only the Toolbar alertdialog breaks this.
- **The jstests harness fix in 553960f7** is real: `matchesSelector` applies the selector to the
  candidate tag/tabindex, so removing `summary` from the selector reddens a test. It still cannot
  express `offsetParent` semantics (every fake node is `{}`), which is the hole the alertdialog
  defect lives in.

## Could not finish

- No browser can run here; the alertdialog `offsetParent === null` claim rests on the CSSOM-View
  spec and MDN rather than a Chromium run. A one-route CI browser test (open the switch warning,
  Shift+Tab, assert containment — with the probe's `dialogs.length === 0 → true` fixed) settles it.

## Modal Specialist Findings Summary
- **Issues found:** 3 confirmed + 4 unverified
- **Critical:** 1 | **Serious:** 2 | **Moderate:** 2 | **Minor:** 2
- **High confidence:** 3 | **Medium:** 2 | **Low:** 2
