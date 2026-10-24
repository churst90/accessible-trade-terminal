using System.Text;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Cipher SR, rebuilt on 2026-10-17 to draw what Market Cipher SR draws.
    ///
    /// <para><b>What the real one is, from the evidence.</b> Market Cipher SR v2.2.0 (inputs
    /// "H/L Pivot Days Back" 1, "Resolution" 15 minutes, "VolumeMA" 6, a VWAP checkbox; plots Pivot
    /// High, Pivot Low, Center Of Range, Cipher Resistance, Cipher Support, VWAP) was read from the
    /// settings dialog and legend in two tutorial videos and digitised from their frames against
    /// Coinbase BTC-USD candles for the same minutes (Fixtures/mcsr_observations.json):</para>
    /// <list type="bullet">
    ///   <item><b>Cipher Resistance / Support</b> — one level per side, drawn as a run of dots, one
    ///   per bar, at a bar's exact high (low), held — through closes beyond it — until a newer level
    ///   replaces it. Its run length is its age ("the number of dots shows how long the level has
    ///   been the local high"). <b>Exact rule (2026-10-23, read from Market Cipher SR 2.4.0's plotted
    ///   values on TradingView, BTCUSD 15-minute, 5,076 bars, 175 resistance and 180 support levels):</b>
    ///   the highs climb STRICTLY for two bars into the pivot bar and fall STRICTLY for two bars after
    ///   it (h[p-2] &lt; h[p-1] &lt; h[p] &gt; h[p+1] &gt; h[p+2]; the mirror for lows), the pivot bar's
    ///   volume is above the average of the last VolumeMA bars (itself included), and the level is
    ///   drawn from bar p + 3 — the first bar after the pivot's second right-hand bar closed. Rebuilt
    ///   from this, every bar of both lines matched Market Cipher SR's at VolumeMA 2, 6, 12 and 50
    ///   (VolumeMA 1 draws nothing there and here: no bar's volume beats itself). A plain two-bar
    ///   pivot is not enough: half of the pivots that pass the volume test do not climb in steps and
    ///   are skipped, the same half whatever the VolumeMA. Changing "H/L Pivot Days Back" did not
    ///   move a level. Earlier versions of this provider took a three-bar-left, one-bar-right pivot,
    ///   fitted to two digitised video frames (69% / 62%). See CipherSrMarketCipherFidelityTests.</item>
    ///   <item><b>Range High / Low / Mid</b> ("Pivot High", "Pivot Low", "Center Of Range") — the
    ///   session's high, low and midpoint; MCSR's legend printed 38226.62 / 37300.00 / 37763.31 for
    ///   2022-01-29 UTC, Coinbase's exact day high and low. MCSR draws the whole day's value from the
    ///   day's first bar (it knows the day's high before it happens; checked 2026-10-23, every bar of
    ///   a 15-minute history); this draws the RUNNING value, which is what MCSR shows on the live bar
    ///   and the only causal form. "H/L Pivot Days Back" N covers today and the N - 1 days before it,
    ///   as MCSR's does (checked at 1 and 3).</item>
    ///   <item><b>VWAP</b> — volume-weighted hlc3, reset at 00:00 UTC, on the chart's own bars. Five
    ///   values MCSR printed matched this to the cent, and on 2026-10-23 every bar of a 15-minute
    ///   history matched MCSR's plotted VWAP to 0.00001.</item>
    /// </list>
    ///
    /// <para><b>Timeframe.</b> MCSR is "built for the hourly timeframe and below" and computes its
    /// levels on 15-minute bars whatever the chart. A provider here sees only the chart's bars, so the
    /// levels come from the chart's bars. On a daily or longer chart a session is one bar, so Range
    /// and VWAP would only restate the bar; they are left empty there, and the dot levels are pivots
    /// of the daily bars.</para>
    ///
    /// <para><b>Replaced is not broken.</b> The zone line carries a level on every bar it is live.
    /// On the bar a newer level takes over, "{zone}_replaced" holds the OLD level (NaN elsewhere): the
    /// narrator says "New resistance at Y, replacing X" and the line renderer lifts the pen there. A
    /// level is never "broken" — MCSR keeps a level through closes beyond it — so the narrator speaks
    /// price crossing it instead, which is the true event.</para>
    ///
    /// <para><b>Retired 2026-10-17:</b> the "Resistance" / "Support" pivot-bar dots (MCSR draws no
    /// marker on the pivot bar, and those dots were drawn bars before the pivot could be known —
    /// declared look-ahead), the Auto-Scale pivot window, the 20-bar 1.2x volume filter, the break
    /// threshold and the ±0.3% bands. SignalCatalog.Retired says so to any saved strategy or alert.</para>
    /// </summary>
    public class CipherSrProvider : IIndicatorProvider
    {
        public string Name => "Cipher SR";

        public const string CompResistanceLine = "Resistance Zone";
        public const string CompSupportLine    = "Support Zone";
        public const string CompRangeHigh      = "Range High";
        public const string CompRangeLow       = "Range Low";
        public const string CompRangeMid       = "Range Mid";
        public const string CompVwap           = "VWAP";

        /// <summary>The pivot-bar dot components retired on 2026-10-17 (see SignalCatalog.Retired).</summary>
        public const string RetiredResistanceDot = "Resistance";
        public const string RetiredSupportDot    = "Support";

        // Companion arrays — hidden from navigation. The touch arrays keep the names the narrator's
        // zone-line scan derives from the zone name (NarrationScanner: Replace(" Zone", "") + "_touches").
        internal const string CompResistanceTouches = "Resistance_touches";
        internal const string CompSupportTouches    = "Support_touches";

        /// <summary>
        /// The suffix of the companion array that says a zone line's level was REPLACED on a bar:
        /// it holds the old level there and NaN everywhere else. Read by the narrator
        /// (NarrationScanner.ScanZoneLines) and the line renderer (StandardRenderers.RenderLine).
        /// </summary>
        public const string ReplacedSuffix = "_replaced";
        public const string CompResistanceReplaced = CompResistanceLine + ReplacedSuffix;
        public const string CompSupportReplaced    = CompSupportLine    + ReplacedSuffix;

        /// <summary>
        /// The suffix of the companion array holding, on every bar of a level's life, the time the
        /// level became live (Unix seconds, UTC). The zone speech turns it into the level's age.
        /// </summary>
        public const string SinceSuffix = "_since";
        public const string CompResistanceSince = CompResistanceLine + SinceSuffix;
        public const string CompSupportSince    = CompSupportLine    + SinceSuffix;

        /// <summary>Bars the highs (lows) must climb in steps into a pivot, and fall in steps after it.</summary>
        public const int PivotSteps = 2;
        /// <summary>Bars after the pivot bar that its level is first drawn: the bar after its last right-hand bar closed.</summary>
        public const int LevelLag = PivotSteps + 1;
        public const int DefaultVolumeMa = 6;
        public const int DefaultRangeDays = 1;

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
                    "Market Cipher SR's levels, built for the hourly chart and below. " +
                    "Purple dots: resistance, a high the two bars before climbed to in steps and the two bars after fell from in steps, on volume above its 6-bar average, drawn from the third bar after it; " +
                    "yellow dots: support, the same for lows. One level per side, held until a newer one replaces it; " +
                    "the length of a run of dots is the level's age. " +
                    "Range High, Low and Mid: the UTC day's range so far. VWAP: the day's volume-weighted average price, reset at 00:00 UTC. " +
                    "On daily and longer charts Range and VWAP are off and the levels come from the chart's own bars.",
                Components = new List<IndicatorComponentMetadata>
                {
                    // ── Cipher Resistance: a run of purple dots at the held level ──────────────────
                    // A Line drawn dotted rather than a Dot component: a Dot is a MARKER to the
                    // narrator, which would announce every bar of the run as a new signal. The pen
                    // lifts where the level is replaced ("{zone}_replaced"), so each level is its
                    // own run, as in MCSR. #E040FB is MCSR's purple, sampled from its frames.
                    // Sound unchanged from the zone line it was: a background sine whose pitch is
                    // where the level sits, stepping when a new one takes over.
                    new() { Name = CompResistanceLine,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Level,
                            DefaultColorHex = "#E040FB", DefaultThickness = 3.0f,
                            DefaultDashStyle = DashStyle.Dot,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value, DefaultBaseFrequency = 650.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultIsZoneLine = true,
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // ── Cipher Support: yellow dots ───────────────────────────────────────────────
                    new() { Name = CompSupportLine,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Level,
                            DefaultColorHex = "#FFEB3B", DefaultThickness = 3.0f,
                            DefaultDashStyle = DashStyle.Dot,
                            DefaultWaveform = "sine",
                            DefaultEnvelopeType = "Sustain",
                            DefaultPitchMapping = PitchMapping.Value, DefaultBaseFrequency = 300.0,
                            DefaultAmplitudeMapping = AmplitudeMapping.None,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultIsZoneLine = true,
                            DefaultUsePolarityColoring = false,
                            IsVisible = true },

                    // ── Range and VWAP: MUTED by default ──────────────────────────────────────────
                    // Drawn because MCSR draws them; muted so they add no voice and no crossing
                    // chatter to a chart that narrates Cipher SR. Navigable, and they speak their
                    // value on request. Appended after the zone lines so the two stops Cody's chart
                    // already had keep their order. The range is a StepLine: it steps when the day
                    // makes a new high or low and at the session change, as MCSR's lines step.
                    new() { Name = CompRangeHigh,
                            DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                            DefaultColorHex = "#2962FF", DefaultThickness = 1.0f,
                            DefaultIsMuted = true, DefaultUsePolarityColoring = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Value, DefaultAmplitudeMapping = AmplitudeMapping.None,
                            IsVisible = true },
                    new() { Name = CompRangeLow,
                            DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                            DefaultColorHex = "#2962FF", DefaultThickness = 1.0f,
                            DefaultIsMuted = true, DefaultUsePolarityColoring = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Value, DefaultAmplitudeMapping = AmplitudeMapping.None,
                            IsVisible = true },
                    new() { Name = CompRangeMid,
                            DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                            DefaultColorHex = "#2962FF", DefaultThickness = 2.0f,
                            DefaultDashStyle = DashStyle.Dot,
                            DefaultIsMuted = true, DefaultUsePolarityColoring = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Value, DefaultAmplitudeMapping = AmplitudeMapping.None,
                            IsVisible = true },
                    new() { Name = CompVwap,
                            DisplayType = ComponentDisplayType.Line, Role = ComponentRole.Level,
                            DefaultColorHex = "#FFFFFF", DefaultThickness = 1.5f,
                            DefaultIsMuted = true, DefaultUsePolarityColoring = false,
                            DefaultPlaybackLayer = PlaybackLayer.Background,
                            DefaultPitchMapping = PitchMapping.Value, DefaultAmplitudeMapping = AmplitudeMapping.None,
                            IsVisible = true },
                },

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "VolumeMA", DisplayName = "VolumeMA", DataType = typeof(int), DefaultValue = (double)DefaultVolumeMa,
                            MinValue = 0, MaxValue = 100,
                            Description = "Market Cipher SR's VolumeMA: a level is taken only from a bar whose volume is above the average of the last this-many bars (itself included). 0 takes every pivot." },
                    new() { Name = "RangeDays", DisplayName = "H/L Pivot Days Back", DataType = typeof(int), DefaultValue = (double)DefaultRangeDays,
                            MinValue = 1, MaxValue = 30,
                            Description = "Market Cipher SR's H/L Pivot Days Back: how many UTC days the Range High and Low cover, today included. Intraday charts only." },
                },
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("CIPHER_SR", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n == 0) return;

            int volMa    = Math.Clamp(GetInt(parameters, "VolumeMA", DefaultVolumeMa), 0, 100);
            int rangeDays = Math.Clamp(GetInt(parameters, "RangeDays", DefaultRangeDays), 1, 30);

            var resistanceLine     = NanArray(n);
            var supportLine        = NanArray(n);
            var resistanceTouches  = NanArray(n);
            var supportTouches     = NanArray(n);
            var resistanceReplaced = NanArray(n);
            var supportReplaced    = NanArray(n);
            var resistanceSince    = NanArray(n);
            var supportSince       = NanArray(n);
            var rangeHigh = NanArray(n);
            var rangeLow  = NanArray(n);
            var rangeMid  = NanArray(n);
            var vwap      = NanArray(n);

            // ── Levels: a stepped pivot, drawn LevelLag bars after it ───────────────────────────
            // Accepted at bar c = p + 3, the first bar after p + 2 closed. Reads bars p - 2 .. p + 2
            // only: nothing here reads the bar being written or any after it.
            double lastRes = double.NaN, lastSup = double.NaN;
            double resSince = double.NaN, supSince = double.NaN;
            int resTouches = 0, supTouches = 0;
            for (int c = 0; c < n; c++)
            {
                int p = c - LevelLag;
                bool newRes = false, newSup = false;
                if (p >= PivotSteps && data[p].High > 0 && data[p].Low > 0 && VolumeOk(data, p, volMa))
                {
                    double h = data[p].High, l = data[p].Low;
                    bool ph = true, pl = true;
                    for (int k = 0; k < PivotSteps; k++)
                    {
                        // Climbing in: bar p-k beats bar p-k-1. Falling out: bar p+k+1 is below bar p+k.
                        if (!(data[p - k].High > data[p - k - 1].High && data[p + k + 1].High < data[p + k].High)) ph = false;
                        if (!(data[p - k].Low  < data[p - k - 1].Low  && data[p + k + 1].Low  > data[p + k].Low))  pl = false;
                    }
                    if (ph)
                    {
                        if (!double.IsNaN(lastRes) && lastRes != h) resistanceReplaced[c] = lastRes;
                        if (lastRes != h) { resSince = UnixSeconds(data[c].Date); resTouches = 0; }
                        lastRes = h; newRes = true;
                    }
                    if (pl)
                    {
                        if (!double.IsNaN(lastSup) && lastSup != l) supportReplaced[c] = lastSup;
                        if (lastSup != l) { supSince = UnixSeconds(data[c].Date); supTouches = 0; }
                        lastSup = l; newSup = true;
                    }
                }

                // A test: the wick reached the level and the close stayed on its side. Not counted
                // on the bar a level is accepted — that bar's wick is no test of a level it did not
                // yet know.
                if (!double.IsNaN(lastRes))
                {
                    if (!newRes && data[c].High >= lastRes && data[c].Close < lastRes) resTouches++;
                    resistanceLine[c] = lastRes; resistanceTouches[c] = resTouches; resistanceSince[c] = resSince;
                }
                if (!double.IsNaN(lastSup))
                {
                    if (!newSup && data[c].Low <= lastSup && data[c].Close > lastSup) supTouches++;
                    supportLine[c] = lastSup; supportTouches[c] = supTouches; supportSince[c] = supSince;
                }
            }

            // ── Session range and VWAP (UTC days; intraday charts only) ─────────────────────────
            long session = long.MinValue;
            double pv = 0, vv = 0;
            var days = new List<(long Day, double High, double Low)>();
            for (int i = 0; i < n; i++)
            {
                // Every bar is accumulated — the first bar of a chart starts its session too — but
                // a value is written only where the chart is known to be intraday (bar 0 cannot know).
                long day = (long)Math.Floor(UnixSeconds(data[i].Date) / 86400.0);
                if (day != session) { session = day; pv = vv = 0; days.Add((day, data[i].High, data[i].Low)); }
                else
                {
                    var d = days[^1];
                    days[^1] = (d.Day, Math.Max(d.High, data[i].High), Math.Min(d.Low, data[i].Low));
                }

                double hlc3 = (data[i].High + data[i].Low + data[i].Close) / 3.0;
                double vol = Math.Max(0, data[i].Volume);
                pv += hlc3 * vol; vv += vol;
                if (!IsIntraday(data, i)) continue;

                double hi = double.MinValue, lo = double.MaxValue;
                for (int k = days.Count - 1; k >= 0 && day - days[k].Day < rangeDays; k--)
                {
                    hi = Math.Max(hi, days[k].High); lo = Math.Min(lo, days[k].Low);
                }
                rangeHigh[i] = hi; rangeLow[i] = lo; rangeMid[i] = (hi + lo) / 2.0;
                vwap[i] = vv > 0 ? pv / vv : double.NaN;
            }

            WriteToBuffer(buffer, CompResistanceLine,     resistanceLine,     n);
            WriteToBuffer(buffer, CompSupportLine,        supportLine,        n);
            WriteToBuffer(buffer, CompRangeHigh,          rangeHigh,          n);
            WriteToBuffer(buffer, CompRangeLow,           rangeLow,           n);
            WriteToBuffer(buffer, CompRangeMid,           rangeMid,           n);
            WriteToBuffer(buffer, CompVwap,               vwap,               n);
            WriteToBuffer(buffer, CompResistanceTouches,  resistanceTouches,  n);
            WriteToBuffer(buffer, CompSupportTouches,     supportTouches,     n);
            WriteToBuffer(buffer, CompResistanceReplaced, resistanceReplaced, n);
            WriteToBuffer(buffer, CompSupportReplaced,    supportReplaced,    n);
            WriteToBuffer(buffer, CompResistanceSince,    resistanceSince,    n);
            WriteToBuffer(buffer, CompSupportSince,       supportSince,       n);
        }

        /// <summary>Pivot-bar volume above the average of the last <paramref name="len"/> bars, itself included.</summary>
        private static bool VolumeOk(ReadOnlySpan<Ohlcv> data, int p, int len)
        {
            if (len <= 0) return true;
            if (p - len + 1 < 0) return false;
            double sum = 0;
            for (int k = p - len + 1; k <= p; k++) sum += data[k].Volume;
            double avg = sum / len;
            return avg < 1e-10 || data[p].Volume > avg;
        }

        /// <summary>
        /// Shorter than a day between this bar and one of the few before it. Read backwards only, so
        /// a bar's answer never depends on bars after it; a weekend gap on an intraday chart is
        /// bridged by the bars before it.
        /// </summary>
        internal static bool IsIntraday(ReadOnlySpan<Ohlcv> data, int i)
        {
            for (int k = i; k >= Math.Max(1, i - 5); k--)
                if ((data[k].Date - data[k - 1].Date).TotalSeconds is > 0 and < 86400) return true;
            return false;
        }

        internal static double UnixSeconds(DateTime t)
        {
            var utc = t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);
            return (utc - DateTime.UnixEpoch).TotalSeconds;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            => Calculate(code, data, parameters, buffer);

        /// <summary>
        /// Bars before a level found here is the level a longer history would find: the pivot's steps
        /// in, its lag and the volume average. (A level can be older than this — it holds
        /// until replaced — and a session's range and VWAP start at the session's first bar.)
        /// </summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            int volMa = Math.Clamp(GetInt(parameters, "VolumeMA", DefaultVolumeMa), 0, 100);
            return Math.Max(PivotSteps, volMa) + LevelLag + 1;
        }

        /// <summary>
        /// Value-only speech. Zone lines: "87373.64, price below, held 12 days, tested 3 times" — the
        /// level, the side current price is on, how long the level has held (the run of dots MCSR
        /// draws) and how many times price tested it. Range, VWAP and companions fall through.
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value))
                return "no data";

            // The live close (injected by NavigationFeedbackManager), so the side is where price
            // is now, whichever historical bar is being read.
            double liveClose = allComponentData.TryGetValue("__live_close", out var lcArr) &&
                               lcArr != null && lcArr.Length > 0 && lcArr[0] > 0
                ? lcArr[0]
                : bar.Close;

            return componentName switch
            {
                CompResistanceLine => FormatZoneLineSpeech(value, liveClose, bar, allComponentData, CompResistanceTouches, CompResistanceSince, dataIndex),
                CompSupportLine    => FormatZoneLineSpeech(value, liveClose, bar, allComponentData, CompSupportTouches,    CompSupportSince,    dataIndex),
                _ => null
            };
        }

        /// <summary>
        /// Names the levels the zone lines carry on this bar, how long each has held, and the session
        /// range. No key speaks this today: BarDetailService bypasses GetDetailFact.
        /// </summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters)
        {
            if (!code.Equals("CIPHER_SR", StringComparison.OrdinalIgnoreCase) || index < 0 || index >= data.Length)
                return string.Empty;

            var sb = new StringBuilder();
            double resLevel = GetVal(results, CompResistanceLine, index);
            double supLevel = GetVal(results, CompSupportLine,    index);
            if (!double.IsNaN(resLevel))
                sb.Append($"Most recent resistance: {Accessibility.SpeechPriceFormatter.FormatPrice(resLevel)}{Held(results, CompResistanceSince, index, data[index].Date)}. ");
            if (!double.IsNaN(supLevel))
                sb.Append($"Most recent support: {Accessibility.SpeechPriceFormatter.FormatPrice(supLevel)}{Held(results, CompSupportSince, index, data[index].Date)}. ");
            double rh = GetVal(results, CompRangeHigh, index), rl = GetVal(results, CompRangeLow, index);
            if (!double.IsNaN(rh) && !double.IsNaN(rl))
                sb.Append($"Range {Accessibility.SpeechPriceFormatter.FormatPrice(rl)} to {Accessibility.SpeechPriceFormatter.FormatPrice(rh)}. ");
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "No confirmed S/R levels yet.";
        }

        private static string Held(IReadOnlyDictionary<string, double[]> results, string sinceKey, int index, DateTime at)
        {
            double since = GetVal(results, sinceKey, index);
            string d = Duration(since, at);
            return d.Length == 0 ? "" : $", held {d}";
        }

        // ── Speech helpers ────────────────────────────────────────────────────────────────

        private static string FormatZoneLineSpeech(double level, double closePrice, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, string touchKey, string sinceKey, int dataIndex)
        {
            string posStr = closePrice >= level ? "price above" : "price below";

            int touches = 0;
            if (allComponentData.TryGetValue(touchKey, out var arr) && dataIndex >= 0 && dataIndex < arr.Length && !double.IsNaN(arr[dataIndex]))
                touches = (int)arr[dataIndex];

            string held = "";
            if (allComponentData.TryGetValue(sinceKey, out var since) && dataIndex >= 0 && dataIndex < since.Length)
            {
                string d = Duration(since[dataIndex], bar.Date);
                if (d.Length > 0) held = $", held {d}";
            }

            string touchStr = touches == 0 ? "" : touches == 1 ? ", tested once" : $", tested {touches} times";
            return $"{Accessibility.SpeechPriceFormatter.FormatPrice(level)}, {posStr}{held}{touchStr}";
        }

        /// <summary>"12 days", "5 hours", "45 minutes" from a Unix-seconds start to <paramref name="at"/>; "" if unknown.</summary>
        internal static string Duration(double sinceUnix, DateTime at)
        {
            if (double.IsNaN(sinceUnix)) return "";
            double s = UnixSeconds(at) - sinceUnix;
            if (s < 0) return "";
            static string U(long v, string unit) => v == 1 ? $"1 {unit}" : $"{v} {unit}s";
            if (s >= 2 * 86400) return U((long)Math.Round(s / 86400), "day");
            if (s >= 2 * 3600)  return U((long)Math.Round(s / 3600), "hour");
            if (s >= 60)        return U((long)Math.Round(s / 60), "minute");
            return "under a minute";
        }

        // ── Data helpers ──────────────────────────────────────────────────────────────────

        private static double[] NanArray(int n)
        {
            var arr = new double[n];
            Array.Fill(arr, double.NaN);
            return arr;
        }

        private static void WriteToBuffer(IIndicatorResultBuffer buffer, string name, double[] src, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len  = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = src[i];
        }

        private static double GetVal(IReadOnlyDictionary<string, double[]> results, string key, int index)
        {
            if (results.TryGetValue(key, out var arr) && index >= 0 && index < arr.Length) return arr[index];
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

        // SR has no OB/OS reference level lines.
        public List<LevelDescriptor> GetDefaultLevels(string code)
            => new();
    }
}
