# A2q target survey: `AccessibleTrader.Core/Services/Accessibility`

2026-09-28. Read-only survey (nothing built or run), made to target the next mutation campaign.
Cody asked "what exactly needs to be tested in the accessibility section".

## Scale and history

- 49 files, 15,902 lines. **83 distinct mutants have ever touched it, on 23 files; 26 files never.**
- The mutants came from A2–A2e (scattered, e.g. M12/M13/M17), A2f (speech, 09-13) and A2j
  (narration, 09-19). No other campaign reached this folder. Every earlier survivor is closed
  (J39/J40/J48 proved in `a2j_prove_kills_results.json`; F24–F26 and M17 per TODO/CHANGES).
- Counted by parsing every `*sabotage_results*.json` by mutant id (a2b's reruns and a2f's first
  pass de-duplicated), with a grep fallback over all scratchpad JSON and Python.

## 1. Files with NO direct test at all (substituted or mocked everywhere)

| File | Lines | What is unguarded |
|---|---|---|
| NavigationEngine | 221 | Boundary earcon at the edge (X :144, Y :174); history backfill on Left below bar 50 (:129); Home/End/Live jump flag and viewport bounds (:65–81); Page Up/Down clamp and component reset (:207, :214); Delete refuses the candles (:107); granularity ±5 (:96). Substituted in 13 test files; only a browser test reaches it. |
| ViewportManager | 104 | Only a substitute and MockViewportManager. |
| SeriesNavigationRegistry | 38 | Profile/heatmap series route Up/Down through price bins (:255). |
| NotificationHub | 40 | Error goes to Critical plus the error earcon; Alert and Info go to Event (:25–37). |
| HistoryBufferCoordinator | 105 | Exhaustion reset on the next Ready (:47); "No more history available." when nothing was added (:73); failures through ReportError. |

## 2. Tested files with specific unpinned behaviours

- **AccessibilityFeedbackCoordinator (1,343 lines, only 2 mutants ever)**
  - `AlertFiredEvent` delivery (:147–157) is untested: Speech/Earcon/Both routing,
    Critical-vs-Event for break-through alerts, and `PlayAlert(breakThroughMutes)`.
  - Forming-pattern commentary gates (:696–772) are all unpinned: switch pair, candle
    `IsAutoNarrated`, the playback gate, the 5 s debounce (:731), change-only repeats (:763),
    and the once-only formation (:841).
  - Toggle confirmations (:319, :322, :325, :344, :348), "Panning step" (:498),
    "Loading history..." (:552), and the "Plus N more formations" count (:1336).
- **FeedbackRouters**: real-time expiry and the 4 s cap (:137), tested only through a reset seam.
  The in-flight reassignment rule (:146) is tested with only one follow-up keypress.
- **AutoNarrationService**: the F2 gate (:104, :150) has no test either way (every fixture sets
  speech on). The prepend/new-bar arithmetic (:170–176) is untested.
- **GlobalErrorCoordinator**: the "CRITICAL" wording (:73) and `ReportError`'s Low→Info mapping,
  "Error: " prefix and interrupt flag (:101–110) are untested; every caller is a substitute.
- **EarconService**: no test tells buy from sell fill motifs (:269–277) or stop from
  take-profit (:286–299).
- **SpeechTimeFormatter**: `FormatBarClock`, `FormatBarRange` and `SpacingOf` (:32–46) have no
  direct test, including the intraday/daily boundary.
- **ProfileBinClassifier / ProfileLevels**: VAH/VAL classification (:34, :36) is untested, and
  the value-area high/low ordering has only a presence check.
- **Delegated to the candidate list**: DrawingInteractionManager (1,362 lines, 0 mutants),
  TactileCanvasCoordinator (1,026, 0), DotpadTactileDriver, SdkCandlePatternAnalyzer (536, 0),
  IndicatorContextAnalyzer, HeadlessChart and HeadlessChartFactory (0 each).

**Dead code, exclude as equivalent**
- `SpeechFeedbackRouter.SpeakPoint/SpeakProfile/SpeakHeatmap`.
- `ViewportManager.GetRichViewportDescription/AnnounceViewport/EnsureVisible`.
- `DrawingInteractionManager.RecordEditForUndo`.
- `IndicatorContextAnalyzer`: NarrativeHint, TrendBars and the Upper/Lower zone branches.
- `BarDetailService`'s BB and MACD facts (see defect 2).

## 3. A2q candidates (49), highest user harm first

| # | Location | Mutation | Why the user notices |
|---|---|---|---|
| 1 | AFC:151 | delete the alert `Speak` | A price alert set to speech is silent |
| 2 | AFC:155 | `pierce ? Critical : Event` → `Event` | Shift+F2 mutes a break-through alert |
| 3 | AFC:157 | `breakThroughMutes: pierce` → `false` | A break-through alert's earcon is muted |
| 4 | NavEngine:144 | delete the Boundary publish | Silence at the chart edge; looks like a dead key |
| 5 | NavEngine:129 | `delta < 0` → `delta > 0` | No history loads when walking left |
| 6 | NavEngine:207 | clamp → wrap | Page Down teleports from the last series to the first |
| 7 | NavEngine:107 | drop `&& id != "candles"` | Delete removes the price series |
| 8 | NavEngine:214 | delete `SelectComponentAction(0)` | A stale component is read on the new series |
| 9 | NavEngine:65 | `ViewportStartIndex` → `0` | Home jumps off-screen |
| 10 | Registry:255 | drop `series.IsProfile \|\|` | Up/Down on a profile walks components |
| 11 | FeedbackRouters:146 | condition → `true` | The second arrow silences an order rejection |
| 12 | FeedbackRouters:137 | `>` → `<` | Every message holds priority for 4 s |
| 13 | AFC:731 | debounce `<` → `>` | Forming-pattern chatter on every tick |
| 14 | AFC:~722 | delete the `IsAutoNarrated` gate | Commentary without pressing N |
| 15 | AFC:763 | drop `patternChanged &&` | The same forming pattern repeats |
| 16 | AFC:841 | delete the `_lastFormingPattern` check | A formation is re-announced |
| 17 | AFC:699 | delete `if (IsPlaying) return` | Commentary talks over playback |
| 18 | AutoNarr:176 | `- 2` → `- 1` | Unstable forming-bar values are narrated |
| 19 | AutoNarr:170 | drop `&& _lastDataCount > 0` | The first redraw is treated as a bar close |
| 20 | AutoNarr:150 | delete the F2 gate | Decide the policy first (defect 4) |
| 21 | HistBuf:47 | delete the exhaustion reset | Backfill is dead after one "no more" |
| 22 | HistBuf:73 | `> 0` → `>= 0` | "No more history" is never said |
| 23 | NotificationHub:37 | Event → Critical | Advisories pierce Shift+F2 |
| 24 | AFC:319/322/325 | swap the on/off strings | Mute confirmations are inverted |
| 25 | AFC:344/348 | swap the HA/scale strings | Wrong candle type or scale announced |
| 26 | GEC:73 | `>=` → `>` | A critical error loses "CRITICAL" |
| 27 | GEC:108 | `>=` → `>` | High errors lose "Error:" |
| 28 | Earcon:269–277 | swap buy/sell motifs | A buy fill sounds like a sell |
| 29 | Earcon:286/296 | swap stop/TP motifs | A stop hit sounds like profit |
| 30 | SpeechTime:36 | `<` → `<=` | Daily bars spoken as "00:00" |
| 31 | ProfileBin:34/36 | swap VAH/VAL | Value area edges named backwards |
| 32 | ProfileLevels:25 | `>` → `<` | Value area range inverted |
| 33 | AFC:1336 | `total - 1` → `total` | Overlap count is off by one |
| 34 | DIM:1055 | swap AnchorName 1/2 | R/R prompt swaps stop and target |
| 35 | DIM:1112 | `?1:2` → `?0:1` | Completion names the wrong point |
| 36 | DIM:560 | swap "up"/"down" | Range summary gives the wrong direction |
| 37 | DIM:1145 | `> 0` → `< 0` | The R:R ratio is never spoken |
| 38 | DIM:1030 | same-bar guard → `false` | A zero-length line is accepted silently |
| 39 | DIM:1276 | delete the cancel announcement | Escape is silent |
| 40 | Driver:478–479 | swap PanLeft/PanRight | Hardware pan keys are reversed |
| 41 | Driver:426 | delete `IsConnected = false` | No reconnect after unplugging |
| 42 | TCC:119 | invert `_brailleEnabled` | F4 never turns braille on |
| 43 | TCC:433 | delete the pause return | Pins keep moving while paused |
| 44 | SdkCandle:321–322 | swap dragonfly/gravestone | A bullish doji is called bearish |
| 45 | ICA:240 | Bearish → Bullish crossover | A bear cross is narrated as bullish |
| 46 | ICA:193 | Falling → Rising | TrendChange alerts never fire on a turn down |
| 47 | BarDetail:141 | drop `KnownAtIndex <= idx` | The detail key leaks future formations |
| 48 | HeadlessChart:230 | delete `_lastValues.Clear()` | False background crosses after a gap |
| 49 | HeadlessFactory:105 | drop the parameter line from the signature | Editing EMA 20→50 keeps narrating EMA 20 |

## 4. Suspected live defects

Defects 1 and 2 were **confirmed by reading on 2026-09-28** but not demonstrated by a failing
test. The rest are suspected only.

1. **A monthly chart is described as "1 minute".** `ChartLayoutDescriber.SpokenTimeframe`
   (:295–305) lower-cases before its switch, so the `"M"` arm is unreachable and `"1M"` →
   "1 minute" (Alt+Shift+/ pane description). Confirmed by reading.
2. **The detail key's Bollinger squeeze and MACD-cross facts are always empty.**
   - `BarDetailService` reads the components `"Upper"`, `"Lower"` and `"MACD"`.
   - Every provider emits `"UpperBand"`, `"LowerBand"` and `"Macd"`, and no provider emits those
     three names.
   - The lookup is a case-sensitive `Dictionary`, and `GetComponentData` returns an empty array
     for a missing name, so the fact silently vanishes.
   - The test that closed J48 uses fictional names.
   - Confirmed by reading.
3. **A history backfill can re-narrate old bars.** A prepend in `AutoNarrationService`
   (:170–176) shifts bar indices, but the scanner's seed, markers and pivot are not shifted.
   `ShiftIndices` exists, but only HeadlessChart calls it. Suspected.
4. **F2 silences bar-close narration, contrary to QUICKSTART:223.** QUICKSTART says F2 silences
   what you asked for and Shift+F2 silences narration. The code comment calls the gate deliberate,
   and no test pins either behaviour. A decision for Cody.
5. **Dot Pad strip.** Values below 0.001 show as "0" (TCC:1012). The strip can stay blank after a
   reconnect because of text dedup (TCC:953, Drv:386). `dotpad.log` is appended on every emission,
   on every host (TCC:896). Suspected.
6. **Drawing.** The drag preview looks unreachable (:324–335, :777–862). One sloppy click on a
   3-point drawing can set both stop and target (:340, :377–401). `IsLocked` is never enforced.
   Suspected.
7. **`ChartPaneModel.Panes`:35 uses an unstable `List.Sort`.** With exactly 3 panes and Main not
   first, the two other panes can swap. Latent.
8. **Design questions.** Binned "No data" is routed as an Error (High earcon, Critical speech).
   `ReportError(Low)` maps to Info, which F2 mutes. The coordinator's Error arm ignores severity
   and interrupt (AFC:1031–1033).
