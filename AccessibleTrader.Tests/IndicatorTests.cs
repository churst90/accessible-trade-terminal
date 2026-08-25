using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Tests
{
    public class IndicatorTests
    {
        private readonly IIndicatorService _indicatorService;

        public IndicatorTests()
        {
            var providers = new List<IIndicatorProvider> {
                new CoreIndicatorProvider(),
                new SkenderBoundedOscillatorProvider(),
                new SkenderZeroCrossProvider(),
                new SkenderBandProvider(),
                new SkenderTrendProvider(),
                new SkenderVolatilityProvider(),
                new SkenderVolumeProvider(),
                new ProfileIndicatorProvider()
            };

            _indicatorService = new IndicatorService(providers, Microsoft.Extensions.Logging.Abstractions.NullLogger<IndicatorService>.Instance);
        }

        private List<Ohlcv> GetMockData(int count)
        {
            var data = new List<Ohlcv>();
            var start = new DateTime(2023, 1, 1);
            for (int i = 0; i < count; i++)
            {
                // Simple sine wave for price to ensure movement
                double price = 100 + Math.Sin(i * 0.1) * 10;
                data.Add(new Ohlcv(start.AddHours(i), price, price + 1, price - 1, price, 1000));
            }
            return data;
        }

        [Fact]
        public void Rsi_Calculation_Returns_Valid_Data()
        {
            // Arrange
            var data = GetMockData(100);
            var parameters = new Dictionary<string, object> { { "lookbackPeriods", 14 } };
            var resultsDict = new Dictionary<string, double[]>();
            var buffer = new IndicatorResultBuffer(resultsDict, data.Count);

            // Act
            _indicatorService.CalculateIndicator("RSI", data.ToArray(), parameters, buffer);

            // Assert
            Assert.True(resultsDict.ContainsKey("Rsi"));
            var rsiValues = resultsDict["Rsi"];
            Assert.Equal(data.Count, rsiValues.Length);
            
            // RSI should have NaNs for the first 14 periods (typical for Skender/Wilder)
            var firstValid = rsiValues.Skip(14).FirstOrDefault(v => !double.IsNaN(v));
            Assert.NotEqual(0, firstValid); 
            Assert.True(firstValid >= 0 && firstValid <= 100);
        }

        [Fact]
        public void Macd_Calculation_Returns_Multiple_Components()
        {
            // Arrange
            var data = GetMockData(100);
            var parameters = new Dictionary<string, object> 
            { 
                { "fastPeriods", 12 }, 
                { "slowPeriods", 26 }, 
                { "signalPeriods", 9 } 
            };
            var resultsDict = new Dictionary<string, double[]>();
            var buffer = new IndicatorResultBuffer(resultsDict, data.Count);

            // Act
            _indicatorService.CalculateIndicator("MACD", data.ToArray(), parameters, buffer);

            // Assert
            Assert.True(resultsDict.ContainsKey("Macd"));
            Assert.True(resultsDict.ContainsKey("Signal"));
            Assert.True(resultsDict.ContainsKey("Histogram"));

            Assert.Equal(data.Count, resultsDict["Macd"].Length);
            Assert.Equal(data.Count, resultsDict["Signal"].Length);
            Assert.Equal(data.Count, resultsDict["Histogram"].Length);
        }
    }
}



