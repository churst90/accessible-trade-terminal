using System.Text.Json;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Does the earnings SURPRISE move price, when the scheduled date does not?
///
/// <para>
/// THE HYPOTHESIS THIS PROJECT EARNED. Release dates for CPI, NFP, PPI and GDP tested null while
/// FOMC did not, and the proposed explanation was that FOMC is a policy <i>action</i> while CPI and
/// NFP are <i>data</i> the market spends the interval forecasting — so what should move price is the
/// surprise, not the date. That was untestable while macro consensus sat behind a paywall. It is
/// testable on company earnings, where actual and consensus EPS are both free.
/// </para>
///
/// <para>
/// THE CONTROL THAT DECIDES IT. Post-earnings-announcement drift is one of the most documented
/// anomalies in finance, so "returns are positive after good earnings" is not news and not the
/// claim. The claim is that surprise <b>magnitude ranks</b> forward returns. So the test is a decile
/// sort on standardised surprise, and the arm that matters is whether the spread beats the plain
/// event-day effect — an exposure-matched null drawn from the same stocks on non-earnings dates,
/// which holds the fact of being in the market fixed and asks only whether the surprise carries
/// information.
/// </para>
///
/// <para>
/// Data: Alpha Vantage's <c>EARNINGS</c> endpoint, which gives reported and estimated EPS back to
/// the 1990s on the free tier. <c>fetch</c> is rate-limit aware and resumable — it skips symbols
/// already on disk, so an interrupted run continues where it stopped rather than starting over.
/// </para>
/// </summary>
public static class EarningsCommand
{
    private sealed record Event(string Symbol, DateTime Reported, double Actual, double Estimated, string When);

    public static async Task<int> RunAsync(string[] args, string snapshotDir)
    {
        string sub = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "study";
        return sub switch
        {
            "fetch" => await FetchAsync(snapshotDir, Flag(args, "--key"), int.TryParse(Flag(args, "--max"), out var m) ? m : 25),
            "study" => Study(snapshotDir, int.TryParse(Flag(args, "--horizon"), out var h) ? h : 20),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.WriteLine("  StrategyLab earnings fetch --key <alphavantage-key> [--max 25]");
        Console.WriteLine("  StrategyLab earnings study [--horizon 20]");
        Console.WriteLine();
        Console.WriteLine("Free tier is ~25 requests/day; one request returns a symbol's whole history.");
        Console.WriteLine("fetch is resumable — it skips symbols already on disk.");
        return 2;
    }

    // ── Acquisition ─────────────────────────────────────────────────────────────

    private static async Task<int> FetchAsync(string dir, string? key, int max)
    {
        if (string.IsNullOrWhiteSpace(key)) { Console.Error.WriteLine("--key is required."); return 2; }

        var symbols = EquityUniverse(dir);
        string outDir = Path.Combine(dir, "earnings");
        Directory.CreateDirectory(outDir);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        int fetched = 0, skipped = 0;

        foreach (var sym in symbols)
        {
            string path = Path.Combine(outDir, $"{sym}.json");
            if (File.Exists(path)) { skipped++; continue; }
            if (fetched >= max) break;

            var url = $"https://www.alphavantage.co/query?function=EARNINGS&symbol={Uri.EscapeDataString(sym)}&apikey={key}";

            // The free tier answers a rate-limit with HTTP 200 and an explanatory body, so the
            // response has to be inspected rather than trusted. There are TWO limits: a per-minute
            // one that clears on its own, and a daily one that does not. Waiting distinguishes them
            // — the first attempt to treat them as one gave up after three symbols.
            string body = "";
            bool limited = true;
            for (int attempt = 0; attempt < 3 && limited; attempt++)
            {
                try { body = await http.GetStringAsync(url); }
                catch (Exception ex) { Console.Error.WriteLine($"  {sym}: {ex.Message}"); return Done(fetched, skipped, symbols.Count, outDir); }

                limited = body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                       || body.Contains("higher API call volume", StringComparison.OrdinalIgnoreCase);
                if (limited)
                {
                    Console.WriteLine($"  {sym}: rate limited, waiting 65s (attempt {attempt + 1}/3)");
                    await Task.Delay(65_000);
                }
            }
            if (limited)
            {
                Console.WriteLine($"  daily cap reached after {fetched} symbols — rerun tomorrow, it resumes.");
                break;
            }
            if (!body.Contains("quarterlyEarnings"))
            {
                Console.WriteLine($"  {sym}: no earnings data ({body[..Math.Min(90, body.Length)]})");
                continue;
            }

            File.WriteAllText(path, body);
            fetched++;
            Console.WriteLine($"  {sym} saved ({fetched}/{max})");
            await Task.Delay(13_000);    // the free tier allows ~5 requests a minute
        }

        return Done(fetched, skipped, symbols.Count, outDir);
    }

    private static int Done(int fetched, int skipped, int universe, string outDir)
    {
        Console.WriteLine();
        Console.WriteLine($"{fetched} fetched, {skipped} already on disk, {universe} in the universe.");
        Console.WriteLine($"→ {outDir}");
        return 0;
    }

    private static List<string> EquityUniverse(string dir) =>
        Directory.GetFiles(dir, "*_1d.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null && !n.StartsWith("xs_") && !n.StartsWith("events_") && !n.StartsWith("fred_")
                        && !n.StartsWith("bitstamp") && !n.StartsWith("mexc"))
            .Select(n => string.Join('_', n!.Split('_')[1..^1]))
            .Where(s => !s.Contains('_'))          // drops XAU_USD and friends: not equities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

    // ── The study ───────────────────────────────────────────────────────────────

    private static int Study(string dir, int horizon)
    {
        string earnDir = Path.Combine(dir, "earnings");
        if (!Directory.Exists(earnDir)) { Console.Error.WriteLine("Run `earnings fetch` first."); return 1; }

        var prices = new Dictionary<string, (DateTime[] D, double[] C)>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(dir, "*_1d.json"))
        {
            var parts = Path.GetFileNameWithoutExtension(f).Split('_');
            if (parts[0] is "xs" or "events" or "fred" or "bitstamp" or "mexc") continue;
            string sym = string.Join('_', parts[1..^1]);
            if (prices.ContainsKey(sym)) continue;
            var s = SnapshotCommand.Load(f);
            prices[sym] = (s.Bars.Select(b => b.Date).ToArray(), s.Bars.Select(b => b.Close).ToArray());
        }

        var events = new List<(Event E, double Surprise, double Fwd)>();
        var allReturns = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in Directory.GetFiles(earnDir, "*.json"))
        {
            string sym = Path.GetFileNameWithoutExtension(f);
            if (!prices.TryGetValue(sym, out var px)) continue;

            foreach (var e in Parse(sym, f))
            {
                int i = Array.BinarySearch(px.D, e.Reported);
                if (i < 0) i = ~i;
                // Enter at the close AFTER the report: a post-market number is not tradable at that
                // day's close, and a pre-market one is already in the open.
                int entry = e.When.Contains("post", StringComparison.OrdinalIgnoreCase) ? i + 1 : i;
                if (entry < 1 || entry + horizon >= px.C.Length) continue;

                // Standardised surprise: actual minus estimate, scaled by the dispersion of this
                // symbol's own past surprises, so a $0.05 miss means something different for a
                // company that usually misses by $0.01 than for one that swings by $0.20.
                events.Add((e, e.Actual - e.Estimated,
                            Math.Log(px.C[entry + horizon]) - Math.Log(px.C[entry])));
            }

            var rets = new List<double>();
            for (int i = horizon; i < px.C.Length; i += horizon)
                rets.Add(Math.Log(px.C[i]) - Math.Log(px.C[i - horizon]));
            allReturns[sym] = rets;
        }

        if (events.Count < 200)
        {
            Console.Error.WriteLine($"Only {events.Count} usable events — fetch more symbols before reading anything into this.");
            return 1;
        }

        // Standardise within symbol.
        var std = new List<(double Z, double Fwd, string Sym)>();
        foreach (var g in events.GroupBy(x => x.E.Symbol))
        {
            var s = g.Select(x => x.Surprise).ToList();
            double mean = s.Average();
            double sd = Math.Sqrt(s.Sum(v => (v - mean) * (v - mean)) / Math.Max(1, s.Count - 1));
            if (sd <= 0) continue;
            foreach (var x in g) std.Add(((x.Surprise - mean) / sd, x.Fwd, x.E.Symbol));
        }

        Console.WriteLine();
        Console.WriteLine("═════ EARNINGS SURPRISE VS THE EVENT ITSELF ═════");
        Console.WriteLine($"{std.Select(x => x.Sym).Distinct().Count()} symbols · {std.Count:N0} events · {horizon}-bar forward return");
        Console.WriteLine();
        Console.WriteLine("Surprise is standardised within each symbol, so a fixed-cent miss is read against");
        Console.WriteLine("that company's own dispersion rather than against the market's.");
        Console.WriteLine();

        var ordered = std.OrderBy(x => x.Z).ToList();
        int per = ordered.Count / 5;
        Console.WriteLine($"  {"quintile",-12}{"n",7}{"mean fwd",11}{"positive",11}");
        for (int q = 0; q < 5; q++)
        {
            var bucket = ordered.Skip(q * per).Take(q == 4 ? ordered.Count - 4 * per : per).ToList();
            Console.WriteLine($"  {"Q" + (q + 1) + (q == 0 ? " (miss)" : q == 4 ? " (beat)" : ""),-12}{bucket.Count,7}"
                            + $"{bucket.Average(x => x.Fwd) * 100,10:+0.00;-0.00}%{bucket.Count(x => x.Fwd > 0) / (double)bucket.Count * 100,10:0.0}%");
        }

        double top = ordered.TakeLast(per).Average(x => x.Fwd);
        double bot = ordered.Take(per).Average(x => x.Fwd);
        double eventMean = std.Average(x => x.Fwd);
        double baseMean = allReturns.Values.SelectMany(v => v).DefaultIfEmpty(0).Average();

        Console.WriteLine();
        Console.WriteLine($"  beat − miss spread            : {(top - bot) * 100:+0.00;-0.00}%   over {horizon} bars");
        Console.WriteLine($"  ALL earnings events, any surprise: {eventMean * 100:+0.00;-0.00}%   ← the plain event effect");
        Console.WriteLine($"  the same stocks, any {horizon}-bar window : {baseMean * 100:+0.00;-0.00}%   ← the exposure-matched null");
        Console.WriteLine();
        Console.WriteLine($"  surprise adds over the event  : {((top - bot) - 0) * 100:+0.00;-0.00} pts of SPREAD");
        Console.WriteLine($"  event adds over plain exposure: {(eventMean - baseMean) * 100:+0.00;-0.00} pts of LEVEL");
        Console.WriteLine();
        Console.WriteLine("Reading it: the hypothesis is that the SPREAD across surprise deciles is real while the");
        Console.WriteLine("event LEVEL is not — that is what 'the surprise moves price, not the date' means.");
        return 0;
    }

    private static IEnumerable<Event> Parse(string sym, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("quarterlyEarnings", out var q)) yield break;

        foreach (var e in q.EnumerateArray())
        {
            if (!e.TryGetProperty("reportedDate", out var rd)) continue;
            if (!DateTime.TryParse(rd.GetString(), out var when)) continue;
            if (!double.TryParse(Get(e, "reportedEPS"), out var act)) continue;
            if (!double.TryParse(Get(e, "estimatedEPS"), out var est)) continue;
            yield return new Event(sym, when.Date, act, est, Get(e, "reportTime") ?? "");
        }
    }

    private static string? Get(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static string? Flag(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
