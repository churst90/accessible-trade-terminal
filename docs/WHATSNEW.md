# What's New in 1.8.0

A short, user-facing summary of what changed since **1.7.0**. For the full engineering
changelog see [`CHANGES.md`](CHANGES.md).

> Version note: 1.8.0 is a **minor** release — nothing breaks. Saved workspaces,
> strategies, shortcuts, patches, and API keys from 1.7.0 load unchanged.

---

## Your settings finally stick

- The speech and navigation preferences that lived only for the session — speak
  timestamps, timestamp placement, column headers, speech order, new-bar
  announcements, WASAPI latency, panning step — now **persist across restarts**.
  They had silently reset to defaults on every launch since the beginning.
- Deliberately unchanged: the F2/F3 speech and sonification mutes stay
  session-only, so the terminal can never start silent on you.

## Bring your own waveforms

- **Import WAV files in the Sound Designer.** A short single-cycle WAV (the free
  AKWF collection is thousands of them, or one period of any recorded
  instrument) becomes a **wavetable** — a custom oscillator shape playable at
  any pitch with envelopes, noise, and layering like any built-in waveform. A
  longer WAV becomes a one-shot **sample** for earcons and signal layers.
  Imports persist and appear in every oscillator's waveform list.

## Patches that respect what the sound means

- Putting a patch on an RSI no longer flattens it into one sound: **the
  overbought/oversold zone texture always plays on top of any patch**, in both
  navigation and playback.
- Oscillators get **Above midline / Below midline** patches (split at RSI 50),
  zero-anchored histograms get **Positive / Negative**, and only price bars say
  **bullish / bearish** — each component's sound options now match what it is.
- Lifting an arrow key now stops **the whole note**: multi-layer patches (organ
  octaves and all) release together instead of the top notes ringing on.

## Touch controls know their place

- The touch toolbar **and** the mobile flick slider ("Bar navigator") share one
  setting: Settings → General → Touch navigation bar — Automatic / Always /
  Never, applied the instant you change it. Automatic detection is far
  stricter (a machine with a mouse is a desktop, whatever the input stack
  claims), and Never is absolute.
- The Braille / tactile display setting no longer appears on the web host —
  the Dot Pad connects to the machine running the app, so it belongs to the
  desktop builds only.

## Under the hood, for people who read changelogs

- One visible speech-precedence list; a typed settings layer where a typo'd
  key is a compile error; every dialog's open/announce/Escape contract enforced
  by CI; the audio engine gains "perceptual" tests that measure rendered sound
  energy so an inaudible-texture bug can never hide again; a data-access seam
  (IMarketFeeds) preparing the eventual multi-chart pipeline.
- Test coverage grew from 1,646 to **1,701** automated tests, run in Debug and
  Release on every push.

---

_Questions or issues: see [`USER_MANUAL.md`](USER_MANUAL.md) and
[`QUICKSTART.md`](QUICKSTART.md)._
