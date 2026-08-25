using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// One-shot downloader for CFTC Commitments of Traders (COT) historical data.
/// Pulls annual ZIP archives of the Disaggregated Futures-Only report from cftc.gov,
/// parses the embedded text file, filters by contract name (case-insensitive substring
/// match on Market_and_Exchange_Names), and writes net Managed Money positioning as a
/// cross-series snapshot file shaped like every other xs_*.json the harness understands.
///
/// Why CFTC COT: it's the cross-asset analogue of crypto funding rate. Same structural
/// signal (smart-money / spec positioning extremes) on every futures contract from S&amp;P
/// to gold to oil to currencies. Free, weekly cadence, published every Friday for the
/// prior Tuesday's positions, archived back to 2006 in the disaggregated format.
///
/// Output: one xs_cftc_{symbol}_cot_1w.json per requested contract. The "value" series
/// is the net spec positioning normalized to open interest:
///   net_pct_oi = (managed_money_long - managed_money_short) / total_open_interest * 100
/// Range typically -40..+40. Positive = specs are net long; extreme positive = crowded
/// long (contrarian short signal historically). Extreme negative = capitulation long signal.
///
/// Contract filters use substring matching against the CFTC's verbose name format:
///   "BITCOIN - CHICAGO MERCANTILE EXCHANGE"  (substring: "BITCOIN")
///   "GOLD - COMMODITY EXCHANGE INC."          (substring: "GOLD ")
///   "WTI-PHYSICAL - NEW YORK MERCANTILE EXCHANGE" (substring: "WTI")
///   "E-MINI S&amp;P 500 - CHICAGO MERCANTILE EXCHANGE" (substring: "E-MINI S&amp;P 500")
///
/// CFTC publishes BTC futures data starting 2017-12 (CME launch). For earlier coverage
/// of crypto-correlated assets, gold and DXY work back to 2006.
/// </summary>
public static class CftcCotCommand
{
    // CFTC publishes COT data in two parallel reports with different schemas:
    //   • Disaggregated (commodities — gold, oil, ag, metals): "Managed Money" column
    //   • Traders in Financial Futures (TFF — equity index, currencies, rates, BTC):
    //     "Leveraged Funds" column. BTC is here, not in the commodities file.
    // We fetch both archives per year and try to match contracts in either.
    private const string DisaggUrl = "https://www.cftc.gov/files/dea/history/fut_disagg_txt_{0}.zip";
    private const string TffUrl    = "https://www.cftc.gov/files/dea/history/fut_fin_txt_{0}.zip";

    public static async Task<int> RunAsync(string outputDir, string[] contractFilters, int? startYear)
    {
        Directory.CreateDirectory(outputDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.Add("User-Agent", "AccessibleTrader-StrategyLab/1.0");

        int currentYear = DateTime.UtcNow.Year;
        int firstYear = startYear ?? 2010;  // disaggregated report starts ~2006; default to 2010 for relevance
        Console.WriteLine($"CFTC COT downloader — fetching {firstYear}..{currentYear} annual archives");
        Console.WriteLine($"Contract filters: {string.Join(", ", contractFilters)}");

        // Per-filter accumulator: contractKey -> sorted (timestamp -> net_pct_oi)
        var accumulators = new Dictionary<string, SortedDictionary<long, double>>();
        foreach (var f in contractFilters) accumulators[f] = new SortedDictionary<long, double>();

        // Track which exact "Market_and_Exchange_Names" matched each filter (for diagnostic).
        var matchedNames = new Dictionary<string, HashSet<string>>();
        foreach (var f in contractFilters) matchedNames[f] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int year = firstYear; year <= currentYear; year++)
        {
            int totalRows = 0;
            // Try both report types — Disaggregated (commodities) and TFF (financials).
            foreach (var (url, kind) in new[] {
                (string.Format(DisaggUrl, year), "DISAGG"),
                (string.Format(TffUrl, year),    "TFF"),
            })
            {
                try
                {
                    using var response = await http.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        // Some years may be missing one of the two reports — silent skip.
                        continue;
                    }
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    int rows = ParseZipInto(bytes, contractFilters, accumulators, matchedNames, kind);
                    totalRows += rows;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  {year} {kind}: {ex.Message}");
                }
            }
            Console.WriteLine($"  {year}: {totalRows} matching rows");
        }

        Console.WriteLine();
        foreach (var filter in contractFilters)
        {
            var data = accumulators[filter];
            var names = matchedNames[filter];
            Console.WriteLine($"=== {filter} ===");
            if (data.Count == 0)
            {
                Console.Error.WriteLine($"  No matching rows. Try a different substring.");
                Console.Error.WriteLine($"  (Run with --list to see all available contract names.)");
                continue;
            }
            Console.WriteLine($"  Matched {names.Count} distinct contract name(s):");
            foreach (var name in names.Take(5)) Console.WriteLine($"    {name}");
            if (names.Count > 5) Console.WriteLine($"    ... ({names.Count - 5} more)");

            var snap = new CrossSeriesSnapshot
            {
                Provider = "CFTC",
                Symbol   = $"{Sanitize(filter)}_COT",
                Timeframe = "1w",
                FetchedUtc = DateTime.UtcNow,
                PointCount = data.Count,
                Points = data.Select(kv => new TimedValue { Ts = kv.Key, Value = kv.Value }).ToList(),
            };
            var path = snap.FilePath(outputDir);
            await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(snap, Formatting.None));

            var firstDate = DateTimeOffset.FromUnixTimeMilliseconds(data.First().Key).UtcDateTime;
            var lastDate  = DateTimeOffset.FromUnixTimeMilliseconds(data.Last().Key).UtcDateTime;
            Console.WriteLine($"  → wrote {snap.PointCount} weekly points [{firstDate:yyyy-MM-dd} → {lastDate:yyyy-MM-dd}]");
            Console.WriteLine($"    {path}");
        }

        return 0;
    }

    /// <summary>
    /// Parses a CFTC annual disaggregated text ZIP. Extracts the .txt file, identifies the
    /// header row to map column names to indices defensively (CFTC has changed column ordering
    /// twice in the report's history), and accumulates rows whose Market_and_Exchange_Names
    /// match any of the supplied filter substrings (case-insensitive).
    /// </summary>
    private static int ParseZipInto(
        byte[] zipBytes,
        string[] contractFilters,
        Dictionary<string, SortedDictionary<long, double>> accumulators,
        Dictionary<string, HashSet<string>> matchedNames,
        string reportKind)  // "DISAGG" or "TFF" — selects which spec column to read
    {
        int totalMatches = 0;
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (!entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? headerLine = reader.ReadLine();
            if (headerLine == null) continue;
            var headers = ParseCsvLine(headerLine);
            int idxName = FindColumn(headers, "Market_and_Exchange_Names");
            int idxDate = FindColumn(headers, "Report_Date_as_YYYY-MM-DD");
            int idxOi   = FindColumn(headers, "Open_Interest_All");
            int idxLong, idxShort;

            if (reportKind == "TFF")
            {
                // Traders in Financial Futures: speculators are "Leveraged Funds".
                idxLong  = FindColumn(headers, "Lev_Money_Positions_Long_All");
                idxShort = FindColumn(headers, "Lev_Money_Positions_Short_All");
            }
            else
            {
                // Disaggregated commodities: speculators are "Managed Money".
                idxLong  = FindColumn(headers, "M_Money_Positions_Long_All");
                idxShort = FindColumn(headers, "M_Money_Positions_Short_All");
            }

            if (idxName < 0 || idxDate < 0 || idxLong < 0 || idxShort < 0 || idxOi < 0)
            {
                Console.Error.WriteLine($"  [{reportKind}] Header column mapping failed in {entry.Name} — skipping");
                continue;
            }

            string? line = reader.ReadLine();
            while (line != null)
            {
                var parts = ParseCsvLine(line);
                if (parts.Length > Math.Max(Math.Max(idxName, idxDate), Math.Max(Math.Max(idxLong, idxShort), idxOi)))
                {
                    var name = parts[idxName].Trim();
                    foreach (var filter in contractFilters)
                    {
                        if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (DateTime.TryParse(parts[idxDate], out var date)
                                && double.TryParse(parts[idxLong],  System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mmL)
                                && double.TryParse(parts[idxShort], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mmS)
                                && double.TryParse(parts[idxOi],    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var oi)
                                && oi > 0)
                            {
                                double netPctOi = (mmL - mmS) / oi * 100.0;
                                long ts = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
                                if (!accumulators[filter].ContainsKey(ts))
                                {
                                    accumulators[filter][ts] = netPctOi;
                                    matchedNames[filter].Add(name);
                                    totalMatches++;
                                }
                            }
                            break; // a row matches at most one filter
                        }
                    }
                }
                line = reader.ReadLine();
            }
        }
        return totalMatches;
    }

    private static int FindColumn(string[] headers, string name)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Trim().Trim('"');
            if (string.Equals(h, name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>
    /// CSV parser that handles quoted fields containing commas (CFTC's contract names
    /// frequently include commas inside double-quoted strings). Not a full RFC 4180 parser
    /// but enough for the CFTC text format.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ',' && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == ' ' || c == '_' || c == '-') sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }
}
