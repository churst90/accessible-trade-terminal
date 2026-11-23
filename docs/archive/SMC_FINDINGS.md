# Smart money concepts: findings

Run 2026-11-23 to the plan in `docs/archive/SMC_PREREG.md` (committed 92b43916 before any outcome
existed). Command: `StrategyLab smc --snapshots <strategy-lab-data> --era dev`, then `--era holdout`
once. The lab grades the app's own providers (`SmartMoneyProviders.cs`), so these verdicts are about
the lines a user sees on the chart.

## The answer, in plain words

**None of the five concepts carries information in any of the three classes.** Fair value gaps,
order blocks, equal highs and lows, liquidity sweeps and premium/discount all pass the plan's test in
0 of 3 classes. Two of them are **significant the other way** in crypto. A crypto sweep was followed
by price carrying on in the direction it poked, not reversing. And in bullish crypto structure,
buying "discount" did worse than buying "premium". Both point the same way as the project's polarity
finding: crypto trends.

What looks like an edge on a chart is the market's own drift and the geometry of levels:
- **"Gaps get filled" is true, and of any level.** 76 to 83% of gaps are filled within 50 bars. A
  level at the same depth under a random bar's close fills about as often. Gaps fill 1.2 points less
  often than that level in crypto 4h, 0.3 more in crypto 1d and 1.1 more in equities. Equities, on
  87,543 gaps, is the only class under p 0.05, unadjusted. This is the same shape as the
  Fibonacci finding, where a random line is "respected" 59% of the time.
- **Buying the first return to a gap or block made money in crypto daily**: net +0.55 R a trade for
  gaps, +0.38 R for blocks. But the same entry at a depth taken from other gaps did as well. The
  money came from crypto's upward drift after a strong move, not from the level. Against random
  bars the same entries look strong (+0.83 R, +1.31 R): that control does not hold the move that
  made the gap, and the plan named the isolating control as the one that decides.

**What this means for the app** (prereg section 7): all four indicators stay on the chart, drawn and
spoken, because traders talk about them. The manual says the lab found no information in them, and
none of their levels is offered in the strategy builder.

## 1. Data

| class | files | development-era note |
|---|---|---|
| crypto 4h | 7 (bitstamp BTC, DOGE, ETH, SOL, XRP; mexc KAS, TAO) | |
| crypto 1d | 10 (bitstamp ADA, BCH, BTC, DOGE, ETH, LTC, SOL, XRP; mexc KAS, TAO) | |
| equities 1d | 38 Yahoo series (ETFs included) | |

No file was skipped for being short. Development era: event bars before 2023-01-01.

## 2. Development era (decides every verdict)

The statistic is the event's R minus its control, averaged over events. The p-values are one-sided
year-block bootstraps (10,000 resamples). Holm runs over the five primaries in each class. "Mirror p"
is the same test the other way round; below 0.05 means significant against the teaching.

### Primaries

| test | class | stat | p | Holm | mirror p | n | event arm gross / net R |
|---|---|---|---|---|---|---|---|
| F1 gap is support | crypto 4h | +0.026 | 0.230 | 1.000 | 0.770 | 4,761 | +0.192 / +0.063 |
| | crypto 1d | +0.071 | 0.050 | 0.200 | 0.950 | 1,706 | +0.598 / +0.550 |
| | equities 1d | +0.002 | 0.348 | 1.000 | 0.652 | 79,846 | −0.030 / −0.220 |
| O1 block is support | crypto 4h | −0.041 | 0.858 | 1.000 | 0.143 | 1,309 | −0.186 / −0.320 |
| | crypto 1d | +0.140 | 0.016 | 0.078 | 0.985 | 391 | +0.428 / +0.378 |
| | equities 1d | +0.021 | 0.167 | 0.832 | 0.834 | 11,786 | +0.020 / −0.169 |
| E1 equal highs draw price | crypto 4h | +0.026 | 0.213 | 1.000 | 0.787 | 184 | 0.685 taken |
| | crypto 1d | −0.078 | 0.884 | 1.000 | 0.116 | 51 | 0.569 taken |
| | equities 1d | −0.009 | 0.753 | 1.000 | 0.247 | 1,435 | 0.677 taken |
| L1 a sweep reverses | crypto 4h | **−0.538** | 1.000 | 1.000 | **0.0001** | 1,805 | −0.521 / −0.656 |
| | crypto 1d | **−0.887** | 1.000 | 1.000 | **0.0001** | 495 | −0.742 / −0.790 |
| | equities 1d | −0.022 | 0.642 | 1.000 | 0.358 | 12,511 | −0.037 / −0.222 |
| P1 discount in bull structure | crypto 4h | **−0.410** | 1.000 | 1.000 | **0.0001** | 25,393 | −0.140 / −0.276 |
| | crypto 1d | −0.333 | 0.898 | 1.000 | 0.102 | 8,149 | +0.039 / −0.010 |
| | equities 1d | −0.028 | 0.707 | 1.000 | 0.293 | 187,023 | +0.002 / −0.181 |

For F1 and O1 the event arm is the trade itself; the control is the same event entered at a depth
drawn from the other gaps (or blocks) on the same side. For E1 it is the rate at which equal highs
were taken within 50 bars, and the control is every other swing high in the same distance bin. For L1
it is the sweep trade, with 20 random same-direction entries as the control. For P1 it is the bars
traded "with the teaching" (discount in bullish structure, premium in bearish, shorted).

### Counts

| class | F1 events / filled / never returned / no control draw filled | O1 the same | E1 equal / other swings / outside 0-5 ATR | L1 sweeps | L2 breakouts | P1 with / against the teaching |
|---|---|---|---|---|---|---|
| crypto 4h | 5,428 / 4,762 / 666 / 1 | 1,753 / 1,312 / 441 / 3 | 184 / 4,590 / 2 | 1,805 | 1,762 | 10,281 / 15,112 |
| crypto 1d | 1,982 / 1,708 / 274 / 2 | 551 / 391 / 160 / 0 | 51 / 1,446 / 2 | 495 | 576 | 3,110 / 5,039 |
| equities 1d | 87,543 / 79,879 / 7,664 / 33 | 14,869 / 11,799 / 3,070 / 13 | 1,435 / 33,947 / 16 | 12,511 | 15,738 | 72,321 / 114,702 |

### Secondaries and the random-anchor controls (unadjusted, reported, decide nothing)

| test | class | stat | p | mirror p | n | note |
|---|---|---|---|---|---|---|
| F1 random anchor | crypto 4h | +0.308 | 0.0001 | 1.000 | 4,762 | the level at the same depth under a random bar |
| | crypto 1d | +0.834 | 0.0002 | 1.000 | 1,708 | |
| | equities 1d | −0.054 | 0.985 | 0.016 | 79,879 | equities revert after the move |
| O1 random anchor | crypto 4h | +0.097 | 0.115 | 0.885 | 1,312 | |
| | crypto 1d | +1.310 | 0.0001 | 1.000 | 391 | |
| | equities 1d | −0.024 | 0.691 | 0.309 | 11,799 | |
| F2 gaps get filled | crypto 4h | −0.012 | 0.883 | 0.117 | 5,428 | 77.4% filled |
| | crypto 1d | +0.003 | 0.401 | 0.599 | 1,982 | 75.7% filled |
| | equities 1d | +0.011 | 0.0035 | 0.997 | 87,543 | 82.6% filled |
| L2 sweep minus breakout, both shorted | crypto 4h | +0.136 | 0.059 | 0.941 | 3,567 | |
| | crypto 1d | +0.785 | 0.016 | 0.984 | 1,071 | |
| | equities 1d | −0.079 | 0.956 | 0.044 | 28,249 | |
| P2 discount minus premium, all bars | crypto 4h | −0.422 | 1.000 | 0.0001 | 25,417 | |
| | crypto 1d | −0.611 | 0.957 | 0.043 | 8,229 | |
| | equities 1d | +0.059 | 0.114 | 0.886 | 187,337 | |

### Verdicts, written down before the held-out era was read

| concept | passes Holm | significant the other way | verdict |
|---|---|---|---|
| Fair value gaps (F1) | 0 of 3 | none | **No information** |
| Order blocks (O1) | 0 of 3 | none | **No information** |
| Equal highs and lows (E1) | 0 of 3 | none | **No information** |
| Liquidity sweeps (L1) | 0 of 3 | crypto 4h, crypto 1d | **No information** (the teaching is wrong in crypto) |
| Premium and discount (P1) | 0 of 3 | crypto 4h | **No information** (the teaching is wrong in crypto 4h) |

The closest any concept came was order blocks in crypto daily: p 0.016, Holm 0.078 on 391 returns.
Section 5's "worth trading" is not reached, because no concept has information.

## 3. Held-out era (2023-01-01 on, signs only)

To be read once, after the commit that holds section 2.

## 4. What the crypto reversals are and are not

The two "other way" results were not the pre-registered claim. They are leads, not findings. Both
say the same thing as the polarity finding (ALPHA_LEDGER #3: crypto trends, equities revert):
- **Sweeps:** after a crypto bar trades through a swing high and closes back under it, price over the
  next 20 bars went **up** more than at random bars. Shorting the sweep lost 0.5 to 0.7 R a trade
  before costs. L2 says breakouts kept going even harder, so the sweep is the milder continuation,
  not a reversal.
- **Premium and discount:** in bullish 4-hour crypto structure, bars in the top half of the dealing
  range went on to do better than bars in the bottom half. Strength followed strength.

Neither is offered as a signal. Testing either as its own claim needs a new plan written before the
look, and the polarity finding already covers it.

## 5. Interpretation notes, fixed before the first run

- **One bar can take two levels on the same side**, for example two swing highs at once. That is one
  sweep (or one breakout) on that bar, keyed on the farthest level swept or the nearest broken.
  Otherwise one candle would count twice.
- **The isolating pool** for F1 and O1 is every development-era gap (or block) on the same side in the
  class. The 20 depth draws are made for every event, whether or not the event itself filled, so the
  random stream does not depend on the outcome.
- **A held-out reading is the sign of the primary statistic only.** No held-out p-value is a verdict.

## 6. Caveats

- Overlapping events share outcomes, so the development p-values are optimistic. None of them passed
  anyway, so the bias runs against a finding that was not made.
- The definitions are one reasonable reading of how SMC is taught: 5-bar swings, a 0.1 ATR gap
  floor, the last opposite candle within 10 bars, and a 0.1 ATR tolerance for equal levels. Other
  scripts draw them other ways. This tests what the app draws, not every variant, and no variant was
  tried: there is one setting per concept, so variants tried = 5 primaries.
- Equities are 38 large US names and ETFs. Crypto is 10 assets, which are not 10 independent samples.
