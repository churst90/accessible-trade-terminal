using System.Globalization;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Analyst revision breadth: when the consensus mix shifts toward buy, does price follow?
///
/// <para>
/// ── Why this claim and not another ──────────────────────────────────────────
/// The earnings-surprise test came back null on ten mega-caps and the write-up said plainly why
/// that was the least favourable possible sample: post-earnings drift is documented in thinly
/// covered small caps, and every name tested was among the most analysed securities in existence.
/// Revision breadth is the natural follow-up because it is a claim about the ANALYSTS rather than
/// about the number they missed — if coverage is what kills surprise, coverage is exactly what
/// revision breadth needs. It is also the last untested item that would justify extending the
/// company-data layer at all, which is why it was queued ahead of anything else.
/// </para>
///
/// <para>
/// ── The measurement ─────────────────────────────────────────────────────────
/// Breadth is the bullish share of the rating mix,
/// <c>(strongBuy + buy) / (strongBuy + buy + hold + sell + strongSell)</c>, and the signal is its
/// CHANGE from the previous observation. The level is not the signal: a stock everyone has always
/// rated a buy carries no news, and using the level would mostly rank sectors.
/// </para>
///
/// <para>
/// ── The reason this is honest ───────────────────────────────────────────────
/// FMP's <c>grades-historical</c> is a MONTHLY SNAPSHOT carrying its own observation date, so the
/// mix attributed to March is the mix as it stood in March. That is the whole reason this endpoint
/// is usable and <c>analyst-estimates</c> is not: the latter returns today's estimates for past
/// periods, which is a restated series and would hand the test the answer.
/// </para>
///
/// <para>
/// <b>One caveat that cannot be resolved from outside the vendor.</b> "Monthly snapshot with a date
/// on it" is what the API presents; whether FMP recorded each row at the time or reconstructed the
/// series later is not visible to us. If it is reconstructed, the point-in-time property is a
/// claim rather than a fact. That is precisely why <c>record</c> exists below — an archive we write
/// ourselves has no such ambiguity.
/// </para>
///
/// <para>
/// ── Sub-commands ────────────────────────────────────────────────────────────
/// <c>fetch</c> pulls what the vendor will give us today. <c>record</c> appends a dated snapshot to
/// a committed forward archive. <c>study</c> runs the cross-sectional test AND states the power it
/// had, which on the free tier is the actual result.
/// </para>
/// </summary>
public static class GradesCommand
{
    private const string GradesUrl = "https://financialmodelingprep.com/stable/grades-historical";

    /// <summary>
    /// Gap between requests. Raised from 400 ms after a record run silently lost twenty of
    /// twenty-one symbols to throttling that looked, from inside the old error handling, exactly
    /// like the whole universe losing analyst coverage overnight.
    /// </summary>
    private const int RequestDelayMs = 1200;

    /// <summary>The rating mix for one symbol as it stood on one date.</summary>
    internal sealed class Row
    {
        [JsonProperty("d")] public string Date { get; set; } = "";
        [JsonProperty("s")] public string Symbol { get; set; } = "";
        [JsonProperty("sb")] public int StrongBuy { get; set; }
        [JsonProperty("b")] public int Buy { get; set; }
        [JsonProperty("h")] public int Hold { get; set; }
        [JsonProperty("sl")] public int Sell { get; set; }
        [JsonProperty("ss")] public int StrongSell { get; set; }

        public int Total => StrongBuy + Buy + Hold + Sell + StrongSell;

        /// <summary>
        /// The bullish share of the mix. Undefined rather than zero when nobody covers the name —
        /// "no analysts" and "all analysts are bearish" are opposite facts and collapsing them would
        /// load every uncovered stock onto the bearish end of the sort.
        /// </summary>
        public double? Breadth => Total == 0 ? null : (double)(StrongBuy + Buy) / Total;
    }

    public static async Task<int> RunAsync(string[] args, string snapshotDir)
    {
        string sub = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "study";
        return sub switch
        {
            "fetch" => await FetchAsync(snapshotDir, Flag(args, "--key"), Flag(args, "--only")),
            "record" => await RecordAsync(Flag(args, "--key"),
                            UniverseRecorderCommand.Anchor(Flag(args, "--out") ?? "grades-archive"),
                            HasFlag(args, "--force")),
            "study" => Study(snapshotDir, int.TryParse(Flag(args, "--horizon"), out var h) ? h : 21),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.WriteLine("  StrategyLab grades fetch  --key <fmp-key> [--only AAPL]");
        Console.WriteLine("  StrategyLab grades record --key <fmp-key> [--out grades-archive] [--force]");
        Console.WriteLine("  StrategyLab grades study  [--horizon 21]");
        Console.WriteLine();
        Console.WriteLine("FMP's free tier caps grades history at 10 rows per symbol and blocks");
        Console.WriteLine("some symbols outright, so `record` is what makes this answerable.");
        return 2;
    }

    // ── Acquisition ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The symbols worth asking about: those we already hold price history for, minus the funds.
    /// An ETF has no analyst rating mix, so requesting one spends a call to be told nothing.
    /// </summary>
    private static readonly string[] Funds =
    {
        "SPY","QQQ","IWM","DIA","VTI","EEM","EFA","FXI","GLD","SLV","TLT","IEF","USO",
        "XLB","XLE","XLF","XLI","XLK","XLP","XLU","XLV","XLY","XAU_USD","SILJ","ARKK","MSOS","VIXY","UNG","XBI"
    };

    internal static List<string> EquityUniverse(string snapshotDir)
    {
        if (!Directory.Exists(snapshotDir)) return new List<string>();

        return Directory.GetFiles(snapshotDir, "*_1d.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(f => f != null)
            .Select(f => f!)
            .Where(f => !f.StartsWith("xs_") && !f.StartsWith("events_") && !f.StartsWith("fred_"))
            .Where(f => f.StartsWith("yahoo_") || f.StartsWith("twelvedata_") || f.StartsWith("alpaca_"))
            .Select(f => string.Join("_", f.Split('_').Skip(1).SkipLast(1)))
            .Where(s => s.Length > 0 && !Funds.Contains(s, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();
    }

    private static async Task<int> FetchAsync(string dir, string? key, string? only)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("grades fetch needs --key <fmp-key>.");
            return 2;
        }

        Directory.CreateDirectory(dir);
        var symbols = EquityUniverse(dir);
        if (only != null)
            symbols = symbols.Where(s => s.Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();

        if (symbols.Count == 0) { Console.Error.WriteLine("No equity symbols found."); return 1; }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        int ok = 0, blocked = 0, empty = 0;

        Console.WriteLine($"Fetching analyst rating mix for {symbols.Count} symbols.");
        Console.WriteLine();

        foreach (var sym in symbols)
        {
            var (status, rows) = await FetchSymbolAsync(http, sym, key!);

            switch (status)
            {
                case FetchStatus.Ok:
                    File.WriteAllText(Path.Combine(dir, $"xs_fmp_{sym}_grades.json"),
                        JsonConvert.SerializeObject(rows, Formatting.Indented));
                    Console.WriteLine($"  {sym,-8} {rows.Count,3} rows  {rows[^1].Date} -> {rows[0].Date}");
                    ok++;
                    break;
                case FetchStatus.Blocked:
                    Console.WriteLine($"  {sym,-8} blocked by subscription tier"); blocked++; break;
                case FetchStatus.NoCoverage:
                    Console.WriteLine($"  {sym,-8} no analyst coverage"); empty++; break;
                default:
                    Console.WriteLine($"  {sym,-8} {status} — retry later"); empty++; break;
            }

            await Task.Delay(RequestDelayMs);
        }

        Console.WriteLine();
        Console.WriteLine($"{ok} written, {blocked} blocked, {empty} uncovered.");
        return ok > 0 ? 0 : 1;
    }

    /// <summary>Why a symbol produced no data. The distinction is the whole point — see below.</summary>
    internal enum FetchStatus { Ok, Blocked, NoCoverage, RateLimited, Error }

    /// <summary>
    /// One symbol's rating history, with the REASON when there is none.
    ///
    /// <para>
    /// The first version collapsed every failure into a null and it cost an archive file: a run
    /// that should have captured twenty-one symbols wrote one, silently, and reported success. Four
    /// completely different facts were wearing the same return value — the tier does not cover this
    /// symbol, no analyst covers this company, we asked too fast, and the network broke. Only the
    /// second is a finding; the rest are reasons to retry or to fix something, and an archive that
    /// cannot tell them apart will later read a throttled afternoon as an industry losing coverage.
    /// </para>
    /// </summary>
    private static async Task<(FetchStatus Status, List<Row> Rows)> FetchSymbolAsync(
        HttpClient http, string symbol, string key)
    {
        try
        {
            string url = $"{GradesUrl}?symbol={Uri.EscapeDataString(symbol)}&limit=10&apikey={key}";
            using var resp = await http.GetAsync(url);
            string body = await resp.Content.ReadAsStringAsync();

            if ((int)resp.StatusCode == 429) return (FetchStatus.RateLimited, new());
            if (!resp.IsSuccessStatusCode) return (FetchStatus.Error, new());

            // FMP answers a paywalled symbol with 200 and a prose sentence, not an error code.
            if (!body.TrimStart().StartsWith("["))
                return (body.Contains("limit", StringComparison.OrdinalIgnoreCase)
                            && body.Contains("rate", StringComparison.OrdinalIgnoreCase)
                        ? FetchStatus.RateLimited
                        : FetchStatus.Blocked, new());

            var rows = JArray.Parse(body).Select(t => new Row
            {
                Date = (string?)t["date"] ?? "",
                Symbol = symbol,
                StrongBuy = (int?)t["analystRatingsStrongBuy"] ?? 0,
                Buy = (int?)t["analystRatingsBuy"] ?? 0,
                Hold = (int?)t["analystRatingsHold"] ?? 0,
                Sell = (int?)t["analystRatingsSell"] ?? 0,
                StrongSell = (int?)t["analystRatingsStrongSell"] ?? 0,
            }).Where(r => r.Date.Length > 0).ToList();

            return (rows.Count > 0 ? FetchStatus.Ok : FetchStatus.NoCoverage, rows);
        }
        catch (Exception)
        {
            return (FetchStatus.Error, new());
        }
    }

    // ── The forward archive ─────────────────────────────────────────────────────

    /// <summary>
    /// Append today's rating mix to a committed archive.
    ///
    /// <para>
    /// This is the part that actually makes the question answerable, and it is here for the same
    /// reason the universe recorder is: the vendor gives ten months and no more, so every month not
    /// recorded is a month permanently outside the sample. Unlike the crypto universe the loss is
    /// not survivorship — these companies do not vanish — it is simply depth, and depth is what the
    /// test lacked. One file per day, never rewritten, gzipped.
    /// </para>
    ///
    /// <para>
    /// It also removes the one caveat we cannot resolve about the vendor's own history: a row we
    /// wrote on the day carries no question about whether it was reconstructed later.
    /// </para>
    /// </summary>
    private static async Task<int> RecordAsync(string? key, string outDir, bool force)
    {
        if (string.IsNullOrWhiteSpace(key)) { Console.Error.WriteLine("grades record needs --key."); return 2; }

        Directory.CreateDirectory(outDir);
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string path = Path.Combine(outDir, $"grades_{today}.jsonl.gz");

        if (File.Exists(path) && !force)
        {
            Console.WriteLine($"{today} already recorded. --force to overwrite (it should not be needed).");
            return 0;
        }

        // The universe is FIXED, not discovered from whatever snapshots happen to be on disk. An
        // archive whose membership drifts with the contents of a gitignored directory cannot be
        // compared against itself later, and the delta would report local housekeeping as companies
        // gaining and losing coverage.
        var symbols = DefaultUniverse.ToList();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var rows = new List<Row>();
        int blocked = 0, transient = 0;

        foreach (var sym in symbols)
        {
            var (status, got) = await FetchSymbolAsync(http, sym, key!);
            switch (status)
            {
                case FetchStatus.Ok: rows.Add(got[0]); break;   // most recent observation only
                case FetchStatus.Blocked or FetchStatus.NoCoverage: blocked++; break;
                default: transient++; break;
            }
            await Task.Delay(RequestDelayMs);
        }

        // ── The coverage floor ──────────────────────────────────────────────────
        //
        // An empty file dated today would read, on any later delta, as universal loss of coverage.
        // But so would a file holding one symbol out of twenty-one, and that is not hypothetical:
        // the first run of this recorder did exactly that and reported success. Permanent absences
        // (paywalled or uncovered symbols) are fine and expected — they are stable, so a delta sees
        // them as nothing at all. TRANSIENT failures are the danger, because they come and go and
        // every appearance looks like news.
        //
        // So the test is on the reachable universe, not the whole one, and a run that lost a third
        // of what it should have reached is refused outright rather than written and explained.
        int reachable = symbols.Count - blocked;
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("Sweep returned nothing — refusing to write an empty snapshot.");
            return 2;
        }
        if (reachable > 0 && rows.Count < reachable * 2 / 3)
        {
            Console.Error.WriteLine(
                $"Only {rows.Count} of {reachable} reachable symbols answered ({transient} transient failures).");
            Console.Error.WriteLine("Refusing to write a partial snapshot — a later delta would read it as lost coverage.");
            Console.Error.WriteLine("Retry in a few minutes; the request spacing may need raising.");
            return 2;
        }

        string tmp = path + ".partial";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        using (var w = new StreamWriter(gz))
            foreach (var r in rows)
                w.WriteLine(JsonConvert.SerializeObject(r));
        File.Move(tmp, path, overwrite: true);

        Console.WriteLine($"Recorded {rows.Count} of {reachable} reachable symbols "
                        + $"({blocked} permanently unavailable) to {Path.GetFileName(path)}.");
        Console.WriteLine($"Archive now holds {Directory.GetFiles(outDir, "grades_*.jsonl.gz").Length} days.");
        return 0;
    }

    private static readonly string[] DefaultUniverse =
        { "AAPL","MSFT","GOOGL","AMZN","TSLA","NVDA","META","JPM","XOM","KO","WMT","PLTR","CAT","CVX","JNJ","MCD","MMM","PFE","PG","T","VZ" };

    // ── The study ───────────────────────────────────────────────────────────────

    private sealed record Obs(string Symbol, DateTime Date, double Breadth);

    private static int Study(string dir, int horizonBars)
    {
        var files = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "xs_fmp_*_grades.json").OrderBy(f => f).ToList()
            : new List<string>();

        if (files.Count == 0)
        {
            Console.Error.WriteLine("No grades files. Run: grades fetch --key <fmp-key>");
            return 1;
        }

        // Load the rating mix, and the prices to measure against.
        var byS = new Dictionary<string, List<Obs>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var rows = JsonConvert.DeserializeObject<List<Row>>(File.ReadAllText(f)) ?? new();
            var obs = rows
                .Where(r => r.Breadth.HasValue && DateTime.TryParse(r.Date, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out _))
                .Select(r => new Obs(r.Symbol, DateTime.Parse(r.Date, CultureInfo.InvariantCulture), r.Breadth!.Value))
                .OrderBy(o => o.Date)
                .ToList();
            if (obs.Count >= 2) byS[obs[0].Symbol] = obs;
        }

        var prices = LoadPrices(dir, byS.Keys);

        Console.WriteLine();
        Console.WriteLine("═════ ANALYST REVISION BREADTH — DOES A SHIFT TOWARD BUY LEAD PRICE? ═════");
        Console.WriteLine($"Signal: change in (strongBuy+buy)/total from one monthly observation to the next.");
        Console.WriteLine($"Forward return measured over {horizonBars} trading bars from the observation date.");
        Console.WriteLine();

        // Build the panel: one row per symbol per month, with the breadth CHANGE and the forward
        // return that followed it. Entry is the first close at or after the observation date —
        // never the close on it, which would assume the mix was known before the day traded.
        var panel = new List<(DateTime Date, string Sym, double Delta, double Fwd)>();
        foreach (var (sym, obs) in byS)
        {
            if (!prices.TryGetValue(sym, out var px)) continue;
            for (int i = 1; i < obs.Count; i++)
            {
                double delta = obs[i].Breadth - obs[i - 1].Breadth;
                double? fwd = ForwardReturn(px, obs[i].Date, horizonBars);
                if (fwd.HasValue) panel.Add((obs[i].Date, sym, delta, fwd.Value));
            }
        }

        var periods = panel.GroupBy(p => p.Date).OrderBy(g => g.Key).ToList();

        Console.WriteLine($"{"symbols with a rating history",-42}{byS.Count,6}");
        Console.WriteLine($"{"of those, with matching price history",-42}{byS.Keys.Count(k => prices.ContainsKey(k)),6}");
        Console.WriteLine($"{"symbol-months with a forward return",-42}{panel.Count,6}");
        Console.WriteLine($"{"distinct monthly cross-sections",-42}{periods.Count,6}");
        Console.WriteLine();

        // The test proper: each month, sort on the breadth change and take the top third minus the
        // bottom third. A cross-sectional spread is the right shape because it holds the market
        // fixed — every name is measured against the others in the same month, so a rising tide
        // cannot be mistaken for a signal.
        var spreads = new List<double>();
        foreach (var g in periods)
        {
            var xs = g.Where(x => Math.Abs(x.Delta) > 0).OrderByDescending(x => x.Delta).ToList();
            if (xs.Count < 6) continue;                       // a tercile of two is not a tercile
            int k = Math.Max(1, xs.Count / 3);
            double top = xs.Take(k).Average(x => x.Fwd);
            double bot = xs.TakeLast(k).Average(x => x.Fwd);
            spreads.Add(top - bot);
        }

        if (spreads.Count == 0)
        {
            Console.WriteLine("── VERDICT ──");
            Console.WriteLine("  NOT RUNNABLE. No month had enough names with a rating change to form terciles.");
            PrintWhyThin();
            return 0;
        }

        double mean = spreads.Average();
        double sd = StdDev(spreads);
        double se = sd / Math.Sqrt(spreads.Count);
        double t = se > 0 ? mean / se : 0;

        Console.WriteLine($"{"months with a usable cross-section",-42}{spreads.Count,6}");
        Console.WriteLine($"{"mean top-minus-bottom tercile spread",-42}{mean * 100,6:F2}%");
        Console.WriteLine($"{"standard deviation across months",-42}{sd * 100,6:F2}%");
        Console.WriteLine($"{"t",-42}{t,6:F2}");
        Console.WriteLine();

        // ── POWER, which on the free tier IS the result ─────────────────────────
        //
        // Reporting a spread and a p-value from a handful of monthly cross-sections invites
        // exactly the mistake this project keeps guarding against: reading noise as a finding, or
        // worse, reading a null as evidence of absence. The minimum detectable effect says what
        // the sample could have seen at all. If the MDE is an implausible number, the test did not
        // fail to find an effect — it was never capable of finding one.
        double mde = 2.8 * se;   // ~80% power, alpha 0.05 two-sided
        Console.WriteLine("── POWER ──");
        Console.WriteLine($"  Smallest monthly spread this sample could detect: {mde * 100:F2}%");
        Console.WriteLine($"  Annualised, that is roughly {mde * 12 * 100:F0}% a year.");
        Console.WriteLine();

        Console.WriteLine("── VERDICT ──");
        if (mde > 0.01)
        {
            Console.WriteLine("  UNDERPOWERED — no conclusion either way.");
            Console.WriteLine($"  A real revision-breadth effect is documented at well under 1% a month.");
            Console.WriteLine($"  This sample cannot see anything smaller than {mde * 100:F2}%, so both a");
            Console.WriteLine( "  positive and a null result here would be uninformative.");
            PrintWhyThin();
        }
        else if (Math.Abs(t) < 2)
        {
            Console.WriteLine("  NULL. The spread is within noise of zero at adequate power.");
        }
        else
        {
            Console.WriteLine($"  SPREAD DETECTED ({mean * 100:F2}% a month, t={t:F2}).");
            Console.WriteLine("  NOT an edge yet: this needs an exposure-matched null, a random-selection");
            Console.WriteLine("  control, costs, and an out-of-sample era before it goes near the registry.");
        }
        Console.WriteLine();
        return 0;
    }

    private static void PrintWhyThin()
    {
        Console.WriteLine();
        Console.WriteLine("  WHY THE SAMPLE IS THIN, AND WHAT FIXES IT");
        Console.WriteLine("    FMP's free tier caps grades-historical at 10 rows per symbol — about nine");
        Console.WriteLine("    usable months — and refuses some symbols outright. Nine monthly cross-sections");
        Console.WriteLine("    over a couple of dozen mega-caps cannot resolve an effect of realistic size.");
        Console.WriteLine();
        Console.WriteLine("    This is a DEPTH problem, not a design one, and it has exactly two fixes:");
        Console.WriteLine("      1. `grades record` monthly — the archive grows one usable period a month");
        Console.WriteLine("         and needs about three years before this test means anything.");
        Console.WriteLine("      2. A paid tier, which buys the history immediately.");
        Console.WriteLine();
        Console.WriteLine("    Until one of those happens the honest status is UNTESTED, not null.");
    }

    // ── Prices ──────────────────────────────────────────────────────────────────

    private static Dictionary<string, List<(DateTime D, double C)>> LoadPrices(string dir, IEnumerable<string> symbols)
    {
        var map = new Dictionary<string, List<(DateTime, double)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in symbols)
        {
            var file = Directory.GetFiles(dir, $"*_{sym}_1d.json")
                .FirstOrDefault(f => !Path.GetFileName(f).StartsWith("xs_"));
            if (file == null) continue;

            try
            {
                var snap = SnapshotCommand.Load(file);
                map[sym] = snap.Bars.Select(b => (b.Date, (double)b.Close)).OrderBy(x => x.Item1).ToList();
            }
            catch { /* a symbol without readable prices simply drops out of the panel */ }
        }
        return map;
    }

    /// <summary>
    /// Return from the first close AT OR AFTER the observation date, forward N bars.
    ///
    /// <para>
    /// At-or-after, never the previous close: the monthly mix is dated the first of the month and
    /// entering on the close before it would hand the rule a day it could not have had. It is the
    /// same next-bar discipline every other command here uses, applied to a monthly signal.
    /// </para>
    /// </summary>
    private static double? ForwardReturn(List<(DateTime D, double C)> px, DateTime from, int bars)
    {
        int i = px.FindIndex(p => p.D >= from);
        if (i < 0 || i + bars >= px.Count) return null;
        double a = px[i].C, b = px[i + bars].C;
        return a > 0 ? b / a - 1.0 : null;
    }

    private static double StdDev(List<double> xs)
    {
        if (xs.Count < 2) return 0;
        double m = xs.Average();
        return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / (xs.Count - 1));
    }

    private static string? Flag(string[] a, string name)
    {
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    private static bool HasFlag(string[] a, string name) => Array.IndexOf(a, name) >= 0;
}
