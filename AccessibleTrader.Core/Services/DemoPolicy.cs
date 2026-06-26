using System;
using System.Collections.Generic;
using System.Linq;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Central, server-side policy for the public website demo (the build run with
    /// <c>--demo</c> and reverse-proxied under <c>/app/</c> on trade.codyhurst.com).
    ///
    /// The demo runs the REAL interface (MainLayout + Toolbar + ChartArea +
    /// IndicatorBar) — it is NOT a bespoke page. Every restriction the demo
    /// enforces is declared here, in one place, so the real UI runs unchanged
    /// except for consulting this object.
    ///
    /// IMPORTANT: when <see cref="IsDemo"/> is false this object is a no-op — every
    /// Is*Allowed() returns true and every Allow* flag is true — so the full app is
    /// completely unaffected. Register a singleton with isDemo:false in the MAUI /
    /// desktop heads, and isDemo:(the --demo flag) in the WebHost.
    /// </summary>
    public sealed class DemoPolicy
    {
        public bool IsDemo { get; }

        public DemoPolicy(bool isDemo) => IsDemo = isDemo;

        // ── Whitelists ───────────────────────────────────────────────────────

        /// <summary>Providers a demo visitor may use. Bitstamp = live crypto (free,
        /// no key). TwelveData = stocks/forex/indices (free key, server-side).</summary>
        public IReadOnlyList<string> AllowedProviders { get; } =
            new[] { "Bitstamp", "TwelveData" };

        /// <summary>Symbol whitelist per provider. Compared normalised
        /// (letters+digits only, upper-case) so "BTC/USD" == "btcusd" == "BTC-USD".</summary>
        public IReadOnlyDictionary<string, string[]> AllowedSymbols { get; } =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Bitstamp"]   = new[] { "BTC/USD", "ETH/USD" },
                ["TwelveData"] = new[] { "AAPL", "TSLA", "NVDA", "SPY", "EUR/USD" },
            };

        /// <summary>The only two timeframes the demo exposes (rendered as buttons).</summary>
        public IReadOnlyList<string> AllowedTimeframes { get; } = new[] { "4h", "1d" };

        /// <summary>Indicator <c>Code</c>s a demo visitor may add. Codes come from the
        /// indicator providers in AccessibleTrader.Core/Services/Indicators:
        ///   Ema, Sma  (SkenderTrendProvider)
        ///   Bb        (Bollinger Bands — SkenderBandProvider)
        ///   Rsi       (SkenderBoundedOscillatorProvider)
        ///   Macd      (SkenderZeroCrossProvider)
        ///   Vwap      (SkenderTrendProvider)
        ///   VPVR      (Volume Profile, visible range — ProfileIndicatorProvider)
        /// </summary>
        public IReadOnlyList<string> AllowedIndicatorCodes { get; } =
            new[] { "Ema", "Sma", "Bb", "Rsi", "Macd", "Vwap", "VPVR" };

        // ── Default selection the demo opens on ──────────────────────────────
        public string DefaultMarket    => "Crypto";
        public string DefaultProvider  => "Bitstamp";
        public string DefaultSymbol    => "BTC/USD";
        public string DefaultTimeframe => "1d";

        // ── Feature flags (everything not explicitly allowed is OFF in demo) ──
        public bool AllowTrading          => !IsDemo;
        public bool AllowStrategies       => !IsDemo;
        public bool AllowAlerts           => !IsDemo;
        public bool AllowCustomScripts    => !IsDemo;
        public bool AllowAiAnalyst        => !IsDemo;
        public bool AllowApiKeysModal     => !IsDemo;
        public bool AllowWorkspaceSaveLoad=> !IsDemo;
        public bool AllowSymbolSearch     => !IsDemo;   // demo is whitelist-only, no free search
        public bool AllowSettingsPersist  => !IsDemo;   // demo never writes settings.json
        public bool AllowOrderBook        => !IsDemo;

        // Per Cody's demo decisions (2026-06-25):
        //  - Settings modal hidden in demo (so its toggles can't confuse the taste).
        //  - Sound Designer held back as a download incentive.
        //  - Drawing tools kept ON for a richer taste.
        public bool AllowSettingsModal    => !IsDemo;   // hide the Settings modal in demo
        public bool AllowSoundDesigner    => !IsDemo;   // hold the Sound Designer for the download
        public bool AllowDrawingTools     => true;      // a taste of drawing

        // ── Helpers (all permissive when !IsDemo) ────────────────────────────

        private static string Norm(string s) =>
            new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        public bool IsProviderAllowed(string provider) =>
            !IsDemo || AllowedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase);

        public bool IsTimeframeAllowed(string timeframe) =>
            !IsDemo || AllowedTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase);

        public bool IsIndicatorAllowed(string code) =>
            !IsDemo || AllowedIndicatorCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

        public bool IsSymbolAllowed(string provider, string symbol)
        {
            if (!IsDemo) return true;
            if (!AllowedSymbols.TryGetValue(provider, out var allowed)) return false;
            var n = Norm(symbol);
            return allowed.Any(a => Norm(a) == n);
        }

        /// <summary>Intersect a provider's full symbol list with the whitelist
        /// (preserving the provider's own formatting). Returns the input unchanged
        /// outside demo mode. Call from MarketOrchestrator.RefreshSymbolsAsync.</summary>
        public IReadOnlyList<string> FilterSymbols(string provider, IReadOnlyList<string> symbols)
        {
            if (!IsDemo) return symbols;
            return symbols.Where(s => IsSymbolAllowed(provider, s)).ToList();
        }

        /// <summary>Intersect the discovered provider list with the whitelist.
        /// Call from MarketOrchestrator.RefreshProvidersAsync.</summary>
        public IReadOnlyList<string> FilterProviders(IReadOnlyList<string> providers)
        {
            if (!IsDemo) return providers;
            return providers.Where(IsProviderAllowed).ToList();
        }
    }
}
