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

## 3. Verify Kraken spot deposits — verification CLEARED, probed 2026-08-05

Kraken's identity review passed (no funds on the account — none are needed for
this). The BTC probe now gets much further, and the picture is specific:

- **Credential OK and the Funding scope works**: `DepositMethods` returns six
  BTC networks (Bitcoin, Lightning, four kBTC L2s). The old
  `EFunding:No funding method` is gone — it was the account, as recorded.
- **Bitcoin**: no address exists yet. The plugin deliberately reads existing
  addresses only (`new=false`, documented in `GetDepositAddressAsync`), so the
  FIRST address must be generated once on kraken.com → Funding → Deposit →
  Bitcoin. **That is the next human step**; then re-run the probe and the
  Deposit dialog shows it end to end.
- **Lightning and all four kBTC networks answer `EAPI:Invalid key`** even
  though Funding clearly works for the primary network — confirmed on the full
  6-network probe. Root cause still unproven; the working hypothesis is that
  Kraken returns this error for address types it will not issue over the API
  (on-demand invoices / unified L2 addresses). The plugin's error hint was
  FIXED the same day to name both readings instead of only "check the key's
  Funding permissions" (pinned in `KrakenWalletTests`). Do not debug the
  signature; it authenticates.
- Deposit history reads fine (0 deposits, correctly).
- The probe is SLOW by design — the client-side private rate limiter paces
  funding calls (~1/min) to avoid Kraken lockouts. A full 6-network probe takes
  several minutes; it is not hung.

Still worth probing **XRP** after BTC works: that exercises the destination-tag
path, and real data beats the defensive coding that currently reads the tag from
whichever of `tag` / `memo` / `destination_tag` is present.

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

**Gemini sandbox verification COMPLETE 2026-08-05, same evening.** Cody made a
sandbox account and key; the live signed path then caught two real bugs that no
offline test could see, both fixed and pinned the same hour:

- **The nonce must be SECONDS, not milliseconds** — Gemini's timestamp-nonce
  keys (the current default) reject anything outside ±30s of server time.
- **Sandbox keys are master-scoped** and require `account` in every payload;
  the plugin learns this from the venue's first `MissingAccounts` refusal and
  remembers, so both key kinds work.

Verified live end to end with paper funds: credential validation, balances
($100k USD / 1000 BTC preloaded), resting limit order placed → Working →
listed → cancelled → Cancelled, and an emulated market buy (IOC 1% through the
touch) that FILLED at the ask — the slippage bound worked and the venue
price-improved to the touch. Known sandbox quirk, documented in the plugin:
`/v1/mytrades` returns empty even after a confirmed fill; order/status reports
fills correctly and that is the path the poller uses. Fresh sandbox keys ship
as Auditor (read-only) — the Trader role must be assigned on the site.

Remaining for Gemini: drive it from the TERMINAL UI (chart, dashboard order,
cancel via the open-orders list) rather than the harness. Then Bybit-blind or
OKX per the doc.

---

## Standing verification debt (carried, not new)

- The **MAUI heads have never been launched**. Carried since 2.1.0 and accepted at
  2.2.0. Needs a human on Windows or macOS.
- The **dialog sweep on both heads** — same.
- **Quick trade has never been driven by a person.** An 11-step script is in
  `docs/RELEASE_2.2.0_VERIFICATION.md`. Deferred at the 2.2.0 tag, not dropped.
- **Live trading is unverified.** Paper is verified end to end; no live account has
  placed an order.
