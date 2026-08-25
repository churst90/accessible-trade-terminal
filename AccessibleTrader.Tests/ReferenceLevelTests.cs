using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies that each category provider returns the correct canonical level definitions,
    /// and that SeriesManagementService.RegisterSeries injects those levels onto new series.
    /// </summary>
    public class ReferenceLevelTests
    {
        private static readonly SkenderBoundedOscillatorProvider _bounded = new();
        private static readonly SkenderZeroCrossProvider _zeroCross = new();

        // ── 1. Provider GetDefaultLevels ─────────────────────────────────────

        [Theory]
        [InlineData("RSI")]
        public void GetLevels_Rsi_ReturnsOverboughtMidpointOversold(string code)
        {
            var levels = _bounded.GetDefaultLevels(code);

            Assert.Equal(3, levels.Count);
            Assert.Contains(levels, l => l.Name == "Overbought" && l.Value == 70);
            Assert.Contains(levels, l => l.Name == "Midpoint"   && l.Value == 50);
            Assert.Contains(levels, l => l.Name == "Oversold"   && l.Value == 30);
        }

        [Theory]
        [InlineData("STOCH")]
        [InlineData("STOCHRSI")]
        [InlineData("MFI")]
        public void GetLevels_BoundedOscillator_ReturnsThreeLevels(string code)
        {
            var levels = _bounded.GetDefaultLevels(code);

            Assert.Equal(3, levels.Count);
            Assert.Contains(levels, l => l.Name == "Overbought");
            Assert.Contains(levels, l => l.Name == "Midpoint");
            Assert.Contains(levels, l => l.Name == "Oversold");
        }

        [Theory]
        [InlineData("WILLIAMSR")]
        [InlineData("WilliamsR")]
        [InlineData("williamsr")]
        public void GetLevels_WilliamsR_ReturnsNegativeScale(string code)
        {
            var levels = _bounded.GetDefaultLevels(code);

            Assert.Equal(3, levels.Count);
            Assert.Contains(levels, l => l.Name == "Overbought" && l.Value == -20);
            Assert.Contains(levels, l => l.Name == "Midpoint"   && l.Value == -50);
            Assert.Contains(levels, l => l.Name == "Oversold"   && l.Value == -80);
        }

        [Fact]
        public void GetLevels_CCI_ReturnsZeroAndPlusMinus100()
        {
            var levels = _bounded.GetDefaultLevels("CCI");

            Assert.Equal(3, levels.Count);
            Assert.Contains(levels, l => l.Name == "Overbought" && l.Value ==  100);
            Assert.Contains(levels, l => l.Name == "Zero"       && l.Value ==    0);
            Assert.Contains(levels, l => l.Name == "Oversold"   && l.Value == -100);
        }

        [Theory]
        [InlineData("MACD")]
        [InlineData("MOM")]
        [InlineData("ROC")]
        [InlineData("CMO")]
        [InlineData("PPO")]
        public void GetLevels_ZeroCrossProvider_ContainsZeroLine(string code)
        {
            var levels = _zeroCross.GetDefaultLevels(code);

            Assert.NotEmpty(levels);
            Assert.Contains(levels, l => l.Name == "Zero" && l.Value == 0);
        }

        [Fact]
        public void GetLevels_Aroon_ReturnsMidpoint50()
        {
            var levels = _zeroCross.GetDefaultLevels("AROON");

            Assert.Single(levels);
            Assert.Equal("Midpoint", levels[0].Name);
            Assert.Equal(50, levels[0].Value);
        }

        [Theory]
        [InlineData("SMA")]
        [InlineData("EMA")]
        [InlineData("WMA")]
        [InlineData("HMA")]
        [InlineData("ALMA")]
        public void GetLevels_MovingAverages_ReturnsEmptyList(string code)
        {
            var trendProvider = new SkenderTrendProvider();
            var levels = trendProvider.GetDefaultLevels(code);
            Assert.Empty(levels);
        }

        [Theory]
        [InlineData("ATR")]
        [InlineData("STDDEV")]
        public void GetLevels_Volatility_ReturnsEmptyList(string code)
        {
            var volatilityProvider = new SkenderVolatilityProvider();
            var levels = volatilityProvider.GetDefaultLevels(code);
            Assert.Empty(levels);
        }

        [Theory]
        [InlineData("BB")]
        [InlineData("KC")]
        [InlineData("DONCHIAN")]
        public void GetLevels_Bands_ReturnsEmptyList(string code)
        {
            var bandProvider = new SkenderBandProvider();
            var levels = bandProvider.GetDefaultLevels(code);
            Assert.Empty(levels);
        }

        [Fact]
        public void GetLevels_IsCaseInsensitive()
        {
            var upper = _bounded.GetDefaultLevels("RSI");
            var lower = _bounded.GetDefaultLevels("rsi");
            var mixed = _bounded.GetDefaultLevels("Rsi");

            Assert.Equal(upper.Count, lower.Count);
            Assert.Equal(upper.Count, mixed.Count);
        }

        // ── 2. RegisterSeries level injection ────────────────────────────────

        private static SeriesManagementService BuildService(out MockWorkspaceStore store)
        {
            store = new MockWorkspaceStore();
            var eventBus = new SpyEventBus();
            var roleMapper = new ComponentRoleMapper();
            var profileProvider = new SonificationProfileProvider();
            var paneService = new PaneAssignmentService();
            var stylingService = new StylingService(roleMapper, profileProvider, paneService);
            var modelFactory = new IndicatorModelFactory(stylingService, new MockIndicatorPreferencesService());
            var libraryLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceLibraryService>.Instance;
            var library = new WorkspaceLibraryService(libraryLogger, new TempWorkspacePaths());

            var customRegistry = new CustomIndicatorRegistry();

            var providers = new List<IIndicatorProvider>
            {
                new SkenderBoundedOscillatorProvider(),
                new SkenderZeroCrossProvider(),
                new SkenderBandProvider(),
                new SkenderTrendProvider(),
                new SkenderVolatilityProvider(),
                new SkenderVolumeProvider(),
            };
            var indicatorService = new IndicatorService(providers, Microsoft.Extensions.Logging.Abstractions.NullLogger<IndicatorService>.Instance);
            var indicatorEngine = new IndicatorEngine(indicatorService, customRegistry, providers);

            return new SeriesManagementService(store, eventBus, modelFactory, stylingService, library, customRegistry, indicatorEngine, new MockIndicatorPreferencesService());
        }

        [Fact]
        public void RegisterSeries_RSI_InjectsOverboughtMidpointOversold()
        {
            var svc = BuildService(out var store);
            svc.RegisterSeries("Rsi", "RSI", new List<string> { "Rsi" });

            var action = store.DispatchedActions
                .OfType<AddSeriesAction>()
                .Single(a => a.Series.IndicatorCode.Equals("Rsi", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(3, action.Series.Levels.Count);
            Assert.Contains(action.Series.Levels, l => l.Name == "Overbought");
            Assert.Contains(action.Series.Levels, l => l.Name == "Oversold");
            Assert.True(action.Series.Levels.All(l => l.IsVisible),
                "All injected levels must start visible.");
        }

        [Fact]
        public void RegisterSeries_MACD_InjectsZeroLine()
        {
            var svc = BuildService(out var store);
            svc.RegisterSeries("Macd", "MACD", new List<string> { "Macd", "Signal", "Histogram" });

            var action = store.DispatchedActions
                .OfType<AddSeriesAction>()
                .Single(a => a.Series.IndicatorCode.Equals("Macd", StringComparison.OrdinalIgnoreCase));

            Assert.Single(action.Series.Levels);
            Assert.Equal("Zero", action.Series.Levels[0].Name);
            Assert.Equal(0, action.Series.Levels[0].Value);
        }

        [Fact]
        public void RegisterSeries_SMA_InjectsNoLevels()
        {
            var svc = BuildService(out var store);
            svc.RegisterSeries("Sma", "SMA(20)", new List<string> { "Sma" });

            var action = store.DispatchedActions
                .OfType<AddSeriesAction>()
                .Single(a => a.Series.IndicatorCode.Equals("Sma", StringComparison.OrdinalIgnoreCase));

            Assert.Empty(action.Series.Levels);
        }

        [Fact]
        public void RegisterSeries_CCI_InjectsZeroAndOverboughtOversold()
        {
            var svc = BuildService(out var store);
            svc.RegisterSeries("Cci", "CCI", new List<string> { "Cci" });

            var action = store.DispatchedActions
                .OfType<AddSeriesAction>()
                .Single(a => a.Series.IndicatorCode.Equals("Cci", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(3, action.Series.Levels.Count);
            Assert.Contains(action.Series.Levels, l => l.Value ==  100);
            Assert.Contains(action.Series.Levels, l => l.Value ==    0);
            Assert.Contains(action.Series.Levels, l => l.Value == -100);
        }

        // ── 3. IsVisible flag is honoured ────────────────────────────────────

        [Fact]
        public void LevelConfig_HiddenLevel_IsVisibleFalse()
        {
            var level = new LevelConfig { Name = "Test", Value = 50, IsVisible = false };
            Assert.False(level.IsVisible);
        }
    }
}
