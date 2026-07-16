# What's New in 1.7.0

A short, user-facing summary of what changed since **1.6.0**. For the full engineering
changelog see [`CHANGES.md`](CHANGES.md).

> Version note: 1.7.0 is a **minor** release — nothing breaks. Saved workspaces,
> strategies, shortcuts, and API keys from 1.6.0 load unchanged. One thing worth
> redoing by hand: an RSI (or other bounded oscillator) saved in an old workspace
> carries the old always-on background noise — re-add the indicator once, or zero
> its Noise slider in Properties, to get the new clean sound.

---

## Your other tabs are no longer deaf

- **Background monitoring** (Settings → General, off by default, desktop builds):
  every open tab keeps being watched while you work elsewhere. Its symbol-scoped
  alerts and running strategies re-evaluate on a polling cadence you control, and
  everything they say arrives prefixed with the symbol — "BTC/USD: crossed above
  50,000" — through speech, earcons, the Journal, and your email/Telegram/Discord
  channels alike.
- One firm rule keeps it honest: **events speak from everywhere; the soundscape
  belongs to the chart you're viewing.** Background signals are announce-only —
  even an Auto-mode strategy never places an order from a background tab.
- Press **Ctrl+Alt+Shift+M** any time for a status report: each watched tab, how
  fresh its data is, and how many strategies are armed on it.

## Every indicator can be its own instrument

- **Sound themes** (Settings → General): pick Orchestra and price lines become a
  flute, the RSI a clarinet, the MACD a pipe organ, band edges glass — so during
  full-chart playback you can tell who's talking by timbre alone. Pipe organ and
  Strings themes voice the same families with different registrations; Classic is
  the original palette. Applies to indicators you add from then on; anything you
  choose per component in Properties always wins.
- Ten new **factory instrument voices** (flute, clarinet, organs, glass, strings)
  appear in every patch dropdown as "Voice: …" — assign them by hand to any
  component or earcon, and preview them in the Sound Designer.
- The three **reference-level cues** — the crossing chirp, the "almost at the
  line" approach ping, and the held-in-zone confirmation — are now re-skinnable
  in the Sound Designer's earcon panel, and each level's earcon can be switched
  off per indicator in Properties, as before.

## The sound now tells the truth

- **Loudness never encodes size, everywhere.** Candle wicks no longer get louder
  with length — length is carried by grit (roughness) at constant loudness, the
  same rule the body already followed. Wick and volume pings last a little
  longer so the texture actually registers.
- **Overbought/oversold texturing is audible and honest.** A long-standing bug
  attenuated all pink/brown noise ~30 dB below its intended level; with that
  fixed, the RSI line is now clean everywhere and rough only inside a zone —
  and the zone texture is stronger by default.
- **Volume bars speak exactly and directionally**: "12,345.68, down" — full
  decimals when they exist, and the same up/down the bar's colour shows.
  The candle **Body** component reads both ends: "Body. Bullish. Open 49,800,
  close 50,200."

## Short interest, honestly labelled

- The FINRA source now serves biweekly **short interest** (`TICKER_SHORTINT`)
  and **days-to-cover** (`TICKER_DTC`) — still completely keyless. Values appear
  only once FINRA actually published them, so backtests can't cheat. Honest
  limitation, verified against the live API: FINRA publishes these for OTC
  securities only; for listed names (AAPL, TSLA) keep using the daily short
  volume ratio.

## Smaller things you'll notice

- **Timeframe buttons never lie**: the quick-picks are exactly what the provider
  serves, a provider with only one timeframe (most analytics feeds are
  daily-only) hides the Time controls entirely, and switching providers snaps an
  unsupported timeframe to a real one and says so out loud.
- **API keys**: the first profile you save for a provider now activates itself
  (no more silently inactive keys), activation takes effect without a restart,
  and the Secret field says what it is — optional for key-only providers like
  Twelve Data.
- **Strategy setups → alert channels** finally has its switch in Settings →
  Alerts (it was documented but unreachable), with a webhook picker for where
  setups land. Hover sonification and drawing magnet snap are in Settings too.
- **Fixes**: Escape now closes the Save/Load workspace, AI Analyst, and Alerts
  dialogs from anywhere; the Journal announces itself when it opens; the touch
  navigation bar no longer clutters desktop screen readers; Ctrl+C on the web
  host exits cleanly instead of printing an audio-pipe error.
- Test coverage grew from 1,593 to **1,646** automated tests, still run in both
  Debug and Release configurations on every push.

---

_Questions or issues: see [`USER_MANUAL.md`](USER_MANUAL.md) and
[`QUICKSTART.md`](QUICKSTART.md)._
