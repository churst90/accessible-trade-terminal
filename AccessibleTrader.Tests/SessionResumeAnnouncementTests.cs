using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>The resume announcement names what came back.</b>
    ///
    /// <para>
    /// Cody, 2026-10-15: after a restart the terminal said "Resumed your last session: 3 tabs."
    /// and nothing else, and he could not tell by ear whether the layout he had left was the one
    /// he got — he believed indicators were missing and had no quick way to check. The sentence
    /// now goes on to name the active chart and the indicators on it, so a wrong or stale layout
    /// is audible on arrival.
    /// </para>
    ///
    /// <para>
    /// Every test here runs the REAL restore (WorkspaceInitializer → SeriesManagementService →
    /// WorkspaceStore) and reads the sentence the real service publishes, because which tab ends
    /// up active and which series end up on it is the restore's decision, not the file's. A
    /// fixture that fed the announcer a hand-built state would agree with whatever the announcer
    /// assumed.
    /// </para>
    /// </summary>
    public class SessionResumeAnnouncementTests
    {
        private static SeriesConfig Core(string id, string code, string name) =>
            new() { Id = id, IndicatorCode = code, Name = name, FriendlyName = name };

        private static SeriesConfig Indicator(string code, string name) =>
            new() { Id = Guid.NewGuid().ToString(), IndicatorCode = code, Name = name, FriendlyName = name };

        /// <summary>Candles, Volume and Price — on every chart, so never "your indicators".</summary>
        private static List<SeriesConfig> CoreStack() => new()
        {
            Core(CoreSeriesIds.Candles, "CANDLES", "Candles"),
            Core(CoreSeriesIds.Volume, "VOLUME", "Volume"),
            Core(CoreSeriesIds.Price, "PRICE", "Price"),
        };

        private static TabConfiguration Tab(string symbol, string timeframe, params SeriesConfig[] added)
        {
            var series = CoreStack();
            series.AddRange(added);
            return new TabConfiguration
            {
                Market = "Spot", Provider = "Binance", Symbol = symbol, Timeframe = timeframe,
                Series = series,
            };
        }

        /// <summary>
        /// Restores <paramref name="config"/> as the last session through the real initializer
        /// and returns the one announcement the resume published.
        /// </summary>
        private static string Resume(WorkspaceConfiguration config)
        {
            var storeBus = new EventBus();
            var store = new WorkspaceStore(storeBus, new MockViewportRangeCalculator(),
                new MockViewportNavigationService(), new MockVolumeStateService());

            var styling = new StylingService(new ComponentRoleMapper(),
                new SonificationProfileProvider(), new PaneAssignmentService());
            var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
            var seriesLibrary = new WorkspaceLibraryService(
                NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths());
            var registry = new CustomIndicatorRegistry();
            var providers = new List<IIndicatorProvider>();
            var indicatorService = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
            var engine = new IndicatorEngine(indicatorService, registry, providers);
            var seriesService = new SeriesManagementService(store, storeBus, factory, styling,
                seriesLibrary, registry, engine, new MockIndicatorPreferencesService());
            var initializer = new WorkspaceInitializer(seriesService, store, indicatorService);

            // The saved slot: the autosave's own library is a fake whose disk holds this one file.
            var library = Substitute.For<IWorkspaceLibraryService>();
            const string slot = SessionAutosaveService.LastSessionProfileName + "resume";
            library.GetAllProfilesWithTimes().Returns(new List<(string, DateTime)> { (slot, DateTime.UtcNow) });
            library.LoadProfile(slot).Returns(config);

            var announcements = Substitute.For<IEventBus>();
            using var svc = new SessionAutosaveService(store, library, initializer,
                Substitute.For<ISettingsManager>(), new DemoPolicy(false), announcements,
                NullLogger<SessionAutosaveService>.Instance);

            Assert.True(svc.TryResumeAtStartup(), "the resume itself failed; nothing to announce");

            var spoken = announcements.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IEventBus.Publish))
                .Select(c => c.GetArguments()[0])
                .OfType<AnnouncementEvent>()
                .ToList();
            var only = Assert.Single(spoken);
            Assert.Equal(SpeechChannel.Interface, only.Channel);
            return only.Message;
        }

        [Fact]
        public void Multi_tab_names_the_ACTIVE_tab_and_its_indicators_not_the_first_tab()
        {
            // The active tab is the middle one, and the first tab carries an indicator of its
            // own, so "describe tab 0" and "describe the active tab" say different things.
            var config = new WorkspaceConfiguration
            {
                ActiveTabIndex = 1,
                Tabs =
                {
                    Tab("ETHUSDT", "4h", Indicator("EMA", "EMA")),
                    Tab("BTCUSDT", "1d",
                        Indicator("CIPHER_B", "Cipher B"),
                        Indicator("SWING_STRUCTURE", "Market Structure"),
                        Indicator("VPVR", "Volume Profile (Visible Range)")),
                    Tab("SOLUSDT", "1h"),
                },
            };

            Assert.Equal(
                "Resumed your last session: 3 tabs. BTCUSDT 1 day, with Cipher B, Market Structure and Volume Profile (Visible Range).",
                Resume(config));
        }

        [Fact]
        public void More_than_three_indicators_are_counted_not_listed()
        {
            var config = new WorkspaceConfiguration
            {
                ActiveTabIndex = 0,
                Tabs =
                {
                    Tab("BTCUSDT", "4h",
                        Indicator("RSI", "RSI"),
                        Indicator("MACD", "MACD"),
                        Indicator("EMA", "EMA"),
                        Indicator("CIPHER_B", "Cipher B")),
                    Tab("ETHUSDT", "1d"),
                },
            };

            Assert.Equal("Resumed your last session: 2 tabs. BTCUSDT 4 hours, with 4 indicators.", Resume(config));
        }

        [Fact]
        public void A_chart_with_only_the_core_stack_and_a_drawing_has_no_indicators()
        {
            // Candles, Volume and Price are on every chart, and a trend line is the user's but is
            // not an indicator — none of them is what "were my indicators restored" is asking.
            var trendLine = new SeriesConfig
            {
                Id = "drawing-1", Name = "Trend line (1)", FriendlyName = "TrendLine Drawing",
                Drawing = new DrawingData { Type = DrawingType.TrendLine },
            };
            var config = new WorkspaceConfiguration
            {
                ActiveTabIndex = 1,
                Tabs = { Tab("ETHUSDT", "1h", Indicator("RSI", "RSI")), Tab("BTCUSDT", "15m", trendLine) },
            };

            Assert.Equal("Resumed your last session: 2 tabs. BTCUSDT 15 minutes, no indicators.", Resume(config));
        }

        [Fact]
        public void A_single_tab_session_names_its_chart_without_a_tab_count()
        {
            var config = new WorkspaceConfiguration
            {
                Tabs = { Tab("BTCUSDT", "1d", Indicator("RSI", "RSI")) },
            };

            Assert.Equal("Resumed your last session. BTCUSDT 1 day, with RSI.", Resume(config));
        }

        [Fact]
        public void Two_indicators_are_joined_with_and()
        {
            var config = new WorkspaceConfiguration
            {
                Tabs = { Tab("BTCUSDT", "1d", Indicator("RSI", "RSI"), Indicator("MACD", "MACD")) },
            };

            Assert.Equal("Resumed your last session. BTCUSDT 1 day, with RSI and MACD.", Resume(config));
        }
    }
}
