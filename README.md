# Accessible Trading Terminal

A trading and market-analysis terminal built for blind and visually impaired traders — not a
sighted terminal with labels bolted on afterwards.

The chart is something you **hear**. Price becomes pitch, volume becomes texture, position in
the viewport becomes stereo placement, and your screen reader speaks the numbers alongside it.
Arrow keys walk the chart bar by bar. Everything the terminal does has a keystroke, and
everything that happens says so out loud.

It is free, donation-supported, and developed in the open by a blind trader who uses it.

## What it does

- **Live and historical charts** from 33 data sources — 16 trading venues (Binance, Kraken,
  Coinbase, Alpaca, Interactive Brokers, Schwab, Oanda and more) and 17 analytics feeds
  (macro, on-chain, positioning, sentiment).
- **Sonification.** Indicators, candles, volume profiles and heatmaps each have their own
  voice, so you can tell them apart by timbre rather than by reading a legend.
- **Speech that is designed, not generated.** What gets spoken, when, in what order and on
  which channel is all under your control — and money events (fills, stops, take-profits)
  are on a channel that never goes quiet.
- **Trading**, live or paper, with a spoken order review before anything is sent.
- **Indicators, drawings and alerts**, all reachable and editable from the keyboard.
- **Your own indicators**, written in C# or transpiled from Pine Script, compiled and run in
  an OS-level sandbox.
- **A strategy lab** for backtesting, with the statistical controls that stop a backtest from
  flattering itself.

## Getting it

Pre-built binaries are on the [Releases page](https://github.com/churst90/accessible-trade-terminal/releases).

The **WebHost** build is the recommended one on every platform: run it and the terminal opens
in your browser, works with your existing screen reader, and runs on Linux, Windows and macOS.
Native desktop builds are also attached but are unsigned, so expect a SmartScreen or Gatekeeper
prompt.

From source:

```
dotnet run --project AccessibleTrader.WebHost
```

See [`docs/PLATFORMS.md`](docs/PLATFORMS.md) for which build to choose.

## Learning it

- [**Quick start**](docs/QUICKSTART.md) — first chart, first trade, in a few minutes.
- [**Keyboard shortcuts**](docs/SHORTCUTS.md) — the complete list. `F1` in the app has it too.
- [**User manual**](docs/USER_MANUAL.md) — the long-form guide.
- [**What's new**](docs/WHATSNEW.md) — the current release, in plain language.
- [**Changelog**](docs/CHANGES.md) — every release.

## For developers

- [**Architecture and subsystems**](docs/README.md) — the technical reference: rendering,
  the audio engine, the data pipeline, the plugin and sandbox model.
- [**Diagrams**](Diagrams/README.md) — the same thing in pictures, each with a prose summary.
- [**TODO**](docs/TODO.md) — what is being worked on and why, including the open design
  questions.

Contributions are welcome. Accessibility is not a feature area here — it is the acceptance
criterion for everything, and a change that cannot be operated and heard from the keyboard is
not finished.

## Licence

See [LICENSE](LICENSE).
