# What's New

<!-- This file holds the CURRENT RELEASE ONLY. The section below belongs to the tag named in
     its heading; anything written after that tag goes under a NEW `## Unreleased` heading above
     it, and the previous release's section moves out (its history is already in CHANGES.md).
     Check with `git diff <lasttag> HEAD -- docs/WHATSNEW.md` before every cut — this file has
     accumulated post-tag entries under an old heading before. -->

## Unreleased

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

## 2.14.1 — the news desk, and chart types you hear as well as see

**2.14.1 is 2.14.0 with all seven downloads.** 2.14.0's Linux and macOS browser downloads did not
build (a packaging script had lost its "may run" mark); the app is the same in both, and everything
below arrived in 2.14.0.

**The terminal is now a news desk as well.** You cannot make a trade without all the information
there is, and that includes the headlines, so the terminal gathers it the way a Bloomberg Terminal
does, by keyboard and ear: market events and company earnings on the bars they happened on, SEC
filings and news wires, a one-sentence market pulse, what changed while you were away, a morning
brief of all three, and a Go bar that reaches every command by typing a short code.

Cody has tried the first market-events stage, the first new chart types, the headlines, the morning
brief and Alt+U; the rest has not been heard in the app yet, and none of it has been tried with money.

### Before you trade, and installing

- **Trading through the venues is still untested.** Paper trading is the tried path; no order has
  been placed through any venue on a demo, practice or real account. Each venue's orders are proven
  only against copies of its public answers and docs.
- **Windows:** run `AccessibleTrader-2.14.1-Windows-Setup.exe`, or unzip
  `AccessibleTrader-Windows-2.14.1-win-x64.zip` and run **Accessible Trader.exe** in the
  `Accessible Trader 2.14.1` folder, as in 2.13.1. Both need Microsoft's .NET 10 Desktop Runtime (the
  setup checks for it) and are unsigned, so Windows may say "Windows protected your PC": choose
  "More info", then "Run anyway". Installing over 2.13.1 keeps your settings and workspaces.

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
- **Future dates.** For CPI, jobs, PPI and GDP the future dates come from FRED, so without a key
  the release countdowns stop where the shipped dates do: 23 December 2026 for releases (the
  scheduled ones from late November on have no figures yet), and 2027 for Fed decisions.

### Market events: the calendar, alerts and release history

- **Toolbar fix.** The Market events button has its own calendar icon instead of sharing the
  Levels one.
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
- **Not yet while the browser is closed.** Watched alerts, released events and headlines are said
  only while the terminal is open in your browser; that part is on hold for now.

### Market events: company earnings

- **A company's earnings on its own charts.** With market events on and your Finnhub key in API
  Keys, a stock chart's report days are events: "AAPL earnings after the close, EPS 1.85, beat 1.77
  expected, revenue 102.5 billion, beat 102.2 billion expected". After the close goes on that day's
  last bar, before the open on its first; when SEC EDGAR has the results filing, the event takes its
  exact minute. Apostrophe reaches them like any other event, and `Alt+Shift+D` adds the estimate's
  source and the quarter before's EPS.
- **Only that company's charts.** Apple's reports never appear on MSFT, and a countdown or the
  Alt+E calendar's "only touching" counts a report only while one of its charts is open.
- **A new tick box**, "Company earnings, on that company's charts", in Settings → Narration and on
  the Alt+E screen, on by default. The Alt+E screen says whether earnings could be read.
- **Limits.** Finnhub's calendar holds only the latest report and the coming ones, so older
  quarters appear only where EDGAR's twenty newest filings date them; a coming report has a day and
  a session but no minute, so it gets no countdown; each company is read again every six hours.

### Market events: the Fed rate

- **A Fed decision says the rate.** Before a meeting, what Kalshi's traders priced: *"Fed
  decision, expected 4.25%, in 15 minutes."* After it, the move and the surprise on the bar:
  *"held at 4.00%, as expected"*, *"raised to 4.25% from 4.00%, higher than expected"*. No key;
  meetings from 2022 on.

### Headlines: company filings

- **A US company's SEC filings are said the minute they are public**, for the stocks on your open
  charts and the stocks you hold: "AAPL filed an 8-K: earnings results." Between bar closes, not at
  them. No key needed.
- **Off by default.** Settings, Narration, "Headlines level": important only, important and medium,
  or all. Earnings, bankruptcies, auditor changes and takeover filings are important; quarterly and
  annual reports and officer changes are medium; insider trades are low. Bond paperwork is never
  said.
- **One sentence per stock**, the most important filing first and the rest counted ("2 more
  filings"). Shift+F2 mutes it, it has its own short sound, a desktop notification when the window is
  hidden, and Alt+U keeps it under Headlines.
- **The asset dossier (Alt+I)** has a new "Recent filings" tab with the twenty newest, in New York time.

### Headlines: the news wire

- **With an Alpaca key in API Keys and "Also read Alpaca's news wire" ticked, news stories are said
  too**, for your stocks: "AAPL: Apple Reports Q4 Results, Beats Estimates." Off by default: Finnhub
  (below) covers the same stocks sooner, and a free Alpaca account's stories come in batches about
  every half hour (so up to 30 minutes after the event), none for coins. Same level and same sound
  as the filings, and a new "Include news wire stories" box beside the level for every wire.
- **Important** is results, guidance, takeovers, bankruptcy, listing notices, FDA decisions, share
  offerings, halts and hacks; **medium** is analyst upgrades and downgrades, takeover talk, officer
  changes, contracts, lawsuits and probes, and dividend rises; **all** adds the rest. Results dates,
  law-firm notices, share-holding notices, price targets, plain dividend declarations and roundups
  of many stocks are always low.
- **One sentence per symbol a minute at most**: a burst of stories is gathered for three seconds and
  the rest counted ("2 more headlines"); an edited story is not said again.
- **The asset dossier (Alt+I)** has a "Recent news" tab with the twenty newest stories, stocks and
  coins alike.

### Headlines: news from Finnhub

- **With a Finnhub key, Finnhub's news is said too**, for your stocks (Yahoo Finance, CNBC,
  Benzinga, Seeking Alpha) and, unlike Alpaca's free account, for your coins (Cointelegraph,
  CoinDesk). The free key works; it is the same key as the Finnhub prices. Each stock, and the coin
  list, is checked about once a minute. Same level, same switch, same sound.
- **Only a story whose headline names your stock, company or coin is said**: Finnhub's list for a
  stock is full of stories that only mention it.
- **A story told by both services, or by two publishers in nearly the same words, is said once.**
- Explainers, questions, "Here's why" pieces, call transcripts, market wraps and every Seeking
  Alpha opinion piece are low; subpoenas are important; probes, outages and signed deals are medium.
- The dossier's "Recent news" lists both services together, a week of each stock's stories.

### Headlines on the chart, the NEWS list, and the keys

- **Each headline sits on the bar it arrived in** (between bars, the next one), on stock and coin
  charts: the arrows add "Headline: Apple Reports Q4 Results.", Alt+Shift+D reads every one on the
  bar with its time and source, and **period / Shift+period jump between them**. A chart's own last
  30 days of news and filings are read in when you open it. Nothing is drawn yet.
- **Type NEWS in the Go bar** for every headline this session, newest first, said or not; `NEWS AAPL`
  for one stock. Enter goes to its bar.
- **The keys, one per kind of thing, Shift going back:** comma for chart formations (it was comma
  back, period forward), period for headlines, apostrophe for market events. **Ctrl+Alt+Shift with
  the same key** turns each on or off. A saved shortcut set is moved over unless you rebound them.
- **Slash opens the Go bar**, as well as Alt+G.
- Shift+comma, Shift+period and Shift+apostrophe never reached the terminal from the browser before;
  they do now, on the chart only.

### The market pulse, what changed, the morning brief and the Go bar

- **Alt+P says the market in one sentence**: *"S&P up 0.4, Bitcoin down 1.2, ten-year 4.31, up 3
  basis points, gold up 0.2, 4 minutes old, VIX closed, up 2.1 Friday."* It reads a watchlist
  named Pulse (made for you on the first press, edited in Alt+M), then anything you hold.
- **Ctrl+Alt+P reads that pulse in detail**: each item's symbol and source, last price, the start
  its change runs from and how old it is. It never fetches.
- **On a Dot Pad the pulse appears on the strip too**, an item at a time: *"SPX +0.4"*, pan right,
  *"BTC -1.2"*. The chart takes the strip back when you move on it.
- **Alt+U says what changed** while you were not listening: fills and stops, alerts, market events
  and connection problems, with their times.
- **Alt+U now includes unusual moves and signals.** A bar that closed at least five times its
  chart's usual move: *"BTC/USDT 1h up 4.2% in one bar, 9 times its usual move."* And the
  narration ladder of your background tabs, and of saved tabs while the browser is closed.
- **Alt+U says how your positions moved while you were away**: *"Positions since 09:40: BTC/USDT
  made 42.10, SOL/USDT opened, ETH/USDT closed."* Once, after the first sentence.
- **The morning brief, Ctrl+Alt+B** (or `BRIEF` in the Go bar): hear what changed, the market pulse and
  today's market events in one go: *"... Today: 13:30, CPI, important."*
- **Alt+G or slash opens the Go bar**: type a short code such as `PULSE`, `NEWS AAPL` or `RENKO`.

### Chart types and price scales

- **The chart type changes the price series itself, name and all.** Choose Line or Area and the
  series is called Line or Area, and the arrows walk one close line, read like any line: *"Close.
  line. X. Up Y from the previous close, Z percent."* Choose Renko and it is called Renko, and
  the arrows walk bricks, one reading and one short ping per brick: *"Brick. Up. 110.00 to
  120.00."* Choose Candles or Heikin Ashi and the upper wick, body and lower wick come back with
  their settings. You do not need the Price indicator for any of this.
- **Range bars and line break** are two more chart types in the Chart type list. Range bars are
  candles of equal price range, read as candles with the time they took; line break draws blocks
  that turn only when a close breaks past the last three, read as *"Block. Up. 110.00 to
  120.00."* Each has its own settings dialog and Go bar word: `RANGEBAR 500`, `RANGEBAR ATR 20`,
  `LINEBREAK 3`. A background tab on either says its new range bars or blocks.
- **Kagi** is in the Chart type list too: one line per swing that keeps growing while price goes
  its way and turns on a reversal amount, thick above the last shoulder and thin below the last
  waist. You hear the thickness as you arrow, *"Kagi line. Up, turns thick at 120.00. 95.00 to
  125.00."*, and once, live, the moment the growing line passes it: *"Kagi turns thick at
  120.00."* `KAGI 500` sets a fixed reversal amount, `KAGI ATR 20` an ATR length, and
  `KAGI 2%` a percentage of the line's end. You hear the thickness too: a thick line sounds
  heavy, a thin line nearly a pure tone, and the pitch still says up or down.
- **Point and figure** is in the Chart type list too: columns of X while price rises and O while
  it falls, one mark per box, turning only when a close comes back the reversal count of boxes
  (three by default). You hear each column's mark and boxes as you arrow, *"Column. Up, X, 4
  boxes. 100.00 to 104.00."*, a taller column sounds heavier, and live, each new box once: *"X
  column, 6 boxes, 106.00."* `PNF 500` sets a fixed box, `PNF ATR 20` an ATR length, `PNF 1%`
  a percentage of the last price, and a second number the reversal, `PNF 500 2`. Add `HL` to
  build the columns from the highs and lows instead of the closes (`PNF 500 3 HL`, or Source in
  Point and figure settings). A column that breaks the last top or bottom tells you: *"Buy
  signal, above the last X top at 105.00."* or *"Sell signal, below the last O bottom at
  95.00."*, as you arrow onto it and once, live, when it happens.
- **Six more chart types**, each heard and spoken as it is drawn. **Bars**: a thin stroke with the
  open ticked left and the close right, read *"Bar. Up. Open 101.00, high 106.00, low 100.00,
  close 103.00."*, and sounding thinner than a candle. **Hollow candles**: coloured by the close
  before, hollow when the close is above its own open, read *"Hollow green."*, *"Filled red."* and
  so on; the tone says the colour, the pitch the body. **Baseline**: the closes green above a base
  and red below it, read *"1.44 percent above the base, 104.00"*, a bright tone above and a warm
  one below, and a click where it crosses; the base is the first close in view, or a price you
  give with `BASELINE 95000`. **High-low**: only the range, read *"High 112.00, low 98.00, range
  14.00."*, a taller bar sounding heavier. **HLC area**: high, close and low lines with the space
  shaded between. **Volume candles**: as wide as their volume, heavier to the ear, and *"Heavy
  volume, 2.3 times the 20-bar average."* when it stands out. All six are in the Chart type list
  and the Go bar: `BARS`, `HOLLOW`, `BASELINE`, `HILO`, `HLC`, `VCANDLE`.
- **Alerts on Renko, range bar, line break, Kagi and point and figure charts fire on the real price.** An alert at 64,000
  fires when the market trades 64,000, not when a brick happens to end there; an alert on a drawn
  level does the same. Indicator alerts still read the indicator as the chart draws it, on the
  bricks.
- **New alert conditions: New up, New down and Reversal.** Under the Candle target they watch the
  chart's own bars: *"Trend: reversal, new down brick, 120 to 110."* On a candle chart a candle
  counts once the next one opens.
- **Percent and Indexed scales** lead every price reading in the main pane: *"Plus 4.20 percent,
  Open 95,000.50."*
- **Alt+C focuses the Chart type list and Alt+L the Price scale list.** Heikin Ashi and Log scale
  are entries in those lists. The separate buttons are gone, and the Go bar's HA and LOG still
  toggle them.
- **A Renko tab you are not looking at says its new bricks**, not its bar closes: *"BTC/USD 1h:
  2 new up bricks, 95,000.00 to 96,000.00."*, as a notification titled *"BTC/USD 1h: new brick"*
  and, with "Also speak bar closes from other tabs" on, in speech. A close that completed no brick
  is silent, and a brick the forming bar made and took back is never said. The same happens with
  the browser closed.
- **Indicators on Renko, range bars, line break, Kagi and point and figure read the built bars.**
  They used to be computed on the time bars underneath and drawn one value per brick. Now an RSI or
  the volume pane on a Kagi chart is the Kagi lines' own. Falling candles' wicks are drawn red.

### Dialogs that fit the window

- **Every dialog was laid out again** at a laptop, a large screen and a phone-width window. The
  Trading Dashboard uses the window's width, so the positions table no longer squeezes each column
  to a letter, and its Close buttons are never cut off; under 900 pixels the order ticket and the
  tables stack. Labels and tab rows wrap, fields are easier to hit, and a table too wide for its
  box scrolls inside that box instead of the whole dialog.
- **Settings > Keyboard and Help name things in words:** "Zoom in", not "ZoomIn"; "Semicolon" and
  "Menu key", not "OEM1" and "CONTEXTMENU"; "Alt+Comma", not "Alt+,".
- **The order ticket's lists show their whole choice** (they were cut off after about sixteen
  letters), and "No API key" is amber, not green. The Find symbol box fits a ticker.
- **Profit and loss reads as money**: "-120.00" rather than "-120.0000"; an amount under a cent keeps
  its digits so it never shows as 0.00.
- **The Levels report** is a proper table, with the numbers lined up and rounded sensibly.
- **Renko opens full.** A Renko chart with fewer bricks than the view holds now opens zoomed to fit
  them, instead of seventeen bricks in the left sixth of an empty chart, and going back to candles
  restores their zoom. The empty space after the last brick is a tenth of the bricks (2 to 10), not
  the 10 candles keep, so a few bricks fill the chart.
- **No more "Data link: HistoricalFilling"** on the status line, and no emoji that drew as an empty
  box: the paper-trading banner and the dashboard's marks are drawn icons now.

### Indicator templates

- **Save a chart's indicators and put them on another chart** without changing its symbol or
  timeframe. The toolbar's workspace group has Save indicator template and Apply indicator
  template; in the Go bar, `TPLS` saves and `TPL Oscillators` applies. Applying replaces the
  chart's indicators, and Ctrl+Z undoes it.

### Your account on the hosted terminal

- **Your account page** (Settings → General → Your account, or `/account/manage`): choose a
  display name and whether to join the public leaderboard, change your password or email,
  download a zip of your own data, and delete the account. Deleting needs your current
  password, the cannot-be-undone checkbox, and a 2FA code when it is on. A forgotten
  password now arrives by email when the server has the mail service. The register form
  quietly refuses anything submitted in under three seconds.
- **The public leaderboard** on accessibletrader.com lists the accounts that opted in, with their
  return, trades, win rate and the positions they hold now.
- **An Order log tab in the trading dashboard (Alt+T)**: the newest 200 orders sent, refused,
  filled or cancelled, as a table you can filter by symbol or exchange and save as CSV.
- **The website's demo** says at the bottom *"Create an account for full functionality."*

### Colour words and the buying/selling profile

- **Bars and heatmap cells say what they look like.** With "Say what lines and signals look like"
  on, a histogram or volume bar says *"Green bar."* on arrival and again only when its colour
  changes, and a heatmap cell says *"Dark cell."*, *"Medium cell."* or *"Light cell."* from how
  strongly it is drawn. A volume-profile row now ends with *"62 percent buying"* or *"58 percent
  selling"* (or *"even"*), and its sound is a triangle when buyers lead, a sine otherwise — the
  profile's pitch by part and grit by size are unchanged.

### Chart context menu

- **Right-click or the Application key now does more.** Add a text label or horizontal line at the
  clicked bar, value and pane; open an alert or a limit ticket prefilled; copy the price or the whole
  bar; start replay from here; remove every drawing with one undo. The per-series menu can preselect
  an alert, copy a reading and move an indicator into its own pane or back to price. Text labels and
  horizontal lines now work on any pane, not just price.

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

### Downloads

- **The web host's zip is one tidy folder.** It used to unzip to about 470 loose files. It now unzips to
  one folder, `AccessibleTrader-WebHost-<version>`, holding a `README.txt`, the program in `app`, and one
  file to start it: **Accessible Trader.exe** on Windows, `./accessible-trader` on Linux, **Accessible
  Trader.command** on macOS. Start it with that file, not from inside `app`.
- **On Windows, if the server cannot start** (most often because it is already running), a message says
  so and points you at `http://localhost:5145`, instead of a window that flashes and closes.
- **The web host no longer leaves a data cache in the folder you started it from.** It goes to your
  system's cache folder (`~/.cache/AccessibleTrader` on Linux).
