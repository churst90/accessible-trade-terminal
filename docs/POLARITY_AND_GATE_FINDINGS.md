# Polarity and the gate — two tests, two negative results, one usable edge
> **RE-RUN 2026-08-27 — reproduces almost exactly; nothing here changes.**
>
> `polarity` and `gate` were both recomputed after the three statistical fixes. This is the doc
> that came through unchanged, and the reason is worth recording: neither command's test was one
> of the three broken ones. `polarity` correlates 49 whole-symbol summary statistics — one row per
> symbol, no overlapping forward windows to be non-exchangeable about — and `gate` compares
> disjoint trade populations rather than searching a grid.
>
> Every rank correlation reproduced to three decimals (rhoZ20 vs depth +0.496 p = 0.0003 pooled,
> +0.359 p = 0.040 within equities, demeaned VR20 vs vol +0.470 p = 0.0010, the crypto sign still
> reversed at −0.564). Every MA-gate lift reproduced exactly (cipherB-long +0.107R, excess over
> random +0.100R). **The one number that moved is the p on the cipherB-long gap: 0.0002 → 0.0004**,
> which is permutation noise at 20,000 draws, not a correction.
>
> A doc that does not move when the machinery is fixed is evidence about the machinery, not only
> about the finding — so this one is worth citing next to XSMOMENTUM and ONCHAIN, which both did.


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
| **cipherB-long** | +0.107R | **+0.100R** | **0.0004** |
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
   longs — on equities. `close > SMA(200)`, +0.10R per trade over random, p = 0.0004, 51 symbols,
   no parameters worth fitting.

The generalisable lesson: both theses died on a control that was cheap to add and that the obvious
version of each test omitted. The random-entry baseline cost twenty lines and turned a tautology
into a real number.

---

# Robustness pass on the SMA(200) dip filter — 2026-07-31

The same four tests cross-sectional momentum passed. **This result does not pass them.**

## 1. Costs — the lift survives, the strategy barely does

Median ATR is **1.71% of price**, so risking one ATR means the position is ~58× the risk unit and a
basis-point cost against notional becomes a large number against R. Cost per round trip in R =
`2 × bps ÷ 10000 ÷ atrPct`.

| bps/side | cipherB-long gated, net | lift vs ungated | excess over random |
|---|---|---|---|
| 0 | +0.202R | +0.115R | +0.106R |
| 2 | +0.179R | +0.113R | +0.107R |
| 5 | +0.143R | +0.110R | +0.108R |
| 10 | **+0.085R** | +0.105R | +0.110R |

**The lift is nearly cost-invariant** — costs hit gated and ungated trades alike and cancel out of a
relative measure. That is a genuine point in the filter's favour.

What costs destroy is the **absolute** return of the thing being filtered: +0.202R → **+0.085R** at
10 bps/side. Still positive, but thin enough that execution quality decides whether it exists.

## 2. Eras — and the control is not clean

Gap per era (four equal-count slices, 1970 → 2026):

| signal | era 1 | era 2 | era 3 | era 4 |
|---|---|---|---|---|
| cipherB-long | +0.31 | +0.14 | +0.24 | +0.15 |
| rsi-bounce-long | +0.15 | −0.06 | +0.25 | +0.04 |
| **random-entry-long** | **+0.11** | **+0.16** | −0.09 | −0.10 |

The random arm is supposed to sit near zero. **In eras 1 and 2 it does not** (+0.11, +0.16), meaning
the filter was picking up plain market direction in those periods rather than signal quality.
Netting it out, cipherB-long's excess over random by era is **+0.20, −0.02, +0.33, +0.25** — three
of four, with one era at nothing.

## 3. Per-symbol — thinner than the pooled number

| signal | symbols positive | median gap | mean gap |
|---|---|---|---|
| cipherB-long | 22/33 (67%) | +0.22R | +0.25R |
| **rsi-bounce-long** | **17/30 (57%)** | **+0.02R** | +0.09R |
| z-reversion-long | 25/33 (76%) | +0.11R | +0.11R |

The rsi-bounce arm is a coin flip across names with a median gap of +0.02R — its pooled +0.084R lift
is carried by a subset. cipherB-long at 67% is the only arm that looks broad.

## 4. Noise injection — this is where it fails

Gaussian noise scaled to each series' own daily volatility, three draws per level:

| noise | cipherB gap | random gap | excess | **vs clean** |
|---|---|---|---|---|
| 0% | +0.200R | +0.017R | +0.183R | **100%** |
| **25%** | +0.054R | +0.015R | +0.039R | **21%** |
| 50% | +0.062R | +0.015R | +0.048R | 26% |
| 100% | −0.016R | −0.015R | −0.001R | −1% |

**79% of the edge is gone at 25% noise.** Cross-sectional momentum retained 86% at the same level.
That is the difference between an effect keyed to the broad shape of a price series and one keyed to
its exact path.

**One honest qualification:** this test cannot fully separate a fragile *signal* from a fragile
*harness*. A 1-ATR stop with a 2R target is inherently path-sensitive — small perturbations flip
trades between win and loss — whereas ranking on a 365-day return is not. Some of the collapse
belongs to the trade construction rather than to the filter. But that distinction offers little
comfort in practice: live fills, spreads and data vendor differences *are* exactly this kind of
perturbation, and the strategy has to survive them as implemented.

## 5. Survivorship — biased the flattering way, and unquantifiable

Unlike the cross-sectional case, this bias runs against us. Every name here recovered from every dip
it ever had — that is what still being listed means. The dips that did not recover belong to
companies that are gone.

Worse for this specific claim: a company heading for delisting spends its final years **below** its
200-day average, so the missing losses would land disproportionately in the gate-**closed** bucket
and would *widen* the measured gap. The filter looks better than it is, for a reason that is an
artefact.

Not stressed with a number, because inventing one here would be inventing the answer.

## Verdict: downgraded

The pooled p = 0.0004 was real, but the effect is **fragile**. It collapses under mild noise, one
era shows nothing, the random control is not clean in two eras, the weaker arm is a coin flip across
symbols, and survivorship biases it in the flattering direction.

**Cross-sectional momentum passed 4/4; this passes 1.5 of 4.** They should not be spoken of in the
same breath, and the earlier framing of "two surviving results" was too generous to this one.

What remains defensible: *if* you are already going to buy dips in equities, doing so only above the
200-day average is better than not — the lift is cost-invariant and positive on 22 of 33 names. That
is a sensible default, not an edge to build on.
