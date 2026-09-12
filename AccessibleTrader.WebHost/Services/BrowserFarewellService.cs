using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>"The browser is closed. The terminal keeps running."</b> One notification, once, when
    /// the last browser connection goes away.
    ///
    /// <para>
    /// Cody, 2026-09-11: <i>"When you close the browser/tab, it should send a notification toast
    /// that says 'Accessible Trade Terminal will continue running in the background and will
    /// receive all terminal events', or something more tasteful and informative."</i> A blind
    /// user closing a browser has no visual cue that a background process survived it, and the
    /// alternative to being told is discovering it by not hearing an alert.
    /// </para>
    ///
    /// <para>
    /// ── Why it hangs off the connected-circuit edge and not off a lifecycle method ─────
    /// <list type="bullet">
    ///   <item><c>OnConnectionDownAsync</c> fires on every network blip, laptop sleep and VPN
    ///   flap — the user would hear "continuing in the background" while looking at the
    ///   browser — and it fires once per tab, and it fires on a reload (the old circuit goes
    ///   down, a new one comes up a second later).</item>
    ///   <item><c>OnCircuitClosedAsync</c> fires about three minutes late, by which time the
    ///   headless side has been announcing for three minutes; it also fires three minutes after
    ///   a reload, with the user sitting in a live circuit, and again during shutdown.</item>
    ///   <item>A JavaScript <c>beforeunload</c> beacon can tell a close from a blip but not from
    ///   a reload, and does not fire at all on a crash, a <c>kill -9</c> or a pulled cable —
    ///   which are exactly the cases the background half exists for.</item>
    /// </list>
    /// So the trigger is <see cref="BrowserPresence"/>'s 1→0 edge plus its grace period, which
    /// is the one place that already knows what an edge is and how long to wait before believing
    /// it. Three tabs closed together are one edge and therefore one notification.
    /// </para>
    ///
    /// <para>
    /// ── It tells the truth about what will happen next ────────────────────────
    /// If the master switch is off, the notification says so and says which switch, because a
    /// farewell that announces silence is more useful than no farewell at all: the moment a user
    /// is about to stop being able to hear anything is the moment to tell them they will not.
    /// </para>
    /// </summary>
    public sealed class BrowserFarewellService : BackgroundService
    {
        /// <summary>How often the edge is re-examined. Short enough that the farewell lands
        /// promptly after the grace period, long enough to cost nothing.</summary>
        internal static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

        private readonly HeadlessSession _session;
        private readonly DemoPolicy _demo;
        private readonly IDesktopAlertPresenter _presenter;
        private readonly ILogger<BrowserFarewellService> _logger;

        /// <summary>Whether a browser has been connected since the last farewell. Starts false
        /// so a process that never sees a browser never says goodbye to one.</summary>
        private bool _sawABrowser;
        private bool _saidGoodbye;

        public BrowserFarewellService(
            HeadlessSession session,
            DemoPolicy demo,
            IDesktopAlertPresenter presenter,
            ILogger<BrowserFarewellService> logger)
        {
            _session = session;
            _demo = demo;
            _presenter = presenter;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if (_demo.IsDemo || _demo.IsHosted) return;   // local desktops only, like the monitor

            while (!ct.IsCancellationRequested)
            {
                try { Check(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Browser farewell check failed."); }

                try { await Task.Delay(Tick, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <remarks>Internal so a test can drive the edge without a 2-second wall clock.</remarks>
        internal void Check()
        {
            if (BrowserPresence.AnyConnected)
            {
                // A browser is here. Arm the farewell for the next time one is not.
                _sawABrowser = true;
                _saidGoodbye = false;
                return;
            }

            if (!_sawABrowser || _saidGoodbye) return;
            if (!BrowserPresence.HeadlessOwnsDelivery) return;   // still inside the grace period

            _saidGoodbye = true;
            var (title, body) = Compose();
            _logger.LogInformation("Browser closed; farewell: {Body}", body);
            DesktopAnnouncement.Present(_presenter, title, body, body,
                urgent: false, withSound: false, _logger);
        }

        /// <summary>
        /// What it says. Short, and honest about the one thing the user cannot check from here:
        /// whether anything is actually being watched.
        /// </summary>
        internal (string Title, string Body) Compose()
        {
            const string title = "Accessible Trade Terminal";

            if (!MonitoringIsOn())
                return (title,
                    "The browser is closed and the terminal is still running, but it is not watching anything. "
                    + "Turn on \"Keep monitoring when the browser is closed\" in Settings, General.");

            return (title,
                "The browser is closed. The terminal keeps running: alerts, order fills, bar closes and "
                + "narration for your saved charts arrive here as notifications until a browser connects again.");
        }

        private bool MonitoringIsOn()
        {
            try
            {
                // Off disk, not from the cached document: the user may have ticked the box in
                // the session they have just closed. See HeadlessSession.RefreshSettings.
                _session.RefreshSettings();
                return _session.Get<ISettingsManager>()
                    .GetSetting(LocalBackgroundMonitor.SettingKey)?.ToObject<bool>() ?? false;
            }
            catch (Exception ex)
            {
                // A settings read that fails must not turn the farewell into a false claim of
                // silence; say the useful thing and let the monitor's own logs carry the fault.
                _logger.LogDebug(ex, "Farewell could not read the monitoring switch.");
                return true;
            }
        }
    }
}
