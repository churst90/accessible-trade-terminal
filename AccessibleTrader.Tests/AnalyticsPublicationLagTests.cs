using System.Reflection;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A whole-day statistic cannot be read on the day it describes.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Active addresses for 2026-01-05 counts every address that transacted between 00:00 and
    /// 23:59 on the 5th, so it is not knowable until the 5th is over. Six providers stamped it
    /// at <b>00:00 UTC on the 5th</b>, and because <c>CrossSeriesForwardFill.Fill</c> admits
    /// ties (<c>ticks[i].Ts &lt;= barTs</c>), an indicator on a daily chart read day D's
    /// full-day value at day D's OPEN.
    /// </para>
    ///
    /// <para>
    /// One bar of look-ahead is the <b>whole edge</b> for a mean-reversion gate: "buy when
    /// today's active addresses are low" is not a rule anyone can trade, because on the morning
    /// of the 5th nobody knows what the 5th's count will be. Every backtest built on such a
    /// gate measured a decision that could not have been made.
    /// </para>
    ///
    /// <para>
    /// The defect was filed against CoinMetrics and found unfiled in five more — Glassnode,
    /// BGeometrics, DefiLlama, Wikipedia pageviews and Alternative.me. Six independent copies
    /// of one off-by-one is the signature of a rule that lives nowhere, which is what
    /// <see cref="AnalyticsPublicationLag"/> is for.
    /// </para>
    /// </summary>
    public class AnalyticsPublicationLagTests
    {
        private static readonly DateTime Jan5 =
            new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void A_whole_day_metric_becomes_readable_on_the_NEXT_day()
        {
            Assert.Equal(new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
                         AnalyticsPublicationLag.ForWholeDayMetric(Jan5));
        }

        [Fact]
        public void The_time_of_day_on_the_input_is_discarded()
        {
            // Providers hand over anything from a midnight stamp to a mid-day one. What the
            // row means is "the 5th", and the answer must not depend on how the row was
            // formatted.
            var midday = new DateTime(2026, 1, 5, 13, 47, 12, DateTimeKind.Utc);

            Assert.Equal(AnalyticsPublicationLag.ForWholeDayMetric(Jan5),
                         AnalyticsPublicationLag.ForWholeDayMetric(midday));
        }

        [Fact]
        public void The_result_is_UTC_whatever_the_input_kind_was()
        {
            // An Unspecified DateTime compared against a UTC bar date is a silent local-time
            // shift, which on this path would be a look-ahead of up to a day in either
            // direction depending on the machine.
            var unspecified = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Unspecified);

            Assert.Equal(DateTimeKind.Utc,
                         AnalyticsPublicationLag.ForWholeDayMetric(unspecified).Kind);
        }

        [Fact]
        public void The_unix_overload_agrees_with_the_DateTime_one()
        {
            long jan5 = new DateTimeOffset(Jan5).ToUnixTimeSeconds();

            Assert.Equal(
                new DateTimeOffset(AnalyticsPublicationLag.ForWholeDayMetric(Jan5)).ToUnixTimeSeconds(),
                AnalyticsPublicationLag.ForWholeDayMetric(jan5));
        }

        [Theory]
        [InlineData(0, 6)]
        [InlineData(1, 7)]
        [InlineData(2, 8)]
        public void An_extra_lag_stacks_on_top(int extra, int expectedDay)
        {
            // Wikipedia pageviews are republished for a day or two, so their count for day D
            // is not final when D ends.
            Assert.Equal(new DateTime(2026, 1, expectedDay, 0, 0, 0, DateTimeKind.Utc),
                         AnalyticsPublicationLag.ForWholeDayMetric(Jan5, extra));
        }

        [Fact]
        public void A_negative_extra_lag_cannot_pull_the_stamp_back_into_look_ahead()
        {
            // Defensive: the whole point is that the value is not readable earlier, so no
            // argument may move it earlier.
            Assert.Equal(AnalyticsPublicationLag.ForWholeDayMetric(Jan5),
                         AnalyticsPublicationLag.ForWholeDayMetric(Jan5, extraDays: -5));
        }

        // ── The rule is actually USED ────────────────────────────────────────

        /// <summary>
        /// The six providers that carried the defect, by source scan.
        ///
        /// <para>A helper nobody calls fixes nothing, and this is the exact shape the repo has
        /// been bitten by before — a guard that tests the FUNCTION while the call sites go
        /// their own way. Their fetch paths need live HTTP to exercise, so the call sites are
        /// checked the only way they can be here: by looking.</para>
        /// </summary>
        [Theory]
        [InlineData("CoinMetrics", "CoinMetricsProvider.cs")]
        [InlineData("Glassnode", "GlassnodeProvider.cs")]
        [InlineData("BGeometrics", "BGeometricsProvider.cs")]
        [InlineData("DefiLlama", "DefiLlamaProvider.cs")]
        [InlineData("WikipediaPageviews", "WikipediaPageviewsProvider.cs")]
        [InlineData("AlternativeMe", "AlternativeMeProvider.cs")]
        public void EveryDailyAnalyticsProviderStampsThroughTheSharedRule(string plugin, string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);

            var path = Path.Combine(dir!.FullName, "Plugins", "Analytics",
                $"AccessibleTrader.Plugins.{plugin}", file);
            Assert.True(File.Exists(path), $"{path} not found — the scan lost its target.");

            Assert.Contains("AnalyticsPublicationLag.ForWholeDayMetric", File.ReadAllText(path),
                StringComparison.Ordinal);
        }
    }
}
