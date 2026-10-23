# Cipher C: cycle lows, after Camel Finance's CF Cycle Trading Indicator

Written 2026-10-21, when Cipher C was rebuilt. Cody: "Cipher c needs to be designed as close to the
cf indicator as possible from any and all screen grabs you can get from it… I want one robust cycle
detection indicator as I can get." The code is `CipherCProvider` and `CipherCEngine`; the tests are
`CipherCProviderTests`.

The CF indicator is closed source and Cody will not buy it, so this is a reconstruction from what
can be seen and read, in the way Cipher B was rebuilt from Market Cipher B. Read section 6 before
trusting any of it.

## 1. Sources

- **The vendor's public docs**: about 50 GitBook pages (current and the archived 1.0) and the
  cftrading.co.uk sales page. Already summarised, with links, in
  `AccessibleTrader.StrategyLab/Corpus/rules/camel-finance.md` (section "What his indicator
  computes") and `docs/CAMEL_METHOD.md` section 8.
- **86 images from those pages**, read one by one. One of them is the settings panel (section 3).
- **405 frames from Camel's YouTube videos** (16 source videos, including the vendor's feature
  demos), about one frame every 7 seconds around the moments he has the indicator on screen. The
  per-frame notes are in `strategy-lab-data/cf-frames/REPORT.md` (gitignored, like the frames; the
  videos were deleted after extraction).
- **The tutorial and cycles playlists**, searched for the indicator on other timeframes. Every chart
  with the indicator on it is daily. His weekly and monthly charts in those videos carry only his own
  drawings. **No weekly chart with CF on it, and no yearly cycle low, was seen anywhere.**

## 2. The sixteen assets

CF's "Finetuned Asset Detection" page links a public TradingView watchlist (182218276). Its page
lists the assets it is tuned for:

| Group | Assets |
|---|---|
| Indices | SPX, DXY, NDQ, DJI |
| Metals and oil | GOLD, SILVER, USOIL |
| Metal equities | GDX, GDXJ, SILJ |
| Crypto | BTCUSD, ETHUSD |
| Crypto equities | COIN, MSTR, MARA, RIOT |

CF's own cycle-range table has now been read for nine of the sixteen: SPX, GOLD, ETH, COIN, MSTR,
MARA, RIOT, GDX and BTC. SPX, gold and Ethereum came from the earlier screens. The other five, and a newer
gold table, were read by eye from reviewer videos of August 2025 (frames under
`strategy-lab-data/cf-frames2/frames/`, gitignored: `76LvfFq1e9M/5.jpg` COIN, `76LvfFq1e9M/200.jpg`
and `m3u0zvEi65I/5.jpg` MSTR, `76LvfFq1e9M/250.jpg` MARA, `76LvfFq1e9M/300.jpg` RIOT,
`76LvfFq1e9M/400.jpg` GDX, `HhZgHv6dhtM/40.jpg` and `Jexw-gt4jzA/100.jpg` gold).

Replacing the estimates did not make the lows agree better with CF's marks on those stocks. On
Coinbase, MicroStrategy, Marathon and Riot, using the Python prototype of the detector, the old
ranges found 9 of 21 of CF's daily lows within four days and CF's ranges find 8 of 21. Most of
those 21 dates were read off the chart by pixel spacing, graded guesses, so the sample cannot tell
the two apart. The numbers are used because they are CF's, not because they fit better.

Bitcoin's table (54–66 days, 22–42 weeks, 4 years) was read from two more reviewer videos of 7–9
August 2025 (`strategy-lab-data/cf-frames3/frames/s4EfttpMNXA/35.jpg`, `qVlnivWr2ho/0.jpg`). Camel
says 28–32 weeks on his 2026 streams; Cipher C follows the indicator's table. For COIN, MSTR, MARA and
RIOT, treating the daily values as trading days follows the TradingView stock-bar context; that is
an inference, not an explicit CF statement.

**All sixteen are now the original's own (2026-10-22, section 11).** The table the original draws
on its own TradingView chart was read for fifteen of them. It confirmed the nine above and replaced
the seven estimates:

| Asset | Daily (days) | Weekly (weeks) | Yearly (years) | Was |
|---|---|---|---|---|
| NDQ, DJI | 36–44 | 22–31 | 4 | yearly 3.5–4.5 |
| DXY | 15–24 | 16–20 | 3 | 15–25, 15–20, 2.5–3.5 |
| SILVER | 22–28 | 20–28 | 8 | the same |
| USOIL | 36–44 | 16–20 | 3 | 30–45, 18–26, 3–4 |
| GDXJ, SILJ | 22–28 | 22–26 | 8 | weekly 20–28 |
| ETH | 15–25 | 7–10 | 4 | yearly 3.5–4.5 |

SPX's table does not draw on TradingView's SP:SPX, so SPX keeps the ranges read off Camel's videos.
Crude oil's real ranges match the original's crude oil lows worse than the estimate did (0.68
against 0.76, exact-bar F1 in the Python prototype); they are used because they are the original's.
No preset says "estimated" any more.

## 3. What CF draws, and how many parts it has

Thirteen visible parts, plus the legend line (`CF Cycle Trading Indicator [camel] V-2 <value>`):

| # | CF | Cipher C |
|---|---|---|
| 1 | Green ▲ / "D" ring / camel under each daily cycle low | Green triangle with a D ring under the low (DCL) |
| 2 | Blue stack under each weekly cycle low, one row under the D | Bigger blue triangle with a W ring (WCL) |
| 3 | Yearly cycle low (described in the docs, never seen) | Bigger gold triangle with a Y ring (YCL) |
| 4 | ✅ on the bar a low is confirmed | Green rounded check box above the bar (DCL / WCL / YCL Confirmed) |
| 5 | 🔄 pending | Grey-blue rounded refresh box (Cycle Low Pending) |
| 6 | ❌ cancelled, in a green box | Red cross (Cycle Low Cancelled); the confirmation that follows says which cancelled low it replaces |
| 7 | Live confirmation marker above price | Same square as 4 |
| 8 | LOW / MID / HIGH confluence text | The Confirmed components' own value (1, 2, 3), spoken as low/mid/high |
| 9 | Cycle count "49D" | Printed over the low with a D / W suffix (49D / 24W) from the low's label companion; spoken with the low ("A 49 day cycle") |
| 10 | Green daily Future Cycle Low Range band | Daily Timing Window: the cycle's day count, green tint while inside its window (past only) |
| 11 | Blue weekly FCLR band | Weekly Timing Window: the week count, blue tint inside its window (weekly charts) |
| 12 | Yellow low-to-low arcs | The Timing Window's drawing (`_path`): drawn, named on landing, not a stop (see below) |
| 13 | Yellow SMA line | 10-bar SMA, the one Camel trades against |

Added, from the 1.0 docs rather than the frames: left, mid and right translated tops (triangles over
each finished cycle's top) and Cycle Failed (a close under a low after its minimum gap, the cycle
school's "failed cycle").

**The arcs (row 12) were removed on 2026-10-22 and came back the same day as a drawing.** Cody first:
"a component which is only visual isn't any use to a screen reader", so the arc stopped being a
component. Then: "if the arc is more akin to the chart formation lines and drawings, then leave it
as such, just a drawing". It is now the daily (or weekly) Timing Window's `_path` companion: drawn
under the candles in yellow (#D4A72C), one arc from each cycle low to the next and a live arc from
the last low to the expected cycle length; landing on the window says "Window, with the cycle arc";
Properties → Appearance → "Draw the cycle arc" turns it off. It is not a keyboard stop and not a
`,`/`.` stop: everything it shows (where each cycle bottomed, how long it ran, where the cycle is
now) is said by the cycle lows and the window's day count. **It is look-ahead**: an arc is drawn
once the low it ends on is known, and the live one moves as lows confirm (`CipherCProviderTests`
excludes it from the causal check and pins the look-ahead in its own test).

### What each part says (2026-10-22)

Cody, 2026-10-22: "Each component must hold value, simply hiding and muting it isn't what I want.
hiding and muting should be a user defined preferential function." So every one of the fifteen is
shown and heard by default, and each carries a number worth landing on. The numbers that used to be
hidden components (the counts, DCL Day, the window bounds, confluence, the replaced low, the arc
position) are now **companions**: arrays beside a component (`ComponentCompanions`), handed to its
speech, with no keyboard stop or sound of their own. Examples are from the Bitcoin daily test chart.

| Component | Its value | Said when landed on |
|---|---|---|
| SMA | the 10-bar average | the average, as any line |
| DCL / WCL / YCL | the low's price | "Daily cycle low, 64955.00. A 52 day cycle. Confirmed 6 days later." |
| DCL / WCL / YCL Confirmed | confluence, 1 to 3 | "Weekly cycle low confirmed, low confluence: the low of 59930.00, 8.7 weeks ago. It replaces the cancelled low of 80537.00." |
| Cycle Low Pending | the bar's close | "… reached oversold on day 50. Lowest low since the last cycle low: 65696.00, 45 days ago." |
| Cycle Low Cancelled | the low it closed under | "Daily cycle low cancelled: closed at 76244.00, under the low of 76600.00 from 28 days ago." |
| Cycle Failed | the cycle low it closed under | "Daily cycle failed: closed at …, under the cycle low of … from N days ago." |
| Left / Mid / Right Translated Top | the top's high | "Left translated top, bearish: high 126272.00, day 36 of an 82 day cycle." |
| Daily / Weekly Timing Window | the day (week) of the cycle | "daily cycle day 79, 13 days past its window"; Ctrl+Left/Right stops where the window opens and closes |

**Navigable or context.** A dated event on one bar is a component (a low, a confirmation, a
cancellation, a failure, a top). A state that lasts over many bars is one component with a value on
every bar (the timing window), whose Ctrl+Left/Right stops are its changes. A fact about the whole
chart (the asset preset and its ranges) is said by Alt+Shift+D and the series summary, not by a
component. Cancelled and Failed stay navigable, but they are warnings: neither is a buy or sell signal.

## 6. What it is not, and what is missing

- **Not an edge.** Camel's checklist showed none against shuffled prices in this project's tests
  (`docs/CYCLE_SCHOOL_FINDINGS.md`). CF's own SPX counts fall inside its stated 36–44 day window only
  5 times in 23 (22%), counting trading days: 22–64, median 43, and 17 of 23 within 30–50. Counted
  in calendar days, 1 in 23. The indicator follows the method; it does not prove it.
  Buying the confirmed daily low (section 8) is not a dependable edge either; the one thing that held
  out of sample is that, on stock ETFs, the cycle after a left-translated one does better.
- **The lows are drawn on their own bar and known only later**, as CF's are. They are declared
  look-ahead, so no strategy can use them. The events (Confirmed, Cancelled, Failed, Pending, the
  Timing Windows and the confirmations' companions) are decided on their own bar; a test checks that cutting the history
  short never changes them, and a bar-by-bar replay of six assets agrees (section 8). Except: one
  weekend bar on a weekday chart (XAU's data has them) changes the clock and redraws the cycles.
- **The presets are the original's own** (section 2), SPX's from videos. Reading the stock
  daily values as trading days is an inference from the TradingView charts.
- **What starts the search for a low is not the original's.** Confirmation timing now is (section
  11), but in strong rallies the original confirms a low the cycle line has not yet found, and in
  others it shows no candidate at all. Neither rule is known.
- **Not built:** CF's future FCLR boxes to the right of the last bar (the chart has no future bars to
  draw on), the camel emoji in the D/W/Y low stacks, confluence that upgrades from LOW to MID to HIGH
  over the following bars (ours is fixed at confirmation), the US macro table and the Bullish Meter.
- **Confluence is ours, not CF's.** CF names three parts (breakout, momentum, supporting indicators)
  without definitions. Cipher C gives one point each on the confirming bar for: the cycle that just
  ended was inside its normal length, the close is above the 10 SMA, and the close is above the low
  bar's high. Three is high, two mid, one or none low.
- **The clock under a week.** Fewer than seven days of a crypto chart look like a stock's until a
  weekend arrives. No tier can run on that little history, so nothing is drawn, but the window bounds
  and preset are declared look-ahead for it.
- **The yearly tier needs about 2,000 daily bars**, and none of CF's yearly lows has been seen to
  compare.
- **Nothing has been heard by ear yet.**

## 7. What it replaced

- **The old Cipher C**, an Ehlers cyber-cycle oscillator in its own pane. Its ten signals are retired
  in `SignalCatalog`: a strategy or alert that used one says it was removed and names the nearest new
  part. A saved chart with the old Cipher C restores as the new one on the price pane, without the old
  ±75 and ±90 levels.
- **Loukas Cycle Detection**, deprecated here and removed on 2026-10-22 (Cody: "loukas is what we want
  to get rid of"); saved charts, strategies and alerts say so through `RetiredIndicators`. Each of its parts has a counterpart: DCL Confirmed is DCL Confirmed; its intermediate cycle
  low (ICL) is Camel's weekly cycle low, WCL; DC Day Count and DC In Window are the Daily Timing
  Window's count and tint; DC Overdue is the window saying "days past its window"; its four-year phase is spoken in Cipher C's
  Bitcoin preset words ("Day N after the April 2024 halving, the … phase"). Cipher C also tiers by
  timeframe, which Loukas left to the user.
- **Pulse Reversal Long**, the StrategyLab catalogue strategy built on the old Cipher C's bottoms. It
  is in the retired list of `CatalogueProvenance` and has not been re-tested on DCL Confirmed.

`docs/CYCLE_LOW_INDICATOR_DESIGN.md` is a different design (a capitulation score, no windows) written
for the pre-registered cycle-school test. It is not what Cipher C does.

## 8. Backtest: buy the confirmed daily low (2026-10-22)

Cody: *"run backtests on cipher c now where we buy at the confirmed cycle low and sold at the peak
left, center or right translated and see how that does."* Run on the shipped `CipherCProvider.Compute`
over the lab's daily files (scripts and the throwaway dump harness:
`strategy-lab-data/cipher-c-backtest-2026-10-22/`). Buy at the next open after **DCL Confirmed**, one
position at a time, 0.1% a side. Every causal exit also stops out on a close under the confirmed low.

**The confirmations are causal.** Replayed bar by bar (each bar computed from the bars up to it), every
confirmation, cancellation and failure lands on the same bar as in the full-history run on BTC, ETH,
SPY, QQQ, DIA and GLD. Not on XAU (twelvedata): 42 of 150 match, because that file has 135 stray
weekend bars since October 2024 and one weekend bar in the last 500 flips `MeasureBars` to "trades
every day", which rescales every cycle (TODO).

**Selling at the top (oracle, look-ahead).** Mean per trade: BTC +38%, ETH +10%, QQQ +5.5%, SPY +3.7%,
DIA +3.5%, GLD +3.3%, winning 83–98% of the time. Right-translated cycles make the most (BTC +45%,
SPY +4.2%) and left the least (BTC +16%, SPY +2.4%), but that is the definition of a right-translated
cycle, not a finding. The top is not reachable: on ETH it had already passed when the low was
confirmed in 67 of 145 cycles, GLD 51 of 225, BTC 22 of 95. Confirmation comes a median 5–12 bars
after the low (BTC 12, p90 56).

**Causal exits** (sell on a close under the 10 SMA; when the next low's window opens; after 10, 20 or 40
bars), against random entry bars with the **same exit rule and stop** (the stop is the lowest low of a
lookback drawn from the real confirmation lags; holding the strategy's own hold lengths flatters an
adaptive exit). Per-trade means are a fraction of a percent on the stock ETFs. DIA beats 92–96% of the
random draws on every rule and SPY 75–95%; QQQ (31–65%) and ETH (5–50%) do worse than random; GLD is
mixed. BTC's SMA exit beats 86% (2012–16 and 2017–26 alike); its window exit 60–70%. Read across 30
comparisons: no exit is a dependable edge on its own.

**The previous cycle's translation (known at the confirmation) is the one thing that held out.** The
cycle after a LEFT-translated one returned more than the cycle after a right-translated one, opposite to
reading right translation as "bullish, carry on". Found on SPY, QQQ and DIA (window exit: +2.1, +3.5,
+2.9 points, shuffle p 0.01, 0.02, 0.00), then predicted before looking on 13 other stock ETFs (IWM,
EFA, EEM, VTI and nine sectors, SPX preset): positive in 12 of 13 with the window exit and 13 of 13
holding 20 bars, about +2 points a trade. Not on crypto (BTC +16 points but p 0.29 on 21 trades; ETH
−1). Part of it is the size of the fall: a low after a left-translated cycle has fallen a median 12%
from the cycle's high, after a right one 5%, and bigger falls bounce more. Within thirds of that fall,
left still beats right by 0.75–2 points.

> **Run before the hold (section 11).** These trades bought a median 5–12 bars after the low.
> Cipher C now confirms after the low has held for half the shortest cycle (18 bars on the stock
> ETFs), so every entry here comes earlier than it would now. The results have not been re-run.

Caveats: the ETFs share their years, so 12 of 13 is fewer independent tests than it looks; the stock
presets were then partly estimated (section 6); no costs beyond 0.1% a side; this is a finding about the
signal, not a strategy that has been walked forward.

## 9. Held-out check against videos after 2026-10-17 (2026-10-22)

> **Corrected the same day (section 10).** The DJI "defect" below is withdrawn. The original
> indicator itself, run on TradingView (TVC:DJI daily, default settings), confirms **06-23** as its
> daily low, the same bar Cipher C took. It has no low at 07-22, and its timing window is counted
> from 06-23 (35–43 bars after it). The screenshot below was made with settings, a symbol or a
> version we cannot see. The proposed fix would have moved Cipher C away from the original, so it
> is dropped. The rest of this section is kept as it was written.

Cody asked how well Cipher C finds the cycle lows, tops and failed cycles, and whether its timing
windows fit. Every label used so far was in hand while Cipher C was being built, so it was scored
only against videos that came out after the build: 10-19, 10-20, 10-21 and 10-22. Their
transcripts are in `AccessibleTrader.StrategyLab/Corpus/transcripts/camel-finance/`. Two more
videos from those days are members-only and were not obtained.

The creator hides the indicator on his own charts in all four videos. The only indicator output on
screen is two screenshots of other traders' charts: DJI daily (made 10-20) and TLT daily (made
10-19). Dates are read from the axis ticks and are good to 1–3 days. Cipher C was run on Yahoo
daily DJI and TLT to 2026-10-22, both as a full run and replayed bar by bar from 2025. DJI uses its
preset (daily window 36–44, partly estimated). TLT has no preset, so it falls back to 36–44.

| | Their label | Cipher C |
|---|---|---|
| DJI | daily low about 05-18 (count 36D) | daily low 05-20 ✓ |
| DJI | daily + weekly low about 07-22 (44D) | **missed.** The daily tier took 06-23 (23 bars after 05-20, below the 36-bar minimum) and confirmed it on 08-04. Its weekly tier picked 07-23 as the weekly low, so the two tiers disagree. |
| DJI | timing band about 09-11 to 09-21 | ours sat a month early, because it was counted from 06-23. Their band is exactly 36–44 trading days after 07-22, which supports the estimated DJI preset. |
| DJI | daily + weekly low about 10-01 (49D) | daily low 10-01, confirmed 10-12 ✓. Before that, a "cycle failed" on 09-30 when the 06-23 low broke, which they did not show. |
| TLT | daily + weekly low about 05-20 | daily low 05-19, weekly confirmed 05-29 ✓ |
| TLT | daily low about 08-14 (±3) | daily low 08-18, confirmed 08-20, about 4 days off. Cancelled on 09-10 when price undercut it; theirs still stands. |
| TLT | timing band about 09-27 to 10-06 | ours was later. Their band reads like a 29–35 day cycle; this is one window, too little to set a preset from. |
| TLT | daily low about 10-06, still unresolved (a white disc) | daily + weekly low 10-07 ✓ |

Five of the seven lows match within a few days. The DJI miss is a defect, and it shows: **when the
lowest low since the last cycle low comes too early (a half-cycle low), the daily tier takes it
anyway**, then measures its window and its translation from the wrong low. The same pattern shows
up across the whole history:

- DJI: 3 of 28 weekly confirmations name a low the daily tier does not mark (2019-09-27, 2020-02-12,
  2026-08-06). TLT: 0 of 24.
- Daily cycles shorter than 27 bars: DJI 6 of 56 (median 39 bars, 9 longer than 60); TLT 5 of 49
  (median 45, 13 longer than 60).

Proposed fix (TODO, not built): when the deepest low comes before the window's minimum and a later
low inside the window holds above it, prefer the later low. At the least, take the weekly tier's low
as the daily low when they disagree. The fix would then be scored on the same labels plus the next
videos, and against the bar-by-bar replay so it stays causal.

New about how the original looks, from the screenshots:
- a low that is not yet resolved is drawn as a white disc;
- the weekly stack can carry its own count (DJI's `27`);
- a row of small blue "D" circles runs along the bottom of the pane.

Still not seen: the yearly low, any translation label, and the settings panel. Spoken: the creator
puts the left/right translation cut-off at 30 days, half of a 60-day cycle. Cipher C already cuts at
the midpoint.

## 10. Against the original indicator itself, DJI daily (2026-10-22)

Cody took the creator's 14-day TradingView trial. TradingView's "download chart data" needs a paid
plan, so nothing was exported. Instead, every label, box, line and table cell the indicator draws was
read from the chart in a separate browser window that Cody logged into (TVC:DJI, 1D, default settings,
version V-2). The indicator's code is compiled and was not opened; only what it draws on the chart
was read. The data is kept out of the repo, in `strategy-lab-data/cf-tv-export/`, along with the
tools and scripts used to read it.

**What it drew:** 500 labels (TradingView's cap), covering 2000-10 to 2026-10:

- 129 daily lows, 53 of them marked "updated", so the original moves its lows too;
- 55 weekly lows and 5 yearly lows;
- 128 tops, 89 right-translated and 39 left-translated. **No mid-translated tops:** it labels only
  left and right;
- a count under each low, in trading days ("58D") or weeks ("24W");
- 218 cycle boxes from low to low. Green is a normal cycle; red (64 of them) is a cycle that ended
  below where it started;
- a shaded timing window and a table. The table gives the preset (DJI: 36–44 days, 22–31 weeks,
  4 years, the same as our estimated preset), days since the last confirmed and unconfirmed daily
  low, and the same for weeks.

**Scored against Cipher C** (Yahoo ^DJI, full run, 2017-03 to 2026-10; matches are one-to-one):

| | Original | Cipher C | Same bar | Within 3 bars | Within 7 bars |
|---|---|---|---|---|---|
| Daily lows | 48 | 56 | 35 | 35 | 36 |
| Weekly lows | 20 | 22 | 12 | 12 | 12 |
| Tops (any translation) | 47 | 56 | 38 | 38 | 38 |

- **When they agree, they agree on the exact bar.** Both take the lowest low. When they disagree,
  each has usually taken a different low inside the same cycle.
- **Cipher C makes more, shorter cycles.** Daily cycle lengths:
  - the original: shortest 27 bars, median 50, 11 longer than 60, only 8 of 47 inside its own 36–44 range;
  - Cipher C: shortest 22, median 37, and 8 shorter than 27.
- **This is the real version of the "too-early low" problem.** The original never starts a new daily
  cycle under 27 bars; Cipher C does, eight times. It is not the 06-23 case: there, the original had
  no 05-20 low at all, so 06-23 came 58 bars after 03-30.
- **The timing window matches.** The original shades 35–43 bars after its last daily low (08-12 to
  08-24 after 06-23). Cipher C's is 36–44 after the same low: the same window, give or take one bar
  of counting convention.
- **Now (10-22):** the original's last *confirmed* daily low is 06-23, 85 bars ago. It shows 10-01 as
  an unconfirmed daily low, tagged "MID", 15 bars ago. Cipher C confirmed 10-01 on 10-12. So the
  original confirms more slowly here, or reads 10-01 as a mid-cycle low. What "MID" means is
  unverified.

**Caveats:**

- These are final labels: what the chart shows after the original has updated its lows. Cipher C's
  full run is the same kind of picture, so the comparison is fair. It says nothing about when
  either one confirmed in real time.
- One asset and one timeframe so far.
- The two data feeds differ slightly (TVC vs Yahoo).

**Next:** the same read on the rest of the original's finetuned list (SPX, DXY, NDQ, GOLD, SILVER,
USOIL, GDX, GDXJ, SILJ, ETHUSD, BTCUSD, COIN, MSTR, MARA, RIOT), daily and weekly, and its settings
panel. Then see whether a minimum cycle length of about 27 bars brings Cipher C's daily lows onto the
original's without costing the matches it already has.


## 11. Confirming when the original confirms (2026-10-22)

Section 10 compared final pictures. This section compares *when* each one confirms. TradingView's bar
replay steps the original one bar at a time, so it draws only what it knew on that bar. Its labels
and table were read on every bar: the Dow daily from January 2023 (920 bars) and GDX daily from
October 2023 (720 bars) to October 2026. The table's "unconfirmed" row names the low it is holding
before it confirms it. As in section 10, nothing was exported and the replay data stays in
`strategy-lab-data/`.

**The original's rule, read from it.** A found low is confirmed once it has held for **half the
shortest cycle**. The hold counts from the low itself, or from **the last confirmed low plus the
shortest cycle less two bars**, whichever is later. A lower low while it holds takes its place
(the original's 🔄) and the hold starts again.

- Dow (36–44): every confirmation from 2023 came 18 bars after the later of the low and the last low
  plus 34.
- GDX (22–28): all 27 cycles came 11 bars after the later of the low and the last low plus 20. Two
  of them separate "less two bars" from 34/36 of the shortest cycle (21 on GDX); both fit less two.
- Tops are labelled only when the next low is confirmed.

**What Cipher C does now.** `CipherCEngine.Detect` keeps its own way of finding the low (the cycle
line's cross up through overbought, then the lowest swing low since the minimum gap). It then holds
that low under the original's rule (`HoldFraction` 0.5, `HoldFromBeforeMin` 2). Cancellation is
kept: the original documents a "cancelled" alert. With the hold, a low can only be cancelled in a
short gap, between half and 0.6 of the shortest cycle after it.

| Confirmations on the original's own bar (same low, same date) | Before | Now |
|---|---|---|
| Dow, 18 of the original's daily lows from March 2023 | 1 | 13 |
| GDX, 27 of the original's daily lows from October 2023 | 0 | 16 |
| Same low, any date: Dow / GDX | 14 / 20 | 16 / 21 |

Measured on the C# engine through the dump harness. Before the hold, Cipher C confirmed a median 15
calendar days earlier than the original on the Dow, and 10 earlier on GDX.

**Across all fifteen assets, without fitting the hold to them.** The original's final labels were read
off its chart for the other finetuned assets, by section 10's method. They were split before looking into seven to train on (GDX,
SILVER, DJI, DXY, ETHUSD, MARA, COIN) and eight held out (GDXJ, SILJ, GOLD, NDQ, USOIL, BTCUSD,
MSTR, RIOT). Each cell is the F1 score: a low counts only on its exact bar, matched one to one,
after a 300-bar warm-up, in the Python prototype with the original's ranges:

| | Train daily | Train weekly | Train tops | Held-out daily | Held-out weekly | Held-out tops |
|---|---|---|---|---|---|---|
| No hold | 0.677 | 0.499 | 0.742 | 0.712 | 0.560 | 0.789 |
| Hold (now) | 0.694 | 0.515 | 0.749 | 0.727 | 0.587 | 0.795 |

The rule came from the Dow and GDX replays, and both are in the training set. The held-out gain is
small but runs the same way on all three counts. Dropping cancellation scored a little higher
(held-out daily about 0.73). It was kept because the original has the state.

**What is still not the original's:**

- **What starts the search.** Most of the remaining misses are lows the two pick differently.
  Sometimes the original confirms a low in a strong rally, on schedule, while Cipher C's cycle line
  has not crossed. GDX in late 2025 shows this: the original confirmed 09-18 on 10-03, and Cipher C
  confirmed it on 11-07. At other times the original shows no candidate at all through a rally (Dow:
  July 2023, January–February 2024, September 2024, September 2025). Its search seems to start
  about three-quarters of a shortest cycle after the last low, and it takes a recent swing low, not
  the window's minimum. Every gate tried on top of Cipher C's own trigger (moving-average position,
  drawdown, position in the range, "no new cycle high since", a top-first rule) scored worse
  without it.
- **The LOW / MID / HIGH tag** on an unconfirmed low. The best single separators found (the high
  since the candidate against its range, or the close against a 38-bar average) agree with the
  tag about 90% of the time. Neither is the rule.
- **The weekly tier.** Only daily charts were replayed. The weekly tier uses the same hold
  unverified.

**What changes for a user.** A daily low is now known at least half a cycle after it happened:
18 trading days on the Dow, 27 calendar days on Bitcoin (the median there was 12 before). The
backtest in section 8 bought earlier than this and has not been re-run.
