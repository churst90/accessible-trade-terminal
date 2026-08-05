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
        //
        // Coinbase, Bitstamp and Oanda were MISSING from this list, which is why every
        // invariant below was passing on seven of ten providers while calling itself a
        // sweep. Oanda is the one that mattered: it declared TrailingStop with no
        // trailing implementation at all, and no test here could see it because Oanda
        // was not enumerated. A sweep that does not enumerate everything is a spot
        // check wearing a sweep's name — see AllTradingProvidersAreEnumeratedHere.
        private static IEnumerable<BaseMarketDataProvider> TradingProviders()
        {
            yield return new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
            yield return new AccessibleTrader.Plugins.Mexc.MexcProvider();
            yield return new AccessibleTrader.Plugins.Binance.BinanceProvider();
            yield return new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            yield return new AccessibleTrader.Plugins.Tradier.TradierProvider();
            yield return new AccessibleTrader.Plugins.Schwab.SchwabProvider();
            yield return new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
            yield return new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            yield return new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
            yield return new AccessibleTrader.Plugins.Oanda.OandaProvider();
        }

        /// <summary>
        /// The list above must not fall behind the repo. A hand-maintained roster
        /// silently stops covering anything added after it was written, and every
        /// assertion that iterates it keeps passing — which is exactly what happened.
        /// The audit's own file sweep is the cross-check.
        /// </summary>
        [Fact]
        public void AllTradingProvidersAreEnumeratedHere()
        {
            var onDisk = AccessibleTrader.StrategyLab.ProviderCapabilityAudit
                .Run(RepoRoot()).Select(a => a.Name).ToHashSet();
            var enumerated = TradingProviders().Select(p => p.GetType().Name).ToHashSet();

            Assert.True(onDisk.SetEquals(enumerated),
                "TradingProviders() has drifted from the trading providers in the repo. "
              + "Missing here: " + string.Join(", ", onDisk.Except(enumerated).DefaultIfEmpty("none"))
              + ". Listed but not found on disk: "
              + string.Join(", ", enumerated.Except(onDisk).DefaultIfEmpty("none")));
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
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
        public void Leverage_requires_a_product_for_it_to_apply_to()
        {
            // Leverage needs EITHER spot margin OR futures — MEXC's is futures-only,
            // Kraken's is spot margin, and a leverage selector with neither product
            // behind it has nothing to multiply.
            //
            // This used to compare the flag against the SupportsMarginTrading and
            // SupportsFuturesTrading BOOLS, which were independently overridable and
            // so could disagree with it. Those bools are now derived from the flags,
            // which makes that particular disagreement impossible to express rather
            // than merely detectable — so the check moved up a level, to whether the
            // flags themselves are coherent with each other.
            foreach (var p in TradingProviders())
            {
                if (!p.Capabilities.HasFlag(ProviderCapabilities.Leverage)) continue;

                Assert.True(
                    p.Capabilities.HasFlag(ProviderCapabilities.MarginTrading) ||
                    p.Capabilities.HasFlag(ProviderCapabilities.FuturesTrading),
                    $"{p.GetType().Name} declares Leverage with neither MarginTrading nor "
                  + "FuturesTrading — there is no product for the leverage to apply to.");

                Assert.True(p.MaxLeverage > 1.0,
                    $"{p.GetType().Name} declares Leverage but MaxLeverage is {p.MaxLeverage}, "
                  + "so the selector has exactly one position.");
            }
        }

        [Fact]
        public void The_derived_bools_cannot_disagree_with_the_flags()
        {
            // Guards the fold itself. If anyone reintroduces an override of these,
            // the two-fields-one-fact problem returns and this catches it.
            foreach (var p in TradingProviders())
            {
                Assert.Equal(p.Capabilities.HasFlag(ProviderCapabilities.MarginTrading), p.SupportsMarginTrading);
                Assert.Equal(p.Capabilities.HasFlag(ProviderCapabilities.FuturesTrading), p.SupportsFuturesTrading);
            }
        }

        [Fact]
        public void Oanda_no_longer_advertises_a_trailing_stop_it_never_implemented()
        {
            // Found by the static audit: the flag was declared and the string "Trail"
            // appeared nowhere else in the file. The dashboard gates its trailing
            // distance and mode fields on this, so the controls rendered and the order
            // went out with no trail attached. OANDA's API does offer trailing stops,
            // so this is a gap to fill, not a capability the venue lacks — when it is
            // implemented, this test flips to asserting the flag is present.
            var oanda = new AccessibleTrader.Plugins.Oanda.OandaProvider();

            Assert.False(oanda.Capabilities.HasFlag(ProviderCapabilities.TrailingStop));
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
