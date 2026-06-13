# Confluence & Sentiment Findings (round 13, 2026-06-12)

Tests the core buy thesis — *"buy when a cycle indicator is at the bottom AND a Cipher B
buy fires AND we're at Cipher SR support"* — and the open question *"does sentiment add
anything?"* All cells run with the look-ahead-honest divergence fix (round 12c) as default.
Walk-forward rolling windows, no-reverse, 10bps + 5bps costs. Control = `v23` base
(Cipher B buy trigger + Anchor<0). `meanR / %windows-positive`.

## The confluence ladder

```
Cell                                    BTC (15 win)        ETH (6 win)
v23 base (Cipher B buy + Anchor<0)      +0.27R / 87%  27tr  +0.29R / 100% 28tr   ← workhorse, highest freq
v23 + Cipher C bottom  (cycle)          +0.29R / 87%  23tr  +0.46R / 100% 22tr   ← cycle helps BOTH
v27 +SR  (support alone)                +0.20R / 73%  12tr  +0.12R /  50%  8tr   ← support: asset-dependent
v27 FULL (B + cycle + support)          +0.19R / 73%   8tr  +0.59R /  83%  9tr   ← thesis: ETH 2× base R
v27 +FEAR (sentiment alone)             0 trades*           +0.30R / 100% 23tr   ← ≈ base (adds nothing)
v27 FULL+FEAR (full + sentiment)        0 trades*           +0.59R /  83%  9tr   ← IDENTICAL to FULL
```
\* BTC sentiment cells = 0 trades because Fear&Greed data starts 2018 and funding 2019, while
BTC's walk-forward spans 2012+; the early windows have NaN sentiment and the gate fails.

## What the confluence layers actually do

1. **Cycle-bottom confluence (Cipher C) is a robust quality improver on BOTH assets.** Adding
   `CIPHER_C.Bottom(any) within 5` to the Cipher B buy lifts per-trade R (BTC +0.27→+0.29,
   ETH +0.29→+0.46) while keeping usable frequency (~22 trades). This is the cleanest part of
   the thesis — *Cipher B buy + cycle at bottom* is genuinely better than Cipher B alone, and
   it generalizes. **Keep and promote.**

2. **Support confluence (Cipher SR / Pivots) is asset-dependent.** On ETH it is excellent
   (`v27 FULL` +0.59R/83%, the quality champion — *double* the base per-trade R at 1/3 the
   frequency). On BTC it *hurts* (+0.19 vs +0.27 base). So the full thesis is the best setup
   **where it works**, but it is not universally better — consistent with the whole session's
   "no one-size-fits-all" result. Use it asset-scoped (ETH-class assets), not as a default.

3. **The full thesis trades frequency for quality.** `v27 FULL` fires ~8 trades/window vs ~28
   for the base. That is exactly the profile you described for a **DCA / accumulation entry**:
   rare, high-conviction, "best buying point" signals. On ETH it is the highest-R cell found
   on honest data this session.

## Does sentiment change anything? — No.

This is the clean answer to the open question:

- **Fear&Greed alone adds nothing.** `v27 +FEAR` on ETH = +0.30R, statistically the same as the
  +0.29R base.
- **Fear&Greed on top of the price confluence is byte-identical to the confluence without it.**
  `v27 FULL+FEAR` == `v27 FULL` (same 8.7 trades, same +0.59R, same windows). The reason is
  structural: **when Cipher B buys at a cycle bottom at support, the market is *already* in
  extreme fear.** Sentiment is a *lagging echo* of what the price structure already encodes, not
  orthogonal information — so gating on it filters nothing and changes nothing.
- **Funding has only a faint, data-starved signal.** `v27 +NEGFUND` fired on SOL (+0.35R, but
  2 windows) and 0 trades elsewhere because funding history is short and sparse.
- **Sentiment data is structurally un-backtestable on the eras that matter.** Fear&Greed exists
  only from 2018, funding from 2019 — so neither can be validated across the maturation history
  of BTC/ETH. Any sentiment edge is unverifiable on the data we have.
- **Cipher S** currently exposes a single `Candle Phase` component — it is not a developed
  sentiment signal, and these results say the sentiment *direction* is low-value: don't invest
  more in it as a sentiment play.

**Bottom line on sentiment:** it is redundant with the price-structure confluence you already
have. The orthogonal information you want is *price-cycle* (Cipher C) and *price-structure*
(Cipher SR), not crowd sentiment. The signal you'd build is already inside A/B/C/SR.

## Recommendations

- **Promote `v23 + Cipher C bottom`** as the robust cross-asset quality long (cycle confluence
  that generalizes).
- **Promote `v27 FULL` (B + cycle + support) asset-scoped for ETH-class assets** as the
  high-conviction / DCA-entry setup — rarest, highest-R, your thesis at its best.
- **Do not add sentiment gates.** They are redundant and un-backtestable. Deprioritize Cipher S
  as a sentiment indicator.
- **Prune candidates** (per this + the round-12 findings): Hurst-gated variants (Hurst proven
  useless cross-asset), blue-dot-alone strategies (no standalone edge, 5× confirmed), and any
  divergence-heavy seed whose library numbers predate the look-ahead fix (re-validate; expect
  demotion). Keep the simple cells: v23 base, v23+C, v27 FULL (ETH).
