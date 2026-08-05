# Which crypto venues to add next

Written 2026-08-05, in answer to "kucoin, lbank, bybit, maybe suggest some
others — remember I'm in the US, but we'll have users from other countries too."

**Regulatory availability changes, sometimes abruptly, and the notes below are a
starting point rather than a citation.** Confirm current status before building.
The one thing that does not change is the framing in §1.

---

## 1. The framing: availability is the USER's constraint, not ours

The terminal runs on the user's own machine with the user's own keys. It is not a
broker and holds no funds. So **which venues a given person may use is decided by
where that person is**, not by which plugins exist.

That has a liberating consequence and an awkward one.

**Liberating:** adding a venue the maintainer cannot personally use is still
worth doing, because international users can. A US-only plugin set would make the
terminal much less useful abroad for no benefit to anyone.

**Awkward:** the maintainer cannot *verify* a venue he cannot open an account on.
And this project's whole method is verification — the capability audit was wrong
four times before it was right, and every provider finding that survived did so
because it was checked against reality.

**So the ordering criterion is not popularity. It is: can this be verified without
an account the maintainer cannot get?** A venue with a self-service testnet can be
built and proven end to end from St. Louis regardless of whether its mainnet
serves US users.

---

## 2. The candidates

| Venue | US retail? | Testnet / demo | API quality | Notes |
|---|---|---|---|---|
| **Bybit** | No | **Yes, self-service** | Excellent, well documented | Large international venue; v5 API is unified across spot/derivatives |
| **OKX** | Separate US entity | **Yes, demo trading** | Very good | Two entities; the international one is the large venue |
| **KuCoin** | **No** — exited US retail after a 2025 settlement | Sandbox status has changed over time — check | Good | Very widely used internationally |
| **Gate.io** | No | Yes | Good | Large altcoin coverage |
| **Bitget** | No | Yes | Good | Copy-trading focus |
| **LBank** | No | Unclear | Thinner docs | Smaller; verify the API is maintained before committing |
| **Gemini** | **Yes** | Yes (sandbox) | Clean, well documented | US-regulated (NYDFS); the strongest *US-usable* addition |
| **Binance.US** | **Yes** | Limited | Adequate | Separate venue from Binance.com; much smaller book |
| **Crypto.com** | **Yes** | Yes | Adequate | Consumer-facing; API less loved than the others |

Already implemented: Binance, Bitstamp, Coinbase, Kraken, Kraken Futures, MEXC.

**Zoomex is dropped**, and the maintainer's read is right: API keys are not
self-service, they are issued through an application with document upload and
Telegram approval. That is an exclusive-access programme, not a retail API. Not
worth building against until a key actually exists.

---

## 3. Recommended order

### First: **Bybit**

Not because it is the biggest, but because it is the most *verifiable*:

- **Self-service testnet** — a full plugin can be built and proven end to end with
  no funds, no approval queue, and no dependency on the maintainer's location
- Genuinely good documentation, and a v5 API unified across spot and derivatives,
  so one plugin covers both books
- Very large international user base — the single biggest reach-per-plugin on the
  list
- Its API shape is the one several smaller venues clone, so the work is partly
  reusable

### Second: **Gemini**

The strongest **US-usable** addition, and the one the maintainer can verify on a
real account with real (small) funds:

- US-regulated, so it stays available to US users
- Clean, well documented API with a sandbox
- Fills a real gap: of the US venues we support, Coinbase is our thinnest plugin
  and Kraken is currently blocked behind identity verification

### Third: **OKX**

Large, good API, demo trading available. Note the two-entity split — confirm which
one the plugin targets and make the base URL configurable per environment, exactly
as Kraken Futures does for its demo host.

### Then, if the appetite is there: **KuCoin**, **Bitget**, **Gate.io**

All large internationally and all buildable. Confirm sandbox availability first,
because without one they can only be verified by someone who holds an account.

### **LBank** — last, and only after checking

Smaller, and the API documentation is thinner than the others. Worth confirming
the API is actively maintained before spending a plugin's worth of effort on it.

---

## 4. What each new plugin must do

Not negotiable, because the audit will catch it and because these are the specific
mistakes already made here:

- **Declare only what it implements**, and everything it does — the capability
  audit checks both directions and has caught claims that were wrong each way
- **Add itself to `ProviderCapabilityHonestyTests.TradingProviders()`** — the
  roster-drift guard will fail the build otherwise, which is how Kraken Futures
  was caught
- **Distinguish a refusal from a failure.** Nearly every venue answers HTTP 200
  with an error body somewhere; a caller that reads only the status code sees
  success and empty data
- **Never claim `SupportsOrderEventStreaming`** without a real push channel, or
  fills silently never announce
- **Verify the signing algorithm against an independent implementation**, not
  against its own output
- **Make the demo/testnet host reachable via the Paper environment**, as Kraken
  Futures does — that is what makes the plugin verifiable at all

## 5. A note on effort

Each plugin is roughly the size of Kraken Futures: a day's careful work plus
verification. Eleven providers already carry a maintenance cost — every SDK
contract change is a pass over all of them. **Three well-verified venues are worth
more than eight unverified ones**, and the audit exists precisely so that number
stays honest.
