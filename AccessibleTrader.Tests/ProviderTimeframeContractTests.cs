using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Every provider's <c>NativelySupportedTimeframes</c> list must parse cleanly
    /// through <see cref="TimeframeUtility.ToSeconds"/>. A single typo ("1H"
    /// instead of "1h", "1D" instead of "1d") silently breaks the provider's
    /// fetch path because the historical fetcher uses <c>ToSeconds</c> to build
    /// the bar step — a zero/negative result falls through to an empty result
    /// with no error, so users just see "no data" for that provider.
    ///
    /// <para>
    /// **This test used to guard nothing.** It carried a hand-copied duplicate of every
    /// provider's timeframe list and asserted that *those strings* parsed — no provider was
    /// ever constructed. So the exact typo the paragraph above names as the motivating bug
    /// ("1h" becoming "1H" in a plugin) left the test's own "1h" parsing happily, and 30 rows
    /// of green guarded nothing in either direction. The rows now come from
    /// <see cref="ProviderRoster"/>, which reads <c>NativelySupportedTimeframes</c> off the
    /// real objects.
    /// </para>
    /// </summary>
    // Constructs real providers, which touch the global ApiKeys bridge — see ProviderRoster.
    [Collection("ProviderCredentialBridge")]
    public class ProviderTimeframeContractTests
    {
        // xUnit needs theory arguments it can serialize for test discovery, so the row carries
        // the type's full name and the provider is built inside the test body.
        public static IEnumerable<object[]> ProviderTypeNames() =>
            ProviderRoster.Types.Select(t => new object[] { t.FullName! });

        private static Sdk.Plugins.BaseMarketDataProvider Build(string typeName) =>
            ProviderRoster.All().First(p => p.GetType().FullName == typeName);

        [Theory]
        [MemberData(nameof(ProviderTypeNames))]
        public void EveryDeclaredTimeframe_ParsesToPositiveSeconds(string providerTypeName)
        {
            using var p = Build(providerTypeName);

            Assert.NotEmpty(p.NativelySupportedTimeframes);

            foreach (var tf in p.NativelySupportedTimeframes)
            {
                int seconds = TimeframeUtility.ToSeconds(tf);
                Assert.True(seconds > 0,
                    $"{p.Name} ({p.GetType().Name}): timeframe '{tf}' failed to parse "
                    + $"(ToSeconds returned {seconds}). "
                    + "A zero / negative result silently disables the provider's fetch path.");
            }
        }

        [Theory]
        [MemberData(nameof(ProviderTypeNames))]
        public void EveryDeclaredTimeframe_HasNoDuplicates(string providerTypeName)
        {
            using var p = Build(providerTypeName);
            var timeframes = p.NativelySupportedTimeframes;

            Assert.True(timeframes.Distinct().Count() == timeframes.Count,
                $"{p.Name} ({p.GetType().Name}): duplicate timeframes in NativelySupportedTimeframes — "
                + string.Join(", ", timeframes));
        }

        /// <summary>
        /// The theories above iterate a list; an empty roster would make both pass by doing
        /// nothing. The roster has its own guard in <see cref="ProviderRosterDriftTests"/>, but
        /// a sweep should also refuse to report success on zero work of its own.
        /// </summary>
        [Fact]
        public void TheSweep_CoversEveryProviderInTheRoster()
        {
            var rows = ProviderTypeNames().ToList();
            Assert.Equal(ProviderRoster.Types.Count, rows.Count);
            Assert.True(rows.Count >= 25, $"Only {rows.Count} providers swept.");
        }

        [Theory]
        [InlineData("1m", 60)]
        [InlineData("5m", 300)]
        [InlineData("1h", 3600)]
        [InlineData("4h", 14400)]
        [InlineData("1d", 86400)]
        [InlineData("1w", 604800)]
        [InlineData("1M", 2592000)]
        public void TimeframeUtility_KnownValues_Roundtrip(string tf, int expectedSeconds)
        {
            // Core pin for the utility itself — every plugin relies on this.
            Assert.Equal(expectedSeconds, TimeframeUtility.ToSeconds(tf));
        }

        [Theory]
        [InlineData("1H")]       // wrong case
        [InlineData("1hour")]    // not a single-letter unit
        [InlineData("1y")]       // unsupported unit
        [InlineData("5s")]       // sub-minute units not declared by any provider
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("abc")]
        public void TimeframeUtility_Unknown_ReturnsZero(string tf)
        {
            // Pin the "unknown → 0" contract. Providers rely on this: a zero
            // result in their fetch path is the signal to abort with an empty
            // result and log a warning.
            Assert.Equal(0, TimeframeUtility.ToSeconds(tf));
        }
    }
}
