# DSS Bressert material (LuxAlgo, TC2000, StrategyQuant, TradingView RSI3M3+): extracted rules

Source slug `bressert-dss`, school **cycle**. **4 public web pages read in full**, all retrieved 2026-10-18. None carries a usable publish date (the StrategyQuant post is dated 31 Jan 2022 on the page; the TradingView script was published 30 Mar 2025 and updated 12 Apr 2025). Kind of material: indicator reference pages and a script description (tool tutorials). Public.

**These are secondary descriptions of Walter Bressert's method, not his own text.** No page quotes Bressert's books or manuals, and none cites a primary source. Three pages are platform documentation for an indicator that carries his name. The fourth is a TradingView script author's summary of "Bressert's five trading rules". Its phrasing ("Let me break it down and explain how it works") suggests a generated or paraphrased write-up, so it is third-hand at best. Attribution is disputed on the pages themselves (see below). Every rule is attributed to the page that states it. The pages are not blended into one composite method.

| Page | URL | What it gives |
|---|---|---|
| LuxAlgo Library, "DSS Bressert" concept page | https://www.luxalgo.com/library/concept/dss-bressert/ | Formula, typical lengths, 80/20 hooks, how a hook is used inside a cycle framework |
| TC2000 Help, "Double Smoothed Stochastic Bressert" (PCF) | https://help.tc2000.com/m/69445/l/755063-double-smoothed-stochastic-bressert | Exact PCF formula with defaults 14 / 5 / 1, an EMA 5 signal plot, 70/30 lines |
| StrategyQuant codebase, "Double Smoothed Stochastic Bressert" | https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/ | Attribution, 80/20 levels, trend behaviour claim, cross-up/cross-down conditions (code is in a zip that was not read) |
| TradingView, "RSI3M3+ v.1.8" by kromut76 | https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/ | "Bressert's Five Trading Rules" with RSI3M3 (not a DSS), 40/60 buy and sell lines, timing bands, entry and stop |

Site furniture (menus, comment threads, risk disclaimers, other indicator lists) was ignored.

## Method in its own terms

As these pages describe it, Bressert's method is cycle-first. Markets move in **trading cycles** with a measurable low-to-low length. A **timing band** is the window where the next cyclical low (or high) is due. An oscillator only **times** the entry inside that window; it is never the thesis. LuxAlgo says an oversold DSS hook "is only taken when a dominant cycle low is due and the higher timeframe points up". The RSI3M3+ page gives five concrete rules: (1) the low falls 15 to 30 market days after the previous trading-cycle bottom; (2) a trading-cycle high came first, with the oscillator above 60; (3) the oscillator drops below 40 and turns up; (4) you buy on a rise above the high of the upturn day; (5) the stop goes slightly under the trading-cycle low. The DSS (double-smoothed stochastic) is the oscillator most associated with his name. It is a stochastic of an EMA-smoothed stochastic, smoothed again, read for hooks out of its 80/20 (or 70/30) extremes. The pages disagree on its exact lengths.

## DSS definitions found (exact)

### 1. LuxAlgo (one EMA length, one stochastic length, used twice)

The page gives it verbatim as "one widely used formulation":

```
raw     = 100 * (close - lowest(low, n)) / (highest(high, n) - lowest(low, n))
s1      = EMA(raw, m)
dss_raw = 100 * (s1 - lowest(s1, n)) / (highest(s1, n) - lowest(s1, n))
DSS     = EMA(dss_raw, m)
```

- The first stochastic is a **raw (fast) %K of price**. The second is a stochastic of **the smoothed line s1 against its own n-bar highest and lowest**, not price's.
- Both stochastic passes use the **same n**. Both smoothings are **EMAs of the same m**.
- "n: stochastic lookback period, commonly around 10 to 21". "m: EMA smoothing period, commonly around 3 to 9". "There is no single authoritative parameter set." Traders "tune them to the cycle length they are trading".
- Variants named: separate lookbacks for the two passes, SMA smoothing instead of EMA, and an optional signal line ("often an EMA of the DSS or a lagged copy"). Its lengths are not given.
- Levels: hooks "beyond 80 or below 20".
- It says William Blau's double-smoothed stochastic "smooths numerator and denominator separately before dividing, which is a distinct construction".
- [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)

### 2. TC2000 (stochastic 14 both passes, EMA 5 both passes, then an optional EMA 1)

The PCF formula, verbatim:

```
100 * XAVG(XAVG((XAVG(STOC#StocPeriod, #kPeriod) - MIN(XAVG(STOC#StocPeriod, #kPeriod), #StocPeriod)) / (MAX(XAVG(STOC#StocPeriod, #kPeriod), #StocPeriod) - MIN(XAVG(STOC#StocPeriod, #kPeriod), #StocPeriod)), #kPeriod), #dPeriod)
```

with `#StocPeriod = 14`, `#kPeriod = 5`, `#dPeriod = 1`. XAVG is TC2000's exponential average.

- Read literally, this is the same structure as LuxAlgo's, with n = 14 and m = 5. The outer `XAVG(…, 1)` is an EMA of length 1, so it does nothing at the default ("Set to 1 if you want the %k"). Setting it above 1 adds a **third EMA**, which the LuxAlgo formula does not have.
- The prose says: "The calculations start with a normal raw stochastic. This stochastic is smoothed by an exponential moving averages before another raw stochastic is taken of the result. Then a second exponential moving average of the same period is applied to this second stochastic."
- Caveat: `STOC14` is TC2000's built-in stochastic with only the period given. The page does not say whether that built-in applies any %K smoothing of its own. Read here as raw %K, per the prose ("a normal raw stochastic"). Not verified against TC2000.
- The signal line is added by hand as a **5-period EMA** of the DSS ("Set EMA period to 5").
- **Levels: 70 / 30.** "30 considered oversold and 70 considered overbought". The page draws a green line at 70 and a red line at 30.
- Attribution: "developed by Walter Bressert" (no mention of Blau).
- [TC2000 DSS Bressert](https://help.tc2000.com/m/69445/l/755063-double-smoothed-stochastic-bressert)

### 3. StrategyQuant (no formula or lengths on the page)

- It says only that the calculation "is similar to the stochastic indicator", with "the use of double exponential smoothing". The lengths are in a downloadable zip (DSS_25092022.zip) that was **not read**.
- **Levels: 80 / 20.** "values above 80 indicate an overbought condition of the market, values below 20 indicate an oversold condition".
- Attribution: "introduced by William Blau and Walter Bressert shortly after each other in two slightly different versions".
- [StrategyQuant DSS Bressert](https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/)

### 4. TradingView RSI3M3+ (not a DSS)

- The "Bressert" oscillator on this page is **RSI3M3**: a 3-period RSI ("sRSI") smoothed by a 3-period moving average ("sMA"). The MA type is not stated.
- Lines: oversold 30, overbought 70 (signal-generation levels), **buy line 40, sell line 60** (the levels in "Bressert's five rules").
- [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

### Against the terminal and Camel Finance settings

The terminal's DSS Bressert (`AccessibleTrader.Core/Services/Indicators/DssBressertProvider.cs`) computes exactly the LuxAlgo structure: stochastic P, EMA E, second stochastic of the EMA line over P, EMA E, plus a trigger EMA T. Its shipped default is 13 / 8 / 5. The 10 / 9 / 5 setting (Camel Finance's, read from a screen frame, see `camel-finance.md`) is a parameter change. Levels are 80/20.

| Setting | LuxAlgo | TC2000 | StrategyQuant | RSI3M3+ | Terminal / Camel (10, 9, 5, 80/20) |
|---|---|---|---|---|---|
| Structure: stoch → EMA → stoch of EMA line → EMA | yes | yes (plus an optional third EMA, off at 1) | "double exponential smoothing", no formula | different indicator (RSI 3, MA 3) | yes |
| Stochastic length | 10 to 21 typical; 10 **agrees** | 14 (10 is a change) | not on the page | n/a | 10 |
| EMA length | 3 to 9 typical; 9 **agrees** (top of range) | 5 (9 is a change) | not on the page | n/a | 9 |
| Same length in both passes | yes | yes | not stated | n/a | yes |
| Signal line | optional, "often an EMA of the DSS", no length | EMA 5 **agrees** | not stated | the 3-period MA of RSI3 | EMA 5 trigger |
| Levels | 80 / 20 **agrees** | **70 / 30 disagrees** | 80 / 20 **agrees** | 30/70 signal, 40/60 Bressert rules **disagree** | 80 / 20 |

So no page contradicts (10, 9, 5) outright. LuxAlgo's ranges contain it. TC2000's defaults (14, 5, 1 + EMA 5) differ in both lengths. The levels split: 80/20 (LuxAlgo, StrategyQuant) against 70/30 (TC2000). The only page that states Bressert's *rules* uses 40/60 on an RSI3M3, not 20/80 on a DSS.

## Context / higher timeframe / regime

- **The oscillator is not the thesis.** In Bressert's framework the DSS "times entries inside a separately identified cycle and trend context". Used alone "it inherits every weakness of bounded oscillators in trends". [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Higher timeframe must point up for a long.** An oversold hook "is only taken when a dominant cycle low is due and the higher timeframe points up". [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Trade with the larger cycle.** "Enter in the direction of the larger cycle when smaller and larger cycles align". Confirm signals with the same indicator on a higher timeframe. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Regime fit.** It "works well in trending markets with clear cyclical behavior" and is "less effective in choppy, non-trending markets". [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Trend behaviour (two pages differ).** LuxAlgo: "in strong trends the DSS pins at an extreme like any stochastic, and counter-trend hooks fail repeatedly until the trend regime breaks". StrategyQuant: "the extreme zones at the other end of the scale are reached quite frequently, even in strong trends, resulting in many trend conforming signals". Both can hold at once (pinned at the trend's extreme, with brief visits to the other one), but they are different testable claims. [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/), [StrategyQuant DSS Bressert](https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/)

## Location (levels, zones, where a setup counts)

- **Location is in time, not price.** A setup counts inside the **timing band**: the projected window for the next cycle low. "Signals occurring within these bands have higher probability of success". "Signals outside these bands may be less reliable". [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Bressert rule 1 (cycle timing).** "The low must be 15-30 market days from the previous Trading Cycle bottom". The script's defaults are minimum/maximum cycle length 15 to 30 days and a cycle-detection lookback of 30. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **How the script finds cycle points.** A bottom is where the RSI is below the buy line (40) and starting to turn up. A top is where the RSI is above the sell line (60) and starting to turn down. These points are recorded to measure cycle lengths, and the timing band is projected from them. The projection formula is not given. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Price levels** are an optional extra only ("Combine with support/resistance levels"). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

## Entry triggers

- **Bressert rules 2 to 4 (RSI3M3+ page).**
  - Rule 2, prior top: "A Trading Cycle high must have occurred with the oscillator above 60".
  - Rule 3, oscillator: "The oscillator must drop below 40 and turn up".
  - Rule 4, entry: "Entry is triggered by a rise above the price high of the upturn day". This is a buy-stop at the high of the bar where the oscillator turned up, not a buy at the close.
  - The page's buy procedure repeats it: wait for RSI below 40, look for the upturn, "Enter the trade when price rises above the high of the upturn day".
  - Ambiguity: the page does not say whether "the RSI"/"the oscillator" is the green 3-period RSI or the red 3-period MA of it.
  - [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **The script's own arrows use other levels.** "Buy signals occur when the RSI turns up from below the oversold level (30)", "Enhanced by Bressert's specific timing rules". So the page has two buy thresholds: 30 for the arrow and 40 for the Bressert rule. It does not reconcile them. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **DSS hook (LuxAlgo).** "traders act on hooks, where the DSS turns back from beyond 80 or below 20, treating them as cleaner than raw stochastic crossings". A long hook is the DSS turning up while below 20. [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Signal-line crossover (LuxAlgo).** Variants with a companion line "use crossovers as entries, read like a slow stochastic but with the double-smoothed base". [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Cross up / cross down (StrategyQuant).** The listed strategy conditions are "DSS is crosses UP/Down" and "DPO is rising/falling". What the DSS crosses (a level or a signal line) and what "DPO" is are not explained on the page. [StrategyQuant DSS Bressert](https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/)

## Confirmations (things that raise conviction)

- **Inside the timing band** (see Location). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Higher-timeframe agreement**: the same indicator on the higher timeframe, the larger cycle in the same direction. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Divergence.** RSI3M3+: price makes a new low while the RSI makes a higher low (bullish), and the mirror for bearish. LuxAlgo: the DSS's smoothed swings "make regular divergences at extremes easier to see". [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/), [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Optional extras** with no thresholds: candlestick patterns and "volume confirmation of cycle turns". [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

## Vetoes (things that cancel a trade whatever else says)

- **No prior cycle high with the oscillator above 60 → no long** (Bressert rule 2 is a precondition). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Outside 15 to 30 market days from the last trading-cycle low → not a valid Bressert low** (rule 1). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **No hook without a due cycle low and an up higher timeframe** (LuxAlgo's condition for taking an oversold hook). [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Strong trend against the hook**: counter-trend hooks "fail repeatedly until the trend regime breaks". This is a caution, with no test given for "regime breaks". [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)

## Stops and invalidation

- **Bressert rule 5.** "Place stop slightly below the Trading Cycle low". The script implements this "as 99% of bottom price", i.e. stop = 0.99 × the cycle low. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

## Exits and targets

- **Sell signal.** Wait for the RSI to rise above the sell line (60), then look for a downturn from above it. "Consider exiting or taking profits when a sell signal appears". [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Alternative exit.** "When price moves below the low of the downturn day". This mirrors the entry rule. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **The script's sell arrow.** The RSI turns down from above the overbought level (70). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Time-based partial.** "Consider taking partial profits at the projected cycle high timing". The page does not say how the cycle-high timing is projected. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

## Position sizing and risk

Nothing stated beyond the stop.

## Timeframes

- Daily is best ("for intermediate-term trading"). Weekly for longer-term position trading. On intraday charts the cycle lengths must be adjusted. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- Bressert's rule 1 is stated in **market days** (15 to 30), so the five rules as given are daily-bar rules. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

## Macro context

Nothing stated.

## Indicator settings stated (exact numbers: lengths, thresholds, colours meaning)

- **LuxAlgo DSS:** n "around 10 to 21", m (EMA) "around 3 to 9", both reused for the second pass. Levels 80/20. Optional signal line with no length. [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **TC2000 DSS:** StocPeriod 14, kPeriod (EMA) 5, dPeriod 1 (outer EMA; 1 = off). Added signal plot EMA 5. Levels 70 (green line) / 30 (red line). Scale 0 to 100. [TC2000 DSS Bressert](https://help.tc2000.com/m/69445/l/755063-double-smoothed-stochastic-bressert)
- **StrategyQuant DSS:** levels 80/20. Lengths not on the page. Implemented for MT4/MT5/TradeStation/MultiCharts. [StrategyQuant DSS Bressert](https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/)
- **RSI3M3+:**
  - RSI 3, smoothed by a 3-period MA (type unstated).
  - Required bars 20. Overbought 70, oversold 30. Buy line 40, sell line 60.
  - Cycle detection length 30. Min/max cycle length 15 to 30 days. Stop 0.99 × cycle low.
  - Colours: green line = 3-period RSI, red line = its 3-period MA. Horizontal bands at 30/70, dotted lines at 40/60. Yellow vertical bands = projected timing windows for the next cycle bottom. Green up arrows = buys, red down arrows = sells.
  - [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

## Calibration numbers and falsifiable side-claims

- **DSS shape.** It "tends to travel quickly between its extremes and pause there", with "less lag than a heavily smoothed slow stochastic". Testable against a slow stochastic: time from extreme to extreme, dwell at the extremes, and lag of turns relative to price pivots. [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Hooks are cleaner than raw-stochastic crossings** (fewer false turns). [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Extremes are reached "quite frequently, even in strong trends"** (StrategyQuant) against **"pins at an extreme… counter-trend hooks fail repeatedly"** (LuxAlgo). Measurable: the fraction of trend bars (by any trend definition) on which the DSS visits the opposite extreme, and the hit rate of counter-trend hooks. [StrategyQuant DSS Bressert](https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/), [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Timing-band signals have "higher probability of success"** than out-of-band signals. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Trading cycles run 15 to 30 market days** low to low (on daily bars). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

## Dated calls (for later scoring)

None. These are reference pages with no market calls.

| Video date | Asset | Direction | Level / target / invalidation | Horizon stated | Citation |
|---|---|---|---|---|---|
| — | — | — | — | — | — |

## Candidate rules NOT in the pre-registered setup grader

The grader does not use DSS Bressert or RSI3M3 at all. "Cycles as a low detector" is dead in this repo. Candidates 1 and 4 use the cycle count only as a *filter* on an oscillator trigger, which is the narrower claim these pages make. They should be pre-registered as such, with an exposure-matched random-entry control.

1. **Bressert five-rule long (RSI3M3+ page), daily bars.** Let L0 be the last confirmed trading-cycle low. All of these must hold:
   - (a) days since L0 in [15, 30];
   - (b) between L0 and now, the oscillator printed > 60 (the trading-cycle high);
   - (c) osc[t−1] < 40 and osc[t] > osc[t−1] (the upturn day = t);
   - (d) entry by buy-stop at high[t], valid for k bars (k not stated; pre-register k = 3);
   - (e) stop = 0.99 × the lowest low since the oscillator went below 40.

   Exit on the first of: osc > 60 then turns down and price trades below that day's low; or the stop. Oscillator = RSI(3) smoothed by SMA(3) (the MA type is assumed). The terminal has RSI and SMA. A second variant swaps in the terminal's DSS Bressert, as LuxAlgo says Bressert used DSS oversold hooks. Report it separately, because the 40/60 levels were stated for the RSI3M3, not the DSS. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
2. **"Upturn-day high" entry versus close entry.** For any oscillator hook (DSS below 20 turning up, or RSI3M3 below 40 turning up), compare entering at the close of the hook bar against a buy-stop at its high (filled only if hit within k bars). This tests whether Bressert's price-confirmation step removes losers or only adds slippage. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
3. **DSS hook versus DSS/trigger cross.** Long hook: DSS[t−1] < 20 and DSS[t] > DSS[t−1]. Compare it with the trigger cross (DSS crosses above EMA(DSS, 5) while below 20) and with a plain Stochastic(14,3) cross below 20. Test LuxAlgo's claim that hooks are "cleaner than raw stochastic crossings". Run each at (10,9,5), (13,8,5) and (14,5) with EMA-5 trigger. Uses the terminal's DSS Bressert and Stochastic. [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/), [TC2000 DSS Bressert](https://help.tc2000.com/m/69445/l/755063-double-smoothed-stochastic-bressert)
4. **Timing-band filter.** Estimate the cycle length as the mean of the last N low-to-low spans between oscillator bottoms (osc < 40 turning up), bounded to [15, 30]. The band is that mean ± a tolerance (the tolerance is not stated; pre-register ±20%). Compare the forward returns of in-band against out-of-band hooks. This tests "signals occurring within these bands have higher probability of success" (a filter claim, not a low detector). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
5. **Higher-timeframe gate.** Take a daily DSS oversold hook only when the weekly DSS is rising (weekly DSS[t] > DSS[t−1]), or its trigger cross is bullish. This uses "the higher timeframe points up" and "confirm signals with the same indicator on higher timeframes". [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/), [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
6. **Level set 80/20 versus 70/30.** Keep the hook rule from candidate 3 and vary only the threshold: 20 (LuxAlgo, StrategyQuant, terminal) against 30 (TC2000). Report the signal count and the forward-return distribution for each. [TC2000 DSS Bressert](https://help.tc2000.com/m/69445/l/755063-double-smoothed-stochastic-bressert), [StrategyQuant DSS Bressert](https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/)
7. **Trend-pinning measurement (descriptive, not a strategy).** Define trend as the close above and rising SMA(50). Within trends, measure how often the DSS reaches the opposite extreme (below 20 in an uptrend), and the hit rate of the resulting with-trend hooks against counter-trend hooks. This settles the LuxAlgo and StrategyQuant disagreement. [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/), [StrategyQuant DSS Bressert](https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/)
8. **Bressert mirror exit.** Exit a long when the oscillator has been above 60, turns down, and price trades below the low of that downturn day. Compare against the grader's existing exits (ATR ladder, signal exit, 20-bar time exit). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

## Not usable / vague

- **"Dominant cycle low is due"** (LuxAlgo): no definition of the dominant cycle or of "due". It needs a cycle-length estimate (see candidate 4) that the page does not give.
- **Timing-band projection** (RSI3M3+): no formula or tolerance is given. The "projected cycle high timing" for partial profits is undefined.
- **"Slightly below" the cycle low**: only the script's 99% makes it computable. This is the script author's choice, not stated as Bressert's.
- **StrategyQuant's "DSS crosses UP/Down" and "DPO is rising/falling"**: what is crossed and what DPO means are not stated on the page. The code zip was not read.
- **"Higher volatility markets may need wider overbought/oversold levels"** (RSI3M3+): no rule for how much wider.
- **Attribution.** TC2000 says Bressert developed it. StrategyQuant says Blau and Bressert, "shortly after each other in two slightly different versions". LuxAlgo says attribution "is best kept loose". None of the three is a primary source.
