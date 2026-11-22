using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// <b>9-13 Sequential</b> (Cody, 2026-11-18): the exhaustion count traders know from the
    /// published descriptions of the method: a setup counts 1 to 9, then a countdown counts 1 to 13.
    /// Not called by the trademarked name.
    ///
    /// <para><b>Setup.</b> A buy setup is nine closes in a row each lower than the close four bars
    /// earlier. It starts only after a price flip: the bar before bar 1 closed above the close four
    /// bars before IT. A bar that fails the comparison ends the count; after a 9 the run must break
    /// before a new setup can start. A sell setup is the mirror (higher closes, a flip down).</para>
    ///
    /// <para><b>Perfection.</b> A buy setup is perfected when the low of bar 8 or bar 9 is at or
    /// below the lows of bars 6 and 7; a sell setup when the high of bar 8 or 9 is at or above the
    /// highs of bars 6 and 7.</para>
    ///
    /// <para><b>Countdown.</b> Starts on the setup's bar 9. A buy countdown counts each bar whose
    /// close is at or below the low two bars earlier; the bars need not be in a row. Bar 13 must
    /// also have a low at or below the close of countdown bar 8; a bar that meets the first rule
    /// but not this one is deferred (not counted), and the count waits for one that meets both.
    /// A sell countdown is the mirror (close at or above the high two bars earlier; bar 13's high
    /// at or above bar 8's close).</para>
    ///
    /// <para><b>Cancellation and recycle.</b> A countdown is cancelled when a setup the other way
    /// completes, or when price closes beyond its own setup's extreme (above a buy setup's highest
    /// high, below a sell setup's lowest low; those two prices are the Setup Resistance and Setup
    /// Support lines). A new setup the same way restarts the countdown from its own bar 9 (the
    /// simple recycle; the published method also compares the two setups' sizes, which this does
    /// not).</para>
    ///
    /// <para><b>Heard.</b> Every count is a marker with its number printed beside it, so every
    /// count ticks as you arrow and is said ("Buy setup 7."). The 9 and the 13 are components of
    /// their own, with their own shape and bell, so they sound unlike the counts before them, and
    /// Ctrl+Left/Right on them jumps from one completed setup or countdown to the next. A marker's
    /// value is its bar's low (buy) or high (sell), the price it hangs from.</para>
    ///
    /// <para>Every value is known at the close of its own bar: nothing is painted in hindsight.</para>
    /// </summary>
    public sealed class Sequential913Provider : IIndicatorProvider
    {
        public string Name => "9-13 Sequential";

        public const string Code = "SEQUENTIAL_9_13";
        public const string CompBuySetup = "Buy Setup";
        public const string CompBuySetup9 = "Buy Setup 9";
        public const string CompSellSetup = "Sell Setup";
        public const string CompSellSetup9 = "Sell Setup 9";
        public const string CompBuyCountdown = "Buy Countdown";
        public const string CompBuyCountdown13 = "Buy Countdown 13";
        public const string CompSellCountdown = "Sell Countdown";
        public const string CompSellCountdown13 = "Sell Countdown 13";
        public const string CompResistance = "Setup Resistance";
        public const string CompSupport = "Setup Support";

        /// <summary>On a 9: 1 when the setup is perfected, 0 when not.</summary>
        public const string PerfectedSuffix = "_perfected";

        public const int SetupLength = 9, SetupLookback = 4, CountdownLength = 13, CountdownLookback = 2;

        private const string BuyHex = "#26A69A", SellHex = "#EF5350";
        private const string Buy9Hex = "#00E676", Sell9Hex = "#FF1744";
        private const string BuyCountHex = "#4FC3F7", SellCountHex = "#FFB74D";
        private const string Buy13Hex = "#2979FF", Sell13Hex = "#FF9100";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code = Code,
                Causality = ComponentCausality.Causal,
                Name = "9-13 Sequential",
                Category = "Reversal",
                DefaultPane = "Main",
                RequiresFullRecalcOnTick = false,
                Description =
                    "Counts a trend toward exhaustion. A setup counts nine closes in a row below " +
                    "(buy) or above (sell) the close four bars earlier; a countdown then counts 13 " +
                    "closes at or beyond the low (buy) or high (sell) two bars earlier. A 9 or a 13 " +
                    "is where the move is often tired. Every count is a numbered marker; the 9 and " +
                    "the 13 have their own sound.",
                Components = new List<IndicatorComponentMetadata>
                {
                    Count(CompBuySetup, "Buy Setup", ComponentDisplayType.Dot, MarkerAnchor.BelowBar, BuyHex, 392, "Buy setup {label}."),
                    Done(CompBuySetup9, "Buy Setup 9", ComponentDisplayType.TriangleUp, MarkerAnchor.BelowBar, Buy9Hex, "crystal_bell", 784, "Buy setup 9."),
                    Count(CompSellSetup, "Sell Setup", ComponentDisplayType.Dot, MarkerAnchor.AboveBar, SellHex, 494, "Sell setup {label}."),
                    Done(CompSellSetup9, "Sell Setup 9", ComponentDisplayType.TriangleDown, MarkerAnchor.AboveBar, Sell9Hex, "crystal_bell", 988, "Sell setup 9."),
                    Count(CompBuyCountdown, "Buy Countdown", ComponentDisplayType.Square, MarkerAnchor.BelowBar, BuyCountHex, 330, "Buy countdown {label}."),
                    Done(CompBuyCountdown13, "Buy Countdown 13", ComponentDisplayType.Diamond, MarkerAnchor.BelowBar, Buy13Hex, "dual_tone_bell", 660, "Buy countdown 13."),
                    Count(CompSellCountdown, "Sell Countdown", ComponentDisplayType.Square, MarkerAnchor.AboveBar, SellCountHex, 440, "Sell countdown {label}."),
                    Done(CompSellCountdown13, "Sell Countdown 13", ComponentDisplayType.Diamond, MarkerAnchor.AboveBar, Sell13Hex, "dual_tone_bell", 880, "Sell countdown 13."),
                    new()
                    {
                        Name = CompResistance, DisplayName = "Setup Resistance",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                        Causality = ComponentCausality.Causal,
                        DefaultColorHex = SellHex, DefaultThickness = 1f, DefaultDashStyle = DashStyle.Dash,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Setup resistance {value:price}.",
                    },
                    new()
                    {
                        Name = CompSupport, DisplayName = "Setup Support",
                        DisplayType = ComponentDisplayType.StepLine, Role = ComponentRole.Level,
                        Causality = ComponentCausality.Causal,
                        DefaultColorHex = BuyHex, DefaultThickness = 1f, DefaultDashStyle = DashStyle.Dash,
                        SubscribedLevelNames = Array.Empty<string>(),
                        SpeechTemplate = "Setup support {value:price}.",
                    },
                },
            }
        };

        /// <summary>A count, 1 to 8 or 1 to 12: a small marker that ticks, its number beside it.</summary>
        private static IndicatorComponentMetadata Count(string name, string display, ComponentDisplayType shape,
            MarkerAnchor anchor, string hex, double freq, string speech) => new()
        {
            Name = name, DisplayName = display, DisplayType = shape, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Causal, DefaultMarkerAnchor = anchor,
            DefaultColorHex = hex, DefaultThickness = 5f,
            SubscribedLevelNames = Array.Empty<string>(),
            DefaultEnvelopeType = "Ping", DefaultDecayMs = 150, DefaultBaseFrequency = freq,
            DefaultPlaybackLayer = PlaybackLayer.Midground,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
        };

        /// <summary>A 9 or a 13: a larger marker with a bell of its own.</summary>
        private static IndicatorComponentMetadata Done(string name, string display, ComponentDisplayType shape,
            MarkerAnchor anchor, string hex, string patch, double freq, string speech) => new()
        {
            Name = name, DisplayName = display, DisplayType = shape, Role = ComponentRole.Signal,
            Causality = ComponentCausality.Causal, DefaultMarkerAnchor = anchor,
            DefaultColorHex = hex, DefaultThickness = 10f,
            SubscribedLevelNames = Array.Empty<string>(),
            DefaultEnvelopeType = "Ping", DefaultDecayMs = 500, DefaultBaseFrequency = freq,
            DefaultSoundPatchId = patch, DefaultPlaybackLayer = PlaybackLayer.Foreground,
            DefaultPitchMapping = PitchMapping.None, DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
        };

        /// <summary>What the count is doing at one bar.</summary>
        internal struct BarCount
        {
            /// <summary>The setup or countdown number printed on this bar, 0 when none.</summary>
            public int BuySetup, SellSetup, BuyCountdown, SellCountdown;
            public bool BuyPerfected, SellPerfected;
            /// <summary>A countdown bar that met the close rule at 13 but not bar 8's qualifier.</summary>
            public bool BuyDeferred, SellDeferred;
            /// <summary>A countdown that ended on this bar without a 13, and why.</summary>
            public string? BuyCancelled, SellCancelled;
            /// <summary>Where each countdown stands after this bar: -1 when none is running.</summary>
            public int BuyCountdownAt, SellCountdownAt;
            /// <summary>The setup run in progress after this bar (0 when none).</summary>
            public int BuySetupAt, SellSetupAt;
            public double Resistance, Support;
        }

        /// <summary>The whole count, bar by bar. Each bar reads only itself and the bars before it.</summary>
        internal static BarCount[] Run(ReadOnlySpan<Ohlcv> d)
        {
            int n = d.Length;
            var r = new BarCount[n];
            int buyRun = 0, sellRun = 0;            // -1 = a 9 was reached; wait for the run to break
            int buyCd = -1, sellCd = -1;            // -1 = no countdown
            double buyCd8 = double.NaN, sellCd8 = double.NaN;
            double resistance = double.NaN, support = double.NaN;
            double buyExtreme = double.NaN, sellExtreme = double.NaN;  // the running countdown's own setup's

            for (int i = 0; i < n; i++)
            {
                var b = new BarCount();

                // ── Setup ──
                bool below = i >= SetupLookback && d[i].Close < d[i - SetupLookback].Close;
                bool above = i >= SetupLookback && d[i].Close > d[i - SetupLookback].Close;
                bool flipDown = i >= SetupLookback + 1 && d[i - 1].Close > d[i - 1 - SetupLookback].Close;
                bool flipUp = i >= SetupLookback + 1 && d[i - 1].Close < d[i - 1 - SetupLookback].Close;

                if (!below) buyRun = 0;
                else if (buyRun > 0) buyRun++;
                else if (buyRun == 0 && flipDown) buyRun = 1;
                if (!above) sellRun = 0;
                else if (sellRun > 0) sellRun++;
                else if (sellRun == 0 && flipUp) sellRun = 1;

                if (buyRun > 0) b.BuySetup = buyRun;
                if (sellRun > 0) b.SellSetup = sellRun;

                if (buyRun == SetupLength)
                {
                    b.BuyPerfected = Low(d, i, 8) <= Math.Min(Low(d, i, 6), Low(d, i, 7))
                                  || Low(d, i, 9) <= Math.Min(Low(d, i, 6), Low(d, i, 7));
                    resistance = Max(d, i - SetupLength + 1, i, high: true);
                    if (sellCd >= 0) b.SellCancelled = "a buy setup completed";
                    sellCd = -1;
                    buyCd = 0; buyCd8 = double.NaN; buyExtreme = resistance;   // start, or recycle
                    buyRun = -1;
                }
                if (sellRun == SetupLength)
                {
                    b.SellPerfected = High(d, i, 8) >= Math.Max(High(d, i, 6), High(d, i, 7))
                                   || High(d, i, 9) >= Math.Max(High(d, i, 6), High(d, i, 7));
                    support = Max(d, i - SetupLength + 1, i, high: false);
                    if (buyCd >= 0) b.BuyCancelled = "a sell setup completed";
                    buyCd = -1;
                    sellCd = 0; sellCd8 = double.NaN; sellExtreme = support;
                    sellRun = -1;
                }

                // ── Cancellation by price ──
                if (buyCd >= 0 && d[i].Close > buyExtreme)
                {
                    b.BuyCancelled = "price closed above the setup's high";
                    buyCd = -1;
                }
                if (sellCd >= 0 && d[i].Close < sellExtreme)
                {
                    b.SellCancelled = "price closed below the setup's low";
                    sellCd = -1;
                }

                // ── Countdown ──
                if (buyCd >= 0 && i >= CountdownLookback && d[i].Close <= d[i - CountdownLookback].Low)
                {
                    int next = buyCd + 1;
                    if (next < CountdownLength || d[i].Low <= buyCd8)
                    {
                        buyCd = next;
                        b.BuyCountdown = next;
                        if (next == 8) buyCd8 = d[i].Close;
                    }
                    else b.BuyDeferred = true;
                }
                if (sellCd >= 0 && i >= CountdownLookback && d[i].Close >= d[i - CountdownLookback].High)
                {
                    int next = sellCd + 1;
                    if (next < CountdownLength || d[i].High >= sellCd8)
                    {
                        sellCd = next;
                        b.SellCountdown = next;
                        if (next == 8) sellCd8 = d[i].Close;
                    }
                    else b.SellDeferred = true;
                }
                if (buyCd == CountdownLength) buyCd = -1;
                if (sellCd == CountdownLength) sellCd = -1;

                b.BuyCountdownAt = buyCd;
                b.SellCountdownAt = sellCd;
                b.BuySetupAt = Math.Max(buyRun, 0);
                b.SellSetupAt = Math.Max(sellRun, 0);
                b.Resistance = resistance;
                b.Support = support;
                r[i] = b;
            }
            return r;
        }

        /// <summary>The low of setup bar <paramref name="k"/> (1–9) when bar 9 is <paramref name="i9"/>.</summary>
        private static double Low(ReadOnlySpan<Ohlcv> d, int i9, int k) => d[i9 - (SetupLength - k)].Low;
        private static double High(ReadOnlySpan<Ohlcv> d, int i9, int k) => d[i9 - (SetupLength - k)].High;

        private static double Max(ReadOnlySpan<Ohlcv> d, int from, int to, bool high)
        {
            double v = high ? double.MinValue : double.MaxValue;
            for (int k = from; k <= to; k++)
                v = high ? Math.Max(v, d[k].High) : Math.Min(v, d[k].Low);
            return v;
        }

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var counts = Run(data);
            Span<double> Nan(string name)
            {
                var s = buffer.GetComponentSpan(name);
                s.Fill(double.NaN);
                return s;
            }

            var buySetup = Nan(CompBuySetup); var buySetupLabel = Nan(CompBuySetup + ComponentCompanions.Label);
            var buy9 = Nan(CompBuySetup9); var buy9Label = Nan(CompBuySetup9 + ComponentCompanions.Label);
            var buy9Perfect = Nan(CompBuySetup9 + PerfectedSuffix);
            var sellSetup = Nan(CompSellSetup); var sellSetupLabel = Nan(CompSellSetup + ComponentCompanions.Label);
            var sell9 = Nan(CompSellSetup9); var sell9Label = Nan(CompSellSetup9 + ComponentCompanions.Label);
            var sell9Perfect = Nan(CompSellSetup9 + PerfectedSuffix);
            var buyCd = Nan(CompBuyCountdown); var buyCdLabel = Nan(CompBuyCountdown + ComponentCompanions.Label);
            var buy13 = Nan(CompBuyCountdown13); var buy13Label = Nan(CompBuyCountdown13 + ComponentCompanions.Label);
            var sellCd = Nan(CompSellCountdown); var sellCdLabel = Nan(CompSellCountdown + ComponentCompanions.Label);
            var sell13 = Nan(CompSellCountdown13); var sell13Label = Nan(CompSellCountdown13 + ComponentCompanions.Label);
            var resistance = Nan(CompResistance);
            var support = Nan(CompSupport);

            for (int i = 0; i < data.Length; i++)
            {
                var c = counts[i];
                double low = data[i].Low, high = data[i].High;
                if (c.BuySetup == SetupLength) { buy9[i] = low; buy9Label[i] = SetupLength; buy9Perfect[i] = c.BuyPerfected ? 1 : 0; }
                else if (c.BuySetup > 0) { buySetup[i] = low; buySetupLabel[i] = c.BuySetup; }
                if (c.SellSetup == SetupLength) { sell9[i] = high; sell9Label[i] = SetupLength; sell9Perfect[i] = c.SellPerfected ? 1 : 0; }
                else if (c.SellSetup > 0) { sellSetup[i] = high; sellSetupLabel[i] = c.SellSetup; }
                if (c.BuyCountdown == CountdownLength) { buy13[i] = low; buy13Label[i] = CountdownLength; }
                else if (c.BuyCountdown > 0) { buyCd[i] = low; buyCdLabel[i] = c.BuyCountdown; }
                if (c.SellCountdown == CountdownLength) { sell13[i] = high; sell13Label[i] = CountdownLength; }
                else if (c.SellCountdown > 0) { sellCd[i] = high; sellCdLabel[i] = c.SellCountdown; }
                resistance[i] = c.Resistance;
                support[i] = c.Support;
            }
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) =>
            Calculate(code, data, parameters, buffer);

        /// <summary>A setup needs its four-bar lookback and the flip before it; a countdown can run
        /// for dozens of bars, so the count settles once a few setups have come and gone.</summary>
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 100;

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            int Label(string comp) =>
                allComponentData.TryGetValue(comp + ComponentCompanions.Label, out var a)
                && dataIndex >= 0 && dataIndex < a.Length && !double.IsNaN(a[dataIndex]) ? (int)a[dataIndex] : 0;
            bool Perfected(string comp) =>
                allComponentData.TryGetValue(comp + PerfectedSuffix, out var a)
                && dataIndex >= 0 && dataIndex < a.Length && a[dataIndex] == 1;

            return componentName switch
            {
                CompBuySetup => $"Buy setup {Label(componentName)}.",
                CompSellSetup => $"Sell setup {Label(componentName)}.",
                CompBuyCountdown => $"Buy countdown {Label(componentName)}.",
                CompSellCountdown => $"Sell countdown {Label(componentName)}.",
                CompBuySetup9 => $"Buy setup 9, {(Perfected(componentName) ? "perfected" : "not perfected")}.",
                CompSellSetup9 => $"Sell setup 9, {(Perfected(componentName) ? "perfected" : "not perfected")}.",
                CompBuyCountdown13 => "Buy countdown 13.",
                CompSellCountdown13 => "Sell countdown 13.",
                _ => null,
            };
        }

        /// <summary>
        /// Alt+Shift+D: what is printed on this bar and where each count stands after it.
        /// "9-13 Sequential: Buy setup 9, perfected. Buy countdown at 0 of 13."
        /// </summary>
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "";
            var c = Run(data[..(index + 1)])[index];
            return "9-13 Sequential: " + Describe(c);
        }

        internal static string Describe(BarCount c)
        {
            var parts = new List<string>();
            if (c.BuySetup > 0)
                parts.Add(c.BuySetup == SetupLength ? $"buy setup 9, {(c.BuyPerfected ? "perfected" : "not perfected")}" : $"buy setup {c.BuySetup}");
            if (c.SellSetup > 0)
                parts.Add(c.SellSetup == SetupLength ? $"sell setup 9, {(c.SellPerfected ? "perfected" : "not perfected")}" : $"sell setup {c.SellSetup}");
            if (c.BuyCountdown > 0) parts.Add($"buy countdown {c.BuyCountdown}");
            if (c.SellCountdown > 0) parts.Add($"sell countdown {c.SellCountdown}");
            if (c.BuyDeferred) parts.Add("buy countdown 13 deferred, the low is above bar 8's close");
            if (c.SellDeferred) parts.Add("sell countdown 13 deferred, the high is below bar 8's close");
            if (c.BuyCancelled != null) parts.Add($"buy countdown cancelled, {c.BuyCancelled}");
            if (c.SellCancelled != null) parts.Add($"sell countdown cancelled, {c.SellCancelled}");
            if (c.BuyCountdownAt >= 0 && c.BuyCountdown == 0) parts.Add($"buy countdown at {c.BuyCountdownAt} of 13");
            if (c.SellCountdownAt >= 0 && c.SellCountdown == 0) parts.Add($"sell countdown at {c.SellCountdownAt} of 13");
            if (parts.Count == 0) parts.Add("no count on this bar");
            return string.Join(". ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..])) + ".";
        }
    }
}
