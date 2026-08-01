# The strategy catalogue — where specs live and how they reach a chart

Adopted 2026-08-01, completing the split begun in
[STRATEGY_LIBRARY_POLICY.md](STRATEGY_LIBRARY_POLICY.md).

## The shape of it

| | holds | ships to |
|---|---|---|
| **AccessibleTrader.StrategyLab** (`Catalogue/`) | 24 strategy specs with their provenance, plus the retirement record for 6 more | nobody — it is a research tool |
| **AccessibleTrader.Core** | the engine, the library, the bundle **format** | the app |
| **the terminal** | an **empty** library on first launch | the user |

Core knows what a strategy file looks like. It does not know a single strategy. That is the whole
architecture.

## Provenance

`StrategySpec.Provenance` (`AccessibleTrader.Sdk/Strategies/StrategyProvenance.cs`) records four
things: an evidence level, what the spec was tested on, which controls were run, and a one-sentence
verdict. Negative verdicts are recorded, not softened.

| level | means |
|---|---|
| `Untested` | never run through the harness. Assume nothing. |
| `InSampleOnly` | has numbers, but only on the data it was designed or **selected** on — including a cell promoted out of a battery. |
| `WalkForward` | survived data it was not built on. No null arm. |
| `ControlTested` | beat an explicit control: random-entry, surrogate series, exposure-matched, era-sliced. |
| `Fragile` | real under its original test, does not survive perturbation. A default, not an edge. |
| `Falsified` | tested and failed. Retired from the catalogue, kept in the retirement record. |

`Fragile` and `Falsified` are outcomes, not rungs — `catalogue export --min-evidence` never sweeps
them in, and naming one requires an explicit `--id`.

The catalogue holds **24** specs: **1** control-tested, **5** walk-forward, **9** in-sample-only,
**7** untested, **2** fragile. The one control-tested entry is the plain trend baseline, whose
verdict is that the family works while its own fitted parameters carry no information. That is the
honest state of five months of strategy work, and it is the reason the terminal stopped shipping
these as a starting library.

**Six were retired on 2026-08-01** — the four Cipher-SR trilogies and the two symmetric
negative-expectancy shorts. Their code is gone; their verdicts are not. `CatalogueProvenance.Retired`
keeps each one's id, name, retirement date and the sentence that killed it, and
`ForAnyEverKnown(id)` still resolves a retired id, so an old note or an exported bundle that names
one gets "this failed, here is why" rather than "unknown id". Deleting a falsified strategy is
housekeeping; forgetting that it failed is how it gets reinvented.

### Two caveats that recur

**Battery promotion is not out-of-sample.** Several v23 variants were promoted from an 89-cell gate
battery for being the best cells. Choosing the best of N is a decision made in-sample even when each
cell was individually walked forward — the winner's number is a maximum over 89 draws, not an
estimate. All of them read `InSampleOnly`.

**Cipher SR repaints.** The SR-proximity edge was traced to a 15-bar lookahead. It was corrected
inside `ConfluenceCommand`, but the provider still repaints, so any backtest of a spec with a
`CIPHER_SR` leaf in its entry stack is optimistic by an unmeasured amount. Four of the affected
specs were retired for it; three remain and say so.

### Libraries that predate the split

An existing install keeps the thirty specs it was already seeded with — including the six since retired —, and those rows have no
provenance attached — nothing rewrites a user's saved strategies. They show **"Not recorded (older
built-in)"** rather than a bare "Not recorded", because for a `builtin.*` id the bare phrasing would
read as "you built this", which is the opposite of true. To attach the evidence, export the
catalogue and import it: import skips ids you already hold, so the honest route is to delete the
built-ins you want re-described first, or simply read the verdicts with
`StrategyLab catalogue list --verbose`.

## Moving a spec into a terminal

```
# what is in the catalogue and what is actually known about it
StrategyLab catalogue list --verbose
StrategyLab catalogue list --status Falsified

# write a bundle
StrategyLab catalogue export --out my-strategies.json --id builtin.long.trend-baseline
StrategyLab catalogue export --out survivors.json --min-evidence WalkForward
```

Then in the app: **Strategy modal → Library tab → Import strategies**, either choosing the file or
pasting its contents (the paste route exists because a file picker is the worse of the two with a
screen reader, and these files are small).

### What import guarantees

- **Never overwrites.** An id already in your library is skipped and reported. Your edits win.
- **Never starts anything.** `IsAutoActivate` is forced false regardless of what the file says.
- **Never runs code.** Roslyn-source specs are refused; that route is the Custom tab, where you
  paste the source yourself.
- **Never throws.** A corrupt or unrelated file produces a message, and the library is untouched.
- **Tells you the whole outcome** — imported, skipped, rejected, and how many arrived set to place
  orders rather than raise suggestions — in one announced sentence.

## The file format

```jsonc
{
  "FormatVersion": 1,          // reader accepts <= MaxSupportedFormatVersion, else says "update the app"
  "Source": "AccessibleTrader StrategyLab catalogue",
  "CatalogueVersion": "2026-08-01.2",
  "ExportedUtc": "2026-08-01T00:00:00Z",
  "Strategies": [ /* StrategySpec, each with its Provenance */ ]
}
```

`StrategyBundle` / `StrategyBundleService` in
`AccessibleTrader.Core/Services/Strategies/StrategyBundle.cs`. The condition tree round-trips
through the `$kind` discriminator already on `ConditionNode`.

## Adding a strategy to the catalogue

1. Add the builder and its id constant to `Catalogue/StrategyCatalogue.cs`, and list it in
   `AllSpecs()`.
2. Add an entry to `Catalogue/CatalogueProvenance.cs`. `CatalogueProvenanceTests` fails the build
   without one — "Untested / never run / none" is a perfectly good entry and the honest one for a
   new spec.
3. Bump `StrategyCatalogue.Version`.

Do not renumber existing ids. They are the join key between a lab run, a note, and a spec someone
already imported.
