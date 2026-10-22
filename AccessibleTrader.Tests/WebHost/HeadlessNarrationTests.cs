using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.WebHost.Services;
using AccessibleTrader.WebHost.Services.Tray;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>Phase 3 D4: the narration ladder with the browser closed, driven through the real
/// monitor poll.</b> Cody, 2026-09-11: <i>"I also want the narration ladder to also be spoken
/// when the browser is closed too."</i>
///
/// <para>
/// The harness registers the REAL indicator stack in the headless scope (Core + Skender
/// providers, engine, mapper, model factory, context analyser) and a provider whose clock the
/// test advances, so every case below is what actually reaches the desktop — and, as with every
/// delivery test since Phase 1, each is written with a browser circuit open and with none.
/// </para>
///
/// <para>
/// The second half pins the doubling Cody reported the same day — <i>"orca reads the
/// notification twice"</i> — through the same poll: on a machine with a notification tool the
/// notification is the whole announcement and nothing is spoken directly.
/// </para>
/// </summary>
[Collection("CircuitCoverage")]
public sealed class HeadlessNarrationTests : IDisposable
{
    public void Dispose() => CircuitAlertCoverage.ResetForTests();

    private static SeriesConfig SavedVolume(bool narrated = true)
    {
        var cfg = new SeriesConfig
        {
            Id = CoreSeriesIds.Volume, Name = "Volume", FriendlyName = "Volume", IndicatorCode = "VOLUME",
            Pane = "Volume", IsAutoNarrated = narrated, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Volume", DisplayName = "Volume", DisplayType = ComponentDisplayType.Bar,
            Role = ComponentRole.Volume, DataMapping = "volume", IsVisible = true,
        });
        return cfg;
    }

    private static IDisposable OpenCircuit(string id, params string[] symbols) =>
        CircuitAlertCoverage.Register(id, () => symbols);

    // ── The ladder, browser closed ────────────────────────────────────────────

    [Fact]
    public async Task With_no_browser_the_ladder_rides_the_bar_close_as_ONE_utterance()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() });
        h.NewBarToasts(true);

        await h.PollAsync();          // seeds; nothing
        Assert.Empty(h.Presenter.Spoken);

        h.CloseABar();
        await h.PollAsync();          // hour 99 closed at its final volume

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.StartsWith("BTC/USD 1h: close 100.00", one, StringComparison.Ordinal);
        Assert.Contains("Volume 100,000", one, StringComparison.Ordinal);
        Assert.True(one.IndexOf("New bar", StringComparison.Ordinal) < one.IndexOf("Volume 100,000", StringComparison.Ordinal),
            "the close first, the reading last: " + one);
        // And the toast carries the same sentence — it is the spoken route where the screen
        // reader reads notifications.
        var toast = Assert.Single(h.Presenter.Toasts);
        Assert.Equal(one, toast.Text);
    }

    /// <summary>
    /// Cody, 2026-10-22: <i>"if ha is on and a bar closes, speak that close."</i> With the browser
    /// closed too: a saved tab showing Heikin-Ashi says the Heikin-Ashi close and open. Hour 99
    /// wicks to 140 and closes at 100, so its Heikin-Ashi close is (100+140+100+100)/4 = 110, and
    /// the flat bar after it opens, Heikin-Ashi, at (100+110)/2 = 105. Run once with no saved
    /// series (the monitor's bare state) and once with a narrated volume pane (a headless chart).
    /// </summary>
    [Theory]
    [InlineData(false, false, "close 100.00", "open 100.00")]
    [InlineData(true, false, "close 110.00", "open 105.00")]
    [InlineData(true, true, "close 110.00", "open 105.00")]
    public async Task A_Heikin_Ashi_tab_says_the_Heikin_Ashi_close_with_the_browser_closed(
        bool heikinAshi, bool withChart, string close, string open)
    {
        using var h = new HeadlessMonitorHarness(withChart ? new[] { SavedVolume() } : Array.Empty<SeriesConfig>(),
            barAt: hr => hr == HeadlessMonitorHarness.StartHour ? (100, 140, 100, 100) : (100, 100, 100, 100),
            heikinAshi: heikinAshi);
        h.NewBarToasts(true);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = h.Single();
        Assert.Contains(close, one, StringComparison.Ordinal);
        Assert.Contains(open, one, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same with a narrated EMA on the tab: the headless chart's state carries the candle
    /// type, so the crossing the ladder narrates is the Heikin-Ashi close crossing the
    /// Heikin-Ashi EMA. Hour 99 closes flat (the scanner's first sighting of the EMA, which
    /// seeds); hour 100 wicks to 140 and closes at 100, level with a flat EMA on the real bars,
    /// so nothing crosses. On Heikin-Ashi it closes at 110, above an EMA 9 that moved a fifth of
    /// the way, to 102.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task A_Heikin_Ashi_tab_narrates_the_Heikin_Ashi_close_crossing_its_EMA(bool heikinAshi, bool crosses)
    {
        var ema = new SeriesConfig
        {
            Id = "ema9", IndicatorCode = "Ema", Name = "EMA", FriendlyName = "EMA 9",
            Pane = "Main", IsAutoNarrated = true, IsVisible = true,
        };
        ema.Parameters["lookbackPeriods"] = 9;
        using var h = new HeadlessMonitorHarness(new[] { ema },
            barAt: hr => hr == HeadlessMonitorHarness.StartHour + 1 ? (100, 140, 100, 100) : (100, 100, 100, 100),
            heikinAshi: heikinAshi);
        h.NewBarToasts(true);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Equal(2, h.Presenter.Spoken.Count);
        string last = h.Presenter.Spoken[^1];
        if (crosses) Assert.Contains("Price crossed above EMA 9 at 102.00", last, StringComparison.Ordinal);
        else Assert.DoesNotContain("crossed", last, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_new_bar_switch_off_the_ladder_still_speaks_led_by_the_symbol()
    {
        // In-session the ladder answers to the narration switches, not to the new-bar toast.
        // Headless the same: N on a volume pane is a request for the reading, whether or not
        // the user also wants "close … new bar" announced.
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() });
        h.NewBarToasts(false);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Equal("BTC/USD 1h: Volume 100,000, up.", one);
        // REVERSED 2026-09-11: a standalone ladder carries the same cue a bar close does. The
        // two used to disagree, which made the sound mean "a bar closed on a chart whose
        // timeframe clears the floor" rather than "something happened on a chart you cannot
        // see" — and in-session the background earcon plays for both.
        Assert.Equal(1, h.Presenter.SoundsPlayed);
    }

    [Fact]
    public async Task With_the_narration_master_switch_off_the_ladder_is_silent()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        h.NarrationMaster(false);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Contains("close 100.00", one, StringComparison.Ordinal);
        Assert.DoesNotContain("Volume", one, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The floor gates the ladder too, and that REVERSES what this test used to assert.</b>
    ///
    /// <para>The old rule was "the floor is new-bar announcements only", so a 1-minute chart
    /// under a 1-hour floor lost its bar close and went on reciting its volume every minute.
    /// The effect was that raising the floor to quieten a fast chart quietened half of it, while
    /// the settings hint promised silence. Cody, 2026-09-11: the floor means "do not talk to me
    /// about charts faster than this", and it means it about every sentence.</para>
    /// </summary>
    [Fact]
    public async Task The_timeframe_floor_gates_the_ladder_as_well_as_the_bar_close()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() }, timeframe: "1m");
        h.NewBarToasts(true);
        h.BarFloor("1h");

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Empty(h.Presenter.Spoken);
    }

    /// <summary>The vacuity floor for the test above: the same chart ABOVE the floor is loud.</summary>
    [Fact]
    public async Task A_chart_that_clears_the_floor_gets_both_the_close_and_the_ladder()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() }, timeframe: "1h");
        h.NewBarToasts(true);
        h.BarFloor("1h");

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Contains("close 100.00", one, StringComparison.Ordinal);
        Assert.Contains("Volume", one, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tab_with_nothing_under_N_is_never_narrated_and_costs_the_small_fetch()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume(narrated: false) });
        h.NewBarToasts(true);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.DoesNotContain("Volume", one, StringComparison.Ordinal);
        Assert.All(h.RequestedLimits, l => Assert.Equal(LocalBackgroundMonitor.MinFetch, l));
    }

    [Fact]
    public async Task The_first_fetch_asks_for_the_indicators_history_and_later_ones_for_three()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() });
        h.NewBarToasts(true);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Equal(3, h.RequestedLimits.Count);
        Assert.Equal(HeadlessChart.MinBars, h.RequestedLimits[0]);
        Assert.Equal(HeadlessChart.CatchUpLimit, h.RequestedLimits[1]);
        Assert.Equal(HeadlessChart.CatchUpLimit, h.RequestedLimits[2]);
        Assert.Equal(2, h.Presenter.Spoken.Count);
    }

    // ── Ownership: the browser covers what it covers ──────────────────────────

    [Fact]
    public async Task A_chart_an_open_circuit_covers_is_observed_silently_and_narrates_the_first_close_after_the_browser_goes()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() });
        h.NewBarToasts(true);

        var circuit = OpenCircuit("c1", "BTC/USD");
        await h.PollAsync();          // seeds while covered
        h.CloseABar();
        await h.PollAsync();          // a close while covered: the browser says it, not us
        Assert.Empty(h.Presenter.Spoken);
        Assert.Empty(h.Presenter.Toasts);

        circuit.Dispose();            // the browser closed
        h.CloseABar();
        await h.PollAsync();          // the FIRST close after the hand-off speaks — no re-seed

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Contains("Volume 101,000", one, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_circuit_on_another_symbol_does_not_silence_ours()
    {
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        using var _ = OpenCircuit("c1", "ETH/USD");

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Contains("Volume 100,000", Assert.Single(h.Presenter.Spoken), StringComparison.Ordinal);
    }

    // ── The doubling: "orca reads the notification twice" ─────────────────────

    [Fact]
    public async Task On_a_machine_with_a_notification_tool_the_notification_is_the_whole_announcement_and_nothing_is_spoken()
    {
        // Cody, 2026-09-11: "Pick the best path, orca/speech dispatcher or notification but not
        // both." Orca reads the MATE notification; speaking as well was every announcement twice.
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        h.Presenter.HasNotificationTool = true;

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Empty(h.Presenter.Spoken);
        var toast = Assert.Single(h.Presenter.Toasts);
        Assert.Contains("close 100.00", toast.Text, StringComparison.Ordinal);
        Assert.Contains("Volume 100,000", toast.Text, StringComparison.Ordinal);
        Assert.Equal(1, h.Presenter.SoundsPlayed);
    }

    [Fact]
    public async Task On_a_machine_with_no_notification_tool_the_sentence_is_spoken_instead()
    {
        // Direct speech is the fallback for the machine that cannot show a notification at all —
        // never a second voice beside one.
        using var h = new HeadlessMonitorHarness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        h.Presenter.HasNotificationTool = false;

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Contains("Volume 100,000", one, StringComparison.Ordinal);
    }
}
