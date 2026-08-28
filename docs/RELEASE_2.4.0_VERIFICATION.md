# 2.4.0 — verification checklist

**Written 2026-08-28, at the moment of tagging.** Kept in the same form as
`RELEASE_2.3.0_VERIFICATION.md`, `RELEASE_2.2.0_VERIFICATION.md` and
`RELEASE_2.1.0_VERIFICATION.md` so the four can be compared. The release is also recorded in
[CHANGES.md](CHANGES.md) and described for users in [WHATSNEW.md](WHATSNEW.md).

**107 commits, 1,160 files, +95,349 / −10,600, 269 new files since `v2.3.0`** (tagged 2026-08-11).

An earlier draft of the README claimed 190 commits over the same span. It was wrong;
`git rev-list --count v2.3.0..HEAD` says 107, and the figure is corrected there as well as here.

---

## What this release is

**Nothing was added.** That is not modesty, it is the entire description: 107 commits and not one
feature. 2.1.0 through 2.3.0 each grew the terminal. This one went to finding out what was actually
wrong and then fixing it, in that order — four commissioned audits, two structural refactors, then
every CRITICAL and every HIGH item closed and about seventy MEDIUM ones.

**The case for tagging it is that its contents are already live in the users' hands as defects.**
Everything fixed here is something a person running 2.3.0 is silently exposed to right now: an API
key containing `&` truncated at the ampersand and then read aloud in the failure message; Kraken's
History tab blank on its busiest pair; a support break announced as resistance; whole-chart playback
clipping at 5.5× full scale since the day it was written; eight ways sound leaked past a mute; a
password reset that did not evict the session it was resetting. `Directory.Build.props` has said
`2.3.0` since 2026-08-12, so the deploy log cannot tell nine builds apart — which is its own reason
to cut a version.

The blast radius is different from 2.3.0's. 2.3.0 could mis-state what an account is worth. This one
mostly *stops* the terminal mis-stating things — but a fixing release carries its own risk, which is
that a fix is wrong in a way nobody looks for because everyone assumes the direction of travel is
toward correctness. The mitigation used throughout, and the standing rule this repo now runs on:
**demonstrate the defect, or mark it explicitly unverified.** Every fix in this release was proved by
reintroducing the bug and watching a named test go red.

---

## Checked ✅

**Build and suite**
- [x] **5,754 tests pass, 0 failed, 0 skipped** in both Debug and Release (Release run: 2m20s).
      `--list-tests` reports **5,749**, which is the number `docs/README.md` carries and
      `doc-drift.yml` checks.
- [x] **`AccessibleTrader.BrowserTests`: 129/129 green** against a real Chromium driving a real
      Kestrel running the real WebHost.
- [x] Doc-drift guard passes, including the fourth check added 2026-08-28 over the user manual and
      QUICKSTART (it previously validated only `SHORTCUTS.md` — the reference doc nobody reads end
      to end — while both docs a user is actually pointed at had drifted).
- [x] Sdk, Core, Components, WebHost, StrategyLab and all 33 plugins build with **0 errors**.

**Proved by sabotage, which is the only evidence this project counts**
- [x] The 2026-08-27 provider batch: **32 mutations, 32 reds.** The batch before it: 9 and 9. The
      hosted-WebHost security batch: 16 and 16. Per-fix sabotage evidence exists for essentially
      everything in this release, and for the question "are these fixes real" that is stronger
      evidence than any aggregate catch rate.
- [x] The two items closed immediately before this tag were each proved the same way. The port-bind
      fix was verified by **occupying 5145 and running the whole browser suite** — 129/129 green,
      where the same run against the old factory died at host build with
      `Failed to bind to address http://127.0.0.1:5145: address already in use`. The
      `LevelPolarity` scan guard was proved red **three** ways: name matching restored, frequency
      restored, and the chokepoint bypassed by an inline comparison that was still *correct*.
- [x] The four commissioned audits all ran, and all four were run by breaking things rather than by
      reading. The sandbox audit compiled 25 candidate escapes and four worked. The test-suite audit
      introduced 28 single-line regressions one at a time.

**Verified on the live box**
- [x] The deployed lineage has passed the full acceptance list on the machine that serves the demo,
      including the browser harness. **As of this release that harness can be run there at all** —
      see item 4 below for what that was worth.

---

## Not checked ❌

### 1. The mutation catch rate is stale, and it is the number the grade turns on

**61% measured 2026-08-26 against a 4,830-test suite.** The suite is now 5,754 — roughly 900 tests
have landed since — and nothing has re-measured it. This is the weakest evidence in the whole
picture and it is being carried across the tag deliberately, with the reasoning recorded rather than
buried:

- It does not change what ships. Re-measuring is a session of its own — 28 mutation cycles, each a
  rebuild plus a full run — and if it finds survivors the honest response is to fix them, which
  attaches an open-ended delay to a release that is already justified on its own evidence.
- Per-fix sabotage evidence already exists for everything in this release (32 mutations / 32 reds in
  the last batch alone). Aggregate catch rate answers "how good is this suite in general"; per-fix
  sabotage answers "is this fix real", and the second is the question a release asks.

**Carry A2's own trap into the re-run:** record failing test *names*, and re-run any single-test
catch in isolation. Five mutants came back falsely "caught" by one unrelated flaky test firing
alone, which is the entire difference between the naive 79% and the true 61%.

### 2. Withdrawals — still built, still switched OFF

Unchanged from 2.3.0, and re-stated rather than allowed to age into invisibility. The gate is
`WithdrawalService.Released`, **false**; `CanWithdrawAsync` returns false regardless of provider or
key, so the toolbar button never renders; `ReadyAsync` refuses before any request leaves the
machine; the API Keys withdrawal checkbox does not render; `WithdrawModal` is never instantiated.
Pinned by `WithdrawalReleaseGateTests`, one of which asserts the flag's literal value so that
opening it fails a test pointing back at this document.

**To close:** Cody, a Kraken account with a withdrawal-enabled key, and a whitelisted address. One
real withdrawal end to end, then flip the flag and update
`WithdrawalReleaseGateTests.The_gate_is_closed_by_default`. **This is now two releases old.**

### 3. The StrategyLab's flagship statistic is provisional

p = 0.0045 tested the **winner of a 16-cell grid**, and the survivorship stress beside it could not
fail by construction. Both are fixed and banner-marked provisional in the app; the re-run has not
happened and remains the top research follow-up.

### 4. What the browser harness could not prove until the day of this tag

Worth stating because it changes how much the "verified on the live box" line above is worth for
every *previous* release. `AccessibleTrader.BrowserTests` asked Kestrel for port 0, but a `Listen`
call does not replace a configured endpoint — it adds to one — and the factory still read the
WebHost's `appsettings.json`, whose Http endpoint is `http://localhost:5145`. So the harness took
both ports. On CI nothing owns 5145 and the extra bind succeeded in silence; **on the box that
serves the demo on exactly that port, every case died.** The suite is the only check that proves a
deployed commit renders, and until this release it could not be run on the machine doing the
deploying. Fixed and guarded on the bound-address set — the obvious guard, that `RootUrl` is
non-empty, passed against the defect.

### 5. Carried from 2.3.0, 2.2.0 and 2.1.0 — now three releases old

- [ ] **The MAUI heads have never been launched.** CI *builds* both on every push, which closes the
      compile-break mechanism but not the launch question. Needs a human on Windows or macOS.
- [ ] **The dialog sweep on both heads.** Same.
- [ ] **Quick trade has never been driven by a person.** The 11-step script in
      `RELEASE_2.2.0_VERIFICATION.md` still stands.
- [ ] **Shorting has not been driven by a person** to liquidation in the paper account.
- [ ] **Live trading remains unverified.** No live account has placed an order through this
      terminal. True at 2.1.0, 2.2.0, 2.3.0 and here.
- [ ] **Kraken deposits stop one manual step short** — the first BTC address must be generated once
      on kraken.com, then `StrategyLab wallet-probe --provider Kraken --asset BTC` re-run. Still the
      cheapest open item in the project.
- [ ] **Kraken Futures has never spoken to its venue.** Its signature is verified only against an
      independent implementation of Kraken's documented example.

### 6. The release workflow's fragile half has not run since 2026-08-11

`release.yml`'s two MAUI jobs depend on the `maui` workload and on publish-output paths that shift
between SDK versions; `RELEASING.md` names them as the fragile part and they last ran for
`v2.3.0-rc2`. **Validate with a throwaway pre-release tag before the real one** — that is what
`RELEASING.md` prescribes and it is what caught two real problems last time (Resizetizer rejecting a
suffixed `ApplicationDisplayVersion`, and the workflow publishing a throwaway rc as "Latest").

---

## Tagging

```bash
git push origin main
git tag v2.4.0-rc1 && git push origin v2.4.0-rc1    # throwaway: shakes out the two MAUI jobs
gh run watch
git tag v2.4.0     && git push origin v2.4.0        # the real one, once rc1 is green
```

`.github/workflows/release.yml` produces four self-contained WebHost builds (linux-x64, win-x64,
osx-x64, osx-arm64), the two unsigned MAUI heads, `SHA256SUMS.txt` and the plugin trust manifest. It
sets `prerelease` automatically for any suffixed tag, so the rc cannot be published as "Latest" the
way `v2.3.0-rc2` was.

**The About dialog needs nothing done to it.** `Directory.Build.props` is the single source of the
version, and its `StampCommitId` target appends the short commit sha as SemVer build metadata, so
Settings → About reads `2.4.0+<sha>` off the assembly's informational version at runtime and
announces the number and the build as two separately-labelled fields. Bumping `<Version>` is the
whole of the change.

---

## For 2.4.1

In order:

1. **Re-measure the mutation catch rate** (item 1). It is a session of its own and it is what the
   production-readiness grade turns on.
2. **One real withdrawal**, then flip `WithdrawalService.Released` (item 2). Two releases old.
3. **Generate the first Kraken BTC deposit address** and re-run the wallet probe (item 5). Five
   minutes.
4. **Re-run the StrategyLab flagship** with the grid winner problem controlled for (item 3).
5. The quick-trade walkthrough, the MAUI launch, the dialog sweep, and a short walked to liquidation
   (item 5) — four releases old if they slip again.
