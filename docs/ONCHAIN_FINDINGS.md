# On-chain value metrics — the first non-price family, and it is not empty

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

**Monotone.** Low − high quintile: **−1.11 ATR, p = 0.0002**.

The matched price/SMA baseline on the same rows: **0.00 ATR, p = 0.9855.**

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
Raw MVRV < 1.0 did give +1.03 ATR against +0.43 for everything else (p = 0.0002) — the "bottom"
half survives, but note it is an *absolute* level that only occurs in deep bear markets, which is a
different claim from the rolling-z result above.

## Other metrics

| metric | metric gap | matched price/SMA | beats baseline? | eras consistent? |
|---|---|---|---|---|
| **NVT (mcap/transfers)** | **−1.43** (p=0.0002) | −0.52 (p=0.003) | **yes** | **3/3, all significant** |
| **MVRV** | **−1.11** (p=0.0002) | 0.00 (p=0.986) | **yes** | **3/3 same sign** |
| mcap/addresses | −1.00 (p=0.0002) | −0.71 (p=0.0002) | marginally | 2/3 |
| active addresses | −1.07 (p=0.0002) | −0.22 (p=0.095) | yes | 2/3, decaying |
| transfer count | −0.82 (p=0.0002) | −0.54 (p=0.002) | yes | **sign flips era 3** |
| tx count | −0.21 (p=0.095) | −0.29 (p=0.019) | no | **sign flips era 3** |
| hashrate | −0.34 (p=0.046) | −0.52 (p=0.003) | no | sign flips |

**NVT and MVRV are the two that survive everything**: beat their price baseline, monotone, and hold
sign across all three eras. Transfer count and tx count flip sign in the most recent era and should
be treated as dead.

## Caveats

- **Seven metrics tested.** Bonferroni would want p < 0.007; MVRV and NVT clear that pooled, but the
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
