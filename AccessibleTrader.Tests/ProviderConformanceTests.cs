using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Universal contract checks every trading provider must satisfy — the audit
    /// findings turned into a permanent gate. Constructs real providers (which touch
    /// the global ApiKeys bridge), so it shares the signed-path collection to stay
    /// serialized. Capability-flag consistency lives in
    /// <see cref="ProviderCapabilityHonestyTests"/>.
    ///
    /// <para>
    /// The roster used to be typed out here by hand, and it had fallen two providers behind the
    /// repo: **Gemini and KrakenFutures were absent**, so neither got the Name / Description /
    /// MaxBarsPerRequest / timeframe-parse / capability gate this file calls universal. It now
    /// enumerates <see cref="ProviderRoster"/>, which discovers providers from the build output.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderConformanceTests
    {
        // xUnit serializes theory arguments for discovery, so rows carry the type name and the
        // provider is constructed in the body.
        public static IEnumerable<object[]> TradingProviderTypeNames() =>
            ProviderRoster.Types
                .Where(t => typeof(ITradingProvider).IsAssignableFrom(t))
                .Select(t => new object[] { t.FullName! });

        public static IEnumerable<object[]> AllProviderTypeNames() =>
            ProviderRoster.Types.Select(t => new object[] { t.FullName! });

        private static BaseMarketDataProvider Build(string typeName) =>
            ProviderRoster.All().First(p => p.GetType().FullName == typeName);

        [Theory]
        [MemberData(nameof(AllProviderTypeNames))]
        public void Provider_satisfies_universal_contracts(string providerTypeName)
        {
            using var p = Build(providerTypeName);

            Assert.False(string.IsNullOrWhiteSpace(p.Name), $"{p.GetType().Name}: Name must be set.");
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

        /// <summary>
        /// A trading provider must also be reachable *as* one through the capability bridge —
        /// the order service asks for <see cref="ITradingProvider"/> via <c>GetCapability</c>,
        /// not by casting, so a provider that implements the interface but does not surface it
        /// there cannot place an order.
        /// </summary>
        [Theory]
        [MemberData(nameof(TradingProviderTypeNames))]
        public void Trading_provider_surfaces_its_trading_face(string providerTypeName)
        {
            using var p = Build(providerTypeName);
            Assert.NotNull(p.GetCapability<ITradingProvider>());
        }

        /// <summary>
        /// Anti-vacuity: both theories above iterate whatever the roster hands them, so an empty
        /// or shrunken roster reports success on no work. Two providers went missing from the
        /// old hand-written version of this list without anything noticing.
        /// </summary>
        [Fact]
        public void The_sweep_covers_every_provider_and_every_trading_provider()
        {
            Assert.Equal(ProviderRoster.Types.Count, AllProviderTypeNames().Count());
            Assert.True(AllProviderTypeNames().Count() >= 25,
                $"Only {AllProviderTypeNames().Count()} providers swept.");

            var trading = TradingProviderTypeNames().Select(r => (string)r[0]).ToList();
            Assert.True(trading.Count >= 12, $"Only {trading.Count} trading providers swept.");

            // The two that were missing, named explicitly — this file's own regression.
            Assert.Contains(trading, n => n.Contains("Gemini"));
            Assert.Contains(trading, n => n.Contains("KrakenFutures"));
        }
    }
}
