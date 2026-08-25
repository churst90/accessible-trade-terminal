using System.Reflection;
using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the 2026-04-23 asset-aware routing in <see cref="FundingRateProvider"/>,
    /// <see cref="OpenInterestProvider"/>, and <see cref="CrowdingIndexProvider"/>.
    /// Each one now derives its cross-series symbol from the <c>__symbol</c> parameter
    /// hint stamped by <see cref="IndicatorOrchestrator"/>; previous versions were
    /// hard-coded to "BTCUSDT_FUNDING" / "BTCUSDT_OI", meaning v18 running live on ETH
    /// or SOL silently fetched BTC data. The tests reach the private static
    /// <c>BuildRequest</c> / <c>BuildRequests</c> helpers via reflection.
    /// </summary>
    public class AssetAwareCrossSeriesTests
    {
        // ── FundingRateProvider ───────────────────────────────────────────────

        [Theory]
        [InlineData("BTC/USDT", "BTCUSDT_FUNDING")]
        [InlineData("ETH/USDT", "ETHUSDT_FUNDING")]
        [InlineData("SOL/USDT", "SOLUSDT_FUNDING")]
        [InlineData("DOGE/USDT", "DOGEUSDT_FUNDING")]
        [InlineData("eth-usdt",  "ETHUSDT_FUNDING")]    // case + separator normalisation
        [InlineData("ETH",       "ETHUSDT_FUNDING")]    // bare base implies USDT quote
        public void FundingRate_SymbolHint_BuildsPerAssetRequest(string symbolHint, string expectedSymbol)
        {
            var parameters = new Dictionary<string, object> { ["__symbol"] = symbolHint };
            var req = InvokeBuildFunding(parameters);
            Assert.Equal(expectedSymbol, req.Symbol);
            Assert.Equal("Derivatives", req.Market);
            Assert.Equal("BinanceVision", req.Provider);
            Assert.Equal("8h", req.Timeframe);
        }

        [Fact]
        public void FundingRate_NoSymbolHint_FallsBackToBtc()
        {
            // Backward-compat: tests and snapshot-cache paths that never set __symbol
            // still resolve to the historical BTCUSDT_FUNDING symbol.
            var req = InvokeBuildFunding(new Dictionary<string, object>());
            Assert.Equal("BTCUSDT_FUNDING", req.Symbol);
        }

        [Fact]
        public void FundingRate_NullParameters_FallsBackToBtc()
        {
            // Defensive: null dict must not crash. IndicatorEngine paths shouldn't pass
            // null, but the parameter conversion layer has historically done so.
            var req = InvokeBuildFunding(null);
            Assert.Equal("BTCUSDT_FUNDING", req.Symbol);
        }

        [Fact]
        public void FundingRate_EmptyStringSymbol_FallsBackToBtc()
        {
            var req = InvokeBuildFunding(new Dictionary<string, object> { ["__symbol"] = "" });
            Assert.Equal("BTCUSDT_FUNDING", req.Symbol);
        }

        // ── OpenInterestProvider ──────────────────────────────────────────────

        [Theory]
        [InlineData("BTC/USDT", "BTCUSDT_OI")]
        [InlineData("ETH/USDT", "ETHUSDT_OI")]
        [InlineData("SOL",       "SOLUSDT_OI")]
        public void OpenInterest_SymbolHint_BuildsPerAssetRequest(string symbolHint, string expectedSymbol)
        {
            var parameters = new Dictionary<string, object> { ["__symbol"] = symbolHint };
            var req = InvokeBuildOi(parameters);
            Assert.Equal(expectedSymbol, req.Symbol);
            Assert.Equal("1d", req.Timeframe);
        }

        [Fact]
        public void OpenInterest_NoSymbolHint_FallsBackToBtc()
        {
            var req = InvokeBuildOi(new Dictionary<string, object>());
            Assert.Equal("BTCUSDT_OI", req.Symbol);
        }

        // ── CrowdingIndexProvider ─────────────────────────────────────────────

        [Fact]
        public void Crowding_SymbolHint_BuildsBothFundingAndOiRequests()
        {
            // CrowdingIndex combines funding + OI, so a single __symbol hint must flow
            // into both constituent requests. If only one picks up the hint the
            // composite score silently mixes ETH funding with BTC OI.
            var parameters = new Dictionary<string, object> { ["__symbol"] = "ETH/USDT" };
            var (funding, oi) = InvokeBuildCrowding(parameters);
            Assert.Equal("ETHUSDT_FUNDING", funding.Symbol);
            Assert.Equal("ETHUSDT_OI",      oi.Symbol);
        }

        [Fact]
        public void Crowding_NoSymbolHint_FallsBackToBtcForBoth()
        {
            var (funding, oi) = InvokeBuildCrowding(new Dictionary<string, object>());
            Assert.Equal("BTCUSDT_FUNDING", funding.Symbol);
            Assert.Equal("BTCUSDT_OI",      oi.Symbol);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static CrossSeriesRequest InvokeBuildFunding(Dictionary<string, object>? parameters)
        {
            var method = typeof(FundingRateProvider).GetMethod(
                "BuildRequest",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("FundingRateProvider.BuildRequest not found.");
            return (CrossSeriesRequest)method.Invoke(null, new object?[] { parameters })!;
        }

        private static CrossSeriesRequest InvokeBuildOi(Dictionary<string, object>? parameters)
        {
            var method = typeof(OpenInterestProvider).GetMethod(
                "BuildRequest",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("OpenInterestProvider.BuildRequest not found.");
            return (CrossSeriesRequest)method.Invoke(null, new object?[] { parameters })!;
        }

        private static (CrossSeriesRequest funding, CrossSeriesRequest oi) InvokeBuildCrowding(Dictionary<string, object>? parameters)
        {
            var method = typeof(CrowdingIndexProvider).GetMethod(
                "BuildRequests",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("CrowdingIndexProvider.BuildRequests not found.");
            return ((CrossSeriesRequest, CrossSeriesRequest))method.Invoke(null, new object?[] { parameters })!;
        }
    }
}
