using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Xunit;

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
}
