# Polarity and the gate — two tests, two negative results, one usable edge

Run 2026-07-29. Commands: `dotnet run -- polarity` and `dotnet run -- gate`.
51 daily series (33 equities/ETFs, 10 crypto, 4 commodities, 2 bonds); 55k gated trades.

Both tests were designed to confirm a thesis. Neither did. What survived is smaller than
either thesis and more useful than most of what has cleared here.

---

## Test 1 — does drawdown depth predict polarity?

**The thesis.** Four separate studies landed on the same split without anyone looking for it: POC
deviation reverts in equities and reverses in crypto; Value Deviation has to invert for crypto;
the Trading Cross (which *buys* extension) beat hold on 10/10 crypto and 0/3 traditional; the
favourability gradient favoured momentum. If the variable underneath is drawdown depth rather than
the word "crypto", the sign on every deviation tool could be set from a measurable number.

**Measures.** Polarity = corr(z-score, forward k-bar return), positive = trends; plus the
Lo–MacKinlay variance ratio as a model-free cross-check. Depth = median rolling 365-**calendar**-day
max drawdown — the full-sample maximum grows with sample length, and the equity history here is 30
years against crypto's 4, which would have manufactured the result outright.

### The class split is real

| class | n | VR(20) | rhoZ20 | depth | vol |
|---|---|---|---|---|---|
| crypto | 10 | **1.150** | +0.075 | 68% | 98% |
| commodity | 4 | 0.984 | +0.039 | 21% | 27% |
| bond | 2 | 0.869 | +0.045 | 10% | 11% |
| equity | 33 | **0.820** | −0.047 | 19% | 25% |

Crypto trends (VR > 1), equities revert (VR < 1). Monotone in volatility across classes.

### But depth is not the variable

Pooled correlation is strong (rhoZ20 vs depth +0.496, p = 0.0003) and means nothing on its own —
crypto has both the deepest drawdowns and the most momentum, so it may only be re-encoding the
class label. The tests that can fail:

- **Within equities (n=33):** rhoZ20 vs depth +0.359, p = 0.040. Jackknife-stable (+0.297 dropping
  AAPL … +0.417 dropping CVX), so not one outlier.
- **Within crypto (n=10):** −0.564, p = 0.098 — **wrong sign**, not significant.
- **Demeaned by class (n=49):** VR20 vs depth +0.449 (p = 0.0012), VR20 vs vol +0.470 (p = 0.0010).
- **Partial correlations:** depth and vol rank-correlate **+0.96**. Neither partial clears
  (depth p = 0.68, vol p = 0.27), and the two polarity measures *disagree* about which one matters —
  rhoZ20 says volatility (+0.519, p = 0.0022 in equities), VR20 says depth (+0.325, p = 0.026 pooled).

**Verdict.** A volatility-family variable does predict polarity within class. This sample cannot
say whether it is drawdown depth or realised volatility, and it is honest to admit that rather than
to rank two noise-level partials. More importantly the relationship is **not monotone**: it rises
from bonds through equities into crypto and then flattens or reverses. A global "more volatility =
more momentum" switch would get crypto backwards.

**So: keep the hard asset-class fork. Do not ship a continuous polarity switch.**

---

## Test 2 — does the Trading Cross z-state work as a gate for other signals?

**The thesis.** The Trading Cross cleared an exposure-matched null at p = 0.001 on only 70 trades in
15 years — an exposure decision wearing a trade's clothes. And `ConfluenceCommand` found stacking
confirmations added nothing. So: one layer decides whether the others may speak.

**The control that makes it falsifiable.** "Do longs earn more when the gate is open?" is
guaranteed to say yes and to mean nothing, because the gate is open when price has been rising.
Every result is therefore reported beside the same measurement using a plain 200-bar moving average
as the gate — and beside **random entries**, which measure the lift any long inherits from an
uptrend regardless of signal.

### The gate is structurally incompatible with dip-buying

| signal | mean z at signal | gate open |
|---|---|---|
| cipherB-long | −1.45 | **0 of 3,059** |
| rsi-bounce-long | −1.95 | **0 of 3,954** |
| z-reversion-long | −1.37 | **0 of 12,718** |
| cipherB-short | +1.42 | 87% |
| breakout-long | +1.99 | 88% |

The gate opens above z = +1 and closes below z = +0.5. Every "buy the dip" signal fires below the
mean. **They occupy disjoint states.** Gating a reversion signal with the Trading Cross is not an
architecture — it is a rule that vetoes 100% of its inputs. This is arithmetic, not a result, and
saying "the gate filtered them all out" would have been a false finding.

### Where the gate *can* be open, it does nothing

- cipherB-short: gap **+0.001R**, p = 0.985
- breakout-long: gap +0.037R, p = 0.128
- random entries: gap +0.004R, p = 0.869

**Verdict: the gate-not-stack thesis fails.** The z-state earns its keep as an exposure rule on its
own book and not as context for other signals.

---

## What actually survived: buy dips only above the 200-day MA

The MA control was supposed to be a null. It is the finding.

| signal | MA-gate lift | excess over random | p on the gap |
|---|---|---|---|
| **cipherB-long** | +0.107R | **+0.100R** | **0.0002** |
| **rsi-bounce-long** | +0.094R | **+0.087R** | 0.073 |
| breakout-long | +0.002R | −0.005R | 0.632 |
| cipherB-short | −0.001R | −0.008R | 0.930 |
| *random entries* | *+0.007R* | *—* | *0.518* |

**Random longs gain +0.007R from the filter — essentially nothing.** So this is not the "longs win
in uptrends" tautology. The lift is specific to mean-reversion entries: dip-buying is a falling
knife below the 200-day average and a pullback buy above it. Breakouts do not care, and shorts do
not care.

Split by class, it is an **equities** result: cipherB-long gap **+0.212R, p = 0.0001** on the 33
equity names, against +0.095R, p = 0.65 on crypto (n=184, too small to say). That is exactly what
Test 1 predicts — equities are the reverting class, so a dip-buy is the right tool there, and it
needs a trend filter to keep it out of genuine declines.

One more result worth recording: on equities the **cross gate makes random longs worse**
(−0.067R, p = 0.029). The Trading Cross is not merely useless outside crypto, it is inverted —
consistent with its 0.23× on SPY.

---

## What to do with this

1. **Keep the asset-class fork.** Polarity is real and large, but no single continuous variable
   reproduces it cleanly enough to ship.
2. **Do not build the regime-gate layer.** It fails its control, and against dip-buys it is
   arithmetically self-defeating.
3. **Do add a trend filter to the mean-reversion tools** — Value Deviation, POC deviation, Cipher B
   longs — on equities. `close > SMA(200)`, +0.10R per trade over random, p = 0.0002, 51 symbols,
   no parameters worth fitting.

The generalisable lesson: both theses died on a control that was cheap to add and that the obvious
version of each test omitted. The random-entry baseline cost twenty lines and turned a tautology
into a real number.
