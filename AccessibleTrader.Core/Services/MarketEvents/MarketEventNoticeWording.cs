using System.Globalization;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// One position a countdown should mention: the signed quantity, the venue as the user knows
    /// it ("Paper" for the simulator), and the price of a working stop order on that symbol when
    /// there is one. <see cref="Market"/> is null when the venue could not be classified; such a
    /// position is not used to gate an event and is not named on one.
    /// </summary>
    public sealed record MarketEventHeldPosition(
        string Symbol,
        double Quantity,
        string Venue,
        double? StopPrice,
        MarketType? Market);

    /// <summary>One live update from the focused chart, kept with its own time and symbol.</summary>
    public sealed record MarketEventPriceSample(DateTime AtUtc, string Symbol, double Close);

    /// <summary>
    /// The words for the live notices. Pure and static so every sentence in the handoff is
    /// pinned by a test, and so the service never composes grammar inline.
    /// </summary>
    public static class MarketEventNoticeWording
    {
        /// <summary>
        /// "CPI in 15 minutes." plus, when the user holds positions in a market the event
        /// touches, one sentence per position (at most three) and "And 2 more." for the rest.
        /// </summary>
        public static string Countdown(
            MarketEvent e,
            int leadMinutes,
            IReadOnlyList<MarketEventHeldPosition> positions)
        {
            string lead = leadMinutes == 1 ? "1 minute" : $"{leadMinutes} minutes";
            string expected = MarketEventSpeech.ExpectedShort(e);
            string text = expected.Length == 0
                ? $"{e.SpokenName} in {lead}."
                : $"{e.SpokenName}, {expected}, in {lead}.";
            if (positions.Count == 0) return text;

            var parts = new List<string>(Math.Min(positions.Count, 3) + 1) { text };
            foreach (var p in positions.Take(3))
                parts.Add(PositionSentence(p));
            if (positions.Count > 3)
                parts.Add($"And {positions.Count - 3} more.");
            return string.Join(" ", parts);
        }

        private static string PositionSentence(MarketEventHeldPosition p)
        {
            string side = p.Quantity >= 0 ? "long" : "short";
            string stop = p.StopPrice is double price
                ? ", stop at " + SpeechPriceFormatter.FormatPrice(price)
                : "";
            return $"You're {side} {QuantityText(p.Quantity)} {p.Symbol} on {p.Venue}{stop}.";
        }

        /// <summary>
        /// A quantity as the canonical formatter writes it, with the padding zeros trimmed:
        /// "0.0500" reads as "0.05" in a sentence, and "1,234" is left alone.
        /// </summary>
        internal static string QuantityText(double quantity)
        {
            string text = QuantityFormatter.Format(Math.Abs(quantity));
            if (text.Contains('.')) text = text.TrimEnd('0').TrimEnd('.');
            return text.Length == 0 ? "0" : text;
        }

        /// <summary>
        /// The release read-out, price reaction first. <paramref name="samples"/> is the focused
        /// chart's live updates. The reaction is measured from the newest close at or before T
        /// (within <see cref="BaselineReach"/>; failing that, the first update within 5 s after
        /// T) to the newest update in (T, T + 60 s]. With no update after T in that minute the
        /// feed was quiet, and the sentence is "CPI out." with nothing about price.
        ///
        /// <para>
        /// <paramref name="awaitingFigure"/> is true for a FRED release whose figure has not
        /// arrived, so the sentence ends "Waiting for the figure." When the event already carries
        /// its figures, the figure rides along: the compact actual/expected/surprise clause when
        /// Kalshi has an expected, otherwise the existing
        /// <see cref="MarketEventSpeech.FigureClause"/> — one wording, wherever the figure is said.
        /// </para>
        /// </summary>
        public static string ReadOut(
            MarketEvent e,
            IReadOnlyList<MarketEventPriceSample> samples,
            bool awaitingFigure)
        {
            var moment = AsUtc(e.MomentUtc);
            var ordered = samples.OrderBy(s => AsUtc(s.AtUtc)).ToList();

            // The price going into the release: the newest live close at or before T, if the
            // feed was live in the minutes before it. With none (the app was asleep, or the
            // chart was opened at the release), the first update within 5 s after T stands in.
            var start = ordered.LastOrDefault(s =>
                AsUtc(s.AtUtc) <= moment && AsUtc(s.AtUtc) >= moment - BaselineReach);
            start ??= ordered.FirstOrDefault(s =>
                AsUtc(s.AtUtc) > moment && AsUtc(s.AtUtc) < moment.AddSeconds(5));

            // A reaction needs a live update AFTER T inside the first minute, other than the
            // one standing in for the start. A quiet feed (a stock before the open) has none.
            var end = start == null ? null : ordered.LastOrDefault(s =>
                AsUtc(s.AtUtc) > moment && AsUtc(s.AtUtc) <= moment.AddSeconds(60)
                && !ReferenceEquals(s, start));

            string text = $"{e.SpokenName} out.";
            if (start != null && end != null
                && start.Close > 0 && double.IsFinite(start.Close)
                && end.Close > 0 && double.IsFinite(end.Close))
            {
                double percent = Math.Round(
                    (end.Close - start.Close) / start.Close * 100.0,
                    1, MidpointRounding.AwayFromZero);
                string symbol = string.IsNullOrWhiteSpace(start.Symbol) ? "Price" : start.Symbol;
                text += percent switch
                {
                    > 0 => $" {symbol} up {Percent(percent)} in the first minute.",
                    < 0 => $" {symbol} down {Percent(percent)} in the first minute.",
                    _ => $" {symbol} unchanged in the first minute.",
                };
            }

            string release = MarketEventSpeech.ReleaseFigureClause(e);
            string figure = release.Length > 0
                ? release + "."
                : e.Figures != null ? MarketEventSpeech.FigureClause(e) : "";
            if (figure.Length > 0) text += " " + figure;
            else if (awaitingFigure) text += " Waiting for the figure.";
            return text;
        }

        /// <summary>How far before T a live close may be and still be "the price going in".</summary>
        public static readonly TimeSpan BaselineReach = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The figure notice's sentence: the compact actual/expected/word when Kalshi has an
        /// expected figure, otherwise the existing figure clause (or "" if none).
        /// </summary>
        public static string Figure(MarketEvent e)
        {
            string release = MarketEventSpeech.ReleaseFigureClause(e);
            return release.Length > 0 ? release + "." : MarketEventSpeech.FigureClause(e);
        }

        private static string Percent(double value)
            => Math.Abs(value).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
