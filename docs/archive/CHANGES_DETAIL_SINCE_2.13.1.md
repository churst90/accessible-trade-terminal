# Changelog detail since 2.13.1 (archived)

The `## [Unreleased]` section of `docs/CHANGES.md` as it stood on 2026-11-21, before it was condensed to one entry per piece of work. Nothing in it was edited; later work is recorded in CHANGES.md itself.

---

## Unreleased, 2026-11-16 to 2026-11-21

### The morning brief (stage 5c), 2026-11-20 — not heard, not on the VPS

- **Go bar `BRIEF`** (`SystemCommand.MorningBrief`, aliases "brief", "morning brief", "briefing";
  dispatcher case beside `WhatChanged`; **Ctrl+Alt+B** added 2026-11-21 at Cody's ask, Alt+B
  staying the order book; saved profiles pick it up through `UpgradeSavedProfile`). `MorningBriefService` (singleton on the desktop,
  scoped on the web host) says "Gathering the brief.", then the catch-up press, the pulse and
  today's market events as one interface-tier sentence; "Still gathering the brief." on a press
  while it gathers; "The brief is unavailable." on a failure; a failed pulse is "The market pulse
  is unavailable." and the rest still speaks.
- **`ChangeLogCatchUpService.PrepareAsync`** returns a `ChangeLogPress` (sentence + mark step);
  `SpeakAsync` is now prepare, say, mark. The brief marks after its whole sentence is published.
- **`MarketPulseService.SentenceAsync`** (`IMarketPulseReader`): the Alt+P sentence without
  speaking or the braille strip, kept as the newest reading for Ctrl+Alt+P; shares Alt+P's gate.
- **Today's events:** the spoken clock's whole day as a UTC range; level from the settings (Off
  read as important only); kind boxes; `MarketEventNoticePlanner.Touches` on open and held
  markets; by time; six read, then "N more in the calendar.".
- Tests: `MorningBriefTests` (9) + one in `MarketPulseServiceTests`; guard rows in
  `Phase5KeyboardScopeTests` and `GoBarRegistryTests`. Sabotages listed in the commit.
- **Caveats:** not heard; events-off fallback to important only is my choice.

### Alt+U: position profit and loss since you left (stage 5c), 2026-11-20 — not heard, not on the VPS

- **The away mark keeps the open positions** (`ChangeLogCatchUpService.SetLastPresentAsync` writes
  the mark, then reads `IHeldPositionsReader` without the caller waiting, 30 s limit, into
  `ChangeLogDocument.AwayPositions`; a later mark drops an earlier mark's positions and a slow read
  never overwrites a later mark's). `HeldPositionsReader` reads the paper account in paper mode,
  else every active, non-withdrawal key's venue; one failed venue makes the read null.
- **The next Alt+U says them after the header** (`AwayPositionsWording`): made/lost the change in
  unrealized profit (two decimals, under half a cent "unchanged"), "opened", "closed", "size
  changed"; the venue is named when one symbol is on two venues; "could not be read" when the
  fresh read fails (5 s limit). With nothing else new: "Nothing new since 09:40. Positions since
  ...". Not on a follow-up press; cleared after it is said.
- Tests: `ChangeLogAwayPositionsTests` (15). 20 sabotages, all red (two needed a new test each; list in the commit).
- **Caveats:** no currency named; the desktop loses the positions (not the mark) if it exits
  within the read; not heard; not tried against a live venue.

### Alt+U: unusual moves and signals on watched charts (stage 5c), 2026-11-20 — not heard, not on the VPS

- **Unusual moves** (`UnusualMoveRules`, `ChangeLogCategory.Unusual`): at a bar close, the closed
  bar's close-to-close move against the median size of the 46 moves before it on the same chart;
  unusual at 5 times or more. "BTC/USDT 1h up 4.2% in one bar, 9 times its usual move." (one decimal
  from 1%, two below; times rounded down). A flat history, too little history and the forming bar
  are never judged. `UnusualMoveWatcher` (new, force-created in `AppStartupService`, registered
  beside `BackgroundBarAnnouncer` in both heads) checks the focused feed and live background tabs,
  never a leased feed, never in replay. `LocalBackgroundMonitor` checks every saved tab it watches
  with the browser closed and keeps a 49-bar history per chart (`MergeMoveHistory`: a bar already
  kept is replaced, newer ones added, capped; a poll that does not reach the newest kept bar starts
  again), so only the first poll fetches 49 bars; later polls stay at 3. Publishes
  `UnusualMoveEvent`; recorded, never spoken.
- **Signals** (`ChangeLogCategory.Signal`): `ChangeLogRecorder` files a background tab's
  `BackgroundBarClosedEvent` / `BackgroundBricksFormedEvent` when it carries a ladder, led by the
  chart ("ETH/USDT 4h: RSI crossed above 70."), and the monitor publishes `ChartSignalEvent` for the
  ladder it speaks. Keys are `signal|provider|symbol|timeframe|bar minute` (bricks: the minute
  now), so the browser and the monitor give one entry.
- **Changed:** a saved tab watched only for its bar closes now fetches 49 bars on its first poll
  instead of 3 (`HeadlessNarrationTests` updated to pin that).
- **Guards:** 21 sabotages (see the commit), all red.
- **Caveats:** the 46 / 5x numbers are not tuned on data; the focused chart's ladder and chart
  formations are not recorded; a poll gap longer than the fetch restarts the monitor's history.

### The market pulse on the Dot Pad's strip, and Ctrl+Alt+P for its detail, 2026-11-20 — not felt or heard, not on the VPS

- **The braille line.** Each Alt+P reading also publishes `TactileStripMessageEvent` with
  `MarketPulseWording.BrailleLine`: "SPX +0.4, BTC -1.2, 10Y 4.31 +3bp, DXY 0.0, Gold +0.2 4m,
  Oil n/a, VIX cl +2.1, SOL +5.0" (short names from the defaults table, else the coin's base asset,
  else the symbol; one decimal; "cl" closed; "4m" old; "n/a" unavailable; "Pulse empty").
  `TactileCanvasCoordinator` holds it on the strip one item a page (`StripPages`; an item longer than
  the strip is cut), through live ticks, until the cursor, focused series or focused line changes, or
  60 seconds pass. While held, the Dot Pad's pan keys step the pages instead of panning the chart;
  pan right past the last page gives the strip back. The x-value revert timer leaves a held message
  alone.
- **The detail read, Ctrl+Alt+P** (`SystemCommand.MarketPulseDetail`, Go bar `PULSED` / "pulse detail"
  / "pulse sources"): the service keeps the last reading and its time; the key reads it item by item
  ("Pulse read 3 minutes ago. Bitcoin: BTC/USD on Coinbase, 94210.50, down 1.23% from midnight UTC
  95387.60, last minute bar ended 1 minute ago.") and never fetches. Before any reading: "No pulse
  read yet. Press Alt+P first." `MarketPulseQuote` gained Short, Provider, Symbol, Last, Reference,
  Start (`MarketPulseStart`) and QuotedUtc, set by `MarketPulseMath` for every answer, including
  unavailable ones.
- **Guards:** 15 sabotages, all red: ticks releasing the held line, the cursor ignored, pan keys
  still panning, the last page never releasing, the whole line as one page, no subscription, two
  braille decimals, long names on the strip, the age dropped, one detail decimal, no strip publish,
  the reading not kept, a fixed crypto start, the quote time at the bar's start, and no provider.
- **Caveats:** no Dot Pad has felt it (none here); pages are cut by characters while Grade 2 changes
  the cell count; an item from daily bars only says "daily bars only", not a time; held positions
  appended to the reading get the symbol as their short name.

### Market events: the Fed rate from Kalshi, 2026-11-20 — not heard, not on the VPS

- **A "Fed rate" figure on Fed decisions** (`MarketEventFigureUnit.Rate`, always two decimals). Kalshi's
  KXFED ladder (FED before 2026; the ticker names the meeting's own month, unlike a release, which
  names the month before) gives the **expected** upper bound, the median on a 0.25 grid, read up to
  fifty days ahead (releases stay at fourteen). After the meeting, the **settled** ladder gives the
  decided rate: the first "no" strike, only when it sits exactly one step above the last "yes" and
  every rung has a result; anything else gives none. The **previous** is the newest settled meeting
  of the same source within sixty days. A past meeting stays unfinished, and is read again, until
  its ladder has settled; the settled rate is saved in the expected-figures cache.
- **Words:** "held at 4.00%", "raised to 4.25% from 4.00%", "cut to 3.75% from 4.00%", "rate 4.25%"
  without a previous; "higher than expected" / "lower than expected" / "as expected"; "rate now
  4.00%" before the meeting; the countdown "Fed decision, expected 4.25%, in 15 minutes."; Alt+Shift+D
  adds the source sentence. A release never carries the Fed figure, and a Fed event carries nothing
  else.
- **Bug found on the way:** `KalshiExpectedFigures.Decimals` used a log rule that gave 1 for a 0.25
  step, which would have said 4.25 as 4.3. It now counts the step's own decimals.
- **Data checked live, 2026-11-20:** October 2026's ladder settled at 4.00 (its last hourly candles
  before 2 PM give a median of 4.00 too); December 2026's open ladder prices 4.25 (91% above 4.00,
  2.5% above 4.25). Thirteen trimmed responses saved as fixtures.
- **Guards:** 15 sabotages, all red: the log decimals, the month before, the gap rule, a yes above a
  no, an open rung skipped (first green, because the gap rule caught the only case; an open TOP rung
  was added), a past meeting final without its result, the actual missing from the cache, the
  sixty-day window removed, a fourteen-day Fed horizon, the past pass Economy-only, the calendar
  Economy-only, the Fed figure on releases, one decimal, hot/cool words for a rate, and raise/cut
  swapped.
- **Caveats:** the decided rate comes from Kalshi's settlement, minutes after the statement, and only
  where Kalshi had a market (2022 on); unscheduled meetings get nothing; Kalshi's traders are not the
  fed funds futures the news quotes; about forty past meetings share the forty-a-pass backfill budget.

### Market events stage 4b: company earnings on their own charts, 2026-11-20 — not heard, not on the VPS

Cody: "Last finish up the remaining bloomberg type items." Earnings were the first.

- **`MarketEventEarningsFeed`** reads, for each stock on an open chart (the focused one and every
  tab), Finnhub's `calendar/earnings` (120 days either side) and `stock/earnings` (the last four
  quarters) with the user's Finnhub key in the `X-Finnhub-Token` header, plus EDGAR's recent filings.
  Once per company per six hours; anything short of a good read (no key, refused, limited, failed)
  is tried again after two minutes, so a key added in API Keys is picked up within minutes. Never
  while market events are Off or the new Earnings box is clear. In memory only.
- **`EarningsEvents.Build`** (pure): one event per report day. When EDGAR has an 8-K with item 2.02
  accepted on the same New York day, the event is Exact at that minute ("time from SEC EDGAR");
  otherwise it is day-only with a session from Finnhub's hour (`bmo` before the open, `amc` after
  the close). Figures: EPS with the previous fiscal quarter's actual, and revenue, each against
  "analysts on Finnhub". Older quarters from the EPS history are added only where an EDGAR results
  filing falls within 100 days after the quarter's end. Finnhub's duplicated calendar rows are kept
  once per date; a body that is not the expected shape is nothing, not an exception (the first
  version threw on an array where an object was expected; a test caught it).
- **Placement:** a day-only report with a session goes on the day's last intraday bar ("after the
  close") or first ("before the open"); daily charts keep the report-day rule.
- **Speech:** "AAPL earnings after the close, EPS 1.85, beat 1.77 expected, revenue 102.5 billion,
  beat 102.2 billion expected"; "as expected" and "missed" likewise. The session word is said once.
  `Alt+Shift+D` gives "Figure: EPS 1.85, expected 1.77, by analysts on Finnhub, beat estimates.
  Previous quarter: EPS 1.57. Revenue: …". New figure units PerShare and Money.
- **One company's charts only.** Events carry a `Symbol`; the bar map filters by the chart's symbol
  and keys its cache on it and on the feed's version; the live notices and the Alt+E calendar's
  "only touching" count a report only while one of its charts is open.
- **Setting:** "Company earnings, on that company's charts" (`narration.marketEvents.earnings`, on by
  default) in Settings → Narration, on the Alt+E screen, and in the Settings search. A host narration
  preference, deliberately not carried into the script sandbox (like the Alpaca switch). The Alt+E
  screen has a third status line ("Earnings unavailable, Finnhub key not set.", "Earnings: Finnhub,
  read for 2 companies.").
- **Wired on both heads** (desktop singleton, hosted scoped), with its own allow-listed HTTP client
  for finnhub.io.
- **Tests:** `MarketEventEarningsTests` (parsing, building, placement, speech, the bar map's symbol
  filter and rebuild, the feed's reads, gating and retry, open stock tabs) and a live-notice theory.
  Twelve sabotages, each turned red: the EDGAR minute dropped, any 8-K counted as results, the UTC date used instead of New York's, the 100-day history window removed, an after-close report put on the first bar, the bar map not filtered by symbol, the map key ignoring the feed's version, live countdowns for every company, the session said twice, the earnings tick box ignored, no key waiting six hours, and the calendar dedupe dropped. The window sabotage first stayed green: the only late filing in the test was a 10-Q, which the form filter already drops. A real results 8-K 100+ days past every quarter was added, and it went red.
- **Caveats:** not heard; not on the VPS; the app's own read has not run against the real Finnhub
  (the shapes were checked live with Cody's key the same day); how soon Finnhub fills in the actuals
  after a report was not measured; Finnhub's calendar lists only the latest report and the coming
  ones, so a busy filer's older quarters may be missing (EDGAR's feed holds twenty filings); no
  countdowns for a coming report; dividends and splits not built; each user needs their own key
  (Finnhub's terms allow no shared key, so the VPS cannot offer one as it does FRED).

### Headlines on the bars, the NEWS list, the keys rearranged and Alpaca made optional, 2026-11-20 — not heard, not on the VPS

Cody: "finish the last headline items. for the keyboard shortcuts we may need to change them up
because they're inconsistent … , and shift , should navigate chart patterns, . and shift . navigate
headlines and ' and shift ' navigate events … why not have / open the command bar. Is alpaca really
needed now as a backup if we're using finhub?" and then "ctrl+alt+shift+, . and ' should … toggle
those settings".

- **Headlines kept, said or not** (`HeadlineHistory`). Every story the gate matched to a symbol is
  kept (up to 3000), whether it was said or held back by the per-symbol minute, plus every SEC filing
  with an importance. The same story from the second wire, in other words within two hours, is kept
  once per symbol, as the gate already says it once.
- **Each headline on the bar it arrived in** (`HeadlineChart.Place`): the bar whose span holds its
  time; between bars (overnight, weekends) the next bar; after the newest bar ended, the newest bar;
  before the chart, nowhere. Stocks and coins only; brick charts carry none (a brick has no clock).
  - Arrowing onto such a bar adds "Headline: Apple Reports Q4 Results. 2 more headlines." — the most
    important first, only the ones the Headlines level lets through, nothing with the level off.
  - `Alt+Shift+D` reads every headline on the bar, whatever the level: "2 headlines on this bar:
    Nov 20 14:02 UTC, Yahoo, important: …".
  - **Period / Shift+period** jump to the next / previous bar with a headline at the level, read as
    the arrows read it (`HeadlineNavigator`). With the level off they say so.
  - **The chart's own past** (`HeadlineBackfill`): on first look at a stock or coin chart, Finnhub's
    company news for it (only stories that name it) and its SEC filings back to the chart's first bar
    or 30 days, read again every half hour, two minutes after a failure, never with headlines off.
    A coin gets only Finnhub's newest hundred or so crypto stories (no older list on a free key).
- **The Go bar's `NEWS` list** (also `HEADLINES`): every kept headline, newest first, the newest 200
  listed: "AAPL: Apple Reports Q4 Results. Important, Yahoo, Nov 20 14:02 UTC, not said." `NEWS AAPL`
  is one symbol's, other words search them. Enter goes to its bar on this chart or says which symbol
  it is for ("GP MSFT opens it"). An empty list says why: none yet, or headlines off and the key that
  turns them on. New codes `HDLN`, `EVTS`, `CPAT` reach the three settings (`news` no longer finds
  the economic calendar).
- **Keys: one family per kind of thing, the shifted key going back.** `,` / `Shift+,` chart formations
  (were `,` back and `.` forward), `.` / `Shift+.` headlines, `'` / `Shift+'` market events.
  `Ctrl+Alt+Shift` with the same key switches each on or off: "Describe chart patterns on.",
  "Headlines off.", "Market events on, important and medium." Back on returns the level it had; from
  off (or the first press after a restart) it turns on at important only. `/` opens the Go bar too.
  - **A saved shortcut profile is moved to the new keys when it loads,** only where it still has the
    old defaults: the formation pair is moved only while `Shift+,` is free, slash is added only while
    the Go bar is still on `Alt+G`, and each new command gets its default only if the key is free.
  - **Found and fixed: `Shift+,`, `Shift+.`, `'`, `Shift+'` and `/` were not in the page's trapped-key
    list.** On the chart they could reach .NET only through the chart's own fallback handler, which
    does not stop the browser, so Firefox's quick find opened over the chart on apostrophe; away from
    the chart slash reached nothing. With Alt held, a layout can report another
    character for the key, so the punctuation is now read from the physical key there. All of them
    fire only with the chart focused; slash only outside a text box, where it types a slash.
  - Spoken key names: `OEM7` is "Apostrophe" (the toolbar and menu would have said "OEM7").
- **Alpaca's wire is now optional, off by default** (Settings → Narration → "Also read Alpaca's news
  wire"). Cody asked whether it is still needed: no. Finnhub's free key covers the same stocks sooner
  and has coins; Alpaca's free feed is fiscal.ai in half-hourly batches, stocks only. Switching it off
  closes its socket but leaves the dedupe it shares with Finnhub alone — clearing it, as the wire-off
  path does, would have made Finnhub say its held stories again.
- **`SymbolAssets.Split` no longer reads XBTUSD as XB priced in TUSD.** The longest quote wins unless
  it leaves a base under three letters: XBT/USD, DOT/USD, BAT/USD; BTCFDUSD stays BTC/FDUSD and OPUSDT
  OP/USDT. Its callers include the sector hint and the COT lookup.
- **Not done:** the Finnhub key cannot go on the VPS for everyone to share — Finnhub's terms forbid
  redistributing its data or sharing a key without its written approval (the hosted site's users can
  enter their own free key; EDGAR filings work for everyone with no key). Nothing is drawn on the bar
  for a headline yet (speech and the detail key only). The kept list is in memory: a restart empties
  it. Finnhub's listing delay is still unmeasured (needs US weekday hours).
- Tests: `HeadlineNewsListTests` (new) and additions to the Alpaca, key-binding, keyboard-scope and
  symbol-split tests; `BarListKeyBrowserTests` (new, real Chromium): each of the six keys answered
  exactly once on the chart and cancelled there, and slash opening the Go bar from the chart and from
  a toolbar button. 33 sabotages, each red (listed in the commit). Two keyboard.js sabotages were
  green against the first version of the browser test, which only counted answers: the chart's
  fallback handler answers these keys with or without the trap, so the test now pins what only the
  trap gives — the keydown cancelled, and slash working off the chart.

### Headlines, stage 3: Finnhub news for stocks and coins, said once with Alpaca's, 2026-11-20 — not heard, not on the VPS

Cody: "Do C finhub and then assess whether B is needed" (B being companies' press-release feeds),
with the Finnhub key in his API-keys folder. The key was used read-only for news and was never
printed, logged or committed.

- **What Finnhub's free key gives (checked live 2026-11-20).** `company-news` for one stock at a time
  (Yahoo Finance, CNBC, Benzinga, Seeking Alpha, ChartMill: 1,800 stories for 22 stocks over two
  days) and `news?category=crypto` (Cointelegraph, CoinDesk: about a hundred, no symbols). The
  news socket is paid-only ("You don't have access to this resource"), so the terminal polls.
- **The poller** (`FinnhubNewsLiveNotices`). The same stocks and coins as the Alpaca wire. One
  request every 4 seconds at most (15 a minute, a quarter of the free key's 60, leaving room for
  the Finnhub price data on the same key); each stock, and the crypto list once for all coins, read
  every minute, or every 4 seconds × the number of lists when there are more than fifteen. The
  first read of a list is a baseline (only the last five minutes are said), a dropped stock starts
  over, a refused key waits 30 minutes, "too many requests" a minute, no key sends nothing.
- **Only stories that name the symbol** (`NewsStoryWords.NamesCompany`, `NamesCoin`). More than half
  of a stock's list is about other companies (Apple's on 2026-11-20 held Dell, Micron and market
  wraps), so the headline must name the ticker or the company. The company name is the SEC's
  (`ICompanyNameSource`, from the EDGAR ticker list the filings already read), trimmed of Inc,
  Corp, Holdings and words like "Advanced" or "Bank" (a name made only of those is matched as its
  phrase: "Bank of America", "Advanced Micro"), plus a short table of other names (Google,
  Facebook, MicroStrategy, Berkshire). A coin needs its symbol in capitals or its name with its
  capital letter (25 coins in a table, so "stellar results" is not Stellar).
- **Said once across both wires.** One shared `NewsHeadlineGate` now serves Alpaca and Finnhub: a
  story is keyed by wire and id, and a story whose headline shares 60 percent of its words with one
  already offered for that symbol is the same story, so a fiscal.ai story and a Yahoo story with
  the same title, or two publishers' copies, are said once. Both wires' stories for a symbol are
  gathered into the same sentence and the same one-a-minute cap.
- **Importance** gained Finnhub's filler, read on its 685 stories of the day (Low 617, Medium 41,
  High 27): questions, "how to", "what to know", "Here's why", call transcripts, key-metrics notes,
  "Stock Market Today" wraps, market-size reports, short-interest notes, and every Seeking Alpha
  piece (signed opinion). Subpoenas became important; investigations, outages and signed deals
  medium.
- **The dossier's Recent news** reads both services at once: Finnhub's last seven days (or the crypto
  list) and Alpaca's, newest first, each story once. One service answering is enough; with none, a
  refused key is named ("Finnhub refused the key in API Keys"), and with no key at all it says to
  add a Finnhub or Alpaca key.
- **Settings.** The news-wire hint names both keys; settings search finds Finnhub, Yahoo, CNBC,
  Cointelegraph and CoinDesk.
- **Tests.** `FinnhubNewsTests` (parser on real captures cut short, naming rules, importance on real
  headlines, the shared gate, the HTTP client's header and errors, the dossier sources, the poller
  on a fake clock, the host wiring) and a startup test. Sixteen sabotages (no naming filter, the key
  in the URL, no spacing, no baseline, a gate per wire, no dedupe across wires, and others) each
  turned them red.
- **How fresh, measured only roughly.** Walmart's results 8-K was accepted by EDGAR at 11:58:52 UTC
  on 2026-11-19; Finnhub lists a Yahoo story on them at 12:00 and four more by 12:15. How long
  Finnhub takes to list a story after its time was not measured: the recorder ran on a Friday
  evening after the US close, when almost nothing new came.
- **Caveats:** not heard; the listing delay above; a big event brings many differently worded
  stories (twelve on one short-seller report about AMD), each said, at most a sentence a minute per
  symbol; "What This Means for AAPL Stock" deal stories stay medium; each WebHost browser circuit
  polls on its own (two windows share the 15 a minute at worst 30, still under 60); the key is shared
  with the Finnhub price data; Finnhub's general market news is not used; a class share (BRK.B) is
  asked by its own symbol, unchecked.

### Headlines, stage 2 checked live: Alpaca's stories are fiscal.ai's, half-hourly, stocks only; importance retuned on 688 real headlines, 2026-11-20 — not heard, not on the VPS

Cody made a new Alpaca key pair ("try now"), then chose option A: "begin A now". The live pair was
used read-only for news, never for orders, and its keys were never printed, logged or committed.

- **What the live feed is.** The handshake, story frames and REST list are as documented (a stream
  frame holds one story and has no `images` field). The 20:01 UTC batch was ten stories; the stream
  and REST agreed story for story, none missed, each delivered within a second of its time. But on
  this free account **every story (688 that day) was fiscal.ai's**, one symbol each, no author, not
  Benzinga's; they arrive **in batches about every half hour** (:01–:05 and :31–:33 UTC), so a story
  can be up to half an hour behind its event; and **not one was about a coin**. Said on arrival
  still holds, but arrival is the batch, so this is a slow second source, not a real-time wire.
- **Importance retuned** (`NewsStoryWords`). The Benzinga-shaped rule ranked results-date notices,
  law-firm ads and price-target notes up. Retuned on the day's 688 headlines: Low 526, Medium 91,
  High 71, each read by me. Now Low first: results dates and conference calls, conference
  appearances, law-firm notices, holdings and director-dealing notices, advisers, routine buyback
  reports, meetings and proxies, bonds/notes/loans, price-target and ratings notes, and a plain
  dividend declaration (was Medium). Medium: upgrades/downgrades (but not "upgrades to OTCQX") and
  new coverage, takeover talk ("in talks", "opposes", "proposed", a "% stake"), officer changes,
  contracts and orders, partnerships and joint ventures, lawsuits and probes ("investigation
  into/of", not a geotechnical investigation), job cuts, dividend rises, trial data. High: results
  (including "results for the quarter ended"), guidance (but not "guidance on flood-damaged
  vehicles"), agreed takeovers and go-privates, listing and non-compliance notices, FDA decisions,
  offerings, hacks, a CEO leaving, no cash distribution.
- **Fixtures are real captures** now (a stream frame, the two newest AAPL REST stories; article text
  cut short), and the entity decoding has its own inline test. 42 new test cases, 39 of them real
  headlines from the day (two reworded to reach one pattern each, marked in the test). Twenty-one
  sabotages of the rule each turned the tests red; five were green at first because another
  pattern covered the same headline, and a headline only that part catches was added for each.
- **Wording.** The settings hint, the manual, WHATSNEW, the design doc and code comments no longer
  say Benzinga or real time; they say fiscal.ai on a free account, half-hourly, no coins. Settings
  search finds "fiscal.ai" too. The dossier's Recent news source shows "Fiscal.ai".
- **Caveats:** the rule is still my judgement (dividend declarations moving to Low is the change to
  check); known miss: "Take-Two GTA VI $2.36B first-day sales" is Low; the coin symbol form and the
  402/406/409 codes stay unchecked (no coin story, no error); the paper pair in Cody's key file still
  has no secret, and the app's Alpaca slot also drives the Alpaca trading venue, so it wants the
  PAPER pair, never the live one.

### Headlines, stage 2: the news wire (Alpaca/Benzinga), 2026-11-20 — not heard, not on the VPS (checked live the same evening: see the entry above)

Cody: "ok start stage 2 with alpaca." Both Alpaca key pairs in his patches folder were refused
(401) the same day, so this was built from Alpaca's documented message shapes and has never
connected; it does nothing until a working key is in API Keys.

- **Said on arrival.** One websocket to Alpaca's news stream (`v1beta1/news`, Benzinga's wire),
  authenticated with the Alpaca key checked out from API Keys at connect time and dropped (never kept
  or logged), subscribed to every story; the terminal keeps those about a touched symbol: the stocks
  the filings use, plus the coin of every open crypto chart and every held coin (BTC/USDT, XBT/USD
  and BTC-USD are all BTC). Spoken exactly like the filings (alerts tier, Headline sound, toast,
  Alt+U Headlines): *"AAPL: Apple Reports Q4 Results, Beats Estimates. 2 more headlines."*
- **Importance from the headline's words** (`NewsStoryWords`): options/whale notes, "why shares are
  moving", movers, previews, and any story about more than four symbols are Low first; then results,
  guidance, takeovers, bankruptcy, FDA and ETF decisions, halts, offerings, SEC/DOJ/FTC charges, CEO
  departures, recalls, dividend cuts, splits and hacks are Important; upgrades/downgrades, dividends,
  buybacks, deals, lawsuits, layoffs and executive appointments are Medium; the rest Low.
- **Gate** (`NewsHeadlineGate`): a story id is said once (edits are not said again); a symbol's first
  story waits 3 seconds for the burst, then one sentence, then a minute's cooldown for that symbol;
  older than two hours is dropped, ten minutes or more adds *"Published N minutes ago."*; a symbol no
  longer touched is dropped unsaid.
- **Connection** (`AlpacaNewsLiveNotices`): no socket while the Headlines level is Off, the new
  switch is off, there is no key, or nothing is touched; reconnects after 1 minute doubling to 15;
  a socket down two minutes is replaced; Alpaca's 402 (key refused) waits 30 minutes, 406 (connection
  limit) 5, 409 (no subscription) 6 hours.
- **Setting:** Settings, Narration, Headlines, **Include news wire stories** (on by default; nothing
  happens without a key), searchable, kept with the narration preferences, out of saved workspaces.
- **Dossier:** Alt+I has a **Recent news** tab for stocks and coins (twenty newest from Alpaca's
  REST news, New York time, headline, source, importance), and says when there is no key or the key
  was refused.
- **Code:** `AlpacaNews.cs` (parser, words, gate, wording), `AlpacaNewsSource.cs` (`INewsSource`,
  REST), `AlpacaNewsLiveNotices.cs` (`INewsSocket` over `ReconnectingWebSocket`), the dossier
  section, registration on both heads, `AppStartupService`. 52 new tests on two fixtures that are
  Alpaca's documentation examples (not captures); fourteen sabotages each turned their guard red
  once one gap was closed: the settings write-back (store to saved settings) was unguarded for the
  new switch and, from stage 1, for the Headlines level; both tests now cover it and both
  sabotages go red.
- **Caveats:** never connected to Alpaca; the importance rule is my judgement; on the WebHost every
  browser window opens its own socket and Alpaca allows one, so a second window waits on 406;
  nothing while the browser is closed, and the live stream does not replay what was missed (those
  stories are in the dossier's Recent news); no cross-source dedupe yet
  (waits for Finnhub); the Go bar news list is stage 2b. Found on the way, not fixed:
  `SymbolAssets.Split("XBTUSD")` reads XB in TUSD (in TODO).

### Headlines, stage 1: a company's SEC filings said the minute they are public, 2026-11-20 — not yet heard by Cody, not on the VPS

Cody: "start with EDGAR, then file the headlines plan in TODO ... edgar first step with headlines,
yes." The whole plan (EDGAR, then Alpaca's Benzinga wire with Cody's key, then Finnhub; CryptoPanic
only if crypto proves thin, its credit in the manual and never spoken) is in
`docs/MARKET_EVENTS_PULSE_GOBAR_DESIGN.md` section 4 and in TODO under Headlines.

- **Said on arrival.** For every stock on an open chart tab and every held stock (paper or venue),
  EDGAR's per-company Atom feed is read once a minute while EDGAR accepts filings (weekdays 6 AM to
  10 PM New York) and every 15 minutes otherwise. A new filing is said on the alerts tier (Shift+F2
  mutes it): *"AAPL filed an 8-K: earnings results."*, one sentence per stock per read, the rest
  counted (*"2 more filings."*), the age added past ten minutes (*"Accepted 25 minutes ago."*), and
  nothing said more than two hours late. The first read of a stock is a baseline (except a filing
  from the last five minutes), so opening a chart reads nothing out.
- **What counts.** An item and form table in `EdgarFilingWords`: earnings (8-K 2.02), bankruptcy,
  cybersecurity incidents, write-downs, delisting notices, auditor changes, non-reliance, change in
  control, tender offers and merger proxies are Important; 10-Q/10-K, officer changes, other 8-K
  items, 13D, S-1 and late-filing notices are Medium; insider Forms 3/4/5/144, 13G and proxies are
  Low. Prospectus supplements, free-writing prospectuses, extra proxy material and 25-NSE are never
  said.
- **Setting:** Settings, Narration, **Headlines level** (Off by default, Important only, Important and
  medium, All); Off fetches nothing. Searchable in Settings. Kept with the other narration
  preferences, out of saved workspaces.
- **Sound:** its own short rising triangle-wave blip (two notes, three when Important), not the
  market-event bell; Shift+F3 silences it; the Sound Designer lists **Headline** for a patch.
- **Also:** a desktop notification titled "Headline" when the window is hidden; Alt+U keeps each one
  under a new **Headlines** category, read after Events; the asset dossier (Alt+I) on a US stock
  has a **Recent filings** tab (twenty newest, New York acceptance time, words, and whether it would
  be announced), from the submissions document it already fetched.
- **Code:** `Core/Services/Headlines/` (parser, words, tracker and wording, `EdgarHeadlineLiveNotices`),
  `HeadlineEvent`, `ICompanyFilingSource` on `EdgarCompanyProfileSource` (which now also backs off
  ten minutes after a failed ticker-index load), `IHeldPositionReader` on `MarketEventLiveNotices`,
  registered on both heads and resolved by `AppStartupService`. 59 new tests with fixtures captured
  from EDGAR on 2026-11-20 (Apple's Atom feed and a trimmed submissions document; the two shapes
  agree filing for filing); ten sabotages each turned their guard red.
- **Caveats:** never run against live EDGAR end to end (the poller was tested on a fake source and
  clock); the importance table is my judgement; tickers are matched as EDGAR spells them, so class
  shares such as BRK.B may be missed; nothing while the browser is closed (the same gap as market
  events); not heard by Cody; not deployed.

### Website: an email list (sends nothing until Cody adds a Brevo API key) and two FAQ answers brought up to date, 2026-11-20 — not yet seen by Cody

Cody: "fix the FAQ out of date questions", "add the email sign up box. I'll write, you send", "add the
blog posts section" (the last was already live: "From the blog" at the foot of the home page, only
posts tagged trading, Accessible Trade Terminal or Accessible Trader).

**FAQ** (`faqs` table, rows backed up to `/home/debian/faqs.bak-20261120-stale.json`): "How much does
it cost?" now says nothing, links the donate page and drops the old "pricing page" link (that URL
redirects to Donate). "Where does my data go?" now lists what the free web account keeps (email, a
one-way hash of the password, settings and sounds, workspaces, indicator templates, strategies, the
paper record, and a display name and leaderboard place only if chosen) and mentions the email list.
The /faq page's search-engine data follows the table, so it changed too. The macOS Keychain answer was
right and is unchanged (the TODO note that said otherwise was my mistake, corrected).

**Email list** (live site files, backups `*.bak-20261120-signup`):
- The home page ends with "Get the news by email": one Email address field and a "Join the email list"
  button (also on its own page, /subscribe; listed in the home page's "On this page" links). A hidden
  field traps bots, the form needs the page's session token, and five signups an hour per network.
- Double opt-in: the address is saved as pending and gets a confirmation email. Its link opens a page
  with a Confirm button; only pressing it confirms (mail scanners open links, so a link alone must not
  sign anyone up). Signing up an address already on the list sends nothing and shows the same page, so
  the form cannot be used to find out who is on it. Unconfirmed addresses are deleted after 30 days.
- Every list email carries an unsubscribe link (a page with an Unsubscribe button) and the headers mail
  apps use for their own one-click Unsubscribe, which work without the button.
- Sending is Brevo's HTTP API from PHP (`includes/mailer.php`), reading `BREVO_API_KEY` from the site's
  env file. **The key is not there yet, so nothing is sent**: signups are kept as pending and the
  after-signup page says the confirmation "will follow shortly".
- Cody writes, I send: `/home/debian/att-mail/newsletter.php` takes a file whose first line is
  `Subject: ...` followed by Markdown. `preview` writes the HTML, `test` sends one copy to Cody, `send`
  is a dry run until `--confirm`. Each address is recorded per issue, so a send that stops (Brevo's
  free plan allows 300 emails a day) carries on later without sending anyone a copy twice. `send`
  refuses until a postal address is set (US law for list mail).
- Privacy page: new bullets for the email list (including that Brevo delivers it) and the free web
  account; its one-line summary now says "or set up in your free account".
- Tables `mail_subscribers` and `mail_sends` (`sql/mail_list.sql` on the server).

Checked: a staging copy on the server against a fake Brevo ran signup, the confirmation email's
contents, repeat signups, confirm by GET (asks only) and POST, a newsletter dry run, send, re-send (no
repeats), one-click unsubscribe, rejoining (new link), Brevo refusing (pending, sent later by
`confirm-pending`), no key (nothing sent), bad address, missing session token, the bot trap and the
rate limit. That run caught one bug, fixed: an issue's subject swallowed the body. Headless Chromium at
1280 and 390 wide, light and dark: no sideways scroll, the bot field is skipped by Tab, a bad address
comes back as an alert with the field marked invalid, no duplicate ids. The live site then took one
test signup through Apache, opened its confirm link and one-click unsubscribed it (row deleted).
Not done: a real email (no key), the 10-minute timer that sends owed confirmations (installing it
needs sudo and was refused; unit files and steps in `/home/debian/att-mail/systemd/`).

### Website home page: shorter intro, audio tour moved up, FAQ, From the blog, a line for VR counselors, 2026-11-20 — not yet seen by Cody

Cody's answers to the "what would you add" list, installed on the live site (files in
`/var/www/html/trade.codyhurst.com`, backups `*.bak-20261120-faq`; the old intro paragraph saved as
`~/hero_lead.bak-20261120-faq.txt` on the server).
- **The intro under the why line** (the `hero_lead` setting in the site database) is one sentence:
  "A free, open-source trading terminal you run entirely by keyboard, and read through speech, sound or
  braille." The old paragraph listed features "What it does" already covers.
- **"Take the audio tour" and the live demo moved up** to straight after the page's contents list, so
  hearing a chart comes before reading about it. The contents list follows the new order.
- **"Questions people ask first"**: five questions above "Why financial literacy matters" (is it free;
  does it ever hold my money; where are my exchange keys kept; which screen readers; do I need to know
  how to trade), with a link to the full FAQ. Answers written from the code: the web account trades play
  money only (real money is desktop-only), and the desktop app encrypts exchange keys on your own
  computer. The plus and minus signs on every FAQ question (here and on /faq) are now hidden from
  screen readers, which read them as part of the question before.
- **"From the blog"**: up to three newest published posts from the blog's own database, ONLY those
  tagged "trading", "Accessible Trade Terminal" or "Accessible Trader" (Cody: no unrelated posts). Today
  that is two posts (16 November update, 12 August). The Market Cipher deep dive (18 November) has no
  tags, so it is not shown. A post with no matching tag never appears; the section disappears, with its
  contents link, if none match or the blog database cannot be read.
- **"For vocational rehabilitation counselors"**: a short paragraph at the end of "Why financial
  literacy matters" pointing them to the free account and the support page.
- The section backgrounds alternate again after the move (the account section lost its grey; the
  leaderboard and the blog have it).
- Checked headless before installing and live after: desktop and phone, light and dark, no sideways
  scroll, no page errors, no broken images, headings in order, no repeated ids, Enter opens a question.

## Leaderboard account value, Market Cipher off the home page, dark-mode button edge, Cody's paper history cleaned, 2026-11-20 — not yet seen by Cody

- **Account value column** (Cody: "add the balance column to the table, yes"). The publisher writes
  `accountValue` per entry: the standing's own equity (cash, positions at their mark, collateral),
  rounded to cents, beside `returnPct`. The site shows "Account value (USD)" after Return, and until
  the terminal with this change is running it works the value out from the rounded return (close to
  the dollar). The footnote now says return and account value count open positions at their latest
  price. `LeaderboardPublisherTests` check it on three accounts (112,400, 105,000, and 100,000 for a
  position with no price, counted at what was paid); proven red by publishing equity minus realized
  profit, green again once restored.
- **Market Cipher off the home page** (Cody: cool, but not to be advertised as clones): the "New in
  2.13" line no longer says the Market Cipher indicators match the originals, and "Charting and
  analysis" lost its last sentence about the Market Cipher family. Nothing else on the home page names
  them; the other pages were not changed.
- **Dark mode main buttons** get a gold edge (`--brand-accent`) on every `btn--primary`, hover and
  focus included, so the navy button shows on the near-black page.
- Site backups on the server: `*.bak-20261120-balance` beside `includes/home.php`,
  `includes/leaderboard-section.php` and `style.css`. Checked headless before and live after: no
  "cipher" left on the page, the column filled, light and dark, desktop and phone, no sideways scroll.
- **Cody's hosted paper history cleaned** (Cody: the losing trades were a terminal bug, "they aren't
  real trades anyway"). On the VPS, `users/49698e84…/paper_account.json`: the fills grouped into round
  trips by the position returning to zero; the 10 trips that lost (11 losing closes, one trip closed in
  two halves: 21 fills) removed, the 4 winning trips (+10,995.44, +450.51, +268.35, +10.37; 9 fills)
  and the fill that opened today's short kept. Cash rebuilt as if the removed trips never happened:
  100,000 + kept realized 11,724.67 - kept fees 294.64 - the short's 75,993.89 from cash = 35,436.14
  (was 22,072.93; the same sum on the old file gives its cash to the cent, which is why the rebuild can
  be trusted). Open orders (the stop and take-profit), collateral, charts untouched. Done with the
  terminal stopped for a few seconds; backups `paper_account.json.bak-1120-before-clean` beside it and
  `~/paper_account.bak-1120-before-clean.json`. The leaderboard now shows 4 closed trades, 100% won,
  +11,724.67 realized, return about +28% (was 15 trades, 27% won, -968.06, about +15%). Cody expected
  "1 or 2" winners; there were 4. His desktop copy (`~/.local/share/AccessibleTrader/paper_account.json`)
  was NOT changed.

## Website home page: one main button, the why line, one stat, "Top traders" leaderboard, 2026-11-20 — not yet seen by Cody

Cody's answers to the advice on the site's call to action. On the server only (no git there); the
drafts are in `strategy-lab-data/site-drafts/home-1120b/` beside `.orig` copies of what was live.

- **One main button at the top.** "Start paper trading free — no download" goes straight to the
  terminal's sign-up page (`/terminal/account/register`, new tab, said in hidden text like the site's
  other terminal links). "Download the app" is the second button. "Try the live demo" is gone from the
  top; the demo is still in "On this page" and its own section. Once the header's script hears the
  browser is signed in, the main button becomes "Open the terminal" (the same `data-account-in/out`
  swap the account section uses). `.btn` sets `display`, which beat `hidden` and would have shown both
  buttons, so `.hero__cta [hidden]` hides them; checked headless, one button each way.
- **The why line** right under the heading, in bold: "Read the same charts sighted traders read, and
  trade from home on your own schedule. No interview, no commute, no employer deciding what you can do."
  The heading and the longer description under it are unchanged (both come from the site settings in
  the database, not the file).
- **One stat under the buttons**: "Screen-reader users pull information from online charts 61% less
  accurately than sighted users, and take 211% longer (ACM ASSETS 2021)", with a "Why it matters" link
  to the full block, which stays where it was at the bottom.
- **Leaderboard**: the heading is "Top traders: paper trading leaderboard" (Cody: add "top traders";
  kept "paper trading" so nobody reads it as real money, and the site's one-word "leaderboard" and
  sentence-case headings); "On this page" says "Top traders leaderboard". Signed-out readers get "Prove
  your skill before you risk a cent. Create a free account, practice with the same $100,000 as
  everyone else, and see where you rank." and a "Create your free account" button; the how-to-join
  sentence now starts "To show your own results" so it reads right signed in too. The empty-board text
  adds "and prove your skill before you risk a cent".
- Checked headless before installing: signed out and signed in, desktop and phone, light and dark; no
  sideways scroll at 390 px; the accessibility tree reads heading, why line, description, button,
  Download, stat, then the platform lines. Checked again on the live page after installing; `/`,
  `/download`, `/features` 200, PHP lint clean.
- Backups on the server: `includes/home.php.bak-20261120-cta`,
  `includes/leaderboard-section.php.bak-20261120-cta`, `style.css.bak-20261120-cta`.
- Caveats: nobody sighted has looked at it. Not changed, both older than this: the dark-mode main
  button is navy on near-black, and the leaderboard caption runs off a phone screen with its table
  (TODO). The accessibility-lead agent the hooks ask for is not installed, so there was no agent review.

## Website home page: new title, "What it does" in plain sentences, 2026-11-20 — not yet seen by Cody

Cody's notes on accessibletrader.com, installed on the server (`layout.php`, `includes/home.php`,
`style.css`; each backed up beside itself as `*.bak-20261120-whatitdoes`, drafts in
`strategy-lab-data/site-drafts/home-1120/`):

- The browser title reads "Accessible Trade Terminal — Making financial markets accessible for
  everyone" (was "the only fully accessible trading platform").
- "What it does" is eight cards of two or three plain sentences each, no bullets: Markets and
  analytics; Charting and analysis; Feedback and accessibility; Live and paper trading; Bloomberg
  Terminal style commands; News and fundamentals; Alerts and strategies (desktop notifications
  included); Customization and extensibility (plug-ins, C# and Pine Script indicators, the developer
  kit, sharing layouts, templates, sounds and strategies).
- "Why financial literacy matters" moved from the top to just before "Why Accessible Trade Terminal
  was needed"; the "On this page" list follows the new order.
- On a phone the cards were 368 pixels wide on a 390-pixel screen, leaving 6 pixels on the right
  against 16 on the left (the live page did this before today); they now shrink to fit, 16 each side.

Checked headless at 1280 and 390 pixels wide (no sideways scroll, headings in order) and live after
install (the title, all headings, and /features, /download, /quickstart, /developers still 200).

Caveats: the site does not mention headlines, because the terminal has no headline feed (the
calendar and the Alt+I dossier are what it has). Cody's note listed speech as a way to drive the
terminal; it speaks but does not listen, so the card says keyboard or touchscreen in. "Prediction
markets" was left out: Kalshi is in as perpetual futures, not event contracts. The audio tour is
unchanged, as asked.

### Built charts compute their indicators on their own bars; falling wicks are red, 2026-11-20 — found in headless screenshots, not yet seen or heard by Cody

Cody, 2026-11-20: "take headless screenshots yourself to make sure the new main pane chart types
are drawn and render correctly". A new browser test photographs all fifteen chart types on the
seeded market and fails if a type paints nothing or repeats another's picture byte for byte. All
fifteen draw their own shape. Looking at the pictures found two bugs:

- **On Renko, range bars, line break, Kagi and point and figure, every indicator and the volume
  pane were computed on the time bars** and then drawn one value per brick. A Kagi chart of five
  lines had two hundred volume bars under it and a Price line that wandered off the bricks.
  Choosing a built type recomputed nothing. The manual and the store already said "indicators read
  the bricks". The headless chart, background tabs and alerts did; the open chart did not, because
  the recompute read the feed's bars. Now, on a built chart, the store's drawn bars start the
  recompute and are what the indicators get. A new type, box or bar count recomputes every bar,
  and a change to the last bar alone takes the cheap update. A related race is fixed too: a
  viewport move straight after a type change cancelled the full recompute still waiting its turn,
  which left the volume pane blank. A full recompute that was asked for now stays owed until one
  finishes without being cancelled. **What you will hear differently:** arrowing an indicator or
  volume on a built chart now reads the value for that brick, where before it read the value for
  some time bar.
- **Falling candles' wicks were drawn in the rising colour.** Every wick took the theme's rising
  wick colour, and the falling one was never used. This goes back to before the new chart types
  (the September screenshots show it). Falling wicks now take the falling colour, mapped for the
  colour-vision palettes like the bodies. A wick colour you set yourself still colours every wick.

Tests: the photograph test (browser); a falling wick is the falling colour and a rising one the
rising colour; five on built charts (choosing one recomputes on the bricks, a feed tick alone
starts nothing, a new brick is full, a moved last brick is incremental, leaving recomputes on the
time bars, another built type with as many bars is full); two on the cancelled-full race. A
value-label test that compared pixels against a chart with no labels now compares against the same
indicator without labels, since the red wick under a label shape changed a blended pixel.
Sabotages each turned them red: one wick colour, the feed's bars, no feed guard, always full, no
subscription, the owed flag ignored, the owed flag cleared on cancel. **Caveats:** the seeded test
market gives Kagi five lines and point and figure seven columns, so in the pictures they sit at the
left of a 100-bar view; on a real market they fill it. The pictures were checked by me, not by a
sighted person.

### The order log is a tab in the trading dashboard, 2026-11-20 — not yet seen or heard by Cody

Cody, 2026-11-20: "add the order log to that new tab as a table in the trading dashboard". The
order log written since earlier today (`OrderAudit/orders-yyyy-MM.jsonl`) could only be read in a
text editor. The trading dashboard (Alt+T) now has an **Order log** tab between History and Book: a
table of the newest 200 entries, newest first, reading back across months, with columns Time,
What happened ("Sent", "Refused", "Sent, not confirmed", "Filled by stop", "Part filled", "Cancel
sent"…), Symbol, Exchange, Side, Type, Quantity, Price and Details (the refusal reason first, then
trigger, stop loss, take profit, leverage, reduce only, what is left, profit or loss, order
number). A filter narrows it by symbol or exchange as you type, and **Save as CSV** saves what is
shown (cells that would start a spreadsheet formula are made harmless). The table scrolls sideways
in its own labelled region. The file is read only when the tab is chosen or refreshed. A line that
does not parse — an append still being written — is skipped, and the file is opened so reading
never blocks an order being written. **On the public web demo the tab shows a sentence instead of
the table**, because every visitor there shares one paper account and one file. Tests: newest
first across months, the limit, a half-written line, no folder yet, a stop fill and a refusal in
words, the CSV quoting and formula guard, and the tab itself (rows, caption, filter, empty, demo);
sabotages (oldest month first, a bad line fatal, the demo shown, the filter ignored, the tab never
loading, the CSV guard off, the stop leg dropped) each turned them red. **Caveats:** only orders
from 2026-11-20 on are in the log; a fill the terminal never heard about (a venue's stream down,
or an order placed on the venue's own website) is not in it — History, which asks the venue,
still has those.

CI caught one thing the first push missed: **Save as CSV** used a plain `disabled`, which drops the
button out of the tab order with nothing said (DisabledButtonScanTests, red on b04cbb49). It is now a
GatedButton: still reachable, read as unavailable, with "No orders recorded yet." or "No orders match
the filter." read alongside.

### The website knows you are signed in, 2026-11-20 — not yet seen or heard by Cody

Cody, 2026-11-20: he signed in to the terminal, set a display name and joined the leaderboard,
and accessibletrader.com still offered "Create your free account" as if he were a stranger — "that
was confusing to me". The site is PHP and could not see the terminal's session. The terminal now
answers `GET /terminal/account/status` with whether this browser is signed in and its display
name (never the email; `no-store`; anonymous, so a stranger gets "signed out", not a login page;
the account is looked up, so a deleted one reads as signed out). When signed in, the site's menu
link "Sign in" reads "Open the terminal" and is followed by "My account" and "Sign out", and the
front page's account section becomes "Your paper-trading account" with the same three. The
site asks again whenever its tab comes back into view, so signing in or out in the terminal's tab
shows without a reload, and says "You are now signed in" or "signed out" once when it changes.
Tests: a stranger, signed in with and without a display name, the very next ask after signing
out, and a deleted account holding its old cookie; sabotages (trust the cookie without the
lookup, drop `no-store`, send the email, require sign-in) each turned them red. **Caveats:** the
site files are not in this repository (live on the server, drafts in strategy-lab-data);
"Sign out" opens the terminal's sign-out page in a new tab, which ends on the terminal's sign-in
page rather than back on the site; with JavaScript off the menu always says "Sign in". Deployed
and installed on the site 2026-11-20 13:47 UTC; the signed-out page was checked live in a headless
browser (no script or security-policy errors), the signed-in page only against a stand-in answer.

### Code review fixes, 2026-11-20 — not yet seen or heard by Cody

**The ATR trailing stop measures ATR the way the chart does.** Cody, 2026-11-20: "make them
consistent. I am not worried about changed replayed backtest right now." The trail that takes over
after the first target (`StopAdjust: TrailByAtr`) used a plain mean of the last N true ranges; the
chart's ATR pane and a strategy's ATR stop use Wilder's smoothing, so the trail sat a different
distance from the ATR the chart read out. It now uses the chart's own `IndicatorMath.Atr`, in the
backtester, in live trading and in the lab's setup grader alike, so the replay and the live trail
still agree with each other. **Caveat:** every replayed backtest with an ATR trail moves. Wilder
reacts more slowly to one wide bar and keeps more of its memory, so trails mostly sit at a
different distance than before; one hand-worked test moved from an exit at 108.5 to 113.25. Tests
pin the trail to the chart's ATR (with a check that the series really tells the two averages
apart), the replay and the live bar-by-bar trail agreeing at every bar, and no later bar moving an
earlier trail; sabotages (one bar stale live, one bar stale in the replay, the newest bar's ATR in
place of this bar's) each turned them red. Not seen in a live strategy.

**A dead stream is forgotten before it is announced.** CI caught it by timing on 2026-11-20: when
Kraken's order book, MEXC's order book or Tradier's account stream could not connect, the provider
said so first and forgot the dead stream second, so a listener that reopened the book the moment it
heard the failure got the dead one back and nothing tried again. All three now forget first. The
Kraken test reopens the book from inside the failure announcement, which fails every time on the
old order (sabotage: red); MEXC and Tradier have the same two-line swap and no test of their own.

**The server's clock monitor is registered by its type.** The push of 2026-11-20 failed CI on one
test: the two-heads parity scan reads `AddSingleton<Type>` and the hosted head registered the clock
monitor through a factory without naming the type, so the scan could not see it. Nothing was wrong
at run time; the type is named now and the scan passes.

Cody, 2026-11-20: "from the code review, start on each of the issues you found, in the best order or smallest to largest." The review is in `strategy-lab-data/reviews/att-review-2026-11-20/`. Smallest first:

- **Docs that said what the app does not do.** The root README called all 18 provider folders "trading venues"; 14 can place orders and Finnhub, FMP, Polygon and Twelve Data only serve prices, so it now says 14 trading venues and 4 market-data feeds. Quick Start said 0 on the price pane marks the price under the cursor; since 2026-10-09 it does nothing there and says why. SHORTCUTS and the technical README named a method that does not exist (`CommandDispatcher.HandleTrendlineCrossJump`; it is `IndicatorCrossingEngine.HandleCrossJump`). The analytics notes said the AI Analyst covers news lookups; it reads only the candles and indicators it is given.
- **The README guard now knows what a venue is.** `check_doc_drift.py` counts a provider folder as a trading venue only if a class in it implements `ITradingProvider`, and the README must give venues, market-data feeds and analytics feeds separately. Three sabotages, all red: the README back at 18, the sentence rephrased away, Kalshi's class no longer trading.
- **Order books and the Tradier order stream no longer die silently.** A first connect that fails throws once and never retries, and Kraken's order book, MEXC's order book and Tradier's account stream started theirs on a discarded task, so the throw went nowhere and the book simply never filled. The shared socket gains `ConnectOrReportAsync`; MEXC and Tradier use it, Kraken's subscribe has a catch. Each now reports on the provider's error stream (Tradier says order status is polled instead) and forgets the dead socket, so opening the book again makes a fresh try. Tests: `StreamConnectFailureTests` (3, against a loopback port that refuses). Three sabotages, all red: Kraken's report removed, Kraken's retry removed, the helper swallowing the failure.
- **The old in-process script runner is gone.** `ScriptingService` ran user C# with no sandbox at all, was registered in both hosts and used by nothing; the sandboxed `RoslynScriptingService` is what the custom scripts dialog uses. Its `ScriptResult` and `ScriptGlobals` types moved to `ScriptTypes.cs` because the sandboxed path uses them; the two tests that walled the old class off went with it.
- **Two comments fixed.** The Shift+H / Shift+M note pasted twice in `WorkspaceStore`'s action list is once now; the hosted head's My Data comment no longer says it is "NOT registered in demo mode" two lines above the code that registers it.

- **The journal's times follow your clock setting.** Entries were stamped with this computer's local time and printed as-is, so the F12 UTC and 12-hour choices never reached the journal (Ctrl+J). They are stamped in UTC now and shown through the same clock as the chart; the AI Analyst's "today's setups" uses the same day boundary. Tests: `JournalClockTests` (4). Three sabotages, all red: a speech entry stamped in local time, the zone conversion removed, the 12-hour clock ignored.
- **The "closed while you were away" record cannot drop a venue.** Each venue's save loads `position_snapshot.json`, sets its own entry and writes it back, and venues connect on their own threads; two saves that loaded together lost one venue's positions, so its overnight close was never reported. One lock around the load and write. Test: 24 venues saving at once all survive; with the lock removed it went red on both runs.
- **The live feed's reconnect count is atomic.** The tick callback reset it while the watchdog added to it on another thread; a lost reset left a healthy feed one silence closer to "stream lost". No new test: a race this narrow cannot be made to fail on demand, and the existing watchdog tests still pass.
- **A strategy's ATR stop uses the chart's ATR.** `RiskPlanResolver` sized an "ATR multiple" stop from a plain mean of the last N true ranges; the ATR pane on the chart is Wilder-smoothed, so the stop distance and the ATR you heard were two numbers for one trade. It now calls the same `IndicatorMath.Atr`. Test: `AtrStop_is_sized_from_Wilders_ATR_not_a_plain_mean` (a long from 100 stops at 96.75, the plain mean gave 99); with the plain mean put back it went red. The 427 strategy and backtest tests still pass.
- **Kalshi checks its key out at each signed request.** It parsed the private key once at Configure and kept it for the life of the app; Kraken, Coinbase, Alpaca, Binance, Bitstamp and MEXC ask the key store at every signed request. Kalshi now does the same: Key ID and private key come from the store, the key is parsed, signs once and is disposed. Configure still reads the key once, only to say early when it is unreadable. Test: `Each_signed_request_checks_the_key_out_of_the_store` (a key changed in the store signs the very next request). Two sabotages, both red: the store ignored, the first checked-out key kept.
- **Every server setting is in the deploy guide.** The hosted server read six settings that `SERVER_SETUP.md`'s environment table did not list (`Demo__Enabled`, `Launch__Disabled`, `PathBase`, `Accounts__SupportEmail`, the two security-event variables, the unverified-plugins switch); they have rows now. `ServerSetupDocumentsEverySettingTests` scans the WebHost for every setting it reads and fails when one has no row. Two sabotages, both red: a row deleted, a key renamed in `Program.cs`. The technical README's "18 trading" providers line now says 14 trading venues and 4 market-data feeds, like the root README.
- **The store's echo tests test something.** Four `WorkspaceStoreTests` dispatched an action and read back the field it set. One of them could never fail: it switched the mode to Trading, which is where the store starts. They now pin what the action does to the chart: a new symbol drops the old symbol's bars and cursor; a mode change leaves every other field as it was; Heikin-Ashi on a line chart brings candles back, and turning it off keeps them; a new tab is exactly one more tab, active and empty, with the old chart in its snapshot (it asserted "at least as many tabs"). A fifth test named for the log scale but testing component focus is renamed to say so. Four sabotages, all red: bars kept across a symbol change, a mode change moving the cursor, Heikin-Ashi left on a line chart, a tab saved twice.
- **The security switches are read in one place.** The desktop head, the hosted head and the hosted plugin bridge each parsed `ACCESSIBLETRADER_SECURITY_EVENT_PERSIST`, `…_DIR` and `ACCESSIBLETRADER_ALLOW_UNVERIFIED_PLUGINS` by their own copy of the same lines; they call `EnvironmentSwitches` in Core now, so one head cannot come to read "off" differently from another. No behaviour changed. Tests: `EnvironmentSwitchesTests` (the accepted spellings, and no head parsing them itself). Two sabotages, both red: "yes" accepted for unverified plugins, the shared file left out of the deploy-guide scan.
- **"Is this a daily chart" is asked in one place.** The crosshair label, a drawing's range end, the tactile strip, nudge speech and two arrow-key paths each wrote `barSeconds >= 86400` out themselves; they ask `SpeechTimeFormatter.IsDaily` now, and the drawing range end and the tactile strip share one short-stamp format instead of two copies of the same pair. Every one of them already turned the stamp into a time through `SpeechTimeFormatter`; their formats differ on purpose (a 20-cell braille strip is not a crosshair label) and stay. No reading changed. Tests: `DailyBarThresholdLivesInOnePlaceTests`, including a scan that fails on a seventh inline copy. Two sabotages, both red: an inline copy put back, the line moved off by one second.
- **Checked and left: the two "decimals" helpers.** The review called `AlertDescriptions.Decimals` and `KalshiExpectedFigures.Decimals` one job with two formulas. They are two jobs: the first gives a price about five significant figures for an input box (63,000.12; a sub-cent coin keeps its digits), the second gives the decimals of a Kalshi strike step (payrolls in whole thousands, CPI in tenths). Merging them would change one of the two. Nothing changed.
- **The sector warning and the COT feed read exchange symbols.** Both cut a symbol's asset off with their own `Split('/', '-', ':')[0]`, so a symbol with no slash was not recognised: "BTCUSDT" and "SOLUSDT" were sector "other", and holding SOLUSDT then buying ETH/USDT gave no same-sector note on the order; a BTCUSDT, XAUUSD or EURUSD chart had no COT report. Shown before the fix with a probe. Both now ask `SymbolAssets.BaseOf`, the splitter the quantity wording already uses (it also drops an exchange prefix, "COINBASE:ETHUSD"). A bare ticker of five letters or fewer is still a stock, read off the whole symbol, so "SETH" does not become "S" priced in ETH. Tests: new rows in `SectorClassifierTests` and `CotPositioningIndicatorTests`. Three sabotages, all red: the old split back in each, the stock test reading the cut base. Left as they are, on purpose: Bitstamp's pair rules (they read the venue's own "BTC/USD" names), the CoinGecko profile lookup and Cipher C's preset lookup (both already strip unseparated quotes, with their own alias tables). Noticed, not changed: the shared splitter would cut "XBTUSD" as "XB" priced in "TUSD"; Kraken renames XBT to BTC before any symbol reaches the app, so nothing hits it today.
- **Checked and left: the bar-by-date lookups.** The review listed five "linear scans" and one binary search. The drawing lookup (`FindNearestBarIndex`) is a binary search already. The others are not one job: the bar-close announcement and the candle-pattern reader look at the newest few bars for the bar that just closed and fall back to the last one; the headless chart's merge walks back only as far as the fetch was wide; Cipher C's lookups count on past the last bar on the chart's own clock. Each falls back differently on purpose, and no case was found where one names the wrong bar. Nothing changed.
- **The paper account, saved workspaces and alerts keep a copy a day.** Writes were already crash-safe (temp file, then a rename), but a save that was wrong (a bug that empties the positions, a bad restore) replaced the only copy. On the first save of each UTC day the file as it stood is copied into a `Backups` folder beside it (`Backups/paper_account.2026-11-20.json`, `Workspaces/Backups/Main.2026-11-20.json`), and the newest seven are kept. One a day rather than one a save, because the account is written on every fill and seven per-save copies would all be from the last minute. A copy that cannot be written never stops the save. The session autosave slots are not copied (machinery, rewritten every few seconds per tab); the reconciliation snapshot is not either (rebuilt from the venue). On hosted the copies sit in each user's folder and go out with **Download my data**; the factory reset does not touch them. USER_MANUAL ("Daily copies") and SERVER_SETUP's data layout say where they are. Tests: `DailyBackupTests`, plus a paper-account case in `PaperTradingProviderTests`. Five sabotages, all red: a copy on every save, never pruning, the paper account not copied, autosave slots copied, a failed copy stopping the save. Caveat: nothing in the app restores a copy; going back a day is copying the file by hand.
- **Every order is written down.** The spoken announcements and the venue's own history were the only record of what the terminal sent, so "did I really send that?" had no answer of its own once speech moved on. Now every order the terminal is asked to send (dashboard ticket, quick trade, close position, protective-order edits, strategies: they all go through `GeneralOrderService`) is one JSON line in `OrderAudit/orders-2026-11.jsonl` in the data folder: time (UTC), sent / refused / failed / uncertain, venue, symbol, side, type, quantity, prices, stop, take profit, leverage, the venue's order id, and the refusal in the words you heard. Cancels (taken or not), one-cancels-other pairs, and every fill or cancel a venue or the paper broker reports back (which leg fired, fill price, realised P&L) get a line too. Never a key or a password. A new file each month; a month stops growing at 20 MB, with one warning in the log, so the public demo cannot fill a disk. A line that cannot be written never stops the order. Hosted: per account, and it goes out with **Download my data**. Tests: `OrderAuditLogTests` (7). Seven sabotages, all red: no line for a placement, for a report back, for a cancel; the cap off by one; a write error thrown into the order; the hosted head not registering it; one lock per log instead of one per process (on the demo every visitor's log writes the same file, and 800 lines from 8 logs at once lost some). Caveat: nothing in the app reads it back yet; it is a file to open in a text editor or hand to a script.
- **A wrong computer clock is said.** Venues sign requests with a timestamp and refuse one too far from their own time (Binance re-syncs itself; Coinbase, Kalshi and the rest just refuse, in words that do not say "clock"), and a wrong clock also misplaces the newest bar. Every venue's answer carries its `Date` header, so the plugin web clients on both heads now pass it to `ClockSkewMonitor`, which compares it with the middle of the round trip. Three answers in a row more than 15 seconds off the same way are said once a run: "This computer's clock is about 60 seconds fast next to Kraken's. Venues refuse signed orders when the clock is off, and the newest bar's time will be wrong. Set the clock to sync automatically." Cached answers (an `Age` header) are not counted; the figure said is the smallest of the three. On the hosted server the clock is the server's, so it is logged, not spoken to visitors. Tests: `ClockSkewMonitorTests`. Four sabotages, all red: one answer enough, cached answers counted, directions mixed, said every time; and a fifth with the desktop factory not reading the clock.
- **`/healthz/deep` for the server's operator.** Plain text, a fact a line: version, uptime, memory, the clock against the venues', free disk under the data root (LOW under 10%), when the leaderboard was last written (STALE after 15 minutes). It answers only a request made on the server itself (loopback and no forwarded-for header); through nginx it is a 404. `/healthz` is unchanged. Tried on a local build: the report on a direct call, 404 with nginx's header, `/healthz` still "ok". Sabotage: the forwarded-for check removed, red. SERVER_SETUP shows it under the deploy checks. Not in it: per-venue connection state (each user's venue connections live in their own session, and the hosted terminal is paper-only) and the error-rate digest the review also suggested.
- **A slow listener no longer freezes the practice account.** The paper broker sent each order event (filled, cancelled, stop moved) to its listeners while holding the account's lock, and the listeners are speech, the journal, the reconciliation record and, since today, the order log's file write. While one of them was busy, every other question to the account waited: positions, balances, another tab's bar. Shown first: a test whose listener holds a fill made `GetPositionsAsync` wait past two seconds. Events are now queued under the lock and sent once it is released, in the order the account changed, by one sender at a time, so two threads cannot reorder them. A listener that places an order of its own still hears it. Proven by four sabotages: sending inside the lock again (the new test went red), and taking the send out of placing, cancelling or a new bar (2, 1 and 11 paper tests went red). A side effect: a listener now sees the account as it settled, with a filled stop's other leg already cancelled and the file saved. Before, it heard the fill first.
- **Old docs moved to `docs/archive/`.** Cody, 2026-11-20: "Clean up old docs, studies, etc and put them in the docs/archive directory." 106 files went: every study's findings and pre-registration, the research notes and ledgers, the September audits, report cards and reviews (and the two folders of accessibility reviews), the old plans and scopes, and the 2.1 to 2.4 release checklists. `docs/` keeps 30: the user's documents (manual, shortcuts, quick start, what's new), TODO and CHANGES, the server and release guides, the authoring guides, the designs of features that are live (Cipher C, Cycle Position, market events, chart patterns, keyed feeds, the sandbox, braille), the alpha ledger and the lab's design and data notes. Every link and repo path that named a moved file (in docs, code comments, tests, the lab's edge registry, the skills) was rewritten by a script that resolved each link from the file's old place; a link check found the same two broken links before and after, both older (`docs/braille/` naming a firmware email and folder that were never committed). `docs/archive/README.md` lists what is there and says the live records are one folder up. The test that every findings document has an edge record looked only in `docs/`, so after the move it would have checked nothing; it now looks in both and fails if it finds fewer than 20 (proven: pointed back at `docs/` alone, it went red). Left alone: `reports/` (the lab writes its output there) and the tracked `scratchpad/` harness output.

Caveats: strategies with an ATR-multiple stop will place it at a slightly different distance than before, so replayed results for them can move a little. The ATR trailing stop (`ManagedExitRules.AtrTrailStop`) is still a plain mean on purpose, because the backtester and live trading share it; changing it is a question for Cody (TODO). MEXC's and Tradier's addresses are fixed in code, so their call sites are covered through the shared helper's test, not a provider-level test; Kraken's is tested end to end. The journal test of "UTC off" can only tell the clocks apart on a computer not set to UTC: it does here (Central time), not on CI.

### Cody's practice account copied to the hosted server, 2026-11-20 — not yet seen by Cody

Cody, 2026-11-20: "Yes, copy my trade data including the open short from my open trade to the leader board."

- **His local `paper_account.json` replaced the hosted one** (`/var/lib/accessible-trader-terminal/users/49698e84…/`), with no "imported" label: cash 22,072.93, short 1 BTC/USDT from 75,993.89, 31 fills. The old hosted file (cash 95,019.33, flat, 7 fills) is backed up as `/home/debian/paper_account.vps-bak-1120-120229.json`. Owner and mode kept (debian, 0600); the checksum matches the local copy.
- **The hosted terminal was restarted** at 12:18 UTC so no account loaded before the copy could be written back over it. Active, no restarts, no error lines, login page 200, the file unchanged after.

Caveats: he is not on the leaderboard yet. His account has no display name and the opt-in is off, and the publisher lists only accounts with both; the name is public, so it is his to choose on the Account page, not written into the login database by hand. Once he is listed, the website still shows the "be one of the first" invitation until 3 people are on the board.

### Deployed to the hosted server with email and the leaderboard switched on, 2026-11-20 — not yet seen by Cody

Server and website work only; no code changed in this entry.

- **`171f3d18` deployed to both VPS services** at 11:14 UTC after CI was green on all three workflows. Gates held: the capability lines matched before and after, no restarts, no error lines, and the login page and demo answered. The `AccountProfileFields` migration (display name, leaderboard opt-in) ran on start.
- **Email through Brevo is on.** `AccountEmail__User` joined `AccountEmail__Password` in the server's environment file (backed up first). One real password-reset email to Cody's own address was sent; the app logged it as sent at 11:15 UTC.
- **The leaderboard is on.** `Leaderboard__OutputPath` points at `/var/lib/accessible-trader-public/leaderboard.json` (folder 0750, readable by the web server's group). The first file was written 30 seconds after start, with no rows: no one has opted in yet.
- **The website section is live** on accessibletrader.com, after the free-account section, with a link in the contents list. Under three entries it shows the "be one of the first" invitation.

Cody confirmed the test email the same day: it arrived, with no Brevo branding he could tell. The table with real rows has not been seen on the live page, only rendered from sample data. Cody's practice P&L is not copied to the server yet; that waits for his yes.

### Leaderboard shows open positions; two browser tests caught up with this batch, 2026-11-20 — not yet seen by Cody

Cody, 2026-11-20: "if the user has an open position, make that visible in the leaderboard as well be it closed or open position."

- **Each row lists the positions held now.** `PaperAccountStanding` gains `OpenPositions`: per held position its symbol, long or short, the amount, the entry price, the price it was valued at and the profit so far (money and percent of entry, before fees; a winning short is positive). With no price fetched the price and profit are null, "unknown", never zero, and the row is still flagged estimated as before. Zero-quantity rows are not positions. The published JSON carries them as `openPositions` (an empty list, not a missing field, when nothing is held).
- **Holding a position is enough to be listed.** An opted-in account with no closed trade yet but an open position is now on the board (return counts the position at its price; trades 0, win rate none). An account that never traded is still left off.
- **The website section** (`strategy-lab-data/site-drafts/leaderboard-section.php`, installed on the VPS by hand) gets an "Open positions" column: "Long 0.5 BTC/USDT from 60,000.00, now 62,000.00: +3.33% (+1,000.00 USD)", "price unavailable" when there is none, "none" when flat; a footnote says return counts open positions and the other columns count closed trades only. The account page hint and the user manual say open positions are shown.
- **Browser harness, red on CI for d86f0851, fixed.** `RouteCatalogCompletenessTests` found five dialogs from this batch with no route: the Go bar (Alt+G, lands on its text box), the market event calendar (Alt+E and its toolbar button), Save and Apply indicator template (toolbar) are now routed, so every focus, Tab-trap, Escape and accessible-name theory runs on them; Brick settings is recorded as needing a built chart type. `CodysSavedSession_ResumesWithEveryIndicatorOnTheChart` expected the price series to be called "Candles", but that slot's chart is Heikin Ashi and the series is named after the chart type since 2026-11-19; it now expects "Heikin Ashi".

Tests: `PaperAccountStandingTests` +2, `LeaderboardPublisherTests` +2. Four sabotages, all red: listing only accounts with a closed trade, a short's percent without its sign, an unknown profit published as zero, every side "long". The browser harness ran in full locally (Chromium 1248, muted): 274 passed, 1 skipped (the opt-in screen-reader probe), up from 246 because of the new routes.

Caveats: a public row now shows what an account holds and its size; that is what was asked, and only opted-in accounts appear. The website column was rendered from sample JSON with the PHP CLI, not yet on the live site. The accessibility-lead agent the project hook asks for is not registered, so no agent review was done.

### The baseline's base kept on screen, and the mouse pans tested, 2026-11-20 — not yet seen by Cody

Cody, after re-hearing the three fixes below: "fix the small issues regarding those bars being off screen, mouse wheel no tests add those".

- **A chosen baseline base is fitted into the price axis.** `ViewportRangeCalculator` fitted a Baseline chart to its closes only, so a base typed far from price (`BASELINE 95000` on a chart trading at 90,000) was drawn off the pane, while the sound and the words still compared every close with it. The base now widens the axis like a reference level does. Left at the first close in view (the default), the base is itself a visible close and changes nothing. The cost, as with any wide overlay: a base far from price squashes the line, and the pitch range with it, so eye and ear agree.
- **The drag and wheel pans on a built chart are tested.** The one-line change in the entry below (history asked for by real bars, not drawn bars) was untested on the mouse-drag pan (`DrawingInteractionManager`) and the Shift+wheel pan (`GlobalInputService.OnWheelPan`). Both now have the keyboard tests' Renko chart: a view starting about 120 real bars in asks for nothing, one in the first real bars asks for older history.

Tests: `MoreChartTypesTests` +2 (a base under and over every close is inside the axis range; the default base leaves the range equal to the line chart's), `ChartMouseInteractionTests` +2 theories (drag and wheel, each far and near). Four sabotages, all red: the base not fitted (2), the wheel pan by drawn bars (1), the drag by drawn bars (1), the drag never asking (1).

Caveat: the base is fitted without the span guard the reference levels use, deliberately (the user chose it), so a mistyped base (9,500 for 95,000) squashes the chart until it is changed. Not yet seen by Cody.

### Cody's three findings on the new chart types, 2026-11-20 — fixed, re-heard and approved by Cody

Cody tried the six new types ("everything sounds good") and found three faults:

- **"The latest rightmost high low bar doesn't make a sound."** A live tick writes each price part's newest value from its own list of bar fields (`ViewportReducer`), and that list had no `hl2`, the middle of the high and the low that a high-low bar is pitched at. So the bar being formed was NaN on every tick, and NaN is silence. A reload filled it, which is why only the newest bar was silent. The tick now uses `IndicatorStateMapper.MapBar`, the mapper the full recalculation uses. Mouse and touch exploration (`TactileCanvasCoordinator`) had the same short list and read the close there instead; it uses the same mapper now.
- **"I'll hear the sound of the body of the bar and focus will jump to the body."** `PriceSeriesShape.Apply` put focus back on the body whenever it replaced the price series. Hollow and volume candles carry hidden values (each bar's colour, each bar's volume) that are refreshed on every tick, so every tick replaced the series and threw a wick-focused user onto the body. Focus now goes back to the price part only when the shape itself changes (a new chart type).
- **"Point and figure ... loaded all the chart history on the daily chart ... and yet I'm only viewing 58 bars."** Walking left, panning, dragging or wheeling asked for older history whenever the cursor or view was within 50 bars of the start. On a built chart those were 50 DRAWN bars, and 58 point and figure columns on a daily chart are years of days, so nearly every Left arrow fetched another 200 days until the whole history (or the 5,000-bar limit) was in. "Near the start" is now measured in real bars (`WorkspaceState.RealBarIndex`: a drawn bar's first real bar on Renko, range bars, line break, Kagi and point and figure; the index itself on every other chart). Note that a built chart is always built from every bar loaded, so 58 columns covering the loaded years is how point and figure looks on TradingView too; the fault was only the loading.

Tests: `MoreChartTypesTests` +5 (the newest high-low middle through an in-place tick and a new bar, with a pitch; focus staying on the wick through ticks on hollow and volume candles, with a check that the tick did reach the hidden values; choosing another type still moving focus) and `ChartKeyboardNavigationTests` +4 (Left and keyboard pan on a Renko chart with a brick every ten real bars ask for history only near the first real bars). Six sabotages, all red: the tick without `hl2`, focus reset on every tick, the walk measured in drawn bars, `RealBarIndex` returning the drawn index, `RealBarIndex` never near, and the keyboard pan measured in drawn bars.

Caveats: the mouse/touch `hl2` fix is not tested. The drag and wheel pans got their tests in the entry above. Cody re-heard the three fixes the same day: "I heard the fixes, approved."

### Six more chart types: bars, hollow candles, baseline, high-low, HLC area, volume candles, 2026-11-20 — not yet heard by Cody

Cody, 2026-11-20, "1. Yes" to building six of TradingView's time-bar chart types (step line,
line with markers and columns skipped), under his rule that *"a totally blind keyboard/speech
user can get the same information that a sighted person can"*. Each is drawn, heard, said, put
on the status line and chosen alike.

- **The types.** `PriceChartStyle.Bars`, `HollowCandles`, `Baseline`, `HighLow`, `HlcArea`,
  `VolumeCandles`. `PriceSeriesShape` gives each its parts: Bars one **Bar** part (`ohlc`), High-low
  one **High-low bar** (`highlow`, mapped to hl2 so the pitch is the bar's middle), Baseline one
  **Close** (`baseline`), HLC area **High** / **Close** / **Low** (focus on Close); hollow and
  volume candles keep the three candle parts with a hidden value (`__hollow_green`, `__volume`;
  high-low adds `__hl_range`, baseline `__baseline`) that only that type carries and that goes
  when the type changes.
- **Drawn.** `StandardRenderers`: `RenderOhlcBars` (a high-low stroke, the open ticked left, the
  close right), `RenderHighLow` (a solid block low to high), `RenderBaseline` (the close green above
  the base and red below, each side shaded to the base, a segment split where it crosses, the base
  dashed), `RenderHlcArea` (high and low lines, high-to-close shaded green, close-to-low red, the
  close line on top), and `RenderCandles` for hollow (coloured by the close against the previous
  close, hollow when the close is at or over its own open, as TradingView) and volume candles (as
  wide as the volume against the heaviest bar in view, never under a sixth).
- **The base** (`PriceSeriesShape.BaselineBase`) is a chosen price or, by default, the close of the
  first bar in view, and it is the one rule the drawing, the sound, the words and the status line
  read. Stored as `RenkoSettings.BaselinePrice` (0 = first close in view).
- **Heard.** A bar is a thin stroke and sounds thinner than a body (half the square edge),
  direction coloured as a candle is; a high-low bar's sub-octave weight is its range against the
  tallest bar in view; the baseline has a bright tone above the base and a warm one below, and
  **clicks where it crosses** the base, up or down (`CrossDirection`); a hollow candle's tone says
  its colour (against the previous close) while the pitch still says its body; a volume candle's
  weight is its volume against the heaviest bar in view. HLC area's three lines sound as lines.
- **Said.** Arrowing: *"Bar. Up. Open 101.00, high 106.00, low 100.00, close 103.00."* then the
  move; *"High-low bar. High 112.00, low 98.00, range 14.00."*; on Baseline *"Close. baseline.
  105.50, 1.44 percent above the base, 104.00."* (or *"below the base"*, *"at the base"*); hollow
  candles lead with *"Hollow green."*, *"Filled green."*, *"Filled red."* …; volume candles add
  *"Heavy volume, 4.0 times the 9-bar average."* (at least 1.5 times) or *"Light volume, …"* (at
  most 0.5), measured over up to 20 bars before, needing 5. Changing type says *"Bar chart."*,
  *"Hollow candle chart. Colour against the previous close, hollow when the close is above the
  open."*, *"Baseline chart, base at the first close in view."* (or *"…, base 104.00."*),
  *"High-low chart."*, *"HLC area chart."*, *"Volume candle chart. Wider candles for more
  volume."*; a new base on a baseline chart is said the same way.
- **Status line.** Bars and hollow candles show O H L C (hollow coloured by the previous close);
  Baseline `C` in its side's colour and `Base`; High-low `H L Range`; HLC area `H L C`; volume
  candles O H L C and `Vol`.
- **Chosen.** Toolbar Chart type gains Bars, Hollow candles, Volume candles, Baseline, High-low,
  HLC area. Go bar: `BARS` (`bar chart`, `ohlc bars`, `western bars`), `HOLLOW`, `BASELINE`
  (`BASELINE 95000` sets the base; a non-price is refused *"A base must be a price above zero,
  like BASELINE 95000."*), `HILO` (`high-low`), `HLC` (`hlc area`), `VCANDLE` (`volume candles`).
  A type already on screen says *"Already a bar chart."*, *"Already an HLC area chart."*, *"Already
  a baseline chart, base 104.00."*; `BASELINE 105` on a baseline chart only moves the base.
  New `SystemCommand`s are chart-scoped like the others.
- **Tests.** `MoreChartTypesTests` (new, 48: shape, hidden values, base, six sounds, speech by
  part and by series, change sentences, coordinator, six drawings, status line, commands, Go
  bar), `ChartTypeToolbarTests` +6, `Phase5KeyboardScopeTests` categorised. Sabotaged 31 ways,
  all red — one only after a fix: the series-level reading's "Heavy volume" had no test (the
  part-level one did), so a series test was added; one test had been comparing the spoken
  baseline percent to an invented number and now checks the real one (vacuity-checked).
- **Caveats.** The base is not added to the price axis range, so a chosen base far from the price
  is off the pane (the shading still reaches the edge). Volume words compare with the bars before,
  not the view. HLC area has no sound of its own for the bands: its three lines are heard. Not
  seen, not heard.

### Point and figure: high and low, a percentage box, buy and sell signals, 2026-11-20 — not yet heard by Cody

Cody, 2026-11-20, on the three P&F questions: *"1. Yes, I'd like this option."* (High/Low),
*"don't alter the chart pattern code, simply just say buy or sell as you suggested"* (signals),
and a percentage box *"if this is something that is offered by tv"* (it is: "Percentage (LTP)").

- **High and low as the source.** `PointFigureBuilder.Build(..., highLow: true)`, TradingView's
  "High/Low": continuation first, so an X column is extended by the bar's high and, only when the
  high does not extend it, turned by its low (an O column the other way round). Before the first
  column, the side that reaches further past the first close's level draws it; a tie goes the way
  the bar closed. The first bar's close still seeds the grid. `RenkoSettings.PnfHighLow` (false
  by default; an old file reads Close); `BuiltCharts.SameFor`/`Merge` carry it. The settings
  dialog gains a **Source** group (**Close** / **High and low**); the Go bar takes a trailing
  `HL` (`PNF 500 3 HL`), named *"…, from highs and lows"*. The change sentence ends *", from
  highs and lows."*
- **A percentage box** ("Percentage (LTP)"). `RenkoBoxMode.Percent` on P&F: `PnfSize` percent of
  the last close, `PointFigureBuilder.PercentBox`, frozen as a price when chosen, as an ATR box
  is. Above 0 and at most 100, else *"A point and figure box percentage must be above 0 and at
  most 100."*; no bars, *"Point and figure needs a last price for a percentage box."* The dialog
  offers the same **Percentage** option Kagi has, labelled *"Percentage of the last price"*,
  field *"Box percentage:"*, with a live *"Resolved box: …"* preview. Go bar `PNF 1%`,
  `PNF 1 %`, `PNF 1 percent 3`. Change sentence *"Point and figure chart, box 1,050.00, 1
  percent of the last price, reversal 3 boxes."*
- **Buy and sell signals, spoken.** `PointFigureColumns.SignalLevel`/`SignalWords`: an X column
  whose top is above the top of the X column before it (two columns back) is *"Buy signal, above
  the last X top at 105.00"*; an O column under the last O bottom, *"Sell signal, below the last
  O bottom at 95.00"*. Said when arrowing (*"Column. Up, X, 5 boxes, buy signal, above the last X
  top at 103.00. 100.00 to 105.00."*), in the bar detail summary, on the status line (`Up column,
  X, 5 boxes, buy signal`), live once per column (with the new column, or with the box that broke
  the level; a waver back and forth does not say it again), and by a background tab or the
  browser-closed monitor at the close that gave it (`BrickNews.PnfSignalAtClose`, once: a column
  that had already broken the level a bar earlier says boxes only). `ChartPatternDetector` and
  its formations are untouched, as Cody asked; the formations' "Double top" (a top that held) is
  a different reading from this breakout, which is why the words are "Buy signal" and not
  "double top". No sound of its own: the words carry it, and TradingView draws no mark for it.
- **Tests.** `PointFigureChartTests` +32 (high/low draws what closes do not, continuation before
  a turn, first-column further/tie four ways, BuiltCharts uses it only when asked; percentage
  resolve, two refusals, no last price; change sentences; Go bar 7 forms, 6 refusals, 2 names;
  signal levels and words; arrowing a sell; status line; live once through a waver; background
  once), five existing expectations now carry the buy signal; `RenkoToolbarTests` +3 (percentage
  preview and Apply, over 100 refused, Source radios and prefill), and P&F now offers Percentage.
  Sabotaged 18 ways, all red — two only after a fix: an X column carried on by its high while its low reached a reversal had no test (added), and a "same direction" check in `SignalLevel` could never fire (columns alternate), so it was removed.
- **Caveats.** The percentage box is frozen at the last price when chosen; TradingView's
  "Percentage (LTP)" follows the last price, so its grid moves as price does. No min-tick
  rounding of the box. High/low takes a forming bar's high and low as they are, so a live column
  can grow on a wick and the next tick cannot take it back (as on TradingView). Not seen, not
  heard.

### Point and figure chart type, 2026-11-20 — not yet heard by Cody

Cody, 2026-11-19: *"then proceed with the next chart types"* — point and figure, after Kagi.

- **Builder.** `Charts/PointFigureBuilder`: closes only, on a grid of whole boxes from zero
  (`UpLevel`/`DownLevel`, with a 1e-9 hair so 300 at box 0.1 is on its level). The first column
  is drawn when a close reaches one box past the first close's own level, either way. A close
  reaching a level past the column's end extends it; a close the **reversal** count of boxes back
  (1 to 20, three by default, TradingView's) starts a column the other way, one box in from the
  end. Each drawn bar is one column: Open is the level it moved FROM (the column before's end, or
  the first close's level), Close its newest box, so `PointFigureColumns.Boxes` = |Close − Open| /
  box and a one-box column keeps its direction. Date is the bar whose close started it; the span
  and volume are the window since the column before's newest box, like Kagi. The forming bar is
  fed in, so the newest column grows with the live close. `PriceChartStyle.PointAndFigure = 7`
  on the end; brick and block style. `RenkoSettings` gains `PnfMode`, `PnfSize`, `PnfAtrLength`
  (ATR 14), `PnfReversal` (3); an old file reads the defaults. `BuiltCharts` covers it; a
  reversal outside 1–20 is refused before the box is resolved.
- **Drawn.** `StandardRenderers.RenderPointFigure`: per column one mark per box at the bar's
  centre, X (two diagonals) going up, O (an oval) going down, in the up/down body colours (or the
  user's), colour-vision safe. Where a box is under 4 px × density on screen the column is drawn
  as a solid bar, which is what the marks would blur into (the first cut drew only its outline;
  a test caught it).
- **Read and heard.** The price series is named *Point and figure*, one part **Column**: *"Column.
  Up, X, 4 boxes. 100.00 to 104.00. window."* The box counts travel in the series' buffer
  (`__pnf_boxes`, one per column, weak-cached per column list and box, as Kagi's thickness), so
  the template, sound and status line (`Up column, X, 4 boxes`) read the same number the drawing
  draws. Sound: the brick ping (direction = pitch) plus sub-octave saw weight rising with the
  boxes, none at one box, 0.3 at ten or more. Change sentence *"Point and figure chart, fixed box
  500.00, reversal 3 boxes."* / *"… box 512.30 from ATR 14, …"*.
- **Live.** A new column says *"New up column, X, 3 boxes, 100.00 to 103.00."*; the growing
  column says each new box once, *"X column, 6 boxes, 106.00."*, on the narration channel. A box
  the forming bar takes back and gives again is not said twice (a high-water mark per column).
- **Background and alerts.** A background P&F tab says a column that turned **or grew** on the
  bar that closed: *"BTC/USD 1h: up column, X, 6 boxes, 100.00 to 106.00."*, titled *"… column
  moved"* (not "new": the column may be an old one growing). New up / New down / Reversal alerts
  fire on a new column, not on growth (the Kagi same-bar rule).
- **Chosen from.** Toolbar Chart type list (*Point and figure*), **Point and figure settings** in
  the shared dialog (Box: ATR or fixed, plus **Reversal**, 1 to 20 boxes), Go bar `PNF`, `PF`,
  `p&f`, `point and figure`; `PNF 500`, `PNF 500 2`, `PNF ATR 20`, `PNF ATR 20 2` ("ATR 3" is ATR
  length 3; the reversal follows a whole size). `SystemCommand.ShowPointFigureChart`. The brick
  settings refusal on a time chart now names all five types.
- **Tests.** `PointFigureChartTests` 42 (builder: grow/turn, dates/spans/volume, reversal 1 and 4,
  growth keeps date and open, first column down and one box, the hair, refusals; BuiltCharts
  fields; store freezes its own box; series part and box buffer; arrowing, summary, change
  sentence, status line; new column and growth said once through a waver; growth-news nulls;
  background turn/grow/inside; alerts; Go bar parse/refuse/words/action/already; dispatcher;
  X vs O drawn; solid bar under 4 px; taller sounds heavier); `RenkoToolbarTests` +3 (select,
  reversal field, Apply, refused at the field); `RenkoSpeechTests` sentence updated. Sabotaged 22
  ways, all red — four only after tightening (growth news for "another column" was already
  rejected by the box check; nothing exercised a waver right after a new column; the tiny-box
  test could not tell marks from a bar; a sed that made a tuple did not build).
- **Caveats and questions for Cody.** Closes only: TradingView also offers High/Low as the
  source, not built. No double-top / double-bottom breakout words (the classic P&F signals). No
  percentage box (Kagi has one). (All three answered and built the same day: see the entry
  above.) The renderer dispatch from `DataLayer` has no test of its own
  (as for Kagi). Not seen, not heard.

### Kagi thickness heard, and a percentage reversal, 2026-11-20 — not yet heard by Cody

Cody, 2026-11-19: *"1. thickness audible, yes if possible. 2. yes percentage adjustment too."*

- **Thick and thin heard.** The sound path sees one value per bar, not the lines before it, so
  the thickness travels in the price series' buffer: `PriceSeriesShape` puts each line's
  thickness at its end (1 thick, 0 thin) under the hidden key `__kagi_thick`, after every reduce
  on a Kagi chart. It is worked out once per drawn-bar list (a weak cache on the list), so an
  arrow key costs a lookup and the series is replaced only when the lines change; the live
  growing line gets its new thickness on the tick that turns it. `DefaultSonificationStrategy`
  reads it for the Kagi part: thick = square 0.18 plus sub-octave saw 0.25 (heavy), thin =
  triangle 0.06 (nearly a pure sine). Direction stays the pitch, so a thick line up and a thick
  line down share a timbre and differ in pitch. A turned line sounds as it ends; the turn itself
  is still said in words. Other built types are unchanged.
- **Percentage reversal.** `RenkoBoxMode.Percent = 2` (appended; Kagi's alone: Renko and range
  bars refuse it in words). `KagiBuilder.Build(..., percent: true)` turns a line when a close
  comes back that percentage of the line's end (before the first line, of the first close), so
  the same move turns a line sooner at a low price than at a high one. The percentage is frozen
  as typed (above 0, at most 100), not resolved. Dialog: a third option, **Percentage**, on Kagi
  only, with its own field (a percentage is never prefilled as a price, nor a price as a
  percentage). Go bar: `KAGI 2%`, `KAGI 2 %`, `KAGI 0.5 percent`. Said *"Kagi chart, reversal 2
  percent."*; a percentage too big for the loaded moves says *"No Kagi lines: price has not moved
  50 percent in the loaded bars."*
- **Tests.** `KagiChartTests` +16 (buffer thickness follows the lines and is left off other
  charts; same-direction thick vs thin differ only in timbre; percent builder vs fixed turn on
  different bars; freeze and refusals; Go bar parse, refuse and action; the store builds on the
  percentage and says it); `RenkoToolbarTests` +4 (Percentage on Kagi only; Apply; prefill never
  as a price; over 100 refused at the percentage field). Sabotaged 18 ways, all red (two only after
  the tests were tightened: the store test now uses closes where percent and fixed draw different
  line counts, and the dialog's duplicate over-100 check was dropped, the refusal coming from
  `BuiltCharts` at the same field).

### Kagi chart type, 2026-11-19 — not yet heard by Cody

Cody: *"continue with the next chart type"* — Kagi, after range bars and line break.

- **Builder.** `Charts/KagiBuilder`: closes only. The first line runs from the first close to the
  first close one reversal amount away; a close past the newest line's end extends it; a close at
  least one reversal back starts a new line from that end. A line's `Date` is the bar that turned
  it and never moves as it grows; its span runs from the bar after the line before ended to its
  own end, and its volume is that window's. The forming bar is fed in, so the newest line follows
  the live close. `PriceChartStyle.Kagi = 6` on the end; brick and block style (one part, no
  wicks). `RenkoSettings` gains `KagiMode`, `KagiSize`, `KagiAtrLength` (ATR 14 by default); an
  old file reads the defaults. `BuiltCharts` covers it like the other three.
- **Thick and thin.** `KagiLines` works them out from the lines in order (the first line thick
  going up, thin going down; an up line turns thick past the line before's top, a down line thin
  past the line before's bottom; otherwise it keeps what it started with). One `Look` per line
  (thick at start, thick at end, turn price) serves the drawing and the speech alike.
- **Drawn.** `StandardRenderers.RenderKagi`: one vertical per line at the bar's centre, split at
  the turn price, joined by a shoulder to the next; thick 3 px × density, thin 1.25 px; the
  theme's up and down body colours (or the user's two Kagi line colours), colour-vision safe.
  The looks are worked out once per frame for the whole history (`RenderContext.KagiLooks`), so
  scrolling does not change a line's thickness.
- **Read and heard.** The price series is named *Kagi*, one part **Kagi line**: *"Kagi line. Up,
  turns thick at 120.00. 95.00 to 125.00. window."*; the summary, status line (`Up Kagi line,
  thick`) and *"New up Kagi line, turns thick at 120.00, 95.00 to 125.00."* carry it too. A
  growing line says *"Kagi turns thick at 120.00."* (or thin) once, on the update that passed the
  turn price, on the narration channel. Change sentence *"Kagi chart, reversal amount 512.30 from
  ATR 14."* / *"… fixed reversal amount 500.00."*.
- **A line growing is not a new bar.** Background Kagi tabs (`BrickNews`) say only lines turned
  by the bar that closed. The new-bar alerts (`AlertEvaluator.DrawnBar.SameBar`) match a bar by
  date, open and direction, not close, so a growing line neither re-seeds nor fires and the next
  line fires once; this changes nothing on the other types, whose drawn bars never change.
- **Chosen from.** Toolbar Chart type list (Kagi), **Kagi settings** in the shared dialog
  ("Reversal amount", ATR or fixed), Go bar `KAGI`, `kagi chart`, `KAGI 500`, `KAGI ATR 20`
  (`SystemCommand.ShowKagiChart`, not persisted by ordinal).
- **Caveats.** Thick and thin have NO sound of their own: a Kagi line pings like a brick (high
  up, low down), because the sound path sees one value and not the lines before it — question
  for Cody. TradingView's percentage reversal is not built (ATR and fixed only) — question for
  Cody. Not seen or heard by Cody.
- **Tests:** `KagiChartTests` (27: builder, dates and spans, refusals, thick/thin, BuiltCharts and
  the reducer, the series shape, arrowing, summary, change sentence, live new line and live turn
  once, `KagiTurnNews`, background news, new-bar alerts, Go bar words and arguments, "already",
  the dispatcher, and the drawing's thick/thin split by pixel width). Sabotaged 17 ways (extending
  makes a new line, strict reversal, span from the wrong bar, turn edge from the close, no thin
  turn, thickness by direction, background news counts growth, `SameBar` compares the close, turn
  said when already past, turn said every update, renderer split and shoulder thickness, Go bar
  style and action, dispatcher, arrowing words, summary words) — every one red.

### Alerts on the charts built off the clock, 2026-11-19 — not yet heard by Cody

Cody: *"yes on your suggestions on the alerts."*

- **Real price.** `AlertEvaluator.OnTheMarket`: on Renko, range bars and line break, price, candle
  and point-of-control alerts read the last two REAL time bars (`TimeBars`), whatever bars the
  caller handed in; other charts keep `AsDrawn` (Heikin-Ashi). Drawn levels are plain price alerts
  already, so they follow. Candle patterns take the real candles as context too. A price alert
  compared with a LINE measures both sides against the line's newest value, because the line is
  per brick and the brick before is not the bar before.
- **Indicators unchanged**: read at the newest brick, as drawn.
- **On-screen chart evaluates every real tick.** `AlertOrchestrator` now watches `TimeBars` as well
  as `Data` (the bricks move only when one completes) and needs two MARKET bars, not two bricks.
- **New conditions** `NewUpBar`, `NewDownBar`, `Reversal` (appended to the persisted enum), Candle
  target only. Read off the completed drawn bars: every brick/range bar/block, or on a time chart
  every candle but the forming one (Heikin-Ashi as drawn). Per alert, remembers the newest bar it
  saw and the chart (identity, type, box, Heikin-Ashi); first look, another chart or a bar it
  cannot find again re-seeds silently. Several new bars fire once, for the newest match. Speech
  *"Name: new up brick, 120 to 130."* / *"Name: reversal, new down block, …"*.
- **Watchability.** The hosted no-chart monitor refuses the new conditions (it does not know the
  chart type); the local monitor builds the tab's chart and watches them. A non-Candle target with
  them is refused as unfireable. Dialog labels name the chart's bar; Changes direction on a built
  chart says "the real candle's colour flips".
- Tests: `BuiltChartAlertsTests` (19). Sabotaged 11 ways (bricks instead of real bars, line's
  brick-before level, pattern context, no warm-up/chart key, unknown bar fires, forming candle
  judged, oldest match said, reversal = any down, orchestrator ignores TimeBars, orchestrator needs
  two bricks, watchability target) — every one red; two first survived and got their own cases.

### Range bars and line break chart types, 2026-11-19 — not yet heard by Cody

Cody, after the series naming: *"yes, continue everyting reads good now"* — range bars and line
break next, Kagi and Point & Figure after.

- **One door.** `Charts/BuiltCharts` is the only place that knows the three types built off the
  clock (Renko, range bars, line break): name, noun ("brick", "range bar", "block"), which
  settings fields are each type's own (`SameFor`, `Merge`), resolving the frozen size, building
  and planning. The reducers, `HeadlessChart`, both background monitors, the dialog and the
  commands call it with the chart type. `PriceChartStyle` gains `RangeBars = 4` and
  `LineBreak = 5` on the end; `IsBrickStyle()` is all three, `IsBlockStyle()` Renko and line break
  (no wicks, one part).
- **Builders.** `RangeBarBuilder`: equal high-to-low range, closes at the edge it broke, keeps
  wicks; built from time bars, so the in-bar path is ASSUMED (up bar open-low-high-close, down
  bar open-high-low-close); gaps walked; only completed bars. `LineBreakBuilder`: closes only,
  continues past the last block, turns past the extreme of the last N blocks (1 to 50, default 3).
- **Settings.** `RenkoSettings` gains `RangeMode`, `RangeSize`, `RangeAtrLength`,
  `LineBreakLines`; an old file reads the defaults. Line break's frozen "box" is its line count,
  so every "is a size resolved" check holds unchanged.
- **Read and heard.** Line break's price series has one part, **Block** (not "line", which is the
  Line chart): *"Block. Up. X to Y. window."* Range bars keep the three candle parts and read as
  candles, with the bar's real window. Series names "Range bars" / "Line break"; change sentences
  *"Line break chart, 3 lines."*, *"Range bars chart, fixed range 500.00."*; *"New up block, …"*;
  status line `Up block …`; layout, playback and background news use the type's noun. Choosing a
  type from another speaks the new type's sentence once (the settings-changed path needs the same
  type before and after).
- **Chosen from.** Toolbar Chart type list (Range bars, Line break); one settings dialog that
  shows the shown type's settings (Range ATR/fixed, or one Lines field); Go bar `RANGEBAR` /
  `RANGEBARS` / `range bars` and `LINEBREAK` / `LBREAK` / `line break` / `three line break`.
  `RANGE` stays the measure tool's word, and codes are capped at eight letters, hence `RANGEBAR`
  and `LBREAK`. `ICommandDispatcher.ShowBuiltChart(style, settings)` merges only that type's
  fields, so `RANGEBAR 500` keeps the tab's Renko box.
- **Found on the way.** The settings dialog built `new RenkoSettings(mode, box, atr)` on Apply,
  which would have reset every other type's settings; it now uses `state.Renko with { … }`.
- **Caveats.** Alerts are UNCHANGED, waiting on Cody's yes to the split (price and drawn-line
  alerts on the real price, indicator alerts on the bars, new up/down/reversal alerts). Restoring a
  workspace across built types re-resolves the size instead of keeping the saved one. The
  range-bar path assumption above. Not seen or heard by Cody.
- **Tests:** `BuiltChartTypesTests` (47 cases: both builders, Merge/SameFor, switching between the
  three in the reducer, naming, speech, change sentences, background news, the Go bar words and
  arguments, the dispatcher merge, "already", and the save round trip); `RenkoSpeechTests`'
  settings refusal updated.

### The price series is named after its chart type and read by the template, 2026-11-19 — not yet heard by Cody

Cody tried 9f5ac4ad: *"when i change to renko and use pg up/down and land on that series, it is
still called candles even though they are renko bricks. same for line or area"* and *"as with
any other line it needs to follow the speech template, component name, component type ... The
main series pane right now just reads values"*. Sound was right; the name and the words were not.

- **Name.** `PriceSeriesShape.NameFor` gives Candles, Heikin Ashi, Line, Area or Renko, and `Apply`
  renames the price series (`Name` and `FriendlyName`) whenever it reshapes it, so Page Up/Down,
  the indicator bar, the Object Tree and Properties all say it. Only a name that is one of those
  five is replaced: a name the user gave is kept. An unnamed series stays unnamed on Candles.
  `Canonical` saves "Candles", so saved workspaces and templates do not change.
- **Reading.** New `PriceLineStrategy` and `BrickStrategy` in `SpeechFormatter`'s strategy list.
  The close line reads *"Close. line. 104.20. Up 4.20 from the previous close, 4.20 percent."*
  (Area: *"area"*), the brick *"Brick. Up. 110.00 to 120.00."* then its real window. On Percent
  and Indexed the scale reading leads the VALUE (*"Close. line. plus 4.20 percent, 104.20. …"*),
  as on the candle body, instead of the whole sentence. Value-only order drops the name and type
  as for every other part. The summary reading and the part reading go through
  `FormatTemplateValue`, so hidden/muted and look suffixes apply as on any part.
- **Caveats.** A Line/Renko chart whose price series still has candle parts (not reshaped; the
  store reshapes after every action, so only hand-built test states get there) keeps the old plain reading. The brick's "type" slot holds its
  direction (Up/Down), mirroring the body's Bullish/Bearish; Cody has not confirmed that choice.
- **Tests:** 15 new or changed cases in `ChartTypeFocusTests` and `RenkoSpeechTests`. Five
  sabotages went red: Renko named Candles (3), save keeps the Renko name (1), a user's name
  overwritten (1), the line template removed (8), the brick template removed (2).

### Background Renko tabs say their new bricks, 2026-11-19 — not yet heard by Cody

Cody: *"yes keep price as option indicator overlay, yes announce new bricks on renko views"*.
Until now a Renko tab in the background, and a saved Renko tab with the browser closed, said
real time-bar closes at real prices, and its narration ladder read the candles.

- **What is said.** New `BrickNews` (Core/Services/Charts). At a bar close it builds the bricks on
  the CLOSED bars only (the last fetched bar is still forming) and keeps those whose completing bar
  is the one that just closed. *"BTC/USD 1h: 2 new up bricks, 100.00 to 120.00."*, then the ladder.
  The toast is titled *"BTC/USD 1h: new brick"*. A close that completed no brick is silent: no
  earcon, no toast, no speech. A brick the forming bar made and took back is never said. The box
  is the tab's frozen box when it has one, else the box its settings resolve on these bars.
- **Open background tab:** `BackgroundBarAnnouncer` takes the brick branch for a tab whose
  `ChartStyle.IsBrickStyle()`, publishes new `BackgroundBricksFormedEvent`, and speaks under the
  same "Also speak bar closes from other tabs" switch. `DesktopNotificationService` toasts it
  under the same gates as a background bar close (switch, playback, already being said).
- **Browser closed:** `LocalBackgroundMonitor` does the same for a saved Renko tab, and fetches
  at least `BrickNews.HistoryBars` (500) bars there, so the bricks go the tab's way rather than
  starting fresh off a short catch-up window. With no brick, the ladder alone may still speak.
- **The ladder reads the bricks.** The background narrator is now built with the tab's chart
  type, Renko settings and frozen box. Found on the way: `HeadlessChart` on a brick chart scanned
  a real-bar index into brick-indexed arrays. It now scans the newest completed brick, once per
  new brick, and reseeds its scanner after a trim instead of shifting indices.
- **Tests:** `BrickNewsTests` (5), four in `BackgroundBarAnnouncerTests`, two in
  `DesktopNotificationServiceTests`, `A_Renko_tab_says_its_bricks_with_the_browser_closed` (bare
  and with a headless chart; `HeadlessMonitorHarness` takes a `shapeTab`). Sabotages, each red:
  no brick branch, the forming bar counted, the narrator built as candles, the real-bar scan
  index, the monitor's brick branch off, the short fetch, the toast unsubscribed.
- **Caveats.** Not heard by Cody. Price stays an optional overlay, as he confirmed; nothing
  changed there. A Renko tab's alerts still evaluate on the bricks as drawn (open question).
  The ATR box on a background tab with no frozen box is measured on the fetched history, so it
  can differ slightly from the open chart's.

### The price series takes the chart type's shape; Alt+C and Alt+L focus the lists, 2026-11-19 — not yet heard by Cody

Cody tried 58d9bad0: *"I select renko ... I still hear 3 components like the candles are still
there ... they all read as 'up brick' ... I switch to line and i still see candles ... if a line
is drawn then the candles series should be replaced with a line series and same for the bricks"*.
The first version kept the candle parts and sent the arrows to the separate Price indicator,
which his charts do not have. So he walked a line chart's wicks, and three parts on Renko.

- **The main price series reshapes.** New `PriceSeriesShape` (Core/Services/Charts). On Candles
  and Heikin Ashi it has three parts (upper wick, body, lower wick). On Line it has one part,
  "Close", a line. On Area it has one part, "Close", filled. On Renko it has one part,
  "Brick". `WorkspaceStore.Dispatch` applies the shape after every reduce, so every path that
  changes the chart type (the select, the Go bar, a loaded workspace) reshapes. The candle parts
  are kept in `SeriesConfig.CandleParts` and come back with their settings. The one part is made
  from the body, so colours, volume, visibility and mute go with it and return to the body.
- **The brick has its own sound.** `ComponentDisplayType.Brick` has its own row in
  `SonificationProfileProvider`: a short ping, pitched by direction (the body's up/down pair).
  The line and area parts use the line and area sounds. Speech is one reading per brick or
  point: *"Up brick, 110.00 to 120.00."*, *"Close X. Up Y from the previous close, Z percent."*
- **Saves keep the candle shape** (`PriceSeriesShape.Canonical` in `WorkspaceLibraryService`
  and `SeriesManagementService`). The background monitor, alerts, templates and older builds
  reading a save meet the series they always have.
- **The Price indicator is optional.** On Line and Area it only repeats the close line, so it
  leaves the arrow rotation and focus asked of it lands on the price series
  (`ChartTypeFocus.Redirect`). A Price line remapped to the high stays. When the change moves
  focus off the Price line, the chart-type sentence says where it went: *"Line chart. On the
  close line."*, *"Renko chart, fixed box 500.00. On the bricks."*
- **Alt+C and Alt+L focus the lists.** Cody: *"also change alt c and l to focus on those
  dropdowns"*. New `SystemCommand.FocusChartTypeSelect` and `FocusPriceScaleSelect` (global keys)
  publish `FocusToolbarControlEvent`. The toolbar leaves focus mode first if it is on, then
  focuses the list. On a tab with no lists it says *"No Chart type list on this tab: it is for
  price charts."* Go bar codes CTYPE and PSCALE. Heikin Ashi and Log no longer have a key of
  their own: they are entries in the lists, and the Go bar's HA and LOG still toggle them.
- **Found on the way:** the Renko merge never enrolled `ChartTypeSettingsModal` in the modal
  catalog. It is now enrolled, with focus on its title.
- **Tests:** `ChartTypeFocusTests` rewritten for the new design. Also new or changed:
  `ChartListKeysTests` (new), the Line cases in `ChartKeyboardNavigationTests`, a one-brick test
  in `RenkoSpeechTests`, `ChartTypeAnnouncementTests`, `Phase5KeyboardScopeTests`, and the modal
  catalog and focus contract. The browser screenshot probe now picks chart type and scale from
  the lists. Each of these sabotages turned the tests red: removing the store's Apply call (14
  failures), the brick sound row, `Canonical`, the mute carry, the old Alt+C binding, and the
  toolbar not leaving focus mode.
- **Caveats.** Cody has not heard any of this. A `shortcuts.json` saved by the user replaces the
  defaults wholesale, so a saved file keeps the old Alt+C/Alt+L toggles until it is reset. None
  exists on Cody's machine. Still open for Cody: whether alerts on a Renko chart should read
  bricks "as drawn". (Background Renko tabs announcing bricks: answered yes, built in the entry
  above.)

### Chart type and price scale are HEARD, not just drawn; HA and Log buttons removed, 2026-11-19 — not yet heard by Cody

Cody tried Line, Area and Percent and heard no change: *"I still wasn't able to arrow around the
candles... was the actual main price pane supposed to change or is it just visual?"* Only the
candles' summary reading knew about the chart type or the scale. His rule: *"whatever is selected
should be mirrored visually and through sound and speech."*

- **Line and Area: the arrows walk the line.** New `ChartTypeFocus`. When the chart type is Line
  or Area and the Price series draws the close, the candles leave the Page Up/Down rotation
  (`NavigationEngine.NavigateSeries`) and the next/previous-series commands
  (`CommandDispatcher`). `SelectSeriesAction` for the candles lands on Price, and so does the
  fallback after removing the focused series. Choosing Line/Area (select, Go bar) moves focus
  from the candles to Price; Candles/Heikin Ashi/Alt+C moves it back from Price to the candles
  (`PlaybackReducer.FollowChartType`). Focus on an indicator is never moved. The chart-type
  sentence adds where the focus went: *"Line chart. On the price line."*, *"Candle chart. On
  the candles."*
- **The Price line reads the point on the line** on Line/Area, in both summary and component
  context: *"Close X. Up Y from the previous close, Z percent."* (the reading the candles had).
- **Percent and Indexed lead every main-pane price reading.** The scale reading now also comes
  first on the Price series (summary and its close part), on each candle part in component
  context (*"plus 4.20 percent, Open 95,000.50"*, using that part's own change), and on Price
  series components and `{value:price}` templates in the value-only speech order, which skipped
  the template before. Indicator panes are untouched; Regular reads exactly as before.
- **Toolbar:** the separate Heikin Ashi and Log scale buttons and their sprite icons are gone;
  the Chart type and Price scale selects carry both. (Alt+C and Alt+L were unchanged here; the
  entry above makes them focus the lists.)
- **Tests:** `ChartTypeFocusTests` (new), three Line-chart cases in
  `ChartKeyboardNavigationTests` (Page Up stops on Price, *silently* at the top),
  `ChartTypeAnnouncementTests` focus clause, `ToolbarControlSurfaceTests` (buttons gone, the
  lists carry them). Fourteen sabotages were each red: the series-key filter, the Page Up/Down
  filter, both redirects, both focus moves, the Price line reading, the Price scale prefix, the
  candle-part prefix, the `{value}` and value-only paths, the high-mapped Price line, a
  returned sprite icon and the "On the price line" clause.
- **Caveats.** No new sound was designed, but the sound follows the focus, so on Line and Area
  arrowing now plays the Price line's tone (the one arrowing to Price always made) instead of
  the candle sound. Percent and Indexed change no pitch, as before. A Price series
  remapped away from the close (to the high, say) keeps the candles in the rotation, because it
  is a different line. The status line, detail key, alerts and orders stay in prices, as
  before. Cody has not heard any of this.
- **Joined with Renko** (rebased onto the Renko merge, 2026-11-19): Renko keeps the candles
  navigable, because the bricks ARE the candles series. Choosing Renko or Heikin Ashi from Line
  or Area moves focus back to the bricks, and Line/Area from Renko moves it to the Price line:
  the Renko style and Alt+C paths go through `FollowChartType` too. Percent and Indexed lead a
  brick reading as well: *"Plus 10.00 percent. Up brick, 110.00 to 120.00."* The base is the first
  visible brick's close. The Renko toolbar's comment about the HA button was rewritten, since the
  button is gone. Tests: two brick-scale cases in `RenkoSpeechTests` and a Line → Renko → Line
  focus case in `ChartTypeFocusTests`. Removing the brick prefix and unwrapping the style path
  were each red.

### Renko chart: price bricks, with the market kept underneath, 2026-11-19 — not yet heard by Cody

- **A fourth chart type, and the shared brick plumbing under it.** `PriceChartStyle.Renko = 3`
  sits beside Candles, Heikin Ashi, Line and Area; `IsBrickStyle()` is the one test the rest of
  the app uses, so range bars, line break, Kagi and Point & Figure can land later under the same
  `BrickChart*` names. `BrickChartBuilder` (Core/Services/Charts) is pure arithmetic: a
  floor-anchored box grid at the chosen box, close-based continuation one box past the last
  brick, a two-box reversal, one brick per horizontal step and as many bricks as a bar crosses.
  Each `BrickSpan` records the REAL first bar after the previous completion and the completion
  bar, so a brick that took three hours says so. A bar can complete several bricks; bricks share
  the completion bar's `Date`. No bricks on a quiet move. The default box is ATR 14 on closed
  bars, rounded to `[0.01, price]`; `RENKO 500` or the dialog can fix it instead.
- **Frozen, not drifting.** The box resolves when Renko is chosen, when the first data of a new
  symbol/timeframe loads, or when the settings change — never on a live tick, a scrollback
  prepend, a replay prefix or a saved-tab restore. `BrickBoxSize` is saved with the tab and with
  the tab snapshot, so a restored chart redraws the same bricks; prepending older data extends
  the same grid backwards. A saved file with no Renko fields loads Candles and `ATR 14`.
- **Two lists, on purpose.** `WorkspaceState.TimeBars` is the real feed (always the full live
  series), `Data` is what is drawn (bricks on Renko), and computed `MarketBars` is `TimeBars ??
  Data`. `TabSnapshot.Data` stays the real bars so a restored tab resumes its feed; the bricks
  are rebuilt from them at the saved box. The reducers keep `Data` in step: `PlaybackReducer`
  enters/leaves Renko, `ViewportReducer.UpdateBrickData` bricks an initial load and keeps the
  frozen box on later updates, `TabReducer` rebuilds on restore and snapshots the time bars.
- **Money, feed, saves and catch-up read `MarketBars`.** Quick trade's arm price and last close,
  the trading dashboard's last/current price, `StrategyEngine.OnDataUpdated` (which now does
  nothing on a brick chart rather than treating a brick as a bar), `AddStrategy`/`LastClose`,
  `MarketOrchestrator`'s restore, catch-up, empty check and new-bar event, `ReplayService`,
  `PaperTradingProvider` (`ChartOn`, `PriceFor`, `OnState`), the workspace snapshot/restore path,
  and the strategy backtest/lab entry points all use the real series. The background monitor
  bricks the state its ALERTS read but hands strategies a separate real-bar state; the headless
  chart bricks its observations with the tab's box. Alerts read the chart as drawn, so a Renko
  tab's price/indicator alerts evaluate on bricks both focused and background.
- **Speech describes a brick.** The arrow-key readout on the candles series is
  *"Up brick, 95,000.00 to 95,500.00. 18 Nov 14:00 to 18 Nov 17:00."*; the status line's first
  line is `Up brick 95,000 → 95,500`; the layout description says *"Renko chart, box 512.30 from
  ATR 14."* or *"Renko chart, fixed box 500.00."*; a completed brick says *"New up brick, …"*
  instead of a new time bar; changing the box says *"Renko chart, …"* once. There is no candle
  body/wick/pattern talk on a brick, and the detail key does not add a candle fact. Playback,
  navigation, hover, window announcements and the focused-chart toast all take their timestamp
  from the brick's real `Start`–`End` window.
- **Refusals before dispatch, one sentence each.** `RENKO` on a chart with too little history
  for ATR says the exact shortfall; a box that would draw over 20,000 bricks says so; a
  non-positive fixed box, a flat chart with no bricks, an analytics single-line chart, and
  Brick settings on a non-Renko chart each say their one sentence. The toolbar select reverts
  on a refusal; the dialog stays open and focuses the offending field. Choosing Renko with the
  same settings again says *"Already a Renko chart."* A workspace restore chooses the style
  before data arrives, so the reducer keeps the choice with an empty brick set and the first
  load resolves the box.
- **Entry points.** Toolbar Chart type select (Renko option) plus a `#brick-settings-button`
  immediately after it only on Renko; `ChartTypeSettingsModal` with ATR/Fixed, length, box and
  the resolved-box preview; Go bar `RENKO`/`renko chart`/`bricks`, `RENKO 500`, `RENKO ATR 20`,
  and `brick settings`/`renko settings`. `SystemCommand.ShowRenkoChart` and
  `ShowBrickSettings`, `ICommandDispatcher.ShowRenko`, and Phase-5 keyboard scoping for both.
  Help, the user manual and shortcut docs updated.
- **Tests.** The standing filter runs **4,650 tests, all passing**. New coverage: builder
  continuation/reversal/reversal-size/multi-brick bars/shared completion dates/volume/prepend
  grid/ATR/floor edges/over-cap/zero-box/flat refusals; ATR and fixed resolution; model and
  snapshot equality including the box; settings round-trip and old-file default; reducer
  entry/leave/re-entry/HA/frozen box/live update/empty pre-load restore/restore-from-snapshot;
  `BrickChartSpeech` sentences and window; status line; orientation; coordinator new-brick,
  settings change and viewport; navigation readout; dispatcher refusals; toolbar select, refusal
  and settings button; dialog prefill, apply, refusal focus and Escape; Go bar forms; money paths
  (quick trade, dashboard, strategy initialize/evaluate, orchestrator catch-up, replay);
  background monitor alerts-vs-strategies; headless observation; workspace persistence. The
  in-scope filters all pass.
- **Sabotages** (each backed up in `.sab/`, built, the 4,650-test filter run and restored with
  `cp`/`touch`/`cmp`; failures are in that filter):
  (a) bricks built from High instead of Close — **3 red**;
  (b) reversal after one box — **1 red**;
  (c) quick trade reads `Data` again — **1 red**;
  (d) tab snapshot saves bricks — **1 red**;
  (e) ATR box recomputed on every update — **2 red**;
  (f) readout speaks only the completion clock instead of Start–End — **2 red**;
  (g) 20,000-brick cap removed — **2 red**;
  (h) `OnDataUpdated` runs on a Renko chart — **1 red**;
  (i) background monitor ignores the tab's style — **1 red**;
  (j) `RenkoSettings` default loses ATR 14 — **10 red**;
  (k) grid anchored at the first close instead of its floor multiple — **2 red**.
- **Review fixes (Claude, 2026-11-19), each with a test that goes red when the fix is undone.**
  The build above was DeepSeek's; the review found seven places still reading the bricks.
  (1) `MarketFeeds` answered the focused chart's bars from `state.Data`: anything asking the
  feed seam for a Renko chart's bars got bricks. (2) The browser tab title priced the chart off
  the newest brick's close. (3)–(5) Paper trading: a market order priced off the brick's close,
  an open position valued off it (`PriceFor`), and resting stops and limits checked against the
  brick's high and low, so a limit could fill at a price the market never traded at. These read
  `MarketBars` already; nothing tested them, and undoing each left every test green. (6) Saving
  a Renko tab recorded the first BRICK's date as the history depth, so each restore fetched a
  little less (also already right, also untested). (7) **The real defect: a strategy on a
  focused Renko chart, and a backtest started from one, read indicators computed on the bricks
  against real bars.** The chart's series are indexed by brick; the strategy is handed bars, so
  "RSI at bar 40" was brick 40's RSI, and an Auto strategy traded on it. `StrategyCandles.For`
  now rebuilds a brick chart's series on the real bars, empties the price lines (brick prices)
  and hands over the market's own chart (`Data` = real bars, Candles style), the same shape the
  background monitor already built; one predicate, `StrategyCandles.NeedsRebuild`, tells the
  live engine and the backtester when, so the three cannot disagree. A backtest with no
  indicator service on a brick chart refuses in words instead of reading brick values. Also:
  the Brick settings dialog's two number fields bind on input, not change — Enter's keydown
  reaches the server before the field's change event, so typing a box and pressing Enter
  applied the previous box. New tests: `RenkoStrategySeriesTests` (6), three paper tests and a
  save test in `RenkoMarketBarsTests`, `FocusedRenkoChart_AnswersWithTheRealBars_NotTheBricks`,
  `TheTitlePriceReadsTheLastRealBar`, `Enter_applies_the_box_just_typed`. Twelve sabotages,
  each red. The accessibility review agents are not registered here; the dialog's markup was
  reviewed by hand (labelled fields, a legend on the radio group, refusals spoken and focused).
- **Caveats, deliberately left out of this change.** A background Renko tab's bar closes are
  announced and notified from that tab's feed, so they say real time-bar closes at real prices,
  not bricks. (Superseded the same day: Cody said yes, and background Renko tabs now announce
  their bricks; see "Background Renko tabs say their new bricks".) A brick still forming can be revised or vanish when the feed bar pulls
  back before close; the frozen box never changes, and a completed brick never does. CSV export
  exports the chart as drawn (bricks and brick-indexed indicators), not the market series. Quick
  trade's cursor bar still reads the focused brick's high/low for a stop the user pointed at;
  its arm and last price are real. The ATR box is measured with the same wilder-style ATR the
  ATR indicator uses, on real bars. Range bars, line break, Kagi and Point & Figure are not in
  this change.

### Indicator templates: save and apply a chart's indicators, 2026-11-19 — not yet heard by Cody

- **A chart's indicators and panes, without its symbol.** A template is every non-core,
  non-drawing indicator on the chart in front, saved as its full `SeriesConfig` (parameters,
  string parameters, colours, levels, components, excluded components, pane, pinned pane, mute,
  volume, visible and narration switches) plus the tab's `PaneHeightRatios`. It is one
  `IndicatorTemplate` (`Version = 1`, `SavedFrom` for the dialog's description, `SavedUtc`), a
  file under `<AppDataDirectory>/IndicatorTemplates/<name>.json` beside `Workspaces`, one per
  user on the hosted server exactly as workspaces are. `IIndicatorTemplateService` has
  `GetAvailable`, `Save`, `Load` (null for missing or unreadable, never a throw to the UI),
  `Delete`, `Capture(IWorkspaceStore)` and the static `Describe`, which reads
  *"4 indicators: RSI 14, EMA 20, EMA 50, Cipher B; 2 panes"*. The name guard is the workspace
  one moved to `ProfileNameGuard`, not a copy, so `..`, rooted paths and `alerts` are refused.
- **What is deliberately NOT in a template, and why.** Candles, Price, Volume and Heatmap are
  the chart's data shape and `InitializeDefaultSeries` owns them; drawings are anchored to this
  symbol's prices; a retired indicator has no provider behind it. Symbol, provider, market,
  timeframe, viewport, chart type, price scale and strategies are not saved either, because the
  point of a template is to move the indicators onto the chart in front and keep its chart.
- **Apply means replace.** Applying removes every non-core, non-drawing indicator on the front
  chart (candles, price, volume, heatmap and drawings stay), restores each template series
  through the same workspace path (`SkipRetired`, `MigrateSeriesConfig`,
  `RestoreSeriesFromSaved`) with a NEW id, applies the saved pane heights through
  `RestorePaneHeightRatios`, and leaves symbol, timeframe, provider, viewport, chart type,
  scale, drawings, alerts and orders alone. The restore publishes `IndicatorUpdatedEvent`, so
  the new indicators compute on the bars already loaded without a chart reload. If the focused
  series was removed, focus goes where `SetPrimaryAndFocusFromRestore` puts it (candles, price,
  then the first series). This logic is in `WorkspaceInitializer.ApplyIndicatorTemplate`, not in
  a razor file.
- **One undo step.** `SeriesReplaceUndo` holds the removed `ChartSeries` whole (as
  `SeriesDeleteUndo` does), the added series by id, and both pane-height maps. Ctrl+Z puts the
  exact indicators back with the same ids and settings, removes the template's, and restores
  the pane heights; the label is *"Apply indicator template NAME"*, so the existing undo
  sentence reads *"Undone: Apply indicator template NAME."* Redo re-applies the same edit.
- **Dialogs, copied from the workspace pair.** **Save indicator template** names the template,
  lists existing ones to overwrite, says exactly what will be saved —
  *"What will be saved: 4 indicators: … from Kraken BTCUSDT 1h."* — and refuses an empty chart
  with *"This chart has no indicators to save as a template."*, writing nothing. **Apply
  indicator template** lists each template with its description, has a delete button per
  template labelled *"Delete indicator template: NAME"*, and says
  *"Replaces this chart's indicators. Symbol, timeframe and drawings stay. Control plus Z undoes
  it."* above the list; an empty list says *"No indicator templates saved yet."* Both run Save
  inline on the dispatcher (the Save workspace circuit lesson) and use the same
  `ModalStateChangedEvent` / `ModalName` wiring.
- **Every sentence** (Interface channel, one announcement each): saved —
  *"Indicator template saved: NAME, 4 indicators."*; nothing to save —
  *"This chart has no indicators to save as a template."*; applied —
  *"Indicator template NAME applied to Kraken BTCUSDT 1h: 4 indicators."* with
  `LastRestoreNotice`'s retired-indicator sentence appended when one was skipped; missing or
  unreadable — *"Indicator template NAME could not be read."*; deleted —
  *"Indicator template deleted: NAME."*; the Go bar's unknown name —
  *"No indicator template called NAME."*; undo — the existing *"Undone: …"* with the label above.
  The public demo refuses the commands with *"Not in the public demo."* (Save workspace and
  Load workspace remain silent there — their dialogs are simply not mounted — but a new silent
  path was not copied).
- **Entry points, no new key.** Two toolbar buttons in the "Workspace persistence" group after
  Load workspace, gated by `Demo.AllowWorkspaceSaveLoad` exactly as those are: **Save indicator
  template** and **Apply indicator template**, on the existing save/load workspace icons. New
  `SystemCommand.SaveIndicatorTemplate` / `ApplyIndicatorTemplate` with dispatcher cases beside
  the workspace ones and Go bar entries beside them too: `TPLS` (*save template*, *save
  indicators*) and `TPL` (*template*, *indicator template*, *apply template*, *load template*).
  `TPL name` applies that template directly with no dialog and the same sentences; `TPL` alone
  opens the Apply dialog. No code collided with the existing registry.
- **Caveats.** There is no "add to this chart's indicators" mode: applying always replaces, as
  decided in the handoff; a template cannot be merged into an existing stack. Volume is not
  part of a template (it is core and belongs to the chart's data shape). Alerts on an indicator
  that the apply removes behave exactly as when that indicator is deleted by hand: an alert
  naming that instance is degraded once — *"the CODE it was set on is no longer on this chart,
  and a different CODE will not be watched in its place"* — while an alert naming only the
  indicator code keeps watching the first series with that code, which may be a template's
  indicator. Alerts are never deleted, and they are not saved into a template. A template saved
  from a chart with no indicators cannot be written. Not heard by Cody.
- **Tests.** The specified filter runs **2,598 tests, all passing**. New coverage: the service
  round-trips every kept field (levels, string parameters, an excluded component, components,
  muted, hidden, pinned pane) and the pane ratios; `GetAvailable` and delete; the shared name
  guard for `..`, a rooted path and `alerts`; an unreadable file as null; the folder beside
  `Workspaces`; per-user isolation; `Describe`'s wording and pane count; capture leaving out
  candles/volume/drawings/a retired indicator and keeping two EMAs and an RSI with its pane;
  workspace capture still keeping its drawings and retired series (the control); apply keeping
  symbol, timeframe, candles and the drawing while replacing A and B with C and D with new ids;
  pane ratios; the same template on two tabs giving different ids; undo restoring the old ids
  and settings and removing the template's; focus on candles after removing the focused
  indicator; a retired template indicator skipped and named; a template whose only indicator is
  retired still replacing the chart; computation on already-loaded bars through
  `DataOrchestrationService`/`IndicatorUpdatedEvent` (with the store's state stream silenced, so
  only the event path can pass it); the save and apply dialogs' descriptions, refusals, saves,
  applies, deletes and speech; the demo gating of all four toolbar buttons; the Go bar by
  name/code/alias, `TPL name`, its unknown-name sentence, the retired notice, and the demo
  refusal; the dispatcher cases; and host parity plus the per-user WebHost path.
- **Sabotages** (backed up in `.sab/`, built, filter run, restored with `cp`/`touch`/`cmp`;
  red counts are failures in the 2,595-test filter the sabotage runs used, before the last three tests were added):
  (a) capture keeps drawings — **1 red**;
  (b) capture keeps VOLUME — **1 red**;
  (c) apply keeps the old indicators — **4 red**;
  (d) apply removes drawings — **2 red** (the apply test, plus `MarketEventEarconTests` once —
  a timing test that passes alone and in the clean run, as the price-scales entry also saw);
  (e) apply reuses the template's ids — **2 red**;
  (f) apply skips the recompute event — **1 red**;
  (g) undo forgets the removed series — **1 red**;
  (h) the name guard lets `..` through — **1 red**;
  (i) apply changes the symbol and timeframe — **1 red**;
  (j) the save refusal on an empty chart is removed — **1 red**.
- **Review (Claude, 2026-11-19).** The "applied" sentence counted the template's series, not
  the ones that went on: a template holding RSI and a retired indicator said "2 indicators" and
  then named the retired one as left off, with one indicator on the chart. The apply now returns
  how many went on and the dialog and the Go bar say one shared sentence
  (`IndicatorTemplateService.AppliedSentence`) built from that number; a test through the real
  initializer pins it. Filter re-run: 2,598 passing. My sabotages, each red: count the template's
  size (2), undo leaves the template's pane heights (1), focus left on a removed indicator (1),
  capture keeps a retired indicator (1), templates written into `Workspaces` (6), WebHost
  registration removed (2), template ids reused (2). The accessibility review agents are not
  registered here; the two dialogs were checked by hand against the workspace dialogs they copy
  (same listbox, labelled name box, status lines, labelled delete buttons). Like Load workspace,
  each Delete button sits inside its list option; not checked with a screen reader.

### Price scales: Percent and Indexed to 100, 2026-11-19 — not yet heard by Cody

- **Two more rulers for the price pane, sharing one choice with Regular and Logarithmic.**
  `PriceScaleMode { Regular = 0, Percent = 1, IndexedTo100 = 2 }` lives beside
  `WorkspaceState`, and `WorkspaceState.ScaleMode` is a per-tab init property saved and
  restored everywhere `ChartStyle` and `IsLogScale` are. The toolbar gained a native
  **Price scale** select (Regular / Logarithmic / Percent / Indexed to 100) immediately before
  the Alt+L Log scale button, with a visually-hidden `<label for="price-scale-select">Price
  scale</label>` and the same trading-tab gate that button has. One store action,
  `SetPriceScaleAction(mode, log)`, keeps the pair consistent: Percent and Indexed clear the
  log flag, Logarithmic is Regular + `IsLogScale`, and turning log on (Alt+L, the toolbar
  button, Go bar `LOG`) returns the mode to Regular. `Regular` is 0, so every workspace written
  before the field loads on the axis it was showing.
- **The base is the close of the first visible bar AS DRAWN** — on a Heikin-Ashi chart that is
  the HA close, so the first candle on screen sits on zero — at `ViewportStartIndex` clamped
  into the data. It moves as the user pans, exactly as on TradingView. One helper,
  `ChartMath.ScaleBase`, is used by the renderer, speech and the describer, so the zero they
  each quote is the same number. Percent reads `(p / base − 1) × 100`; Indexed reads
  `p / base × 100`. If the base is zero, negative or missing (an analytics series can be), the
  mode cannot apply: the chart draws and reads Regular, the request is refused aloud, and the
  state stays Regular when the choice came from the Go bar or the toolbar.
- **Drawing is an affine relabel of the unchanged pixel mapping.** The price axis computes its
  1–2–5 step over the pane's percent (or index) extent, then places each tick at
  `base × (1 + pct/100)` — `base × idx/100` for Indexed. Labels are two decimals invariant:
  `"4.00%"`, `"-2.00%"`, `"0.00%"`; Indexed `"104.00"`. The gridline layer walks the SAME tick
  list (`ChartMath.ScaleTicks`) with the same shared-step rule, so every label still sits on a
  line in the new units. The crosshair badge and the per-plot value labels on the price axis
  show scale units; the status line, the bar-detail key, alerts, orders, drawings, typed prices,
  sonification and playback narration keep prices, and every indicator pane is unchanged in
  every mode.
- **The COMPARE overlay is rebased to ITS OWN first visible value** in Percent and Indexed:
  drawn at `chartBase × v / vFirst` (the first non-NaN visible value if the first is missing),
  so the two symbols start together at the left edge and drift apart — TradingView's compare
  behaviour, and each spoken reading is that symbol's own change. `ChartMath.RebaseCompareValue`
  is the one transform the renderer, the range calculator and the speech all call; other
  main-pane overlays are prices of this symbol and are not rebased. The range calculator uses
  the rebased values, and the store refits the range when the mode changes on a chart carrying
  a visible COMPARE overlay — through the store, so a paused market refits at once.
- **Speech:** the candles series readout (Candles, Heikin-Ashi, Line and Area alike) STARTS
  with the scale reading of the close as drawn, then says everything it said before —
  *"Plus 4.20 percent."*, *"Minus 1.30 percent."*, *"Zero percent."*, *"Index 104.20."*. A
  main-pane series whose template uses `{value:price}` reads the scale first and the price
  second: *"plus 4.20 percent, 95000.50"* or *"index 104.20, 95000.50"*. The COMPARE overlay
  reads from its own base; an RSI-pane reading is untouched; Regular is byte-identical.
- **Saying the change** (Interface channel, interrupt, one sentence from where the log toggle
  is spoken): *"Percent scale. Zero is 93000.50, the first close on screen."*, *"Indexed to
  100. 100 is 93000.50, the first close on screen."*, *"Regular scale."* (replacing *"Linear
  scale."*), *"Log scale."* on a log turn-on — including from Percent or Indexed, where the
  select also returns to Regular, and that is not named a second time. A repeat of the shown
  Go bar entry says *"Already a percent scale."*, *"Already indexed to 100."* or *"Already a
  regular scale."*; re-choosing Logarithmic in the select says *"Already a log scale."*. An
  unusable base says *"Percent scale needs a positive first close; showing the regular
  scale."* (Indexed likewise) and changes nothing. Panning does NOT announce the moving base;
  the layout describer carries it.
- **Orientation:** Ctrl+Alt+Shift+Y (and Alt+Shift+/) now says *"Percent scale from the first
  close on screen, 93000.50 on 3 November 14:00."* or *"Indexed to 100 from …"*, using the
  describer's bar-zone formatting — date and clock for intraday bars, the date alone for daily
  and coarser. The Y-axis clause says "percent scale" / "indexed to 100" instead of "linear
  scale" while one is on.
- **Go bar:** `REG` (*regular scale*, *linear scale*, *linear*), `PCT` (*percent scale*,
  *percentage*) and `IDX` (*indexed*, *index to 100*, *rebase*), categorised with
  `ToggleLogScale` and chart-scoped like it. No key was added; `LOG` remains the toggle and its
  own-words-first guard stayed green.
- **Analytics / single-line charts get the rulers too**, as TradingView allows percent on any
  series; the base is the Price series' first visible close. The toolbar select is gated
  exactly like the Log button (both trade-only), but the Go bar and command paths do not refuse
  a single-line chart the way the chart-type commands do. The status line stays in prices, so
  on an analytics chart the spoken status is the metric's own value as before.
- **Persistence and scope:** `TabSnapshot.ScaleMode`, `TabConfiguration.ScaleMode`,
  `WorkspaceLibraryService`, `WorkspaceInitializer` and the reducer's snapshot/restore all
  carry the field; a file without it loads Regular, and the restore writes mode+log in one
  action so an impossible Percent+log pair from an old file heals to Regular+log. A new tab
  starts Regular. The script-sandbox projection deliberately does not carry it: an axis relabel
  is presentation, like `ChartStyle` and pane ranges.
- **Caveats.** The base moves as you pan, as on TradingView, and is not pinned at a fixed bar.
  Add symbol and formula charts are NOT built; the comparison that exists today is
  `Compare symbol (overlay)` (COMPARE), and it is the one that rebases. The status line, bar
  detail, alerts, orders and typed prices stay in prices by design. Sonification is unchanged
  (pitch was already relative to the pane's range). Percent and Indexed have not been heard or
  seen by Cody. The describer's existing bar formatter had no clock clause for intraday bars;
  the new sentence uses the same `SpeechTimeFormatter.FormatBar` rule with `d MMMM HH:mm` for
  intraday and the existing long date for daily, matching the handoff's example.
- **Tests:** the specified filter runs **4,113 tests, all passing** (up from 4,027). New
  coverage: reducer invariants (Percent clears log, Percent cannot carry log, log on from
  Indexed gives Regular+log, per-tab modes, new tab Regular); library round-trip of both modes,
  an old-shape JSON that loads Regular, and restore sending mode+log in one action; the base
  helper (first visible, clamped, HA-aware, zero/negative/NaN unusable) and the effective-mode
  guards; axis labels and their y, Indexed labels, the crosshair badge and value-label text,
  Regular and Log label strings as controls, and a pixel test that the major gridline is on the
  row the percent label marks with a base where percent and price steps genuinely differ; the
  compare transform on the series copy, a pixel test of the rebased line's start and end, and a
  store-level refit test with a compare overlay plus a no-overlay control; speech for candles,
  Line, Heikin-Ashi, both modes, `{value:price}`, COMPARE's own base, an unchanged RSI pane and a
  Regular control; the spoken change sentences including "Already …" and the unusable base; the
  describer's sentences; toolbar bUnit for the labelled select, current value (Logarithmic wins
  when log is on), dispatch, the "already" and unusable answers and the analytics gate; Go bar
  reachability by name/code/alias and dispatch.
- **Sabotages** (backed up in `.sab/`, built, filter run, restored with `cp`/`touch`/`cmp`;
  red counts are failures in the 4,113-test filter):
  (a) base = the first bar of the whole history — **4 red**;
  (b) base from the raw bar on a Heikin-Ashi chart — **2 red** (a third failure in that run was
  `TabKeepsItsMarketTests`, a timing test that passes alone and in every clean run);
  (c) Percent leaves log on — **1 red**;
  (d) gridlines keep the regular step in Percent — **1 red**;
  (e) COMPARE is not rebased in Percent — **2 red**;
  (f) the store does not refit the range on a mode change — **1 red**;
  (g) the speech prefix is dropped — **5 red**;
  (h) an RSI-pane value is spoken in percent — **1 red**;
  (i) a zero base divides — **4 red**.
- **Review (Claude, 2026-11-19).** A refused Percent or Indexed in the toolbar select left the
  control SHOWING the refused entry over a regular axis: Blazor writes a select's value only when
  the rendered value changes, and a refusal renders "regular" before and after. The handler now
  renders the picked entry once and then the real one, so the control is put back; a bUnit test
  pins that the render passes through the picked entry and ends on Regular. Not checked in a
  browser. My sabotages, each red: no revert (1), crosshair badge left in prices (1), value
  labels left in prices (1), COMPARE spoken from the chart's base (1), tab snapshot drops the
  mode (1), library save drops it (1), frame renderer ignores it (1), log on keeps Percent (1).
  The accessibility review agents are not registered here; the select's markup was checked by
  hand (native select, visually-hidden label tied by `for`, never disabled).

### Chart types: Line and Area, 2026-11-19 — not yet heard by Cody

- **Two more ways to draw the price pane, sharing one choice with Candles and
  Heikin-Ashi.** `PriceChartStyle { Candles = 0, Line = 1, Area = 2 }` lives beside
  `WorkspaceState`; the toolbar gained a native **Chart type** select (Candles / Heikin Ashi /
  Line / Area) before the Alt+C Heikin-Ashi button, and the Go bar gained `CANDLE`, `LINE`,
  `AREA` and a code-less *Heikin Ashi chart*. Alt+C is still the toggle and no key was added.
  Candles = 0 so every saved workspace written before the field loads as candles.
- **Line** draws one 2 × density line through the RAW closes of the visible bars in
  `ChartTheme.PriceChartLine` (new non-required token, default TradingView #2962FF). **Area**
  adds a fill from the line to the pane floor in the same colour, fading to transparent at the
  bottom. No bodies, no wicks; hollow-up and colour-vision-safe candle settings do not apply
  because blue is already safe. The volume pane and every indicator pane are unchanged.
- **The Price series keeps its speech, navigation and sonification, and loses only its ink**
  while the style is Line or Area and every visible part of it maps to `close`. Otherwise it is
  the identical white line drawn on top of the chart type's line, hiding its colour. A Price
  whose mapping is `high` (or anything else) is a different line and still draws. The Price
  series still appears in the status line either way.
- **Autoscale is fitted to the closes on Line and Area**, not to highs and lows, so the line
  fills the pane as it does on TradingView. Reference levels and main-pane overlays still widen
  the range exactly as before, the existing 5% padding is unchanged, and the log scale still
  maps it unchanged. Candles and Heikin-Ashi keep the original high/low fit.
- **Speech on the candles series describes the point on the line**, not a candle:
  *"Close 95,000.50. Up 1,120.00 from the previous close, 1.19 percent."*,
  *"Close 95,000.50. Down 300.00 from the previous close, 0.31 percent."*,
  *"Close 95,000.50. Unchanged from the previous close."* and *"Close 95,000.50. First bar."*
  Prices are `SpeechPriceFormatter.FormatPrice`, the percentage is two decimals invariant with
  its sign removed (the word Up/Down carries it), and the close is always raw. No candle shape,
  pattern name, open/high/low or wick percentage is read, because none of it is on screen.
  Everything else is untouched: the Alt+Shift+D detail key still reads full OHLC, and
  candle-pattern announcements at bar close, alerts, playback narration and sonification keep
  their old meaning.
- **The status line's symbol line is `C <close>` in the line colour on Line and Area.** There
  is no O/H/L on the canvas. Candles and Heikin-Ashi keep O H L C as before. The layout
  describer (Ctrl+Alt+Shift+Y) says *"Line chart of closes."* or *"Area chart of closes."* where
  the Heikin-Ashi sentence would sit on a HA chart.
- **Speaking the change, one Interface sentence, interrupt:** *"Line chart."*, *"Area chart."*,
  *"Candle chart."*, *"Heikin Ashi chart."*; a request for the type already on screen says
  *"Already a line chart."*, *"Already an area chart."*, *"Already a candle chart."* or
  *"Already a Heikin Ashi chart."*. Turning Heikin-Ashi on from Line or Area says the existing
  HA-on sentence (including the formation caveat when it applies) followed by *"Candle chart."*,
  because two things changed. Turning HA off says *"Candle chart."*
- **Analytics / single-line charts:** the select is gated exactly like the Heikin-Ashi button
  (trading tabs only), and a Go bar or command request on a `SingleValueLine` chart says
  *"This chart is already a single line; chart types are for price bars."* and changes
  nothing. `ToggleHeikinAshi` refuses there the same way.
- **State and persistence:** `WorkspaceState.ChartStyle` and `TabSnapshot.ChartStyle` are init
  properties (default candles — the positional records are built in too many places to widen),
  `TabConfiguration.ChartStyle` defaults to candles in the file, and save/restore now covers the
  active tab, every snapshot, the workspace library and `WorkspaceInitializer`. The reducer's
  `SetChartStyleAction(style, heikinAshi)` enforces the invariants: Line/Area always clear the
  HA flag, HA only exists with Candles, and `ToggleHeikinAshiAction` ON also returns to Candles.
  A new tab starts as Candles. The script-sandbox projection deliberately does not carry the
  field: it is presentation, like pane ranges.
- **Go bar alias moved.** `candles` belonged to `ToggleHeikinAshi`; it now belongs to the
  **Candle chart** entry, so typing "candles" gives candles instead of turning Heikin-Ashi on.
  The HA entry keeps `heikin ashi` and `heikin`, and its own chart-type entry is reachable as
  "heikin ashi chart". The `LINE` code needed no fallback: the 6b own-words-first guard stayed
  green.
- **One handoff claim was wrong against the code, so the implementation differs.** The
  `Gradient` display type does not draw a line with a fade to the pane bottom: `RenderLine`
  fills a flat alpha-60 polygon to the **zero baseline** (clamped to the pane floor for a price
  series). Area therefore reuses that polygon-to-floor shape but gives it the fade the handoff
  describes (line colour at 110 alpha at the top, transparent at the floor). `RenderPriceLine`
  is the one place that draws the new line and fill; there is no second RenderCandles path.
- **Caveats.** Renko, range bars, line break, Kagi and Point & Figure are NOT built: this run
  is Line and Area only, and the enum has room for them. The new token and select have not been
  heard or seen by Cody. The example sentences in the handoff used thousands separators
  ("95,000.5", "1,120"); `SpeechPriceFormatter.FormatPrice` is the formatter they asked for and
  does not group, so the shipped sentence reads "95000.50 ... 1120.00".
- **Tests:** the specified filter runs **3,640 tests, all passing** (up from 3,333). New
  coverage: reducer invariants (Line clears HA, HA on from Area gives Candles+HA, HA off keeps
  Candles, per-tab styles, new tab = Candles); workspace round-trip for Line and Area plus a
  literal old-shape JSON that loads as Candles and a restore that dispatches the saved type;
  pixel tests for the blue line, no body on Line, tinted floor under Area, Price-series white
  hidden when it maps close and present when it maps high, and the closes-only autoscale with
  the candles high/low control; the four speech sentences plus candles/HA anatomy controls;
  status line C-only and candles O H L C control; both describer sentences; a toolbar bUnit
  suite for the labelled select, current value, dispatch and analytics gating; Go bar reachability
  by name/code/alias, the moved `candles` alias, `Already …` sentences and the analytics refusal.
- **Sabotages** (backed up in `.sab/`, built, filter run, restored with `cp`/`touch`/`cmp`;
  red counts are failures in the 3,637-test filter build used for the sabotage runs):
  (a) draw candle bodies on Line too — **1 red**;
  (b) keep drawing the white Price series on Line — **1 red**;
  (c) autoscale Line from highs/lows — **1 red**;
  (d) default a missing saved field to Line — **2 red**;
  (e) let SetChartStyle Line leave HA on — **2 red**;
  (f) speak the candle shape on Line — **4 red**;
  (g) refuse analytics chart-type requests silently — **6 red**.
  Sabotage (a) first exposed a hole in the tests (they called `RenderPriceLine` directly and
  never exercised the Layer's dispatch); the new `The_layer_draws_the_line_chart_type_as_a_line_and_not_as_candles`
  test is the one that now goes red.
- **Review (Claude).** Re-ran the filter: 3,640 passing. Claude's own sabotages, each restored
  with `cp`, `touch`ed and compared: the tab snapshot drops the style **1 red**; the workspace
  library save drops it **1 red**; restoring a saved workspace ignores it **1 red**; Heikin-Ashi
  on from Line keeps Line **1 red**; an analytics chart's status line takes the tab's style
  **1 red**; the spoken change measured from the bar's open instead of the previous close
  **3 red**. One sabotage stayed green: the store no longer refits the price axis when the
  chart type changes (the tests called the range calculator directly), so choosing Line on a
  paused or closed market would leave the line floating in a pane sized for highs and lows
  until the next tick. Added `Choosing_line_in_the_store_refits_the_axis_to_the_closes_at_once`;
  the same sabotage is now **1 red**, and the filter is 3,641 passing. The toolbar select's
  markup was reviewed by hand (native `<select>`, visually hidden `<label for>`, same name as
  its `aria-label`, never disabled); the accessibility-lead agent is not registered in this
  setup, so it was not run.

### Go bar (stage 6c), 2026-11-18 — not yet heard by Cody

- **Settings by name.** The Settings dialog's 63-row search registry moved to Core
  (`SettingsRegistry`, same rows, same order, same text) so the Go bar and the dialog answer
  from one list. The seven host-gated rows use one Core rule, `SettingsRegistry.IsAvailable`,
  fed by a `SettingsHost` built from `DemoPolicy`, `IRuntimePlatform` and
  `IBrowserSpeechOutput`; `SettingsModal`'s `IsRendered` is now that one call and its own search
  box behaves exactly as before. Typing a setting's label, a prefix, the start of a word or a
  near miss offers it; a keyword matches only as an exact whole word and ranks below every
  label match. Only rows this host renders are offered, so the browser host never sends anyone
  to the Braille control. Plain text lists settings **after** every command.
  `SET <text>` offers settings only and says *"No setting matches <text>."* when none does;
  `SET` alone is still the 6a command that opens Settings. A setting option is spoken
  *"<Label>, setting, <Tab> tab."* and choosing it publishes `OpenSettingsEvent(Tab, ControlId)`.
- **`OpenSettingsEvent` can name a control.** When the control is available the dialog opens on
  its tab and focuses that control; when it is not, it opens the tab and says
  *"<Label> is not in this version."* An unknown control id says *"That setting is not in this
  version."* Nothing silently drops focus on the body.
- **`HELP <code>` reads, it never runs.** Each option is the whole sentence:
  *"<Name>. Codes GP, SYM. Key Control plus K. <help line>."* — codes omitted when there are
  none, the key omitted when the command has no live binding, and both taken from the same
  registry and shortcut profile the rest of the bar uses. A setting says *"<Label>, a setting on
  the <Tab> tab. Type SET <Label> to go there."* Enter on a HELP option speaks it again and
  **keeps the box open with the text as typed**: no dispatcher call, no `OpenHelpEvent`, no
  money request. `HELP BUY` explains the typed buy command and opens nothing. No match says
  *"No command or setting matches <text>."*; `HELP` alone is still F1's 6a command.
- **Typed money: `BUY`, `SELL`, `FLAT`.** Three new `SystemCommand` values (`TypedBuy`,
  `TypedSell`, `TypedFlat`) are `Money` entries owned by the Go bar (`HandledByGoBar`), with
  codes `BUY`, `SELL`, `FLAT`, no aliases and no key. The verb must be the **first word** and
  spelled exactly, any letter case. `BUY`/`SELL` alone change only the side; `BUY 0.5` sets
  side, quantity (Base) and Market; `BUY 0.5 95000` or `BUY 0.5 at 95000` sets side, quantity
  and a Limit price; `FLAT` takes nothing and uses the chart's symbol. Quantities and prices
  are positive finite invariant decimals: `1,5`, `1e3`, `-1`, `0`, `abc` and `half` are refused
  with *"BUY takes a quantity and an optional limit price, like BUY 0.5 or BUY 0.5 95000.
  Nothing was opened."* (SELL uses its own word); FLAT with anything after it says *"FLAT takes
  nothing; it uses the chart's symbol. Nothing was opened."* A money word typed last is the
  deliberate exception to either order: *"Money commands start with the word, like BUY 0.5.
  Nothing was opened."* A near miss or prefix (`BYU 1`, `BU`, `SEL 2`, `FLA`) never produces a
  money option.
- **Money only fills in; the dashboard applies and speaks.** Choosing a money option closes the
  box and publishes `OpenTradingDashboardEvent`; the dashboard sets the side, quantity (Base),
  type and limit price, then says one sentence on the OrderEvent channel: *"Buy 0.5 BTCUSDT at
  market, filled in. Nothing was sent. Paper account."* (or *"at limit 95000"*; a live account
  ends *"Live account, Submit reads it back first."*). A stale stop loss, take profit or
  reduce-only is named: *"A stop loss at 90000 is still on the ticket."* A venue that cannot
  trade says *"<Provider> does not support order execution, so the ticket was left alone.
  Nothing was sent."*; no chart says there is nothing to fill in. Submit remains the only send:
  on a live account it still arms the existing read-back review, and nothing in the typed path
  touches `ArmLiveReview`.
- **FLAT finds the position, never closes it.** It opens the Positions tab after the account
  read, matches rows whose `Position.Symbol` is the chart's symbol and whose quantity is not
  zero, and focuses the first row's first cell (a new stable id and `tabindex="-1"`). No row:
  *"No open position in BTCUSDT. Nothing to close."* One: *"Long 0.5 BTCUSDT on Kraken. Tab
  reaches Close position. Nothing was sent."* Several: *"2 positions in BTCUSDT; the first is
  focused."* in front of that sentence. The close buttons are not touched.
- **Focus safety, and why it is a rule.** The Go bar acts on Enter's keydown, and a held Enter
  repeats. If a typed command left focus on a button that sends on Enter, a repeat would place a
  paper order, arm and then Confirm a live one, or close a live position at market with no
  review. So BUY/SELL always leaves focus on the size field (`order-qty` in Base, `order-value`
  in Value) and FLAT on the position row's first cell. Focus never lands on Submit, Confirm,
  Close position or Close at limit from a Go bar request; a bUnit sweep asserts every money
  request's focus ids exclude all of them.
- **Caveats and differences found while building.** The handoff's `SET speech rate` example has
  no destination in this build: there is no speech-rate control, and the 63-row registry
  contains no such label, so it correctly answers *"No setting matches speech rate."* The money
  verb is the first word only (deliberate, documented). `FLAT` takes no symbol. No second opener
  was built: Cody decided on 2026-11-18 that Alt+G is the only one (backtick and `Shift+\`
  dropped; `Shift+\` is not one key on AltGr layouts). The box has **not been heard by
  Cody**.
- **Tests:** the specified filter runs **3,333 tests, all passing**. New coverage: money parser
  cases for every table row and every refusal sentence; case-insensitive exact verbs and the
  near-miss/prefix list through `GoBarService.Suggest`; a guard that `BUY`, `SELL` and `FLAT`
  are no other entry's code, alias, name word, argument verb or settings label (proved red by
  giving *Open Help* the alias `sell` — **1 red**); settings label/keyword/availability/order
  tests; bUnit `OpenSettingsEvent` focus and unavailable-control speech; HELP sentence shapes
  and the box-stays-open behaviour; and dashboard money tests for market, limit, side-only, a
  stale stop, demo-paper, a venue that cannot trade, FLAT none/one/two and the focus sweep.
  Two 6b assertions were deliberately broadened because stage 6c now lists a matching setting
  after the command: `GoBarServiceTests`' "pulse" single-match test selects the command and its
  sentence is checked with `StartsWith`, and the live-key sentence test does the same. The old
  single-option claim is no longer true, and hiding the setting would have broken settings by
  name.
- **Sabotages** (each restored with `cp`, `touch`ed and compared; red counts are failures in the
  targeted tests): (a) let a near miss produce a typed BUY option — **4 red**; (b) make BUY
  focus `order-confirm-live` — **8 red**; (c) make the dashboard call `SubmitOrder` after
  applying a BUY — **7 red**; (d) delete the Text size row from the Core registry — **2 red**;
  (e) offer unavailable settings in the Go bar — **1 red**; (f) make Enter on a HELP option run
  it — **3 red**; (g) accept `1,5` as a quantity — **1 red**; (h) make FLAT with no position say
  nothing — **1 red**.
- **Review (Claude).** Re-ran the filter: 3,333 passing. Claude's own sabotages, each restored
  and compared: HELP option scheduled instead of read — **3 red**; settings put above commands in
  a plain search — **7 red**; paper trading offered where the host hides it — **1 red**; focus
  sent on to `order-confirm-live` after the size field — **8 red**. One gap: dropping the
  `LimitPrice == null` part of the dashboard's "is this a typed request" test (so the chart
  menu's *limit order here* would say "filled in" and pull focus to the size field) left
  everything **green**. New test `TheChartMenusSideAndPriceStillFillSilently` pins that the
  menu's side-and-price request still fills silently and leaves focus on the dialog title;
  the same sabotage is now **1 red**. The Go bar's settings, HELP and money paths add no new
  service registrations (the two new optional dependencies are already registered on both
  heads). Not heard by Cody.

### Go bar (stage 6b), 2026-11-18 — not yet heard by Cody

- **A typed command can carry one value, and the words go in either order.** `TF 4h` =
  `4h TF`, `GP BTC` = `BTC GP`, `PROV Kraken` = `Kraken PROV`, `IND rsi` = `rsi IND`,
  `ALERT 95000` = `95000 ALERT`, `GOTO 2024-03-01` = `2024-03-01 GOTO`, `FIND hammer` =
  `hammer FIND`. `go to` is matched as a two-word phrase at either end. Bare timeframes (`4h`,
  `15m`, `1d`, `1w`, `1M`, `4 hours`, `15 min`) and ISO dates (`2024-03-01`,
  `2024-03-01 14:00`) are offered after any command match, because they cannot mean anything
  else. Nothing else is guessed without its verb.
- **The four 6a silent drops are now commands.** `ChangeTimeframe`, `ChangeSymbol`,
  `ChangeProvider` and `RefreshData` had registry entries, no key and no handler: typing `TF` and
  Enter closed the box and reached nothing. `TF`/`SYM`(`GP`)/`PROV` now change the chart on
  screen or choose a provider, and `REF` reloads it through the same one load path. A bare verb
  keeps the box open, completes the text and says what to type next — *"Type a timeframe after
  TF, like TF 4h."*, *"Type a symbol after GP, like GP BTC."*, *"Type a provider after PROV, like
  PROV Kraken."* — instead of closing. `GP` is a new code on `ChangeSymbol`.
- **The either-command guard.** `EveryGoBarCommandHasAHandler` scans every `.cs`/`.razor` under
  Core, the components, the WebHost and the Blazor client (asserting each folder exists and more
  than 100 files were scanned) for `SystemCommand.<Name>` on every non-`KeyOnly` entry, except
  the entries the registry marks `HandledByGoBar`. The Go bar's own folder is excluded from the
  scan: a mention in the verb table or the service is not a handler in a head, and counting it
  made removing the flags stay green. Proved red before the flags existed: **1 failed test,
  naming ChangeProvider, ChangeSymbol, ChangeTimeframe and RefreshData**.
- **One load path for the chart on screen.** `IMarketOrchestrator.LoadOnActiveTabAsync` starts
  from the active tab's identity, applies only the parts given, refreshes the provider's symbol
  list only when the provider changes, reads every value back (the public demo refuses silently
  in the setters), and then loads. `TF` therefore changes the chart in front of the user, not a
  toolbar dropdown they never pressed Load on. The shape-change pre-flight is the toolbar's: a
  symbol load that would turn an OHLCV tab into a single-value analytics tab and strip user
  series is refused in words — *"That is a different kind of chart and would remove your 3
  indicators. Load it from the toolbar to open it in a new tab."* The toolbar's timeframe
  composer now re-parses `SelectedTimeframe` on `PipelineUpdated`; before this, `TF 4h` left it
  showing `1` / `hr` and the next edit wrote a 1h timeframe back over the chart.
- **Symbols are never guessed quietly.** The search is the current venue, then every watchlist,
  then other venues whose symbol lists are already in memory, de-duplicated by (provider,
  symbol). Comparison ignores case and removes `/`, `-`, `_` and spaces. One match is one
  option ("Open MEXC BTCUSDT"); several are one option each, capped at 20, and the sentence
  names the first five: *"BTC, 6 matches: MEXC BTCUSDT, MEXC BTCUSDC, Kraken XBTUSD, …, and 1
  more."* Enter without moving onto one runs nothing and says *"BTC, 3 matches. Arrow down to
  pick one."*; an exact full-symbol match on the current venue is the only option and runs. A
  watchlist match loads with its provider, market and sub-type; the same venue keeps the tab's
  timeframe. **No other venue is searched in 6b:** only the current provider's symbol list is in
  memory (the data service's symbol cache is an asynchronous disk cache, not a list the bar can
  read while typing), so the no-match sentence names where it looked — *"No BTC on MEXC or your
  watchlists."*
- **`IND` shares the dialog's list.** `IndicatorCatalog` in Core now owns the demo and retired
  filters, the metadata defaults, the pane words and the name/code/description search that the
  Add Indicator dialog uses, so a typed `IND rsi` adds exactly what the dialog would with exactly
  its defaults and says *"RSI added, its own pane."* An analytics tab is refused with *"This is
  an analytics chart; indicators need a price chart."* (There was no existing indicator-button
  refusal to copy; this sentence parallels the chart menu's order refusal.)
- **`ALERT` opens the form; it does not create the alert.** *"Price alert at 95,000"* closes the
  box and publishes `OpenAlertsEvent(AlertTarget.Price, Value)`, pre-filling the form. The user
  reads it and presses Add. Prices accept `95000`, `95,000`, `95000.5`, `95k`, `1.2m`; zero and
  negative are refused in words.
- **`GOTO` a date uses the app's own clock.** A typed date is in the zone the bar readout uses
  (F12's "times in UTC" setting, otherwise local) — never the machine's zone in tests, where the
  zone is passed in. It moves to the last bar at or before the moment and the bar read says
  where; before the first bar it says *"1 March 2019 is before this chart's first bar, 3 January
  2023."* and after the latest *"That is after the latest bar."*; neither moves. Slash dates are
  refused: *"Write the date as 2024-03-01 or 1 March 2024."* `GOTO CPI` finds the latest placed
  event at or before the newest bar; events off says *"Market events are off. Turn them on in
  Settings to use GOTO with an event."*, none found says *"No CPI on this chart."*, and several
  different names become options.
- **`FIND` scans only on run.** The candle route calls `CandlePatternSpeech.AnalyzeAt` with the
  same DI analyzer instance and the same Heikin-Ashi flag the arrow keys use, so a bar can never
  be named one way by FIND and another by the arrows; the formation route uses
  `ChartPatternNavigator`/`ChartPatternNarrator.Name` and keeps the *"Chart formation description
  is off…"* refusal. It lands on the first match right of the cursor and lets the bar read say
  it; none after says *"No Hammer after this bar; 4 before it."* and does not move.
- **The box** now renders the options `GoBarService.Suggest` returned and reads that same
  object's sentence, so the two can no longer disagree once arguments exist. It tracks whether
  the user moved with the arrows (typing resets it), which is what makes the several-symbols rule
  honest, and Tab completes an argument option to its canonical text (`TF 4h`, `GP BTCUSDT`,
  `IND RSI`). Every argument command still waits for the box to close before acting.
- **Caveats.** Other venues are not searched (no in-memory cross-venue symbol list; reported
  above). `ALERT` opens the form rather than creating an alert. Stage 6c — settings by name,
  `HELP <code>`, typed `BUY`/`SELL`/`FLAT` and the backtick question — is not built. **Not heard
  by Cody.**
- **Tests:** 69 parser cases (`GoBarParserTests`: both orders for every verb, the `go to` phrase
  at either end, both ends verbs, bare timeframes and ISO dates, spelled units, `1m`/`1M`, the
  999 cap, prices, each date form, the slash refusal, `tr-TR`, and a money code followed by
  text), 44 `GoBarArgumentTests` sentence-by-sentence with a recording store and a controllable
  orchestrator, the new registry guards (GP→ChangeSymbol, no verb belongs to another entry, no
  verb is a money code, and the handler guard), one box script and one toolbar-composer bUnit
  test. The specified filter runs **2,197 tests, all passing**.
- **Sabotages** (backed up in `.sab/`, restored with `cp`, `touch`ed and compared; each count is
  failures in the specified filter): (a) delete `ChangeTimeframe`'s `HandledByGoBar` flag —
  **1 red**, the handler guard; (b) let Enter run the first of several symbol matches — **1
  red**; (c) make the load path prefer the toolbar's stray selection — **1 red**; (d) make FIND
  use a fresh analyzer and the wrong Heikin-Ashi flag — **2 red**; (e) make GOTO move when the
  date is before the first bar — **1 red**; (f) remove the toolbar composer's re-parse — **1
  red**; (g) act on an argument option before the box closes — **4 red** (including two 6a
  sequencing tests).
- **Review fix (Claude): a verb word inside a command's own name no longer hides the command.**
  Before the fix, 44 of the registry's own names, codes and aliases put something else first or
  nothing at all: *add tab* searched indicators for "tab", *Change the timeframe* said "Change the
  is not a timeframe", *focus the chart* searched symbols, *alert manager* asked for a price.
  `Suggest` now always keeps the 6a matches. A command typed by its exact code, name or alias
  leads and the value options follow; otherwise the value options lead and the commands follow;
  a refusal is the answer only when no command matches either. Guard:
  `EveryCommandsOwnWordsStillPutThatCommandFirst` runs every name, code and alias (money aliases
  left out, as the matcher refuses them) against resolvers given symbols and indicators that DO
  match the leftover words. Removing the merge turns it and three ordering tests red (4); always
  putting the values first turns it red on *add indicator* (2).
- **Review fix (Claude): FIND stops on a shape inside a multi-bar pattern.** The bar readout names
  the pattern first, so *FIND doji* walked past the doji that ends a morning doji star and left it
  out of the "before it" count. A bar now answers to its pattern's name and its own shape's name.
  Removing the shape half turns `FindCandleStopsOnAShapeInsideAMultiBarPattern` red.
- **Review guard (Claude): the web head builds the Go bar with all ten new dependencies.** They
  are optional on the constructor, so a container that lost one would build the bar without it
  and IND, GP, GOTO or FIND would refuse everything in silence.
  `TheGoBarIsBuiltWithEveryArgumentDependency` resolves it from the real WebHost container;
  replacing the registration with a four-argument factory turns it red, naming the nine missing.
  The desktop head registers the same ten types (checked by hand, not by a test: the desktop
  container needs MAUI). The review's other note, that the handler guard skipped a MAUI head
  folder, was wrong: `AccessibleTrader/` holds only an untracked data cache, and the MAUI head
  is `AccessibleTrader.BlazorClient`, which the guard already scans.

### Go bar (stage 6a), 2026-11-18

- **Alt+G opens one text box that reaches every action in the terminal by typing.** Type a
  name, a plain alias or a short Bloomberg-style code, then Enter. The registry in Core carries
  one entry for every `SystemCommand` except `None` — 140 entries, in the enum's own order, with
  the enum's section comments — so the desktop and the browser get the same Go bar. A guard test
  fails if any command is missing; removing one entry turns it red (proved below).
- **The matching order is fixed:** exact code, then exact name or alias, then a prefix of a code,
  alias or name, then the start of a word inside a name or alias, then a near-miss spelling
  (Damerau–Levenshtein ≤ 1 for 4–7 characters, ≤ 2 for 8+, nothing under 4). Every tier is sorted
  by registry order; an entry appears once, at its best tier; the list is capped at 20. Matching
  is case- and culture-insensitive (`ToUpperInvariant`, so Turkish's dotted i cannot break it).
- **Money is never fuzzy.** The eight `Quick*` commands are `Money`, and the matcher has one
  guard that returns them only for an exact code or an exact full name — never a prefix, alias,
  word start or typo. New commands cannot opt out of that guard by accident.
- **Keys that are only keys.** The arrows, Home, End, Page Up/Down, `CloseModal`,
  `CancelDrawing`, `ConfirmCoordinateEntry`, the drawing context menu, the pane/component walks
  and the anchor nudges are `KeyOnly`: named, matched and refused with
  *"Move one bar back is a key, Left Arrow; it does nothing from the Go bar."*
- **The box** is an ARIA 1.2 combobox: focus goes to the input on open; Down/Up move the active
  option (DOM focus stays in the input); Tab completes to its first code; Enter runs the active
  or top suggestion, and with nothing matching says *"Nothing matches …"* and stays open; Escape
  closes and focus returns to the chart. No key string is stored in the registry: the key shown
  in the list and read in the sentence comes from the live shortcut profile, so a rebind is
  reflected with no second table.
- **The polite read** is the top suggestion, 500 ms after the last keystroke, on the Interface
  tier with `Interrupt: false`, so F2 and Shift+F2 never mute what the user asked for. A new
  keystroke cancels a pending read; moving through the list does not trigger it.
- **Closed, then dispatched.** `CommandDispatcher.DispatchFromGoBar` runs the same dispatch path
  as the keyboard but skips ONLY the chart-focus gate: naming the command is the intent that
  gate exists to infer, and the browser's focus event arrives after the box closes. It requests
  chart focus first, so the next key press lands on the chart. The modal trap still applies; the
  service dispatches only after the one `ModalStack` reports empty, and if another dialog is
  underneath it says *"Market pulse can't run while Settings is open."* The keyboard path still
  drops a chart command when the chart has no focus (a control test proves no behaviour change).
- **Caveats.** Stage 6a reaches commands that take no argument. Typed arguments (`TF 4h`,
  `IND rsi`, `ALERT 95000`, `GOTO 2024-03-01`, `FIND hammer`, symbols, `BTC GP` / `GP BTC`),
  settings by name, `HELP <code>` and the typed money commands `BUY` / `SELL` / `FLAT` are stages
  6b and 6c in `docs/TODO.md` and are not built. The backtick as a second opener is a 6c
  question, not a decision. The box has not been heard by Cody.
- **Tests:** 43 new tests across `GoBarRegistryTests`, `GoBarMatcherTests`, `GoBarServiceTests`,
  `CommandDispatcherGoBarTests`, `GoBarCommandTests` and `GoBarModalTests`, plus the Alt+G
  binding/scope/parity coverage in the existing shortcut tests. They pin the registry's
  completeness and code rules, each tier and the fixed order, the Turkish case, whitespace, the
  20 cap, the money guard from both directions, the live key after a rebind, the key-only refusal,
  closed-then-dispatched and the dialog-underneath refusal, the dispatcher's two gates, focus on
  the input, Down/Up, Tab, Enter, no-match and the non-interrupting polite read. The specified
  filter runs 1,326 tests: 1,325 pass and the one failure is the pre-existing
  `ProviderCredentialBridgeEnrollmentTests.Bridge_touching_helpers_are_all_known` source scan
  (it names `HostedAccountTestSupport.cs` and `WebHostIntegrationHarness.cs`, neither touched
  here).
- **Sabotages** (backed up in `.sab/`, restored with `cp`, `touch`ed and compared): delete one
  registry entry (**10 red**), let money entries through the prefix tier (**1 red**), swap tiers
  1 and 3 (**2 red**), make `DispatchFromGoBar` skip the modal trap too (**1 red**), dispatch
  before the close (**3 red**), publish the polite read on the Chart channel (**1 red**). Each
  count is the new failures above the standing `ProviderCredentialBridgeEnrollmentTests` failure.
- **Review fix (Claude).**
  - **Typing "market" offered a market order.** The money codes were ordinary words (`MARKET`,
    `LIMIT`, `STOP`, `DISARM`, `ARMED`). An exact code is allowed for money, so *market*, typed
    to find Market pulse or the market watch, put "Place a market order" at the top of the
    list. With a trade armed and a stop set, Enter would have sent it. The codes are now
    `QMARKET`, `QLIMIT`, `QSTOP`, `QDISARM` and `QARMED`. A new guard fails if any money code
    is a word or code another command is known by. Red before the rename: the guard plus five
    ordinary words.
  - **Two silent paths.** A command typed by name now always gets an answer:
    - Placing from the Go bar with nothing armed now says "Nothing armed." The keyboard's
      Enter chords still pass through unanswered, as before.
    - A chart command typed with no chart loaded now says "No chart loaded." Drawing tools
      were among the commands that said nothing.
    - Sabotage: removing the "Nothing armed" answer turns 2 tests red; removing the "No chart
      loaded" answer turns 1 red.
  - **Escape had two close paths.** The box closed itself on Escape, and so did the dispatcher
    (keyboard.js always sends Escape there, even from a text field). Now only the dispatcher
    closes it, like every other dialog. The close request does nothing when the box is already
    closed.
  - **The "standing" failure was not standing.** It came from the account page work merged
    locally on 2026-11-18. Its new hosted test helpers were never registered with the bridge
    guard, and the three plain test classes in their files were never enrolled. CI would have
    gone red on the push. Fixed: the five helpers are now registered and the three classes
    enrolled. The guard and the account tests pass (168).
  - **Caveat, not heard:** while typing, the screen reader reads the active option as it
    changes, and half a second later the polite read says the top suggestion again. It may
    sound doubled. Cody to judge.
  - Review filter: 1,633 tests, all pass. The accessibility agents are not installed here, so
    the combobox markup was reviewed by hand: label, role, expanded, controls, active
    descendant, option ids and selection state are all correct.

### What changed (stage 5b), 2026-11-18

- **Alt+U answers "what happened while I was not listening?"** with a catch-up of the change log.
  The fixed order is money (fills, partial fills, stop hits, take-profit hits), alerts, market
  events released (the read-out and the figure), unusual moves, signals and connection problems.
  Stage 5b records the four categories whose events already exist; `Unusual` and `Signal` stay in
  the enum and in the ordering so stage 5c adds only producers.
- **The words.** The first press says *"7 changes since 09:40, 47 minutes ago."* and then each item
  as the sentence the terminal already spoke, with a time: *"Money: 09:52, BTC/USDT stop hit, sold
  0.1 at 94,210. Alerts: 09:44, BTC/USDT crossed above 95,000. 10:01, ETH/USDT RSI above 70."*
  A category word is said once per press; a stored trailing period is removed so the result never
  says ". .". At most five items are read per press, followed by *"Press Alt+U again for 2 more."*;
  an uninterrupted follow-up starts *"2 more."*. Nothing new says *"Nothing new since 09:40."*;
  an untouched log says *"Nothing recorded yet."*. Every time goes through
  `SpeechTimeFormatter`, so UTC and the 12-hour clock are honoured; numbers are invariant.
- **The caught-up mark moves past spoken items only.** `MarkHeard` sets exactly the keys in the
  sentence and moves `LastCatchUpUtc`; an item the press did not reach stays unheard, so an
  interrupted catch-up keeps the rest.
- **Pure halves.** `ChangeLogRules` appends without duplicate keys, keeps 500 entries, drops
  anything older than seven days without reordering, plans category-then-oldest-first and marks
  heard. `ChangeLogWording` writes the header, category groups, follow-up, nothing sentences and
  the return prompt; `ChangeLogRecorder`'s mapping methods turn each event into one entry using
  `AccessibilityFeedbackCoordinator`'s existing fill/partial/stop/take-profit sentences, so there
  is no second wording to drift.
- **The store.** `ChangeLogStore` reads and writes `change-log.json` beside the user's other JSON
  files, with one semaphore per FULL PATH shared by every instance in the process. Every write is
  a read-modify-write through the rules and then `AtomicFile`; a file that does not parse is moved
  aside by `CorruptFileQuarantine` and a fresh log starts. Failures are logged by exception type
  and never thrown to a bus subscriber.
- **The recorders.** `ChangeLogRecorder` subscribes to alerts, the four money events, market-event
  notices and connection status. It records only `ReadOut`/`Figure` notices, never countdowns; it
  records `Error`/`Disconnected` and a `Connected` only after that provider's previous recorded
  state was a problem. `MarketEventNoticeEvent` gained an optional
  `MarketEventNoticeKind Kind = ReadOut` at the end and the live-notice publish site passes the
  real kind. One recorder is force-created per browser circuit, one in `HeadlessSession.Start()`,
  and one at MAUI startup; the same fill seen by two buses is one entry because the key is
  `order|provider|OrderId|Status|FilledQuantity`. `HostedAlertMonitor` appends its fired alerts
  through the same per-user scope's store, because the hosted head has no headless bus.
- **The return prompt.** Settings → Narration → **What changed → Offer a catch-up when I come
  back** (default ON, `changeLogOfferOnReturn`) adds the optional sentence *"You were away 47
  minutes, 3 things changed, Alt+U to hear them."* after at least five minutes away, when
  something unheard was recorded after `LastPresentUtc`. It never reads or marks anything. The
  away mark is set in the circuit handler's connection-down path and on the MAUI window's
  `Stopped`/`Destroying` and tray-hide path; the offer runs in `AppStartupService` (step 9b, once
  the speech pipeline is listening) on both heads, and on MAUI `Resumed`/tray-show.
- **Interface tier.** Both the catch-up and the prompt publish on
  `SpeechChannel.Interface`, so neither F2 nor Shift+F2 mutes them: the user asked.
- **Caveats.** This is the first four categories only: unusual moves, signals/formations on watched
  charts, position P&L since you left, market events released while the browser is closed and the
  morning brief are stage 5c items in `docs/TODO.md`. A market event released while the browser is
  closed is still not recorded at all, because stage 3a's process-wide owner is still missing. The
  hosted head records its server-side alerts, while its in-circuit items come through the circuit
  recorder. The listening test was not run: **not heard by Cody.**
- **Tests:** 37 new tests across `ChangeLogRulesTests`, `ChangeLogWordingTests`,
  `ChangeLogRecorderTests`, `ChangeLogStoreTests` and `ChangeLogCatchUpServiceTests`, plus the
  Alt+U command/scope coverage. They pin duplicate keys, the 500/7-day cap without reordering,
  category-then-oldest planning and the remaining count, mark-heard touching only the spoken keys,
  header singular/plural and both "since" sources, one category word per press, press-again and
  follow-up, nothing new/recorded, the return prompt, no ". .", `de-DE` invariance, the 12-hour UTC
  clock, all seven event mappings, countdown exclusion, first-connect exclusion and
  error-then-reconnect, one fill on two buses, a throwing store, store round-trip and 2 × 200
  concurrent appends, corrupt-file quarantine, the settings file being read on the first offer of a session, and the two-press catch-up and prompt rules. The
  specified filter (`ChangeLog|MarketPulse|MarketEvent|Shortcut|CommandDispatcher|WebHost|HostParity|Settings|Phase5KeyboardScope|Headless|Order|Alert|TestTemp`)
  runs 2463 tests, all green. One pre-existing failure outside this stage
  (`AccountManageProfileTests` called `Path.GetTempPath` directly, which the TestTemp guard
  forbids) was fixed so that guard suite is green.
- **Review fix (Claude).** Three things the first build got wrong. (1) The desktop recorder was a
  singleton nothing resolved, so on the desktop app the log stayed empty; `AppStartupService` now
  resolves it (step 4d). (2) The return prompt was published at circuit open and on the MAUI
  window's `Created`, before anything was listening for speech, so it was said to no one; it now
  runs in `AppStartupService` after the feedback coordinator exists (step 9b). (3) After a press
  that left items unread, a new item made the next header say *"since"* the press time while
  reading an older leftover; "since" is now the earlier of the last catch-up and the oldest unheard
  item. Three new tests (`ChangeLogStartupTests` ×2, a wording test); one existing catch-up test
  pinned the old "since" and was updated. Sabotages: recorder not resolved (**1 red**), offer moved
  before the speech pipeline (**1 red**), "since" back to the catch-up time (**2 red**). Further
  caveats: the alert de-duplication key is the fire time to the minute, so if the circuit and a
  background monitor fire the same alert on either side of a minute boundary it is logged twice;
  a provider whose circuit keeps tripping logs one connection item per minute it trips.
- **Sabotages:** record countdowns too (**1 red**), drop the de-duplication check (**2 red**), mark
  every unheard entry heard (**4 red**), sort newest-first within a category (**1 red**), publish on
  the Chart channel (**2 red**), remove the per-path lock (**1 red**, five consecutive runs). Each
  was backed up in `.sab/`, restored with `cp`, `touch`ed and compared byte-for-byte.

### Market pulse (stage 5a), 2026-11-18

- **Alt+P answers "how is the market right now?" in one sentence.** The list is an ordinary
  watchlist named **Pulse**, seeded on the first press with nine items in a fixed, never-re-sorted
  order: S&P, Nasdaq, Bitcoin, Ethereum, ten-year (US 10-year yield), dollar index, gold, oil and
  VIX. It is edited in the existing watchlist screen (Alt+M). Held positions are appended after
  the list for that reading only; they are never written into the saved list. If an item found no
  usable data source when the list was created, the seeding press adds *"Left out, no data
  source: … Add them in the watchlist screen, Alt+M."*
- **The words.** A change is one decimal with no "percent" and no plus sign: *"Bitcoin down 1.2"*;
  a value that rounds to 0.0 is *"flat"*. A yield reads its level and changes in basis points:
  *"ten-year 4.31, up 3 basis points"*, with the singular *"1 basis point"*. A quote more than two
  minutes old says its age (*"4 minutes old"*), a closed market names its session
  (*"VIX closed, up 2.1 Friday"*, or *today*/*yesterday*), and a failed or late fetch says
  *"unavailable"* without losing the rest of the sentence.
- **Pure halves.** `MarketPulseDefaults` holds the nine-item candidate table, the provider
  preference (Coinbase, Kraken, Gemini, Bitstamp for crypto; Fmp, TwelveData, Oanda, Finnhub,
  Polygon, InteractiveBrokers otherwise; any other configured provider last, never Binance) and
  the resolver that stores the provider's own symbol spelling. `MarketPulseMath` turns daily and
  one-minute bars into a quote: today's open for a crypto midnight reference, a one-hour bar for
  the last-24-hours setting, the previous session's close otherwise, a stale one-minute bar as
  the holiday test, the 09:30–16:00 Eastern equity hours and the
  Friday-to-Sunday forex/commodity window through the shared exchange-time resolver, and the CBOE
  TNX ten-times adjustment. `MarketPulseWording` builds the sentence.
- **The live half.** `MarketPulseService` (singleton on MAUI, scoped on the WebHost) fetches every
  item at once under one four-second `CancellationTokenSource`, with at most four requests in
  flight per provider. An item that misses the deadline or throws is unavailable, and nothing is
  published after the sentence. If the sentence is not ready one second after the press, it says
  *"Gathering the pulse."*; a second press while gathering says *"Still gathering the pulse."* and
  starts nothing. Everything is published on the Interface speech tier, which neither F2 nor
  Shift+F2 mutes. Failures are logged by exception type only; symbol lists and keys never are.
- **The setting.** Settings → Narration → **Pulse: crypto change measured from** chooses
  **Midnight UTC** (default) or **Last 24 hours**. Stored as `marketPulseCryptoStart`
  (`midnightUtc` / `last24h`), mirrored through `WorkspaceState` and the preference file; an
  unknown value reads as midnight.
- **Caveats.** The whole sentence is limited to four seconds, so a slow provider becomes
  "unavailable" rather than delaying or improving the answer. The US-hours closed rule has no
  holiday calendar of its own: it relies on the stale-minute-bar test, so a US holiday that still
  publishes a fresh-looking bar is not detected. The holdings read has its own two-second limit
  before the four-second fetch, so the slowest possible press is about six seconds. Index CFDs that trade
  nearly 24 hours are still treated as closed outside 09:30–16:00 Eastern. Symbol candidates are
  a fixed table and were never tried against live providers in this build. The listening test was
  not run: **not heard by Cody.**
- **Tests:** 45 new tests across `MarketPulseDefaultsTests`, `MarketPulseMathTests`,
  `MarketPulseWordingTests` and `MarketPulseServiceTests`. Wording pins every example sentence,
  singulars, flat at ±0.04 and invariance under `de-DE`; math pins both crypto reference modes,
  the previous close, the Saturday and holiday closed rules, freshness at 90 seconds and 4
  minutes, TNX at 43.1, the Sunday forex open and both DST seasons; defaults pin the provider's
  own spelling, usable-provider filtering, Binance exclusion, preference order and left-out
  reporting; the service pins one-time seeding, a user-edited list, the four-second deadline and
  its dropped late answers, the one-second gather notice, a second press, the Interface channel,
  held-position append-without-saving and the seeding-only left-out sentence. The filter
  (`MarketPulse|MarketEvent|Shortcut|CommandDispatcher|Watchlist|WebHost|HostParity|Settings`)
  runs 1167 tests, all green.
- **Sabotages:** Chart channel instead of Interface (**1 red**), crypto reference changed to the
  previous daily close (**2 red**), TNX ÷10 dropped (**1 red**), Binance allowed to be picked
  (**1 red**), per-provider in-flight limit raised from four to one hundred (**1 red**). Each was
  restored and compared byte-for-byte.
- **Review fix (Claude): daily bars that are not stamped 00:00 UTC, late daily bars, a slow
  broker and missing minute bars.** "Today's bar" was judged by its UTC date, so Oanda's forex
  bar (stamped 17:00 New York the evening before) read as yesterday's all day and the change came
  out near flat, and a crypto bar stamped midnight New York measured from the wrong open between
  midnight UTC and 05:00. It is now "the bar in progress": opened at or before now and less than a
  day ago. A closed market's session is the New York date half a day into its bar, so Oanda's
  Friday bar no longer reads "Thursday". During US hours a missing daily bar no longer closes the
  market when fresh minute bars exist (some providers publish the day's bar only after the
  close); the change is then measured from the newest daily close. A failed one-minute fetch
  leaves the item on its daily bars instead of making it "unavailable", and the holdings read is
  held to two seconds and skipped if it misses, instead of holding up the whole sentence. Six
  tests; each fix was sabotaged back and went red (in-progress by UTC date **2 red**, session by
  UTC date **1 red**, missing daily bar closes **1 red**, minute failure fails the item **1 red**,
  unbounded holdings **1 red**), restored and compared byte-for-byte.

### Market events stage 4a: expected figures from Kalshi, 2026-11-18

- **An expected figure for the five releases that have Kalshi markets:** CPI, core CPI,
  payrolls, unemployment and GDP. `MarketEventFigure` gained an optional `Expected`; the
  calendar merges it in from `MarketEventKalshiFeed` as a copy, and the seed is never mutated.
  Says its source: *"expected 0.3, by Kalshi"*. The terminal never invents a consensus: no
  Kalshi market means no expected figure.
- **The surprise word, when an actual and an expected both exist:** hotter/cooler for CPI and
  core CPI, stronger/weaker for payrolls and GDP, weaker/stronger (reversed) for unemployment,
  and *"as expected"* when both round to the same 0.1 (or 1 thousand) step. It is added to the
  bar clause, each Alt+Shift+D figure line, the release read-out, the Alt+E event line and its
  release history, and counts down as *"CPI, expected -0.1, in 15 minutes."*
- **`KalshiExpectedFigures`** is the pure half: the event-ticker rule, the one-day close guard,
  the candle and market-list quote rule, and the median that finds where the ladder crosses
  one half and rounds up onto the release's own grid.
- **`MarketEventKalshiFeed`** is the live half, keyless, one per process, cached atomically
  under `market-events/kalshi.json`. It reads upcoming markets in the next 14 days at most
  hourly (provisional, in memory and on disk) and backfills past releases from 2021-06-01 at
  most 40 per pass. At most three requests a second; a 429, 5xx, timeout or malformed answer
  stops the pass and is never remembered as "no market". The bar-map key includes its version
  beside FRED's, and Settings and the calendar show its own status line.
- **Caveats.** Only the five figures: Fed decisions still have no expected rate, earnings still
  need Cody's Finnhub key, and token unlocks are blocked on a paid source. History goes only as
  far back as Kalshi's markets (about mid-2021); older bars have no expected figure and say no
  surprise word. Kalshi's traders are not the economists whose consensus the news quotes, so
  the two can differ. An upcoming release's expected figure moves until the release. The live
  fetch was never run against the real Kalshi API from the app in this build (there is no
  network here); the fixtures are real responses saved on 2026-11-18. Not heard by Cody.
- **Tests:** the reference table of eleven saved events (including both series spellings, live
  candles and an upcoming market list) is reproduced end to end; the cache reads back with no
  requests; a 429 is retried; Level Off makes no request; the bar map rebuilds on a version
  change. Sabotages: spread limit dropped (**5 red**), median rounded to nearest (**5 red**),
  429 cached as no market (**1 red**), unemployment direction swapped (**2 red**).
- **Review fix (Claude):** the merge gives an upcoming release an expected-only figure row, and
  three places took "the event has a figures object" to mean "the figure was published" (the
  planner's FRED figure tries, the read-out's "Waiting for the figure" and the FRED key check).
  With Kalshi on, every upcoming CPI and jobs release would have skipped its figure, so the
  surprise would never have been said at the release. Now `MarketEventFigures.Published(e)`
  asks whether any figure has an actual. Guarded by `MarketEventExpectedOnlyTests` and a live
  notices test that hears "CPI 3.1, expected 3.0, hotter."; each of the three lines put back
  turns a test red.
- **Also a caveat:** upcoming release dates come from FRED (the seed ends in July 2026), so
  without a FRED key there are no upcoming releases for Kalshi to read either.

### Leaderboard publisher, 2026-11-18

- **Lifetime trade stats on every paper account** (`PaperTradeStats`, persisted under
  `Stats` in `paper_account.json`): closed trades, wins, realised total, first and last fill.
  Updated on every fill; a break-even close counts as a closed trade and not a win. Old files
  with no `Stats` are seeded by replaying the kept `History` back from the positions the account holds now, and `ResetAccount`
  zeroes them. The starting balance is now `PaperTradingProvider.DefaultStartingBalance`
  (100 000), the only copy.
- **A pure calculator** (`AccessibleTrader.Core/Services/Leaderboard/PaperAccountStanding.cs`):
  equity = cash + positions at their marks + collateral, return percent, realised P&L, trade
  count, win rate, first/last trade, and a stale flag. A position with no usable price is
  valued at its entry price and flagged, never at zero and never by throwing. `PaperAccountFile`
  is a public read model of the account file, parsed with Newtonsoft; malformed JSON is refused.
- **A hosted publisher** (`LeaderboardPublisher`, `--accounts` only): every five minutes it
  reads users with `LeaderboardOptIn` and a `DisplayName` (never `UserName`/`Email`, and it
  refuses a name containing `@`), prices each position from the chart it was traded on, skips
  accounts with no closed trades, ranks by return percent (ties share a rank, then trade count,
  then name), keeps the top 25, and writes `{dataRoot}/public/leaderboard.json` atomically,
  mode 0640 on Unix. The file is written even with no entries so `generatedAt` distinguishes
  "no one yet" from "publisher down". Configurable with `Leaderboard__OutputPath`.
- **Review fix (Claude).** The first seed replayed History forward from a flat account, which is wrong
  whenever the 200-fill cap has dropped the fill that opened a position: the oldest kept close reads
  as an opening, and later fills on that symbol are counted against a phantom position. It now walks
  back from the held positions, which is exact for every kept fill; a test drives an old file through
  both the calculator and the broker's own load, and goes red if either reader loses the positions.
- **Caveats.** For accounts written before this change, the seeded trade count covers only the
  kept 200 fills — older fills were already dropped and cannot be recovered. Prices come from
  the chart each position was traded on, so a venue that is down makes that row stale rather
  than guessing. The website that renders the JSON is not in this repository; only the file is.

### Account page, account email through Brevo, and a minimum fill time on Register, 2026-11-18

- **A real account page at `/account/manage`**, reached from Settings → General → Account
  on the hosted build (and linked from Account security). Separate forms for display name,
  leaderboard opt-in, change password, change email, download my data and delete account.
  Display names are 3–30 characters, letters/digits/spaces/`.`/`-`/`_` only, never an `@`,
  and unique regardless of case (a `COLLATE NOCASE` partial unique index on `AspNetUsers`,
  added by an EF migration after the initial Identity schema; old `auth.db` files gain the
  columns with leaderboard opt-in false). Empty clears the name and turns the opt-in off.
- **Account email through Brevo SMTP.** A new `IAccountEmailSender` and
  `SmtpAccountEmailSender` (STARTTLS on 587, 20-second timeout, plain text) send
  password-reset links, change-email confirmation links to the new address, and
  "your password/email was changed" notices to the old one. Configured by the
  `AccountEmail__*` environment settings; only `AccountEmail__Password` is secret.
  Logs name the recipient DOMAIN, the subject and success/failure — never the password,
  a token or a link carrying one. Every message ends with the support address.
- **ForgotPassword now emails the reset link when the service is configured**, with one
  email per address per five minutes, and keeps the same neutral answer for known,
  unknown and throttled addresses. With no email service it keeps the old support-contact
  flow and owner push notice; `--reset-link` is unchanged. Links use the new
  `Accounts:PublicBaseUrl` (default `https://accessibletrader.com/terminal`).
- **Changing an email needs two steps.** The confirmation link goes to the new address;
  GET shows a **Confirm the new address** button and changes nothing (link scanners), the
  POST changes the address and sign-in name, refreshes a signed-in session, and emails the
  old address. A bad or expired link says so and changes nothing.
- **Download my data** returns `accessible-trader-data-{yyyyMMdd}.zip` with every file in
  the user's own folder plus `account.json` (email, display name, leaderboard opt-in,
  created/last seen, tier, 2FA on/off). It contains no password hash, security stamp,
  authenticator key, recovery codes or tokens, and no other user's files.
- **Delete account** evicts the user's in-memory paper account and per-user caches, deletes
  the Identity row, deletes only a folder proven to be inside `{dataRoot}/users/` and not
  `users/anon`, signs out, and tells the sign-in page "Your account has been deleted."
  Current password, the cannot-be-undone checkbox, and a current authenticator/recovery
  code when 2FA is on are all required.
- **Register has a minimum fill time.** The GET signs the issue time with DataProtection;
  a POST missing it, tampered with, older than two hours or less than three seconds old is
  dropped exactly like the honeypot — a success-looking redirect and no account.
- **Settings → General → Account** on the hosted build offers ordinary links to Your
  account and Sign out; desktop and `--demo` do not render the group.
- **New security events** for display name, leaderboard opt-in, password change, email
  change requested/changed, data download and account deletion, all with the client IP.
- **Review fixes (Claude, after DeepSeek's build).** The reset email is sent without
  waiting for the mail server, so a registered address no longer answers seconds slower
  than an unknown one. A wrong current password on Change password, and a wrong
  authenticator code on Delete, now count toward the 15-minute lockout like the sign-in
  page (before, a stolen session could guess forever there). Delete no longer reaches into
  the background alert monitor's memory from the web request (its own next poll drops a
  deleted user); the folder guard accepts only a direct child of `users/`, never a folder
  nested inside another. Each fix has a test that was watched to fail with the bug put back.
- **Caveats.** The leaderboard itself is not built; this release only stores the display
  name and the opt-in. The reset-email throttle is in memory, so a restart forgets the
  five-minute window. Account email needs Brevo (or any SMTP relay) credentials; without
  them the old admin-mediated reset is used. Registration does not yet require email
  confirmation.

### Colour said the same way everywhere; profiles split into buying and selling, 2026-11-18

- **Histograms and volume bars say their colour**, in the same words and on the same rule as a
  line: *"Green bar."*, *"Faint red bar."* on arriving at the component, then only where the
  colour's name changes, either direction and after a jump. The colour comes from the paint the
  renderer uses — `StandardRenderers.BarColorAt`, shared by the bar renderer and speech — so
  colour rules (MACD's shades), a recoloured histogram and volume's candle-direction pair are all
  what is drawn. Behind the existing **Say what lines and signals look like** opt-in; off, nothing
  changes.
- **The heatmap says its shade**: *"Dark cell."*, *"Medium cell."* or *"Light cell."* from the top,
  middle and bottom third of the alpha `HeatmapRenderer` paints, with the same arrival/change rule
  when moving between prices in a column or between bars. An empty cell says nothing. The alpha is
  one shared function, `HeatmapRenderer.CellAlpha`, so renderer and speech cannot drift. Behind the
  same opt-in.
- **A volume-profile row is split into buying and selling**, as TradingView draws it. `ProfileBin`
  gained `BuyVolume` and `SellVolume`; a bar's volume is buying when it closed above its open and
  selling when below, a doji is compared with the previous bar's close, and an equal close is half
  each. Each row is drawn buying-first from the profile's base side in the theme's bullish colour,
  then selling in the bearish colour, at `ProfileNormal`'s opacity; the POC row keeps its own
  marking. A TPO/zero-volume row draws as before.
- **Speech and sound.** The row's price line ends with *"…, 62 percent buying."*, *"…, 58 percent
  selling."* or *"…, even."*; this is data, not looks, and is always on. Under the look opt-in the
  row's drawn colours follow the line rule (*"… and … row."*), and the POC row says the POC's own
  colour. The profile sound keeps the accepted 2026-11-03 pitch-by-part and grit-by-size, and
  changes only the base waveform: **triangle when buying leads the row**, sine when selling leads,
  the split is even, or there is no split — the oscillator rule applied to buying and selling.
  "Even" (within half a percentage point) is one rule, `ProfileSplit.Leader`, read by both the
  words and the sound, so a row spoken as even never plays the buying triangle (Claude's review
  fix: the first cut played a triangle for 50.3 percent buying). TPO
  and no-volume profiles sound exactly as before.
- **Caveats.** The split is by **bar direction**, not by real trade-by-trade buying and selling;
  that needs lower-timeframe or tick data the terminal does not have, so a bar that opened low,
  traded up and closed high is all "buying" however much selling happened inside it. Where volume
  goes is untouched: `TotalVolume`, the POC, the value area, the levels and the backtests are
  bit-identical on a pinned data set, `BuyVolume + SellVolume == TotalVolume` on every
  volume-profile bin, and TPO/market-profile rows carry no split and are unchanged. The new fields
  cross the script-sandbox projection wire, so an older sandbox build cannot read a newer
  projection (they ship together). The shades are thirds of the alpha range, not absolute volume:
  a cell is "dark" relative to the 0–255 opacity scale the renderer uses, so changing the chart's
  heatmap volume multiplier changes the words as it changes the picture.

### Chart context menu: here, labels and horizontal lines in any pane, 2026-11-18

- **One definition of "here".** `ChartContextHere` resolves the clicked bar and the value under the
  pointer in the clicked pane, or the cursor bar and the focused series' value in its own pane when
  the menu is opened from the keyboard. Every new item uses it; mouse and keyboard origins are both
  unit-tested.
- **New chart-level items, after Jump to latest.** Add label here..., Draw a horizontal line here,
  Add alert at the price, Buy/Sell limit at the price, Copy price, Copy this bar, Start/Stop bar
  replay here, then Add indicator, Object tree and Remove all drawings. Labels and lines are placed
  in the clicked/focused pane at that pane's value. Every item that has an exact existing shortcut
  names its current binding in words; rebinding changes the words.
- **Alerts and orders are prefilled, never fired.** `OpenAlertsEvent` gained optional
  target/series/component/value and `OpenTradingDashboardEvent` gained optional side/limit price.
  The limit items open the dashboard for review; they never call the order service. They are gated
  for demo builds, analytics markets, non-price panes, an empty provider and providers that do not
  execute orders, using the dashboard's own refusal sentence where it has one.
- **Copy actions** use the same clipboard call and failure sentence as the wallet: "Copied." or
  "Could not copy: the browser refused." Copy this bar is one line: the chart's date/time stamp,
  then Open, High, Low, Close and Volume.
- **Remove all drawings** asks in place, removes nothing on Cancel, announces the count, and is one
  `Ctrl+Z` entry via a new batch undo. **Move to a new pane / Move to the price pane** update
  `ChartSeries.Pane`, add the new `PanePinned` flag so a workspace reload does not re-derive the
  provider's default pane, and announce the move.
- **Per-series submenu** adds Add alert on this line, Copy value and the two pane moves after
  Properties. Drawings and provider-fixed panes are refused with a spoken reason.
- **Drawing pipeline in a non-price pane.** Text labels and horizontal lines now carry a pane and a
  price; they render in that pane's own scale, speak their value, nudge by that pane's range rather
  than the price range, survive a workspace round-trip, and remain reachable in the pane list and
  object tree when their indicator is removed.
- **Keyboard menu behaviour.** The chart menu now implements the ARIA menu pattern: Arrow Up/Down
  wrap, Home/End jump, Tab closes, with a roving tabindex (`keyboard.js moveMenuFocus`).
- **Caveats.** Only text labels and horizontal lines can leave the price pane; every other drawing
  tool stays price-pane only. Clipboard success/failure and the actual browser menu focus were not
  exercised in a real browser in this change — only through bUnit and the JS bridge. The context
  menu's limit ticket still depends on the dashboard's own account/provider checks after it opens.
  Writing the new `PanePinned` flag into the workspace restore contract found and fixed its restore
  path; older workspaces without it keep deriving the provider pane as before.

### Toolbar: Heikin Ashi and Log scale name their shortcuts; Market events gets a calendar icon, 2026-11-18

Cody found that the Heikin Ashi and Log scale buttons did not say their keyboard shortcuts, though every
other toolbar button does.

- **Tooltips.** "Toggle Heikin Ashi candles, Alt plus C" and "Toggle the logarithmic price scale, Alt
  plus L". The tooltip is the button's accessible description, so a screen reader now reads the chord
  for both.
- **Market events icon.** The button had reused the Levels icon (a zigzag with dots), so the two looked
  the same. It now has its own calendar icon: a page with two binding rings and a grid of day dots, drawn
  in the same 28-unit outline style as the rest of the sprite.
- **Guard.** `ToolbarControlSurfaceTests.EveryToolbarButtonWithADefaultChordNamesItInItsTooltip` ties
  each toolbar button to its command through `CommandDispatcher` (the action or event its click
  constructs). If that command has a default letter or digit chord, the tooltip must name it in words.
  Before the fix it went red on exactly these two buttons and no others.
- **Caveats.** The guard only covers buttons whose click constructs an action or event that a
  dispatcher case also constructs, and only letter or digit chords. On the day it was written that was 8
  or more buttons; the test fails if it ever checks fewer. A user who rebinds a key still sees the
  default chord in the tooltip, as before. Nobody has looked at the icon yet.

### Market events live, stage 3b: the Alt+E calendar, watched alerts and release history, 2026-11-18

- **The calendar screen (Alt+E).** Days as headings ("Wednesday 11 March 2026"), one line per
  event in time order, the FRED status sentence at the top, and filters for range (next 7 days by
  default, next 30, past 30), importance, the five kinds, and "only events touching my charts and
  positions". The screen opens whatever the Level setting is — it was asked for. Its filters start
  from the user's Level and kinds, or from important-only with every kind when Level is Off.
- **One line per event.** "8:30, CPI, important, previous up 2.9%." The time follows the F12 clock; a
  day-only event says "all day". A past event reads its actual in the existing figure-clause words;
  a future one reads only its known previous figure. Enter on a line takes the chart to the event's
  bar through the apostrophe jump's own navigation, so the bar is read with the jump clause. When
  the event is not on the loaded bars (the future, or older than the history) the screen stays open
  and announces "CPI on 11 March 2026 is not on this chart's bars." through the speech router.
- **"Alert me before".** A toggle beside every exact-time event, with aria-pressed, writes the new
  `narration.marketEvents.watched` setting (a string list of stable ids such as
  `cpi-2026-03-11`). A watched event gets its countdowns and read-out even when it touches no open
  chart or held position, its importance or kind is filtered out, or Level is Off. Day-only events
  cannot be watched (a disabled "No set time" toggle). Countdown leads are the user's setting; when
  that is Off, a watched event uses 15 and 1. Ids more than two days past are pruned whenever the
  list is written.
- **Release history.** The History disclosure lists the last six releases of the same source before
  the event, newest first: date, the existing figure clause (or "no figure"), and the focused chart's
  price move over the bar that release sits on, when that bar is loaded and the chart touches the
  event's market. Same source means the same id prefix (`cpi`, `nfp`, `ppi`, `gdp`, `fomc`,
  `stock-expiry`, ...). Fewer than six lists what exists; none says "No earlier releases." Day-only
  kinds have History too, with no figure part.
- **One touch test.** The calendar's "only touching" filter and the live notices share
  `MarketEventNoticePlanner.Touches`; `MarketEventLiveNotices` exposes its last held markets
  read-only rather than doing a second broker read for the screen. Those are the markets of the
  last position read, which happens only around a notice, so until one has happened this session
  only open charts count as touching (Claude's review note).
- **Fix found in review: the FRED feed broke hosted-accounts mode.** Stage 2 registered
  `MarketEventFredFeed` as one-per-process, but in hosted-accounts mode (the VPS) the data-folder
  service is per user, so the service container refused to start: 58 WebHost tests failed, and CI
  would have failed on the push. `AddHostedAccounts` now gives the feed the instance's own folders,
  as it already does for the encrypted key store; the key service is process-wide there anyway.
  Proven: without the fix the hosted-accounts tests go red (12 of 38), with it all 537 WebHost tests
  pass.
- **Caveats.** There is no expected figure and no surprise word yet; that needs the stage 4 source.
  The history's price part comes from the focused chart only, so a release older than the loaded
  bars, or on a market the chart does not touch, has no price part. Watched alerts are still not
  delivered while the browser is closed — the same browser-closed item remains open.

### Market events live, stage 3a: countdowns, release read-outs and position warnings, 2026-11-18

- **What was built.** The live half of market events, while the app is open: countdowns before an
  event that touches an open chart or a held position, the release read-out one minute after it
  with the focused chart's price reaction, the FRED figure when it posts, the stage 2 bell with
  every notice, and a desktop toast when the window is hidden. Nothing is heard when the
  market-events Level is Off, which is still the default. All speech is on the **Event** tier:
  **Shift+F2 mutes it; F2 does not.**
- **Pure core.** `MarketEventNoticePlanner` decides what is due from a fakeable `now`, the events,
  the leads, the open and held markets and the fired set. `MarketEventNoticeWording` writes every
  sentence. Both are static and free of clocks, buses and I/O; the service only reads state and
  acts.
- **Windows, so nothing stale.** A lead L countdown fires only in [T − L, T − L + 20 s); the
  read-out only in [T + 60 s, T + 90 s); a figure try at minute M only in [T + M, T + M + 20 s).
  Starting at T − 10 minutes with leads 15 and 1 says only the 1-minute countdown. Each
  (event id, lead), each read-out and each figure try fires once per service instance.
- **Which events.** An event needs the user's Level and kind filter, an exact time (day-only
  expiries, holidays, halvings and old dates get nothing), and a market that is on an open chart
  tab or held by a position. The planner takes open and held markets separately so a position's
  market alone is enough; a position whose venue and symbol cannot be classified is not guessed
  at.
- **The words.** "CPI in 15 minutes." and "CPI in 1 minute." use the event's `SpokenName`. When a
  position is held in a market the event touches the sentence adds it: "CPI in 15 minutes. You're
  long 0.05 BTC on MEXC, stop at 89000.00." At most three positions are named, then "And 2 more."
  "stop at" is omitted when no working stop order rests on that symbol, and paper positions say
  "on Paper". Quantities use `QuantityFormatter` and prices `SpeechPriceFormatter`, the existing
  speech formatters, rather than a second set.
- **The read-out.** At T + 60 s, one utterance: "CPI out. BTC down 0.8% in the first minute."
  The price is the focused chart's own live feed, from its newest close at or before T (no more
  than 5 minutes before) to its newest close in (T, T + 60 s], rounded to one decimal, with "up",
  "down" or "unchanged". With no close before T in reach (the app was asleep), the first update
  within 5 s after T stands in. With no update after T in that minute the feed was quiet and the
  sentence is "CPI out." with nothing about price. If the focused chart does not touch the event's
  market, likewise "CPI out." (Claude's review changed the start from "the first tick at or after
  T", which said nothing on a feed slower than 5 s and measured from a price already moved.)
- **The figure.** A FRED release with a key ends the read-out "Waiting for the figure." and is
  asked for at T + 2, 5, 15, 30 and 60 minutes, at most five times, bypassing only the 12-hour
  gate. `MarketEventFredFeed.RequestReleaseFigureAsync` fetches that release's series only, merges
  the result, bumps `Version` so the bar map rebuilds, and saves the cache; the key, logging and
  failure-word rules are unchanged (exception type names only). As soon as the event carries
  figures the asking stops, and the figure is said with the existing
  `MarketEventSpeech.FigureClause`. A try that finds nothing says nothing; the last one included.
  With no key there is no "Waiting for the figure" and no request.
- **The bell.** `INavigationSonifier.PlayMarketEventBell` is the bar bell without a bar: the same
  notes, Sound Designer `MarketEvent` override and volume scale, shared through one private core
  with `FireMarketEventEarcon`. It is silent when "Sound on event bars" is unticked or Shift+F3
  mutes earcons. The bar bell's own wording and firing times are unchanged.
- **Desktop notification.** `DesktopNotificationService` subscribes to the new
  `MarketEventNoticeEvent` under the Alerts category and toasts it only when
  `_presence.IsAppVisible` is false and `Enabled()` is true. A visible window is already speaking
  it; a window hidden to the tray has no other channel.
- **Setting.** "Countdowns before events" in Settings → Narration → Market events, values Off,
  1 minute, 5 and 1, 15 and 1 (default) and 30, 15 and 1, key
  `narration.marketEvents.countdowns`. It travels the same four places as every preference
  (SettingsKeys, AppSettings, PreferencePersistenceService, WorkspaceState) and is not carried
  into the script sandbox. The service runs on a one-second `TimeProvider.CreateTimer`;
  `TimeProvider.System` is registered on both heads and the tests use a hand-written fake clock,
  no package.
- **Registration.** `MarketEventLiveNotices` is a singleton on the MAUI head, scoped on the
  WebHost, registered beside `IAccessibilityFeedbackCoordinator`, and resolved by
  `AppStartupService` so its timer and subscriptions start with the session. It does nothing when
  `IUserPresence.CanReachUser` is false, as the alert delivery service does.
- **Tests.** 748 pass on the handoff's working filter; the source-reading classes pass 1,066.
  Five sabotages, each buildable and each restored from `strategy-lab-data/sab/` with `cp`,
  `touch` and `cmp`, then green: dropping the countdown window's upper bound turned 1 red;
  ignoring held markets in the planner turned 1 red; dropping the quiet-feed rule in the wording
  turned 1 red; removing the presence check turned 1 red; speaking the notice on Chart instead of
  Event turned 1 red.
- **Caveats.** No delivery while the browser is closed: that needs one process-wide owner, as the
  alert monitor has, or a closed tab and the headless session would both speak it
  (`docs/TODO.md`). For CPI, jobs, PPI and GDP, future dates come only from the FRED feed, so
  without a key the only future countdowns are Fed decisions, which the seed carries into 2027.
  The price reaction is the focused chart only. FRED can lag the official release by minutes, so
  the figure may never come within the hour. Nothing here has been heard by Cody yet.

### Market events on the chart, stage 2: figures, live FRED dates and the landing bell, 2026-11-18

- **What was built.** The market-events seed now carries the release figures, the model stores them
  with their unit and hot direction, the arrow clause and Alt+Shift+D speak them, an optional live
  FRED feed refreshes dates and figures, and a small bell marks event bars. Nothing was heard by
  Cody yet.
- **First print, never the future.** A release day gets figures only from the ALFRED vintage visible
  on that day itself: rows with `realtime_start <= D <= realtime_end`, one value per period, newest
  vintage winning, and no value from a later revision. CPI uses CPIAUCSL and CPILFESL, jobs uses
  PAYEMS and UNRATE, GDP uses GDPC1. A figure exists only when that series published on the day;
  otherwise its column is null. Percentages are rounded to 1 decimal and payrolls to whole thousands,
  half away from zero after a 6-decimal clean-up, and a rounded -0.0 is stored as 0.0. The seed's
  values were checked against the handoff's independently computed table; every row matches.
- **Revision days removed (review fix).** FRED's `release/dates` list also names correction and
  seasonal-revision days, so stage 1's seed named a release on days when none came out, and stage
  2's figures made those days speak near-copies of the real release. A date is now kept only when
  it is before the key series' first vintage, or a key-series vintage starts that day and — for
  CPI, jobs and PPI — its newest period is newer than the previous kept date's. GDP keeps any date
  a GDPC1 vintage starts, because second and third estimates republish the same quarter and are
  real releases. Dropped with their figures rows: CPI 19, jobs 7, GDP 48, PPI 16; the 1960s jobs
  months with two real releases stay. PPI has no offline vintages, so two PPI dates in the same
  February no more than 7 days apart drop the earlier one; that is inferred from CPI's pattern,
  not checked against PPI data. The same rule runs in `MarketEventFredFeed` for fetched dates on
  or before the fetch day; future dates are kept because nothing can be checked yet.
- **Seed.** `us_events_seed.json` grew from 47,076 to 95,440 bytes (still under 150 KB) and keeps
  3,419 events. Figure rows are aligned by index with each source's dates: cpi
  `[CPI, previous, core, previous]`, nfp `[payrolls, previous, unemployment, previous]`, gdp
  `[growth, estimate, previous, period]`, and `ppi_figures` is all null. Coverage: CPI 648 of 928
  dates, core CPI 355, payrolls 854 of 854, unemployment 796, GDP 417 of 807. The header now carries
  each ALFRED series id, its download URL and the file's newest `realtime_start`.
- **What is heard.** A one-event bar shorter than a week reads the figure after the time and before
  the placement word: "CPI 8:30, up 1.3% on the month, previous up 1.0%", "unchanged on the month",
  "payrolls up 227 thousand, previous up 36 thousand, unemployment 4.2%", "payrolls down 20.5
  million", "GDP 8:30, first estimate, up 3.0% annualized, previous quarter down 0.5%", "second
  estimate ... was up 3.0%". Percentages always carry one decimal and thousands never do. The detail
  key adds a full line per figure, "unavailable" where a source had nothing, plus GDP's estimate and
  quarter. A crowd or a weekly-or-longer bar keeps stage 1's wording unchanged.
- **Live FRED (optional).** `MarketEventFredFeed` is one singleton per process on both heads, takes
  the user's key from `IApiKeyService` (provider "FRED"), and does nothing without one. It fetches
  release dates for ids 10/50/46/53, observations for CPIAUCSL, CPILFESL, PAYEMS, UNRATE, GDPC1 and
  PPIFIS, and computes figures with one C# function whose fixture test agrees with the seed's Python
  builder on the handoff's table. Fetched dates and figures win over the seed by event id; seed dates
  are never removed. The fetch runs at most every 12 hours, one at a time, only while the level is
  not Off, and never on the navigation path; the first fetch starts on the first event read. The
  bar-map stamp includes the feed version, so the next arrow press rebuilds it. Failures keep the old
  data and record the time and a short reason ("no answer", "HTTP 500", "a bad answer"); there is no
  error sound or speech.
- **Cache and key.** `AppDataDirectory/market-events/fred.json` is written atomically and holds the
  fetch time and fetched figures only. It is loaded at start, so a restart with no network still has
  the last fetch; a corrupt file is logged and ignored. The key is URL-escaped into the request and
  appears in no log, exception message, status line or cache file; failures log an exception type,
  never its message.
- **Settings.** The "Market events on the chart" fieldset gains a sixth tick box, **Sound on event
  bars** (default on, key `narration.marketEvents.sound`), the status paragraph
  `<p id="market-events-status">`, and a never-disabled **Update release dates now** button. The
  Level select describes both the existing hint and the status. The status words the seed's newest
  economic release date (30 July 2026), the last successful update, or the last failure. The button
  skips the 12-hour gate, does nothing but re-read status without a key, and announces its result
  once through the dialog's existing `FeedbackRequestEvent` route.
- **The landing bell.** `NavigationSonifier.FireMarketEventEarcon` plays an inharmonic bell — a
  0.18 s sine plus 2.76× and 5.40× partials — with two falling strikes (F5 then C5, 110 ms apart)
  for High and one F5 strike for Medium and Low, quieter for Low. It uses the same
  `MarketEventSpeech.Passing` filter as the spoken clause, deduplicates per bar, starts 130 ms after
  a text-label tick, is scaled by `ChartVolume` and panned by bar position. It is silent with the
  level off, the new box unticked or Shift+F3, and while arrowing with F3 off. Playback calls it on
  each index change through the new interface entry point, ignoring F3 as playback sound does by
  design. A `MarketEvent` earcon key is in the Sound Designer, and an assigned patch replaces the
  bell with volume scale 1.0 (High), 0.75 (Medium) and 0.5 (Low).
- **Tests.** 492 tests pass on the handoff's working filter; the source-reading classes pass 1,061.
  The test project builds with no new warnings. Six earlier sabotages, each
  buildable and each restored from `strategy-lab-data/sab/` with `cp`, `touch` and `cmp`, then green:
  using the newest vintage instead of the one on the release day turned 7 red; bypassing the shared
  kind/level filter in the sonifier turned 1 red; removing the bell's dedup turned 1 red; putting the
  key-bearing URL in the cache turned 1 red; dropping the feed version from the map stamp turned 1
  red; leaving the feed unregistered on the WebHost turned 2 red (the feed guard and `HostParity`).
  For this fix, rebuilding the seed with the drop skipped (into a copy, then used for the test run)
  turned 7 `MarketEventSeed*` tests red; a one-release-per-month rule turned the 1960s double-month
  test red (1); and returning every fetched date as a release turned 3 red. Each was restored from
  `strategy-lab-data/sab/` with `cp`, `touch` and `cmp`, then green.
- **Caveats.** The live fetch was never tried against real FRED — this environment has no network.
  PPI history before February 2014 has no figures. ALFRED refused automated requests for PPI on
  2026-11-18, so its history comes only through the user's key, and the seed's PPI February drops
  are inferred from CPI's pattern rather than checked against PPI data. A scheduled future revision
  day, if FRED lists one, is kept until it has passed and a fetch shows it published nothing new.
  Nothing here has been heard by Cody yet.

### Market events on the chart, stage 1: the past, 2026-11-18

- **What was built.** Scheduled market events now sit on the bars they happened on. The new
  `AccessibleTrader.Core/Services/MarketEvents/` folder holds the record (`MarketEvent`: id, kind,
  short and long names, market scope, importance, UTC moment, exact-or-day-only precision, the
  event's own date, and its source), the rule engine, the calendar, the pure bar-placement
  function, a cached bar-to-event map, the speech wording, a detail section and the apostrophe
  jumps. Kinds are Economy, Fed, Expiry, Holiday and Halving; importance is High, Medium or Low.
- **Sources.** A committed seed, `AccessibleTrader.Core/Resources/MarketEvents/us_events_seed.json`
  (embedded; 3,509 events), rebuilt by `python3 -I scripts/build_market_events_seed.py`. FRED
  release dates for CPI (release 10), NFP (50), PPI (46) and GDP (53), fetched 2026-08-01:
  947 / 861 / 598 / 855 dates. Fed decisions from the Federal Reserve's calendar pages, fetched
  2026-07-31: 248 dates, 2000–2027, with unscheduled meetings marked. The seed header names every
  source, its URL and its fetch date. No network is used at run time.
- **Worked out by rule.** Stock options expiry is the third Friday, moved to Thursday when that
  Friday is Good Friday (April 2025 is the 17th); March, June, September and December are
  quarterly and Medium. Crypto options expiry is the last Friday at 08:00 UTC. US market holidays
  reuse `CipherCFutureClock.Holiday` and `HolidayNames` unchanged, with Juneteenth only from 2022.
  The four past Bitcoin halvings are UTC dates. One-off closures (for example 2025-01-09) are not
  listed.
- **The placement rule (Cody, 2026-11-18).** The event goes on the bar of the day it happened on,
  as TradingView does. On intraday charts, the bar whose span holds the moment, with a bar's own
  opening moment inside it; the same day outside the session is "before the open" or "after the
  close"; daily charts use the chart's own date; weekly and monthly bars hold their span; a date
  with no bar goes on the next bar as "came out {weekday}"; day-only events go on the day's first
  intraday bar; before the first bar is not placed; inside the last bar but later than "now" is
  "scheduled". Replay reveals a growing prefix of the bars, so rule 7 already keeps events after
  the replay cursor out — no replay-specific branch was needed.
- **Setting.** "Market events on the chart" is on the Narration tab, **off by default** (Cody:
  "don't want to overwhelm new users"), with levels Off, Important only, Important and medium and
  All, and tick boxes for Economy, Fed, Expiries, Holidays and Bitcoin halvings (all on). It
  travels the same path as `DescribeCandlePatterns`: `SettingsKeys`, `AppSettings`,
  `PreferencePersistenceService`, the `WorkspaceState` mirror and `WorkspaceProjection`.
- **What is heard.** A short clause follows the price reading on a bar with events; more than two
  show the two most important and "plus N more events"; weekly bars carry weekdays. Alt+Shift+D
  adds "Events on this bar" with long name, time, importance, placement, source and the bar's
  open-to-close move, and reads whatever the level is set to. Apostrophe and Shift+apostrophe jump
  to the next and previous event bar; with the level off, at either end, or with nothing passing
  the filters, they say why rather than nothing.
- **Placement speed (review fix).** `MarketEventPlacement.Place` now computes each bar's chart
  date and UTC open once into arrays and finds each event's bars by binary search, instead of
  scanning every bar per event and converting its zone again. Fresh-cache `For` timings fell:
  stock daily 2006–2026 (7,300 bars) 153 → 6 ms; crypto daily 2012–2026 (5,100) 65 → 2 ms;
  crypto 1m, 35 days (50,000) 77 → 2 ms; crypto 1h, 5 years (43,800) 424 → 3 ms. An internal
  `[ThreadStatic]` `BarDateCalls` counter guards the property, not a time: placing all 3,509 seed
  events on 5,000 daily bars and 5,000 hourly bars must call `BarDate` at most bars + events.
  Putting one per-event scan back turned the guard red at 9,794,081 calls (limit 8,509);
  restored from `strategy-lab-data/sab/` with `cp`, `touch`, `cmp`, green again.
- **Compact seed.** `us_events_seed.json` now holds the unchanged header plus one date array per
  source (FOMC scheduled and unscheduled separately); the repeated names, importance, scope,
  source and the 8:30/14:00 New York release-time rules moved into `MarketEventSeed.cs`. The
  file fell from 1,086,612 bytes to 47,076; the same 3,509 events, counts, dates, daylight-saving
  times and header. `scripts/build_market_events_seed.py` writes the new format.
- **One event map.** `IMarketEventBarMapCache` is registered once (WebHost scoped, BlazorClient
  singleton, beside `IChartPatternCache`), and both `AccessibilityFeedbackCoordinator` and
  `BarDetailService` take the interface, so navigation speech, the detail key and the apostrophe
  jumps share one map instead of each service building its own.
  Claude's review found the jumps still had a private map: `CommandDispatcher` takes its
  `MarketEventNavigator` as an optional parameter and no head registered it, so dependency
  injection passed null and the dispatcher built its own. `MarketEventNavigator` is now registered
  on both heads with the map's lifetime; `EachHeadRegistersTheJumpNavigatorBesideTheSharedMap`
  turned red (1) with the WebHost line removed. Claude also re-ran the default-Off sabotage
  (`WorkspaceState` default to "importantOnly"): 1 red, as DeepSeek reported.
- **Setting label.** The select under the "Market events on the chart" legend is now labelled
  **Level**, so a screen reader no longer reads the group name twice; the manual names it the
  same way.
- **Caveats.** The seed ends where its inputs end — July 2026 for the releases, 2027 for the Fed;
  stage 2 fetches the rest. Fed decisions before 2013 and releases before 1997 are day-only.
  There are no actual or expected figures yet, no live fetch, no countdowns and no sound.
  **Cody tried it locally on 2026-11-18: "beautiful, this works amazing."**
- **Tests.** 81 new `MarketEvent*` tests (seed counts, header, first/last dates, daylight saving,
  the rules, one test per placement rule, the cache built once, the placement call-count guard,
  the one shared cache, settings defaults and filtering, speech wording, detail key, jumps, key
  bindings), plus the two new chart-scope cases in `Phase5KeyboardScopeTests`. Four sabotages,
  each after green and restored from `strategy-lab-data/sab/`: excluding a bar's opening moment
  from its span turned 1 red; a fixed UTC-5 in place of New York turned 1 red (a cache test added
  for that gap); defaulting the level to Important only turned 1 red; dropping rule 5's next-bar
  fallback turned 2 red. The review's per-event scan turned the new placement guard red (1) and
  was restored green. Every restore rebuilt green.

### Cipher B's sell dot placed like its buy dot when the Zero line is hidden, 2026-11-17

- **What Cody heard:** "the large red sell dot is below -100, but shouldn't it be above 60 to mirror the
  large green [buy] dot?" His saved Cipher B charts hide the Zero line (his choice). With no middle line
  shown, the place fell back to the nearest line: the sell dot (drawn at +96) is nearest the 100% line at
  the top of the pane and was said "below 100", while the buy dot (at -96, with no line at -100) was
  "below -60".
- **Cody's rule:** "the 0 line should be used as the 'above' or 'below' phrasing ... it shouldn't hinge
  on whether the 0 line is shown visually or not". The middle line now counts shown or hidden, both to
  say which side a mark is on and as a line to name: the sell dot is "above 60", the buy dot "below -60",
  a dot at +30 "above 0", at 0 "on 0". (The first fix, b9094530, read a hidden Zero for the side but did
  not name it, so a dot at +30 was "below 53"; that is gone.)
- **Tests:** `SignalLevelPlaceTests` +7: the provider's own dot positions with Zero hidden, hidden-Zero
  cases, and every value from -110 to 110 in half steps placed the same with Zero shown and hidden. Two
  deliberate breaks (Zero named only when shown; Zero the middle only when shown) each turned tests red.

### DBSI study closed; the app's DBSI checked against the last model, 2026-11-17

- **What Cody asked:** "close the dbsi study and be sure that's the latest dbsi in the app". The last model
  the study adopted is `dbsi8/zu_hs66e.py` (2026-11-01); everything tried after it (the Stoch RSI top read
  as exactly 100, dividing first, the one-bar-Q rule, log prices) was rejected. Re-checked today, nothing
  changed: both test fixtures (`DbsiCleanScores`, 35 charts, and `DbsiCoverageCharts`) regenerated from
  `zu_hs66e` today equal the committed ones on every bar, and the 128 DBSI tests pass, among them the parity
  tests that hold `DbsiStyleProvider.Compute` to those scores bit for bit. The app has one DBSI,
  `DbsiStyleProvider`, registered on the desktop, the web host and the strategy lab, and its `Calculate`
  runs that `Compute`. TODO's study item is closed with its limits listed.

### Signals say where they sit, by the number of a level; DBSI's Stoch RSI checked on log prices, 2026-11-17

Not heard in the app; the VPS is not deployed.

- **What Cody asked:** say where every signal sits when "Say what signals look like" is on, for any
  indicator, against "a fixed value on the axis", not another part; then "use the number of the level".
  A mark drawn at its value in an indicator pane now ends with the level it has passed going out from
  the middle line: "Large green dot below -60", "Red dot above 0", "on 53" when it sits on the line.
  On Cipher B a dot at -70 is "below -60", at -55 "below -53", at -40 "below 0". With no middle line
  (a level named Zero, Midline, Midpoint or Neutral) the nearest level is named. Only levels shown AND
  drawn count, so Cipher B's unpainted stochastic 80/20 are never named. A pane with no drawn levels says
  nothing extra. Marks pinned to the candle (Cipher A, the price pane) keep "above/below the candle"; a
  zero dot is drawn on the zero line whatever its value, so it gets no place. The number is written the
  same way in every language setting (`MarkerLook.LevelPlace`). Arrowing, playback, auto narration and
  the speech formatter all say it, since they share the one description.
- **Tests:** `SignalLevelPlaceTests` (19) against Cipher B's own declared levels, plus no-middle-line,
  hidden and undrawn levels, candle-pinned marks, the zero dot and a German culture. Five deliberate
  breaks (no place, nearest only, undrawn levels counted, current culture, zero dot placed) each turned
  tests red.
- **Note:** a level the user moves is named at its new number, since it is read from the chart.
- **DBSI: is its Stoch RSI on log prices? (Cody: "is it using the stoch rsi on tv or the stoch rsis from
  cipher b?")** The probe used TradingView's own Stoch RSI, 12/12 on plain close, which is what our DBSI
  model was fitted to; Market Cipher B's Stoch RSI (14/14, log price) is a different one. Since Cipher B's
  takes the log of price, the whole DBSI model was re-scored with its Stoch RSI on log(close): worse on 95
  of 113 charts, better on 1. DBSI uses plain close. Nothing changed.

### Filled lines said as "area"; TradingView's Stoch RSI probe run, 2026-11-17

Not heard in the app; the VPS is not deployed.

- **What Cody asked:** "I want the speech description to match what is drawn and have it change based
  on whether there is or is not a fill under the line ... if the waves are technically just filled
  areas, let's stick with that." A line or area now says "area" exactly when the chart paints a fill
  under it, and "line" when only its stroke is drawn: "Blue area", "Faint green area". Cipher B's waves,
  money flow and wave gap are filled, so they are areas; turning a part's fill off makes it a line again.
  The word comes from the renderer's own choice of drawing route (`StandardRenderers.DrawsFill`), which
  copies the routes in the renderer: a part coloured by rules or by another part is drawn as a stroke
  even when a fill is asked for, so it stays a "line".
- **A wave filled per side names the colour painted.** With its two-colour switch off, a part filled
  above and below its split (Cipher B's money flow and wave gap) is painted in its second colour below
  the split, but the colour word named the line's own colour. It now names the colour painted
  (`StandardRenderers.LineColorAt`). Cipher B's shipped colours were not affected: their fill below the
  split is the same colour the word already named.
- **Tests:** `AreaWordTests` draws nine kinds of part through the real chart renderer, once with values
  and once empty, counts the pixels painted, and checks the word against that count (a fill paints about
  ten times the pixels of a stroke). Three deliberate breaks each turned tests red.
- **Limit:** the colour said is the line's colour. A filled part given a different fill colour, and every
  fill (drawn see-through), still says the line's colour.
- **DBSI: the Stoch RSI probe (Cody: "try the rsi script").** A Pine script printing TradingView's own
  `ta.stoch(ta.rsi(close, 12), 12)` was run on TradingView at the DBSI bars our model gets wrong by one
  rounding step, and at bars it gets right. TradingView's Stoch RSI equals ours to the last binary digit
  at every one, wrong and right alike. The same Stoch RSI value appears at bars DBSI treats one way and
  bars it treats the other, so the difference is inside DBSI's own hidden code, not in TradingView's
  arithmetic. Nothing changed; the study is proposed closed (TODO). Numbers kept out of the repo.

### Line colour said where it changes, two-tone DBSI trend, Cipher A above the candle, 2026-11-17

None of this has been seen or heard in the app, and the VPS is not deployed.

- **What Cody asked (4):** "Sometimes I may hear 'faint green line' when I'm far away from a transition
  ... I should hear the description as I left and right arrow only when it transitions." The colour was
  compared with the bar to the LEFT of the one landed on, so arrowing left named it one bar early and a
  jump named it on whatever bar it hit. It is now compared with the bar you came FROM: the speech
  formatter remembers, per line, the last bar it read (`SpeechFormatter._lineReadAt`), and
  `MarkerLook.LineSentence` speaks only when the colour differs. The first bar read on a line, or after
  moving to another line, says its colour once. Three tests (`MarkerLookTests`).
- **What Cody asked (3):** "should this line have 2 tones, like oscillators do ... Have ctrl left/right
  jump to crossings too." A line coloured by which side of price it is on (the DBSI trend, through
  `ColorAboveComponent`) now picks its patch and timbre by the colour drawn: the bright colour (green) a
  brighter tone, the warm one (red) a softer tone. Warm = hue under 70 or from 300, or saturation under
  25; if both colours fall on one side, the line's own colour is the bright one. A plain one-colour line
  keeps its one tone. Ctrl+Left/Right on such a line stops where its colour changes ("DBSI trend turns
  green at ..."), or says "No more DBSI trend colour changes in this direction". Four tests
  (`SiblingColouredLineTests`), each turned red by breaking the code.
- **What Cody asked (1):** "cipher A: option B, do it proper." Cipher A's symbols were stored as prices
  (the candle's high plus a fixed step per symbol), so the look never said where they sit. They are now
  pinned above the candle (`MarkerAnchor.AboveBar`) with a stack slot each
  (`IndicatorMetadata.MarkerStackSlot`, the old order: Red X 1, red and Blood diamonds 2, yellow diamond
  3, green dot 4, yellow X 5, blue triangle 6). The renderer (`StandardRenderers.MarkerY`) stands the
  symbols on one candle one above another, each slot as tall as the largest symbol in it, so they never
  overlap; a lone symbol sits right at the candle. The look now says "above the candle". Each symbol's
  value is the candle's high. The anchor is read from the indicator, so saved charts get it too. Four
  tests (`MarkerStackTests`) plus an "above the bar" cell in `DeclaredKnobObservabilityTests`.
  - **Limit:** a bar-pinned symbol keeps the existing padding of 0.35 of the candle's range, so a tall
    candle has a wide gap above it. Checked in a rendered PNG only.
- **Not built then:** (2) the TradingView Stoch RSI probe (TV's browser had no debugging port; run later
  the same day, above); the "where the signal sits" description (still a question in TODO) and the
  line/area names (built the same day, above).

### Browser harness on Playwright 1.64.1, CI back on ubuntu-latest, 2026-11-17

- **What Cody asked:** "can we look into upgrading playright and addressing this issue?" The browser job
  had been pinned to `ubuntu-24.04` since 2026-11-07, because Playwright 1.55's installer refuses Ubuntu
  26.04. 1.64.1 (2026-10-14) is the newest version on both npm and NuGet, and its installer lists
  Ubuntu 26.04 with Chromium and its system packages (read from the published `playwright-core`
  package). The package, the `npx playwright@` line, the hint the harness prints when Chromium is
  missing and SERVER_SETUP.md all move to 1.64.1, and the job runs on `ubuntu-latest` again.
- **On CI it passed on Ubuntu 26.04** (run on 8460d881: image `ubuntu-26.04`, 245 passed, 1 skipped,
  the same as the last run on 24.04). It builds with no errors and no obsolete-API warnings.
- **A test now checks the version is the same everywhere it is named** (`PlaywrightVersionPinTests`). A
  workflow line left at 1.55.0 turns it red (checked).
- **The VPS** has build 1187 (1.55). It needs `npx playwright@1.64.1 install chromium` before its next
  browser-harness run.

### Tidy WebHost downloads, 2026-11-17

- **What Cody asked:** "webhost zips being flat: let's structure these and all packaged zips to be clean
  and organized for the end user." The four WebHost zips were the publish's ~470 files at the zip's root.
  Each now unzips to one folder, `AccessibleTrader-WebHost-<version>/`, holding `app/` (the publish,
  untouched), a `README.txt` for that system, and one file to start it: **Accessible Trader.exe** on
  Windows, `accessible-trader` on Linux, **Accessible Trader.command** on macOS. The MAUI Windows zip,
  its setup and the macOS `.app` zip were already one folder each and are unchanged.
- **The starter runs the server from inside `app/`, and that is its one real job.** The server reads
  `appsettings.json` from its working directory; started from anywhere else it binds port 5000 instead of
  5145 and says nothing (the page itself is served from any directory, so it looks fine). On Linux and
  macOS the starter is a shell script; on Windows it is the existing launcher built a second way
  (`-p:LauncherApp=WebHost`), which also puts up a message box if the server stops within 30 seconds of
  starting (port taken, most likely), where before the console window would just flash and vanish.
- **The market-data cache no longer lands in the folder the server was started from.** On Linux
  `GetFolderPath(InternetCache)` is empty, so `WebHostPathService` and `UserScopedPathService` put the
  cache at the relative path `AccessibleTrader/DataCache`: in the checkout on Cody's machine (TODO since
  2026-10-21), and inside an unzipped download for anyone who ran it from there. It now goes to
  `PlatformPaths.LocalCacheRoot()`: the OS cache folder, else `$XDG_CACHE_HOME`, else `~/.cache`, always
  absolute. The old `AccessibleTrader/DataCache` folder in the checkout is no longer used and can be
  deleted; the cache rebuilds itself. A test checks every cache path is absolute and outside the working
  directory; the old code turns it red (checked).
- **Laid out by `packaging/webhost/package.sh` (Linux, macOS) and `package.ps1` (Windows)**, run by the
  release job, which now builds win-x64 on a Windows runner. Each tries its zip where it can: unzipped,
  the starter run from another folder must serve the page and blazor.web.js, the server's log must name
  `app/` as its content root, and nothing may be written into the folder. Run here on the Linux zip: all
  three pass, a starter that does not change into `app/` fails the content-root check, and the old cache
  code fails the nothing-written check (both checked). `release-payloads.json` now expects the payload
  under `AccessibleTrader-WebHost-*/app/` and the starter and README on top, with the Linux and macOS
  starters executable in the zip; the verifier checks that bit (a 644 starter and the old flat zip both
  fail it, checked). A test runs `package.sh` for all three of its RIDs on a stand-in publish and checks
  the zips against the manifest; a 644 starter, a flat zip, a wrong script name and a workflow without
  `package.ps1` each turn a test red (checked).
- **Not tried:** `package.ps1` has never run (no PowerShell here), so the Windows zip, its launcher and its
  message box are untried until a `diagnostic-build.yml` run or the next release; the macOS starter is
  cross-built and has not been opened on a Mac.

### Cody's answers, 2026-11-17: what was built

- **What Cody said** to the eight questions left after 2026-11-16: "1. cipher b: Yes, keep them gone, I
  agree / 2. Ok, leave them refusing / 3. Yes, each keeps its own sound / 4. add indicator dialog: Yes,
  add it / 5. No as you suggested / 6. No. this just unhides what was hidden using h / 7. Yes / 8. Don't
  wait on it/close it". Answers 1, 2, 5, 6 and 8 build nothing (recorded in TODO); 3, 4 and 7 are below.
  None of it has been seen or heard in the app.
- **Each "Read as" form keeps its own saved sound.** A part that can be read as a wave or as a histogram
  (Cipher B's Wave Gap, for one) had one set of saved sound defaults, applied to whichever form was
  built, so a histogram's waveform saved in Properties came back on the wave. Switching form in
  Properties also rebuilt the part from the indicator's defaults, ignoring what was saved. Now the
  saved defaults hold the form they were saved in plus the other form's sound
  (`ComponentPreference.OtherFormSound`, `FormSounds`), Save as defaults keeps the other form's sound
  instead of dropping it, and both a new indicator and a form switch take the sound saved for that form.
  Four tests; breaking the lookup or the keep turns them red (checked).
- **Add Indicator splits settings into Advanced, as Properties does.** Settings the original indicator
  does not have sit in a collapsed "Advanced (N settings)" group with the same sentence Properties uses;
  the rest are in a "Parameters" group. An indicator with no advanced settings shows no Advanced group.
  Three tests; putting an advanced setting back in the main list turns them red (checked).
  The group's title is a level-3 heading inside the summary, as in Help, so screen reader heading keys
  reach it. The first push had plain text there, and CI's DisclosureHeadingSweepTests caught it.
- **The Alerts dialog lists left-out parts, marked "(left out)".** A part left out in Properties is
  still computed, so an alert can now watch it without putting it on the chart; the parts are listed
  after the included ones. Listing them was not enough: every alert path looked parts up among the
  included ones only, so the evaluator said the indicator "has no Signal any more", a zone alert read
  every overbought or oversold line instead of the part's own, and the bar-before memory (the chart's
  alert loop, the hosted and browser-closed poll, and background tabs) skipped the part, so a cross on
  it could never be known. `ComponentInclusion.Find` and `Computed` cover both lists and each path uses
  them. Six tests; eight sabotages (the list, the label, the evaluator, the zone lookup, both chart-loop
  snapshots, the hosted poll's snapshot, and the shared helper) each turn them red (checked). Not tested
  on its own: the background tab monitor's snapshot (its harness has no indicator engine); it uses the
  same helper.

### Cody's answers to the open questions: what was built (2026-11-16)

- **What Cody said:** "take all of your suggestions to the questions. drop the gold dot, not needed."
- **Alt+Shift+D says a timing window's count once.** It used to read Cycle Position's bare counts and
  then the same counts in words ("Cycle Position: 78, 29, 3, 78 days since the last daily cycle low,
  ..."). A window whose tier runs on the chart is now left out of the numbers, on the ladder and for a
  hidden focused series (`CipherCProvider.CountSaidInWords`). The status line still prints the count.
  The test fails with the skip removed (checked).
- **Cipher B on light themes and Steel Gray.** Market Cipher B's translucent white lines (40-50%) were
  about 1.0:1 on Paper; on a light chart (background luminance above one half: Paper, High Contrast
  Light, or a custom theme built from them) they are now drawn in the grey #8C8C96 they had from
  2026-10-17 to 10-31 (3.2:1 on Paper, 3.3:1 on High Contrast Light). On Steel Gray, where neither translucent wave reached 3:1
  (2.995:1), the waves are drawn opaque. The swap is at draw time (`Rendering.ThemeAdaptedColours`)
  and only for colours still at MCB's values: saved charts keep their colours, a colour you chose is
  drawn as chosen, and every other theme looks as before. Tests read the drawn pixels on all twelve
  themes; removing either swap, or swapping a chosen colour, turns them red (checked).
- **Cipher A's Blood Diamond is drawn larger than the red diamond at every zoom.** Each mark was capped
  alone at about two candles wide, so from about 150 bars on an 800-pixel chart the Strongest Sell
  Warning (12) and Momentum Turned Down (8) came out the same size, told apart only by shade. Now an
  indicator's visible dots, diamonds, squares and crosses of one shape shrink by one factor: the largest
  stops at the cap and the smallest at the floor, so the declared 12-to-8 holds at every zoom (at extreme
  zoom the larger can pass the cap by that ratio). A shape drawn in one size is clamped as before, so
  other indicators look the same unless they declare one shape in two sizes. Triangles, check boxes and
  gradient dots keep the per-mark cap. Tests check the ratio at five zooms, the unchanged one-size case,
  and the drawn pixels; four sabotages (per-mark cap, no shared floor, hidden marks counted, renderer
  not switched) each turn one red. Not seen in the app.
- **Formation stops name what starts or ends there; nesting says the whole chain.** Inside a chosen
  formation a `,` or `.` stop read the choice and only "Plus 1 more formation here", so the formation
  the key stopped for went unnamed. Every stop and arrow step on a start or end now adds *"Also starts
  here: possible falling wedge."* / *"Also ends here: double bottom."* for the others there, and the
  "Plus N more" count leaves them out. The containment clause names the whole chain outward, the
  immediate parent with its date and the rest by name (*"Inside a larger range that began 1 January,
  inside a larger inverse head and shoulders."*). Two older tests now expect the names in place of the
  count; three sabotages (no chain, no also-here clause, also-here counted again) each turn one red.
  Not heard in the app.
- **Retired built-in strategies leave old libraries; `TradeRanker` deleted.** The twenty catalogue
  strategies retired since 2026-08-01 (the trilogies, the two symmetric shorts, Pulse Reversal, v24, and
  on 10-31 v14, v15, v18 and the v23 family) had their code removed already, but a library seeded before
  August still held them, unable to fire (Cody's held all thirty built-ins). On load they are now
  written to `strategies.retired.json` beside `strategies.json` (added to it if it exists) and the
  library is saved without them; if that write fails the library keeps them. Only those exact
  `builtin.*` ids move (`RetiredBuiltInStrategies`); a test holds the list equal to the lab's
  `CatalogueProvenance.Retired`, whose verdicts stay. `TradeRanker`, the 0-100 scorer for the v23
  family's trades with no callers, and its tests are deleted. Six sabotages (nothing moved, dropped
  without the backup, an id missing, not saved, backup overwritten, same id not replaced) each turn a
  test red; two of my first tests stayed green under them and were tightened. Your own library moves
  on the next start of the terminal; not seen in the app.
- **Cipher C's "Low expected" is removed.** The chance of a new daily low in the next few days was
  weak: its "likely" was right about half the time (45% on 15 unseen charts, 59% on Bitcoin;
  CYCLE_POSITION_DESIGN section 8). Its table row, state words, speech, the "Low expected, %" line
  (`CompExpectedLine`, off by default) and its fitted model are gone; "Low is in" is unchanged, still the
  study's to 1e-9 on Bitcoin. The "Low is in" cell was blank wherever Low expected was empty; it now
  follows the weekly and yearly cells' rule, so on a daily chart's bars before the first confirmed low it
  shows a dash (no low held) instead of nothing; off a daily chart it stays blank. **An alert or strategy built on the "Low expected, %" line no longer finds
  it.** A test checks no row, line, key or word for it is left; bringing back the row, or dropping the
  cell's dash, turns it red (checked). Not heard in the app.
- **Cipher C: "high" is the word to act on for the weekly.** Measured first on 51 daily charts
  (strategy-lab-data, 3,878 weekly lows): a weekly low's first "very high" is right 95.8% of the time
  but comes for only 75% of weekly lows, 11 bars before the confirmation; its first "high" (80%) is
  right 82.3%, comes for 98.9%, 32 bars before. The lows only "high" catches are confirmed 57% of the
  time, and a "high" with few days left is no better (92.1%), so no extra rule. The very high mark and
  its call line are unchanged; added beside them: **Weekly High**, a paler blue diamond on the first
  "high" of each weekly low ("Weekly low is in: high, 84%, for the low of ..."; no mark on a bar
  already "very high"), and **Weekly High Call**, a line for strategies and alerts (off by default).
  The "Very high marks" switch hides it too. CYCLE_POSITION_DESIGN section 9.13. Four sabotages each turn
  a test red (checked). Not seen or heard in the app; one weekly "high" in six is later undercut.
- **Cipher C: Low is in is back on weekly charts, and its weekly reading on 4-hour coin charts.** It
  was daily-only since the 2026-11-07 change to counting in time, with its old 4-hour and weekly
  scores unchecked. Re-run on the time-counted lows (nothing refitted), the first "very high" call:
  on 51 charts made weekly, the chart's own weekly lows 99.2% right (91.7% called by their
  confirmation) and the yearly reading 99.7%; on 7 coins' 4-hour charts the weekly reading 96.2%
  (95.8% on daily charts). Kept off, below the daily chart's own: the 4-hour daily reading (94.8%,
  against 98.1% on the same coins' daily charts) and the 4-hour yearly one (9 of 11). On those charts
  the weekly very high and high marks, call lines and the "often missed here" record come back with
  it. Said in time: days to the confirmation in days (trading days without weekends), "the 10-week
  average", "the 40-hour average". Other intraday charts and 4-hour stock charts were not measured and
  read nothing. CYCLE_POSITION_DESIGN section 9.14. Five sabotages each turn a test red (checked). Not
  seen or heard in the app.
- **Custom compiled indicators are computed for strategies, on Heikin-Ashi candles when asked.** Cody:
  "custom indicators should be able to be calculated using ha, of course." The chart already computed
  a custom (Roslyn or Pine) indicator on Heikin-Ashi; the strategy path did not compute one at all. It
  recomputes through the indicator service, which knew only the built-in providers; the custom
  indicators sit in a registry only the chart read. So a custom indicator's leaves were false (no
  error, no word) in four places: a Heikin-Ashi-signal strategy; any strategy on a Heikin-Ashi chart
  (which recomputes on the real bars); the backtester for both; and, found while doing this, a
  background tab, whose alerts and strategies recompute every indicator through the same service on
  candle charts too. The service now holds the custom registry and computes a custom indicator first,
  as the chart does, through one shared routine (`IndicatorEngine.CalculateCustom`), so a strategy
  reads what the chart drew. Tests: a custom spy beside the provider spy in the Heikin-Ashi strategy
  tests (signal on the Heikin-Ashi close, real-bar reading on a Heikin-Ashi chart, the backtest), and
  the web host's own container computing a circuit's custom indicator. Three sabotages turn them red
  (no custom branch, customs on the real bars, the registry not kept). The background tab is covered
  by the shared service, not by a test through the monitor. Not tried in the app with a custom
  indicator.

### After the 2.13.1 tag: the release checked, the website on 2.13.1 (2026-11-16)

- **The release run** (43896320872) passed every packaging trial (launcher start; 1,069 files installed byte
  for byte, twice; Start menu entry; uninstall) and the payload check on all seven assets. The downloaded
  setup and zip match SHA256SUMS.txt; the zip's one top folder is `Accessible Trader 2.13.1`. Sizes: setup
  85.1 MB, zip 136.9 MB; the other five are the same size as 2.13.0's to 0.1 MB.
- **accessibletrader.com:** the download page is on 2.13.1 with a row for the setup and one for the
  portable zip, says what 2.13.1 changed and how to start the Windows app, and no longer calls the
  desktop app "traditionally installed"; all eight download links answer 200. The home page's hero line,
  llms.txt, the Quick Start, PLATFORMS and What's New pages (copied from the tag) and llms-full.txt
  (regenerated) follow. The structured data reads 2.13.1 from What's New. Backup on the VPS:
  `~/backups/trade-site-20261116-2131/`.
- **Not changed:** the hosted terminal is still the 2.13.0 build (2.13.1 changes no app code), and
  codyhurst.com's card names no version.

