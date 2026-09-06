# Background monitoring — the expansion, scoped

**Status: SCOPE ONLY. Nothing here is built.** Written 2026-09-06 from a reading of the code as
it stands at `4702a00f`. Every "today" statement below carries the file and line it was read from,
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
- The `ITrayPlatform` seam is the right thing to reuse, but it lives in the WebHost project and
  would have to move to Core (or an equivalent be written for MAUI's window model).
- The checkbox must **default off**. An app that does not close when you close it is a surprise,
  and for a screen-reader user a surprise with no announcement is worse than an extra keystroke.
  Turning it on should say what it now does; the tray icon should carry a "Quit" item so there is
  always a way out that does not need the window.
- Accessibility note for whoever builds it: a tray icon is a control with no visible label. It
  needs an accessible name and its menu needs to be keyboard-reachable — the WebHost tray already
  has this problem and has never been measured with a screen reader.

## 4. The phases

### Phase 0 — delivery paths (independently shippable, highest value per hour)

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

### Phase 1 — one long-lived scope, not a second event bus

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
