using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Charts
{
    /// <summary>
    /// The one door to the charts built off the clock — Renko, range bars, line break and Kagi. Every
    /// reducer, background monitor, dialog and command that builds one asks here with the chart
    /// type, so a new type is added in this file and its builder, not at each caller.
    ///
    /// <para>
    /// The "box" every caller freezes is the type's own size: Renko's box, the range bars' range,
    /// Kagi's reversal amount.
    /// Line break has none; its frozen value is its line count, so the callers' "is a box
    /// resolved" test holds unchanged, and the build reads the count from the settings.
    /// </para>
    /// </summary>
    public static class BuiltCharts
    {
        /// <summary>The chart type's name, as the Chart type list says it.</summary>
        public static string Name(PriceChartStyle style) => style switch
        {
            PriceChartStyle.RangeBars => "Range bars",
            PriceChartStyle.LineBreak => "Line break",
            PriceChartStyle.Kagi => "Kagi",
            _ => "Renko",
        };

        /// <summary>What one drawn bar is called: "brick", "range bar", "block" or "Kagi line"
        /// (never plain "line", which is the Line chart).</summary>
        public static string Noun(PriceChartStyle style, int count = 1) => (style, count == 1) switch
        {
            (PriceChartStyle.RangeBars, true) => "range bar",
            (PriceChartStyle.RangeBars, false) => "range bars",
            (PriceChartStyle.LineBreak, true) => "block",
            (PriceChartStyle.LineBreak, false) => "blocks",
            (PriceChartStyle.Kagi, true) => "Kagi line",
            (PriceChartStyle.Kagi, false) => "Kagi lines",
            (_, true) => "brick",
            _ => "bricks",
        };

        /// <summary>
        /// True when <paramref name="a"/> and <paramref name="b"/> build the same <paramref name="style"/>
        /// chart: only that type's own fields count, so changing the range never rebuilds Renko.
        /// </summary>
        public static bool SameFor(PriceChartStyle style, RenkoSettings a, RenkoSettings b) => style switch
        {
            PriceChartStyle.RangeBars => a.RangeMode == b.RangeMode && a.RangeSize == b.RangeSize
                                         && a.RangeAtrLength == b.RangeAtrLength,
            PriceChartStyle.LineBreak => a.LineBreakLines == b.LineBreakLines,
            PriceChartStyle.Kagi => a.KagiMode == b.KagiMode && a.KagiSize == b.KagiSize
                                    && a.KagiAtrLength == b.KagiAtrLength,
            _ => a.Mode == b.Mode && a.BoxSize == b.BoxSize && a.AtrLength == b.AtrLength,
        };

        /// <summary>
        /// <paramref name="current"/> with <paramref name="style"/>'s own fields taken from
        /// <paramref name="typed"/>: RANGEBAR 500 changes the range and leaves the Renko box and the
        /// line count as the tab had them.
        /// </summary>
        public static RenkoSettings Merge(PriceChartStyle style, RenkoSettings current, RenkoSettings typed) => style switch
        {
            PriceChartStyle.RangeBars => current with
            {
                RangeMode = typed.RangeMode, RangeSize = typed.RangeSize, RangeAtrLength = typed.RangeAtrLength,
            },
            PriceChartStyle.LineBreak => current with { LineBreakLines = typed.LineBreakLines },
            PriceChartStyle.Kagi => current with
            {
                KagiMode = typed.KagiMode, KagiSize = typed.KagiSize, KagiAtrLength = typed.KagiAtrLength,
            },
            _ => current with { Mode = typed.Mode, BoxSize = typed.BoxSize, AtrLength = typed.AtrLength },
        };

        /// <summary>The size <paramref name="style"/> would freeze, or the refusal that stops it.</summary>
        public static bool TryResolveBox(
            PriceChartStyle style,
            IReadOnlyList<Ohlcv> bars,
            RenkoSettings settings,
            out double box,
            out string? refusal)
        {
            switch (style)
            {
                case PriceChartStyle.RangeBars:
                    return BrickChartBuilder.TryResolveSize(bars, settings.RangeMode, settings.RangeSize,
                        settings.RangeAtrLength, "Range bars", "range", out box, out refusal);
                case PriceChartStyle.Kagi:
                    return BrickChartBuilder.TryResolveSize(bars, settings.KagiMode, settings.KagiSize,
                        settings.KagiAtrLength, "Kagi", "reversal amount", out box, out refusal);
                case PriceChartStyle.LineBreak:
                    box = settings.LineBreakLines;
                    refusal = null;
                    if (settings.LineBreakLines is >= LineBreakBuilder.MinLines and <= LineBreakBuilder.MaxLines)
                        return true;
                    refusal = $"Line break takes {LineBreakBuilder.MinLines} to {LineBreakBuilder.MaxLines} lines.";
                    box = 0;
                    return false;
                default:
                    return BrickChartBuilder.TryResolveBox(bars, settings, out box, out refusal);
            }
        }

        /// <summary>Builds <paramref name="style"/>'s bars at the frozen <paramref name="box"/>.</summary>
        public static BrickBuildResult Build(
            PriceChartStyle style,
            IReadOnlyList<Ohlcv> bars,
            RenkoSettings settings,
            double box,
            TimeSpan barLength,
            int max = BrickChartBuilder.MaxBricks) => style switch
        {
            PriceChartStyle.RangeBars => RangeBarBuilder.Build(bars, box, barLength, max),
            PriceChartStyle.LineBreak => LineBreakBuilder.Build(bars, settings.LineBreakLines, barLength, max),
            PriceChartStyle.Kagi => KagiBuilder.Build(bars, box, barLength, max),
            _ => BrickChartBuilder.Build(bars, box, barLength, max),
        };

        /// <summary>
        /// Resolve the size when <paramref name="frozenBox"/> is none, then build. False, with no
        /// bars, when neither gives a usable size.
        /// </summary>
        public static bool TryBuild(
            PriceChartStyle style,
            IReadOnlyList<Ohlcv> bars,
            RenkoSettings settings,
            double frozenBox,
            TimeSpan barLength,
            out BrickBuildResult built,
            out double box)
        {
            built = default;
            box = frozenBox;
            if (!(box > 0) && !TryResolveBox(style, bars, settings, out box, out _))
            {
                box = 0;
                return false;
            }
            built = Build(style, bars, settings, box, barLength);
            return built.Ok;
        }

        /// <summary>
        /// A full user choice: resolve the size, build, then refuse an empty chart. Commands and
        /// dialogs call this BEFORE dispatching; the reducer never refuses.
        /// </summary>
        public static bool TryPlan(
            PriceChartStyle style,
            IReadOnlyList<Ohlcv> bars,
            RenkoSettings settings,
            TimeSpan barLength,
            out BrickChartPlan? plan,
            out string? refusal)
        {
            plan = null;
            if (!TryResolveBox(style, bars, settings, out double box, out refusal)) return false;

            var built = Build(style, bars, settings, box, barLength);
            if (!built.Ok)
            {
                refusal = built.Refusal;
                return false;
            }

            if (built.Bricks.Count == 0)
            {
                string size = SpeechPriceFormatter.FormatPrice(box);
                refusal = style switch
                {
                    PriceChartStyle.RangeBars =>
                        $"No range bars: price has not moved a range of {size} in the loaded bars.",
                    PriceChartStyle.LineBreak =>
                        "No line break blocks: no close in the loaded bars differs from the first.",
                    PriceChartStyle.Kagi =>
                        $"No Kagi lines: price has not moved a reversal amount of {size} in the loaded bars.",
                    _ => $"No Renko bricks: price has not moved one box of {size} in the loaded bars.",
                };
                return false;
            }

            plan = new BrickChartPlan(settings, box, built.Bricks, built.Spans);
            return true;
        }
    }
}
