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
            _connectionSub.Dispose();
            foreach (var sub in _fillSubs) sub.Dispose();
        }
    }
}
