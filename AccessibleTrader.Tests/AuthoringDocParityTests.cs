using System.Globalization;
using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The authoring docs promise (PLUGIN_AUTHORING.md line 5): "All APIs described
    /// here are taken directly from the current source code." The 2026-08-21 audit
    /// found that promise broken in ways that COMPILE for a plugin author and then
    /// fail silently at runtime: GetDefaultLevels documented with a tuple return
    /// type in six places (a default interface method, so the wrong signature
    /// declares an unrelated method and the empty default wins — levels never draw,
    /// never sound), four override samples that drop tuple element names (CS8139),
    /// epoch conversions that read the machine's local zone, a member-hiding
    /// anti-pattern the SDK deliberately made impossible to express, and an entire
    /// trading-provider surface documented nowhere. These scans pin the fixes.
    ///
    /// All scans are empty-baseline with a vacuity floor: each asserts the doc
    /// still discusses the topic before asserting the broken spelling is gone, so
    /// deleting the section can't pass as fixing it.
    /// </summary>
    public class AuthoringDocParityTests
    {
        private static string Doc(string name) =>
            File.ReadAllText(Path.Combine(RepoPaths.RepoRoot(), "docs", name));

        private static readonly string[] AuthoringDocs =
        {
            "PLUGIN_AUTHORING.md", "PROVIDER_AUTHORING.md",
            "SDK_GUIDE.md", "ANALYTICS_DATA_PROVIDERS.md",
        };

        [Fact]
        public void GetDefaultLevels_is_documented_with_the_sdk_signature_not_a_tuple_list()
        {
            string doc = Doc("PLUGIN_AUTHORING.md");

            // Vacuity floor: the doc still teaches the method.
            Assert.True(Regex.Matches(doc, "GetDefaultLevels").Count >= 4,
                "PLUGIN_AUTHORING.md no longer documents GetDefaultLevels — this scan is checking nothing.");

            Assert.Contains("List<LevelDescriptor> GetDefaultLevels", doc);
            Assert.DoesNotContain("(string Name, double Value, string ColorHex, DashStyle Dash)", doc);
        }

        [Fact]
        public void PluginAuthoring_does_not_claim_the_removed_static_levels_fallback()
        {
            // The doc used to say an empty GetDefaultLevels falls back to a static
            // IndicatorReferenceLevels table — which is how a custom indicator was
            // documented to inherit RSI's levels. SeriesManagementService has no
            // such fallback; the doc must not resurrect it.
            Assert.DoesNotContain("IndicatorReferenceLevels", Doc("PLUGIN_AUTHORING.md"));
        }

        [Fact]
        public void CipherB_doc_sample_levels_match_the_provider_source()
        {
            // The doc block is titled "actual values from source" — hold it to that.
            string doc = Doc("PLUGIN_AUTHORING.md");
            var levels = new AccessibleTrader.Core.Services.Indicators.CipherBProvider()
                .GetDefaultLevels("CIPHER_B");

            Assert.NotEmpty(levels);
            foreach (var level in levels)
            {
                string expected = $"new(\"{level.Name}\"";
                Assert.Contains(expected, doc);
                Assert.Contains(level.ColorHex, doc);
                Assert.Contains(level.Value.ToString("0.0", CultureInfo.InvariantCulture), doc);
            }
        }

        [Fact]
        public void Override_samples_keep_the_sdk_tuple_element_names()
        {
            // Dropping the names from FetchOhlcvAsync / GetOrderBookAsync overrides
            // is CS8139 — four documented samples shipped that way and none compiled.
            foreach (var name in AuthoringDocs)
            {
                string doc = Doc(name);
                Assert.DoesNotContain("List<(long, double)>", doc);
                Assert.DoesNotContain("(List<Ohlcv>,", doc);
                Assert.DoesNotContain("(List<OrderBookEntry>,", doc);
            }

            // Vacuity floor: the corrected spelling exists somewhere.
            Assert.Contains("List<(long Timestamp, double Volume)> Volume", Doc("PROVIDER_AUTHORING.md"));
        }

        [Fact]
        public void Epoch_conversion_samples_pin_the_offset()
        {
            // new DateTimeOffset(dt) reads the machine's LOCAL zone when dt.Kind is
            // Unspecified: the sample passes on a UTC dev box and shifts the volume
            // pane against the candles for everyone else. Samples must use the
            // two-argument form with TimeSpan.Zero.
            var bare = new Regex(@"new DateTimeOffset\([^,)]*\)\s*\.ToUnixTime");
            foreach (var name in AuthoringDocs)
            {
                var hits = bare.Matches(Doc(name)).Select(m => m.Value).ToList();
                Assert.True(hits.Count == 0,
                    $"{name} contains machine-timezone-dependent epoch conversions: {string.Join(" | ", hits)}");
            }

            // Vacuity floor: the pinned form is present, so the regex is aimed at
            // live samples rather than a topic the docs dropped.
            Assert.Contains("TimeSpan.Zero).ToUnixTimeMilliseconds", Doc("PROVIDER_AUTHORING.md"));
        }

        [Fact]
        public void SdkGuide_declares_margin_futures_via_flags_not_member_hiding()
        {
            string doc = Doc("SDK_GUIDE.md");

            // Vacuity floor: §5.2 still documents the trading surface.
            Assert.Contains("ITradingProvider", doc);

            // The bools are non-virtual on the base and derived from the flags;
            // a sample declaring them fresh teaches member hiding — it compiles,
            // and the declared value is dead code.
            Assert.DoesNotContain("public bool SupportsMarginTrading", doc);
            Assert.DoesNotContain("public bool SupportsFuturesTrading", doc);
            Assert.Contains("ProviderCapabilities.MarginTrading", doc);
        }

        [Fact]
        public void ProviderAuthoring_covers_the_trading_surface_the_audit_found_absent()
        {
            // The audit's finding was causal, not cosmetic: "this is the root cause
            // of the provider defects above". Every token below was verified absent
            // on 2026-08-21; each is now required to stay present so the sections
            // cannot quietly disappear.
            string doc = Doc("PROVIDER_AUTHORING.md");
            string[] mustCover =
            {
                "ITradingProvider", "PlaceOrderAsync", "ORDER_FAILED", "OrderStatus",
                "ProviderCapabilities", "IWalletProvider", "IWithdrawalProvider",
                "RestSigning", "SymbolFormat", "ReconnectingWebSocket", "SurfaceError",
                "SupportsOrderEventStreaming", "InvariantCulture", "LiveTickStyle",
            };
            var missing = mustCover.Where(t => !doc.Contains(t, StringComparison.Ordinal)).ToList();
            Assert.True(missing.Count == 0,
                "PROVIDER_AUTHORING.md lost coverage of: " + string.Join(", ", missing));
        }
    }
}
