using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// COT Positioning — weekly hedge-fund positioning from the CFTC Commitment of
    /// Traders report, promoted from the StrategyLab CftcCotProvider after the
    /// 2026-07 cross-asset validation. Reads the CFTC analytics plugin through the
    /// shared <see cref="ICrossSeriesCache"/> and displays the 26-week positioning
    /// z-score with crowded-long / crowded-short extreme markers.
    ///
    /// What the research says (era-sliced, 2006–2026, ±1.5σ extremes)
    /// ───────────────────────────────────────────────────────────────
    ///   • GOLD — contrarian and era-stable. Crowded-short extremes preceded
    ///     above-baseline forward returns; crowded-long extremes collapsed gold's
    ///     forward drift to ~zero in every era. Treat crowded-long as a
    ///     "longs degraded" warning.
    ///   • S&amp;P — crowded-long degrades oversold dip-buys (+2.2%/20d when not
    ///     crowded vs −0.3% when crowded). Use as a long-entry gate.
    ///   • EUR/FX — INVERTED: speculators are informed trend flow. Crowded-short
    ///     preceded further declines. Do not trade FX contrarian off this series.
    ///   • BTC/ETH — CME leveraged-funds positioning is contaminated by the
    ///     futures basis trade; both extremes preceded above-baseline returns.
    ///     Prefer funding rate / open interest for crypto crowding.
    ///
    /// The z-score is the plotted series (net-%-of-OI scales differ wildly per
    /// contract; the z puts gold and the S&amp;P on one audible scale). The raw
    /// net % of open interest is available via the detail fact and speech.
    ///
    /// Positions are as-of Tuesday, published Friday; the CFTC plugin stamps values
    /// at the release date, so nothing here is knowable before it was public.
    /// </summary>
    public class CotPositioningProvider : IIndicatorProvider
    {
        public string Name => "COT Positioning";

        public const string CompNetPctOi     = "Net % of OI";
        public const string CompZScore       = "Positioning Z-Score";
        public const string CompCrowdedLong  = "Crowded Long";
        public const string CompCrowdedShort = "Crowded Short";

        private const string Code = "COT_POSITIONING";
        private const int DefaultZWindow = 26;      // ~6 months of weekly reports
        private const double DefaultExtremeZ = 1.5; // top/bottom ~13% of positioning history

        // Chart symbol → CFTC plugin symbol. Matching is on the BASE token of the
        // chart symbol ("XAU/USD" → "XAU", "BTC/USDT" → "BTC", "SPY" → "SPY") so
        // quote currencies never mis-route. No match → indicator stays empty and
        // the detail fact explains why.
        private static readonly (string[] Bases, string CotSymbol)[] SymbolMap =
        {
            (new[] { "BTC", "XBT" },                              "BITCOIN_COT"),
            (new[] { "ETH" },                                     "ETHER_COT"),
            (new[] { "XAU", "GOLD", "GC", "GLD" },                "GOLD_COT"),
            (new[] { "XAG", "SILVER", "SI", "SLV" },              "SILVER_COT"),
            (new[] { "HG", "COPPER" },                            "COPPER_COT"),
            (new[] { "WTI", "CL", "USOIL", "OIL", "USO" },        "WTI_CRUDE_COT"),
            (new[] { "NG", "NATGAS" },                            "NATGAS_COT"),
            (new[] { "SPX", "SPY", "ES", "SP500" },               "SP500_COT"),
            (new[] { "NDX", "QQQ", "NQ", "NASDAQ" },              "NASDAQ_COT"),
            (new[] { "EUR" },                                     "EURO_FX_COT"),
            (new[] { "DXY", "DX", "USDX" },                       "USD_INDEX_COT"),
        };

        internal static string? MapSymbol(string? chartSymbol)
        {
            if (string.IsNullOrWhiteSpace(chartSymbol)) return null;
            string baseToken = chartSymbol.Split('/', '-', ':')[0].Trim().ToUpperInvariant();
            foreach (var (bases, cot) in SymbolMap)
                foreach (var b in bases)
                    if (baseToken == b) return cot;
            return null;
        }

        private static CrossSeriesRequest? BuildRequest(Dictionary<string, object> parameters)
        {
            string? sym = null;
            if (parameters != null &&
                parameters.TryGetValue("__symbol", out var raw) &&
                raw is string active)
            {
                sym = MapSymbol(active);
            }
            if (sym == null) return null;
            return new CrossSeriesRequest(
                Market: "Derivatives",
                Provider: "CFTC",
                Symbol: sym,
                Timeframe: "1w",
                MaxPages: 1);
        }

        private readonly ICrossSeriesCache _xs;

        public CotPositioningProvider(ICrossSeriesCache xs)
        {
            _xs = xs;
        }

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = Code,
                Causality = ComponentCausality.Causal,
                Name        = "COT Positioning",
                Category    = "Positioning",
                DefaultPane = "Pane_COT",
                Description = "CFTC Commitment of Traders — weekly hedge-fund positioning as a 26-week " +
                              "z-score, with crowded extremes at ±1.5 sigma. Contract auto-selected from " +
                              "the chart symbol (gold, silver, copper, oil, gas, Bitcoin, Ether, S&P, " +
                              "Nasdaq, Euro, Dollar Index). INTERPRETATION IS PER-ASSET: contrarian and " +
                              "era-stable on gold; a long-entry crowding gate on the S&P; INVERTED on FX " +
                              "(specs are informed trend flow); contaminated by the basis trade on CME " +
                              "crypto — prefer funding/OI there. Data publishes Friday for Tuesday " +
                              "positions; values appear on their release date.",
                RequiresFullRecalcOnTick = false,

                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompZScore,
                            DisplayName = "Positioning Z-Score",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#BB86FC",
                            DefaultThickness = 2.0f,
                            DefaultWaveform = "sine",
                            DefaultAboveWaveform = "triangle",
                            DefaultBelowWaveform = "sine",
                            DefaultReferenceLevel = 0.0,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultNoiseAmount = 0.10f,
                            DefaultTriggerBoundaryClick = true,
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultIsAreaFill = false,
                            DefaultUsePolarityColoring = true,
                            SpeechTemplate = "Positioning z {value:F2}.",
                            IsVisible = true },

                    // Raw net % of OI — hidden by default so the pane scale stays on the
                    // z-score; still navigable/queryable and available to strategies.
                    new() { Name = CompNetPctOi,
                            DisplayName = "Net % of OI",
                            DisplayType = ComponentDisplayType.Oscillator,
                            Role = ComponentRole.None,
                            DefaultColorHex = "#808080",
                            DefaultThickness = 1.5f,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultUsePolarityColoring = true,
                            SpeechTemplate = "Net {value:F1} percent of open interest.",
                            IsVisible = false },

                    new() { Name = CompCrowdedLong,
                            DisplayName = "Crowded Long",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF4444",
                            DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "detuned_pair_bell",
                            DefaultDecayMs = 320,
                            DefaultBaseFrequency = 440.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Funds crowded long",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    new() { Name = CompCrowdedShort,
                            DisplayName = "Crowded Short",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00E5FF",
                            DefaultThickness = 6.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "sine_bell",
                            DefaultDecayMs = 320,
                            DefaultBaseFrequency = 300.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Funds crowded short",
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "ZWindow", DisplayName = "Z-Score Window (weeks)",
                            DataType = typeof(int), DefaultValue = DefaultZWindow,
                            Description = "Rolling window of UNIQUE weekly reports used for the z-score. " +
                                          "26 weeks (~6 months) matches the StrategyLab validation." },
                    new() { Name = "ExtremeZ", DisplayName = "Extreme Threshold (sigma)",
                            DataType = typeof(double), DefaultValue = DefaultExtremeZ,
                            Description = "Absolute z-score at which the Crowded Long / Crowded Short " +
                                          "markers fire. 1.5 sigma is roughly the top/bottom 13% of " +
                                          "positioning history." }
                }
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals(Code, StringComparison.OrdinalIgnoreCase)) return new();
            return new()
            {
                new("Crowded Long",  DefaultExtremeZ, "#FF2222", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
                new("Neutral", 0.00, "#666666", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.7f),
                new("Crowded Short", -DefaultExtremeZ, "#22FFFF", DashStyle.Dot,
                    PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
            };
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int zWindow = (int)GetDbl(parameters, "ZWindow", DefaultZWindow);
            double extremeZ = GetDbl(parameters, "ExtremeZ", DefaultExtremeZ);
            if (zWindow < 5) zWindow = 5;

            int n = data.Length;
            var netSpan   = buffer.GetComponentSpan(CompNetPctOi);
            var zSpan     = buffer.GetComponentSpan(CompZScore);
            var longSpan  = buffer.GetComponentSpan(CompCrowdedLong);
            var shortSpan = buffer.GetComponentSpan(CompCrowdedShort);

            for (int i = 0; i < n && i < netSpan.Length; i++)
            {
                netSpan[i]   = double.NaN;
                zSpan[i]     = double.NaN;
                longSpan[i]  = double.NaN;
                shortSpan[i] = double.NaN;
            }

            if (n == 0) return;

            var request = BuildRequest(parameters);
            if (request == null) return; // no COT contract for this chart symbol

            var ticks = _xs.GetOrFetch(request);
            if (ticks.Count == 0) return;

            CrossSeriesForwardFill.Fill(ticks, data, netSpan);

            // Rolling z-score over UNIQUE weekly values. Weekly reports forward-fill
            // ~5-7x onto daily bars; enqueueing every bar would understate the
            // variance and inflate |z|. Track value changes only (ported from the
            // StrategyLab CftcCotProvider after cross-asset validation).
            double lastVal = double.NaN;
            var window = new Queue<double>();
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n && i < netSpan.Length; i++)
            {
                double v = netSpan[i];
                if (double.IsNaN(v)) continue;

                if (v != lastVal)
                {
                    window.Enqueue(v);
                    sum += v; sumSq += v * v;
                    if (window.Count > zWindow)
                    {
                        double dropped = window.Dequeue();
                        sum -= dropped; sumSq -= dropped * dropped;
                    }
                    lastVal = v;
                }

                if (window.Count >= 5)
                {
                    double mean = window.Count > 0 ? sum / window.Count : 0;
                    double variance = (sumSq / window.Count) - mean * mean;
                    double sd = variance > 1e-12 ? Math.Sqrt(variance) : 0;
                    if (sd > 0)
                    {
                        double z = (v - mean) / sd;
                        zSpan[i] = z;
                        if (z >= extremeZ) longSpan[i] = z;
                        else if (z <= -extremeZ) shortSpan[i] = z;
                    }
                }
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (componentName == CompZScore)
            {
                if (double.IsNaN(value))
                    return "COT positioning, no data for this bar. The series needs about six months " +
                           "of weekly reports before the z-score is defined.";
                string state = value >= DefaultExtremeZ ? ", crowded long"
                             : value <= -DefaultExtremeZ ? ", crowded short" : "";
                return $"Positioning z {value:F2}{state}.";
            }
            if (componentName == CompNetPctOi && !double.IsNaN(value))
                return $"Funds net {value:F1} percent of open interest.";
            if (componentName == CompCrowdedLong && !double.IsNaN(value))
                return $"Funds crowded long, z {value:F2}.";
            if (componentName == CompCrowdedShort && !double.IsNaN(value))
                return $"Funds crowded short, z {value:F2}.";
            return null;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            bool hasZ = calculatedResults.TryGetValue(CompZScore, out var zArr) && zArr != null
                        && index >= 0 && index < zArr.Length && !double.IsNaN(zArr[index]);
            if (!hasZ)
            {
                string? sym = parameters != null && parameters.TryGetValue("__symbol", out var raw)
                    ? raw as string : null;
                return MapSymbol(sym) == null
                    ? "COT positioning: no CFTC contract mapped for this symbol."
                    : "COT positioning: no data for this bar.";
            }

            double z = zArr![index];
            double net = calculatedResults.TryGetValue(CompNetPctOi, out var netArr) && netArr != null
                         && index < netArr.Length ? netArr[index] : double.NaN;
            string netTxt = double.IsNaN(net) ? "" : $" Net {net:F1}% of open interest.";
            string state = z >= DefaultExtremeZ ? " Crowded long — on gold and equities this has meant degraded forward returns for longs."
                         : z <= -DefaultExtremeZ ? " Crowded short — on gold this has been a contrarian long context."
                         : "";
            return $"Fund positioning z-score {z:F2} (26-week window).{netTxt}{state} Weekly CFTC data, shown from its Friday release.";
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def) =>
            p != null && p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
    }
}
