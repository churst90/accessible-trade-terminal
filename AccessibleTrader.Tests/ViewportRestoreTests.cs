using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the mechanism WorkspaceInitializer.RestoreViewportLength relies on: a saved zoom
/// width (ViewportLength) applied BEFORE the chart's initial data load must survive that
/// load and drive the live-edge window — rather than being reset to the default. Before the
/// fix the saved viewport was serialized but never re-applied, so every load/resume snapped
/// to the default zoom.
/// </summary>
public sealed class ViewportRestoreTests
{
    private static WorkspaceStore NewEmptyStore() => new(
        new SpyEventBus(),
        new ViewportRangeCalculator(),
        new ViewportNavigationService(),
        new VolumeStateService());

    private static TimeSeriesBuffer<Ohlcv> Bars(int n)
    {
        var bars = new List<Ohlcv>(n);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
            bars.Add(new Ohlcv(t0.AddMinutes(i), 100, 101, 99, 100, 1000));
        return new TimeSeriesBuffer<Ohlcv>(bars);
    }

    [Fact]
    public void Restored_zoom_width_survives_the_initial_data_load()
    {
        var store = NewEmptyStore();
        int defaultLength = store.State.ViewportLength;
        int restored = defaultLength + 137; // a distinct, non-default width

        // Simulates RestoreViewportLength running before any data is loaded.
        store.Dispatch(new ZoomAction(restored));
        // The chart's first data load then arrives.
        store.Dispatch(new UpdateDataAction(Bars(1000), IsInitialLoad: true));

        Assert.Equal(restored, store.State.ViewportLength); // NOT snapped back to default
        // The initial load opened at the live edge using the restored width, not the default.
        int effectiveWindow = restored - store.State.RightMarginBars;
        Assert.Equal(Math.Max(0, 1000 - effectiveWindow), store.State.ViewportStartIndex);
    }

    [Fact]
    public void Without_restore_the_initial_load_uses_the_default_width()
    {
        // Control: proves the test above isn't vacuously true — a fresh store loads at the
        // default width, which is what made the missing restore observable.
        var store = NewEmptyStore();
        int defaultLength = store.State.ViewportLength;

        store.Dispatch(new UpdateDataAction(Bars(1000), IsInitialLoad: true));

        Assert.Equal(defaultLength, store.State.ViewportLength);
    }
}
