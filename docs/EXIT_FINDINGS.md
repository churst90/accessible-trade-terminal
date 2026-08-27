# Exits — real skill exists, it is crypto-only, and fixed scale-outs destroy the edge
> **⚠ SUPERSEDED STATISTICS — re-run before quoting (added 2026-08-27).**
>
> Every p-value below was computed with machinery that has since been found wrong, and the
> numbers have NOT been recomputed. Three defects, all fixed in code on 2026-08-27:
>
> 1. **Post-selection p-values.** `XsMomentumCommand` picked the best of 16 grid cells and then
>    ran the permutation test on that cell against a fixed-configuration null. The statistic
>    actually computed is a *maximum over 16*, whose null is much wider — so the p was too small
>    by roughly the effective number of independent cells. The command now reports a
>    max-statistic null alongside the naive one.
> 2. **Overlapping rows treated as exchangeable.** Every permutation test that emits one
>    observation per bar over a multi-bar horizon shuffled rows individually, though consecutive
>    rows share all but one of their forward bars. Effective sample size is nearer `n/horizon`
>    than `n`, so **significance was inflated by roughly √horizon**. The affected commands now
>    block-permute.
> 3. **The survivorship stress could not fail.** `XsMomentumRobustness` applied a uniform drag
>    that did not depend on the ranking, which reduces algebraically to the clean excess times a
>    positive constant. "The edge survives every cell" was arithmetic, not evidence. It now
>    removes names from the universe and re-ranks.
>
> Separately, the sample these numbers were computed on is not recorded — `strategy-lab-data/`
> is gitignored — so a re-run is a re-measurement on possibly different data, not a reproduction.
> Snapshots now carry a `barsSha256` so future results can name their sample.
>
> **Treat every number below as provisional until the commands are re-run.**


Run 2026-08-01. `dotnet run -- exits`. **The entry is held completely fixed** (the BTC trend rule,
z50 crossing +1 — the only entry validated out of sample here). Only the exit varies, so every
difference is attributable to the exit.

## The control

A **random exit drawn from the same holding-period distribution** the tested rule produces. This is
the exposure-matched null applied to exits: bars-in-trade held fixed, only *timing* randomised.
Without it, a rule that merely holds longer in a rising asset looks skilful.

## Result: only the signal exit has skill, and only in crypto

`vs RANDOM` = the rule's equity divided by the random-exit control's.

| exit rule | BTC | ETH | SPY | QQQ |
|---|---|---|---|---|
| **signal (z<0.5)** | **32.34× (p=0.003)** | **4.35× (p=0.034)** | 0.53× (p=0.89) | 0.84× (p=0.48) |
| fixed 2R | 0.63× | 1.05× | 0.72× | 0.89× |
| fixed 3R | 0.71× | 0.66× | 0.56× | 0.73× |
| fixed 5R | 1.26× | 0.33× | 0.70× | 0.94× |
| ATR trail 3 | 0.39× | 0.58× | 0.66× | 0.67× |
| ATR trail 5 | 1.02× | 1.09× | 0.03× | 0.25× |
| time 20 bars | 1.00× | 1.00× | 1.00× | 1.00× |
| time 60 bars | 1.00× | 0.99× | 1.02× | 1.04× |

**Three findings:**

1. **Exit skill exists — in crypto.** The z-exit beats a random exit of the same holding length by
   32× on BTC (p=0.003) and 4.35× on ETH (p=0.034). This is a genuine, measurable exit edge.
2. **It is the same polarity split as everything else.** No exit rule beats random on SPY or QQQ.
   Mechanistically consistent: the z-exit works in crypto *because crypto trends*, so "the trend has
   weakened" is real information about what comes next. In a reverting asset it is not.
3. **Every mechanical exit is worthless everywhere.** Fixed R targets, ATR trails and time stops all
   score at or below random on all four assets. Time stops sit at exactly 1.00× — which is expected,
   since a fixed holding period *is* the random control.

**The exit that works is the entry signal, run in reverse.** Not a separate exit technology.

## Fixed percentage scale-outs destroy the edge

The proposed rule — take 50–80% off at +10–20%, trail the remainder — tested on BTC against holding
to the signal exit (27,835×):

| take | at gain | trail | equity | vs hold-to-signal |
|---|---|---|---|---|
| 50% | 10% | 3 ATR | 26.78× | **0.00×** |
| 50% | 20% | 5 ATR | 1,289× | **0.05×** |
| 80% | 10% | 5 ATR | 45.24× | **0.00×** |
| 80% | 20% | 5 ATR | 247.80× | **0.01×** |

**Between 95% and 100% of the return is destroyed.**

The reason is in the trade distribution: BTC trend trades average **+8.15R at a 47% win rate**. The
return lives entirely in a fat right tail. Capping winners at +10% or +20% amputates precisely the
trades that produce everything.

This does not mean scale-outs are wrong in general — it means **the correct exit is determined by
the return distribution of the strategy, not by preference.** A fat right tail must be allowed to
run. An intraday mean-reversion book with thin, symmetric outcomes is a different animal, and the
practitioners who advocate partials are describing that animal.

## The best-equity rule is not the best rule

ATR trail 5 posted the highest BTC equity (29,973× vs the signal exit's 27,835×) — but with **56%
max drawdown against 29%**, and **1.02× versus random**, i.e. no skill. It got there by holding
long, which is an exposure fact. The signal exit gets nearly the same return at half the drawdown
*and* is the only rule that beats its control.

That is why the `vs RANDOM` column is the one to read, not the equity column.

## Caveats

- 70 entries on BTC, fewer on the others. The BTC and ETH p-values are real but the sample is small.
- Long-only, one entry rule, no costs. Fixed-R exits trade far more often, so costs would widen the
  gap against them.
- The scale-out grid is coarse (2 take fractions × 2 gains × 2 trails); the conclusion is not
  sensitive to it because the effect is 20–100×, not marginal.
