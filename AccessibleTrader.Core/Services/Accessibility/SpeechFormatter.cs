using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        private readonly IReadOnlyList<IComponentSpeechStrategy> _strategies;
        private readonly IComponentSpeechStrategy _fallback;

        public SpeechFormatter()
        {
            _strategies = new IComponentSpeechStrategy[]
            {
                new HiddenComponentStrategy(),
                new CloudComponentStrategy(),
                new PhaseNameStrategy(),
                new MarkerSignalStrategy(),
            };
            _fallback = new StandardTemplateStrategy();
        }

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

                msg = $"{trend}{typeStr}. Close {SpeechPriceFormatter.FormatPrice(pt.Close)}. Open {SpeechPriceFormatter.FormatPrice(pt.Open)}. " +
                      $"High {SpeechPriceFormatter.FormatPrice(pt.High)}. Low {SpeechPriceFormatter.FormatPrice(pt.Low)}. Volume {FormatVolume(pt.Volume)}. " +
                      $"Body {bodyPct:F0}%, Upper wick {upperPct:F0}%, Lower wick {lowerPct:F0}%.";
            }
            else if (summary && seriesId == "price")
            {
                var priceComp = series.Components.FirstOrDefault(c => c.IsVisible && !c.IsMuted);
                string lineType = priceComp != null ? FriendlyTypeName(priceComp.DisplayType) : "line";
                msg = $"{series.Name}. {lineType}. {SpeechPriceFormatter.FormatPrice(pt.Close)}.";
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
                dataMsg = $"Price {SpeechPriceFormatter.FormatPrice(bin.PriceLow)} to {SpeechPriceFormatter.FormatPrice(bin.PriceHigh)}, no data.";
            }
            else
            {
                var nodeType = ProfileBinClassifier.Classify(bin, allBins);
                string nodeLabel = ProfileBinClassifier.GetLabel(nodeType);

                // Percentage of total session volume for context.
                double totalVol = allBins.Where(b => !double.IsNaN(b.TotalVolume)).Sum(b => b.TotalVolume);
                double pct = totalVol > 0 ? bin.TotalVolume / totalVol * 100.0 : 0;

                if (bin.TpoLetters.Any())
                {
                    // TPO mode: report time periods (letters) rather than volume.
                    string letters = string.Join(" ", bin.TpoLetters);
                    string labelPart = string.IsNullOrEmpty(nodeLabel) ? "" : $", {nodeLabel}";
                    dataMsg = $"Price {SpeechPriceFormatter.FormatPrice(bin.PriceLow)} to {SpeechPriceFormatter.FormatPrice(bin.PriceHigh)}, " +
                              $"{bin.TpoPeriodCount:F0} {(bin.TpoPeriodCount == 1 ? "period" : "periods")}, " +
                              $"letters {letters}{labelPart}.";
                }
                else
                {
                    string labelPart = string.IsNullOrEmpty(nodeLabel) ? "" : $"{nodeLabel}, ";
                    dataMsg = $"Price {SpeechPriceFormatter.FormatPrice(bin.PriceLow)} to {SpeechPriceFormatter.FormatPrice(bin.PriceHigh)}, " +
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
                    dataMsg = $"peak at price {SpeechPriceFormatter.FormatPrice(peak.PriceMid)}{labelPart}, {FormatVolume(peak.TotalVolume)} contracts.";
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

                dataMsg = $"price {SpeechPriceFormatter.FormatPrice(bin.PriceLow)} to {SpeechPriceFormatter.FormatPrice(bin.PriceHigh)}" +
                          $"{labelPart}, " +
                          $"{FormatVolume(bin.TotalVolume)} contracts, " +
                          $"{pct:F1} percent.";
            }

            // Timestamp first, then prefix, then data — consistent with FormatPointFeedback ordering.
            string timestampPrefix = state.SpeakTimestamps ? timeLabel : "";
            return timestampPrefix + prefixMessage + dataMsg;
        }

        // ── Dispatcher ───────────────────────────────────────────────────────────

        private string FormatTemplateValue(ChartSeries series, ComponentConfig comp, Ohlcv pt, int dataIndex, bool readHeaders, string speechOrder)
        {
            try
            {
                double val = GetPointValue(series, pt, comp.Name, dataIndex);
                var ctx = new ComponentFormatContext(series, comp, pt, dataIndex, readHeaders, speechOrder, val);

                foreach (var strategy in _strategies)
                    if (strategy.CanHandle(ctx))
                        return strategy.Format(ctx);

                return _fallback.Format(ctx);
            }
            catch (Exception)
            {
                // Accessibility path: a malformed template or missing companion series must
                // not crash the speech pipeline -- a blind user listening for price updates
                // relies on a continuous output. "error" is a bounded fallback string so the
                // screen reader still has something to say. The trade-off is that a bug in
                // a template will be silent -- debug by enabling the same path under a test
                // harness, not by re-raising here.
                return $"{comp.DisplayName}: error";
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps ComponentDisplayType to a TTS-friendly lowercase string.
        /// Prevents internal enum names like "ZeroArea" being mangled by the speech engine.
        /// </summary>
        internal static string FriendlyTypeName(ComponentDisplayType dt) => dt switch
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

        internal static double GetPointValue(ChartSeries s, Ohlcv p, string c, int i)
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

    // ── Strategy registry ────────────────────────────────────────────────────────

    /// <summary>
    /// Per-component speech strategy. Strategies are consulted in registration
    /// order; the first <see cref="CanHandle"/> match owns the component.
    /// Adding a new DisplayType-specific speech path means adding a strategy
    /// class here, not editing <see cref="SpeechFormatter.FormatTemplateValue"/>.
    /// </summary>
    internal interface IComponentSpeechStrategy
    {
        bool CanHandle(ComponentFormatContext ctx);
        string Format(ComponentFormatContext ctx);
    }

    internal readonly record struct ComponentFormatContext(
        ChartSeries Series,
        ComponentConfig Comp,
        Ohlcv Pt,
        int DataIndex,
        bool ReadHeaders,
        string SpeechOrder,
        double Value);

    /// <summary>Announces hidden components so the user still knows where Y-navigation landed.</summary>
    internal sealed class HiddenComponentStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => !ctx.Comp.IsVisible;
        public string Format(ComponentFormatContext ctx) => $"{ctx.Comp.DisplayName}: hidden";
    }

    /// <summary>
    /// Cloud components (Ichimoku-style): announce bullish/bearish direction,
    /// cloud width, and whether price is inside / above / below the cloud.
    /// </summary>
    internal sealed class CloudComponentStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => ctx.Comp.DisplayType == ComponentDisplayType.Cloud;

        public string Format(ComponentFormatContext ctx)
        {
            double signedWidth = ctx.Value;
            if (double.IsNaN(signedWidth))
                return $"{ctx.Comp.DisplayName}: no data";

            bool bullish = signedWidth >= 0;
            string direction = bullish ? "bullish" : "bearish";
            double absWidth = Math.Abs(signedWidth);

            string pricePosition = "";
            if (!string.IsNullOrEmpty(ctx.Comp.UpperComponentName) && !string.IsNullOrEmpty(ctx.Comp.LowerComponentName))
            {
                var upperData = ctx.Series.GetComponentData(ctx.Comp.UpperComponentName);
                var lowerData = ctx.Series.GetComponentData(ctx.Comp.LowerComponentName);
                if (upperData.Length > ctx.DataIndex && lowerData.Length > ctx.DataIndex)
                {
                    double u = upperData[ctx.DataIndex];
                    double l = lowerData[ctx.DataIndex];
                    if (!double.IsNaN(u) && !double.IsNaN(l))
                    {
                        double hi = Math.Max(u, l);
                        double lo = Math.Min(u, l);
                        double close = ctx.Pt.Close;
                        if (close >= lo && close <= hi)
                            pricePosition = " Price inside cloud.";
                        else if (close > hi)
                            pricePosition = " Price above cloud.";
                        else
                            pricePosition = " Price below cloud.";
                    }
                }
            }

            return $"{ctx.Comp.DisplayName}. {direction}, width {absWidth:F2}.{pricePosition}";
        }
    }

    /// <summary>
    /// CandleColor components carry a sentiment-phase index (0–10). Converts
    /// the raw numeric value to its phase name so the user hears "Neutral"
    /// instead of "CandleColor 5".
    /// </summary>
    internal sealed class PhaseNameStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => ctx.Comp.DisplayType == ComponentDisplayType.CandleColor;

        public string Format(ComponentFormatContext ctx)
        {
            if (double.IsNaN(ctx.Value)) return $"{ctx.Comp.DisplayName}: warming up.";
            int phaseIdx = Math.Clamp((int)Math.Round(ctx.Value), 0, AudioConstants.PhaseNames.Length - 1);
            return $"{ctx.Comp.DisplayName}. {AudioConstants.PhaseNames[phaseIdx]}.";
        }
    }

    /// <summary>
    /// Marker components (Dot, Diamond, Cross, Arrow, Triangle*, Square) with a
    /// configured <see cref="ComponentConfig.SignalSpeechTemplate"/>. Expands the
    /// template when the signal fires; announces "no data" when it does not.
    /// </summary>
    internal sealed class MarkerSignalStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) =>
            ctx.Comp.SignalSpeechTemplate != null
            && AudioConstants.MarkerDisplayTypes.Contains(ctx.Comp.DisplayType);

        public string Format(ComponentFormatContext ctx)
        {
            if (double.IsNaN(ctx.Value))
                return $"{ctx.Comp.DisplayName}: no data";  // user needs to know where they are
            string priceStr = ctx.Value.ToString("F0");
            return ctx.Comp.SignalSpeechTemplate!
                .Replace("{price}", priceStr)
                .Replace("{name}", ctx.Comp.DisplayName);
        }
    }

    /// <summary>
    /// Fallback: standard template processing with token substitution.
    /// Tokens: {name}, {type}, {value}, {value:Fn}, {trend}, {zone}, {gradient_speech}.
    /// Provider metadata supplies the template via <see cref="ComponentConfig.SpeechTemplate"/>;
    /// the default is "{name}. {type}. {value}.".
    /// </summary>
    internal sealed class StandardTemplateStrategy : IComponentSpeechStrategy
    {
        public bool CanHandle(ComponentFormatContext ctx) => true;

        public string Format(ComponentFormatContext ctx)
        {
            double val = ctx.Value;
            var comp = ctx.Comp;
            var series = ctx.Series;

            // Price-family series (candles / price line) need magnitude-aware
            // precision so sub-dollar assets like KAS don't collapse to "0.04".
            string sId = series.Id.ToLowerInvariant();
            bool isPriceSeries = sId == "price" || sId == "candles";
            string valF2 = double.IsNaN(val)
                ? "no data"
                : (isPriceSeries ? SpeechPriceFormatter.FormatPrice(val) : val.ToString("F2"));

            if (!ctx.ReadHeaders || ctx.SpeechOrder == "ValueOnly")
                return double.IsNaN(val) ? "no data" : valF2;

            // Template priority: provider metadata (comp.SpeechTemplate) → generic fallback.
            string tmpl = string.IsNullOrEmpty(comp.SpeechTemplate) ? "{name}. {type}. {value}." : comp.SpeechTemplate;

            string trend = ctx.Pt.Close >= ctx.Pt.Open ? "Bullish" : "Bearish";
            string zone = ResolveZone(series, val);
            string gradientSpeech = ResolveGradientSpeech(ctx, tmpl);

            string result = tmpl
                .Replace("{gradient_speech}", gradientSpeech)
                .Replace("{name}", comp.DisplayName)
                .Replace("{type}", SpeechFormatter.FriendlyTypeName(comp.DisplayType));

            // Generic {value:Fn} format handler — catches F0, F1, F2, F3, ... so providers
            // can use any precision without adding a new Replace() line here.
            result = Regex.Replace(
                result,
                @"\{value:F(\d+)\}",
                m =>
                {
                    int digits = int.Parse(m.Groups[1].Value);
                    return double.IsNaN(val) ? "no data" : val.ToString("F" + digits);
                });

            return result
                .Replace("{value}", valF2)
                .Replace("{trend}", trend)
                .Replace("{zone}", zone);
        }

        private static string ResolveZone(ChartSeries series, double val)
        {
            foreach (var lc in series.Config.Levels)
            {
                if (!lc.IsVisible) continue;
                if (lc.Name.Contains("Overbought", StringComparison.OrdinalIgnoreCase) ||
                    lc.Name.Contains("Extreme OB", StringComparison.OrdinalIgnoreCase))
                {
                    if (val >= lc.Value) return "Overbought";
                }
                else if (lc.Name.Contains("Oversold", StringComparison.OrdinalIgnoreCase) ||
                         lc.Name.Contains("Extreme OS", StringComparison.OrdinalIgnoreCase))
                {
                    if (val <= lc.Value) return "Oversold";
                }
            }
            return "";
        }

        private static string ResolveGradientSpeech(ComponentFormatContext ctx, string tmpl)
        {
            if (!ctx.Comp.UsesGradientSpeech || !tmpl.Contains("{gradient_speech}"))
                return "";

            double oscillatorVal = SpeechFormatter.GetPointValue(ctx.Series, ctx.Pt, ctx.Comp.Name + "_color", ctx.DataIndex);
            if (double.IsNaN(oscillatorVal)) return "no data";

            string intensity = oscillatorVal > 60.0  ? "strong bullish momentum"
                             : oscillatorVal > 20.0  ? "moderate bullish momentum"
                             : oscillatorVal >= -20.0 ? "neutral momentum"
                             : oscillatorVal >= -60.0 ? "moderate bearish momentum"
                             :                          "strong bearish momentum";
            return $"{intensity}, {oscillatorVal:F1}";
        }
    }
}
