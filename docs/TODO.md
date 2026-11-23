# TODO — Accessible Trading Terminal

This file lists only what is LEFT to do. What was done, with its caveats, is in `docs/CHANGES.md`.
Finished items and the full wording of older ones are in `docs/archive/TODO_HISTORY.md`: the whole file
as it stood on 2026-11-04 at its top, and this file's top half as it stood on 2026-11-21 (before it was
condensed) at its end. Line numbers below like "archive 9300" point into that file.

## How to work from this file

The rule that has held since 2026-08-25: **demonstrate the defect, or mark it explicitly unverified.**
Reading code for bugs filed 283 findings in 2026-08 and about half were wrong; compiling the escape or
running the sabotage is what found real ones. Test one thing, write down what the test could not show.

The order:

1. **Heard by Cody.** Everything built since 2.13.1 up to 2.14.1 is heard and verified (Cody,
   2026-11-21: the leaderboard, Cipher A above the candle, "all of the built but unheard stuff"), and
   from 2.15.0 the settings, the Donate tab, the 9-13's reading and Cipher B's "9 components" (Cody,
   2026-11-22). What is left to hear is under "To hear". What he says comes first.
2. **Trading through the venues** is built but has not been tried on a demo, practice or real account.
   Treat it as unverified until an account answers.
3. Cody's open questions (**Q**), then the open items below, then the older audit findings at the end.
   Those were filed in 2026-08 and earlier; the 2026-11-21 pass re-checked the indicator,
   accessibility and workspace ones (see its note at the head of that tail). The rest have not been
   re-checked: check CHANGES and the code before taking one.

Standing facts: the full test suite runs on GitHub Actions at the push, not locally; build in a worktree,
never in the main checkout under a running server; the DBSI study is closed (Cody, 2026-11-17).

## Cody to do

- **Check the website rewrite (2026-11-22, see CHANGES).** Signed in, open accessibletrader.com/chart:
  it should go straight to your terminal (untested with a real session). Then read /status, the home
  page and Download with your screen reader and say what jars. /status must be updated at each release.
- **Turn on the website's email list.** It is live (the "Get the news by email" box) and saves signups,
  but sends nothing until:
  1. **A Brevo API key**: app.brevo.com, the menu under your name, **SMTP & API**, **API keys**,
     **Generate a new API key**, named `accessibletrader-site`. Copy it at once (shown once; starts
     `xkeysib-`). Not the SMTP key the terminal's password emails use.
  2. **On the server**: `ssh debian@codyhurst.com`, open `/home/cody/site_env/.env_trade_codyhurst_com`
     in nano (with `sudo` if it will not save), add `BREVO_API_KEY=` and the key, no quotes. No restart.
     Check with `php /home/debian/att-mail/newsletter.php status` ("sending set up: yes"; it never
     prints the key).
  3. **A postal address** for the foot of list emails (US CAN-SPAM; a PO box is fine). Tell me and it
     goes in `site_settings.mail_postal_address`; until then `send` refuses.
  4. **The 10-minute confirmation timer** (my install needed sudo and was refused): run
     `sudo cp /home/debian/att-mail/systemd/att-mail-confirm.* /etc/systemd/system/`,
     `sudo systemctl daemon-reload`, `sudo systemctl enable --now att-mail-confirm.timer`, or tell me I
     may. Without it, I run `newsletter.php confirm-pending` by hand.
  5. Optional: Brevo's open and click tracking for transactional mail (on or off is your choice); the
     sender domain is likely authenticated (the password emails arrived 11-20) but unchecked.
  6. Tell me: I run `confirm-pending`, send you a test issue, and after you have read it, the first
     real issue goes out with `send ISSUE.md --confirm` (Markdown, starting with a `Subject: ...` line).
- **Screen-reader pass of the Windows setup** (NVDA or JAWS). The runner installs, upgrades and
  uninstalls it silently and checks every file; nobody has gone through its screens.

- **Try 2.15.0** (released 2026-11-22; the VPS runs the same build): the Windows setup or zip from
  the download page. What is not yet heard is listed under "To hear".
- **Look at the chart's two 2.15.0 fixes** (Cody's 2026-11-22 screenshot): the date under the mouse
  pointer no longer sits beside half a date ("Oc"), and Cipher A's symbols over the highest candles
  are drawn whole below the legend. Neither has been seen by Cody.
- **Look at the rest of the dialogs** (2026-11-21 layout pass, CHANGES; on the VPS since 13:37 UTC):
  Settings > Keyboard, the Levels report, and anything at a narrow window. The dashboard and tables:
  Cody, 11-21, "read better now".

## To hear (in 2.15.0, built 2026-11-21 and 11-22 from Cody's notes, not heard)

Everything before this, from 2.13.1 to 2.14.1, Cody verified on 2026-11-21. On 2026-11-22 he heard the
settings ("looks good on the short text"), the Donate tab (its BNB Smart Chain row is now one with
Ethereum, Arbitrum and Base, as he asked), the 9-13 sequential ("reads good") and Cipher B's "9
components". Caveats for these are in CHANGES (2.15.0).

- [ ] **Invert the price pane, `I`** (2026-11-22, not in 2.15.0): press `I` on a chart. It should
  say "Price pane inverted..." and arrowing should sound a rising market FALLING in pitch, while the
  words still say the real prices and "up". An RSI pane below should sound as before. `I` again says
  "Price pane upright". Ctrl+Alt+Shift+Y (the chart layout) names it; switch tabs and back, it stays per tab; save and reopen
  the workspace, it comes back. A line or alert placed with the mouse on the inverted pane should
  land on the price you clicked. If a Dot Pad is ever connected, `I` should turn the candles over on the pins
  (the top half when candles are focused) and leave an indicator pane's half as it was.
- [ ] **Supertrend, ZigZag, Williams Fractals, Pivot Points High/Low** (2026-11-22, not in 2.15.0):
  add each from Alt+A, Overlays, on your BTC 4h chart and arrow along it. Supertrend should say
  "Supertrend up, support ..." and ping at each flip; ZigZag should say the leg ("falling leg, ...
  down 6.2%") and "still moving, not confirmed" on the last one; the fractal and pivot marks should
  say their price. Do the tones sit apart from Market Structure's on the same chart?
- [ ] **Smart money overlays** (2026-11-23, not in 2.15.0): add Fair Value Gaps, Order Blocks,
  Liquidity and Premium and Discount from Alt+A, Overlays, on BTC 4h. Arrowing over a gap band
  should say "Open gap up, ... to ...", and a new gap should ping on its third bar. A sweep should
  say "Swept the high ..., closed back below". Premium and Discount's middle line should say
  "Equilibrium ..., price in premium". Six marker tones on one chart: can you tell them apart?
  In the strategy builder, none of them should be offered.
- [ ] **Alerts on the chart they were set on** (2026-11-22, not in 2.15.0): your six BTCUSDT tabs,
  background monitoring on, a "price touches" alert set on the 4h tab at a price only the daily
  candle reaches. It should stay silent; one at a price the 4h candle reaches should fire once.
  Switch tabs back and forth with an RSI "crosses" alert set: nothing should fire. Old alerts with no
  timeframe still fire on every tab of their symbol; set them again.
- [ ] **Volume on Heikin-Ashi** (2026-11-22): the two BTC 4h bars that sounded up but were said red
  should now sound down AND say "down", matching their red paint. Every pane below the price pane
  reads the market's candle; the price pane reads Heikin-Ashi.
- [ ] **Indicators live on a busy market** (2026-11-22): Cipher B on your 15m chart, leave it a few
  minutes without switching tabs; the waves and money flow at the newest bar should keep changing
  with the price. Before this, every tick was cancelled by the next one while the price moved fast.
- [ ] **The 9-13 against TradingView** (it reads right, Cody 2026-11-22): put a community
  "Sequential" script on the same chart and compare where the 9s and 13s land; the 9s should match
  any of them, the 13s only one using the same published recycle. **Q:** should narration as a bar
  closes say "perfected" too (needs a template word)?
- [ ] **Announce nearby levels while navigating** (Settings, Speech): off, no "Near resistance".
- [ ] **The three workspace audit fixes** (2026-11-22): a stock or currency balance priced on a
  broker; the heatmap's history on an hour or day chart; a strategy saying a level source failed.
- [ ] **Near support and resistance on every level** (pivots, Cipher A pivots, profile levels, your
  lines, price alerts), each named after the price.
- [ ] **No candle pattern said unprompted on line-style charts.**
- [ ] **"Marker" wording, the heatmap under Add Indicator.**
- [ ] **The audit-tail fixes you can hear:** Shift+F12 with nothing focused; a tab key with nowhere
  to go (1 to 9 past the last tab, Delete on the only tab, Home on the first) in the tab bar, whose
  key handling has no test; Pulse's Alt+Shift+D on its first bars; a quick trade on a euro pair.
- [ ] **The manual:** read a chapter or two as a newcomer would; say what still assumes too much.
- [ ] **Headlines from Finnhub only** (2026-11-22): with the Finnhub key, stock and
  coin stories still said; the Alpaca checkbox gone from Settings → Narration; the dossier's Recent
  news footnote names Finnhub alone.

Cody's decision (2026-11-22): **no Website button in the signed-in terminal.** Clicking Chart on the
website opens the terminal without the site's navigation; he passed on adding a way back.

Open questions from Cody's seven notes (2026-11-22):

- **Arrowing after Home or End:** Left and Right then read one component, not the whole bar. Keep
  that, or have Home and End keep whole-series reading when you came from PageUp or PageDown?
- **The Strategy Lab:** offer its exported strategy list (a JSON) as a download, or leave it in the
  repository only?
- **Phones:** to try Android, a CI job building an APK (not built yet); for iOS, a Mac with Xcode and
  an Apple ID (a free one installs for 7 days; TestFlight needs the paid developer program). Until
  then, phones use the web version.

Open questions kept from the heard items:

- **Headlines Q:** is the importance table right for EDGAR filings (should a Form 4 insider trade be
  medium for a held stock?) and for the wire (plain "declares quarterly dividend" moved from medium to
  low; is a minute per symbol the right cap)? Too many Finnhub stories per event (twelve on one AMD
  short-seller report on 2026-11-20)? If so, a per-symbol cap per quarter hour, or Medium and up only.
  Should a first look at a stock also say a filing from the last 15 minutes rather than 5?
- **Unusual moves Q:** the 46-bar median and the 5x line are my choice, not tuned on data.
- **Morning brief:** with market events off it falls back to the important ones (my choice).
- **Chart types Q:** is a brick's direction right in the type slot? Line break reads as "Block".
- **Unchecked by anyone:** the Dot Pad's pulse line (no Dot Pad here; Grade 2 may make an item longer
  or shorter than the 20 cells); slash for the Go bar on a German keyboard (Shift+7); the new keys
  (`,` `.` `'`) with a screen reader on a real browser other than Chromium.

## News desk: what is left

- [ ] **SHELVED (Cody, 2026-11-21: "shelve the headline notification for later"): market events and
  headlines while the browser is closed.** Countdowns, read-outs, watched events and headlines are said
  only while a session is open, and Alt+U does not record a market event released with the browser
  closed. It needs ONE process-wide owner, as `LocalBackgroundMonitor` and `CircuitAlertCoverage` are for
  alerts: with a plain second subscriber, a closed tab's circuit (alive about three minutes) and the
  headless session would both speak it, and the bell would ring twice. Not to be started until Cody
  brings it back.
- [ ] **Measure Finnhub's listing delay** (a story's `datetime` to the moment it is listed) during US
  weekday hours; the 2026-11-20 recorder ran after the close.
- [ ] **Headlines on screen**: nothing is drawn on the bar for a headline yet (sound and speech only).
- [ ] **The kept headline list is in memory**: a restart empties it (the backfill refills the focused
  chart's own).
- [ ] **WebHost: one Alpaca socket per browser circuit** (only with the Alpaca switch on). Alpaca allows
  one news connection per account, so a second window gets 406 and waits five minutes. A per-user shared
  socket fixes it. Finnhub is polled per window (each at most 15 a minute of the key's 60).
- [ ] **Unchecked**: the Alpaca coin symbol form (`BTCUSD` assumed; no coin story came) and its
  402/406/409 codes; class shares (BRK.B) in EDGAR and Finnhub; foreign listings' (20-F) company names.
- [ ] **The stored US release dates end 23 December 2026** (refreshed 2026-11-21). Without a FRED key
  no CPI, jobs, PPI or GDP countdown shows after that. Refresh with `scripts/build_market_events_seed.py`
  once FRED lists the 2027 schedule (it had none on 2026-11-21), before the release after that date.
- [ ] **Never run end to end against the live services from the app**: EDGAR's poller (parsers pinned
  by real captures), Finnhub earnings (shapes checked live), FRED (no network where it was built),
  Kalshi (fixtures are real responses).
- [ ] **Earnings**: no countdown for a coming report (Finnhub gives a day and a session, not a minute);
  dividends and splits not built; how soon Finnhub fills in the actuals after a report is not measured.
- [ ] **Token unlocks: blocked.** DefiLlama's unlocks API now wants its paid plan (2026-11-18); needs a
  paid source or Cody's decision.
- [ ] **CryptoPanic**: only if crypto news proves thin (Finnhub's crypto list is Cointelegraph and
  CoinDesk, about 90 stories over two days). If added: the credit goes in the user manual, never in
  speech, and check its terms first.
- [ ] **Companies' own press releases (RSS)**: assessed, not needed now; the releases that move a stock
  are 8-Ks, which EDGAR gives within a minute. Revisit only if Finnhub's listing delay proves long.

## Found by the website screenshots (2026-11-21)

Seen in the HEAD build shot on the VPS (an isolated copy on port 5199, Chromium, 1280x760). Each one is
what the screen showed; why is not yet proven unless it says so.

Fixed 2026-11-21 (CHANGES, "every dialog laid out again"): the "Data link: HistoricalFilling" line, the
positions table's one-letter columns, the half-empty Renko first view, the paper banner's emoji box.

Seen in the second shoot (c51a402f) and answered by Cody, 2026-11-21: Renko's wide right margin with
few bricks, the ticket's clipped lists, the narrow Find box and the release dates ending in July are
fixed (CHANGES, "Renko's narrow margin"). The symbols drawn as text (⚙ ⚠ ✔ ✕) stay: "text symbols are
ok / already known". The hosted terminal's Compile button failing (no bwrap on the server) is not
needed: "don't worry about the custom scripts on the hosted vps".

- [ ] **Dialogs in other browsers**: the 2026-11-21 layout pass measured Chromium only. Check Firefox,
  the Windows desktop app's WebView2 and a real phone.

## Asked for, not built yet

- [ ] **Shared charts** (Cody, 2026-11-22: "share charts only you can change", "for later"). Design in
  [SHARED_CHARTS_DESIGN.md](SHARED_CHARTS_DESIGN.md), on Cody's answers: **live** (his edits reach
  viewers in real time, the viewer's tab named "..., Cody's chart, read only"), **anyone can share**,
  every share read-only to all but its owner, **link-only**. Second round (11-22) answered with
  recommendations to confirm (question 5 in that file): one or several picked tabs, never the
  session; chart notes saved with the tab; optional sign-in from the app, never for keys; shares open
  in the app from a pasted link with no account. **Cody accepted all of these on 2026-11-22** ("I
  like your additions"); the design file says so. Stage 0 (chart notes, an Account tab in Settings
  that explains itself) is useful on its own. Not started.
- [x] **Invert chart on `I`**: built 2026-11-22, see CHANGES and "To hear". Left out on purpose:
  the picture the AI analyst is sent stays upright (it is read as the market, not as the screen);
  templates do not carry it (it is a way of looking at one tab, not a
  layout); the status line does not name it (it does not name log scale either). The braille chart turns over
  too (Cody 2026-11-23: "Yes, the braille chart should also invert"), built the same day.
- [ ] **A larger indicator library** (Cody, 2026-11-22: "I really want to expand our indicator library
  and make it super robust"; then "work through these, adding all of the ones you listed to the
  inventory that are missing", and for SMC "test and report on the value provided"). Each group is
  checked against the suite first so nothing is added twice. We have about 50 classic studies (Skender) and 30 of our own. The gaps
  against TradingView's built-ins, in the order proposed (most useful read aloud first):
  1. ~~**Supertrend**, **ZigZag**, **Williams Fractals**, **Pivot Points High/Low**~~: **built
     2026-11-22** (see CHANGES); not heard, not in the demo allow-list yet.
  2. ~~**Smart money concepts**: fair value gaps, order blocks, equal highs/lows, liquidity sweeps,
     premium/discount~~: **built and graded 2026-11-23** (see CHANGES, `docs/archive/SMC_FINDINGS.md`).
     No information in any of the five concepts or three classes. In crypto, sweeps were followed by
     continuation and premium beat discount. On the chart, withheld from the strategy builder; not
     heard.
  3. **Volume and order-flow**: Volume Delta / CVD (needs trade-side data, which MEXC's deals stream
     has), Klinger, Price Volume Trend, Net Volume, Relative Volume at Time.
  4. **Trend/oscillators**: Elder-Ray, Fisher Transform, Coppock, Know Sure Thing, Linear Regression
     channel and slope, Relative Vigor, SMI Ergodic, Awesome/Accelerator Oscillator, Woodies CCI,
     Bull Bear Power, Chande Kroll Stop, McGinley Dynamic, Least Squares MA,
     Envelopes, Moving Average Ribbon.
  5. **Session and time tools**: session high/low and opening range, previous day/week/month
     high-low-close, day-of-week separators: each a level the "Near resistance" speech can name.
  6. **Auto patterns**: auto Fibonacci retracement from the last swing, auto trend lines, chart
     pattern recognition (ChartPatternDetector exists for strategies; give it a visible indicator).
  Every one ships with a spoken form, sonification and the usual causality test (no future bars).
  LuxAlgo is a TradingView vendor: paid toolkits (Signals & Overlays, Price Action Concepts,
  Oscillator Matrix) and many free open-source scripts (their free "Smart Money Concepts" is one of
  TradingView's most used). Their paid code is closed; their free scripts are CC BY-NC-SA, so we can
  learn from them but not copy them into this repo; ours are written from the published definitions.
- [ ] **Kill switch** (Cody, 2026-11-20). Design to agree with Cody before building:
  1. **Close everything** on one key: cancel every working order and close every position on the
     focused venue (then optionally all venues), after one spoken confirmation that says what it will do
     ("Close 3 positions and cancel 5 orders on MEXC?"), then read back what each venue answered.
  2. **A daily loss limit** you set (an amount or a percent): once the day's realised loss reaches it,
     new entries are refused out loud until the next day; stops, targets and closes still work. Open:
     the day's boundary (local or UTC), whether unrealised loss counts, whether it can be lifted early.
- [ ] **"Add symbol (own pane)"**: a third Compare mode putting any of the 17 analytics sources (FRED,
  Glassnode, DefiLlama, …) in its own pane under a price chart, with a picker from the toolbar's choices.
  To solve: FRED dates a figure by the period it measures, not its release (needs a release delay or
  ALFRED vintages). Untested: whether Compare already accepts an analytics market.
- [ ] **Formula charts** (BTC/ETH, spreads, baskets): a "Formula" source with a builder and a typed form
  (`Bitstamp:"BTC/USD" / Bitstamp:"ETH/USD"`); legs matched by time, last value held when one market is
  closed; high and low approximate; no volume; never tradeable.
- [ ] **Full Pine Script in the script editor** (Cody, 2026-11-19: later). Today's
  `Core/PineScript/PineTranspiler.cs` is regex rewriting and cannot follow blocks, user functions,
  types, collections, `var`/`varip` or tuples. The order: (1) a conformance bank of 30–50 open-source
  indicators against TradingView's exports; (2) a real parser and bar-by-bar engine; (3) the built-in
  library; (4) `request.security` without lookahead; (5) drawings with spoken forms; (6) a strategy
  emulator. Never possible, and to be said when met: protected scripts, imported libraries without
  source, TradingView-only data, tick-exact live bars. Describe it as "runs Pine Script v5/v6
  indicators" (TradingView's trademark).
- [ ] **Other drawings in other panes**: only text labels and horizontal lines can leave the price pane.
- [ ] **Indicator templates**: an "add to this chart" mode (today they only replace); volume is not
  included.
- [ ] **Profiles**: the buying/selling split is by bar direction; true delta needs lower-timeframe or
  tick data.
- [ ] **Website**: redo "Take the audio tour" to show the different kinds of data (Cody: later). On a
  phone the leaderboard's caption scrolls off with the table. The terminal does not listen (speech
  input), so the site says keyboard or touchscreen in.
- Not picked for now (Cody, 2026-11-18): several charts on one screen with a linked crosshair;
  extended-hours sessions. Declined (2026-11-20): a "Restore from yesterday" button (the daily copies
  exist; going back is a hand copy). Deferred (2026-10-22, again 2026-11-16): Heatmap and Profile
  Ctrl+Left/Right stops.

## Trading not yet tried on a practice or real account

Cody, 2026-11-16: "I'll get to this as I can." Every item below is proven only against fixtures copied
from each venue's public answers or docs. The README and WHATSNEW say so. The full lists of unverified
shapes are in the archive (the 2026-11-21 block at its end).

- [ ] **Kalshi perpetual futures** (2026-11-14): Cody to make a demo account first. Unverified: the
  margin orders, positions, risk, balance and fills shapes; exit-trigger brackets (fire on the mark as an
  immediate limit, so a fast market can leave part unfilled). Daily candles close at midnight New York.
  No websocket (polled). Trailing stops not built.
- [ ] **Coinbase US futures** (2026-11-14): Cody is applying. Unverified: `base_size` in contracts, the
  `cfm/*` shapes. Not built: Coinbase's attached bracket, the liquidation line, funding. MEXC futures
  positions carry no `ContractSize`, so their percent move is off.
- [ ] **Kraken** (2026-11-10, spot BTC, TAO and KAS planned): on the desktop with a live key, check the
  Balances tab (USD under Free, Locked after a resting limit), a 100 KAS order refused ("smallest KAS/USD
  order is 170 KAS"), the KAS leverage hint ("2 or 3"). Unverified: BalanceEx, `.HOLD`, the margin
  close, the account's margin permission and rate tier, Futures leverage preferences. Kraken Futures
  could attach real legs instead of refusing (needs a funded account); it gives no liquidation price.
  **Cody's paper take-profit edit error (2026-11-10)** did not reproduce on 2026-11-13; if it happens
  again, the exact words and the position are needed.
- [ ] **Gemini, Binance, Coinbase, Bitstamp, MEXC, Hyperliquid** (2026-11-10): Cody to try a tiny order
  under a minimum (refused in words before anything is sent), 1x after a higher leverage, leverage above
  a market's maximum (refused, named). **Q:** Gemini's limit_only/post_only markets refuse every order:
  let a limit (or post-only) through? Binance: a different leverage from an open position is refused:
  keep, or change the position's after asking? MEXC futures placement has been "under maintenance" in
  its docs since 2022. Not checked: a market order's minimum value on Coinbase, MEXC spot and Binance
  futures (no price to hand).
- [ ] **Several tabs and venues, on PAPER** (2026-11-05): move a stop with a take profit set (silent,
  target kept); arm a quick trade, switch tabs, Ctrl+Enter (says where it was armed, places nothing);
  BTCUSDT on Kraken and Bitstamp, buy on one then the other (refused). **Q: one paper account per
  venue?** Paper keeps one position per symbol across venues; the recommendation is one per venue, which
  touches the ledger file, the Positions tab and the status bar, so it waits for a yes.
- [ ] **Hyperliquid** (2026-11-04, no order ever sent): Cody to make a testnet agent wallet, add a Paper
  profile, place a small limit, cancel it, place a bracket; then I record real replies for the fixtures.
  **Q (2026-10-25, still open):** as a US resident, live trading breaks Hyperliquid's terms until the
  Payward/Bitnomial route opens: build for your trading, for users, read-only data only, or wait?
- [ ] **Position P&L since you left** has not been read against a live venue; no currency is named.

## Known limits and smaller open items

- [ ] **The order book is fetched on every indicator pass** (`DataOrchestrationService`), a network
  round trip, even with no heatmap on the chart; since 2026-11-22 at most one is in flight. Fetch it
  only when a heatmap is on, or from a stream. **Q for Cody:** should volume bars follow Heikin-Ashi
  colours? Today (and on TradingView) they are coloured by the market's own candle.
- [ ] **The .NET runtime in the WebHost zips is 10.0.11**, older than the Brotli decoder fix in 10.0.14
  (GHSA-p3jp-9v83-76w9, high). No shipped code decompresses untrusted Brotli. Next release: a newer
  10.0.3xx SDK, then check the zips' System.Private.CoreLib.dll version.
- [ ] **Unsigned Windows setup and exe** (SmartScreen warns; signing needs a certificate). An upgrade
  empties `app\` (a Strategies drop-in put there by hand would go; the documented place is under
  `%LocalAppData%`).
- [ ] **Packaging never run**: `packaging/webhost/package.ps1` (no pwsh here), the WebHost launcher's
  message box, the macOS `Accessible Trader.command`.
- [ ] **The VPS's browser harness is on Playwright 1.55's Chromium**; run
  `npx playwright@1.64.1 install chromium` there before its next run.
- [ ] **CI flakes**: c0b44e9e needed three attempts (a WebSocket heartbeat test, then three bUnit waits,
  different each time); `TabKeepsItsMarketTests.A_stock_tab_on_a_provider_that_also_serves_crypto_stays_on_stock`
  failed once locally (2026-11-04). Watch for a repeat; force the one that recurs.
  - **`DataOrchestratorResilienceTests.A_tripped_provider_fails_fast_and_leaves_every_other_provider_alone`
    is timing-bound, and failed once** (a6f6d4bf, xunit attempt 1, 2026-11-21: line 117, expected 10
    calls, got 11; the test took 23 s, not its usual 7). The breaker stays open 5 s; a runner stalled
    past that between tripping it and the `"polygon"` check lets the half-open trial call through, which
    is the breaker working. Fix: give `DataOrchestrator` a `TimeProvider` (or the break length) so the
    test holds the clock still. Attempt 2 was 12,770 of 12,770.
- [ ] **Cipher C** (later, Cody: yes): read the check-box marks as events in the strategy catalog. Limits:
  4-hour daily and yearly readings off; 1-hour and shorter charts unmeasured; weekly charts match the
  original's weekly lows less well than daily (.516 vs .658 F1).
- [ ] **Cipher B**: the waves' overbought/oversold uses ±53 whatever the Triggers say; the pane's floor is
  −103, so Trigger 2 below −53 clips the bar; a Different Timeframe shorter than the chart falls back to
  the chart; 4-hour and daily buckets on stocks are UTC-aligned.
- [ ] **Heikin-Ashi**: custom compiled indicators on HA candles not tried in the app; a background tab's
  candle type is read when its monitor starts; compiled and plugin strategies ignore "Signals from
  Heikin-Ashi candles".
- [ ] **Drawings**: an alert on a drawn level stays at its price if the drawing moves; sloped lines are
  not offered as alert lines; a drawing anchored before the reachable history is drawn wrong.
- [ ] **Smaller limits, mine**: 7h buckets count from 1970 (check TradingView); a 7h chart from Bitstamp's
  1h loads about 142 bars; no key speaks the trade status bar's trades; no percent on the market Close
  position button; "Size in" not remembered; profiles left out of playback; the heatmap's sound unheard;
  the hidden window's bar-close notification has no ladder; with two browser windows the browser-closed
  monitor watches the one that saved last; Bitstamp's new-bar open is provisional for its first seconds;
  Cipher A pivots on sessions crossing midnight UTC and on weekly charts unchecked; the browser-closed
  monitor's memory of a left-out part has no test of its own.
- [ ] **Cody's 2026-10-22 design point**: nothing out of reach (every drawn thing named at least in the
  series summary or Alt+Shift+D).

## From the 2026-09-25 to 2026-10-01 test and mutation passes (status unclear, check)

**Audit tail pass, 2026-11-21** (Cody: "hit that tail of audit items if you can"): the indicator,
accessibility and workspace items checked against the code. Nine fixed with sabotage-proven tests,
one guard added untested, three found already fixed and moved to the archive; three marked "still
present". The rest of this tail (strategy engine, market data, SDK, audio, StrategyLab, the research
questions) was not re-checked in that pass.

The 81st pass (archive 2090) ended with these. Later passes (A2q merged 2026-10-01 with 44 of 44
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
- [ ] Cody to look at text labels in the app: one on the newest bar, two on one bar, a long one. (archive 1459)
- [ ] A label's text in a script no installed font covers (Japanese on this machine) still shows (archive 1461)
- [ ] Not checked: whether a label's colour set in Properties changes anything visible (the plate (archive 1463)
- [ ] The trend line rule. One dated case separates the candidate rules: the Dow's 23 Jul 2026 (archive 1465)
- [ ] The trend line is NOT part of CF as far as can be told: CF is hidden on every frame with a (archive 1480)
- [ ] Cody to open `strategy-lab-data/trendlines/Camel cycles trend lines.json`: a copy of his (archive 1483)

### 2026-10-21 — Cipher C rebuilt as cycle lows after CF's Cycle Trading Indicator; ...
- [ ] Cody to hear it with Orca: a daily Bitcoin and an SPX chart, a weekly chart (no "daily cycle (archive 1496)
- [ ] Merge (both branches), push (the full suite runs there), restart the server: Cody's go-ahead. (archive 1498)
- [ ] CF's future cycle low range (FCLR) boxes to the right of the last bar: the chart has no future (archive 1499)
- [ ] Confluence that upgrades LOW → MID → HIGH over the bars after a low, as CF's demo shows. Ours is (archive 1501)
- [ ] A yearly cycle low has never been seen on CF's screens; ours is unchecked against it. Same for (archive 1504)
- [ ] Still estimated: NDQ, DJI, DXY, SILVER, USOIL, GDXJ, SILJ. Twelve more videos searched (archive 1517)
- [ ] "Days since Last DCL : N" is bars from the low to the last bar (Bitcoin: 46 on 7 Aug 2025 = (archive 1520)
- [ ] More of CF's look, not built: (archive 1527)
- [ ] The cancelled low's green box around the original stack (ours is a red cross on the cancelling bar). (archive 1532)
- [ ] CF puts the check under the low's own stack once confirmed; ours sits above the confirming bar. (archive 1533)
- [ ] CF's LOW / MID / HIGH text on the pending label; ours is spoken only (the tag itself is the original's since 2026-10-27). (archive 1534)
- [ ] Re-test the retired Pulse Reversal Long on `CIPHER_C.DCL Confirmed` before reviving it. (archive 1544)
- [ ] Drawing the window tint as CF's band is: only the past part is drawn. (archive 1545)

### 2026-10-21 — the Camel cycles workspace broke on load: five defects FIXED on branch ...
- [ ] Not pushed, not deployed, and Cody's running server is still on the old build. Merge, push (archive 1552)
- [ ] The GLD 1d tab's "400 (Bad Request)": the reason is unknown because the body was never read. (archive 1554)
- [ ] One Bitstamp "400 BadRequest" for BTCUSDT from the MarketFeedHub live subscription. Every (archive 1556)
- [ ] The session file already saved the DIA tab as Crypto Alpaca AAVE/USD, and dropped the Loukas (archive 1558)
- [ ] The Alpaca live stream: the log showed 409 "insufficient subscription" (the plan is IEX only) (archive 1561)
- [ ] The DIA 1d tab "freezes" and Orca crashed on it (Cody, 2026-10-21). Not explained: DIA's (archive 1568)
- [ ] `LocalBackgroundMonitor.FetchAsync` (WebHost alert poll) asks the provider for the watch's (archive 1593)
- [ ] Untested live on stocks: no stock request with the new `start`/`sort=desc` has been sent with (archive 1601)
- [ ] Re-score the dated calls in `Corpus/rules/*.md` against prices (Camel Finance's 358 first, (archive 1620)
- [ ] Fold the 35 later educator transcripts (Trader Geo's 93-minute tutorial among them) and the (archive 1625)

### 2026-10-18 — setup grader round 3, Market Cipher with Cipher SR location: DONE, No ...
- [ ] Questions for Cody (nothing applied): (archive 1633)
- [ ] Not pushed: the branch is committed locally. The full suite runs on GitHub Actions at the push. (archive 1644)

### 2026-10-18 — the cycle school: stage 2 DONE, results in ...
- [ ] Decide (Cody): fix the old `cftc-cot` downloader to choose contracts by code and stamp (archive 1654)
- [ ] Unverified: the CFTC release dates before 2026 are a holiday rule plus the CFTC's (archive 1658)
- [ ] If the design is ever revised (a new pre-registration): drop the "low due" rule (B5 found it (archive 1661)

### 2026-10-18 — the cycle school: stage 1 DONE (method, design, pre-registration); see ...
- [ ] Stage 2: build and run the 56 pre-registered tests exactly as written, results into a new (archive 1674)
- [ ] Before using COT in the score: confirm which CFTC report each `xs_cftc_*` file came from. The (archive 1676)
- [ ] Questions for Cody (nothing applied): the "90+ metrics" claim for the CF indicator is in (archive 1679)

### 2026-10-18 — macro, Fed, liquidity and sentiment data: FETCHED, point in time; see ...
- [ ] Decide on Cboe put/call after 2019-10-04. It exists only as one HTML page per day, about (archive 1700)
- [ ] Decide on NFCI as-known-then values. ALFRED returned HTTP 504 on the initial-release file. (archive 1702)
- [ ] Optional: a loader that registers the macro CSVs in the cross-series cache, so indicators can (archive 1704)

### 2026-10-18 — Cody's BTCUSDT 1D screenshot: axis tags and empty status-line plots DONE; ...
- [ ] General tag drift rule (recommendation, Cody's call). `StackScaleLabels` has no limit on (archive 1715)
- [ ] Ctrl+Up/Down still stops on a plot with no value on this chart (Cipher SR's Range/VWAP on a (archive 1723)

### 2026-10-18 — setup grader round 2, Market Cipher school: DONE, No edge; see CHANGES ...
- [ ] Question for Cody (nothing applied): in equities, the first bearish divergence exit beat (archive 1734)
- [ ] Not pushed: the branch is committed locally. The full suite runs on GitHub Actions at the push. (archive 1737)

### 2026-10-18 — setup grader for the Cipher B Buy dot: DONE, No edge; see CHANGES and ...
- [ ] The equities side results (non-vetoed dots +0.115R over a random bar in the same context; (archive 1742)
- [ ] If the grading idea is to be tried again, it needs a new dated pre-registration (the old one (archive 1746)

### 2026-10-18 — three lab defects verified and fixed: R after a stop move, HTF ...
- [ ] Re-record every engine R figure on a two-rung ladder with breakeven or trail. R is now one (archive 1752)
- [ ] Re-run the COT battery cells (`battery` v5a-v5h, BTC CFTC_COT) and `cipher-a-battery`'s COT gate (archive 1763)
- [ ] Optional: re-download the COT snapshots with the fixed `cftc-cot` so the files themselves carry (archive 1765)
- [ ] `docs/archive/SETUP_GRADER_PREREG.md` section 2.2 still lists the HTF and R caveats; it is a frozen (archive 1767)

### 2026-10-17 night — Cipher strategy re-run on the new Cipher A/B/SR: DONE; see CHANGES ...
- [ ] Delete `CIPHER_A.Buy Signal` and `CIPHER_SR.Support` from bare-bull-pulse, faber-pulse and (archive 1776)
- [ ] The 1h Cipher SR proximity result (old Cipher B +0.308R p 0.006, new +0.164R p 0.069, KAS and (archive 1782)
- [ ] CatalogueProvenance's rolling-window figures were all computed before the 2026-08-24 (archive 1784)
- [ ] The battery's cells that need retired leaves inside an AND (v23+A, v23+SR, v23+ASR, v23+ALL, (archive 1787)
- [ ] ML confidence model: retrain on the new Cipher B export when lightgbm/scikit-learn are (archive 1790)

### The work A2b creates (status unclear, check)
- [ ] Re-check what else parked behind `ShowModalAsync`. Until 2026-08-28 every line a concrete (archive 10537)

### The work A3 creates (status unclear, check)
- [ ] (F4) Read the timeout in (archive 11123)
- [ ] Teach the harness to seed a chart and credentials, which unlocks the seven unswept dialogs (archive 11126)
- [ ] Sweep controls behind `<details>` expanders and populated lists, not just the initial render (archive 11129)

### Trading dashboard decoupling (roadmap adopted 2026-08-23) (status unclear, check)
- [ ] Spec Appendix B follow-ups (separate from the decoupling): duplicate protective legs (archive 11717)

### Hosted deployment — notes back from the server (2026-08-24) (status unclear, check)
- [ ] `AudioEngine.RingBuffer<T>.Enqueue` is single-producer and now has FOUR producers (note §5b). (archive 11820)
- [ ] Process, not code: tell the server before the SDK band moves (note §4). `global.json` pins (archive 11857)
- [ ] Run the integration suite a second time under a non-empty `PathBase` (note §3). Re-checked (archive 11955)

### Money path, batch two — follow-ups filed 2026-08-24 (status unclear, check)
- [ ] A withdrawal still shares its provider instance with the order path for the length of one (archive 12255)
- [ ] No guard stops the next `WaitForAssertion` over non-render state. bUnit re-evaluates (archive 12262)
- [ ] The shared paper account still holds its creating tab's scoped `IDataService`. (archive 12273)
- [ ] API-key secrets are stored under the nickname alone, so one nickname across two providers (archive 12279)
- [ ] A failed secure-storage read makes every credential silently absent (archive 12286)
- [ ] Follow-up from the money-path batch: the quick-trade sizing seam cannot see the instrument. (archive 12299)

### Health audit — strategy engine and backtest↔live parity (grade D+) (status unclear, check)
- [ ] Two deliberate live-vs-replay divergences left open by the 2026-08-25 parity work, both in (archive 12470)
- [ ] An account selector for Schwab. Carved out of the multi-account finding closed (archive 12591)
- [ ] `StrategyEngine.AddStrategy:255`, `PauseStrategy:280-282` and `SetExecutionMode:287-289` (archive 12690)
- [ ] `StrategyEngine._pendingSignals` is written and never read — Suggestion mode has no confirmation (archive 12697)
- [ ] The announced entry price and R:R are never the ones the backtest fills at. (archive 12704)
- [ ] (amends TODO:2084/2092) Add two more copies to the hand-rolled-ATR census: (archive 12759)

### Health audit — alerts and notification delivery (grade D) (status unclear, check)
- [ ] `AlertOrchestrator` is scoped per circuit in WebHost, so two browser tabs silently delete each (archive 12978)
- [ ] The VAPID private key is written in plaintext with default permissions and the VAPID subject is (archive 12992)
- [ ] Nothing checks bar freshness, so a stale feed reads as a live market — (archive 13005)
- [ ] Two load-bearing comments describe behaviour the code does not have. (archive 13011)
- [ ] (amends TODO:1281) That entry says "Adding or deleting an alert produces no feedback and (archive 13019)

### Health audit — market-data flow (grade C+) (status unclear, check)
- [ ] Opt the providers into the cancellable fetch, one at a time. Carved out of the (archive 13152)
- [ ] `DataOrchestrationService` issues a live order-book network request on every single data update, (archive 13168)
- [ ] The provider-timeframe verification only catches data that is too FINE, never too COARSE (archive 13178)
- [ ] The tab-switch handler assigns the composite market string to the SUB-TYPE field (archive 13188)
- [ ] `MarketFeedHub.TryStartFeedLiveAsync` reports `Started` when it lost the race and threw its (archive 13217)
- [ ] `ChartFeed`'s cap-trimming loops reallocate the entire backing array once per bar (archive 13226)
- [ ] Two `CancellationTokenSource`s are replaced without being disposed, once per tick and once per (archive 13234)
- [ ] `DataService`'s constructor takes an `IPluginLoaderService` it drops on the floor and stores an (archive 13240)

### Health audit — SDK and the 16 market-data / trading provider plugins (grade B-) (status unclear, check)
- [ ] The SDK is not dependency-free, contradicting `docs/CODEBASE_KNOWLEDGE_BASE.md:30`. (archive 13546)

### Health audit — analytics / alternative-data plugins (grade C) (status unclear, check)
- [ ] (amends TODO:5825-5834) `ProviderFetchOhlcvTests.cs` gives the analytics plugins one (archive 14187)

### Health audit — indicators (grade C+) (status unclear, check)
- [ ] A parameter can silently flip a component from causal to look-ahead, and the declaration cannot (archive 14400)
- [ ] `CipherSProvider.cs:193-213` sorts a freshly allocated copy of the whole rolling window on every (archive 14431)
- [ ] `IndicatorService.cs:151-201` and `IndicatorEngine.cs:46-51` call `provider.GetIndicators()` on (archive 14438)
- [ ] `AccessibleTrader.Sdk/Indicators/IndicatorMath.cs` has no unit test. It is the shared library — (archive 14469)
- [ ] The causality guard has two remaining structural blind spots its own doc does not name: (archive 14482)
- [ ] Comments that are actively wrong and load-bearing. `RegimeProvider.cs:106-107` — "seeded with (archive 14491) 2026-11-21: the Regime, Value Deviation and Hurst comments corrected (dba9c00a); `IndicatorCausalityTests`' blind-spot list is still stale.
- [ ] `IndicatorStateMapper.cs:28-30` maps provider output to components by substring in both (archive 14505)
- [ ] (amends TODO:2084) `CipherCProvider.cs:819-831` re-implements Hull MA next to (archive 14525)

### Health audit — accessibility layer (grade B-) (status unclear, check)
- [ ] A chart load failure still cannot say WHY. Filed out of the 2026-08-27 MEDIUM batch, which (archive 14858)
- [ ] Sub-pane navigation names no pane on the first feedback event of a session. Filed out of (archive 14865)
- [ ] Five price/size formatters coexist in the speech layer. `SpeechPriceFormatter.FormatPrice` (archive 14873)
- [ ] (amends TODO:960-968) Those citations have drifted ~17 lines since the 2026-08-23 edits: the (archive 14885)
- [ ] The speech-layer components that still have no tests. Carved out of the (archive 14918)
- [ ] The candle-summary tests assert only the *bullish* prefix (archive 14928)

### Health audit — workspace state, settings, persistence (grade C+) (status unclear, check)
- [ ] `ImportVisualProfile`/`ImportAudioProfile` mutate live state objects without dispatching — (archive 15105)
- [ ] `SettingsManager`'s `JObject` is mutated from multiple threads with no lock — (archive 15122)
- [ ] `WorkspaceConfiguration` has no schema version, and an indicator that no longer exists is restored (archive 15132)
- [ ] `CaptureConfig` mutates the live series during a save — `WorkspaceLibraryService.cs:205-209`. (archive 15144)
- [ ] `AtomicFile` never fsyncs the parent directory and never cleans stale temps — (archive 15170)
- [ ] `AppDbContext` declares an index identical to its primary key, and its fallback path ignores the (archive 15178)
- [ ] `MarketFeeds.GetBarsAsync` drops its `CancellationToken` at the fetch — (archive 15185)
- [ ] Indicator parameters round-trip through the ambient culture and are re-parsed as invariant — (archive 15190)
- [ ] (amends TODO:4495-4497) "`WorkspaceStore` immutable snapshots. Replace direct property mutation (archive 15201)

### Health audit — audio engine and sonification (grade C+) (status unclear, check)
- [ ] A self-terminating voice is deactivated with no release fade, and the note envelope's (archive 15271)
- [ ] The engine's `DataIndex` → `PointReached` plumbing is dead, and per-frame work in the render loop (archive 15310)
- [ ] `WebHostAudioDriver` is `AddScoped` — one `AudioEngine`, one OS thread and one `pw-cat` child (archive 15321)
- [ ] The audio callback runs on the general .NET ThreadPool on Windows, under a lock — (archive 15357)
- [ ] `WebHostBrowserAudioSink`'s class doc is actively wrong about its own lifetime — (archive 15367)
- [ ] Playback tempo is `Task.Delay` with no clock anchoring, so it drifts and quantises — (archive 15373)
- [ ] Two `CancellationTokenSource` leaks in the playback path. `AudioSequencer.Stop:554-559` does (archive 15381)
- [ ] `SonifyProfile` and `SonifyHeatmap` are the only sonifiers that never pan, and `SonifyHeatmap` (archive 15386)
- [ ] `AudioDiagnosticTests.Coordinator_ShouldCoalesce_RapidStateChanges:84-119` asserts nothing and says (archive 15395)
- [ ] (amends TODO:1024) The no-output-limiting item understates the measurement: 20 sine voices at (archive 15401)

### Health audit — chart rendering, drawing tools, coordinate math, theming (grade C+) (status unclear, check)
- [ ] `ExtendLeft`/`ExtendRight` are accepted and ignored — (archive 15529)
- [ ] Audio series volume is used as the heatmap's visual alpha — `HeatmapRenderer.cs:35-36`. (archive 15548)
- [ ] The pane-divider drag uses a hardcoded 600 px container height that is never updated — (archive 15555)
- [ ] Zone-band shading and zone audio are two independent declarations with nothing keeping them in (archive 15562)
- [ ] The TBD "distribution ≥ 0.5" tint has no non-visual counterpart — `ChartRenderer.cs:391-424`. (archive 15573)
- [ ] `RenderZoneBand`'s new-level test uses a signed threshold — `StandardRenderers.cs:1134`. (archive 15581)
- [ ] Crosshair value labels are drawn on a hardcoded dark plate with theme-coloured ink — (archive 15588)
- [ ] The colour-vision-safe override reaches only candles and directional bars — (archive 15598)
- [ ] Candle bodies vanish at wide zoom — `StandardRenderers.cs:195`. (archive 15607)
- [ ] A render failure is logged but never spoken — `ChartRenderer.cs:269-273`, (archive 15620)
- [ ] Dash `SKPathEffect`s are disposed at two of four creation sites — `StylingService.cs:69,76` and (archive 15628)
- [ ] Heikin-Ashi is recomputed over the entire history three times per keystroke — (archive 15636)
- [ ] Two crosshairs are drawn on the main pane every frame — `OverlayLayer.cs:24-40` and (archive 15643)
- [ ] A formation whose trigger is off-screen also loses its floor and its target — (archive 15679)
- [ ] Area fills stretch across the empty right margin — `StandardRenderers.cs:267-270`. (archive 15684)
- [ ] Stale constructor comment — `ThemeService.cs:58`. "fall back to HighContrastDark if not set" (archive 15700)
- [ ] (amends TODO:3665 / 5105 / 5521) TODO:3665 ("shared coordinate math consolidated into (archive 15714)

### Health audit — hosted WebHost, auth, multi-user security (grade B) (status unclear, check)
- [ ] `Register` has no email confirmation and anyone can register any address — (archive 16122)

### Health audit — StrategyLab research methodology (grade B-) (status unclear, check)
- [ ] `ExitCommand.cs:164-165` asserts trades do not overlap; for at least four of the eight exit rules (archive 16273)
- [ ] `ExitCommand`'s stop and target rules exit at the trigger bar's CLOSE, not at the stop or target (archive 16284)
- [ ] `TradingCrossCommand`'s fill-timing comment contradicts its own code by one bar (`:168-172` vs (archive 16292)
- [ ] `TradingCrossCommand.RunEras` leaks the next era into the first era's result (`:472-476`). (archive 16303)
- [ ] `TradingCrossCommand.RunDca:273` computes DCA's CAGR as `(finalValue / totalDeployed)^(1/years) - (archive 16312)
- [ ] Two commands derive a noise seed as `base + hash % 1000`, guaranteeing correlated noise across (archive 16320)
- [ ] `LabStats.PermutationP` silently computes a wrong statistic when `nA + nB > pool.Length`, and it (archive 16331)
- [ ] `XsMomentumRobustness.cs:66` and `:72` carry two undocumented magic constants inside the cost model (archive 16342)
- [ ] `ml/train.py:39-46` splits chronologically but applies no purge or embargo, and the split boundary (archive 16350)
- [ ] `OnChainCommand.cs:248-249` prints a dynamically-counted metric total next to two hardcoded (archive 16359)
- [ ] `GateCommand.cs:52` holds mutable static accumulator state (`_barsSeen`, `_crossBarsOpen`, (archive 16365)
- [ ] `ExitCommand.SignalExitAt:269-273` recomputes the full `ZScore` array on every bar of every trade (archive 16370)
- [ ] `WalkForwardCommand` blemishes (the strongest command in the file set, recorded so they are not (archive 16376)
- [ ] Cost model absent from most commands. No costs in `xsmom` (disclosed at (archive 16381)
- [ ] The forward universe recorder holds two days and there is a gap. `universe-archive/` contains (archive 16389)
- [ ] `RegimePersistenceCommand:187,312,338` hardcodes 252 bars/year for crypto too. Every use is a (archive 16393)
- [ ] The lab has no machine-readable output mode, which is its biggest accessibility gap. (archive 16396)
- [ ] (amends TODO:2028) The permutation-clamp count is wrong: it says "four commands" and there are (archive 16408)
- [ ] Have each command print its own test count and a Bonferroni threshold, the way (archive 16510)
- [ ] Commit content-addressed snapshots for the samples that back a recorded edge, and (archive 16517)
- [ ] The 59 lab files with zero test references, and the absent arithmetic guards. Carved (archive 16525)

### The scrollback smear (reported from use, 2026-08-25) (status unclear, check)
- [ ] The heatmap series has the same prepend bug and is not covered by that fix. (archive 16598)
- [ ] Known full-suite flake: `ModalDisposeLeakTests.Dialog_DisposeReleasesEveryEventBusSubscription` (archive 16785)

### Causality for scripted indicators — closed 2026-08-25 (status unclear, check)
- [ ] Nothing consults `ICustomIndicatorRegistry.IsPublishable` yet, because nothing publishes (archive 16849)
- [ ] The strategy probe hands over an empty `ActiveSeries`, so an indicator-reading strategy is (archive 16890)

### Open (status unclear, check)
- [ ] A script strategy cannot reach the host services a script strategy might want. Filed out (archive 17032)
- [ ] Pine dialect fidelity was not assessed. `Core/PineScript/PineTranspiler.cs` (649 lines) (archive 17132)

### Still open from the patch's own list (status unclear, check)
- [ ] The impossible tab. Nothing validates a symbol/provider pair on workspace restore — (archive 17279)

### Ship-blockers — data pipeline — FIRST TWO FIXED 2026-08-22 (status unclear, check)
- [ ] `OhlcvStore` can write bars that were never real, and being insert-only can never repair (archive 17461)
- [ ] `EventBus` has no exception isolation between subscribers. `Publish` is (archive 17472)
- [ ] `DataState.LiveStreaming` is unreachable; the pipeline reports `Initializing` while (archive 17477)
- [ ] `AnalyticsDataResolver` ignores its `asset` parameter, so requesting ETH returns BTC (archive 17495)
- [ ] Live ticks never recalculate indicators. `DataManager.OnFocusedFeedUpdated` deliberately (archive 17500)
- [ ] Zero-price bars are silently deleted from every fetch. (archive 17506)
- [ ] `"1y"` datasets are unchartable with a misleading error. `MyDataProvider` advertises (archive 17511)
- [ ] Two `MyDataStore` instances over one user directory destroy each other's imports. (archive 17516)

### Ship-blockers — indicator lookahead (the backtest-validity cluster) — ALL SIX FIXED 2026-08-21 (status unclear, check)
- [ ] `ScreenerService.ReadColumns` still reads a component value by descriptor without consulting (archive 17559)
- [ ] Surface `ConditionEvaluator.LastDegradation` in the UI. Some strategies (archive 17569)
- [ ] `IchimokuProvider.cs:264-266` — the Chikou Span array is a raw 26-bar future price, exposed (archive 17623)
- [ ] `CipherAProvider.cs:400-427` — Cipher A's divergences are still stamped at the pivot bar; (archive 17632)
- [ ] `CipherSRProvider.cs:245,299,321` — the historic "Cipher SR backtest lookahead" is still (archive 17641)
- [ ] `SwingStructureProvider.cs:196-201` contradicts its own class doc. The doc at `:37-40` (archive 17647)
- [ ] `SwingStructureAnalyzer.cs:141-159` — pass 2 retroactively deletes swings, so the same bar (archive 17653)
- [ ] The only causality test in the codebase is vacuous. VERIFIED — (archive 17661)

### Ship-blockers — indicator computation (status unclear, check)
- [ ] `IndicatorEngine.cs:127-133` — on a same-bar tick the buffer is handed to the provider (archive 17681)
- [ ] `IndicatorResultBuffer.cs:65,75` zero-fills where the codebase's own stated convention is (archive 17694)
- [ ] `SkenderCalculationCore.cs:193-201` — ten registered Skender indicators resolve to no method (archive 17699)
- [ ] NOT IN THE AUDIT — seven more Skender indicators resolve fine and then write to component (archive 17706)
- [ ] `SkenderCalculationCore.cs:172-178` — every Skender indicator's live bar is recomputed from (archive 17732)
- [ ] `SkenderCalculationCore.cs:173-178` reads uninitialised `ArrayPool` memory. The temp buffer (archive 17738)
- [ ] `SkenderCalculationCore.cs:139-157` — the synthetic `__CROSSOVER`/`__SQUEEZE` flags are (archive 17742)
- [ ] `CoinMetricsProvider.cs:204` — daily metrics are stamped at the START of the day they (archive 17746)
- [ ] `Plugins/Analytics/…Cftc/CftcProvider.cs:197` — COT "release date" is a synthesised (archive 17751)
- [ ] `OpenInterestProvider.cs:203` and `CrowdingIndexProvider.cs:258` — zero-valued OI rows are (archive 17755)

### Indicator correctness — HIGH (status unclear, check)
- [ ] `BarDetailService.cs:227-282` — both indicator narrative facts are dead code. (archive 17763)
- [ ] `IndicatorContextAnalyzer.cs:85-91` — the Money Flow Wave thresholds are calibrated to a (archive 17769)
- [ ] `IndicatorContextAnalyzer.cs:197-213` — `DetermineZone` never compares price to a band. (archive 17773)
- [ ] `CipherSProvider.cs:96-109` speaks unhedged trading instructions — *"strong DCA (archive 17777)
- [ ] `CipherSRProvider.cs:448-455` — pivot speech scans FORWARD to report a touch count from the (archive 17786)
- [ ] `AnchoredVwapProvider.cs:142-163` anchors to the confirmation bar, not the pivot, silently (archive 17793)
- [ ] Skender component names that resolve to nothing or to duplicates. `Vip`/`Vim` on Vortex (archive 17796)
- [ ] `IndicatorMath.cs:141-151` — `ComputeStochRsi` fabricates 50.0 across the entire RSI (archive 17808)
- [ ] `FundingRateProvider.cs:60` (+ OpenInterest, Crowding — verbatim triplicate) mangles (archive 17812)
- [ ] `CrossSeriesCache.cs:97,153` — no TTL on success, no negative caching on failure. A (archive 17817)
- [ ] `CoinMetricsProvider.cs:279` attributes ETH's on-chain metrics to a BTC chart whenever the (archive 17822)
- [ ] `CotPositioningProvider.cs:281` starts emitting Crowded markers off a 5-sample population (archive 17825)
- [ ] Chart-pattern geometry defects. `ChartPatternDetector.cs:388-391` — the H&S neckline (archive 17833)
- [ ] `ChartPatternNarrator.cs:189-190` — the exact wording defect the class doc at `:199-207` (archive 17841)

### Ship-blockers — audio and speech (status unclear, check)
- [ ] Component metadata has no way to say "this value is a price." The fix above changed the (archive 17876)
- [ ] Saved workspaces persist `SpeechTemplate`, so existing users keep the old `{value:F2}` (archive 17883)
- [ ] Zone-proximity speech overwrites the bar reading on the web head. VERIFIED as a second (archive 17930)
- [ ] Component-context speech reads RAW OHLC under Heikin-Ashi. `NavigationFeedbackManager` (archive 17955)
- [ ] Muting is not absolute for Cloud components. `NavigationSonifier:352` and (archive 17963)
- [ ] `LevelCrossingMonitor` ignores series mute and every volume tier. `:113` filters on (archive 17971)
- [ ] The earcon round-robin overwrites the "dedicated" cross-chirp and level-cue slots. (archive 17988)
- [ ] No output limiting anywhere. `AudioEngine:621-624` writes `leftSum * _masterGain` (archive 17994)
- [ ] The volume bed and the candle body play the same two pitches. (archive 18001)
- [ ] `AutoNarrationService:179` re-introduces the F2 bypass the router redesign removed. A (archive 18010)
- [ ] Hitting the first or last series is a silent no-op. `NavigationEngine:178-190` clamps and (archive 18024)
- [ ] `SoundPatchLibrary` mutates a plain `List<SoundPatch>` that the navigation path enumerates. (archive 18028)

### Ship-blockers — UI and accessibility (status unclear, check)
- [ ] Every feedback message is spoken twice, and F2 silences only one of the speakers. (archive 18180)
- [ ] The assertive double buffer fails on even-numbered batches. `MainLayout.razor:186-194` (archive 18186)
- [ ] `role="menu"` popups are counted as open modals but are not covered by the Tab trap. (archive 18193)
- [ ] Keyboard rebinding cannot capture any modifier chord. `captureNextKey` registers on (archive 18199)
- [ ] The inline stop-loss / take-profit editor opens with `autofocus`, which browsers ignore for (archive 18205)
- [ ] `LoadWorkspaceModal` arrow keys change the highlight silently. `:96-120` implements a (archive 18211)
- [ ] `OrderBookModal` rows are `@key`ed on price, so a live depth update destroys and recreates (archive 18216)
- [ ] The chart's failure state is visual-only. `ChartArea.razor:128-149` puts "Connection (archive 18228)
- [ ] ~15 modals render status text into a `role="status"` region created in the same render as (archive 18233)
- [ ] Silent early returns. `TradingDashboardModal:788` returns before `_isVisible = true` when (archive 18258)
- [ ] MAUI: `MainPage.xaml.cs:61-129` subscribes on every `OnHandlerChanged` and unsubscribes only (archive 18288)
- [ ] MAUI: the Android hardware-keyboard bridge is dead for navigation keys and double-dispatches (archive 18294)
- [ ] MAUI: `TrayIconService.cs` is the highest compile risk in the tree. Behind `#if TRAY_ICON`, (archive 18307)
- [ ] MAUI: `BlazorAudioDriver.cs:205-213` (Android) exits its PCM push loop on (archive 18311)
- [ ] Two copies of `app.css` (763 and 782 lines) differing by exactly two hunks — and the (archive 18314)
- [ ] `.sr-only` is defined only inside `TradingDashboardModal`'s `<style>` block but used by (archive 18317)
- [ ] Three independent modal-open counters (`keyboard.js`, `CommandDispatcher`, (archive 18320)
- [ ] `role="dialog"` markup is copy-pasted 25 times. `ModalBase` captures the *event* contract (archive 18328)

### Ship-blockers — trading providers and the SDK order contract (status unclear, check)
- [ ] Tradier equity sides ignore position effect: `sell` vs `sell_short`, `buy` vs (archive 18578)
- [ ] `ReconnectingWebSocket` — three defects in the SDK's shared socket. `SendAsync` is (archive 18687)
- [ ] `KrakenFuturesProvider.cs:367` silently turns an IOC request into a GTC order — it sets (archive 18693)
- [ ] `InteractiveBrokersProvider.cs:649-668` auto-confirms every IBKR risk warning — up to 8 in (archive 18699)
- [ ] `PolygonProvider.cs:126-131` hardcodes the 15-minute-delayed WebSocket while (archive 18702)
- [ ] `FinnhubProvider.cs:155` processes only the last trade in each batched frame (archive 18706)
- [ ] Capability flags declared with nothing behind them. IB declares `L2` and does not implement (archive 18709)
- [ ] `BaseMarketDataProvider.GetCapability<T>` returns null for everything except market data, (archive 18717)
- [ ] No `CancellationToken` on `IMarketDataProvider` or `ITradingProvider`, while (archive 18738)
- [ ] `BarBucketConsolidator.cs:52` drops every negative-valued tick (`tick.Open > 0 && …`), so (archive 18742)
- [ ] `ProviderResult<T>` has zero callers repo-wide, and `ProviderErrors`/`SurfaceError` has (archive 18841)

### Duplication swept 2026-08-22, and what the sweep found (status unclear, check)
- [ ] 36 copies of the indicator parameter accessors, in 15 distinct implementations, and they (archive 19012)
- [ ] `ValueDeviationProvider.Ema` is a different algorithm from every other EMA in the tree. (archive 19030)
- [ ] Three near-identical JSON-backed list stores. `JsonScreenerLibrary`, (archive 19044)

### Correctness / duplication debt found by this audit (status unclear, check)
- [ ] `VolumeProfileLevelProvider:61` silently falls back to a known future-leaking profile. (archive 19067)
- [ ] Three candle classifiers — and the 2026-08-20 entry understates it on two counts. That (archive 19075)
- [ ] ATR is hand-rolled five times with different warmups. `ChartPatternDetector.cs:675-699` (archive 19104)
- [ ] Signal polarity is classified twice and they disagree. `NavigationSonifier.IsPositiveSignal` (archive 19130)
- [ ] Order-failure classification exists three times in three non-equivalent forms. (archive 19150)
- [ ] "Order placed" is spoken unconditionally, including for orders the broker refused. (archive 19159)
- [ ] A forced liquidation is announced as an ordinary fill. `PaperTradingProvider` builds (archive 19163)
- [ ] Paper order updates are published synchronously while the account lock is held. `Emit` → (archive 19168)
- [ ] Stops and take-profits fill at the trigger even when the bar gapped through it. (archive 19182)
- [ ] Realized P&L excludes fees, so the spoken result of a trade is wrong by the round-trip (archive 19187)
- [ ] `GetBalancesAsync` nets a short against a long in the same base asset, so a BTC/USDT long (archive 19189)

### Documentation drift (status unclear, check)
- [ ] README is two to three releases stale in its most prominent sections. Line 14 still says (archive 19283)
- [ ] `ServiceCollectionExtensions.cs:487-490` (WebHost) describes a silent unsandboxed (archive 19307)

### Untested code that decides money, security, or research truth (status unclear, check)
- [ ] The block bootstrap is non-circular, so the sample edges are under-drawn. Found while (archive 19668)
- [ ] Zero tests for (MOSTLY CLOSED 2026-08-22 — five clusters tested, two remain): (archive 19766)

### Flake risk (status unclear, check)
- [ ] OBSERVED 2026-08-25 — `LinuxBwrapSandboxTests.A_script_cannot_read_the_hosts_environment` (archive 19905)
- [ ] No time abstraction anywhere. Every timeout, throttle, debounce, poll interval and (archive 19958)

### Tests that should exist and do not (status unclear, check)
- [ ] Orphaned legs after a manual close: close the position, drive price to the stop, assert no (archive 20031)
- [ ] The focused pump must not deliver a tick for a different identity. Set focus to B, push an (archive 20033)
- [ ] Replace `DataOrchestratorResilienceTests`' hand-copied `Transition` switch with a test (archive 20062)

### Open after the 2026-08-20 hosted-tier and audio batch (status unclear, check)
- [ ] `trader_local.db` is not under `Accounts__DataRoot`. It resolves to (archive 20243)
- [ ] `OhlcvStore` has no schema migration path. `EnsureCreated` will not alter an existing (archive 20248)
- [ ] Server-side state migration for existing hosted installs. Workspaces, `alerts.json` and (archive 20251)
- [ ] The analytics cache key has no user or credential dimension. Correct today — hosted keys (archive 20263)
- [ ] Three candle classifiers. `BarDetailService.ClassifyBar`, (archive 20266)
- [ ] Upper and lower wicks share a waveform. They are now correctly distinguished by pitch (archive 20271)
- [ ] `AccessibleTrader.BlazorClient` cannot be built on this machine (`NETSDK1147: (archive 20282)

### Terminal / lab split (2026-08-01) (status unclear, check)
- [ ] Bundle export from the terminal. Import exists; the app can only write the old (archive 20306)
- [ ] Per-spec confirmation before starting an `ExecutionMode.Auto` import. The count (archive 20309)

### Platform tiers + signal service — designed, not built (2026-08-02) (status unclear, check)
- [ ] `CoinMarketCapProvider` — now optional. Only worth it for fields CoinGecko lacks (archive 20346)
- [ ] `SecEdgarProvider`, `GdeltProvider` — larger, gated on the revision-breadth result. (archive 20348)
- [ ] Signal service LAST, when there is more than one control-tested edge to serve. (archive 20349)

### Company / macro data layer — designed, not built (2026-08-02) (status unclear, check)
- [ ] Start the GDELT recorder. (archive 20374)
- [ ] First test if the layer is greenlit: analyst estimate revision breadth, through the (archive 20375)

### Post-2.0 polish batch (2026-07-26) (status unclear, check)
- [ ] CoinGlass + extra trading brokers (Tastytrade / Bybit / Kraken Futures / (archive 20421)

### Provider system — quality pass (2026-07-24 → 07-25) (status unclear, check)
- [ ] Coinbase live candle volume. Live candles are synthesized from the (archive 20443)
- [ ] MEXC live-price responsiveness on thin markets. The live title price is (archive 20448)
- [ ] Migrate the other crypto providers onto the shared REST base. MEXC uses (archive 20456)

### Follow-ups (status unclear, check)
- [ ] Keyed-feed DataManager refactor if tick-level background evaluation is ever (archive 20547)
- [ ] Auth: HaveIBeenPwned password validator — k-anonymity range API, (archive 20591)
- [ ] Ops: systemd `UMask=0077` drop-in on the hosted unit so future files (archive 20600)
- [ ] FINRA Query API short-interest metric (needs free dev registration — Cody). (archive 20612)
- [ ] Tiered RiskPercent by setup quality (2-tier, evidence-based) in RiskPlan. (archive 20613)
- [ ] Sector risk governor as an optional *enforcing* mode (default stays warn-only). (archive 20614)
- [ ] Pre-registered lab test: wider ladder (3R/6R or trail) for the gated v23 variants. (archive 20615)
- [ ] Schwab streamer ACCT_ACTIVITY (~2-3d, riskier) — requires the full Schwab (archive 20637)
- [ ] MAUI native window title: add the live price (WebHost browser-tab title has it, (archive 20642)

### [2026-07-22] — Local background monitoring core (SHIPPED) (status unclear, check)
- [ ] Factory sound bank — Cody's event WAVs as bundled defaults per earcon key (archive 20659)

### [2026-07-22] — My Data: CSV import (Wave 2 CSV item SHIPPED) (status unclear, check)
- [ ] Windows tray VERIFY — MAUI head close-to-tray (H.NotifyIcon.Maui 2.3.0, (archive 20700)
- [ ] Hosted server-side alerts + Web Push — evaluate saved alerts server-side (archive 20714)
- [ ] xlsx import — future; needs a spreadsheet-parsing dependency. The (archive 20718)

### Mute-tier redesign (the F-key row) (~2-3d total, ship as one coherent change) (status unclear, check)
- [ ] Shift+F4 dedicated braille display picker modal — PARTLY CLOSED 2026-09-06. (archive 20743)

### Follow-ups raised by this pass (status unclear, check)
- [ ] Mexc full contract enrollment after per-plugin dependency folders land. (archive 20791)

### Still open (the honest remainder) (status unclear, check)
- [ ] Native touch layer — iOS adjustable `UIAccessibilityElement` + rotor (archive 20828)
- [ ] Play range — sequencer needs an end-index concept; shift+click summary (archive 20835)
- [ ] Price-axis drag to scale (needs a manual y-range override in state); (archive 20837)
- [ ] "Recent events" visual journal ticker (visual earcons cover the alerting half). (archive 20839)

### Phase D remaining (deferred, tracked) (status unclear, check)
- [ ] "Recent events" visual ticker surfacing the Journal ambiently (deaf/HoH). (archive 20878)

### Phase C second pass (native, deferred — needs macOS + physical devices) (status unclear, check)
- [ ] iOS: `UIAccessibilityElement` with adjustable trait over the canvas (archive 20908)
- [ ] Android: `ExploreByTouchHelper` virtual nodes (adjustable first; per-pane (archive 20911)
- [ ] On-device VoiceOver + TalkBack verification of the shipped web layer inside the (archive 20913)
- [ ] JS test infra (Vitest) so the gesture state machine gets direct tests (Tier-4). (archive 20915)

### Phase B second pass (deferred, tracked) (status unclear, check)
- [ ] Price-axis drag to scale; time-axis drag to zoom; double-click axis to reset. (archive 20956)

### Deliberately deferred (tracked, not forgotten) (status unclear, check)
- [ ] Plugin trust manifest cryptographic signing (hash-based TOFU fine until (archive 20999)
- [ ] Per-user (post-auth) rate limiting. (archive 21001)
- [ ] CAPTCHA-alternative / accessible fallback flow on 429 for shared-IP users. (archive 21002)

### Open (status unclear, check)
- [ ] Multi-layer patch preview vs component parity — verify by ear that the (archive 21117)
- [ ] `AudioSequencer.BuildVoicePlan` slot packing / budget-overflow / (archive 21129)

### Medium-value tests still pending (status unclear, check)
- [ ] `WebHostProgramStartupSmokeTests` (1 case) — boots host via (archive 21270)
- [ ] `DiagJournalEndpointTests` (2 cases) — empty / newest-first. (archive 21274)

### WebHost phases — L5 / L6 / L7 / L8 / L9 (L6 docs is the only one still open) (status unclear, check)
- [ ] L8 — Desktop shell. SKIPPED per user choice (2026-05-16). (archive 21345)
- [ ] Shared connection pool keyed by symbol (per-visitor upstream connections are (archive 21362)
- [ ] Follow-ups: transactional email (confirmation + password reset); tier (archive 21372)

### Cosmetic + known issues (status unclear, check)
- [ ] Chart pixel density. Server renders at fixed 1280×720; CSS (archive 21378)
- [ ] Drawing-tool mouse interactions in browser. With the chart (archive 21396)

### Empirical verification (status unclear, check)
- [ ] PgDn/PgUp cycle skips price-line and produces sensible top/bottom (archive 21516)
- [ ] F4 pause resumes cleanly after workspace identity change (auto-reset). (archive 21518)
- [ ] Pan keys empirically belong to graphic vs strip on the actual device. (archive 21519)
- [ ] Strip X-value timeout (~1.5 s) feels right under live arrow nav. (archive 21522)
- [ ] Body+wick+gap structure is tactile-distinguishable at 4 dot rows (archive 21523)
- [ ] Dot Pad X on-device verification. The Dot Pad X is supported (same (archive 21526)

### Out of scope (deferred to future iterations) (status unclear, check)
- [ ] 3-pane mode. F-key chord toggle (e.g. F12) cycling 1 / 2 / 3 (archive 21536)

### Empirical verification (after each ship) (status unclear, check)
- [ ] Splash actually centers in 60×40 with the chosen font table. (archive 21552)
- [ ] PgDn/PgUp cycle skips price-line and produces sensible top/bottom (archive 21553)
- [ ] F4 pause resumes cleanly after workspace identity change (auto-reset). (archive 21555)
- [ ] Pan keys empirically belong to graphic vs strip on the actual device. (archive 21556)
- [ ] Strip X-value timeout (~1.5 s) feels right under live arrow nav. (archive 21557)

### Important security (status unclear, check)
- [ ] Sign ScriptWorker.exe + plugin DLLs. Use a code-signing cert; verify (archive 21728)
- [ ] Coinbase + remaining-providers credential-checkout migration. (archive 21731)
- [ ] CPU quota uses sliding window. `OutOfProcessScriptHost.cs:262-301` (archive 21735)
- [ ] Sync-over-async cleanup (3 sites). `StrategyAutoLoader.cs:73-74` (archive 21738)

### Build-system safeguards (status unclear, check)
- [ ] RCL platform-code Roslyn analyzer. Error on (archive 21745)
- [ ] DI lifetime validator. Add a startup pass that walks the registered (archive 21749)

### Documentation (status unclear, check)
- [ ] Customer-facing README rewrite. Current README is 90% architecture. (archive 21756)
- [ ] USER_GUIDE coverage gaps. Add sections for: strategy building, (archive 21759)
- [ ] Sample plugin DLL. Ship `Plugins/Samples/SimpleRsiProvider.cs` (or (archive 21762)
- [ ] `Tests/README.md` — fixture conventions, `BlazorTestHarness` recipe, (archive 21765)

### Architectural follow-ups (post-1.0; large) (status unclear, check)
- [ ] God-modal split. `PropertiesModal.razor` (826 lines), (archive 21770)
- [ ] `WorkspaceStore` immutable snapshots. Replace direct property (archive 21775)
- [ ] Plugin manifest v2. Version, signature chain, capability declaration (archive 21778)
- [ ] Modal close-on-Esc + open-on-app-key as single dispatcher cases. (archive 21781)
- [ ] Per-strategy timeout override. `OutOfProcessScriptHost` (archive 21783)

### Genuinely-future research (unblocked, not backlog) (status unclear, check)
- [ ] AssetClassifier on equity snapshots. Pull TSLA/AMZN/SPY via the Alpaca (archive 21819)
- [ ] `v23r-SRRES` fires ZERO times in nine years and is a broken cell, not a null. (archive 21853)
- [ ] v23or weekly cross-asset. OR-gate may broaden coverage on smaller-cap (archive 21869)

### Open follow-ups for v22 (revised after walk-windows analysis) (status unclear, check)
- [ ] Default `walk-windows` over `walk` for strategy validation. (archive 22151)
- [ ] ConfirmThreshold sweep — try 0.5, 0.6, 0.7, 0.8 on v22-LONG (archive 22158)

### Open follow-ups for the timeframe work (status unclear, check)
- [ ] Cross-asset 1d rolling-window for v22-LONG. Walk-windows says (archive 22210)
- [ ] Weekly confirmation across other markets. With adaptation, (archive 22215)
- [ ] Score-component adaptation review. The current weekly (archive 22219)

### Remaining follow-ups (optional, no security impact) (status unclear, check)
- [ ] On-device / on-AppContainer integration tests — a test (archive 23044)

### Next priorities (broader codebase audit — 2026-04-17) (status unclear, check)
- [ ] Toolbar button labels showing raw shortcut keys (surfaced (archive 23125)
- [ ] Phase 5 v2: order book large-order detection. Adaptive (archive 23133)

### Open gaps — next session (status unclear, check)
- [ ] Delete redundant BNVISION_FUNDING / BNVISION_OI lab providers — now duplicating what Core FUNDING_RATE / OPEN_INTEREST provide. Stil... (archive 23204)

### Strategy work — future (status unclear, check)
- [ ] Cross-asset matrix rerun with v18 asset-aware: once Core providers accept `__symbol`, re-run v18 on ETH/SOL/XRP/DOGE in LIVE mode to... (archive 23211)
- [ ] v13/v14/v15 cross-asset walk-forward — never tested on non-BTC. May reveal additional survivors or confirm BTC-specialist pattern. (archive 23212)

### Research / Next Session (status unclear, check)
- [ ] Adaptive WT thresholds for Cipher B — dynamic OB/OS levels based on oscillator's own distribution (percentile-based) (archive 23260)
- [ ] Pulse indicator simplification — consider decomposing v1/v2/v3 signal tiers into strategy conditions (archive 23261)
- [ ] Phase 12 Session 3 — v9 backtest + thesis validation (archive 23262)

### Phase 11 Audit Fixes (2026-04-07) — DONE (status unclear, check)
- [ ] Custom Script tab Roslyn strategy persistence — Roslyn-compiled strategies still aren't saved as `StrategySpec`s. (archive 23394)

### Session 3 — v9 backtest + thesis verdict (planned, next session) (status unclear, check)
- [ ] Test the refactor + CrowdingIndex end-to-end — confirm shared cache means no duplicate fetches when multiple cross-series indicators... (archive 23427)
- [ ] Run v9 backtest on BTC/USDT 1h Bitstamp, recent 30-90 day range (OKX history depth) (archive 23428)
- [ ] Verdict on the strategy thesis — does v9 produce materially different walk-forward metrics than v7/v8? (archive 23429)

### Session 4 — Glassnode + multi-asset (deferred) (status unclear, check)
- [ ] `GlassnodeProvider` plugin — when API key is purchased. Deep history (back to 2019) for funding/OI/sentiment. Same source-name swap... (archive 23433)
- [ ] Multi-asset support — once Glassnode is in or v9 thesis validated, expose `Source` and `Symbol` as string parameters on each cross-s... (archive 23434)

### Session 5 — Glassnode plugin (deferred — paid) (status unclear, check)
- [ ] `GlassnodeProvider` — when API key is purchased. Same pattern as OkxDerivatives. Unlocks deep history for funding/OI/on-chain that t... (archive 23438)

### CommandDispatcher — Chart-Focus Gate + Structural Clarity (status unclear, check)
- [ ] Full Command Pattern (extract `ICommandHandler<T>` per domain) — deferred to Phase 5+ if dispatcher grows beyond current size. (archive 23543)

### SkenderIndicatorProvider — GetDetailFact Extraction (status unclear, check)
- [ ] Split `SkenderIndicatorProvider` into `SkenderIndicatorDiscovery` + `SkenderResultMapper` — deferred to Phase 5+ when a second Skend... (archive 23556)

### Feature Completion (status unclear, check)
- [ ] Multi-Symbol Watchlist: Extend `WorkspaceState` to hold collection of `ChartState`. (archive 23812)

### Performance (from previous Phase 6) (status unclear, check)
- [ ] Span-Based Indicator Pipeline: `ReadOnlySpan<Ohlcv>` + `ArrayPool<double>` in `SkenderIndicatorFactory`. (archive 23824)
- [ ] Full Channels Migration: `Channel<Ohlcv>` from plugin → `DataManager` for live ticks. (archive 23825)

### Phase 10-D — Custom Indicator Platform (Roslyn) ✅ (status unclear, check)
- [ ] `ICustomScriptService.RunScriptAsync` full pipeline: Compiled `ICustomIndicator.Calculate` routed through `IndicatorOrchestrator` →... (archive 23985)

### v7 strategy build (AFTER infrastructure is in place) (status unclear, check)
- [ ] Build v7 — Score-based multi-source confluence (archive 24221)

### Bigger systemic improvements (future, lower priority) (status unclear, check)
- [ ] Walk-forward parameter optimization (expanding window) — re-tune Cipher periods every N months using only data prior to that point.... (archive 24232)
- [ ] Regime classifier with regime-conditional strategy routing — classify each bar as trending/ranging/volatile via ADX + ATR percentile... (archive 24233)
- [ ] Indicator-on-HTF computation (deferred from Session B) — sync `IIndicatorRunner` so HTF leaves can reference indicators not just pri... (archive 24234)
- [ ] Expand SignalCatalog companion-array support — generic mechanism for indicators to expose multiple value streams per component (not... (archive 24235)

## Where the rest went

Everything else is in `docs/archive/TODO_HISTORY.md`: the 2026-08 audits (A1, A2, A3, B1, B2), the 2026-09
START HERE passes, the health-assessment write-ups, the phase plans from March and April 2026, and the old
release sections. Open `docs/CHANGES.md` for what shipped.

