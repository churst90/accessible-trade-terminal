# Setup grader for the Cipher B Buy dot: pre-registration

> **2026-10-31: the grader no longer runs.** Its rules read Cipher B parts that were removed that day
> because Market Cipher B does not draw them (the divergences and the red Overbought Crossover). `setup-grader` now refuses and exits 2;
> to re-run it, build commit 2e86d377. The results stand.
>
> **2026-10-18: results are in `docs/SETUP_GRADER_FINDINGS.md`.** Verdict: No edge in both classes;
> grade A had 0 (crypto) and 2 (equities) development-era triggers, so its tests were not testable.
> Nothing below this line was edited after results existed.

Written 2026-10-18, before any result existed. This is stage 1 of 2. Stage 2 (a different agent)
builds and runs what is written here. **This file was committed before any forward return, win rate
or trade outcome was computed for any rule in it. The commit time is the proof.** No backtest was
run while writing it, and no outcome was looked at. The only data work was listing which files and
fields exist in `strategy-lab-data/`.

## Why this exists

Cody's point: the lab's strategies are too mechanical. They are one trigger, one or two yes-or-no
filters, and an ATR stop with a 2R to 4R ladder and a trail. Discretionary traders work differently:

1. **Context first:** the higher-timeframe trend, where we are in the cycle, the macro backdrop.
2. **Then location:** is the signal at a level that matters?
3. **Then the trigger.**
4. **Then confirmations.**
5. **Some conditions are vetoes.** They override everything else.

This document turns that hierarchy into exact, computable rules for the Cipher B Buy dot and its
mirror sell setup. It also fixes the tests and the verdict rules now, so a null cannot be renamed
afterwards.

## What I had already seen when writing this

Stage 2 must read this section, because it limits how blind the test is.

- **Ledger row 8, the dip filter: Buy dot plus close above SMA(200), against random entries with
  the same filter.**
  - With the refitted Cipher B (2026-10-17) its excess over random is +0.145R.
  - It keeps only 29% of that at 25% noise injection, and two of four eras show nothing.
  - It is Fragile (`docs/POLARITY_AND_GATE_FINDINGS.md`, `reports/cipher_strategies_rerun_REPORT.md`
    section 3).
  - The short side (red dot) showed nothing.
- **The 2026-10-17 Cipher A battery** (`cipher-a-battery`). No Cipher B reference cell survived the
  family-wise null; the best was pFW 0.38. The K1 finding matters here: a Buy dot essentially never
  fires while the daily EMA 11 is above the daily EMA 33. So **context has to come from a higher
  timeframe**. A same-timeframe "uptrend" filter is almost always off at a Buy dot.
- **Firing count:** the Buy dot fires about 346 times on the ten crypto daily series (rerun report,
  section 3).
- **Standing nulls I knew:**
  - Price-derived confluence does not stack (ledger row 16).
  - How price approaches a level does not predict whether the level holds; real levels behave like
    random lines (row 20).
  - Crowding from funding and open interest is a null (row 12).
  - Fear and Greed is redundant with oscillator buys (row 18).
  - Fixed R targets, ATR trails and time stops have no skill against random exits. The signal
    exit has skill in crypto only (row 6, `docs/EXIT_FINDINGS.md`).
  - Cosasverdes' equidistant and origin lines failed their surrogate tests.
  - Camel and Lucas cycle counts are a detector artifact (`docs/CYCLE_FINDINGS.md`).
  - Translation is momentum in cycle words (`docs/TRANSLATION_FINDINGS.md`).

None of the thresholds below was chosen from those numbers. Where a number comes from no source, it
says so. The dip filter is carried as a named reference row (section 6.6) because it is the cheap
alternative the grader has to beat.

---

## 1. Sources and the rules each one supports

Downloads are in the session scratchpad under `playbook/` and are not committed:

- **Prose transcripts:** `playbook/txt/`.
- **Raw subtitles:** `playbook/dl/<source>/`.
- **Whisper transcripts of the eight local tutorial videos:** `playbook/videos/t1.txt` to `t8.txt`.
  These were made with Whisper large-v3-turbo from the repo's `video content/` folder.

### 1.1 The eight tutorial videos in `video content/`: Cosasverdes ("Tim")

**Whose they are.** Tutorial 8 names the trading style as used "within the cosas verdes community
and within my own personal trading style". Tutorial 1 signs off "you just got timmified", and the
other videos sign off much the same way. Several more things match the public cosasverdes YouTube
channel:

- origin lines;
- channels;
- the BEPRO examples;
- videos such as "ORIGIN LINES: Bitcoin Price Technical Analysis BEPRO", https://www.youtube.com/watch?v=ZnAm-o3b-sk.

**When they were made.** Tutorials 1 and 3 say they were recorded "december 18th 2021". Tutorial 4
mentions "beyond february 2022". Tutorial 6 refers to February and March 2022. Tutorial 8 mentions
a bounce from 15,500 "a couple months earlier", so it is about early 2023.

**Their status.** They are tutorial-library videos with no public URL. Tutorial 8 asks "why did you
pay money for this", so at least that one was sold. They were in the repo already; I fetched
nothing.

**Prior record.** This repo already tested this author: "all four claims failed surrogate testing;
the origin grid collapsed out of sample" (`docs/PLATFORM_AND_SIGNAL_SERVICE.md`). So **origin lines
(tutorial 6) and channels (tutorial 7) are not used here.** They are hand-fitted parallel and
equidistant line families, anchored by eye on finished charts. That is the retrospective-selection
trap, and the idea is already dead.

The rules these videos do support:

**Trend (tutorial 3, "Let the trend be your friend").**

- *"you must have both a higher local high and a higher local low"*
- *"you typically specifically use candle bodies … the closures and openings of that time frame to
  establish higher highs and higher lows"*
- *"if you have one without the other … that would be essentially your definition of a no trend"*
- *"the very first candle closure under that line … you now broke your daily uptrend … you did not
  have to wait for a turnaround"*
- *"once a day closes a candle above here then we're in an uptrend"* (said where a higher low
  already exists)
- Bitcoin governs altcoins: *"it's bitcoin's trend is broken it doesn't matter what your coin is
  doing … if you're looking to buy an altcoin when the daily trend is broken … you're making
  potentially a mistake"*
- How to act in each state:
  - in a downtrend, *"any pump I might actually want to short"*;
  - with no trend, *"take profits more frequently"*.

This is the most mechanical rule in the whole source set, and it becomes the context layer below.

**Cycle position (tutorial 4, "Identifying where you are in the crypto cycle", and tutorial 5,
"Portfolio Management").** All of it is on the weekly chart.

- *"put on the 10 and 21 emas of the week … is the 10 above the 21 … if it's below it don't think
  about being an investor and buying stuff yet"*
- *"if the 10 ma is above [the 10 ema] then it's probably one of those times"* (a bad time, even
  with the EMAs bullish)
- *"if the 21 ma is on top [of the 21 ema] it's very likely … down for over a year"*
- What to do with it (tutorial 5):
  - *"Once you get a warning sign … sell half of each of the coins"*;
  - *"when the 10 EMA goes below the 21 and this band flips red, that's when you sell the second
    half"*.
- He also mentions a monthly Accumulation/Distribution slope. It is not used here; section 2.2
  says why.

**Location and selling (tutorial 2, "Horizontals").**

- *"the very strongest horizontals are where price turns on a dime and price doesn't go back to that
  point for a while"*
- *"if a bunch of candle bodies are connected, those are stronger"*
- *"If the daily trend is down, you can't quite do the same"*
- *"I simply want to sell where I have a statistically good chance to buy back lower within a
  relatively short timeframe"*
- *"typically I only sell a line twice"*
- Weekly horizontals are the strong ones; daily lines are weaker.

**Moving averages (tutorial 1).** *"ma's crossing above ema's is a bearish sign particular to larger
time frames"*. He uses 10/10 and 21/20 pairs, and recommends the 12-hour and 2-day charts.

**Reaction at a level (tutorial 8, "Micro Candle Ricochet").**

- At any level that is *"truly in play"*, *"your ricochets or the pure rejections get a full two
  points"*.
- A pierce-and-reclaim (*"big guy on a trampoline"*) gets one point.
- Passing straight through (*"samurai sword"*) gets nothing.

This supports requiring that the trigger bar closes back at or above the support level. The point
system itself runs on 15-second charts and cannot be reproduced on daily data. It is not used.

### 1.2 Camel Finance (from this repo's own write-ups)

Sources: `docs/CAMEL_TRACK_RECORD.md` (in the main checkout; its videos are dated August 2026) and
`docs/CAMEL_CYCLE_LIVE_READ.md` (https://www.youtube.com/watch?v=JoRNW_4jCVc, analysed 2026-08-04).

- **A checklist with a veto.** The cycle-low confirmation checklist has items 1 to 7 as a
  confluence score:
  - a swing low in its window;
  - back above the 10 SMA;
  - a DSS Bressert bullish cross out of oversold, *"ideally with divergence"*;
  - a trendline break;
  - a green thrust candle;
  - a Parabolic SAR flip;
  - a close on the cycle's own timeframe.

  Item 8 is a veto over all of them: *"you can't be a bull and cheer on a bullish uptrend without a
  succession of higher highs and higher lows."* That is the same structure this document uses:
  a score, gated by a veto.
- **The stop goes under a low.** *"we're not searching for a number to buy. We are searching for an
  area on the chart to manage risk around"*. Also: *"We're looking for the high probability low to
  manage the risk around."* The invalidation level is named in advance.
- **His regime gate is not in our data.** It uses high-yield credit spreads, the 10y−2y curve and
  the 10y−3m curve, so it is not testable here (section 2.1).

### 1.3 Market Cipher, the vendor (Crypto Face and the official channel)

The public videos used:

- "Intro to Market Cipher: Everything You Need to Know", 2019-06-17, https://youtu.be/bxkm4Kjubqs.
  - *"these green dots technically you can call them long signals but i want you to think of them as
    bottoms"*
  - *"when it's thick and green … you are looking for these green dots"*
  - *"always avoid trading when the money flow index is thin"*
  - *"if you see a yellow x on the same candle as a green dot the yellow x takes precedent … just stay
    out or short"*
  - *"the larger time frames the stronger the indication"*
  - Exit *"when the white is cutting into the blue"* on the opposite side.
- "Market Cipher Basic/Core Strategy", 2023-08-03, https://youtu.be/OscnxGwvAWs.
  - *"a fat deep initial wave we want to go at least below this white line and dotted line … then
    the trigger wave should be noticeably smaller"*
  - *"I want a smooth transfer of money"* (money flow)
  - Exit at red diamonds or a blood diamond.
- "How to Use Market Cipher Core Strategy CORRECTLY", 2023-11-25, https://youtu.be/sJbamrt-Cdc.
  - The failed setups had a *"one hour money flow coming straight down … did not have the higher
    time frames backing it up"*.
  - A winner taken while money flow was still falling *"kind of got lucky"*.
  - *"you always want to have your higher time frames backing up your lower time frame entries"*
- "Extreme overbought/oversold conditions w/ MarketCipher B", 2021-04-04,
  https://youtu.be/KKHCyMxrgVw. Above +60 on a high timeframe is acceleration, not a sell:
  *"wait for the oscillator to actually cross back over that 60 threshold"*.
- "Divergent price action w/ MarketCipher B", 2021-02-28, https://youtu.be/2IhGZje4TQ8. A bullish
  divergence in a bear market or on too small a timeframe *"might not play out"*.
- Earlier report (`market_cipher_family_REPORT.md`): the vendor site marketciphertrading.com
  describes momentum on a −100 to 100 range with thresholds at ±60.

### 1.4 Market Cipher educators

- **Trade Pro**, "Insanely Profitable New Proven Trading Strategy | VuManChu Cipher B", 2021-06-25,
  https://youtu.be/3RvKWdnrs2g.
  - Longs only above the 200 EMA.
  - A pullback into the 50 EMA.
  - *"only take a long position if money flow is in the green"*, and the waves below zero.
  - Stop *"just below the swing low targeting two times the risk"*.
  - Coded by Trading with DaviddTech, 2021-09-17, https://youtu.be/OSupc_S0sJY.
- **Trader Geo.**
  - "Beginner Market Cipher B Trading Strategy | Cheat Sheet Entry", 2023-09-08,
    https://youtu.be/qC9JMB4nmHI:
    - *"the only two that are absolutely non-negotiable is the money flow must be green and the
      Green Dot must be below the zero line"*;
    - stop *"under the low"*;
    - exit at *"the first Divergence on the time frame that you entered"*.
  - "EASY Market Cipher Strategy", 2023-06-02, https://youtu.be/2QGTGSXjHi4: the execution
    timeframe is the environment timeframe divided by 5 to 10.
  - "The Two Ways To Use Market Cipher B", 2024-11-01, https://youtu.be/Uril5gdDpO0: *"I put Market
    Cipher away until I'm at a key level"*.
- **Trading Bureau.**
  - "MASTERING MONEY FLOW", 2026-03-03, https://youtu.be/90kAAraFOS4: *"the green dot printed below
    the 45 degree line, our VWAP was crossing up but our money flow was clearly trending in a
    downwards direction … we did not take the trade"*.
  - "MARKET CIPHER TRIGGER WAVES", 2025-11-26, https://youtu.be/q4UdlkQScmg: stop beyond the swing
    with *"a little bit of buffer"*; most of the position off at 1:1.
- **Dylan Waves**, "This Is What Market Cipher Is Known For (Basic Strategy)", 2023-11-24,
  https://youtu.be/sYJD_E6pXV4. *"if a wave gets past the -60 line … I will consider it an anchor
  wave"*. The trigger is smaller with a higher low. The exit comes when money flow gets thick in the
  green and price slows.
- **Gunslinger Trades**, "The 4/24 Market Cipher Strategy", 2022-03-29, https://youtu.be/BozOB9RMbxs.
  The setup *"only works in trending markets"*. The stop goes *"below the last wick"*.
- **Frankie Candles**, "Market Cipher For Beginners!", 2024-07-17, https://youtu.be/QqeLqy2z9yg.
  Money flow is *"a filter into whether you should be looking for Longs or shorts"*. Dots alone are
  noise.
- **Gainium blog**, "VuManChu Cipher B indicator explained", Ares Sanchez, 2026-04-06,
  https://gainium.io/blog/vumanchu-cipher-b-indicator-explained.
  - Long when the waves are below −60 and money flow shifts from red to green.
  - Stop below the recent swing low.
  - Target the next resistance or a red dot.
  - *"Only take Cipher B buy signals when price is above the 200 EMA"*.

Also read and found to add no new dot rules: the vendor's DBSI videos
(`scratchpad/dbsi/dl/yt_*`), AlexOCrypto (https://youtu.be/l1MhxBW7gQ0) and Chart Wis
(https://youtu.be/GbT7iCVdaq8, probably a re-voiced vendor tutorial).

### 1.5 Where the sources agree, and what was not obtained

**Agreement:**

1. The Buy dot is a bottom signal, not a trend call.
2. Money flow is the main filter. Do not buy while it is red and still falling.
3. The higher timeframe has to back the trade.
4. Buy at a key level, not in the middle of nowhere.
5. Stops go just below the swing low, with a small buffer.
6. Exits are the opposite wave cross, a bearish divergence, money flow rolling over, or the next
   resistance.

**Not obtained:**

- Trader Geo's 93-minute "Complete Tutorial" (DYW_M-hoOn8): YouTube returned HTTP 429 three times.
- No vendor video gives a numeric stop or target.
- No source defines "thick" or "smooth" money flow, or how deep an anchor must be, beyond "past
  ±60".
- The cosasverdes videos have no public URL to cite.

---

## 2. What the lab can compute (inventory, read-only)

### 2.1 Data that exists in `strategy-lab-data/`

Listed by reading file headers only. No returns were computed.

**Crypto daily, Bitstamp OHLCV:**

| Asset | First bar |
|---|---|
| BTC | 2011-08-18 |
| XRP | 2016-12-16 |
| LTC | 2017-06-16 |
| ETH | 2017-08-16 |
| BCH | 2017-12-04 |
| ADA | 2021-11-17 |
| SOL | 2022-08-18 |
| DOGE | 2022-12-21 |

All end on 2026-06-15.

**Other crypto:**

- MEXC KAS daily and 4h (from 2022-09-27) and TAO daily and 4h (from 2023-03-03), both to
  2026-04-27.
- Bitstamp 4h: BTC (from 2017-04-30), ETH, XRP, SOL and DOGE. The files are capped at 20,000 bars.

**Equities and ETFs, Yahoo daily OHLCV, to 2026-07-24.** 33 series:

AAPL, CAT, CVX, DIA, EEM, EFA, FXI, IBM, IWM, JNJ, JPM, KO, MCD, MMM, MSFT, PFE, PG, QQQ, SPY, T,
VTI, VZ, WMT, XLB, XLE, XLF, XLI, XLK, XLP, XLU, XLV, XLY, XOM.

Also present: GLD, SLV, USO, TLT, IEF (Yahoo), and Twelve Data SPY, QQQ and XAU/USD, which
duplicate the Yahoo instruments.

**Cross-series feeds:**

- Binance perpetual funding (8-hourly, from 2020) and open interest (daily, from late 2021) for
  ADA, BNB, BTC, DOGE, ETH, LTC, SOL and XRP.
- Fear and Greed daily from 2018-02.
- CoinMetrics on-chain for BTC, ETH, LTC and XRP.
- CFTC COT weekly.

**Events:**

- FOMC dates, 2000 to 2027, with a scheduled flag but **no rate decision or direction**.
- CPI, NFP, PPI and GDP release dates.

**Missing for the macro layer Cody named.** There is no FRED value series:

- no fed funds rate;
- no Treasury yields or curve;
- no dollar index;
- no credit spreads.

FRED here means release dates only. **So "Fed easing versus tightening", "dollar trend", "credit
spreads" and Camel's curve gate cannot be computed from this snapshot.** Section 7.3 says what
stands in for them, and the rule that stops them being added mid-study.

### 2.2 Lab facts stage 2 must respect

From a read of `AccessibleTrader.StrategyLab`, `AccessibleTrader.Sdk` and `AccessibleTrader.Core`.

**Cipher B component names** (`CipherBProvider.cs`):

- `Oversold Crossover`: the Buy dot. In MCB mode (the default) it fires on every upward cross of
  the fast wave over the slow wave with the slow wave (Wave Trend 2) at −60 or below.
- `Overbought Crossover`: the red dot. **It is our own gated rule, not MCB's.** It needs a cross
  down with the fast wave above +53, held for 2 bars, with a cooldown and anchor suppression. It
  has no MCB mode. Section 4 deals with this.
- `Wave Trend` (WT1), `Wave Trend 2` (WT2) and `Money Flow Wave`. MCB money flow is 180 × SMA(60)
  of Heikin-Ashi body over range, centred on zero.
- `WaveTrend Cross Bull` and `WaveTrend Cross Bear`.
- `Bullish Divergence`, `Bearish Divergence`, `Hidden Bull Continuation` and
  `Hidden Bear Continuation`. All four are already moved forward to the bar where they can first
  be known.
- `Anchor Wave` is a 5× slower wave on the same bars, **not a higher timeframe**. It is not used.

**Higher timeframes.** **Do not use `ConditionLeaf.Timeframe`.** The lab wires the multi-timeframe
service as null. Worse, the app's `HtfLastClosedIndexExclusive` treats a weekly bar as closed once
it has *opened*, so a Wednesday sees that week's final values. That is look-ahead. Stage 2 must
build weekly bars from daily bars itself and use only completed weeks, the way
`WeeklyPersistenceCommand.ToWeekly` does.

**Exits.** The backtest engine has **no exit-condition tree and no time stop**. Signal, condition
and time exits have to be written in a lab harness, as `ExitCommand` does.

**R accounting caveat.** The engine's per-exit-row R uses the *current* stop. After a breakeven or
trail move, later rows have zero or tiny risk and are dropped or blown up. **Stage 2 must compute
R per position:** the sum over exits of (portion × (exit − entry)) ÷ (entry − initial stop).

**Costs.** The engine's defaults are 10 bps commission plus 5 bps slippage per fill.

**The standard R harness** (`RHarness` in `cipher-a-battery`):

- enter at the next bar's open;
- stop 1 × ATR(14) below entry;
- target 2R;
- exit at the 20th bar's close if neither is hit;
- the stop wins if one bar touches both.

**Seeds.** All seeds come from `StableSeed.From(...)`, never from `string.GetHashCode()`.

---

## 3. The Buy-dot grading hierarchy (exact rules)

Notation:

- *i* is the daily bar on which the Buy dot fires. Everything is known at bar *i*'s close.
- Entry is at bar *i*+1's open.
- ATR means ATR(14) on the trigger timeframe at bar *i*.
- "Weekly" means weekly bars built from the daily bars:
  - crypto weeks run Monday 00:00 UTC to Sunday;
  - equity weeks are calendar weeks ending on the week's last trading day.
- A daily bar may use only weeks whose last daily bar is on or before bar *i*. A week ending on bar
  *i* is usable on bar *i*.

**Undefined is not false.** A trigger where any input below is undefined (warm-up, missing feed) is
**excluded and counted**, never put in a grade. With weekly MCB money flow needing 60 weeks, most
assets lose about their first 65 weeks.

### 3.1 Layer (c), the trigger

`CIPHER_B.Oversold Crossover` fires on bar *i*: the MCB rule, a WT1 cross above WT2 with WT2 at
−60 or below. Only the onset bar counts, meaning fired on *i* and not on *i*−1. That never differs
for a cross, but it is stated so it is pinned.

### 3.2 Layer (a), context

**(a1) Weekly trend from candle bodies (cosasverdes, tutorial 3).** On weekly bars:

- The body top is max(open, close). The body bottom is min(open, close).
- **Local high:** week *j* is a local high if its body top is strictly greater than the body tops
  of weeks *j*−2, *j*−1, *j*+1 and *j*+2. It is confirmed at the close of week *j*+2.
- **Local low:** the same, using body bottoms and strictly less.
- Each completed week, process in this order:
  1. Pivot confirmations.
     - When a new local high is confirmed, set HH to (new high > the previous confirmed high).
       Then make it the last high.
     - When a new local low is confirmed, set HL to (new low > the previous confirmed low). Then
       make it the last low.
  2. Close tests.
     - If the week closes above the last confirmed local high, set HH to true.
     - If the week closes below the last confirmed local low, set HL to false.
- Read the state:
  - **Up** = HH and HL.
  - **Down** = neither.
  - **No trend** = one without the other.
- The state is undefined until two highs and two lows have been confirmed.

Free choice: the span of 2 weeks. No source gives a number; he reads local highs by eye and rejects
"consolidation". Two is the smallest span that ignores single-week noise. It is frozen.

**(a2) Cycle phase from weekly moving averages (cosasverdes, tutorials 4 and 5).** On weekly
closes, compute EMA(10), EMA(21) and SMA(10):

- **Bull:** EMA10 > EMA21 and SMA10 ≤ EMA10.
- **Warning:** EMA10 > EMA21 and SMA10 > EMA10. This is the "white line on top" state.
- **Bear:** EMA10 ≤ EMA21.

For crypto, the phase is **always Bitcoin's** (Bitstamp BTC), because the source says Bitcoin
governs altcoins. For equities, the phase is the asset's own. This rule is from a crypto source;
applying it to equities is an extension, and it is the same rule. Two extras from the videos are
deliberately dropped to keep the free choices few:

- **Monthly A/D slope.** It needs single-venue volume and a slope window no source gives.
- **The "SMA21 above EMA21" box.** It is a duration forecast that sits inside Bear.

**Context grade (CG) for a long**, computed on every bar, not only at triggers:

| CG | Rule |
|---|---|
| Good | own weekly trend Up AND phase Bull |
| Bad | own weekly trend Down AND phase Bear |
| Neutral | anything else |

**(a3) Macro.** It is not part of the grade. The data for the macro rules the sources name does not
exist (section 2.1), and the stand-ins have nulls behind them. Macro is tested only as a
pre-registered modifier (section 7.3).

### 3.3 Layer (b), location

**Candidate support levels at bar *i*:**

- The body bottoms of confirmed weekly local lows **and** the body tops of confirmed weekly local
  highs (an old high can act as support). Both come from the (a1) detector, confirmed within the
  156 completed weeks before bar *i*.
- The weekly EMA(10) of the last completed week. This is the 10 EMA in cosasverdes' weekly set. It
  is close to Trade Pro's daily 50 EMA pullback (50 days is about 10 weeks).

**Location = yes** if any candidate level ℓ satisfies both:

- the lowest low of bars *i*−4 to *i* is within 1.0 × ATR of ℓ, above or below; and
- bar *i* closes at or above ℓ. That means the touch was a rejection or a reclaim (tutorial 8's
  "ricochet" or "trampoline"), not a pass-through.

If several levels qualify, ℓ* is the one nearest to that lowest low. It is used for the
invalidation stop in section 5.

Free choices, frozen:

- **The 5-bar window.** The dot often prints a few bars after the low. No number is given; Dylan
  Waves' trigger comes after the anchor.
- **The 1.0 × ATR band.** Sources say "doesn't have to be exact"; no number.
- **The 156-week lookback.** Three years. No number is given.

### 3.4 Layer (d), confirmations

There are three; count how many are true.

- **D1, divergence:** `Bullish Divergence` or `Hidden Bull Continuation` fired on any of bars *i*−5
  to *i*, at its knowable bar. Sources: Camel, "ideally with divergence"; Trader Geo, hidden
  bullish divergence.
- **D2, money flow curving up:** MF(*i*) > MF(*i*−1) > MF(*i*−2), on the daily `Money Flow Wave`.
  Sources: the vendor's "smooth transfer", Gainium's red to green, Trading Bureau. Free choice: two
  rises in a row is the smallest count that tells a curve from a single tick.
- **D3, higher timeframe backing:** the weekly `Money Flow Wave` (Cipher B in MCB mode on completed
  weekly bars) is rising, meaning the last completed week is above the week before. Source: the
  vendor's "higher time frames backing it up".

### 3.5 Layer (e), vetoes

Any one of these forces **grade V: no trade**, whatever the other layers say.

- **V1, higher-timeframe downtrend:** CG is Bad (weekly trend Down and phase Bear). Sources:
  - cosasverdes, "in a downtrend … any pump I might actually want to short";
  - Camel's criterion-8 veto;
  - Trade Pro and Gainium, no longs below the 200 EMA (expressed here in the weekly terms the
    trend sources use).
- **V2, daily money flow red and still falling:** MF(*i*) < 0 and MF(*i*) < MF(*i*−1). Sources:
  - Trading Bureau's "did not take the trade";
  - the vendor's "kind of got lucky";
  - Trader Geo's "non-negotiable".
- **V3, weekly money flow red and falling:** the weekly MF of the last completed week is below 0
  and below the week before. Source: the vendor's failed setups with the higher-timeframe "money
  flow coming straight down".

Considered and not used:

- **The vendor's yellow X on the same candle.** It cannot happen: our Yellow X is a bearish wave
  cross (100% overlap in the 2026-10-17 battery), so it can never share a bar with a bullish cross.
- **Event-day vetoes.** No source in this set gives one.
- **A daily level-break veto.** The weekly trend's close-below-the-last-low rule already covers it,
  and it would add three more free numbers.

### 3.6 Grades

Every non-excluded Buy dot gets exactly one grade.

| Grade | Rule |
|---|---|
| V | any veto (V1, V2 or V3) |
| A | CG Good, Location yes, at least 2 of D1-D3 |
| B | not A or V, and either CG Good with exactly one of {Location yes, at least 2 confirmations}, or CG Neutral with both |
| C | every other non-vetoed Buy dot |

This is a hierarchy, not a sum: grade A needs good context first, as Camel's master gate and
cosasverdes' trend rule require.

---

## 4. The mirror sell setup

**The trigger, and why it is not the shipped red dot.** The shipped `Overbought Crossover` is not
MCB's rule and is not the mirror of the Buy dot (section 2.2). So **the primary sell trigger is the
exact mirror of the buy rule:** `WaveTrend Cross Bear` on a bar where WT2 is +60 or above,
computed from the `Wave Trend 2` component. The shipped `Overbought Crossover` is run once as a
named alternate. It is counted in the budget and reported descriptively; it is never the basis of a
verdict.

**Mirror layers:**

- **Context (short):**
  - Good = own weekly trend Down AND phase Bear;
  - Bad = own weekly trend Up AND phase Bull;
  - Neutral = anything else.
  - For crypto the phase is Bitcoin's, as before.
- **Location (resistance):** the highest high of bars *i*−4 to *i* is within 1.0 × ATR of a
  candidate level, and bar *i* closes at or below it. The candidates are the same weekly
  local-high body tops and local-low body bottoms from the last 156 weeks, plus the weekly EMA(10).
- **Confirmations:**
  - `Bearish Divergence` or `Hidden Bear Continuation` within bars *i*−5 to *i*;
  - MF(*i*) < MF(*i*−1) < MF(*i*−2);
  - the weekly MF is falling.
- **Vetoes:**
  - short CG Bad;
  - daily MF above 0 and rising;
  - weekly MF above 0 and rising.
  - The vendor's "above +60 on a high timeframe is acceleration" warning is covered by the
    weekly-trend veto.
- **Grades:** the same A, B, C and V definitions, with the mirrored terms.

**Used twice:**

1. **As a short test.** These are section 6's secondary tests S3.
2. **As an exit for longs (X4).** A **"sell setup for exit"** is the mirror trigger plus either
   resistance location or at least 2 bearish confirmations. Context and vetoes are ignored here,
   because selling into a weekly horizontal in an uptrend is what cosasverdes' tutorial 2 teaches:
   a trim, not a short.

---

## 5. Exit variants, compared on the same graded entries

**Which entries.** The population is grade A and grade B long entries pooled, for each asset class.
Grade A is also reported alone, descriptively. Entry is bar *i*+1's open for every variant.

**The invalidation stop**, used by X2 to X5. Sources: Camel's "a low to put a stop under";
Trade Pro, Trader Geo and Gainium's "below the swing low"; Trading Bureau's "little bit of buffer".

- S_inv = (the lowest low of bars *i*−4 to *i*, or ℓ* if that is lower and Location is yes)
  − 0.25 × ATR.
- If entry − S_inv is less than 0.5 × ATR, use entry − 0.5 × ATR. This floor stops the costs in R
  from blowing up.
- Free choices: the 0.25 buffer and the 0.5 floor. No source gives a number. Both are frozen.

**The five variants.** X3 and X4 share a time cap of 60 bars. That is a free choice so that every
variant ends. R is always per position, with initial risk (section 2.2).

| Id | Exit rule |
|---|---|
| X1 | the lab's current style: ATR(14) × 3 stop; 50% off at 2R and 50% at 4R; after the 2R exit the stop trails at ATR(14) × 3 |
| X2 | the X1 ladder and trail, but the initial stop is S_inv and R is measured from it; only the stop changes |
| X3 | stop S_inv; exit at the close of the first bar after entry with `WaveTrend Cross Bear` (the entry signal reversed); 60-bar cap |
| X4 | stop S_inv; exit at the close of the first bar after entry with a sell setup for exit (section 4); 60-bar cap |
| X5 | stop S_inv; time exit at the close of bar *i*+20 |

There is no single "lab default" risk plan. The catalogue has ATR × 3 with 2R/4R and breakeven,
and ATR × 3 with 2R/8R and a trail. X1 is fixed as written above, matching Cody's description:
"ATR stop, 2R-4R ladder, trail".

**The control for every variant** comes from `docs/EXIT_FINDINGS.md`. Keep each entry. Draw a
random exit bar from that variant's own holding-period distribution (pooled per class) and keep the
variant's stop active until then. Use 1,000 draws. **Report:**

- each variant's mean R;
- its ratio to the random-exit mean;
- its p-value: the share of random books with mean R at least the variant's;
- every variant also in ATR units (return ÷ ATR at entry), so X1, whose R uses a different stop,
  can be compared with X2 to X5.

**Prior, stated now.** The ledger predicts no mechanical exit beats random. X5 is a time stop, so it
*is* the random control and should sit near 1.0. If X3 shows skill, it should be in crypto only.

---

## 6. Universe, timeframes, costs, eras and the controls

### 6.1 Classes

**Asset classes are never pooled together**, because of the polarity finding: crypto trends,
equities revert.

- **Crypto, primary.** Trigger on daily bars, context on weekly bars. Ten assets: BTC, ETH, LTC,
  XRP, BCH, ADA, SOL and DOGE (Bitstamp daily), plus KAS and TAO (MEXC daily).
- **Equities, primary.** The same timeframes, on the 33 Yahoo series in section 2.1.
- **Left out:**
  - GLD, SLV, USO, TLT and IEF: a different class, and too few to stand alone. TLT is used only as
    a macro input.
  - The Twelve Data duplicates: one provider per instrument.
- **Crypto 4h, secondary only (S4).** The trigger is the Buy dot on 4h bars, and ATR is on 4h. The
  context timeframe is daily:
  - the (a1) trend on daily bars;
  - the phase stays Bitcoin's weekly;
  - location uses daily local lows and highs from the last 156 days, plus the daily EMA(10);
  - D3 and V3 use the daily MF.

  Assets: BTC, ETH, XRP, SOL and DOGE (Bitstamp 4h), KAS and TAO (MEXC 4h).

### 6.2 Outcome for the grading tests

Use the standard R harness (section 2.2) so results line up with ledger row 8:

- next open;
- 1 × ATR stop;
- 2R target;
- 20 bars;
- the stop wins ties.

**Costs.** Charge 15 bps per side (the engine's 10 bps commission plus 5 bps slippage), converted
to R as 2 × 0.0015 ÷ (ATR ÷ entry). The **net figure is primary**. The gross (0 bps) figure is
reported beside it.

### 6.3 Eras

- **Development era:** trigger bars before 2023-01-01. **All confirmatory tests run here.**
- **Held-out era:** 2023-01-01 to the end of the data. It is used for no choice. It is read once,
  after the development tests, for sign agreement only.

Most of SOL, DOGE, KAS and TAO falls in the held-out era after warm-up. That is acceptable: they
become out-of-sample assets.

### 6.4 The matched random control

For each graded trigger, the control value is the mean net R of **all** bars:

- on the same asset;
- in the same era;
- with the same context grade (CG) on that bar.

Each bar's R comes from the same harness, entering at the next open. This is an averaged random
entry, as the 2026-10-17 battery used, not a single draw.

- A trigger's **excess** = its R minus that control.
- A grade's **excess (Ex)** = the mean excess over its triggers.

Matching on CG is what makes the primary test ask "does the trigger and its grade add anything
inside the same context?" rather than "is the context good?".

### 6.5 Significance

**Within-test p-values.**

- **For Ex > 0:** a block permutation. For each asset × era × CG stratum, cut the bar sequence into
  consecutive 20-bar blocks (block = horizon). Permute the block order, carrying the trigger labels
  with their blocks, and recompute Ex.
  - 10,000 draws.
  - Seed = `StableSeed.From("setup-grader", test id, asset)`.
  - p is one-sided: (1 + the number of draws with null ≥ observed) ÷ (1 + 10,000).
- **For orderings (A versus C):** permute the grade labels among that pair's triggers within each
  asset × era stratum, with 10,000 draws.
  - Statistic: ExA − ExC.
  - Caveat: triggers less than 20 bars apart share outcome bars, so this p is optimistic. That is
    one reason the held-out sign check is required.

**Family-wise correction.** Use **Holm–Bonferroni at α = 0.05, one-sided, inside each family**
(section 7.5). Holm is valid under any dependence, so it is used in place of a joint max-statistic
across tests that use different permutation schemes.

### 6.6 Named reference rows

These are reported with every result and are not tests.

- **R0, the dip filter (ledger row 8):** Buy dots with the daily close above SMA(200), on the same
  sample and with the same excess method. Grade A has to beat its point estimate for the write-up
  to call the hierarchy better than the cheap alternative.
- **R1:** all non-excluded Buy dots, ungraded.
- **Counts:**
  - the number of triggers in each grade, each era and each class;
  - the number excluded as undefined;
  - for V, which veto fired.

### 6.7 Robustness

These are part of the verdict, not separate tests.

- **Noise injection.** Use the gate command's `Perturb` (Varma) at 25% of each series' own daily
  volatility, seeds 0, 1 and 2, on the development era. Rebuild everything: weekly bars, levels,
  grades and controls. **Retained** = the mean of noisy ExA ÷ clean ExA.
- **Breadth.** Report ExA per asset for every asset with at least 3 grade A triggers in the
  development era.
- **Data proof.** Record the sha256 of every input file before and after the run.

---

## 7. Tests and success criteria, fixed now

### 7.1 Primary family (8 tests, Holm inside the family)

The same four tests run for each of crypto daily and equities daily.

| Id | Test (development era, net R) |
|---|---|
| P1 | ExA > 0 |
| P2 | ExB > 0 |
| P3 | ExC > 0 |
| P4 | ExA − ExC > 0 |

**"The grader works" (Supported) in a class** needs all six of these:

1. P1 is significant after Holm.
2. P4 is significant after Holm.
3. The point estimates are monotonic, A > B > C, in both mean net R and Ex.
4. In the held-out era, ExA > 0 and ExA − ExC > 0, provided the held-out minimum counts (section
   7.4) are met.
5. ExA > 0 on more than half of the assets in the breadth list (section 6.7).
6. ExA keeps at least 50% of its value at 25% noise. This is the bar the 2026-10-17 re-run used.

**The other verdicts:**

- **Fragile:** criteria 1 to 5 hold, but criterion 6 fails.
- **Suggestive:** criterion 3 holds, but P1 or P4 is only nominally significant (p below 0.05
  before Holm), or grade A is below the confirmatory count.
- **No edge:** anything else (section 8).

### 7.2 Secondary family (12 tests, Holm inside the family)

| Id | Test | Count |
|---|---|---|
| S1 | vetoed triggers do worse than C: ExV < ExC, per class | 2 |
| S2 | location placebo: ExA with real levels exceeds 95% of 200 placebo-level draws, per class | 2 |
| S3 | mirror short: ExA > 0, and ExA − ExC > 0, per class | 4 |
| S4 | crypto 4h (section 6.1): ExA > 0, and ExA − ExC > 0 | 2 |
| S5 | inside good location: ExA against random bars matched on CG **and** Location (each bar's own lows tested against its levels), per class | 2 |

**S2 placebo.** Shift every candidate level by a random offset of 3 to 6 daily ATRs, measured at
the level's confirmation, with a random sign. Use 200 draws, seeded with `StableSeed`. Rebuild the
grades each draw. This is the "surrogate the detector" control: ledger row 20 found real levels
behave like random lines, so location has to beat placebo lines.

S1, S2 and S5 are one-sided in the direction stated. S3 and S4 are one-sided, positive.

### 7.3 Macro modifier family (5 tests, Holm inside the family)

**The population** is all non-vetoed Buy dots (A, B and C pooled), so there is enough sample.

**The statistic** is Δ = Ex(state 1) − Ex(state 0). Here a trigger's control is matched on asset,
era, CG **and** macro state. Each test is one-sided in the stated direction, which is the
discretionary prior that dip-buying works better in risk-on and easing backdrops.

| Id | Macro state 1 (as of bar *i*, last value on or before that date) | Classes |
|---|---|---|
| M1 | risk-on: SPY (Yahoo) daily close > SMA(200) of SPY | crypto, equities |
| M2 | rates easing (proxy): TLT (Yahoo) 126-trading-day price return > 0 | crypto, equities |
| M3 | crowded longs: the mean of the last 21 BTC 8-hour funding prints is **above** the 80th percentile of the trailing 1,095 prints. Predicted direction: Ex is **lower** when crowded | crypto |

**Notes:**

- M1 is self-referential for SPY itself. That is accepted and noted.
- M2 stands in for "Fed easing", which cannot be computed from the snapshot.
- M3's development-era data runs only from 2021 to 2022.
- **Fed funds direction, the dollar trend, credit spreads and the yield curve are NOT in this
  study.** They are not in the snapshot. Fetching them after seeing M1 to M3 would be a forking
  path. If Cody wants them, a separate dated pre-registration must be committed before any of them
  is fetched. The lab rule is that nothing is fetched during a study.

### 7.4 Minimum events and what to do when grade A is rare

- **Confirmatory:** each grade needs at least **30** triggers in a class in the development era.
  Assets are already pooled within a class.
  - With 15 to 29, that grade's tests are run but reported as **Suggestive only**. They cannot
    support a "Supported" verdict.
  - Below 15, the grade's tests are "not testable". The count is reported and nothing else.
- **Held-out sign check:** needs at least 10 triggers in that grade in the held-out era. Below that
  it is "not checkable", which caps the class verdict at Suggestive.
- **Fallback if grade A is below 30 in a class:** run one extra pooled grade "A∪B" against C, with
  the same P1 and P4 tests. It is **reported as Suggestive whatever its p-value**, and counted in
  the budget as 2 extra tests.

### 7.5 Exit family (10 tests, Holm inside the family)

Five variants times two classes. Each test asks whether the variant's mean R beats its random-exit
control (section 5), one-sided.

### 7.6 The full budget and the counting rule

**Pre-registered:** 8 primary + 12 secondary + 5 macro + 10 exit = **35 tests.**

**Pre-registered descriptive runs** count toward variantsTried but are not tests:

- the R0 and R1 reference rows;
- the shipped `Overbought Crossover` alternate (2 rows);
- the gross-of-cost figures;
- the three noise seeds;
- the per-asset breakdowns;
- the A∪B fallback if it is triggered.

**Counting rule.** Every run stage 2 makes beyond this list is counted in a `variantsTried` total
and listed in its report, with its result. That includes:

- a different span, band, window or level set;
- a different era cut or asset subset;
- a bug-fix rerun;
- an exploratory look.

**A bug fix that changes a pre-registered number** must report both numbers, the old and the new.
**Nothing outside this list may be used to reach a verdict.** Anything new is exploratory and has to
be labelled so in the same sentence as its number.

**Frozen numbers.** None of these may be tuned.

| What | Value | From |
|---|---|---|
| Buy dot level | WT2 ≤ −60 | the MCB rule as shipped |
| weekly pivot span | 2 weeks each side | free choice |
| phase averages | EMA 10, EMA 21, SMA 10, weekly | cosasverdes |
| level lookback | 156 weeks | free choice |
| touch window | bars *i*−4 to *i* | free choice |
| touch band | 1.0 × ATR(14) | free choice |
| divergence window | bars *i*−5 to *i* | free choice |
| money flow curve | 2 rises in a row | free choice |
| confirmations for A | at least 2 of 3 | free choice |
| stop buffer, floor | 0.25 ATR, 0.5 ATR | free choice |
| exit cap | 60 bars | free choice |
| outcome harness | 1 ATR, 2R, 20 bars | lab standard |
| costs | 15 bps per side | engine defaults |
| era cut | 2023-01-01 | free choice |
| minimum counts | 30 / 15 / 10 | free choice |
| noise bar | 50% kept at 25% noise | the 2026-10-17 re-run |
| permutation draws | 10,000; placebo 200; random exit 1,000 | free choice |
| block size | 20 bars | the horizon |
| M2 lookback | 126 trading days | free choice |
| M3 window, percentile | 21 prints; 80th of 1,095 | free choice |

---

## 8. What counts as "no edge", stated plainly

For a class, **the setup grader has no edge if any one of these is true:**

- P1 is not significant after Holm in the development era. Grade A does not beat random entries in
  the same context.
- P4 is not significant after Holm. Grade A is not better than grade C.
- The A > B > C ordering fails on the point estimates.
- In the held-out era, ExA ≤ 0 or ExA − ExC ≤ 0.

**If so, the verdict is: the hierarchy adds nothing to the bare Buy dot that this test can detect,
and the Buy dot inside good context is no better than a random bar in the same context.**

The null **cannot be reframed afterwards** by any of these:

- picking a different grade cut, threshold, span, band, lookback or confirmation count;
- dropping assets or eras, or switching from the development era to the full sample;
- moving from net to gross R, or from excess to raw R;
- switching to a different outcome harness or exit variant;
- promoting a secondary or macro test, or a subgroup found after the fact, to the headline;
- pooling crypto with equities.

**Other things that do not count as rescues:**

- **A good exit result does not rescue the grading.** The exits are compared on the graded entries;
  they are not evidence that the grades work.
- **A significant macro interaction on an ungraded population** is a statement about the Buy dot,
  not about the hierarchy.
- **Grade A merely beating the dip filter (R0) on point estimate** is not a pass if P1 or P4 fails.

**Also stated now, so it cannot be argued later:**

- If grade A beats C only because A's matched control is lower, the matching has done its job. The
  verdict is still about Ex, not raw R.
- If grade V turns out *better* than C (S1 fails in the other direction), the vetoes are wrong and
  the write-up must say so.
- Location that fails S2 means the weekly levels are placebo lines in this test, whatever P1 says
  about grade A.

## 9. Files for stage 2

- **Read first:** this file, `reports/cipher_strategies_rerun_REPORT.md` and
  `docs/EXIT_FINDINGS.md`.
- **Lab commands to reuse rather than reinvent:**
  - `cipher-a-battery`, for `RHarness`, the averaged gate control and block permutation;
  - `exits`, for the random-exit control;
  - `gate`, for `Perturb` noise injection;
  - `WeeklyPersistenceCommand.ToWeekly`, for completed-week resampling.
- **Put the results in** a new `docs/SETUP_GRADER_FINDINGS.md`. That document will need an
  `edges.json` record, because `EdgeRegistryTests` requires one for every findings document. **Do
  not edit this pre-registration after results exist**, except to add a dated pointer to the
  findings at the top.
