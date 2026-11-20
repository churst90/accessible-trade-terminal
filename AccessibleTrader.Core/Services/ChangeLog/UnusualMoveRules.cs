using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>One bar's move that was far outside the chart's own usual move.</summary>
    public sealed record UnusualMove(DateTime BarUtc, double Percent, double Times);

    /// <summary>
    /// "Unusual" against each asset's own normal swing over that much time, not a fixed percent
    /// (the design: a 2% Bitcoin day is not news, a 1% EUR/USD day is). The interval is the
    /// chart's own bar: the bar that just closed is compared with the median size of the
    /// <see cref="Lookback"/> close-to-close moves before it, and it is unusual at
    /// <see cref="Multiple"/> times that or more. The median, not the standard deviation, so one
    /// earlier spike does not hide the next.
    /// </summary>
    public static class UnusualMoveRules
    {
        public const int Lookback = 46;
        public const double Multiple = 5.0;

        /// <summary>Bars a caller needs: the forming bar, the closed bar, the close before it,
        /// and one more close behind each of the <see cref="Lookback"/> moves. Inside the 50 the
        /// browser-closed monitor already fetches to warm a narrated chart.</summary>
        public const int HistoryBars = Lookback + 3;

        /// <summary>
        /// The bar that closed when the newest (forming) bar opened, when its move was unusual;
        /// null otherwise, and null with too little history or a flat history.
        /// </summary>
        public static UnusualMove? AtClose(IReadOnlyList<Ohlcv> bars)
        {
            if (bars == null) return null;
            int closed = bars.Count - 2;
            if (closed < Lookback + 1) return null;

            double move = Move(bars, closed);
            if (!double.IsFinite(move)) return null;

            var usual = new double[Lookback];
            for (int k = 0; k < Lookback; k++)
            {
                double m = Move(bars, closed - Lookback + k);
                if (!double.IsFinite(m)) return null;
                usual[k] = Math.Abs(m);
            }
            Array.Sort(usual);
            double median = (usual[Lookback / 2 - 1] + usual[Lookback / 2]) / 2;
            if (median <= 0) return null;

            double times = Math.Abs(move) / median;
            if (times < Multiple) return null;
            return new UnusualMove(AsUtc(bars[closed].Date), move * 100, times);
        }

        /// <summary>"BTC/USDT 1h up 4.2% in one bar, 6 times its usual move."</summary>
        public static string Sentence(ChartIdentity identity, UnusualMove move)
        {
            double size = Math.Abs(move.Percent);
            string amount = size.ToString(size >= 1 ? "0.0" : "0.00", CultureInfo.InvariantCulture);
            // Rounded down, so it never claims a bigger surprise than there was.
            int times = (int)Math.Floor(move.Times);
            return $"{identity.Symbol} {identity.Timeframe} {(move.Percent > 0 ? "up" : "down")} {amount}% "
                 + $"in one bar, {times} times its usual move.";
        }

        private static double Move(IReadOnlyList<Ohlcv> bars, int i)
        {
            double before = bars[i - 1].Close;
            return before > 0 && double.IsFinite(before) ? bars[i].Close / before - 1 : double.NaN;
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
