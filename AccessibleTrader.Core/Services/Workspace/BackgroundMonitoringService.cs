using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Workspace
{
    public interface IBackgroundMonitoringService
    {
        /// <summary>True when the master setting is on AND the host policy allows it.</summary>
        bool IsEnabled { get; }
        /// <summary>Currently-running monitors (non-focused tabs under watch).</summary>
        IReadOnlyList<BackgroundWorkspaceMonitor> Monitors { get; }
        /// <summary>Re-reads settings + tab list and starts/stops monitors to match.</summary>
        void Reconcile();
        /// <summary>Speaks a one-utterance status summary ("the glance across monitors").</summary>
        void AnnounceStatus();
    }

    /// <summary>
    /// Keeps one <see cref="BackgroundWorkspaceMonitor"/> alive per NON-focused tab
    /// while background monitoring is enabled. The focused tab is never monitored —
    /// it is fully live through the normal pipeline. Monitors reconcile on every tab
    /// switch, tab add/close, and settings change:
    ///
    ///   tab focused   → its monitor stops (real pipeline takes over)
    ///   tab unfocused → a monitor spawns from its snapshot (identity + the user's
    ///                   own indicator setup, so alert/strategy leaves resolve against
    ///                   exactly the series the user configured on that chart)
    ///   tab closed    → its monitor stops
    ///
    /// Gating: DemoPolicy.AllowBackgroundMonitoring (desktop only) AND the
    /// "workspace.backgroundMonitoring" setting (default OFF — opt-in, like every
    /// resource-consuming feature in this terminal). Poll cadence is user-settable
    /// ("workspace.monitorPollSeconds", default 30, floor 10) and every fetch rides
    /// the provider's own rate limiter, so N monitors can never exceed a provider's
    /// request budget — they queue behind it instead.
    /// </summary>
    public sealed class BackgroundMonitoringService : IBackgroundMonitoringService, IDisposable
    {
        public const string EnabledKey = SettingsKeys.BackgroundMonitoring;
        public const string PollSecondsKey = SettingsKeys.MonitorPollSeconds;

        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly ISettingsManager _settings;
        private readonly DemoPolicy _policy;
        private readonly IMarketFeeds _feeds;
        private readonly IIndicatorService _indicators;
        private readonly IAlertOrchestrator _alerts;
        private readonly IAlertEvaluator _alertEvaluator;
        private readonly IStrategyEngine _strategyEngine;
        private readonly ILogger<BackgroundMonitoringService> _logger;
        private readonly IPaperTradingProvider? _paper;
        private readonly Strategies.IStrategyPositionManager? _positions;

        private readonly object _gate = new();
        // Keyed by identity — two tabs on the same identity coalesce into one monitor.
        private readonly Dictionary<ChartIdentity, BackgroundWorkspaceMonitor> _monitors = new();
        private readonly List<IDisposable> _subs = new();
        private bool _announcedEnable;

        public BackgroundMonitoringService(
            IWorkspaceStore store,
            IEventBus eventBus,
            ISettingsManager settings,
            DemoPolicy policy,
            IMarketFeeds feeds,
            IIndicatorService indicators,
            IAlertOrchestrator alerts,
            IAlertEvaluator alertEvaluator,
            IStrategyEngine strategyEngine,
            ILogger<BackgroundMonitoringService> logger,
            IPaperTradingProvider? paper = null,
            Strategies.IStrategyPositionManager? positions = null)
        {
            _positions = positions;
            _store = store;
            _eventBus = eventBus;
            _settings = settings;
            _policy = policy;
            _feeds = feeds;
            _indicators = indicators;
            _alerts = alerts;
            _alertEvaluator = alertEvaluator;
            _strategyEngine = strategyEngine;
            _logger = logger;
            _paper = paper;

            // Exposure changes what must be watched, so every order event is a
            // reconcile trigger: a new position starts a monitor, a closed one
            // stops it. Without this a position opened on the focused chart would
            // go unwatched the moment the user switched tabs.
            _subs.Add(eventBus.Subscribe<OrderFilledEvent>(_ => Reconcile()));
            _subs.Add(eventBus.Subscribe<OrderCancelledEvent>(_ => Reconcile()));
            // An expired order can have partially filled first — exposure may
            // have changed just like a cancel-after-partial.
            _subs.Add(eventBus.Subscribe<OrderExpiredEvent>(_ => Reconcile()));

            // Tab switches change which tab is "background"; snapshot-list changes
            // (add/close tab) change what exists to monitor.
            _subs.Add(eventBus.Subscribe<TabSwitchedEvent>(_ => Reconcile()));
            _subs.Add(eventBus.Subscribe<AnnounceMonitoringStatusEvent>(_ => AnnounceStatus()));
            _subs.Add(_store.StateStream
                .Select(s => (s.TabSnapshots?.Count ?? 0, s.ActiveTabIndex))
                .DistinctUntilChanged()
                .Subscribe(_ => Reconcile()));
        }

        public bool IsEnabled =>
            _policy.AllowBackgroundMonitoring
            && (_settings.GetSetting(EnabledKey)?.ToObject<bool>() ?? false);

        public IReadOnlyList<BackgroundWorkspaceMonitor> Monitors
        {
            get { lock (_gate) return _monitors.Values.ToList(); }
        }

        private TimeSpan PollInterval
        {
            get
            {
                int seconds = _settings.GetSetting(PollSecondsKey)?.ToObject<int>() ?? 30;
                return TimeSpan.FromSeconds(Math.Max(10, seconds));
            }
        }

        public void Reconcile()
        {
            lock (_gate)
            {
                var state = _store.State;
                var tabs = state.TabSnapshots ?? System.Collections.Immutable.ImmutableList<TabSnapshot>.Empty;

                // Discretionary monitoring: every inactive tab, when the user has
                // opted in. This is the resource-hungry half — N charts the user
                // merely has open — and it stays opt-in and desktop-only.
                var wanted = IsEnabled
                    ? tabs.Where(t => !string.IsNullOrWhiteSpace(t.Identity.Symbol))
                          .GroupBy(t => t.Identity) // coalesce duplicate identities → one monitor
                          .ToDictionary(g => g.Key, g => (Name: g.First().SymbolDisplayName, Series: g.First().ActiveSeries))
                    : new Dictionary<ChartIdentity, (string Name, ImmutableList<ChartSeries> Series)>();

                // Exposure monitoring: any chart the paper account has a position or
                // a resting order on, whether or not its tab is open and whether or
                // not discretionary monitoring is. Money at risk is not a power
                // feature — an order that cannot fill because the user looked at a
                // different chart is a broken order, and the position someone forgot
                // about is exactly the one that needs to keep reporting.
                //
                // It is bounded by real exposure rather than by how many charts are
                // open, which is what makes it affordable on the hosted host too.
                foreach (var id in ExposedIdentities())
                {
                    if (wanted.ContainsKey(id) || id.Equals(state.Identity)) continue;
                    // Prefer the user's own series setup for this chart when a tab
                    // still has one, so alerts and strategies resolve against exactly
                    // what was configured. With no tab there is nothing to evaluate —
                    // the monitor exists to fetch bars, and bars are what the fill
                    // engine and P&L need.
                    var snap = tabs.FirstOrDefault(t => t.Identity.Equals(id));
                    wanted[id] = (snap?.SymbolDisplayName ?? "", snap?.ActiveSeries ?? ImmutableList<ChartSeries>.Empty);
                }

                if (wanted.Count == 0)
                {
                    StopAllLocked(announce: _announcedEnable);
                    _announcedEnable = false;
                    return;
                }

                // Stop monitors whose tab was closed or became focused.
                foreach (var key in _monitors.Keys.ToList())
                {
                    if (!wanted.ContainsKey(key))
                    {
                        _monitors[key].Dispose();
                        _monitors.Remove(key);
                        _logger.LogInformation("Background monitor stopped: {Symbol}", key.Symbol);
                    }
                }

                // Start monitors for newly-backgrounded tabs.
                foreach (var (identity, snap) in wanted)
                {
                    if (_monitors.ContainsKey(identity)) continue;

                    var monitor = new BackgroundWorkspaceMonitor(
                        identity,
                        string.IsNullOrWhiteSpace(snap.Name) ? identity.Symbol : snap.Name,
                        snap.Series,
                        _feeds, _indicators, _alerts, _alertEvaluator,
                        _strategyEngine, _eventBus, _logger,
                        PollInterval, barsToFetch: 400, positions: _positions);
                    monitor.Start();
                    _monitors[identity] = monitor;
                    _logger.LogInformation("Background monitor started: {Symbol} ({Timeframe})",
                        identity.Symbol, identity.Timeframe);
                }

                if (!_announcedEnable && _monitors.Count > 0)
                {
                    _announcedEnable = true;
                    _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange,
                        $"Background monitoring on: watching {_monitors.Count} " +
                        $"{(_monitors.Count == 1 ? "workspace" : "workspaces")}.",
                        Interrupt: false, IsUserInitiated: false));
                }
            }
        }

        /// <summary>
        /// Charts the paper account has money on. Never throws into
        /// <see cref="Reconcile"/> — a broker that cannot answer must degrade to
        /// "no exposure", not take tab monitoring down with it.
        /// </summary>
        private IReadOnlyList<ChartIdentity> ExposedIdentities()
        {
            if (_paper == null) return Array.Empty<ChartIdentity>();
            try { return _paper.ExposedIdentities(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read paper exposure for monitoring.");
                return Array.Empty<ChartIdentity>();
            }
        }

        private void StopAllLocked(bool announce)
        {
            if (_monitors.Count == 0) return;
            foreach (var m in _monitors.Values) m.Dispose();
            _monitors.Clear();
            if (announce)
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange,
                    "Background monitoring off.", Interrupt: false, IsUserInitiated: false));
        }

        public void AnnounceStatus()
        {
            List<BackgroundWorkspaceMonitor> monitors;
            lock (_gate) monitors = _monitors.Values.ToList();

            // "Off" must not be said while monitors are running. With discretionary
            // monitoring off, exposure monitors can still be watching open positions
            // — reporting that as "off" would tell the user their forgotten trade is
            // unattended when it is not.
            if (!IsEnabled && monitors.Count == 0)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                    _policy.AllowBackgroundMonitoring
                        ? "Background monitoring is off, and no positions are open. Enable it in Settings, General."
                        : "Background monitoring is not available on this host.",
                    Interrupt: true));
                return;
            }

            if (!IsEnabled)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                    $"Background monitoring is off, but {monitors.Count} " +
                    $"{(monitors.Count == 1 ? "chart is" : "charts are")} watched for open paper positions and orders: " +
                    string.Join(", ", monitors.Select(m => m.SymbolDisplayName).OrderBy(s => s)) + ".",
                    Interrupt: true));
                return;
            }

            var sb = new StringBuilder();
            var focused = _store.State.SymbolDisplayName;
            sb.Append($"Watching {monitors.Count} background " +
                      $"{(monitors.Count == 1 ? "workspace" : "workspaces")} for alerts and strategy signals");
            sb.Append(string.IsNullOrWhiteSpace(focused) ? ". " : $"; {focused} is the focused chart. ");

            foreach (var m in monitors.OrderBy(m => m.SymbolDisplayName))
            {
                sb.Append(m.SymbolDisplayName);
                // "current" read as "the current tab" in live testing — say the
                // data's AGE instead, which is what this always meant.
                if (m.LastError != null) sb.Append(", data error");
                else if (m.LastEvaluatedUtc is DateTime t)
                {
                    var age = DateTime.UtcNow - t;
                    sb.Append(age.TotalSeconds < 90
                        ? ", checked under a minute ago"
                        : $", checked {(int)age.TotalMinutes} minute{((int)age.TotalMinutes == 1 ? "" : "s")} ago");
                }
                else sb.Append(", starting");

                int armedStrategies = _strategyEngine.ActiveStrategies.Count(a =>
                    !a.IsPaused && string.Equals(a.Symbol, m.SymbolDisplayName, StringComparison.OrdinalIgnoreCase));
                int armedAlerts = _alerts.GetAlerts().Count(a =>
                    a.IsActive && string.Equals(a.Symbol, m.SymbolDisplayName, StringComparison.OrdinalIgnoreCase));

                if (armedAlerts > 0) sb.Append($", {armedAlerts} alert{(armedAlerts == 1 ? "" : "s")}");
                if (armedStrategies > 0) sb.Append($", {armedStrategies} strateg{(armedStrategies == 1 ? "y" : "ies")}");
                // The teaching line: a monitored workspace with NOTHING armed is
                // silent by design — bar-by-bar announcements belong to the
                // focused chart. Say so, or the silence reads as a bug.
                if (armedAlerts == 0 && armedStrategies == 0)
                    sb.Append(", nothing armed — add an alert or strategy for this symbol to hear from it");
                sb.Append(". ");
            }

            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, sb.ToString().TrimEnd(), Interrupt: true));
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            lock (_gate) StopAllLocked(announce: false);
        }
    }
}
