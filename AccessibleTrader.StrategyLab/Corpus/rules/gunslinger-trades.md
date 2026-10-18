# Gunslinger Trades: extracted rules

Source slug `gunslinger-trades`, school **market-cipher**. Channel: Gunslinger Trades (YouTube). **5 transcripts read in full**, dated 2022-01-11 to 2022-04-03, all short (4 to 11 minutes). Kind of material: tool tutorials and strategy walkthroughs. Public, with a free Discord. Indicators are paid (MC B, MC DBSI). YouTube ASR: "dpsi/dvsi/dbs size" = DBSI, "microcipher/microserver" = Market Cipher.

**Attribution.** He credits the **4/24** and **3/12** strategies to Jason Casper ("thank you to jason casper for letting me use this strategy", "he's the one who really discovered it") ([4/24 Strategy, 2022-03-29, 00:00](https://youtu.be/BozOB9RMbxs?t=0); [3/12 Strategy, 2022-04-03, 00:00](https://youtu.be/HOAZ6_sploI?t=0)). The rules below are **Gunslinger's own statement** of them, which differs in places from Casper's own videos (see `jason-casper.md`; not merged here). He attributes the DBSI threshold to CryptoFace's recommendation.

| Date | Id | Title | Read |
|---|---|---|---|
| 2022-01-11 | BODk5WthpXI | Simple and Profitable Market Cipher DBSI Strategy - Beginner Friendly! | in full |
| 2022-02-03 | LI6ske5q99M | Market Cipher B Basics: Momentum Waves | in full |
| 2022-03-15 | rSfFx7gG1UA | Market Cipher DBSI - Walk Through | in full |
| 2022-03-29 | BozOB9RMbxs | The 4/24 Market Cipher Strategy \| High Win rates | in full |
| 2022-04-03 | HOAZ6_sploI | The 3/12 Market Cipher Strategy \| High Win rate | in full |

Timestamps cite the start of the ~30 s transcript chunk that holds the words.

## Method in its own terms

Gunslinger trades Market Cipher in **timeframe pairs**, and always only **with a clear trend** read from the daily.
- **4/24.** A 4H green dot on a "lumpy" **trigger wave** (a wave above the earlier big-green-dot anchor, a single island "lump" below zero). Then drop to the 24m and enter on **whichever comes first**: a 24m green dot or a 24m money flow cross into positive.
- **3/12.** The other way round. A **12m money flow zero cross first**, then a 3H dot in the same direction within a few 12m candles.
- **DBSI 6H.** A 6H DBSI score of 14 or more on the trend side, followed by a matching MC B dot.

Stops are "the last wick" or the swing. The DBSI play takes profit at 2 to 3%. He sets TradingView alerts for every condition.

## Anchor wave, trigger wave, money flow and confirmation (exact)

- **Anchor wave.**
  - Identified by the **large green dot**. In the alert setup: "make sure you guys don't hit the green dot because this is showing you only the anchor wave ... this big green dot" ([4/24 Strategy, 2022-03-29, 07:42](https://youtu.be/BozOB9RMbxs?t=462)).
  - **No depth number** (no −60, no dotted line). In MC B the large green dot is itself a depth-gated signal, but he does not say what gates it.
- **Trigger wave (exact).**
  - "a trigger wave is essentially a uh a wave i'm formed on market cipher that is above a large green dot". He calls it "a bullish trigger wave" ([4/24 Strategy, 2022-03-29, 00:30](https://youtu.be/BozOB9RMbxs?t=30)). That is, a later wave whose low sits higher than the big-dot anchor's.
  - Shape: "you want to see a single like island lump below the zero line", "its own bump" ([4/24 Strategy, 2022-03-29, 01:01](https://youtu.be/BozOB9RMbxs?t=61); [4/24 Strategy, 2022-03-29, 01:32](https://youtu.be/BozOB9RMbxs?t=92)). He prefers "lumpy bullish trigger waves" as the safer, higher-win-rate option ([4/24 Strategy, 2022-03-29, 03:34](https://youtu.be/BozOB9RMbxs?t=214); [4/24 Strategy, 2022-03-29, 04:05](https://youtu.be/BozOB9RMbxs?t=245)).
  - The trigger's dot is the **small green dot** (alert "blue wave crossing up small green dot") ([4/24 Strategy, 2022-03-29, 06:39](https://youtu.be/BozOB9RMbxs?t=399); [4/24 Strategy, 2022-03-29, 07:11](https://youtu.be/BozOB9RMbxs?t=431)).
  - No number for "how much smaller" and no bar gap.
- **Depth caps (exact as stated).** "i usually don't like to enter green dots when momentum is above the 30 line". The exception is when money flow is "super thick into the positive" ([4/24 Strategy, 2022-03-29, 05:06](https://youtu.be/BozOB9RMbxs?t=306)). Later: "it's above the value level the 20 line whatever", and "for a safe safer option you want to have a low momentum usually below the 30 or so" ([4/24 Strategy, 2022-03-29, 05:37](https://youtu.be/BozOB9RMbxs?t=337); [4/24 Strategy, 2022-03-29, 06:08](https://youtu.be/BozOB9RMbxs?t=368)). From context the "30 line" is +30 (examples above zero), but **it is not said explicitly**. The "20 line" remark is unclear.
- **Money flow (exact as stated).**
  - *4/24 safe option:* nothing required of 4H money flow beyond the trigger wave.
  - *4/24 riskier option* (more trades): enter 4H green dots when "the money flow is flipped to the positive and it's kind of increasing over time" ([4/24 Strategy, 2022-03-29, 04:05](https://youtu.be/BozOB9RMbxs?t=245)). A bad example is money flow "thick in the red and it's coming down" ([4/24 Strategy, 2022-03-29, 04:36](https://youtu.be/BozOB9RMbxs?t=276)).
  - *"Thick"* is never quantified ("pretty thick", "super thick", "very thick").
  - *3/12:* the 12m money flow zero cross *is* the first signal ("the 12 minute money flow is very strong") ([3/12 Strategy, 2022-04-03, 00:30](https://youtu.be/HOAZ6_sploI?t=30); [3/12 Strategy, 2022-04-03, 01:01](https://youtu.be/HOAZ6_sploI?t=61)).
- **What he WAITS FOR after a green dot prints (exact).**
  - *4/24:* after the 4H green dot ("once a green dot on the 4 hour is printing", so intrabar is allowed), mark a vertical line and go to the 24m. Wait for "either a money flow crossover to the positive ... or a green dot", whichever "comes first" ([4/24 Strategy, 2022-03-29, 02:04](https://youtu.be/BozOB9RMbxs?t=124); [4/24 Strategy, 2022-03-29, 02:34](https://youtu.be/BozOB9RMbxs?t=154); [4/24 Strategy, 2022-03-29, 10:14](https://youtu.be/BozOB9RMbxs?t=614)). On the 24m "you can either wait until it's printing or it has printed". In his example the 24m green dot came first, so "we wouldn't have entered until the next candle" ([4/24 Strategy, 2022-03-29, 03:04](https://youtu.be/BozOB9RMbxs?t=184); [4/24 Strategy, 2022-03-29, 03:34](https://youtu.be/BozOB9RMbxs?t=214)). **So: a 4H dot (intrabar OK), then the first 24m dot or MF up-cross, with entry on the next 24m candle.** The 4H alert is set "once per bar", which fires as soon as the dot starts printing ("if a green dot shows up and it starts printing i will be notified") ([4/24 Strategy, 2022-03-29, 06:39](https://youtu.be/BozOB9RMbxs?t=399); [4/24 Strategy, 2022-03-29, 07:11](https://youtu.be/BozOB9RMbxs?t=431)).
  - *3/12:* the order is reversed. The 12m money flow cross must come **first**, then the 3H dot. "you don't want a dot printing already before that 12 minute money flow crossover" ([3/12 Strategy, 2022-04-03, 04:37](https://youtu.be/HOAZ6_sploI?t=277)).
  - **Window:** "i only give myself three to five candles max of the 12 minute candle when it crossed in the money flow before waiting for that green dot". If it shows up "after like say like seven candles or so i will not enter" ([3/12 Strategy, 2022-04-03, 04:06](https://youtu.be/HOAZ6_sploI?t=246)). His summary says "two to five candles max" ([3/12 Strategy, 2022-04-03, 07:14](https://youtu.be/HOAZ6_sploI?t=434)), and earlier he said "three hours three to five candles" ([3/12 Strategy, 2022-04-03, 02:33](https://youtu.be/HOAZ6_sploI?t=153)). **Best reading: the 3H dot must start printing within 2 to 5 twelve-minute candles (24 to 60 minutes) of the 12m money flow cross.** That is less than one 3H bar, so the 3H dot is taken intrabar.
  - *DBSI 6H:* after the DBSI 14 print, wait for the MC B dot in the same direction ([DBSI Strategy, 2022-01-11, 02:06](https://youtu.be/BODk5WthpXI?t=126); [DBSI Strategy, 2022-01-11, 02:36](https://youtu.be/BODk5WthpXI?t=156)). The dot's timeframe is not stated (6H implied).
- **How a dot is confirmed or invalidated.** 3/12: invalid if the 3H dot comes too late (more than 5 to 7 twelve-minute candles) or before the money flow cross ([3/12 Strategy, 2022-04-03, 04:06](https://youtu.be/HOAZ6_sploI?t=246); [3/12 Strategy, 2022-04-03, 04:37](https://youtu.be/HOAZ6_sploI?t=277)). 4/24: an anchor's big dot is not a trigger (see above). Otherwise nothing stated.
- **Timeframe pairing.** 4H/24m (ratio 10), 3H/12m (ratio 15), and the DBSI on 6H. In the 3/12 he puts the two charts side by side ([3/12 Strategy, 2022-04-03, 02:02](https://youtu.be/HOAZ6_sploI?t=122)).

## DBSI rules (exact)

- **What the numbers are.** "on the bottom of the candle is the bullish power and then on top of the candle is the bearish power ... the higher the number the stronger the trend" ([DBSI Walk Through, 2022-03-15, 00:33](https://youtu.be/rSfFx7gG1UA?t=33)). He calls it "dual band support indicator" ([DBSI Walk Through, 2022-03-15, 00:00](https://youtu.be/rSfFx7gG1UA?t=0)) [sic: other sources say "dual band strength index"].
- **Scale.** "it goes from a negative 10 value to a anywhere to a 30 value but i could be wrong" ([DBSI Walk Through, 2022-03-15, 01:03](https://youtu.be/rSfFx7gG1UA?t=63)). His readings range from −3 to 14.
- **Signal threshold.** Long on the 6H when bullish power hits **14**. Short at bearish power **14** ([DBSI Strategy, 2022-01-11, 00:01](https://youtu.be/BODk5WthpXI?t=1)). "what cryptoface recommends is using uh 13 or above but i tend to use 14". Set this in the DBSI inputs and alert on "long entry" / "short entry", once per bar ([DBSI Strategy, 2022-01-11, 01:03](https://youtu.be/BODk5WthpXI?t=63); [DBSI Strategy, 2022-01-11, 01:34](https://youtu.be/BODk5WthpXI?t=94)).
- **What he waits for after the DBSI signal.** A red dot (shorts) or green dot (longs), and only in a trending market. Sequence: trend → DBSI ≥ 14 on the trend side → dot ([DBSI Strategy, 2022-01-11, 02:06](https://youtu.be/BODk5WthpXI?t=126); [DBSI Strategy, 2022-01-11, 02:36](https://youtu.be/BODk5WthpXI?t=156)).
- **Reading across timeframes (walk-through).**
  - Go top-down: monthly → 2W → weekly → 3D → 2D → daily. On each, check "at least the four previous candles" and whether bull power is rising and bear power falling, or the reverse ([DBSI Walk Through, 2022-03-15, 02:36](https://youtu.be/rSfFx7gG1UA?t=156); [DBSI Walk Through, 2022-03-15, 03:07](https://youtu.be/rSfFx7gG1UA?t=187)).
  - Both numbers small and not moving (weekly 5 bull / 2 bear) = consolidation ([DBSI Walk Through, 2022-03-15, 04:07](https://youtu.be/rSfFx7gG1UA?t=247)).
  - 3D bull −3 / bear 14 = bears in control ([DBSI Walk Through, 2022-03-15, 04:39](https://youtu.be/rSfFx7gG1UA?t=279)).
  - Daily bull 13 / bear −3 = upward momentum, "however ... we are still below this 200 moving average", so "there might not be that much power" ([DBSI Walk Through, 2022-03-15, 05:40](https://youtu.be/rSfFx7gG1UA?t=340)).
- **The 200 MA overlay.** DBSI's 200-candle moving average is red when price is below it and green above. Below it, "bears do have control" ([DBSI Walk Through, 2022-03-15, 01:33](https://youtu.be/rSfFx7gG1UA?t=93); [DBSI Walk Through, 2022-03-15, 06:11](https://youtu.be/rSfFx7gG1UA?t=371)).
- **Timeframes.** "mainly larger time frames and so the daily time frame and up". The 6H also "works pretty well" ([DBSI Walk Through, 2022-03-15, 02:05](https://youtu.be/rSfFx7gG1UA?t=125)).
- **Data.** Use the Coinbase BTC chart because "there's more data to read and it gets more accurate" ([DBSI Walk Through, 2022-03-15, 02:36](https://youtu.be/rSfFx7gG1UA?t=156)).
- **Backtestability.** "you can't really back test it". It "only goes back 24 bars" ([DBSI Walk Through, 2022-03-15, 06:11](https://youtu.be/rSfFx7gG1UA?t=371); [DBSI Walk Through, 2022-03-15, 06:43](https://youtu.be/rSfFx7gG1UA?t=403)).

## Context / higher timeframe / regime

- **Trend only.** 4/24: "this strategy overall only works in trending markets so a clear uptrend and a clear downtrend". Check it on the daily ("where price is heading in a macro perspective") ([4/24 Strategy, 2022-03-29, 01:32](https://youtu.be/BozOB9RMbxs?t=92)). DBSI 6H: "the strategy comes into play when it works on trending markets". In a downtrend he looks only for shorts ([DBSI Strategy, 2022-01-11, 00:32](https://youtu.be/BODk5WthpXI?t=32)). How a "clear trend" is judged is not stated.
- **Top-down momentum read** (momentum-waves video). Monthly → weekly → 2D → daily → 12H → 4H → 1H, noting on each whether the waves are overbought or oversold ([Momentum Waves, 2022-02-03, 02:01](https://youtu.be/LI6ske5q99M?t=121); [Momentum Waves, 2022-02-03, 05:34](https://youtu.be/LI6ske5q99M?t=334)).
- **DBSI 200 MA.** Price below it means bears control, and it caps how much a bullish DBSI score can deliver ([DBSI Walk Through, 2022-03-15, 05:40](https://youtu.be/rSfFx7gG1UA?t=340)).

## Location (levels, zones, where a setup counts)

- Only "do TA" so you are "not longing into resistance" ([4/24 Strategy, 2022-03-29, 03:34](https://youtu.be/BozOB9RMbxs?t=214)). The DBSI 200 MA "can be useful for major support and resistance" ([DBSI Walk Through, 2022-03-15, 01:33](https://youtu.be/rSfFx7gG1UA?t=93)). No exact rule.

## Entry triggers

- **4/24 long:** (1) a clear daily uptrend; (2) a 4H small green dot on a lumpy trigger wave above the big-dot anchor; (3) the first 24m green dot or 24m MF cross > 0; (4) enter on the next 24m candle ([4/24 Strategy, 2022-03-29, 09:14](https://youtu.be/BozOB9RMbxs?t=554); [4/24 Strategy, 2022-03-29, 09:44](https://youtu.be/BozOB9RMbxs?t=584); [4/24 Strategy, 2022-03-29, 10:14](https://youtu.be/BozOB9RMbxs?t=614)). Shorts mirror it.
- **3/12:** (1) a 12m money flow zero cross; (2) a 3H dot in the same direction within 2 to 5 twelve-minute candles. Enter then ([3/12 Strategy, 2022-04-03, 06:42](https://youtu.be/HOAZ6_sploI?t=402); [3/12 Strategy, 2022-04-03, 07:14](https://youtu.be/HOAZ6_sploI?t=434)). His worked example is a short after a 12m cross down, though the ASR says "we did get a green dot" ([3/12 Strategy, 2022-04-03, 03:03](https://youtu.be/HOAZ6_sploI?t=183)). Earlier he self-corrects that a negative cross needs "a red dot" ([3/12 Strategy, 2022-04-03, 01:32](https://youtu.be/HOAZ6_sploI?t=92)). Read the example as a red dot.
- **DBSI 6H:** trend → DBSI ≥ 14 on the trend side → MC B dot in the same direction ([DBSI Strategy, 2022-01-11, 02:06](https://youtu.be/BODk5WthpXI?t=126)).

## Confirmations (things that raise conviction)

- A lumpy, isolated 4H trigger wave makes "a more of a safe bet option and that is has a pretty high win rate" ([4/24 Strategy, 2022-03-29, 04:05](https://youtu.be/BozOB9RMbxs?t=245)).
- Low momentum at the dot ("below the 30 or so") ([4/24 Strategy, 2022-03-29, 06:08](https://youtu.be/BozOB9RMbxs?t=368)).
- Thick money flow in the trade's direction permits otherwise-high dots ([4/24 Strategy, 2022-03-29, 05:06](https://youtu.be/BozOB9RMbxs?t=306)).
- Divergences on the momentum waves, mainly on 12m to 4H ([Momentum Waves, 2022-02-03, 04:32](https://youtu.be/LI6ske5q99M?t=272); [Momentum Waves, 2022-02-03, 05:03](https://youtu.be/LI6ske5q99M?t=303)).

## Vetoes (things that cancel a trade whatever else says)

- **No clear trend → no 4/24 or DBSI trade** ([4/24 Strategy, 2022-03-29, 01:32](https://youtu.be/BozOB9RMbxs?t=92); [DBSI Strategy, 2022-01-11, 00:32](https://youtu.be/BODk5WthpXI?t=32)).
- **3/12: the dot printed before the 12m money flow cross, or more than 5 (at most 7) twelve-minute candles after it** → no trade ([3/12 Strategy, 2022-04-03, 04:06](https://youtu.be/HOAZ6_sploI?t=246); [3/12 Strategy, 2022-04-03, 04:37](https://youtu.be/HOAZ6_sploI?t=277)).
- **Riskier 4/24 option:** 4H money flow "thick in the red and it's coming down" → a bad time to long ([4/24 Strategy, 2022-03-29, 04:36](https://youtu.be/BozOB9RMbxs?t=276)).
- **Green dot with momentum above +30** → skip, unless money flow is super thick ([4/24 Strategy, 2022-03-29, 05:06](https://youtu.be/BozOB9RMbxs?t=306)).

## Stops and invalidation

- 4/24: "put our stop loss below the last wick or whatever" ([4/24 Strategy, 2022-03-29, 03:34](https://youtu.be/BozOB9RMbxs?t=214)).
- 3/12 short: "put our stop loss above the swing high" ([3/12 Strategy, 2022-04-03, 03:34](https://youtu.be/HOAZ6_sploI?t=214)).
- DBSI 6H: "aim for the last wick on the previous six hour candle", "about three percent" ([DBSI Strategy, 2022-01-11, 03:07](https://youtu.be/BODk5WthpXI?t=187)).

## Exits and targets

- DBSI 6H: "i usually aim for two to three percent uh three percent works pretty well for me" ([DBSI Strategy, 2022-01-11, 02:36](https://youtu.be/BODk5WthpXI?t=156)). He sets take-profit orders in advance on shorts because "price tends to whip down really fast" ([DBSI Strategy, 2022-01-11, 03:07](https://youtu.be/BODk5WthpXI?t=187)).
- 3/12: "aim down low whatever", with TA for targets ([3/12 Strategy, 2022-04-03, 03:34](https://youtu.be/HOAZ6_sploI?t=214)).
- 4/24: no target rule. The example made "a nice 5.5 percent move" ([4/24 Strategy, 2022-03-29, 03:34](https://youtu.be/BozOB9RMbxs?t=214)).

## Position sizing and risk

- Nothing stated.

## Timeframes

- 4H/24m, 3H/12m and 6H DBSI. DBSI also on daily and higher ([DBSI Walk Through, 2022-03-15, 02:05](https://youtu.be/rSfFx7gG1UA?t=125)). Divergences 12m to 4H mainly, occasionally daily ([Momentum Waves, 2022-02-03, 05:03](https://youtu.be/LI6ske5q99M?t=303)).

## Macro context

- Nothing beyond the trend reads above.

## Indicator settings stated (exact numbers: lengths, thresholds, colours meaning)

- DBSI long/short entry threshold 14 (CryptoFace "13 or above") ([DBSI Strategy, 2022-01-11, 01:34](https://youtu.be/BODk5WthpXI?t=94)).
- DBSI moving average: 200 candles, coloured red when price is below and green above ([DBSI Walk Through, 2022-03-15, 01:33](https://youtu.be/rSfFx7gG1UA?t=93); [DBSI Walk Through, 2022-03-15, 06:11](https://youtu.be/rSfFx7gG1UA?t=371)).
- MC B alert names: "blue wave crossing up small green dot", "blue wave crossing down small red dot", "money flow crossing value of zero" ([4/24 Strategy, 2022-03-29, 06:39](https://youtu.be/BozOB9RMbxs?t=399); [3/12 Strategy, 2022-04-03, 05:41](https://youtu.be/HOAZ6_sploI?t=341); [3/12 Strategy, 2022-04-03, 06:11](https://youtu.be/HOAZ6_sploI?t=371)).
- MC B waves: he calls them "blue and white waves" ([Momentum Waves, 2022-02-03, 00:31](https://youtu.be/LI6ske5q99M?t=31)).

## Calibration numbers and falsifiable side-claims (win rates, base rates, "X happens N% of the time")

- 4/24 and 3/12 are each called "high win rate" in the titles and on air ([4/24 Strategy, 2022-03-29, 04:05](https://youtu.be/BozOB9RMbxs?t=245); [3/12 Strategy, 2022-04-03, 04:06](https://youtu.be/HOAZ6_sploI?t=246)). No numbers.
- 3/12 "doesn't occur too often" ([3/12 Strategy, 2022-04-03, 04:06](https://youtu.be/HOAZ6_sploI?t=246)).
- 12m money flow flips gave "a nice six percent move", "four percent", "five percent" in his examples ([3/12 Strategy, 2022-04-03, 00:30](https://youtu.be/HOAZ6_sploI?t=30); [3/12 Strategy, 2022-04-03, 01:01](https://youtu.be/HOAZ6_sploI?t=61)).
- DBSI is "pretty accurate on higher time frames". It can't be backtested ([DBSI Walk Through, 2022-03-15, 06:43](https://youtu.be/rSfFx7gG1UA?t=403)).

## Dated calls (for later scoring)

| Video date | Asset | Direction | Level / target / invalidation | Horizon stated | Citation |
|---|---|---|---|---|---|
| 2022-01-11 | chart asset, not named (likely BTC) | Bias short ("right now we are in a big downtrend ... i tend to look for shorts") | 6H DBSI ≥ 14 bear + red dot; TP 2–3%, stop last 6H wick ~3% | not stated | [DBSI Strategy, 2022-01-11, 00:32](https://youtu.be/BODk5WthpXI?t=32) |
| 2022-03-15 | BTC (Coinbase) | Mixed: daily bull 13 / bear −3 "upward momentum", but below the 200 MA, so limited power; 3D bear 14 | none | not stated | [DBSI Walk Through, 2022-03-15, 05:40](https://youtu.be/rSfFx7gG1UA?t=340) |
| 2022-03-29 | chart asset, not named (likely BTC) | Bias long ("pretty safe to say we're in a uptrend", daily) | none | not stated | [4/24 Strategy, 2022-03-29, 01:32](https://youtu.be/BozOB9RMbxs?t=92) |

## Candidate rules NOT in the pre-registered setup grader

1. **3/12 sequence (computable with intraday data).** At a 12m bar where MF crosses 0 upward, open a window of 5 twelve-minute bars. If, inside that window, the 3H WT1 crosses above WT2 *intrabar* and no 3H green dot had printed before the MF cross, go long. Stop = swing low. Mirror for shorts. **Needs 12m and intrabar 3H data** (probably data we do not have in daily snapshots). A daily analogue would be a daily MF cross followed by a weekly cross within 2 to 5 daily bars. That analogue is ours, not his.
2. **4/24 "whichever comes first" entry.** After a 4H trigger dot (a wave with a higher low than the big-dot anchor), enter long at the next 24m bar after the first 24m green dot or 24m MF up-cross. A daily/weekly analogue (weekly trigger, daily entry) is computable. Terminal: Cipher B on two timeframes.
3. **Anchor = the big-dot wave; trigger = the next wave with a higher low and a single "island" below zero.** Computable if "island" = one contiguous run of WT2 < 0 whose min > the anchor's min.
4. **Momentum cap.** Skip buy dots with WT > +30 unless MF is "super thick". Mostly irrelevant to the grader's WT2 ≤ −60 dots, but relevant to the 24m entry dot.
5. **DBSI ≥ 14 on the trend side, then a dot (6H).** Needs the vendor's DBSI (closed-source, 24-bar history). The terminal's DBSI-style rebuild is not the vendor formula, so treat it as a proxy at best.
6. **Fixed 3% take-profit with a stop at the last 6H wick** for the DBSI play. Fixed targets are on the repo's dead list, so only use it to reproduce his full system.

## Not usable / vague

- "Clear trend" on the daily: never defined.
- "Lumpy", "island", "super thick": shapes, not thresholds.
- "Above the 30 line" sign and "the 20 line": ambiguous.
- DBSI scale "−10 to 30 ... could be wrong": unreliable. Other sources give −5 to 28 or −5 to 31 (see `dbsi-reviewers.md`, `chart-wis.md`).
