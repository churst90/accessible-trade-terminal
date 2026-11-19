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
            "Brick settings are for Renko charts. Choose RENKO first.";
        public const string BadArgument =
            "Renko takes a box size, like RENKO 500, or ATR and a length, like RENKO ATR 14.";

        /// <summary>"Renko chart, box 512.30 from ATR 14." or "Renko chart, fixed box 500.00."
        /// The box is the frozen one the chart actually uses.</summary>
        public static string ChangeSentence(WorkspaceState state)
        {
            double box = state.BrickBoxSize;
            if (!(box > 0))
                return "Renko chart.";

            string price = SpeechPriceFormatter.FormatPrice(box);
            return state.Renko.Mode == RenkoBoxMode.Fixed
                ? $"Renko chart, fixed box {price}."
                : $"Renko chart, box {price} from ATR {state.Renko.AtrLength}.";
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

        /// <summary>"Up brick, 95,000.00 to 95,500.00" — direction and the two prices as drawn.</summary>
        public static string BrickSummary(Ohlcv brick, BrickSpan span)
            => $"{(span.Up ? "Up" : "Down")} brick, "
             + $"{SpeechPriceFormatter.FormatPrice(brick.Open)} to "
             + $"{SpeechPriceFormatter.FormatPrice(brick.Close)}";

        /// <summary>The arrow-key sentence for a brick: its direction, prices and real window.</summary>
        public static string Readout(WorkspaceState state, Ohlcv brick, BrickSpan span)
            => $"{BrickSummary(brick, span)}. {Window(state, span)}.";

        /// <summary>Validates a choice against the real bars and returns the bricks it would make.</summary>
        public static bool TryPlan(
            WorkspaceState state,
            RenkoSettings settings,
            out BrickChartPlan? plan,
            out string? refusal)
            => BrickChartBuilder.TryPlan(
                state.MarketBars,
                settings,
                BrickChartBuilder.BarLength(state.Identity, state.MarketBars),
                out plan,
                out refusal);
    }
}
