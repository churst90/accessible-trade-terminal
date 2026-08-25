namespace AccessibleTrader.WebHost.Services.Tray
{
    /// <summary>
    /// Platform-agnostic tray behaviour: builds the menu, runs each action against injected
    /// callbacks, and keeps the icon's accessible label in sync with the unread-alert count.
    /// All OS specifics go through <see cref="ITrayPlatform"/>, so this whole class is unit
    /// tested with a fake platform. The concrete tray service just wires the callbacks to real
    /// settings / lifetime / URL and picks the platform for the current OS.
    /// </summary>
    public sealed class TrayController : IDisposable
    {
        public sealed class Config
        {
            public required Func<string> BaseUrl { get; init; }
            public required RecentAlertsBuffer Alerts { get; init; }
            public required AlertSnooze Snooze { get; init; }
            public required Func<bool> GetMonitoring { get; init; }
            public required Action<bool> SetMonitoring { get; init; }
            public required Func<int> ArmedAlertCount { get; init; }
            public required Action Exit { get; init; }
        }

        private readonly ITrayPlatform _platform;
        private readonly Config _c;
        private bool _started;

        public TrayController(ITrayPlatform platform, Config config)
        {
            _platform = platform;
            _c = config;
        }

        /// <summary>Builds the menu and shows the icon. Returns false if the platform couldn't.</summary>
        public bool Start()
        {
            var items = new List<TrayMenuItem>
            {
                new(1, () => "Restore workspaces to browser", OpenBrowser),
                new(2, () => "Show recent alerts", ShowAlerts),
                new(3, () => _c.Snooze.IsActive
                        ? $"Resume alerts (silenced, {_c.Snooze.RemainingMinutes} min left)"
                        : "Silence alerts for 30 minutes", ToggleSilence),
                new(4, () => "Connection status", SpeakStatus),
                new(5, () => "Copy terminal address", CopyAddress),
                new(6, () => _c.GetMonitoring()
                        ? "Turn background monitoring off"
                        : "Turn background monitoring on", ToggleMonitoring),
                new(7, () => "Exit terminal", Exit),
            };

            if (!_platform.Initialize(new TrayModel(BuildTitle(), items))) return false;
            _c.Alerts.Changed += OnAlertsChanged;
            _started = true;
            return true;
        }

        // ── Menu actions (each is independently unit-tested) ─────────────────

        internal void OpenBrowser() => _platform.OpenUrl(BaseUrl());

        internal void ShowAlerts()
        {
            int unread = _c.Alerts.UnreadCount;
            int total = _c.Alerts.Snapshot().Count;
            if (total == 0) { _platform.Speak("No new alerts."); return; }
            _platform.Speak(unread > 0
                ? $"{unread} unread of {total} recent alert{(total == 1 ? "" : "s")}."
                : $"{total} recent alert{(total == 1 ? "" : "s")}.");
            _platform.OpenUrl($"{BaseUrl()}/alerts/recent");
        }

        internal void ToggleSilence()
        {
            if (_c.Snooze.IsActive) { _c.Snooze.Resume(); _platform.Speak("Alerts resumed."); }
            else { _c.Snooze.SilenceFor(TimeSpan.FromMinutes(30)); _platform.Speak("Alerts silenced for 30 minutes."); }
        }

        internal void SpeakStatus() => _platform.Speak(BuildStatus());

        internal void CopyAddress()
        {
            _platform.CopyToClipboard(BaseUrl());
            _platform.Speak($"Terminal address copied: {BaseUrl()}");
        }

        internal void ToggleMonitoring()
        {
            bool next = !_c.GetMonitoring();
            _c.SetMonitoring(next);
            _platform.Speak(next
                ? "Background monitoring on. Alerts will speak with the browser closed."
                : "Background monitoring off.");
        }

        internal void Exit()
        {
            _platform.Speak("Exiting Accessible Trade Terminal.");
            _c.Exit();
        }

        // ── Labels ───────────────────────────────────────────────────────────

        internal string BuildTitle()
        {
            int n = _c.Alerts.UnreadCount;
            return n > 0
                ? $"Accessible Trade Terminal — {n} new alert{(n == 1 ? "" : "s")}"
                : "Accessible Trade Terminal";
        }

        internal string BuildStatus()
        {
            var parts = new List<string>();
            parts.Add(_c.GetMonitoring() ? "Background monitoring is on." : "Background monitoring is off.");
            if (_c.Snooze.IsActive) parts.Add($"Alerts silenced for {_c.Snooze.RemainingMinutes} more minutes.");
            int armed = _c.ArmedAlertCount();
            parts.Add(armed == 1 ? "1 alert armed." : $"{armed} alerts armed.");
            int unread = _c.Alerts.UnreadCount;
            if (unread > 0) parts.Add(unread == 1 ? "1 unread alert." : $"{unread} unread alerts.");
            return string.Join(" ", parts);
        }

        private string BaseUrl() => _c.BaseUrl().TrimEnd('/');

        private void OnAlertsChanged() => _platform.UpdateLabel(BuildTitle());

        public void Dispose()
        {
            if (_started) _c.Alerts.Changed -= OnAlertsChanged;
            _platform.Dispose();
        }
    }
}
