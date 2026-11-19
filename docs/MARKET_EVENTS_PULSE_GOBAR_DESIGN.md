# Market events, market pulse and the Go bar — design

Written 2026-11-18 from a discussion with Cody about what a Bloomberg Terminal gives a trader that this
terminal does not. **Stages 1, 2, 3a, 3b, 4a, 5a and 5b are built (2026-11-18); stage 1 has been tried
by Cody and stages 2, 3a, 3b, 4a, 5a and 5b have not been heard yet. Stage 4b, the rest of stage 5
(5c), stage 6 and the rest are not built** except where a stage below says it is. This is the plan and
the reasons, so a later session (or a sub-agent) does not have to re-derive them.

## Why these three

A Bloomberg seat (about $30,000 a year) is mostly data licences, a news desk and a chat network that
everyone on Wall Street is on. None of that is software, and little of it matters to someone trading
crypto and a few stocks. What can be copied is how it works:

- a **command line** with short codes (`AAPL US Equity DES <GO>`), so nothing is more than one typed line away;
- every screen the **same shape**, headline first;
- **peripheral vision**: four to six monitors, and things moving at the edge of sight.

The last one is the real gap for a blind trader. A sighted trader takes in a lot without looking at
anything. A blind trader gets nothing until they ask. So the aim is not to copy Bloomberg's screens. It
is to give a blind trader, through sound and well-chosen interruptions, the awareness six monitors give
a sighted one. Three features do most of that:

1. **Market events** (Bloomberg's ECO and EVTS screens): scheduled events read on the bars they happened
   on, counted down before they happen, and read out when they land.
2. **Market pulse and "what changed"**: a ten-second spoken summary of the market, and a catch-up of what
   changed since you last listened.
3. **The Go bar**: one text box that reaches every action in the terminal by typing.

## Cody's decisions (2026-11-18)

1. **An event goes on the bar of the day it happened, as TradingView does.** Earnings after the close go
   on the report day's bar, not the next one. Worded "after the close".
2. **Earnings dates from Finnhub.** Cody is signing up for the free key.
3. **Events are OFF by default** on new charts and for new users ("don't want to overwhelm new users").
4. **The pulse list as proposed** (below) is good for now.
5. **The work is done by DeepSeek V4.1 Flash through OpenCode**, to save Claude usage; Claude writes the
   handoffs, reviews, re-runs the tests and merges. The docs and the user manual are updated with every
   stage.

---

## 1. Market events

### What counts as an event, and where it comes from

| kind | source | what it gives |
|---|---|---|
| US economic releases: CPI, jobs (NFP), PPI, GDP, retail sales, PCE, ISM, JOLTS | FRED release dates (`fred/release/dates`, the FRED key the terminal already supports); a seed file of past dates ships with the app | dates, past and scheduled. **No times**: a small table of release times in New York time (CPI 8:30, ISM 10:00, ...) |
| Fed decisions and minutes | the Fed's own calendar pages. 248 decision dates 2000-2027 were already parsed for the lab (`strategy-lab-data/events_fomc.json`); ship them as a seed and refresh | dates; decision time 14:00 New York |
| Worked out by the terminal | rules | stock options expiry (third Friday, Thursday when Friday is a market holiday), quarterly expiry (March, June, September, December), crypto monthly options expiry (last Friday, 08:00 UTC), US market holidays (the terminal already knows them: `CipherCFutureClock`), Bitcoin halvings |
| Earnings, dividends, splits | past report dates: SEC EDGAR filings (exact, free). Future: Finnhub (free key) | date, and before the open / after the close |
| Crypto token unlocks | DefiLlama's emissions/unlocks API now answers "Upgrade to the paid API plan" (checked 2026-11-18), so this needs a paid source or Cody's decision | date, amount |
| Your own | typed in | anything |

**The "expected" figure is the honest gap.** Free sources give the actual figure (the terminal already
reads FRED's first prints, not later revisions). Nobody gives away the economists' consensus. The plan is
**Kalshi**, already integrated: it runs markets on CPI, jobs and Fed decisions, so "expected" is what
Kalshi traders priced before the release, said with its source ("expected 3.0, by Kalshi"). Which
Kalshi markets exist must be checked. For history before Kalshi there is no expected figure: old bars say
"CPI 3.1, up from 2.9" and no surprise word. The terminal never makes a consensus up.

#### Kalshi expected figures (stage 4a, checked against the live API 2026-11-18)

No key and no auth header; base URL `https://api.elections.kalshi.com/trade-api/v2`. One event per
release per figure. The series are tried newest name first, then the older name for releases before
late 2024:

| our figure | series (in order) | reporting step | strike scale |
|---|---|---|---|
| `CPI` | `KXCPI`, `CPI` | 0.1 | 1 |
| `core CPI` | `KXCPICORE`, `CPICORE` | 0.1 | 1 |
| `payrolls` | `KXPAYROLLS`, `PAYROLLS` | 1 (thousands) | strikes are jobs: divide by 1000 |
| `unemployment` | `KXU3`, `U3` | 0.1 | 1 |
| `GDP` | `KXGDP`, `GDP` | 0.1 | 1 |

- **Event ticker.** Monthly figures use the month BEFORE the release month: CPI released
  2026-07-14 is `KXCPI-26JUN`; payrolls released 2026-07-02 is `KXPAYROLLS-26JUN`; the old
  series uses `CPI-24OCT` for the 2024-11-13 release. GDP uses the RELEASE date itself:
  `KXGDP-26APR30`, `GDP-24OCT30`. Month names are upper-case English three letters, year two
  digits. Only GDP's advance estimate has markets; a second or third estimate finds none.
- **Close-date guard.** A found event is used only when its markets' `close_time` date is within
  one day of our release date, so a delayed release cannot borrow another month's market.
- **Listing markets.** `GET /markets?event_ticker={T}&limit=100`; if that is empty, the event is
  older than the live cutoff, so `GET /historical/markets?event_ticker={T}&limit=100`. A market
  has `ticker`, `close_time`, `strike_type` ("greater" only), `floor_strike` ("the figure is
  greater than this"), `status`, and for open markets `yes_bid_dollars`, `yes_ask_dollars`,
  `last_price_dollars` as decimal strings.
- **Candles.** Live: `GET /series/{series}/markets/{ticker}/candlesticks?start_ts={s}&end_ts={e}&period_interval=60`.
  Historical: `GET /historical/markets/{ticker}/candlesticks?...` (the live path 404s for them).
  Unix seconds; `e` is the release moment and `s` is `e - 3 days`. A candle's `end_period_ts`,
  `yes_bid`, `yes_ask` and `price` objects close under `close_dollars` on the live path and
  `close` on the historical path; both spellings are read everywhere.
- **One strike's probability** is P(figure > strike) from the newest candle at or before the
  release moment with a usable quote, walking back if needed: mid of bid and ask when both exist,
  ask >= bid and ask - bid <= 0.10; else the last trade (`price.close`); else the previous candle.
  An empty book (bid 0, ask 1, mid 0.50) is why the spread limit exists — `CPI-24OCT` has three
  such strikes and without it the answer is wrong. An upcoming release uses the market list's
  own `yes_bid_dollars` / `yes_ask_dollars` / `last_price_dollars` with the same rule, no candles.
- **The expected figure** is the median outcome. Strikes ascend, probabilities are made
  non-increasing by a running minimum from the lowest strike, and adjacent k1 < k2 with
  p1 >= 0.5 > p2 give c = k1 + (k2 - k1) x (p1 - 0.5) / (p1 - p2); expected is
  `ceil(c / step - 1e-9) x step`, rounded to the step's decimals. Rounding up is right: the
  figures are on a 0.1 or 1-thousand grid and the median is the smallest grid value v with
  P(X > v) <= 0.5. Strikes one step apart give k2. Fewer than two strikes, or a ladder that never
  crosses 0.5, give no expected figure.
- **Saved reference answers** (in `AccessibleTrader.Tests/Fixtures/MarketEvents/Kalshi/`):
  KXCPI-26JUN -0.2, KXCPICORE-26JUN 0.2, KXPAYROLLS-26JUN 128, KXU3-26JUN 4.3,
  KXGDP-26APR30 2.6, KXGDP-26JUL30 1.9, KXCPI-26OCT 0.3 (live candles), CPI-24OCT 0.2 and
  PAYROLLS-24OCT 137 (old series), KXCPI-25OCT 0.2, and upcoming KXCPI-26NOV -0.1 (market list).
- **Rate limit.** About 12 requests a second got a 429, so the feed sends at most three a second.
  A 429, 5xx, timeout or malformed answer stops the pass and is never cached as "no market"; a
  404 or empty list from both market paths is a real no market, permanent once the release is
  more than two days past.

### One event

Each event is one record: an id; its kind; a short name ("CPI") and a long one; who it touches (all
markets, US, one currency, one symbol); importance (high, medium, low); when (a moment in UTC, plus how
precise that is: an exact time, a day only, before the open, after the close); its state (scheduled,
released, moved, cancelled); actual, expected and its source, previous and any revision, with a unit and
which way is "hot"; where it came from and when that was fetched.

Every field follows the dossier's rule: a value, "none", "doesn't apply" or "unavailable". Never a blank.

### Putting an event on a bar

This is where it is won or lost. Cody's rule (decision 1): **the bar of the day it happened.**

- **Intraday charts:** the bar whose span holds the moment. A bar's opening moment is inside it (on a
  30-minute chart, 8:30 is on the bar that opens at 8:30).
- **An event the same day but outside the session** (a stock chart has no bar at 8:30 or 16:05): before
  the first bar, it goes on the day's first bar, "before the open"; after the last bar, on the day's last
  bar, "after the close".
- **Daily charts:** the bar of that date, in the market's own day (UTC for crypto, the exchange's date for
  stocks). Earnings after the close stay on the report day's bar (decision 1).
- **A day with no bar** (weekend, holiday): the next bar, worded with its day, "came out Saturday".
  **An event is never dropped because no bar holds it.**
- **Weekly and monthly bars** hold many events: filtered by importance, then summed: "FOMC Wednesday, CPI
  Thursday, plus 1 more".
- **Day-only events** (an unlock with no time) go on the day's first bar and say "time not given". The
  terminal never invents a time.
- **Past the last bar**, upcoming events sit on the projected bars the chart already draws on its own
  clock: "Thursday 8:30, CPI, expected 3.0".
- **Never the future:**
  - On today's bar before the release, "CPI in 25 minutes", not the number.
  - In replay (F11), events after the replay cursor read as scheduled, with no result.
  - Old bars read what was known then: the first print, not today's revised one.
  - A guard test proves it, red first.

### What you hear

- **Arrowing onto a bar:** a short clause after the price and patterns, such as "CPI 8:30, hotter than
  expected." Each release stores which way is "hot": higher CPI is hotter, higher unemployment is weaker.
- **A sound** on landing on an event bar, louder for important ones, set in the Sound Designer, so events
  can be noticed with speech muted. Playback (Space) sounds it as it passes.
- **Alt+Shift+D** adds "Events on this bar" with the time on your F12 clock, actual, expected and its
  source, previous and revision, and what price did over that bar ("price fell 1.8% by this bar's close").
- **Apostrophe / Shift+apostrophe** jump to the next / previous event bar, the way `.` and `,` move
  between formations. (Both keys were free on 2026-11-18.)
- **Off by default** (decision 3). Settings → Narration: off, important only, important and medium, all;
  a tick box per kind (economy, Fed, earnings, unlocks, expiries, holidays). The default set when turned
  on follows the market: crypto gets US economy, Fed, the coin's unlocks and crypto options expiry; stocks
  get economy, Fed, the company's earnings and dividends; forex (later) gets both central banks.
- **Muted by F2**, like all chart reading.

**As a chart overlay (later stage).** "Events" as an overlay like Market Structure would give the Object
Tree, H to hide, N to narrate and its own sound and volume for nothing. Stage 1 starts with the setting
only; the overlay is a later choice.

### While it is live

- **Countdowns** before important events that touch an open chart or a position: 15 minutes and 1 minute
  by default, your choice.
- **Position-aware warnings:** "CPI in 15 minutes. You're long 0.05 BTC on MEXC, stop at 89,000."
- **At the release, the price reaction first**, because it is instant and comes from your own feed: "CPI
  out. BTC down 0.8% in the first minute." Then the figure when the source posts it: "CPI 3.1, expected
  3.0, hotter." FRED can lag the official release by minutes, so it says "waiting for the figure" rather
  than going quiet. The official source, BLS, blocks automated requests (noted in the lab's
  `MacroEventCommand`).
- **On the alerts channel** (Shift+F2 mutes), through the same route alerts use when the browser is closed.
- **"Alert me before this"** on any event.

### The calendar screen (Alt+E, free on 2026-11-18)

Days as headings. One line per event, most important part first: "8:30, CPI, important, expected 3.0,
previous 2.9." Filters by importance, by kind, and "only things touching my charts and positions". Enter
takes the chart to the event's bar. One key sets an alert. One key reads the release's history: the last
six figures with the surprise and the price reaction each time. The source status sits at the top: "Earnings
unavailable, Finnhub key not set".

### Robustness

- The four states everywhere.
- The calendar is saved to disk with the time it was fetched. When a source fails, the saved copy is used
  and its age is said.
- One broken source never blanks the calendar; the screen says which one is down.
- Moved and cancelled events are kept and worded ("moved from Wednesday"). The 2025 US government shutdown
  delayed the jobs and CPI reports, so this happens.
- The same event from two sources is merged once.
- Tests: daylight-saving changeovers both ways; an event exactly on a bar's start; the never-the-future
  guard; recorded copies of each source's real answers (the venue rule); twenty years of weekly bars with
  the bar-to-event map built once per data change, not per key press.

---

## 2. Market pulse and "what changed"

### Pulse (Alt+P, free on 2026-11-18)

- The pulse list is **a watchlist named "Pulse"**, edited in the existing watchlist screen (Alt+M). The
  list (decision 4): S&P 500, Nasdaq, Bitcoin, Ethereum, US 10-year yield, dollar index, gold, oil, VIX,
  then anything you hold.
- **A fixed order, never re-sorted**, so the ear learns where Bitcoin comes.
- Short: "S&P up 0.4, Bitcoin down 1.2, ten-year 4.31, up 3 basis points." Yields change in basis
  points. A closed market: "S&P closed, down 0.3 Friday." An old quote: "gold, 4 minutes old."
- **Every change is measured from a stated start**: the previous session close for stocks, midnight UTC
  for crypto. A setting; the detail read names it.
- **All quotes are fetched at once with a short time limit, then spoken as one sentence.** A slow quote is
  "unavailable"; nothing arrives after the sentence.
- **You asked, so neither F2 nor Shift+F2 mutes it**, like the detail key.
- One compact braille line.

### What changed (Alt+U, free on 2026-11-18)

- A **change log** written all the time, including while the browser is closed (server side). Most of what
  goes in already exists as events inside the terminal; this saves them: alerts that fired, fills and stops
  (the journal), events released, signals and formations on watched charts, dead feeds and reconnects,
  position profit and loss, unusual moves.
- **"Unusual" is against each asset's own normal swing over that much time**, not a fixed percent. A 2%
  Bitcoin day is not news; a 1% EUR/USD day is.
- The catch-up starts with a count and the time away: "7 changes since 9:40, 47 minutes ago." Then, in
  this order: money, alerts, events released, unusual moves, signals, connection problems. The top few,
  then "press again for the rest".
- **The "caught up" mark moves past spoken items only.** An interrupted catch-up keeps the rest.
- An optional prompt on return: "You were away 47 minutes, 3 things changed, Alt+U to hear them." It
  offers; it never reads unasked.
- Later, a **morning brief**: catch-up, pulse and today's events.

---

## 3. The Go bar (Alt+G, free on 2026-11-18; maybe backtick as well)

- **One registry behind it.** Every action the terminal has (about 140 key bindings on 2026-11-18) gets a
  name, Bloomberg-style short codes and plain aliases, one line of help, its arguments, and the
  dispatcher's existing rules for when it is allowed. **A guard test fails if any action is missing**, so
  new features cannot skip the Go bar; proved red by removing one.
- **Words in either order:** `BTC GP` or `GP BTC`.
- **Example commands:** `ECO`, `PULSE`, `CHANGES`, `DES`, `TF 4h`, `IND rsi`, `ALERT 95000`,
  `GOTO 2024-03-01` (jump to a date), `GOTO CPI` (the last CPI bar), `FIND hammer`, a setting by name
  (`log scale` goes to that setting), `HELP GP`.
- **Matching order is fixed:** exact code, then alias, then prefix, then start of a word, then a near-miss
  spelling. After a short pause it reads the top suggestion politely. Down arrow lists suggestions, Tab
  completes, Enter runs the top one, Escape returns focus exactly where it was.
- **Symbols are never guessed quietly:** the current venue, then watchlists, then other venues. More than
  one match is said and picked: "BTC, 3 matches: MEXC BTCUSDT, ...".
- **Money commands are never fuzzy.** `BUY`, `SELL`, `FLAT` must be spelled exactly and go through the
  spoken order review. Nothing involving money runs on a near-miss.
- **Every command says what happened or why it could not.** No silent failure.
- **It lives in Core**, so the desktop and the browser get the same Go bar. The box follows the accessible
  combobox pattern; review it against the terminal's existing dialog conventions (the accessibility
  agents are not registered in this setup; say so in the review).

---

## Stages

Each stage is one or more DeepSeek runs from a handoff; Claude reviews, re-runs the tests and the
sabotages, merges locally, and pushes once at the end of a batch (the push is the full suite).

1. **Events on bars, the past — BUILT 2026-11-18; Cody tried it the same day: "this works amazing."** The event model; the bar
   placement rule; sources that need no network at run time: a seed of FRED release dates for CPI, NFP,
   PPI and GDP and the Fed decision dates, the release-time table, and the rule-worked events (expiries,
   holidays, halvings); the setting (off by default) with levels and kinds; the arrow clause; the
   Alt+Shift+D section; apostrophe jumps. No expected figures, no actual figures yet, no live fetch.
   Differences found while building: FRED's release ids were not in `MacroEventCommand.cs` (it reads the
   extracted date files), so the seed script records the standard ids 10/50/46/53; the FRED inputs run
   from 1947–49 rather than 1990, and the seed keeps those older day-only dates; the chart's zone is the
   market's (US Eastern for stock-session markets, UTC otherwise) rather than inferred per chart from
   `CipherCFutureClock`, which needs a long bar run and answers null on a daily stock chart; replay keeps
   all bars in `ReplayService` only as its private history and dispatches a growing PREFIX, so the
   ordinary rule 7 already excludes later events and no replay branch was needed.
2. **Live dates and actual figures — BUILT 2026-11-18, not yet heard by Cody.** FRED fetch with the
   user's key, saved to disk with its fetch time; first-print actuals with the previous figure; "hot"
   direction per release; the landing sound. What was built: the seed carries figures for CPI (with core
   CPI), payrolls, unemployment and GDP, computed from ALFRED vintages visible on each release day and
   aligned by index with the date arrays; the model stores each figure's unit and its hot direction,
   though nothing speaks "hot" yet because there is still no expected figure. A `MarketEventFredFeed`
   singleton on both heads fetches release dates (ids 10/50/46/53) and observations for CPIAUCSL,
   CPILFESL, PAYEMS, UNRATE, GDPC1 and PPIFIS, computes figures with the same rules as the seed's Python
   builder, merges them over the seed without removing a date, and caches what was fetched (never the
   key) under `market-events/fred.json`. The bar-map stamp includes the feed's version. The settings
   fieldset gains "Sound on event bars", a status paragraph and an "Update release dates now" button.
   The landing sound is a three-partial inharmonic bell — two falling strikes for High, one F5 strike for
   Medium and Low, quieter for Low — deduplicated per bar, delayed 130 ms after a text label, silent with
   the level off, the new box unticked or Shift+F3, and fired by playback as it crosses bars under the
   `MarketEvent` key in the Sound Designer. Differences found while building: ALFRED refused automated
   requests for PPI on 2026-11-18, so PPI has no figures in the seed and its history comes only through
   the user's FRED key, from the PPIFIS headline start in February 2014; the seed's newest economic
   release date is 30 July 2026 (GDP), not the 14 July 2026 example in the handoff; and the live fetch
   could not be tried against real FRED because this environment has no network. Correction and
   seasonal-revision days were also dropped in a follow-up: a date is kept only before the key
   series' first vintage, or when a vintage starts that day and — except for GDP — its newest period
   is newer than the previous kept date's. GDP keeps every GDPC1 vintage start because second and
   third estimates are real releases. That removed 19 CPI, 7 jobs, 48 GDP and 16 PPI dates; the same
   rule runs in the live feed for fetched dates on or before the fetch day, while future dates stay
   unchecked. The 16 PPI February drops are inferred from CPI's pattern, not checked against PPI
   data, because PPIFIS has no offline vintages.
3. **Live — BUILT 2026-11-18 (3a and 3b), not yet heard by Cody.**
   - **3a BUILT 2026-11-18, not yet heard by Cody.** Countdowns before an event that touches an
     open chart or a held position; the position-aware line; the release read-out at T + 60 s with
     the focused chart's price reaction first; the FRED figure asked for at T + 2, 5, 15, 30 and 60
     minutes; the stage 2 bell with every notice; a desktop toast when the window is hidden; the
     "Countdowns before events" setting (Off, 1, 5 and 1, 15 and 1 — the default — and 30, 15 and
     1). Everything goes out on the alerts tier, so Shift+F2 mutes it and F2 does not. The planner
     and the wording are pure Core classes; `MarketEventLiveNotices` owns the one-second
     `TimeProvider` timer, the fired set, the focused chart's recent closes and the rare broker
     reads. It is registered singleton on the MAUI head, scoped on the WebHost, and resolved at
     startup.
     **Differences found while building:** the planner takes open and held markets as two sets,
     because a position's market alone has to be enough (and so a test can prove the held half);
     the position line reuses `QuantityFormatter` and `SpeechPriceFormatter`, so the stop is said
     as "89000.00" rather than the handoff's illustrative "89,000"; the read-out compares the
     newest close in the first minute (bounded at T + 60 s) at the read-out tick, and requires the
     first tick within 5 s of T or says nothing about price; positions are read on the first
     countdown, then at most every five minutes around a read-out, never on a plain tick; the
     single-release FRED request skips a try when a full refresh is in flight rather than waiting;
     `MarketEventCalendar` gained a multi-market range overload so the live tick walks the
     calendar once instead of once per market; and the toast rides the existing Alerts category
     rather than adding a fourth. The browser-closed delivery promised above is **not** done: it
     needs one process-wide owner, as the alert monitor has, or a closed tab and the headless
     session would both speak it. It is in `docs/TODO.md`.
   - **3b BUILT 2026-11-18, not yet heard by Cody.** The Alt+E calendar screen: days as
     headings, the FRED status sentence, range/importance/kind filters and "only touching my
     charts and positions"; one button per event line with Enter taking the chart to its bar
     through the apostrophe jump's own navigation, or a spoken "not on this chart's bars" when
     it is not; "alert me before" as a toggle that writes the watched ids, which then skip the
     level, kind, importance and touching tests and fall back to 15 and 1 when countdowns are
     off; and a History disclosure with the last six releases of the same source, each with its
     figure and the focused chart's move over its bar. The words are pure static
     `MarketEventCalendarWording`; the watched list is pruned of events more than two days past
     whenever it is written.
     **Differences found while building:** the handoff's history example says "BTC fell 1.2% by
     that bar's close", but Alt+Shift+D has always said "Price fell 1.2% over this bar"; the
     history reuses that sentence verbatim rather than introducing a second wording. The handoff's
     event-line example says "previous 2.9", while the existing figure clause carries the sign and
     the unit ("previous up 2.9%"); the calendar uses the existing words, so "previous 2.9" reads
     with its direction and percent. At that stage there was still no expected figure and no
     surprise word; stage 4a supplied both. The history price comes from the focused chart only. The held-market half of "only touching" is the live service's last position read,
     exposed read-only rather than re-read by the screen; before the first read it is empty, and
     the live service now keeps ticking while a watch exists even at Level Off.
4. **Earnings, Kalshi expected figures, token unlocks.**
   - **4a BUILT 2026-11-18, not yet heard by Cody.** Expected figures from Kalshi for CPI,
     core CPI, payrolls, unemployment and GDP, with the surprise word; the pure
     `KalshiExpectedFigures` rules and the keyless `MarketEventKalshiFeed` with its own cache,
     status line and rate limit; expected merged into the calendar, bar map, detail, read-out,
     countdown and Alt+E screen. The facts and rules are in section 1 above. **Differences found
     while building:** the seed ends in July 2026, so the upcoming path depends on FRED's future
     dates (as stage 3a already did); an upcoming release whose FRED event carries no figures
     gains an expected-only figure row so the countdown can say it; with no Kalshi market the
     event is left exactly as stage 3b had it.
   - **4b OPEN: earnings (Finnhub, Cody's key).**
   - **Token unlocks BLOCKED:** DefiLlama's emissions/unlocks API now answers "Upgrade to the
     paid API plan" (checked 2026-11-18); needs a paid source or Cody's decision.
   - **Fed expected rate OPEN:** Kalshi has KXFED, but our Fed events carry no figures yet, so
     stage 4a deliberately leaves them alone.
5. **Pulse — 5a BUILT 2026-11-18, not yet heard by Cody**, then the change log and catch-up. The
   watchlist named "Pulse" seeded from the nine-item table and edited in Alt+M; the fixed order;
   held positions appended for the reading but never saved; one sentence with percent changes,
   basis-point yields, closed sessions, minutes-old quotes and "unavailable"; the four-second
   limit, the one-second "Gathering the pulse.", and the Interface speech tier neither mute
   silences; and the "Pulse: crypto change measured from" setting (midnight UTC or last 24 hours).
   The pure halves are `MarketPulseDefaults`, `MarketPulseMath` and `MarketPulseWording`; the live
   half is `MarketPulseService`, singleton on MAUI and scoped on the WebHost.
   **Differences found while building:** `MarketType` has no CFD or ETF member, so S&P searches
   Index then Stock and cannot try a CFD/ETF market the way the handoff's parenthetical imagined;
   the US-hours closed rule has no holiday calendar of its own and relies on the stale-minute-bar
   test, so a holiday with a fresh-looking bar is not detected (review fix: a missing daily bar no
   longer closes a market whose minute bars are fresh, and "today's bar" is the bar in progress,
   not the bar whose UTC date is today, because Oanda stamps forex at 17:00 New York the evening
   before);
   Forex/Commodity use the Friday-to-Sunday window only and do not use the equity hours; symbol
   resolution reads each provider's own symbol list but was never run against live providers in
   this build, and the candidate spellings are the fixed table above.
   **5b BUILT 2026-11-18, not yet heard by Cody.** The change log and Alt+U. The spoken order is
   money, alerts, events released, unusual moves, signals, connection problems; stage 5b builds
   the four whose events already exist (money, alerts, the release read-out, connection problems)
   and leaves the `Unusual` and `Signal` categories in the enum and ordering for 5c. The top five
   are read per press, then *"Press Alt+U again for N more."*; the caught-up mark moves past the
   spoken items only. The optional return prompt appears after five minutes away (default on) and
   only offers. Settings → Narration gains **Offer a catch-up when I come back**
   (`changeLogOfferOnReturn`). The pure halves are `ChangeLogRules`, `ChangeLogWording` and the
   per-event mapping; the live half is `ChangeLogStore` (one per-path lock, `change-log.json`),
   `ChangeLogRecorder` (per circuit, in the headless session and at MAUI startup) and
   `ChangeLogCatchUpService` (Alt+U and the prompt). **Differences found while building:** the
   alert de-duplication key uses the fired time to the minute because `AlertFired` carries no bar
   time; order events use `OrderUpdate.Timestamp`; `MarketEventNoticeEvent` needed the optional
   `MarketEventNoticeKind` the handoff predicted, and the publish site now passes it so a countdown
   is not recorded; the MAUI head has no single foreground hook, so its existing `Window.Resumed`,
   `Stopped`, `Destroying` and the tray's visibility callback carry the away mark and the later
   offers, while the launch and circuit-open offer runs in `AppStartupService` once speech is
   listening (review fix: `Created` and circuit open were too early to be heard); and the hosted monitor appends its alerts through the same per-user scope's store
   because that head has no headless bus.
6. **The Go bar.** Most useful once there are more places to go; it touches every action, so it is the
   biggest change and goes last.
   - **6a BUILT 2026-11-18, not yet heard by Cody.** The registry of every action, the guard test
     that keeps it complete, the fixed-tier matcher, the service that closes the box before it
     dispatches, the command and Alt+G binding, and the ARIA 1.2 combobox box. Money entries are
     exact-code or exact-full-name only. **Differences found while building:** the enum had 140
     members including `None`, not the handoff's "about 173"; `CloseModal` has no binding of its
     own by design (Escape is bound to `CancelDrawing` and re-routed by the dispatcher), so its
     key-only sentence takes Escape from that live binding; `KeyOnly` entries carry no short
     codes, because a code that only exists to be refused is noise, while their names and aliases
     are still matched; and the service speaks the key in the app's existing words ("Alt plus P")
     through `ShortcutWords`, rather than the handoff's illustrative "Alt+P".
   - **6b OPEN: commands with arguments.** `TF 4h`, `IND rsi`, `ALERT 95000`, `GOTO 2024-03-01`,
     `GOTO CPI`, `FIND hammer`, symbols, and words in either order (`BTC GP` / `GP BTC`).
   - **6c OPEN: the rest of the typed surface.** Settings by name, `HELP <code>`, and the typed
     money commands `BUY` / `SELL` / `FLAT` through the spoken order review. Open question: the
     backtick as a second opener ("maybe as well" from the 2026-11-18 discussion) is not built and
     is not yet a decision.
