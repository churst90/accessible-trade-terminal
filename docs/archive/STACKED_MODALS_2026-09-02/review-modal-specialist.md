# Modal-specialist review: the ordered modal stack (a8ce12ec WIP, reviewed 2026-09-02)

STATUS: COMPLETE. Evidence: full `git diff` + the three new files read; every ModalStateChangedEvent publisher grepped; every ShowAsync/Close ordering read; `node tools/jstests/keyboard-tests.mjs` = 33/33. No dotnet run.

## Confirmed defects

### Serious: F1 while Help is open pushes a second "Help"; the first Escape then leaves a phantom entry — Escape is dead and every chart command stays gated until reload
- **WCAG:** 2.1.2 No Keyboard Trap (Level A) / 2.1.1 Keyboard (A)
- **Confidence:** high by code reading; NOT executed (no dotnet/browser run permitted here). Pre-existing — the old private Stack<string?> did exactly the same Push twice — so it is not a regression of this diff, but the diff re-implements the semantics and its tests pin duplicates as intended behaviour.
- **Impact:** A screen-reader user presses F1 twice ("did that work?"), Escapes once, and from then on Escape does nothing, arrows/letters on the chart do nothing, and no focus is ever sent back to the chart.
- **Location:** `AccessibleTrader.BlazorClient.Components/HelpModal.razor:440-447` (ShowAsync has no `_isVisible` guard); `AccessibleTrader.Core/Services/Input/CommandDispatcher.cs:179,222` (OpenHelp is allowedWhileModalOpen and publishes unconditionally); `AccessibleTrader.Core/Services/Input/ModalStack.cs` Apply (open always Adds).
- **Repro (browser harness):** F1, F1 → `ModalStackAsync()` == ["Help","Help"]; Escape → Help gone, `_openModalCount` == 1 with `VisibleDialogIdsAsync()` empty; Escape → nothing; ArrowRight → no bar announcement. C# unit: `Apply(open Help); Apply(open Help); Apply(close Help)` → `Top == "Help"`, `IsAnyOpen == true`.
- **Fix:** guard in HelpModal.ShowAsync (`if (_isVisible) { re-focus help-title; return; }`) — and the same guard in `ModalBase.ShowModalAsync` for the day another opener becomes allowed-while-open. Alternatively make ModalStack.Apply's open a move-to-top for a name already on the stack (JS sees [Help]→[Help]: "nothing changed", returnTo preserved).

## Unverified concerns (code-certain, scenario not demonstrated)

### Moderate: `_returnFocusAfterClose` runs for a NON-top removal and moves focus to the top dialog's heading while the user is legitimately inside the top dialog
- keyboard.js:149 `if (removed && next.length > 0)` fires for any removal, including `prev=[Strategy, Help] → [Help]`. Then `target` = Strategy's opener (not inside Help) → heading fallback → Help's heading is focused under the user, unprompted.
- Not reachable today: no modal closes inside a foreign subscription (awk sweep of every `Subscribe<` within 6 lines of a Close in *Modal.razor: zero hits); ObjectTree closes ITSELF then publishes OpenStrategies (ObjectTreeModal.razor:245-248); Toolbar's alertdialog opens only from the Load button, which is under every overlay. But `ModalStackTests.Closing_a_modal_that_is_not_on_top...` and the dispatcher comments both promise this path.
- Fix (one line, start of `_returnFocusAfterClose`): `if (top && top.contains(document.activeElement)) return;`

### Minor: `usable` does not verify the focus actually landed
- keyboard.js:167-170: a `returnTo` that is now `disabled` (or `visibility:hidden`) passes `getClientRects().length > 0`, `focus()` no-ops, the closing render then drops focus on `<body>`. Fix: add `!target.disabled` and after `target.focus()` check `document.activeElement === target`, else fall through to the heading.

### Minor (Q2): a context menu on top of a dialog would trap Tab in the dialog beneath — identical to before, not worse, and not reachable
- OpenChartContextMenu / OpenDrawingContextMenu are not in `allowedWhileModalOpen` (CommandDispatcher.cs:172-180); the mouse path (DrawingInteractionManager.cs:334,346) and TouchNavBar.razor:168 are under the overlay. Realistic stack with a menu is `[ChartContextMenu]` alone → `_visibleDialogs()` empty → trap returns at keyboard.js:252, exactly as the old counter did.
- If it ever happens: `_topDialog` falls through to the dialog beneath, `!inside` → focus yanked out of the menu into that dialog. Before: DOM-last dialog — the same yank. Suggest stamping `data-modal-name` on the two `role="menu"` elements and resolving `[data-modal-name]` from any rendered element, so a menu on top suppresses the dialog trap instead of trapping beneath it.

### Minor (Q3): the out-of-step rebuild throws away returnTo for entries it still recognises
- keyboard.js:143 rebuilds every entry with `returnTo: null`. `prev.find(e => e.name === n)` would keep the targets of entries that survived. Only matters after a dropped call.

### Doc drift: `StackedModalBrowserTests.cs` header still describes the PRE-fix state ("leaves two holes this file demonstrates").

## Correct — do not re-check

**Q1 focus return / timing.** Correct in BOTH orders.
- Every open publishes BEFORE `StateHasChanged()`: ModalBase.cs:90-92; the 13 self-implemented ShowAsyncs (AddIndicator:187-189, ApiKeys:415-417, CustomScripts:236-238, DrawingTools:68-70, Help:442-444, Journal:212-215, ObjectTree:230-232, OrderBook:205-210, Properties, Settings:1903-1905, Strategy:908-910, TradingDashboard, Toolbar:810-813). `EventBus.Publish` is a synchronous `Subject.OnNext` (EventBus.cs:54-58); ModalStack.Changed fires inline; MainLayout's `InvokeVoidAsync` is enqueued on the circuit before the render batch that follows the handler. So `returnTo = document.activeElement` is the opener on Blazor Server; BlazorWebView serialises interop and render batches over the same IPC channel, so the same holds there.
- If the call arrived AFTER the render anyway: on push, `returnTo` would be the new dialog's own heading (focusElement); on its close `top.contains(target)` is false → heading of the dialog beneath. Safe. On close-after-render: the returnTo element lives in the dialog beneath and is untouched by the removal render; `_topDialog` resolves from `next`, which no longer holds the closed name, so it can never pick the closing dialog even while it is still rendered.
- Cannot focus under the top: `top.contains(target)` is mandatory whenever any stack entry resolves. Cannot focus hidden: `getClientRects().length > 0` (display:none and display:none ancestors). Cannot focus inside the closing dialog: needs `top == null` (every remaining entry a non-dialog) AND a returnTo captured after render — menus `Close()` before they publish an Open*Event (ChartContextMenu.razor:254-256, DrawingContextMenu.razor:210-211), so the stack never holds a menu beneath a dialog.
- All 26 `aria-labelledby` targets carry `tabindex="-1"` (grep table), so the heading fallback is a real focus, not a no-op.

**Q3 diff heuristic vs C# `LastIndexOf`.** Consistent, including duplicates: prev=[Help,Settings,Help] close Help → C# removes index 2, JS d=2 removes prev[2]; close Settings → JS d=1 removes prev[1]; prev=[A,A]→[A] removes the newer in both. Two events in one handler (ObjectTree Close + OpenStrategies) arrive as two single-step calls. A close for a name not open raises no Changed → no call (old code called setModalOpen(false) and clamped — equivalent).

**Q4 names — all 26 match what is published at runtime.**
- ModalBase override: AIAnalyst "AI Analyst" (:216), Alerts "Alerts" (:274), SoundDesigner "Sound designer" (:328), LoadWorkspace "Load workspace" (:202), SaveWorkspace "Save workspace" (:154).
- ModalBase default (`GetType().Name` minus "Modal", ModalBase.cs:152-162; no .razor.cs overrides, no extra literal publishes): AssetDossier, LabelText, LevelReport, MyData, ThemeEditor, Wallet, Watchlist, Withdraw.
- Self-implemented (open and close literals identical in each file): "Add indicator" (188/206), "API keys" (416/426), "Custom scripts" (237/516), "Drawing tools" (69/82), "Help" (443/452), "Journal" (214/223), "Object tree" (231/240), "Order book" (209/370), "Properties" (799/808), "Settings" (1904/1955), "Strategy manager" (909/925), "Trading dashboard" (1085/1558), Toolbar "ShapeChangeWarning" (812/865).
- Only Toolbar.razor has two dialog-family elements and only one is a real dialog (the count-2 is the alertdialog plus a `role="dialog"` string in a comment; the scan strips comments). No nested dialogs in any modal.

**Q5 regressions — none found.**
- Single dialog: `_topDialog` resolves the one name to the one element; the containment/DOM-last fallbacks are the previous behaviour verbatim. Toolbar alertdialog: name matches, `_visibleDialogs` still uses getClientRects, `CloseSwitchWarningAsync` focuses the Load button itself on a last close (JS does nothing on a last close). MainLayout keeps its speech subscription; only the JS call moved.
- Dispatcher: `IsAnyModalOpen`, Escape re-route, `CloseTopModalEvent(Top)`, out-of-order close, and RequestChartFocusEvent-only-on-last-close are semantically identical; the new `ChartFocus_IsRequested_OnlyWhenTheLastModalCloses` test pins the last part.
- DI: no production `new CommandDispatcher(` — `_ownsModalStack` is test-only. Both hosts register ModalStack at the dispatcher's lifetime (WebHost scoped with a scoped IEventBus at :67; MAUI singleton with singleton bus at :58). MainLayout injects it so it exists before any modal renders. Note (not a defect): MS DI picks `ModalStack(IEventBus)` only because IEventBus is registered — if it ever weren't, the parameterless test ctor would be chosen silently and the stack never fed.
- jstests: 33/33, including the four new stacked cases and the harness's `isConnected`/`data-modal-name` additions; `mountDialogs` now pushes the stack rather than a bool.

## Modal Specialist Findings Summary
- **Issues found:** 6
- **Critical:** 0 | **Serious:** 1 | **Moderate:** 1 | **Minor:** 4
- **High confidence:** 1 | **Medium:** 2 | **Low:** 3
