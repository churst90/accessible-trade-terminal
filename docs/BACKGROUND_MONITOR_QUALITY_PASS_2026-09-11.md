# Background monitor quality pass — 2026-09-11 (evening, after the forty-first pass)

**Status: ANALYSIS ONLY. Nothing in the tree was edited. No test was written or run.**
This document is the work order for the next session. It was produced by one session reading
the code and three delegated read-only reviews; every finding is tagged **[verified]** where
this session re-read the cited lines itself, or **[reported]** where it rests on the delegated
review alone. Nothing here has been *demonstrated* by a failing test yet — the standing rule
("demonstrate the defect or mark it unverified") applies to every item, and the implementing
session should write the red guard before the fix, item by item.

Suite at the time of writing: 7,321 (commit `e1e41709`, CI green on all three workflows).

---

## 0a. CODY'S ANSWERS — 2026-09-11 (night). These AMEND §1; where they disagree, these win.

Asked and answered in the implementing session. Four decisions, and the first one **reverses**
§1 rule 2.

### D1. The channel is decided by the event's SUBJECT, not by the process.

> "When monitoring other tabs that are open in the browser, yes, I think receiving toast
> notifications for those would be appropriate as that's the only way you'll see them. The aria
> live region should only be used for the tab currently open in front of the trader."

| Event | Browser connected | Browser closed |
|---|---|---|
| bar close on the FOCUSED chart | live region + earcon, **no toast** | toast |
| bar close on another open tab | **toast** + earcon | toast |
| alert on the focused symbol | live region | toast |
| alert on a symbol with no tab open | **toast** | toast |
| fill / stop / take-profit on the focused symbol | live region | toast |
| fill / stop / TP on any other symbol | **toast** | toast |
| narration ladder | focused chart only, live region | toast |

**This kills §1 rule 2 ("no system toast, no spd-say, from anything") and with it H1's
"delete both coverage registries".** The registries answer exactly the right question —
*is a browser already announcing this symbol?* — and under D1 that is still the question. What
changes is the CHANNEL the circuit uses for the events it owns: speech for the chart in front of
the trader, a toast for everything else. `CircuitAlertCoverage` and `CircuitOrderCoverage` stay.

`BrowserPresence` (the connected-circuit counter, H2) is still needed, for three things that are
genuinely per-process: the farewell toast on the 1→0 edge (§6), muting a disconnected circuit's
own deliverers during the three-minute retention window (H9), and the sentence
Ctrl+Alt+Shift+M speaks (§2).

### D2. Q1 — one switch, default ON.

The three `notifications.desktop.*` switches collapse into **one**, and it defaults **on**.
Wording: *"Notify me about events I cannot see — other open tabs, and while the browser is
closed."* The bar-close timeframe floor stays as a separate volume control beneath it. Rationale
is the thirty-ninth pass: three default-off switches across two dialogs is how "switched off, not
broken" happened, and a blind user's cost of an accidental silence is total.

### D3. Q4 — money always pierces a snooze.

Snooze silences alerts, bar closes, the narration ladder and the self-reports. Order fills, stops
hit and take-profits hit always get through. The tray label becomes **"Silence alerts and bar
closes for 30 minutes"**. And per H7 the snooze must gate ANNOUNCEMENT, never observation.

### D4. Q5 — the timeframe floor gates the ladder as well as the bar close.

A 1-minute chart under a 5-minute floor is fully silent with the browser closed. This is what the
settings hint already promises; today the ladder ignores the floor and recites every minute.

### D5. Q6 — MAUI maps "browser closed" to "window hidden".

> "On the MAUI heads, if the person closes the application with the X in the upper corner or
> Alt+F4, then it should, by default, minimize to tray and toast notifications should be sent."

So the Windows head intercepts window close, hides to the tray instead of exiting, and its
singleton `DesktopNotificationService` becomes the delivery owner from that moment — the same
role `LocalBackgroundMonitor` plays on the WebHost. Default on, with a Settings escape hatch for
"actually exit on close".

### Still unanswered: Q2 (partly), Q3, Q7, Q8.

- Q2's toast half is answered by D1 (background-tab closes toast). Its speech half —
  `notifications.backgroundTabBars.speak`, default off — is now redundant with the toast and
  should probably go; not asked.
- Q3 (should an in-session background tab get the full ladder, not the two-clause sentence) —
  unanswered.
- Q7 (recent-alerts list becoming a recent-events list) — unanswered.

---

## 0. The question that started it, and the answer

Cody: *"The desktop notifications like in MATE seem to fire even when the browser is open.
They should only fire if the browser is closed."*

**Answer [verified]:** they come from the *in-session* toaster, not from the headless monitor.

- `AccessibleTrader.BlazorClient.Components/Layout/MainLayout.razor:41` injects
  `DesktopNotificationService` into every browser circuit, so one instance is created per tab.
- `AccessibleTrader.Core/Services/Notifications/DesktopNotificationService.cs:100-119`
  subscribes that instance to `AlertFiredEvent`, `NewBarEvent`, `BackgroundBarClosedEvent`,
  `OrderFilledEvent`, `StopHitEvent`, `TakeProfitHitEvent` and calls `notify-send` for each,
  gated only by the three `notifications.desktop.*` switches.
- Cody's `~/.local/share/AccessibleTrader/settings.json` has all three switches on and the
  new-bar floor at `1m`; his saved session has three tabs (MEXC KASUSDT 1d, Bitstamp BTCUSDT 1m
  focused, MEXC TAOUSDT 1d) with `workspace.backgroundMonitoring` and
  `workspace.liveBackgroundTabs` both on. So every BTCUSDT minute close (the focused chart,
  via `NewBarEvent`) and every daily close on the other two tabs (via `BackgroundBarClosedEvent`
  from `BackgroundBarAnnouncer`) raises a MATE toast **from the circuit itself**.
- The headless monitor is innocent here: `LocalBackgroundMonitor.cs:162-166` announces only
  charts no circuit covers, coverage is the focused symbol plus every background-monitored tab
  (`WebHostBrowserCircuitHandler.CoveredSymbols`, `:329-339`), and with background monitoring on
  all three tabs are covered.

The toast was never *defined* as the browser-closed channel. It was added in the twenty-sixth
pass (2026-09-05) as "a toast for the moment the terminal is NOT the window you are in"
(`DesktopNotificationService.cs:40-44`), i.e. for the alt-tabbed-away case. The headless work
then reused its switches. The class comment at `:57-62` still claims the monitor "pauses while
a circuit is open", which stopped being true on 2026-09-06 (Phase 1). Two audiences ended up on
one switch, and nobody wrote the policy down.

---

## 1. The policy (Cody, 2026-09-11) — this is now the contract

> Toast should be browser closed only, that's the entire purpose. When I have background
> monitoring on, I close the browser and receive all terminal events as system notifications.
> If the browser is open, then I receive them as normal aria events in the browser, and that
> includes if the browser is minimized. When I close the tab or the browser, the terminal
> events should be sent through the system. In fact, when you close the browser/tab, it should
> send a notification toast that says "Accessible Trade Terminal will continue running in the
> background and will receive all terminal events", or something more tasteful and informative.

Restated as rules the code and tests can pin:

1. **One process-wide gate: "is any browser circuit CONNECTED right now?"** Not "does a
   circuit exist" (a closed tab's circuit is retained ~3 minutes), and not "is this symbol on
   screen". Connected means the SignalR connection is up.
2. **Gate open (a browser is connected):** every terminal event — alert, fill/stop/TP and the
   other order outcomes, bar close on any tab, narration ladder — is delivered inside the
   browser as live-region speech and earcons. **No system toast, no `spd-say`, from anything.**
   Minimised is connected.
3. **Gate closed (no browser connected):** the headless process owns every one of those events
   and delivers each as a system notification (read by whatever screen reader is present),
   through `DesktopAnnouncement.Present`, which already picks toast-or-speech per machine.
4. **The 1→0 edge** (last connected circuit drops) produces exactly one farewell toast, after
   a short grace period so a reload or a network blip does not trigger it, and the headless side
   starts announcing from that edge — with the memory it needs already warm.
5. **The 0→1 edge** (a browser connects) silences the headless side at once.
6. Nothing about this changes what is *evaluated*. The headless monitor keeps observing every
   saved chart on every poll, connected or not, so its seed is warm at hand-off (the 2026-09-11
   "second bar after hand-off" fix stays).

### Decisions only Cody can make (asked once here, answered nowhere yet)

- **Q1. What happens to the three `notifications.desktop.*` switches?** Under the policy the
  alerts and fills switches have no reader left (the headless alert and order paths deliberately
  ignore them and ride `monitoring.backgroundLocal`). The new-bars one is already the headless
  bar-close gate. Options: (a) delete alerts+fills, rename new-bars to say "with the browser
  closed"; (b) keep all three as per-category headless gates (the Phase 1 and Phase 2 write-ups
  argued against that: a switch inherited from another caller is policy nobody wrote). The
  recommendation here is (a).
- **Q2. Background-tab bar closes inside the browser.** Today a bar closing on a tab you are
  not looking at reaches you in-session as an earcon always, a toast if the switch is on, and
  speech only if `notifications.backgroundTabBars.speak` is on (default OFF, Cody's 2026-09-08
  call: "toast and earcon by default, speech opt-in"). Remove the toast and the default channel
  is an earcon with no words. The policy says every event reaches you as speech inside the
  browser. Does the speech switch default ON now, or does the earcon-only default stand?
- **Q3. The narration ladder for background tabs in the browser.** Headless, every saved tab
  gets bar close **plus the full ladder**. In-session, only the focused chart has a ladder;
  background tabs get the short two-clause sentence. After the policy lands, closing the browser
  still gives you *more* narration than leaving it open. Is that acceptable, or should the
  in-session background sentence carry the ladder too?
- **Q4. Order events during a snooze.** The tray's "Silence alerts for 30 minutes" silences the
  whole headless poll (alerts, bar closes, ladder, self-reports) but not order events. Is that
  the intended split? (Money events piercing a snooze is defensible; the label is wrong either
  way.)
- **Q5. Should the timeframe floor gate the ladder as well as bar closes?** Today a 1-minute
  chart under the floor stays silent for bar closes but still speaks its ladder every minute,
  and the hint text says it stays silent.
- **Q6. MAUI.** The Windows app has no headless half; its `DesktopNotificationService` is a
  singleton and the "browser closed" analogue is minimise-to-tray. Deleting the in-session toast
  makes the MAUI head silent when minimised. Does the policy map "browser closed" to "MAUI window
  hidden", and who delivers there? (Not compiled on this box; unverifiable here.)
- **Q7. Recent-alerts list.** With headless owning all events, should the tray's "Show recent
  alerts" become a recent-*events* list (bar closes and ladders are not filed today)?
- **Q8. Ctrl+Alt+Shift+M** — see §2.

---

## 2. Ctrl+Alt+Shift+M — not redundant, but incomplete and sometimes wrong

**What it is [verified]:** `ShortcutManager.cs:610` binds `SystemCommand.MonitoringStatus`;
`CommandDispatcher` publishes `AnnounceMonitoringStatusEvent`; the only subscriber is
`BackgroundMonitoringService.AnnounceStatus()` (`Core/Services/Workspace/BackgroundMonitoringService.cs:243-309`).

**What it speaks [verified for the off branch, reported for the rest]:**
- in-session monitoring off and no exposure monitors: *"Background monitoring is off, and no
  positions are open. Enable it in Settings, General."* (`:252-260`)
- off but positions open: *"…off, but N charts are watched for open paper positions…"*
- on: *"Watching N background workspaces for alerts and strategy signals; X is the focused
  chart."* then per monitor: freshness, armed alert count, armed strategy count.

**Verdict:**
- It reports the **in-session** `BackgroundMonitoringService` (`workspace.backgroundMonitoring`)
  only. It never reads `monitoring.backgroundLocal`, never asks whether `LocalBackgroundMonitor`
  or `HeadlessOrderWatch` is running, never lists the headless watch set, the floor, or the
  snooze. **It does not know the headless monitor exists.** [reported, consistent with the code
  read here]
- With "Keep monitoring when the browser is closed" ticked and "Keep watching other tabs"
  unticked it says *"Background monitoring is off"* — false on that machine. The tray's
  "Connection status" line uses the same phrase for the *other* switch. **Two switches, one
  phrase, three surfaces.** CODE DEFECT.
- It is **not** redundant with Alt+J: Alt+J is `SystemCommand.OpenAlerts` (`ShortcutManager.cs:561`),
  which opens the Alerts dialog whose Delivery panel holds the switches. There is no toast
  toggle shortcut anywhere; the memory note that called Alt+J a toggle was shorthand for "the
  dialog where the switch lives".
- It is redundant with the "Speak status" button in Settings → General (same call) — fine.
- Its teaching line *"nothing armed — add an alert or strategy for this symbol to hear from it"*
  is false once live background tabs are on: such a tab earcons every bar close regardless.
  [reported]
- It reports only `BackgroundMonitoringService` monitors, never the live-streamed tabs from
  `BackgroundTabFeedService` — a different set, and the one producing bar closes. [reported]

**Recommendation:** keep the key, make the sentence cover both owners: which switch is on
(in-session watching / headless), what the headless side is watching (saved tabs, floor, snooze
state, venues hooked), and — after the policy — the one word that matters: *"a browser is
connected, so everything is announced here"* vs *"no browser connected"*. Rename nothing.

---

## 3. Findings — the headless half (`AccessibleTrader.WebHost/Services`)

### H1. The routing rule is per symbol; the policy is per process. DESIGN NOW REVERSED [verified]
`LocalBackgroundMonitor.cs:162-166`, `OwnedWatches` at `:582-605`, `CircuitAlertCoverage.cs` (whole
class), `CircuitOrderCoverage.cs:22-33`, `HeadlessOrderAnnouncer.cs:122-134`. Every one of these
argues *for* per-symbol/per-venue coverage and *against* a whole-process pause, in writing. Under
the policy the decision input becomes one boolean ("any circuit connected") and the symbol/venue
registries become dead. The class docs of both registries must be rewritten, not amended.

### H2. There is no "connected circuit" count, and the one counter that exists is wrong for the job. CODE DEFECT if used [verified]
`WebHostBrowserCircuitHandler._activeCircuits` is incremented in `OnCircuitOpenedAsync` (`:139`)
and decremented only in `OnCircuitClosedAsync` (`:342`). `OnConnectionDownAsync`/`OnConnectionUpAsync`
(`:306-319`) never touch it. So it counts **retained** circuits: it reads 1 for three minutes
after the last tab closes and 2 for three minutes after a reload. Gating the headless side on it
would reinstate the exact three-minute silence the 2026-09-11 hand-off fix removed (comment at
`:290-304`). It has no production consumer today (`ActiveCircuitsForUser` is dead too — §5).
`CircuitAlertCoverage.SourceCount` *is* a connected count by construction, but it depends on
`IWorkspaceStore` resolving inside a try/catch that logs at Debug (`:237-248`); a circuit that
fails to register would read as "no browser", which under the policy means a toast over a live
browser.
**Build:** an explicit `Interlocked` connected-circuit counter on the handler, incremented in
`OnConnectionUpAsync` and decremented in `OnConnectionDownAsync`, each idempotent per handler via
a `_isConnected` flag (note `OnConnectionUpAsync` also runs for the FIRST connection, `:314-315`).
Keep `_activeCircuits` as the ops count and say so. Expose it as a static the way the registries
are, with a `ResetForTests`, because four test files open circuits through a static seam
(`HeadlessAlertTests.cs:65`, `HeadlessNarrationTests.cs:61`, `HeadlessSessionTests.cs:41`,
`HeadlessOrderWatchTests.cs`).

### H3. The headless scope's settings are frozen at first poll. CODE DEFECT, high [verified]
`ISettingsManager` is `AddScoped` (`WebHost/ServiceCollectionExtensions.cs:204`). `SettingsManager`
loads the JObject once and caches it for the life of the instance (`Core/Services/SettingsManager.cs:55-66`;
its own doc at `:19-27` says so). `HeadlessSession` holds **one scope for the process lifetime**
(`HeadlessSession.cs:82-110, 164`). Therefore the headless monitor reads `settings.json` exactly
once and never again. Every "read per poll, so toggling takes effect without a restart" claim is
false: `LocalBackgroundMonitor.cs:61-63`, `HeadlessOrderWatch.cs:68-72`, `DesktopTrayService.cs:90-93`
(which also still refers to "the monitor's per-poll scope", gone since Phase 1). The tray's
"Turn background monitoring on/off", the F12 checkbox, the Alt+J switches and the floor all fail
to reach a running monitor once it has polled once. **Fix first; nothing gated on a switch can be
verified by hand until it is.** Options: a `Reload()` on `ISettingsManager` called at the top of
each poll; or resolve `ISettingsManager` from a fresh short-lived scope per poll (what the tray
does); or a file watcher. Guard: flip a key on disk between two polls in the harness and assert
the second poll saw it.

### H4. Alerts are dropped from the fetch list while covered; bar closes and narration are not. CODE DEFECT [verified]
`:166` filters alert watches **before** `MergeTargets`; `:181-185` and `:194-198` filter only the
announce keys and keep the chart in `targets`. The comment at `:173-180` explains why dropping a
covered chart makes the first post-hand-off announcement the *second* bar close — and leaves that
bug in place for alerts: a covered symbol carrying alerts but no saved tab is not fetched, so the
evaluator's previous-value memory is cold at hand-off and the first poll cannot see a crossing.
When the symbol *is* a saved tab, the alert watch is filtered out, the tab watch survives with
`Alerts` empty, `:263` skips evaluation, and `GetChart`'s signature (`:799`) is computed without
the alerts; the moment the browser closes the alert watch reappears, the signature changes, and
`GetChart` rebuilds the chart from scratch, discarding the warm buffer. **Under the policy the
observe-while-connected rule must apply to all three reasons uniformly:** compute `targets` from
the unfiltered lists; decide *announcement* from the one gate. This also deletes `OwnedWatches`.

### H5. Two independent "did a bar close" oracles that can disagree. CODE DEFECT [reported]
`NoteBarClose` (`:721-737`, keyed on `_lastBarSeen[WatchKey]`) versus `HeadlessObservation.BarClosed`
(`HeadlessChart.cs:235-251`, keyed on `Merge()` appending). On a cold buffer or a gap re-seed the
chart returns `BarClosed:false, Narration:null` while `_lastBarSeen` may say a bar closed, so
`:258-259` raises a bar-close toast with no ladder, silently. Reverse case: with the new-bars
switch off `_lastBarSeen` is never populated (`:255` short-circuits), so turning it on mid-session
swallows one close. `ForgetChartsNotIn` (`:858-863`) prunes `_charts` but never `_lastBarSeen`
(`:429`), so the two memories drift. One oracle, owned by the chart.

### H6. `HeadlessOrderWatch.Announce` toasts AND speaks. CODE DEFECT [verified]
`HeadlessOrderWatch.cs:335-346`: `_presenter.Notify(...)` then `_presenter.Speak(text)`. Every other
headless announcement goes through `DesktopAnnouncement.Present`, whose entire purpose (Cody,
2026-09-11, after hearing it twice: "pick the best path, not both") is that speech runs only where
no notification tool exists. This is the doubling, one file over. Route through `Present`.

### H7. Snooze silences the whole poll, not "alerts", and not order events. CODE DEFECT + mislabel [verified]
`LocalBackgroundMonitor.cs:140` `if (_snooze.IsActive) return;` is the first statement of
`PollOnceAsync`, before the opt-in check and before any fetch — so it silences bar closes, the
ladder, dead-feed escalation and recovery reports too. `HeadlessOrderWatch` and
`HeadlessOrderAnnouncer` never consult `AlertSnooze` (grep: zero hits). The tray labels it
"Silence alerts for 30 minutes". Also, because the return precedes the fetch, a 30-minute snooze
on a 1-minute chart is a 30-bar gap: `HeadlessChart` re-seeds (`HeadlessChart.cs:221-233`) and the
first close after the snooze is swallowed on both oracles, and `_deadFeeds` never advances. Fix:
snooze gates *announcement*, never observation; decide Q4 for order events; relabel.

### H8. `DesktopAnnouncement.Present` cannot tell whether the toast was delivered. CODE DEFECT [reported]
`DesktopAlertPresenter.cs:86-101`: `Notify` is attempted unconditionally, exceptions become a
`LogWarning`, and `ProcessDesktopAlertPresenter.Run` (`:172-192`) swallows `Process.Start`
failures at Debug and never reads the exit code. A `notify-send` that exits non-zero (daemon gone,
no D-Bus session) is total silence with a success-shaped log, and no speech fallback because
`ShouldSpeak` is `!CanNotify`. At minimum: read the exit code, and fall through to speech on
failure.

### H9. The three-minute overlap is a double delivery today, including email/webhook. CODE DEFECT [verified for the mechanism; the concrete double not demonstrated]
On connection-down the circuit releases coverage at once (`WebHostBrowserCircuitHandler.cs:306-310`)
but its DI scope stays alive for the retention period with `AlertOrchestrator`,
`DesktopNotificationService`, `InSessionAlertRecorder`, `AlertDeliveryService`,
`BackgroundBarAnnouncer` and `IOrderExecutionService` all running. For up to three minutes an
alert fires in both pipelines: circuit toast + headless toast, two `RecentAlertsBuffer` entries
(`LocalBackgroundMonitor.cs:884`), and **two emails/Telegrams/webhooks** (circuit's
`AlertDeliveryService` and the headless one at `HeadlessSession.cs:178-179`). The comment at
`:300-304` acknowledges only the speech half. A duplicate outbound webhook can place a duplicate
order on the receiving end. **Fix:** at connection-down, mute the circuit's deliverers (they
cannot reach the user anyway — their speech goes to an unrendered live region) rather than rely
on the headless side to wait.

### H10. The final autosave races the monitor's session read. TIMING HAZARD [reported]
`OnCircuitClosedAsync` writes the last session snapshot (`:369-377`) ~3 minutes after the tab
closed; `LoadLastSession` (`LocalBackgroundMonitor.cs:689-706`) picks the newest profile every
poll. For three minutes the monitor works from the previous autosave, then the tab list can change
under it, changing `DeriveBarCloseWatches` → `HeadlessChartFactory.Signature` → chart rebuild →
one swallowed close, right in the hand-off window. Either autosave on connection-down as well, or
key charts so a tab-list change does not rebuild a chart whose identity did not change.

### H11. No farewell toast exists, and no hook for one. MISSING FEATURE [verified]
Grep for `beforeunload|pagehide|visibilitychange` across `.cs/.razor/.js/.html`: zero hits.
Design in §6.

### H12. Stale or reversed documentation on the headless side [verified unless noted]
- `LocalBackgroundMonitor.cs:38-47` class doc, "the pause is now a ROUTING rule".
- `LocalBackgroundMonitor.cs:582-597` `OwnedWatches` doc, "THE HAZARD is doubling, not silence".
- `CircuitAlertCoverage.cs:5-41` and `:63-66` (claims upper-casing that does not happen — the set
  is `OrdinalIgnoreCase`, nothing is upper-cased; same claim at `WebHostBrowserCircuitHandler.cs:64-68`).
- `CircuitOrderCoverage.cs:22-33` "Why PER PROVIDER and not 'is a browser open'".
- `HeadlessSession.cs:42-56` (three "exactly one delivery" invariants, two now reversed) and
  `:45-52` describing a headless `DesktopNotificationService` that `:203-220` of the same file says
  does not exist. `HeadlessOrderAnnouncer.cs:43-44` repeats the stale version.
- `HeadlessChart.cs:79-87` "a browser that has this chart open is already narrating it" [reported].
- `InSessionAlertRecorder.cs:13-19` "since Phase 1 it is true for a better reason" [reported].
- `Program.cs:122-123` "the monitor only covers browser-closed" — false today, true after H1.
- `WebHostBrowserCircuitHandler.cs:114-118` `ActiveCircuits` doc.
- `WebHostBrowserCircuitHandler.cs:290-304` says "the headless side owns everything the instant
  the connection is down" — true of the two registries only; `_symbolsByUser`, `_symbolWatch`,
  `_circuitsByUser` are still torn down only at circuit close (§5).
- `docs/BACKGROUND_MONITOR_SCOPE.md`: `:1-7` status line (says Phases 0-2 built; 3 and D4 are on
  main), `:36-38` (describes the pause as current), `:42-48` (table stale in three rows), `:50-56`,
  `:189-202` routing table (Toast row reads "headless" in BOTH columns — the "Circuit open" cell
  becomes "none"), `:216-222` (the written justification for toasting while a circuit is open),
  `:279-290`, `:308-310`, `:316-322` ("Suite 7,012"). Add a dated Phase 4 block; do not edit the
  history in place.

### H13. Smaller headless defects [reported unless noted]
- `_reportedFailures` (`:94, :310-315`) never clears; an alert fixed and failing again for a new
  reason is never re-reported. Contrast `DeadFeedTracker`, which recovers.
- `_lastBarSeen`, `_warnedMissingIndicators` (`:792`), `HeadlessOrderWatch._noStreamReported`
  (`:248`) grow for the process lifetime.
- `LoadLastSession` runs a directory scan and profile load every 60 s even with no alerts and
  every switch off, because `narrationOn` defaults true (`:154-157`).
- The ladder is computed and discarded when narration is off or the chart is beyond the
  live-tab cap (`:235-256`).
- `HasNarratedSeries` means two things: factory excludes drawings, `HeadlessChart.cs:120` does not.
- `MergeTargets` two-argument overload (`:628-631`) is test-only.
- `ExecuteAsync:126` swallows every poll exception into one warning a minute forever — the same
  "it can speak and did not report its own failure" hole the class was written to close, one
  level up. A persistent poll failure should announce once.
- `CircuitAlertCoverage.cs:73-74`, `CircuitOrderCoverage.cs:76-77`, `HeadlessOrderAnnouncer.cs:125-126`:
  bare `catch` with no log. Moot once the registries go, but the replacement gate must log.
- `HeadlessSession.Start()` `:189-196` logs at Information when the presenter is missing and then
  loses every order event for the process lifetime; should be Warning.
- `HeadlessOrderWatch.HasOpenWorkAsync` `:291-305` two bare catches discard the venue's actual
  error before reporting "its account could not be read".
- `AnnounceBarClose` uses a sound (`:761`), `AnnounceNarration` does not (`:776`); in-session the
  background earcon plays regardless. Decide once.

---

## 4. Findings — the in-session half

### S1. The per-circuit toaster must go. DESIGN NOW REVERSED [verified]
`DesktopNotificationService.cs` (whole class), its eager injection at `MainLayout.razor:39-41`,
the Scoped registration at `WebHost/ServiceCollectionExtensions.cs:543-545`, the MAUI singleton at
`BlazorClient/ServiceCollectionExtensions.cs:521-534`, and the `Program.cs:146-151` comment
("In-session desktop toasts … through the same presenter the monitor uses"). Keep the static
wording helpers `NewBarTitle`/`NewBarBody`/`FillBody`/`AlertTitle` — `LocalBackgroundMonitor` and
`HeadlessOrderAnnouncer` use them. Keep `LocalDesktopNotifier`/`IDesktopNotifier` registered
only if something headless still needs the seam; today the headless side uses
`IDesktopAlertPresenter` directly, so the notifier seam may go too (Q1).
Consequences: `DesktopNotificationCategories` becomes dead (already effectively dead: the only
production construction is `All`, `:82`); `BackgroundBarClosedEvent` (`Events.cs:369`) loses its
only subscriber (`:111`) — delete the event or repoint it (Q2).

### S2. The Delivery panel describes the toast as in-session. DESIGN NOW REVERSED [verified for :103-109, :139; rest reported]
`AlertDeliverySettings.razor:64-142`: legend "Desktop notifications", label "New bars on any open
chart" (`:104`), hint "…with the browser open or closed" (`:109`), "Delivered by
@Notifier.Describe()" (`:140`, attached to a fieldset that also holds the in-session speech switch
and the headless floor, neither delivered by that notifier). The fieldset is gated on the
*circuit's* `IDesktopNotifier.IsAvailable` (`:64`), so on a machine with no `notify-send` the user
also loses the **in-session speech** switch and the **headless floor** — backwards under the
policy. After Q1: gate on "this host has a headless monitor" (`Platform.IsBrowserHost && !IsDemo
&& !IsHosted`, the same gate `SettingsModal.razor:304` uses), relabel everything "with the browser
closed", move the speech switch out of the toast fieldset.

### S3. The Web Push panel is unreachable on every host. CODE DEFECT [verified]
`AlertDeliverySettings.razor:45` `@if (Demo.IsHosted && Demo.AllowBackgroundAlerts)`;
`DemoPolicy.cs:38` `IsHosted => Mode == Hosted`, `:212` `AllowBackgroundAlerts => Mode == Full`.
Unsatisfiable. The comment above it says this is intentional for hosted "today", but the effect
is that the only UI to enable or disable Web Push renders nowhere, while
`docs/USER_MANUAL.md:2505-2509` still tells hosted users to use it [reported]. Decide: gate on
`IsHosted` alone, or delete the panel and the manual paragraph together.

### S4. The switch inventory — nine switches, three lifetimes, two dialogs [verified for keys and defaults; UI locations reported]

| Key | Default | Read by | Exposed |
|---|---|---|---|
| `workspace.backgroundMonitoring` | false | in-session `BackgroundMonitoringService` (AND `DemoPolicy.AllowBackgroundMonitoring`) | F12 General "Keep watching other tabs"; **tray toggles the OTHER switch under the same phrase** |
| `workspace.monitorPollSeconds` | 30, floor 10 | in-session monitors only; no effect headless | F12 General |
| `workspace.liveBackgroundTabs` | false | `BackgroundTabFeedService` | F12 General "Live-stream other tabs" |
| `monitoring.backgroundLocal` | false | headless master switch | F12 General "Keep monitoring when the browser is closed"; tray |
| `notifications.desktop.alerts` | false | in-session toast ONLY | Alt+J Delivery |
| `notifications.desktop.orderFills` | false | in-session toast ONLY | Alt+J Delivery |
| `notifications.desktop.newBars` | false | in-session toast AND headless bar closes | Alt+J Delivery |
| `notifications.backgroundTabBars.speak` | false | in-session speech for background-tab closes | Alt+J Delivery |
| `notifications.newBars.minTimeframe` | "1m" | headless only | Alt+J Delivery |

Defects in the table:
- `monitoring.backgroundLocal` has **no `SettingsKeys` constant**: it is a `const` on
  `LocalBackgroundMonitor` (`:67`) and a raw literal twice in `SettingsModal.razor` (`:1877`, `:2137`).
  The master switch of the browser-closed half is typo-exposed across a project boundary. [verified]
- `notifications.desktop.newBars` is the only switch that already means "headless", and both its
  key and its label say the opposite. `notifications.newBars.minTimeframe`'s constant is
  `HeadlessNewBarMinTimeframe`; key and constant disagree about scope. [verified]
- Headless bar closes need `monitoring.backgroundLocal` AND `notifications.desktop.newBars` AND the
  floor; the dependency is stated once, in the other dialog (`SettingsModal.razor:318-325`). This
  already produced the "switched off, not broken" incident of the thirty-ninth pass. [verified]
- `workspace.liveBackgroundTabs` silently gates the in-session background bar close:
  `BackgroundBarAnnouncer.IsAnOpenBackgroundTab` (`:186-197`) asks `LiveBackgroundFeeds`, empty
  unless that switch is on. "New bars on any open chart" is false whenever it is off. [verified]
- `workspace.backgroundMonitoring` vs `workspace.liveBackgroundTabs`: near-identical names,
  unrelated mechanisms, one fieldset; live-stream works with watching off, so a user can get bar
  closes from tabs Ctrl+Alt+Shift+M says are not watched. [reported]
- Two dialogs, two commit models: `AlertDeliverySettings` writes through on every edit;
  `SettingsModal` commits on Save (Escape discards). One feature, two save semantics. [reported]
- `AlertDeliverySettings.Load()` runs from `OnInitialized` only, while its doc says "each time the
  panel opens" — check against `AlertsModal`'s view switching. [reported]

### S5. Background-tab bar closes in the browser: earcon always, speech off by default, and the docs say otherwise [verified]
`BackgroundBarAnnouncer.cs:126-141`: toast via the bus (gated on the desktop switch), then
`_earcons.PlayNewBar()` **unconditionally** (not gated on the desktop switch, on `AnnounceNewBars`,
or on the speech switch), then speech only if `SpeakBackgroundTabBars` — `interrupt: false` on the
Event channel. Against that:
- `SettingsKeys.cs:174-179` says "Toast and earcon ride the switches above" — false for the earcon.
- `BackgroundBarAnnouncer.cs:37-44`, `CHANGES.md:360-362`: "toast and earcon by default" — the toast
  was never default-on; `notifications.desktop.newBars` defaults false. Documented one way, coded
  another.
- `AlertDeliverySettings.razor:122` hint "over whatever you are reading" [reported] — the code is
  explicitly non-interrupting.
This is where Q2 and Q3 live.

### S6. Stale or wrong in-session documentation and text [reported unless noted]
- `DesktopNotificationService.cs:10-24` "there are now TWO of these services alive" — there is one.
- `DesktopNotificationService.cs:57-62` "pauses while a circuit is open" [verified].
- `IDesktopNotifier.cs:10-16, 24-27, 37` name the in-session service as the decider.
- `SettingsKeys.cs:167-172` rationale is the in-session one.
- `AlertDeliverySettings.razor:66-77, 83-101, 369`.
- `BackgroundTabFeedService.cs:12-16` "instead of a 30s poll" — evaluation still polls at 30 s;
  only the network round-trip is removed.
- `BackgroundWorkspaceMonitor.cs:31-36` "deliberately deferred" refactor that has shipped.
- `BackgroundMonitoringService.cs:37-42` gating paragraph omits the exposure monitors.
- `docs/USER_MANUAL.md:2332-2345` (browser-closed section is alerts-only, a version behind, and
  still says "and speech through Orca" which the 2026-09-11 fix removed), `:2470-2481` ("three
  switches", "on the current chart", "the window you are not in"), `:2477-2479` (advice premised
  on in-session toasts, floor unmentioned), `:2622-2624` and `:2631-2633` (control names that do
  not exist: "Monitor background tabs", "Live-stream background tabs"), `:2632` ("Binance today"),
  `:2657-2660` (quotes the removed word "current"), and the entire background-tab bar-close feature
  is undocumented.
- `docs/SHORTCUTS.md:363` Ctrl+Alt+Shift+M line — accurate, silent on the headless half.
- `docs/README.md:127-130` still describes the browser-closed half as "the next feature".
- `docs/WHATSNEW.md` has no sentence on any of this; the policy change needs one.
- `docs/CHANGES.md:1374-1391` (the 2026-09-05 origin entry) — annotate, do not rewrite.
- `TrayController.cs:82-89` announces "Alerts will speak with the browser closed" — wrong channel
  (notification, since 2026-09-11) and incomplete scope (fills, bar closes, ladder).
- `DesktopNotificationService.OnNewBar` ignores `WorkspaceState.AnnounceNewBars` (the speech path
  honours it at `AccessibilityFeedbackCoordinator.cs:593`); the headless bar-close path does not
  consult it either [verified for the coordinator line]. Decide whether the headless path should.
- `DesktopNotificationService.Enabled()` (`:126`) is the one unguarded settings read on the event
  bus path. Moot after S1.

### S7. MAUI [reported; not compiled here]
`BlazorClient/ServiceCollectionExtensions.cs:534` registers the in-session toaster as a singleton
with `WindowsDesktopNotifier` behind it; minimise-to-tray keeps it running. Deleting S1 without a
MAUI answer makes the Windows head silent when minimised. Q6.

---

## 5. Findings — the hosted analogue (latent: `HostedAlertMonitor` is never constructed)

- **Never constructed** [reported]: `Program.cs:195` gates registration on
  `AllowBackgroundAlerts` (`== Full`) inside a block that runs only when `hostMode == Hosted`.
  Documented as deliberate (`:190-194`). Everything below is therefore latent, and no test covers
  the suppression path (`HostedAlertMonitorTests.cs` covers enumeration, fan-out, dead feeds).
- **The retention hand-off is unfixed on the hosted side** [verified from the handler]:
  `_symbolsByUser` is populated in `OnCircuitOpenedAsync` (`:202`) and cleared only in
  `OnCircuitClosedAsync` (`:353`); `OnConnectionDownAsync` releases only the two local registries.
  A hosted user who closes the tab is unwatched for three minutes.
- **Hosted coverage is narrower than local**: focused symbol only, no background monitors —
  dormant because `AllowBackgroundMonitoring` is Full-only, but fails in the *doubling* direction.
- **`ActiveCircuitsForUser` and `_circuitsByUser` are dead code** [verified: the only reference
  outside the definition is a comment at `HostedAlertMonitor.cs:98`].
- **Two delivery architectures for one feature**: hosted calls `IAlertChannel`s directly
  (`HostedAlertMonitor.cs:223-224`), local publishes `AlertFiredEvent` on the headless bus and lets
  `AlertDeliveryService` fan out. Hosted therefore misses every other `AlertFiredEvent`
  subscriber. [reported]
- **Class doc contradicts body**: `HostedAlertMonitor.cs:22-24` says per-user suppression,
  `:96` says per symbol. `Program.cs:161-165` repeats the per-user version. [reported]
- **Crossing-edge state goes stale across suppression** (both monitors): a watch skipped while
  covered never updates the evaluator's previous value; on resume it can fire a spurious crossing
  or miss one. Local bar closes solved this (observe while covered); local alerts and all of hosted
  did not. Same fix as H4. [reported]
- Hosted keeps the blank-chart refusal list (`WhyUnwatchableWithoutAChart`) and never announces
  feed recovery — both documented and deliberate. Listed for completeness.

---

## 6. The farewell toast — design

**Hook on the 1→0 edge of the connected-circuit counter (H2), debounced.** Not on any of the
three raw events:
- `OnConnectionDownAsync` fires on every network blip, laptop sleep and VPN flap (the user would
  hear "continuing in the background" while looking at the browser), fires on reload (the old
  circuit goes down, a new one comes up), and fires once per tab.
- `OnCircuitClosedAsync` fires ~3 minutes late, after the headless side has already been
  announcing; also fires 3 minutes after a reload with the user in a live new circuit; and fires
  during process shutdown.
- A JS `beforeunload`/`pagehide` beacon distinguishes a close from a blip but not from a reload,
  needs a new endpoint (`navigator.sendBeacon`, respecting the localhost guard at
  `Program.cs:790`), and does not fire on a crash, `kill -9` or a pulled cable — the cases the
  feature exists for. No JS interop of this kind exists today.

**Mechanism:** when the connected count goes 1→0, start a grace timer (10–20 s: longer than a
reload round-trip or a brief blip, shorter than one poll). If the count is still 0 when it fires:
(1) raise the farewell toast through `DesktopAnnouncement.Present`; (2) open the headless gate.
If a circuit connects before it fires, cancel silently. On 0→1 the gate closes immediately. The
same edge is where the circuit's own deliverers should be muted (H9), and where the final
autosave should also run (H10) so the headless session reads the tab list the user just left.

**Wording (proposal, Cody to edit):** title *"Accessible Trade Terminal"*, body *"The browser is
closed. The terminal keeps running in the background: alerts, order fills, bar closes and
narration for your saved charts arrive here as notifications until a browser connects again."*
Say what will be watched (N saved charts, M active alerts, venues hooked) if that costs one
sentence; say "nothing is being watched — turn on 'Keep monitoring when the browser is closed'"
if the master switch is off — a farewell that announces silence is more useful than no farewell.

**Guards to write first:** the counter reads 0 during retention and 1 after a reconnect; the
farewell fires once for three tabs closed together; a reload produces no farewell; a 5-second
blip produces no farewell; a circuit connecting during the grace cancels it; the headless side
announces nothing while any circuit is connected (alert, fill, bar close, ladder — one test each,
each written twice: connected and not, per the Phase 1 rule).

---

## 7. A separate defect found on the way: the saved tab's Market field grows

Cody's session file has MEXC tabs with `Market = "Crypto|Crypto|Crypto|Crypto|Spot"` (KASUSDT)
and eight `Crypto` segments (TAOUSDT); the Bitstamp tab has `"Crypto"`.

**Producer [verified]:** a feedback loop in `Core/Services/MarketOrchestrator.cs`.
- `:775-777` composes `Market = $"{EffectiveMarket}|{_selectedSubType}"` when the provider
  declares more than one sub-type (MEXC declares Spot and Futures, `MexcProvider.cs:354-355`;
  Bitstamp declares one, hence its bare `"Crypto"`).
- `:178` (`AdoptIdentityIntoToolbar`) and `:396` (`SyncMarketToProviderAsync`) both assign the
  **whole composite** back into `_selectedSubType`, which is declared as a bare sub-type.
- The sanitiser at `:634` resets an unknown sub-type to the list default — and `:396` re-pollutes
  it four lines later. Ordering defeats the guard.
- Result: every `LoadChartAsync` prepends one `"Crypto|"`. Saving is not the trigger; it only
  persists the value across restarts (`WorkspaceLibraryService.cs:240, :259` and
  `WorkspaceInitializer.cs:469` copy it verbatim). Ten Load Chart presses in one session = ten
  segments.

**What breaks [verified for DataService and WatchKey; rest reported]:**
- `DataService.cs:455-457` takes `Split('|')[1]` as the sub-type → `"Crypto"` instead of `"Spot"`;
  the API-key lookup survives only through its generous fallback.
- `ChartIdentity` equality includes `Market`, so every load mints a new identity: fresh cache
  buckets in `DataOrchestrationService`, `StrategyIndicatorCache`, `ChartPatternCache`, cold start
  each time, and the duplicate-feed hazard the `ChartIdentity` doc warns about.
- `LocalBackgroundMonitor.WatchKey` (`:432-433`) includes `Market`; when it grows, `_lastBarSeen`'s
  seed is orphaned and the chart reads as a first sighting — **which only seeds and announces
  nothing.** The "second bar after hand-off" bug, back through another door. Two tabs on one chart
  with different pipe counts also dedupe as two watches (double fetch, double announcement), and
  `SavedSeriesFor`'s exact market match (`:576-579`) never matches, so an alert may pick up the
  wrong tab's indicator stack.
- `TradingDashboardModal.razor:1170-1173, :2256-2257` take `[1]` as the sub-type. [reported]
- `WatchlistModal.razor:649` composes a key from the polluted sub-type. [reported]

**Fix shape:** at `:178` and `:396` take the sub-type half of the composite (split on `'|'`, last
segment) and never the whole string; add a guard that loads a MEXC chart three times and asserts
`Market == "Crypto|Spot"` after each; add a one-shot normaliser on restore
(`WorkspaceInitializer.RestoreTabInto`) that collapses `A|A|…|B` to `A|B`, so Cody's existing file
heals itself. Do this before the hand-off work, because it corrupts the very `WatchKey` the
hand-off tests will rely on.

---

## 8. Tests that pin the old routing (must change) and the doubling pairs (must survive)

All from `AccessibleTrader.Tests` [reported; names not re-read here].

**Go red under the policy** (each asserts a different symbol/venue survives a connected circuit):
- `WebHost/HeadlessSessionTests.cs:239` `A_symbol_an_open_circuit_is_watching_is_not_ours`, `:369`
  `With_a_circuit_open_on_another_symbol_ours_is_still_delivered_exactly_once`.
- `WebHost/HeadlessNarrationTests.cs:197` `A_circuit_on_another_symbol_does_not_silence_ours`.
- `WebHost/HeadlessOrderWatchTests.cs:109`, `:142`, `:184`, `:199` (venue-not-hooked and
  unattributed-fill cases).
- `DesktopNotificationServiceTests.cs:75, :100, :138, :151, :172, :189` — the in-session toast,
  pinned. Move the wording assertions (`:189` daily-date rule) onto the static helpers.
- `Blazor/AlertDeliverySettingsTests.cs:143, :157` — switch presence and keys.

**Become vacuous** (pass with the feature deleted; rewrite or delete): `HeadlessSessionTests.cs:255,
:269, :557`; `HeadlessAlertTests.cs:126`; `HeadlessOrderWatchTests.cs:100, :121, :133, :212`;
`DesktopNotificationServiceTests.cs:113, :125, :206, :215`.

**Must survive, rephrased from "covering the symbol" to "connected"** — these are the doubling
guards written twice per the Phase 1 rule:
- `HeadlessSessionTests.cs:328` ↔ `:351` (alert, closed vs open).
- `HeadlessSessionTests.cs:572` and `HeadlessNarrationTests.cs:176` — observe-while-covered,
  first close after the browser goes is announced with no re-seed. **These protect rule 6.**
- `HeadlessOrderWatchTests.cs:155` ↔ `:168`; `:399` ↔ `:425` (both turn the fills switch ON and
  assert one toast / no toast — the most important pair); `:251` ↔ `:283`.
- `HeadlessNarrationTests.cs:213` ↔ `:233` and `WebHost/DesktopAnnouncementTests.cs:37, :49, :59`
  (toast-vs-speech axis, unaffected).
- `CircuitCoverageHandoffTests.cs:41, :54, :68` — connection-down releases at once, reconnect
  takes back, double register holds one. Keep, against the new counter.

**Harness note:** `HeadlessMonitorHarness` has no circuit seam; every headless test opens a
circuit through `CircuitAlertCoverage.Register` directly. The new counter needs the same static
seam and a `ResetForTests`, or four files' reset hooks break.

---

## 9. Suggested order for the implementing session

1. **§7 Market growth** — small, self-contained, and it corrupts the keys everything below is
   tested through. Guard: three loads, `Market` unchanged; restore normaliser.
2. **H3 settings frozen** — nothing switch-gated can be verified by hand until this is fixed.
   Guard: flip a key between two polls.
3. **H2 the connected-circuit counter** with its test seam. Guards in §6.
4. **H1 + H4 + H6**: one gate at the top of `PollOnceAsync` and in `HeadlessOrderAnnouncer`, applied
   to *announcement only*; `targets` built from the unfiltered lists; `OwnedWatches` and both
   registries deleted with their tests; order-watch self-reports through `Present`.
5. **S1 + S2 + S3**: delete the in-session toaster, its injection, its categories enum, and
   decide `BackgroundBarClosedEvent`; relabel and regate the Delivery panel; fix or delete the
   Web Push gate. Cody's answers to Q1, Q2, Q6 are needed before this step.
6. **H9 + H10 + §6**: mute the circuit's deliverers on connection-down, autosave on
   connection-down, the debounced farewell.
7. **H7 + H5 + H8**: snooze gates announcement only (Q4); one bar-close oracle; `Present`
   reports delivery failure and falls through to speech.
8. **§2 Ctrl+Alt+Shift+M** reports both owners and the gate state.
9. **Docs last, in one commit**: the H12 and S6 lists, a dated Phase 4 block in
   `docs/BACKGROUND_MONITOR_SCOPE.md`, the User Manual's browser-closed section rewritten to the
   policy in §1, WHATSNEW, CHANGES, and a new START HERE block in `docs/TODO.md`.

Sabotage rule for every guard above: cp-restore, never `git checkout --`; grep for both
`Passed!` and `Failed!`; a non-matching filter is a failure.

---

## 10. Not verified in this pass

- Nothing was heard. No toast, no `spd-say`, no Orca.
- No test was run; every line reference was read, not executed.
- The MAUI head was not compiled; S7 rests on reading `BlazorClient/ServiceCollectionExtensions.cs`.
- `AlertDeliverySettings.razor` lines other than `:103-109` and `:139` were not re-read here.
- The hosted findings are latent by construction (`HostedAlertMonitor` never registers).
- The delegated reviews' items tagged **[reported]** were not re-read line by line here; treat
  each as a claim to confirm with a red guard before fixing.
