# What's New

<!-- This file holds the CURRENT RELEASE ONLY. The section below belongs to the tag named in
     its heading; anything written after that tag goes under a NEW `## Unreleased` heading above
     it, and the previous release's section moves out (its history is already in CHANGES.md).
     Check with `git diff <lasttag> HEAD -- docs/WHATSNEW.md` before every cut — this file has
     accumulated post-tag entries under an old heading before. -->

## Unreleased

- **Turn the price pane upside down with I** (or `INVERT` in the Go bar), to check a bias. The pitch
  turns over with the picture, so a rising market falls in pitch while it is inverted; the words,
  prices and colours stay the market's, and indicator panes stay upright. Each tab keeps its own
  setting; drawings and alerts keep real prices.
- **An alert fires on the chart you set it on, and nowhere else.** With several tabs of the same
  coin on different timeframes, a price alert set on the 4-hour chart was checked on every tab, and
  a daily or monthly candle spanning the price made it fire again and again. Now it belongs to its
  own symbol, timeframe and provider. Alerts saved before this keep firing on every chart of their
  symbol; set them again to scope them.
- **On a Heikin-Ashi chart, a price "touches" alert needs the market to touch it**, not only a
  Heikin-Ashi wick.
- **Switching tabs no longer fires an indicator "crosses" alert** by comparing two charts.
- **Volume bars on a Heikin-Ashi chart sound and read the colour they are drawn**: a red bar under
  a down candle no longer plays the "up" tone or says "up". Every pane below the price pane
  (volume, oscillators) is drawn, said and sounded from the market's own candles; the price pane
  follows Heikin-Ashi.
- **Indicators update live again on a busy market.** Cipher B and the rest froze between tab
  switches whenever prices ticked faster than the order book could be fetched (about three times a
  second): each tick cancelled the one before it. Now one update runs at a time and the next one
  always uses the newest prices.

## 2.15.0: the chart's fixes from Cody's notes, the 9-13 sequential, and nearby levels you can switch off

A smaller release after 2.14's news desk. Cody has heard the shorter settings, the Donate tab, the
9-13's reading and Cipher B's "9 components"; the rest has not been heard yet, and none of it has
been tried with money. What 2.14 added (the news desk, fifteen chart types, indicator templates and
the rest) is in [CHANGES.md](CHANGES.md) and on the
[2.14.1 release page](https://github.com/churst90/accessible-trade-terminal/releases/tag/v2.14.1).

- **The date under the mouse pointer** no longer sits beside half of a date on the time axis
  ("Oc"): the date it would cut is hidden while the pointer is there.
- **Cipher A's symbols over the highest candles are drawn whole**, below the chart's status lines,
  instead of cut off under them. The chart keeps just enough room above the candles while such a
  symbol is on screen.
- **Donate:** Ethereum, Arbitrum, Base and BNB Smart Chain share one address, so they are one row.

- **Headlines come from Finnhub only.** The optional Alpaca news wire is gone: Finnhub's free key
  covers the same stocks sooner, and coins too. Alpaca as a trading venue is unchanged.
- **Moving onto an indicator counts every component** you can reach with Up and Down, muted ones
  too: Cipher B says "9 components".
- **The Android and iOS apps are not ready yet**, and the manual says so plainly. On a phone or
  tablet, use the website's terminal in the browser.
- **The 9-13 sequential** (Add Indicator, Reversal): a setup counts 1 to 9, then a countdown 1 to
  13, each count a numbered marker you hear as you arrow ("Buy setup 7."). The 9 and the 13 ring
  their own bells; focused on a 9 you hear whether it is perfected. Not yet checked against
  TradingView's version. A new setup restarts the countdown only when it is about as large as the
  one the countdown counts from (at least as large, under 1.618 times), as in the published method.
- **"Near resistance" covers every level on the chart**: pivots, Cipher A's pivots, a volume
  profile's levels, your own horizontal lines and your price alerts, each named after the price
  ("Near resistance at 95,200.00, pivot R1."). Turn it off with **Announce nearby levels while
  navigating** on the Speech tab of Settings.
- **The portfolio values stocks and currencies** on a broker, not only coins.
- **The heatmap keeps its history on every timeframe**, not only one-minute charts.
- **A strategy says when a level source fails** and costs it a setup, instead of quietly never
  trading.
- **Line-style charts no longer name candle patterns** as bars close or form, since no candle is
  drawn; ask with Alt+Shift+D and it still tells you.
- **Settings say less**, with clearer names: "Announce other markers on the bar while navigating"
  and the rest. A **Donate** tab holds the ways to support the project.
- **The heatmap is an indicator** (Add Indicator, Order Flow, or `HEAT` in the Go bar); the toolbar
  button and Alt+H are gone.
- **Keys that can do nothing now say why**: Shift+F12 with no series focused ("No series
  focused."), and a tab key with nowhere to go ("Already on tab 1", "No tab 5. 2 tabs open.", "The
  last tab stays open.").
- **Pulse's detail no longer says "NaN"** while it warms up, and Vol Regime's detail uses your own
  thresholds.
- **Quick trade on a euro pair says euros** ("45,700.00 EUR"), not "$45,700.00 EUR".
- **The user manual** reads as a manual: no version history, the new features in, and a longer
  glossary for anyone new to trading terms.
