# The asset dossier (Alt+I), and the crypto vetting question

Built 2026-08-02. Suite 2701 green. Verified live against CoinGecko, GitHub and SEC EDGAR.

---

## How it works

**The asset is whatever is loaded on the active chart.** Choose market → provider → symbol on the
toolbar, load the chart, press `Alt+I`. There is deliberately no symbol picker inside the modal: a
second selection would drift out of sync with what the user is looking at, and "the dossier is
describing a different asset than the chart" is a bug nobody would catch by ear.

**Lookup is dynamic, by ticker, at open time.** Nothing is pre-baked. The asset class comes from the
**market the chart was loaded from**, not from the ticker — "ETH" is a coin on Bitstamp and could be
an equity ticker elsewhere, and guessing from the symbol would produce a confident dossier about the
wrong kind of thing.

**`Alt+I` was verified free.** The plain-Alt letters already bound are A, B, C, D, H, J, K, L, M, O,
R, S, T, W and comma. I for Instrument / Info.

### When there is nothing to show

This is the case the feature exists to handle well, so every section carries one of four states and
**a blank row is treated as a bug**:

| state | means | example |
|---|---|---|
| **Ok** | a real value | — |
| **NoData** | the source answered and the answer is "none" | "no insider filings in 90 days" — often the interesting one |
| **NotApplicable** | meaningless for this asset class | R&D expense for a coin |
| **Unavailable** | the source could not be reached, or is not configured | EDGAR timed out |

`NoData` and `Unavailable` are never collapsed together: the first is a finding, the second is a
reason to retry. For an unlisted crypto ticker the dossier says so explicitly — *"No listing found.
For a very new token this is expected, and it means nothing here can be verified — treat every claim
about it as unchecked."* That is the most informative thing the screen can say about a brand-new
token, and it would be lost in a blank panel.

The **chart read never depends on a network** and is always first, so the modal is useful even with
every remote source down.

### Tabs are by question, not by source

Tabs labelled "CoinGecko", "GitHub", "SEC" would push the synthesis back onto the user — four tabs
and four half-answers to hold in their head to decide one thing. Tabs are therefore **Chart read ·
Identity · Supply and dilution · Development · Disclosure · Checks** (crypto) and **Chart read ·
Company · Financials · Filing activity · Checks** (equities), with **every individual field citing
its own source**. Same rule as the rest of this layer: display everything with its provenance.

---

## Crypto: what makes it more than a CMC reprint

The honest starting point is that price, market cap and rank **are** the front page. Repeating them
adds nothing. Two things are not on any price page, and they are what the 24-point guide spends
several of its steps on:

**1. Is anyone still building it?** CoinGecko's coin document carries developer counts, and it is
free and keyless. But its figures are only as current as the repository someone registered with
them, and that goes stale silently.

> **The worked example, measured 2026-08-02:** CoinGecko reports **Kaspa at zero commits in four
> weeks**, because it tracks `kaspanet/kaspad` — which was superseded. `kaspanet/rusty-kaspa` was
> pushed **that same day**, with 843 stars against kaspad's 528. Reading the aggregator alone shows
> one of the most active projects in the market as abandoned.

So the dossier queries GitHub directly on the listed repositories, and when everything listed looks
stale (>30 days) it sweeps the **owning organisation** for its most recently pushed repos. That
found `kaspanet` active the same day. Crucially it is **labelled as what it is** — *"Most recent push
elsewhere in the org … this is activity in the same org, NOT necessarily the project's main
repository"* — because presenting a minor repo as the flagship would be a subtler version of the
error the second lookup exists to fix.

**2. Does it disclose anything?** Website, whitepaper, public source, block explorer — present or
**MISSING**. Absence is the measurement. No whitepaper or no public source is the loudest cheap
signal there is.

Plus supply and dilution (FDV/market cap, circulating share of max, uncapped issuance) and turnover
(flagged at **both** ends — too little is illiquid, too much is a wash-trading tell).

**Nothing is scored.** Eleven checks, each one comparison over a value already displayed, each
shown with its own reasoning, never summed. A single number would read as a rating. **None of these
thresholds has been tested against forward returns**, and testing them properly needs point-in-time
universe snapshots taken *forward* — dead tokens vanish from today's listings, so any backtest on the
surviving universe is poisoned. Until that exists they are labelled as conventional red flags, not
evidence.

The standing prediction, recorded before any of it was built: **this works as a veto, not a timing
signal.** It should avoid losses; it should not pick winners.

---

## The 24-point guide: what I'd keep and what I'd change

**It is a good document, and its best property is that it is mostly about disqualification.** It is
ordered roughly by cost, it is specific, and the fact that Kaspa and Bittensor pass it while most of
the market does not is exactly the behaviour a filter should have. A high false-negative rate on
garbage is the goal, not a flaw.

Five changes I would make:

**1. The VC list is a liability and should go.** It lists **3AC — Three Arrows Capital**, which blew
up in 2022 and was a catastrophic counterparty; treating "3AC invested" as a quality signal in 2026
is actively harmful. It lists **a16z twice** (#4 and #28), and the numbering skips 23. More
fundamentally, VC backing is a **lagging, promotional** signal — projects advertise it precisely
because it is persuasive, which is what makes it a poor filter.

**2. Move "research the bearish narrative" from last to first.** It is the single best item in the
document — it is a pre-mortem, and it is the step most likely to end the research early and cheaply.
Doing it 25th means doing it after you are invested in the conclusion.

**3. Add an explicit veto list.** The guide is 24 things to look at with no rule for when to stop.
Machine-checkable disqualifiers that should end the process immediately: no public source code, no
whitepaper, uncapped issuance paired with a large team allocation, FDV/MC above ~10x, self-reported
circulating supply, and a repository archived or untouched for a year. The dossier now checks eight
of these automatically.

**4. Add what is missing and matters most.** **Token unlock schedules and cliff dates** are the
single biggest driver of small-cap drawdowns and the guide only touches them obliquely under
tokenomics. Also: contract verification and mint/freeze authority, liquidity-lock status, and holder
concentration.

**5. Treat the "have OpenAI read it" steps as design-time only.** Summarising a whitepaper with an
LLM is fine and useful. It must never become a scored input, and it cannot be backtested — a model
reading a 2019 whitepaper today already knows what happened next.

**On the unanswerable points:** you are right that this is the feature, not the bug. For a new
project, "no founder information" and "no audited contract" are not gaps in the research — they are
the finding. The dossier reflects this by making absence explicit rather than blank.

---

## Screening new small caps

Being straight: **this is a different activity and should be labelled as one.** Nothing in this
project's ledger predicts returns for new listings, and the honest expected value of trading brand-new
tokens is negative before fees. What can be built responsibly is a **risk-first screener** that makes
the gamble informed rather than blind:

- Run the eleven checks across a candidate list and **sort by flags raised**, not by return.
- Surface the machine-checkable disqualifiers first, so the obvious garbage is gone before any chart
  is opened.
- **Record the universe forward.** Survivorship is fatal here and it cannot be fixed after the fact:
  the tokens that went to zero are not in today's listings. A point-in-time snapshot taken daily is
  the only way this ever becomes testable, and it costs nothing but starting.

That last point is the one worth acting on now. Everything else can wait; the recording cannot.

---

## Limits worth knowing

- **CoinGecko rate-limits** the free tier (~10–30 requests/minute). The dossier makes one coin call
  plus up to four GitHub calls per open.
- **GitHub allows 60 unauthenticated requests an hour.** The org sweep only fires when the listed
  repos look stale, to conserve that budget.
- **EDGAR covers US filers only.** ETFs, index vehicles and non-US listings are not filers, and the
  dossier says so rather than showing an error.
- **The org sweep finds the most recently pushed repo, which may be a minor one.** It answers "is
  this organisation alive?", not "is the flagship alive?", and the label says which question it
  answered.

Cross-references: `COMPANY_DATA_LAYER.md` (the dual-audience design this implements) ·
`PLATFORM_AND_SIGNAL_SERVICE.md` (describe freely, score never) · `LAB_DESIGN.md` (the 24-point
material decomposed) · `SHORTCUTS.md`.
