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

Only three of CF's cycle-range tables were ever readable on screen: SPX, gold and Ethereum. Bitcoin's
ranges are the ones Camel states on his streams (54–66 days). The other twelve presets in
`CipherCEngine.Presets` are estimates from the nearest of those, and each says so when spoken
("estimated, not CF's").

## 3. What CF draws, and how many parts it has

Thirteen visible parts, plus the legend line (`CF Cycle Trading Indicator [camel] V-2 <value>`):

| # | CF | Cipher C |
|---|---|---|
| 1 | Green ▲ / "D" ring / camel under each daily cycle low | Green triangle under the low (DCL) |
| 2 | Blue stack under each weekly cycle low, one row under the D | Bigger blue triangle (WCL) |
| 3 | Yearly cycle low (described in the docs, never seen) | Bigger gold triangle (YCL) |
| 4 | ✅ on the bar a low is confirmed | Square above the bar (DCL / WCL / YCL Confirmed) |
| 5 | 🔄 pending | Grey dot (Cycle Low Pending) |
| 6 | ❌ cancelled, in a green box | Red cross (Cycle Low Cancelled); the next low is Cycle Low Updated |
| 7 | Live confirmation marker above price | Same square as 4 |
| 8 | LOW / MID / HIGH confluence text | Cycle Low Confluence (1, 2, 3), spoken as low/mid/high |
| 9 | Cycle count "49D" | DCL Count / WCL Count over the low, in days or weeks |
| 10 | Green daily Future Cycle Low Range band | Green tint while the daily cycle is in its window (past only) |
| 11 | Blue weekly FCLR band | In WCL Window (not drawn) |
| 12 | Yellow low-to-low arcs | Cycle Arc, in a strip along the bottom of the price pane |
| 13 | Yellow SMA line | 10-bar SMA, the one Camel trades against |

Added, from the 1.0 docs rather than the frames: left, mid and right translated tops (triangles over
each finished cycle's top) and Cycle Failed (a close under a low after its minimum gap, the cycle
school's "failed cycle").

Hidden numbers, for speech and strategies: DCL Day, WCL Week, the four window bounds, the asset preset.

## 4. The detector, and why it is a Schaff Trend Cycle

The settings panel on CF's "Asset Count Adjustment" page reads: Cycle Length Adjustment Factor,
Fast EMA Adjustment Factor, Slow EMA Adjustment Factor, **Overbought 75**, **Oversold 10**, Faster
Confirmation. A fast and a slow EMA with an overbought and oversold pair are the inputs of a Schaff
Trend Cycle (a MACD put through two stochastics), with its lengths taken from the asset's cycle range.
That is an inference; CF never names its engine.

The rule (`CipherCEngine.Detect`, run once per tier):

1. Schaff lengths come from the tier's mean cycle in bars: fast EMA × 0.5, slow EMA × 1.0,
   stochastic × 0.25.
2. Once the line has been at or under 10, its first cross up through 75 **confirms** a low: the
   lowest swing low since the previous low's minimum gap (60% of the shortest cycle). The low must
   still be the lowest bar up to the confirming bar.
3. A close under a confirmed low before that gap **cancels** it. A close under it after the gap is a
   **failed** cycle.
4. A weekly low is snapped to the nearest daily low, and a yearly one to the nearest weekly low.
   Every higher-tier low is also a lower-tier low: CF draws a D under every W. Bitcoin's 5 August 2024
   crash was a weekly low that the daily detector had passed over, 31 days after the 5 July low.

**Fitting.** The four constants were chosen on the 25 SPX daily cycle lows CF labelled on Camel's
videos (2020–2022 and 2024–2025). It finds 18 of 25 within two bars (72%). Confirmation comes a
median of nine bars after the low.

**Held out.** On CF's own labels it was never fitted to, it finds 8 of 13 within four days: gold 3 of
7, the Dow 3 of 4, Bitcoin 2 of 2 (4 February and 29 March 2026, now pinned by a test). Those labels
were read off video frames by pixel spacing and are ±1–3 days themselves. Most misses are half-cycle
lows CF marks and this does not.

## 5. Bars, timeframes and tiers

A tier runs only where its cycle spans at least six bars and the bar is no longer than its unit. So a
weekly chart has weekly and yearly lows and no daily cycle low at all, which fixes what Cody heard on
Loukas's weekly chart ("dcl" on a weekly chart). An hourly chart runs the daily tier when it has the
history (54 days of Bitcoin is 1,296 hourly bars).

The bar clock is measured from the last 500 bars: the median spacing, and whether any bar falls on a
weekend. Bitcoin counts calendar days; a stock counts trading days, so a stock's weekly bar is five
trading days. (This was inverted while building and caught by a test; see CHANGES.)

## 6. What it is not, and what is missing

- **Not an edge.** Camel's checklist showed none against shuffled prices in this project's tests
  (`docs/CYCLE_SCHOOL_FINDINGS.md`). CF's own SPX counts fall inside its stated 36–44 day window only
  about 22% of the time (22–64 days, median 46). The indicator follows the method; it does not prove it.
- **The lows are drawn on their own bar and known only later**, as CF's are. They are declared
  look-ahead, so no strategy can use them. The events (Confirmed, Cancelled, Updated, Failed, Pending,
  DCL Day, the window flags) are decided on their own bar; a test checks that cutting the history
  short never changes them.
- **Twelve of sixteen presets are estimated.**
- **Not built:** CF's future FCLR boxes to the right of the last bar (the chart has no future bars to
  draw on), the D/W letters and camel stacking, the "49D" suffix (the count is a number), confluence
  that upgrades from LOW to MID to HIGH over the following bars (ours is fixed at confirmation), the
  US macro table and the Bullish Meter.
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
- **Loukas Cycle Detection**, marked deprecated: it still computes on saved charts but is no longer
  offered. Each of its parts has a counterpart: DCL Confirmed is DCL Confirmed; its intermediate cycle
  low (ICL) is Camel's weekly cycle low, WCL; DC Day Count and DC In Window are DCL Day and In DCL
  Window; DC Overdue is DCL Day past DCL Window End; its four-year phase is spoken in Cipher C's
  Bitcoin preset words ("Day N after the April 2024 halving, the … phase"). Cipher C also tiers by
  timeframe, which Loukas left to the user.
- **Pulse Reversal Long**, the StrategyLab catalogue strategy built on the old Cipher C's bottoms. It
  is in the retired list of `CatalogueProvenance` and has not been re-tested on DCL Confirmed.

`docs/CYCLE_LOW_INDICATOR_DESIGN.md` is a different design (a capitulation score, no windows) written
for the pre-registered cycle-school test. It is not what Cipher C does.
