using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Enter places a drawing's next point, and the Drawing Tools list (Alt+D) offers every tool.
    ///
    /// <para>
    /// Before 2026-11-01 a point was placed only by pressing the tool's own shortcut again, and
    /// the list held eleven of the fifteen tools. A tool chosen from the list has no shortcut, so
    /// the new pattern tools could be chosen and never placed by keyboard — and the speech after
    /// choosing one said "press Enter".
    /// </para>
    /// </summary>
    public class DrawingEnterPlacementTests
    {
        private sealed class TempPaths : IPlatformPathService
        {
            public string AppDataDirectory { get; } = TestTemp.NewDir("att-enter-");
            public string CacheDirectory => AppDataDirectory;
        }

        [Theory]
        [InlineData("Enter")]
        [InlineData("RETURN")]
        public void A_bare_Enter_is_the_place_point_command(string spelling)
        {
            var mgr = new ShortcutManager(new TempPaths());
            Assert.Equal(SystemCommand.ConfirmCoordinateEntry, mgr.GetCommand(spelling, shift: false, ctrl: false, alt: false));
            // Quick trade keeps its two Enter chords.
            Assert.Equal(SystemCommand.QuickPlaceLimit, mgr.GetCommand(spelling, shift: true, ctrl: false, alt: false));
            Assert.Equal(SystemCommand.QuickPlaceMarket, mgr.GetCommand(spelling, shift: false, ctrl: true, alt: false));
        }

        private static (CommandDispatcher, SpyEventBus) Build()
        {
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            store.EmitState(WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 3)
                    .Select(i => new Ohlcv(new DateTime(2026, 1, 1, 0, i, 0, DateTimeKind.Utc), 1, 2, 0, 1, 1)).ToList()),
                CurrentDataIndex = 2,
            });
            var crossing = new IndicatorCrossingEngine(store, bus);
            var d = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
                Substitute.For<IBarDetailService>(), crossing);
            return (d, bus);
        }

        [Fact]
        public void Enter_on_the_chart_places_the_next_point()
        {
            var (dispatcher, bus) = Build();
            bus.Publish(new ChartFocusEvent());
            dispatcher.Dispatch(SystemCommand.ConfirmCoordinateEntry);
            Assert.Single(bus.Log.OfType<PlaceDrawingAnchorEvent>());
        }

        [Fact]
        public void Enter_off_the_chart_places_nothing()
        {
            var (dispatcher, bus) = Build();
            dispatcher.Dispatch(SystemCommand.ConfirmCoordinateEntry);
            Assert.Empty(bus.Log.OfType<PlaceDrawingAnchorEvent>());
        }

        [Fact]
        public void The_browser_forwards_a_bare_Enter_only_from_the_chart()
        {
            // Trapping Enter everywhere would stop it activating every button in the app.
            string js = File.ReadAllText(Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient.Components",
                "wwwroot", "js", "keyboard.js"));
            var m = Regex.Match(js, @"const isChartEnter\s*=\s*([^;]+);");
            Assert.True(m.Success, "keyboard.js no longer defines isChartEnter, so Enter never reaches the dispatcher.");
            Assert.Contains("self._chartFocused", m.Groups[1].Value);
            Assert.Contains("e.key === 'Enter'", m.Groups[1].Value);
            Assert.Matches(@"const isTrapped\s*=\s*trappedKeys\.includes\(e\.key\)\s*\|\|\s*isChartEnter;", js);
            Assert.DoesNotContain("'Enter'", Regex.Match(js, @"const trappedKeys = \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value);
        }

        [Fact]
        public void The_tool_list_holds_every_drawing_type_once()
        {
            var listed = DrawingToolCatalog.All.Select(t => t.Type).ToList();
            var every = Enum.GetValues<DrawingType>().Where(t => t != DrawingType.None).ToList();
            Assert.Equal(every.OrderBy(t => t), listed.OrderBy(t => t));
            foreach (var tool in DrawingToolCatalog.All)
            {
                Assert.Equal(tool.Type, DrawingInteractionManager.ToolType(tool.Key));
                Assert.False(string.IsNullOrWhiteSpace(tool.Hint));
            }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.sln"))
                   && !Directory.Exists(Path.Combine(dir.FullName, "AccessibleTrader.Core")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
        }
    }
}
