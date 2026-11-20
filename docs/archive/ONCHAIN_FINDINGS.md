# On-chain value metrics — the first non-price family, and it is not empty
> **RE-RUN 2026-08-27 — the overlap correction cost this family its significance.**
>
> Recomputed with block permutation over the overlapping forward-return rows. The **effect sizes
> are unchanged to two decimals** — MVRV's low-minus-high quintile is still −1.11 ATR — but every
> p-value in the metric table moved by roughly the predicted factor of √horizon at horizon 20:
> **MVRV p = 0.0002 → 0.0267, NVT 0.0002 → 0.0245, active addresses 0.0002 → 0.0305.** The
> Bonferroni threshold across the seven metrics is ~0.007, so **nothing in this family clears it
> any more.** MVRV and NVT are now nominally significant and multiplicity-corrected null.
>
> Nothing else changed direction: the exposure-matched timing null still rejects the rule on all
> six symbol-metric pairs (best p = 0.134, was 0.143), it still loses to buy-and-hold on five of
> six, and ETH still gets *better* under noise injection. **The verdict is unchanged and slightly
> stronger; the quintile finding it rested on is weaker.** variantsTried = 7.
>
> Every number below is now the 2026-08-27 re-run. Sample caveat unchanged:
> `strategy-lab-data/` is gitignored, so this is a re-measurement, not a reproduction.


Run 2026-07-31. `dotnet run -- onchain`. BTC/ETH/LTC/XRP, CoinMetrics 2015→2026, ~12k observations
per metric, 20-bar forward horizon. Metrics lagged one day.

## Why this family

Every null this lab has produced was a price-derived, single-asset, time-series signal or a
conditioner on one. The one thing that survived a full robustness pass — cross-sectional momentum —
was a *family* change. On-chain is the next family with real data behind it.

MVRV is market cap over **realized** cap, where realized cap prices every coin at the level it last
moved on-chain. The denominator is the aggregate cost basis of the network — information that exists
nowhere in a price series.

## The control, designed in from the start

Market cap is price × supply and supply barely moves, so MVRV is structurally close to "price over a
slow baseline" — the Trading Cross z-score in different clothes. This lab has been fooled by exactly
that shape twice (crowding, volume). So the question was never "does MVRV predict returns" but
**"does it beat a price-over-SMA baseline of the same speed?"** — where the SMA length is *found by
search* as the one whose ratio best tracks the metric, not assumed.

## Result: MVRV beats its price baseline outright

| z quintile | forward return |
|---|---|
| 1 (lowest) | +0.13 ATR |
| 2 | +0.38 ATR |
| 3 | +0.83 ATR |
| 4 | +1.20 ATR |
| 5 (highest) | +1.24 ATR |

**Monotone.** Low − high quintile: **−1.11 ATR, p = 0.0267** — nominally significant, and
*not* significant against the ~0.007 Bonferroni threshold for the seven metrics tested.

The matched price/SMA baseline on the same rows: **0.00 ATR, p = 0.9950.**

That is the striking part. MVRV correlates **+0.752** with its matched price ratio, yet the price
ratio predicts *nothing* and MVRV predicts strongly. The signal lives entirely in the residual — in
what the network's cost basis knows that the price path does not.

**Holds across all three eras**, same sign, strongest most recently:
2015-07→2021-01 **−0.64** (p=0.013) · 2021-01→2023-09 **−0.32** (p=0.068) · 2023-09→2026-05
**−1.12** (p=0.000).

Per symbol: BTC −1.97, ETH −1.18, LTC −0.36, XRP −0.41. Consistent sign on all four; BTC and ETH
carry most of the magnitude.

### The direction is the opposite of the folklore

High MVRV predicts **higher** forward returns. The received wisdom — MVRV above ~3.7 marks tops,
below ~1 marks bottoms — reads MVRV as a *value* signal, i.e. expensive means sell. In this sample
expensive means **continue**.

That is exactly what `POLARITY_AND_GATE_FINDINGS.md` predicts: crypto trends, so extension is
continuation rather than exhaustion. The value reading imports a mean-reversion assumption that
crypto does not satisfy.

The literal thresholds cannot be tested here: **MVRV exceeded 3.7 on only 22 days** across 11 years
of four coins. The "top" half of the folklore is untestable on this sample rather than refuted.
Raw MVRV < 1.0 did give +1.03 ATR against +0.43 for everything else (p = 0.1945) — the "bottom"
half survives, but note it is an *absolute* level that only occurs in deep bear markets, which is a
different claim from the rolling-z result above.

## Other metrics

| metric | metric gap | matched price/SMA | beats baseline? | eras consistent? |
|---|---|---|---|---|
| **NVT (mcap/transfers)** | **−1.43** (p=0.0245) | −0.52 (p=0.418) | **yes** | **3/3, all significant** |
| **MVRV** | **−1.11** (p=0.0267) | 0.00 (p=0.995) | **yes** | **3/3 same sign** |
| mcap/addresses | −1.00 (p=0.117) | −0.71 (p=0.271) | marginally | 2/3 |
| active addresses | −1.07 (p=0.0305) | −0.22 (p=0.653) | yes | 2/3, decaying |
| transfer count | −0.82 (p=0.204) | −0.54 (p=0.403) | yes | **sign flips era 3** |
| tx count | −0.21 (p=0.661) | −0.29 (p=0.544) | no | **sign flips era 3** |
| hashrate | −0.34 (p=0.592) | −0.52 (p=0.421) | no | sign flips |

**Every p in this table is a 2026-08-27 block-permutation number.** The column of `p=0.0002`s it
replaced was the artefact of shuffling rows that share 19 of their 20 forward bars: effective
sample size is nearer `n/horizon` than `n`, so significance was inflated by about √20 ≈ 4.5. The
effect sizes did not move at all, which is the tell — the overlap bug was never about the size of
the relationship, only about how confident the arithmetic was allowed to sound.

**NVT and MVRV are the two that survive everything**: beat their price baseline, monotone, and hold
sign across all three eras. Transfer count and tx count flip sign in the most recent era and should
be treated as dead.

## Caveats

- **Seven metrics tested.** Bonferroni would want p < 0.007; after the overlap correction **nothing
  clears it** — MVRV is 0.0267 and NVT 0.0245, both nominally significant and both inside the
  range you expect from seven tries. This was the line that read "MVRV and NVT clear that pooled" and it is no longer true; the
  per-era p-values do not all clear it individually.
- **Four symbols, and only two (BTC, ETH) have the derived ratios.** This is a small cross-section.
- 2015–2026 is roughly two crypto cycles. The era split helps but cannot manufacture independence.
- No costs, entry, exit or position sizing modelled — this is a conditional relationship, not a
  strategy.
- Secular growth in every on-chain series against a secularly rising price is the standing confound.
  Rolling 365-day z-scoring and the era split are the defences; neither is perfect.

## Standing

**This is the second family with something real in it.** Not yet robustness-passed — costs, noise
injection and a forward test have not been run. But unlike crowding, cycles, or the conditioner
line, it survived the control it was designed against.

---

# Robustness pass — 2026-07-31. **Both fail.**

The conditional relationship had to become a strategy before costs meant anything: **long while the
metric's rolling-365d z is in its top quintile, flat otherwise.** That is the formulation the
quintile finding implies, and it is the one tested — searching for a threshold that works after the
fact would be fitting, not testing.

## The test that decides it: exposure-matched timing null

A partial-exposure rule cannot be judged against a block bootstrap — that null sits far below 1 for
any such rule and almost anything clears it. The right control is the same number of days in market,
chosen as **random contiguous blocks** instead of by the signal. This is the test that carried the
Trading Cross at p = 0.001.

| metric | symbol | signal | random median | p |
|---|---|---|---|---|
| MVRV | BTC | 169.0× | 29.8× | **0.161** |
| MVRV | ETH | 7.4× | 1.9× | **0.134** |
| MVRV | LTC | 1.0× | 0.9× | 0.439 |
| MVRV | XRP | 2.7× | 1.0× | 0.224 |
| NVT | BTC | 42.4× | **48.0×** | 0.537 |
| NVT | ETH | 1.4× | 2.1× | 0.644 |

**Nothing clears 0.05. On NVT/BTC the random median actually beats the signal.**

## And it loses to buy-and-hold

| metric | symbol | signal (0 bps) | buy & hold |
|---|---|---|---|
| MVRV | BTC | 169.0× | **6046.1×** |
| MVRV | ETH | 7.4× | 5.7× ✓ |
| MVRV | LTC | 1.0× | 1.5× |
| MVRV | XRP | 2.7× | 158.2× |
| NVT | BTC | 42.4× | **6046.1×** |
| NVT | ETH | 1.4× | 5.7× |

One of six beats holding, and that one (ETH/MVRV, 7.4× vs 5.7×) fails the exposure null anyway.

## Costs and eras

Costs are real but not the killer: MVRV/BTC runs 47 trades at 39% exposure, 169.0× → 153.8× at
10 bps → 105.5× at 50 bps. NVT/BTC churns much harder (257 trades) and drops 42.4× → 3.2× at 50 bps.

Eras are mixed rather than damning — BTC/MVRV loses to hold in two of three thirds, LTC wins all
three (on a book that ends at 1.0×).

## Noise injection — the diagnostic that explains everything

| metric | symbol | 0% | 25% | 50% | 100% |
|---|---|---|---|---|---|
| MVRV | BTC | 169.0× | 52.3× | 30.2× | 8.1× |
| MVRV | **ETH** | 7.4× | **11.7×** | **39.8×** | **61.8×** |
| MVRV | XRP | 2.7× | 1.8× | 1.5× | **3.8×** |
| NVT | **ETH** | 1.4× | **3.5×** | **8.6×** | **17.1×** |

**ETH gets better as the data gets noisier, by a factor of eight.** That is not a robust edge
degrading gracefully — it is the signal doing nothing, with the outcome decided by whatever exposure
chance hands it. A rule with real information cannot improve when you destroy the information.

*(Note the test design: MVRV and NVT both carry market cap — i.e. price — in the numerator, so the
metric is perturbed by the same cumulative factor as the price. Leaving it clean while noising the
price would have handed the metric a clean signal and a noisy target.)*

## Why the earlier result was true and still failed

The quintile analysis was not wrong. High-MVRV days really did precede higher forward returns
(−1.11 ATR, p = 0.0267, monotone, all three eras), and MVRV really does beat its matched price
baseline, which really does mean it carries non-price information.

But **conditional mean forward return is an exposure statement, not a timing statement.** In an asset
that rose 6000×, "days with property X had better forward returns" is largely a description of when
you happened to be invested. Only the exposure-matched null separates the two, and it says the
selection is no better than random blocks of the same size.

This is the same lesson the Trading Cross taught, arriving from the other direction — there, the
exposure-matched null *rescued* a result a weak null could not support. Here it **kills** one a
strong-looking null appeared to support.

## Standing

**MVRV and NVT are downgraded from "a family with something in it" to "a real conditional
relationship that does not convert into a tradeable rule."**

What survives: MVRV contains information that is not in the price path (it beats a matched price/SMA
baseline that predicts literally nothing, p = 0.995, despite correlating 0.752 with it). That is
genuinely interesting and it is not nothing. It just is not an edge at this formulation, on four
survivor coins, with this cross-section.

**Cross-sectional momentum remains the only result in this lab that has passed a full robustness
pass.**
