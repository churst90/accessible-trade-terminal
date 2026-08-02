using System.Globalization;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Records how much of the world's news is about a given theme, every day, forever.
///
/// <para>
/// ── Why record what the API already serves ──────────────────────────────────
/// GDELT's DOC 2.0 timeline is free and keyless and returns about <b>two years</b> of daily history.
/// That sounds like there is nothing to preserve, and it is the reason this was nearly not built.
/// Two things make it worth doing anyway, and only the first is obvious:
/// </para>
///
/// <list type="number">
///   <item>
///     <b>The window rolls.</b> Two years is all there will ever be. Every month not recorded is a
///     month that leaves the reachable sample permanently, and an attention series is only
///     interesting across regimes — which needs more than two years by definition. The cost of
///     starting is one HTTP call a day; the cost of starting three years late is three years.
///   </item>
///   <item>
///     <b>The series is NORMALISED, so it is restated.</b> <c>timelinevol</c> reports each theme's
///     share of all coverage GDELT indexed that day. The denominator is the entire news firehose,
///     and it grows as sources are added and reprocessed — so the value returned today for a date
///     last year is not necessarily the value that would have been returned then. A number that can
///     change after the fact is not point-in-time, and a study built on it inherits a subtle
///     lookahead that no control can remove. Writing our own dated row each day is the only way to
///     obtain a series that is genuinely as-observed.
///   </item>
/// </list>
///
/// <para>
/// ── Why attention at all ────────────────────────────────────────────────────
/// It is the same untested thread as <c>WikipediaPageviewsProvider</c>: a genuinely non-price input,
/// which this project has very few of. Every "non-price" signal tested so far turned out to be
/// momentum wearing a different name — the crowding index correlated 0.19 with trailing return, the
/// volume signal 0.43 to 0.59 — so the FIRST thing any study of this must do is check the
/// correlation against trailing returns before interpreting anything. News volume plausibly fails
/// that test too. Recording it costs nothing; believing it would cost a great deal.
/// </para>
///
/// <para>
/// ── Same archive discipline as the universe recorder ────────────────────────
/// Committed rather than gitignored, gzipped, one immutable file per day, an empty sweep refused,
/// and the observation date stored on every row rather than inferred from the filename. GDELT asks
/// for one request every five seconds and that request budget is respected.
/// </para>
/// </summary>
public static class GdeltRecorderCommand
{
    private const string DocApi = "https://api.gdeltproject.org/api/v2/doc/doc";

    /// <summary>
    /// GDELT publishes a courtesy limit of one request every five seconds. Measured, the throttle
    /// is stickier than that once tripped, so the spacing is doubled and <see cref="FetchAsync"/>
    /// backs off on top of it.
    /// </summary>
    private const int RequestDelayMs = 10_000;

    /// <summary>Ceiling on a single retry wait, so the whole run has a statable worst case.</summary>
    private const int MaxBackoffMs = 30_000;

    /// <summary>
    /// The themes. Fixed, and deliberately short.
    ///
    /// <para>
    /// A fixed list is what makes the archive comparable against itself: a set that grows over time
    /// produces a panel where early dates are missing series, and a later study reads those holes as
    /// attention collapsing rather than as us adding a query. New themes may be appended — never
    /// removed, never re-worded — because changing a query silently changes what the old rows mean.
    /// </para>
    ///
    /// <para>
    /// Chosen to span the things this project already has price series for, plus the macro fears
    /// that would plausibly drive them, so a study can ask whether attention leads price rather than
    /// simply co-moving with it.
    /// </para>
    /// </summary>
    internal static readonly (string Key, string Query)[] Themes =
    {
        ("bitcoin",     "bitcoin"),
        ("ethereum",    "ethereum"),
        ("crypto",      "cryptocurrency"),
        ("stockmarket", "\"stock market\""),
        ("recession",   "recession"),
        ("inflation",   "inflation"),
        ("interestrate","\"interest rates\""),
        ("gold",        "\"gold price\""),
        ("oil",         "\"oil price\""),
        ("layoffs",     "layoffs"),
    };

    /// <summary>One theme's share of global coverage on one day.</summary>
    internal sealed class Row
    {
        [JsonProperty("d")] public string Date { get; set; } = "";
        [JsonProperty("t")] public string Theme { get; set; } = "";
        [JsonProperty("v")] public double Value { get; set; }
    }

    // ── Recording ───────────────────────────────────────────────────────────────

    public static async Task<int> RunAsync(string outDir, bool force, string timespan = "3m")
    {
        // Note the directory is NOT created here. A refused sweep should leave no trace, and an
        // empty archive directory reads to the next person as "recording started and produced
        // nothing" rather than "recording never succeeded".
        outDir = UniverseRecorderCommand.Anchor(outDir);

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string path = Path.Combine(outDir, $"gdelt_{today}.jsonl.gz");

        if (File.Exists(path) && !force)
        {
            Console.WriteLine($"{today} already recorded. --force to overwrite (it should not be needed).");
            return 0;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AccessibleTrader-StrategyLab/2.1 (research)");

        var rows = new List<Row>();
        int failed = 0;

        // State the worst case up front. GDELT's throttle is sticky and this command deliberately
        // waits it out, so without this line a slow run is indistinguishable from a hung one.
        int worstSec = Themes.Length * (RequestDelayMs + 10_000 + MaxBackoffMs) / 1000;
        Console.WriteLine($"Fetching {Themes.Length} themes over {timespan}, one request per {RequestDelayMs / 1000}s.");
        Console.WriteLine($"Expect about {Themes.Length * RequestDelayMs / 1000}s, "
                        + $"or up to {worstSec / 60}m {worstSec % 60}s if GDELT throttles.");
        Console.WriteLine();

        foreach (var (key, query) in Themes)
        {
            Console.Write($"  {key,-14}");
            var series = await FetchAsync(http, query, timespan);
            if (series == null)
            {
                Console.WriteLine(" FAILED after retries");
                failed++;
            }
            else
            {
                // The whole window is stored, not only today's point. It costs almost nothing
                // gzipped and it means a single run bootstraps the archive with real history —
                // and that later runs overlap, which is what makes restatement detectable.
                foreach (var (d, v) in series) rows.Add(new Row { Date = d, Theme = key, Value = v });
                Console.WriteLine($" {series.Count,4} days  {series[0].Date} -> {series[^1].Date}");
            }
            await Task.Delay(RequestDelayMs);
        }

        // Same rule as every other recorder here: a file dated today holding nothing would read, on
        // any later comparison, as the world abruptly ceasing to discuss any of these subjects.
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("Sweep returned nothing — refusing to write an empty snapshot.");
            return 2;
        }
        if (failed > Themes.Length / 2)
        {
            Console.Error.WriteLine($"{failed} of {Themes.Length} themes failed — refusing to write a partial snapshot.");
            return 2;
        }

        Directory.CreateDirectory(outDir);   // only now that there is something worth keeping

        string tmp = path + ".partial";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        using (var w = new StreamWriter(gz))
            foreach (var r in rows)
                w.WriteLine(JsonConvert.SerializeObject(r));
        File.Move(tmp, path, overwrite: true);

        var info = new FileInfo(path);
        Console.WriteLine();
        Console.WriteLine($"Recorded {rows.Count} theme-days ({Themes.Length - failed}/{Themes.Length} themes) "
                        + $"to {Path.GetFileName(path)} — {info.Length / 1024.0:F0} KB.");

        // A missing theme has to be VISIBLE, not merely survivable. It is recoverable here — unlike
        // the crypto universe, GDELT re-serves the whole window on the next run, so tomorrow
        // backfills today's gap — but only if somebody knows the gap exists. Silence about a hole
        // is how a hole becomes permanent.
        var got = rows.Select(r => r.Theme).Distinct().ToHashSet();
        var missing = Themes.Where(t => !got.Contains(t.Key)).Select(t => t.Key).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine($"MISSING: {string.Join(", ", missing)} — throttled out.");
            Console.WriteLine("Re-run later; GDELT re-serves the full window, so the next run backfills these.");
        }

        Console.WriteLine($"Archive now holds {Directory.GetFiles(outDir, "gdelt_*.jsonl.gz").Length} days.");
        return 0;
    }

    /// <summary>
    /// One theme's series, retrying through GDELT's throttle.
    ///
    /// <para>
    /// GDELT answers an over-rate request with <b>HTTP 200 and a plain-text apology</b>, so a naive
    /// client sees a successful response that simply is not JSON. The first version treated that
    /// identically to a genuine failure and reported eight of ten themes as FAILED when in fact the
    /// server had merely asked us to slow down — the same conflation of "transient" with "absent"
    /// that cost the grades recorder twenty of twenty-one symbols an hour earlier. It is worth
    /// naming as a pattern: <b>any recorder that cannot distinguish "not yet" from "not there" will
    /// eventually write a hole into an archive and call it data.</b>
    /// </para>
    ///
    /// <para>
    /// The throttle is also sticky — once tripped it persists past the documented five seconds — so
    /// the backoff doubles rather than retrying at a fixed interval.
    /// </para>
    /// </summary>
    private static async Task<List<(string Date, double V)>?> FetchAsync(
        HttpClient http, string query, string timespan, int attempts = 3)
    {
        // Backoff is capped, and the cap is the point. Doubling without a ceiling makes the total
        // runtime unbounded in practice, and a long-running command that cannot state its own worst
        // case is indistinguishable from a hung one — which is exactly how this was first reported.
        int wait = 10_000;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            var got = await TryFetchOnceAsync(http, query, timespan);
            if (got.Throttled)
            {
                if (attempt == attempts) return null;
                Console.Write($" (throttled, waiting {wait / 1000}s)");
                await Console.Out.FlushAsync();
                await Task.Delay(wait);
                wait = Math.Min(wait * 2, MaxBackoffMs);
                continue;
            }
            return got.Series;
        }
        return null;
    }

    private static async Task<(bool Throttled, List<(string Date, double V)>? Series)> TryFetchOnceAsync(
        HttpClient http, string query, string timespan)
    {
        try
        {
            string url = $"{DocApi}?query={Uri.EscapeDataString(query)}&mode=timelinevol&format=json&timespan={timespan}";
            string body = await http.GetStringAsync(url);

            if (!body.TrimStart().StartsWith("{"))
                return (body.Contains("limit requests", StringComparison.OrdinalIgnoreCase), null);

            var tl = JObject.Parse(body)["timeline"] as JArray;
            var data = tl?.FirstOrDefault()?["data"] as JArray;
            if (data == null) return (false, null);

            var outp = new List<(string, double)>();
            foreach (var p in data)
            {
                string raw = (string?)p["date"] ?? "";
                if (raw.Length < 8) continue;
                // "20260802T000000Z" -> "2026-08-02"
                string iso = $"{raw[..4]}-{raw.Substring(4, 2)}-{raw.Substring(6, 2)}";
                outp.Add((iso, (double?)p["value"] ?? 0));
            }
            return (false, outp.Count > 0 ? outp : null);
        }
        catch (HttpRequestException)
        {
            // A network-level failure is worth one more try, on the same reasoning as the throttle.
            return (true, null);
        }
        catch
        {
            return (false, null);
        }
    }

    // ── Status ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the archive holds, and — the part that justifies its existence — whether the vendor has
    /// restated any value we already recorded.
    /// </summary>
    public static int Status(string outDir)
    {
        outDir = UniverseRecorderCommand.Anchor(outDir);
        if (!Directory.Exists(outDir))
        {
            Console.WriteLine($"No archive at '{outDir}'. Run: record-gdelt");
            return 1;
        }

        var files = Directory.GetFiles(outDir, "gdelt_*.jsonl.gz").OrderBy(f => f).ToList();
        if (files.Count == 0) { Console.WriteLine("Archive is empty. Run: record-gdelt"); return 1; }

        Console.WriteLine();
        Console.WriteLine($"GDELT attention archive: {files.Count} run{(files.Count == 1 ? "" : "s")}, "
                        + $"{files.Sum(f => new FileInfo(f).Length) / 1024.0:F0} KB.");

        var first = LoadRun(files[0]);
        var last = LoadRun(files[^1]);
        Console.WriteLine($"  earliest run {RunDate(files[0])}: {first.Count} theme-days");
        Console.WriteLine($"  latest   run {RunDate(files[^1])}: {last.Count} theme-days");

        var days = last.Select(r => r.Date).Distinct().OrderBy(d => d).ToList();
        if (days.Count > 0) Console.WriteLine($"  coverage {days[0]} -> {days[^1]} ({days.Count} days)");

        // Per-theme completeness across the whole archive. A theme that keeps getting throttled out
        // would otherwise look identical, in any later study, to a theme nobody is writing about.
        Console.WriteLine();
        Console.WriteLine("── THEME COVERAGE ACROSS THE ARCHIVE ──");
        var runsWith = new Dictionary<string, int>();
        foreach (var f in files)
            foreach (var t in LoadRun(f).Select(r => r.Theme).Distinct())
                runsWith[t] = runsWith.GetValueOrDefault(t) + 1;

        foreach (var (key, _) in Themes)
        {
            int n = runsWith.GetValueOrDefault(key);
            Console.WriteLine($"  {key,-14}{n,4}/{files.Count} runs{(n == 0 ? "   <-- never captured" : n < files.Count ? "   (gaps)" : "")}");
        }

        if (files.Count < 2)
        {
            Console.WriteLine();
            Console.WriteLine("  One run so far. The restatement check needs a second run on a later day.");
            return 0;
        }

        // ── The restatement check ────────────────────────────────────────────────
        //
        // This is the measurement that decides whether recording forward was necessary or merely
        // tidy. Every (theme, date) present in both an old run and a new one SHOULD carry the same
        // value: the past does not change. If it does change, the series is being restated behind
        // us, and any study using the vendor's current history is quietly using numbers that were
        // not available at the time.
        var oldMap = first.ToDictionary(r => (r.Theme, r.Date), r => r.Value);
        int shared = 0, changed = 0;
        double worst = 0;
        foreach (var r in last)
        {
            if (!oldMap.TryGetValue((r.Theme, r.Date), out double old)) continue;
            shared++;
            if (Math.Abs(old) < 1e-12) continue;
            double rel = Math.Abs(r.Value - old) / Math.Abs(old);
            if (rel > 0.001) { changed++; worst = Math.Max(worst, rel); }
        }

        Console.WriteLine();
        Console.WriteLine("── RESTATEMENT CHECK ──");
        Console.WriteLine($"  {shared} theme-days appear in both the first and latest run.");
        if (shared == 0)
        {
            Console.WriteLine("  No overlap yet — nothing to compare.");
        }
        else if (changed == 0)
        {
            Console.WriteLine("  None changed. The vendor's history is stable, so far.");
        }
        else
        {
            Console.WriteLine($"  {changed} ({100.0 * changed / shared:F1}%) CHANGED, worst by {worst * 100:F1}%.");
            Console.WriteLine("  The series IS restated. Any study on the vendor's current history is");
            Console.WriteLine("  using values that were not available on the dates they are attributed to.");
            Console.WriteLine("  Use this archive's earliest observation of each date, not the latest.");
        }
        Console.WriteLine();
        return 0;
    }

    internal static List<Row> LoadRun(string path)
    {
        var rows = new List<Row>();
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var sr = new StreamReader(gz);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            // One malformed line costs one row, never the run.
            try { var r = JsonConvert.DeserializeObject<Row>(line); if (r != null) rows.Add(r); }
            catch { }
        }
        return rows;
    }

    internal static string RunDate(string path)
    {
        string n = Path.GetFileName(path);
        int i = n.IndexOf('_');
        return i >= 0 && n.Length >= i + 11 ? n.Substring(i + 1, 10) : n;
    }
}
