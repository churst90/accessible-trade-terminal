# Cosasverdes (Tim): extracted rules

Source slug `cosasverdes`. **School: structure** (trend structure, horizontals, level reactions).

- **Channel:** cosasverdes ("Tim"): his tutorial library plus his public YouTube channel.
- **Transcripts read:** 25, all in full.
  - Part 1: 8 tutorial-library videos, transcribed with Whisper large-v3-turbo. They have no public URL and are cited `(tutorial N, mm:ss)`.
  - Part 2: 17 public YouTube videos, YouTube auto-captions (ASR).
- **Date range:** tutorials 2021-12-18 to about early 2023 (most are undated; see Part 1). Public videos 2021-03-08 to 2024-11-19.
- **Material:**
  - Part 1: education and tool tutorials.
  - Part 2: public Bitcoin analysis videos with dated price calls. From 2024 most were recorded at the start of his private live streams.
- **Access:**
  - Public: tutorials 1–7 (on Cody's word that the creator publishes them) and all 17 YouTube videos.
  - Community or paid: tutorial 8 (Micro Candle Ricochet), which was sold: "why did you pay money for this" (tutorial 8, 04:09).

## Method in its own terms

Tim reads price as **structure**. A trend exists only when candle bodies make a higher local high AND a higher local low (or the mirror). It changes only on a candle close. Bitcoin's trend governs every altcoin decision.

**Regime** comes from a few moving averages read as structure:
- **Weekly EMA10 / EMA21 / SMA10.** EMA10 above EMA21 is bull. The SMA10 rising above the EMA10 is the "warning": sell half. EMA10 below EMA21 is bear: sell the rest and wait, often a year or more. The weekly SMA20/21 above the EMA21 is the "big nasty box".
- **The weekly 10 EMA as a magnet.** Price gets "sucked back" to it, and in a bear market reacts in only three ways: comes up shy, touches it, or makes a failure pattern above it.
- **The 2-day EMA10/21 cloud.** Its first red flip after a run has marked every macro top.
- **The daily EMA10/21/55 spacing,** which separates a buyable dip from a dump.

**Location** is a proven level: a weekly horizontal at candle-body and wick clusters (strongest where price "turned on a dime"), a big moving average, or a round number. Hand-drawn origin lines and channels also count for him; this repo has already tested those dead.

**Trigger:** watch how price behaves at the level. A clean rejection scores 2 points, a pierce-and-reclaim 1, a pass-through 0 (tutorial 8's micro-candle point system). For a breakout, price must punch through, retest the level, then take out the "punch high". A breakdown is the mirror, through the "punch low". He wants higher lows into resistance, a 45° slope, and a "look left" target.

**Confirmations:** a daily RSI reading of 80 (Bitcoin then keeps making daily highs), three drives down with RSI higher lows, and several daily closes above a reclaimed weekly 10 EMA.

**Vetoes:** no altcoin buys while Bitcoin's daily trend is broken, no shorts in a bull market, no buying in the warning or bear phases. A 10–12% bounce inside a bear is "still bearish".

**Stops:** structural, not ATR. Exit when the last swing low breaks (the "Batman sign"), or bail when the structure fails. He prefers to go neutral before flipping direction.

**Exits:** sell into weekly horizontals in 20% tranches and buy back 18–50% lower, the depth scaled to the size of the run (a 500–800% run means a 50–55% pullback). Sell a line at most twice. In the cycle, sell by phase.

**Sizing:**
- Half large caps and half mid/small caps, at most 15 coins.
- Futures at most 25% of the portfolio (5% for newcomers). Leverage at most 4–5x.
- A partial cash reserve while Bitcoin dominance is rising, because altcoins fall up to twice as hard as Bitcoin.

## Part 1: tutorial library (8 local videos)

Transcripts read (all read in full): `2021-12-18_tutorial-1` (How To Set Up A Chart), `0000-00-00_tutorial-2` (Horizontals: How to Set them + How to SELL), `2021-12-18_tutorial-3` (Let the trend be your friend), `0000-00-00_tutorial-4` (Identifying where you are in the crypto cycle), `0000-00-00_tutorial-5` (Portfolio Management), `0000-00-00_tutorial-6` (Origin lines and object tree), `0000-00-00_tutorial-7` (Channels), `0000-00-00_tutorial-8` (Micro Candle Ricochet).

Dates. Tutorials 1 and 3 are dated 2021-12-18 (tutorial 3 states "december 18th 2021", tutorial 3, 04:00). The rest are undated (`0000-00-00`). Clues in the audio, which are inference and were not stated as dates: tutorial 4 talks to viewers "beyond february 2022" (tutorial 4, 09:00), so it is about early 2022. Tutorial 5 says the market is "past the warning signs... full bear market alert" (tutorial 5, 31:06), so it is about early 2022. Tutorial 6 says Bitcoin is close to the master origin line "when i'm making it" and mentions February/March 2022 candles (tutorial 6, 04:12; 04:47), so it is about March 2022 or later. Tutorial 8 says Bitcoin bounced "very hard off of its lows from 15 500 a couple months earlier" (tutorial 8, 05:21). The 15.5K low was in November 2022, so tutorial 8 is about early 2023.

Kind of material: education and tool tutorials. It covers TradingView setup, how to draw horizontals and sell them in tranches, trend definitions from candle bodies, cycle phase from weekly moving averages, phase-driven portfolio allocation, hand-drawn origin lines and channels, and a sub-minute "point system" for reading how price reacts at a level.

Part 1 in brief: this is the investor and swing layer of the method above.
- **Weekly moving-average phase:** sell half at the warning, sell the rest at bear, buy back only when bull is restored.
- **Selling weekly horizontals:** sell 20% in tranches, buy back about 20% lower, at most twice per line, only while the daily trend is up.
- **Body-based trend states,** with Bitcoin's daily trend gating altcoin buys.
- **The micro-candle point system and the "Batman sign"** for timing at a level.

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
- **Weekly closes:** nothing specific is stated about weekly closes in the tutorials. All trend and break rules use candle closes on whichever timeframe is being read. Part 2 adds explicit weekly-close rules: a week closing below the weekly low, and needing a week or two of closes above the weekly 10 EMA.

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

All 17 public transcripts were read in full. They are YouTube auto-captions (ASR), so numbers and names are sometimes mis-heard; corrections are in brackets. The brief said 2022–2025, but the actual video dates run **2021-03-08 to 2024-11-19**.

In citations below, each video goes by the short title in this table:

| Date | Video id | Full title (manifest) | Short title used in citations |
|---|---|---|---|
| 2021-03-08 | ZnAm-o3b-sk | ORIGIN LINES: Bitcoin Price Technical Analysis BEPRO, Cardano, Ethereum | ORIGIN LINES |
| 2021-11-17 | nDhA8HEJgAg | 1 SIMPLE TRICK crypto bitcoin price [ make more money in crypto ] | 1 SIMPLE TRICK |
| 2021-12-15 | lWSvR3DKRDg | Bitcoin BTC price ( ORIGIN-WAVE THEORY by cosasverdes ) | ORIGIN-WAVE THEORY |
| 2022-07-28 | MUR-vwc1KdA | Trading Crypto and Bitcoin - If You Are New to Trading, TA ≠ Trading | TA ≠ Trading |
| 2022-12-02 | wnumqaYJCeU | Bitcoin Price $18,309 Origin Line Target (Use These 4 Tools To Get BTC Price right! Step-by-step) | $18,309 Origin Line Target |
| 2023-05-22 | 8R5bdGlMBb0 | Bitcoin CRASH To $21,450 By July 1st ( BTC Price Analysis ) | CRASH To $21,450 By July 1st |
| 2023-06-06 | IQI6y3Fw6Ws | Bitcoin Price $21,300 Mid Summer (Top 3 Price Paths on BTC journey down) | $21,300 Mid Summer |
| 2023-06-13 | D9fc1UjBV64 | Bitcoin Price $21,300 by July 1st (Still On Track) | $21,300 by July 1st |
| 2023-10-25 | pcXnQM_-wzE | $40K Bitcoin Price Prediction Could be Too Low for BTC (Oct 2023 BTC prediction) | $40K Could be Too Low |
| 2023-11-01 | a_e-oGqrl4g | Bitcoin Price $47K in November could become a reality (here's why) | $47K in November |
| 2024-04-11 | Zu_2k_R35iM | The three time frames you should use to analyze Bitcoin Price right now (and why) | The three time frames |
| 2024-04-12 | Pn09kGwmbgU | You need to know Bitcoin Dominance's effect on BTC price (Cheat Code for analysis) | Bitcoin Dominance's effect |
| 2024-05-10 | eXGNKLdqWk8 | Let me show you a Bitcoin Price Cheat Code & High Probability Guide (Easy to do) | Cheat Code & High Probability Guide |
| 2024-06-14 | FHxc_hnNKUA | Bitcoin Price CRASH to $60,000 and here is why | CRASH to $60,000 |
| 2024-06-25 | A4YzJcwHB1M | Bitcoin Price & How I predicted $60K to be hit on this day 12 days ago | How I predicted $60K |
| 2024-06-27 | MGKPt-KbSPQ | Bitcoin Price $42K Target if this happens in next 10 days? | $42K Target |
| 2024-11-19 | sj_Rj19PrbY | Why $500K Bitcoin is a realistic target in 2025 | $500K realistic target in 2025 |

**How Part 2 relates to Part 1.**

Confirms Part 1:
- Bitcoin governs altcoins, and altcoins fall harder.
- Sell into strength and buy back lower using "other people's money".
- The 45° higher low, and "look left".
- The "punch low" is the same thing as tutorial 8's "hits its head".
- No shorting in a bull market.
- Leverage and futures limits.
- A big run means a big correction.

Extends Part 1 (all new):
- The weekly 10 EMA "magnet" and its three bear-market reactions.
- The daily 10/21/55 EMA dip test.
- The 2-day EMA10/21 cloud as a top marker.
- The daily RSI 80 rule.
- Explicit weekly-close rules.
- Monthly-open (monthly candle colour) gating.
- Bitcoin dominance regimes.
- Stocks, FOMC and CPI context.

Contradicts or qualifies Part 1:
- In 2021 he says to buy "a little" of the blood near capitulation, at an origin line or under the 200 EMA ([ORIGIN LINES, 2021-03-08, 17:26](https://youtu.be/ZnAm-o3b-sk?t=1046)). Tutorial 5 says to buy nothing until the weekly EMAs flip back.
- Tutorial 8 rates micro-candle reactions above candle closes. In 2024 he says closes on the 4H, daily and monthly are what currently works, that this changes over time, and that it has a "shelf life" ([The three time frames, 2024-04-11, 14:05](https://youtu.be/Zu_2k_R35iM?t=845); [Cheat Code & High Probability Guide, 2024-05-10, 12:29](https://youtu.be/eXGNKLdqWk8?t=749)).
- The "weekly 10 equivalent" on the daily is the 55 EMA in 2021 ([1 SIMPLE TRICK, 2021-11-17, 15:48](https://youtu.be/nDhA8HEJgAg?t=948)) and the 70 EMA in 2023 ([$21,300 Mid Summer, 2023-06-06, 25:43](https://youtu.be/IQI6y3Fw6Ws?t=1543)). Arithmetically, 10 weeks is 70 days.

### Context / higher timeframe / regime
- **Weekly 10 EMA as the single best tool and a magnet.** Too much separation from it "sucks price back" in both directions. In a bear market there are only three reactions: price comes up shy of it, touches it exactly, or makes a "failure pattern above" (a fake-out over it). A bull market adds a fourth, staying above. "if you only had one tool this is the tool I would use" ([$18,309 Origin Line Target, 2022-12-02, 10:14](https://youtu.be/wnumqaYJCeU?t=614); [11:49](https://youtu.be/wnumqaYJCeU?t=709)). The terminal has moving averages.
- **A bull market requires respecting the weekly 10 EMA first.** At the 2018–19 bottom Bitcoin respected the weekly 10 EMA for 14 weeks before its rally; in 2023 it was "zero weeks". "it's literally a requirement" ([CRASH To $21,450 By July 1st, 2023-05-22, 02:03](https://youtu.be/8R5bdGlMBb0?t=123)).
- **Turning bullish on a weekly 10 EMA reclaim takes time.** A spike above it is not bullish. He would only start "flipping my tune" after 3–4 daily closes above the weekly 10 EMA, roughly a week or two of closes with little volatility ([$21,300 by July 1st, 2023-06-13, 09:39](https://youtu.be/D9fc1UjBV64?t=579); [10:10](https://youtu.be/D9fc1UjBV64?t=610)).
- **Stuck under the weekly 21 EMA is bearish** (the 2018 example) ([ORIGIN LINES, 2021-03-08, 16:56](https://youtu.be/ZnAm-o3b-sk?t=1016)).
- **Weekly-close trend break.** A week closing below the last weekly low historically had "an 86% historical chance", "eight out of nine", of continuing down. In late 2023 it failed (he blames a bear trap tied to the BlackRock ETF) ([$42K Target, 2024-06-27, 05:48](https://youtu.be/MGKPt-KbSPQ?t=348)). In Nov 2023 he also calls the weekly trend "no trend cuz it closed below", then a fake-out ([$47K in November, 2023-11-01, 05:05](https://youtu.be/a_e-oGqrl4g?t=305)). This extends tutorial 3's body/close trend rules to the weekly.
- **Daily three-EMA dip test (10 red, 21 yellow, 55 blue).** When the daily EMA10 comes down to the EMA21, look at how far the EMA21 is above the EMA55. "the farther away that yellow is from blue the stronger the chance that it's a buy the dip scenario". The closest spacing preceded the dump ([1 SIMPLE TRICK, 2021-11-17, 03:03](https://youtu.be/nDhA8HEJgAg?t=183); [04:03](https://youtu.be/nDhA8HEJgAg?t=243); [05:03](https://youtu.be/nDhA8HEJgAg?t=303)).
- **Daily closes back above EMA10 before EMA10 touches EMA21** mean "statistically likely out of the woods". The next dip is then just a wick, a 1H/4H higher low ([1 SIMPLE TRICK, 2021-11-17, 07:37](https://youtu.be/nDhA8HEJgAg?t=457); [08:08](https://youtu.be/nDhA8HEJgAg?t=488)).
- **Bear-market daily regime.** The daily EMA55 sits "almost always ... very close to its weekly 10 ema". In a bear, when the daily EMA10 gets above the EMA55 and turns, price "is going to tank again" ([1 SIMPLE TRICK, 2021-11-17, 15:48](https://youtu.be/nDhA8HEJgAg?t=948)).
- **2-day EMA10/21 cloud as a macro-top marker.** At the $1,300 (2013), 20K (2017), 14K (2019), 64K and 69K (2021) tops, "once it flipped red the top was in", with zero fake-outs at tops. Pullbacks on the way up flipped it red only "barely" (false alarms) ([Cheat Code & High Probability Guide, 2024-05-10, 02:07](https://youtu.be/eXGNKLdqWk8?t=127); [04:13](https://youtu.be/eXGNKLdqWk8?t=253); [06:18](https://youtu.be/eXGNKLdqWk8?t=378)). "last five times it flipped green it went crazy" ([06:48](https://youtu.be/eXGNKLdqWk8?t=408)). This extends Part 1's weekly 10/21 phase to the 2-day.
- **Shelf life by timeframe.** A move after a signal lasts about a day and a half on the 2H, about five days to a week on the 4H, almost a month on the daily, and a year to a year and a half on the monthly ([1 SIMPLE TRICK, 2021-11-17, 06:37](https://youtu.be/nDhA8HEJgAg?t=397)).
- **Monthly candle colour (price against the monthly open).** While the monthly candle is red there is no violent upside, so you can "relax". It "needs to flip green before violent price to the upside is going to happen" ([The three time frames, 2024-04-11, 07:52](https://youtu.be/Zu_2k_R35iM?t=472); [08:25](https://youtu.be/Zu_2k_R35iM?t=505)). Green months often wick below the open early, then flip green and charge ([Bitcoin Dominance's effect, 2024-04-12, 05:13](https://youtu.be/Pn09kGwmbgU?t=313)). A monthly doji near the top puts you on "lower high watch" on the 4H ([08:16](https://youtu.be/Pn09kGwmbgU?t=496)).
- **Bitcoin dominance regimes.** Dominance rises in two ways: a Bitcoin-led run, or a market-wide crash (alts fall more). It falls only in an all-crypto rally where alts outpace BTC ([Bitcoin Dominance's effect, 2024-04-12, 00:01](https://youtu.be/Pn09kGwmbgU?t=1); [01:38](https://youtu.be/Pn09kGwmbgU?t=98)). This extends tutorials 4 and 5 ("altcoins are even worse").
- **Trend outranks indicators, except inside a "yellow triangle."** "Trend is by far the more important than any indicator ... unless it's in a yellow triangle", meaning price has recently fallen out of a macro channel, made worse by FOMC ([$21,300 by July 1st, 2023-06-13, 12:14](https://youtu.be/D9fc1UjBV64?t=734)). This qualifies tutorial 3.
- **Young-asset "speed limits" and the 500–600% rule.** A 500–600% run needs a 50–55% correction, "period". He regrets exempting BTC in 2021 and now treats BTC like an altcoin ([ORIGIN-WAVE THEORY, 2021-12-15, 04:38](https://youtu.be/lWSvR3DKRDg?t=278); [05:09](https://youtu.be/lWSvR3DKRDg?t=309)). This is consistent with tutorial 2's 800% → 50%. The origin-line speed-limit geometry itself is dead here.

### Location (levels, zones, where a setup counts)
- **Proven against theoretical support.** Only a level that price has "proved ... over and over" counts, e.g. a level ridden for a month. A merely theoretical line does not ([CRASH to $60,000, 2024-06-14, 03:06](https://youtu.be/FHxc_hnNKUA?t=186); [03:37](https://youtu.be/FHxc_hnNKUA?t=217)).
- **Look left after leaving a channel.** When price falls out of an ascending channel, the target is the first prominent area to the left where a strong low and a high sit at the same level ([$40K Could be Too Low, 2023-10-25, 03:09](https://youtu.be/pcXnQM_-wzE?t=189)).
- **Previous support becomes future resistance**, more strongly the longer and higher-timeframe the support was ([$500K realistic target in 2025, 2024-11-19, 09:15](https://youtu.be/sj_Rj19PrbY?t=555); [$47K in November, 2023-11-01, 01:33](https://youtu.be/a_e-oGqrl4g?t=93)).
- **Big moving averages as levels:** the weekly 10, monthly 10, weekly 200 and daily 300 EMAs. Price below a "huge" EMA "invites mosquitoes", meaning other big money starts selling, so big players defend it ([$21,300 by July 1st, 2023-06-13, 01:01](https://youtu.be/D9fc1UjBV64?t=61); [05:34](https://youtu.be/D9fc1UjBV64?t=334)). Buy zone in a bear: under the weekly 200 EMA and SMA ([ORIGIN LINES, 2021-03-08, 19:00](https://youtu.be/ZnAm-o3b-sk?t=1140)). The 4H 200 often lines up with the daily 21 for corrections ([25:11](https://youtu.be/ZnAm-o3b-sk?t=1511)).
- **Round numbers:** a 60,000–62,500 zone "for round number purposes" ([How I predicted $60K, 2024-06-25, 05:36](https://youtu.be/A4YzJcwHB1M?t=336)), and 100K as a psychological target ([$500K realistic target in 2025, 2024-11-19, 12:20](https://youtu.be/sj_Rj19PrbY?t=740)).
- **His horizontals match order-book heat maps** "almost perfectly" (claim) ([$18,309 Origin Line Target, 2022-12-02, 21:03](https://youtu.be/wnumqaYJCeU?t=1263)).
- **Yellow triangle (extends tutorial 8):** a holding pattern for big money waiting on stocks. Inside it, micro trend is "a little bit more useless" ([$21,300 Mid Summer, 2023-06-06, 04:34](https://youtu.be/IQI6y3Fw6Ws?t=274); [26:46](https://youtu.be/IQI6y3Fw6Ws?t=1606)).
- **Origin lines, the super macro mega channel and the "pace line"** are hand-drawn parallel-line geometry. The repo has already tested this family dead, so it is recorded only under Dated calls and Not usable.

### Entry triggers
- **Getting over resistance: punch, retest, take out the punch high.** Price closes above resistance (wicks do not count: "not on a wick wicks don't count"), comes back to test it as support, and "things start cooking after you take out the punch high". The 29,100 retest came within $43 (0.14%) ([$40K Could be Too Low, 2023-10-25, 14:27](https://youtu.be/pcXnQM_-wzE?t=867); [16:30](https://youtu.be/pcXnQM_-wzE?t=990); [17:41](https://youtu.be/pcXnQM_-wzE?t=1061); [18:12](https://youtu.be/pcXnQM_-wzE?t=1092)).
- **Breaking down: the punch low.** Price closes below proven support, is rejected on the retest, then takes out the punch low. That is where it accelerates, "over 70%" of the time ([CRASH to $60,000, 2024-06-14, 04:07](https://youtu.be/FHxc_hnNKUA?t=247); [04:37](https://youtu.be/FHxc_hnNKUA?t=277)). Same mechanism as tutorial 8's "hits its head".
- **Higher lows into resistance (ascending triangle), two attempts.** On 5m–1h, keeping higher lows under resistance makes up more likely. "you have two attempts ... but you have to get above it on the second time" ([$40K Could be Too Low, 2023-10-25, 07:46](https://youtu.be/pcXnQM_-wzE?t=466); [15:28](https://youtu.be/pcXnQM_-wzE?t=928)).
- **Buy the first 4H higher low after the daily dip test passes.** Don't catch the red candle. A 4H higher low "usually takes about four days", three to four days ([1 SIMPLE TRICK, 2021-11-17, 10:11](https://youtu.be/nDhA8HEJgAg?t=611); [12:12](https://youtu.be/nDhA8HEJgAg?t=732)). "don't chase red candles just like you don't want to chase green candles" ([09:39](https://youtu.be/nDhA8HEJgAg?t=579)).
- **Knife-catching only with three drives down.** Three lower lows in price with three higher lows on RSI (bullish divergence) on at least the 1H, better the 4H. "that's the only time i try to catch a knife". Try once, maybe twice, with risk management ([1 SIMPLE TRICK, 2021-11-17, 11:11](https://youtu.be/nDhA8HEJgAg?t=671); [12:42](https://youtu.be/nDhA8HEJgAg?t=762); [13:12](https://youtu.be/nDhA8HEJgAg?t=792)). The terminal has RSI.
- **Bear-market short ("Russian doll").** Only when the daily EMA10 is above the daily EMA55, drop to the 4H and short the 4H EMA10/21 bearish cross. Use risk management, get out, and retry the next time the rule fires ([1 SIMPLE TRICK, 2021-11-17, 15:16](https://youtu.be/nDhA8HEJgAg?t=916); [18:25](https://youtu.be/nDhA8HEJgAg?t=1105); [19:58](https://youtu.be/nDhA8HEJgAg?t=1198)).
- **Front-running a breakdown.** He takes a "lower high within a lower high", but only once price also loses the rising 45° line (a pennant) ([How I predicted $60K, 2024-06-25, 03:33](https://youtu.be/A4YzJcwHB1M?t=213); [04:03](https://youtu.be/A4YzJcwHB1M?t=243)).
- **2-day cloud decision point.** While the cloud is red, "assume down until candles close above it". On the first 2-day closes above it, "get poised for either". When it flips green, expect "a lot of upside", at minimum taking out the last high ([Cheat Code & High Probability Guide, 2024-05-10, 08:21](https://youtu.be/eXGNKLdqWk8?t=501); [09:22](https://youtu.be/eXGNKLdqWk8?t=562); [10:23](https://youtu.be/eXGNKLdqWk8?t=623)).
- **Buy "a little" of the blood** at capitulation and add as it falls, rather than spending all your stablecoins ([ORIGIN LINES, 2021-03-08, 17:26](https://youtu.be/ZnAm-o3b-sk?t=1046)). This contradicts tutorial 5 (see above).

### Confirmations (things that raise conviction)
- **Daily RSI hitting 80.** "Bitcoin hits daily RSI hits 80 it just keeps going". It "almost doesn't have permission to not make new daily highs", with one or two exceptions (2019). Example: in Jan–Feb 2023 it made 4–5 more daily higher highs ([$47K in November, 2023-11-01, 10:17](https://youtu.be/a_e-oGqrl4g?t=617); [10:49](https://youtu.be/a_e-oGqrl4g?t=649); [11:52](https://youtu.be/a_e-oGqrl4g?t=712)). The RSI "green zone" starts at about 65. On the 4H and above, RSI dipping out of the zone (under 60) and back in usually comes with a higher high in price even when RSI makes a lower high ([12:58](https://youtu.be/a_e-oGqrl4g?t=778)). The terminal has RSI. The RSI length is not stated here; tutorial 1's variant is 22/12.
- **A weak bull reaction at broken support confirms the bears.** If price does not even get back up to retest broken support from below, "the bulls are half-assing this" ([CRASH to $60,000, 2024-06-14, 05:38](https://youtu.be/FHxc_hnNKUA?t=338); [06:09](https://youtu.be/FHxc_hnNKUA?t=369)).
- **Probability stacking.** He multiplies successive probable steps: 0.7 × 0.7 ≈ 49% ([CRASH to $60,000, 2024-06-14, 08:45](https://youtu.be/FHxc_hnNKUA?t=525); [09:15](https://youtu.be/FHxc_hnNKUA?t=555)).
- **Closes on the "algo" timeframes.** In 2024 he says the 4H, daily and monthly closes are where big players' algorithms act, so a 4H close is the confirmation ([The three time frames, 2024-04-11, 14:05](https://youtu.be/Zu_2k_R35iM?t=845); [Cheat Code & High Probability Guide, 2024-05-10, 13:00](https://youtu.be/eXGNKLdqWk8?t=780)).
- **Weekly candle signals can mislead.** In April 2024, hanging-man and doji weekly candles pointed the opposite way to price: "this is not the right time frame" ([The three time frames, 2024-04-11, 08:55](https://youtu.be/Zu_2k_R35iM?t=535)).

### Vetoes (things that cancel a trade whatever else says)
- **A 10–12% bounce inside a bear is still bearish.** "28k or two sevens is hit it's not bullish ... period". It is a failure pattern above the weekly 10 ([$21,300 Mid Summer, 2023-06-06, 25:12](https://youtu.be/IQI6y3Fw6Ws?t=1512); [$21,300 by July 1st, 2023-06-13, 08:35](https://youtu.be/D9fc1UjBV64?t=515)).
- **No shorts in a bull market.** "shorting bull market is not smart". Only leveraged longs, then go flat during the month-long corrections ([ORIGIN LINES, 2021-03-08, 23:39](https://youtu.be/ZnAm-o3b-sk?t=1419); [27:13](https://youtu.be/ZnAm-o3b-sk?t=1633)). This confirms tutorial 3.
- **No 4H-cross shorts unless the daily EMA10 is above the daily EMA55** ([1 SIMPLE TRICK, 2021-11-17, 19:58](https://youtu.be/nDhA8HEJgAg?t=1198)).
- **No-trade zone.** Before a decisive daily close at a key level, "sometimes in bitcoin no trade is the best trade" ([ORIGIN LINES, 2021-03-08, 23:08](https://youtu.be/ZnAm-o3b-sk?t=1388)).
- **No new altcoin exposure until the triangle is beaten to the upside** ([How I predicted $60K, 2024-06-25, 03:02](https://youtu.be/A4YzJcwHB1M?t=182)).
- **New traders.** "TA ≠ trading". Without risk management his correct calls can still wreck you ([TA ≠ Trading, 2022-07-28, 00:30](https://youtu.be/MUR-vwc1KdA?t=30)). Be profitable, or at least break even, for six months before trading ([ORIGIN LINES, 2021-03-08, 24:40](https://youtu.be/ZnAm-o3b-sk?t=1480); [1 SIMPLE TRICK, 2021-11-17, 04:03](https://youtu.be/nDhA8HEJgAg?t=243)).

### Stops and invalidation
- **Risk management because no rule always works.** "even the best rules in the world don't work every time that's why risk management exists". Get out, then take the rule again next time ([1 SIMPLE TRICK, 2021-11-17, 18:25](https://youtu.be/nDhA8HEJgAg?t=1105)).
- **Stop just above the level for a short.** His bull-market short at a line was squeezed; the stop above it limited the loss to about $150 ([ORIGIN LINES, 2021-03-08, 27:13](https://youtu.be/ZnAm-o3b-sk?t=1633); [27:44](https://youtu.be/ZnAm-o3b-sk?t=1664)).
- **Structural invalidation on a 4H close.** A 4H close below 68,389 lifts the chance of 60K from "under 20%" to much higher, even with the daily still in a higher-low posture ([The three time frames, 2024-04-11, 16:38](https://youtu.be/Zu_2k_R35iM?t=998); [17:09](https://youtu.be/Zu_2k_R35iM?t=1029)).
- **Bail, go neutral, then flip.** "I typically don't like instantly flipping I like to bail stay neutral wait and then flip" ([$42K Target, 2024-06-27, 14:03](https://youtu.be/MGKPt-KbSPQ?t=843)).
- No fixed percentage or ATR stop is stated.

### Exits and targets
- **Sell altcoins when Bitcoin rejects a level, then buy back lower.** A 10–15% BTC move means 20–30% on alts, so "accumulating crypto with other people's money". This confirms tutorial 2 ([How I predicted $60K, 2024-06-25, 05:36](https://youtu.be/A4YzJcwHB1M?t=336); [06:08](https://youtu.be/A4YzJcwHB1M?t=368)).
- **Expected depth of corrections:**
  - A 30% pullback "is average for a bull run" ([The three time frames, 2024-04-11, 10:15](https://youtu.be/Zu_2k_R35iM?t=615)).
  - If BTC corrects 30% while dominance is rising, top-50 alts fall about 35–40%, and smaller caps up to about twice the BTC drop (50–60%) ([$500K realistic target in 2025, 2024-11-19, 14:22](https://youtu.be/sj_Rj19PrbY?t=862); [14:52](https://youtu.be/sj_Rj19PrbY?t=892)).
  - This extends tutorial 5's page-based caps and is consistent with tutorial 4.
- **Take altcoin profits faster when Bitcoin is trapped below its "pace line"**, and let alts run when it is above. The line is hand-drawn ([$500K realistic target in 2025, 2024-11-19, 07:14](https://youtu.be/sj_Rj19PrbY?t=434); [10:17](https://youtu.be/sj_Rj19PrbY?t=617)).
- **The first red flip of the 2-day cloud after a run means the top is in** (see Context) ([Cheat Code & High Probability Guide, 2024-05-10, 04:13](https://youtu.be/eXGNKLdqWk8?t=253)).
- **Earn yield on stablecoins during corrections**, e.g. "like 40 interest" on dYdX in 2021 ([ORIGIN LINES, 2021-03-08, 23:39](https://youtu.be/ZnAm-o3b-sk?t=1419)).

### Position sizing and risk
- **Leverage:** "four to five ex leverages maximum". Newcomers should use no futures at all ([1 SIMPLE TRICK, 2021-11-17, 04:03](https://youtu.be/nDhA8HEJgAg?t=243)). This tightens tutorial 5's futures limits into a leverage cap.
- **Cash reserve while dominance rises.** Nov 2024: "I am 70% into crypto 30% of my active crypto money is sidelined", waiting for the pullback ([$500K realistic target in 2025, 2024-11-19, 15:52](https://youtu.be/sj_Rj19PrbY?t=952)). June 2024: mostly flat in cash, more exposed to silver ([How I predicted $60K, 2024-06-25, 03:02](https://youtu.be/A4YzJcwHB1M?t=182)).
- **Practice size.** His counter-trend bull-market short was "like 150 bucks just to practice" ([ORIGIN LINES, 2021-03-08, 27:44](https://youtu.be/ZnAm-o3b-sk?t=1664)).
- **Buying blood in pieces.** "don't spend all of your us dollar bags just buy a little" ([ORIGIN LINES, 2021-03-08, 17:26](https://youtu.be/ZnAm-o3b-sk?t=1046)).

### Timeframes
- **The best timeframes change with the regime.** In April 2024 he used the monthly (candle colour against the open), the daily (structure) and the 4H (closes), and dropped the weekly. More usual sets are daily/weekly/monthly or 1H/4H/weekly ([The three time frames, 2024-04-11, 12:33](https://youtu.be/Zu_2k_R35iM?t=753); [17:39](https://youtu.be/Zu_2k_R35iM?t=1059)).
- **Equivalents of the 2-day 10/21 EMA cloud:** 1D 20/42, and 4H ×12 = 120/252 ([Cheat Code & High Probability Guide, 2024-05-10, 03:02](https://youtu.be/eXGNKLdqWk8?t=182); [10:54](https://youtu.be/eXGNKLdqWk8?t=654)).
- **Viewing higher-timeframe EMAs on lower charts:** an MTF EMA indicator shows the weekly 10 on the 1H ([$18,309 Origin Line Target, 2022-12-02, 11:17](https://youtu.be/wnumqaYJCeU?t=677)). Between monthly closes the daily 300 EMA "is much more accurate" than the monthly 10 ([$21,300 Mid Summer, 2023-06-06, 17:25](https://youtu.be/IQI6y3Fw6Ws?t=1045)).
- **Higher timeframe means a bigger effect**, and the shelf-life ladder in Context (2H to monthly) applies ([1 SIMPLE TRICK, 2021-11-17, 06:37](https://youtu.be/nDhA8HEJgAg?t=397)).
- **Weekly closes:** see Context (a week closing below the weekly low; a week or two of closes above the weekly 10 EMA).

### Macro context
- **Stocks lead Bitcoin.** "stocks are clearly walking Bitcoin around like a dog", Powell moves stocks, and the next big economic data (mid-December 2022) gates continuation. He uses forexfactory.com for dates ([$18,309 Origin Line Target, 2022-12-02, 00:00](https://youtu.be/wnumqaYJCeU?t=0); [01:01](https://youtu.be/wnumqaYJCeU?t=61)).
- **US500 trigger.** If US500 (Pepperstone) loses 4,181 (heard "41.81"), the BTC breakdown path becomes the second most probable ([$21,300 Mid Summer, 2023-06-06, 21:04](https://youtu.be/IQI6y3Fw6Ws?t=1264); [22:07](https://youtu.be/IQI6y3Fw6Ws?t=1327)). If stocks are strong while BTC loses the daily 300 EMA, big money rotates out of crypto, and new lows become possible ([17:57](https://youtu.be/IQI6y3Fw6Ws?t=1077)).
- **FOMC volatility.** FOMC "by default has permission to move price ten percent in volatility total" that day. CPI and FOMC "often play seven to nine percent", split e.g. 4/6 or 3/7 ([$21,300 by July 1st, 2023-06-13, 04:34](https://youtu.be/D9fc1UjBV64?t=274); [06:34](https://youtu.be/D9fc1UjBV64?t=394)).
- **Bullish wildcards that could void his bearish calls:** a currency rejection in Argentina triggering an adoption wave, or a US inflation scare ([CRASH To $21,450 By July 1st, 2023-05-22, 13:59](https://youtu.be/8R5bdGlMBb0?t=839); [17:31](https://youtu.be/8R5bdGlMBb0?t=1051)). He sees altcoins with no revenue and over $1B market cap as overvalued ([$21,300 Mid Summer, 2023-06-06, 18:28](https://youtu.be/IQI6y3Fw6Ws?t=1108)).
- **ETF / BlackRock** as the force behind the late-2023 bear trap (speculation) ([$42K Target, 2024-06-27, 06:20](https://youtu.be/MGKPt-KbSPQ?t=380)).

### Indicator settings stated (exact numbers: lengths, thresholds, colours meaning)
- **Daily EMAs:** 10 (red), 21 (yellow), 55 (blue) ([1 SIMPLE TRICK, 2021-11-17, 03:03](https://youtu.be/nDhA8HEJgAg?t=183); [14:46](https://youtu.be/nDhA8HEJgAg?t=886)).
- **2-day EMA 10/21 fill** ("EMA fill"), with free-plan equivalents 1D 20/42 and 4H 120/252 ([Cheat Code & High Probability Guide, 2024-05-10, 03:02](https://youtu.be/eXGNKLdqWk8?t=182); [11:27](https://youtu.be/eXGNKLdqWk8?t=687)).
- **Daily RSI:** zone from about 65, trigger at 80, 60 as the dip level ([$47K in November, 2023-11-01, 10:17](https://youtu.be/a_e-oGqrl4g?t=617); [12:58](https://youtu.be/a_e-oGqrl4g?t=778)).
- **Proxy EMAs:**
  - Weekly 10 EMA, monthly 10 EMA and weekly 200 EMA as his main 2023 tools ([$21,300 by July 1st, 2023-06-13, 01:01](https://youtu.be/D9fc1UjBV64?t=61)).
  - Daily 300 EMA ≈ monthly 10. Daily 70 EMA ≈ weekly 10 ([$21,300 Mid Summer, 2023-06-06, 25:43](https://youtu.be/IQI6y3Fw6Ws?t=1543)). Daily 55 ≈ weekly 10 ([1 SIMPLE TRICK, 2021-11-17, 15:48](https://youtu.be/nDhA8HEJgAg?t=948)).
  - Weekly 200 EMA (orange) and 200 SMA (thin) as the bear buy zone ([ORIGIN LINES, 2021-03-08, 19:00](https://youtu.be/ZnAm-o3b-sk?t=1140)).
- **Daily RSI crossing above its EMA** used as a bullish hint ([ORIGIN LINES, 2021-03-08, 21:36](https://youtu.be/ZnAm-o3b-sk?t=1296)).
- **"Proprietary EMAs" (two clouds, fast and slow) are not disclosed.** "I'm not going to show you what they are". The rules read from them concern price against each cloud, cloud colours, and the clouds against each other ([$42K Target, 2024-06-27, 01:07](https://youtu.be/MGKPt-KbSPQ?t=67)).

### Calibration numbers and falsifiable side-claims (win rates, base rates, "X happens N% of the time")
- **Weekly close below the weekly low** led to more downside 8 of 9 times ("86%") ([$42K Target, 2024-06-27, 05:48](https://youtu.be/MGKPt-KbSPQ?t=348)).
- **Daily RSI at 80** is followed by more daily highs with "only one or two exceptions" in about 14 years ([$47K in November, 2023-11-01, 10:49](https://youtu.be/a_e-oGqrl4g?t=649)).
- **2-day 10/21 cloud** had zero fake-outs at five or six macro tops, and the last five green flips "went crazy" ([Cheat Code & High Probability Guide, 2024-05-10, 04:13](https://youtu.be/eXGNKLdqWk8?t=253); [06:48](https://youtu.be/eXGNKLdqWk8?t=408)).
- **Taking out a punch low below proven support** means acceleration "over 70%" ([CRASH to $60,000, 2024-06-14, 04:37](https://youtu.be/FHxc_hnNKUA?t=277)).
- **A daily close above a key level (52,500)** means "like a 90 likelihood" of going up ([ORIGIN LINES, 2021-03-08, 22:38](https://youtu.be/ZnAm-o3b-sk?t=1358)).
- **14 weeks of weekly 10 EMA respect** at the 2019 bottom against 0 in 2023 ([CRASH To $21,450 By July 1st, 2023-05-22, 02:03](https://youtu.be/8R5bdGlMBb0?t=123)).
- **Origin-line squeeze:** squeezed under the weekly 10 below an origin line, price went to the next line down "every single time ... no exceptions" (one break, May 2021), within about 45 days. It fell 1–3 lines, traversing in 9–40 days, typically about 15 ([$21,300 Mid Summer, 2023-06-06, 09:44](https://youtu.be/IQI6y3Fw6Ws?t=584); [CRASH To $21,450 By July 1st, 2023-05-22, 10:52](https://youtu.be/8R5bdGlMBb0?t=652); [11:23](https://youtu.be/8R5bdGlMBb0?t=683)). This is origin-line geometry, already dead here.
- **Runs and corrections:** a 500–600% run means a 50–55% correction ([ORIGIN-WAVE THEORY, 2021-12-15, 05:09](https://youtu.be/lWSvR3DKRDg?t=309)). Bull-market BTC "corrects for a month at a time" ([ORIGIN LINES, 2021-03-08, 23:39](https://youtu.be/ZnAm-o3b-sk?t=1419)). An average bull pullback is 30% ([The three time frames, 2024-04-11, 10:15](https://youtu.be/Zu_2k_R35iM?t=615)).
- **4H higher lows** take 3–4 days to form ([1 SIMPLE TRICK, 2021-11-17, 08:38](https://youtu.be/nDhA8HEJgAg?t=518)).
- **FOMC / CPI** days move about 10% total, on average 7–9% ([$21,300 by July 1st, 2023-06-13, 04:34](https://youtu.be/D9fc1UjBV64?t=274); [06:34](https://youtu.be/D9fc1UjBV64?t=394)).
- **Seven green monthly candles in a row** (to March 2024) "has never been done before" ([Bitcoin Dominance's effect, 2024-04-12, 05:13](https://youtu.be/Pn09kGwmbgU?t=313)).
- **Self-reported track record:** "rarely do I completely get it wrong where none of the top three price paths" happen ([$21,300 by July 1st, 2023-06-13, 01:31](https://youtu.be/D9fc1UjBV64?t=91)). The 60K target was hit in "9 to 10 days ... it took 10 days" ([How I predicted $60K, 2024-06-25, 01:01](https://youtu.be/A4YzJcwHB1M?t=61)).

### Dated calls (for later scoring)
| Video date | Asset | Direction | Level / target / invalidation | Horizon stated | Citation |
|---|---|---|---|---|---|
| 2021-03-08 | BTC | Up if confirmed | A daily close (not a wick) above 52,500 means up with "like a 90 likelihood", maybe "up to 80 000". Until then it is a no-trade zone. If forced, he would guess down to the lower white line (on screen) | Not stated | [ORIGIN LINES, 2021-03-08, 22:38](https://youtu.be/ZnAm-o3b-sk?t=1358) |
| 2021-03-08 | BTC | Up | 200K–300K "this year" ("close to 50 or higher", "might be an 80 chance"). More likely to hit about 320K than to stop below 200K. A short squeeze could reach 400–500K | 2021 (by about December) | [ORIGIN LINES, 2021-03-08, 28:48](https://youtu.be/ZnAm-o3b-sk?t=1728); [30:51](https://youtu.be/ZnAm-o3b-sk?t=1851) |
| 2021-03-08 | BTC | Down after the top | After the cycle top, about a year of decline, then capitulation to about 80,000. Put buy orders near 80K. Possible second run to 500K after that | About 1 year after the top | [ORIGIN LINES, 2021-03-08, 31:52](https://youtu.be/ZnAm-o3b-sk?t=1912); [32:22](https://youtu.be/ZnAm-o3b-sk?t=1942) |
| 2021-03-08 | BEPRO | Up after a squeeze | About a 20% chance it gets "sandwiched" against its yellow trend line then skyrockets; a scare-down first is almost as likely | Not stated | [ORIGIN LINES, 2021-03-08, 08:48](https://youtu.be/ZnAm-o3b-sk?t=528) |
| 2021-12-15 | BTC | Sideways, then up | Ranges about 30K–60K with "not much upside until september 2022", breaking out around September 2022. "30k really might be in the picture sooner than later" | To Sep 2022 | [ORIGIN-WAVE THEORY, 2021-12-15, 00:00](https://youtu.be/lWSvR3DKRDg?t=0); [08:14](https://youtu.be/lWSvR3DKRDg?t=494); [10:48](https://youtu.be/lWSvR3DKRDg?t=648) |
| 2021-12-15 | BTC | Up | Fractal paths: about 350–400K in Q1/Q2 2023; aggressive case about 1.1M "in just under two years" | Q1–Q2 2023 / by about Dec 2023 | [ORIGIN-WAVE THEORY, 2021-12-15, 09:17](https://youtu.be/lWSvR3DKRDg?t=557); [10:17](https://youtu.be/lWSvR3DKRDg?t=617) |
| 2022-12-02 | BTC | Up (rally) | A "Santa Claus rally" "would easily get Bitcoin to 19k" before the Dec 13–15 data. The weekly 10 EMA at about 18,000 is "a pretty good target". Title target $18,309 (origin line). The micro line on screen is the bare minimum | By Dec 13–15, 2022 | [$18,309 Origin Line Target, 2022-12-02, 01:31](https://youtu.be/wnumqaYJCeU?t=91); [13:22](https://youtu.be/wnumqaYJCeU?t=802); [21:33](https://youtu.be/wnumqaYJCeU?t=1293) |
| 2022-12-02 | BTC | Bottom | 16,400 restated as his "absolute favorite number for a bottom" (made before FTX) | Cycle bottom | [$18,309 Origin Line Target, 2022-12-02, 06:38](https://youtu.be/wnumqaYJCeU?t=398) |
| 2023-05-22 | BTC | Down | The next origin line down will be hit: "at least 21 500 or lower" (20,500 if later). A downtrend of about a month (7–38 days) | Before or around July 1, 2023 | [CRASH To $21,450 By July 1st, 2023-05-22, 12:25](https://youtu.be/8R5bdGlMBb0?t=745); [12:56](https://youtu.be/8R5bdGlMBb0?t=776); [20:36](https://youtu.be/8R5bdGlMBb0?t=1236) |
| 2023-06-06 | BTC | Down | After 25,400 was hit: 21,300 by July 1, and BTC "will go below 23k". If it waits until August 1, still about 22K; zone 21–23K | Jul 1 (latest about Aug 1), 2023 | [$21,300 Mid Summer, 2023-06-06, 00:30](https://youtu.be/IQI6y3Fw6Ws?t=30); [02:31](https://youtu.be/IQI6y3Fw6Ws?t=151); [23:07](https://youtu.be/IQI6y3Fw6Ws?t=1387) |
| 2023-06-06 | BTC | Paths | Most likely a descending triangle under the weekly and monthly 10 EMAs. Least likely a 10–12% bounce to about 28K that is "still bearish". A loss of the daily 300 EMA with strong stocks could mean new lows (more likely if US500 < 4,181). The decision is due by the weekend of June 16–18 | By Jun 16–18, 2023 | [$21,300 Mid Summer, 2023-06-06, 12:21](https://youtu.be/IQI6y3Fw6Ws?t=741); [13:52](https://youtu.be/IQI6y3Fw6Ws?t=832); [21:04](https://youtu.be/IQI6y3Fw6Ws?t=1264); [24:42](https://youtu.be/IQI6y3Fw6Ws?t=1482) |
| 2023-06-13 | BTC | Down | Triangle confirmed. FOMC could spike price to 28,500 and it would "still be bearish". The next origin line down must be hit | Around Jul 1, 2023 | [$21,300 by July 1st, 2023-06-13, 08:05](https://youtu.be/D9fc1UjBV64?t=485); [09:06](https://youtu.be/D9fc1UjBV64?t=546) |
| 2023-10-25 | BTC | Up | Above 29,100 and holding it, BTC is "51% or more" likely to go "a lot closer to 40K". The next small target is about 37K (quarter line). If 5m higher lows break, a dip to the 33,333 punch low comes first | Weeks (not stated) | [$40K Could be Too Low, 2023-10-25, 08:48](https://youtu.be/pcXnQM_-wzE?t=528); [09:19](https://youtu.be/pcXnQM_-wzE?t=559); [11:51](https://youtu.be/pcXnQM_-wzE?t=711); [19:13](https://youtu.be/pcXnQM_-wzE?t=1153) |
| 2023-11-01 | BTC | Up, then correction | 47K could be "the next monthly target" in November (or December) 2023. If hit, expect about a 30% correction and sideways. 41K is "probable to be hit before this move peters out" (RSI 80 rule) | Nov–Dec 2023 | [$47K in November, 2023-11-01, 00:01](https://youtu.be/a_e-oGqrl4g?t=1); [14:00](https://youtu.be/a_e-oGqrl4g?t=840) |
| 2023-11-01 | BTC | Cycle top | If the next mega-channel line acts as resistance all cycle: about 170–180K at a Nov–Dec 2025 top | Nov–Dec 2025 | [$47K in November, 2023-11-01, 06:33](https://youtu.be/a_e-oGqrl4g?t=393) |
| 2024-04-11 | BTC | Up if the month flips green | No violent upside while April is red. A move above about 73K flips the month green and could go "straight to $105,000", skipping 85K. A 4H close below 68,389 makes 60K much likelier (from <20%). Possible later scenario: 100K, then a 30% pullback to the 70K line (midsummer 2024) | 24–48 h trigger; April 2024; summer 2024 | [The three time frames, 2024-04-11, 10:30](https://youtu.be/Zu_2k_R35iM?t=630); [11:01](https://youtu.be/Zu_2k_R35iM?t=661); [16:38](https://youtu.be/Zu_2k_R35iM?t=998); [09:46](https://youtu.be/Zu_2k_R35iM?t=586) |
| 2024-04-12 | BTC / BTC.D | Two paths | Above the 71,500 monthly open means a "hot month" (an 8th green month). Otherwise a doji, then lower-high watch on the 4H; below about 67,000 (heard "6,700 00") means about 61,000, with a red May and dominance spiking about 5% | Next 45 days | [Bitcoin Dominance's effect, 2024-04-12, 07:15](https://youtu.be/Pn09kGwmbgU?t=435); [08:48](https://youtu.be/Pn09kGwmbgU?t=528); [10:20](https://youtu.be/Pn09kGwmbgU?t=620) |
| 2024-05-10 | BTC | Down until a flip | The 2-day 10/21 cloud just flipped red near 63K. Assume down until 2-day closes above it. A green flip means, at minimum, taking out the prior high (about 73K) | Not stated | [Cheat Code & High Probability Guide, 2024-05-10, 04:43](https://youtu.be/eXGNKLdqWk8?t=283); [10:23](https://youtu.be/eXGNKLdqWk8?t=623) |
| 2024-06-14 | BTC | Down | "just over 50%" chance of spiking to the low 60s, taking about 10 days. After that, a likely retest toward 70K | About 10 days (to about Jun 24, 2024) | [CRASH to $60,000, 2024-06-14, 00:01](https://youtu.be/FHxc_hnNKUA?t=1); [07:11](https://youtu.be/FHxc_hnNKUA?t=431); [07:42](https://youtu.be/FHxc_hnNKUA?t=462) |
| 2024-06-25 | BTC | Sideways or up | Claims 60K was hit. Expect a triangle at 60,000–62,500 and about 9 days of boredom. "slightly more probable" sideways or up, the most likely path about +7% | About 9–14 days | [How I predicted $60K, 2024-06-25, 01:01](https://youtu.be/A4YzJcwHB1M?t=61); [05:04](https://youtu.be/A4YzJcwHB1M?t=304); [07:10](https://youtu.be/A4YzJcwHB1M?t=430) |
| 2024-06-27 | BTC | Down (conditional) | If price fails the bottom of the slow EMA cloud: a big move to 42,500–52,000 ("not convinced it's the most probable"). Otherwise a bounce to 73K (triple top) or new all-time highs | Next 10 days (title) | [$42K Target, 2024-06-27, 10:27](https://youtu.be/MGKPt-KbSPQ?t=627); [12:30](https://youtu.be/MGKPt-KbSPQ?t=750) |
| 2024-11-19 | BTC | Up, then pullback | Passes 100K within 1–2 weeks then pulls back. A mega-line hit would top at about 95K. "I do not see Bitcoin going much above 103k before a pullback" (failure pattern about 105K). Pullback to the previous daily higher low, about 70,300 (about −30%), possibly over about 3 months | 1–2 weeks; pullback up to about 3 months | [$500K realistic target in 2025, 2024-11-19, 11:49](https://youtu.be/sj_Rj19PrbY?t=709); [12:20](https://youtu.be/sj_Rj19PrbY?t=740); [12:50](https://youtu.be/sj_Rj19PrbY?t=770); [14:22](https://youtu.be/sj_Rj19PrbY?t=862) |
| 2024-11-19 | BTC | Cycle top | Around the end of November 2025: about 200K "bare minimum". In-between cases: 250K (failure-rejection, up to 300K), or a 200-day double top. 500K if it touches the half-court line late in the 4-year cycle | End of Nov 2025 | [$500K realistic target in 2025, 2024-11-19, 06:11](https://youtu.be/sj_Rj19PrbY?t=371); [07:44](https://youtu.be/sj_Rj19PrbY?t=464); [13:21](https://youtu.be/sj_Rj19PrbY?t=801) |
| 2024-11-19 | Altcoins | Down (conditional) | If BTC corrects 30%: top-50 alts −35–40%, small caps up to about 2× BTC's drop (50–60%) | Same pullback window | [$500K realistic target in 2025, 2024-11-19, 14:52](https://youtu.be/sj_Rj19PrbY?t=892) |

Videos with no concrete forward call: 1 SIMPLE TRICK (2021-11-17) and TA ≠ Trading (2022-07-28).

### Candidate rules NOT in the pre-registered setup grader
1. **Weekly close below the last confirmed weekly body low means bearish continuation.** Claimed 8 of 9 (86%). Test the forward 4/8/13-week return and the probability of a lower low against base rate. Computable on weekly BTC. The grader uses the weekly trend as context but never tests the break event or the claim ([$42K Target, 2024-06-27, 05:48](https://youtu.be/MGKPt-KbSPQ?t=348)).
2. **2-day EMA10/EMA21 cross state as a top marker and re-entry.** The first bearish cross after a ≥ X% run marks the macro top: measure the max price after the cross against the price at the cross, and count fake-outs. The bullish cross implies taking out the prior swing high. Also test the 1D 20/42 equivalent. Computable ([Cheat Code & High Probability Guide, 2024-05-10, 04:13](https://youtu.be/eXGNKLdqWk8?t=253); [08:21](https://youtu.be/eXGNKLdqWk8?t=501)).
3. **Daily RSI ≥ 80 means continuation.** After a daily RSI(n) close ≥ 80, count the further daily higher highs (claim ≥ 4) and the 10/20-day forward return against base rate. The RSI length is not stated: test 14, plus tutorial 1's 22. Computable; the terminal has RSI ([$47K in November, 2023-11-01, 10:17](https://youtu.be/a_e-oGqrl4g?t=617)).
4. **Punch-high / punch-low continuation.** Take a level with at least 3 prior touches. A candle closes beyond it (wicks excluded). The retest fails. Then a close beyond the "punch" extreme. Measure continuation probability (claim > 70%) and move size. Computable with pivots on 1H/4H/daily ([CRASH to $60,000, 2024-06-14, 04:37](https://youtu.be/FHxc_hnNKUA?t=277); [$40K Could be Too Low, 2023-10-25, 18:12](https://youtu.be/pcXnQM_-wzE?t=1092)).
5. **Daily EMA 10/21/55 dip test.** When the daily EMA10 falls to within x% of EMA21 (or touches it), compute s = (EMA21 − EMA55)/EMA55. A higher s means a better 10–20 day forward return (buy the dip), and a low s means a dump. Add the sub-rule that daily closes back above EMA10 before the touch mean a bullish outcome. Computable ([1 SIMPLE TRICK, 2021-11-17, 03:03](https://youtu.be/nDhA8HEJgAg?t=183); [07:37](https://youtu.be/nDhA8HEJgAg?t=457)).
6. **Weekly 10 EMA respect count before a bull.** After a bear low, count consecutive weekly closes at or above weekly EMA10 before the breakout rally. Claim: a sustainable bull needs several (14 in 2019) and a 0-week respect fails. Computable ([CRASH To $21,450 By July 1st, 2023-05-22, 02:03](https://youtu.be/8R5bdGlMBb0?t=123)).
7. **Weekly 10 EMA reclaim filter.** Treat a cross back above the weekly EMA10 as valid only after ≥ 3–4 consecutive daily closes above it. Otherwise it is a "failure pattern above". Compare forward returns. Computable ([$21,300 by July 1st, 2023-06-13, 09:39](https://youtu.be/D9fc1UjBV64?t=579)).
8. **Weekly 10 EMA separation magnet.** When |close − EMA10w| / EMA10w exceeds its k-th percentile, P(touching EMA10w within N weeks) is elevated. The bear-market reactions split into shy, touch and failure-above. Computable ([$18,309 Origin Line Target, 2022-12-02, 10:14](https://youtu.be/wnumqaYJCeU?t=614)).
9. **Three drives down plus RSI higher lows on the 4H** as the only counter-trend buy. Three lower swing lows in price, each with a higher RSI low. Compare against buying the first 4H higher low 3–4 days later. Computable with RSI ([1 SIMPLE TRICK, 2021-11-17, 11:11](https://youtu.be/nDhA8HEJgAg?t=671)).
10. **Bear-market 4H cross short gated by the daily 10 > 55 EMA.** Short the 4H EMA10/21 bearish cross only while the daily EMA10 > EMA55 and the daily trend is down. Computable ([1 SIMPLE TRICK, 2021-11-17, 19:58](https://youtu.be/nDhA8HEJgAg?t=1198)).
11. **Monthly-open gate.** No upside breakout entries while price is below the current month's open. Measure breakout follow-through split by month colour. Computable ([The three time frames, 2024-04-11, 07:52](https://youtu.be/Zu_2k_R35iM?t=472)).
12. **Bitcoin dominance regime for altcoin exposure.** If dominance is rising and BTC corrects, alt drawdown ≈ 1.2–2 × BTC's, growing with smaller market cap. Needs data we do not have: dominance and cap-rank history ([$500K realistic target in 2025, 2024-11-19, 14:52](https://youtu.be/sj_Rj19PrbY?t=892)).
13. **FOMC/CPI-day range about 10% total** (claim; average 7–9%). Needs an economic-calendar dataset we do not have ([$21,300 by July 1st, 2023-06-13, 04:34](https://youtu.be/D9fc1UjBV64?t=274)).

Already dead and not proposed: origin lines, the "speed limit", origin-line squeezes, the super macro mega channel and pace line, and channels.

### Not usable / vague
- **Origin lines, "speed limits", the mega channel and the "pace line".** Hand-drawn, re-fitted lines ("I have adjusted this one up a tiny bit"), already tested dead. Their "no exceptions" claims are retrospective ([ORIGIN LINES, 2021-03-08, 13:24](https://youtu.be/ZnAm-o3b-sk?t=804); [ORIGIN-WAVE THEORY, 2021-12-15, 00:33](https://youtu.be/lWSvR3DKRDg?t=33)).
- **"Proprietary EMAs" clouds:** the lengths are not disclosed ([$42K Target, 2024-06-27, 01:07](https://youtu.be/MGKPt-KbSPQ?t=67)).
- **Price "permission", "big money / BlackRock algorithms", exchanges faking futures counter-orders, "mosquitoes":** motive stories, not measurable ([$21,300 by July 1st, 2023-06-13, 11:11](https://youtu.be/D9fc1UjBV64?t=671)).
- **Yellow-triangle "holding pattern" and "price may ignore trend":** no definition beyond a drawing ([$21,300 Mid Summer, 2023-06-06, 04:34](https://youtu.be/IQI6y3Fw6Ws?t=274)).
- **45° projections, "lower high within a lower high" pennants, fractal copy-paste of past runs:** depend on chart scaling and selection ([How I predicted $60K, 2024-06-25, 04:03](https://youtu.be/A4YzJcwHB1M?t=243); [ORIGIN-WAVE THEORY, 2021-12-15, 08:46](https://youtu.be/lWSvR3DKRDg?t=526)).
- **Heat-map alignment and "which timeframe the algos use right now":** no data, and he changes it at will ([$18,309 Origin Line Target, 2022-12-02, 21:03](https://youtu.be/wnumqaYJCeU?t=1263); [The three time frames, 2024-04-11, 15:06](https://youtu.be/Zu_2k_R35iM?t=906)).
- **Top-three "price paths":** stated loosely enough that some path usually matches ([$21,300 by July 1st, 2023-06-13, 01:31](https://youtu.be/D9fc1UjBV64?t=91)).
