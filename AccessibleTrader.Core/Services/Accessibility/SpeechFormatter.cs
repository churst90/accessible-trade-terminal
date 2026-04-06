using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface ISpeechFormatter
    {
        string FormatPointFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, Ohlcv pt, string prefixMessage);
        string FormatProfileFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int binIndex, string prefixMessage);
        string FormatHeatmapFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int dataIndex, int binIndex, string prefixMessage);
        string FormatViewportDescription(int count, DateTime start, DateTime end);
        void RegisterTemplate(string indicatorCode, string componentName, string template);
    }

    public class SpeechFormatter : ISpeechFormatter
    {
        public void RegisterTemplate(string indicatorCode, string componentName, string template)
        {
            // No-op: kept for interface compatibility. Templates are now declared in
            // provider metadata via IndicatorComponentMetadata.SpeechTemplate.
        }

        public string FormatViewportDescription(int count, DateTime start, DateTime end)
        {
            string fmt = "MMMM d yyyy";
            return $"Viewing {count} bars from {start.ToLocalTime().ToString(fmt)} to {end.ToLocalTime().ToString(fmt)}";
        }

        public string FormatPointFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, Ohlcv pt, string prefixMessage)
        {
            string msg = string.Empty;
            bool summary = state.LastInteractionContext == InteractionContext.Series;

            string seriesId = series.Id.ToLowerInvariant();

            if (summary && seriesId == "candles")
            {
                string trend = pt.Close >= pt.Open ? "Bullish" : "Bearish";
                string candleType = ClassifyCandleType(pt);
                string typeStr = string.IsNullOrEmpty(candleType) ? "" : $" {candleType}";
                
                double range = pt.High - pt.Low;
                double body = Math.Abs(pt.Close - pt.Open);
                double bodyPct = range > 0 ? (body / range) * 100.0 : 0;
                double upperWick = pt.High - Math.Max(pt.Open, pt.Close);
                double lowerWick = Math.Min(pt.Open, pt.Close) - pt.Low;
                double upperPct = range > 0 ? (upperWick / range) * 100.0 : 0;
                double lowerPct = range > 0 ? (lowerWick / range) * 100.0 : 0;

                msg = $"{trend}{typeStr}. Close {pt.Close:F2}. Open {pt.Open:F2}. " +
                      $"High {pt.High:F2}. Low {pt.Low:F2}. Volume {pt.Volume:F2}. " +
                      $"Body {bodyPct:F0}%, Upper wick {upperPct:F0}%, Lower wick {lowerPct:F0}%.";
            }
            else if (summary && seriesId == "price")
            {
                var priceComp = series.Components.FirstOrDefault(c => c.IsVisible && !c.IsMuted);
                string lineType = priceComp != null ? FriendlyTypeName(priceComp.DisplayType) : "line";
                msg = $"{series.Name}. {lineType}. {pt.Close:F2}.";
            }
            else if (summary)
            {
                var values = series.Components
                    .Where(c => c.IsVisible && !c.IsMuted)
                    .Select(c => FormatTemplateValue(series, c, pt, state.CurrentDataIndex, state.ReadColumnHeaders, state.SpeechOrder));

                msg = string.Join(". ", values);
            }
            else
            {
                if (series.Components.Count == 0) return "";
                var compIndex = Math.Clamp(state.FocusedComponentIndex, 0, series.Components.Count - 1);
                var comp = series.Components[compIndex];
                msg = FormatTemplateValue(series, comp, pt, state.CurrentDataIndex, state.ReadColumnHeaders, state.SpeechOrder);
            }

            // STRICT SPEECH POLICY: Apply settings to timestamps
            bool shouldSpeakTimestamp = state.SpeakTimestamps;
            if (shouldSpeakTimestamp)
            {
                if (state.TimestampReadLocation == "Along X Axis" && !isXMove) shouldSpeakTimestamp = false;
                else if (state.TimestampReadLocation == "Along Y Axis" && !isYMove) shouldSpeakTimestamp = false;
                else if (state.TimestampReadLocation == "None") shouldSpeakTimestamp = false;
            }

            string timestampFormat = "MMMM dd, yyyy, HH:mm";
            if (state.SpeechOrder.Contains("TimeOnly")) timestampFormat = "HH:mm";
            else if (state.SpeechOrder.Contains("DateOnly")) timestampFormat = "MMMM dd";

            string timestamp = shouldSpeakTimestamp ? pt.Date.ToLocalTime().ToString(timestampFormat) + ". " : "";

            return timestamp + prefixMessage + msg;
        }

        public string FormatProfileFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int binIndex, string prefixMessage)
        {
            if (binIndex < 0 || series.ProfileBins == null || binIndex >= series.ProfileBins.Count) return "";
            var allBins = series.ProfileBins;
            var bin     = allBins[binIndex];

            string dataMsg;
            if (double.IsNaN(bin.TotalVolume) || bin.TotalVolume == 0)
            {
                dataMsg = $"Price {bin.PriceLow:F2} to {bin.PriceHigh:F2}, no data.";
            }
            else
            {
                var nodeType = ProfileBinClassifier.Classify(bin, allBins);
                string nodeLabel = ProfileBinClassifier.GetLabel(nodeType);

                // Percentage of total session volume for context.
                double totalVol = allBins.Sum(b => b.TotalVolume);
                double pct = totalVol > 0 ? bin.TotalVolume / totalVol * 100.0 : 0;

                if (bin.TpoLetters.Any())
                {
                    // TPO mode: report time periods (letters) rather than volume.
                    string letters = string.Join(" ", bin.TpoLetters);
                    string labelPart = string.IsNullOrEmpty(nodeLabel) ? "" : $", {nodeLabel}";
                    dataMsg = $"Price {bin.PriceLow:F2} to {bin.PriceHigh:F2}, " +
                              $"{bin.TpoPeriodCount:F0} {(bin.TpoPeriodCount == 1 ? "period" : "periods")}, " +
                              $"letters {letters}{labelPart}.";
                }
                else
                {
                    string labelPart = string.IsNullOrEmpty(nodeLabel) ? "" : $"{nodeLabel}, ";
                    dataMsg = $"Price {bin.PriceLow:F2} to {bin.PriceHigh:F2}, " +
                              $"{labelPart}" +
                              $"{FormatVolume(bin.TotalVolume)} contracts, " +
                              $"{pct:F1} percent.";
                }
            }

            // Timestamps on profiles only when moving across time (X axis).
            bool shouldSpeakTimestamp = state.SpeakTimestamps && isXMove
                && state.Data != null && state.CurrentDataIndex >= 0 && state.CurrentDataIndex < state.Data.Count;
            string timestamp = shouldSpeakTimestamp
                ? state.Data![state.CurrentDataIndex].Date.ToLocalTime().ToString("HH:mm") + ". "
                : "";

            return timestamp + prefixMessage + dataMsg;
        }

        public string FormatHeatmapFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int dataIndex, int binIndex, string prefixMessage)
        {
            if (series.HeatmapData == null || dataIndex < 0 || dataIndex >= series.HeatmapData.Count)
                return "No data.";

            var bar = series.HeatmapData[dataIndex];
            if (bar == null || !bar.Any())
                return "No data at this bar.";

            // Time label for the bar — always relevant for heatmaps (both axes navigable).
            string timeLabel = "";
            if (state.Data != null && dataIndex >= 0 && dataIndex < state.Data.Count)
                timeLabel = state.Data[dataIndex].Date.ToLocalTime().ToString("HH:mm") + ", ";

            string dataMsg;
            if (binIndex < 0 || binIndex >= bar.Count)
            {
                // No bin focused — announce the column's peak.
                var peak = bar.MaxBy(b => b.TotalVolume);
                if (peak == null || peak.TotalVolume <= 0)
                {
                    dataMsg = "no liquidity.";
                }
                else
                {
                    // Classify against this bar to get a consistent intensity label.
                    double barMax = bar.Max(b => b.TotalVolume);
                    var classifyBins = BuildClassifyBins(bar, barMax);
                    var peakClassify = classifyBins[bar.IndexOf(peak)];
                    var nodeType = ProfileBinClassifier.Classify(peakClassify, classifyBins);
                    string label = ProfileBinClassifier.GetLabel(nodeType);
                    string labelPart = string.IsNullOrEmpty(label) ? "" : $", {label}";
                    dataMsg = $"peak at price {peak.PriceMid:F2}{labelPart}, {FormatVolume(peak.TotalVolume)} contracts.";
                }
            }
            else
            {
                var bin     = bar[binIndex];
                double barMax = bar.Max(b => b.TotalVolume);
                var classifyBins = BuildClassifyBins(bar, barMax);
                var nodeType = ProfileBinClassifier.Classify(classifyBins[binIndex], classifyBins);
                string nodeLabel = ProfileBinClassifier.GetLabel(nodeType);
                string labelPart = string.IsNullOrEmpty(nodeLabel) ? "" : $", {nodeLabel}";

                // Percentage of this column's volume for relative intensity.
                double colTotal = bar.Sum(b => b.TotalVolume);
                double pct = colTotal > 0 ? bin.TotalVolume / colTotal * 100.0 : 0;

                dataMsg = $"price {bin.PriceLow:F2} to {bin.PriceHigh:F2}" +
                          $"{labelPart}, " +
                          $"{FormatVolume(bin.TotalVolume)} contracts, " +
                          $"{pct:F1} percent.";
            }

            // Timestamp first, then prefix, then data — consistent with FormatPointFeedback ordering.
            string timestampPrefix = state.SpeakTimestamps ? timeLabel : "";
            return timestampPrefix + prefixMessage + dataMsg;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        // Phase names for CandleColor display type (Cipher S and any future sentiment overlays).
        // Index 0 = Max Fear … 10 = Max Euphoria.  Matches CipherSProvider.PhaseNames exactly.
        private static readonly string[] _phaseNames =
        {
            "Max Fear", "Fear", "Concern", "Caution", "Mild Caution",
            "Neutral", "Mild Greed", "Greed", "High Greed", "Extreme Greed", "Max Euphoria"
        };

        /// <summary>
        /// Maps ComponentDisplayType to a TTS-friendly lowercase string.
        /// Prevents internal enum names like "ZeroArea" being mangled by the speech engine.
        /// </summary>
        private static string FriendlyTypeName(ComponentDisplayType dt) => dt switch
        {
            ComponentDisplayType.Line       => "line",
            ComponentDisplayType.Area       => "area",
            ComponentDisplayType.Oscillator => "oscillator",
            ComponentDisplayType.ZeroArea   => "oscillator",
            ComponentDisplayType.Histogram  => "histogram",
            ComponentDisplayType.Bar        => "bar",
            ComponentDisplayType.Dot        => "dot",
            ComponentDisplayType.ZeroDot    => "dot",
            ComponentDisplayType.Arrow      => "arrow",
            ComponentDisplayType.StepLine   => "step line",
            ComponentDisplayType.Gradient   => "gradient",
            ComponentDisplayType.Cloud      => "cloud",
            ComponentDisplayType.Heatmap    => "heatmap",
            ComponentDisplayType.Wick       => "wick",
            ComponentDisplayType.Candle      => "candle",
            ComponentDisplayType.TriangleUp   => "triangle up",
            ComponentDisplayType.TriangleDown => "triangle down",
            ComponentDisplayType.Diamond      => "diamond",
            ComponentDisplayType.Square       => "square",
            ComponentDisplayType.Cross        => "cross",
            ComponentDisplayType.CandleColor  => "sentiment phase",
            _                                 => dt.ToString().ToLower()
        };

        /// <summary>Formats large volume numbers for natural speech (e.g., 24350 → "24,350").</summary>
        private static string FormatVolume(double vol)
            => vol >= 1_000_000 ? $"{vol / 1_000_000:F2}M"
             : vol >= 1_000     ? $"{vol:N0}"
             : vol.ToString("F0");

        /// <summary>
        /// Builds a temporary ProfileBin list for classification from heatmap bar data.
        /// Marks the column's highest-volume bin as IsPOC so the classifier can identify HVN/LVN
        /// relative to the column mean.
        /// </summary>
        private static List<ProfileBin> BuildClassifyBins(List<ProfileBin> bar, double barMax)
        {
            return bar.Select(b => new ProfileBin
            {
                PriceLow       = b.PriceLow,
                PriceHigh      = b.PriceHigh,
                TotalVolume    = b.TotalVolume,
                TpoPeriodCount = 0,
                IsPOC          = Math.Abs(b.TotalVolume - barMax) < 1e-9,
                IsValueArea    = false,
            }).ToList();
        }


        private string FormatTemplateValue(ChartSeries series, ComponentConfig comp, Ohlcv pt, int dataIndex, bool readHeaders, string speechOrder)
        {
            try
            {
                // Hidden components: announce so the user knows where they are during Y navigation.
                if (!comp.IsVisible)
                    return $"{comp.DisplayName}: hidden";

                // Cloud components are visual-only.
                if (comp.DisplayType == ComponentDisplayType.Cloud)
                    return $"{comp.DisplayName}. Visual only.";

                // CandleColor components store a sentiment phase index (0–10).
                // Convert the raw numeric value to its phase name so the user hears
                // "Market Phase. Neutral." instead of the meaningless "CandleColor 5."
                if (comp.DisplayType == ComponentDisplayType.CandleColor)
                {
                    double rawPhase = GetPointValue(series, pt, comp.Name, dataIndex);
                    if (double.IsNaN(rawPhase)) return $"{comp.DisplayName}: warming up.";
                    int phaseIdx = Math.Clamp((int)Math.Round(rawPhase), 0, _phaseNames.Length - 1);
                    return $"{comp.DisplayName}. {_phaseNames[phaseIdx]}.";
                }

                double val = GetPointValue(series, pt, comp.Name, dataIndex);

                // SignalSpeechTemplate: marker components use a contextual description instead of raw numeric template.
                // When the signal IS present (non-NaN): expand the template.
                // When the signal is NOT present (NaN): announce "[Name]: no data" — never misleading, never silent.
                if (comp.SignalSpeechTemplate != null && AudioConstants.MarkerDisplayTypes.Contains(comp.DisplayType))
                {
                    if (double.IsNaN(val))
                        return $"{comp.DisplayName}: no data";  // user needs to know where they are
                    string priceStr = val.ToString("F0");
                    return comp.SignalSpeechTemplate
                        .Replace("{price}", priceStr)
                        .Replace("{name}", comp.DisplayName);
                }

                string valF2 = double.IsNaN(val) ? "no data" : val.ToString("F2");
                string valF1 = double.IsNaN(val) ? "no data" : val.ToString("F1");

                if (!readHeaders || speechOrder == "ValueOnly")
                    return double.IsNaN(val) ? "no data" : valF2;

                // Template priority: provider metadata (comp.SpeechTemplate) → generic fallback.
                string tmpl = string.IsNullOrEmpty(comp.SpeechTemplate) ? "{name}. {type}. {value}." : comp.SpeechTemplate;

                string trend = pt.Close >= pt.Open ? "Bullish" : "Bearish";

                // Zone label: scan visible LevelConfig entries for OB/OS thresholds.
                string zone = "";
                foreach (var lc in series.Config.Levels)
                {
                    if (!lc.IsVisible) continue;
                    if (lc.Name.Contains("Overbought", StringComparison.OrdinalIgnoreCase) ||
                        lc.Name.Contains("Extreme OB", StringComparison.OrdinalIgnoreCase))
                    {
                        if (val >= lc.Value) { zone = "Overbought"; break; }
                    }
                    else if (lc.Name.Contains("Oversold", StringComparison.OrdinalIgnoreCase) ||
                             lc.Name.Contains("Extreme OS", StringComparison.OrdinalIgnoreCase))
                    {
                        if (val <= lc.Value) { zone = "Oversold"; break; }
                    }
                }

                // Gradient speech: qualitative momentum language for gradient-dot components.
                // Reads the companion "_color" array (raw oscillator value, ~-100 to +100) and
                // maps it to a direction + intensity description.
                string gradientSpeech = "";
                if (comp.UsesGradientSpeech && tmpl.Contains("{gradient_speech}"))
                {
                    double oscillatorVal = GetPointValue(series, pt, comp.Name + "_color", dataIndex);
                    if (!double.IsNaN(oscillatorVal))
                    {
                        string intensity = oscillatorVal > 60.0  ? "strong bullish momentum"
                                         : oscillatorVal > 20.0  ? "moderate bullish momentum"
                                         : oscillatorVal >= -20.0 ? "neutral momentum"
                                         : oscillatorVal >= -60.0 ? "moderate bearish momentum"
                                         :                          "strong bearish momentum";
                        gradientSpeech = $"{intensity}, {oscillatorVal:F1}";
                    }
                    else
                    {
                        gradientSpeech = "no data";
                    }
                }

                return tmpl
                    .Replace("{gradient_speech}", gradientSpeech)
                    .Replace("{name}", comp.DisplayName)
                    .Replace("{type}", FriendlyTypeName(comp.DisplayType))
                    .Replace("{value:F1}", valF1)
                    .Replace("{value:F2}", valF2)
                    .Replace("{value}",   valF2)
                    .Replace("{trend}", trend)
                    .Replace("{zone}", zone);
            }
            catch (Exception)
            {
                return $"{comp.DisplayName}: error";
            }
        }

        private static string ClassifyCandleType(Ohlcv bar)
        {
            double range = bar.High - bar.Low;
            if (range <= 0) return "";
            double body     = Math.Abs(bar.Close - bar.Open);
            double bodyPct  = body / range;
            double upper    = bar.High - Math.Max(bar.Open, bar.Close);
            double lower    = Math.Min(bar.Open, bar.Close) - bar.Low;
            double upperPct = upper / range;
            double lowerPct = lower / range;
            if (bodyPct < 0.05)
            {
                if (lowerPct > 0.6 && upperPct < 0.1) return "Dragonfly Doji";
                if (upperPct > 0.6 && lowerPct < 0.1) return "Gravestone Doji";
                return "Doji";
            }
            if (bodyPct > 0.90) return bar.Close >= bar.Open ? "Marubozu" : "Bearish Marubozu";
            if (bodyPct < 0.30 && lowerPct > 0.60 && upperPct < 0.10) return "Hammer";
            if (bodyPct < 0.30 && upperPct > 0.60 && lowerPct < 0.10) return "Shooting Star";
            if (bodyPct < 0.30 && upperPct > 0.25 && lowerPct > 0.25) return "Spinning Top";
            return "";
        }

        private double GetPointValue(ChartSeries s, Ohlcv p, string c, int i)
        {
            var comp = s.Components.FirstOrDefault(x => x.Name.Trim().Equals(c.Trim(), StringComparison.OrdinalIgnoreCase));
            if (comp != null)
            {
                var data = s.GetComponentData(comp.Name);
                if (data != null && i >= 0 && i < data.Length)
                    return data[i];
            }

            string seriesId = s.Id.ToLowerInvariant();
            if (seriesId == "price" || seriesId == "candles")
            {
                if (c.Contains("Body") || c.Contains("Close")) return p.Close;
                if (c.Contains("Upper") || c.Contains("High")) return p.High;
                if (c.Contains("Lower") || c.Contains("Low")) return p.Low;
                if (c.Contains("Open")) return p.Open;
            }
            if (seriesId == "volume") return p.Volume;
            return double.NaN;
        }
    }
}
