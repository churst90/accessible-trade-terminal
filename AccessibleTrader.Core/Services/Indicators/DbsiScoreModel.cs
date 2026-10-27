namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// The Cipher DBSI score (2026-10-26): every rule found by reading Market Cipher DBSI's own numbers
    /// (an hourly, a fifteen-minute and a daily set of them, kept out of this repository), written once
    /// from the bull side. A line-for-line port of the study's <c>dbsi8/clean_dbsi.py</c>: the golden
    /// test replays that file's scores on every bar of the 35 digitised charts, so any change here must
    /// be made there first.
    ///
    /// <para>
    /// Each side is scored on side-flipped prices (the bears see −price, so a 0–100 oscillator reads
    /// 100 less the bulls'): "rising" always means rising for that side. A bar is in one of three
    /// states, and each is scored its own way:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Off</b> — no WaveTrend run (the side's wt1 is not above its 3-bar average): the votes, the
    /// gap's widening streak, the Q value (<see cref="QState"/>) and the Stoch RSI terms.</item>
    /// <item><b>Alive</b> — a run whose gap has widened on every bar since the cross: the votes, the run's
    /// level, 3 plus one per bar, the ARM the run carries (<see cref="Arm"/>), and, for bears, terms read
    /// at the cross (<see cref="BearCrossTerms"/>).</item>
    /// <item><b>Dead</b> — a run whose gap has stopped widening: the votes, the run's level, the streak, and
    /// a held step that raw Stoch RSI re-arms as the ARM is, holds or drops (<see cref="DeadStep"/>).</item>
    /// </list>
    /// <para>
    /// The votes (each 1 or 0 on the side's candles): V0 close above the close 2 bars back; V1 ALMA(9,
    /// 0.85, 6) rising; V2 the 40-bar stochastic (SMA2) rising; V3 WaveTrend wt1 rising; V4 wt1's gap
    /// over its 3-bar average widening; V5 the Stoch RSI vote (<see cref="StochRsiVotes"/>); V6 money
    /// flow rising; V7 the 81-bar stochastic (SMA2) rising. Votes = −5 + 2·(V1+V2+V3+V5+V6+V7) + V4;
    /// V0 is not scored by itself but gates several rules.
    /// </para>
    /// <para>
    /// "Raw Stoch RSI" (<c>sk</c>) is the unsmoothed 12-bar stochastic of RSI 12 on the side, rounded to
    /// 9 decimals so its 0 and 100 pins compare equal on both sides (100 − 100 is not always 0 in
    /// floating point); <c>K</c> is its 3-bar SMA, <c>D</c> K's 3-bar SMA. Sums and means follow
    /// numpy's pairwise order (<see cref="Sum"/>), so every bar matches the study bit for bit. Its SMA2
    /// (<c>s2</c>) decides the +1 of every re-arm: at most 60 for bulls, 40 for bears.
    /// </para>
    /// </summary>
    internal static class DbsiScoreModel
    {
        internal const int Floor = -5;
        internal const int RunCountCap = 10;
        internal const int RsiLength = 12;

        /// <summary>One side's run settings: the run's level (deep cross / not), and the step an ARM or a dead run re-arms to.</summary>
        internal readonly record struct RunSettings(int Level, int DeepLevel, int Step);
        internal static readonly RunSettings Bull = new(Level: 1, DeepLevel: 7, Step: 2);
        internal static readonly RunSettings Bear = new(Level: 1, DeepLevel: 2, Step: 3);

        /// <summary>ALMA(9, 0.85, 6)'s weights, as the study computes them (normalised Gaussian), to the last bit.</summary>
        private static readonly double[] AlmaWeights =
        {
            1.0464377916978786e-05, 0.00017208299852398428, 0.0018144405559293569, 0.012266700291604558, 0.053173224157302426,
            0.14778775942139383, 0.26336870336074847, 0.30093319532267887, 0.2204734295139016,
        };

        /// <summary>Bull and bear scores on these candles, with each side's money-flow vote (<see cref="MoneyFlowVote"/>).</summary>
        internal static (double[] Bull, double[] Bear) Score(double[] o, double[] h, double[] l, double[] c) =>
            Score(o, h, l, c, MoneyFlowVote(h, l, c), MoneyFlowVote(Neg(l), Neg(h), Neg(c)));

        /// <summary>
        /// The money-flow vote, 1 when Cipher B's money flow rose (its <c>McbMoneyFlowShare</c>: the SMA60 of
        /// hlc3 less its 5-bar mean, over the 5-bar mean size of that), on the candles given; computed here
        /// with numpy's sums so it matches the study on every bar.
        /// </summary>
        internal static double[] MoneyFlowVote(double[] h, double[] l, double[] c)
        {
            int n = c.Length;
            var tp = new double[n]; for (int i = 0; i < n; i++) tp[i] = (h[i] + l[i] + c[i]) / 3;
            var nn = Sub(tp, Sma(tp, 5));
            var sz = Sma(nn.Select(Math.Abs).ToArray(), 5);
            var share = new double[n];
            for (int i = 0; i < n; i++) share[i] = double.IsNaN(sz[i]) ? double.NaN : sz[i] < 1e-10 ? 0.0 : nn[i] / sz[i];
            var mf = Sma(share, 60);
            var v = new double[n];
            for (int i = 1; i < n; i++) v[i] = mf[i] - mf[i - 1] > 0 ? 1 : 0;   // NaN compares false
            return v;
        }

        /// <summary>
        /// Bull and bear scores (NaN while WaveTrend's gap or its previous value is undefined, and on
        /// bar 0). <paramref name="mfBull"/>/<paramref name="mfBear"/> are the money-flow votes (1 when
        /// the side's money flow rose).
        /// </summary>
        internal static (double[] Bull, double[] Bear) Score(double[] o, double[] h, double[] l, double[] c, double[] mfBull, double[] mfBear)
        {
            int n = c.Length;
            var (w1, w2) = WaveTrend(h, l, c);
            var rsi = Rsi(c, RsiLength);
            var sraw = Stoch(rsi, rsi, rsi, RsiLength);
            var kraw = Sma(sraw, 3);
            var draw = Sma(kraw, 3);
            var st11raw = Sma(Stoch(c, h, l, 11), 3);
            var st81raw = Sma(Stoch(c, h, l, 81), 2);
            var (srUp, srDown) = StochRsiVotes(c);
            var bull = ScoreSide(1, o, h, l, c, mfBull, srUp, w1, w2, rsi, sraw, kraw, draw, st11raw, st81raw);
            var bear = ScoreSide(-1, Neg(o), Neg(l), Neg(h), Neg(c), mfBear, srDown, w1, w2, rsi, sraw, kraw, draw, st11raw, st81raw);
            for (int i = 0; i < n; i++)
            {
                double gap = w1[i] - w2[i];
                if (i == 0 || double.IsNaN(gap) || double.IsNaN(w1[i - 1] - w2[i - 1])) { bull[i] = double.NaN; bear[i] = double.NaN; }
            }
            return (bull, bear);
        }

        /// <summary>One side, on that side's candles (<paramref name="O"/>…<paramref name="C"/>); the oscillators arrive unflipped.</summary>
        private static double[] ScoreSide(int side, double[] O, double[] H, double[] L, double[] C, double[] mf, double[] sr,
            double[] w1, double[] w2, double[] rsi, double[] sraw, double[] kraw, double[] draw, double[] st11raw, double[] st81raw)
        {
            int n = C.Length;
            bool bull = side == 1;
            var R = bull ? Bull : Bear;
            Func<double, double> f = bull ? x => x : x => 100 - x;               // flips a 0..100 oscillator to the side
            double[] Side(double[] x, double nanAs) { var r = new double[n]; for (int i = 0; i < n; i++) { double v = f(x[i]); r[i] = double.IsNaN(v) ? nanAs : v; } return r; }

            var sk = Side(sraw, 50); for (int i = 0; i < n; i++) sk[i] = Math.Round(sk[i], 9);   // raw Stoch RSI (pins compare equal)
            var K = Side(kraw, 50); var D = Side(draw, 50); var st11 = Side(st11raw, 50); var RS = Side(rsi, 50);
            var sw1 = new double[n]; var sg = new double[n];
            for (int i = 0; i < n; i++) { sw1[i] = side * w1[i]; sg[i] = side * (w1[i] - w2[i]); }
            var sk1 = LagSelf(sk, 1); var sk2 = LagSelf(sk, 2);
            var d2 = new double[n]; for (int i = 0; i < n; i++) d2[i] = sk[i] - sk2[i];           // raw Stoch RSI change over 2 bars
            var sk1v = LagV(sk, 1, 50); var sk2v = LagV(sk, 2, 50); var K1v = LagV(K, 1, 50);

            // ---- votes
            var k40 = Sma(Stoch(C, H, L, 40), 2); var (wS, _) = WaveTrend(H, L, C); var gS = Sub(wS, Sma(wS, 3));
            var a9 = Alma(C); var k81 = Sma(Stoch(C, H, L, 81), 2);
            var V = new bool[8][];
            V[0] = Above(C, Lag(C, 2)); V[1] = Rising(a9); V[2] = Rising(k40); V[3] = Rising(wS); V[4] = Rising(gS);
            V[5] = sr.Select(x => x == 1).ToArray(); V[6] = mf.Select(x => x == 1).ToArray(); V[7] = Rising(k81);
            var votes = new double[n];
            for (int i = 0; i < n; i++)
                votes[i] = -5 + 2 * (B(V[7][i]) + B(V[1][i]) + B(V[2][i]) + B(V[3][i]) + B(V[5][i]) + B(V[6][i])) + B(V[4][i]);
            var stt = new double[n];                                                // gap-widening streak term (not on alive bars)
            for (int i = 0, streak = 0; i < n; i++) { streak = V[4][i] ? streak + 1 : 0; stt[i] = Math.Max(Math.Min(streak, RunCountCap) - 1, 0); }
            var cp = new bool[n]; for (int i = 1; i < n; i++) cp[i] = C[i] > C[i - 1];   // close up (side)
            var w = WaveRun(w1, w2, side);
            var off = new bool[n]; var al = new bool[n]; var dead = new bool[n];
            for (int i = 0; i < n; i++) { off[i] = !w.Run[i]; al[i] = w.Alive[i]; dead[i] = w.Run[i] && !w.Alive[i]; }
            var q = QState(side, sk, sk2, K, kraw, f, st11, V, cp, w, off);

            // ---- shared by the states
            var q1 = LagB(q); var q2 = LagB(q, 2);
            var lb = new double[n]; for (int i = 0; i < n; i++) lb[i] = w.Deep[i] ? R.DeepLevel : R.Level;   // the run's level
            var s2 = new double[n]; for (int i = 0; i < n; i++) s2[i] = (sk[i] + sk1[i]) / 2;   // raw Stoch RSI's SMA2: an arm at or below
            double s2T = bull ? 60 : 40;                                                         // s2T takes +1
            var k1 = LagSelf(K, 1);
            var dK = new double[n]; var flatK = new bool[n];
            for (int i = 0; i < n; i++) { dK[i] = K[i] - k1[i]; flatK[i] = Math.Abs(dK[i]) <= 1e-4; }
            var cpo = new bool[n]; for (int i = 1; i < n; i++) cpo[i] = cp[i] && !cp[i - 1];  // close-up onset
            var cx = new int[n];                                                    // the run's cross bar
            for (int i = 1; i < n; i++) cx[i] = w.Run[i] && !w.Run[i - 1] ? i : cx[i - 1];
            var sw60 = new double[n]; for (int i = 0; i < n; i++) sw60[i] = B(sw1[Math.Max(cx[i], 1)] > 60);   // crossed with side WT above 60
            // the Q value: what an off bar in Q scores, and what a run crossing straight out of Q inherits
            var qv = new double[n];
            var qLow = bull ? OnsetLatch(q, sk.Select((x, i) => Math.Max(x, sk1v[i])).ToArray()) : OnsetLatch(q, K);
            for (int i = 0; i < n; i++)
                if (q[i]) qv[i] = bull ? 4 - B(qLow[i] > 18) - B(K[i] > 61 && dK[i] <= 6.25) : 4 - B(qLow[i] > 41);

            var s = new double[n];
            // ============================================================ OFF bars
            var Kl = Lag(K, 1);
            var v0on = new int[n];
            for (int i = 0, v0s = 0; i < n; i++)
            {
                v0s = V[0][i] ? v0s + 1 : 0;
                if (!off[i]) continue;
                bool nq = !q[i];
                // V0 just turned on (1-3 bars), raw Stoch RSI rising over 2 and high: bulls +2, bears +3
                double dw = i == 0 ? 0 : sw1[i] - sw1[i - 1]; if (double.IsNaN(dw)) dw = 0;
                if (nq && v0s >= 1 && v0s <= 3 && D[i] < 92 && w.Since[i] <= 99 && d2[i] > 0)
                    v0on[i] = bull ? (sk[i] >= 59 && K[i] > 65 && dw > -2.1 && st11[i] <= 80 ? 2 : 0) : (sk[i] >= 40 && sk[i] > K[i] && K[i] > -1 ? 3 : 0);
                double x = votes[i] + stt[i] + v0on[i] + qv[i];
                // fresh V0 on a close-up with stoch11 low and K in [25,60): bulls +3
                if (bull && w.Since[i] >= 2 && cp[i] && V[0][i] && st11[i] < 54 && Kl[i] >= 25 && Kl[i] < 60 && i > 0 && !V[0][i - 1]) x += 3;
                // no Q, V0 on, close down, raw Stoch RSI peaked last bar: a missed one-bar Q
                if (nq && V[0][i] && !cp[i] && sk[i] >= sk2[i] && sk[i] < sk1[i] && !V[3][i])
                    x += bull ? (sk1[i] > k1[i] ? 3 : 0) : (sk[i] <= K[i] ? 4 : 0);
                if (bull && nq && !V[5][i] && sk[i] > sk2[i] && st11[i] >= 80 && V[0][i]) x += 2;
                if (sk[i] >= 100) x += bull ? 2 : 3;                                 // raw Stoch RSI pinned at the top
                // Q just ended with raw Stoch RSI on its floor, on a raw floor double touch (0, up, 0)
                bool qend = nq && q1[i] && sk[i] <= 0 && K1v[i] <= 3.5;
                if (qend && sk2v[i] <= 0) x += V[5][i] ? 4 : 6;
                // a missed Q: raw Stoch RSI crossed K from a low K on a down close
                bool mx = nq && !q1[i] && sk[i] > K[i] && sk1v[i] <= K1v[i] && !cp[i] && K1v[i] < (bull ? 50 : 45) && w.Since[i] >= 2
                          && sk[i] > (bull ? -1 : 2) && sk2v[i] <= (bull ? 54 : 101);
                if (mx) x += 4 - (bull && sk[i] > 18 ? 1 : 0);
                // a one-bar Q that ended: its value carried one bar
                double dsr1 = i >= 2 ? sk[i - 1] - sk[i - 2] : 0, dkh = i >= 1 ? K[i] - K[i - 1] : 0;
                if (nq && q1[i] && !q2[i] && dsr1 > 6 && dkh > -5 && !qend && st11[i] <= 47) x += 4 - (bull && sk1v[i] > 18 ? 1 : 0) + 2 * (V[5][i] ? 0 : 1);
                x = Math.Max(Floor, x);
                if (bull)
                {
                    if (nq && !V[5][i] && sk[i] - sk2[i] > 4 && sk1[i] > 49 && sk[i] < 100 && st11[i] < 80 && K[i] <= 67.5 && V[0][i]) x += 2;
                    if (nq && cp[i] && (double.IsNaN(sw1[i]) ? -999 : sw1[i]) > 50 && d2[i] > 0 && sk[i] < 100 && v0on[i] <= 1 && V[5][i]) x += 2;
                    if (nq && d2[i] > 0 && !cp[i] && sk1[i] > 77.2 && w.Since[i] >= 2) x -= 1;   // up over 2 from high but close down
                }
                else if (nq && d2[i] > 0 && sk[i] - sk1[i] < -6 && sk2[i] > 32 && v0on[i] == 0) x -= 1;
                // raw Stoch RSI rising over 2: +3 just after a run when the score is low; bears also at any distance on a
                // close-up with K not dropping, raw Stoch RSI off its pin and no V0 bonus
                if (d2[i] > 0 && nq && ((x <= 0.5 && w.Since[i] <= 2) || (!bull && cp[i] && dK[i] > -3 && sk[i] < 100 && v0on[i] == 0))) x += 3;
                if (bull)
                {
                    if (nq && d2[i] > 0.07 && !cp[i] && K[i] > 60 && dK[i] < 8 && w.Since[i] <= 1 && V[5][i]) x -= 1;   // run just ended, close down
                    if (nq && sk[i] >= 100 && st11[i] <= 79 && d2[i] > 0 && w.Since[i] >= 1) x -= 2;   // rose to the pin, stoch11 not high
                }
                else if (nq && sk[i] >= 100 && v0on[i] >= 3) x -= 3;                 // pinned with the V0 bonus
                s[i] = x;                                                            // (not floored again)
            }

            // ============================================================ ALIVE bars: the ARM a run carries
            var (arm, kept) = Arm(bull, d2, s2, s2T, K, q, qv, al, R.Step);
            for (int i = 0; i < n; i++)
                if (al[i]) s[i] = votes[i] + lb[i] + 3 + w.Count[i] + arm[i] + sw60[i] - 2 * B(cpo[i] && flatK[i]);
            bool[]? zl = null; double[]? xo = null;
            if (!bull) BearCrossTerms(s, sw1, sg, sk, sk1, D, RS, st11, st81raw, sw60, w, al, cx, kept, out zl, out xo);
            for (int i = 0; i < n; i++) if (al[i]) s[i] = Math.Max(Floor, s[i]);
            if (!bull)
            {
                QTurn(s, sw1, q, off, al, kept);
                ContinuationCross(s, sw1, st11, RS, sk, w, al, zl!, xo!, kept);
            }

            // ============================================================ DEAD bars: the held step
            DeadStep(bull, s, votes, lb, stt, s2, s2T, d2, V, sw1, w, al, dead, R);
            var cp1 = LagB(cp);
            for (int i = 0; i < n; i++)
            {
                if (!dead[i]) continue;
                // after a down close, raw Stoch RSI reached its pin from below (over 2 bars) with K flat and the Stoch RSI vote on: −2
                if (!(i == 0 || cp1[i]) && sk[i] >= 100 && (i >= 2 ? sk[i - 2] : 100) < 100 && flatK[i] && V[5][i]) s[i] -= 2;
                if (!bull && sw60[i] == 1 && d2[i] < 0) s[i] += 1;                   // bears: a WT > 60 cross survives a raw Stoch RSI fall
            }
            return s;
        }

        /// <summary>
        /// The Q state ("bounce armed", off a run only): on when raw Stoch RSI's K is pinned at 0, or raw
        /// Stoch RSI has just crossed up through K from a low K, on a close-up bar at least 2 bars after the
        /// run ended (1 when V2 is on for bulls, V0 or V4 for bears); it holds while V0 stays on. Raw Stoch
        /// RSI rising over 2 bars with V0 off also turns it on, and it is never on while raw Stoch RSI fell
        /// over 2 bars.
        /// </summary>
        private static bool[] QState(int side, double[] sk, double[] sk2, double[] K, double[] kraw, Func<double, double> f,
            double[] st11, bool[][] V, bool[] cp, RunState w, bool[] off)
        {
            int n = sk.Length;
            bool bull = side == 1;
            var Kp = LagV(K, 1, 50); var skp = LagV(sk, 1, 50);
            var ks = new double[n]; var xraw = new bool[n];
            bool prevZone = false;
            for (int i = 0; i < n; i++)
            {
                double kq = f(kraw[i]); if (double.IsNaN(kq)) kq = 100;
                xraw[i] = sk[i] > K[i] && skp[i] <= Kp[i] && Kp[i] < (bull ? 64 : 50);   // raw crosses up through K from a low K
                bool xm = xraw[i], zone = false;
                if (bull) { zone = xm && Kp[i] >= 25 && st11[i] <= 54 && V[0][i]; xm &= !zone; }   // counts one bar later
                ks[i] = xm || (bull && prevZone) ? 0.0 : kq;
                if (i > 0 && xraw[i - 1]) ks[i] = 0.0;                                   // any raw cross arms the next bar too
                prevZone = zone;
            }
            var q = new bool[n]; bool a = false;
            for (int i = 0; i < n; i++)
            {
                bool early = bull ? V[2][i] : V[0][i] || V[4][i];
                if (w.Run[i]) a = false;
                else if (cp[i] && (w.Since[i] >= 2 || (w.Since[i] == 1 && early)) && ks[i] < 0.0001) a = true;
                else if (a && !V[0][i]) a = false;
                q[i] = a;
            }
            for (int i = 0; i < n; i++)
            {
                if (off[i] && !q[i] && sk[i] > sk2[i] && !V[0][i]) q[i] = true;          // raw Stoch RSI rising over 2 with V0 off
                if (sk[i] < sk2[i]) q[i] = false;                                         // never while it fell over 2
            }
            return q;
        }

        /// <summary>
        /// The ARM an alive run carries, and whether it was kept (never dropped since the cross). At the
        /// cross: 0 if raw Stoch RSI fell over 2 bars (bulls: did not rise), else the Q value of the bar
        /// before when Q was on, else the hidden arm (<see cref="ArmHidden"/>; bulls +1 with K at most 6).
        /// After it: dropped whenever raw Stoch RSI falls over 2, re-armed to the side's step (+1 when its
        /// SMA2 is at most <paramref name="s2T"/>) when it next rises.
        /// </summary>
        private static (double[] Arm, bool[] Kept) Arm(bool bull, double[] d2, double[] s2, double s2T, double[] K, bool[] q, double[] qv, bool[] al, int step)
        {
            int n = d2.Length;
            var hid = ArmHidden(d2, s2, s2T, step);
            var arm = new double[n]; var kept = new bool[n];
            double a = 0; bool kf = false;
            for (int i = 1; i < n; i++)
            {
                if (!al[i]) continue;
                if (!al[i - 1])
                {
                    a = (bull ? d2[i] <= 0 : d2[i] < 0) ? 0 : q[i - 1] ? qv[i - 1] : hid[i] + B(bull && K[i] <= 6);
                    kf = d2[i] >= 0;
                }
                else if (d2[i] < 0) { a = 0; kf = false; }
                else if (d2[i] > 0 && a == 0) a = step + B(s2[i] <= s2T);
                arm[i] = a; kept[i] = kf;
            }
            return (arm, kept);
        }

        /// <summary>The hidden arm, on every bar: 0 once raw Stoch RSI falls over 2 bars; when it next rises, the step (+1 when its SMA2 is at most <paramref name="s2T"/>), then held.</summary>
        private static double[] ArmHidden(double[] d2, double[] s2, double s2T, int step)
        {
            var o = new double[d2.Length]; double a = 0;
            for (int i = 0; i < d2.Length; i++)
            {
                if (d2[i] < 0) a = 0;
                else if (d2[i] > 0 && a == 0) a = step + B(s2[i] <= s2T);
                o[i] = a;
            }
            return o;
        }

        /// <summary>
        /// Bears' alive terms, added to <paramref name="s"/> on alive bars. Read at the cross: +4 with the
        /// 81-bar stochastic high and WT high the bar before; +2 on a widening jump with D and RSI low at the
        /// cross; +2 near the zero line (unless D was high at the cross). The zero-line cross: a run that
        /// starts near the zero line (after an off stretch that reached below it, raw Stoch RSI high with a WT
        /// jump, raw Stoch RSI off its floor with RSI up, a WT jump to just below zero, or, with no cross term,
        /// a WT jump of 10+ from as deep as -32 with a wide gap, raw Stoch RSI 95+ and RSI 52+) is worth 6, 4 on
        /// top of a cross term. The near-zero and zero-line points need the ARM kept; with no cross term at
        /// all, a wide gap near zero adds +2 twice.
        /// <paramref name="zl"/> is the zero-line cross, <paramref name="xo"/> the cross terms before it.
        /// </summary>
        private static void BearCrossTerms(double[] s, double[] sw1, double[] sg, double[] sk, double[] sk1, double[] D, double[] RS, double[] st11,
            double[] st81raw, double[] sw60, RunState w, bool[] al, int[] cx, bool[] kept, out bool[] zl, out double[] xo)
        {
            int n = s.Length;
            var sm = Sma(sw1, 3); var RS2 = LagSelf(RS, 2); var sw1p = LagSelf(sw1, 1);
            var xc = new double[n]; var nz = new double[n]; xo = new double[n];
            for (int i = 0; i < n; i++)
            {
                int x = cx[i], x1 = Math.Max(x, 1);
                bool hi = 100 - D[x] > 60;                                           // unflipped D at the cross
                double st81 = double.IsNaN(st81raw[x1]) ? 50 : 100 - st81raw[x1];
                xc[i] = 4 * B(st81 > 81.5 && sw1[x1 - 1] > 36.5) + 2 * B(sw1[i] - sm[i] > 13 && sw1[i] > -22 && D[x1] <= 34 && RS[x1] <= 58);
                nz[i] = 2 * B(!hi && sw1[i] > -2 && sw1[x] < 6);
                xo[i] = xc[i] + sw60[i] + nz[i];
            }
            // the zero-line cross, from the run's start
            zl = new bool[n];
            double pmin = double.PositiveInfinity; bool g1 = false, g2 = false;
            for (int i = 0; i < n; i++)
            {
                if (w.Run[i] && (i == 0 || !w.Run[i - 1]))
                {
                    g1 = sw1[i] >= -8.5 && sw1[i] <= 18 && pmin <= 999 && sk[i] >= 62 && st11[i] <= 54 && (i == 0 || sw1[i] - sw1[i - 1] >= 7);
                    g2 = sw1[i] >= 0 && sw1[i] <= 20 && pmin <= -1 && sg[i] >= 2;
                }
                if (!w.Run[i])
                {
                    if (i > 0 && w.Run[i - 1]) pmin = double.PositiveInfinity;
                    if (sw1[i] < pmin) pmin = sw1[i];                                   // a NaN never lowers it
                    g1 = g2 = false;
                }
                zl[i] = al[i] && w.Run[i] && (g1 || g2);
            }
            for (int i = 0; i < n; i++)
            {
                if (!al[i]) continue;
                int x = cx[i];
                double sgx = double.IsNaN(sg[x]) ? 0 : sg[x];
                bool c3 = sk1[x] <= 2.65 && RS[x] - RS2[x] > 7.2 && sw1[x] <= 40 && sw1[x] > -3 && xo[x] <= 0.5 && sk[x] >= 50;   // off the floor, RSI up
                bool c4 = sw1[x] > -18 && sw1[x] <= 0 && sgx > 10.69 && sw1[x] - sw1p[x] > 14 && sk[x] >= 90 && st11[x] > 45;  // jump to just below zero
                bool c5 = sw1[x] >= -32 && sw1[x] <= 5 && sw1[x] - sw1p[x] >= 10 && sk[x] >= 95 && sgx >= 10 && RS[x] >= 52 && xo[x] <= 0.5;  // deep jump, wide gap, RSI over 52
                if (c3 || c4 || c5) zl[i] = true;
            }
            for (int i = 0; i < n; i++)
            {
                if (!al[i]) continue;
                bool nox = !zl[i] && xo[i] == 0;                                     // no cross term at all
                double sgz = double.IsNaN(sg[i]) ? 0 : sg[i], g = sw1[i] - sm[i]; if (double.IsNaN(g)) g = 99;
                double wg = 2 * B(sg[i] > 17.34 && sw1[i] <= 27.5 && nox) + 2 * B(sw1[i] > -10 && sw1[i] <= 25 && sgz > 10.5 && g <= 17 && nox);
                s[i] += xc[i] + ((zl[i] ? (xo[i] > 0.5 ? 4 : 6) : 0) + nz[i]) * B(kept[i]) + wg;
            }
        }

        /// <summary>Bears: a Q bar with side WT turning up near the zero line gives +2, held through the Q and the alive run after it (alive bars only while the ARM is kept).</summary>
        private static void QTurn(double[] s, double[] sw1, bool[] q, bool[] off, bool[] al, bool[] kept)
        {
            bool on = false;
            for (int i = 0; i < s.Length; i++)
            {
                double sp = i == 0 ? sw1[0] : sw1[i - 1], dw = sw1[i] - sp;
                if (off[i] && q[i] && dw > 4.2 && sp > -3 && sp <= 18) on = true;
                else if (!((off[i] && q[i]) || al[i])) on = false;
                if (on && (off[i] || kept[i])) s[i] += 2;
            }
        }

        /// <summary>
        /// Bears' continuation zero-cross: an alive run whose previous bar, and the whole off stretch before
        /// it, had side WT above 0, starting low with RSI up and raw Stoch RSI on its floor 2 bars back:
        /// +6 (+4 when a cross term is on) on its alive bars while the ARM is kept, after the off/alive merge.
        /// </summary>
        private static void ContinuationCross(double[] s, double[] sw1, double[] st11, double[] RS, double[] sk, RunState w, bool[] al, bool[] zl, double[] xo, bool[] kept)
        {
            double pm = double.PositiveInfinity; bool on = false;
            for (int i = 3; i < s.Length; i++)
            {
                if (!w.Run[i])
                {
                    if (w.Run[i - 1]) pm = double.PositiveInfinity;
                    if (sw1[i] < pm) pm = sw1[i];
                    on = false; continue;
                }
                if (!w.Run[i - 1])
                    on = al[i] && !zl[i] && sw1[i - 1] > 0 && pm > 0 && sw1[i] <= 18 && st11[i] <= 52 && RS[i] - RS[i - 2] > 5 && sk[i - 2] <= 8;
                if (on && al[i] && kept[i]) s[i] += xo[i] > 0.5 ? 4 : 6;
            }
        }

        /// <summary>
        /// Dead-run bars, in order: votes + the run's level + the streak + a held step. On the break bar the
        /// alive bar's extras (less its count) carry over minus 3, unless raw Stoch RSI fell over 2 bars.
        /// After it the step is dropped when raw Stoch RSI fell over 2, re-armed as the ARM is (the side's
        /// step, +1 when its SMA2 is at most <paramref name="s2T"/>) when it rose, and held when flat (a flat
        /// bar with no step left reads −2 if the Stoch RSI vote is on). Bears also carry a near-zero flag worth
        /// +2: set when side WT climbs by 5 or more into −15..10 with the gap and ALMA votes on and raw Stoch
        /// RSI not falling (not on a flat bar reading −2), held until raw Stoch RSI falls.
        /// </summary>
        private static void DeadStep(bool bull, double[] s, double[] votes, double[] lb, double[] stt, double[] s2, double s2T, double[] d2, bool[][] V,
            double[] sw1, RunState w, bool[] al, bool[] dead, RunSettings R)
        {
            bool zf = false;
            for (int i = 1; i < s.Length; i++)
            {
                if (!dead[i]) { zf = false; continue; }
                double rest = votes[i] + lb[i] + stt[i];
                bool tz = !bull && d2[i] >= 0 && V[4][i] && V[1][i] && -15 <= sw1[i] && sw1[i] <= 10 && sw1[i] - sw1[i - 1] >= 5;
                if (al[i - 1])
                {
                    zf = tz;
                    s[i] = d2[i] >= 0 ? Math.Max(Floor, votes[i] + stt[i] + (s[i - 1] - votes[i - 1] - w.Count[i - 1]) - 3 + 2 * B(zf)) : Math.Max(Floor, rest);
                    continue;
                }
                double dp = s[i - 1] - (votes[i - 1] + lb[i - 1] + stt[i - 1]) - 2 * B(zf), dv;
                if (d2[i] < 0) dv = 0;
                else if (d2[i] > 0) dv = Math.Max(dp, R.Step + B(s2[i] <= s2T));
                else dv = dp > 0 ? dp : -2 * B(V[5][i]);
                if (tz && dv >= 0) zf = true;
                else if (d2[i] < 0) zf = false;
                s[i] = Math.Max(Floor, rest + dv + 2 * B(zf));
            }
        }

        // ---------------------------------------------------------------- the Stoch RSI vote (V5)

        /// <summary>
        /// The Stoch RSI (12, 12, 3) up and down votes. Primary: the SMA2 of raw Stoch RSI against itself
        /// 2 bars earlier. Where that is exactly flat: K's slope, then K's side of 50; and a flat K on the
        /// wrong side of 50 still votes when raw Stoch RSI stayed off its pin and RSI moved that way a bar ago.
        /// </summary>
        internal static (double[] Up, double[] Down) StochRsiVotes(double[] c)
        {
            int n = c.Length;
            var r = Rsi(c, RsiLength); var sRaw = Stoch(r, r, r, RsiLength); var k = Sma(sRaw, 3);
            var up = new double[n]; var dn = new double[n];
            double S(int j) => j < 0 || double.IsNaN(sRaw[j]) ? 50 : sRaw[j];
            const double t = 1e-6;
            for (int i = 0; i < n; i++)
            {
                double d = i > 0 ? k[i] - k[i - 1] : double.NaN;
                if (Math.Abs(d) <= 1e-9) d = 0;
                bool tie = double.IsNaN(d) || d == 0;
                bool u = d > 0 || (tie && k[i] > 50), dd = d < 0 || (tie && k[i] < 50);
                double a = (S(i) + S(i - 1)) - (S(i - 2) + S(i - 3));
                if (Math.Abs(a) > 1e-9 && Math.Abs(S(i) - S(i - 3)) > 0) { u = a > 0; dd = a < 0; }
                double s1 = i >= 1 ? sRaw[i - 1] : double.NaN, s2 = i >= 2 ? sRaw[i - 2] : double.NaN;
                double dr1 = i >= 2 ? r[i - 1] - r[i - 2] : double.NaN;
                u |= tie && k[i] < 50 && s1 > t && s2 > t && dr1 > 0;
                dd |= tie && k[i] > 50 && s1 < 100 - t && s2 < 100 - t && dr1 < 0;
                up[i] = u ? 1 : 0; dn[i] = dd ? 1 : 0;
            }
            return (up, dn);
        }

        // ---------------------------------------------------------------- wave run

        /// <summary>
        /// One side's WaveTrend run. Run: the side's gap (wt1 − wt2) above 0, from its cross. Alive: the gap
        /// widened on every bar since the cross. Deep: side wt1 below −60 at the cross. Count: bars since the
        /// cross (up to 10) while alive. Since: on bars off a run, bars since the last run ended. A run
        /// already going on bar 0 is no run.
        /// </summary>
        internal sealed record RunState(bool[] Run, bool[] Alive, bool[] Deep, int[] Count, int[] Since);

        internal static RunState WaveRun(double[] w1, double[] w2, int side)
        {
            int n = w1.Length;
            var r = new RunState(new bool[n], new bool[n], new bool[n], new int[n], new int[n]);
            int cross = -1, last = -1; bool alive = false;
            for (int i = 1; i < n; i++)
            {
                double g = side * (w1[i] - w2[i]), b = side * (w1[i - 1] - w2[i - 1]);
                if (!(g > 0))
                {
                    if (cross >= 0) last = i;
                    cross = -1; r.Since[i] = last >= 0 ? i - last : 0; continue;
                }
                if (!(b > 0)) { cross = i; alive = true; }
                if (cross < 0) continue;
                int age = i - cross;
                if (age > 0 && alive && g <= b) alive = false;
                r.Run[i] = true; r.Deep[i] = side * w1[cross] < -60; r.Alive[i] = alive; r.Count[i] = alive ? Math.Min(age, RunCountCap) : 0;
            }
            return r;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>x's value on the first bar of each Q episode, held for the episode (0 outside Q).</summary>
        private static double[] OnsetLatch(bool[] q, double[] x)
        {
            var o = new double[q.Length];
            for (int i = 0; i < q.Length; i++) o[i] = q[i] && (i == 0 || !q[i - 1]) ? x[i] : q[i] ? o[i - 1] : 0;
            return o;
        }

        private static int B(bool b) => b ? 1 : 0;
        private static double[] Neg(double[] x) { var r = new double[x.Length]; for (int i = 0; i < x.Length; i++) r[i] = -x[i]; return r; }
        private static double[] Sub(double[] a, double[] b) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i]; return r; }
        private static double[] Nan(int n) { var r = new double[n]; Array.Fill(r, double.NaN); return r; }

        /// <summary>Shifted k bars, NaN-padded.</summary>
        private static double[] Lag(double[] x, int k) { var r = Nan(x.Length); for (int i = k; i < x.Length; i++) r[i] = x[i - k]; return r; }
        /// <summary>Shifted k bars; the first k bars keep their own value.</summary>
        private static double[] LagSelf(double[] x, int k) { var r = (double[])x.Clone(); for (int i = k; i < x.Length; i++) r[i] = x[i - k]; return r; }
        /// <summary>Shifted k bars, padded with <paramref name="v"/>.</summary>
        private static double[] LagV(double[] x, int k, double v) { var r = new double[x.Length]; for (int i = 0; i < x.Length; i++) r[i] = i >= k ? x[i - k] : v; return r; }
        private static bool[] LagB(bool[] x, int k = 1) { var r = new bool[x.Length]; for (int i = k; i < x.Length; i++) r[i] = x[i - k]; return r; }
        /// <summary>a − b above 0 (NaN compares false).</summary>
        private static bool[] Above(double[] a, double[] b) { var r = new bool[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i] > 0; return r; }
        private static bool[] Rising(double[] x) => Above(x, Lag(x, 1));

        /// <summary>
        /// numpy's sum of x[from..from+len) (its pairwise_sum): straight for under 8 values; up to 128, 8
        /// running partial sums combined pairwise, then the remainder; above 128, the two halves (split on a
        /// multiple of 8) summed the same way and added.
        /// </summary>
        internal static double Sum(double[] x, int from, int len)
        {
            if (len > 128)
            {
                int half = len / 2; half -= half % 8;
                return Sum(x, from, half) + Sum(x, from + half, len - half);
            }
            if (len < 8)
            {
                double s = 0;
                for (int i = 0; i < len; i++) s += x[from + i];
                return s;
            }
            var r = new double[8];
            for (int j = 0; j < 8; j++) r[j] = x[from + j];
            int k = 8;
            for (; k < len - len % 8; k += 8)
                for (int j = 0; j < 8; j++) r[j] += x[from + k + j];
            double res = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
            for (; k < len; k++) res += x[from + k];
            return res;
        }

        /// <summary>SMA; NaN while the window holds a NaN.</summary>
        internal static double[] Sma(double[] x, int len)
        {
            var r = Nan(x.Length);
            for (int i = len - 1; i < x.Length; i++)
            {
                bool ok = true;
                for (int j = i - len + 1; j <= i && ok; j++) ok = !double.IsNaN(x[j]);
                if (ok) r[i] = Sum(x, i - len + 1, len) / len;
            }
            return r;
        }

        /// <summary>Pine's EMA/RMA: seeded with the mean of the first <paramref name="len"/> values from the first defined one; a NaN or infinite input after that leaves a gap.</summary>
        private static double[] Smoothed(double[] x, int len, double a)
        {
            int n = x.Length;
            var r = Nan(n);
            int s = Array.FindIndex(x, v => !double.IsNaN(v));
            if (s < 0 || s + len > n) return r;
            var seed = x.Skip(s).Take(len).Where(v => !double.IsNaN(v)).ToArray();
            if (seed.Length == 0) return r;
            double e = Sum(seed, 0, seed.Length) / seed.Length;
            r[s + len - 1] = e;
            for (int i = s + len; i < n; i++)
            {
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i])) continue;
                e = a * x[i] + (1 - a) * e;
                r[i] = e;
            }
            return r;
        }

        internal static double[] Ema(double[] x, int len) => Smoothed(x, len, 2.0 / (len + 1));
        private static double[] Rma(double[] x, int len) => Smoothed(x, len, 1.0 / len);

        /// <summary>The n-bar stochastic of c against h/l (undefined values skipped); a flat window is undefined.</summary>
        internal static double[] Stoch(double[] c, double[] h, double[] l, int len)
        {
            int n = c.Length;
            var st = Nan(n);
            for (int i = len - 1; i < n; i++)
            {
                double hh = double.NegativeInfinity, ll = double.PositiveInfinity;
                for (int j = i - len + 1; j <= i; j++) { if (h[j] > hh) hh = h[j]; if (l[j] < ll) ll = l[j]; }
                if (hh > ll) st[i] = 100 * (c[i] - ll) / (hh - ll);
            }
            return st;
        }

        /// <summary>Wilder's RSI, its averages seeded as Pine's <c>ta.rma</c>.</summary>
        internal static double[] Rsi(double[] c, int len)
        {
            int n = c.Length;
            var up = Nan(n); var dn = Nan(n);
            for (int i = 1; i < n; i++) { double d = c[i] - c[i - 1]; up[i] = Math.Max(d, 0); dn[i] = Math.Max(-d, 0); }
            var au = Rma(up, len); var ad = Rma(dn, len);
            var r = new double[n];
            for (int i = 0; i < n; i++) r[i] = 100 - 100 / (1 + au[i] / ad[i]);
            return r;
        }

        /// <summary>WaveTrend (9, 12, 3): wt1 and its 3-bar SMA.</summary>
        internal static (double[] W1, double[] W2) WaveTrend(double[] h, double[] l, double[] c)
        {
            int n = c.Length;
            var ap = new double[n];
            for (int i = 0; i < n; i++) ap[i] = (h[i] + l[i] + c[i]) / 3;
            var esa = Ema(ap, 9);
            var dev = new double[n]; for (int i = 0; i < n; i++) dev[i] = Math.Abs(ap[i] - esa[i]);
            var dd = Ema(dev, 9);
            var ci = new double[n]; for (int i = 0; i < n; i++) ci[i] = (ap[i] - esa[i]) / (0.015 * dd[i]);
            var w1 = Ema(ci, 12);
            return (w1, Sma(w1, 3));
        }

        /// <summary>ALMA(9, 0.85, 6) of c.</summary>
        private static double[] Alma(double[] c)
        {
            int n = c.Length, m = AlmaWeights.Length;
            var r = Nan(n); var p = new double[m];
            for (int i = m - 1; i < n; i++)
            {
                for (int j = 0; j < m; j++) p[j] = c[i - m + 1 + j] * AlmaWeights[j];
                r[i] = Sum(p, 0, m);
            }
            return r;
        }
    }
}
