# Smart money concepts: pre-registration

Written 2026-11-23, before any result existed. **This file was committed before any forward return,
hit rate, fill rate, event count or outcome was computed for any rule in it. The commit time is the
proof.** The same agent writes this and runs it (Cody 2026-11-19: no hand-offs), so section 1 says
exactly what I had seen.

Cody, 2026-11-22: "SMC: test and report on the value provided." The five concepts are the ones the
indicator-library list names: **fair value gaps, order blocks, equal highs and lows, liquidity
sweeps, premium and discount zones.** Market Structure (break of structure, change of character) is
already in the app and is not retested here.

The rules below are also what the app will draw. The lab grades the providers' own output, not a
second copy of the logic, so the verdict is about what a user sees on the chart.

## 1. What I had seen when writing this

- **No outcome of any rule below was computed or looked at.** The only data work was listing the
  files in `strategy-lab-data/` and reading the first and last bar dates and bar counts of
  `bitstamp_BTC_USDT_4h`, `mexc_KAS_USDT_4h`, `bitstamp_BTC_USDT_1d`, `yahoo_SPY_1d` and
  `twelvedata_XAU_USD_1d`.
- **Findings I had read, which set priors:**
  - Sweep and reclaim of equal-high clusters: **not supported** in crypto 1d, crypto 4h or equities
    1d (`docs/archive/RISK_TARGET_AND_METRIC_FINDINGS.md`, section 5). Concept S4 below overlaps it;
    it differs in using any swing level, a same-bar reclaim and paired random controls.
  - A random horizontal line is "respected" 59% of the time (`docs/archive/FIB_GANN_FINDINGS.md`).
    This is why every zone test below has a random-level control.
  - Cipher SR support levels: No edge in every class (`SETUP_GRADER_ROUND3_MC_SR_FINDINGS.md`).
  - Crypto trends and equities revert: "do not short a crypto breakout" (the polarity finding).
  - Fixed exits leave most of the fat right tail on the table (`EXIT_FINDINGS.md`), which is why
    every outcome here is a fixed horizon, not an exit rule.
- I know how SMC is usually taught and how popular TradingView scripts draw it, in general terms. No
  threshold below was tuned on any chart.

Every number below is a free choice made now unless it says otherwise.

## 2. Shared definitions

### 2.1 Data, classes, eras

| class | files | bars |
|---|---|---|
| crypto 4h | the seven `*_4h.json` files (bitstamp BTC, DOGE, ETH, SOL, XRP; mexc KAS, TAO) | 4h |
| crypto 1d | the ten `bitstamp_*_1d` and `mexc_*_1d` files | daily |
| equities 1d | the `yahoo_*_1d` files (ETFs included); `twelvedata_*` left out as duplicates of SPY, QQQ and gold | daily |

- Files starting `xs_`, `events_`, `fred_` are not price files and are skipped. A file with fewer
  than 500 bars is skipped and counted.
- **Development era:** event bars before 2023-01-01. **Every verdict is decided here.** An event's
  outcome may use bars after 2022-12-31.
- **Held-out era:** event bars from 2023-01-01 to the last bar on disk. Read **once**, after the
  development verdicts are written down, for the sign of each primary statistic only.
- An event whose wait window plus horizon runs past the last bar is dropped and counted.

### 2.2 Building blocks

- **ATR** is the lab's `IndicatorMath.Atr(bars, 14)` (Wilder). An event with no ATR is skipped.
- **Swing high** at bar p: `High[p]` strictly above the highs of the 5 bars before and the 5 after.
  It is **known at bar p + 5** and usable only from then. Swing lows mirror. (The app's
  `PivotMath.IsPivot`, 5 each side.)
- **Horizon:** every R outcome is the close 20 bars after entry. R = move ÷ the ATR named in the
  test. Long: `(Close[t+20] − entry) / ATR`; short mirrored.
- **Wait window:** a zone that has not been touched within 50 bars of being known is dropped.
- **Limit fills:** a zone entry is a limit order at the zone edge. On the first bar t whose range
  reaches the edge, the fill is the edge, or the bar's open if it opened beyond the edge.
- **Costs:** 15 bps per side (the lab's engine default), converted to R at the entry. **Gross R
  decides the information verdict (section 4); net R decides "worth trading" (section 5).**
- **Seeds:** `StableSeed.From(file)` combined with a fixed salt per test, so every run is the same.

## 3. The five concepts and their tests

Each concept is drawn bullish and bearish. Bearish is the exact mirror of bullish and the two are
pooled in every test (a bearish event enters short).

### S1. Fair value gaps

**Definition.** A bullish gap is known at bar i when `Low[i] > High[i−2]` and the gap,
`Low[i] − High[i−2]`, is at least **0.1 × ATR[i]**. The zone is `High[i−2]` (bottom) to `Low[i]`
(top). It is **filled** when a later bar's low reaches the bottom, and **mitigated** (no longer
drawn) when a later bar closes below the bottom.

**F1 (primary): the gap is support on the first return.** Entry is the first bar t in (i, i+50]
whose low reaches the top. The entry is long at the limit fill and the outcome is 20-bar R over
ATR[i]. The claim is that the outcome is higher than a pullback of the same depth to a level that is
not a gap.
- **Isolating control (decides the verdict):** the same event, with the entry level at a depth drawn
  from the other gaps. d = `(Close[i] − top) / ATR[i]`. For each event, 20 depths are drawn with
  replacement from the d values of that class's development-era gaps on the same side. Each draw
  sets a level `Close[i] − d* × ATR[i]`, and the same entry rule applies (first touch within 50
  bars, limit fill, 20-bar R). The event's control value is the mean over the draws that filled.
- **Random-anchor control (reported):** 20 random bars j from the same file and era. Each puts a
  level at the event's own depth below `Close[j]` and applies the same entry rule.
- Statistic: the mean over events of (event R − its control value). Events that never fill drop out,
  and so does an event whose control draws all fail to fill. Both counts are reported.

**F2 (secondary): gaps get filled.** The outcome is 1 if a bar in (i, i+50] reaches the gap's
bottom, else 0. The control is the random-anchor level at the event's own bottom depth
`(Close[i] − bottom) / ATR[i]`, 20 draws, as a rate. Statistic: the mean of (event outcome − control
rate). The claim is that it is positive.

### S2. Order blocks

**Definition.** The app tracks the last confirmed swing high H (its price, from the bar it is
known). A **bullish break of structure** happens at bar i when `Close[i] > H` and `Close[i−1] ≤ H`.
Each swing high breaks at most once. The **bullish order block** is the most recent bar k in
[i−10, i−1] with `Close[k] < Open[k]`. With no such bar there is no block. The zone is `Low[k]` to
`High[k]`, known at i, and **mitigated** when a later bar closes below `Low[k]`.

**O1 (primary): the block is support on the first return.** Entry is the first bar t in (i, i+50]
whose low reaches `High[k]`. The entry is long at the limit fill and the outcome is 20-bar R over
ATR[i]. The controls and the statistic are built exactly as in F1. The isolating control takes the
same break of structure with depths drawn from the other blocks, so it asks: given the break, is
the block's own level special?

### S3. Equal highs and lows

**Definition.** When swing high p2 becomes known (bar c = p2 + 5), look back to the previous swing
high p1. Two conditions make it an **equal high**:
- `|High[p2] − High[p1]| ≤ 0.1 × ATR[c]`;
- no bar between p1 and p2 traded above the level, `max(High[p1], High[p2])`.

It is drawn from c until a bar's high goes above the level.

**E1 (primary): equal highs draw price to them.** The outcome is 1 if a bar in (c, c+50] trades
above the level, else 0.
- **Control:** every other swing high in the same class and era, at its own confirmation bar.
- **Distance match:** d = `(level − Close[c]) / ATR[c]`, binned 0–0.5, 0.5–1, … 4.5–5. Distances
  over 5, or below 0, are dropped from both groups.
- Statistic: the mean over equal highs of (outcome − the control rate in its bin). The claim is that
  it is positive.

### S4. Liquidity sweeps

**Definition.** A swing high's level stays open from the bar it is known until a bar's high goes
above it. That first bar t ends the level, and it is one of two things:
- a **sweep**, when `Close[t] < level` (through and back inside on the same bar);
- a **breakout**, when the close is at or above the level.

**L1 (primary): a sweep reverses.** The entry is short at `Close[t]` and the outcome is 20-bar R
over ATR[t].
- **Control (decides the verdict):** 20 random bars from the same file and era, also short at the
  close, over their own ATR. Statistic: the mean of (sweep R − its random mean). The claim is that it
  is positive.
- **L2 (secondary):** sweep R minus breakout R, with the breakout also entered short at its close.
  The claim is that sweeps reverse more than breakouts.

### S5. Premium and discount

**Definition.** At bar i, the dealing range runs from the last known swing low L to the last known
swing high H, with H > L. The equilibrium is (H + L) / 2. A bar is **discount** when
`L ≤ Close[i] < equilibrium` and **premium** when `equilibrium < Close[i] ≤ H`. Bars outside the
range are in neither.
- **Structure is bullish** when the most recent break was a close above a swing high, and
  **bearish** when it was a close below a swing low. These are the same breaks as in S2.

**P1 (primary): buy discount in bullish structure.** Every bullish-structure bar is either discount
or premium, and each gets the long 20-bar R over ATR[i]. Statistic: the mean R of discount bars
minus the mean R of premium bars. The claim is that it is positive. The mirror case (bearish
structure, short R, premium minus discount) is pooled in.

**P2 (secondary):** the same difference over all bars, ignoring structure.

## 4. Statistics and the information verdict

- **p-values:** block bootstrap over calendar years within the class and the development era. All
  of a year's events on all of the class's files move together, 10,000 resamples. One-sided p =
  (1 + resamples with the statistic ≤ 0) ÷ (1 + 10,000).
- **Holm–Bonferroni, α = 0.05, one-sided:** within each class over the five primary tests (F1, O1,
  E1, L1, P1). The family size is five even if a test cannot run.
- **A test runs** only with at least 30 development-era events, and for P1 at least 30 bars in each
  zone. Otherwise it is reported as "could not run" and counts as no evidence.
- **Overlap:** events close in time share outcomes, so these p-values are optimistic. That is why
  the held-out sign is part of the verdict.

**The information verdict, per concept:**
- **Has information** when its primary test passes Holm in **at least two of the three classes**,
  no class is significant the other way (the mirror one-sided p below 0.05, unadjusted), and the
  held-out sign agrees in every class that passed.
- **Fragile** when it passes in exactly one class, or in two with a held-out sign that disagrees.
- **No information** otherwise.

## 5. Worth trading

This is reported only for a concept with information.
- Take the event arm's mean net R per trade (after the section 2.2 costs) in each class that passed.
- It is **worth trading** when that mean is at least **+0.10 R** in every class that passed.
- Otherwise the information is real but too small to pay for its costs.

## 6. What is reported regardless

Every count, every arm's gross and net mean R, the secondary tests (F2, L2, P2) with their unadjusted
p, the random-anchor results for F1 and O1, and the held-out signs. Nothing is dropped for being
uninteresting.

## 7. What each verdict will mean for the app

- **All five concepts are added to the chart either way.** Cody asked for them, and a user should be
  able to hear what other traders look at.
- What the lab decides is what the manual says about them, in plain words:
  - "the lab found no information in it" or "the lab found information but not enough to pay";
  - whether its levels may be offered in the strategy builder, which is the case only for a concept
    with information. The other levels stay drawable and speakable.

## 8. Fixed choices, in one place

| choice | value | source |
|---|---|---|
| swing | 5 bars each side, strict, known 5 bars later | free choice (the lab's sweep study used 5) |
| ATR | Wilder 14 | lab default |
| FVG minimum | 0.1 × ATR | free choice |
| order block search | last down candle in the 10 bars before the break | free choice |
| equal tolerance | 0.1 × ATR at confirmation | free choice |
| wait window | 50 bars | free choice |
| horizon | 20 bars | lab convention (sweep study) |
| control draws | 20 per event | free choice |
| bootstrap | calendar-year blocks, 10,000 | lab convention (cycle school) |
| α, correction | 0.05 one-sided, Holm over 5 per class | lab convention |
| costs | 15 bps per side | engine default |
| worth-trading bar | +0.10 R net | free choice |
