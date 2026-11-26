# What's New

<!-- This file holds the CURRENT RELEASE ONLY. The section below belongs to the tag named in
     its heading; anything written after that tag goes under a NEW `## Unreleased` heading above
     it, and the previous release's section moves out (its history is already in CHANGES.md).
     Check with `git diff <lasttag> HEAD -- docs/WHATSNEW.md` before every cut — this file has
     accumulated post-tag entries under an old heading before. -->

## Unreleased

- **Clearer trading fields (from an accessibility review).** Hyperliquid's "Vault or subaccount"
  field is now plain text you can read back, with a hint, and a bad address is refused at Save. The
  dead-man's switch seconds field says when it corrects a number, and its hint is short and its
  own. TWAP minutes of 0 are marked before you press Place, and a TWAP that has not filled yet
  says "none filled yet" for its price instead of 0.
- **Dead-man's switch on Hyperliquid (off unless you turn it on).** Settings (F12), General,
  Trading: if the terminal crashes, the computer is forced off or the connection drops for the
  seconds you choose (60 by default), Hyperliquid cancels your open orders there, which may
  include stops and targets. Positions stay open, and closing the terminal normally keeps your
  orders. Not tried against Hyperliquid itself yet.
- **Hyperliquid subaccounts and vaults.** Put a subaccount's or vault's address in the profile's
  "Vault or subaccount" field to trade it with your usual agent key; balances, positions and orders
  are then its own.
- **TWAP orders on Hyperliquid.** Choose TWAP in the trading ticket's Type list, set how many
  minutes, and Hyperliquid buys or sells your size in small slices about every 30 seconds until the
  time is up. It is listed with your open orders with how much is done and when it ends, and Cancel
  stops it. Not simulated by paper trading. Not tried with a real order yet.
- **Hyperliquid orders expire if they arrive late.** An order or change of order that takes more
  than 60 seconds to reach Hyperliquid is refused instead of filling at a stale price.
- **Funding and open interest now.** Type `FUND` in the Go bar (Alt+G) on a Hyperliquid chart to
  hear the market's funding right now (per 8 hours, who pays whom and what it comes to over a
  year), open interest in coins and dollars, the mark price against the oracle, and the day's
  volume. Other exchanges say they do not give these yet.
- **More Hyperliquid markets.** Hyperliquid's Futures list now includes the markets other firms
  run on it: shares such as xyz:TSLA-PERP, gold and stock indices, 178 in all. You can chart them,
  hear live bars, read the book, see their funding and trade them. Each firm keeps its own margin:
  on an ordinary account, move USDC to it on Hyperliquid's website first (an order with none there
  says so); a unified account trades them from its spot USDC. Their positions, orders and margin
  are listed with the rest. Not tried with a real order yet.
- **Funding Rate on Hyperliquid charts.** On a Hyperliquid chart, the Funding Rate indicator now
  shows Hyperliquid's own funding (it pays every hour; shown times eight, per 8 hours, so the
  levels read the same as elsewhere). Before, the pane was empty there. Funding, open interest and
  the other cross-market indicators also keep updating now while the chart stays open; before,
  they stopped at the hour you opened it.
- **Hyperliquid: fills heard as they happen.** Fills, partial fills, cancels and refusals on
  Hyperliquid are announced the moment the exchange sends them, including orders you place on its
  website, and a cancel you did not ask for says why. On a unified account your USDC is listed once;
  before, your perpetuals margin was listed again beside it. Not tried with a real order yet.
- **Gemini: limit orders on limit-only markets.** When Gemini sets a market to limit-only, your
  limit orders now go through; a market or stop-limit order is refused in words. On a post-only
  market, a limit order with Post-only ticked goes through. Before, both refused every order.
- **Paper: one position per symbol, whichever tab you trade from.** With BTCUSDT bought on Kraken,
  a buy from your Bitstamp tab now adds to the Kraken trade at Kraken's price and tells you so.
  Before, it was refused.
- **"Entered overbought" follows your levels.** When a bar closes, RSI and Cipher B's wave now say
  "entered overbought" and "oversold" at the lines you set in Properties. Before, they used 70/30
  and 53 there, even after the rest of the app followed your lines.
- **Cipher B follows your overbought and oversold levels.** Change Trigger 1 and 2 (or the extremes)
  and the waves say "overbought" and "oversold" past YOUR levels. Before, they always used 53.
- **Indicators update live.** Cipher B, the moving averages and every other indicator now follow the
  price as it ticks, up to four times a second. Before, their values on the forming bar stayed where
  they were until a new bar or a tab switch.
- **True buy and sell volume** (Alt+A, Volume: Buy and Sell Volume (trades), Volume Delta (trades)).
  Each bar's volume split by the side that took each trade, counted from the exchange's own trades
  from when the chart opens (there is no history). MEXC, Kraken and Binance (spot and futures), Bitstamp, Coinbase, Gemini and Hyperliquid.
- **Day separators** (Settings, Appearance, Draw day separators; off by default). A dotted line where
  each new day starts on charts with bars shorter than a day, at midnight on your clock.
- **Live indicators keep each bar's final value.** When a bar closed, its indicator values could stay
  at an earlier tick's reading until you switched tabs, so the bar read differently afterwards. The
  bar that closes is now worked out again on its final candle, and bars that arrive together no
  longer leave a gap.
- **Auto Fib retracement and auto trend lines** (Alt+A). The Fibonacci levels of the last finished
  swing, and trend lines through the last two falling pivot highs and rising pivot lows. Arrowing near
  one says its name ("near support at ..., Fib 61.8"). A new swing and a close through a trend line
  are marked and sounded.
- **Session levels: the previous day's, week's and month's high, low and close, and the session's
  high, low and opening range** (Alt+A, Reference Level). Arrowing near one says its name ("near
  resistance at ..., previous day high"). A close beyond yesterday's high or low, or out of the
  opening range, is marked and sounded once a day. Weekly pivots no longer restart in the middle of
  New Year's week.
- **Five trend overlays: linear regression channel, Chande Kroll Stop, McGinley Dynamic, Envelopes
  and a moving average ribbon** (Alt+A, Overlays). The regression channel's centre is the least
  squares moving average. Breakouts and crosses are marked and sounded, and the ribbon says its order
  and where the close is among its four lines.
- **Eight oscillators: Elder-Ray with Bull Bear Power, Fisher Transform, Coppock Curve, Know Sure
  Thing, Relative Vigor Index, SMI Ergodic, and the Awesome and Accelerator oscillators** (Alt+A,
  Oscillators). Each one is in its own pane, computed the way TradingView's built-in is. The ones
  with a signal line mark and sound each cross of it. Coppock marks its classic signal, turning up
  below zero.
- **Four volume indicators: Cumulative Volume Delta, Klinger Oscillator, Price Volume Trend and
  Relative Volume at Time** (Alt+A, Volume). Volume delta adds up buying minus selling from the start
  of each day and says who is ahead. Klinger marks and sounds each cross of its signal line. Relative
  volume at time compares volume with the usual for that time of day, so a busy open is not called
  unusual. The chart's candles do not say who bought, so a bar's volume counts as buying when its
  candle closed up. The manual says what that means on a daily chart.
- **Four smart money overlays: Fair Value Gaps, Order Blocks, Liquidity (equal highs and lows, and
  sweeps) and Premium and Discount** (Alt+A, Overlays). Each draws its zones or levels, marks and
  sounds the bar a new one appears, and says the prices and how far they are from the close. The
  strategy lab tested all five ideas against a plan written first and found no information in any
  of them. In crypto, a sweep was followed by price carrying on, not reversing. So they are on the
  chart to hear what other traders look at, but they are not offered in the strategy builder. The
  manual has the numbers.
- **Four new overlays: Supertrend, ZigZag, Williams Fractals and Pivot Points High/Low** (Alt+A,
  Overlays). Supertrend is a trailing line under or over price that marks and sounds each flip, and
  says "support" or "resistance". ZigZag joins the turns bigger than 5% and says the leg you are on.
  Fractals and Pivot Points High/Low mark every local high and low with its price. The manual says
  how each differs from Market Structure, and which marks are only known some bars later.
- **Turn the price pane upside down with I** (or `INVERT` in the Go bar), to check a bias. The pitch
  turns over with the picture, so a rising market falls in pitch while it is inverted; the words,
  prices and colours stay the market's, and indicator panes stay upright. Each tab keeps its own
  setting; drawings and alerts keep real prices. A braille graphics display turns over with it.
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
