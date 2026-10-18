using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.StrategyLab.Macro;

/// <summary>
/// <c>macro-fetch</c>: downloads the public macro, Fed, liquidity, credit and sentiment data the
/// lab had none of (it had FRED release DATES only), into <c>strategy-lab-data/macro/&lt;source&gt;/</c>,
/// with a manifest recording every file's URL, retrieval time, sha256, coverage and
/// point-in-time status.
///
/// <para><b>Nothing existing is ever overwritten.</b> A raw download that is already on disk is
/// reused, never re-fetched, and a normalized file is rewritten only when the new content is
/// byte-identical. Re-fetching a snapshot silently changes the sample an old verdict was computed
/// on; a refresh must go to a new <c>--out</c> directory so both samples survive.</para>
///
/// <para><b>Point in time.</b> Revised series (payrolls, unemployment, CPI, production, GDP,
/// claims, M2, sentiment, the recession flag) come from ALFRED's full real-time history, via
/// keyless POSTs of ALFRED's own download form (one per 1,000 vintages), so a study can ask what was known on
/// any day (<see cref="MacroPointInTime.AsOf"/>). Market closes and weekly Fed releases carry a
/// <c>known_from</c> date: ALFRED's real release dates where it has them, else the publisher's schedule (<see cref="ReleaseRule"/>).
/// NBER turning points carry the date each was announced.</para>
///
/// <para><b>Keyless only.</b> The FRED API needs a key, so it is not used; ALFRED's graph CSV
/// and download form need none. Sources that need an account are listed as failures with the
/// reason rather than worked around.</para>
/// </summary>
public static class MacroFetchCommand
{
    private const string UserAgent = "AccessibleTrader-StrategyLab/1.0 (macro-fetch; research)";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A FRED series: what it is for, how its release date is derived, and whether its
    /// full vintage history is fetched from ALFRED.</summary>
    public sealed record FredSeries(string Id, string Group, string Why, ReleaseRule Rule, bool Vintaged = false, bool InitialRelease = false,
        int Chunk = AlfredChunk, bool ReleaseDates = false);

    public static readonly FredSeries[] Fred =
    {
        // Curve: the bond market as the arbiter.
        new("DGS3MO", "curve", "3-month Treasury constant maturity (from 1981)", ReleaseRule.MarketClose),
        new("DTB3", "curve", "3-month T-bill secondary market (from 1954): longer front-end history than DGS3MO", ReleaseRule.MarketClose),
        new("DGS2", "curve", "2-year Treasury (from 1976)", ReleaseRule.MarketClose),
        new("DGS5", "curve", "5-year Treasury: belly of the curve", ReleaseRule.MarketClose),
        new("DGS10", "curve", "10-year Treasury (from 1962)", ReleaseRule.MarketClose),
        new("DGS30", "curve", "30-year Treasury: long end", ReleaseRule.MarketClose),
        new("T10Y2Y", "curve", "10y minus 2y spread, Camel's curve gate", ReleaseRule.MarketClose),
        new("T10Y3M", "curve", "10y minus 3m spread, Camel's curve gate", ReleaseRule.MarketClose),
        new("DFII10", "curve", "10-year TIPS real yield", ReleaseRule.MarketClose),
        new("T10YIE", "curve", "10-year breakeven inflation", ReleaseRule.MarketClose),
        // Policy rate and stance.
        new("DFF", "policy", "Effective fed funds, daily (from 1954)", ReleaseRule.NextBusinessDay),
        new("EFFR", "policy", "Effective fed funds, NY Fed series (from 2000)", ReleaseRule.NextBusinessDay),
        new("FEDFUNDS", "policy", "Effective fed funds, monthly average", ReleaseRule.MonthlyAverageAfterMonthEnd, InitialRelease: true),
        new("DFEDTAR", "policy", "Fed funds target, single value (1982-2008): easing vs tightening", ReleaseRule.MarketClose),
        new("DFEDTARU", "policy", "Fed funds target range upper bound (from 2008-12)", ReleaseRule.MarketClose),
        new("DFEDTARL", "policy", "Fed funds target range lower bound (from 2008-12)", ReleaseRule.MarketClose),
        // Credit.
        new("BAMLH0A0HYM2", "credit", "ICE BofA US high-yield OAS. FRED serves only the last 3 years (ICE licence)", ReleaseRule.MarketClose),
        new("BAMLC0A0CM", "credit", "ICE BofA US corporate (IG) OAS. Last 3 years only (ICE licence)", ReleaseRule.MarketClose),
        new("BAA10Y", "credit", "Moody's Baa minus 10y, daily from 1986: the long-history credit spread ICE no longer allows", ReleaseRule.MarketClose),
        new("AAA10Y", "credit", "Moody's Aaa minus 10y, daily from 1983", ReleaseRule.MarketClose),
        new("DBAA", "credit", "Moody's Baa yield, daily from 1986", ReleaseRule.MarketClose),
        new("DAAA", "credit", "Moody's Aaa yield, daily from 1983", ReleaseRule.MarketClose),
        new("BAA", "credit", "Moody's Baa yield, monthly average from 1919: credit across every cycle on record", ReleaseRule.MonthlyAverageAfterMonthEnd, InitialRelease: true),
        new("AAA", "credit", "Moody's Aaa yield, monthly average from 1919", ReleaseRule.MonthlyAverageAfterMonthEnd, InitialRelease: true),
        // Dollar.
        new("DTWEXBGS", "dollar", "Broad trade-weighted dollar (from 2006)", ReleaseRule.H10Weekly, InitialRelease: true),
        new("DTWEXM", "dollar", "Major-currencies dollar index, 1973-2019 (discontinued): the long dollar history", ReleaseRule.H10Weekly),
        // Liquidity.
        new("WALCL", "liquidity", "Fed total assets, Wednesday level (from 2002-12)", ReleaseRule.H41Weekly, InitialRelease: true),
        new("WRESBAL", "liquidity", "Reserve balances with Federal Reserve Banks, week average", ReleaseRule.H41Weekly, InitialRelease: true),
        new("WTREGEN", "liquidity", "Treasury General Account, Wednesday level: net liquidity = WALCL - TGA - RRP", ReleaseRule.H41Weekly, InitialRelease: true),
        new("RRPONTSYD", "liquidity", "Overnight reverse repo, daily (published ~1:15 pm ET the same day)", ReleaseRule.MarketClose),
        new("M2SL", "liquidity", "M2 money stock, monthly, revised", ReleaseRule.Unknown, Vintaged: true),
        // Stress and volatility.
        new("VIXCLS", "stress", "VIX close (from 1990)", ReleaseRule.MarketClose),
        new("NFCI", "stress", "Chicago Fed National Financial Conditions Index. Revised EVERY week", ReleaseRule.NfciWeekly, ReleaseDates: true),
        new("ANFCI", "stress", "Adjusted NFCI (conditions net of the cycle). Revised every week", ReleaseRule.NfciWeekly, ReleaseDates: true),
        new("STLFSI4", "stress", "St. Louis Fed Financial Stress Index v4. Revised", ReleaseRule.StlfsiWeekly, InitialRelease: true),
        // Headline data: the numbers Camel says do not mean what they appear to. All vintaged.
        new("PAYEMS", "headline", "Nonfarm payrolls", ReleaseRule.Unknown, Vintaged: true),
        new("UNRATE", "headline", "Unemployment rate", ReleaseRule.Unknown, Vintaged: true),
        new("ICSA", "headline", "Initial jobless claims, weekly", ReleaseRule.Unknown, Vintaged: true),
        new("CPIAUCSL", "headline", "CPI, all items, seasonally adjusted", ReleaseRule.Unknown, Vintaged: true),
        new("CPILFESL", "headline", "Core CPI", ReleaseRule.Unknown, Vintaged: true),
        new("PCEPILFE", "headline", "Core PCE price index, the Fed's target measure", ReleaseRule.Unknown, Vintaged: true),
        new("INDPRO", "headline", "Industrial production", ReleaseRule.Unknown, Vintaged: true),
        new("GDPC1", "headline", "Real GDP, quarterly", ReleaseRule.Unknown, Vintaged: true),
        new("UMCSENT", "headline", "University of Michigan consumer sentiment (preliminary then final)", ReleaseRule.Unknown, Vintaged: true),
        new("SAHMREALTIME", "headline", "Sahm rule recession indicator, built from real-time unemployment", ReleaseRule.Unknown, Vintaged: true),
        // Recession flags: assigned by NBER months after the fact.
        new("USREC", "recession", "NBER recession flag, monthly. Vintaged: shows when FRED learned each turning point", ReleaseRule.Unknown, Vintaged: true),
        new("USRECD", "recession", "NBER recession flag, daily. NOT point in time: known_from left blank so it is never served", ReleaseRule.Unknown),
    };

    private static readonly string[] CoinMetricsMetrics =
        { "CapMVRVCur", "CapMrktCurUSD", "PriceUSD", "IssTotUSD", "HashRate", "SplyCur", "AdrActCnt" };

    private static readonly string[] BlockchainCharts = { "miners-revenue", "difficulty", "hash-rate" };

    private static readonly (string File, string Product)[] CboeFiles =
    {
        ("pcratioarchive.csv", "1995-2003 archive: total, index and equity ratios"),
        ("totalpcarchive.csv", "total, 2003-2012"),
        ("totalpc.csv", "total, 2006-2019"),
        ("equitypcarchive.csv", "equity, 2003-2012"),
        ("equitypc.csv", "equity, 2006-2019"),
        ("indexpcarchive.csv", "index, 2003-2012"),
        ("indexpc.csv", "index, 2006-2019"),
        ("spxpc.csv", "SPX options, 2010-2019"),
        ("vixpc.csv", "VIX options, 2006-2019"),
        ("etppc.csv", "exchange-traded products, 2006-2019"),
    };

    /// <summary>What was looked for and deliberately not fetched, with the reason (docs/MACRO_DATA.md).</summary>
    private static readonly object[] NotFetched =
    {
        new { source = "FRED API (api.stlouisfed.org)", reason = "needs an API key; not used. ALFRED's graph CSV and download form are keyless and give the same values and vintages." },
        new { source = "ICE BofA OAS before 2023-10-17 (BAMLH0A0HYM2, BAMLC0A0CM)", reason = "ICE licence: FRED and ALFRED serve only the last 3 years, with no older vintages. Moody's BAA10Y/AAA10Y (daily from 1986/1983) and BAA/AAA (monthly from 1919) are the long-history credit spreads." },
        new { source = "NFCI and ANFCI initial-release file", reason = "ALFRED returned HTTP 504 at 800 and at 100 vintages per request (2026-10-18). known_from uses the release dates on ALFRED's download page instead; the values are the latest revision." },
        new { source = "data.nber.org business_cycle_dates.json", reason = "the server refuses non-browser clients ('we have disallowed bots from downloading datasets'). The same dates come from the chronology table on www.nber.org." },
        new { source = "Cboe put/call after 2019-10-04", reason = "Cboe stopped the CSV archives; later days exist only as one HTML page per day (~425 KB each, ~1,770 pages). Not scraped; needs Cody's decision or Cboe DataShop." },
        new { source = "Google Trends", reason = "no keyless, terms-compliant route (the public endpoints are undocumented and scraping them breaks Google's terms)." },
        new { source = "Glassnode, CryptoQuant, CoinMetrics paid metrics (SOPR, realized cap, NUPL, exchange flows, RevUSD)", reason = "need an account/key. CoinMetrics community metrics and Blockchain.com charts were fetched instead; realized price and the Puell multiple are derivable from them." },
        new { source = "Philadelphia Fed Real-Time Data Set", reason = "not needed: ALFRED gave every requested series' full vintage history keylessly." },
    };

    // ── Manifest ─────────────────────────────────────────────────────────────

    public sealed class ManifestFile
    {
        public string Path { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Source { get; set; } = "";
        public string? Series { get; set; }
        public string? Url { get; set; }
        public string? Method { get; set; }
        public DateTime? RetrievedUtc { get; set; }
        public string Sha256 { get; set; } = "";
        public long Bytes { get; set; }
        public int? Rows { get; set; }
        public string? First { get; set; }
        public string? Last { get; set; }
        public string? PointInTime { get; set; }
        public string? KnownFrom { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class RawMeta
    {
        public string Url { get; set; } = "";
        public string Method { get; set; } = "GET";
        public string? Request { get; set; }
        public DateTime RetrievedUtc { get; set; }
        public int Status { get; set; }
        public string? ContentType { get; set; }
        public long Bytes { get; set; }
        public string Sha256 { get; set; } = "";
    }

    private sealed class Ctx
    {
        public required string DataRoot;   // strategy-lab-data
        public required string MacroRoot;  // strategy-lab-data/macro (or --out)
        public readonly List<ManifestFile> Files = new();
        public readonly List<object> Failures = new();
        public readonly List<object> Checks = new();
        public readonly HttpClient Http = MakeHttp();
        public DateTime LastRequest = DateTime.MinValue;
        public int Fetched, Kept;

        public string Rel(string abs) => System.IO.Path.GetRelativePath(DataRoot, abs).Replace('\\', '/');
        public void Fail(string source, string? series, string reason)
        {
            Failures.Add(new { source, series, reason });
            Console.Error.WriteLine($"  ! {source} {series}: {reason}");
        }
    }

    private static HttpClient MakeHttp()
    {
        var h = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(180) };
        h.DefaultRequestVersion = HttpVersion.Version11;
        h.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        h.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return h;
    }

    // ── Entry ────────────────────────────────────────────────────────────────

    public static async Task<int> RunAsync(string dataRoot, string? outDir, string? manifestCopy, string? only)
    {
        dataRoot = Path.GetFullPath(dataRoot);
        var macroRoot = Path.GetFullPath(outDir ?? Path.Combine(dataRoot, "macro"));
        Directory.CreateDirectory(macroRoot);
        var ctx = new Ctx { DataRoot = dataRoot, MacroRoot = macroRoot };
        var sources = (only ?? "fred,alfred,nber,aaii,cboe,coinmetrics,blockchain")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"macro-fetch → {macroRoot}");
        Console.WriteLine("Existing raw files are reused, never re-fetched; normalized files are only rewritten when identical.");
        if (sources.Contains("fred")) await FredLatest(ctx);
        if (sources.Contains("alfred")) await AlfredVintages(ctx);
        if (sources.Contains("nber")) await Nber(ctx);
        if (sources.Contains("aaii")) await Aaii(ctx);
        if (sources.Contains("cboe")) await Cboe(ctx);
        if (sources.Contains("coinmetrics")) await CoinMetrics(ctx);
        if (sources.Contains("blockchain")) await Blockchain(ctx);

        WriteManifest(ctx, only, manifestCopy);
        Console.WriteLine();
        Console.WriteLine($"Fetched {ctx.Fetched} raw files, kept {ctx.Kept} already on disk, {ctx.Failures.Count} failures.");
        return ctx.Failures.Count == 0 ? 0 : 1;
    }

    private static void WriteManifest(Ctx ctx, string? only, string? manifestCopy)
    {
        var path = Path.Combine(ctx.MacroRoot, "MACRO_MANIFEST.json");
        // A partial run (--only) merges into the existing manifest instead of dropping entries.
        var files = new Dictionary<string, ManifestFile>(StringComparer.Ordinal);
        var failures = new List<object>();
        var checks = new List<object>();
        if (File.Exists(path) && only != null)
        {
            var old = JObject.Parse(File.ReadAllText(path));
            foreach (var f in old["files"]?.ToObject<List<ManifestFile>>() ?? new()) files[f.Path] = f;
            var touched = ctx.Files.Select(f => f.Source).ToHashSet();
            failures.AddRange((old["failures"] as JArray ?? new JArray()).Where(t => !touched.Contains((string?)t["source"] ?? "")));
            checks.AddRange((old["checks"] as JArray ?? new JArray()).Where(t => !touched.Contains((string?)t["source"] ?? "")));
        }
        foreach (var f in ctx.Files) files[f.Path] = f;
        failures.AddRange(ctx.Failures);
        checks.AddRange(ctx.Checks);
        var doc = new
        {
            generator = "StrategyLab macro-fetch (AccessibleTrader.StrategyLab/Macro/MacroFetchCommand.cs)",
            written = DateTime.UtcNow,
            root = "paths are relative to strategy-lab-data/",
            knownFromConvention =
                "known_from = the first calendar day by whose END the value was public (US Eastern for US releases). " +
                "Read at bar i's close with known_from <= date(i) and act at bar i+1; a study acting at bar i's open needs known_from < date(i). " +
                "Blank known_from = no defensible release date; never serve it.",
            pointInTimeValues = new Dictionary<string, string>
            {
                ["vintaged"] = "ALFRED real-time history: every value with the dates it was current. Exact.",
                ["vintaged-latest"] = "Latest values of a vintaged series; known_from = the day the CURRENT value was published (exact, but early rows show today's revised value).",
                ["release-rule"] = "Values as published, known_from from the publisher's schedule plus a holiday margin (estimated, conservative).",
                ["release-rule-revised"] = "As release-rule, but the series is revised after release and only the latest vintage is stored: values are NOT what was known then.",
                ["announcement"] = "Each event carries the date it was announced.",
                ["not-point-in-time"] = "Assigned after the fact; known_from blank. Do not use as a signal.",
                ["raw"] = "Exactly what the server returned.",
            },
            files = files.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToList(),
            failures,
            checks,
            notFetched = NotFetched,
        };
        var json = JsonConvert.SerializeObject(doc, Formatting.Indented);
        File.WriteAllText(path, json);
        Console.WriteLine($"manifest → {path} ({files.Count} files)");
        if (manifestCopy != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestCopy))!);
            File.WriteAllText(manifestCopy, json);
            Console.WriteLine($"manifest copy → {manifestCopy}");
        }
    }

    // ── HTTP, raw files and normalized files ─────────────────────────────────

    /// <summary>Returns the raw bytes, from disk when present (no network), else downloaded with
    /// a polite gap and backoff. Writes the file plus a <c>.meta.json</c> sidecar.</summary>
    private static async Task<byte[]?> Raw(Ctx ctx, string relUnderMacro, string source, string? series,
        string url, string? postBody = null, string? requestNote = null, Func<byte[], string?>? validate = null)
    {
        var path = Path.Combine(ctx.MacroRoot, relUnderMacro);
        var metaPath = path + ".meta.json";
        if (File.Exists(path) && File.Exists(metaPath))
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var meta = JsonConvert.DeserializeObject<RawMeta>(await File.ReadAllTextAsync(metaPath))!;
            if (Sha(bytes) != meta.Sha256)
            {
                ctx.Fail(source, series, $"{ctx.Rel(path)} no longer matches its recorded sha256; not touched");
                return null;
            }
            ctx.Kept++;
            AddRaw(ctx, path, source, series, meta);
            return bytes;
        }
        if (File.Exists(path))
        {
            ctx.Fail(source, series, $"{ctx.Rel(path)} exists without a .meta.json; not overwritten");
            return null;
        }

        Exception? last = null;
        int[] backoff = { 0, 15, 45, 120 };
        foreach (var wait in backoff)
        {
            if (wait > 0)
            {
                Console.WriteLine($"    retry in {wait}s ({last?.Message})");
                await Task.Delay(TimeSpan.FromSeconds(wait));
            }
            var gap = DateTime.UtcNow - ctx.LastRequest;
            if (gap < TimeSpan.FromSeconds(2)) await Task.Delay(TimeSpan.FromSeconds(2) - gap);
            ctx.LastRequest = DateTime.UtcNow;
            try
            {
                using var req = new HttpRequestMessage(postBody == null ? HttpMethod.Get : HttpMethod.Post, url);
                if (postBody != null)
                    req.Content = new StringContent(postBody, Encoding.ASCII, "application/x-www-form-urlencoded");
                using var resp = await ctx.Http.SendAsync(req);
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                int code = (int)resp.StatusCode;
                if (code == 429 || code >= 500) { last = new HttpRequestException($"HTTP {code}"); continue; }
                if (!resp.IsSuccessStatusCode)
                {
                    ctx.Fail(source, series, $"HTTP {code} from {url}: {Snippet(bytes)}");
                    return null;
                }
                var invalid = validate?.Invoke(bytes);
                if (invalid != null)
                {
                    ctx.Fail(source, series, $"{url}: {invalid}; not saved");
                    return null;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes);
                var meta = new RawMeta
                {
                    Url = url, Method = postBody == null ? "GET" : "POST", Request = requestNote,
                    RetrievedUtc = DateTime.UtcNow, Status = code,
                    ContentType = resp.Content.Headers.ContentType?.ToString(),
                    Bytes = bytes.LongLength, Sha256 = Sha(bytes),
                };
                await File.WriteAllTextAsync(metaPath, JsonConvert.SerializeObject(meta, Formatting.Indented));
                ctx.Fetched++;
                Console.WriteLine($"  fetched {ctx.Rel(path)} ({bytes.Length:N0} bytes)");
                AddRaw(ctx, path, source, series, meta);
                return bytes;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                last = ex;
            }
        }
        ctx.Fail(source, series, $"{url}: gave up after {backoff.Length} attempts ({last?.Message})");
        return null;
    }

    private static void AddRaw(Ctx ctx, string path, string source, string? series, RawMeta meta) =>
        ctx.Files.Add(new ManifestFile
        {
            Path = ctx.Rel(path), Kind = "raw", Source = source, Series = series, Url = meta.Url,
            Method = meta.Method == "POST" ? $"POST {meta.Request}" : "GET", RetrievedUtc = meta.RetrievedUtc,
            Sha256 = meta.Sha256, Bytes = meta.Bytes, PointInTime = "raw",
        });

    /// <summary>Writes a normalized CSV unless a different file is already there.</summary>
    private static void Norm(Ctx ctx, string relUnderMacro, string content, string source, string? series,
        string pointInTime, string? knownFrom, string? notes, DateTime? retrieved, string? url, int dateColumn = 0)
    {
        var path = Path.Combine(ctx.MacroRoot, relUnderMacro);
        var bytes = Encoding.UTF8.GetBytes(content);
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (!existing.AsSpan().SequenceEqual(bytes))
            {
                ctx.Fail(source, series, $"{ctx.Rel(path)} exists with different content; refusing to overwrite (use a new --out)");
                return;
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        ctx.Files.Add(new ManifestFile
        {
            Path = ctx.Rel(path), Kind = "normalized", Source = source, Series = series, Url = url,
            RetrievedUtc = retrieved, Sha256 = Sha(bytes), Bytes = bytes.LongLength,
            Rows = Math.Max(0, lines.Length - 1),
            First = DateRange(lines, dateColumn).First,
            Last = DateRange(lines, dateColumn).Last,
            PointInTime = pointInTime, KnownFrom = knownFrom, Notes = notes,
        });
    }

    private static (string? First, string? Last) DateRange(string[] lines, int col)
    {
        var dates = lines.Skip(1).Select(l => l.Split(',')).Where(f => f.Length > col && Regex.IsMatch(f[col], @"^\d{4}-\d\d-\d\d$"))
                         .Select(f => f[col]).ToList();
        return dates.Count == 0 ? (null, null) : (dates.Min(StringComparer.Ordinal), dates.Max(StringComparer.Ordinal));
    }

    private static DateTime? Retrieved(Ctx ctx, string relUnderMacro)
    {
        var meta = Path.Combine(ctx.MacroRoot, relUnderMacro) + ".meta.json";
        return File.Exists(meta) ? JsonConvert.DeserializeObject<RawMeta>(File.ReadAllText(meta))!.RetrievedUtc : null;
    }

    public static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static string Snippet(byte[] b) =>
        Regex.Replace(Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 200)), @"\s+", " ");

    private static string? ExpectCsvHeader(byte[] b, string prefix) =>
        Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 64)).StartsWith(prefix, StringComparison.Ordinal)
            ? null : $"unexpected content '{Snippet(b)}'";

    internal const int AlfredChunk = 1000;

    private static string? ExpectAlfredZip(byte[] b)
    {
        if (b.Length > 4 && b[0] == 'P' && b[1] == 'K') return null;
        var limit = Regex.Match(Encoding.UTF8.GetString(b), @"You may only download[^<]*");
        return limit.Success ? $"ALFRED refused: {Regex.Replace(limit.Value, @"\s+", " ").Trim()}" : $"not a zip: '{Snippet(b)}'";
    }

    private static string RuleText(ReleaseRule r) => r switch
    {
        ReleaseRule.MarketClose => "observation date (a market close, known at the end of its day)",
        ReleaseRule.NextBusinessDay => "next weekday + 1 day (NY Fed publishes day D the next business morning; +1 holiday margin)",
        ReleaseRule.H41Weekly => "observation + 2 days (H.4.1: Wednesday level released Thursday 4:30 pm ET; +1 holiday margin)",
        ReleaseRule.H10Weekly => "Tuesday after the observation (H.10 posts the week's dailies on Monday; +1 holiday margin)",
        ReleaseRule.NfciWeekly => "observation + 6 days (week ending Friday, released Wednesday 8:30 ET; +1 margin)",
        ReleaseRule.StlfsiWeekly => "observation + 7 days (week ending Friday, released Thursday; +1 margin)",
        ReleaseRule.MonthlyAverageAfterMonthEnd => "first of the following month + 1 day (a monthly average is complete when the month ends)",
        ReleaseRule.OnChainNextDay => "observation + 1 day (UTC day D is computed after the day closes)",
        ReleaseRule.SameDayRelease => "the report date (published that day)",
        _ => "blank: no defensible release date",
    };

    // ── FRED latest values (non-vintaged series) ─────────────────────────────

    private static async Task FredLatest(Ctx ctx)
    {
        Console.WriteLine();
        Console.WriteLine("== FRED values (ALFRED graph CSV, latest vintage, keyless) ==");
        foreach (var s in Fred.Where(f => !f.Vintaged))
        {
            var url = $"https://alfred.stlouisfed.org/graph/alfredgraph.csv?id={s.Id}";
            var rel = $"fred/raw/{s.Id}.csv";
            var bytes = await Raw(ctx, rel, "fred", s.Id, url, validate: b => ExpectCsvHeader(b, "observation_date,"));
            if (bytes == null) continue;
            var obs = MacroPointInTime.ParseFredGraphCsv(Encoding.UTF8.GetString(bytes));

            // Weekly and monthly releases: where ALFRED has the observation's initial release, its
            // date is the known_from (exact, and it catches holiday-week delays a rule misses);
            // before ALFRED's first vintage the schedule rule is all there is.
            Dictionary<DateOnly, (double Value, DateOnly Date)>? init = null;
            if (s.InitialRelease)
            {
                var form = await LoadAlfredForm(ctx, "fred", "fred/raw", s.Id, s.Chunk);
                var parts = form == null ? null
                    : await AlfredDownload(ctx, "fred", "fred/raw", form, "initial", 4, "file_type=4 (initial release only)");
                if (parts != null)
                {
                    var rowsInit = InitialRows(parts);
                    init = rowsInit.ToDictionary(r => r.Period, r => (r.Value, r.Date));
                    var initKnown = rowsInit.Select(r => new KnownValue(r.Period, r.Value, r.Date)).ToList();
                    Norm(ctx, $"fred/{s.Id}_initial.csv", MacroPointInTime.ToKnownCsv(initKnown), "fred", s.Id, "vintaged",
                        "ALFRED initial release date (exact)",
                        $"{s.Group}: {s.Why}. First print of each observation and the day it was first published, from ALFRED (from its first vintage, {form!.Vintages[0]}).",
                        Retrieved(ctx, form.Part("fred/raw", "initial", 0)), form.PageUrl);
                }
            }
            // NFCI: ALFRED answers its initial-release download with HTTP 504 (tried 2026-10-18 at
            // 800 and at 100 vintages per request), so the release DATES on its download page are
            // used instead: each Wednesday release carries the newest week.
            List<DateOnly>? releases = null;
            if (s.ReleaseDates)
            {
                var form = await LoadAlfredForm(ctx, "fred", "fred/raw", s.Id);
                if (form == null) continue;
                releases = form.Vintages.Select(v => DateOnly.ParseExact(v, "yyyy-MM-dd", Inv)).OrderBy(d => d).ToList();
            }
            // Without its release dates the file would be written with rule dates only, and the
            // never-overwrite rule would then lock that weaker version in. Leave it for a re-run.
            if (s.InitialRelease && init == null) continue;
            int fromAlfred = 0;
            var rows = obs.Select(o =>
            {
                var rule = MacroPointInTime.KnownFrom(o.Date, s.Rule);
                if (init != null && init.TryGetValue(o.Date, out var first) && first.Date >= o.Date)
                {
                    fromAlfred++;
                    return new KnownValue(o.Date, o.Value, first.Date);
                }
                if (releases != null)
                {
                    var k = MacroPointInTime.KnownFromReleaseDates(o.Date, releases, rule);
                    if (k != rule) fromAlfred++;
                    return new KnownValue(o.Date, o.Value, k);
                }
                return new KnownValue(o.Date, o.Value, rule);
            }).ToList();
            string pit = s.Rule == ReleaseRule.Unknown ? "not-point-in-time"
                : s.Id is "NFCI" or "ANFCI" or "STLFSI4" ? "release-rule-revised" : "release-rule";
            string known = init != null
                ? $"ALFRED initial-release date for {fromAlfred} of {rows.Count} observations; the rest (before ALFRED's archive) by rule: {RuleText(s.Rule)}"
                : releases != null
                    ? $"first ALFRED release date after the observation, never earlier than the rule ({fromAlfred} of {rows.Count} moved later by it); before {releases[0]:yyyy-MM-dd} by rule: {RuleText(s.Rule)}"
                    : RuleText(s.Rule);
            string notes = $"{s.Group}: {s.Why}" + (pit == "release-rule-revised"
                ? $". Values are TODAY's revised numbers; the as-printed value is in {s.Id}_initial.csv" : "");
            Norm(ctx, $"fred/{s.Id}.csv", MacroPointInTime.ToKnownCsv(rows), "fred", s.Id, pit, known, notes, Retrieved(ctx, rel), url);
        }
    }

    // ── ALFRED full vintage history ──────────────────────────────────────────

    /// <summary>What ALFRED's download form needs: the series' vintage dates and observation range,
    /// read from its download page (kept as a raw file so a re-run asks for the same vintages).</summary>
    private sealed record AlfredForm(string Id, string PageUrl, List<string> Vintages, string ObsStart, string ObsEnd, int ChunkSize)
    {
        // ALFRED caps one download at 1,500 vintages for a monthly series ("You may only
        // download 1500 vintages at one time"). Longer histories are fetched in chunks and
        // merged: each chunk's rows already carry their true realtime_end, so a value that
        // spans a chunk boundary appears in both chunks and is joined back into one row
        // (verified 2026-10-18: PAYEMS fetched as two halves merges to the one-request file
        // row for row).
        // NFCI is revised in full every week, and ALFRED times out (HTTP 504) building even its
        // initial-release file from 800 vintages at once, so it is asked for in smaller slices.
        public List<List<string>> Chunks { get; } = Vintages.Chunk(ChunkSize).Select(c => c.ToList()).ToList();

        public string Part(string dir, string kind, int k) =>
            Chunks.Count == 1 ? $"{dir}/{Id}_{kind}.zip" : $"{dir}/{Id}_{kind}_part{k:00}.zip";

        public string Body(int fileType, List<string> dates)
        {
            var pairs = new List<KeyValuePair<string, string>>
            {
                new("form[units]", "lin"), new("form[obs_start_date]", ObsStart), new("form[obs_end_date]", ObsEnd),
            };
            pairs.AddRange(dates.Select(v => new KeyValuePair<string, string>("form[selected_vintage_dates][]", v)));
            pairs.Add(new("form[entered_vintage_dates]", ""));
            pairs.Add(new("form[file_type]", fileType.ToString(Inv)));
            pairs.Add(new("form[file_format]", "csv"));
            pairs.Add(new("form[download_data]", ""));
            return string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        }

        public string Note(string what, int k) =>
            $"{what}, csv, units=lin, obs {ObsStart}..{ObsEnd}, vintage dates {Chunks[k][0]}..{Chunks[k][^1]} ({Chunks[k].Count}" +
            (Chunks.Count == 1 ? $", all of those listed in {Id}_downloaddata.html)" : $"; chunk {k + 1} of {Chunks.Count} of the list in {Id}_downloaddata.html)");
    }

    private static async Task<AlfredForm?> LoadAlfredForm(Ctx ctx, string source, string dir, string id, int chunk = AlfredChunk)
    {
        var pageUrl = $"https://alfred.stlouisfed.org/series/downloaddata?seid={id}";
        var page = await Raw(ctx, $"{dir}/{id}_downloaddata.html", source, id, pageUrl,
            validate: b => Encoding.UTF8.GetString(b).Contains("form[selected_vintage_dates][]") ? null : "no vintage list on the page");
        if (page == null) return null;
        var html = Encoding.UTF8.GetString(page);
        var vintages = Regex.Matches(html, "<option value=\"(\\d{4}-\\d\\d-\\d\\d)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        var obsStart = Regex.Match(html, "name=\"form\\[obs_start_date\\]\"[^>]*value=\"([0-9-]{10})\"").Groups[1].Value;
        var obsEnd = Regex.Match(html, "name=\"form\\[obs_end_date\\]\"[^>]*value=\"([0-9-]{10})\"").Groups[1].Value;
        if (vintages.Count == 0 || obsStart.Length == 0 || obsEnd.Length == 0)
        {
            ctx.Fail(source, id, "could not read the vintage list or observation range from the download page");
            return null;
        }
        Console.WriteLine($"  {id}: {vintages.Count} vintages {vintages[0]} → {vintages[^1]}, observations {obsStart} → {obsEnd}");
        return new AlfredForm(id, pageUrl, vintages, obsStart, obsEnd, chunk);
    }

    /// <summary>Every chunk of one ALFRED download, or null if any chunk failed.</summary>
    private static async Task<List<byte[]>?> AlfredDownload(Ctx ctx, string source, string dir, AlfredForm form,
        string kind, int fileType, string what)
    {
        var parts = new List<byte[]>();
        for (int k = 0; k < form.Chunks.Count; k++)
        {
            var bytes = await Raw(ctx, form.Part(dir, kind, k), source, form.Id, form.PageUrl,
                form.Body(fileType, form.Chunks[k]), form.Note(what, k), ExpectAlfredZip);
            if (bytes == null) return null;
            parts.Add(bytes);
        }
        return parts;
    }

    /// <summary>ALFRED's initial-release-only rows across chunks; a period in two chunks keeps its earliest release.</summary>
    private static List<(DateOnly Period, double Value, DateOnly Date)> InitialRows(List<byte[]> parts) =>
        parts.SelectMany(z => ParseInitial(ZipCsv(z, "initial_release_only.csv")))
             .GroupBy(r => r.Period).Select(g => g.OrderBy(r => r.Date).First()).OrderBy(r => r.Period).ToList();

    private static async Task AlfredVintages(Ctx ctx)
    {
        Console.WriteLine();
        Console.WriteLine("== ALFRED real-time history (download form, keyless) ==");
        foreach (var s in Fred.Where(f => f.Vintaged))
        {
            var form = await LoadAlfredForm(ctx, "alfred", "alfred/raw", s.Id);
            if (form == null) continue;
            var pageUrl = form.PageUrl;
            var rtParts = await AlfredDownload(ctx, "alfred", "alfred/raw", form, "realtime", 1, "file_type=1 (observations by real-time period)");
            var initParts = await AlfredDownload(ctx, "alfred", "alfred/raw", form, "initial", 4, "file_type=4 (initial release only)");
            if (rtParts == null) continue;
            var rtRel = form.Part("alfred/raw", "realtime", 0);

            List<VintageRow> rows;
            try
            {
                rows = MacroPointInTime.MergeChunks(rtParts.Select(z => MacroPointInTime.ParseAlfredRealtimeCsv(ZipCsv(z, "real-time_period.csv"))));
            }
            catch (Exception ex) { ctx.Fail("alfred", s.Id, $"real-time CSV did not parse or merge: {ex.Message}"); continue; }
            var first = MacroPointInTime.FirstReleases(rows);
            var retrieved = Retrieved(ctx, rtRel);
            var why = $"{s.Group}: {s.Why}";

            Norm(ctx, $"alfred/{s.Id}_vintages.csv", MacroPointInTime.ToVintageCsv(rows), "alfred", s.Id, "vintaged",
                "realtime_start (the value was current from realtime_start through realtime_end inclusive)", why, retrieved, pageUrl);
            Norm(ctx, $"alfred/{s.Id}_first_release.csv", MacroPointInTime.ToFirstReleaseCsv(first), "alfred", s.Id, "vintaged",
                "first_release_date", why + ". first_is_initial_print=false marks back-filled periods and the archive's first vintage", retrieved, pageUrl);
            var latest = rows.Where(r => r.RealtimeEnd is null && r.Value is not null)
                .Select(r => new KnownValue(r.Period, r.Value!.Value, r.RealtimeStart)).ToList();
            Norm(ctx, $"alfred/{s.Id}.csv", MacroPointInTime.ToKnownCsv(latest), "alfred", s.Id, "vintaged-latest",
                "realtime_start of the current value", why + ". For as-known-then values read the _vintages file", retrieved, pageUrl);

            // Cross-check our first-release derivation against ALFRED's own initial-release file.
            if (initParts != null)
            {
                var init = InitialRows(initParts);
                var ours = first.Where(f => f.FirstIsInitialPrint).ToDictionary(f => f.Period);
                int agree = 0, valueOrDate = 0, onlyTheirs = 0;
                foreach (var (period, value, date) in init)
                {
                    if (!ours.TryGetValue(period, out var o)) { onlyTheirs++; continue; }
                    if (o.FirstReleaseDate == date && Math.Abs(o.FirstValue - value) < 1e-9) agree++; else valueOrDate++;
                }
                int onlyOurs = ours.Count - agree - valueOrDate;
                ctx.Checks.Add(new
                {
                    source = "alfred", series = s.Id, check = "derived first prints vs ALFRED initial-release-only file",
                    alfredInitialRows = init.Count, ourInitialPrints = ours.Count, agree, differ = valueOrDate, onlyAlfred = onlyTheirs, onlyOurs,
                });
                Console.WriteLine($"    first-print check: {agree}/{init.Count} agree, {valueOrDate} differ, {onlyTheirs} only ALFRED, {onlyOurs} only ours");
                if (valueOrDate > 0 || onlyTheirs > 0 || onlyOurs > 0)
                    ctx.Fail("alfred", s.Id, $"first-print derivation disagrees with ALFRED's initial-release file ({valueOrDate} differ, {onlyTheirs} only ALFRED, {onlyOurs} only ours)");
            }
        }
    }

    private static string ZipCsv(byte[] zip, string suffix)
    {
        using var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var e = z.Entries.FirstOrDefault(x => x.FullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException($"zip has no *{suffix}: {string.Join(", ", z.Entries.Select(x => x.FullName))}");
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    private static List<(DateOnly Period, double Value, DateOnly Date)> ParseInitial(string csv)
    {
        var list = new List<(DateOnly, double, DateOnly)>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var f = line.TrimEnd('\r').Split(',');
            if (f.Length < 3 || f[1] == "." || f[1].Length == 0) continue;
            list.Add((DateOnly.ParseExact(f[0], "yyyy-MM-dd", Inv), double.Parse(f[1], NumberStyles.Float, Inv),
                DateOnly.ParseExact(f[2], "yyyy-MM-dd", Inv)));
        }
        return list;
    }

    // ── NBER turning points with announcement dates ──────────────────────────

    private static async Task Nber(Ctx ctx)
    {
        Console.WriteLine();
        Console.WriteLine("== NBER business-cycle dates and announcement dates ==");
        // data.nber.org's JSON refuses non-browser clients ("we have disallowed bots from
        // downloading datasets"), so the dates come from the public chronology page's table
        // instead of from that server under a spoofed user agent.
        const string datesUrl = "https://www.nber.org/research/data/us-business-cycle-expansions-and-contractions";
        const string annUrl = "https://www.nber.org/research/business-cycle-dating/business-cycle-dating-committee-announcements";
        var dates = await Raw(ctx, "nber/raw/business_cycle_dates.html", "nber", null, datesUrl,
            validate: b => Encoding.UTF8.GetString(b).Contains("Peak Month") ? null : "page lacks the chronology table");
        var ann = await Raw(ctx, "nber/raw/announcements.html", "nber", null, annUrl,
            validate: b => Encoding.UTF8.GetString(b).Contains("Business Cycle Dating") ? null : "page lacks the announcement list");
        if (dates == null || ann == null) return;

        var turning = ParseNberChronology(Encoding.UTF8.GetString(dates));
        var announcements = ParseNberAnnouncements(Encoding.UTF8.GetString(ann));

        var sb = new StringBuilder("type,month,announcement_date,lag_days,lag_months,announcement\n");
        int matched = 0;
        foreach (var (type, month) in turning.OrderBy(t => t.Month))
        {
            var a = announcements.FirstOrDefault(x => x.Type == type && x.Month == month);
            if (a.Title != null)
            {
                matched++;
                int lagDays = a.Date.DayNumber - month.DayNumber;
                int lagMonths = (a.Date.Year - month.Year) * 12 + a.Date.Month - month.Month;
                sb.Append($"{type},{month:yyyy-MM-dd},{a.Date:yyyy-MM-dd},{lagDays},{lagMonths},\"{a.Title}\"\n");
            }
            else
            {
                sb.Append($"{type},{month:yyyy-MM-dd},,,,\"none: dated retrospectively (the committee's first announcement was 1979)\"\n");
            }
        }
        foreach (var memo in announcements.Where(x => x.Type == "memo").OrderBy(x => x.Date))
            sb.Append($"memo,,{memo.Date:yyyy-MM-dd},,,\"{memo.Title}\"\n");

        var expected = turning.Count(t => t.Month.Year >= 1979);
        if (matched != expected)
            ctx.Fail("nber", null, $"matched {matched} announcements to turning points, expected {expected} (all turning points since 1979)");
        Norm(ctx, "nber/nber_turning_points.csv", sb.ToString(), "nber", "turning_points", "announcement",
            "announcement_date (blank before 1979: no real-time announcement existed)",
            "Peaks and troughs (month), with the day NBER announced each. A recession flag for month M is unknowable before the announcement.",
            Retrieved(ctx, "nber/raw/announcements.html"), annUrl, dateColumn: 1);
        Console.WriteLine($"  {turning.Count} turning points, {matched} with announcement dates, {announcements.Count(a => a.Type == "memo")} memos");
    }

    /// <summary>Rows of the chronology table: a peak cell and a trough cell, each "Month YYYY (YYYYQn)" or blank.</summary>
    internal static List<(string Type, DateOnly Month)> ParseNberChronology(string html)
    {
        var list = new List<(string, DateOnly)>();
        foreach (Match row in Regex.Matches(html, "<tr>(.*?)</tr>", RegexOptions.Singleline))
        {
            var cells = Regex.Matches(row.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
                .Select(c => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(c.Groups[1].Value, "<[^>]+>", "")), @"\s+", " ").Trim())
                .ToList();
            if (cells.Count < 2) continue;
            for (int i = 0; i < 2; i++)
            {
                var m = Regex.Match(cells[i], @"^([A-Z][a-z]+) (\d{4}) \((\d{4})Q[1-4]\)$");
                if (!m.Success) continue;
                list.Add((i == 0 ? "peak" : "trough",
                    DateOnly.ParseExact($"{m.Groups[1].Value} 1 {m.Groups[2].Value}", "MMMM d yyyy", Inv)));
            }
        }
        return list;
    }

    internal static List<(string Type, DateOnly Month, DateOnly Date, string? Title)> ParseNberAnnouncements(string html)
    {
        var text = Regex.Replace(html, "<script.*?</script>", " ", RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " "));
        text = Regex.Replace(text, @"\s+", " ");
        var list = new List<(string, DateOnly, DateOnly, string?)>();
        const string date = @"(January|February|March|April|May|June|July|August|September|October|November|December) (\d{1,2}), (\d{4})";
        const string month = "(January|February|March|April|May|June|July|August|September|October|November|December)";
        foreach (Match m in Regex.Matches(text, date + " (Determination of the|Announcement of) " + month +
                     @" (\d{4}) (?:business cycle )?(peak|trough)", RegexOptions.IgnoreCase))
        {
            var d = DateOnly.ParseExact($"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}", "MMMM d yyyy", Inv);
            var turn = DateOnly.ParseExact($"{m.Groups[5].Value} 1 {m.Groups[6].Value}", "MMMM d yyyy", Inv);
            list.Add((m.Groups[7].Value.ToLowerInvariant(), turn, d,
                $"{m.Groups[4].Value} {m.Groups[5].Value} {m.Groups[6].Value} {m.Groups[7].Value}"));
        }
        foreach (Match m in Regex.Matches(text, date + " (Memo from the Business Cycle Dating Committee)"))
        {
            var d = DateOnly.ParseExact($"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}", "MMMM d yyyy", Inv);
            list.Add(("memo", default, d, m.Groups[4].Value));
        }
        return list;
    }

    // ── AAII sentiment ───────────────────────────────────────────────────────

    private static async Task Aaii(Ctx ctx)
    {
        Console.WriteLine();
        Console.WriteLine("== AAII weekly sentiment survey ==");
        const string url = "https://www.aaii.com/files/surveys/sentiment.xls";
        const string rel = "aaii/raw/sentiment.xls";
        var bytes = await Raw(ctx, rel, "aaii", null, url,
            validate: b => b.Length > 8 && BitConverter.ToUInt64(b, 0) == 0xE11AB1A1E011CFD0UL ? null : $"not an .xls: '{Snippet(b)}'");
        if (bytes == null) return;
        LegacyXlsReader xls;
        try { xls = LegacyXlsReader.Read(bytes); }
        catch (Exception ex) { ctx.Fail("aaii", null, $"xls did not parse: {ex.Message}"); return; }

        var parsed = ParseAaii(xls, out var note);
        if (parsed == null) { ctx.Fail("aaii", null, note); return; }
        Console.WriteLine($"  {note}");
        var retrieved = Retrieved(ctx, rel);
        foreach (var (name, series) in parsed)
            Norm(ctx, $"aaii/aaii_{name}.csv", MacroPointInTime.ToKnownCsv(series), "aaii", name, "release-rule",
                RuleText(ReleaseRule.SameDayRelease) + "; the sheet's date is the Thursday the week's result was published",
                "AAII member survey, weekly; fraction of respondents (0-1). " + note, retrieved, url);
    }

    /// <summary>Finds the header row (a "Bullish" and a "Bearish" cell) and reads every row below
    /// whose first cell is an Excel date and whose bullish/neutral/bearish cells are numbers.</summary>
    internal static List<(string Name, List<KnownValue> Series)>? ParseAaii(LegacyXlsReader xls, out string note)
    {
        foreach (var sheet in xls.Sheets)
        {
            int headerRow = -1, bull = -1, neu = -1, bear = -1;
            for (int r = 0; r <= Math.Min(sheet.MaxRow, 30) && headerRow < 0; r++)
            {
                for (int c = 0; c < 20; c++)
                {
                    if (sheet.Get(r, c) is not string s) continue;
                    var t = s.Trim();
                    if (t.Equals("Bullish", StringComparison.OrdinalIgnoreCase)) bull = c;
                    else if (t.Equals("Neutral", StringComparison.OrdinalIgnoreCase)) neu = c;
                    else if (t.Equals("Bearish", StringComparison.OrdinalIgnoreCase)) bear = c;
                }
                if (bull >= 0 && neu >= 0 && bear >= 0) headerRow = r; else { bull = neu = bear = -1; }
            }
            if (headerRow < 0) continue;

            var outBull = new List<KnownValue>();
            var outNeu = new List<KnownValue>();
            var outBear = new List<KnownValue>();
            var outSpread = new List<KnownValue>();
            int skipped = 0;
            var seen = new HashSet<DateOnly>();
            for (int r = headerRow + 1; r <= sheet.MaxRow; r++)
            {
                if (sheet.Get(r, 0) is not double serial || serial < 10000 || serial > 80000) { skipped++; continue; }
                if (sheet.Get(r, bull) is not double b || sheet.Get(r, neu) is not double n || sheet.Get(r, bear) is not double be)
                { skipped++; continue; }
                var d = xls.SerialToDate(serial);
                if (!seen.Add(d)) { skipped++; continue; }
                var k = MacroPointInTime.KnownFrom(d, ReleaseRule.SameDayRelease);
                outBull.Add(new KnownValue(d, b, k));
                outNeu.Add(new KnownValue(d, n, k));
                outBear.Add(new KnownValue(d, be, k));
                outSpread.Add(new KnownValue(d, b - be, k));
            }
            if (outBull.Count < 100) continue;
            foreach (var l in new[] { outBull, outNeu, outBear, outSpread }) l.Sort((x, y) => x.Date.CompareTo(y.Date));
            note = $"sheet '{sheet.Name}', header row {headerRow + 1}, {outBull.Count} weeks {outBull[0].Date:yyyy-MM-dd} → {outBull[^1].Date:yyyy-MM-dd}, {skipped} non-data rows skipped";
            return new() { ("bullish", outBull), ("neutral", outNeu), ("bearish", outBear), ("bull_minus_bear", outSpread) };
        }
        note = "no sheet with Bullish/Neutral/Bearish columns and 100+ dated rows";
        return null;
    }

    // ── Cboe put/call archives ───────────────────────────────────────────────

    private static async Task Cboe(Ctx ctx)
    {
        Console.WriteLine();
        Console.WriteLine("== Cboe put/call ratio archives (end 2019-10-04) ==");
        var stitched = new Dictionary<string, SortedDictionary<DateOnly, (double V, string From)>>();
        DateTime? retrieved = null;
        foreach (var (file, product) in CboeFiles)
        {
            var url = $"https://cdn.cboe.com/resources/options/volume_and_call_put_ratios/{file}";
            var rel = $"cboe/raw/{file}";
            var bytes = await Raw(ctx, rel, "cboe", file, url,
                validate: b => Encoding.UTF8.GetString(b).Contains("Ratio", StringComparison.OrdinalIgnoreCase) ? null : "no ratio column");
            if (bytes == null) continue;
            retrieved ??= Retrieved(ctx, rel);
            var cols = ParseCboe(Encoding.UTF8.GetString(bytes));
            if (cols.Count == 0) { ctx.Fail("cboe", file, "no DATE header / ratio column found"); continue; }
            foreach (var (col, rows) in cols)
            {
                var name = $"{Path.GetFileNameWithoutExtension(file)}_{col}";
                Norm(ctx, $"cboe/{name}.csv", MacroPointInTime.ToKnownCsv(rows), "cboe", name, "release-rule",
                    RuleText(ReleaseRule.MarketClose) + " (Cboe posts the day's totals after the close)",
                    $"Cboe {product}; column '{col}'", Retrieved(ctx, rel), url);
                string? key = col switch
                {
                    "total" or "equity" or "index" => col,
                    _ => null,
                };
                if (key == null) continue;
                if (!stitched.TryGetValue(key, out var map)) stitched[key] = map = new();
                foreach (var r in rows) map[r.Date] = (r.Value, name); // later files (newer methodology) win
            }
        }
        foreach (var (key, map) in stitched)
        {
            var rows = map.Select(kv => new KnownValue(kv.Key, kv.Value.V, kv.Key)).ToList();
            var parts = map.Values.GroupBy(v => v.From).Select(g => $"{g.Key} {g.Count()} days");
            Norm(ctx, $"cboe/cboe_{key}_pc_stitched.csv", MacroPointInTime.ToKnownCsv(rows), "cboe", $"{key}_stitched", "release-rule",
                RuleText(ReleaseRule.MarketClose),
                $"{key} put/call stitched across archives, later file wins on overlapping days ({string.Join("; ", parts)}). " +
                "Volume basis changes: OCC cleared volume to 2012-05-31, preliminary after; equity excludes ETPs from 2012-06-11.",
                retrieved, null);
        }
    }

    /// <summary>Each Cboe file has a disclaimer, then a header whose first cell is DATE /
    /// Trade_date, then M/D/YYYY rows. Every column whose header says "P/C" or "Put/Call Ratio"
    /// is returned, named total/index/equity/spx/vix/etp/ratio.</summary>
    internal static List<(string Col, List<KnownValue> Rows)> ParseCboe(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        int h = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^\s*(DATE|Trade_date|Date)\s*,", RegexOptions.IgnoreCase));
        var result = new List<(string, List<KnownValue>)>();
        if (h < 0) return result;
        var header = lines[h].Split(',');
        var ratioCols = new List<(int Idx, string Name)>();
        for (int i = 1; i < header.Length; i++)
        {
            var c = header[i].Trim();
            if (!Regex.IsMatch(c, @"P/C|Put/Call Ratio", RegexOptions.IgnoreCase)) continue;
            string name = Regex.IsMatch(c, "TOTAL", RegexOptions.IgnoreCase) ? "total"
                : Regex.IsMatch(c, "INDEX", RegexOptions.IgnoreCase) ? "index"
                : Regex.IsMatch(c, "EQUITY", RegexOptions.IgnoreCase) ? "equity"
                : Regex.IsMatch(c, "SPX", RegexOptions.IgnoreCase) ? "spx"
                : Regex.IsMatch(c, "VIX", RegexOptions.IgnoreCase) ? "vix"
                : "ratio";
            ratioCols.Add((i, name));
        }
        foreach (var (idx, name) in ratioCols)
        {
            var rows = new SortedDictionary<DateOnly, double>();
            for (int i = h + 1; i < lines.Length; i++)
            {
                var f = lines[i].Split(',');
                if (f.Length <= idx) continue;
                if (!DateOnly.TryParseExact(f[0].Trim(), new[] { "M/d/yyyy", "MM/dd/yyyy" }, Inv, DateTimeStyles.None, out var d)) continue;
                if (!double.TryParse(f[idx].Trim(), NumberStyles.Float, Inv, out var v)) continue;
                rows[d] = v;
            }
            if (rows.Count > 0) result.Add((name, rows.Select(kv => new KnownValue(kv.Key, kv.Value, kv.Key)).ToList()));
        }
        // A file with one ratio column is named for its product by the caller's file name.
        if (result.Count == 1 && result[0].Item1 == "ratio")
        {
            var only = result[0];
            var nm = Regex.IsMatch(lines[Math.Max(0, h - 1)], "TOTAL", RegexOptions.IgnoreCase) ? "total"
                : Regex.IsMatch(lines[Math.Max(0, h - 1)], "EQUITY", RegexOptions.IgnoreCase) ? "equity"
                : Regex.IsMatch(lines[Math.Max(0, h - 1)], "INDEX", RegexOptions.IgnoreCase) ? "index"
                : Regex.IsMatch(lines[Math.Max(0, h - 1)], "ETP", RegexOptions.IgnoreCase) ? "etp"
                : "ratio";
            result[0] = (nm, only.Item2);
        }
        return result;
    }

    // ── On-chain (keyless) ───────────────────────────────────────────────────

    private static async Task CoinMetrics(Ctx ctx)
    {
        Console.WriteLine();
        Console.WriteLine("== CoinMetrics community API, BTC from 2010 (the lab's xs_ files start 2015) ==");
        string url = "https://community-api.coinmetrics.io/v4/timeseries/asset-metrics?assets=btc" +
                     $"&metrics={string.Join(",", CoinMetricsMetrics)}&frequency=1d&start_time=2009-01-03" +
                     "&page_size=10000&paging_from=start&pretty=false";
        var sinks = CoinMetricsMetrics.ToDictionary(m => m, _ => new SortedDictionary<DateOnly, double>());
        int page = 0;
        DateTime? retrieved = null;
        string firstUrl = url;
        while (!string.IsNullOrEmpty(url) && page < 20)
        {
            var rel = $"coinmetrics/raw/btc_page{page:00}.json";
            var bytes = await Raw(ctx, rel, "coinmetrics", "btc", url,
                validate: b => b.Length > 0 && b[0] == '{' ? null : "not JSON");
            if (bytes == null) return;
            retrieved ??= Retrieved(ctx, rel);
            var doc = JObject.Parse(Encoding.UTF8.GetString(bytes));
            foreach (var row in doc["data"] as JArray ?? new JArray())
            {
                var t = (string?)row["time"];
                if (t == null) continue;
                var d = DateOnly.ParseExact(t[..10], "yyyy-MM-dd", Inv);
                foreach (var m in CoinMetricsMetrics)
                    if (row[m] is JToken tok && tok.Type != JTokenType.Null &&
                        double.TryParse((string?)tok, NumberStyles.Float, Inv, out var v))
                        sinks[m][d] = v;
            }
            url = (string?)doc["next_page_url"] ?? "";
            page++;
        }
        foreach (var (m, sink) in sinks)
        {
            if (sink.Count == 0) { ctx.Fail("coinmetrics", m, "no data returned"); continue; }
            var rows = sink.Select(kv => new KnownValue(kv.Key, kv.Value, MacroPointInTime.KnownFrom(kv.Key, ReleaseRule.OnChainNextDay))).ToList();
            Norm(ctx, $"coinmetrics/btc_{m.ToLowerInvariant()}.csv", MacroPointInTime.ToKnownCsv(rows), "coinmetrics", $"btc_{m}",
                "release-rule", RuleText(ReleaseRule.OnChainNextDay),
                "Community tier, keyless. Realized price = CapMrktCurUSD / CapMVRVCur / SplyCur; Puell multiple = IssTotUSD / its 365-day mean. " +
                "CoinMetrics may restate history (no vintages).", retrieved, firstUrl);
        }
    }

    private static async Task Blockchain(Ctx ctx)
    {
        Console.WriteLine();
        Console.WriteLine("== Blockchain.com charts API (keyless) ==");
        foreach (var chart in BlockchainCharts)
        {
            var url = $"https://api.blockchain.info/charts/{chart}?timespan=all&format=json&sampled=false";
            var rel = $"blockchain/raw/{chart}.json";
            var bytes = await Raw(ctx, rel, "blockchain", chart, url,
                validate: b => Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 40)).Contains("\"status\":\"ok\"") ? null : $"unexpected: '{Snippet(b)}'");
            if (bytes == null) continue;
            var doc = JObject.Parse(Encoding.UTF8.GetString(bytes));
            var map = new SortedDictionary<DateOnly, double>();
            foreach (var p in doc["values"] as JArray ?? new JArray())
                map[DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds((long)p["x"]!).UtcDateTime)] = (double)p["y"]!;
            var rows = map.Select(kv => new KnownValue(kv.Key, kv.Value, MacroPointInTime.KnownFrom(kv.Key, ReleaseRule.OnChainNextDay))).ToList();
            Norm(ctx, $"blockchain/{chart}.csv", MacroPointInTime.ToKnownCsv(rows), "blockchain", chart, "release-rule",
                RuleText(ReleaseRule.OnChainNextDay), $"{(string?)doc["name"]} ({(string?)doc["unit"]}): {(string?)doc["description"]}",
                Retrieved(ctx, rel), url);
        }
    }

    /// <summary>Prints every non-empty cell of a legacy .xls (debug aid for the AAII layout).</summary>
    public static int DumpXls(string path, int maxRows)
    {
        var x = LegacyXlsReader.Read(File.ReadAllBytes(path));
        Console.WriteLine($"date1904={x.Date1904}");
        foreach (var s in x.Sheets)
        {
            Console.WriteLine($"== sheet '{s.Name}' cells={s.Cells.Count} maxRow={s.MaxRow}");
            foreach (var g in s.Cells.GroupBy(c => c.Key.Row).OrderBy(g => g.Key).Where(g => g.Key < maxRows || g.Key > s.MaxRow - 3))
                Console.WriteLine($"  r{g.Key}: " + string.Join(" | ", g.OrderBy(c => c.Key.Col).Select(c => $"c{c.Key.Col}={c.Value}")));
        }
        return 0;
    }
}
