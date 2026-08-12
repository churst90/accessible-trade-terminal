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
- [x] Sdk, Core, Components, WebHost, StrategyLab and all 33 plugins build with **0 errors**
      (2 warnings in shipped code — see item 7, which corrects a claim this document made in an
      earlier draft)
- [x] **3,240 tests pass, 0 failed, 0 skipped** (~11s)
- [x] **Both MAUI heads build and produce artifacts — for the first time.** Verified by
      `v2.3.0-rc2`, after `v2.3.0-rc1` failed both. All 8 jobs green.
- [x] Doc-drift guard passes (it had been **red on main since 2026-08-06** — see below)
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

### 7. The "zero warnings" standard has not held for at least a release

An earlier draft of this document repeated 2.2.0's claim that the shipping projects build clean. **On
a clean rebuild they do not**, and the mistake is worth recording because of how it was made: the
check was run as an *incremental* build, where Razor components already up to date do not re-emit
their warnings. It looked like zero because nothing recompiled.

- **Shipped code: 2 warnings.** `PropertiesModal.razor:1154` and `:1156`, both `CS8602`
  (dereference of a possibly null reference) in `RenameDrawing`. **Pre-existing** — that file has not
  changed since `v2.2.0`, so the 2.2.0 claim did not hold either. Deliberately *not* fixed here: the
  obvious guard is an early return, which converts a crash into a drawing that silently fails to
  rename, and that is a behaviour decision about an accessibility path rather than a warning cleanup
  to slip into a release commit.
- **Test project: ~25 warnings.** Nullability (`CS8602` ×11, `CS8621` ×2, `CS8620`, `CS8634` ×2,
  `CS0649`) and analyzer style (`xUnit2013` ×7 — `Assert.Equal` for collection size, `xUnit2000` —
  swapped expected/actual).

None is release-blocking. It is recorded because "zero warnings" was a standard this project held and
has quietly stopped holding, and a standard that erodes silently is worth naming while it is still 27
and not 270. **Whoever fixes it should verify with `-t:Rebuild`**, or they will measure the same
nothing.

### 8. What the release-candidate tags actually caught

`v2.3.0-rc1` was pushed to validate the MAUI jobs, which had never produced an artifact. It was worth
it, and not for the reason `RELEASING.md` predicted.

| Failure | Real? | Cause |
|---|---|---|
| **maui-mac**, `RZ2005` / `RZ1011` ×4 | **Yes — would have failed the real tag** | `AssetDossierModal` named a loop variable `section`, so `@section.Title` parsed as Razor's `@section` **directive**. Only the mac job pins .NET 10 GA, whose parser is stricter; every other job's SDK accepted it |
| **maui-windows**, invalid `ApplicationDisplayVersion` | No — an artifact of the rc tag | Resizetizer demands a bare 3-part version and rejected `2.3.0-rc1`. The real `v2.3.0` would have passed — but `RELEASING.md` *tells* you to validate with a pre-release tag, so the documented validation path could not build the tag shape it documents |
| **The release was published as "Latest"** | Yes — outward-facing | The workflow marked the throwaway `v2.3.0-rc2` as a full release, making the validation build what every visitor to the releases page was offered. Demoted by hand; the workflow now sets `prerelease` automatically for any suffixed tag |

Both code fixes and both workflow fixes are in. `v2.3.0-rc2` ran **all 8 jobs green**.

**A correction to the 2.2.0 record while it matters:** that document recorded "MAUI head now compiled
by CI on every push to main, not only at tag time" as closing the compile-break mechanism. **It did
not.** `tests.yml` builds on a runner whose SDK tolerated the `@section` collision; only the mac
release job, on pinned GA, caught it. A head that CI compiles on one SDK is not a head that builds.

---

## The MAUI release jobs — now validated

They had never produced an artifact. As of `v2.3.0-rc2` they do: `AccessibleTrader-Windows-…zip`
(7m28s) and `AccessibleTrader-macOS-…universal.zip` (3m32s), alongside the four WebHost builds,
`SHA256SUMS.txt` and the plugin trust manifest.

**Building is not launching.** Both heads remain unsigned, and item 5 — that nobody has ever *run*
either of them — is untouched by this. What closed is the question of whether the jobs work at all.

---

## Tagging

The rc2 run is green, so:

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
