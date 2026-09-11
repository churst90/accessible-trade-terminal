# Session review — background monitor Phase 3, and the narration coherence pass

**For a reviewer coming to this cold.** Everything below was done in one session across
2026-09-08 → 2026-09-11, in four commits on `main`. Suite went **7,223 → 7,265**, green.
Nothing here has been heard through a screen reader; §7 is the honest list.

| Commit | What |
|---|---|
| `85b82945` | Phase 3 scoped, and the new-bar feature measured down to one chart |
| `356f0591` | Phase 3 D3 — bar closes on background tabs (browser OPEN) |
| `0983ce28` | Phase 3 D1+D2 — bar closes with the browser CLOSED |
| *(this one)* | The narration coherence pass — Cody's five questions of 2026-09-11 |

---

## 1. The one idea underneath most of it

Six separate symptoms turned out to be three instances of one mistake, and it is worth stating
before the detail because it is the thing to check the rest of the codebase for:

> **A ✅ in a feature table is a claim about a SUBSCRIBER until someone traces the PUBLISHER.**
> **And "there is nothing here for you" is a sentence — not silence, and not an alarm.**

- New bars had a subscriber, a mask and a comment. No publisher could reach it. (§2)
- `IsAutoNarrated` on a Volume histogram set a flag that nothing could ever act on. Silence. (§5)
- Pressing `0` on a pane with no neutral was classified an **Error**, and error earcons
  deliberately ignore the earcon mute. Alarm. (§6)

The repo already had the right shape for this in `BackgroundWatchability`: a predicate shared by
the engine and the UI, so a thing that can never work says so at the moment you ask for it. Two
new predicates follow that pattern.

---

## 2. Phase 3, the finding that reframed it

`docs/BACKGROUND_MONITOR_SCOPE.md` put a ✅ against "new bars — browser open". Tracing it:

- One publisher of `NewBarEvent` exists: `WorkspaceStore.cs:231`, gated on a non-initial
  `UpdateDataAction` (`:116`).
- The only live path into it is `DataManager.OnFocusedFeedUpdated` (`DataManager.cs:79`).
- `MarketFeedHub.cs:194-198` raises `FocusedFeedUpdated` **only for the focused feed** — its own
  doc says so.

Meanwhile `BackgroundTabFeedService` deliberately keeps up to **8 non-focused tabs LIVE**
(`:31`), precisely so background charts stay current. **Their bars closed in silence.** Four
charts open, bar closes announced on one.

And `HeadlessSession.cs:214-224` force-created a `DesktopNotificationService` carrying
`DesktopNotificationCategories.NewBars`, with a paragraph explaining the mask — wired to an event
nothing headless could publish.

**Pinned by tests written to go RED when fixed:** `AccessibleTrader.Tests/NewBarReachTests.cs`
(3, with a positive control) and one in `HeadlessSessionTests`. Sabotaging the `ReferenceEquals`
focus filter turned both negatives red and left the control green.

**Review question for Fable:** are there other ✅ rows in `docs/` describing a subscriber?
`docs/BACKGROUND_MONITOR_SCOPE.md` §1's table is the one that has now been wrong twice (row 3 in
Phase 2, row 4 here).

---

## 3. D3 — bar closes on background tabs, browser OPEN

**`IMarketFeedHub.BackgroundFeedUpdated`** is the exact complement of `FocusedFeedUpdated`
(if/else on one read of `_focused`), so a handler on each sees every update once.

**`FocusedFeedUpdated` was deliberately NOT widened.** Its contract is load-bearing: binding a
background feed's bars to the store would file the wrong symbol's data under the focused chart's
identity, which is the bug `DispatchIfStillFocused` exists to prevent.

**`BackgroundBarAnnouncer`** (Core/Services/Feeds) owns the route and publishes
**`BackgroundBarClosedEvent`**, which carries its own `ChartIdentity`. `NewBarEvent` carries none
— it is always about the focused chart — and its subscribers fill in Heikin-Ashi, candle patterns
and formations from the store, every one of which would have described the wrong chart.

**Delivery (Cody's decision): toast + earcon by default, speech opt-in**
(`notifications.backgroundTabBars.speak`, default off, Event channel, never interrupting).

**Eligibility is TAB membership, not liveness** — `IBackgroundTabFeedService.LiveBackgroundFeeds`,
asked at announcement time. The hub also holds leased feeds for monitors, evaluators and split
views; those are not charts the user opened.

**Dependency worth flagging:** D3 only produces anything when **"Keep background tabs live"**
(`workspace.liveBackgroundTabs`, Settings → General) is ON — it defaults **off**
(`BackgroundTabFeedService.cs:75`). Cody confirmed hearing background earcons, so it is on for
him. *This is a feature whose visibility depends on a setting in a different tab, which is the
`Phase 1` "a switch inherited from another caller is a policy nobody wrote down" shape. Left as
is deliberately — the dependency is real, not accidental — but it is a candidate for a hint line
next to the new-bar switch.*

---

## 4. D1 + D2 — bar closes with the browser CLOSED

**The monitor OBSERVES the close rather than receiving it.** It already re-fetches every watched
chart once a minute, so "the newest bar is later than the one I saw last poll" is the whole test.
No store, no event, no second pipeline.

**The watch list is the user's saved TABS, not the alert list** — read from the newest
`__last-session__` autosave slot. *An alert is a question about a price; a chart is what the user
chose to watch, and a user with no alerts still has tabs open.* **Capped at
`BackgroundTabFeedService.MaxLiveBackgroundFeeds` (8)**, reusing that budget rather than inventing
a second number. `MergeTargets` keeps the ALERT watch when a chart has both — taking the other
would have dropped every alert on that chart silently.

**Seed on first sighting** (else startup announces a close on every chart at once) and **one
announcement however many polls were missed**.

**D2 floor:** `notifications.newBars.minTimeframe`, **default `1m` = every timeframe announces**
(Cody). The gate is the opt-in category switch `notifications.desktop.newBars` (default off); the
floor is the escape hatch. An unparseable floor or timeframe lets the announcement THROUGH — *a
typo in a setting must not be a mute switch the user cannot see.* **New bars only** — never
alerts or trade events.

The dead headless `NewBars` subscriber is deleted. All three headless event classes are now
consistent: Alerts (Phase 1), OrderFills (Phase 2), NewBars (Phase 3), each owned by the component
that can ask whether a browser is already saying it.

**`docs/BACKGROUND_MONITOR_PHASE3_SCOPE.md` is the full work order.** D4 remains, as its own pass
by Cody's decision.

---

## 5. Narration: ability vs occasion, and the volume silence

### 5a. The split (Cody's design, adopted as proposed)

The two "Describe candle patterns" / "Describe chart patterns" switches (General tab, under
Analysis — this document originally said "Speech tab", which was wrong) are the **ABILITY**: whether a pattern is ever *named*, on the arrow keys, in the bar-close sentence, in
Alt+Shift+D. That was *also*, accidentally, the only switch over the **live intra-bar
commentary**, so wanting pattern names while arrowing signed you up for a running commentary on
the forming bar with no way to separate them.

Two new **Narration**-tab switches are the **OCCASION**:

| Setting | Default | Why that default |
|---|---|---|
| `narration.formingCandlePatterns` | **ON** | It is what shipped; what is new is the ability to turn it off |
| `narration.formingChartPatterns` | **OFF** | A new occasion for speech is asked for, never imposed — the same call `DescribeChartPatterns` made |

Both halves are required: nothing narrates a pattern the user has told the terminal not to name.

### 5b. Forming CHART patterns — the missing half

Candle patterns on the live bar have been spoken since 2026-04. Chart formations were spoken when
the user **arrowed onto** them, and again when they **resolved** at a bar close — but never while
building, which is the moment a trader watching for a double top actually wants.
`AnnounceFormingChartPattern` closes it, and inherits three rules rather than inventing them:

- **`AsOf`** projects the formation to what was knowable *at* this bar — the no-lookahead rule, so
  a shape whose eventual break is already in the record does not announce that break early.
- **`ByDominance`**, capped at **one** (the navigation route allows two) — this arrives unbidden.
- **`Identity`, not `Key`** for "have I said this already", so a scroll-back that shifts every bar
  index does not re-announce the same formation.
- Shared debounce with the candle half, and the memory is **per formation, not per bar** — a double
  top spans dozens of bars.

### 5c. Volume — the silence that meant "never"

Narration is **signal-shaped**: marker components (dots/arrows/crosses), oscillator zone
transitions and crossovers, overlay crosses, level crosses. A Volume histogram has none, so
`IsAutoNarrated` went on, the scan ran every bar, and nothing was ever found.
`SeriesNarrationScope`'s own doc already committed to the rule it was breaking — *"Nothing lands
in a state where narration is 'on' and silent."*

**`SeriesNarrationScope.WhyNothingToNarrate(series)`** now answers it, and the series-scope
`ToggleNarration` reducer speaks it: *"Volume, narrating. Volume has no signals to narrate. Press
0 to add a reference level and its crossings will speak."*

**It names the way out, and the way out works** — `ScanLevelCrosses` narrates level crossings on
any **non-price** pane. On the price pane the advice is omitted, because level crossings are
skipped there (the overlay path owns it) and advice that does not work is worse than none.

**This does not make volume narrate by itself.** A volume-spike detector would be a new feature;
what changed is that the switch stops lying. *Flagging for Fable as the obvious follow-up if Cody
wants "every series narrates something".*

---

## 6. The `0` key, and the mute it was piercing

Only one branch of `ReferenceLevelPlacement.For` sets `isRefusal`: *"Nothing on this pane declares
a neutral line."* That was published as **`FeedbackType.Error`**.

Error earcons **deliberately ignore Shift+F3** — the silent-failure rule, so a *failure* can never
be inaudible (`EarconService.cs:103-127`). But pressing 0 on a pane with no neutral is not a
failure: nothing broke, and the sentence explains it completely. The effect was that a routine
"not applicable here" was the one sound a user could not mute, on every pane they explored.

Now **`Boundary`** — matching this same file's precedent twenty lines up for the anchor nudge:
*"the key was understood and has nowhere to go. Error would play the failure earcon and speak on
the channel F2 cannot mute, for a keypress that failed nothing."* "Already marked" stays `Info`.

**Answer to Cody's question:** the error earcon breaking through the mute was correct *policy*;
the classification was wrong. Fixed at the classification.

---

## 7. Ctrl+Alt+Shift+N, retired

One command had two bindings. The chord was kept for one release as "the one that works with focus
outside the chart" — a case where the user cannot see which series they are toggling either.
Removed from the profile and from Help, and **every comment that named it as the live way to set
the per-series flag was updated to say N** (ten files). A comment naming a removed keybinding is
the stale-claim genre this repo treats as a defect. Two deliberate historical notes remain.

An existing test asserted the chord was kept; that assertion was inverted, which is the stronger
guard.

---

## 8. What is NOT verified — read this before trusting any of it

- **Nothing in this session has been heard.** Every assertion is a unit test. Four phases into the
  background monitor, **no headless delivery has been heard through `spd-say` with Orca running**,
  and no earcon or sentence from D3 or the forming-chart-pattern route has been heard at all.
- `docs/BACKGROUND_MONITOR_SCOPE.md` §6's list is untouched: Windows toast, macOS commands,
  minimize-to-tray, the tray icon with a screen reader, Phase 2's live exchange socket.
- Phase 3 raises the per-poll request rate (one fetch per saved tab, capped at 8) and reads a
  workspace profile per poll. Unmeasured against a large workspace or a real venue's rate limit.
- The forming-chart-pattern route runs `_patternCache.For` on every intra-bar tick that passes the
  debounce. The cache is memoised per identity+data, but this is a **new caller on a hot path** and
  has not been profiled.
- The MAUI head was not compiled here (no workload on this box). `SettingsModal.razor` and
  `AlertDeliverySettings.razor` both changed; the `maui-windows` CI job is the first compile.
- **Accessibility review:** this session's UI changes (three new checkboxes, one new select, two
  corrected labels) follow the existing `modal-row` + `label for` + `aria-describedby` conventions
  and are covered by the repo's own guards (`SettingsWiringAuditTests`, `FormControlNameSweepTests`,
  `LandmarkContractTests`). **They have not been reviewed by the accessibility agents** — the
  `accessibility-agents:accessibility-lead` subagent is not registered in this environment. Worth a
  pass by Fable.

---

## 9. Guards added this session

| File | What it pins |
|---|---|
| `NewBarReachTests.cs` | New bars reach only the focused chart via `NewBarEvent`; the boundary D3 must not cross |
| `BackgroundBarAnnouncerTests.cs` | D3: eligibility, LiveAppend-only, playback guard, speech opt-in as a PAIR, wording |
| `HeadlessSessionTests.cs` (+12) | D1/D2: seed, missed-poll catch-up, category switch, floor, circuit coverage, merge |
| `NarrationAbilityVsOccasionTests.cs` | Narratability, the 0-key classification, the two defaults, the single N binding |

**Fourteen sabotages across the session, each proven red, each restored.**

## 10. Test-fixture defects found in my own work

Recorded because they are the kind of thing that makes a green suite meaningless:

- A harness returned the **same two bars on every fetch**, so a test claiming "five bar closes"
  was asserting that nothing closed. Added `advancingBars`.
- The first fix then fed the **crossing pair on every poll**, and the alert fired five times —
  *correct* behaviour for data that crosses five times. Realistic data crosses once and stays.
- A test named "missed polls announce once" advanced exactly one bar per poll. Renamed, and
  `barsPerFetch` added so one genuinely skips.
- One assertion compared a value **to itself**. Replaced with real assertions.

---

## 11. Addendum, later on 2026-09-11 — Cody's review of this batch

Three items came back from Cody after reading the above. Suite **7,265 → 7,280**.

**§5c was the wrong answer.** Cody: a bar-type series under N should be *read* at each bar close
as part of the ladder, not told it has nothing to say — "I may be doing dishes but still want to
keep an ear on the volume." Playback is a different occasion and speaks signals only.
`SeriesNarrationScope.ReadingComponent` is the predicate; `AutoNarrationService.ReadValueAtClose`
adds the clause at tier 7, bar close only. `VolumeReadingNarrationTests` runs the real
coordinator + narrator wiring: one utterance, close first, reading last; nothing on an intra-bar
tick; `PlaybackNarration.SignalsForStep` null on every bar for a narrated Volume.

**§5c's "the way out works" was also false, and the fixture was why.** Production Volume is a
`ComponentDisplayType.Bar`; §5c's tests used `Histogram`; `PrimaryReading` accepted Line and
Histogram only. So a level on a volume pane could never be crossed by anything. Fixed in
`PrimaryReading`, proven by sabotage (reverting it turns exactly one test red), and the advice is
now given only where a readable component exists.

**The headless bar close Cody could not hear was switched off, not broken.** His settings file has
no `notifications.desktop.newBars`; the default is off; the switch is "New bars on any open
chart" under Alerts (Alt+J). The General tab hint for headless monitoring now names it. Not
changed: the default. Whether it should be on by default is Cody's call (§3 of the TODO block).

**Correction to §5a:** the pattern switches are on the General tab (Analysis), not Speech.

**Then, with every box checked, still nothing — so it was run here.** The server was started on
this box with no browser attached (`ASPNETCORE_ENVIRONMENT=Development`, the Debug build, Cody's
own settings and saved tabs). Log, verbatim:

```
Background bar close: BTCUSDT 1m: close 77403.49 at 15:24. New bar: open 77403.48.
Browser circuit closed (0 active).
Background bar close: BTCUSDT 1m: close 77392.44 at 15:25. New bar: open 77398.71.
Background bar close: BTCUSDT 1m: close 77403.46 at 15:26. New bar: open 77403.45.
Background bar close: BTCUSDT 1m: close 77407.70 at 15:27. New bar: open 77407.70.
```

Cody opened a browser on that instance mid-run and closed it again; the closes after "circuit
closed" are headless. (The 15:24 one was announced while the circuit was open — the poll had
snapshotted coverage before the circuit registered, a one-time startup race; noted, not fixed.)
**This is the first headless delivery ever observed in four phases of the background monitor.**

Two delays in the hand-off were found and closed — the covered chart that was never observed, and
coverage released three minutes late on a dropped connection. `HeadlessSessionTests` (+1) and
`CircuitCoverageHandoffTests` (+3) pin them; both sabotaged red.

**Not established:** why Cody's own server was silent. There was no server process on the box
when checked. **Cody's next ask — the indicator narration ladder with the browser closed — is D4**
(`docs/BACKGROUND_MONITOR_PHASE3_SCOPE.md`), and it is now the top NEXT item.

**Still not heard by the author.** Cody may have heard the run above; nothing else in this addendum
has been through a screen reader.
