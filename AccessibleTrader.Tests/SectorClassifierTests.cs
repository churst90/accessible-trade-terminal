using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Sector classification behind the warn-only "2% risk per sector" hints.
    /// The hint must fire for correlated stacking (BTC+ETH+KAS+TAO is one bet)
    /// and stay silent for a diversified book — and it must NEVER block anything;
    /// blocking is not its contract.
    /// </summary>
    public class SectorClassifierTests
    {
        [Theory]
        [InlineData("BTC/USDT", SectorClassifier.Crypto)]
        [InlineData("KAS/USDT", SectorClassifier.Crypto)]
        [InlineData("TAO/USDT", SectorClassifier.Crypto)]
        [InlineData("XAU/USD",  SectorClassifier.Metals)]
        [InlineData("SLV",      SectorClassifier.Metals)]
        [InlineData("CL",       SectorClassifier.Energy)]
        [InlineData("SPY",      SectorClassifier.Indices)]
        [InlineData("QQQ",      SectorClassifier.Indices)]
        [InlineData("EUR/USD",  SectorClassifier.Fx)]
        [InlineData("AAPL",     SectorClassifier.Stocks)]
        [InlineData("NEWCOIN/USDT", SectorClassifier.Crypto)] // unknown base, crypto-style quote
        public void Classify_MapsSymbolsToSectors(string symbol, string expected)
            => Assert.Equal(expected, SectorClassifier.Classify(symbol));

        [Fact]
        public void Hint_Fires_WhenStackingCorrelatedPositions()
        {
            var hint = SectorClassifier.BuildSectorHint(
                "TAO/USDT", new[] { "BTC/USDT", "ETH/USDT", "KAS/USDT" });

            Assert.NotNull(hint);
            Assert.Contains("3 open positions", hint);
            Assert.Contains("crypto", hint);
            Assert.Contains("2-percent-per-sector", hint);
        }

        [Fact]
        public void Hint_Silent_ForDiversifiedBook_AndForSameSymbol()
        {
            // Different sectors — nothing to warn about.
            Assert.Null(SectorClassifier.BuildSectorHint("XAU/USD", new[] { "BTC/USDT", "SPY" }));
            // Adding to the SAME symbol is position management, not sector stacking.
            Assert.Null(SectorClassifier.BuildSectorHint("BTC/USDT", new[] { "BTC/USDT" }));
            // Empty book.
            Assert.Null(SectorClassifier.BuildSectorHint("BTC/USDT", System.Array.Empty<string>()));
        }
    }
}
