using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// A venue's bar list caught in the seconds after a bar closed is set aside, and the next poll's
/// list is used. Seen on Bitstamp 2026-11-01 04:00 UTC: first the bar that had just closed was
/// missing, then the new bar was a zero-volume placeholder at a price five hours old. Before
/// this, the monitor re-announced the bar before the one that closed, never said the one that
/// did ("1h: close 77401.41 at 07:00" twice, no 08:00), and read the placeholder's stale price
/// as "New bar: open". See <see cref="VenueSettling"/>.
/// </summary>
[Collection("CircuitCoverage")]
public sealed class VenueSettlingTests
{
    private static readonly DateTime T = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);

    private static Ohlcv Bar(int hour, double price, double volume = 10)
        => new(T.AddHours(hour), price, price, price, price, volume);

    [Fact]
    public void A_list_missing_the_bar_that_was_forming_is_unsettled()
        // Last poll's newest was 03:00 (hour 0); now 02:00 is followed straight by 04:00.
        => Assert.True(VenueSettling.LooksUnsettled(new[] { Bar(-1, 1), Bar(1, 2) }, T));

    [Fact]
    public void A_zero_volume_new_bar_after_a_traded_one_is_unsettled()
        => Assert.True(VenueSettling.LooksUnsettled(new[] { Bar(0, 1), Bar(1, 2, volume: 0) }, T));

    [Fact]
    public void An_ordinary_close_is_settled()
        => Assert.False(VenueSettling.LooksUnsettled(new[] { Bar(0, 1), Bar(1, 2, volume: 0.0003) }, T));

    [Fact]
    public void Several_closes_since_the_last_poll_are_settled()
        // A laptop asleep for three hours: the forming bar is far behind, not missing.
        => Assert.False(VenueSettling.LooksUnsettled(new[] { Bar(0, 1), Bar(1, 2), Bar(2, 3), Bar(3, 4) }, T));

    [Fact]
    public void A_feed_that_never_reports_volume_is_settled()
        => Assert.False(VenueSettling.LooksUnsettled(new[] { Bar(0, 1, volume: 0), Bar(1, 2, volume: 0) }, T));

    [Fact]
    public void No_new_bar_is_settled()
        => Assert.False(VenueSettling.LooksUnsettled(new[] { Bar(-1, 1), Bar(0, 2, volume: 0) }, T));

    [Fact]
    public void One_new_bar_is_set_aside_at_most_MaxSkips_times()
    {
        var s = new VenueSettling();
        var unsettled = new[] { Bar(-1, 1), Bar(1, 2) };
        for (int i = 0; i < VenueSettling.MaxSkips; i++)
            Assert.True(s.ShouldSkip("k", unsettled, T), $"skip {i + 1}");
        Assert.False(s.ShouldSkip("k", unsettled, T));
    }

    [Fact]
    public void No_seen_bar_yet_is_never_set_aside()
        => Assert.False(new VenueSettling().ShouldSkip("k", new[] { Bar(-1, 1), Bar(1, 2) }, null));

    [Fact]
    public void The_hosted_monitor_sets_an_unsettled_list_aside_as_empty_not_as_a_failed_fetch()
    {
        // Empty is skipped quietly by HasComparableBars; null would report a dead feed.
        var m = new HostedAlertMonitor(
            NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            new AccessibleTrader.Core.Services.DemoPolicy(isDemo: false), usersRoot: "/nonexistent/users/root",
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<HostedAlertMonitor>.Instance);
        var chart = ("Bitstamp", "BTC/USD", "1h", "Spot");

        Assert.Equal(2, m.SetAsideIfUnsettled(chart, new List<Ohlcv> { Bar(-1, 1), Bar(0, 2) }).Count);   // seeds 03:00
        Assert.Empty(m.SetAsideIfUnsettled(chart, new List<Ohlcv> { Bar(-1, 1), Bar(1, 7, volume: 0) }));  // 03:00 gone
        Assert.Equal(3, m.SetAsideIfUnsettled(chart, new List<Ohlcv> { Bar(-1, 1), Bar(0, 2), Bar(1, 3) }).Count);
    }

    // ── The poll itself ─────────────────────────────────────────────────

    private static SeriesConfig Ema() => new() { Id = "e", Name = "EMA", IndicatorCode = "Ema", Pane = "Main" };

    private static HeadlessMonitorHarness Harness()
    {
        var h = new HeadlessMonitorHarness(new[] { Ema() }, timeframe: "1h", priceAt: hour => 100 + hour);
        h.Presenter.HasNotificationTool = true;
        return h;
    }

    /// <summary>What an untampered poll announces for the first bar to close: the sentence the
    /// tampered run must end up saying, once.</summary>
    private static async Task<string> TrueAnnouncementAsync()
    {
        using var h = Harness();
        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();
        return h.Single();
    }

    [Fact]
    public async Task A_close_whose_bar_is_missing_is_said_on_the_next_poll_not_as_the_bar_before()
    {
        string expected = await TrueAnnouncementAsync();
        using var h = Harness();
        await h.PollAsync();                                   // seeds: the forming bar is noted
        h.CloseABar();
        h.Tamper = bars => bars.Where((_, i) => i != bars.Count - 2).ToList();   // the bar that just closed is gone
        await h.PollAsync();
        h.Nothing();

        h.Tamper = null;
        await h.PollAsync();
        Assert.Equal(expected, h.Single());

        await h.PollAsync();
        Assert.Single(h.Delivered);
    }

    [Fact]
    public async Task A_placeholder_new_bar_at_a_stale_price_is_not_read_as_the_open()
    {
        string expected = await TrueAnnouncementAsync();
        using var h = Harness();
        await h.PollAsync();
        h.CloseABar();
        h.Tamper = bars => bars.Take(bars.Count - 1).Append(new Ohlcv(bars[^1].Date, 7, 7, 7, 7, 0)).ToList();
        await h.PollAsync();
        h.Nothing();

        h.Tamper = null;
        await h.PollAsync();
        Assert.Equal(expected, h.Single());
    }

    [Fact]
    public async Task A_bar_that_never_comes_back_is_not_announced_as_the_bar_before_it()
    {
        using var h = Harness();
        await h.PollAsync();
        h.CloseABar();
        h.Tamper = bars => bars.Where((_, i) => i != bars.Count - 2).ToList();
        for (int i = 0; i <= VenueSettling.MaxSkips + 1; i++) await h.PollAsync();
        h.Nothing();

        // The next close is said normally.
        h.Tamper = null;
        h.CloseABar();
        await h.PollAsync();
        Assert.Single(h.Delivered);
    }
}
