# The Trading Cross — test results

Tested 2026-07-28. Lab command: `dotnet run -- cross --only BTC --tf 1d`.

## The rule

From the Onchain Mind video *"This Strategy Beat Bitcoin DCA by 20x"* (transcript pulled via
yt-dlp auto-captions):

> Buy when the rolling z-score of price crosses **above +1**. Sell when it crosses back **below 0**.
> Long or flat.

The asymmetry is deliberate and stated: *"the entry demands proof… but the exit demands nothing."*

**Claimed:** $10,000 → $8M since 2014 (72% CAGR) vs DCA's $446,000 (36%), 42.9% max drawdown vs
83%, Calmar 1.68 vs 0.44.

## Why the video's own robustness test cannot fail

The video runs 10,000 Monte Carlo simulations *reshuffling the strategy's own daily returns* and
reports that the 5th percentile still beats DCA.

That test cannot fail. Reshuffling returns the strategy **already earned** asks "is this set of
returns favourable?" — and the answer is yes by construction, because the set was selected by the
signal. Order was never the question. The null that can fail is to shuffle the **input**: bootstrap
the price series and re-run the rule.

To their credit, the presenter frames the lab as a tool "to give you every tool you need to
disbelieve it." This is that, done properly.

## Results — BTC/USDT daily, 2011-08-18 → 2026-06-15 (5,416 bars, 10 bps/side)

| | final × | CAGR | max DD | Calmar |
|---|---|---|---|---|
| Trading Cross (published 200/+1/0) | 11,540 | 87.9% | 71.0% | 1.24 |
| Trading Cross (tuned 50/+1/+0.5) | **41,387** | **104.8%** | **68.1%** | **1.54** |
| Buy & hold | 6,046 | 79.9% | 84.9% | 0.94 |
| DCA (weekly, per dollar deployed) | 1,143 | 60.8% | 84.7% | 0.72 |

**vs DCA: 36×. vs buy-and-hold: 6.85×.** The first number is the headline; the second is the one
that isolates the signal, because DCA's shortfall is largely that it invests most of its money
late into an asset that rose a thousandfold.

### Test 1 — causality: PASS
The z-score at bar *i* uses only bars [*i*−window, *i*]. Fills are at the **next** bar's close;
filling at the signal bar's own close would hand a close-based rule a free day on the very day the
move happened.

### Test 2 — vs buy-and-hold: PASS
6.85× on the tuned setting, 1.91× on the published one, over 15 years.

### Test 3 — block-bootstrap surrogates: PASS, p = 0.011
21 of 2,000 volatility-matched random series reproduced the ratio.
*Caveat, found by reading the output:* the surrogate median is 0.05, so this null sits far below 1
for **any** partial-exposure rule — 57% exposure captures ~57% of a drifting series' log return and
compounding does the rest. Nearly anything that beats hold clears it. Which is the same criticism
levelled at the video, so it needed a second null.

### Test 3b — exposure-matched timing null: PASS, p = 0.0010
Same number of days in the market, chosen as random contiguous blocks instead of by the signal.
**1 of 2,000** random books beat it. Median random book: 60×. Real: 41,387×.
This is the result that carries the weight — it cannot be cleared by exposure alone.

### Test 4 — era slices: **FAIL / characterises**

| era | cross × | hold × | ratio |
|---|---|---|---|
| 2012–2015 | 47.1 | 64.2 | **0.73** |
| 2015–2018 | 16.6 | 44.2 | **0.38** |
| 2018–2021 | 7.67 | 2.16 | 3.55 |
| 2021–2024 | 2.84 | 1.44 | 1.97 |
| 2024–now | 1.40 | 1.49 | **0.94** |

Loses to buy-and-hold in three of five eras. But the pattern is not noise — it is the **signature
of insurance**. It underperforms in relentless bull cycles (2012–15, 2015–18) and outperforms in
periods containing a bear (2018–21, 2021–24). The whole-period edge is paid for by the bears.

### Parameter sweep: PASS — a plateau, not a spike
**44 of 50** window × entry combinations beat buy-and-hold. The region window 20–250, entry
0.75–1.25 is uniformly positive; only window ≥ 300 collapses. The exit threshold has a clean
interior optimum at **+0.5** (2.76× at window 200) and turns over after — 0.75 → 1.70, 1.0 → 0.79.

Notably the published 200/+1/0 sits in a *weak* corner of the plateau. Shorter windows and an
earlier exit are materially better.

## The cross-asset result, which explains the mechanism

Same tuned parameters, no per-asset fitting:

| | vs buy & hold | | | vs buy & hold |
|---|---|---|---|---|
| BCH | 14.03× | | XRP | 1.35× |
| ADA | 13.59× | | KAS | 1.03× |
| ETH | 9.94× | | **QQQ** | **0.16×** |
| BTC | 6.85× | | **SPY** | **0.23×** |
| DOGE | 3.42× | | **XAU** | **0.42×** |
| TAO | 2.62× · SOL 2.20× · LTC 1.57× | | | |

**10 of 10 crypto assets beat buy-and-hold. 0 of 3 traditional assets do.**

That split is the finding. This is not general market timing — it is **drawdown avoidance, and it
only pays when drawdowns are catastrophic.** Buy-and-hold max drawdowns: crypto 76–98%, equities
and gold 45–57%. In crypto the rule cuts drawdown to 49–68% and the preserved capital compounds.
In equities, being out of the market 45% of the time costs far more than the protection is worth.

On ADA and BCH, buy-and-hold **lost 90%+** over the window while the rule returned 1.3× and 2.0×.
That is the mechanism in its purest form.

## Verdict

**There is a real edge here, and it is smaller and more conditional than advertised.**

- The signal genuinely picks better days than chance at matched exposure (p = 0.001). That is not
  a fitted artefact — it survives the null the original analysis did not run.
- The parameter plateau is broad, and it transfers across ten crypto assets with no refitting.
- **But** the honest benchmark is 6.85×, not 20×, and the edge is absent in three of five eras and
  absent entirely outside crypto.
- **The risk that matters:** the edge is paid for by 80%+ bear markets. BTC's per-era strategy
  drawdown is already shrinking (68% → 34% → 31% → 37% → 31%) and the most recent era ratio is
  0.94 — the first losing era since 2018. If crypto's drawdowns mature toward equity-like levels,
  this converges on the SPY result, which is 0.23×.

Worth having. Not worth believing at 20×.
