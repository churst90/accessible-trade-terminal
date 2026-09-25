using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <see cref="LevelProvenanceService"/> — the level report (which levels near price have held,
    /// and how often). It had NO test file until A2m: the report modal's bUnit harness substitutes
    /// the interface, so nothing ever ran the real service. Two of A2m's survivors lived here, and
    /// both are statements a trader acts on with no visual to check them against:
    /// <list type="bullet">
    ///   <item>which SIDE of price a level is on ("below: …" / "above: …", M38);</item>
    ///   <item>whether "prior day high" is the prior day's, or today's — a lookahead that makes the
    ///         level report the high the session has not finished making yet (M39).</item>
    /// </list>
    /// </summary>
    public class LevelProvenanceServiceTests
    {
        private static LevelProvenanceService Service()
        {
            var analyzer = new LevelRespectAnalyzer();
            var resampler = new ResamplerService();
            return new LevelProvenanceService(analyzer, new MaRespectRanker(analyzer, resampler), resampler);
        }

        private static RespectStats Reliable(string label, double value) =>
            new(label, label, LineKind.Horizontal,
                Touches: 10, Holds: 7, HoldRate: 0.7, MeanReactionAtr: 1.5, MedianReactionAtr: 1.5,
                SupportHolds: 4, ResistanceHolds: 3, LastTouchTime: null, BarsSinceLastTouch: 3,
                CurrentValue: value, DistanceAtr: 2.0, TouchDetail: Array.Empty<LineTouch>());

        [Fact]
        public void ALevelIsNarratedOnTheSideOfPriceItIsActuallyOn()
        {
            var bars = new List<Ohlcv> { new(new DateTime(2026, 1, 1), 60000, 60100, 59900, 60000, 1) };
            var ranked = new[] { Reliable("200 EMA", 58000), Reliable("prior 1w high", 64000) };

            string text = Service().Narrate(ranked, bars);

            Assert.Contains("below: 200 EMA at 58,000", text);
            Assert.Contains("above: prior 1w high at 64,000", text);
            Assert.DoesNotContain("below: prior 1w high", text);
            Assert.DoesNotContain("above: 200 EMA", text);
        }

        /// <summary>
        /// Ten days of hourly bars, each day's high printed at noon. On the last bar of day 9 the
        /// "prior 1d high" is day 8's (185) — never day 9's own (195), which is exactly what reading
        /// the current, still-open bucket reports.
        /// </summary>
        [Fact]
        public void ThePriorDayHighIsYesterdays_NotTodaysStillFormingHigh()
        {
            var bars = new List<Ohlcv>();
            var start = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
            for (int d = 0; d < 10; d++)
            {
                double basePx = 100 + d * 10;
                for (int h = 0; h < 24; h++)
                {
                    double high = h == 12 ? basePx + 5 : basePx + 1;
                    bars.Add(new Ohlcv(start.AddDays(d).AddHours(h), basePx, high, basePx - 1, basePx, 100));
                }
            }

            var stats = Service().Analyze(bars, "1h", nearAtr: 1e9);

            var priorHigh = Assert.Single(stats, s => s.Id == "PRIOR:1d:HIGH");
            Assert.Equal(185.0, priorHigh.CurrentValue, 6);
            var priorLow = Assert.Single(stats, s => s.Id == "PRIOR:1d:LOW");
            Assert.Equal(179.0, priorLow.CurrentValue, 6);
        }
    }
}
