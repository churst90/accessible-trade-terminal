using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// A fixed-range profile must not move when the viewport does.
///
/// <para>
/// <b>The defect.</b> The rule deciding whether a profile follows the viewport was
/// <c>!code.Contains("FIXED")</c>. <c>"VPFR"</c> does not contain the string <c>"FIXED"</c>, so the
/// condition was true for it and the Fixed Range profile was sliced to the viewport exactly like the
/// Visible Range one. Two catalogue entries, two descriptions, one behaviour — and nothing failed,
/// because both produced a perfectly valid profile.
/// </para>
///
/// <para>
/// Anchors are timestamps rather than bar indices on purpose: loading older history shifts every
/// index, which would slide an index-anchored profile onto a different stretch of chart without
/// anyone touching it.
/// </para>
/// </summary>
public class ProfileAnchoringTests
{
    private static List<Ohlcv> Bars(int n)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000))
            .ToList();
    }

    [Fact]
    public void VisibleRangeFollowsTheViewportAndFixedRangeDoesNot()
    {
        Assert.True(ProfileAnchoring.FollowsViewport("VPVR"));
        Assert.False(ProfileAnchoring.FollowsViewport("VPFR"));
    }

    /// <summary>A market profile is read against what you are looking at.</summary>
    [Fact]
    public void MarketProfileFollowsTheViewport()
        => Assert.True(ProfileAnchoring.FollowsViewport("TPO"));

    [Theory]
    [InlineData("vpfr")]
    [InlineData("VPVR")]
    [InlineData("vpvr")]
    public void TheCodeComparisonIsCaseInsensitive(string code)
        => Assert.Equal(code.Equals("VPVR", StringComparison.OrdinalIgnoreCase),
                        ProfileAnchoring.FollowsViewport(code));

    [Fact]
    public void AnAnchorCapturesExactlyTheViewportItWasCreatedFrom()
    {
        var bars = Bars(100);
        var p = new Dictionary<string, double>();

        ProfileAnchoring.CaptureAnchor(p, bars, viewportStart: 20, viewportLength: 30);
        var slice = ProfileAnchoring.SliceToAnchor(bars, p);

        Assert.Equal(30, slice.Count);
        Assert.Equal(bars[20].Date, slice[0].Date);
        Assert.Equal(bars[49].Date, slice[^1].Date);
    }

    /// <summary>The whole point: it stays put while you look elsewhere.</summary>
    [Fact]
    public void TheAnchoredSliceIsIdenticalNoMatterWhereTheViewportGoes()
    {
        var bars = Bars(100);
        var p = new Dictionary<string, double>();
        ProfileAnchoring.CaptureAnchor(p, bars, 20, 30);

        var first = ProfileAnchoring.SliceToAnchor(bars, p).Select(b => b.Date).ToList();
        // Panning does not touch the parameters, so re-slicing must give the same bars.
        var second = ProfileAnchoring.SliceToAnchor(bars, p).Select(b => b.Date).ToList();

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Loading older history prepends bars and shifts every index. A timestamp anchor must still
    /// select the same bars; an index anchor would silently have moved.
    /// </summary>
    [Fact]
    public void LoadingOlderHistoryDoesNotSlideTheAnchor()
    {
        var bars = Bars(100);
        var p = new Dictionary<string, double>();
        ProfileAnchoring.CaptureAnchor(p, bars, 20, 30);
        var before = ProfileAnchoring.SliceToAnchor(bars, p).Select(b => b.Date).ToList();

        var older = Bars(1).Select(b => new Ohlcv(b.Date.AddDays(-50), 1, 1, 1, 1, 1)).ToList();
        var extended = older.Concat(bars).ToList();

        var after = ProfileAnchoring.SliceToAnchor(extended, p).Select(b => b.Date).ToList();
        Assert.Equal(before, after);
    }

    /// <summary>
    /// With no anchor, every loaded bar is used. That is still fixed in the sense that matters — it
    /// does not follow the viewport — and it covers workspaces saved before anchoring existed.
    /// </summary>
    [Fact]
    public void NoAnchorMeansEveryLoadedBar()
    {
        var bars = Bars(40);
        Assert.Equal(40, ProfileAnchoring.SliceToAnchor(bars, new Dictionary<string, double>()).Count);
        Assert.Equal(40, ProfileAnchoring.SliceToAnchor(bars, null).Count);
    }

    /// <summary>
    /// An anchor pointing outside the loaded data falls back to everything rather than rendering
    /// nothing — a blank pane is indistinguishable from a broken indicator.
    /// </summary>
    [Fact]
    public void AnAnchorThatSelectsNothingFallsBackRatherThanRenderingEmpty()
    {
        var bars = Bars(40);
        var p = new Dictionary<string, double>
        {
            [ProfileAnchoring.AnchorStartParam] = ProfileAnchoring.ToUnix(new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            [ProfileAnchoring.AnchorEndParam]   = ProfileAnchoring.ToUnix(new DateTime(1990, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
        };

        Assert.Equal(40, ProfileAnchoring.SliceToAnchor(bars, p).Count);
    }

    [Fact]
    public void AReversedAnchorIsReadTheRightWayRound()
    {
        var bars = Bars(100);
        var p = new Dictionary<string, double>
        {
            [ProfileAnchoring.AnchorStartParam] = ProfileAnchoring.ToUnix(bars[49].Date),
            [ProfileAnchoring.AnchorEndParam]   = ProfileAnchoring.ToUnix(bars[20].Date),
        };

        Assert.Equal(30, ProfileAnchoring.SliceToAnchor(bars, p).Count);
    }

    [Fact]
    public void EmptyDataIsHandledWithoutThrowing()
    {
        var p = new Dictionary<string, double>();
        ProfileAnchoring.CaptureAnchor(p, new List<Ohlcv>(), 0, 10);
        Assert.Empty(p);
        Assert.Empty(ProfileAnchoring.SliceToAnchor(new List<Ohlcv>(), p));
    }

    // ── The eight codes, and the four windows (2026-09-05) ─────────────────────

    /// <summary>
    /// One idea crossed with another: four windows by two measures. Every cell exists, every
    /// cell is a profile, and the window and the measure are read from the code — not from a
    /// string guess, which is the defect this class was born from.
    /// </summary>
    [Theory]
    [InlineData("VPVR",       ProfileWindow.Visible,  false)]
    [InlineData("VPFR",       ProfileWindow.Fixed,    false)]
    [InlineData("VPSESSION",  ProfileWindow.Session,  false)]
    [InlineData("VPANCHOR",   ProfileWindow.Anchored, false)]
    [InlineData("TPO",        ProfileWindow.Visible,  true)]
    [InlineData("TPOFR",      ProfileWindow.Fixed,    true)]
    [InlineData("TPOSESSION", ProfileWindow.Session,  true)]
    [InlineData("TPOANCHOR",  ProfileWindow.Anchored, true)]
    public void EveryCellOfTheGrid_IsAProfileWithItsWindowAndMeasure(string code, ProfileWindow window, bool countsTime)
    {
        Assert.True(ProfileAnchoring.IsProfileCode(code));
        Assert.True(ProfileAnchoring.IsProfileCode(code.ToLowerInvariant()));
        Assert.Equal(window, ProfileAnchoring.WindowOf(code));
        Assert.Equal(countsTime, ProfileAnchoring.CountsTime(code));
        // Visible and session profiles are picked BY the viewport; fixed and anchored are not.
        Assert.Equal(window is ProfileWindow.Visible or ProfileWindow.Session,
                     ProfileAnchoring.FollowsViewport(code));
        Assert.Contains(code, ProfileAnchoring.AllCodes);
    }

    [Fact]
    public void TheCatalogueRegistersExactlyTheEight_UnderProfile()
    {
        var metas = new AccessibleTrader.Core.Services.Indicators.ProfileIndicatorProvider().GetIndicators();
        Assert.Equal(ProfileAnchoring.AllCodes, metas.Select(m => m.Code).ToList());
        Assert.All(metas, m => Assert.Equal("Profile", m.Category));
        // Every name is distinct: two entries with the same spoken name is the old VPVR/VPFR
        // defect in a new coat — two descriptions the ear cannot tell apart.
        Assert.Equal(metas.Count, metas.Select(m => m.Name).Distinct().Count());
    }

    [Theory]
    [InlineData("VOLUME PROFILE", false)]
    [InlineData("MARKET PROFILE", true)]
    [InlineData("EMA", null)]
    public void ALegacyProfileCodeStillLoadsAsOne_AndAnIndicatorDoesNot(string code, bool? countsTime)
    {
        if (countsTime is null)
        {
            Assert.False(ProfileAnchoring.IsProfileCode(code));
            return;
        }
        Assert.True(ProfileAnchoring.IsProfileCode(code));
        Assert.Equal(ProfileWindow.Visible, ProfileAnchoring.WindowOf(code));
        Assert.Equal(countsTime.Value, ProfileAnchoring.CountsTime(code));
    }

    private static List<Ohlcv> HourlyBars(int days)
    {
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, days * 24)
            .Select(i => new Ohlcv(start.AddHours(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000))
            .ToList();
    }

    /// <summary>A session is the calendar day of the LAST visible bar — the day you have panned to.</summary>
    [Fact]
    public void ASessionSlice_IsTheDayOfTheLastVisibleBar()
    {
        var bars = HourlyBars(3); // 72 bars, three UTC days
        // Viewport ends at bar 40 (day 2, 16:00): the session is all 24 bars of day 2.
        var slice = ProfileAnchoring.SliceToSession(bars, viewportStart: 20, viewportLength: 21);
        Assert.Equal(24, slice.Count);
        Assert.All(slice, b => Assert.Equal(new DateTime(2026, 3, 2), b.Date.Date));
        // Pan into day 3 and the profile follows.
        var next = ProfileAnchoring.SliceToSession(bars, viewportStart: 50, viewportLength: 20);
        Assert.All(next, b => Assert.Equal(new DateTime(2026, 3, 3), b.Date.Date));
    }

    /// <summary>An anchored profile has a start and no end: it runs to the newest bar and grows.</summary>
    [Fact]
    public void AnAnchoredProfile_RunsFromTheChosenBarToTheNewestOne_AndGrows()
    {
        var bars = Bars(100);
        var p = new Dictionary<string, double>();
        ProfileAnchoring.CaptureAnchorStart(p, bars, barIndex: 30);
        Assert.False(p.ContainsKey(ProfileAnchoring.AnchorEndParam));

        var slice = ProfileAnchoring.SliceToAnchor(bars, p);
        Assert.Equal(70, slice.Count);
        Assert.Equal(bars[30].Date, slice[0].Date);
        Assert.Equal(bars[^1].Date, slice[^1].Date);

        // Five more bars arrive: the anchor stays, the window grows.
        var grown = bars.Concat(Enumerable.Range(100, 5)
            .Select(i => new Ohlcv(bars[0].Date.AddDays(i), 1, 1, 1, 1, 1))).ToList();
        Assert.Equal(75, ProfileAnchoring.SliceToAnchor(grown, p).Count);
    }

    /// <summary>
    /// The one entry point the orchestrator and the backtester share, so a window means the
    /// same thing on both. Each code goes to its own slice.
    /// </summary>
    [Fact]
    public void Slice_DispatchesEachCodeToItsWindow()
    {
        var bars = HourlyBars(3);
        var fixedRange = new Dictionary<string, double>();
        ProfileAnchoring.CaptureAnchor(fixedRange, bars, 10, 5);
        var anchored = new Dictionary<string, double>();
        ProfileAnchoring.CaptureAnchorStart(anchored, bars, 60);

        Assert.Equal(21, ProfileAnchoring.Slice("VPVR", bars, null, 20, 21).Count);
        Assert.Equal(21, ProfileAnchoring.Slice("TPO", bars, null, 20, 21).Count);
        Assert.Equal(24, ProfileAnchoring.Slice("VPSESSION", bars, null, 20, 21).Count);
        Assert.Equal(24, ProfileAnchoring.Slice("TPOSESSION", bars, null, 20, 21).Count);
        Assert.Equal(5,  ProfileAnchoring.Slice("VPFR", bars, fixedRange, 20, 21).Count);
        Assert.Equal(5,  ProfileAnchoring.Slice("TPOFR", bars, fixedRange, 20, 21).Count);
        Assert.Equal(12, ProfileAnchoring.Slice("VPANCHOR", bars, anchored, 20, 21).Count);
        Assert.Equal(12, ProfileAnchoring.Slice("TPOANCHOR", bars, anchored, 20, 21).Count);
        // A caller with no viewport (the backtester) passes the whole buffer: the visible
        // window is everything and the session is the newest one.
        Assert.Equal(72, ProfileAnchoring.Slice("VPVR", bars, null, 0, bars.Count).Count);
        Assert.Equal(24, ProfileAnchoring.Slice("VPSESSION", bars, null, 0, bars.Count).Count);
        Assert.All(ProfileAnchoring.Slice("VPSESSION", bars, null, 0, bars.Count),
                   b => Assert.Equal(new DateTime(2026, 3, 3), b.Date.Date));
    }
}
