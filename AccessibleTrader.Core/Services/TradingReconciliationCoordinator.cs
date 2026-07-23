using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services
{
    public interface ITradingReconciliationCoordinator
    {
        /// <summary>
        /// Announces persisted paper-account exposure (open positions / working
        /// orders) once at startup. Live-broker exposure is announced reactively
        /// the first time each provider connects — see the constructor subscription.
        /// </summary>
        Task AnnounceAtStartupAsync();
    }

    /// <summary>
    /// Surfaces broker state that survives an app restart but was previously only
    /// discoverable by opening the Trading Dashboard: open positions and working
    /// orders. A user who closed the app with live exposure must not have to
    /// remember to go looking for it — the terminal says so out loud.
    ///
    /// Paper account: announced once at startup when paper mode is active (state
    /// is local, no connection needed). Live brokers: announced the first time
    /// each provider connects in a session, via ConnectionStatusEvent.
    ///
    /// WHILE-YOU-WERE-AWAY (2026-07-22): each reconciliation also diffs the
    /// provider's positions against a persisted snapshot from the previous
    /// session. A position that vanished while the app was off — a stop that
    /// fired overnight — is reported by voice with its closing fill and realized
    /// P&amp;L instead of silently missing from the count. The snapshot refreshes
    /// after every fill event so closes announced live are not re-reported at
    /// the next startup.
    /// </summary>
    public sealed class TradingReconciliationCoordinator : ITradingReconciliationCoordinator, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IOrderExecutionService _orders;
        private readonly IPaperTradingProvider _paper;
        private readonly ISettingsManager _settings;
        private readonly DemoPolicy _demo;
        private readonly ILogger<TradingReconciliationCoordinator> _logger;
        private readonly string _snapshotPath;

        // Providers already reconciled this session — announce once, not on every
        // reconnect blip (ConnectionManager already suppresses most of those).
        private readonly HashSet<string> _reconciled = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _reconciledLock = new();
        private readonly IDisposable _connectionSub;
        private readonly List<IDisposable> _fillSubs = new();
        private int _snapshotRefreshQueued;

        // Symbols already warned about margin this session. A symbol is removed when its
        // position recovers or closes, so a position that dips into danger, recovers, then
        // dips again warns each time — without spamming a warning on every 30s poll while
        // it sits in the danger zone.
        private readonly HashSet<string> _marginWarned = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _marginLock = new();
        private readonly System.Threading.CancellationTokenSource _monitorCts = new();

        // Warn when the live mark price is within this fraction of the liquidation price.
        private const double MarginWarnFraction = 0.15;

        internal sealed class SnapshotPosition
        {
            public string Symbol { get; set; } = "";
            public double Quantity { get; set; }
            public double AveragePrice { get; set; }
        }

        internal sealed class PositionSnapshot
        {
            public DateTime SavedAtUtc { get; set; }
            public Dictionary<string, List<SnapshotPosition>> ByProvider { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public TradingReconciliationCoordinator(
            IEventBus eventBus,
            IOrderExecutionService orders,
            IPaperTradingProvider paper,
            ISettingsManager settings,
            DemoPolicy demo,
            IPlatformPathService paths,
            ILogger<TradingReconciliationCoordinator> logger)
        {
            _eventBus = eventBus;
            _orders = orders;
            _paper = paper;
            _settings = settings;
            _demo = demo;
            _logger = logger;
            _snapshotPath = System.IO.Path.Combine(paths.AppDataDirectory, "position_snapshot.json");

            _connectionSub = eventBus.Subscribe<ConnectionStatusEvent>(e =>
            {
                if (e.State == ConnectionState.Connected)
                    _ = ReconcileLiveAsync(e.Provider);
            });

            // Any fill changes positions — refresh the snapshot (debounced) so a
            // close the user already HEARD live isn't re-reported next startup.
            _fillSubs.Add(eventBus.Subscribe<OrderFilledEvent>(_ => QueueSnapshotRefresh()));
            _fillSubs.Add(eventBus.Subscribe<StopHitEvent>(_ => QueueSnapshotRefresh()));
            _fillSubs.Add(eventBus.Subscribe<TakeProfitHitEvent>(_ => QueueSnapshotRefresh()));

            // Poll open positions for liquidation proximity. Margin risk grows from price
            // moves, not just fills, so it must be checked on a timer — connect/fill checks
            // alone would miss a position sliding toward liquidation on a quiet chart.
            _ = MonitorMarginAsync(_monitorCts.Token);
        }

        // Mirrors GeneralOrderService.IsPaperMode: hosted/demo builds force paper
        // regardless of the user setting.
        private bool IsPaperMode =>
            !_demo.AllowLiveTrading
            || (_settings.GetSetting(SettingsKeys.PaperTradingMode)?.ToObject<bool>() ?? false);

        public async Task AnnounceAtStartupAsync()
        {
            if (!IsPaperMode) return;
            try
            {
                var positions = await _paper.GetPositionsAsync().ConfigureAwait(false);
                var orders = await _paper.GetOpenOrdersAsync().ConfigureAwait(false);
                Announce("Paper account", positions.Count, orders.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Paper account reconciliation failed at startup");
            }
        }

        private async Task ReconcileLiveAsync(string provider)
        {
            // In paper mode every order routes to the paper broker, which was
            // already announced at startup — a live-provider connection carries
            // no tradable state of its own.
            if (IsPaperMode) return;

            lock (_reconciledLock)
            {
                if (!_reconciled.Add(provider)) return;
            }

            try
            {
                if (!await _orders.SupportsTradingAsync(provider).ConfigureAwait(false)) return;

                var positions = await _orders.GetPositionsAsync(provider).ConfigureAwait(false);
                var orders = await _orders.GetOpenOrdersAsync(provider).ConfigureAwait(false);
                Announce(provider, positions.Count, orders.Count);
                CheckMargin(provider, positions);

                await ReportClosedWhileAwayAsync(provider, positions).ConfigureAwait(false);
                SaveSnapshotFor(provider, positions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Broker reconciliation failed for {Provider}", provider);
            }
        }

        // ── While-you-were-away diff ─────────────────────────────────────────

        private async Task ReportClosedWhileAwayAsync(string provider, List<Position> current)
        {
            PositionSnapshot? snapshot = LoadSnapshot();
            if (snapshot == null || !snapshot.ByProvider.TryGetValue(provider, out var previous) || previous.Count == 0)
                return;

            foreach (var old in previous)
            {
                var now = current.Find(p => string.Equals(p.Symbol, old.Symbol, StringComparison.OrdinalIgnoreCase));
                double nowQty = now?.Quantity ?? 0;
                bool closed = Math.Abs(nowQty) < 1e-9 || Math.Sign(nowQty) != Math.Sign(old.Quantity);
                bool reduced = !closed && Math.Abs(nowQty) < Math.Abs(old.Quantity) - 1e-9;
                if (!closed && !reduced) continue;

                string detail = "";
                try
                {
                    // The closing fill is the freshest fill on the OPPOSITE side of
                    // the old position, after the snapshot was taken.
                    var fills = await _orders.GetFillsAsync(provider, old.Symbol, 20).ConfigureAwait(false);
                    var closingSide = old.Quantity > 0 ? OrderSide.Sell : OrderSide.Buy;
                    var closing = fills.Find(f =>
                        f.Side == closingSide && f.FilledAt >= snapshot.SavedAtUtc.AddMinutes(-5));
                    if (closing != null)
                    {
                        // Brokers that don't report realized P&L per fill get an
                        // approximation from the snapshot's average entry price:
                        // long → (exit − entry) × closed qty; short → inverted.
                        double closedQty = Math.Abs(old.Quantity) - Math.Abs(nowQty);
                        double perUnit = old.Quantity > 0
                            ? closing.Price - old.AveragePrice
                            : old.AveragePrice - closing.Price;
                        double pnl = closing.RealizedPnL != 0.0 ? closing.RealizedPnL : perUnit * closedQty;
                        string verb = closingSide == OrderSide.Sell ? "Sold" : "Bought";
                        string pnlText = $" {(pnl >= 0 ? "Profit" : "Loss")} {Accessibility.SpeechPriceFormatter.FormatPrice(Math.Abs(pnl))}.";
                        detail = $" {verb} at {Accessibility.SpeechPriceFormatter.FormatPrice(closing.Price)}.{pnlText}";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Closing-fill lookup failed for {Symbol} on {Provider}.", old.Symbol, provider);
                }

                string what = closed
                    ? $"{old.Symbol} position closed"
                    : $"{old.Symbol} position reduced to {Math.Abs(nowQty)}";
                string message = $"While you were away on {provider}: {what}.{detail}";
                _logger.LogInformation("Reconciliation: {Message}", message);
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.StateChange, message,
                    Interrupt: false, IsUserInitiated: false));
            }
        }

        // ── Snapshot persistence ─────────────────────────────────────────────

        private PositionSnapshot? LoadSnapshot()
        {
            try
            {
                if (!System.IO.File.Exists(_snapshotPath)) return null;
                return Newtonsoft.Json.JsonConvert.DeserializeObject<PositionSnapshot>(
                    System.IO.File.ReadAllText(_snapshotPath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Position snapshot could not be read; away-diff skipped.");
                return null;
            }
        }

        private void SaveSnapshotFor(string provider, List<Position> positions)
        {
            try
            {
                var snapshot = LoadSnapshot() ?? new PositionSnapshot();
                snapshot.SavedAtUtc = DateTime.UtcNow;
                snapshot.ByProvider[provider] = positions.ConvertAll(p => new SnapshotPosition
                {
                    Symbol = p.Symbol,
                    Quantity = p.Quantity,
                    AveragePrice = p.AveragePrice,
                });
                AtomicFile.WriteAllText(_snapshotPath,
                    Newtonsoft.Json.JsonConvert.SerializeObject(snapshot, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Position snapshot save failed for {Provider}.", provider);
            }
        }

        private void QueueSnapshotRefresh()
        {
            // Debounce: one refresh 5s after the last burst of fills.
            if (System.Threading.Interlocked.Exchange(ref _snapshotRefreshQueued, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    List<string> providers;
                    lock (_reconciledLock) providers = new List<string>(_reconciled);
                    foreach (var provider in providers)
                    {
                        try
                        {
                            var positions = await _orders.GetPositionsAsync(provider).ConfigureAwait(false);
                            SaveSnapshotFor(provider, positions);
                            CheckMargin(provider, positions);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Post-fill snapshot refresh failed for {Provider}.", provider);
                        }
                    }
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _snapshotRefreshQueued, 0);
                }
            });
        }

        // ── Margin / liquidation-proximity monitor ───────────────────────────

        private async Task MonitorMarginAsync(System.Threading.CancellationToken ct)
        {
            // 30s cadence balances early warning against position-endpoint rate limits;
            // connect and post-fill checks cover the moments in between.
            using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromSeconds(30));
            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        if (IsPaperMode)
                        {
                            var positions = await _paper.GetPositionsAsync().ConfigureAwait(false);
                            CheckMargin("Paper account", positions);
                        }
                        else
                        {
                            List<string> providers;
                            lock (_reconciledLock) providers = new List<string>(_reconciled);
                            foreach (var provider in providers)
                            {
                                var positions = await _orders.GetPositionsAsync(provider).ConfigureAwait(false);
                                CheckMargin(provider, positions);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Margin monitor tick failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        /// <summary>
        /// Publishes a <see cref="MarginWarningEvent"/> for any leveraged position whose
        /// live mark price is within <see cref="MarginWarnFraction"/> of its broker-supplied
        /// liquidation price. Debounced per provider+symbol (see <c>_marginWarned</c>) so the
        /// warning is spoken once per approach, not on every 30s poll. Spot holdings and
        /// providers that report no liquidation price are skipped.
        /// </summary>
        private void CheckMargin(string provider, IReadOnlyList<Position> positions)
        {
            // Keys (provider|symbol) still in the danger zone on THIS provider after this
            // pass. Anything previously warned for this provider but absent here — a position
            // that recovered or closed and dropped off the list entirely — has its debounce
            // flag cleared below so a fresh approach later warns again.
            var stillEndangered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in positions)
            {
                if (p.LiquidationPrice <= 0 || Math.Abs(p.Quantity) < 1e-9)
                    continue; // spot / no liquidation price — not a margin position

                // Recover the current mark from the position itself: MarketValue = qty × price.
                // Sign of either factor is irrelevant to the distance, hence Math.Abs. A
                // provider that leaves MarketValue at 0 yields price 0 and is skipped.
                double currentPrice = Math.Abs(p.MarketValue / p.Quantity);
                if (currentPrice <= 0) continue;

                double distanceFraction = Math.Abs(currentPrice - p.LiquidationPrice) / currentPrice;
                if (distanceFraction > MarginWarnFraction)
                    continue; // comfortably clear — reconcile step below re-arms it

                string key = MarginKey(provider, p.Symbol);
                stillEndangered.Add(key);
                bool firstTime;
                lock (_marginLock) firstTime = _marginWarned.Add(key);
                if (!firstTime) continue; // already announced; don't repeat every poll

                double pct = distanceFraction * 100.0;
                string side = p.Quantity > 0 ? "long" : "short";
                string message =
                    $"Margin warning: {p.Symbol} {side} on {provider} is within {pct:0.#} percent of its liquidation price " +
                    $"{Accessibility.SpeechPriceFormatter.FormatPrice(p.LiquidationPrice)}. " +
                    $"Current price {Accessibility.SpeechPriceFormatter.FormatPrice(currentPrice)}. " +
                    $"Reduce the position or add margin.";
                _logger.LogWarning("Margin warning: {Message}", message);
                _eventBus.Publish(new MarginWarningEvent(p.Symbol, distanceFraction, message));
            }

            // Re-arm: drop this provider's debounce flags for symbols no longer endangered
            // (recovered in place OR closed and gone from the list). Scoped by provider prefix
            // so one provider's pass never clears another provider's warnings.
            string providerPrefix = provider + '\u0001';
            lock (_marginLock)
            {
                _marginWarned.RemoveWhere(k =>
                    k.StartsWith(providerPrefix, StringComparison.Ordinal) && !stillEndangered.Contains(k));
            }
        }

        private static string MarginKey(string provider, string symbol) => provider + '\u0001' + symbol;

        private void Announce(string account, int positionCount, int orderCount)
        {
            if (positionCount == 0 && orderCount == 0) return;

            var parts = new List<string>(2);
            if (positionCount > 0)
                parts.Add(positionCount == 1 ? "1 open position" : $"{positionCount} open positions");
            if (orderCount > 0)
                parts.Add(orderCount == 1 ? "1 working order" : $"{orderCount} working orders");

            string message = $"{account}: {string.Join(" and ", parts)}. Press the trading dashboard shortcut to review.";
            _logger.LogInformation("Reconciliation: {Message}", message);
            _eventBus.Publish(new FeedbackRequestEvent(
                FeedbackType.StateChange, message,
                Interrupt: false, IsUserInitiated: false));
        }

        public void Dispose()
        {
            _monitorCts.Cancel();
            _monitorCts.Dispose();
            _connectionSub.Dispose();
            foreach (var sub in _fillSubs) sub.Dispose();
        }
    }
}
