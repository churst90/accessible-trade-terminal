using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Universal contract checks every trading provider must satisfy — the audit
    /// findings turned into a permanent gate. Constructs real providers (which touch
    /// the global ApiKeys bridge), so it shares the signed-path collection to stay
    /// serialized. Capability-flag consistency lives in
    /// <see cref="ProviderCapabilityHonestyTests"/>.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderConformanceTests
    {
        public static IEnumerable<object[]> Providers()
        {
            yield return P(new AccessibleTrader.Plugins.Binance.BinanceProvider());
            yield return P(new AccessibleTrader.Plugins.Bitstamp.BitstampProvider());
            yield return P(new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider());
            yield return P(new AccessibleTrader.Plugins.Kraken.KrakenProvider());
            yield return P(new AccessibleTrader.Plugins.Mexc.MexcProvider());
            yield return P(new AccessibleTrader.Plugins.Alpaca.AlpacaProvider());
            yield return P(new AccessibleTrader.Plugins.Oanda.OandaProvider());
            yield return P(new AccessibleTrader.Plugins.Tradier.TradierProvider());
            yield return P(new AccessibleTrader.Plugins.Schwab.SchwabProvider());
            yield return P(new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider());
            yield return P(new AccessibleTrader.Plugins.Polygon.PolygonProvider());
            yield return P(new AccessibleTrader.Plugins.Finnhub.FinnhubProvider());
            yield return P(new AccessibleTrader.Plugins.Fmp.FmpProvider());
            yield return P(new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider());
        }

        private static object[] P(BaseMarketDataProvider provider) => new object[] { new Wrapped(provider) };

        // Boxes the provider with a readable ToString so a failing row names the provider.
        public sealed class Wrapped
        {
            public BaseMarketDataProvider Provider { get; }
            public Wrapped(BaseMarketDataProvider p) { Provider = p; }
            public override string ToString() => Provider.Name;
        }

        [Theory]
        [MemberData(nameof(Providers))]
        public void Provider_satisfies_universal_contracts(Wrapped w)
        {
            var p = w.Provider;

            Assert.False(string.IsNullOrWhiteSpace(p.Name), "Name must be set.");
            Assert.False(string.IsNullOrWhiteSpace(p.Description), $"{p.Name}: Description must be set.");
            Assert.NotEmpty(p.SupportedMarkets);
            Assert.True(p.MaxBarsPerRequest > 0, $"{p.Name}: MaxBarsPerRequest must be positive.");
            Assert.NotEmpty(p.NativelySupportedTimeframes);

            // Every declared native timeframe must be a recognised token, so the
            // provider's interval mapping and the app's timeframe utility agree.
            foreach (var tf in p.NativelySupportedTimeframes)
                Assert.True(TimeframeUtility.ToSeconds(tf) > 0, $"{p.Name}: unrecognised timeframe '{tf}'.");

            // GetCapability must at least surface the market-data face.
            Assert.NotNull(p.GetCapability<IMarketDataProvider>());
        }
    }
}
