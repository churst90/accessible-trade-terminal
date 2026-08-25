namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Which deployment a <see cref="DemoPolicy"/> governs.
    /// <list type="bullet">
    /// <item><b>Full</b> — desktop (MAUI) and local web: every feature on.</item>
    /// <item><b>Demo</b> — the public, anonymous <c>--demo</c> build: whitelisted, locked-down taste.</item>
    /// <item><b>Hosted</b> — the public, logged-in <c>--accounts</c> build: the FULL app MINUS the
    /// desktop-only differentiators (custom scripts, real-money trading, broker API keys, AI analyst).
    /// Paper trading and all indicators/markets/sound/settings/workspaces are ON.</item>
    /// </list>
    /// </summary>
    public enum HostMode { Full, Demo, Hosted }

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
        /// <summary>Which deployment this policy governs (see <see cref="HostMode"/>).</summary>
        public HostMode Mode { get; }

        /// <summary>True only for the locked-down, anonymous public <c>--demo</c> build.</summary>
        public bool IsDemo { get; }

        /// <summary>True for the hosted, logged-in <c>--accounts</c> build (paper-only full app).</summary>
        public bool IsHosted => Mode == HostMode.Hosted;

        /// <summary>
        /// True for the server-hosted builds (Demo + Hosted) that hold NO user broker keys, so they
        /// can only offer server-provided market data. These builds curate the provider/market/symbol
        /// lists to the sources that actually work server-side — Bitstamp (crypto, no key) and Twelve
        /// Data (stocks/forex, seeded key) — instead of showing dead-end "API key required" providers.
        /// False for Full (desktop/local), where the user supplies their own keys and everything is open.
        /// (Feature gates and timeframe/indicator breadth stay separate: Hosted keeps the full set.)
        /// </summary>
        public bool RestrictsData => Mode != HostMode.Full;

        public DemoPolicy(HostMode mode)
        {
            Mode   = mode;
            IsDemo = mode == HostMode.Demo;
        }

        /// <summary>Back-compat ctor: <c>true</c> → Demo, <c>false</c> → Full (desktop/local).</summary>
        public DemoPolicy(bool isDemo) : this(isDemo ? HostMode.Demo : HostMode.Full) { }

        // ── Whitelists ───────────────────────────────────────────────────────

        /// <summary>Providers a demo visitor may use. Bitstamp = live crypto (free,
        /// no key). TwelveData = stocks/forex/indices (free key, server-side).</summary>
        public IReadOnlyList<string> AllowedProviders { get; } =
            new[] { "Bitstamp", "Twelve Data" };

        /// <summary>
        /// Providers the HOSTED (logged-in) build offers — everything that actually works with no
        /// user-supplied key, which is a far wider set than the anonymous demo's two.
        ///
        /// Hosted used to reuse <see cref="AllowedProviders"/> because both modes share
        /// <see cref="RestrictsData"/>, so a logged-in user saw Bitstamp and Twelve Data and nothing
        /// else — no MEXC, no Binance, no Kraken, and no analytics at all — even though every
        /// provider below declares <c>RequiresApiKey == false</c> and needs nothing from the user.
        ///
        /// Membership rule, so this list stays honest: a provider belongs here if EITHER its public
        /// market data needs no credential at all, OR the server seeds its key at startup
        /// (Twelve Data, FRED — see Program.cs). Anything that would send the user to the API-keys
        /// modal must stay out: hosted has that modal switched off, so it can only ever render as a
        /// dead end reading "API key required".
        /// </summary>
        public IReadOnlyList<string> HostedProviders { get; } = new[]
        {
            // Crypto spot — public REST + public WebSocket, no key.
            "Binance", "Bitstamp", "Kraken", "MEXC",
            // Crypto, historical only (no public stream implemented).
            "Gemini", "KrakenFutures",
            // Stocks / forex / indices — server-seeded key.
            "Twelve Data",
            // Economic — server-seeded FRED key.
            "FRED", "SEC EDGAR",
            // On-chain / derivatives / sentiment — keyless public APIs.
            "CoinGecko", "CoinMetrics", "DefiLlama", "Mempool", "BGeometrics",
            "BinanceDerivatives", "BinanceVision", "OkxDerivatives", "Deribit",
            "CFTC", "FINRA", "AlternativeMe", "Wikipedia",
            // The user's own imported datasets. Note the SPACE — MyDataProvider.ProviderName is
            // "My Data", and this list is matched against a provider's Name. Omitting it left the
            // MyData market in HostedMarkets with an empty provider dropdown: selectable, and
            // then a dead end.
            "My Data",
        };

        /// <summary>Markets the demo exposes — each has a whitelisted provider with
        /// content: Crypto (Bitstamp), Stock + Forex (TwelveData). Markets without a
        /// whitelisted provider (OnChain, Economic, Derivatives, …) are hidden so the
        /// market dropdown can never land on one that filters to an empty provider
        /// list. Add "Index" here (and an index symbol below) to expose indices.</summary>
        public IReadOnlyList<string> AllowedMarkets { get; } =
            new[] { "Crypto", "Stock", "Forex" };

        /// <summary>
        /// Markets the HOSTED build exposes: every category in <see cref="HostedProviders"/> has at
        /// least one keyless provider, so none of these can land on an empty provider list.
        /// "MyData" is included because a logged-in user's imported datasets are their own, stored
        /// under their per-user directory — the one market that needs no provider at all.
        /// </summary>
        public IReadOnlyList<string> HostedMarkets { get; } =
            new[] { "Crypto", "Stock", "Forex", "Economic", "OnChain", "Derivatives", "Sentiment", "MyData" };

        /// <summary>The single provider the demo uses for a given market. Crypto is
        /// Bitstamp (free, live WebSocket); Stock and Forex are Twelve Data. This keeps
        /// the provider dropdown to one correct choice per market and — importantly —
        /// stops Twelve Data (which also advertises Crypto support) from being picked
        /// for BTC/USD, where its free-tier stream fails. Empty string = not a demo market.</summary>
        public string ProviderForMarket(string market) => (market ?? "") switch
        {
            "Crypto" => "Bitstamp",
            "Stock"  => "Twelve Data",
            "Forex"  => "Twelve Data",
            _        => ""
        };

        /// <summary>In the demo, only Bitstamp (crypto) has a working live WebSocket feed.
        /// Twelve Data's free tier has none, so attempting a live stream for it just loops
        /// on reconnects and can wedge the session — treat stock/forex as historical-only.
        /// Always true outside demo mode.</summary>
        public bool AllowsLiveStream(string provider) =>
            !RestrictsData
            || (IsDemo
                ? string.Equals(provider, "Bitstamp", StringComparison.OrdinalIgnoreCase)
                : HostedStreamingProviders.Contains(provider, StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// The hosted providers that actually implement a public WebSocket feed. Everything else in
        /// <see cref="HostedProviders"/> is historical-only, and asking it to stream just loops on
        /// reconnects — the failure mode Twelve Data's free tier taught us in the demo.
        /// </summary>
        public IReadOnlyList<string> HostedStreamingProviders { get; } =
            new[] { "Bitstamp", "Binance", "Kraken", "MEXC" };

        /// <summary>Symbol whitelist per provider. Compared normalised
        /// (letters+digits only, upper-case) so "BTC/USD" == "btcusd" == "BTC-USD".</summary>
        public IReadOnlyDictionary<string, string[]> AllowedSymbols { get; } =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Bitstamp"]   = new[] { "BTC/USD", "ETH/USD" },
                ["Twelve Data"] = new[] { "AAPL", "TSLA", "NVDA", "SPY", "EUR/USD" },
            };

        /// <summary>Curated symbols per market, used as the demo's symbol list directly
        /// when a provider's symbol-LIST endpoint is empty/unavailable even though its
        /// DATA endpoint works (Twelve Data's /stocks returns nothing on the free tier,
        /// yet time_series charts AAPL/SPY fine). Each demo market has one provider.</summary>
        public IReadOnlyDictionary<string, string[]> DemoSymbolsByMarket { get; } =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Crypto"] = new[] { "BTC/USD", "ETH/USD" },
                ["Stock"]  = new[] { "AAPL", "TSLA", "NVDA", "SPY" },
                ["Forex"]  = new[] { "EUR/USD" },
            };

        /// <summary>Curated symbols for a market (empty if not a demo market). Used as a
        /// fallback when the provider's own symbol list is empty. Call from RefreshSymbolsAsync.</summary>
        public IReadOnlyList<string> DemoSymbolsForMarket(string market) =>
            DemoSymbolsByMarket.TryGetValue(market ?? "", out var s) ? s : Array.Empty<string>();

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

        // ── Feature flags — three tiers (Full / Demo / Hosted) ───────────────
        // Desktop-only differentiators — OFF in BOTH Demo and Hosted (only Full has them):
        public bool AllowCustomScripts    => Mode == HostMode.Full;   // server-side Roslyn = RCE; desktop only
        public bool AllowApiKeysModal     => Mode == HostMode.Full;   // no real broker keys held server-side
        public bool AllowLiveTrading      => Mode == HostMode.Full;   // real money is desktop-only; hosted = paper
        public bool AllowAiAnalyst        => Mode == HostMode.Full;   // external-LLM cost; desktop / tiered
        public bool AllowStrategies       => Mode == HostMode.Full;   // experimental auto-trading — local/desktop power feature
        public bool AllowBackgroundMonitoring => Mode == HostMode.Full; // multi-workspace background eval: N polling loops + N indicator recomputes is a desktop power feature; hosted stays single-workspace by design

        // On a server, a user-supplied webhook URL or SMTP host/port is an SSRF
        // primitive: "deliver my alert to https://169.254.169.254/…" or "connect
        // to 10.0.0.5:6379" probes the network the SERVER sits on, with delivery
        // success/failure spoken back as the oracle. On the desktop the same
        // config only reaches the user's own machine and LAN — where pointing a
        // webhook at Home Assistant on 192.168.x is a legitimate feature.
        public bool BlockPrivateNetworkTargets => Mode != HostMode.Full;

        // Full-app features — ON in Hosted, OFF only in the locked Demo:
        public bool AllowTrading          => !IsDemo;   // paper-trading dashboard — the hosted educational core
        public bool AllowAlerts           => !IsDemo;
        public bool AllowWorkspaceSaveLoad=> !IsDemo;
        public bool AllowSymbolSearch     => !IsDemo;   // demo is whitelist-only, no free search
        public bool AllowSettingsPersist  => !IsDemo;   // demo never writes settings.json
        public bool AllowOrderBook        => !IsDemo;

        // Per Cody's demo decisions (2026-06-25): in the locked DEMO these are off, but in
        // Hosted they are ON — saving audio/visual/speech settings is the whole hosted draw.
        public bool AllowSettingsModal    => !IsDemo;   // hidden only in demo
        public bool AllowSoundDesigner    => !IsDemo;   // held back only in demo (download incentive)
        public bool AllowDrawingTools     => true;      // always available

        // ── Helpers (all permissive when !IsDemo) ────────────────────────────

        private static string Norm(string s) =>
            new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        /// <summary>The provider whitelist that applies to THIS mode. Full = no whitelist.</summary>
        private IReadOnlyList<string> ProviderWhitelist => IsDemo ? AllowedProviders : HostedProviders;

        /// <summary>The market whitelist that applies to THIS mode. Full = no whitelist.</summary>
        private IReadOnlyList<string> MarketWhitelist => IsDemo ? AllowedMarkets : HostedMarkets;

        public bool IsProviderAllowed(string provider) =>
            !RestrictsData || ProviderWhitelist.Contains(provider, StringComparer.OrdinalIgnoreCase);

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
            if (!RestrictsData) return providers;
            return providers.Where(IsProviderAllowed).ToList();
        }

        /// <summary>Intersect the discovered market list with the whitelist.
        /// Call from MarketOrchestrator.RefreshPipelineAsync.</summary>
        public IReadOnlyList<string> FilterMarkets(IReadOnlyList<string> markets)
        {
            if (!RestrictsData) return markets;
            return markets.Where(m => MarketWhitelist.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
        }
    }
}
