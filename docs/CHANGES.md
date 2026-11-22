# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### 2026-11-22: Supertrend, ZigZag, Williams Fractals and Pivot Points High/Low

Cody, 2026-11-22, on the indicator library: "work through these, adding all of the ones you listed
to the inventory that are missing", and "make sure anything you add isn't already in the terminal
indicator suite already". Group 1 of the six in TODO.

**Not duplicates, checked first.** Market Structure (`SWING_STRUCTURE`) filters swings by an ATR
multiple and makes highs and lows alternate; `PIVOTS` is floor-trader levels; Cipher A's pivots are
support/resistance zones; nothing computed Supertrend (the pane rules named `SUPERTREND` for a
provider that never existed). The manual's new section says how each differs.

- **Supertrend** (`SupertrendProvider`, `SUPERTREND`): TradingView's `ta.supertrend` (Wilder ATR
  seeded with the mean of the first `period` true ranges, bands around the bar midpoint ratcheted
  against the previous close, first direction down). Up line green, down line red, a triangle and a
  tone at each flip, a hidden Direction step. Causal. Checked against Skender's independent
  `GetSuperTrend` on three 1,200-bar probe series: line and side agree on every bar from 300 on,
  across at least four flips each (they seed differently before that).
- **Williams Fractals** (`WILLIAMS_FRACTALS`) and **Pivot Points High/Low** (`PIVOTS_HL`), sharing
  `PivotMath`: a fractal looks past an earlier equal high (Williams' flat-top rule) and is cancelled
  by an earlier higher one; a pivot is strict on both sides with separate left/right windows (10/10).
  Marks on the pivot bar are declared **Lookahead** (known `span`/`right` bars later); the hidden
  Last High/Low levels take the price only from the confirming bar and are **Causal**.
- **ZigZag** (`ZIGZAG`): percent reversal on highs and lows (5%). The line is interpolated between
  turns (the Line renderer breaks at NaN) and runs on to the newest extreme; it and the turn marks
  are Lookahead; the last-turn levels are Causal from the confirming bar. Speech names the leg
  ("ZigZag falling leg, 110.00 to 99.00, down 10.0%.") and the last one "still moving, not confirmed".
- Registered in the desktop, the web app and the lab. A new guard,
  `Every_core_indicator_is_registered_in_the_desktop_and_the_web_app`, fails for any public Core
  provider missing from either app (the lab leaves three data-feed providers out on purpose and is
  not checked).

**Tests.** `PriceActionIndicatorsTests`, 19 tests (the suite grows by 39: the provider-wide theories add a case per new indicator); the provider-wide contracts (causality on a
prefix and with bars prepended, metadata, hidden parts, render-nothing, buffer keys, marker
legibility, pane, narration causality) pick the four up and pass. **30 sabotages, all red**
(each ratchet, the flip rule, the first direction, Wilder smoothing, each gap term of the true range,
the turn mark, the flat-top and higher-high fractal rules, strictness, the confirm delay, swapped
pivot windows, every default, ZigZag's confirm bar, threshold, extreme-following, interpolation and
last leg, the "not confirmed" words, each app's registration, and both halves of the ZigZag detail below). Four first came back wrong and
were fixed before this: the gap term of the true range passed with its line broken (the probe
series hardly gap; a gap-up and gap-down fixture now sets the ATR entirely by the gap), as did the
fractal span default (the fixture gave the same answer at 1 and 2); the ZigZag extreme sabotage did
not build; and a ZigZag helper choosing the first leg's extreme was dead code (the confirming bar
is always that extreme) and was removed.

**CI caught one I had not run locally.** The first push (2879cb4e) failed
`NarrationCausalityTests` for ZigZag: its detail named the leg's end ("falling leg, 120.87 to
89.56"), which is decided by later bars. The detail now says only what the causal last-turn levels
knew at that bar: "ZigZag: falling since the confirmed high 110.00, down 8.2% to the close. The low
before it, 99.50." Arrowing along the line, which is declared hindsight, still names the leg.

**Caveats.** Not heard. Not in the demo allow-list, so the demo does not offer them. The marks use
dots, diamonds and triangles, which other indicators also use; they differ by colour, words and tone.
A ZigZag "outside" bar (a new high and a reversal-sized low in one bar) is taken as a new high,
not a turn (not checked against TradingView). Not on the VPS (release, VPS and website go together).

### 2026-11-22: invert the price pane with I, pitch and all

Cody, 2026-11-22: "New feature: invert chart, 'i'. if invert chart applies to only the main pane,
scope it to just that." Then, asked: "pitch flip too: yes" and "Drawing and alerts set to real
prices: yes".

**What it does.** Bare `I` (and the Go bar's `INVERT`, *invert chart*, *upside down*, *flip
chart*) turns the price pane upside down, per tab, and back. Indicator panes stay upright, as
TradingView's *Invert scale* is per price scale. It says *"Price pane inverted. Higher prices are
drawn lower and sound lower; prices and directions are read as they are."* or *"Price pane
upright."*, and the chart layout (Ctrl+Alt+Shift+Y) adds *"Inverted: <top price> at the bottom,
<bottom price> at the top; the pitch is turned over with it."*

**One switch, every mapping.** `ChartMath`'s price-to-height mappings (`MapY`, `MapYToPrice`,
`PriceToScreenY`, `MapYToPriceInPane`, `PriceToCanvasY`) and `NormalizedPosition` (what the pitch
reads) take `inverted`; `ChartMath.IsInvertedFor(state, pane)` answers it, true only for the price
pane. The renderer passes it to every renderer through `RenderContext.IsInverted` (49 `MapY`
calls), the axis, its labels and the crosshair badge; the hover readout, the hit tester, "context
here" and the drawing tools read the pointer through it; `NavigationSonifier` (arrowing and the
heatmap) and `AudioSequencer` (playback) pitch through it. Upside down a candle's upper wick takes
the lower wick's tone (880 and 220 Hz trade places). The body's up and down tones do not change:
they follow the candle's colour, and the colour is the market's.

**What does not turn over.** Words, prices, "up"/"down", candle colours and patterns. A mark
"above the bar" still sits above the candle on screen (`RenderContext.HeadPrice`/`FootPrice` give
the bar's screen-top and screen-bottom price). Drawings and alerts store real prices, so a line
placed by mouse on the inverted pane holds the price clicked. Saved with each tab
(`TabConfiguration.IsPriceInverted`, restored by `WorkspaceInitializer`); a file without it loads
upright.

**Left out, on purpose.** Chart templates do not carry it (a way of looking at one tab, not a
layout). The braille/tactile chart and the picture sent to the AI analyst stay upright: both are
read as the market. The status line does not name it (it does not name log scale either).

**Tests.** `InvertedPriceChartTests` (34): the key and Alt+I left alone, the Go bar words, the
sentence both ways, the reducer, per-tab switching, the workspace file (the active tab and a
background tab, each inverted while the other is not, since they are saved by different lines), restore, the mirror and its
four inverse mappings, the price-pane-only scope, the pitch and the wick swap in the strategy,
arrowing and playback (price pane inverted, an indicator pane not), the hit tester finding a line
where it is drawn, the axis labels and badge, a mark above an inverted candle, and the layout
sentence, and a line placed by mouse near the top of the inverted pane storing the low price drawn
there. Each was made to fail by breaking the line it guards: 24 sabotages, all red. Two first
passed with their line broken (the Go bar test matched on the words alone, and the save test
inverted only the active tab); both were rewritten until they failed. F1 Help has an `I` row, and
`Phase5KeyboardScopeTests` counts it as a chart command.

**Not heard yet.** Cody has not tried it; it is in TODO "To hear". It ships with the next release,
with the VPS and the website.

### 2026-11-22: indicators update live on a busy market; volume bars say the direction they are drawn

Cody, 2026-11-22: "when indicators like cipher b are on the chart, do these indicators update in
realtime? I don't think they do ... nothing seems to move except the timestamp ... If I switch
workspaces then switch back I see the updated values". And: "if the volume bars switch when HA is
on, then that is what i should hear, colors, value, direction ... What is seen should be what is
heard".

**Why the indicators froze.** `DataOrchestrationService` cancelled the recalculation in flight on
every trigger, and each recalculation first awaits the order book, a network round trip (MEXC's
took about 300 ms from here, timed 2026-11-22). A market ticking faster than that cancelled every
tick before it reached the indicators; only a forced full recalculation (a tab switch) brought them
up to date, and the candle (not computed there) kept moving. A test with a 300 ms book and a tick
every 100 ms for two seconds counted **0** indicator updates. The engine itself was not at fault:
one Cipher B tick through `IndicatorEngine.CalculateIncrementalAsync` moved every value.

**Fixed.** One recalculation runs at a time; a trigger that arrives meanwhile asks for one more
pass on the newest bars when it finishes (`RunUntilCurrentAsync`). Only a change that makes the
work in flight wrong (another chart, Heikin-Ashi, an added indicator, a scrollback prepend, a new
built-chart shape) still cancels it. The "full recalculation owed" flag became a pair of counters
(`_fullAsked`, `_fullDone`), so a full request that lands just as a full pass finishes is not wiped
out. Tests: `Ticks_faster_than_the_order_book_still_update_the_indicators` (red with ticks
cancelling again, red without the extra pass), and the two cancellation tests rewritten for the
new rule (a tick waits for a full pass and then runs; a newer change cancels it and is a full pass
itself, red when changes stop cancelling). **Not guarded:** the cancelled-full-does-not-pay-the-debt
check; every cancel now comes from a change that asks for a full pass itself, so removing it
changes nothing a test can see. It stays as a backstop. The order book is still fetched on every
pass (at most one at a time now); fetching it only when a heatmap is on is filed.

**Volume direction.** 43022e3d made the volume TONE follow the market's candle in the panes below
the price pane (they are painted from it); the spoken "up"/"down" still came from the Heikin-Ashi
candle. `ChartMath.IsDrawnOverTheCandles(pane)` is now the one rule for the renderer's choice, the
tone (`NavigationSonifier`) and the words (`SpeechFormatter`, both the single-component and the
summary branch). The end-to-end test now also asserts "10, down" (red with the old rule).
Cody asked "if the volume bars switch when HA is on": they do not, on our chart or on TradingView,
which also colours volume by the real candle; filed as a question for him.

**A browser test race.** CI on 6a3a3d73 failed once in `Turning_speech_off_still_says_so`: it
returned on the first utterance, and F2's blank interrupt landed before "Chart speech off". A rerun
was green. The test now waits for the words themselves (red when the announcement is renamed, green
restored).

### 2026-11-22: an alert fires on the chart it was set on, and nowhere else; volume bars sound the colour they are drawn

Cody, 2026-11-22: he set "price touches 61,450" on his BTCUSDT chart and "it kept firing over and over
when the price hadn't even been near that price ... they don't need to fire on all workspaces, just
the one it was set on". And: "2 recent bars on the 4 hour btc chart sound positive but are reported as
red".

**Why the alert fired.** His saved session had six BTCUSDT tabs (1d, 4h, 1h, 15m, 1w, 1M), all
Heikin-Ashi, with background tabs watched every ten seconds. An alert records the symbol, provider
and timeframe of the chart it was set on, but only the symbol was ever checked (the timeframe was
marked "informational"). So every BTCUSDT tab tested the alert against its own newest candle. The
day, week and month candles all spanned 61,450, so they "touched". Each tab's candle has a different
date, and the "already fired on this bar" memory is kept per alert, so each tab reset it for the next
and the alert fired again on every poll.

What changed (`AlertScope`, one rule for every place that evaluates alerts):

- **An alert belongs to the chart it was set on**: same symbol, same timeframe, same provider. The
  focused chart (`AlertOrchestrator`) and each background tab (`BackgroundWorkspaceMonitor`) only
  evaluate the alerts that match them. A part an alert does not record still matches any chart, so
  alerts saved before this (no timeframe) and "current chart" alerts (no symbol) behave as before.
  Timeframes compare by length, case-sensitively: "1M" (a month) is not "1m" (a minute), and "60m"
  is "1h".
- **A price "touches" alert reads the market's own high and low** on a Heikin-Ashi chart. A
  Heikin-Ashi candle's wick can reach prices the market never traded (its open lags behind), so a
  touch was possible with the price nowhere near. "Crosses" alerts keep reading the Heikin-Ashi
  close, as Cody asked on 2026-10-22, and Renko-style charts already read the real bars.
- **Switching tabs is not a crossing.** The focused chart compared the last values of the chart you
  left with the first values of the one you switched to, so an "RSI crosses above 70" alert could
  fire just because the 1-day RSI was 50 and the 1-hour RSI was 80. The comparison now starts afresh
  on every switch of symbol, timeframe, provider or chart type.
- **The browser-closed watchers still cover what the open browser does not.** With the 1-day tab on
  screen, "BTCUSDT is on screen" used to mean every BTCUSDT alert was the browser's; now that the
  1-day tab no longer evaluates a 4-hour alert, the headless watchers (`LocalBackgroundMonitor`
  locally, `HostedAlertMonitor` on the server) take the 4-hour one. An open browser claims each
  chart it shows by symbol, timeframe and provider (`WebHostBrowserCircuitHandler`).

**Why the volume bars disagreed.** On a Heikin-Ashi chart the arrow-key tone used the Heikin-Ashi
candle for every series except price, but the volume pane is painted from the market's real candles,
and the colour word ("red bar") is said from the real candle too. When the Heikin-Ashi candle was up
and the real one down, the tone said up and the paint and words said red. The tone now uses the same
candle the renderer paints that pane with: Heikin-Ashi only for series drawn over the candles in the
price pane, the real candle for every pane below. Playback already used the real bars.

Tests: `AlertScopeTests` (10, including Cody's case: one shared evaluator and a 1d and a 4h monitor,
the 61,450 alert fires zero times and a 61,000 alert that the 4h candle reaches fires exactly once
over four polls; the Heikin-Ashi touch with its precondition checked; the tab switch; the coverage
claims) and two in `PriceLineTransformAudioTests` (a volume bar under a down candle and an up
Heikin-Ashi candle sounds the bearish tone and is said "red bar"). Each was proven red by undoing its
fix. The fake browser in `HeadlessSessionTests` and `HeadlessAlertTests` now claims what a real one
does. The alert, monitor, coverage, headless, sound and navigation tests pass locally (1,060 and
1,496).

Caveats: **not heard by Cody.** An alert's **market** is not part of the match (the strings differ
between places). Two tabs with exactly the same symbol, timeframe and provider still share one
monitor, as before. An alert saved before 2026-11-22 without a timeframe still fires on every chart of
its symbol; set it again on the chart you mean to scope it.

### 2026-11-22: shared charts, the second round of questions (design only)

`docs/SHARED_CHARTS_DESIGN.md` gains Cody's second round, answered with recommendations for him to
confirm: a share holds one tab by default or several picked tabs, never the whole session; it is on
or stopped with the same link, plus "New link"; **chart notes** are saved with the tab (useful before
anything is shared, and in the app with no account) and go live with a share, spoken to viewers,
beside the chart and on the website page; the app may sign in to a website account (optional, for
sharing first and copying charts later, never for API keys); viewing a share needs no account; the
app opens a share from a pasted link through a small public API and loads the prices from its own
feeds. An **Account** tab in Settings that explains itself is suggested before any of it is built.

### 2026-11-22: a design for shared charts (nothing built)

`docs/SHARED_CHARTS_DESIGN.md`, written for later from Cody's answers: shared charts are live (the
owner's edits reach viewers within about a second and are spoken), anyone can share, viewers can
never change a shared chart (refused in `WorkspaceStore.Dispatch`, not only hidden), and links are the
only way in. It records what exists to build on (tabs save drawings, `AppUser.DisplayName`, the
anonymous `account/status` route), the open choice for signed-out viewers (the demo runs in another
process), the Twelve Data terms check before stock charts can be shared, the abuse basics, and four
smaller questions for Cody. Listed in TODO under "Asked for, not built yet".

## [2.15.0] — 2026-11-22

Everything since 2.14.1, newest first. **On the VPS:** all of it (the tagged commit, deployed after
the tag, Cody 2026-11-22: "update, cut the release and bring the website up to date"). **The
website** carries 2.15.0's downloads and docs. Cody has heard the settings, the Donate tab, the 9-13's
reading and Cipher B's "9 components" (2026-11-22); the rest is under "To hear" in TODO. **Nothing
here has been tried with money.**

### 2026-11-22: the hover date no longer sits beside half a date; Cipher A's symbols over the top candles are drawn whole; one Donate row for the EVM chains

From Cody's BTCUSDT 1d screenshot of the hosted terminal (Cipher A, Volume, Cipher B) and his notes
on the Donate tab. He passed on a Website button in the signed-in terminal.

- **The date under the mouse pointer covered half of a tick label** ("Oc" showing beside "Oct 6,
  2026"). The tick labels are drawn on the chart image; the hover date is page text over it, so it
  could not make the axis leave a label out the way the keyboard cursor's date does on the image.
  The renderer now publishes where it drew each time-axis label (`IPaneLayoutService.TimeAxisLabels`,
  fractions of the plot width, published after the axis is drawn), and `ChartArea` covers, in the axis
  strip's own colour (`--chart-fade-top`), every label within 4 px of the hover date
  (`ChartArea.HoverDateCovers`). Tests: `The_hover_date_covers_the_tick_labels_it_would_cut_and_no_others`
  (red with the covers removed) and `Each_published_span_is_a_label_drawn_on_the_axis` (ink across
  each published span and none just past its ends; red when nothing is published). **Caveat:** the
  hover date's width is an estimate (7.5 px a character plus padding, on the generous side), since
  its real width is the browser's; a label just beyond the estimate could still touch it. Not seen
  by Cody.
- **Cipher A's symbols over the highest candles were cut off under the legend.** The symbols stack
  above the candle (since 2026-11-17), and over a candle at the top of the range the stack rose past
  the top of the plot, which is clipped just under the status lines, so the top of it was lost there.
  The price pane now keeps room between its status lines and its values for what the marks on screen
  need (`ChartRenderer.MarkHeadroom`, measured with the marks' own placement through
  `StandardRenderers.AboveBarOverhang`, stacking included, plus 2 px), none when no mark reaches the
  top, never more than a quarter of the plot. The room is part of the published status band, so the
  pointer, the hit tester and the drawing tools map values where they are drawn; drawing is still
  clipped at the band. It applies to every indicator with above-the-candle marks (DBSI's shapes too).
  Tests: `A_stack_of_marks_over_the_highest_candle_is_drawn_whole_below_the_legend` (three symbols on
  every candle with the highs at the top: as many pixels of their colours as with room over the
  candles, none in the legend's rows; 0 pixels against 12,245 with the room removed) and
  `The_room_is_kept_only_when_a_mark_reaches_the_top`. One existing test,
  `When_the_slot_is_too_narrow_no_label_is_drawn_and_zooming_in_brings_them_back`, compared a chart
  with DBSI's shapes against one without, assuming both plots start at one height; both now use a
  range with room over the candles. **Caveats:** a stack taller than a quarter of the plot is still
  cut at the band (all six Cipher A symbols on one candle on a short pane); the plot's top moves down
  by the room while such a mark is on screen, so the chart shifts a little when one scrolls in or out.
  Below-the-candle marks at the bottom of the range were not changed. Not seen by Cody.
- **One Donate row for the EVM chains.** BNB Smart Chain had its own row with the same address as
  Ethereum, Arbitrum and Base; it is one row now, "Ethereum, Arbitrum, Base or BNB Smart Chain"
  (Settings, Donate; `SettingsModalTests` updated).

### 2026-11-22: the website rewritten for the public — one Chart link, a "What is tested" page, downloads first; live 14:14 UTC

Cody asked for every suggestion from the site critique except hiding the leaderboard, one "Chart"
link instead of "Chart" plus "Open terminal", no em dashes, everything consistent, accurate and
readable by anyone, and the detail kept in the manual (the docs-content pages are unchanged).

- **One Chart link.** Signed out it opens the demo at /chart; signed in, the nav's href becomes
  /terminal/, and /chart itself asks the terminal's /terminal/account/status (localhost, 1.5 s timeout)
  and answers 302 to /terminal/ (`Cache-Control: private, no-store`, `Vary: Cookie`). Top nav: Home,
  Features, Chart, Download, Docs, FAQ, then Create account and Sign in (now /terminal/account/login)
  when signed out, My account and Sign out when signed in. Quick Start left the nav (it is under Docs
  and in the footer). No site link opens the terminal in a new tab any more.
- **New /status page, "What is tested, and what is not"**: each version (website and Linux in daily
  use; Windows app built and installed on CI with no screen-reader session on record; Mac never
  started; phones not ready), the unconfirmed features (live trading in beta with no order through any
  venue, withdrawals off, Dot Pad never on a device, Windows toast, no scripts in the Mac app) and the
  known limits. Home, Features, Download, Platforms, FAQ and llms.txt link it. **Update it at every
  release.**
- **Home**: short hero ("free, open source"), six feature blocks, a free-account section framed as
  learning with play money, the leaderboard kept and framed for early days, five questions, the two
  "why" sections merged (the voc-rehab paragraph now says "a learning tool, not a promise of income"),
  one blog post. Screenshots got short alts plus a "Describe this screenshot" expander.
- **Features**: eight short sections, each linked to its manual chapter, and a table of what the
  chart, the free account and the download each include. **Download**: the tables come first, "WebHost"
  is called "the browser version", the launcher names are 2.14.1's, the release notes moved to What's
  New and the limitations to /status. **Platforms**: a plain table (platform, what to use, screen
  readers, tested) replacing the rendered PLATFORMS.md, which is linked on GitHub for developers.
- **Smaller fixes**: Docs and Support said help was Alt+H (it is F1; Alt+H is the heatmap); Docs'
  "Training, coming soon" removed; Developers no longer says you can drop a compiled plug-in into the
  app (the trust manifest refuses it; the page now says pull request, or the .atpkg path); Privacy
  dropped the mobile key stores, describes Linux's encrypted store and discloses the self-hosted,
  cookieless GoatCounter; Privacy and Terms had a "last updated" that was always today's date; 404
  linked the retired waitlist; footer reads "Code under GPL-3.0 · Site © 2026 Cody Hurst" and says
  Contact and Donate instead of Support twice; titles and descriptions lost their em dashes;
  `twitter:card` is `summary_large_image`; structured data no longer claims Linux arm64 and labels live
  trading beta and the Dot Pad experimental; FAQ answers rewritten (no iOS VoiceOver or TalkBack
  claims, no "options and futures through the major brokers", no "for individuals") with two new
  questions (real money, the leaderboard), its categories in a sensible order; the site tagline no
  longer says "The only fully accessible trading terminal".
- **Caveats:** the signed-in /chart redirect was checked only signed out and with a cookie that is
  not a session (both 200); it has not been tried with a real session. The site's files are not in
  this repo: backups are `~/backups/trade-site-20261122-1414.tgz` (files),
  `~/backups/trade-site-db-20261122-1414.json` (nav, FAQ, settings) and `*.bak-20261122-site` beside
  each changed file on the VPS. Nothing was heard through a screen reader.

### 2026-11-22: Cody's seven notes on the manual — headlines from Finnhub only; the manual corrected on Cipher B, arrowing, platforms and the Strategy Lab — in 2.15.0; Cipher B's count heard

- **Headlines come from Finnhub only.** Alpaca's news wire (an optional second source, off by
  default) is taken out of the code, the settings and the manual. Cody: "news headlines should only
  come from finhub as it is the better source". On a free account Alpaca's stories were fiscal.ai's,
  in batches up to half an hour late, and carried no coins; Finnhub's free key covers the same
  stocks plus coins. The parts both used stay: the importance words, the once-a-minute gate, the
  wording, which charts and holdings count (now `FinnhubNewsLiveNotices.TouchedTargets`), and the
  dossier's dedupe of the same story from two publishers. Files renamed: `AlpacaNews.cs` →
  `NewsStories.cs`, `AlpacaNewsSource.cs` → `NewsSource.cs`, `AlpacaNewsTests.cs` →
  `NewsStoryTests.cs`; `AlpacaNewsLiveNotices` and the Alpaca socket, parser and REST source are
  deleted. **Caveats:** Finnhub is asked about once a minute, not pushed, and how long Finnhub takes
  to list a story after it is published is still unmeasured, so a story can now arrive later than
  Alpaca's socket would have pushed it on a paid Alpaca plan. A saved
  `narration.headlinesAlpaca` stays in settings.json and is never read. The Alpaca trading venue and
  its price data are untouched (same API Keys slot). The manual's Alpaca table and rules moved into
  the Finnhub section. Tests: 26 fewer (the parser, socket and REST tests went with their code).
  Guards proven red: the dossier's two-publisher dedupe, the touched-target order, the HTML-entity
  clean-up, the hosted container's single gate, and the desktop container text.
- **Cipher B says "9 components"** (Cody: "cipher b count, 9"). Moving onto any series now counts
  every component Up and Down stop on, muted ones included; before, it left the muted out, so Cipher
  B said 7 while Up and Down stopped on 9. The code's own comment already said "count ALL". The
  manual's three Cipher B counts say nine (two still said eighteen after 011af0e7). **Caveats:** this
  changes the count for every indicator with a muted component, not only Cipher B; a hidden
  component was already counted. Test: `The_series_switch_counts_muted_components_too`, red with the
  old muted-skipping count.
- **The mobile apps are "not ready yet"** in the manual, the Quick Start and the docs README (Cody:
  "make it clear in the manual and the site"). The docs README's platform table showed Android and
  iOS with green ticks for chart, audio, speech and trading; it now says never built, no download,
  and marks the Mac app unsigned and never run. "Desktop & mobile" headings and "the Mac and mobile
  apps" lines no longer imply a mobile app exists. **On the website (2026-11-22 12:42 UTC):** the
  download page ("will use this app too, once mobile builds ship"), the features page ("VoiceOver on
  Mac and iOS, TalkBack on Android"), the donate page ("on the way") and llms.txt now say the apps
  are not ready yet; the Manual, Quick Start, Shortcuts, What's New and Platforms pages carry these
  docs; llms-full.txt was regenerated from them (it still says 2.14.1, as the pages do, ahead of the
  download). Backup ~/backups/trade-site-20261122-1242 on the VPS.
- **The manual** (011af0e7): Cipher B speaks **seven** components, not eighteen (PageDown says
  seven; Up and Down also stop on the muted RSI and Stochastic; the Sell dot starts left out). Left
  and Right read the whole series after PageUp or PageDown and only one component after Up, Down,
  Home, End, Backslash or a jump, which the "Moving through time" section now says. The indicator
  primer describes Cipher B without Market Cipher. There is **no Android or iOS app** (never built,
  known defects in the Android keyboard bridge and audio loop); the Mac app is unsigned and has never
  been run on a real Mac; phones use the web version (PLATFORMS.md and QUICKSTART say so). The
  Strategy Lab is in the repository only, not in any download, and the terminal does not need it.
  **Caveats:** the "seven" was corrected to nine the same day (below). No code changed for the arrowing: Home, End and Backslash still
  switch to one-component reading.
- **Settings' key table said Ctrl+Shift+D** for the detailed point summary; it is Alt+Shift+D on
  every head, Windows included. The Ctrl+Shift-to-Alt+Shift remap in the web host does not depend on
  the operating system; only Ctrl+Shift+Space (and Ctrl+Shift+Tab on the desktop) stay Ctrl+Shift.

### 2026-11-22: Cody's three questions — a switch for near-level speech; the 9-13's published recycle; the last three workspace audit items — CI green, on the VPS, not heard

- **Announce nearby levels while navigating** (Settings, Speech tab; on by default). Off, arrowing
  the price says no "Near resistance at …" for any level: pivots, Cipher SR's zones, a profile's
  levels, your lines and your alert prices together. Cody: "want the option to enable or disable
  that functionality". The manual's line saying the level comes "after the prices" was wrong (it
  comes first) and is corrected. **Caveats:** there is no per-kind switch; muting one indicator
  still silences only its own levels. Tests: `NearLevelTests` (default on through the real settings;
  off silences four kinds at once while the bar is still read), red with the gate removed or the
  default flipped. The checkbox's load and save are not tested.
- **The 9-13 recycles by the published rule.** A new setup the same way as a running countdown is
  compared with the setup that countdown counts from, each measured highest true high to lowest true
  low (the running one with the bars its run went on past 9). It restarts the countdown only when at
  least as large and under 1.618 times as large; smaller, or 1.618 times or more, and the old
  countdown carries on. Alt+Shift+D on the new 9 says which, and the ratio ("Buy countdown kept,
  this setup is smaller than the one it counts from."). **Caveats:** the rule as Perl's *DeMark
  Indicators* gives it, read through Oxford Strategies' write-up; Symbolik's help (unread, 403) is
  reported to use 100 to 200%, and some platforms add an 18- or 22-bar cancel and a
  setup-within-a-setup rule, which are not here. What happens at 1.618 or more is not stated in that
  source; carrying on is our reading. **Still not compared against TradingView**: no TradingView here,
  and the open-source scripts found did not load. Tests: five new in `Sequential913Tests` (in band,
  smaller, 1.618 or more, the run past 9, true highs and lows), red under the simple recycle, either
  bound removed, never restarting, no extension, plain highs or plain lows.
- **Portfolio values stocks and currencies** (audit, archive 12311). The price lookup asked the
  Crypto market for every holding, so a share or currency balance read "no AAPL/USD market". It now
  asks the venue which markets it trades and tries crypto first (as before; the stock "BTC" is a
  fund, not bitcoin), then a stock by its ticker when the quote is dollars, then a currency pair
  (EUR/USD, EURUSD, EUR_USD). A venue whose markets are unknown is asked crypto only, as before.
  **Caveats:** not tried against a real broker account; a stock quoted in anything but dollars is
  still unpriced. Tests: four in `PortfolioValuationTests`, red with the market hardcoded again, the
  stock step removed or the unknown-venue fallback removed.
- **Heatmap history on every timeframe** (audit, archive 13159). Each bar now takes the last
  order-book snapshot inside its own time (up to the next bar's start), so an hour or a day chart
  shows its history; before, only a one-minute chart did. A bar with no snapshot of its own stays
  empty rather than borrowing an older one. **Caveats:** history is only what the app recorded while
  running, so the heatmap on a day chart shows the days it was open. Tests:
  `OrderBookHistoryTimeframeTests` (hour, day, a gap, one minute), red with the one-minute bucket put
  back or with borrowing allowed.
- **A level source that throws is said, not swallowed** (audit, archive 12682). `LevelService` used
  to skip a failing level provider silently, so a strategy whose stop or target came from it just
  never traded. The failure is now logged once (source, error) and, when it cost a setup, the
  strategy says so once: *"Strategy 'X': setup skipped, no stop: the Cipher SR levels (…) failed."*
  **Caveats:** the log line is not tested; if a target fails for another reason after a level source
  failed on the stop's query, the level source can be blamed. Tests: `LevelFailureSpokenTests` (5),
  each red with its part removed.

### 2026-11-21: Cody's notes — the 9-13 sequential; near support and resistance on every level; candle patterns follow the chart; settings say less; markers are markers; a Donate tab; the heatmap is an indicator — CI green, on the VPS, not heard

- **The 9-13 sequential** (Add Indicator, Reversal; `Sequential913Provider`). A buy setup counts
  nine closes in a row each below the close four bars before, starting only after a price flip
  (the bar before closed above its own four-bars-back close); a sell setup is the mirror. A 9 is
  **perfected** when bar 8 or 9 reaches the lows (highs) of bars 6 and 7. The **countdown** starts
  on the 9 and counts, not necessarily in a row, each close at or below the low two bars before
  (sell: at or above the high); **13** also needs its low at or below countdown bar 8's close, and a
  bar that fails only that is **deferred**. A countdown is **cancelled** by a completed setup the
  other way or by a close beyond its own setup's extreme (drawn as the dashed **Setup Resistance**
  and **Setup Support** lines), and a new setup the same way **recycles** it (starts again from 1).
  Every count is a marker with its number printed beside it; arrowing says "Buy setup 7." and
  ticks. The 9 and the 13 are components of their own (a triangle with a crystal bell, a diamond
  with a dual-tone bell), so they sound unlike the counts and Ctrl+Left/Right jumps between them.
  Focused on a 9 it says "Buy setup 9, perfected." (or "not perfected"); Alt+Shift+D says the
  count, a deferral or a cancellation and why. Not called by the trademarked name.
  **Caveats:** written from the published descriptions only; **not checked against a TradingView
  script** (none was run side by side). The recycle is the simple one: the published method also
  compares the two setups' sizes, and this does not (superseded 2026-11-22: the size comparison,
  above). Narration as a bar closes says "Buy setup 9."
  without "perfected" (the marker's spoken template has no word for it); focus or Alt+Shift+D says
  it. Tests: 14 in `Sequential913Tests`, from hand-built bar sequences for each rule; removing the
  flip, perfection, deferral, either cancellation, the recycle or the label fill each turns one red.
- **A marker's spoken template takes `{label}`**: the number or text printed beside the marker (its
  `_label` companion), in every route that speaks a marker (focus, other markers on the bar,
  narration, playback). Documented in Properties and `PLUGIN_AUTHORING.md`.
- **"Announce other markers on the bar" no longer says two full stops** ("Swing high 100.." became
  "Swing high 100."): each template's own stop is taken off before the join adds one.
- **Near support and resistance covers every level** (Cody chose option A). Arrowing the candles
  said "Near resistance at X" for Cipher SR's two zone lines only. Now also pivots (PP, R1 to R3, S1
  to S3, Camarilla), Cipher A's pivot high and low, a volume profile's point of control and value
  area edges, your horizontal lines, and the prices that active alerts on this symbol watch
  ("crosses" or "touches" a price). Each names itself after the price: "Near resistance at
  95,200.00, pivot R1." Cipher SR's sentence is unchanged; the nearest level each side wins; hidden
  or muted lines are skipped. **Caveat:** "alert lines" means fixed-price alerts; an alert on an
  indicator's value is not a price and is not counted. 13 tests in `NearLevelTests`.
- **Candle patterns follow what is drawn.** On a line, area, OHLC bars, baseline, high-low or HLC
  area chart no candle is drawn, and the bar close and the forming commentary no longer name one
  ("Close 198.50, Bullish engulfing", "Long-legged doji forming"); chart patterns are still said.
  Brick charts (Renko and the rest) were already quiet. **Correction:** I first told Cody
  "Narrate candle patterns as they form" had no brick check; it had one. Asking (Alt+Shift+D, the
  Go bar) still names a candle pattern on any chart.
- **Settings say less** (Cody's cuts on the Speech tab, then every tab: a sentence where it adds
  context, none where the name says it). Renamed, keys kept: **Announce other markers on the bar
  while navigating**, **Tick other markers on the bar while navigating**, **Describe how markers
  and lines are drawn**, **Narrate chart patterns as they form**, **Draw chart patterns**. Two of
  them are over the 42-character label guard and are allow-listed there.
- **A Donate tab** in Settings (out of the License tab): Cash App, PayPal and the coin addresses
  from codyhurst.com, each in a read-only field with a Copy button that says what it copied ("via
  Tangem" dropped, as Cody asked).
- **"Marker" for the shape, "signal" for its meaning.** The shape drawn on a bar (a dot, a
  triangle, a diamond) is a marker everywhere it is named: the type label, "N markers in view",
  "No more X markers in this direction", Properties, Help, the settings. "Signal" stays for what a
  marker means (a buy signal), MACD's Signal line and narration.
- **The heatmap is an indicator.** The toolbar's Heatmap button and Alt+H are gone; the liquidity
  heatmap is added from Add Indicator (Order Flow) or the Go bar's `HEAT`, whose description now
  says liquidity, not volume. Alt+H is free.
- **The manual is a manual, not a change log** (Cody: "the manual isn't a change log"). Every
  dated or version note ("until 2.x", "since …", "no longer", "Cody has not heard") taken out of
  `USER_MANUAL.md` and `SHORTCUTS.md`; the liquidity heatmap, near-level speech, candle patterns
  on drawn bricks, the 9-13 and the Speech tab's template words documented; eight glossary entries
  added for newcomers (brick, candle pattern, chart pattern, marker, order book, swing high/low,
  volume, watchlist) and the glossary sorted. Setting names checked against the Settings dialog.
  **Caveat:** the sections touched were checked against the code; the middle chapters (Trading,
  Automation, Monitoring) were cleaned of history but not tightened line by line. No appendix: the
  glossary is the appendix, the Settings chapter the settings reference.

### 2026-11-21: the audit tail — nine defects fixed, each with a test that fails on the old code — CI green, on the VPS, not heard

Checked against the code from `TODO.md`'s audit sections (indicators, accessibility, workspace);
each fix sabotage-proven (the old code put back turns its test red).

- **Pulse's Alt+Shift+D said "Fast NaN, Anchor NaN, MFI NaN, ADX NaN"** while warming up. Ready
  readings are said, the others left out, "Pulse: warming up." when none is ready.
- **Vol Regime's detail used a fixed 1.25 / 0.75** while its state uses your thresholds, so it
  could say "neutral" on a bar the chart called expansion. It uses yours.
- **Regime's state was 0 (flat) during its 199-bar warm-up**; it is no reading (NaN), so a
  strategy gating on "not bearish" no longer passes through the warm-up.
- **Shift+F12 (or P) with no series focused said nothing.** It says "No series focused."
- **A tab switch or close that changed nothing was silent.** It says "Already on tab 1: …",
  "No tab 5. 2 tabs open." or "The last tab stays open."; the tab bar sends 1-9, Home, End, the
  arrows and Delete to the store even when they can do nothing. **Caveat:** the store side is
  tested; the tab bar's key handling is not (no harness for it).
- **Quick trade said "$45,700.00 EUR"** on a euro pair. An amount in a known quote is said in it
  ("45,700.00 EUR"); the arming line's account amounts keep "$" (the account currency is not
  known there; the TODO follow-up stays open).
- **A level colour that does not parse** (a hand-edited workspace) threw while drawing and lost
  the whole frame; it draws in the axis ink.
- **Pane and category assignment under a Turkish culture** missed every code with an "i"
  ("CIPHER_B" lower-cased to "cıpher_b"): invariant casing.
- **The hosted server logged each fired alert's text at Information**, the user's symbol, level
  and direction beside their id. It logs the alert id there and the text only at Debug; a source
  scan fails on any WebHost log above Debug carrying the text.
- Untested: the Load Workspace reset loop stops if a tab close is refused (it could spin forever;
  unreachable since the tab reindex fix, no harness for the modal).
- Comments corrected: AVWAP Bias Soft (between the anchors is +1), Regime's EMA seeding (not every
  EMA here seeds the same way), Value Deviation's WaveTrend (not Cipher B's), Hurst's window sizes.
- Already fixed before this pass and moved to the archive: the Sound Designer's import box label,
  Cipher B's `DivergenceConfirmLag`, Cipher SR's AutoScale text. Still present and marked so:
  portfolio pricing hardcoded to Crypto, `LevelService`'s silent catch, the heatmap history's
  1-minute assumption.

## [2.14.1] — 2026-11-21

### 2026-11-21: the Linux and macOS WebHost downloads build again

- **v2.14.0's release built four of its seven downloads.** The Linux, macOS Apple Silicon and macOS
  Intel WebHost jobs stopped at "Package (Linux, macOS)" with exit code 126: `packaging/webhost/package.sh`
  (new on 2026-11-17, the one-folder WebHost zips) was committed as 644, and the workflow runs it as
  `./packaging/webhost/package.sh`. The test that runs it for all three RIDs calls `bash package.sh`,
  which never needs the bit, so nothing went red before a tag. The Windows setup, the Windows zip, the
  Windows WebHost and the macOS app built and are on v2.14.0.
- **Fixed:** the script is 755 in git. `ReleasePayloadManifestTests.EveryScriptAWorkflowRunsDirectlyIsExecutable`
  reads every workflow for `./x.sh` or `./x.py` and fails on one that is not executable; red on the 644
  script, green at 755 (checked).
- **The app is 2.14.0's.** Nothing else changed. The hosted terminal stays on the 2.14.0 build
  (a6f6d4bf, deployed 2026-11-21 14:50 UTC); its stamp says 2.14.0.
- **Released 2026-11-21 15:24 UTC, all seven downloads.** Every release job passed; the six zips were
  opened and counted (35 plugins and the trust manifest in each, the Windows zip with its NVDA, Dot
  Pad and liblouis files; the Windows WebHost holds 2.13.1's 566 files plus the launcher and README);
  the Windows setup installed, installed again over the top and uninstalled on the runner. The
  v2.14.0 release notes now begin by pointing to 2.14.1.
- **The website is on 2.14.1** (download page with the 2.14 news, sizes from the release, home page,
  llms.txt, structured data, the six docs pages, llms-full.txt). **Caveat:** the first upload wrote
  the three PHP files empty (`php -l` read the input before `cat` could) and the home and download
  pages answered 500 for about two minutes, 15:29 to 15:31 UTC, until the backup went back; the second
  upload was checked file by file against the drafts. Backup: `~/backups/trade-site-20261121-1529`.

## [2.14.0] — 2026-11-21

Everything since 2.13.1 (2026-11-16), one entry per piece of work, newest first, with what is
still unproven. The full entries as first written (every class, test and sabotage) are in
[`archive/CHANGES_DETAIL_SINCE_2.13.1.md`](archive/CHANGES_DETAIL_SINCE_2.13.1.md), condensed here
on 2026-11-21. **On the VPS:** all of it (a6f6d4bf, the tagged commit, deployed 2026-11-21 14:50 UTC,
Cody: "when done, update vps, cut 2.14"). **Nothing here has been tried with money.**

### 2026-11-21: Renko's narrow margin; the ticket's lists in full; a wider Find box; money amounts; release dates to December

Cody's answers to the second shoot's findings ("fix the renko bug", "fix order ticket being cut off",
"fix the symbol box issue", the release dates "add them if you want", "smaller nits: all of them").
In the same message: **"Headlines, brief, alt u all work"**, the first time any of the news desk has
been heard.

- **Renko, Kagi, Line break and Point and figure keep a narrow right margin.** The empty space after
  the last brick was the 10-bar margin candles have, so 23 daily bricks filled 23 of 33 slots. While a
  brick chart shows, the margin is a tenth of the bricks, from 2 to 10 (`PlaybackReducer.BrickMargin`):
  23 bricks leave 2 empty slots, 100 or more leave 10 as before. Going back to candles, and a new tab
  opened from a brick tab, give the 10 back. The smallest zoom is margin plus 10 on either. Two panning
  tests started at bar 12, which is now past the last start on their short series; they start at 10.
  Proved: with the margin fixed at 10 again, six of the new tests fail. Not looked at on screen yet.
- **The order ticket's lists show their whole choice.** Every list in the ticket was 120px wide, so the
  Margin, Time in force and Order type lists cut off past about sixteen letters ("Isolated — loss c").
  They now size to their longest choice and stop at the ticket's edge. The Margin choices are shorter
  too: "Isolated — loss capped at this position", "Cross — risks your whole balance". Measured in the
  CSS only; not re-photographed.
- **"No API key" on the ticket is amber, not green.** It is a step to take; green says all is well.
- **The Find symbol box is 12 characters wide** (was 8, which showed "TS" of TSLA).
- **A profit or loss reads as money**: "-120.00", "1,234.57", "0.50". It was formatted as a price
  ("-120.0000", "0.00250000"). An amount under a cent keeps its digits ("0.0025") so a tiny loss is
  never shown as 0.00. `PriceFormatter.FormatPnL`, used by the dashboard's positions and fills, the order log, and the
  strategy backtest's totals and trade list.
- **Shortcut names say "Comma", not ",".** Settings > Keyboard, Help and the shortcut list wrote
  "Alt+," for Custom scripts; a screen reader often skips a lone comma. Help's own table and the
  Custom scripts button's tooltip, which spelled it by hand, say "Alt+Comma" too. Every key that is a symbol
  is now written as its name, as the rebind announcements already did (`ShortcutWords.KeyWords`).
- **US release dates stored to 23 December 2026** (were 30 July). Fetched from FRED on 2026-11-21: CPI
  12 August to 10 December, jobs 7 August to 4 December, PPI 13 August to 15 December, GDP 26 August
  to 23 December. The ones already out (to 10 November) carry their first-print figures from the
  fresh ALFRED vintages; the scheduled ones (CPI 10 December, jobs 4 December, PPI and GDP from 25
  November) are kept with no figures: the builder now keeps any date after the fetch day
  (`FETCHED` in `scripts/build_market_events_seed.py`). The 3,171 earlier release dates and their figures
  are unchanged; the fresh fetch also lists 12 older dates the August one did not (for
  example CPI 29 February 2000), which were NOT added, so nothing before August changes. Proved: with
  the scheduled-date line taken out, nine seed tests fail. Without a FRED key, release countdowns stop
  after 23 December; FRED had no 2027 schedule on 2026-11-21. Fed decisions run to 2027 as before.
- **Not changed (Cody):** the non-emoji symbols (⚙ ⚠ ✔ ✕) stay as they are, and the hosted
  terminal's Compile button (no sandbox on the server) is not needed.

### 2026-11-21: every dialog laid out again; Renko opens full; no "Data link" line; no emoji — on the VPS 13:37 UTC; website re-shot

Cody, before the deploy: "the dashboard and tables read better now". Deployed c51a402f to both
services at 13:37 UTC (rollback-1121-133711; Capabilities unchanged, no error lines). The website's
ten screenshots were then shot again from the deployed build in an isolated copy (Chromium, 1280x760):
the hero's status line now says "Ready", the Renko card is the fitted first view, the TSLA shot is a
fresh copy with no paper position, and every changed image has new alt text. The dashboard card stays
cropped to the order ticket: the whole dialog shrank to about 470px in the site's two-column cards and
could not be read. The replaced images are in `~/att-release-tools/shots-archive/2.13.1-head-20261121b/`
on the VPS. The new shots showed four more small things, now in TODO ("Found by the website
screenshots").


Every dialog and every tab in it was opened in Chromium at 1280x760, 1920x1080 and 390x800 (phone
width), measured for sideways scrolling, controls past the edge, squeezed text and small targets, and
photographed. What was wrong and what changed:

- **Trading Dashboard.** The ticket and the account tables were two equal halves of a fixed 866px
  dialog, so the positions table squeezed each column to a letter ("B T C U S D" down a cell). Now the
  dialog uses the window (up to 1160px), the ticket takes 300 to 360px and the tables the rest, and
  under 900px they stack. Table cells break only between words; a table too wide for its box scrolls
  inside its own labelled box rather than the whole dialog. A row's Close buttons wrap their words
  ("Close / position") so at 1280 nothing is cut off. The ticket's rows wrap (Risk % and Size ran off
  at phone width); the Order Book tab's asks and bids stack when they do not fit side by side.
- **All dialogs (both app.css copies).** Labels wrap (the page-wide `label { white-space: nowrap }` is
  for the toolbar and pushed Alerts, Narration, Market events and the strategy builder sideways at
  phone width); fieldsets can shrink; text fields are at least 28px tall; tab rows wrap; a file
  picker's button is drawn like the dialog's other buttons (it was a white slab); a small window keeps
  a 16px gutter.
- **Settings > Keyboard and Help** name each action as the Go bar does ("Zoom in", not "ZoomIn") and
  each key in words ("Semicolon", "Menu key", not "OEM1", "CONTEXTMENU"); a chord wraps only after a
  "+"; the Rebind button is 28px. Settings is 780px wide. What a rebind SAYS uses the same words: it
  said "OpenHelp rebound. This removed the only shortcut for SpeakChartLayout" (found by CI's browser
  harness on fc09aa9c, whose rebind tests still looked up "Rebind ToggleNarration"; both fixed, and
  the harness now fails on an internal name in anything said during a rebind).
- **Levels report:** a styled table (numbers right-aligned in tabular figures, ATR to two places,
  thousands separated, dates kept whole), in its own scroll box; the summary's sentences start with a
  capital ("Below: 10 SMA at …").
- **Smaller:** Object Tree's buttons 28px with a border; Order Book, strategy walk-forward and script
  import rows wrap; the screener's timeframe field 5rem (was 8%, unreadable at phone width); Drawing
  tools' buttons and the watch list's empty option tidied.
- **Renko opens full.** A chart with fewer bricks than the view holds opened with seventeen bricks in
  its left sixth; it now opens zoomed to fit them, and going back to candles restores the zoom they
  left at (remembered per tab and in a saved workspace). The first fix passed its tests and still
  showed the half-empty view in the browser: the fresh-load `ZoomAction(100)` arrived after the fit
  and undid it; the zoom now goes to the remembered candle zoom while bricks show.
  `RenkoFirstViewTests` (9).
- **"Data link: HistoricalFilling" is gone.** The enum name was published on every chart load and,
  as nothing published the move to live, stayed on the status line. The state change is now logged
  only; every fault already says itself in words.
- **No emoji.** The paper banner's 📄, the dashboard's marks and Custom Scripts' folder (an HTML
  entity) drew as an empty box without an emoji font; they are IconSprite symbols now.
- **Guards:** `EmojiFreeUiTests` (every .razor and the UI code, comments skipped, entities caught),
  `DialogLayoutTests` (the value each rule ends on after the cascade, in both sheets and the dashboard's
  own), new `ShortcutWordsTests`. Each was reverted or sabotaged and went red: 13 sabotages in all.

**Caveats.** Measured and photographed in Chromium only (no Firefox, no Windows WebView2, no real
phone), and not seen by Cody or with a screen reader. Phone-width tables (dashboard, Levels, journal)
still scroll sideways inside their own boxes, which is deliberate. The dashboard's Details column
breaks a paper order id at its hyphen. The restored-tab Renko test pins the order of the actions as
the initializer sends them today, not the initializer itself. The `accessibility-lead` review agent
the hooks ask for is not registered, so no agent review was done.

### 2026-11-21: the brief gets Ctrl+Alt+B; the demo asks for an account; the website's Chart page and new screenshots — app changes not heard; on the VPS 10:45 UTC (after Cody's OK); website live

- **Website (live on accessibletrader.com, not in this repo):** the home page's "Drive it yourself"
  heading and small in-page demo are gone; a **Chart** link in the top navigation (and the footer)
  opens `/chart`, the demo terminal full screen under the site's own header, which stays visible.
  `/preview` now redirects there (301). The nav's "Create account" link hides once signed in.
  Sitemap and `llms.txt` updated. Backups on the server end `.bak-20261121-chart` and `-shots`.
- **New screenshots**, shot from the HEAD build (ahead of the 2.13.1 download) in an isolated copy on
  the VPS: the hero (the two toolbar rows with the chart type and price scale lists), Tesla, and one
  per feature card (Market watch, Renko, Sound Designer, the order panel, the Go bar, Market events,
  Alerts, Custom Scripts), each with written alt text; WebP checked against its PNG. The old images
  are archived on the server in `att-release-tools/shots-archive/2.13.1-head-20261121/`. The Go bar
  and News cards now name `/`, Ctrl+Alt+B and the headlines. What the shots showed wrong is in TODO
  ("Found by the website screenshots"). Not checked with a screen reader.

- **Ctrl+Alt+B** says the morning brief (Cody asked for Alt+B, which is the order book; he chose
  Ctrl+Alt+B). Saved shortcut sets pick it up through `UpgradeSavedProfile`. Help, SHORTCUTS and the
  manual list it.
- **The website demo's bottom line** is *"Create an account for full functionality."*
  (`DemoPolicy.FooterText`, Demo only; the desktop and the signed-in hosted terminal keep the
  copyright and risk line).
- Docs condensed: CHANGES 2.11.0 to 2.13.0 and this section's full text moved to the archive; the
  docs README's 2.3.0 to 2.11.0 status sections moved to `archive/README_STATUS_HISTORY.md`; the
  READMEs, Quick Start (new section 12, *The news desk*), manual intro and WHATSNEW say the terminal
  is also a news desk.
- **Shelved (Cody):** market events and headlines released while the browser is closed.

### The news desk, 2026-11-18 to 11-20 — the first market-events stage tried by Cody ("works amazing"); the rest not heard

- **Morning brief** (Go bar `BRIEF`, now also Ctrl+Alt+B): what changed, the pulse and today's
  market events as one sentence; what changed is marked heard only after it is said; at most six
  events, then "N more in the calendar". Caveat: with market events off it falls back to the
  important ones (my choice).
- **Alt+U, what changed** (stage 5b/5c): fills and stops, alerts, released events, connection
  problems, then unusual moves (a closed bar at least five times its chart's usual move, median of
  the last 46), the narration ladder of background and saved tabs, and once, how your positions
  moved since you left. Caveats: 46 and 5x are not tuned on data; the focused chart's ladder and
  formations are not recorded; no currency is named in position moves.
- **Market pulse** (Alt+P, stage 5a): a watchlist named Pulse, made on the first press, then what
  you hold, in one sentence; on a Dot Pad's braille strip an item at a time; Ctrl+Alt+P the detail
  (symbol, source, price, base, age; never fetches). Caveats: no Dot Pad has felt it; a slow
  provider is said as unavailable.
- **Go bar** (Alt+G or `/`, stages 6a to 6c): one box reaching every `SystemCommand` by code, name,
  alias, prefix or near-miss, plus settings, symbols (`GP BTC`), timeframes, chart types with their
  numbers (`RENKO 500`, `PNF 1% 3 HL`), `NEWS`, `BUY`/`SELL`/`FLAT`, templates. Caveat: symbols are
  searched on the selected venue only.
- **Market events** (stages 1 to 4b): CPI, jobs, PPI, GDP, Fed decisions, expiries, holidays and
  halvings on their bars, with first-print figures, a bell, `'`/Shift+`'`, Alt+Shift+D, live FRED
  dates and figures with a key, 15- and 1-minute countdowns that name your positions, a read-out
  with the first minute's move, the Alt+E calendar with "Alert me before" and release history,
  Kalshi's expected figures and surprise words, company **earnings** on their own charts (Finnhub
  key; EDGAR's minute when it has the filing), and the **Fed rate** from Kalshi (priced before,
  held/raised/cut after). Caveats: shipped dates end July 2026 (releases) and 2027 (Fed), so future
  CPI/jobs/PPI/GDP dates need a FRED key; Kalshi's settlement comes minutes after the statement and
  only from 2022; earnings older quarters only where EDGAR's twenty newest filings date them, and a
  coming report gets no countdown; nothing while the browser is closed (shelved).
- **Headlines** (stages 1 to 3 and the list): a stock's SEC filings from EDGAR the minute they are
  public (no key); Alpaca's Benzinga wire, optional and off by default (half-hourly batches from
  fiscal.ai on a free account, stocks only; importance retuned on 688 real headlines); Finnhub news
  for stocks and coins with each user's own key, only stories whose headline names the symbol; the
  same story from two wires or two publishers said once; one sentence a symbol a minute at most;
  each headline on the bar it arrived in, `.`/Shift+`.`, `NEWS` / `NEWS AAPL` in the Go bar, the
  dossier's Recent filings and Recent news tabs. Keys rearranged: `,` formations, `.` headlines,
  `'` events, Ctrl+Alt+Shift with each to toggle; Shift+`,` `.` `'` now reach the terminal from the
  browser. Caveats: the importance rules are my judgement; Finnhub's terms forbid a shared key, so
  the VPS cannot offer one; never run end to end against live EDGAR; the kept list empties on a
  restart; nothing is drawn on the bar for a headline; class shares (BRK.B) may be missed;
  `SymbolAssets.Split("XBTUSD")` misreads (in TODO).

### Chart types, scales and templates, 2026-11-19 to 11-20 — Cody heard the new types and approved three fixes; the rest not heard

- **Fifteen chart types** in the Chart type list (Alt+C): candles, Heikin Ashi, bars, hollow
  candles, volume candles, line, area, baseline, high-low, HLC area, Renko, range bars, line break,
  Kagi and point and figure, each with its own reading and sound; the price series takes the
  type's shape and name, so the arrows walk bricks, lines or columns. Built types have settings
  dialogs and Go bar numbers; Kagi thickness and P&F buy/sell signals are said as you arrow and once
  live; background tabs on built types say their new bricks.
- **Indicators on built types read the built bars** (they were computed on the time bars and drawn
  one value per brick; found in headless screenshots of all fifteen types); falling wicks are red.
- **Price scales** (Alt+L): regular, logarithmic, percent and indexed to 100; percent and indexed
  lead every price reading.
- **Alerts on built charts** fire on the real price; new Candle conditions New up, New down and
  Reversal.
- **Indicator templates**: `TPLS` saves a chart's indicators and panes, `TPL` replaces another
  chart's with them, Ctrl+Z undoes.
- Caveats: the percent base moves as you pan; the P&F percentage box is frozen at the price when
  chosen; a mistyped baseline base squashes the chart until changed; templates only replace, never
  add; the mouse/touch `hl2` fix is untested.

### Accounts, the leaderboard and the website, 2026-11-18 to 11-20 — deployed; not seen by Cody

- **Account page** (display name, leaderboard opt-in, password, email, data zip, delete with
  password, checkbox and 2FA), account email through Brevo, a three-second minimum on Register.
- **Leaderboard**: published from opted-in paper accounts (return, account value, trades, win rate,
  open positions); the site's "Top traders" section. Cody's practice account was copied to the
  server; he is not listed until he sets a name and opts in.
- **The website knows you are signed in** (`/terminal/account/status`), the home page rewritten
  (title, one main button, "What it does" in plain sentences, audio tour, FAQ, From the blog), and
  an **email list** that sends nothing until Cody adds a Brevo API key, a postal address and the
  timer (TODO).
- **Order log tab** in the trading dashboard: the newest 200 entries of `OrderAudit/*.jsonl`,
  filter and CSV.
- **Code review fixes**: a daily copy of the paper account, workspaces and alerts; every order
  written down; ATR-multiple stops placed from a true Wilder ATR. Caveat: replayed backtests with
  an ATR stop move a little.
- **Chart context menu** places text labels and horizontal lines in any pane (other drawings stay
  on the price pane). **Colour said the same way everywhere**; a volume profile row says its
  buying or selling share. Caveat: the split is by bar direction, not trade-by-trade.

### Indicators, speech, downloads and Cody's answers, 2026-11-16 to 11-17 — CI green, not heard

- A line's colour is said only where it changes; filled lines are "area"; DBSI's trend line has two
  tones; signals name the level they pass ("Large green dot below -60"); Cipher A's symbols sit
  above the candle; Cipher B's sell dot sits like its buy dot with the Zero line hidden. DBSI study
  closed (TradingView's Stoch RSI equals ours bit for bit).
- Cody's answers built: each Read-as form keeps its own sound; Add Indicator has an Advanced group;
  alerts can watch a left-out part; custom (Roslyn/Pine) indicators are computed in strategies,
  backtests and background tabs (they were computed nowhere off the chart); retired strategies move
  to `strategies.retired.json`; Cipher C's "Low is in" is back on weekly and 4-hour coin charts and
  "Low expected" is gone.
- The WebHost zip unzips to one tidy folder with one start file; its cache leaves the working
  folder; the browser harness is on Playwright 1.64.1 and CI back on ubuntu-latest.
- After the 2.13.1 tag: the release checked and the website moved to 2.13.1.

## [2.13.1] — 2026-11-16

### The Windows download: one folder with "Accessible Trader.exe" on top, and an installer (2026-11-16)

- **What Cody said:** "there's hundreds of files when I unzip the windows zip and finding the exe I'm
  supposed to run isn't always easy" … "do the repackaging cleanup on the zip and the exe. don't just call
  it setup.exe call it something meaningful".
- **The zip** keeps its name (`AccessibleTrader-Windows-<version>-win-x64.zip`, so links still work) and
  now unzips to one folder, `Accessible Trader <version>`, holding three things: **Accessible Trader.exe**,
  **README.txt** and **app**. The app folder is the publish as before (about 1,070 files), untouched.
  Accessible Trader.exe is a new launcher (`AccessibleTrader.Launcher`, NativeAOT, 1.5 MB): it starts
  `app\AccessibleTrader.BlazorClient.exe` with the app folder as its working directory (the Dot Pad SDK
  opens a file by a path relative to it), passes on any arguments, and exits. If the app folder is
  missing it says so in a message box, so a screen reader reads it, instead of failing silently.
- **The installer,** `AccessibleTrader-<version>-Windows-Setup.exe` (Inno Setup 6), is built from the same
  folder. It installs for the current user only, to `%LocalAppData%\Programs\Accessible Trader`, with no
  administrator prompt; adds a Start menu entry, an optional desktop icon and an uninstaller in Windows'
  installed apps list; and offers to start the app when done. The app is framework-dependent
  (`SelfContained=false`; its runtimeconfig asks for Microsoft.NETCore.App and
  Microsoft.WindowsDesktop.App 10.0, and the 2.13.0 zip has no coreclr.dll), which no doc in the repo
  said; only the website's download page did. The setup cannot carry that runtime (it installs
  machine-wide and needs admin), so it looks for
  `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\10.*` and, if none is there, says so and
  offers Microsoft's download page (a silent install takes No). README.md, the Quick Start and the zip's
  README.txt now say it. Installing a newer version over an older
  one empties `app\` first, so no older file is left beside the new ones. Settings, workspaces and logs
  live in `%LocalAppData%\AccessibleTrader` and are never touched by installing, upgrading or uninstalling.
- **Tried on the Windows runner, every release:** `packaging/windows/package.ps1` (run by `release.yml`
  and `diagnostic-build.yml`) starts the launcher and waits for the app's process from `app\`; installs
  the setup silently and compares every installed file with the staged one by SHA-256; checks the Start
  menu entry's target; plants an older file in `app\`, runs the setup again and checks it is gone; then
  uninstalls and checks the folder and the Start menu entry are gone. Any failure fails the job.
  First run (diagnostic run 43890759698, commit 211bbbf2): every trial passed, 1,069 files installed byte
  for byte both times; the setup is 85.1 MB, the zip 136.9 MB (1,067 files in `app\`), and the payload
  check passes the zip on both Windows rule sets.
- **The payload check** (`scripts/verify_release_payloads.py`) now finds the Windows payloads under
  `Accessible Trader */app/` and requires the launcher and README at the top (new rule set
  `maui-windows-package`). Its root is matched one folder at a time: matched against the whole path, a
  `*` also crossed folders, and a launcher placed inside `app\` passed (shown: the old matching passes such
  a zip, the new one fails it; the 2.13.0 flat zip fails both new rule sets; a zip without the launcher
  fails). Two new tests: `package.ps1` stages every top-folder file the manifest requires, and the release
  job runs the script without `-SkipTrials` and publishes `release/*.exe`. Each was made to fail by
  breaking what it guards (README staged into `app\`, `release/*.exe` dropped, `-SkipTrials` added, an
  unknown `stagedBy`).
- **The release** checksums the setup too, and fails if the Windows zip was built without its setup.
- **`diagnostic-build.yml` builds again.** It publishes with `ReleasePublish=true` but never fetched the Dot
  Pad SDK, so the SDK check added after its last green run (2026-09-22) stopped every run; nobody had run it
  since. It now takes release.yml's two vendor steps (the pinned Dot Pad SDK, the VC++ runtime).
- **Caveats:** the runtime check's "missing" path has not run anywhere (the runner has .NET 10);
  on Windows on Arm, where x64 .NET lives in `dotnet\x64`, it would say missing when it is not;
  nobody has gone through the setup's screens with a screen reader (the runner installs it
  silently); both the setup and Accessible Trader.exe are unsigned, so SmartScreen still warns; a
  Strategies drop-in put by hand into `app\Strategies` is removed by an upgrade (the documented place,
  `%LocalAppData%\AccessibleTrader\Strategies`, is not); the WebHost zips are unchanged (still flat);
  the macOS zip is unchanged. Only the Windows download changed: the app inside is 2.13.0's code.
  The launcher's publish warns NU1903 (GHSA-p3jp-9v83-76w9, a Brotli decoder overflow fixed in .NET
  10.0.14; the pinned SDK 10.0.301 carries 10.0.11). The launcher decompresses nothing, so NativeAOT
  leaves that code out; the self-contained WebHosts carry the same 10.0.11 runtime (see TODO).

### After the 2.13.0 tag: the Windows zip checked, the website and the hosted terminal on 2.13.0 (2026-11-16)

No code changed. Recorded here because TODO listed them.

- **The Windows zip is not precompiled**, the same as 2.12.0: `r2r-check.py` finds IL only
  (`AccessibleTrader.Core.dll` has no ready-to-run header). 2.13's notes make no precompile claim, so the
  2.12 correction notice on the site's What's new page was deleted.
- **The hosted terminal** (demo and accounts) runs the tagged commit `33901aa5` and says 2.13.0. Every
  post-deploy check passed, and the old copies were kept for rollback.
- **accessibletrader.com:** the six docs synced from the tag, not from main; the download page's sizes and
  "What's new in 2.13.0" rewritten, starting with a warning that ordering through a venue is untested;
  the front page says what is new in 2.13 (Kalshi, Hyperliquid, Coinbase US futures, the Market Cipher
  indicators), 35 providers and 21 drawing tools; the features page says eighteen trading venues and names
  the seven with no practice account; the structured data, llms.txt and llms-full.txt say 2.13.0. The FAQ
  had nothing out of date. The screenshots were not re-shot (TODO).
- **codyhurst.com:** a banner entry about Kalshi and Hyperliquid, which says Hyperliquid is not for the US
  or Ontario and that trading through them is untested; the terminal's card says 35 providers (18 venues),
  10,578 tests, Linux in the browser and the Linux sandbox.

Older releases (2.13.0 and earlier) are in [`archive/CHANGES_HISTORY.md`](archive/CHANGES_HISTORY.md).
