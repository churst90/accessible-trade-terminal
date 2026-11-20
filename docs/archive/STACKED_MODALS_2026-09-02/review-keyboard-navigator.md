# Keyboard-navigator review: shared modal stack (uncommitted, 2026-09-02)

Status: COMPLETE. No `dotnet` command run; browser-test claims are marked unverified.

Files: `AccessibleTrader.BlazorClient.Components/wwwroot/js/keyboard.js`, `tools/jstests/keyboard-tests.mjs`,
`AccessibleTrader.Core/Services/Input/ModalStack.cs`, `AccessibleTrader.BrowserTests/StackedModalBrowserTests.cs`.
Probe scripts: `scratchpad/probe-stack.mjs` (Q1), `scratchpad/sabotage.mjs` (Q2), `scratchpad/probe-fkeys.mjs` (Q3).

## Baseline
- `node tools/jstests/keyboard-tests.mjs`: 33/33 on the working tree.
- data-modal-name == published ModalName on all 27 dialog-bearing components (grep; ModalBase default =
  class name minus "Modal" matches ThemeEditor/AssetDossier/LabelText/LevelReport/MyData/Wallet/Watchlist/
  Withdraw). New ModalContractScanTests clause pins it. ChartContextMenu/DrawingContextMenu publish names but
  are role=menu (no dialog) — `_topDialog` skips them by design.

## Q1. Trap correctness, Help (DOM-first) on top of Settings (DOM-last) — CORRECT
Probe (`probe-stack.mjs`) models a nested DOM with browser sequential navigation for un-prevented Tabs.
Stack pushed as production would: `['Settings']` with focus on the toolbar, then `['Settings','Help']` with
focus on Settings' General tab. All 20 cases (10 positions x Tab/Shift+Tab) land INSIDE Help:
- heading (tabindex=-1): Tab->first stop, Shift+Tab->last (idx===-1 branch, prevented)
- first stop: Tab un-prevented, browser goes to next stop which is inside Help (Help is contiguous in DOM);
  Shift+Tab -> last (prevented)
- last stop: Tab -> first (prevented); Shift+Tab un-prevented, browser goes back inside Help
- non-focusable prose / roved summary tabindex=-1 inside Help: idx===-1 -> first/last
- body, any element in Settings (search, General tab, heading), toolbar Load: `!inside` -> first/last of Help
- 8 Tab + 8 Shift+Tab walk from the heading never leaves {h-s1,h-s2,h-close}.
Close Help (stack -> ['Settings'], Help still rendered at call time): focus -> tab-general (the recorded
opener); Tab from s-close then wraps to s-search, i.e. the trap is now on Settings. With returnTo = Settings'
heading (cold F12,F1 route) focus returns to the heading. Nothing lets focus stay or land in the lower dialog.

Why it holds: `_topDialog` (keyboard.js:191-200) resolves the stack top by `data-modal-name` before any
containment/DOM-last fallback (keyboard.js:319-321); `modal.contains(active)` is false for anything in the
lower dialog, so the `!inside` branch seeds first/last of the TOP dialog.

## Q2. Harness: does any mock hand the code its own answer? — mostly no; two tests are weaker than named
- `mountDialogs` pushes the stack from `data-modal-name` in mount order. That does NOT make test A vacuous:
  "keeps focus in the one on TOP OF THE STACK" re-pushes `['Settings','Help']`, which hits the rebuild branch
  (equal length, disagreement at 0) so top=Help while DOM-last=Settings. RED under S1/S2/S4 below.
- `d.querySelectorAll` applies the selector for real (matchesSelector); `getAttribute('data-modal-name')`,
  `aria-labelledby`, `getClientRects`, `isConnected` are real attribute/option reads. No self-answering double
  in the new code path. `document.querySelectorAll` sniffs the role strings in the selector (`sel.includes(
  '[role="dialog"]')`) — acceptable, it would go red if the family selector changed shape.

Sabotage matrix (control 33/33; failing test names recorded):
| sabotage | result | reds |
|---|---|---|
| S1 `_topDialog` returns null | 29/33 | A (escaped focus -> Settings), B (Help focus not pulled up), heading-fallback, out-of-step |
| S2 trap uses DOM-last only | 31/33 | A, out-of-step |
| S3 `_returnFocusAfterClose` never called | 31/33 | returns-to-opener, heading-fallback |
| S4 containment-only (old mitigation) | 30/33 | A, B, out-of-step |
| S5 return-focus also on LAST close (guard removed) | 33/33 SURVIVES — behaviourally inert (top=null, closing dialog never contains its own opener) |
| S6 `top.contains(target)` check removed | 33/33 SURVIVES — the "opener rendered but outside the top dialog" guard is untested |
| S7 heading fallback removed | 32/33 | heading-fallback |
| S8 returnTo recorded as null | 32/33 | returns-to-opener |

Findings on the new tests:
1. Test B "the trap follows the modal STACK, not DOM order: reverse the open order and Settings is top"
   (keyboard-tests.mjs:478-499) is MISNAMED: `mountDialogs(help, settings)` already pushed `['Help','Settings']`,
   and the explicit `setModalStack(['Help','Settings'])` is a no-op (`next = prev`). Stack top == DOM-last, so
   the test cannot distinguish the stack from DOM order — it survives S2 (DOM-last). It does catch S1/S4 via
   its second half (focus in the lower dialog is pulled up). Suggest renaming or swapping the mount order.
2. Test "a stack entry with no dialog element (a role=menu) falls through to the entry beneath"
   (keyboard-tests.mjs:501-512) is green under S1, S2 AND S4: one dialog mounted, so every strategy picks
   Settings. It only proves an unresolvable top does not disarm the trap. To make it bite: mount
   `(help, settings)`, push `['Settings','Help','ChartContextMenu']`, Tab from outside must land in HELP.
3. S6 survivor: `_returnFocusAfterClose`'s `top.contains(target)` clause (keyboard.js:169-171) has no test.
   Realistic path: A opened from the chart (returnTo = chart canvas), B opened from A, A closed out of order
   (programmatic close) — without the clause focus goes to the chart behind B while B is aria-modal.
   Also the `target.isConnected` clause is untested (`detached` option exists in the harness, no test uses it).
4. Test "the LAST close moves nothing here" cannot detect removal of the `next.length > 0` guard (S5). Low: the
   guard is defensive, not load-bearing.

## Q3. isFormControl guard kills F1-F12 in text fields — CONFIRMED (not part of this diff, not fixed)
- keyboard.js:398-401:
  `const tag = e.target.tagName; const isFormControl = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';`
  `if ((isFormControl || isEditable) && !isModified && e.key !== 'Escape') return;`
  The return precedes `dotnetHelper.invokeMethodAsync('OnKeyDown', ...)` at keyboard.js:484-485, and
  `e.preventDefault()` at :461. Doc comment at keyboard.js:206-208 promises "F1-F12 and arrow keys must work
  regardless of which element is focused — including when the user is in the toolbar dropdowns"; the toolbar
  dropdowns are `<select>` (Toolbar.razor:195,223,252), the exact tag the guard returns for.
- Probe (`probe-fkeys.mjs`): INPUT/TEXTAREA/SELECT + F1/F2/F12 -> prevented=false, nothing sent to .NET;
  BUTTON + same keys -> sent. Escape is the only unmodified key that passes.
- No other route: ChartArea's `@onkeydown` only fires with focus in the chart; no modal binds F-keys.
- Bindings: F1 OpenHelp, F12 OpenSettings, F2 ToggleSpeech, Shift+F2 ToggleEventSpeech, F3/F4 the other
  accessibility toggles (ShortcutManager.cs:328-342). F2/F3/F4 are in the dispatcher's allowedWhileModalOpen
  list precisely because they are meant to be global.
- Consequence: default NOT prevented, so in a browser F1 in the symbol `<select>` opens the browser's help
  tab, F12 opens DevTools, F11 fullscreens; a screen-reader user who wants to mute speech (F2) while in the
  Settings search box or a `<select>` cannot, and Help's own table (HelpModal.razor:273) says F1 opens Help.
- WCAG: 2.1.1 Keyboard (Level A) — the functions remain reachable after Tabbing off the field, so this is a
  contextual failure of the app's documented keyboard contract rather than an absolute one; confidence on the
  criterion mapping medium, on the defect high. Severity: serious (global accessibility toggles unreachable
  from any form field; F1 navigates the user away in a browser). Fix shape (not applied): let F1-F12 through
  the guard the way Escape is, e.g. `&& !/^F\d{1,2}$/.test(e.key)`.

## Other findings
5. `StackedModalBrowserTests.DIAG_second_escape` (StackedModalBrowserTests.cs:215-231) `throw new Exception(...)`
   unconditionally — an always-red diagnostic left in an uncommitted test file. Delete before commit.
6. Unverified here (dotnet not run): the two browser assertions that depend on message ORDER — that
   `setModalStack` arrives before the render that shows/removes the dialog so `document.activeElement` at push
   time is the opener and at removal time the opener is still rendered. Reading ModalBase.cs:87-99/140-145 the
   order is Publish (-> interop message) then StateHasChanged (-> render batch) on the same circuit, so it
   should hold; `Closing_the_top_dialog_returns_focus_to_the_dialog_beneath_it` and
   `Theme_editor_over_Settings_returns_focus_to_the_button_that_opened_it` are the gates.
7. Pre-existing, not this diff: a role=menu alone on the stack (`_modalStack.length>0`, zero dialogs) returns
   from the trap, so Tab is free to leave a context menu. The old counter behaved the same.

## What is correct
- `setModalStack` diff: single push / single removal / no-op / rebuild all reasoned through, including
  duplicate names (removes the newest, matching `ModalStack.Apply`'s `LastIndexOf`).
- `_visibleDialogs` uses `getClientRects()` (not `offsetParent`), whole dialog family.
- `_openModalCount` kept as a derived value for the existing guards.
- `ModalStack` C#: one list, lock, `LastIndexOf` close-by-name, ignore unknown close, Changed raised after the
  mutation with a copy; dispatcher reads `Top`/`IsAnyOpen` from it; DI lifetimes match the dispatcher in both
  hosts (singleton MAUI, scoped WebHost).
- New ModalContractScanTests clause checks the attribute on the opening tag and the ModalBase default name.
