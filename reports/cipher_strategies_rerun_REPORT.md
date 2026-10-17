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
