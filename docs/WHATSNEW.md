# What's New in 1.6.0

A short, user-facing summary of what changed since **1.5.0**. For the full engineering
changelog see [`CHANGES.md`](CHANGES.md).

> Version note: 1.6.0 is a **minor** release — nothing breaks. Saved workspaces,
> strategies, shortcuts, and API keys from 1.5.0 load unchanged. Some strategies
> have new, clearer display names; their identities (and your copies) are untouched.

---

## See what the big money holds — for free

- **COT Positioning** (new indicator): weekly hedge-fund positioning for gold,
  silver, copper, oil, gas, Bitcoin, Ether, the S&P, the Nasdaq, the euro, and the
  dollar index, straight from the CFTC — no key, no subscription. It speaks a
  z-score with "crowded long" / "crowded short" bells, and its help text tells you
  honestly where the signal works (gold, indices) and where it doesn't (crypto, FX).
- **Daily short volume** for any US stock (new FINRA data source, also free):
  chart `AAPL_SHORTVOL` and hear how much of the day's tape was short sales.

## Strategies you can actually pick from a list

- Every built-in strategy now has a **plain-English name** ("Dip Buy in Uptrend",
  "Capitulation Bottom", "Trend Baseline") with the research version tag at the end.
- New **Trend Baseline** benchmark strategy — the boring institutional standard that
  every fancier strategy must beat; it passed walk-forward on all four assets tested.
- New **Cipher Reversal + Trend + COT Gates** for metals and index dip-buying, with
  its full validation record (including the weak spots) spoken in its description.
- Setup announcements now speak the **complete trade plan** — entry, stop, and every
  take-profit rung — and armed/triggered/dropped setups all land in the Journal for
  review, so a missed announcement is never lost.

## Risk management that talks, never blocks

- The live-order review now **warns about liquidation**: if your leverage would let
  the exchange close the trade before your stop fires, you hear it before confirming.
- A **sector hint** notes when a new trade stacks onto correlated positions you
  already hold (BTC + ETH + KAS is one bet, not three) — a nudge toward the
  2%-per-sector discipline, never a refusal.

## Alerts, everywhere you are

- New **webhook channel**: paste a Discord or Slack webhook URL in Settings → Alerts
  and every alert lands in your channel — alongside the existing speech, sounds,
  email, and Telegram. Custom endpoints get structured JSON.

## Housekeeping

- The green-and-gold logo is now the app icon, splash, favicon, and About emblem.
- On restart, the terminal announces any open paper or broker positions it finds.
- Settings pages corrected (live version number, working background-color picker).
- Cipher A is retired (Cipher B carries all of its information); saved workspaces
  that use it keep working.

---

# What's New in 1.5.0

A short, user-facing summary of what changed since **1.4.0**. For the full engineering
changelog see [`CHANGES.md`](CHANGES.md).

> Version note: 1.5.0 is a **minor** release — it adds a lot, but nothing breaks.
> Saved workspaces, shortcuts, and API keys from 1.4.0 load unchanged. The one thing
> that happens automatically the first time you run 1.5.0: your saved API-key list is
> moved into your operating system's encrypted storage (the keys themselves were
> already encrypted; now the list of which exchanges you use is too), and the old
> plain-text file is removed for you.

This is the **finalization release**. The terminal is still, first and foremost, a
voice-and-audio instrument — that hasn't changed, and everything visual below is
optional and off until you turn it on. What 1.5.0 adds is depth: a complete mouse
experience for sighted and low-vision users sharing your screen, touch and mobile
screen-reader support on the website, and a set of visual accommodations for other
needs — all while keeping the audio-first experience exactly as it was.

---

## Use the chart with a mouse — and hear everything you click

Every mouse action now lands in the same place the keyboard navigates, and everything
is spoken through the same voice you already know. That makes the mouse fully usable
alongside a screen reader, and it lets a sighted friend point at something and have you
hear exactly what they're looking at.

- **Click a bar to hear it.** A single click moves the reading cursor there and reads
  it out, just like arrowing to it. Click roughly, then fine-tune with the arrows —
  precise aim is never required.
- **Click near an indicator to focus it.** If your click lands close to an EMA, a MACD
  line, or a band, focus moves to that indicator before the bar is read.
- **Shift+click to measure.** Speaks a summary from your cursor to the clicked bar —
  how many bars, the dates, the high and low, and the net change — without moving your
  place.
- **Scroll to zoom, Shift+scroll to pan, double-click to jump to now.**
- **A hover crosshair** shows the date, price, and OHLC of the bar under the pointer as
  real on-screen text (so magnifiers and zoom work on it). It never speaks — clicking
  is the spoken path.
- **Right-click for a chart menu**: play from here, jump to latest, crosshair, and
  every indicator listed **by name** with Mute, Hide, Properties, and Remove — so you
  never have to click a two-pixel line to act on an indicator. Right-click *near* a
  line and the menu opens straight on that indicator.
- **Optional extras** in that menu: **magnet snap** (drawing anchors jump to the exact
  open/high/low/close) and **hover sound** (a soft tick per bar that hums the price
  contour as you sweep the mouse). Both are off by default.

## Touch and mobile

On a touchscreen — a phone or tablet on accessibletrader.com, or a touch laptop — the
chart understands the gestures you'd expect, each one spoken like the keyboard:

- **Tap** a bar to hear it, **drag** to pan, **pinch** to zoom, **double-tap** to jump
  to the latest bar, **press and hold** for the menu.
- A **touch toolbar** appears below the chart with large, clearly-labelled buttons —
  Previous/Next bar, Previous/Next component, Play, and Chart menu — so nothing ever
  depends on a gesture landing just right.
- **With VoiceOver or TalkBack running**, a **Bar navigator** (announced right before
  the chart) lets you flick up and down to move through bars, each spoken with its
  date and price.

Touch in the installed Windows/macOS/iOS/Android apps flows through the same layer and
is expected to work the same way, but it hasn't yet been verified on physical devices —
until then, a connected Bluetooth keyboard remains the fully-supported way to drive the
mobile apps. Page pinch-zoom on the website is no longer blocked, so browser
magnification works everywhere.

## Visual accessibility — all optional, off by default

The terminal presents itself audio-first. If a visual channel helps you, turn any of
these on in **Settings (F12) → Appearance → Visual accessibility** — each applies
instantly:

- **Visual earcons** — mirrors every sound cue (order fills, stops, take-profits,
  setups, new bars, errors) as a brief on-screen badge, for deaf and hard-of-hearing
  traders or a quiet room. One gentle fade per event; nothing flashes.
- **Color-vision-safe chart colors** — up in blue, down in orange instead of
  red/green, distinguishable with deuteranopia or protanopia.
- **Hollow up-candles** — rising candles drawn as outlines, so direction reads by shape
  alone.
- **Text size** — scale the whole interface from 85% to 175%.

Two more accommodations need no switch: if your system is set to **reduce motion**, the
terminal's animations turn off automatically, and on high-resolution screens the chart
now renders crisply at your display's native pixel density instead of softly upscaled.

## Find any setting, and other conveniences

- **Search settings** — a search box atop the F12 dialog: type "speech", "theme", or
  "alerts" and jump straight to the setting, no matter which tab it lives on.
- **The AI Analyst shows its analysis as text**, not just speech.
- **Rebinding a shortcut tells you** if it displaced another command's only key, so a
  shortcut never goes missing silently.
- **Getting-started help** — press F1 for a five-step walkthrough to your first chart.

## Under the hood: security and reliability

- Custom scripts now **refuse to run** if the operating-system sandbox is unavailable,
  rather than quietly running unprotected. Your **API-key list is encrypted at rest**.
  The hosted website sends proper security headers and throttles login attempts.
- A data bug was found and fixed: one stock/forex provider (FMP) could return stale
  bars when asked for a small number of recent ones.
- Test coverage grew from 1,176 to **1,505** automated tests, with a new suite for the
  touch gestures — so the features above are guarded against regressing.

---

_Questions or issues: see [`USER_MANUAL.md`](USER_MANUAL.md) and
[`QUICKSTART.md`](QUICKSTART.md)._
