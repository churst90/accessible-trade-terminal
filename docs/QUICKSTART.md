# Accessible Trader: Quick Start Guide

Accessible Trader is a trading terminal built for blind and visually impaired traders. The
chart is something you hear, every function has a keystroke, and everything that happens says
so out loud. This guide gets you from a blank window to a chart, a first indicator and a first
paper trade, and gives you the keys you will look up most. The
[User Manual](USER_MANUAL.md) explains the why behind everything here; `SHORTCUTS.md` and
**F1** in the app hold the complete key list.

## Contents

1. What you are working with
2. Load your first chart
3. How a chart is laid out
4. Moving around
5. Playback
6. When the terminal speaks on its own
7. Volume, mute and hide
8. Indicators
9. Drawing tools
10. Trading safely
11. Alerts and monitoring
12. Settings, workspaces and sharing
13. Platform notes
14. Keyboard reference
15. First five minutes

---

## 1. What you are working with

**Two voices at once.** Your screen reader (NVDA, JAWS, Narrator, VoiceOver, TalkBack, or Orca
on the web host) speaks exact values: prices, indicator readings, dialog labels, confirmations.
The built-in audio engine plays the shape of the market: pitch for value, timbre for character,
bells for events. The terminal calls this the Hybrid Voice model. You can run either alone
(F2 for speech, F3 for sound) or both together.

**The sound vocabulary, in one table.**

| You hear | It means |
|---|---|
| Rising or falling pitch | Value going up or down. Every pane has its own scale, so RSI 70 is the same note on every chart. |
| Brighter, reedy tone / softer, warmer tone | An oscillator above / below its midline |
| Roughness (noise) on the tone | The reading is in an overbought or oversold zone |
| A heavier, weightier candle tone | A bigger candle body; wick pings roughen with wick length |
| A note a fourth under the candle | The volume bar: E4 up, E3 down, against the body's A4/A3 |
| Sound moving left to right | Position in the visible window: leftmost bar hard left, newest bar hard right |
| Short bells | Discrete events: crossovers (sine), divergences (triangle), levels (crystal), confluence (detuned pair), momentum (blend) |
| Very short high ping / two-note chirp / low long tone | Approaching a level / crossing it / staying beyond it |
| Boundary tone | You reached an edge: no more bars, components, panes or signals that way |

**A third layer, optional.** A Dot Pad tactile display (any model) can show the chart under
your fingers. Windows only; see the manual's Tactile Display chapter.

---

## 2. Load your first chart

**No key needed to start.** Bitstamp, Kraken, Kraken Futures, Gemini, MEXC and Binance serve
crypto market data without credentials, and so do most analytics feeds (CoinGecko, SEC EDGAR,
Fear & Greed, CoinMetrics, FINRA, CFTC, DefiLlama, Mempool and more).

**Keys worth adding, in this order.** All go in the API key manager, **Alt+K**, and stay on
your machine.

1. **Alpaca (free).** US equities and ETFs, and a **paper trading account**. Enter the paper key
   pair first. No indices: use SPY, QQQ, IWM, DIA, GLD, SLV, TLT. Typing `ES` returns Eversource
   Energy, not S&P futures.
2. **FRED (free).** The Economic analytics provider: CPI, unemployment, yields, Fed funds.
3. **TwelveData (free tier).** Spot gold `XAU/USD` works; `SPX` and `XAG/USD` need a paid tier.
4. **An AI provider (optional).** Claude, OpenAI, or a local Ollama. Only the AI Analyst uses it.

Broker keys (Coinbase, Schwab, Tradier, Oanda, Interactive Brokers) are for real orders through
a broker you already have. Add them last.

**The toolbar cascade.** Tab into the toolbar's second row: **Market → Provider → (Type) →
Symbol → Time → Load**. Each choice refills the next list and pre-selects its first entry.
Choose **Analytics** in Market to chart data feeds instead of instruments; **My Data** to chart
your own CSV files. A symbol list reading "API key required" means open Alt+K. Timeframe is a
multiplier plus a unit; quick-pick buttons show the provider's own timeframes.

**Load.** Activate **Load** or press **Ctrl+Alt+Shift+L** from anywhere. You hear "Loading
history…" then "{Symbol} on {Provider}, {Timeframe}. Ready." From then on **Shift+F1** repeats
the symbol, provider and timeframe, and **Ctrl+Alt+Shift+C** focuses the chart with a fuller
summary.

**Is it live?** A live feed announces each bar close: a short bell and "Close 42,500. Doji. New
bar: Open 42,510." Those announcements are your confirmation that data is flowing. Backslash
(`\`) jumps to the newest bar; "No live data yet" means you ran off the end of what exists.

---

## 3. How a chart is laid out

The keys in the rest of this guide move through a structure, so here is the structure.

**The X axis is time**, running left (past) to right (now), shared by every pane. Each step
is one bar of the timeframe you loaded. The Left and Right arrows walk it.

**A pane is a Y axis.** The chart is a stack of panes, top to bottom. The top pane is
**Main**: candles, the close line, and every overlay measured in price (moving averages,
Bollinger Bands, VWAP). **Volume** is a pane of its own because volume is not a price. **Every
oscillator gets its own pane**, because RSI runs 0 to 100 while MACD swings by hundreds, and a
shared scale would flatten one of them into a sliver of the other. Alt+PageUp and Alt+PageDown
move between panes and name the one you land on.

**Inside a pane: series, then components.** A series is one indicator; a component is one line
or marker it draws, with a value on every bar. MACD is one series with three components: the
MACD line, the signal line, the histogram. Up and Down walk the components of the focused
series; PageUp and PageDown walk series. Some panes divide into **strips**: Cipher B keeps its
money-flow histogram in a strip with its own axis, and Ctrl+Up and Ctrl+Down walk a strip
across every series in the pane.

**Three things live on a pane, and they are not the same.**

- A **component** has a value on every bar (an EMA, an RSI line, a histogram) or on some bars
  (a buy dot). You navigate to it and hear its value.
- A **reference level** is a constant: RSI's 70 and 30, MACD's zero, ADX's 25. It has a value,
  a meaning (overbought, oversold, neutral, or a named band such as "strong trend") and an
  owner (the indicator's, or yours from the **0** key). Levels ping as you approach, chirp as
  you cross, and are the stops for Ctrl+Left and Ctrl+Right.
- A **zone** is a region rather than a line: the shaded overbought band, a support or
  resistance zone a level indicator carries forward, or a Value Deviation zone where price
  turned away from value. Zone lines speak "support at…" and "resistance at…"; shaded bands are
  visual only.

**Three keys describe all of this.** **Alt+Shift+/** describes the pane you are in: what each
axis measures, its range, the gridline step, and what is drawn there. **Ctrl+Alt+Shift+Y**
describes the whole layout: how many panes, what is in each, what is hidden or muted.
**Shift+F1** says where you are. The manual's "How a chart is laid out" chapter has the
household picture of all this if you want one.

---

## 4. Moving around

**Time**

| Key | Action |
|---|---|
| Left / Right Arrow | One bar earlier / later, spoken and sounded |
| Home / End | First / last bar in the visible window |
| Backslash (`\`) | The newest bar |
| `[` / `]` | Pan the window; Shift+`[` / Shift+`]` shrink / grow the pan step |
| `-` / `=` | Zoom out / in |

**Structure**

| Key | Action |
|---|---|
| Up / Down Arrow | Previous / next component in the focused series |
| Page Up / Page Down | Previous / next series, in the order they are drawn |
| Alt+Page Up / Alt+Page Down | Previous / next pane (the next Y axis); clamps at the ends |
| Ctrl+Up / Ctrl+Down | Previous / next component in this strip, across every series in the pane |

**Jumps.** Ctrl+Left and Ctrl+Right skip to the previous or next event for the focused thing:
a price crossing your trend line (on candles), a signal firing (on a dot or arrow), a crossing
of the midline or an overbought/oversold line (on an oscillator), a band edge (on ADX or
Choppiness), price crossing the average (on a moving average). A hidden level is not a target,
and on a pane with more than one reference line — Aroon's Midpoint at 50 and its Oscillator's
zero — the jump uses the line the focused component answers to.
At the end: "No more {component} signals in this direction."

**Formations.** With **Describe chart patterns** on (Settings → General), `,` and `.` step
between the edges of double tops, head and shoulders, triangles, wedges, flags and ranges; `;`
chooses which overlapping formation leads, Shift+`;` releases it.

**Mouse and touch** work too: click a bar to hear it, Shift+click to measure, double-click for
the newest bar, scroll to zoom, Shift+scroll to pan, right-click or Shift+F10 for the chart
menu. Touch has a navigation toolbar under the chart.

---

## 5. Playback

| Key | Action |
|---|---|
| Space | Play / stop the whole chart, every visible unmuted series, from the left edge |
| Shift+Space | Play / stop the focused series, from the cursor |
| Ctrl+Shift+Space | Play / stop the focused component |
| Ctrl+Space | Pause / resume (paused is silent; arrows still audition bars) |
| Shift+Escape | Stop everything |
| Shift+`=` / Shift+`-` | Faster / slower (remembered across restarts) |

**What playback says.** Start, pause, speed and finish are always spoken. Beyond that,
governed by Settings → Narration → **Narrate during playback**: the date or hour as the bars
cross it, discrete **signals** on series you flagged with **N** (at most two per bar, the
rarest first), a line crossing one of its own levels ("strong trend"), and a chart formation
resolving. Continuous lines never narrate during playback, oscillator zone commentary is left
to the bar close, and volume profiles say nothing while playing. F3 turns the tones off and
leaves the words; F2 mutes the words.

---

## 6. When the terminal speaks on its own

**The bar close.** One sentence per bar: the closing candle and its pattern, then whatever your
narrated indicators made of it, up to five clauses, most consequential first. "Close 64,905 at
14:32, Bullish engulfing. New bar: Open 64,910. Triple confluence buy. RSI 14: RSI overbought."

**N chooses what may speak.** Press **N** on a series (or on one component to narrow it) and its
signals, level crossings, moving-average crosses and cloud entries are spoken at each bar
close. **Ctrl+Alt+Shift+O** switches narration off everywhere. Settings → Narration holds the
switches: Announce new bars, Narrate candle patterns as they form, Narrate chart formations as
they form (off by default), Narrate signals on bar close, Narrate during playback, Speak time
landmarks during playback.

**Where it goes.** Whatever is about the chart in front of you is spoken here. Anything else
(another tab, a symbol with no tab, or the terminal running with the browser closed) arrives as
a **system notification**, behind one switch that is on by default: Alt+J → Delivery settings →
**Events you cannot see**.

**Two mutes, one rule.** F2 silences what you asked for (navigation, summaries); Shift+F2
silences what happens to you (alerts, new bars, narration). Errors and money (fills, stops,
take-profits) speak through both. Alerts ignore every narration switch.

---

## 7. Volume, mute and hide

| Key | Action |
|---|---|
| F5 / Shift+F5 | Focused component louder / quieter |
| F6 / Shift+F6 | Focused series louder / quieter |
| F7 / Shift+F7 | Whole chart louder / quieter |
| M | Mute / unmute the focused series or component |
| H | Hide / show it (hidden things leave navigation too) |
| N | Narrate it on bar closes |
| Ctrl+Alt+Shift+K / U / O | Show everything hidden / unmute everything / narration off everywhere |
| F2 / Shift+F2 | Interactive speech / event speech |
| F3 / Shift+F3 | Chart sound / earcons |
| F4 / Shift+F4 | Tactile display / its Settings tab |

None of the mutes persist: every start is audible.

---

## 8. Indicators

An indicator is a calculation over the bars, drawn as one or more lines or markers. Trend
indicators (moving averages) smooth price; momentum oscillators (RSI, MACD, Stochastic)
measure speed and swing about a midline; volatility indicators (ATR, Bollinger Bands) measure
range; volume indicators weigh moves by participation. The manual's indicator primer explains
the common ones.

**Add one with Alt+A.** Categories: Multi-Signal, Trend, Momentum, Cycles, Positioning,
Derivatives, Volatility, Volume, Profiles, Overlays, My Data. Arrow to it, Enter. The dialog
says whether it joins the price pane or gets its own. Drawing tools are not in this dialog;
they are under Alt+D.

**Reach it** with PageDown or Alt+PageDown, then Up and Down through its components.

**Tune it with P (or Shift+F12).** Five tabs: General (parameters), Appearance, Levels,
Sonification (a sound patch per component, with bullish/bearish or above/below-midline pairs),
Speech. **Save as Defaults** makes the next indicator of that type start this way.

**0 marks the line that matters.** On an oscillator, 0 toggles the pane's declared neutral
(50 on RSI, zero on MACD, −50 on Williams %R). On the price pane it marks the price under the
cursor. Press 0 again on your own level to remove it. Indicator-declared levels live in
Properties.

**Delete** removes the focused series after confirmation. **Ctrl+Z** takes it back.

**Heikin-Ashi (Alt+C)** replaces each candle with a smoothed one: its close is the average of
open, high, low and close, its open the midpoint of the previous smoothed candle. Trends read
as runs of one colour, so they are easier to hear. Two things to know: the smoothed prices
are averages nobody traded at, so **every indicator, the close line and every formation level
keep reading the raw candles**; and with HA on, the candle you hear and its pattern are the
smoothed ones. **Alt+L** switches the price axis to logarithmic for long histories, and the
sound follows it: a level-pitched component in the price pane takes its pitch from where the
value sits on the scale as drawn.

---

## 9. Drawing tools

Drawings are placed by **sequential anchoring**: no dragging, no Enter.

1. Arrow to the first point.
2. Press the tool's chord. Speech names the point you set and the point it wants next: "Trend
   line: first point at 42,500.00, 14:30. Navigate to the second point and press the shortcut
   again."
3. Arrow to the next point and press the **same** chord. Three-point tools take a third press.
4. Completion speaks the tool's answer: the measure tool its distance, percent and bar count;
   the risk/reward tool "Risk to reward, 1 to 3.00."
5. Escape cancels a drawing in progress.

| Chord | Tool (points) |
|---|---|
| Alt+Shift+T | Trend line (2) |
| Alt+Shift+H | Horizontal line at the cursor price (1) |
| Alt+Shift+V | Vertical line at the cursor bar (1) |
| Alt+Shift+C | Price channel (2) |
| Alt+Shift+F | Fibonacci retracement: swing start, swing end (2) |
| Alt+Shift+E | Fibonacci extension: swing start, swing end, retracement point (3) |
| Alt+Shift+L | Text label, then a dialog for the wording (1) |
| Alt+Shift+R | Rectangle: opposite corners (2) |
| Alt+Shift+M | Measure tool: start and end of the move (2) |
| Alt+Shift+A | Andrews' pitchfork: pivot, median line, swing point (3) |
| Alt+Shift+G | Gann fan: origin, angle point (2) |
| Alt+Shift+B | Gann box: opposite corners (2) |
| Alt+Shift+J | Angle Fibonacci: swing start, swing end (2) |
| Alt+Shift+P | Risk/Reward: entry, stop loss, take profit (3) |
| Alt+Shift+W | Anchored VWAP from the cursor bar (1) |
| Alt+D | Drawing tools panel: review and delete |

Alt+Shift+R is the rectangle and Alt+Shift+M is the two-point measure tool; the three-point
entry, stop and target tool is Risk/Reward on Alt+Shift+P.

**Afterwards.** Focus a drawing with PageUp or PageDown and each bar reads its value and which
side price is on. **Shift+Left / Shift+Right** move the selected anchor a bar, **Shift+Up /
Shift+Down** move its price, **Ctrl+Alt+Shift+G** selects the next anchor, **Ctrl+Alt+Shift+B**
snaps it to the bar's high, low, open or close. **Ctrl+Z / Ctrl+Y** undo and redo, fifty deep.
**Shift+F10** opens the drawing's menu. Name a drawing in Properties (P) so it reads as "Weekly
resistance" instead of "Trend line 3".

---

## 10. Trading safely

**Practise first.** Settings (F12) → General → **Paper trading**, then Save. Every order on
every chart then goes to a built-in simulator: 100,000 USDT to start, fills at real live
prices, a 0.04% fee, brackets, OCO pairs, trailing exits and 1x shorts all simulated. Reset it
from the same tab; it asks first. On the hosted website paper is always on.

**Know which account you are on.** The status bar carries a PAPER or a **LIVE** badge. The
dashboard (Alt+T) speaks it when it opens: "{Provider}: LIVE account '{name}'. Orders here are
real money." The **API key for this order** dropdown at the top of the dashboard is the choice
of record; anything not marked Paper is treated as live. A key marked Paper on a venue with no
practice environment (Bitstamp, Coinbase, Interactive Brokers, Kraken, Kraken Futures, MEXC,
Schwab) is **refused**, never routed live.

**The dashboard (Alt+T).** A ticket (BUY/SELL, quantity, type, stop loss, take profit,
trailing exits, a **Size** button that sizes from a risk percentage and a stop) and five tabs:
Positions, Balances, Orders, History, Book. Positions has inline stop and target editors and
**Close position** / **Close at limit** buttons.

**Quick trade from the chart.**

| Key | Action |
|---|---|
| Ctrl+Alt+Shift+1 / Ctrl+Alt+Shift+2 / Ctrl+Alt+Shift+3 | Arm 0.5% / 1% / 2% |
| Ctrl+Alt+Shift+X | The bar under the cursor becomes the stop (direction is inferred from its side) |
| Shift+Enter | Place a limit at the bar under the cursor |
| Ctrl+Enter | Place at market |
| Ctrl+Alt+Shift+Q | Say what is armed |
| Ctrl+Alt+Shift+0 or Escape | Cancel |

Nothing places until a stop is set, and the stop always travels with the entry. What the
percentage means is a setting (Settings → General → "Quick trade: the risk percentage means"):
**the position's value** (the default: 1% of a 100,000 account is a 1,000 position, whatever
the stop) or **what I lose if the stop is hit** (the "1% rule": size = risk ÷ stop distance,
which can be a large position on a tight stop). Every bar reading reminds you that you are
armed.

**Live orders get a spoken review** with Confirm and Cancel, plus warn-only checks on
liquidation distance and correlated exposure. Change the ticket after the review and nothing
is sent until you review again.

**Money always speaks.** "Order filled…", "Stop loss hit…", "Order rejected for… {reason}" come
through every mute and every playback. The manual's Risk Management chapter is the one to
read before the first real order.

---

## 11. Alerts and monitoring

**Alerts (Alt+J).** Name, target (price, candle, indicator, point of control), condition,
level, delivery (speech, earcon, both). Alerts are scoped to the chart they were created on
unless you choose "any symbol". **Delivery settings** in the same dialog holds email, Telegram,
named webhooks (Discord, Slack), the **Events you cannot see** notification switch, and the
**Shortest timeframe to announce** floor. Advanced condition trees are available but evaluate
only while their chart is open.

**Two monitoring switches, Settings → General.**

- **Keep watching other tabs** (off by default): the other open tabs keep polling (every 30
  seconds, or live-streamed on exchanges that allow it), their alerts and strategies keep
  evaluating, and their events arrive as notifications prefixed with the symbol. Background
  strategies announce; they never place orders.
- **Keep monitoring when the browser is closed** (off by default, local web host only): the
  terminal keeps running with no browser, checking about once a minute. It evaluates every
  alert with a symbol, reports order fills on every venue you hold a key for, announces bar
  closes and the narration ladder for your saved tabs, and delivers all of it as system
  notifications. Closing the last tab sends one notification saying whether anything will be
  watched. It reports and never acts.

**Ctrl+Alt+Shift+M** speaks the status of both halves. The **system tray icon** (local web
host; Windows app) can restore the browser, show recent alerts, silence alerts and bar closes
for 30 minutes (money still comes through), toggle browser-closed monitoring, and exit. On the
Windows app the close button minimises to the tray by default. The Mac and mobile apps have no
headless monitor.

---

## 12. Settings, workspaces and sharing

**Settings (F12).** Nine tabs on the desktop, eight on the web (no Braille tab there): General,
Speech, Narration, Sonification, Braille, Appearance, Keyboard, License, About. A search box
at the top jumps to any setting. **Nothing is saved until you press Save**; Escape and Cancel
discard. Appearance choices preview immediately and are put back on Cancel; keyboard rebinds
save immediately. Speech is how the terminal says what you asked for; Narration is what it
says when you pressed nothing.

**Workspaces.** Every tab, its symbol and timeframe, indicators with their settings and sounds,
drawings, Heikin-Ashi and log scale, pane heights and running strategies. **Ctrl+Alt+Shift+W**
saves, **Ctrl+Alt+W** loads. The session autosaves every 30 seconds and resumes on start
("Resumed your last session: N tabs"); alerts persist on their own.

**Tabs.** Alt+Shift+N opens one everywhere; Ctrl+T, Ctrl+W, Ctrl+Tab and Ctrl+Shift+Tab work on
the desktop apps only. On the web press Ctrl+Alt+Shift+T to focus the tab bar, then arrows,
1 to 9, Insert to add and Delete to close.

**Sharing your setup.** Themes copy and paste as text (Appearance → Edit theme → Copy theme
text). Sound patches export and import as JSON (Alt+W). Strategies export as `.atstrat` files
and scripts as `.atpkg` packages. Everything else (settings, shortcuts, workspaces,
watchlists, screens, indicator defaults) is a JSON file in the app-data folder you can copy:
`~/.local/share/AccessibleTrader/` on Linux and macOS, `%LOCALAPPDATA%\AccessibleTrader\` on
Windows.

---

## 13. Platform notes

- **Windows, macOS, iOS, Android:** the native app. Windows drives the Dot Pad; iOS cannot
  compile custom scripts.
- **Linux:** the web host in a browser with Orca. Also the recommended build everywhere.
- **The keyboard is the same on every head.** Drawing chords are Alt+Shift+letter everywhere.
  The only differences are the browser-reserved tab chords above, and F11 for bar replay
  (use Ctrl+Alt+Shift+P in a browser). F1 always shows the bindings in effect on your host.
- **Windows with two keyboard layouts:** hold Alt+Shift, then press the letter. **Mac with
  VoiceOver:** set the VoiceOver modifier to Caps Lock, or the Ctrl+Alt+Shift chords collide.

---

## 14. Keyboard reference

**Time and viewport**

| Key | Action |
|---|---|
| Left Arrow / Right Arrow | One bar back / forward |
| Home / End | First / last visible bar |
| Backslash (`\`) | Newest bar |
| `[` / `]` | Pan left / right |
| Shift+`[` / Shift+`]` | Smaller / larger pan step |
| `-` / `=` | Zoom out / in |
| Ctrl+Left / Ctrl+Right | Previous / next crossing or signal for the focused thing |
| `,` / `.` | Previous / next chart-formation edge |
| `;` / Shift+`;` | Choose the leading overlapping formation / release the choice |

**Panes, series, components**

| Key | Action |
|---|---|
| Up Arrow / Down Arrow | Previous / next component in the focused series |
| Page Up / Page Down | Previous / next series |
| Alt+Page Up / Alt+Page Down | Previous / next pane |
| Ctrl+Up / Ctrl+Down | Previous / next component in the strip, across series |
| Shift+F1 | Symbol, provider, timeframe, focused series and pane |
| Ctrl+Alt+Shift+C | Focus the chart and speak the context summary |
| Ctrl+Alt+Shift+Y | Describe the chart layout |
| Alt+Shift+/ | Describe this pane's axes |
| Alt+Shift+D | Full analysis of the current bar: candle, patterns, every indicator, every formation |

**Playback and bar replay**

| Key | Action |
|---|---|
| Space / Shift+Space / Ctrl+Shift+Space | Play or stop the chart / series / component |
| Ctrl+Space | Pause / resume |
| Shift+Escape | Stop all playback |
| Shift+`=` / Shift+`-` | Faster / slower |
| Ctrl+Alt+Shift+P (or F11 on the desktop) | Bar replay on / off at the cursor bar |
| F9 / Shift+F9 | Reveal the next bar / hide the last revealed bar |
| F10 | Replay auto-advance play / pause |

**Speech, sound, volume**

| Key | Action |
|---|---|
| F2 / Shift+F2 | Interactive speech / event speech |
| F3 / Shift+F3 | Chart sonification / earcons |
| F4 / Shift+F4 | Tactile display on/off / open its Settings tab |
| F5 / Shift+F5 | Component volume up / down |
| F6 / Shift+F6 | Series volume up / down |
| F7 / Shift+F7 | Chart volume up / down |
| H / M / N | Hide, mute, narrate the focused series or component |
| Ctrl+Alt+Shift+K / Ctrl+Alt+Shift+U / Ctrl+Alt+Shift+O | Show all hidden / unmute all / narration off everywhere |

**Indicators and chart**

| Key | Action |
|---|---|
| Alt+A | Add indicator |
| P or Shift+F12 | Properties of the focused indicator or drawing |
| 0 | Toggle the pane's neutral level, or mark the price under the cursor |
| Delete | Remove the focused series |
| Ctrl+Z / Ctrl+Y | Undo / redo a chart edit |
| Alt+C / Alt+L / Alt+H | Heikin-Ashi / log scale / volume heatmap |

**Drawings**

| Key | Action |
|---|---|
| Alt+Shift+T, H, V, C, F, E, L, R, M, A, G, B, J, P, W | The tools in section 9 (trend line, horizontal line, vertical line, channel, Fibonacci retracement, Fibonacci extension, text label, rectangle, measure, pitchfork, Gann fan, Gann box, angle Fibonacci, risk/reward, anchored VWAP); re-press to set each point |
| Alt+D | Drawing tools panel |
| Escape | Cancel the drawing in progress (or the armed quick trade first) |
| ContextMenu or Shift+F10 | Menu for the focused drawing, or the chart menu |
| Shift+Left / Shift+Right / Shift+Up / Shift+Down | Nudge the selected anchor by a bar / by price |
| Ctrl+Alt+Shift+G / Ctrl+Alt+Shift+B | Next anchor / snap the anchor to the bar's O, H, L or C |

**Trading**

| Key | Action |
|---|---|
| Ctrl+Alt+Shift+1 / Ctrl+Alt+Shift+2 / Ctrl+Alt+Shift+3 | Arm 0.5% / 1% / 2% |
| Ctrl+Alt+Shift+X | Stop at the cursor bar |
| Shift+Enter / Ctrl+Enter | Place limit at the cursor bar / place at market |
| Ctrl+Alt+Shift+Q / Ctrl+Alt+Shift+0 | Say what is armed / cancel |

**Dialogs and panels**

| Key | Opens |
|---|---|
| F1 / F12 | Help / Settings |
| Alt+K | API keys |
| Alt+T / Alt+B | Trading dashboard / order book |
| Alt+J / Alt+S | Alerts / strategies |
| Alt+O | Object tree |
| Alt+W / Alt+, | Sound designer / custom scripts |
| Alt+M / Alt+R / Alt+I | Market watch / respect report / asset dossier |
| Ctrl+Alt+Shift+A / Ctrl+Alt+Shift+J | AI analyst / journal |
| Ctrl+Alt+Shift+I | My Data import |
| Ctrl+Alt+Shift+M | Background monitoring status |

**Tabs and workspaces**

| Key | Action |
|---|---|
| Alt+Shift+N (Ctrl+T on the desktop) | New chart tab |
| Ctrl+W (desktop) | Close tab; on the web focus the bar and press Delete |
| Ctrl+Tab / Ctrl+Shift+Tab (desktop) | Next / previous tab |
| Ctrl+Alt+Shift+T | Focus the tab bar (then arrows, 1–9, Insert, Delete) |
| Ctrl+Alt+Shift+W / Ctrl+Alt+W | Save / load a workspace |
| Ctrl+Alt+Shift+L | Load the chart for the toolbar's selection |

---

## 15. First five minutes

1. Tab into the toolbar, pick Crypto, keep the first provider and symbol, press Load. Wait for
   "Ready."
2. Press Right Arrow a few times and listen to the pitch follow price. Press Alt+Shift+D on a
   bar.
3. Press Space. Listen for ten seconds. Press Space again.
4. Press Alt+A, arrow to Momentum, add RSI. Press Alt+PageDown to reach its pane, Up and Down
   through its components, Ctrl+Right to jump to its next overbought or oversold crossing.
5. Press N on the RSI series, and leave the chart running: the next bar close will tell you
   what RSI did.
6. Press F12, tick Paper trading, Save. Press Alt+T and place a market buy for a small
   quantity with a stop below the price. Listen to the fill.
7. Press F1 and skim the full key list. Then read the manual's chapters on how a chart is laid
   out and on risk management.
