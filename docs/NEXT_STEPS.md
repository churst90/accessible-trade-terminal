# Next steps — trading surface, as of 2026-08-05

Written at the close of the session that completed the trading-surface scope.
Everything below is either **unfinished**, **unverified**, or **proposed**. Nothing
here is speculative about what was built — see `git log` and
`docs/PROVIDER_CAPABILITY_AUDIT.md` for that.

---

## 1. Finish withdrawals — DONE 2026-08-05, unverified by a person

Both named gaps are closed:

- **1a. The withdrawal dialog exists** (`WithdrawModal.razor`): asset →
  whitelist-only destination picker (no free-text address field anywhere; the
  address display is readonly with copy + character-by-character reading) → quote
  with fee and **what arrives**, spoken in full via
  `WithdrawalService.Confirmation(...)` → typed `WITHDRAW` field. Any edit voids
  the quote and the typed word, so what was read aloud is exactly what is sent.
  The toolbar button appears only when `CanWithdrawAsync` is true.
- **1b. The API Keys modal has the withdrawal checkbox**, as its own explained
  block. One withdrawal profile per provider is enforced in the form; the service
  refuses to ACTIVATE a withdrawal profile (activation is a trading concept), and
  provider configuration skips withdrawal profiles even if storage was hand-edited.

Pinned by `WithdrawalReachabilityTests`, `ApiKeyServiceTests`, and the toolbar
checklist. **Not yet driven by a person** — no live withdrawal-enabled key has
exercised the dialog end to end. That verification still needs Cody, a Kraken
account that clears identity review, and a whitelisted address.

---

## 2. ~~Verify Kraken Futures against the demo venue~~ — DEAD: the venue no longer exists

Kraken **decommissioned `demo-futures.kraken.com` on 2026-07-14** with no
announced replacement (found 2026-08-05 when the sign-up page 301'd to
marketing). Every path on the old host — the REST API included — now redirects to
a marketing page; `demo.kraken.com` exists but serves a block page and has no
documented self-service keys; Kraken's own developer docs still describe the dead
host. There is nothing to sign up for.

Consequences, all handled in code:

- The plugin now **refuses Paper-environment calls with the decommission message**
  (thrown before any request leaves) instead of surfacing an allow-list violation
  or an HTML-into-JSON parse error that would read as a signing bug.
- The signature stays verified only against the independent implementation of
  Kraken's documented example. A real-venue confirmation now requires a **funded
  non-US live account** — not available to this project's maintainer.
- The venue-signature-verification role this item played **transfers to Bybit's
  testnet** (self-service, US-accessible) — which was already first in
  `docs/PROVIDER_EXPANSION.md` for exactly this property.

## 3. Verify Kraken spot deposits once verification clears

Blocked on Kraken's identity review, not on us. When it clears:

```
StrategyLab wallet-probe --provider Kraken --asset BTC
```

Currently returns `EFunding:No funding method` on every asset while the credential
itself validates OK — which is the account, not the code.

Worth probing **XRP** as well as BTC: that exercises the destination-tag path, and
real data beats the defensive coding that currently reads the tag from whichever
of `tag` / `memo` / `destination_tag` is present.

---

## 4. Provider expansion — see `docs/PROVIDER_EXPANSION.md`

Requested: KuCoin, LBank, Bybit, plus suggestions. The order in that document
was revised 2026-08-05: **Bybit's API is geo-blocked from the US, testnet
included** (probed before building — the Kraken-demo lesson), so by the
document's own verifiability criterion **Gemini moved first — and shipped the
same day**: spot market data, order book, and trading (limit, stop-limit,
emulated market via IOC), sandbox routed through Environment = Paper, signature
pinned against independently computed vectors, public read path verified live
against the sandbox.

Remaining for Gemini: a person driving the sandbox end to end — create an
account at exchange.sandbox.gemini.com, generate a key, add it in API Keys as
Gemini with Environment = Paper, load a BTCUSD chart, place and cancel a limit
order, check balances. Then Bybit-blind or OKX per the doc.

---

## Standing verification debt (carried, not new)

- The **MAUI heads have never been launched**. Carried since 2.1.0 and accepted at
  2.2.0. Needs a human on Windows or macOS.
- The **dialog sweep on both heads** — same.
- **Quick trade has never been driven by a person.** An 11-step script is in
  `docs/RELEASE_2.2.0_VERIFICATION.md`. Deferred at the 2.2.0 tag, not dropped.
- **Live trading is unverified.** Paper is verified end to end; no live account has
  placed an order.
