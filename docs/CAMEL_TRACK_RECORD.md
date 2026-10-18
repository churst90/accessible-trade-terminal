# Camel Finance — track record, August 2026

A running ledger of what he said, when he said it, what he was actually holding, and what price did
next. Built from the daily videos so that the method can be scored rather than admired.

**Coverage.** 26 videos listed for August; **22 obtained**. Two (`N3SQsbNkXXU`, `AYvdyIDIQ0I`) are
Level 3 Camel Crew members-only and cannot be retrieved. Two of the listed URLs are dated
2026-09-01 and 2026-09-02 and are included because they close out the August calls. Transcripts are
YouTube's own ASR (not Whisper) — faster, and adequate here; every number quoted below was
cross-checked against a screen frame or against price data, because ASR mangles tickers.
**The gap matters: there is no public video from 16–18 August, which is exactly the three days
Bitcoin bottomed.** The members-only videos likely sit in that gap.

Sources: `docs/CAMEL_CYCLE_LIVE_READ.md` (the method, from a single 2026-08-04 video),
`docs/CYCLE_FINDINGS.md` (the cycle system already tested and largely falsified),
memory `camel-cycle-spec`.

---

## 1. The August tape — what actually happened

Everything below is scored against these. BTC from Bitstamp; equities/metals/rates from Yahoo.

| | 31 Jul | Aug low | Aug high | 31 Aug | 2 Sep | Aug % |
|---|---|---|---|---|---|---|
| **BTC** | 62,818 | **62,653** (Aug 16) | **81,255** (Aug 25) | 78,571 | 76,778 | **+25.1%** |
| SPY | 747.03 | 748.80 | 779.37 | 767.05 | 761.78 | +2.7% |
| QQQ | 687.99 | 685.82 | 734.58 | 716.76 | 707.64 | +4.2% |
| DIA | 524.32 | 527.20 | 546.75 | 531.57 | 527.75 | +1.4% |
| IWM | 291.20 | 292.40 | 305.18 | 293.93 | 290.57 | +0.9% |
| SMH | 540.53 | 524.77 | 600.37 | 556.63 | 545.22 | +3.0% |
| **MSTR** | 93.28 | 90.93 | **139.78** | 132.94 | 124.88 | **+42.5%** |
| Gold (GC) | 4049 | 4026 | **4671** (Aug 24) | 4431 | 4355 | **+9.4%** |
| Silver (SI) | 57.59 | 57.67 | 71.16 | 66.22 | 64.29 | **+15.0%** |
| GDXJ | 95.39 | 94.70 | 136.25 | 127.93 | 122.28 | **+34.1%** |
| SILJ | 24.09 | 23.66 | 33.25 | 31.34 | 30.03 | **+30.1%** |
| TLT | 82.25 | 81.17 | 83.48 | 82.52 | 81.87 | +0.3% |
| WTI | 84.67 | 74.24 | 89.00 | 85.76 | **90.63** | +1.3% |
| DXY | 99.80 | 98.56 | 100.08 | 99.43 | 99.80 | −0.4% |
| US 10y | 4.74 | 4.60 | 4.77 | 4.76 | 4.80 | +6 bp |
| NKE | 41.71 | 38.17 | 43.21 | 39.06 | 38.12 | −6.4% |

**The shape of the month:** a V-bottom in Bitcoin on 16 August at 62,653, then +29.7% to 81,255 in
nine sessions — the two big legs being 19 Aug (+7.8%) and 21 Aug (+7.3%). Precious metals broke out
on the same 19 August candle. Equities ground higher into 25–27 August, then rolled over. Oil
bottomed on 5 August and rallied 20% into 2 September.

---

## 2. The system, as a model

This is reconstructed from what he *does*, not from what a tutorial says. The screen frames confirm
the setup: TradingView, **linear (not log) scale** on the working charts, `TVC:GOLD` and
`BITSTAMP:BTCUSD`, his own **CF Cycle Trading Indicator v2** plotting cycle-low arrows in three
colours (three cycle degrees), and — the useful find — an oscillator pane identified from the object
tree as **`DSS Bressert (10, 9, 5, 80, …)`**, Walter Bressert's Double Smoothed Stochastic. The ASR
renders this as "brelet", "breast" and "BrEazy"; it is Bressert, and it is the thing every
"bracketing cross" refers to. His drawing layers are named `cycles`, `4 year cycle`, `log fib fork`,
`3 angles`, `Hyperwaves`, `WCL and double`, `current TA`.

### 2.1 Seven components

**(a) Nested cycles.** Daily inside weekly inside 4-year ("Russian nesting dolls"). For BTC he uses
~3 daily cycles plus an inversion per weekly cycle. He rejects an October 2026 bottom *specifically*
on the arithmetic: it would need a weekly cycle containing only two dailies, which has not happened
elsewhere in this cycle. This is the same structural argument recorded in `CAMEL_CYCLE_LIVE_READ.md`.

**(b) The "yellow squiggle" — a projected path, not a forecast.** Drawn months ahead and left
unmoved. He is unusually explicit that it predicts neither price nor time: it is "the overall shape
I expect the price to take". Judged as a forecast it is unfalsifiable; judged as a *stated prior
with an attached invalidation level*, it is the opposite — see (c).

**(c) A pre-registered, numeric invalidation.** This is the strongest part of the method and the
part most worth stealing. In August he stated, in public, in advance:

| Thesis | Invalidation he named | Date named |
|---|---|---|
| BTC 4-year decline | **weekly/monthly close above ~83K** (the May 2026 swing-high cluster) | 21 Aug |
| Gold decline | break of the descending trendline, then retest | 6 Aug |
| Disinflation | **three consecutive higher CPI prints** | 15 Aug |
| Rate cuts | 10y breaks range top **and** 2–3 actual hikes | 15 Aug |
| Stocks 4-year low | no 4-year low printed by **April 2027** | 15 Aug |
| "Lost decade" | a sustained higher high after the 4-year low | 15 Aug |
| Recession | no technical recession by ~April 2027 | 15 Aug |

The 15 August video is a deliberate self-audit — he walks each component of his macro thesis and
asks "has this changed, is it working, do more changes seem likely, where is hard invalidation".
That video was published **one day before Bitcoin bottomed.**

**(d) The trade is a low to put a stop under, not a price to buy.** Stated many ways, most cleanly:
*"we're not searching for a number to buy. We are searching for an area on the chart to manage risk
around where the probabilities are stacked in our favor."* This is the risk-placement framing
already flagged in `CAMEL_CYCLE_LIVE_READ.md`, and it is why his cycle work survives his cycle calls
being wrong.

**(e) Deliberate both-sides positioning.** He holds spot BTC *and* a short hedge from just below 80K.
He bought the gold breakout that **invalidates his own gold thesis**, while keeping that thesis as
base case. His words: position "every which way so that no matter what, we can not have to feel too
much FOMO". The purpose is behavioural, not statistical — it removes the incentive to defend a view.

**(f) Scale in, never all-in; prefer the later, tighter entry.** He repeatedly argues that buying the
retest with a stop under the higher low is *better* than buying the low, because the stop is tighter
and the same account risk buys more size. He would rather pay a 2% stop to keep optionality than
wait for certainty.

**(g) Explicit asymmetry arithmetic, stated out loud.** TLT: 20% of the long-term account at an
average of 85; a move to ~68 costs 4% of the account; the thesis pays ~20% of the whole liquid
portfolio. He says the numbers on air. Whatever else, the sizing is not vibes.

### 2.2 The one mechanical part: his cycle-low confirmation checklist

Assembled from 25 and 31 August, where he ran it live on BTC, ETH, MSTR and TLT. This is the piece
that can actually be coded:

1. Pivot/swing low in place, **inside the timing window** for that cycle degree.
2. Price back **above the 10 SMA** of that timeframe.
3. **DSS Bressert bullish cross** out of oversold ("bracketing cross"), ideally with divergence.
4. A **trendline break**.
5. A **large green thrust candle** out of the low.
6. Parabolic SAR flip (secondary).
7. **The confirming close must be on the timeframe of the cycle degree** — weekly close for a weekly
   cycle, monthly close for a 4-year cycle.
8. **Master gate: a higher high close.** *"you can't be a bull and cheer on a bullish uptrend without
   a succession of higher highs and higher lows."*

Note items 1–7 are a confluence score and item 8 is a veto. In August that distinction decided the
whole month — see §4.

### 2.3 The regime gate ("alarm signals")

Checked every weekend, and the most transferable component in the whole system because it is
entirely mechanical and uses public series:

- High-yield credit spreads — blow-out = risk-off.
- 10y−2y curve — an **accelerative** sweep upward (not the level, the acceleration) = tops.
- 10y−3m spread — the sharper version, "gives you less time to react".
- **Gated on:** all three only matter *in conjunction with* breakdowns / failed cycles in equities.

His rule: while these are range-bound, you are in a buy-the-dip environment "no matter how insane
valuations seem". Through August all three stayed range-bound and he stayed long equities. That was
correct.

### 2.4 Lead–lag chains he uses as confirmation

BKLN (leveraged loans) leads SPX · Russell 2K leads the bull-flag break · semis/SMH lead the
indices · **ETH vs BTC — "one of these is lying"** · MSTR leads BTC bottoms because institutions can
buy it more easily. The ETH/BTC divergence framing on 24 August is a genuinely good piece of
reasoning: ETH had made the higher high and BTC had not, and rather than pick, he went and measured
whether ETH has historically led BTC bottoms (answer: inconsistently, and he said so).

---

## 3. The ledger

Positions are as he described them on air. "L3" = called to Level 3 members in real time.

| Date | Video | Calls and positions | What happened |
|---|---|---|---|
| **6 Aug** | Gold's Breakout is Extremely Important | Stocks bullish, blow-off top into year end; long US100/Dow/RUT/semis (L3). BTC "day 837", new lows within 2 months. Gold: expects rejection at descending trendline; **not entering**. | Stocks up. BTC bottomed 10 days later. Gold broke out 13 days later. |
| **7 Aug** | We are printing again | "No work to do." Cites BKLN making new highs → more runway. Cites NDX breadth stat (see §5). Silver day 15, low due ~20 Aug. Flags **16 Sep** as a confluence date. | BKLN read correct. Silver low did not come; silver ran +15%. |
| **8 Aug** | Another Record Close | BTC: weekly cycle left-translating, expects failure and a **4-year low late Dec 2026 / early 2027**; expects "truly negative sentiment" first. Disinflation intact. | Wrong on the immediate direction; BTC +25% that month. |
| **10 Aug** | Gold at Critical Inflection Point | Gold day 17, DCL due 21–24 Aug. BTC: favours "chop at 60K then hot-knife lower", 2018-fractal analogue at 10× the price. DAX-fractal → hard crash into a 4-year low. | Gold DCL window was right in *time* (the high came 24 Aug) but wrong in *direction*. |
| **11 Aug** | New Lows Coming For Crypto?? | ETH to sweep its 4-year low (~$860). "Tennis ball bounce vs egg splat" — says BTC looks like an egg splat. | BTC bounced like a tennis ball five days later. |
| **12 Aug** | Lower CPI to Ignite a Rally? | TLT most contrarian trade, wants a sweep to ~80 then reversal. MSTR: expects one more spill, "everyone will say it's going to zero". BTC rally is futures-led, spot demand negative → more downside. | TLT never swept 80 (low 81.17). **MSTR +42.5% with no position.** |
| **13 Aug** | More Upside Ahead | Gold at "the bull market breakout level", day 20. Margin debt/GDP at record → hard unwind coming. Admits: "I've definitely paid a few stops this year", targeting 20–30% for the year. | Gold broke out six days later. |
| **14 Aug** | Rejected at Resistance | Overlays **Nike on Bitcoin** to argue BTC is bearish. Gold: expects the DCL around 24 Aug. | NKE −6.4%; BTC +25%. The analogy broke immediately. |
| **15 Aug** | Let's be Brutally Honest | **The self-audit.** Every invalidation in §2.1(c) is set here. Confirms BTC short hedge from just below 80K. | Published the day before the bottom. |
| *16–18 Aug* | *— no public video —* | | **BTC prints 62,653 on 16 Aug.** |
| **19 Aug** | Record Shorts to fuel more upside?? | Record Nasdaq futures shorts from asset managers/HFs → squeeze fuel, more equity upside. BTC: "near the end of this countertrend bounce", ~10 days left on the squiggle. Gold "about to have a nasty spill". | Equity call right. Gold broke out **that day** (+3.4%). BTC +18% over the next four sessions. |
| **20 Aug** | The Treasury Just Blinked | **Acts on his own invalidation: takes a gold long** on the breakout-retest. Frame shows entry ≈ 4,483, stop ≈ 4,294 (−4.22%), R:R 8.78, sized so the stop costs ~2% of account. Long anti-FOMO sermon. | Gold ran to 4,671 (+4.2%, ~1R) then back to 4,355. Position still open, underwater, ~70% of the way to the stop as of 2 Sep. |
| **21 Aug** | A NEW BULL MARKET HAS FORMED | Title oversells the content. Names **"83K is the level to watch"** — a close above the May swing-high cluster confirms an early 4-year low. Keeps the bear base case below it. | **BTC's August high was 81,255 intraday / 80,279 closing. The line was never taken.** |
| **22 Aug** | Could it be?? | Full accounting of his own BTC errors: wrong on left translation, wrong on the final blow-off top, **missed the top entirely**. Notes spot entry at 18K is +340%. | Honest, and verifiable from his public record. |
| **24 Aug** | Evidence of a New Crypto Bull is Mounting | Presents both cases. Notes ETH already made its higher high and BTC has not — "one of these is lying". Measures whether ETH leads BTC bottoms historically; concludes inconsistent. TLT 7.5y cycle "month 93 of 93". | ETH's higher high did not drag BTC through 83K. |
| **25 Aug** | Does Sentiment Match a Bottom?? | Runs the checklist (§2.2) live. Gives exact numbers: BTC needs closes above ~83K; a retrace to **74–75K** kills the bullish confirmation; measured move if it breaks = ~118K. Sentiment read: victory laps are atypical of 4-year lows. | BTC topped that day at 81,255. Never broke 83K, never fell to 74K. Both his lines held. |
| **26 Aug** | The Most Important Level In Finance | Reiterates the 83K gate. "FOMO finder" not greedy → more upside available. | — |
| **27 Aug** | Crypto Bull Market Confirms Here!! | Still below 83K. Left-translation cutoff = day 30 from the 16 Aug low. If it Barts, notes the range could run ~22 days to ~12 Sep before the next leg down; next low due ~3–4 Oct. | Consistent with what has happened since. |
| **28 Aug** | Left Translation, MSTR & Vertical Accumulation | Teaching video: defines translation; introduces "vertical accumulation" (a rising wedge that resolves *up*, contra the textbook). Argues an early low is *harder* to trade than a late one. | The "careful what you wish for" argument is the sharpest thing he says all month. |
| **29 Aug** | Jackson Hole Changes Nothing | Rate-hike odds flipped to 60% *for* a hike post-Jackson Hole; he takes the other side. All macro components unchanged. | Unresolved at time of writing. |
| **31 Aug** | A Massive Week Ahead | "More and more bullish on Bitcoin, but not this bullish." Still gated on 83K. TLT: Wyckoff spring, and a genuinely good technical point — **a Wyckoff schematic is invalid without a matching volume-profile fingerprint** (absorption at the lows). | — |
| **1 Sep** | Bitcoin Bulls Got Real Quiet | Sentiment case for a lower high. Notes BTC has used only 84% of the average bear-market duration. | QQQ/SPY rolling over from the 25–27 Aug highs. |
| **2 Sep** | The Funniest Analysis I've Ever Seen | Responds to an LLM-generated "scorecard" of traders. **Declares his gold cycle failed and left-translated** — the squiggle is back in control and he expects to be stopped. Best material: why an X feed cannot be used for sentiment (the algorithm optimises your dwell time, so "everyone is bullish" is a measurement of you). | Gold long still open and underwater at 4,355. |

---

## 4. Scorecard

### Right

| Call | Result |
|---|---|
| **Stay long equities through August** | SPY +2.7%, QQQ +4.2%, SMH +3.0%. Held all month, added on the bull-flag break. |
| **The regime gate stayed green** | HY spreads, 10y−2y and 10y−3m all range-bound all month. The gate said "buy dips" and buying dips worked. |
| **Russell leads the bull-flag break** (13 Aug) | IWM did break first; QQQ/DIA followed within two sessions. |
| **83K as the BTC invalidation line** (21 Aug) | BTC's August high was **81,255 — 1.9% short of it**, closing high 80,279. He named a level eleven days ahead and price stopped under it. This is the single most impressive call of the month. |
| **74–75K as the level that would kill the bull confirmation** (25 Aug) | BTC's post-25 Aug low is 76,380. Both of his lines held; the market stayed in the corridor he defined. |
| **Half-cycle low in equities ~26–27 Aug** | DIA/IWM turned there; SPY made its high 25–27 Aug. Correct in time, though the follow-through failed. |
| **Gold DCL timing window 21–24 Aug** | Gold's high was 24 Aug. The *turn* landed in his window; only the direction of the swing was wrong. |
| **The NDX breadth-recovery statistic** | Independently reproduced — see §5. Real, and asset-specific. |

### Wrong

| Call | Result |
|---|---|
| **BTC to new lows / 4-year low in Dec–Feb** | BTC bottomed 16 Aug and ran +29.7%. As of 2 Sep the call is not *invalidated* by his own rule, but it has been very badly timed for a month. |
| **Oil down to a 4-year low** | The month's clearest unforced error. He said "no changes to oil, still following the yellow squiggle" on 8, 13, 19, 22, 26 and 29 August while WTI rallied **from 75.22 on 5 Aug to 90.63 on 2 Sep, +20.5%**. On 26 Aug: *"I see a lot of people saying that this is bullish, but I just don't think so."* Oil added another 10% after that. He applied none of his own invalidation discipline here, because he had never set a level for oil. |
| **MSTR to make one more low** | +42.5% in August with no position. He talked about it in six videos and bought none of it. |
| **Metals sizing** | Called the metals bull market for months, got the direction right, and captured a fraction of it: GDXJ +34%, SILJ +30%, silver +15%, and he was still "starting to build" at month end. His own patience rule cost him the move. |
| **TLT sweep to ~80 then reverse** | Never swept (low 81.17), never reversed. Flat month on a 20% position at an average of 85. |
| **Yields roll over** | 10y went 4.74 → 4.80. |
| **Nike as a BTC proxy** | NKE −6.4% to new lows; BTC +25%. Chart-overlay analogies with no shared mechanism are the weakest tool he uses. |
| **Gold "reject and go to new lows"** | Gold +9.4% on the month. He was on the wrong side of a breakout he had named the invalidation for two weeks earlier — but he traded it (see below). |

### The interesting split

**His checklist gave a mixed verdict and he resolved it with a veto.** Of the confirmation criteria
he listed on 25 August, Bitcoin satisfied most of them:

- **August monthly close 78,571 vs 10-month SMA 74,151 → ABOVE.** First monthly close above it since
  October 2025. ✔
- Weekly swing low in place, inside the window ✔ · trendline break ✔ · big green thrust ✔ ·
  DSS Bressert bullish ✔
- **Higher high close above ~83K → NO.** ✘

He treated criterion 8 as a veto over criteria 1–7 and stayed bearish. That is a real, describable
decision rule, and it is the difference between his month and everyone else's. Whether the veto is
*correct* is not yet resolvable — but it is at least explicit, which is more than the confluence
score alone would have been.

### The behaviour that is worth more than any of the calls

On 20 August he took a gold long **on the trade that invalidated his own published thesis**, at a
size that cost 2% of account if wrong, while continuing to argue the thesis. There is no ego cost
built into his process because both outcomes were pre-priced. Compare the oil call, where no
invalidation was ever set and he simply repeated "no changes" for four weeks while the chart went
the other way. **The discipline is not a personality trait — it is entirely a function of whether a
level was written down in advance.** That is the finding of this month.

---

## 5. The one claim I actually tested

On 7 August he cited a statistic to justify staying long (not his own work, source unnamed):

> when the Nasdaq 100 moves back within 5% of all-time highs after a recent 10% pullback from its
> 63-day high — historically higher **72%** after 2 weeks, **81%** after 2 months, **88%** after
> 3 months, **97%** after 6 months.

Implemented as: `close ≥ 0.95 × running ATH` **and** at some point in the prior 63 bars
`close ≤ 0.90 × trailing-63-bar high`; signals de-duplicated to one per 63 bars. Daily closes,
QQQ 1999-03→2026-07 (6,886 bars) and SPY 1993-01→2026-07 as the control asset.

**The required control is the unconditional base rate** — QQQ is up over 6 months about 72% of the
time regardless, so "97% after 6 months" needs comparing to that, not to 50%.

| horizon | QQQ signal up% | QQQ uncond. | lift | p (random-date, 2000 draws) | SPY signal up% | SPY lift | SPY p |
|---|---|---|---|---|---|---|---|
| 2 weeks | 82% | 59% | **+23pp** | 0.043 | 62% | +2pp | 0.569 |
| 2 months | 94% | 66% | **+28pp** | **0.011** | 69% | +1pp | 0.569 |
| 3 months | 94% | 68% | **+26pp** | 0.021 | 62% | −7pp | 0.835 |
| 6 months | 88% | 72% | +15pp | 0.137 | 75% | +1pp | 0.576 |

n = 17 signals on QQQ, 16 on SPY.

**Verdict: `Suggestive`, not established.** The effect is real against the base rate and is large at
the 2–3 month horizon, and it is **absent on SPY under the identical rule** — which is a meaningful
control, because it rules out "any index recovering from a drawdown goes up". But: n = 17, eight
tests were run, a Bonferroni threshold would be 0.006, and the signals cluster in a handful of
regimes (2003, 2009–10, 2020, 2023) that overlap heavily. The 6-month horizon — his headline 97% —
does not clear the control at all.

It is worth noting what this means for August: he used a genuinely real statistic to stay long, and
it worked (QQQ +4.2%). That is one observation, and it is the *only* call of the month with a
measured base rate behind it.

Script: `scratchpad/camel/ndx_test.py` (ad hoc). Promoting it to a StrategyLab command is queued
below.

---

## 6. What is transferable to ATT

Ordered by how testable each item is, most testable first. **Nothing here is an endorsement — every
item is a candidate that needs the standard controls before it goes near `Catalogue/edges.json`.**

### 6.1 Implementable now, low risk

1. **DSS Bressert (10, 9, 5, 80, 20)** — a published, fully-specified indicator (double-smoothed
   stochastic). This is the oscillator behind every "bracketing cross" in the channel and it does not
   currently exist in ATT's indicator set. It is a small, well-defined addition with an unambiguous
   causality contract (`docs/CAMEL_TRACK_RECORD.md` §2.2, `causality-contract-for-indicators`).
   Speech design matters here: the useful event is *the cross out of oversold*, not the value.

2. **A "confirmation checklist" composite panel.** Items 1–7 of §2.2 are all things ATT already
   computes or nearly does: pivot detection, 10 SMA, oscillator cross, trendline break, thrust
   candle, SAR flip, timeframe-of-close. Rendering these as a **checklist with a count and a named
   veto** is a genuinely accessible-first design — a screen-reader user gets "5 of 7 confirmations,
   higher high NOT met" in one utterance where a sighted user is squinting at six panes. This is a
   better fit for ATT than any chart overlay.

3. **The regime gate (§2.3) as a spoken status.** HY spread level + 10y−2y **acceleration** +
   10y−3m, gated on equity structure. All three series are already reachable. The output is one
   sentence: *"risk gate: green — spreads range-bound, curve not accelerating."* Benchmark it
   against `close > SMA(200)`, which is the control that killed the Trading Cross gate.

4. **Lead–lag pair monitors.** BKLN→SPX, ETH→BTC, SMH→NDX, MSTR→BTC. Cheap to compute, and the
   "one of these is lying" divergence alert is a good accessible primitive: announce when a
   designated leader makes a structural high/low that the follower has not.

### 6.2 Worth testing before implementing

5. **The higher-high veto.** Does gating a confluence score on "close above the prior swing high"
   actually improve outcomes, or does it just delay entries by a fixed amount? Testable directly:
   score entries by the 7-item confluence with and without the veto, exposure-matched.

6. **"Vertical accumulation"** — his claim that a rising wedge inside an established uptrend resolves
   *upward*, contrary to the textbook. This is falsifiable: define the wedge algorithmically, condition
   on trend state, measure break direction. He gives two examples (BTC, gold), which is where every
   pattern claim in this repo has started and mostly ended.

7. **The Wyckoff volume fingerprint rule** (31 Aug) — that a Wyckoff accumulation schematic without
   absorption volume at the lows is invalid. This is the most specific pattern-validation criterion
   anyone in this project's source material has offered, and it is testable: split candidate
   schematics by volume-profile shape and compare forward outcomes.

8. **Cycle-window timing as a *risk-placement* claim, not a forecast.** `CYCLE_FINDINGS.md` already
   killed cycles as a low *detector*. It did not test what he actually claims: that a stop placed
   under a projected cycle low is less likely to be hit than a stop placed at an arbitrary point with
   the same distance. That is a different, narrower and genuinely testable claim.

### 6.3 Do not re-test

- **"Cycles find lows."** `Falsified` with 200 surrogates (`camel-cycle-counts`). The 54–66 day
  period is a property of the swing detector.
- **Translation as a direction signal.** Already measured: it is momentum in cycle vocabulary,
  positive in crypto, absent in equities. Use `xsmom` instead.
- **Retrospective fractal overlays** (DAX fractal, US500 fractal, 2018 BTC fractal, Nike/BTC). These
  are chosen by looking back at a finished chart; they will fit and they will mean nothing. The
  Nike/BTC overlay failing within five days of being presented is a live demonstration.

### 6.4 Two things that are not features but should change how ATT is built

9. **Feed-derived sentiment is endogenous.** His 2 September point is correct and it applies directly
   to any social-sentiment feature ATT might add: an engagement-ranked timeline is optimised against
   the reader's dwell time, so "everyone is bullish" measures the reader, not the market. If ATT ever
   surfaces social sentiment it must be from an unranked, complete sample or not at all. Worth
   recording against `docs/CONFLUENCE_SENTIMENT_FINDINGS.md`.

10. **Pre-registration as a product feature.** The whole difference between his gold trade and his
    oil call is that one had a written-down level and the other did not. ATT already has a
    `FORECAST_LEDGER.md` and a forward-recorder pattern. The lesson is to make it *impossible* to
    hold a thesis without a stored invalidation — a position or watchlist entry with no invalidation
    level should be announced as such. That is a small feature with a demonstrated failure mode
    behind it.

---

## 7. Falsifiable claims queued from August

| # | Claim | Source | Status |
|---|---|---|---|
| 1 | NDX breadth-recovery → forward returns | 7 Aug (cited) | **Suggestive**, tested above; promote to StrategyLab |
| 2 | Stop under a projected cycle low survives better than an equidistant arbitrary stop | throughout | Untested — the real Camel claim |
| 3 | Higher-high veto improves a confluence entry | 25 Aug | Untested |
| 4 | Rising wedge in an uptrend breaks up ("vertical accumulation") | 28 Aug | Untested |
| 5 | Wyckoff schematic invalid without absorption volume | 31 Aug | Untested |
| 6 | 10y−2y **acceleration** beats `close > SMA(200)` as a risk gate | weekly | Untested; control is mandatory |
| 7 | ETH leads BTC at 4-year lows | 24 Aug | He tested it himself on air and found it inconsistent — worth redoing properly |
| 8 | Weekly cycles decompose into 3 dailies + an inversion | 8, 11 Aug | Untested (carried from `CAMEL_CYCLE_LIVE_READ.md`) |

Verifiable macro assertions he made that were not checked here: 911k jobs revised out Sept-2025;
2.1M over three years; 627k for Apr-25→Mar-26; five of twelve recessions began with unemployment
≤4.1%; TLT's prior 7.5-year cycle was 93 months.

---

## 8. For the July pass

Things to look for specifically, since August raised them:

- **When was the "yellow squiggle" first drawn, and has it moved?** He claims it dates from ~March
  2026 and has never been shifted. July should show it.
- **When and where was the BTC short hedge actually placed?** He says "just below 80K"; BTC traded
  79–82K through May 2026. A July video should date it.
- **Was an invalidation level ever set for oil?** August suggests no. If July also has none, the
  pre-registration finding in §4 hardens considerably.
- **The metals allocation timeline.** He says repeatedly he expects to be "fully exposed within six
  weeks". July will show whether that clock has been running since June.
- **Whether the 15 August self-audit format is periodic or a one-off.** If he does it monthly, the
  invalidation levels can be tracked as a time series and the whole channel becomes scoreable.

---

**Method note.** Transcripts and screen frames are in the session scratchpad
(`scratchpad/camel/{txt,shots,vid}`), not committed. Every price figure in this document is from
Bitstamp's public OHLC API or Yahoo chart data pulled 2026-09-02, not from his charts.
