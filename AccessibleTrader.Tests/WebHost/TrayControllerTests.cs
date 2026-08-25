using AccessibleTrader.WebHost.Services;
using AccessibleTrader.WebHost.Services.Tray;

namespace AccessibleTrader.Tests.WebHost
{
    /// <summary>
    /// The platform-agnostic tray behaviour: each menu action against a fake platform, the
    /// snooze flag, the recent-alerts buffer states, and the dynamic labels. The OS glue
    /// (D-Bus / Win32 / AppKit) is thin and verified separately at runtime; this pins the
    /// logic every platform shares, so it's covered on Linux/Windows/macOS alike.
    /// </summary>
    public class TrayControllerTests
    {
        private sealed class FakePlatform : ITrayPlatform
        {
            public bool InitReturns = true;
            public TrayModel? Model;
            public readonly List<string> Spoken = new();
            public readonly List<string> Opened = new();
            public readonly List<string> Copied = new();
            public readonly List<string> Labels = new();

            public bool Initialize(TrayModel model) { Model = model; return InitReturns; }
            public void UpdateLabel(string title) => Labels.Add(title);
            public void Speak(string text) => Spoken.Add(text);
            public void OpenUrl(string url) => Opened.Add(url);
            public void CopyToClipboard(string text) => Copied.Add(text);
            public void Dispose() { }
        }

        private sealed class Harness
        {
            public FakePlatform Platform = new();
            public RecentAlertsBuffer Alerts = new();
            public AlertSnooze Snooze = new();
            public bool Monitoring;
            public int Armed;
            public bool Exited;
            public TrayController Controller = null!;

            public Harness Build()
            {
                Controller = new TrayController(Platform, new TrayController.Config
                {
                    BaseUrl = () => "http://localhost:5000/",
                    Alerts = Alerts,
                    Snooze = Snooze,
                    GetMonitoring = () => Monitoring,
                    SetMonitoring = v => Monitoring = v,
                    ArmedAlertCount = () => Armed,
                    Exit = () => Exited = true,
                });
                return this;
            }
        }

        [Fact]
        public void Start_shows_seven_items_and_the_icon()
        {
            var h = new Harness().Build();
            Assert.True(h.Controller.Start());
            Assert.NotNull(h.Platform.Model);
            Assert.Equal(7, h.Platform.Model!.Items.Count);
        }

        [Fact]
        public void OpenBrowser_opens_the_trimmed_base_url()
        {
            var h = new Harness().Build();
            h.Controller.OpenBrowser();
            Assert.Equal("http://localhost:5000", Assert.Single(h.Platform.Opened)); // trailing slash trimmed
        }

        [Fact]
        public void ShowAlerts_with_none_speaks_and_does_not_open()
        {
            var h = new Harness().Build();
            h.Controller.ShowAlerts();
            Assert.Contains("No new alerts.", h.Platform.Spoken);
            Assert.Empty(h.Platform.Opened);
        }

        [Fact]
        public void ShowAlerts_with_alerts_speaks_count_and_opens_the_page()
        {
            var h = new Harness().Build();
            h.Alerts.Add("Gold crossed 2500", "XAU/USD");
            h.Alerts.Add("BTC above 100k", "BTC/USD");
            h.Controller.ShowAlerts();
            Assert.Contains(h.Platform.Spoken, s => s.Contains("2 unread of 2 recent alerts"));
            Assert.Equal("http://localhost:5000/alerts/recent", Assert.Single(h.Platform.Opened));
        }

        [Fact]
        public void ToggleSilence_silences_then_resumes()
        {
            var h = new Harness().Build();
            h.Controller.ToggleSilence();
            Assert.True(h.Snooze.IsActive);
            Assert.Contains(h.Platform.Spoken, s => s.Contains("silenced for 30 minutes"));

            h.Controller.ToggleSilence();
            Assert.False(h.Snooze.IsActive);
            Assert.Contains("Alerts resumed.", h.Platform.Spoken);
        }

        [Fact]
        public void ToggleMonitoring_flips_the_setting_and_speaks()
        {
            var h = new Harness().Build();
            Assert.False(h.Monitoring);
            h.Controller.ToggleMonitoring();
            Assert.True(h.Monitoring);
            Assert.Contains(h.Platform.Spoken, s => s.Contains("Background monitoring on"));
        }

        [Fact]
        public void CopyAddress_copies_the_url_and_confirms()
        {
            var h = new Harness().Build();
            h.Controller.CopyAddress();
            Assert.Equal("http://localhost:5000", Assert.Single(h.Platform.Copied));
            Assert.Contains(h.Platform.Spoken, s => s.Contains("copied"));
        }

        [Fact]
        public void Exit_speaks_then_invokes_the_exit_callback()
        {
            var h = new Harness().Build();
            h.Controller.Exit();
            Assert.True(h.Exited);
            Assert.Contains(h.Platform.Spoken, s => s.Contains("Exiting"));
        }

        [Fact]
        public void SpeakStatus_reports_monitoring_armed_and_unread()
        {
            var h = new Harness().Build();
            h.Monitoring = true;
            h.Armed = 3;
            h.Alerts.Add("something", null);
            h.Controller.SpeakStatus();
            var status = Assert.Single(h.Platform.Spoken);
            Assert.Contains("Background monitoring is on", status);
            Assert.Contains("3 alerts armed", status);
            Assert.Contains("1 unread alert", status);
        }

        [Fact]
        public void Adding_an_alert_after_start_pushes_a_new_label_with_the_count()
        {
            var h = new Harness().Build();
            h.Controller.Start();
            h.Alerts.Add("Gold crossed 2500", "XAU/USD");
            Assert.Contains(h.Platform.Labels, l => l.Contains("1 new alert"));
        }

        [Fact]
        public void Menu_item_activation_routes_to_its_action()
        {
            // Proves the menu wiring, not just the action methods: activating item 7 (Exit)
            // runs the Exit action.
            var h = new Harness().Build();
            h.Controller.Start();
            var exitItem = h.Platform.Model!.Items.First(i => i.Id == 7);
            exitItem.OnActivate();
            Assert.True(h.Exited);
        }
    }
}
