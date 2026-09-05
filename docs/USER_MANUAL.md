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

## Contents

1. [Getting Oriented](#getting-oriented) — what it is, the Hybrid Voice model, the soundscape
2. [Loading a Market](#loading-a-market) — API keys, the market/provider/symbol/timeframe cascade, live vs. historical
3. [Market Watch and Screening](#market-watch-and-screening) — watchlists, the screen builder, running a screen
4. [Reading the Chart](#reading-the-chart) — navigation, scanning for events, playback, bar replay, point analysis
5. [Analysis Tools](#analysis-tools) — indicators, market structure, chart formations, value zones, the respect report, the asset dossier, quick trade, drawing tools, volume profile, heatmap, the object tree
6. [AI, Narration, and the Journal](#ai-narration-and-the-journal) — the AI analyst, auto-narration, the session record
7. [Trading](#trading) — paper mode, order types, protective and trailing exits, the live review, fills, positions, the order book
8. [Automation](#automation) — alerts, strategies, background monitoring, custom scripts, the Strategy Lab
9. [Customizing](#customizing) — settings, the sound designer, tabs and workspaces
10. [The Tactile Display](#the-tactile-display) — the Dot Pad, enabling braille output, reading the chart by touch
11. [Platform Support](#platform-support) — per-OS notes, which version to use, the web-host modifier remap
12. [Glossary](#glossary)

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
where a value sits relative to the structure that matters. Every voice is built on a
warm sine base with a slight coloring blended in, and the coloring carries the
meaning: an oscillator takes on a brighter square tinge above its midline and a
softer triangle warmth below it, so you can hear which side you are on without
checking a number; a candle's body adds a deep sub-octave weight that grows with the
body's size — a big conviction candle sounds *heavier* than a doji at the same
loudness, so size reads as character rather than volume — and wicks ring as clean
pings whose grit grows with their length, the upper wick a bright tone and the lower
a deep one, each roughening in proportion to its own reach so a candle with a long
tail below and none above is unmistakable by ear. Meanwhile, and when a reading pushes into an
overbought or oversold extreme — on a bounded oscillator such as RSI, Stochastics,
MFI, CCI, Williams %R, or the Ultimate Oscillator — a noise texture roughens the
tone so the extremity itself is audible. It is a pronounced roughness, not a faint
wash, and you can set how strong it is per level with the **Zone Texture** slider in
an indicator's properties (P), under Reference Levels. You learn to hear "deep
oversold" the way a sighted trader sees a line pinned to the bottom of its range.

On top of the continuous tones, discrete events ring as short bells, and each event
type has its own bell so you can tell them apart by ear: a smooth sine bell for
crossovers, a bright metallic triangle bell for divergences, a high pure crystal
bell for support and resistance levels, a shimmering detuned pair for
high-confluence signals, and a rich multi-harmonic blend for momentum signals. When
a bell catches your attention during playback you can stop and read the exact event
through your screen reader.

Reference levels themselves (the 30/70 lines on an RSI, a zero line, your own levels)
speak up in three stages as a value interacts with them. Approaching a level —
within about five percent, not yet across — fires one very short, very high ping
(1,400 hertz), louder the closer the value sits to the line; it is a "heads up,
almost there" cue and fires once per approach. Crossing the level rings a two-note
chirp: rising pitch for an upward cross, falling for a downward one. And when a
value has *stayed* beyond the level for several bars, a single low, longer tone
(220 hertz) confirms "still out there". These are navigation cues, not trade
signals — they are not written to the Journal, and none of them changes with your
strategy setup. If you hear one lone high beep while arrowing across an RSI, that is
the approach ping.

**Adding a level of your own.** Press `0` with a series focused and the terminal drops a reference
line on it. What it does depends on which pane you are on, because a level only means something in
the units of the thing it sits on. On an oscillator — MACD, a momentum reading, anything that swings
around a centre — it goes at **zero**, which is the line that matters there. On the price chart there
is no meaningful zero, so it goes at **the price of the bar under your cursor**: arrow to the level
you care about, press `0`, and you have marked it. You will hear which one you got — *"Zero line
added"* or *"Level added at 63,920.11"*. From then on that line speaks and pings like any other, so
you can hear price approach and cross the level you chose — from either direction, straight away,
without visiting any settings.

**Removing one.** Press `0` again on the same bar and the level comes off; you will hear "Level
removed". Levels an indicator declared for itself — an RSI's 30 and 70 lines — are never removed this
way, because they are part of what the indicator is; those live in Properties. Properties also gives
every level a Remove button, a colour, a line style, a thickness, and a choice of which crossings to
report: either direction, only rising through, or only falling through. All of it is saved with the
workspace and survives a restart, and "Reset to defaults" restores the indicator's own levels while
leaving yours alone.

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
Binance, Coinbase, Gemini, Kraken, Kraken Futures, Oanda, Polygon, Schwab and the
rest, or "Custom" — note that **Kraken Futures is its own venue, not a setting on
Kraken**: it has a different host, different request signing, and API keys you mint
separately, so it needs its own profile), give
the profile a **Profile Name** you will recognise later — the placeholder suggests
something like "Alpaca Paper" — and set the **Environment** to either Paper or
Live. Paper points the provider at its simulated/sandbox endpoints; Live uses your
real, funded account. Below that you set the **Market Type** (Spot, Futures, Crypto
or Stocks) and then enter the **API Key**, the **API Secret**, and, only if your
provider issues one, a **Passphrase** (it is labelled Optional and you can leave it
blank for providers that do not use one). All three secret fields are masked.

Everything the profile stores — the secrets *and* the profile list itself (which
providers you use, the nicknames, the environments) — is held in your operating
system's encrypted credential storage, never in a plain-text file on disk. Older
versions kept the profile list (though never the secrets) in a plain file; the first
run of a current build migrates it into encrypted storage automatically and removes
the old file.

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

### The toolbar, in two rows

The toolbar has two rows and they divide by purpose, which is worth learning once
because it tells you where to look for anything.

The **first row opens things.** Left to right it holds the object tree, drawing
tools and the sound designer; then the trading dashboard, order book, strategies,
**Watch** (watchlists and the screener), **Levels** (the respect report),
**Journal** and **AI**; then alerts and API keys; then save and load workspace;
and finally settings and help. Every one of these opens a dialog, and every one
has a keyboard shortcut named in its tooltip, so the toolbar is how you *find* a
feature and the shortcut is how you reach it once you know it is there.

While a dialog is open, the rest of the terminal is switched off — not merely covered
over. The toolbar, the tab bar, the chart, the indicator bar, the status bar and the
footer stop taking focus and disappear from your screen reader's view of the page
entirely, so Tab cannot walk out of the dialog and a stray review command cannot land
you on a toolbar button behind it. Escape brings all of it back, and focus with it: the
chart if that was the last dialog, or the dialog underneath if you had two open. The
one thing that keeps speaking is the terminal's own announcements — those live outside
the switched-off region on purpose.

The **second row builds and changes the chart**: the market cascade described
below, then import and load, then pan and zoom, then the display toggles —
heatmap, Heikin Ashi, log scale, **Split** view and **Replay**. The last five are
pressed-state toggles, so your screen reader announces whether each is currently
on.

Buttons are labelled with an abbreviation on screen and a full name for your
screen reader, so "Watch" reads as "Watch lists and screener" and "AI" reads as
"AI Analyst". Some buttons only appear when they apply — drawing tools and the
chart toggles are hidden on analytics charts, where they mean nothing.

### Choosing what to chart

Everything you select to build a chart lives on the toolbar's second row, in the
order the terminal needs it: **Market**, **Provider**, an optional **Type**,
**Symbol**, and **Time**. There is no dedicated shortcut for these fields; you Tab
into the toolbar and Tab through them left to right, and your screen reader reads
each control's label and current value as you land on it.

These four selectors form a cascade — each choice decides what the next one can
offer. Choosing the **Market** (for example Crypto, Stock, or Forex) refills the
**Provider** list with the sources that cover that market and automatically selects
the first of them. Choosing a **Provider** refills the **Symbol** list and the
available timeframes and, again, selects the first symbol for you.

The Market list also holds one special entry, **Analytics**, which is where you chart
data feeds rather than tradeable instruments. There is no longer a separate
Trading/Analytics switch — picking **Analytics** in the Market dropdown is how you
cross over. When you do, an extra **Analytics type** selector appears right after
Market offering Economic, OnChain, Derivatives, and Sentiment; choose one and the
Provider and Symbol lists refill with that category's sources (FRED economic series,
on-chain metrics, funding/open-interest, Fear & Greed, weekly CFTC fund positioning,
daily short-sale volume for any US stock, and so on). For everything else
— actual markets you can trade — you simply never touch the Analytics entry. One consequence worth understanding: moving through this cascade does not
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
The quick-picks are always the provider's *own* list — a button you can press is a
timeframe that provider genuinely serves. Two consequences follow. When a provider
offers exactly one timeframe (most analytics feeds — Fear and Greed, COT
positioning, short volume — are daily-only), the whole Time area disappears:
there is nothing to choose, and the timeframe still reads in the tab title and
the Shift+F1 context summary. And when you switch to a provider that doesn't offer your
current timeframe, the terminal snaps to one it does offer and tells you so —
"provides 1d data only; timeframe set to 1d" — instead of silently fetching
nothing.

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
press Shift+F1 and the terminal announces the current symbol, provider, and timeframe.
This is handy after you have been deep in navigation for a while and want to
reconfirm the instrument before acting on it.

Once you are used to the toolbar you rarely need to Tab back to it. `Ctrl+Alt+Shift+L`
loads the chart from wherever you are — it does exactly what activating the **Load**
button does. If you have not yet chosen enough to load anything it says so rather than
doing nothing quietly: *"Cannot load yet. Choose a market, provider, and symbol first."*
That makes it safe to reach for out of habit; the worst case is a sentence telling you
what is still missing.

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
heads-up: if you load an analytics provider — chosen through the **Analytics** entry
in the Market dropdown, one that returns single scalar metrics like an economic
series rather than OHLCV candles — onto a tab that already
holds indicators or drawings, those tools cannot apply to a non-candle series, so
the terminal stops to confirm with a "Switching to analytics" dialog. It offers
three choices: "Continue (strip & load)" replaces the chart and removes the tools
that no longer fit, "Open in New Tab" loads the analytics series beside your
existing work and leaves it untouched, and "Cancel" backs out. When in doubt, open
it in a new tab so you keep both views.

With a chart loaded and confirmed live, you are ready to read the market itself —
which is where the next chapter, on moving through time bar by bar, begins.

---

### Your own data: the My Data market

The Market dropdown has one entry that isn't a market at all: **My Data** —
your own CSV files, charted with everything the terminal knows how to do.
Select it and the Symbol list shows what you've imported; an **Import data
file** button appears beside the cascade (or press Ctrl+Alt+Shift+I from
anywhere). The import dialog takes a file or — often easier with a screen
reader — pasted CSV text, and it tells you exactly what it understood before
anything loads: "Imported Household Budget: value series, 240 rows, January
2005 to June 2026."

Three file shapes are recognized from the header row. A file with
`date,open,high,low,close` (volume optional) charts as full candles — playback,
pattern announcements, even backtesting your own history. A file with a date
column plus any named number columns — `date,Income,Expenses,Net` — makes each
column its own loadable chart ("Budget — Income", "Budget — Expenses"). And a
file shaped `date,label,value` becomes **event markers**: add "My Events:
your-dataset" from the indicator dialog (Alt+A, category My Data) and each
event lands on the bar covering its date, spoken with its own label — put your
real fills on the chart where they happened and hear "Bought 0.5 BTC, 42,000"
as you arrow past. Dates read best as 2026-07-22; common spreadsheet formats
and unix timestamps also work, and the dialog has copyable templates for all
three shapes. Excel and LibreOffice users: File, Save As, CSV. If a file can't
be read safely the import is refused with the line number and reason — a
refused import beats a silently wrong chart.

Imported data doesn't have to live on its own chart. The indicator dialog's
**My Data** category puts any dataset ON the chart you're viewing, aligned to
its bars (sparser data holds its value between points, like the weekly COT
series does on a daily chart). "My Data: X" opens the dataset in its own pane
with every column navigable, and a Normalize-to-100 setting makes
different-sized columns comparable by shape. "My Data overlay: X" draws it on
the price pane itself, rebased so it starts where the chart was — from there
the two lines diverge by relative performance, in pitch as well as pixels.
For imported OHLCV data, "My Data ratio: X" adds the classic strength read:
the chart's close divided by yours, rising when the loaded symbol outperforms.

The same comparison works against any exchange symbol without importing
anything: the indicator dialog's **Overlays** category has "Compare symbol
(overlay)" and "Compare symbol (ratio)". Type the symbol (and optionally a
different provider — it defaults to the chart's own), and the second market
appears rebased on the price pane or as a strength ratio in its own pane,
always on the chart's timeframe so the bars line up one to one.

## Market Watch and Screening

Loading one symbol at a time answers "what is this market doing?". Market watch
answers the question before it: "which market should I be looking at?" It holds
your watchlists, and it runs a screen — the same conditions a strategy uses for
entry, asked across every symbol on a list at one instant instead of across
every bar of one symbol.

Open it with **Alt+M**, or with the **Watch** button on the toolbar's first row.
The dialog has three tabs — Watchlists, Build a screen, and Run screener — and
you move between them with Tab and Space like any other tab list.

### Watchlists

A watchlist is a named, ordered set of symbols, each remembering which provider
and sub-type it came from. Order is yours: nothing re-sorts behind your back,
and Up and Down on each row move a symbol explicitly.

Create one by typing a name into **New list name** and pressing **Create list**.
The **Add a symbol** area below is a cascade, exactly like the toolbar's:
Market, then Provider, then Sub-type, then Symbol. Choosing a market narrows
the providers; choosing a provider loads its real symbol list, so you pick from
what actually exists rather than typing a symbol name and hoping. When the
dialog opens, all four are pre-filled from the chart you were looking at, which
makes "add this to a list" a single press of **Add**.

Providers can list thousands of symbols, so there is a **Filter symbols** box
beside the picker. Type any fragment — `usdt`, `EUR`, `SOL` — and the list
narrows as you type; a live message under the picker reads out how many symbols
are showing and how many exist in total. That message also tells you when the
list is capped at 500 and you need to narrow further, so a truncated list never
poses as a complete one. **Add all shown** adds everything the filter is
currently showing, and announces both how many were added and how many were
already on the list.

Every row on the list has **Load**, which puts that symbol on the chart through
exactly the same path the toolbar's Load button uses, then closes the dialog.

### Building a screen

A screen is a set of filters plus a rule for combining them. On the **Build a
screen** tab, press **Add filter** and you get one row with four or five
controls:

- **Indicator** — which indicator the condition reads. Split from the component
  deliberately: one flat list of every signal in the app runs to several hundred
  entries and is miserable to move through by ear.
- **Component** — which of that indicator's outputs, for example Cipher B's Buy
  signal or RSI's Value line.
- **Condition** — what has to be true. The choices offered depend on what kind
  of component you picked, and that gating matters: a marker component has no
  value on bars where it did not fire, so "is above 30" would be false forever
  and would read to you as a quiet market rather than as a nonsense filter. A
  marker offers *fired on the last bar* and *fired within N bars*; an oscillator
  offers thresholds, ranges, crosses, and percentiles; a cloud offers
  inside/above/below; a level offers rejection and break.
- **Value**, **Upper**, **Within bars**, **Weight** — only the boxes the chosen
  condition actually uses appear, and a fresh filter is seeded with a threshold
  somewhere sensible inside the component's own range rather than at zero.

Under each row, a plain-language sentence restates the whole filter — "Cipher B
— Buy fired within 3 bars." — so you can check a row in one read instead of
tabbing back through five controls.

**Combine filters** decides the logic. *All must be true* is a strict AND. *Any
may be true* is an OR. *Weighted score reaches a threshold* gives every filter a
Weight box and matches when the weights of the filters that are true add up to
the threshold you set — useful when you want confluence ("any three of these
five") rather than unanimity.

**Bars of history per symbol** is the one setting that quietly breaks screens if
you get it wrong. Every indicator needs a warm-up before it produces values, and
if you fetch fewer bars than the slowest indicator you referenced needs, every
symbol comes back as "not enough history". The default of 500 covers everything
that ships; raise it if you build a filter on a long moving average.

**Save screen** stores it under the name you gave it, and selects it in the Run
screener tab so you can run it immediately. Editing an existing screen is the
same dialog — pick it from **Edit saved screen**, change what you like, save
again. If a screen was built elsewhere and contains nested groups, the flat
builder says so rather than showing you a plausible-looking subset, because
saving a flattened copy over it would destroy structure you cannot get back.

### Running a screen

The **Run screener** tab takes three things: which saved screen (or none, which
matches everything and gives you a plain quote board), which watchlist to run it
against, and on what timeframe. Press **Run screen**.

Progress is spoken as it goes — how many symbols are done, and which one is in
flight — and **Cancel** stops it. Results arrive as a real table with proper
column and row headers, so your screen reader reads the column name with each
value as you move cell by cell. Each row carries the symbol, provider, whether
it matched, the score if the screen used weights, last close, percentage change,
a status, and a **Load** button that puts it on the chart.

Two things about the results are deliberate. First, symbols that could not be
evaluated are reported, never dropped: a row reading "not enough history" or a
fetch error stays visible even with **Show matches only** ticked, because "we
could not check twelve of these" must not be able to look like "nothing
qualified". Second, the spoken summary after every run names all three numbers —
matched, evaluated, and failed — for the same reason.

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

**A note on the picture during fast markets.** The chart image repaints about ten times a
second. Until August 2026 the rate limit was the wrong kind: it waited for a *pause* in the
data rather than repainting on a timer, so while ticks kept arriving faster than that pause
— which is exactly what a busy market is — the image stopped updating altogether and only
caught up once things went quiet. Sighted collaborators looking over your shoulder would
have seen a frozen chart at the one moment it mattered. Everything you hear and everything
you navigate came from the live data all along and was never affected; only the drawn PNG
was stale.

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
the viewport, with `[` bringing older bars into view and `]` newer ones. `Shift+[`
makes each of those presses move a smaller distance and `Shift+]` a larger one, which
matters more than it sounds: on a daily chart you usually want to step a week at a
time, and on a one-minute chart a week is thousands of bars away. Set the step to suit
the timeframe once and every later `[` or `]` lands where you expected. The minus key
zooms out to see more bars at once, and the equals key zooms in for finer detail on
fewer.

If you prefer the mouse, the chart toolbar carries **Pan left**, **Pan right**, **Zoom
in**, and **Zoom out** buttons that do exactly the same things and speak the new visible
range just as the keys do, so they are safe to use with a screen reader. You can also
**click and drag the chart itself** to pan — provided no drawing tool is selected —
dragging right to reveal older bars; letting go of the button anywhere stops the pan.

### Using the mouse on the chart

Every mouse action on the chart lands in the same place the keyboard navigates, so the
mouse and the keyboard always agree about where you are — and everything a mouse action
does is spoken through the same pipeline the arrow keys use. That makes the mouse fully
usable alongside a screen reader, and it makes the chart approachable for a sighted or
low-vision trader sharing the terminal with you.

**Click a bar to hear it.** A single click on the chart moves the reading cursor to the
bar under the pointer and announces it exactly as if you had arrowed there — values
spoken, tone played. Because the click really moves the cursor, you can click roughly
in the area you care about and then fine-tune with Left and Right arrows; pointing
precision is never required. Clicks in the empty space to the right of the newest bar
do nothing.

**Click near an indicator to focus it.** If your click lands close to an indicator
line — an EMA, a MACD line, a band — keyboard focus also moves to that series and
component before the bar is announced, so what you hear is the thing you pointed at.
A click that isn't near anything specific simply selects the bar on whatever was
already focused, so imprecise pointing still works.

**Shift+click to measure.** Hold Shift and click a bar, and the terminal speaks a
range summary from the reading cursor to the clicked bar — how many bars, the dates,
the high and low of the span, and the net change in price and percent. Measuring
never moves your cursor, so you never lose your place.

**Magnet snap for drawings.** Off by default; toggle it from the chart's right-click
menu. With magnet on, drawing anchors pull to the nearest open, high, low, or close
of the bar under the pointer when you get close — trend lines land exactly on the
wick or the close without pixel-perfect aim.

**Hover sound.** Also off by default, also in the right-click menu: a soft, short
tick as the pointer crosses each bar, pitched to that bar's closing price — sweep the
mouse across the chart and it hums the price contour, without touching your reading
cursor or interrupting speech.

**Scroll wheel.** Scrolling zooms in and out, keeping the bar under the pointer fixed
in place. **Shift+scroll pans through time instead** — down or right for newer bars, up
or left for older — using the same pan step as the bracket keys, with no button-holding
or dragging needed, which makes it the easiest way to move through history if clicking
and dragging is difficult for you. A sideways swipe on a trackpad pans the same way.

**Double-click to jump to now.** Double-clicking anywhere on the chart jumps straight
to the newest, live bar and announces it — the mouse twin of the Backslash key.

**The crosshair.** As the pointer moves over the chart, a crosshair follows it: a
vertical line snapped to the bar under the pointer, a horizontal line at the pointer's
price, and a readout in the top corner showing the bar's date, the price at the pointer,
and the bar's open/high/low/close. The readout is real text on the page — not part of
the chart image — so screen magnifiers, browser zoom, and custom styles all work on it.
It never speaks (that would be constant chatter as the mouse moves); the spoken
equivalent is simply clicking the bar. You can turn the crosshair off and on from the
chart's right-click menu.

**The right-click menu.** Right-click on open chart space (or press the Application
key — or Shift+F10 — with the chart focused) for the chart menu: **Play from here**
starts playback at the bar you clicked; **Jump to latest** returns to the live edge;
**Show/Hide crosshair**, **Magnet snap**, and **Hover sound** toggles; and beneath
those, every series on the chart is listed **by name**. Better still: right-click
*near an indicator line* and the menu opens directly on that series' actions — no
hunting the list — with Back one press away. Choose a series to get its actions — Focus, Mute or
Unmute, Hide or Show, Properties, and Remove. Listing series as menu items is
deliberate: acting on an indicator never requires clicking on a thin line, which
matters as much for shaky hands and low vision as it does for screen reader users.
Right-clicking directly on a drawing's anchor handle still opens the drawing's own
menu (Delete, Duplicate, Properties), as before.

### Touch and mobile

On a touchscreen — a phone or tablet browsing accessibletrader.com, or a touch-screen
laptop — the chart understands the gestures you would expect, and each one lands in the
same place the keyboard navigates, announced through the same speech pipeline:

- **Tap a bar** to move the reading cursor there and hear it, exactly like clicking.
- **Drag with one finger** to pan through time, like grabbing the chart.
- **Pinch** to zoom in and out, anchored between your fingers.
- **Double-tap** to jump to the newest, live bar.
- **Press and hold** for about half a second to open the chart menu (or a drawing's
  menu if your finger is on its anchor handle).

On touch devices a **navigation toolbar** also appears below the chart with large,
plainly-labelled buttons — Previous bar, Next bar, Previous component, Next component,
Previous series, Next series, Place drawing point, Play, and Chart menu. If a gesture
ever misbehaves, the buttons always work, and they are the most reliable way to drive
the chart with VoiceOver or TalkBack running (swipe to the button, double-tap to
press). Previous/Next series are the touch equivalents of Page Up/Down, so you can move
between loaded series without a keyboard. **Place drawing point** is what makes drawing
tools usable by touch alone: arm a tool from the chart menu's Drawing Tools, move the
cursor with the bar buttons or a tap, then press Place drawing point to drop each
anchor — no keyboard shortcut needed. It speaks a hint if no tool is armed.

**With a mobile screen reader**, the screen reader owns the touchscreen, so the chart
also offers a **bar navigator** — announced as "Bar navigator" just before the chart.
It behaves like a slider: focus it and flick up or down (VoiceOver) or swipe up/down or
use the volume keys (TalkBack) to step through bars, each one spoken with its position,
date, and closing price, with the full announcement and tone following from the app's
own speech. One honest caveat: **iOS VoiceOver moves web sliders in steps of about 10%
of the chart** rather than one bar at a time — flick to get close, then use the
toolbar's Previous/Next bar buttons for single steps. TalkBack on Android steps one bar
at a time. Finer VoiceOver control arrives with a future native iOS update.

Touch in the **installed mobile apps** flows through the same layer and is expected to
behave the same way, but it has not yet been verified on physical devices — until it
is, a connected Bluetooth keyboard remains the fully-supported way to drive the mobile
apps. Pinch-zooming the page itself is no longer blocked anywhere, so browser-level
magnification works on the website.


**Explore mode** turns the touchscreen into a reading surface. The touch
toolbar's Explore button (announced with its state) switches a single
finger's drag from panning to exploring: slide across the chart and each bar
under your finger speaks — value first, then the date — with a pitch tick
that traces the price contour, and the crosshair follows for sighted
partners. Lift to stop; touch again anywhere to keep reading; pinch still
zooms mid-explore. Screen reader users: activate the Explore button through
the toolbar as usual, then use your screen reader's pass-through gesture on
the chart surface to hand it your finger. Turning Explore off restores
drag-to-pan, and the terminal says so.

### Moving between panes and components

Page Down moves your focus to the next pane below, Page Up to the pane above; as you
arrive, speech announces the newly focused series by name — "RSI", "Volume". Within
a series, the Up and Down arrows step through its components: Down from the MACD line
to the signal line to the histogram, Up back through them, each announced with its
name and current value.

**A pane is a Y axis.** That is the whole definition, and it is what makes the structure
worth navigating. Candles and a price overlay share the Main pane because they share a
price scale; volume gets a pane of its own because a volume axis is not a price axis;
Cipher B gets a third. Alt+PageUp and Alt+PageDown move between them — the next Y axis
up or down the chart — and the pane's name is spoken at the end of the move, so you hear
what you landed on after you hear what it says.

Some panes hold several things drawn against the same axis, and some divide into strips:
a Cipher B pane holds a money-flow histogram, two wave lines and cross dots, with the
money-flow histogram in a strip of its own at the bottom. Ctrl+Up and Ctrl+Down walk the
strip you are in, **across every series in the pane** — so from the candles they reach a
price overlay drawn on top of them, which is the pair you most often want to compare and
the one move the terminal could not previously make. The plain arrows still walk every
component of the focused series in order.

Alt+Shift+/ answers the question those moves raise: it describes the pane you are in —
what each axis measures, the range each covers, the step between gridlines, and what is
drawn in it. A value means nothing without the scale it sits against, and that scale is
something a sighted trader reads off an axis without thinking about it.

> Ctrl+PageUp and Ctrl+PageDown are left unbound on every head. Browsers use them to
> cycle their own tabs, ahead of anything the page can do about it, so pane navigation
> lives on Alt+PageUp / Alt+PageDown everywhere and the desktop and the browser agree.
> F1 always shows the bindings actually in effect on the host you are using.

Three keys re-orient you whenever you lose the thread. Shift+F1 announces the current
symbol, provider, and timeframe. Ctrl+Alt+Shift+C focuses the chart and reads a fuller
context summary. And Ctrl+Alt+Shift+Y describes the chart's **layout** rather than its
values — the axes and their scales, how many panes are open and what is in each, how
many series and components they hold, and what is currently hidden or muted. It is the
answer to "what am I looking at?", which is the question a sighted trader settles with
one glance and every other spoken message quietly assumes you have already answered.
Reach for all three freely — there is no penalty for asking the terminal where you are.

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
hear. Space plays or stops the whole chart — every visible, unmuted series at once,
each sounding all of its own visible, unmuted components together, so a busy chart
really does play as a full ensemble, down to the soft cloud- and ribbon-fill washes
(an EMA Fill between two averages, say) that used to drop out when too many voices
were in play. Shift with Space plays just the series you have focused, all of its
components, for studying one indicator without the rest. Ctrl+Shift+Space narrows
further to a single component — the RSI line alone, say. Muting a series or component
with M, or hiding it with H, drops it straight out of the mix, which is how you thin
a crowded soundscape down to what matters.

Those two single-key toggles are fast on purpose, and that makes them easy to
over-use: hide four components across three indicators and there is no practical way
to walk back and find them, because a hidden component is exactly the thing you can no
longer navigate to. So there are two undo-alls. **Ctrl+Alt+Shift+K brings back
everything you have hidden** and **Ctrl+Alt+Shift+U unmutes everything you have
muted**, each announcing how many it restored — *"3 items shown."*, or *"Nothing was
hidden."* if there was nothing to bring back. Narration has the same escape hatch:
**Ctrl+Alt+Shift+O switches narration off on every series and clears every component you
picked out with N**, so a selection built up across three indicators over an afternoon is
one chord from a known state — *"Narration off for 2 series."*, or *"Nothing was
narrating."* Treat all three as
the way out whenever the chart has gone quieter than you meant it to, and
Ctrl+Alt+Shift+Y (above) as the way to hear what is currently hidden or muted before
you decide. Ctrl+Space pauses and resumes whatever is
playing — and while paused it now falls properly silent instead of holding the last
chord, though the arrow keys still audition individual bars so you can inspect the
frozen moment. Shift+Escape is the panic key that stops all playback at once, and
Shift+= speeds playback up while Shift+- slows it down — slower to
dwell on each bar, faster to scan a long history.

**What playback says while it runs.** The tones carry the price, so the words carry
everything the tones cannot. Three things speak, all of them without interrupting each other
and all of them composed into a single sentence per bar so nothing gets cut off half way:

- **Where you are in time.** Each time the bars cross a calendar boundary one step coarser
  than the bar spacing — a new hour on minute bars, a new month on daily ones — the new
  period is spoken: *"February 2024."* The unit is chosen so the announcements land at least
  two seconds apart at whatever speed you are playing, so speeding up makes them coarser
  rather than more frequent.
- **Signals, on the series you asked for.** A marker signal printing on the bar the tones
  have just reached is spoken — *"Triple confluence buy, strong confirmation."* This is the same
  flag as live auto-narration, and there are three questions behind what you hear: **N picks what
  MAY speak, the Narration tab picks WHEN, and the scope you played picks WHICH of them.** Press
  Space and the whole chart may speak; Shift+Space on one series and only that series does.
  Only discrete signals, never crossings, zone changes or oscillator commentary — at ten bars a
  second those would be a wall of speech.
- **When several fire at once, the rarest is the one you hear.** Two clauses is the ceiling for
  one bar, and what falls off the end is the marker that fires most often on the chart in front
  of you. That matters on an indicator like Cipher B, whose gold Triple Confluence dot can only
  print on a bar that is *also* an oversold crossover and *also* a WaveTrend cross: all three
  fire together, and the one you added the indicator for is the one that prints twice in four
  hundred bars. The rare one leads the sentence too, because at ten bars a second the first
  clause is the one that lands before the next bar sounds.
- **A formation resolving.** If you have "Describe chart patterns" on, a chart formation
  whose story ends on the bar being played is spoken there, in the same words the arrow keys
  would use if you stopped on it.

Signals and formations are rate-limited to the same two-second cadence as the landmarks: a
second one arriving inside the window is **dropped, not queued**, because at ten bars a
second a queue means hearing about a bar the tones passed eight seconds ago. One thing gets
through that window anyway: a **rarer** signal than the one that opened it. A WaveTrend cross
every dozen bars would otherwise swallow the gold dot four bars behind it, which is the
opposite of what the limit is for. It cannot cascade — whatever speaks claims the window at its
own rarity, so the next thing has to be rarer still. Landmarks are
never dropped by that limit — they are the only thing telling you where in time you are. The
master switch is **Settings (F12) → Narration → Narrate during playback**: turn it off and
playback is tones and nothing else, with the start, pause, speed and finish confirmations
still spoken so the end of a run never sounds like a crash. The landmarks have a switch of
their own beneath it — **Speak time landmarks during playback**, on by default — because the
date and the signals answer different questions, and wanting to hear what your indicators
printed is not the same as wanting the calendar read to you every few seconds.

**A signal is introduced by the component that fired it, never by the series.** *"Bullish
divergence."* — and where the wording does not already say which marker it was, the component
leads: *"WaveTrend Cross Bull: Wave cross up 12."* You chose which series narrate, so the
indicator's name tells you nothing you did not know; which of its markers just printed is the
fact you are waiting for.

**What playback does NOT say, and where to find it instead.** A continuous line — a moving
average, a VWAP, an oscillator — never speaks during playback, whether or not you have flagged
it with N. It has a value on every bar, so narrating it would mean a number ten times a second
for the length of the run, and the tones already carry it. The events those overlays produce are
narrated on the **bar close** instead, where there is a whole bar interval for the words to land
in: price crossing your EMA is announced there, as is an oscillator changing zone. So the
division is not a special case for playback so much as one rule seen from two sides — *playback
speaks what happened at a point, bar-close narration speaks what changed.*

If you want playback as *pure* narration rather than as sound, F3 turns the chart tones off
and leaves everything above running: the cursor still walks the chart, the words still
arrive, and nothing plays underneath them.

### Choosing what you hear

You are always in command of the output layers, and the F-key row follows one
rule: **the plain key controls what you asked for, and Shift controls what
happens to you.** F2 toggles interactive speech — navigation values, zoom and
pan announcements, summaries; with it off, your commands run silently.
Shift+F2 toggles event speech — alerts, monitoring reports, new-bar
announcements. F3 toggles chart sonification — the navigation tones and
playback; Shift+F3 toggles earcons. Two things refuse to be silenced: errors,
and your order outcomes — fills, stop hits, take profits speak and sound
through every mute, because missing a stop firing costs real money. (If you
truly want total silence, Settings → Speech has an "Event mutes also silence
order fills and stops" switch — read its warning first.) Individual alerts can
also be marked **Break through mutes** when you create them, for the handful
that must never be missed. F4 toggles the braille display on platforms that
support one, and Shift+F4 opens its settings. None of these mutes persist —
the terminal always starts with everything audible.

One extra control exists on the public website versions (the demo and the
hosted terminal), where the last speech hop is your browser: **Speech output
on this device**. A browser cannot tell whether a screen reader is running, so
on your first visit the terminal asks — let your screen reader do the talking,
have the browser's voice read everything aloud, or both. Answering "screen
reader" is what stops the doubled voice some Chrome users hear (Orca and the
browser voice saying everything twice). The choice is remembered per browser,
and can be changed any time in Settings under Speech. Volume is adjustable at three depths: F5 and Shift+F5 raise
and lower the focused component, F6 and Shift+F6 the whole focused series, and F7 and
Shift+F7 the master chart volume — so you can bring one quiet line forward without
touching the rest. The M key mutes or unmutes the focused series or component without
removing it, and H hides or shows it; both are toggles, and both leave the data in
place so you can bring it back with the same key.

### Text labels on the chart

**Ctrl+Shift+L** pins a text label at the cursor bar. The anchor goes down first,
then a small dialog asks what the label should say — so the position and the
wording are two separate decisions, which matters when you cannot see where the
anchor landed.

The text is drawn on the chart beside the anchor, and it is read wherever you meet
the label. Arrow or jump onto the bar it is pinned to — from the label's own series
or from any other — and you hear a short high two-note tick followed by the wording.
The tick is deliberately unlike any tone the chart makes from data: a label is an
annotation, not a measurement, and it plays no price tone at all. The wording is also
the label's name, so the object tree lists it and the legend shows it. A label reading
"Label (3)" would be a label you have to go and look at, which is the one thing that
does not work here.

Off its bar, the label's own series says the wording and "not on this bar" — a label
is a single point, so most bars of its series are empty and silence there would be
indistinguishable from a series that had stopped working.

Cancelling the dialog leaves the label in place with no text, and says so — deleting
something you had just deliberately positioned would be the worse surprise. Remove
it from the object tree if you did not want it.

### Bar replay

Playback reads you a chart you can already see all of. Bar replay does the
opposite: it hides everything after a point in history and gives it back one bar
at a time, so you meet the market the way you meet it live — without knowing what
happens next. It is the honest way to practise, and it is the only way to find out
whether a setup you like the look of was actually readable at the time or only
looks obvious with the rest of the chart in view.

Start it with **Ctrl+Alt+Shift+P** (or **F11** on the desktop), or with the
**Replay** button on the toolbar's second row. Replay begins at the bar your
cursor is on, and everything after it disappears.

| Key | Action |
| --- | --- |
| Ctrl+Alt+Shift+P or F11 | Start replay at the cursor bar, or stop and restore full history |
| F9 | Reveal the next bar |
| Shift+F9 | Hide the last revealed bar |
| F10 | Play / pause auto-advance |

On the web host use `Ctrl+Alt+Shift+P` rather than `F11` — browsers own F11 for
fullscreen and will not pass it through.

While replay is running, the chart is a normal chart in every other respect.
Indicators recompute on the revealed bars only, so an oscillator reads exactly what
it would have read at that moment; you can navigate, inspect a bar, draw, and place
paper orders. Stopping replay restores the full history and the viewport you had
before you started.

The **Replay** toolbar button shows its own state, so you can tell at a glance — or
by the button's announced pressed state — whether history is currently hidden.

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
— Multi-Signal, Trend, Momentum, Cycles, Positioning, Derivatives, Volatility,
Volume, and Profiles — and you move
through the category and indicator lists with the arrow keys and add one with Enter.
A new indicator arrives with audio properties already chosen for its type, so it is
immediately playable; you can refine them later.

**Some indicator lines have new names, and five indicators have gone.** Until August 2026
a number of indicators drew nothing at all: they were listed, they could be added, and
they produced an empty line with no error — indistinguishable, by ear, from a market with
nothing to say. Two separate causes, both now fixed and both now guarded by tests.

Working again, unchanged in how you use them: **Bollinger Bands**, **Keltner Channel**,
**Chandelier Exit**, **Ultimate Oscillator** and **Momentum**. If you added any of these
before and assumed you had mis-set something, you had not.

Renamed lines, because the old names were the reason those lines were blank. The
indicator behaves the same; only what speech calls the component has changed:

| Indicator | Was | Now |
|---|---|---|
| Stochastic | PercentK / PercentD | **%K** / **%D** |
| Vortex | Vip / Vim | **VI+** / **VI−** |
| Choppiness | ChopIndex | **Choppiness** |
| Ulcer Index | UlcerIndex | **Ulcer Index** |
| ADX | Adl / Adh | **Adxr** (the smoothed ADX rating) |
| ADL | Adl3 | **ADL SMA** |
| ROC | RocP | **ROC SMA** |

**TRIX**, **ROC** and **ADL** also gained a smoothing period in their settings. Their
signal and average lines were declared but could never be filled in, because the setting
that controls them was not offered; the lines work now and default to sensible periods.

**No longer listed: PPO, ZLEMA, TMA, Historical Volatility and Ease of Movement.** The
maths library the terminal uses does not implement these, so they could never have drawn
anything. They have been withdrawn rather than left in the list — an indicator you can add
and wait for is worse than one that is honestly absent. They return if the library gains
them.

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
like.

**Positioning indicators — who actually holds what.** Two 1.6.0 additions read
official positioning data instead of price. **COT Positioning** (Positioning
category) speaks the weekly CFTC report — hedge-fund net position as a 26-week
z-score, with "crowded long" and "crowded short" bells at the ±1.5-sigma extremes —
and picks the right futures contract from whatever chart you are on (gold, silver,
copper, oil, gas, Bitcoin, Ether, the S&P, the Nasdaq, the euro, the dollar index).
Its detail facts tell you honestly where the signal has tested well (contrarian on
gold, a dip-buy gate on equity indices) and where it has not (CME crypto, FX). For
individual stocks, load the **FINRA** analytics provider and chart
`{TICKER}_SHORTVOL` — the share of each day's volume that was sold short, a daily
crowding gauge for any US equity. Both sources are free and need no key.

**A note on Cipher A.** As of 1.6.0 Cipher A is retired from the Add Indicator
dialog: its engine is the same WaveTrend as Cipher B, so it added no independent
information. Saved workspaces and strategies that use it keep working exactly as
before — it is hidden from the menu, not removed from the terminal. Tab and the arrow keys move through the dialog and your screen reader reads
every label and value.

Two whole-chart toggles live near the indicators. Alt+C switches the price pane to
Heikin-Ashi candles, a smoothed formula that strips noise and can make a trend easier
to hear; Alt+L switches to a logarithmic price scale, useful over long histories
where price has moved by large percentages.

### Market structure

**Market Structure** is on your chart by default, and it is the
answer to "where am I?" before any indicator answers "what should I do?". It
finds the swing highs and lows, labels each one against the one before it —
higher high, higher low, lower high, lower low — and reports the trend state
those labels imply.

A swing high is a bar whose high is the highest of the bars either side of it,
within the **Pivot span** (five bars each side by default), and a swing low is
the mirror. The **Minimum swing size (ATR)** setting suppresses pivots that are
too small to be structure rather than noise — a swing has to differ from the
previous one by at least one ATR by default.

The indicator marks a **red square** on each swing high and a **green square** on
each swing low — just clear of the wick, so the price they mark stays readable —
and rings a ping for each as you navigate over it, a low tone for lows and a
higher one for highs. Two further events get their own marks, both drawn as an
**X**: an **amber X** for a **Break of Structure**, when price closes beyond the
last swing in the direction the trend was already going, which is continuation;
and a larger **purple X** for a **Change of Character**, when it closes beyond
the last swing *against* the trend, which is the first mechanical evidence that
the trend may be over.

The shapes are deliberate. Market Structure owns the angular family — squares and
crosses — and Value Deviation below owns triangles, dots and diamonds, so that on
a chart carrying both you can never mistake a swing high for a resistance zone.
They shipped a week apart, were each checked alone, and for a while both drew red
down-triangles at the same size. Three more components are computed but hidden by default —
Structure State, Last Swing High and Last Swing Low — and you can switch any of
them on in the object tree or the indicator's properties.

**One thing to be clear about, because it is easy to misread.** A swing marker
is drawn ON the pivot bar, but a pivot cannot be *identified* until the span has
passed — five more bars have to fail to exceed it. So the mark appears in a place
you could not have traded: by the time it shows up, price has already moved five
bars away from it. A chart full of triangles sitting exactly on the lows is the
most seductive illusion in technical analysis. Testing this directly, filling at
the pivot price versus filling at the close of the bar where the pivot could
first be known, was the difference between an impossible return and one that did
not beat buy-and-hold. Use structure to understand where you are, not as an
entry trigger.

The indicator is descriptive by design and it says so in its own description. You
can turn it off for good in Settings if you would rather add it per chart.

### Chart formations

**Chart formations** are the multi-bar shapes a sighted trader names in one glance — double
tops, head and shoulders, triangles, wedges, flags, ranges. Delivering that by ear is the
reason this terminal exists, so the terminal describes them; it never tells you what
they mean. Turn it on in **Settings → General → Describe chart patterns**. It is off by
default, because it is extra narration on an action you perform constantly. The separate
**Describe candle patterns** switch beside it covers the one-to-three-bar shapes and is
**on** by default — it rides on announcements you already asked for rather than adding an
occasion to speak. Since 2026-09-04 it governs the arrow keys as well: turning it off leaves
the candle reading as "Bullish" or "Bearish" and the prices, which is what it said before
the multi-bar patterns arrived.

Twelve shapes are recognised: **double top** and **double bottom**, **head and shoulders**
and **inverse head and shoulders**, **ascending / descending / symmetrical triangle**,
**rising** and **falling wedge**, **bull** and **bear flag**, and the **range** (a flat
top against a flat bottom — the most common state a market is in).

#### The three things that can happen, and how you hear each one

Every formation has a **trigger level** — one price that decides its fate. On a double top
or a head and shoulders that price is the **neckline**; on a triangle or wedge it is the
boundary a break would cross first; on a flag it is the flag edge. A range is the one
exception and has two.

| What you hear | What it means |
| --- | --- |
| "**Possible** double top **forming**, neckline 42,100, measured target 39,400 if it breaks." | The shape is there, the neckline has not been touched through yet, and the outcome is still open. This is the only state you can act on in advance. |
| "Double top **confirmed** here: **closed below** the neckline at 42,100." | A bar closed through the trigger. The pattern did what it is defined to do. |
| "Double top ends here **without confirming** — the neckline at 42,100 **held**." | The shape aged out. Price came to the line and the line won. |

The wording is deliberately literal. You will never hear the word "completed", because it
could not tell you whether the pattern worked or failed — it only ever meant "price closed
through a line". So the terminal says which side of which level, and leaves the meaning to
you.

#### Where you are in the shape

A formation is a stretch of chart, not a single bar, and the announcement tells you which
edge you just crossed:

- On its **first bar**: "**Start of** possible double top forming… Spans 22 bars." The
  outcome has not happened yet, and the neckline is the number to remember.
- On the bar where it **resolved**: "**End of** double top: price closed below the
  neckline…", or when reached going forward, "Double top **confirmed here**…" / "…**ends
  here** without confirming."
- Between those points: **silence.** It has already been described and nothing has changed.

**The edge word describes the bar, not the direction you came from.** A formation's first
bar says "Start of" whether you arrowed onto it going left or right, and its last bar says
"End of" either way. That is the only arrangement in which the readout is a reliable map:
if the word changed with your direction of travel, the same bar would describe itself
differently depending on how you got there, and you could not build a picture of the chart
by moving around in it.

Press **`,`** and **`.`** to jump between formation edges — the start of each shape and the
bar its story ended. Two keys walk you through every formation on the chart in the order
they happened. They only work while formation description is on; with it off they say so
rather than moving you somewhere without explaining why.

Press **Alt+Shift+D** on any bar for the full list, including every overlapping formation
with its own levels. If nothing is live where you are standing, it tells you what finished
most recently and how — "No formation here. Most recent, 20 bars ago: double top: price
closed below the neckline at 42,100." That broken level is often still the most relevant
price on the screen.

#### Reading a break versus a hold — the part that is easy to get backwards

Take the double top, because it is the clearest case and the confusion is common.

A double top is two highs at roughly the same level with a trough between them. **The
neckline is that trough, and it is support.** So:

- **Price closes below the neckline.** The double top has *confirmed*. In the conventional
  reading this is the *bearish* case and the pattern working as advertised: the level that
  was holding price up gave way. The textbook entry is short on the break, or on a retest
  of the broken neckline from underneath; the measured target is the neckline minus the
  height from the twin tops down to it. **A break is the pattern succeeding, not failing** —
  the shape is a top, and tops are supposed to break downward.
- **Price comes to the neckline and holds.** The double top *failed to confirm*. Support was
  tested and it survived. Conventionally that is the bullish case, and the level that held
  is the reference an upside trade is built around.

Both are useful and they point opposite ways, which is exactly why the terminal reports
what price did rather than whether the pattern "worked". Every shape follows the same logic
once you know which side its trigger sits on, and the announcement always names the side:

| Formation | Trigger is | Confirmation is a close |
| --- | --- | --- |
| Double top, head and shoulders | the trough between the peaks (support) | **below** it |
| Double bottom, inverse head and shoulders | the peak between the lows (resistance) | **above** it |
| Ascending triangle, falling wedge, symmetrical triangle | the upper boundary | **above** it |
| Descending triangle, rising wedge | the lower boundary | **below** it |
| Bull flag | the flag's high | **above** it |
| Bear flag | the flag's low | **below** it |
| Range | both boundaries | **either** — whichever gives way first |

**Playing the second top.** There is a second, earlier trade hiding in the forming
announcement, and it is worth naming because it is the reason forming patterns are reported
at all. Once you hear "possible double top forming" you are being told that price has
returned to a level it was rejected from once already. Acting there — at the second top,
before the neckline is anywhere near — is the anticipation trade: a much better price, a
much tighter stop just above the twin highs, and a much lower chance of being right,
because most possible double tops never become double tops. Waiting for the neckline break
is the confirmation trade: worse price, wider stop, higher hit rate. The terminal gives you
both moments and takes no view on which one to take.

#### Ranges

A range is announced with **both** boundaries — "Possible range forming, top 110, bottom
100. Height 10." — and no target, because a range that has not broken has not chosen a
direction and projecting one would be inventing an opinion the shape does not hold.

When it goes, you hear which way: "Range breaks here: closed above the top at 110, measured
target 120." Until then the two numbers are the whole content: the conventional readings —
buy the bottom, sell the top, or wait for the break — all need the same two prices, and the
terminal gives them to you without choosing between them.

If it expires still intact you hear "Range ends here still intact — price held between 100
and 110", which is a different statement from a failed pattern and is worded differently on
purpose.

#### Overlap: when several shapes fit at once

A stretch of chart can genuinely be an inverse head and shoulders *and* a double bottom
*and* an ascending triangle at the same time. Two experienced traders looking at it would
disagree about which it is, and the terminal is not going to pretend otherwise by silently
picking one.

What it does instead is **rank** them and describe the leader, then count the rest: "…**Plus
2 more formations here.**" The ranking is live formations before resolved ones, then the
largest structure first — the eighty-bar shape is what the chart is making, the twelve-bar
flag inside it is a detail of that shape. Size is the tie-break because it is the only one
available that is not a directional opinion; ranking by "which pattern is more reliable"
would be exactly the untested claim this terminal refuses to make.

**Alt+Shift+D** reads them all when you want the disagreement in full.

**A formation inside a larger one says so.** *"…Inside a larger double bottom that began
12 March."* The container's start date is given rather than just its name, because that is
what lets you go and find it — and it is the difference between a setup that stands on its
own and one that is a detail of a shape still in play. Where shapes nest three deep the
*immediate* parent is named, not the outermost, because that is the level you are actually
standing in.

**And you can choose which one leads.** Press **`;`** to cycle through the overlapping
formations at the current bar; the one you pick is described first from then on, on that
chart, until you press **`Shift+;`** to go back to largest-first. The default ranking exists
because size is the only ordering that is not a directional opinion — but your setup may
well be built on the small one, and the terminal has no business insisting otherwise.
Nothing is hidden either way.

The cycle covers **every** formation over the bar you are standing on, container and
contained alike — a range, the double bottom inside it, and the flag inside that are three
presses apart, and a fourth press wraps back to the first. Each announcement says which of
how many you are on ("2 of 3"), so you always know the size of the set you are walking.

**While one is chosen, `,` and `.` walk that formation's edges only** — its start and its
ending, nothing else. Choosing a shape and then being carried to a different one's break bar
is the behaviour this replaced, and it made the choice feel as though it had not registered:
you would hear *"leading with ascending triangle"* and, one keypress later, *"double bottom
confirmed here."* Both were true. The key had simply gone somewhere you did not ask to go.
When you reach the end of a chosen formation the terminal names it and reminds you that
`Shift+;` releases it.

#### What the terminal will not tell you, and why

You will never hear a formation called bullish or bearish, and you will never hear a
probability. The measured target is spoken because it is arithmetic on two numbers already
on your screen, and it is always phrased as the *measured* target, conditional on *if it
breaks* — that is the difference between reporting a convention and endorsing one.

That reticence is not caution for its own sake. Every price-derived pattern claim this
project has tested has come back null: a randomly-drawn horizontal line was respected 59%
of the time, real swing levels held 46.2% of the time against 46.7% for random lines, and
fib ratios did nothing across 355,000 tests. The shapes are real and worth hearing — they
are how you build a picture of the chart. Whether acting on them makes money is a separate
question, and one this project has repeatedly failed to answer in their favour. Use them to
understand where you are, and get your edge somewhere you have tested.

#### Seeing them, if that helps

**Settings → Appearance → Draw chart formations.** Off by default. It shades each formation's span,
draws the trigger level solid, and draws the conventional measured target faint and dashed.

The audience is anyone who is not listening: a low-vision user, a sighted trading partner, a
screenshot in a bug report. If you are working by ear you already have everything the drawing shows,
in more detail.

The two levels are drawn with deliberately different weight, and the reason is the same reason the
wording is careful. Speech can hedge — *"measured target 39,400 **if it breaks**"* — but a bold line
at 39,400 simply says *target*. So the trigger is solid, because it is a real price where the
formation really does confirm; the measured target is faint, dashed and labelled, because it is a
convention nobody here has tested. **The visual weight is the disclaimer.**

At most three formations are drawn at once, for the same reason the spoken readout describes one and
counts the rest: five overlapping shapes hide the price they are describing.

#### Timeframes

Every tolerance in the detector is expressed in **ATR** — the instrument's own volatility —
rather than in percent or in dollars, so nothing needs recalibrating when you change
timeframe or switch from a $3 small cap to a $600 index fund. Measured across 1-hour,
4-hour, daily, 2-day and weekly bars on the same set of markets, the share of bars carrying
an announcement stays between **8.1% and 9.2%**, and the number of announcements per
formation stays between **1.69 and 1.71**. Those are the numbers you would want to be flat,
and they are.

The one thing that *is* counted in bars rather than time is how long a formation may run: a
shape must span at least 12 bars and at most 160. That is intentional — a "double top"
whose two highs are two years apart is not a double top, it is two highs — but it does mean
a formation is always sized relative to the chart you are on, never to the calendar. A
12-bar flag is an hour on a 5-minute chart and three months on a weekly one, and both are
flags.

Detection runs once when a chart loads and is then cached, so it costs nothing as you
navigate. On a 5,400-bar daily chart it takes about 20 milliseconds; on 328,000 intraday
bars, about two seconds.

#### Heikin-Ashi

**Formations are always read from standard candles, even while Heikin-Ashi is displayed** —
and the terminal tells you so when you switch HA on with formation description enabled.

The reason is the levels. A Heikin-Ashi close is an average of four prices, not a price
anything ever traded at, so a neckline measured from one cannot be put into an order. The
trigger and the measured target are exactly the numbers you might act on, so they are taken
from real prices. Heikin-Ashi also smooths away the wicks that define a double top's two
peaks, which means shapes that exist on an HA chart may not exist in the market and vice
versa.

The practical consequence: with HA on, your spoken open/high/low/close **are** Heikin-Ashi
values, but the formation levels beside them are not. Use HA to hear trend more clearly;
trust the formation levels as real prices.

The same applies to the body and wick percentages, from both the arrow keys and the detail
key: with HA on they describe the Heikin-Ashi candle you are looking at, not the raw bar
underneath it. That matters more than it sounds, because a trending HA series routinely
produces candles with **no shadow at all** on one side — the shaved look is the whole point
of the smoothing — where the underlying bar has one. If those two ever disagree for you,
the readout is wrong and worth reporting: they are meant to describe the same candle.

#### How many formations to expect

Roughly **5 formations per 100 bars**, and about **8–9% of bars** carry an announcement —
one every twelve bars or so. That holds steady across 1-hour, 4-hour, daily, 2-day and
weekly charts, because the detector measures everything in ATR.

That rate is an *output*, not a dial: it is what the detector happens to find, and nothing
you can set changes how the shapes are identified. It is measured after every change to the
feature, because both real defects found in it so far were properties of a rate rather than
of any single sentence — one version announced each formation on exactly one bar and never
again, and every unit test still passed.

**Nested formations are normal, not a fault.** A large inverse head and shoulders can
genuinely contain two ascending triangles, in the same way a paragraph contains sentences.
The larger shape is what the chart is making; the smaller ones are detail inside it. That is
why the readout ranks by size and describes the biggest live one first.

### Value zones

**Value Deviation** answers a different question:
not where the swings are, but where *value* is, and where price has historically
refused to go far from it.

It builds a rolling volume profile over the last N bars (the **Profile window**,
240 by default) and takes its point of control — the price where most volume
traded — as value. Then it measures how far each bar strayed from that value in
units of the value area's own width, and when price *reverses* at a distance, it
marks that bar. A reversal below value marks a support zone; a reversal above it
marks a resistance zone.

The mark's shape and colour carry how far from value the zone formed, across
five tiers per side:

| Distance from value | Support | Resistance |
| --- | --- | --- |
| Tiers 1–2, nearest value | pale green up-triangle | pale red down-triangle |
| Tier 3 | mid green dot | mid red dot |
| Tiers 4–5, furthest | bright green diamond | bright red diamond |

Each tier has its own pitch too, so the tier is audible as well as visible — the
support tiers descend in pitch as they deepen, the resistance tiers rise.

**Show tiers from** is the density control, and it defaults to 2. Tier 1 is a
reversal barely outside value — closer to noise than to a zone — and on a long
view it is the bulk of the marks; left in, it turns the price pane into a
continuous band of glyphs. Raise it to 3 or 4 on a weekly chart or a wide zoom to
leave only the deep stretches, or drop it to 1 to see everything the analyzer
found. It hides the *glyph* only: the Deviation Tier component and the spoken
detail still report every tier, so navigating to a bar still tells you it was a
tier 1, and nothing you could act on is hidden from speech.

**Require a momentum turn as well** is on by default: a zone is only marked when
the indicator's own internal WaveTrend oscillator is turning the same way, which
gives fewer and better-confirmed zones. Turn it off for more zones and more
noise. The indicator carries its own oscillator maths internally, so you do not
have to add Cipher B to the chart to get the benefit.

This one, too, is descriptive rather than a buy/sell signal. What it tells you is
"price came this far from value and turned here" — which is what makes a level a
level. Its own testing is worth stating plainly: the mean reversion it rests on
is real but small in equities at a roughly five-day horizon, and it did not
replicate as a tradable edge in crypto beyond Bitcoin. Read it as a map of where
zones formed, not as a promise about where price goes next.

The **Profile window** adapts down if the chart has not loaded enough history —
a window can never exceed a third of the loaded bars, because otherwise a fresh
200-bar chart would leave every component empty and read as broken. More loaded
history genuinely helps this indicator, so zoom out and let it fetch before
judging it.

### The respect report

Every chart has more lines on it than matter. The respect report tells you which
ones this market actually reacts to — measured, not assumed.

Open it with **Alt+R**, or with the **Levels** button on the toolbar's first row.
It measures over the loaded history and presents two tabs: **Levels near price**
(support and resistance levels from the indicators you have on the chart, prior
period highs and lows, round numbers) and **Moving-average ranking** (a standard
set of periods — 10, 20, 21, 50, 89, 100, 200 — including higher-timeframe
projections).

Each row is a table row with proper headers: the line, its current price, its
hold rate, how many touches it has had, the median reaction size in ATR, how
often it held as support versus as resistance, when it was last touched, and how
far away it is now in ATR.

The definitions matter, and the dialog states them under the table:

- A **touch** is counted when the bar's *range* — wick included — reaches within
  a tolerance of the line, and only if enough bars have passed since the last
  counted touch that this is a separate event rather than the same one over
  again.
- It **held** if price then moved a minimum distance away within a set window
  *without closing* through the line by more than the break tolerance.
- Wicks through and straight back count as holds. That is a sweep — the level
  working, not failing. Measuring the reaction from the wick instead of the close
  would make a sweep and a genuine breakdown look identical, which was the first
  version's bug.

**Only lines with at least N touches** is ticked by default. Leave it ticked: a
line with two touches and a 100% hold rate is not a reliable line, it is two
coincidences, and rows below the threshold are marked "(thin sample)" when you
untick it. **Re-measure** recomputes after you have loaded more history or
changed indicators, and **Speak summary** reads the top of the ranking aloud.

### The asset dossier (Alt+I)

**`Alt+I`** — I for Instrument — opens a report on whatever is loaded on the active chart. There is
no symbol picker inside it, deliberately: a second selection would drift out of sync with the chart
you are reading, and "the dossier is describing a different asset than the chart" is a bug nobody
would catch by ear. Choose market, provider and symbol on the toolbar, load the chart, press Alt+I.

Everything is looked up live at the moment you open it. **The asset class comes from the market you
loaded from, not from the ticker** — "ETH" is a coin on Bitstamp and could be an equity ticker
elsewhere, and guessing from the symbol would produce a confident report about the wrong kind of
thing.

The tabs are **questions, not sources**. Tabs labelled "CoinGecko", "GitHub" and "SEC" would push
the synthesis back onto you — four tabs and four half-answers to hold in your head to decide one
thing. So you get:

| Crypto | Equities |
| --- | --- |
| Chart read · Identity · Supply and dilution · Development · Disclosure · Checks | Chart read · Company · Financials · Filing activity · Checks |

Every individual field still names its own source. The **chart read never needs a network** and is
always the first tab, so the dossier is useful even with every remote source down.

#### When there is nothing to show

This is the case the feature exists to handle well, so every row carries one of four states and a
blank row is treated as a defect:

| State | Means |
| --- | --- |
| **Ok** | a real value |
| **No data** | the source answered and the answer is "none" — often the interesting one |
| **Not applicable** | meaningless for this asset class (R&D expense for a coin) |
| **Unavailable** | the source could not be reached, or is not configured |

*No data* and *unavailable* are never merged: the first is a finding, the second is a reason to
retry. For an unlisted token the dossier says so outright — *"No listing found. For a very new token
this is expected, and it means nothing here can be verified — treat every claim about it as
unchecked."* That is the most informative thing the screen can say about a brand-new token, and it
would be lost in a blank panel.

#### What makes it more than a price page

Price, market cap and rank **are** the front page, and repeating them adds nothing. Two things are
not on any price page:

**Is anyone still building it?** The dossier queries GitHub directly on the repositories a project
lists, and when everything listed looks stale it sweeps the owning organisation for its most
recently pushed work. This matters more than it sounds. Measured while building the feature:
CoinGecko reported **Kaspa at zero commits in four weeks**, because it tracks a repository that was
superseded — while the actual project repository had been pushed **that same day**. Reading the
aggregator alone shows one of the most active projects in the market as abandoned. When the fallback
sweep is used it is labelled as what it is: activity in the same organisation, not necessarily the
flagship.

**Does it disclose anything?** Website, whitepaper, public source, block explorer — present or
**MISSING**. The absence is the measurement. No whitepaper or no public source is the loudest cheap
signal there is.

Plus supply and dilution (FDV against market cap, circulating share of maximum, uncapped issuance)
and turnover, flagged at **both** ends — too little and you cannot exit, too much is a wash-trading
tell.

#### The checks are not a score

Eleven checks for crypto, each one comparison over a value already on the screen, each shown with
its own reasoning, and **never summed**. A single number would read as a rating.

**None of these thresholds has been tested against forward returns.** They are conventional red
flags and the dossier says so. Testing them properly needs point-in-time snapshots of a universe
that still contains the tokens that died, and today's listings by construction do not — which is why
this project records the crypto universe daily. The standing prediction, written down before any of
it was built: **this works as a veto, not as a timing signal.** It should avoid losses; it should not
pick winners.

#### Reading it by ear

The headline is a live region, so it is spoken when the dossier opens. **Speak summary** repeats it —
useful when the summary is long, or when you have arrowed away and want it again. Tab moves between
the tab strip and the panel; arrow keys move along the tabs.

#### Limits worth knowing

- **CoinGecko rate-limits** the free tier. The dossier makes one coin call plus up to four GitHub
  calls per open.
- **GitHub allows 60 unauthenticated requests an hour.** The organisation sweep only fires when the
  listed repositories look stale, to conserve that budget.
- **SEC EDGAR covers US filers only.** ETFs, index vehicles and non-US listings are not filers, and
  the dossier says so rather than showing an error.

### Quick trade from the chart

You can size and place a trade without leaving the chart or opening the trading dashboard.

| Key | Action |
| --- | --- |
| `Ctrl+Alt+Shift+1` | Arm 0.5% risk |
| `Ctrl+Alt+Shift+2` | Arm 1% risk |
| `Ctrl+Alt+Shift+3` | Arm 2% risk |
| `Ctrl+Alt+Shift+X` | Make the bar under the cursor your stop |
| `Shift+Enter` | Place a limit at the bar under the cursor |
| `Ctrl+Enter` | Place at market |
| `Ctrl+Alt+Shift+Q` | Say what is armed right now |
| `Ctrl+Alt+Shift+0`, or `Escape` | Cancel |

#### Why the stop comes first

**A risk percentage is not a position size.** "Risk 1%" is a cash budget — on a $100,000
account, $1,000. Turning that into a quantity needs the distance to your stop, because that
distance is what one unit of the instrument can lose. Entry 43,700 with a stop at 42,100 is
1,600 of risk per unit, so $1,000 buys 0.625 units.

So arming a percentage puts the terminal into *stop needed*, and it will not place an order
until you have set one. What you get in return is the calculation itself, spoken at the
moment you need it:

> *"Armed 1 percent. $1,000.00 at risk, stop 42,100, long 0.625 units, entry 43,700."*

That sum is what a sighted trader does in a position-size calculator before every trade. Not
having to leave the chart to do it is the point of the feature.

#### The rest of the behaviour

**Direction is inferred, never asked.** A stop below the current price can only be protecting
a long; above it, a short. There is exactly one right answer, so the terminal does not ask.

**You are told you are armed on every bar you move to** — *"Armed 1 percent, ready."* It is
short because you hear it constantly, and unconditional because forgetting you are armed and
then pressing Enter for some other reason is the one way this feature could cost you money.

**`Escape` always cancels**, and it reaches an armed trade before it reaches a half-placed
drawing — the armed trade is the one with consequences.

**The stop is always sent with the entry.** Your size was derived from the stop distance, so
an entry placed without it would have a quantity justified by protection that does not exist.
On brokers with native bracket support (Alpaca) all three legs go as one order; on the others
the terminal verifies afterwards that something protective actually reached the exchange, and
says so loudly if not.

**Limits and markets price differently.** `Ctrl+Enter` uses the live price. `Shift+Enter`
uses the bar under your cursor — and re-derives the size from there, because if you have
moved a long way since setting the stop, the stop distance has changed and so has the correct
quantity.

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

On a **touchscreen with no keyboard**, arm the tool from the chart menu's Drawing
Tools, then use the touch toolbar's **Place drawing point** button in place of the
shortcut: move the cursor with the bar buttons (or tap a bar) and press Place drawing
point once per anchor. Re-pressing it advances through the same tool's anchors exactly
as re-pressing the shortcut would, so a trend line, channel, or any multi-anchor tool
completes entirely by touch.

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

#### Arrowing along a drawing

Focus a drawing with `Page Up` or `Page Down` and the bars read differently from an
indicator's. The switch names it once — *"Trend line 2."* — and each bar then says the
drawing's value first, where you are on it second, and which side of it price is on third,
with the middle and last parts left out whenever there is nothing to say: *"150.50, price
below."* between the anchors, *"170.50, at end anchor, price above."* on an anchor, *"199.50,
past end, price above."* where the line is projected beyond the point you drew it to, and
*"price crossed above"* on the bar where the close changed sides. Where the drawing has no
value at all, the position leads with a bar count — *"Before start, 20 bars."* — because
arrow keys move bars and a count tells you how far to press. A drawing with several parts (a
Fibonacci retracement's levels, a rectangle's top and bottom) announces which one it is
reading — *"Fibonacci retracement 1. 7 components, reading 61.8%."* — and `Ctrl+Up` and
`Ctrl+Down` name each level once as you move between them; the bars themselves stay
value-first. *"Not yet calculated."* means the drawing genuinely has no number at a bar it
should — that is a fault worth reporting, not a place you are standing.

#### Moving an anchor afterwards, from the keyboard

Until version 2.6 an anchor you had already placed could be moved only by dragging its
handle with a mouse, or by typing an exact price and date into Properties. Typing an exact
value is a route, but it is not the same thing as "a little to the right", which is what
the drag was for. Now it has a keyboard equivalent. Focus the drawing with `Page Up` or
`Page Down` — or pick it in the Object Tree — and:

- `Shift+Left` and `Shift+Right` move the selected anchor one **bar** earlier or
  later. A bar, not a day: on a daily chart Friday's next bar is Monday, and a halt is
  stepped over the same way. Past the last bar the anchor projects into the chart's right
  margin, and the readback says so: "June 30, 3 bars past the last bar."
- `Shift+Up` and `Shift+Down` move its price by one percent of the price range you
  can currently see, so the step follows your zoom the way a drag would, and never by less
  than the last decimal the price is spoken with.
- `Ctrl+Alt+Shift+G` selects the next anchor. On a drawing you have just focused, the first
  press only tells you which anchor is selected; the next moves on, and it wraps.
- `Ctrl+Alt+Shift+B` snaps the selected anchor's price onto its bar's high, low, open or
  close — the nearest first, then the others in that order on repeated presses. It is the
  quick way to put a trend line's end exactly on a wick.
- `Shift+F1`, the context summary, names the selected anchor without moving it.

Hold a key and you hear a short tick while the anchor moves; one sentence follows when you
stop — *"End: 105.20 at June 15, 2026, 09:30. Trend line 2, anchor 2 of 2."* — the value
first, then which drawing and which anchor of how many, every time, because there is no
status bar to glance at. A key that cannot act plays the boundary sound and says why once:
the anchor is already at the first bar, a Fibonacci level has no date to move, no drawing
is focused — or the chord was pressed where it cannot reach the chart: *"The chart does not
have focus. Control Alt Shift C returns to the chart."* with focus on a toolbar button, and
*"Not while Properties is open. Escape closes it."* under a dialog. The Object Tree is the
exception, on purpose: it is where you pick a drawing, so the nudge works while the tree is
open and you can move what you just selected without closing it first. A whole run of nudges
is one `Ctrl+Z`. The same six actions are in the
drawing's context menu (`Shift+F10`) for voice control, switch access and single-pointer
use, and the menu stays open so "move later" can be activated several times.

Two platform notes. On Windows with more than one keyboard layout, `Alt+Shift` pressed and
released on its own switches layout, so hold `Alt+Shift` and then press the arrow. On a Mac
with VoiceOver's modifier set to Control+Option, every `Ctrl+Alt+Shift` chord in this
application is a VoiceOver chord: set the modifier to Caps Lock in VoiceOver Utility, or
pass a single key through with VO+Tab.

#### Taking a drawing back

`Ctrl+Z` undoes the last chart edit and `Ctrl+Y` redoes it, and both tell you *what*
they just did rather than only that they did something — *"Undone: trendline moved."*,
*"Nothing to undo."* The stack holds the last fifty edits, which is deep enough that
"undo until it sounds right" works. It covers the two things that are genuinely painful
to reconstruct by hand: edits to a drawing, and deleting a series with `Delete`. It is
not a general application-wide undo — it will not take back an order, an alert, or a
settings change, and nothing that leaves the chart is on it.

#### The context menu, from the keyboard

The Applications / Context Menu key, or `Shift+F10` if your keyboard has no such key,
opens the same right-click menu a mouse user gets. What you get depends on what is
focused: a focused drawing gets the drawing's own menu — Delete, Duplicate,
Properties — and anything else gets the chart-level menu, the one right-clicking empty chart space
opens, carrying the bar under your cursor so options like "Play from here" act on the
right bar. It is exact keyboard parity with right-click, so nothing in this application
is mouse-only.

### Naming your drawings

A drawing's name is what you hear when you move to it, what the object tree lists, and what
an alert refers to. By default that is "Trendline 3", which tells you nothing. Open
**Properties** (`P`) on a drawing and set **Name** to why you drew it — "Weekly resistance
from the January high" — and that is what is announced from then on. Clearing the field
restores the automatic name.

For a keyboard user moving between a dozen drawings by ear, this is the difference between a
list and an inventory.

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

### The object tree

Once a chart has a few indicators and drawings on it, navigating to a particular one
just to hide or remove it becomes a chore. The object tree, opened with Alt+O and
announced as "Objects: chart object tree", is the manager's-eye view that solves that:
a single tree of everything on the chart, laid out as the same panes → series →
components hierarchy you move through on the chart itself, but as one list you can walk
through without disturbing the chart.

It is a real tree, so it answers to the tree keys rather than to Tab: up and down arrows
move between entries, right and left expand and collapse the entry you are on, and Home
and End jump to the first and last. Tab is still how you reach an entry's Hide, Mute and
Delete buttons, and Escape closes the panel. Each entry says whether it is expanded or
collapsed as you land on it, so collapsing a pane and re-opening it sound different.

Each series in the tree reads itself the way you'd want — its name, how many components
it has, and its current state — so you hear, for example, "RSI, one component, visible,
audible, focused, collapsed" as you move onto it. From there you act on any object in place,
without first navigating to it on the chart: each series and each component has a toggle
to hide or show it (the same effect as H) and a toggle to mute or unmute it (the same as
M), and a series carries a Remove control to take it off the chart entirely. Selecting a
series in the tree also focuses it back on the chart, so the panel doubles as a
jump-to-anything: find the indicator in the list, activate it, and your chart cursor is
now on it. When the soundscape is getting crowded, this is the quickest place to find
the one series cluttering it and silence or remove it. A button at the top also jumps
straight to the Strategy manager.

---

## AI, Narration, and the Journal

Three features sit between reading the chart yourself and acting on it: an AI analyst
you can ask for a second opinion, an auto-narrator that watches a series and speaks
up when something happens, and the Journal that quietly records everything the
terminal has said so nothing scrolls past for good. All three chords use three
modifiers and so are **not** remapped on the web host — they are the same everywhere.

### The AI technical analyst

Press Ctrl+Alt+Shift+A to open the AI Analyst. It gathers a snapshot of what you are
looking at — the current symbol and timeframe, the most recent candles, a summary of
every indicator you have on the chart, and, where the provider supports vision, an
actual image of the chart — sends it to a large language model, and reads back a
concise, plain-language technical analysis written for text-to-speech: trend
direction, the key support and resistance levels, what momentum is doing, and a
short-term outlook. Think of it as a second pair of eyes on demand — a narrative
framing of the same structure you have been navigating bar by bar, in one paragraph
you can take in at listening speed.

It is worth being clear about what it is and is not. It *describes* the chart; it does
not advise you and it cannot place a trade — there is no button in it that touches your
account. And because the answer is generated, it can be wrong: treat it as informed
commentary to weigh against what you heard yourself navigating, not as a signal to act
on. Used that way — as a sanity check on the read you already formed — it earns its
place.

The natural way to use it is to set the scene first. Load the symbol and timeframe you
care about, add the indicators you want it to consider, navigate enough to have your
own opinion, and *then* press Ctrl+Alt+Shift+A — because it analyses the chart as it
stands at that moment, the more you have set up, the more grounded its answer. A useful
habit is to ask twice: get its read on the daily, switch the timeframe to the hourly
(or add an indicator you suspect matters), and run it again to hear how the framing
changes. A typical reply sounds like this:

> "Bitcoin, four-hour. The trend is up but stretching — price is roughly eight percent
> above the fifty-period moving average and RSI is at seventy-three, in overbought. The
> nearest support is the prior breakout around sixty-one thousand five hundred;
> resistance is the recent high near sixty-four thousand two hundred. Momentum is still
> positive but the MACD histogram is shrinking, which often precedes a pause. Short-term
> outlook: constructive but extended — a pullback toward the moving average would be
> normal and would not break the uptrend."

It needs a key. Add one for at least one provider in the API key manager (Alt+K); the
terminal tries the providers it knows in order — Claude, then OpenAI, then Ollama — and
uses the first you have configured. If none is set up, the Analyst tells you so rather
than failing silently. The choice of provider is partly a privacy choice. Claude and
OpenAI are cloud services and are vision-capable, so they get the richest input — but
your chart snapshot, image included, leaves your machine to reach them. **Ollama runs a
model locally on your own computer**, so nothing leaves the device at all; it is the
right pick when you would rather your data stay home, at the cost of running a smaller
model and installing it yourself. Either way, remember that a cloud request both shares
your chart data and usually costs a small amount per call, so it is a deliberate
action, not something to lean on every bar.

### Auto-narration and live announcements

Where the AI Analyst is something you ask, auto-narration is something you switch on and
forget. Press **N** — the third switch on a chart object, beside H for hide and M for mute —
and from then on the terminal watches that indicator and **speaks new events as they occur on
live bar closes** — a fresh signal firing, or the oscillator entering or leaving an
overbought or oversold zone. You will hear short, plain announcements as they happen:
"RSI overbought", "MACD bullish crossover", "Stochastic leaving oversold". It announces
only what happens *after* you switch it on; it does not replay the history you already
navigated. And because it is per-series, you can leave it running on the one indicator
you care about and not be interrupted by the rest of the chart — set it on your RSI, say,
and get on with reading price while the terminal keeps half an ear on momentum for you.
Toggling it announces the new state, "Narrating" or "Narration off", so you always
know whether it is listening. `Ctrl+Alt+Shift+N` does the same thing and is the one to reach
for when focus is somewhere other than the chart — it has three modifiers, which browsers do
not reserve, so it works unchanged on the Linux web host.

**N follows your cursor, exactly like H and M.** With the cursor on a series it switches the
whole series; with the cursor on a *component* — Ctrl+Up and Ctrl+Down move between them — it
picks out that one component. That matters on an indicator like Cipher B, which prints eleven
components: switching the series on and leaving it there gives you all of them, and pressing N
on the divergence line narrows narration to just that. Press N on it again and you are back to
the whole series. So the rule is: **no component picked out means all of them**, and the series
switch is always the master — a component flagged on a series that is not narrating says
nothing yet, and the confirmation tells you so rather than leaving you waiting. The
confirmation on a component names the component and nothing else — *"Triple Confluence Buy,
narrating"* — because the cursor is already on it and the series was named when you arrived.
When you want everything back to a known state, **Ctrl+Alt+Shift+O** switches narration off
on every series and clears every component selection, the way Ctrl+Alt+Shift+K and U undo
every hide and every mute.

**Where these words land in the sentence.** Moving onto a series or component that is switched
off says so **first** — *"Hidden and muted. Cipher B. 11 components…"* — and that it is
narrating **last**: *"…64,900. Narrating."* The two halves are not the same kind of fact.
Hidden and muted explain a *silence*, and anything that interrupts an utterance takes its end,
so the half you must not lose goes in front; narrating is an addition, everything else about the
reading is normal, and it is the least urgent thing in the phrase. Both flags are spoken when
both apply, because they are cleared by different keys and a readout naming one of them is a
readout that guarantees a second wrong guess.

**What an overlay narrates, and when.** Flag a moving average with N and what you get is its
**crosses, on the bar close**: *"Price crossed above EMA 9 at 64,900."* An overlay gets crosses
and nothing else — no "support broken", no "tested twice" — because those belong to a level,
which has a side and can cease to exist, and a moving average has neither. Cipher SR's pivots
and Spider Lines' fibonacci EMAs are declared levels and do get the fuller vocabulary. And none
of it is spoken during playback; see the playback section above for why.

**What an oscillator narrates, and when.** Its **levels** — the thresholds the indicator was
built around, which it declares for itself and which you can see as dashed lines in its pane.
Crossing one is spoken on the bar close: *"Stochastic 14: crossed above overbought, 80."*,
*"Rate of Change 9: crossed above zero."* Move a level with the Properties dialog, or drop your
own with `0`, and the new value is what gets narrated — the sentence follows the line, not a
number baked into the terminal. A handful of indicators have wording written specially for them
and use that instead, so RSI says *"RSI overbought"* rather than the generic form, and Cipher B
says *"Anchor wave oversold"*; you never hear both for one crossing.

**Every indicator, and what it will say.** Six routes, and an indicator uses whichever ones it
has:

| What the indicator has | What you hear, and when | In playback? |
|---|---|---|
| **A signal marker** — an entry dot, a divergence, a break of structure | The signal itself, on the bar it prints: *"Triple confluence buy, strong confirmation."* | **Yes** — this is the only thing playback speaks |
| **A price-space line** — EMA, SMA, VWAP, Bollinger, Keltner, Donchian, Ichimoku's lines, pivots | *"Price crossed above EMA 9 at 64,900."* on the bar close | No |
| **A declared level** — most oscillators: Stochastic, CCI, MFI, ADX, ROC, %R, TRIX, CMO, Chop, PPO, StochRSI | *"crossed above overbought, 80."* on the bar close | No |
| **Its own zone wording** — RSI, MACD, Vortex, Cipher B's waves | The indicator's own phrase: *"RSI overbought."*, *"MACD bullish crossover."* | No |
| **A declared level line** — Cipher SR's pivots, Spider Lines | Broken, tested, approached, and crossed — the full support/resistance vocabulary | No |
| **A cloud** — Ichimoku's Kumo, MA Cloud, Cipher B's anchor fill | *"Price entered the Kumo."*, exited, crossed | No |

Four kinds of thing stay silent however you flag them, and it is deliberate: your **drawings**
(they answer to the drawing keys and read themselves when you arrow onto them), the **candles,
volume and the profile surfaces** (the new-bar announcement *is* the candle's narration), the
**comparison overlays** (another symbol's price, drawn here), and the **unbounded accumulators**
— OBV, A/D line, Force Index, standard deviation, ATR, Ulcer, historical volatility. Those last
ones have no threshold that means anything: an ATR of 400 is enormous on one asset and noise on
another, so there is nothing to cross and inventing a number to cross would be worse than the
silence.

**What a signal sounds like.** A narrated signal is introduced by the *component* that fired
it, never by the series: *"WaveTrend Cross Bull: Wave cross up 12."* You chose which series
narrate, so the series name tells you nothing you did not already know; which of Cipher B's
eleven markers just printed is the fact you are waiting for. Where a signal's own wording
already says which component it is — *"Bullish divergence"*, *"Triple confluence buy, strong
confirmation"* — nothing is put in front of it. The same rule holds during playback.

**Everything about one bar arrives as one sentence.** When a bar closes you hear the candle and
then whatever your indicators made of it, in one breath: *"Close 64,905. Bullish engulfing. New
bar: Open 64,910. Triple confluence buy, strong confirmation. RSI 14: RSI overbought."* Up to
five clauses, ordered by consequence — a level breaking first, then an indicator's own signals,
then crossings, then tests, then approaches, and last the oscillator commentary, which is the
most frequent thing the terminal can say and the least worth leading with. Anything beyond five
is dropped from the bottom of that order rather than from whatever arrived last.

Two switches sit above all of it, both on the **Narration** tab of Settings (F12). "Narrate
signals on bar close" is the master: N chooses which series and components speak, that switch
decides whether any of them do, and turning it off is how you get an hour of quiet without
un-flagging every indicator and having to remember what you had flagged. "Narrate during
playback" decides whether those same flagged series speak while the chart is playing, covered
back in the playback section — and what you hear there is scoped to what you *played*, so
Shift+Space on one series narrates that series and nothing else on the chart.

Auto-narration is one of a small family of "let the terminal keep you posted" features
worth knowing together. The rolling **new-bar announcement** — the "Close … New bar …"
you met when you first loaded a market — is the always-on heartbeat of the live candle,
and you can turn it on or off under Settings (F12), Narration, with "Announce new bars". The
**detailed point analysis**, Ctrl+Shift+D (Alt+Shift+D on the web host), is the
on-demand deep read of whichever bar you are sitting on — candle values, patterns, every
indicator, every signal, in one keystroke, covered back in the chart chapter. And the
**context summary**, Shift+F1 for a quick "symbol, provider, timeframe" and Ctrl+Alt+Shift+C
for the fuller picture, tells you where you are at any moment. Between them you can run
as hands-off or as hands-on as you like: narration and new-bar announcements stream the
live market to you, while Ctrl+Shift+D and the AI Analyst are there the moment you want
to stop and look hard at something.

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
built-in simulator instead of a real exchange. Fills are driven by **real live
prices**: a market order fills at the current price, and a stop, target, or trailing
order fills the moment live price action actually crosses it. You start with a
virtual balance, the account persists between sessions, and a "Reset paper account"
button on the same settings tab wipes it back to the starting balance whenever you
want a clean slate. **It asks you first.** The first press arms a confirmation and
says what is at stake — the balance, every open position and working order, and the
whole trade history, with no undo — then offers "Confirm reset" and "Cancel", with
focus on Confirm. Escape at that point backs out of the question rather than closing
Settings; press it again to close. Whichever way you answer, focus returns to the
button you pressed. While paper mode is on the dashboard shows the environment as
"Paper (simulated)" with a paper banner, and the red live-funds banner is
suppressed. Everything described in the rest of this chapter behaves identically in
paper and live — so practise here until the spoken feedback is second nature, then
switch a real key in.

**Your trades keep running when you look elsewhere.** A chart you have an open
position or a resting order on is watched in the background, so a stop fires and a
limit fills while you are on a different tab entirely, and the position's profit and
loss keeps moving rather than sitting still until you go back. This works even after
you close that chart's tab, and after you close and reopen the app — the terminal
remembers which chart each position was opened on. You do **not** need to switch
background monitoring on for this; that setting governs watching charts you merely
have open, while a chart with money on it is watched either way.

**What the paper account will and will not do.** It settles like a spot exchange out
of a single cash balance, so it will refuse to sell an asset you do not hold and
refuse a buy you cannot afford — telling you by how much in both cases. Brackets,
one-cancels-other pairs, trailing stops and trailing take-profits are all fully
simulated.

**Short selling is available again, at 1x, with collateral.** It was withdrawn in
2.2.0 because it was offered and did nothing; it returned in 2.3.0 modelled properly.
Selling something you do not own means somebody lent it to you and you owe it back at
whatever it ends up costing, so the account locks the sale proceeds *and* an equal
amount of margin beside them — both appear under Locked on the Balances tab, and
neither is spendable while the short is open. You are told what the short cost to
open, what it is worth now, and the **liquidation price**: the level at which the
collateral no longer covers the position and the simulator closes it for you. If you
try to open a short the collateral will not cover, it is refused with the shortfall
named rather than accepted and quietly unwound later.

**Leverage is still withheld.** The margin requirement is 1x — a dollar of collateral
per dollar of exposure — so there is no borrowing beyond your own balance, and the
leverage selector does not appear on the paper ticket at all. It returns when
multi-x margin is properly modelled, and not before.

On the **hosted web terminal** (the logged-in, multi-user build) this choice is made
for you: paper trading is **always on and cannot be switched off**, so pressing Alt+T
always opens a paper dashboard and you can never place a real-money order from the
browser. Real trading with your own broker keys is a desktop-app feature. (If you tried
this on an earlier build and got "provider does not support trading," that was the bug
this behaviour fixes — the web providers are data-only, and orders now correctly route
to the paper simulator.)

**Your paper account is one account, however many tabs you have open.** Open the terminal
in three browser tabs and all three are looking at the same balance, the same positions and
the same resting orders; a trade placed in one appears in the others. This is worth stating
because it was not always true: until August 2026 each tab kept its own copy of the account
and wrote the whole thing back to disk on every change, so the last tab to save silently
erased whatever the others had done. If you ever found a position you were sure you had
opened simply missing, that is very likely what happened, and it can no longer happen.

Resting orders are watched from **every** chart you have open, not only the tab that placed
them. A stop you set in one tab still fills while you are working in another.

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

How the protection actually reaches the exchange differs by broker, and the
terminal is honest about each:

- **Binance** attaches stop-loss and take-profit natively with the entry (and
  supports true OCO pairs on spot).
- **Tradier** places the entry and its exits as ONE exchange-native advanced
  order (OTO with one protective leg, OTOCO with both) — the linkage lives on
  Tradier's servers, so your protection exists even if the terminal dies the
  moment after submission.
- **Schwab** does the same with an order tree: the entry triggers the exit (or
  an either-or pair of exits), enforced server-side, good-till-cancelled.
- **Kraken** can attach only ONE protective order per entry. If you set both a
  stop and a target, the STOP is attached — safety over profit — and the
  terminal tells you out loud that the take-profit was not placed.
- On the remaining brokers the terminal submits the entry, then the
  protection, then about two seconds later verifies the protection actually
  landed — and if it cannot find it, warns you, interrupting: "Warning: no
  stop loss or take profit found on the exchange for {symbol}. The position
  may be unprotected — verify your open orders." Treat that as a call to
  action.

Your resting protective legs are visible: on Tradier and Schwab the Orders tab
lists each leg of a bracket individually — entry, stop, and target, each with
its real trigger price — so you can hear that your protection is in place.
One further caution: there is no inline editor for a resting order's
protective levels; to move a stop you cancel it on the Orders tab and place a
new one. Set your exits deliberately at entry.

### The live order review

Paper orders submit the instant you activate the submit button. **Live** orders do
not — they get a spoken safety review first. When you submit on a live profile, the
terminal speaks a one-line summary — "Confirm: {side} {qty} {symbol}, {type}. Stop
{price}. Target {price}. Estimated cost {amount}, fee {amount}. Confirm or cancel." —
and replaces the submit button with **Confirm** and **Cancel**. The review also
carries two warn-only risk checks. On a leveraged entry it estimates the liquidation
distance and compares it with your stop: if the exchange could close the trade
before your stop fires you hear a plain warning to reduce leverage, and if the
buffer is under two stop-distances you hear a caution. And if the order stacks onto
open positions in the same sector — Bitcoin and Ether are one bet, gold and silver
are one bet, SPY and QQQ are one bet — a sector note reminds you that correlated
exposure counts toward one 2-percent-per-sector risk budget. Neither ever blocks
the order; the terminal informs, you decide. (Paper fills speak the sector note
too, so the habit forms where the money is pretend.) Nothing reaches the
exchange until you activate Confirm; Cancel backs out with "Order canceled before
submit." It is the deliberate pause that a real-money order deserves, and it is on
automatically whenever you are live. (The live-versus-paper decision is re-checked at
the moment you submit, so flipping paper mode off in Settings with the dashboard open
never lets a real-money order skip its review.)

Beyond that one-time check at submit, the terminal keeps watching every open leveraged
position. If the market drifts close to a position's liquidation price while you hold
it, you hear an interrupting spoken warning with an alert earcon — once per approach,
naming the symbol and how far it is from liquidation — so a slow slide toward a forced
close announces itself instead of surprising you. Positions with no liquidation price
(spot holdings) are not monitored.

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
- A cancel: "Order canceled. {symbol}."
- A refusal: "Order rejected for {symbol}," **followed by the reason** — "Insufficient
  paper balance: that position needs 51,970.00 USDT and the account holds 174.91.", "No
  price available for BTC/USD: open its chart, or check the venue is reachable.", "The
  position was already closed." Closing a position from the Positions tab announces the
  same way. Before August 2026 the reason was computed and then discarded, so a refusal
  arrived as three words with nothing to act on; the half that told you what to do was the
  half being dropped.

Order IDs are never spoken — they are meaningless to the ear. Every one of these
events is also written to the Journal (Ctrl+Alt+Shift+J), so a fill that goes by
while you are concentrating elsewhere can be read back afterward. (Realized profit
and loss are spoken on every paper close; on a live exchange they appear when the
exchange reports them.)

A timing note for two brokers: most brokers (and the paper simulator) push order
events instantly, so announcements are immediate. **Tradier** now has a live
account-event stream too; if it can't connect, and always on **Schwab** (whose
push channel isn't implemented yet), the terminal instead watches each order you
place by polling the broker — so fills, stops, and cancels there still announce,
typically within a few seconds (up to about half a minute for an order that has
been resting a long time). One thing polling cannot see: protective legs the
broker attaches server-side get their own order ids, so if one of those fires on
Schwab you'll find it in the Orders/History tabs rather than hearing it named.

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
- **Balances** shows, for every asset you hold: the **free** and **locked**
  quantities, what the holding is **worth** in the account's quote currency, its
  **share** of the account as a percentage, and the **day's change**. Above the table
  a summary line gives the portfolio total. Before 2.3.0 this tab showed quantities
  only, which told you what you held but never what it was worth. An asset whose price
  cannot be fetched is shown as **unpriced** and the total says explicitly that it is
  partial — a price that could not be read is never silently counted as zero. Money
  locked as short collateral appears here under Locked (see paper trading, above).

### Getting funds onto a venue: deposit addresses

Some venues can hand you a deposit address for an asset, and when the terminal can do
that for the provider you are on, a **Deposit** button appears in the toolbar. Choose
the asset and the network and the address comes back in a **read-only text field with
a Copy button** beside it. That is deliberate, and it is the whole point of the
dialog: a read-only field is walkable by your screen reader's review cursor and by a
braille display character by character, so you can verify an address the same way a
sighted user reads one off the screen — no special handling, no image of a QR code you
cannot use.

Two things happen before you ever see the address. It is **checked against the
network's own checksum**, and if it fails that check it is refused outright rather
than shown with a warning — an address that is wrong by one character sends your funds
nowhere recoverable, and that is not a risk to leave to the reader. And its
**capitalisation is preserved exactly**, because on several networks the pattern of
capitals *is* the checksum, so an address helpfully lower-cased in transit is a broken
address.

**Kraken** is the first venue behind this. Note that the terminal **reads** deposit
addresses; it does not create them. If you have never deposited that asset on Kraken
before, no address exists yet and you will be told so — generate the first one once on
kraken.com under Funding → Deposit, and the dialog will find it from then on.

Deposits need your own API key, so this is a desktop-app feature; the hosted browser
terminal has no wallet.

**Moving funds off a venue is not available.** A withdrawal path exists in the code
and is switched off. It is the one operation in this terminal where a mistake loses
money directly rather than through a trade, and it will not ship until it has been run
end to end against a live venue by a person. Until then no button renders, and the
code refuses before any request could leave your machine.

### Monitoring with the browser closed

On your own machine (the local web host — not the hosted site), the terminal
is a server that outlives the browser tab. Settings → General → **"Keep
monitoring when the browser is closed"** puts that to work: any alert that
names a symbol and provider keeps evaluating about once a minute with no
browser open, and when one fires you hear it three ways — a notification
sound, a desktop notification, and speech through Orca in your own voice. The
watch list is simply your alert list; there is nothing separate to configure.
The honest limits: alerts that read the chart itself — indicator values, the
volume-profile POC, trend and zone conditions, and advanced condition trees —
stay session-only (the indicators they read exist only while their chart is
open, and the terminal says so when you create one), alerts scoped to "the
current chart" stay session-only too, and the background monitor stands down
whenever a browser session is open so nothing is announced twice. The sound
is replaceable — drop your own WAV at sounds/alert.wav in the app data
folder. Pair it with a systemd user service and the terminal listens from
login to shutdown.

### The system-tray applet

On a local machine the terminal also puts an icon in your system tray (the
notification area) — the control surface for that always-running server, so
you can drive it without a browser open. Its accessible name carries the live
unread-alert count ("Accessible Trade Terminal — 3 new alerts"), and its menu,
which your screen reader navigates like any menu, has seven items:

- **Restore workspaces to browser** — reopens the terminal in your browser; the
  last session resumes itself.
- **Show recent alerts** — speaks how many alerts are waiting and opens a plain,
  navigable **Recent alerts** page where each alert has *Mark as read* and
  *Dismiss* buttons (plus *Mark all read*). Alerts that fired while the browser
  was open show up here too, not only the ones caught with it closed.
- **Silence alerts for 30 minutes** — pauses the background announcements; the
  item then reads "Resume alerts" with the minutes remaining, so a second
  activation lifts the silence early.
- **Connection status** — speaks a quick summary: whether monitoring is on, how
  many alerts are armed, and how many are unread.
- **Copy terminal address** — copies the local URL to the clipboard, for opening
  the terminal from another browser or device.
- **Turn background monitoring on / off** — the same setting as the Settings
  checkbox above, reachable from the tray.
- **Exit terminal** — shuts the server down cleanly.

This is a local-machine feature only; the hosted multi-user site never shows a
tray. On Linux it uses the freedesktop StatusNotifier protocol (its menu is
exposed to your screen reader over AT-SPI); on Windows it uses the standard
notification-area icon; on macOS the WebHost provides the menu *actions* but not
the icon itself — the native Mac desktop app is the right home for a Mac tray.
If your desktop can't host a tray icon, the terminal simply runs without one —
the background monitoring above still works either way.

### OCO pairs: one cancels the other

In paper mode the dashboard offers an **OCO pair** — two resting orders where
whichever executes first cancels the other, announced like any cancel. A sell
pair brackets an exit: a take-profit limit above and a protective stop below;
a buy pair brackets a breakout: a stop above and a pullback limit below. The
form is one side, one quantity, and the two prices, and it refuses an
inverted layout out loud rather than resting a pair that would fill
instantly. Cancelling either leg cancels both, and the pairing survives a
restart. (Live exchanges will get OCO through their native order types later;
until then the section only appears in paper mode, so a live account can
never hold two secretly-unlinked orders.)

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

The History tab is real on every trading broker — Binance, Kraken, Tradier,
Alpaca, Coinbase, and Schwab all report your recent fills with price, quantity,
and fees. And the terminal watches your back across restarts: when it reconnects
to a broker it compares your positions against what you held last session, and
anything that closed while the app was off is announced — "While you were away
on Kraken: BTC/USD position closed. Sold at 92,300. Profit 1,150." A stop that
fires overnight is never a silent surprise. Cancelled orders speak too ("Order
cancelled for {symbol}") — no order leaves the book without you hearing it.

---

## Automation

Three features let the terminal watch and act so you do not have to stare at every
bar: alerts that speak up when a condition you set is met, strategies that evaluate a
rule set and propose (or place) trades, and custom scripts that let you bring your own
indicator logic. The strategy tools are research instruments — treat them as
exploratory, as the in-app banner says — but the alerts are an everyday convenience.

### Alerts

Press Alt+J for the alerts manager. The top of the dialog is a short form for adding
one: a **Name** (the placeholder suggests "e.g. Price crosses 50000"), a **Target**
of Price, Candle, Indicator, or POC, a **Condition** — crosses above, crosses below,
enters a zone, exits a zone, or changes direction — a **Price Level** to test
against, and a **Delivery** choice of Speech, Earcon, or Both. Fill it in and activate
"Add Alert"; the alert joins the "Active Alerts" list above, where each shows its
name, target, condition, and level with a Delete button. There is no separate edit
step — to change an alert, delete it and add a new one. At the bottom of the dialog,
beside Close, is **Delivery settings** — the email, Telegram, webhook and browser-
notification channels that carry alerts to you when you are away from the terminal.
It opens as a second view of the same dialog, so Escape still closes one thing; "Back
to alerts" returns you to the list, and to the button you left from.

When an alert fires it reaches you immediately. Per its Delivery setting it speaks,
interrupting whatever is being said — "{name}: crossed above {level}. Current value
{value}." — and/or plays an alert earcon, and the event is written to the Journal so
you can read it back. Alerts are never gated by your speech or sonification toggles —
a condition you asked to be told about will always tell you. If you have set up email,
Telegram, or webhooks under **Delivery settings** in this same dialog, fired alerts are
sent there too, so you can be notified away from the keyboard. Webhooks are a **named
list**: add as many as you like — "BTC channel", "Gold channel" — each pointing at a
Discord webhook URL, a Slack incoming webhook, or any custom HTTPS endpoint, and
each alert chooses which one it posts to from a dropdown when you create it. That is
how a Bitcoin alert lands in your #btc Discord channel while a gold alert lands in
#gold. Alerts are also **scoped to a symbol** now: a new alert defaults to the chart
it was created on and only evaluates there, so a BTC alert no longer fires against
whatever chart happens to be on screen (choose "any symbol" if you do want that).

On the **hosted terminal**, alerts on **price and candle patterns** do not need
you at all: symbol-scoped alerts of those kinds keep evaluating on the server
after you close the browser, and anything that fires is delivered through your
configured email, Telegram, and webhook channels. Alerts that read the chart
itself — indicator values, the volume-profile POC, trend and zone conditions,
and advanced condition trees — only run while their chart is open, because the
indicators they read exist only there; the terminal tells you so the moment you
create one, so you always know which alerts are watching while you're away and
which are not. You can also enable **browser notifications** (Alt+J, Delivery
settings, Browser notifications): your alerts then arrive as system notifications on that
device — spoken by your screen reader like any other notification — even with
the terminal tab closed, as long as the browser is running. Turn server-side
evaluation off with the "alerts.serverSide" setting if you'd rather alerts only
run while you are present.

**A limitation worth knowing while you are signed in.** The server steps back
while you have the terminal open, on the assumption that your live session is
watching — but your live session only evaluates alerts for symbols you
currently have on screen. So a price alert on a symbol whose tab you closed is
watched by the server once you sign out, and by nobody while you are signed in
with other charts open. Until that is fixed, the reliable pattern is to keep a
tab open for anything you are actively waiting on, or to sign out and let the
server carry the watch.
And if you enable "send setups to alerts" in the alerts dialog's Delivery settings,
confirmed and armed strategy
setups flow through the same delivery — your Discord channel hears "Long setup —
gold" with the trade plan, even when you're away from the terminal.

For conditions a single rule can't express, switch on **Advanced condition** in
the add-alert form. The same rule-tree builder the strategy composer uses
appears: groups (AND, OR, NOT, or a Score threshold) over leaves that test
indicator components, with optional higher timeframes per leaf — "RSI below 30
AND price above the 200-day EMA" is three clicks. The alert fires the moment
the whole tree first becomes true, then re-arms when it goes false; turn on
repeat-while-active and it re-announces on your cooldown while conditions hold.
Score trees speak their score ("conditions met, score 7 of 9"). Delivery
channels and symbol scoping apply unchanged. One requirement: leaves reference
indicators by code, so add the indicator to the chart before building
conditions on it — which is also why an advanced alert evaluates only while its
chart is open (in a foreground or background tab), never server-side with the
browser closed. The terminal says so when you create one.

### Strategies

Press Alt+S for the Strategy Manager — labelled **EXPERIMENTAL**, with the standing
caveat that backtested results do not guarantee live performance. It opens on a row of
tabs: **Library** (your saved strategies, each with Start/Stop), **Build Setup** (the
composer), **Active** (running strategies, with Pause/Resume), **Backtest**, and
**Custom Script**.

**Your library starts empty, and that is deliberate.** This application ships the
tools — the chart, the indicators, the condition builder, the backtester — and leaves
the choice of what to trade to you. It does not pick a strategy for you and it does
not arrive pre-loaded with someone else's. Earlier versions seeded thirty strategies
from the project's own research on first launch; that was removed, because a shelf of
strategies the application put there itself reads as advice, and of those thirty only
one had ever been tested against a proper control while six had been tested and
*failed*. If you upgraded, nothing was taken away — your library still holds whatever
it held before.

Two ways to fill it. Build one in **Build Setup**, or import a strategy file.

**Importing.** At the bottom of the Library tab, **Import strategies** takes a `.json`
strategy file — either choose the file or paste its contents, whichever suits you —
and adds what it contains. The rules are fixed and worth knowing: importing **never
overwrites** a strategy you already have (a file that contains one you already hold is
skipped, and you are told), it **never starts anything** (everything arrives stopped,
however the file was saved), and it refuses strategies that carry program code, which
belong in the Custom Script tab where you paste the code yourself. Afterwards you hear
the whole outcome in one sentence — how many were imported, how many skipped, how many
rejected, and how many are set to place orders rather than only suggest them.

**The Evidence column.** Every row in the library says how far that strategy has
actually been tested: *Untested*, *In-sample only*, *Walk-forward*, *Control-tested*,
*Fragile*, or *Falsified* — with the detail alongside it, in the Description column:
what it was tested on, which controls were run, and the verdict in a sentence, negative
verdicts included. Strategies you built yourself read **Not recorded**, which is simply
the truth about them. The point of the column is that a tested strategy and an untested
one should never look alike in a list.

To build one, the Build Setup tab gives the strategy a Name and a Side (Long or
Short), then a condition tree you assemble from "+ Group" (AND/OR/NOT) and "+ Leaf"
buttons — each leaf picks an indicator, an operator, and the component to test, with
an optional timeframe for multi-timeframe rules. Beneath it a risk plan sets the stop
source (a percent of price, an ATR multiple, below a swing low, or a fixed price), a
take-profit ladder of one or more rungs, and a stop buffer. Adding the finished setup
to the engine marks it to re-load on the next launch, so your strategies survive a
restart.

A running strategy talks to you as its state changes. When its conditions line up it
rings a setup bell — a bright ascending chord for longs, a heavy descending chord
for shorts, unlike any other sound in the terminal — and speaks the complete trade
plan: "Long setup, {strategy name}, score 0.85. Entry 50,000, stop 49,500, target 1
51,000, target 2 52,500 (R:R 2.50). {why}." Every ladder rung is spoken, so you can
hand the order to the strategy or place it yourself, manually, from what you just
heard. — and if the entry is conditional you will
hear it arm ("waiting for {trigger}") and then report when the trigger is reached.
While the setup holds it heartbeats a quieter reconfirmation, and if a condition drops
away it names what fell off. It also tells you when it is not yet ready: "indicators
warming up — {n} of {m} bars loaded. Signals begin once warm." Every one of these
lands in the Journal's Setups filter for review.

The **Lab** tab brings the research workflow in-app. **Walk-forward windows**
slices the loaded data into equal chronological windows and backtests one
strategy in each — hear whether an edge holds across regimes or lived in one
lucky stretch. **Compare all strategies** backtests every saved strategy on the
first and second half of the data and ranks them; SURVIVOR means the 95%
confidence lower bound on per-trade reward-to-risk is positive in both halves
with at least five trades each — the same statistical gate the offline research
harness applies, because a single positive average on a dozen trades is
indistinguishable from luck. Rankings favour the weaker half: a strategy is
only as good as its weaker regime. Results are tables your screen reader can
walk, plus a spoken verdict.

The Backtest tab runs a strategy over history with realistic settings — starting
capital, commission, slippage, and a warm-up period you can auto-detect — over an
optional date range, including one-press "first half / last half" buttons for
walk-forward testing. Run it and the results read back as trade count, win rate, total
P&L, max drawdown, Sharpe ratio, and an expandable trade log of each entry, exit, and
reason. Because the whole feature is experimental, read those numbers as a study of
the rules, not a promise.

### Background monitoring — watching every tab at once

Normally only the chart on screen is live: switch from your BTC tab to a gold tab and
the BTC alerts and strategies go quiet until you switch back. **Background monitoring**
lifts that limit. Turn it on in Settings (F12), under General, "Monitor background
tabs", and every *other* open tab keeps being watched while you work: its data is
re-fetched on a polling cadence (every 30 seconds by default — adjustable in the same
place, with a floor of 10), its indicators are recomputed, and its symbol-scoped
alerts and running strategies are evaluated against the fresh bars. It is off by
default, like every feature that spends your provider's request budget, and it is a
desktop feature — the hosted web builds stay single-chart by design.

On exchanges whose data feeds support it (Binance today, more as they are
enrolled), you can go one better: **"Live-stream background tabs"**, in the same
Settings section, keeps up to eight background tabs on real streaming data
instead of the 30-second poll. Background alerts and strategies then evaluate on
tick-fresh bars, and switching to a live background tab is instant — the chart
binds its already-current data with no network fetch at all. On exchanges that
cannot stream multiple charts at once, the poll quietly remains — nothing
breaks, it is simply not as fresh.

What you hear follows one simple rule: **events speak from everywhere, the soundscape
belongs to the focused chart.** A background tab's alerts and strategy setups reach
you at full priority — earcons, speech, Journal, and your email/Telegram/Discord
deliveries all fire exactly as if that tab were on screen — and every spoken
announcement is prefixed with its symbol ("BTC/USD: crossed above 50,000") so you
always know which market is talking. But playback, navigation ticks, and the
sonification bed never mix across tabs; only the chart you are actually viewing is
sonified.

Two rules keep this honest. First, an alert or strategy is evaluated by exactly one
side at a time: while its tab is focused, the normal live pipeline runs it; the moment
you switch away, the background monitor takes over — never both, so nothing
double-fires. (Alerts set to "any symbol" belong to the focused chart only, as
always.) Second, background strategy signals are **announce-only**: even a strategy in
Auto mode will speak its signal but never place an order from a background tab.
Order placement stays something that happens on the chart in front of you.

Press **Ctrl+Alt+Shift+M** any time for a status report: it names each watched tab,
how fresh its data is ("current", or "last checked 4 minutes ago", or "data error"),
and how many strategies are armed on it. Monitors start and stop themselves as you
open, close, and switch tabs, and come back automatically when a saved workspace is
restored — there is nothing to manage beyond the one setting.

Each background tab costs one small history request per poll, and every request goes
through the provider's own rate limiter — so many tabs can never blow a provider's
request budget; at worst they queue behind each other and a tab's "last checked" age
grows. If you monitor a great many tabs on one provider, lengthen the poll interval
rather than racing the limiter.

### Custom scripts

When the built-in indicator set doesn't have the one you want, you can write your own.
Press Alt+Comma for the **Custom Scripts** panel. If you can write a little C# — or you
have a PineScript indicator from elsewhere — you can add an indicator that behaves, and
*sounds*, like any of the built-ins. This section walks through writing one from
scratch; deeper authoring (multi-output indicators, full control over how each output
is voiced, or packaging a compiled plugin) is covered in the SDK guide and
`docs/PLUGIN_AUTHORING.md`.

**What a custom indicator is.** Under the hood every indicator is a small class that
takes the price history and returns one or more arrays of numbers — one number per bar,
per line it draws. Your script implements a contract called `ICustomIndicator`, which
is just six things: an `Id` (a short stable code), a `DisplayName` (what you'll hear it
called in the indicator list), the `ComponentNames` (one name per output line), the
`DisplayTypes` (how each line is drawn — and, importantly, how it is *heard*), a set of
`DefaultParameters`, and a `Calculate` method that does the maths. The panel pre-fills a
commented skeleton of exactly these members when you start a new script, so you are
never staring at a blank page.

Two of those deserve a word, because they are where accessibility lives. The
`DisplayTypes` you choose are not only about drawing — they decide the *sound*. Declare
a line as an `Oscillator` and the terminal voices it around its zero line, so you hear
it cross from negative to positive without reading a number; declare it as a `Dot` or
`Arrow` and it becomes a sparse marker you can jump between with Ctrl+Left and
Ctrl+Right. And the `DefaultParameters` you expose show up in the indicator's Properties
dialog (P), so you — or anyone you share the script with — can retune the period or a
threshold later without editing code.

**A worked example.** Here is a complete, working custom indicator: a Rate-of-Change
oscillator that measures how far price has moved, as a percentage, over the last *n*
bars.

```csharp
public class RateOfChange : ICustomIndicator
{
    public string Id => "ROC_CUSTOM";
    public string DisplayName => "Rate of Change";

    // One output line, drawn — and voiced — as an oscillator around zero.
    public string[] ComponentNames => new[] { "ROC" };
    public ComponentDisplayType[] DisplayTypes => new[] { ComponentDisplayType.Oscillator };

    // Appears in the Properties dialog (P), so the period is tunable without code.
    public Dictionary<string, double> DefaultParameters => new() { ["Period"] = 14 };

    public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> p)
    {
        int period = (int)p["Period"];
        var roc = new double[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            // No value until there are `period` earlier bars to compare against —
            // NaN tells the chart "nothing here yet" so warm-up bars stay silent.
            if (i < period) { roc[i] = double.NaN; continue; }
            double prior = data[i - period].Close;
            roc[i] = prior == 0 ? double.NaN : (data[i].Close - prior) / prior * 100.0;
        }
        return new[] { roc };   // one array per component name, each the length of `data`
    }
}
```

A few rules the example shows: `Calculate` receives the **full history, oldest bar
first**; it must return one array per name in `ComponentNames`, and **each array must be
the same length as the input**; and any bar you can't compute yet — the warm-up at the
start — should be `double.NaN`, which the terminal treats as "no value" and skips in
both the drawing and the sound. An indicator with two lines (say a value and a signal)
just returns two arrays and lists two `ComponentNames` and two `DisplayTypes`.

**The workflow.** Press "+ New", give the script a name, and write or paste your class
into the editor. "Save" keeps it in your library. Activating **Compile** builds it in
the sandbox and tells you the result — "Compiled successfully. Indicator: Rate of
Change" when it builds, or a read-aloud list of the compiler errors when it doesn't, so
you can fix them and try again. Once it compiles, "+ Add to Chart" drops it onto the
chart as its own pane, and from that moment it is an indicator like any other: Page Down
to it, Up and Down through its components, set its waveform and bell in Properties (P),
hear it in playback, and jump its signals with Ctrl+Left and Ctrl+Right. "Export .atpkg"
packages the script to a single file you can share or back up.

**Bringing in someone else's script.** The Import controls take an `.atpkg` file from
disk or pasted JSON. Because that is code from outside, the terminal treats it as
untrusted and asks first — "Import untrusted script '{name}'? … It will be sandboxed,
but review the code before pressing Compile." — so you always get a chance to read what
you're about to run. It is imported but not compiled until you choose to; read the
source first.

**From PineScript.** If you have a PineScript v5 indicator, paste it into the "Transpile
from Pine Script v5" box and press Transpile. The terminal converts it to C# you can
then review and Compile, reporting "Transpiled to C# — review and compile." and listing
any conversion warnings. It handles a practical subset of Pine rather than the whole
language, so always read the generated C# and check the warnings before relying on it.

**The sandbox, and one platform limit.** Compiling and running a custom script never
endangers the rest of the terminal: the code runs in an isolated worker process with no
file, network, or reflection access, limited to the charting and indicator libraries,
and on Windows, macOS, Android, and Linux that worker is additionally locked down by the
operating system itself. The one exception is **iOS**, which provides no such sandbox, so
custom-script compilation is disabled there — the editor still works as a text editor,
it just can't run; use the Windows, macOS, or Linux build for scripting. The full design
is in `docs/SANDBOX_DESIGN.md`.

**The sandbox is required, not optional.** If the operating-system sandbox is missing —
on Linux that means the `bubblewrap` package (the `bwrap` command) is not installed —
the terminal refuses to run scripts rather than quietly running them unprotected, and
the compile attempt reports exactly what is missing and how to fix it. On Linux, install
your distribution's `bubblewrap` package and restart. If you genuinely want to run
scripts without the OS sandbox (for example on a locked-down machine you fully trust),
set the environment variable `ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1`; the
terminal will honour it, and it records a security event every time a script runs that
way so the decision is never invisible.

### The Strategy Lab

Everything above runs inside the terminal. The **Strategy Lab** is a separate,
optional command-line research harness that ships in the same repository — the place
where strategy ideas are tested *before* they earn a spot in the application. The
division of labor is deliberate: the lab is for research, the terminal is for
trading. Nothing you run in the lab touches your workspaces, keys, or positions, and
nothing in the terminal depends on the lab being present.

The lab also **owns the strategies**. Its catalogue holds every spec this project has
built — thirty of them, with their bracketed research tags ([v13], [v23] and so on) —
each carrying a record of how far it was actually tested and what the verdict was. None
of them is installed with the application. To put one in your own library you export it
from the lab and import the file, which is the only route in:

```
dotnet run --project AccessibleTrader.StrategyLab -- catalogue list --verbose
dotnet run --project AccessibleTrader.StrategyLab -- catalogue export \
    --out my-strategies.json --id builtin.long.trend-baseline
```

`catalogue list --verbose` prints each strategy with what it was tested on, which
controls were run, and the verdict — read it before you import anything. `--status`
narrows the list to one evidence level (`--status Falsified` is a short and instructive
list). Exporting in bulk by `--min-evidence` deliberately refuses to include anything
recorded as Fragile or Falsified; if you want one of those, for instance to re-test it
yourself, you have to name it with `--id`.

You do not need the lab to validate a strategy day-to-day: the Strategy Manager's
**Backtest** tab has walk-forward first-half / last-half buttons built in, which is
the same honesty check in point-and-speak form. Reach for the lab when you want the
heavier machinery — testing dozens of gate combinations at once, or stress-testing
across many rolling windows instead of one split.

The lab is run from a terminal window with the .NET SDK installed, from the
repository root. `dotnet run --project AccessibleTrader.StrategyLab -- help` lists
every command; the ones that matter:

- `snapshot` — download bar history for a symbol into `strategy-lab-data/` (the
  lab always works from these saved snapshots, so experiments are reproducible).
- `catalogue list` / `catalogue export` — see what the lab holds, what the evidence
  for each spec is, and write a strategy file the terminal can import.
- `cftc-cot` — download CFTC positioning history for named contracts.
- `run` / `walk` — backtest one built-in strategy by its id against a snapshot;
  `walk` splits the data in half and reports each half separately, which is the
  minimum standard of evidence used throughout this project.
- `battery` — run the full grid of entry/gate combinations against a snapshot and
  report which cells survive both halves with statistical confidence.
- `rolling-window` — the strictest test: the same battery across many overlapping
  windows, reporting how *consistently* each cell wins, not just whether it won
  once.

A typical session: fetch a snapshot, `walk` a strategy id you are curious about,
and read the two halves side by side. If the first half and the second half
disagree, the lab just saved you from a strategy that only worked in one era —
which is precisely its job. All output is plain console text, fully readable with
a screen reader, and per-trade CSVs are written for deeper review.

---

## Customizing

The terminal is meant to be shaped to how you work and what you can hear. This chapter
covers the three places you do that shaping: the settings dialog for global
preferences, the sound designer for the audio palette, and tabs and workspaces for
arranging and saving your charts.

### Settings

Press F12 for the settings dialog. It has eight tabs — **General, Speech, Narration,
Sonification, Appearance, Keyboard, License, About** — and Left and Right arrows
move between them.

**Save is what saves, and Escape is Cancel.** Every control in this dialog holds your change
until you press Save; Save writes them all at once and says "Settings saved", and Cancel — or
Escape, or a click on the backdrop — drops them all and says "Settings discarded". You can
change your mind about anything in here without consequence, and you never have to wonder
whether a box you ticked and then thought better of has already taken effect.

There is one deliberate exception, on the **Appearance** tab. The theme, the text size and the
visual accommodations (chart formations drawn, visual earcons, colour-vision-safe colours,
hollow up-candles) apply the *moment* you pick them, because a visual setting you cannot see
while you decide is a setting you cannot judge. Cancel restores them as well, and says
"Settings discarded. Appearance restored" so you know it did.

**Speech** and **Narration** are split by a rule worth learning, because it tells you which
tab to go to without hunting: **Speech is how the terminal says what you asked for, and
Narration is what it says when you pressed nothing.** So Speech holds whether it speaks
timestamps and when, whether it reads column headers, the order it reads a value and its
name in, and which voice talks on this device — every one of them a change to the answer you
get back from a key you pressed. Narration holds the three things that speak on their own:
the new-bar announcement, signal narration on a bar close, and narration during playback.

Two settings you might expect on Speech or Narration are on **General**, under *Analysis*:
**Describe candle patterns** and **Describe chart patterns**. Neither belongs to a single
trigger — each changes what the arrow keys say *and* what a bar close says *and* what
playback says — so filing either by trigger would put it on a tab whose rule it breaks.
(That sentence was aspirational for the candle switch until 2026-09-04, when the arrow keys
started naming candle patterns at all; it is now simply true.) They
are filed by what they are, an analysis the terminal either performs or does not, and they
sit next to each other because that adjacency is what makes the difference between them
legible: a **candle** pattern is one to three bars (engulfing, harami, doji, hammer), a
**chart** formation is tens of them (double top, head and shoulders, triangle). Both answer
to "narration" and "speech" in the settings search box.

**Sonification** mirrors F3 and holds the sound theme, the sound-under-the-mouse option and
the two **earcon families** described below.
**General** is where you switch **paper trading mode** on and choose what the quick-trade
risk percentage means, plus workspace, background-monitoring, braille, touch-bar, analysis,
drawing and viewport preferences — and the **factory reset**, also below. **Appearance** is the theme — most relevant to a sighted
collaborator looking over your shoulder — and the visual accommodations, plus the
**visual profile** export/import. The **audio profile** export/import is on Sonification;
each sits on the tab whose settings it writes.

**Save and Cancel.** The dialog commits when you press **Save**, and only then. **Escape,
the Cancel button and a click outside the dialog all discard** — that is the rule everywhere
in the terminal now, and it was not always true here: until 2.6.0 this dialog had a single
button reading *Close*, and closing is what saved. A few controls still take effect the
moment you change them, and each of them says so as you do: the theme picker, interface
scale, panning step, the paper-account reset, and the visual accommodations.

#### The two earcon families (Sonification tab)

Earcons — the short tones that stand in for a sentence — come in two families, and each has
its own checkbox under **Shift+F3**, which remains the single mute over both.

- **Market earcons** are the ones about the market: an alert firing, a new bar opening, a
  strategy setup arming or reaching its entry.
- **Interface earcons** are the ones about the terminal: the edge of the chart or of a series,
  a mode toggled with F2 or F3, an action that succeeded or is being retried, the connection
  changing state.

Both start on, so an install that never opens this tab sounds exactly as it always did. They
are worth separating because they fire at wildly different rates for wildly different reasons:
the boundary tone sounds on *every further arrow press* at the edge of a chart, and a setup
bell might fire twice in a session. One switch used to govern both, so quietening the first
meant losing the second. Every interface earcon is also **spoken**, so turning that family off
costs you the beep and nothing else.

**Neither switch reaches error tones or order outcomes** — a fill, a stop, a take-profit, and
every error. Those sound with both families off *and* with Shift+F3 muted. There is no
compensating channel for an error you cannot see, and money moving is neither a market
observation nor an interface confirmation.

#### Factory reset (General tab)

"Reset all settings to defaults" arms a confirmation the same way the paper-account reset
does, and for the same WCAG 3.3.4 reason. It puts back to shipped defaults: every setting in
this dialog, every keyboard rebinding, your own themes, your sound patches and earcon
assignments, and the colours and sounds you gave individual indicators.

**It keeps your API keys, your paper trading account and its history, and your saved
workspaces**, and the confirmation says so — because "all personalization will be lost" is a
sentence most people will read as "including my broker logins", and being wrong about that in
the frightening direction is what stops someone using a button they need. Your keys are
credentials rather than preferences and nothing on this machine could rebuild them; the paper
account is a trading record with its own reset a few rows above; your workspaces are documents
you named and can delete by name.

Every part of the reset is attempted even if one of them fails, and you are told how many
failed rather than simply "done" — a reset that stopped at the first problem would leave you
with a keyboard from one era and preferences from another and no way to tell which. Restart
the terminal afterwards so every part of it re-reads the defaults.

#### The Narration tab

Three switches, and the two new ones are both **on** to begin with. That is deliberate and it
is not the terminal being chatty at you: neither switch lets anything new through on its own.
Signal narration only ever speaks about series and components you flagged yourself with N, so
on a chart where you have flagged nothing it has nothing to say; and playback narration is
on because it is what the terminal already did. What is new is that you can now turn either
of them **off**.

- **Announce new bars** — the rolling "Close … New bar …" heartbeat of the live candle. It
  moved here from the Speech tab; nothing about it changed.
- **Narrate signals on bar close** — the master switch over auto-narration. N chooses *which*
  series and components speak; this decides whether any of them do. Turn it off for an hour of
  quiet without having to un-flag six indicators and then remember which six they were.
- **Narrate during playback** — the master switch for everything playback says beyond its
  own start, pause, speed and finish confirmations: the date or hour as the tones cross each
  boundary, the signals your narrated series print, and any chart formation that resolves.
  Off leaves playback as pure tones. Nobody had that switch before 2.6.0.
- **Speak time landmarks during playback** — the date or hour alone, on by default and
  subordinate to the switch above. It is the only thing that says where in time the tones
  are, and it is also a calendar read to you every few seconds; turning it off keeps the
  signals and the formation outcomes and stops the dates.

There used to be an eighth tab, **Alerts**, holding the email, Telegram and named-webhook
delivery details. Those moved into the alerts dialog itself (Alt+J, then **Delivery
settings**), next to the alerts they deliver — the webhook names you define there are the
choices in an alert's Webhook list, and having the two a dialog apart meant closing the
alert you were writing to go and define its destination.

#### Appearance and the theme editor

The appearance section is easy to skip if you never look at the screen, and worth a
minute if you sometimes do, or if anyone ever looks at your screen with you.

A theme covers the **whole window**, chart and chrome together, so switching one never
leaves a themed chart sitting inside a fixed grey frame. **Classic** is the default.
**Blackout** is pure black with white text, for OLED panels and for anyone who finds any
lit background tiring; **High-contrast dark** and **High-contrast light** are the two
built for maximum separation. **Paper** is a real light theme — warm off-white, near-black
ink — meant for daylight, projectors and printing. **Amber CRT**, **Walnut**, **Midnight
Blue**, **Classic**, **Soft Dark**, **Solarized** and **Braille** fill in the rest. The high-contrast pair are
deliberately *not* the default: they are accessibility tools, and greeting every new user
with black-and-white made a finished application look like a debug harness. They are one
setting away and unchanged.

Beyond picking one, three buttons open the theme editor and mean what they say. **New
theme** starts a raw theme from a plain scheme — black chart, green rising candles, red
falling candles. **Clone theme** copies whichever theme the picker shows, built-in or your
own, into a new theme you can rename. **Edit theme** changes one of your own themes in place;
on a built-in it stays where it is and tells you why ("Built-in themes can't be edited. Use
Clone to make your own copy.") rather than vanishing. Whichever way in, you get all 33
colours the application can draw, grouped into sections you can jump between with your
screen reader — top bar, chart area, candles, overlays, bottom bar, dialogs, text. The chart
background and its gradient are the **Chart top** and **Chart bottom** colours there, and a
**Blend into one gradient** group at the top of the editor colours the toolbars, chart and
footer as one fade from a top colour to a bottom colour, six fields at once, each of which
you can still change or revert afterwards. Every picker carries a description of what the
colour actually affects, because "gridlines, minor" means nothing on its own, and every change
is announced as you make it — *"Rising candle set to #26A69A."* — so you know it took without
needing to see it. Where two colours you have chosen are too close to read against each
other, the editor **says so inline and leaves them alone**; it never quietly corrects your
choice. **Reset all** returns to the base theme, and **Save and use** keeps your version as a
named theme of your own, which saves and loads as text you can send to someone else.

Before 2.6.0 the Appearance tab also held a chart background colour, a gradient, a window
gradient switch and a bullish/bearish colour pair that were layered over *every* theme.
Those are gone: a colour belongs to a theme now, and anything you had set there is ignored.
To get the same look back, Clone the theme you use and set the colours in the editor.

The **Analysis** group on the general tab holds one preference worth knowing about:
**Add Market Structure (swing highs and lows) to new charts**, which is on by
default and is why every chart you load already has swing labelling on it. Untick
it if you would rather add that indicator per chart. It changes what happens on the
*next* load and deliberately leaves charts you already have open alone — silently
stripping an indicator off a chart you were reading would be a worse surprise than
the setting waiting one load to take effect.

**Visual accessibility options.** Under the theme, the Appearance tab carries a group of
visual accommodations. All of them are **off by default** — the terminal presents itself
audio-first, and these exist for users who want a visual channel too. Each applies
and saves the moment you toggle it:

- **Visual earcons** mirrors every sound cue — order fills, stops, take-profits,
  setups, errors, new bars, connection changes — as a brief on-screen badge naming
  the event, for deaf and hard-of-hearing traders or anyone working with the sound
  down. Each event fades in and out once; nothing ever flashes repeatedly.
- **Color-vision-safe chart colors** replaces the red/green up-down convention with
  blue for up and orange for down on candles and direction-coloured bars — the two
  hues remain distinct with deuteranopia or protanopia. While on, it deliberately
  overrides any per-indicator direction colours so one switch covers the whole chart.
- **Hollow up-candles** draws rising candles as outlines and falling candles filled,
  making direction readable by shape alone, with any colours.

The same group holds a **Text size** selector (85% to 175%) that scales the
interface text throughout the terminal — browser zoom still works on top of it. And
at the top of the whole dialog there is now a **Search settings** box: type a word
like "speech", "theme", or "alerts" and matching settings are listed with the tab
they live on; choose one and the dialog jumps there and focuses the control, so you
never need to remember which of the eight tabs holds a setting.

Two further accommodations need no switch at all: if your operating system or browser
is set to **reduce motion**, the terminal's animations and transitions are disabled
automatically; and on touchscreens, buttons and tabs enlarge automatically to
comfortable touch sizes. On high-resolution screens the chart image itself now
renders at your display's native pixel density, so candles and axis text are sharp
rather than softly upscaled. During playback, the highlighted bar on screen follows
the audio bar for bar, so a sighted companion can watch what you are hearing.

### The sound designer

Two layers of audio control sit underneath settings. The first is each indicator's
properties dialog (P). Its **Sonification** tab has an **Acoustics** section for the
component you pick there, and at the top of it a **Sound Patch** dropdown chooses the
voice that component plays — any built-in patch (the bells and more) or any patch you
have made yourself — with a ▶ Preview button beside it to hear it. The second and third
dropdowns adapt to what the component actually is: price bars get **Green
(bullish)** and **Red (bearish)** patches so rising and falling bars can sound
different; zero-anchored histograms and areas get **Positive** and **Negative**
patches split at their baseline; and bounded oscillators like the RSI get
**Above midline** and **Below midline** patches split at the middle of their
range (RSI 50). Plain lines show only the single patch. Whatever patch you
choose, the overbought/oversold **zone texture** from the Reference Levels
section still plays on top — a patch changes the instrument, never the zone
cue. Leave a patch
unset and the older manual controls — waveform, noise, volume — take over as a
fallback, and "Save as Defaults" still makes your choices stick for the next
indicator of that type.

The second layer is the **sound designer**, opened with Alt+W, where the patches
themselves are built. It is now a general-purpose patch workbench rather than an
earcon-only panel. A single patch can stack several **oscillators**, each with its
own waveform (sine, square, sawtooth, triangle, or noise), level in the mix,
frequency ratio (a harmonic multiple of the patch's base pitch — 2.0 is an octave
up), and noise blend and noise colour (pink, white, or brown); the "Add Oscillator"
button layers on another. A Mix section sets the base frequency, a frequency
multiplier, and overall volume; an Envelope section chooses a sustained tone or a
plucked Ping and its duration. The Preview button auditions the whole patch — noise
and envelope included, not just the bare waveform as before. A patch you build here
can be assigned to event earcons in this same panel, or to indicator components
through the properties dialog above, and the link is live: edit a patch in the sound
designer and every component and earcon using it updates at once. Patches saved by
older versions still load unchanged. Think of the properties dialog as choosing which
instrument each part plays and the sound designer as building the instruments.

New in this panel: **Import WAV**. A short single-cycle WAV file — the free
public-domain AKWF collection is thousands of them, or one period of any
recorded instrument — imports as a **wavetable**: a custom oscillator waveform
that plays at any pitch and takes envelopes, noise, and layering exactly like
the built-in shapes. A longer WAV imports as a one-shot **sample** for earcons
and signal layers, played at its natural speed. Both appear in every
oscillator's waveform list ("Wavetable: …" and "Sample: …") and survive
restarts. If an import ever goes missing, patches that reference it fall back
to a plain sine — audible, never silent.

The earcon list in this panel now includes the three reference-level cues — the
crossing chirps (up and down separately), the approach ping, and the
sustained-in-zone tone — so those can be re-skinned with any patch too. And a
factory bank of ready-made **instrument voices** ships alongside the built-in
bells: flute, clarinet, pipe-organ registrations, glass, and string-ensemble
patches (they appear as "Voice: …" in every patch dropdown). They exist for the
sound themes below but you can assign them anywhere by hand.

**Sound themes.** Settings (F12), Sonification, "Sound theme" assigns those factory
voices automatically, one instrument per indicator *family*: with the Orchestra
theme, price and moving-average lines are a flute, bounded oscillators like the
RSI a clarinet, zero-cross indicators like the MACD a pipe organ, and band edges
glass. The point is playback: press Space on a busy chart and you can tell which
line is talking by its instrument alone. The Pipe organ and Strings themes voice
the same families with different registrations of a single instrument, and
Classic is the original pure-tone palette. A theme applies to indicators you add
after choosing it, never touches candles, wicks, or volume (their sound carries
size information that a fixed instrument would erase), and any per-component
patch you pick in a Properties dialog always wins.

### Tabs and workspaces

You can keep several charts open at once in tabs. Ctrl+T opens a new chart tab, Ctrl+W
closes the current one, and Ctrl+Tab and Ctrl+Shift+Tab move to the next and previous
tab — so you might hold BTC on the hourly in one tab and a stock index on the daily in
another, and flip between them without reloading. A row of tabs sits just above the
chart and is always visible, even when only one tab is open, so the "+" new-tab button
is always there for the mouse. (On the Linux web host the browser claims Ctrl+T, Ctrl+W,
and Ctrl+Tab for its own tabs, so there they are replaced: press Alt+Shift+N to open a
new chart tab, and press Ctrl+Alt+Shift+T to move keyboard focus onto the tab switcher
bar. Once the bar has focus, switch with the left/right arrow keys, Home and End, or the
number row (1–9 jump straight to that tab); press Insert to open another tab and Delete
to close the focused one. The bar is an ARIA tablist, so your screen reader announces
each tab as you move. See the Platform Support chapter.)

Out of the box only the tab on screen is live — but if you want the others watched
too, turn on background monitoring in Settings and their alerts and strategies keep
evaluating while you work elsewhere, each announcement prefixed with its symbol. That
feature has its own section in the Automation chapter.

A whole arrangement — every tab, its symbol and timeframe, its indicators and drawings
— is a workspace you can save and restore. Ctrl+Alt+Shift+W saves the current
workspace and Ctrl+Alt+W loads one back; because those are three-modifier chords the
browser does not reserve them, so they work the same on every platform. Set up the
charts and indicators you return to every session once, save them, and you are one
shortcut from that whole layout the next time you sit down.

A workspace save captures every tab's identity (market, provider, symbol,
timeframe), the indicator stacks with their settings and audio patches, your
drawings, display toggles like Heikin-Ashi and log scale, pane heights, and the
strategies that were RUNNING — each remembered with its symbol binding, its
Suggestion/Auto mode, and whether it was paused, so loading the workspace brings
them back exactly as they were. Alerts are not part of a workspace; they persist
on their own the moment you create them. Bar data is refetched fresh on load.

You also do not need to remember to save at all: the terminal **autosaves your
session** every thirty seconds and again when it closes, and the next start
simply resumes it — you hear "Resumed your last session: N tabs" and you are
back where you left off, tabs, drawings, strategies and all. This covers the
browser-refresh case on the web build too, which previously lost everything
unsaved. If you prefer a blank start, turn off Settings > Workspace > "Resume
last session at startup"; explicit named workspaces are untouched either way.

---

## The Tactile Display

Alongside speech and sound, Accessible Trader can drive a **refreshable tactile
graphics display** — a pad of pins that rise and fall so you can read the shape of the
chart with your fingers. **All Dot Pad models are supported** — including the **Dot Pad
X** (the newest model) and the **second generation** — because they share the same
graphics SDK, so the driver works across the family. Each is a grid of thirty by ten
graphic cells — sixty by forty individual pins — with a separate twenty-cell braille
text strip beneath it. (On-device testing so far has been on the second generation; the
Dot Pad X uses the same SDK and is supported.) Tactile output does not replace the speech
and the soundscape; it is a third layer you read at the same time, and it is the one
that gives you the chart's *form* directly under your hand rather than as pitch over
time.

Tactile support is a Windows feature today, because the device's graphics need the
vendor's Windows driver; the Linux web host can't drive the pins yet (the vendor's
Linux library has no graphics support — see the project docs for the current state).

### Turning it on

Tactile output is off until you ask for it. Open Settings with F12 and, on the General
tab, tick **"Enable braille / tactile display output."** With it on, the terminal looks
for a connected Dot Pad as it starts, and keeps watching while it runs — so you can plug
the display in at any time and hear it announce itself, "Dot Pad connected," and unplug
it to hear "Dot Pad disconnected." Turning the setting back off stops all of this and
skips device detection entirely. The toggle is deliberately opt-in: looking for the
display means probing serial ports, which is the sort of thing you only want happening
when you actually have one.

### Reading the chart by touch

When a chart is loaded the pad shows it as two stacked panes — the focused series and
the one above it in the cycle — with candles drawn as a body, a wick, and a gap so the
bars are distinct under your fingers, and indicators drawn as the line, oscillator, or
markers their type calls for. Beneath the graphic, the twenty-cell strip carries the
live value of wherever your cursor is, switching for about a second and a half to the
bar's timestamp each time you move with the left or right arrow before falling back to
the value.

The graphic redraws when you *navigate* — change focus, switch panes, zoom, pan — but
deliberately **not** on every live tick. Each tactile frame takes a second or two of
physical pin movement, faster than ticks arrive, so redrawing on every tick would leave
the pad permanently in motion and unreadable. The stable graphic stays put under your
hand while the fast-moving live value rides on the strip, which is the right surface for
it. Before any chart is loaded the pad reads "accessible trade terminal ready" and the
strip "no chart loaded…".

### The device keys

The Dot Pad's own four function keys and its panning keys are wired into the terminal so
you can keep both hands on the pad. **F1** speaks the focused series — "candles" for the
price pane, or the indicator's name. **F2** speaks the focused component within it.
**F3** speaks the chart's identity — symbol, timeframe, and provider. **F4** freezes the
graphic so you can study a frame without it redrawing under you, and frees it again on a
second press; the strip keeps updating either way. The display's **pan** keys scroll the
chart left and right exactly as the `[` and `]` keys do. Each of the function-key
answers is also written to the braille strip, so you can read what F1 to F3 reported as
well as hear it.

Setting up the hardware itself — installing the Dot Pad SDK, the supported connection,
and calibration — is covered in the project's platform documentation rather than here.

---

## Platform Support

Accessible Trader runs two ways: as a native desktop and mobile app on Windows,
macOS, iOS, and Android, and as a self-hosted browser application — the Linux web
host — that you reach in a browser such as Firefox. The keyboard navigation, the
Hybrid Voice model, and everything in the preceding chapters are the same everywhere;
what differs is the plumbing underneath and a single block of keyboard shortcuts on
the web host.

- **Windows** — works with NVDA, JAWS, and Narrator; uses the WASAPI audio engine for
  the lowest latency; needs a full hardware keyboard for the shortcuts. The Windows
  build is also the one that drives a Dot Pad tactile display when one is connected.
- **macOS** — works with VoiceOver, uses the AVAudioEngine audio path, full keyboard
  support.
- **Android** — works with TalkBack and the AudioTrack engine; the keyboard shortcuts
  are available when a physical keyboard is connected.
- **iOS** — works with VoiceOver and AVAudioEngine; shortcuts require a connected
  hardware keyboard, and, as the Automation chapter noted, custom-script compilation
  is disabled because iOS provides no process sandbox.
- **Linux (web host, in a browser)** — works with Orca and other browser-compatible
  screen readers, with audio routed to your system through PipeWire or PulseAudio (or,
  on a remote/demo deployment, streamed to the browser).

### Which version to use

The native app and the web host are not rivals so much as two doors into the same
terminal, and which one you want is mostly decided by your operating system. **On
Windows and macOS, use the native app** — it gives you the deepest integration: the
lowest-latency native audio, a chart that redraws at full speed, and your credentials
held in the operating system's own keychain. **On Linux, use the web host** — there is
no native Linux build, and the web host is an excellent first-class client, speaking
through Orca and playing through PipeWire or PulseAudio; it is also what powers the
public chart demo on the website. And **on a phone or tablet, the native app is the only
option** — there is no way to put the web host in your pocket.

Two capabilities tip the balance toward the native Windows app in particular. The
**tactile display** described in the previous chapter is Windows-only, so a Dot Pad user
wants that build. And the safety sandbox for custom scripts is fullest on the native
desktop platforms. Everything else — every chart, indicator, drawing tool, alert,
strategy, trade, and the whole Hybrid Voice model — works the same on both, so if you
move between a Windows desktop and a Linux laptop you are using the same terminal in both
places, with only the plumbing underneath and the one block of shortcuts below changing.

### The web host modifier remap

The one place the keyboard genuinely differs is the browser. Firefox and most
browsers reserve several `Ctrl+Shift+<letter>` chords for themselves — `Ctrl+Shift+T`
reopens a closed tab, `Ctrl+Shift+P` opens a private window, and so on — and a web
page cannot override them. So on the web host **every `Ctrl+Shift+<letter>` chord is
remapped to `Alt+Shift+<letter>`**: the same letter and the same command, just a
different modifier. This affects all the drawing tools and the detailed point summary
(`Ctrl+Shift+D` becomes `Alt+Shift+D`). Chords with three modifiers — the AI Analyst,
auto-narration, the Journal, save and load workspace — are **not** remapped, because
browsers do not reserve them. A few single-`Ctrl` chords are reserved by the browser at
an even deeper level — it acts on them before the page ever sees the keystroke, so they
cannot be cancelled in-page at all. On the web host these are dropped from the bindings
(so the Help dialog never lists a chord the browser eats) and the action moves elsewhere:
open a new tab with `Alt+Shift+N` or the tab bar's **+** button instead of `Ctrl+T`;
close a tab with its **×** button or by focusing the bar and pressing Delete instead of
`Ctrl+W`; switch tabs by pressing `Ctrl+Alt+Shift+T` (a three-modifier chord browsers
leave alone) to focus the tab switcher bar, then the arrow keys, Home/End, the number
row, Insert (new) or Delete (close) instead of `Ctrl+Tab`. Pane navigation needs no such
rule: it is `Alt+PageUp` / `Alt+PageDown` on every head, and `Ctrl+PageUp` / `Ctrl+PageDown`
— which the browser uses to cycle its own tabs — is left unbound rather than reassigned.
You never have to memorise which is which —
the Help dialog (F1) always lists the bindings actually in effect on the host you are
using, so it self-documents per platform.

| Tool / command | Desktop & mobile | Linux web host |
|---|---|---|
| Trendline | Ctrl+Shift+T | Alt+Shift+T |
| Horizontal line | Ctrl+Shift+H | Alt+Shift+H |
| Vertical line | Ctrl+Shift+V | Alt+Shift+V |
| Channel | Ctrl+Shift+C | Alt+Shift+C |
| Fibonacci retracement | Ctrl+Shift+F | Alt+Shift+F |
| Fibonacci extension | Ctrl+Shift+E | Alt+Shift+E |
| Text label | Ctrl+Shift+L | Alt+Shift+L |
| Rectangle | Ctrl+Shift+R | Alt+Shift+R |
| Measure | Ctrl+Shift+M | Alt+Shift+M |
| Andrews' pitchfork | Ctrl+Shift+A | Alt+Shift+A |
| Gann fan | Ctrl+Shift+G | Alt+Shift+G |
| Gann box | Ctrl+Shift+B | Alt+Shift+B |
| Angle | Ctrl+Shift+J | Alt+Shift+J |
| Risk/Reward | Ctrl+Shift+P | Alt+Shift+P |
| Anchored VWAP | Ctrl+Shift+W | Alt+Shift+W |
| Detailed point summary | Ctrl+Shift+D | Alt+Shift+D |
| New chart tab | Ctrl+T (or Alt+Shift+N) | Alt+Shift+N (or the tab bar's + button) |
| Close chart tab | Ctrl+W | Tab's × button, or focus the bar + Delete |
| Switch chart tabs | Ctrl+Tab / Ctrl+Shift+Tab | Ctrl+Alt+Shift+T, then arrows / Home / End / 1–9 |
| Move between panes | Alt+PageUp / Alt+PageDown | Alt+PageUp / Alt+PageDown |

---

## Glossary

Brief definitions of the terms used throughout this manual, in alphabetical order. They
are deliberately short — enough to keep you moving if a word in a chapter was unfamiliar,
not a substitute for a course in the markets themselves.

**Anchored VWAP.** A VWAP (see *VWAP*) calculated forward from one bar you choose, rather
than from the start of the session.

**Ask.** The lowest price a seller is currently willing to accept. The counterpart to the
*bid*; the gap between them is the *spread*.

**Bar / Candle.** One unit of the chart, summarising price over one *timeframe* by its
open, high, low, and close (see *OHLC*). "Candle" refers to the common visual form of a
bar.

**Bid.** The highest price a buyer is currently willing to pay. The counterpart to the
*ask*.

**Bracket / OCO.** A pair of protective orders attached to a position — a *stop-loss* and
a *take-profit* — where filling one cancels the other ("one cancels the other").

**Crossover.** The moment one line passes through another — for example a fast moving
average crossing a slow one, or an oscillator crossing its zero or signal line. Often
read as a momentum signal.

**Cross / Isolated margin.** Two ways an exchange backs a leveraged position. Cross
margin shares your whole balance as collateral across positions; isolated margin ring-
fences a set amount to one position, capping what a single trade can lose.

**Divergence.** When price and an indicator disagree — price making a higher high while
the indicator makes a lower high, or vice versa — often read as weakening momentum.

**Earcon.** A short, distinct sound used as an event signal — a modal opening, an alert
firing, a boundary reached — as opposed to the continuous sonification of values.

**Fibonacci retracement / extension.** Horizontal levels placed at standard ratios
(23.6%, 38.2%, 50%, 61.8%, 78.6%) of a price move, used to anticipate where a pullback
might pause (retracement) or where a continuation might reach (extension).

**Fill.** An execution of an order, in whole or in part. A "partial fill" completes only
some of the requested quantity.

**Heatmap.** An overlay that shades each bar by relative volume so you can see — and, in
playback, hear — where trading was most active.

**Heikin-Ashi.** A smoothed candlestick formula that averages price to reduce noise and
make trends easier to follow.

**Hybrid Voice.** This terminal's core model: your screen reader speaks exact values
while the built-in engine sonifies the shape of the market, the two heard together.

**Leverage.** Trading a position larger than your cash by borrowing from the exchange,
expressed as a multiple (e.g. 10×). It magnifies both gains and losses and introduces a
*liquidation* price.

**Limit order.** An order to buy or sell at a specified price or better, rather than at
whatever the market currently offers (compare *market order*).

**Liquidation.** The exchange's forced closure of a leveraged position when losses
threaten the borrowed funds — the price at which this happens is the liquidation price.

**Log scale.** A price axis where equal vertical distance means equal *percentage* move,
useful over long histories where price has changed by large multiples.

**Long / Short.** A long position profits when price rises; a short position profits when
price falls. In hedge mode an account can hold both sides at once — the *position side*.

**Market order.** An order to buy or sell immediately at the best available price
(compare *limit order*).

**Moving average.** The average price over a number of recent bars, redrawn each bar — a
smoothed line used to read trend direction and as a dynamic support/resistance level.

**OHLC.** The four prices that define a bar: Open (first trade), High, Low, and Close
(last trade) of the period.

**Oscillator.** An indicator that moves around a centre line (such as RSI or MACD),
measuring momentum or the speed of price change rather than price itself.

**Overbought / Oversold.** Zones at the extremes of an oscillator suggesting price may
have risen (overbought) or fallen (oversold) faster than is sustainable. Not by
themselves a signal to act.

**Paper trading.** A simulated mode that fills orders against the live price with
imaginary money, so you can rehearse the whole workflow without risk.

**Point of Control (POC).** In a volume profile, the price level that saw the most
trading volume.

**Position.** An open holding in a market — long or short — with a size, an entry price,
and a running *unrealized* profit or loss until it is closed.

**Post-only / Maker.** An order that will only rest in the book (adding liquidity, paying
the lower "maker" fee) and is cancelled rather than executed if it would fill
immediately.

**Realized / Unrealized P&L.** Profit or loss that has been locked in by closing
(realized) versus the running figure on a position still open (unrealized).

**Reduce-only.** An order flag that can only shrink or close an existing position, never
open or enlarge one — a guard against accidentally flipping direction.

**Sonification.** Turning data into non-speech sound — here, mapping price and indicator
values to pitch, timbre, and stereo position so you can hear the chart's shape.

**Sound patch.** A reusable, named voice built in the sound designer (Alt+W) — one or
more layered oscillators plus noise and an envelope — that can be assigned to an event
earcon or to an indicator component. Editing a patch updates everything using it.

**Spread.** The gap between the best *bid* and the best *ask*.

**Stop-loss.** A protective order that closes a position once price reaches a level
working against you, capping the loss.

**Stop / Stop-limit order.** An order that activates only once price reaches a *trigger
price* — then submitting as a market order (stop) or a limit order at a set price
(stop-limit).

**Support / Resistance.** Price levels where falling tends to stall (support) or rising
tends to stall (resistance), often where prior turning points or heavy volume sit.

**Take-profit.** An order that closes a position once price reaches a favourable target,
locking in the gain. A "ladder" splits this across several targets.

**Time-in-force (TIF).** How long an order stays live: GTC (good-till-cancelled), IOC
(immediate-or-cancel — fill what you can now, cancel the rest), or FOK (fill-or-kill —
all at once or nothing).

**Timeframe.** The period each bar covers — one minute, one hour, one day, and so on.

**Trailing stop.** A *stop-loss* that follows price as it moves in your favour, locking in
gains, and holds its level when price reverses — set by an amount, a percentage, or a
callback rate.

**Trigger price.** The price at which a stop, stop-limit, or trailing order becomes
active.

**Value Area.** In a volume profile, the band of price levels that together account for
roughly 70% of traded volume.

**VWAP.** Volume-Weighted Average Price — the average price over a span weighted by volume
at each level, used as a fair-value reference (see also *Anchored VWAP*).

**Volume profile.** A view of how much volume traded at each price level (rather than over
time), highlighting the *Point of Control* and *Value Area*.
