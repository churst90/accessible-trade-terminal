# Background monitoring — the expansion, scoped

**Status: PHASES 0 AND 1 ARE BUILT (2026-09-06). Phases 2–3 are scope only.** Written 2026-09-06 from a
reading of the code as it stands at `4702a00f`. Every "today" statement below carries the file and line it was read from,
so a later reader can check whether it is still true rather than trusting the date.

**The goal, in Cody's words:** *"keep the terminal running, close the browser and still receive
notifications on linux, windows and mac heads, be them maui or webhost, for all terminal events
plus alerts as well. order fills, order closes, alerts, new bar notifications."*

---

## 1. Why the browser has to stay open today

It is not a missing feature. It is a **service-lifetime boundary**.

On the WebHost these are all `AddScoped` — which for Blazor Server means **per circuit**, i.e. per
open browser connection:

| Service | Registered | Lifetime |
|---|---|---|
| `IEventBus` | `ServiceCollectionExtensions.cs:67` | Scoped |
| `IWorkspaceStore` | `:74` | Scoped |
| `IDataService` | `:264` | Scoped |
| `IOrderExecutionService` (`GeneralOrderService`) | `:415` | Scoped |
| `DesktopNotificationService` | `:541` | Scoped |

Close the browser, the circuit is disposed, and every subscription in that list goes with it.
`DesktopNotificationService` — the thing that turns a fill or a bar close into a toast — has no
existence outside a circuit at all.

`LocalBackgroundMonitor` is the one component that survives, and it survives by being a
**parallel implementation** rather than a continuation: its own `AlertEvaluator`, its own OHLCV
fetch loop, its own delivery through `IDesktopAlertPresenter`, a 60-second poll. It also **pauses
entirely whenever a circuit is open** (`LocalBackgroundMonitor.cs`, `ActiveCircuits > 0`), because
it and the circuit would otherwise both speak through the same Orca and double every announcement.

### What that leaves

| | Browser open | Browser closed |
|---|---|---|
| Simple alerts (explicit symbol + provider) | in-session pipeline | ✅ `LocalBackgroundMonitor` |
| Condition-tree / current-chart / indicator alerts | ✅ | ❌ nothing (`BackgroundWatchability` says so out loud) |
| Order fills, stops, take-profits | ✅ | ❌ nothing |
| New bars | ✅ | ❌ nothing |
| Dead-feed escalation | ✅ | ✅ (monitor only) |

> **PARTLY CLOSED 2026-09-06 (Phase 1).** Row 1's "browser open" cell was worse than it looks:
> the in-session pipeline evaluates alerts for the symbol ON SCREEN only, and the monitor stood
> down entirely whenever a circuit was connected, so an alert on a symbol with no tab open was
> evaluated by **nobody** while the browser was open. The pause is now a per-symbol routing rule
> (`CircuitAlertCoverage`), so row 1 reads "in-session for what is on screen, headless for
> everything else". Rows 3 and 4 are unchanged and are Phases 2 and 3; the long-lived scope they
> need now exists.

## 2. The delivery matrix is the other half, and it is in worse shape

`NotifySendDesktopNotifier.IsAvailable` is `_presenter.CanNotify`, which is "`notify-send` is on
the PATH". `Program.cs:136` registers it for **`HostMode.Full` on every OS**, not just Linux — so
on Windows and macOS it resolves, reports unavailable, and the delivery panel silently hides its
switches.

| Head | Toast path | Headless speech |
|---|---|---|
| WebHost Linux | ✅ `notify-send` | ✅ Orca/gdbus → `spd-say` |
| **WebHost Windows** | ❌ none | ❌ none |
| **WebHost macOS** | ❌ none | ❌ none |
| MAUI Windows | ⚠️ `WindowsDesktopNotifier` — compiles (v2.8.0 `maui-windows` job), runtime unverified | n/a (in-process) |
| **MAUI Mac Catalyst** | ❌ `NullDesktopNotifier` | n/a |
| Hosted | Web Push (`HostedWebPushSender`) — works, separate path | n/a |

**So "notifications with the browser closed" is today a Linux-only feature even for the subset of
alerts it does cover.** That is the cheapest and largest gap in the whole plan.

> **CLOSED 2026-09-06 (Phase 0).** The per-OS decision moved to `WebHost/Services/
> DesktopDeliveryPlan.cs`, which takes the OS and the filesystem probe as parameters and is
> therefore asserted for all three desktops from this repo's Linux box. Rows 2 and 3 of the table
> above now read: **WebHost Windows** — PowerShell toast into the Action Center, SAPI speech;
> **WebHost macOS** — `terminal-notifier` or `osascript` into Notification Center, `say` speech.
> The root cause was never in the notification code: every probe went through
> `WebHostSpeechManager.FindOnPath`, whose first line returns null on anything that is not Linux.
> **What is proved is what gets spawned. Nothing on this row has run on a Mac or a Windows box** —
> see §6.

## 3. A correction to the framing, and the decision it produced

**On MAUI desktop there is no "close the browser".** Closing the window closes the app. The
analogue is *minimise*, and there the singletons keep running and `DesktopNotificationService`
(registered `AddSingleton` at `BlazorClient/ServiceCollectionExtensions.cs:532`) already works. So
the MAUI half of the ask is mostly **delivery paths plus runtime verification, not a monitor**.

**DECIDED (Cody, 2026-09-06): on the MAUI clients, closing the window minimises to tray rather
than quitting, and that behaviour is a checkbox on Settings → General — "Minimize to tray on
exit".** Consequences to carry into Phase 0:

- MAUI currently has **no tray at all**; the tray is WebHost-only (`WebHost/Services/Tray/`, with
  `LinuxTrayPlatform`, `WindowsTrayPlatform`, `MacTrayPlatform` behind `ITrayPlatform`).
  **CORRECTED 2026-09-06: this was wrong, and it is the one factual error in this document.** The
  MAUI *Windows* head has had a tray since before 2.4.0 —
  `BlazorClient/Platforms/Windows/TrayIconService.cs`, behind `#if TRAY_ICON`, with
  `EnableWindowsTrayIcon` defaulting to true. The grep that produced this bullet looked for the
  `ITrayPlatform` seam, and the MAUI applet does not use it. **Mac Catalyst genuinely has none**,
  which is why the Phase-0 checkbox ships on the Windows head only.
- The `ITrayPlatform` seam is the right thing to reuse, but it lives in the WebHost project and
  would have to move to Core (or an equivalent be written for MAUI's window model).
- The checkbox must **default off**. An app that does not close when you close it is a surprise,
  and for a screen-reader user a surprise with no announcement is worse than an extra keystroke.
  Turning it on should say what it now does; the tray icon should carry a "Quit" item so there is
  always a way out that does not need the window. **DONE 2026-09-06:** the applet reads
  `app.minimizeToTray` (`Core/Services/DesktopWindowSettings.MinimizeToTrayKey`, one constant with
  two readers a project apart) *at close time* rather than at startup, so the switch takes effect
  on the next close; "Exit" is renamed "Quit"; and the checkbox announces what the close button
  now means.
- Accessibility note for whoever builds it: a tray icon is a control with no visible label. It
  needs an accessible name and its menu needs to be keyboard-reachable — the WebHost tray already
  has this problem and has never been measured with a screen reader.

## 4. The phases

### Phase 0 — delivery paths (independently shippable, highest value per hour) — **DONE 2026-09-06**

Nothing here touches a lifetime, and it makes the feature that **already exists** work on two more
operating systems.

- `MacDesktopNotifier` — `osascript -e 'display notification "…" with title "…"'`, or
  `terminal-notifier` when present. Serves both the WebHost and MAUI Mac Catalyst heads.
- A Windows notifier for the **WebHost** head. The MAUI one uses the Windows App SDK's
  `AppNotificationManager`; check whether the WebHost can reference the same package before
  falling back to a PowerShell toast.
- Headless **speech** per OS, behind the existing `IDesktopAlertPresenter` seam: SAPI on Windows,
  `say` on macOS. Today `ProcessDesktopAlertPresenter` probes only Linux binaries.
- MAUI minimise-to-tray plus the General-tab checkbox (see §3).
- **Verify `WindowsDesktopNotifier` at runtime** — it has only ever been compiled.

**Estimate: 1–2 sessions.** Ship it as its own release.

**What landed, against that list:**

| Item | State |
|---|---|
| `MacDesktopNotifier` | Done, as the macOS branch of `DesktopDeliveryPlan` rather than a class of its own — `terminal-notifier` when present, `osascript` otherwise, both Intel and Apple-silicon Homebrew probed. Serves the WebHost head. |
| Windows notifier for the WebHost | Done. The WebHost cannot reference the Windows App SDK (wrong TFM), so it is the PowerShell toast, borrowing PowerShell's own AUMID because an unpackaged process has no identity to hang a toast on. |
| Headless speech per OS | Done — `say` on macOS, SAPI on Windows, the Orca→`spd-say` ladder unchanged on Linux. Windows speech is SAPI, **not** NVDA/JAWS: there is no supported command-line route into a running Windows screen reader, so the toast is the path that reaches one. |
| MAUI minimise-to-tray + General checkbox | Done, Windows head only (Mac Catalyst has no tray — see the correction in §3). Default OFF, announces on change, tray menu says "Quit". |
| Verify `WindowsDesktopNotifier` at runtime | **NOT DONE — no Windows box.** Still compile-only, as since 2.8.0. |

**Not shipped as its own release yet.** The suite is 6,939 green and Phase 0 is on `main`; cutting
2.10.0 is a separate decision, and there is an argument for holding it until one of the two
untested desktops has actually been touched.

### Phase 1 — one long-lived scope, not a second event bus — **DONE 2026-09-06**

`LocalBackgroundMonitor` already calls `_scopes.CreateScope()` once per poll. **Keep one scope for
the process lifetime instead**, and resolve `IEventBus`, `IWorkspaceStore`, `IOrderExecutionService`
and `DesktopNotificationService` inside it. "Browser closed" then becomes "a headless session that
happens to have no UI", and every existing in-session subscriber works unchanged.

This is deliberately **not** a re-lifetiming of the container — that would be a far larger and
riskier change, and scoped-per-circuit is correct for everything a circuit actually owns.

The rule that replaces *"pause the monitor while a circuit is open"* becomes a **routing** rule,
with exactly one delivery owner at a time:

| | Circuit open | Circuit closed |
|---|---|---|
| Speech | the circuit (Orca via the browser) | headless (`IDesktopAlertPresenter.Speak`) |
| Toast | headless, opt-in per category | headless, opt-in per category |
| Earcon | the circuit | headless sound |

> **THE HAZARD, and it is the 22nd pass's lesson inverted.** *Two subscribers speaking about the
> same event is one lost utterance* — the narration bug of 2026-09-05. Here the risk is the mirror
> image: two subscribers **doubling** one event. Any test for this phase must capture the bus with
> a circuit open **and** with none, and assert exactly one delivery in each. A test that only
> exercises one of the two states proves nothing about the case that breaks.

**Estimate: 1 session plus tests.**

**What landed, against that list:**

| Item | State |
|---|---|
| One scope for the process lifetime | Done — `WebHost/Services/HeadlessSession.cs`, a singleton created lazily on first poll and disposed with the container. `LocalBackgroundMonitor` no longer calls `CreateScope()` at all; a behavioural test counts scopes across two polls. |
| Resolve the in-session subscribers inside it | Partly, and the omissions are deliberate. `AlertDeliveryService` is force-created, so background alerts reach email / Telegram / webhook for the first time. `DesktopNotificationService` is force-created **without the Alerts category** (see the routing note below). `IOrderExecutionService` is **not** subscribed yet — that is Phase 2, and it needs the credential and reconnect decisions listed there first. |
| The routing rule replacing the pause | Done, and it is per SYMBOL rather than per channel — see below. `CircuitAlertCoverage` asks each open circuit what it is actually watching (focused chart + running background-tab monitors) and the headless session takes the rest. |
| A test with a circuit open AND with none | Done. Every delivery assertion in `HeadlessSessionTests` is written twice and asserts exactly one delivery in each state. Four sabotages, each red, each restored. |

**The routing table above was implemented differently from how it was written, and the reason is
worth keeping.** The toast row said "headless, opt-in per category" in *both* columns. That cannot
mean "the headless subscriber delivers the circuit's toasts" — the buses are per scope, and the
headless bus never sees a circuit's events. Read as *"the toast is an OS artifact in both states,
unlike speech (browser Orca vs `spd-say`) and earcon (WebAudio vs `paplay`)"* it is already true
and needed no change. The rule that was actually needed is the one the table does not state:
**the scope that produced an event owns its delivery**, and the per-symbol coverage rule is what
guarantees exactly one producer.

**And one thing the plan would have broken.** Routing the monitor's alert through
`DesktopNotificationService` — which is what "resolve `DesktopNotificationService` inside it"
literally says — would have put an already-opted-in delivery behind
`notifications.desktop.alerts`, which defaults **off**. A user who had turned on "keep monitoring
when the browser is closed" would have silently stopped getting toasts. Hence the
`DesktopNotificationCategories` mask: the headless instance owns fills and new bars (Phases 2 and
3) and never alerts. It is the Phase 0 lesson repeating — *a switch inherited from another caller
is a policy nobody wrote down.*

**Found on the way:** `AlertEvaluator` leaves `AlertFired.Symbol` null and only
`AlertOrchestrator` stamps it, so every background alert had been filed in the tray's recent list
with no symbol at all — and would have reached per-asset webhook routing the same way. Fixed and
pinned.

### Phase 2 — order fills headless (the risky one)

`GeneralOrderService.SubscribeLive(providerName)` already exists
(`GeneralOrderService.cs:105–129`) and subscribes to a provider's `OrderUpdateStream`. The headless
scope subscribes on startup to every provider that has stored keys and an open order or position.

Decisions this needs, each of which should be settled before code:

- **Credentials without a user session.** Fine on local `HostMode.Full` (one user, one key store).
  Needs separate thought if this ever reaches the hosted head, where keys are per-user.
- **Unattended reconnect and rate limits.** A websocket that dies at 03:00 must escalate the way
  `DeadFeedTracker` already does for OHLCV. Silent non-coverage is worse than no feature: the user
  believes they are being watched and they are not.
- **SAFETY LINE, and it should be stated in the code as well as here: headless REPORTS, it never
  ACTS.** No strategy execution, no automatic orders, no stop adjustment with nobody watching.
  Anything that places an order stays in-session.

**Estimate: 1–2 sessions.**

### Phase 3 — new bars, and the alerts the monitor cannot watch today

- New bars need live feeds for the watched symbols. `Core/Services/Feeds/BackgroundTabFeedService`
  already has a cap on concurrent live background feeds — reuse it rather than inventing a second
  budget.
- Condition-tree, indicator-target and POC alerts become watchable because Phase 1 gives the
  headless scope the whole indicator pipeline. `BackgroundWatchability.WhyNotBackgroundWatchable`
  is the list of reasons that should then shrink; it is also the honest record of what is still
  refused, and it must be updated in the same commit rather than left claiming a limit that no
  longer applies.
- **New-bar toasts need to be gated harder headless than in-session.** A one-minute chart is a
  toast a minute and the MATE notification daemon queues them. Consider a minimum timeframe, or a
  digest, rather than one toast per bar.

**Estimate: 1–2 sessions.**

## 5. Total, and the sequencing argument

**4–7 sessions.** Which is the whole reason 2.9.0 should be cut before any of it starts: the
release currently sitting untagged fixes a 2.8.0 regression (the order-book button gate) and a
marker-anchor defect that has been live for as long as the anchor has existed. Holding those
behind a multi-week feature is the wrong trade, and a tagged 2.9.0 is the fallback point if the
lifetime work goes badly.

**Recommended order:** cut 2.9.0 → Phase 0 as its own small release → Phases 1–3 as the next
headline.

**Where that stands (2026-09-06):** 2.9.0 is tagged. Phases 0 and 1 are both on `main` and
neither has been released — the argument for holding is unchanged and now covers two phases:
every Phase 0 path is proved only as far as the process start, and Phase 1's doubling hazard is
proved by unit test rather than by a person with Orca running and a browser open. Two to three
sessions remain (Phases 2 and 3).

## 6. What Phase 0 does NOT prove, stated plainly

Every command above is asserted character for character by `DesktopDeliveryPlanTests`, from Linux.
That proves **what gets spawned** and nothing about what happens after it starts. The standing
rule in this repo is to demonstrate the defect or mark it unverified, so:

- **The Windows PowerShell toast has never been raised on Windows.** The AUMID trick, the WinRT
  type load in PowerShell 5.1, and whether Narrator announces the result are all unverified.
- **`WindowsDesktopNotifier` (MAUI, Windows App SDK) is still compile-only**, unchanged since
  2.8.0: does `Register()` succeed unpackaged, does the toast reach Narrator/NVDA.
- **No macOS command has run on a Mac.** `osascript` display-notification behaviour under a
  hardened runtime, and whether VoiceOver announces Notification Center banners in the user's
  configured way, are unverified.
- **Minimize-to-tray has never been exercised in a Windows session.** The four smoke-test steps
  are at the top of `TrayIconService.cs`. CI's `maui-windows-build` job compiles the file; a
  compile is not a smoke test.
- **The tray icon has still never been measured with a screen reader**, on either head.
- **Phase 1's doubling hazard has never been heard.** The routing is proved at the level of
  *which owner delivered what*, in unit tests. Nobody has sat with Orca running, a browser open on
  one symbol, and an alert firing on another. Its
  tooltip is its accessible name and the menu is reachable the way any notification-area menu is,
  but that is reasoning, not measurement.
