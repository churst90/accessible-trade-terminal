using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services;

/// <summary>
/// Which pane a series lands on. A PANE IS A Y AXIS (see <see cref="ChartPaneModel"/>), so two
/// series may share one only when they are in the same units — and that is the whole rule.
///
/// <para>
/// <b>There is one authority for the decision: <see cref="PaneFor"/>.</b> Until 2026-09-11 there
/// were two. The provider's <c>DefaultPane</c> was consulted first, and <see cref="GetPane"/> —
/// which gives almost everything a pane of its own — was written as its fallback behind a
/// <c>??</c> that could never fire, because <c>DefaultPane</c> is a non-nullable string
/// defaulting to "Main". Thirty indicators declared the same <c>"Oscillator"</c> pane across
/// five incompatible scale families, one range was computed over all of them, and Cody heard
/// the result: <i>"the RSI almost sounds flat … RSI sounds correct after I removed the MACD"</i>.
/// MACD is a price difference (±800 on BTC) and RSI is bounded 0–100, so RSI's whole working
/// span was ~2.5% of the pitch range. See <c>docs/SHARED_OSCILLATOR_PANE_2026-09-11.md</c>.
/// </para>
///
/// <para>
/// Now every non-overlay indicator declares a pane of its own (<c>Pane_{Code}</c>), the only
/// panes shared by more than one indicator code are the two whose units are fixed by
/// definition — <see cref="ChartPaneModel.MainPaneKey"/> (price) and <see cref="VolumePane"/>
/// — and <c>PaneAssignmentTests</c> holds that across the whole fleet, so a new provider that
/// reintroduces a shared bucket goes red at build time naming both indicators.
/// </para>
/// </summary>
public sealed class PaneAssignmentService : IPaneAssignmentService
{
    /// <summary>The pane volume series share. Its units are volume by definition.</summary>
    public const string VolumePane = "Volume";

    /// <summary>
    /// The RETIRED shared bucket. No provider may declare it (the fleet guard says so); it
    /// survives here only so that a workspace saved before 2026-09-11 still loads — a saved
    /// series on this pane is moved to its own pane by <see cref="PaneFor"/> when
    /// <c>WorkspaceInitializer.MigrateSeriesConfig</c> runs on restore.
    /// </summary>
    public const string RetiredSharedPane = "Oscillator";

    /// <summary>
    /// The panes shared BY DESIGN: every series in them is in the same units, so one range over
    /// the whole pane is the right range for each of them. Anything else is one indicator's.
    /// </summary>
    public static bool IsSharedByDesign(string? pane) =>
        string.Equals(pane, ChartPaneModel.MainPaneKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(pane, VolumePane, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The pane an indicator gets when nothing else shares its units. Two instances of the SAME
    /// indicator (RSI 14 beside RSI 7) share it, because they are in the same units; two
    /// different indicators never do. This is the one place the key's shape is written down —
    /// <see cref="GetPane"/> and <see cref="PaneFor"/> both come here for it.
    /// </summary>
    public static string OwnPaneKey(string indicatorCode) =>
        $"Pane_{(indicatorCode ?? string.Empty).Trim().Replace(" ", "_")}";

    /// <summary>
    /// <b>The pane a series created from this metadata lands on.</b> The declared
    /// <c>DefaultPane</c>, except that the retired shared bucket — and an empty declaration —
    /// resolve to the indicator's own pane. Every site that turns metadata into a series comes
    /// through here: the Add Indicator path, the workspace restore migration, the headless
    /// chart the background monitor reads alerts from, and the dialog text that tells the user
    /// where the indicator will go.
    /// </summary>
    public static string PaneFor(IndicatorMetadata meta)
    {
        if (meta == null) throw new ArgumentNullException(nameof(meta));
        string? declared = meta.DefaultPane?.Trim();
        if (string.IsNullOrEmpty(declared)
            || string.Equals(declared, RetiredSharedPane, StringComparison.OrdinalIgnoreCase))
            return OwnPaneKey(meta.Code);
        return declared;
    }

    /// <summary>
    /// The pane for an indicator CODE with no metadata to hand — the heatmap and the other core
    /// series registered by code alone. For an indicator that has metadata, <see cref="PaneFor"/>
    /// is the answer; this agrees with it on the shape of an own pane but cannot see a declared
    /// one (<c>Pane_CIPHER_B</c>, a My Data dataset's own name).
    /// </summary>
    public string GetPane(string indicatorCode)
    {
        string cat  = GetCategory(indicatorCode);
        string code = indicatorCode.ToUpper();

        // Profile indicators overlay the price pane — rendered on the right edge via ProfileRenderLayer.
        if (ProfileAnchoring.IsProfileCode(code)) return "Main";

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
        if (code.Contains("VOLUME")) return VolumePane;
        return OwnPaneKey(indicatorCode);
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
