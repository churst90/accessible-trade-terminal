# The crypto screener — layer 1

`StrategyLab screen-crypto` · built 2026-08-02 · 27 tests · runs on the committed universe archive.

---

## What it is, and the one thing it is not

**It sorts by flags raised, never by expected return.** That is not a hedge, it is the design.

Nothing in this project's ledger predicts returns for small or new tokens, and the honest expected
value of trading brand-new listings is negative before fees. What *can* be built responsibly is a
filter that removes the obviously disqualified before a chart is ever opened — which makes the
gamble informed rather than blind. A screener that ranked by upside would be making a claim this
project has never earned.

## Every check is arithmetic

No model, no LLM, no judgement, nothing that cannot be recomputed identically tomorrow. That matters
for a reason beyond taste: **layer 2 is the forward test of whether these flags actually predicted
death**, and a rule containing judgement cannot be replayed against a historical snapshot to find
out.

| Flag | Trips when | Why it is on the list |
|---|---|---|
| `fdv` | FDV > 3× market cap | Most of the supply is unissued; every unlock is a seller who paid less than you |
| `float` | < 30% of max supply circulating | Same fact from the supply side; catches what the FDV field misses |
| `uncapped` | no maximum supply | Not disqualifying — several credible chains are uncapped — but a fact you should have to accept knowingly |
| `illiquid` | turnover < 0.5% of market cap | You may not be able to exit |
| `wash` | turnover > 150% of market cap in a day | Volume above the entire market cap is not organic interest |
| `microcap` | market cap < $10M | One holder can move it at will |
| `drawdown` | > 95% below all-time high | A full cycle of buyers is underwater; every recovery passes through them |
| `depeg` | stablecoin > 2% off $1 | The only mechanical question worth asking of a dollar token |

It runs **on the archive, not on a live sweep** — the daily snapshot already carries supply,
dilution, turnover and drawdown for a thousand assets, so layer 1 costs **zero API calls** and can
be re-run against any past day. Which is exactly how layer 2 will eventually be run.

## Calibration against assets with a known answer

The most useful check when setting thresholds is to run them over assets you already have a view on.
`--only KAS,TAO,BTC,ETH,USDT,TRUMP`, as at 2026-08-02:

| Asset | Flags | |
|---|---|---|
| **KAS** (Kaspa) | **0 of 7** | passes, matching its standing as a genuine project |
| **TAO** (Bittensor) | **0 of 7** | passes |
| **BTC** | **0 of 7** | passes |
| **ETH** | 1 of 6 | `uncapped` — factually true, correctly not disqualifying |
| **USDT** | 1 of 4 | `uncapped`; peg check clean at 0.1% off |
| **TRUMP** | **3 of 7** | `fdv` 4.0×, 25% float, 98% below high — the expected memecoin profile |

Kaspa and Bittensor passing while a memecoin raises three is the behaviour a filter should have. A
high false-negative rate on garbage is the goal, not a flaw.

## Distribution across the top 1,000

| Flags | Assets | |
|---|---:|---|
| 0 | 136 | |
| 1 | 413 | |
| 2 | 347 | |
| 3 | 50 | |
| 4 | 4 | |

The distribution is printed first on every run, and deliberately so: **a screen that flags
everything and one that flags nothing are equally useless, and both look perfectly reasonable if you
only read the top of the list.**

## Two defects the first live run exposed

Neither was caught by a test. Both came from running the thing on real data and reading the output.

**A sentinel value became a confident sentence.** One asset was reported as having *"FDV is
999999995.3x market cap — most of the supply is not issued yet"*. That is a broken field wearing
prose. An implausible ratio (> 1000×) is now reported as unusable data and trips nothing: stating a
fabricated fact in the same voice as a real one is worse than staying quiet.

**Stablecoins were judged on rules that cannot apply to them.** Two dollar-pegged tokens were
flagged *"100% below all-time high"*. A stablecoin is *supposed* to sit at its high and to turn over
many times its market cap, so those flags are definitionally true and carry no information. Pegged
assets are now excluded from the price-shaped checks (drawdown, turnover) and get a `depeg` check
instead — detected by price behaviour rather than by a curated list, which would go stale on the
next launch, or by matching "USD" in the ticker, which would catch wrapped assets and miss
euro-pegged ones. Supply and issuance checks still apply: those matter at least as much for
something claiming to be money.

A third, cosmetic but not harmless: the per-asset view printed each check's *failing* sentence
beside "ok", so Bitcoin read as **"ok — no maximum supply"**. A flat falsehood produced purely by
formatting. Only a tripped check may print its sentence now.

## The thresholds are conventions, not findings

**Not one has been tested against forward outcomes.** They are the machine-checkable half of the
24-point vetting guide, and the output says so.

The standing prediction, recorded before any of this was built: **this works as a veto, not a timing
signal.** It should avoid losses; it should not pick winners.

Testing that needs point-in-time universe snapshots taken **forward** — dead tokens are absent from
today's listings, so any backtest on the surviving universe is poisoned. `record-universe` has been
accumulating since 2026-08-02; `universe-status` reports how close it is to answering.

## What layer 2 will look like

Once the archive is deep enough, the question becomes answerable in exactly one form:

> Using only assets present on day X, did those flagged on day X survive worse than those unflagged,
> over the following N months?

Assets present on the day, flags computed from that day's data, outcome measured forward. No
survivorship, no restatement, no judgement anywhere in the loop. That is the whole reason every
check here is arithmetic.

## Layer 3, which is not code

Team credibility, whitepaper substance, the bear case, actually using the product. Stored as **notes
on the asset, displayed, never scored** — the same rule as everything else in this layer. The
`Alt+I` dossier is where those live.

Cross-references: `ASSET_DOSSIER.md` · `PLATFORM_AND_SIGNAL_SERVICE.md` · `ALPHA_LEDGER.md` ·
`UniverseRecorderCommand` (the archive this reads).
