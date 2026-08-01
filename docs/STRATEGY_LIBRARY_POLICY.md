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

The existing design was already careful in one respect worth preserving: seeds defaulted to
`IsAutoActivate = false`, and the seeder never resurrected a spec the user had deleted. Nothing ever
executed without explicit user action. The problem was the *implied endorsement*, not silent trading.

## Second half, 2026-08-01: the specs moved out

The library now ships **empty**. `JsonStrategyLibrary.Reload()` no longer seeds anything, and
`BuiltInStrategySeeds` is gone from `AccessibleTrader.Core` — the thirty specs live in
`AccessibleTrader.StrategyLab/Catalogue/` as the research catalogue, each with a recorded
provenance. Full detail in [STRATEGY_CATALOGUE.md](STRATEGY_CATALOGUE.md).

Why the seeding had to go too, having already removed the recommender: a library pre-filled with
thirty specs at first launch *is* a recommendation, just a slower one. The user did not ask for
them, cannot tell which have been tested, and reasonably assumes anything the application put there
on its own is something the application stands behind. Of the thirty, one is control-tested, five
carry a plain walk-forward, and six are recorded as falsified.

What replaced it:

- **Provenance on the spec.** `StrategySpec.Provenance` (`StrategyEvidenceLevel` + what it was
  tested on, which controls ran, the verdict). The library table shows it for every row, including
  **"Not recorded"** for user-built specs — a table where tested and untested strategies look
  identical is the same implied endorsement in a quieter form.
- **An import path.** `StrategyBundleService` reads a versioned JSON bundle. It never overwrites an
  existing spec, forces `IsAutoActivate = false` so importing cannot start anything, refuses
  Roslyn-source specs (importing a file must not compile and run code), and returns an error rather
  than throwing on bad input.
- **A first-class empty state.** A focusable heading, an explanation that empty is intentional, and
  both routes out — Build Setup, or import.
- **`StrategyLab catalogue list | export`.** The lab's side of the same contract.

## What was deliberately kept

**`AssetClassifier.Classify()` stays.** Profiling an asset by volatility, cycle, regime and liquidity
is a neutral measurement, and it is the basis of the research lab's character classifier — the
single most robust finding in this project is that asset character determines which tool family
applies. It was the *mapping from profile to a named strategy* that was an opinion, not the profiling.

**The specs themselves are kept, in the lab** — including the falsified ones. A recorded negative
stops the next person re-running the same idea hopefully; deleting it just makes room for a
rediscovery.

## Enforcement

`AccessibleTrader.Tests/StrategyLibraryPolicyTests.cs` fails the build if:

- any per-asset recommender reappears in `Core`, `BlazorClient.Components` or `Maui`
- a "Use Recommended" control is rendered in any `.razor`
- `AssetClassifier.Classify` is deleted while removing the recommenders (guards over-correction)
- a strategy catalogue or seeder reappears in shipping code (`BuiltInStrategySeeds`,
  `EnsureSeeded`, `StrategyCatalogue.AllSpecs`)
- the importer stops forcing `IsAutoActivate = false`

Comments are stripped before matching, so a note *about* the removal does not trip the guard.

`AccessibleTrader.Tests/CatalogueProvenanceTests.cs` fails the build if a catalogue spec has no
provenance entry, if an entry names a spec that no longer exists, or if a verdict is too thin to
say anything. Adding a strategy to the lab therefore requires stating what is known about it.

## Still to do

- **Library export.** The terminal cannot write a *bundle*. The older per-spec route still exists
  (Build Setup → Export / Import latest, writing one `.atstrat` file into `{AppData}/exports/` and
  reading back the most recent), but it handles a single spec at a time and cannot be pointed at an
  arbitrary path — so moving a whole library between machines is still one-directional.
- **`StrategyExecutionMode.Auto` on import** is preserved and counted, and the count is announced,
  but there is no per-spec confirmation before a user starts one.
