using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the scroll-wheel zoom contract: the bar under the cursor stays fixed on screen
/// as the viewport shrinks or grows around it. Without this anchor-fraction math a
/// wheel-zoom would drift the focus (always re-centring at the live edge or viewport
/// start), which is unusable UX for zooming into a specific price-action region.
/// </summary>
public sealed class WheelZoomAnchorTests
{
    private static WorkspaceStore NewStoreWithBars()
    {
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 1000; i++)
        {
            bars.Add(new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                100, 101, 99, 100, 1000));
        }
        var store = new WorkspaceStore(
            new SpyEventBus(),
            new ViewportRangeCalculator(),
            new ViewportNavigationService(),
            new VolumeStateService());
        store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(bars), IsInitialLoad: true));
        return store;
    }

    /// <summary>
    /// The bar under the cursor at <paramref name="frac"/> of the viewport width before
    /// and after a wheel zoom must stay within one bar of each other. This is the
    /// anchor-fraction invariant — regardless of the exact length/start after clamping,
    /// the user perceives zoom as "pivoting around the cursor".
    /// </summary>
    private static void AssertAnchorPreserved(WorkspaceState before, WorkspaceState after, double frac, double tolerance = 1.5)
    {
        double beforeBar = before.ViewportStartIndex + frac * before.ViewportLength;
        double afterBar  = after.ViewportStartIndex  + frac * after.ViewportLength;
        Assert.InRange(afterBar, beforeBar - tolerance, beforeBar + tolerance);
    }

    [Fact]
    public void WheelZoom_in_actually_shrinks_the_viewport()
    {
        // Regression fence for the dead-action bug (2026-07-23): WheelZoomAction was
        // missing from WorkspaceStore's routing switch, so dispatching it was a no-op.
        // The anchor-invariant assertions below are trivially satisfied by a no-op
        // (before == after), so we MUST first prove the viewport actually changed.
        var store = NewStoreWithBars();
        var before = store.State;
        store.Dispatch(new WheelZoomAction(Direction: +1, AnchorFraction: 0.5));
        var after = store.State;
        Assert.True(after.ViewportLength < before.ViewportLength,
            "wheel zoom-in must reduce the viewport length — a no-op means the action isn't routed");
    }

    [Fact]
    public void WheelZoom_in_keeps_cursor_bar_fixed_on_screen()
    {
        // Cursor at 50% of viewport → zoom in → the bar under the cursor before and
        // after zoom should match (up to a bar of rounding error). This is the core
        // UX invariant for scroll-wheel zoom.
        var store = NewStoreWithBars();
        var before = store.State;
        store.Dispatch(new WheelZoomAction(Direction: +1, AnchorFraction: 0.5));
        var after = store.State;
        Assert.NotEqual(before.ViewportLength, after.ViewportLength); // guard against no-op
        AssertAnchorPreserved(before, after, 0.5);
    }

    [Fact]
    public void WheelZoom_out_actually_grows_the_viewport()
    {
        // Zoom out from a mid-history position must lengthen the viewport. Pinned as a
        // no-op guard alongside the anchor invariant (see zoom-in regression note).
        var store = NewStoreWithBars();
        store.Dispatch(new PanAction(-400)); // leave the live edge so there's room to grow
        var before = store.State;
        store.Dispatch(new WheelZoomAction(Direction: -1, AnchorFraction: 0.95));
        var after = store.State;
        Assert.True(after.ViewportLength > before.ViewportLength,
            "wheel zoom-out must grow the viewport length — a no-op means the action isn't routed");
    }

    [Fact]
    public void WheelZoom_out_keeps_right_edge_bar_fixed_on_screen()
    {
        // Cursor at 95% of viewport → zoom out → the right-edge bar should stay pinned
        // under the cursor, not re-centre to the viewport middle.
        var store = NewStoreWithBars();
        var before = store.State;
        store.Dispatch(new WheelZoomAction(Direction: -1, AnchorFraction: 0.95));
        var after = store.State;
        Assert.NotEqual(before.ViewportLength, after.ViewportLength); // guard against no-op
        AssertAnchorPreserved(before, after, 0.95, tolerance: 2.0);
    }

    [Fact]
    public void WheelZoom_respects_minimum_viewport_length()
    {
        // Repeated zoom-in should bottom out at the minimum allowed length (10). Prevents
        // a runaway wheel from collapsing the viewport to a single bar.
        var store = NewStoreWithBars();
        for (int i = 0; i < 50; i++)
        {
            store.Dispatch(new WheelZoomAction(+1, 0.5));
        }
        Assert.True(store.State.ViewportLength >= 10);
    }

    [Fact]
    public void WheelZoom_clamps_start_to_nonNegative()
    {
        // With the anchor near the left edge and zooming out, the computed start can go
        // negative; the reducer must clamp to 0.
        var store = NewStoreWithBars();
        // Pan left so we're not at the live edge, then zoom out with anchor near left.
        store.Dispatch(new PanAction(-800));
        store.Dispatch(new WheelZoomAction(Direction: -1, AnchorFraction: 0.05));
        Assert.True(store.State.ViewportStartIndex >= 0);
    }
}
