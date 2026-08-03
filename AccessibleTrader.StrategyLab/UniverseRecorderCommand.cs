using System.Globalization;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Records the crypto universe as it stands TODAY, so that one day it can be studied honestly.
///
/// <para>
/// ── The only problem this solves, and it cannot be solved any other way ─────
/// Every backtest of a token-quality rule run against today's listings is worthless, because the
/// tokens that went to zero are <b>not in today's listings</b>. They were delisted, the project
/// abandoned the ticker, or the aggregator dropped them. Screen the survivors and every quality
/// metric looks predictive — the assets that would have falsified it are missing from the sample.
/// </para>
///
/// <para>
/// Survivorship is the one bias that <b>cannot be corrected after the fact</b>. There is no clever
/// control, no reweighting, no statistical adjustment: either the dead assets were recorded while
/// they were alive, or the question is permanently unanswerable. Which is why this is the single
/// piece of the whole data layer where <i>delay costs something unrecoverable</i>, and why it is
/// worth running before anyone has decided whether the research is a good idea.
/// </para>
///
/// <para>
/// ── Wide and shallow, deliberately ──────────────────────────────────────────
/// <c>/coins/markets</c> returns 250 assets per call with rank, market cap, fully diluted value,
/// circulating and max supply, volume, and all-time high and low. Four calls cover the top 1,000.
/// The per-coin detail endpoint carries more (developer activity, disclosure links) but costs one
/// call per asset, which is 1,000 calls a day against a free tier that allows tens per minute.
/// </para>
///
/// <para>
/// The wide sweep is the one that matters. To defeat survivorship you need to know the asset
/// <b>existed</b> on a given day and what its rank and size were — not the full dossier. The deep
/// detail can be sampled later on a watchlist, and for assets that survive it can be fetched at any
/// time. For assets that die, existence and size are the whole record and there is no second chance.
/// </para>
///
/// <para>
/// ── It lives in a COMMITTED directory, gzipped ──────────────────────────────
/// Not under <c>strategy-lab-data/</c>, which is gitignored. Everything else in this project's
/// research data can be re-fetched from a provider if the disk dies; this cannot. An archive whose
/// entire value is that it is irreplaceable, kept only on one machine and excluded from backup,
/// would be the single worst-designed artefact in the repository. Gzipped it costs about
/// <b>65 KB a day — 23 MB a year</b>, which is small beside the build artefacts already in this
/// repo's history and buys something none of them do.
/// </para>
///
/// <para>
/// ── Append-only and immutable ───────────────────────────────────────────────
/// One file per day, never rewritten. Re-running on the same day is a no-op unless
/// <c>--force</c> is passed, because a research record that can be silently overwritten by a later
/// run is not a record. The observation date is stored ON each row rather than inferred from the
/// filename, so a mis-sorted or renamed file cannot silently shift history.
/// </para>
///
/// <para>
/// ── The delta is the finding ────────────────────────────────────────────────
/// On each run the new snapshot is compared against the most recent previous one, and the assets
/// that <b>disappeared</b> are reported. That list is the survivorship signal itself, and it is the
/// thing no amount of later cleverness can reconstruct.
/// </para>
/// </summary>
public static class UniverseRecorderCommand
{
    private const string MarketsUrl =
        "https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&order=market_cap_desc"
      + "&per_page=250&sparkline=false&page=";

    /// <summary>One asset as observed on one day. Short keys: this file is written every day forever.</summary>
    internal sealed class Row
    {
        [JsonProperty("d")] public string Date { get; set; } = "";
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("s")] public string Symbol { get; set; } = "";
        [JsonProperty("n")] public string Name { get; set; } = "";
        [JsonProperty("r")] public int? Rank { get; set; }
        [JsonProperty("p")] public double? Price { get; set; }
        [JsonProperty("mc")] public double? MarketCap { get; set; }
        [JsonProperty("fdv")] public double? FullyDiluted { get; set; }
        [JsonProperty("circ")] public double? Circulating { get; set; }
        [JsonProperty("max")] public double? MaxSupply { get; set; }
        [JsonProperty("vol")] public double? Volume24h { get; set; }
        [JsonProperty("ath")] public double? Ath { get; set; }
        [JsonProperty("athd")] public string? AthDate { get; set; }
    }

    /// <summary>
    /// Anchors a relative archive path to the REPOSITORY ROOT rather than the working directory.
    ///
    /// <para>
    /// Every other command here defaults to a CWD-relative snapshot directory, which is harmless
    /// because those files are re-fetchable. This one is not. Run from the solution root one day and
    /// from the lab directory the next and you would silently maintain TWO archives, each with holes
    /// where the other has data — and the delta report, which is the whole point, would read those
    /// holes as mass delistings. Anchoring removes the failure mode rather than documenting it.
    /// </para>
    /// </summary>
    internal static string Anchor(string outDir)
    {
        if (Path.IsPathRooted(outDir)) return outDir;
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        return dir == null ? outDir : Path.Combine(dir.FullName, outDir);
    }

    public static async Task<int> RunAsync(string outDir, int pages, bool force, int delayMs = 3000)
    {
        outDir = Anchor(outDir);
        Directory.CreateDirectory(outDir);

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string path = Path.Combine(outDir, $"crypto_{today}.jsonl.gz");

        if (File.Exists(path) && !force)
        {
            Console.WriteLine($"Already recorded today: {path}");
            Console.WriteLine("A snapshot is a research record; re-running would overwrite history. Pass --force to replace it.");
            ReportDelta(outDir, path);
            return 0;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("User-Agent", "AccessibleTrader/2.2 (accessible-trade-terminal)");

        var rows = new List<Row>();
        for (int page = 1; page <= pages; page++)
        {
            string json;
            try
            {
                json = await http.GetStringAsync(MarketsUrl + page);
            }
            catch (Exception ex)
            {
                // A partial sweep is still worth keeping — a day with the top 500 recorded beats a
                // day with nothing. But say so loudly, because a silently short snapshot would look
                // like assets had disappeared when the next delta is computed.
                Console.Error.WriteLine($"  ! page {page} failed ({ex.Message}); keeping the {rows.Count} rows already fetched.");
                break;
            }

            if (JToken.Parse(json) is not JArray arr || arr.Count == 0)
            {
                Console.WriteLine($"  page {page}: empty — the free tier rate-limits; stopping here.");
                break;
            }

            foreach (var c in arr) rows.Add(ToRow(c, today));
            Console.WriteLine($"  page {page}: {arr.Count} assets (total {rows.Count})");

            // The free tier is aggressive about bursts, and a 429 mid-sweep truncates the snapshot.
            if (page < pages) await Task.Delay(delayMs);
        }

        if (rows.Count == 0)
        {
            Console.Error.WriteLine("Recorded nothing — refusing to write an empty snapshot, which would read as a mass delisting.");
            return 2;
        }

        // Write to a temporary file and move into place, so an interrupted run cannot leave a
        // half-written day that later looks like a real observation.
        string tmp = path + ".partial";
        await using (var fs = File.Create(tmp))
        await using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        await using (var w = new StreamWriter(gz))
            foreach (var r in rows)
                await w.WriteLineAsync(JsonConvert.SerializeObject(r, Formatting.None));
        File.Move(tmp, path, overwrite: true);

        Console.WriteLine($"Wrote {rows.Count:N0} assets → {path} ({new FileInfo(path).Length / 1024:N0} KB)");
        ReportDelta(outDir, path);
        return 0;
    }

    private static Row ToRow(JToken c, string date)
    {
        static double? Num(JToken? t) =>
            t == null || t.Type == JTokenType.Null ? null
            : double.TryParse(t.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

        return new Row
        {
            Date = date,
            Id = c["id"]?.ToString() ?? "",
            Symbol = c["symbol"]?.ToString() ?? "",
            Name = c["name"]?.ToString() ?? "",
            Rank = int.TryParse(c["market_cap_rank"]?.ToString(), out var r) ? r : null,
            Price = Num(c["current_price"]),
            MarketCap = Num(c["market_cap"]),
            FullyDiluted = Num(c["fully_diluted_valuation"]),
            Circulating = Num(c["circulating_supply"]),
            MaxSupply = Num(c["max_supply"]),
            Volume24h = Num(c["total_volume"]),
            Ath = Num(c["ath"]),
            AthDate = c["ath_date"]?.ToString(),
        };
    }

    // ── The delta, which is the whole point ─────────────────────────────────────

    /// <summary>
    /// What changed since the previous snapshot. The DISAPPEARED list is the survivorship signal —
    /// the assets that were in the ranked universe and are not any more. That list is exactly what
    /// no later analysis can reconstruct, and it is why this command exists.
    /// </summary>
    private static void ReportDelta(string outDir, string todayPath)
    {
        var files = Snapshots(outDir);
        int idx = files.IndexOf(todayPath);
        if (idx <= 0)
        {
            Console.WriteLine();
            Console.WriteLine("First snapshot — no delta yet. The value of this file is entirely in the future:");
            Console.WriteLine("run it daily and in a year it answers questions that are unanswerable today.");
            return;
        }

        var prev = Load(files[idx - 1]);
        var now = Load(todayPath);
        if (prev.Count == 0 || now.Count == 0) return;

        var gone = prev.Keys.Except(now.Keys).ToList();
        var arrived = now.Keys.Except(prev.Keys).ToList();

        Console.WriteLine();
        Console.WriteLine($"── Change since {Path.GetFileName(files[idx - 1])} " + new string('─', 30));
        Console.WriteLine($"  {arrived.Count} new to the ranked universe, {gone.Count} gone from it.");

        if (gone.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  GONE — this is the survivorship record. Nothing recoverable later:");
            foreach (var id in gone.OrderBy(g => prev[g].Rank ?? int.MaxValue).Take(15))
                Console.WriteLine($"    {prev[id].Symbol,-10} {prev[id].Name,-28} was rank {prev[id].Rank}, "
                                + $"mcap {prev[id].MarketCap:N0}");
            if (gone.Count > 15) Console.WriteLine($"    …and {gone.Count - 15} more.");
            Console.WriteLine();
            Console.WriteLine("  NOTE: leaving the ranked top-N is not the same as dying. An asset can simply have");
            Console.WriteLine("  fallen below the cut. Widen --pages to reduce that ambiguity.");
        }

        if (arrived.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  NEW:");
            foreach (var id in arrived.OrderBy(a => now[a].Rank ?? int.MaxValue).Take(10))
                Console.WriteLine($"    {now[id].Symbol,-10} {now[id].Name,-28} rank {now[id].Rank}, "
                                + $"mcap {now[id].MarketCap:N0}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {files.Count} daily snapshots on record, {DateOf(files[0])} → {DateTime.UtcNow:yyyy-MM-dd}.");
    }

    /// <summary>Reads a snapshot, gzipped or not — early days were written plain.</summary>
    internal static Dictionary<string, Row> Load(string path)
    {
        var d = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var line in ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var r = JsonConvert.DeserializeObject<Row>(line);
                if (r != null && r.Id.Length > 0) d[r.Id] = r;
            }
            catch { /* one bad line must not lose the day */ }
        }
        return d;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        if (!path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var l in File.ReadLines(path)) yield return l;
            yield break;
        }
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var r = new StreamReader(gz);
        string? line;
        while ((line = r.ReadLine()) != null) yield return line;
    }

    /// <summary>Every snapshot on disk, oldest first, whichever extension it was written with.</summary>
    internal static List<string> Snapshots(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, "crypto_*.jsonl*").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList()
            : new List<string>();

    /// <summary>The yyyy-MM-dd embedded in a snapshot filename.</summary>
    internal static string DateOf(string path)
    {
        var n = Path.GetFileName(path);
        return n.Length >= 17 ? n.Substring(7, 10) : "";
    }

    // ── Reporting over the accumulated history ──────────────────────────────────

    /// <summary>
    /// What the archive currently supports. Printed rather than computed into a result, because for
    /// the first months the honest answer is "not enough yet" and saying so plainly is the point.
    /// </summary>
    public static int Status(string outDir)
    {
        outDir = Anchor(outDir);
        if (!Directory.Exists(outDir))
        {
            Console.WriteLine($"No universe archive at {outDir}. Start one with: StrategyLab record-universe");
            return 1;
        }

        var files = Snapshots(outDir);
        if (files.Count == 0)
        {
            Console.WriteLine("No snapshots recorded yet.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("═════ CRYPTO UNIVERSE ARCHIVE ═════");
        Console.WriteLine($"{files.Count} daily snapshots, {DateOf(files[0])} → {DateOf(files[^1])}");

        var first = Load(files[0]);
        var last = Load(files[^1]);
        var everSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in files) foreach (var k in Load(f).Keys) everSeen.Add(k);

        Console.WriteLine($"  assets in the first snapshot : {first.Count:N0}");
        Console.WriteLine($"  assets in the latest         : {last.Count:N0}");
        Console.WriteLine($"  distinct assets ever seen    : {everSeen.Count:N0}");
        Console.WriteLine($"  seen once and never since    : {everSeen.Except(last.Keys).Count():N0}");

        var span = files.Count;
        Console.WriteLine();
        Console.WriteLine(span < 90
            ? $"  {span} days is not yet enough to test anything. A token-quality rule needs enough\n"
            + "  elapsed time for weak assets to actually fail; before that the archive is only a record."
            : $"  {span} days recorded. Long enough to begin measuring whether the quality checks\n"
            + "  precede survival — using ONLY assets that were present on the day a rule would have run.");
        return 0;
    }
}
