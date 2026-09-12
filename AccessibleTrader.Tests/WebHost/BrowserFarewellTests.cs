using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>"The browser is closed. The terminal keeps running."</b>
///
/// <para>
/// Cody, 2026-09-11: closing the browser should say so, because a blind user has no visual cue
/// that a background process survived it. The hard part is not the sentence, it is the trigger:
/// a connection goes down on a reload, a laptop sleep, a VPN flap and a pulled cable, and only
/// one of those means "the user has left". Every test here is about the trigger.
/// </para>
/// </summary>
[Collection("CircuitCoverage")]
public sealed class BrowserFarewellTests : IDisposable
{
    public BrowserFarewellTests() => BrowserPresence.ResetForTests();
    public void Dispose() => BrowserPresence.ResetForTests();

    private DateTime _now = new(2026, 9, 11, 21, 0, 0, DateTimeKind.Utc);
    private void Advance(TimeSpan by) => _now += by;

    private sealed class Rig : IDisposable
    {
        public readonly SpyPresenter Presenter = new();
        public readonly BrowserFarewellService Service;
        private readonly ServiceProvider _root;
        public readonly HeadlessSession Session;

        public Rig(bool monitoringOn)
        {
            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting(LocalBackgroundMonitor.SettingKey).Returns(JToken.FromObject(monitoringOn));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(settings);
            _root = services.BuildServiceProvider();
            Session = new HeadlessSession(_root.GetRequiredService<IServiceScopeFactory>(),
                                          NullLogger<HeadlessSession>.Instance);
            Service = new BrowserFarewellService(
                Session, new DemoPolicy(isDemo: false), Presenter,
                NullLogger<BrowserFarewellService>.Instance);
        }

        public void Dispose() { Session.Dispose(); _root.Dispose(); }
    }

    private void UseTestClock() => BrowserPresence.UtcNow = () => _now;

    // ── The happy path ───────────────────────────────────────────────────────

    [Fact]
    public void Closing_the_last_browser_says_the_terminal_keeps_running()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: true);

        BrowserPresence.Connected("c1");
        rig.Service.Check();
        Assert.Empty(rig.Presenter.Toasts);

        BrowserPresence.Disconnected("c1");
        Advance(BrowserPresence.Grace);
        rig.Service.Check();

        var toast = Assert.Single(rig.Presenter.Toasts);
        Assert.Equal("Accessible Trade Terminal", toast.Title);
        Assert.Contains("keeps running", toast.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_is_said_ONCE_however_many_polls_follow()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: true);

        BrowserPresence.Connected("c1");
        rig.Service.Check();
        BrowserPresence.Disconnected("c1");
        Advance(BrowserPresence.Grace);

        for (int i = 0; i < 20; i++) { rig.Service.Check(); Advance(TimeSpan.FromMinutes(1)); }

        Assert.Single(rig.Presenter.Toasts);
    }

    [Fact]
    public void Three_tabs_closed_together_are_one_farewell()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: true);

        BrowserPresence.Connected("a");
        BrowserPresence.Connected("b");
        BrowserPresence.Connected("c");
        rig.Service.Check();

        BrowserPresence.Disconnected("a");
        rig.Service.Check();
        BrowserPresence.Disconnected("b");
        rig.Service.Check();
        BrowserPresence.Disconnected("c");
        Advance(BrowserPresence.Grace);
        rig.Service.Check();

        Assert.Single(rig.Presenter.Toasts);
    }

    // ── The cases that must say NOTHING ──────────────────────────────────────

    [Fact]
    public void A_reload_says_nothing()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: true);

        BrowserPresence.Connected("old");
        rig.Service.Check();

        BrowserPresence.Disconnected("old");
        Advance(TimeSpan.FromSeconds(2));
        rig.Service.Check();
        BrowserPresence.Connected("new");
        Advance(TimeSpan.FromMinutes(5));
        rig.Service.Check();

        Assert.Empty(rig.Presenter.Toasts);
    }

    [Fact]
    public void A_short_network_blip_says_nothing()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: true);

        BrowserPresence.Connected("c1");
        rig.Service.Check();

        BrowserPresence.Disconnected("c1");
        Advance(TimeSpan.FromSeconds(5));
        rig.Service.Check();
        BrowserPresence.Connected("c1");
        Advance(TimeSpan.FromMinutes(5));
        rig.Service.Check();

        Assert.Empty(rig.Presenter.Toasts);
    }

    [Fact]
    public void One_tab_of_three_closing_says_nothing()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: true);

        BrowserPresence.Connected("a");
        BrowserPresence.Connected("b");
        rig.Service.Check();

        BrowserPresence.Disconnected("a");
        Advance(BrowserPresence.Grace + TimeSpan.FromMinutes(1));
        rig.Service.Check();

        Assert.Empty(rig.Presenter.Toasts);
    }

    /// <summary>
    /// A process that has never had a browser connected has nobody to say goodbye to. Without
    /// this, starting the WebHost from the tray would announce a farewell to a browser that was
    /// never opened.
    /// </summary>
    [Fact]
    public void A_process_that_never_saw_a_browser_says_nothing()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: true);

        for (int i = 0; i < 10; i++) { rig.Service.Check(); Advance(TimeSpan.FromMinutes(1)); }

        Assert.Empty(rig.Presenter.Toasts);
    }

    /// <summary>Reconnect, then close again: the second close is news again.</summary>
    [Fact]
    public void Closing_again_after_a_reconnect_says_it_again()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: true);

        BrowserPresence.Connected("c1");
        rig.Service.Check();
        BrowserPresence.Disconnected("c1");
        Advance(BrowserPresence.Grace);
        rig.Service.Check();
        Assert.Single(rig.Presenter.Toasts);

        BrowserPresence.Connected("c2");
        rig.Service.Check();
        BrowserPresence.Disconnected("c2");
        Advance(BrowserPresence.Grace);
        rig.Service.Check();

        Assert.Equal(2, rig.Presenter.Toasts.Count);
    }

    // ── It tells the truth about what happens next ───────────────────────────

    /// <summary>
    /// The master switch is off, so nothing WILL be watched. Saying "the terminal keeps running,
    /// alerts arrive here" would be a false promise at the exact moment the user loses the
    /// ability to check. A farewell that announces silence is more useful than no farewell.
    /// </summary>
    [Fact]
    public void With_monitoring_off_it_says_so_and_names_the_switch()
    {
        UseTestClock();
        using var rig = new Rig(monitoringOn: false);

        BrowserPresence.Connected("c1");
        rig.Service.Check();
        BrowserPresence.Disconnected("c1");
        Advance(BrowserPresence.Grace);
        rig.Service.Check();

        var toast = Assert.Single(rig.Presenter.Toasts);
        Assert.Contains("not watching anything", toast.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Keep monitoring when the browser is closed", toast.Text, StringComparison.Ordinal);
    }
}
