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
an inference, not an explicit CF statement. The other seven presets in `CipherCEngine.Presets` are
estimates from the nearest of those: NDQ, DJI, DXY, SILVER, USOIL, GDXJ and SILJ. Each says so when
spoken ("estimated, not CF's").

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
- **The lows are drawn on their own bar and known only later**, as CF's are. They are declared
  look-ahead, so no strategy can use them. The events (Confirmed, Cancelled, Failed, Pending, the
  Timing Windows and the confirmations' companions) are decided on their own bar; a test checks that cutting the history
  short never changes them.
- **Seven of sixteen presets are estimated:** NDQ, DJI, DXY, SILVER, USOIL, GDXJ and SILJ. The five
  newly read stock/miner tables and the updated gold table are now marked as CF's own; the stock
  daily-values-as-trading-days reading is an inference from the TradingView charts.
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
