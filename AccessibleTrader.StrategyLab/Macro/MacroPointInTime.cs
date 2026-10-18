using System.Globalization;
using System.Text;

namespace AccessibleTrader.StrategyLab.Macro;

/// <summary>
/// One row of an ALFRED "observations by real-time period" file: the value <see cref="Value"/>
/// for observation period <see cref="Period"/> was what the publisher showed from
/// <see cref="RealtimeStart"/> through <see cref="RealtimeEnd"/> inclusive. A null end means the
/// value is still current. A null value is ALFRED's "." (the period existed in that vintage but
/// had no number).
/// </summary>
public sealed record VintageRow(DateOnly Period, double? Value, DateOnly RealtimeStart, DateOnly? RealtimeEnd)
{
    /// <summary>True when this row is the value a reader saw on <paramref name="day"/>.</summary>
    public bool ValidOn(DateOnly day) =>
        RealtimeStart <= day && (RealtimeEnd is null || day <= RealtimeEnd.Value);
}

/// <summary>Per-period summary: what was printed first, when, and what the number is now.</summary>
public sealed record FirstRelease(
    DateOnly Period,
    double FirstValue,
    DateOnly FirstReleaseDate,
    double? LatestValue,
    DateOnly LatestVintageDate,
    int Versions,
    bool FirstIsInitialPrint);

/// <summary>One observation of a series without vintages, with the first day it is safe to use.</summary>
public sealed record KnownValue(DateOnly Date, double Value, DateOnly KnownFrom);

/// <summary>
/// The point-in-time core of the macro data layer. Everything a study needs to ask "what did
/// a reader know on day D" without look-ahead lives here, as pure functions over parsed rows,
/// so the test suite can pin it without the network.
///
/// <para><b>The day convention.</b> Every "known" date in this layer means: known by the END of
/// that calendar day (US Eastern for US releases). An 8:30 ET release on day D is known on D;
/// a 4:30 pm ET release (the Fed's H.4.1) is known on D too, but only just, so it is usable at
/// D's close and not at D's open. A lab study should therefore read macro data at bar i's close
/// with <c>knownFrom &lt;= date(i)</c> and act at bar i+1, which is the lab's standing rule
/// anyway. A study that acts at bar i's OPEN must use <c>knownFrom &lt; date(i)</c>.</para>
///
/// <para><b>Why vintages and not the latest file.</b> FRED's ordinary download is the latest
/// vintage: payrolls for March 2020 as revised through today, not as printed in April 2020. A
/// study that reads the latest file at a past date is reading numbers nobody had. ALFRED keeps
/// every vintage; <see cref="AsOf"/> reconstructs the series as it stood on any day.</para>
///
/// <para><b>Why "first print" needs its own flag.</b> ALFRED's archive for a series starts at
/// some vintage. Periods older than that first archived vintage first appear in it with an
/// already-revised value, and periods that were back-filled later (PAYEMS 1939 first appears in
/// a 1961 vintage) look like "first releases" decades late. <see cref="FirstReleases"/> marks a
/// period's first archived value as a genuine initial print only when it is not in the archive's
/// first vintage and, just before it appeared, every period the archive already held was EARLIER
/// than it — i.e. it arrived as the new observation at the frontier, not as a back-fill.</para>
/// </summary>
public static class MacroPointInTime
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── Parsing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses ALFRED's <c>obs._by_real-time_period.csv</c>:
    /// <c>period_start_date,&lt;SERIES&gt;,realtime_start_date,realtime_end_date</c>, end blank when
    /// current, value "." when missing. Rows are returned sorted by (period, realtime start).
    /// Throws on a malformed line rather than skipping it: a silently dropped vintage row is a
    /// silently wrong as-of value.
    /// </summary>
    public static List<VintageRow> ParseAlfredRealtimeCsv(string text)
    {
        var rows = new List<VintageRow>();
        using var reader = new StringReader(text);
        string? header = reader.ReadLine();
        if (header == null || !header.StartsWith("period_start_date,", StringComparison.Ordinal))
            throw new FormatException($"not an ALFRED real-time-period CSV (header '{header}')");
        string? line;
        int lineNo = 1;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            if (line.Length == 0) continue;
            var f = line.Split(',');
            if (f.Length != 4) throw new FormatException($"line {lineNo}: expected 4 fields, got {f.Length}");
            rows.Add(new VintageRow(
                DateOnly.ParseExact(f[0], "yyyy-MM-dd", Inv),
                ParseValue(f[1]),
                DateOnly.ParseExact(f[2], "yyyy-MM-dd", Inv),
                f[3].Length == 0 ? null : DateOnly.ParseExact(f[3], "yyyy-MM-dd", Inv)));
        }
        rows.Sort(CompareRows);
        return rows;
    }

    /// <summary>Parses a FRED/ALFRED graph CSV (<c>observation_date,&lt;ID&gt;</c>); "." and blanks are skipped.</summary>
    public static List<(DateOnly Date, double Value)> ParseFredGraphCsv(string text)
    {
        var rows = new List<(DateOnly, double)>();
        using var reader = new StringReader(text);
        string? header = reader.ReadLine();
        if (header == null || !header.StartsWith("observation_date,", StringComparison.Ordinal))
            throw new FormatException($"not a FRED graph CSV (header '{header}')");
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            var f = line.Split(',');
            if (f.Length != 2) throw new FormatException($"expected 2 fields: '{line}'");
            var v = ParseValue(f[1]);
            if (v is null) continue;
            rows.Add((DateOnly.ParseExact(f[0], "yyyy-MM-dd", Inv), v.Value));
        }
        return rows;
    }

    private static double? ParseValue(string s) =>
        s.Length == 0 || s == "." ? null : double.Parse(s, NumberStyles.Float, Inv);

    private static int CompareRows(VintageRow a, VintageRow b)
    {
        int c = a.Period.CompareTo(b.Period);
        return c != 0 ? c : a.RealtimeStart.CompareTo(b.RealtimeStart);
    }

    /// <summary>
    /// Joins real-time files downloaded for consecutive slices of the vintage list. ALFRED
    /// stamps every row with its true real-time window, so a value current across a slice
    /// boundary arrives twice (once per slice) and is joined back into one row. Two rows for
    /// the same period whose windows overlap with DIFFERENT values cannot both be right, and
    /// throw.
    /// </summary>
    public static List<VintageRow> MergeChunks(IEnumerable<IEnumerable<VintageRow>> chunks)
    {
        var all = chunks.SelectMany(c => c).ToList();
        all.Sort(CompareRows);
        var merged = new List<VintageRow>(all.Count);
        foreach (var r in all)
        {
            if (merged.Count > 0 && merged[^1].Period == r.Period)
            {
                var last = merged[^1];
                bool touches = last.RealtimeEnd is null || last.RealtimeEnd.Value.AddDays(1) >= r.RealtimeStart;
                if (touches && last.Value == r.Value)
                {
                    merged[^1] = last with { RealtimeEnd = last.RealtimeEnd is null || r.RealtimeEnd is null ? null
                        : (last.RealtimeEnd.Value > r.RealtimeEnd.Value ? last.RealtimeEnd : r.RealtimeEnd) };
                    continue;
                }
                if (last.RealtimeEnd is null || last.RealtimeEnd.Value >= r.RealtimeStart)
                    throw new FormatException(
                        $"period {last.Period:yyyy-MM-dd}: overlapping real-time windows with different values ({last.RealtimeStart:yyyy-MM-dd} and {r.RealtimeStart:yyyy-MM-dd})");
            }
            merged.Add(r);
        }
        return merged;
    }

    // ── Point-in-time queries ────────────────────────────────────────────────

    /// <summary>
    /// The series as a reader saw it at the end of <paramref name="day"/>: for each period, the
    /// value whose real-time window contains that day. Periods not yet published on that day are
    /// absent. A row is NEVER returned before its <see cref="VintageRow.RealtimeStart"/>.
    /// </summary>
    public static SortedDictionary<DateOnly, double> AsOf(IEnumerable<VintageRow> rows, DateOnly day)
    {
        var result = new SortedDictionary<DateOnly, double>();
        foreach (var r in rows)
        {
            if (!r.ValidOn(day) || r.Value is null) continue;
            result[r.Period] = r.Value.Value;
        }
        return result;
    }

    /// <summary>The newest period a reader had on <paramref name="day"/>, with its value then, or null.</summary>
    public static (DateOnly Period, double Value)? LatestKnown(IEnumerable<VintageRow> rows, DateOnly day)
    {
        (DateOnly, double)? best = null;
        foreach (var r in rows)
        {
            if (!r.ValidOn(day) || r.Value is null) continue;
            if (best is null || r.Period > best.Value.Item1) best = (r.Period, r.Value.Value);
        }
        return best;
    }

    /// <summary>
    /// First value printed for each period, its release date, the current value and how many
    /// distinct versions there have been. See the class remarks for <see cref="FirstRelease.FirstIsInitialPrint"/>.
    /// </summary>
    public static List<FirstRelease> FirstReleases(IReadOnlyList<VintageRow> rows)
    {
        var withValues = rows.Where(r => r.Value is not null).ToList();
        if (withValues.Count == 0) return new();

        // Every vintage date the archive has, ascending, and the newest period visible in each.
        var vintageDates = withValues.Select(r => r.RealtimeStart).Distinct().OrderBy(d => d).ToList();
        DateOnly firstVintage = vintageDates[0];

        // Frontier before a date: the newest period whose first appearance is strictly earlier.
        var firstSeen = withValues
            .GroupBy(r => r.Period)
            .ToDictionary(g => g.Key, g => g.Min(r => r.RealtimeStart));
        var byFirstSeen = firstSeen.OrderBy(kv => kv.Value).ToList();
        var frontierBefore = new Dictionary<DateOnly, DateOnly?>();
        DateOnly? running = null;
        int k = 0;
        foreach (var d in vintageDates)
        {
            while (k < byFirstSeen.Count && byFirstSeen[k].Value < d)
            {
                if (running is null || byFirstSeen[k].Key > running) running = byFirstSeen[k].Key;
                k++;
            }
            frontierBefore[d] = running;
        }
        // The current value may be "." (withdrawn), so it is read from every row, not just valued ones.
        var current = rows.Where(r => r.RealtimeEnd is null)
                          .GroupBy(r => r.Period)
                          .ToDictionary(g => g.Key, g => g.OrderBy(r => r.RealtimeStart).Last().Value);

        var result = new List<FirstRelease>();
        foreach (var g in withValues.GroupBy(r => r.Period).OrderBy(g => g.Key))
        {
            var ordered = g.OrderBy(r => r.RealtimeStart).ToList();
            var first = ordered[0];
            var last = ordered[^1];
            var latestValue = current.TryGetValue(g.Key, out var cv) ? cv : null;
            // Nothing in the archive's first vintage counts: there is no earlier vintage to show
            // whether it arrived as news or was already old. ALFRED's own "initial release only"
            // download makes the same cut, and the fetcher checks the two agree.
            bool initial = first.RealtimeStart != firstVintage
                && frontierBefore[first.RealtimeStart] is { } f && f < g.Key;
            result.Add(new FirstRelease(g.Key, first.Value!.Value, first.RealtimeStart,
                latestValue, last.RealtimeStart,
                ordered.Select(r => r.Value).Distinct().Count(), initial));
        }
        return result;
    }

    /// <summary>The newest observation whose <see cref="KnownValue.KnownFrom"/> is on or before
    /// <paramref name="day"/>. <paramref name="series"/> must be sorted by date.</summary>
    public static KnownValue? LatestKnown(IReadOnlyList<KnownValue> series, DateOnly day)
    {
        KnownValue? best = null;
        foreach (var v in series)
            if (v.KnownFrom <= day && (best is null || v.Date > best.Date)) best = v;
        return best;
    }

    // ── Release-lag rules for series that have no vintages ───────────────────

    /// <summary>
    /// The first day an observation dated <paramref name="obs"/> is safe to use, for series
    /// whose publication date is not recorded. Each rule is the publisher's documented schedule
    /// plus a one-day margin for holidays where a holiday can push the release. These are
    /// deliberately late rather than early: a rule that is a day late costs a day of signal; a
    /// rule that is a day early is look-ahead.
    /// </summary>
    public static DateOnly KnownFrom(DateOnly obs, ReleaseRule rule) => rule switch
    {
        // A market close (yields, spreads, VIX): known at the end of its own day.
        ReleaseRule.MarketClose => obs,
        // NY Fed publishes effective fed funds for day D at ~9:00 ET the next business day.
        ReleaseRule.NextBusinessDay => NextWeekday(obs).AddDays(1),
        // H.4.1: Wednesday level, released Thursday 4:30 pm ET (Friday after a holiday).
        ReleaseRule.H41Weekly => obs.AddDays(2),
        // H.10 broad dollar: the week's daily values post the following Monday.
        ReleaseRule.H10Weekly => NextMonday(obs).AddDays(1),
        // Chicago Fed NFCI: week ending Friday, released the following Wednesday 8:30 ET.
        ReleaseRule.NfciWeekly => obs.AddDays(6),
        // St. Louis Fed STLFSI4: week ending Friday, released the following Thursday.
        ReleaseRule.StlfsiWeekly => obs.AddDays(7),
        // A monthly average stamped on the 1st (FEDFUNDS): complete after the month ends.
        ReleaseRule.MonthlyAverageAfterMonthEnd => obs.AddMonths(1).AddDays(1),
        // On-chain day D (UTC) is computed after the day closes and published hours later.
        ReleaseRule.OnChainNextDay => obs.AddDays(1),
        // AAII posts each Thursday's survey result that day; the sheet is dated the release.
        ReleaseRule.SameDayRelease => obs,
        ReleaseRule.Unknown => DateOnly.MaxValue,
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
    };

    /// <summary>
    /// For a series that publishes its newest observation in every release (NFCI: each
    /// Wednesday's release carries the week that ended the Friday before), the observation
    /// first appeared in the first release dated AFTER it. That catches the weeks a schedule
    /// rule gets wrong — releases skipped over Christmas, New Year and Thanksgiving put two
    /// weeks into one later release — and it is never allowed to be earlier than the schedule.
    /// Observations older than the archive's first release fall back to <paramref name="rule"/>.
    /// </summary>
    public static DateOnly KnownFromReleaseDates(DateOnly obs, IReadOnlyList<DateOnly> sortedReleases, DateOnly rule)
    {
        if (sortedReleases.Count == 0 || obs < sortedReleases[0].AddDays(-14)) return rule;
        int lo = 0, hi = sortedReleases.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sortedReleases[mid] <= obs) lo = mid + 1; else hi = mid;
        }
        if (lo >= sortedReleases.Count) return DateOnly.MaxValue; // after the last release: not yet published
        return sortedReleases[lo] > rule ? sortedReleases[lo] : rule;
    }

    private static DateOnly NextWeekday(DateOnly d)
    {
        var n = d.AddDays(1);
        while (n.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) n = n.AddDays(1);
        return n;
    }

    private static DateOnly NextMonday(DateOnly d)
    {
        var n = d.AddDays(1);
        while (n.DayOfWeek != DayOfWeek.Monday) n = n.AddDays(1);
        return n;
    }

    // ── Normalized CSV formats (what lands in strategy-lab-data/macro) ───────

    public const string VintageCsvHeader = "period,value,realtime_start,realtime_end";
    public const string FirstReleaseCsvHeader =
        "period,first_value,first_release_date,latest_value,latest_vintage_date,versions,first_is_initial_print";
    public const string KnownCsvHeader = "date,value,known_from";

    public static string ToVintageCsv(IEnumerable<VintageRow> rows)
    {
        var sb = new StringBuilder(VintageCsvHeader).Append('\n');
        foreach (var r in rows)
            sb.Append(D(r.Period)).Append(',').Append(V(r.Value)).Append(',')
              .Append(D(r.RealtimeStart)).Append(',').Append(r.RealtimeEnd is { } e ? D(e) : "").Append('\n');
        return sb.ToString();
    }

    public static string ToFirstReleaseCsv(IEnumerable<FirstRelease> rows)
    {
        var sb = new StringBuilder(FirstReleaseCsvHeader).Append('\n');
        foreach (var r in rows)
            sb.Append(D(r.Period)).Append(',').Append(V(r.FirstValue)).Append(',').Append(D(r.FirstReleaseDate))
              .Append(',').Append(V(r.LatestValue)).Append(',').Append(D(r.LatestVintageDate))
              .Append(',').Append(r.Versions.ToString(Inv)).Append(',').Append(r.FirstIsInitialPrint ? "true" : "false")
              .Append('\n');
        return sb.ToString();
    }

    public static string ToKnownCsv(IEnumerable<KnownValue> rows)
    {
        var sb = new StringBuilder(KnownCsvHeader).Append('\n');
        foreach (var r in rows)
            sb.Append(D(r.Date)).Append(',').Append(V(r.Value)).Append(',')
              .Append(r.KnownFrom == DateOnly.MaxValue ? "" : D(r.KnownFrom)).Append('\n');
        return sb.ToString();
    }

    /// <summary>Reads a normalized <c>period,value,realtime_start,realtime_end</c> file back.</summary>
    public static List<VintageRow> ParseVintageCsv(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != VintageCsvHeader)
            throw new FormatException("not a normalized vintage CSV");
        var rows = new List<VintageRow>(lines.Length - 1);
        foreach (var raw in lines.Skip(1))
        {
            var f = raw.TrimEnd('\r').Split(',');
            if (f.Length != 4) throw new FormatException($"expected 4 fields: '{raw}'");
            rows.Add(new VintageRow(DateOnly.ParseExact(f[0], "yyyy-MM-dd", Inv), ParseValue(f[1]),
                DateOnly.ParseExact(f[2], "yyyy-MM-dd", Inv),
                f[3].Length == 0 ? null : DateOnly.ParseExact(f[3], "yyyy-MM-dd", Inv)));
        }
        rows.Sort(CompareRows);
        return rows;
    }

    /// <summary>Reads a normalized <c>date,value,known_from</c> file back. A blank known_from
    /// (no defensible release date) loads as <see cref="DateOnly.MaxValue"/>, so it is never served.</summary>
    public static List<KnownValue> ParseKnownCsv(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != KnownCsvHeader)
            throw new FormatException("not a normalized date,value,known_from CSV");
        var rows = new List<KnownValue>(lines.Length - 1);
        foreach (var raw in lines.Skip(1))
        {
            var f = raw.TrimEnd('\r').Split(',');
            if (f.Length != 3) throw new FormatException($"expected 3 fields: '{raw}'");
            rows.Add(new KnownValue(DateOnly.ParseExact(f[0], "yyyy-MM-dd", Inv),
                double.Parse(f[1], NumberStyles.Float, Inv),
                f[2].Length == 0 ? DateOnly.MaxValue : DateOnly.ParseExact(f[2], "yyyy-MM-dd", Inv)));
        }
        rows.Sort((a, b) => a.Date.CompareTo(b.Date));
        return rows;
    }

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    private static string V(double? v) => v is { } x ? x.ToString("R", Inv) : ".";
}

/// <summary>How the first safe-to-use day of a no-vintage observation is derived. See
/// <see cref="MacroPointInTime.KnownFrom"/>.</summary>
public enum ReleaseRule
{
    MarketClose,
    NextBusinessDay,
    H41Weekly,
    H10Weekly,
    NfciWeekly,
    StlfsiWeekly,
    MonthlyAverageAfterMonthEnd,
    OnChainNextDay,
    SameDayRelease,
    /// <summary>No defensible release date (e.g. a recession flag assigned years later). Never served.</summary>
    Unknown,
}
