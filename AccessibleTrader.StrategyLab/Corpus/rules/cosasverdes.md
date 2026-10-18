# Cosasverdes (Tim): extracted rules

Source slug `cosasverdes`. Channel: cosasverdes ("Tim"), tutorial library. This file currently covers Part 1 only: 8 local tutorial transcripts (Whisper large-v3-turbo) of tutorial-library videos that have no public URL. Access: community or paid. Citations are `(tutorial N, mm:ss)`.

## Part 1: tutorial library (8 local videos)

Transcripts read (all read in full): `2021-12-18_tutorial-1` (How To Set Up A Chart), `0000-00-00_tutorial-2` (Horizontals: How to Set them + How to SELL), `2021-12-18_tutorial-3` (Let the trend be your friend), `0000-00-00_tutorial-4` (Identifying where you are in the crypto cycle), `0000-00-00_tutorial-5` (Portfolio Management), `0000-00-00_tutorial-6` (Origin lines and object tree), `0000-00-00_tutorial-7` (Channels), `0000-00-00_tutorial-8` (Micro Candle Ricochet).

Dates. Tutorials 1 and 3 are dated 2021-12-18 (tutorial 3 states "december 18th 2021", tutorial 3, 04:00). The rest are undated (`0000-00-00`). Clues in the audio, which are inference and were not stated as dates: tutorial 4 talks to viewers "beyond february 2022" (tutorial 4, 09:00), so it is about early 2022. Tutorial 5 says the market is "past the warning signs... full bear market alert" (tutorial 5, 31:06), so it is about early 2022. Tutorial 6 says Bitcoin is close to the master origin line "when i'm making it" and mentions February/March 2022 candles (tutorial 6, 04:12; 04:47), so it is about March 2022 or later. Tutorial 8 says Bitcoin bounced "very hard off of its lows from 15 500 a couple months earlier" (tutorial 8, 05:21). The 15.5K low was in November 2022, so tutorial 8 is about early 2023.

Kind of material: education and tool tutorials. It covers TradingView setup, how to draw horizontals and sell them in tranches, trend definitions from candle bodies, cycle phase from weekly moving averages, phase-driven portfolio allocation, hand-drawn origin lines and channels, and a sub-minute "point system" for reading how price reacts at a level.

**The method in plain words.** Tim's framework for investors is mechanical. On the weekly chart, red EMA10 above yellow EMA21 means "bull". The SMA10 rising above the EMA10 is a "warning": sell half of every coin. EMA10 dropping below EMA21 is "bear": sell the rest and hold stablecoins, often for a year or more. Buy back only when EMA10 is above EMA21 again and the SMA10 is back under the EMA10. The SMA20/21 rising above the EMA21 marks the deep, year-plus "big nasty box" bear. Inside a bull phase he trades for more coins. He draws weekly horizontals at candle-body and wick clusters, sells 20% of a coin in four to five straddled limit orders at a line, and buys back about 20% lower. The buyback depth scales with how big the preceding run was. He sells a line at most twice, the second time with only 10%, and only while the daily trend is up. Trend is read from candle bodies: higher local high plus higher local low means uptrend. A trend changes only on a candle close, and Bitcoin's daily trend governs when to buy altcoins. For timing entries and exits at a level he uses a "micro candle ricochet" point score on 15-second or 1-minute bars plus a "Batman sign" trend break. The origin lines and channels are hand-drawn parallel-line geometry.

### Context / higher timeframe / regime
- **Bull / warning / bear phase on the weekly** (this is the source of the grader's phase layer, so it is not new). Bull: weekly EMA10 (red) above EMA21 (yellow). If it is below, "don't think about being an investor and buying stuff yet" (tutorial 4, 12:16–12:48). Warning: weekly MA10 (simple, drawn white) above the EMA10 even while EMA10 > EMA21. "you want red on top" (tutorial 4, 14:23–15:24). Fakeouts of the MA10-over-EMA10 signal usually resolve within "seven to 14 days" (tutorial 4, 15:24).
- **"Big nasty box" (deep bear) = weekly 20/21 MA above the weekly 21 EMA.** When it happens, expect down and/or sideways "for over a year", or "at least ... sideways for a year bare minimum" (tutorial 4, 16:29–17:33). The audio says "21 ma" at 16:29 and "20 ma on top of the 21" at 17:33. His chart setup uses MA20 next to EMA21 (tutorial 1, 08:00), so the MA is most likely 20.
- **Monthly accumulation/distribution slope.** On the monthly chart, use "accumulation/distribution with markers" by "dov caspy" [sic; author name as heard]. If its slope is negative while the weekly red is above yellow, "then it's probably one of those times" (a bad time to buy). Colour does not matter, only slope (tutorial 4, 13:21–14:23).
- **Same-length MA crossing above its EMA is bearish on larger timeframes.** MA10 over EMA10, or MA20 over EMA21, on the daily and weekly. "ma's crossing above ema's is a bearish sign particular to larger time frames". Do not use it on the 1-hour (tutorial 1, 10:13). In his example the cross "didn't get the top perfectly it was about a week off" (tutorial 1, 10:45).
- **Trend states from candle bodies.** Uptrend means a higher local high AND a higher local low. Downtrend means a lower local high AND a lower local low. "No trend" means one without the other (tutorial 3, 05:11–06:00; 11:00). Use bodies (opens/closes), not wicks: "this wick ... does not count" (tutorial 3, 06:30–07:02). Consolidation does not count as a local high or low (tutorial 3, 09:12–10:10).
- **A trend changes only at a candle close.** The breaking candle is known only when it closes (tutorial 3, 06:30–07:35). Price can go straight from uptrend to downtrend in one candle if that close both confirms a lower high and breaks the last low (tutorial 3, 17:59–19:03).
- **Bitcoin's trend governs altcoins.** If Bitcoin's daily trend is broken, buying an altcoin "you're making potentially a mistake", whatever the alt is doing (tutorial 3, 27:00–27:33). Bitcoin recovers before altcoins. Alts keep making new lows while BTC goes sideways, so for alts "increase the down numbers" (tutorial 4, 07:27–08:01).
- **Timeframe hierarchy of trend breaks.** A 4H trend break means be "a little bit more cautious" (it happens more often). A daily break into no-trend means be "very on guard" (tutorial 3, 34:18). A 4H full downtrend inside a daily uptrend can flip the daily (tutorial 3, 40:17).
- **Horizontal selling depends on the daily trend.** "If it's in an uptrend, it'll likely stay in an uptrend ... If the daily trend is down, you can't quite do the same" (tutorial 2, 14:46).
- **Cycle base rate.** Most of Bitcoin's history is sideways and/or down, and most new money enters near tops (tutorial 4, 10:51; 18:35–19:06; 21:16). See the calibration section for his per-run day counts.

### Location (levels, zones, where a setup counts)
- **Draw horizontals on the weekly first** (tutorial 2, 00:00). Daily horizontals are secondary. On 1H–4H, "the same method" works for day trading (tutorial 2, 05:42; 23:51).
- **Strength rank 1: price "turns on a dime".** This is a sharp V reversal where price "doesn't go back to that point for a while". If a body line and a wick line both exist at such a turn, the wick line is the stronger (tutorial 2, 00:30–01:33; 02:34).
- **Strength rank 2: most connections.** The line touching the most candle bodies and wicks. Lines through many bodies are stronger than lines through many wicks (tutorial 2, 02:34–03:07). Touches "doesn't have to be exact, but it's got to be kind of close" (tutorial 2, 04:08).
- **Weekly lines are thick (4 px), daily lines thinner.** A daily line between two weekly lines is weaker, because "price might be more attracted to the weekly horizontals". He sells daily horizontals only "if there's a lot of space between the weekly horizontals" (tutorial 2, 25:24–27:03).
- **No horizontals above price (USD all-time high):** draw horizontals on the coin/BTC pair instead and sell into BTC (tutorial 2, 17:28–19:05). Early in a bull run, coins often make USD all-time highs well before BTC-pair highs (tutorial 2, 24:54). If price is at all-time highs on both charts, use sloped trend lines (tutorial 2, 27:03).
- **Levels the micro-candle system accepts:** a "rather powerful and common" EMA (the daily 10 EMA), flat horizontals, Fibonacci lines, ascending or descending trend lines, round psychological numbers such as $10,000. Example: 2-day candles bouncing off the monthly 10 EMA (tutorial 8, 01:37–02:09; 03:37). The level must be "truly in play" (tutorial 8, 04:09).
- **Two nearby levels ("yellow triangle").** Between two close objects is "no edge", with thin liquidity that bots "are not seeing". Price "will often bounce to the farther object" (tutorial 8, 14:48; 15:49; 26:54).
- **45° higher low / "look left" slope.** The "most common type of higher lows" sits at roughly a 45-degree angle. Recoveries tend to repeat the slope of the prior leg (tutorial 3, 23:47; tutorial 8, 17:25; 22:42–23:12).
- **Origin lines** (hand-drawn; already tested dead in this repo). They are drawn on the 2-day BTCUSD chart on Bitstamp. The master line runs from the August 2015 lows, through the lows after the 20K top, scraping the June 2020 highs. It is extended left to touch about the August 2012 wick high and checked against the September 2015 and March 2019 lows (tutorial 6, 00:00–04:12). Parallel copies sit above it, through the 2015–16 range tops, the 2019 capitulation and the November 2021 69K top, and below it through the March 2020 capitulation. Dashed midlines (width 1) go in the exact middle (tutorial 6, 07:30–10:08).
- **Channels** (hand-drawn; already tested dead). Start from a line through two wick lows. Make parallel copies. The "point of control" is the exact middle, and all lines are equidistant. Validate with a perpendicular measurement. A macro channel is drawn on 4H/8H (about a year of history), a micro one on 1H. He keeps three dashed lines in his Ethereum macro channel (tutorial 7, 01:03–05:45; 12:02–16:55).

### Entry triggers
- **Investor buy:** weekly EMA10 > EMA21 AND weekly SMA10 below EMA10. Then go about 50% large caps and 50% mid/small caps (tutorial 5, 09:17; 17:20; 19:23; 26:24). He says not to try to buy the wick low before this (tutorial 5, 25:50–26:24).
- **Altcoin hunting trigger:** the daily candle close that confirms Bitcoin's daily uptrend (a higher low already in, then a close above the last local high). "this candle close should be the time to go hunting for altcoins" (tutorial 3, 22:11). If BTC breaks the high before making a higher low, wait for the pullback and higher low (tutorial 3, 16:20–16:52).
- **Short in a downtrend at a trend line ("two assumptions").** In a confirmed downtrend, assume the next pump makes a lower high. If a trend line through prior lower highs exists, short at that line (tutorial 3, 19:35–20:38). The same applies on the 4H when the trend is broken with lower highs (tutorial 3, 36:27).
- **Micro-candle entries.** A team "in control" means it has scored points at the level (see Confirmations). A long after a confirmed micro higher low gave windows of 12 and 18 minutes to enter (tutorial 8, 22:42; 24:14). Before real points are scored he takes only "a tiny tiny tiny bit" and waits for points for a full position (tutorial 8, 12:44).
- **Breakdown entry:** do not count bear points or get bearish "until price goes both below here and hits its head", meaning it breaks below and then fails a retest from below (tutorial 8, 08:28–08:58).

### Confirmations (things that raise conviction)
- **Micro candle ricochet point system** (on 15-second, 30-second or 1-minute bars at the level) (tutorial 8, 03:04; 12:24–13:44; 32:55):
  - "Samurai sword": price passes straight through the level. **0 points** to anyone. "it was targeting price" further away (tutorial 8, 12:24–13:13).
  - "Big man on a trampoline": price goes through the level, then comes back and jumps away (failure / fakeout). **1 point** to the side it bounces toward (tutorial 8, 13:44–14:17).
  - "Ricochet": a pure rejection off the level. **2 points** (tutorial 8, 03:04; 13:44).
  - Weak ricochet (did not quite touch, small reaction): judged as **1 point** (tutorial 8, 21:06–21:42).
  - Typical full-control sequences: rejection (2) + failure (1) = 3 points, then the Batman sign, then the move. Or trampoline (1) + perfect higher low (2) = 3 on the bull side (tutorial 8, 15:25–16:28).
  - One weak point right after a heavy loss does not count as control (tutorial 8, 21:06–21:42). Bulls "in control" over only 0.7% of territory is not enough to long (tutorial 8, 18:29–19:00).
- **"Batman sign" (layer 3, a trend break).** Box the most recent swing low from its candle body down to its wick low. One candle close below it signals the bearish break, and the mirror rule applies for bulls (tutorial 8, 11:42–12:12). It matters most right after a rejection from resistance. In a trend leg he swaps it for leaving a "super micro channel" to cut noise (tutorial 8, 13:47–14:48). Once a micro uptrend is established near the scoring zone, the Batman sign fires on a break whatever the points tally (tutorial 8, 16:19–16:51).
- **Close above two local highs.** "something has absolutely changed" (tutorial 8, 15:19).
- **Body-cluster level with many touches**, bodies more than wicks (tutorial 2, 03:07).
- **Three lower highs in a row while in no-trend** points to an expected lower low (trend turning down) (tutorial 3, 37:00–37:30).

### Vetoes (things that cancel a trade whatever else says)
- **New investors buy nothing during a warning sign** (weekly SMA10 > EMA10). "I strongly suggest just don't buy anything yet" (tutorial 5, 11:27). Also nothing while EMA10 < EMA21 (bear), which may last a year or more (tutorial 5, 12:26; 21:31).
- **Big nasty box** (weekly MA20/21 > EMA21): expect more than a year down or sideways (tutorial 4, 16:29–17:33).
- **Negative monthly accumulation/distribution slope** cancels a weekly-bullish read (tutorial 4, 13:52–14:23).
- **No altcoin buys while Bitcoin's daily trend is broken** (no trend or down) (tutorial 3, 27:00–27:33; 16:20).
- **No longs in a confirmed downtrend.** In a downtrend, any pump is a short candidate (tutorial 3, 19:35; 22:42 "you're not looking for any short positions" in an uptrend).
- **Do not sell a horizontal a third time.** "typically I only sell a line twice" (tutorial 2, 16:57–17:28).
- **Do not re-sell a line price is consolidating just under.** Cancel the unfilled orders and move them to the next line up. If price tagged the line and dropped, leave the orders (tutorial 2, 07:49; 08:53).
- **Micro-candle:** no long while the 1-minute structure is in a little downtrend near the level. It must break out, then make a higher low (tutorial 8, 17:25). Inside a "yellow triangle", no edge, so no trade (tutorial 8, 14:48; 26:54). A 4H close above a line does not count as bullish if the micro points say otherwise: "that closure doesn't mean anything in this world" (tutorial 8, 19:30).
- **Wealthy accounts:** with several hundred thousand dollars or more, skip small caps. You would move the market (tutorial 5, 05:09–06:41).

### Stops and invalidation
- **Exit a long at the Batman-sign close** (market out on that candle close). "don't wait don't guess" (tutorial 8, 13:14–13:47).
- **Daily uptrend invalidated at the first daily close below the last local (higher) low.** "you did not have to wait for a turnaround" (tutorial 3, 15:13–15:47). Draw a horizontal at that low and watch the close (tutorial 3, 25:56–26:29).
- **Moving stops:** after a favourable shift (a close above two local highs against a short), take some profit and move the stop into profit (tutorial 8, 15:49). In a long, trail the stop under horizontals and get stopped out in profit (tutorial 8, 24:47).
- No fixed percentage or ATR stop is stated anywhere.

### Exits and targets
- **Investor exits by phase:** sell **half of every coin** when weekly SMA10 crosses above EMA10 (warning). Sell **the second half** when EMA10 crosses below EMA21 (bear) and hold all stablecoins (tutorial 5, 11:53–12:56; 24:13–24:44). Why only half at the warning: "sometimes when that white goes above, it can just go straight bullish again" (tutorial 5, 24:13).
- **Horizontal sell ("20 bag"):** sell 20% of the coin at a strong line, straddled as 4 limit orders of 5% each around the line, some slightly above (tutorial 2, 06:43; 08:53–09:24; 02:03). On a second sale of the same line, or a weaker or daily line, use a "10 bag": 5 orders of 2% each (tutorial 2, 12:05; 20:10; 26:31). Re-place an order that filled while the others hang (tutorial 2, 14:16). With straddled orders, partial fills are normal: 3 of 5 filled was a "15 bag" (tutorial 2, 15:20).
- **Buyback depth rule:**
  - Base: buy back at "10% of that run". "if I had a 200% run, I will buy back from this line, 10% of that run, which is 20%" (tutorial 2, 07:17).
  - An 800% run means try to buy back about 50% lower (tutorial 2, 11:00).
  - If price raced up from two or more lines below without consolidating, expect a 20–30% correction and buy back 26–30% lower, adjusted to sit at the next horizontal (tutorial 2, 09:56–10:29; 15:20–15:50; 20:40–21:11). If the measured drop to the next line is over 30% (45% in his example), settle for 30% (tutorial 2, 20:40).
  - Second time selling a line: buy back only about 18% lower (18–20%) (tutorial 2, 12:39; 16:57; 21:42).
  - Alternatives: buy back at the next horizontal down or at the last structure high (tutorial 2, 10:59).
- **The guiding rule:** "I simply want to sell where I have a statistically good chance to buy back lower within a relatively short timeframe." (tutorial 2, 23:21).
- **Short profit-taking:** when the 4H confirms a downtrend on a candle close, take profits or take 80% and move the stop far into profit (tutorial 3, 37:30–38:01).
- **In no-trend regimes** take profits faster on smaller moves and be "less ambitious" (tutorial 3, 12:25; 40:17).
- **Sell some whenever in profit**, especially altcoins: "when you are in profits sell some hold stable coins" (tutorial 4, 18:35–19:06). An investor-holder's stablecoin share appears when a coin reaches "at least ... 20% profits" (tutorial 5, 09:41).
- **Micro range play:** short the top and long the bottom of a roughly 3–3.25% range, and apply the same signals to alts that follow BTC (tutorial 8, 25:18).

### Position sizing and risk
- **Bull-market new-investor split:** about 50% large caps (CoinMarketCap page 1), 50% mid caps (pages 2–4) plus small caps (page 5 and beyond) (tutorial 5, 09:17–10:53).
- **Coin count:** 15 coins total at most, maybe 10 for newcomers, 15–20 if experienced. Example: 4 large caps plus 10 mid/small caps at $500 each out of $10K (tutorial 5, 15:33–17:00).
- **Futures cap:** at most 25% of the portfolio. About 5% for newcomers who want to trade. Tim keeps about 10% (tutorial 5, 10:15–11:53). In the bear he uses 3L/3S leveraged tokens instead of futures (tutorial 5, 31:40–32:10).
- **Bear-market investor:** fully stable with no hodl position. Keeping about a quarter in BTC "could be okay" (tutorial 5, 13:03–14:03). A trader keeps a small hodl slice (his is DASH and MOVR) and is "more than half stable" (tutorial 5, 31:06–32:40).
- **Horizontal tranche sizes:** 20% per line (5% × 4), 10% (2% × 5) on second sales or weaker lines (tutorial 2, 06:43; 12:05).
- **Micro-candle:** a tiny starter position before points are scored, the full position after (tutorial 8, 12:44).
- **No-trend regimes:** smaller and more cautious buys, e.g. "if you had 5 000 bucks to buy a coin maybe buying at this time is a lot riskier" (tutorial 3, 11:20–11:52).

### Timeframes
- Investor phase: **weekly** EMA10/EMA21/SMA10/SMA20(21). Accumulation/distribution: **monthly** (tutorial 4, 11:46; 13:21).
- Horizontals: weekly (swing, "more like a once a month type of deal"), daily, and 1H–4H for day trading (tutorial 2, 05:11–05:42; 23:51). Order management is done zoomed in on the 4H (tutorial 2, 05:42; 19:05).
- Trend: daily is the main one. The 4H is "more finicky" (tutorial 3, 34:18).
- Recommended extra timeframes: **12H and 2D**, because "whales are setting traps more on the normal time frames like the four hour in the daily and the weekly" (tutorial 1, 19:14–19:46).
- Origin lines: **2-day** BTCUSD, Bitstamp (tutorial 6, 00:00). Channels: 4H/8H macro, 1H micro (tutorial 7, 02:05; 12:34).
- Micro candle ricochet: **15-second** is the most accurate. 1-minute works on free TradingView "simply not quite as perfect". 30-second is also allowed (tutorial 8, 32:55–33:27).
- Log scale always, autoscale off (tutorial 1, 05:17).
- **Weekly closes:** nothing specific is stated about weekly closes. All trend and break rules use candle closes on whichever timeframe is being read.

### Macro context
- Nothing stated about rates, equities or other macro data, beyond a passing remark. The March 2020 "coronavirus dump" is used as a start point (tutorial 3, 03:36), and one 2023 wick is attributed to "something ... in traditional markets" (tutorial 7, 03:08). Weekends since 2019 often have "no one ... in control" (tutorial 8, 10:01).

### Indicator settings stated (exact numbers: lengths, thresholds, colours meaning)
- **"5 exponential" moving-average indicator** (author heard as "unknown unicorn"): five EMAs plus five MAs (tutorial 1, 06:24–08:32).
  - EMA lengths heard: **10, 21, ?, 89, ?**. MA lengths heard: **10, 20, ?, 100, ?**.
  - Pair 1 is EMA10/MA10 and pair 2 is EMA21/MA20. Pair 4 is EMA89 versus MA100: "i have the 100 ma but i use the 89 ema" (tutorial 1, 08:32). The 3rd and 5th lengths were only shown on screen ("pause this and type in the same numbers"), so they are not in the transcript.
  - Colours: pair 1 red, pair 2 yellow, pair 3 blue. EMA at full opacity, the same-numbered MA in the same colour at middle opacity, thickest line (tutorial 1, 09:04–09:40). In tutorial 4 he draws MA10 white for clarity (tutorial 4, 14:23).
  - In the terminal: moving averages, yes.
- **RSI:** "crown trading rsi plus ema plus control zones" [sic; the audio says "crown trading" and author "crypto crown"]. Inputs **22 and 12**: "you just put 22 and 12" (tutorial 1, 12:50). Which input is the RSI length and which the EMA is not said. The upper/lower line values must equal the control-zone values (values not said). Zone fill opacity is about 30 (tutorial 1, 13:57–14:28). The terminal has RSI, but not this exact zone variant.
- **Stochastic** (TradingView built-in): **14, 1, 3**, bands **80 / 20** (tutorial 1, 15:34–16:40). The terminal has it.
- **Bollinger Band Width Percentile** (by "the caretaker"): stock settings. Lookback **252**. Colour type "spectrum" (blue-green-red). Extreme high **98**, extreme low "too" [sic, likely 2]. Style lines **50 and 100**. The MA2 line is unchecked (tutorial 1, 17:43–18:13). Not in the terminal.
- **Volume profile, visible range** (stock) and **Bollinger Bands Fibonacci Ratios** (by "shizaru"): used on a second template, with no numbers (tutorial 1, 23:34–24:38).
- **Accumulation/Distribution with markers** (by "dov caspy" [sic]), used on the monthly for slope only (tutorial 4, 13:52). Not in the terminal, but computable from OHLCV.
- **Moving Average Shaded Fill Area Crossover:** fills the weekly 10/21 EMA band green when EMA10 > EMA21 and red when below (tutorial 5, 15:08–15:44).
- **Line conventions:** weekly horizontals 4 px, daily thinner (tutorial 2, 25:59). Origin lines width 2 in light grey, master thicker, dashed midlines width 1 (tutorial 6, 07:30; 09:37; 10:08). Channel lines width 4, dashed width 1 (tutorial 7, 04:11; 07:19).

### Calibration numbers and falsifiable side-claims (win rates, base rates, "X happens N% of the time")
- **Micro candle ricochet hit rate:** across "seven data sets" (as far back as TradingView's 15-second history went), "In six of them ... the appropriate team was in control" before the tradable move. The seventh was a "dip and rip" off a bullish divergence. Even there the bulls scored a point or two first (tutorial 8, 33:27–33:58). Self-reported, n = 7, retrospective.
- **Bitcoin run statistics** (days up / down / sideways and drawdown, as read off his chart; segment boundaries were on screen) (tutorial 4, 05:05–09:04):
  - Run 1: 42 / 42 / 100, drawdown 45%.
  - Run 2: about 100 up, a "smidge" down, 140 sideways.
  - Run 3: 100 / about 100 / 100, drawdown 80%.
  - Run 4: 50 up, 630 down, about 300 sideways after the low, drawdown 87%.
  - Next: 35 / 56, drawdown 40%.
  - Next: 100 / 35 / 30, with two 30% drops.
  - Next: 50 / 30 / 20, drawdown about 40% (the only segment where sideways was the shortest).
  - Next: 20 / 20 / 14, drawdown about 40%.
  - 2018 run: 65 up, about a year down, 100 sideways, drawdown 84%.
  - Next: 84 / 260 / 200.
  - Base: about 470 days down/sideways, then 85 up.
  - The 2020–21 run: 180 up and 70 down so far, possibly 360 or more down.
  - His claim: sideways usually lasts longer than up and down combined, and "most the money is made" in sideways (tutorial 4, 07:15–07:50).
- **Investor phase signal frequency:** the new-investor state changed 11 times in about 10 years ("roughly once a year") (tutorial 5, 13:28). A portfolio started early changed about 15–17 times over 9–10 years, i.e. one to two sells per year (tutorial 5, 14:03; 22:35).
- **His backtest walk-through (BTC weekly), with holding and waiting times:**
  - Waited 77 days to buy, held about 150–161 days to the warning (tutorial 5, 23:40–24:13).
  - Waited 550 days after a full exit (tutorial 5, 25:20).
  - Warning came 300 days after a rebuy (tutorial 5, 25:20).
  - Out for 399 days in 2018–19. ADA went from $0.68 (2018-01-15) to about $0.03 (about April 2019), "like 25 times more Cardano" (tutorial 5, 27:25–28:28).
  - Held 126 days, then out 189 days, rebuying about 10% below the sell, avoiding a 58% BTC drop (tutorial 5, 28:59–29:33).
  - Other quoted drops: ADA 98.77% top to bottom (tutorial 5, 34:00). BTC 64% with alts about 80% (tutorial 5, 36:53). BTC 55% and SOL 66% from April to July 2021 (tutorial 5, 38:00–39:00). "Stuff goes down 95 to 99% over a course of two years" (tutorial 5, 32:00).
- **The 2020-04 MA10-over-EMA10 warning** "would have" saved newcomers an "80 drop on all coins within just a few weeks" (tutorial 4, 15:56).
- **Horizontal-sell fill times in his BEPRO walk-through** (illustrative, 4H): buybacks filled after 3 days, 8 h, 4 h, 12 h, 16 h and 12 h. Weekly horizontals "often take a month" to come back into play (34 days in the example) (tutorial 2, 07:17–08:22; 10:29; 16:57; 19:38; 21:42; 22:49).
- **Run size vs correction depth:** a 3x (200%) run means about 20% pullbacks, and an 8x (800%) run means about 50% pullbacks (tutorial 2, 07:17; 11:00–11:34). This is falsifiable.
- **The more often a line is hit, the more likely it breaks:** "the more times it hits the line, the more likely it is to really race up" (tutorial 2, 19:38–20:10). This is falsifiable on level-touch counts.
- **A no-trend state after a trend break is "about a 50 50"**, i.e. no edge (tutorial 3, 27:00).
- **Micro entry precision:** a 4H line long entry was "0.4 of one percent" from ideal (tutorial 8, 13:14).

### Dated calls (for later scoring)
| Video date | Asset | Direction | Level / target / invalidation | Horizon stated | Citation |
|---|---|---|---|---|---|
| undated (about Feb 2022, inferred) | BTC | Bearish / sideways | The 2021 top's down phase is "70 [days] which could change ... could end up going down for 360 like a year ... or more". In a big nasty box: "at least gonna go sideways for a year bare minimum" | 1 year or more from the top | (tutorial 4, 08:33–09:04; 16:59–17:33) |
| undated (about early 2022, inferred) | Crypto market (BTC weekly phase) | Bearish: stay stable | "past the warning signs ... full bear market alert". EMA10 just went below EMA21, so stay stable: "this could turn into years". No price level given | "over a year" possible | (tutorial 5, 21:31; 31:06) |
| undated (about early 2023, inferred from the "15 500" low) | BTC | Bullish bounce (conditional) | "this could be a four hour bounce that goes goes goes to 33k" [sic? heard as 33k, which would be far above early-2023 price; possibly 23k]. Entry window at a micro higher low | 4H swing ("mid macro") | (tutorial 8, 22:42) |
| undated (about early 2023, inferred) | BTC | Range | Shorts at the top, longs at the bottom of a roughly 3–3.25% range around current price (levels only on screen) | Next sessions | (tutorial 8, 25:18) |

Tutorials 1, 2, 3, 6 and 7 contain no forward-looking calls with an asset, direction and level. Tutorial 6 only notes that BTC is trading near the master origin line (tutorial 6, 04:12).

### Candidate rules NOT in the pre-registered setup grader
1. **Deep-bear box veto: weekly SMA20 > weekly EMA21.** If true, no long-term longs, and expect at least 52 weeks of down or sideways. Test: forward 52-week max drawdown and return after the cross, versus SMA10 > EMA10 alone. Computable on weekly BTC. The grader has SMA10/EMA10/EMA21 but not SMA20 vs EMA21 (tutorial 4, 16:29–17:33).
2. **Phase allocation backtest (two-step exit).** 100% long (BTC, or a BTC + alt basket) when weekly EMA10 > EMA21 and SMA10 ≤ EMA10. Go to 50% when SMA10 > EMA10. Go to 0% when EMA10 ≤ EMA21. Return to 100% when both conditions restore. Compare with buy-and-hold and with a one-step exit at EMA10 < EMA21, and report the number of switches (he claims 1–2 per year). Computable. The grader uses phase as context, not as an allocation rule (tutorial 5, 11:53–12:56; 24:13–24:44; 26:24).
3. **Monthly accumulation/distribution slope veto.** Monthly A/D (from OHLCV) slope < 0 over the last 1–2 months means veto buys even if weekly EMA10 > EMA21. Computable, but needs reliable volume (exchange volume quality is an issue for early BTC history) (tutorial 4, 13:52–14:23).
4. **BTC daily body-trend gate for altcoin entries.** Daily trend from candle bodies: uptrend = higher body-high pivot and higher body-low pivot. The uptrend is broken at the first daily close below the last confirmed body-low pivot. Alt longs only when the BTC daily trend is up. The "hunt" trigger is the close confirming a new BTC daily uptrend. Computable (needs a pivot definition, e.g. a 2–3 bar span). The grader's trend layer is weekly only (tutorial 3, 15:13–15:47; 22:11; 27:00).
5. **Same-length MA-over-EMA cross on the daily** (SMA10 > EMA10, SMA20 > EMA21) as a bearish signal. Test the forward 1–4 week return versus base rate. Computable (tutorial 1, 10:13–11:16).
6. **Horizontal sell-and-rebuy accumulation ("20 bag").** Sell 20% at a weekly body/wick cluster level (approximated by confirmed weekly body pivots), straddled as 4 × 5% orders within ±x% of the level. Buy back at sell × (1 − d), where d = 0.1 × (prior run %) capped at 50%, or 18% on a line's second sale. Sell each level at most twice, only while the daily body trend is up. Measure coin count versus buy-and-hold, plus buyback fill rate within N days. Partly computable (the level proxy replaces a hand-drawn line) (tutorial 2, 06:13–09:24; 07:17; 11:00; 12:39; 17:28).
7. **Run size predicts correction depth.** From a swing low, if the run is R% then the next pullback ≥ 0.1 × R% (20% after +200%, 50% after +800%). Computable as a side-claim test (tutorial 2, 07:17; 11:00).
8. **Level touch count and break probability.** P(break on the k-th touch) rises with k, which is why he sells a line at most twice. Computable on pivot-derived levels. Note "approach-to-level shape" is dead, but touch count is a different variable (tutorial 2, 19:38–20:10; 17:28).
9. **"Turn on a dime" level strength.** A weekly pivot whose price is not revisited for ≥ N weeks (e.g. 8) is stronger than a multi-touch level. Test the reaction rate on the first retest. Computable (tutorial 2, 00:30–01:33).
10. **Batman sign exit.** For a long, find the most recent confirmed swing low on the trading timeframe, take box = [wick low, body low] and exit on the first close below the wick low. Mirror rule for shorts. Compare with the grader's exits (signal / ATR / 20-bar). Computable on any timeframe (tutorial 8, 11:42–12:12; 13:14).
11. **Micro candle ricochet score at a daily level** (e.g. daily EMA10 or a round number). On 1-minute bars within ±0.1–0.2% of the level, score each touch:
    - Close-back-same-side with no close beyond the level = 2.
    - Close beyond then reclaim within k bars = 1.
    - Pass-through with no return within k bars = 0.
    Take the side with ≥ 3 net points plus a Batman break. Needs data we do not have at 15 s. 1-minute history may be available only for recent periods, and the touch classifier is a judgement call (tutorial 8, 03:04; 13:44–16:28; 32:55).
12. **Three consecutive lower highs in no-trend predict a lower low** (a trend turn to down). Computable with body pivots (tutorial 3, 37:00–37:30).
13. **Close above two prior local highs** as a regime-change / take-profit-on-short signal. Computable (tutorial 8, 15:19).
14. **Round-number levels** (multiples of $1,000 / $10,000 on BTC) as levels for the grader's location layer. Computable (tutorial 8, 04:09).
15. **Coin/BTC pair horizontals once the coin is at its USD all-time high.** Uses the pair's body pivots. Needs alt/BTC pair data (tutorial 2, 17:28–19:05; 24:54).

Already tested dead in this repo, so not proposed: origin lines, equidistant lines and channels (tutorials 6 and 7).

### Not usable / vague
- **Origin lines and channels** (tutorials 6 and 7): hand-drawn, re-fitted "for years". "don't feel bad if you mess around with these for years at a time" (tutorial 6, 12:43). They are fitted after the fact and already tested dead here.
- **The 45° higher-low and "same slope" projection:** angle depends on chart scaling and zoom, so it is not computable as stated (tutorial 3, 23:47; tutorial 8, 17:25).
- **"Whales are setting traps more on the normal time frames"**, the reason given for 12H/2D: untestable as stated (tutorial 1, 19:14–19:46).
- **Micro-candle judgement calls:** "weak ricochet", "best judgment", "this can be art as well" (tutorial 8, 10:35–11:09). The point awarding is partly subjective, and he says results will differ between users (tutorial 8, 34:28).
- **"Yellow triangle / void" no-edge zones where "bots are not seeing"** liquidity: not measurable with our data (tutorial 8, 14:48; 15:49).
- **Horizontal strength by eyeball** ("doesn't have to be exact, but it's got to be kind of close"). No tolerance is given (tutorial 2, 04:08).
- **Third and fifth EMA/MA lengths, and the RSI zone values:** shown on screen only, not spoken (tutorial 1, 08:32; 13:57).
- **"Shorting into a fall" / range plays in tutorial 8:** levels are only on screen.

## Part 2: public YouTube channel

(pending)
