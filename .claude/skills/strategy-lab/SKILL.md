---
name: strategy-lab
description: Use when running or adding a StrategyLab research command — the CLI surface, where snapshot data lives, how cross-series feeds are wired, and the conventions a new command must follow.
---

# The StrategyLab harness

A console app under `AccessibleTrader.StrategyLab/` that replays offline snapshots through the real
indicator engine, so research uses the same code paths the application ships.

## Running

```bash
cd AccessibleTrader.StrategyLab
dotnet build -v q --nologo
dotnet run --no-build -- <command> --snapshots ../strategy-lab-data --tf 1d
```

Common flags: `--snapshots` (dir), `--only <substr>` (filename filter), `--tf`, `--permutations`,
`--surrogates`. `dotnet run -- help` lists every command.

Research commands relevant to strategy work: `cross` (Trading Cross z-momentum), `gate`
(conditioner vs 200-MA vs random), `polarity` (revert-vs-trend by asset), `crowding` (funding+OI),
`confluence` (structure as context), `poc-dev`, `respect`, `battery`, `walk-windows`.

## Data

`strategy-lab-data/` at the repo root — **gitignored**, so it can hold large snapshots freely.
Never commit it; the repo's `.git` is already heavy with historical build artifacts.

- `bitstamp_*`, `mexc_*` — crypto OHLCV (1h…1w)
- `twelvedata_*`, `yahoo_*` — equities, ETFs, metals, bonds (1d)
- `xs_*` — cross-series feeds: Binance Vision funding (8h) and open interest (1d), CFTC COT (1w),
  CoinMetrics on-chain, Alternative.me Fear & Greed

`SnapshotCommand.Load(path)` returns `SnapshotFile` with `Provider`, `Symbol`, `Timeframe`, `Bars`.
Filter out `xs_*` when enumerating price files.

**Coverage limits that constrain study design:** open interest starts 2021-12 (so crowding studies
span one crypto cycle); funding starts 2020; equity history runs 20–55 years while crypto runs 3–15.
Sample-length asymmetry that large will fabricate cross-sectional results if you use any
length-sensitive measure.

## Cross-series feeds

`LabHost.Build()` auto-discovers `xs_*.json` in `strategy-lab-data` and registers a
`SnapshottingCrossSeriesCache`. Then indicators that need external data just work:

```csharp
var engine = LabHost.Build().Services.GetRequiredService<IIndicatorEngine>();
var result = await engine.CalculateAsync("CROWDING_INDEX", bars,
    new Dictionary<string, object> { ["__symbol"] = snap.Symbol }, default);
```

Alignment is **causal** — `CrossSeriesAligner.Fill` takes the most recent tick with timestamp ≤ the
bar's, leaving NaN before the feed starts. No lookahead. Always check how many non-NaN points you
actually got; a missing feed yields an all-NaN array rather than an error.

`IndicatorProbeCommand` uses a hardcoded provider switch and **silently falls back to
ValueDeviation** for codes it does not know. Do not trust it for arbitrary indicators.

## Conventions for a new command

- One `static class <Name>Command` with `RunAsync(...)`, wired into `Program.cs`'s switch with
  `GetFlag(args.Skip(1).ToArray(), "--x") ?? default`.
- Signals fire on bar `i`; **enter at bar `i+1`'s open or close, never bar `i`'s**. Filling on the
  bar that produced the signal hands a close-based rule a free period.
- Skip bars where any conditioner is NaN or still in warmup — do not let undefined count as "off".
- Standard trade harness for comparability across commands: enter next open, 1×ATR(14) risk,
  2R target, 20-bar horizon, R-multiple outcome.
- Fixed RNG seeds so a re-run reproduces the number.
- Print the controls beside the result and a `── VERDICT ──` block that states the conclusion in
  words, including when it is null.
- Document *why* the test is built the way it is in the class XML docs — especially which trap the
  design is avoiding. The comment is the durable part.

## Build note

The whole solution builds with `-p:UseRazorSourceGenerator=false` (SDK 10.0.301 miscompiles Razor
`<text>` and same-line code-block markup). The lab itself has no Razor, so a plain
`dotnet build` inside `AccessibleTrader.StrategyLab/` is fine.

Full test suite: `dotnet test AccessibleTrader.Tests/AccessibleTrader.Tests.csproj
-p:UseRazorSourceGenerator=false`.

## Writing results up

Findings go in `docs/<TOPIC>_FINDINGS.md` — the thesis, the tests, the controls, the verdict, and an
explicit scope/caveats section. Commit the command and the doc together.
