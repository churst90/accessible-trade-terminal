using System.Globalization;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.StrategyLab.Macro;

/// <summary>
/// <c>macro-fetch --only cftc</c>: the CFTC Traders in Financial Futures (TFF) history for the
/// E-mini S&amp;P 500, the Nasdaq-100 mini and CME Bitcoin, plus the disaggregated report for COMEX
/// gold, from the public annual archives on cftc.gov. Added 2026-10-18 for the cycle school (its
/// positioning metric Q3, <c>docs/CYCLE_LOW_INDICATOR_DESIGN.md</c> 5.4).
///
/// <para><b>Contracts are chosen by CFTC contract code, never by name.</b> The older
/// <c>cftc-cot</c> downloader matched a substring of the market name and kept the first row per
/// week, so "BITCOIN" could be CME's contract, the micro contract or Cboe's 2017-19 contract
/// depending on row order. Codes do not change when the CFTC renames a market (it did in 2022).</para>
///
/// <para><b>Stamped at the release, not the report date.</b> <c>known_from</c> is
/// <see cref="CftcReleaseCalendar.KnownFrom"/>: the Friday release, moved for holidays, shutdowns
/// and the 2023 ION incident, and never before the day each report's history was first posted.</para>
///
/// <para>It also checks every existing <c>xs_cftc_*</c> file against the archives and records which
/// report, contract and trader group each one actually holds (manifest <c>checks</c>).</para>
/// </summary>
public static partial class MacroFetchCommand
{
    private sealed record CftcContract(string Key, string Code, string Report, string What);

    private static readonly CftcContract[] CftcContracts =
    {
        new("sp500_emini", "13874A", "tff", "E-mini S&P 500 (CME)"),
        new("nasdaq100_mini", "209742", "tff", "Nasdaq-100 mini (CME)"),
        new("bitcoin_cme", "133741", "tff", "Bitcoin (CME, 5 BTC)"),
        new("gold_comex", "088691", "disagg", "Gold (COMEX)"),
    };

    private static readonly (string File, string Why)[] CftcPages =
    {
        ("https://www.cftc.gov/MarketReports/CommitmentsofTraders/HistoricalSpecialAnnouncements/index.htm", "special_announcements.html"),
        ("https://www.cftc.gov/MarketReports/CommitmentsofTraders/ReleaseSchedule/index.htm", "release_schedule.html"),
        ("https://www.cftc.gov/PressRoom/PressReleases/6745-13", "pr_6745-13.html"),
        ("https://www.cftc.gov/PressRoom/PressReleases/7864-19", "pr_7864-19.html"),
        ("https://www.cftc.gov/PressRoom/PressReleases/9138-25", "pr_9138-25.html"),
        ("https://www.cftc.gov/PressRoom/PressReleases/9147-25", "pr_9147-25.html"),
    };

    /// <summary>One parsed archive row, the fields the lab uses.</summary>
    internal sealed record CotRow(string Code, string Name, DateOnly Report, double Oi,
        double DealerL, double DealerS, double AssetL, double AssetS, double LevL, double LevS, double MmL, double MmS);

    private static async Task Cftc(Ctx ctx)
    {
        Console.WriteLine();
        Console.WriteLine("== CFTC COT archives: TFF (financial) and disaggregated (gold), by contract code ==");
        foreach (var (url, file) in CftcPages)
            await Raw(ctx, $"cftc/raw/{file}", "cftc", file, url,
                validate: b => Encoding.UTF8.GetString(b).Contains("Commitments", StringComparison.OrdinalIgnoreCase) ||
                               Encoding.UTF8.GetString(b).Contains("COT", StringComparison.Ordinal) ? null : "page does not mention the COT");

        int lastYear = DateTime.UtcNow.Year;
        var archives = new List<(string Report, string Url, string Rel)>
        {
            ("tff", "https://www.cftc.gov/files/dea/history/fin_fut_txt_2006_2016.zip", "cftc/raw/fin_fut_txt_2006_2016.zip"),
            ("disagg", "https://www.cftc.gov/files/dea/history/fut_disagg_txt_hist_2006_2016.zip", "cftc/raw/fut_disagg_txt_hist_2006_2016.zip"),
        };
        for (int y = 2017; y <= lastYear; y++)
        {
            archives.Add(("tff", $"https://www.cftc.gov/files/dea/history/fut_fin_txt_{y}.zip", $"cftc/raw/fut_fin_txt_{y}.zip"));
            archives.Add(("disagg", $"https://www.cftc.gov/files/dea/history/fut_disagg_txt_{y}.zip", $"cftc/raw/fut_disagg_txt_{y}.zip"));
        }
        var rows = new Dictionary<string, List<CotRow>> { ["tff"] = new(), ["disagg"] = new() };
        DateTime? retrieved = null;
        foreach (var (report, url, rel) in archives)
        {
            var bytes = await Raw(ctx, rel, "cftc", Path.GetFileName(rel), url,
                validate: b => b.Length > 4 && b[0] == 'P' && b[1] == 'K' ? null : $"not a zip: '{Snippet(b)}'");
            if (bytes == null) return;
            retrieved ??= Retrieved(ctx, rel);
            try { rows[report].AddRange(ParseCotZip(bytes, report)); }
            catch (Exception ex) { ctx.Fail("cftc", rel, $"archive did not parse: {ex.Message}"); return; }
        }

        // One value per (code, report date). The 2006-2016 file and a yearly file never overlap,
        // but a duplicate would be a parse error, so it is checked rather than assumed.
        var byCode = new Dictionary<string, Dictionary<string, SortedDictionary<DateOnly, CotRow>>>();
        foreach (var (report, list) in rows)
        {
            var map = byCode[report] = new();
            foreach (var r in list)
            {
                if (!map.TryGetValue(r.Code, out var s)) map[r.Code] = s = new();
                if (s.TryGetValue(r.Report, out var prev) && prev != r)
                {
                    ctx.Fail("cftc", r.Code, $"two different {report} rows for {r.Report:yyyy-MM-dd}");
                    return;
                }
                s[r.Report] = r;
            }
        }

        foreach (var c in CftcContracts)
        {
            if (!byCode[c.Report].TryGetValue(c.Code, out var series) || series.Count == 0)
            {
                ctx.Fail("cftc", c.Code, $"no {c.Report} rows for contract code {c.Code}");
                continue;
            }
            var kind = c.Report == "tff" ? CftcReleaseCalendar.Report.Tff : CftcReleaseCalendar.Report.Disaggregated;
            var names = string.Join("; ", series.Values.Select(r => r.Name).Distinct());
            var groups = c.Report == "tff"
                ? new (string Name, Func<CotRow, double> Net, string What)[]
                {
                    ("lev_funds", r => r.LevL - r.LevS, "leveraged funds (hedge funds, CTAs: the TFF report's speculator group)"),
                    ("asset_mgr", r => r.AssetL - r.AssetS, "asset managers and institutions"),
                    ("dealer", r => r.DealerL - r.DealerS, "dealers and intermediaries"),
                }
                : new (string, Func<CotRow, double>, string)[]
                {
                    ("managed_money", r => r.MmL - r.MmS, "managed money (the disaggregated report's speculator group)"),
                };
            foreach (var (g, net, what) in groups)
            {
                var known = series.Values.Where(r => r.Oi > 0)
                    .Select(r => new KnownValue(r.Report, net(r) / r.Oi * 100.0, CftcReleaseCalendar.KnownFrom(r.Report, kind))).ToList();
                Norm(ctx, $"cftc/{c.Report}_{c.Key}_{g}_net_pct_oi.csv", MacroPointInTime.ToKnownCsv(known), "cftc", $"{c.Code}_{g}",
                    "release-rule",
                    "CFTC release (Friday 3:30 pm ET after the Tuesday report date; holiday, shutdown and ION delays from the CFTC's announcements; " +
                    $"never before {(kind == CftcReleaseCalendar.Report.Tff ? "2010-12-03, when TFF history was first posted" : "2010-05-07, when disaggregated history was completed")})",
                    $"{c.What}, contract code {c.Code}, futures only. Net long of {what} as % of total open interest " +
                    $"((long - short) / open interest * 100; spreads excluded). date = report (as-of) date. Market names seen: {names}.",
                    retrieved, null);
            }
            Console.WriteLine($"  {c.Key} ({c.Code}): {series.Count} weeks {series.Keys.First():yyyy-MM-dd} → {series.Keys.Last():yyyy-MM-dd}");
        }

        CheckExistingXsCftc(ctx, byCode);
    }

    internal static List<CotRow> ParseCotZip(byte[] zip, string report)
    {
        using var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entry = z.Entries.Single(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var header = SplitCsv(reader.ReadLine() ?? throw new FormatException("empty archive"));
        int Col(string name)
        {
            int i = Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new FormatException($"no column {name}");
            return i;
        }
        int name = Col("Market_and_Exchange_Names"), yymmdd = Col("As_of_Date_In_Form_YYMMDD"),
            code = Col("CFTC_Contract_Market_Code"), oi = Col("Open_Interest_All");
        int dl = -1, ds = -1, al = -1, ash = -1, ll = -1, ls = -1, ml = -1, ms = -1;
        if (report == "tff")
        {
            dl = Col("Dealer_Positions_Long_All"); ds = Col("Dealer_Positions_Short_All");
            al = Col("Asset_Mgr_Positions_Long_All"); ash = Col("Asset_Mgr_Positions_Short_All");
            ll = Col("Lev_Money_Positions_Long_All"); ls = Col("Lev_Money_Positions_Short_All");
        }
        else
        {
            ml = Col("M_Money_Positions_Long_All"); ms = Col("M_Money_Positions_Short_All");
        }
        double Num(string[] f, int i) => i < 0 ? double.NaN
            : double.TryParse(f[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        var list = new List<CotRow>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            var f = SplitCsv(line);
            if (f.Length < header.Length - 2) throw new FormatException($"short row ({f.Length} fields)");
            var d = DateOnly.ParseExact(f[yymmdd].Trim(), "yyMMdd", CultureInfo.InvariantCulture);
            list.Add(new CotRow(f[code].Trim(), f[name].Trim(), d, Num(f, oi),
                Num(f, dl), Num(f, ds), Num(f, al), Num(f, ash), Num(f, ll), Num(f, ls), Num(f, ml), Num(f, ms)));
        }
        return list;
    }

    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool q = false;
        foreach (var ch in line)
        {
            if (ch == '"') { q = !q; continue; }
            if (ch == ',' && !q) { fields.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    /// <summary>
    /// What each existing <c>xs_cftc_*.json</c> really holds: for every (report, contract code,
    /// trader group) series in the archives, and for the file's timestamps read as the report date
    /// or as the report date + 3 or + 4 days, the share of the file's points equal to the series
    /// (to 1e-6). The best match is recorded. Read-only: the xs files are never touched.
    /// </summary>
    private static void CheckExistingXsCftc(Ctx ctx, Dictionary<string, Dictionary<string, SortedDictionary<DateOnly, CotRow>>> byCode)
    {
        var candidates = new List<(string Label, Dictionary<DateOnly, double> Values)>();
        foreach (var (report, map) in byCode)
            foreach (var (code, series) in map)
            {
                var name = series.Values.Last().Name;
                var groups = report == "tff"
                    ? new (string, Func<CotRow, double>)[] { ("lev_funds", r => r.LevL - r.LevS), ("asset_mgr", r => r.AssetL - r.AssetS), ("dealer", r => r.DealerL - r.DealerS) }
                    : new (string, Func<CotRow, double>)[] { ("managed_money", r => r.MmL - r.MmS) };
                foreach (var (g, net) in groups)
                    candidates.Add(($"{report} {code} {name} {g}",
                        series.Values.Where(r => r.Oi > 0 && !double.IsNaN(net(r))).ToDictionary(r => r.Report, r => net(r) / r.Oi * 100.0)));
            }

        foreach (var path in Directory.GetFiles(ctx.DataRoot, "xs_cftc_*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var doc = JObject.Parse(File.ReadAllText(path));
            var pts = (doc["Points"] as JArray ?? new JArray())
                .Select(p => (Day: DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds((long)p["Ts"]!).UtcDateTime), V: (double)p["Value"]!))
                .ToList();
            (string Label, int Shift, int Hits) best = ("none", 0, 0);
            foreach (var (label, values) in candidates)
                foreach (var shift in new[] { 0, 3, 4 })
                {
                    int hits = 0;
                    foreach (var (day, v) in pts)
                        if (values.TryGetValue(day.AddDays(-shift), out var x) && Math.Abs(x - v) <= 1e-6 * Math.Max(1, Math.Abs(x))) hits++;
                    if (hits > best.Hits) best = (label, shift, hits);
                }
            var dows = string.Join(", ", pts.GroupBy(p => p.Day.DayOfWeek).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"));
            var file = Path.GetFileName(path);
            ctx.Checks.Add(new
            {
                source = "cftc", series = file, check = "existing xs_cftc file against the CFTC archives (best-matching report, contract code and trader group)",
                points = pts.Count, first = pts.Count > 0 ? pts[0].Day.ToString("yyyy-MM-dd") : null, last = pts.Count > 0 ? pts[^1].Day.ToString("yyyy-MM-dd") : null,
                stampDays = dows, bestMatch = best.Label, stampMinusReportDays = best.Shift, matching = best.Hits,
            });
            Console.WriteLine($"  check {file}: {pts.Count} points, stamps {dows}; best {best.Label} at stamp - {best.Shift} days: {best.Hits}/{pts.Count}");
        }
    }
}
