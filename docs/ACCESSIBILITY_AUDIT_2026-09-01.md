# Accessibility Audit — Accessible Trade Terminal

**Date:** 2026-09-01
**Standard:** WCAG 2.2 Level AA (AAA noted where relevant)
**Scope:** the Blazor component library (44 `.razor`, ~19,000 lines), the WebHost shell and its
nine Razor Pages auth screens, all four stylesheets, the JS interop layer, and the C# speech,
theming, sonification and input services behind them.
**Method:** twelve parallel domain audits — architecture, ARIA, modals, keyboard, forms, contrast,
live regions, tables, structure, cognitive/WCAG-2.2, data visualization, text quality. Static
source analysis with computed contrast ratios. No runtime AT session; every claim that needs one
is marked UNVERIFIED.
**Nothing was edited.** This is an audit.

> **Correction, recorded rather than quietly edited.** One claim in this report was false: the
> coverage table said no accessibility guard walked any JavaScript. A JS test suite for
> `keyboard.js` existed and ran in CI. The mistake came from a repo-wide grep for `.js` inside the
> two `.csproj` test projects, which cannot see a `node`-run suite under `tools/`. It is corrected
> in §8 and noted here because a report that hides its own error rate is worth less than one that
> does not — the same reason the chart audit's failed hypotheses are printed in full.

---

## Verdict

This is the most seriously-engineered accessibility work in any codebase I have audited. That is
not a courtesy sentence — it is the finding that determines how to read everything below.

The evidence: hand-built source scanners with vacuity floors and population minimums; exemption
lists that were **deleted** rather than grown; a real-Chromium browser harness that fails rather
than skips when Chromium is absent; a speech router with a duration model so a navigation readout
cannot clip an order fill; three separate places that **refuse** to add a live region because it
would flood; and a first-run chooser that solves a problem browsers make technically unsolvable.

The defects are almost all in the **seams between well-guarded components** — the JS/C# split on
modal state, the boundary between the app's own speech channel and the platform accessibility
tree, and the gap between a scanner written in C# and the JavaScript it describes.

**272 findings** across the twelve domains. The severity distribution is not the important number.
Two things are:

1. **Ten instances of gate blindness** — a test that states the right rule and asserts something
   else. This repo has recorded that shape three times before. It is now the dominant failure
   mode, and it is why a green suite coexists with this list.
2. **One finding is a financial correctness bug, not an accessibility bug** (F-01 below). It
   should be fixed before anything else here.

---

## 1. The single most serious finding

### The live-order review goes stale, and the order that ships is not the order that was read out

**`TradingDashboardModal.razor:301-315, 1539, 2005, 1553, 2062`** · Severity: **Critical** ·
WCAG 3.3.4 Error Prevention (Legal, Financial, Data), AA · **Verified independently.**

> **CLOSED 2026-09-01 (second pass), and it was REPRODUCED before it was fixed.**
> `AccessibleTrader.Tests/Blazor/LiveOrderReviewStalenessTests.cs` drives the real component
> through the real `SubmitOrder`: arm at 1 BTC, edit the field, press Confirm, and the order
> service **received `Quantity: 5`**. This entry said "verified by hand against the source, not
> inferred"; it is now verified by execution. A second demonstration flips the SIDE after arming,
> through the BUY/SELL buttons, which are plain `@onclick` and cannot be reached by any binding
> hook — which is why the fix is not only per-control voiding. `BuildSignal()` was extracted from
> `SubmitOrder` so the review and the submit build the same order, and confirm-time compares the
> whole `TradeSignal` record. That also closes two holes this entry did not name: **Symbol and
> Provider are sent with every order and are not ticket fields**, so a chart moving to ETH under
> an armed BTC review was a third route to the same defect, and `SizeFromRisk` writes the quantity
> in C# where no input hook can observe it. Eight sabotages red, control green.
>
> **A FOURTH ROUTE, which this audit missed entirely:** `Close()` did not reset `_reviewArmed`
> and neither did `ShowAsync()`. Arm on BTC, Escape, load ETH, Alt+T — the dialog reopened with
> Confirm/Cancel already rendered and, because `ShowAsync` blanks `_orderStatus`, **nothing spoken
> and nothing on screen**. One Enter sent a live order on the new symbol that had never been
> reviewed at all. `Close()`'s own comment explains why a half-typed limit price must not come
> back, and the one flag that spends money was left out of it.

`ArmLiveReview()` builds a full spoken review — quantity, price, estimated cost, fee, stop, target,
and a liquidation-vs-stop risk warning — and requires a second explicit Confirm. That design is
correct and is genuinely better than most commercial venues.

But `_reviewArmed` is cleared in exactly two places:

```
1553:   _reviewArmed = false;      // on submit
2062:   _reviewArmed = false;      // on cancel
2005:   _reviewArmed = true;       // on arm
```

**No field edit voids it**, and the ticket inputs are never disabled — `:132` (Quantity) carries no
`disabled` binding. So:

> Arm the review at 0.5 BTC. Hear "Buy 0.5 BTC…". Edit quantity to 5. Press "Confirm Buy".
> **It sends 5.** The spoken review said 0.5.

For a blind trader the spoken review *is* the order ticket. This turns the safety mechanism into
the delivery vehicle for the error.

`WithdrawModal.razor:210-240` solves precisely this problem with `VoidQuote()`, and carries a
comment explaining why. The pattern exists in the repo; it was not applied here.

**Fix:** void `_reviewArmed` on any change to quantity, price, side, order type, stop or target —
or disable the inputs while armed. Announce the voiding ("Review cleared, ticket changed").

Two adjacent defects on the same path, from separate audits:

- **`TradingDashboardModal.razor:301-315`** — arming the review swaps Submit for Confirm/Cancel
  with `StateHasChanged()` and no focus move. **Focus falls to `<body>`** on a 2,093-line dialog,
  immediately after arming a real-money order.
- **`TradingDashboardModal.razor:319`** — the order-placement result is a bare
  `<div class='status-msg'>` with **no live region**. The OCO sibling 43 lines below (`:362`)
  *has* `role="status"`, which makes this an omission rather than a policy. The compensating
  speech uses `FeedbackType.StateChange` → the `Manual` channel → **silenced by F2**
  (`FeedbackRouters.cs:176-181`). Under F2 mute: failures announce, successes and the armed
  confirmation gate do not.

  > **CORRECTION, 2026-09-01 second pass. The prescribed fix was wrong and was NOT applied.**
  > Adding `role="status"` here would have made this the THIRD live region carrying the same
  > string: `MainLayout.razor:157-160` is the assertive double buffer every spoken message is
  > written into, and `StatusBar.razor:8` is a polite region fed from *every*
  > `FeedbackRequestEvent` — which this same audit records at its own "ungoverned second live
  > region" finding. The `@if` wrapper also creates region and content in one DOM mutation, the
  > pattern this audit lists as missed by Orca and Firefox+NVDA. The OCO sibling at `:362` is a
  > second instance of that defect, not a model to copy.
  >
  > The real defect is the **channel asymmetry**, and that is what was fixed: `Error` →
  > `Critical` (never muted) versus `Info`/`StateChange` → `Manual` (F2-muted), so with speech
  > off the terminal spoke every rejection and no confirmation — and **the arm review itself was
  > silent**, which means F2 switched off the readback for a real-money order while leaving the
  > order sendable. `FeedbackRequestEvent` gained an optional `Channel`; all eight speaking arms
  > of `OnFeedbackRequest` honour it with their defaults unchanged; the review and the placement
  > outcome now ride `SpeechChannel.OrderEvent`, the tier every *asynchronous* order outcome
  > already used. Sabotaging the override reddens `FeedbackTypeCoverageTests`.

---

## 2. The systemic finding: ten gates that assert the wrong thing

Every item here is a test that is green, whose stated rule is correct, and which cannot see the
population that violates it. This is the repo's own recorded pattern — *"read what a gate ASSERTS,
never its summary"* — at ten new sites.

| # | Gate | States | Actually asserts | Consequence |
|---|---|---|---|---|
| 1 | `keyboard.js:107` vs `ModalContractScanTests.cs:56-62` | dialogs are trapped | `[role="dialog"]` only | The C# scanner was widened to `{dialog, alertdialog}`; **the JS selector was not.** Tab walks out of the destructive "strip your indicators and drawings" prompt. |
| 2 | `DismissControlNameScanTests.cs:59-61` | an aria-label contains the visible text (2.5.3) | regex `<button\b…` | `<ToolbarIconButton />` is a **component tag**. All ~33 call sites invisible to all four assertions in the file. |
| 3 | `DismissControlNameScanTests.cs:157-162` | same | `.Where(!c.VisibleText.Contains('@'))` | Filters out every dynamic label. `@(x ? "Hide" : "Show")` is dynamic **and** generic — the exact case the rule exists for. |
| 4 | `ToolbarControlSurfaceTests.cs:143` | toolbar buttons are labelled | `Contains("AriaLabel=")` | **Presence, not containment.** Each of gates 2–4 looks at one of the two constructs; none looks at both. |
| 5 | `ModalBrowserContractTests.cs:121` | Tab never escapes an open dialog | presses only `Tab` | **Never presses Shift+Tab** — the one keystroke that escapes every dialog (§3.1). |
| 6 | `ModalBrowserContractTests.cs:132-137` | focus stays in the top dialog | **reimplements production's selection expression** | Computes "inside" against the same wrong dialog, so a stacked-modal trap failure passes by construction. |
| 7 | `TerminalPage.cs:405` `TopDialogAriaSnapshotAsync` | exists so the suite does not mirror the logic it guards (its own comment) | **never called by any test** | Every sweep uses the hand-rolled accname re-implementation at `:468-514` — precisely what the comment says was avoided. |
| 8 | `A3SurveyProbe.cs:173` | page-wide unnamed-control sweep | `Assert.Equal(routes.Count, report.Count)` | **Nothing asserts** that the ~40 toolbar buttons, tabs, indicator bar or status bar have names. |
| 9 | `ModalEscapeCloseTests.cs:7` | "pins the behaviour for each ModalBase modal" | 2 of 25 dialogs | Fleet-wide Escape coverage is a substring check plus a browser suite that cannot reach 8 dialogs. |
| 10 | `ThemeCoverageTests.cs:57` | theme colour separation | luminance **deltas** vs hand-picked thresholds | Self-disclaimed as *"not a full WCAG ratio"* — **and that honesty is what made this audit tractable.** Listed for completeness, not as a criticism. |

**Recommended response.** Six of these ten close with one change each. Two structural ones are
worth doing first because they close whole classes:

- **Assert against the browser's accessibility tree** (`TopDialogAriaSnapshotAsync`) instead of the
  repo's model of how a name is computed. Keep the hand-rolled walker only to locate candidates.
- **Extract one shared "is this a dialog?" helper** used by the C# scanners *and* exported to the
  JS trap, so the two cannot drift again. `ModalContractScanTests.cs:72` accepts both quote styles
  while `RazorMarkupHazardTests.cs:78` accepts only double quotes — they already disagree.

---

## 3. Critical and high-severity findings

### 3.1 Modal focus containment — four defects, one file

`wwwroot/js/keyboard.js` is 20 lines from correct. Four independent audits converged here.

**(a) Shift+Tab escapes every one of the 25 dialogs.** `keyboard.js:119-133` only rehomes focus
when `active === first`. The opening focus target is the `<h2 tabindex="-1">`, which the focusable
selector at `:114` **deliberately excludes**. On open, `active` is that heading: not `first`, not
`last`, and inside the modal — so no branch fires, `preventDefault` is never called, and native
backward navigation walks out of the dialog. Because the dialog declares `aria-modal="true"`, the
screen reader has restricted its buffer to the dialog, so the user is now standing on a background
control the AT will not describe. On the Trading Dashboard the nearest one is the toolbar's **Load**
button, which reloads the chart out from under an order form.

**(b) The trap is inert for `alertdialog` and `menu` while the counter says a modal is open.**
`keyboard.js:107` matches `[role="dialog"]` exactly. `Toolbar.razor:419` (`alertdialog`) and both
context menus increment `_openModalCount`, then hit `dialogs.length === 0 → return`. There is no
backdrop either, so `aria-modal="true"` overclaims. The comment at `Toolbar.razor:409-416` says
this was fixed.

**(c) The trap picks the last dialog in DOM order; the dispatcher picks by open order.**
`CommandDispatcher.cs:64` keeps a real `Stack<string?>`; `keyboard.js:109` takes
`dialogs[dialogs.length-1]`. All modals render in a fixed order at `MainLayout.razor:102-149`, so
DOM order is a constant and cannot track open order. Open Settings (F12) then Help (F1): Escape
correctly closes Help, but Tab is trapped in **Settings**, and `:128-132` actively yanks focus out
of Help. Reverse the open order and it works — so it presents as intermittent.

**(d) Closing a stacked modal leaves focus on `<body>`.** `CommandDispatcher.cs:146` restores focus
only `if (_openModalCount == 0)`. `SettingsModal.razor:1621` opens the Theme Editor *over* Settings.
No test anywhere opens two dialogs.

(c) and (d) share one root cause — no single ordered modal stack that both the JS trap and
`CommandDispatcher` read — and should be fixed together.

### 3.2 Keyboard scrolling is dead application-wide

**`keyboard.js:139-215`** · **Serious** · WCAG 2.1.1 Keyboard (A)

Arrows, Home, End, PageUp, PageDown and Space are `preventDefault`ed for anything that is not
`INPUT`/`TEXTAREA`/`SELECT`. `CommandDispatcher.cs:190` then *drops* the resulting command because a
modal is open. Both halves are individually reasonable; together they delete scrolling.

`HelpModal.razor` has exactly two focusable elements — the `h2` at `:22` and Close at `:420` — with
**~400 lines of guide and ten shortcut tables between them**, inside a container with
`overflow-y: auto`. **The keyboard reference cannot be read by keyboard.** Same for Settings,
Trading Dashboard, Strategy and the Respect Report.

**Fix:** the trap should not claim scroll keys while a dialog owns the keyboard — the dispatcher
suppresses the corresponding chart commands anyway, so `preventDefault()` here buys nothing.

### 3.3 A single Escape keypress can permanently remove Escape-to-close from every dialog

**`SettingsModal.razor:821`, `keyboard.js:744-751`, `ShortcutManager.cs:222-224`** · **Serious**

The rebinding UI promises "Escape cancels". `keyboard.js:744-751` filters only *modifier* keys, so
Escape closes Settings (window capture fires first) **and** rebinds the command to Escape.
`ShortcutManager.cs:222-224` then removes `CancelDrawing`'s Escape binding — the only route to
`CloseModal` (`CommandDispatcher.cs:190-192`) — **and persists it to disk.**

One keypress, no confirmation, permanent, and the on-screen text says it is safe.

### 3.4 App-owned speech bypasses the accessibility tree in most shipping configurations

**`BlazorSpeechManager.cs:78-106`, `WebHostSpeechManager.cs:126-127`** · **Critical** ·
WCAG 4.1.3 Status Messages (AA)

```csharp
if (_isNvdaAvailable == true) { … NvdaNative.SpeakText(text); return; }   // exits before the live region
if (!LiveRegionEnabled) return;
```

```csharp
internal static bool ShouldEnableLiveRegion(SpeechBackend backend, SpeechOutputMode mode)
    => backend == SpeechBackend.BrowserTts && mode != SpeechOutputMode.BrowserVoice;
```

Configurations where **nothing** reaches the accessibility tree: the spd-say backend, NVDA-direct
on the desktop head, and BrowserVoice mode. 4.1.3 requires status messages to be programmatically
determinable so assistive tech can present them without focus. Routing them through an out-of-band
TTS process satisfies *speech* and nothing else.

**A deaf-blind trader on a braille display, or anyone reading in braille with speech off, receives
no order fills, no stop hits, no rejections.** On the desktop NVDA head that is the entire
announcement system.

This is the finding that most directly contradicts the product's premise. Note the honest
counterweight: the single-sink policy this implements is a **correct** answer to a real problem
(diagnosed live against Chrome + Orca on 2026-07-23, recorded in the source). The defect is the
direction — the live region should be the source of truth and the app's TTS the thing suppressed,
not the reverse.

### 3.5 A live P&L sentence floods the announcement queue every two seconds

**`TradingDashboardModal.razor:403` (region) + `:1065` (timer)** · **Critical**

A ~15-word portfolio sentence — *"Portfolio 12,345.67 USDT across 5 assets. Largest BTC, 62.3
percent."*, roughly six seconds of speech — sits in `role="status"` and is recomputed every two
seconds from live prices. On any crypto pair the text changes on essentially every refresh.

It makes the dialog unusable and, worse, **buries the order-fill and rejection announcements the
whole app exists to deliver.**

The same file explicitly refuses this 210 lines later:
`:616` — *"No aria-live here: the book refreshes every 2s and a live region would re-announce the
spread endlessly."*

### 3.6 The strategy heartbeat cannot be silenced by any control the user has

**`SetupSonifier.cs:25, 104-114`** · **Critical** · WCAG 1.4.2 Audio Control (A)

`OnReconfirmed` speaks *"{Strategy} still confirmed, bar {N}."* on every bar close via
`ISpeechManager` directly, bypassing the router that owns the mute gate.

**Verified:** `ISpeechManager.IsSpeechEnabled` is declared at `BlazorSpeechManager.cs:17` with
`= true` and has **no production assignment anywhere**. The real F2 mute lives on a *different*
flag — `WorkspaceState.IsSpeechEnabled`, read at `FeedbackRouters.cs:178` and
`MainLayout.razor:268`. **Two different flags share one name in the most safety-critical path in
the app.**

So neither F2, nor Shift+F2, nor any setting silences it. On a 1-minute chart with three active
setups that is three unstoppable utterances a minute.

This breaches the contract the codebase states for itself at `FeedbackRouters.cs:11-13`:
*"the gate lives HERE, at the router, not at call sites — per-call-site IsSpeechEnabled checks are
exactly how the F2 bypasses crept in."*

The same bypass exists at `AIAnalystModal`, `WalletModal`, `WithdrawModal` and `SummaryExport`.

### 3.7 PropertiesModal renders 24 form controls with no accessible name at all

**`PropertiesModal.razor`** — 24 sites · **Critical** · WCAG 3.3.2 Labels or Instructions (A)

The idiom is an orphan `<label>` with neither `for` nor a wrapped control:

```razor
229:  <label>Bullish Color</label>
230:  <input type="color" value="@comp.ColorHex" @onchange='…' />
```

An orphan `<label>` names nothing. Lines `361` and `411` have **no adjacent label text at all**.
The enclosing `<legend>Component: @comp.Name</legend>` does not rescue this — a legend supplies
group context, not part of a control's accessible name.

A blind trader configuring a series hears "colour edit", "slider", "spin button", "combo box".
**This is the sonification config — the file that decides what the chart sounds like.**

`RiskPlanEditor.razor:86-142` is the correct in-repo template and should be the model for the fix.

### 3.8 Four critical contrast failures, and the reason behind all of them

**The codebase contains no WCAG contrast function.** `grep` for `0.04045`, `1.055`, `12.92` across
every `.cs` returns nothing. The one luminance function — `ThemeCssBridge.cs:146` — omits gamma and
says so. Three different non-WCAG proxies are live: naive luminance (picks focus-ring and button
ink), luminance deltas vs hand-picked thresholds (`ThemeCoverageTests`), and **squared Euclidean
RGB distance** (`ThemeEditorModal.razor:260`).

Nothing in this app has ever been measured against 4.5:1 or 3:1. Hence **89 failing pairs** across
the 12 built-in themes.

| Finding | Location | Measured |
|---|---|---|
| Chart status header is `color:#fff` over `theme.Background` — the parent already set the correct `GetThemeTextHex()` and the child throws it away | `ChartArea.razor:135` | **1.00:1** on High Contrast Light, 1.03:1 on Paper |
| Crosshair is `rgba(255,255,255,0.45)` — the only visual indication of which bar the cursor is on. `theme.Crosshair` would give 11.45:1 | `ChartArea.razor:100-101` | **1.00:1** HCLight, 1.02:1 Paper, 2.94:1 SteelGray |
| Icon-button focus ring **is** the variant hue, on the **shipped default theme** | `app.css:680-710` | **1.52–2.89:1** — functionally no focus indicator |
| ~40 hard-coded hex literals across 25+ modals assume a dark dialog | 25+ files | `#fff` 1.00:1, `#4f4` 1.26:1, `#6d6` 1.62:1 on light themes; `#888`/`#777`/`#0078d4` fail on **every** theme |

**The theme editor waves through `#0000ff` on `#000000`** — Euclidean distance 65,025 against a
threshold of 12,000; actual contrast **2.44:1**. Six demonstrated false negatives are in the full
report. Its docstring claims *"Every built-in theme is checked against these same rules by the test
suite, so a preset is always safe."* That claim is false as measured.

Two more that deserve naming:

- **`ColorVisionSafe`'s pair is fixed** at `#409cff`/`#ffa020` → **1.97–2.04:1** on light charts.
  The colour-blindness accommodation is itself inaccessible on the themes a low-vision user is most
  likely to pick.
- **Zero `forced-colors` support**, and the chart is a base64 PNG (`ChartArea.razor:84`) that
  Windows High Contrast cannot touch. Highest-leverage fix: switch to the existing
  `HighContrastDark`/`Light` theme on `forced-colors: active`.

### 3.9 The chart — the product's centre — has no visible focus indicator

**`ChartArea.razor:67`** · **Serious** · WCAG 2.4.7 (A) · found independently by three audits

An inline `outline: none` beats the app's own correct global rule at `app.css:255-264`, which has
no `!important`. No CSS anywhere targets `#chart-interact-zone`, and `OnChartFocused` triggers no
visual change.

Chart focus is the gate for every single-letter command (`keyboard.js:209-211`), so *"my chart keys
stopped working"* has no visible answer. Compounded by `CommandDispatcher.cs:146-147`, which sends
focus **to this element** on every modal close.

The standard is understood in this very file — the deliberately-hidden bar slider *does* get a
ring, with a comment citing 2.4.7 (`ChartArea.razor:179-199`). The chart itself was missed.

### 3.10 Playback is a speech-free island, justified by a comment that is factually untrue

**`AccessibilityFeedbackCoordinator.cs:289-295`** · **High** · WCAG 1.1.1 (A), 1.2.1 (A)

```csharp
// The PlaybackOrchestrator handles its own sonification/speech.
if (state.IsPlaying) { _previousState = state; return; }
```

`PlaybackOrchestrator` does not handle speech. Its constructor takes `IAudioSequencer`,
`IAudioDriver` and `ILogger` — **no speech router, no event bus, no `Speak` call in the 97-line
file.**

For the entire duration of a playback the chart is conveyed by **tone alone with no text
equivalent** — up to ~8 minutes at default speed against a 5,000-bar cache, hours at 0.1×. And
because the speed announcement at `:336-339` sits *below* the gate, pressing Shift+= during
playback — the only moment the control is useful — changes speed silently.

Compounding it: `PlaybackFinished` (`SonificationManager.cs:32`) has **zero production
subscribers**. When playback ends the tones simply stop, indistinguishable from a crash, a
disconnect, or an empty dataset.

**A stale comment naming a class that does not do the thing is how this survived.** Fix the comment
as part of the fix.

### 3.11 AutoNarrationService emits up to nine `Speak` calls per scan; on the web head eight are silently discarded

**`AutoNarrationService.cs:129-133`, emits at `:257, 286, 294, 438, 470, 484, 504, 509, 561`** ·
**High**

This is a **known, documented, already-fixed-elsewhere defect class.**
`NavigationFeedbackManager.cs:281-292` describes it exactly:

> *"…on the web head speech is delivered by writing into an ARIA live region; Blazor batches an
> entire event handler into one render; so the region is written three times but only the final
> value ever reaches the DOM… The earlier phrases were never dropped by a mute or a filter — they
> were overwritten before anything could read them."*

`NavigationFeedbackManager` was fixed by composing one utterance. `AutoNarrationService` was not.
A bar tripping three conditions announces only the last — and the most important signal can be the
one discarded. On the desktop head the failure inverts: all nine queue, uninterruptibly.

### 3.12 An existing drawing's anchors can only be moved with a 10-pixel mouse drag

**`DrawingInteractionManager.cs:360, 623, 89`** · **High** · WCAG 2.1.1 (A), 2.5.7 (AA)

`TryBeginEditDrag` has exactly one call site: a mouse-down. No `SystemCommand`, event, or other
caller writes `AnchorDate*`/`AnchorPrice*` on an existing drawing.

**This is precisely the population the product exists for.** A blind user can *create* a trendline
beautifully — fifteen tools, sequential anchoring with no drag, spoken prompts, Escape to cancel,
which satisfies 2.5.7 for creation outright — **but cannot nudge it one bar left.** They must
delete and redraw.

The machinery already exists in `PlaceAnchorAtCursor` (`:1158`).

### 3.13 Error state is not conveyed anywhere in the product

**Verified sweeps across both projects:**

```
aria-invalid   → 0 occurrences
aria-required  → 0 occurrences
required=      → 0 occurrences
aria-disabled  → 0 occurrences
```

Every auth model carries `[Required]`, but `asp-for` emits `data-val-required` and there is no
unobtrusive-validation script on any of the nine account pages — so the requirement reaches the
server only. There is no asterisk convention and no "fields marked … are required" note either, so
the information is conveyed by **nothing at all**, visually or programmatically.

When a field is rejected its accessible state stays "valid". A screen-reader user moving back
through a failed form hears exactly what they heard before the error.

Compounding on the auth pages: `role="alert"` on **parse-time** content does not fire in NVDA or
VoiceOver, and unconditional `autofocus` (`Login.cshtml:42`) drops the user into Email regardless
of which field failed. Sign-in fails and the user is given no signal whatsoever.

These four sweeps are cheap scan-guard candidates that would have caught this the day it was
introduced.

---

## 4. Serious findings by domain

### Structure and semantics

- **`<nav role="toolbar">`** (`Toolbar.razor:31`) — the explicit role overrides the element, so the
  app has **zero navigation landmarks**. Toolbar, tab bar, indicator bar, status bar and touch bar
  all sit outside every landmark; NVDA's `D` key gives three stops. The role also promises
  roving-tabindex arrow navigation that the comment three lines above says was *deliberately* not
  implemented. **Deleting the role recovers the landmark, stops promising a keyboard model that
  does not exist, and changes zero behaviour** — the single highest value-to-effort fix in this
  report. Same shape at `IndicatorBar.razor:7` and two more containers.
- **Ten controls fail WCAG 2.5.3 Label in Name** — visible `Zones` announced as "Level Respect
  Report" (**zero word overlap**), `Trade`→"Trading Dashboard", `Objects`→"Object Tree",
  `Pan left`→"Pan chart left" ×4, and both `IndicatorBar` toggles, which show the **state**
  ("Visible") and name the **action** ("Hide SMA 20"). Fix by *extending the visible text*, per the
  rule that closed the original 32 on 2026-08-29.
- **`ObjectTreeModal` exposes no `aria-expanded` anywhere.** `role="treeitem"` on `<summary>`
  replaces the native disclosure mapping, and `treeKeyboard.js:95-97` drives expansion off
  `details.open` — so the *code* knows the state and the *user* never hears it. Compounding:
  treeitems after the first carry `tabindex="-1"` with no roving handler, so their rich labels
  (`"…visible, muted"` — the whole point of the panel) **are never announced**; and the footer at
  `:136` tells users "Use Tab to navigate", which the declared role contradicts.
- **`HelpModal.razor`** — 471 lines, 18 sections, **one heading**. All section titles are
  `<summary style="font-weight:700">`. For a product whose navigation model is "heading structure
  IS the navigation", this is the screen where that matters most.

### Tables

Census: **33 `<table>` elements. 29 have no `scope="row"`. 17 have no accessible name. 17 `<th>`
have no `scope`.** Walking the P&L column gives numbers with no position attached; the Help dialog
offers ten identical "table" entries in NVDA's table list.

Two of these are **data-correctness bugs, not accessibility bugs**:

- **`OrderBookModal.razor:361` still uses `:G4`** — the exact scientific-notation defect
  `QuantityFormatter.cs`'s own doc comment records as fixed. The spread reads aloud as
  *"one point two three E minus zero five"*. `TradingDashboardModal.razor:2085` computes the same
  value correctly, so the two disagree.
- **`"0.####"` collapses any sub-penny price to `"0"`** (`WatchlistModal.razor:1043`,
  `LevelReportModal.razor:225`). A SHIB screener shows a Close column of zeros, indistinguishable
  from a worthless market.

Credit: **zero div-soup grids.** Every tabular surface is a real `<table>`, and
`WatchlistModal.razor:5-8` records that the choice was deliberate.

### Live regions and announcements

- **Every `FeedbackRequestEvent` is announced twice.** `StatusBar.razor:8` is a second, ungoverned
  `role="status"` region carrying the same events as the speech pipeline. Worse: on the Orca and
  spd-say backends `ShouldEnableLiveRegion` empties the main buffers *specifically to stop double
  speech*, and the StatusBar is not covered by that policy — **so the double comes back through the
  side door.** Same shape in four modals.
- **`Interrupt` is discarded for Info, StateChange, Error and Boundary**
  (`AccessibilityFeedbackCoordinator.cs:561, 590, 604, 616`). Five publishers explicitly pass
  `Interrupt: false` — including `OrderBookModal.razor:334`, whose comment says *"so it never cuts
  off navigation"* — and are overridden. The `Alert` and selection arms *do* honour it, which makes
  this a defect rather than a policy.
- **Both live buffers are assertive and `interrupt` never reaches the DOM**
  (`MainLayout.razor:157-162`, `BlazorSpeechManager.cs:102-110`). `OnSpeak` is `Action<string>`, so
  the politeness decision **cannot** reach `MainLayout` even in principle. For screen-reader mode —
  the primary mode — the carefully-built "a Manual message must not clip an in-flight OrderEvent"
  guarantee does not exist.
- **The order book announces nothing when its fetch fails or returns empty**
  (`OrderBookModal.razor:45-50, 250-262`). Silence is indistinguishable from an empty market or a
  dead keystroke. This is the UI half of the status-blind provider reads already fixed service-side.
- **`aria-busy="true"` is hardcoded on a conditionally-rendered loading region**
  (`AIAnalystModal.razor:25`), so a 30-second AI call is silent; `:108` then pushes the entire
  multi-paragraph analysis as **one assertive interrupting utterance**.
- **~30 live regions are created in the same DOM mutation as their content.** Firefox+NVDA and
  Safari+VoiceOver frequently miss `role="status"` inserted this way — and Firefox/Chromium + Orca
  is this app's primary Linux target. (UNVERIFIED per instance; the pattern is definitively
  fragile.)

### Cognitive and WCAG 2.2

- **Three destructive deletes with no confirm and no undo** — workspace
  (`LoadWorkspaceModal.razor:38-44`, straight to `Library.DeleteProfile`), alert
  (`AlertsModal.razor:41`), script (`CustomScriptsModal.razor:58`). The script delete **announces
  nothing at all** and silently reselects `_scripts[0]`. `ApiKeysModal.razor:121-142` already
  implements the correct pattern.
- **All 25 modals close on backdrop click**, including `WithdrawModal` — a stray click wipes a
  fetched quote and a typed `WITHDRAW`.
- **36 sites put a raw `ex.Message` in front of the user; two speak it aloud.**
- **The dashboard repaints every 2 seconds with no pause**, and `RefreshBookAsync()` sits *above*
  the `_editing` guard.
- **Four `RiskPlanEditor` dropdowns render raw enum names** — "AtrMultiple", "BelowKijun"; the
  rebind table renders "NavPatternPrev", "QuickArmRisk1".
- **No help or contact route on any auth page** — a 2FA user without recovery codes has a dead end.

### Text quality

- **17 indicator components speak a code token.** `IndicatorModelFactory.cs:298` is
  `DisplayName = meta.DisplayName ?? meta.Name`, fed into `{name}` at `SpeechFormatter.cs:897`. The
  chart says *"ChandelierExit. Line. 143.20."* on every arrow-key press. In all 17 cases the
  English phrase is already written two lines up as the parent's `Name` — `Name = "Williams %R"`
  sits directly above `new() { Name = "WilliamsR" }`.
- **`1m` and `1M` are the same name to a screen reader** — minute against month.
  `Toolbar.razor:305` interpolates the raw token and nine providers ship both in one array
  (`BinanceProvider.cs:500`), so the quick-pick row renders two audibly identical buttons. **No
  timeframe-to-words helper exists anywhere.**
- **`Security.cshtml:63,74`** — two fields named "Current password"; one of them turns 2FA off.
- **The CSV export corrupts itself under any comma-decimal locale.**
  `DataExportService.cs:39-51` uses `CurrentCulture` for numerics, producing `64,900` inside a
  comma-separated file under `de-DE`/`fr-FR`/`es-ES`/`pt-BR`. The chart's only durable text
  alternative silently becomes unparseable. The date format and `EscapeCsv` in the same file are
  careful — only the numerics were missed.

### Reflow and zoom

**`app.css:407-424`** — `.modal-content` has `min-width: 400px` and `box-sizing` is applied only to
fieldsets, giving a **466 px floor**. WCAG 1.4.10 requires no horizontal scrolling at 320 px. There
are no width-based media queries in the application's CSS; the only two in the repo target
`.sidebar`/`.top-row`, **stock Blazor template scaffolding this app's layout does not use**.

---

## 5. What is already strong — and must not be regressed

Stated as specifically as the defects, because several of these are load-bearing and a
well-intentioned "accessibility cleanup" could destroy them.

**Testing architecture**
- `ModalCatalog.cs:195` is an **enrollment** guard — closed, not sampled — and it is pinned against
  its own regex going vacuous at `:225`.
- `ModalFocusTargetContractTests` asserts *which* element holds focus against an externally
  declared table with a stated reason per dialog, closed in both directions, with an anti-collapse
  floor requiring at least 3 non-heading targets. **This is the correct shape for a focus contract
  and I have nothing to add to it.**
- Source scanners are pinned against their own machinery: the comment stripper is unit-tested, the
  tag and expression walkers are pinned, three population floors, and one branch is *openly
  self-declared vacuous*.
- **Both exemption lists were DELETED rather than grown**, each with the fix shape recorded.
- `HarnessSmokeTests.cs:20-25` **fails** when Chromium is absent rather than skipping.
- `ModalRoute.cs:116-117`: *"A sweep that lists 25 modals and exercises 21 is a sweep that reports
  84% as 100%."*

**Announcement architecture** — the best I have audited
- `ShouldEnableLiveRegion` — exactly one sink may vocalise a phrase, diagnosed live against
  Chrome + Orca and recorded in the source.
- The `MainLayout` **double buffer** defeats screen readers' identical-string suppression.
- `FeedbackRouters.MayInterrupt` — duration-modelled priority at 15 chars/sec with a 4-second
  ceiling, so a navigation read cannot clip an order fill. The 80-line comment explaining why is
  exemplary.
- Mute tiers gated **at the router, not at call sites**, with a comment naming the exact regression
  that motivated it.
- **Three places that refuse a live region** because it would flood, each with its reasoning.
- Earcon **before** speech on every failure, so the cue lands even if the AT is mid-phrase.
- `AccessibilityFeedbackCoordinator.cs:697-704` — an unhandled `FeedbackType` **logs and speaks**
  rather than dropping the message.

**Product decisions**
- **WCAG 3.3.8 Accessible Authentication passes outright**: zero JavaScript on all nine account
  pages so nothing blocks paste; one OTP input, not split boxes; **a honeypot instead of a CAPTCHA**;
  setup key as the primary 2FA path with QR as convenience; recovery codes in a readonly
  `<textarea>` with a comment explaining why.
- **3.3.4 is genuinely met on both money paths.** `WithdrawModal` requires a venue-saved
  destination (no free-text address), a quote, a spoken readback, and a typed phrase.
- `SpeechOutputPrompt.razor` — an accessible first-visit chooser for a problem browsers make
  technically unsolvable.
- **Keyboard drawing creation is complete** — 15 tools, sequential anchoring with no drag, spoken
  prompts, Escape cancel. This satisfies 2.5.7 for creation outright.
- **Real hearing-safety engineering** — a brickwall limiter using gain-riding rather than
  waveshaping (so timbre, which carries data here, survives), plus a Nyquist/volume/pan clamp added
  after a recorded incident.
- `ShortcutHelpParityTests.cs:15` — writing the test found **37 of 124 default bindings appeared
  nowhere in the in-app help**. Both directions now asserted.
- **A genuine sonification legend** (`HelpModal.razor:50-79`) mapping pitch, glide, timbre, panning
  and earcons to meaning, with per-bin frequencies.
- `EnableAuthenticator.cshtml:72` carries **the best alt text in the repo** — it describes the QR
  code's *purpose* and states its equivalence to the adjacent setup key, which is the one thing a
  blind user needs to know.
- **The `IconSprite` `<use>` pattern is correct and should not be changed.** The sprite root is
  `aria-hidden` + `focusable="false"`; **no `<symbol>` carries a `<title>`**, which is right — a
  name there would double-announce through every `<use>`.
- `JournalModal` is deliberately a `<textarea>`, **not** `role="log"` — a log region would announce
  the app's own speech back to the user. Correct, and worth a comment so a future cleanup does not
  "fix" it.
- **`prefers-reduced-motion` is handled correctly** in both stylesheets and scoped in
  `VisualEarconOverlay`; photosensitivity is safe **by construction** rather than patched.
- **Zero div-soup tables. Zero positive `tabindex`. Zero template-variable leakage into accessible
  names. Zero "click here". Zero typos across 713 unique words. All 12 `<img>` elements correct.
  `lang="en"` on all 11 documents. One `<h1>`, clip-hidden not `display:none`. `<PageTitle>` updates
  per chart and per tab.**
- A **working shortcut-rebinding UI** that genuinely satisfies WCAG 2.1.4 (the Escape defect in
  §3.3 is a bug in it, not an absence of it).

---

## 6. There is no accessibility documentation

`docs/` holds 76 files. **Not one is an accessibility document.** No conformance statement, no
VPAT, no accessibility test plan, no declared WCAG target, no `ACCESSIBILITY.md`.

For a product whose entire premise is accessibility, this is the gap most visible from outside.
The conventions that *do* exist live only in `ModalBase.cs` XML doc-comments and inline Razor
comments — which is exactly how the `role="toolbar"` and `role="menu"` promises drifted from what
the code implements.

**Recommended:** an `ACCESSIBILITY.md` declaring the target (WCAG 2.2 AA, with the AAA criteria
this product deliberately exceeds), the announcement contract, the modal contract, and the known
exceptions with their reasons. This repo has already established that **an exemption with a written
reason is the only kind worth trusting**; the same applies to a conformance claim.

---

## 7. Recommended order

> **STATUS 2026-09-01, after two fix passes.** Items 1-5 and 10's second clause are **DONE**;
> item 8 is **recorded, not fixed**. Item 1 grew a fourth route (armed state surviving
> close/reopen) that this list did not contain. Item 10's first clause — the ordered modal stack —
> is still open and is now the top item in `docs/TODO.md`, alongside 11 and 9 in that order.

**Before the next release**

1. **F-01** — void `_reviewArmed` on any ticket edit. *Financial correctness, not accessibility.*
   **DONE** — and the prescription here was incomplete: per-control voiding alone does not cover
   the side buttons, `SizeFromRisk`, or Symbol/Provider. See the CLOSED note on the finding itself.
2. **`keyboard.js`** — three small edits to one file close §3.1(a), (b) and §3.2: widen the selector
   to `[role="dialog"],[role="alertdialog"]`, trap on containment rather than identity with
   `first`/`last`, and release the scroll keys while a dialog is open. **These close the two worst
   outcomes: escaping an unanswered destructive confirmation, and being unable to read a dialog.**
3. **`ChartArea.razor:67`** — delete the inline `outline: none`. One line.
4. **`Toolbar.razor:31`** — delete `role="toolbar"`. One attribute, recovers the navigation
   landmark, zero runtime change.
5. **`ChartArea.razor:135` and `:100-101`** — inherit the theme text colour; use `theme.Crosshair`.
   Two changes, closes two of the four Critical contrast failures.
6. **`SettingsModal.razor` / `keyboard.js:744`** — stop Escape being captured as a binding.
7. **`SetupSonifier`** — inject the router, not the manager. Then rename one of the two
   `IsSpeechEnabled` flags.
8. **`TradingDashboardModal.razor:403`** — drop `role="status"` from the portfolio summary.
   **STILL OPEN.** It is recomputed by the 2-second refresh timer, so it re-announces on every
   tick and competes with the order review for the same live region.

**Next**

9. Implement a real WCAG ratio function once; use it in the theme editor as a **blocking** check
   and replace the luminance-delta assertions with it.
10. Focus management: one ordered modal stack read by both the JS trap and `CommandDispatcher`
    (closes §3.1(c) and (d) together) — **still open**; focus the Confirm button when the live
    review arms — **DONE**, and it must happen BEFORE the review is spoken, because the focus
    announcement interrupts the live region and speak-then-focus clips the review at word one.
11. `PropertiesModal` — 24 labels, following the `RiskPlanEditor` template.
12. Playback: speech during playback, start/stop/complete announcements, subscribe
    `PlaybackFinished`, **and fix the false comment.**
13. Compose `AutoNarrationService`'s nine emits into one utterance, mirroring
    `NavigationFeedbackManager`.
14. Keyboard anchor editing (`SelectAnchor` / `NudgeAnchor`).
15. The `:G4` and `"0.####"` formatter defects, and the CSV culture fix.

**Guard tests worth adding** — in this repo's existing idiom, each proven red by reintroducing the
defect before being trusted:

- Assert against `TopDialogAriaSnapshotAsync` (the browser's accessibility tree) rather than the
  hand-rolled accname walker.
- `ShiftTab_never_escapes_an_open_dialog` — the missing half of the existing Tab guard.
- A stacked-modal test. **No test anywhere currently opens two dialogs.**
- One shared "is this a dialog?" helper, used by every C# scanner and exported to the JS trap.
- Scan: no focusable element carries an inline `outline: none`.
- Scan: no component both renders a field inside `role="status"`/`aria-live` **and** publishes that
  same field to the speech pipeline.
- Scan: `ISpeechManager` may be injected only by `MainLayout` and `SpeechOutputPrompt`; everything
  else uses `ISpeechFeedbackRouter`.
- Unit: `FeedbackRequestEvent(…, Interrupt: false)` must reach the router as `Speak(…, false, …)`.
  **Reddens today.**
- Sweeps for `aria-invalid` / `aria-required` / `aria-disabled` presence on the surfaces that need
  them.
- A `de-DE` culture test on `ExportToCsvAsync` asserting the output round-trips.
- Extend every scanner's file set to `Pages/**/*.cshtml` and `auth.css`, which **zero guards
  currently walk.**

---

## 8. Coverage gaps in the existing suite

| Surface | Status |
|---|---|
| `WebHost/Pages/Account/*.cshtml` (9 pages) | **Zero guards.** `grep "cshtml"` across both test projects returns nothing. |
| `WebHost/Components/*.razor` | Zero source guards. |
| `WebHost/wwwroot/auth.css` | Both CSS guards hardcode only the two `app.css` copies. |
| All `.js`, including `keyboard.js` | **CORRECTED 2026-09-01, after the fix pass.** This row originally read "no accessibility guard walks any JavaScript". That was **wrong**. `tools/jstests/keyboard-tests.mjs` is a zero-dependency vm-sandbox suite that loads `keyboard.js`, fires synthetic keydowns and asserts on `preventDefault` — which, as its own header says, no C# test can observe — and it runs in CI. The real gap was narrower: it covered the Space-activation trap and did not touch the Tab trap. Nine tests added there; it is now 22, and the Shift+Tab defect is demonstrated by reverting the fix. |
| `Layout/MainLayout.razor`, `Pages/Home.razor` | Excluded by `TopDirectoryOnly` and absent from `ModalCatalog.BareComponents` — yet they hold both ARIA live regions and `#main-heading`. |
| `lang`, skip link, `alt`, page title, landmark roles, heading hierarchy | No guard asserts any of these anywhere. |
| Stacked modals | No test opens two dialogs. |
| WCAG contrast ratios | Never computed. |
| axe-core / pa11y / Lighthouse | None. `BrowserTests.csproj` references only Playwright, MVC.Testing and xunit. |

---

## Appendix — findings by domain

| Domain | Findings | Critical/High | Serious | Moderate | Minor |
|---|---|---|---|---|---|
| Architecture / cross-cutting | 36 | 3 | 10 | 11 | 12 |
| Contrast and visual | 35 | 4 | 14 | 13 | 4 |
| ARIA | 30 | 0 | 8 | 13 | 9 |
| Data visualization / chart | 27 | 7 | — | 9 | 11 |
| Keyboard and focus | 26 | 0 | 6 | 12 | 8 |
| Tables and data | 22 | 0 | 6 | 9 | 7 |
| Forms and inputs | 19 | 1 | 7 | 6 | 5 |
| Live regions | 19 | 3 | 5 | 8 | 3 |
| Modals and dialogs | 18 | 0 | 5 | 6 | 7 |
| Cognitive / WCAG 2.2 | 15 | 3 | — | 6 | 6 |
| Text quality | 14 | 0 | 7 | 4 | 3 |
| Structure / headings / alt | 11 | 0 | 1 | 4 | 6 |
| **Total (raw)** | **272** | **21** | **69** | **101** | **81** |

Raw totals include cross-domain duplicates. Twenty findings were reported by two or more
independent audits; the four found by **four** separate audits are the `[role="dialog"]` Tab trap,
the `ToolbarIconButton` Label-in-Name failures, `<nav role="toolbar">`, and the context menus'
unimplemented `role="menu"`. Convergence at that rate is itself a confidence signal, and those four
are the ones to fix first among the non-Critical items.
