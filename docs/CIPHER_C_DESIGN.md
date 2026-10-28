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
| 5 | 🕘 pending (🔄 too when an earlier pending low was dropped since the last confirmed one) | Grey-blue rounded refresh box (Cycle Low Pending); one after a cancellation says which cancelled low it replaces (section 20) |
| 6 | ❌ cancelled, in a green box | Red cross (Cycle Low Cancelled) when a bar goes under the pending low before it is due; the confirmation that follows says which cancelled low it replaces. A confirmed low is never cancelled (section 15) |
| 7 | Live confirmation marker above price | Same square as 4 |
| 8 | LOW / MID / HIGH on the pending label, bar by bar | The pending low's `_grade` companion (0, 1, 2) on every bar it is held, spoken with the pending (section 20). The Confirmed components' own confluence (1 to 3) is ours: the original tags no confirmed low |
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
| Cycle Low Pending | the bar's close | "Daily cycle low pending on day 76, mid confluence: the low of 59100.00, 7 days ago. It replaces the cancelled low of 61000.00." (the last sentence only after a cancellation; the low the daily tier names; with no daily tier, "… the cycle line reached oversold on week N. Lowest low since the last cycle low: …") |
| Cycle Low Cancelled | the pending low it went under | "Daily pending cycle low cancelled: a low of 75285.00, under the pending low of 76000.00 from 4 days ago." |
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
  Buying the confirmed daily low (section 8) is not a dependable edge either. The left-translated
  finding that once held out of sample went away when confirmation moved to the original's timing.
- **The lows are drawn on their own bar and known only later**, as CF's are. They are declared
  look-ahead, so no strategy can use them. The events (Confirmed, Cancelled, Failed, Pending, the
  Timing Windows and the confirmations' companions) are decided on their own bar; a test checks that cutting the history
  short never changes them, and a bar-by-bar replay of seven assets agrees on every event (section 8).
- **Weekends.** The original counts gold's and the dollar index's weeks as five bars and only crypto's
  as seven, although TradingView dates a fifth of their bars on a Sunday (UTC). Cipher C now calls a
  market "trades every day" only when Saturday and Sunday bars each turn up on at least half the weeks
  and each day's median range is at least a quarter of a weekday's (on two-day bars: at least a fifth
  of the bars fall on a weekend). Flat bars (nothing traded) are left out of the ranges; counting
  them made Bitstamp's 2011 Bitcoin a five-day market for its first 300 bars. One stray weekend bar
  used to be enough, which flipped XAU's clock.
- **The presets are the original's own** (section 2), SPX's from videos. Reading the stock
  daily values as trading days is an inference from the TradingView charts.
- **What starts the search for a low is closer to the original's, not the same.** Confirmation timing
  is the original's (section 11). The daily search now opens on schedule and takes the low after the
  cycle's top on the first bar that turns up (section 11), but it names the original's candidate on
  the bar it appears only about two times in three.
- **Not built:** CF's future FCLR boxes to the right of the last bar (the chart has no future bars to
  draw on), the camel emoji in the D/W/Y low stacks, the US macro table and the Bullish Meter. The
  pending tag changes bar by bar as the original's does (section 20), but it is spoken only when
  the low is named; a later change is read on the bar, not announced.
- **Confirmation confluence is ours, not CF's.** CF tags only pending lows (section 20); its
  confirmed labels carry no tag. CF names three parts (breakout, momentum, supporting indicators)
  without definitions. On confirmation Cipher C gives one point each on the confirming bar for: the cycle that just
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

**Re-run on the original's timing (2026-10-23).** Cody chose to keep the original's later
confirmation (section 11) and re-run this. The run below is on the engine with the hold, the weekend
clock (section 6) and the daily search (section 11); scripts in
`strategy-lab-data/cipher-c-backtest-2026-10-23/`.

- **The confirmations are causal, now on all seven assets.** Replayed bar by bar (each bar computed
  from the bars up to it), every daily, weekly and yearly confirmation, cancellation and failure lands
  on the same bar as in the full-history run: BTC 85 of 85 daily confirmations, ETH 143, SPY 178, QQQ
  134, DIA 144, GLD 177, and XAU 161 of 161 (42 of 150 before the weekend fix), each replayed from
  bar 300. From bar 100, after flat weekend bars were left out of the clock: BTC 89 of 89, ETH 153,
  XAU 168.
- **Confirmation now comes later:** a median 18 bars after the low on SPY, QQQ and DIA (p90 19–22),
  11 on GLD and XAU, 8 on ETH, 27 on BTC (p90 46). The top had already passed when the low was
  confirmed in 35 of 89 BTC cycles, 68 of 155 ETH, 45 of 183 SPY.
- **Causal exits still show no edge** against random entries with the same exit rule and stop. On
  the stock ETFs per-trade means are within a fraction of a percent of zero; SPY now does worse than
  random on all five exits (beats 6–27% of the draws), DIA is at chance (36–50%), QQQ mixed (33–77%).
  BTC beats 57–81% (SMA exit 78%), ETH 42–87%, GLD 25–94%, XAU 16–85%. Across 35 comparisons nothing
  is a dependable edge.
- **The left-translated finding is gone.** The cycle after a left-translated one against the cycle
  after a right-translated one, window exit: SPY −0.5, QQQ +0.4, DIA +0.2 points (shuffle p 0.79,
  0.40, 0.39). On the 13 ETFs it was predicted on, 6 positive, 1 level, 6 negative; only IWM is
  significant (+1.9, p 0.02). BTC −15, ETH −4. Holding 20 bars reads the same. What the first run
  found was a property of buying 5–12 bars after the low, which the original does not do.

**The first run (2026-10-22, before the hold).** Kept for the record; its entries came earlier than
Cipher C's do now.

- Replayed bar by bar, every event matched the full run on BTC, ETH, SPY, QQQ, DIA and GLD. On XAU
  (twelvedata) 42 of 150 matched: that file has 135 stray weekend bars since October 2024, and one
  in the last 500 flipped the clock (fixed since, section 6).
- **Selling at the top (oracle, look-ahead).** Mean per trade: BTC +38%, ETH +10%, QQQ +5.5%, SPY
  +3.7%, DIA +3.5%, GLD +3.3%, winning 83–98% of the time. Right-translated cycles make the most and
  left the least, but that is the definition of a right-translated cycle, not a finding.
  Confirmation came a median 5–12 bars after the low.
- **Causal exits** (sell on a close under the 10 SMA; when the next low's window opens; after 10, 20
  or 40 bars; every exit also stops out on a close under the confirmed low), against random entry
  bars with the same exit rule and stop: DIA beat 92–96% of the draws and SPY 75–95%, QQQ and ETH did
  worse than random, GLD mixed. No exit was a dependable edge.
- **Left-translated (withdrawn 2026-10-23).** The cycle after a left-translated one returned more than
  the cycle after a right-translated one on SPY, QQQ and DIA (window exit +2.1, +3.5, +2.9 points,
  p 0.01, 0.02, 0.00), and then on 12 of 13 other stock ETFs predicted before looking. It did not
  survive the original's timing (above).

Caveats: the ETFs share their years; the stock presets are partly estimated (section 6); no costs
beyond 0.1% a side.

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
short gap, between half and 0.6 of the shortest cycle after it. (Superseded 2026-10-24, section 15:
only a pending low is cancelled; a confirmed one never is.)

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
| Hold | 0.694 | 0.515 | 0.749 | 0.727 | 0.587 | 0.795 |
| Hold and the daily search (now) | 0.721 | 0.526 | 0.771 | 0.739 | 0.592 | 0.815 |

The rule came from the Dow and GDX replays, and both are in the training set. The held-out gain is
small but runs the same way on all three counts. Dropping cancellation scored a little higher
(held-out daily about 0.73). It was kept because the original has the state.

**What is still not the original's:**

- **What starts the search (partly done 2026-10-23).** Before, Cipher C found a low only when its
  cycle line crossed up through overbought. In strong rallies the original confirmed on schedule
  while the line had not crossed (GDX: the original confirmed 2025-09-18 on 10-03, Cipher C on
  11-07), and at other times it showed no candidate through a rally (Dow: July 2023, early 2024).
  Read from the replays: the original's "unconfirmed" low first appears 21 to 43 bars after the last
  low on GDX (shortest cycle 22), and it is seldom the lowest bar since the last low but the low after
  the cycle's top. **The daily tier searched that way until 2026-10-27** (`CipherCEngine.Detect(searchOnTurn:
  true)`, removed; section 19 has the rule that replaced it): from the last low plus the shortest cycle less two bars (`SearchFromBeforeMin`), the first
  close above the bar before's high takes the lowest low since the cycle's highest high. Simulated on
  every replayed bar it names the original's candidate on the bar it appears more often than any
  rule tried (GDX 25 of 38, Dow 21 of 35), but it is not the rule: the
  original sometimes names a low on a falling bar, and sometimes a day or two later. Held out (table
  above): daily 0.727 → 0.739 and tops 0.795 → 0.815; per asset five better, two worse, Bitcoin level.
  The first low on a chart, and the weekly and yearly tiers, still wait for the cross. Tried and
  worse: a close above the bar before's close, the lowest low since the last low, two bars to the
  low's right, a gate a few bars later.
- **The LOW / MID / HIGH tag** on an unconfirmed low: found later (section 20).
- **The weekly tier.** Only daily charts were replayed. The weekly tier uses the same hold
  unverified.

**What changes for a user.** A daily low is now known at least half a cycle after it happened:
18 trading days on the Dow, 24 calendar days on Bitcoin since 2026-10-24 (section 12; 27 before that, and a median of 12 before the hold). The
backtest in section 8 was re-run on it (2026-10-23).

## 12. The same rule on candles, and Bitcoin's real count (2026-10-24)

The replays in section 11 ran on Heikin-Ashi charts. The original was replayed again on regular
candles from January 2022 to October 2026, one asset at a time (`strategy-lab-data/cf-tv-export/cap2/`,
not committed). For each confirmed low, two numbers were read. The **hold** is the bars from the low
to its check mark. The **gate** is the bars from the last confirmed low to the check mark, on the
cycles cut short. Under section 11's rule, hold = half the shortest cycle and gate = 1.5 × the
shortest cycle − 2. The first label of a new low comes no sooner than the last low + the shortest
cycle − 1.

| Asset (table's shortest cycle) | Lows | Commonest hold | Gate on short cycles | Earliest first label | Shortest cycle that fits |
|---|---|---|---|---|---|
| GDX (22) | 44 | 11 on 37 | 31, 31, 31 (then 33s) | last + 21 | 22 |
| Dow (36) | 25 | 18 on 19 | 52, 52 (then later) | last + 35 | 36 |
| Bitcoin (54) | 24 | 24 on 19 | 70, 70, 70, 70, 72 | last + 47 | **48** |
| S&P, SP:SPX (none: the original said "Unoptimised Asset Detected") | 21 | 16 on 18 | 46, 46, 48 | last + 31 | 32 |
| Nasdaq (36) | 25 | 18 on 24 | 52 | last + 35 | 36 |
| Gold (22) | 44 | 11 on 37 | 31, 31, 31 (then later) | last + 21 | 22 |
| Silver (22) | 40 | 12 on 33 | 34, 35, 36, 36, 36, 38 | last + 23 | **24** |
| Dollar index (15) | 52 | 9 on 39 | 25, 25, 25, 25, 26 (then later) | last + 17 | **18** |
| Ethereum (15) | 75 | 8 on 59 | 22, 22, 22, 23, 24 (then later) | last + 15 | **16** |
| GDXJ (22) | 44 | 11 on 37 | 31, 31, 31, 31, 32 | last + 21 | 22 |
| SILJ (22) | 43 | 11 on 38 | 31, 31, 33, 34 | last + 21 | 22 |
| Coinbase (40) | 17 | 25 on 14 | 74 (then 77, 101) | last + 50 | **51** |
| MicroStrategy (40) | 18 | 25 on 14 | 74, 74, 74, 75 | last + 50 | **51** |
| MARA (36) | 23 | 19 on 19 | 55, 55, 56 (then 67) | last + 37 | **38** |
| RIOT (36) | 23 | 19 on 19 | 56, 56, 56 (then 60) | last + 38 | **39** |

- **The rule holds on candles.** All three numbers agree with the same shortest cycle on every asset.
- **Bitcoin counts from 48, not 54.** Its table says 54 to 66 days. At 54, Cipher C confirmed every
  Bitcoin low 3 bars late. `CyclePreset.CountDailyMin` now holds the 48. The table's 54 to 66 is
  still what is shown and said, followed by "counted from 48". Scored on the Bitcoin candle replay:
  daily lows on the original's bar 19 → 22 of 25, extra lows 6 → 6, check marks on the original's
  bar 0 of 19 → 20 of 22. Weekly lows stay 9 of 11, with extra weekly lows 1 → 3.
- **Silver counts from 24, not 22**, though its table is Gold's (22 to 28) and Gold counts from
  22. At 22 none of Cipher C's Silver check marks landed on the original's bar (0 of 33, each a bar
  early). Counted from 24 (`CountDailyMin`, said as "counted from 24"): check marks on its bar 0 of
  33 → 27 of 32, extra daily lows 12 → 8, but exact daily lows 47 → 46 of 56 and weekly 6 → 5 of
  12 (both still 6 within 2 bars). Silver was not used to fit anything before this.
- **The dollar index counts from 18, not 15.** At 15 one Cipher C check mark in 43 landed on the
  original's bar (most a bar early). Counted from 18: 30 of 40, extra daily lows 37 → 21, but exact
  daily lows 93 → 87 of 114 and weekly 21 → 19 of 46 (daily F1 0.76 → 0.78, weekly 0.51 → 0.47).
  The lows lost are not a timing matter: the search fires earlier than the original's and finds an
  earlier top (2026-03: ours picks the 9th, after a top on the 5th; the original waits for the top of
  the 12th and picks the 22nd). Searching 3 bars sooner than the count (tried) gives 90 daily, 18
  weekly; not adopted. The original's lows on the dollar are often closer than its shortest cycle
  (35 of 113 gaps under 18 bars, down to 10): its first label waits, the low itself need not.
- **Ethereum counts from 16, not 15.** Both hold 8 bars (7.5 rounds up), so the check mark after a
  normal cycle is the same; a short cycle's waits a bar longer, to the last low + 22. Counted from
  16: check marks on the original's bar 47 of 66 → 52 of 67, extra daily lows 44 → 37, exact daily
  lows 113 → 112 of 130, weekly 31 of 57 both (extras 36 → 33). Its weekly lows fit neither weekly
  rule (counting daily lows 26 of 35 of the original's own since 2021-11, the weekly pick 24 of 34).
- **Coinbase and MicroStrategy count from 51, not 40, and the hold rounds down.** A hold of 25 with
  a short-cycle gate at 74 is 51 − 2 + 25, so the hold is half of 51 rounded down (Pine's `int()`).
  These are the first odd shortest cycles; the even ones cannot tell rounding down from rounding to
  nearest. MicroStrategy: daily lows 22 → 25 of 28, extras 14 → 4, weekly 7 → 10 of 12, check marks
  on the original's bar 0 → 15 of 16. Coinbase: check marks 0 → 7 of 8, but daily lows 12 → 7 of 19
  and weekly 5 → 4 of 9. Two of the losses are explained. On 2025-06-18 the bar that turns up is
  itself the cycle's highest high, so the search (top over the last low to this bar, inclusive)
  finds no low before it; the original names 06-13. Excluding the turning bar from the top was
  tried on all fourteen assets: 886 → 904 exact of 1167, but extras 229 → 334. On 2022-09-23 the
  low is undercut 29 bars on, inside the cancel gap of round(0.6 × 51) = 31; the original keeps it.
- **MARA counts from 38, not 36.** At 36 every Cipher C check mark came a bar early (0 of 20 on the
  original's bar). Counted from 38: 15 of 19, exact daily lows 27 → 26 of 32, extras 4 → 5, weekly
  11 of 14 both.
- **RIOT counts from 39.** It has MARA's table and MARA's hold, but its short cycles wait one bar
  longer and its first labels come one bar later, so it counts from 39 (floor(39/2) = 19). Check marks
  on the original's bar 0 of 16 → 12 of 15. Exact daily lows (21 of 31) and weekly lows (7 of 15)
  are unchanged. At 38: 9 of 15 on the bar, daily 22, weekly 8.
- **The original never removes a confirmed low.** In every candle replay so far (0 of 379
  confirmed lows on nine assets) a low with its check mark is still in the final labels, and its
  cancelled mark (❌) is never drawn; its table shows only Confirmed and Unconfirmed. Cipher C
  cancels a confirmed low closed under within 0.6 of the shortest cycle. Turning that off: exact
  daily lows 886 → 903 of 1167, extras 229 → 239, F1 0.781 → 0.786, weekly lows unchanged. Not
  adopted yet: it would leave the Cycle Low Cancelled component with nothing to say (TODO).
  Adopted 2026-10-24 (section 15), with Cycle Low Cancelled moved to the pending low.
- **The rule, put in one number:** on every asset so far the check mark waits h bars (the commonest
  hold) after the low, and a short cycle's waits until the last low + 3h − 2: the shortest cycle that
  fits is 2h. GDX 11 → 22, Dow 18 → 36, Bitcoin 24 → 48, Silver 12 → 24, dollar 9 → 18, Ethereum
  8 → 16, S&P 16 → 32, Coinbase and MicroStrategy 25 → 51 (2h + 1: the hold is half, rounded down), MARA 19 → 38, RIOT 19 → 39 (2h + 1).
- **The S&P on TradingView's SP:SPX is not recognised** by the original, though its list names
  SP{SPX}, so it fell back to its default count. That default behaves as a shortest cycle of 32.
  Cipher C keeps the S&P's own 36 to 44 for SPX. Its fallback for an unknown symbol is still the
  S&P's ranges, not 32.
- **An open-source look-alike, CTZ Cycle Trader v1.7, was scored beside it** (ported exactly, checked
  against its own chart labels). It confirms about 5 bars after a low, and never on the original's
  bar. Exact daily lows: GDX CTZ 44 of 61 vs Cipher C 49; Bitcoin 17 of 25 vs 22; Dow 81 of 129 vs
  101; S&P (SP:SPX) 86 of 129 vs 83, where Cipher C counts from 36 and the original from its
  default 32. CTZ finds many more lows that the original does not have (Dow 137 vs 23, S&P 102
  vs 27).

## 13. A lower low on the bar a low comes due (2026-10-24)

`strategy-lab-data/cf-tv-export/ctz/moved.py` lists every replay bar whose low went under the
original's unconfirmed (🕘) daily low, and what the original showed on the next bar. Five candle
replays (GDX, Bitcoin, Dow, SP:SPX, Nasdaq), 124 undercuts:

| Bars since the low, as a share of the hold | Confirmed | Kept unconfirmed | Dropped |
|---|---|---|---|
| under 0.5 | 0 | 0 | 74 |
| 0.5 to 0.75 | 0 | 0 | 19 |
| 0.75 to 1 | 0 | 7 | 11 |
| 1 or more (due) | 8 | 3 | 2 |

- **On the due bar the original confirms.** Cipher C used to move the low to the new bar and hold
  again. It now confirms on that bar. Scores in CHANGES (2026-10-24): small gains
  on GDX, Bitcoin and the S&P, Dow weekly one worse, Nasdaq unchanged.
- **Earlier in the hold the original drops the low** (its 🕘 label goes away) rather than moving it.
  The next label comes from a fresh search. Cipher C moves the low to the lower bar instead. The
  lows and check marks come out the same: a fresh search finds the lowest bar since the cycle's top,
  which is the lower bar. Only when the unconfirmed label shows differs.
- **Counted in bars left until the low is due** (the hold counted from the later of the low and the
  last low + the shortest cycle − 2), the 124 undercuts split cleanly: 4 or more bars left, dropped
  100 of 100; 1 or 2 left, kept 7 of 8; the due bar, confirmed 7 of 8. Tried: ignoring a lower low
  in the last 1, 2 or 3 bars as well. Two bars is best on GDX (daily 51 → 53 of 61, weekly 6 → 7),
  S&P (84 → 85, weekly 30 → 32) and Gold (83 → 85), but the Dow loses 2 daily and 6 weekly lows
  (101 → 99, 39 → 33): its three such bars (2022-06-13, 2024-12-18, 2025-04-03) were all dropped. Not
  adopted; what decides the 8 near-due cases is not found.
- **Nasdaq's misses are the search, not the hold.** On 20 May 2022 the original found the low and
  confirmed it on 16 June, the bar that made a lower low. Cipher C found no low there: an earlier low
  had failed, and the low it found was 16 June itself.

## 14. Weekly lows by counting daily lows: the dollar, the Dow, the S&P, the Nasdaq (2026-10-24)

**Superseded (2026-10-27) by section 23**; kept as the record of what was tried.

From the dollar on, the candle replays log every weekly label the original shows on every bar, and
its table's two week counts ("Weeks Since Last WCL", the confirmed one, and the unconfirmed one).
`strategy-lab-data/cf-tv-export/ctz/wcount.py` turns the counts back into weekly-low dates;
`ctz/wsim.py` replays a rule on the original's own daily confirmations; `ctz/wours.py` on Cipher C's.

**The rule read from the dollar** (all its 2022–26 weekly lows; each step acted on the bar a daily
low is confirmed):

- The first daily low after a new weekly low takes its place if it is lower (12 of 12), however
  near. A low that took a place is never moved itself (4 of 4).
- From the second daily low after the newest weekly low, a new weekly low goes on the lowest of the
  daily lows since it that is at least 2 × (shortest daily cycle − 2) bars after it. This can be an
  earlier daily low than the one just confirmed. Until one is far enough, nothing happens.
- The confirmed weekly low, which the week count runs from, is the one before the newest. It moves
  at a daily confirmation, except on the bar a new weekly low is drawn on the low just confirmed.

Fed the original's own daily lows (counted from the dollar's 18, the Dow's and Nasdaq's 36, the S&P's
default 32):

| | Gap (bars) | Weekly lows drawn | Missed | Extra |
|---|---|---|---|---|
| Dollar | 32 | 22 of 22 | 0 | 0 |
| Dow | 68 | 10 of 11 | 1 | 1 |
| Nasdaq | 68 | 10 of 11 | 1 | 1 |
| S&P (SP:SPX) | 60 | 11 of 13 | 2 | 1 |
| Oil | 68 | 7 of 13 | 6 | 3 |

The old pick (`WeeklyFromDaily`, the four-week margin) on the same lows: Dow 10 of 11, S&P 11 of 13,
Nasdaq 8 of 11. The gap was fitted on these replays, so the replays are not held out; the history
before them (the original's final labels back to the 1990s–2000s) is, in the end-to-end scores below.

**Built** as `CipherCEngine.WeeklyFromTwoDaily`, used where `CyclePreset.WeeklyEveryTwoDaily` is set
(dollar, Dow, S&P, Nasdaq). Cipher C finding its own daily lows, scored on the original's final
labels (exact weekly lows, extras):

| | Before | After |
|---|---|---|
| Dollar | 19 of 46, 16 | 27 of 46, 16 |
| Dow | 39 of 57, 17 | 40 of 57, 13 |
| Nasdaq | 33 of 56, 22 | 38 of 56, 16 |
| S&P | 30 of 57, 19 | 29 of 57, 20 |

- The S&P loses one because Cipher C counts it from 36 (gap 68) where the original counts from its
  default 32 (gap 60) — the open question in TODO.
- Daily lows change by one on the Dow (101 → 100) and the dollar (87 → 86): every weekly low is
  drawn as a daily low too, and a weekly low the daily tier later cancelled now falls elsewhere.
- **Oil, Gold, Silver, GDX and Bitcoin do not fit** (Gold, Silver and the miners got their own rule
  later: section 16). Oil follows the same
  shape (its first-low replacement holds) but no single gap works: 20 bars draws all 13 with 3
  extra, 50 draws 10 with 3 extra. Gold, Silver and GDX keep weekly lows five or more daily cycles
  apart; Bitcoin fits the old pick (11 of 11) better than this one (9 of 11). The weekly-label
  replays of Gold, Bitcoin, Silver, GDX, the Dow, the Nasdaq and the S&P from 2022 (cap3) are
  queued to read those.

Oil (held out, scored 2026-10-24): it counts from its table's 36 (check marks 18 bars after the
low on 21 of 27), Cipher C's daily lows 97 of 129 exact with 23 extra, check marks on the
original's bar 13 of 17, weekly 31 of 57.

## 16. Weekly lows judged at the next daily low: Gold, Silver, the miners (2026-10-24)

**Superseded (2026-10-27) by section 23**; kept as the record of what was tried.

The 22-day assets (Gold, Silver, GDX, GDXJ, SILJ; all with a table shortest daily cycle of 22) did
not fit either weekly rule. Their rule was read from GDXJ's candle replay, which logs every weekly
label the original draws on every bar, and the 🕘 counters in its table (weeks since the
confirmed and since the unconfirmed weekly low).

**The rule** (`CipherCEngine.WeeklyFromNextDaily`, used where `CyclePreset.WeeklyNextDaily` is set).
U is the newest weekly low. Each daily confirmation is acted on the bar it happens.

1. A daily low confirmed after U is drawn at once as the weekly candidate F.
2. The next daily confirmation N judges it:
   - N lower than F: N is a new weekly low when at least 4 cycles (88 bars) after U; otherwise it
     takes U's place if lower than U.
   - N not lower: F is a new weekly low when at least 4.5 cycles (99 bars) after U; otherwise it
     takes U's place if lower.
3. The confirmation after N draws no candidate when N undercut F, or F was more than 6 cycles
   (132 bars) after U. The one after that does.
4. The confirmed weekly low (where the week count runs from) is the one before the newest.

The three gaps are fitted, in multiples of the table's shortest daily cycle. The fit allows 100–104,
85–88 and 130–140 bars at 22. Silver is counted from 24 (section 12) but fits the table's 22; at 24
(108 bars) it draws one fewer. A minimum distance for drawing the candidate does nothing below 34
bars and is left out.

**Fed the original's own daily lows**, in the order it confirmed them, from its weekly state on the
first replay bar (the replays since 2021-22, `ctz/w22.py`):

| | Weekly lows | This rule | Extra | The old pick |
|---|---|---|---|---|
| GDXJ | 10 | 10 | 0 | |
| GDX | 9 | 8 | 1 | |
| Gold | 8 | 7 | 2 | |
| Silver | 9 | 8 | 1 | |
| SILJ (held out of the reading) | 11 | 7 | 2 | |
| Total | 47 | 40 | 6 | 43, 51 extra |

**End to end**, with Cipher C finding its own daily lows, scored on the original's final weekly lows:

| | Exact, all history | Extra | Exact, replay years | Extra |
|---|---|---|---|---|
| Old pick | 43 of 72 | 44 | 26 of 47 | 31 |
| This rule | 39 of 72 | 23 | 24 of 47 | 18 |

- F1 rises 0.54 → 0.58 over all history and 0.50 → 0.54 over the replay years; the gain is in extras.
- By asset (all history, exact / extra): GDX 6/6 → 7/3, GDXJ 7/6 → 7/2, Gold 15/17 → 15/6, Silver
  6/10 → 4/7, SILJ 9/5 → 6/5. SILJ, the one asset the rule was not read from, loses three.
- Why it loses end to end: the rule alternates (candidate, judge, sometimes a pass). An extra or
  missing daily low of ours flips that rhythm and the error carries on. On SILJ our daily low of
  2023-06-29 (the original's is 2023-07-06) starts a run of three wrong weekly lows. Variants with
  less memory (no pass, or the judging low becoming the next candidate) fit the original's own lows
  worse (33–38 of 47, 12–15 extra) and are no better end to end.
- Open: the passes the rule does not explain (GDXJ 2023-12-13, SILJ 2024-10-09, Gold 2025-01-26), and
  Silver's 2024-02-27 weekly low, which the original placed afterwards at the next check mark.

## 15. A confirmed low is never cancelled; a pending one is (2026-10-24)

Cody's decision, from the replays: the original removed none of the 379 lows it confirmed and never
drew its cancelled mark over one. What it does drop is its unconfirmed (🕘) low. In the candle
replays of all sixteen assets it drops 332 of them, each on a bar that went under it before it came
due. On 328 of the 332 the next pending low is named a bar or more later; it is never replaced on
the same bar. After a drop, the next low it names is the lowest since the drop bar on 329 of 353.

**What Cipher C does now** (`CipherCEngine.Detect`):

- The first close under the newest confirmed low is a failed cycle, at any gap. Nothing is removed.
- While a found low holds, a bar under it up to the bar before it is due cancels it. The event
  carries the cancelled low's price (`TierRun.Cancelled`, `TierRun.CancelledLow`).
- The daily search then holds nothing and looks again from the next bar. The cycle line's cross,
  which may not come again, moves the low onto the undercutting bar. That path is used before the
  first low and on charts with no daily tier.
- On the due bar the low is confirmed whatever the bar's low (section 13).
- `TierRun.HeldAt` is the low holding at the end of each bar.

| All sixteen candle replays | Before | Now |
|---|---|---|
| Exact daily lows, of 1,244 | 946 | 958 |
| Extra daily lows | 246 | 256 |
| Weekly lows, of 463 | 280 | 284 |
| Extra weekly lows | 176 | 172 |
| Check marks on the original's bar | 340 | 332 |
| Cancellations, against the original's 332 drops | (confirmed lows only) | 470, 197 on its bar |

**Studied and not adopted:**

- **When the original names a pending low.** Given the bar it names one on, the low is the lowest
  since the highest high after the last confirmed low on 454 of 528 (86%). No price shape beats
  "the next bar after the gate" (38%). A Schaff trend cycle trigger (length 0.3, fast 0.2, slow
  1.25 of the shortest cycle, first bar no sooner than the gate with the line over 15) names the
  original's bar 405 of 854 times against 258. Held out one asset at a time, it is 381 of 854 and
  better on 15 of 16 assets. In the engine it loses: daily 925, extra 310, check marks 321, with 485
  pending lows the original never shows. A leave-one-asset-out decision tree over bars at risk
  (Schaff, RSI, distances to the averages, bar shape, age) gets at best recall 0.52 at
  precision 0.40.
- **Keeping an undercut low near its due bar.** The original keeps 76% of lows undercut 1 bar
  before due, 52% at 2, 29% at 3, and drops nearly all from 5 on. Its rare long holds through
  undercuts are overdue cycles: Coinbase 2025-12-31, 13 bars under and confirmed on the due bar;
  oil 2025-02-12 and 2026-04-07; MARA 2024-12-31; MicroStrategy 2023-08-18. Dropping only with 2 or
  more bars left: daily 964, extra 276, weekly 280. With 3 or more: 968, 298, 279.
- **Re-picking the pending low every bar** (lowest since the highest high after the last low)
  instead of keeping the one found. On 7,458 bars where the original shows a pending low, ours
  matches on 5,277, the re-pick on 5,081. The re-pick fills 775 bars where we hold nothing but
  breaks 1,018. The original keeps the low it found.
- **The LOW / MID / HIGH tag** loosely follows how far above the low price is (medians 4.9, 7.4,
  15.2%), but the ranges overlap heavily. The rule is the bar's own (section 20).


## 17. The original's default count on symbols it does not tune (2026-10-25)

On a symbol outside its list the original's table says "Unoptimised Asset Detected" and "Default
Count Selected", and shows no daily band. Its final drawings were read on 17 such daily charts
(private captures, cf-tv-export cap5, plus SP:SPX from cap2). Its shortest gap between daily lows is
18 to 23 bars on each of them, stocks and coins alike. The replica's best shortest cycle is 26 to 33
(stocks 30 to 33, coins 28 to 30, forex 29 to 30). So the default is a count in **bars**, about 32,
whatever the chart's clock.

Cipher C's fallback is now the S&P's ranges and weekly rule counted from 32 bars
(`CipherCEngine.Fallback`). 32 was chosen on AAPL, TSLA, NVDA and MSFT (daily F1 0.744/0.691/0.685/
0.610 → 0.758/0.751/0.727/0.679). The other 13 were only scored, Cipher C finding its own lows:

| Held out | Daily F1, old → 32 | Weekly F1, old → 32 | Old fallback |
|---|---|---|---|
| AMZN | 0.783 → 0.822 | 0.673 → 0.775 | S&P (36) |
| JPM | 0.661 → 0.718 | 0.486 → 0.557 | S&P |
| XOM | 0.755 → 0.815 | 0.701 → 0.621 | S&P |
| KO | 0.732 → 0.780 | 0.462 → 0.587 | S&P |
| SPY | 0.710 → 0.758 | 0.593 → 0.595 | S&P (alias) |
| QQQ | 0.645 → 0.706 | 0.500 → 0.554 | Nasdaq (alias) |
| SOL | 0.500 → 0.649 | 0.296 → 0.437 | Bitcoin (48) |
| XRP | 0.605 → 0.772 | 0.500 → 0.526 | Bitcoin |
| LTC | 0.601 → 0.784 | 0.456 → 0.529 | Bitcoin |
| EURUSD | 0.709 → 0.738 | 0.486 → 0.577 | S&P |
| USDJPY | 0.764 → 0.773 | 0.642 → 0.561 | S&P |
| Brent (UKOIL) | 0.708 → 0.771 | 0.549 → 0.495 | S&P |
| Nikkei 225 | 0.710 → 0.744 | 0.577 → 0.537 | S&P |
| **All 13** | **0.700 → 0.762** (1,007 → 1,170 of 1,529, extra 341 → 373) | **0.551 → 0.575** (336 → 373 of 673) | |

- **The weekly gap.** Most of the default's weekly lows span two daily lows (2 on 34 of 58 on AMZN), so
  the S&P's rule applies: two daily counts less two bars each, 60. Fed the original's own daily lows,
  56 fits better (F1 0.752 → 0.793 over six stocks). With Cipher C's own daily lows it is a wash
  (mean 0.582 at 60, 0.584 at 56, 3 better and 3 worse), so 60 stays.
- **Which tickers the original tunes** (cap6-cap8, final drawings, daily candles). It tunes one
  TradingView feed per asset. Ticker alone is enough for some (BATS:GDX, BATS:COIN are "Finetuned"
  though its list says ARCA and NASDAQ), not for others. Each row below says "Unoptimised Asset
  Detected", and Cipher C's default count matches the original's lows better than the preset the
  replica used to give it:

  | Chart | Daily F1, preset → default | Weekly F1, preset → default | Preset |
  |---|---|---|---|
  | TVC:SPX (CBOE:SPX opens it) | 0.697 → 0.739 | 0.547 → 0.648 | S&P |
  | OANDA:SPX500USD | 0.689 → 0.735 | 0.418 → 0.544 | S&P |
  | CAPITALCOM:SPX500 | 0.689 → 0.727 | 0.485 → 0.537 | S&P |
  | CME_MINI_DL:ES1! | 0.658 → 0.735 | 0.569 → 0.584 | S&P |
  | BATS:SPY | 0.710 → 0.758 | 0.593 → 0.595 | S&P |
  | NASDAQ_DLY:NDX | 0.642 → 0.706 | 0.509 → 0.631 | Nasdaq |
  | BATS:QQQ | 0.645 → 0.706 | 0.500 → 0.554 | Nasdaq |
  | COINBASE:BTCUSD | 0.632 → 0.694 | 0.493 → 0.523 | Bitcoin |
  | BINANCE:BTCUSDT | 0.629 → 0.714 | 0.492 → 0.548 | Bitcoin |
  | COINBASE:ETHUSD | 0.583 → 0.690 | 0.465 → 0.370 | Ether |
  | OANDA:XAUUSD | 0.688 → 0.769 | 0.433 → 0.691 | Gold |

  BATS:DIA, BATS:GLD, BATS:SLV, BATS:USO, BATS:UUP, BATS:IBIT and COMEX_DL:GC1! are unoptimised too
  (not scored). BITSTAMP:BTCUSD, TVC:USOIL, BATS:COIN and BATS:MSTR are finetuned (controls). The S&P preset is its table for SP:SPX, which the Basic plan cannot open (it serves
  SP_DLY:SPX, unoptimised).

  Cipher C now drops the ETF and future aliases (SPY, QQQ, DIA, ES, GLD, IBIT and the rest) and IXIC,
  the Nasdaq Composite. It keeps every spelling of the instrument itself, on any feed. That is the
  original on the feed it tunes, not on the others (the Coinbase, Binance and OANDA rows). Cody,
  2026-10-25: keep it per asset, every feed of an asset taking its preset.
- **The trigger, again** (the first eligible bar is still the best, 38% of 854 namings):
  - depth gates and a floor under the candidate: at most +0.004 F1;
  - Heikin-Ashi green 37%, RSI over 30 38.5% (a tie);
  - smoothed turns: the best is a 4-bar EMA of the high-low midpoint, a tie;
  - weekday: no effect;
  - the bar after the low: no feature separates "named now" from "named later" (AUC 0.58 at best,
    for the low's next low and the close's rise in ATR).
- **The table's bars counter (row 5, column 4)** counts from the pending low, not from its naming.
  After a pending low is dropped it keeps counting from the dropped low, with no label drawn, until
  the next one is named (1,927 bars in 327 runs on the sixteen replays).

## 18. Off the daily chart: the daily count in the chart's bars (2026-10-25)

On any chart that is not daily the original's table says "Timeframe Not Optimised, Switch to
Daily", and it still draws daily and weekly lows. Its final drawings were read on weekly and 4-hour
charts of the Dow, gold, the miners and Bitcoin (private captures, cf-tv-export cap5). The shortest
daily cycle that fits its daily labels best is the preset's own, **in bars**: the Dow 36 on weekly and
on 4-hour (preset 36), gold 23 on weekly (22), the miners 21 (22), Bitcoin 51 on 4-hour (48). Counted
in time, Bitcoin's 4-hour chart would need 324 bars.

So off daily Cipher C runs the preset as if each bar were a day of the preset's own calendar
(`CipherCProvider.Compute`: `BarClock(1, preset.CalendarDays)`): every daily range is in bars, a
"week" is five or seven bars, the weekly ranges and window are the preset's weeks in those bars. Each
bar carries `__in_bars`, which turns the spoken units into bars and adds "Not tuned for this
timeframe" to the preset line. Cody, 2026-10-25: copy the original.

`MeasureBars` had a bug the 4-hour charts exposed. TradingView's Dow 4-hour chart has two bars a
session (13:30 and 17:30 UTC), so half its gaps are the 20-hour night and the median gap sat on the
20-hour cut-off: read as one bar a day. That happened to match the original (it counts bars), which is
why the Dow and the miners scored 0.836 and 0.782 on 4-hour before. A market that does not trade on
weekends with bars under a day and a half apart is now judged by its bars per date.

| Chart (rule read here) | Daily F1, before → after | Weekly F1, before → after |
|---|---|---|
| Dow 1W | none → 0.755 | 0.365 → 0.773 |
| Gold 1W | none → 0.846 | 0.214 → 0.889 |
| GDX 1W | none → 0.960 | → 1.000 (3 labels) |
| Bitcoin 1W | none → 0.667 | → 0.667 (3 labels) |
| Dow 4h | 0.836 → 0.836 | 0.577 |
| Gold 4h | 0.22 → 0.748 | 0.528 |
| GDX 4h | 0.782 → 0.782 | 0.605 |
| Bitcoin 4h | 0.245 → 0.729 | 0.610 |

Held out (cap9, captured after the rule was written; both builds scored on the same captures):

| Chart | Daily F1, before → after | Weekly F1, before → after |
|---|---|---|
| Nasdaq 4h | 0.692 → 0.692 | 0.476 → 0.476 |
| Silver 4h | 0.233 → 0.761 | 0.057 → 0.533 |
| Ether 4h | 0.325 → 0.755 | 0.265 → 0.628 |
| Coinbase 4h | 0.567 → 0.567 | 0.552 → 0.552 |
| Oil 4h | 0.254 → 0.745 | 0.161 → 0.554 |
| Dollar index 4h | 0.273 → 0.829 | 0.197 → 0.577 |
| **4h pooled** | **0.398 → 0.749** | **0.300 → 0.558** |
| Nasdaq 1W | none → 0.727 | 0.301 → 0.476 |
| Silver 1W | none → 0.870 | 0.267 → 0.800 |
| Ether 1W | none → 0.857 | 0.333 → 0.200 |
| Coinbase 1W | none → 0 (4 labels) | 0.250 → 0 (2 labels) |
| Oil 1W | none → 0.763 | 0.316 → 0.409 |
| Dollar index 1W | none → 0.791 | 0.514 → 0.375 |
| **1W pooled** | **none → 0.763** | **0.332 → 0.400** |

- The 4-hour result holds out. The Nasdaq and Coinbase have two bars a session, so the old bug
  already counted them in bars.
- On weekly charts the daily lows hold out, the weekly lows only partly: recall fell 0.76 → 0.39 as
  extras fell 185 → 38. Three charts better, three worse. The weekly tier on a weekly chart (the
  window in weeks of five or seven bars, the weekly lows picked from the daily lows) is not yet the
  original's, and its "Weeks since last WCL" off daily is not decoded.

**Weekly charts, read further (2026-10-25).** On the ten weekly charts (cap5, cap9):

- **The table's week counts are bars divided by the preset's week.** The unconfirmed "Weeks Since
  Last WCL" is the bars since the newest weekly low divided by five (seven on Bitcoin and Ether),
  rounded down, on all nine charts with one: the dollar 24 bars → 4, Ether 37 → 5, the Dow 80 → 16.
  The confirmed count runs from the weekly low before the newest. It stays two back when the newest
  was drawn on the daily low just confirmed and no daily low has been confirmed since, as section 14
  says: the dollar 171 → 34, Gold and GDX 212 → 42, oil 255 → 51. That holds on 8 of 9; Silver
  (43, from 2022-08-28) is not explained. "Days since Last DCL" is in bars too.
- **The dollar's, Dow's and Nasdaq's weekly rule holds on weekly charts.** Fed the original's own
  daily lows, the every-two-daily-lows rule (`ctz/w1w.py`, gap 2 × (shortest − 2) bars) draws the
  dollar's weekly lows 7 of 9, the Dow's 19 of 22, the Nasdaq's 17 of 21. The misses are at the start
  of the history. So on those charts Cipher C's weekly misses come from its daily lows.
- **Ether fits that rule on its weekly chart, but not end to end.** On the original's own daily lows,
  Ether's weekly chart gets 9 of 9 with the every-two rule (2 extra) against 2 of 9 with the old pick,
  and its daily chart 45 of 57 against 39. Tried in the engine with Cipher C's own daily lows: weekly
  chart 2 → 6 of 9, daily 33 → 34 of 57, 4-hour 38 → 32 of 54. That is 73 → 72 correct with 3 more
  extras, so it was not adopted.
- **Oil's weekly chart fits neither rule.** The old pick, 55 of 57 on oil's daily chart given the
  original's daily lows, is 14 of 23 on its weekly chart. Changing its gap makes no difference.
  The every-two rule is at best 22 of 23 with 9 extra (gap 32). Not decoded.
- Nine Ether lows and two Coinbase lows are too few to fit a rule to. The original itself says
  "switch to daily" here. So the weekly tier on weekly charts stays as built.

## 19. The daily tier is the original's rule (2026-10-27)

The daily tier has its own detector, `CipherCEngine.DetectDaily`, read bar by bar from the original
in TradingView's bar replay on candles on all sixteen tuned assets, and on three untuned ones held
out (private captures, cf-tv-export cap2, cap10, cap11; study model `ctz/sim_dss2.py`, which the C#
matches bit for bit). The weekly and yearly tiers keep `Detect`. With d the shortest daily cycle
the detector counts in bars:

1. **Naming.** Once the last low is d − 1 bars back, and d − 1 bars from where its hold began, a bar
   names a low when Bressert's double-smoothed stochastic (s = EMA(stoch(close, high, low, N)),
   DSS = EMA(stoch(s, s, s, N)), both EMAs the preset's length) is above its 5-bar EMA and between
   oversold 10 and overbought 75. Found by lowering the original's overbought input on a replay until
   a naming vanished: the last level that still names it is the DSS rounded up, on every date tried.
   N and the EMA per preset: Bitcoin, Coinbase, MicroStrategy 14; Ether 9; GDX, GDXJ, Gold, the
   dollar 8; Silver, SILJ 10; others 10; EMA 9, Bitcoin 14.
2. **The second way** (Gold, Silver, GDX, GDXJ, SILJ, the dollar). A bar names a low by going above
   the last one-bar swing high (at least two bars back), when no bar since it formed has closed above
   it and the bar is above the low's own high. Read from a GDX replay with the overbought input at
   1, where every naming is this way: 59 of 59 named, no false bar of 461. On the other assets it
   names far too early, and on Platinum it costs 0.939 → 0.807, so it is per preset.
3. **The low.** The lowest one-bar swing low after the cycle's top, the top being the highest one-bar
   swing high from the later of the bar after the last low and the bar before the last low was
   confirmed (the highest bar there if there is none), at most 99 bars back, and after any pending low
   dropped since.
4. **The hold.** Half of d rounded down, from the low if it is at least d − 2 bars after the last,
   else from the bar before the naming bar; never on the naming bar. A low due on a bar that goes
   under it still confirms.
5. **The drop.** A bar under the pending low within d / 3 bars of naming it, rounded up, drops it;
   the search goes on from there. After that the low is locked.
6. **The start.** The lowest bar of the first d bars (first of equal lows); not drawn.

Scored from the first low the original and the study share, then on the study's own lows to the
end (a wrong low early would otherwise shift the rest; the chart's own start is rule 6).

| Set | Original's lows | Ours | Same bar | F1 |
|---|---|---|---|---|
| Sixteen tuned replays (rules read from their 2022–26 bars) | 1240 | | 1183 | 0.951 (before 2022 0.950, after 0.953) |
| Platinum, held out, untuned | 166 | 164 | 155 | 0.939 |
| Apple, held out, untuned | 162 | 161 | 159 | 0.985 |
| EUR/USD, held out, untuned | 142 | 141 | 136 | 0.961 |

Left on the tuned replays, with the original's lows put back at each of its confirmations: 18 drops
(it keeps a low after an early undercut, or drops one late, on the Dow, the dollar, Silver, GDXJ,
Ether), 7 namings a bar or more early (Riot, Bitcoin twice, Ether, the dollar twice, Gold), and one
candidate. Nothing tried separates them: the DSS and its EMA, the close, the undercut's size, a close
or open under, lows before the candidate, time past the longest cycle, a weekly lock. Tried and worse
for naming: a second DSS of any length, other price sources, a 4-hour DSS, latches, a pivot plus a
close above the previous high, shallow trees.

**The weekly tier,** run on our own daily lows (`ctz/we2e.py`): the presets' weekly rules score F1
0.799. Ether keeps the first rule without its "double" (`CyclePreset.WeeklyDoubles`; a weekly low
after a lower one is a new candidate) and SILJ moves to the first rule from the next-daily one: 0.839
(before 2022 0.802 → 0.837, after 0.794 → 0.841; Ether 0.61 → 0.85, SILJ 0.61 → 0.96, no asset
worse).

The original draws no weekly lows on untuned assets, but its table still counts "weeks since last
WCL", confirmed and unconfirmed. On the three held-out replays every drop in that count lands on
exactly one of its daily lows (weeks = bars / 5, rounded down), so its hidden weekly lows can be read
(`ctz/hidw3.py`). They are a weekly test that was never fitted. Cipher C's fallback rule (every two
daily lows, gap two shortest cycles less two bars, 60) on the original's daily lows: EUR/USD 10 of 10,
Platinum 12 of 14, Apple 9 of 15 with 3 extra, F1 0.849 in all; on our own daily lows 0.822. The
first-rule and next-daily rules score 0.720 and 0.473 there. Apple's misses are not the rule's "move"
step (a lower next daily low replaces a fresh weekly low): without it Apple gains 2, but every tuned
every-two asset and the other two held-out ones collapse. Cipher C still draws these lows.

**The original's table, bar by bar** (`ctz/tabcmp.py`, `ctz/unc2.py`, `ctz/wconf_t.py`). "Days since
last DCL" is t − low; the daily window's count is t − low + 1, the day of the cycle, so it reads one
more on every bar where the lows agree. "Days since unconfirmed DCL" is t − the pending low, and it
keeps counting a dropped low until the next naming. Cipher C's pending lows reproduce it on 96.4% of
rows. The original's weekly low is confirmed only on a daily confirmation, once the next weekly low
is fixed, as in Cipher C. A daily pending low's companions name the held low (`TierRun.HeldAt`).

Prepending history moves the weekly chain for longer than the old search did: the chain starts from
the first bar, as the original's does. Pinned by a test (daily lows move only in the first 300 bars
of 1400; for the weekly chain since section 23, see "The start" there).

## 20. The pending low's LOW / MID / HIGH tag, and its 🔄 (2026-10-27)

The original writes LOW, MID or HIGH on its pending daily label (🕘), and the word changes from bar
to bar while the low is held: it follows the price bar, not the low. On each bar it is two points,
read from the replays (private, `ctz/grademodel.py`, `ctz/gradeds.py`):

- one for the Awesome Oscillator above zero: SMA 5 less SMA 34 of hl2;
- one for the close above the VWAP of hlc3 anchored at the calendar month (reset on the first bar of
  each month; no point when the month has no volume).

0 is LOW, 1 MID, 2 HIGH. `CipherCProvider.Grade` computes it; the pending low carries it per bar
as `Cycle Low Pending_grade` on every bar its low is held (`TierRun.HeldAt`), and its speech says it
("…on day 76, mid confluence: …"). The C# matches the study model on every row:

| Set | Labelled bars | Same grade |
|---|---|---|
| Sixteen tuned replays, 2022-2026 (the rule was read here) | 7,486 | 98.65% (per asset 97.3-99.4) |
| Fourteen of them held out in time, 2020-03 to 2022-01 | 4,874 | 98.75% |

Bars where Cipher C holds a low too: 7,192 of the 7,486 (98.69% same). Worse sources for both
parts: close 95.9%, hl2 98.1%, ohlc4 97.8%. Every miss is the original one grade higher, with
the oscillator under zero, on a single bar (101 of 101 a lower low than the bar before, the low
within one ATR of the pending low, most of them down closes). Nothing tried separates them: lags,
divergence, the DSS, weekly and monthly oscillators, RSI, averages, the distance in ATRs; the bars
are the replay's own. The original's index, dollar, gold, silver and oil charts have no volume, so
no VWAP point, and are never HIGH; neither is Cipher C on a chart without volume. **Caveat:** a
data source that does give an index volume would give Cipher C HIGH where the original cannot.

**🔄.** A pending label also carries 🔄 when it is not the first pending low since the last
confirmed one, that is when an earlier one was dropped: 880 of 884 labels (2022-26), 583 of 584
(held out). It does not change during the pending low's life. Cipher C's pending low named after a
cancellation names the cancelled low it replaces (`_replaced`), as its confirmation already did:
same as the original's 🔄 on 831 of 844 namings both share (2022-26) and 535 of 548 (held out); the
rest follow the drop differences in section 19. Confirmed labels can carry 🔄 too.

## 21. Faster confirmation, and the original's other inputs (2026-10-27)

The original's inputs were compared one by one by setting each on its study and reading the
drawing back (private, `probe/fc_*.jsonl`, `cap13/`). Its asset-adjustment inputs act only while
their group's switch is on; with the switch on and every value at its default, the drawing is the
switch-off drawing (six assets).

| The original's input | What it does there | Cipher C |
|---|---|---|
| Asset preset | which cycle ranges | Asset preset |
| Cycle multiplier | shortest cycle × the value above 1, ÷ it below −1 | Daily cycle shortest / longest (a count, not a multiplier) |
| Trigger adjustment | the DSS trigger EMA ≈ 5 − the value | fixed at 5 |
| Slow adjustment | the DSS EMA = 9 − the value, exactly | the preset's, not a setting |
| Overbought / oversold | the band the DSS names a daily low in | Confirm level / Arm level (75 / 10) |
| Faster confirmation | the swing-high break switched on | Faster confirmation (Advanced, off) |

**Faster confirmation** ("This will make the indicator confirm lows faster") does not change the
hold. On the Dow, where the break is off, it names pending lows earlier (2022-03-15 for 03-16,
05-04 for 05-06, 06-24 for 06-27) and still confirms floor(36 / 2) = 18 bars after the hold
begins. Every low it names passes the DSS gate or the swing-high break (section 19). It is the break
switched on for an asset that lacks it:

- **The Dow, in bar replay with it on (2022-2026):** the model with the break names 41 of the original's 49
  lows on the same bar with the same low; without the break, 22. The control, with it off and
  without the break, is 39 of 44.
- **Final drawings, model with the break vs without, against the original with it on (F1):**
  Bitcoin 1.000 vs 0.846, Coinbase 1.000 vs 0.923, Ether 0.946 vs 0.792, the Dow 0.857 vs 0.770.
  With it off, the model without the break fits as before (0.980, 0.919, 0.970, 0.874).
- **Gold and GDX, which always break:** the drawing does not change at all.
- **The S&P:** neither fits end to end on its long history (0.677, 0.673; 0.788 with it off),
  because one moved low moves the rest. Step by step, seeded at each of the original's lows,
  the next low is right 152 of 174 times with the break, 133 without; with it off, 144 of 164
  without the break.

Cipher C's setting turns the break on (`preset.PivotBreak || FasterConfirmation`); on the metals and
the dollar it changes nothing. The Confirm level / Arm level settings bound the DSS band on the
daily tier; their old STC meaning (arm, then confirm) now applies only to a weekly chart's own
weekly tier and to the yearly tier. In the original they act only with the adjustment switch on;
Cipher C's act on their own. Not ported: the trigger and slow adjustments (each preset's DSS is
fixed), and the cycle multiplier as a multiplier.

## 22. Dropping until due on four assets, the yearly low, and Heikin-Ashi (2026-10-27)

**The drop, read from cancelled marks.** The original's full-history drawings, with its 80- and
32-bar limits switched off, keep its cancelled (❌) daily marks, which the replays' final frames did
not (private, `probe/y_*.jsonl`; `ctz/ddign.py`, `ctz/dd.py`, a Python replica of
`CipherCEngine.DetectDaily` that matches the C# on every low). Rule 5 of section 19 drops a pending
low only on a bar under it within d / 3 bars of naming it. Every bar under a pending low later than
that, and before it is due, was a cancellation on the Dow (13), the dollar (8), Silver (3) and
Ether (2). On every other tuned asset it was a keep: 64 such bars, 6 of them on the Nasdaq, whose
cycle numbers are the Dow's. GDXJ has one of each and is left as it was.

So the preset carries `DropUntilDue`: on the Dow, the dollar, Silver and Ether any bar under the
pending low before it comes due drops it. A bar under it on the due bar still confirms it.

| Set | Before (d / 3 everywhere) | After |
|---|---|---|
| Sixteen tuned replays, final frames | 1,185 of 1,244, 65 others, F1 0.950 | 1,202, 35 others, F1 0.969 |
| Same assets, whole history (probe) | 1,310 of 1,372, 71 others, F1 0.952 | 1,328, 36 others, F1 0.971 |
| Platinum, Apple, EUR/USD (untuned), whole history | 445 of 466, 16 others, F1 0.960 | no change |

Per asset, whole history, daily lows on the original's bar / others, and weekly F1:

| Asset | Daily before | Daily after | Weekly F1 |
|---|---|---|---|
| The Dow | 138 of 148, 16 others | 147 of 148, 0 | 0.692 → 0.794 |
| The dollar | 107 of 115, 11 | 111 of 115, 2 | 0.830 → 0.848 |
| Silver | 51 of 56, 7 | 54 of 56, 2 | 0.696 → 0.783 |
| Ether | 162 of 165, 7 | 164 of 165, 3 | 0.855 → 0.897 |

No asset is worse. Why the Dow differs from the Nasdaq on the same numbers is not known: the flag is
fitted per asset, from these drawings.

**Heikin-Ashi, held out.** The original's whole-history drawings on Heikin-Ashi bars (Dow, Nasdaq,
gold, Bitcoin; `ctz/hamk.py`) were never used to fit anything. Cipher C run on the same Heikin-Ashi
bars (TradingView's formula), F1 candles → Heikin-Ashi, before the flag:

| Asset | Daily | Weekly |
|---|---|---|
| The Dow | 0.914 → 0.923 | 0.692 → 0.703 |
| Gold | 0.920 → 0.947 | 0.744 → 0.698 |
| Bitcoin | 0.980 → 0.980 | 0.909 → 0.909 |
| The Nasdaq | 0.987 → 0.987 | 0.894 → 0.821 |

With the flag the Dow's Heikin-Ashi daily goes 138 of 147 with 14 others → 146 of 147 with none,
weekly 0.703 → 0.810. The daily rule carries over to Heikin-Ashi; the weekly tier is weaker there
on the Nasdaq and gold, not studied.

**The yearly low (🅨) is not computed from price.** On Heikin-Ashi bars the original's daily lows
move (Bitcoin 47 → 45, the Dow 264 → 262, gold 175 → 178 marks), but all 23 of its yearly marks on
those four assets keep their dates; only the mark's height follows the bar's low. Each is drawn on
the first bar at or after a calendar date (00:00 UTC), so gold's, whose daily bars open the evening
before, all fall on the first bar of a UTC week. None of its inputs moves them (the cycle
multiplier, the DSS adjustments, the overbought and oversold levels, and one more input that changes nothing visible; one Dow set
hit a script error and drew nothing). The marks do not follow any rule tried: weekly or daily DSS,
the lowest low, the spacing, an arm and fire. Three crypto stocks share one date, one mark sits 19%
above its week's low, and untuned assets (Platinum, Apple, EUR/USD) and the S&P, which has no yearly
row in its table, draw none. They read as a table of dates per tuned asset. Cipher C's yearly tier
is still its own computed one; porting the dates would put the original's labels in this public
repo (TODO question for Cody).

## 23. The weekly tier: one state machine, and by weeks on the metals (2026-10-27)

Replaces sections 14 and 16 and the first rule's four-week margin. The rules were read from the
original's candle replays (the table's two week counts, logged per bar, 2022–26) and checked on its
whole-history drawings on nineteen assets (`strategy-lab-data/cf-tv-export/ctz/weekly2/`: `wt3.py`,
`wt3f.py`, `wm3.py`, `we2e2.py`). Every step is acted on the bar a daily low is confirmed.

**Everywhere but the metals** (`CipherCEngine.WeeklyFromDaily`). U is the newest weekly low.

1. The second daily low after U is drawn at once as the candidate P.
2. The next daily low judges it. Under P: P is cancelled, and that daily low is a weekly low at any
   distance. Not under P: P is a weekly low, and that daily low counts as the first after it.
3. A weekly low is never taken back.
4. A "double": before P is drawn, the first daily low F after U is a weekly low of its own when the
   second is not under F and either
   - F is under U at least 1.25 shortest daily cycles after it (the far double; off on Ether:
     F1 0.82 with it, 0.88 without), or
   - U was a candidate made a weekly low, U is more than 2% under the weekly low before it, and F is
     not under U (the deep double). **Superseded by section 24:** F under the daily low before U, at
     least 16 bars after U.
5. The confirmed weekly low, which the week count runs from, is the one before the newest.

**Gold, Silver and the miners** (`CipherCEngine.WeeklyByWeeks`, `CyclePreset.WeeklyByWeeks`). The
same candidate, judged by distance from U, in weeks short of the table's longest weekly cycle (28
weeks for Gold and Silver, 26 for the miners):

- A new weekly low needs 8 weeks short of it (20 or 18 weeks). Nearer, a lower one takes U's place
  and U is cancelled.
- An undercut cancels the candidate, and the lower low is judged the same way. If it is neither far
  enough nor under U, it is only the new reference, drawn nowhere.
- A candidate neither far enough nor under U is dropped, not cancelled.
- The double: the first daily low after the reference, under it, with the second not under it, is a
  weekly low 14 weeks short (14 or 12 weeks) from U. Nearer, there is no double.

**Scores**, weekly lows after the original's first, exact / extra / missed, F1:

| Daily lows fed in | Rules before | These rules |
|---|---|---|
| The original's own | 582 / 96 / 131, 0.837 | 646 / 66 / 67, 0.907 |
| — metals | 0.772 | 0.848 |
| — the rest | 0.843 | 0.913 |
| Cipher C's, from the chart's first bar (the app) | 0.794 | 0.838 (C#: 595 / 111 / 118, 0.839) |
| — metals | 0.750 | 0.809 (C# 0.815) |
| — the rest | 0.799 | 0.841 |

- **Held out by time.** The machine was read from the 2022–26 replays. Before 2022, on the original's
  daily lows, the non-metal rule scores 0.912 (the rules before, 0.840).
- **The metals' margins were fitted on the whole history.** Fitted after 2022 only, they score 0.694
  before 2022 (the rule before, 0.683). Fitted before 2022 only, they score 0.817–0.822 after (the
  rule before, 0.814). Most of the metals' gain is in sample.
- **By asset, on Cipher C's own daily lows**, before → after:
  - Worse: GDXJ 0.76 → 0.55, Bitcoin 0.90 → 0.86, MicroStrategy 0.91 → 0.86, GDX 0.91 → 0.87,
    Apple 0.74 → 0.72, oil 0.90 → 0.89.
  - The same: the S&P 0.81.
  - Better: the Dow 0.81 → 0.99, SILJ 0.75 → 0.96, gold 0.73 → 0.88, Coinbase 0.67 → 0.88,
    EUR/USD 0.81 → 0.88, Silver 0.60 → 0.70, Ether 0.65 → 0.70, Platinum 0.78 → 0.84, and the rest
    by 0.01–0.04.
  - On the original's own daily lows GDXJ also falls, 1.00 → 0.78.
- **The start.** The score from the chart's first bar (0.838) is below the one from the original's
  first weekly low (0.855). The machine counts daily lows, so a different first daily low can leave
  it one out of step until an undercut or a double brings it back. The original's earliest marks are
  cut off by the chart's label limit, so where it starts cannot be read. Started 1–8 daily lows
  later on real candles, the chain is back in step within 32 daily lows (most within 10). On the
  test Bitcoin bars cut by 17–500, it agrees again by bar 1,188 of 5,416. On the 1,400-bar probe
  series, with five weekly lows, it can stay out of step to the end.
- **Not reproduced.** After some candidates are made weekly lows, the original moves its reference
  on by one daily low. Nothing tried separates those cases. Supplied from its replays, they add only
  2 weekly lows on gold (metals 0.848 → 0.879).

**Held out: Heikin-Ashi bars.** The original's whole-history drawings on Heikin-Ashi bars (the Dow,
the Nasdaq, Bitcoin, gold; never used in fitting) against Cipher C run on the same Heikin-Ashi bars
from the first bar, weekly F1 (daily in brackets):

| | Candles | Heikin-Ashi |
|---|---|---|
| The Dow | 0.992 (0.997) | 0.992 (0.997) |
| The Nasdaq | 0.902 (0.987) | 0.962 (0.986) |
| Bitcoin | 0.900 (0.978) | 0.900 (0.978) |
| Gold (by weeks) | 0.900 (0.940) | 0.872 (0.968) |

Before the weekly state machine the Heikin-Ashi weekly scores were the Nasdaq 0.821 and gold 0.698.
On the original's own Heikin-Ashi daily lows the machine draws its weekly lows exactly on the Dow,
the Nasdaq and Bitcoin (gold 0.829, as on candles). Scripts: `whasc.py`, `wha.py` in the lab.

**Parity.** On the nineteen assets' candles (`ZzCipherCWeeklyParity`, not committed), the C# rules
match the Python models fed the C# daily lows. The weekly lows are equal on all nineteen, and so
are the non-metal cancellations. On a metal bar that cancels both the candidate and U, the engine
carries U.

## 24. The deep double: under the daily low before the weekly low (2026-10-27)

Replaces section 23's deep double (U more than 2% under the weekly low before it). The far double,
the candidate and the metals are unchanged.

**The rule** (`CipherCEngine.WeeklyFromDaily`, `CipherCEngine.DoubleMinBars`). U is a candidate
made a weekly low; M is the daily low before U; F, the first daily low after U, judged U, so F is
not under U. F is a weekly low of its own when the second daily low after U is not under F and:

- F is under M, and
- F is at least 16 bars after U.

**How it was found.** On the original's whole-history drawings, every pair of weekly lows was
listed with the daily lows around the newer one (`wg0.py`, `wg1.py`, `wg2.py`; the model is
`wt3g.py`; all in the private lab's `ctz/weekly2`). The candidate cases split on one comparison:

| After a candidate made a weekly low, second daily low not under the first | F under M | F not under M |
|---|---|---|
| F is a weekly low too | 36 | 0 |
| F is not | 4 | 174 |

On these 214 cases the 2% depth rule is wrong 24 times; F under M and at least 16 bars after U is
wrong on none. The four exceptions in the table are all under 16 bars: two of the dollar's (14
bars each) and two of Ether's (12 and 15). The cases are counted on the original's own lows, with
"a candidate made a weekly low" read as one daily low between the two weekly lows (`wg2.py`).

**The 16 bars are fitted.** On the original's own daily lows, non-metals:

| Minimum bars, U to F | 0 | 13–14 | 15 | **16–17** | 18 | 20 | 22 |
|---|---|---|---|---|---|---|---|
| F1 | 0.978 | 0.985 | 0.990 | **0.994** | 0.991 | 0.985 | 0.981 |

As a share of the shortest daily cycle, 0.8 scores 0.984; in weeks (3), 0.983. The far double's
1.25 cycles is still the best (1.2: 0.979, 1.3: 0.993, 1.4: 0.961), and still off on Ether
(on: 0.985).

**Scores**, weekly lows after the original's first, exact / extra / missed, F1:

| Daily lows fed in | Section 23 | This rule |
|---|---|---|
| The original's own, non-metals | 590 / 57 / 56, 0.913 | 643 / 5 / 3, 0.994 |
| — before 2022 | 0.912 | 0.993 |
| — from 2022 | 0.913 | 0.995 |
| The original's own, all nineteen | 646 / 66 / 67, 0.907 | 699 / 14 / 14, 0.980 |
| Cipher C's, from the chart's first bar (the app), all | 595 / 111 / 118, 0.839 | 657 / 51 / 56, 0.925 |
| — non-metals | 0.841 | 0.936 |
| — metals | 0.815 | 0.815 |

- **On the original's own daily lows,** 11 of the 14 non-metal assets now match exactly. The rest:
  the Dow's first years, 3 extra and 3 missed (one daily low out of step until an undercut), the
  dollar 1 extra and oil 1 extra (both candidates at the live edge the original had not drawn yet).
- **By asset, on Cipher C's own daily lows**, before → after:
  - Better: Apple 0.73 → 1.00, MicroStrategy 0.86 → 1.00, Ether 0.69 → 0.97, Riot 0.71 → 0.89,
    the S&P 0.81 → 0.94, Platinum 0.84 → 0.91, oil 0.90 → 0.94, the dollar 0.88 → 0.89, the Dow
    0.985 → 0.992.
  - Worse: Marathon 0.89 → 0.86 (one more extra).
  - The same: the metals, Bitcoin, Coinbase, the Nasdaq, EUR/USD.
- **Not held out.** The comparison was read from all the history at once, so the split by 2022 is
  only a check that it holds on both sides, not a held-out test. Why the original compares with the
  daily low before the weekly low is not known; no transcript or document describes it.
- **The metals.** The same machine with this rule does not fit them (gold and Silver 0.61, the
  miners 0.49–0.62 on the original's own daily lows): they keep `WeeklyByWeeks`.

**Parity.** On the nineteen assets' candles (`ZzCipherCWeeklyParity`, not committed) the C# matches
`wt3g.py` (minimum 16 bars) fed the C# daily lows (`wparcmp3.py`): weekly lows and cancellations
equal on all nineteen. The app-path scores are `wcsscore.py`, before and after on the same
candles.

## 25. Off the daily chart, held out: the drop split checked, and Bitcoin drops until due (2026-10-27)

On a 4-hour or weekly chart the original shows its daily table with "Timeframe Not Optimised,
Switch to Daily" and runs its daily count on the chart's own bars (section 18). Its confirmed daily
marks there (bar replays on candles, 2022–26, private) were never used to fit anything, so they are a
held-out test of the daily engine, and in particular of section 22's split between assets that drop a
pending low on any undercut before it is due and assets that drop only within d / 3 of naming it.
`ctz/tfscore.py` runs the Python replica of `DetectDaily` (which the C# matches) on each chart and
counts the original's confirmed marks from its first.

Each chart under both rules, the original's marks on the replica's bar / theirs + the replica's
others, F1:

| Chart | Section 22 says | 4-hour, d / 3 | 4-hour, until due | Weekly, d / 3 | Weekly, until due |
|---|---|---|---|---|---|
| The Dow | until due | 103/108 + 11, 0.928 | 107/108 + 3, **0.982** | 42/48 + 6, 0.875 | 42/48 + 5, **0.884** |
| The dollar | until due | 124/132 + 16, 0.912 | 129/132 + 4, **0.974** | 20/22 + 3, 0.889 | 20/22 + 1, **0.930** |
| Silver | until due | 125/146 + 28, 0.836 | 135/146 + 9, **0.931** | 10/11 + 3, 0.833 | 11/11 + 0, **1.000** |
| Ether | until due | 127/130 + 7, 0.962 | 126/130 + 6, 0.962 | 24/24 + 1, 0.980 | 24/24 + 1, 0.980 |
| The Nasdaq | d / 3 | 107/116 + 7, **0.930** | 99/116 + 11, 0.876 | 45/48 + 1, **0.957** | 42/48 + 4, 0.894 |
| Gold | d / 3 | 143/149 + 6, **0.960** | 134/149 + 10, 0.915 | 25/25 + 0, **1.000** | 24/25 + 1, 0.960 |
| GDX | d / 3 | 111/119 + 6, **0.941** | 99/119 + 11, 0.865 | 13/13 + 0, 1.000 | 13/13 + 0, 1.000 |
| Oil | d / 3 | 114/121 + 6, **0.946** | 97/121 + 15, 0.833 | 45/47 + 2, **0.957** | 42/47 + 3, 0.913 |
| Coinbase | d / 3 | 25/27 + 14, **0.758** | 21/27 + 17, 0.646 | 2/4 + 1, **0.571** | 1/4 + 1, 0.333 |
| Bitcoin | undecided | 84/88 + 6, 0.944 | 87/88 + 1, **0.989** | 2/2 + 2, 0.667 | 2/2 + 1, **0.800** |

- **The split holds out of sample.** Every asset section 22 put on drop-until-due scores better or
  the same with it on both timeframes; every asset it left on d / 3 scores worse with it (or the
  same, GDX weekly). The split was read from daily drawings only.
- **Bitcoin drops until due** (`CyclePreset.DropUntilDue`, now on for BTC). On its daily chart it
  never undercuts a pending low later than d / 3 and before due, so the daily drawings could not
  tell; the 4-hour and weekly charts both say until due. On the daily chart it removes the one extra
  low it had on the whole history (F1 0.9681 → 0.9683; Heikin-Ashi held out 0.9793 → 0.9804). As the
  app runs on the captured Bitcoin daily candles (from the chart's first bar), its daily lows go 22
  of 22 with 1 other → 22 of 22 with none, and its weekly lows 9 of 10 with 1 other → 10 of 10 with
  none. Pooled over the nineteen captured daily charts, weekly F1 0.924 → 0.926, daily 0.970 → 0.970.
- **Pooled off-daily**, with Bitcoin until due: 4-hour 0.947 → 0.950 (1,084 of 1,136, 62 others),
  weekly 0.942 → 0.944 (229 of 244, 12 others).
- **What is left there.** Coinbase's 4-hour chart: the original's marks skip half a year where the
  replica has three lows, then the original confirms
  nothing for 659 bars; the replica keeps finding lows. Neither rule explains it. Bitcoin's weekly
  chart: the original confirms nothing after its second mark and holds one pending low with 🔄;
  until due takes one of the replica's two others away, not both.
- **Still not separable on the daily chart:** the 17 times the original kept a pending low through
  an undercut within d / 3 of naming it, against 516 drops in the same position (price, grade, 🔄,
  the table's cells, the weekly label, the DSS, the path since naming; a boosted model reaches AUC
  0.66 only). Cipher C drops all of them.

### Symbols the original does not tune, held out

The original's daily marks on 36 captured charts it does not tune (stocks, ETFs, futures, CFDs,
other feeds of the tuned assets; private bar replays, never fitted on) score the default count
(section 17: 32 bars, the S&P's ranges) at pooled F1 0.949 with the d / 3 drop window (3,797 of
3,999 marks, 204 others) against 0.905 dropping until due, and the window is better or level on
every chart but two. So the default drops within d / 3, as built.

The two exceptions are NASDAQ:COIN (0.408 on the default) and NASDAQ:MSTR (0.725), and they are
not exceptions: the original tunes them, matching by ticker as it does on BATS:COIN and BATS:GDX
(section 17). On their presets the same charts score 17 of 19 + 1 (0.919) and 25 of 28 + 2
(0.909). Cipher C already gives them those presets (`PresetForSymbol` drops the exchange), so
nothing changes; without them the default's pooled F1 is 0.955. The weakest default charts are
other feeds of Bitcoin (COINBASE:BTCUSD 0.765, BINANCE:BTCUSDT 0.854), where the Bitcoin preset
scores worse still (section 17).
