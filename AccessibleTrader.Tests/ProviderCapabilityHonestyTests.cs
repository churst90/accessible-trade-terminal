using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Capability honesty: a provider must not advertise a trading capability it cannot
    /// actually place. The 2026-07 audit found InteractiveBrokers declaring OCO / brackets /
    /// trailing-stop it never implemented (the UI offered controls that silently placed a
    /// bare order) and MEXC declaring spot brackets it rejects. These pin the corrected
    /// declarations and the general OCO-implies-IOcoTradingProvider invariant so the class
    /// of bug can't reappear.
    /// </summary>
    public class ProviderCapabilityHonestyTests
    {
        // Trading-capable providers with parameterless constructors (Capabilities is a pure
        // property — no credentials or network needed to read it).
        private static IEnumerable<BaseMarketDataProvider> TradingProviders()
        {
            yield return new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
            yield return new AccessibleTrader.Plugins.Mexc.MexcProvider();
            yield return new AccessibleTrader.Plugins.Binance.BinanceProvider();
            yield return new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            yield return new AccessibleTrader.Plugins.Tradier.TradierProvider();
            yield return new AccessibleTrader.Plugins.Schwab.SchwabProvider();
            yield return new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
        }

        [Fact]
        public void Declaring_OCO_requires_implementing_IOcoTradingProvider()
        {
            // The SDK contract (ITradingProvider docs): the OCO capability flag WITHOUT an
            // IOcoTradingProvider implementation means the order service refuses the pair —
            // so the flag is a lie the UI acts on. Only Binance should pass this today.
            foreach (var p in TradingProviders())
            {
                if (p.Capabilities.HasFlag(ProviderCapabilities.OCO))
                    Assert.True(p is IOcoTradingProvider,
                        $"{p.GetType().Name} declares OCO but does not implement IOcoTradingProvider.");
            }
        }

        [Fact]
        public void InteractiveBrokers_does_not_advertise_unimplemented_capabilities()
        {
            var ib = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
            Assert.False(ib.Capabilities.HasFlag(ProviderCapabilities.OCO));
            Assert.False(ib.Capabilities.HasFlag(ProviderCapabilities.Brackets));
            Assert.False(ib.Capabilities.HasFlag(ProviderCapabilities.TrailingStop));
            // Single-leg protective orders ARE supported and must still be advertised.
            Assert.True(ib.SupportsStopLoss);
            Assert.True(ib.SupportsTakeProfit);
        }

        [Fact]
        public void Mexc_does_not_advertise_spot_brackets()
        {
            var mexc = new AccessibleTrader.Plugins.Mexc.MexcProvider();
            Assert.False(mexc.Capabilities.HasFlag(ProviderCapabilities.Brackets));
        }

        [Fact]
        public void Binance_remains_the_reference_OCO_implementation()
        {
            // Guards the invariant test above against vacuously passing (i.e. proves at least
            // one provider genuinely exercises the OCO-implies-IOcoTradingProvider branch).
            var binance = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            Assert.True(binance.Capabilities.HasFlag(ProviderCapabilities.OCO));
            Assert.True(binance is IOcoTradingProvider);
        }
    }
}
