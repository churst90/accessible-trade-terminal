using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase B (2026-04-27 e19) keyboard-close routing.
    ///
    /// <para>
    /// The audit found Escape didn't close any modal — `keyboard.js` forwarded the
    /// keystroke but `CommandDispatcher` had no handler. This was patched by adding
    /// `SystemCommand.CloseModal`, routing Escape through it whenever any modal is
    /// open, and tracking a modal-name stack so `CloseTopModalEvent` targets only
    /// the topmost open modal.
    /// </para>
    ///
    /// These tests pin that wiring so a future refactor can't silently revert it.
    /// </summary>
    public class ModalCloseDispatchTests
    {
        private static (CommandDispatcher dispatcher, IEventBus bus, List<CloseTopModalEvent> closeEvents)
            BuildDispatcher(WorkspaceState? initialState = null)
        {
            var bus = new EventBus();
            var nav = Substitute.For<INavigationEngine>();
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(_ => initialState ?? WorkspaceState.Initial);
            var bar = Substitute.For<IBarDetailService>();
            var crossing = new IndicatorCrossingEngine(store, bus);

            var dispatcher = new CommandDispatcher(bus, nav, store, bar, crossing);

            var captured = new List<CloseTopModalEvent>();
            bus.Subscribe<CloseTopModalEvent>(captured.Add);
            return (dispatcher, bus, captured);
        }

        [Fact]
        public void CloseModal_PublishesEvent_WhenSingleModalOpen()
        {
            var (dispatcher, bus, captured) = BuildDispatcher();
            bus.Publish(new ModalStateChangedEvent(true, "Help"));

            dispatcher.Dispatch(SystemCommand.CloseModal);

            Assert.Single(captured);
            Assert.Equal("Help", captured[0].ModalName);
        }

        [Fact]
        public void CloseModal_TargetsTopmostName_WhenStacked()
        {
            var (dispatcher, bus, captured) = BuildDispatcher();
            // Stack: Strategy ← Help (topmost). Pressing Escape should target Help.
            bus.Publish(new ModalStateChangedEvent(true, "Strategy manager"));
            bus.Publish(new ModalStateChangedEvent(true, "Help"));

            dispatcher.Dispatch(SystemCommand.CloseModal);
            Assert.Single(captured);
            Assert.Equal("Help", captured[0].ModalName);

            // Help closes; Strategy is now topmost.
            bus.Publish(new ModalStateChangedEvent(false, "Help"));

            dispatcher.Dispatch(SystemCommand.CloseModal);
            Assert.Equal(2, captured.Count);
            Assert.Equal("Strategy manager", captured[1].ModalName);
        }

        [Fact]
        public void EscapeAsCancelDrawing_ReroutesToCloseModal_WhenModalOpen()
        {
            var (dispatcher, bus, captured) = BuildDispatcher();
            bus.Publish(new ModalStateChangedEvent(true, "Help"));

            // Keyboard binding for Escape is `CancelDrawing`. With a modal open the
            // dispatcher rewrites it to CloseModal so Esc closes the modal instead
            // of cancelling a non-existent drawing placement.
            dispatcher.Dispatch(SystemCommand.CancelDrawing);

            Assert.Single(captured);
            Assert.Equal("Help", captured[0].ModalName);
        }

        [Fact]
        public void OpenDrawingContextMenu_FiresEvent_ForFocusedDrawing()
        {
            // Build state with one drawing focused.
            var drawingSeries = new ChartSeries(
                new SeriesConfig
                {
                    Id = "drw-1",
                    Name = "Trendline",
                    FriendlyName = "Trendline",
                    Pane = "main",
                    IndicatorCode = "TrendLine",
                },
                new SeriesDataBuffer { SeriesId = "drw-1" })
            {
                Drawing = new DrawingData
                {
                    Type = DrawingType.TrendLine,
                    AnchorDate1 = System.DateTime.UtcNow,
                    AnchorPrice1 = 100.0,
                    AnchorDate2 = System.DateTime.UtcNow.AddMinutes(5),
                    AnchorPrice2 = 105.0,
                }
            };
            var state = WorkspaceState.Initial with
            {
                FocusedSeriesId = "drw-1",
                ActiveSeries = ImmutableList.Create(drawingSeries)
            };
            var (dispatcher, bus, _) = BuildDispatcher(state);
            dispatcher.SetChartActive(true);

            var captured = new List<OpenDrawingContextMenuEvent>();
            bus.Subscribe<OpenDrawingContextMenuEvent>(captured.Add);

            dispatcher.Dispatch(SystemCommand.OpenDrawingContextMenu);

            Assert.Single(captured);
            Assert.Equal("drw-1", captured[0].SeriesId);
            // Sentinel NaN coords signal "self-position centrally" to the menu component.
            Assert.True(double.IsNaN(captured[0].ViewportX));
            Assert.True(double.IsNaN(captured[0].ViewportY));
        }

        [Fact]
        public void OpenDrawingContextMenu_OpensChartMenu_WhenNoDrawingFocused()
        {
            // Phase B: the Application key with no drawing focused used to speak an
            // error ("No drawing focused."); it now opens the chart-level context
            // menu — full keyboard parity with mouse right-click on empty chart space.
            var (dispatcher, bus, _) = BuildDispatcher();
            dispatcher.SetChartActive(true);

            var drawingMenu = new List<OpenDrawingContextMenuEvent>();
            var chartMenu = new List<OpenChartContextMenuEvent>();
            bus.Subscribe<OpenDrawingContextMenuEvent>(drawingMenu.Add);
            bus.Subscribe<OpenChartContextMenuEvent>(chartMenu.Add);

            dispatcher.Dispatch(SystemCommand.OpenDrawingContextMenu);

            Assert.Empty(drawingMenu);
            var ev = Assert.Single(chartMenu);
            // Sentinel NaN coords = "self-position centrally" (keyboard origin).
            Assert.True(double.IsNaN(ev.ViewportX));
            Assert.True(double.IsNaN(ev.ViewportY));
        }
    }
}
