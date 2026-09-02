# Keyboard Navigator review — commits bc52e652 and 553960f7

Reviewer: keyboard-navigator (accessibility-agents 3.2.0). Review only; nothing in the tree was edited.
Repo: accessible-trade-terminal, branch main, clean tree. Date: 2026-09-02.
Paths below are relative to /home/cody/external-rescue/Github/accessible-trade-terminal unless absolute.

Method notes. `node tools/jstests/keyboard-tests.mjs` => 24/24, exit 0. A Chromium binary exists on this box
(/home/cody/.cache/ms-playwright/chromium-1187/chrome-linux/chrome, reports Chrome/140), so the questions that
needed a real engine were run in it over CDP (`Input.dispatchKeyEvent`, trusted keys) against the COMMITTED
keyboard.js and app.css loaded from the tree. Probe pages and driver are in this scratchpad directory:
`offsetparent-probe.html`, `ring-probe.html`, `cdp-page.html`, `cdp-page2.html`, `instrument.js`,
`cdp-drive.mjs`, `ring-contrast.py`. The app's own browser harness was not run (cannot start here).

## VERDICT

**NO-SHIP as described — SHIP as code.** The code in both commits is an improvement everywhere it acts, but the
headline claim of bc52e652 ("the destructive 'strip your indicators and drawings' alertdialog is now trapped")
is false: the trap is still inert for that dialog, and all three gates that were meant to prove it — the jstest,
the C# selector guard, and the CI browser theory — are each blind to the reason. The commit message, the
keyboard.js comment at :125-133, and docs/TODO.md should not carry that claim.

---

## CONFIRMED defects

### 1. The widened dialog selector cannot reach the one alertdialog in the app: it is `position:fixed`, and the trap's `offsetParent !== null` filter drops it

- **Severity:** serious
- **WCAG:** 2.1.2 No Keyboard Trap is not the issue; this is 2.4.3 Focus Order (A) / 4.1.2 (aria-modal overclaims) — same class as the Shift+Tab defect the commit fixed.
- **Confidence:** high — reproduced in Chromium 140.
- **Impact:** Tab or Shift+Tab from the "switch chart / strip indicators" confirmation walks onto the toolbar behind it (the Load button that raised it) while `aria-modal="true"` tells the screen reader not to describe anything out there. Exactly the scenario the commit describes, still live.
- **Location:** `AccessibleTrader.BlazorClient.Components/wwwroot/js/keyboard.js:137-140` (selector at :138, filter at :139) against `AccessibleTrader.BlazorClient.Components/Toolbar.razor:430-434` (`<div role="alertdialog" ... style="position:fixed; ...">`).

**How confirmed.** CSSOM View defines `offsetParent` as `null` when the element's computed `position` is `fixed`.
Probe 1 (`offsetparent-probe.html`, headless Chromium 140): with a `.modal-content[role=dialog]` inside a fixed
overlay and a `role=alertdialog` that is itself fixed, the trap's exact expression
`querySelectorAll('[role="dialog"], [role="alertdialog"]').filter(el => el.offsetParent !== null)` returned
`matched=dialog,alertdialog | offsetParent null? dialog:false,alertdialog:true | kept after filter=dialog`.
Probe 2 (`cdp-drive.mjs`, real keyboard.js loaded, `setModalOpen(true)`, trusted Tab keys):
- position:fixed (Toolbar's shape): `visibleDialogsSeenByTrap=0`; Tab x4 from the LAST button: `bg-after > (body) > bg-before > ad-continue`; Shift+Tab x2 from the FIRST button: `bg-before > (body)`. Instrumented run: `keydown Tab prevented=false`, then `focusin bg-after`.
- position:absolute (control, everything else identical): Tab from the last button wraps via `keyboard.js:205` (`prevented=true`, `focus(ad-continue)`); Shift+Tab from the first wraps to `ad-cancel`.
So the selector edit at :138 is correct and the filter at :139 undoes it for this dialog. (`.modal-content` is NOT fixed — the fixed element is its `.modal-overlay` parent, app.css:402-403 — so the 25 ModalBase dialogs are unaffected; CI's green run on them is consistent with this.)

**Why every gate was green.**
- `tools/jstests/keyboard-tests.mjs:67` fakes `offsetParent: opts.hidden ? null : {}` — a node is visible unless the test says `hidden`. The test at :327-344 ("the trap sees role=alertdialog") therefore proves the selector string, not the trap. Same shape as the `querySelectorAll` mock 553960f7 fixed: the harness hands the code the answer to the question the browser actually asks.
- `AccessibleTrader.Tests/ChromeAccessibilityScanTests.cs` `TheJsTabTrapCoversTheWholeAriaDialogFamily` (added in bc52e652) string-reads the selector for `role="alertdialog"`; the filter is on the next line and is invisible to a presence check (repo rule: scan guards need a path check).
- `AccessibleTrader.BrowserTests/ModalBrowserContractTests.cs:173` `ShiftTab_never_escapes_an_open_dialog` runs only over `RouteNames` (ModalRoute.cs:50-111: the 25 ModalBase dialogs; no route opens Toolbar's switch warning), AND its `inside` predicate at :203 (and Tab's at :135) reads `if (dialogs.length === 0) return true;` with the same `offsetParent` filter — so even a route that did open the fixed alertdialog would pass vacuously.

**Fix (one line each):** filter dialogs by `getClientRects().length > 0` (or `!el.hidden && getComputedStyle(el).display !== 'none'`) instead of `offsetParent`, at keyboard.js:139 AND in both browser predicates (ModalBrowserContractTests.cs:132-135, :200-203); give keyboard-tests.mjs a `fixed: true` node option that returns `offsetParent: null` and pin it.

### 2. The commit message / comment claim is wrong, and a stale comment persists

- **Severity:** minor (documentation) — but it is the standing rule.
- **Location:** `keyboard.js:125-133` says the alertdialog is now trapped; bc52e652's message says the same; `docs/TODO.md` (bc52e652 hunk) records 3.1(b) as closed for alertdialog. All three should say: selector widened, dialog still not trapped because of the `offsetParent` filter (finding 1).

---

## Question 1 — scroll-key release: what is released, what stays trapped, double handling?

**Released (keyboard.js:30-33, :277-280):** ArrowLeft/Right/Up/Down, Home, End, PageUp, PageDown, only when
`_openModalCount > 0`, only without Ctrl/Alt (`!isModified`, :241), and only when `e.target.closest(ARROW_WIDGET_SELECTOR)` is null.
**Kept trapped (:38-41):** focus inside `[role=tablist|tab|tree|treeitem|listbox|option|menu|menuitem|radiogroup|slider|spinbutton|grid]`.
Shift+scroll-key is released (Shift is not in `isModified`); Ctrl/Alt+scroll-key stays trapped and is then dropped by
CommandDispatcher (Ctrl+Home / Ctrl+End inside a dialog remain dead — unmodified Home/End cover the same need; noted, not flagged).

**Native form controls — CONFIRMED no double handling, and no fixed second defect either.** The form-control early
return at `keyboard.js:251-254` (`INPUT`/`TEXTAREA`/`SELECT`/`isContentEditable` → `return` unless Ctrl/Alt or Escape)
runs BEFORE the release at :277 and predates both commits (unchanged context in the diff). So for every
`<input type="number">` (e.g. AlertsModal.razor:150, PropertiesModal.razor:108/176/188/293/359/638, SettingsModal.razor:291/384/908,
TradingDashboardModal.razor:133-390, SoundDesignerModal.razor:122/151/157/182, WatchlistModal.razor:218-308, StrategyModal.razor:372-556),
`<input type="range">` (PropertiesModal.razor:246/371/385/409/541/633/642, SoundDesignerModal.razor:115/128/163),
every `<select>` and every `<textarea>` in the modal set: the trap never claimed the key before and does not now;
the browser steps/scrolls the control natively exactly as before. Nothing changed for them; CDP run (b2) shows the
release applies to a `<button>` target (scrollTop 0→36, nothing sent to .NET), and the input branch is never reached.

**Composite widgets with their own handlers — CONFIRMED still trapped, no double handling:**
- Tablists: ModalBase.cs:123-134 `NavigateTablistAsync` via `TablistNavigator` (Arrow/Home/End) on AssetDossierModal.razor:46, LevelReportModal.razor:41, PropertiesModal.razor:30, SettingsModal.razor:83, StrategyModal.razor:37, TradingDashboardModal.razor:417, WatchlistModal.razor:54. Tab buttons sit inside the `role="tablist"` container, so `closest` finds the owner. CDP (b): ArrowRight/ArrowDown on a `role=tab` inside a scrollable dialog: `dlg.scrollTop` stayed 0 and `OnKeyDown RIGHT/DOWN` reached .NET. jstest :417-427 covers it.
- Trees: ConditionTreeEditor.razor:346-352 and ObjectTreeModal.razor:47-105 are driven by `treeKeyboard.js:155-223`, which calls `preventDefault()` itself for every key it handles — so keyboard.js's trap there is redundant but harmless (and releasing there would also not double-handle). The commit message's "arrowing through the condition tree moves focus" is true via treeKeyboard.js, not via any Razor handler.
- Listbox with arrows: LoadWorkspaceModal.razor:23-26 + :96-107 handles ArrowUp/Down with no preventDefault of its own → relies on keyboard.js; `role="listbox"` is in the selector → still trapped. Correct.

**Pre-existing, unchanged, not caused by these commits (recorded for completeness):** SaveWorkspaceModal.razor:30,
CustomScriptsModal.razor:34 and SoundDesignerModal.razor:27 are `role="listbox"` with options whose only handlers are
Enter/Space (SaveWorkspaceModal.razor:89, CustomScriptsModal.razor:243, SoundDesignerModal.razor:417) and no JS
handles listbox/option keys (grep of wwwroot/js). Arrows there are trapped by the new selector and consumed by nobody —
exactly as dead as before the commits. WCAG 2.1.1 / APG listbox gap, out of scope here.

**No handler outside the composite roles reacts to scroll keys** (grep for "ArrowUp|ArrowDown|Home|End|PageUp|PageDown"
across Components: only LoadWorkspaceModal (listbox), TabBar.razor:162-173 (TabBar.razor:25 is `role="tablist"`, and it is outside modals), and GlobalInputService's key-name map). So no double handling exists anywhere.

**Scroll release positive effect — CONFIRMED in engine:** CDP (a): modal open, focus on the `<h2 tabindex="-1">` of a
tall `.modal-content`: ArrowDown scrollTop 0→34, PageDown →242, End →1147 (bottom), nothing reached `OnKeyDown`.
(The app's CI browser suite has no scrolling test; jstest :365-377 only checks `preventDefault` was not called.)

## Question 2 — the focus ring

**Rules.** `[tabindex]:focus-visible` at app.css:260-267 (both copies) = `outline: var(--focus-outline)` (= `2px solid var(--focus-outline-color)`, :52), `outline-offset: 2px`.
New `#chart-interact-zone:focus-visible` at :274-277 = `outline: 3px solid var(--focus-outline-color); outline-offset: -3px`.
Specificity (1,1,0) beats (0,2,0); the inline `outline: none` is gone from ChartArea.razor:76 (diff confirmed). ChartArea's own `<style>` (ChartArea.razor:176-222) only styles `.chart-bar-slider`, `.blackout-overlay`, `.pane-divider`. No CSS in either app.css or any `.razor` targets `[role=treeitem]`, `[role=application]`, or `#chart-interact-zone` besides :274. No `.razor.css` isolation files exist. StrategyModal/ConditionTreeEditor have no `outline` rules.

**Confirmed in engine** (`ring-probe.html`, committed app.css, Chromium 140): chart div → `focus-visible=true outline=3px solid rgb(255,255,0) offset=-3px`; treeitem `<li role="treeitem" tabindex="0">` with the committed inline style → `focus-visible=true outline=2px solid rgb(255,255,0) offset=2px`. Both rules reach.

**Parity.** `diff BlazorClient/wwwroot/app.css WebHost/wwwroot/app.css` differs in exactly two hunks that predate these commits (a `flex-wrap: wrap` line at BlazorClient:749 absent from WebHost; a `.speech-prompt` block at WebHost:795-814 absent from BlazorClient). Every rule these commits touched (`--crosshair-color` :50, `#chart-interact-zone:focus-visible` :274-277) is byte-identical in both. The pre-existing drift is not in scope but is the kind that bit once already.

**Colour.** `--focus-outline-color` is `#ffff00` at `:root` (app.css:45) and is overwritten at runtime by ThemeCssBridge (`--focus-outline-color` = `FocusRingFor(theme)`, ThemeCssBridge.cs:103, :137: luminance(SurfaceRaised) > 0.5 ? #0020b0 : #ffff00), applied by MainLayout.razor:287-288 → keyboard.js:830-835 `root.style.setProperty` on `<html>`, which beats the stylesheet `:root` value. Measured (ring-contrast.py, WCAG relative luminance) against the surface the INSET ring actually sits on — the chart `Background`, plus `ChromeBottom`/`ChromeBottomEnd` for the lower edge — and against the toolbar it was chosen by:

| theme | ring | vs chart Background | vs ChromeBottom | vs BottomEnd | vs SurfaceRaised |
|---|---|---|---|---|---|
| SteelGray | #ffff00 | 7.10 | 14.32 | 16.39 | 4.79 |
| Blackout | #ffff00 | 19.56 | 19.56 | 18.44 | 18.44 |
| Classic | #ffff00 | 16.67 | 16.67 | 17.28 | 14.79 |
| AmberCrt | #ffff00 | 18.32 | 18.87 | 17.92 | 17.04 |
| Walnut | #ffff00 | 14.82 | 17.39 | 14.51 | 8.83 |
| Paper | #0020b0 | 11.07 | 9.98 | 8.78 | 9.29 |
| MidnightBlue | #ffff00 | 16.44 | 18.17 | 18.64 | 13.26 |
| HighContrastDark | #ffff00 | 19.56 | 17.98 | 17.98 | 17.16 |
| HighContrastLight | #0020b0 | 11.45 | 8.84 | 8.84 | 9.87 |
| SoftDark | #ffff00 | 17.12 | 16.81 | 16.81 | 14.94 |
| Solarized (SurfaceRaised default 30,30,30, ChartTheme.cs:96) | #ffff00 | ~14.0 (bg 0,43,54) | — | — | ~15 |
| BrailleOptimized (same default) | #ffff00 | 19.56 (black) | — | — | ~15 |

Every built-in theme: ring ≥ 2px (chart 3px, treeitems 2px), ≥ 3:1 against every adjacent colour → **2.4.7 (AA) met; 2.4.13 Focus Appearance (AAA) met** on built-ins (the chart's inset ring is also never clipped, so 2.4.11 holds).

**One trace-confirmed hole, contrast failure inferred:** `FocusRingFor` reads only `SurfaceRaised` (toolbar), but ThemeService.cs:139 lets the user override `Background` alone (`BackgroundOverrideKey`), and the chart ring sits on `Background`, not the toolbar. A dark theme with a user-chosen light chart background gets a yellow ring on a light canvas (#ffff00 on #ffffff = 1.07:1). UNVERIFIED as a user-facing failure (needs a run with the override set); the arithmetic is not in doubt. One-line fix: pick the chart ring from `theme.Background` luminance, or publish a second variable for the chart.

Note, not a defect: after a modal closes by mouse-click on Close, Chrome's `:focus-visible` heuristic may not show the chart ring on the programmatic refocus; after Escape it does. Standard behaviour.

## Question 3 — every remaining double in tools/jstests/keyboard-tests.mjs

Asked of each: if production got this wrong, would the mock still return the right answer?

1. **`offsetParent: opts.hidden ? null : {}` (:67)** — YES it masks. Production's `offsetParent !== null` is a visibility test that is also false for `position:fixed`; the mock cannot express fixed. This is finding 1, and it sits directly on the code path bc52e652 changed (:138-139). **Matters: yes, it is the defect.**
2. **`mountDialogs` selector matching by substring (:122-124)** — `sel.includes('[role="dialog"]')`: a production selector with the comma dropped (`'[role="dialog"] [role="alertdialog"]'`, descendant combinator) or bad quoting would still "match" both. Matters for bc52e652's selector edit; CONFIRMED benign today by reading :138 and by CDP matching both roles.
3. **`node.closest` (:77-82)** — only understands `[role="x"]` parts; a tag or class part in `ARROW_WIDGET_SELECTOR`, or a malformed selector (which throws in the browser and would abort the keydown handler), is invisible. Matters for the scroll-release path; CONFIRMED valid today by CDP (b) where `closest` found the tablist in a real engine.
4. **`matchesSelector` (:95-108)** — attribute requirements are ignored: `a[href]` matches any A; `input[type="hidden"]` is not excluded; `audio[controls]`/`video[controls]` match any audio/video; `[contenteditable]` NEVER matches (falls to the tag branch and fails). Of 553960f7's five additions only `summary` and `iframe` are faithfully testable. Matters for 553960f7; low today because nothing uses them (the commit says so).
5. **`dialog()` candidate list (:112-119)** — `querySelectorAll` only filters nodes the test author listed; a descendant given `parent = d` but not listed is never shown to the selector. Every trap test builds the `<h2 tabindex="-1">` that way (:270-272, :286-288, :335-338, :406-409), so the selector's `:not([tabindex="-1"])` exclusion is never exercised — dropping it keeps all 24 green. Matters for the `idx === -1` branch; browser impact would be benign (heading becomes idx 0 and Tab from it falls to browser default, which stays inside), so low.
6. **`node.focus()` (:73)** — always succeeds and always sets `activeElement`; the browser silently refuses to focus a non-focusable or hidden element. `first`/`last` come from the selector list so today's paths are safe; low.
7. **`document.activeElement` / browser default** — the harness never models what the browser does when `preventDefault` is NOT called; "prevented === false" is read as "browser owns it". Acceptable, but it is why the scroll release's positive effect is unproven by this file (proven here by CDP instead).
8. `contains` (:116-117), `getAttribute`/`hasAttribute` (:71-72), `disabled` (via attrs), `isContentEditable` fixed `false`, `stopImmediatePropagation` no-op, timers/`Date` stubs — faithful for these paths; nothing masked.

## Question 4 — focus when the chart is focused and a modal opens / closes

Untouched: open still goes ModalBase.cs:94 → `focusElement(heading)` (chart blur → `setChartFocused(false)`, ChartArea.razor:542); close still goes CommandDispatcher.cs:146-147 `RequestChartFocusEvent` when the count reaches 0 → ChartArea.razor:396 `focusElement("chart-interact-zone")`. The only observable change is that the chart now SHOWS its ring on that return (Chrome carries focus-visible through a programmatic focus after a keyboard action). The stacked-modal case (audit 3.1(d), focus left on `<body>`) is unchanged and still open.

## UNVERIFIED concerns (and what would verify each)

- U1. User `Background` override under a dark theme yields a yellow ring on a light canvas (Q2). Verify: set the background override to #ffffff on SteelGray and read `getComputedStyle(chart).outlineColor` vs canvas.
- U2. The `role="menu"` context menus (DrawingContextMenu.razor:30, ChartContextMenu.razor:38) — audit 3.1(b) says they raise `_openModalCount`; the trap does not look for `menu`, so Tab walks out of an open context menu. Not claimed fixed by these commits; verify with a browser route that opens a context menu and presses Tab.
- U3. Ctrl+Home/Ctrl+End inside dialogs remain dead (trapped then dropped). Verify by pressing them in the CI browser job; decide whether to add Ctrl to the release.
- U4. The headless trace showed one unexplained `focusin` onto the `tabindex="-1"` heading after the first Tab in BOTH fixed and absolute runs, with no script `focus()` recorded; uninstrumented runs did not show it. Treated as a CDP/headless artefact; it does not change finding 1 (the `prevented=false` and the escape to `bg-after`/`bg-before` are the evidence). Verify by repeating in the CI harness if anyone cares.

## Checked and found CORRECT

- Tab trap containment/position logic, keyboard.js:190-206: Shift+Tab from the heading → last; Tab from heading → first; wrap at both ends; middle Tab untouched; escaped focus rehomed to the correct end. jstests :259-361 exercise each branch; CDP control run (position:absolute) reproduced both wraps in-engine.
- `focusableSelector` :157-161 includes `summary`; the harness now applies the selector for real for tag-based parts; removing `summary` reddens :379-398 (per commit; consistent with the mock's behaviour as read).
- Scroll-key release scope (Q1) and the composite exclusion (Q1) — no double handling anywhere; native form controls unaffected; tree/tablist/listbox handlers still get their keys; dialog scrolling works in-engine.
- Chart focus ring and treeitem ring reach in-engine; widths 3px/2px; colours ≥ 7:1 on every built-in theme; inset by design; both app.css copies identical for every touched rule.
- `--crosshair-color` published by ThemeCssBridge.cs:116 and given a `:root` fallback in both app.css copies (:50); ChartArea.razor:116-117 reads it.
- `ThemeCssBridge.VariableNames` (:49, :52) lists both new variables, so the C# roster and the JS application agree.
- `.modal-content` (the 25 ModalBase dialogs) is not `position:fixed` — the overlay is — so the `offsetParent` filter keeps them; CI's green run on all 25 routes is consistent.
- Q4: modal open/close focus flow untouched.
- Correct-by-design and not flagged, per instructions: flat Tab stops on toolbars; scroll keys kept trapped inside composite widgets; the chart's inset ring.

## Could not finish

- The app's own real-Chromium harness (`AccessibleTrader.BrowserTests`) cannot start on this box; all in-engine evidence above comes from standalone pages that load the committed keyboard.js/app.css, not from the running app. The Toolbar alertdialog was reproduced from its exact markup/style (Toolbar.razor:430-434), not by opening it in the app.
- U1-U4 above.

## Keyboard Navigator Findings Summary
- **Issues found:** 2 (plus 4 unverified concerns)
- **Critical:** 0 | **Serious:** 1 | **Moderate:** 0 | **Minor:** 1
- **High confidence:** 2 | **Medium:** 0 | **Low:** 0
