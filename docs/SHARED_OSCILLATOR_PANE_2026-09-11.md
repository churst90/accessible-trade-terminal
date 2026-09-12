# The shared "Oscillator" pane — why RSI went flat next to MACD

**Status: DIAGNOSIS ONLY. Nothing was edited, no test written. The fix changes the pane model
the user navigates, so it is Cody's call — options are in §6.**

Reported by Cody, 2026-09-11 (night): *"the RSI when I look at it almost sounds flat, I still
hear the texturing but the line is definitely not correct sounding"*, and then the decisive
observation: *"RSI sounds correct after I removed the MACD"*.

His guess was that the two share a scale. It is the opposite, and that is the whole bug: **they
do not share a scale, and they do share a pane.**

---

## 1. The mechanism, end to end

1. **Both indicators declare the same default pane.** [verified]
   - `Indicators/SkenderBoundedOscillatorProvider.cs:85` — `Code = "Rsi" … DefaultPane = "Oscillator"`
   - `Indicators/SkenderZeroCrossProvider.cs:60` — `Code = "Macd" … DefaultPane = "Oscillator"`

2. **The metadata's `DefaultPane` WINS when a series is created.** [verified]
   `SeriesManagementService.cs:246`:
   ```csharp
   string pane = meta.DefaultPane ?? _stylingService.GetPane(indicatorCode);
   ```
   So adding RSI and adding MACD both put a series in the one pane literally named
   `"Oscillator"`.

3. **The viewport range calculator computes ONE range per pane**, accumulating min/max over every
   component of every series in it (`ViewportRangeCalculator.cs`, the `accum` dictionary keyed by
   pane), then adds a 10% buffer. [verified]

4. **RSI and MACD are not on the same numeric scale, and it is not close.** RSI is bounded 0–100
   by construction. MACD is a *price difference* — EMA(12) − EMA(26) of price — so on BTC around
   $100,000 its values run in the hundreds to low thousands. One pane holding both gets a range of
   roughly −800…+800 rather than 0…100.

5. **Pitch is normalised against that pane range.** [verified]
   `Audio/NavigationSonifier.cs:210-212`:
   ```csharp
   var range = state.PaneRanges.TryGetValue(rangeKey, out var r)
       ? r
       : (state.PaneRanges.TryGetValue(series.Pane, out var pr) ? pr : state.ViewportRange);
   ```
   RSI's whole 30→70 working span then occupies about **2.5%** of the pitch range. Arrowing
   across it is a line that barely moves.

6. **Everything else still works, which is why it reads as "flat but not broken".** Grit and
   texture, pan, per-bar volume and the tick are driven by other parameters and are unaffected by
   the pane range. Only the *pitch* collapses. Cody heard exactly that.

Removing the MACD leaves RSI alone in the pane, the range becomes 0–100 (buffered), and the pitch
is correct again — which is the observation that identified the cause.

---

## 2. It is NOT a regression from the notification work

[verified] `git diff --name-only e1e41709..HEAD` touches no file under `Services/Audio`,
`Services/Indicators`, `Services/Accessibility`, or the renderer. The `DefaultPane` values are
static metadata and are unchanged. Cody confirmed independently that switching the background
narration off makes no difference.

This is long-standing, and the reason it has survived is in §4.

---

## 3. Blast radius

**Thirty-one production indicators declare `DefaultPane = "Oscillator"`** [verified by grep], and
their value scales fall into at least five incompatible families:

| Scale family | Typical magnitude | Indicators |
|---|---|---|
| Bounded 0–100 | 0–100 | RSI, Stochastic, Stoch RSI, MFI, Ultimate Oscillator, Choppiness, ADX, Connors RSI, Aroon |
| Bounded negative | −100–0 | Williams %R |
| Roughly ±100 | ±100–300 | CCI, CMO, STC |
| **Price-difference (unbounded, scales with the instrument)** | **hundreds–thousands on BTC** | **MACD**, Momentum, DPO, ATR, Std Dev, Force Index |
| **Cumulative volume (unbounded, enormous)** | **millions–billions** | **OBV**, ADL, Chaikin Oscillator, EOM |
| Percent / small | −1…1 or a few % | ROC, TRIX, Historical Volatility, CMF, PPO, Ulcer Index, Vortex |

Any indicator from a *smaller* family sharing the pane with one from a *larger* family is
compressed toward inaudibility. **RSI beside OBV is far worse than RSI beside MACD** — OBV on BTC
is in the hundreds of millions, which would pin RSI to a single pitch.

**What is affected beyond the arrow keys:**

- **Playback** — `Audio/AudioSequencer.cs:268-269` uses the same pane range, so a run through an
  oscillator is flat in the same way. [verified]
- **The rendered chart** — the same `PaneRanges` drives drawing, so the squashed line is visible
  too. A sighted user sees a flat line near the middle; this is not audio-only.
- **`Accessibility/ChartLayoutDescriber.cs:206`** — `Alt+Shift+/` announces the pane's range, so
  it reports the *combined* range and describes the pane honestly but uselessly ("Y axis: −800 to
  800" for a pane containing RSI).
- **`Accessibility/ChartHitTester.cs:93`** — coordinate/mouse mapping onto the pane uses it.

**Not affected:** anything in the `Main` pane (price, moving averages, bands, VWAP, Ichimoku) and
anything with a pane of its own (Cipher B has `Pane_CIPHER_B`, volume has `Volume`).

---

## 4. Why nobody caught it — two authorities for one decision

There are **two sources of truth for which pane a series lands on**, and they disagree.

- `PaneAssignmentService.GetPane(code)` gives almost everything **its own pane**: its last line is
  `return $"Pane_{indicatorCode.Replace(" ", "_")}";`. Under this rule RSI → `Pane_Rsi`, MACD →
  `Pane_Macd`, and this bug cannot happen. [verified]
- The provider metadata's `DefaultPane` says `"Oscillator"` for all 31, and **it is consulted
  first** at `SeriesManagementService.cs:246`. [verified]

The codebase already knows the second one is a hazard, and says so — in a comment that asserts the
opposite of what the code does. `SeriesManagementService.cs:588-594`:

> *"pane assignment is decided by `PaneAssignmentService` from the indicator code — **not by the
> provider's own `DefaultPane`, which several providers set to a value the assignment service
> never returns**."*

That paragraph is the rationale for a real invariant (a default level must be in the units of the
pane the indicator lands on). Its premise is false: line 246 checks `meta.DefaultPane` first.

**And the guard built on that premise is aimed at the wrong pane.** [verified]
`AccessibleTrader.Tests/MainPaneLevelUnitsTests.cs:87` scans with
`panes.GetPane(meta.Code)` — the assignment service — while production uses the metadata value.
So for every indicator that declares a `DefaultPane`, that guard is green about a pane the series
never lands on. It is a second instance of the durable rule from this repo's own history: **a
guard aimed one method left of the code it protects.**

---

## 5. A separate latent defect found on the way (NOT this bug)

While tracing the pitch path I hypothesised — wrongly — that the pane key was *missing* from
`PaneRanges`. It is not, in Cody's case. But the fallback that would fire if it ever were is worth
recording, because it fails silently in the same direction:

`NavigationSonifier.cs:212` and `AudioSequencer.cs:269` both fall back to **`state.ViewportRange`
— the price range** when the pane key is absent. For a Main-pane series that is accidentally
correct; for any oscillator it would be catastrophically flat, with no log. `PaneRanges` is only
recomputed when `Data`, the viewport, or the `ActiveSeries` **list reference** changes
(`WorkspaceStore.cs:123-135`), and `TabReducer.cs:88` restores it verbatim from a tab snapshot —
so a stale-key miss is reachable in principle. The range calculator already knows the right
default for an empty pane (0–100, or ±100 for Cipher B, `ViewportRangeCalculator.cs:161-167`); the
sonifier should use that, not the price range.

Filed as its own item; it did not cause this report.

---

## 6. Fix options — Cody's call, because this changes the pane model

Every option changes what `Alt+PageUp` / `Alt+PageDown` traverse, which is why none of them is
being taken unilaterally.

**Option A — one pane per indicator (delete `DefaultPane = "Oscillator"`).**
Let `PaneAssignmentService` decide, as its own documentation already claims happens. RSI, MACD and
OBV each get a pane, each with its own honest range, and the bug cannot recur for any pair.
*Cost:* a chart with six oscillators becomes six panes to traverse instead of one. On a saved
workspace the existing series keep their stored `Pane` string, so nothing moves until re-added —
which means Cody's current charts would not self-heal without a migration.

**Option B — normalise per SERIES rather than per pane.**
Leave the pane layout alone and give each series its own pitch range (its own min/max over the
viewport, or its declared bounds where it has them — RSI knows it is 0–100). Indicators can keep
sharing a pane visually while each sounds correct.
*Cost:* the audible pitch would no longer correspond to the drawn position within the pane, so
what you hear and what a sighted person sees diverge. That is a real accessibility trade-off and
arguably the wrong direction for this project.

**Option C — group by scale family.**
Keep a shared pane but only for compatible scales: a `Oscillator_Bounded` pane for the 0–100
family, and own-pane for the unbounded ones (MACD, OBV, ATR…).
*Cost:* a rule someone has to maintain per indicator, and a wrong classification is silent again.

**Option D — refuse the collision and say so.**
Keep today's layout, but when a pane's range spans more than ~10× a series' own range, announce it
("RSI is sharing a pane with MACD and cannot be heard clearly — move it to its own pane?") and
offer the move.
*Cost:* does not fix anything on its own; but it converts a silent wrongness into a spoken one,
which is this project's standing preference.

**Recommendation: A, with a one-shot migration** that moves an existing series off a shared
`"Oscillator"` pane onto its own, so current workspaces heal — plus D's detection as the guard
that stops it coming back. A and D together make the invariant enforceable: no pane may hold two
series whose ranges differ by more than an order of magnitude, and if one does, the terminal says
so instead of going quiet.

Whatever is chosen, two things should land with it:
- **Fix the `MainPaneLevelUnitsTests` aim** (§4) so it scans the pane production actually uses.
- **Correct the comment at `SeriesManagementService.cs:588-594`**, which currently asserts the
  opposite of line 246.

---

## 7. Not verified here

- Nothing was run or heard by this session; the reproduction is Cody's ear, and it matches the
  mechanism exactly.
- No pane range was measured on a live BTC chart — the "hundreds to low thousands" figure for
  MACD is arithmetic from its definition, not an observation.
- The other 29 indicators sharing the pane were read from metadata, not exercised.
- Whether any saved workspace of Cody's already carries a per-indicator `Pane` string (which would
  change what a migration has to do) was not checked.
