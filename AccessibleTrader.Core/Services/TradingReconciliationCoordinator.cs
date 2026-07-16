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
    /// </summary>
    public sealed class TradingReconciliationCoordinator : ITradingReconciliationCoordinator, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IOrderExecutionService _orders;
        private readonly IPaperTradingProvider _paper;
        private readonly ISettingsManager _settings;
        private readonly DemoPolicy _demo;
        private readonly ILogger<TradingReconciliationCoordinator> _logger;

        // Providers already reconciled this session — announce once, not on every
        // reconnect blip (ConnectionManager already suppresses most of those).
        private readonly HashSet<string> _reconciled = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _reconciledLock = new();
        private readonly IDisposable _connectionSub;

        public TradingReconciliationCoordinator(
            IEventBus eventBus,
            IOrderExecutionService orders,
            IPaperTradingProvider paper,
            ISettingsManager settings,
            DemoPolicy demo,
            ILogger<TradingReconciliationCoordinator> logger)
        {
            _eventBus = eventBus;
            _orders = orders;
            _paper = paper;
            _settings = settings;
            _demo = demo;
            _logger = logger;

            _connectionSub = eventBus.Subscribe<ConnectionStatusEvent>(e =>
            {
                if (e.State == ConnectionState.Connected)
                    _ = ReconcileLiveAsync(e.Provider);
            });
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Broker reconciliation failed for {Provider}", provider);
            }
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

        public void Dispose() => _connectionSub.Dispose();
    }
}
