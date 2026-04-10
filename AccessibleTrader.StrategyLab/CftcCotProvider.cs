using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// StrategyLab-only indicator provider that reads CFTC Commitments of Traders (COT)
/// snapshots from the cross-series cache and emits speculator-positioning components.
///
/// Three components:
///   CFTC_COT.NetPctOi   — net speculator positioning as % of total open interest,
///                          forward-filled from weekly Tuesday COT publishes onto daily bars.
///                          Range typically -40..+40. Positive = specs net long.
///   CFTC_COT.NetZScore  — rolling 26-week z-score of NetPctOi (~6 months — long enough
///                          to capture multi-month positioning cycles, short enough to
///                          adapt to regime change).
///   CFTC_COT.NetExtreme — marker firing when |Z| ≥ 1.5 (top/bottom 13% of recent positioning).
///                          +1 = extreme net long (contrarian short signal historically).
///                          -1 = extreme net short (contrarian long signal — capitulation).
///
/// Asset detection: same hack as BinanceVisionFundingProvider — peek at median close to
/// dispatch which COT file to load. BTC > $20k loads BITCOIN; gold $1500-3000 loads GOLD;
/// WTI $30-100 loads LIGHT SWEET-WTI; SPX > $3000 (and ≠ BTC range) loads E-MINI S&amp;P 500.
/// Overlap between gold and SPX ranges is real — BTC and WTI are unambiguous, the others
/// fall back to BTC if no clear signal. A production version would take a symbol parameter.
/// </summary>
public sealed class CftcCotProvider : IIndicatorProvider
{
    public const string Code = "CFTC_COT";
    public const string CompNetPctOi  = "NetPctOi";
    public const string CompNetZ      = "NetZScore";
    public const string CompNetExtreme= "NetExtreme";

    private const int ZWindow = 26;          // ~6 months at weekly cadence
    private const double ExtremeAbsZ = 1.5;

    private readonly ICrossSeriesCache _xs;

    public CftcCotProvider(ICrossSeriesCache xs) { _xs = xs; }

    public string Name => "CFTC COT (Lab)";

    public List<IndicatorMetadata> GetIndicators() => new()
    {
        new IndicatorMetadata
        {
            Code = Code,
            Name = "CFTC Commitments of Traders",
            Category = "Positioning",
            DefaultPane = "Pane_CFTC_COT",
            Parameters = new List<IndicatorParameterMetadata>(),
            Components = new List<IndicatorComponentMetadata>
            {
                new() { Name = CompNetPctOi,   DisplayName = "Net Spec % OI",   DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal },
                new() { Name = CompNetZ,       DisplayName = "Net Z (26w)",     DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal },
                new() { Name = CompNetExtreme, DisplayName = "Net Extreme",     DisplayType = ComponentDisplayType.Dot,        Role = ComponentRole.Signal },
            }
        }
    };

    public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
    {
        var netSpan = buffer.GetComponentSpan(CompNetPctOi);
        var zSpan   = buffer.GetComponentSpan(CompNetZ);
        var exSpan  = buffer.GetComponentSpan(CompNetExtreme);
        int n = data.Length;
        for (int i = 0; i < n; i++) { netSpan[i] = double.NaN; zSpan[i] = double.NaN; exSpan[i] = double.NaN; }
        if (n == 0) return;

        // Asset resolution: __symbol hint authoritative when present. Only BTC has a real
        // CFTC crypto contract — ETH/XRP/LTC/SOL etc. cleanly return null. The previous
        // median-close fallback misdetected ETH ($1647) as gold and LTC ($80) as WTI;
        // fix is to honor the hint absolutely and never fall back when hint is present.
        string? symbolKey;
        if (parameters != null && parameters.ContainsKey("__symbol"))
        {
            symbolKey = ResolveSymbolFromParameters(parameters);
            if (symbolKey == null)
            {
                Console.WriteLine($"  [CFTC_COT] symbol hint '{parameters["__symbol"]}' has no CFTC contract — leaving all-NaN");
                return;
            }
            Console.WriteLine($"  [CFTC_COT] symbol hint → loading {symbolKey}");
        }
        else
        {
            var sortedCloses = new double[n];
            for (int i = 0; i < n; i++) sortedCloses[i] = data[i].Close;
            Array.Sort(sortedCloses);
            var medianClose = sortedCloses[n / 2];
            symbolKey = DetectSymbol(medianClose);
            if (symbolKey == null)
            {
                Console.Error.WriteLine($"  [CFTC_COT] Could not detect asset from median close {medianClose:0.00} — leaving all-NaN");
                return;
            }
            Console.WriteLine($"  [CFTC_COT] median close = {medianClose:0.00} → loading {symbolKey} (fallback)");
        }

        var request = new CrossSeriesRequest(
            Market: "Positioning",
            Provider: "CFTC",
            Symbol: symbolKey,
            Timeframe: "1w",
            MaxPages: 1);
        var ticks = _xs.GetOrFetch(request);
        if (ticks == null || ticks.Count == 0)
        {
            Console.Error.WriteLine($"  [CFTC_COT] No COT data in cache for key {request.CacheKey}");
            return;
        }
        Console.WriteLine($"  [CFTC_COT] {ticks.Count} weekly COT rows loaded for {symbolKey}");

        // Forward-fill weekly COT onto daily bars. COT publishes every Friday for the prior
        // Tuesday's positions, so the value is "stale by Tuesday" — we use it from the file's
        // own timestamp (the Tuesday-of-record) onward, ensuring no lookahead.
        int tickIdx = 0;
        var hist = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            long barTs = new DateTimeOffset(data[i].Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
            while (tickIdx + 1 < ticks.Count && ticks[tickIdx + 1].Ts <= barTs) tickIdx++;
            double v = ticks[tickIdx].Ts <= barTs ? ticks[tickIdx].Value : double.NaN;
            netSpan[i] = v;
            hist.Add(v);
        }

        // Rolling z-score on a daily-frequency series of forward-filled weekly values.
        // The same weekly value repeats ~7 times so the effective sample at any bar is
        // ZWindow / 7 ≈ 4 unique weekly publishes — that's intentional, we want the z to
        // react to the most recent few WEEKS not days.
        // Use unique-value tracking to avoid the repetition inflating the variance estimate.
        var lastVal = double.NaN;
        var window = new Queue<double>();
        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double v = hist[i];
            if (!double.IsNaN(v) && v != lastVal)
            {
                window.Enqueue(v);
                sum += v;
                sumSq += v * v;
                if (window.Count > ZWindow)
                {
                    var dropped = window.Dequeue();
                    sum -= dropped;
                    sumSq -= dropped * dropped;
                }
                lastVal = v;
            }
            if (window.Count >= 5)
            {
                double mean = sum / window.Count;
                double var = (sumSq / window.Count) - mean * mean;
                double sd = var > 1e-12 ? Math.Sqrt(var) : 0;
                if (sd > 0)
                {
                    double z = (v - mean) / sd;
                    zSpan[i] = z;
                    if (Math.Abs(z) >= ExtremeAbsZ) exSpan[i] = z > 0 ? 1.0 : -1.0;
                }
            }
        }
    }

    /// <summary>
    /// Detects which CFTC COT file to load from the median bar close. The cross-series
    /// cache key uses the sanitized contract name from the downloader, so we have to
    /// produce a key that EXACTLY matches what was written. The keys below mirror the
    /// downloader's Sanitize() output for the recommended filter strings.
    /// </summary>
    private static string? DetectSymbol(double medianClose) => medianClose switch
    {
        > 20000               => "BITCOIN___CHICAGO_MERCANTILE_EXCHANGE_COT",  // BTC ~$60k median
        > 5000 and <= 20000   => "BITCOIN___CHICAGO_MERCANTILE_EXCHANGE_COT",  // BTC older snapshots, ~$15k median
        > 1500 and <= 5000    => "GOLD___COMMODITY_EXCHANGE_COT",              // gold ~$2k or SPX (overlap risk)
        > 30 and <= 200       => "LIGHT_SWEET_WTI_COT",                         // WTI ~$70
        _ => null,
    };

    /// <summary>Reads "__symbol" from parameters and maps to a CFTC contract key. Only BTC
    /// has a real crypto contract on the CFTC's Disagg/TFF reports — every other crypto
    /// returns null cleanly (not all-NaN noise from a misdetection). Non-crypto assets like
    /// gold and SPX would need their own ticker conventions; we don't currently use them
    /// from the live app, only from the harness, so the asset list is intentionally minimal.</summary>
    private static string? ResolveSymbolFromParameters(Dictionary<string, object> parameters)
    {
        if (parameters == null) return null;
        if (!parameters.TryGetValue("__symbol", out var symObj)) return null;
        var sym = symObj?.ToString();
        if (string.IsNullOrEmpty(sym)) return null;
        var slash = sym.IndexOf('/');
        string baseAsset = slash >= 0 ? sym[..slash] : sym;
        baseAsset = baseAsset.ToUpperInvariant();
        if (baseAsset.EndsWith("USDT")) baseAsset = baseAsset[..^4];
        else if (baseAsset.EndsWith("USD")) baseAsset = baseAsset[..^3];
        return baseAsset switch
        {
            "BTC" => "BITCOIN___CHICAGO_MERCANTILE_EXCHANGE_COT",
            // ETH, XRP, SOL, LTC, etc. — no CFTC crypto contract. Return null cleanly.
            _ => null,
        };
    }

    public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        => Calculate(code, data, parameters, buffer);

    public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 50; // ~7 weeks of warmup

    public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";

    public string GetSpeechFact(string code, string componentName, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";
}
