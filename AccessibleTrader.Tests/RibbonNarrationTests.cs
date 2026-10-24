using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A RIBBON — several price-space lines declared as one band — narrates as the band: price
    /// entering it, leaving it above or below, or jumping straight through it, on the bar close.
    ///
    /// <para>
    /// Cody, 2026-10-23, on Cipher A's eight-line ribbon: <i>"Ribbon narration, yes I agree, for
    /// this indicator and I suppose ribbons in general"</i>, and <i>"announce when in the ribbon?
    /// yes"</i>. Before this, the ribbon said nothing at all on a bar close: its lines are muted
    /// (eight tones within a few percent of price would bury the candles), and the overlay-cross
    /// scan skips a muted line (<c>Ribbon_lines_were_silent_before_ribbon_narration</c> records
    /// it). Unmuting them would have given up to eight "Price crossed" sentences on one bar.
    /// </para>
    /// </summary>
    public class RibbonNarrationTests
    {
        private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
        {
            public List<string> Spoken { get; } = new();
            public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Chart) => Spoken.Add(message);
            public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
            public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
            public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
        }

        private sealed class NoContexts : IIndicatorContextAnalyzer
        {
            public void RegisterDefinition(IndicatorContextDefinition def) { }
            public bool HasZoneThresholds(string indicatorCode, string componentName) => false;
            public IndicatorContext? AnalyzeAt(ChartSeries series, string? componentName, int dataIndex) => null;
            public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state) => null;
            public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state)
                => Enumerable.Empty<IndicatorContext>();
        }

        // Three flat lines: the band runs 100 to 110, with a middle line at 105.
        private static readonly string[] Lines = { "EMA 5", "EMA 11", "EMA 15" };
        private static readonly double[] Levels = { 110, 105, 100 };

        private static SeriesConfig RibbonConfig(string? ribbon = "ribbon", bool muted = true)
        {
            var cfg = new SeriesConfig
            {
                Id = "ca", Name = "Cipher A", FriendlyName = "Cipher A", IndicatorCode = "CipherA",
                Pane = "Main", IsAutoNarrated = true, IsVisible = true, IsMuted = false,
            };
            foreach (var name in Lines)
                cfg.Components.Add(new ComponentConfig
                {
                    Name = name, DisplayName = name, DisplayType = ComponentDisplayType.Line,
                    IsVisible = true, IsMuted = muted, RibbonName = ribbon,
                });
            return cfg;
        }

        private static WorkspaceState State(SeriesConfig cfg, double[] closes, int count)
        {
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            for (int k = 0; k < Lines.Length; k++)
                buf.ComponentData[Lines[k]] = Enumerable.Repeat(Levels[k], count).ToArray();
            var series = new ChartSeries(cfg, buf);
            var bars = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, count).Select(i =>
                new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                          closes[i], closes[i] + 1, closes[i] - 1, closes[i], 1000)));
            return WorkspaceState.Initial with
            {
                Data = bars,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                CurrentDataIndex = count - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                IsSpeechEnabled = true,
            };
        }

        /// <summary>
        /// The real bar-close sequence, one growth per bar: seeded at two bars, the third growth
        /// is skipped as history, and from then on each growth closes the bar before it. So
        /// closes[2..^1] are each scanned once; the last close is the forming bar.
        /// </summary>
        private static List<string> Walk(SeriesConfig cfg, params double[] closes)
            => Ticks(cfg, Enumerable.Range(2, closes.Length - 1).Select(n => closes[..n]).ToArray());

        /// <summary>One emission per array: a longer array is a new bar, the same length is a
        /// tick that moves the forming bar's close.</summary>
        private static List<string> Ticks(SeriesConfig cfg, params double[][] emissions)
        {
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            _ = new AutoNarrationService(store, bus, router, new NoContexts());
            foreach (var closes in emissions)
            {
                store.EmitState(State(cfg, closes, closes.Length));
                bus.Publish(new RedrawEvent());
            }
            return router.Spoken;
        }

        [Fact]
        public void Ribbon_lines_were_silent_before_ribbon_narration()
        {
            // The defect, kept as a record: three muted lines and no ribbon declared. Price closes
            // below all three, then above all three, and nothing is said.
            var spoken = Walk(RibbonConfig(ribbon: null), 95, 95, 95, 120, 120);
            Assert.Empty(spoken);
        }

        [Fact]
        public void Entering_the_ribbon_says_where_it_runs()
        {
            var spoken = Walk(RibbonConfig(), 95, 95, 95, 103, 103);
            Assert.Equal(new[] { "Cipher A: Price entered the ribbon, between 100.00 and 110.00." }, spoken);
        }

        [Fact]
        public void Leaving_it_says_which_side()
        {
            var up = Walk(RibbonConfig(), 103, 103, 103, 112, 112);
            Assert.Equal(new[] { "Cipher A: Price left the ribbon above, at 110.00." }, up);

            var down = Walk(RibbonConfig(), 103, 103, 103, 97, 97);
            Assert.Equal(new[] { "Cipher A: Price left the ribbon below, at 100.00." }, down);
        }

        [Fact]
        public void Jumping_through_it_in_one_bar_is_a_cross()
        {
            var up = Walk(RibbonConfig(), 95, 95, 95, 120, 120);
            Assert.Equal(new[] { "Cipher A: Price crossed above the ribbon, at 110.00." }, up);

            var down = Walk(RibbonConfig(), 120, 120, 120, 95, 95);
            Assert.Equal(new[] { "Cipher A: Price crossed below the ribbon, at 100.00." }, down);
        }

        [Fact]
        public void The_bar_forming_when_narration_starts_is_news_if_it_closes_on_the_other_side()
        {
            // N pressed while the forming bar sits below the band (95); it then ticks up into the
            // band and closes there (103). That close is the one the user pressed N to hear, so
            // the side price was on WHEN NARRATION STARTED is what it is compared with. Without
            // the seed the first close would only be recorded, and say nothing.
            var spoken = Ticks(RibbonConfig(),
                new double[] { 95, 95 },
                new double[] { 95, 103 },
                new double[] { 95, 103, 103 });
            Assert.Equal(new[] { "Cipher A: Price entered the ribbon, between 100.00 and 110.00." }, spoken);

            // Already inside when N was pressed and still inside at the close: not news.
            Assert.Empty(Ticks(RibbonConfig(),
                new double[] { 95, 103 },
                new double[] { 95, 103, 103 }));
        }

        [Fact]
        public void Moving_inside_it_or_staying_out_says_nothing()
        {
            // 101 → 108 crosses the middle line but stays in the band; 120 → 125 stays above.
            Assert.Empty(Walk(RibbonConfig(), 101, 101, 101, 108, 108));
            Assert.Empty(Walk(RibbonConfig(), 120, 120, 120, 125, 125));
        }

        [Fact]
        public void A_ribbon_line_never_speaks_its_own_cross()
        {
            // Unmuted, a ribbon line would otherwise also be a plain overlay: one band sentence,
            // not one per line.
            var spoken = Walk(RibbonConfig(muted: false), 95, 95, 95, 120, 120);
            Assert.Equal(new[] { "Cipher A: Price crossed above the ribbon, at 110.00." }, spoken);
        }

        [Fact]
        public void A_hidden_line_is_not_part_of_the_band()
        {
            // Hide the top line: the band is now 100 to 105, so 108 is above it, not inside.
            var cfg = RibbonConfig();
            cfg.Components[0].IsVisible = false;
            var spoken = Walk(cfg, 95, 95, 95, 108, 108);
            Assert.Equal(new[] { "Cipher A: Price crossed above the ribbon, at 105.00." }, spoken);

            var none = RibbonConfig();
            foreach (var c in none.Components) c.IsVisible = false;
            Assert.Empty(Walk(none, 95, 95, 95, 108, 108));
        }

        [Fact]
        public void It_follows_the_N_selection()
        {
            // Another component singled out with N: the ribbon is not selected, so it is quiet.
            var other = RibbonConfig();
            other.Components.Add(new ComponentConfig { Name = "Green Dot", DisplayType = ComponentDisplayType.Dot, IsVisible = true, IsAutoNarrated = true });
            Assert.Empty(Walk(other, 95, 95, 95, 103, 103));

            // Any one of the ribbon's lines singled out selects the ribbon.
            var line = RibbonConfig();
            line.Components[1].IsAutoNarrated = true;
            Assert.Single(Walk(line, 95, 95, 95, 103, 103));

            // A series not narrating says nothing.
            var off = RibbonConfig();
            off.IsAutoNarrated = false;
            Assert.Empty(Walk(off, 95, 95, 95, 103, 103));
        }

        [Fact]
        public void Cipher_As_eight_lines_are_one_ribbon_and_its_ninth_line_and_pivots_are_not()
        {
            var meta = new CipherAProvider().GetIndicators().Single();
            var ribbon = meta.Components.Where(c => c.RibbonName != null).Select(c => c.Name).ToList();
            Assert.Equal(Enumerable.Range(0, 8).Select(CipherAProvider.RibbonLine), ribbon);
            Assert.All(meta.Components.Where(c => c.RibbonName != null), c => Assert.Equal("ribbon", c.RibbonName));
        }

        [Fact]
        public void A_Cipher_A_series_built_from_its_metadata_carries_the_ribbon()
        {
            // What narration reads is the series' ComponentConfig, not the metadata: the factory
            // has to copy the name across.
            var factory = new IndicatorModelFactory(
                new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
                new MockIndicatorPreferencesService());
            var series = factory.CreateSeriesFromMetadata(new CipherAProvider().GetIndicators().Single(), "Cipher A", "Main", new(), null);
            var ribbon = series.Config.Components.Where(c => c.RibbonName == "ribbon").Select(c => c.Name);
            Assert.Equal(Enumerable.Range(0, 8).Select(CipherAProvider.RibbonLine), ribbon);
        }
    }
}
