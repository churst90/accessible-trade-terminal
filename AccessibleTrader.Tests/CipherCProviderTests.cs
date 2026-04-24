using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for <see cref="CipherCProvider"/> — metadata, component audio config,
    /// signal classification logic (tier assignment, mutual exclusion, failure signals),
    /// GetDetailFact speech, GetComponentSpeech contextual output, and stability window.
    /// </summary>
    public class CipherCProviderTests
    {
        private static readonly CipherCProvider Provider = new();

        private static readonly Dictionary<string, object> DefaultParams = new()
        {
            { "CyclePeriod",  10 },
            { "SmoothPeriod", 3  },
            { "SignalPeriod", 3  },
            { "OBLevel",      75.0 },
        };

        // ── Build helpers ─────────────────────────────────────────────────────

        private static Ohlcv[] BuildBars(int count, Func<int, double> closeFunc)
        {
            var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var bars = new Ohlcv[count];
            for (int i = 0; i < count; i++)
            {
                double c = closeFunc(i);
                bars[i] = new Ohlcv(start.AddDays(i), c - 0.5, c + 1.0, c - 1.0, c, 1000);
            }
            return bars;
        }

        /// <summary>Flat price — produces minimal oscillator movement.</summary>
        private static Ohlcv[] BuildFlatBars(int count) =>
            BuildBars(count, _ => 100.0);

        /// <summary>
        /// Strong uptrend then strong downtrend.
        /// risingCount bars: close = 100, 101, ..., 99+risingCount
        /// fallingCount bars: close falls symmetrically back from the peak.
        /// </summary>
        private static Ohlcv[] BuildRiseThenFall(int risingCount, int fallingCount)
            => BuildBars(risingCount + fallingCount, i =>
                i < risingCount ? 100.0 + i : 100.0 + risingCount - (i - risingCount));

        /// <summary>Strong downtrend then strong uptrend.</summary>
        private static Ohlcv[] BuildFallThenRise(int fallingCount, int risingCount)
            => BuildBars(fallingCount + risingCount, i =>
                i < fallingCount ? 200.0 - i : 200.0 - fallingCount + (i - fallingCount));

        private static (Dictionary<string, double[]> Results, IndicatorResultBuffer Buffer)
            Calculate(Ohlcv[] bars, Dictionary<string, object>? parms = null)
        {
            int n   = bars.Length;
            var rd  = new Dictionary<string, double[]>();
            var buf = new IndicatorResultBuffer(rd, n);
            Provider.Calculate("CIPHER_C", bars.AsSpan(), parms ?? DefaultParams, buf);
            return (rd, buf);
        }

        // ── 1. Component count ────────────────────────────────────────────────

        [Fact]
        public void GetMetadata_Returns10Components()
        {
            var meta = Provider.GetIndicators()[0];
            Assert.Equal(10, meta.Components.Count);
        }

        // ── 2. Cloud fill ─────────────────────────────────────────────────────

        [Fact]
        public void GetMetadata_Has1CloudFill_CycleFill()
        {
            var meta = Provider.GetIndicators()[0];
            Assert.Single(meta.DefaultCloudFills);
            Assert.Equal("Cycle Fill", meta.DefaultCloudFills[0].DisplayName);
        }

        [Fact]
        public void CycleFill_UpperComponent_IsCycleSine()
        {
            var fill = Provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.Equal(CipherCProvider.CompCycleSine, fill.UpperComponentName);
        }

        [Fact]
        public void CycleFill_LowerComponent_IsLeadSine()
        {
            var fill = Provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.Equal(CipherCProvider.CompLeadSine, fill.LowerComponentName);
        }

        [Fact]
        public void CycleFill_IsVisualOnly()
        {
            // Policy (see CloudSonificationConfig XML docs): oscillator-to-oscillator
            // cloud fills stay visual-only because both boundaries (CycleSine and
            // LeadSine) already sonify independently — a third cloud voice between
            // them would duplicate information the user already hears.
            var fill = Provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.Null(fill.Sonification);
        }

        // ── 3. All 10 components have non-null DefaultColorHex ────────────────

        [Fact]
        public void AllComponents_HaveNonNullDefaultColorHex()
        {
            foreach (var comp in Provider.GetIndicators()[0].Components)
                Assert.False(string.IsNullOrEmpty(comp.DefaultColorHex),
                    $"Component '{comp.Name}' must have a non-null DefaultColorHex.");
        }

        // ── 4. Component audio config ─────────────────────────────────────────

        private static IndicatorComponentMetadata GetComp(string name) =>
            Provider.GetIndicators()[0].Components.Single(c => c.Name == name);

        [Fact]
        public void TopTriple_HasSoundPatch_DualToneBell() =>
            Assert.Equal("dual_tone_bell", GetComp(CipherCProvider.CompTopTriple).DefaultSoundPatchId);

        [Fact]
        public void TopTriple_HasBaseFrequency_380() =>
            Assert.Equal(380.0, GetComp(CipherCProvider.CompTopTriple).DefaultBaseFrequency);

        [Fact]
        public void TopTriple_HasDecayMs_500() =>
            Assert.Equal(500, GetComp(CipherCProvider.CompTopTriple).DefaultDecayMs);

        [Fact]
        public void TopTriple_HasPlaybackLayer_Foreground() =>
            Assert.Equal(PlaybackLayer.Foreground, GetComp(CipherCProvider.CompTopTriple).DefaultPlaybackLayer);

        [Fact]
        public void TopSingle_HasPlaybackLayer_Background() =>
            Assert.Equal(PlaybackLayer.Background, GetComp(CipherCProvider.CompTopSingle).DefaultPlaybackLayer);

        [Fact]
        public void BottomDouble_HasSoundPatch_SineBell() =>
            Assert.Equal("sine_bell", GetComp(CipherCProvider.CompBottomDouble).DefaultSoundPatchId);

        [Fact]
        public void BottomTriple_HasBaseFrequency_320() =>
            Assert.Equal(320.0, GetComp(CipherCProvider.CompBottomTriple).DefaultBaseFrequency);

        [Fact]
        public void BottomTriple_HasPlaybackLayer_Foreground() =>
            Assert.Equal(PlaybackLayer.Foreground, GetComp(CipherCProvider.CompBottomTriple).DefaultPlaybackLayer);

        [Fact]
        public void BottomSingle_HasPlaybackLayer_Background() =>
            Assert.Equal(PlaybackLayer.Background, GetComp(CipherCProvider.CompBottomSingle).DefaultPlaybackLayer);

        [Fact]
        public void ShallowPeak_HasBaseFrequency_260() =>
            Assert.Equal(260.0, GetComp(CipherCProvider.CompShallowPeak).DefaultBaseFrequency);

        [Fact]
        public void ShallowTrough_HasBaseFrequency_480() =>
            Assert.Equal(480.0, GetComp(CipherCProvider.CompShallowTrough).DefaultBaseFrequency);

        [Fact]
        public void ShallowPeak_HasPlaybackLayer_Midground() =>
            Assert.Equal(PlaybackLayer.Midground, GetComp(CipherCProvider.CompShallowPeak).DefaultPlaybackLayer);

        // ── 5. Parameters ─────────────────────────────────────────────────────

        [Fact]
        public void GetMetadata_Has4Parameters()
        {
            var meta = Provider.GetIndicators()[0];
            Assert.Equal(4, meta.Parameters.Count);
        }

        [Fact]
        public void CyclePeriod_DefaultValue_Is10()
        {
            var p = Provider.GetIndicators()[0].Parameters.Single(x => x.Name == "CyclePeriod");
            Assert.Equal(10.0, p.DefaultValue);
        }

        [Fact]
        public void OBLevel_DefaultValue_Is75()
        {
            var p = Provider.GetIndicators()[0].Parameters.Single(x => x.Name == "OBLevel");
            Assert.Equal(75.0, p.DefaultValue);
        }

        // ── 6. Default levels ─────────────────────────────────────────────────

        [Fact]
        public void GetDefaultLevels_Returns5Levels()
        {
            var levels = Provider.GetDefaultLevels("CIPHER_C");
            Assert.Equal(5, levels.Count);
        }

        [Fact]
        public void GetDefaultLevels_ContainsExtremeOB_At90()
        {
            var levels = Provider.GetDefaultLevels("CIPHER_C");
            Assert.Contains(levels, l => l.Name == "Extreme OB" && l.Value == 90.0);
        }

        [Fact]
        public void GetDefaultLevels_ContainsOverbought_At75()
        {
            var levels = Provider.GetDefaultLevels("CIPHER_C");
            Assert.Contains(levels, l => l.Name == "Overbought" && l.Value == 75.0);
        }

        [Fact]
        public void GetDefaultLevels_ContainsZero()
        {
            var levels = Provider.GetDefaultLevels("CIPHER_C");
            Assert.Contains(levels, l => l.Name == "Zero" && l.Value == 0.0);
        }

        [Fact]
        public void GetDefaultLevels_ContainsOversold_AtNeg75()
        {
            var levels = Provider.GetDefaultLevels("CIPHER_C");
            Assert.Contains(levels, l => l.Name == "Oversold" && l.Value == -75.0);
        }

        [Fact]
        public void GetDefaultLevels_ContainsExtremeOS_AtNeg90()
        {
            var levels = Provider.GetDefaultLevels("CIPHER_C");
            Assert.Contains(levels, l => l.Name == "Extreme OS" && l.Value == -90.0);
        }

        [Fact]
        public void GetDefaultLevels_CaseInsensitive()
        {
            Assert.Equal(5, Provider.GetDefaultLevels("cipher_c").Count);
            Assert.Equal(5, Provider.GetDefaultLevels("Cipher_C").Count);
        }

        [Fact]
        public void GetDefaultLevels_UnknownCode_ReturnsEmpty()
        {
            Assert.Empty(Provider.GetDefaultLevels("OTHER_CODE"));
        }

        // ── 7. Stability window ───────────────────────────────────────────────

        [Fact]
        public void StabilityWindow_WithDefaults_Equals66()
        {
            // CyclePeriod*4 + SignalPeriod*2 + 20 = 40 + 6 + 20 = 66
            // (Ehlers bandpass needs more warmup than the old EMA/stochastic path)
            int window = Provider.GetStabilityWindow("CIPHER_C", DefaultParams);
            Assert.Equal(66, window);
        }

        // ── 7b. Warmup suppression — no signals fire before stabilityStart ──────

        [Fact]
        public void Signals_NoneFireBeforeStabilityWindow()
        {
            // stabilityStart = CyclePeriod*4 + SignalPeriod*2 + 20 = 40+6+20 = 66
            // Use a deeply oscillating price series that would easily trigger signals
            // if warmup suppression were absent.
            int stabilityStart = 66;
            var bars = BuildBars(200, i => 100.0 + 45.0 * Math.Sin(i * 0.3));
            var (rd, _) = Calculate(bars);

            var signalKeys = new[]
            {
                CipherCProvider.CompTopTriple,    CipherCProvider.CompTopDouble,
                CipherCProvider.CompTopSingle,    CipherCProvider.CompBottomTriple,
                CipherCProvider.CompBottomDouble, CipherCProvider.CompBottomSingle,
                CipherCProvider.CompShallowPeak,  CipherCProvider.CompShallowTrough,
            };

            for (int i = 0; i < stabilityStart; i++)
            {
                foreach (var key in signalKeys)
                {
                    if (!rd.TryGetValue(key, out var arr) || arr == null || i >= arr.Length) continue;
                    Assert.True(double.IsNaN(arr[i]),
                        $"Signal '{key}' fired at bar {i} (before stabilityStart={stabilityStart}) — warmup suppression failed.");
                }
            }
        }

        // ── 8. Calculate — insufficient data ─────────────────────────────────

        [Fact]
        public void Calculate_WithFewerThan20Bars_DoesNotThrow()
        {
            var bars = BuildFlatBars(10);
            var ex = Record.Exception(() => Calculate(bars));
            Assert.Null(ex);
        }

        [Fact]
        public void Calculate_WithFewerThan20Bars_ProducesNoOutput()
        {
            var bars = BuildFlatBars(10);
            var (rd, _) = Calculate(bars);
            // Buffer should be empty or contain only NaN arrays — no valid signals
            foreach (var kvp in rd)
                foreach (double v in kvp.Value)
                    Assert.True(double.IsNaN(v) || v == 0,
                        $"Expected NaN/0 for {kvp.Key} with insufficient bars, got {v}.");
        }

        // ── 9. Calculate — CycleSine and LeadSine are computed ────────────────

        [Fact]
        public void Calculate_WithSufficientBars_CycleSineHasNonNanValues()
        {
            // Use 100 bars of a gentle oscillation so the stochastic doesn't pin
            // at extremes and EMA warmup can complete.
            var bars = BuildBars(100, i => 100.0 + 10.0 * Math.Sin(i * 0.2));
            var (rd, _) = Calculate(bars);

            Assert.True(rd.ContainsKey(CipherCProvider.CompCycleSine),
                "CycleSine component array must be present in results.");
            double[] sine = rd[CipherCProvider.CompCycleSine];
            Assert.True(sine.Any(v => !double.IsNaN(v)),
                "CycleSine must have at least one non-NaN value with sufficient bars.");
        }

        [Fact]
        public void Calculate_WithSufficientBars_LeadSineHasNonNanValues()
        {
            var bars = BuildBars(100, i => 100.0 + 10.0 * Math.Sin(i * 0.2));
            var (rd, _) = Calculate(bars);

            Assert.True(rd.ContainsKey(CipherCProvider.CompLeadSine),
                "LeadSine component array must be present in results.");
            double[] lead = rd[CipherCProvider.CompLeadSine];
            Assert.True(lead.Any(v => !double.IsNaN(v)),
                "LeadSine must have at least one non-NaN value with sufficient bars.");
        }

        // ── 10. CycleSine values are clamped to [-100, 100] ──────────────────

        [Fact]
        public void CycleSine_AllValues_AreWithinClampRange()
        {
            var bars = BuildRiseThenFall(80, 80);
            var (rd, _) = Calculate(bars);

            if (!rd.ContainsKey(CipherCProvider.CompCycleSine)) return;
            foreach (double v in rd[CipherCProvider.CompCycleSine])
            {
                if (double.IsNaN(v)) continue;
                Assert.InRange(v, -100.0, 100.0);
            }
        }

        // ── 11. Signal mutual exclusion ───────────────────────────────────────
        // On any single bar, at most one signal component should be non-NaN.

        [Fact]
        public void Signals_AreExclusivePerBar()
        {
            // Use a sinusoidal price series to generate multiple crossovers.
            var bars = BuildBars(200, i => 100.0 + 40.0 * Math.Sin(i * 0.15));
            var (rd, _) = Calculate(bars);

            var signalKeys = new[]
            {
                CipherCProvider.CompTopTriple,
                CipherCProvider.CompTopDouble,
                CipherCProvider.CompTopSingle,
                CipherCProvider.CompBottomTriple,
                CipherCProvider.CompBottomDouble,
                CipherCProvider.CompBottomSingle,
                CipherCProvider.CompShallowPeak,
                CipherCProvider.CompShallowTrough,
            };

            int n = bars.Length;
            for (int i = 0; i < n; i++)
            {
                int signals = signalKeys.Count(k =>
                    rd.TryGetValue(k, out var arr) && arr != null && i < arr.Length && !double.IsNaN(arr[i]));
                Assert.True(signals <= 1,
                    $"Bar {i} has {signals} simultaneous signal components — expected at most 1.");
            }
        }

        // ── 12. Signal values are within oscillator range ─────────────────────

        [Fact]
        public void SignalValues_WhenPresent_AreWithinOscillatorRange()
        {
            var bars = BuildBars(200, i => 100.0 + 40.0 * Math.Sin(i * 0.15));
            var (rd, _) = Calculate(bars);

            var signalKeys = new[]
            {
                CipherCProvider.CompTopTriple,    CipherCProvider.CompTopDouble,
                CipherCProvider.CompTopSingle,    CipherCProvider.CompBottomTriple,
                CipherCProvider.CompBottomDouble, CipherCProvider.CompBottomSingle,
                CipherCProvider.CompShallowPeak,  CipherCProvider.CompShallowTrough,
            };

            foreach (var key in signalKeys)
            {
                if (!rd.TryGetValue(key, out var arr)) continue;
                foreach (double v in arr)
                {
                    if (double.IsNaN(v)) continue;
                    Assert.InRange(v, -100.0, 100.0);
                }
            }
        }

        // ── 13. Top signal on sustained uptrend reversal ──────────────────────
        // A long monotonic rise followed by a fall should produce at least one top signal
        // (Triple/Double/Single) during the reversal phase.

        [Fact]
        public void TopSignal_FiredOnSustainedUptrendReversal()
        {
            // 80 bars rising (+1/bar) then 80 bars falling — 160 bars total.
            // OB zone is reached during the rise (stochastic pins at 1.0 → Fisher clamped to +100).
            // Hull RSI is near 100 (all gains) → hullBearish fires. Expect topTriple.
            var bars = BuildRiseThenFall(80, 80);
            var (rd, _) = Calculate(bars);

            var topKeys = new[]
            {
                CipherCProvider.CompTopTriple,
                CipherCProvider.CompTopDouble,
                CipherCProvider.CompTopSingle,
            };

            bool anyTopSignal = topKeys.Any(k =>
                rd.TryGetValue(k, out var arr) && arr != null &&
                arr.Skip(60).Take(60).Any(v => !double.IsNaN(v)));  // signal in reversal window

            Assert.True(anyTopSignal,
                "Expected at least one top-tier signal (Triple/Double/Single) after sustained uptrend reversal.");
        }

        // ── 14. Bottom signal on sustained downtrend reversal ─────────────────

        [Fact]
        public void BottomSignal_FiredOnSustainedDowntrendReversal()
        {
            // 80 bars falling then 80 bars rising.
            // OS zone reached during fall → Hull RSI near 0 → hullBullish. Expect bottomTriple.
            var bars = BuildFallThenRise(80, 80);
            var (rd, _) = Calculate(bars);

            var bottomKeys = new[]
            {
                CipherCProvider.CompBottomTriple,
                CipherCProvider.CompBottomDouble,
                CipherCProvider.CompBottomSingle,
            };

            bool anyBottomSignal = bottomKeys.Any(k =>
                rd.TryGetValue(k, out var arr) && arr != null &&
                arr.Skip(60).Take(60).Any(v => !double.IsNaN(v)));

            Assert.True(anyBottomSignal,
                "Expected at least one bottom-tier signal (Triple/Double/Single) after sustained downtrend reversal.");
        }

        // ── 15. UpdateLast — same result as full Calculate at last bar ─────────

        [Fact]
        public void UpdateLast_MatchesCalculateAtLastBar()
        {
            var bars = BuildBars(80, i => 100.0 + 10.0 * Math.Sin(i * 0.2));

            // Full calculate
            var (rdFull, _) = Calculate(bars);

            // UpdateLast
            int n   = bars.Length;
            var rd2 = new Dictionary<string, double[]>();
            var buf2 = new IndicatorResultBuffer(rd2, n);
            Provider.UpdateLast("CIPHER_C", bars.AsSpan(), DefaultParams, buf2);

            // At the last bar, values must agree for CycleSine and LeadSine.
            foreach (var key in new[] { CipherCProvider.CompCycleSine, CipherCProvider.CompLeadSine })
            {
                if (!rdFull.TryGetValue(key, out var fullArr) || !rd2.TryGetValue(key, out var lastArr))
                    continue;
                double vFull = fullArr[^1];
                double vLast = lastArr[^1];
                if (double.IsNaN(vFull) && double.IsNaN(vLast)) continue;
                Assert.Equal(vFull, vLast, precision: 8);
            }
        }

        // ── 16. GetDetailFact ─────────────────────────────────────────────────

        [Fact]
        public void GetDetailFact_WithSufficientData_ReturnsNonEmptyString()
        {
            var bars = BuildBars(100, i => 100.0 + 10.0 * Math.Sin(i * 0.2));
            var (rd, _) = Calculate(bars);
            string fact = Provider.GetDetailFact("CIPHER_C", bars.AsSpan(), rd, 90, DefaultParams);
            Assert.False(string.IsNullOrWhiteSpace(fact));
        }

        [Fact]
        public void GetDetailFact_WhenSineAboveOBLevel_MentionsOverbought()
        {
            // Build a bar set where CycleSine is firmly in the OB zone.
            // 80 rising bars produce Fisher clamped to +100 (above OBLevel=75).
            var bars = BuildRiseThenFall(80, 20);
            var (rd, _) = Calculate(bars);

            // Find an index where CycleSine > 75
            double[]? sineArr = rd.ContainsKey(CipherCProvider.CompCycleSine)
                ? rd[CipherCProvider.CompCycleSine] : null;
            if (sineArr == null) return;

            int idx = -1;
            for (int i = 0; i < sineArr.Length; i++)
                if (!double.IsNaN(sineArr[i]) && sineArr[i] > 75.0) { idx = i; break; }

            if (idx < 0) return; // No OB bar found — skip (won't fail, data-conditional)

            string fact = Provider.GetDetailFact("CIPHER_C", bars.AsSpan(), rd, idx, DefaultParams);
            Assert.Contains("overbought", fact, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetDetailFact_WhenSineBelowNegOBLevel_MentionsOversold()
        {
            var bars = BuildFallThenRise(80, 20);
            var (rd, _) = Calculate(bars);

            double[]? sineArr = rd.ContainsKey(CipherCProvider.CompCycleSine)
                ? rd[CipherCProvider.CompCycleSine] : null;
            if (sineArr == null) return;

            int idx = -1;
            for (int i = 0; i < sineArr.Length; i++)
                if (!double.IsNaN(sineArr[i]) && sineArr[i] < -75.0) { idx = i; break; }

            if (idx < 0) return;

            string fact = Provider.GetDetailFact("CIPHER_C", bars.AsSpan(), rd, idx, DefaultParams);
            Assert.Contains("oversold", fact, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetDetailFact_WhenSineAboveLead_MentionsRisingPhase()
        {
            var bars = BuildBars(100, i => 100.0 + 10.0 * Math.Sin(i * 0.2));
            var (rd, _) = Calculate(bars);

            double[]? sine = rd.ContainsKey(CipherCProvider.CompCycleSine) ? rd[CipherCProvider.CompCycleSine] : null;
            double[]? lead = rd.ContainsKey(CipherCProvider.CompLeadSine)  ? rd[CipherCProvider.CompLeadSine]  : null;
            if (sine == null || lead == null) return;

            int idx = -1;
            for (int i = 0; i < Math.Min(sine.Length, lead.Length); i++)
                if (!double.IsNaN(sine[i]) && !double.IsNaN(lead[i]) && sine[i] > lead[i])
                { idx = i; break; }

            if (idx < 0) return;

            string fact = Provider.GetDetailFact("CIPHER_C", bars.AsSpan(), rd, idx, DefaultParams);
            Assert.Contains("Rising phase", fact, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetDetailFact_WhenSineBelowLead_MentionsFallingPhase()
        {
            var bars = BuildBars(100, i => 100.0 + 10.0 * Math.Sin(i * 0.2));
            var (rd, _) = Calculate(bars);

            double[]? sine = rd.ContainsKey(CipherCProvider.CompCycleSine) ? rd[CipherCProvider.CompCycleSine] : null;
            double[]? lead = rd.ContainsKey(CipherCProvider.CompLeadSine)  ? rd[CipherCProvider.CompLeadSine]  : null;
            if (sine == null || lead == null) return;

            int idx = -1;
            for (int i = 0; i < Math.Min(sine.Length, lead.Length); i++)
                if (!double.IsNaN(sine[i]) && !double.IsNaN(lead[i]) && sine[i] < lead[i])
                { idx = i; break; }

            if (idx < 0) return;

            string fact = Provider.GetDetailFact("CIPHER_C", bars.AsSpan(), rd, idx, DefaultParams);
            Assert.Contains("Falling phase", fact, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetDetailFact_WithNegativeIndex_ReturnsEmpty()
        {
            var bars = BuildFlatBars(60);
            var (rd, _) = Calculate(bars);
            string fact = Provider.GetDetailFact("CIPHER_C", bars.AsSpan(), rd, -1, DefaultParams);
            Assert.Equal(string.Empty, fact);
        }

        [Fact]
        public void GetDetailFact_WrongCode_ReturnsEmpty()
        {
            var bars = BuildFlatBars(60);
            var (rd, _) = Calculate(bars);
            string fact = Provider.GetDetailFact("RSI", bars.AsSpan(), rd, 50, DefaultParams);
            Assert.Equal(string.Empty, fact);
        }

        // ── 17. GetComponentSpeech ────────────────────────────────────────────

        [Fact]
        public void GetComponentSpeech_CycleSine_WithOBValue_MentionsOverbought()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                [CipherCProvider.CompCycleSine] = new[] { 82.0 },
                [CipherCProvider.CompLeadSine]  = new[] { 78.0 },
            };
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech(
                CipherCProvider.CompCycleSine, 82.0, bar, data, 0);

            Assert.NotNull(speech);
            Assert.Contains("overbought", speech!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetComponentSpeech_CycleSine_WithExtremeOBValue_MentionsExtremeOverbought()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                [CipherCProvider.CompCycleSine] = new[] { 95.0 },
                [CipherCProvider.CompLeadSine]  = new[] { 92.0 },
            };
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech(
                CipherCProvider.CompCycleSine, 95.0, bar, data, 0);

            Assert.NotNull(speech);
            Assert.Contains("extreme overbought", speech!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetComponentSpeech_CycleSine_WithNeutralValue_MentionsNeutral()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                [CipherCProvider.CompCycleSine] = new[] { 10.0 },
                [CipherCProvider.CompLeadSine]  = new[] { 8.0  },
            };
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech(
                CipherCProvider.CompCycleSine, 10.0, bar, data, 0);

            Assert.NotNull(speech);
            Assert.Contains("neutral", speech!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetComponentSpeech_CycleSine_SineAboveLead_MentionsRisingPhase()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                [CipherCProvider.CompCycleSine] = new[] { 30.0 },
                [CipherCProvider.CompLeadSine]  = new[] { 20.0 },
            };
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech(
                CipherCProvider.CompCycleSine, 30.0, bar, data, 0);

            Assert.NotNull(speech);
            Assert.Contains("Rising phase", speech!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetComponentSpeech_CycleSine_SineBelowLead_MentionsFallingPhase()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                [CipherCProvider.CompCycleSine] = new[] { 20.0 },
                [CipherCProvider.CompLeadSine]  = new[] { 30.0 },
            };
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech(
                CipherCProvider.CompCycleSine, 20.0, bar, data, 0);

            Assert.NotNull(speech);
            Assert.Contains("Falling phase", speech!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetComponentSpeech_CycleSine_NaN_ReturnsNoData()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech(
                CipherCProvider.CompCycleSine, double.NaN, bar, data, 0);

            Assert.NotNull(speech);
            Assert.Contains("no data", speech!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetComponentSpeech_LeadSine_ContainsValue()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                [CipherCProvider.CompCycleSine] = new[] { 45.0 },
                [CipherCProvider.CompLeadSine]  = new[] { 38.0 },
            };
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech(
                CipherCProvider.CompLeadSine, 38.0, bar, data, 0);

            Assert.NotNull(speech);
            Assert.Contains("38", speech!); // value appears in speech
        }

        [Fact]
        public void GetComponentSpeech_LeadSine_SineAbove_MentionsRisingPhase()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                [CipherCProvider.CompCycleSine] = new[] { 50.0 },
                [CipherCProvider.CompLeadSine]  = new[] { 38.0 },
            };
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech(
                CipherCProvider.CompLeadSine, 38.0, bar, data, 0);

            Assert.NotNull(speech);
            Assert.Contains("rising phase", speech!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetComponentSpeech_DotComponent_ReturnsNull()
        {
            // Dot signals should return null so DefaultSignalSpeechTemplate handles them.
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            foreach (var dotName in new[]
            {
                CipherCProvider.CompTopTriple,    CipherCProvider.CompTopDouble,
                CipherCProvider.CompTopSingle,    CipherCProvider.CompBottomTriple,
                CipherCProvider.CompBottomDouble, CipherCProvider.CompBottomSingle,
                CipherCProvider.CompShallowPeak,  CipherCProvider.CompShallowTrough,
            })
            {
                string? speech = Provider.GetComponentSpeech(dotName, 42.0, bar, data, 0);
                Assert.Null(speech);
            }
        }

        [Fact]
        public void GetComponentSpeech_UnknownComponent_ReturnsNull()
        {
            var data = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            var bar = new Ohlcv(DateTime.UtcNow, 100, 101, 99, 100, 1000);

            string? speech = Provider.GetComponentSpeech("Unknown Component", 42.0, bar, data, 0);
            Assert.Null(speech);
        }
    }
}
