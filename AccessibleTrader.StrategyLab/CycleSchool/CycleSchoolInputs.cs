using System.Globalization;
using System.Security.Cryptography;
using AccessibleTrader.StrategyLab.Macro;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.StrategyLab.CycleSchool;

/// <summary>An observation of an external metric: its own date, the first day it may be used, the
/// day its age is counted from for staleness, and its oriented value.</summary>
public readonly record struct Obs(DateOnly Date, DateOnly Known, DateOnly AgeRef, double Value);

/// <summary>One metric of the capitulation score on an asset's bars: its percentile (NaN where
/// undefined or stale) and the family it belongs to.</summary>
public sealed record Metric(string Id, string Family, double[] Pct);

/// <summary>
/// Reads every input of the cycle school, read-only, from <c>strategy-lab-data</c>, and builds the
/// capitulation score's metrics exactly as <c>docs/CYCLE_LOW_INDICATOR_DESIGN.md</c> section 5 and
/// the prereg addendum (A.3) fix them. Every macro value goes through <see cref="MacroPointInTime"/>
/// (<c>ParseKnownCsv</c>, <c>ParseVintageCsv</c>, <c>AsOf</c>, <c>LatestKnown</c>) and is used on a bar
/// only once its known-from date has come (<see cref="MapReleases"/>).
/// </summary>
public sealed class CycleSchoolInputs
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Root { get; }
    /// <summary>Every file read, for the before/after sha256 proof.</summary>
    public SortedSet<string> FilesRead { get; } = new(StringComparer.Ordinal);

    public CycleSchoolInputs(string root) => Root = Path.GetFullPath(root);

    /// <summary>The sha256 of each input, taken the first time it is opened (before it is read).</summary>
    public Dictionary<string, string> HashBefore { get; } = new(StringComparer.Ordinal);

    public string Abs(string rel)
    {
        var p = Path.Combine(Root, rel);
        if (FilesRead.Add(rel)) HashBefore[rel] = Sha(p);
        return p;
    }

    public static string Sha(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    // ── Files ────────────────────────────────────────────────────────────────

    /// <summary>Daily bars by trading day. A last bar fetched before its day had ended (Bitstamp's
    /// 2026-06-15 bar, fetched at 03:58 UTC that day) is dropped: it is not a daily bar.</summary>
    public Bar[] Bars(string file)
    {
        var doc = JObject.Parse(File.ReadAllText(Abs(file)));
        var fetched = (DateTime?)doc["FetchedUtc"];
        var bars = new List<Bar>();
        foreach (var b in (JArray)doc["Bars"]!)
        {
            var raw = (DateTime)b["Date"]!; var dt = raw.Kind == DateTimeKind.Local ? raw.ToUniversalTime() : raw; // a stamp without a zone is taken as written
            var day = DateOnly.FromDateTime(dt);
            bars.Add(new Bar(day, (double)b["Open"]!, (double)b["High"]!, (double)b["Low"]!, (double)b["Close"]!));
        }
        bars = bars.GroupBy(x => x.Day).Select(g => g.Last()).OrderBy(x => x.Day).ToList();
        bool crypto = file.StartsWith("bitstamp_", StringComparison.Ordinal);
        if (crypto && fetched is { } f && bars.Count > 0 && bars[^1].Day.AddDays(1).ToDateTime(TimeOnly.MinValue) > (f.Kind == DateTimeKind.Local ? f.ToUniversalTime() : f))
            bars.RemoveAt(bars.Count - 1);
        return bars.Where(x => x.Open > 0 && x.High > 0 && x.Low > 0 && x.Close > 0).ToArray();
    }

    public List<KnownValue> Known(string rel) => MacroPointInTime.ParseKnownCsv(File.ReadAllText(Abs(rel)));
    public List<VintageRow> Vintages(string rel) => MacroPointInTime.ParseVintageCsv(File.ReadAllText(Abs(rel)));

    /// <summary>A cross-series snapshot's points as (UTC day of the timestamp, timestamp, value).</summary>
    public List<(DateOnly Day, long Ts, double V)> Xs(string file)
    {
        var doc = JObject.Parse(File.ReadAllText(Abs(file)));
        return ((JArray)doc["Points"]!)
            .Select(p => ((long)p["Ts"]!, (double)p["Value"]!))
            .Select(p => (DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(p.Item1).UtcDateTime), p.Item1, p.Item2))
            .OrderBy(p => p.Item2).ToList();
    }

    public List<(DateOnly Date, string Type, DateOnly Month, DateOnly? Announced)> Nber()
    {
        var res = new List<(DateOnly, string, DateOnly, DateOnly?)>();
        foreach (var line in File.ReadAllLines(Abs("macro/nber/nber_turning_points.csv")).Skip(1))
        {
            var f = line.Split(',');
            if (f[0] is not ("peak" or "trough")) continue;
            var m = DateOnly.ParseExact(f[1], "yyyy-MM-dd", Inv);
            DateOnly? a = f[2].Length == 10 ? DateOnly.ParseExact(f[2], "yyyy-MM-dd", Inv) : null;
            res.Add((m, f[0], m, a));
        }
        return res;
    }

    public List<(DateOnly Date, bool Scheduled)> Fomc() =>
        JArray.Parse(File.ReadAllText(Abs("events_fomc.json")))
            .Select(t => (DateOnly.ParseExact((string)t["date"]!, "yyyy-MM-dd", Inv), (bool)t["scheduled"]!))
            .OrderBy(x => x.Item1).ToList();

    // ── Mapping a metric's own history onto bars ─────────────────────────────

    /// <summary>
    /// A metric with its own cadence (design note 3): its percentile is computed over its own
    /// history, in date order, needing <paramref name="min"/> values; on a bar dated D it shows the
    /// newest observation whose known-from is on or before D (strictly before D when
    /// <paramref name="strictlyBefore"/>, the H.4.1 rule on equity bars), and is undefined once
    /// D − AgeRef exceeds <paramref name="staleDays"/>.
    /// </summary>
    public static double[] MapReleases(IReadOnlyList<Obs> obs, int min, int staleDays, IReadOnlyList<DateOnly> barDays, bool strictlyBefore = false)
    {
        var ordered = obs.Where(o => !double.IsNaN(o.Value)).OrderBy(o => o.Date).ThenBy(o => o.Known).ToList();
        var pct = CycleSchoolCore.ExpandingPct(ordered.Select(o => o.Value).ToList(), min);
        // Walk the observations in known-from order, keeping the newest-dated one known so far.
        var byKnown = Enumerable.Range(0, ordered.Count).OrderBy(k => ordered[k].Known).ThenBy(k => ordered[k].Date).ToList();
        var outp = CycleSchoolCore.Nan(barDays.Count);
        int q = 0, best = -1;
        for (int i = 0; i < barDays.Count; i++)
        {
            var d = barDays[i];
            while (q < byKnown.Count && (strictlyBefore ? ordered[byKnown[q]].Known < d : ordered[byKnown[q]].Known <= d))
            {
                int k = byKnown[q++];
                if (best < 0 || ordered[k].Date > ordered[best].Date || (ordered[k].Date == ordered[best].Date && k > best)) best = k;
            }
            if (best < 0) continue;
            if (d.DayNumber - ordered[best].AgeRef.DayNumber > staleDays) continue;
            outp[i] = pct[best];
        }
        return outp;
    }

    // ── External series as observations ──────────────────────────────────────

    /// <summary>The value on or before <paramref name="d"/> in a date-sorted list (null if none).</summary>
    public static KnownValue? OnOrBefore(IReadOnlyList<KnownValue> s, DateOnly d)
    {
        int lo = 0, hi = s.Count;
        while (lo < hi) { int m = (lo + hi) >> 1; if (s[m].Date <= d) lo = m + 1; else hi = m; }
        return lo == 0 ? null : s[lo - 1];
    }

    /// <summary>x(d) − x(d − lag days) on the series' own dates; the earlier value is the newest on or
    /// before d − lag. Known from the later of the two known-from dates.</summary>
    public static List<Obs> ChangeOver(IReadOnlyList<KnownValue> s, int lagDays, double sign, bool logRatio = false, bool pctChange = false)
    {
        var res = new List<Obs>();
        foreach (var r in s)
        {
            if (r.KnownFrom == DateOnly.MaxValue) continue;
            var e = OnOrBefore(s, r.Date.AddDays(-lagDays));
            if (e is null || e.KnownFrom == DateOnly.MaxValue) continue;
            double v = logRatio ? Math.Log(r.Value / e.Value) : pctChange ? r.Value / e.Value - 1 : r.Value - e.Value;
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            var k = r.KnownFrom > e.KnownFrom ? r.KnownFrom : e.KnownFrom;
            res.Add(new Obs(r.Date, k, k, sign * v));
        }
        return res;
    }

    public static List<Obs> Level(IReadOnlyList<KnownValue> s, double sign) =>
        s.Where(r => r.KnownFrom != DateOnly.MaxValue).Select(r => new Obs(r.Date, r.KnownFrom, r.KnownFrom, sign * r.Value)).ToList();

    /// <summary>
    /// A vintaged monthly or weekly metric, one value per ALFRED vintage date (each release), from
    /// the series as published that day (<see cref="MacroPointInTime.AsOf"/>). Its age is counted
    /// from the first release of the newest period it uses (prereg addendum A.4). Only periods
    /// within <paramref name="window"/> days of the vintage date are passed to <c>AsOf</c>; the
    /// metrics read at most the last 56 weeks.
    /// </summary>
    public static List<Obs> PerVintage(IReadOnlyList<VintageRow> rows, Func<SortedDictionary<DateOnly, double>, double> metric, double sign, int window = 800)
    {
        var res = new List<Obs>();
        var firstSeen = rows.GroupBy(r => r.Period).ToDictionary(g => g.Key, g => g.Min(r => r.RealtimeStart));
        var byPeriod = rows.OrderBy(r => r.Period).ToList();
        var periods = byPeriod.Select(r => r.Period).ToList();
        foreach (var v in rows.Select(r => r.RealtimeStart).Distinct().OrderBy(d => d))
        {
            int from = LowerBound(periods, v.AddDays(-window));
            var sub = byPeriod.Skip(from);
            var asOf = MacroPointInTime.AsOf(sub, v);
            if (asOf.Count == 0) continue;
            double x = metric(asOf);
            if (double.IsNaN(x) || double.IsInfinity(x)) continue;
            var newest = asOf.Keys.Last();
            res.Add(new Obs(v, v, firstSeen[newest], sign * x));
        }
        return res;
    }

    private static int LowerBound(List<DateOnly> s, DateOnly d)
    {
        int lo = 0, hi = s.Count;
        while (lo < hi) { int m = (lo + hi) >> 1; if (s[m] < d) lo = m + 1; else hi = m; }
        return lo;
    }

    /// <summary>M9's transform (and M10 to M12's): the mean of the last 3 monthly changes, which
    /// is (x(m) − x(m−3)) ÷ 3; needs the four newest months to be consecutive.</summary>
    public static double MeanOfLast3Changes(SortedDictionary<DateOnly, double> s)
    {
        if (s.Count < 4) return double.NaN;
        var k = s.Keys.ToList();
        var m = k[^1];
        if (k[^2] != m.AddMonths(-1) || k[^3] != m.AddMonths(-2) || k[^4] != m.AddMonths(-3)) return double.NaN;
        return (s[m] - s[k[^4]]) / 3.0;
    }

    /// <summary>M8: the 4-week mean of claims ÷ the lowest 4-week mean of the previous 52 weeks, − 1.</summary>
    public static double ClaimsRatio(SortedDictionary<DateOnly, double> s)
    {
        var v = s.Values.ToList();
        if (v.Count < 56) return double.NaN;
        double Mean4(int end) => (v[end] + v[end - 1] + v[end - 2] + v[end - 3]) / 4.0;
        int n = v.Count - 1;
        double now = Mean4(n), min = double.MaxValue;
        for (int k = 1; k <= 52; k++) min = Math.Min(min, Mean4(n - k));
        return now / min - 1;
    }
}
