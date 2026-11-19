using AccessibleTrader.Core.Services.Charts;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// The one place the brick chart's sentences are built, so the toolbar, the Go bar, the
    /// settings dialog and the state-change announcer say the same words.
    /// </summary>
    public static class BrickChartSpeech
    {
        public const string Already = "Already a Renko chart.";
        public const string SettingsOnlyOnRenko =
            "Brick settings are for Renko, range bars and line break charts. Choose one first.";
        public const string BadArgument =
            "Renko takes a box size, like RENKO 500, or ATR and a length, like RENKO ATR 14.";
        public const string RangeBadArgument =
            "Range bars take a range, like RANGEBAR 500, or ATR and a length, like RANGEBAR ATR 14.";
        public const string LineBreakBadArgument =
            "Line break takes a number of lines from 1 to 50, like LINEBREAK 3.";

        /// <summary>"Already a Renko chart.", "Already a Range bars chart." ...</summary>
        public static string AlreadyFor(PriceChartStyle style) =>
            style == PriceChartStyle.Renko ? Already
                : style == PriceChartStyle.RangeBars ? "Already a range bar chart."
                : "Already a line break chart.";

        /// <summary>The Go bar's refusal for a bad argument to <paramref name="style"/>'s word.</summary>
        public static string BadArgumentFor(PriceChartStyle style) => style switch
        {
            PriceChartStyle.RangeBars => RangeBadArgument,
            PriceChartStyle.LineBreak => LineBreakBadArgument,
            _ => BadArgument,
        };

        /// <summary>"Renko chart, box 512.30 from ATR 14." or "Renko chart, fixed box 500.00.";
        /// "Range bars chart, range 512.30 from ATR 14."; "Line break chart, 3 lines." The size is
        /// the frozen one the chart actually uses.</summary>
        public static string ChangeSentence(WorkspaceState state)
        {
            var style = state.ChartStyle.IsBrickStyle() ? state.ChartStyle : PriceChartStyle.Renko;
            string chart = $"{BuiltCharts.Name(style)} chart";
            if (style == PriceChartStyle.LineBreak)
            {
                int lines = state.Renko.LineBreakLines;
                return $"{chart}, {lines} line{(lines == 1 ? "" : "s")}.";
            }

            double box = state.BrickBoxSize;
            if (!(box > 0))
                return chart + ".";

            string price = SpeechPriceFormatter.FormatPrice(box);
            string unit = style == PriceChartStyle.RangeBars ? "range" : "box";
            var (mode, atr) = style == PriceChartStyle.RangeBars
                ? (state.Renko.RangeMode, state.Renko.RangeAtrLength)
                : (state.Renko.Mode, state.Renko.AtrLength);
            return mode == RenkoBoxMode.Fixed
                ? $"{chart}, fixed {unit} {price}."
                : $"{chart}, {unit} {price} from ATR {atr}.";
        }

        /// <summary>The brick at <paramref name="index"/>, when this chart has one.</summary>
        public static bool TryGetBrick(WorkspaceState state, int index, out Ohlcv brick, out BrickSpan span)
        {
            brick = default;
            span = default;
            if (!state.ChartStyle.IsBrickStyle()) return false;
            if (state.Data == null || index < 0 || index >= state.Data.Count) return false;
            if (index >= state.BrickSpans.Length) return false;

            brick = state.Data[index];
            span = state.BrickSpans[index];
            return true;
        }

        /// <summary>The real window a brick formed in, in the shared time formatter's words.</summary>
        public static string Window(WorkspaceState state, BrickSpan span)
            => SpeechTimeFormatter.FormatBrickRange(
                span.Start, span.End, PlaybackNarration.BarSeconds(state));

        /// <summary>"Up brick, 95,000.00 to 95,500.00" — direction and the two prices as drawn
        /// ("Up block" on line break).</summary>
        public static string BrickSummary(Ohlcv brick, BrickSpan span, PriceChartStyle style = PriceChartStyle.Renko)
            => $"{(span.Up ? "Up" : "Down")} {BuiltCharts.Noun(style)}, "
             + $"{SpeechPriceFormatter.FormatPrice(brick.Open)} to "
             + $"{SpeechPriceFormatter.FormatPrice(brick.Close)}";

        /// <summary>The arrow-key sentence for a brick: its direction, prices and real window.</summary>
        public static string Readout(WorkspaceState state, Ohlcv brick, BrickSpan span)
            => $"{BrickSummary(brick, span, state.ChartStyle)}. {Window(state, span)}.";

        /// <summary>Validates a choice against the real bars and returns the bricks it would make.</summary>
        public static bool TryPlan(
            WorkspaceState state,
            RenkoSettings settings,
            out BrickChartPlan? plan,
            out string? refusal,
            PriceChartStyle style = PriceChartStyle.Renko)
            => BuiltCharts.TryPlan(
                style,
                state.MarketBars,
                settings,
                BrickChartBuilder.BarLength(state.Identity, state.MarketBars),
                out plan,
                out refusal);
    }
}
