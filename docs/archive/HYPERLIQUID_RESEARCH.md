# Hyperliquid for the Accessible Trade Terminal

> Saved from the Claude Doc https://claude.ai/artifact/ArWAdoRPAWy62CMESyVmgD on 2026-10-25. Research only: nothing here is built. Figures are as of 2026-10-25. The doc's phased-plan diagram is not in this copy; its text version is under *Suggested plan*.

2026-10-25 · Cody Hurst

## Summary

**Yes, Hyperliquid supports leverage**: up to 40x on BTC, 25x on ETH, 20x on SOL and XRP, and 3x to 10x on most of its 178 crypto perpetuals. Those figures come from its live API today. Its stock, commodity and FX markets (HIP-3) go up to 20x on stocks and 50x on EUR and JPY.

**Technically, live trading from the terminal is very feasible.** The API looks like a normal exchange's: candles, an order book, websocket feeds, limit, stop and trailing orders. The terminal can trade through an *API wallet* (agent key). That key can place and cancel orders but cannot withdraw funds or send them to another address.

**The blocker is legal, not technical.** Hyperliquid's terms list anyone resident or located in the United States, or in Ontario, as a "Restricted Person". That is enforced at its website, not in the protocol. So nothing stops the API from working from the US, but using it from there breaks the terms, and its CEO has said so publicly.

**A legal US route is coming but is not live.** On 2026-09-16, Payward (Kraken's parent) announced US access to Hyperliquid HIP-3 markets through its CFTC-regulated Bitnomial exchange. It still needs CFTC approval, and no date was given. Kraken is already a provider in the terminal.

**Recommendation:** build read-only market data and testnet trading now. Turn on mainnet live trading only if you are not a US person, or once the Payward/Bitnomial route opens.

## What Hyperliquid is

Hyperliquid is its own blockchain, built to be an exchange. Its order book and margin engine run on the chain itself, not on an off-chain server ([overview](https://hyperliquid.gitbook.io/hyperliquid-docs/hypercore/overview)).

- **Two halves.** *HyperCore* holds the order books, margin and matching. *HyperEVM* is an Ethereum-style smart-contract chain beside it. The terminal would only need HyperCore.
- **Speed.** The docs quote a median of 0.2 seconds from sending an order to its confirmation, 0.9 seconds at the 99th percentile, for a client near the servers. That is CEX-like.
- **Markets today (live API).** 178 crypto perpetuals, 347 spot pairs, plus 10 builder-deployed exchanges (HIP-3). The largest, `xyz`, lists 116 markets: stocks such as NVDA, TSLA and AAPL, gold, silver, oil, EUR and JPY. BTC traded about $1.08 billion in the last 24 hours.
- **Collateral.** Perps are margined in USDC, which is now issued natively on Hyperliquid by Circle. The legacy Arbitrum bridge holds under 10% of it ([USDC](https://hyperliquid.gitbook.io/hyperliquid-docs/hypercore/usdc)). Withdrawals cost $1 and take about 5 minutes.
- **Fees.** Base perps fees are 0.045% taker and 0.015% maker; spot is 0.070% and 0.040%. Fees fall with 14-day volume and with HYPE staked ([fees](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/fees)).
- **No KYC.** You trade from a wallet address. There is no account sign-up.
- **HLP vault.** A community vault that market-makes and takes over liquidated positions. Profits and losses go to its depositors. Its TVL was about $268.6 million in June 2026 ([CoinGecko](https://www.coingecko.com/learn/hyperliquid-hlp-vault-analysis)).
- **Centralisation.** 35 validators as of 2026-09-25. Five Hyper Foundation validators hold about 47% of the stake, and the node software ships as closed binaries ([Cipher Intelligence](https://cipher-intelligence.io/exchanges/hyperliquid/)).

### Track record

| Date | Event | What happened |
| --- | --- | --- |
| 2025-11-12 | POPCAT manipulation | A roughly $26-30M long behind a fake buy wall; HLP took about $4.9M of bad debt |
| 2025-10-10 | Market-wide crash | Over $10B liquidated on Hyperliquid; first cross-margin auto-deleveraging in two years closed winning traders' positions |
| 2025-10-10 | Key theft | About $21M drained from one trader whose private key leaked; not a protocol breach |
| 2025-07-29 | API outage | 37 minutes of delayed orders and errors; about $1.99M refunded |
| 2025-03-26 | JELLY manipulation | HLP's unrealised loss reached about $12-13.5M; validators delisted the coin and settled it at their own price |
| 2024-12-23 | Lazarus scare | North Korea-linked addresses traded there; no exploit was found |

No 2026 outage or exploit is listed in the sources I found. The JELLY vote is the standing criticism: when it mattered, a small validator set overrode the market price.

## Leverage and margin

Leverage is a whole number you pick per asset, from 1x up to that asset's maximum ([margining](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/margining)). Maintenance margin is half the initial margin at the maximum, so higher-leverage assets also liquidate closer to the entry price ([liquidations](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/liquidations)).

| Max leverage | Maintenance margin | Crypto perps | Examples |
| --- | --- | --- | --- |
| 40x | 1.25% | 1 | BTC (20x above $150M of position) |
| 25x | 2% | 1 | ETH (15x above $100M) |
| 20x | 2.5% | 2 | SOL, XRP |
| 10x | 5% | 30 | AVAX, BNB, LTC, DOGE, SUI, LINK, HYPE |
| 5x | 10% | 56 | ATOM, OP, INJ, LDO, DYDX |
| 3x | 16.7% | 88 | GMX, SNX, KAS, BLUR |

Counts are from the live `meta` API call on 2026-10-25. On the `xyz` HIP-3 exchange, US megacap stocks go to 20x, gold and silver to 25x, and EUR and JPY to 50x.

**A worked example** (fees and funding left out). With $1,000 at 10x on BTC, the position is $10,000 and maintenance is $125. A move of about 8.75% against you liquidates it. At 40x the position is $40,000, maintenance is $500, and a 1.25% move liquidates it.

### Margin modes

- **Cross** (the default): all cross positions share one pool of collateral. One bad position can liquidate the whole account.
- **Isolated:** each position has its own margin. A liquidation takes only that position and its margin.
- **Strict isolated:** isolated, but you cannot take margin back out while the position is open.

### Liquidation

- Liquidation uses the **mark price**, a blend of outside CEX prices and Hyperliquid's own book. It is not the last trade price.
- First the position is market-sold on the book. If equity falls below two-thirds of maintenance, the liquidator vault takes the position, and **you lose the maintenance margin** too.
- As a last resort, **auto-deleveraging** closes profitable traders on the other side so the exchange never carries bad debt. This happened on 2025-10-10 ([auto-deleveraging](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/auto-deleveraging)).

### Funding

Perps never expire. Funding is paid **every hour** between longs and shorts to keep the perp near spot ([contract specs](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/contract-specifications)).

### Order types

All of these are in the API ([order types](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/order-types)):

- Market, limit (GTC, post-only, IOC), reduce-only
- Stop market, stop limit, take-profit market and limit, with TP/SL attachable to a position
- Trailing stop on perps
- TWAP (slices at least 30 seconds apart, 3% max slippage per slice) and scale orders

Stops and TPs **rest on the exchange**. They still fire if the terminal is closed, unlike stops on swap services.

## The API

The API has two HTTP endpoints and a websocket. Every market-data call needs no key, and I ran several of them for this document.

- **`/info` (read, no signature).** Markets and max leverage (`meta`, `spotMeta`), prices and funding (`metaAndAssetCtxs`), order book (`l2Book`), candles (`candleSnapshot`), and an address's positions, orders and fills (`clearinghouseState`, `openOrders`, `userFills`).
- **`/exchange` (write, signed).** Order, cancel, modify, batch modify, TWAP, trailing stop, update leverage, update isolated margin, transfers, withdraw, approve an API wallet ([exchange endpoint](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/exchange-endpoint)).
- **Websocket.** Live trades, book, candles, mid prices, and per-user order, fill and position updates. You can also send signed actions over it.
- **Testnet.** A full copy at `api.hyperliquid-testnet.xyz` with mock USDC. The faucet only pays addresses that have **deposited on mainnet** ([faucet](https://hyperliquid.gitbook.io/hyperliquid-docs/onboarding/testnet-faucet)).

### Candle history (measured 2026-10-25)

| Interval | Candles returned | Earliest |
| --- | --- | --- |
| 1 day | 2,259 | 2020-08-19 |
| 1 hour | 5,003 | 2026-03-31 |
| 1 minute | 5,200 | 2026-10-22 |

Intraday history is capped at about the last 5,000 candles. Hyperliquid has no candle archive ([historical data](https://hyperliquid.gitbook.io/hyperliquid-docs/historical-data)). The daily series starts before Hyperliquid existed, so its early years must come from another source; I haven't checked where. For deep intraday charts and StrategyLab, the terminal would keep using Binance or Coinbase data, or record Hyperliquid candles itself.

### Signing, and the API wallet

There is no API key. Each write is signed with an Ethereum-style private key (secp256k1, EIP-712).

- **Trading actions** (orders, cancels, leverage) are hashed and signed under a fixed "Exchange" domain.
- **Money-moving actions** (withdraw, send USDC, approve an API wallet) are signed differently, and only by the **main wallet**.
- An **API wallet** ("agent") is a fresh key the main wallet approves once with `approveAgent`. It can trade and cancel on the account. It cannot withdraw. Its one transfer action, `agentSendAsset`, must send to the same address, so it can only move money between your own spot and perp balances ([API wallets](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/nonces-and-api-wallets)).
- Approving a new unnamed agent revokes the old one. Named agents can sit side by side, one per process. The docs warn against reusing a revoked agent's address, because its old signed actions could be replayed.
- Reads must use the **main address**. Querying the agent's address returns nothing, a common mistake.

The practical result: the terminal holds only an agent key. Funding and withdrawing happen in your own wallet. A stolen agent key can trade your account badly, but it cannot take the money.

### Safety features in the API

- **`scheduleCancel`, a dead-man's switch.** Cancels all open orders at a set time, at least 5 seconds ahead, up to 10 triggers a day. The terminal can keep pushing it forward while it runs, so its orders die if it crashes or loses its connection.
- **`expiresAfter`.** An order the network receives late is rejected instead of filled late.
- **Client order ids** (`cloid`) let the terminal match fills to its own orders after a reconnect.

### Limits

From [rate limits](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/rate-limits-and-user-limits):

- **REST:** 1,200 weight a minute per IP. Most reads weigh 20; book and position reads weigh 2.
- **Websocket:** 10 connections, 1,000 subscriptions.
- **Per address:** 10,000 actions to start, then one per $1 traded. A small account that places and cancels many orders can run dry.
- **Open orders:** 1,000.

### .NET options

The providers in `Plugins/Providers` are hand-written over Newtonsoft.Json and use no exchange library. Three ways in:

| Option | What it is | Fit |
| --- | --- | --- |
| Hand-write it | REST and websocket by hand, like Kraken. Signing needs secp256k1, Keccak, EIP-712 and MessagePack. | Matches the codebase; signing is the risky part |
| [HyperLiquid.Net](https://github.com/JKorf/HyperLiquid.Net) | JKorf's MIT client, 5.9.1, 67k NuGet downloads, updated 2026-10-22 | Fastest; pulls in the CryptoExchange.Net framework |
| [Nethereum](https://nethereum.com) for signing only | Mature MIT Ethereum library | Hand-written transport, proven signing |

The reference implementation is the official [Python SDK](https://github.com/hyperliquid-dex/hyperliquid-python-sdk). Whichever way we go, our signatures should be tested against its output.

## Who can use it

**US residents cannot, under Hyperliquid's terms.** The terms list as "Restricted Persons" anyone residing, located or incorporated in the United States or Ontario, plus sanctioned regions. They were last updated 2026-06-15 ([Cipher Intelligence](https://cipher-intelligence.io/exchanges/hyperliquid/)), and one summary says they also cover US citizens living abroad ([Datawallet](https://www.datawallet.com/crypto/is-hyperliquid-available-in-the-usa)). I could not open the terms page itself (it refused the request), so these points are second-hand.

- **Enforcement is at the website.** The site geoblocks by IP and makes you promise you are not restricted. The chain and API do not check who you are, so the terminal would technically work from anywhere.
- **That doesn't make it allowed.** The terms ban VPNs and misstating where you live. Jeff Yan, Hyperliquid's CEO, has said Americans using a VPN violate the terms, and that legal US access waits on the CFTC ([24/7 Wall St, 2026-10-19](https://247wallst.com/investing/cryptocurrency/2026/10/19/hyperliquids-ceo-explained-how-americans-could-legally-use-it-what-would-us-access-mean-for-hype/)). A terminal that wires a US user to the API is the same breach by another route. It would also be a risk to ship to US users of a public app.
- **No KYC normally,** with one exception: after the 2025-07-29 outage, refunds of 10,000 USDC or more required KYC.

### The legal US route: Payward and Bitnomial

On 2026-09-16, [Payward](https://www.payward.com/press-release/payward-hyperliquid-onchain-perpetual-futures), Kraken's parent, announced plans to offer US clients perpetual futures on Hyperliquid HIP-3 markets.

- **Who does what.** Bitnomial Exchange (a CFTC-regulated exchange) would list the contracts. Bitnomial Clearinghouse would deploy and clear the market. NinjaTrader Clearing would hold client accounts.
- **Who can trade.** Only accounts allowlisted by both NinjaTrader and Bitnomial. You would open a US futures account with a broker, not connect a wallet.
- **Status.** Not live. It needs CFTC approval, and no date was given. A former SEC senior counsel estimated at least 10 to 12 months, "assuming things went quickly" ([The Block, 2026-08-31](https://www.theblock.co/news/markets/2026-08-31-hyperliquid-eyes-us-entry-kraken-parent-payward-perpetual-futures-deal-bloomberg-413129)).
- **Why it matters here.** Payward already sells CFTC-regulated perps to US clients on Kraken Pro, live since 2026-06-15. If the Hyperliquid markets arrive the same way, the terminal's existing Kraken provider may be the shortest legal path. Whether Kraken's API would expose them is unknown.

Tax: as with any exchange, the terminal would need to export fills and funding payments. Hyperliquid's `userFills` and `userFunding` calls provide both.

## Fit with the terminal

**Hyperliquid fits the existing provider model almost exactly, because Kraken Futures already set the pattern.** `KrakenFuturesProvider` is a `BaseMarketDataProvider` with `ITradingProvider` and `IOrderBookProvider`. It declares `Leverage` and `FuturesTrading`, implements `SetLeverageAsync`, and routes Paper to Kraken's demo. The SDK already has `MarginMode` (Cross or Isolated), `Position.LiquidationPrice`, the `IsolatedMargin` capability and `_FUNDING` series.

| Terminal piece | Hyperliquid call | Status |
| --- | --- | --- |
| Symbols, max leverage | `meta`, `spotMeta`, `perpDexs` | Maps directly |
| Candles, live bars | `candleSnapshot` and the `candle` websocket | Maps; history capped at about 5,000 bars |
| Order book | `l2Book` and websocket | Maps |
| Place, cancel, modify orders | `order`, `cancel`, `modify` | Maps; needs signing |
| Leverage, margin mode | `updateLeverage` | Maps (`SetLeverageAsync`) |
| Positions, liquidation price | `clearinghouseState` | Maps |
| Funding rate | `metaAndAssetCtxs`, `fundingHistory` | Maps to the `_FUNDING` series |
| Paper environment | Testnet API | Maps, as Kraken demo does |
| Withdrawals (`IWithdrawalProvider`) | `withdraw3` | Leave out: needs the main wallet key |

### What is new

- **Signing.** Today's providers authenticate with API keys, HMAC secrets, OAuth or JWTs. This one needs secp256k1, EIP-712 and MessagePack, the first Ethereum-style crypto in the codebase. It must be byte-tested against the Python SDK.
- **The credential is a private key**, not a key pair. It goes in the same encrypted store, labelled as an agent key, and the setup check should confirm it is approved and is not the main wallet.
- **Max leverage per asset.** `MaxLeverage` is one number per provider (Kraken Futures says 50). Hyperliquid's runs from 3x to 40x by asset, and drops above a position size. The SDK needs a per-symbol answer, or the order form will offer leverage the exchange refuses.
- **The account address and the signer differ.** Reads use the main address, writes are signed by the agent. Both go in the config.
- **HIP-3 symbols** carry their exchange's prefix (`xyz:TSLA`). Each HIP-3 exchange has its own collateral and margin, and spot is a separate balance from perps.
- **Onboarding happens outside the terminal.** Buy USDC, deposit it from a wallet, and approve the agent on Hyperliquid's site or with a script. For a US user that site is geoblocked, which comes back to the legal question.

### Data for charts and StrategyLab

Live bars, funding and open interest work well. History is the weak spot: about 5,000 bars per interval and no candle archive. StrategyLab research should keep using Binance or Coinbase history; Hyperliquid's mark price follows the big CEXs closely by design. If the terminal trades Hyperliquid, it should record Hyperliquid candles itself from day one.

## Is live trading feasible?

**Technically yes, and it would be one of the cleaner live integrations the terminal has. Legally, not for a US resident today.**

| Question | Answer |
| --- | --- |
| Can the API do everything an order form needs? | Yes: market, limit, stops, TP/SL, trailing, reduce-only, leverage, margin mode |
| Do stops survive the terminal closing? | Yes, they rest on the exchange |
| Is it fast enough? | Yes: about 0.2 s median to a confirmed order |
| Can the terminal be stopped from losing the money outright? | Yes: an agent key cannot withdraw |
| Can a crash leave orders running? | Not if `scheduleCancel` is kept pushed forward |
| Can we test without real money? | Yes, on testnet, once the address has made a mainnet deposit |
| Is it allowed for a US resident? | No, under the terms; the Payward/Bitnomial route is pending |

### Risks

- **Liquidation.** With 1.25% maintenance on BTC at 40x, a normal hourly candle can wipe out a position. Backstop liquidation also takes the maintenance margin.
- **Auto-deleveraging.** In a crash a winning position can be closed for you at the mark price, as on 2025-10-10.
- **Thin markets.** Most perps are 3x to 5x because they are thin. JELLY and POPCAT show what manipulation does there.
- **Self-custody.** No support desk and no reversals. A lost main key is lost money, and a leaked key can be drained ($21M on 2025-10-10).
- **Outages.** The API failed for 37 minutes on 2025-07-29. The validator set is small and can step in, as it did with JELLY.
- **Legal.** Covered in *Who can use it*.

### Safety design for a screen-reader user

Leverage plus self-custody punishes a missed detail, so the terminal should say the details out loud. Most of this reuses patterns the terminal already has:

1. **Speak the liquidation price before every order.** For example: "Long 0.1 BTC at 80,000, 10x isolated. Liquidates near 73,000, 8.75% below. Stop at 76,000." Make the user confirm it.
2. **Default to isolated margin and low leverage.** Cap leverage in the terminal's own settings, below the exchange maximum.
3. **Warn when a position has no stop**, and offer to attach TP/SL in the same order.
4. **Speak margin alerts** as equity nears maintenance, through the existing alerts channel.
5. **Run the dead-man's switch while connected,** and say aloud when it fires.
6. **Agent key only.** Refuse a key that matches the main address. Never offer withdrawals from the terminal, consistent with the wallet design's "never a hotkey" rule.
7. **Testnet is the default** until the user switches live on purpose, the same pattern as Paper.

## Suggested plan

**Build it in four phases. Phases 1 and 3 need no Hyperliquid account, and only phase 4 moves real money.**

The plan:

1. **Read-only market data.** Symbols, candles, book, funding and open interest from `/info`. No key, no signing. Charts and alerts on Hyperliquid prices. *Gate:* prices match the website, and the 5,000-bar history cap is handled.
2. **Signing and testnet trading.** Agent key, EIP-712 signing, orders, leverage, positions. The Paper environment routes to testnet. *Gate:* signatures match the Python SDK byte for byte.
3. **Speech safety layer.** Spoken liquidation price, leverage cap, missing-stop warning, margin alerts, dead-man's switch. *Gate:* it is legal for you to trade live: you are not a US person, or the Payward/Bitnomial route is open.
4. **Small live trading.** Mainnet, small size, isolated margin, the terminal's own leverage cap. Record Hyperliquid candles from day one.

Phase 2 has a catch. The testnet faucet only pays addresses that have deposited on mainnet, and that deposit is itself using Hyperliquid. For a US resident, phase 2 waits on the legal gate too, unless testnet funds come another way.

### Open questions for Cody

- [ ] Are you a US resident or citizen? That decides whether phases 2 and 4 are open to you at all.
- [ ] Is this for your own trading, or a feature for the terminal's users? A public provider has to handle the US restriction for everyone.
- [ ] Is a read-only Hyperliquid data provider (phase 1) worth building on its own: funding, open interest and HIP-3 stock perps prices?
- [ ] Hand-written signing, Nethereum, or JKorf's HyperLiquid.Net?
- [ ] Should we watch Payward/Bitnomial and Kraken Pro instead, since Kraken is already a provider?

## Sources

Leverage tiers, market counts, volume and candle depth come from Hyperliquid's public API (`api.hyperliquid.xyz/info`), queried on 2026-10-25. Pages opened:

- Hyperliquid docs: [HyperCore overview](https://hyperliquid.gitbook.io/hyperliquid-docs/hypercore/overview), [USDC](https://hyperliquid.gitbook.io/hyperliquid-docs/hypercore/usdc), [margining](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/margining), [contract specifications](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/contract-specifications), [liquidations](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/liquidations), [auto-deleveraging](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/auto-deleveraging), [order types](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/order-types), [fees](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/fees)
- Hyperliquid API docs: [exchange endpoint](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/exchange-endpoint), [nonces and API wallets](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/nonces-and-api-wallets), [rate limits](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/rate-limits-and-user-limits), [historical data](https://hyperliquid.gitbook.io/hyperliquid-docs/historical-data), [testnet faucet](https://hyperliquid.gitbook.io/hyperliquid-docs/onboarding/testnet-faucet)
- [Payward press release, 2026-09-16](https://www.payward.com/press-release/payward-hyperliquid-onchain-perpetual-futures)
- [The Block, 2026-08-31: Hyperliquid eyes US entry via Payward](https://www.theblock.co/news/markets/2026-08-31-hyperliquid-eyes-us-entry-kraken-parent-payward-perpetual-futures-deal-bloomberg-413129)
- [24/7 Wall St, 2026-10-19: Hyperliquid's CEO on US access](https://247wallst.com/investing/cryptocurrency/2026/10/19/hyperliquids-ceo-explained-how-americans-could-legally-use-it-what-would-us-access-mean-for-hype/)
- [Datawallet: Is Hyperliquid available in the US?](https://www.datawallet.com/crypto/is-hyperliquid-available-in-the-usa)
- [Cipher Intelligence: Hyperliquid review](https://cipher-intelligence.io/exchanges/hyperliquid/) (incidents, validators, terms date)
- [CoinGecko: HLP vault analysis](https://www.coingecko.com/learn/hyperliquid-hlp-vault-analysis) (JELLY, POPCAT, October 2025)
- [HyperLiquid.Net](https://github.com/JKorf/HyperLiquid.Net) and the official [Python SDK](https://github.com/hyperliquid-dex/hyperliquid-python-sdk)

Not opened: Hyperliquid's Terms of Use, which refused automated requests. The US restriction is taken from the guides above and the CEO's own statement.
