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

        // Code review, 2026-11-20: exchange symbols carry no slash ("BTCUSDT", as MEXC and the
        // paper book name them), and the classifier cut on separators alone, so every one was
        // "other" and the hint never spoke for them.
        [Theory]
        [InlineData("BTCUSDT", SectorClassifier.Crypto)]
        [InlineData("SOLUSDT", SectorClassifier.Crypto)]
        [InlineData("ETHUSD", SectorClassifier.Crypto)]
        [InlineData("ABCUSDT", SectorClassifier.Crypto)]
        [InlineData("KRAKEN:BTCUSD", SectorClassifier.Crypto)]
        [InlineData("XAUUSD", SectorClassifier.Metals)]
        [InlineData("EURUSD", SectorClassifier.Fx)]
        [InlineData("SETH", SectorClassifier.Stocks)]
        [InlineData("AAPL", SectorClassifier.Stocks)]
        public void Symbols_without_a_separator_are_classified_by_their_base(string symbol, string sector)
            => Assert.Equal(sector, SectorClassifier.Classify(symbol));

        [Fact]
        public void Hint_speaks_when_the_book_holds_exchange_style_symbols()
        {
            var hint = SectorClassifier.BuildSectorHint("ETH/USDT", new[] { "BTCUSDT", "SOLUSDT" });
            Assert.NotNull(hint);
            Assert.Contains("2 open positions in the crypto sector (BTCUSDT, SOLUSDT)", hint);
        }
    }
}
