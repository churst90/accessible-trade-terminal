namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// <b>When a whole-day statistic first becomes readable.</b>
    ///
    /// <para>
    /// ── The rule, and why it needs one shared home ─────────────────────────────
    /// A daily on-chain or sentiment metric describes a whole day. Active addresses for
    /// 2026-01-05 counts every address that transacted between 00:00 and 23:59 on the 5th, so
    /// it is not knowable until the 5th is over — and several providers stamped it at
    /// <b>00:00 UTC on the 5th</b>. Because <c>CrossSeriesForwardFill.Fill</c> admits ties
    /// (<c>ticks[i].Ts &lt;= barTs</c>), an indicator on a daily chart then read day D's
    /// full-day value at day D's OPEN.
    /// </para>
    ///
    /// <para>
    /// One bar of look-ahead sounds small. It is the <b>whole edge</b> for a mean-reversion
    /// gate: "buy when today's active addresses are low" is a rule you cannot trade, because
    /// on the morning of the 5th nobody knows what the 5th's count will be. Every backtest
    /// built on such a gate is measuring a decision that could not have been made.
    /// </para>
    ///
    /// <para>
    /// The defect was already filed against CoinMetrics and found unfiled in five more —
    /// Glassnode, BGeometrics, DefiLlama, Wikipedia pageviews and Alternative.me. Six
    /// independent copies of the same off-by-one is the signature of a rule that lives nowhere,
    /// so it lives here now.
    /// </para>
    /// </summary>
    public static class AnalyticsPublicationLag
    {
        /// <summary>
        /// The earliest bar that may honestly carry a statistic covering the whole of
        /// <paramref name="metricDay"/>.
        ///
        /// <para>One period after the day it describes: the value is complete at that day's
        /// close, so the next bar is the first that could have read it.</para>
        /// </summary>
        /// <param name="metricDay">
        /// The day the metric COVERS — the date on the provider's row, not the date it was
        /// downloaded.
        /// </param>
        public static DateTime ForWholeDayMetric(DateTime metricDay)
            => DateTime.SpecifyKind(metricDay.Date, DateTimeKind.Utc).AddDays(1);

        /// <summary>
        /// <see cref="ForWholeDayMetric(DateTime)"/> for a Unix-second timestamp, returning
        /// Unix seconds. Convenience for the providers whose rows carry epoch stamps.
        /// </summary>
        public static long ForWholeDayMetric(long unixSeconds)
            => new DateTimeOffset(
                   ForWholeDayMetric(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime))
               .ToUnixTimeSeconds();

        /// <summary>
        /// A statistic that is not final until some days after the period it covers — Wikipedia
        /// pageviews are republished for a day or two, and revision-bearing series behave the
        /// same way.
        /// </summary>
        public static DateTime ForWholeDayMetric(DateTime metricDay, int extraDays)
            => ForWholeDayMetric(metricDay).AddDays(Math.Max(0, extraDays));
    }
}
