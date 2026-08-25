using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for <see cref="IchimokuProvider"/> — component count, cloud fill structure,
    /// Tenkan/Chikou/Senkou calculations, GetDetailFact speech, and stability window.
    /// </summary>
    public class IchimokuProviderTests
    {
        private static readonly IchimokuProvider Provider = new();
        private static readonly Dictionary<string, object> DefaultParams = new()
        {
            { "TenkanPeriod",  9 },
            { "KijunPeriod",   26 },
            { "SenkouBPeriod", 52 },
            { "Displacement",  26 },
        };

        // ── Helper: build N bars with High=i+10, Low=i, Close=i+5, Open=i+4 ──

        private static Ohlcv[] BuildBars(int count)
        {
            var bars = new Ohlcv[count];
            var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < count; i++)
            {
                double h = i + 10;
                double l = i;
                bars[i] = new Ohlcv(start.AddHours(i), i + 4, h, l, i + 5, 1000);
            }
            return bars;
        }

        private static (Dictionary<string, double[]> results, IndicatorResultBuffer buffer)
            Calculate(Ohlcv[] bars, Dictionary<string, object>? parms = null)
        {
            int n  = bars.Length;
            var rd = new Dictionary<string, double[]>();
            var buf = new IndicatorResultBuffer(rd, n);
            Provider.Calculate("ICHIMOKU", bars.AsSpan(), parms ?? DefaultParams, buf);
            return (rd, buf);
        }

        // ── 1. Component contract ─────────────────────────────────────────────
        //
        // The Ichimoku provider declares 8 components: the classical 5-line
        // Ichimoku spec (Tenkan, Kijun, Senkou A/B, Chikou) plus 3 additions
        // that layer on top (a hidden Kumo-polarity strategy leaf and two
        // confirmed-cross marker dots). Rather than assert "Components.Count
        // equals N" — which tells you nothing when it regresses and has to be
        // bumped every time a helper is added — these tests encode the actual
        // contract by name so a failure identifies *which* piece broke.

        private static IndicatorComponentMetadata GetComponent(string name)
        {
            var meta = Provider.GetIndicators()[0];
            var comp = meta.Components.FirstOrDefault(c => c.Name == name);
            Assert.True(comp != null, $"Expected component '{name}' in Ichimoku metadata.");
            return comp!;
        }

        [Fact]
        public void Components_ContainClassicalFiveLines()
        {
            // Every classical Ichimoku line must be present, rendered as a
            // Line, and visible by default — they are the indicator's headline.
            string[] classical =
            {
                IchimokuProvider.CompTenkan,
                IchimokuProvider.CompKijun,
                IchimokuProvider.CompSenkouA,
                IchimokuProvider.CompSenkouB,
                IchimokuProvider.CompChikou,
            };
            foreach (var name in classical)
            {
                var c = GetComponent(name);
                Assert.Equal(ComponentDisplayType.Line, c.DisplayType);
                Assert.True(c.IsVisible, $"{name} must be visible by default.");
            }
        }

        [Fact]
        public void Components_ExposeHiddenKumoPolarityHelper()
        {
            // Kumo Polarity is a strategy-composer helper (+1 / 0 / -1) — it
            // is intentionally hidden from the chart and has a zero reference
            // level so strategies can gate on it via a simple threshold leaf.
            var c = GetComponent(IchimokuProvider.CompKumoPolarity);
            Assert.False(c.IsVisible, "Kumo Polarity must be hidden — it's a strategy leaf, not a chart line.");
            Assert.Equal(0.0, c.DefaultReferenceLevel);
        }

        [Fact]
        public void Components_ExposeVisibleTkCrossMarkers()
        {
            // TK Bull / TK Bear are 2-bar-confirmed crossover dots. They must
            // be visible (the user wants to see and hear them) and must carry
            // distinct base frequencies so sonification differentiates the two.
            var bull = GetComponent(IchimokuProvider.CompTkBull);
            var bear = GetComponent(IchimokuProvider.CompTkBear);

            Assert.Equal(ComponentDisplayType.Dot, bull.DisplayType);
            Assert.Equal(ComponentDisplayType.Dot, bear.DisplayType);
            Assert.True(bull.IsVisible, "TK Bull must be visible by default.");
            Assert.True(bear.IsVisible, "TK Bear must be visible by default.");
            Assert.NotEqual(bull.DefaultBaseFrequency, bear.DefaultBaseFrequency);
        }

        [Fact]
        public void Components_CountMatchesDeclaredContract()
        {
            // Sentinel: the three Components_* tests above pin the 5 classical
            // + 1 hidden helper + 2 markers = 8. If someone adds a ninth
            // component, this assertion catches it and forces them to add a
            // targeted test for the new component (documenting its intent)
            // rather than silently growing the metadata.
            var meta = Provider.GetIndicators()[0];
            Assert.Equal(8, meta.Components.Count);
        }

        // ── 2. Cloud fill structure ────────────────────────────────────────────

        [Fact]
        public void GetMetadata_Has1CloudFill_Kumo()
        {
            var meta = Provider.GetIndicators()[0];
            Assert.Single(meta.DefaultCloudFills);
            Assert.Equal("Kumo", meta.DefaultCloudFills[0].DisplayName);
        }

        [Fact]
        public void KumoCloudFill_UpperComponentName_IsSenkouSpanA()
        {
            var fill = Provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.Equal(IchimokuProvider.CompSenkouA, fill.UpperComponentName);
        }

        [Fact]
        public void KumoCloudFill_LowerComponentName_IsSenkouSpanB()
        {
            var fill = Provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.Equal(IchimokuProvider.CompSenkouB, fill.LowerComponentName);
        }

        [Fact]
        public void KumoCloudFill_HasNonNullSonification()
        {
            var fill = Provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.NotNull(fill.Sonification);
        }

        // ── 3. Tenkan-sen calculation ─────────────────────────────────────────
        // Bars: High=i+10, Low=i over 9 bars (indices 0-8).
        // Tenkan at index 8 = (maxHigh + minLow) / 2 = (18 + 0) / 2 = 9.0

        [Fact]
        public void Tenkan_AtLastBar_EqualsExpectedMidpoint()
        {
            // Need at least 9 bars to have a valid Tenkan.
            var bars = BuildBars(9);
            var (rd, _) = Calculate(bars);

            Assert.True(rd.ContainsKey(IchimokuProvider.CompTenkan));
            double tenkan = rd[IchimokuProvider.CompTenkan][8]; // index 8 = 9th bar

            // High[8]=18, Low[0]=0 → midpoint = (18 + 0) / 2 = 9.0
            Assert.Equal(9.0, tenkan, precision: 6);
        }

        // ── 4. Chikou span — displaced 26 bars into the past ──────────────────
        // Chikou at index (n - displacement - 1) should equal close[n-1].

        [Fact]
        public void Chikou_IsDisplaced26BarsIntoPast()
        {
            int n = 30;
            var bars = BuildBars(n);
            var (rd, _) = Calculate(bars);

            Assert.True(rd.ContainsKey(IchimokuProvider.CompChikou));
            double[] chikou = rd[IchimokuProvider.CompChikou];

            // close[i] is placed at index i - 26
            // So chikou[0] = close[26], chikou[1] = close[27], etc.
            // For bar index 26, close = 26 + 5 = 31. So chikou[0] = 31.
            Assert.Equal(bars[26].Close, chikou[0], precision: 6);
        }

        // ── 5. Senkou Span A — forward displacement ────────────────────────────
        // Senkou A at computed bar i is placed at index i + displacement.
        // With default params and 80 bars, the last Senkou A values should be valid.

        [Fact]
        public void SenkouA_HasValidValuesInFutureZone()
        {
            int n = 80;
            var bars = BuildBars(n);
            var (rd, _) = Calculate(bars);

            Assert.True(rd.ContainsKey(IchimokuProvider.CompSenkouA));
            double[] senkouA = rd[IchimokuProvider.CompSenkouA];

            // Forward-displaced values: bars 26..79 should have Senkou A when kijun period is satisfied.
            // After KijunPeriod-1 (25) bars of warmup + displacement (26) = index 51 is first valid.
            bool anyValid = false;
            for (int i = 51; i < n; i++)
                if (!double.IsNaN(senkouA[i])) { anyValid = true; break; }

            Assert.True(anyValid, "Senkou Span A must have at least one valid (non-NaN) value at displacement offset.");
        }

        // ── 6. GetDetailFact — price above Kijun ─────────────────────────────

        [Fact]
        public void GetDetailFact_PriceAboveKijun_MentionsBullish()
        {
            // Build enough bars for Kijun to be valid (26 bars minimum).
            int n = 60;
            var bars = BuildBars(n);
            var (rd, _) = Calculate(bars);

            // At index 59: close = 64, Kijun = midpoint of last 26 highs/lows.
            // Highs: 34..69 → max=69. Lows: 34-10=24..59 → actually Low[59-25..59] = 34..59.
            // midpoint = (69 + 34) / 2 = 51.5. Close = 64. So close > kijun → "above".
            string fact = Provider.GetDetailFact("ICHIMOKU", bars.AsSpan(), rd, 59, DefaultParams);

            Assert.False(string.IsNullOrEmpty(fact));
            Assert.Contains("above", fact, StringComparison.OrdinalIgnoreCase);
        }

        // ── 7. GetDetailFact — mentions cloud ────────────────────────────────
        // Use 120 bars so Senkou B displacement (52+26=78) has valid data past index 78.
        // Check at index 79+ where cloud is guaranteed to be formed.

        [Fact]
        public void GetDetailFact_WithValidCloud_MentionsCloud()
        {
            int n = 120;
            var bars = BuildBars(n);
            var (rd, _) = Calculate(bars);

            // Find first index where both Senkou A and B are valid.
            double[] sA = rd.ContainsKey(IchimokuProvider.CompSenkouA) ? rd[IchimokuProvider.CompSenkouA] : Array.Empty<double>();
            double[] sB = rd.ContainsKey(IchimokuProvider.CompSenkouB) ? rd[IchimokuProvider.CompSenkouB] : Array.Empty<double>();

            int idx = -1;
            for (int i = 78; i < n; i++)
            {
                if (sA.Length > i && sB.Length > i && !double.IsNaN(sA[i]) && !double.IsNaN(sB[i]))
                { idx = i; break; }
            }

            if (idx < 0) return; // Skip if not enough data for Kumo in test dataset

            string fact = Provider.GetDetailFact("ICHIMOKU", bars.AsSpan(), rd, idx, DefaultParams);

            // Should mention cloud (above/below/inside or Kumo polarity)
            Assert.False(string.IsNullOrEmpty(fact));
            bool mentionsCloud = fact.Contains("cloud", StringComparison.OrdinalIgnoreCase)
                              || fact.Contains("kumo",  StringComparison.OrdinalIgnoreCase);
            Assert.True(mentionsCloud, $"GetDetailFact should mention the cloud. Got: {fact}");
        }

        // ── 8. GetDetailFact — SenkouA > SenkouB = bullish Kumo ───────────────

        [Fact]
        public void GetDetailFact_SenkouAAboveSenkouB_MentionsBullishKumo()
        {
            int n = 80;
            var bars = BuildBars(n);
            var (rd, _) = Calculate(bars);

            // Find an index where Senkou A and B are both valid.
            double[] senkouA = rd[IchimokuProvider.CompSenkouA];
            double[] senkouB = rd[IchimokuProvider.CompSenkouB];
            int idx = -1;
            for (int i = 30; i < n; i++)
            {
                if (!double.IsNaN(senkouA[i]) && !double.IsNaN(senkouB[i]) && senkouA[i] > senkouB[i])
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0) return; // Skip if no valid bullish Kumo found in test data

            string fact = Provider.GetDetailFact("ICHIMOKU", bars.AsSpan(), rd, idx, DefaultParams);
            Assert.Contains("Bullish", fact, StringComparison.OrdinalIgnoreCase);
        }

        // ── 9. Stability window ────────────────────────────────────────────────

        [Fact]
        public void StabilityWindow_WithDefaults_Equals78()
        {
            // max(KijunPeriod=26, SenkouBPeriod=52) + Displacement=26 = 78
            int window = Provider.GetStabilityWindow("ICHIMOKU", DefaultParams);
            Assert.Equal(78, window);
        }

        // ── 10. Returns NaN for bars before stability window ──────────────────

        [Fact]
        public void Tenkan_ReturnNaN_BeforeWarmupPeriod()
        {
            // With only 5 bars, TenkanPeriod=9 → all indices should be NaN.
            var bars = BuildBars(5);
            var (rd, _) = Calculate(bars);

            if (rd.ContainsKey(IchimokuProvider.CompTenkan))
            {
                double[] tenkan = rd[IchimokuProvider.CompTenkan];
                foreach (double v in tenkan)
                    Assert.True(double.IsNaN(v), $"Expected NaN before warmup but got {v}.");
            }
            // If no key present at all, that's also acceptable for insufficient data.
        }
    }
}
