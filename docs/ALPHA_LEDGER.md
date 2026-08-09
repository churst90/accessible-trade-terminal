# The alpha ledger — everything this project has actually tested

Compiled 2026-08-01 from the individual findings documents. This is the distillation: one row per
claim, what survived, how big it was, where it applies, and which control decided it.

**Why this document exists.** Fifteen findings documents and thirty strategy specs is not knowledge
— it is material. What a research programme needs is a single place that answers "what do we
believe, how strongly, and where does it apply", so the next study starts from the frontier rather
than from scratch, and so a scoring engine has something principled to read. This file is the prose
version. The machine-readable one now exists:
**`AccessibleTrader.StrategyLab/Catalogue/edges.json`**, read by `EdgeRegistry` and queryable with
`StrategyLab edges list | show | scorable | overlaps | stale | validate`. Keep the two in step —
`EdgeRegistryTests` fails the build if a `*_FINDINGS.md` document has no edge record.

**The honest prior.** Narang's number for a successful quantitative strategy's out-of-sample R² is
**0.03–0.04**. Nothing below beats that, and nothing should be expected to. The strongest result in
this table is a 0.37% per-30-day spread. Edges here are attention-direction, not certainty.

---

## The survivors

| # | Claim | Scope | Effect | Controls it passed | Status |
|---|---|---|---|---|---|
| 1 | **Cross-sectional momentum** — rank a universe by trailing return, long the top tercile | Equities (38 names) | Top−bottom **+0.37%** per 30d, **p = 0.0045**; 365d p = 0.0029 | Monotone in lookback 8/8; sign flip at 1 month exactly where the literature puts it; 86% retained under noise injection | **Strongest result in the project** |
| 2 | **Time-series trend following** — the family, not the parameters | Crypto (BTC), and as crash insurance in equities/gold | **1.80× hold** out of sample at ~40% drawdown; vol-targeted BTC Sharpe 1.19 vs 0.80 hold, maxDD 23% vs 83% | Walk-forward with a **random-parameter arm** — random params returned 41.69× vs 34.22× for best-by-return | **Real; the tuning is not** |
| 3 | **Asset-class polarity** — crypto trends, equities mean-revert | Both, as a hard fork | VR20 1.15 crypto vs 0.82 equities; four independent studies landed on it without looking | Within-class and demeaned tests; jackknife | **Real as a fork, not as a dial** (see caveat) |
| 4 | **Volume carries forward information** | **Crypto only — and it reverses in equities** | Crypto +0.37 to +1.40 (p ≤ 0.009 in all three buckets); equity **−0.37, p = 0.0002** | Three independent volume buckets, same sign in crypto | **Real, and the sign is asset-class dependent** |
| 5 | **Mean reversion to the volume POC** | Equities, ~5-day horizon | p = 0.0004 across 348k bars, 38 symbols; tiny | Cross-asset; the crypto arm **reverses** (momentum) | **Real but small; a known anomaly** |
| 6 | **Exit on the entry signal reversed** | **Crypto only** | BTC **32.34× (p = 0.003)**, ETH 4.35× (p = 0.034) vs random exits | Random-exit control; equities arm null (0.53×, 0.84×) | **Real — and the only exit that is** |
| 7 | **FOMC pre-announcement drift** | 4 US equity vehicles | Same offset, same sign across four vehicles, 224 real dates | Exposure-matched null; weekday-matched random control | **Real but ~70% arbitraged away post-2015** |

## The near-misses

| # | Claim | Verdict | The control that decided it |
|---|---|---|---|
| 8 | **Buy dips only above the 200MA** | **Fragile.** Real (+0.107R vs +0.007R for random entries with the same filter) but only **21% retained** under noise injection, against 86% for cross-sectional momentum | Random-entry baseline, then noise injection |
| 9 | **Crypto bearish engulfing weekly → down week** | **Interesting, not established.** +8.30 pts, pooled p = 0.013, cheap alternative flat (+0.04), holds in both eras and strengthens (H1 +5.2, H2 +12.0) — but four pooled tests put Bonferroni at 0.0125, and ten crypto instruments are not ten independent samples | Random-week null, cheap alternative, era split — needs an independent asset set with a correlation-preserving null |
| 10 | **The Trading Cross z-score rule** | **Real, but it is drawdown avoidance, not timing.** 10/10 crypto beat hold, 0/3 traditional | Exposure-matched timing null (p = 0.001) — the block bootstrap could not decide it |

## The nulls — tested and dead

Recorded so nobody re-runs them hopefully. **Most of these looked good until one specific control** —
and three of the 2026-08-01 batch are the same mistake in different clothes: the thing that would
produce the observed pattern *without* the claimed mechanism was never built. A fat tail makes any
target look good against a smaller target; a z-cross makes "above the 50-bar mean" look like
agreement; full-sample ranking makes any extension metric look prescient.

| # | Claim | Verdict | What killed it |
|---|---|---|---|
| 10 | **On-chain valuation (MVRV, NVT) times the market** | **Null** | Monotone −1.11 ATR (p = 0.0002) — but the matched price/SMA baseline on the same rows gives 0.00 ATR (p = 0.9855), and the exposure-matched null failed **0 for 6**. It was exposure to the bottom, not timing |
| 11 | **COT positioning predicts returns** | **Null**, both data sources | Dedicated study after the battery cell that promoted it |
| 12 | **Crowding (funding + open interest)** | **Null at every horizon 1–40 bars** | Direct test; the provider's docstring overstates its orthogonality (+0.19 with trailing return) |
| 13 | **Macro release days (CPI, NFP, PPI, GDP)** | **Null** | 2 of 20 release-day cells significant where ~3 false positives are expected; CPI shows nothing anywhere. **The contrast with FOMC is the finding** — FOMC is a policy *action*, CPI/NFP are *data* the market spends the interval forecasting |
| 14 | **Fixed-percentage scale-outs protect gains** | **Destroys 95–100% of the return** | The R-distribution has a fat right tail; capping it removes the only trades that pay |
| 15 | **Camel / Lucas cycle counts (54–66d BTC)** | **Detector artifact** | 200 surrogate series from shuffled log returns reproduce the cycle length on every asset, landing inside the claimed band |
| 16 | **Cipher confluence (A + B + SR)** | **Falsified** | Eight versions walked forward to break-even; structure labels indistinguishable from random; SR proximity was a 15-bar lookahead |
| 17 | **Hurst exponent as a cross-asset classifier** | **Useless** | 0.57–0.60 on all 17 asset/timeframe combinations — no discrimination |
| 18 | **Sentiment (Fear & Greed) adds orthogonal information** | **Redundant** | The gated spec was byte-identical to the ungated one: when the oscillator buys at a cycle bottom at support, you are already in extreme fear |
| 19 | **Weekly bullish engulfing predicts the next week** | **Null** | Equities −2.31 pts with 0 of 39 instruments significant (≈2 expected by chance); the crypto arm's +3.21 is an era artifact (H1 +10.4, H2 −1.8). The cheap alternative — "last week simply closed up" — is flat too |
| 20 | **How price approaches a level predicts whether it holds** | **Null**, both halves | Matched random lines. Real levels hold 46.2% vs random 46.7%, and every conditioning bucket lands inside ±1.2 pts of its random control. Loitering does lower the hold rate — and lowers it identically for random lines |
| 21 | **A fixed 1:3 reward-to-risk target** | **Falsified**, both classes | Total R rises monotonically 1:1→1:8 and no target beats the signal exit (crypto +3.265 vs +0.155 at 1:3). The median trade loses; the top 10% carry 101% (crypto) and **245%** (equities) of total R |
| 22 | **Stopping after two consecutive losses** | **No statistical basis** | Lag-1 autocorrelation of win/loss −0.035 / −0.040, p 0.53 / 0.999 against a shuffle. Outcomes are independent, so the rule moves variance, not expectancy — behavioural, not an edge |
| 23 | **Sizing up when timeframes agree** | **Null** | The overlap check is the finding: "above the 50-bar mean" is true at **100%** of entries by construction. Expectancy is flat across agreement buckets (equities +0.037 R, p = 0.324) |
| 24 | **A 0–1 rank-normalised risk metric** | **Falsified** | Ranking against the full series (what a published rainbow chart shows) gives 2.3× the spread of an honest expanding window; the normalisation then adds almost nothing over its own raw input (3.0 vs 2.6 pts); equities are a null with the wrong sign |
| 25 | **Sweep-and-reclaim entry** | **Not supported** | The one arm resembling the claim is the smallest sample and is contradicted by two larger ones; most of its gap is the breach arm being *bad*, not the reclaim being good |
| 26 | **The ladder — always in the trend's direction** | **Falsified** | Loses at every rung width **with costs set to zero**, so it is the structure and not the churn. Long-only is 4× better in crypto and 7× in equities; the short side helps on 1 of 10 crypto names — the sixth symmetric-short failure here |
| 27 | **Earnings surprise ranks forward returns** | **Null on the sample we could reach** | Quintiles non-monotone at 20/60/120 bars with Q2 best every time; the event itself is *below* an exposure-matched null at 60 and 120 bars. But the universe is 10 mega-caps — the most analyst-covered names there are, where drift is weakest by construction |
| 28 | **ML confidence model on price features** | **Below the bar** | Pooled OOS AUC ~0.52 (stable, calibrated, not a coin flip — but under the 0.55 "build big" line). Asset-tuned models were *worse*. What predictive power exists lives in regime/vol features, not in signals |

---

## Three caveats that change how the survivors are used

**Polarity is collinear with depth (ρ = 0.96) and its sign reverses inside crypto.** Treat "crypto
trends, equities revert" as a hard fork between two playbooks. Do not build a continuous
"trendiness" dial out of it — the number is mostly market depth wearing a different name.

**Battery promotion is not out-of-sample.** Nine catalogue specs were promoted for being the best
cells of an 89-cell battery. The winner's rolling-window number is a maximum over 89 draws.

**Cipher SR still repaints in the provider.** The lookahead was corrected inside `ConfluenceCommand`
but not in the provider, so any backtest touching a `CIPHER_SR` leaf is optimistic by an unmeasured
amount.

## The methodology that produced these answers

Four of the last six theses died on a control that was cheap to add and that the obvious version of
the test omitted. Before running a study, ask **what is the cheapest thing that would produce this
same result without the claimed mechanism**, and build that first as a named control in the output.

**When a conditioning variable and an outcome are both fractions of the same total, they are ONE
variable.** Added 2026-08-04, after shipping the mistake. The right-translation test measured
"faster" as decline share — `(nextLow−high)/length` — against a translation of `(high−low)/length`.
They sum to exactly 1, so it reported a −0.524 effect for a +0.524 translation gap and looked like a
strong confirmation. It was arithmetic restating its own input. Check that two variables cannot be
added to a constant before interpreting any spread between them.
(`TRANSLATION_FINDINGS.md`)

**Breadth is now recorded, not just described.** Added 2026-08-04. Every scorable edge carries
`breadth: {held, tested, notes}` in `edges.json`, and `StrategyLab edges breadth` ranks them by it.
A p-value says a pooled number is unlikely by chance; breadth says whether it showed up in more than
one place — and a result significant across thirty symbols but driven by two is one instrument's
behaviour wearing a statistic. Current spread: polarity 51/51, volume 48/48, POC 38/38, Trading
Cross 10/13, FOMC 4/6, signal-exit 2/4, trend-family 1/3.

**A low share is not automatically a weakness.** The signal-reversed exit held on 2 of 4 because the
two it failed on were equities and the two it held on were crypto — the asset-class fork, not noise.
The note field exists so the number cannot be read without the reason.

**Controls that have changed a verdict here:** random-entry baseline · exposure-matched timing null
(same days in market, random contiguous blocks) · a cheap alternative doing the same job
(`close > SMA(200)`) · within-class and demeaned tests · noise injection · random-parameter arms ·
Freedman–Lane permutation for partial correlations · surrogate series from shuffled returns.

**State the minimum detectable effect whenever the sample is small enough to doubt.** Added
2026-08-02 after the analyst revision-breadth run. Six monthly cross-sections over eleven mega-caps
produced a −1.56% tercile spread at t = −0.68, which reads exactly like a null — and is not one. The
smallest effect that sample could have detected at all was **6.47% a month**, against a literature
effect well under 1%. Reporting it as a null would have been the mirror image of reporting a lucky
backtest as an edge, and would have closed a live question with a number that meant nothing. One
line of arithmetic separates *untested* from *tested and dead*, and the ledger depends on the
difference. (`REVISION_BREADTH_FINDINGS.md`)

**Any recorder that cannot distinguish "not yet" from "not there" will eventually write a hole into
an archive and call it data.** Also 2026-08-02, and found twice in one afternoon. The analyst-grades
recorder collapsed *paywalled*, *uncovered*, *rate-limited* and *network error* into a single null
return, captured 1 symbol of 21, and reported success. The GDELT recorder, written hours later, hit
the identical failure: GDELT answers an over-rate request with **HTTP 200 and a plain-text apology**,
so a naive client sees a successful non-JSON response and records nothing. Both now classify the
failure and both refuse a run that lost more than a set share of the *reachable* universe —
permanent absences are stable and harmless, transient ones come and go and every reappearance looks
like news.

**Traps that produced false results here:** a control seeded from `string.GetHashCode()`, which .NET
randomises per process — the same bucket printed −5.6 and then −1.8 on consecutive runs of identical
code, and the first number would have been reported (run any study twice and diff) · shuffling a
strategy's own returns (order was never the
question — shuffle the *input*) · block-bootstrap surrogates for a partial-exposure rule (the null
median sits near 0.05) · full-sample max drawdown as a cross-sectional variable · the same
instrument from two providers · a signal and its gate derived from the same series · confirmation
lookahead · a test that shares the code's misunderstanding.

## Sources

`EARNINGS_SURPRISE_FINDINGS.md` · `APPROACH_AND_WEEKLY_FINDINGS.md` · `RISK_TARGET_AND_METRIC_FINDINGS.md` · `XSMOMENTUM_FINDINGS.md` · `WALKFORWARD_FINDINGS.md` · `POLARITY_AND_GATE_FINDINGS.md` ·
`VOLUME_FINDINGS.md` · `EXIT_FINDINGS.md` · `FOMC_FINDINGS.md` · `MACRO_EVENT_FINDINGS.md` ·
`ONCHAIN_FINDINGS.md` · `CROWDING_FINDINGS.md` · `POSITIONING_AND_EVENTS_FINDINGS.md` ·
`TRADING_CROSS_FINDINGS.md` · `CYCLE_FINDINGS.md` · `CONFLUENCE_SENTIMENT_FINDINGS.md` ·
`ML_CONFIDENCE_FINDINGS.md` · `ASSET_PROFILE_FINDINGS.md` · `FIB_GANN_FINDINGS.md`

---

## Addition, 2026-08-06 — the Bitcoin four-year cycle (Benjamin Cowen)

Two edges, from 22 videos. Full working in `docs/CYCLE_FINDINGS.md` (addendum); dated predictions
in `docs/FORECAST_LEDGER.md`.

| Claim | Verdict | The control that decided it |
|---|---|---|
| **BTC cycle top timed from the HALVING** — 525 / 546 / 535 days for the 2016/2020/2024 halvings, a 21-day spread on a ~1,460-day cycle | **Fragile.** Passes its control decisively and still cannot be acted on | **0 of 400** shuffled-return surrogates through the same detector from the same anchors cluster that tightly; stable across every threshold 0.55–0.75 (plateau, not peak). But **n = 3**, breadth **1 and unimprovable**, the halving is **perfectly collinear with the US election cycle**, and the 2025 top is unconfirmed |
| **BTC midterm-year monthly seasonality** — July green, Aug/Sep red | **Falsified as a month claim** | The same months in **all** years: July is +10.0% unconditionally and only 2-of-3 green in midterms; Aug/Sep are already BTC's weakest months. 13 of 42 midterm month-cells green vs ~60% baseline — **midterm *years* are down years**, and the month claim restates it |

**The methodological keeper:** *re-anchor a cycle claim on an exogenous, pre-scheduled event and the
circularity disappears.* Camel cycle counts and Cowen's low-to-low counts both fail because the
analyst picks the anchors looking backwards; the same claim measured from the halving — a date fixed
by protocol years ahead — is testable, and it passed. **Then check the anchor is not collinear with
something else on the same period.** Here it is, fatally: halvings are always US election years.

**A base-rate discipline this batch forced:** his S&P call ("10–20% correction starting Aug–Sept")
is correct about 2 of the last 4 midterm years *and* about 2023, 2024 and 2021, which were not
midterm years. **11 of 19 SPY years had a 9–25% drawdown.** Record the unconditional base rate next
to any conditional forecast, or a coin flip reads as a cycle.
