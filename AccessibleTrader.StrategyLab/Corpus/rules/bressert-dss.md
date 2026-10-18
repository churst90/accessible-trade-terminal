# DSS Bressert and Walter Bressert cycle material: extracted rules

Source slug `bressert-dss`, school **cycle**. **11 sources read in full**:
- **4 public web pages**, all retrieved 2026-10-18. None carries a usable publish date: the StrategyQuant post is dated 31 Jan 2022 on the page, and the TradingView script was published 30 Mar 2025 and updated 12 Apr 2025.
- **7 public YouTube videos** (ASR subtitles), dated 2016-02-02 to 2026-01-12.

Kind of material: indicator reference pages and DSS tool tutorials; one reader's chapter summary of Bressert's book; one live-chart demo from the Bressert firm's software; one cycle-analysis lesson; two "strategy test" videos. Public. ASR mangles corrected in paraphrase only: "wter Reser/berserk/Bart's/B's" = Walter Bressert, "breasted/presser/bresser" = Bressert, "blauth" = Blau, "Dixie" = DXY, "Bob Lucas" = Bob Loukas, "d-trend" = detrend.

**These are all secondary descriptions of Walter Bressert's method, not his own text.**
- No source here is Bressert's own writing. The web pages cite no primary source.
- The **closest to primary** is the 2016 live-chart video on the **Jerome Bressert** channel (Walter's son and firm). It demonstrates the firm's proprietary HAL Cycle Finder bands and lines, not the DSS or the published rules.
- The most detailed account of the *method* is **CrusherHODL** summarising chapter 1 of Bressert's book ("The Power of Oscillator/Cycle Combinations"). That is a viewer's paraphrase of the book, with his own asides mixed in. Only what he attributes to the book ("he labels", "according to B's research", "this is from Walter [Bressert] himself") is filed under Bressert below.
- The TradingView RSI3M3+ page's phrasing ("Let me break it down and explain how it works") suggests a generated or paraphrased write-up, so it is third-hand at best.
- The DSS tutorial videos (NetPicks, The Forex Geek, Indicator Factory) and the TQFM test are retail indicator content. They are not cycle method.
- Attribution of the DSS itself is disputed across the sources (see below).

Every rule is attributed to the source that states it. Each video presenter has their own subsection, and nothing is blended into "Bressert's rules" unless the source says it is quoting Bressert.

| Date | Source | Presenter / channel | Link | What it gives |
|---|---|---|---|---|
| — | LuxAlgo Library, "DSS Bressert" concept page | LuxAlgo | https://www.luxalgo.com/library/concept/dss-bressert/ | Formula, typical lengths, 80/20 hooks, a hook used inside a cycle framework |
| — | TC2000 Help, "Double Smoothed Stochastic Bressert" (PCF) | TC2000 | https://help.tc2000.com/m/69445/l/755063-double-smoothed-stochastic-bressert | Exact PCF formula with defaults 14 / 5 / 1, an EMA 5 signal plot, 70/30 lines |
| — | StrategyQuant codebase, "Double Smoothed Stochastic Bressert" | clonex / Ivan Hudec | https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/ | Attribution, 80/20, a trend-behaviour claim, cross conditions (the code zip was not read) |
| — | TradingView, "RSI3M3+ v.1.8" | kromut76 | https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/ | "Bressert's Five Trading Rules" with RSI3M3 (not a DSS), 40/60 lines, timing bands |
| 2016-02-02 | Sample Live Chart Using the HAL Cycle Finder Trading Software | Jerome Bressert channel (presenter not named) | https://youtu.be/pEXJnwGJCmw | Live ES 5-min/20-min demo: Oracle Band, cycle-low bands, three oscillator lines |
| 2022-10-02 | Stock Market Cycle Analysis 101 | SwingTrading with Cycles | https://youtu.be/wKIzZiLtuL8 | Low-to-low cycles, swing definitions, 10-period MA gauge, DXY yearly/3-year/weekly/daily counts |
| 2022-11-22 | Best Scalping Strategy l Full Result B DSS Bressert indicator | Indicator Factory | https://youtu.be/Vq5iMzqDAeQ | DSS calculation "according to Bressert", parameter ranges, line-cross signals, a claimed 11 wins / 2 losses |
| 2023-07-20 | Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator | NetPicks | https://youtu.be/r7a4sDuM_7M | Blau attribution, 70/30, re-cross signals, divergence |
| 2023-10-25 | Double Stochastic Indicator Trading Strategy, Lets Test It! | TQFM Trading Strategies | https://youtu.be/3KAdvlpHdck | Two plain stochastics (20 and 200), "trend inside a trend" (not a DSS); no results spoken |
| 2024-12-22 | Cycle Theory Trading (Walter Bressert) Crypto Chapter 1 | CrusherHODL | https://youtu.be/mXf069WzS2g | The book's cycle hierarchy, alpha/beta sub-cycles, translation, 70% timing bands, seasonals |
| 2026-01-12 | DSS Bressert Indicator for MT4 & MT5 | The Forex Geek | https://youtu.be/kHrmwew2sSQ | MT4/MT5 defaults EMA 8 / stochastic 13, 80/20 cross signals |

Site furniture (menus, comment threads, risk disclaimers, other indicator lists, channel promotions) was ignored. Video timestamps cite the start of the ~30 s caption chunk that holds the words.

## Method in its own terms

As these sources relay it, Bressert's method is cycle-first.
- **Nested cycles.** Markets move in nested cycles: long-term (2 to 11 years), seasonal, primary or weekly (averaging 9 to 22 weeks), and the **trading (daily) cycle**, measured in days. Each trading cycle splits into an **alpha** and a **beta** sub-cycle, each normally half its length.
- **The larger cycle sets the trend of the smaller.** When it rises, the smaller cycle peaks late (right translated, with the beta crest) and makes higher lows. When it falls, the smaller cycle peaks early (left translated, with the alpha crest) and makes lower lows. "You have to trade the trend with the cycle".
- **Timing bands** are the windows where tops and bottoms are due. Bressert's "70% cycle timing bands" catch about 7 in 10 turns, with roughly 20% early and 10% late.
- **The oscillator only times the turn inside the band.** It is never the thesis. The book's premise, as relayed, is that the oscillator/cycle combination gives "like a 70% probability". LuxAlgo says an oversold DSS hook "is only taken when a dominant cycle low is due and the higher timeframe points up".
- **The five rules** (RSI3M3+ page):
  1. the low falls 15 to 30 market days after the previous trading-cycle bottom;
  2. a trading-cycle high came first, with the oscillator above 60;
  3. the oscillator drops below 40 and turns up;
  4. buy on a rise above the high of the upturn day;
  5. the stop goes slightly under the trading-cycle low.
- **The DSS** (double-smoothed stochastic) is the oscillator most associated with his name. It is a stochastic of an EMA-smoothed stochastic, smoothed again, read for hooks or re-crosses of its 80/20 (or 70/30) extremes. The sources disagree on its lengths and levels.

## Bressert-attributed method (as relayed; the sources quote or summarise Bressert)

### Cycle hierarchy and lengths (CrusherHODL, reading chapter 1 of the book)

- **Cycles in "order of dominance".** A larger cycle contains smaller cycles, and so on. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 01:36](https://youtu.be/mXf069WzS2g?t=96)
- **Long-term dominant cycle**: "longer than one year", "most of these long-term Cycles range from two to 11 years". Start the analysis here, because these set the trend for the next shorter (seasonal) cycle, and "the big moves up or down usually occur around the highs and lows of these Cycles". [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 01:36](https://youtu.be/mXf069WzS2g?t=96), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 08:48](https://youtu.be/mXf069WzS2g?t=528)
- **Primary (weekly) cycle**: a dominant cycle shorter than one year. "the average cycle length ranges from 9 to 22 weeks"; "every asset is going to be a little bit different". There are "simple" and "complex" primary cycles. The complex one has half-cycle highs and lows. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 03:11](https://youtu.be/mXf069WzS2g?t=191), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 11:21](https://youtu.be/mXf069WzS2g?t=681)
- **Trading cycle = the daily cycle**, "measured in days". It is "used to enter and exit the market for short-term trades". (CrusherHODL says the book's "trading" label here is "a misprint" for daily; that is his reading.) [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 03:11](https://youtu.be/mXf069WzS2g?t=191)
- **Alpha and beta sub-cycles.** "within the trading cycle there are two sub cycles each normally half the length". The trading cycle has no crest of its own: "it will always have a Crest or high at the same time as one of the shorter sub Cycles". It always begins with a new alpha cycle. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 03:42](https://youtu.be/mXf069WzS2g?t=222), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 04:12](https://youtu.be/mXf069WzS2g?t=252)
- **Seven components of the trading cycle**: the trading-cycle low to low; the alpha low to low (DCL to half-cycle low); the beta low to low; the alpha low to high; the alpha high to low; the beta low to high; the beta high to low. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 13:23](https://youtu.be/mXf069WzS2g?t=803), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 14:23](https://youtu.be/mXf069WzS2g?t=863)
- **Trading-cycle length on daily bars: 15 to 30 market days** low to low (rule 1 of the five rules, RSI3M3+ page). The page's script defaults are minimum/maximum cycle length 15 to 30 and a detection lookback of 30. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
  - Note the clash with CrusherHODL's own "60-day daily cycle count", which he gets from the counts "most people agree on" (he names Bob Loukas), not from the book. See his subsection.

### Translation, trend and mid-cycle (CrusherHODL, from the book's text and diagrams)

- **The daily cycle's relationship to the next longer cycle.** That is "usually the primary weekly cycle", sometimes a half-primary. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 05:43](https://youtu.be/mXf069WzS2g?t=343)
- **Weekly up → the daily peaks with the beta crest (right translated). Weekly down → the daily peaks with the alpha crest (left translated).** "if the primary weekly cycle is moving in an uptrend the daily … tends to Peak at the crest or high with the beta". [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 06:13](https://youtu.be/mXf069WzS2g?t=373)
- **Lows follow the larger cycle.** When a larger cycle is moving down, the smaller cycle's peaks shift left and "the smaller cycle lows will be lower than the previous low". When it is moving up, the peaks shift right and the smaller cycle makes higher highs. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 06:44](https://youtu.be/mXf069WzS2g?t=404), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 07:16](https://youtu.be/mXf069WzS2g?t=436)
- **Trend from the sequence of lows.** Higher lows = uptrend. Lower highs and lower lows = downtrend (read off Bressert's own graphic). "each longer cycle determines a trend for the shorter cycle if the direction of the longer cycle is known than the trend is known for the shorter cycle". [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 05:13](https://youtu.be/mXf069WzS2g?t=313), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 07:47](https://youtu.be/mXf069WzS2g?t=467)
- **Mid-cycle.** CrusherHODL says there are actually three kinds but the book "only lists two" (right and left translated). The third, the midpoint cycle, peaks in the middle: "if it was a 60-day cycle 30 would … be your high". So the mid-cycle as a third category is his addition, not Bressert's. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 07:16](https://youtu.be/mXf069WzS2g?t=436), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 07:47](https://youtu.be/mXf069WzS2g?t=467)
- **Half-cycle trading.** CrusherHODL describes how Bressert trades: "he plays like the half cycle low", and he shorts the alpha-cycle high. That is, he trades the sub-cycles, not only the trading-cycle lows. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 16:56](https://youtu.be/mXf069WzS2g?t=1016)
- **Oscillators confirm the cycle turn.** The timing bands for primary-cycle tops and bottoms "indicate … when to look for specific patterns and the oscillators that indicate a cycle top or bottom or a high probability trade". The book's oscillator is not named here; CrusherHODL offers RSI, MACD or TSI himself. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 10:50](https://youtu.be/mXf069WzS2g?t=650)

### Timing bands (the 70% rule)

- **"70% cycle timing bands".** "according to B's research on Cycles he coined the phrase 70% cycle timing bands because he found that seven out of 10 times a … cyclical component will top or bottom within" the band, "and approximately 20% will occur before and then 10% after". CrusherHODL's follow-on arithmetic ("so 90% a little bit before") is muddled. The relayed split is 70 in the band / 20 early / 10 late. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 12:23](https://youtu.be/mXf069WzS2g?t=743), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 12:53](https://youtu.be/mXf069WzS2g?t=773)
- **The oscillator/cycle combination "gives you like a 70% probability"** (CrusherHODL's one-line summary of the book's premise). [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 00:34](https://youtu.be/mXf069WzS2g?t=34)
- **The script's timing bands** (RSI3M3+): projected from measured cycle lengths. "Signals occurring within these bands have higher probability of success". The projection formula is not given. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

### The five trading rules (RSI3M3+ page; the only source that lists them)

1. "The low must be 15-30 market days from the previous Trading Cycle bottom".
2. "A Trading Cycle high must have occurred with the oscillator above 60".
3. "The oscillator must drop below 40 and turn up".
4. "Entry is triggered by a rise above the price high of the upturn day".
5. "Place stop slightly below the Trading Cycle low" (the script uses 99% of the bottom price).

The oscillator on that page is RSI3M3, not the DSS. The sell side mirrors this: above 60, a turn down, exit below the low of the downturn day. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)

### DSS rules attributed to Bressert

- **Calculation "according to Bressert"** (Indicator Factory, narrating a written guide).
  - Step 1: take (close − period low) / (period high − period low), "exponentially smoothed and then multiplied by 100".
  - Step 2: "the method is analogous to one with the distinction that now the prices of the newly calculated price series of 1 is used".
  - Same structure as LuxAlgo's formula, but no lengths given.
  - [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 00:35](https://youtu.be/Vq5iMzqDAeQ?t=35), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 01:05](https://youtu.be/Vq5iMzqDAeQ?t=65)
- **Oversold hooks inside the cycle** (LuxAlgo). "In Bressert's own cycle-trading framework, the oscillator's oversold hooks were used to time entries within an identified dominant cycle, not as standalone signals". [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **No source gives Bressert's own DSS lengths or levels.**

### Seasonal tendencies (Bressert, as relayed by CrusherHODL)

CrusherHODL skimmed the seasonal chapter and says Bressert noticed these tendencies "in his career". They are given as recited, and garbled where the audio is.
- **S&P**: lows "tend to be made in September through November or March or uh February"; highs "August through October or sometimes April".
- **Interest rates**: lows in Q1 or Q3, highs in Q3 or Q4.
- **Precious metals**: in bull markets, a top in Q1 and a bottom in Q2. In bear markets, a bottom in Q3 and a top in Q3 or Q4.
- **Currencies**: lows May through September, highs November and April.

[Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 09:50](https://youtu.be/mXf069WzS2g?t=590), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 10:20](https://youtu.be/mXf069WzS2g?t=620)

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

### 5. Indicator Factory (structure as LuxAlgo; ranges only)

- The calculation is narrated "according to Bressert": a stochastic of price, exponentially smoothed, ×100, then the same method applied to that smoothed series.
- "the adjustable period length can be chosen from two to five hundred the most common settings will have a period length ranging from 5 to 30". The smoothing can be set "in the interval from 1 to 50", and "meaningful smoothing values lie in the short-term range".
- Levels: "values above 70 or 80 must be regarded as overbought and values below 20 or 30 as oversold". The MT4 indicator shown draws its horizontal levels at 80 and 20. A rise above the centre line is bullish, a fall below it bearish.
- It has two coloured lines, red and blue, but what each line is is not said.
- Attribution: "one after the other William Blau and Walter bresser [sic] each presented a version", and later "developed by Mr bressert".
- [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 00:05](https://youtu.be/Vq5iMzqDAeQ?t=5), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 01:05](https://youtu.be/Vq5iMzqDAeQ?t=65), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 01:35](https://youtu.be/Vq5iMzqDAeQ?t=95), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 02:06](https://youtu.be/Vq5iMzqDAeQ?t=126)

### 6. The Forex Geek (MT4/MT5 defaults: EMA 8, stochastic 13)

- "the EMA period is set at default 8, stoastic [sic] period default 13". Levels 80 and 20.
- This is the MetaQuotes pair. It equals the terminal's **shipped default** (13 / 8), not Camel's (10 / 9).
- [DSS Bressert Indicator for MT4 & MT5, 2026-01-12, 01:04](https://youtu.be/kHrmwew2sSQ?t=64)

### 7. NetPicks (credited to Blau; a different construction described)

- "it was created by a guy named William blauth [Blau]". It "applies EMAs … to a standard stochastic percent k". It involves "applying EMAs of two different periods to the highest high and lowest low for a specified period".
- That last sentence describes Blau's construction, where the range terms are smoothed before dividing (see LuxAlgo's note). It is not the stochastic-of-a-smoothed-stochastic. No lengths are given.
- Levels: "above 70 indicate overbought below 30 are oversold"; later "above 70 or 80" and "20 or 30".
- [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 00:00](https://youtu.be/r7a4sDuM_7M?t=0), [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 00:30](https://youtu.be/r7a4sDuM_7M?t=30), [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 01:00](https://youtu.be/r7a4sDuM_7M?t=60), [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 01:32](https://youtu.be/r7a4sDuM_7M?t=92)

### 8. Not a DSS: TQFM, Jerome Bressert, CrusherHODL, SwingTrading with Cycles

- **TQFM**'s "double stochastic" is **two plain stochastics**, "a fast one with a length of 20 and a slow one with a length of 200". There is no double smoothing. [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 00:30](https://youtu.be/3KAdvlpHdck?t=30)
- **The Jerome Bressert video** uses the HAL Cycle Finder's own green, yellow and red lines and a detrend oscillator. No formula or lengths are given. [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 01:31](https://youtu.be/pEXJnwGJCmw?t=91)
- **CrusherHODL** and **SwingTrading with Cycles** give no DSS settings.

### Against the terminal and Camel Finance settings

The terminal's DSS Bressert (`AccessibleTrader.Core/Services/Indicators/DssBressertProvider.cs`) computes exactly the LuxAlgo structure: stochastic P, EMA E, a second stochastic of the EMA line over P, EMA E, plus a trigger EMA T. Its **shipped default is 13 / 8 / 5**. The **10 / 9 / 5 with 80/20** setting (Camel Finance's, read from a screen frame, see `camel-finance.md`) is a parameter change.

| Source | Structure | Stochastic length | EMA length | Signal line | Levels | Against 10/9/5, 80/20 and 13/8/5 |
|---|---|---|---|---|---|---|
| LuxAlgo | stoch → EMA → stoch of EMA line → EMA, same n and m in both passes | 10 to 21 typical | 3 to 9 typical | optional, "often an EMA of the DSS", no length | 80 / 20 | Contains both 10/9 and 13/8. Levels **agree** |
| TC2000 | same, plus an optional third EMA (1 = off) | 14 | 5 | EMA 5 | **70 / 30** | Both lengths differ. Trigger 5 **agrees**. Levels **disagree** |
| StrategyQuant | "double exponential smoothing", no formula on the page | not on the page | not on the page | not stated | 80 / 20 | Levels **agree** |
| RSI3M3+ | not a DSS (RSI 3, MA 3) | n/a | n/a | 3-period MA of RSI 3 | 30/70 signal, 40/60 Bressert rules | **Disagrees** (different oscillator and levels) |
| Indicator Factory | same as LuxAlgo, "according to Bressert" | 2 to 500, "most common" 5 to 30 | 1 to 50, "short-term" | a second line (red against blue), unnamed | 80 / 20 drawn; "70 or 80", "20 or 30" in text; 50 centre line | Ranges contain both. Levels **agree** (drawn) |
| The Forex Geek | MT4/MT5 DSS Bressert | **13** | **8** | not stated | 80 / 20 | **Equals the terminal's shipped 13/8**. Differs from Camel's 10/9. Levels **agree** |
| NetPicks | Blau-style (range terms smoothed), credited to Blau | not stated | "two different periods" | not stated | 70 / 30 (also "70 or 80", "20 or 30") | A different construction. Levels mostly **disagree** |
| TQFM | two plain stochastics | 20 and 200 | none | none | "top", "bottom", midline | Not comparable |
| Terminal / Camel | LuxAlgo structure | 13 shipped / 10 Camel | 8 shipped / 9 Camel | EMA 5 trigger | 80 / 20 | — |

So no source contradicts (10, 9, 5) outright. LuxAlgo's and Indicator Factory's ranges contain it, but **no source names 10/9/5 as a default**. The only named default pairs are 13/8 (The Forex Geek, = the terminal's shipped default) and 14/5 (TC2000). The levels split: 80/20 (LuxAlgo, StrategyQuant, The Forex Geek, Indicator Factory's drawn levels) against 70/30 (TC2000, NetPicks). The only source that states Bressert's *rules* uses 40/60 on an RSI3M3, not 20/80 on a DSS. No source gives Bressert's own DSS lengths.

The headings from Context to Position sizing collect the **web pages'** rules, in method order. Some are Bressert-attributed and some are the page authors' own. Each video presenter's own method follows under "Video presenters", one subsection each.

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

Nothing stated on the web pages beyond the stop. (CrusherHODL's "go harder" at larger-cycle lows is in his subsection.)

## Video presenters (each presenter's own method; not Bressert's unless marked)

### CrusherHODL (2024-12-22): his own additions to the book

His Bressert-attributed material is filed at the top. These are his own counts and opinions.
- **Context: trade with the trend.** "you have to trade the trend with the cycle trading or else it is … it doesn't work". Always look at higher timeframes, not "the one minute schizophrenic … frame". [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 05:43](https://youtu.be/mXf069WzS2g?t=343), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 06:44](https://youtu.be/mXf069WzS2g?t=404)
- **Counts: a 60-day daily cycle ± 10% = 54 to 66 days.** This is from "the counts that most people agree on in … the books and you know Bob Lucas [Loukas]", not from Bressert. It conflicts with the five rules' 15 to 30 market-day trading cycle. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 14:54](https://youtu.be/mXf069WzS2g?t=894)
- **Bitcoin counts:** a weekly cycle of 24 to 42 weeks (also "24 to like I think 40 something 44 42 weeks"), and a 4-year cycle. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 02:39](https://youtu.be/mXf069WzS2g?t=159), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 15:24](https://youtu.be/mXf069WzS2g?t=924)
- **Nesting.** "a daily cycle low will always fall on a weekly cycle low". He also says "every time you have a weekly cycle bottom it has to coincide with a … daily … cycle". [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 02:39](https://youtu.be/mXf069WzS2g?t=159), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 15:24](https://youtu.be/mXf069WzS2g?t=924)
- **Veto / invalidation.**
  - If the alpha-cycle low breaks below the daily cycle low, "this is technically a cycle failure".
  - "if you're in an uptrend it shouldn't break the previous … daily cycle lo[w]".
  - [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 14:23](https://youtu.be/mXf069WzS2g?t=863), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 16:26](https://youtu.be/mXf069WzS2g?t=986)
- **Sizing by cycle degree.** At a weekly cycle low "you can go harder", and at a yearly (4-year) cycle low "you go even harder". The long-term lows are when "you bet a lot". No numbers. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 15:24](https://youtu.be/mXf069WzS2g?t=924), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 01:36](https://youtu.be/mXf069WzS2g?t=96)
- **Oscillators.** RSI, MACD, or the true strength index, "to kind of see if the sellers are exhausted". No settings. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 10:50](https://youtu.be/mXf069WzS2g?t=650)
- **His own trading.** He would rather mark only the daily cycle lows than Bressert's alpha and beta lows ("too much for me"). [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 16:56](https://youtu.be/mXf069WzS2g?t=1016)

### Jerome Bressert channel (2016-02-02): HAL Cycle Finder live chart

The presenter is not named. This is a live E-mini S&P session on 2 Feb 2016, sped up 10×. The tools are the firm's proprietary bands and lines, so nothing here is computable without their definitions.
- **Context: the higher-timeframe oscillators set the side.** A 5-minute chart sits beside a longer-term (20-minute) chart. "the oscillators on the longer term charts are headed down and so … we were looking for a [sell]". [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 00:31](https://youtu.be/pEXJnwGJCmw?t=31), [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 02:02](https://youtu.be/pEXJnwGJCmw?t=122)
- **Location: the Oracle Band.**
  - After price breaks outside the band it typically retraces into it: "about 25 percent of the way up which is typical sometimes it will go to the midsection". "it does it quite often".
  - Prices "tend to trade on these bands on the lower part or upper part" in a bearish or bullish market. In a bear market "the prices aren't going too much lower than where the bands are so if you're looking for a grand slam … you're not going to get it".
  - "these bands will give you an indication of price … and time".
  - [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 00:31](https://youtu.be/pEXJnwGJCmw?t=31), [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 02:02](https://youtu.be/pEXJnwGJCmw?t=122), [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 02:33](https://youtu.be/pEXJnwGJCmw?t=153)
- **Trigger: three oscillator lines.**
  - The green line ("flexible enough and sensitive enough") turns first and "give[s] you a [sell] line".
  - The yellow "medium line" follows.
  - The red line, "holding down sturdy", says "the Market's still on its way down". "these three indicators work really really well together".
  - [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 01:01](https://youtu.be/pEXJnwGJCmw?t=61), [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 01:31](https://youtu.be/pEXJnwGJCmw?t=91)
- **Cycle-low bands.** Green dots, "automatic", mark cycle lows. An "upper Trend band" acts as resistance. The yellow line being "about 82 and a half" meant the market "would be slowing down". It is unclear whether 82.5 is an oscillator reading or a price. [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 03:03](https://youtu.be/pEXJnwGJCmw?t=183)
- **Confirmation of a coming low.** The detrend oscillator on the higher-timeframe chart "had gone to a point where it is typically bottoms". Yet he is "not looking to buy". He waits for a rally to set up a new sell, because the day is bearish. [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 03:35](https://youtu.be/pEXJnwGJCmw?t=215), [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 04:06](https://youtu.be/pEXJnwGJCmw?t=246)
- **Gaps.** Stocks gap outside the Oracle bands at the open and "typically run back into the Oracle Band". [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 04:37](https://youtu.be/pEXJnwGJCmw?t=277)
- **Stops, exits and sizing:** nothing stated.

### SwingTrading with Cycles (2022-10-02): cycle analysis 101

He does not mention Bressert. His method is price structure on OHLC candles plus day, week and month counts.
- **Context.**
  - Cycles run low to low, "and the high can happen at any time".
  - "the low doesn't have to happen on a scheduled basis either". Do not just wait for a date; use "the price action to be my guide".
  - Lower timeframes "ultimately trigger and make the higher time frames do things".
  - [Stock Market Cycle Analysis 101, 2022-10-02, 00:30](https://youtu.be/wKIzZiLtuL8?t=30), [Stock Market Cycle Analysis 101, 2022-10-02, 01:01](https://youtu.be/wKIzZiLtuL8?t=61), [Stock Market Cycle Analysis 101, 2022-10-02, 19:44](https://youtu.be/wKIzZiLtuL8?t=1184), [Stock Market Cycle Analysis 101, 2022-10-02, 21:16](https://youtu.be/wKIzZiLtuL8?t=1276)
- **Trigger: swing low and swing high (exact).**
  - A low is set when a later candle moves above the high of the candle that marks the low.
  - A high is set when a later candle moves below the low of the candle that marks the high.
  - "you know a low when you have a candle that marks a low and then the subsequent candle moves above the high of that same candle".
  - [Stock Market Cycle Analysis 101, 2022-10-02, 04:42](https://youtu.be/wKIzZiLtuL8?t=282), [Stock Market Cycle Analysis 101, 2022-10-02, 20:16](https://youtu.be/wKIzZiLtuL8?t=1216), [Stock Market Cycle Analysis 101, 2022-10-02, 25:21](https://youtu.be/wKIzZiLtuL8?t=1521)
- **Location / confirmation: the 10-period MA.**
  - "your cycle low is always going to drop below the 10 period moving average", "and that's true on all time frames".
  - Late in a weekly cycle, "the pullback almost always bottoms below the 10 period moving average".
  - [Stock Market Cycle Analysis 101, 2022-10-02, 22:49](https://youtu.be/wKIzZiLtuL8?t=1369), [Stock Market Cycle Analysis 101, 2022-10-02, 23:19](https://youtu.be/wKIzZiLtuL8?t=1399), [Stock Market Cycle Analysis 101, 2022-10-02, 23:49](https://youtu.be/wKIzZiLtuL8?t=1429)
- **Reversal candles.** A bullish outside candle (a new low and a new high, closing green) marked the 1980 DXY yearly low. A bearish outside candle marked the 1985 top. [Stock Market Cycle Analysis 101, 2022-10-02, 05:44](https://youtu.be/wKIzZiLtuL8?t=344), [Stock Market Cycle Analysis 101, 2022-10-02, 07:17](https://youtu.be/wKIzZiLtuL8?t=437)
- **Cycle shape.**
  - "every cycle starts by advancing". A cycle that declines as long as it advanced (7 up, 7 down on the 14-year DXY cycle) is "not very bullish".
  - Measure a move "from the low of the last cycle to the high of the current cycle".
  - [Stock Market Cycle Analysis 101, 2022-10-02, 10:53](https://youtu.be/wKIzZiLtuL8?t=653), [Stock Market Cycle Analysis 101, 2022-10-02, 11:23](https://youtu.be/wKIzZiLtuL8?t=683), [Stock Market Cycle Analysis 101, 2022-10-02, 12:57](https://youtu.be/wKIzZiLtuL8?t=777)
- **Stops.** The swing candle "also gives you a sense of where you can set your stop loss", whether long or short. [Stock Market Cycle Analysis 101, 2022-10-02, 25:21](https://youtu.be/wKIzZiLtuL8?t=1521)
- **Trend lines** are "a helpful kind of rule of thumb" but "dubious" and "subjective". He prefers swing structure. [Stock Market Cycle Analysis 101, 2022-10-02, 25:52](https://youtu.be/wKIzZiLtuL8?t=1552)
- **Exits and sizing:** nothing stated.

### Indicator Factory (2022-11-22): DSS scalping

The narration appears to be read from two written guides. One is an indicator description; the other, a first-person blog-style passage.
- **Context.** "it is essential to be aware of the current Trend direction". Take buys "if the market is in an overall uptrend" and sells in a downtrend. [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 00:05](https://youtu.be/Vq5iMzqDAeQ?t=5), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 02:06](https://youtu.be/Vq5iMzqDAeQ?t=126)
- **Triggers.**
  - (a) The DSS "rebounds from 20 and turns blue" → buy (in an uptrend). It "turns red and drops below 80" → sell (in a downtrend).
  - (b) The main signal: "when the red line of the indicator crosses the Blue Line downwards that's a signal to open a bearish trade when the red line crosses the blue upwards open a bullish trade".
  - (c) The alternative: the red line crossing 80 downwards from above = sell, and the mirror at 20 for buys. The audio is cut off at "place bullish trades".
  - (d) A DSS rise above its centre line is bullish, a fall below it bearish.
  - [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 01:35](https://youtu.be/Vq5iMzqDAeQ?t=95), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 02:06](https://youtu.be/Vq5iMzqDAeQ?t=126), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 03:37](https://youtu.be/Vq5iMzqDAeQ?t=217), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 04:07](https://youtu.be/Vq5iMzqDAeQ?t=247)
- **Claimed results (record only).** From "a picture of the indicator in action": "blue lines are winners and red lines are losers 11 winning trades and two losing trades". "in my book that means outstanding especially because only one indicator is used".
  - No instrument, timeframe, period, settings, stop, target or costs are stated.
  - Despite the title's "Full Result", no other result is spoken.
  - [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 03:06](https://youtu.be/Vq5iMzqDAeQ?t=186)
- **Timeframes.** "it suits All metatrader 4 time frames enabling you to perform scalping and intraday trading". [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 00:35](https://youtu.be/Vq5iMzqDAeQ?t=35)
- **Stops, exits and sizing:** nothing stated. The exit is presumably the opposite cross; not said.

### NetPicks (2023-07-20): DSS overview

- **Triggers: re-crosses of 70 and 30.** "crossovers above the 70 followed by a crossover back below the 70 that could be a sell signal crossovers below 30 and Then followed by a crossover back above 30 could be Buy Signal". [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 03:03](https://youtu.be/r7a4sDuM_7M?t=183)
- **Overbought is not a sell by itself.** Above 70 or 80 the asset "could experience a bearish reversal soon but it could also go into a trading range". The same holds for 20 or 30. [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 03:03](https://youtu.be/r7a4sDuM_7M?t=183)
- **Divergence.** Bullish = price makes a lower low while the DSS makes a higher low. Bearish = price makes a higher high while the DSS makes a lower high. It is claimed to show up earlier than on a raw stochastic. [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 02:32](https://youtu.be/r7a4sDuM_7M?t=152), [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 03:33](https://youtu.be/r7a4sDuM_7M?t=213)
- **Confirmation.** Combine it with trend lines or support and resistance. "no single indicator should be used in an isolation". [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 02:02](https://youtu.be/r7a4sDuM_7M?t=122), [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 04:04](https://youtu.be/r7a4sDuM_7M?t=244)
- **Stops, exits, sizing and timeframes:** nothing stated.

### The Forex Geek (2026-01-12): DSS Bressert MT4/MT5

- **Triggers: level crosses.** "when the indicator is above the 80 level, it's an overbought signal … When it crosses below the 80 level, it's a sign to sell". Below 20 is oversold, and "when it crosses above the 20 level, it's a sign to buy". [DSS Bressert Indicator for MT4 & MT5, 2026-01-12, 01:04](https://youtu.be/kHrmwew2sSQ?t=64), [DSS Bressert Indicator for MT4 & MT5, 2026-01-12, 01:35](https://youtu.be/kHrmwew2sSQ?t=95)
- **Context.** "I'm going to recommend using this indicator in a trending market". The trend definition is not given. [DSS Bressert Indicator for MT4 & MT5, 2026-01-12, 01:35](https://youtu.be/kHrmwew2sSQ?t=95)
- **Settings:** EMA 8, stochastic 13 (see definition 6). Stops, exits and sizing: nothing stated.

### TQFM Trading Strategies (2023-10-25): two-stochastic "trend inside a trend" (not a DSS)

- **Indicators.** A fast stochastic of length 20 and a slow stochastic of length 200. No %K or %D smoothing is stated. [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 00:30](https://youtu.be/3KAdvlpHdck?t=30)
- **Short.** "When the slower stochastic is positioned at the bottom signaling a potential downtrend we exercise patience we wait for the fast stochastic to bounce and give us the green light to sell". Exit when "both stochastic indicators … hit the bottom". [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 01:30](https://youtu.be/3KAdvlpHdck?t=90), [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 02:00](https://youtu.be/3KAdvlpHdck?t=120)
- **Long (mirror).** The slow stochastic "climbs to the top", then wait for the fast one "to pull back from its peak". Exit when the fast "falls in line with the slow stochastic at the top". The wording is muddled ("we know it's time to sell locking in those gains"), and the long entry trigger is not stated more precisely than that. [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 02:31](https://youtu.be/3KAdvlpHdck?t=151)
- **Veto / stop.** "what if the slow [stochastic] crosses the midline in such cases our strategy is to abandon the position". [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 03:04](https://youtu.be/3KAdvlpHdck?t=184)
- **Sizing.** Generic risk-management talk only. [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 03:04](https://youtu.be/3KAdvlpHdck?t=184)
- **Claimed results (record only): none spoken.** The title says "Lets Test It!", but no trade count, win rate, profit, instrument or timeframe appears anywhere in the transcript. Any results must be on screen only, which we cannot read. "Thresholds" for top, bottom and midline are not given numerically.

## Timeframes

- **RSI3M3+.** Daily is best ("for intermediate-term trading"). Weekly for longer-term position trading. On intraday charts the cycle lengths must be adjusted. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Bressert's rule 1** is stated in **market days** (15 to 30), so the five rules as given are daily-bar rules. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Bressert hierarchy (CrusherHODL).** Long-term 2 to 11 years, primary/weekly 9 to 22 weeks on average, trading/daily in days, alpha and beta sub-cycles about half the trading cycle. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 01:36](https://youtu.be/mXf069WzS2g?t=96), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 03:11](https://youtu.be/mXf069WzS2g?t=191)
- **Jerome Bressert channel.** A 5-minute trading chart with a 20-minute chart for oscillator direction. [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 02:02](https://youtu.be/pEXJnwGJCmw?t=122)
- **SwingTrading with Cycles.** Yearly, 3-year (36 months), weekly and daily counts. The 10-period MA rule is applied "on all time frames". [Stock Market Cycle Analysis 101, 2022-10-02, 16:38](https://youtu.be/wKIzZiLtuL8?t=998), [Stock Market Cycle Analysis 101, 2022-10-02, 23:19](https://youtu.be/wKIzZiLtuL8?t=1399)
- **Indicator Factory.** All MT4 timeframes, scalping and intraday. [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 00:35](https://youtu.be/Vq5iMzqDAeQ?t=35)

## Macro context

- **Bressert's seasonal tendencies** (S&P, interest rates, precious metals, currencies), as relayed by CrusherHODL. See "Seasonal tendencies" at the top. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 09:50](https://youtu.be/mXf069WzS2g?t=590)
- **The book's premise, as relayed.** Current fundamentals are "relative only to the current price structure", and "fundamental events will occur to move prices in the direction of the cycle". [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 01:05](https://youtu.be/mXf069WzS2g?t=65)
- **SwingTrading with Cycles.** In early October 2022, the dollar "is a wrecking ball for all assets". [Stock Market Cycle Analysis 101, 2022-10-02, 24:51](https://youtu.be/wKIzZiLtuL8?t=1491)

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
- **Indicator Factory DSS:**
  - Period 2 to 500, "most common" 5 to 30. Smoothing 1 to 50, "short-term".
  - Levels drawn at 80/20 (the text says "70 or 80", "20 or 30"). Centre line.
  - Colours: the curve turns blue = bullish momentum, red = bearish. In the crossing rule, red line against blue line.
  - [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 01:05](https://youtu.be/Vq5iMzqDAeQ?t=65), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 02:06](https://youtu.be/Vq5iMzqDAeQ?t=126)
- **The Forex Geek DSS:** EMA 8, stochastic 13, levels 80/20. [DSS Bressert Indicator for MT4 & MT5, 2026-01-12, 01:04](https://youtu.be/kHrmwew2sSQ?t=64)
- **NetPicks DSS:** levels 70/30 (also "70 or 80", "20 or 30"). No lengths. [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 01:32](https://youtu.be/r7a4sDuM_7M?t=92)
- **TQFM:** Stochastic 20 (fast) and Stochastic 200 (slow). Midline = the abandon line. [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 00:30](https://youtu.be/3KAdvlpHdck?t=30)
- **Jerome Bressert channel (HAL Cycle Finder):**
  - Green = sensitive line, yellow = medium line, red = the sturdy trend line.
  - Green dots = automatic cycle-low bands. There are an Oracle Band and an upper trend band.
  - No formulas or lengths.
  - [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 01:31](https://youtu.be/pEXJnwGJCmw?t=91), [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 03:03](https://youtu.be/pEXJnwGJCmw?t=183)
- **SwingTrading with Cycles:** a 10-period moving average (type not stated) on every timeframe. [Stock Market Cycle Analysis 101, 2022-10-02, 22:49](https://youtu.be/wKIzZiLtuL8?t=1369)

## Calibration numbers and falsifiable side-claims

- **DSS shape.** It "tends to travel quickly between its extremes and pause there", with "less lag than a heavily smoothed slow stochastic". Testable against a slow stochastic: time from extreme to extreme, dwell at the extremes, and lag of turns relative to price pivots. [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Hooks are cleaner than raw-stochastic crossings** (fewer false turns). [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Extremes are reached "quite frequently, even in strong trends"** (StrategyQuant) against **"pins at an extreme… counter-trend hooks fail repeatedly"** (LuxAlgo). Measurable: the fraction of trend bars (by any trend definition) on which the DSS visits the opposite extreme, and the hit rate of counter-trend hooks. [StrategyQuant DSS Bressert](https://strategyquant.com/codebase/double-smoothed-stochastic-bressert/), [LuxAlgo DSS Bressert](https://www.luxalgo.com/library/concept/dss-bressert/)
- **Timing-band signals have "higher probability of success"** than out-of-band signals. [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Trading cycles run 15 to 30 market days** low to low (on daily bars). [TradingView RSI3M3+](https://www.tradingview.com/script/FNk3wGex-RSI3M3-v-1-8/)
- **Bressert's 70% timing bands: about 70% of cycle turns fall inside the band, about 20% before it, about 10% after it.** This is the most directly testable Bressert-attributed number. Note that "cycles as a low detector" is dead in this repo; this is a calibration of the band, not a strategy. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 12:23](https://youtu.be/mXf069WzS2g?t=743), [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 12:53](https://youtu.be/mXf069WzS2g?t=773)
- **The oscillator/cycle combination "gives you like a 70% probability"** (relayed; no definition of the outcome). [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 00:34](https://youtu.be/mXf069WzS2g?t=34)
- **Primary cycle averages 9 to 22 weeks. The alpha and beta sub-cycles are each "normally half" the trading cycle.** Relayed as Bressert's. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 03:11](https://youtu.be/mXf069WzS2g?t=191)
- **Translation follows the larger cycle**: up → right translated with higher lows; down → left translated with lower lows. This is Bressert's, as relayed. "Translation as direction" is dead in this repo, so do not re-test it as is. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 06:13](https://youtu.be/mXf069WzS2g?t=373)
- **Indicator Factory: "11 winning trades and two losing trades"** (84.6% wins) from one chart picture. No instrument, timeframe, dates, settings, exits or costs. Not reproducible as stated. [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 03:06](https://youtu.be/Vq5iMzqDAeQ?t=186)
- **TQFM: no results spoken**, despite "Lets Test It!" in the title. [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 00:30](https://youtu.be/3KAdvlpHdck?t=30)
- **Jerome Bressert channel.** After a break outside the Oracle Band, price typically retraces "about 25 percent" of the way into it, sometimes to the middle, "quite often". Opening gaps outside the bands "typically run back into" them. Needs the band definition. [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 02:02](https://youtu.be/pEXJnwGJCmw?t=122), [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 04:37](https://youtu.be/pEXJnwGJCmw?t=277)
- **SwingTrading with Cycles.** A cycle low "is always going to drop below the 10 period moving average", on all timeframes. A late-weekly-cycle pullback "almost always bottoms below" it. [Stock Market Cycle Analysis 101, 2022-10-02, 23:19](https://youtu.be/wKIzZiLtuL8?t=1399), [Stock Market Cycle Analysis 101, 2022-10-02, 23:49](https://youtu.be/wKIzZiLtuL8?t=1429)
- **NetPicks.** DSS divergences appear earlier than raw-stochastic divergences. [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 03:33](https://youtu.be/r7a4sDuM_7M?t=213)

## Dated calls (for later scoring)

The web pages make no calls. The videos make two sets.

| Video date | Asset | Direction | Level / target / invalidation | Horizon stated | Citation |
|---|---|---|---|---|---|
| 2016-02-02 | E-mini S&P (ES), intraday | Down (sell the rally) | Not buying, despite a detrend-oscillator low and a cycle-low band. Expects a rally to set up a new sell. No levels spoken | that session | [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 03:35](https://youtu.be/pEXJnwGJCmw?t=215), [Sample Live Chart Using the HAL Cycle Finder Trading Software, 2016-02-02, 04:06](https://youtu.be/pEXJnwGJCmw?t=246) |
| 2022-10-02 | DXY (yearly, 15-year cycle) | Up longer-term; a pullback first | Year 14 of a 15-year cycle (the last one bottomed in year 16; this one could run to year 17 or 18). Expects it to "at least kind of sort of test" ~120 (DXY ~112 now) after a weekly-cycle pullback. Possibly "another seven eight nine months of still going higher" before the long-term high, and a big decline after that | months to years | [Stock Market Cycle Analysis 101, 2022-10-02, 14:30](https://youtu.be/wKIzZiLtuL8?t=870), [Stock Market Cycle Analysis 101, 2022-10-02, 15:33](https://youtu.be/wKIzZiLtuL8?t=933), [Stock Market Cycle Analysis 101, 2022-10-02, 16:05](https://youtu.be/wKIzZiLtuL8?t=965) |
| 2022-10-02 | DXY (3-year cycle) | Up, then decline | Month 20 of 36. Could run to about month 25, then decline 10 to 12 months | ~5 months up, then ~1 year down | [Stock Market Cycle Analysis 101, 2022-10-02, 16:38](https://youtu.be/wKIzZiLtuL8?t=998), [Stock Market Cycle Analysis 101, 2022-10-02, 18:12](https://youtu.be/wKIzZiLtuL8?t=1092) |
| 2022-10-02 | DXY (weekly) | Down into a weekly cycle low, then up to ~120 | Week 17 swing high set up. Expects a move below the candle low at 112.173 and a hold around 107 to 108 ("much lower than that … concerning"). If it holds, "this thing is probably going to 120". The next weekly cycle takes it to 120 | early to mid October 2022 for the low | [Stock Market Cycle Analysis 101, 2022-10-02, 21:47](https://youtu.be/wKIzZiLtuL8?t=1307), [Stock Market Cycle Analysis 101, 2022-10-02, 23:49](https://youtu.be/wKIzZiLtuL8?t=1429), [Stock Market Cycle Analysis 101, 2022-10-02, 24:20](https://youtu.be/wKIzZiLtuL8?t=1460) |
| 2022-10-02 | DXY (daily) | Shallow pullback / rally | Day 11 of the daily cycle. A bullish reversal tried on day 13. With "downside expected for the market", the dollar may rally | days | [Stock Market Cycle Analysis 101, 2022-10-02, 22:19](https://youtu.be/wKIzZiLtuL8?t=1339), [Stock Market Cycle Analysis 101, 2022-10-02, 22:49](https://youtu.be/wKIzZiLtuL8?t=1369) |

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

Added from the videos (each attributed to its presenter):

9. **DSS level re-cross with a trend filter** (Indicator Factory, The Forex Geek, NetPicks).
   - Long: DSS[t−1] ≤ L and DSS[t] > L, taken only when the trend is up.
   - Short: DSS[t−1] ≥ 100−L and DSS[t] < 100−L, taken only when the trend is down.
   - L = 20 (The Forex Geek, Indicator Factory) or 30 (NetPicks). The trend filter is undefined in all three; pre-register close above a rising SMA(200), and also run with no filter.
   - Settings: 13/8 (The Forex Geek's stated default = the terminal's shipped default) and 10/9.
   - This is distinct from candidate 3's hook (a turn below 20) and from the trigger cross.
   - [DSS Bressert Indicator for MT4 & MT5, 2026-01-12, 01:04](https://youtu.be/kHrmwew2sSQ?t=64), [Best Scalping Strategy l Full Result B DSS Bressert indicator, 2022-11-22, 02:06](https://youtu.be/Vq5iMzqDAeQ?t=126), [Unlock Profitable Trades with the Double Smoothed Stochastic Oscillator, 2023-07-20, 03:03](https://youtu.be/r7a4sDuM_7M?t=183)
10. **TQFM "trend inside a trend"** (plain Stochastic 20 and 200; terminal Stochastic).
    - Long: Stoch200 > 80 (the "top"; 80 is pre-registered, since no number is given) and Stoch20 turns up after a dip.
    - Exit when Stoch20 > 80 again. Abandon when Stoch200 crosses below 50.
    - Short: the mirror (Stoch200 < 20, Stoch20 turns down after a bounce, exit at Stoch20 < 20, abandon on Stoch200 > 50).
    - The presenter's long wording is muddled, so this is the most literal computable reading.
    - [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 01:30](https://youtu.be/3KAdvlpHdck?t=90), [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 02:31](https://youtu.be/3KAdvlpHdck?t=151), [Double Stochastic Indicator Trading Strategy, Lets Test It!, 2023-10-25, 03:04](https://youtu.be/3KAdvlpHdck?t=184)
11. **10-period-MA undercut as a low gate** (SwingTrading with Cycles). A candidate cycle low counts only if low < SMA(10) on that timeframe. It is confirmed when a later bar's high exceeds the high of the low bar (his swing-low definition). The stop goes under the low bar.
    - Descriptive check first: the share of confirmed swing lows that undercut SMA(10). He says "always".
    - Then: do undercutting swing lows outperform the others as long entries? Exposure-matched.
    - [Stock Market Cycle Analysis 101, 2022-10-02, 23:19](https://youtu.be/wKIzZiLtuL8?t=1399), [Stock Market Cycle Analysis 101, 2022-10-02, 25:21](https://youtu.be/wKIzZiLtuL8?t=1521)
12. **Swing-high plus a late-count short** (SwingTrading with Cycles). On weekly bars, at week ≥ 17 of the weekly count: a bar closes below the low of the bar that made the highest high → expect a pullback that ends below the weekly SMA(10). Measure how often the pullback reaches below SMA(10). He uses this in his DXY call. [Stock Market Cycle Analysis 101, 2022-10-02, 20:16](https://youtu.be/wKIzZiLtuL8?t=1216), [Stock Market Cycle Analysis 101, 2022-10-02, 23:49](https://youtu.be/wKIzZiLtuL8?t=1429)
13. **70/20/10 timing-band calibration** (Bressert, relayed by CrusherHODL). Descriptive only.
    - Using confirmed daily cycle lows, build a band from the trailing mean length (pre-register ±15% as the "band").
    - Measure the share of the next low inside, before and after the band. Compare with 70/20/10.
    - It does not propose a trade, because "cycles as a low detector" is dead.
    - [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 12:23](https://youtu.be/mXf069WzS2g?t=743)
14. **Alpha-low failure as a veto** (CrusherHODL's own). If the half-cycle (alpha) low breaks below the daily cycle low, mark a cycle failure and suppress longs until a new DCL is confirmed. The labelling needs a cycle-segmentation rule, so this is partly retrospective. [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 14:23](https://youtu.be/mXf069WzS2g?t=863)
15. **Bressert's seasonal windows** (relayed). For example, gold in bull years bottoms in Q2 and tops in Q1, and S&P lows fall in Sep to Nov. Testable as a calendar-month return table per asset. The wording is garbled, so pre-register the windows exactly as listed under "Seasonal tendencies". **Needs long history; it is weak evidence at best.** [Cycle Theory Trading (Walter Bressert) Crypto Chapter 1, 2024-12-22, 09:50](https://youtu.be/mXf069WzS2g?t=590)

## Not usable / vague

- **"Dominant cycle low is due"** (LuxAlgo): no definition of the dominant cycle or of "due". It needs a cycle-length estimate (see candidate 4) that the page does not give.
- **Timing-band projection** (RSI3M3+): no formula or tolerance is given. The "projected cycle high timing" for partial profits is undefined.
- **"Slightly below" the cycle low**: only the script's 99% makes it computable. This is the script author's choice, not stated as Bressert's.
- **StrategyQuant's "DSS crosses UP/Down" and "DPO is rising/falling"**: what is crossed and what DPO means are not stated on the page. The code zip was not read.
- **"Higher volatility markets may need wider overbought/oversold levels"** (RSI3M3+): no rule for how much wider.
- **Attribution.**
  - TC2000 says Bressert developed it.
  - StrategyQuant and Indicator Factory say Blau and Bressert, one after the other, in two versions.
  - NetPicks credits Blau alone and describes Blau's construction.
  - LuxAlgo says attribution "is best kept loose".
  - None of these is a primary source.
- **The HAL Cycle Finder** (Jerome Bressert channel): the Oracle Band, cycle-low bands, the three lines and the detrend oscillator are proprietary and undefined. The "25% retrace into the band" claim **needs data we do not have**.
- **Bressert's half-cycle trades** ("plays the half cycle low", shorts the alpha high): the half-cycle low needs a hand-marked cycle structure. Retrospective / not computable as stated.
- **The mid-cycle as a third translation type**: CrusherHODL's own addition, with no rule beyond "the high is at the midpoint".
- **The seven trading-cycle components**: a vocabulary, not a rule.
- **Indicator Factory's "11 winners, 2 losers"**: no instrument, timeframe or period, so it cannot be reproduced or scored.
- **Jerome Bressert's "82 and a half"**: unclear whether this is an oscillator value or a price.
- **CrusherHODL's 60-day ± 10% daily cycle**: from "most people" and Loukas, not Bressert. It conflicts with the five rules' 15 to 30 days. Do not merge them.
