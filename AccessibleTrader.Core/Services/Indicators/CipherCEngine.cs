using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// One asset's cycle lengths: the daily cycle in days, the weekly cycle in weeks, the yearly
    /// cycle in years. <see cref="CalendarDays"/> says which days the daily range counts — calendar
    /// days for an asset that trades every day (Bitcoin), trading days for one that does not (the
    /// S&amp;P). <see cref="Source"/> is said aloud, so it is plain words.
    /// </summary>
    public sealed record CyclePreset(
        string Key, string Name,
        double DailyMin, double DailyMax,
        double WeeklyMin, double WeeklyMax,
        double YearlyMin, double YearlyMax,
        bool CalendarDays, string Source);

    /// <summary>
    /// The arithmetic behind <see cref="CipherCProvider"/>: the asset presets, how many bars a day
    /// is on this chart, and the cycle-low detector run once per tier.
    ///
    /// <para>
    /// <b>What the detector is.</b> Camel Finance's CF Cycle Trading Indicator is closed source. Its
    /// settings screen shows "overbought 75, oversold 10", "fast/slow EMA adjustment" and a "cycle
    /// length adjustment factor" — the inputs of a Schaff Trend Cycle (a MACD put through a double
    /// stochastic), with its lengths scaled from the asset's cycle range. This detector is that
    /// inference, fitted to the 25 SPX daily cycle lows CF itself labelled on Camel's videos
    /// (2020–2022 and 2024–2025): it finds 18 of the 25 within two bars (72%). Held out, on CF's
    /// own labels it was never fitted to, it finds 8 of 13 within four days (gold 3 of 7, Dow 3 of
    /// 4, Bitcoin 2 of 2); those labels were read off video frames and carry ±1–3 days themselves.
    /// The misses are mostly half-cycle lows CF marks and this does not.
    /// </para>
    ///
    /// <para>
    /// <b>The rule.</b> After the Schaff line has been at or under the oversold level, its first
    /// cross up through the overbought level CONFIRMS a cycle low: the lowest swing low since the
    /// last confirmed low's minimum gap (60% of the shortest cycle). The low must still be the
    /// lowest bar up to the confirming bar. A close under a confirmed low before that minimum gap
    /// CANCELS it (CF's "cancelled" state); a close under it after the gap is a FAILED cycle.
    /// Confirmation therefore comes some bars after the low — a median of nine on SPX daily.
    /// </para>
    ///
    /// <para>
    /// <b>What it is not.</b> Not an edge. Camel's own checklist showed none against shuffled
    /// prices in this project's tests, and CF's own SPX counts fall inside its stated 36–44 day
    /// window only 5 times in 23 (22–64 trading days, median 43; 17 of 23 fall in 30–50). The indicator follows the method;
    /// it does not prove it.
    /// </para>
    /// </summary>
    public static class CipherCEngine
    {
        // ── Fitted on CF's SPX labels (scratchpad cfproto/det2.py; see the class remarks) ──
        public const double MinGapFraction   = 0.6;   // of the shortest cycle, in bars
        public const double StcLengthFactor  = 0.25;  // stochastic length = mean cycle × this
        public const double StcFastFactor    = 0.5;   // fast EMA = mean cycle × this
        public const double StcSlowFactor    = 1.0;   // slow EMA = mean cycle × this
        public const double SearchBackCycles = 1.5;   // the low is looked for at most this many longest cycles back

        // ── Presets ────────────────────────────────────────────────────────────────────

        private const string FromCf     = "CF's published ranges";
        private const string Estimated  = "estimated, not CF's";

        /// <summary>
        /// The sixteen assets CF says it is fine-tuned for (its own watchlist on cftrading.co.uk:
        /// SPX, DXY, NDQ, DJI, GOLD, SILVER, USOIL, GDX, GDXJ, SILJ, ETHUSD, BTCUSD, COIN, MSTR,
        /// MARA, RIOT). Nine of its range tables are now CF's own: SPX, GOLD and Ethereum, plus
        /// COIN, MSTR, MARA, RIOT, GDX and Bitcoin read off reviewer videos from August 2025. The
        /// other seven are estimates from the nearest of those and say so.
        /// </summary>
        public static readonly IReadOnlyList<CyclePreset> Presets = new[]
        {
            new CyclePreset("SPX",    "S&P 500",     36, 44, 22, 31, 3.5, 4.5, false, FromCf),
            new CyclePreset("NDQ",    "Nasdaq 100",  36, 44, 22, 31, 3.5, 4.5, false, Estimated),
            new CyclePreset("DJI",    "Dow Jones",   36, 44, 22, 31, 3.5, 4.5, false, Estimated),
            new CyclePreset("DXY",    "Dollar index", 15, 25, 15, 20, 2.5, 3.5, false, Estimated),
            new CyclePreset("GOLD",   "Gold",        22, 28, 20, 28, 8,   8,   false, FromCf),
            new CyclePreset("SILVER", "Silver",      22, 28, 20, 28, 8,   8,   false, Estimated),
            new CyclePreset("USOIL",  "Crude oil",   30, 45, 18, 26, 3,   4,   false, Estimated),
            new CyclePreset("GDX",    "Gold miners", 22, 28, 22, 26, 8,   8,   false, FromCf),
            new CyclePreset("GDXJ",   "Junior gold miners", 22, 28, 20, 28, 8, 8, false, Estimated),
            new CyclePreset("SILJ",   "Junior silver miners", 22, 28, 20, 28, 8, 8, false, Estimated),
            new CyclePreset("ETH",    "Ethereum",    15, 25, 7,  10, 3.5, 4.5, true,  FromCf),
            // CF's own BTC table (reviewer videos, 7-9 Aug 2025) reads 22-42 weeks; Camel says 28-32 on
            // his 2026 streams. The indicator is what Cipher C copies, so the table wins.
            new CyclePreset("BTC",    "Bitcoin",     54, 66, 22, 42, 4,   4,   true,  FromCf),
            // TradingView counts stock bars, so these CF daily ranges are treated as trading days;
            // that interpretation is an inference from the stock charts, not an explicit CF note.
            new CyclePreset("COIN",   "Coinbase",    40, 90, 24, 42, 4,   4,   false, FromCf),
            new CyclePreset("MSTR",   "MicroStrategy", 40, 90, 24, 42, 4, 4, false, FromCf),
            new CyclePreset("MARA",   "Marathon Digital", 36, 44, 22, 26, 4, 4, false, FromCf),
            new CyclePreset("RIOT",   "Riot Platforms", 36, 44, 22, 26, 4, 4, false, FromCf),
        };

        /// <summary>Other names each preset's asset trades under: index, ETF, future, CFD, spot pair.</summary>
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SPX"] = "SPX", ["SPY"] = "SPX", ["ES"] = "SPX", ["US500"] = "SPX", ["VOO"] = "SPX", ["IVV"] = "SPX", ["GSPC"] = "SPX", ["SPX500"] = "SPX",
            ["NDQ"] = "NDQ", ["NDX"] = "NDQ", ["QQQ"] = "NDQ", ["NQ"] = "NDQ", ["US100"] = "NDQ", ["NAS100"] = "NDQ", ["IXIC"] = "NDQ",
            ["DJI"] = "DJI", ["DIA"] = "DJI", ["YM"] = "DJI", ["US30"] = "DJI", ["DJIA"] = "DJI",
            ["DXY"] = "DXY", ["UUP"] = "DXY", ["DX"] = "DXY",
            ["GOLD"] = "GOLD", ["XAU"] = "GOLD", ["GLD"] = "GOLD", ["GC"] = "GOLD", ["IAU"] = "GOLD",
            ["SILVER"] = "SILVER", ["XAG"] = "SILVER", ["SLV"] = "SILVER", ["SI"] = "SILVER",
            ["USOIL"] = "USOIL", ["WTI"] = "USOIL", ["CL"] = "USOIL", ["USO"] = "USOIL", ["OIL"] = "USOIL",
            ["GDX"] = "GDX", ["GDXJ"] = "GDXJ", ["SILJ"] = "SILJ",
            ["ETH"] = "ETH", ["ETHEREUM"] = "ETH", ["ETHA"] = "ETH", ["ETHE"] = "ETH",
            ["BTC"] = "BTC", ["XBT"] = "BTC", ["BITCOIN"] = "BTC", ["IBIT"] = "BTC", ["FBTC"] = "BTC", ["GBTC"] = "BTC",
            ["COIN"] = "COIN", ["MSTR"] = "MSTR", ["MARA"] = "MARA", ["RIOT"] = "RIOT",
        };

        private static readonly string[] QuoteSuffixes = { "USDT", "USDC", "USD", "EUR", "GBP" };

        public static CyclePreset Preset(string key) => Presets.First(p => p.Key == key);

        /// <summary>
        /// The preset for a chart symbol ("BTC/USD", "TVC:GOLD", "XAUUSD", "SPY", "^GSPC"), or null
        /// when the symbol is none of the sixteen.
        /// </summary>
        public static CyclePreset? PresetForSymbol(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            string s = symbol.Trim();
            int colon = s.LastIndexOf(':');
            if (colon >= 0) s = s[(colon + 1)..];
            s = s.TrimStart('^', '$');
            string head = s.Split('/', '-', '_', ' ', '.')[0];
            if (Aliases.TryGetValue(head, out var key)) return Preset(key);
            foreach (var q in QuoteSuffixes)
                if (head.Length > q.Length && head.EndsWith(q, StringComparison.OrdinalIgnoreCase)
                    && Aliases.TryGetValue(head[..^q.Length], out key))
                    return Preset(key);
            return null;
        }

        /// <summary>
        /// Which preset drives a chart, and whether it was chosen for this asset. "Auto" (or blank)
        /// reads the symbol; a symbol that is none of the sixteen gets Bitcoin's ranges when it
        /// trades every day and the S&amp;P's otherwise — CF's own fallback is not known, so the
        /// reading says the asset is not one CF tunes.
        /// </summary>
        public static (CyclePreset Preset, bool FineTuned) Resolve(string? choice, string? symbol, bool tradesEveryDay)
        {
            string c = (choice ?? "").Trim();
            if (c.Length > 0 && !c.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                var chosen = PresetForSymbol(c);
                if (chosen != null) return (chosen, true);
            }
            var bySymbol = PresetForSymbol(symbol);
            if (bySymbol != null) return (bySymbol, true);
            return (Preset(tradesEveryDay ? "BTC" : "SPX"), false);
        }

        // ── How long a bar is ─────────────────────────────────────────────────────────

        /// <summary>
        /// What the chart's bars are, measured from the bars themselves (so a test, a background
        /// tab and the lab agree with the browser without being told the timeframe).
        /// </summary>
        /// <param name="BarsPerDay">Bars in one of the chart's days: a calendar day when the asset
        /// trades every day, a trading day when it does not. 1 on a daily chart, 1/5 on a weekly
        /// stock chart, 24 on an hourly crypto chart.</param>
        /// <param name="TradesEveryDay">Bars fall on Saturdays or Sundays.</param>
        /// <param name="BarDays">The bar's own length in those days (1 daily, 5 or 7 weekly).</param>
        public readonly record struct BarClock(double BarsPerDay, bool TradesEveryDay)
        {
            public double DaysPerWeek => TradesEveryDay ? 7 : 5;
            public double DaysPerYear => TradesEveryDay ? 365.25 : 252;
            public double BarDays => 1 / BarsPerDay;
        }

        public static BarClock MeasureBars(ReadOnlySpan<Ohlcv> data)
        {
            int n = data.Length;
            if (n < 3) return new BarClock(1, false);
            int from = Math.Max(1, n - 500);
            var gaps = new List<double>(n - from);
            bool weekend = false;
            for (int i = from; i < n; i++)
            {
                double g = (data[i].Date - data[i - 1].Date).TotalSeconds;
                if (g > 0) gaps.Add(g);
            }
            for (int i = from - 1; i < n; i++)
                if (data[i].Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { weekend = true; break; }
            if (gaps.Count == 0) return new BarClock(1, weekend);
            gaps.Sort();
            double spacing = gaps[gaps.Count / 2];
            double spacingDays = spacing / 86400.0;

            if (spacingDays < 20.0 / 24)
            {
                if (weekend) return new BarClock(86400.0 / spacing, true);
                // A stock's intraday bars: how many make one session, from the dates themselves.
                var perDate = new Dictionary<DateTime, int>();
                for (int i = from - 1; i < n; i++)
                {
                    var d = data[i].Date.Date;
                    perDate[d] = perDate.TryGetValue(d, out var c) ? c + 1 : 1;
                }
                var counts = perDate.Values.OrderBy(v => v).ToList();
                return new BarClock(Math.Max(1, counts[counts.Count / 2]), false);
            }
            if (spacingDays < 4)
                return new BarClock(weekend ? 1 / Math.Round(spacingDays) : 1 / Math.Max(1, Math.Round(spacingDays)), weekend);
            // Weekly and longer: a week is 7 calendar or 5 trading days, a month about 30.4 or 21.7.
            // A bar spanning S calendar days spans S × 5/7 trading days, so a stock's weekly bar is
            // a fifth of a bar per trading day. (The factor was once 5/7 instead of 7/5, which made
            // a weekly stock bar 9.8 trading days long and switched the weekly tier off there.)
            return new BarClock((weekend ? 1.0 : 7.0 / 5.0) / spacingDays, weekend);
        }

        // ── One tier ───────────────────────────────────────────────────────────────────

        /// <summary>A tier's range in BARS of this chart, and whether the tier runs here.</summary>
        public readonly record struct TierBars(double MinBars, double MaxBars, bool Active)
        {
            public double MeanBars => (MinBars + MaxBars) / 2;
            public int MinGap => Math.Max(1, (int)Math.Round(MinBars * MinGapFraction));
        }

        /// <summary>
        /// A tier runs only when the chart's bar is no longer than the tier's unit — a daily cycle
        /// on a daily or shorter chart, a weekly cycle on a weekly or shorter one, a yearly cycle on
        /// anything up to a year — and its shortest cycle spans at least six bars. That is what
        /// keeps "DCL" off a weekly chart: on a weekly chart a daily cycle is six to nine bars at
        /// most and is the weekly cycle's business.
        /// </summary>
        public static TierBars Tier(double minDays, double maxDays, double unitDays, BarClock clock)
        {
            double minBars = minDays * clock.BarsPerDay, maxBars = maxDays * clock.BarsPerDay;
            bool active = clock.BarDays <= unitDays * 1.05 && minBars >= 6 && maxBars >= minBars;
            return new TierBars(minBars, maxBars, active);
        }

        /// <summary>What one tier found.</summary>
        public sealed class TierRun
        {
            /// <summary>The cycle lows that stood at the end of the data, oldest first.</summary>
            public List<int> Lows { get; } = new();
            /// <summary>The bar each of <see cref="Lows"/> was confirmed on.</summary>
            public List<int> ConfirmedAt { get; } = new();
            /// <summary>Per bar: the newest confirmed low known ON that bar, or -1.</summary>
            public int[] LastLowAt = Array.Empty<int>();
            /// <summary>Events, by the bar they happen on. The value is the price of the low concerned.</summary>
            public double[] Confirmed = Array.Empty<double>(), Cancelled = Array.Empty<double>(),
                            Updated = Array.Empty<double>(), Pending = Array.Empty<double>(),
                            Failed = Array.Empty<double>();
        }

        /// <summary>
        /// Runs the detector for one tier. Everything it publishes per bar is decided from that bar
        /// and earlier ones; only <see cref="TierRun.Lows"/> (the final list) knows the future.
        /// </summary>
        public static TierRun Detect(ReadOnlySpan<Ohlcv> data, TierBars tier, double overbought, double oversold)
        {
            int n = data.Length;
            var run = new TierRun
            {
                LastLowAt = Enumerable.Repeat(-1, n).ToArray(),
                Confirmed = Nan(n), Cancelled = Nan(n), Updated = Nan(n), Pending = Nan(n), Failed = Nan(n),
            };
            if (!tier.Active || n == 0) return run;

            double mean = tier.MeanBars;
            int len  = Math.Max(3, (int)Math.Round(mean * StcLengthFactor));
            int fast = Math.Max(2, (int)Math.Round(mean * StcFastFactor));
            int slow = Math.Max(fast + 1, (int)Math.Round(mean * StcSlowFactor));
            int swing = Math.Clamp((int)Math.Round(tier.MinBars / 7), 2, 5);
            int minGap = tier.MinGap;
            int searchBack = (int)Math.Round(tier.MaxBars * SearchBackCycles);

            var close = new double[n]; var low = new double[n];
            for (int i = 0; i < n; i++) { close[i] = data[i].Close; low[i] = data[i].Low; }
            var stc = SchaffTrendCycle(close, len, fast, slow);

            bool armed = false, cancelledSince = false;
            int last = -1, failedFor = -1;
            for (int t = slow * 2; t < n; t++)
            {
                if (stc[t] <= oversold && !armed)
                {
                    armed = true;
                    if (last < 0 || t - last >= minGap) run.Pending[t] = close[t];
                }
                else if (stc[t] <= oversold) armed = true;

                if (last >= 0 && close[t] < low[last])
                {
                    if (t - last < minGap)
                    {
                        run.Cancelled[t] = low[last];
                        run.Lows.RemoveAt(run.Lows.Count - 1);
                        run.ConfirmedAt.RemoveAt(run.ConfirmedAt.Count - 1);
                        last = run.Lows.Count > 0 ? run.Lows[^1] : -1;
                        armed = true; cancelledSince = true;
                    }
                    else if (failedFor != last)
                    {
                        run.Failed[t] = low[last];
                        failedFor = last;
                    }
                }

                if (armed && stc[t - 1] < overbought && stc[t] >= overbought)
                {
                    int start = last >= 0 ? last + minGap : 0;
                    start = Math.Max(Math.Max(start, t - searchBack), swing);
                    int c = -1;
                    for (int i = start; i < t; i++)
                    {
                        if (c >= 0 && low[i] >= low[c]) continue;
                        bool swingLow = true;
                        for (int j = i - swing; j <= Math.Min(t, i + swing) && swingLow; j++)
                            if (low[j] < low[i]) swingLow = false;
                        if (swingLow) c = i;
                    }
                    if (c >= 0)
                    {
                        bool stillLowest = true;
                        for (int j = c + 1; j <= t && stillLowest; j++)
                            if (low[j] < low[c]) stillLowest = false;
                        if (stillLowest)
                        {
                            run.Confirmed[t] = low[c];
                            if (cancelledSince) run.Updated[t] = low[c];
                            run.Lows.Add(c); run.ConfirmedAt.Add(t);
                            last = c; armed = false; cancelledSince = false;
                        }
                    }
                }
                run.LastLowAt[t] = last;
            }
            return run;
        }

        /// <summary>
        /// Moves each of a higher tier's lows onto the lowest lower-tier low within
        /// <paramref name="reach"/> bars of it, so a weekly cycle low is always one of the daily
        /// ones (as on CF's charts, where the blue W stack sits on a green D one). A low with no
        /// lower-tier low near it keeps its own bar.
        /// </summary>
        public static List<int> SnapTo(IReadOnlyList<int> lows, IReadOnlyList<int> lowerTier, int reach, ReadOnlySpan<Ohlcv> data)
        {
            var result = new List<int>();
            foreach (int w in lows)
            {
                int best = -1;
                foreach (int d in lowerTier)
                    if (Math.Abs(d - w) <= reach && (best < 0 || data[d].Low < data[best].Low))
                        best = d;
                if (best < 0) best = w;
                if (result.Count == 0 || result[^1] != best) result.Add(best);
            }
            return result;
        }

        /// <summary>
        /// The Schaff Trend Cycle: a MACD (fast EMA minus slow EMA of the close) turned into a
        /// 0–100 stochastic over <paramref name="length"/> bars, smoothed by half, put through the
        /// same stochastic again and smoothed by half again.
        /// </summary>
        public static double[] SchaffTrendCycle(double[] close, int length, int fast, int slow)
        {
            int n = close.Length;
            var macd = new double[n];
            double ef = double.NaN, es = double.NaN, af = 2.0 / (fast + 1), aS = 2.0 / (slow + 1);
            for (int i = 0; i < n; i++)
            {
                ef = double.IsNaN(ef) ? close[i] : ef + af * (close[i] - ef);
                es = double.IsNaN(es) ? close[i] : es + aS * (close[i] - es);
                macd[i] = ef - es;
            }
            var pf = new double[n];
            var outp = new double[n];
            double f1 = double.NaN, p = double.NaN, f2 = double.NaN, q = double.NaN;
            var w1 = new SlidingRange(length); var w2 = new SlidingRange(length);
            for (int i = 0; i < n; i++)
            {
                w1.Add(i, macd[i]);
                var (lo, hi) = w1.Range(i);
                f1 = hi > lo ? (macd[i] - lo) / (hi - lo) * 100 : (double.IsNaN(f1) ? 50 : f1);
                p = double.IsNaN(p) ? f1 : p + 0.5 * (f1 - p);
                pf[i] = p;
                w2.Add(i, p);
                var (lo2, hi2) = w2.Range(i);
                f2 = hi2 > lo2 ? (p - lo2) / (hi2 - lo2) * 100 : (double.IsNaN(f2) ? 50 : f2);
                q = double.IsNaN(q) ? f2 : q + 0.5 * (f2 - q);
                outp[i] = q;
            }
            return outp;
        }

        /// <summary>The lowest and highest of the last <c>length</c> values, kept in two monotonic deques.</summary>
        private sealed class SlidingRange
        {
            private readonly int _length;
            private readonly LinkedList<(int I, double V)> _min = new(), _max = new();
            public SlidingRange(int length) { _length = length; }
            public void Add(int i, double v)
            {
                while (_min.Count > 0 && _min.Last!.Value.V >= v) _min.RemoveLast();
                _min.AddLast((i, v));
                while (_max.Count > 0 && _max.Last!.Value.V <= v) _max.RemoveLast();
                _max.AddLast((i, v));
            }
            public (double Lo, double Hi) Range(int i)
            {
                while (_min.First!.Value.I <= i - _length) _min.RemoveFirst();
                while (_max.First!.Value.I <= i - _length) _max.RemoveFirst();
                return (_min.First.Value.V, _max.First.Value.V);
            }
        }

        // ── Bitcoin's four-year cycle ──────────────────────────────────────────────────

        /// <summary>
        /// Bitcoin halving dates (UTC), the anchors of Loukas's and Camel's four-year cycle. The
        /// 2028 date is an estimate from the average spacing.
        /// </summary>
        public static readonly DateTime[] BtcHalvings =
        {
            new DateTime(2012, 11, 28, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2016,  7,  9, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020,  5, 11, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024,  4, 19, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2028,  4, 20, 0, 0, 0, DateTimeKind.Utc), // estimated
        };

        /// <summary>
        /// Where a date sits in Bitcoin's four-year cycle, in words, by Loukas's published bands
        /// (accumulation to day 180, markup to 540, distribution to 720, then bear), or null before
        /// the first halving.
        /// </summary>
        public static string? HalvingPhase(DateTime date)
        {
            DateTime? anchor = null;
            foreach (var h in BtcHalvings) if (h <= date) anchor = h;
            if (anchor == null) return null;
            int day = (int)(date - anchor.Value).TotalDays;
            string phase = day < 180 ? "accumulation" : day < 540 ? "markup" : day < 720 ? "distribution" : "bear";
            return $"Day {day} after the {anchor.Value.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)} halving, the {phase} phase of the four-year cycle";
        }

        public static double[] Nan(int n)
        {
            var a = new double[n];
            Array.Fill(a, double.NaN);
            return a;
        }
    }
}
