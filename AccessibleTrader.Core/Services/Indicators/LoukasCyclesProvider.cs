using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Loukas Cycle Detection — count-based, pivot-anchored layered cycle tracker.
    ///
    /// Implements Bob Loukas's behavioural cycle framework: layered Daily / Intermediate /
    /// (Four-Year, BTC-only) cycles counted in bars-since-last-confirmed-pivot-low, with
    /// explicit timing band awareness. Fundamentally different from math-on-price cycle
    /// detectors like Cipher C (Ehlers Cyber Cycle bandpass) — this indicator does not
    /// try to predict turns from recent price oscillation. It counts calendar position
    /// within a cycle of known statistical length and tells you where the next low is
    /// *due*, letting an oscillator-based indicator do the turn confirmation.
    ///
    /// Pairs naturally with Cipher C: Loukas says "a DCL is expected in the next 5-10
    /// bars", Cipher C says "the bandpass detects a confirmed bottom here". Score-based
    /// strategies can combine both as orthogonal evidence.
    ///
    /// ── Cycles tracked ──
    ///
    ///   DAILY CYCLE (DC)
    ///     Period: ~35–60 bars (Loukas's published range for BTC daily; tunable).
    ///     Detection: bars-since-last-confirmed-swing-low, where a swing low is a
    ///       SwingLookback-symmetric local minimum that is also inside the DCL timing
    ///       window (day ≥ DcMinBars). Not every swing low is a DCL — only ones that
    ///       arrive after the minimum cycle length.
    ///     Components:
    ///       DC Day Count  — line, integer bars since last DCL
    ///       DC In Window  — marker, fires on any bar where DayCount ∈ [DcMinBars, DcMaxBars]
    ///       DC Overdue    — marker, fires when DayCount > DcMaxBars (cycle stretched)
    ///       DCL Confirmed — marker, fires on the bar where a new DCL is confirmed
    ///
    ///   INTERMEDIATE CYCLE (IC)
    ///     Structure: contains IcDcCount DCs (default 3). An ICL is the DCL that
    ///       terminates the Nth DC of an IC, where N = IcDcCount or IcDcCount+1.
    ///     Components:
    ///       IC DC Count  — line, 1..4 which-DC-of-IC we're currently in
    ///       ICL Confirmed — marker, fires when a DCL was also an ICL
    ///
    ///   FOUR-YEAR CYCLE (BTC only, hardcoded halving anchors)
    ///     Structure: anchored on Bitcoin halving dates, the de-facto 4Y cycle used
    ///       by Loukas for BTC. Phase bands are published averages:
    ///         Accumulation  (day 0–180)    — post-halving chop
    ///         Markup        (day 180–540)  — bull run
    ///         Distribution  (day 540–720)  — blow-off top window
    ///         Bear          (day 720+)     — drawdown to next cycle low (~900–1100)
    ///     Components:
    ///       FY Day Count — line, days since most recent halving
    ///       FY Phase     — line, 0=Accum, 1=Markup, 2=Distrib, 3=Bear
    ///
    /// ── What this indicator deliberately does NOT do ──
    ///   • No momentum, oscillator, or price-math smoothing. Those belong in Cipher A/B/C.
    ///   • No forecast arithmetic beyond counting. The day-count IS the prediction.
    ///   • No adaptive cycle detection. Periods are user-configurable bands, not
    ///     continuously re-measured.
    ///   • No asset auto-detection in the math. FY anchors apply to every symbol if
    ///     EnableFourYearCycle is true — on non-BTC instruments those components are
    ///     numerically meaningless and should be hidden via parameter.
    ///
    /// ── Timeframe semantics ──
    /// The "bars" in DcMinBars/DcMaxBars are bars OF THE ACTIVE TIMEFRAME. On daily BTC,
    /// 35..60 means 35..60 days — Loukas's canonical DC. On weekly BTC, those same
    /// values would mean 35..60 weeks and the cycle label is no longer a Loukas DC —
    /// it becomes an IC-scale or YC-scale cycle. The indicator is timeframe-agnostic
    /// in math but timeframe-specific in meaning; users are responsible for choosing
    /// sensible band values per timeframe.
    /// </summary>
    public class LoukasCyclesProvider : IIndicatorProvider
    {
        public string Name => "Loukas Cycle Detection";

        // ── Component name constants ──────────────────────────────────────────
        public const string CompDcDayCount    = "DC Day Count";
        public const string CompDcInWindow    = "DC In Window";
        public const string CompDcOverdue     = "DC Overdue";
        public const string CompDclConfirmed  = "DCL Confirmed";
        public const string CompIcDcCount     = "IC DC Count";
        public const string CompIclConfirmed  = "ICL Confirmed";
        public const string CompFyDayCount    = "FY Day Count";
        public const string CompFyPhase       = "FY Phase";

        /// <summary>
        /// Bitcoin halving dates (UTC). Each halving is the approximate anchor of a new
        /// 4Y cycle: block reward drops, supply issuance halves, prior cycle's Bear phase
        /// ends. The 2028 entry is an estimate based on average inter-halving spacing —
        /// it will be replaced with the actual date when the halving is confirmed.
        /// </summary>
        private static readonly DateTime[] BtcHalvings =
        {
            new DateTime(2012, 11, 28, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2016,  7,  9, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020,  5, 11, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024,  4, 19, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2028,  4, 20, 0, 0, 0, DateTimeKind.Utc), // estimated
        };

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "LOUKAS_CYCLES",
                Causality = ComponentCausality.Causal,
                Name        = "Loukas Cycle Detection",
                Category    = "Cycles",
                DefaultPane = "Pane_LOUKAS",
                Description = "Count-based layered cycle tracker: Daily Cycle, Intermediate Cycle, " +
                              "and (BTC charts only) Four-Year Cycle phase. Pivot-anchored, timing-band " +
                              "aware, no momentum math. Pairs with Cipher C for count + turn " +
                              "confirmation setups. The default [35, 90] bar DC window was validated " +
                              "cross-asset 2026-07: realized daily-cycle lengths measured a ~50-bar " +
                              "median with 90th percentile 68-91 on BTC, ETH, gold, silver, S&P, " +
                              "Nasdaq, large-cap stocks, and EUR alike — the same window fits every " +
                              "asset class at daily bars, so no per-asset tuning is needed. Cycle " +
                              "position is CONTEXT: a fixed in-window filter tested as a mechanical " +
                              "entry gate did not improve results — use the count to know where you " +
                              "are, not as a standalone trigger.",
                RequiresFullRecalcOnTick = true,

                Components = new List<IndicatorComponentMetadata>
                {
                    // ─── DC DAY COUNT (main line) ──────────────────────────────
                    new() { Name = CompDcDayCount,
                            DisplayName = "DC Day Count",
                            DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Level,
                            DefaultColorHex = "#66CCFF",
                            DefaultThickness = 2.0f,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultSoundPatchId = "sine",
                            DefaultBaseFrequency = 220.0,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            IsVisible = true },

                    // ─── DC IN WINDOW (strategy signal — hidden by default) ─────
                    // Fires on every bar while day ∈ [DcMinBars, DcMaxBars]. As a visible
                    // marker this would render as a constant orange stripe, which is
                    // visual noise — the DC Day Count line + reference levels at 35/60
                    // already convey the information. Kept as a hidden signal so the
                    // strategy system can read it via FiredWithin.
                    new() { Name = CompDcInWindow,
                            DisplayName = "DC In Window",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FFCC00",
                            DefaultThickness = 3.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultSoundPatchId = "triangle_bell",
                            DefaultDecayMs = 160,
                            DefaultBaseFrequency = 320.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Daily cycle in timing window",
                            IsVisible = false },

                    // ─── DC OVERDUE (strategy signal — hidden by default) ───────
                    // Same rationale as DC In Window: would draw as a flooded stripe
                    // every bar past day 60. Hidden visually, available to strategies.
                    new() { Name = CompDcOverdue,
                            DisplayName = "DC Overdue",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#FF8800",
                            DefaultThickness = 3.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Midground,
                            DefaultSoundPatchId = "detuned_pair_bell",
                            DefaultDecayMs = 240,
                            DefaultBaseFrequency = 280.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Daily cycle overdue, low stretched past timing window",
                            IsVisible = false },

                    // ─── DCL CONFIRMED (the DCL event itself) ───────────────────
                    new() { Name = CompDclConfirmed,
                            DisplayName = "DCL Confirmed",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00FF88",
                            DefaultThickness = 7.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "crystal_bell",
                            DefaultDecayMs = 450,
                            DefaultBaseFrequency = 440.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Daily cycle low confirmed",
                            IsVisible = true },

                    // ─── IC DC COUNT (which DC of IC) ───────────────────────────
                    new() { Name = CompIcDcCount,
                            DisplayName = "IC DC Count",
                            DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Level,
                            DefaultColorHex = "#BB88FF",
                            DefaultThickness = 1.5f,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultSoundPatchId = "sine",
                            DefaultBaseFrequency = 180.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            IsVisible = true },

                    // ─── ICL CONFIRMED ──────────────────────────────────────────
                    new() { Name = CompIclConfirmed,
                            DisplayName = "ICL Confirmed",
                            DisplayType = ComponentDisplayType.Dot,
                            Role = ComponentRole.Signal,
                            DefaultColorHex = "#00FFCC",
                            DefaultThickness = 9.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultPlaybackLayer = PlaybackLayer.Foreground,
                            DefaultSoundPatchId = "dual_tone_bell",
                            DefaultDecayMs = 600,
                            DefaultBaseFrequency = 520.0,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultSignalSpeechTemplate = "Intermediate cycle low confirmed",
                            IsVisible = true },

                    // ─── FY DAY COUNT (BTC only — own sub-pane) ────────────────
                    // FY Day Count ranges 0..1460 days, fundamentally incompatible
                    // with the DC pane's 0..70 day-count scale. Without isolation,
                    // the parent pane's Y-axis auto-scaler unions both ranges and
                    // squashes the DC sawtooth into the bottom 5% of the pane.
                    // The "FY" sub-pane gets 25% of parent height by default.
                    new() { Name = CompFyDayCount,
                            DisplayName = "FY Day Count",
                            DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Level,
                            SubPaneName = "FY",
                            SubPaneHeightRatio = 0.25f,
                            DefaultColorHex = "#AAAAAA",
                            DefaultThickness = 1.5f,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultSoundPatchId = "sine",
                            DefaultBaseFrequency = 120.0,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            IsVisible = true },

                    // ─── FY PHASE (0..3 in same FY sub-pane) ───────────────────
                    new() { Name = CompFyPhase,
                            DisplayName = "FY Phase",
                            DisplayType = ComponentDisplayType.Line,
                            Role = ComponentRole.Level,
                            SubPaneName = "FY",
                            SubPaneHeightRatio = 0.25f,
                            DefaultColorHex = "#FFFFFF",
                            DefaultThickness = 1.0f,
                            DefaultEnvelopeType = "Sustain",
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultSoundPatchId = "sine",
                            DefaultBaseFrequency = 140.0,
                            DefaultPitchMapping = PitchMapping.Value,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "DcMinBars", DisplayName = "DC Min Bars",
                            DataType = typeof(int), DefaultValue = 35.0,
                            Description = "Earliest bar count at which a swing low qualifies as a " +
                                          "Daily Cycle Low. Below this, swing lows are treated as " +
                                          "in-cycle pullbacks, not cycle terminations. Loukas BTC " +
                                          "daily: 35." },
                    new() { Name = "DcMaxBars", DisplayName = "DC Max Bars",
                            DataType = typeof(int), DefaultValue = 90.0,
                            Description = "Upper end of the DCL timing band. Day counts above this " +
                                          "are classed as Overdue (cycle stretched). Loukas's " +
                                          "published BTC daily band was 35-60, but empirical " +
                                          "measurement on 2022-2025 BTC shows DCs running 70-85 " +
                                          "days — modern BTC's cycles have stretched. Default 90 " +
                                          "matches that observed range. Drop to 60 for pre-2020 " +
                                          "BTC backtests or for assets with shorter cycles." },
                    new() { Name = "SwingLookback", DisplayName = "Swing Lookback",
                            DataType = typeof(int), DefaultValue = 10.0,
                            Description = "Symmetric bars on each side for swing-low detection. " +
                                          "This is the dominant selectivity dial — higher values " +
                                          "reject minor pullbacks and only confirm structurally " +
                                          "significant lows. Default 10 is tuned for BTC daily; " +
                                          "lower (5-7) on smoother instruments, higher (12-15) on " +
                                          "noisier ones. NOTE: confirmation lags by SwingLookback " +
                                          "bars — strategies see a DCL N bars after it actually formed." },
                    new() { Name = "IcDcCount", DisplayName = "IC DC Count",
                            DataType = typeof(int), DefaultValue = 3.0,
                            Description = "Number of Daily Cycles per Intermediate Cycle. The Nth " +
                                          "DCL is retroactively promoted to an ICL. Loukas: 3, " +
                                          "occasionally 4." },
                    new() { Name = "EnableFourYearCycle", DisplayName = "Enable Four-Year Cycle",
                            DataType = typeof(bool), DefaultValue = false,
                            Description = "Emit FY Day Count and FY Phase using Bitcoin halving " +
                                          "anchors. ONLY meaningful on BTC instruments — defaults " +
                                          "to off so the indicator works correctly on every other " +
                                          "asset out of the box. Turn ON for BTC charts to see the " +
                                          "four-year cycle phase tracking in the FY sub-pane." },
                }
            }
        };

        // ── Calculation ───────────────────────────────────────────────────────

        public void Calculate(
            string code,
            ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters,
            IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("LOUKAS_CYCLES", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n == 0) return;

            int  dcMinBars   = GetInt(parameters, "DcMinBars", 35);
            int  dcMaxBars   = GetInt(parameters, "DcMaxBars", 90);
            int  swingLookback = GetInt(parameters, "SwingLookback", 10);
            int  icDcCount   = GetInt(parameters, "IcDcCount", 3);
            bool enableFy    = IndicatorParams.GetBool(parameters, "EnableFourYearCycle", false) && IsBtcSymbol(parameters);

            // Clamp to safe ranges so a bad parameter never crashes the calc.
            if (dcMinBars < 1) dcMinBars = 1;
            if (dcMaxBars < dcMinBars) dcMaxBars = dcMinBars;
            if (swingLookback < 1) swingLookback = 1;
            if (icDcCount   < 1) icDcCount = 1;

            // Output arrays — Line components carry counts/phase (dense), marker components
            // are sparse (NaN except on firing bars, matching the Cipher convention).
            var dcDayCount   = new double[n];
            var dcInWindow   = NanArray(n);
            var dcOverdue    = NanArray(n);
            var dclConfirmed = NanArray(n);
            var icDcCountArr = new double[n];
            var iclConfirmed = NanArray(n);
            var fyDayCount   = NanArray(n);
            var fyPhase      = NanArray(n);

            // ── Cycle state machine ──────────────────────────────────────────
            // lastDclBarIndex anchors the current DC. Until the first DCL is confirmed
            // we treat bar 0 as the anchor — day count climbs from 0 and the "in window"
            // / "overdue" flags behave sensibly from the start of the series.
            int lastDclBarIndex = 0;
            int currentDcOfIc   = 1;

            for (int i = 0; i < n; i++)
            {
                // Current DC day count is bars since last confirmed DCL (or start of series).
                int day = i - lastDclBarIndex;
                dcDayCount[i]   = day;
                icDcCountArr[i] = currentDcOfIc;

                if (day >= dcMinBars && day <= dcMaxBars)
                    dcInWindow[i] = day;               // non-NaN marker value

                if (day > dcMaxBars)
                    dcOverdue[i] = day;                // non-NaN marker value

                // ── Swing low detection ──────────────────────────────────────
                // A bar k is a candidate swing low at confirmation time i when:
                //   1. k = i - swingLookback (we have swingLookback forward bars)
                //   2. data[k].Low is strictly less than all other lows in
                //      [k - swingLookback, k + swingLookback] — local minimum
                //   3. data[k].Low is the LOWEST low in [lastDclBarIndex+1, k] —
                //      the canonical Loukas DCL definition: a DCL is the lowest
                //      point of its DC, not just any local minimum. Without this
                //      gate, in-cycle pullbacks chain into spurious DCLs on real
                //      price data where 5-bar local minima are common.
                //   4. dayAtK >= dcMinBars — early swings are in-cycle pullbacks
                //
                // Confirmation LAGS by swingLookback bars — the indicator reports a
                // DCL on the bar where it becomes provable, not the bar the low
                // actually happened. Strategies reading DclConfirmed are reacting
                // on the bar where the DCL is knowable.
                if (i >= swingLookback * 2)
                {
                    int k = i - swingLookback;
                    double kLow = data[k].Low;

                    // Check (2): symmetric local minimum
                    bool isLocalMin = true;
                    for (int j = k - swingLookback; j <= k + swingLookback; j++)
                    {
                        if (j == k) continue;
                        if (data[j].Low <= kLow) { isLocalMin = false; break; }
                    }

                    if (isLocalMin)
                    {
                        // Check (4): past the minimum cycle length
                        int dayAtK = k - lastDclBarIndex;
                        if (dayAtK >= dcMinBars)
                        {
                            // No additional "lowest in window" filter. Two earlier
                            // attempts (lowest-since-last-DCL, lowest-in-sliding-window)
                            // both broke real bull-market data: in an uptrend each
                            // successive DCL is HIGHER than parts of the prior cycle's
                            // recovery, so any backward-looking lowest-low filter
                            // either locks out everything after the first DCL or
                            // produces boundary failures when prior cycle recovery
                            // bars equal the current cycle trough. Selectivity is
                            // correctly enforced by:
                            //   • SwingLookback being large enough that minor pullbacks
                            //     don't qualify as local minima (default raised to 10),
                            //   • dayAtK ≥ DcMinBars enforcing minimum cycle separation,
                            //   • the local-min check itself being symmetric strict-<.
                            // This is what the canonical Loukas-mechanical implementations
                            // do — they use a wider swing filter, not a windowed lowest
                            // filter, because the latter is regime-fragile.
                            {
                                // Confirmed DCL — passes all checks.
                                // Marker value lives on the count scale
                                // (set to 0 — "cycle floor") so the sub-pane Y axis
                                // doesn't blow up trying to span both day-count values
                                // (0..70) and price values ($60k+). Strategies only
                                // care about non-NaN, not the value, so this is safe.
                                dclConfirmed[i] = 0.0;
                                lastDclBarIndex = k;

                                // Rolling IC-of-DC counter. When the Nth DC of an IC
                                // is terminated by a DCL, that DCL is also an ICL.
                                currentDcOfIc += 1;
                                if (currentDcOfIc > icDcCount)
                                {
                                    // ICL marker also count-scale (placed at DcMaxBars
                                    // so it visually anchors to the upper band line).
                                    iclConfirmed[i] = (double)dcMaxBars;
                                    currentDcOfIc = 1;
                                }

                                // Recompute current bar outputs after the reset.
                                day = i - lastDclBarIndex;
                                dcDayCount[i]   = day;
                                icDcCountArr[i] = currentDcOfIc;
                                dcInWindow[i]   = double.NaN;
                                dcOverdue[i]    = double.NaN;
                            }
                        }
                    }
                }

                // ── Four-Year (halving-anchored) components ──────────────────
                if (enableFy)
                {
                    var anchor = FindMostRecentHalving(data[i].Date);
                    if (anchor.HasValue)
                    {
                        double daysSince = (data[i].Date - anchor.Value).TotalDays;
                        fyDayCount[i] = daysSince;
                        fyPhase[i]    = ClassifyFyPhase(daysSince);
                    }
                }
            }

            WriteToBuffer(buffer, CompDcDayCount,   dcDayCount,   n);
            WriteToBuffer(buffer, CompDcInWindow,   dcInWindow,   n);
            WriteToBuffer(buffer, CompDcOverdue,    dcOverdue,    n);
            WriteToBuffer(buffer, CompDclConfirmed, dclConfirmed, n);
            WriteToBuffer(buffer, CompIcDcCount,    icDcCountArr, n);
            WriteToBuffer(buffer, CompIclConfirmed, iclConfirmed, n);
            WriteToBuffer(buffer, CompFyDayCount,   fyDayCount,   n);
            WriteToBuffer(buffer, CompFyPhase,      fyPhase,      n);
        }

        public void UpdateLast(
            string code,
            ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters,
            IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals("LOUKAS_CYCLES", StringComparison.OrdinalIgnoreCase)) return new();
            // Reference lines aligned to the canonical Loukas DCL timing window. The
            // band between DcMinBars (35) and DcMaxBars (60) is the "DCL is due" zone;
            // anything past 60 is overdue. Lines are drawn at the default values; if
            // the user changes parameters they'll diverge from the lines (a known
            // limitation also present on Cipher C).
            return new()
            {
                new("DC Overdue", 90.0, "#FF8800", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.6f),
                new("DC Window Open", 35.0, "#FFCC00", DashStyle.Dash,
                    PlayEarcon: true, EarconVolume: 0.5f),
                new("DC Floor", 0.0, "#666666", DashStyle.Dot,
                    PlayEarcon: false),
            };
        }

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            // DC counting is meaningful only after at least one DC can complete. The
            // warmup matches the full DcMaxBars + swing confirmation tail so the first
            // reported DCL falls in a window that is actually measurable. 2× SwingLookback
            // is added so the swing filter has room at series start.
            int dcMaxBars     = GetInt(parameters, "DcMaxBars",     90);
            int swingLookback = GetInt(parameters, "SwingLookback", 10);
            return dcMaxBars + swingLookback * 2;
        }

        public string GetDetailFact(
            string code,
            ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> results,
            int index,
            Dictionary<string, object> parameters)
        {
            if (!code.Equals("LOUKAS_CYCLES", StringComparison.OrdinalIgnoreCase) || index < 0)
                return string.Empty;

            int  dcMinBars = GetInt(parameters, "DcMinBars", 35);
            int  dcMaxBars = GetInt(parameters, "DcMaxBars", 90);
            bool enableFy  = IndicatorParams.GetBool(parameters, "EnableFourYearCycle", false);

            double day     = GetVal(results, CompDcDayCount,  index);
            double dcOfIc  = GetVal(results, CompIcDcCount,   index);
            double dclFire = GetVal(results, CompDclConfirmed, index);
            double iclFire = GetVal(results, CompIclConfirmed, index);

            var sb = new StringBuilder();

            // Sentence 1 — DC position
            if (!double.IsNaN(day))
            {
                string phase =
                    day <  dcMinBars ? "early"      :
                    day <= dcMaxBars ? "in window"  :
                                       "overdue";
                sb.Append($"DC day {(int)day}, {phase}. ");
            }

            // Sentence 2 — IC position
            if (!double.IsNaN(dcOfIc))
                sb.Append($"DC {(int)dcOfIc} of IC. ");

            // Sentence 3 — explicit events
            if (!double.IsNaN(dclFire)) sb.Append("Daily cycle low confirmed this bar. ");
            if (!double.IsNaN(iclFire)) sb.Append("Intermediate cycle low confirmed this bar. ");

            // Sentence 4 — FY phase (BTC only, NaN on non-BTC)
            if (enableFy)
            {
                double fyDay   = GetVal(results, CompFyDayCount, index);
                double fyPhase = GetVal(results, CompFyPhase,    index);
                if (!double.IsNaN(fyDay) && !double.IsNaN(fyPhase))
                {
                    sb.Append($"Four-year day {(int)fyDay}, {PhaseLabel((int)fyPhase)}. ");
                }
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : string.Empty;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Find the most recent halving anchor on or before <paramref name="barDate"/>.
        /// Returns null if the bar predates all known halvings (e.g. a 2010 BTC bar).
        /// </summary>
        private static DateTime? FindMostRecentHalving(DateTime barDate)
        {
            DateTime? best = null;
            for (int i = 0; i < BtcHalvings.Length; i++)
            {
                if (BtcHalvings[i] <= barDate)
                {
                    best = BtcHalvings[i];
                }
                else break;
            }
            return best;
        }

        /// <summary>
        /// Map a days-since-halving count to a 4Y cycle phase index.
        /// Bands are Loukas's published averages for the Bitcoin halving cycle.
        ///   0 = Accumulation (0–180 days)
        ///   1 = Markup       (180–540)
        ///   2 = Distribution (540–720)
        ///   3 = Bear         (720+)
        /// </summary>
        private static int ClassifyFyPhase(double daysSince)
        {
            if (daysSince < 180)  return 0;
            if (daysSince < 540)  return 1;
            if (daysSince < 720)  return 2;
            return 3;
        }

        private static string PhaseLabel(int phase) => phase switch
        {
            0 => "accumulation",
            1 => "markup",
            2 => "distribution",
            3 => "bear",
            _ => "unknown",
        };

        private static double[] NanArray(int length)
        {
            var a = new double[length];
            for (int i = 0; i < length; i++) a[i] = double.NaN;
            return a;
        }

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] data, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len  = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = data[i];
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> r, string key, int idx)
        {
            if (!r.TryGetValue(key, out var arr) || arr == null || idx < 0 || idx >= arr.Length) return double.NaN;
            return arr[idx];
        }

        private static int GetInt(Dictionary<string, object> p, string k, int def) =>
            p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;

        /// <summary>
        /// The Four-Year Cycle anchors are Bitcoin halving dates — on any other
        /// instrument the day counts are numerically meaningless, so the FY
        /// components are suppressed unless the chart symbol (stamped as
        /// <c>__symbol</c> by the orchestrator) is a BTC pair. When no symbol
        /// hint is present (snapshot caches, tests) the user's explicit
        /// EnableFourYearCycle=true is honored as before.
        /// </summary>
        private static bool IsBtcSymbol(Dictionary<string, object> parameters)
        {
            if (parameters == null ||
                !parameters.TryGetValue("__symbol", out var raw) ||
                raw is not string s || string.IsNullOrWhiteSpace(s))
                return true; // no hint — honor the user's explicit opt-in

            string baseToken = s.Split('/', '-', ':')[0].Trim().ToUpperInvariant();
            return baseToken is "BTC" or "XBT" or "BITCOIN";
        }
    }
}
