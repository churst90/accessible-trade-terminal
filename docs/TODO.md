# TODO — Accessible Trading Terminal

This file lists what is LEFT to do. What was done, with its caveats, is in `docs/CHANGES.md`. Everything
that used to be in this file (finished items, old audits, superseded START HERE blocks, old phase plans) was
moved on 2026-11-04 to `docs/archive/TODO_HISTORY.md`, unedited. Line numbers below like "archive line 9300"
point into that file.

## How to work from this file

The rule that has held since 2026-08-25: **demonstrate the defect, or mark it explicitly unverified.**
Reading code for bugs filed 283 findings in 2026-08 and about half were wrong; compiling the escape or
running the sabotage is what found real ones. Test one thing, write down what the test could not show.

The agreed order, unchanged:

1. The first section below is trading through the venues, which is BUILT but has not been tried on a
   demo, practice or real account. Treat it as unverified until an account answers. Everything else built
   up to 2.13 was seen and heard by Cody on 2026-11-16.
2. Then Cody's open questions (marked **Q** in the second section).
3. Then the older audit findings in the last section. None of them has been re-checked since it was
   filed (2026-08 and earlier), and many may have shipped since: check CHANGES and the code before taking one.

Standing facts: the full test suite runs on GitHub Actions at the push, not locally; build in a worktree,
never in the main checkout under a running server; the DBSI study is closed (Cody, 2026-11-17).

## Code review findings left (2026-11-20)

Cody, 2026-11-20: "start on each of the issues you found, in the best order or smallest to largest."
The review is in `strategy-lab-data/reviews/att-review-2026-11-20/`; what is fixed is in CHANGES
("Code review fixes, 2026-11-20"). Left, smallest first:

- [ ] **Kill switch** (Cody, 2026-11-20: "kill switch, add this to the todo"). Nothing caps the day
  or the account; for a trader who cannot see the screen "one more trade" is the failure this exists
  to stop. Suggested in two steps, design to agree with Cody before building:
  1. **Close everything** on one key: cancels every working order and closes every position on the
     focused venue (then optionally all venues), after one spoken confirmation that says what it will
     do ("Close 3 positions and cancel 5 orders on MEXC?"), and reads back what each venue answered.
     Close-position and cancel already exist per row; this is the one-key version.
  2. **Daily loss limit** you set yourself (an amount or a percent of the account): once the day's
     realised loss reaches it, new entries are refused out loud until the next day; stops, targets
     and closing trades still work. Open questions: the day's boundary (midnight local or UTC),
     whether unrealised loss counts, and whether it can be lifted early.
- Declined for now (Cody, 2026-11-20: "restore from yesterday, nah, I'm good on this for now"): a
  **Restore from yesterday** button. The daily copies exist; going back is a hand copy.
- [x] The **order log** is read in the app: trading dashboard → **Order log** tab (2026-11-20, CHANGES).

## CI flakiness seen 2026-11-20

- [ ] c0b44e9e's xunit needed three attempts: first `ReconnectingWebSocketBehaviourTests.The_heartbeat_sends_the_configured_keepalive_payload`
  (the loopback connect hit its 10 s limit), then three bUnit waits (`SettingsModalTests` focus and factory
  reset, `TradingDashboardDecouplingTests.Close_at_limit_...`). Different tests each time, all green locally
  three runs out of three, suite time unchanged (5 to 6.5 minutes). Watch for a repeat before changing the
  waits; if one test keeps showing up, that is the one to look at.

## Website (accessibletrader.com), Cody's notes 2026-11-20

The site's files live on the server (`/var/www/html/trade.codyhurst.com`), drafts in
`strategy-lab-data/site-drafts/`; what was changed is in CHANGES.
- [ ] **"Take the audio tour"** needs redoing to show the different kinds of data (Cody: "leave it
  alone for now but flag for later").
- [x] Title, "What it does" as eight plain-sentence sections, "Why financial literacy matters" moved
  down by the story (2026-11-20, CHANGES).
- [ ] **News headlines** for the News and fundamentals section: the terminal has the events calendar
  and the asset dossier but no headline feed, so the site does not claim one.
- [ ] **Speech input**: Cody's note lists speech as a way in; the terminal speaks but does not
  listen, so the site says keyboard or touchscreen in, speech, sound or braille out.
- [x] Cody's answers to the advice (2026-11-20): one main button at the top, the why line under the
  heading, one stat line under the buttons, the leaderboard as a reason to sign up with the heading
  "Top traders: paper trading leaderboard" (CHANGES). Skipped by Cody: replacing the feature list with
  three steps (keep the list), testimonials and the YouTube video (coming later), a "coming soon" line
  (not yet).
- [x] **Account value on the leaderboard** and the **gold edge on dark-mode main buttons**: Cody said
  yes, built 2026-11-20 (CHANGES). Market Cipher taken off the home page (Cody: not to be advertised as
  clones).
- [ ] Phone: the leaderboard table's caption runs off the right edge with the table (scrolls with it;
  true before 2026-11-20).

## Accounts, leaderboard and charting asks (Cody, 2026-11-18)

Agreed order: the toolbar fixes (done 2026-11-18, CHANGES), then the account page, the leaderboard and
the P&L seed, then market events stages 4 to 6, then the charting items below "as outlined". The accounts
stay on the terminal's own ASP.NET Identity (not the PHP sites' logins): different registrable domains,
and Identity already has 2FA, lockout and rate limits.

- [x] **Email through Brevo (Cody chose it, 2026-11-18).** Free plan, 300 a day. The terminal writes
  every email itself (password reset, confirm a new address, "your password/email was changed" sent to
  the old address); Brevo only delivers. Cody: Brevo account, add `accessibletrader.com` under Senders,
  Domains, copy its records into NameSilo (brevo-code TXT on @, `brevo1._domainkey` and
  `brevo2._domainkey` CNAMEs, `_dmarc` TXT), make an SMTP key, and put it on the VPS. Sender
  `no-reply@accessibletrader.com`; the domain has no mailbox, so say "do not reply". Check the first test
  email for Brevo branding. Self-hosting was checked and NOT chosen: port 25 is open from the VPS
  (Gmail answered), but a new IP's deliverability and the shared IP of every hosted site are the risk.
  Done 2026-11-18: server side by Cody and Claude; this run wired the app
  (`IAccountEmailSender`/`SmtpAccountEmailSender`, ForgotPassword, change-email confirm, notices).
  Both settings went on the VPS 2026-11-20 and one real password-reset email was sent through Brevo
  (the app logged it as sent). Cody, 2026-11-20: "Yes i did receive the password reset email. No
  brevo branding that I could tell." Nothing left.
- [x] **Account page.** Display name, change password, change email (needs the email above), leaderboard
  opt-in (OFF by default), download my data (a zip of the user's folder), delete account; and a minimum
  fill time on the register form's honeypot. Built with its tests on 2026-11-18. **Left:** the
  leaderboard itself (next item) reading the stored display name and opt-in.
- [x] **Leaderboard on the accessibletrader.com front page.** Per `~/ATT-ACCOUNTS-LEADERBOARD-SPEC.md` on
  the VPS: a pure calculator, a publisher every 5 minutes writing `public/leaderboard.json` atomically,
  opt-in only, display names never emails, ranked by return %, "be the first" under 3 entries, a real
  `<table>` with caption and column headers in the PHP page. Paper only (the hosted terminal is paper).
  **Server side done 2026-11-18 (`CHANGES.md`):** lifetime stats, the calculator and the publisher.
  Open positions on every row, and an account listed once it holds one, done 2026-11-20 (Cody).
  Deployed 2026-11-20 with `Leaderboard__OutputPath` set; the first file was written (no one has
  opted in yet, so it is empty and the page shows "be the first").
- [x] **Website leaderboard section (VPS PHP, Claude).** Render the published JSON on the
  accessibletrader.com front page: a real `<table>` with a caption and column headers, "be the first"
  under 3 entries, no email address anywhere. The file, its 0640 mode and
  `Leaderboard__OutputPath` are documented in `SERVER_SETUP.md`; the publisher is the part this
  repository owns. Installed 2026-11-20 as `includes/leaderboard-section.php`, after the free-account section,
  with a contents link; the open positions column lists each holding. Not yet seen with real rows:
  it needs three opted-in accounts.
- [x] **Seed Cody's P&L.** Cody, 2026-11-20: "Yes, copy my trade data including the open short from my
  open trade to the leader board." Done 2026-11-20 (CHANGES). **Left, Cody:** on the hosted terminal's
  Account page, set a display name and tick the leaderboard box; until both are set the publisher leaves
  him off. The site shows the table only from 3 entries; under that it shows the invitation.
- [x] **"Add label here..." in the chart's context menu (Application key / right-click).** Done
  2026-11-18: the chart menu resolves one "here" (bar, price, pane) for mouse and keyboard, places the
  label through the same prompt as Alt+Shift+L, and also adds the other context-menu items requested
  for this pass (alerts, limit tickets, copy, replay, remove all, per-series actions). The limit items
  only prefill the dashboard; they never place an order.
- [x] **Text labels anywhere, as on TradingView, including another pane.** Done 2026-11-18 for text
  labels and horizontal lines: they carry a pane and a price, use the focused/clicked pane's own
  units, render/speak/nudge/save there, and survive the indicator's removal. What is left: the other
  drawing types (trend line, Fibonacci, rectangle, etc.) still stay on the price pane.
- [ ] **"Add symbol (own pane)".** A third Compare mode: the series' own values in its own pane, so any
  of the 17 analytics sources (FRED, Glassnode, DefiLlama, …) can sit under a price chart without a
  wrapper indicator each. Plus a picker using the toolbar's Market, Analytics type, Provider, Symbol
  choices instead of typed text. Analytics stay providers (TradingView treats them as symbols too).
  Caveat to solve: FRED dates a figure by the period it measures, not its release, so a forward-filled
  CPI would show weeks before it was known; it needs a release delay or ALFRED vintages. Untested
  today: whether the existing Compare already accepts an analytics market.
- [ ] **Formula charts (BTC/ETH, spreads, baskets).** A "Formula" source in the Market list: a builder
  (leg, operator, leg, add a leg) plus a typed form with quoted symbols, `Bitstamp:"BTC/USD" /
  Bitstamp:"ETH/USD"`, since our symbols contain "/". Legs fetched on the chart's timeframe, matched by
  time, last value held when one market is closed. Open and close exact; high and low by the formula on
  each part (as TradingView) and kept valid, said in the manual to be approximate; no volume. Both live
  streams feed the last bar. Never tradeable, paper included. Today's nearest thing is "Compare symbol
  (ratio)", a close-only line.
- [x] **The context menu items (Cody: "all recommended options", 2026-11-18).** Built 2026-11-18
  (DeepSeek, branch `att-ctx-menu`, merged c52e20ab with Claude's fix; see the "Add label here" item above): add label here, horizontal line here, alert at this price, buy /
  sell limit here (opens the dashboard filled in, never places), copy price, copy this bar, bar
  replay from here, add indicator, object tree, remove all drawings (asks first); per series: alert
  on this line, copy value, move to a new pane. Each names its shortcut.
- [x] **Colour said the same way everywhere (Cody: "consistency", 2026-11-18).** Done 2026-11-18:
  histograms and volume bars say "{colour} bar" as lines do (on arrival, then only where it
  changes); heatmap cells say dark/medium/light from the alpha the renderer paints, with silence on
  an empty cell; all behind the existing opt-in look setting. Renderer and words share one colour /
  alpha function, pinned by pixel tests. What is left: the heatmap shade is a third of the 0–255
  alpha range, so it is relative to the chart's heatmap volume multiplier rather than absolute
  liquidity, and no new colour words were invented for display types that are not bars or lines.
- [x] **Profiles split into buying and selling, as TradingView draws them (Cody, 2026-11-18).** Done
  2026-11-18: `ProfileBin.BuyVolume`/`SellVolume`; a bar counts as buying when it closed up and
  selling when down, with TradingView's doji-vs-previous-close rule and half each when that is
  equal. Rows are drawn in the theme's bullish and bearish colours, spoken as "62 percent buying" /
  "even", and sound a triangle when buyers lead, sine otherwise, keeping pitch by part and grit by
  size. TotalVolume, POC, value area, levels and backtests are pinned unchanged; TPO rows have no
  split. What is left: this is a split by **bar direction**, not real trade-by-trade buying; proper
  delta needs lower-timeframe or tick data, and the profile is not split per trade or per price
  inside a bar.
- [ ] **"9-13 sequential" (Cody named it, 2026-11-18).** A DeMark-style count: setup 1 to 9 (close
  against the close 4 bars back), then countdown 1 to 13 (close against the low/high 2 bars back),
  with perfection, cancellation and recycle rules to settle from the published descriptions and to
  check against a TradingView community script before calling it exact. A tick per count, a distinct
  tone at 9 and at 13, spoken "Buy setup 9" / "Sell countdown 13". Not called "TD Sequential" (a
  DeMark trademark).
- [ ] **TradingView gaps, Cody's pick 2026-11-18 ("1, 2 and 3 for right now"):**
  1. **More chart types.** **Line and area** — built 2026-11-19, not yet heard by Cody (toolbar
     Chart type select and Go bar `CANDLE` / `LINE` / `AREA`; `PriceChartStyle` in the Sdk).
     **Renko** — built 2026-11-19, not yet heard by Cody (toolbar select, Brick settings dialog,
     Go bar `RENKO` / `bricks`; shared `BrickChart*` plumbing). Cody 2026-11-19: Price stays an
     optional overlay; background Renko tabs announce their bricks (built 2026-11-19, not heard).
     Same day, Cody: the series kept the name "Candles" and the line read values only → the price
     series is now named after the chart type and reads "Close. line. X. …" / "Brick. Up. X to Y."
     (built, not heard). Open: is the brick's direction right in the type slot?
     **Range bars and line break** — built 2026-11-19, not yet heard by Cody (toolbar select,
     per-type settings dialog, Go bar `RANGEBAR` / `LINEBREAK` / `LBREAK`; one-door
     `BuiltCharts`; line break reads as "Block"). **Alerts split** (Cody's yes 2026-11-19) built
     the same day, not heard: price, candle and drawn-level alerts read the real price on all three
     built types, indicator alerts the bricks, and Candle gains New up / New down / Reversal (the
     chart's own bars; not watched by the hosted no-chart monitor). Caveat: restoring a workspace across built
     types re-resolves the size. **Kagi** — built 2026-11-19, not yet heard by Cody (toolbar select,
     Kagi settings dialog, Go bar `KAGI 500` / `KAGI ATR 20`; thick/thin drawn and spoken, live
     "Kagi turns thick at X." once per line). Cody's two answers (2026-11-19, "thickness audible,
     yes if possible" / "yes percentage adjustment too") built 2026-11-20, not heard: a thick line
     sounds heavy (square edge and sub-octave weight), a thin line nearly a pure sine, direction
     still the pitch; and a **Percentage** reversal (dialog option, Go bar `KAGI 2%`), that
     percentage of the line's end. **Point and figure** — built 2026-11-20, not yet heard by Cody
     (toolbar select, settings dialog with Box and Reversal 1–20, Go bar `PNF 500 3` /
     `PNF ATR 20`; X/O marks drawn, "Column. Up, X, 4 boxes." spoken, heavier sound for taller
     columns, each new box said once live, background tabs say a column that grew). Cody's three
     answers (2026-11-20) built the same day, not heard: **High and low** as the source (dialog
     Source, Go bar `PNF 500 3 HL`), a **percentage box** (TradingView's "Percentage (LTP)", Go
     bar `PNF 1%`; frozen at the price when chosen, TradingView's follows it), and spoken **buy
     and sell signals** ("Buy signal, above the last X top at 105.00"; chart formations
     untouched). **Bars, hollow candles, baseline, high-low, HLC area and volume
     candles** (Cody's yes, 2026-11-20) — built 2026-11-20 and TRIED by Cody ("everything sounds
     good"); his three findings fixed and re-heard the same day ("I heard the fixes, approved":
     newest high-low bar silent, focus jumping to the body on a tick, point and figure fetching all
     history) (toolbar select,
     Go bar `BARS` / `HOLLOW` / `BASELINE 95000` / `HILO` / `HLC` / `VCANDLE`; each drawn, heard,
     spoken and on the status line as its own shape; baseline clicks on crossing its base).
     The baseline's base is fitted into the price axis (2026-11-20, not seen). Caveat: volume words weigh the 20 bars
     before, not the view. Step line, line with markers and columns skipped. The brick and box types drop
     time, so the bar cursor, playback and speech must say a brick's own start and end time, and
     indicators run on the bricks.
  2. **Percent and indexed-to-100 price scales.** **Built 2026-11-19, not yet heard by Cody**
     (toolbar Price scale select and Go bar `REG` / `PCT` / `IDX`; `PriceScaleMode` in the Sdk).
     Percent from the first visible bar (TradingView's), indexed to 100 the same way; speech says
     "plus 4.2 percent" / "index 104.2"; COMPARE rebases to its own first visible value. Add symbol
     and the formula charts are still open, and the scales have not been heard by Cody.
  3. **Indicator templates.** **Built 2026-11-19, not yet heard by Cody** (toolbar workspace
     group, Go bar `TPLS` / `TPL`, `IndicatorTemplates/<name>.json`). Replace-only; see
     "Indicator template (Cody, 2026-11-05)" below for the details and caveats.

  Not picked for now: several charts on one screen with linked symbol and crosshair; extended-hours
  sessions for stocks.
- [ ] **Full Pine Script in the script editor (Cody, 2026-11-19: "add that to the todo to be done
  later").** Its own track, after the charting items above. Today `Core/PineScript/PineTranspiler.cs`
  rewrites Pine to C# with regex patterns; that cannot follow indented blocks, user functions,
  types/methods, arrays/maps/matrices, `[n]` on any expression, `var`/`varip` or tuples, and
  `line.new`, `label.new`, `strategy.*` and `request.security` are stubs. Nothing in the language
  needs TradingView's internals; it is documented. The order:
  1. **Conformance bank first (cheap, can go alone):** 30–50 popular open-source indicators run
     through today's transpiler and compared value by value with TradingView's own export, the
     way Stoch RSI and Cipher B were matched. This measures what "some may not work" really is.
  2. **A real parser + bar-by-bar engine**, compiled to C# for the existing sandboxed worker:
     per-value history, `var`, `varip`, live-bar rollback and re-run per tick.
  3. **The built-in library** (`ta.*`, `math.*`, `str.*`, `array.*`, `map.*`, `matrix.*`, types),
     matched against the bank, including seeding and `na` handling.
  4. **`request.security`** with Pine's documented no-lookahead rules for higher timeframes.
  5. **Drawings** (`line`, `label`, `box`, `table`, `polyline`) on the drawing layer, each with a
     spoken form (a table read out, a label as an event).
  6. **Strategies**: an emulator of TradingView's documented broker (fill rules, intrabar order
     path, pyramiding, commission, slippage, margin calls).

  Can never work, and must be said in words when met: invite-only/protected scripts (no source),
  `import user/lib/N` without pasted source, TradingView-only data (`request.financial`,
  `request.economic`, `request.seed`, `EXCHANGE:SYMBOL` ids that do not map to a provider), and
  tick-exact live-bar behaviour (their feed's ticks, not ours; closed bars can match exactly).
  "Pine Script" is TradingView's trademark: describe it as "runs Pine Script v5/v6 indicators".
  Replaces the old "Pine dialect fidelity was not assessed" line below once started.

## Market events, market pulse and the Go bar (Cody, 2026-11-18)

The plan, the reasons and Cody's decisions are in `docs/MARKET_EVENTS_PULSE_GOBAR_DESIGN.md`. Built in
stages by DeepSeek V4.1 Flash through OpenCode (Cody: saves Claude usage); Claude writes each handoff,
reviews, re-runs the tests and sabotages, and merges locally. Docs and the user manual with every stage.
Decisions: an event sits on the bar of its own day, as TradingView does (after-close earnings on the
report day); events OFF by default; earnings from Finnhub (Cody is getting the free key); the pulse list
as proposed.

- [x] Stage 1: events on bars, the past (seeded US release dates and Fed decisions, rule-worked expiries,
  holidays and halvings; the placement rule; the setting, off by default; the arrow clause; Alt+Shift+D;
  apostrophe jumps). No figures, no live fetch. **Built 2026-11-18; Cody tried it the same day: "this works amazing."**
- [x] Stage 2: live FRED dates with Cody's key, first-print actual figures, "hot" direction, landing
  sound. **Built 2026-11-18, not yet heard by Cody.** Figures are in the seed for CPI (with core),
  payrolls, unemployment and GDP; PPI needs the key. Caveats: the live fetch was never tried against
  real FRED (no network in the build environment), PPI history before 2014 has no figures, ALFRED
  refused automated requests for PPI so its history comes only through the key, and nothing here has
  been heard by Cody yet.
- [x] **Stage 3a: live notices.** Countdowns and position-aware warnings, the release read-out
  (price reaction first), the FRED figure at T + 2/5/15/30/60 minutes, the bell with every notice,
  the desktop toast when the window is hidden, and the "Countdowns before events" setting.
  **Built 2026-11-18, not yet heard by Cody.** Caveats: no delivery while the browser is closed
  (see the item below); for CPI, jobs, PPI and GDP future dates come only from FRED, so without a
  key the only future countdowns are Fed decisions; the price reaction is the focused chart only;
  FRED can lag, so the figure may never come within the hour.
- [x] **Stage 3b: the Alt+E calendar.** Days as headings, range/importance/kind/only-touching
  filters, Enter to the event's bar, "alert me before this" (the watched ids) and the release
  history (the last six releases of the same source, with each figure and the focused chart's
  price move). **Built 2026-11-18, not yet heard by Cody.** Caveats: its history's price part is
  the focused chart only, and watched alerts are not delivered while the browser is closed (the
  item below). Expected figures and the surprise word arrived in stage 4a.
- [ ] **Live market-event notices while the browser is closed.** The design doc promised the
  alerts route; stages 3a and 3b only speak while a session is open, including a watched event's countdowns. Doing it needs ONE process-wide
  owner, as `LocalBackgroundMonitor` and `CircuitAlertCoverage` are for alerts: with a plain
  second subscriber, a closed tab's circuit (alive for about three minutes) and the headless
  session would both speak every countdown and read-out, and the bell would ring twice. Not done
  yet, deliberately.
- [x] **Stage 4a: Kalshi expected figures.** What Kalshi's prediction markets priced before CPI,
  core CPI, payrolls, unemployment and GDP, said with its source (*"expected 0.3, by Kalshi"*),
  and the surprise word wherever an actual and an expected both exist. **Built 2026-11-18, not
  yet heard by Cody.** Caveats: those five figures only; history only as far as Kalshi's markets
  (about mid-2021); Kalshi's traders, not economists, so it can differ from the consensus quoted
  in the news; an upcoming release's expected moves until the release; the live fetch was never
  run against the real API from the app (the fixtures are real responses saved 2026-11-18).
- [ ] **Stage 4b: earnings (Finnhub, Cody's key).** Open; needs the key.
- [ ] **Token unlocks: blocked.** DefiLlama's emissions/unlocks API now answers "Upgrade to the
  paid API plan" (checked 2026-11-18), so it needs a paid source or Cody's decision.
- [ ] **Fed expected rate.** Kalshi has KXFED, but our Fed events carry no figures; open, and
  deliberately not in stage 4a.
- [x] **Stage 5a: the market pulse (Alt+P).** The "Pulse" watchlist, seeded from the nine-item
  table and edited in Alt+M; fixed order; held positions appended for the reading only; one
  sentence with percent changes, basis-point yields, closed sessions and minutes-old quotes; the
  four-second limit and the one-second "Gathering the pulse."; and the "Pulse: crypto change
  measured from" setting. **Built 2026-11-18, not yet heard by Cody.** Caveats: the four-second
  limit, the US-hours closed rule's reliance on a stale bar instead of a holiday calendar, index
  CFDs treated as closed outside 09:30–16:00, a fixed symbol-candidate table never probed live,
  and the browser-closed delivery limits above.
- [ ] **Stage 5a follow-up: the compact braille line for the Dot Pad.** The design's one-line
  tactile summary of the pulse is not built.
- [ ] **Stage 5a follow-up: a detail read of the pulse.** Each item's source, reference and time
  on demand; not built.
- [x] **Stage 5b: "what changed" (Alt+U).** The change log and catch-up for the four
  categories whose events exist: money (fills, stops, take-profits), alerts, market events
  released and connection problems. It keeps the latest 500 entries for seven days under
  `change-log.json`, de-duplicates a fill seen by a circuit and the headless session, reads the
  top five per press in category order with "Press Alt+U again for N more", and marks only the
  spoken items heard. "Offer a catch-up when I come back" (default on) appears in Settings →
  Narration and only offers after five minutes. **Built 2026-11-18, not yet heard by Cody.**
  Caveats: the sound was never heard; a market event released while the browser is closed is
  still not recorded (the existing process-wide-owner item above); and the hosted head records
  only its alerts, with in-circuit items through the circuit recorder.
- [ ] **Stage 5c: unusual moves.** A producer for the `Unusual` category: each asset against its
  own normal swing over the same interval, not a fixed percent. Not built in 5b.
- [ ] **Stage 5c: signals and formations on watched charts.** A producer for the `Signal`
  category, which needs a background signal evaluator. Not built in 5b.
- [ ] **Stage 5c: position profit and loss since you left.** The change log currently records
  money events (fills, stops, take-profits), not the mark-to-market move of open positions.
- [ ] **Stage 5c: market events released while the browser is closed.** The same blocker as the
  stage 3a item above: one process-wide owner is needed, or a closed tab and the headless
  session would both speak/record it.
- [ ] **Stage 5c: the morning brief.** Catch-up, pulse and today's events. Not built.
- [x] **Stage 6a: the Go bar's registry, matcher and box (Alt+G).** One entry for every
  `SystemCommand` except `None` (140) behind a guard test that goes red when one is removed; the
  fixed order exact code → exact name/alias → prefix → word start → near miss; money exact-code or
  exact-full-name only; the close-then-dispatch entry point; and the ARIA 1.2 combobox with the
  polite read on the Interface tier. **Built 2026-11-18, not yet heard by Cody.** Caveats: no
  arguments, no settings by name, no typed `BUY`/`SELL`/`FLAT`, and the box has not been heard.
- [x] **Stage 6b: Go bar commands with arguments.** `TF 4h`, `IND rsi`, `ALERT 95000`,
  `GOTO 2024-03-01`, `GOTO CPI`, `FIND hammer`, symbols, and words in either order (`BTC GP` /
  `GP BTC`); the four 6a silent-drop commands now have real handlers and a guard; one load path
  from the tab's identity, the shape-change refusal and the toolbar composer re-parse.
  **Built 2026-11-18, not yet heard by Cody.** Caveats: other venues are not searched (no
  in-memory cross-venue symbol list), `ALERT` opens the form rather than creating the alert, and
  6c (settings by name, `HELP <code>`, typed `BUY`/`SELL`/`FLAT`) is still open.
- [x] **Stage 6c: the rest of the typed surface.** Settings by name, `HELP <code>`, and the typed
  money commands `BUY` / `SELL` / `FLAT` through the spoken order review; the Settings registry
  moved to Core and `OpenSettingsEvent` can focus a named control. **Built 2026-11-18, not yet
  heard by Cody.** Caveats: the money verb is first-word-only, `FLAT` takes no symbol, and the
  handoff's `speech rate` example has no setting to reach in this build.
- [x] **Second Go bar opener — closed, Cody 2026-11-18: Alt+G only.** Neither the backtick nor
  `Shift+\` is bound. `Shift+\` was free, but on German and other AltGr layouts the backslash
  needs AltGr, so it is not one key there; and Alt+G already opens the bar. Nothing to bind.

## Trading not yet tried on a practice or real account

Cody, 2026-11-16: everything else built up to 2.13 has been seen and heard; trading through the venues
on demo or practice accounts has not. Later the same day: "I'll get to this as I can." Every item below is proven only against fixtures copied from each
venue's public answers or docs. The README and WHATSNEW say so.

- [ ] **Kalshi perpetual futures (2026-11-14; see CHANGES).** Not tried with money; no order placed,
  not even on the demo venue. Cody to make a demo account first.
  - Unverified until an account answers: whether `/margin/orders` lists only resting orders or
    everything (the terminal keeps those with a remaining count); the positions, risk, balance and
    fills shapes; whether `fill_count` is in an IOC's answer.
  - Stops and targets (2026-11-15) are Kalshi's exit triggers, also untried. Unverified: that setting
    an isolated bracket again replaces it; which margin type the main account is; the trigger answer
    shapes. They fire on the MARK price as an immediate limit near the level, so a fast market can
    leave part or all of it unfilled. A new entry's stop changes a bracket that covers the whole
    position (said aloud). Trailing stops are not built (listed and cancelled only).
  - Daily candles close at midnight New York time, not UTC; weekly and other timeframes are built
    from 1h or 1d by the terminal.
  - Prices are per contract; on a market whose contract is not one unit of the asset the chart is
    the contract's price, not the asset's.
  - Stock perps need Kalshi's stocks application; the refusal says so but the terminal cannot check
    first.
  - No websocket: the book and positions are polled.
  - A failed `/margin/risk` read fails the whole positions read (by design, nothing swallowed).

- [ ] **Coinbase US futures (2026-11-14; see CHANGES).** Cody is applying for a Coinbase futures
  account. Not tried with money; no order placed.
  - Unverified until an account answers: that `base_size` is counted in contracts (the product
    steps of 1 say so; the docs do not), the `cfm/positions` and `cfm/balance_summary` shapes,
    and what a spot-only account gets from them (assumed a 4xx).
  - Coinbase documents an attached bracket (`attached_order_configuration` with
    `trigger_bracket_gtc`) for spot and US derivatives. If it works, both Coinbase markets could
    attach a stop and target instead of refusing. Not built.
  - No liquidation price per position; the futures account's liquidation line
    (`liquidation_threshold`, `liquidation_buffer_percentage`) is in the balance summary and is not
    shown yet.
  - Only unexpired contracts are listed. Funding on the perps is not shown.
  - MEXC futures positions are in contracts but carry no `ContractSize` yet, so their percent move
    is off by the contract size, as Coinbase's was.

- [ ] **Stops on entries refused where the venue cannot attach them; Kraken Futures positions and sizes
  (2026-11-14; see CHANGES).** Not tried with money.
  - Kraken Futures could attach real legs (a reduce-only `stp` and `take_profit` after a market entry)
    instead of refusing. That needs a funded account to see how the venue treats a reduce-only stop
    while the entry is resting. Not built.
  - Kraken Futures gives no liquidation price per position, so the near-liquidation warning cannot
    fire there.
  - Unverified, not demonstrated: Kraken spot `CanonicalKrakenPair` keeps "USDTZUSD" whole (only
    pairs with BOTH halves prefixed are stripped). It may not match the "USDT/USD" spelling elsewhere.

- [ ] **Every other crypto venue: market rules, leverage as asked, read pacing (2026-11-10, built, NOT
  tried with money on any venue; see CHANGES).** Gemini, Binance, Coinbase, Bitstamp, MEXC and
  Hyperliquid.
  - Cody to try, wherever there is a live key: a tiny order under the venue's minimum should be refused
    in words before anything is sent; a 1x perp order after a higher one should open at 1x; leverage above
    a market's maximum should be refused and name it (Hyperliquid TAO 5, KAS 3 as read 2026-11-10). On
    Gemini, the dashboard should no longer slow an order down.
  - **Questions for Cody:**
    - Gemini: a market not "open" (limit_only, post_only, cancel_only) refuses EVERY order. Should a
      limit order go through on a limit_only market, and a post-only one on post_only?
    - Binance: an order at a different leverage from a position already open on that market is refused
      (Binance keeps one leverage per market and would change the open position too). Keep this, or
      change the position's leverage after asking?
    - MEXC: its API docs have marked futures order placement "under maintenance" since 2022. If MEXC
      futures are wanted, it needs a live try; spot is unaffected.
  - Unverified, needs a live account:
    - Every venue: no real order sent, so the formatting and refusals are proven against fixtures copied
      from each venue's public answers (2026-11-10), not against a real fill.
    - Binance answers 451 from here: spot fixtures are from data-api.binance.vision, futures from the
      testnet; leverageBracket and positionRisk follow the docs' shape; the mainnet futures BTCUSDT step
      and minimum value are from memory; whether a US account can reach Binance at all.
    - Coinbase: the private accounts paging (needs a login; follows the docs); base_max_size is read but
      not checked.
    - Bitstamp: whether instant (by-value) orders want `instant_order_counter_decimals` rather than the
      pair's counter decimals.
    - Hyperliquid: whether the $10 minimum applies to reduce-only orders (left to the exchange), and how
      it values a market order.
    - MEXC: leverage risk tiers above the contract's plain maximum.
  - Not done:
    - A market order's minimum VALUE is not checked on Coinbase, MEXC spot or Binance futures, because no
      price is to hand; the venue still judges it.
    - Binance hedge mode: the open-position leverage guard treats LONG and SHORT positions alike.
    - Rules are kept six hours; a market that changes status in between is caught by the venue.

- [ ] **Kraken live trading: balances, order minimums, leverage, read pacing (2026-11-10, built, NOT
  tried with money; see CHANGES).** Cody plans to trade BTC, TAO and KAS on Kraken spot.
  - Cody to try, on the DESKTOP app with a live Kraken key (the VPS is paper only): after a deposit, the
    Balances tab should say USD (not ZUSD) with the amount under Free, and the risk sizer should find the
    cash. Place a resting limit buy: its dollars should move to Locked. Try a 100 KAS order: it should be
    refused with "Kraken's smallest KAS/USD order is 170 KAS". On KAS/USD the leverage field should read
    "this pair takes 2 or 3".
  - Unverified, needs a live account: BalanceEx (read from Kraken's docs; the fixture is shaped like the
    docs, not like a real answer from this account); what `.HOLD` really holds (not documented by Kraken);
    the margin close (an opposite order with the position's leverage and `reduce_only`; Kraken's docs do
    not say a close must use the same leverage, and the position's leverage is rounded from cost/margin);
    whether this account is allowed margin at all (Kraken limits margin by region and verification);
    the account's real rate tier (the limiter assumes the lowest, Starter); Kraken Futures' PUT
    /leveragepreferences (the signing of a query string is my reading of the docs).
  - Not done: Kraken Futures positions still report leverage 1 (hard-coded); a close of a Kraken Futures
    position sends no leverage (none is needed there: reduceOnly is enough). The pair rules are read
    every six hours; a pair Kraken moves to cancel-only in between is caught by Kraken, not by us.
  - **Cody's 2026-11-10 report, the error on editing a paper take profit (Positions tab, take-profit
    column):** done again on 2026-11-13 through the real dashboard on a copy of Cody's own paper account,
    and on a short in a unit test: the current code says nothing wrong. The server Cody heard it on was
    started before the 2026-11-10 fix (21:42). Cody to try once more on the running server; if it still
    happens, the exact words and the position (long or short, which legs) are needed.

- [ ] **Trading with several tabs and venues, and the stop-edit error (2026-11-05, built, NOT tried; see
  CHANGES).**
  - Cody to try, on PAPER:
    - Open a position with both a stop and a take profit, then move the stop on the Positions tab. It
      should be silent (no error sound, no "may be unprotected"), and the take profit should still be
      listed.
    - Arm a quick trade on one tab, switch tabs and press Ctrl+Enter. It should say where it was armed and
      place nothing.
    - With BTCUSDT on Kraken and on Bitstamp in two tabs, buy on one. Then buy from the other: it should
      be refused.
  - Question for Cody: **paper accounts per venue?** Paper mode keeps one position per symbol, so you
    cannot hold paper BTC on Kraken and on Hyperliquid at once (the second is refused). Recommendation:
    one paper account per venue, keyed by venue and symbol, so a paper session can rehearse exactly what
    the live accounts would hold. That touches the paper ledger file, the Positions tab and the status
    bar, so it waits for a yes.
  - Unverified (built, no test drives it): the Positions tab's current price comes from the position's own
    venue, and a second Enter while an edit is in flight is ignored.
  - Open, mine:
    - Before any paper position or order exists for a symbol, its last price is shared by every venue's
      bars. A market order is priced from its own venue's tab, so this only matters with no such tab open.
    - A recorded venue that returns no price at all turns the venue guard off for that symbol.

- [ ] **Hyperliquid provider (2026-11-04, merged, NO order ever sent; see CHANGES and
  docs/HYPERLIQUID_PROVIDER.md).**
  - Cody: make an agent wallet on the TESTNET (app.hyperliquid-testnet.xyz), add a Paper profile and
    validate it.
  - Then place one small limit order, cancel it, and place a bracket.
  - Mine, after that first order: record real `clearinghouseState` and `frontendOpenOrders` replies to
    replace the documented-shape fixtures, and try the websocket live.
  - Not built: HIP-3 dexes, TWAP, `expiresAfter`, the `orderUpdates` stream, the dead-man's switch,
    funding as an indicator, vaults.

- [ ] **Hyperliquid: research done, nothing built (2026-10-25; see `docs/archive/HYPERLIQUID_RESEARCH.md`).**
  Questions for Cody before anything is built:
  - Are you a US resident or citizen? If so, live trading (phase 4), and in practice testnet
    (phase 2), break Hyperliquid's terms until the Payward/Bitnomial route opens.
  - Is this for your own trading, or a feature for the terminal's users?
  - Is a read-only data provider (phase 1: funding, open interest, HIP-3 stock perps) worth building on its own?
  - Hand-written signing, Nethereum, or JKorf's HyperLiquid.Net?
  - Should we watch Payward/Bitnomial and Kraken Pro instead?

## Verified by Cody (2026-11-16): what is still open

The items that waited here for Cody's look and listen (2026-10-22 to 2026-11-15) moved word for word to
`docs/archive/TODO_HISTORY.md`, "Verified by Cody 2026-11-16". A question that only asked whether something
sounded or looked right is taken as answered by that. What they still ask is below. **Q** marks a
question for Cody; the rest are known limits I wrote down.

- [ ] **Cipher A names, "faint", Cipher B Sell dot (2026-11-15).** The two red diamonds' size: DONE
  2026-11-16 (one factor per indicator; the Blood Diamond stays larger at every zoom). Limits: the Sell dot is our rule (the buy dot's mirror), with no chart to
  check it against; the hidden keys keep the shape names ("Blood Diamond").
- **Cody's answers, 2026-11-16** ("take all of your suggestions to the questions. drop the gold dot, not
  needed."), recorded here so they are not asked again; what was built is in CHANGES. Note the names: the
  items below that say "Cycle Position" mean today's Cycle Position (the copy of the original, named
  Cipher C until 2026-11-04), and "Cipher C" today's Cipher C. Decided, nothing to build: Cycle
  Position's "Yearly lows found from price" stays off; the dollar's 15-24 day table beside its 16-25
  band stays, as in the original; confirmed lows keep saying their confluence; untuned assets keep
  their weekly lows drawn; a pending low's later tag change is not announced; no white dotted zero
  line. Cipher C: no two-daily-cycles rule for a weekly cycle; the 65% "low had come" share stays; the
  per-asset rules stay off (not without refitting Low is in). Alerts do not describe what a marker
  looks like. Bars stay named by their open time (no close-time option). The gold dot question is
  dropped. The screenshots are not re-shot. Already built before the question (it was stale): Cycle
  Position's "held, unconfirmed, N days" (2026-10-28) and Cipher C's "less reliable on this chart",
  which is the weekly reading's "often missed here" (CYCLE_POSITION_DESIGN 9.8). Built 2026-11-16: the
  rest (CHANGES, "Cody's answers to the open questions").
- **Cody's answers, 2026-11-17**, to the eight questions left after that: (1) Cipher B's periods and
  "Version" input stay gone; (2) the three setup graders keep refusing to run; (3) each "Read as" form
  keeps its own saved sound: BUILT; (4) Add Indicator splits settings into Advanced: BUILT; (5) no trigger
  or slow DSS adjustment settings for Cycle Position; (6) Ctrl+Alt+Shift+K stays as it is (it unhides
  only what H hid, not left-out parts); (7) the Alerts dialog lists left-out parts, marked "(left out)":
  BUILT; (8) the Market Cipher crypto 4h re-test is closed, not waited on (re-runnable later under its
  own pre-registration). The "move Cipher B's Wave Trend periods to Advanced" question was stale: the
  periods are gone (answer 1). Built: CHANGES, "Cody's answers, 2026-11-17".
- [ ] **Cipher C on the chart's own clock (2026-11-07).** Low is in re-run on the time-counted lows:
  DONE 2026-11-16, back on weekly charts (their own weekly lows and the yearly reading) and, on 4-hour
  coin charts, the weekly reading only (CYCLE_POSITION_DESIGN 9.14). Limits: the 4-hour daily and yearly
  readings stay off (94.8% and 9 of 11, under the daily chart's); 1-hour and shorter charts and 4-hour
  stock charts were not measured and read none of it; on a weekly chart only 6 of Bitstamp's own weekly
  files were tried, the rest are daily charts made weekly; the first weekly low after the count's start
  is never said pending; weekly charts match the original's weekly lows less well than daily charts
  (.516 vs .658 F1); a scroll-back on an intraday chart can move the daily lows for several cycles.
- [ ] **Cycle Position (the copy of the original, 2026-10-21 to 10-28).** Every question answered (above).
  The known gaps are listed in the archived item ("Cipher C drawn as the original").
- [ ] **Cipher C (2026-10-28, named Cycle Position until 2026-11-04).** Low expected removed and the weekly
  "high" mark added: DONE 2026-11-16. To build, LATER (Cody: yes, later): read the check-box marks as
  events in the strategy catalog.
- [ ] **Cipher B as Market Cipher B 2.4.0 (2026-10-31).** Grey lines on light themes and opaque waves on
  Steel Gray: DONE 2026-11-16. Retired strategies and `TradeRanker`: DONE 2026-11-16 (the retired
  built-ins move out of old libraries to `strategies.retired.json`; `TradeRanker` deleted; see CHANGES).
  The periods and "Version" input stay gone and the setup graders keep refusing (Cody, 2026-11-17;
  build 2e86d377 to re-run one). Each "Read as" form keeps its own saved sound: DONE 2026-11-17.
  Limits: the waves' "overbought"/"oversold" uses ±53 whatever the Triggers say; the pane's floor is −103,
  so Trigger 2 below −53 clips the bar; the buy dot is drawn at −96; a Different Timeframe shorter than the
  chart's falls back to the chart; 4-hour and daily buckets on stocks are UTC-aligned.
- [ ] **Properties (2026-10-23).** Every question answered 2026-11-17 (above). Add Indicator's Advanced
  group and the Alerts dialog's left-out parts: DONE 2026-11-17. Limit: the browser-closed tab monitor's
  memory of a left-out part has no test of its own (its test harness has no indicator engine); it uses
  the same helper the chart and the hosted poll do, which are tested.
- [x] **Cipher DBSI (the study).** CLOSED by Cody 2026-11-17 ("close the dbsi study"). The app's DBSI is the
  study's last adopted model, `zu_hs66e` (ported 2026-11-02); re-checked 2026-11-17: both fixtures regenerated
  from it equal the committed ones bar for bar, and every DBSI test passes. Left as limits, not to be chased:
  the bear side misses more than the bull side; flat-price eras ("flat prices, may be 2 off" is said there);
  the Stoch RSI top digit (TradingView's own `ta.stoch(ta.rsi(close, 12), 12)` equals ours to the last
  binary digit, and DBSI treats the same value both ways, so the cause is in its hidden code); the
  pinned-Stoch-RSI re-arms. Its Stoch RSI is on plain close: on log(close), as Cipher B's is, the model was
  worse on 95 of 113 charts. The original's Start-grid numbers stay out of the public repo (Cody,
  2026-11-16).
- [x] **The Market Cipher strategy tests (2026-10-25).** CLOSED by Cody 2026-11-17 ("Don't wait on
  it/close it"). The crypto 4h trigger-wave re-test can be run later under its own pre-registration; a
  round with exits needs more than 2,380 random books.
- [ ] **Heikin-Ashi (2026-10-22).** Custom compiled indicators are computed for strategies (on HA
  candles when asked), in the backtester and in background tabs: DONE 2026-11-16 (CHANGES); not tried in
  the app with a custom indicator. Limits: a background tab's candle type is read when its monitor starts; the hosted server's
  alerts and the focused close line read the raw close; compiled and plugin strategies ignore "Signals from
  Heikin-Ashi candles".
- [ ] **Drawing tools (2026-11-01/02).** Limits: an alert on a drawn level stays at its price if the
  drawing moves; sloped lines are not offered as alert lines.
- [ ] **Smaller limits, mine.** Built timeframes: 7h buckets count from 1970, not from midnight (check what
  TradingView does), and a 7h chart from Bitstamp's 1h loads about 142 bars. Trade status bar: no key
  speaks the trades. Close at limit: no percent on the market Close position button, and "Size in" is not
  remembered. Profiles are left out of playback, and the heatmap's sound is unheard. A bar close: the
  hidden window's notification has no ladder, and with two browser windows the browser-closed monitor
  watches the one that saved last. Bitstamp's new-bar open is provisional for its first seconds. Cipher A
  pivots on sessions crossing midnight UTC and on weekly charts are unchecked. A drawing anchored before
  the reachable history is drawn wrong.

- [ ] **The VPS's browser harness is still on Playwright 1.55's Chromium.** CI moved to 1.64.1 on
  ubuntu-latest (Ubuntu 26.04) on 2026-11-17 and passed there. Before the VPS's next browser-harness
  run, run `npx playwright@1.64.1 install chromium` there (SERVER_SETUP.md).

- [ ] **Flake, not forced yet (2026-11-04):** `TabKeepsItsMarketTests.A_stock_tab_on_a_provider_that_also_serves_crypto_stays_on_stock`
  failed once in a full local run, then passed 3 of 3 alone. It polls `SelectedMarket` for at most
  3 s (60 × 50 ms), so it is probably load. Force it (per the 2026-09-25 rule) before trusting either way.

- [ ] **Heatmap and Profile Ctrl+Left/Right: DEFERRED by Cody 2026-10-22** ("it works fine and it's not hurting
  anything"), and again 2026-11-16 ("not necessary right now"). The bins live outside the component, so Ctrl+Left/Right says "No points of interest" and
  Alt+Shift+D says nothing. To build: Profile stops where price crosses the point of control and enters or
  leaves the value area; heatmap stops where the biggest liquidity level moved.

- [ ] **Cody's 2026-10-22 design points, still open:** nothing out of reach (every drawn thing must at least be
  named in the series summary or Alt+Shift+D, even with nothing to step through). The yearly cycle low
  sketch (a yearly low is a weekly cycle low confirmed when the next weekly low is confirmed higher, about 7
  to 9 months on Bitcoin) was worked by hand on one asset only; Cipher C's "yearly lows found from price"
  switch (above) is the built version.

## After the 2.13 tag

The Windows zip check, the website and the hosted terminal were done on 2026-11-16 (see CHANGES,
Unreleased). Left:

Nothing left: the screenshots are not re-shot (Cody, 2026-11-16, taking the recommendation; the hero
shot's old colour key is the only detail out of date).

## The Windows installer and tidy zip (2.13.1, 2026-11-16)

- [ ] **Cody to try the setup with a screen reader.** The runner installs, upgrades and uninstalls it
  silently and checks every file; nobody has gone through its screens with NVDA or JAWS. Inno Setup's
  wizard is standard Windows controls, so it should read, but that is not shown.
- [ ] **Unsigned.** SmartScreen warns on the setup and on Accessible Trader.exe ("More info", "Run
  anyway"); the README in the zip says so. Signing needs a certificate.
- [ ] **An upgrade empties `app\`** before copying the new version in. The app keeps nothing there (user
  data is under `%LocalAppData%\AccessibleTrader`), but a Strategies drop-in put by hand into
  `app\Strategies` would go. The documented place is `%LocalAppData%\AccessibleTrader\Strategies`.
- [ ] **The tidy WebHost zips (2026-11-17, Cody: "structure these and all packaged zips") are tried only
  on Linux.** Each unzips to `AccessibleTrader-WebHost-<version>/` with `app/`, `README.txt` and one
  starter. The Linux zip was built and run here (started from another folder, it served the page from
  `app/` and wrote nothing into its folder; the trial in `package.sh` fails when either is broken). Not
  yet run anywhere: **`packaging/webhost/package.ps1`** (pwsh is not on this machine; the first
  `diagnostic-build.yml` run or the next release is its first run, and its trial then runs the Windows
  zip), the **WebHost launcher's message box** when the server stops as it starts, and the **macOS
  `Accessible Trader.command`** (cross-built; Gatekeeper may want Control-click Open, which its README
  says). Starting the server outside `app/` used to be how everyone ran it; old bookmarks to
  `AccessibleTrader.WebHost` at the top of the folder no longer exist.

- **The .NET runtime the WebHosts carry is 10.0.11**, older than the Brotli decoder fix in 10.0.14
  (GHSA-p3jp-9v83-76w9, high). `global.json` pins SDK 10.0.301 (see its rzc note), and the self-contained
  WebHost zips ship that SDK's runtime. Exposure needs Brotli decompression of untrusted data; no shipped
  code enables it (only StrategyLab's MacroFetchCommand, a research tool, uses DecompressionMethods.All),
  and HttpClient does not by default. Next release: install a newer 10.0.3xx SDK, or set the runtime
  pack version, and check the WebHost zips' System.Private.CoreLib.dll version.

## Asked for, not built yet

- [x] **Where a signal sits (Cody, 2026-11-17).** DONE 2026-11-17 ("use the number of the level"): a mark
  drawn at its value names the drawn level it has passed going out from the middle line, "Large green dot
  below -60" (CHANGES). Not heard.
- [x] **Line, wave or area fill (Cody, 2026-11-17).** DONE 2026-11-17: "area" when a fill is drawn under
  it, "line" when not (CHANGES). Not heard.
- [ ] **Cipher A above the candle (2026-11-17).** Built; not seen. The pad over a tall candle (0.35 of
  its range, the existing rule) may look far; say if it does.

- [x] **Indicator template (Cody, 2026-11-05).** **Built 2026-11-19, not yet heard by Cody.** Take
  one chart's indicators and panes, with their settings, and put them on the chart in front, keeping
  that chart's symbol and timeframe (TradingView calls this an indicator template). It is the toolbar
  workspace group's **Save indicator template** / **Apply indicator template**, or the Go bar's
  `TPLS` / `TPL` (`TPL name` applies directly); files live in `IndicatorTemplates/` beside
  `Workspaces/`. Replace-only, one Ctrl+Z; no "add to this chart" mode, volume is not included, and
  alerts on a replaced indicator behave as when you delete it by hand. See `CHANGES.md`.

## From the 2026-09-25 to 2026-10-01 test and mutation passes (status unclear, check)

The 81st pass (archive 1679) ended with these. Later passes (A2q merged 2026-10-01 with 44 of 44
survivors closed) may have settled some. Decisions for Cody:

- [ ] Where should Page Up/Down land on the candles? The engine says component 0 (upper wick), the reducer's
  default is the body, and the tests pin component 0.
- [ ] A signal that printed while F2 was on is heard after unmuting. Is that wanted?
- [ ] A same-chart reload that returns more history is treated as history loaded in front. OK?
- [ ] The survey's section 4 item 8 design questions (`scratchpad/a2q_SURVEY.md`) are still open.
- [ ] Should any Interface/Event/OrderEvent classification in the 2026-10-01 F2 entry in CHANGES move?
- [ ] The Tab-trap flake now has a message ("AIAnalystModal reports 2 focusable controls but Tab only ever
  reached 1"). Root-cause it.
- [ ] Unverified: Dot Pad shows values below 0.001 as "0" (read only); `dotpad.log` growth; the Dot Pad fixes on
  hardware; all new wording by ear.
- [ ] Not yet mutated: StrategyLab (25,804 lines); `ModalBrowserContractTests` Tab/Shift+Tab (not reproduced in
  eight loaded runs); `SettingsModalTests.SettingsModal_OpenedOnATab...` (one co-failure, no message kept);
  `ValueDeviationTests.BarsBeforeTheWindowFills_HaveNoReading` looks vacuous (flat fixture); a bUnit upgrade
  may break `SettledEventDispatch`'s reflection route (its guard names it); the browser probes rewrite
  `scratchpad/` files on every run.

## Still open from earlier audits

Every line below was an unchecked `[ ]` item in the old TODO, quoted by its first line, in the order it
appears there, under the section it was filed in. The group names are the old section titles. Items from the
2026-10-18 to 2026-10-21 passes are recent; everything from "The work A2b creates" down was filed in
2026-08 or earlier and is **(status unclear, check)**: many have been fixed since, and the archive holds the
full finding with file and line references. Open the archive line before starting one.

Not lifted, because they are superseded or answered: the questions for Cody in the 2026-10-17 blocks about
Cipher B, Cipher A, Cipher SR, the old DBSI-style indicator, Spider Lines and formations (all of those
indicators were rebuilt as Market Cipher copies on 2026-10-23 and 2026-10-31, Spider Lines were removed, and
the 2026-10-17 answers are recorded as DONE in CHANGES); every NEXT list in the START HERE blocks from
2026-09-03 to 2026-09-24 (each repeated the one before it; the work was done, or is carried in the items
above or below); and all `[x]` items, finished audit write-ups and phase plans.

### 2026-10-21 — the hand-drawn trend line study, and text label placement fixed; ...
- [ ] Cody to look at text labels in the app: one on the newest bar, two on one bar, a long one. (archive 1048)
- [ ] A label's text in a script no installed font covers (Japanese on this machine) still shows (archive 1050)
- [ ] Not checked: whether a label's colour set in Properties changes anything visible (the plate (archive 1052)
- [ ] The trend line rule. One dated case separates the candidate rules: the Dow's 23 Jul 2026 (archive 1054)
- [ ] The trend line is NOT part of CF as far as can be told: CF is hidden on every frame with a (archive 1069)
- [ ] Cody to open `strategy-lab-data/trendlines/Camel cycles trend lines.json`: a copy of his (archive 1072)

### 2026-10-21 — Cipher C rebuilt as cycle lows after CF's Cycle Trading Indicator; ...
- [ ] Cody to hear it with Orca: a daily Bitcoin and an SPX chart, a weekly chart (no "daily cycle (archive 1085)
- [ ] Merge (both branches), push (the full suite runs there), restart the server: Cody's go-ahead. (archive 1087)
- [ ] CF's future cycle low range (FCLR) boxes to the right of the last bar: the chart has no future (archive 1088)
- [ ] Confluence that upgrades LOW → MID → HIGH over the bars after a low, as CF's demo shows. Ours is (archive 1090)
- [ ] A yearly cycle low has never been seen on CF's screens; ours is unchecked against it. Same for (archive 1093)
- [ ] Still estimated: NDQ, DJI, DXY, SILVER, USOIL, GDXJ, SILJ. Twelve more videos searched (archive 1106)
- [ ] "Days since Last DCL : N" is bars from the low to the last bar (Bitcoin: 46 on 7 Aug 2025 = (archive 1109)
- [ ] More of CF's look, not built: (archive 1116)
- [ ] The cancelled low's green box around the original stack (ours is a red cross on the cancelling bar). (archive 1121)
- [ ] CF puts the check under the low's own stack once confirmed; ours sits above the confirming bar. (archive 1122)
- [ ] CF's LOW / MID / HIGH text on the pending label; ours is spoken only (the tag itself is the original's since 2026-10-27). (archive 1123)
- [ ] Re-test the retired Pulse Reversal Long on `CIPHER_C.DCL Confirmed` before reviving it. (archive 1133)
- [ ] Drawing the window tint as CF's band is: only the past part is drawn. (archive 1134)

### 2026-10-21 — the Camel cycles workspace broke on load: five defects FIXED on branch ...
- [ ] Not pushed, not deployed, and Cody's running server is still on the old build. Merge, push (archive 1141)
- [ ] The GLD 1d tab's "400 (Bad Request)": the reason is unknown because the body was never read. (archive 1143)
- [ ] One Bitstamp "400 BadRequest" for BTCUSDT from the MarketFeedHub live subscription. Every (archive 1145)
- [ ] The session file already saved the DIA tab as Crypto Alpaca AAVE/USD, and dropped the Loukas (archive 1147)
- [ ] The Alpaca live stream: the log showed 409 "insufficient subscription" (the plan is IEX only) (archive 1150)
- [ ] The DIA 1d tab "freezes" and Orca crashed on it (Cody, 2026-10-21). Not explained: DIA's (archive 1157)
- [ ] `LocalBackgroundMonitor.FetchAsync` (WebHost alert poll) asks the provider for the watch's (archive 1182)
- [ ] Untested live on stocks: no stock request with the new `start`/`sort=desc` has been sent with (archive 1190)
- [ ] Re-score the dated calls in `Corpus/rules/*.md` against prices (Camel Finance's 358 first, (archive 1209)
- [ ] Fold the 35 later educator transcripts (Trader Geo's 93-minute tutorial among them) and the (archive 1214)

### 2026-10-18 — setup grader round 3, Market Cipher with Cipher SR location: DONE, No ...
- [ ] Questions for Cody (nothing applied): (archive 1222)
- [ ] Not pushed: the branch is committed locally. The full suite runs on GitHub Actions at the push. (archive 1233)

### 2026-10-18 — the cycle school: stage 2 DONE, results in ...
- [ ] Decide (Cody): fix the old `cftc-cot` downloader to choose contracts by code and stamp (archive 1243)
- [ ] Unverified: the CFTC release dates before 2026 are a holiday rule plus the CFTC's (archive 1247)
- [ ] If the design is ever revised (a new pre-registration): drop the "low due" rule (B5 found it (archive 1250)

### 2026-10-18 — the cycle school: stage 1 DONE (method, design, pre-registration); see ...
- [ ] Stage 2: build and run the 56 pre-registered tests exactly as written, results into a new (archive 1263)
- [ ] Before using COT in the score: confirm which CFTC report each `xs_cftc_*` file came from. The (archive 1265)
- [ ] Questions for Cody (nothing applied): the "90+ metrics" claim for the CF indicator is in (archive 1268)

### 2026-10-18 — macro, Fed, liquidity and sentiment data: FETCHED, point in time; see ...
- [ ] Decide on Cboe put/call after 2019-10-04. It exists only as one HTML page per day, about (archive 1289)
- [ ] Decide on NFCI as-known-then values. ALFRED returned HTTP 504 on the initial-release file. (archive 1291)
- [ ] Optional: a loader that registers the macro CSVs in the cross-series cache, so indicators can (archive 1293)

### 2026-10-18 — Cody's BTCUSDT 1D screenshot: axis tags and empty status-line plots DONE; ...
- [ ] General tag drift rule (recommendation, Cody's call). `StackScaleLabels` has no limit on (archive 1304)
- [ ] Ctrl+Up/Down still stops on a plot with no value on this chart (Cipher SR's Range/VWAP on a (archive 1312)

### 2026-10-18 — setup grader round 2, Market Cipher school: DONE, No edge; see CHANGES ...
- [ ] Question for Cody (nothing applied): in equities, the first bearish divergence exit beat (archive 1323)
- [ ] Not pushed: the branch is committed locally. The full suite runs on GitHub Actions at the push. (archive 1326)

### 2026-10-18 — setup grader for the Cipher B Buy dot: DONE, No edge; see CHANGES and ...
- [ ] The equities side results (non-vetoed dots +0.115R over a random bar in the same context; (archive 1331)
- [ ] If the grading idea is to be tried again, it needs a new dated pre-registration (the old one (archive 1335)

### 2026-10-18 — three lab defects verified and fixed: R after a stop move, HTF ...
- [ ] Re-record every engine R figure on a two-rung ladder with breakeven or trail. R is now one (archive 1341)
- [ ] Re-run the COT battery cells (`battery` v5a-v5h, BTC CFTC_COT) and `cipher-a-battery`'s COT gate (archive 1352)
- [ ] Optional: re-download the COT snapshots with the fixed `cftc-cot` so the files themselves carry (archive 1354)
- [ ] `docs/archive/SETUP_GRADER_PREREG.md` section 2.2 still lists the HTF and R caveats; it is a frozen (archive 1356)

### 2026-10-17 night — Cipher strategy re-run on the new Cipher A/B/SR: DONE; see CHANGES ...
- [ ] Delete `CIPHER_A.Buy Signal` and `CIPHER_SR.Support` from bare-bull-pulse, faber-pulse and (archive 1365)
- [ ] The 1h Cipher SR proximity result (old Cipher B +0.308R p 0.006, new +0.164R p 0.069, KAS and (archive 1371)
- [ ] CatalogueProvenance's rolling-window figures were all computed before the 2026-08-24 (archive 1373)
- [ ] The battery's cells that need retired leaves inside an AND (v23+A, v23+SR, v23+ASR, v23+ALL, (archive 1376)
- [ ] ML confidence model: retrain on the new Cipher B export when lightgbm/scikit-learn are (archive 1379)

### The work A2b creates (status unclear, check)
- [ ] Re-check what else parked behind `ShowModalAsync`. Until 2026-08-28 every line a concrete (archive 10126)

### The work A3 creates (status unclear, check)
- [ ] (F2) Give `textarea#sd-import-json` a real label. Same acceptance test. LOW. (archive 10710)
- [ ] (F3) Make Shift+F12 with no focused series say so instead of returning silently. LOW. (archive 10711)
- [ ] (F4) Read the timeout in (archive 10712)
- [ ] Teach the harness to seed a chart and credentials, which unlocks the seven unswept dialogs (archive 10715)
- [ ] Sweep controls behind `<details>` expanders and populated lists, not just the initial render (archive 10718)

### Trading dashboard decoupling (roadmap adopted 2026-08-23) (status unclear, check)
- [ ] Spec Appendix B follow-ups (separate from the decoupling): duplicate protective legs (archive 11306)

### Hosted deployment — notes back from the server (2026-08-24) (status unclear, check)
- [ ] `AudioEngine.RingBuffer<T>.Enqueue` is single-producer and now has FOUR producers (note §5b). (archive 11409)
- [ ] Process, not code: tell the server before the SDK band moves (note §4). `global.json` pins (archive 11446)
- [ ] Run the integration suite a second time under a non-empty `PathBase` (note §3). Re-checked (archive 11544)

### Money path, batch two — follow-ups filed 2026-08-24 (status unclear, check)
- [ ] A withdrawal still shares its provider instance with the order path for the length of one (archive 11844)
- [ ] No guard stops the next `WaitForAssertion` over non-render state. bUnit re-evaluates (archive 11851)
- [ ] The shared paper account still holds its creating tab's scoped `IDataService`. (archive 11862)
- [ ] API-key secrets are stored under the nickname alone, so one nickname across two providers (archive 11868)
- [ ] A failed secure-storage read makes every credential silently absent (archive 11875)
- [ ] Spoken money is always prefixed "$" regardless of quote currency (archive 11881)
- [ ] Follow-up from the money-path batch: the quick-trade sizing seam cannot see the instrument. (archive 11888)
- [ ] Portfolio pricing is hardcoded to the Crypto market (`MarketDataPriceSource.cs:106`). (archive 11900)

### Health audit — strategy engine and backtest↔live parity (grade D+) (status unclear, check)
- [ ] Two deliberate live-vs-replay divergences left open by the 2026-08-25 parity work, both in (archive 12059)
- [ ] An account selector for Schwab. Carved out of the multi-account finding closed (archive 12180)
- [ ] `LevelService.cs:30-31` swallows every level-provider exception with `catch { continue; }` — no (archive 12271)
- [ ] `StrategyEngine.AddStrategy:255`, `PauseStrategy:280-282` and `SetExecutionMode:287-289` (archive 12279)
- [ ] `StrategyEngine._pendingSignals` is written and never read — Suggestion mode has no confirmation (archive 12286)
- [ ] The announced entry price and R:R are never the ones the backtest fills at. (archive 12293)
- [ ] (amends TODO:2084/2092) Add two more copies to the hand-rolled-ATR census: (archive 12348)

### Health audit — alerts and notification delivery (grade D) (status unclear, check)
- [ ] `AlertOrchestrator` is scoped per circuit in WebHost, so two browser tabs silently delete each (archive 12567)
- [ ] The VAPID private key is written in plaintext with default permissions and the VAPID subject is (archive 12581)
- [ ] The hosted server logs every user's alert text at Information — `HostedAlertMonitor.cs:185`. (archive 12588)
- [ ] Nothing checks bar freshness, so a stale feed reads as a live market — (archive 12594)
- [ ] Two load-bearing comments describe behaviour the code does not have. (archive 12600)
- [ ] (amends TODO:1281) That entry says "Adding or deleting an alert produces no feedback and (archive 12608)

### Health audit — market-data flow (grade C+) (status unclear, check)
- [ ] Opt the providers into the cancellable fetch, one at a time. Carved out of the (archive 12741)
- [ ] `OrderBookHistoryService` hardcodes a 1-minute bar, so heatmap history is empty on every other (archive 12748)
- [ ] `DataOrchestrationService` issues a live order-book network request on every single data update, (archive 12757)
- [ ] The provider-timeframe verification only catches data that is too FINE, never too COARSE (archive 12767)
- [ ] The tab-switch handler assigns the composite market string to the SUB-TYPE field (archive 12777)
- [ ] `MarketFeedHub.TryStartFeedLiveAsync` reports `Started` when it lost the race and threw its (archive 12806)
- [ ] `ChartFeed`'s cap-trimming loops reallocate the entire backing array once per bar (archive 12815)
- [ ] Two `CancellationTokenSource`s are replaced without being disposed, once per tick and once per (archive 12823)
- [ ] `DataService`'s constructor takes an `IPluginLoaderService` it drops on the floor and stores an (archive 12829)

### Health audit — SDK and the 16 market-data / trading provider plugins (grade B-) (status unclear, check)
- [ ] The SDK is not dependency-free, contradicting `docs/CODEBASE_KNOWLEDGE_BASE.md:30`. (archive 13135)

### Health audit — analytics / alternative-data plugins (grade C) (status unclear, check)
- [ ] (amends TODO:5825-5834) `ProviderFetchOhlcvTests.cs` gives the analytics plugins one (archive 13776)

### Health audit — indicators (grade C+) (status unclear, check)
- [ ] `AnchoredVwapProvider.cs:191-198` — `AVWAP Bias Soft` can never take the value 0 its comment (archive 13974)
- [ ] `CipherBProvider.cs:1038` reads a `DivergenceConfirmLag` parameter that `:385-431` never (archive 13982)
- [ ] A parameter can silently flip a component from causal to look-ahead, and the declaration cannot (archive 13989)
- [ ] `CipherSRProvider.cs:199` — the `AutoScale` parameter description shown to the user documents the (archive 14000)
- [ ] `PulseProvider.cs:967-995` — `GetDetailFact` speaks the literal string "NaN" during warmup. (archive 14006)
- [ ] `RegimeProvider.cs:103` emits `0.0` for `RegimeState` during the 199-bar warmup instead of NaN, (archive 14013)
- [ ] `CipherSProvider.cs:193-213` sorts a freshly allocated copy of the whole rolling window on every (archive 14020)
- [ ] `IndicatorService.cs:151-201` and `IndicatorEngine.cs:46-51` call `provider.GetIndicators()` on (archive 14027)
- [ ] `AccessibleTrader.Sdk/Indicators/IndicatorMath.cs` has no unit test. It is the shared library — (archive 14058)
- [ ] The causality guard has two remaining structural blind spots its own doc does not name: (archive 14071)
- [ ] Comments that are actively wrong and load-bearing. `RegimeProvider.cs:106-107` — "seeded with (archive 14080)
- [ ] `IndicatorStateMapper.cs:28-30` maps provider output to components by substring in both (archive 14094)
- [ ] `PaneAssignmentService.cs:15,44` use culture-sensitive `ToUpper()`/`ToLower()` on indicator (archive 14103)
- [ ] `VolRegimeProvider.cs:215` hardcodes 1.25 / 0.75 in `GetDetailFact` while `VolState` (`:148`) (archive 14110)
- [ ] (amends TODO:2084) `CipherCProvider.cs:819-831` re-implements Hull MA next to (archive 14114)

### Health audit — accessibility layer (grade B-) (status unclear, check)
- [ ] A chart load failure still cannot say WHY. Filed out of the 2026-08-27 MEDIUM batch, which (archive 14447)
- [ ] Sub-pane navigation names no pane on the first feedback event of a session. Filed out of (archive 14454)
- [ ] Five price/size formatters coexist in the speech layer. `SpeechPriceFormatter.FormatPrice` (archive 14462)
- [ ] (amends TODO:960-968) Those citations have drifted ~17 lines since the 2026-08-23 edits: the (archive 14474)
- [ ] The speech-layer components that still have no tests. Carved out of the (archive 14507)
- [ ] The candle-summary tests assert only the *bullish* prefix (archive 14517)

### Health audit — workspace state, settings, persistence (grade C+) (status unclear, check)
- [ ] `ImportVisualProfile`/`ImportAudioProfile` mutate live state objects without dispatching — (archive 14694)
- [ ] `LoadWorkspaceModal.ResetWorkspaceState` can spin forever — `LoadWorkspaceModal.razor:158-171`. (archive 14703)
- [ ] `SettingsManager`'s `JObject` is mutated from multiple threads with no lock — (archive 14711)
- [ ] `WorkspaceConfiguration` has no schema version, and an indicator that no longer exists is restored (archive 14721)
- [ ] `CaptureConfig` mutates the live series during a save — `WorkspaceLibraryService.cs:205-209`. (archive 14733)
- [ ] A failed or refused tab switch is completely silent — `WorkspaceStore.cs:100`, `:163-170`. The (archive 14738)
- [ ] `AtomicFile` never fsyncs the parent directory and never cleans stale temps — (archive 14759)
- [ ] `AppDbContext` declares an index identical to its primary key, and its fallback path ignores the (archive 14767)
- [ ] `MarketFeeds.GetBarsAsync` drops its `CancellationToken` at the fetch — (archive 14774)
- [ ] Indicator parameters round-trip through the ambient culture and are re-parsed as invariant — (archive 14779)
- [ ] (amends TODO:4495-4497) "`WorkspaceStore` immutable snapshots. Replace direct property mutation (archive 14790)

### Health audit — audio engine and sonification (grade C+) (status unclear, check)
- [ ] A self-terminating voice is deactivated with no release fade, and the note envelope's (archive 14860)
- [ ] The engine's `DataIndex` → `PointReached` plumbing is dead, and per-frame work in the render loop (archive 14899)
- [ ] `WebHostAudioDriver` is `AddScoped` — one `AudioEngine`, one OS thread and one `pw-cat` child (archive 14910)
- [ ] The audio callback runs on the general .NET ThreadPool on Windows, under a lock — (archive 14946)
- [ ] `WebHostBrowserAudioSink`'s class doc is actively wrong about its own lifetime — (archive 14956)
- [ ] Playback tempo is `Task.Delay` with no clock anchoring, so it drifts and quantises — (archive 14962)
- [ ] Two `CancellationTokenSource` leaks in the playback path. `AudioSequencer.Stop:554-559` does (archive 14970)
- [ ] `SonifyProfile` and `SonifyHeatmap` are the only sonifiers that never pan, and `SonifyHeatmap` (archive 14975)
- [ ] `AudioDiagnosticTests.Coordinator_ShouldCoalesce_RapidStateChanges:84-119` asserts nothing and says (archive 14984)
- [ ] (amends TODO:1024) The no-output-limiting item understates the measurement: 20 sine voices at (archive 14990)

### Health audit — chart rendering, drawing tools, coordinate math, theming (grade C+) (status unclear, check)
- [ ] `ExtendLeft`/`ExtendRight` are accepted and ignored — (archive 15118)
- [ ] Audio series volume is used as the heatmap's visual alpha — `HeatmapRenderer.cs:35-36`. (archive 15137)
- [ ] The pane-divider drag uses a hardcoded 600 px container height that is never updated — (archive 15144)
- [ ] Zone-band shading and zone audio are two independent declarations with nothing keeping them in (archive 15151)
- [ ] The TBD "distribution ≥ 0.5" tint has no non-visual counterpart — `ChartRenderer.cs:391-424`. (archive 15162)
- [ ] `RenderZoneBand`'s new-level test uses a signed threshold — `StandardRenderers.cs:1134`. (archive 15170)
- [ ] Crosshair value labels are drawn on a hardcoded dark plate with theme-coloured ink — (archive 15177)
- [ ] The colour-vision-safe override reaches only candles and directional bars — (archive 15187)
- [ ] Candle bodies vanish at wide zoom — `StandardRenderers.cs:195`. (archive 15196)
- [ ] Malformed level colour hex kills the whole frame — `DataLayer.cs:192`. (archive 15202)
- [ ] A render failure is logged but never spoken — `ChartRenderer.cs:269-273`, (archive 15209)
- [ ] Dash `SKPathEffect`s are disposed at two of four creation sites — `StylingService.cs:69,76` and (archive 15217)
- [ ] Heikin-Ashi is recomputed over the entire history three times per keystroke — (archive 15225)
- [ ] Two crosshairs are drawn on the main pane every frame — `OverlayLayer.cs:24-40` and (archive 15232)
- [ ] A formation whose trigger is off-screen also loses its floor and its target — (archive 15268)
- [ ] Area fills stretch across the empty right margin — `StandardRenderers.cs:267-270`. (archive 15273)
- [ ] Stale constructor comment — `ThemeService.cs:58`. "fall back to HighContrastDark if not set" (archive 15289)
- [ ] (amends TODO:3665 / 5105 / 5521) TODO:3665 ("shared coordinate math consolidated into (archive 15303)

### Health audit — hosted WebHost, auth, multi-user security (grade B) (status unclear, check)
- [ ] `Register` has no email confirmation and anyone can register any address — (archive 15711)

### Health audit — StrategyLab research methodology (grade B-) (status unclear, check)
- [ ] `ExitCommand.cs:164-165` asserts trades do not overlap; for at least four of the eight exit rules (archive 15862)
- [ ] `ExitCommand`'s stop and target rules exit at the trigger bar's CLOSE, not at the stop or target (archive 15873)
- [ ] `TradingCrossCommand`'s fill-timing comment contradicts its own code by one bar (`:168-172` vs (archive 15881)
- [ ] `TradingCrossCommand.RunEras` leaks the next era into the first era's result (`:472-476`). (archive 15892)
- [ ] `TradingCrossCommand.RunDca:273` computes DCA's CAGR as `(finalValue / totalDeployed)^(1/years) - (archive 15901)
- [ ] Two commands derive a noise seed as `base + hash % 1000`, guaranteeing correlated noise across (archive 15909)
- [ ] `LabStats.PermutationP` silently computes a wrong statistic when `nA + nB > pool.Length`, and it (archive 15920)
- [ ] `XsMomentumRobustness.cs:66` and `:72` carry two undocumented magic constants inside the cost model (archive 15931)
- [ ] `ml/train.py:39-46` splits chronologically but applies no purge or embargo, and the split boundary (archive 15939)
- [ ] `OnChainCommand.cs:248-249` prints a dynamically-counted metric total next to two hardcoded (archive 15948)
- [ ] `GateCommand.cs:52` holds mutable static accumulator state (`_barsSeen`, `_crossBarsOpen`, (archive 15954)
- [ ] `ExitCommand.SignalExitAt:269-273` recomputes the full `ZScore` array on every bar of every trade (archive 15959)
- [ ] `WalkForwardCommand` blemishes (the strongest command in the file set, recorded so they are not (archive 15965)
- [ ] Cost model absent from most commands. No costs in `xsmom` (disclosed at (archive 15970)
- [ ] The forward universe recorder holds two days and there is a gap. `universe-archive/` contains (archive 15978)
- [ ] `RegimePersistenceCommand:187,312,338` hardcodes 252 bars/year for crypto too. Every use is a (archive 15982)
- [ ] The lab has no machine-readable output mode, which is its biggest accessibility gap. (archive 15985)
- [ ] (amends TODO:2028) The permutation-clamp count is wrong: it says "four commands" and there are (archive 15997)
- [ ] Have each command print its own test count and a Bonferroni threshold, the way (archive 16099)
- [ ] Commit content-addressed snapshots for the samples that back a recorded edge, and (archive 16106)
- [ ] The 59 lab files with zero test references, and the absent arithmetic guards. Carved (archive 16114)

### The scrollback smear (reported from use, 2026-08-25) (status unclear, check)
- [ ] The heatmap series has the same prepend bug and is not covered by that fix. (archive 16187)
- [ ] Known full-suite flake: `ModalDisposeLeakTests.Dialog_DisposeReleasesEveryEventBusSubscription` (archive 16374)

### Causality for scripted indicators — closed 2026-08-25 (status unclear, check)
- [ ] Nothing consults `ICustomIndicatorRegistry.IsPublishable` yet, because nothing publishes (archive 16438)
- [ ] The strategy probe hands over an empty `ActiveSeries`, so an indicator-reading strategy is (archive 16479)

### Open (status unclear, check)
- [ ] A script strategy cannot reach the host services a script strategy might want. Filed out (archive 16621)
- [ ] Pine dialect fidelity was not assessed. `Core/PineScript/PineTranspiler.cs` (649 lines) (archive 16721)

### Still open from the patch's own list (status unclear, check)
- [ ] The impossible tab. Nothing validates a symbol/provider pair on workspace restore — (archive 16868)

### Ship-blockers — data pipeline — FIRST TWO FIXED 2026-08-22 (status unclear, check)
- [ ] `OhlcvStore` can write bars that were never real, and being insert-only can never repair (archive 17050)
- [ ] `EventBus` has no exception isolation between subscribers. `Publish` is (archive 17061)
- [ ] `DataState.LiveStreaming` is unreachable; the pipeline reports `Initializing` while (archive 17066)
- [ ] `AnalyticsDataResolver` ignores its `asset` parameter, so requesting ETH returns BTC (archive 17084)
- [ ] Live ticks never recalculate indicators. `DataManager.OnFocusedFeedUpdated` deliberately (archive 17089)
- [ ] Zero-price bars are silently deleted from every fetch. (archive 17095)
- [ ] `"1y"` datasets are unchartable with a misleading error. `MyDataProvider` advertises (archive 17100)
- [ ] Two `MyDataStore` instances over one user directory destroy each other's imports. (archive 17105)

### Ship-blockers — indicator lookahead (the backtest-validity cluster) — ALL SIX FIXED 2026-08-21 (status unclear, check)
- [ ] `ScreenerService.ReadColumns` still reads a component value by descriptor without consulting (archive 17148)
- [ ] Surface `ConditionEvaluator.LastDegradation` in the UI. Some strategies (archive 17158)
- [ ] `IchimokuProvider.cs:264-266` — the Chikou Span array is a raw 26-bar future price, exposed (archive 17212)
- [ ] `CipherAProvider.cs:400-427` — Cipher A's divergences are still stamped at the pivot bar; (archive 17221)
- [ ] `CipherSRProvider.cs:245,299,321` — the historic "Cipher SR backtest lookahead" is still (archive 17230)
- [ ] `SwingStructureProvider.cs:196-201` contradicts its own class doc. The doc at `:37-40` (archive 17236)
- [ ] `SwingStructureAnalyzer.cs:141-159` — pass 2 retroactively deletes swings, so the same bar (archive 17242)
- [ ] The only causality test in the codebase is vacuous. VERIFIED — (archive 17250)

### Ship-blockers — indicator computation (status unclear, check)
- [ ] `IndicatorEngine.cs:127-133` — on a same-bar tick the buffer is handed to the provider (archive 17270)
- [ ] `IndicatorResultBuffer.cs:65,75` zero-fills where the codebase's own stated convention is (archive 17283)
- [ ] `SkenderCalculationCore.cs:193-201` — ten registered Skender indicators resolve to no method (archive 17288)
- [ ] NOT IN THE AUDIT — seven more Skender indicators resolve fine and then write to component (archive 17295)
- [ ] `SkenderCalculationCore.cs:172-178` — every Skender indicator's live bar is recomputed from (archive 17321)
- [ ] `SkenderCalculationCore.cs:173-178` reads uninitialised `ArrayPool` memory. The temp buffer (archive 17327)
- [ ] `SkenderCalculationCore.cs:139-157` — the synthetic `__CROSSOVER`/`__SQUEEZE` flags are (archive 17331)
- [ ] `CoinMetricsProvider.cs:204` — daily metrics are stamped at the START of the day they (archive 17335)
- [ ] `Plugins/Analytics/…Cftc/CftcProvider.cs:197` — COT "release date" is a synthesised (archive 17340)
- [ ] `OpenInterestProvider.cs:203` and `CrowdingIndexProvider.cs:258` — zero-valued OI rows are (archive 17344)

### Indicator correctness — HIGH (status unclear, check)
- [ ] `BarDetailService.cs:227-282` — both indicator narrative facts are dead code. (archive 17352)
- [ ] `IndicatorContextAnalyzer.cs:85-91` — the Money Flow Wave thresholds are calibrated to a (archive 17358)
- [ ] `IndicatorContextAnalyzer.cs:197-213` — `DetermineZone` never compares price to a band. (archive 17362)
- [ ] `CipherSProvider.cs:96-109` speaks unhedged trading instructions — *"strong DCA (archive 17366)
- [ ] `CipherSRProvider.cs:448-455` — pivot speech scans FORWARD to report a touch count from the (archive 17375)
- [ ] `AnchoredVwapProvider.cs:142-163` anchors to the confirmation bar, not the pivot, silently (archive 17382)
- [ ] Skender component names that resolve to nothing or to duplicates. `Vip`/`Vim` on Vortex (archive 17385)
- [ ] `IndicatorMath.cs:141-151` — `ComputeStochRsi` fabricates 50.0 across the entire RSI (archive 17397)
- [ ] `FundingRateProvider.cs:60` (+ OpenInterest, Crowding — verbatim triplicate) mangles (archive 17401)
- [ ] `CrossSeriesCache.cs:97,153` — no TTL on success, no negative caching on failure. A (archive 17406)
- [ ] `CoinMetricsProvider.cs:279` attributes ETH's on-chain metrics to a BTC chart whenever the (archive 17411)
- [ ] `CotPositioningProvider.cs:281` starts emitting Crowded markers off a 5-sample population (archive 17414)
- [ ] Chart-pattern geometry defects. `ChartPatternDetector.cs:388-391` — the H&S neckline (archive 17422)
- [ ] `ChartPatternNarrator.cs:189-190` — the exact wording defect the class doc at `:199-207` (archive 17430)

### Ship-blockers — audio and speech (status unclear, check)
- [ ] Component metadata has no way to say "this value is a price." The fix above changed the (archive 17465)
- [ ] Saved workspaces persist `SpeechTemplate`, so existing users keep the old `{value:F2}` (archive 17472)
- [ ] Zone-proximity speech overwrites the bar reading on the web head. VERIFIED as a second (archive 17519)
- [ ] Component-context speech reads RAW OHLC under Heikin-Ashi. `NavigationFeedbackManager` (archive 17544)
- [ ] Muting is not absolute for Cloud components. `NavigationSonifier:352` and (archive 17552)
- [ ] `LevelCrossingMonitor` ignores series mute and every volume tier. `:113` filters on (archive 17560)
- [ ] The earcon round-robin overwrites the "dedicated" cross-chirp and level-cue slots. (archive 17577)
- [ ] No output limiting anywhere. `AudioEngine:621-624` writes `leftSum * _masterGain` (archive 17583)
- [ ] The volume bed and the candle body play the same two pitches. (archive 17590)
- [ ] `AutoNarrationService:179` re-introduces the F2 bypass the router redesign removed. A (archive 17599)
- [ ] Hitting the first or last series is a silent no-op. `NavigationEngine:178-190` clamps and (archive 17613)
- [ ] `SoundPatchLibrary` mutates a plain `List<SoundPatch>` that the navigation path enumerates. (archive 17617)

### Ship-blockers — UI and accessibility (status unclear, check)
- [ ] Every feedback message is spoken twice, and F2 silences only one of the speakers. (archive 17769)
- [ ] The assertive double buffer fails on even-numbered batches. `MainLayout.razor:186-194` (archive 17775)
- [ ] `role="menu"` popups are counted as open modals but are not covered by the Tab trap. (archive 17782)
- [ ] Keyboard rebinding cannot capture any modifier chord. `captureNextKey` registers on (archive 17788)
- [ ] The inline stop-loss / take-profit editor opens with `autofocus`, which browsers ignore for (archive 17794)
- [ ] `LoadWorkspaceModal` arrow keys change the highlight silently. `:96-120` implements a (archive 17800)
- [ ] `OrderBookModal` rows are `@key`ed on price, so a live depth update destroys and recreates (archive 17805)
- [ ] The chart's failure state is visual-only. `ChartArea.razor:128-149` puts "Connection (archive 17817)
- [ ] ~15 modals render status text into a `role="status"` region created in the same render as (archive 17822)
- [ ] Silent early returns. `TradingDashboardModal:788` returns before `_isVisible = true` when (archive 17847)
- [ ] MAUI: `MainPage.xaml.cs:61-129` subscribes on every `OnHandlerChanged` and unsubscribes only (archive 17877)
- [ ] MAUI: the Android hardware-keyboard bridge is dead for navigation keys and double-dispatches (archive 17883)
- [ ] MAUI: `TrayIconService.cs` is the highest compile risk in the tree. Behind `#if TRAY_ICON`, (archive 17896)
- [ ] MAUI: `BlazorAudioDriver.cs:205-213` (Android) exits its PCM push loop on (archive 17900)
- [ ] Two copies of `app.css` (763 and 782 lines) differing by exactly two hunks — and the (archive 17903)
- [ ] `.sr-only` is defined only inside `TradingDashboardModal`'s `<style>` block but used by (archive 17906)
- [ ] Three independent modal-open counters (`keyboard.js`, `CommandDispatcher`, (archive 17909)
- [ ] `role="dialog"` markup is copy-pasted 25 times. `ModalBase` captures the *event* contract (archive 17917)

### Ship-blockers — trading providers and the SDK order contract (status unclear, check)
- [ ] Tradier equity sides ignore position effect: `sell` vs `sell_short`, `buy` vs (archive 18167)
- [ ] `ReconnectingWebSocket` — three defects in the SDK's shared socket. `SendAsync` is (archive 18276)
- [ ] `KrakenFuturesProvider.cs:367` silently turns an IOC request into a GTC order — it sets (archive 18282)
- [ ] `InteractiveBrokersProvider.cs:649-668` auto-confirms every IBKR risk warning — up to 8 in (archive 18288)
- [ ] `PolygonProvider.cs:126-131` hardcodes the 15-minute-delayed WebSocket while (archive 18291)
- [ ] `FinnhubProvider.cs:155` processes only the last trade in each batched frame (archive 18295)
- [ ] Capability flags declared with nothing behind them. IB declares `L2` and does not implement (archive 18298)
- [ ] `BaseMarketDataProvider.GetCapability<T>` returns null for everything except market data, (archive 18306)
- [ ] No `CancellationToken` on `IMarketDataProvider` or `ITradingProvider`, while (archive 18327)
- [ ] `BarBucketConsolidator.cs:52` drops every negative-valued tick (`tick.Open > 0 && …`), so (archive 18331)
- [ ] `ProviderResult<T>` has zero callers repo-wide, and `ProviderErrors`/`SurfaceError` has (archive 18430)

### Duplication swept 2026-08-22, and what the sweep found (status unclear, check)
- [ ] 36 copies of the indicator parameter accessors, in 15 distinct implementations, and they (archive 18601)
- [ ] `ValueDeviationProvider.Ema` is a different algorithm from every other EMA in the tree. (archive 18619)
- [ ] Three near-identical JSON-backed list stores. `JsonScreenerLibrary`, (archive 18633)

### Correctness / duplication debt found by this audit (status unclear, check)
- [ ] `VolumeProfileLevelProvider:61` silently falls back to a known future-leaking profile. (archive 18656)
- [ ] Three candle classifiers — and the 2026-08-20 entry understates it on two counts. That (archive 18664)
- [ ] ATR is hand-rolled five times with different warmups. `ChartPatternDetector.cs:675-699` (archive 18693)
- [ ] Signal polarity is classified twice and they disagree. `NavigationSonifier.IsPositiveSignal` (archive 18719)
- [ ] Order-failure classification exists three times in three non-equivalent forms. (archive 18739)
- [ ] "Order placed" is spoken unconditionally, including for orders the broker refused. (archive 18748)
- [ ] A forced liquidation is announced as an ordinary fill. `PaperTradingProvider` builds (archive 18752)
- [ ] Paper order updates are published synchronously while the account lock is held. `Emit` → (archive 18757)
- [ ] Stops and take-profits fill at the trigger even when the bar gapped through it. (archive 18771)
- [ ] Realized P&L excludes fees, so the spoken result of a trade is wrong by the round-trip (archive 18776)
- [ ] `GetBalancesAsync` nets a short against a long in the same base asset, so a BTC/USDT long (archive 18778)

### Documentation drift (status unclear, check)
- [ ] README is two to three releases stale in its most prominent sections. Line 14 still says (archive 18872)
- [ ] `ServiceCollectionExtensions.cs:487-490` (WebHost) describes a silent unsandboxed (archive 18896)

### Untested code that decides money, security, or research truth (status unclear, check)
- [ ] The block bootstrap is non-circular, so the sample edges are under-drawn. Found while (archive 19257)
- [ ] Zero tests for (MOSTLY CLOSED 2026-08-22 — five clusters tested, two remain): (archive 19355)

### Flake risk (status unclear, check)
- [ ] OBSERVED 2026-08-25 — `LinuxBwrapSandboxTests.A_script_cannot_read_the_hosts_environment` (archive 19494)
- [ ] No time abstraction anywhere. Every timeout, throttle, debounce, poll interval and (archive 19547)

### Tests that should exist and do not (status unclear, check)
- [ ] Orphaned legs after a manual close: close the position, drive price to the stop, assert no (archive 19620)
- [ ] The focused pump must not deliver a tick for a different identity. Set focus to B, push an (archive 19622)
- [ ] Replace `DataOrchestratorResilienceTests`' hand-copied `Transition` switch with a test (archive 19651)

### Open after the 2026-08-20 hosted-tier and audio batch (status unclear, check)
- [ ] `trader_local.db` is not under `Accounts__DataRoot`. It resolves to (archive 19832)
- [ ] `OhlcvStore` has no schema migration path. `EnsureCreated` will not alter an existing (archive 19837)
- [ ] Server-side state migration for existing hosted installs. Workspaces, `alerts.json` and (archive 19840)
- [ ] The analytics cache key has no user or credential dimension. Correct today — hosted keys (archive 19852)
- [ ] Three candle classifiers. `BarDetailService.ClassifyBar`, (archive 19855)
- [ ] Upper and lower wicks share a waveform. They are now correctly distinguished by pitch (archive 19860)
- [ ] `AccessibleTrader.BlazorClient` cannot be built on this machine (`NETSDK1147: (archive 19871)

### Terminal / lab split (2026-08-01) (status unclear, check)
- [ ] Bundle export from the terminal. Import exists; the app can only write the old (archive 19895)
- [ ] Per-spec confirmation before starting an `ExecutionMode.Auto` import. The count (archive 19898)

### Platform tiers + signal service — designed, not built (2026-08-02) (status unclear, check)
- [ ] `CoinMarketCapProvider` — now optional. Only worth it for fields CoinGecko lacks (archive 19935)
- [ ] `SecEdgarProvider`, `GdeltProvider` — larger, gated on the revision-breadth result. (archive 19937)
- [ ] Signal service LAST, when there is more than one control-tested edge to serve. (archive 19938)

### Company / macro data layer — designed, not built (2026-08-02) (status unclear, check)
- [ ] Start the GDELT recorder. (archive 19963)
- [ ] First test if the layer is greenlit: analyst estimate revision breadth, through the (archive 19964)

### Post-2.0 polish batch (2026-07-26) (status unclear, check)
- [ ] CoinGlass + extra trading brokers (Tastytrade / Bybit / Kraken Futures / (archive 20010)

### Provider system — quality pass (2026-07-24 → 07-25) (status unclear, check)
- [ ] Coinbase live candle volume. Live candles are synthesized from the (archive 20032)
- [ ] MEXC live-price responsiveness on thin markets. The live title price is (archive 20037)
- [ ] Migrate the other crypto providers onto the shared REST base. MEXC uses (archive 20045)

### Follow-ups (status unclear, check)
- [ ] Keyed-feed DataManager refactor if tick-level background evaluation is ever (archive 20136)
- [ ] Auth: HaveIBeenPwned password validator — k-anonymity range API, (archive 20180)
- [ ] Ops: systemd `UMask=0077` drop-in on the hosted unit so future files (archive 20189)
- [ ] FINRA Query API short-interest metric (needs free dev registration — Cody). (archive 20201)
- [ ] Tiered RiskPercent by setup quality (2-tier, evidence-based) in RiskPlan. (archive 20202)
- [ ] Sector risk governor as an optional *enforcing* mode (default stays warn-only). (archive 20203)
- [ ] Pre-registered lab test: wider ladder (3R/6R or trail) for the gated v23 variants. (archive 20204)
- [ ] Schwab streamer ACCT_ACTIVITY (~2-3d, riskier) — requires the full Schwab (archive 20226)
- [ ] MAUI native window title: add the live price (WebHost browser-tab title has it, (archive 20231)

### [2026-07-22] — Local background monitoring core (SHIPPED) (status unclear, check)
- [ ] Factory sound bank — Cody's event WAVs as bundled defaults per earcon key (archive 20248)

### [2026-07-22] — My Data: CSV import (Wave 2 CSV item SHIPPED) (status unclear, check)
- [ ] Windows tray VERIFY — MAUI head close-to-tray (H.NotifyIcon.Maui 2.3.0, (archive 20289)
- [ ] Hosted server-side alerts + Web Push — evaluate saved alerts server-side (archive 20303)
- [ ] xlsx import — future; needs a spreadsheet-parsing dependency. The (archive 20307)

### Mute-tier redesign (the F-key row) (~2-3d total, ship as one coherent change) (status unclear, check)
- [ ] Shift+F4 dedicated braille display picker modal — PARTLY CLOSED 2026-09-06. (archive 20332)

### Follow-ups raised by this pass (status unclear, check)
- [ ] Mexc full contract enrollment after per-plugin dependency folders land. (archive 20380)

### Still open (the honest remainder) (status unclear, check)
- [ ] Native touch layer — iOS adjustable `UIAccessibilityElement` + rotor (archive 20417)
- [ ] Play range — sequencer needs an end-index concept; shift+click summary (archive 20424)
- [ ] Price-axis drag to scale (needs a manual y-range override in state); (archive 20426)
- [ ] "Recent events" visual journal ticker (visual earcons cover the alerting half). (archive 20428)

### Phase D remaining (deferred, tracked) (status unclear, check)
- [ ] "Recent events" visual ticker surfacing the Journal ambiently (deaf/HoH). (archive 20467)

### Phase C second pass (native, deferred — needs macOS + physical devices) (status unclear, check)
- [ ] iOS: `UIAccessibilityElement` with adjustable trait over the canvas (archive 20497)
- [ ] Android: `ExploreByTouchHelper` virtual nodes (adjustable first; per-pane (archive 20500)
- [ ] On-device VoiceOver + TalkBack verification of the shipped web layer inside the (archive 20502)
- [ ] JS test infra (Vitest) so the gesture state machine gets direct tests (Tier-4). (archive 20504)

### Phase B second pass (deferred, tracked) (status unclear, check)
- [ ] Price-axis drag to scale; time-axis drag to zoom; double-click axis to reset. (archive 20545)

### Deliberately deferred (tracked, not forgotten) (status unclear, check)
- [ ] Plugin trust manifest cryptographic signing (hash-based TOFU fine until (archive 20588)
- [ ] Per-user (post-auth) rate limiting. (archive 20590)
- [ ] CAPTCHA-alternative / accessible fallback flow on 429 for shared-IP users. (archive 20591)

### Open (status unclear, check)
- [ ] Multi-layer patch preview vs component parity — verify by ear that the (archive 20706)
- [ ] `AudioSequencer.BuildVoicePlan` slot packing / budget-overflow / (archive 20718)

### Medium-value tests still pending (status unclear, check)
- [ ] `WebHostProgramStartupSmokeTests` (1 case) — boots host via (archive 20859)
- [ ] `DiagJournalEndpointTests` (2 cases) — empty / newest-first. (archive 20863)

### WebHost phases — L5 / L6 / L7 / L8 / L9 (L6 docs is the only one still open) (status unclear, check)
- [ ] L8 — Desktop shell. SKIPPED per user choice (2026-05-16). (archive 20934)
- [ ] Shared connection pool keyed by symbol (per-visitor upstream connections are (archive 20951)
- [ ] Follow-ups: transactional email (confirmation + password reset); tier (archive 20961)

### Cosmetic + known issues (status unclear, check)
- [ ] Chart pixel density. Server renders at fixed 1280×720; CSS (archive 20967)
- [ ] Drawing-tool mouse interactions in browser. With the chart (archive 20985)

### Empirical verification (status unclear, check)
- [ ] PgDn/PgUp cycle skips price-line and produces sensible top/bottom (archive 21105)
- [ ] F4 pause resumes cleanly after workspace identity change (auto-reset). (archive 21107)
- [ ] Pan keys empirically belong to graphic vs strip on the actual device. (archive 21108)
- [ ] Strip X-value timeout (~1.5 s) feels right under live arrow nav. (archive 21111)
- [ ] Body+wick+gap structure is tactile-distinguishable at 4 dot rows (archive 21112)
- [ ] Dot Pad X on-device verification. The Dot Pad X is supported (same (archive 21115)

### Out of scope (deferred to future iterations) (status unclear, check)
- [ ] 3-pane mode. F-key chord toggle (e.g. F12) cycling 1 / 2 / 3 (archive 21125)

### Empirical verification (after each ship) (status unclear, check)
- [ ] Splash actually centers in 60×40 with the chosen font table. (archive 21141)
- [ ] PgDn/PgUp cycle skips price-line and produces sensible top/bottom (archive 21142)
- [ ] F4 pause resumes cleanly after workspace identity change (auto-reset). (archive 21144)
- [ ] Pan keys empirically belong to graphic vs strip on the actual device. (archive 21145)
- [ ] Strip X-value timeout (~1.5 s) feels right under live arrow nav. (archive 21146)

### Important security (status unclear, check)
- [ ] Sign ScriptWorker.exe + plugin DLLs. Use a code-signing cert; verify (archive 21317)
- [ ] Coinbase + remaining-providers credential-checkout migration. (archive 21320)
- [ ] CPU quota uses sliding window. `OutOfProcessScriptHost.cs:262-301` (archive 21324)
- [ ] Sync-over-async cleanup (3 sites). `StrategyAutoLoader.cs:73-74` (archive 21327)

### Build-system safeguards (status unclear, check)
- [ ] RCL platform-code Roslyn analyzer. Error on (archive 21334)
- [ ] DI lifetime validator. Add a startup pass that walks the registered (archive 21338)

### Documentation (status unclear, check)
- [ ] Customer-facing README rewrite. Current README is 90% architecture. (archive 21345)
- [ ] USER_GUIDE coverage gaps. Add sections for: strategy building, (archive 21348)
- [ ] Sample plugin DLL. Ship `Plugins/Samples/SimpleRsiProvider.cs` (or (archive 21351)
- [ ] `Tests/README.md` — fixture conventions, `BlazorTestHarness` recipe, (archive 21354)

### Architectural follow-ups (post-1.0; large) (status unclear, check)
- [ ] God-modal split. `PropertiesModal.razor` (826 lines), (archive 21359)
- [ ] `WorkspaceStore` immutable snapshots. Replace direct property (archive 21364)
- [ ] Plugin manifest v2. Version, signature chain, capability declaration (archive 21367)
- [ ] Modal close-on-Esc + open-on-app-key as single dispatcher cases. (archive 21370)
- [ ] Per-strategy timeout override. `OutOfProcessScriptHost` (archive 21372)

### Genuinely-future research (unblocked, not backlog) (status unclear, check)
- [ ] AssetClassifier on equity snapshots. Pull TSLA/AMZN/SPY via the Alpaca (archive 21408)
- [ ] `v23r-SRRES` fires ZERO times in nine years and is a broken cell, not a null. (archive 21442)
- [ ] v23or weekly cross-asset. OR-gate may broaden coverage on smaller-cap (archive 21458)

### Open follow-ups for v22 (revised after walk-windows analysis) (status unclear, check)
- [ ] Default `walk-windows` over `walk` for strategy validation. (archive 21740)
- [ ] ConfirmThreshold sweep — try 0.5, 0.6, 0.7, 0.8 on v22-LONG (archive 21747)

### Open follow-ups for the timeframe work (status unclear, check)
- [ ] Cross-asset 1d rolling-window for v22-LONG. Walk-windows says (archive 21799)
- [ ] Weekly confirmation across other markets. With adaptation, (archive 21804)
- [ ] Score-component adaptation review. The current weekly (archive 21808)

### Remaining follow-ups (optional, no security impact) (status unclear, check)
- [ ] On-device / on-AppContainer integration tests — a test (archive 22633)

### Next priorities (broader codebase audit — 2026-04-17) (status unclear, check)
- [ ] Toolbar button labels showing raw shortcut keys (surfaced (archive 22714)
- [ ] Phase 5 v2: order book large-order detection. Adaptive (archive 22722)

### Open gaps — next session (status unclear, check)
- [ ] Delete redundant BNVISION_FUNDING / BNVISION_OI lab providers — now duplicating what Core FUNDING_RATE / OPEN_INTEREST provide. Stil... (archive 22793)

### Strategy work — future (status unclear, check)
- [ ] Cross-asset matrix rerun with v18 asset-aware: once Core providers accept `__symbol`, re-run v18 on ETH/SOL/XRP/DOGE in LIVE mode to... (archive 22800)
- [ ] v13/v14/v15 cross-asset walk-forward — never tested on non-BTC. May reveal additional survivors or confirm BTC-specialist pattern. (archive 22801)

### Research / Next Session (status unclear, check)
- [ ] Adaptive WT thresholds for Cipher B — dynamic OB/OS levels based on oscillator's own distribution (percentile-based) (archive 22849)
- [ ] Pulse indicator simplification — consider decomposing v1/v2/v3 signal tiers into strategy conditions (archive 22850)
- [ ] Phase 12 Session 3 — v9 backtest + thesis validation (archive 22851)

### Phase 11 Audit Fixes (2026-04-07) — DONE (status unclear, check)
- [ ] Custom Script tab Roslyn strategy persistence — Roslyn-compiled strategies still aren't saved as `StrategySpec`s. (archive 22983)

### Session 3 — v9 backtest + thesis verdict (planned, next session) (status unclear, check)
- [ ] Test the refactor + CrowdingIndex end-to-end — confirm shared cache means no duplicate fetches when multiple cross-series indicators... (archive 23016)
- [ ] Run v9 backtest on BTC/USDT 1h Bitstamp, recent 30-90 day range (OKX history depth) (archive 23017)
- [ ] Verdict on the strategy thesis — does v9 produce materially different walk-forward metrics than v7/v8? (archive 23018)

### Session 4 — Glassnode + multi-asset (deferred) (status unclear, check)
- [ ] `GlassnodeProvider` plugin — when API key is purchased. Deep history (back to 2019) for funding/OI/sentiment. Same source-name swap... (archive 23022)
- [ ] Multi-asset support — once Glassnode is in or v9 thesis validated, expose `Source` and `Symbol` as string parameters on each cross-s... (archive 23023)

### Session 5 — Glassnode plugin (deferred — paid) (status unclear, check)
- [ ] `GlassnodeProvider` — when API key is purchased. Same pattern as OkxDerivatives. Unlocks deep history for funding/OI/on-chain that t... (archive 23027)

### CommandDispatcher — Chart-Focus Gate + Structural Clarity (status unclear, check)
- [ ] Full Command Pattern (extract `ICommandHandler<T>` per domain) — deferred to Phase 5+ if dispatcher grows beyond current size. (archive 23132)

### SkenderIndicatorProvider — GetDetailFact Extraction (status unclear, check)
- [ ] Split `SkenderIndicatorProvider` into `SkenderIndicatorDiscovery` + `SkenderResultMapper` — deferred to Phase 5+ when a second Skend... (archive 23145)

### Feature Completion (status unclear, check)
- [ ] Multi-Symbol Watchlist: Extend `WorkspaceState` to hold collection of `ChartState`. (archive 23401)

### Performance (from previous Phase 6) (status unclear, check)
- [ ] Span-Based Indicator Pipeline: `ReadOnlySpan<Ohlcv>` + `ArrayPool<double>` in `SkenderIndicatorFactory`. (archive 23413)
- [ ] Full Channels Migration: `Channel<Ohlcv>` from plugin → `DataManager` for live ticks. (archive 23414)

### Phase 10-D — Custom Indicator Platform (Roslyn) ✅ (status unclear, check)
- [ ] `ICustomScriptService.RunScriptAsync` full pipeline: Compiled `ICustomIndicator.Calculate` routed through `IndicatorOrchestrator` →... (archive 23574)

### v7 strategy build (AFTER infrastructure is in place) (status unclear, check)
- [ ] Build v7 — Score-based multi-source confluence (archive 23810)

### Bigger systemic improvements (future, lower priority) (status unclear, check)
- [ ] Walk-forward parameter optimization (expanding window) — re-tune Cipher periods every N months using only data prior to that point.... (archive 23821)
- [ ] Regime classifier with regime-conditional strategy routing — classify each bar as trending/ranging/volatile via ADX + ATR percentile... (archive 23822)
- [ ] Indicator-on-HTF computation (deferred from Session B) — sync `IIndicatorRunner` so HTF leaves can reference indicators not just pri... (archive 23823)
- [ ] Expand SignalCatalog companion-array support — generic mechanism for indicators to expose multiple value streams per component (not... (archive 23824)

## Where the rest went

Everything else is in `docs/archive/TODO_HISTORY.md`: the 2026-08 audits (A1, A2, A3, B1, B2), the 2026-09
START HERE passes, the health-assessment write-ups, the phase plans from March and April 2026, and the old
release sections. Open `docs/CHANGES.md` for what shipped.

