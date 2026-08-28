using System.Globalization;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Applies a <see cref="MarketDataRequest"/>'s <c>Since</c>/<c>Until</c> window to bars that
    /// came back from an endpoint which does not accept one.
    ///
    /// <para>
    /// ── Why this exists ────────────────────────────────────────────────────────
    /// Several venues expose only "the most recent N candles" and take no date range. The
    /// providers in front of them dropped <c>Since</c>/<c>Until</c> on the floor and then
    /// trimmed with <c>Skip(count - Limit)</c>, which keeps the NEWEST bars — so a chart
    /// scrolled back to 2019, or any date-ranged backtest, was quietly served this morning's
    /// data wearing 2019's request. Nothing was empty and nothing was flagged; the numbers were
    /// simply from the wrong century of the series.
    /// </para>
    ///
    /// <para>
    /// ── What it does, and the half that matters ────────────────────────────────
    /// It filters to the requested window and trims to <c>Limit</c>. And when the window falls
    /// entirely outside what the venue returned, it <b>says so and names the dates that ARE
    /// available</b>, because that is the only fact that lets someone pick a window that works.
    /// A blank chart and a flat one are the same picture for a user who cannot see the axis, so
    /// "returns empty" was never an acceptable answer even when the emptiness is honest. This is
    /// the same shape <c>MempoolProvider</c> settled on.
    /// </para>
    /// </summary>
    public static class WindowedBars
    {
        /// <summary>
        /// Filters <paramref name="bars"/> (and the parallel <paramref name="volumes"/>) to the
        /// request's window, then to its limit.
        /// </summary>
        /// <param name="report">
        /// Called with a spoken-quality explanation when the window asked for lies outside the
        /// data the venue returned. Not called when the venue returned nothing at all — that is
        /// a fetch failure and the caller has already reported it.
        /// </param>
        public static (List<Ohlcv> Bars, List<(long Timestamp, double Volume)> Volumes) Apply(
            MarketDataRequest request,
            List<Ohlcv> bars,
            List<(long Timestamp, double Volume)> volumes,
            string venueName,
            string symbol,
            Action<string> report)
        {
            if (bars.Count == 0) return (bars, volumes);

            // Bars and volumes are built in lockstep by every caller, so they are filtered as
            // pairs rather than independently — filtering them separately is how a series and
            // its volume overlay drift apart by one bar.
            bool paired = volumes.Count == bars.Count;
            var oldest = bars[0].Date;
            var newest = bars[^1].Date;

            DateTime? since = request.Since.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime
                : null;
            DateTime? until = request.Until.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime
                : null;

            if (since.HasValue || until.HasValue)
            {
                var keptBars = new List<Ohlcv>(bars.Count);
                var keptVols = new List<(long, double)>(bars.Count);
                for (int i = 0; i < bars.Count; i++)
                {
                    if (since.HasValue && bars[i].Date < since.Value) continue;
                    if (until.HasValue && bars[i].Date > until.Value) continue;
                    keptBars.Add(bars[i]);
                    if (paired) keptVols.Add(volumes[i]);
                }

                if (keptBars.Count == 0)
                {
                    report(
                        $"{venueName} has no {symbol} candles for the requested dates — this endpoint "
                        + "only serves its most recent window, which currently runs "
                        + $"{oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to "
                        + $"{newest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.");
                }

                bars = keptBars;
                volumes = paired ? keptVols : volumes;
                paired = volumes.Count == bars.Count;
            }

            // Limit trims from the OLD end: a request for N bars ending at Until wants the N
            // closest to Until, which after the filter above are the last N.
            if (request.Limit > 0 && bars.Count > request.Limit)
            {
                int drop = bars.Count - request.Limit;
                bars = bars.Skip(drop).ToList();
                if (paired) volumes = volumes.Skip(drop).ToList();
            }

            return (bars, volumes);
        }
    }
}
