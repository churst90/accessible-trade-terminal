using System.Globalization;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// <b>Cipher C — cycle lows, after Camel Finance's CF Cycle Trading Indicator.</b> Rebuilt
    /// 2026-10-21 (Cody: "Cipher c needs to be designed as close to the cf indicator as possible
    /// from any and all screen grabs"). The previous Cipher C was an Ehlers cyber-cycle oscillator
    /// in its own pane; its ten components are retired in <see cref="Strategies.SignalCatalog"/>.
    /// It also replaces Loukas Cycle Detection, which is kept working but no longer offered.
    ///
    /// <para>
    /// <b>What is drawn, and what CF draws.</b> CF is closed source; everything here comes from
    /// 86 vendor images and 405 frames of Camel's videos (strategy-lab-data/cf-frames/REPORT.md,
    /// summarised in docs/CIPHER_C_DESIGN.md). On the price chart:
    /// </para>
    /// <list type="bullet">
    /// <item>A green triangle and D ring under each daily cycle low; a bigger blue triangle and W
    /// ring under each weekly low; and a gold triangle and Y ring under each yearly low. Drawn on the
    /// LOW's bar, so they are known only once confirmed (look-ahead, like CF's).</item>
    /// <item>The cycle's length above each low, with CF's unit suffix: 49D for days and 24W for
    /// weeks, counted inclusively as CF does. The suffix is printed, not spoken.</item>
    /// <item>A green rounded check box on the bar a low is CONFIRMED, a grey-blue rounded refresh
    /// box while one is pending, a red cross when price closes under a low too soon after it and
    /// cancels it (CF's red X), and an orange cross when price closes under a low after that, which
    /// is a failed cycle.</item>
    /// <item>Triangles over each finished cycle's top: red when it came early (left translated,
    /// bearish), orange in the middle, green late (right translated, bullish).</item>
    /// <item>CF's yellow low-to-low arcs, a wave of equal humps under the candles with its troughs under
    /// the lows' marks.</item>
    /// <item>A soft green tint behind the bars where the daily cycle was inside its timing window.</item>
    /// <item>The 10-bar simple moving average Camel trades against (CF's yellow line).</item>
    /// </list>
    ///
    /// <para>
    /// <b>Tiers follow the chart.</b> A tier runs only where its cycle spans at least six bars and
    /// the bar is no longer than its unit, so a weekly chart has weekly and yearly lows and no
    /// "daily cycle low" at all — the confusion Cody hit on Loukas's weekly chart. The detector,
    /// the presets and its measured agreement with CF are in <see cref="CipherCEngine"/>.
    /// </para>
    /// </summary>
    public class CipherCProvider : IIndicatorProvider
    {
        public const string Code = "CIPHER_C";
        public string Name => "Cipher C";

        // ── Components ─────────────────────────────────────────────────────────
        // DisplayName is the Name throughout: speech and navigation key on either.
        public const string CompSma            = "SMA";
        public const string CompYcl            = "YCL";
        public const string CompWcl            = "WCL";
        public const string CompDcl            = "DCL";
        public const string CompDclCount       = "DCL Count";
        public const string CompWclCount       = "WCL Count";
        public const string CompDclConfirmed   = "DCL Confirmed";
        public const string CompWclConfirmed   = "WCL Confirmed";
        public const string CompYclConfirmed   = "YCL Confirmed";
        public const string CompPending        = "Cycle Low Pending";
        public const string CompCancelled      = "Cycle Low Cancelled";
        public const string CompUpdated        = "Cycle Low Updated";
        public const string CompFailed         = "Cycle Failed";
        public const string CompLeftTop        = "Left Translated Top";
        public const string CompMidTop         = "Mid Translated Top";
        public const string CompRightTop       = "Right Translated Top";
        public const string CompArc            = "Cycle Arc";
        public const string CompArcPosition    = "Cycle Arc Position";
        public const string CompInDclWindow    = "In DCL Window";
        public const string CompInWclWindow    = "In WCL Window";
        public const string CompDclDay         = "DCL Day";
        public const string CompWclWeek        = "WCL Week";
        public const string CompDclWindowStart = "DCL Window Start";
        public const string CompDclWindowEnd   = "DCL Window End";
        public const string CompWclWindowStart = "WCL Window Start";
        public const string CompWclWindowEnd   = "WCL Window End";
        public const string CompConfluence     = "Cycle Low Confluence";
        public const string CompPreset         = "Asset Preset";

        // ── Parameters ─────────────────────────────────────────────────────────
        public const string ParamPreset     = "Preset";
        public const string ParamDailyMin   = "DailyMinDays";
        public const string ParamDailyMax   = "DailyMaxDays";
        public const string ParamWeeklyMin  = "WeeklyMinWeeks";
        public const string ParamWeeklyMax  = "WeeklyMaxWeeks";
        public const string ParamYearlyMin  = "YearlyMinYears";
        public const string ParamYearlyMax  = "YearlyMaxYears";
        public const string ParamSma        = "SmaLength";
        public const string ParamOverbought = "Overbought";
        public const string ParamOversold   = "Oversold";

        public const int DefaultSma = 10;
        public const double DefaultOverbought = 75, DefaultOversold = 10;

        internal const string DclHex = "#00E676", WclHex = "#2979FF", YclHex = "#FFD600", SmaHex = "#FFEB3B", ArcHex = "#D4A72C";

        private static IndicatorComponentMetadata Low(string name, string hex, float size, string tier, string markerText, string patch, double freq, int order) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.TriangleUpRing, MarkerText = markerText, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Lookahead, DefaultMarkerAnchor = MarkerAnchor.BelowBar,
            DefaultColorHex = hex, DefaultThickness = size, DefaultDrawOrder = order,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = PlaybackLayer.Midground,
            DefaultSoundPatchId = patch, DefaultBaseFrequency = freq, DefaultDecayMs = 400,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = $"{tier} cycle low.",
        };

        private static IndicatorComponentMetadata Event(string name, ComponentDisplayType shape, MarkerAnchor anchor, string hex,
            float size, string patch, double freq, PlaybackLayer layer, string speech, bool visible = true) => new()
        {
            Name = name, DisplayName = name, DisplayType = shape, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Causal, DefaultMarkerAnchor = anchor,
            DefaultColorHex = hex, DefaultThickness = size, IsVisible = visible,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = layer,
            DefaultSoundPatchId = patch, DefaultBaseFrequency = freq, DefaultDecayMs = 300,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
        };

        private static IndicatorComponentMetadata Top(string name, string hex, string speech) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.TriangleDown, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Lookahead, DefaultMarkerAnchor = MarkerAnchor.AboveBar,
            DefaultColorHex = hex, DefaultThickness = 3f,
            DefaultEnvelopeType = "Ping", DefaultPlaybackLayer = PlaybackLayer.Background,
            DefaultSoundPatchId = "triangle_bell", DefaultBaseFrequency = 520, DefaultDecayMs = 200,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
        };

        /// <summary>A number carried for speech and strategies, not drawn and not sounded.</summary>
        private static IndicatorComponentMetadata Hidden(string name, ComponentCausality causality = ComponentCausality.Causal) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
            Causality = causality, IsVisible = false, DefaultIsMuted = true, DefaultColorHex = "#9E9E9E",
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultPlaybackLayer = PlaybackLayer.Background,
        };

        private static IndicatorComponentMetadata Window(string name, string tintHex, BackgroundTint tint) => new()
        {
            Name = name, DisplayName = name, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Causal, IsVisible = false, DefaultIsMuted = true, DefaultColorHex = "#9E9E9E",
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultPlaybackLayer = PlaybackLayer.Background,
            // 1 inside the window, 0 outside: the whole 0x18 alpha at 1, nothing at 0.
            DefaultBackgroundTint = tint, DefaultTintColorAboveHex = tintHex, DefaultTintColorBelowHex = "#00000000",
            DefaultTintMidline = 0.5, DefaultTintFullAt = 1.0,
        };

        private static readonly string PresetKeys = string.Join(", ", CipherCEngine.Presets.Select(p => p.Key));

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Name = "Cipher C",
                Category = "Cycles",
                DefaultPane = "Main",
                Causality = ComponentCausality.Causal,
                // The confirm/cancel state machine and the tier's moving windows are re-decided
                // from the whole history; a cheap last-bar update would miss a cancellation.
                RequiresFullRecalcOnTick = true,
                Description =
                    "Cycle lows on the price chart, after Camel Finance's CF Cycle Trading Indicator (closed source; " +
                    "rebuilt from screen grabs). Green D-ringed triangles under daily lows, blue W-ringed triangles " +
                    "under weekly lows, gold Y-ringed triangles under yearly lows, 49D / 24W cycle counts, a green " +
                    "rounded check box when a low is confirmed, a grey-blue refresh box while pending, crosses when " +
                    "one is cancelled or a cycle fails, translated-top triangles, CF's yellow arcs and a tint over " +
                    "the daily timing " +
                    "window. Ranges are CF's for the S&P, gold, Ethereum, Coinbase, MicroStrategy, Marathon Digital, " +
                    "Riot Platforms, gold miners and Bitcoin, and estimated for the other seven of CF's " +
                    "sixteen assets. Fitted to CF's own SPX labels: 18 of 25 lows within two bars; held out on gold, " +
                    "the Dow and Bitcoin, 8 of 13 within four days. Lows are drawn on the low's bar but known only when " +
                    "confirmed, a median of nine bars later. A map of the method, not an edge.",
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = ParamPreset, DisplayName = "Asset preset", DataType = typeof(string), DefaultValue = "Auto",
                            Description = $"Whose cycle ranges to use. Auto reads the chart's symbol (SPY, ES and ^GSPC are the S&P; GLD and XAUUSD gold; and so on). Or type one of: {PresetKeys}." },
                    new() { Name = ParamDailyMin, DisplayName = "Daily cycle shortest (days)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's. In the chart's own days: calendar days for crypto, trading days for stocks." },
                    new() { Name = ParamDailyMax, DisplayName = "Daily cycle longest (days)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { Name = ParamWeeklyMin, DisplayName = "Weekly cycle shortest (weeks)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { Name = ParamWeeklyMax, DisplayName = "Weekly cycle longest (weeks)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 1,
                            Description = "0 = the preset's." },
                    new() { Name = ParamYearlyMin, DisplayName = "Yearly cycle shortest (years)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 0.5,
                            Description = "0 = the preset's." },
                    new() { Name = ParamYearlyMax, DisplayName = "Yearly cycle longest (years)", DataType = typeof(double), DefaultValue = 0.0, MinValue = 0, Step = 0.5,
                            Description = "0 = the preset's." },
                    new() { Name = ParamSma, DisplayName = "Moving average length", DataType = typeof(int), DefaultValue = (double)DefaultSma, MinValue = 2, Step = 1,
                            Description = "The simple moving average drawn on price. Camel uses 10." },
                    new() { Name = ParamOverbought, DisplayName = "Confirm level", DataType = typeof(double), DefaultValue = DefaultOverbought, MinValue = 1, MaxValue = 99, Step = 1,
                            Description = "CF's 'overbought', 75. A low is confirmed when the hidden cycle line climbs through this after reaching the oversold level." },
                    new() { Name = ParamOversold, DisplayName = "Arm level", DataType = typeof(double), DefaultValue = DefaultOversold, MinValue = 0, MaxValue = 99, Step = 1,
                            Description = "CF's 'oversold', 10. The hidden cycle line must reach this before the next low can be confirmed." },
                },
                Components = new List<IndicatorComponentMetadata>
                {
                    new() { Name = CompSma, DisplayName = CompSma, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Median,
                            Causality = ComponentCausality.Causal, DefaultColorHex = SmaHex, DefaultThickness = 1.5f,
                            DefaultUsePolarityColoring = false, DefaultIsAreaFill = false, DefaultPlaybackLayer = PlaybackLayer.Background },

                    // Biggest first, so the daily triangle is drawn over the weekly over the yearly.
                    Low(CompYcl, YclHex, 6f,   "Yearly", "Y", "dual_tone_bell", 196, 0),
                    Low(CompWcl, WclHex, 4.5f, "Weekly", "W", "dual_tone_bell", 262, 1),
                    Low(CompDcl, DclHex, 3f,   "Daily",  "D", "sine_bell",      330, 2),

                    new() { Name = CompDclCount, DisplayName = CompDclCount, DisplayType = ComponentDisplayType.ValueLabel, Role = ComponentRole.Signal,
                            Causality = ComponentCausality.Lookahead, MarkerText = "D", DefaultMarkerAnchor = MarkerAnchor.AboveBar, DefaultColorHex = DclHex,
                            DefaultIsMuted = true, DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background },
                    // Hidden by default: on a daily chart it would print on top of the daily count.
                    new() { Name = CompWclCount, DisplayName = CompWclCount, DisplayType = ComponentDisplayType.ValueLabel, Role = ComponentRole.Signal,
                            Causality = ComponentCausality.Lookahead, MarkerText = "W", DefaultMarkerAnchor = MarkerAnchor.AboveBar, DefaultColorHex = WclHex,
                            IsVisible = false, DefaultIsMuted = true, DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background },

                    Event(CompDclConfirmed, ComponentDisplayType.CheckBox, MarkerAnchor.AboveBar, DclHex, 3f, "sine_bell", 392, PlaybackLayer.Foreground,
                          "Daily cycle low confirmed. Close {price}."),
                    Event(CompWclConfirmed, ComponentDisplayType.CheckBox, MarkerAnchor.AboveBar, DclHex, 4f, "dual_tone_bell", 294, PlaybackLayer.Foreground,
                          "Weekly cycle low confirmed. Close {price}."),
                    Event(CompYclConfirmed, ComponentDisplayType.CheckBox, MarkerAnchor.AboveBar, DclHex, 5f, "dual_tone_bell", 220, PlaybackLayer.Foreground,
                          "Yearly cycle low confirmed. Close {price}."),
                    Event(CompPending, ComponentDisplayType.RefreshBox, MarkerAnchor.AboveBar, "#78909C", 2.5f, "triangle_bell", 440, PlaybackLayer.Background,
                          "Cycle low pending."),
                    Event(CompCancelled, ComponentDisplayType.Cross, MarkerAnchor.BelowBar, "#FF1744", 3f, "triangle_bell", 220, PlaybackLayer.Midground,
                          "Cycle low cancelled, price closed under it. Close {price}."),
                    Event(CompUpdated, ComponentDisplayType.Square, MarkerAnchor.AboveBar, DclHex, 3f, "sine_bell", 392, PlaybackLayer.Background,
                          "Cycle low moved to the new low.", visible: false),
                    Event(CompFailed, ComponentDisplayType.Cross, MarkerAnchor.AboveBar, "#FF6D00", 3f, "triangle_bell", 175, PlaybackLayer.Midground,
                          "Cycle failed, price closed under the cycle low. Close {price}."),

                    Top(CompLeftTop,  "#FF5252", "Left translated top, bearish."),
                    Top(CompMidTop,   "#FFA726", "Mid translated top."),
                    Top(CompRightTop, "#66BB6A", "Right translated top, bullish."),

                    Window(CompInDclWindow, "#1800E676", BackgroundTint.PriceChart),
                    Window(CompInWclWindow, "#182979FF", BackgroundTint.Off),
                    Hidden(CompDclDay),
                    Hidden(CompWclWeek),
                    // The window bounds and the preset describe the chart, not a bar: they come
                    // from the bar clock (measured from the loaded bars — whether any fall on a
                    // weekend) and the symbol. Fewer than seven days of a crypto chart read as a
                    // stock's until a weekend arrives, so their value on bar 0 can change as later
                    // bars load. No tier can run on that little history, so nothing is decided on
                    // them; they are declared Lookahead so no strategy leaf is offered either.
                    Hidden(CompDclWindowStart, ComponentCausality.Lookahead),
                    Hidden(CompDclWindowEnd, ComponentCausality.Lookahead),
                    Hidden(CompWclWindowStart, ComponentCausality.Lookahead),
                    Hidden(CompWclWindowEnd, ComponentCausality.Lookahead),
                    Hidden(CompConfluence),
                    Hidden(CompPreset, ComponentCausality.Lookahead),
                    // How far through its arc each bar is, 0 at a low and 1 at the crest: what the arc says.
                    Hidden(CompArcPosition, ComponentCausality.Lookahead),

                    // On the price pane, in price, under the candles, as CF draws them (mustard, thin).
                    new() { Name = CompArc, DisplayName = CompArc, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                            Causality = ComponentCausality.Lookahead,
                            DefaultColorHex = ArcHex, DefaultThickness = 1f, DefaultUsePolarityColoring = false, DefaultIsAreaFill = false,
                            // A picture of where in the cycle each bar sits; its pitch would repeat the counts.
                            DefaultIsMuted = true, DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background },
                },
            }
        };

        // ── Calculation ────────────────────────────────────────────────────────

        /// <summary>Everything one run decides, by component name. Public for the tests and the lab.</summary>
        public static Dictionary<string, double[]> Compute(ReadOnlySpan<Ohlcv> data, Dictionary<string, object>? parameters)
        {
            int n = data.Length;
            var o = new Dictionary<string, double[]>();
            foreach (var c in new[] { CompSma, CompYcl, CompWcl, CompDcl, CompDclCount, CompWclCount, CompDclConfirmed, CompWclConfirmed,
                                      CompYclConfirmed, CompPending, CompCancelled, CompUpdated, CompFailed, CompLeftTop, CompMidTop,
                                      CompRightTop, CompArc, CompInDclWindow, CompInWclWindow, CompDclDay, CompWclWeek,
                                      CompDclWindowStart, CompDclWindowEnd, CompWclWindowStart, CompWclWindowEnd, CompConfluence, CompPreset,
                                      CompArcPosition })
                o[c] = CipherCEngine.Nan(n);
            if (n == 0) return o;

            var p = parameters ?? new Dictionary<string, object>();
            var clock = CipherCEngine.MeasureBars(data);
            var (preset, fineTuned) = CipherCEngine.Resolve(Str(p, ParamPreset), Str(p, "__symbol"), clock.TradesEveryDay);
            double ob = Dbl(p, ParamOverbought, DefaultOverbought), os = Dbl(p, ParamOversold, DefaultOversold);

            // The preset's days are converted to the chart's own: a trading-day range on a chart
            // that trades every day grows by 7/5, and the other way shrinks.
            double dayScale = (clock.TradesEveryDay ? 7.0 : 5.0) / (preset.CalendarDays ? 7.0 : 5.0);
            double dMin = Override(p, ParamDailyMin, preset.DailyMin * dayScale), dMax = Override(p, ParamDailyMax, preset.DailyMax * dayScale);
            double wMin = Override(p, ParamWeeklyMin, preset.WeeklyMin), wMax = Override(p, ParamWeeklyMax, preset.WeeklyMax);
            double yMin = Override(p, ParamYearlyMin, preset.YearlyMin), yMax = Override(p, ParamYearlyMax, preset.YearlyMax);

            var dTier = CipherCEngine.Tier(dMin, dMax, 1, clock);
            var wTier = CipherCEngine.Tier(wMin * clock.DaysPerWeek, wMax * clock.DaysPerWeek, clock.DaysPerWeek, clock);
            var yTier = CipherCEngine.Tier(yMin * clock.DaysPerYear, yMax * clock.DaysPerYear, clock.DaysPerYear, clock);
            var d = CipherCEngine.Detect(data, dTier, ob, os);
            var w = CipherCEngine.Detect(data, wTier, ob, os);
            var y = CipherCEngine.Detect(data, yTier, ob, os);

            var close = new double[n]; var high = new double[n]; var low = new double[n];
            for (int i = 0; i < n; i++) { close[i] = data[i].Close; high[i] = data[i].High; low[i] = data[i].Low; }

            // The moving average.
            int smaLen = Math.Max(2, Int(p, ParamSma, DefaultSma));
            var sma = o[CompSma];
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += close[i];
                if (i >= smaLen) sum -= close[i - smaLen];
                if (i >= smaLen - 1) sma[i] = sum / smaLen;
            }

            // Where the lows are. A weekly low is one of the daily lows near it, a yearly low one
            // of the weekly (or daily) ones — CF's stacks always sit on one another.
            var dLows = new List<int>(d.Lows);
            var wLows = dTier.Active ? CipherCEngine.SnapTo(w.Lows, dLows, (int)Math.Round(dTier.MinBars / 2), data) : new List<int>(w.Lows);
            var yLows = wTier.Active ? CipherCEngine.SnapTo(y.Lows, wLows, (int)Math.Round(wTier.MinBars / 2), data)
                      : dTier.Active ? CipherCEngine.SnapTo(y.Lows, dLows, (int)Math.Round(dTier.MinBars / 2), data) : y.Lows;
            // And a higher low with no lower one near it is a lower low as well: a weekly cycle
            // low IS a daily cycle low. The daily detector misses one when it comes inside the
            // minimum gap and only its wick undercuts the last low, so nothing cancelled that one
            // — Bitcoin's 5 August 2024 crash (low 49,577, close near 54,000, 31 days after the
            // 5 July low). CF draws a D under every W; so does this.
            if (wTier.Active) foreach (int i in yLows) AddLow(wLows, i);
            if (dTier.Active) { foreach (int i in wLows) AddLow(dLows, i); foreach (int i in yLows) AddLow(dLows, i); }
            foreach (int i in dLows) o[CompDcl][i] = low[i];
            foreach (int i in wLows) o[CompWcl][i] = low[i];
            foreach (int i in yLows) o[CompYcl][i] = low[i];

            // CF's count under each low: the cycle that ended there, in days, counting both ends.
            for (int k = 1; k < dLows.Count; k++)
                o[CompDclCount][dLows[k]] = Math.Round((dLows[k] - dLows[k - 1]) / clock.BarsPerDay) + 1;
            double barsPerWeek = clock.BarsPerDay * clock.DaysPerWeek;
            for (int k = 1; k < wLows.Count; k++)
                o[CompWclCount][wLows[k]] = Math.Round((wLows[k] - wLows[k - 1]) / barsPerWeek);

            Copy(d.Confirmed, o[CompDclConfirmed]);
            Copy(w.Confirmed, o[CompWclConfirmed]);
            Copy(y.Confirmed, o[CompYclConfirmed]);

            // The events, the arcs, the tops and the confluence belong to the shortest tier that runs.
            var (lowest, lowestTier, lowestLows) = dTier.Active ? (d, dTier, dLows) : wTier.Active ? (w, wTier, wLows) : (y, yTier, yLows);
            Copy(lowest.Pending, o[CompPending]);
            Copy(lowest.Cancelled, o[CompCancelled]);
            Copy(lowest.Updated, o[CompUpdated]);
            Copy(lowest.Failed, o[CompFailed]);

            var arcPos = o[CompArcPosition];
            var floor = ArcFloors(lowestLows, high, low, n, out double hump);
            for (int k = 1; k < lowestLows.Count; k++)
            {
                int a = lowestLows[k - 1], b = lowestLows[k];
                for (int i = a; i <= b; i++) arcPos[i] = Math.Sin(Math.PI * (i - a) / (b - a));
                DrawArc(o[CompArc], a, b, b - a, floor[k - 1], floor[k], hump);
                int top = a;
                for (int i = a + 1; i < b; i++) if (high[i] > high[top]) top = i;
                if (top == a) continue;
                double ratio = (double)(top - a) / (b - a);
                string comp = ratio < 0.45 ? CompLeftTop : ratio > 0.55 ? CompRightTop : CompMidTop;
                o[comp][top] = high[top];
            }
            if (lowestLows.Count > 0)
            {
                // The live arc ends in the middle of the window the next low is due in, as CF's does.
                int a = lowestLows[^1], due = Math.Max(1, (int)Math.Round(lowestTier.MeanBars));
                for (int i = a; i < n && i <= a + due; i++) arcPos[i] = Math.Sin(Math.PI * (i - a) / due);
                DrawArc(o[CompArc], a, Math.Min(n - 1, a + due), due, floor[^1], floor[^1], hump);
            }

            // Confluence on each confirming bar: was the cycle that ended a normal length, is the
            // close above the moving average, and has it closed above the low bar's high.
            for (int t = 0; t < n; t++)
            {
                if (double.IsNaN(lowest.Confirmed[t])) continue;
                int c = FindLow(lowest, t);
                if (c < 0) continue;
                int prev = PreviousLow(lowest, t, c);
                int score = 0;
                if (prev >= 0 && c - prev >= lowestTier.MinBars - 0.5 && c - prev <= lowestTier.MaxBars + 0.5) score++;
                if (!double.IsNaN(sma[t]) && close[t] > sma[t]) score++;
                if (close[t] > high[c]) score++;
                o[CompConfluence][t] = Math.Max(1, score);
            }

            // Day and week counts, and whether the bar is in the timing window — known on the bar.
            WriteCounts(d, dTier, clock.BarsPerDay, dMin, dMax, o[CompDclDay], o[CompInDclWindow], o[CompDclWindowStart], o[CompDclWindowEnd], n);
            WriteCounts(w, wTier, barsPerWeek, wMin, wMax, o[CompWclWeek], o[CompInWclWindow], o[CompWclWindowStart], o[CompWclWindowEnd], n);

            int presetIndex = IndexOf(preset);
            Array.Fill(o[CompPreset], fineTuned ? presetIndex : -1 - presetIndex);
            return o;
        }

        private static void AddLow(List<int> lows, int i)
        {
            int at = lows.BinarySearch(i);
            if (at < 0) lows.Insert(~at, i);
        }

        private static void WriteCounts(CipherCEngine.TierRun run, CipherCEngine.TierBars tier, double barsPerUnit,
            double min, double max, double[] count, double[] inWindow, double[] start, double[] end, int n)
        {
            if (!tier.Active) return;
            Array.Fill(start, Math.Round(min, 1));
            Array.Fill(end, Math.Round(max, 1));
            for (int t = 0; t < n; t++)
            {
                int last = run.LastLowAt[t];
                if (last < 0) continue;
                double units = Math.Floor((t - last) / barsPerUnit + 1e-9) + 1;
                count[t] = units;
                inWindow[t] = units >= min && units <= max ? 1 : 0;
            }
        }

        /// <summary>The low a confirmation on bar <paramref name="t"/> confirmed: the one LastLowAt names from then on.</summary>
        private static int FindLow(CipherCEngine.TierRun run, int t) => run.LastLowAt[t];

        /// <summary>The low before <paramref name="c"/> as it stood on the bar before the confirmation.</summary>
        private static int PreviousLow(CipherCEngine.TierRun run, int t, int c)
        {
            int before = t > 0 ? run.LastLowAt[t - 1] : -1;
            return before >= 0 && before < c ? before : -1;
        }

        private static int IndexOf(CyclePreset preset)
        {
            for (int i = 0; i < CipherCEngine.Presets.Count; i++) if (CipherCEngine.Presets[i].Key == preset.Key) return i;
            return 0;
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals(Code, StringComparison.OrdinalIgnoreCase)) return;
            foreach (var (name, values) in Compute(data, parameters))
            {
                var span = buffer.GetComponentSpan(name);
                int len = Math.Min(span.Length, values.Length);
                for (int i = 0; i < len; i++) span[i] = values[i];
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        /// <summary>
        /// The daily detector's settling time on a daily chart with the widest preset (Bitcoin's 66
        /// days): its slow average is a cycle long and it starts after two of them.
        /// </summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 132;

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        // ── Speech ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The cycle's state on a bar in words — where the daily and weekly cycles are against
        /// their timing windows, and a confirmation's confluence. Read by Alt+Shift+D after the
        /// drawn components (<see cref="Accessibility.BarDetailService"/>) and by the detail fact.
        /// </summary>
        public static IReadOnlyList<string> StateWords(Func<string, double[]?> component, int index)
        {
            var words = new List<string>();
            double At(string name) => component(name) is { } a && index >= 0 && index < a.Length ? a[index] : double.NaN;

            string? daily = WindowWords("daily cycle", "day", "days", At(CompDclDay), At(CompDclWindowStart), At(CompDclWindowEnd));
            if (daily != null) words.Add(daily);
            string? weekly = WindowWords("weekly cycle", "week", "weeks", At(CompWclWeek), At(CompWclWindowStart), At(CompWclWindowEnd));
            if (weekly != null) words.Add(weekly);

            double conf = At(CompConfluence);
            if (!double.IsNaN(conf))
                words.Add($"cycle low confirmed, {ConfluenceWord(conf)} confluence");
            if (!double.IsNaN(At(CompCancelled))) words.Add("cycle low cancelled");
            if (!double.IsNaN(At(CompFailed))) words.Add("cycle failed");
            return words;
        }

        internal static string? WindowWords(string cycle, string unit, string units, double count, double start, double end)
        {
            if (double.IsNaN(start)) return null;          // the tier does not run on this chart
            if (double.IsNaN(count)) return $"no {cycle} low confirmed yet";
            string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            string Units(double v) => Math.Abs(v - 1) < 1e-9 ? unit : units;
            string head = $"{cycle} {unit} {Num(count)}";
            if (count < start)
            {
                double inN = Math.Ceiling(start - count);
                return $"{head}, its window opens in {Num(inN)} {Units(inN)}";
            }
            if (count <= end)
            {
                double left = Math.Floor(end - count);
                return left < 1 ? $"{head}, the last {unit} of its window" : $"{head}, in its window, {Num(left)} {Units(left)} left";
            }
            double past = Math.Ceiling(count - end);
            return $"{head}, {Num(past)} {Units(past)} past its window";
        }

        public static string ConfluenceWord(double score) => score >= 3 ? "high" : score >= 2 ? "mid" : "low";

        /// <summary>The preset a chart used, in words, from the value of <see cref="CompPreset"/>.</summary>
        public static string PresetWords(double value, DateTime barDate)
        {
            if (double.IsNaN(value)) return "no preset";
            bool fineTuned = value >= 0;
            int index = fineTuned ? (int)value : -1 - (int)value;
            if (index < 0 || index >= CipherCEngine.Presets.Count) return "no preset";
            var preset = CipherCEngine.Presets[index];
            string text = fineTuned
                ? $"{preset.Name} ranges, {preset.Source}"
                : $"not one of CF's sixteen assets, so {preset.Name}'s ranges";
            text += $": daily {Num(preset.DailyMin)} to {Num(preset.DailyMax)} {(preset.CalendarDays ? "days" : "trading days")}, " +
                    $"weekly {Num(preset.WeeklyMin)} to {Num(preset.WeeklyMax)} weeks, yearly {Num(preset.YearlyMin)} to {Num(preset.YearlyMax)} years";
            if (preset.Key == "BTC" && fineTuned)
            {
                var halving = CipherCEngine.HalvingPhase(barDate);
                if (halving != null) text += $". {halving}";
            }
            return text;
        }

        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            double At(string name) => allComponentData.TryGetValue(name, out var a) && dataIndex >= 0 && dataIndex < a.Length ? a[dataIndex] : double.NaN;
            return componentName switch
            {
                CompDclCount => $"{Num(value)} day cycle",
                CompWclCount => $"{Num(value)} week cycle",
                CompConfluence => $"{ConfluenceWord(value)} confluence",
                CompDclDay => WindowWords("daily cycle", "day", "days", value, At(CompDclWindowStart), At(CompDclWindowEnd)),
                CompWclWeek => WindowWords("weekly cycle", "week", "weeks", value, At(CompWclWindowStart), At(CompWclWindowEnd)),
                CompInDclWindow => value >= 0.5 ? "in the daily timing window" : "outside the daily timing window",
                CompInWclWindow => value >= 0.5 ? "in the weekly timing window" : "outside the weekly timing window",
                CompArc or CompArcPosition => At(CompArcPosition) is var pos && double.IsNaN(pos) ? null
                    : pos >= 0.995 ? "mid-cycle" : $"{Math.Round(pos * 100)} percent of the arc",
                CompPreset => PresetWords(value, bar.Date),
                _ => null,
            };
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results,
            int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals(Code, StringComparison.OrdinalIgnoreCase) || index < 0) return string.Empty;
            var words = new List<string>(StateWords(name => results.TryGetValue(name, out var a) ? a : null, index));
            if (results.TryGetValue(CompPreset, out var preset) && index < preset.Length && index < data.Length)
                words.Add(PresetWords(preset[index], data[index].Date));
            if (words.Count == 0) return "Calculating...";
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..] + "."));
        }

        // ── Parameters ─────────────────────────────────────────────────────────

        /// <summary>
        /// Where CF's arcs sit: a level wave under the candles, every hump the same height, its troughs
        /// under the cycle lows' marks (the vendor's demo frames). The hump is a quarter of the median
        /// cycle's swing (log of its high over its low), so it reads the same on any asset and timeframe
        /// and does not squeeze the candles when the price pane fits it in. Each low's floor is one and a
        /// quarter humps under the lowest low of the two cycles it joins, so every crest stays a quarter
        /// hump under the candles. Returned in log price, one per low.
        /// </summary>
        private static double[] ArcFloors(List<int> lows, double[] high, double[] low, int n, out double hump)
        {
            var swings = new List<double>();
            for (int k = 1; k < lows.Count; k++)
            {
                double hi = 0, lo = double.MaxValue;
                for (int i = lows[k - 1]; i <= lows[k]; i++) { hi = Math.Max(hi, high[i]); lo = Math.Min(lo, low[i]); }
                if (lo > 0) swings.Add(Math.Log(hi / lo));
            }
            swings.Sort();
            hump = swings.Count > 0 ? 0.25 * swings[swings.Count / 2] : 0.05;
            var floor = new double[lows.Count];
            for (int k = 0; k < lows.Count; k++)
            {
                int from = k > 0 ? lows[k - 1] : lows[k], to = k + 1 < lows.Count ? lows[k + 1] : n - 1;
                double lo = double.MaxValue;
                for (int i = from; i <= to; i++) if (low[i] > 0) lo = Math.Min(lo, low[i]);
                floor[k] = Math.Log(lo) - 1.25 * hump;
            }
            return floor;
        }

        /// <summary>
        /// One of CF's arcs: from the floor at low <paramref name="a"/> to the floor at the far end,
        /// <paramref name="span"/> bars later, rising a hump in the middle; rounded at both ends, as CF
        /// draws them. The floor eases from one level to the next and is flat at each low, so the
        /// trough stays on the low's bar. Drawn up to bar <paramref name="last"/>; the live arc's far end is in the future.
        /// </summary>
        private static void DrawArc(double[] arc, int a, int last, int span, double fa, double fb, double hump)
        {
            if (span <= 0) return;
            for (int i = a; i <= last; i++)
            {
                double t = Math.Min(1, (double)(i - a) / span);
                double level = fa + (fb - fa) * t * t * t * (10 - 15 * t + 6 * t * t);
                arc[i] = Math.Exp(level + hump * 0.5 * (1 - Math.Cos(2 * Math.PI * t)));
            }
        }

        private static void Copy(double[] from, double[] to) => Array.Copy(from, to, Math.Min(from.Length, to.Length));

        private static string? Str(Dictionary<string, object> p, string k) => p.TryGetValue(k, out var v) ? v?.ToString() : null;

        private static double Dbl(Dictionary<string, object> p, string k, double def)
        {
            if (!p.TryGetValue(k, out var v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return def; }
        }

        private static int Int(Dictionary<string, object> p, string k, int def) => (int)Math.Round(Dbl(p, k, def));

        /// <summary>A range setting: 0 (or nonsense) keeps the preset's value.</summary>
        private static double Override(Dictionary<string, object> p, string k, double preset)
        {
            double v = Dbl(p, k, 0);
            return v > 0 && !double.IsNaN(v) ? v : preset;
        }
    }
}
