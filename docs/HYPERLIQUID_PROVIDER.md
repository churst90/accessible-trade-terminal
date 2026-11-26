# Hyperliquid provider

Plugin: `Plugins/Providers/AccessibleTrader.Plugins.Hyperliquid/`. Tests: `AccessibleTrader.Tests/HyperliquidTests.cs` and `HyperliquidOrderStreamTests.cs`, plus a `HyperliquidRig` in `ProviderOrderConformanceRigs.cs`. Fixtures: `AccessibleTrader.Tests/Fixtures/Hyperliquid/`.

## Restriction notice

Hyperliquid does not serve United States persons, and Ontario, Canada is also restricted. The provider's description says so. Do not enable it from those places.

## What is built

- Market data: perpetual and spot symbols from `meta` and `spotMeta`, `candleSnapshot` candles (paged backward past the 5,000-candle cap), `l2Book` depth, `allMids`, funding rate and open interest (`metaAndAssetCtxs`), and websocket streaming of candles and the book. Websocket candle frames are cumulative.
- Trading through `/exchange`, signed with an agent wallet: limit (Gtc, Alo for post-only, Ioc), market (an IOC limit 5% past the mid), reduce-only, stop-loss and take-profit trigger orders (market or limit), entry plus stop plus target as one `normalTpsl` action, cancel, cancel-all, modify, leverage with isolated margin by default, and a client order id sent as a 16-byte cloid.
- Account reads: balances, positions with leverage, margin mode and the venue's liquidation price, open orders, fills, per-order status.
- Balances follow the account's mode (`userAbstraction`, remembered ten minutes). In the classic split ("disabled") the perps account is its own `USDC-PERPS` row beside the spot rows. In a unified or portfolio-margin account the perps margin IS the spot USDC, so only the spot rows are listed; listing both counted the margin twice. If the mode cannot be read, the spot reply tells them apart (only a unified account's carries `tokenToAvailableAfterMaintenance`).
- Order events are pushed: `orderUpdates` and `userFills` for the account address on a socket of their own (`HyperliquidOrderStream`), opened when the order service subscribes, so a fill is heard whichever venue the chart shows. `ProvidesOrderStream` is true; `SupportsOrderEventStreaming` is true only while that socket is up and the venue has confirmed both subscriptions, and while it is false a placed order is polled as before. Fills are tallied per order: a partial fill is announced only for an order whose size is known (seen opening); a terminal status waits up to 2 s for fills it has not seen, then takes the price from the REST fill history; the order type from one REST `orderStatus` call says whether a filled order was a stop or a target. The history frame that opens `userFills` (`isSnapshot`) is not announced. Every cancel or refusal the user did not ask for carries the reason in words.
- Signing is done in-process (keccak, msgpack, EIP-712 phantom agent, RFC 6979 low-s ECDSA via BouncyCastle). No new dependency beyond BouncyCastle.Cryptography.
- Rounding follows the venue: prices to 5 significant figures and at most (6 perp, 8 spot) minus szDecimals decimals, whole numbers from 100,000; sizes truncated, never rounded up.
- Asset ids: perp is the position in the `meta` universe (a delisted entry keeps its number); spot is 10000 plus the spot universe index.
- Environments: Live is mainnet, anything else (Paper) is the testnet host. Only the exact value Live reaches mainnet.
- Safety: the main wallet's own private key is refused (no request is made); unsupported time in force (FOK) and spot brackets are refused before any request; a failed leverage update stops the order; every venue refusal comes back as `ORDER_FAILED:` with the venue's text; a refused protective leg is surfaced while the entry id is returned; the key and URL never appear in messages. No new UI was added.
- Trailing stop is NOT offered. Hyperliquid has no native trailing stop, and the capability audit counts reading `TrailStopValue` as backing the flag, so the provider neither reads it nor declares it.

## User setup

1. On app.hyperliquid.xyz open More, then API. Create an agent wallet (API wallet), give it a name and approve it with your main wallet.
2. Copy the agent's private key. It can trade but cannot withdraw.
3. In Settings, API keys, Hyperliquid: paste your MAIN wallet address as the key and the AGENT private key as the secret.
4. Use Validate. It checks the agent appears in your approved agents. Use Paper for the testnet (separate account and funds at app.hyperliquid-testnet.xyz).

## Verified

- Signing is byte-exact against the Python SDK's own `tests/signing_test.py` vectors: the phantom-agent connection id, and r, s and v for dummy, order, order with cloid, vault, trigger order and scheduleCancel with and without a time, on both mainnet and testnet (14 signatures). The vectors were fetched from the SDK repository (signing_test.py) on 2026-11-04; the SDK itself was not run.
- msgpack bytes, the Hardhat address vector, wire number formatting, price and size rounding, asset ids.
- Parsing of recorded live replies: meta, spotMeta, candleSnapshot (trimmed copies taken 2026-11-04).
- Account replies recorded 2026-11-26 from two public mainnet addresses, read without a key (`recorded_*` fixtures): a unified account with 14 positions and venue stop orders, and a market-making sub-account in the classic mode. Positions, open orders (stop market shows its trigger, post-only limit its price), fills (`fee` already includes `builderFee`; a maker's fee is negative), order status (open trigger, filled) and both balance modes are tested against them. The unified account showed perps margin used 978.324102 equal to spot USDC on hold 978.324102, which is what found the double count.
- The order stream, recorded 2026-11-26: 75 s of the busy public account, `orderUpdates`, `userFills` and a BTC candle feed on ONE socket (all three confirmed and flowing). Seen: a partial fill sends no order update; a fill can come before its order's "open"; every fill came before its order's "filled" or "canceled"; a cancel after a partial fill reports the unfilled rest in `sz`. The tests replay those frames (`recorded_ws_frames.json`).
- The real provider's stream against that account for 60 s (read-only, no key, 2026-11-26): streaming claimed within 5 s, 3,482 updates (Filled, PartialFill, Cancelled with the filled part, Rejected "it is post-only and would have traded at once"), and false again after DisconnectAsync. It printed a cancel's filled size as 0.0044299999999999895; sizes worked out by subtraction are now rounded to 10 decimals.
- Sabotage 2026-11-26: 5/5 on the balance mode and recorded fills, 8/8 on the order stream (snapshot skip, the wait for fills, partial only below the known size, stop flag, both subscriptions, REST price fallback, reject reason, size rounding).
- Request shapes for every order type through the real provider with a fake HTTP handler; the posted signature equals a re-signing of the posted action and nonce.
- Candle paging with a synthetic 12,000-bar series that mimics the live newest-5,000 behaviour.
- One live read-only check through the provider (2026-11-04): 179 symbols, five BTC daily bars, book bid/ask. No order was placed.
- Sabotage: changing the EIP-712 chainId turned 14 tests red; rounding sizes instead of truncating turned 4 red. Both restored and diffed clean.

## Unverified

- No order has ever been sent, on mainnet or testnet. The first real order is the real test of the whole chain.
- `clearinghouseState.json` and `frontendOpenOrders.json` are written from the documented shapes (each says so); the `recorded_*` fixtures beside them are the real replies and agree with the parsers.
- The candle and book websocket has been tested through its message parsing; the live check above carried a candle subscription on the account socket, not the provider's own candle path.
- A filled STOP or TARGET announced as "stop hit" or "take profit hit": the order type comes from REST `orderStatus` on the filled order's id, and no triggered order has been watched filling. If the venue gives the triggered order a new id or another type name, it is announced as a plain fill.
- Of the cancel and refusal words, only open, filled, canceled and badAloPxRejected were seen live; the rest are the documented status names.
- Spot order paths are tested against fixtures only.
- Nothing was seen or heard in the app UI.

## Follow-ups

- HIP-3 builder-deployed perp dexes (not listed).
- TWAP orders.
- `expiresAfter` on actions.
- Wiring dead-man's switch (`ScheduleCancelAsync` exists and is tested, but nothing calls it).
- Wiring funding and open interest into an indicator or the status line (the provider exposes them).
- Vault and sub-account trading (the signer supports a vault address; the provider does not expose it).
- Withdrawals and transfers are deliberately excluded.
- Rate limits are approximated as three lanes (heavy info 30/min, light info 200/min, exchange 100/min).

## Draft entries for CHANGES / TODO / USER_MANUAL

CHANGES: Added a Hyperliquid provider (perps and spot): candles paged past the venue's 5,000 cap, depth, funding and open interest, websocket streaming, and trading through an agent wallet with in-process signing proven byte-exact against the Python SDK's vectors. Trailing stops are not offered because the venue has none. Never tested with a real order.

TODO: Send the first testnet order and record real clearinghouseState and frontendOpenOrders replies; HIP-3 dexes, TWAP, expiresAfter, dead-man's-switch wiring, funding indicator, vault trading.

USER_MANUAL: Hyperliquid needs an agent wallet. Create one at app.hyperliquid.xyz under More, API, approve it, then enter your main wallet address as the key and the agent's private key as the secret. Never enter your main wallet's own key; it is refused. Paper uses the testnet. Hyperliquid is not available to United States persons or in Ontario.
