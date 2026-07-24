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
    // Constructs real provider instances (Kraken/Tradier/etc.), which touch the same global
    // provider state (PluginHostServices.ApiKeys bridge) as the signed-path tests — so it must
    // share their collection to stay serialized, or it races BrokerParityTests and flakes them.
    [Collection("ProviderCredentialBridge")]
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

        // ── Capability model consistency (single source of truth) ────────────
        // The model is dual-sourced: some UI gates read the ProviderCapabilities
        // FLAGS, others read the SupportsX BOOLS. These invariants keep the two in
        // agreement so a flag can't disagree with the behaviour it implies.

        [Fact]
        public void Implementing_IOcoTradingProvider_requires_declaring_the_OCO_flag()
        {
            foreach (var p in TradingProviders())
                if (p is IOcoTradingProvider)
                    Assert.True(p.Capabilities.HasFlag(ProviderCapabilities.OCO),
                        $"{p.GetType().Name} implements IOcoTradingProvider but does not declare the OCO flag.");
        }

        [Fact]
        public void Leverage_flag_agrees_with_margin_or_futures_trading()
        {
            // Leverage is available when EITHER spot margin OR futures is supported
            // (MEXC's leverage is futures-only; Kraken's is spot margin). The flag and
            // the bools gate the same feature from different call sites, so they must
            // agree, and leverage must actually exceed 1x where declared.
            foreach (var p in TradingProviders())
            {
                bool flag = p.Capabilities.HasFlag(ProviderCapabilities.Leverage);
                bool leveraged = p.SupportsMarginTrading || p.SupportsFuturesTrading;
                Assert.True(flag == leveraged,
                    $"{p.GetType().Name}: Leverage flag ({flag}) disagrees with margin-or-futures ({leveraged}).");
                if (flag)
                    Assert.True(p.MaxLeverage > 1.0,
                        $"{p.GetType().Name} declares Leverage but MaxLeverage is {p.MaxLeverage}.");
            }
        }

        [Fact]
        public void Brackets_flag_requires_a_protective_leg()
        {
            foreach (var p in TradingProviders())
                if (p.Capabilities.HasFlag(ProviderCapabilities.Brackets))
                    Assert.True(p.SupportsStopLoss || p.SupportsTakeProfit,
                        $"{p.GetType().Name} declares Brackets but supports neither stop-loss nor take-profit.");
        }
    }
}
