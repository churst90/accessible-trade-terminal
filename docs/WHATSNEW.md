# What's New in 2.0.0

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
library versions. The full test suite stands at **2,072 tests, all green**.

---

*Known limitations tracked for a follow-up: Coinbase live candles don't yet report
volume (historical bars do), and the Schwab real-time account stream awaits Schwab
developer-app approval. Neither affects charting or order placement.*
