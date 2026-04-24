using System;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Ichimoku Kinko Hyo — a comprehensive trend-following overlay indicator.
    ///
    /// Five components:
    ///   Tenkan-sen  (Conversion Line) — (highest_high + lowest_low) / 2 over TenkanPeriod bars.
    ///                                   Fast trend line; signals momentum when it crosses Kijun.
    ///   Kijun-sen   (Base Line)       — (highest_high + lowest_low) / 2 over KijunPeriod bars.
    ///                                   Slower confirmation line; acts as dynamic support/resistance.
    ///   Senkou Span A (Leading Span A)— (Tenkan + Kijun) / 2, plotted Displacement bars AHEAD.
    ///                                   Upper or lower Kumo boundary depending on Senkou B value.
    ///   Senkou Span B (Leading Span B)— (highest_high + lowest_low) / 2 over SenkouBPeriod bars,
    ///                                   plotted Displacement bars AHEAD. Other Kumo boundary.
    ///   Chikou Span  (Lagging Span)   — Current close, plotted Displacement bars BEHIND.
    ///                                   Confirms trend when above/below historical price.
    ///
    /// One cloud fill (Kumo):
    ///   Bullish (green) when Senkou A > Senkou B; bearish (red) when Senkou B > Senkou A.
    ///   Semi-transparent fill between the two Senkou lines gives visual cloud depth.
    ///   Cloud sonification: 520/180 Hz — distinct from EMA Fill (440/220) and WT Fill (480/200).
    ///
    /// Displacement handling:
    ///   Senkou arrays are shifted forward by Displacement bars — index i holds the value
    ///   that belongs to bar i+Displacement (projected into future). Indices beyond the end
    ///   of the data are silently omitted (out-of-bounds write is skipped). Indices at the
    ///   start with no valid data remain NaN.
    ///   Chikou is shifted backward — index i holds the close of bar i+Displacement.
    ///   The first Displacement bars remain NaN (no historical data behind them).
    ///
    /// GetDetailFact (Ctrl+Shift+D / F4 context speech):
    ///   Reports TK cross status, price position vs Kijun, price position vs Kumo,
    ///   and Kumo polarity (bullish/bearish cloud).
    /// </summary>
    public class IchimokuProvider : IIndicatorProvider
    {
        public string Name => "Accessible.Ichimoku";

        public const string CompTenkan       = "Tenkan-sen";
        public const string CompKijun        = "Kijun-sen";
        public const string CompSenkouA      = "Senkou Span A";
        public const string CompSenkouB      = "Senkou Span B";
        public const string CompChikou       = "Chikou Span";
        public const string CompKumoPolarity = "Kumo Polarity";
        public const string CompTkBull       = "TK Bull";
        public const string CompTkBear       = "TK Bear";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "ICHIMOKU",
                Name        = "Ichimoku Kinko Hyo",
                Category    = "Overlays",
                DefaultPane = "Main",
                Description =
                    "Ichimoku Kinko Hyo — five-component trend and momentum overlay. " +
                    "Tenkan-sen (conversion line, pink) and Kijun-sen (base line, blue) signal momentum crosses. " +
                    "Senkou Span A and B form the Kumo cloud projected 26 bars ahead — " +
                    "green (bullish) when Span A is above Span B, red (bearish) otherwise. " +
                    "Chikou Span (purple) is the current close plotted 26 bars in the past; " +
                    "trend is confirmed when Chikou is above/below historical price.",

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "TenkanPeriod",  DisplayName = "Tenkan Period",       DataType = typeof(int), DefaultValue = 9.0,
                            Description = "Conversion line period: (highest high + lowest low) / 2 over this many bars." },
                    new() { Name = "KijunPeriod",   DisplayName = "Kijun Period",        DataType = typeof(int), DefaultValue = 26.0,
                            Description = "Base line period: (highest high + lowest low) / 2 over this many bars." },
                    new() { Name = "SenkouBPeriod", DisplayName = "Senkou B Period",     DataType = typeof(int), DefaultValue = 52.0,
                            Description = "Leading Span B period: (highest high + lowest low) / 2 over this many bars, displaced forward." },
                    new() { Name = "Displacement",  DisplayName = "Displacement (bars)", DataType = typeof(int), DefaultValue = 26.0,
                            Description = "How many bars ahead Senkou spans are plotted and how many bars behind Chikou is plotted." },
                },

                Components = new List<IndicatorComponentMetadata>
                {
                    // ── Tenkan-sen (Conversion Line) ─────────────────────────────────────────
                    // Fast trend reference — crosses above Kijun = bullish momentum.
                    // Triangle wave (mid-register), sustain envelope — conveys active momentum character.
                    new() { Name = CompTenkan,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#E91E63", DefaultThickness = 1.5f,
                            DefaultWaveform = "triangle",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            IsVisible = true },

                    // ── Kijun-sen (Base Line) ────────────────────────────────────────────────
                    // Slower confirmation — dynamic support/resistance level.
                    // Sine wave, sustain — smooth authority character (heavier than Tenkan).
                    new() { Name = CompKijun,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#2196F3", DefaultThickness = 2.0f,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            IsVisible = true },

                    // ── Senkou Span A (Leading Span A) ───────────────────────────────────────
                    // Upper Kumo boundary when cloud is bullish.
                    // Background layer — visual cloud edge; sine sustain, quiet presence.
                    new() { Name = CompSenkouA,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#4CAF50", DefaultThickness = 1.0f,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            IsVisible = true },

                    // ── Senkou Span B (Leading Span B) ───────────────────────────────────────
                    // Lower Kumo boundary when cloud is bullish.
                    // Background layer — visual cloud edge; sine sustain.
                    new() { Name = CompSenkouB,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#F44336", DefaultThickness = 1.0f,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            IsVisible = true },

                    // ── Chikou Span (Lagging Span) ───────────────────────────────────────────
                    // Close plotted Displacement bars in the past — confirms trend direction.
                    // Background layer — historic confirmation context; purple.
                    new() { Name = CompChikou,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#9C27B0", DefaultThickness = 1.5f,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            IsVisible = true },

                    // ── Kumo Polarity ────────────────────────────────────────────────────────
                    // Hidden ternary line: +1 when Senkou A > B (bullish cloud), -1 when B > A,
                    // 0 when equal. Lets strategies gate on cloud direction with a single leaf
                    // instead of an arithmetic comparison on the two Senkou spans.
                    new() { Name = CompKumoPolarity,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#9E9E9E", DefaultThickness = 1.0f,
                            IsVisible = false,
                            DefaultReferenceLevel = 0.0,
                            SpeechTemplate = "Kumo polarity {value:F0}." },

                    // ── TK Cross markers ─────────────────────────────────────────────────────
                    // Fire on confirmed Tenkan/Kijun crossovers with 2-bar sustained hold.
                    // A bare single-bar cross on Tenkan/Kijun flips 3× in 5 bars on choppy
                    // assets; the 2-bar confirmation eliminates most of that noise while
                    // lagging only one bar on genuine reversals.
                    new() { Name = CompTkBull,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E676", DefaultThickness = 5.0f,
                            IsVisible = true,
                            DefaultEnvelopeType = "Ping",
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 180,
                            DefaultBaseFrequency = 580.0,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Tenkan Kijun bull cross, confirmed" },
                    new() { Name = CompTkBear,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF1744", DefaultThickness = 5.0f,
                            IsVisible = true,
                            DefaultEnvelopeType = "Ping",
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 180,
                            DefaultBaseFrequency = 260.0,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Tenkan Kijun bear cross, confirmed" },
                },

                // ── Kumo cloud fill (Senkou A vs Senkou B) ───────────────────────────────────
                // Bullish (green) when Senkou A > Senkou B, bearish (red) when Senkou B > Senkou A.
                // Semi-transparent (60 in hex alpha) so price candles remain visible through the cloud.
                // Sonification: 520 Hz bullish / 180 Hz bearish — distinct registers from EMA Fill (440/220)
                // and WT Fill (480/200).
                DefaultCloudFills = new List<CloudFillConfig>
                {
                    new()
                    {
                        UpperComponentName = CompSenkouA,
                        LowerComponentName = CompSenkouB,
                        BullishColorHex    = "#4CAF5060",
                        BearishColorHex    = "#F4433660",
                        DisplayName        = "Kumo",
                        IsVisible          = true,
                        Sonification       = new CloudSonificationConfig(
                            BullishFrequency: 520f,
                            BearishFrequency: 180f,
                            SoundPatchId:     "sine_bell",
                            DecayMs:          220,
                            MaxVolume:        0.80f),
                    },
                },
            }
        };

        // ── Calculate (full recalc) ───────────────────────────────────────────────────────

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("ICHIMOKU", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 2) return;

            int tenkanPeriod  = GetInt(parameters, "TenkanPeriod",  9);
            int kijunPeriod   = GetInt(parameters, "KijunPeriod",   26);
            int senkouBPeriod = GetInt(parameters, "SenkouBPeriod", 52);
            int displacement  = GetInt(parameters, "Displacement",  26);

            // Output arrays — all NaN initially.
            var tenkan  = IndicatorMath.NanArray(n);
            var kijun   = IndicatorMath.NanArray(n);
            var senkouA = IndicatorMath.NanArray(n);
            var senkouB = IndicatorMath.NanArray(n);
            var chikou  = IndicatorMath.NanArray(n);

            for (int i = 0; i < n; i++)
            {
                // ── Tenkan-sen ────────────────────────────────────────────────────────────
                if (i >= tenkanPeriod - 1)
                    tenkan[i] = IndicatorMath.Midpoint(data, i, tenkanPeriod);

                // ── Kijun-sen ─────────────────────────────────────────────────────────────
                if (i >= kijunPeriod - 1)
                    kijun[i] = IndicatorMath.Midpoint(data, i, kijunPeriod);

                // ── Senkou Span A → plotted Displacement bars ahead ───────────────────────
                // Value computed at bar i is placed at index i + displacement.
                if (i >= kijunPeriod - 1 && !double.IsNaN(tenkan[i]) && !double.IsNaN(kijun[i]))
                {
                    int fwd = i + displacement;
                    if (fwd < n)
                        senkouA[fwd] = (tenkan[i] + kijun[i]) / 2.0;
                }

                // ── Senkou Span B → plotted Displacement bars ahead ───────────────────────
                if (i >= senkouBPeriod - 1)
                {
                    int fwd = i + displacement;
                    if (fwd < n)
                        senkouB[fwd] = IndicatorMath.Midpoint(data, i, senkouBPeriod);
                }

                // ── Chikou Span → plotted Displacement bars behind ────────────────────────
                // Close at bar i is placed at index i - displacement.
                int bwd = i - displacement;
                if (bwd >= 0)
                    chikou[bwd] = data[i].Close;
            }

            // ── Kumo polarity + TK cross markers (2-bar confirmed) ───────────────────
            var kumoPol = IndicatorMath.NanArray(n);
            var tkBull  = IndicatorMath.NanArray(n);
            var tkBear  = IndicatorMath.NanArray(n);
            for (int i = 0; i < n; i++)
            {
                if (!double.IsNaN(senkouA[i]) && !double.IsNaN(senkouB[i]))
                    kumoPol[i] = senkouA[i] > senkouB[i] ? 1.0
                               : senkouA[i] < senkouB[i] ? -1.0
                               : 0.0;

                if (i < 2) continue;
                if (double.IsNaN(tenkan[i]) || double.IsNaN(kijun[i]) ||
                    double.IsNaN(tenkan[i - 1]) || double.IsNaN(kijun[i - 1]) ||
                    double.IsNaN(tenkan[i - 2]) || double.IsNaN(kijun[i - 2])) continue;

                // Cross on bar i-1, and bar i confirms direction.
                bool crossUpPrior = tenkan[i - 2] < kijun[i - 2] && tenkan[i - 1] >= kijun[i - 1];
                bool stillUp      = tenkan[i] >= kijun[i];
                bool crossDnPrior = tenkan[i - 2] > kijun[i - 2] && tenkan[i - 1] <= kijun[i - 1];
                bool stillDn      = tenkan[i] <= kijun[i];

                if (crossUpPrior && stillUp) tkBull[i] = kijun[i];
                if (crossDnPrior && stillDn) tkBear[i] = kijun[i];
            }

            WriteToBuffer(buffer, CompTenkan,       tenkan,  n);
            WriteToBuffer(buffer, CompKijun,        kijun,   n);
            WriteToBuffer(buffer, CompSenkouA,      senkouA, n);
            WriteToBuffer(buffer, CompSenkouB,      senkouB, n);
            WriteToBuffer(buffer, CompChikou,       chikou,  n);
            WriteToBuffer(buffer, CompKumoPolarity, kumoPol, n);
            WriteToBuffer(buffer, CompTkBull,       tkBull,  n);
            WriteToBuffer(buffer, CompTkBear,       tkBear,  n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int kijun       = GetInt(parameters, "KijunPeriod",   26);
            int senkouB     = GetInt(parameters, "SenkouBPeriod", 52);
            int displacement = GetInt(parameters, "Displacement",  26);
            return Math.Max(kijun, senkouB) + displacement;
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value))
                return $"{componentName}: no data";

            return componentName switch
            {
                var n when n.Contains("Tenkan") =>
                    bar.Close > value
                        ? $"Price above Tenkan at {SpeechPriceFormatter.FormatPrice(value)}"
                        : $"Price below Tenkan at {SpeechPriceFormatter.FormatPrice(value)}",
                var n when n.Contains("Kijun") =>
                    bar.Close > value
                        ? $"Price above Kijun at {SpeechPriceFormatter.FormatPrice(value)}"
                        : $"Price below Kijun at {SpeechPriceFormatter.FormatPrice(value)}",
                var n when n.Contains("Senkou A") || n.Contains("Span A") =>
                    $"Senkou A at {SpeechPriceFormatter.FormatPrice(value)}",
                var n when n.Contains("Senkou B") || n.Contains("Span B") =>
                    $"Senkou B at {SpeechPriceFormatter.FormatPrice(value)}",
                var n when n.Contains("Chikou") =>
                    $"Chikou span at {SpeechPriceFormatter.FormatPrice(value)}",
                _ => null
            };
        }

        // ── GetDetailFact ─────────────────────────────────────────────────────────────────

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("ICHIMOKU", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (index < 0 || data.Length == 0 || index >= data.Length) return string.Empty;

            double tenkan  = GetVal(results, CompTenkan,  index);
            double kijun   = GetVal(results, CompKijun,   index);
            double senkouA = GetVal(results, CompSenkouA, index);
            double senkouB = GetVal(results, CompSenkouB, index);
            double close   = data[index].Close;

            var sb = new StringBuilder();

            // Sentence 1: TK cross
            if (!double.IsNaN(tenkan) && !double.IsNaN(kijun))
            {
                string tkCross = tenkan > kijun ? "bullish" : tenkan < kijun ? "bearish" : "flat";
                sb.Append($"TK cross {tkCross}. ");
            }

            // Sentence 2: Price vs Kijun
            if (!double.IsNaN(kijun))
            {
                string priceVsKijun = close > kijun ? "above" : close < kijun ? "below" : "at";
                sb.Append($"Price {priceVsKijun} Kijun at {SpeechPriceFormatter.FormatPrice(kijun)}. ");
            }

            // Sentence 3: Price vs Kumo
            if (!double.IsNaN(senkouA) && !double.IsNaN(senkouB))
            {
                double kumoTop    = Math.Max(senkouA, senkouB);
                double kumoBottom = Math.Min(senkouA, senkouB);
                string pricePos = close > kumoTop    ? "above cloud"
                                : close < kumoBottom ? "below cloud"
                                :                      "inside cloud";
                string cloud = senkouA > senkouB ? "bullish" : senkouA < senkouB ? "bearish" : "neutral";
                sb.Append($"Price {pricePos}. Cloud {cloud}.");
            }
            else
            {
                sb.Append("Cloud not yet formed.");
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "Ichimoku: insufficient data.";
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] src, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len  = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = src[i];
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> results, string key, int index)
        {
            if (results.TryGetValue(key, out var arr) && index < arr.Length) return arr[index];
            return double.NaN;
        }

        private static int GetInt(Dictionary<string, object> p, string k, int def)
        {
            if (p.TryGetValue(k, out var v))
            {
                if (v is int i)    return i;
                if (v is double d) return (int)d;
                if (int.TryParse(v?.ToString(), out int parsed)) return parsed;
            }
            return def;
        }
    }
}
