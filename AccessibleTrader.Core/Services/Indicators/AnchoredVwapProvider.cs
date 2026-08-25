using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Anchored VWAP — Brian Shannon's "AVWAP" methodology. Volume-weighted
    /// average price computed forward from the most recent confirmed swing
    /// high (`VwapFromHigh`) and most recent confirmed swing low
    /// (`VwapFromLow`). Each anchor resets when a new pivot of that type
    /// confirms (PivotLookback bars on each side).
    ///
    /// Why anchored not session: institutions track "the average price all
    /// participants have paid since [reference event]." Significant pivots
    /// — major lows, blow-off highs, news events — are natural anchors
    /// because every bar after the anchor is part of the same regime.
    /// AVWAP is then a magnet (price tends to mean-revert toward it) and
    /// a level (price holding above the anchor's AVWAP = bullish bias).
    ///
    /// Components:
    ///   VwapFromHigh — Cumulative Σ(typical*vol)/Σvol since last swing high
    ///   VwapFromLow  — Same since last swing low
    ///   Bias         — Signed score: +1 if close above both, -1 if below both, 0 mixed
    ///
    /// All math universal — works on any TF and any asset that has volume.
    /// </summary>
    public sealed class AnchoredVwapProvider : IIndicatorProvider
    {
        public string Name => "Anchored VWAP";

        public const string Code         = "ANCHORED_VWAP";
        public const string CompFromHigh = "VWAP From High";
        public const string CompFromLow  = "VWAP From Low";
        public const string CompBias     = "AVWAP Bias";
        public const string CompBiasSoft = "AVWAP Bias Soft";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = Code,
                Causality = ComponentCausality.Causal,
                Name        = "Anchored VWAP",
                Category    = "Reference Level",
                DefaultPane = "Main",
                Description =
                    "Volume-weighted average price anchored to the most recent confirmed " +
                    "swing high and swing low (Brian Shannon AVWAP methodology). Re-anchors " +
                    "automatically when a new pivot confirms. Plotted as overlay lines on " +
                    "the price pane; Bias is a +1 / 0 / -1 signed component for strategy gating.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompFromHigh, DisplayName = "VWAP From High",
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF7043", DefaultThickness = 1.8f,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "sine",
                            DefaultPitchMapping = PitchMapping.Value,
                            SpeechTemplate = "AVWAP from high {value:price}.",
                            IsVisible = true },
                    new() { Name = CompFromLow, DisplayName = "VWAP From Low",
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            DefaultColorHex = "#26A69A", DefaultThickness = 1.8f,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultWaveform = "sine",
                            DefaultPitchMapping = PitchMapping.Value,
                            SpeechTemplate = "AVWAP from low {value:price}.",
                            IsVisible = true },
                    new() { Name = CompBias, DisplayName = "AVWAP Bias",
                            DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFB300", DefaultThickness = 1.0f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultReferenceLevel = 0.0,
                            DefaultPitchMapping = PitchMapping.Direction,
                            SpeechTemplate = "AVWAP bias {value:F0}.",
                            IsVisible = false },
                    new() { Name = CompBiasSoft, DisplayName = "AVWAP Bias Soft",
                            DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFA000", DefaultThickness = 1.0f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultReferenceLevel = 0.0,
                            DefaultPitchMapping = PitchMapping.Direction,
                            SpeechTemplate = "AVWAP bias soft {value:F0}.",
                            IsVisible = false },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "PivotLookback", DisplayName = "Pivot Lookback (bars)",
                            DataType = typeof(int), DefaultValue = 5.0,
                            Description = "Bars on either side required to confirm a swing pivot." },
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int pivotLb = Math.Max(2, GetInt(parameters, "PivotLookback", 5));
            int n = data.Length;
            var fhSpan = buffer.GetComponentSpan(CompFromHigh);
            var flSpan = buffer.GetComponentSpan(CompFromLow);
            var biasSpan = buffer.GetComponentSpan(CompBias);
            var biasSoftSpan = buffer.GetComponentSpan(CompBiasSoft);

            for (int i = 0; i < n; i++)
            {
                if (i < fhSpan.Length)       fhSpan[i]       = double.NaN;
                if (i < flSpan.Length)       flSpan[i]       = double.NaN;
                if (i < biasSpan.Length)     biasSpan[i]     = double.NaN;
                if (i < biasSoftSpan.Length) biasSoftSpan[i] = double.NaN;
            }
            if (n < pivotLb * 2 + 2) return;

            int lastHighAnchor = -1, lastLowAnchor = -1;
            double sumPVHigh = 0, sumVHigh = 0;
            double sumPVLow  = 0, sumVLow  = 0;

            for (int i = 0; i < n; i++)
            {
                // Detect a confirmed pivot at i - pivotLb (pivotLb bars back, with pivotLb
                // bars on each side — which means we can confirm starting at i = 2*pivotLb).
                int pIdx = i - pivotLb;
                if (pIdx >= pivotLb && pIdx + pivotLb < n)
                {
                    bool isHigh = true, isLow = true;
                    double pH = data[pIdx].High, pL = data[pIdx].Low;
                    for (int k = 1; k <= pivotLb; k++)
                    {
                        if (data[pIdx - k].High >= pH) isHigh = false;
                        if (data[pIdx + k].High >= pH) isHigh = false;
                        if (data[pIdx - k].Low  <= pL) isLow  = false;
                        if (data[pIdx + k].Low  <= pL) isLow  = false;
                        if (!isHigh && !isLow) break;
                    }
                    if (isHigh)
                    {
                        lastHighAnchor = pIdx;
                        sumPVHigh = 0;
                        sumVHigh = 0;
                    }
                    if (isLow)
                    {
                        lastLowAnchor = pIdx;
                        sumPVLow = 0;
                        sumVLow = 0;
                    }
                }

                double typical = (data[i].High + data[i].Low + data[i].Close) / 3.0;
                double v = data[i].Volume;

                if (lastHighAnchor >= 0 && i >= lastHighAnchor)
                {
                    sumPVHigh += typical * v;
                    sumVHigh  += v;
                    if (sumVHigh > 0 && i < fhSpan.Length) fhSpan[i] = sumPVHigh / sumVHigh;
                }
                if (lastLowAnchor >= 0 && i >= lastLowAnchor)
                {
                    sumPVLow += typical * v;
                    sumVLow  += v;
                    if (sumVLow > 0 && i < flSpan.Length) flSpan[i] = sumPVLow / sumVLow;
                }

                if (i < biasSpan.Length || i < biasSoftSpan.Length)
                {
                    double fh = i < fhSpan.Length ? fhSpan[i] : double.NaN;
                    double fl = i < flSpan.Length ? flSpan[i] : double.NaN;
                    if (!double.IsNaN(fh) && !double.IsNaN(fl))
                    {
                        bool above = data[i].Close > fh;
                        bool below = data[i].Close < fl;
                        // Strict bias: requires close above BOTH (or below BOTH).
                        if (i < biasSpan.Length)
                        {
                            biasSpan[i] = above && !below ?  1.0
                                        : below && !above ? -1.0
                                        :                    0.0;
                        }
                        // Soft bias: +1 if close above EITHER anchor, -1 if below
                        // BOTH, 0 only when between them. Lets the gate fire more
                        // often — caller can use Bias for strict, BiasSoft for permissive.
                        if (i < biasSoftSpan.Length)
                        {
                            bool aboveEither = data[i].Close > fh || data[i].Close > fl;
                            bool belowBoth   = data[i].Close < fh && data[i].Close < fl;
                            biasSoftSpan[i] = belowBoth ? -1.0
                                            : aboveEither ?  1.0
                                            :                0.0;
                        }
                    }
                }
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int pivotLb = GetInt(parameters, "PivotLookback", 5);
            return pivotLb * 2 + 5;
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            return componentName switch
            {
                CompFromHigh => $"AVWAP from high {Accessibility.SpeechPriceFormatter.FormatPrice(value)}.",
                CompFromLow  => $"AVWAP from low {Accessibility.SpeechPriceFormatter.FormatPrice(value)}.",
                CompBias     => $"AVWAP bias {value:F0}.",
                _ => null
            };
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            double fh = TryGet(calculatedResults, CompFromHigh, index);
            double fl = TryGet(calculatedResults, CompFromLow, index);
            string fhS = double.IsNaN(fh) ? "n/a" : fh.ToString("F2");
            string flS = double.IsNaN(fl) ? "n/a" : fl.ToString("F2");
            return $"Anchored VWAP: from high {fhS}, from low {flS}.";
        }

        private static double TryGet(IReadOnlyDictionary<string, double[]> dict, string key, int idx)
        {
            if (!dict.TryGetValue(key, out var arr) || arr == null
                || idx < 0 || idx >= arr.Length) return double.NaN;
            return arr[idx];
        }

        private static int GetInt(Dictionary<string, object> p, string k, int def) =>
            p != null && p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
    }
}
