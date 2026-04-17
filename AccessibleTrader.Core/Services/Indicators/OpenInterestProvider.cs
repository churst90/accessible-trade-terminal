using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Open Interest — total notional value of outstanding perpetual swap contracts.
    ///
    /// Cross-series indicator that reads through <see cref="ICrossSeriesCache"/>. See
    /// <see cref="FundingRateProvider"/> for the architectural background on why cross-series
    /// providers don't await inside Calculate.
    ///
    /// Why open interest matters
    /// ─────────────────────────
    /// OI tells you how much money is on the table in the perpetual swap market right now.
    /// Combined with the direction of price, it answers the most useful question in derivatives
    /// reading: *who is positioning into this move?*
    ///
    ///   Price up + OI up    → new longs entering. Real positioning, strong move.
    ///   Price up + OI down  → shorts covering. "Squeeze rally", weak hands closing, often fades.
    ///   Price down + OI up  → new shorts entering. Real selling, often continues.
    ///   Price down + OI down→ longs capitulating. Late-stage flush, often near a bottom.
    ///
    /// The most actionable signal here is **OI divergence with price** — the OiDivergence
    /// component below. Raw OI line is a context cue (rising regime vs falling regime), and
    /// OiDelta picks out sharp single-bar OI changes that often precede liquidation cascades.
    /// </summary>
    public class OpenInterestProvider : IIndicatorProvider
    {
        public string Name => "Open Interest";

        public const string CompOiValue      = "OI Value";
        public const string CompOiDelta      = "OI Delta";
        public const string CompOiSpike      = "OI Spike";
        public const string CompOiDivergence = "OI Divergence";

        // BinanceVision daily metrics archive gives multi-year OI history for free.
        // 1d cadence is native — each ZIP is one day's 5-min samples, and the
        // BinanceVision plugin pulls the EOD value out of each file. MaxPages=10
        // is unused here since the plugin returns the full history in one call,
        // but left at 10 for consistency with the funding request.
        private static readonly CrossSeriesRequest OiRequest = new(
            Market: "Derivatives",
            Provider: "BinanceVision",
            Symbol: "BTCUSDT_OI",
            Timeframe: "1d",
            MaxPages: 10);

        private readonly ICrossSeriesCache _xs;

        public OpenInterestProvider(ICrossSeriesCache xs)
        {
            _xs = xs;
        }

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "OPEN_INTEREST",
                Name        = "Open Interest",
                Category    = "Derivatives",
                DefaultPane = "Pane_OPEN_INTEREST",
                Description = "Total perpetual swap open interest (cross-series). Fetches OI history " +
                              "from OkxDerivatives and forward-fills onto the active chart by timestamp. " +
                              "OI rising while price rises = new longs (continuation). OI falling while " +
                              "price rises = short cover (often fades). OI Divergence dots fire when " +
                              "5-bar price and OI direction disagree on material moves.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompOiValue,
                            DisplayName = "OI Value",
                            DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF9800",
                            DefaultThickness = 2.0f,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultNoiseAmount = 0.08f,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            SpeechTemplate = "OI {value:F0} dollars.",
                            IsVisible = true },

                    new() { Name = CompOiDelta,
                            DisplayName = "OI Delta",
                            DisplayType = ComponentDisplayType.Histogram,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFB74D",
                            DefaultThickness = 2.0f,
                            DefaultReferenceLevel = 0.0,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultUsePolarityColoring = true,
                            SpeechTemplate = "OI delta {value:F0}.",
                            IsVisible = true },

                    new() { Name = CompOiSpike,
                            DisplayName = "OI Spike",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFA000",
                            DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "detuned_pair_bell",
                            DefaultDecayMs = 320,
                            DefaultBaseFrequency = 380.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Open interest spike",
                            IsVisible = true },

                    new() { Name = CompOiDivergence,
                            DisplayName = "OI Divergence",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#BA68C8",
                            DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 400,
                            DefaultBaseFrequency = 340.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Open interest divergence with price",
                            IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "SpikeThreshold", DisplayName = "Spike Threshold (sigma)",
                            DataType = typeof(double), DefaultValue = 2.0,
                            Description = "Multiple of the rolling OI-delta standard deviation above which " +
                                          "the OI Spike marker fires. 2.0 = top ~5% of bars by delta magnitude." },
                    new() { Name = "DivergenceLookback", DisplayName = "Divergence Lookback (bars)",
                            DataType = typeof(int), DefaultValue = 5.0,
                            Description = "Number of bars over which price direction and OI direction are " +
                                          "compared. Smaller = more sensitive, larger = only major divergences." }
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            double spikeThreshold = GetDbl(parameters, "SpikeThreshold", 2.0);
            int divLookback = Math.Max(2, GetInt(parameters, "DivergenceLookback", 5));

            int n = data.Length;
            var valueSpan = buffer.GetComponentSpan(CompOiValue);
            var deltaSpan = buffer.GetComponentSpan(CompOiDelta);
            var spikeSpan = buffer.GetComponentSpan(CompOiSpike);
            var divSpan   = buffer.GetComponentSpan(CompOiDivergence);

            for (int i = 0; i < n && i < valueSpan.Length; i++)
            {
                valueSpan[i] = double.NaN;
                deltaSpan[i] = double.NaN;
                spikeSpan[i] = double.NaN;
                divSpan[i]   = double.NaN;
            }

            if (n == 0) return;

            var ticks = _xs.GetOrFetch(OiRequest);
            if (ticks.Count == 0) return;

            CrossSeriesForwardFill.Fill(ticks, data, valueSpan);

            // Per-bar delta from the populated value span.
            double prevValue = double.NaN;
            for (int i = 0; i < n && i < deltaSpan.Length; i++)
            {
                double v = valueSpan[i];
                if (double.IsNaN(v)) continue;
                if (!double.IsNaN(prevValue))
                    deltaSpan[i] = v - prevValue;
                prevValue = v;
            }

            // Spike detection: rolling 30-bar stdev of delta. |delta - mean| > k×stdev = spike.
            const int StdWindow = 30;
            for (int i = StdWindow; i < n && i < spikeSpan.Length; i++)
            {
                double sum = 0, sumSq = 0; int count = 0;
                for (int j = i - StdWindow + 1; j <= i; j++)
                {
                    if (j < 0 || j >= deltaSpan.Length) continue;
                    double d = deltaSpan[j];
                    if (double.IsNaN(d)) continue;
                    sum += d; sumSq += d * d; count++;
                }
                if (count < 5) continue;
                double mean = sum / count;
                double variance = (sumSq / count) - (mean * mean);
                if (variance <= 0) continue;
                double stdev = Math.Sqrt(variance);
                double cur = deltaSpan[i];
                if (!double.IsNaN(cur) && Math.Abs(cur - mean) > spikeThreshold * stdev)
                    spikeSpan[i] = valueSpan[i];
            }

            // Divergence: 5-bar price direction vs OI direction, both moves material.
            for (int i = divLookback; i < n && i < divSpan.Length; i++)
            {
                double oiNow  = valueSpan[i];
                double oiPrev = valueSpan[i - divLookback];
                if (double.IsNaN(oiNow) || double.IsNaN(oiPrev) || oiPrev == 0) continue;

                double pNow  = data[i].Close;
                double pPrev = data[i - divLookback].Close;
                if (pPrev == 0) continue;

                double priceChangePct = (pNow - pPrev) / pPrev;
                double oiChangePct    = (oiNow - oiPrev) / oiPrev;

                if (Math.Abs(priceChangePct) < 0.003) continue;
                if (Math.Abs(oiChangePct)    < 0.005) continue;

                if (Math.Sign(priceChangePct) != Math.Sign(oiChangePct))
                    divSpan[i] = oiNow;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 30;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (componentName == CompOiValue)
            {
                if (double.IsNaN(value))
                    return "Open interest, no data for this bar. Try a recent intraday range.";
                double bn = value / 1_000_000_000.0;
                return $"Open interest {bn:F2} billion dollars.";
            }
            if (componentName == CompOiDelta && !double.IsNaN(value))
            {
                string side = value > 0 ? "adding positions" : value < 0 ? "closing positions" : "flat";
                double mn = value / 1_000_000.0;
                return $"OI delta {mn:F1} million, {side}.";
            }
            if (componentName == CompOiSpike && !double.IsNaN(value))
                return "Open interest spike, large positioning shift this bar.";
            if (componentName == CompOiDivergence && !double.IsNaN(value))
            {
                if (allComponentData.TryGetValue(CompOiValue, out var oiArr) && oiArr != null
                    && dataIndex >= 5 && dataIndex < oiArr.Length)
                {
                    double oiNow = oiArr[dataIndex], oiPrev = oiArr[dataIndex - 5];
                    if (!double.IsNaN(oiNow) && !double.IsNaN(oiPrev))
                    {
                        bool oiUp = oiNow > oiPrev;
                        return oiUp
                            ? "OI divergence: open interest rising while price falling. Possible reversal up."
                            : "OI divergence: open interest falling while price rising. Possible squeeze top.";
                    }
                }
                return "Open interest divergence with price.";
            }
            return null;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (!calculatedResults.TryGetValue(CompOiValue, out var arr) || arr == null
                || index < 0 || index >= arr.Length || double.IsNaN(arr[index]))
                return "Open interest: no data";
            double bn = arr[index] / 1_000_000_000.0;
            return $"Open interest {bn:F2} billion USD on OKX BTC-USDT perpetual.";
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
        private static int GetInt(Dictionary<string, object> p, string k, int def) =>
            p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
    }
}
