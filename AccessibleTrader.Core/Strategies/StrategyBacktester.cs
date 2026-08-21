using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Strategies;

/// <summary>
/// Replays historical OHLCV data through a strategy bar-by-bar and returns a full backtest result.
/// Simulates fills at the next bar's open price plus slippage.
/// Tracks equity curve and key performance metrics.
/// </summary>
public class StrategyBacktester : IStrategyBacktester
{
    private readonly Services.IProfileService? _profileService;
    private readonly Services.Strategies.IBacktestProfileCache? _profileCache;
    private readonly Services.Strategies.IMultiTimeframeDataService? _mtf;
    private readonly Services.IStrategyIndicatorCache? _indicatorCache;

    public StrategyBacktester(
        Services.IProfileService? profileService = null,
        Services.Strategies.IBacktestProfileCache? profileCache = null,
        Services.Strategies.IMultiTimeframeDataService? mtf = null,
        Services.IStrategyIndicatorCache? indicatorCache = null)
    {
        _profileService = profileService;
        _profileCache   = profileCache;
        _mtf            = mtf;
        _indicatorCache = indicatorCache;
    }

    public Task<BacktestResult> RunAsync(
        ITradingStrategy strategy,
        IReadOnlyList<Ohlcv> data,
        BacktestConfig config,
        WorkspaceState? state = null)
    {
        var result = Run(strategy, data, config, state ?? WorkspaceState.Initial);
        return Task.FromResult(result);
    }

    private BacktestResult Run(
        ITradingStrategy strategy,
        IReadOnlyList<Ohlcv> data,
        BacktestConfig config,
        WorkspaceState state)
    {
        // ── HTF cache invalidation ────────────────────────────────────────────
        // The MultiTimeframeDataService cache is keyed by (provider, symbol, TF) and is
        // populated once per ConfigurableStrategy.Initialize via PrewarmIndicatorAsync.
        // The prewarm path explicitly skips when an existing cache entry has Count > 0,
        // which is correct for live use but catastrophic for back-to-back backtests:
        // running first-half then second-half on the same chart leaves the second run
        // reading first-run HTF indicator values, which has no relationship to the
        // second-half date range. Symptom: second-half backtests with HTF leaves show
        // wildly degraded WR/PF as the strategy reacts to stale HTF context.
        // Clearing here at the start of every run guarantees the next prewarm computes
        // fresh against the (now potentially-different) date range.
        _mtf?.Clear();
        _profileCache?.Clear();

        // Apply optional date-range filter for walk-forward testing. Slicing here (before any
        // other work) means warmup, indicator buffers, and the strategy lifecycle all see the
        // narrower window — there's no leak from outside the window into the run. The trade-off
        // is that any indicator settling that would have happened in the discarded prefix has
        // to repeat inside warmup; the user should set WarmupBars accordingly.
        //
        // v11 diagnostic FIX: track the offset between data[0] (post-filter) and the original
        // data parameter, so the feature snapshot capture can map the loop index back to the
        // correct position in the workspace's ComponentData arrays. The workspace's indicator
        // buffers are aligned with the *original* full chart history — when we date-filter we
        // need to add this offset before reading. Without this fix, H1 and H2 both read
        // arr[0..filtered.Count-1] from the same shared workspace arrays and produce identical
        // duplicate snapshots at different dates.
        int featureCaptureOffset = 0;
        if (config.StartDate.HasValue || config.EndDate.HasValue)
        {
            DateTime lo = config.StartDate ?? DateTime.MinValue;
            DateTime hi = config.EndDate   ?? DateTime.MaxValue;
            var filtered = new List<Ohlcv>(data.Count);
            bool foundStart = false;
            for (int idx = 0; idx < data.Count; idx++)
            {
                var bar = data[idx];
                if (bar.Date >= lo && bar.Date <= hi)
                {
                    if (!foundStart) { featureCaptureOffset = idx; foundStart = true; }
                    filtered.Add(bar);
                }
            }
            data = filtered;
        }

        if (data.Count < 2)
        {
            var empty = new StrategyMetrics(0, 0, 0, 0, 0, 0);
            return new BacktestResult(empty, Array.Empty<BacktestTrade>(),
                Array.Empty<(DateTime, double)>(), "Insufficient data for backtest.", 0, 0);
        }

        // Warmup gate. The strategy still receives every bar via OnBar (so its internal
        // state and any IStrategyIndicatorCache entries can converge) but signals returned
        // before bar index >= warmupBars are dropped. Without this, indicators with long
        // settling periods (Ichimoku ~78, SMA(200), Cipher C stability window 66, etc.) emit
        // unreliable signals during warmup that skew win rate and drawdown numbers.
        // Clamp so warmup never consumes the entire dataset — at least 2 evaluated bars.
        int warmupBars = Math.Max(0, Math.Min(config.WarmupBars, data.Count - 2));

        var sizer = config.PositionSizer ?? new FixedSizePositionSizer();

        double equity = config.StartingCapital;
        double peakEquity = equity;
        double maxDrawdown = 0.0;

        var trades = new List<BacktestTrade>();
        var equityCurve = new List<(DateTime Date, double EquityValue)>();
        equityCurve.Add((data[0].Date, equity));

        // ── Open-position state ───────────────────────────────────────────────
        // Tracks every detail needed to honor stop / TP ladder exits during the bar loop.
        // RemainingQty decrements as TP rungs hit; the position is fully closed when it
        // reaches zero or when the stop is hit (which closes the entire remainder).
        OrderSide? openSide = null;
        double openEntryPrice = 0;
        double openInitialQty = 0;
        double openRemainingQty = 0;
        DateTime openTime = default;
        double? openStop = null;
        int openBarIndex = 0;
        // TP ladder queues — popped as each rung hits. Mirrors so prices and portions advance together.
        var openTpPrices    = new Queue<double>();
        var openTpPortions  = new Queue<double>();
        bool stopMovedToBreakeven = false;
        StopAdjustOnTp1 openStopAdjust = StopAdjustOnTp1.MoveToBreakeven;
        int openTrailAtrPeriod = 14;
        double openTrailAtrMultiple = 1.5;
        int winningTrades = 0;

        // v11 diagnostic: feature snapshot captured at the entry decision bar of the open
        // position. Persists across the open-position lifecycle so every BacktestTrade row
        // produced from this position (initial fill, TP rungs, stop, end-of-data close)
        // carries the same entry-bar feature context. Cleared when a new position opens.
        IReadOnlyDictionary<string, double>? openFeatureSnapshot = null;

        // The workspace state is what the strategy reads ActiveSeries from. ConfigurableStrategy
        // and any other strategy that uses condition trees / indicator references depends on the
        // live state being passed in — see IStrategyBacktester.RunAsync XML doc.
        // Stamp IsBacktesting=true so ConfigurableStrategy (and any other event-publishing
        // strategy) skips IEventBus publication during the replay — otherwise SetupSonifier
        // speaks every Armed/Dropped/Reconfirm event for thousands of replayed bars.
        var liveState = state with { IsBacktesting = true };

        // Build a growing history window; use immutable list for type compat
        var historyBuffer = ImmutableList<Ohlcv>.Empty;

        // Initialize with first bar
        var initParams = new Dictionary<string, object>();
        strategy.Initialize(historyBuffer, liveState, initParams);

        // Detect profile indicator series for per-bar replay. We snapshot the indicator codes
        // up front so we don't pay the lookup cost on every bar.
        var profileCodes = config.ReplayProfiles && _profileService != null && _profileCache != null
            ? liveState.ActiveSeries
                .Where(s => !string.IsNullOrEmpty(s.IndicatorCode))
                .Select(s => s.IndicatorCode!.ToUpperInvariant())
                .Where(c => c == "VPVR" || c == "VPFR" || c == "TPO")
                .Distinct()
                .ToList()
            : new List<string>();

        try
        {

        for (int i = 0; i < data.Count - 1; i++)
        {
            historyBuffer = historyBuffer.Add(data[i]);
            var bar = data[i];

            // Advance IStrategyIndicatorCache to this bar. The cache keys values by
            // (indicator, period, current bar count) and the engine's live loop invalidates
            // once per bar via OnDataUpdated. During backtest we grow historyBuffer a bar at
            // a time and re-feed it into the strategy, so the cache must be invalidated at
            // each step or a cached SMA/EMA/RSI/BB value from a prior bar leaks into the
            // current evaluation. Without this the first OnBar populates the cache at
            // historyBuffer.Count==1 and every subsequent bar reads stale values since the
            // data-length key never advances. Optional dependency — tests that run the
            // backtester without the cache still work.
            _indicatorCache?.Invalidate(historyBuffer.Count);

            // Per-bar profile replay: recompute VPVR/TPO bins from history[0..i] so the level
            // provider reads bar-i snapshots instead of the workspace's final-state bins.
            if (profileCodes.Count > 0)
            {
                foreach (var code in profileCodes)
                {
                    var bins = code == "TPO"
                        ? _profileService!.CalculateMarketProfile(historyBuffer)
                        : _profileService!.CalculateVolumeProfile(historyBuffer);
                    _profileCache!.Set(code, bins);
                }
            }

            // ── EXIT CHECK ────────────────────────────────────────────────────
            // Before processing any new strategy signal, walk this bar's range against the
            // open position (if any) and apply stop / TP exits. Stop has priority — if both
            // a stop AND a TP could hit on the same bar we conservatively assume the worse
            // outcome (stop) hit first. After TP1 fires, the stop optionally moves to breakeven
            // so the runner is protected. Multiple TPs can hit on the same bar (fast spike)
            // and are processed in order.
            if (openSide.HasValue && openStop.HasValue)
            {
                bool stopHit = openSide.Value == OrderSide.Buy
                    ? bar.Low  <= openStop.Value
                    : bar.High >= openStop.Value;
                if (stopHit)
                {
                    // Not openStop.Value. When the bar OPENS past the stop the market
                    // gapped through it and was never at that price — booking the loss
                    // at the stop invents a fill nobody could have got, and does it in
                    // the direction that flatters every strategy this engine scores.
                    double exitPrice = BarFill.StopExit(openStop.Value, bar.Open, openSide.Value);
                    double commission = exitPrice * openRemainingQty * config.CommissionRate;
                    double pnl = openSide.Value == OrderSide.Buy
                        ? (exitPrice - openEntryPrice) * openRemainingQty - commission
                        : (openEntryPrice - exitPrice) * openRemainingQty - commission;

                    equity += pnl;
                    if (equity > peakEquity) peakEquity = equity;
                    double dd2 = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0;
                    if (dd2 > maxDrawdown) maxDrawdown = dd2;
                    if (pnl > 0) winningTrades++;

                    trades.Add(new BacktestTrade(
                        openTime, openEntryPrice, openSide.Value, openRemainingQty,
                        bar.Date, exitPrice, pnl,
                        stopMovedToBreakeven ? "Breakeven stop" : "Stop hit",
                        StopPrice:   openStop,
                        BarsInTrade: i - openBarIndex,
                        FeatureSnapshot: openFeatureSnapshot));

                    equityCurve.Add((bar.Date, equity));

                    openSide = null;
                    openRemainingQty = 0;
                    openTpPrices.Clear();
                    openTpPortions.Clear();
                    stopMovedToBreakeven = false;
                }
            }

            // After the stop check, see how many TP rungs (if any) this bar reaches. Each rung
            // closes its configured portion of the *initial* quantity. If a rung's portion
            // exceeds the remaining quantity (because earlier rungs already closed most of it
            // or because the stop almost-but-not-quite hit), we just close whatever is left.
            while (openSide.HasValue && openTpPrices.Count > 0)
            {
                double tpPrice = openTpPrices.Peek();
                bool tpHit = openSide.Value == OrderSide.Buy
                    ? bar.High >= tpPrice
                    : bar.Low  <= tpPrice;
                if (!tpHit) break;

                openTpPrices.Dequeue();
                double portion = openTpPortions.Count > 0 ? openTpPortions.Dequeue() : 1.0;
                double closeQty = Math.Min(openRemainingQty, openInitialQty * portion);
                if (closeQty <= 0) break;

                // Same correction as the stop above, opposite sign: a bar that opened
                // beyond the rung filled there, which is better than the rung, not at
                // it. Leaving this uncorrected while correcting the stop would make
                // the two exit paths disagree about what a gap means.
                double fillPx = BarFill.TargetExit(tpPrice, bar.Open, openSide.Value);

                double commission = fillPx * closeQty * config.CommissionRate;
                double pnl = openSide.Value == OrderSide.Buy
                    ? (fillPx - openEntryPrice) * closeQty - commission
                    : (openEntryPrice - fillPx) * closeQty - commission;

                equity += pnl;
                if (equity > peakEquity) peakEquity = equity;
                double dd3 = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0;
                if (dd3 > maxDrawdown) maxDrawdown = dd3;
                if (pnl > 0) winningTrades++;

                trades.Add(new BacktestTrade(
                    openTime, openEntryPrice, openSide.Value, closeQty,
                    bar.Date, fillPx, pnl,
                    $"TP rung hit at {tpPrice:F4}",
                    StopPrice:   openStop,
                    BarsInTrade: i - openBarIndex,
                    FeatureSnapshot: openFeatureSnapshot));

                openRemainingQty -= closeQty;
                equityCurve.Add((bar.Date, equity));

                // Adjust stop after the first TP rung clears based on the signal's StopAdjust mode.
                if (!stopMovedToBreakeven)
                {
                    switch (openStopAdjust)
                    {
                        case StopAdjustOnTp1.MoveToBreakeven:
                            openStop = openEntryPrice;
                            break;
                        case StopAdjustOnTp1.TrailByAtr:
                            // Initial trail: entry + ATR trail distance (will be updated each bar below).
                            openStop = openEntryPrice;
                            break;
                        case StopAdjustOnTp1.None:
                            // Leave stop where it is.
                            break;
                    }
                    stopMovedToBreakeven = true;
                }

                if (openRemainingQty <= 0.000001)
                {
                    openSide = null;
                    openRemainingQty = 0;
                    openTpPrices.Clear();
                    openTpPortions.Clear();
                    stopMovedToBreakeven = false;
                    break;
                }
            }

            // ── ATR TRAIL ─────────────────────────────────────────────────────
            // After TP1 fires with TrailByAtr mode, update the trailing stop each bar.
            // The stop ratchets forward (longs) / backward (shorts) but never retreats.
            if (openSide.HasValue && stopMovedToBreakeven && openStopAdjust == StopAdjustOnTp1.TrailByAtr
                && openStop.HasValue && i >= openTrailAtrPeriod)
            {
                // Compute Wilder's ATR over the trailing period.
                double atrSum = 0;
                for (int a = i - openTrailAtrPeriod + 1; a <= i; a++)
                {
                    double tr = data[a].High - data[a].Low;
                    if (a > 0)
                    {
                        tr = Math.Max(tr, Math.Abs(data[a].High - data[a - 1].Close));
                        tr = Math.Max(tr, Math.Abs(data[a].Low  - data[a - 1].Close));
                    }
                    atrSum += tr;
                }
                double atr = atrSum / openTrailAtrPeriod;
                double trailDistance = atr * openTrailAtrMultiple;

                double newStop = openSide.Value == OrderSide.Buy
                    ? bar.Close - trailDistance
                    : bar.Close + trailDistance;

                // Ratchet: only move the stop in the favorable direction.
                if (openSide.Value == OrderSide.Buy && newStop > openStop.Value)
                    openStop = newStop;
                else if (openSide.Value == OrderSide.Sell && newStop < openStop.Value)
                    openStop = newStop;
            }

            // ── STRATEGY EVALUATION ───────────────────────────────────────────
            var signal = strategy.OnBar(bar, historyBuffer, liveState);

            // Drop signals emitted while indicators are still warming up.
            if (i < warmupBars) signal = null;

            // Reverse-on-signal opt-out (research-harness use): when AllowReverseOnSignal is
            // false and a position is already open, drop incoming signals on the floor so the
            // existing position can ride to its stop / TP / end-of-data without a "Reversed by"
            // exit. Lets us isolate the entry signal's standalone edge from the structural
            // benefit of frequent counter-signal exits. Default true preserves all production
            // behavior (live trading + the live-app backtest button).
            if (signal != null && openSide.HasValue && !config.AllowReverseOnSignal)
                signal = null;

            if (signal != null)
            {
                var liveMetrics = strategy.GetMetrics();
                double qty = signal.Quantity ?? sizer.CalculateSize(signal, equity, liveMetrics);

                // Simulate fill at next bar open + slippage
                double fillPrice = data[i + 1].Open;
                double slippage = fillPrice * config.SlippagePercent;
                fillPrice += signal.Side == OrderSide.Buy ? slippage : -slippage;

                double commission = fillPrice * qty * config.CommissionRate;

                if (openSide.HasValue)
                {
                    // Reverse: close existing remainder at next-bar open, then open new in signal direction.
                    double pnl = openSide.Value == OrderSide.Buy
                        ? (fillPrice - openEntryPrice) * openRemainingQty - commission
                        : (openEntryPrice - fillPrice) * openRemainingQty - commission;

                    equity += pnl;
                    if (equity > peakEquity) peakEquity = equity;
                    double dd = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0;
                    if (dd > maxDrawdown) maxDrawdown = dd;

                    trades.Add(new BacktestTrade(
                        openTime, openEntryPrice, openSide.Value, openRemainingQty,
                        data[i + 1].Date, fillPrice, pnl,
                        $"Reversed by {signal.Rationale}",
                        StopPrice:   openStop,
                        BarsInTrade: (i + 1) - openBarIndex,
                        FeatureSnapshot: openFeatureSnapshot));

                    if (pnl > 0) winningTrades++;
                    equityCurve.Add((data[i + 1].Date, equity));
                }

                // Open new position (whether reversing or opening fresh)
                if (!openSide.HasValue) equity -= commission;
                openSide = signal.Side;
                openEntryPrice = fillPrice;
                openInitialQty = qty;
                openRemainingQty = qty;
                openTime = data[i + 1].Date;
                openStop = signal.StopLoss;
                openBarIndex = i + 1;
                stopMovedToBreakeven = false;
                openStopAdjust = signal.StopAdjust;
                openTrailAtrPeriod = signal.TrailAtrPeriod;
                openTrailAtrMultiple = signal.TrailAtrMultiple;

                // v11 diagnostic: capture every numeric indicator component value at the
                // DECISION bar (i, not i+1 — the signal was generated reading bar i's data
                // and bars before it, the fill happens at i+1's open). This snapshot is
                // attached to every BacktestTrade row produced from this position so the
                // CSV export can correlate winners vs losers against feature values that
                // may not even be in the strategy's leaf set. The captureOffset maps from
                // the date-filtered loop index back to the original ComponentData index —
                // see the date-filter block above for the offset calculation. Without it,
                // H1 and H2 read identical absolute positions from the shared workspace
                // arrays and produce duplicate (wrong) snapshots.
                openFeatureSnapshot = CaptureFeatureSnapshot(liveState, i + featureCaptureOffset);

                // Capture the TP ladder so the exit check on subsequent bars can fire each rung.
                // Falls back to the single TakeProfit field if no ladder was provided (e.g. by a
                // strategy other than ConfigurableStrategy).
                openTpPrices.Clear();
                openTpPortions.Clear();
                if (signal.TpLadder != null && signal.TpLadder.Count > 0)
                {
                    for (int t = 0; t < signal.TpLadder.Count; t++)
                    {
                        openTpPrices.Enqueue(signal.TpLadder[t]);
                        double portion = signal.TpClosePortions != null && t < signal.TpClosePortions.Count
                            ? signal.TpClosePortions[t]
                            : (1.0 / signal.TpLadder.Count);
                        openTpPortions.Enqueue(portion);
                    }
                }
                else if (signal.TakeProfit.HasValue)
                {
                    openTpPrices.Enqueue(signal.TakeProfit.Value);
                    openTpPortions.Enqueue(1.0);
                }

                if (!openSide.HasValue || trades.Count == 0 ||
                    trades[trades.Count - 1].EntryTime != openTime)
                {
                    equityCurve.Add((data[i + 1].Date, equity));
                }
            }
        }

        // Close any open position at last bar close (whatever remainder is left after the
        // exit-check loop has processed every TP rung that fit in the data window).
        if (openSide.HasValue && data.Count > 0 && openRemainingQty > 0)
        {
            var lastBar = data[^1];
            double fillPrice = lastBar.Close;
            double commission = fillPrice * openRemainingQty * config.CommissionRate;
            double pnl = openSide.Value == OrderSide.Buy
                ? (fillPrice - openEntryPrice) * openRemainingQty - commission
                : (openEntryPrice - fillPrice) * openRemainingQty - commission;

            equity += pnl;
            if (pnl > 0) winningTrades++;

            trades.Add(new BacktestTrade(
                openTime, openEntryPrice, openSide.Value, openRemainingQty,
                lastBar.Date, fillPrice, pnl, "End of data",
                StopPrice:   openStop,
                BarsInTrade: (data.Count - 1) - openBarIndex,
                FeatureSnapshot: openFeatureSnapshot));

            equityCurve.Add((lastBar.Date, equity));
        }

        // v11 diagnostic: dump the trade log + feature snapshots to a CSV in %TEMP% so we
        // can open it in Excel and find which features discriminate winners from losers.
        // Best-effort — failures here must not break the backtest result. Path is logged
        // to Debug output so the user can find the file after the run.
        TryWriteDiagnosticCsv(trades);

        int totalTrades = trades.Count;
        double winRate = totalTrades > 0 ? (double)winningTrades / totalTrades : 0.0;
        double totalPnL = equity - config.StartingCapital;
        double totalReturn = config.StartingCapital > 0 ? totalPnL / config.StartingCapital * 100.0 : 0.0;

        // Sharpe: (annualised return) / (annualised stddev of daily returns)
        double sharpe = ComputeSharpe(equityCurve);

        // Gross profit/loss feed both the profit factor below and position sizers
        // (Kelly needs real avg win/loss, not a net-PnL approximation).
        double grossProfit = trades.Where(t => t.PnL.GetValueOrDefault() > 0).Sum(t => t.PnL!.Value);
        double grossLoss   = trades.Where(t => t.PnL.GetValueOrDefault() < 0).Sum(t => -t.PnL!.Value);

        var metrics = new StrategyMetrics(
            TotalSignals: totalTrades,
            WinningTrades: winningTrades,
            WinRate: winRate,
            MaxDrawdown: maxDrawdown,
            TotalPnL: totalPnL,
            SharpeRatio: sharpe,
            GrossProfit: grossProfit,
            GrossLoss: grossLoss
        );

        int evaluatedBars = Math.Max(0, data.Count - warmupBars);

        // R-multiple metrics — only meaningful when stops were provided in the entry signals.
        // ConfigurableStrategy emits StopLoss; the legacy built-in strategies (SMA cross, RSI,
        // BB) do not — for those the R fields will be NaN, which the modal renders as "—".
        var rMultiples = new List<double>();
        foreach (var t in trades)
        {
            if (!t.StopPrice.HasValue || !t.ExitPrice.HasValue) continue;
            double riskPerUnit = t.Side == OrderSide.Buy ? t.EntryPrice - t.StopPrice.Value : t.StopPrice.Value - t.EntryPrice;
            if (riskPerUnit <= 0) continue;
            double rewardPerUnit = t.Side == OrderSide.Buy ? t.ExitPrice.Value - t.EntryPrice : t.EntryPrice - t.ExitPrice.Value;
            rMultiples.Add(rewardPerUnit / riskPerUnit);
        }
        double avgR       = rMultiples.Count > 0 ? rMultiples.Average() : double.NaN;
        double expectancy = avgR; // expectancy in R per trade is the same statistic

        // Profit factor: sum of winning P&L over absolute sum of losing P&L.
        double profitFactor = grossLoss > 0 ? grossProfit / grossLoss : double.NaN;

        // Average bars-in-trade.
        double avgBars = trades.Count > 0 ? trades.Average(t => (double)t.BarsInTrade) : 0.0;

        // Longest losing streak.
        int curStreak = 0, longestStreak = 0;
        foreach (var t in trades)
        {
            if (t.PnL.GetValueOrDefault() < 0)
            {
                curStreak++;
                if (curStreak > longestStreak) longestStreak = curStreak;
            }
            else curStreak = 0;
        }

        string rText = double.IsNaN(avgR) ? "" : $" Average R: {avgR:F2}.";
        string speech = $"{totalTrades} trades, {winRate * 100.0:F1} percent win rate, " +
                        $"maximum drawdown {maxDrawdown * 100.0:F1} percent, " +
                        $"total return {totalReturn:F1} percent." + rText + " " +
                        $"{warmupBars} warmup bars, {evaluatedBars} bars evaluated.";

        return new BacktestResult(
            metrics, trades, equityCurve, speech, warmupBars, evaluatedBars,
            AverageR: avgR,
            Expectancy: expectancy,
            ProfitFactor: profitFactor,
            AverageBarsInTrade: avgBars,
            LongestLosingStreak: longestStreak);
        }
        finally
        {
            // Always clear both caches when the run finishes. The profile cache's IsActive must
            // drop back to false so subsequent live evaluations of VolumeProfileLevelProvider
            // fall through to series.ProfileBins instead of reading the stale final-bar snapshot.
            // The MTF cache is cleared for the same reason — stale HTF indicator values from a
            // date-filtered run must not leak into a subsequent run or live evaluation.
            // Belt-and-suspenders: both caches are also cleared at the START of Run() (above)
            // so even if a prior run threw between the try and finally, the next run starts clean.
            _profileCache?.Clear();
            _mtf?.Clear();
        }
    }

    // Internal for test access (Sharpe annualisation pins in StrategyBacktesterTests).
    internal static double ComputeSharpe(List<(DateTime Date, double EquityValue)> curve)
    {
        if (curve.Count < 2) return 0.0;

        var returns = new List<double>();
        for (int i = 1; i < curve.Count; i++)
        {
            double prev = curve[i - 1].EquityValue;
            if (prev == 0) continue;
            returns.Add((curve[i].EquityValue - prev) / prev);
        }

        if (returns.Count < 2) return 0.0;

        double mean = returns.Average();
        double variance = returns.Select(r => (r - mean) * (r - mean)).Average();
        double stdDev = Math.Sqrt(variance);

        if (stdDev == 0) return 0.0;

        // Annualise by the OBSERVED sampling frequency. The curve points land on trade
        // events, not calendar days, and the bar interval varies by timeframe — the old
        // hardcoded √252 understated intraday Sharpe by ~20× on 1m charts and overstated
        // weekly ones. periodsPerYear = how many equity observations actually occurred
        // per year of backtest span; √(that) is the correct annualisation for the
        // return series we actually measured.
        double years = (curve[^1].Date - curve[0].Date).TotalDays / 365.25;
        if (years <= 0) return 0.0;
        double periodsPerYear = returns.Count / years;

        return mean / stdDev * Math.Sqrt(periodsPerYear);
    }

    /// <summary>
    /// v11 diagnostic: snapshot every numeric indicator component value at a specific bar
    /// from the workspace's active series. Returns a flat dictionary keyed by
    /// "{IndicatorCode}.{ComponentName}" for use in trade-level CSV export. NaN values are
    /// preserved (they're meaningful — the indicator hadn't computed at this bar). Returns
    /// null when no active series exist so the trade carries no snapshot rather than an
    /// empty dictionary.
    /// </summary>
    private static IReadOnlyDictionary<string, double>? CaptureFeatureSnapshot(
        WorkspaceState state, int barIndex)
    {
        if (state.ActiveSeries == null || state.ActiveSeries.Count == 0) return null;
        if (barIndex < 0) return null;

        var snap = new Dictionary<string, double>();
        foreach (var series in state.ActiveSeries)
        {
            if (series.IndicatorCode == null) continue;
            // ChartSeries exposes its component buffers via GetComponentData(name); the
            // available component names live on Data.ComponentData. Iterate that dictionary
            // directly so we don't need to know the component vocabulary up front.
            if (series.Data?.ComponentData == null) continue;
            foreach (var kv in series.Data.ComponentData)
            {
                var arr = kv.Value;
                if (arr == null || arr.Length == 0) continue;
                int idx = Math.Min(barIndex, arr.Length - 1);
                if (idx < 0) continue;
                string key = $"{series.IndicatorCode}.{kv.Key}";
                snap[key] = arr[idx];
            }
        }
        return snap.Count > 0 ? snap : null;
    }

    /// <summary>
    /// v11 diagnostic: write the trade log + per-trade feature snapshots to a CSV in
    /// %TEMP%. Best-effort — exceptions are swallowed (with a Debug log) so the backtest
    /// result still returns even if the disk write fails. Path is logged to Debug output
    /// and to the Console so the user can find the file after the run.
    ///
    /// CSV layout: a fixed prefix of trade columns (entry/exit/PnL/etc) followed by one
    /// column per unique feature key found across all trades' snapshots. Trades that
    /// don't have a feature key get an empty cell for it.
    /// </summary>
    private static void TryWriteDiagnosticCsv(IReadOnlyList<BacktestTrade> trades)
    {
        if (trades == null || trades.Count == 0) return;

        try
        {
            // Discover the union of feature keys across all snapshots so the CSV has a
            // stable column header even when individual trades have different features
            // (e.g. some bars have NaN-pruned components, etc).
            var featureKeys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var t in trades)
            {
                if (t.FeatureSnapshot == null) continue;
                foreach (var k in t.FeatureSnapshot.Keys) featureKeys.Add(k);
            }

            // UTC with explicit "Z" suffix so exports from traders in different
            // timezones sort and compare cleanly. Local time produced ambiguous
            // filenames that could collide across machines on the same trade
            // desk.
            string filename = $"accessible-trader-backtest-{DateTime.UtcNow:yyyyMMdd-HHmmss}Z.csv";
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), filename);

            using var writer = new System.IO.StreamWriter(path);

            // Header
            var header = new System.Text.StringBuilder();
            header.Append("EntryTime,EntryPrice,Side,Quantity,ExitTime,ExitPrice,PnL,R,BarsInTrade,ExitReason,StopPrice");
            foreach (var k in featureKeys)
            {
                header.Append(',');
                header.Append(EscapeCsv(k));
            }
            writer.WriteLine(header.ToString());

            // Rows
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var t in trades)
            {
                double r = double.NaN;
                if (t.StopPrice.HasValue && t.ExitPrice.HasValue)
                {
                    double riskPerUnit = t.Side == OrderSide.Buy
                        ? t.EntryPrice - t.StopPrice.Value
                        : t.StopPrice.Value - t.EntryPrice;
                    if (riskPerUnit > 0)
                    {
                        double rewardPerUnit = t.Side == OrderSide.Buy
                            ? t.ExitPrice.Value - t.EntryPrice
                            : t.EntryPrice - t.ExitPrice.Value;
                        r = rewardPerUnit / riskPerUnit;
                    }
                }

                var row = new System.Text.StringBuilder();
                row.Append(t.EntryTime.ToString("o", inv)); row.Append(',');
                row.Append(t.EntryPrice.ToString("R", inv)); row.Append(',');
                row.Append(t.Side); row.Append(',');
                row.Append(t.Quantity.ToString("R", inv)); row.Append(',');
                row.Append(t.ExitTime?.ToString("o", inv) ?? ""); row.Append(',');
                row.Append(t.ExitPrice?.ToString("R", inv) ?? ""); row.Append(',');
                row.Append(t.PnL?.ToString("R", inv) ?? ""); row.Append(',');
                row.Append(double.IsNaN(r) ? "" : r.ToString("R", inv)); row.Append(',');
                row.Append(t.BarsInTrade); row.Append(',');
                row.Append(EscapeCsv(t.ExitReason)); row.Append(',');
                row.Append(t.StopPrice?.ToString("R", inv) ?? "");

                foreach (var k in featureKeys)
                {
                    row.Append(',');
                    if (t.FeatureSnapshot != null && t.FeatureSnapshot.TryGetValue(k, out var v))
                        row.Append(double.IsNaN(v) ? "" : v.ToString("R", inv));
                }
                writer.WriteLine(row.ToString());
            }

            writer.Flush();
            System.Diagnostics.Debug.WriteLine($"[Backtest CSV] Wrote {trades.Count} trades to {path}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Backtest CSV] Failed to write diagnostic CSV: {ex.Message}");
        }
    }

    /// <summary>RFC 4180 minimal CSV escape — wraps in quotes if the field contains comma,
    /// quote, or newline; doubles embedded quotes.</summary>
    private static string EscapeCsv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
