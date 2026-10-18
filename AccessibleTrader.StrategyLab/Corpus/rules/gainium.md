# Gainium blog (Ares Sanchez): extracted rules

Source slug `gainium`, school **market-cipher**. One web page: "VuManChu Cipher B indicator explained", by Ares Sanchez (founder of Gainium, a crypto trading-bot platform), dated 2026-04-06, https://gainium.io/blog/vumanchu-cipher-b-indicator-explained. **1 page read in full.** Kind of material: an educational explainer that doubles as product marketing (it ends with how to wire Cipher B alerts into Gainium bots by webhook). Public. It describes the **free VuManChu Cipher B + Divergences** script, not paid Market Cipher B. No timestamps exist. Citations name the page section, linked to the page URL.

| Date | Id | Title | Read |
|---|---|---|---|
| 2026-04-06 | gainium-vumanchu-cipher-b-explained | VuManChu Cipher B indicator explained (Ares Sanchez) | in full |

## Method in its own terms

The page explains Cipher B as **four visual layers**: WT1/WT2 momentum waves, dots, circles and divergence lines, plus the money flow histogram. It then gives recipe setups. The main one is the **"Classic Green Dot Reversal"**: waves below −60, a green dot, money flow shifting from red to green, entry on the candle after the dot, a stop below the recent swing low, and a target at the next resistance or a red dot / bearish cross. Others are a divergence plus wave cross, a zero-line reclaim, and the bearish mirrors. Filters it recommends: only buy above the 200 EMA, RSI(14) < 30 confluence, a volume spike, and a support level. Start on 4H/daily. "Don't trade every dot."

## Anchor wave, trigger wave, money flow and confirmation (exact)

- **Anchor wave / trigger wave.** Nothing stated. The page never uses the terms.
- **Depth (exact).** OB "typically above 60" and OS "below −60". "Signals generated in these extreme zones carry more weight" ([Gainium, "The Momentum Waves"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). Classic setup: "Waves (WT1 and WT2) are deep in the oversold zone (below −60)", so **both lines**, not just WT2 ([Gainium, "The Classic Green Dot Reversal"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). "Green dots that appear deep below the zero line (in the oversold zone) are considered the highest-probability buy signals" ([Gainium, "The Dots"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- **Money flow (exact).** Classic setup: "Money flow histogram shifts from red to green". The order relative to the dot is not stated; it is listed after the dot ([Gainium, "The Classic Green Dot Reversal"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). In general: "a green dot appearing with green money flow is a higher-confidence buy than a green dot appearing during red money flow" ([Gainium, "The Money Flow Histogram"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). No thickness or depth.
- **What it WAITS FOR after a green dot prints (exact).** **The dot candle's close**: "Entry: On the candle following the green dot" ([Gainium, "The Classic Green Dot Reversal"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). For the divergence setup, entry is "On the WaveTrend crossover after divergence confirmation" ([Gainium, "Bullish Divergence + Wave Cross"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- **Confirmation / invalidation of a dot.** "Don't trade every dot ... Wait for confluence with money flow, divergence, and price structure" ([Gainium, "Tips"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). No invalidation rule beyond the stop.
- **What a dot is, per the page.** "Green dots appear when multiple algorithms converge in oversold territory — WaveTrend crosses bullish, money flow is positive, and momentum is aligned" ([Gainium, "The Dots"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). "Large green circles — WaveTrend bullish crossover happening inside the oversold zone". "Small green/red circles — simple WaveTrend crossovers" ([Gainium, "The Circles"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). **Caution (ours, not the page's):** as we read VuManChu's published script, its buy circle is a WT cross up with WT2 at or below its oversold level (default −53), with no money flow condition. The page's "money flow is positive" may be wrong. Check against the script before using the page as a definition. The grader uses WT2 ≤ −60.
- **Timeframe pairing.** None. Use higher timeframes: "The 4H and daily charts produce the most reliable signals" ([Gainium, "Tips"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Context / higher timeframe / regime

- "Only take Cipher B buy signals when price is above the 200 EMA (long-term uptrend)", and sells only below it. A 50/200 golden or death cross "combined with Cipher B wave direction gives a macro-level confirmation" ([Gainium, "Cipher B + Moving Averages"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- "Both waves above the zero line — the market is in a bullish phase". Below zero = bearish phase. "Compressed and flat near zero" = consolidating ([Gainium, "The Momentum Waves"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Location (levels, zones, where a setup counts)

- "Green dot at a major support level → strong long entry". "Red dot at a major resistance level → strong short or exit signal". "Divergence at a range boundary" ([Gainium, "Cipher B + Support/Resistance Levels"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- Overbought rejection at resistance: price approaches resistance, the waves are overbought and flatten or curl down, then a red dot or large red circle, and money flow shifts red ([Gainium, "Overbought Rejection at Resistance"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Entry triggers

- **Classic green dot reversal**: waves < −60, then a green dot, then MF red→green. Enter on the next candle ([Gainium, "The Classic Green Dot Reversal"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- **Bullish divergence + wave cross**: price lower low with waves higher low, then WT1 crosses above WT2. A green or gold circle "may" reinforce it. Enter on the cross ([Gainium, "Bullish Divergence + Wave Cross"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- **Zero-line reclaim**: waves below zero curl up, WT1 crosses above zero with WT2 following, and MF is green or turning green. Enter "When both waves are above zero and WT1 > WT2". "works well in trending markets" ([Gainium, "Zero-Line Reclaim"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- **Bearish mirrors**: red dot rejection (waves > 60, red dot, MF green→red, enter on the next candle, stop above the swing high). Also bearish divergence + wave cross ([Gainium, "Bearish Setups"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Confirmations (things that raise conviction)

- "Cipher B green dot + RSI below 30 → strong oversold confluence" (standalone RSI 14). Red dot + RSI above 70. Matching RSI divergence → "very high-confidence" ([Gainium, "Cipher B + RSI"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). Terminal: has RSI.
- "Green dot + volume spike above average → buyers are stepping in". A green dot on low volume "may lack follow-through" ([Gainium, "Cipher B + Volume"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). Terminal: has volume.
- Gold/orange circles: "a bullish signal that appears after strong bullish divergence in extreme oversold conditions". Purple triangles: "divergences confirmed by WaveTrend crossovers at overbought or oversold levels" ([Gainium, "The Circles"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- Divergences on 4H/daily "are generally more reliable" ([Gainium, "Divergence Lines"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- "Wide gap between WT1 and WT2 — strong momentum in the current direction" ([Gainium, "The Momentum Waves"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Vetoes (things that cancel a trade whatever else says)

- Below the 200 EMA → no buy signals ([Gainium, "Cipher B + Moving Averages"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- Soft: dots in "choppy, low-volume markets" produce false signals ([Gainium, "Tips"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Stops and invalidation

- Classic long: "Below the recent swing low". Divergence long: "Below the divergence low". Red dot short: "Above the recent swing high" ([Gainium, "The Classic Green Dot Reversal"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained); [Gainium, "Bullish Divergence + Wave Cross"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained); [Gainium, "The Red Dot Rejection"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). No buffer.

## Exits and targets

- Classic long: "The next resistance level, or when a red dot or bearish crossover appears" ([Gainium, "The Classic Green Dot Reversal"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- A bearish divergence + cross or a red dot rejection also serve as "exit long" ([Gainium, "Bearish Setups"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Position sizing and risk

- "Always use a stop-loss and manage position size accordingly". Paper trade first ([Gainium, "Tips"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained); [Gainium, "Step 4: Paper Trade First"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). No numbers.

## Timeframes

- 4H and daily are "the most reliable". "The default settings are tuned for 4-hour charts". On 1H or 15m, "experiment with shorter lengths" ([Gainium, "How to Add VuManChu Cipher B to Your Chart"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained); [Gainium, "Tips"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Macro context

- Nothing stated.

## Indicator settings stated (exact numbers: lengths, thresholds, colours meaning)

- "The default WaveTrend channel length (9) and average length (12) are tuned for 4H" ([Gainium, "Tips"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- OB 60 / OS −60 ([Gainium, "The Momentum Waves"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)). (VuManChu's script has several levels. This page names only ±60.)
- WT1 = the faster, lighter line (leading). WT2 = the slower, darker line (lagging) ([Gainium, "The Momentum Waves"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- Money flow: green = net buying, red = net selling ([Gainium, "The Money Flow Histogram"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).
- Standalone RSI period 14; 30/70 ([Gainium, "Cipher B + RSI"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Calibration numbers and falsifiable side-claims (win rates, base rates, "X happens N% of the time")

- None numeric. Falsifiable:
  - signals beyond ±60 are "significantly more reliable than those near the zero line" ([Gainium, "Tips"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained));
  - a green dot with green money flow beats one with red money flow ([Gainium, "The Money Flow Histogram"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained));
  - low-volume dots lack follow-through ([Gainium, "Cipher B + Volume"](https://gainium.io/blog/vumanchu-cipher-b-indicator-explained)).

## Dated calls (for later scoring)

| Video date | Asset | Direction | Level / target / invalidation | Horizon stated | Citation |
|---|---|---|---|---|---|
| — | — | — | None. Explainer only. | — | — |

## Candidate rules NOT in the pre-registered setup grader

1. **Entry on the candle after the dot**, with both WT1 and WT2 < −60 at the cross (the grader requires only WT2 ≤ −60 and says nothing of next-bar entry). Computable.
2. **Dot + MF red→green within k bars** (MF crosses above 0 within k bars after the dot; k not stated). The grader has "MF rising" but not a zero cross. Computable.
3. **Exit at the first red dot (bearish cross) after entry, or at the next resistance**. The grader already compares the opposite-wave-cross exit, so this is not new apart from the resistance target, which needs a resistance definition.
4. **RSI(14) < 30 at the green dot** as a confluence. Computable. Terminal: RSI.
5. **Volume spike at the dot**: volume ≥ k × its N-bar average (k, N not stated). Computable. Terminal: volume.
6. **Zero-line reclaim (trend re-entry)**: after a period with WT1, WT2 < 0, enter at the first bar where WT1 > 0, WT2 > 0 and WT1 > WT2, with MF ≥ 0 or rising. Computable. It is a separate setup from the Buy dot.
7. **200 EMA filter on the trading timeframe** (the grader's context is weekly trend/phase). Computable.

## Not usable / vague

- "Multi-algorithm convergence" as the dot definition: probably inaccurate for the free script (see the caution above).
- "Next resistance level": no definition.
- The automation (webhook) steps are product instructions, not trading rules.
