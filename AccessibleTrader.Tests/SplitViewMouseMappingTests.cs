using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pointer coordinates map into the ACTIVE chart while split view is on.
    ///
    /// <para>
    /// The browser reports a position inside the whole canvas element, and every mapping
    /// downstream — bar index, price, drawing hit-tests, the context menu — assumes that canvas
    /// IS the chart. With split view on it is half of it, so a click landed on roughly the bar
    /// twice as far along as the cursor. That shipped as a stated limitation; this closes it.
    /// </para>
    ///
    /// <para>
    /// The second half of the contract matters as much as the first: a pointer over the divider
    /// or over the read-only second pane is DROPPED. Clicking the reference chart must never draw
    /// on the one being worked in — an action in the wrong place is far worse than no action.
    /// </para>
    /// </summary>
    public class SplitViewMouseMappingTests
    {
        private static DrawingInteractionManager Manager(ISplitViewCoordinator? split)
        {
            var input = Substitute.For<AccessibleTrader.Core.Services.IInputService>();
            return new DrawingInteractionManager(
                Substitute.For<AccessibleTrader.Core.Services.IEventBus>(),
                Substitute.For<AccessibleTrader.Core.Services.IDrawingService>(),
                Substitute.For<AccessibleTrader.Core.Services.IWorkspaceStore>(),
                Substitute.For<AccessibleTrader.Core.Services.IIndicatorModelFactory>(),
                input,
                paneLayout: null,
                settings: null,
                splitView: split);
        }

        private static ISplitViewCoordinator SplitAt(float left, float top, float width, float height)
        {
            var s = Substitute.For<ISplitViewCoordinator>();
            s.ActiveChartFraction.Returns((left, top, width, height));
            return s;
        }

        [Fact]
        public void WithoutSplitView_coordinatesArePassedThroughUntouched()
        {
            // The overwhelmingly common case has to cost nothing and change nothing.
            var m = Manager(SplitAt(0f, 0f, 1f, 1f));
            double x = 300, y = 200, w = 1000, h = 500;

            Assert.True(m.TryMapToActiveChart(ref x, ref y, ref w, ref h));
            Assert.Equal(300, x);
            Assert.Equal(200, y);
            Assert.Equal(1000, w);
            Assert.Equal(500, h);
        }

        [Fact]
        public void ANullCoordinatorMeansNoSplitView()
        {
            // Hosts and tests that never stand one up must behave exactly as before.
            var m = Manager(null);
            double x = 42, y = 7, w = 800, h = 400;

            Assert.True(m.TryMapToActiveChart(ref x, ref y, ref w, ref h));
            Assert.Equal(42, x);
        }

        [Fact]
        public void SideBySide_aClickInTheLeftPaneMapsToItsOwnSpace()
        {
            // Active chart is the left half. A click 100px in is 100px into the ACTIVE chart, and
            // that chart is 500 wide, not 1000 — both numbers have to change or the bar index is
            // computed against the wrong span.
            var m = Manager(SplitAt(0f, 0f, 0.5f, 1f));
            double x = 100, y = 200, w = 1000, h = 500;

            Assert.True(m.TryMapToActiveChart(ref x, ref y, ref w, ref h));
            Assert.Equal(100, x);
            Assert.Equal(500, w);
            Assert.Equal(500, h);
        }

        [Fact]
        public void SideBySide_aClickInTheRightPaneIsDropped()
        {
            // The right half is the read-only reference chart. Letting a click through would draw
            // on the active chart at a position the user never pointed at.
            var m = Manager(SplitAt(0f, 0f, 0.5f, 1f));
            double x = 800, y = 200, w = 1000, h = 500;

            Assert.False(m.TryMapToActiveChart(ref x, ref y, ref w, ref h));
        }

        [Fact]
        public void SideBySide_aClickOnTheDividerIsDropped()
        {
            // The gap between the panes belongs to neither.
            var m = Manager(SplitAt(0f, 0f, 0.497f, 1f));
            double x = 499, y = 100, w = 1000, h = 500;

            Assert.False(m.TryMapToActiveChart(ref x, ref y, ref w, ref h));
        }

        [Fact]
        public void Stacked_aClickInTheTopPaneMapsWithTheHeightHalved()
        {
            var m = Manager(SplitAt(0f, 0f, 1f, 0.5f));
            double x = 400, y = 100, w = 1000, h = 600;

            Assert.True(m.TryMapToActiveChart(ref x, ref y, ref w, ref h));
            Assert.Equal(400, x);
            Assert.Equal(100, y);
            Assert.Equal(300, h);
        }

        [Fact]
        public void AnActivePaneThatIsNotFirst_hasItsOriginSubtracted()
        {
            // Nothing guarantees the active chart is the left or top one — cycling the secondary
            // tab can put it second. A mapping that only ever divided the width would be right by
            // accident in one arrangement and wrong in the other.
            var m = Manager(SplitAt(0.5f, 0f, 0.5f, 1f));
            double x = 700, y = 250, w = 1000, h = 500;

            Assert.True(m.TryMapToActiveChart(ref x, ref y, ref w, ref h));
            Assert.Equal(200, x);       // 700 - 500
            Assert.Equal(250, y);
            Assert.Equal(500, w);
        }

        [Fact]
        public void ADegenerateFractionIsRefusedRatherThanDividingByZero()
        {
            var m = Manager(SplitAt(0f, 0f, 0f, 1f));
            double x = 10, y = 10, w = 100, h = 100;

            Assert.False(m.TryMapToActiveChart(ref x, ref y, ref w, ref h));
        }

        // ── The coordinator's half of the contract ───────────────────────

        [Fact]
        public void TheCoordinatorReportsTheWholeCanvasWhenSplitIsOff()
        {
            var coordinator = new SplitViewCoordinator(renderer: null);
            using var surface = SKSurface.Create(new SKImageInfo(800, 600));

            coordinator.Render(surface.Canvas, 800, 600, WorkspaceState.Initial, 1f);

            Assert.Equal((0f, 0f, 1f, 1f), coordinator.ActiveChartFraction);
        }

        [Fact]
        public void TheCoordinatorReportsAFractionNotPixels()
        {
            // Pointer coordinates arrive in CSS pixels and the canvas is painted in device pixels.
            // A pixel-valued rect would need a density this layer never sees; a fraction is the
            // same number in both spaces. Pinning the units because getting them wrong produces an
            // offset that only appears on a HiDPI screen.
            var coordinator = new SplitViewCoordinator(renderer: null);
            using var surface = SKSurface.Create(new SKImageInfo(800, 600));

            coordinator.Render(surface.Canvas, 800, 600, WorkspaceState.Initial, 2f);
            var f = coordinator.ActiveChartFraction;

            Assert.InRange(f.Width, 0f, 1f);
            Assert.InRange(f.Height, 0f, 1f);
        }
    }
}
