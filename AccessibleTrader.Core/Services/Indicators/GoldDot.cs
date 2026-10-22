using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// The gold dot, kept as a piece to draw with after it left Cipher B on 2026-10-22 (Cody: "remove
    /// the gold dot from the cipher b indicator as it isn't faithful. Keep the gold dot as a drawing
    /// element because something may want it later"). No indicator uses it today.
    ///
    /// <para>Two parts. <see cref="Component"/> is its look and sound — a gold dot, 8 thick, a
    /// dual-tone bell at 440 Hz for 500 ms — as a component any indicator can declare in one line.
    /// <see cref="Rule"/> is when it fired in Cipher B: the readable VuManChu Cipher B code's gold
    /// circle, ported exactly, as a function of a slow wave, the lows and RSI 14.</para>
    ///
    /// <para>What went with it: Cipher B's "Triple Confluence Buy" component, its Gold Dot Rule and
    /// Gold Dot: RSI Divergence Too settings, and the older Confluence rule (the gated buy dot plus
    /// 3 of RSI oversold, a green bar, ADX and ATR) with its RSI, ADX and ATR settings — that rule
    /// was ours, not Market Cipher's, and is not kept.</para>
    /// </summary>
    public static class GoldDot
    {
        /// <summary>The colour the gold dot was drawn in.</summary>
        public const string ColorHex = "#FFD700";

        /// <summary>
        /// A gold dot component: drawn as a dot, sounded as a bell, spoken by
        /// <paramref name="speech"/>. Its values are where to draw it (NaN = no dot on that bar).
        /// </summary>
        public static IndicatorComponentMetadata Component(string name, string displayName, string speech) => new()
        {
            Name = name, DisplayName = displayName, DisplayType = ComponentDisplayType.Dot, Role = ComponentRole.Signal,
            DefaultColorHex = ColorHex, DefaultThickness = 8.0f,
            DefaultEnvelopeType = "Ping",
            DefaultPlaybackLayer = PlaybackLayer.Foreground,
            DefaultSoundPatchId = "dual_tone_bell",
            DefaultDecayMs = 500,
            DefaultBaseFrequency = 440.0,
            DefaultPitchMapping = PitchMapping.None,
            DefaultAmplitudeMapping = AmplitudeMapping.None,
            DefaultSignalSpeechTemplate = speech,
            DefaultUsePolarityColoring = false,
        };

        /// <summary>VuManChu's <c>osLevel3 = input(-75, 'WT Oversold Level 3')</c>: the earlier
        /// bottom must reach it, and the slow wave must be back above it.</summary>
        public const double GoldDeepLevel = -75.0;
        /// <summary>VuManChu's <c>wtDivOSLevel = input(-65, 'WT Bullish Divergence min')</c>: a
        /// slow-wave bottom counts for the divergence only at or below this.</summary>
        public const double GoldWtDivergenceLevel = -65.0;
        /// <summary>VuManChu's <c>rsiDivOSLevel = input(30, 'RSI Bullish Divergence min')</c>, for the
        /// optional RSI divergence (<c>includeRsiDivergence</c>).</summary>
        public const double GoldRsiDivergenceLevel = 30.0;
        /// <summary>The literal <c>wtLow_prev - wt2 &lt;= -5</c>: the slow wave at least this far above
        /// the earlier bottom.</summary>
        public const double GoldMinRise = 5.0;
        /// <summary>The literal <c>lastRsi &lt; 30</c> .</summary>
        public const double GoldRsiAtLow = 30.0;

        /// <summary>
        /// <b>The gold dot, ported exactly from the readable VuManChu Cipher B code</b>
        /// ("VMC Cipher_B_Divergences", pastebin.com/raw/m8Kp9XZk, fetched 2026-10-18). The lines it
        /// rests on, verbatim:
        /// <code>
        /// f_bot_fractal(src) => src[4] > src[2] and src[3] > src[2] and src[2] &lt; src[1] and src[2] &lt; src[0]
        /// fractalBot = f_fractalize(src) &lt; 0 and (useLimits ? src[2] &lt;= botLimit : true) ? src[2] : na
        /// lowPrev = valuewhen(fractalBot, src[2], 0)[2]
        /// lowPrice = valuewhen(fractalBot, low[2], 0)[2]
        /// bullSignal = fractalBot and low[2] &lt; lowPrice and src[2] > lowPrev
        /// [wtFractalTop, wtFractalBot, wtLow_prev, wtBearDiv, wtBullDiv, ...] = f_findDivs(wt2, wtDivOBLevel, wtDivOSLevel, true)
        /// [rsiFractalTop, rsiFractalBot, rsiLow_prev, rsiBearDiv, rsiBullDiv, ...] = f_findDivs(rsi, rsiDivOBLevel, rsiDivOSLevel, true)
        /// lastRsi = valuewhen(wtFractalBot, rsi[2], 0)[2]
        /// wtGoldBuy = ((wtShowDiv and wtBullDiv) or (rsiShowDiv and rsiBullDiv)) and
        ///            wtLow_prev &lt;= osLevel3 and
        ///            wt2 > osLevel3 and
        ///            wtLow_prev - wt2 &lt;= -5 and
        ///            lastRsi &lt; 30
        /// </code>
        /// with wtShowDiv = true, rsiShowDiv = false, osLevel3 = −75, wtDivOSLevel = −65,
        /// rsiDivOSLevel = 30, and rsi = rsi(close, 14). In words, at bar t:
        /// <list type="bullet">
        /// <item>bar t−2 is a slow-wave bottom — strictly below the two bars either side of it — at
        ///   or below −65 (a "fractal", confirmed only once bars t−1 and t exist);</item>
        /// <item>the previous such bottom, confirmed at or before bar t−2, was HIGHER on the wave and
        ///   its bar's low was HIGHER in price: price made a lower low, the wave a higher one — a
        ///   regular bullish divergence (or, with <paramref name="includeRsiDivergence"/>, the same on
        ///   RSI with both RSI bottoms at or below 30);</item>
        /// <item>that previous slow-wave bottom was at or below −75, and RSI at its bar was under 30;</item>
        /// <item>the slow wave NOW (bar t, not the bottom) is above −75 and at least 5 above that
        ///   previous bottom.</item>
        /// </list>
        ///
        /// <para><b>No cross is required</b> — the code's own header says "have crossed up", and
        /// "RSI below 20, … below or equal to -80"; the code says neither, and the code is what
        /// draws the dot. Where they disagree the CODE is ported.</para>
        ///
        /// <para><b>Causal: stamped at t, the bar the bottom is first knowable.</b> The code
        /// evaluates the condition at t and draws it with <c>offset = -2</c>, i.e. on the bottom bar
        /// two bars in the past — a picture that knew two bars of the future. This returns true at t
        /// and reads nothing after t. Nothing is shifted afterwards: there is no pivot-stamped marker
        /// to move (unlike the pivot divergences' DivergenceConfirmLag).</para>
        ///
        /// <para>Cipher B fed it its slow wave — Market Cipher B's, fitted on 2026-10-17 (hlc3, 9/12,
        /// SMA of 3) — not VuManChu's own (ohlc4, 9/13). Lows are raw candles' lows, as the code reads
        /// <c>low</c>. NaN anywhere in a comparison makes it false, as <c>na</c> does in Pine.</para>
        ///
        /// <para>Whether Market Cipher B itself has a gold dot is NOT established: none of the dated
        /// MCB charts examined on 2026-10-17/18 shows one, and tradelogic.pro (2026-02-24) states
        /// "The Golden Circle does not exist in the official Market Cipher B from CryptoFace." Every
        /// gold-dot description traced back to VuManChu.</para>
        /// </summary>
        public static bool[] Rule(double[] wt2, double[] low, double[] rsi, bool includeRsiDivergence)
        {
            int n = wt2.Length;
            var gold = new bool[n];
            var wtDiv  = BullishFractalDivergence(wt2, low, GoldWtDivergenceLevel, out var wtPrevBottom);
            bool[]? rsiDiv = includeRsiDivergence
                ? BullishFractalDivergence(rsi, low, GoldRsiDivergenceLevel, out _)
                : null;
            for (int t = 0; t < n; t++)
            {
                bool divergence = wtDiv[t] || (rsiDiv != null && rsiDiv[t]);
                int p = wtPrevBottom[t];   // wtLow_prev / lastRsi: the previous WT bottom's bar
                if (!divergence || p < 0) continue;
                gold[t] = wt2[p] <= GoldDeepLevel
                       && wt2[t] > GoldDeepLevel
                       && wt2[p] - wt2[t] <= -GoldMinRise
                       && rsi[p] < GoldRsiAtLow;
            }
            return gold;
        }

        /// <summary>
        /// VuManChu's <c>f_findDivs(src, _, botLimit, true)</c>, bullish half: per bar t, whether a
        /// bottom fractal at t−2 (at or below <paramref name="botLimit"/>) makes a regular bullish
        /// divergence with the previous qualifying bottom; and in <paramref name="prevBottom"/> the
        /// BAR of that previous bottom (the one <c>valuewhen(fractalBot, src[2], 0)[2]</c> reads), or
        /// −1 when there is none. Reads only bars at or before t.
        /// </summary>
        private static bool[] BullishFractalDivergence(double[] src, double[] low, double botLimit, out int[] prevBottom)
        {
            int n = src.Length;
            var signal = new bool[n];
            var isFractal = new bool[n];
            prevBottom = new int[n];
            int latestBottom = -1;   // bottom bar of the latest fractal confirmed at or before t−2
            for (int t = 0; t < n; t++)
            {
                if (t >= 2 && isFractal[t - 2]) latestBottom = t - 4;
                prevBottom[t] = latestBottom;
                if (t < 4) continue;
                int b = t - 2;
                isFractal[t] = src[t - 4] > src[b] && src[t - 3] > src[b] && src[b] < src[t - 1] && src[b] < src[t]
                               && src[b] <= botLimit;
                signal[t] = isFractal[t] && latestBottom >= 0
                            && low[b] < low[latestBottom] && src[b] > src[latestBottom];
            }
            return signal;
        }
    }
}
