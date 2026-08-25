using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests
{
    public class SymbolValidatorTests
    {
        [Theory]
        [InlineData("BTCUSDT")]
        [InlineData("BTC-USD")]
        [InlineData("EUR_USD")]
        [InlineData("BRK.B")]
        [InlineData("AAPL:NASDAQ")]
        [InlineData("1INCH")]
        [InlineData("a")]
        public void IsValid_RealSymbols_ReturnsTrue(string symbol)
        {
            Assert.True(SymbolValidator.IsValid(symbol));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("BTC USDT")]        // space
        [InlineData("BTC?override=1")]  // query injection
        [InlineData("BTC&apiKey=leak")] // query injection
        [InlineData("BTC#anchor")]      // fragment
        [InlineData("BTC%2FUSD")]       // percent-encoded slash
        [InlineData("BTC USDT\n")]      // newline
        [InlineData("BTC\rUSDT")]       // carriage return
        [InlineData("../../../etc/passwd")]
        [InlineData("/BTCUSDT")]        // leading slash
        [InlineData("..BTCUSDT")]       // traversal prefix
        [InlineData("BTC;rm -rf /")]    // shell
        public void IsValid_HostilePatterns_ReturnsFalse(string? symbol)
        {
            Assert.False(SymbolValidator.IsValid(symbol));
        }

        [Fact]
        public void IsValid_ExceedsMaxLength_ReturnsFalse()
        {
            var tooLong = new string('A', SymbolValidator.MaxLength + 1);
            Assert.False(SymbolValidator.IsValid(tooLong));
        }

        [Fact]
        public void Validate_InvalidSymbol_Throws()
        {
            var ex = Assert.Throws<System.ArgumentException>(() =>
                SymbolValidator.Validate("BTC?drop=1", "TestProvider"));
            Assert.Contains("TestProvider", ex.Message);
        }

        [Fact]
        public void Validate_ValidSymbol_DoesNotThrow()
        {
            SymbolValidator.Validate("BTC-USD", "TestProvider");
        }
    }
}
