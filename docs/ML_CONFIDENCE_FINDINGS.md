# ML Confidence-Model Experiment (round 14, 2026-06-14/15)

Tests the "train a model on OHLCV to make an asset-tuned master indicator that outputs
confidence-scored buy/sell signals" idea — starting with the cheap falsification: *do the
features rank trade outcomes out-of-sample?* If not, the edge isn't ML-extractable and we
learned it in an afternoon instead of weeks.

## Setup

- **Data:** refreshed Bitstamp snapshots through 2026-06-15. 1d (21,118 rows / 10 assets) and
  4h (86,791 rows / 7 assets), via `StrategyLab ml-export`.
- **Features (causal only):** 13 trailing-window indicator values (Cipher B WT1/WT2/hist/MFW/
  Anchor, Cipher C sine/lead, Hurst, VolRatio/VolPercentile/VolState, RegimeState) + 5 engineered
  (ret1, ret5, atr_pct, dist_sma_pct, range_pct) + 3 causal buy-signal flags (WT-cross/blue/gold).
  **Deliberately excluded** divergence/SR/pivot markers — we confirmed they stamp at the pivot
  bar using future bars (the look-ahead bug), so they would leak.
- **Label:** triple-barrier, long. From each bar, +1.5·ATR target vs −1.0·ATR stop over 20 bars.
  `win=1` if target first. Base win rate ≈ 0.39–0.42.
- **Validation:** strictly chronological walk-forward (train only on the past, no shuffling).
  LightGBM. Asset NOT used as a feature (testing for a universal signal); per-asset AUC reported.

## Results

| Test | 1d | 4h |
|---|---|---|
| Pooled OOS AUC | 0.517 | 0.522 |
| Fold stability | ±0.018 | **±0.006** (very stable) |
| Calibration | weak/noisy | **monotonic** (decile 0→37%, decile 9→43% win) |
| Confidence lift (top-20% − bottom-20% win) | +0.042 | +0.046 |
| Meta-model on signal bars (AUC) | — | 0.524 |
| Confidence tier adds to a fired signal's win rate | — | +0.02 to +0.05 |
| **Asset-tuned (per-asset trained)** | — | **0.47–0.52 (no better, often worse)** |

## What it means — honest read

1. **Not a coin flip, but not an oracle.** OOS AUC ≈ 0.52, rock-stable across folds on 4h, with
   *monotonic* calibration. There is a **weak but real and persistent** edge — well below the
   0.55 bar I'd want before building a large system, but not nothing.

2. **A "master indicator that fires accurate buy/sell at every bar" does not work.** At the bar
   level the features barely rank forward outcomes (0.52). The market is ~95% noise at this
   resolution — consistent with every other result this research arc.

3. **The Cipher B signals carry ≈ZERO forward-predictive information.** `sig_wtx/sig_blue/sig_gold`
   get ~0 feature importance — the model ignores them entirely. This is the deepest finding: the
   oscillator *fires* are not where edge lives. (Matches the suite's standing "blue dot has no
   edge of its own" result, now confirmed by an independent method.)

4. **What little predictive power exists is REGIME / VOLATILITY context.** Top features by gain:
   `dist_sma_pct, vol_ratio, vol_pct, atr_pct` — *where the asset is in its trend/vol regime*
   matters far more than any oscillator value. The model independently rediscovered the
   maturation thesis we built the asset-profiler around.

5. **Asset-tuned models do NOT beat the universal one** (the user's core hypothesis). Per-asset
   training (less data) lands at 0.47–0.52 — at or below the pooled model. The bottleneck is
   *signal*, not asset-specificity. Pooling more data helps slightly; specializing hurts.

6. **The confidence-tint UX is viable but modest.** Top-confidence signals win ~+2–5% more than
   bottom, and the calibration is honest/monotonic — so tinting a dot or pitching an earcon by
   model confidence *would* carry real information. But it's a nudge (≈43% vs 38% win), not a
   night-and-day filter. Worth shipping as an *honest* confidence layer; not worth overselling.

## Verdict

- **As a buy/sell oracle: no.** The edge isn't ML-extractable from this feature family at the
  bar level, on either timeframe, pooled or asset-tuned.
- **As a calibrated confidence / risk-tint layer on existing setups: marginally yes.** It produces
  a monotonic, honestly-calibrated probability that adds a few points of win-rate separation —
  exactly the "tint by confidence / pitch by risk" accessibility feature, just modest in size.
- **Biggest takeaway:** the model agrees with everything we've learned — edge is faint and lives
  in *regime/volatility context*, not in oscillator signals. Build the confidence layer on
  regime/vol features, set realistic expectations, and don't expect ML to find edge that the
  honest backtests already showed isn't there.

## What could still move the needle (and the odds)

- **Stocks (needs the Alpaca key).** Equities have more persistent factor/regime structure than
  crypto; the maturation→edge relationship might be stronger and more ML-extractable. This is the
  one genuinely promising expansion — and the real generality test. *Blocked on your API key.*
- **Sequence models on raw multi-TF bars** (Temporal CNN / small Transformer) — could capture
  patterns the tabular features miss, but want more data than crypto gives and overfit easily.
  Modest odds; large effort.
- **Different label horizons / barrier ratios** — quick to sweep; unlikely to change the ~0.52
  ceiling given how stable it is, but cheap to confirm.
- **Adding analytics/sentiment as features** — we already showed sentiment is redundant with
  price-regime confluence (round 13); low odds it helps here.

## Reproduce

```
# refresh data (Bitstamp; needs network):
StrategyLab snapshot --symbol BTC/USDT --tf 4h --bars 20000 --out ../strategy-lab-data
# export features+labels:
StrategyLab ml-export --snapshots ../strategy-lab-data --tf 4h --out ../strategy-lab-data/ml/ml_4h.csv
# train + evaluate (venv with numpy/pandas/scikit-learn/lightgbm):
python ml/train.py ../strategy-lab-data/ml/ml_4h.csv
```
