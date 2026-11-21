# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

Everything since 2.13.1 (2026-11-16), one entry per piece of work, newest first, with what is
still unproven. The full entries as first written (every class, test and sabotage) are in
[`archive/CHANGES_DETAIL_SINCE_2.13.1.md`](archive/CHANGES_DETAIL_SINCE_2.13.1.md), condensed here
on 2026-11-21. **On the VPS:** everything up to c3eed63d (deployed 2026-11-21 10:45 UTC); the
dialog fixes below are not. **Nothing here has been tried with money.**

### 2026-11-21: every dialog laid out again; Renko opens full; no "Data link" line; no emoji — not on the VPS, not seen by Cody

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
