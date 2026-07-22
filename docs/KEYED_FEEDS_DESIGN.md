# Keyed feeds — the 2.0 pipeline architecture

Tier 2 centerpiece (see ROADMAP_2.0.md). Written 2026-07-22 before implementation;
updated as phases land.

## The problem

Seven services assume "one chart identity". The focused chart owns the ONLY
data buffer (`DataManager._cache`), the ONLY live subscription
(`LiveStreamManager`), and the store's `State.Data`. Everything else — every
background tab, every non-focused workspace monitor — either re-fetches over
REST every 30 seconds or waits for a tab switch to gap-fill a stale snapshot.

Consequences: tab switches pay a network round-trip; background strategy/alert
evaluation is 30-second-granular instead of tick-level; split view is
impossible; the hosted terminal can't share one upstream feed between users.

## Root constraint

`IMarketDataProvider` exposes ONE untagged `IObservable<Ohlcv> LiveStream` and
one REPLACE-style `SetSubscriptionAsync(market, symbol, timeframe)`. Even if a
provider streamed two symbols, consumers could not demux the ticks. So true
multi-live needs an SDK capability, not just Core plumbing.

## Architecture

```
                    ┌─────────────────────────────────────────┐
                    │            MarketFeedHub                │
                    │  ConcurrentDictionary<ChartIdentity,    │
                    │                       ChartFeed>        │
                    └───┬──────────────┬──────────────┬───────┘
                        │ focused      │ leased       │ leased
                  ┌─────▼─────┐  ┌─────▼─────┐  ┌─────▼─────┐
                  │ ChartFeed │  │ ChartFeed │  │ ChartFeed │
                  │ BTC/USDT  │  │ AAPL 5m   │  │ GOLD 1h   │
                  │ 1m LIVE   │  │ live/poll │  │ idle buf  │
                  └─────┬─────┘  └───────────┘  └───────────┘
                        │ BarsUpdated
              ┌─────────▼──────────┐
              │ DataManager        │  (adapter: IDataManager surface kept;
              │ (focused binding)  │   dispatches UpdateDataAction to store)
              └────────────────────┘
```

- **ChartFeed** (one per ChartIdentity): owns an immutable
  `TimeSeriesBuffer<Ohlcv>` and ALL lifecycle logic that used to live in
  DataManager — initial refresh, snapshot catch-up + gap-fill, scrollback
  prepend, live-tick merge with the prepend-lock race guard. Raises
  `BarsUpdated(feed, isInitialLoad)`.
- **MarketFeedHub**: keyed registry + lease-based lifetime.
  `Acquire(identity)` returns a `FeedLease : IDisposable`; the last lease
  released stops the feed's live source (buffer retained for instant
  re-acquire). The FOCUSED feed is a hub-level concept: `SetFocused(identity)`
  moves the store binding and the legacy single live subscription.
- **DataManager** survives as a thin adapter so the ~10 existing consumers
  (MarketOrchestrator, StrategyEngine's DataUpdated hook, order-book path)
  keep compiling against `IDataManager`. It delegates to the hub's focused
  feed and forwards its events. New code should take the hub or IMarketFeeds,
  not IDataManager.
- **Store sync stays focused-only.** WorkspaceState.Data remains the UI's
  source of truth for the focused chart (renderer, sonification, navigation
  all read it). The hub binds exactly one feed to the store at a time.
  Non-focused feeds update their own buffers; nothing else re-renders.

### Live sources per feed

| Feed | Source |
|---|---|
| Focused | Legacy path unchanged: LiveStreamManager → DataOrchestrator channel (watchdog, reconnect, demo policy all preserved) |
| Non-focused, provider supports multi-sub | `SubscribeLiveAsync` handle (Phase B) |
| Non-focused, single-sub provider | No live source; buffer refreshed by whoever leases it (background monitor poll, tab-switch catch-up) |

### SDK capability (Phase B)

```csharp
// IMarketDataProvider — defaults keep all 28 plugins compiling untouched:
bool SupportsMultipleLiveSubscriptions => false;
Task<IAsyncDisposable> SubscribeLiveAsync(
    string market, string symbol, string timeframe, Action<Ohlcv> onBar)
    => throw new NotSupportedException(...);
```

Per-subscription callback = demux for free; no tick tagging needed. Each
subscription handle owns its socket/loop; disposing it unsubscribes. The
period-bucket consolidation (GetPeriodStart merge that LiveStreamManager does
with its single `_currentBucketCandle`) moves into a small per-subscription
`BarBucketConsolidator` so N feeds don't share one bucket.

Binance first (direct-API rewrite already owns raw websockets; one
`<symbol>@kline_<interval>` socket per subscription is well within Binance
connection limits). Other exchanges enroll later as demand asks.

### What each phase delivers

- **Phase A — foundation (behavior-preserving).** ChartFeed + hub extracted;
  DataManager adapts; suite proves no behavior change. No new resource use.
- **Phase B — multi-live SDK capability.** New DIMs + Binance implementation +
  consolidator; hub can run N live feeds. Nothing acquires them yet.
- **Phase C — the payoff.**
  - Tab switch: if the hub holds a feed for the target identity whose buffer
    is current (live, or fresher than the snapshot), bind it instantly — no
    network. Otherwise the existing catch-up path runs THROUGH the feed.
  - `MarketFeeds.GetBarsAsync` serves hub buffers before falling back to REST:
    background workspace monitors on multi-sub providers evaluate on
    tick-fresh bars with zero extra requests.
  - Live background tabs: opt-in setting (`workspace.liveBackgroundTabs`,
    default OFF like background monitoring), capped at 8 concurrent
    background live feeds, only on multi-sub providers; beyond the cap or
    capability, the existing 30-second poll remains.

### Lifetimes & hosted

Hub registered with the same lifetime as the store: singleton on MAUI, scoped
per session on WebHost. The hosted shared-feed pool (one upstream Binance
socket serving many user sessions) is the LATER hosted-alerts work — the hub's
`Acquire` seam is where a cross-session pool will plug in, but nothing in this
refactor depends on it.

### Found during Phase B: live-bar volume inflation on kline providers

`Ohlcv.UpdateWith` ADDS tick volume — correct for trade-tick streams, wrong for
kline-style streams that re-send the current candle with CUMULATIVE
volume-so-far: every ~1s update re-added the running total, inflating the live
bar's volume until the next REST refresh corrected it. Fix: providers declare
`LiveTickStyle` (default TradeDeltas = old behavior) and the new style-aware
`BarBucketConsolidator` diffs cumulative volumes instead of accumulating.

Fleet classification (2026-07-22 audit, verified at the emission sites):
- **CumulativeBars (was inflating intra-bar)**: Binance, MEXC, Kraken.
- **CumulativeBars (one-shot completed bars — style-neutral, declared for
  accuracy)**: Alpaca, Polygon.
- **TradeDeltas (correct all along)**: Bitstamp, Coinbase, Finnhub, Oanda,
  TwelveData, Tradier.
- **No live stream**: Schwab (REST poll), analytics providers.
- **Unclear, left on the safe default**: Interactive Brokers (smd field
  semantics undocumented in-repo; revisit if IB live charts show volume drift).

Also fixed here: `TimeSeriesBuffer.Append` overflowed on the `Empty` singleton
(0-length array × 2 = 0) — unreachable in the old pipeline because live always
started after a refresh, reachable the moment a per-feed subscription ticks an
empty feed.

### Found during Phase A: the silent strategy gap

`DataManager`'s live loop dispatches `UpdateDataAction` but has NEVER fired
`DataUpdated` (verified back to the original commit). Alerts and sonification
evaluate live because they subscribe to the store's StateStream; StrategyEngine
subscribes to `DataUpdated` — so focused-chart strategies only evaluate on
load, tab catch-up, and scrollback prepend, never on live bars. Background
monitors (opt-in) evaluate non-focused symbols on a 30s poll, but the chart
you are LOOKING at has no live strategy evaluation at all.

Fix lands in Phase C, deliberately: strategies subscribe to the focused feed's
`LiveAppend` transition (a new bar appended = the previous bar closed), which
is the correct bar-close semantic. Rewiring `DataUpdated` itself to fire per
tick was rejected: DataOrchestrationService also listens to it and would start
a full indicator recalc + order-book REST fetch on every tick.

### Invariants that must not regress

1. Live tick vs scrollback-prepend race: a tick either sees the fully
   prepended buffer or is dropped (prepend lock held across the merge).
2. Tab restore preserves full scrollback (snapshot restore + gap-fill only
   appends newer bars — never a fresh 200-bar reset).
3. A superseded tab-switch refresh never lands its data on the wrong tab
   (cancellation token checked after fetch).
4. Buffer caps: 5000 bars historical, trim-oldest on live append.
5. Demo policy: providers without live feeds are never live-subscribed.
6. Malformed ticks (zero OHLC legs) never reach indicator buffers.
