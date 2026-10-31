using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Cipher A — a rebuild of Market Cipher A (CryptoFace, invite-only on TradingView), MEASURED
    /// against real Market Cipher A charts on 2026-10-17. Everything here is the chart's own
    /// candles: no other symbol, no volume.
    ///
    /// <para><b>How it was measured.</b> Seven TradingView chart snapshots that show Market Cipher A
    /// were digitised column by column against the same bars: three with the legend
    /// "MarketCipher A (2.1.2, 1, 21)" (BTCUSD 1D Bitstamp Heikin-Ashi, and two snapshots of DPRUSDT
    /// 1D Gate.io Heikin-Ashi), a BTCUSD 2D chart whose eight ribbon lines are 23 px apart per bar,
    /// a second BTCUSD 1D Heikin-Ashi chart, and — never used for fitting — a BTCUSDT 1D Binance
    /// chart, a Bitget BTC 3h Heikin-Ashi chart and an SLPUSDT 1D Binance chart. Scripts, images
    /// and numbers: the 2026-10-17 Cipher A report.</para>
    ///
    /// <para><b>The ribbon</b> is eight EMAs of the close, 5 11 15 18 21 25 29 33 — OpenCipher A's
    /// set, not VuManChu's (…24 28 34). On the 2D chart every line fits at 0.3–0.7 px RMSE and the
    /// neighbouring lengths are 4–8 px off; on the hold-out DPR chart the set fits again. Each line
    /// has its own colour: blue/white while it is above the next-longer line (the last line: while
    /// it is below the one before), grey otherwise — 1,410 of 1,410 digitised line-segments on three
    /// charts. MCA does not tint the background; the lines themselves change colour, which is
    /// drawn here with <c>DefaultColorAboveComponent</c>.</para>
    ///
    /// <para><b>The symbols</b> (exact bar / within one bar, fit charts then hold-outs):</para>
    /// <list type="bullet">
    /// <item><b>Green Dot</b> — EMA 11 crosses above EMA 33 on a Yellow Diamond candle.</item>
    /// <item><b>Blue Triangle</b> — EMA 5 crosses above EMA 25 with the fast wave above the slow
    /// one and EMA 11 still below EMA 33.</item>
    /// <item><b>Yellow Diamond</b> — Wave Trend fast above slow while EMA 5 is above both EMA 11 and
    /// EMA 33, and the fast wave more than 1.25 above the slow one or Cipher B's money flow above 5.</item>
    /// <item><b>Red X</b> — EMA 5 crosses below EMA 11.</item>
    /// <item><b>Red Diamond</b> — Wave Trend fast crosses below slow (hlc3, 9, 12, SMA 3: Cipher B's
    /// wave), on every candle, a Red X candle included.</item>
    /// <item><b>Blood Diamond</b> — a Red Diamond and a Red X on the same candle.</item>
    /// <item><b>Yellow X</b> — a Red Diamond with the slow wave at −40 or lower and Cipher B's
    /// "Stochastic" line (the 81-bar stochastic of the close, SMA 2) below 20.</item>
    /// </list>
    /// <para>Checked 2026-10-23 against Market Cipher A 2.5.0's own plotted values on BTCUSD 1D
    /// (Bitstamp, 2011–2026, candles and Heikin-Ashi): the eight ribbon lines are exact, and every
    /// symbol but the Yellow Diamond fires on exactly its bars, none missing and none extra (Green
    /// Dot 60/60, Blue Triangle 88/88, Red X 185/185, Red Diamond 541/541, Blood Diamond 29/29,
    /// Yellow X 26/26 on candles; the same on Heikin-Ashi). The Yellow Diamond also needs the fast wave
    /// more than 1.25 above the slow one OR Cipher B's money flow above 5: with Market Cipher B's own
    /// money flow that is exact on both charts (1,312/1,312 and 1,322/1,322, none extra; it was 34 and
    /// 39 extra), the bounds fitted to (1.22, 1.28] and [4.97, 5.01) on the two. Since 2026-10-24 this
    /// app's money flow is Market Cipher B's exactly, and the Yellow Diamond is exact too: 0 bars differ
    /// on either chart (4 and 3 with the fitted money flow before). Held out: Market Cipher A on DJI 1D
    /// (300 bars, never fitted) went from 9 extra to exact, and stays exact.
    /// The earlier rules (Green Dot without the Yellow Diamond gate, Blue Triangle on EMA 29 below
    /// EMA 33, Red Diamond suppressed on Red X candles, Yellow X on an 80-bar stochastic of hlc3
    /// below 22) came from digitised pictures and each missed or added symbols.</para>
    ///
    /// <para><b>Saved charts and strategies.</b> The code stays <c>CIPHER_A</c>. The components of the
    /// all-WaveTrend Cipher A this replaced (Buy/Sell Signal, the divergences, Manipulation,
    /// Exhaustion, WT Momentum) are gone: a saved chart rebuilds its components from this metadata,
    /// and a strategy or alert naming one is told why (<c>SignalCatalog.Retired</c>).</para>
    /// </summary>
    public class CipherAProvider : IIndicatorProvider
    {
        public string Name => "Cipher A";

        public const string CompGreenDot      = "Green Dot";
        public const string CompBlueTriangle  = "Blue Triangle";
        public const string CompYellowDiamond = "Yellow Diamond";
        private const double YellowDiamondWaveGap = 1.25, YellowDiamondMoneyFlow = 5.0;
        private const int YellowDiamondMoneyFlowPeriod = 60;
        public const string CompRedX          = "Red X";
        public const string CompRedDiamond    = "Red Diamond";
        public const string CompBloodDiamond  = "Blood Diamond";
        public const string CompYellowX       = "Yellow X";

        /// <summary>The ribbon's lengths, shortest first. Measured, not a setting.</summary>
        public static readonly int[] RibbonLengths = { 5, 11, 15, 18, 21, 25, 29, 33 };
        public static string RibbonLine(int k) => $"EMA {RibbonLengths[k]}";

        // Companion arrays (not components, so not keyboard stops): read by the speech and the bar detail.
        public const string Wt1Key   = "Cipher A_wt1";
        public const string Wt2Key   = "Cipher A_wt2";
        public const string StochKey = "Cipher A_stoch80";

        public const string CompRibbon9   = "Ribbon 9";
        public const string CompPivotHigh = "Pivot High";
        public const string CompPivotLow  = "Pivot Low";

        // Colours, widths and sizes are Market Cipher A 2.5.0's own, read from the study's style
        // table on TradingView (2026-10-23), alpha included. Eight-digit hexes are #AARRGGBB.
        /// <summary>The uptrend ladder, darkest (EMA 5) to near-white (EMA 33). Lines 5 and 6 share a colour
        /// in the original. (The 2.1.2 snapshots digitised on 2026-10-17 were a few shades off these.)</summary>
        private static readonly string[] BlueLadder = { "#0066CC", "#1A8CFF", "#4DA6FF", "#80BFFF", "#99CCFF", "#99CCFF", "#CCE6FF", "#E6F2FF" };
        /// <summary>The downtrend grey, rgb(80, 86, 98), the same for every line.</summary>
        private const string Grey = "#505662";
        /// <summary>Market Cipher A's "tiny" and "small" shape sizes.</summary>
        private const float Tiny = 8f, Small = 12f;

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "CIPHER_A",
                Causality   = ComponentCausality.Causal,
                Name        = "Cipher A",
                Category    = "Multi-Signal",
                DefaultPane = "Main",
                Description =
                    "Market Cipher A, measured against real Market Cipher A charts: an eight-EMA ribbon " +
                    "(5 11 15 18 21 25 29 33) whose lines are blue while each is above the next and grey " +
                    "when not, and seven symbols — Green Dot, Blue Triangle, Yellow Diamond (bullish) and " +
                    "Red X, Red Diamond, Blood Diamond, Yellow X (bearish). Also, off until included: a ninth " +
                    "line (a 21-bar simple average) and pivot high and low lines. Computed from the chart's own " +
                    "candles only.",
                Components = BuildComponents(),
                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "WT1Period", DisplayName = "Wave Trend Channel Period", DataType = typeof(int), DefaultValue = 9.0,
                            MinValue = 2, MaxValue = 50, Step = 1,
                            Description = "Channel period of the Wave Trend behind Red Diamond, Blood Diamond, Yellow Diamond and Yellow X. 9 by default." },
                    new() { Name = "WT2Period", DisplayName = "Wave Trend Average Period", DataType = typeof(int), DefaultValue = 12.0,
                            MinValue = 2, MaxValue = 50, Step = 1,
                            Description = "Average period of the Wave Trend. 12 by default." },
                    new() { Name = "WTSignalPeriod", DisplayName = "Wave Trend Slow Wave Smoothing", DataType = typeof(int), DefaultValue = 3.0,
                            MinValue = 1, MaxValue = 10, Step = 1,
                            Description = "Bars in the simple average that makes the slow wave from the fast one. 3 by default." },
                    new() { Name = "YellowXWaveLevel", DisplayName = "Yellow X Wave Level", DataType = typeof(double), DefaultValue = -40.0,
                            MinValue = -100, MaxValue = 0, Step = 1,
                            Description = "A Red Diamond is a Yellow X only with the slow wave at or below this level." },
                    new() { Name = "YellowXRangeLevel", DisplayName = "Yellow X Range Level (%)", DataType = typeof(double), DefaultValue = 20.0,
                            MinValue = 1, MaxValue = 100, Step = 1,
                            Description = "…and with Cipher B's Stochastic line (the 81-bar stochastic of the close, smoothed over 2) below this." },
                    new() { Name = "Ribbon9Length", DisplayName = "Ribbon 9 Length", DataType = typeof(int), DefaultValue = 21.0,
                            MinValue = 2, MaxValue = 400, Step = 1,
                            Description = "Bars in Ribbon 9, a simple average of the close. 21 by default." },
                    new() { Name = "PivotDaysBack", DisplayName = "Pivot Bars Back", DataType = typeof(int), DefaultValue = 1.0,
                            MinValue = 1, MaxValue = 50, Step = 1,
                            Description = "Pivot High is the highest high of this many bars, Pivot Low the lowest low; on an intraday chart, of this many trading days, today's high so far included." },
                },
                // Market Cipher A shades between the first and last ribbon lines and, more strongly, between
                // the first two. Visual only: the lines themselves are already navigable.
                DefaultCloudFills = new List<CloudFillConfig>
                {
                    new() { UpperComponentName = RibbonLine(0), LowerComponentName = RibbonLine(RibbonLengths.Length - 1),
                            BullishColorHex = "#1AB3D9FF", BearishColorHex = "#1AB3D9FF", DisplayName = "Ribbon Fill" },
                    new() { UpperComponentName = RibbonLine(0), LowerComponentName = RibbonLine(1),
                            BullishColorHex = "#2680B3FF", BearishColorHex = "#2680B3FF", DisplayName = "Fast Ribbon Fill" },
                },
            }
        };

        private static List<IndicatorComponentMetadata> BuildComponents()
        {
            var list = new List<IndicatorComponentMetadata>
            {
                // ── Symbols. Bullish first, then bearish; each a marker that pings. ─────────────
                Marker(CompGreenDot,      ComponentDisplayType.Dot,       "#00E676",   Tiny,  "sine_bell",         880.0, 380, "Green dot, bullish"),
                Marker(CompBlueTriangle,  ComponentDisplayType.TriangleUp,"#4259F4",   Small, "crystal_bell",      660.0, 320, "Blue triangle, momentum turning"),
                // Fires on many bars in a row in a trend, so it is the quietest: short and in the background.
                // Drawn at 30% opacity, as the original, for the same reason.
                Marker(CompYellowDiamond, ComponentDisplayType.Diamond,   "#4DFDD835", Tiny,  "triangle_bell",     1046.0, 90, "Yellow diamond", PlaybackLayer.Background),
                Marker(CompRedX,          ComponentDisplayType.Cross,     "#F23645",   Tiny,  "detuned_pair_bell", 250.0, 320, "Red X, bearish"),
                Marker(CompRedDiamond,    ComponentDisplayType.Diamond,   "#8CF23645", Tiny,  "triangle_bell",     330.0, 200, "Red diamond"),
                Marker(CompBloodDiamond,  ComponentDisplayType.Diamond,   "#F23645",   Small, "dual_tone_bell",    165.0, 500, "Blood diamond, strongly bearish"),
                Marker(CompYellowX,       ComponentDisplayType.Cross,     "#FDD835",   Small, "detuned_pair_bell", 550.0, 450, "Yellow X, bearish warning"),
            };

            // ── The ribbon. Muted: eight voices within a few percent of price would bury the
            // candles and the symbols. Each is navigable and says its value and its colour.
            for (int k = 0; k < RibbonLengths.Length; k++)
            {
                bool last = k == RibbonLengths.Length - 1;
                list.Add(new IndicatorComponentMetadata
                {
                    Name = RibbonLine(k),
                    DisplayType = ComponentDisplayType.Line,
                    Role = ComponentRole.Signal,
                    // Coloured against a sibling: the secondary colour where this line is ABOVE the
                    // sibling, the primary at or below. Lines 1–7 are blue above the next-longer line;
                    // the last line is blue BELOW the one before it, so its two colours swap.
                    DefaultColorAboveComponent = last ? RibbonLine(k - 1) : RibbonLine(k + 1),
                    DefaultColorHex          = last ? BlueLadder[k] : Grey,
                    DefaultColorHexSecondary = last ? Grey : BlueLadder[k],
                    DefaultThickness = 2.0f,
                    DefaultIsMuted = true,
                    // One band for narration: "Price entered the ribbon", not eight crosses.
                    RibbonName = "ribbon",
                    // Only EMA 5 puts its value on the price axis (Cody, 2026-10-18). Eight labels
                    // within a few percent of price, with Cipher SR's levels and the close, stacked
                    // about 13 tags up the axis and pushed some ~100 px from their own lines (the
                    // 84,254 resistance tag among them). The lines are still drawn and spoken; the
                    // user can turn a label back on in Properties → Appearance.
                    DefaultShowAxisLabel = k == 0,
                    DefaultPlaybackLayer = PlaybackLayer.Background,
                    DefaultUsePolarityColoring = false,
                    // Painted under the symbols; keyboard order is unchanged.
                    DefaultDrawOrder = -1,
                });
            }

            // ── Off until the user includes them, as in the original (its Ribbon 9 and pivot
            // switches are off by default). Muted and quiet like the ribbon when included.
            list.Add(new IndicatorComponentMetadata
            {
                Name = CompRibbon9, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                DefaultColorHex = "#FDD835", DefaultThickness = 2.0f,
                DefaultIsEnabled = false, DefaultIsMuted = true, DefaultShowAxisLabel = false,
                DefaultPlaybackLayer = PlaybackLayer.Background, DefaultUsePolarityColoring = false, DefaultDrawOrder = -1,
            });
            foreach (var pivot in new[] { CompPivotHigh, CompPivotLow })
                list.Add(new IndicatorComponentMetadata
                {
                    Name = pivot, DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Signal,
                    DefaultColorHex = "#CC4DA6FF", DefaultThickness = 1.0f,
                    DefaultIsEnabled = false, DefaultIsMuted = true, DefaultShowAxisLabel = false,
                    DefaultPlaybackLayer = PlaybackLayer.Background, DefaultUsePolarityColoring = false, DefaultDrawOrder = -1,
                });
            return list;
        }

        private static IndicatorComponentMetadata Marker(string name, ComponentDisplayType shape, string color, float size,
            string patch, double freq, int decayMs, string narration, PlaybackLayer layer = PlaybackLayer.Foreground) => new()
        {
            Name = name,
            DisplayType = shape,
            Role = ComponentRole.Signal,
            DefaultColorHex = color,
            DefaultThickness = size,
            DefaultEnvelopeType = "Ping",
            DefaultSoundPatchId = patch,
            DefaultBaseFrequency = freq,
            DefaultDecayMs = decayMs,
            DefaultPitchMapping = PitchMapping.None,
            DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultPlaybackLayer = layer,
            DefaultSignalSpeechTemplate = narration,
            DefaultUsePolarityColoring = false,
        };

        /// <summary>
        /// Pivot High and Pivot Low: the highest high and lowest low of the last
        /// <paramref name="back"/> bars on a daily chart, and of the last <paramref name="back"/>
        /// trading DAYS on an intraday one, today included.
        ///
        /// <para>Checked against Market Cipher A on TradingView, 2026-10-23 (TVC:DJI, 300 bars each):
        /// 1D at 1 and 3 bars back match bar for bar; on 1 hour at 1 and 2 days back every bar
        /// shows the high and low of the 1D bars of its day and the day(s) before it, 300/300. A
        /// Monday at 2 pairs with the Friday, so the count is in trading days, not calendar days.</para>
        ///
        /// <para><b>Deliberately different on intraday history:</b> the original shows the WHOLE
        /// day's high on every bar of that day, so a 10:00 bar already carries the high made at
        /// 15:00. Here today's part is the high SO FAR, up to and including the bar. The two agree
        /// on each day's last bar and on the live bar; earlier in a past day this one is lower (or
        /// the low higher). Same choice as Cipher SR's range (Cody: keep it running).</para>
        ///
        /// <para>Days are UTC dates, as Cipher SR's range and VWAP. A session that spans midnight
        /// UTC (futures, Asian markets) splits in two here; not checked against the original.
        /// Weekly and longer charts use bars, also not checked.</para>
        /// </summary>
        private static (double[] High, double[] Low) Pivots(ReadOnlySpan<Ohlcv> data, double[] high, double[] low, int back)
        {
            int n = data.Length;
            var pivHigh = Nan(n); var pivLow = Nan(n);
            var days = new List<(long Day, double High, double Low)>();
            for (int i = 0; i < n; i++)
            {
                long day = (long)Math.Floor(CipherSrProvider.UnixSeconds(data[i].Date) / 86400.0);
                if (days.Count == 0 || days[^1].Day != day) days.Add((day, high[i], low[i]));
                else days[^1] = (day, Math.Max(days[^1].High, high[i]), Math.Min(days[^1].Low, low[i]));

                double hi = double.MinValue, lo = double.MaxValue;
                if (CipherSrProvider.IsIntraday(data, i))
                {
                    if (days.Count < back) continue;
                    for (int k = days.Count - back; k < days.Count; k++) { hi = Math.Max(hi, days[k].High); lo = Math.Min(lo, days[k].Low); }
                }
                else
                {
                    if (i < back - 1) continue;
                    for (int j = i - back + 1; j <= i; j++) { hi = Math.Max(hi, high[j]); lo = Math.Min(lo, low[j]); }
                }
                pivHigh[i] = hi; pivLow[i] = lo;
            }
            return (pivHigh, pivLow);
        }

        /// <summary>Stacking slot above the high, so symbols on one candle sit one above another
        /// as Market Cipher A draws them, rather than on top of each other.</summary>
        private static int Slot(string comp) => comp switch
        {
            CompRedX => 1, CompRedDiamond => 2, CompBloodDiamond => 2, CompYellowDiamond => 3,
            CompGreenDot => 4, CompYellowX => 5, CompBlueTriangle => 6, _ => 1,
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("CIPHER_A", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 2) return;

            int wtChannel = Math.Max(1, GetInt(parameters, "WT1Period", 9));
            int wtAverage = Math.Max(1, GetInt(parameters, "WT2Period", 12));
            int wtSignal  = Math.Max(1, GetInt(parameters, "WTSignalPeriod", 3));
            double yxWave  = GetDbl(parameters, "YellowXWaveLevel", -40.0);
            double yxRange = GetDbl(parameters, "YellowXRangeLevel", 20.0);
            int ribbon9   = Math.Max(1, GetInt(parameters, "Ribbon9Length", 21));
            int pivotBack = Math.Max(1, GetInt(parameters, "PivotDaysBack", 1));

            var close = new double[n]; var open = new double[n]; var high = new double[n]; var low = new double[n]; var hlc3 = new double[n];
            for (int i = 0; i < n; i++)
            {
                close[i] = data[i].Close; open[i] = data[i].Open; high[i] = data[i].High; low[i] = data[i].Low;
                hlc3[i] = (high[i] + low[i] + close[i]) / 3.0;
            }

            var ema = new double[RibbonLengths.Length][];
            for (int k = 0; k < RibbonLengths.Length; k++) ema[k] = IndicatorMath.Ema(close, RibbonLengths[k]);
            var (wt1, wt2) = WaveTrendMath.Compute(hlc3, wtChannel, wtAverage, wtSignal);
            var stoch = WaveTrendMath.SmoothedStochastic(close, high, low, CipherBProvider.McbStochLength, CipherBProvider.McbLineSmoothing);
            var moneyFlow = CipherBProvider.McbMoneyFlow(high, low, close, YellowDiamondMoneyFlowPeriod);

            double[] e5 = ema[0], e11 = ema[1], e25 = ema[5], e33 = ema[7];
            var green = Nan(n); var blue = Nan(n); var yd = Nan(n); var redX = Nan(n); var redD = Nan(n); var blood = Nan(n); var yx = Nan(n);
            for (int i = 1; i < n; i++)
            {
                double unit = Math.Max((high[i] - low[i]) * 0.15, Math.Abs(close[i]) * 0.002);
                double At(string comp) => high[i] + unit * Slot(comp);

                bool rx = WaveTrendMath.CrossedUnder(e5, e11, i);
                bool rd = WaveTrendMath.CrossedUnder(wt1, wt2, i);
                bool upWave = wt1[i] > wt2[i];
                // A wave gap of 1.25 or less needs Cipher B's money flow above 5 (see the class notes).
                bool ydOn = upWave && e5[i] > e11[i] && e5[i] > e33[i]
                         && (wt1[i] - wt2[i] > YellowDiamondWaveGap || moneyFlow[i] > YellowDiamondMoneyFlow);
                if (WaveTrendMath.CrossedOver(e11, e33, i) && ydOn) green[i] = At(CompGreenDot);
                if (WaveTrendMath.CrossedOver(e5, e25, i) && upWave && e11[i] < e33[i]) blue[i] = At(CompBlueTriangle);
                if (ydOn) yd[i] = At(CompYellowDiamond);
                if (rx) redX[i] = At(CompRedX);
                if (rd) redD[i] = At(CompRedDiamond);   // also on a Red X candle, as Market Cipher A plots it
                if (rd && rx) blood[i] = At(CompBloodDiamond);
                if (rd && wt2[i] <= yxWave && stoch[i] < yxRange) yx[i] = At(CompYellowX);
            }

            Write(buffer, CompGreenDot, green); Write(buffer, CompBlueTriangle, blue); Write(buffer, CompYellowDiamond, yd);
            Write(buffer, CompRedX, redX); Write(buffer, CompRedDiamond, redD); Write(buffer, CompBloodDiamond, blood); Write(buffer, CompYellowX, yx);
            for (int k = 0; k < RibbonLengths.Length; k++) Write(buffer, RibbonLine(k), ema[k]);
            Write(buffer, CompRibbon9, IndicatorMath.Sma(close, ribbon9));
            var (pivHigh, pivLow) = Pivots(data, high, low, pivotBack);
            Write(buffer, CompPivotHigh, pivHigh); Write(buffer, CompPivotLow, pivLow);
            Write(buffer, Wt1Key, wt1); Write(buffer, Wt2Key, wt2); Write(buffer, StochKey, stoch);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        /// <summary>EMA 33 needs about four times its length to forget its seed; the range stochastic needs 81.</summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 140;

        // ── Speech ──────────────────────────────────────────────────────────────────────────

        /// <summary>Value-only speech when the keyboard lands on a component (NavigationFeedbackManager
        /// says the name first): what the symbol means, in words rather than the averages behind it
        /// ("short-term trend" = EMA 5 against EMA 11 or 25, "medium-term" = EMA 11 against EMA 33,
        /// "momentum" = the Wave Trend against its slow wave; Cody, 2026-10-23), and for a ribbon
        /// line its colour and why.</summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return "no data";
            string price = SpeechPriceFormatter.FormatPrice(bar.Close);
            int k = Array.FindIndex(RibbonLengths, L => componentName == $"EMA {L}");
            if (k >= 0) return RibbonLineSpeech(k, value, allComponentData, dataIndex);
            if (componentName == CompRibbon9) return $"{SpeechPriceFormatter.FormatPrice(value)}. Yellow, simple average.";
            if (componentName is CompPivotHigh or CompPivotLow) return $"{SpeechPriceFormatter.FormatPrice(value)}. Light blue.";

            return componentName switch
            {
                CompGreenDot      => $"Trend turned up: medium-term trend crossed up, with momentum and the short-term trend already up. Price {price}.",
                CompBlueTriangle  => $"Early turn up: short-term trend crossed up with momentum rising, longer trend still down. Price {price}.",
                CompYellowDiamond => $"Bullish conditions: momentum and the short-term trend both up. Price {price}.",
                CompRedX          => $"Short-term trend turned down. Price {price}.",
                CompRedDiamond    => $"Momentum turned down. Price {price}.",
                CompBloodDiamond  => $"Momentum and the short-term trend turned down together: the strongest sell warning. Price {price}.",
                CompYellowX       => $"Momentum turned down again while already oversold: often a trap; several in a row can mean selling is running out. Price {price}.",
                _ => null,
            };
        }

        private static string RibbonLineSpeech(int k, double value, IReadOnlyDictionary<string, double[]> data, int idx)
        {
            bool last = k == RibbonLengths.Length - 1;
            int sib = last ? k - 1 : k + 1;
            double other = Get(data, RibbonLine(sib), idx);
            string v = SpeechPriceFormatter.FormatPrice(value);
            if (double.IsNaN(other)) return v + ".";
            bool bull = last ? value < other : value > other;
            string rel = last ? (bull ? "below" : "above") : (bull ? "above" : "below");
            return $"{v}. {(bull ? "Blue, bullish" : "Grey, bearish")}: {rel} {RibbonLine(sib)}.";
        }

        /// <summary>The bar detail (Alt+Shift+D and the context summary): the ribbon's colour and any symbol on the bar.</summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_A", StringComparison.OrdinalIgnoreCase) || index < 0) return string.Empty;
            var sb = new StringBuilder();
            int blueLines = 0, known = 0;
            for (int k = 0; k < RibbonLengths.Length; k++)
            {
                bool last = k == RibbonLengths.Length - 1;
                double v = Get(results, RibbonLine(k), index), o = Get(results, RibbonLine(last ? k - 1 : k + 1), index);
                if (double.IsNaN(v) || double.IsNaN(o)) continue;
                known++;
                if (last ? v < o : v > o) blueLines++;
            }
            if (known == RibbonLengths.Length)
                sb.Append(blueLines == known ? "Ribbon blue, all eight lines bullish. "
                        : blueLines == 0 ? "Ribbon grey, all eight lines bearish. "
                        : $"Ribbon mixed, {blueLines} of 8 lines blue. ");

            var on = new List<string>();
            foreach (var c in new[] { CompBloodDiamond, CompYellowX, CompRedX, CompRedDiamond, CompGreenDot, CompBlueTriangle, CompYellowDiamond })
            {
                if (double.IsNaN(Get(results, c, index))) continue;
                if (c == CompRedX || c == CompRedDiamond) { if (!double.IsNaN(Get(results, CompBloodDiamond, index))) continue; }
                on.Add(c);
            }
            if (on.Count > 0) sb.Append(string.Join(", ", on) + ".");
            return sb.ToString().TrimEnd();
        }

        public List<LevelDescriptor> GetDefaultLevels(string code) => new();

        // ── helpers ─────────────────────────────────────────────────────────────────────────

        private static double[] Nan(int n) { var a = new double[n]; Array.Fill(a, double.NaN); return a; }

        private static void Write(IIndicatorResultBuffer buffer, string name, double[] data)
        {
            var span = buffer.GetComponentSpan(name);
            int len = Math.Min(span.Length, data.Length);
            for (int i = 0; i < len; i++) span[i] = data[i];
        }

        private static double Get(IReadOnlyDictionary<string, double[]> r, string key, int idx) =>
            r.TryGetValue(key, out var arr) && arr != null && idx >= 0 && idx < arr.Length ? arr[idx] : double.NaN;

        private static int    GetInt(Dictionary<string, object> p, string k, int    def) => p.TryGetValue(k, out var v) ? (int)Convert.ToDouble(v) : def;
        private static double GetDbl(Dictionary<string, object> p, string k, double def) => p.TryGetValue(k, out var v) ? Convert.ToDouble(v) : def;
    }
}
