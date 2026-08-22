using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Is the late-session move worth following rather than fading?
///
/// <para>
/// TWO CLAIMS, FROM TWO PRACTITIONERS, BOTH BLOCKED UNTIL NOW on the absence of US equity intraday
/// data. Both are testable on the same bars.
/// </para>
///
/// <list type="number">
///   <item>
///     <b>Peter Tuchman</b> (NYSE floor broker since 1985), 2026-08-02: "at 3:00 and at 3:30 usually
///     the market make a big move … your retail audience should know that you should rather be on
///     the same side of the move that the market does at 3 and 3:30 rather than trying to be
///     counterintuitive." He offers a MECHANISM — closing-bell order flow populates brokers'
///     handhelds at 14:00 and updates through the afternoon, so the late move is the market
///     absorbing imbalance information. That makes it more than folklore, and worth a real test.
///   </item>
///   <item>
///     <b>David Hannan</b>, 2026-08-02: "you tend to get a lot cleaner moves around open … if you're
///     trying to buy a breakout at like 1:00 p.m. I found it's just it tends to be lower volume and
///     lower follow-through."
///   </item>
/// </list>
///
/// <para>
/// THE CONTROL THAT THE QUEUED RECORD DEMANDED, and the only one that matters here: <b>compare the
/// 15:00–16:00 window against EVERY OTHER HOUR of the session.</b> "The market moves at 3pm" is true
/// of every hour if you never compare, and every hour of the session has someone who swears by it.
/// So each hour gets the same three numbers and they are printed side by side. A claim that
/// singles out one hour has to show that hour standing out.
/// </para>
///
/// <para>
/// WHAT IS MEASURED, and why follow-through rather than movement. Two different questions get
/// conflated in these claims:
/// </para>
/// <list type="bullet">
///   <item><b>Magnitude</b> — how far does price travel in this hour? Almost certainly biggest at the
///         open and into the close; that is a well-known volatility smile and it is not a claim
///         about anything tradeable.</item>
///   <item><b>Follow-through</b> — GIVEN the hour's move, does the NEXT interval continue it or
///         reverse it? This is the actual claim: "be on the same side of the move". It is measured
///         as the correlation between the hour's return and the following interval's return, and as
///         the mean forward return conditioned on the hour's direction.</item>
/// </list>
///
/// <para>
/// A positive follow-through number says continuation; negative says the move fades. The null is
/// zero, and the honest comparison is against the other hours rather than against zero, since a
/// whole-session drift would lift every hour together.
/// </para>
///
/// <para>
/// SESSION FILTERING. Only regular trading hours count, 09:30–16:00 ET. The snapshots carry
/// extended-hours bars, which are thin, wide-spread and dominated by a handful of prints — leaving
/// them in would let 04:00 look like the most "volatile" hour of the day and quietly wreck every
/// cross-hour comparison. US daylight-saving is handled by converting each UTC timestamp with the
/// America/New_York zone rather than a fixed offset, because a fixed offset silently shifts the
/// whole study by an hour for eight months of the year — which for a study ABOUT the hour of day
/// would be fatal.
/// </para>
///
/// <para>
/// NO LOOKAHEAD. Every measurement uses the hour's completed return to predict the NEXT interval.
/// Nothing reads a bar that had not printed.
/// </para>
/// </summary>
public static class LateSessionCommand
{
    private static readonly TimeZoneInfo Eastern = ResolveEastern();

    private sealed record HourStat(
        int Hour, int N, double MeanAbsMovePct, double FollowThroughCorr,
        double MeanNextGivenUp, double MeanNextGivenDown, double ContinuationRate, double PContinuation);

    /// <summary>
    /// Mean absolute move of the FULL hour, kept separate from the follow-through measurement.
    ///
    /// <para>
    /// These have to be measured on different windows and it matters. Follow-through at 15:00 needs
    /// two NON-OVERLAPPING halves (15:00-15:30 predicting 15:30-16:00), but magnitude has to be the
    /// whole 15:00-16:00 hour or it is being compared against other rows' full hours and will look
    /// artificially small — roughly by a factor of root two, purely from the shorter window. The
    /// first version of this table made exactly that mistake and would have supported a conclusion
    /// ("the 3pm hour is the QUIETEST of the day") that the data does not actually say.
    /// </para>
    /// </summary>
    private static readonly Dictionary<(string Sym, int Hour), double> FullHourMove = new();

    public static int Run(string snapshotDir, string? only, int permutations = 20000)
    {
        var files = Directory.GetFiles(snapshotDir, "*_5m.json")
            .Where(f => Path.GetFileName(f).StartsWith("alpaca_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            Console.Error.WriteLine("No alpaca 5m snapshots found. Pull them with:");
            Console.Error.WriteLine("  StrategyLab snapshot --provider alpaca --symbol SPY --tf 5m --bars 900000 --key K --secret S");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("═════ DOES THE LATE-SESSION MOVE FOLLOW THROUGH? ═════");
        Console.WriteLine("Tuchman: 'be on the same side of the move that the market does at 3 and 3:30.'");
        Console.WriteLine("Hannan:  'cleaner moves around open; a 1pm breakout has lower follow-through.'");
        Console.WriteLine();
        Console.WriteLine("THE CONTROL: every hour of the session gets the same numbers. 'The market moves at 3pm'");
        Console.WriteLine("is true of every hour if you never compare.");
        Console.WriteLine("Regular trading hours only (09:30-16:00 ET, DST-aware). Follow-through = does the NEXT");
        Console.WriteLine("half-hour continue this hour's move?");
        Console.WriteLine();

        var all = new List<(string Sym, List<HourStat> Hours)>();

        foreach (var f in files)
        {
            var snap = SnapshotCommand.Load(f);
            var stats = Analyse(snap.Bars, snap.Symbol, permutations);
            if (stats.Count == 0) continue;
            all.Add((snap.Symbol, stats));
            Print(snap.Symbol, snap.Bars, stats);
        }

        if (all.Count == 0) { Console.Error.WriteLine("No instrument produced usable sessions."); return 1; }

        Pooled(all, permutations);
        return 0;
    }

    // ── Measurement ─────────────────────────────────────────────────────────────

    private static List<HourStat> Analyse(IReadOnlyList<Ohlcv> bars, string symbol, int permutations)
    {
        // Group into (date, hour) buckets of regular-hours 5-minute bars.
        var sessions = new Dictionary<DateTime, SortedDictionary<DateTime, Ohlcv>>();
        foreach (var b in bars)
        {
            var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(b.Date, DateTimeKind.Utc), Eastern);
            var tod = et.TimeOfDay;
            if (tod < new TimeSpan(9, 30, 0) || tod >= new TimeSpan(16, 0, 0)) continue;   // RTH only
            if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (!sessions.TryGetValue(et.Date, out var day))
                sessions[et.Date] = day = new SortedDictionary<DateTime, Ohlcv>();
            day[et] = b;
        }

        // Hourly buckets, keyed by the hour the window STARTS: 9 covers 09:30-10:30 (the open is a
        // half hour), then 10..15. Hour 15 is the 15:00-16:00 window the claim is about.
        var byHour = new Dictionary<int, List<(double Move, double Next)>>();
        for (int h = 9; h <= 15; h++) byHour[h] = new List<(double, double)>();
        var fullHour = new Dictionary<(string, int), (double Sum, int Count)>();

        foreach (var (_, day) in sessions)
        {
            var times = day.Keys.ToList();
            if (times.Count < 60) continue;                 // a shortened session; skip rather than distort

            for (int h = 9; h <= 15; h++)
            {
                var start = h == 9 ? new TimeSpan(9, 30, 0) : new TimeSpan(h, 0, 0);
                var end = new TimeSpan(h + 1, 0, 0);
                var window = times.Where(t => t.TimeOfDay >= start && t.TimeOfDay < end).ToList();
                if (window.Count < 6) continue;

                // The NEXT half hour is the outcome. Half an hour rather than the next full hour so
                // the 15:00 window has somewhere to point (15:00-16:00's outcome is 15:30-16:00's
                // second half is not available, so hour 15 is measured on its own two halves).
                var nextStart = end;
                var nextEnd = end.Add(TimeSpan.FromMinutes(30));
                if (h == 15) { nextStart = new TimeSpan(15, 30, 0); nextEnd = new TimeSpan(16, 0, 0); }

                var next = times.Where(t => t.TimeOfDay >= nextStart && t.TimeOfDay < nextEnd).ToList();
                if (next.Count < 3) continue;

                // For hour 15 the "move" is the first half only, so the two windows do not overlap.
                var moveWindow = h == 15
                    ? times.Where(t => t.TimeOfDay >= new TimeSpan(15, 0, 0) && t.TimeOfDay < new TimeSpan(15, 30, 0)).ToList()
                    : window;
                if (moveWindow.Count < 3) continue;

                double o = day[moveWindow[0]].Open, c = day[moveWindow[^1]].Close;
                double no = day[next[0]].Open, nc = day[next[^1]].Close;
                if (o <= 0 || no <= 0) continue;

                byHour[h].Add(((c - o) / o * 100.0, (nc - no) / no * 100.0));

                // Full-hour magnitude, always the complete 09:30-10:30 / HH:00-HH+1:00 window, so
                // the magnitude column compares like with like even where follow-through cannot.
                double fo = day[window[0]].Open, fc = day[window[^1]].Close;
                if (fo > 0)
                {
                    var key = (symbol, h);
                    fullHour.TryGetValue(key, out var acc);
                    fullHour[key] = (acc.Sum + Math.Abs((fc - fo) / fo * 100.0), acc.Count + 1);
                }
            }
        }

        var outp = new List<HourStat>();
        foreach (var h in byHour.Keys.OrderBy(x => x))
        {
            var rows = byHour[h];
            if (rows.Count < 200) continue;

            double meanAbs = fullHour.TryGetValue((symbol, h), out var fh) && fh.Count > 0
                ? fh.Sum / fh.Count
                : rows.Average(r => Math.Abs(r.Move));
            double corr = Corr(rows.Select(r => r.Move).ToArray(), rows.Select(r => r.Next).ToArray());
            var up = rows.Where(r => r.Move > 0).ToList();
            var dn = rows.Where(r => r.Move < 0).ToList();
            double mUp = up.Count > 0 ? up.Average(r => r.Next) : double.NaN;
            double mDn = dn.Count > 0 ? dn.Average(r => r.Next) : double.NaN;

            // Continuation rate: how often does the next window move the SAME way as this one?
            int cont = rows.Count(r => Math.Sign(r.Move) == Math.Sign(r.Next) && r.Move != 0 && r.Next != 0);
            int valid = rows.Count(r => r.Move != 0 && r.Next != 0);
            double rate = valid == 0 ? double.NaN : cont / (double)valid;

            // A sign-flip permutation null. Flipping the sign of the FORWARD return breaks any
            // directional relationship while preserving both series' magnitudes exactly, so the null
            // is "the next window moves independently of this one" rather than "returns are zero".
            double p = SignFlipP(rows, rate, permutations, symbol, h);

            outp.Add(new HourStat(h, valid, meanAbs, corr, mUp, mDn, rate, p));
        }
        return outp;
    }

    private static double SignFlipP(List<(double Move, double Next)> rows, double observed,
                                    int permutations, string symbol, int hour)
    {
        if (double.IsNaN(observed)) return double.NaN;
        var rng = new Random(StableSeed.From($"{symbol}|{hour}"));
        var valid = rows.Where(r => r.Move != 0 && r.Next != 0).ToList();
        int atLeast = 0;
        for (int p = 0; p < permutations; p++)
        {
            int cont = 0;
            foreach (var r in valid)
            {
                int nextSign = rng.Next(2) == 0 ? 1 : -1;
                if (Math.Sign(r.Move) == nextSign) cont++;
            }
            if (cont / (double)valid.Count >= observed) atLeast++;
        }
        return (atLeast + 1.0) / (permutations + 1.0);
    }

    private static double Corr(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average();
        double num = 0, da = 0, db = 0;
        for (int i = 0; i < a.Length; i++)
        {
            num += (a[i] - ma) * (b[i] - mb);
            da += (a[i] - ma) * (a[i] - ma);
            db += (b[i] - mb) * (b[i] - mb);
        }
        return da <= 0 || db <= 0 ? double.NaN : num / Math.Sqrt(da * db);
    }

    // ── Reporting ───────────────────────────────────────────────────────────────

    private static void Print(string sym, IReadOnlyList<Ohlcv> bars, List<HourStat> stats)
    {
        Console.WriteLine($"── {sym}  ({bars.Count:N0} 5m bars, {bars[0].Date:yyyy-MM} → {bars[^1].Date:yyyy-MM}) "
                        + new string('─', 20));
        Console.WriteLine($"{"window (ET)",-16}{"n",7}{"|move| full hr",16}{"follow-thru r",15}{"cont rate",11}{"p",8}{"next|up",10}{"next|down",11}");
        foreach (var s in stats)
        {
            string label = s.Hour == 9 ? "09:30-10:30" : s.Hour == 15 ? "15:00-15:30*" : $"{s.Hour}:00-{s.Hour + 1}:00";
            Console.WriteLine($"{label,-16}{s.N,7}{s.MeanAbsMovePct,15:0.000}%{s.FollowThroughCorr,15:+0.000;-0.000}"
                            + $"{s.ContinuationRate * 100,10:0.0}%{s.PContinuation,8:0.000}"
                            + $"{s.MeanNextGivenUp,9:+0.000;-0.000}%{s.MeanNextGivenDown,10:+0.000;-0.000}%");
        }
        Console.WriteLine("  * follow-through for 15:00 uses 15:00-15:30 predicting 15:30-16:00 so the windows do not");
        Console.WriteLine("    overlap; the magnitude column is always the FULL hour, so all rows are comparable.");
        Console.WriteLine();
    }

    private static void Pooled(List<(string Sym, List<HourStat> Hours)> all, int permutations)
    {
        Console.WriteLine("── POOLED ACROSS INSTRUMENTS " + new string('─', 48));
        Console.WriteLine($"{"window (ET)",-16}{"mean cont rate",16}{"mean follow-thru r",20}{"instruments > 50%",20}");

        var hours = all.SelectMany(a => a.Hours.Select(h => h.Hour)).Distinct().OrderBy(x => x).ToList();
        var summary = new List<(int Hour, double Rate, double Corr, int Above)>();

        foreach (var h in hours)
        {
            var rows = all.Select(a => a.Hours.FirstOrDefault(x => x.Hour == h)).Where(x => x != null).ToList();
            if (rows.Count == 0) continue;
            double rate = rows.Average(r => r!.ContinuationRate);
            double corr = rows.Average(r => r!.FollowThroughCorr);
            int above = rows.Count(r => r!.ContinuationRate > 0.5);
            summary.Add((h, rate, corr, above));

            string label = h == 9 ? "09:30-10:30" : h == 15 ? "15:00-15:30" : $"{h}:00-{h + 1}:00";
            Console.WriteLine($"{label,-16}{rate * 100,15:0.00}%{corr,20:+0.0000;-0.0000}{above + "/" + rows.Count,20}");
        }

        Console.WriteLine();
        Console.WriteLine("── VERDICT " + new string('─', 66));

        var late = summary.FirstOrDefault(s => s.Hour == 15);
        var others = summary.Where(s => s.Hour != 15).ToList();
        if (others.Count == 0) { Console.WriteLine("  Not enough hours to compare."); return; }

        double otherRate = others.Average(o => o.Rate);
        double otherCorr = others.Average(o => o.Corr);
        int rank = 1 + summary.Count(s => s.Rate > late.Rate);

        Console.WriteLine($"  TUCHMAN (follow the 15:00 move): continuation {late.Rate * 100:0.00}% at 15:00 against "
                        + $"{otherRate * 100:0.00}% across the other hours.");
        Console.WriteLine($"  The 15:00 window ranks {rank} of {summary.Count} hours for continuation.");
        bool tuchman = late.Rate > otherRate + 0.01 && rank == 1;
        Console.WriteLine($"  → {(tuchman ? "SUPPORTED" : "NOT SUPPORTED")}: the late-session move is "
                        + $"{(tuchman ? "measurably more likely to continue than other hours." : "not distinguishable from any other hour of the session.")}");
        Console.WriteLine();

        var open = summary.FirstOrDefault(s => s.Hour == 9);
        var midday = summary.FirstOrDefault(s => s.Hour == 13);
        Console.WriteLine($"  HANNAN (open follows through, 1pm does not): open {open.Rate * 100:0.00}% vs "
                        + $"13:00 {midday.Rate * 100:0.00}%.");
        bool hannan = open.Rate > midday.Rate + 0.01;
        Console.WriteLine($"  → {(hannan ? "SUPPORTED" : "NOT SUPPORTED")}.");
        Console.WriteLine();
        Console.WriteLine($"  Every continuation rate above is against a sign-flip null whose expectation is 50%.");
        Console.WriteLine($"  Mean follow-through correlation across all hours: {summary.Average(s => s.Corr):+0.0000;-0.0000}.");
        Console.WriteLine();
        Console.WriteLine("  Caveats: SPY/QQQ/DIA/IWM are largely one portfolio, so the instrument counts are not");
        Console.WriteLine("  independent votes. 7 hourly windows x 2 claims = 14 comparisons; at alpha 0.05 expect 0.7");
        Console.WriteLine("  by chance. Costs are NOT modelled — a half-hour equity round trip would consume most of");
        Console.WriteLine("  any edge this size, so a positive result here would still need a cost pass.");
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The America/New_York zone. A FIXED -5 offset would silently shift the whole study by an hour
    /// for the eight months of daylight saving — in a study about the hour of day, that is not a
    /// rounding issue, it is the study measuring the wrong windows most of the year.
    /// </summary>
    private static TimeZoneInfo ResolveEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new InvalidOperationException("No US Eastern time zone available; the session filter cannot be trusted.");
    }
}
