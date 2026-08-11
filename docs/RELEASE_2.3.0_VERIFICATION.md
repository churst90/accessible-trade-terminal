# 2.3.0 — verification checklist

**Not yet tagged.** The live record of what has and has not been checked, kept in the same form as
`RELEASE_2.2.0_VERIFICATION.md` and `RELEASE_2.1.0_VERIFICATION.md` so the three can be compared.

2.2.0 ended with three items open at the moment of tagging, recorded rather than quietly dropped.
**All three are still open**, and they are listed again below rather than being allowed to age into
invisibility. That is now two releases running, and it is stated plainly here because a carried item
mentioned in ever smaller print eventually stops being a decision and becomes a habit.

---

## What this release is

2.2.0 was about what the chart *says*. 2.3.0 is about what the **account** can do: two new venues, a
wallet, a Balances tab that reports value rather than quantity, shorting restored with the collateral
that makes it honest, and a capability surface audited against reality instead of against its own
documentation.

Nineteen commits, 75 files, +8,725 / −230, 36 new files since `v2.2.0`.

The blast radius is different from 2.2.0's and so is the risk. 2.2.0 could mis-speak; 2.3.0 can
mis-state what an account is worth, or let a control claim a power the venue does not grant. The
mitigation was the same in both cases and it is the only one that has ever worked here: check it
against the real venue.

---

## Checked ✅

**Build and suite**
- [x] Sdk, Core, Components, WebHost, StrategyLab and all 33 plugins build **clean, zero warnings**
- [x] **3,240 tests pass, 0 failed, 0 skipped** (~11s)
- [x] Provider capability honesty tests green, including the roster-drift guard that catches a new
      trading provider not added to `ProviderCapabilityHonestyTests.TradingProviders()`
- [x] Edge registry validates

**Verified against a real venue — the only evidence that counts**
- [x] **Gemini, live against the sandbox (2026-08-05, by Cody).** Credential validation, balances
      ($100k / 1000 BTC paper), a resting limit order placed → Working → listed → cancelled →
      Cancelled, and an emulated market buy (IOC 1% through the touch) that **filled at the ask**
      with the slippage bound holding. The live path found **two bugs no offline test could see** —
      the nonce must be SECONDS, and sandbox keys are master-scoped and need `account` in every
      payload — both fixed and pinned the same hour. This is the release's strongest verification and
      it is exactly the kind that only a real key produces.
- [x] **Kraken funding scope, live (2026-08-05).** Identity review passed; `DepositMethods` returns
      six BTC networks. The old `EFunding:No funding method` was the account, as recorded — not the
      code.
- [x] **Gemini signature** pinned against vectors from an independent implementation (HMAC-SHA384
      over the base64 payload string).
- [x] **Kraken Futures signature** pinned the same way — see the caveat below, which is significant.

**Behaviour pinned by test, not by venue**
- [x] 1× collateralised shorting: locked proceeds, locked margin, enforced liquidation price
- [x] Capability gating of every order-ticket control
- [x] Withdrawal controls (separate credential, whitelist-only destinations, typed confirmation) —
      shipped **dark**, see below
- [x] Deposit-address checksum verification and case preservation

---

## Not checked ❌

### 1. Withdrawals — built, tested, and switched OFF for this release

**Decided by Cody, 2026-08-09.** The path is complete and pinned by `WithdrawalServiceTests`,
`WithdrawalReachabilityTests` and `ApiKeyServiceTests`, and **no human has ever run a real withdrawal
with it.** It is the only path in the terminal that moves money off a venue; everything else
unverified in this release costs a wasted click.

So it ships dark rather than reverted — reverting would throw away work that is probably right and
would have to be rebuilt from a diff, while a flag keeps the tests running against real markup every
build.

- The gate is `WithdrawalService.Released`, **false**.
- `CanWithdrawAsync` returns false regardless of provider or key, so the toolbar button never renders.
- `ReadyAsync` refuses before any request leaves the machine, so a missed UI surface cannot reach a
  venue.
- The API Keys withdrawal checkbox does not render, so no user can mint a profile that nothing uses.
- `WithdrawModal` is not instantiated.
- Pinned by `WithdrawalReleaseGateTests` (5 tests), one of which asserts the flag's literal value so
  that opening it fails a test pointing back at this document.

**To close for 2.3.1:** Cody, a Kraken account with a withdrawal-enabled key, and a whitelisted
address. Run one real withdrawal end to end, then flip the flag and update
`WithdrawalReleaseGateTests.The_gate_is_closed_by_default`.

### 2. Kraken Futures has never spoken to its venue

The demo host was **decommissioned 2026-07-14** and there is nothing to sign up for. The plugin
refuses Paper-environment calls with that fact rather than surfacing the dead host's 301 as a parse
error that reads like a signing bug.

The consequence has to be stated plainly: **this venue's signature is verified only against an
independent implementation of Kraken's documented example, never against Kraken.** That is a
genuinely weaker guarantee than Gemini's, and the plugin ships on it. A real-venue confirmation needs
a funded non-US live account, which this project's maintainer cannot get.

### 3. Kraken deposits stop one manual step short

The BTC probe gets through the credential, the Funding scope and `DepositMethods`, and then finds
**no Bitcoin address exists yet** — the plugin reads existing addresses only, by design (`new=false`).
The first address must be generated once on kraken.com → Funding → Deposit → Bitcoin. Then re-run
`StrategyLab wallet-probe --provider Kraken --asset BTC` and the Deposit dialog is verified end to
end. **This is a five-minute human step and it is the cheapest open item in the release.**

Also open: Lightning and all four kBTC networks answer `EAPI:Invalid key` even though Funding
demonstrably works for the primary network. Root cause unproven; the working hypothesis is that
Kraken returns this for address types it will not issue over the API. The error hint names both
readings rather than sending the user to remake a key that was fine. **Do not debug the signature —
it authenticates.**

XRP is worth probing after BTC, because it exercises the destination-tag path that is currently
served by defensive coding rather than by evidence.

### 4. Shorting has not been driven by a person

Restored at 1x with collateral and liquidation, and pinned by tests including hand-worked collateral
arithmetic. **No record exists of a human opening a short in the paper account, watching the locked
balances move, and taking it to liquidation.** Liquidation is a number that decides money, and 2.2.0's
own lesson was that nine paper-trading defects were found by using the terminal and none by the suite.
This should be walked through before or immediately after the tag.

### 5. Carried from 2.2.0 and 2.1.0 — now two releases old

- [ ] **The MAUI heads have never been launched.** CI *builds* both on every push, which closes the
      compile-break mechanism but not the launch question. Needs a human on Windows or macOS.
- [ ] **The dialog sweep on both heads.** Same.
- [ ] **Quick trade has never been driven by a person.** The 11-step script in
      `RELEASE_2.2.0_VERIFICATION.md` still stands and should be run against the tagged build.

### 6. Live trading remains unverified

Paper is verified end to end. **No live account has placed an order through this terminal.** True at
2.1.0, true at 2.2.0, true here.

### 7. Test-project warnings have drifted

2.2.0 was tagged at **zero warnings everywhere**. The shipping projects still are. The **test project
now carries ~25** — nullability (`CS8602` ×11, `CS8621` ×2, `CS8620`, `CS8634` ×2, `CS0649`) and
analyzer style (`xUnit2013` ×7 — `Assert.Equal` for collection size, `xUnit2000` — swapped
expected/actual).

None affects shipped code and none is release-blocking. It is recorded because "zero warnings" was a
standard this project held and has quietly stopped holding, and a standard that erodes silently is
worth naming while it is still 25 and not 250.

---

## The MAUI release jobs have never run

`RELEASING.md` warns that the two MAUI jobs depend on the `maui` workload and on publish-output paths
that shift between SDK versions, and recommends validating with a throwaway pre-release tag. **Those
jobs have never produced an artifact**, and the release workflow will attach unsigned MAUI heads to
2.3.0 regardless of whether anyone can launch them.

Validate first:

```bash
git tag v2.3.0-rc1
git push origin v2.3.0-rc1
gh run watch
```

If a MAUI job fails at "Locate + zip", the publish succeeded and the artifact glob needs adjusting.

---

## Tagging

Once the pre-release run is green:

```bash
git tag v2.3.0
git push origin main
git push origin v2.3.0
```

`.github/workflows/release.yml` produces four self-contained WebHost builds (linux-x64, win-x64,
osx-x64, osx-arm64), the two unsigned MAUI heads, and `SHA256SUMS.txt`.

---

## For 2.3.1

In order:

1. **One real withdrawal**, then flip `WithdrawalService.Released`. This is the whole of item 1 and it
   is the only thing standing between the code and the users.
2. **Generate the first Kraken BTC deposit address** and re-run the wallet probe (item 3). Five
   minutes.
3. **Walk a short through the paper account** to liquidation (item 4).
4. The quick-trade walkthrough, the MAUI launch, and the dialog sweep (item 5) — three releases old
   if they slip again.
