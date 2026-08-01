# Strategy library policy — the terminal ships tools, not opinions

Adopted 2026-08-01.

## The rule

**AccessibleTradeTerminal does not choose a strategy for the user.** It ships the engine, the
editor, the backtester and the library. Which strategy to run is the user's decision, always.

## What was removed and why

Until 2026-08-01 the app made an automatic per-asset recommendation, surfaced in four places:

| location | form |
|---|---|
| `StrategyModal.razor` | highlighted library row + "the empirically-recommended v23 long strategy is highlighted below" |
| `SummaryExport.razor` | ★ marker, " — recommended for {symbol}" in the dropdown, "Use recommended" button |
| `BuildSetupTab.razor` | "Use Recommended" button loading a full editable preset |
| `AssetClassifier.RecommendV23Long/Short` | the picker itself |
| `BuiltInStrategySeeds.GetV23*Preset*` | symbol-string and behaviour-driven routes |

Two reasons it had to go, in order of importance:

**1. It was an opinion carrying the application's authority.** A highlighted row in the product's own
UI reads as an endorsement. Nothing about the design signalled that these were research artifacts.

**2. Every branch returned a Cipher-B variant** — and Cipher confluence is precisely what this
project's own research falsified. Eight versions of pure-Cipher confluence walked forward to
break-even; structure labels tested indistinguishable from random (`ConfluenceCommand`); Cipher SR
proximity turned out to be a lookahead artifact. Recommending a component we have shown does not
work is worse than recommending nothing.

The existing design was already careful in one respect worth preserving: seeds default to
`IsAutoActivate = false`, and the seeder never resurrects a spec the user has deleted. Nothing ever
executed without explicit user action. The problem was the *implied endorsement*, not silent trading.

## What was deliberately kept

**`AssetClassifier.Classify()` stays.** Profiling an asset by volatility, cycle, regime and liquidity
is a neutral measurement, and it is the basis of the research lab's character classifier — the
single most robust finding in this project is that asset character determines which tool family
applies. It was the *mapping from profile to a named strategy* that was an opinion, not the profiling.

The 30 built-in seeds also remain, as a library the user browses. They are templates, not advice.

## Enforcement

`AccessibleTrader.Tests/StrategyLibraryPolicyTests.cs` fails the build if:

- any per-asset recommender reappears in `Core`, `BlazorClient.Components` or `Maui`
- a "Use Recommended" control is rendered in any `.razor`
- `AssetClassifier.Classify` is deleted while removing the recommenders (guards over-correction)

Comments are stripped before matching, so a note *about* the removal does not trip the guard.

## Still to do

The seeds themselves have not yet moved to the lab. They remain in
`AccessibleTrader.Core/Services/Strategies/BuiltInStrategySeeds.cs` and are shared with
`AccessibleTrader.StrategyLab/RunCommand.cs`, so relocating them is a real refactor rather than a
deletion. The intended end state:

- specs live in the lab as a versioned catalogue with **provenance per spec** — tested or untested,
  against which controls, with what verdict (most will read "untested")
- the terminal keeps the engine and gains a documented **strategy-import path**
- an empty library gets a first-class empty state with its own screen-reader announcements and a
  clear "import or create" route — not silence
