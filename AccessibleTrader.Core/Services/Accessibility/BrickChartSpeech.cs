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
            "Brick settings are for Renko, range bars, line break and Kagi charts. Choose one first.";
        public const string BadArgument =
            "Renko takes a box size, like RENKO 500, or ATR and a length, like RENKO ATR 14.";
        public const string RangeBadArgument =
            "Range bars take a range, like RANGEBAR 500, or ATR and a length, like RANGEBAR ATR 14.";
        public const string LineBreakBadArgument =
            "Line break takes a number of lines from 1 to 50, like LINEBREAK 3.";
        public const string KagiBadArgument =
            "Kagi takes a reversal amount, like KAGI 500, a percentage, like KAGI 2%, or ATR and a length, like KAGI ATR 14.";

        /// <summary>"Already a Renko chart.", "Already a Range bars chart." ...</summary>
        public static string AlreadyFor(PriceChartStyle style) =>
            style == PriceChartStyle.Renko ? Already
                : style == PriceChartStyle.RangeBars ? "Already a range bar chart."
                : style == PriceChartStyle.Kagi ? "Already a Kagi chart."
                : "Already a line break chart.";

        /// <summary>The Go bar's refusal for a bad argument to <paramref name="style"/>'s word.</summary>
        public static string BadArgumentFor(PriceChartStyle style) => style switch
        {
            PriceChartStyle.RangeBars => RangeBadArgument,
            PriceChartStyle.LineBreak => LineBreakBadArgument,
            PriceChartStyle.Kagi => KagiBadArgument,
            _ => BadArgument,
        };

        /// <summary>A reversal percentage as said: "2", "0.5", "1.25".</summary>
        public static string Percent(double percent) =>
            percent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>"Renko chart, box 512.30 from ATR 14." or "Renko chart, fixed box 500.00.";
        /// "Range bars chart, range 512.30 from ATR 14."; "Line break chart, 3 lines." The size is
        /// the frozen one the chart actually uses. "Kagi chart, reversal amount 512.30 from ATR 14."
        /// "Kagi chart, reversal 2 percent."</summary>
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

            // "Kagi chart, reversal 2 percent." A percentage is not a price.
            if (style == PriceChartStyle.Kagi && state.Renko.KagiMode == RenkoBoxMode.Percent)
                return $"{chart}, reversal {Percent(box)} percent.";

            string price = SpeechPriceFormatter.FormatPrice(box);
            string unit = style switch
            {
                PriceChartStyle.RangeBars => "range",
                PriceChartStyle.Kagi => "reversal amount",
                _ => "box",
            };
            var (mode, atr) = style switch
            {
                PriceChartStyle.RangeBars => (state.Renko.RangeMode, state.Renko.RangeAtrLength),
                PriceChartStyle.Kagi => (state.Renko.KagiMode, state.Renko.KagiAtrLength),
                _ => (state.Renko.Mode, state.Renko.AtrLength),
            };
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
        /// ("Up block" on line break). On Kagi, given the lines and the index, the line's
        /// thickness follows its name: "Up Kagi line, turns thick at 95,200.00, 95,000.00 to
        /// 95,500.00", as the chart draws it.</summary>
        public static string BrickSummary(Ohlcv brick, BrickSpan span, PriceChartStyle style = PriceChartStyle.Renko,
            IReadOnlyList<Ohlcv>? drawn = null, int index = -1)
            => $"{(span.Up ? "Up" : "Down")} {BuiltCharts.Noun(style)}, "
             + (style == PriceChartStyle.Kagi && drawn != null && index >= 0 && index < drawn.Count
                 ? KagiLines.Words(KagiLines.At(drawn, index)) + ", "
                 : "")
             + $"{SpeechPriceFormatter.FormatPrice(brick.Open)} to "
             + $"{SpeechPriceFormatter.FormatPrice(brick.Close)}";

        /// <summary>
        /// "Kagi turns thick at 95,200.00." when the newest line, growing in place, has just passed
        /// the shoulder (or "thin", the waist) that it had not passed in <paramref name="before"/>,
        /// the same line's previous snapshot. Null when nothing turned, or when
        /// <paramref name="before"/> is another line.
        /// </summary>
        public static string? KagiTurnNews(IReadOnlyList<Ohlcv>? lines, Ohlcv? before)
        {
            if (lines == null || lines.Count == 0 || before is not { } prev) return null;
            var now = lines[^1];
            if (prev.Date != now.Date || prev.Open != now.Open) return null;
            var look = KagiLines.At(lines, lines.Count - 1);
            if (!look.Turned) return null;
            bool passedBefore = look.ThickAtEnd ? prev.Close > look.TurnAt : prev.Close < look.TurnAt;
            if (passedBefore) return null;
            return $"Kagi turns {(look.ThickAtEnd ? "thick" : "thin")} at {SpeechPriceFormatter.FormatPrice(look.TurnAt)}.";
        }

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
