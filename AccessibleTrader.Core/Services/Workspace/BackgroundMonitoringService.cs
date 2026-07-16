using System;
using System.Collections.Generic;
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
        public const string EnabledKey = "workspace.backgroundMonitoring";
        public const string PollSecondsKey = "workspace.monitorPollSeconds";

        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly ISettingsManager _settings;
        private readonly DemoPolicy _policy;
        private readonly IDataService _dataService;
        private readonly IIndicatorService _indicators;
        private readonly IAlertOrchestrator _alerts;
        private readonly IAlertEvaluator _alertEvaluator;
        private readonly IStrategyEngine _strategyEngine;
        private readonly ILogger<BackgroundMonitoringService> _logger;

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
            IDataService dataService,
            IIndicatorService indicators,
            IAlertOrchestrator alerts,
            IAlertEvaluator alertEvaluator,
            IStrategyEngine strategyEngine,
            ILogger<BackgroundMonitoringService> logger)
        {
            _store = store;
            _eventBus = eventBus;
            _settings = settings;
            _policy = policy;
            _dataService = dataService;
            _indicators = indicators;
            _alerts = alerts;
            _alertEvaluator = alertEvaluator;
            _strategyEngine = strategyEngine;
            _logger = logger;

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
                if (!IsEnabled)
                {
                    StopAllLocked(announce: _announcedEnable);
                    _announcedEnable = false;
                    return;
                }

                var state = _store.State;
                // Every inactive tab with a real symbol is a monitoring candidate.
                var wanted = (state.TabSnapshots ?? System.Collections.Immutable.ImmutableList<TabSnapshot>.Empty)
                    .Where(t => !string.IsNullOrWhiteSpace(t.Identity.Symbol))
                    .GroupBy(t => t.Identity) // coalesce duplicate identities → one monitor
                    .ToDictionary(g => g.Key, g => g.First());

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
                        string.IsNullOrWhiteSpace(snap.SymbolDisplayName) ? identity.Symbol : snap.SymbolDisplayName,
                        snap.ActiveSeries,
                        _dataService, _indicators, _alerts, _alertEvaluator,
                        _strategyEngine, _eventBus, _logger,
                        PollInterval);
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

            if (!IsEnabled)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                    _policy.AllowBackgroundMonitoring
                        ? "Background monitoring is off. Enable it in Settings, General."
                        : "Background monitoring is not available on this host.",
                    Interrupt: true));
                return;
            }

            var sb = new StringBuilder();
            var focused = _store.State.SymbolDisplayName;
            sb.Append($"Monitoring {monitors.Count} background " +
                      $"{(monitors.Count == 1 ? "workspace" : "workspaces")}");
            sb.Append(string.IsNullOrWhiteSpace(focused) ? ". " : $", plus {focused} live on screen. ");

            foreach (var m in monitors.OrderBy(m => m.SymbolDisplayName))
            {
                sb.Append(m.SymbolDisplayName);
                if (m.LastError != null) sb.Append(", data error");
                else if (m.LastEvaluatedUtc is DateTime t)
                {
                    var age = DateTime.UtcNow - t;
                    sb.Append(age.TotalSeconds < 90
                        ? ", current"
                        : $", last checked {(int)age.TotalMinutes} minutes ago");
                }
                else sb.Append(", starting");

                int armed = _strategyEngine.ActiveStrategies.Count(a =>
                    !a.IsPaused && string.Equals(a.Symbol, m.SymbolDisplayName, StringComparison.OrdinalIgnoreCase));
                if (armed > 0) sb.Append($", {armed} strateg{(armed == 1 ? "y" : "ies")}");
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
