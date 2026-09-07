namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// <b>The keys a host puts in <c>IProviderPlugin.Configure</c>'s dictionary.</b>
    ///
    /// <para>
    /// ── Why this file exists ──────────────────────────────────────────────────
    /// <c>Configure(Dictionary&lt;string, string&gt;)</c> is stringly-typed and, until
    /// 2026-09-07, nothing anywhere said what the keys were or what the values meant. Each
    /// plugin invented its own reading, and the fleet ended up with FOUR vocabularies for one
    /// fact — whether this credential is for the real venue or a practice one:
    /// </para>
    /// <list type="bullet">
    ///   <item>Gemini and Kraken Futures compared against <c>"Paper"</c>.</item>
    ///   <item>Alpaca against <c>"Live"</c>, Oanda against <c>"live"</c>.</item>
    ///   <item>Tradier against <c>"sandbox"</c> — a value the API-keys dialog, a two-option
    ///   dropdown, could never produce. <b>Every Tradier profile signed against the live
    ///   broker.</b></item>
    ///   <item>Binance ignored the field entirely and read a separate <see cref="Testnet"/> key
    ///   that only a unit test had ever supplied. <b>A Binance profile marked Paper signed
    ///   against live Binance.</b></item>
    /// </list>
    ///
    /// <para>
    /// Two of those four failed toward REAL MONEY. That is what an undocumented contract costs,
    /// and naming the keys is the cheapest part of not paying it again.
    /// </para>
    ///
    /// <para>
    /// ── The contract ──────────────────────────────────────────────────────────
    /// The host guarantees every key below is present on every <c>Configure</c> call, and that
    /// <see cref="Environment"/> is exactly <see cref="Live"/> or <see cref="Paper"/> — never
    /// empty, never anything else. A plugin should branch on <see cref="Live"/> and treat
    /// EVERYTHING ELSE as practice, so that an unrecognised value fails toward the safe side.
    /// The reverse polarity is what turned legacy profiles, which carry no environment at all,
    /// into live-money credentials.
    /// </para>
    ///
    /// <para>
    /// These are constants rather than an enum because the dictionary is the plugin ABI: a
    /// third-party plugin compiled against an older SDK still receives the same strings.
    /// </para>
    /// </summary>
    public static class ProviderConfigKeys
    {
        /// <summary>The API key / client id.</summary>
        public const string ApiKey = "ApiKey";

        /// <summary>The API secret. Empty string when the venue uses a bearer token only.</summary>
        public const string ApiSecret = "ApiSecret";

        /// <summary>The passphrase, for venues that use one (Coinbase). Empty otherwise.</summary>
        public const string Passphrase = "Passphrase";

        /// <summary>
        /// Exactly <see cref="Live"/> or <see cref="Paper"/>. Branch on <see cref="Live"/> and
        /// treat everything else as practice.
        /// </summary>
        public const string Environment = "Environment";

        /// <summary>
        /// <c>"true"</c> or <c>"false"</c> — the same fact as <see cref="Environment"/>, spelled
        /// the way Binance's plugin reads it. Derived by the host from the environment, so the
        /// two can never disagree; a plugin should read whichever it prefers, not both.
        /// </summary>
        public const string Testnet = "Testnet";

        /// <summary>The real venue, with real money.</summary>
        public const string Live = "Live";

        /// <summary>Practice: a sandbox, testnet, demo or paper environment.</summary>
        public const string Paper = "Paper";

        /// <summary>
        /// Whether a config dictionary asks for the REAL venue. The one place this question
        /// should be answered, so a plugin cannot get the polarity backwards.
        /// </summary>
        public static bool IsLive(IReadOnlyDictionary<string, string> config) =>
            config.TryGetValue(Environment, out var env) && IsLive(env);

        /// <summary>
        /// The same question asked of a bare environment string — the value a stored key carries.
        /// <b>The polarity is fail-safe and must stay that way:</b> only the exact word
        /// <see cref="Live"/> means the real venue, so an empty environment (every profile stored
        /// before that field existed) reads as practice rather than as real money.
        /// </summary>
        public static bool IsLive(string? environment) =>
            string.Equals(environment, Live, System.StringComparison.OrdinalIgnoreCase);
    }
}
