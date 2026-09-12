# Pre-release review — 2026-09-12 (discussion only, nothing edited)

Written at `bec6652b` (v2.9.0 + 29 commits, suite 7,472). Cody asked for a read-only pass over six
questions before the next tag. Every item below is marked **VERIFIED** (read in the code, with the
line), **DEMONSTRATED** (a concrete input that produces the wrong output), or **UNVERIFIED** (a
subagent's claim I did not re-read). The standing rule applies: do not fix an UNVERIFIED item
without first demonstrating it.

The accessibility-lead hook fired on this turn; that agent type is not registered in this
environment (see memory) and no UI code was touched, so nothing was delegated to it.

---

## STATUS after the forty-eighth pass (2026-09-12, evening)

This document was written as discussion only. What has since been ACTED ON, and what has not:

**Closed.** §1a D1, D2, D3 (every component declares `DefaultReferenceLevel`;
`StylingService.GetReferenceLevel` deleted; Connors RSI's "Zero" at 0 is now "Midpoint" at 50).
§1b in part — CMF, Cipher C and Pulse declare bounds, and `RangeMin` may now be declared alone as
a one-sided floor (ATR, StdDev, HV, Ulcer). §1c rows 1 and the `GetCategory` half of the last row.
§2 — the demonstrated gridline defect is fixed with one `ChartMath.NiceStep` shared by both files,
tests 1–9 and 11 are written, and a mutation campaign over the rendering path caught 17 of 18.
§5 — the `SKTypeface` leak and `AIAnalystService`'s shared-renderer snapshot, which turned out to
carry a third defect: the snapshot was also publishing its own pane layout to the service every
pointer-to-bar mapping reads. §6 — the whole comment sweep, in the commits beside the code. §7
items 1 and 2 — `CapabilityManifest` and a startup line naming every `DemoPolicy` flag. §3's
`TODO.md:9830` and the `HostedAlertMonitor.cs:42` comment are ticked.

**Cody's 0 key.** Reported separately and fixed in this pass: `0` now TOGGLES the pane's declared
midline (line and earcon together) instead of refusing with "already marked", which was its only
outcome on every indicator that declares one.

**Explicitly NOT done.** No release was cut — Cody, mid-turn: *"hold off on the cutting of the
release though."* So all of §4 stands, WHATSNEW included. §1b's Hurst, TBD and Vol Regime were
looked at and deliberately left: each is one pane holding components with different natural
ranges, and `RangeMin`/`RangeMax` are properties of the INDICATOR — per-component bounds are the
declaration those want. §1c rows 3–9, §2's browser test (12), and §3's MAUI gap and two-tab
lost-update remain open. Nothing in this pass has been HEARD.

---

## 1. Indicators — what else should be a declared property

The bounds work (`RangeMin`/`RangeMax`) fixed one instance of a pattern: a fact about an indicator
that was being inferred per call-site instead of declared once. The census found the same pattern
in **eleven more places**, and three of them are audible defects today.

### 1a. Audible defects found on the way (all VERIFIED by reading the chain)

**D1. Four bounded oscillators split their waveform at zero, on a 0–100 pane.**
`IndicatorModelFactory.cs:356-364` builds `ReferenceLevel` as
`meta.DefaultReferenceLevel ?? StylingService.GetReferenceLevel(...) ?? (Oscillator ? 0.0 : null) ?? ColorBaseline`.
No Skender provider sets `DefaultReferenceLevel` (grep: 0 hits across all seven files).
`StylingService.cs:170-178` hard-codes only RSI/MACD/STOCH/WILLIAMS by substring. So **MFI,
Ultimate Oscillator, Choppiness, STC** fall to step 3 and get `ReferenceLevel = 0.0`. The audio
above/below split (`ISonificationStrategy.cs:204`) is on that field, so those four never flip:
they are "above" forever. MFI's `ColorBaseline = 50` (`SkenderBoundedOscillatorProvider.cs:193`)
is unreachable because step 3 returns first — the comment at `:356-360` calling step 4 a
"fallback for histograms" describes a branch that cannot be taken for the type it names.

**D2. The `0` key on UltOsc, Chop and STC adds "Zero" at the bottom of a 0–100 pane.**
`CommandDispatcher.cs:381-386` takes `paneNeutral` from `ComponentConfig.ReferenceLevel` (0.0, per
D1). `ReferenceLevelPlacement.cs:170-174` first looks for an existing `LevelRole.Neutral` level:
MFI has "Midpoint 50" so it is protected; **UltOsc has only Overbought/Oversold
(`SkenderBoundedOscillatorProvider.cs:55-59`), Chop and STC declare no levels**, so the key falls
through to `paneNeutral = 0` and places "Zero" at 0. That is the exact defect the comment at
`ReferenceLevelPlacement.cs:178-181` says was retired ("Zero was the old answer and it was wrong
wherever the pane did not straddle zero"). The comment at `CommandDispatcher.cs:375-379` even
states the rule that produces it: "0 for oscillator and zero-area types".

**D3. ConnorsRSI's neutral is on its lower bound.** Bounds 0–100, its only level is "Zero" at 0
(`SkenderZeroCrossProvider.cs:51`), StylingService gives 50 by substring match on "RSI". Two
neutrals, one of them a bound. `DeclaredBoundsTests` passes because 0 is within [0,100].

Smaller, same family (VERIFIED by the census, not re-read by me): ADX's speech template has
`{zone}` but its levels are Developing/Strong/Very Strong (role None), so the zone word is always
empty; Hurst's "Random 0.5" level has role None so the `0` key stacks "Midpoint" on top of it;
Aroon declares ±100 for the whole indicator while Up/Down are 0–100 and only the Oscillator is
±100, and its single "Midpoint 50" level is right for Up/Down and wrong for the Oscillator; TBD's
confidences (0..1) carry a reference level of 0.0.

### 1b. Bounded-by-definition indicators that do not declare bounds (VERIFIED)

| Indicator | Natural range | Where the bound is already stated |
|---|---|---|
| CMF | −1..+1 | `SkenderZeroCrossProvider.cs:201` (Histogram, no bounds) |
| Hurst | 0..1 | `HurstExponentProvider.cs:58` ref 0.5, levels 0.45/0.5/0.55 |
| TBD confidences | 0..1 | `TopBottomDetectorProvider.cs:141` "(0..1)" |
| Pulse (four oscillators) | 0..100 | `PulseProvider.cs:10` "sharing a 0–100 scale" |
| Cipher C | ±100 | `CipherCProvider.cs:509` "clamped to ±100" |
| Vol Regime percentile | 0..1 | `VolRegimeProvider.cs:23` |
| Vortex | no hard bound, neutral 1.0 | `SkenderTrendProvider.cs:223` — leave auto-fit, but it needs a declared neutral |

Also: ATR, HV, Ulcer, OBV, volume-scale series are ≥0 by definition and cannot say so, because
`RangeMin` requires `RangeMax` (`ViewportRangeCalculator.cs:95`, test "Declare both or neither").
The "always-positive pane" is instead inferred from the data at `ViewportRangeCalculator.cs:200`.
A one-sided floor (`RangeMin` alone) is a small, honest extension.

**Answer to Cody's question:** of the 36 providers, the 14 that declare are the fixed-range
oscillators. The rest are overlays (no pane range of their own), genuinely unbounded oscillators
(MACD, ROC, CCI, ATR, OBV...) where auto-fit is right, or the seven above that are bounded and
simply were not declared. CCI is the judgment call: unbounded, but read at ±100/±200; leave it.

### 1c. The properties the code is asking for (by-name special-casing found outside metadata)

| What the code infers by name | Where | The declaration it wants |
|---|---|---|
| Neutral / reference level | `StylingService.cs:170-178` (RSI→50, MACD→0, STOCH→50, WILLIAMS→−50) | `IndicatorComponentMetadata.DefaultReferenceLevel` **already exists** (`IndicatorMetadata.cs:179`); the Skender providers never set it. The comment at `StylingService.cs:166-169` claiming they "have no static field" is false — they are hand-written lists. **Set it on every Skender component and delete `GetReferenceLevel`.** This alone closes D1, D2, D3. |
| Component role / display type | `ComponentRoleMapper.cs:12-88` (name registry + `Contains("UPPER"/"SIGNAL"/"HISTOGRAM"/"VOLUME"/"RSI"...)`) | `Role`/`DisplayType` exist per component; Skender still routes through the mapper at `IndicatorModelFactory.cs:33-34` |
| Is a moving average (crossing strategy) | `IndicatorCrossingEngine.cs:146-152` `StartsWith("EMA"/"SMA"/.../"HULL")` | `CrossingKind` or `IsMovingAverage`. **The list misses HMA (code is "Hma", the test says "HULL"), KAMA, ZLEMA, SMMA, TMA, VWAP; "PERCENTB" matches no registered code.** UNVERIFIED by me — demonstrate with an HMA crossing before fixing. |
| Overbought/oversold thresholds | declared levels **and** `IndicatorContextAnalyzer.cs:14-86` **and** `SkenderDetailFactProvider.cs:21-60` (RSI 70/30 read from parameters) | one source: the levels. Only the first sees user overrides (`SeriesManagementService.cs:614-617`), so a user who moves RSI's overbought to 80 hears 70 from two of three narrators. UNVERIFIED — demonstrate. |
| Which level is "the neutral" | `ISonificationStrategy.cs:224-225` `Name.Contains("Zero"/"Midpoint")`, `NarrationScanner.cs:717` `Contains("zero")`, `SpeechFormatter.cs:1157-1163` `Contains("Overbought"...)` | `LevelConfig.EffectiveRole` already exists (`LevelConfig.cs:130`); these sites predate it. The sonification one misses "Neutral"/"Midline" spellings used by Fear&Greed, COT, Crowding, Pulse. |
| Band pair / signal pair | `BarDetailService.cs:263-268` `code == "BB"`, `== "MACD"` | `HasBandPair` / `SignalPair` declaration |
| Is a profile series | `IndicatorModelFactory.cs:262`, `ProfileAnchoring.cs:94` `Contains("PROFILE")` | `IsProfile` |
| Value units (price vs percent vs index) | `SpeechFormatter.cs:1098` magnitude-aware decimals only for series id "price"/"candles" | `ValueUnits`. An EMA overlay on a sub-cent asset speaks "0.04". |
| "The" component | every consumer takes the first non-level visible component (`LevelCrossingMonitor.cs:115`, `IndicatorCrossingEngine.cs:212`, `ISonificationStrategy.cs:473`, `SeriesNarrationScope.cs:88`, `DeclaredBoundsTests.cs:114`) | `PrimaryComponent`, sibling to `NamedByParameters`. Works today because providers list the primary first; nothing pins that. |
| Pane / category | `PaneAssignmentService.cs:97-159` | `DefaultPane`/`Category` exist; `GetPane` survives only for the no-metadata path, `GetCategory` has no caller but `GetPane`. `:144` `c.Contains("ad")` matches any code containing "ad". |

**Recommended order:** (1) `DefaultReferenceLevel` on every Skender component + delete
`GetReferenceLevel` (closes D1–D3, one sabotage: blank MFI's level, hear it never flip). (2) The
seven missing bounds, plus one-sided `RangeMin`. (3) One OB/OS source. (4) `PrimaryComponent`.
(5) `CrossingKind`. The rest are cleanups with no audible defect behind them.

---

## 2. Rendering — what tests exist, and what to sabotage

The premise was half right. The Skia path has about **190 test methods in 15 files**, several
reading real pixels from an `SKBitmap` (`StandardRenderersSmokeTests`, `MarkerSizingTests`,
`RenderLayerTests`). But: **no rendering file has ever been mutated** (`MUTATION_A2E_2026-09-06.md:157-163`
lists `Core/Services/Rendering` among the never-mutated areas); sabotage is recorded for only four
classes (`ChartRendererDisposalTests`, `DeclaredBoundsTests`, `PaneRangeFallbackTests`,
`PaneAssignmentTests`); the smoke tests assert "drew something / drew something different, never
exact colours" (`StandardRenderersSmokeTests.cs:11-23`); and the browser harness never looks at a
chart pixel — the WebHost chart is a base64 PNG in an `<img>` (`ChartArea.razor:93`) and the only
assertion is that `src` starts with `data:image/png;base64,`, which the 1×1 placeholder satisfies.

There is no JS or SVG drawing; both heads draw through `ChartFrameRenderer.Render`. Never named by
any test: the concrete `PaneLayoutService`, `ViewportManager` (104 lines), `CanvasRegionProvider`,
`ChartRenderer.RenderYAxis/RenderXAxis/FormatAxisValue`, the pane-height allocator
(`ChartRenderer.cs:148-195`), `ResolveMarkerY`, and eight of the nine marker shapes.

**One live visual defect, DEMONSTRATED by hand.** `ChartRenderer.cs:444-452` (labels) and
`BackgroundLayer.cs:54-61` (gridlines) are two copies of the nice-step algorithm with different
targets (range/5 vs range/7). For a pane of range 20: gridlines pick step 2, labels pick step 5,
so labels 5 and 15 sit on no gridline. Any range in roughly 17.5–24.5 × 10^k does this, including
a 20,000-dollar BTC window. The comment at `ChartRenderer.cs:433-438` promises "Label positions
align exactly with major gridlines". Fix: one `NiceStep(range, target)` and a rule that the label
step is a multiple of the grid step. A blind user cannot detect this; a sighted one sees it on
every such chart.

**The tests worth writing, each with its proving sabotage** (extract the pure part first where noted):

1. `NiceStep` extracted; 30-unit → 5. Sabotage `< 7.5` → `< 5.5` at `ChartRenderer.cs:451`.
2. Label step is an integer multiple of gridline step. **Red on today's code** (above).
3. `FormatAxisValue` tiny ranges: 0.00003 on range 0.00001 → "0.0000300". Sabotage clamp `10`→`4` at `:525`; and delete `:534-535` to see "-0.00" return.
4. X-axis format by span (extract): 1 day→`HH:mm`, 30→`MM/dd`, 90→`MMM d`. Sabotage `< 2`→`< 20` at `:560`.
5. Pane heights via the real `PaneLayoutService`: three equal panes → dividers ≈0.25/0.50/0.75, all above the axis strip. Sabotage delete the rescale at `:182-195`.
6. Up candle body pixel == `Theme.CandleBullishBody`. Sabotage `>=`→`<` at `StandardRenderers.cs:159`. Sonification reads Close/Open directly, so this is invisible to Cody.
7. Volume bar colour follows the candle. Sabotage `:324` role check.
8. Directional bars index the viewport slice: render with `viewportStart = 50`; sabotage `ctx.Data[i]`→`ctx.Data[dataIdx]` at `:379/:387`.
9. `ResolveMarkerY`: a BelowBar mark's Y is below `MapY(bar.Low)`. Sabotage swap at `:786`.
10. `ViewportRangeCalculator` never pushes a positive-only pane negative. Sabotage delete `:201` (check `ViewportRangeCalculatorTests` first — may exist).
11. Whole-frame structural: one indicator pane, real layout; indicator band non-background, axis strip has text. Sabotage delete `currentY += mainPaneHeight` at `:224`.
12. Browser: after "My Data" loads, `<img src>` length ≫ the placeholder's ~100 bytes (`ChartArea.razor:261`).

Then a **mutation campaign over `Core/Services/Rendering` + `ChartRenderer` + `ChartMath`**, the
five never-mutated areas' first entry. Budget: 20–25 mutants, one full run each.

---

## 3. Background monitor — is it done?

**Against its own scope documents, yes.** Every Phase 0–4 deliverable and every quality-pass item
(H1–H13, S1–S7) has production code and, with two exceptions, a named test. (Subagent census,
spot-checked by me on the snooze path, the settings re-read, the farewell, and the DI scope.)
One process-wide monitor is correct for `HostMode.Full` because `Program.cs:798-812` refuses
non-loopback binding — Full is single-user by construction, so the "user A's alert fires for
user B" worry does not arise.

What is **not** done, and it splits three ways:

**Declared open by the docs themselves**
- `WindowsDesktopNotifier` never run (scope §4/§6). Tray-on-close likewise never compiled here.
- Hosted §5: `HostedAlertMonitor` is never constructed (`Program.cs:190-203` gates on
  `AllowBackgroundAlerts`, which is `Mode == HostMode.Full` at `DemoPolicy.cs:212`). Cody's
  decision on 09-11. Two defects sit dormant behind it (see §7, from the server notes).
- Q7 (recent-events list) unanswered; Q2 (the redundant speech switch, `SettingsKeys.cs:202`) not asked.

**Undeclared gap (VERIFIED registration, behaviour not run)**
- The MAUI head has no `LocalBackgroundMonitor`. It registers only the in-app
  `BackgroundMonitoringService` (`BlazorClient/ServiceCollectionExtensions.cs:623`), which is
  behind `workspace.backgroundMonitoring`, default off. So on MAUI, an alert on a symbol with no
  tab open is evaluated by nobody unless that switch is on — the Phase 1 defect, unfixed on that
  head. Scope §3 framed MAUI as "delivery paths, not a monitor" and that framing hid it.

**UNVERIFIED (exists, no test or never heard)**
- Poll-failure announcement (`LocalBackgroundMonitor.cs:405-424`) — no test names it.
- MAUI hidden-window → notification: parity registration test only.
- Still-not-heard list at `TODO.md:225-234`: farewell, settings toggle without restart,
  Ctrl+Alt+Shift+M combinations, a symbol-scoped alert on a resumed session, and the 47th pass's
  "RSI announces 0 to 100". Only two things have ever been heard: the routing rule and the
  browser-closed hand-off.

**Open TODO items in the monitor area, re-checked against the code:**
- `TODO.md:9830` (snooze suppresses evaluation) — **STALE, fixed** in the 43rd pass
  (`LocalBackgroundMonitor.cs:174-187`: evaluate always, speak by gate). Tick it.
- `TODO.md:9823` `AlertOrchestrator` scoped per circuit in WebHost, whole-list save → two tabs
  lose each other's alerts. **STILL OPEN**: `WebHost/ServiceCollectionExtensions.cs:608` AddScoped,
  `AlertOrchestrator.cs:110/119` save the whole list.
- `TODO.md:9843` hosted server logs alert text at Information — **STILL OPEN**
  (`HostedAlertMonitor.cs:222`). Moot while the monitor is gated off, but the line is one deploy
  away from being live again.
- `TODO.md:9849` bar freshness — **STILL OPEN**, severity lower than written: `DeadFeedTracker`
  catches a feed that stops answering; a feed that answers with old bars is silent, not
  duplicating (crossing-edge state at `AlertEvaluator.cs:277` stops the re-fire the item feared).
  Worth a "newest bar is N intervals old" announcement, not urgent.
- `TODO.md:9855` three comments — one of the three (`HostedAlertMonitor.cs:42` crossing-edge
  state) is now **true**, since `AlertEvaluator.cs:277` holds that state. The other two unchecked.

---

## 4. Should the next version be cut, and what number

**Yes, cut it, and it is 2.10.0, not 2.9.1.** The 2.8.0 precedent: minor when a default changes
under the user. This round changes four defaults a user hears without touching a setting: every
oscillator gets its own pane (Alt+PageDown walks more panes), fourteen indicators keep a fixed
axis, the three notification switches became one that defaults ON, and the Windows X button
minimises to the tray. That is a minor release by the repo's own rule.

Before tagging, in order:

1. **WHATSNEW is incomplete.** Its Unreleased section (lines 8–96) tells the story of the 09-11
   and 09-12 passes only. CHANGES `[Unreleased]` also holds, with no WHATSNEW sentence: order
   routing safety and the "real money" dashboard (09-07), one credential per provider, the
   provider conformance suite's ten plugin fixes, the toolbar dropdowns following the chart
   after a restore, "not real money" reaching every plugin, monitor Phases 0–3 (09-06/08),
   profiles that narrate and say their name, volume read at the close. Grep confirms: "real
   money" 0, "conformance" 0, "profile" 0, "volume" 0 in the Unreleased section. Assemble from
   CHANGES:5-1456, as RELEASING.md says.
2. Hear items 1–3 of §3's "not heard" list, or ship them marked unheard in WHATSNEW as the tray
   item already is ("compiles only").
3. Decide on §1a D1/D2 — they are audible and cheap (one property set on ~30 components); shipping
   2.10.0 with the `0` key placing "Zero" under a 0–100 pane on three indicators contradicts the
   2.9.0 story ("the line goes where the line means something").
4. Tick `TODO.md:9830`. Rename `[Unreleased]` → `[2.10.0] — date`. `Directory.Build.props:8`
   2.9.0 → 2.10.0; `ApplicationVersion` 9 → 10 in `BlazorClient.csproj:40`.
5. `python3 scripts/check_doc_drift.py`. Tell the server agent BEFORE the tag (§7: it must not run
   `sync-trader-docs.sh` between tags, and `/features`' "three switches, each off" sentence goes
   false at this tag).

---

## 5. Other things found on the way

- **`ChartRenderer.cs:56` leaks an `SKTypeface`** on every construction; `Dispose` is deliberately
  empty (`:1162`, documented at `:1110-1160` as leaving "two adjacent defects" open). VERIFIED.
  The server notes confirm the segfault fix is holding (0 crashes in 211 service-hours,
  P≈0.006 under the old rate), so this is a leak, not a crash. Fix: a static shared typeface.
- **`AIAnalystService.cs:271` renders through the shared `ChartRenderer`** at a fixed density —
  the second of those two adjacent defects. VERIFIED it still does.
- **The circuit handler forgets a closed tab's symbol only at circuit close**
  (`WebHostBrowserCircuitHandler.cs:92-94`), so during the ~3-minute retention window a closed
  tab is unwatched in hosted mode. Dormant while `AllowBackgroundAlerts` is false. VERIFIED the
  code; the consequence is the server agent's, UNVERIFIED by me.
- `ChartArea.razor:33-37` says the chart is an `SKCanvasView` painted via `OnPaintSurface`;
  `:83-90` says it cannot be and draws a PNG into an `<img>`. Same file, opposite claims.
- Two `<summary>` blocks on one member at `ChartRenderer.cs:607-635` (an orphan from
  `RenderCrosshair` above `CrosshairValueAt`'s own).
- `StandardRenderers.cs:1156` "must match RenderDot's palette exactly": a comment standing in for
  a shared constant (duplicated at `:555-558` and `:1158-1161`).

---

## 6. Comments that no longer match the code (clean as we go)

Monitor area:
- `HeadlessOrderAnnouncer.cs:38-46` — names `notifications.desktop.orderFills` (retired 09-11,
  `SettingsKeys.cs:194` is now `Legacy...`) and a headless `DesktopNotificationService` "built
  WITHOUT the OrderFills category" that was deleted 09-08. VERIFIED.
- `HostedAlertMonitor.cs:22` "per-user suppression" vs `:96` "per SYMBOL, not per user". Same file.
- `DesktopTrayService.cs:92` "the monitor's per-poll scope" — gone since Phase 1.
- `TrayController.cs:104` "Alerts will speak with the browser closed" — wrong channel and scope.
- `BlazorClient/ServiceCollectionExtensions.cs:529-531` — three opt-in toasts, panel hides them: one switch, default ON, panel shows it.
- `Program.cs:112` "heard through Orca/spd-say + notify-send" — notification-only since `67ba1790`.
- `HeadlessOrderWatch.cs:71` "Read per poll" — true only via `RefreshSettings`; reads as if always true.
- `CommandDispatcher.cs:375-379` — states "0 for oscillator and zero-area types" as the intended
  rule; it is the rule that produces D2.

Indicator/rendering area:
- `StylingService.cs:166-169` — "reflection-generated ... no static DefaultReferenceLevel field": false.
- `IndicatorModelFactory.cs:258-260` — "reference level lines are visual-only": they drive earcons, zone noise, the `0` key and narration.
- `IndicatorModelFactory.cs:356-360` — step 4 described as reachable for histograms; it is not for Oscillator/ZeroArea types.
- `ViewportRangeCalculator.cs:62-65` — says bounds come "from SymbolRenderHints"; the factory copies indicator metadata bounds too (`IndicatorModelFactory.cs:151`). Note the two rules differ: a Main-pane series with bounds hard-clamps the price axis, an indicator pane floors. No Main-pane indicator declares bounds today, so this is a comment defect, not a behaviour defect.
- `ChartRenderer.cs:433-438` — "align exactly with major gridlines": demonstrated false (§2).
- `PaneAssignmentService.cs:147-148` — "EMA Fill" exists nowhere else; `:155-157` unreachable for sma/ema.
- `LevelConfig.cs:62-64` — "~350 provider level declarations": about 60.
- `ChartRenderer.cs:373` "whisper-quiet — adjust if overwhelming": a tuning note with no owner.
- Tombstones for deleted code: `StandardRenderers.cs:83-89`, `:296-299`; `ChartRenderer.cs:137-144`.

---

## 7. The hosted deploy notes — systemic patterns and the central fix for each

`patches/HOSTED-DEPLOY-NOTES.md` (2,383 lines, revised 2026-09-12 14:00 UTC against `bec6652b`).
The server agent says nothing is open that it is carrying; what follows is the recurring shape of
what it keeps handling by hand, and where the repo could absorb each.

1. **Public sentences track the box, not the tag.** `/features` drifted five times (split view,
   the default theme, "two venues", missing narration, a Split button in a PNG), and the
   09-12 gate made "Close the browser and your alerts keep running" false on the hosted head.
   Its next false sentence is already queued: "three switches, each off until you turn it on".
   `check_doc_drift.py` "has never seen this site". **Central fix:** a per-`HostMode` capability
   manifest emitted by the repo (what `DemoPolicy` allows in each mode, one JSON per tag) that
   the site reads or the server checks. The `DemoPolicy` booleans already are that manifest.
2. **A `HostMode` gate lands with no runtime signal.** The only proof that `HostedAlertMonitor`
   stopped was a journal grep. **Central fix:** one startup log line per head enumerating every
   `DemoPolicy` flag and its value.
3. **Docs sync copies from HEAD, not the tag** (`sync-trader-docs.sh` does a blind `cp`; three
   consecutive passes where running it would have been wrong). **Central fix:** copy from
   `git show v<tag>:docs/...`, or make it a `release.yml` artifact.
4. **`/download` is hand-maintained per release** — five steps, six sizes, seven URLs, test
   counts. **Central fix:** generate from the GitHub release JSON plus WHATSNEW.
5. **Persisted state the desktop fixtures never see** (nested `settings.json` keys, `alerts.json`,
   `secrets/`). The notes' rule: "a release that *reinterprets* a persisted field needs a state
   archive, not a grep". **Central fix:** a migration note per commit touching a persisted
   type's wire format, and the `CloneCompletenessTests` pattern extended to serialisation.
6. **Checks whose passing value is reachable without the thing happening** — a `stat` size on a
   stale build, `publish` exit 0 after being killed, `grep -c SEGV` matching its own
   `kill -SEGV`. The repo's answer ("assert the artifact, not the incantation") already exists;
   the server side leans on the `2.x.y+sha` version stamp as "the only signal".
7. **Environment facts the app does not enforce** (XDG vars inside the deploy dir get destroyed
   by `rsync --delete`; `PrivateTmp` ate 16 dumps). The notes praise `KeyRingPolicy` for
   refusing to start on a loose key dir. **Central fix:** the same refusal for a data root inside
   the deploy directory.
8. **Two items the server agent leaves for the repo:** `SKTypeface` leak and `AIAnalystService`
   density (§5 above), and the two dormant hosted defects behind `AllowBackgroundAlerts`. Plus
   two questions it is waiting on from Cody: whether the key ring was rotated at 2.1.0→2.2.0
   (for the preserved-state tarball), and whether it should run the bUnit suite at deploy time.
9. **Operational facts to remember:** publish with `-p:ServerPublish=true`; publish log must end
   "Wrote 33 trusted plugin hashes"; harness 209/209 in ~4m30s; SDK pin `10.0.301` is
   load-bearing — "tell me before the pin moves"; `UseRazorSourceGenerator=false` likewise.

---

## 8. Suggested order for the next turn

1. §1c row 1: set `DefaultReferenceLevel` on every Skender component, delete
   `StylingService.GetReferenceLevel`, fix the three comments that describe the chain. Sabotage:
   blank MFI's level and hear the waveform never flip; press `0` on UltOsc and hear "Zero".
2. §1b: the seven missing bounds, plus one-sided `RangeMin`. Extend `DeclaredBoundsTests`.
3. §2 tests 1–2 (the demonstrated gridline defect) and 6–7 (candle colour), then the rendering
   mutation campaign.
4. §4: assemble WHATSNEW from all of CHANGES `[Unreleased]`; tick `TODO:9830`; cut 2.10.0.
5. §6 comment sweep, in the same commits as the code each comment sits beside.
6. §3 MAUI gap and the two-tab alert lost-update, after the tag.
