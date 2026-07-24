# What's New in 1.9.0

A short, user-facing summary of what changed since **1.8.0**. For the full engineering
changelog see [`CHANGES.md`](CHANGES.md).

> Version note: 1.9.0 is a **minor** release — nothing breaks. Saved workspaces,
> strategies, shortcuts, patches, and API keys from 1.8.0 load unchanged.

---

## Your terminal keeps watching when the browser is closed

On your own machine the terminal is a server that outlives the browser, and it
now has a **system-tray icon** to prove it — the control surface for that
running process, usable with no browser open. Its name carries the live count of
unread alerts, and its menu (which your screen reader reads like any menu) lets
you reopen the terminal in your browser, hear and review recent alerts (with
per-alert *Mark as read* and *Dismiss* on a simple page), silence alerts for 30
minutes, hear a quick status, copy the terminal's address, toggle background
monitoring, or exit cleanly. Turn on **Settings → General → "Keep monitoring
when the browser is closed"** and saved alerts keep firing — spoken through Orca,
with a sound and a desktop notification — whether the browser is open or shut.
(Local machines only; the hosted site doesn't show a tray. On Linux the menu is
fully screen-reader navigable; Windows works the same way; on macOS the desktop
app is the place for a native tray.)

## Safer, better-wired, more of it spoken (2.0-readiness pass)

A deep audit tightened a batch of loose ends — the ones you'd actually notice:

- **Scroll-wheel zoom works.** It had quietly stopped doing anything; the wheel
  now zooms the chart around the cursor again.
- **Liquidation warnings.** If a leveraged position drifts close to its
  liquidation price, you now hear a spoken warning (with an alert earcon)
  instead of finding out the hard way.
- **A real-money order can't slip out thinking it's paper.** If you flip paper
  mode off in Settings with the Trading Dashboard open, the next order still
  gets its live confirmation — no unconfirmed live send.
- **Failed alert webhooks tell you.** If a Discord/Slack/custom webhook is
  broken or renamed, you now hear that the alert didn't deliver, once per
  endpoint, instead of it failing silently.
- **Your custom earcons are honored.** Patches you assign in the Sound Designer
  for errors, success, retry, and connect/disconnect now actually play.
- **Dialogs announce in browser-voice mode.** If you use the browser's own
  voice instead of a screen reader, opening and closing dialogs now speaks.
- **Your chart zoom comes back.** Reopening or resuming a workspace restores the
  zoom level you were using.
- **A corrupted workspace or alerts file no longer vanishes silently** — it's
  set aside (recoverable) and you're told, rather than starting blank.

## Your live fills now speak

- An audit found that order announcements — "Order filled…", "Stop loss hit…" —
  only ever worked in paper mode. **Live-broker fills, stops, and take-profits
  now announce the moment the exchange reports them**, on every broker that
  streams order events (Binance, Alpaca, Coinbase, Kraken, Bitstamp, MEXC,
  OANDA, Interactive Brokers). The fix also means fills keep announcing on
  every connected broker at once, and real-money orders still resting on an
  exchange keep speaking even while you practice in paper mode.
- **Schwab and Tradier announce now too.** Tradier gains a real account-event
  stream (instant announcements), and both brokers get a polling fallback: the
  terminal watches every order it places and announces the outcome within
  seconds even when the broker can't push events. The manual's Trading chapter
  has the fine print.

## Sub-dollar assets speak real prices

- KAS at $0.0363 used to read "0.03" everywhere on the price line. Price speech
  is now **magnitude-aware** (about three significant digits, however small the
  asset), and the fix heals **existing saved workspaces**, not just new ones.
  The Ctrl+Shift+D raw detail readout gets the same treatment.

## The live price lives in the title bar

- The browser tab now reads like a ticker:
  **"▲ 0.0363 KAS/USDT 1d on MEXC - Accessible Trade Terminal"** — load-state
  triangle, live price (updated about once a second), symbol, timeframe,
  exchange. Glance at the tab — or let your screen reader read the window
  title — and you have the price.

## The research lab, in the app

- The Strategy Manager (Alt+S) gains a **Lab** tab: **walk-forward windows**
  (slice history into 2–12 windows, backtest each, hear "Profitable in 3 of 4
  windows") and **Compare all strategies** — every saved strategy tested on the
  first and second half of your data, with the research harness's exact
  SURVIVOR gate and ranking by the *weaker* half. "No survivors" is a result,
  not a failure.

## Alerts grow the full rule tree

- The Alerts dialog (Alt+J) gains an **Advanced condition** toggle: the same
  AND/OR/NOT/Score tree builder the strategy composer uses, so an alert can be
  "RSI oversold AND price above the daily 200 SMA" instead of one threshold.
  Advanced alerts fire once per condition-edge, with optional repeat and
  cooldown.

## A new built-in strategy: Cycle Low Reversal [v24]

- The strategy the Loukas Cycles indicator was built for: enter in the first
  days of a new daily cycle, when a **confirmed daily cycle low** meets a
  Cipher B reversal trigger. Lab walk-forward on BTC daily 2011–2026: positive
  in **both** halves (+0.25R / +0.31R, 35 trades each, profit factor ~2.1),
  5 of 6 windows positive. Its description carries the full record — including
  where it's weak (the most recent window) and where it failed (LTC, thin ETH,
  not enough SOL history). BTC daily charts, suggestion-only, as always.

## Under the hood, for people who read changelogs

- Condition trees round-trip through alerts.json via a System.Text.Json ↔
  Newtonsoft bridge; BootstrapCi moved into Core so the app and the research
  harness share ONE definition of "survivor"; two latent order-stream bugs
  (paper double-subscribe, single-slot provider drop) fixed before they could
  ever fire.
- Test coverage grew from 1,701 to **1,755** automated tests, run in Debug and
  Release on every push.

---

_Questions or issues: see [`USER_MANUAL.md`](USER_MANUAL.md) and
[`QUICKSTART.md`](QUICKSTART.md)._
