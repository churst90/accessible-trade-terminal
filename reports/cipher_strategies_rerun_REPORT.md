# Cipher strategies re-run on the new Cipher A / Cipher B — report

(Results sections are added below this pre-registration as the runs finish. This first part was
written and committed BEFORE any rebuilt-Cipher-A result was computed. Its commit time is the proof.)

## Pre-registration (written 2026-10-17 21:46 UTC, before any Cipher A result was looked at)

### What I had already seen when this was written

Cipher B OLD vs NEW on `gate`, `confluence`, `asset-profile`, gate noise injection, and the
143-cell rolling-window battery on BTC and ETH daily. Nothing about the rebuilt Cipher A had been
computed except that its leaves are absent from the battery (retired names never fire).

### "Went well" — the bar a member must clear before a suite (A + B + DBSI, maybe SR) is tested

A member (Cipher A or Cipher B NEW) "went well" only if at least one of its signals:

1. survives the **max-statistic (family-wise) block-permutation null** at alpha 0.05 across the whole
   pre-registered family it was tested in (block = 20 bars, the horizon), AND
2. has the **same sign of excess in both halves** of the sample (split at the median date per asset), AND
3. for an R-harness result, keeps at least **50% of its excess over random at 25% noise injection**
   (the bar cross-sectional momentum passed at 86%).

Because I had already seen Cipher B NEW's gate and noise numbers when writing this, any suite test
that rests on Cipher B is EXPLORATORY by construction, and is labelled so. (For the record: Cipher B
NEW's gate result retained 29% of its excess at 25% noise on fixed seeds 0-2, so it does not meet
criterion 3 as written.)

### Cipher A battery (reconstruction of the 2026-07-13 gate battery)

The July battery's code was never committed (git history and docs hold only its summary in the
e19efdd0 commit message and CHANGES.md). So this is a reconstruction from that description, not a
reproduction, and is reported as such.

- **Assets** (the July list, where a snapshot exists): BTC and ETH (bitstamp 1d), gold
  (twelvedata XAU/USD 1d), silver (yahoo SLV 1d), SPY and QQQ (twelvedata 1d), AAPL and MSFT
  (yahoo 1d). EUR and COIN: no snapshot, not fetched (rule: no fetches). SLV/AAPL/MSFT files are
  dated 2026-07-29, after the battery, so they are not the July sample.
- **Signals**: all seven rebuilt Cipher A symbols, onset bar only (a marker set on bar i and not on
  i-1). Long: Green Dot, Blue Triangle, Yellow Diamond. Short: Red X, Red Diamond, Blood Diamond,
  Yellow X. Reference rows (same harness, not counted as Cipher A tests but counted in variantsTried):
  Cipher B NEW Oversold Crossover (green buy dot), WaveTrend Cross Bull, Overbought Crossover,
  WaveTrend Cross Bear.
- **Outcome**: enter at bar i+1's open; signed 20-bar return to bar i+20's close (short = negative of
  the return); hit = signed return > 0. Also the lab's standard R harness (1 ATR(14) stop, 2R target,
  20-bar horizon) as a second column.
- **Gates** (direction-aware): G0 none; G1 Faber (long: close > SMA200, short: close < SMA200);
  G2 COT (long: COT_POSITIONING z < 1.5, short: z > -1.5; only BTC, gold, SPY, QQQ have COT);
  G3 Faber + COT; G4 RSI(14) (long < 50, short > 50); G5 variance ratio VR20 over the trailing
  252 bars < 1; G6 Loukas "DC In Window" (longs only).
- **Control**: the mean signed 20-bar return of ALL gate-open bars on that asset (an averaged
  random entry under the same gate, not one draw). Excess = signal mean minus that.
- **Test**: block permutation of the signal labels over the gate-open bar sequence, block 20 bars,
  two-sided, 2,000 draws, fixed seed. Family-wise: single-step max-|t| over every cell in the family.
- **Era**: excess per half (median date split).
- **Count**: every (signal, asset, gate) cell with at least 5 events is a test; every cell attempted
  counts toward variantsTried whether or not it had 5 events.

### Old-leaf to new-symbol mapping (meaning, not name)

- CIPHER_A.Buy Signal (wave crosses up from oversold): no Cipher A counterpart. The same event is
  Cipher B's Oversold Crossover. Green Dot is an EMA 11/33 trend cross — a different meaning.
- CIPHER_A.Sell Signal (wave crosses down from overbought): no exact counterpart; Cipher A's Red
  Diamond is the wave cross down WITHOUT the overbought condition (partial); Cipher B's Overbought
  Crossover is the same event.
- Bullish / Bearish / Overbought Bearish Divergence: none in Cipher A (MCA draws none); Cipher B's
  divergences.
- Manipulation, Exhaustion: no counterpart.
- WT Momentum / Gradient: none; Cipher B's Wave Trend.

### Pre-registered combinations with Cipher B NEW (6 rules x 8 assets x 2 gates = 96 tests)

- K1 long: Cipher B green dot (Oversold Crossover) with the Cipher A ribbon up (EMA 11 > EMA 33).
- K2 long: Cipher B green dot with the ribbon down (EMA 11 < EMA 33) — the complement of K1.
- K3 long: Cipher A Green Dot with Cipher B's slow wave below zero.
- K4 long: Cipher A Blue Triangle with a Cipher B green dot in the 5 bars before (inclusive).
- K5 short: Cipher A Blood Diamond with Cipher B Money Flow below zero.
- K6 short: Cipher A Red X with a Cipher B WaveTrend Cross Bear within 2 bars (inclusive).
- Each under G0 and G1; same outcome, control and test as the battery; its own max-statistic family.

### Built-in strategy fixes (proposal only, not applied)

For each catalogue spec with a dead leaf (Cipher A Buy Signal, Cipher SR Support): run (a) as
written today, (b) the dead leaves removed, (c) each replacement candidate, on the assets the spec
was originally tested on, rolling 1500-bar windows stepped 250 as the battery did, and in the
pre-2026-10-17 build for the as-recorded baseline.

---

# Results (2026-10-17, after the pre-registration above)

Branch state when these were computed: main at 1f3b6756 merged in (rebuilt Cipher A f8169668,
Cipher B refit, Cipher SR rebuild d5aa3dd3, gold dot refit 2640c8b4, DBSI-style). Data: the
read-only snapshot directory; the sha256 of all 186 non-intraday files was identical before and
after every run. Nothing was fetched.

## 1. Read this first: what reproduced, and what did not

Cody's rule was "a seeded, deterministic routine that does not reproduce means the data or code
moved — stop and report that first." This is that report. I did not stop the whole re-run, because
the cause was found in every case and none of it is the 2026-10-17 refit. Say if you would rather I
had stopped.

**The old-parameter switch is exact.** `--cipherb old` (WTSignalPeriod 4, Legacy money flow, Gated
buy dot, Confluence gold) printed output identical line for line to a lab built from aca73a1d, the
commit before the refit, on gate, on confluence at 1h, 4h, 1d and 2d, and on asset-profile at 1d
and 4h. The ML export's Cipher B columns match too. So the OLD-versus-NEW numbers below are a clean
comparison.

**The data did not move.** Built at the commit each result was recorded on, the lab reproduces the
recorded numbers on today's snapshots. Confluence at 45672f51 (2026-07-26) gives 1h −0.059R p 0.73,
4h −0.380R p 0.0042, 2d +0.552R p 0.070, and 1d −0.391R p 0.051 on the thirteen 1d files that existed
that day. The rolling-window battery at 45672f51 gives v23 base on BTC +0.266R, 87% of windows,
27.4 trades, and v23 plus Cipher C +0.289R, 87%, 22.7 trades — CONFLUENCE_SENTIMENT recorded +0.27R,
87%, 27 and +0.29R, 87%, 23.

**On the code as it was just before the refit:**

- `gate` (POLARITY_AND_GATE, last re-run 2026-08-27) reproduces exactly: n 3,059, MA-gate lift
  +0.107R, excess over random +0.100R, p 0.0004, and every cost, era and per-symbol number.
- The ML export (ML_CONFIDENCE, 2026-06) reproduces the stored June training file (21,118 rows) cell
  for cell for wt1, wt2, mfw, sig_wtx, sig_blue, sig_gold and win.
- The gate's noise-injection table (2026-07-31) cannot be reproduced, by construction: its seed came
  from `string.GetHashCode()`, which .NET randomises per process, until StableSeed replaced it on
  2026-08-22 (dc652371). I re-ran it on fixed seeds 0, 1 and 2 instead; OLD keeps 23% of its excess
  at 25% noise (recorded: 21%).
- `confluence` structure-as-context does NOT reproduce. Bisected to 3601e37e (2026-08-21, the
  causality contract): SwingStructureAnalyzer stopped deleting a superseded pivot, so panning back no
  longer shows a structure that was never knowable, and stopped sorting pivots with an unstable sort.
  4h moved from −0.380R p 0.004 to −0.252R p 0.061. Part of the July number was look-ahead in the
  structure labels.
- The rolling-window battery and every catalogue-spec number from 2026-04 to 2026-07 do NOT
  reproduce. Bisected to three commits. ef4d3916 (2026-08-24) fixed a look-ahead in date-filtered
  backtests: bar 0 of a later window read bar 0 of the whole chart, and the risk plan placed stops
  from the chart's LAST bar. That moved v23 base BTC from +0.266R and 27.4 trades to +0.378R and
  20.5. 91648ad5 (2026-08-27) moved divergences to the bar they fire (+0.378R to +0.395R). 411022a2
  (2026-08-27) stopped counting each take-profit rung as a trade (20.3 trades to 13.2, expectancy
  unchanged). So every rolling-window figure in CatalogueProvenance and in the battery-promotion
  history was computed with that window look-ahead. The biggest casualty: v23p's "ETH 100% of
  windows positive at +0.523R, the best cell anywhere" is −0.462R with 14% of windows positive on
  the pre-refit code (NEW: −0.416R, 14%).
- Asset-profile (2026-06-12) does not reproduce, for the same reasons; OLD versus NEW is on today's
  code.
- The ML model's AUC was not re-run: the training script needs lightgbm and scikit-learn, which are
  not installed here.

## 2. Inventory

**Lab commands that compute a cipher** (recorded result, date, doc in brackets):

- `gate` and GateRobustness: Cipher B Oversold Crossover (long) and Overbought Crossover (short).
  Both names exist. [Dip filter +0.107R, excess +0.100R, p 0.0004; 2026-07-29, re-run 08-27;
  POLARITY_AND_GATE; ledger row 8.]
- `confluence`: Cipher B Oversold and Overbought Crossover, plus Cipher SR's Support Zone and
  Resistance Zone lines. All exist; the SR lines are rebuilt. [Structure gap per timeframe and SR
  proximity; 2026-07-26; commit 45672f51; ledger row 16.]
- `asset-profile`: Cipher B WaveTrend Cross Bull and Bear, Oversold and Overbought Crossover, Bullish
  and Bearish Divergence, Anchor Wave. All exist. [2026-06-12; ASSET_PROFILE_FINDINGS.]
- `ml-export`: Cipher B Wave Trend, Wave Trend 2, WT Histogram, Money Flow Wave, Anchor Wave and Anchor
  Wave 2, WaveTrend Cross Bull, Oversold Crossover, Triple Confluence Buy. All exist. [AUC about 0.52;
  2026-06-25; ML_CONFIDENCE_FINDINGS; ledger row 28.]
- `battery` and `rolling-window` (StrategyBatteryCommand, 143 cells): all of Cipher B; Cipher A Buy
  Signal, Sell Signal, Exhaustion and Manipulation; Cipher SR Support and Resistance. Every Cipher A
  name is retired and never fires since 2026-10-17. Cipher SR Support and Resistance were retired as
  names on 2026-10-17, but they had already been refused as look-ahead since 3601e37e (2026-08-21),
  so they have fired in no backtest for two months. Cells that need them inside an AND — v23+A,
  v23+SR, v23+ASR, v23+ALL, v23r-ASELL, v23r-AEXH, v23r-SRRES, v27 +SR, v27 FULL, v27 FULL+FEAR,
  "CIPHER_A.Manipulation alone" — make no trades; cells with them inside an OR (the bull and bear
  pulses) silently lost a leg. [Rounds 1 to 8, 2026-04; CONFLUENCE_SENTIMENT 2026-06-20;
  STRATEGY_CATALOGUE; CatalogueProvenance.]
- `diagnostic` (default CIPHER_A, CIPHER_B) and `combo-sweep` (default CIPHER_B) auto-discover marker
  components, so they now see the seven new Cipher A symbols. Not re-run: their recorded results are
  from 2026-04 and predate the window look-ahead fix.
- `combo`'s default filter "CIPHER_B.Money Flow Wave" still exists; StrategyBatteryCommand's −80
  money-flow baseline is stale (already in TODO): the money flow has been centred on zero.
- WorkspaceFactory's default pack computes CIPHER_A, CIPHER_B and CIPHER_SR for every strategy run.

**Catalogue strategies — the "built-in" app strategies.** Since bf57d5fb the terminal's library
ships empty; these are the lab catalogue's specs, exported to the terminal. Nothing in
AccessibleTrader.Core seeds strategies any more.

- With dead leaves: bare-bull-pulse, faber-pulse and capitulation-buy. Each ORs four "fired within 5"
  leaves: Cipher B Oversold Crossover (the green Buy dot), Cipher B Triple Confluence Buy (gold),
  CIPHER_A.Buy Signal (dead since 2026-10-17) and CIPHER_SR.Support (dead since 2026-08-21). They now
  rest on the Buy dot and the rare gold dot alone.
- Cipher B only: v13 (Oversold Crossover plus SMA200), v14 (Hidden Bull Continuation plus SMA200),
  v15 (Oversold Crossover plus Bullish Divergence), v18 short (Hidden Bear Continuation), the v23
  family (WaveTrend Cross Bull, Oversold Crossover or Bullish Divergence, plus Anchor Wave, with Faber,
  COT, Hurst, AVWAP or Pivots gates) and v24 (the same trio plus a Loukas DCL). All names exist.
- pulse-reversal reads Cipher C only and is unaffected.

**Ledger edges touched:** row 8 dip-buy-above-200ma (Fragile), row 16 cipher-confluence (Falsified),
row 18 sentiment-orthogonal (Falsified; measured on the v27 SR cells), row 28 ml-confidence (below
the bar), and row 11 COT, whose promoting battery cell was v23c.

## 3. Cipher B, OLD versus NEW

What the refit changes for signals: the slow wave is a 3-bar average (was 4); the money flow is MCB's
Heikin-Ashi formula; the green Oversold Crossover fires on every wave cross up with the slow wave at
−60 or below (it was a gated, cooled-down crossover); and the gold dot is VuManChu's rule (it was the
K-of-4 confluence). On the ten crypto daily series the green dot went from 183 fires to 346, gold
from 93 to 13, and wave crosses from 1,767 to 2,057.

**Gate — the SMA200 dip filter, ledger row 8.** 51 daily series, 20,000 permutations.

| measure | OLD (reproduced) | NEW |
|---|---|---|
| cipherB-long signals | 3,059 | 5,615 |
| gated mean / ungated mean | +0.224R / +0.118R | +0.284R / +0.132R |
| MA-gate lift | +0.107R | +0.151R |
| excess over random entries | +0.100R | +0.145R |
| p on the gap | 0.0004 | below 0.0001 (0 of 20,000) |
| signals with the gate open | 47% | 26% |

On equities, NEW is +0.280R per gated trade at zero cost and +0.163R at 10 bps a side (OLD +0.202R
and +0.085R); the lift stays nearly cost-invariant (+0.160R falling to +0.141R). Gap per era, four
equal slices of 1970 to 2026: OLD +0.31, +0.14, +0.24, +0.15; NEW +0.07, +0.12, +0.37, +0.27. The
random arm is +0.11, +0.16, −0.09, −0.10 in both, so NEW's excess over random by era is −0.04, −0.04,
+0.46, +0.37 — two eras at nothing where OLD had one. Per symbol it is positive on 28 of 33 equities
(OLD 22 of 33). The short side (red dot) is nothing either way: gap −0.075R, p 0.12 NEW; −0.005R,
p 0.92 OLD.

Noise injection on fixed seeds 0 to 2; share of the clean excess over random that survives:

| noise | OLD kept | NEW kept |
|---|---|---|
| 25% | 23% | 29% |
| 50% | 6% | 23% |
| 100% | 10% | 23% |

Verdict: the new green dot is a larger and broader dip-buy signal under the SMA200 filter, and it
still collapses under 25% noise. Row 8 stays Fragile. Tries: the command's 12 variants, run in 2
profiles, plus 19 noise runs.

**Confluence — structure as context.** Cipher B longs, with-structure minus against-structure.

| timeframe | recorded 07-26 | OLD (pre-refit code) | NEW |
|---|---|---|---|
| 1h | −0.059R p 0.73 | −0.046R p 0.78 | −0.123R p 0.28 |
| 4h | −0.380R p 0.004 | −0.252R p 0.061 | −0.222R p 0.018 |
| 1d (07-26 file set) | −0.391R p 0.051 | −0.314R p 0.117 | −0.235R p 0.128 |
| 2d | +0.552R p 0.070 | +0.486R p 0.113 | +0.342R p 0.151 |

The same shape as July — "with the structure" is worse at 4h and 1d and the sign flips at 2d — and
none of it clears a correction for 8 timeframe-by-profile tests (Bonferroni 0.006). Unchanged:
fragile, timeframe-dependent, and no evidence that structure helps.

**Asset-profile**, means across assets. 1d (ten crypto): L:v23 +0.195R OLD, +0.201R NEW; L:blue
+0.35R OLD (eight assets fire it), +0.30R NEW (all ten). 4h (seven): L:v23 +0.004R to −0.006R; L:blue
−0.12R to +0.04R; L:div +0.07R to +0.02R. Per asset the swings are large and go both ways (ETH 1d
blue +0.44R to +0.07R; SOL 1d v23 +0.25R to +0.50R) on 4 to 14 overlapping windows: noise. Note that
June's "the blue dot has no standalone edge, confirmed five times" is no longer what the pre-refit
code says either (+0.35R on 1d). That changed with the 2026-08-24 window fix, not with the refit.

**ML export.** NEW changes wt2 and mfw on every row and the three signal flags on 0.5% to 5.7% of
rows. The univariate AUC of each Cipher B column against the win label is 0.497 to 0.528 in both OLD
and NEW, identical to two decimals. The model was not retrained (section 1). There is no sign the
refit gave the Cipher B inputs information they did not have.

**Rolling-window battery**, 143 cells, BTC and ETH daily, four arms: PRE is the pre-refit build;
OLD-B is today's code with old Cipher B (so the retired Cipher A and SR leaves are dead); NEW is
today's code; a fourth arm, new Cipher B with the old gold rule, is in section 4. A selection, as
mean ER and the share of valid windows positive:

| cell, asset | PRE | OLD-B | NEW |
|---|---|---|---|
| v23 base, BTC | +0.390, 100% | +0.390, 100% | +0.391, 93% |
| v23 base, ETH | +0.323, 100% | +0.323, 100% | +0.294, 86% |
| bare bull pulse, BTC | +0.406, 93% | +0.602, 80% | +0.577, 93% |
| v23 + AVWAP soft, BTC | +0.294, 100% | +0.294, 100% | +0.191, 93% |
| v23 + Pivots, ETH | −0.462, 14% | −0.462, 14% | −0.416, 14% |
| v25 blue dot alone, BTC | +0.602, 80% | +0.602, 80% | +0.562, 100% |

The v23 trio barely moves: its wave-cross leg dominates. Cells that lean on the green dot move most,
in both directions. Every number here is a mean over 15 (BTC) or 7 (ETH) OVERLAPPING 1500-bar windows
of one asset, and none is controlled for the 143-cell selection.

## 4. Gold dot (Triple Confluence Buy), both rules

The new rule fires about seven times less often (93 to 13 on the ten crypto dailies), so every
gold-dependent strategy becomes, in practice, its green-dot leg. New Cipher B with the new gold rule
versus with the old one: bare-bull-pulse on BTC +0.536R against +0.373R per trade, on ETH −0.017R
against +0.057R; faber-pulse on BTC +0.347R against +0.072R, on ETH +0.100R against +0.239R;
capitulation-buy within 0.1R either way on four coins. In the battery, the bull-pulse cells with
on-chain or funding filters gain 0 to 0.2R on BTC and lose 0 to 0.1R elsewhere. The samples are 6
to 42 trades. **There is no basis here for preferring either rule on returns**; it is a fidelity
question (and, per the gold-refit note, there is no evidence real MCB has a gold dot at all).
Nothing was pinned to either rule.

## 5. Cipher SR (rebuilt)

The confluence command shifted SR levels 15 bars right to undo the old provider's look-ahead. The
rebuilt zone lines are causal, so they were also run unshifted (new flag `--sr-lag 0`). SR proximity,
near minus away, new SR unshifted:

| timeframe | OLD Cipher B | NEW Cipher B |
|---|---|---|
| 1h (KAS and TAO only) | +0.308R p 0.006 | +0.164R p 0.069 |
| 4h | −0.013R p 0.90 | −0.024R p 0.78 |
| 1d (51 series) | −0.034R p 0.46 | −0.046R p 0.25 |
| 2d | −0.226R p 0.29 | +0.076R p 0.71 |

One of the eight is at p 0.006 — exactly the Bonferroni line for eight tests — on two altcoins at 1h,
the timeframe MCSR is built for, and it halves with the new Cipher B. Interesting, not established:
it needs an independent asset set before anything is built on it. The full stack (Cipher B, with the
structure, at an SR level) is negative at 4h and 1d in every arm (NEW −0.143R on 76 trades at 4h,
−0.119R on 422 at 1d). Ledger row 16 stays Falsified; its clause "SR proximity was a lookahead" is
now history — the rebuilt SR is causal and still adds nothing at 4h, 1d or 2d.

## 6. Cipher A, signal by signal (the reconstructed July battery)

Firing counts, for example on BTC daily: Green Dot 64, Blue Triangle 82, Yellow Diamond 326 onsets,
Red X 180, Red Diamond 497, Blood Diamond 30, Yellow X 25.

**Is it Cipher B again?** Partly. Red Diamond IS Cipher B's wave cross down: 100% of Red Diamonds sit
within one bar of a Cipher B WaveTrend Cross Bear, and so do 100% of Blood Diamonds and Yellow X's,
which are Red Diamonds with conditions. Yellow Diamond is within a bar of a bull wave cross 77% of the
time. Green Dot, Blue Triangle and Red X are EMA-ribbon crosses with 6% to 46% overlap — new
information in the narrow sense.

**Result.** 480 cells attempted (variantsTried), 441 with at least 5 events, 266 of them Cipher A.
20 of the 266 Cipher A cells have an uncorrected p at or below 0.05, against about 13 expected by
chance. **No cell, Cipher A or Cipher B, survives the family-wise max-|t| null**; the best is pFW 0.38,
a Cipher B row. Per symbol, ungated, across the eight assets:

| symbol (direction) | mean excess per 20 bars | assets positive | cells p ≤ 0.05 (of about 40) |
|---|---|---|---|
| Green Dot (long) | +0.21% | 5 of 8 | 1 |
| Blue Triangle (long) | −0.84% | 3 of 8 | 3 |
| Yellow Diamond (long) | +0.94% | 5 of 8 | 9 |
| Red X (short) | +0.84% | 8 of 8 | 3 |
| Red Diamond (short) | −0.01% | 4 of 8 | 0 |
| Blood Diamond (short) | +1.04% | 5 of 8 | 1 |
| Yellow X (short) | +0.77% | 4 of 8 | 3 |

For a short, positive excess means the next 20 bars did worse than the average bar. Red X is positive
on all eight assets, but four of the eight are US large-cap equity vehicles that move together, its
uncorrected p is under 0.05 on only one, and its mean R as a short trade is −0.04: a short after Red X
still loses money in rising markets, it just loses less. Blue Triangle on BTC is significantly
NEGATIVE (−5.6%, p 0.006 ungated) — the wrong way for a buy signal. Yellow Diamond's nine nominal hits
are mostly under the VR and RSI gates on BTC and ETH, and its excess fades from the first half to the
second (BTC under VR<1: +7.8% to +1.9%).

Old-leaf mapping: there is no Cipher A counterpart for the old Buy Signal, Sell Signal, divergences,
Manipulation or Exhaustion; the closest same-meaning signals are Cipher B's (see the
pre-registration).

**The retirement verdict, re-checked.** July's reasoning ("the same WaveTrend engine as B") is now
three-sevenths true. The rebuilt symbols carry no forward information that survives a correction for
the number of tests, on any of the eight assets or six gates. Keep it as a faithful chart indicator;
there is nothing here for strategies.

**The Cipher B reference rows in the same battery.** The new green dot under Faber plus COT on QQQ
(14 events, 93% hit, +3.28% excess, t 3.95) is the family's top cell, at pFW 0.38. Under Faber alone
on SPY, QQQ and gold it is 81% to 88% hit and +2.0% to +3.1% excess, pFW 0.69 to 0.88. That is the
dip-filter result again, on a handful of trades, and it does not survive the family.

## 7. Pre-registered combinations (Cipher A with Cipher B NEW)

96 cells attempted, 70 with at least 5 events, 7 with an uncorrected p at or below 0.05 (about 4
expected), and **none surviving the family-wise null** (best pFW 0.47). Two structural findings came
before any result could be read:

- K1 (green dot with the ribbon up) never reaches 5 events on any asset: a cross up with the slow wave
  at −60 or below essentially never happens while EMA 11 is above EMA 33. So K2 (ribbon down) is the
  green dot itself, row for row, and "a Cipher B buy confirmed by the Cipher A ribbon" is not a
  combination that exists on these assets.
- K6 was implemented looking back only (a Red X with a bear cross in the last 2 bars): "within 2
  bars" forward would read the future.

## 8. The suite (A + B + DBSI, SR if earned): NOT run

Against the bar written down before any Cipher A result: no Cipher A signal and no combination
survives the family-wise null (criterion 1), and Cipher B NEW's best result, the dip filter, keeps 29%
at 25% noise where criterion 3 asks for 50%. Neither member went well, so the suite was not tested and
DBSI-style was not run. I had seen Cipher B's numbers before writing the bar, so for B the decision is
not blind; but it is a decision NOT to test, which cannot be steered toward a positive.

## 9. Built-in strategies: proposals (not applied)

The three pulse specs were each run as written, with the dead leaves removed, and with three
replacements, on the assets in their provenance (BTC and ETH daily; capitulation-buy also XRP and
LTC), over the whole sample and 1500/250 rolling windows, with each spec's own risk plan and the
battery's costs. "As written" and "removed" print identical numbers in every arm, which confirms the
dead leaves do nothing.

bare-bull-pulse, NEW: whole-sample trades and ER, then rolling mean ER and share of windows positive.

| variant | BTC | ETH |
|---|---|---|
| as written = removed | 39, +0.536R; +0.577, 93% | 39, −0.017R; +0.297, 100% |
| A.Buy to A.Green Dot | 65, +0.312R; +0.370, 93% | 57, +0.062R; +0.309, 100% |
| A.Buy to A.Blue Triangle | 61, +0.390R; +0.435, 93% | 52, +0.070R; +0.311, 100% |
| SR to close crossing above Support Zone | 79, +0.154R; +0.119, 73% | 53, +0.144R; +0.527, 100% |

faber-pulse, NEW, BTC then ETH: as written 20 trades +0.347R and 9 trades +0.100R; Green Dot 41,
+0.276R and 23, +0.399R; Blue Triangle 30, +0.321R and 17, +0.166R; Support Zone cross 44, +0.041R
and 22, +0.237R. capitulation-buy, NEW, BTC, ETH, XRP, LTC: as written 6, +0.247R; 18, +0.031R; 30,
+0.202R; 37, +0.113R. Blue Triangle: 10, +0.478R; 24, +0.081R; 43, +0.241R; 46, +0.164R. Green Dot:
8, +0.537R; 24, −0.127R; 42, +0.154R; 46, +0.057R.

**Trade counts against earlier runs.** The pulse specs' recorded April runs did not record trade
counts, and their numbers predate the window and trade-counting fixes, so the comparable anchors are
these. The battery's bare-bull-pulse cell averaged 25.0 trades per BTC window at the 2026-07-26 build
(all four legs live; take-profit rungs then counted as trades, about doubling the figure), 12.5 on the
pre-refit build (SR already dead), and 10.6 NEW. For the specs themselves, pre-refit (A.Buy live, SR
dead) to NEW: bare-bull-pulse BTC 44 to 39, ETH 41 to 39; faber-pulse BTC 23 to 20, ETH 10 to 9;
capitulation-buy BTC 6 to 6, ETH 18 to 18, XRP 31 to 30, LTC 40 to 37. Losing Cipher A's Buy Signal
alone (old Cipher B, same code) would have cut them much harder — bare-bull-pulse ETH 41 to 19,
capitulation-buy LTC 40 to 19 — and the new green dot, firing about twice as often, refills most of
that.

**Proposal for each dead leaf: delete it, and add no replacement.** Deleting changes nothing a
backtest can see, and stops each spec claiming four legs when it has two. The Cipher A replacements
add trades and lower per-trade R on BTC while nudging ETH up, and Cipher A had no signal that survived
its own battery — adding one now would be choosing on these very numbers. The Support Zone
replacement is worse on BTC in every spec. Each spec's provenance should then say that its pulse is
the green Buy dot plus gold, and that gold now fires a handful of times. Tries for this section: 40
variant-by-asset runs per arm, in 4 arms.

**The whole catalogue, as written, NEW against pre-refit** (whole-sample ER on the recorded asset).
The biggest moves: v23a BTC +0.776R to +0.504R; v13 BTC +0.454R to +0.222R (17 to 20 trades); v23c QQQ
+1.036R to +0.658R (9 to 7 trades) and gold −0.124R to −0.289R; v24 ETH −0.006R to +0.263R; v23r BTC
4h +0.461R to +0.534R; v15 from 1 trade to 8 (still negative). v14, v18 and pulse-reversal are
unchanged: hidden divergences and Cipher C do not depend on the refit. v18 makes 0 trades on BTC
daily in every arm — pre-existing, not investigated. 37 spec-by-asset runs per arm.

## 10. Ledger rows

- Row 8 (dip filter): stays Fragile. NEW +0.145R excess, p below 0.0001, positive on 28 of 33
  equities; 29% kept at 25% noise; two of four eras at nothing.
- Row 16 (Cipher confluence): stays Falsified, now re-measured with the rebuilt A and SR. The SR
  look-ahead clause is history.
- Row 18 (sentiment): its v27 SR cells have made no trades since 2026-08-21 (the SR leaf is
  refused), so it cannot be re-measured as built; it stands as recorded.
- Row 28 (ML): the export reproduced; the refit leaves the univariate AUCs unchanged; the model was
  not retrained.
- New: the rebuilt Cipher A as a signal source is a null (0 of 441 cells survive the family null).

## 11. Total tries

gate: 2 profiles of 12, plus 19 noise runs. confluence: 4 timeframes, 3 builds, 2 SR lags, plus the
July and crypto-only 1d sets (22 runs). asset-profile: 2 timeframes, 2 profiles. rolling-window: 143
cells, 2 assets, 4 arms (1,144). cipher-a-battery: 480 plus 96. spec-variants: 40 in 4 arms, plus 37
in 4 arms (308). The bisection builds (15 commits) were diagnostics, not variants.

## 12. What is left

SR and gold are done (sections 4 and 5; both merged mid-run). Open for Cody: the three dead-leaf
deletions (section 9); which gold rule to keep (section 4); whether the 1h SR result deserves an
independent-asset test; and re-recording CatalogueProvenance's rolling-window figures, every one of
which was computed before the 2026-08-24 window look-ahead fix.

## Files

Lab code (all in AccessibleTrader.StrategyLab): LabCipherProfile.cs (the `--cipherb old|new` and
`--cipherb-gold old|new` switch, in the engine every command resolves), CipherABatteryCommand.cs
(`cipher-a-battery`), SpecVariantsCommand.cs (`spec-variants`, `--all`), ConfluenceCommand.cs
(`--sr-lag`). Test: AccessibleTrader.Tests/LabCipherProfileTests.cs, seen red twice by sabotage.
