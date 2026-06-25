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

## Trading

Everything to do with money — placing orders, attaching protective exits, watching
positions and fills, and reading the live order book — runs through the trading
dashboard, which you open with Alt+T. Your screen reader announces it as "Trading
Dashboard". It gathers four things in one place: the market you are trading and its
environment, an order ticket, a five-level order book snapshot, and a set of tabs
for your balances, positions, and working orders. This chapter assumes you already
know what a market versus a limit order is, and what a stop-loss and a take-profit
are for; it concentrates on how you express and hear those decisions here, and —
just as importantly — on what this terminal does and does not do on your behalf.

### Paper or live — check this first

Before you place anything, know which account you are about to hit. The terminal
does not have a separate "paper trading mode"; instead, your environment follows
whichever API key profile is active. A profile marked Paper routes to the
provider's simulated account; a profile marked Live trades real, funded money. The
dashboard shows the current environment in its market panel as "Paper" or "Live",
and when you are in a live profile it puts an unmissable red banner across the top
of the trading controls reading "⚠ LIVE TRADING — Real funds at risk." that stays
on screen the whole time.

You can change accounts without leaving the dashboard: the "Switch API Key"
dropdown lists your profiles as "{name} ({environment})", and selecting one
announces "Switched to API key {name} ({environment})" and reloads that account's
balances and positions. Build the habit of confirming this out loud with the
dropdown before a session — it is the single most consequential setting on the
screen, and there is no second "are you sure?" prompt later to catch a mistake.

### Placing an order

The order ticket lives in the dashboard's "Place Order" panel and reads top to
bottom. You choose a side with the "BUY" and "SELL" buttons — they are a toggle, so
exactly one is active — then enter a "Quantity", and pick a "Type" of either Market
or Limit. Choosing Limit reveals a "Limit Price" field; a market order omits it and
fills at the prevailing price. If your provider and market support margin, two more
controls appear: a "Margin" choice of Cross or Isolated, and a "Leverage"
multiplier. You move through all of these with Tab, and your screen reader reads
each label and value as you land on it.

When the ticket is complete you activate the submit button, which is labelled for
the side you chose — "Submit Buy Order" or "Submit Sell Order". The button stays
disabled until the quantity is above zero and a symbol is loaded, so if you cannot
activate it, check those first. There is no confirmation dialog: pressing the button
sends the order. The terminal acknowledges that it is working ("Submitting…") and
then goes quiet on the ticket itself — because the meaningful news, the fill, comes
back through the announcement channel described below rather than as a form message.
Behind the scenes the terminal also guards against a double-tap: an identical order
re-submitted within thirty seconds is ignored, so a stray second press will not
accidentally double your size.

### Attaching a stop-loss and a take-profit

You protect a trade at the moment you enter it. The ticket has an optional "Stop
Loss" field and an optional "Take Profit" field — both shown with a placeholder of
"Optional" — and both are entered as **absolute price levels**, not distances,
ticks, or percentages. If you are buying BTC at 42,500 and want to be out if it
breaks 42,180 or to bank profit at 43,400, you type those two prices directly into
the fields before submitting. Leave either blank to skip it.

Two things about this are important to understand, because they shape how you work.
First, the entry and its protective orders are **not placed as a single guaranteed
bracket**. The terminal submits the entry, then submits the stop and target, and
then — about two seconds later — scans the exchange to confirm protection actually
landed. If it cannot find a protective order, it warns you, interrupting: "Warning:
no stop loss or take profit found on the exchange for {symbol}. The position may be
unprotected — verify your open orders." Treat that sentence as a call to action: open
the Orders tab and check, because you may be holding an unguarded position. Second,
there is no inline editor for the protective levels of a resting order. To move a
stop you cancel the working order from the Orders tab and place a new one at the new
price. So set your stop and target deliberately at entry rather than planning to
nudge them afterward.

A note on trailing exits, since the concept is common: the stop and take-profit you
attach to a manual order today are static price levels. Trailing stops and trailing
take-profits are planned for the live ticket but are not yet available there. For
now, trailing logic lives inside automated strategies (covered in the Automation
chapter), where a strategy can trail a stop by an ATR multiple; for a manual trade,
treat "stop" and "take-profit" as fixed levels you set and manage yourself until the
trailing variants arrive.

### Hearing your fills

Order outcomes are the one kind of feedback the terminal will never let you miss.
Whatever else is happening — sonification muted, a playback running, speech in the
middle of another sentence — an order event plays a short earcon immediately and
then speaks over whatever was being said. The announcements are plain and
quantity-and-price first:

- A complete fill: "Order filled. Bought {qty} {symbol} at {price}." (or "Sold …").
- A partial fill: "Partial fill. Bought {qty} {symbol} at {price}. {remaining}
  remaining."
- Your protective stop triggering: "Stop loss hit. Sold {qty} {symbol} at {price}."
- Your target triggering: "Take profit hit. Sold {qty} {symbol} at {price}."
- A refusal: "Order rejected for {symbol}."

Order IDs are deliberately never spoken — they are long, meaningless to the ear, and
would only clutter the message. Every one of these events is also written to the
Journal (Ctrl+Alt+Shift+J), so if a fill announcement goes by while you are
concentrating elsewhere, you can read it back afterward with its full detail.

### Positions, balances, and working orders

The lower part of the dashboard carries three tabs — "Balances", "Positions", and
"Orders" — that you move between to read account state. The Positions tab lists each
open position by symbol, its quantity, and its unrealized profit or loss, the last
read back so you can hear at once whether you are green or red. The Orders tab lists
your working (not-yet-filled) orders with their side and price and a Cancel button
on each, which is how you pull a resting limit or a protective stop you no longer
want.

There is no one-button "close position" or "flatten" here. To exit, you place an
opposing order: a Sell to close a long, a Buy to close a short, sized to how much
you want to reduce. So closing a 0.5 BTC long means submitting a 0.5 BTC market sell
on the same symbol. Keep that in mind when you are managing risk under pressure —
exiting is a deliberate order, not a single keystroke.

### Reading the order book

Press Alt+B to open the order book for the current symbol; your screen reader
announces it as "Order Book — {symbol}". It presents the resting buy and sell
interest as two columns — "Bids (Buy Orders)" on one side and "Asks (Sell Orders)"
on the other — each listing up to twenty price levels with their size and a running
cumulative total. A summary line across the top gives you the best bid, the best
ask, and the spread as both an absolute number and a percentage, for example a
spread of "1.50 (0.004%)".

You read the book by Tab: every price level is focusable, and landing on one has your
screen reader announce it as "Bid {price}, size {quantity}" or "Ask {price}, size
{quantity}", so you can walk down the bids to feel where the resting demand thins
out, or up the asks to find a wall of supply. If your provider streams the book, it
updates live as you read; if it only offers snapshots, a "Refresh" button appears so
you can pull a fresh picture on demand. The book updates quietly — it does not
announce every change — so it is a place you go to read depth deliberately, not a
running commentary.

### A trade from start to finish

Putting it together: you are watching BTC/USDT on Binance and decide to buy a
pullback. You press Alt+T, hear the environment is Paper, and Tab through the ticket
— BUY, quantity 0.5, type Market, stop loss 42,180, take profit 43,400 — then
activate "Submit Buy Order". You hear "Submitting…", and a moment later the earcon
and "Order filled. Bought 0.5 BTC/USDT at 42,500." Two seconds on, no warning means
your protection registered; you confirm it anyway on the Orders tab, where the
resting stop and target appear with Cancel buttons. From here you let it run: if
price falls to your stop you will hear "Stop loss hit. Sold 0.5 BTC/USDT at
42,180.", and if it rallies you will hear "Take profit hit." instead — and either
way the Journal holds the record for your review. When you want out early, you do
not look for a close button; you place a 0.5 market sell and let the fill
announcement confirm you are flat.
