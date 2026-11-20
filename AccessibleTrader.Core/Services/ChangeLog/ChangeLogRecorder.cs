using System.Collections.Concurrent;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>
    /// Turns the events that already exist on the bus into change-log entries.
    ///
    /// <para>
    /// One recorder may live per browser circuit and one in the headless session. Both can see
    /// the same fill, so the store's key de-duplication — not an exclusive owner — is what keeps
    /// it to one entry. This class never throws into a bus subscriber: the append is
    /// fire-and-forget through <see cref="SafeFireAndForget"/>, and the store logs and swallows
    /// its own failures.
    /// </para>
    /// </summary>
    public sealed class ChangeLogRecorder : IDisposable
    {
        private readonly ChangeLogStore _store;
        private readonly TimeProvider _time;
        private readonly ILogger<ChangeLogRecorder> _logger;
        private readonly List<IDisposable> _subscriptions = new();

        /// <summary>Providers whose last recorded state was a problem, so a reconnect is a change
        /// and a first connect is not.</summary>
        private readonly ConcurrentDictionary<string, bool> _problem =
            new(StringComparer.OrdinalIgnoreCase);

        public ChangeLogRecorder(
            IEventBus bus,
            ChangeLogStore store,
            TimeProvider? time = null,
            ILogger<ChangeLogRecorder>? logger = null)
        {
            _store = store;
            _time = time ?? TimeProvider.System;
            _logger = logger ?? NullLogger<ChangeLogRecorder>.Instance;

            _subscriptions.Add(bus.Subscribe<AlertFiredEvent>(
                e => Append(MapAlert(e, NowUtc))));
            _subscriptions.Add(bus.Subscribe<OrderFilledEvent>(
                e => Append(MapOrderFilled(e, NowUtc))));
            _subscriptions.Add(bus.Subscribe<OrderPartialFillEvent>(
                e => Append(MapOrderPartialFill(e, NowUtc))));
            _subscriptions.Add(bus.Subscribe<StopHitEvent>(
                e => Append(MapStopHit(e, NowUtc))));
            _subscriptions.Add(bus.Subscribe<TakeProfitHitEvent>(
                e => Append(MapTakeProfitHit(e, NowUtc))));
            _subscriptions.Add(bus.Subscribe<MarketEventNoticeEvent>(e =>
            {
                if (MapMarketEvent(e, NowUtc) is { } entry) Append(entry);
            }));
            _subscriptions.Add(bus.Subscribe<HeadlineEvent>(
                e => Append(MapHeadline(e, NowUtc))));
            _subscriptions.Add(bus.Subscribe<ChartSignalEvent>(
                e => Append(MapSignal(e))));
            _subscriptions.Add(bus.Subscribe<BackgroundBarClosedEvent>(e =>
            {
                if (MapSignal(e) is { } entry) Append(entry);
            }));
            _subscriptions.Add(bus.Subscribe<BackgroundBricksFormedEvent>(e =>
            {
                if (MapSignal(e, NowUtc) is { } entry) Append(entry);
            }));
            _subscriptions.Add(bus.Subscribe<UnusualMoveEvent>(
                e => Append(MapUnusual(e))));
            _subscriptions.Add(bus.Subscribe<ConnectionStatusEvent>(e =>
            {
                bool wasProblem = e.State is ConnectionState.Error or ConnectionState.Disconnected;
                bool reconnect = e.State == ConnectionState.Connected
                                 && _problem.ContainsKey(e.Provider);

                if (wasProblem) _problem[e.Provider] = true;
                else if (e.State == ConnectionState.Connected) _problem.TryRemove(e.Provider, out _);

                if (wasProblem || reconnect)
                    Append(MapConnection(e, NowUtc));
            }));
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        private void Append(ChangeLogEntry entry)
            => SafeFireAndForget.Run(
                () => _store.AppendAsync(entry), _logger, "change-log append");

        // ── Pure mapping: one method per event, testable without a bus ───────────

        public static ChangeLogEntry MapAlert(AlertFiredEvent e, DateTime nowUtc)
            => MapAlert(e.Alert, nowUtc);

        public static ChangeLogEntry MapAlert(AlertFired alert, DateTime nowUtc)
            => new(
                ChangeLogKeys.Alert(alert, nowUtc),
                nowUtc,
                ChangeLogCategory.Alert,
                alert.SpeechText,
                alert.Symbol ?? alert.Definition?.Symbol,
                Heard: false);

        public static ChangeLogEntry MapOrderFilled(OrderFilledEvent e, DateTime nowUtc)
            => Money(
                e.Order,
                e.Provider,
                AccessibilityFeedbackCoordinator.FormatFill("Order filled", e.Order),
                nowUtc);

        public static ChangeLogEntry MapOrderPartialFill(OrderPartialFillEvent e, DateTime nowUtc)
            => Money(
                e.Order,
                e.Provider,
                AccessibilityFeedbackCoordinator.FormatPartialFill(e.Order),
                nowUtc);

        public static ChangeLogEntry MapStopHit(StopHitEvent e, DateTime nowUtc)
            => Money(
                e.Order,
                e.Provider,
                AccessibilityFeedbackCoordinator.FormatFill(
                    e.Order.Trailing ? "Trailing stop hit" : "Stop loss hit", e.Order),
                nowUtc);

        public static ChangeLogEntry MapTakeProfitHit(TakeProfitHitEvent e, DateTime nowUtc)
            => Money(
                e.Order,
                e.Provider,
                AccessibilityFeedbackCoordinator.FormatFill(
                    e.Order.Trailing ? "Trailing take profit hit" : "Take profit hit", e.Order),
                nowUtc);

        public static ChangeLogEntry? MapMarketEvent(MarketEventNoticeEvent e, DateTime nowUtc)
        {
            // A countdown is not a change; the read-out and the figure are.
            if (e.Kind is not (MarketEventNoticeKind.ReadOut or MarketEventNoticeKind.Figure))
                return null;

            return new ChangeLogEntry(
                ChangeLogKeys.Event(e.EventId, e.Kind),
                nowUtc,
                ChangeLogCategory.Event,
                e.Text,
                Symbol: null,
                Heard: false);
        }

        /// <summary>A headline that was said: kept under News, with its symbol.</summary>
        public static ChangeLogEntry MapHeadline(HeadlineEvent e, DateTime nowUtc)
            => new(
                ChangeLogKeys.Headline(e.Id),
                nowUtc,
                ChangeLogCategory.News,
                e.Text,
                e.Symbol,
                Heard: false);

        /// <summary>A browser-closed chart's ladder: already led by the chart's name.</summary>
        public static ChangeLogEntry MapSignal(ChartSignalEvent e)
            => new(
                ChangeLogKeys.Chart("signal", e.Identity, e.BarUtc),
                AsUtc(e.BarUtc, e.BarUtc),
                ChangeLogCategory.Signal,
                e.Text,
                e.Identity.Symbol,
                Heard: false);

        /// <summary>A background tab's close with a ladder; null without one.</summary>
        public static ChangeLogEntry? MapSignal(BackgroundBarClosedEvent e)
            => string.IsNullOrWhiteSpace(e.Narration)
                ? null
                : MapSignal(new ChartSignalEvent(e.Identity, e.ClosedBar.Date, Led(e.Identity, e.Narration!)));

        /// <summary>A background Renko tab's bricks with a ladder; keyed by the minute, since a
        /// brick has no bar time of its own.</summary>
        public static ChangeLogEntry? MapSignal(BackgroundBricksFormedEvent e, DateTime nowUtc)
            => string.IsNullOrWhiteSpace(e.Narration)
                ? null
                : MapSignal(new ChartSignalEvent(e.Identity, nowUtc, Led(e.Identity, e.Narration!)));

        public static ChangeLogEntry MapUnusual(UnusualMoveEvent e)
            => new(
                ChangeLogKeys.Chart("unusual", e.Identity, e.BarUtc),
                AsUtc(e.BarUtc, e.BarUtc),
                ChangeLogCategory.Unusual,
                e.Text,
                e.Identity.Symbol,
                Heard: false);

        /// <summary>"BTC/USDT 1h: ladder" — the browser-closed monitor's own lead.</summary>
        public static string Led(ChartIdentity identity, string ladder)
            => $"{identity.Symbol} {identity.Timeframe}: {ladder}";

        public static ChangeLogEntry MapConnection(ConnectionStatusEvent e, DateTime nowUtc)
        {
            string text = string.IsNullOrWhiteSpace(e.Message)
                ? $"{e.Provider} {e.State.ToString().ToLowerInvariant()}."
                : e.Message;

            return new ChangeLogEntry(
                ChangeLogKeys.Connection(e.Provider, e.State, nowUtc),
                nowUtc,
                ChangeLogCategory.Connection,
                text,
                Symbol: null,
                Heard: false);
        }

        private static ChangeLogEntry Money(
            OrderUpdate order, string? provider, string text, DateTime nowUtc)
            => new(
                ChangeLogKeys.Order(provider, order),
                AsUtc(order.Timestamp, nowUtc),
                ChangeLogCategory.Money,
                text,
                order.Symbol,
                Heard: false);

        private static DateTime AsUtc(DateTime value, DateTime nowUtc)
        {
            if (value == default) return nowUtc;
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                try { subscription.Dispose(); } catch { /* already gone */ }
            }
            _subscriptions.Clear();
        }
    }
}
