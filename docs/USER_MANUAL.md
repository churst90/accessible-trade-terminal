# Accessible Trader — User Manual

This is the full reference manual for Accessible Trader. It explains every part of
the terminal in depth: what each feature does, when you would reach for it, the
keys that drive it, and the speech you should expect to hear as you go. It assumes
you already understand core trading ideas — candles, support and resistance, stops
and take-profits, trailing stops, moving averages, oscillators — and concentrates
on how those ideas are expressed and controlled inside this application.

If all you want is a one-line-per-shortcut crib sheet, press **F1** inside the app
or read `SHORTCUTS.md`. If you are brand new, the shorter `QUICKSTART.md`
(Quick Start Guide) is a gentler tour. This manual is the place to come when you want to actually operate a
feature and understand what it is doing.

## How to read this manual

- **Keys** appear inline, like Alt+K or Ctrl+Shift+T. A chord such as Ctrl+Shift+T
  means hold Control and Shift together, then press T.
- **Spoken feedback** is written in quotation marks exactly as the application says
  it, for example "Loading history…". When a value changes with your symbol or
  price, it is shown in braces, like "{Symbol} on {Provider}, {Timeframe}. Ready."
- **Web-host modifier note:** on the Linux browser host, every Ctrl+Shift+letter
  chord becomes Alt+Shift+letter (the browser reserves the Ctrl+Shift versions).
  The letter and the command are identical. None of the onboarding keys in this
  chapter are affected. See the Platform Support chapter for the full remap.

---

## Getting Oriented

Accessible Trader is a full trading terminal — real-time and historical data across
stocks, crypto, forex and more — built so that none of it depends on sight. Every
function, from placing a drawing tool to tuning an indicator's timbre to submitting
an order, is reachable from the keyboard, and nothing requires a mouse. What makes
the application different from a conventional platform with a screen reader bolted
on is that it was designed around sound from the start, and it treats your screen
reader and its own audio engine as two halves of one instrument.

### The Hybrid Voice model

The central idea, which everything else builds on, is what the application calls the
Hybrid Voice model. Your screen reader and the built-in sonification engine carry
different kinds of information at the same time, and neither replaces the other.

Your screen reader — NVDA, JAWS, Narrator, VoiceOver, TalkBack, or Orca on the web
host — handles everything exact and textual: the precise open, high, low and close
when you land on a candle; an indicator's exact reading when you move to it; dialog
labels, menu items and settings; and the confirmation messages after you act. If
you need a number, the screen reader is where it comes from.

The sonification engine handles everything continuous and structural: the rising and
falling pitch of price as it moves through time, the tonal texture of an oscillator
as it swings overbought or oversold, distinct bell tones for events like crossovers
and divergences, the stereo position that tells you where in the visible window you
are, and volume that scales with how significant a move is. If you need a shape — a
trend, a rhythm, a sense of where pressure is building — the audio engine is where
it comes from.

The point of running both together is that you perceive precise values and broad
structure at once, which is something a conventional chart cannot offer in either
direction. During playback you hear them in concert: the screen reader speaks each
bar's numbers while the engine plays that bar's sonic shape. You can also lean on
one alone — silence the audio and work by speech, or mute speech and listen to pure
shape — and later chapters show you how to toggle each as the task demands.

### The soundscape

Because so much of the terminal speaks in sound, it is worth learning the vocabulary
once; after that it becomes second nature and you will stop having to think about it.

Pitch maps to value, everywhere and consistently. Higher pitch means a higher price
or a higher indicator reading; lower pitch means lower. A rising trend is a rising
pitch; a sell-off slides downward. For oscillators that have a centre line — an RSI,
a MACD — pitch rises above a mid-point for positive readings and falls below it for
negative ones, so zero has a recognisable pitch you come to know by feel.

The *kind* of sound — its timbre, the tonal colour rather than the pitch — tells you
where a value sits relative to the structure that matters. An oscillator above its
zero line uses one waveform and below it uses another, so you can hear which side of
zero you are on without checking a number; and when a reading pushes into an
overbought or oversold extreme, a faint noise texture is layered in so the
extremity itself is audible. You learn to hear "deep oversold" the way a sighted
trader sees a line pinned to the bottom of its range.

On top of the continuous tones, discrete events ring as short bells, and each event
type has its own bell so you can tell them apart by ear: a smooth sine bell for
crossovers, a bright metallic triangle bell for divergences, a high pure crystal
bell for support and resistance levels, a shimmering detuned pair for
high-confluence signals, and a rich multi-harmonic blend for momentum signals. When
a bell catches your attention during playback you can stop and read the exact event
through your screen reader.

Two more cues round out the picture. Stereo position places you in time: the
leftmost visible bar plays hard in the left channel and the rightmost hard in the
right, so as playback advances or you move the cursor rightward the sound travels
across the stereo field and you feel your position in the window without a spoken
word. And the engine layers sound by depth — quieter background tones for long-term
context, mid-level tones for the main price and oscillator voices, and the loudest
layer reserved for signals and event bells — so an important event cuts through the
continuous wash instead of competing with it at the same volume.

None of this needs to be memorised before you start. Load a chart, press the right
arrow a few times, and let the pitch move with the price; the rest of the vocabulary
arrives naturally as you meet it in the chapters that follow.

---

## Loading a Market

When the terminal first opens it is deliberately quiet. There is no chart, no
symbol, and no spoken greeting — just an empty workspace waiting for you to tell it
what to look at. That silence is intentional: the application never assumes which
market you trade or whose data you pay for. This chapter takes you from that blank
start to a live, navigable chart, and explains what you will hear at each step so
you always know where you are in the process.

The whole sequence is short once it is familiar: connect a data source if the
provider needs credentials, pick a market, provider, symbol and timeframe from the
toolbar, and press Load. The terminal then fetches recent history, announces that
the chart is ready, and — where the provider supports it — begins streaming live
bars.

### Connecting a data source

Market data comes from providers, and many of them require an account and an API
key before they will return anything. You manage those credentials in the API key
manager, which you open with Alt+K. Your screen reader announces the dialog as
"API Keys", and it opens on a list of any profiles you have already configured,
followed by a form for adding a new one.

A profile is one set of credentials for one provider in one environment. The
"Add New Profile" form walks top to bottom: choose the **Provider** (Alpaca,
Binance, Coinbase, Kraken, Oanda, Polygon, Schwab and the rest, or "Custom"), give
the profile a **Profile Name** you will recognise later — the placeholder suggests
something like "Alpaca Paper" — and set the **Environment** to either Paper or
Live. Paper points the provider at its simulated/sandbox endpoints; Live uses your
real, funded account. Below that you set the **Market Type** (Spot, Futures, Crypto
or Stocks) and then enter the **API Key**, the **API Secret**, and, only if your
provider issues one, a **Passphrase** (it is labelled Optional and you can leave it
blank for providers that do not use one). All three secret fields are masked.

Activate the **Save Profile** button and the terminal confirms with "Profile
{name} saved". A profile must be the active one for its provider before its data
or trading access is used in the current session; if you keep more than one profile
for a provider — say a Paper profile and a Live profile — select the one you want
and activate it, and you will hear "{name} set as active". The active profile is
read back in bold in the profile list so you can confirm at a glance which
environment you are about to trade against. This Paper/Live distinction matters
later when you place real orders, so it is worth getting into the habit of checking
it here first.

Schwab is the one provider that does not take a typed key, because it uses a
browser sign-in instead. For a Schwab profile you will see a **Sign in** button;
activating it announces "Opening Schwab sign-in in browser", hands you off to
Schwab's own login in your default browser, and on return confirms "Schwab sign-in
complete for {name}".

Not every provider needs a key. Several crypto sources and the free historical
archives chart happily with no credentials at all, so if you only want to study
those you can skip this dialog entirely and go straight to the toolbar. The
terminal will tell you, in the next step, on the rare occasion a key is actually
missing — you do not have to memorise which providers need one.

### Choosing what to chart

Everything you select to build a chart lives on the toolbar's second row, in the
order the terminal needs it: **Market**, **Provider**, an optional **Type**,
**Symbol**, and **Time**. There is no dedicated shortcut for these fields; you Tab
into the toolbar and Tab through them left to right, and your screen reader reads
each control's label and current value as you land on it.

These four selectors form a cascade — each choice decides what the next one can
offer. Choosing the **Market** (for example Crypto, Stock, or Forex in trading
mode) refills the **Provider** list with the sources that cover that market and
automatically selects the first of them. Choosing a **Provider** refills the
**Symbol** list and the available timeframes and, again, selects the first symbol
for you. One consequence worth understanding: moving through this cascade does not
speak on its own — the terminal repopulates the dropdowns silently, and it is your
screen reader, reading each list as you open it with the arrow keys, that tells you
what is now available. So after picking a market, expect to arrow through the
Provider and Symbol lists to hear and confirm your choices rather than waiting for
an announcement.

Some providers split a market into a **Type** — most commonly Spot versus Futures
on crypto exchanges. When that distinction applies, a Type selector appears between
Provider and Symbol; when it does not, the field is simply absent and the Tab order
closes up around it.

The **Symbol** list is a standard dropdown rather than a search box. Open it and
type the first letters of a ticker to jump to it — typing "B" then "T" walks you
toward BTC pairs, for instance — exactly as you would in any list your screen
reader knows. This is also where a missing credential surfaces: if the provider you
picked requires a key you have not configured, the Symbol list contains a single
entry reading "⚠ API key required — open API Keys (Alt+K)", and the Load button
stays disabled until you go back and add one. That sentinel is the terminal's way
of pointing you to Alt+K at exactly the moment it matters.

**Time** is two controls working together: a multiplier you type (1 to 999) and a
unit you choose — min, hr, day, wk, or mo. Together they read as a timeframe such
as 1 hr or 15 min; the default is 1 hr. When a provider advertises a set of common
timeframes, quick-pick buttons appear alongside the two fields so you can jump
straight to, for example, "Set timeframe to 1h" without touching the multiplier.

A worked path makes the cascade concrete. Suppose you want Bitcoin against the
dollar on Binance at the hourly. Tab to Market and select Crypto; the Provider list
fills and lands on Binance. Tab to Provider and confirm Binance (or arrow to
another exchange). Tab past the Type field — leaving it on Spot — to Symbol, open
the list, type "BTC" to reach BTC/USDT, and select it. The Time field is already 1
hr, so you are done choosing. Because the cascade pre-selected sensible firsts, a
common shortcut is to pick only the market, glance through to confirm the provider
and symbol it chose for you, and load that.

### Loading the chart

With a symbol chosen, Tab to the **Load** button and activate it. The terminal
announces "Loading history…", interrupting whatever was being said, while it
fetches the most recent bars. When the data is in and the chart is built, it
announces the full identity of what you are now looking at — "{Symbol} on
{Provider}, {Timeframe}. Ready.", for example "BTC/USDT on Binance, 1h. Ready." —
again interrupting so you hear it immediately. That "Ready." is your cue that the
chart is populated and every navigation and playback command in the rest of this
manual is now live.

If the fetch fails — a bad or inactive key, a provider outage, an unsupported
symbol — you hear "Chart failed to load." instead, and the toolbar shows a matching
error such as "Chart load failed. Check provider settings." The usual fixes are to
confirm the right profile is active (Alt+K) and that the symbol is one the provider
actually serves.

At any later moment you can re-hear what is loaded without touching the toolbar:
press F4 and the terminal announces the current symbol, provider, and timeframe.
This is handy after you have been deep in navigation for a while and want to
reconfirm the instrument before acting on it.

### Knowing whether your data is live

A loaded chart is not automatically a live one. After the "Ready." announcement the
terminal keeps working in the background: it fills in any gap between the history it
fetched and the present moment, and then, if the provider streams, it switches to
receiving real-time ticks. There is no separate "you are live now" banner, so the
terminal tells you in the language it uses everywhere else — sound and speech.

The clearest signal is a closing bar. Once a live stream is running, each time the
current bar closes and a new one opens you hear a short bell and an announcement in
the shape "Close {price}. {pattern}. New bar: Open {price}." — for example "Close
42,500. Doji. New bar: Open 42,510." Those rolling new-bar announcements are the
sound of a live feed; their arrival is your confirmation that data is flowing. (If
you find them distracting you can switch them off in settings, so treat their
presence, not their absence, as the positive signal.)

To check the live edge deliberately, press Backslash (\\) to jump the cursor to the
most recent bar. If you navigate forward past the end of the fetched data into time
that has not happened yet, the terminal says "No live data yet", which tells you
you have run off the end of what exists rather than that anything is wrong. A
provider that offers only historical data — or a live connection that has not come
up — simply never produces those new-bar bells; the chart stays perfectly usable
for study, it just is not advancing on its own.

### Switching markets later

You are not locked into your first choice. Return to the toolbar at any time,
change any selector, and press Load again to replace the chart. One case is worth a
heads-up: if you load an analytics provider — one that returns single scalar
metrics like an economic series rather than OHLCV candles — onto a tab that already
holds indicators or drawings, those tools cannot apply to a non-candle series, so
the terminal stops to confirm with a "Switching to analytics" dialog. It offers
three choices: "Continue (strip & load)" replaces the chart and removes the tools
that no longer fit, "Open in New Tab" loads the analytics series beside your
existing work and leaves it untouched, and "Cancel" backs out. When in doubt, open
it in a new tab so you keep both views.

With a chart loaded and confirmed live, you are ready to read the market itself —
which is where the next chapter, on moving through time bar by bar, begins.

---

## Reading the Chart

A loaded chart is a structure you move through, not a picture you glance at. This
chapter covers how you navigate that structure by keyboard, how you scan it for the
moments that matter, how you play it back as sound, and how you control what you hear
while you do. None of it requires sight; all of it is faster once the handful of
movement keys are in your fingers.

### The shape of a chart

The chart is a stack of **panes**, one above the next. The top pane is always the
price itself — candlesticks by default. Below it sit indicator panes, one for each
indicator that needs its own area; some indicators, like moving averages, instead
draw directly on the price pane as overlays and never get a pane of their own.

Inside a pane the hierarchy continues. A pane holds one or more **series**, and a
series holds one or more **components**. A MACD, for instance, is one series with
three components — the MACD line, the signal line, and the histogram. Holding this
three-level shape in mind — panes, series, components — is the key to moving around
with confidence: you change panes one way, and components another, and the terminal
always tells you where you have landed.

### Moving through time

Left and Right arrow move the cursor one bar at a time — Left into the past, Right
toward the present. As you land on each bar your screen reader announces its values:
on the price pane, the open, high, low and close of that candle; on an indicator
component, that component's reading for the bar. At the same time the sonification
engine plays the bar's tone, so a run of Right-arrows is also a little rising or
falling melody of where price went.

When you want to cover ground faster, Home jumps to the leftmost bar in view and End
to the rightmost, while Backslash (\\) leaps all the way to the latest, live bar.
You can also move the window itself without moving the cursor: the bracket keys pan
the viewport, with `[` bringing older bars into view and `]` newer ones, and
Shift with either changes the pan step. The minus key zooms out to see more bars at
once, and the equals key zooms in for finer detail on fewer.

### Moving between panes and components

Page Down moves your focus to the next pane below, Page Up to the pane above; as you
arrive, speech announces the newly focused series by name — "RSI", "Volume". Within
a series, the Up and Down arrows step through its components: Down from the MACD line
to the signal line to the histogram, Up back through them, each announced with its
name and current value. When more panes are open than fit on screen, Alt+Up and
Alt+Down scroll the pane list.

Two keys re-orient you whenever you lose the thread. F4 announces the current symbol,
provider, and timeframe; Ctrl+Alt+Shift+C focuses the chart and reads a fuller
context summary. Reach for them freely — there is no penalty for asking the terminal
where you are.

### Scanning for events

Stepping bar by bar is precise but slow when what you actually want is the next thing
that *happened*. Ctrl+Left and Ctrl+Right are context-aware jumps that depend on what
you have focused. On a price candle they jump to the previous or next bar where price
crosses a trendline you have drawn. On an oscillator that crosses zero, like MACD,
they jump to the next zero-crossing; on a banded oscillator like RSI, to the next
entry into or exit from overbought or oversold; on a moving-average overlay, to the
next price-versus-average cross; on a sparse signal marker, to the next bar where
that signal fires. Press repeatedly to walk through every such event in turn; when
there are no more in that direction the terminal says "No more {component} signals in
this direction." It turns a long history into a short list of the moments worth
hearing.

### Playback: listening to the market

Playback is the core listening mode: the cursor animates through the visible window
while the engine plays each bar's sound and your screen reader speaks its values, so
you *hear* the shape of a stretch of market in seconds. Three keys set how much you
hear. Space plays or stops the whole chart — every pane and series together. Shift
with Space plays just the series you have focused, for studying one indicator without
the rest. Ctrl+Shift+Space narrows further to a single component — the RSI line
alone, say. Ctrl+Space pauses and resumes whatever is playing, and Shift+Escape is
the panic key that stops all playback at once. Shift with the equals or minus keys
speeds playback up or slows it down — slower to dwell on each bar, faster to scan a
long history.

### Choosing what you hear

You are always in command of the two output layers. F2 toggles speech on and off and
F3 toggles the sonification engine, so you can run with numbers only, sound only, or
both, to suit the task. Volume is adjustable at three depths: F5 and Shift+F5 raise
and lower the focused component, F6 and Shift+F6 the whole focused series, and F7 and
Shift+F7 the master chart volume — so you can bring one quiet line forward without
touching the rest. The M key mutes or unmutes the focused series or component without
removing it, and H hides or shows it; both are toggles, and both leave the data in
place so you can bring it back with the same key.

### Inspecting a single bar

When one bar deserves a thorough look before you act on it, press Ctrl+Shift+D
(Alt+Shift+D on the Linux web host) for a full point analysis. It reads the candle's
open, high, low, close and volume, names any candlestick pattern recognised at that
bar — "Engulfing bullish" — reports every active indicator's reading there, and lists
any signal events on the bar across all indicators. It is the one-key way to gather
everything the terminal knows about a single moment, which is exactly what you want
in front of a decision — and a natural lead-in to the analysis tools in the next
chapter.

---

## Analysis Tools

The chart is only the canvas. This chapter covers what you put on it to make sense of
the market: indicators that compute and sonify their own readings, drawing tools you
place by keyboard and then hear the cursor cross, the volume profile that maps where
trading actually concentrated, and the heatmap that colours activity into the
playback itself.

### Adding and tuning indicators

Press Alt+A to open the Add Indicator dialog. Indicators are grouped into categories
— Multi-Signal, Trend, Momentum, Volatility, Volume, and Profiles — and you move
through the category and indicator lists with the arrow keys and add one with Enter.
A new indicator arrives with audio properties already chosen for its type, so it is
immediately playable; you can refine them later.

Once it is on the chart you reach it the same way you reach any pane — Page Down from
the price pane until speech announces it — and explore its components with Up and
Down, as the previous chapter described. Two small touches help here. Pressing 0
(zero) on a focused indicator adds a zero-level reference line, which both sounds
during playback and gives Ctrl+Left and Ctrl+Right something to jump between. And
when an indicator has outlived its use, Delete removes the focused pane after a
spoken confirmation — the price pane itself cannot be removed.

To change how an indicator calculates or sounds, focus it and press P (or
Shift+F12) for its properties dialog. There you adjust calculation parameters —
periods, smoothing, thresholds — and, per component, the things that shape how it is
heard: the waveform that colours its continuous tone, the bell patch that rings on
its signal events, and its relative volume. A "Save as Defaults" option stores your
preferences so the next indicator of that type starts already configured the way you
like. Tab and the arrow keys move through the dialog and your screen reader reads
every label and value.

Two whole-chart toggles live near the indicators. Alt+C switches the price pane to
Heikin-Ashi candles, a smoothed formula that strips noise and can make a trend easier
to hear; Alt+L switches to a logarithmic price scale, useful over long histories
where price has moved by large percentages.

### Drawing tools

Drawing tools place reference lines and shapes that then become audible as you
navigate — the cursor will announce when it crosses a trendline you have set. Every
tool uses **sequential anchoring**, which is what makes them fully keyboard-
accessible: you do not hold and drag, and you do not press Enter. Instead you
navigate the cursor to a point and press the tool's own shortcut to drop an anchor
there; navigate to the next point and press the **same** shortcut again to set the
next; and so on until the shape is complete. Escape cancels a drawing in progress.

Take a trendline (Ctrl+Shift+T) as the model. Arrow to the first point and press
Ctrl+Shift+T — speech confirms "Trend line: anchor 1 set at {price}, {time}.
Navigate to next point and press the shortcut again." Arrow to the second point,
press it once more, and the line completes: "Trend line placed from {price} to
{price}." Three-anchor tools — Fibonacci extension, Andrews' pitchfork,
Risk/Reward — simply take a third press. Single-anchor tools — a horizontal price
line, a vertical time marker, a text label, an anchored VWAP — finish on the first
press, placing immediately at the cursor.

> **Linux web host:** every `Ctrl+Shift+<letter>` drawing chord becomes
> `Alt+Shift+<letter>` in the browser, because the browser reserves the Ctrl+Shift
> versions. Same letter, same tool. The desktop and mobile apps use Ctrl+Shift as
> written, and the F1 help always shows the bindings live on your host.

The set spans Trendline (T), Horizontal and Vertical lines (H, V), Channel (C),
Fibonacci retracement and extension (F, E), Text label (L), Rectangle (R), Measure
(M), Andrews' pitchfork (A), Gann fan and box (G, B), Angle (J), Risk/Reward (P), and
anchored VWAP (W); Alt+D opens a panel to review and delete what you have placed. A
few behave specially. A Fibonacci retracement, anchored from a swing low to a swing
high, lays down the standard 23.6, 38.2, 50, 61.8 and 78.6 percent levels, each
audible as the cursor crosses it. An anchored VWAP behaves like a moving-average
overlay you can focus and whose crossings you can jump between. And the Risk/Reward
tool, after you set its entry and stop anchors, speaks the resulting risk and then
asks for the target, announcing the full reward-to-risk ratio once you set it — the
same measuring workflow described in the Trading chapter.

### The volume profile

A volume profile is a different kind of view: instead of volume across time, it shows
volume across **price** — a horizontal histogram revealing which price levels saw the
most trading. When your focus is on a volume-profile series the Up and Down arrows
change meaning: rather than stepping through components they move between price bins,
Up to the next higher level and Down to the next lower, each announced with its price
and the volume there. Left and Right still move the cursor through time as usual.

Two landmarks are worth listening for. The **Point of Control** — the single
highest-volume price level — rings with a distinct square-wave tone and is announced
"Point of Control" when you land on it. The **Value Area**, the band of prices that
accounts for roughly seventy percent of all volume, announces "Entering Value Area"
as you move into it and "Exiting Value Area" as you leave — so you can feel the edges
of where the market agreed on price.

### The heatmap

Alt+H toggles a volume heatmap over the price chart, shading each candle and time
zone by how much trading happened there. Its real payoff is in sound: with the
heatmap on, playback folds that intensity into the audio, so higher-volume bars play
louder than quiet ones and a busy stretch of market is something you hear swell
rather than something you have to look up. Press Alt+H again to turn it off.

---

## AI, Narration, and the Journal

Three features sit between reading the chart yourself and acting on it: an AI analyst
you can ask for a second opinion, an auto-narrator that watches a series and speaks
up when something happens, and the Journal that quietly records everything the
terminal has said so nothing scrolls past for good. All three chords use three
modifiers and so are **not** remapped on the web host — they are the same everywhere.

### The AI technical analyst

Press Ctrl+Alt+Shift+A to open the AI Analyst. It gathers a snapshot of what you are
looking at — recent candles, a summary of your active indicators, and, where the
provider supports vision, an image of the chart — sends it to a large language model,
and reads back a concise, plain-language technical analysis written for text-to-
speech: trend direction, key support and resistance, momentum, and a short-term
outlook. It is a second pair of eyes on demand, useful when you want a narrative
framing of the structure you have been navigating bar by bar.

It needs a key. Add one for at least one AI provider in the API key manager (Alt+K);
the terminal tries the providers it knows in order — Claude, then OpenAI, then Ollama
— and uses the first you have configured. If none is set up, the Analyst tells you so
rather than failing silently. And because the request goes to an outside service,
only use it with a provider you are comfortable sharing your chart data with.

### Auto-narration

Auto-narration is the hands-off companion to navigating yourself. Press
Ctrl+Alt+Shift+N to toggle it on for the series you have focused, and from then on
the terminal watches that one indicator and **speaks new events as they occur on live
bar closes** — a fresh signal firing, or the oscillator entering or leaving an
overbought or oversold zone. It announces only what happens *after* you switch it on;
it does not replay the history you already navigated. Because it is per-series, you
can leave it running on the one indicator you care about and not be interrupted by the
rest of the chart — set it on your RSI, say, and get on with reading price while the
terminal keeps half an ear on momentum for you.

### The Journal

The Journal, opened with Ctrl+Alt+Shift+J, is the terminal's memory of the session.
Everything it has spoken or alerted — ordinary speech, alerts, strategy setups,
errors, and backtest results — is written here, newest at the bottom, up to a couple
of thousand entries. It is the primary way to recover something that went by in
speech while you were concentrating elsewhere: a fill you half-heard, an alert that
fired mid-thought, the exact wording of an error.

The view is a plain monospace text area you can Tab into to read or copy any line,
with filter buttons — All, Speech, Alerts, Setups, Errors, Backtests — to narrow it
to just the kind of entry you are after, and a "Copy visible" button to lift the
current selection out to paste elsewhere. Automated-strategy setups land here in
full, with their rationale spelled out — side, score, stop price, first target,
reward-to-risk, and the notes on where the stop was placed — so the Journal doubles
as the record of what your strategies proposed and why. When in doubt about what the
terminal just told you, this is where you go to read it back at your own pace.

---

## Trading

Everything to do with money — placing orders, attaching protective exits, watching
positions and fills, and reading the live order book — runs through the trading
dashboard, which you open with Alt+T. Your screen reader announces it as "Trading
Dashboard". It gathers four things in one place: the market you are trading and its
environment, an order ticket, a five-level order book snapshot, and a row of account
tabs — Balances, Positions, Orders, and History. This chapter assumes you already
know what market, limit, stop, and trailing orders are, and concentrates on how you
express and hear those decisions here, and on what the terminal does and does not do
on your behalf.

### Practise first: paper trading

Before risking a cent, turn on **paper trading mode** and learn the whole workflow
against simulated money. Open Settings with F12, and on the General tab tick "Paper
trading mode"; the terminal confirms "Paper trading enabled" and a small paper
indicator appears in the status bar (announced as "Paper trading enabled"). From
then on, every order you place — on any chart, with any provider — is routed to a
built-in simulator instead of a real exchange. Fills are driven by the **real
live price** of the chart you are watching: a market order fills at the current
price, and a stop, target, or trailing order fills the moment live price action
actually crosses it. You start with a virtual balance, the account persists between
sessions, and a "Reset paper account" button on the same settings tab wipes it back
to the starting balance whenever you want a clean slate. While paper mode is on the
dashboard shows the environment as "Paper (simulated)" with a paper banner, and the
red live-funds banner is suppressed. Everything described in the rest of this
chapter behaves identically in paper and live — so practise here until the spoken
feedback is second nature, then switch a real key in.

### Paper or live — check this first

When you are not in paper mode, your environment follows whichever API key profile
is active. A profile marked Paper points at the provider's own sandbox; a profile
marked Live trades real, funded money. The dashboard shows the current environment
in its market panel, and when you are on a live profile it puts an unmissable red
banner across the top of the controls — "⚠ LIVE TRADING — Real funds at risk." —
that stays on screen the whole time.

You can change accounts without leaving the dashboard: the "Switch API Key" dropdown
lists your profiles as "{name} ({environment})", and selecting one announces
"Switched to API key {name} ({environment})" and reloads that account's balances and
positions. Build the habit of confirming this out loud before a session — it is the
single most consequential setting on the screen.

### Placing an order

The order ticket lives in the dashboard's "Place Order" panel and reshapes itself to
the order you are building, so you only ever Tab past fields that apply. You choose a
side with the big round green "BUY" and red "SELL" buttons — they are a toggle, so
exactly one is active and your screen reader reports which is pressed — then enter a
"Quantity" and pick a "Type".

The Type list holds the full set: **Market**, **Limit**, **Stop-Market**,
**Stop-Limit**, **Take-Profit-Market**, and **Take-Profit-Limit**. The fields that
appear depend on it. The stop and take-profit types reveal a "Trigger Price" — the
level at which the order activates. The limit-style types (Limit, Stop-Limit,
Take-Profit-Limit) reveal a "Limit Price" and, with it, a "Time in force" choice of
GTC, IOC, or FOK and a "Post-only (maker)" checkbox. A plain market order fills at
the prevailing price and needs neither.

On a market or limit *entry* you also get the protective and sizing controls
described in the next two sections — Stop Loss, Take Profit, trailing exits, and a
risk sizer. And if your provider supports margin or futures, the ticket adds a
"Margin" choice (Cross or Isolated), a "Leverage" multiplier, a "Position side"
(One-way, Long, or Short, for hedge accounts), and a "Reduce-only" checkbox. Controls
the active provider does not support are simply not shown — so the same ticket is
lean on a spot exchange and full on a futures one.

Two helpers worth knowing before you submit. The **Size** button next to "Risk % of
balance" does position-sizing for you: enter a risk percentage and a stop, and it
sets the quantity so that being stopped out costs you that share of your balance,
announcing "Sized {quantity} from {n} percent risk." And a double-tap guard ignores
an identical order resubmitted within thirty seconds, so a stray second press will
not double your size.

### Protecting a trade: stops, targets, and trailing exits

You protect a trade at the moment you enter it. On a market or limit entry the ticket
offers an optional "Stop Loss" and "Take Profit", both entered as **absolute price
levels** — buy at 42,500 and you might type 42,180 to stop out and 43,400 to bank
profit. Leave either blank to skip it.

When you would rather your exit follow the trade, use a **trailing stop** or a
**trailing take-profit** (shown when the provider supports trailing). The "Trailing
stop" selector lets you trail by Percent or by Amount; enter a distance and the
terminal keeps a stop that ratchet toward the trade as price moves your way and
never loosens, firing only on a reversal of that distance. The "Trailing
take-profit" works the same way but adds an optional "TP activation price": it stays
dormant until price reaches that level, then arms and trails from there, so it locks
in profit only after a target is reached. In paper mode you can watch both work tick
by tick.

Two cautions carry over from real exchanges. First, an entry and its protective
orders are **not placed as one guaranteed bracket**: the terminal submits the entry,
then the protection, then about two seconds later checks that protection actually
landed — and if it cannot find it, warns you, interrupting, "Warning: no stop loss
or take profit found on the exchange for {symbol}. The position may be unprotected —
verify your open orders." Treat that as a call to action. Second, there is no inline
editor for a resting order's protective levels; to move a stop you cancel it on the
Orders tab and place a new one. Set your exits deliberately at entry.

### The live order review

Paper orders submit the instant you activate the submit button. **Live** orders do
not — they get a spoken safety review first. When you submit on a live profile, the
terminal speaks a one-line summary — "Confirm: {side} {qty} {symbol}, {type}. Stop
{price}. Target {price}. Estimated cost {amount}, fee {amount}. Confirm or cancel." —
and replaces the submit button with **Confirm** and **Cancel**. Nothing reaches the
exchange until you activate Confirm; Cancel backs out with "Order canceled before
submit." It is the deliberate pause that a real-money order deserves, and it is on
automatically whenever you are live.

### Hearing your fills

Order outcomes are the one kind of feedback the terminal will never let you miss.
Whatever else is happening — sonification muted, a playback running, speech mid-
sentence — an order event plays a short earcon immediately and then speaks over
whatever was being said. The announcements are plain, quantity-and-price first, and
on a closing fill they add the realized result:

- Placed: "Order placed. {Buy|Sell} {qty} {symbol}, {type}."
- A complete fill: "Order filled. Bought {qty} {symbol} at {price}." — and on a
  close, "… Profit {amount}." or "… Loss {amount}."
- A partial fill: "Partial fill. … {remaining} remaining."
- A fixed stop or target: "Stop loss hit. Sold {qty} {symbol} at {price}. Loss
  {amount}." / "Take profit hit. … Profit {amount}."
- A trailing exit: "Trailing stop hit. …" / "Trailing take profit hit. …", with the
  same price and profit/loss.
- A cancel: "Order canceled. {symbol}."  A refusal: "Order rejected for {symbol}."

Order IDs are never spoken — they are meaningless to the ear. Every one of these
events is also written to the Journal (Ctrl+Alt+Shift+J), so a fill that goes by
while you are concentrating elsewhere can be read back afterward. (Realized profit
and loss are spoken on every paper close; on a live exchange they appear when the
exchange reports them.)

### Positions, orders, balances, and history

The lower part of the dashboard carries four tabs — Balances, Positions, Orders, and
History — and switching to one speaks its name and count, like "Positions, 2".

- **Positions** lists each open position with its quantity, average price, value,
  unrealized profit or loss, leverage, and liquidation price, and gives each a
  **Close** button that flattens it with an opposing market order — so exiting is now
  a single action, announced as "Closing {symbol}. …".
- **Orders** lists your working, not-yet-filled orders — side, type, quantity, price,
  status — each with a Cancel button to pull a resting limit or protective stop.
- **History** is your fill log, newest first: time, symbol, side, quantity, price,
  realized profit or loss, and fee — the place to review how a session actually went.
- **Balances** shows your free and locked funds per asset.

### Reading the order book

Press Alt+B to open the order book for the current symbol; your screen reader
announces it as "Order Book — {symbol}". It presents the resting buy and sell
interest as two columns — "Bids (Buy Orders)" and "Asks (Sell Orders)" — each listing
up to twenty price levels with their size and a running cumulative total, under a
summary line giving the best bid, best ask, and the spread as both a number and a
percent (for example "1.50 (0.004%)").

You read the book by Tab: every price level is focusable, and landing on one is
announced as "Bid {price}, size {quantity}" or "Ask {price}, size {quantity}", so you
can walk down the bids to feel where demand thins out or up the asks to find a wall of
supply. If your provider streams the book it updates live as you read; otherwise a
"Refresh" button pulls a fresh snapshot. It updates quietly rather than narrating
every change, so it is a place you go to read depth deliberately.

### A trade from start to finish

Putting it together, in paper mode: you are watching BTC/USDT on Binance and decide
to buy a pullback. You press Alt+T, hear "Paper (simulated)", and Tab through the
ticket — BUY, quantity 0.5, type Market, stop loss 42,180, and a Trailing
take-profit of 1.5 percent with an activation at 43,000. You activate "Submit Buy
Order"; because you are in paper it places at once, and you hear the earcon and
"Order filled. Bought 0.5 BTC/USDT at 42,500." Price climbs through 43,000, arming
the trailing take-profit, and then pulls back: "Trailing take profit hit. Sold 0.5
BTC/USDT at 43,260. Profit 380.00." You open the History tab and the trade is there
with its price, profit, and fee. Had price instead fallen to 42,180 first, you would
have heard "Stop loss hit. Sold 0.5 BTC/USDT at 42,180. Loss 160.00." — and either
way, when you want out early, the **Close** button on the Positions tab flattens you
with one press.
