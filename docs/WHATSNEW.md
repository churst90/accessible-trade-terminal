# What's New

## 2.2.0 — the chart describes its own shapes

- **The terminal can now name chart formations as you move through them.** Turn on
  *Settings → Describe chart patterns*. Twelve shapes — double top and bottom, head and
  shoulders and its inverse, three kinds of triangle, two wedges, two flags, and the
  horizontal range. You hear them the way you would read them: *"Start of possible double
  top forming, neckline 42,100, measured target 39,400 if it breaks. Spans 22 bars."*
- **You are told which side of the formation you walked in from.** "Start of" going
  forward, "End of" going back, so you always know where you are inside a shape rather
  than just that you are inside one.
- **And you are told what price actually did.** The word "completed" is gone — it could
  not tell you whether a pattern worked or failed, and it never meant either. Instead you
  hear *"confirmed here: closed **below** the neckline at 42,100"*, or *"ends here without
  confirming — the neckline **held**"*. Which side, which level. What it means is yours to
  decide, and the manual explains how the two readings differ.
- **Comma and period step between formations.** Two keys walk you through every shape on
  the chart in the order it happened, landing on the bar each one starts and the bar each
  one resolves.
- **When several shapes fit at once, you hear the main one and a count** — *"plus 2 more
  formations here"*. Traders disagree about these; the terminal is not going to pretend
  otherwise by silently picking one. `Alt+Shift+D` reads them all.
- **What is special about a bar is now spoken before the bar's value.** Support zones,
  structure breaks, formations — they lead, and the routine value follows. When you are
  scanning quickly, the first few words are what you actually hear.
- **Indicator signals now follow you around the chart.** Previously a support zone or a
  break of structure went silent the moment you moved focus off price. Each indicator can
  opt out in its Properties if its signals only make sense inside its own pane.
- **Fixed: only one thing was ever spoken per keypress.** If a bar had a formation *and* a
  signal *and* a value, you heard one of them and never knew the others existed.
- **New: `Alt+I` opens a dossier on whatever is on your chart.** For a coin: identity,
  supply and dilution, whether anyone is still committing code, and whether it discloses a
  whitepaper, source and explorer at all. For a company: filings straight from SEC EDGAR.
  Where something is missing, it says so — because for a brand-new token, "nothing here can
  be verified" is the most useful sentence on the screen.
- **Fixed: the dossier's Speak summary button did nothing.** It was calling a function that
  did not exist, and the failure was being swallowed.
- **Fixed: arrowing left into a formation said "End of" when you were at its start.** The
  edge word now describes the bar you are on, not the direction you came from — so a
  formation's first bar always says "Start of" and its last always says "End of", whichever
  way you reached them.
- **Fixed: several keys explained themselves to nobody.** "No more signals in this
  direction", "Focused trendline has no anchors" and a handful of others were being
  produced and then thrown away before anything spoke them. Ten messages in total, some of
  which had never been heard since the day they were written.
- **Fixed: comma and period jumped you around the chart with pattern description turned
  off**, without saying why. They now tell you to turn the setting on.
- **Heikin-Ashi now says what it does not change.** Formations are read from standard
  candles even when Heikin-Ashi is displayed, because a Heikin-Ashi price is an average
  rather than something that traded — and a level you might put in an order has to be real.
  Switching it on now tells you.

## Unreleased — the app stops recommending strategies

- **The strategy library ships empty now.** A fresh install opens the Strategy Manager
  with an explanation and two routes — build one, or import one — instead of thirty
  strategies from this project's own research. **If you are upgrading, nothing is
  removed:** your library is exactly as you left it. The reason is simple. A shelf of
  strategies the application put there itself reads as advice, and when they were
  scored honestly only one of the thirty had ever beaten a real control, while six had
  been tested and *failed*. Shipping that as a starting library was an endorsement
  nobody had earned. (The auto-recommendation that used to highlight a "recommended"
  strategy per symbol went in the same clear-out.)
- **Every strategy now says how far it has been tested.** A new Evidence column in the
  library: *Untested*, *In-sample only*, *Walk-forward*, *Control-tested*, *Fragile* or
  *Falsified* — with what it was tested on, which controls were run, and the verdict in
  a sentence, alongside the description. Strategies you built read **Not recorded**,
  which is the truth about them. A tested strategy and an untested one should never
  look alike in a list.
- **Import strategies from a file.** At the bottom of the Library tab: choose a `.json`
  strategy file or paste its contents. Importing never overwrites a strategy you
  already have, never starts anything (everything arrives stopped), and refuses files
  containing program code. You hear the full outcome — imported, skipped, rejected, and
  how many are set to place orders rather than suggest them.
- **The research catalogue lives in the Strategy Lab.** All thirty specs, with their
  evidence, are in the command-line lab: `catalogue list --verbose` to read the
  verdicts, `catalogue export` to write a file you can import. A bulk export by
  evidence level will not hand you anything recorded as Fragile or Falsified — those
  have to be asked for by name.

## 2.1.0 — market watch, screening, and a terminal that looks like one

The big addition is **market watch**: a place to keep lists of symbols and to scan
them all at once. Alongside it, three tools that answer "where am I on this chart?",
and two chart modes. Everything below has a toolbar button and a keyboard shortcut.

- **Watchlists (Alt+M, or the Watch button).** Named, ordered lists of symbols that
  remember which provider and market they came from. Add the symbol you're looking
  at with one press, or pick from the provider's real symbol list through the same
  Market → Provider → Sub-type → Symbol cascade the toolbar uses. Type into
  **Filter symbols** to narrow a long list — it tells you how many are showing out
  of how many exist — and **Add all shown** builds a list in one go.
- **A screener, and a builder for it.** Screens run your conditions against every
  symbol on a list at once. The new **Build a screen** tab lets you make one:
  choose an indicator, a component, a condition, and any values it needs; add as
  many filters as you like; and decide whether all of them must be true, any of
  them, or enough of them by weight. Each row is restated underneath in plain
  English so you can check it in a single read. Results come back as a proper table
  your screen reader can move through cell by cell — and symbols that couldn't be
  checked are always shown, never quietly dropped, because "we couldn't fetch
  twelve of these" must never look like "nothing qualified".
- **The respect report (Alt+R, or the Zones button).** Which levels does this market
  *actually* hold? This measures rather than assumes: for every level near price and
  every standard moving average, how often price touched it and how often it held,
  how big the reaction was, and how long ago. Wicks through and straight back count
  as holds — that's a sweep, which is the level working. Thin samples are filtered
  out by default and labelled when you show them.
- **Market Structure, on your charts by default.** Swing highs and lows labelled as
  higher or lower, the trend state they imply, plus a Break of Structure when price
  continues past the last swing and a Change of Character when it goes the other
  way. Turn it off for good in Settings → Analysis if you'd rather add it yourself.
  One honest caveat, stated in the manual too: a swing mark can only appear five
  bars after the bar it sits on, so it shows you where you *are*, not where to enter.
- **Value Deviation.** A new indicator that marks where price reversed relative to
  value — value being a rolling volume profile's point of control. Reversals below
  it mark support zones, above it resistance zones, and five tiers per side say how
  far from value the zone formed, in shape, colour and pitch.
- **Bar replay (Ctrl+Alt+Shift+P, or the Replay button).** Hides everything after
  the bar you're on and gives it back one bar at a time with F9, so you can practise
  reading a market without knowing what happens next. F10 auto-advances; stopping
  restores the full chart.
- **Split view (Ctrl+Alt+Shift+S, or the Split button).** Puts a second tab's chart
  beside the one you're working on — the daily next to the four-hour, say — either
  side-by-side or stacked. Speech and sound stay with the chart you're actually on.

**Chart legibility.** With Market Structure and Value Deviation both on a weekly chart,
the result was a mess — so: the pane legend now sizes itself against the pane instead of
covering a third of the plot, names the price series and lines before markers, and folds
a whole family of marks into one row (it used to list nine tier labels and never mention
the candles); Market Structure's swing marks became **squares** and its structure events
**crosses**, so they can no longer be confused with Value Deviation's triangles, dots and
diamonds; and Value Deviation gained a **Show tiers from** setting, defaulting to 2, which
drops the shallowest marks. That last one hides glyphs only — speech still reports every
tier, so nothing you could act on has become unreachable.

### The application has a look now

The chart was themed; everything around it was not. Toolbars, tabs and dialogs read
a fixed dark-grey palette no matter which theme you picked, so choosing the light
theme gave you a white chart inside a near-black frame. That seam is gone — a theme
now covers the whole window.

- **Steel Gray is the new default.** The window is one vertical fade: lighter chrome
  at the top, the chart darkening as it goes down, the footer carrying it out. The
  fade is deliberately shallow so it never washes out candles near the top of the
  pane, which is exactly where price at the top of its range sits.
- **Three new presets.** **Blackout** — pure black, white text, lifted dark-grey
  dialogs; a true dark mode for OLED panels and low light. **Classic** — the familiar
  dark navy-and-teal scheme, so anyone arriving from another platform can start from
  something their eye already knows. And Steel Gray itself. The high-contrast and
  braille themes are untouched.
- **Set the colours yourself.** Settings → Appearance now covers up and down colours,
  the chart background and gradient, and a **Window gradient** switch that blends the
  toolbars, chart and footer into one continuous fade between any two colours you
  pick. A theme can also give those three regions completely different colours — a
  light header over a near-black chart is a thing you can build.
- **Dialogs match the application.** They were a fixed light panel in a dark app,
  which is why they read as a different product.
- **Every dropdown and button now follows the theme.** The market, provider and
  symbol pickers were unstyled operating-system controls; they are the single thing
  that most made the terminal look assembled rather than designed.

A safety note that applies to all of it: every built-in theme is checked
automatically for legible chrome text, a visible focus ring, and candles that stand
out against its own background at both ends of its gradient. Picking a preset is
always safe. Only a hand-picked colour pair can collide with a background, and
Settings warns you the moment it does rather than quietly correcting a choice you
made on purpose.

**Also:** boolean indicator settings now work. They were silently ignored across the
whole app, which had been quietly disabling a few options on Cipher SR and Cipher B,
and they now appear as checkboxes rather than a box expecting you to type "true".
Indicator markers follow Heikin Ashi candles when you have them on, the toolbar's
market and provider now follow you when you switch tabs, and bar replay moved to
F9–F11 because F4 was already the braille toggle.


## 2.0.1 — accessibility polish + crypto-options data (2026-07-26)

A small point release on top of 2.0.0. A handful of accessibility fixes that came
straight out of live use, plus one new keyless data provider. Nothing breaks — every
2.0.0 note below still applies.

- **Finish a drawing with touch alone.** On a phone or tablet you could *start* a trend
  line or channel from the touch bar but couldn't set the later points without a
  keyboard. The touch toolbar now has a **Place drawing point** button: arm a tool from
  Drawing Tools, move the cursor, and tap it once per point — multi-point drawings
  complete entirely by touch. It tells you if no tool is armed yet.
- **Move between series on touch.** New **Previous series / Next series** buttons on the
  touch bar (the Page Up/Down equivalents), so you're no longer limited to bars and
  components without a keyboard.
- **Sparse indicators announce a count, not "no data".** On indicators whose signals
  are rare — Cipher B's dots, for example — landing on a bar with no marker used to say
  "no data", which sounded like the whole series was broken even though Ctrl+Left/Right
  still jumps between the signals that are there. It now says **"3 signals in view"** (or
  "no signals in view"). Only a genuinely empty series still says "no data".
- **Optional gradient chart background.** Settings → Appearance → Colors gains a
  **Gradient background** switch and a second "bottom" colour, fading the chart pane
  vertically between two colours. Purely cosmetic and **off by default** — audio-first
  users can ignore it. (Cloud fills also no longer leave a hairline gap where two lines
  cross.)
- **New provider — Deribit crypto-options volatility (no API key).** Chart the **Deribit
  Volatility Index (DVOL)** — crypto's "VIX", the options market's forward implied
  volatility — plus realised volatility, for BTC and ETH. Load it from the market
  dropdown under **Derivatives → Deribit** (`BTC_DVOL`, `ETH_DVOL`, `BTC_HISTVOL`,
  `ETH_HISTVOL`). DVOL sitting well above realised volatility means options are pricing
  fear — a useful mean-reversion tell, and the terminal's first window onto the crypto
  *options* side.

For the full engineering changelog see [`CHANGES.md`](CHANGES.md).

---

## What's New in 2.0.0

Version 2.0 is the milestone the whole "2.0 line" was building toward: a rock-solid
trading core, every provider brought up to a reliable standard, and background
monitoring that never fails silently. This page is the user-facing tour of what
changed since **1.9.0**. For the full engineering changelog see [`CHANGES.md`](CHANGES.md).

> **Version note:** 2.0.0 is a **major** milestone, but **nothing breaks for you.**
> Your saved workspaces, strategies, drawings, alerts, sound designs, shortcuts, and
> API keys all load unchanged. The "major" is about how much got rock-solid under the
> hood, not about migration pain.

---

## Every exchange now talks to its API directly

The crypto exchanges no longer rely on heavyweight third-party SDKs — **Binance and
MEXC were rewritten to call their REST and WebSocket APIs directly**, joining Bitstamp,
Kraken, and Coinbase. The whole app now carries **no shared exchange library at all**,
which had been a hidden source of plugin-loading conflicts.

For you that means:

- **MEXC charts actually stream.** MEXC moved its spot data feed to a compact binary
  (Protobuf) format; the terminal now speaks it natively, so KAS, TAO, and other
  MEXC-only assets chart and update the live price in real time — verified live.
- **Leaner, faster, fewer moving parts.** Each exchange integration is small,
  transparent, and independent, so a problem with one can't take another down.

## Your fills and cancels always reach your ears

This was the biggest reliability push of 2.0. A deep audit of all ~17 providers found
places where the thing a blind trader most needs to hear — *did my order fill? did my
data actually load?* — could go silent. Those are closed now:

- **A filled order is never announced as "cancelled" again.** Some brokers (Tradier,
  Schwab) don't tag their fill records with the order you placed, so the terminal used
  to guess — and sometimes guessed "cancelled" for an order that actually filled. It
  now asks the broker directly for that order's status, so what you hear is the truth.
- **Bitstamp order updates were silently broken and now work.** The private order feed
  was subscribing to the wrong channel name, so fills and cancels never arrived. Fixed —
  and the fill amounts and buy/sell side are now reported correctly.
- **A failed data load is heard, not shown as an empty chart.** Across every provider,
  a rate-limit, a bad key, or a network hiccup now speaks the reason instead of leaving
  a silent blank chart or a false "you hold nothing."
- **Interactive Brokers is safer with real money.** Order confirmations now read out any
  broker warnings before auto-confirming them, take-profit orders carry their trigger
  price, and a working order no longer mis-announces as a partial fill.

## Background monitoring can't die quietly

If you keep charts monitoring in the background (or run the terminal headless with the
tray), a feed that goes silent is now **detected, announced once, and automatically
restarted** — the same safety net the focused chart already had. A background feed can
no longer stop updating without telling you.

## A correctness sweep across the providers

Dozens of smaller fixes that add up to trustworthy data and orders:

- **Right times on the chart.** Tradier intraday bars and FMP intraday bars were landing
  at the wrong timestamps (off by hours, or at "year 0001"); both now sit where they
  belong, with proper US-Eastern conversion.
- **Alpaca crypto works.** Alpaca crypto pairs were coming back empty because the symbol
  was formatted wrong; they chart correctly now.
- **More history where the exchange allows it.** Polygon no longer silently caps you at
  1,000 bars when far more are available.
- **Honest connection state and cleaner shutdown.** Oanda now wipes your live-money token
  on disconnect; several providers report streaming status based on the real connection
  rather than just "a key is present," so the terminal falls back to polling and still
  announces fills when a stream is down.
- **Smarter rate limiting.** Failed requests that can't succeed (a bad key, a malformed
  request) no longer get retried pointlessly — you hear the real error sooner.

## What made 2.0 "2.0" (the flagship features)

If you're coming from an older release, these are the tent-poles of the 2.0 line:

- **Instant tab switches and live background tabs.** The data pipeline was rebuilt around
  per-chart feeds, so switching tabs is instant from a warm buffer, background tabs can
  stream live (opt-in, in Settings → Background monitoring), and your strategies finally
  evaluate on live bar closes.
- **Alerts that fire with the browser closed.** On the hosted terminal, every user's
  saved alerts are evaluated server-side and delivered by email, Telegram, webhook, or
  **browser push notifications** — even when no browser is open.
- **A desktop system-tray icon** for the local terminal, so closing the browser leaves
  the server running with a screen-reader-navigable control menu (reopen, recent alerts,
  silence, status, exit) and the live unread-alert count in its name.
- **Session resume.** The terminal remembers your last session and offers to restore your
  workspaces and charts on launch.
- **Broker parity.** Native bracket orders on Tradier and Schwab, fill history across the
  brokers, and clear spoken handling where an exchange only supports one protective leg.

## Under the hood (for the curious)

The provider system got a shared foundation so quality stays put: shared signing and
symbol-formatting helpers, structured error reporting, a **conformance test gate** every
provider must pass, and a **build guard** that fails if two plugins ever pull conflicting
library versions. The full test suite stands at **2,109 tests, all green**.

---

*Known limitations tracked for a follow-up: Coinbase live candles don't yet report
volume (historical bars do), and the Schwab real-time account stream awaits Schwab
developer-app approval. Neither affects charting or order placement.*
