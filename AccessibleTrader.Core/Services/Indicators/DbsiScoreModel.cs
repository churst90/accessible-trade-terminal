namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// The Cipher DBSI score (2026-10-26): every rule found by reading Market Cipher DBSI's own numbers
    /// (an hourly, a fifteen-minute and a daily set of them, kept out of this repository), written once
    /// from the bull side. A line-for-line port of the study's <c>dbsi8/clean_dbsi.py</c> (since 2026-10-29
    /// its successors, now <c>zu_hs66e.py</c>, ported 2026-11-02 at Cody's request: the Q value, the bears' EMA
    /// cross, the hidden arm, the deep-run flag, the flat-price rules, the floor-touch re-arms and the pin terms): the golden
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
    /// gap's widening streak, the Q value (<see cref="QState"/>) and the Stoch RSI floor-touch, missed-Q and pin
    /// terms; with V0 on and raw Stoch RSI not falling over 2 bars, only the votes, the streak and the hidden
    /// arm (the ARM a run starting on this bar would carry, <see cref="ArmHidden"/>) or the Q value if larger.</item>
    /// <item><b>Alive</b> — a run whose gap has widened on every bar since the cross: the votes, the run's
    /// level, 3 plus one per bar, the ARM the run carries, and, for bears, terms read at the cross.</item>
    /// <item><b>Dead</b> — a run whose gap has stopped widening: the votes, the run's level, the streak, and
    /// a held step that raw Stoch RSI re-arms as the ARM is, holds or drops.</item>
    /// </list>
    /// <para>
    /// The votes (each 1 or 0 on the side's candles): V0 close above the close 2 bars back; V1 ALMA(9,
    /// 0.85, 6) rising; V2 the 40-bar stochastic (SMA2) rising; V3 WaveTrend wt1 rising; V4 wt1's gap
    /// over its 3-bar average widening; V5 the Stoch RSI vote (<see cref="StochRsiVotes"/>); V6 money
    /// flow rising (<see cref="MoneyFlowVote"/>); V7 the 81-bar stochastic (SMA2) rising. On an exactly flat
    /// bar V0, V1, V2, V6 and V7 keep their last direction. Votes = −5 + 2·(V1+V2+V3+V5+V6+V7) + V4;
    /// V0 is not scored by itself but gates several rules.
    /// </para>
    /// <para>
    /// "Raw Stoch RSI" (<c>sk</c>) is the unsmoothed 12-bar stochastic of RSI 12 on the side, rounded to
    /// 9 decimals so its 0 and 100 pins compare equal on both sides (100 − 100 is not always 0 in
    /// floating point), and held through a flat window after the warm-up; <c>K</c> is its 3-bar SMA,
    /// <c>D</c> K's 3-bar SMA. The arithmetic is Pine's, as the study found it exact against TradingView:
    /// an SMA is the exactly rounded sum over the length (<see cref="Fsum"/>), the RMA is
    /// (x + (n − 1)·prev) / n, money flow's SMAs are running sums; numpy's pairwise order
    /// (<see cref="Sum"/>) only where the study takes a numpy mean. Every bar matches the study bit for bit.
    /// Its SMA2 (<c>s2</c>) decides the +1 of every re-arm: at most 60 for bulls, 40 for bears.
    /// </para>
    /// </summary>
    internal static class DbsiScoreModel
    {
        internal const int Floor = -5;
        internal const int RunCountCap = 10;
        internal const int RsiLength = 12;
        private const int StreakCap = 100000;

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

        /// <summary>
        /// Bull and bear scores on these candles (NaN while WaveTrend's gap or its previous value is
        /// undefined, and on bar 0).
        /// </summary>
        internal static (double[] Bull, double[] Bear) Score(double[] o, double[] h, double[] l, double[] c)
        {
            int n = c.Length;
            var mfBull = MoneyFlowVote(h, l, c, 1); var mfBear = MoneyFlowVote(h, l, c, -1);
            var (w1, w2) = WaveTrend(h, l, c);
            var e5 = Ema(c, 5); var e11 = Ema(c, 11);
            var e511 = new double[n]; for (int i = 0; i < n; i++) e511[i] = e5[i] - e11[i];
            var rsi = Rsi(c, RsiLength);
            var sraw = Stoch(rsi, rsi, rsi, RsiLength, ofItself: true);
            var kraw = Sma(sraw, 3); var draw = Sma(kraw, 3);                      // before the flat-window hold
            int fv = Array.FindIndex(sraw, v => !double.IsNaN(v)); if (fv < 0) fv = n;
            var nanS = new bool[n];                                                // a flat Stoch RSI window after the warm-up
            for (int i = 0; i < n; i++) nanS[i] = double.IsNaN(sraw[i]) && i > fv;
            for (int i = 0; i < n; i++) if (nanS[i]) sraw[i] = sraw[i - 1];       // ... holds the last value
            var st81raw = Sma(Stoch(c, h, l, 81), 2);
            var (srUp, srDown) = StochRsiVotes(c);
            var pb = FirstPass(1, o, h, l, c, c, h, l, mfBull, srUp, w1, w2, e511, sraw, kraw, draw, nanS);
            var pr = FirstPass(-1, Neg(o), Neg(l), Neg(h), Neg(c), c, h, l, mfBear, srDown, w1, w2, e511, sraw, kraw, draw, nanS);
            var bull = SecondPass(1, pb, st81raw);
            var bear = SecondPass(-1, pr, st81raw);
            for (int i = 0; i < n; i++)
            {
                double gap = w1[i] - w2[i];
                if (i == 0 || double.IsNaN(gap) || double.IsNaN(w1[i - 1] - w2[i - 1])) { bull[i] = double.NaN; bear[i] = double.NaN; }
            }
            return (bull, bear);
        }

        /// <summary>
        /// The money-flow vote on the (unflipped) candles: Cipher B's money flow (the 60-bar mean of hlc3 less its
        /// 5-bar mean, over the 5-bar mean size of that; 0 where that size is 0), each mean a running sum as Pine
        /// keeps it. Bulls when it rose, bears when it fell; an exact tie keeps the last vote.
        /// </summary>
        internal static bool[] MoneyFlowVote(double[] h, double[] l, double[] c, int side)
        {
            int n = c.Length;
            var tp = new double[n]; for (int i = 0; i < n; i++) tp[i] = (h[i] + l[i] + c[i]) / 3;
            var nn = Sub(tp, RunningSma(tp, 5));
            var sz = RunningSma(nn.Select(Math.Abs).ToArray(), 5);
            var share = new double[n];
            for (int i = 0; i < n; i++) share[i] = double.IsNaN(sz[i]) ? double.NaN : sz[i] == 0 ? 0.0 : nn[i] / sz[i];
            var mf = RunningSma(share, 60);
            var v = new bool[n];
            for (int i = 1; i < n; i++)
                v[i] = mf[i] == mf[i - 1] ? v[i - 1] : side == 1 ? mf[i] > mf[i - 1] : mf[i] < mf[i - 1];   // NaN compares false
            return v;
        }

        /// <summary>One side's arrays from the first pass: oscillators on the side, the votes, the run and Q.</summary>
        private sealed class SideState
        {
            public double[] Sk = null!, K = null!, D = null!, Sw1 = null!, Ex = null!, Sg = null!, Sk1 = null!, Sk2 = null!, D2 = null!, WS2 = null!, Xr = null!;
            public bool[][] V = null!;
            public double[] Votes = null!, Stt = null!;
            public bool[] Cp = null!, Off = null!, Al = null!, Dead = null!, Q = null!, Hair = null!, Dipv = null!;
            public RunState W = null!;
        }

        /// <summary>
        /// One side, on that side's candles (<paramref name="O"/>…<paramref name="C"/>); <paramref name="c"/>,
        /// <paramref name="h"/>, <paramref name="l"/> are the unflipped ones (the bears' 40- and 81-bar stochastics
        /// are the bulls' negated); the oscillators arrive unflipped.
        /// </summary>
        private static SideState FirstPass(int side, double[] O, double[] H, double[] L, double[] C, double[] c, double[] h, double[] l,
            bool[] mf, double[] sr, double[] w1, double[] w2, double[] e511, double[] sraw, double[] kraw, double[] draw, bool[] nanS)
        {
            int n = C.Length;
            bool bull = side == 1;
            Func<double, double> f = bull ? x => x : x => 100 - x;               // flips a 0..100 oscillator to the side
            double[] Side(double[] x, double nanAs) { var r = new double[n]; for (int i = 0; i < n; i++) { double v = f(x[i]); r[i] = double.IsNaN(v) ? nanAs : v; } return r; }

            var sk = Side(sraw, 50); for (int i = 0; i < n; i++) sk[i] = Math.Round(sk[i], 9);   // raw Stoch RSI (pins compare equal)
            var K = Side(kraw, 50); var D = Side(draw, 50);
            var sw1 = new double[n]; var ex = new double[n]; var sg = new double[n];
            for (int i = 0; i < n; i++) { sw1[i] = side * w1[i]; ex[i] = side * e511[i]; sg[i] = side * (w1[i] - w2[i]); }
            var sk1 = LagSelf(sk, 1); var sk2 = LagSelf(sk, 2);
            var d2 = new double[n]; for (int i = 0; i < n; i++) d2[i] = sk[i] - sk2[i];           // raw Stoch RSI change over 2 bars
            var x = Side(sraw, 50); var x1 = LagSelf(x, 1); var x2 = LagSelf(x, 2);              // unrounded raw Stoch RSI, side
            var r0 = new double[n]; for (int i = 0; i < n; i++) r0[i] = double.IsNaN(sraw[i]) ? 50 : sraw[i];
            var r1 = LagSelf(r0, 1); var r2 = LagSelf(r0, 2);
            static bool Inn(double v) => v > 0 && v < 100;
            var hair = new bool[n]; var dipv = new bool[n];
            for (int i = 0; i < n; i++)
            {
                bool pin = sk[i] >= 100 || sk[i] <= 0;
                // flat on the pin but a hair inside it for 2 bars, or two bars exactly on it then a hair inside on a flat close
                hair[i] = (d2[i] == 0 && pin && Inn(x[i]) && Inn(x1[i]) && sk1[i] == sk[i] && !Inn(x2[i]))
                       || (d2[i] == 0 && pin && Inn(x[i]) && x1[i] == x2[i] && !Inn(x2[i]) && sk1[i] == sk[i] && C[i] == (i >= 1 ? C[i - 1] : C[i]));
                if (hair[i]) d2[i] = sk[i] <= 0 ? 1e-12 : -1e-12;                   // ... reads as leaving the pin
                // a pin dip reads the 2-bar sum's change only when the older sum was rounded, or from a hair inside the pin to a hair past it
                bool dip = d2[i] == 0 && pin && sk1[i] != sk[i] && x[i] != x2[i];
                double s1 = r1[i] + r2[i], bb = s1 - r2[i];
                bool ex1 = ((r2[i] - (s1 - bb)) + (r1[i] - bb)) == 0;
                dipv[i] = dip && (!ex1 || (Inn(x2[i]) && (x[i] > 100 || x[i] < 0) && ((r0[i] + r1[i]) - (r1[i] + r2[i])) != 0));
                if (dipv[i]) d2[i] = side * (double)Math.Sign((r0[i] + r1[i]) - s1) * 1e-12;
            }

            // ---- votes
            double[] k40, kk;
            if (bull) { k40 = Sma(Stoch(C, H, L, 40), 2); kk = Sma(Stoch(C, H, L, 81), 2); }
            else { k40 = Neg(Sma(Stoch(c, h, l, 40), 2)); kk = Neg(Sma(Stoch(c, h, l, 81), 2)); }
            var (wS, wS2) = WaveTrend(H, L, C); var gS = Sub(wS, Sma(wS, 3)); var a9 = Alma(C);
            var v0 = new bool[n];                                                    // close above the close 2 bars back; equal keeps the last
            for (int i = 0, dir = 0; i < n; i++)
            {
                double d = i >= 2 ? C[i] - C[i - 2] : double.NaN;
                if (double.IsNaN(d)) dir = 0; else if (d != 0) dir = Math.Sign(d);
                v0[i] = dir > 0;
            }
            var v5 = new bool[n]; for (int i = 0; i < n; i++) v5[i] = nanS[i] ? v5[i - 1] : sr[i] == 1;   // held through a flat Stoch RSI window
            var V = new[] { v0, HeldRising(a9), HeldRising(k40), Rising(wS), Rising(gS), v5, mf, HeldRising(kk) };
            var votes = new double[n];
            for (int i = 0; i < n; i++)
                votes[i] = -5 + 2 * (B(V[7][i]) + B(V[1][i]) + B(V[2][i]) + B(V[3][i]) + B(V[5][i]) + B(V[6][i])) + B(V[4][i]);
            var stt = new double[n];                                                // gap-widening streak term (not on alive bars)
            for (int i = 0, streak = 0; i < n; i++) { streak = V[4][i] ? streak + 1 : 0; stt[i] = Math.Max(Math.Min(streak, StreakCap) - 1, 0); }
            var cp = HeldRising(C);                                                  // close up (side); equal keeps the last
            var w = WaveRun(w1, w2, side);
            var off = new bool[n]; var al = new bool[n]; var dead = new bool[n];
            for (int i = 0; i < n; i++) { off[i] = !w.Run[i]; al[i] = w.Alive[i]; dead[i] = w.Run[i] && !w.Alive[i]; }
            var q = QState(bull, sk, K, kraw, f, kk, v0, cp, d2, hair, w, off);
            return new SideState
            {
                Sk = sk, K = K, D = D, Sw1 = sw1, Ex = ex, Sg = sg, Sk1 = sk1, Sk2 = sk2, D2 = d2, WS2 = wS2, Xr = x, V = V, Votes = votes,
                Stt = stt, Cp = cp, Off = off, Al = al, Dead = dead, Q = q, Hair = hair, Dipv = dipv, W = w,
            };
        }

        /// <summary>
        /// The Q state ("bounce armed", off a run only): on when raw Stoch RSI's K is pinned at 0, or raw
        /// Stoch RSI has just crossed up through K from a low K (the bar after counts too; a bull cross with K at
        /// least 30, the 81-bar stochastic at most 40 and V0 on counts only one bar later), on a close-up bar;
        /// it holds while V0 stays on. Raw Stoch RSI rising over 2 bars with V0 off also turns it on, it is never
        /// on while raw Stoch RSI fell over 2 bars, and a Q a hair rise started stays on through the flat bars after it.
        /// </summary>
        private static bool[] QState(bool bull, double[] sk, double[] K, double[] kraw, Func<double, double> f, double[] kk,
            bool[] v0, bool[] cp, double[] d2, bool[] hair, RunState w, bool[] off)
        {
            int n = sk.Length;
            var Kp = LagV(K, 1, 50); var skp = LagV(sk, 1, 50);
            var ks = new double[n]; var xraw = new bool[n]; var zone = new bool[n];
            for (int i = 0; i < n; i++)
            {
                double kq = f(kraw[i]); if (double.IsNaN(kq)) kq = 100;
                xraw[i] = sk[i] > K[i] && skp[i] <= Kp[i] && Kp[i] < (bull ? 64 : 50);   // raw crosses up through K from a low K
                bool xm = xraw[i];
                if (bull) { zone[i] = xm && Kp[i] >= 30 && kk[i] <= 40 && v0[i]; xm &= !zone[i]; }
                ks[i] = xm ? 0.0 : kq;
                if (bull && i > 0 && zone[i - 1]) ks[i] = 0.0;
                if (i > 0 && xraw[i - 1] && ks[i] != 0.0) ks[i] = 0.0;                    // any raw cross arms the next bar too
            }
            var q = new bool[n]; bool a = false;
            for (int i = 0; i < n; i++)
            {
                if (w.Run[i]) a = false;
                else if (cp[i] && ks[i] < 0.0001) a = true;
                else if (a && !v0[i]) a = false;
                q[i] = a;
            }
            for (int i = 0; i < n; i++)
            {
                if (off[i] && !q[i] && d2[i] > 0 && !v0[i]) q[i] = true;                  // raw Stoch RSI rising over 2 with V0 off
                if (d2[i] < 0) q[i] = false;                                              // never while it fell over 2
            }
            bool held = false;
            for (int i = 1; i < n; i++)
            {
                if (q[i] && hair[i]) held = true;
                else if (held && q[i - 1] && !q[i] && off[i] && d2[i] == 0 && !v0[i]) q[i] = true;
                else if (!q[i]) held = false;
            }
            return q;
        }

        /// <summary>The scores of one side from its first-pass arrays: off, alive and dead bars in turn.</summary>
        private static double[] SecondPass(int side, SideState P, double[] st81raw)
        {
            var sk = P.Sk; var K = P.K; var D = P.D; var sw1 = P.Sw1; var ex = P.Ex; var sg = P.Sg; var sk1 = P.Sk1; var sk2 = P.Sk2;
            var d2 = P.D2; var wS2 = P.WS2; var x = P.Xr; var V = P.V; var votes = P.Votes; var stt = P.Stt; var cp = P.Cp;
            var off = P.Off; var al = P.Al; var dead = P.Dead; var q = P.Q; var hair = P.Hair; var dipv = P.Dipv; var w = P.W;
            var run = w.Run; var cnt = w.Count; var since = w.Since; var deep = w.Deep;
            var v0 = V[0]; var v5 = V[5];
            int n = sk.Length;
            bool bull = side == 1;
            var R = bull ? Bull : Bear; int WB = R.Step;
            double s2T = bull ? 60 : 40;                                             // an arm with raw Stoch RSI's SMA2 at or below takes +1

            var aU = new double[n]; var lb = new double[n]; var s2 = new double[n];
            var q1 = new bool[n]; var q2 = new bool[n]; var dK = new double[n]; var flatK = new bool[n];
            var cpo = new bool[n]; var pdc = new bool[n]; var cx = new int[n]; var al1 = new bool[n]; var brk = new bool[n];
            var xe = new bool[n]; var r100x2 = new bool[n]; var flo = new bool[n]; var sw60 = new double[n]; var nfl = new bool[n];
            for (int i = 0; i < n; i++)
            {
                aU[i] = (x[i] + Lf(x, i, 1)) - (Lf(x, i, 2) + Lf(x, i, 3));         // the SMA2 test, unrounded
                lb[i] = deep[i] ? R.DeepLevel : R.Level;                             // the run's level
                s2[i] = (sk[i] + sk1[i]) / 2;
                q1[i] = i >= 1 && q[i - 1]; q2[i] = i >= 2 && q[i - 2];
                dK[i] = K[i] - Lf(K, i, 1); flatK[i] = Math.Abs(dK[i]) <= 1e-4;
                cpo[i] = cp[i] && !(i >= 1 && cp[i - 1]);                            // close-up onset
                pdc[i] = i >= 1 && !cp[i - 1];                                       // the close fell on the bar before
                if (i >= 1) cx[i] = run[i] && !run[i - 1] ? i : cx[i - 1];           // the run's cross bar
                al1[i] = i >= 1 && al[i - 1];
                brk[i] = dead[i] && al1[i];                                          // break bar: the first dead bar
                xe[i] = i >= 1 && ex[i] > 0 && ex[i - 1] <= 0;                       // EMA(5)/EMA(11) cross bar (side)
                r100x2[i] = sk[i] >= 100 && Lv(sk, i, 2, 100) < 100;                 // raw Stoch RSI reached the top within 2 bars
                flo[i] = sk[i] <= 0 && Lv(sk, i, 2, 50) <= 0 && sk1[i] > 0;          // floor touch (0, up, 0)
                sw60[i] = B(sw1[Math.Max(cx[i], 1)] > 60);                           // the run crossed with side WT above 60
                nfl[i] = sk[i] + Lv(sk, i, 1, 50) <= 18;                             // raw Stoch RSI near the floor
            }
            double Base(int i) => WB + B(s2[i] <= s2T) + B(bull && nfl[i]);          // the step a re-arm takes

            // ---- the pieces held until raw Stoch RSI turns
            var hid = ArmHidden(d2, s2, s2T, WB, flo);
            var ge = new bool[n];                                                    // EMA latch: set on the cross bar, cleared on a fresh fall over 2
            for (int i = 1, g = 0; i < n; i++)
            {
                if (xe[i]) g = 1; else if (d2[i] < 0 && !(d2[i - 1] < 0)) g = 0;
                ge[i] = g == 1;
            }
            var wl = new bool[n];                                                    // WT latch: set on the run's cross bar, cleared on a fresh fall
            for (int i = 0, g = 0; i < n; i++)
            {
                if (run[i] && (i == 0 || !run[i - 1])) g = 1; else if (d2[i] < 0 && !(i > 0 && d2[i - 1] < 0)) g = 0;
                wl[i] = g == 1;
            }
            var lf = new int[n];                                                     // the last bar raw Stoch RSI fell over 2
            for (int i = 0, last = -1; i < n; i++) { if (d2[i] < 0) last = i; lf[i] = last; }
            var nfa = new bool[n];                                                   // near the floor now, or when the hidden arm armed on the bar before
            for (int i = 0; i < n; i++) nfa[i] = nfl[i] || (i >= 1 && nfl[i - 1] && hid[i - 1] > 0 && Lv(hid, i, 2, 0) == 0);
            var skSum = new double[n]; for (int i = 0; i < n; i++) skSum[i] = sk[i] + Lv(sk, i, 1, 50);

            // ---- the Q value: what an off bar in Q scores, and what a run crossing straight out of Q inherits
            var qv = new double[n];
            if (bull)
            {
                var cnf = new bool[n];                                               // a run's cross armed near the floor (the +1)
                for (int i = 0; i < n; i++)
                    cnf[i] = al[i] && !al1[i] && d2[i] > 0 && !q1[i]
                             && (nfl[i] || (i >= 1 && nfl[i - 1] && hid[i - 1] > 0 && Lv(hid, i, 2, 0) == 0) || (i >= 1 && off[i - 1] && nfa[i - 1] && !nfl[i - 1]));
                var qfIn = new double[n];
                for (int i = 0; i < n; i++) qfIn[i] = B(q[i] && !q1[i] && i >= 1 && al[i - 1] && cnf[cx[i - 1]]);
                var qf = OnsetLatch(q, qfIn);                                        // a Q straight after such a run keeps the +1
                var carry = OnsetLatchCarryH(q, skSum, run, 999, hid);
                var lq = new bool[n]; for (int i = 0; i < n; i++) lq[i] = carry[i] > 18 && qf[i] != 1;
                for (int i = 2; i < n; i++)                                          // a floor touch inside Q: the next rise re-takes the latch
                {
                    if (((q[i] && q[i - 1] && q[i - 2]) || (q[i] && al[i - 1])) && flo[i - 1] && d2[i] > 0) lq[i] = sk[i] + sk[i - 1] > 18;
                    else if (q[i] && q[i - 1] && !(flo[i - 1] && d2[i] > 0)) lq[i] = lq[i - 1];
                }
                for (int i = 0; i < n; i++) qv[i] = q[i] && hid[i] > 0 ? hid[i] + 1 - B(lq[i]) : 0;
            }
            else
            {
                var first = new double[n]; for (int i = 0; i < n; i++) first[i] = B(!q1[i] && !v0[i]);
                var qh = OnsetLatch(q, first);                                       // this Q started on the hidden arm (first bar V0 off)
                var ol = OnsetLatch(q, skSum);
                for (int i = 0; i < n; i++)
                    if (q[i] && hid[i] > 0) qv[i] = !q1[i] && !v0[i] ? hid[i] : Math.Max(4 - B(ol[i] > 80), qh[i] == 1 ? hid[i] : 0);
            }

            var hv0 = new double[n]; for (int i = 0; i < n; i++) hv0[i] = hid[i] + (bull ? B(hid[i] > 0 && nfa[i]) : 0);
            var qc = new double[n];                                                  // bulls: the arm a Q gave the last run, until raw Stoch RSI falls
            if (bull)
            {
                double qa = 0; int qi = -1;
                for (int i = 1; i < n; i++)
                {
                    if (al[i] && !al[i - 1] && q[i - 1] && d2[i] > 0) { qa = Math.Max(qv[i - 1], hid[i]); qi = i; }
                    qc[i] = qi >= 0 && qi > lf[i] && hid[i] > 0 && dead[i - 1] && d2[i] > 0 ? qa : 0;
                    if (off[i]) qi = -1;
                }
            }
            var fl = new bool[n];                                                    // bulls: after a deep run (crossed below -60) that ended with side WT above 60
            if (bull)
            {
                bool on = false;
                for (int i = 1; i < n; i++)
                {
                    if (run[i] && !run[i - 1]) on = on && sw1[i] > 60;
                    if (!run[i] && run[i - 1]) on = (on || deep[i - 1]) && sw1[i] > 60;
                    fl[i] = on;
                }
            }

            // ============================================================ OFF bars
            var so = new double[n];
            for (int i = 0; i < n; i++)
            {
                double y = votes[i] + stt[i] + qv[i];
                bool nq = !q[i];
                double dsr1 = i >= 2 ? sk[i - 1] - sk[i - 2] : 0;
                // Q just ended with Stoch RSI on the floor, on a raw floor touch (0, up, 0): +6, +4 with V5
                bool qend = off[i] && nq && q1[i] && sk[i] <= 0 && Lv(K, i, 1, 50) <= 3.5;
                if (qend && Lv(sk, i, 2, 50) <= 0 && d2[i] >= 0) y += 6 - 2 * B(v5[i]);
                // a floor touch outside Q (or just after one, not on the floor-K Q end), armed
                bool ft = off[i] && nq && !qend && !(q1[i] && !q2[i] && dsr1 > 6) && sk[i] <= 0 && Lv(sk, i, 2, 50) <= 0 && sk1[i] > 0 && hid[i] > 0;
                if (ft) y += hid[i] + B(bull && nfl[i]) + 2 * B(!v5[i]);
                // a missed Q: raw Stoch RSI crossed K on a down close (never while it fell over 2)
                bool mx = off[i] && nq && !q1[i] && sk[i] > K[i] && Lv(sk, i, 1, 50) <= Lv(K, i, 1, 50) && !cp[i] && Lv(K, i, 1, 50) < (bull ? 50 : 45)
                          && since[i] >= 2 && sk[i] > (bull ? -1 : 2) && Lv(sk, i, 2, 50) <= (bull ? 54 : 101) && d2[i] >= 0;
                if (mx) y += 4 + (bull ? (sk[i] <= 18 ? 0 : -1) : 0);
                // a one-bar Q that ended: its value carried a bar (on a floor touch, the Q's own value)
                bool mh = off[i] && nq && q1[i] && !q2[i] && dsr1 > 6 && !qend && hid[i] > 0;
                if (mh) y += flo[i] ? Lv(qv, i, 1, 0) + 2 * (1 - B(v5[i])) : 4 + (bull ? (Lv(sk, i, 1, 50) <= 18 ? 0 : -1) : 0) + 2 * (1 - B(v5[i]));
                y = Math.Max(Floor, y);
                if (bull)
                {
                    if (off[i] && nq && cp[i] && (double.IsNaN(sw1[i]) ? -999 : sw1[i]) > 50 && d2[i] > 0 && sk[i] < 100 && v5[i]) y += 2;
                    if (off[i] && nq && d2[i] > 0 && !cp[i] && sk1[i] > 77.2 && since[i] >= 2) y -= 1;   // up over 2 from high but close down
                }
                else if (off[i] && nq && d2[i] > 0 && sk[i] - sk1[i] < -6 && sk2[i] > 32) y -= 1;
                // raw Stoch RSI rising over 2: +3 just after a run when the score is low; bears also on a close-up with K not dropping
                if (off[i] && d2[i] > 0 && nq && ((y <= 0.5 && since[i] <= 2) || (!bull && cp[i] && dK[i] > -3))) y += 3;
                if (bull && off[i] && nq && d2[i] > 0.07 && !cp[i] && K[i] > 60 && dK[i] < 8 && since[i] <= 1 && v5[i]) y -= 1;   // run just ended, close down
                // V0 on (or a 3-bar floor pin outside Q) and raw Stoch RSI not falling: the votes + the hidden arm (or Q value, or the Q's arm)
                if (off[i] && (v0[i] || (nq && sk[i] <= 0 && sk1[i] <= 0 && sk2[i] <= 0)) && d2[i] >= 0) y = votes[i] + stt[i] + Math.Max(Math.Max(hv0[i], qv[i]), qc[i]);
                if (off[i] && v0[i] && d2[i] >= 0 && nq && q1[i]) y = votes[i] + stt[i] + Math.Max(hv0[i], Lv(qv, i, 1, 0));   // V0 just on after a Q
                if (off[i] && q[i] && q1[i] && flo[i] && hid[i] > 0 && !v5[i]) y += 2;   // a floor touch inside Q, no Stoch RSI vote
                if (!bull && off[i] && nq && sk[i] >= 100 && sk1[i] < 100 && flatK[i] && hid[i] == 0 && v5[i]) y -= 2;   // back at the top pin, hidden arm down
                if (fl[i] && off[i]) y += 2;                                         // bulls: after a deep run that ended high
                if (off[i] && pdc[i] && r100x2[i] && flatK[i] && v5[i]) y -= 2;      // reached the top pin after a down close, K flat
                so[i] = y;
            }

            // ============================================================ ALIVE bars: the ARM a run carries
            // At the cross: the Q value of the bar before (Q on), else the hidden arm (bull +1 near the floor); 0 if raw Stoch RSI
            // fell over 2 (bull: did not rise). After it: dropped whenever raw Stoch RSI falls over 2, re-armed when it next rises.
            var arm = new double[n]; var kept = new bool[n]; var kb = new double[n];
            {
                double a = 0, qa = 0, b = 0; int qi = -1; bool kf = false;
                for (int i = 1; i < n; i++)
                {
                    if (!al[i]) continue;
                    if (!al[i - 1])
                    {
                        a = (bull ? d2[i] <= 0 : d2[i] < 0) ? 0
                            : q[i - 1] ? Math.Max(qv[i - 1], hid[i])
                            : hid[i] + B(bull && (nfl[i] || (nfl[i - 1] && hid[i - 1] > 0 && (i < 2 || hid[i - 2] == 0)) || (off[i - 1] && nfa[i - 1] && !nfl[i - 1])));
                        if (a > 0 && i > 1 && off[i - 1] && !q[i - 1] && q[i - 2] && v0[i - 1] && d2[i - 1] >= 0) a = Math.Max(a, qv[i - 2]);   // from the V0-on bar after a Q
                        if (bull && a > 0 && !q[i - 1] && qi > lf[i]) a = Math.Max(a, qa);   // the arm a Q gave a run, kept till Stoch RSI falls
                        if (bull && q[i - 1] && a > 0) { qa = a; qi = i; }
                        if (bull && d2[i] == 0 && sk[i] >= 100 && sk2[i] >= 100) a = hid[i];   // pinned flat at the top: the hidden arm
                        kf = true;
                    }
                    else if (d2[i] < 0) { a = 0; kf = false; b = 0; }
                    else if (d2[i] > 0 && a == 0) { a = WB + B(s2[i] <= s2T); b = B(bull && nfl[i]); }
                    if (bull && al[i - 1] && flo[i - 1] && d2[i] > 0) { a = WB + B(s2[i] <= s2T); b = B(nfl[i]); }   // a floor touch on the run: the next rise re-arms
                    if (!bull && i > 2 && flo[i - 1] && (x[i - 1] - x[i - 3] <= 0 || sg[i - 1] > -4) && d2[i] > 0) a = WB + B(s2[i] <= s2T);
                    if (!al[i - 1]) b = 0;
                    arm[i] = a; kept[i] = kf && (al[i - 1] || d2[i] >= 0); kb[i] = b;   // kept: the arm never dropped since the cross
                    if (bull && !al[i - 1] && a == 0 && flo[i] && hid[i] > 0) arm[i] = hid[i] + B(nfl[i]);   // floor touch at the cross: on the cross bar only
                }
            }
            var sa = new double[n];
            for (int i = 0; i < n; i++)
            {
                bool t8 = Math.Abs(aU[i]) <= 1e-9 && i >= 1 && v5[i - 1];            // an exact SMA2 tie after a V5-on bar: no flat-K penalty
                sa[i] = votes[i] + lb[i] + 3 + cnt[i] + arm[i] + sw60[i] - 2 * B(cpo[i] && flatK[i] && v5[i] && !t8)
                        + 2 * B(fl[i] && al[i]) + kb[i] + 2 * B(al[i] && flo[i] && hid[i] > 0 && !v5[i]);
            }
            var wg = new double[n]; var w18 = new double[n];
            if (!bull) BearCrossTerms(sa, wg, w18, sw1, sg, D, st81raw, wS2, sw60, ex, xe, ge, wl, al, dead, off, q, run, cx, kept);
            var lt = new bool[n]; var ltv = new double[n]; var lfv = new double[n];
            for (int i = 0; i < n; i++)
            {
                lt[i] = al[i] && sk1[i] >= 100 && sk2[i] < 100 && flatK[i] && v5[i] && d2[i] > 0 && hid[i] > WB;   // just left the top pin
                ltv[i] = lt[i] ? Math.Max(arm[i] - Base(i), 1) : 0;
                if (i >= 1 && !lt[i] && al[i] && al[i - 1] && arm[i] > 0) ltv[i] = ltv[i - 1];   // ... held while the arm stays up
                sa[i] -= ltv[i];
            }
            for (int i = 0; i < n; i++)
            {
                lfv[i] = al[i] && sk1[i] >= 100 && sk[i] < 100 && d2[i] > 0 && Lv(d2, i, 1, 1) == 0 && hid[i] > WB ? Math.Max(arm[i] - Base(i), 1) : 0;
                if (i >= 1 && lfv[i] == 0 && al[i] && al[i - 1] && d2[i] == 0 && !lt[i]) lfv[i] = lfv[i - 1];   // ... held through a flat alive bar
                sa[i] -= lfv[i];                                                     // back at the top for a flat bar, then left it, armed
            }
            var s = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (off[i] && al1[i] && v0[i] && d2[i] >= 0) so[i] += Math.Max(0, Lv(arm, i, 1, 0) - Math.Max(hv0[i], qv[i]));   // the bar after a run: its arm if above the hidden arm
                s[i] = off[i] ? so[i] : al[i] ? Math.Max(Floor, sa[i]) : 0;
            }

            // ============================================================ DEAD bars: the held step
            var pA = new double[n]; var pB = new bool[n]; var pK = new bool[n]; var pR = new bool[n]; var pin = new double[n];
            var rest = new double[n]; var lbk = new bool[n];
            for (int i = 0; i < n; i++)
            {
                bool ex2 = dead[i] && pdc[i] && r100x2[i] && flatK[i] && v5[i] && Math.Abs(aU[i]) <= 1e-9 && !(i >= 1 && v5[i - 1]);   // the pin penalty only on a tie after a V5-off bar
                pA[i] = 2 * B(ex2);
                pB[i] = dead[i] && !ex2 && sk1[i] >= 100 && sk2[i] < 100 && flatK[i] && v5[i] && d2[i] > 0 && hid[i] > WB;
                bool dr1 = i >= 1 && dipv[i - 1] && d2[i - 1] > 0;                    // the bar before read a top-pin dip as a rise
                pB[i] &= !(al1[i] && dr1);
                bool leftTop = sk1[i] >= 100 && sk[i] < 100 && sk2[i] < 100 && d2[i] > 0 && Lv(d2, i, 1, 1) == 0 && hid[i] > WB;   // left the top for a bar
                if (bull)
                {
                    pK[i] = pB[i] && !al1[i] && dr1; pR[i] = pB[i] && !al1[i] && !dr1; pB[i] &= al1[i];
                    pR[i] |= dead[i] && !al1[i] && leftTop;
                }
                pin[i] = pB[i] ? (al1[i] ? Math.Max(Lv(arm, i, 1, 0) - Base(i), 1) : 1) : 0;   // on a break bar the run's arm re-arms at the base
                rest[i] = votes[i] + lb[i] + stt[i];
                lbk[i] = dead[i] && al1[i] && leftTop;                               // a break bar that left the top for a bar, armed
            }
            var lxe = new int[n]; for (int i = 0, m = -1; i < n; i++) { if (xe[i]) m = i; lxe[i] = m; }
            {
                bool zf = false, fd = false;                                         // bear EMA flag (+2 held until Stoch RSI falls); a hair fall dropped the step
                for (int i = 1; i < n; i++)
                {
                    if (!dead[i]) { zf = false; continue; }
                    // a rising re-cross while the run's own EMA cross latch holds adds nothing
                    bool tz = !bull && xe[i] && !(d2[i] > 0 && ge[i - 1] && lxe[i - 1] == cx[i] && xe[cx[i]]);
                    double bs = Base(i);
                    if (al[i - 1])
                    {
                        // break bar: carry the alive bar's extras (less its count) minus 3, unless Stoch RSI fell
                        zf = tz || (!bull && ge[i - 1] && arm[i - 1] == 0 && wg[i - 1] > 0 && (d2[i] >= 0 || d2[i - 1] < 0) && !(xe[i - 1] && d2[i] > 0));
                        double rb = d2[i] > 0 && arm[i - 1] == 0 ? bs : 0;              // the arm dropped on the alive bar: a rise re-arms
                        fd = d2[i] < 0 && hair[i];
                        if (d2[i] >= 0 || (d2[i - 1] < 0 && cx[i - 1] == i - 1))
                            s[i] = -B(bull && flo[i - 1] && d2[i] > 0) * Math.Max(arm[i - 1] - bs, 0) - B(bull && lbk[i]) * Math.Max(arm[i - 1] - bs, 1) - pin[i]
                                   + Math.Max(Floor, votes[i] + stt[i] + (s[i - 1] - votes[i - 1] - cnt[i - 1] - (d2[i] <= 0 && sk[i] < 100 ? kb[i - 1] : 0)
                                       + 2 * B(cpo[i - 1] && flatK[i - 1] && v5[i - 1] && !zf)) - 3 + rb + 2 * B(zf && (tz || !(d2[i] == 0 && d2[i - 1] < 0))));
                        else s[i] = Math.Max(Floor, rest[i] + 2 * B(zf) + 2 * B(fl[i]));
                        s[i] -= w18[i - 1];
                        continue;
                    }
                    double dp = s[i - 1] - rest[i - 1] - 2 * B(zf), dv = 0;
                    if (d2[i] < 0) { dv = 2 * B(fl[i]); fd = hair[i]; }                // fell over 2: drop the step (the deep-run flag's +2 stays)
                    else if (d2[i] > 0) fd = false;
                    if (d2[i] < 0) { }
                    else if (d2[i] > 0 && pK[i] && dp > 0) dv = dp;                     // the step held
                    else if (d2[i] > 0 && pR[i]) dv = bs;
                    else if (d2[i] == 0 && bull && 0 < aU[i] && aU[i] <= 1e-9 && hid[i] > WB) dv = Math.Max(WB + B(s2[i] <= s2T) + B(nfl[i]), dp - 1);   // the SMA2 test rose by ulps
                    else if (d2[i] > 0 && dipv[i]) dv = dp > 0 ? dp : hid[i] + B(bull && nfl[i]);   // a dip reading re-arms only a dropped step
                    else if (d2[i] > 0 && flo[i - 1]) dv = hid[i] + B(bull && nfl[i]);  // the rise right after a floor touch
                    else if (d2[i] > 0 && i >= 2 && al[i - 2] && d2[i - 1] == 0 && d2[i - 2] < 0 && sw60[i] == 1 && dp > 0) dv = dp + hid[i] + B(bull && nfl[i]);   // the WT > 60 +1 kept under the re-arm
                    else if (d2[i] > 0) dv = Math.Max(dp, hid[i] + B(bull && nfl[i])); // rose: re-arm like the ARM
                    else dv = dp > 0 ? dp : fd ? 0 : -2 * B(v5[i]);                     // flat: hold (no -2 after a hair fall)
                    if (tz) zf = true;
                    else if (d2[i] < 0 && !(d2[i - 1] < 0)) zf = false;                 // a fresh fall drops the flag
                    s[i] = Math.Max(Floor, rest[i] + dv + 2 * B(zf)) - pin[i];
                }
            }
            var cpn = new double[n];                                                 // the held step's pin penalty, carried until a fall
            for (int i = 0; i < n; i++)
            {
                double c0 = i >= 1 ? cpn[i - 1] : 0;
                if (!dead[i] || d2[i] < 0) c0 = 0;
                if (dead[i] && pB[i]) c0 = pin[i];
                cpn[i] = c0;
            }
            for (int i = 0; i < n; i++)
            {
                if (off[i] && i >= 1 && dead[i - 1] && v0[i] && d2[i] >= 0 && !q[i]) s[i] -= cpn[i - 1];   // ... and the off bar after it, on the hidden arm
                if (off[i] && v0[i] && !q[i] && sk1[i] >= 100 && sk[i] < 100 && d2[i] > 0 && Lv(d2, i, 1, 1) == 0 && hid[i] > WB)
                    s[i] -= Math.Max(hid[i] - Base(i), 1);                           // an off bar on the hidden arm that left the top for a bar
            }
            var pout = pA;
            if (bull)                                                                // a flat off bar after a dead stretch keeps the dead bar's step if above the hidden arm
                for (int i = 1; i < n; i++)
                    if (off[i] && dead[i - 1] && !q[i] && d2[i] == 0) s[i] += Math.Max(0, (s[i - 1] - pout[i - 1] - rest[i - 1] - sw60[i - 1]) - hid[i]);
            if (!bull)                                                               // left the top pin for a bar: -1, held through the flat dead bars after it
            {
                pout = (double[])pA.Clone();
                for (int i = 0, p = 0; i < n; i++)
                {
                    if (dead[i] && sk1[i] >= 100 && sk[i] < 100 && d2[i] > 0 && Lv(d2, i, 1, 1) == 0 && hid[i] > WB) p = 1;
                    else if (!(dead[i] && d2[i] == 0)) p = 0;
                    pout[i] += p;
                }
            }
            for (int i = 0; i < n; i++)
            {
                s[i] -= pout[i];
                if (brk[i] && flo[i]) s[i] += 2 * B(hid[i] > 0 && !v5[i]) + Lv(kb, i, 1, 0);   // a break bar on a floor touch
            }
            if (!bull)                                                               // bear EMA cross on an off-run bar: +2 on it, held by the latch after it
            {
                bool xf = false;
                for (int i = 1; i < n; i++)
                {
                    if (xe[i]) xf = !run[i];
                    if (run[i]) xf = false;
                    if (off[i] && !q[i] && ge[i] && xf && !xe[i]) s[i] += 2;
                    if (off[i] && xe[i]) s[i] += 2;
                    if (off[i] && q[i] && ge[i] && xf && !xe[i]) s[i] += 2;
                }
                for (int i = 0; i < n; i++) if (brk[i] && cpo[i] && flatK[i] && v5[i]) s[i] -= 2;
            }
            for (int i = 0, l = 0; i < n; i++)                                      // a WT > 60 cross bonus survives a Stoch RSI fall for the dead stretch
            {
                if (!dead[i]) l = 0; else if (sw60[i] == 1 && d2[i] < 0) l = 1;
                if (dead[i] && sw60[i] == 1 && l == 1) s[i] += 1;
            }
            return s;
        }

        /// <summary>
        /// Bears' cross-time terms, added to <paramref name="sa"/> (the alive score): +4 with the 81-bar stochastic
        /// and the side's wt2 high at the cross, while the WT latch holds; +2 near the zero line (unless D was high at
        /// the cross); the zero-line bonus when the run's WT cross bar is a bearish EMA(5)/EMA(11) cross bar (6, or 4
        /// on top of a cross term); with no cross term at all, +2 terms for a wide gap and for an EMA cross near the
        /// run's (<paramref name="wg"/>, also read by the break bar), and the off-bar EMA latch carried into the run
        /// (<paramref name="w18"/>, which the break bar takes back off).
        /// </summary>
        private static void BearCrossTerms(double[] sa, double[] wg, double[] w18, double[] sw1, double[] sg, double[] D, double[] st81raw, double[] wS2,
            double[] sw60, double[] ex, bool[] xe, bool[] ge, bool[] wl, bool[] al, bool[] dead, bool[] off, bool[] q, bool[] run, int[] cx, bool[] kept)
        {
            int n = sa.Length;
            var sm = Sma(sw1, 3);
            var xc = new double[n]; var nz = new double[n]; var xo = new double[n]; var z = new bool[n]; var nox = new bool[n];
            var sXc = new int[n]; var lx = new int[n];
            for (int i = 0; i < n; i++)
            {
                int x = cx[i], x1 = Math.Max(x, 1);
                bool hi = 100 - D[x] > 60;                                           // unflipped D at the cross
                double st81 = 100 - st81raw[x1]; if (double.IsNaN(st81)) st81 = 50;
                bool epos = ex[i] > 0;
                xc[i] = 4 * B(st81 > 80 && wS2[x1] > 40 && wl[i] && al[i]);
                nz[i] = 2 * B(!hi && sw1[i] > -2 && sw1[x] < 5.6 && epos);
                xo[i] = xc[i] + sw60[i] + nz[i];
                z[i] = al[i] && xe[x];                                               // zero-line bonus: the run's cross bar was an EMA cross bar
                nox[i] = !z[i] && xo[i] == 0;                                        // no cross term at all
                double sgz = double.IsNaN(sg[i]) ? 0 : sg[i], g = sw1[i] - sm[i]; if (double.IsNaN(g)) g = 99;
                wg[i] = 2 * B(epos && sg[i] > 17.34 && sw1[i] <= 27.5 && nox[i] && ge[i]) + 2 * B(epos && sw1[i] > -10 && sw1[i] <= 25 && sgz > 10.5 && g <= 17 && nox[i] && ge[i]);
                sXc[i] = i == 0 ? 999 : xe[i] ? 0 : Math.Min(999, sXc[i - 1] + 1); // bars since the EMA cross
                lx[i] = xe[i] ? i : i == 0 ? -1 : lx[i - 1];
            }
            for (int i = 0; i < n; i++)
            {
                int m = Math.Max(lx[i], 0);
                bool xdd = dead[m], xpa = lx[i] >= 0 && lx[i] < cx[i] && al[m], xof = off[m];
                wg[i] = Math.Max(wg[i], 2 * B(nox[i] && ((sXc[i] <= 2 && !xdd && !xpa) || (ge[i] && sXc[i] <= i - cx[i] + 1)) && kept[i]));   // the cross +2 on a kept arm
                wg[i] = Math.Max(wg[i], 2 * B(nox[i] && kept[i] && ge[i] && al[i] && (cx[i] >= 1 && q[cx[i] - 1]) && xof));   // ... and the run crosses out of a Q
            }
            var lat = new bool[n];                                                   // the off-bar EMA latch +2
            for (int i = 1, xf = 0; i < n; i++)
            {
                if (xe[i]) xf = run[i] ? 0 : 1;
                if (run[i]) xf = 0;
                lat[i] = off[i] && !q[i] && ge[i] && xf == 1 && !xe[i];
            }
            for (int i = 0; i < n; i++)
            {
                double w0 = wg[i];
                wg[i] = Math.Max(wg[i], 2 * B(nox[i] && kept[i] && ge[i] && al[i] && cx[i] >= 1 && lat[Math.Max(cx[i] - 1, 0)]));   // held on the bar before the run's cross
                w18[i] = wg[i] - w0;
                wg[i] = Math.Max(wg[i], 2 * B(!kept[i] && !z[i] && xc[i] == 0 && sXc[i] <= 2 && ge[i]));   // arm dropped: +2 from the EMA cross, held by the latch
                nz[i] *= B(sXc[i] <= i - cx[i] + 1);                                 // near zero only if the EMA cross came at most 1 bar before the run's
                bool cz = z[i] && cx[i] == i;                                        // the run's cross bar on an EMA cross keeps near-zero
                sa[i] += xc[i] + B(z[i]) * (xo[i] > 0.5 ? 4 : 6) * B(kept[i] || ge[i]) + nz[i] * B(kept[i] || cz) + wg[i];
            }
        }

        /// <summary>The hidden arm, on every bar: 0 once raw Stoch RSI falls over 2 bars; when it next rises (or rises right after a floor touch), the step (+1 when its SMA2 is at most <paramref name="s2T"/>), then held.</summary>
        private static double[] ArmHidden(double[] d2, double[] s2, double s2T, int step, bool[] floorTouch)
        {
            var o = new double[d2.Length]; double a = 0;
            for (int i = 0; i < d2.Length; i++)
            {
                if (d2[i] < 0) a = 0;
                else if (d2[i] > 0 && (a == 0 || (i > 0 && floorTouch[i - 1]))) a = step + B(s2[i] <= s2T);
                o[i] = a;
            }
            return o;
        }

        // ---------------------------------------------------------------- the Stoch RSI vote (V5)

        /// <summary>
        /// The Stoch RSI (12, 12, 3) up and down votes (1 or 0). Primary: the SMA2 of raw Stoch RSI against itself
        /// 2 bars earlier. Where that is exactly flat: K's slope, then K's side of 50; a flat K on the wrong side of
        /// 50 still votes when raw Stoch RSI stayed off its pin and RSI moved that way a bar ago, and keeps the last
        /// vote when RSI was flat a bar ago.
        /// </summary>
        internal static (double[] Up, double[] Down) StochRsiVotes(double[] c)
        {
            int n = c.Length;
            var r = Rsi(c, RsiLength); var sRaw = Stoch(r, r, r, RsiLength, ofItself: true); var k = Sma(sRaw, 3);
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
                if (Math.Abs(a) > 1e-9 && (Math.Abs(S(i) - S(i - 3)) > 0 || (S(i - 2) + S(i - 3)) > 0)) { u = a > 0; dd = a < 0; }
                double s1 = i >= 1 ? sRaw[i - 1] : double.NaN, s2 = i >= 2 ? sRaw[i - 2] : double.NaN;
                double dr1 = i >= 2 ? r[i - 1] - r[i - 2] : double.NaN;
                bool lowK = tie && k[i] < 50 && s1 > t && s2 > t, highK = tie && k[i] > 50 && s1 < 100 - t && s2 < 100 - t;
                u |= lowK && dr1 > 0;
                dd |= highK && dr1 < 0;
                if (i >= 1 && lowK && dr1 == 0 && up[i - 1] == 1) u = true;
                if (i >= 1 && highK && dr1 == 0 && dn[i - 1] == 1) dd = true;
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

        /// <summary>
        /// <see cref="OnsetLatch"/>, but a Q episode starting right after a run of 1 to <paramref name="maxRun"/> bars that
        /// itself crossed straight out of Q keeps that earlier episode's value while the hidden arm stayed up from that Q to
        /// this one; a Q starting right after the hidden arm armed takes the lower of this bar's and the last bar's value.
        /// </summary>
        private static double[] OnsetLatchCarryH(bool[] q, double[] x, bool[] run, int maxRun, double[] hid)
        {
            var o = new double[q.Length]; double? last = null;
            for (int i = 0; i < q.Length; i++)
            {
                if (q[i] && (i == 0 || !q[i - 1]))
                {
                    int j = i - 1, len = 0;
                    while (j >= 0 && run[j]) { j--; len++; }
                    bool ok = len >= 1 && len <= maxRun && j >= 0 && q[j] && last.HasValue;
                    for (int k = j; ok && k < i; k++) ok = hid[k] > 0;
                    double xi = i >= 2 && !q[i - 1] && !run[i - 1] && hid[i - 1] > 0 && hid[i - 2] <= 0 ? (x[i - 1] < x[i] ? x[i - 1] : x[i]) : x[i];
                    o[i] = ok ? last!.Value : xi;
                    last = o[i];
                }
                else o[i] = q[i] ? o[i - 1] : 0;
                if (q[i]) last = o[i];
            }
            return o;
        }

        private static int B(bool b) => b ? 1 : 0;
        private static double[] Neg(double[] x) { var r = new double[x.Length]; for (int i = 0; i < x.Length; i++) r[i] = -x[i]; return r; }
        private static double[] Sub(double[] a, double[] b) { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i]; return r; }
        private static double[] Nan(int n) { var r = new double[n]; Array.Fill(r, double.NaN); return r; }

        /// <summary>x[i − k], or x[i] itself on the first k bars.</summary>
        private static double Lf(double[] x, int i, int k) => i >= k ? x[i - k] : x[i];
        /// <summary>x[i − k], or <paramref name="v"/> on the first k bars.</summary>
        private static double Lv(double[] x, int i, int k, double v) => i >= k ? x[i - k] : v;
        /// <summary>Shifted k bars, NaN-padded.</summary>
        private static double[] Lag(double[] x, int k) { var r = Nan(x.Length); for (int i = k; i < x.Length; i++) r[i] = x[i - k]; return r; }
        /// <summary>Shifted k bars; the first k bars keep their own value.</summary>
        private static double[] LagSelf(double[] x, int k) { var r = (double[])x.Clone(); for (int i = k; i < x.Length; i++) r[i] = x[i - k]; return r; }
        /// <summary>Shifted k bars, padded with <paramref name="v"/>.</summary>
        private static double[] LagV(double[] x, int k, double v) { var r = new double[x.Length]; for (int i = 0; i < x.Length; i++) r[i] = i >= k ? x[i - k] : v; return r; }
        /// <summary>x above the bar before (NaN compares false).</summary>
        private static bool[] Rising(double[] x) { var r = new bool[x.Length]; for (int i = 1; i < x.Length; i++) r[i] = x[i] - x[i - 1] > 0; return r; }

        /// <summary>x above the bar before; on an exact tie the last direction; an undefined bar (or bar before) resets it.</summary>
        private static bool[] HeldRising(double[] x)
        {
            var r = new bool[x.Length]; int dir = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (double.IsNaN(x[i]) || (i > 0 && double.IsNaN(x[i - 1]))) dir = 0;
                else if (i > 0 && x[i] != x[i - 1]) dir = Math.Sign(x[i] - x[i - 1]);
                r[i] = dir > 0;
            }
            return r;
        }

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

        /// <summary>
        /// The exactly rounded sum of x[from..from+len), as Python's <c>math.fsum</c> computes it (Shewchuk's
        /// partials, then the half-even fix-up across them); Pine's SMA sums exactly.
        /// </summary>
        internal static double Fsum(double[] x, int from, int len)
        {
            var p = new double[len + 1]; int m = 0; double special = 0; bool anySpecial = false;
            for (int k = 0; k < len; k++)
            {
                double v = x[from + k];
                if (!double.IsFinite(v)) { special += v; anySpecial = true; m = 0; continue; }
                int i = 0;
                for (int j = 0; j < m; j++)
                {
                    double y = p[j];
                    if (Math.Abs(v) < Math.Abs(y)) (v, y) = (y, v);
                    double hi0 = v + y, lo0 = y - (hi0 - v);
                    if (lo0 != 0.0) p[i++] = lo0;
                    v = hi0;
                }
                m = i;
                if (v != 0.0) p[m++] = v;
            }
            if (anySpecial) return special;
            double hi = 0.0;
            if (m > 0)
            {
                double lo = 0;
                hi = p[--m];
                while (m > 0)
                {
                    double xx = hi, y = p[--m];
                    hi = xx + y; double yr = hi - xx; lo = y - yr;
                    if (lo != 0.0) break;
                }
                if (m > 0 && ((lo < 0.0 && p[m - 1] < 0.0) || (lo > 0.0 && p[m - 1] > 0.0)))
                {
                    double y = lo * 2.0, xx = hi + y, yr = xx - hi;
                    if (y == yr) hi = xx;
                }
            }
            return hi;
        }

        /// <summary>SMA, Pine's: the exact sum over the length (<see cref="Fsum"/>); NaN while the window holds a NaN.</summary>
        internal static double[] Sma(double[] x, int len)
        {
            var r = Nan(x.Length);
            for (int i = len - 1; i < x.Length; i++)
            {
                bool ok = true;
                for (int j = i - len + 1; j <= i && ok; j++) ok = !double.IsNaN(x[j]);
                if (ok) r[i] = Fsum(x, i - len + 1, len) / len;
            }
            return r;
        }

        /// <summary>SMA as a running sum (add the new value, take off the one leaving); an undefined value restarts it.</summary>
        private static double[] RunningSma(double[] x, int len)
        {
            var r = Nan(x.Length); double s = 0; int cnt = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (double.IsNaN(x[i])) { s = 0; cnt = 0; continue; }
                cnt++;
                s += x[i];
                if (cnt > len) s -= x[i - len];
                if (cnt >= len) r[i] = s / len;
            }
            return r;
        }

        /// <summary>Pine's EMA: seeded with the mean of the first <paramref name="len"/> values from the first defined one; a NaN or infinite input after that leaves a gap.</summary>
        internal static double[] Ema(double[] x, int len)
        {
            int n = x.Length; double a = 2.0 / (len + 1);
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

        /// <summary>Pine's RMA, its own arithmetic: (x + (n − 1)·prev) / n after a mean seed; a NaN input leaves a gap.</summary>
        private static double[] Rma(double[] x, int len)
        {
            int n = x.Length;
            var r = Nan(n);
            int s = Array.FindIndex(x, v => !double.IsNaN(v));
            if (s < 0 || s + len > n) return r;
            double e = Sum(x, s, len) / len;
            r[s + len - 1] = e;
            for (int i = s + len; i < n; i++)
            {
                if (double.IsNaN(x[i])) continue;
                e = (x[i] + (len - 1) * e) / len;
                r[i] = e;
            }
            return r;
        }

        /// <summary>
        /// The n-bar stochastic of c against h/l (undefined values skipped). Of a price: (c − low) / range · 100, a flat
        /// window undefined. <paramref name="ofItself"/> (Stoch RSI): 100 · (c − low) / range, and a window flat to
        /// 1e-16 of its high keeps the last value.
        /// </summary>
        internal static double[] Stoch(double[] c, double[] h, double[] l, int len, bool ofItself = false)
        {
            int n = c.Length;
            var st = Nan(n);
            for (int i = len - 1; i < n; i++)
            {
                double hh = double.NegativeInfinity, ll = double.PositiveInfinity; bool anyH = false, anyL = false;
                for (int j = i - len + 1; j <= i; j++)
                {
                    if (!double.IsNaN(h[j])) { anyH = true; if (h[j] > hh) hh = h[j]; }
                    if (!double.IsNaN(l[j])) { anyL = true; if (l[j] < ll) ll = l[j]; }
                }
                if (!anyH || !anyL) continue;
                if (ofItself && hh - ll <= 1e-16 * Math.Abs(hh)) { if (i > 0) st[i] = st[i - 1]; continue; }
                if (hh > ll) st[i] = ofItself ? 100 * (c[i] - ll) / (hh - ll) : (c[i] - ll) / (hh - ll) * 100;
            }
            return st;
        }

        /// <summary>Wilder's RSI on Pine's RMA.</summary>
        internal static double[] Rsi(double[] c, int len)
        {
            int n = c.Length;
            var up = Nan(n); var dn = Nan(n);
            static double NpMax(double a, double b) => a >= b || double.IsNaN(a) ? a : b;   // numpy's maximum (keeps -0.0)
            for (int i = 1; i < n; i++) { double d = c[i] - c[i - 1]; up[i] = NpMax(d, 0); dn[i] = NpMax(-d, 0); }
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
