using System;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services;

/// <summary>
/// Maps indicator codes to their display pane and category.
/// Extracted from StylingService.GetPane / GetCategory.
/// </summary>
public sealed class PaneAssignmentService : IPaneAssignmentService
{
    public string GetPane(string indicatorCode)
    {
        string cat  = GetCategory(indicatorCode);
        string code = indicatorCode.ToUpper();

        // Profile indicators overlay the price pane — rendered on the right edge via ProfileRenderLayer.
        if (code == "VPVR" || code == "VPFR" || code == "TPO") return "Main";

        // Heatmap overlays the main price pane as a background behind candles.
        if (code == "HEATMAP") return "Main";

        // Moving averages and band overlays draw directly on the price chart.
        if (code is "SMA" or "EMA" or "WMA" or "HMA" or "ALMA" or "DEMA" or "TEMA" or "TMA"
                 or "KAMA" or "MAMA" or "SMMA" or "ZLEMA" or "BB" or "KC" or "DONCHIAN"
                 or "PARABOLICSAR" or "SAR" or "VWAP" or "SUPERTREND" or "ALLIGATOR"
                 or "PIVOTPOINTS" or "CHANDELIER" or "CHANDELIEREXIT" or "PSAR"
                 or "EMA_FILL" or "MA_CLOUD" or "SPIDER_LINES" or "ICHIMOKU")
            return "Main";

        // Cipher B suite renders in its own dedicated oscillator pane
        if (code == "CIPHER_B" || code == "CIPHERB") return "Pane_CIPHER_B";

        // Cipher A renders on the main price chart as a price overlay (like CipherSR)
        if (code == "CIPHER_A" || code == "CIPHERA") return "Main";

        if (cat == "Overlays" || code.Contains("PRICE") || code.Contains("CANDLES")) return "Main";
        if (code.Contains("VOLUME")) return "Volume";
        return $"Pane_{indicatorCode.Replace(" ", "_")}";
    }

    public string GetCategory(string indicatorCode)
    {
        string c = indicatorCode.ToLower();
        
        // Profiles
        if (c == "vpvr" || c == "vpfr" || c == "tpo")
            return "Profiles";

        // Trend
        if (c.Contains("sma") || c.Contains("ema") || c.Contains("hma") || c.Contains("wma") || 
            c.Contains("movingaverage") || c.Contains("ichimoku") || c.Contains("supertrend") || 
            c.Contains("parabolic") || c.Contains("adx") || c.Contains("vortex") || c.Contains("alligator") ||
            c.Contains("trix") || c.Contains("zig") || c.Contains("pivot"))
            return "Trend";

        // Momentum
        if (c.Contains("macd") || c.Contains("rsi") || c.Contains("stoch") || c.Contains("cci") || 
            c.Contains("roc") || c.Contains("awesome") || c.Contains("williams") || c.Contains("ultimate") ||
            c.Contains("stc") || c.Contains("fisher") || c.Contains("tsi"))
            return "Momentum";

        // Volatility
        if (c.Contains("bollinger") || c.Contains("keltner") || c.Contains("atr") || c.Contains("donchian") || 
            c.Contains("standarddeviation") || c.Contains("standarderror") || c.Contains("massindex") || 
            c.Contains("ulcer") || c.Contains("choppiness"))
            return "Volatility";

        // Volume
        if (c.Contains("volume") || c.Contains("obv") || c.Contains("chaikin") || c.Contains("mfi") || 
            c.Contains("ad") || c.Contains("eom") || c.Contains("vwap"))
            return "Volume";

        // EMA Fill, Spider Lines, and Ichimoku overlays on the main price pane
        if (c == "ema_fill" || c == "spider_lines" || c == "ichimoku") return "Overlays";

        // Multi-signal suites
        if (c == "cipher_b" || c == "cipherb") return "Multi-Signal";
        if (c == "cipher_a" || c == "ciphera") return "Multi-Signal";
        if (c == "cipher_sr" || c == "ciphersr") return "Overlays";

        // Default grouping for overlays vs oscillators if not specifically categorized above
        if (c.Contains("sma") || c.Contains("ema") || c.Contains("bollinger"))
            return "Overlays";

        return "Others";
    }
}
