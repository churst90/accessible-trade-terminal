# Have market regimes got shorter since COVID?

**Verdict: NULL.** The post-COVID era's regimes are ordinary by fifty-year standards. They look
short only next to the era immediately before them, which turns out to be the calmest six years in
the whole record.

Command: `StrategyLab regime-persistence --snapshots strategy-lab-data [--recent-start yyyy-mm-dd]`
Detector tests: `AccessibleTrader.Tests/RegimeDetectorTests.cs`
Measured 2026-08-02 · 29 US instruments · 1970–2026 · suite green.

---

## The claim

From the 2026-08-02 Peter Tuchman interview (NYSE floor broker since 1985): the traditional
vocabulary — bull market, bear market, correction — has stopped meaning anything, because the market
now flips between those states far faster than it used to.

Of the three claims that interview produced, this is the only one that is both **falsifiable** and
**testable on data already on disk**. The other two (the market-on-close imbalance edge, the 15:00
and 15:30 drift) are blocked on a paid exchange feed and on US equity intraday respectively.

It is also the kind of claim nobody checks. It is repeated because it feels true, and the feeling is
real — this study ends up explaining where the feeling comes from.

**Falsifiable form:** the rate at which a mechanically-defined market regime reverses, in flips per
year, is higher in the post-COVID era than in equal-length earlier eras of the same instrument. If
the post-COVID rate sits inside the spread of earlier eras, the claim is false.

---

## Design, and the trap each decision avoids

**A mechanical label, not a journalist's.** His anecdote is about how the *press* used the words, and
press usage is exactly the thing that could have changed while the market did not. So a regime here
is a confirmed percentage swing: a run that ends when price reverses by θ from its running extreme.
θ = 10% is a "correction", θ = 20% is the textbook bull/bear definition. **Both are reported**, since
picking whichever one answers is the oldest trick there is.

**Eras balanced by length, not by calendar.** The post-COVID window is about six years; "before
COVID" is fifty. Comparing a six-year window against a fifty-year average compares a sample against
a mean, and short windows are more variable by construction — so the recent one would look extreme
however the market behaved. Instead each instrument's history is tiled *backwards from today* into
slices of exactly the post-COVID length, and the recent slice is ranked against its own siblings.

**A shuffled-returns surrogate per slice — the control that decides it.** A fixed-percentage detector
fires more often when volatility is higher, mechanically, with no change in market character at all.
Post-COVID volatility *is* higher. So "regimes are shorter" and "volatility went up" produce
identical output. The surrogate shuffles that slice's own daily returns and re-runs the same
detector: the slice's volatility is preserved, everything else destroyed. Reported as
**observed ÷ shuffled**. This is the same control that reduced the 60-day cycle claim to a
detector artifact.

**A volatility-scaled threshold, as a second reading.** θ is rescaled per slice by that slice's
volatility relative to the instrument's own median slice volatility, so the detector asks "a move
large relative to *this* era's noise" in every era.

**On the p-value.** Twenty-nine US equities and ETFs are not twenty-nine independent samples — SPY,
VTI, DIA and the sector funds are largely the same portfolio. A permutation that redrew each
instrument independently would count that correlation as evidence. The null used here draws **one
uniform per permutation and maps it to a slice index in every instrument simultaneously**, so a draw
moves the whole market to a different era together.

**No lookahead claim is made.** A swing pivot is only knowable θ later, so this segmentation could
not be traded as written. The question is descriptive — how long did regimes last — not "can you
trade the flip". The incomplete final leg of every slice is dropped.

---

## The detector was verified before the result was believed

A swing detector that quietly drifted would falsify a claim without anyone noticing, because the
output is a plausible-looking number either way. Run at θ = 20% over SPY's full history, it returns:

| pivot | close | what it is |
|---|---|---|
| 2000-03-24 | 153.56 | dot-com top |
| 2002-07-23 | 79.95 | dot-com bottom region |
| 2007-10-09 | 156.48 | pre-GFC top |
| 2009-03-09 | 68.11 | GFC bottom, to the day |
| 2018-12-24 | 234.34 | Christmas Eve low |
| 2020-02-19 | 338.34 | COVID top |
| 2020-03-23 | 222.95 | COVID bottom, to the day |
| 2022-01-03 | 477.71 | 2022 top |
| 2022-10-12 | 356.56 | 2022 bottom |

`RegimeDetectorTests` pins this against dates that are public record rather than against the
detector's own prior output, and separately checks that the pivot lands on the extreme rather than on
the bar that confirmed it — that difference *is* the duration measurement.

**Those tests found a bug.** The detector emits a seed pivot marking where the record becomes
measurable, and the study was initially counting it as a flip. On a monotonically rising slice that
seed is the first bar. It added a constant phantom flip to every slice and shrank every relative
difference toward zero — a bias pointing *at* the null, in a study whose answer is a null. A flip is
now counted as a transition between two regimes. It changed the headline numbers by about a
percentage point and changed no conclusion, but it is the class of error that would have.

---

## Results — recent era from 2020-03-01

29 instruments, 8 equal-length eras of ~1,600 trading days each, 20,000 permutations, 200 shuffles
per slice.

### θ = 10% (correction)

| era | window | ann vol | flips/yr | shuffled | obs/shuf | vol-scaled | mean duration |
|---|---|---|---|---|---|---|---|
| **0** | **2020-03…2026-07** | **25.5%** | **3.70** | 3.88 | 0.95 | 3.33 | **76** |
| 1 | 2013-10…2020-02 | 17.5% | 2.16 | 2.12 | 1.02 | 3.91 | 131 |
| 2 | 2007-05…2013-10 | 27.0% | 4.08 | 4.39 | 0.88 | 3.24 | 66 |
| 3 | 2000-12…2007-05 | 23.7% | 3.37 | 3.70 | 0.91 | 3.55 | 68 |
| 4 | 1994-08…2000-12 | 32.4% | 5.46 | 6.00 | 0.91 | 3.79 | 53 |
| 5 | 1988-04…1994-08 | 25.4% | 3.94 | 4.13 | 0.96 | 4.41 | 70 |
| 6 | 1981-11…1988-04 | 30.9% | 5.11 | 5.22 | 0.97 | 3.75 | 58 |
| 7 | 1975-07…1981-11 | 22.3% | 3.80 | 3.43 | 1.12 | 4.59 | 69 |

| arm | recent era vs its own earlier eras | p (floor 0.125) | fastest era in | mean rank of 8 |
|---|---|---|---|---|
| fixed threshold | +8.9% | 0.456 | 4/29 | 3.0 |
| vol-scaled threshold | −7.7% | 0.919 | 2/29 | 3.8 |
| vs shuffled surrogate | +0.7% | 0.584 | 2/29 | 3.4 |

### θ = 20% (textbook bull/bear)

| era | window | ann vol | flips/yr | shuffled | obs/shuf | vol-scaled | mean duration |
|---|---|---|---|---|---|---|---|
| **0** | **2020-03…2026-07** | **25.5%** | **0.91** | 1.06 | 0.83 | 0.84 | **224** |
| 1 | 2013-10…2020-02 | 17.5% | 0.43 | 0.47 | 0.85 | 1.05 | 381 |
| 2 | 2007-05…2013-10 | 27.0% | 0.91 | 1.32 | 0.61 | 0.69 | 160 |
| 3 | 2000-12…2007-05 | 23.7% | 0.93 | 1.02 | 0.92 | 1.05 | 142 |
| 4 | 1994-08…2000-12 | 32.4% | 1.42 | 1.66 | 0.75 | 0.80 | 222 |
| 5 | 1988-04…1994-08 | 25.4% | 0.95 | 1.06 | 0.79 | 1.03 | 237 |
| 6 | 1981-11…1988-04 | 30.9% | 1.23 | 1.49 | 0.80 | 0.92 | 249 |
| 7 | 1975-07…1981-11 | 22.3% | 0.94 | 0.90 | 1.10 | 1.13 | 252 |

| arm | recent era vs its own earlier eras | p (floor 0.125) | fastest era in | mean rank of 8 |
|---|---|---|---|---|
| fixed threshold | +10.0% | 0.546 | 9/29 | 2.8 |
| vol-scaled threshold | −6.1% | 0.709 | 4/29 | 3.3 |
| vs shuffled surrogate | −3.0% | 0.627 | 2/29 | 3.2 |

**The recent era ranks third of eight at both thresholds.** The 2007–2013 window (GFC) and the
1994–2000 window (Asia crisis, LTCM, dot-com) both flipped faster.

---

## Why the perception is real anyway

Look at **era 1**, which is exactly the pre-COVID window, 2013-10 to 2020-02:

- **17.5% annualised volatility** — the lowest of the eight eras, by six points.
- **2.16 flips/year at θ = 10%** — the lowest of the eight, against 3.4–5.5 elsewhere.
- **0.43 flips/year at θ = 20%** — half the next-lowest.
- **Mean regime duration 381 bars at θ = 20%** — the longest of the eight, against 142–252.

So a trader comparing the last six years against the six before them sees regimes lasting roughly
**half as long** (76 vs 131 bars at 10%; 224 vs 381 at 20%). That comparison is correct and the
feeling that follows from it is honestly earned. It is just anchored on the calmest stretch of the
last fifty years rather than on the norm. Measured against the norm, the post-COVID market is
unremarkable.

This is the useful output of the study: not "he is wrong" but **"the baseline moved, not the
market."**

---

## A side finding worth recording

**obs ÷ shuffled sits below 1.0 in almost every era** — 0.61 to 1.12, mostly 0.8–0.95. Real price
paths flip *slightly less often* than their own returns in a shuffled order. Regimes are marginally
more persistent than a same-volatility random walk.

It is small, it is present in every era including the recent one, and it moves in the *opposite*
direction to the claim. It is not queued as an edge: "trends persist a bit more than chance" is
already what the polarity finding says, measured better, and this is not a tradeable form of it.

---

## Robustness — moving the era boundary

The queued record flagged that the post-2020 window "contains one enormous volatility event". Re-run
with the recent era starting **2020-09-01**, after the crash and the recovery to new highs (33
instruments, 9 eras):

| arm | θ=10% | θ=20% |
|---|---|---|
| fixed threshold | −0.8% (p 0.647) | −7.2% (p 0.737) |
| vol-scaled threshold | +15.9% (p 0.111) | +33.5% (p 0.111) |
| vs shuffled surrogate | +14.5% (p 0.111) | +22.8% (p 0.111) |

**The sign flips on both the raw and the controlled arms when the boundary moves six months.** That
is decisive against the claim, not for it. The mechanism is visible: including Feb–Mar 2020 raises
the recent era's measured volatility to 25.5%, which raises the vol-scaled threshold, which
suppresses flips for the whole six years. A result that depends on which side of a boundary one
crash falls is a statement about the crash.

Note also that **p = 0.111 is the floor** in that variant (1 ÷ 9 slices). The recent-era arm cannot
reach conventional significance by construction. Where 0.111 appears, it means the recent era was the
most extreme of nine — the strongest statement this design can make — and it appears only on the arms
whose sign is not stable.

---

## Scope and caveats

- **US equities and ETFs only.** 29 instruments with at least four equal-length eras of history;
  Yahoo snapshots reaching 1970–1993 depending on the name. Crypto cannot answer a pre/post-2020
  question and was not included.
- **Not independent samples.** SPY, VTI, DIA, IWM, QQQ and nine sector funds are largely one
  portfolio. This is why the null moves every instrument to the same era together, and it is still
  the largest weakness in the design.
- **Survivorship.** The snapshot universe is survivors only. Instruments that stopped trading are
  absent, and their final regimes would be short ones.
- **Test count.** 2 thresholds × 3 arms = 6 tests in the primary run; at α = 0.05 expect 0.3 by
  chance. Nothing came close, so no correction is needed — worth saying because the same count would
  matter if something had.
- **Descriptive, not tradeable.** The confirmation lag is real and unremoved. This measures how long
  regimes lasted, not whether their ends were knowable in advance.
- **The strongest version not tested:** Tuchman may mean intraday character rather than multi-year
  regime length. That would need US equity intraday history, which is the same blocker sitting on
  `late-session-drift`.

---

Cross-references: `ALPHA_LEDGER.md` · `CYCLE_FINDINGS.md` (the same surrogate control, same class of
verdict) · `POLARITY_AND_GATE_FINDINGS.md` (persistence measured properly) ·
`AccessibleTrader.StrategyLab/Catalogue/edges.json` (`regime-labels-shortened-post-covid`).
