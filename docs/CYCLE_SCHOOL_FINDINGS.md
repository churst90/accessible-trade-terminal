# The cycle school: findings

Run 2026-10-18 by stage 2, exactly as frozen in `docs/CYCLE_SCHOOL_PREREG.md` and its dated
addendum (A.1 to A.4, committed before any outcome). The design is
`docs/CYCLE_LOW_INDICATOR_DESIGN.md`; Camel's method is `docs/CAMEL_METHOD.md`. The command is
`StrategyLab cycle-school --snapshots <absolute path to strategy-lab-data>`; every number below is
in `reports/cycle-school/cycle_school_full.json` (counts only: `cycle_school_counts.json`).

## 1. The answers, in plain words

- **Does Camel's checklist call lows better than chance and than random swing lows?** Yes, in all
  three classes: about 40% (crypto) and 54 to 55% (equities, gold) of its calls come within 15 bars
  after a low that a fixed rule later confirms, against 20 to 30% for the same calls at random times
  and 25 to 34% for random swing lows. **But it does no better on shuffled prices.** Run on 200
  random-walk versions of each chart, the same checklist scores 50 to 54%. Its precision is what its
  own definitions produce on a random walk: a call that needs price to have closed above its
  10-day average, broken a trendline and printed a thrust candle has already seen the rally that
  confirms the low. Under the pre-registered rule that is **no edge in every class**.
- **How early?** Its hits come a median of 3 to 5 bars after the low and 2 to 5 bars before the
  fixed rule confirms it, so "earlier than a lagged rule" holds, by a few days.
- **Does our capitulation score beat price alone?** No. Its alerts find lows far better than chance
  (66% in crypto and 79% in equities come within 5 bars before to 20 bars after a low, against 31%
  and 45% for chance timing), but the same alert built from the price family alone is as good or
  better (70% and 91%). Sentiment, positioning, on-chain and macro data added nothing detectable,
  which is what the pre-registration said to expect. Gold had too few alerts to test (12).
- **Which macro theses held?** None. The closest was C3 (a run of downward payroll revisions comes
  before deeper SPY drawdowns): 5 points deeper on average, raw p 0.0085, but Holm p 0.051 and a
  held-out difference of almost zero. C6, the alarm gate, had too few events (19) to test.
- **The trading result:** no edge, because no detector passed. The trade numbers are reported and
  cannot rescue a detector.

## 2. The data added first (step 0)

Fetched before any outcome, recorded in the addendum and in `docs/MACRO_DATA.md`:

- **JOLTS quits rate, JOLTS job openings and temporary help services jobs**, with their full ALFRED
  vintage history (vintages from 2010-08 and 2011-01). They became metrics M10 to M12 of the macro
  family, copying M9's transform.
- **The CFTC Traders in Financial Futures history** for the E-mini S&P 500, the Nasdaq-100 mini and
  CME Bitcoin, and the disaggregated report for COMEX gold, chosen by contract code and stamped at
  the release (Friday, later for holidays, the three shutdowns and the 2023 ION incident, and never
  before the history was first posted: 2010-12-03 for TFF). These fill positioning metric Q3.
- **What the old COT files held.** `sp500_cot` and `nasdaq_cot` are TFF leveraged funds, every week
  matched; `gold_cot` is disaggregated managed money, every week matched. `bitcoin_cot` is **three
  contracts mixed** (Cboe 73 weeks, CME 356, CME micro 24), because the downloader matched the name
  "BITCOIN" and kept the first row each week; `wti_cot` is a mixture too. The S&P and Nasdaq files
  also carry 2006 to 2010 values nobody could see until 2010-12-03, and every old file ignores
  holiday and shutdown delays.
- Not fetched, as instructed: Cboe put/call after 2019, and anything needing a key.

## 3. Counts (development era, before 2023)

| Class | G_D lows | G_W lows | Checklist calls | Alerts |
|---|---|---|---|---|
| Crypto (BTC, ETH) | 78 | 14 | 112 | 29 |
| Equities (4 ETFs) | 464 | 66 | 508 | 81 |
| Gold (GLD) | 86 | 12 | 114 | 12 |

- Pre-registered fallbacks, settled from the counts-only pass (run 1) before any outcome: the
  2-of-4 checklist was not needed; the alert at pctCAP ≥ 90 was triggered in crypto and gold.
- No call was excluded for a window past the data in the development era.

## 4. (a) Camel's checklist

Precision is the share of calls with a reference low 0 to 15 bars before the call. p-values are
one-sided; Holm is inside the A-primary family (9 tests).

| Class | Precision | N1 chance | N2 swing lows | N3 shuffled |
|---|---|---|---|---|
| Crypto | 0.402 | 0.196 | 0.253 | 0.502 (95th 0.584) |
| Equities | 0.539 | 0.284 | 0.338 | 0.536 (95th 0.563) |
| Gold | 0.553 | 0.299 | 0.322 | 0.522 (95th 0.600) |

| Test | Crypto p (Holm) | Equities p (Holm) | Gold p (Holm) |
|---|---|---|---|
| A1 vs N1 | 0.0001 (0.0009) | 0.0001 (0.0009) | 0.0001 (0.0009) |
| A2 vs N2 | 0.0001 (0.0009) | 0.0001 (0.0009) | 0.0001 (0.0009) |
| A3 vs N3 | 0.980 (0.980) | 0.478 (0.955) | 0.239 (0.716) |

Timing and false alarms of the primary checklist (development era):

| Class | Recall | Median lag from low | Median lead over confirm | False alarms a year |
|---|---|---|---|---|
| Crypto | 62% | 5 bars | 5 bars (87% early) | 4.0 |
| Equities | 59% | 3 bars | 2 bars (69% early) | 2.3 |
| Gold | 73% | 3 bars | 2 bars (78% early) | 2.8 |

The remaining requirements for "Supported", for the record: every asset beat its own N2 mean (BTC
0.39 vs 0.26, ETH 0.42 vs 0.25, SPY 0.56 vs 0.37, QQQ 0.49 vs 0.31, DIA 0.59 vs 0.36, IWM 0.53 vs
0.32, GLD 0.55 vs 0.32); the excess over N2 kept 126%, 101% and 75% at 25% noise; and the held-out
era (2023 on) kept the sign (crypto 0.43 vs N2 0.29 on 44 calls, equities 0.49 vs 0.31 on 65, gold
0.41 vs 0.28 on 22). None of that rescues A3.

**Secondary tests** (A-secondary family, 15 tests, Holm inside it):

- **A4, any one item** (crypto 0.341, equities 0.408, gold 0.368) and **A5, pivot + Bressert +
  trendline** (0.467, 0.536, 0.516) beat random swing lows in every class (Holm p 0.0015 to 0.013).
  They were not run against shuffled prices; the registry did not ask for it, and A3's lesson
  applies to them.
- **A6, does the window add anything?** Slightly: +0.04 precision in each class, Holm p 0.011
  (equities) and 0.013 (gold), not significant in crypto (0.54). The window does not hurt.
- **A7, the weekly checklist**, against weekly reference lows: equities 0.366 vs 0.264 for random
  weekly swing lows (Holm p 0.0018); gold 0.353 vs 0.254, 17 calls, Suggestive (Holm 0.83); crypto 9
  calls, not testable. A weekly pass beside a daily fail is Suggestive at most (prereg 10).
- **A8, the 80% rule.** After a big confirmed low makes a new high, the low held for the next 182
  days 74% of the time in equities (66 events), against 63% for lows that had merely survived as
  long (Holm p 0.0015). Crypto and gold had 11 events each (82% against 70% and 69%), not testable.
  Camel's 80% is close to the raw rate, and most of it is the base rate of a low that has already
  survived; the higher high adds about 11 points in equities.

**Verdict (a), in the prereg's words:** Camel's checklist does not find later-confirmed lows better
than shuffled-bar surrogates, in crypto, in equities or in gold, over the development era. **No edge
in all three classes.**

## 5. (b) The capitulation score

| Class | ALERT precision | N1 chance | ALERT_P (price only) | Difference, p (Holm) |
|---|---|---|---|---|
| Crypto | 0.655 (29) | 0.311 | 0.696 (23) | −0.040, 0.67 (1.00) |
| Equities | 0.790 (81) | 0.454 | 0.908 (65) | −0.118, 0.92 (1.00) |
| Gold | 0.917 (12) | 0.467 | 0.938 (16) | not testable |

- **B1, alerts vs chance:** crypto p 0.0001 (Holm 0.0015, Suggestive: 29 alerts), equities p 0.0001
  (Holm 0.0015). Gold has 12 alerts: not testable.
- **B2, does the extra data beat price alone?** No, in either testable class; the full score is
  worse on the point estimate. Per asset, ALERT beat ALERT_P on 0 of 2 crypto and 1 of 4 equity
  assets. At 25% price noise the difference stayed negative in all three seeds.
- **B3, are the full-score alerts earlier?** Read because B1 passed: crypto first on 7 of 13 shared
  lows (p 0.50), equities 18 of 27 (p 0.061, Holm 0.67). Not significant.
- **Timing:** a median alert comes 1 to 4 bars before the low and 10 to 17 bars before the fixed rule
  confirms it. Alerts are rare: they catch 13 to 24% of lows, with 0.06 to 0.6 false alarms a year.
- **Held-out:** crypto 9 alerts (not checkable); equities 17 alerts at 0.82 against chance 0.39 but
  below price alone (0.92); gold none.
- **Fallback, alert at pctCAP ≥ 90** (Suggestive whatever its p): crypto 45 alerts at 0.622 against
  chance 0.311 (p 0.0001), and no better than price alone (−0.006, p 0.55); gold 23 alerts at 0.826
  (p 0.0001), +0.011 over price alone (p 0.45).

Secondary tests (B-secondary family, 11 tests):

- **B4, CONFIRMED vs random bars with the same price conditions:** equities 0.923 against 0.553
  (Holm p 0.0011); crypto 0.591 against 0.541, not significant (22 calls, Suggestive); gold 10
  calls, not testable.
- **B5, does "low due" add anything?** No: crypto +0.055 (p 0.14), equities −0.019 (p 0.62). As the
  prereg says, any later version of the design drops it (and needs its own pre-registration).
- **B6, weekly-degree alerts vs chance:** equities 0.667 against 0.301 (Holm 0.0011); gold 0.733
  against 0.299 (Holm 0.0011, Suggestive: 15 alerts, 12 lows); crypto 0.659 against 0.356 (Holm
  0.068, Suggestive).
- **B7, sentiment at big lows:** equities, sentiment reached 80 near 28 points more of the weekly
  lows than of the other daily lows (Holm p 0.0011); crypto 10 weekly lows, not testable.

Descriptive output (prereg 5.3, not tests):

- **Effective number of families** (participation ratio of the family correlation matrix,
  development era): 3.0 to 3.5 on every asset, out of 4 to 7 families. The design note guessed two or
  three.
- **Leave one family out:** dropping positioning or macro *raises* the alert's precision
  in every class, and dropping sentiment raises it in crypto and equities (gold has none); for
  example equities go from 0.79 to 0.85 without positioning and 0.86 without sentiment.
  Descriptive only: no weight may change because of it.
- **Grades at CONFIRMED:** high, mid and low grades confirm at similar rates (equities 14 of 16 high,
  21 of 24 mid, 25 of 25 low hit). The grade does not order the lows.
- **When CAP is undefined:** about 250 to 270 development bars per asset have fewer than 3 families
  (the first year), and pctCAP then needs 251 more bars of warm-up.
- The family timeline per asset is in the JSON (`descriptive.family_timeline`): positioning starts
  2010-12 for the equity ETFs (the TFF history floor), 2010-05 for gold and 2020-04 for BTC.

**Verdict (b), in the prereg's words:** in crypto and in equities, the score finds lows only as well
as its price part; the added data adds nothing detectable. **No edge from the added data.** Gold:
not testable (the fallback says the same thing, Suggestive only).

## 6. (c) Camel's macro theses

Each test needs 20 events per group in the development era. Holm is over the 6 tests of family C.

| Test | Groups | Difference | p (Holm) | Held-out |
|---|---|---|---|---|
| C1-SPY | 64 / 101 | −0.7 pt drawdown | 0.21 (1.00) | 3 / 9, same sign, not checkable |
| C1-BTC | 21 / 38 | −1.9 pt | 0.36 (1.00) | 3 / 9, same sign, not checkable |
| C2 | 141 / 135 | −0.006 pt in DGS10 | 0.31 (1.00) | 7 / 16, opposite sign |
| C3 | 77 / 282 | −5.1 pt drawdown | 0.0085 (0.051) | 24 / 10, same sign, −0.3 pt |
| C5 | 182 meetings | +3.3 pt accuracy | 0.24 (1.00) | 31, same sign, +6.5 pt |
| C6 | 19 / 38 | −3.5 pt | not testable | 2 / 5 |

- **C1, a strong jobs print with the 2-year yield falling.** Drawdowns over the next 60 bars were
  a little deeper (SPY and BTC), nowhere near significant. Descriptive: the 2-year rose about the
  same over the next 60 observations whether it had fallen or risen on the day (+0.015 both, SPY
  events), so the bonds were not "right" either.
- **C2, hot CPI with the 10-year falling.** The 10-year kept falling by 0.6 basis points more than
  after hot CPI with the 10-year rising; nothing. TLT's 60-bar return was +1.5% against +1.9%.
- **C3, downward payroll revisions.** The strongest result: SPY's 120-bar drawdown after a run of
  downward revisions averaged 5 points deeper, but it misses Holm (0.051) and the held-out difference
  is almost zero. Descriptive: an NBER peak followed within 12 months after 6 of the 77 signal days
  and 30 of the 282 other days.
- **C4, the NBER is late** (descriptive). Six announcements fall in SPY's history; the lags were about 4 to
  20 months. At every peak announcement SPY was already 9 to 45% above its prior-year low; at the
  trough announcements it was 12 to 32% above. GLD and BTC are in the JSON.
- **C5, the 3-month bill against "same as last meeting".** The bill was right 84.6% of the time and
  persistence 81.3%; the discordant meetings split 28 to 22, p 0.24. Descriptive: with the 2-year
  yield in place of the bill (his words) accuracy is 46.7%, far worse than persistence; on the 24
  unscheduled meetings the bill was right 70.8% against 66.7%.
- **C6, the alarm gate.** Only 19 gate days, below the 20 needed. Descriptive: gate days were
  followed by 11.0% mean drawdowns against 7.6% for the same days without the alarm, and 6.0% after a
  first close below the 200-day average. BAA10Y is investment grade, not high yield.

**Verdict (c):** no thesis has support. C1-SPY, C1-BTC, C2, C3 and C5 fail after Holm (and C2's
held-out sign disagrees); C6 is not testable.

## 7. (d) The trading translation

Enter at the next open; stop under the low; exit on a close below the 10-day average after 5 bars,
or at 60 bars; 15 bps a side. R is net of costs, from the initial risk.

| Test, class | Trades | Mean net R | Control | p (Holm) |
|---|---|---|---|---|
| D1 CONFIRMED, crypto | 22 | +0.945 | +0.066 | 0.040 (0.24) |
| D2 checklist, crypto | 112 | +1.173 | +0.541 | 0.069 (0.34) |
| D1 CONFIRMED, equities | 65 | +0.055 | −0.048 | 0.16 (0.48) |
| D2 checklist, equities | 508 | −0.025 | −0.088 | 0.088 (0.35) |
| D1 CONFIRMED, gold | 10 | +0.396 | +0.040 | not testable |
| D2 checklist, gold | 114 | −0.014 | −0.062 | 0.33 (0.67) |

- Gross R, for the record: +0.966, +1.199, +0.100, +0.060, +0.463, +0.093.
- Reference rows: the lab's standard harness (1 ATR stop, 2R target, 20 bars) on the same entries
  gives −0.149, +0.036, +0.059, −0.099, +0.520 and +0.008; CONFIRMED built on the price-only score
  gives +0.911 (crypto, 21), −0.061 (equities, 115) and −0.143 (gold, 23).
- Held-out (net R against its control): crypto D1 +0.24 vs +0.17 (5 trades), D2 +0.15 vs +0.18;
  equities D1 +0.31 vs +0.24, D2 +0.17 vs +0.11; gold D2 +0.52 vs +0.54.
- At 25% noise the development excess over the control kept between −84% and +57%.

**Verdict (d):** no edge (the detector failed), in every class. Nothing passed Holm either.

## 8. Held-out signs, together

The held-out era (2023 to the last bar) was read once, after the development tests:

- (a) checklist minus random swing lows: positive in all three classes.
- (b) alerts above chance in crypto and equities; above price alone in neither checkable class
  (crypto had 9 alerts, gold none).
- (c) same sign as development for C1-SPY, C1-BTC, C3, C5; opposite for C2.
- (d) above the control for D1 crypto, D1 and D2 equities; below it for D2 crypto and D2 gold.

None of these can change a verdict: the development-era tests had already decided each one.

## 9. Deviations and readings, decided without looking at outcomes

All of these were written into the harness committed before the first run (bb3c8695), except the
two fixes in the next section.

- **Weekly bars** are usable from their last daily bar when that bar is the calendar end (Sunday
  for crypto, Friday for equities), otherwise from the first bar of the next week. That is one bar
  later than the prereg allows in a holiday week, never earlier, and it keeps every weekly input the
  same on a truncated history. A weekly checklist call maps to that bar.
- **Scoring:** a call is also excluded if a bar in its window is a candidate low whose fate is still
  open at the last bar. For lag and lead, the hit is the reference low nearest the call.
- **N1** shifts within the era's scorable bars (bars where a call could be scored), not every bar of
  the era.
- **N2 for CONFIRMED** groups its eligible bars by anchor, like swing lows for the checklist.
- **Minimum counts for difference tests** use the smaller of the two detectors' calls.
- **Holm:** a not-testable test counts as p = 1, so each family keeps its registered size; the
  fallback tests join the primary family they belong to (they "count as 3 extra tests").
- **CONFIRMED's anchor** on a tie is the latest bar with that low.
- **"First firing in any 20 bars" (C6)** reads as ALERT's refractory rule: a day counts unless a
  counted day of its kind is in the 20 bars before it.
- **C1 and C3 on a day that printed two months** (October and November 2025 payrolls on
  2025-12-16): the newest month is the release.
- **C6's alarm** uses the expanding percentile rank of the BAA10Y change (≥ 90), the design note's
  own percentile.
- **A8:** candidates are dated by their low; an IN label given to a candidate in the permutation that
  did not survive its delay scores 0.
- **B7** uses only lows where the sentiment family was defined somewhere in the window.
- **Trade control:** the same number of signals per asset as the detector (those with a full
  60-bar window), drawn without replacement; the control's stop uses the 10 bars before the signal.
- **Noise:** each bar's open, high and low move by the same factor as its close, so candle shapes
  (the thrust and engulfing items) survive. The lab's `OnChainRobustness.Perturb` sets open = close
  and would have voided them.
- **Bitstamp's last bar** (2026-06-15, fetched at 03:58 UTC that day) is not a daily bar and was
  dropped.

## 10. Runs counted

| Run | What | Why |
|---|---|---|
| 1 | counts only | settles the fallbacks before any outcome |
| 2 | full | the registered run |
| 3 | full | fix: a test with under 15 reference lows had been called not testable |
| 4 | full | fix: C5 counted 10 FOMC meetings that have not happened yet |
| 5 | full | adds C5's two pre-registered descriptive rows, which run 4 omitted |

- **Run 3** changed three statuses (B6 crypto, B6 gold and A7 gold went from not testable to
  Suggestive) and the B-secondary Holm values (B5 crypto 1.00 to 0.86). No verdict changed.
- **Run 4** changed only C5's held-out number: −0.171 on 41 meetings in run 3, +0.065 on 31 meetings
  after the fix. `events_fomc.json` lists scheduled meetings into 2027, and those had been read as
  holds. No verdict changed.
- Runs 2 to 5 read the held-out era each time with the same code; apart from C5's held-out (run 4)
  every held-out number is identical across them.
- **variantsTried: 77** = 56 registered tests + 6 fallback tests + 7 leave-one-family-out alerts +
  3 noise seeds + 2 C5 descriptive rows + 3 trade reference rows (standard harness, CAP_P
  CONFIRMED, gross). No other variant was run.

## 11. Data proof

All 63 input files were hashed with sha256 when first opened and again at the end of each run;
none changed (`inputs` in the JSON). The fetch before the study added files only: a hash of all
1,125 files in `strategy-lab-data` before and after it showed no existing file changed except
`macro/MACRO_MANIFEST.json`.

## 12. What this does and does not show

- **The checklist is not useless; it is not evidence.** Its calls are a fair description of where
  lows are confirmed, but a random walk gets the same description from the same definitions. A
  test of Camel's own calls, with his discretion, belongs to the dated-call ledger
  (`docs/CAMEL_TRACK_RECORD.md`), not here; "it needs his indicator" is a forbidden reframing.
- **The score's alerts are good and the reason is price.** Equity alerts within 5 bars before to 20
  after a low 79% of the time is high, but the price family alone does 91%. Three to three and a
  half effective families is more than guessed, yet the extra ones dilute rather than add.
- **Small classes.** Gold is one asset; crypto is two, and ETH starts in 2017. The equity ETFs move
  together, so pooled counts overstate independent evidence; the per-asset rows are in the JSON.
- **Investment grade, not high yield.** C6 uses BAA10Y, because ICE spreads exist only from 2023.
- **Not tested, on purpose:** cycle lengths, low spacing and translation (all shown before to be
  detector artifacts), pyramiding, size-to-zero and shorts.
