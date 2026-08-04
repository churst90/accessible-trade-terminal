# Wallet, portfolio and transfers — design

Written 2026-08-04, replacing the thinking-out-loud in `TRADING_SURFACE_SCOPE.md` §5.5 with
something buildable. **Design only. Nothing here is built yet.** Targeted at 2.3.

Three features got conflated under "wallet". They have different costs, different risk profiles,
and only one of them is cheap:

| # | Feature | New API surface | Trust cost | Size |
|---|---|---|---|---|
| 1 | Portfolio valuation | none | none | small |
| 2 | Deposit addresses + history | read-only wallet endpoints | moderate | medium |
| 3 | Withdrawals | write endpoint that moves money | **high** | medium |

They should ship in that order, and 3 should ship well after 2 has been used in anger.

---

## 0. The rule for which venues get it

**Every provider whose API supports it.** Not a curated shortlist.

An earlier draft proposed starting with Coinbase and Kraken because those are the maintainer's own
accounts. That is the right place to *verify*, and the wrong place to *stop* — a capability that
exists on a venue and not in the terminal is precisely the gap this whole line of work is about, and
picking favourites bakes it in. The interface is defined once; each plugin implements it if the
venue has the endpoints and does not if it does not.

Expected coverage on the current plugin set — **to be confirmed against each venue's current API
docs before implementing**, not from memory:

| Provider | Deposit addresses | Deposit history | Withdrawals |
|---|---|---|---|
| Coinbase | expected | expected | expected, permission-scoped |
| Kraken | expected | expected | expected, address must be pre-whitelisted |
| Binance | expected | expected | expected |
| Mexc | expected | expected | expected |
| Bitstamp | expected | expected | expected |
| Alpaca | crypto only, if at all | — | — |
| Schwab / Tradier / IBKR / Oanda | not applicable — not crypto custodians | | |

The "not applicable" row is the point of the interface split below: those brokers should not
implement `IWalletProvider` at all, and the Wallet UI should then be *absent* rather than present
and empty.

---

## 1. Portfolio valuation

### What is missing

The Balances tab renders `Asset / Free / Locked` and nothing else. There is no value column, no
total, no allocation, no day change. That is the gap; the table itself is fine.

As of 2026-08-04 the paper broker also reports held assets as balances, not just quote cash, so the
tab finally has something to value.

### The constraint worth knowing first

**`IMarketDataProvider` has no "current price" call.** It has `FetchOhlcvAsync` and
`GetOrderBookAsync`. So valuation fetches the most recent daily bar per asset — which means **day
change comes almost free**, since the same bar carries the open. Include it; do not defer it.

### Shape

```csharp
public record AssetValuation(
    string  Asset,
    double  Quantity,
    double? Price,          // null when the asset could not be priced
    double? Value,
    double? DayChangePct,
    string? Unpriced);      // why, in words, when Price is null

public record PortfolioSnapshot(
    IReadOnlyList<AssetValuation> Assets,
    double TotalValue,
    string QuoteAsset,
    int    PricedCount,
    int    TotalCount);
```

### Rules

- **An unpriceable asset says so and is never counted as zero.** A total that quietly omits an asset
  is worse than no total, and this is the failure mode the provider audit called dominant: silent
  read-path failures. The summary reads *"$48,210 across 7 of 9 assets; DUST and FOO could not be
  priced."*
- **On equity brokers, prefer the broker's own account equity** over summing our own valuations.
  Their number is authoritative; ours is an estimate. Never mix the two in one figure — label which
  one is on screen.
- **Symbol convention is per venue.** `BTC` prices as `BTC/USD` on Coinbase, `XBTUSD` on Kraken,
  `BTCUSDT` on Binance. Resolution belongs in the provider, not in the valuation service.
- Spoken summary on tab focus: total, day change, largest holding and its share.

### Known limitation, deliberately carried

The paper broker settles in a single hardcoded quote (`USDT`), so an equity paper trade shows USDT
cash. Multi-currency cash is part of the 2.3 margin-accounting rewrite, where the settlement model is
being rebuilt anyway; splitting it out earlier would mean doing it twice.

---

## 2. Deposit addresses

### Interface: a separate interface, not a flag

```csharp
public interface IWalletProvider : IProviderPlugin
{
    Task<IReadOnlyList<string>>         GetNetworksAsync(string asset);
    Task<DepositAddress>                GetDepositAddressAsync(string asset, string network);
    Task<IReadOnlyList<DepositRecord>>  GetDepositsAsync(string? asset = null, int limit = 50);
}
```

**Why an interface rather than a `ProviderCapabilities` flag.** Capability flags have now been wrong
in both directions in this codebase within one week — declared and unimplemented (paper's `Leverage`,
`Shorting`), and implemented and undeclared (paper's `TrailingStop`, which hid working UI). A flag is
a claim a developer writes by hand. `provider is IWalletProvider` is a fact the compiler enforces.
For a feature whose failure mode is *showing someone the wrong address to send money to*, use the
one that cannot be mistyped.

The Wallet button appears only when the connected provider implements the interface.

### The record

```csharp
public record DepositAddress(
    string  Asset,
    string  Network,        // "Bitcoin", "ERC20", "Solana" — the single biggest way to lose a deposit
    string  Address,
    string? Memo,           // destination tag / memo — omit it on XRP or XLM and the funds are gone
    string? MemoLabel,      // venues call it "memo", "tag", "note" — use the venue's own word
    double? MinimumDeposit,
    int?    Confirmations);
```

**Network is chosen before the address is shown, and spoken first.** Sending USDC to an Ethereum
address on the Solana network destroys it, and that is a more common loss than address substitution.

**A non-null `Memo` gets equal billing with the address**, never a footnote. Missing memos are the
other classic way a deposit disappears.

### Presentation — decided with the maintainer

Chunked speech was proposed and **rejected**, correctly:

> *"crypto addresses are long and hard to understand so breaking them up wouldn't be helpful.
> They'll be written down so the person can just read them manually to verify."*

So:

- **The full address, unbroken, as one selectable string.** No truncation, no visual grouping, no
  invented chunking scheme.
- **Focusable and arrow-navigable**, so the screen reader's own review cursor and the braille display
  walk it character by character. Braille is the best verification channel available here and it
  carries case natively — which matters, because **most address formats are case-sensitive** and a
  plain speech read drops case entirely.
- **One explicit "read character by character" button** that announces capitals ("capital A") for
  anyone without a review cursor. `b` and `B` are different addresses.
- **Copy is offered.** The defence against address substitution is not withholding the clipboard; it
  is the two rules below.

### Security rules that gate the whole feature

1. **Never cached, never persisted, refetched on every open.** Substitution needs a stored copy to
   substitute; there is not one. The address lives in memory for the life of the dialog and is
   cleared on close.
2. **Validated locally before display.** Check the format against the selected network — length,
   prefix, and the checksum where the format has one (bech32 and base58check both carry one; EIP-55
   encodes it in the letter case). A malformed address from the API is the alarm, and this check
   costs nothing and runs offline.
3. Read only from the authenticated API. Never from a config file, never from a cache, never from a
   value the user can edit.

---

## 3. Withdrawals

In scope, per the maintainer: *"withdraws are a provider supported feature and if it is available
through the api then it needs to be implemented."* Agreed — with two conditions that make it
defensible rather than reckless, and after §2 has seen real use.

```csharp
Task<WithdrawalResult> WithdrawAsync(
    string asset, string network, string address, double amount, string? memo);
```

### Condition 1 — a separate credential

Withdrawal permission is the most dangerous scope an API key can carry. A trading key that can also
move funds means one compromise empties the account.

**Withdrawals require a distinct, withdrawal-enabled key, stored separately, absent by default.** The
terminal's ordinary operating credentials must not be able to move money. This is a real security
boundary, it costs the user one extra setup step, and it is the difference between "my key leaked"
and "my funds are gone".

### Condition 2 — the venue keeps the trust anchor

Most venues require withdrawal addresses to be pre-approved in their own UI, frequently with 2FA.
That is a gift, not an obstacle: **do not build our own address book to route around it.** The
exchange holds the whitelist; we are the submit button.

### The confirmation path

- Full spoken readback: asset, network, amount, fee, **net amount received**, destination address read
  character by character with case.
- A typed confirmation, not a button press.
- Never a hotkey. Never a quick action. Never reachable from the chart.
- The transfer happens rarely; the friction is affordable and the failure is not.

---

## 4. Order of work

1. Portfolio valuation on the Balances tab (small, no new API surface, immediately useful)
2. `IWalletProvider` + `GetNetworksAsync` / `GetDepositAddressAsync`, implemented on every crypto
   plugin whose API supports it
3. Local address validation + the accessible address presentation
4. `GetDepositsAsync` — the "did my $5 arrive?" loop
5. Withdrawals, behind the separate credential

## Cross-references

`docs/TRADING_SURFACE_SCOPE.md` §5.5 · `AccessibleTrader.Sdk/Plugins/ITradingProvider.cs` ·
`AccessibleTrader.BlazorClient.Components/TradingDashboardModal.razor` (Balances tab) ·
`docs/PROVIDER_AUTHORING.md`
