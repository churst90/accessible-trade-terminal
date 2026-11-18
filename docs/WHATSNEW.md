# What's New

<!-- This file holds the CURRENT RELEASE ONLY. The section below belongs to the tag named in
     its heading; anything written after that tag goes under a NEW `## Unreleased` heading above
     it, and the previous release's section moves out (its history is already in CHANGES.md).
     Check with `git diff <lasttag> HEAD -- docs/WHATSNEW.md` before every cut — this file has
     accumulated post-tag entries under an old heading before. -->

## Unreleased

None of this has been seen or heard in the app yet.

### Market events on the chart

- **The chart can tell you what happened on a bar, not just what it cost.** CPI, the jobs report,
  PPI and GDP, Fed decisions, stock and crypto options expiries, US market holidays and the four
  past Bitcoin halvings are placed on the bar of the day they happened. Arrow onto one and a short
  clause follows the price reading: "CPI 8:30", "CPI 8:30, before the open", "market
  closed Thursday, Thanksgiving".
- **Off by default.** Turn it on under Settings, Narration, "Market events on the chart"; choose
  important only, important and medium, or all, and tick which kinds you want. Times follow your
  F12 clock choices. Alt+Shift+D adds an "Events on this bar" section with each event's long name,
  importance, source and what price did over the bar.
- **Apostrophe and Shift+apostrophe** jump to the next and previous bar with an event.
- **The figures are spoken.** CPI, core CPI, payrolls, unemployment and GDP now read their
  first print — the number as first published, never a later revision — with the previous figure.
  CPI says "up 1.3% on the month, previous up 1.0%", jobs says "payrolls up 227 thousand, previous
  up 36 thousand, unemployment 4.2%", and GDP names its estimate and quarter. PPI figures need a
  FRED key and run from February 2014.
- **A small bell on event bars.** Two falling notes for an important event, one for the rest and a
  quieter one for low; it plays as you arrow and during playback, has its own "Sound on event bars"
  tick box and a MarketEvent entry in the Sound Designer.
- **Live dates with a FRED key.** Add one in API Keys and the terminal refreshes release dates and
  figures in the background, at most every 12 hours. The status line under the setting says what
  was stored or when it last failed, and "Update release dates now" skips the wait. No key, no
  network: the shipped dates and figures still work.
- **Countdowns and the release read-out.** With market events on, an event that touches an open
  chart or a position is counted down 15 and 1 minutes before ("CPI in 15 minutes.", "CPI in
  1 minute."), and one minute after it lands the terminal says the price reaction first ("CPI out.
  BTC down 0.8% in the first minute."). If the focused chart was quiet in that first minute it
  says only "CPI out." The countdown times are a setting: Off, 1, 5 and 1, 15 and 1, or 30, 15
  and 1.
- **Your positions ride along.** Holding a position in a market the event touches adds it: "CPI in
  15 minutes. You're long 0.05 BTC on MEXC, stop at 89000.00." Up to three positions are named,
  then "And 2 more." "stop at" appears only when a working stop order rests on that symbol, and
  paper positions say "on Paper".
- **The figure when FRED has it.** With a FRED key, a release says "Waiting for the figure." and
  the terminal asks for that release's figure at 2, 5, 15, 30 and 60 minutes, then reads it with
  the existing wording. FRED can lag the official release, so it may never arrive within the hour.
  No key, no request.
- **Shift+F2 mutes the countdowns and read-outs; F2 does not.** They are alerts-tier speech, with
  the same market-event bell as the bar, and they become a desktop notification when the app
  window is hidden. They do not yet arrive while the browser is closed.
- **Still to come.** Expected figures (and the surprise word), earnings and the market pulse. For
  CPI, jobs, PPI and GDP the future dates come from FRED, so without a key the only future
  countdowns are Fed decisions; the shipped dates run through July 2026 for releases and 2027 for
  Fed decisions.

### Market events: the calendar, alerts and release history

- **Toolbar fixes.** The Heikin Ashi and Log scale buttons now say their shortcuts (Alt+C and
  Alt+L), and the Market events button has its own calendar icon instead of sharing the Levels one.
- **Alt+E opens a calendar of the days ahead.** Days are headings, each event is one line —
  "8:30, CPI, important, previous up 2.9%." — and filters choose the range, importance, kinds, or only
  the events touching your charts and positions. The FRED status sentence sits at the top. The
  screen opens even when market events are off.
- **Enter on a line takes the chart to that event's bar**, with the same jump reading as the
  apostrophe key. If the event is not on the loaded bars it says so and stays open.
- **"Alert me before"** watches one event by name. It is counted down and read out even if it
  touches nothing you hold, its kind or importance is filtered out, or market events are off.
  The setting's countdowns are used; with countdowns off it uses 15 and 1 minutes.
- **History** on a line lists the last six releases of the same source, newest first, with each
  figure and — when that bar is on your chart — what price did over it.
- **Still to come.** Expected figures (and the surprise word they make possible), earnings from
  Finnhub, and the market pulse. Watched alerts still do not arrive while the browser is closed.

### Downloads

- **The web host's zip is one tidy folder.** It used to unzip to about 470 loose files. It now unzips to
  one folder, `AccessibleTrader-WebHost-<version>`, holding a `README.txt`, the program in `app`, and one
  file to start it: **Accessible Trader.exe** on Windows, `./accessible-trader` on Linux, **Accessible
  Trader.command** on macOS. Start it with that file, not from inside `app`.
- **On Windows, if the server cannot start** (most often because it is already running), a message says
  so and points you at `http://localhost:5145`, instead of a window that flashes and closes.
- **The web host no longer leaves a data cache in the folder you started it from.** It goes to your
  system's cache folder (`~/.cache/AccessibleTrader` on Linux).

### Indicators

- **A line's colour is said only where it changes.** Arrowing along a line such as Cipher B's money
  flow, you hear "faint green line" on the bar where it turns green, in either direction, and not on
  the bars after. The first bar you read on a line says its colour once.
- **The DBSI trend line has two tones.** It sounds brighter while it is green and softer while it is
  red, so you can hear which side of price it is on. Ctrl+Left and Ctrl+Right on it jump to where it
  changes colour.
- **Cipher A's symbols sit above the candle**, one above another when several fall on the same candle,
  and their description says "above the candle".
- **A filled line is called an "area".** Cipher B's waves, money flow and wave gap are drawn filled,
  so their colour is said as "blue area" or "faint green area". A line drawn without a fill, or one
  whose fill you turn off, is still a "line".
- **A signal says where it sits.** With "Say what signals look like" on, a mark in an indicator pane
  names the level it has passed: "Large green dot below -60", "Red dot above 0". It uses the levels drawn
  on the chart, so it works for any indicator that draws some, your own included.

- **Cipher A's Blood Diamond is always bigger than the red diamond.** Zoomed out, the two used to come
  out the same size and differed only by shade. They now shrink together, so the larger stays larger at
  every zoom.
- **Cipher B is easier to see on light themes and Steel Gray.** On a light chart (Paper, High Contrast
  Light, or a theme built from them) Market Cipher B's faint white lines are drawn grey. On Steel Gray
  its waves are drawn solid. Your saved colours and any colour you chose are left alone, and every
  other theme looks as before.
- **Cipher C: "Low expected" is gone.** Its "likely" was right only about half the time. "Low is in"
  is unchanged. **An alert or strategy built on the "Low expected, %" line no longer finds it.**
- **Cipher C: a weekly "high" mark.** Beside the weekly "very high" diamond there is now a paler blue
  diamond on the first "high" reading of each weekly low. It comes much earlier (about 32 bars before
  the low is confirmed, against 11) and catches nearly every weekly low, but one in six is later
  undercut. A matching "Weekly High Call" line is there for alerts and strategies, off by default.
- **Cipher C: "Low is in" is back on weekly charts, and on 4-hour coin charts for the weekly low.** It
  was daily-only since 7 November. Re-checked on the new time-counted lows, its first "very high" was
  right 99% of the time on weekly charts and 96% for the weekly low on 4-hour coin charts. Times are
  said in days, and the average it names is "the 10-week average" or "the 40-hour average". Other
  intraday charts and 4-hour stock charts were not measured and still show nothing.

### Charts and speech

- **`,` and `.` say what else starts or ends at a formation stop.** For example: "Also starts here:
  possible falling wedge." When a formation sits inside others, the whole chain is named, the nearest
  with its date.
- **Alt+Shift+D says a timing window's count once,** not as bare numbers and then again in words.
- **Each "Read as" form keeps its own sound.** A part you can read as a wave or a histogram now
  remembers the sound you saved for each, so saving the histogram's sound no longer changes the wave's.
- **Add Indicator has an Advanced group,** as Properties does: settings the original indicator does not
  have are collapsed under "Advanced", with how many there are.
- **Alerts can watch a part you left out.** The Alerts dialog lists left-out parts after the others,
  marked "(left out)", so you can set an alert on one without putting it on the chart.

### Strategies

- **Your own compiled indicators work in strategies everywhere.** A strategy, the backtester and a
  background tab used to compute nothing for a custom (Roslyn or Pine) indicator, so any condition on
  one was quietly false. They are now computed as the chart computes them, on Heikin-Ashi candles when a
  strategy reads its signals there.
- **Retired built-in strategies leave older libraries.** A library started before August 2026 still
  held twenty research strategies that can no longer fire. On the next start they are moved to
  `strategies.retired.json` beside your `strategies.json`, not deleted. If that file cannot be written,
  your library is left as it was.

## 2.13.1 — a Windows installer, and a tidy Windows zip

2.13.1 changes only the Windows download. Everything under 2.13.0 below is what is in it.

- **An installer.** Download `AccessibleTrader-2.13.1-Windows-Setup.exe` and run it. It installs for you
  alone, with no administrator prompt, puts **Accessible Trader** in the Start menu, can add a desktop
  icon, and can be removed from Windows' installed apps list. Installing a later version over it replaces
  it. Your settings, workspaces and logs are kept apart and are never touched. The Windows app needs
  Microsoft's .NET 10 Desktop Runtime, as 2.13.0's did; the setup checks for it and, if it is
  missing, offers to open Microsoft's download page.
- **The zip is one folder.** `AccessibleTrader-Windows-2.13.1-win-x64.zip` now unzips to a folder named
  `Accessible Trader 2.13.1` with three things in it: **Accessible Trader.exe** (run this), README.txt,
  and the app folder, which holds the program. You no longer have to find the right exe among hundreds
  of files.
- **Still unsigned.** The first time you run either, Windows may say "Windows protected your PC": choose
  "More info", then "Run anyway". The setup's screens have not been tried with a screen reader yet.
- **If you put the NVDA controller DLL in by hand** for an older version, it now goes in the app folder,
  beside `AccessibleTrader.BlazorClient.exe`. Since 2.12.0 it is in the download, so you should not need to.

## 2.13.0 — the Ciphers match Market Cipher, cycle lows on every chart, and three new ways to trade

### Before you trade: venue trading is untested

Paper trading is the tried path. Ordering through a venue has not been tried in this release, on a demo
or practice account or with money. Kalshi, Coinbase futures and Hyperliquid are new, and every crypto
venue's order rules were rewritten. Each is checked only against copies of the venue's own answers.
Try a venue on its demo or practice account, with small sizes, before trusting it with money.

### Market Cipher's indicators

- **Cipher A's symbols are named for what they signal.** The green dot is now "Trend Turned Up", the
  blue triangle "Early Turn Up", the yellow diamond "Bullish Conditions", the red X "Short-Term Turn
  Down", the red diamond "Momentum Turned Down", the blood diamond "Strongest Sell Warning" and the
  yellow X "Oversold Trap Warning". That is what you hear when one fires and when you land on one, followed by what it means: the green
  dot says *"Trend turned up: medium-term trend crossed up, with momentum and the short-term trend
  already up"*, and a yellow X says it is often a trap. The manual explains each one.
  Saved charts, alerts and strategies keep working.
- **"Faint" colours.** A marker drawn see-through is now said as "faint". Cipher A's two red diamonds
  are the same red, but the Momentum Turned Down one is drawn faint and smaller: "large faint red
  diamond" against "large red diamond".
- **Cipher A looks like the original.** Its ribbon uses Market Cipher A's own blues, line widths and
  two light fills, and its symbols the original's colours and sizes. The original's ninth line (a
  yellow 21-day average) and its pivot lines are there too, left out until you include them. On an
  hourly chart the pivots show the day's high and low, as the original does.
- **Cipher A is back, rebuilt to match real Market Cipher A:** an eight-line moving-average ribbon
  that turns from blue to grey as a trend weakens, and Market Cipher A's seven symbols, each with its
  own sound and named for what it signals (see the first item of this section). The ribbon lines start muted. Old Cipher A
  strategies and alerts tell you which parts went and what to use instead.
- **Cipher A's ribbon is narrated as one band.** When a candle closes you hear *"Price entered the
  ribbon, between …"*, *"Price left the ribbon above"* (or below), or *"Price crossed above the
  ribbon"* when one candle jumps it, and nothing while price moves about inside it.
- **Cipher A's yellow diamond now matches Market Cipher A's.** When the two waves nearly touch, it is
  drawn only if Cipher B's money flow is above 5. That removes the few extra diamonds the old rule drew.
- **Cipher A now matches Market Cipher A's own numbers, symbol for symbol.** Checked
  candle by candle on Bitcoin daily: the ribbon, the red X and the blood diamond already matched.
  The green dot, blue triangle, red diamond and yellow X now land on every one of the original's
  and nowhere else. The red diamond now also appears under a blood diamond, as the original's does.
  The yellow diamond, which reads Cipher B's money flow, now matches too (see Cipher B below).
- **Cipher B Sell dot.** A large red dot at the top of the pane where the waves cross down above Over
  Bought Level 1, the buy dot's mirror. Market Cipher B has no such dot, so it is left out until you
  include it in Properties → Appearance → Parts.
- **Cipher B is Market Cipher B and nothing else.** The red Overbought Crossover, divergences, anchor waves,
  WT Fill and the older formula and threshold settings are gone; a saved chart opens without them. Its
  settings are Market Cipher B's six: Use Current Chart Resolution, Different Timeframe, Over Bought Level 1,
  Trigger 1, Over Sold Level 1 and Trigger 2. The Wave Gap and Money Flow are heard as waves, and
  Properties, Appearance, "Read as" turns either into a histogram.
- **Cipher B no longer has a gold dot.** Market Cipher B draws none; ours came from the free VuManChu
  Cipher B, so it is gone. Everything else Cipher B draws and says is exactly as before. A saved chart
  opens without it, and a strategy or alert built on it tells you it was removed.
- **Cipher B now matches Market Cipher B's own numbers.** Checked against the original's values
  candle by candle on Bitcoin daily: the waves, the yellow area and every small cross dot already
  matched. The "RSI" and "Stochastic" lines did not and now do. The big green Buy dot now comes on
  every cross up with the *fast* wave below −60, so it lands where the original's does: every one
  of its dots, none extra. The money flow now matches too. It measures how far each candle's typical
  price (high, low and close averaged) sits from its average over the last five candles, against
  the usual size of that distance, averaged over 60 candles. It equals the original's to the five
  decimals TradingView shows on the Dow's whole daily history and on Bitcoin daily (candles and
  Heikin-Ashi) and 15-minute charts; before, its typical miss was about 6½ points. Cipher A's Yellow
  Diamond, which reads it, now lands on exactly the original's candles on Bitcoin daily and the Dow.
- **Cipher B looks like Market Cipher B.** Blue wave areas, a yellow VWAP, green and red money flow,
  a money-flow bar along the bottom with big green buy dots, and Market Cipher's RSI and Stochastic
  lines. Its numbers were matched against real Market Cipher charts: buy dots now land where Market
  Cipher's do. Two new parts, RSI and Stochastic, are the last stops when you arrow through Cipher B,
  and they start muted. Money Flow narration now really says bearish when money flows out.
- **Cipher B's money flow now has Market Cipher B's bright edge** along the top of each green hump and
  the bottom of each red one.
- **Cipher B's big green dot is now the "Buy dot".** Properties now names every indicator's parts
  the way speech does.
- **Cipher B's dots say the wave trend again** when you land on them, and Money Flow says "buying
  pressure, 18.0" instead of a percentage it never was.
- **Cipher B's Money Flow and Wave Gap bars are easier to hear.** Read as a histogram, a bar's roughness measures it
  against the biggest bar of its own kind on screen, not the whole Cipher B pane, so the biggest
  Money Flow or histogram bar in view is fully rough and small ones stay smooth. Before, nearly
  every bar played almost clean. Volume and MACD follow the same rule.
- **Rough texture only where it means something.** Cipher B's waves are clean inside plus or minus 53, a
  soft noise comes in past 53, and louder past 60. Other lines no longer carry a texture of their own.
- **Cipher B's RSI and Stochastic now say "overbought" and "oversold"** past their own 80 and 20, and
  narration announces when they cross (once you unmute them).
- **Cipher B's RSI and Stochastic lines turn rough above 80 and below 20** and click as they cross,
  on their own 0–100 scale (nothing extra is drawn). The waves now audibly roughen past ±53, and more
  past ±60; before, the ±53 zone was too quiet to hear.
- **Cipher B is two stops shorter:** the hidden Adaptive OB and OS parts are gone; they only repeated
  the ±53 lines.
- **Cipher SR's purple and yellow dots now match Market Cipher SR's on every candle.** Checked
  against the original's values on Bitcoin 15-minute: a level is a high the two candles before
  climbed to step by step and the two after fell from step by step, on above-average volume, and it
  appears three candles later. About half the levels Cipher SR used to draw are gone, and the rest
  arrive a little later. VWAP already matched. The day's range lines still show the range so far
  rather than the whole day's, which the original only knows by looking ahead. The Pivot Left Bars
  setting is gone.
- **Cipher SR draws Market Cipher SR's levels:** one resistance and one support, held as runs of
  dots until a newer level replaces them. You now hear "New resistance at …, replacing …" where it
  used to say a level was "broken" when price never went through it. Landing on a level tells you
  how long it has held. Range lines and VWAP are new and silent by default. On a daily chart expect
  much more Cipher SR narration than before.
- **New indicator: Cipher DBSI.** A bulls score under each candle and a bears score
  over it, like Market Cipher DBSI, plus a 200-bar trend line. Land on a score to hear "Bulls 15, bears
  minus 5, bulls in control"; with narration on it tells you when control changes hands. It is our
  reconstruction; how often its number is DBSI's own is in the next item.
- **Cipher DBSI now prints Market Cipher DBSI's own number on all but a handful of candles.** On the ten
  charts used to check every rule it is exact on 37,132 of 37,136 candles (was 98.5%), and on charts never used
  to choose a rule on 33,097 of 33,108. Old stocks and commodities with long runs of flat candles still miss
  about 1 in 16.
- **New indicator: DSS Bressert,** a calmer double-smoothed Stochastic with a trigger line. It sounds
  like the Stochastic.

### Cycle lows: Cipher C and Cycle Position

- **Cipher C and Cycle Position have swapped names.** The more complete cycle indicator, with
  learned windows, "Low is in", "Low expected" and the half-cycle low, is now **Cipher C**; our copy
  of Camel Finance's cycle indicator is now **Cycle Position**. Saved charts, strategies and alerts
  keep working and show the new names. Below, entries written before the swap use the new names.
- **Cipher C follows the chart's own cycle.** On a weekly chart the wave and the events are the weekly
  cycle's, not a daily one counted in weeks, and the weekly lows are about a year apart, not several.
  On a 4-hour chart the daily cycle is counted in days. The weekly and yearly cycles now have their own
  pending, cancelled and failed lows, said like the daily ones; turn them on in Parts to see them. A
  new check box, "Only this chart's own cycle" (off by default), leaves the longer cycles out. Low is
  in, Low expected, the top so far and the half-cycle low are now on daily charts only.
- **The old Cipher C oscillator is now a cycle-low indicator, Cycle Position.**
  It marks daily, weekly and yearly cycle lows under the candles, says when a low is confirmed,
  pending, cancelled or failed, counts the days of each cycle, and on Alt+Shift+D tells you where you
  are in the cycle: "Daily cycle day 40, in its window, 4 days left". It has cycle ranges tuned
  for sixteen assets (S&P, Nasdaq, Dow, dollar, gold, silver, oil, the miners, Bitcoin, Ethereum and the
  crypto stocks), and on a weekly chart it talks about weekly cycles, not daily ones. A low is only
  marked once it is confirmed, usually some days later. Old Cipher C
  strategies and alerts tell you its old parts were removed.
- **Cycle Position confirms a cycle low when the indicator it follows does.** A daily low now has to hold
  for half the shortest cycle before it is confirmed, and a lower low in that time takes its place.
  On the Dow and gold miners, most confirmations now land on the same day as the original's. A low is
  therefore known later than before: about 18 trading days after it on the Dow, 27 days on Bitcoin.
  The cycle ranges for all sixteen tuned assets are now the original's own.
- **Cycle Position finds the next daily low sooner in strong rallies.** It used to wait for its cycle line to
  turn, which on gold miners in autumn 2025 left a low unconfirmed five weeks after the original
  had confirmed it. It now starts looking on schedule and takes the low after the cycle's top as soon
  as a candle closes above the one before. It is closer to the original, not identical. Gold and the
  dollar index now count five-day weeks, as the original does; a stray weekend bar no longer redraws
  their cycles.
- **Cycle Position's weekly lows on a daily chart are picked from its daily lows**, the way the indicator
  it follows picks them, so a weekly low now always sits on a daily low. As there, it is called
  confirmed only once the weekly low after it is kept, which can be months. On Bitcoin since 2022 they land on the same dates as that indicator's wherever the daily
  lows agree. A weekly chart works as before.
- **Cycle Position on a symbol without a preset counts the way the original does.** Apple, Solana, the euro
  or any symbol none of the sixteen presets covers is now counted from 32 bars, the original's
  default count. Coins used Bitcoin's 48 before, stocks the S&P's 36. Its daily lows match the
  original's more often on all thirteen charts checked (from 66% to 77% of them).
- **Cycle Position off the daily chart counts as the original does.** On a weekly, four-hour or hourly
  chart it counts each bar as one of the preset's days, as the original does there, and says its
  counts in bars. It says the preset is not tuned for that timeframe and that daily is. A weekly
  chart now has daily lows too. On four-hour gold and Bitcoin its lows match the original's three
  times as often as before.
- **An ETF or future is counted as itself, not as its index.** SPY, QQQ, DIA, GLD, IBIT and other
  funds and futures no longer take the S&P's, Nasdaq's, Dow's, gold's or Bitcoin's preset; they get
  the default count, as the original gives them. The index, gold or coin itself under another name
  (^GSPC, US30, XAU/USD, BTC/USDT) keeps its preset. You can still choose a preset by hand.
- **Loukas Cycle Detection is gone.** Cycle Position finds the same cycle lows for each asset. A chart that
  had it opens without it and says so once; a strategy or alert on it tells you which Cycle Position signal
  to use instead. The StrategyLab "Cycle Low Reversal" strategy, built on it, is retired too.

### Trading

- **Every crypto venue checks a market's own rules before it sends.** Kraken, Coinbase, Gemini,
  Bitstamp, MEXC and Hyperliquid read each market's smallest order, its size and price steps and whether
  it is taking orders. An order under the smallest size or value is refused in words before anything is
  sent ("Kraken's smallest KAS/USD order is 170 KAS; this one is 100 KAS"). Sizes are cut down to the
  step, never up, and prices go to the tick. Leverage is sent as you asked: 1x is really 1x, and an amount
  a market does not lend at is refused with the amounts it does. Not yet tried with money.
- **Kraken balances say what you have.** Deposited dollars read as USD, not ZUSD, so the risk sizer
  finds your cash, and the Balances tab shows what open orders hold under Locked. Staked and reward
  amounts get rows of their own. Not yet tried with money.
- **Kalshi.** A new venue for Kalshi's perpetual futures: crypto, US stocks, metals, oil, currencies
  and indices in US dollars. Put the Key ID in the API key box and the private key file in the secret
  box. Practice uses Kalshi's demo venue. Market and limit orders only; a market order goes out 5%
  through the best price and fills at once or not at all. No leverage setting.
  Kalshi is opening perps to members a few at a time; the key check says whether yours has them. Not
  yet tried with money.
- **Kalshi stop loss and take profit.** An order on Kalshi can carry a stop and a target, quick trade
  works there, and the Positions tab moves them. Kalshi keeps them on the position, not as orders; when
  one fires it sends an order that fills at once near the level, or not at all. Moving the stop keeps
  the target. If Kalshi refuses the stop after the entry filled, you hear "POSITION UNPROTECTED". Not
  yet tried with money.
- **Coinbase futures.** Pick Futures on Coinbase for the US futures contracts, such as the BTC Perp
  (BIP-20DEC30-CDE). Sizes are whole contracts (one BTC Perp contract is 0.01 BTC), and a size typed
  as bitcoin is refused with what a contract is. A close is checked against the position first, so it
  cannot open one the other way. Positions and a "USD futures" balance show. Not yet tried with a real
  futures account.
- **Hyperliquid.** A new trading provider for perpetuals (Bitcoin included) and spot on Hyperliquid, for
  traders outside the United States and Ontario. Charts and the order book need no key; trading uses an
  agent wallet that cannot withdraw (see the manual's API key section). Paper uses Hyperliquid's testnet.
  No order has been sent through it yet, on the testnet or for real: try it on the testnet first.
- **A stop is never silently dropped.** On Bitstamp, Coinbase, Gemini, Kraken Futures and MEXC spot,
  an order carrying a stop loss or take profit is now refused before it is sent, with the reason. Those
  venues cannot attach one to an entry, and the entry used to go out without it. Send the entry, then a
  separate stop order. Quick trade always carries a stop, so it is refused on those venues too.
- **Kraken Futures positions show the real profit and loss and leverage.** Sizes are cut to the
  contract's step, and a size that is too small is refused in words.
- **Moving a stop or target no longer sounds an alarm, and no longer removes the other one.** Editing
  a stop on the Positions tab used to play the error sound and show "may be unprotected" two seconds
  later. It was a false alarm, and the position was protected. The edit could also cancel the take
  profit placed with it. Now the other leg is checked after each edit and put back if it went. If it
  cannot be put back, you are told.
- **Each tab trades on its own chart.** A quick trade armed on one tab places only from that tab. On
  another tab it says where it was armed, and Escape still cancels it. A strategy runs only on the venue,
  symbol and timeframe it was started on, and orders there even when another tab is in front.
  Background tabs act on closed bars, as the tab in front does, and signal once per bar. In paper mode, a
  position is moved only by its own venue's prices. An order for the same symbol from another venue's tab
  is refused, and a close from anywhere closes it at its own venue's price.
- **Paper stops no longer fire on another tab's candle.** With several tabs open on one symbol at
  different timeframes, a paper stop could be hit within seconds of a market buy by a low that a
  longer timeframe's candle had made hours before the trade. Each tab's candle is now judged on its own,
  so a stop fires only when price really reaches it after the order is placed.
- **Your open trades at the bottom of the window.** While you hold a position, a strip below the indicator
  bar lists it: "BTCUSDT Kraken Futures isolated 5x long, up 14.3%", green when up, red when down. It is
  the "Open trades" region for screen readers and never speaks by itself. Settings (F12), Appearance:
  coloured blocks, outlined, or off, and the percent, the amount or both.
- **Close part of a position.** Close at limit now has a percent field after the price, starting at
  100. Type 50 to close half; the rest stays open.
- **Size an order by its value.** The ticket's new **Size in** choice switches the quantity from the base
  unit (1 = one bitcoin) to the quote currency (1000 = a thousand dollars' worth). The quantity is worked
  out at the order's price when you submit, and the live review reads it back.
- **Shift+Enter places an armed quick trade as a limit order.** In the browser it did nothing before; Ctrl+Enter
  (market) always worked. Try it on paper first.
- **Escape really cancels a quick trade while your balance is loading.** Pressing Escape during
  "Fetching your account balance" used to be ignored, and the trade armed when the balance
  arrived. It now says "Quick trade cancelled." and stays cancelled.
- **A withdrawal address for the wrong network is refused, not called verified.** A Bitcoin
  address offered for a Litecoin withdrawal used to be announced as verified, because only its
  checksum was checked. The terminal now also reads which network the address was made for.
  It says "this is not a Litecoin address" (or Tron, or Bitcoin), or that the address is the
  wrong length.
- **A corrupt Bitcoin or Litecoin withdrawal address is no longer called verified.** A native
  SegWit address carrying the wrong kind of checksum (one of the two that BIP-350 says must
  never be mixed) used to be announced as verified. It is now refused as corrupt. A wallet
  refuses such an address as well, so this was a false reassurance rather than a lost payment.

### Hearing the chart

- **The new bar says when it opened.** A bar close now ends "New bar: Open 75,712 at 14:33", on the
  same clock as the close (the date on a daily chart). It is the same on a background tab, in a
  notification and with the browser closed.
- **A bar close is read in the order it happened.** The close, then what your indicators said about that bar,
  then the new bar's open. Before, the open came before the indicators. The same order applies on the focused
  chart, background tabs, with the browser closed, and in notifications.
- **Each part of a volume profile has its own pitch.** As you arrow Up and Down through a profile, each bin
  is a sine tone:
  - the point of control: an 880 Hz chime with a click;
  - the value area, its edges included: 440 Hz;
  - outside the value area: 330 Hz;
  - high volume nodes 220 Hz, low volume nodes 165 Hz, empty bins 110 Hz.

  A bin's size is heard as roughness, and every bin is equally loud. The price is spoken, not played. The
  order-book heatmap's bins follow the same size rule.
- **Times in UTC or on a 12-hour clock.** Settings (F12), Speech: "Times of day are read in" this computer's zone
  or UTC, and "12-hour clock with AM and PM". Speech and the chart's time axis both follow.
- **Arrowing speaks and sounds only the component you are on.** On Cipher A's green dot you no longer
  hear the yellow diamond on the same candle, and on Cipher B you no longer hear Cipher A's signals. The
  approach ping and the "held beyond" tone come only from the component in focus and its own levels, not
  from an RSI nearing 70 in another pane, and the indicator's other dots and crosses no longer tick. To
  bring them back, turn on **Speak other signals while arrowing** (Settings, Speech) or **Tick other
  signals while arrowing** (Settings, Sonification). Chart patterns, cycle marks, your labels, nearby
  zones and the lines you drew are still heard.
- **Lines and signals can say what they look like.** Turn on "Say what lines and signals look like" on
  the Speech tab of Settings. Moving onto a line says *"Orange line."*, and arrowing along it says the
  colour again only where it changes. Each signal is followed by its look on the chart: *"Buy. Large
  green dot."*, *"Cycle low. Green up triangle below the candle."* The colour is the one drawn on that
  bar, so a dot that turns red when it is bearish is said as red. It helps when you are talking a chart
  through with someone who reads it by eye.
- **Candle patterns say which way they lean, and follow the textbook rules.** Each pattern is now said
  with its lean: "Tweezer top, bearish reversal", "Rising three methods, bullish continuation". This
  happens on the arrow keys, Alt+Shift+D, the bar close and the forming bar. A doji has no lean and says
  none. A reversal is named only after a move for it to reverse: a hammer after a fall, a tweezer top
  after a rise. Long and short bodies are judged against the candles before, so a tiny candle with no
  wicks is no longer a marubozu. A candle that never traded (its high is its low) says "No movement"
  instead of "Doji", and it is part of no pattern. Expect far fewer patterns on a quiet market, and fewer
  candle-pattern alerts.
- **A candle in two patterns says both.** One candle can end a pattern and start the next. It used to
  say only one of them, so the next candle's "bar 2 of 2" seemed to have no first candle. It now says
  "Three outside up, bullish reversal, bar 3 of 3, also bar 1 of 2, Bearish harami, bearish reversal".
  And three white soldiers and three black crows now need a large third candle, not only large first
  and second ones.
- **Semicolon chooses a formation.** Where formations overlap, `;` and Shift+`;` move to the next
  or previous one at the bar and say which larger formation it sits inside. Your choice is what you
  hear on every bar it covers until you choose again. It used to land back where it started on every
  press.
- **Formations say "downward breakout" and "upward breakout"** when a close goes through the line.
- **Flags on a steady trend are announced.** A bull or bear flag on a clean trend was never
  spoken when it formed, only found later when panning back. You may now hear more flags on
  trending charts.
- **Alt+Shift+D reads everything at the bar:** the indicator you are on, the candle's prices and
  volume, then every other visible indicator from the top of the chart down with any signals on that
  bar, then formations. Hidden indicators and hidden parts are skipped, and an indicator with nothing
  to say stays quiet. DSS Bressert adds its state: overbought or oversold, rising or falling, and
  when it crosses its trigger.
- **Bars sound like what they say.** A histogram bar now plays its higher tone above zero and its
  lower tone below, matching the number you hear and the colour on screen. Before, Cipher B's WT
  Histogram and Money Flow, MACD's histogram and several others followed the candle instead, so a
  negative bar could sound positive. Volume still follows the candle.
- **Above and below always mean the indicator's own midline.** The bright upper and warm lower sound of
  an oscillator now switch at its midline (RSI's 50, Momentum's zero, Cipher B's RSI and Stochastic
  lines' 50) however the pane is scrolled; before, they switched at the middle of whatever was on
  screen.
- **You choose what a sound's pitch follows.** In an indicator's properties (P), the
  Sonification tab has a new **Pitch follows** choice for each part. Set the candle bodies to
  "The value" to hear them climb and fall with price instead of playing an up tone and a down
  tone.
- **Heikin-Ashi now feeds your indicators, as on TradingView.** With Heikin-Ashi on (Alt+C), RSI,
  MACD, moving averages, Cipher A, B and C, Cipher DBSI and your own indicators compute on the Heikin-Ashi
  candles, and switching it recomputes them at once. Cipher A and Cipher B now put their marks on the
  same bars as Market Cipher's Heikin-Ashi screenshots. Every close you hear is the Heikin-Ashi close
  too: the new-bar announcement, background tabs, toasts, price and candle-pattern alerts, price
  crossing a line or a moving average, and the crossing chirp. Turn Heikin-Ashi off and they are the
  real close again. Volume, profiles, the heatmap, drawings, chart formations and the close line stay
  real.
- **A strategy can read its signals on Heikin-Ashi candles.** Tick "Signals from Heikin-Ashi candles"
  in Build Setup. Its conditions then read Heikin-Ashi candles, on any chart, while entries, stops,
  targets and fills stay on real prices, as with TradingView's "Fill orders on standard OHLC". Left
  off, a strategy reads the real candles even on a Heikin-Ashi chart. A backtest started on a
  Heikin-Ashi chart used to read the chart's Heikin-Ashi indicators, so it could show trades the live
  strategy would never take. It now reads the same candles the live strategy does.
- **F2 now silences only the chart.** F2 quiets what the chart says as you move through it and
  all narration, including new-bar announcements, and says "Chart speech off". Dialogs, the
  toolbar and settings keep speaking, so you always hear what you just did. Shift+F2 now covers
  alerts, monitoring and connection status only. If you used Shift+F2 to quiet new bars, use
  F2, or turn off Announce new bars in Settings → Narration to keep hearing the chart as you
  move. Orders placed, cancelled or closed now speak through both mutes, like fills.
- **`0` is for indicators.** On an indicator in its own pane it adds or switches its midline. On the
  price chart it now does nothing and says so; draw a horizontal line to mark a price.
- **The detail key explains Bollinger squeezes and MACD crosses again.** Ctrl+Shift+D on
  Bollinger Bands says whether the bands are squeezing or expanding, and on MACD whether it just
  crossed its signal line. Both had been silent on every real chart.
- **Scrolling back no longer replays old news.** When moving left loaded older history, narration
  could announce an old bar as if it had just closed, and then repeat a signal you had already
  heard. Fixed.
- **A monthly chart is described as monthly.** The pane description (Alt+Shift+/) called a
  1-month chart "1 minute per bar".

### Drawings and alerts

- **Fibs read like TradingView.** A retracement goes swing start, then swing end, with 0% at the end, so 61.8%
  is a 61.8% pullback; the default levels are TradingView's eleven, and Properties takes your own. For a fib
  extension's third point, the pullback, the app tells you where it is after the second point: its price, date,
  how many bars to the right and how deep it went, so you arrow there and press Enter.
- **Enter places the next drawing point**, and **Alt+D lists every drawing tool**, 21 in five groups, including
  five new pattern tools (ABCD, XABCD harmonics, head and shoulders, triangle, Elliott impulse) that say what the
  points you placed make.
- **Drawn lines chirp when price crosses them.** Fib levels, horizontal and vertical lines play the same
  crossing sound as levels and are Ctrl+Left/Right stops on the candles, and you can set an alert on a fib
  level, a horizontal line or a risk/reward line ("A line on the chart").
- **Drawing tools name the right point.** Finishing a trend line from the keyboard said "point 3",
  a point it does not have. It now names the second point.
- **The alert form makes sense.** Conditions match the target (no zones on price), the value starts
  at the current price or reading, and there is a new **Touches** condition. You can compare price
  with a line on the chart, such as the SMA 50. Alerts that could never fire are refused with the
  reason, and fired alerts speak plain numbers. The Advanced editor can watch one line crossing
  another, including a weekly line from a daily chart.

### Charts, tabs and timeframes

- **Any timeframe you type loads.** An 8-hour or 7-hour chart on Bitstamp used to be switched to 1 hour
  with "Bitstamp does not offer 8h". Now any timeframe the venue's own candles divide evenly is built from
  them quietly: 8 hours from 4-hour candles, 7 hours from 1-hour candles. The quick-pick buttons still show
  the venue's own list. The oldest candle on such a chart is no longer a short, partial one.
- **The browser-closed monitor watches timeframes the exchange does not serve,** such as 24 minutes on
  Bitstamp, by building them from smaller bars. A chart whose feed has gone quiet is reported.
- **The newest candle is the real one.** The candle still forming used to restart flat when you
  opened a chart, as if the day or week began when you arrived. It now continues the exchange's
  candle from its real open, high and low. Live stock daily candles, which didn't move at all, now do.
- **Today's bar is called today.** Daily and weekly bars are named by their own date, so the bar
  forming on October 9 is "October 9", not "October 8", and a week starts on Monday. Intraday bars
  still use your local clock. The bar slider and the arrow keys now always agree.
- **You hear what came back.** When the terminal resumes your last session it now names the chart
  you were on and its indicators, for example "BTCUSDT 1 day, with Cipher B and Market Structure", so
  you can tell straight away whether your layout is all there.
- **Reopening the terminal brings back the tab you left.** With two terminal tabs open, an idle live
  chart could win and be the one restored. Now the tab you were last working in is.
- **A new tab no longer empties the Symbol box.** Opening a blank tab, or starting the app for the
  first time, used to clear the symbol you had selected, so Load Chart had nothing to load.
- **Charts you already had pick up indicator improvements.** A saved chart now gets new levels an
  indicator has gained and its improved defaults, while anything you changed yourself in Properties stays
  as you set it. This is what brings Cipher B's RSI and Stochastic 80/20 and its louder ±53 texture to an
  existing chart. Ctrl+Left/Right also stops at a hidden zero line in every case.
- **Status lines like TradingView's** replace the chart's colour key and the box that followed the
  mouse: each pane lists its indicators with their values at the bar, in their colours. They only
  show what you can already hear from the chart.
- **Status lines never cover the chart.** Each pane draws below its status lines, so Cipher B's RSI and
  Stochastic, and the tops of the waves, are no longer hidden behind the text.
- **The chart says more and hides less** (for a sighted helper or anyone using low vision):
  - Axis prices read like the status line: `85,000`, not `85000.00`.
  - Volume Profile is drawn under the candles instead of over the newest ones.
  - Formation lines are labelled with what they are ("trigger", "measured target") and stop where the
    formation broke or reached its target; wedges and triangles show their two sloping edges.
  - The white line through the closes is the Price series, and the status line now names it.
- **Filled waves stop at the newest bar** instead of trailing off into the empty space, and clouds
  such as Ichimoku's draw their newest stretch again.
- **Ichimoku's cloud is green and red again,** and My Data event markers are amber; both were drawn
  in the wrong colours.
- **Formation labels are easier to read** on every theme and move off the candles where they can.
- **Formation labels read right and stay on the chart** ("confirms on an upward breakout"), and dragging a
  pane divider now follows the mouse at any window size.
- **Background tint is your choice, on any indicator.** Properties → Appearance → Background tint: Off,
  This pane or Price chart. Cipher B's price-chart shading is off by default, like Market Cipher B.
- **The mouse lands on the candle under it**, all the way to the edges, and a click that cannot do anything says
  why instead of staying silent.
- **Alt+F is gone, and everything you can see always fits the price pane.** Alt+F let bands run
  off the top or bottom of the pane to give the price line more pitch range. A band off the pane
  sits at the top or bottom pitch and sounds as if it had stopped moving, which is misleading. To
  give the price line the whole pitch range, hide the band with H instead. Alt+F is now left free,
  since it is the File menu in most Windows programs. Custom key settings you saved are kept.
- **Panes keep their order.** On a chart with three panes, the two below the price pane could be
  read in the wrong order. Fixed.

### Settings, workspaces and the rest

- **Save and Load workspace ask what you want.** With several tabs open, Save offers "All tabs" (the
  default) or "This tab only". Load offers "Replace my tabs" or "Open as new tabs, keeping mine". If
  Replace would close open charts, the first Load names them and the second Load replaces them. The
  Load list now says what each workspace holds: its tabs and how many strategies.
- **Settings say what they do and their default, nothing else.** The Properties dialog no longer
  tells you which other product a Cipher, DSS or DBSI-style default matches, or what a setting did
  before. Cipher B's older Money Flow Formula and Buy Dot Rule settings were later removed (see above).
- **Leave out any part of an indicator.** Properties → Appearance → Parts has an "Include" box for
  each part. A part left out is gone: not drawn, heard, spoken or reached with the arrow keys. "Save
  as Defaults" remembers it for new charts.
- **Rebinding a key works for chords, and Escape really cancels.** In Settings, Keyboard, press
  Rebind and focus moves to a "New shortcut" field. Press the new key or combination, like
  Ctrl+Alt+Y, and you hear what the command is now bound to. Escape or Tab cancels without
  changing anything. Before, a chord was never picked up, and Escape closed Settings and became
  the new key.
- **The Hide, Mute and Delete buttons in the Object Tree work from the keyboard.** Enter or Space
  on one of them used to select the row instead. The same fix applies to the buttons in the
  strategy condition tree.
- **Saving a strategy keeps its exit plan.** Opening a strategy in the builder and saving it,
  even just to rename it, used to replace a trailing stop with "move to breakeven". It no longer
  does. Strategies added from the builder or from a script also keep their positions across a
  restart now, and stay in a saved workspace.
- **Deleting a strategy condition keeps your place.** Focus moves to the next condition and the
  deletion is announced, instead of focus falling off the page.
- **Fewer warnings in the server window.** It no longer warns about parts of DBSI, Cipher A and
  Market Structure that are left out by default. They were never a problem; the check counted only the
  parts that were included.
- **The Dot Pad's braille strip comes back after a reconnect.** It used to stay blank until the
  value changed.
- **The hosted terminal reconnects more gently.** When the server restarts, an open page used
  to retry ten times in the same instant, which only added load while it restarted. It now tries once
  straight away, then waits a little longer each time, up to about 15 seconds between tries.
  You hear that the connection was lost once, a short "still reconnecting" about once a minute,
  and "Reconnected to the terminal" when it is back. It used to repeat the whole message every
  second.
- **The online demo opens on a chart.** Visiting the demo now lands straight on a live Bitcoin
  daily chart that already speaks, instead of an empty symbol box and four steps to get there.
- **Spider Lines is gone.** It was eight moving averages under a misleading name. A chart that had it
  opens without it and says so once; add EMAs at the lengths you want instead.
