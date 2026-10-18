# The cycle school: pre-registration

> **Results (2026-10-18):** `docs/CYCLE_SCHOOL_FINDINGS.md`. Nothing below was changed after them.

Written 2026-10-18, before any result existed. This is stage 1 of 2. Stage 2 (a different agent)
builds and runs what is written here. **This file was committed before any forward return, hit
rate, outcome or event count was computed for any rule in it. The commit time is the proof.**

What it tests, each school kept separate (Cody, 2026-10-18):

- **(a)** Camel Finance's real-time low-calling checklist, against lows confirmed later by a fixed
  rule (`docs/CAMEL_METHOD.md` sections 3 and 4).
- **(b)** Our own capitulation score as a low detector, against the same lows and against a
  price-only version of itself (`docs/CYCLE_LOW_INDICATOR_DESIGN.md`).
- **(c)** Camel's macro theses as conditional tests (`CAMEL_METHOD.md` section 7).
- **(d)** A trading version of (a) and (b), whose result cannot rescue a failed (a) or (b).

Nothing from Market Cipher or from Tim's structure method is an input anywhere.

## 1. What I had already seen when writing this

Stage 2 must read this, because it limits how blind the test is.

- **No outcome of any rule below was computed or looked at.** The only data work was listing the
  files in `strategy-lab-data/` and `strategy-lab-data/macro/`, reading their header rows, and
  reading the first and last dates of the price and cross-series files.
- **Findings I had read**, all of which set priors:
  - cycle lengths are a swing-detector artifact; the real data landed in the claimed band 10 to 17%
    of the time; cycle failure predicted nothing (`docs/CYCLE_FINDINGS.md`);
  - translation is momentum; "right translation means a faster fall" is falsified
    (`docs/TRANSLATION_FINDINGS.md`);
  - ledger rows 10 to 13 and 18: on-chain valuation is exposure to the bottom, COT and crowding are
    nulls, macro release days are a null for same-day returns, Fear and Greed is redundant with an
    oscillator buy (`docs/ALPHA_LEDGER.md`);
  - the setup grader's top grade almost never fired, so its headline tests could not run
    (`docs/SETUP_GRADER_FINDINGS.md` as summarised in the ledger, row 29).
- **Prices I had seen**: the August 2026 table in `docs/CAMEL_TRACK_RECORD.md` (Bitcoin's 16 Aug low
  at 62,653 and the month's moves). That is held-out-era data and no choice below uses it. I also
  know the general history of markets (the 2008 and 2020 crashes, for example) the way anyone does.
  No threshold was chosen to fit any episode.
- **NBER announcement lags** (4 to 21 months since 1980) were read in `docs/MACRO_DATA.md`.

Where a number below comes from Camel, it says so. Every other number is a free choice made now.

## 2. The trap this design has to avoid, stated first

The lab has already shown that a "cycle" of N days is what a swing detector produces on any
price path, including a shuffled one, at a spacing set by the detector's own parameters. Lows
picked by hand on a finished chart are spaced the claimed way because that is how they were picked.

So this pre-registration:

1. **never scores a cycle length, a low-to-low spacing, an in-window rate or a nesting count**;
2. scores only **timing relative to lows confirmed by one fixed causal rule** (section 3.3), which
   is usable only from the bar on which it confirms;
3. uses Camel's timing window **only as a filter**, and tests whether the filter adds anything
   (test A6) against a null that keeps everything else the same;
4. treats our design's "low due" rule as a minimum spacing, and tests it by switching it off (B5);
5. runs every price-only detector on **shuffled-bar surrogates** too (A3), so that whatever
   precision the definitions alone produce on a random walk is measured and subtracted.

## 3. Shared definitions

### 3.1 Bars, eras, data end

- Daily bars only, from the files in section 3.2. Weekly bars are built from daily bars in the lab
  harness: crypto weeks run Monday 00:00 UTC to Sunday; equity weeks end on the week's last trading
  day. A daily bar may use only weeks whose last daily bar is on or before it. Never use
  `ConditionLeaf.Timeframe` or the app's higher-timeframe service.
- Everything is decided at bar *i*'s close; any action is at bar *i*+1.
- **Development era**: call, alert or event bars before 2023-01-01. **All confirmatory tests run
  here.** Outcomes and reference lows for development calls may use bars after 2022-12-31; no choice
  is made from them.
- **Held-out era**: 2023-01-01 to the last bar on disk on 2026-10-18 (crypto 2026-06-15, equities
  and GLD 2026-07-24). It is read **once**, after all development tests, for sign agreement only. A
  later price refresh is a new, separate held-out period and is reported separately.
- Calls whose scoring window runs past the last bar on disk are excluded and counted.

### 3.2 Universe and classes

Classes are never pooled together.

| Class | Assets (daily bar file) | First bar |
|---|---|---|
| Crypto | BTC (`bitstamp_BTC_USDT_1d.json`), ETH (`bitstamp_ETH_USDT_1d.json`) | 2011-08-18, 2017-08-16 |
| US equities | SPY, QQQ, DIA, IWM (`yahoo_*_1d.json`) | 1993-01-29, 1999-03-10, 1998-01-20, 2000-05-26 |
| Gold | GLD (`yahoo_GLD_1d.json`) | 2004-11-18 |

BTC's longer CoinMetrics history (from 2010-07) is used only to warm up the on-chain percentiles;
the bar series is Bitstamp's. The four equity ETFs move together closely, so pooled counts overstate
the independent evidence; the per-asset breakdown (section 8) is required for that reason. Gold is a
single asset.

### 3.3 The reference low (the "confirmed cycle low" everything is scored against)

**Daily degree, G_D** (primary). Bar *j* is a reference low if:

1. low(*j*) is the lowest low of bars *j*−20 to *j*; and
2. before any later bar has a low below low(*j*), some later bar closes at or above
   low(*j*) + 3 × ATR(14)(*j*).

Its **confirmation bar** c(*j*) is the first bar that meets condition 2. **It is usable only from
c(*j*)**: any rule that uses reference lows (Camel's window, our "low due") may use only those with
c(*j*) ≤ *i*.

**Weekly degree, G_W** (secondary). The same rule with the lowest low of bars *j*−130 to *j* and a
close at least 6 × ATR(14)(*j*) above it.

ATR(14) is Wilder's, on daily bars. Neither rule was tuned; the lengths are free choices made now.

### 3.4 Scoring a detector

A detector produces **calls** at bars *t*. For each call:

- **Post-low detectors** (Camel's checklist, our CONFIRMED): the call is a **hit** if some reference
  low *j* satisfies *t* − 15 ≤ *j* ≤ *t* (daily degree), or the low falls at most 15 weeks before
  *t* (weekly degree; 7 bars a week for crypto, 5 for the others).
- **Pre-low detectors** (our ALERT): a **hit** if some reference low *j* satisfies
  *t* − 5 ≤ *j* ≤ *t* + 20 (daily degree), or between 5 weeks before and 20 weeks after *t*
  (weekly degree).
- A call that is not a hit is a **false alarm**.

**Statistics**, pooled within a class (total hits over total calls across its assets):

- **precision** = hits ÷ calls (1 − the false-alarm rate). **This is the primary statistic.** The
  nulls below keep the number of calls, so precision comparisons are fair;
- **recall** = the share of reference lows with at least one call in their window;
- **lag from the low** = *t* − *j*, and **lead over the confirmation** = c(*j*) − *t*, for hits
  (positive lead means the call came before the fixed rule confirmed the low); the median and the
  share with lead > 0 are reported;
- false alarms per year.

### 3.5 Nulls

- **N1, chance timing.** Per asset and era, shift the whole call series by one uniform random offset
  between 60 bars and (era length − 60) bars, wrapping within the era, and rescore. This keeps the
  number of calls and their spacing and destroys their link to price. 10,000 draws.
- **N2, price-only baseline: random swing lows.** Per asset and era, draw the same number of calls as
  the detector made, by choosing swing lows uniformly (each at most once) and, for each, a call bar
  uniformly among its eligible bars. For the checklist, a swing low and its eligible bars are as in
  item SW of section 4.1 (call bars *j*+2 to *j*+10). For CONFIRMED, eligible bars are those where its
  price conditions 1 and 4 hold (section 5.1). This asks whether the extra conditions pick better
  lows than the bare price pattern does. 10,000 draws.
- **N3, shuffled-bar surrogates** (price-only detectors). Per asset, development era only, shuffle the
  daily bars as whole tuples (close-to-close log return, open ÷ previous close, high ÷ close, low ÷
  close), rebuild the price path, recompute the reference lows and the detector on the surrogate, and
  score it. 200 surrogates.

Seeds: `StableSeed.From("cycle-school", test id, asset, draw)`. Never `string.GetHashCode()`.

### 3.6 Significance and correction

- Against N1 and N2: one-sided p = (1 + number of draws with pooled precision ≥ observed) ÷
  (1 + 10,000). Against N3: (1 + number ≥ observed) ÷ 201.
- A difference between two detectors' precisions (A6, B2, B5): block bootstrap over calendar years
  within the class and era (resample years with replacement; all of a year's calls on all of the
  class's assets move together), 10,000 resamples; one-sided p = (1 + number of resamples with
  difference ≤ 0) ÷ (1 + 10,000).
- Calls close in time share reference lows, so these p-values are somewhat optimistic. That is one
  reason the held-out check and the noise check are part of every verdict.
- **Holm–Bonferroni at α = 0.05, one-sided, inside each family** (section 9). No claim may combine
  families.

### 3.7 Minimum events

- **Confirmatory**: at least **30 calls and 30 reference lows** in the class in the development era.
  With 15 to 29 calls, a test is run but reported as **Suggestive only**. Below 15 it is **not
  testable**: the count is reported and nothing else.
- **Held-out check**: at least **10 calls** in the class in the held-out era; below that it is "not
  checkable", which caps the verdict at Suggestive.
- The weekly-degree tests (A7, B6) and gold are expected to be short of these numbers. That is
  stated now so a "not testable" there is not a surprise.
- **The counts were not looked at before freezing this**, on Cody's instruction (no event counts).
  The setup grader's top grade turned out never to fire (ledger row 29), so two fallbacks are
  pre-registered: if the primary checklist has fewer than 30 development calls in a class, the
  version with 2 of 4 items (section 4.2) is also run and reported as **Suggestive whatever its p**;
  if the primary ALERT has fewer than 30, the version at pctCAP ≥ 90 is run on the same terms. Each
  fallback counts as 3 extra tests in the budget.

### 3.8 Costs

For the trading tests only: 15 bps per side (the engine's 10 bps commission plus 5 bps slippage),
converted to R per position. The **net figure is primary**; gross is reported beside it.

## 4. Test (a): Camel's low-calling checklist

### 4.1 The items, frozen

All on daily bars, decided at the close of bar *i*. *j* is the swing-low bar.

- **SW, swing low.** There is a bar *j* with *i*−10 ≤ *j* ≤ *i*−2 whose low is the lowest low of
  bars *j*−10 to *i*. If several qualify, *j* is the latest.
- **WIN, window open** (Camel's timing window, used as a filter). Take the most recent reference low
  (section 3.3) whose low bar is before *j* and whose confirmation bar is on or before *i*. The number
  of bars from that low to bar *j* is at least 0.9 × L. With no such reference low yet, WIN is false. L is his
  stated average length: BTC 60, ETH 60 (he gives no ETH number and says Bitcoin's phase governs the
  alts), SPY, QQQ, DIA and IWM 40, GLD 25. The window has no upper end, because he takes lows "through
  the window" and says lows can overrun it.
- **SMA.** close(*i*) > SMA(10)(*i*).
- **DSS, a Bressert cross down the bottom.** On some bar *b* with *j*−3 ≤ *b* ≤ *i*, the DSS line
  crosses above its trigger (DSS(*b*) > Trigger(*b*) and DSS(*b*−1) ≤ Trigger(*b*−1)), with the
  smaller of DSS(*b*−1) and Trigger(*b*−1) below 20. DSS Bressert (10, 9, 5) from
  `DssBressertProvider`.
- **TL, trendline break** (a proxy for his hand-drawn line). Find the pivot highs (the highest high
  of 3 bars either side) between bars *j*−120 and *j*−1. Step back from the latest to find the most
  recent pair H1 (earlier) and H2 (later) with high(H2) < high(H1). TL holds if close(*i*) is above
  the straight line through those two highs, extended to bar *i*. With no such pair, TL is false.
- **THR, thrust candle.** On some bar *b* with *j* ≤ *b* ≤ *i*: close > open, true range ≥ 1.5 ×
  ATR(14)(*b*−1) and the close in the top 25% of the bar's range; or a bullish engulfing (a green
  body that covers the whole body of a red bar *b*−1).

Not computable and left out: his indicator's label (closed source). The Parabolic SAR flip is
secondary in his own words and is not used. Every close is a daily close on a daily bar, which is
his "close on the cycle's own timeframe" for the daily degree.

### 4.2 The checklist call

- **Primary (A-P)**: SW and WIN and **at least 3 of** {SMA, DSS, TL, THR}. This is "two out of six
  was not enough" (28 Jul) and "all three wanted" (16 Sep) made into one count.
- **Variant "any one" (A-V1)**: SW and WIN and at least 1 of the four (17 Sep; 2023).
- **Variant "pivot, Bressert, trendline" (A-V2)**: SW and WIN and DSS and TL (16 Sep, 13 Oct).
- **Fallback (only under section 3.7)**: SW and WIN and at least 2 of the four.
- **No-window version (A-NW)**: A-P without WIN, used only in test A6.

**One call per swing low**: the call fires on the first bar *i* the condition holds for a given *j*;
no further call is made for that *j*. A new call needs a new swing low.

### 4.3 The weekly-degree checklist (A7 only)

The same items on completed weekly bars, scored against G_W: SW is a weekly bar *j* between 2 and 10
weeks back that is the lowest low of weeks *j*−10 to now; WIN uses weekly L of BTC 30 weeks (his
2026 "average of about 30 or 31"), ETH 30, the equity ETFs 24 (2023's 22 to 26), GLD 26 ("a week 26
on average", 24 Sep), counted from the most recent G_W low before *j* that is confirmed by the
week's end; SMA is SMA(10) of
weekly closes; DSS (10, 9, 5) on weekly bars; TL and THR as above on weekly bars. The call is mapped
to the last daily bar of its week.

### 4.4 The 80% rule (A8)

Camel, 13 Oct: if a confirmed daily low near a candidate bigger low "goes on to make new highs",
"about 80% probability" the bigger low is in.

- **Candidate**: a G_D reference low *j* whose low is also the lowest low of bars *j*−130 to *j*.
- **IN**: after c(*j*), a close above the highest high between the previous G_D reference low and
  *j* comes before any close below low(*j*). Its bar is *t_in*.
- **Outcome**: no bar trades below low(*j*) within 182 calendar days after *t_in*.
- **Survival-matched null**: for each IN event with delay *d* = *t_in* − c(*j*), take every candidate
  in the same class and era that had no close below its low for at least *d* bars after its own
  confirmation, and measure the same outcome from its c + *d*. The null rate is the mean over those.
- **Statistic**: the share of IN events with the outcome, minus the matched null rate, > 0. p by
  permuting the IN label among candidates within asset and era, 10,000 draws.
- His "not in" branch is not tested: a close below the confirmed low already is a lower low, so it is
  true by construction. His 80% is printed beside the result as a reference, not as the test.

### 4.5 The (a) tests

| Id | Test (development era, per class) | Family |
|---|---|---|
| A1 | precision of A-P > N1 | A-primary |
| A2 | precision of A-P > N2 (random swing lows) | A-primary |
| A3 | precision of A-P > the 95th percentile of its N3 surrogate precisions | A-primary |
| A4 | precision of A-V1 > N2 | A-secondary |
| A5 | precision of A-V2 > N2 | A-secondary |
| A6 | precision of A-P − precision of A-NW > 0 (does the window add anything?) | A-secondary |
| A7 | weekly-degree checklist precision > N2 against G_W | A-secondary |
| A8 | the 80% rule, section 4.4 | A-secondary |

Three classes each: 9 primary tests, 15 secondary.

**"Camel's checklist calls lows in real time" (Supported) in a class needs all of:**

1. A1, A2 and A3 significant after Holm in A-primary;
2. median lead over the confirmation > 0 (the calls come before the fixed rule confirms);
3. held-out: precision of A-P minus the mean N2 precision > 0, with at least 10 calls;
4. for crypto and equities, the development-era precision minus the N2 mean is positive on more
   than half of the class's assets;
5. at least 50% of the development excess over N2 is kept at 25% noise (section 8).

**Fragile**: 1 to 4 hold, 5 fails. **Suggestive**: A2 nominally significant (p < 0.05) but not after
Holm, or counts in the 15 to 29 band, or the held-out check not checkable. **No edge**: anything
else.

## 5. Test (b): the capitulation score

The score, ALERT, CONFIRMED and grade are exactly as in `docs/CYCLE_LOW_INDICATOR_DESIGN.md`
sections 3 to 5, including every metric, sign, file, known-from rule and staleness limit. They are
frozen by this reference. A short restatement:

### 5.1 Restatement

- Each metric is an expanding-window percentile of its oriented value (needs 252 daily, 104 weekly or
  36 monthly values). Family score = mean of its defined metrics. CAP = mean of defined family
  scores, needing at least 3 families. CAP_P = the price family alone.
- pctCAP and pctCAP_P = each score's own expanding percentile (needs 252 values).
- **ALERT**: pctCAP ≥ 95; a low is due (bars since the last known G_D low ≥ 0.75 × the median of
  that asset's earlier known G_D spacings, needing 4; treated as met with fewer); no ALERT in the
  previous 20 bars. **ALERT_P** is the same on pctCAP_P. **ALERT_ND** is ALERT without the due rule.
- **CONFIRMED** on bar *i*, anchor *j* = the lowest low among bars *i*−10 to *i*−1: (1) low(*j*) is
  the lowest low of bars *j*−20 to *i*; (2) max pctCAP over *j*−5 to *j* ≥ 90; (3) pctCAP(*i*) ≤ that
  maximum over *j*−5 to *i*, minus 15; (4) close(*i*) > high(*j*); (5) first such bar for *j*.

### 5.2 The (b) tests

| Id | Test (development era, per class) | Family |
|---|---|---|
| B1 | ALERT precision > N1 | B-primary |
| B2 | ALERT precision − ALERT_P precision > 0 (does the extra data beat price alone?) | B-primary |
| B3 | earlier: for reference lows hit by both ALERT and ALERT_P, the first ALERT comes before the first ALERT_P; sign test on the differences (ties dropped), one-sided | B-primary |
| B4 | CONFIRMED precision > N2 (random bars where its price conditions hold) | B-secondary |
| B5 | ALERT precision − ALERT_ND precision > 0 (does "low due" add anything?) | B-secondary |
| B6 | weekly degree: ALERT precision > N1 against G_W | B-secondary |
| B7 | sentiment matches big lows: the share of G_W lows with the sentiment family score ≥ 80 somewhere in bars *j*−10 to *j*+5, minus the same share at G_D lows that are not G_W lows, > 0; p by permuting the two labels within asset, 10,000 draws | B-secondary (equities and crypto only; gold has no sentiment family) |

B1 to B6 run for three classes; B7 for two. 9 primary tests, 11 secondary.

**"The score finds lows, and the extra data earns its place" (Supported) in a class needs all of:**

1. B1 and B2 significant after Holm in B-primary;
2. held-out: ALERT precision > its N1 mean, and ALERT precision > ALERT_P precision, with at least 10
   alerts;
3. for crypto and equities, ALERT precision − ALERT_P precision > 0 on more than half of the assets;
4. at least 50% of the development B2 difference is kept at 25% price noise (macro inputs are not
   perturbed).

**B3 is read only if B1 passes**: being earlier is worth nothing if the alerts are not better than
chance.

**Stated now, as the expected result**: B1 may pass and B2 fail. That would mean the score finds
lows about as well as its price family alone, and that sentiment, positioning, on-chain and macro
data add nothing detectable (section 6 of the design note gives the reasons to expect it). The
write-up must then say "No edge from the added data", whatever B1 says.

**Fragile**, **Suggestive** and **No edge** as in section 4.5, with B2 in place of A2.

### 5.3 Pre-registered descriptive output for (b)

Reported, not tested:

- the family-availability timeline per asset (which families were defined, from when);
- the development-era Spearman correlation matrix of the family scores per asset, and the effective
  number of families (the participation ratio of its eigenvalues);
- leave-one-family-out ALERT precision, per class;
- the grade distribution at CONFIRMED lows, and precision by grade;
- the count of bars where CAP was undefined, and why.

None of these may be used to change a weight, a sign, a threshold or a family.

## 6. Test (c): Camel's macro theses

Each is a small, fixed set of events with a matched null. All macro reads follow the design note's
known-from rules and `docs/MACRO_DATA.md`'s traps: revised series from their ALFRED vintages, the
2025 shutdown dates from the vintages (never a schedule rule), NBER dates from
`nber_turning_points.csv` (never USREC), Moody's BAA in place of the ICE high-yield spread.

Outcomes are measured from the close of the event day. The equity and BTC bars used are those whose
close is after the 8:30 am Eastern release.

### C1. The bond market disagrees with a strong payrolls first print

Camel's thesis (CAMEL_METHOD 7.2): bonds see labour weakness the headline hides, so a strong print
with falling yields means trouble ahead for risk assets.

- **Events**: every payrolls release day *d* = `first_release_date` of a period in
  `PAYEMS_first_release.csv` with `first_is_initial_print` = true.
- **Headline change** h(*d*) = the first print of month *m* minus the value of month *m*−1 in the
  vintage in force on *d*.
- **Strong**: h(*d*) > the median of h over the previous 12 release days. (There are no consensus
  forecasts in the lab, so "strong" is against the recent trend.)
- **Bond reaction**: r(*d*) = DGS2 on *d* minus DGS2 on its previous observation. **Disagree** =
  strong and r < 0. **Agree** = strong and r > 0. r = 0 or missing is excluded.
- **Outcome**: the asset's maximum drawdown over the next 60 bars: the lowest low of bars *d*+1 to
  *d*+60 ÷ close(*d*), minus 1.
- **Statistic**: mean drawdown (Disagree) − mean drawdown (Agree) < 0, one-sided.
- **Null**: permute the Disagree and Agree labels among strong days within the same calendar
  decade, 10,000 draws.
- **Two tests**: C1-SPY (from 1993) and C1-BTC (from 2011-08).
- Descriptive beside it: the 60-day forward change in DGS2 for each group (were the bonds right?).

### C2. Hot CPI with falling yields means yields keep falling

Camel, 15 Oct: "CPI comes in hot and yields went down, not up."

- **Events**: CPI release days from `CPIAUCSL_first_release.csv` (`first_is_initial_print` = true;
  periods whose first value is missing, such as October 2025, are skipped).
- **First-print change** π(*d*) = the first print of month *m* ÷ the value of month *m*−1 in the
  vintage in force on *d*, minus 1. **Hot**: π(*d*) > the median of π over the previous 12 releases.
- **Reaction**: DGS10 on *d* minus its previous observation. **Disagree** = hot and down; **Agree** =
  hot and up.
- **Outcome**: DGS10 at its 60th observation after *d*, minus DGS10 on *d*.
- **Statistic**: mean outcome (Disagree) − mean outcome (Agree) < 0. Null as in C1.
- Descriptive: TLT's 60-bar return for each group (TLT from 2002).

### C3. Downward payroll revisions mean the headline overstates the labour market

- **Events**: every payrolls release day *d* as in C1.
- **Net revision** ρ(*d*) = the sum over months *m*−1 and *m*−2 of [their value in the vintage in force
  on *d*] − [their value in the vintage in force the day before *d*].
- **Signal**: ρ(*d*) + ρ(previous release) + ρ(the one before) ≤ the 20th percentile of that
  three-release sum over all earlier release days (needs 60 earlier values).
- **Outcome**: SPY's maximum drawdown over the next 120 bars from close(*d*).
- **Statistic**: mean outcome (signal days) − mean outcome (other release days) < 0.
- **Null**: circular shift of the signal labels along the sequence of release days, by a uniform
  offset of at least 6 releases, 10,000 draws (the signal and the 120-bar outcome both overlap
  across neighbouring releases).
- Descriptive only: whether an NBER peak month falls within 12 months after *d* (counts only; there
  are too few recessions for a test).

### C4. The NBER is late (descriptive, no test)

For every NBER announcement since 1993 (SPY), since 2004 (GLD) and since 2011 (BTC) in
`nber_turning_points.csv`: the type, the turning-point month, the announcement date, the lag, and
the asset's close at the announcement relative to its lowest close in the 365 days before and the
365 days after. Six announcements fall in SPY's history. **No verdict is drawn.** A test needs a
daily S&P 500 index before 1993, which the lab lacks.

### C5. Short-dated yields lead the Fed ("tell that to the bond market", proxy)

The FedWatch 85% rule needs fed funds futures history, which the lab lacks. This tests the claim
under it, that the bond market sets the path and the Fed follows.

- **Events**: scheduled FOMC decisions in `events_fomc.json` (scheduled = true), 2000 to 2022.
  Unscheduled meetings are reported separately and not tested.
- **Target**: DFEDTAR to 2008-12-15, then the midpoint of DFEDTARU and DFEDTARL. **Decision** = the
  sign of target(*d*+1) − target(*d*−1): cut, hold or hike.
- **Bond pricing**: s = DGS3MO on its last observation before *d*, minus target(*d*−1). Predicted
  cut if s ≤ −0.25, hike if s ≥ +0.25, hold otherwise.
- **Cheap alternative**: predict the same decision as the previous scheduled meeting.
- **Statistic**: accuracy (bond pricing) − accuracy (previous decision) > 0, one-sided exact McNemar
  test on the meetings where the two disagree.
- Descriptive: the same with DGS2 in place of DGS3MO (his words: "wherever the two year yields go").
- Caveat stated now: a 3-month bill prices the next meetings almost by definition. The test is
  whether it beats persistence, not whether it is right.

### C6. The alarm gate

Camel, 26 Sep: failed daily cycles first, then trendline breakdowns, and only then the alarms.

- **On SPY, day *d*:** (i) a failed daily cycle: close(*d*) below the low of the most recent G_D
  reference low known on *d*, with that first close below it in the last 10 bars; (ii) a breakdown:
  close(*d*) < SMA(50)(*d*); (iii) an alarm: BAA10Y on *d* minus 91 days earlier ≥ the expanding
  90th percentile of that change (needs 252 values), **or** T10Y3M on *d* minus 182 days earlier ≥ 2
  × the standard deviation of that 182-day change over the previous 365 days.
- **Gate days**: (i), (ii) and (iii), first firing in any 20 bars. **Comparison days**: (i) and (ii)
  without (iii), first firing in any 20 bars.
- **Outcome**: SPY's maximum drawdown over the next 60 bars.
- **Statistic**: mean outcome (gate) − mean outcome (comparison) < 0. Null: permute the (iii) label
  among all days with (i) and (ii), within decade, 10,000 draws.
- Reference row, descriptive: the first close below SMA(200) after a close above it, first in any
  20 bars.
- BAA is investment grade, not high yield. Any finding must say so.

**Family C**: C1-SPY, C1-BTC, C2, C3, C5, C6 = 6 tests, Holm inside the family. Each needs at
least **20 events per group** in the development era; with fewer it is not testable. **A thesis is
supported** if its test is significant after Holm and the held-out difference has the same sign
(at least 10 events per group; otherwise Suggestive at most).

## 7. Test (d): a trading version, only after (a) and (b)

Run after (a) and (b) are scored. **A pass here counts only where the matching detector passed**: D1
needs B1 Supported or Fragile in that class, D2 needs A2 Supported or Fragile. Otherwise the result
is reported, and the verdict is "No edge (the detector failed)". A trading result never rescues a
detector.

- **D1**: enter on our CONFIRMED. **D2**: enter on Camel's checklist call A-P.
- **Entry**: the open of bar *i*+1.
- **Initial stop**: low(*j*) − 0.25 × ATR(14)(*i*), and at least 0.5 × ATR(14)(*i*) below the entry
  price. If bar *i*+1 opens at or below the stop, the trade is skipped and counted.
- **Exit**, the first of: the stop, filled at the stop or at the open if the bar gaps through it;
  the close of the first bar at least 5 bars after entry whose close is below SMA(10) (his "cross
  back below the 10 MA" exit); the close of the 60th bar after entry. If one bar touches the stop and
  meets the close rule, the stop wins.
- **R per position** = (exit − entry) ÷ (entry − initial stop), minus costs 2 × 0.0015 × entry ÷
  (entry − initial stop). R is from the initial risk, one number per position.
- **Control**: the same number of entries drawn at random among the N2-eligible bars of that
  detector (section 3.5), with the stop under the lowest low of the previous 10 bars minus 0.25 ×
  ATR, the same floor and the same exit; 1,000 draws. p = (1 + number of draws with mean R ≥
  observed) ÷ 1,001.
- **Reference rows**, descriptive: the lab's standard harness (1 × ATR stop, 2R, 20 bars) on the
  same entries; the CAP_P version of CONFIRMED; gross R.

| Id | Test (development era, per class, net R) |
|---|---|
| D1 | mean R of CONFIRMED trades > its control |
| D2 | mean R of checklist trades > its control |

6 tests, Holm inside family D. **Supported** in a class: significant after Holm, held-out mean net R
above the held-out control mean (at least 10 trades), at least 50% of the development excess kept at
25% noise, and the detector gate above.

Not tested, on purpose: pyramiding, the long-term account's "size to zero", short trades.

## 8. Robustness, required beside every verdict

- **Noise injection**: the lab's Varma noise (gaussian noise on every log return at 25% of each
  price series' own daily volatility, as `gate` and `XsMomentumRobustness` do), seeds 0, 1 and 2,
  development era, rebuilding everything (reference lows, weekly bars, the checklist, the price,
  breadth and volatility families). MVRV and the Puell multiple carry price in their numerator, so
  they are scaled by the same cumulative factor as the price, the way `OnChainRobustness.Perturb`
  does; the other macro, sentiment, positioning and on-chain inputs are not perturbed.
  **Retained** = the mean of the noisy excess (over N2 for A2, the B2 difference, over the control
  for D) ÷ the clean excess.
- **Breadth**: every statistic per asset as well as pooled.
- **Held-out**: one read, after the development tests.
- **Data proof**: the sha256 of every input file before and after the run.

## 9. The budget and the counting rule

| Family | Tests |
|---|---|
| A-primary (A1 to A3 × 3 classes) | 9 |
| A-secondary (A4 to A8 × 3) | 15 |
| B-primary (B1 to B3 × 3) | 9 |
| B-secondary (B4 to B6 × 3, B7 × 2) | 11 |
| C (macro) | 6 |
| D (trading) | 6 |
| **Total** | **56** |

The two fallbacks of section 3.7, if triggered, add up to 3 tests each, reported as Suggestive.

**Pre-registered descriptive runs** count toward `variantsTried` but are not tests: C4, the
descriptive columns of C1 to C3 and C5, the reference rows of C6 and D, the (b) descriptive output
(section 5.3), the gross-cost figures, the three noise seeds and the per-asset breakdowns.

**Counting rule.** Every other run stage 2 makes is counted in a `variantsTried` total and listed in
its report with its result: a different span, threshold, window, lookback, family, sign or weight; a
different era cut or asset subset; a bug-fix rerun; an exploratory look. **A bug fix that changes a
pre-registered number must report both numbers.** Nothing outside this list may be used to reach a
verdict, and anything new is labelled exploratory in the same sentence as its number.

**Frozen numbers.** None may be tuned.

| What | Value | From |
|---|---|---|
| reference low, daily | 20-bar low, 3 × ATR(14) rally | free choice |
| reference low, weekly | 130-bar low, 6 × ATR(14) rally | free choice |
| hit window, post-low calls | low 0 to 15 bars before (15 weeks weekly) | free choice |
| hit window, alerts | low 5 bars before to 20 after (5 and 20 weeks weekly) | free choice |
| swing low | 10 bars back, 2 to 10 bars old | free choice |
| window lengths L, daily | BTC 60, ETH 60, equity ETFs 40, GLD 25 | Camel |
| window lengths, weekly | BTC 30, ETH 30, equity ETFs 24, GLD 26 | Camel |
| window open | 0.9 × L, no upper end | Camel's 10%; open end from "through the window" |
| SMA | 10 | Camel |
| DSS Bressert | (10, 9, 5), cross with the lower line below 20, from *j*−3 | Camel's screen frame; 20 is the common level |
| trendline proxy | 3-bar pivot highs, back to *j*−120 | free choice |
| thrust | range ≥ 1.5 × ATR(14), close in top 25%, or engulfing | free choice |
| checklist count | SW + WIN + 3 of 4 (variants 1 of 4; DSS and TL) | Camel's dated wordings |
| percentile warm-up | 252 daily, 104 weekly, 36 monthly | free choice |
| families needed | 3 | free choice |
| ALERT | pctCAP ≥ 95, due at 0.75 × median spacing (4 spacings), 20-bar refractory | free choice |
| CONFIRMED | anchor in last 10 bars, washout ≥ 90 in *j*−5 to *j*, a 15-point turn, close > high(*j*) | free choice |
| grade cuts | 97 and 93 | free choice |
| 80% rule | 130-bar candidate, 182-day outcome | free choice; 80% is Camel's |
| macro horizons | 60 bars (C1, C2, C6), 120 bars (C3) | free choice |
| "strong" and "hot" | above the median of the previous 12 releases | free choice |
| revision signal | 3-release sum at or below the 20th percentile (60 needed) | free choice |
| FOMC threshold | ±0.25 percentage points | free choice |
| alarm thresholds | BAA10Y 91-day change at the 90th percentile; T10Y3M 182-day change ≥ 2 SD | free choice |
| trade stop | 0.25 × ATR under the low, floor 0.5 × ATR | free choice |
| trade exit | close < SMA(10) after 5 bars, 60-bar cap | Camel's 10-MA exit; numbers free |
| costs | 15 bps per side | engine defaults |
| era cut | 2023-01-01 | Cody |
| minimum counts | 30 / 15 / 10; 20 per group for (c) | free choice |
| noise bar | 50% kept at 25% noise | lab standard |
| draws | 10,000 (N1, N2, permutations, bootstrap); 200 surrogates; 1,000 trade controls | free choice |

## 10. What counts as "no edge", stated plainly

**For (a), in a class, Camel's checklist has no edge if any one of these is true:**

- A2 is not significant after Holm: the checklist does not pick lows better than a random swing low;
- A3 is not significant after Holm: its precision is what the definitions give on a random walk;
- the median lead over the confirmation is not above zero: it does not call lows earlier than a
  fixed lagged rule;
- the held-out excess over N2 is zero or negative.

**For (b), in a class, the score has no edge if B1 or B2 fails after Holm, or either held-out check
fails.** If B1 passes and B2 fails, the verdict is "the score finds lows only as well as its price
part; the added data adds nothing detectable".

**For (c), a thesis has no support** if its test fails after Holm or its held-out sign disagrees.

**For (d)**, see section 7: no pass without the detector's pass.

**The verdict then reads**: "Camel's checklist [or the capitulation score] does not find
later-confirmed lows better than [the null that failed], in [class], over [era]."

The null **cannot be reframed afterwards** by any of these:

- picking a different item set, count, window length, threshold, span, lookback, hit window or
  family list, or flipping a metric's sign;
- dropping assets, eras or years, or switching from the development era to the full sample;
- switching to recall, F1, lead time or any other statistic as the headline in place of precision;
- moving from net to gross R, or to a different trade exit;
- promoting a secondary test, a descriptive table or a subgroup found afterwards to the headline;
- pooling classes;
- adding a data source after seeing a result (each needs its own dated pre-registration first);
- arguing that the checklist "needs his indicator" or "needs his discretion": the indicator is
  closed source, and his discretion is what the dated-call ledger scores, not this test.

**Also stated now:**

- A pass at the weekly degree (A7, B6) with a fail at the daily degree is reported as exactly that,
  and is Suggestive at most, because the weekly tests are expected to be short of events.
- If the window filter makes precision **worse** (A6 in the wrong direction), the write-up says the
  window hurts.
- If B5 shows the "due" rule adds nothing, the design drops it in any later version; that later
  version is new and needs its own pre-registration.
- If the 80% rule's IN rate is near his 80% but no better than the survival-matched null, the 80%
  is the base rate of a low that has already survived, not a property of the higher high.

## 11. Files for stage 2

- **Read first**: this file, `docs/CYCLE_LOW_INDICATOR_DESIGN.md`, `docs/CAMEL_METHOD.md`,
  `docs/MACRO_DATA.md` (the traps), `docs/CYCLE_FINDINGS.md`.
- **Reuse**: `DssBressertProvider`; `MacroPointInTime` (`ParseKnownCsv`, `ParseVintageCsv`, `AsOf`,
  `LatestKnown`); `StableSeed`; the Varma noise of `XsMomentumRobustness` and the price-scaled
  version in `OnChainRobustness.Perturb`; `WeeklyPersistenceCommand.ToWeekly` for completed weeks
  (it is private; copy or expose it); `CycleCommand`'s surrogate builder as a model for N3 (it
  shuffles returns only; N3 shuffles whole bar tuples so that ATR and pivots stay defined).
- **Before using COT**: confirm which CFTC report each `xs_cftc_*` file came from (design note
  section 5.4).
- **Results go in** a new `docs/CYCLE_SCHOOL_FINDINGS.md`, with an `edges.json` record
  (`EdgeRegistryTests` requires one for every findings document). **Do not edit this
  pre-registration after results exist**, except to add a dated pointer to the findings at the top.

## Addendum (2026-10-18, before any outcome)

Written by stage 2 after fetching the extra data Cody asked for ("any extra data we don't have in
terms of Fed and macro, fetch that before running the tests") and **before any reference low,
call, alert, event count, forward return or test statistic had been computed for any rule in this
file**. Nothing above is changed. This addendum only says which new series fill which slot the
design note already has, and what the COT check found.

### A.1 What was fetched

`StrategyLab macro-fetch --only labour,cftc` on 2026-10-18 (details and file list in
`docs/MACRO_DATA.md`, sections "Labour leading data" and "CFTC positioning"):

- **JOLTS quits rate (JTSQUR), JOLTS job openings (JTSJOL) and temporary help services jobs
  (TEMPHELPS)**, with their full ALFRED vintage history. Vintages start 2010-08-11 for both JOLTS
  series and 2011-01-07 for temporary help. Nothing is known before a series' first vintage.
- **The CFTC Traders in Financial Futures report** for the E-mini S&P 500 (contract code 13874A), the
  Nasdaq-100 mini (209742) and CME Bitcoin (133741), and **the disaggregated report** for COMEX gold
  (088691), from the CFTC's annual archives. Each value is a trader group's net position as a
  percentage of open interest, stamped `known_from` = its release (normally the Friday; later for
  holidays, the 2013, 2018-19 and 2025 shutdowns and the 2023 ION incident, from the CFTC's own
  announcements), and never earlier than the day the report's history was first posted (2010-12-03
  for TFF, 2010-05-07 for the disaggregated report).

Not fetched, as instructed: Cboe put/call after 2019, and anything needing a key or a login.

What I saw while doing this: the vintage start dates above, the first and last report dates of the
CFTC series, and the values of the old `xs_cftc_*` files only as far as matching them number for
number against the archives (A.2). No price, return or outcome was looked at.

### A.2 What the existing COT files contain

The design note (5.4) asked stage 2 to confirm which report each `xs_cftc_*` file came from. Each
file was matched value by value against every contract and trader group in both archives:

- `xs_cftc_sp500_cot_1w.json`: TFF leveraged funds, E-mini S&P 500 (13874A), all 1,048 weeks,
  stamped report date + 3 days. **Confirmed**, with two faults: it starts in 2006, but TFF values
  before 2010-12-03 were not public then; and "+3 days" ignores holiday and shutdown delays.
- `xs_cftc_nasdaq_cot_1w.json`: TFF leveraged funds, Nasdaq-100 mini (209742), all 1,048 weeks.
  **Confirmed**, with the same two faults.
- `xs_cftc_bitcoin_cot_1w.json`: **not one contract**. 73 weeks are Cboe's bitcoin future (1330E1,
  2017-12 to 2019-05), 356 are CME Bitcoin (133741) and 24 are CME's micro contract (133742). The
  downloader matched the name "BITCOIN" and kept the first matching row each week. Under the design
  note's rule ("if a file cannot be confirmed, Q3 is undefined for that asset") this file is unusable.
- `xs_cftc_gold_cot_1w.json`: disaggregated managed money, COMEX gold (088691), all 711 weeks,
  report-dated (the lab cache moves it to report + 3). **Confirmed.**

So the S&P, Nasdaq and Bitcoin files are TFF data, not the disaggregated report the design note
feared; the problem is the Bitcoin mixture and the release stamping.

### A.3 Where the new series go (only slots the design note already has)

**Positioning, metric Q3** (design note 5.4). Same metric, same sign (−), same staleness (21 days),
same weekly warm-up (104 values). Only the file changes, and every asset reads the same kind of
file, stamped by the same release rule:

| Asset | Q3 now reads | Group |
|---|---|---|
| SPY, DIA, IWM | `macro/cftc/tff_sp500_emini_lev_funds_net_pct_oi.csv` | leveraged funds |
| QQQ | `macro/cftc/tff_nasdaq100_mini_lev_funds_net_pct_oi.csv` | leveraged funds |
| BTC | `macro/cftc/tff_bitcoin_cme_lev_funds_net_pct_oi.csv` | leveraged funds |
| GLD | `macro/cftc/disagg_gold_comex_managed_money_net_pct_oi.csv` | managed money |
| ETH | none (unchanged: the design gives ETH no Q3) | |

Reasons, all decided before any outcome:

- For SPY, QQQ and gold the values are identical to the confirmed old files week for week; only
  `known_from` changes, to the real release and the history floor. That removes look-ahead (up to
  five weeks in the 2019 backlog, and 2006 to 2010 values that nobody could see then).
- For BTC the old file fails the design's confirmation rule; the new file is CME Bitcoin only, the
  contract the design meant. Its first week is 2018-04-10 (the CFTC shows a market only once 20
  traders are reportable), not 2017-12.
- Leveraged funds is the TFF report's speculator group and is what the confirmed old files already
  held, so the metric means what it meant when frozen.
- Consequences, stated now: Q3 for the equity ETFs starts from 2010-12-03 rather than 2006; Q3 for
  gold starts from 2010-05-07 rather than 2013-01 (the same values, a longer history); BTC's Q3 is
  newly defined. The asset-manager and dealer files were fetched but enter nothing, because the
  design has no slot for them.
- The release is at 3:30 pm Eastern, before the US equity close, so the normal rule (`known_from` on
  or before the bar's date) holds for equity bars too; the H.4.1 exception does not apply.

**Macro and liquidity, metrics M10 to M12** (design note 5.7, the family's labour metrics M7 to M9).
Each copies M9 exactly ("mean of the last 3 monthly changes, as published that day", read through
`MacroPointInTime.AsOf` from the `_vintages.csv` file, sign −, staleness 75 days, monthly warm-up of
36 values, `realtime_start` as known-from), so no new length, sign rule or threshold is introduced:

| Id | Series | Sign | File |
|---|---|---|---|
| M10 | temporary help services jobs (TEMPHELPS) | − | `macro/alfred/TEMPHELPS_vintages.csv` |
| M11 | JOLTS quits rate (JTSQUR) | − | `macro/alfred/JTSQUR_vintages.csv` |
| M12 | JOLTS job openings (JTSJOL) | − | `macro/alfred/JTSJOL_vintages.csv` |

Falling temporary help, quits and openings are the labour weakness Camel reads as leading (CAMEL_METHOD
7.3), so lower is more washed out, the same orientation as payrolls. They join family M, which every
asset has; the number of families, the equal weights across families and the "at least 3 families"
rule are unchanged. Like every metric, each is undefined until its own 36 releases exist (from about
2013-08 for JOLTS and 2014-01 for temporary help) and when its newest value is older than 75 days.

### A.4 What does not change

- **No new test, no new threshold, no new family, and the count stays at 56.** No slot in this file
  was marked "pending data", so none is added.
- The macro tests C1 to C6 are unchanged; none of them uses the new series.
- The 2025 shutdown, USREC, ICE-spread and NFCI traps (`docs/MACRO_DATA.md`) are handled as section
  6 and the design note already say.
- **Most literal readings for the build, fixed now:**
  - a vintaged monthly metric "enters at its own cadence" with one value per ALFRED vintage date (each
    release), computed from `AsOf` that day; its age for staleness is the bar's date minus the first
    release date of the newest period it uses;
  - a weekly COT value is one value per report week, entering on its `known_from`.
