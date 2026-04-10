using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// StrategyLab-only indicator provider that reads Binance Vision funding rate snapshots from
/// the cross-series cache and emits funding-rate-derived components for the strategy engine.
/// Three components:
///
///   BNVISION_FUNDING.Funding         — raw funding rate, forward-filled onto chart bars
///   BNVISION_FUNDING.FundingZScore   — rolling 14-period z-score of the funding rate
///   BNVISION_FUNDING.FundingExtreme  — marker firing when |Z| ≥ 1.5 (top/bottom 13% by Gauss approx)
///
/// Asset detection: this provider has to figure out which symbol's funding history to load
/// without being told (the harness's WorkspaceFactory passes empty parameters and the
/// indicator engine doesn't see the chart's Symbol). Workaround: peek at the median Close
/// of the bar set and dispatch by price magnitude. Hacky but unambiguous for the four assets
/// we care about (BTC, ETH, XRP, SOL — three orders of magnitude apart). A user-facing
/// production version would take a 'symbol' parameter, but that requires plumbing parameters
/// through WorkspaceFactory.BuildAsync which is out of scope for this experiment.
/// </summary>
public sealed class BinanceVisionFundingProvider : IIndicatorProvider
{
    public const string Code = "BNVISION_FUNDING";
    public const string CompFunding = "Funding";
    public const string CompFundingZ = "FundingZScore";
    public const string CompFundingExtreme = "FundingExtreme";

    private const int ZWindow = 14;     // ~4-5 days at 8h funding cadence
    private const double ExtremeAbsZ = 1.5;

    private readonly ICrossSeriesCache _xs;

    public BinanceVisionFundingProvider(ICrossSeriesCache xs) { _xs = xs; }

    public string Name => "Binance Vision Funding (Lab)";

    public List<IndicatorMetadata> GetIndicators() => new()
    {
        new IndicatorMetadata
        {
            Code = Code,
            Name = "Binance Vision Funding",
            Category = "Derivatives",
            DefaultPane = "Pane_BNVISION_FUNDING",
            Parameters = new List<IndicatorParameterMetadata>(),
            Components = new List<IndicatorComponentMetadata>
            {
                new() { Name = CompFunding, DisplayName = "Funding Rate", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal },
                new() { Name = CompFundingZ, DisplayName = "Funding Z-Score (14)", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal },
                new() { Name = CompFundingExtreme, DisplayName = "Funding Extreme |Z|≥1.5", DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal },
            }
        }
    };

    public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
    {
        var fundingSpan = buffer.GetComponentSpan(CompFunding);
        var zSpan = buffer.GetComponentSpan(CompFundingZ);
        var extremeSpan = buffer.GetComponentSpan(CompFundingExtreme);
        int n = data.Length;
        for (int i = 0; i < n; i++) { fundingSpan[i] = double.NaN; zSpan[i] = double.NaN; extremeSpan[i] = double.NaN; }

        if (n == 0) return;

        // Asset resolution: __symbol hint is authoritative when present (even if it
        // resolves to "unsupported" — return all-NaN cleanly rather than falling back
        // to a misdetection). Median-close fallback only runs when no hint was passed
        // at all (live MAUI app path until parameter plumbing lands there too).
        string? symbol;
        if (parameters != null && parameters.ContainsKey("__symbol"))
        {
            symbol = ResolveSymbolFromParameters(parameters);
            if (symbol == null)
            {
                Console.WriteLine($"  [BNVISION_FUNDING] symbol hint '{parameters["__symbol"]}' has no funding data — leaving all-NaN");
                return;
            }
            Console.WriteLine($"  [BNVISION_FUNDING] symbol hint {symbol}");
        }
        else
        {
            var sortedCloses = new double[n];
            for (int i = 0; i < n; i++) sortedCloses[i] = data[i].Close;
            Array.Sort(sortedCloses);
            var medianClose = sortedCloses[n / 2];
            symbol = DetectSymbol(medianClose);
            if (symbol == null)
            {
                Console.Error.WriteLine($"  [BNVISION_FUNDING] Could not detect asset from median close {medianClose:0.00} — leaving all-NaN");
                return;
            }
            Console.WriteLine($"  [BNVISION_FUNDING] median close = {medianClose:0.00} → detected {symbol} (fallback)");
        }

        var request = new CrossSeriesRequest(
            Market: "Derivatives",
            Provider: "BinanceVision",
            Symbol: $"{symbol}_FUNDING",
            Timeframe: "8h",
            MaxPages: 1);

        var ticks = _xs.GetOrFetch(request);
        if (ticks == null || ticks.Count == 0)
        {
            Console.Error.WriteLine($"  [BNVISION_FUNDING] No funding data in cache for key {request.CacheKey}");
            return;
        }
        Console.WriteLine($"  [BNVISION_FUNDING] {ticks.Count} funding rows loaded for {symbol}");

        // Forward-fill funding rate onto each bar by walking the funding ticks in lockstep.
        // Funding cadence (8h) is much slower than daily/4h bars in some cases so the same
        // funding value will replicate across many bars; for 8h+ bars each bar may straddle
        // 1-3 funding events and we use the most recent settled rate at the bar's open.
        int tickIdx = 0;
        var fundingHistory = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            long barTs = new DateTimeOffset(data[i].Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
            // Advance the tick cursor to the latest tick whose timestamp is <= bar time.
            while (tickIdx + 1 < ticks.Count && ticks[tickIdx + 1].Ts <= barTs) tickIdx++;
            double f = ticks[tickIdx].Ts <= barTs ? ticks[tickIdx].Value : double.NaN;
            fundingSpan[i] = f;
            fundingHistory.Add(f);
        }

        // Rolling z-score over the most recent ZWindow non-NaN funding values up to bar i.
        // Use a deque-style window to stay O(N).
        var window = new Queue<double>();
        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double f = fundingHistory[i];
            if (!double.IsNaN(f))
            {
                window.Enqueue(f);
                sum += f;
                sumSq += f * f;
                if (window.Count > ZWindow)
                {
                    var dropped = window.Dequeue();
                    sum -= dropped;
                    sumSq -= dropped * dropped;
                }
            }
            if (window.Count >= 5)  // need a few samples before z is meaningful
            {
                double mean = sum / window.Count;
                double var = (sumSq / window.Count) - mean * mean;
                double sd = var > 1e-12 ? Math.Sqrt(var) : 0;
                if (sd > 0)
                {
                    double z = (f - mean) / sd;
                    zSpan[i] = z;
                    if (Math.Abs(z) >= ExtremeAbsZ) extremeSpan[i] = z > 0 ? 1.0 : -1.0;
                }
            }
        }
    }

    private static string? DetectSymbol(double medianClose) => medianClose switch
    {
        > 8000 => "BTCUSDT",
        > 200 and <= 8000 => "ETHUSDT",
        > 5 and <= 200 => "SOLUSDT",
        > 0 and <= 5 => "XRPUSDT",
        _ => null,
    };

    /// <summary>Reads the "__symbol" hint from parameters and normalizes to a Binance Vision
    /// USDT-perp symbol (e.g. "LTC/USDT" → "LTCUSDT"). Returns null if missing or unsupported.
    /// The whitelist mirrors SymbolStartMonths so we never request data we know doesn't exist.</summary>
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
        var perp = $"{baseAsset}USDT";
        // Whitelist — only return symbols we have funding data for. LTC is intentionally
        // not on this list because Binance Vision LTC funding history is incomplete.
        return perp switch
        {
            "BTCUSDT" or "ETHUSDT" or "XRPUSDT" or "SOLUSDT" => perp,
            _ => null,
        };
    }

    public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        => Calculate(code, data, parameters, buffer);

    public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => ZWindow;

    public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";

    public string GetSpeechFact(string code, string componentName, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";
}
