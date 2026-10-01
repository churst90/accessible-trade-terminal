using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// What a drawing tool SAYS while it is being placed from the keyboard, and what shift+click
/// measuring says: the point just set, the point being asked for next, the point that finished
/// it, and which way price went.
///
/// <para>
/// A2q (2026-10-01). These sentences are the ONLY way a blind user knows where a multi-point
/// drawing is in its sequence, and none of them had a test that read its words: the campaign
/// had risk/reward ask for the stop loss again after the stop was set, the completion name the
/// wrong point, and a rally described as a fall, and all three survived.
/// </para>
/// </summary>
public sealed class DrawingPlacementSpeechTests
{
    private sealed class StubDrawingService : IDrawingService
    {
        public Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData) => new();
    }

    private sealed class Harness
    {
        public WorkspaceStore Store { get; }
        public BlazorInputService Input { get; } = new();
        public SpyEventBus Bus { get; } = new();
        public DrawingInteractionManager Manager { get; }
        public List<Ohlcv> Bars { get; } = new();

        /// <summary>200 one-minute bars whose close RISES by 1 every bar, from 100.</summary>
        public Harness()
        {
            Store = new WorkspaceStore(Bus, new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());
            for (int i = 0; i < 200; i++)
                Bars.Add(new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                    100 + i, 100.5 + i, 99.5 + i, 100 + i, 1000));
            Store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(Bars), IsInitialLoad: true));
            Manager = new DrawingInteractionManager(Bus, new StubDrawingService(), Store,
                new IndicatorModelFactory(new MockStylingService(), new MockIndicatorPreferencesService()), Input);
        }

        public void CursorTo(int bar) => Store.Dispatch(new NavigateAction(bar));

        /// <summary>The announcement the last key produced.</summary>
        public string Last => Bus.Log.OfType<AnnouncementEvent>().Last().Message;
    }

    // ── Risk / reward: entry, stop loss, take profit ─────────────────────────

    [Fact]
    public void After_the_stop_loss_is_set_risk_reward_asks_for_the_take_profit()
    {
        var h = new Harness();
        h.CursorTo(190);
        h.Manager.HandleAddDrawing("RiskReward", h.Bars);            // entry
        Assert.Contains("Navigate to the stop loss", h.Last, StringComparison.Ordinal);

        h.CursorTo(185);
        h.Manager.PlaceAnchorAtCursor(h.Bars);                        // stop loss

        Assert.Contains("stop loss at", h.Last, StringComparison.Ordinal);
        Assert.Contains("Navigate to the take profit", h.Last, StringComparison.Ordinal);
    }

    [Fact]
    public void Completing_risk_reward_names_the_take_profit_as_the_last_point()
    {
        var h = new Harness();
        h.CursorTo(190);
        h.Manager.HandleAddDrawing("RiskReward", h.Bars);
        h.CursorTo(185);
        h.Manager.PlaceAnchorAtCursor(h.Bars);
        h.CursorTo(195);
        h.Manager.PlaceAnchorAtCursor(h.Bars);                        // take profit at 295

        Assert.Contains("take profit at 295", h.Last, StringComparison.Ordinal);
        Assert.False(h.Manager.HasPendingDrawing);
    }

    // ── Two-point tools ──────────────────────────────────────────────────────

    [Fact]
    public void Completing_a_trend_line_names_its_second_point()
    {
        var h = new Harness();
        h.CursorTo(150);
        h.Manager.HandleAddDrawing("TrendLine", h.Bars);
        Assert.Contains("Navigate to the second point", h.Last, StringComparison.Ordinal);

        h.CursorTo(170);
        h.Manager.PlaceAnchorAtCursor(h.Bars);

        Assert.Contains("Trend line placed, second point at 270", h.Last, StringComparison.Ordinal);
    }

    // ── Shift+click measuring ────────────────────────────────────────────────

    [Fact]
    public void Measuring_from_the_cursor_to_a_later_higher_bar_says_up()
    {
        // The bars rise by 1 a bar: from bar 20 to any bar to its right is a rise.
        var h = new Harness();
        h.CursorTo(20);
        int target = ClickTarget(h, 1000);
        Assert.True(target > 20);

        h.Input.ProcessMouse(1000, 360, "MouseDown", 1280, 720);
        h.Input.ProcessMouse(1000, 360, "ShiftMouseUp", 1280, 720);

        Assert.StartsWith("Range:", h.Last, StringComparison.Ordinal);
        Assert.Contains("Change up", h.Last, StringComparison.Ordinal);
    }

    [Fact]
    public void Measuring_from_the_cursor_to_an_earlier_lower_bar_says_down()
    {
        var h = new Harness();
        h.CursorTo(h.Bars.Count - 1);
        int target = ClickTarget(h, 300);
        Assert.True(target < h.Bars.Count - 1);

        h.Input.ProcessMouse(300, 360, "MouseDown", 1280, 720);
        h.Input.ProcessMouse(300, 360, "ShiftMouseUp", 1280, 720);

        Assert.StartsWith("Range:", h.Last, StringComparison.Ordinal);
        Assert.Contains("Change down", h.Last, StringComparison.Ordinal);
    }

    private static int ClickTarget(Harness h, double x)
    {
        var s = h.Store.State;
        return ChartMath.MapXToIndex(x, 1280, s.ViewportStartIndex, s.ViewportLength);
    }
}
