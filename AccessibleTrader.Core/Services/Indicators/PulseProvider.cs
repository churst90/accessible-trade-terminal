using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Pulse — composite confluence oscillator (2026-04-09 design).
    ///
    /// One sub-pane, four continuous oscillators sharing a 0–100 scale, plus marker dots:
    ///
    ///   • WtFast      — fast price oscillator (RSI of close, default period 14). The trigger.
    ///   • AnchorSlow  — slow price oscillator (RSI longer period, default 50). Bull/bear regime in price-space.
    ///   • Mfi         — Money Flow Index (volume-weighted RSI). The orthogonal volume axis the
    ///                   Cipher stack has been missing — CipherB.MoneyFlowWave is misnamed and price-derived.
    ///   • Adx         — Wilder ADX (default 14). Trend strength / chop filter.
    ///
    /// Marker components (NaN unless the event fires on that bar):
    ///   • BullCross   — raw WtFast cross-up through midline. Plotted as a green dot at y=10.
    ///   • BearCross   — raw WtFast cross-down through midline. Red dot at y=90.
    ///   • GreenDot    — bull cross AND all filters pass (Anchor ≥ AnchorBullMin, MFI ≥ MfiThreshold,
    ///                   ADX ≥ AdxMin). Bright green dot at y=15.
    ///   • RedDot      — symmetric for shorts. Bright red dot at y=85.
    ///
    /// Strategy leaves consume these via the standard SignalCatalog conventions:
    ///   `Fired PULSE.GreenDot within 5`
    ///   `PULSE.Adx GreaterThan 25`
    ///   `PULSE.AnchorSlow LessThan 40`
    ///
    /// Every threshold is a tunable parameter so the same indicator works on BTC daily, SPX 4h,
    /// or EURUSD 1h with per-instrument calibration in the strategy spec — the indicator is
    /// deterministic, the asset context lives upstream.
    /// </summary>
    public sealed class PulseProvider : IIndicatorProvider
    {
        public const string Code = "PULSE";

        public const string CompWtFast     = "WtFast";
        public const string CompAnchorSlow = "AnchorSlow";
        public const string CompMfi        = "Mfi";
        public const string CompAdx        = "Adx";
        public const string CompBullCross  = "BullCross";
        public const string CompBearCross  = "BearCross";
        public const string CompGreenDot   = "GreenDot";
        public const string CompRedDot     = "RedDot";

        // ── v2 components (2026-04-09 — additive, do not change v1 behavior) ──
        public const string CompRegime          = "Regime";          // -1/0/+1 SMA200-slope state
        public const string CompAnchorSlope     = "AnchorSlope";     // Δ Anchor over slopeBars
        public const string CompBullCrossV2     = "BullCrossV2";     // slope-confirmed midline cross + holddown
        public const string CompBearCrossV2     = "BearCrossV2";
        public const string CompBullCrossAnchor = "BullCrossAnchor"; // Fast crosses Anchor (not midline)
        public const string CompBearCrossAnchor = "BearCrossAnchor";
        public const string CompGreenDotV2      = "GreenDotV2";      // v2 long entry — uses BullCrossV2 + Regime gate
        public const string CompRedDotV2        = "RedDotV2";        // v2 short entry — asymmetric, uses BearCrossAnchor + Regime
        public const string CompYellowLong      = "YellowLong";      // would-be GreenDotV2 blocked by filter
        public const string CompYellowShort     = "YellowShort";

        // ── v3 components (2026-04-09 — orthogonal axes added) ────────────────
        public const string CompCmf           = "Cmf";             // Chaikin Money Flow (volume direction, -1..+1)
        public const string CompVolPctile     = "VolPctile";       // ATR percentile rank over rolling window (0..100)
        public const string CompGoldenDot     = "GoldenDot";       // GreenDotV2 + vol-not-climax + CMF positive
        public const string CompGoldenDotShort= "GoldenDotShort";  // RedDotV2 symmetric

        // ── v4 components (2026-04-09 — MTF anchor/regime + smoothing) ────────
        public const string CompAnchorMtf  = "AnchorMtf";   // RSI on every-Nth-bar sub-sample (≈ weekly RSI)
        public const string CompRegimeMtf  = "RegimeMtf";   // 3-state regime on the same sub-sample

        // ── v5 component (2026-04-09 — 4-state cycle phase classifier) ────────
        // CycleState extends Regime from {-1, 0, +1} to {1, 2, 3, 4} corresponding to
        // the textbook 4-stage market cycle (Stan Weinstein / Camel Finance framework):
        //   1 = Accumulation (price below MA, slope flat or rising)
        //   2 = Markup       (price above MA, slope rising) — bull trend
        //   3 = Distribution (price above MA, slope flattening or falling)
        //   4 = Markdown     (price below MA, slope falling) — bear trend
        // The four stages have different optimal entry mechanics — Pulse strategies can
        // gate on cycle state to apply stage-appropriate logic (long-at-extreme-oversold
        // in stage 1, trend-follow in stage 2, short-at-strength in stage 3, etc).
        public const string CompCycleState    = "CycleState";    // raw 1..4 (catalog use)
        public const string CompCycleStateBar = "CycleStateBar"; // visible scaled 20/40/60/80 with stage-colored steps

        // ── v6 components (2026-04-09 — asset-personality routing) ────────────
        // RollSharpe is the trailing-window annualized Sharpe of close-to-close log returns.
        // AbsRollSharpe is its absolute value. Together they form the AssetProfile routing
        // signal: |Sharpe| < ChopThreshold means the asset is currently in a near-random-walk
        // regime where mean-reverting Pulse cells (BearCrossAnchor, RedDotV2, etc.) have
        // positive expectancy. Validated 2026-04-09: gating bear pulse cells on
        // |RollSharpe(365)| < 0.20 produced the first BTC short cell to ever reach the
        // strict bootstrap CI gate (BTC H2 +0.83 R, 10 trades, CIlo 0.00) AND simultaneously
        // hit XRP H1 (10 trades, +0.86 R, CIlo +0.03), making it the first cross-asset
        // portable short gate. The same gate rescues LONG cells on ETH H2 (+1.48 R CIlo +1.46),
        // confirming it is side-agnostic and asset-personality based, not a short filter.
        public const string CompRollSharpe    = "RollSharpe";    // signed annualized rolling Sharpe
        public const string CompAbsRollSharpe = "AbsRollSharpe"; // |RollSharpe| — the chop gate signal
        public const string CompTrendState    = "TrendState";    // -2 strong down, -1 weak down, 0 chop, +1 weak up, +2 strong up

        /// <summary>
        /// Per-instrument-class parameter presets. Pulse defaults are calibrated for
        /// crypto daily; other instruments need retuning because bar-counted periods
        /// and percent-based slope thresholds are asset-specific.
        ///
        /// These are a reference table, not something the app can apply in one call: there is
        /// no SetParameters API, and the store's UpdateSeriesParametersAction carries
        /// Dictionary&lt;string, double&gt; rather than the object-valued shape here. Read the
        /// preset for the instrument class you are on and enter the values in the indicator's
        /// Properties dialog. (An earlier version of this line named a method that has never
        /// existed, which is worth knowing if you went looking for it.)
        ///
        /// Reasoning per preset:
        /// - **CryptoDaily** (default): 14/50 RSI, 200 SMA, 0.02%/bar slope. Current validated.
        /// - **CryptoFourHour**: periods scaled down (7/30 RSI, 100 SMA) to match ~daily cadence
        ///   on 4h candles; slope tightened to 0.03% since 4h trends are faster.
        /// - **StocksDaily**: 14/50 unchanged, 200 SMA unchanged, slope raised to 0.10% since
        ///   equities have stronger trends and 0.02% would fire on every pullback. Wilder's
        ///   original RSI params are already stock-calibrated so the fast/anchor periods carry.
        /// - **ForexFourHour**: 8/30 RSI (FX moves faster), 150 SMA, slope 0.005% (low drift),
        ///   ADX 20/20 symmetric (FX shorts work).
        /// - **FuturesDaily**: close to stocks daily but slope 0.08% since futures are
        ///   slightly noisier than equities.
        /// </summary>
        public static class Presets
        {
            // v6 timeframe-calibrated Sharpe windows: window = 1 calendar year of bars,
            // barsPerYear = how many bars span a calendar year on this TF. Both must scale
            // together or annualization breaks. Crypto trades 24/7 so the math is clean.
            // Stocks trade ~252 sessions/year. FX is ~252 daily sessions but ~6× that on 4h.
            public static Dictionary<string, object> CryptoDaily => new()
            {
                // default values — included explicitly so users can diff against other presets
                { "wtFastPeriod", 14 }, { "anchorPeriod", 50 }, { "mfiPeriod", 14 }, { "adxPeriod", 14 },
                { "regimeMaPeriod", 200 }, { "regimeSlopeBars", 20 }, { "regimeSlopeMinPct", 0.02 },
                { "adxBullMin", 20.0 }, { "adxBearMin", 25.0 },
                { "cmfPeriod", 20 }, { "atrPeriod", 14 },
                { "sharpeWindowBars", 365 }, { "barsPerYear", 365 },
                { "chopMaxAbsSharpe", 0.20 }, { "trendMinAbsSharpe", 0.50 },
            };

            public static Dictionary<string, object> CryptoFourHour => new()
            {
                { "wtFastPeriod", 7 }, { "anchorPeriod", 30 }, { "mfiPeriod", 10 }, { "adxPeriod", 10 },
                { "regimeMaPeriod", 100 }, { "regimeSlopeBars", 12 }, { "regimeSlopeMinPct", 0.03 },
                { "adxBullMin", 20.0 }, { "adxBearMin", 25.0 },
                { "cmfPeriod", 14 }, { "atrPeriod", 10 },
                { "crossSlopeBars", 2 }, { "crossHoldDownBars", 3 },
                { "sharpeWindowBars", 2190 }, { "barsPerYear", 2190 },  // 365 × 6
                { "chopMaxAbsSharpe", 0.20 }, { "trendMinAbsSharpe", 0.50 },
            };

            public static Dictionary<string, object> StocksDaily => new()
            {
                { "wtFastPeriod", 14 }, { "anchorPeriod", 50 }, { "mfiPeriod", 14 }, { "adxPeriod", 14 },
                { "regimeMaPeriod", 200 }, { "regimeSlopeBars", 20 }, { "regimeSlopeMinPct", 0.10 },
                { "adxBullMin", 18.0 }, { "adxBearMin", 22.0 },
                { "cmfPeriod", 20 }, { "atrPeriod", 14 },
                { "sharpeWindowBars", 252 }, { "barsPerYear", 252 },
                { "chopMaxAbsSharpe", 0.20 }, { "trendMinAbsSharpe", 0.50 },
                // Stocks drift +10% CAGR — shorts structurally broken. Use long-only.
            };

            public static Dictionary<string, object> ForexFourHour => new()
            {
                { "wtFastPeriod", 8 }, { "anchorPeriod", 30 }, { "mfiPeriod", 10 }, { "adxPeriod", 10 },
                { "regimeMaPeriod", 150 }, { "regimeSlopeBars", 15 }, { "regimeSlopeMinPct", 0.005 },
                { "adxBullMin", 20.0 }, { "adxBearMin", 20.0 },  // symmetric — FX has no drift
                { "cmfPeriod", 14 }, { "atrPeriod", 10 },
                { "crossSlopeBars", 2 }, { "crossHoldDownBars", 3 },
                { "sharpeWindowBars", 1512 }, { "barsPerYear", 1512 },  // 252 × 6
                { "chopMaxAbsSharpe", 0.20 }, { "trendMinAbsSharpe", 0.50 },
            };

            public static Dictionary<string, object> CryptoWeekly => new()
            {
                { "wtFastPeriod", 14 }, { "anchorPeriod", 14 }, { "mfiPeriod", 14 }, { "adxPeriod", 14 },
                { "regimeMaPeriod", 40 }, { "regimeSlopeBars", 8 }, { "regimeSlopeMinPct", 0.10 },
                { "adxBullMin", 18.0 }, { "adxBearMin", 22.0 },
                { "cmfPeriod", 14 }, { "atrPeriod", 14 },
                { "crossSlopeBars", 2 }, { "crossHoldDownBars", 2 },
                { "anchorBullMin", 50.0 }, { "anchorBearMax", 40.0 },
                { "sharpeWindowBars", 52 }, { "barsPerYear", 52 },
                { "chopMaxAbsSharpe", 0.20 }, { "trendMinAbsSharpe", 0.50 },
            };

            public static Dictionary<string, object> CryptoOneHour => new()
            {
                { "wtFastPeriod", 14 }, { "anchorPeriod", 50 }, { "mfiPeriod", 14 }, { "adxPeriod", 14 },
                { "regimeMaPeriod", 200 }, { "regimeSlopeBars", 24 }, { "regimeSlopeMinPct", 0.04 },
                { "adxBullMin", 20.0 }, { "adxBearMin", 25.0 },
                { "cmfPeriod", 20 }, { "atrPeriod", 14 },
                { "crossSlopeBars", 3 }, { "crossHoldDownBars", 4 },
                { "sharpeWindowBars", 8760 }, { "barsPerYear", 8760 },  // 365 × 24
                { "chopMaxAbsSharpe", 0.20 }, { "trendMinAbsSharpe", 0.50 },
            };

            public static Dictionary<string, object> FuturesDaily => new()
            {
                { "wtFastPeriod", 14 }, { "anchorPeriod", 50 }, { "mfiPeriod", 14 }, { "adxPeriod", 14 },
                { "regimeMaPeriod", 200 }, { "regimeSlopeBars", 20 }, { "regimeSlopeMinPct", 0.08 },
                { "adxBullMin", 20.0 }, { "adxBearMin", 22.0 },
                { "cmfPeriod", 20 }, { "atrPeriod", 14 },
                { "sharpeWindowBars", 252 }, { "barsPerYear", 252 },
                { "chopMaxAbsSharpe", 0.20 }, { "trendMinAbsSharpe", 0.50 },
            };
        }

        // Marker Y positions. Spread so visible markers don't overlap.
        private const double GoldenDotY      = 2.0;   // gold dot at the very bottom (most prestigious entry)
        private const double BullCrossY      = 5.0;
        private const double GreenDotV2Y     = 9.0;
        private const double GreenDotY       = 14.0;
        private const double YellowLongY     = 19.0;
        private const double YellowShortY    = 81.0;
        private const double RedDotY         = 86.0;
        private const double RedDotV2Y       = 91.0;
        private const double BearCrossY      = 95.0;
        private const double GoldenDotShortY = 98.0;

        public string Name => "Pulse Composite";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Causality = ComponentCausality.Causal,
                Name = "Pulse",
                Category = "Oscillators",
                DefaultPane = "Pane_PULSE",
                Description = "Composite trigger / regime / volume / trend confluence with pre-filtered entry dots.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "wtFastPeriod",        DisplayName = "Fast Trigger Period",  DataType = typeof(int),    DefaultValue = 14   },
                    new() { Name = "anchorPeriod",        DisplayName = "Anchor (Slow) Period", DataType = typeof(int),    DefaultValue = 50   },
                    new() { Name = "mfiPeriod",           DisplayName = "MFI Period",           DataType = typeof(int),    DefaultValue = 14   },
                    new() { Name = "adxPeriod",           DisplayName = "ADX Period",           DataType = typeof(int),    DefaultValue = 14   },
                    new() { Name = "wtFastMidline",       DisplayName = "Fast Cross Midline",   DataType = typeof(double), DefaultValue = 50.0 },
                    new() { Name = "anchorBullMin",       DisplayName = "Anchor Bull Min",      DataType = typeof(double), DefaultValue = 50.0 },
                    new() { Name = "anchorBearMax",       DisplayName = "Anchor Bear Max",      DataType = typeof(double), DefaultValue = 40.0 },
                    new() { Name = "mfiBullThreshold",    DisplayName = "MFI Bull Min",         DataType = typeof(double), DefaultValue = 50.0 },
                    new() { Name = "mfiBearThreshold",    DisplayName = "MFI Bear Max",         DataType = typeof(double), DefaultValue = 50.0 },
                    new() { Name = "adxMin",              DisplayName = "ADX Min (Chop Gate)",  DataType = typeof(double), DefaultValue = 25.0 },
                    new() { Name = "pulseLookback",       DisplayName = "Filter Lookback Bars", DataType = typeof(int),    DefaultValue = 3    },

                    // ── v2 parameters ────────────────────────────────────────────
                    new() { Name = "regimeMaPeriod",      DisplayName = "Regime MA Period",     DataType = typeof(int),    DefaultValue = 200  },
                    new() { Name = "regimeSlopeBars",     DisplayName = "Regime Slope Window",  DataType = typeof(int),    DefaultValue = 20   },
                    new() { Name = "regimeSlopeMinPct",   DisplayName = "Regime Min Slope %",   DataType = typeof(double), DefaultValue = 0.02 },
                    new() { Name = "anchorSlopeBars",     DisplayName = "Anchor Slope Window",  DataType = typeof(int),    DefaultValue = 5    },
                    new() { Name = "crossSlopeBars",      DisplayName = "Cross Slope Window",   DataType = typeof(int),    DefaultValue = 3    },
                    new() { Name = "crossHoldDownBars",   DisplayName = "Cross Hold-Down Bars", DataType = typeof(int),    DefaultValue = 5    },
                    new() { Name = "adxBullMin",          DisplayName = "ADX Bull Min (v2)",    DataType = typeof(double), DefaultValue = 20.0 },
                    new() { Name = "adxBearMin",          DisplayName = "ADX Bear Min (v2)",    DataType = typeof(double), DefaultValue = 25.0 },

                    // ── v3 parameters (CMF + Vol regime + Golden dot) ───────────
                    new() { Name = "cmfPeriod",           DisplayName = "CMF Period",           DataType = typeof(int),    DefaultValue = 20   },
                    new() { Name = "cmfBullMin",          DisplayName = "CMF Bull Min",         DataType = typeof(double), DefaultValue = 0.0  },
                    new() { Name = "cmfBearMax",          DisplayName = "CMF Bear Max",         DataType = typeof(double), DefaultValue = 0.0  },
                    new() { Name = "atrPeriod",           DisplayName = "ATR Period",           DataType = typeof(int),    DefaultValue = 14   },
                    new() { Name = "volPctileBars",       DisplayName = "Vol Percentile Window",DataType = typeof(int),    DefaultValue = 100  },

                    // ── v4 parameters (MTF + smoothing) ─────────────────────────
                    new() { Name = "mtfBarsPerWeek",      DisplayName = "MTF Bars per Week",    DataType = typeof(int),    DefaultValue = 7    },
                    new() { Name = "mtfAnchorPeriod",     DisplayName = "MTF Anchor RSI Period",DataType = typeof(int),    DefaultValue = 14   },
                    new() { Name = "mtfRegimeMa",         DisplayName = "MTF Regime MA Period", DataType = typeof(int),    DefaultValue = 30   },
                    new() { Name = "mtfRegimeSlopeBars",  DisplayName = "MTF Regime Slope Bars",DataType = typeof(int),    DefaultValue = 6    },
                    new() { Name = "mtfRegimeSlopePct",   DisplayName = "MTF Regime Min Slope %",DataType= typeof(double), DefaultValue = 0.30 },
                    new() { Name = "anchorSmoothBars",    DisplayName = "Anchor Smooth (EMA)",  DataType = typeof(int),    DefaultValue = 0    },

                    // ── v5 parameters (CycleState) ──────────────────────────────
                    new() { Name = "cycleSlopeFlatPct",   DisplayName = "Cycle Flat Slope %",   DataType = typeof(double), DefaultValue = 0.005},

                    // ── v6 parameters (RollSharpe / chop gate / TrendState) ─────
                    // sharpeWindowBars and barsPerYear MUST be set per timeframe in the preset
                    // (defaults are crypto-daily). The TrendState classifier uses two |Sharpe|
                    // thresholds: chopMax (below = chop / both sides allowed) and trendMin
                    // (above = strong directional / fade strategies should refuse).
                    new() { Name = "sharpeWindowBars",    DisplayName = "Sharpe Window (bars)", DataType = typeof(int),    DefaultValue = 365  },
                    new() { Name = "barsPerYear",         DisplayName = "Bars Per Year",        DataType = typeof(int),    DefaultValue = 365  },
                    new() { Name = "chopMaxAbsSharpe",    DisplayName = "Chop Max |Sharpe|",    DataType = typeof(double), DefaultValue = 0.20 },
                    new() { Name = "trendMinAbsSharpe",   DisplayName = "Trend Min |Sharpe|",   DataType = typeof(double), DefaultValue = 0.50 },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    // ── Continuous oscillators ─────────────────────────────────────
                    new()
                    {
                        Name = CompWtFast, DisplayName = "Fast Trigger",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        DefaultColorHex = "#42A5F5",   // sky blue — the metronome
                        DefaultThickness = 2.0f,
                        DefaultTriggerBoundaryClick = true,
                        DefaultNoiseAmount = 0.06f,
                        SpeechTemplate = "{name}. {value:F1}.",
                    },
                    new()
                    {
                        Name = CompAnchorSlow, DisplayName = "Anchor",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        DefaultColorHex = "#9E9E9E",   // gray — slow regime
                        DefaultThickness = 1.5f,
                        DefaultDashStyle = DashStyle.Dash,
                        SpeechTemplate = "{name}. {value:F1}.",
                    },
                    new()
                    {
                        Name = CompMfi, DisplayName = "MFI",
                        DisplayType = ComponentDisplayType.Oscillator, Role = ComponentRole.Signal,
                        DefaultColorHex = "#26A69A",          // teal above midline
                        DefaultColorHexSecondary = "#EF5350", // red below
                        DefaultColorSource = ColorSource.Value,
                        ColorBaseline = 50.0,
                        DefaultThickness = 1.5f,
                        DefaultNoiseAmount = 0.05f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "{name}. {value:F1}.",
                    },
                    new()
                    {
                        Name = CompAdx, DisplayName = "ADX",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        DefaultColorHex = "#FFB300",   // amber — trend strength
                        DefaultThickness = 1.25f,
                        DefaultDashStyle = DashStyle.Dot,
                        SpeechTemplate = "{name}. {value:F1}.",
                    },

                    // ── Raw cross markers (filterless events) ──────────────────────
                    new()
                    {
                        Name = CompBullCross, DisplayName = "Bull Cross",
                        DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                        DefaultColorHex = "#00E5FF",  // cyan — distinct from MFI green & Pulse Long
                        DefaultThickness = 5.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse bull cross.",
                    },
                    new()
                    {
                        Name = CompBearCross, DisplayName = "Bear Cross",
                        DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                        DefaultColorHex = "#E040FB",  // magenta — distinct from MFI red & Pulse Short
                        DefaultThickness = 5.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse bear cross.",
                    },

                    // ── Pre-filtered confluence dots ───────────────────────────────
                    new()
                    {
                        Name = CompGreenDot, DisplayName = "Pulse Long",
                        DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                        DefaultColorHex = "#76FF03",  // lime — clearly brighter than cyan Bull Cross
                        DefaultThickness = 12.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse long entry.",
                        DefaultPlaybackLayer = PlaybackLayer.Foreground,
                    },
                    new()
                    {
                        Name = CompRedDot, DisplayName = "Pulse Short",
                        DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                        DefaultColorHex = "#FF1744",  // bright red — clearly distinct from magenta Bear Cross
                        DefaultThickness = 12.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse short entry.",
                        DefaultPlaybackLayer = PlaybackLayer.Foreground,
                    },

                    // ── v2 visible markers (chart) ─────────────────────────────────
                    new()
                    {
                        Name = CompGreenDotV2, DisplayName = "Pulse Long V2",
                        DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                        DefaultColorHex = "#00FFB3",  // bright emerald — even brighter than v1 lime
                        DefaultThickness = 14.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse long V2 entry.",
                        DefaultPlaybackLayer = PlaybackLayer.Foreground,
                    },
                    new()
                    {
                        Name = CompRedDotV2, DisplayName = "Pulse Short V2",
                        DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                        DefaultColorHex = "#FF3D71",  // crimson — distinct from v1 red
                        DefaultThickness = 14.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse short V2 entry.",
                        DefaultPlaybackLayer = PlaybackLayer.Foreground,
                    },
                    new()
                    {
                        Name = CompYellowLong, DisplayName = "Pulse Long Blocked",
                        DisplayType = ComponentDisplayType.Cross, Role = ComponentRole.Signal,
                        DefaultColorHex = "#FFEB3B",  // amber/yellow — would-be entry rejected
                        DefaultThickness = 6.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse long blocked.",
                    },
                    new()
                    {
                        Name = CompYellowShort, DisplayName = "Pulse Short Blocked",
                        DisplayType = ComponentDisplayType.Cross, Role = ComponentRole.Signal,
                        DefaultColorHex = "#FFEB3B",
                        DefaultThickness = 6.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse short blocked.",
                    },

                    // ── v2 hidden components (catalog-only, harness/strategy use) ──
                    // IsVisible=false → renderer skips them but SignalCatalog still
                    // exposes them as PULSE.* leaf signals.
                    new()
                    {
                        Name = CompRegime, DisplayName = "Regime State",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Regime {value:F0}.",
                    },
                    new()
                    {
                        Name = CompAnchorSlope, DisplayName = "Anchor Slope",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Anchor slope {value:F2}.",
                    },
                    new()
                    {
                        Name = CompBullCrossV2, DisplayName = "Bull Cross V2",
                        DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                    },
                    new()
                    {
                        Name = CompBearCrossV2, DisplayName = "Bear Cross V2",
                        DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                    },
                    new()
                    {
                        Name = CompBullCrossAnchor, DisplayName = "Bull Cross Anchor",
                        DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                    },
                    new()
                    {
                        Name = CompBearCrossAnchor, DisplayName = "Bear Cross Anchor",
                        DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                    },

                    // ── v3 hidden continuous components ────────────────────────────
                    new()
                    {
                        Name = CompCmf, DisplayName = "CMF",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "C M F {value:F2}.",
                    },
                    new()
                    {
                        Name = CompVolPctile, DisplayName = "Vol Percentile",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Vol percentile {value:F0}.",
                    },

                    // ── v3 visible "Golden" entry markers (highest-confidence) ─────
                    new()
                    {
                        Name = CompGoldenDot, DisplayName = "Pulse Golden Long",
                        DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                        DefaultColorHex = "#FFD700",  // pure gold — highest-confidence long entry
                        DefaultThickness = 16.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse golden long entry.",
                        DefaultPlaybackLayer = PlaybackLayer.Foreground,
                    },
                    // ── v5 hidden continuous (CycleState — raw 1..4) ───────────────
                    new()
                    {
                        Name = CompCycleState, DisplayName = "Cycle State (1-4)",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Cycle stage {value:F0}.",
                    },

                    // ── v5 visible CycleState bar — stepped colored line in pane ───
                    // Scaled 1..4 → 20/40/60/80 so it sits in the middle of the
                    // 0-100 oscillator pane and is visible at a glance. Color rules
                    // tint the line by stage:
                    //   stage 1 (accumulation, value=20) → blue
                    //   stage 2 (markup,        value=40) → green
                    //   stage 3 (distribution,  value=60) → amber
                    //   stage 4 (markdown,      value=80) → red
                    // Rules evaluate first-match-wins so BelowLevel rules in
                    // ascending order correctly classify each band.
                    new()
                    {
                        Name = CompCycleStateBar, DisplayName = "Cycle Phase",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Signal,
                        DefaultColorHex = "#9E9E9E",  // fallback
                        DefaultThickness = 4.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Cycle phase value {value:F0}.",
                        DefaultColorRules = new List<ColorRule>
                        {
                            new() { Condition = ColorCondition.BelowLevel, Level = 30.0, ColorHex = "#42A5F5" }, // stage 1: blue
                            new() { Condition = ColorCondition.BelowLevel, Level = 50.0, ColorHex = "#66BB6A" }, // stage 2: green
                            new() { Condition = ColorCondition.BelowLevel, Level = 70.0, ColorHex = "#FFB300" }, // stage 3: amber
                            new() { Condition = ColorCondition.AboveLevel, Level = 70.0, ColorHex = "#EF5350" }, // stage 4: red
                        },
                    },

                    // ── v4 hidden continuous (MTF Anchor + MTF Regime) ─────────────
                    new()
                    {
                        Name = CompAnchorMtf, DisplayName = "Anchor MTF",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Anchor M T F {value:F1}.",
                    },
                    new()
                    {
                        Name = CompRegimeMtf, DisplayName = "Regime MTF",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Regime M T F {value:F0}.",
                    },

                    // ── v6 hidden continuous (RollSharpe + AbsRollSharpe + TrendState) ─
                    new()
                    {
                        Name = CompRollSharpe, DisplayName = "Rolling Sharpe",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Rolling Sharpe {value:F2}.",
                    },
                    new()
                    {
                        Name = CompAbsRollSharpe, DisplayName = "|Rolling Sharpe|",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Absolute Sharpe {value:F2}.",
                    },
                    new()
                    {
                        Name = CompTrendState, DisplayName = "Trend State",
                        DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                        IsVisible = false,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Trend state {value:F0}.",
                    },

                    new()
                    {
                        Name = CompGoldenDotShort, DisplayName = "Pulse Golden Short",
                        DisplayType = ComponentDisplayType.Diamond, Role = ComponentRole.Signal,
                        DefaultColorHex = "#B71C1C",  // deep blood red — highest-confidence short
                        DefaultThickness = 16.0f,
                        SubscribedLevelNames = Array.Empty<string>(),
                        DefaultSignalSpeechTemplate = "Pulse golden short entry.",
                        DefaultPlaybackLayer = PlaybackLayer.Foreground,
                    },
                },
            }
        };

        public List<LevelDescriptor> GetDefaultLevels(string code) => code.ToUpperInvariant() switch
        {
            "PULSE" => new List<LevelDescriptor>
            {
                new("Overbought", 70.0, "#FF4444", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.10f, ZoneNoiseType: "pink"),
                new("Midline",    50.0, "#888888", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.7f),
                new("Oversold",   30.0, "#44BB44", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.10f, ZoneNoiseType: "pink"),
                new("AdxMin",     20.0, "#FFB300", DashStyle.Dot,  PlayEarcon: false),
            },
            _ => new List<LevelDescriptor>(),
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            int n = data.Length;
            int wtFastPeriod  = GetInt(parameters, "wtFastPeriod", 14);
            int anchorPeriod  = GetInt(parameters, "anchorPeriod", 50);
            int mfiPeriod     = GetInt(parameters, "mfiPeriod", 14);
            int adxPeriod     = GetInt(parameters, "adxPeriod", 14);
            double midline    = GetDbl(parameters, "wtFastMidline", 50.0);
            double anchorBull = GetDbl(parameters, "anchorBullMin", 50.0);
            double anchorBear = GetDbl(parameters, "anchorBearMax", 40.0);
            double mfiBull    = GetDbl(parameters, "mfiBullThreshold", 50.0);
            double mfiBear    = GetDbl(parameters, "mfiBearThreshold", 50.0);
            double adxMin     = GetDbl(parameters, "adxMin", 25.0);
            int lookback      = Math.Max(0, GetInt(parameters, "pulseLookback", 3));

            // v2 parameters
            int regimeMaPeriod  = GetInt(parameters, "regimeMaPeriod", 200);
            int regimeSlopeBars = GetInt(parameters, "regimeSlopeBars", 20);
            double regimeSlopePct = GetDbl(parameters, "regimeSlopeMinPct", 0.02);
            int anchorSlopeBars = Math.Max(1, GetInt(parameters, "anchorSlopeBars", 5));
            int crossSlopeBars  = Math.Max(1, GetInt(parameters, "crossSlopeBars", 3));
            int holdDownBars    = Math.Max(0, GetInt(parameters, "crossHoldDownBars", 5));
            double adxBullMin   = GetDbl(parameters, "adxBullMin", 20.0);
            double adxBearMin   = GetDbl(parameters, "adxBearMin", 25.0);

            // v3 parameters
            int cmfPeriod       = Math.Max(2, GetInt(parameters, "cmfPeriod", 20));
            double cmfBullMin   = GetDbl(parameters, "cmfBullMin", 0.0);
            double cmfBearMax   = GetDbl(parameters, "cmfBearMax", 0.0);
            int atrPeriod       = Math.Max(2, GetInt(parameters, "atrPeriod", 14));
            int volPctileBars   = Math.Max(10, GetInt(parameters, "volPctileBars", 100));

            // v4 parameters
            int mtfBarsPerWeek    = Math.Max(2, GetInt(parameters, "mtfBarsPerWeek", 7));
            int mtfAnchorPeriod   = Math.Max(2, GetInt(parameters, "mtfAnchorPeriod", 14));
            int mtfRegimeMa       = Math.Max(5, GetInt(parameters, "mtfRegimeMa", 30));
            int mtfRegimeSlopeBars= Math.Max(2, GetInt(parameters, "mtfRegimeSlopeBars", 6));
            double mtfRegimeSlope = GetDbl(parameters, "mtfRegimeSlopePct", 0.30);
            int anchorSmoothBars  = Math.Max(0, GetInt(parameters, "anchorSmoothBars", 0));
            double cycleSlopeFlat = GetDbl(parameters, "cycleSlopeFlatPct", 0.005);

            // v6 parameters (RollSharpe / chop gate / TrendState)
            int sharpeWindow   = Math.Max(20, GetInt(parameters, "sharpeWindowBars", 365));
            int barsPerYear    = Math.Max(10, GetInt(parameters, "barsPerYear", 365));
            double chopMaxAbs  = GetDbl(parameters, "chopMaxAbsSharpe", 0.20);
            double trendMinAbs = GetDbl(parameters, "trendMinAbsSharpe", 0.50);

            var wt   = buffer.GetComponentSpan(CompWtFast);
            var anc  = buffer.GetComponentSpan(CompAnchorSlow);
            var mfi  = buffer.GetComponentSpan(CompMfi);
            var adx  = buffer.GetComponentSpan(CompAdx);
            var bx   = buffer.GetComponentSpan(CompBullCross);
            var sx   = buffer.GetComponentSpan(CompBearCross);
            var gd   = buffer.GetComponentSpan(CompGreenDot);
            var rd   = buffer.GetComponentSpan(CompRedDot);

            // v2 spans
            var reg     = buffer.GetComponentSpan(CompRegime);
            var ancSlp  = buffer.GetComponentSpan(CompAnchorSlope);
            var bxV2    = buffer.GetComponentSpan(CompBullCrossV2);
            var sxV2    = buffer.GetComponentSpan(CompBearCrossV2);
            var bxAnc   = buffer.GetComponentSpan(CompBullCrossAnchor);
            var sxAnc   = buffer.GetComponentSpan(CompBearCrossAnchor);
            var gdV2    = buffer.GetComponentSpan(CompGreenDotV2);
            var rdV2    = buffer.GetComponentSpan(CompRedDotV2);
            var ylLong  = buffer.GetComponentSpan(CompYellowLong);
            var ylShort = buffer.GetComponentSpan(CompYellowShort);

            // v3 spans
            var cmf     = buffer.GetComponentSpan(CompCmf);
            var volPct  = buffer.GetComponentSpan(CompVolPctile);
            var gold    = buffer.GetComponentSpan(CompGoldenDot);
            var goldS   = buffer.GetComponentSpan(CompGoldenDotShort);

            // v4 spans
            var ancMtf  = buffer.GetComponentSpan(CompAnchorMtf);
            var regMtf  = buffer.GetComponentSpan(CompRegimeMtf);

            // v5 spans
            var cycle    = buffer.GetComponentSpan(CompCycleState);
            var cycleBar = buffer.GetComponentSpan(CompCycleStateBar);

            // v6 spans
            var rsh    = buffer.GetComponentSpan(CompRollSharpe);
            var arsh   = buffer.GetComponentSpan(CompAbsRollSharpe);
            var trend  = buffer.GetComponentSpan(CompTrendState);

            // Pull closes once into a temp buffer for RSI calls.
            var closes = new double[n];
            for (int i = 0; i < n; i++) closes[i] = data[i].Close;

            ComputeRsi(closes, wtFastPeriod, wt);
            ComputeRsi(closes, anchorPeriod, anc);
            ComputeMfi(data, mfiPeriod, mfi);
            ComputeAdx(data, adxPeriod, adx);
            // A3: optional Anchor smoothing applied IN PLACE before downstream consumers
            // (AnchorSlope, harness leaves on PULSE.AnchorSlow). 0 = off (default).
            if (anchorSmoothBars > 0)
                EmaInPlace(anc, anchorSmoothBars);

            ComputeRegime(closes, regimeMaPeriod, regimeSlopeBars, regimeSlopePct, reg);
            ComputeAnchorSlope(anc, anchorSlopeBars, ancSlp);
            ComputeCmf(data, cmfPeriod, cmf);
            ComputeVolPercentile(data, atrPeriod, volPctileBars, volPct);

            // A1: MTF Anchor + MTF Regime via every-N-bar sub-sampling.
            // No-lookahead — at daily bar i, the most recent COMPLETED weekly bucket
            // is the one whose last bar index ≤ i. The first bar of week W+1 carries
            // week W's value (the just-closed bar's RSI/regime). This is the same
            // pattern higher-timeframe indicators use in real charting platforms.
            var mtfBuckets = MtfBuckets(data, mtfBarsPerWeek);
            ComputeMtfRsi(closes, mtfBuckets, mtfAnchorPeriod, ancMtf);
            ComputeMtfRegime(closes, mtfBuckets, mtfRegimeMa, mtfRegimeSlopeBars, mtfRegimeSlope, regMtf);
            ComputeCycleState(closes, regimeMaPeriod, regimeSlopeBars, cycleSlopeFlat, cycle);
            // Project the raw 1..4 stage onto the visible 20/40/60/80 band so the
            // chart shows the cycle as a stepped colored line in the middle of the pane.
            for (int i = 0; i < n; i++)
                cycleBar[i] = double.IsNaN(cycle[i]) ? double.NaN : cycle[i] * 20.0;

            // Marker pass — initialise everything to NaN.
            for (int i = 0; i < n; i++)
            {
                bx[i]      = double.NaN; sx[i]      = double.NaN;
                gd[i]      = double.NaN; rd[i]      = double.NaN;
                bxV2[i]    = double.NaN; sxV2[i]    = double.NaN;
                bxAnc[i]   = double.NaN; sxAnc[i]   = double.NaN;
                gdV2[i]    = double.NaN; rdV2[i]    = double.NaN;
                ylLong[i]  = double.NaN; ylShort[i] = double.NaN;
                gold[i]    = double.NaN; goldS[i]   = double.NaN;
            }

            // Track last cross bar index for hold-down. Per direction × per cross type.
            // Initialise to a large negative so the first cross is always allowed (avoid
            // int.MinValue subtraction overflow).
            int lastBullV2  = -1_000_000, lastBearV2  = -1_000_000;
            int lastBullAnc = -1_000_000, lastBearAnc = -1_000_000;

            for (int i = 1; i < n; i++)
            {
                double prev = wt[i - 1], cur = wt[i];
                if (double.IsNaN(prev) || double.IsNaN(cur)) continue;

                // ── v1 raw midline crosses (unchanged) ────────────────────────
                bool bullCross = prev <  midline && cur >= midline;
                bool bearCross = prev >  midline && cur <= midline;
                if (bullCross) bx[i] = BullCrossY;
                if (bearCross) sx[i] = BearCrossY;

                // ── v1 filtered dots (unchanged behavior) ─────────────────────
                if (bullCross && FiltersPassWithin(i, lookback, anc, mfi, adx, anchorBull, mfiBull, adxMin, bull: true))
                    gd[i] = GreenDotY;
                if (bearCross && FiltersPassWithin(i, lookback, anc, mfi, adx, anchorBear, mfiBear, adxMin, bull: false))
                    rd[i] = RedDotY;

                // ── v2 slope-confirmed midline cross with hold-down ───────────
                int slopeRef = i - crossSlopeBars;
                bool fastSlopeUp   = slopeRef >= 0 && !double.IsNaN(wt[slopeRef]) && wt[i] > wt[slopeRef];
                bool fastSlopeDown = slopeRef >= 0 && !double.IsNaN(wt[slopeRef]) && wt[i] < wt[slopeRef];

                if (bullCross && fastSlopeUp && (i - lastBullV2) >= holdDownBars)
                {
                    bxV2[i] = double.NaN; // hidden marker — value doesn't matter, just non-NaN sentinel
                    bxV2[i] = 1.0;
                    lastBullV2 = i;
                }
                if (bearCross && fastSlopeDown && (i - lastBearV2) >= holdDownBars)
                {
                    sxV2[i] = 1.0;
                    lastBearV2 = i;
                }

                // ── v2 Fast-crosses-Anchor markers (the asymmetric short trigger) ─
                double prevA = anc[i - 1], curA = anc[i];
                if (!double.IsNaN(prevA) && !double.IsNaN(curA))
                {
                    double prevDiff = prev - prevA;
                    double curDiff  = cur  - curA;
                    bool bullAnchorCross = prevDiff <  0 && curDiff >= 0;
                    bool bearAnchorCross = prevDiff >  0 && curDiff <= 0;
                    if (bullAnchorCross && (i - lastBullAnc) >= holdDownBars)
                    {
                        bxAnc[i] = 1.0;
                        lastBullAnc = i;
                    }
                    if (bearAnchorCross && (i - lastBearAnc) >= holdDownBars)
                    {
                        sxAnc[i] = 1.0;
                        lastBearAnc = i;
                    }
                }

                // ── v2 long entry: BullCrossV2 + Regime==+1 + ADX (same-bar) ──
                // Stripped down from the v1 design. The 2026-04-09 walk-forward showed
                // the all-filters version (Anchor + MFI + ADX + Regime) was so restrictive
                // it produced 3-4 trades per half (sample-too-small). Regime+1 already
                // implies bull price structure, so Anchor>=50 and MFI>=50 are redundant
                // restrictions that shrink the sample without adding edge. Three orthogonal
                // gates only: trigger discipline (BullCrossV2), regime (Regime), trend
                // strength (ADX). Anchor/MFI are still exposed as separate leaves so
                // strategies can layer them if desired.
                bool bullV2Fired = !double.IsNaN(bxV2[i]);
                if (bullV2Fired)
                {
                    bool regimeOk = !double.IsNaN(reg[i]) && reg[i] >= 0.5;            // Regime == +1
                    bool adxOk    = AdxPassWithin(i, lookback, adx, adxBullMin);       // bull = lookback, looser
                    if (regimeOk && adxOk)
                        gdV2[i] = GreenDotV2Y;
                    else
                        ylLong[i] = YellowLongY;

                    // ── v3 GoldenDot: BullCrossV2 + Regime ≥ 0 (not bear) + CMF > 0 ──
                    // Different shape from GreenDotV2 — instead of stacking another filter
                    // on the already-narrow Regime+1+ADX combo (where CMF redundancy was
                    // empirically harmful), GoldenDot uses CMF as the *primary discriminator*
                    // on a wider regime gate (range or bull, not bear). This is the
                    // configuration where CMF actually adds edge in walk-forward testing.
                    // No ADX gate — CMF is doing the discriminating. Vol pctile dropped
                    // (regime-conditional in the H1/H2 walk-forward — fits one half, not both).
                    bool regimeNotBear = !double.IsNaN(reg[i]) && reg[i] >= -0.5;
                    bool cmfBull = !double.IsNaN(cmf[i]) && cmf[i] >= cmfBullMin;
                    if (regimeNotBear && cmfBull)
                        gold[i] = GoldenDotY;
                }

                // ── v2 short entry: BearCrossAnchor + Regime==-1 + ADX (same-bar) ─
                // BearCrossAnchor (Fast crosses Anchor from above) is the asymmetric short
                // trigger — it fires when Fast rallies into a falling Anchor and rolls
                // over, which catches downtrend continuations the midline cross misses.
                // Same gate-stripping logic: Regime==-1 already implies bear structure,
                // so AnchorSlope<0 and MFI<40 are over-restriction. Three gates only.
                bool bearAncFired = !double.IsNaN(sxAnc[i]);
                if (bearAncFired)
                {
                    bool regimeOk = !double.IsNaN(reg[i]) && reg[i] <= -0.5;           // Regime == -1
                    bool adxOk    = !double.IsNaN(adx[i]) && adx[i] >= adxBearMin;     // same-bar ADX (asym)
                    if (regimeOk && adxOk)
                        rdV2[i] = RedDotV2Y;
                    else
                        ylShort[i] = YellowShortY;

                    // v3 Golden short symmetric: BearCrossAnchor + Regime ≤ 0 + CMF < 0.
                    bool regimeNotBull = !double.IsNaN(reg[i]) && reg[i] <= 0.5;
                    bool cmfBear = !double.IsNaN(cmf[i]) && cmf[i] <= cmfBearMax;
                    if (regimeNotBull && cmfBear)
                        goldS[i] = GoldenDotShortY;
                }
            }

            // ── v6: RollSharpe / AbsRollSharpe / TrendState ──────────────────
            // Trailing-window annualized Sharpe of close-to-close log returns. The window
            // and bars-per-year are TIMEFRAME-CALIBRATED via the parameter presets — daily
            // uses 365 / 365, 4h uses 2190 / 2190, 1h uses 8760 / 8760, etc. — so the
            // statistical period is "one year of bars" regardless of timeframe.
            //
            // Trend state thresholds collapse the continuous |Sharpe| into 5 named regimes
            // for speech / detail-fact / strategy gating:
            //   |Sharpe| < chopMax                                → 0  chop / mean-reverting
            //   chopMax  ≤ |Sharpe| < trendMin   AND  Sharpe > 0  → +1 weak uptrend
            //   |Sharpe| ≥ trendMin              AND  Sharpe > 0  → +2 strong uptrend
            //   chopMax  ≤ |Sharpe| < trendMin   AND  Sharpe < 0  → -1 weak downtrend
            //   |Sharpe| ≥ trendMin              AND  Sharpe < 0  → -2 strong downtrend
            // The bands are what GetDetailFact reads to speak the regime to the user
            // ("strong uptrend", "chop", etc.) — the visual analogue blind users have
            // been missing. The same number is what strategies leaf on for routing.
            if (n >= sharpeWindow + 1)
            {
                double annFactor = barsPerYear;
                double sqrtAnn = Math.Sqrt(annFactor);
                var lr = new double[n - 1];
                for (int i = 1; i < n; i++)
                {
                    double prev = closes[i - 1];
                    lr[i - 1] = prev > 0 ? Math.Log(closes[i] / prev) : 0.0;
                }

                // Rolling window: incremental sum + sum-of-squares for O(n) instead of O(n*W).
                double sum = 0, sqSum = 0;
                int retEnd = -1;
                for (int i = 0; i < n; i++)
                {
                    int retIdx = i - 1;
                    int from = retIdx - sharpeWindow + 1;
                    if (retIdx < 0 || from < 0)
                    {
                        rsh[i] = double.NaN; arsh[i] = double.NaN; trend[i] = double.NaN;
                        continue;
                    }
                    // Bring window forward to [from..retIdx]
                    while (retEnd < retIdx)
                    {
                        retEnd++;
                        sum += lr[retEnd];
                        sqSum += lr[retEnd] * lr[retEnd];
                        int dropIdx = retEnd - sharpeWindow;
                        if (dropIdx >= 0)
                        {
                            sum -= lr[dropIdx];
                            sqSum -= lr[dropIdx] * lr[dropIdx];
                        }
                    }
                    double mean = sum / sharpeWindow;
                    double var2 = (sqSum - sum * sum / sharpeWindow) / (sharpeWindow - 1);
                    if (var2 < 0) var2 = 0;
                    double std = Math.Sqrt(var2);
                    double sh = std > 0 ? (mean * annFactor) / (std * sqrtAnn) : 0;
                    rsh[i] = sh;
                    double abs = Math.Abs(sh);
                    arsh[i] = abs;

                    int ts;
                    if (abs < chopMaxAbs) ts = 0;
                    else if (abs < trendMinAbs) ts = sh > 0 ? 1 : -1;
                    else ts = sh > 0 ? 2 : -2;
                    trend[i] = ts;
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    rsh[i] = double.NaN; arsh[i] = double.NaN; trend[i] = double.NaN;
                }
            }
        }

        private static bool AdxPassWithin(int i, int lookback, ReadOnlySpan<double> adx, double minVal)
        {
            int start = Math.Max(0, i - lookback);
            for (int k = start; k <= i; k++)
            {
                double a = adx[k];
                if (!double.IsNaN(a) && a >= minVal) return true;
            }
            return false;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int wt = GetInt(parameters, "wtFastPeriod", 14);
            int an = GetInt(parameters, "anchorPeriod", 50);
            int mf = GetInt(parameters, "mfiPeriod", 14);
            int ad = GetInt(parameters, "adxPeriod", 14);
            int rm = GetInt(parameters, "regimeMaPeriod", 200);
            int rs = GetInt(parameters, "regimeSlopeBars", 20);
            int cf = GetInt(parameters, "cmfPeriod", 20);
            int at = GetInt(parameters, "atrPeriod", 14);
            int vp = GetInt(parameters, "volPctileBars", 100);
            int mtfBpw = GetInt(parameters, "mtfBarsPerWeek", 7);
            int mtfRm  = GetInt(parameters, "mtfRegimeMa", 30);
            int mtfRs  = GetInt(parameters, "mtfRegimeSlopeBars", 6);
            // ADX needs ~2× period for Wilder smoothing to settle. Regime needs MA period + slope window.
            // Vol percentile needs ATR warmup + percentile window.
            // MTF Regime needs (MA period + slope window) WEEKLY bars × bars-per-week DAILY bars.
            int basic = Math.Max(Math.Max(wt, an), Math.Max(mf, ad * 2));
            int regime = rm + rs;
            int vol = at + vp;
            int mtfRegime = (mtfRm + mtfRs) * mtfBpw;
            int sharpe = GetInt(parameters, "sharpeWindowBars", 365) + 1;
            return Math.Max(Math.Max(Math.Max(Math.Max(basic, regime), Math.Max(cf, vol)), mtfRegime), sharpe) + 5;
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return string.Empty;
            double w = Get(calculatedResults, CompWtFast, index);
            double a = Get(calculatedResults, CompAnchorSlow, index);
            double m = Get(calculatedResults, CompMfi, index);
            double x = Get(calculatedResults, CompAdx, index);
            double c = Get(calculatedResults, CompCycleState, index);
            double sh = Get(calculatedResults, CompRollSharpe, index);
            double ts = Get(calculatedResults, CompTrendState, index);
            string stageText = double.IsNaN(c) ? "warming up" : c switch
            {
                <= 1.5 => "stage 1, accumulation",
                <= 2.5 => "stage 2, markup",
                <= 3.5 => "stage 3, distribution",
                _      => "stage 4, markdown",
            };
            string trendText = double.IsNaN(ts) ? "trend unknown" : ts switch
            {
                <= -1.5 => "strong downtrend",
                <= -0.5 => "weak downtrend",
                <  0.5  => "chop, mean-reverting",
                <  1.5  => "weak uptrend",
                _       => "strong uptrend",
            };
            string sharpeText = double.IsNaN(sh) ? "" : $", Sharpe {sh:+0.00;-0.00;0.00}";
            return $"Pulse: Fast {w:F1}, Anchor {a:F1}, MFI {m:F1}, ADX {x:F1}. Cycle {stageText}. {trendText}{sharpeText}.";
        }

        // ── Math ─────────────────────────────────────────────────────────────

        private static void ComputeRsi(double[] src, int period, Span<double> dst)
        {
            int n = src.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            if (n < period + 1 || period <= 0) return;

            double gainSum = 0, lossSum = 0;
            for (int i = 1; i <= period; i++)
            {
                double ch = src[i] - src[i - 1];
                if (ch >= 0) gainSum += ch; else lossSum -= ch;
            }
            double avgGain = gainSum / period;
            double avgLoss = lossSum / period;
            dst[period] = avgLoss == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + avgGain / avgLoss);

            for (int i = period + 1; i < n; i++)
            {
                double ch = src[i] - src[i - 1];
                double g = ch >  0 ?  ch : 0;
                double l = ch <  0 ? -ch : 0;
                avgGain = (avgGain * (period - 1) + g) / period;
                avgLoss = (avgLoss * (period - 1) + l) / period;
                dst[i] = avgLoss == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + avgGain / avgLoss);
            }
        }

        private static void ComputeMfi(ReadOnlySpan<Ohlcv> data, int period, Span<double> dst)
        {
            int n = data.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            if (n < period + 1 || period <= 0) return;

            // Raw money flow per bar = typical_price * volume; signed by typical-price direction.
            var tp = new double[n];
            var rmf = new double[n];
            for (int i = 0; i < n; i++)
            {
                tp[i] = (data[i].High + data[i].Low + data[i].Close) / 3.0;
                rmf[i] = tp[i] * data[i].Volume;
            }

            // Rolling sums of positive/negative money flow over `period` bars.
            double posSum = 0, negSum = 0;
            for (int i = 1; i <= period; i++)
            {
                if (tp[i] > tp[i - 1]) posSum += rmf[i];
                else if (tp[i] < tp[i - 1]) negSum += rmf[i];
            }
            dst[period] = negSum == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + posSum / negSum);

            for (int i = period + 1; i < n; i++)
            {
                // Add current bar.
                if (tp[i] > tp[i - 1]) posSum += rmf[i];
                else if (tp[i] < tp[i - 1]) negSum += rmf[i];

                // Drop bar that fell out of the window (i - period).
                int drop = i - period;
                if (tp[drop] > tp[drop - 1]) posSum -= rmf[drop];
                else if (tp[drop] < tp[drop - 1]) negSum -= rmf[drop];

                if (posSum < 0) posSum = 0;
                if (negSum < 0) negSum = 0;

                dst[i] = negSum == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + posSum / negSum);
            }
        }

        private static void ComputeAdx(ReadOnlySpan<Ohlcv> data, int period, Span<double> dst)
        {
            int n = data.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            if (n < period * 2 + 1 || period <= 0) return;

            // Wilder ADX. TR, +DM, -DM → smoothed → DI+ / DI- → DX → ADX (Wilder smooth of DX).
            var tr = new double[n];
            var pdm = new double[n];
            var mdm = new double[n];

            for (int i = 1; i < n; i++)
            {
                double high = data[i].High, low = data[i].Low, prevClose = data[i - 1].Close;
                double prevHigh = data[i - 1].High, prevLow = data[i - 1].Low;
                tr[i] = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
                double upMove   = high - prevHigh;
                double downMove = prevLow - low;
                pdm[i] = (upMove   > downMove && upMove   > 0) ? upMove   : 0;
                mdm[i] = (downMove > upMove   && downMove > 0) ? downMove : 0;
            }

            // Initial smoothed values at index = period (sum of first `period` non-zero entries from i=1).
            double trSum = 0, pdmSum = 0, mdmSum = 0;
            for (int i = 1; i <= period; i++)
            {
                trSum  += tr[i];
                pdmSum += pdm[i];
                mdmSum += mdm[i];
            }

            var dx = new double[n];
            for (int i = 0; i < n; i++) dx[i] = double.NaN;

            // First DI/DX at i = period.
            int first = period;
            double pdi = trSum == 0 ? 0 : 100.0 * pdmSum / trSum;
            double mdi = trSum == 0 ? 0 : 100.0 * mdmSum / trSum;
            dx[first] = (pdi + mdi) == 0 ? 0 : 100.0 * Math.Abs(pdi - mdi) / (pdi + mdi);

            for (int i = first + 1; i < n; i++)
            {
                trSum  = trSum  - trSum  / period + tr[i];
                pdmSum = pdmSum - pdmSum / period + pdm[i];
                mdmSum = mdmSum - mdmSum / period + mdm[i];
                pdi = trSum == 0 ? 0 : 100.0 * pdmSum / trSum;
                mdi = trSum == 0 ? 0 : 100.0 * mdmSum / trSum;
                dx[i] = (pdi + mdi) == 0 ? 0 : 100.0 * Math.Abs(pdi - mdi) / (pdi + mdi);
            }

            // ADX = Wilder-smoothed DX, seeded by simple average of first `period` DX values.
            int adxFirst = first + period - 1; // first index where ADX is defined
            if (adxFirst >= n) return;

            double adxSeed = 0;
            for (int i = first; i < first + period; i++) adxSeed += dx[i];
            adxSeed /= period;
            dst[adxFirst] = adxSeed;

            double prev = adxSeed;
            for (int i = adxFirst + 1; i < n; i++)
            {
                prev = (prev * (period - 1) + dx[i]) / period;
                dst[i] = prev;
            }
        }

        // 3-state regime classifier: +1 = bull (price > SMA AND slope > +slopePct/bar),
        // -1 = bear (price < SMA AND slope < -slopePct/bar), 0 = range/transition.
        // Slope is measured as percent-per-bar of the SMA over slopeBars window.
        // Returns NaN until both the SMA and the slope reference are valid.
        private static void ComputeRegime(double[] closes, int maPeriod, int slopeBars, double slopePctMin, Span<double> dst)
        {
            int n = closes.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            if (n < maPeriod + slopeBars || maPeriod <= 0 || slopeBars <= 0) return;

            // Rolling SMA into a temp buffer.
            var sma = new double[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += closes[i];
                if (i >= maPeriod) sum -= closes[i - maPeriod];
                sma[i] = i < maPeriod - 1 ? double.NaN : sum / maPeriod;
            }

            int firstValid = (maPeriod - 1) + slopeBars;
            for (int i = firstValid; i < n; i++)
            {
                double cur = sma[i];
                double prev = sma[i - slopeBars];
                if (double.IsNaN(cur) || double.IsNaN(prev) || prev <= 0) { dst[i] = 0; continue; }
                double slopePct = (cur - prev) / prev / slopeBars * 100.0; // percent per bar
                bool aboveMa = closes[i] > cur;
                bool belowMa = closes[i] < cur;
                if (aboveMa && slopePct >  slopePctMin) dst[i] =  1.0;
                else if (belowMa && slopePct < -slopePctMin) dst[i] = -1.0;
                else dst[i] = 0.0;
            }
        }

        // 4-state cycle phase classifier (Stan Weinstein / Camel Finance framework):
        //   1 = Accumulation: price BELOW MA, slope flat or positive
        //   2 = Markup:       price ABOVE MA, slope clearly positive (above flatPct)
        //   3 = Distribution: price ABOVE MA, slope flat or negative (below flatPct)
        //   4 = Markdown:     price BELOW MA, slope clearly negative (below -flatPct)
        // Uses a SOFTER slope threshold than the 3-state Regime classifier so that
        // "early stage 1" (price below MA, slope just turning up) is correctly classified
        // as 1 rather than 4. The flatPct parameter is the boundary between "rising"
        // and "flat/falling" — default 0.005% per bar (a very gentle slope).
        private static void ComputeCycleState(double[] closes, int maPeriod, int slopeBars, double flatPct, Span<double> dst)
        {
            int n = closes.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            if (n < maPeriod + slopeBars || maPeriod <= 0 || slopeBars <= 0) return;

            var sma = new double[n];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += closes[i];
                if (i >= maPeriod) sum -= closes[i - maPeriod];
                sma[i] = i < maPeriod - 1 ? double.NaN : sum / maPeriod;
            }

            int firstValid = (maPeriod - 1) + slopeBars;
            for (int i = firstValid; i < n; i++)
            {
                double cur = sma[i], prev = sma[i - slopeBars];
                if (double.IsNaN(cur) || double.IsNaN(prev) || prev <= 0) continue;
                double slopePct = (cur - prev) / prev / slopeBars * 100.0;
                bool above = closes[i] > cur;
                bool slopeRising  = slopePct >  flatPct;
                bool slopeFalling = slopePct < -flatPct;

                if (above && slopeRising)        dst[i] = 2.0;  // Markup
                else if (above && !slopeRising)  dst[i] = 3.0;  // Distribution (flat or falling)
                else if (!above && slopeFalling) dst[i] = 4.0;  // Markdown
                else                              dst[i] = 1.0;  // Accumulation (below MA, flat or rising)
            }
        }

        // Anchor slope = simple delta over slopeBars (RSI is already a percent, so this
        // is in RSI-points-per-window units, e.g. -8 means Anchor fell 8 points in 5 bars).
        private static void ComputeAnchorSlope(ReadOnlySpan<double> anchor, int slopeBars, Span<double> dst)
        {
            int n = anchor.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            for (int i = slopeBars; i < n; i++)
            {
                double cur = anchor[i], prev = anchor[i - slopeBars];
                if (double.IsNaN(cur) || double.IsNaN(prev)) continue;
                dst[i] = cur - prev;
            }
        }

        // Chaikin Money Flow:
        //   MFM = ((C-L) - (H-C)) / (H-L)        [-1..+1, position of close in bar range]
        //   MFV = MFM * Volume
        //   CMF = sum(MFV, period) / sum(Volume, period)
        // Result is in [-1, +1]. Positive = accumulation, negative = distribution.
        // Genuinely orthogonal to RSI/MFI (uses bar-range position, not price change).
        private static void ComputeCmf(ReadOnlySpan<Ohlcv> data, int period, Span<double> dst)
        {
            int n = data.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            if (n < period || period <= 0) return;

            var mfv = new double[n];
            for (int i = 0; i < n; i++)
            {
                double h = data[i].High, l = data[i].Low, c = data[i].Close;
                double range = h - l;
                if (range <= 0) { mfv[i] = 0; continue; }
                double mfm = ((c - l) - (h - c)) / range;
                mfv[i] = mfm * data[i].Volume;
            }

            // Rolling sums.
            double mfvSum = 0, volSum = 0;
            for (int i = 0; i < period; i++) { mfvSum += mfv[i]; volSum += data[i].Volume; }
            dst[period - 1] = volSum > 0 ? mfvSum / volSum : 0;

            for (int i = period; i < n; i++)
            {
                mfvSum += mfv[i] - mfv[i - period];
                volSum += data[i].Volume - data[i - period].Volume;
                dst[i] = volSum > 0 ? mfvSum / volSum : 0;
            }
        }

        /// <summary>
        /// The weekly sub-sampling grid, with its boundaries pinned to bar DATES rather than to
        /// array positions.
        ///
        /// <para>
        /// The grid used to be <c>i / barsPerWeek</c> — week 0 started at whatever bar happened to
        /// be sitting at index 0. Scroll back on the chart, two hundred older bars arrive at the
        /// front, and every week boundary in the whole series moves: bars that had been the last
        /// bar of a week are now mid-week, the weekly closes are taken from different bars, and
        /// every MTF value on the chart changes for bars the user was already looking at. Backtest
        /// and live chart then disagree about the same bar, decided by how much history was
        /// fetched. Anchoring the grid to absolute time removes the array from the question: a bar
        /// belongs to the same bucket whichever slice of the series it is drawn in.
        /// </para>
        ///
        /// <para>
        /// The bucket a bar belongs to is <c>floor(minutes-since-epoch / (barsPerWeek × interval))</c>,
        /// where interval is the median bar spacing. <see cref="Bucket.LastBar"/> of a <see cref="Bucket.Complete"/> bucket is the bar
        /// whose own close completes it, and it is decided from that bar's date plus one
        /// interval — never by peeking at the next bar, which would be exactly the look-ahead this
        /// sub-sampling exists to avoid. When no interval can be detected (a series with no
        /// positive spacing at all) the old positional grid is used, because a wrong grid is still
        /// better than no MTF component.
        /// </para>
        /// </summary>
        private readonly struct Bucket
        {
            /// <summary>Index into the daily array of the last bar of this bucket that is present.</summary>
            public int LastBar { get; init; }
            /// <summary>False for a bucket the series only holds part of — the trailing one, or one
            /// straddling a data gap. Its close is not yet known, so it is never forward-filled.</summary>
            public bool Complete { get; init; }
        }

        private static (Bucket[] Buckets, int[] BucketOf) MtfBuckets(ReadOnlySpan<Ohlcv> data, int barsPerWeek)
        {
            int n = data.Length;
            var bucketOf = new int[n];
            if (n == 0 || barsPerWeek <= 0) return (Array.Empty<Bucket>(), bucketOf);

            double intervalMin = IndicatorMath.MedianBarIntervalMinutes(data);
            double spanMin = intervalMin * barsPerWeek;

            var buckets = new List<Bucket>();
            long lastKey = long.MinValue;

            for (int i = 0; i < n; i++)
            {
                long key;
                bool closesHere;
                if (spanMin > 0)
                {
                    double mins = (data[i].Date - DateTime.UnixEpoch).TotalMinutes;
                    key = (long)Math.Floor(mins / spanMin);
                    // Does this bar's own close carry the series past the bucket boundary? Decided
                    // from this bar alone: its date plus one interval is when it closes.
                    closesHere = (long)Math.Floor((mins + intervalMin) / spanMin) > key;
                }
                else
                {
                    key = i / barsPerWeek;
                    closesHere = i % barsPerWeek == barsPerWeek - 1;
                }

                if (key != lastKey)
                {
                    buckets.Add(new Bucket { LastBar = i, Complete = closesHere });
                    lastKey = key;
                }
                else
                {
                    buckets[^1] = new Bucket { LastBar = i, Complete = closesHere };
                }
                bucketOf[i] = buckets.Count - 1;
            }

            return (buckets.ToArray(), bucketOf);
        }

        /// <summary>
        /// At bar <paramref name="i"/>, the most recent bucket whose close is already known — its
        /// own bucket if this bar completed it, otherwise the one before. Returns -1 when there is
        /// no such bucket, or when the candidate is incomplete (a gap, or the running bucket).
        /// </summary>
        private static int CompletedBucketAt((Bucket[] Buckets, int[] BucketOf) grid, int i)
        {
            var (buckets, bucketOf) = grid;
            int b = bucketOf[i];
            if (b >= buckets.Length) return -1;
            int use = (buckets[b].Complete && i >= buckets[b].LastBar) ? b : b - 1;
            if (use < 0 || !buckets[use].Complete) return -1;
            return use;
        }

        // MTF RSI via no-lookahead sub-sampling. Builds a "weekly" close series by taking the
        // close of the last bar of each date-anchored bucket, runs RSI on the subseries, then
        // forward-fills the most recently COMPLETED bucket's value to each daily bar. On the bar
        // that closes a bucket, that bar's own close is the weekly close, which is fine because
        // it is known at the moment of the calculation.
        private static void ComputeMtfRsi(double[] closes, (Bucket[] Buckets, int[] BucketOf) grid,
            int rsiPeriod, Span<double> dst)
        {
            int n = closes.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            // Only the parameters are checked here, not the length of the series. This used to bail
            // when n < barsPerWeek * (rsiPeriod + 2), which blanked the WHOLE component on a short
            // chart — including bars that have all the weekly history they need. The same bar then
            // read NaN live on a freshly loaded chart and a number in a backtest over the full
            // series, which is a disagreement about the past decided by how much data was fetched.
            // Insufficient history is already handled per bar: weeklyRsi stays NaN until enough
            // weeks have closed, and the forward-fill below only writes non-NaN weeks.
            if (rsiPeriod <= 0) return;

            int weekCount = grid.Buckets.Length;
            var weeklyCloses = new double[weekCount];
            for (int k = 0; k < weekCount; k++) weeklyCloses[k] = closes[grid.Buckets[k].LastBar];

            // RSI on the subseries.
            var weeklyRsi = new double[weekCount];
            for (int k = 0; k < weekCount; k++) weeklyRsi[k] = double.NaN;
            if (weekCount < rsiPeriod + 1) return;

            double gainSum = 0, lossSum = 0;
            for (int k = 1; k <= rsiPeriod; k++)
            {
                double ch = weeklyCloses[k] - weeklyCloses[k - 1];
                if (ch >= 0) gainSum += ch; else lossSum -= ch;
            }
            double avgGain = gainSum / rsiPeriod;
            double avgLoss = lossSum / rsiPeriod;
            weeklyRsi[rsiPeriod] = avgLoss == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + avgGain / avgLoss);
            for (int k = rsiPeriod + 1; k < weekCount; k++)
            {
                double ch = weeklyCloses[k] - weeklyCloses[k - 1];
                double g = ch > 0 ?  ch : 0;
                double l = ch < 0 ? -ch : 0;
                avgGain = (avgGain * (rsiPeriod - 1) + g) / rsiPeriod;
                avgLoss = (avgLoss * (rsiPeriod - 1) + l) / rsiPeriod;
                weeklyRsi[k] = avgLoss == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + avgGain / avgLoss);
            }

            // Forward-fill to daily, no lookahead — the most recently COMPLETED bucket.
            for (int i = 0; i < n; i++)
            {
                int useWeek = CompletedBucketAt(grid, i);
                if (useWeek >= 0 && useWeek < weekCount && !double.IsNaN(weeklyRsi[useWeek]))
                    dst[i] = weeklyRsi[useWeek];
            }
        }

        // MTF 3-state regime classifier on the same sub-sample. Same rule as ComputeRegime
        // but operating on weekly closes, then forward-filled. Slope threshold is per-weekly-bar
        // not per-daily-bar (default 0.30% per week ≈ 0.04%/day, comparable to daily 0.02%).
        private static void ComputeMtfRegime(double[] closes, (Bucket[] Buckets, int[] BucketOf) grid,
            int maPeriod, int slopeBars, double slopePctMin, Span<double> dst)
        {
            int n = closes.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            // Length-independent for the same reason as ComputeMtfRsi: the per-week arrays stay NaN
            // until enough weeks exist, so a whole-series bail only removes bars that were entitled
            // to a value.
            if (maPeriod <= 0) return;

            int weekCount = grid.Buckets.Length;
            var weeklyCloses = new double[weekCount];
            for (int k = 0; k < weekCount; k++) weeklyCloses[k] = closes[grid.Buckets[k].LastBar];

            // Weekly SMA.
            var weeklySma = new double[weekCount];
            for (int k = 0; k < weekCount; k++) weeklySma[k] = double.NaN;
            double sum = 0;
            for (int k = 0; k < weekCount; k++)
            {
                sum += weeklyCloses[k];
                if (k >= maPeriod) sum -= weeklyCloses[k - maPeriod];
                if (k >= maPeriod - 1) weeklySma[k] = sum / maPeriod;
            }

            // Weekly regime classification.
            var weeklyRegime = new double[weekCount];
            for (int k = 0; k < weekCount; k++) weeklyRegime[k] = double.NaN;
            int firstValid = (maPeriod - 1) + slopeBars;
            for (int k = firstValid; k < weekCount; k++)
            {
                double cur = weeklySma[k], prev = weeklySma[k - slopeBars];
                if (double.IsNaN(cur) || double.IsNaN(prev) || prev <= 0) { weeklyRegime[k] = 0; continue; }
                double slopePct = (cur - prev) / prev / slopeBars * 100.0; // percent per weekly bar
                bool above = weeklyCloses[k] > cur;
                bool below = weeklyCloses[k] < cur;
                if (above && slopePct > slopePctMin) weeklyRegime[k] = 1.0;
                else if (below && slopePct < -slopePctMin) weeklyRegime[k] = -1.0;
                else weeklyRegime[k] = 0.0;
            }

            // Forward-fill to daily, no lookahead — the most recently COMPLETED bucket.
            for (int i = 0; i < n; i++)
            {
                int useWeek = CompletedBucketAt(grid, i);
                if (useWeek >= 0 && useWeek < weekCount && !double.IsNaN(weeklyRegime[useWeek]))
                    dst[i] = weeklyRegime[useWeek];
            }
        }

        // In-place EMA smoother for an existing component span. Skips leading NaNs and
        // uses the first non-NaN value as the seed. period <= 1 = no-op.
        private static void EmaInPlace(Span<double> series, int period)
        {
            if (period <= 1) return;
            int n = series.Length;
            double k = 2.0 / (period + 1);
            int seed = -1;
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(series[i])) continue;
                if (seed < 0) { seed = i; continue; }
                series[i] = series[i] * k + series[i - 1] * (1 - k);
            }
        }

        // Volatility percentile rank: ATR(atrPeriod) percentile within the last
        // pctileBars bars. Returns 0..100. 100 = current ATR is the highest in window
        // (vol climax — bad time to enter), 0 = lowest (compression — often a setup).
        private static void ComputeVolPercentile(ReadOnlySpan<Ohlcv> data, int atrPeriod, int pctileBars, Span<double> dst)
        {
            int n = data.Length;
            for (int i = 0; i < n; i++) dst[i] = double.NaN;
            if (n < atrPeriod + pctileBars || atrPeriod <= 0 || pctileBars <= 0) return;

            // Wilder ATR.
            var tr = new double[n];
            var atr = new double[n];
            for (int i = 0; i < n; i++) atr[i] = double.NaN;
            for (int i = 1; i < n; i++)
            {
                double h = data[i].High, l = data[i].Low, pc = data[i - 1].Close;
                tr[i] = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
            }
            // Seed
            double atrSum = 0;
            for (int i = 1; i <= atrPeriod; i++) atrSum += tr[i];
            atr[atrPeriod] = atrSum / atrPeriod;
            for (int i = atrPeriod + 1; i < n; i++)
                atr[i] = (atr[i - 1] * (atrPeriod - 1) + tr[i]) / atrPeriod;

            // Percentile rank in trailing pctileBars window.
            int firstValid = atrPeriod + pctileBars;
            for (int i = firstValid; i < n; i++)
            {
                double cur = atr[i];
                if (double.IsNaN(cur)) continue;
                int cnt = 0;
                for (int k = i - pctileBars + 1; k <= i; k++)
                {
                    if (!double.IsNaN(atr[k]) && atr[k] <= cur) cnt++;
                }
                dst[i] = 100.0 * cnt / pctileBars;
            }
        }

        // Filter rules:
        //   • ADX must be at-or-above adxMin AT THE CROSS BAR ITSELF (same-bar gate). The
        //     12h walk-forward showed that allowing ADX to pass via the lookback window let
        //     shorts fire after a brief ADX spike had already collapsed back into chop.
        //   • Anchor and MFI may be satisfied at any bar within the last (lookback+1) bars
        //     including the cross. Both are slow regime reads; a small look-back keeps the
        //     trigger from missing setups where the regime confirmed a couple of bars early.
        private static bool FiltersPassWithin(int i, int lookback,
            ReadOnlySpan<double> anc, ReadOnlySpan<double> mfi, ReadOnlySpan<double> adx,
            double ancThreshold, double mfiThreshold, double adxMin, bool bull)
        {
            // ADX same-bar gate. NaN during warmup → cannot fire.
            double adxNow = adx[i];
            if (double.IsNaN(adxNow) || adxNow < adxMin) return false;

            int start = Math.Max(0, i - lookback);
            for (int k = start; k <= i; k++)
            {
                double a = anc[k], m = mfi[k];
                if (double.IsNaN(a) || double.IsNaN(m)) continue;
                bool ancOk = bull ? a >= ancThreshold : a <= ancThreshold;
                bool mfiOk = bull ? m >= mfiThreshold : m <= mfiThreshold;
                if (ancOk && mfiOk) return true;
            }
            return false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static int GetInt(Dictionary<string, object> p, string key, int dflt)
            => p != null && p.TryGetValue(key, out var v) && v != null && int.TryParse(v.ToString(), out var i) ? i : dflt;

        private static double GetDbl(Dictionary<string, object> p, string key, double dflt)
            => p != null && p.TryGetValue(key, out var v) && v != null && double.TryParse(v.ToString(),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : dflt;

        private static double Get(IReadOnlyDictionary<string, double[]> r, string key, int i)
            => r.TryGetValue(key, out var arr) && i < arr.Length ? arr[i] : double.NaN;
    }
}
