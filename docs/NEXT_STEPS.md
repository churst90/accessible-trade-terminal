# Next steps — trading surface, as of 2026-08-05

Written at the close of the session that completed the trading-surface scope.
Everything below is either **unfinished**, **unverified**, or **proposed**. Nothing
here is speculative about what was built — see `git log` and
`docs/PROVIDER_CAPABILITY_AUDIT.md` for that.

---

## 1. Finish withdrawals — the two named gaps

Withdrawals shipped as **service and provider only**. They are not reachable by a
user, and that is stated plainly rather than allowed to pass as done.

### 1a. No UI

There is no withdrawal dialog. It needs:

- an asset picker, then a **destination picker populated from the venue's
  whitelist** (never a free-text address field — the SDK has no parameter for one
  and it must stay that way)
- the quote read back before confirmation: amount, fee, and **what arrives**
- a text field for the typed `WITHDRAW` confirmation, with the same read-only /
  arrow-navigable treatment the deposit address gets
- an unmistakable spoken readback; this is the one action in the terminal that
  cannot be undone

`WithdrawalService.Confirmation(...)` already produces the sentence to speak.

### 1b. No way to create a withdrawal profile

`ApiKeyConfig.AllowsWithdrawal` exists, is persisted, and is enforced on both
sides — but the API Keys modal has **no checkbox for it**, so the flag can only be
set by editing storage. Until that checkbox exists, `CanWithdrawAsync` returns
false for everyone and the whole feature is unreachable.

When adding it: it should read as a deliberate, separate act — its own row, its
own explanation of why a trading key must not carry this, and it should NOT be
offered on the same form flow as an ordinary trading key without comment.

---

## 2. Verify Kraken Futures against the demo venue

The highest-value verification available right now, because it needs **no account
verification at all**.

`demo-futures.kraken.com` is a real environment with its own self-service keys and
paper funds. Steps:

1. Generate a key at demo-futures.kraken.com
2. Add it in API Keys with **Environment = Paper** (that is what routes the plugin
   to the demo host)
3. Load a `PI_XBTUSD` chart, confirm candles arrive
4. Check balances, place and cancel a limit order, confirm it appears and clears

What this proves that no test can: that the **signature is right against a live
venue**. The algorithm is verified against an independent implementation of
Kraken's documented example, which is strong — but only a real 200 response
settles it.

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

Requested: KuCoin, LBank, Bybit, plus suggestions. Analysis and a recommended
order are in that document. Short version: **Bybit first**, for reasons that are
about verifiability rather than popularity.

---

## Standing verification debt (carried, not new)

- The **MAUI heads have never been launched**. Carried since 2.1.0 and accepted at
  2.2.0. Needs a human on Windows or macOS.
- The **dialog sweep on both heads** — same.
- **Quick trade has never been driven by a person.** An 11-step script is in
  `docs/RELEASE_2.2.0_VERIFICATION.md`. Deferred at the 2.2.0 tag, not dropped.
- **Live trading is unverified.** Paper is verified end to end; no live account has
  placed an order.
