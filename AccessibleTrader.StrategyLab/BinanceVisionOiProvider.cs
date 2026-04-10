using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Open-interest cross-series indicator built on the Binance Vision daily metrics dump.
/// Reads the matching xs_binancevision_{symbol}_oi_1d.json from the cross-series cache and
/// emits four components shaped for strategy use:
///
///   BNVISION_OI.Oi              — raw EOD USDT-denominated open interest, forward-filled
///   BNVISION_OI.OiDeltaPct      — day-over-day percent change in OI (ΔOI / prior OI × 100)
///   BNVISION_OI.OiDeltaZScore   — rolling 14-day z-score of OiDeltaPct
///   BNVISION_OI.PriceOiAlign    — sign-product of bar return and OI delta:
///                                  +1 if both up (healthy bull)
///                                  -1 if both down (healthy bear / capitulation)
///                                  -2 if price up, OI down (bull divergence — short cover)
///                                  +2 if price down, OI up (bear leverage building, dangerous)
///
/// PriceOiAlign is the canonical "OI divergence" reading from the OpenInterestProvider doc
/// already in the codebase: see CipherBProvider memory + the existing OkxDerivatives/OI logic
/// for the same four-state classification. Strategies use it as a regime gate. Asset detection
/// uses the same close-price magnitude trick as BinanceVisionFundingProvider.
/// </summary>
public sealed class BinanceVisionOiProvider : IIndicatorProvider
{
    public const string Code = "BNVISION_OI";
    public const string CompOi = "Oi";
    public const string CompOiDeltaPct = "OiDeltaPct";
    public const string CompOiDeltaZ = "OiDeltaZScore";
    public const string CompPriceOiAlign = "PriceOiAlign";

    private const int ZWindow = 14;

    private readonly ICrossSeriesCache _xs;
    public BinanceVisionOiProvider(ICrossSeriesCache xs) { _xs = xs; }

    public string Name => "Binance Vision Open Interest (Lab)";

    public List<IndicatorMetadata> GetIndicators() => new()
    {
        new IndicatorMetadata
        {
            Code = Code,
            Name = "Binance Vision OI",
            Category = "Derivatives",
            DefaultPane = "Pane_BNVISION_OI",
            Parameters = new List<IndicatorParameterMetadata>(),
            Components = new List<IndicatorComponentMetadata>
            {
                new() { Name = CompOi, DisplayName = "Open Interest (USDT)", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal },
                new() { Name = CompOiDeltaPct, DisplayName = "ΔOI %", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal },
                new() { Name = CompOiDeltaZ, DisplayName = "ΔOI Z (14)", DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal },
                new() { Name = CompPriceOiAlign, DisplayName = "Price/OI Align", DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal },
            }
        }
    };

    public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
    {
        var oiSpan = buffer.GetComponentSpan(CompOi);
        var dPctSpan = buffer.GetComponentSpan(CompOiDeltaPct);
        var dZSpan = buffer.GetComponentSpan(CompOiDeltaZ);
        var alignSpan = buffer.GetComponentSpan(CompPriceOiAlign);
        int n = data.Length;
        for (int i = 0; i < n; i++) { oiSpan[i] = double.NaN; dPctSpan[i] = double.NaN; dZSpan[i] = double.NaN; alignSpan[i] = double.NaN; }
        if (n == 0) return;

        // Asset resolution: __symbol hint authoritative when present, median fallback
        // only for callers without hint plumbing. See BinanceVisionFundingProvider.
        string? symbol;
        if (parameters != null && parameters.ContainsKey("__symbol"))
        {
            symbol = ResolveSymbolFromParameters(parameters);
            if (symbol == null) return;  // hint present but unsupported — clean all-NaN
        }
        else
        {
            var closes = new double[n];
            for (int i = 0; i < n; i++) closes[i] = data[i].Close;
            Array.Sort(closes);
            var medianClose = closes[n / 2];
            symbol = DetectSymbol(medianClose);
            if (symbol == null) return;
        }

        var request = new CrossSeriesRequest(
            Market: "Derivatives",
            Provider: "BinanceVision",
            Symbol: $"{symbol}_OI",
            Timeframe: "1d",
            MaxPages: 1);
        var ticks = _xs.GetOrFetch(request);
        if (ticks == null || ticks.Count == 0)
        {
            Console.Error.WriteLine($"  [BNVISION_OI] No OI data in cache for {request.CacheKey}");
            return;
        }
        Console.WriteLine($"  [BNVISION_OI] {ticks.Count} OI rows loaded for {symbol}");

        // Forward-fill OI onto bars by walking the OI ticks in lockstep.
        int tickIdx = 0;
        var oiHistory = new double[n];
        for (int i = 0; i < n; i++)
        {
            long barTs = new DateTimeOffset(data[i].Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
            while (tickIdx + 1 < ticks.Count && ticks[tickIdx + 1].Ts <= barTs) tickIdx++;
            double f = ticks[tickIdx].Ts <= barTs ? ticks[tickIdx].Value : double.NaN;
            oiSpan[i] = f;
            oiHistory[i] = f;
        }

        // Day-over-day percent change in OI. Skips NaN gaps cleanly.
        for (int i = 1; i < n; i++)
        {
            double a = oiHistory[i - 1], b = oiHistory[i];
            if (double.IsNaN(a) || double.IsNaN(b) || a == 0) continue;
            dPctSpan[i] = (b - a) / a * 100.0;
        }

        // Rolling z-score of OI delta over ZWindow bars.
        var window = new Queue<double>();
        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double dp = dPctSpan[i];
            if (!double.IsNaN(dp))
            {
                window.Enqueue(dp);
                sum += dp;
                sumSq += dp * dp;
                if (window.Count > ZWindow)
                {
                    var dropped = window.Dequeue();
                    sum -= dropped;
                    sumSq -= dropped * dropped;
                }
            }
            if (window.Count >= 5)
            {
                double mean = sum / window.Count;
                double var = sumSq / window.Count - mean * mean;
                double sd = var > 1e-12 ? Math.Sqrt(var) : 0;
                if (sd > 0)
                    dZSpan[i] = (dp - mean) / sd;
            }
        }

        // Price/OI alignment classifier — see XML doc above.
        for (int i = 1; i < n; i++)
        {
            double priceRet = data[i].Close - data[i - 1].Close;
            double oiDelta = dPctSpan[i];
            if (double.IsNaN(oiDelta)) continue;
            int priceSign = priceRet > 0 ? 1 : priceRet < 0 ? -1 : 0;
            int oiSign = oiDelta > 0 ? 1 : oiDelta < 0 ? -1 : 0;
            if (priceSign == 0 || oiSign == 0) continue;
            double align;
            if (priceSign == 1 && oiSign == 1) align = 1;        // healthy bull
            else if (priceSign == -1 && oiSign == -1) align = -1; // capitulation / healthy bear
            else if (priceSign == 1 && oiSign == -1) align = -2;  // bull divergence (short cover)
            else align = 2;                                       // price down, OI up — bear leverage building
            alignSpan[i] = align;
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

    /// <summary>Reads "__symbol" hint from parameters, normalizes to a Binance Vision USDT-perp
    /// symbol. Whitelist matches the OI download set; LTC excluded because Binance Vision
    /// LTCUSDT OI data is not in the standard download bundle.</summary>
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
        return perp switch
        {
            "BTCUSDT" or "ETHUSDT" or "XRPUSDT" or "SOLUSDT" => perp,
            _ => null,
        };
    }

    public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        => Calculate(code, data, parameters, buffer);

    public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => ZWindow + 1;

    public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";

    public string GetSpeechFact(string code, string componentName, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters) => "";
}
