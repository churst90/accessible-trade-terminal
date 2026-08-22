using System;
using System.Collections.Generic;
using System.Text;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Accessible Cipher SR — Support and Resistance level detector inspired by Market Cipher SR.
    ///
    /// Four navigable components:
    ///   Resistance      — Purple dot at the confirmed pivot high bar, at the high price.
    ///   Resistance Zone — Semi-transparent purple dashed line carrying the resistance price
    ///                     forward until price closes convincingly above it (break detection).
    ///   Support         — Gold dot at the confirmed pivot low bar, at the low price.
    ///   Support Zone    — Semi-transparent gold dashed line carrying the support price forward.
    ///
    /// One cloud fill (visual-only, no component):
    ///   SR Zone — subtle fill between the Resistance Zone and Support Zone lines, showing
    ///             the contested price range between the two active levels. Narrow fill =
    ///             price compression; wide fill = extended range. Follows the Cipher B WT Fill
    ///             pattern: cloud fill reuses existing navigable component data, no extra
    ///             components added purely for visual purposes.
    ///
    /// Pivot detection:
    ///   A bar at index i is a pivot high when high[i] is strictly the maximum of
    ///   [i-PivotBars .. i+PivotBars]. The last PivotBars bars never emit dots (expected).
    ///   Optional volume confluence: pivot volume must be >= VolumeMultiplier × rolling average.
    ///   Set VolumeMultiplier=0 to disable volume filtering entirely.
    ///   The DOT is drawn on the pivot bar, where the level is; the ZONE line only steps to that
    ///   level at bar i+PivotBars, the first bar the pivot could be recognised. That split is why
    ///   the dots are declared Lookahead and the zone lines Causal.
    ///
    /// Auto-Scale (works at any timeframe / zoom level):
    ///   When AutoScale=1, the pivot window for a bar is derived from that BAR'S OWN position:
    ///   PivotBars(i) = Clamp((i + 1) / 25, 2, 15). This targets roughly one pivot per 25 bars and
    ///   produces consistent SR density whether viewing 20 bars on a 5-minute chart or 200 bars on
    ///   a daily chart. It used to be Clamp(totalBarCount / 25, 2, 15), which made every bar's
    ///   answer depend on how much history was loaded after it — the same bar showed a window of 2
    ///   on a fresh 60-bar chart and 15 in a 500-bar backtest.
    ///
    /// NaN-Break Pattern:
    ///   At the bar where a new pivot forms, the zone line is NaN rather than the new value.
    ///   The dot component marks that bar; the zone line restarts from the next bar. This
    ///   prevents the renderer from drawing a vertical connector between old and new level
    ///   (the "closed box" artifact in the original implementation).
    ///
    /// Break/Invalidation:
    ///   Resistance is invalidated when Close > level × (1 + BreakThreshold).
    ///   Support is invalidated when Close < level × (1 - BreakThreshold).
    ///   Invalidated zones emit NaN for all subsequent bars until a new pivot forms.
    ///
    /// Sonification (independent of visual display type):
    ///   Resistance dot:   660 Hz Ping — audible "ceiling".
    ///   Resistance Zone:  Sustain, PitchMapping.Value — pitch tracks where resistance sits
    ///                     within the viewport; constant for held levels, steps on new pivot.
    ///   Support dot:      330 Hz Ping — audible "floor".
    ///   Support Zone:     Sustain, PitchMapping.Value — same logic, lower register.
    ///   During playback: zone lines produce steady tones that step to new pitches when a
    ///   new pivot confirms, giving an audible sense of levels stepping up or down.
    ///
    /// GetDetailFact (Ctrl+Shift+D / F4 context speech):
    ///   Reports active zone prices, how long each level has held, and how many times
    ///   price has tested (wick-touched without closing through) each zone.
    /// </summary>
    public class CipherSrProvider : IIndicatorProvider
    {
        public string Name => "Cipher SR";

        public const string CompResistance     = "Resistance";
        public const string CompResistanceLine = "Resistance Zone";
        public const string CompSupport        = "Support";
        public const string CompSupportLine    = "Support Zone";

        // Companion arrays — hidden from navigation, used by GetComponentSpeech for touch counts.
        private const string CompResistanceTouches = CompResistance + "_touches";
        private const string CompSupportTouches    = CompSupport    + "_touches";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CIPHER_SR",
                Causality = ComponentCausality.Causal,
                Name        = "Cipher SR",
                Category    = "Overlays",
                DefaultPane = "Main",
                RequiresFullRecalcOnTick = true,
                Description =
                    "Support and resistance levels from volume-confirmed pivot highs/lows. " +
                    "Purple = resistance, gold = support. " +
                    "Zone lines extend until price closes convincingly through them. " +
                    "Auto-scales to any timeframe or zoom level.",
                Components = new List<IndicatorComponentMetadata>
                {
                    // ── Resistance dot ─────────────────────────────────────────────────
                    // Sparse: non-NaN only at pivot high bars, drawn ON the pivot bar because that
                    // is where the level is. A pivot is not knowable until PivotBars later, so the
                    // dot is a chart and navigation convention, not a signal — the Zone line below
                    // is the causal form and the one a strategy can be built on.
                    // crystal_bell Ping at 700 Hz = clear high-pitched ceiling earcon.
                    new() { Causality = ComponentCausality.Lookahead,
                            Name = CompResistance,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Level,
                            DefaultColorHex = "#AB47BC", DefaultThickness = 7.0f,
                            DefaultSoundPatchId = "crystal_bell",
                            DefaultEnvelopeType = "Ping", DefaultDecayMs = 220,
                            DefaultPitchMapping = PitchMapping.None, DefaultBaseFrequency = 700.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Resistance pivot at {price}",
                            IsVisible = true },

                    // ── Resistance Zone line ────────────────────────────────────────────
                    // Carry-forward: holds the last confirmed resistance price until broken.
                    // Background sine drone — PitchMapping.Value so pitch tracks where the
                    // resistance level sits in the viewport; when the level steps to a new
                    // price, the pitch steps with it (audible level-change cue during playback).
                    // IsZoneLine = true → NavigationFeedbackManager plays proximity cue at cursor.
                    new() { Name = CompResistanceLine,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Level,
                            DefaultColorHex = "#CE93D8", DefaultThickness = 1.5f,
                            DefaultDashStyle = DashStyle.Dash,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value, DefaultBaseFrequency = 650.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultIsZoneLine = true,
                            IsVisible = true },

                    // ── Support dot ────────────────────────────────────────────────────
                    // crystal_bell Ping at 330 Hz = low-pitched floor earcon.
                    // Lookahead for the same reason as the resistance dot.
                    new() { Causality = ComponentCausality.Lookahead,
                            Name = CompSupport,
                            DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Level,
                            DefaultColorHex = "#FFB300", DefaultThickness = 7.0f,
                            DefaultSoundPatchId = "crystal_bell",
                            DefaultEnvelopeType = "Ping", DefaultDecayMs = 220,
                            DefaultPitchMapping = PitchMapping.None, DefaultBaseFrequency = 330.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSignalSpeechTemplate = "Support pivot at {price}",
                            IsVisible = true },

                    // ── Support Zone line ───────────────────────────────────────────────
                    // Background sine drone — PitchMapping.Value so pitch tracks where the
                    // support level sits in the viewport. Steps when a new pivot confirms.
                    // IsZoneLine = true → NavigationFeedbackManager plays proximity cue at cursor.
                    new() { Name = CompSupportLine,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Level,
                            DefaultColorHex = "#FFD54F", DefaultThickness = 1.5f,
                            DefaultDashStyle = DashStyle.Dash,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value, DefaultBaseFrequency = 300.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultIsZoneLine = true,
                            IsVisible = true },

                },

                // ── Zone bands: per-level shaded bands around each active S/R price ──────────
                // Each band hugs its carry-forward line with a ±0.3% width, giving a visible
                // zone without flooding the chart. Purely visual — no new components.
                DefaultZoneBands = new List<ZoneBandConfig>
                {
                    // Resistance zone band — semi-transparent purple, ±0.3% of price level.
                    // #AARRGGBB: A=0x40 (25%), R=0xCE, G=0x93, B=0xD8 (light purple matches dot colour).
                    new() { ComponentName = CompResistanceLine,
                            ColorHex = "#40CE93D8", BandWidthPct = 0.3f,
                            DisplayName = "Resistance Band", IsVisible = true },

                    // Support zone band — semi-transparent gold, ±0.3% of price level.
                    // #AARRGGBB: A=0x40 (25%), R=0xFF, G=0xD7, B=0x00 (matches gold dot colour).
                    new() { ComponentName = CompSupportLine,
                            ColorHex = "#40FFD700", BandWidthPct = 0.3f,
                            DisplayName = "Support Band", IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "PivotBars",        DisplayName = "Pivot Bars (each side)", DataType = typeof(int),    DefaultValue = 5.0,
                            Description = "Bars on each side required to confirm a pivot. Overridden when AutoScale=1." },
                    new() { Name = "VolumeLookback",   DisplayName = "Volume Lookback",        DataType = typeof(int),    DefaultValue = 20.0,
                            Description = "Bars for rolling volume average used in confluence filtering." },
                    new() { Name = "VolumeMultiplier", DisplayName = "Volume Multiplier",      DataType = typeof(double), DefaultValue = 1.2,
                            Description = "Pivot volume must be ≥ this × average. Set to 0 to disable volume filtering." },
                    new() { Name = "BreakThreshold",   DisplayName = "Break Threshold (%)",    DataType = typeof(double), DefaultValue = 0.2,
                            Description = "Fixed-mode: close must clear this % beyond the zone price to invalidate the level (e.g. 0.2 = 0.2%). Ignored when AdaptiveBreak is true." },
                    new() { Name = "AdaptiveBreak",    DisplayName = "Adaptive Break (ATR-scaled)", DataType = typeof(bool), DefaultValue = true,
                            Description = "When true, the break threshold scales with volatility: max(0.1%, 0.25 × ATR/close). This prevents a fixed 0.2% from firing spurious breaks during volatile days and missing real breaks during quiet ones." },
                    new() { Name = "AtrPeriod",        DisplayName = "ATR Period (Adaptive Break)", DataType = typeof(int), DefaultValue = 14.0,
                            Description = "ATR lookback used when AdaptiveBreak is true." },
                    new() { Name = "AutoScale",        DisplayName = "Auto-Scale Pivot Bars",  DataType = typeof(int),    DefaultValue = 1.0,
                            Description = "1 = set PivotBars from loaded bar count (barCount ÷ 25, clamped 2–15). Works at any timeframe." },
                }
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("CIPHER_SR", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 6) return;

            int    pivotBars   = GetInt(parameters, "PivotBars",        5);
            int    volLookback = GetInt(parameters, "VolumeLookback",  20);
            double volMult     = GetDbl(parameters, "VolumeMultiplier", 1.2);
            double breakPctFixed = GetDbl(parameters, "BreakThreshold", 0.2) / 100.0; // % → ratio
            bool   autoScale   = GetInt(parameters, "AutoScale",        1) != 0;
            bool   adaptiveBreak = GetBool(parameters, "AdaptiveBreak", true);
            int    atrPeriod   = Math.Max(2, GetInt(parameters, "AtrPeriod", 14));

            // Adaptive break uses ATR/close so the threshold adjusts to realized volatility.
            // A quiet daily BTC day (ATR ≈ 0.8%) produces a 0.2% threshold; a crash day
            // (ATR ≈ 4%) scales it up to 1.0%, preventing pullback-noise from firing breaks.
            double[]? atrSeries = null;
            if (adaptiveBreak)
                atrSeries = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(data, atrPeriod);

            // ── Output arrays ─────────────────────────────────────────────────────────────
            var resistance        = NanArray(n);
            var resistanceLine    = NanArray(n);
            var support           = NanArray(n);
            var supportLine       = NanArray(n);
            var resistanceTouches = NanArray(n);
            var supportTouches    = NanArray(n);

            // Levels indexed by the bar they become CONFIRMABLE (pivot bar + its own window),
            // which is the bar the carry-forward line is allowed to step to them. Separate from
            // the dot arrays above, which stay on the pivot bar because that is where the level
            // visually is — see the Causality declarations on the two dot components.
            var resConfirmed = NanArray(n);
            var supConfirmed = NanArray(n);

            // ── Pass 1: Pivot detection with optional volume confluence ────────────────────
            for (int i = 1; i < n - 1; i++)
            {
                // Auto-scale targets ~1 pivot per 25 bars. It used to derive the window from the
                // TOTAL loaded bar count — Clamp(n / 25, 2, 15) — which made bar 40's answer depend
                // on how much history happened to be loaded after it: window 2 on a 60-bar chart,
                // window 15 on a 500-bar one, so a backtest over a long series and a live chart
                // disagreed about the same bar by construction. Deriving it from the bar's own
                // position keeps the intent (density grows as history accumulates) and makes the
                // window a property of the bar rather than of the fetch.
                int pb = autoScale ? Math.Clamp((i + 1) / 25, 2, 15) : pivotBars;
                if (i - pb < 0 || i + pb >= n) continue;

                if (data[i].Close <= 0 || data[i].High <= 0 || data[i].Low <= 0) continue;
                // Volume confluence: skip if volume doesn't clear the threshold.
                // volMult <= 0 disables the filter entirely.
                bool volOk = true;
                if (volMult > 0.01)
                {
                    double volAvg = RollingAverage(data, i, volLookback);
                    volOk = volAvg < 1e-10 || data[i].Volume >= volAvg * volMult;
                }

                // Pivot high: high[i] strictly greatest in the symmetric window.
                bool isPivotHigh = volOk;
                for (int k = 1; k <= pb && isPivotHigh; k++)
                    if (data[i].High <= data[i - k].High || data[i].High <= data[i + k].High)
                        isPivotHigh = false;
                if (isPivotHigh) { resistance[i] = data[i].High; resConfirmed[i + pb] = data[i].High; }

                // Pivot low: low[i] strictly smallest in the symmetric window.
                bool isPivotLow = volOk;
                for (int k = 1; k <= pb && isPivotLow; k++)
                    if (data[i].Low >= data[i - k].Low || data[i].Low >= data[i + k].Low)
                        isPivotLow = false;
                if (isPivotLow) { support[i] = data[i].Low; supConfirmed[i + pb] = data[i].Low; }
            }

            // ── Pass 2: Carry-forward zone lines with NaN-break and break detection ────────
            //
            // The line steps to a level at the bar the pivot became CONFIRMABLE, not at the pivot
            // bar. A pivot at bar i needs bars i+1..i+pb to be a pivot at all, so carrying the
            // level forward from i+1 — what this did until 2026-08-21 — drew the zone up to 14 bars
            // before it existed, and a strategy comparing price to the zone read a level derived
            // from bars it had not seen. This is the same rule SwingStructureAnalyzer already used
            // (ConfirmedAtIndex = BarIndex + Span).
            //
            // NaN-break: at the bar where a new level is accepted, the zone line emits NaN.
            //   The dot component already marks the pivot bar visually. The zone line restarts
            //   at the next bar. This eliminates vertical connector strokes in the renderer
            //   (the "box artifact" from the original implementation).
            //
            // Break detection: a close convincingly past the level invalidates it.
            //   The zone emits NaN for all subsequent bars until a new pivot forms.
            //   Note: break check runs on the PREVIOUS level before updating to the new one,
            //   so a break on the same bar as a new pivot doesn't accidentally invalidate the
            //   newly confirmed level.
            double lastRes = double.NaN;
            double lastSup = double.NaN;
            int resTouchCount = 0;
            int supTouchCount = 0;

            for (int i = 0; i < n; i++)
            {
                if (data[i].Close <= 0) continue;
                bool newRes = !double.IsNaN(resConfirmed[i]);
                bool newSup = !double.IsNaN(supConfirmed[i]);

                // Compute the effective break threshold for this bar.
                double breakPct = breakPctFixed;
                if (adaptiveBreak && atrSeries != null && i < atrSeries.Length && !double.IsNaN(atrSeries[i]) && data[i].Close > 0)
                {
                    double atrFrac = atrSeries[i] / data[i].Close;
                    breakPct = Math.Max(0.001, 0.25 * atrFrac);
                }

                // Break detection runs on the previously held level before accepting a new one.
                if (!newRes && !double.IsNaN(lastRes) && data[i].Close > lastRes * (1.0 + breakPct))
                {
                    lastRes = double.NaN;
                    resTouchCount = 0;
                }
                if (!newSup && !double.IsNaN(lastSup) && data[i].Close < lastSup * (1.0 - breakPct))
                {
                    lastSup = double.NaN;
                    supTouchCount = 0;
                }

                // Accept new pivot level and reset touch counter.
                if (newRes) { lastRes = resConfirmed[i]; resTouchCount = 0; }
                if (newSup) { lastSup = supConfirmed[i]; supTouchCount = 0; }

                // Count wick touches: wick reached the level but close did not close through it.
                // Skip the pivot bar itself — the pivot high/low IS the level, so High==lastRes
                // is trivially true and would always produce a spurious "tested once" read-out.
                // Only count touches on SUBSEQUENT bars where price returns to test the level.
                if (!double.IsNaN(lastRes) && !newRes)
                {
                    if (data[i].High >= lastRes && data[i].Close < lastRes)
                        resTouchCount++;
                    resistanceTouches[i] = resTouchCount;
                }
                if (!double.IsNaN(lastSup) && !newSup)
                {
                    if (data[i].Low <= lastSup && data[i].Close > lastSup)
                        supTouchCount++;
                    supportTouches[i] = supTouchCount;
                }

                // Write zone line: NaN at the pivot bar itself (break visual connection),
                // carry-forward value on all other bars while the level is active.
                if (!newRes && !double.IsNaN(lastRes)) resistanceLine[i] = lastRes;
                if (!newSup && !double.IsNaN(lastSup)) supportLine[i]    = lastSup;
            }

            WriteToBuffer(buffer, CompResistance,        resistance,        n);
            WriteToBuffer(buffer, CompResistanceLine,    resistanceLine,    n);
            WriteToBuffer(buffer, CompSupport,           support,           n);
            WriteToBuffer(buffer, CompSupportLine,       supportLine,       n);
            WriteToBuffer(buffer, CompResistanceTouches, resistanceTouches, n);
            WriteToBuffer(buffer, CompSupportTouches,    supportTouches,    n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int pivot = GetInt(parameters, "PivotBars",      5);
            int vol   = GetInt(parameters, "VolumeLookback", 20);
            return (pivot * 2) + vol + 10;
        }

        /// <summary>
        /// Returns an imperative speech string for the given component at the current bar.
        /// Covers pivot dots (resistance/support with touch count and price), zone carry-forward
        /// lines (distance from current price and touch history), and companion touch-count arrays
        /// (returns null — these are navigation-invisible and should never be spoken directly).
        /// Returns null to fall through to SpeechTemplate or generic SpeechFormatter.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value))
                return "no data";

            // Providers return VALUE-ONLY strings. NavigationFeedbackManager prepends
            // "[Component Name]. [Type]. " on UP/DOWN moves.

            // Use the current live close (injected by NavigationFeedbackManager) rather than
            // bar.Close (the navigated bar's close) so the distance always reflects the present
            // price relationship to the level, regardless of which historical bar is navigated.
            double liveClose = allComponentData.TryGetValue("__live_close", out var lcArr) &&
                               lcArr != null && lcArr.Length > 0 && lcArr[0] > 0
                ? lcArr[0]
                : bar.Close;
            double distancePct = liveClose > 0
                ? Math.Abs(liveClose - value) / liveClose * 100.0
                : double.NaN;

            return componentName switch
            {
                // Pivot dots: level price, distance from current price, and how many times
                // price has wick-tested this level without closing through it.
                CompResistance => FormatPivotSpeech(value, distancePct, allComponentData, CompResistanceTouches, dataIndex, isResistance: true),
                CompSupport    => FormatPivotSpeech(value, distancePct, allComponentData, CompSupportTouches,    dataIndex, isResistance: false),

                // Zone lines: level price, position of current price, and accumulated wick-test count.
                CompResistanceLine => FormatZoneLineSpeech(value, liveClose, allComponentData, CompResistanceTouches, dataIndex, isResistance: true),
                CompSupportLine    => FormatZoneLineSpeech(value, liveClose, allComponentData, CompSupportTouches,    dataIndex, isResistance: false),

                _ => null
            };
        }

        /// <summary>
        /// Returns a spoken summary of the indicator's state at the given bar index.
        /// Triggered by Ctrl+Shift+D (full analysis) and F4 (context summary).
        /// Describes the nearest active resistance and support zones, their distances from
        /// the current close, touch counts, and how long each level has been held.
        /// Returns empty string when no SR data is available at the given index.
        /// </summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_SR", StringComparison.OrdinalIgnoreCase) || index < 0 || data.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();

            double resDot  = GetVal(results, CompResistance,     index);
            double resZone = GetVal(results, CompResistanceLine, index);
            double supDot  = GetVal(results, CompSupport,        index);
            double supZone = GetVal(results, CompSupportLine,    index);

            // Nearest resistance
            double resLevel = !double.IsNaN(resDot) ? resDot : resZone;
            double supLevel = !double.IsNaN(supDot) ? supDot : supZone;

            if (!double.IsNaN(resLevel))
                sb.Append($"Nearest resistance: {resLevel:F0}. ");
            if (!double.IsNaN(supLevel))
                sb.Append($"Nearest support: {supLevel:F0}. ");

            if (!double.IsNaN(resLevel) && !double.IsNaN(supLevel))
            {
                double close = data.Length > 0 && index < data.Length ? data[index].Close : 0;
                if (close > resLevel)       sb.Append("Price above both.");
                else if (close < supLevel)  sb.Append("Price below both.");
                else                        sb.Append("Price between levels.");
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "No confirmed S/R levels yet.";
        }

        // ── Speech helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Formats pivot dot speech: level price, distance from current price, and touch count.
        /// E.g. "170.00, price 2.3% below, tested 3 times" or "170.00, at price, tested once".
        /// </summary>
        private static string FormatPivotSpeech(double value, double distancePct,
            IReadOnlyDictionary<string, double[]> allComponentData, string touchKey, int dataIndex, bool isResistance)
        {
            // NaN distancePct means no valid close was available (zero bar or no data).
            string distStr = double.IsNaN(distancePct)
                ? ""
                : distancePct < 0.1
                    ? "at price"
                    : isResistance
                        ? $"price {distancePct:F1}% below"
                        : $"price {distancePct:F1}% above";

            // The pivot bar itself has NaN in the touches array (touch counting starts on the
            // next bar). Scan forward from the pivot to get the most recent accumulated count
            // for this level, stopping at the first NaN which marks a break or a new pivot.
            int touches = 0;
            if (allComponentData.TryGetValue(touchKey, out var arr) && arr.Length > 0)
            {
                for (int i = dataIndex + 1; i < arr.Length; i++)
                {
                    if (double.IsNaN(arr[i])) break;
                    touches = (int)arr[i];
                }
                // Fallback: check the index itself (non-NaN on zone line bars when called from pivot speech path)
                if (touches == 0 && dataIndex < arr.Length && !double.IsNaN(arr[dataIndex]))
                    touches = (int)arr[dataIndex];
            }

            string touchStr = touches == 0 ? "" : touches == 1 ? ", tested once" : $", tested {touches} times";
            string sep = string.IsNullOrEmpty(distStr) ? "" : ", ";

            return $"{value:F2}{sep}{distStr}{touchStr}";
        }

        /// <summary>
        /// Formats zone line speech: level price, price position relative to the zone, and
        /// the accumulated wick-touch count from the companion _touches array at the current bar.
        /// E.g. "170.00, price below, tested 3 times" or "170.00, price above".
        /// </summary>
        private static string FormatZoneLineSpeech(double level, double closePrice,
            IReadOnlyDictionary<string, double[]> allComponentData, string touchKey, int dataIndex, bool isResistance)
        {
            string posStr = isResistance
                ? (closePrice >= level ? "price above" : "price below")
                : (closePrice <= level ? "price below" : "price above");

            int touches = 0;
            if (allComponentData.TryGetValue(touchKey, out var arr) && dataIndex < arr.Length && !double.IsNaN(arr[dataIndex]))
                touches = (int)arr[dataIndex];

            string touchStr = touches == 0 ? "" : touches == 1 ? ", tested once" : $", tested {touches} times";
            return $"{level:F2}, {posStr}{touchStr}";
        }

        // ── Data helpers ──────────────────────────────────────────────────────────────────

        private static double[] NanArray(int n)
        {
            var arr = new double[n];
            for (int i = 0; i < n; i++) arr[i] = double.NaN;
            return arr;
        }

        private static double RollingAverage(ReadOnlySpan<Ohlcv> data, int idx, int lookback)
        {
            int start = Math.Max(0, idx - lookback);
            double sum = 0; int count = 0;
            for (int i = start; i < idx; i++) { sum += data[i].Volume; count++; }
            return count > 0 ? sum / count : 0;
        }

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] src, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len  = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = src[i];
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> results, string key, int index)
        {
            if (results.TryGetValue(key, out var arr) && index < arr.Length) return arr[index];
            return double.NaN;
        }

        private static int GetInt(Dictionary<string, object> p, string k, int def)
        {
            if (p.TryGetValue(k, out var v))
            {
                if (v is int i)    return i;
                if (v is double d) return (int)d;
                if (int.TryParse(v?.ToString(), out int parsed)) return parsed;
            }
            return def;
        }

        private static bool GetBool(Dictionary<string, object> p, string k, bool def)
        {
            if (!p.TryGetValue(k, out var v)) return def;
            if (v is bool b)   return b;
            if (v is string s) return bool.TryParse(s, out var r) ? r : def;
            return def;
        }

        private static double GetDbl(Dictionary<string, object> p, string k, double def)
        {
            if (p.TryGetValue(k, out var v))
            {
                if (v is double d) return d;
                if (v is int i)    return (double)i;
                if (double.TryParse(v?.ToString(), out double parsed)) return parsed;
            }
            return def;
        }

        // SR pivots have no OB/OS reference level lines.
        public List<LevelDescriptor> GetDefaultLevels(string code)
            => new();
    }
}
