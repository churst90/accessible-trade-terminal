using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coverage for <see cref="SplitViewCoordinator"/>'s geometry and tab selection.
    ///
    /// The rendering itself is exercised against a real <see cref="SKSurface"/> so the clip and
    /// translate maths are proven rather than assumed — a split that silently drew both charts
    /// on top of each other at the origin would look plausible in a unit test that only checked
    /// call counts.
    /// </summary>
    public class SplitViewCoordinatorTests
    {
        private static TabSnapshot Snapshot(int index, string symbol, int bars = 50)
        {
            var start = new DateTime(2026, 1, 1);
            var list = new List<Ohlcv>();
            for (int i = 0; i < bars; i++)
                list.Add(new Ohlcv(start.AddDays(i), 100, 101, 99, 100, 10));

            return new TabSnapshot(
                TabIndex: index,
                Identity: new ChartIdentity("Spot", "Binance", symbol, "1d"),
                Data: new TimeSeriesBuffer<Ohlcv>(list),
                ActiveSeries: ImmutableList<ChartSeries>.Empty,
                FocusedSeriesIndex: 0,
                FocusedSeriesId: null,
                FocusedComponentIndex: 0,
                FocusedBinIndex: -1,
                CurrentDataIndex: bars - 1,
                ViewportStartIndex: 0,
                ViewportLength: 50,
                RightMarginBars: 10,
                ViewportRange: (99, 101),
                PaneRanges: ImmutableDictionary<string, (double, double)>.Empty,
                IsHeikinAshi: false,
                IsLogScale: false,
                LastInteractionContext: InteractionContext.Series,
                PaneHeightRatios: null,
                IndicatorPaneScrollIndex: 0,
                InitStatus: InitializationStatus.Ready,
                DataStatus: DataStatus.Ready,
                IsCoordinateEntryMode: false,
                PendingDrawingTool: null,
                CoordinateEntryAnchorCount: 0,
                CoordinateEntryAnchor1Index: -1);
        }

        private static WorkspaceState StateWith(params TabSnapshot[] snapshots) =>
            WorkspaceState.Initial with
            {
                ActiveTabIndex = 0,
                TabSnapshots = ImmutableList.CreateRange(snapshots),
            };

        // Null renderer: these tests are about layout, tab selection and canvas-state balance,
        // none of which depend on pixels. Standing up a real ChartRenderer would drag in six
        // services and test nothing extra.
        private static SplitViewCoordinator Build() => new(renderer: null);

        // ── Geometry ──────────────────────────────────────────────────────────

        [Fact]
        public void SideBySide_SplitsHorizontallyIntoTwoEqualHalves()
        {
            var c = Build();
            Assert.True(c.TrySplit(1000, 600, out var primary, out var secondary));

            Assert.Equal(0, primary.Left);
            Assert.Equal(600, primary.Height);
            Assert.Equal(600, secondary.Height);
            Assert.Equal(primary.Width, secondary.Width, 3);
            // The divider gap must actually separate them.
            Assert.True(secondary.Left >= primary.Right);
            Assert.Equal(SplitViewCoordinator.DividerPx, secondary.Left - primary.Right, 3);
        }

        [Fact]
        public void Stacked_SplitsVerticallyIntoTwoEqualHalves()
        {
            var c = Build();
            c.ToggleOrientation();
            Assert.Equal(SplitOrientation.Stacked, c.Orientation);

            Assert.True(c.TrySplit(1000, 600, out var primary, out var secondary));
            Assert.Equal(1000, primary.Width);
            Assert.Equal(1000, secondary.Width);
            Assert.Equal(primary.Height, secondary.Height, 3);
            Assert.Equal(SplitViewCoordinator.DividerPx, secondary.Top - primary.Bottom, 3);
        }

        [Fact]
        public void TrySplit_RefusesWhenAPaneWouldBeUnreadablySmall()
        {
            var c = Build();
            Assert.False(c.TrySplit(150, 600, out _, out _));
        }

        // ── Tab selection ─────────────────────────────────────────────────────

        [Fact]
        public void Toggle_WithNoOtherTab_StaysOff()
        {
            var c = Build();
            c.Toggle(StateWith(Snapshot(0, "BTC/USDT")));

            Assert.False(c.IsEnabled);
            Assert.Equal(-1, c.SecondaryTabIndex);
        }

        [Fact]
        public void Toggle_PicksTheFirstInactiveTab()
        {
            var c = Build();
            c.Toggle(StateWith(Snapshot(0, "BTC/USDT"), Snapshot(1, "ETH/USDT"), Snapshot(2, "SOL/USDT")));

            Assert.True(c.IsEnabled);
            Assert.Equal(1, c.SecondaryTabIndex);
        }

        [Fact]
        public void Toggle_Twice_ReturnsToSingleChart()
        {
            var state = StateWith(Snapshot(0, "BTC/USDT"), Snapshot(1, "ETH/USDT"));
            var c = Build();

            c.Toggle(state);
            c.Toggle(state);

            Assert.False(c.IsEnabled);
            Assert.Equal(-1, c.SecondaryTabIndex);
        }

        [Fact]
        public void CycleSecondary_WrapsThroughInactiveTabsAndSkipsTheActiveOne()
        {
            var state = StateWith(Snapshot(0, "BTC/USDT"), Snapshot(1, "ETH/USDT"), Snapshot(2, "SOL/USDT"));
            var c = Build();
            c.Toggle(state);
            Assert.Equal(1, c.SecondaryTabIndex);

            c.CycleSecondary(state);
            Assert.Equal(2, c.SecondaryTabIndex);

            c.CycleSecondary(state);
            Assert.Equal(1, c.SecondaryTabIndex); // wrapped, never landing on the active tab 0
        }

        // ── Render behaviour ──────────────────────────────────────────────────

        [Fact]
        public void Render_WhenOff_ReportsTheFullCanvasAsTheActiveRegion()
        {
            var c = Build();
            using var surface = SKSurface.Create(new SKImageInfo(800, 600));

            var rect = c.Render(surface.Canvas, 800, 600, StateWith(Snapshot(0, "BTC/USDT")), 1f);

            Assert.Equal(new SKRect(0, 0, 800, 600), rect);
        }

        [Fact]
        public void Render_WhenOn_ReportsOnlyTheActiveHalf()
        {
            // Hit-testing and pointer mapping consume this rect; returning the full canvas
            // while drawing into half of it would put every mouse coordinate in the wrong bar.
            var state = StateWith(Snapshot(0, "BTC/USDT"), Snapshot(1, "ETH/USDT"));
            var c = Build();
            c.Toggle(state);

            using var surface = SKSurface.Create(new SKImageInfo(800, 600));
            var rect = c.Render(surface.Canvas, 800, 600, state, 1f);

            Assert.True(rect.Width < 800);
            Assert.Equal(0, rect.Left);
            Assert.Equal(600, rect.Height);
        }

        [Fact]
        public void Render_FallsBackToFullSize_WhenTheCanvasIsTooNarrowToSplit()
        {
            var state = StateWith(Snapshot(0, "BTC/USDT"), Snapshot(1, "ETH/USDT"));
            var c = Build();
            c.Toggle(state);

            using var surface = SKSurface.Create(new SKImageInfo(150, 600));
            var rect = c.Render(surface.Canvas, 150, 600, state, 1f);

            // A squashed unreadable pane is worse than staying single.
            Assert.Equal(new SKRect(0, 0, 150, 600), rect);
        }

        [Fact]
        public void Render_FallsBackToFullSize_WhenTheSecondaryTabHasNoBars()
        {
            var empty = Snapshot(1, "ETH/USDT", bars: 0);
            var state = StateWith(Snapshot(0, "BTC/USDT"), empty);
            var c = Build();
            c.Toggle(state);

            using var surface = SKSurface.Create(new SKImageInfo(800, 600));
            var rect = c.Render(surface.Canvas, 800, 600, state, 1f);

            // An empty second pane reads as a broken pane, so it is not drawn at all.
            Assert.Equal(new SKRect(0, 0, 800, 600), rect);
        }

        [Fact]
        public void Render_LeavesTheCanvasTransformUnchanged()
        {
            // The clip/translate must be balanced: leaking a translate would shift every
            // subsequent overlay draw by half the canvas.
            var state = StateWith(Snapshot(0, "BTC/USDT"), Snapshot(1, "ETH/USDT"));
            var c = Build();
            c.Toggle(state);

            using var surface = SKSurface.Create(new SKImageInfo(800, 600));
            var before = surface.Canvas.TotalMatrix;
            c.Render(surface.Canvas, 800, 600, state, 1f);

            Assert.Equal(before, surface.Canvas.TotalMatrix);
        }
    }
}
