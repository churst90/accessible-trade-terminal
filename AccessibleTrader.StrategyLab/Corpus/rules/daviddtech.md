# Trading with DaviddTech: extracted rules

Source slug `daviddtech`, school **market-cipher**. Channel: Trading with DaviddTech (YouTube; the presenter greets "dave traders"). **1 transcript read in full**, dated 2021-09-17 (manual subtitles). Kind of material: a coding video. He turns a strategy he found on YouTube into a TradingView Pine strategy and reads out the strategy-tester results. Public. The indicator is the free VuManChu Cipher B + Divergences. The strategy he codes is Trade Pro's (2021-06-25, see `trade-pro.md`), credited on air ([VuManChu Market Cipher B strategy, 2021-09-17, 01:02](https://youtu.be/OSupc_S0sJY?t=62)). The rules below are DaviddTech's version as he describes and codes it. Where he departs from Trade Pro, this file says so.

| Date | Id | Title | Read |
|---|---|---|---|
| 2021-09-17 | OSupc_S0sJY | Insanely Profitable Proven Trading Strategy VuManChu Market Cipher B | in full |

Timestamps cite the start of the ~30 s transcript chunk that holds the words.

## Method in its own terms

The same four-filter trend pullback as Trade Pro. EMA 200 sets the side. A pullback to EMA 50 is the location: he describes it as a **close below the 50 EMA** for a long (above it for a short). Money flow colour must match the side. The waves must be below zero for longs (above for shorts). The entry is the wave cross dot. DaviddTech replaces the swing stop with an **ATR band** stop ("the second ATR band") and a **1.5 to 2 R** target. On default settings his coded version had a **44% win rate over 144 trades** and was net profitable, before any optimisation.

## Anchor wave, trigger wave, money flow and confirmation (exact)

- **Anchor wave / trigger wave.** Nothing stated.
- **Wave depth.** Only the zero line. For longs, "waves underneath the zero line inside the green money index". For shorts, "the blue wave to be above the zero line" ([VuManChu Market Cipher B strategy, 2021-09-17, 03:43](https://youtu.be/OSupc_S0sJY?t=223)).
- **The dot.** "these green dots actually mean that there is a crossover between this lighter blue wave and this darker blue wave". The entry is taken "on this green dot when all of the other parameters ... match" ([VuManChu Market Cipher B strategy, 2021-09-17, 04:18](https://youtu.be/OSupc_S0sJY?t=258)). In code he uses VuManChu's own wave cross-up/cross-down functions as the buy and sell signals ([VuManChu Market Cipher B strategy, 2021-09-17, 08:16](https://youtu.be/OSupc_S0sJY?t=496); [VuManChu Market Cipher B strategy, 2021-09-17, 08:46](https://youtu.be/OSupc_S0sJY?t=526)).
- **Money flow.** Colour only (green area for longs, red for shorts). He calls it the "money flow index" ([VuManChu Market Cipher B strategy, 2021-09-17, 03:43](https://youtu.be/OSupc_S0sJY?t=223)). No thickness or slope.
- **What he waits for after the dot.** Nothing. The entry is "just about here on this candle", the dot candle ([VuManChu Market Cipher B strategy, 2021-09-17, 04:52](https://youtu.be/OSupc_S0sJY?t=292)).
- **Confirmation / invalidation of a dot.** Nothing stated.
- **Timeframe pairing.** None. The timeframe of the coded test is not stated on audio.

## Context / higher timeframe / regime

- "if we are above the 200 we're looking for long positions and if we're below we are looking for short positions" ([VuManChu Market Cipher B strategy, 2021-09-17, 03:13](https://youtu.be/OSupc_S0sJY?t=193)). In code: "if we're above that [the 200 EMA] we're looking for a buyer signal" ([VuManChu Market Cipher B strategy, 2021-09-17, 07:40](https://youtu.be/OSupc_S0sJY?t=460)).
- On the coded chart he also says "when the 200 ema is above the 50 ema we're looking for a short" ([VuManChu Market Cipher B strategy, 2021-09-17, 07:07](https://youtu.be/OSupc_S0sJY?t=427)). That is EMA order rather than price versus EMA200. **The two statements differ, and the transcript does not resolve which one the code uses.**

## Location (levels, zones, where a setup counts)

- "we are looking for pullbacks into the the 50 ema so if we close below the 50 ema that's where we're actually looking for our position uh and shorts if we close above it" ([VuManChu Market Cipher B strategy, 2021-09-17, 03:13](https://youtu.be/OSupc_S0sJY?t=193)). This is **a close beyond EMA50 against the trend**, which is stricter than Trade Pro's "pulled back into".
- Short example: the 200 EMA above price, and price "between the green and the red", i.e. between EMA50 and EMA200, having closed above EMA50 ([VuManChu Market Cipher B strategy, 2021-09-17, 04:18](https://youtu.be/OSupc_S0sJY?t=258)).
- In code he adds a "bounce" variable. It checks that the high is below the previous high and that the close is beyond the 50 EMA (the audio is garbled) ([VuManChu Market Cipher B strategy, 2021-09-17, 07:40](https://youtu.be/OSupc_S0sJY?t=460); [VuManChu Market Cipher B strategy, 2021-09-17, 08:16](https://youtu.be/OSupc_S0sJY?t=496)).

## Entry triggers

- All filters met (EMA200 side, close beyond EMA50, money flow colour, waves beyond zero) plus the dot in the trade's direction. Enter on that candle ([VuManChu Market Cipher B strategy, 2021-09-17, 04:18](https://youtu.be/OSupc_S0sJY?t=258); [VuManChu Market Cipher B strategy, 2021-09-17, 04:52](https://youtu.be/OSupc_S0sJY?t=292)). Coded example: back in the 50 EMA, waves under zero, green money flow, then "a little green dot" → entry ([VuManChu Market Cipher B strategy, 2021-09-17, 07:07](https://youtu.be/OSupc_S0sJY?t=427)).

## Confirmations (things that raise conviction)

- Nothing stated beyond the binary filters.

## Vetoes (things that cancel a trade whatever else says)

- Any filter false = no trade (same four as Trade Pro).

## Stops and invalidation

- **ATR band stop.** He plots ATR bands. For a short, "we're using the stop loss just on that second atr band there" ([VuManChu Market Cipher B strategy, 2021-09-17, 04:52](https://youtu.be/OSupc_S0sJY?t=292); [VuManChu Market Cipher B strategy, 2021-09-17, 05:28](https://youtu.be/OSupc_S0sJY?t=328)). He does not give the ATR length or the band multiple. Trade Pro's swing stop is replaced.

## Exits and targets

- "normally we'll be doing a one point five to two" risk/reward ([VuManChu Market Cipher B strategy, 2021-09-17, 05:28](https://youtu.be/OSupc_S0sJY?t=328)). In the coded version the entries are "worked out on atr" and "i'm using atr to take profits" ([VuManChu Market Cipher B strategy, 2021-09-17, 07:40](https://youtu.be/OSupc_S0sJY?t=460); [VuManChu Market Cipher B strategy, 2021-09-17, 09:16](https://youtu.be/OSupc_S0sJY?t=556)). The exact multiple used in the test is not stated.

## Position sizing and risk

- Nothing stated.

## Timeframes

- Not stated for the coded test.

## Macro context

- Nothing stated.

## Indicator settings stated (exact numbers: lengths, thresholds, colours meaning)

- EMA 50 (plotted green) and EMA 200. They change colour by side in his script ([VuManChu Market Cipher B strategy, 2021-09-17, 02:39](https://youtu.be/OSupc_S0sJY?t=159); [VuManChu Market Cipher B strategy, 2021-09-17, 07:40](https://youtu.be/OSupc_S0sJY?t=460)).
- ATR bands for the stop and target. Length and multiple not stated ([VuManChu Market Cipher B strategy, 2021-09-17, 02:39](https://youtu.be/OSupc_S0sJY?t=159)).
- VuManChu wave functions copied as they are.

## Calibration numbers and falsifiable side-claims (win rates, base rates, "X happens N% of the time")

- **Coded result, default settings:** "a 44 win rate" ([VuManChu Market Cipher B strategy, 2021-09-17, 08:46](https://youtu.be/OSupc_S0sJY?t=526)–[09:16](https://youtu.be/OSupc_S0sJY?t=556)). **64 wins, 80 losses, net profit 3174** (units not stated), **144 trades (81 long, 63 short)**. Max 6 wins in a row, 8 losses in a row ([VuManChu Market Cipher B strategy, 2021-09-17, 09:16](https://youtu.be/OSupc_S0sJY?t=556); [VuManChu Market Cipher B strategy, 2021-09-17, 09:46](https://youtu.be/OSupc_S0sJY?t=586)). Asset, timeframe, date range and costs: not stated. 64/144 = 44.4%, consistent with the stated 44%.
- He compares this with Trade Pro's hand test (54% at 2R on EUR/USD 30m). The coded version on unknown data won about 10 points less, with ATR exits instead of swing/2R.

## Dated calls (for later scoring)

| Video date | Asset | Direction | Level / target / invalidation | Horizon stated | Citation |
|---|---|---|---|---|---|
| — | — | — | No forward-looking calls. | — | — |

## Candidate rules NOT in the pre-registered setup grader

1. **Pullback defined as a close beyond EMA50 against the trend** (long: close < EMA50 while close > EMA200, within k bars before the dot). This is DaviddTech's definition, a computable alternative to Trade Pro's "into the 50 EMA". Terminal: EMA, Cipher B.
2. **ATR-band stop with a 1.5 to 2R target** on the Trade Pro entry. Length and multiple unstated: needs a pre-registered choice. Fixed-R/ATR exits are listed as already dead in this repo, so test this only as part of reproducing the full system, not as a new exit idea.
3. **Replication check.** Run the same four-filter entry with both exits (swing/2R per Trade Pro, ATR per DaviddTech) on one data set, to see whether the 54% versus 44% gap comes from the exit or the data.

## Not usable / vague

- The "bounce" variable: its logic is garbled in the audio.
- EMA-order versus price-versus-EMA200 regime: stated both ways.
- The test settings (asset, timeframe, costs): not given, so 44% cannot be reproduced exactly.
