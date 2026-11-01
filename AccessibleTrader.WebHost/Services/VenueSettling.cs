using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// A venue's bar list in the first seconds after a bar closes, which can be wrong in two
    /// ways that put back right within seconds. The background monitors poll once a minute
    /// with nothing watching the screen, so a poll that lands in that window is believed
    /// whole: it is the bar close they announce and the price their alerts read.
    ///
    /// <para><b>Seen on Bitstamp, 2026-11-01 04:00 UTC,</b> polling btcusd every 5 seconds:</para>
    /// <list type="bullet">
    /// <item><b>The bar that just closed is missing.</b> At 04:00:01 the hourly list came back
    /// with 999 bars: 02:00, then a 04:00 bar, and no 03:00. The monitor saw a newer bar,
    /// announced <c>bars[^2]</c>, and that was the 02:00 bar it had already announced an hour
    /// before. Every later poll saw 04:00 again, so the real 03:00 close was never said. Cody
    /// heard this as the same hour announced twice and a later hour skipped ("1h: close
    /// 77401.41 at 07:00" twice, no 08:00).</item>
    /// <item><b>The new bar is a placeholder.</b> At 04:00:04 the 03:00 bar was back, but the
    /// 04:00 bar read open = close = 77429.98 with zero volume. That price belonged to a bar
    /// that closed five hours earlier; the market was at 77658. That stale open is what the
    /// announcement read out as "New bar: open". A price alert compares the newest close with
    /// the one before it, so it could fire on a price the market was nowhere near.</item>
    /// </list>
    /// <para>By 04:00:08 both answers were right. So a list that looks like either case is set
    /// aside and the next poll, a minute later, is used instead. That happens at most
    /// <see cref="MaxSkips"/> times for one new bar. After that the list is taken as it is: a
    /// quiet market can open a bar with no trades in it, and that bar is real.</para>
    /// </summary>
    internal sealed class VenueSettling
    {
        /// <summary>How many polls in a row one new bar may be set aside before it is believed.</summary>
        internal const int MaxSkips = 2;

        private readonly Dictionary<string, (DateTime Newest, int Skipped)> _skips = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <paramref name="bars"/> has the shape of a list caught while it was still
        /// settling. <paramref name="previousNewest"/> is the newest bar the last poll saw: the
        /// bar that was forming then. No new bar means nothing was announced, so nothing can be
        /// wrong.
        /// </summary>
        internal static bool LooksUnsettled(IReadOnlyList<Ohlcv> bars, DateTime previousNewest)
        {
            if (bars.Count < 2 || bars[^1].Date <= previousNewest) return false;
            if (bars[^2].Date < previousNewest) return true;           // the bar that was forming is gone
            return bars[^1].Volume == 0 && bars[^2].Volume > 0;         // a placeholder on a feed that reports volume
        }

        /// <summary>
        /// True when this poll's list for <paramref name="key"/> should be set aside. Each call
        /// that returns true counts against <see cref="MaxSkips"/> for the list's newest bar.
        /// </summary>
        internal bool ShouldSkip(string key, IReadOnlyList<Ohlcv> bars, DateTime? previousNewest)
        {
            lock (_skips)
            {
                if (previousNewest is not DateTime previous || !LooksUnsettled(bars, previous))
                {
                    _skips.Remove(key);
                    return false;
                }
                var newest = bars[^1].Date;
                int skipped = _skips.TryGetValue(key, out var s) && s.Newest == newest ? s.Skipped : 0;
                if (skipped >= MaxSkips)
                {
                    _skips.Remove(key);
                    return false;
                }
                _skips[key] = (newest, skipped + 1);
                return true;
            }
        }

        /// <summary>Forget every key not in <paramref name="keep"/>.</summary>
        internal void Prune(ISet<string> keep)
        {
            lock (_skips)
                foreach (var key in _skips.Keys.Where(k => !keep.Contains(k)).ToList())
                    _skips.Remove(key);
        }
    }
}
