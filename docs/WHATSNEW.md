# What's New in 1.9.0

A short, user-facing summary of what changed since **1.8.0**. For the full engineering
changelog see [`CHANGES.md`](CHANGES.md).

> Version note: 1.9.0 is a **minor** release — nothing breaks. Saved workspaces,
> strategies, shortcuts, patches, and API keys from 1.8.0 load unchanged.

---

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
