using AccessibleTrader.Core.Services.Strategies;
using Xunit;

namespace AccessibleTrader.Tests
{
    public class BuiltInStrategySeedsPresetTests
    {
        [Theory]
        [InlineData("BTC/USDT")]
        [InlineData("BTCUSDT")]
        [InlineData("BTC-USD")]
        [InlineData("ETH/USDT")]
        [InlineData("ETHUSDT")]
        public void V23LongPreset_BtcOrEth_NoTimeframe_ReturnsFaberGated(string symbol)
        {
            // No timeframe given → conservative default = Faber-gated.
            var preset = BuiltInStrategySeeds.GetV23LongPresetForAsset(symbol);
            Assert.Equal(BuiltInStrategySeeds.LongV23rCipherBFaberId, preset);
        }

        [Theory]
        [InlineData("BTC/USDT", "1d")]
        [InlineData("ETH/USDT", "1d")]
        [InlineData("BTCUSDT",  "24h")]
        public void V23LongPreset_BtcOrEthDaily_ReturnsPivots(string symbol, string tf)
        {
            // Empirical champion at 1d on BTC/ETH = Pivots gate
            // (ETH 1d 100% / 33% CI / +0.523R from round 4 face-rolling).
            var preset = BuiltInStrategySeeds.GetV23LongPresetForAsset(symbol, tf);
            Assert.Equal(BuiltInStrategySeeds.LongV23pCipherBPivotsId, preset);
        }

        [Theory]
        [InlineData("BTC/USDT", "4h")]
        [InlineData("ETH/USDT", "4h")]
        public void V23LongPreset_BtcOrEth4h_ReturnsFaber(string symbol, string tf)
        {
            var preset = BuiltInStrategySeeds.GetV23LongPresetForAsset(symbol, tf);
            Assert.Equal(BuiltInStrategySeeds.LongV23rCipherBFaberId, preset);
        }

        [Theory]
        [InlineData("BTC/USDT", "4h")]
        public void V23ShortPreset_Btc4h_ReturnsV22DistributionTopRobust(string symbol, string tf)
        {
            // BTC 4h SHORT is the only ROBUST short anywhere → use v22.
            var preset = BuiltInStrategySeeds.GetV23ShortPresetForAsset(symbol, tf);
            Assert.Equal(BuiltInStrategySeeds.ShortV22DistributionTopId, preset);
        }

        [Theory]
        [InlineData("ETH/USDT", "1d")]
        [InlineData("KAS/USDT", "4h")]
        public void V23ShortPreset_NotBtc4h_ReturnsHurst(string symbol, string tf)
        {
            // Hurst-gated short is the per-trade-R champion everywhere except BTC 4h.
            var preset = BuiltInStrategySeeds.GetV23ShortPresetForAsset(symbol, tf);
            Assert.Equal(BuiltInStrategySeeds.ShortV23hCipherBHurstId, preset);
        }

        [Theory]
        [InlineData("XRP/USDT")]
        [InlineData("LTC/USDT")]
        [InlineData("LTCUSDT")]
        public void V23LongPreset_XrpOrLtc_ReturnsBareV23(string symbol)
        {
            var preset = BuiltInStrategySeeds.GetV23LongPresetForAsset(symbol);
            Assert.Equal(BuiltInStrategySeeds.LongV23CipherBWeeklyId, preset);
        }

        [Theory]
        [InlineData("SOL/USDT")]
        [InlineData("DOGE/USDT")]
        [InlineData("ADA/USDT")]
        public void V23LongPreset_UnknownAsset_FallsBackToBareV23(string symbol)
        {
            var preset = BuiltInStrategySeeds.GetV23LongPresetForAsset(symbol);
            Assert.Equal(BuiltInStrategySeeds.LongV23CipherBWeeklyId, preset);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void V23LongPreset_NullOrEmpty_ReturnsNull(string? symbol)
        {
            var preset = BuiltInStrategySeeds.GetV23LongPresetForAsset(symbol!);
            Assert.Null(preset);
        }
    }
}
