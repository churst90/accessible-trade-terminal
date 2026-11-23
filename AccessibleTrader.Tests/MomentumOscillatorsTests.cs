using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Elder-Ray with Bull Bear Power, Fisher Transform, Coppock, Know Sure Thing, Relative Vigor Index,
/// SMI Ergodic, and the Awesome and Accelerator oscillators (indicator group 4, first half, Cody
/// 2026-11-22). Each is checked against TradingView's formula written out longhand here, a bar at a
/// time, not against a second call into the same helpers. The provider-wide contracts (causality,
/// metadata, narration routes) reach them through IndicatorProviderFixture.
/// </summary>
public class MomentumOscillatorsTests
{
    private static Dictionary<string, double[]> Run(IIndicatorProvider p, string code, IReadOnlyList<Ohlcv> bars,
        Dictionary<string, object>? pars = null)
    {
        var rd = new Dictionary<string, double[]>();
        p.Calculate(code, bars.ToArray().AsSpan(), pars ?? new Dictionary<string, object>(), new IndicatorResultBuffer(rd, bars.Count));
        return rd;
    }

    private static readonly DateTime T0 = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    private static List<Ohlcv> Closes(params double[] closes)
        => closes.Select((c, i) => new Ohlcv(T0.AddHours(i), c, c + 1, c - 1, c, 100)).ToList();

    /// <summary>Pine's <c>ta.ema</c>: seeded from its first value, shown from the <paramref name="len"/>-th.</summary>
    private sealed class Ema(int len)
    {
        private double _v = double.NaN;
        private int _count;
        public double Next(double x)
        {
            if (double.IsNaN(x)) return double.NaN;
            _v = double.IsNaN(_v) ? x : _v + 2.0 / (len + 1) * (x - _v);
            return ++_count < len ? double.NaN : _v;
        }
    }

    private static double Sma(Func<int, double> x, int i, int len)
    {
        if (i < len - 1) return double.NaN;
        double s = 0;
        for (int j = i - len + 1; j <= i; j++) s += x(j);
        return s / len;
    }

    private static double Roc(IReadOnlyList<Ohlcv> b, int i, int len)
        => i < len ? double.NaN : 100 * (b[i].Close - b[i - len].Close) / b[i - len].Close;

    private static void Same(double expected, double actual, string what)
    {
        if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual), $"{what}: expected no value, got {actual}");
        else Assert.True(Math.Abs(expected - actual) <= 1e-9 * Math.Max(1, Math.Abs(expected)), $"{what}: expected {expected}, got {actual}");
    }

    // ── Elder-Ray and Bull Bear Power ─────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Elder_ray_is_the_high_and_the_low_against_a_13_bar_ema(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 400);
        var r = Run(new ElderRayProvider(), ElderRayProvider.Code, bars);
        var ema = new Ema(13);
        for (int i = 0; i < bars.Count; i++)
        {
            double e = ema.Next(bars[i].Close);
            Same(bars[i].High - e, r[ElderRayProvider.CompBull][i], $"bull power, bar {i}");
            Same(bars[i].Low - e, r[ElderRayProvider.CompBear][i], $"bear power, bar {i}");
            Same(bars[i].High + bars[i].Low - 2 * e, r[ElderRayProvider.CompNet][i], $"bull bear power, bar {i}");
        }
        Assert.True(double.IsNaN(r[ElderRayProvider.CompNet][11]));
        Assert.False(double.IsNaN(r[ElderRayProvider.CompNet][12]));
    }

    [Fact]
    public void Elder_ray_says_where_the_high_and_the_low_were_against_the_average()
    {
        // Flat at 100 (high 101, low 99), so the EMA is 100: the high 1 above it, the low 1 below.
        var bars = Closes(Enumerable.Repeat(100.0, 20).ToArray());
        var p = new ElderRayProvider();
        var r = Run(p, ElderRayProvider.Code, bars);
        string one = SpeechPriceFormatter.FormatPrice(1.0);
        Assert.Equal($"Bull power plus {one}: the high {one} above the 13-bar EMA. Bear power minus {one}: the low {one} below it. Bull bear power 0.",
            p.GetDetailFact(ElderRayProvider.Code, bars.ToArray(), r, 19, new()));
        Assert.Equal("Elder-Ray: needs 13 bars for its average.", p.GetDetailFact(ElderRayProvider.Code, bars.ToArray(), r, 5, new()));
        Assert.Equal($"Bear power minus {one}.", p.GetComponentSpeech(ElderRayProvider.CompBear, -1, bars[19], r, 19));
    }

    // ── Fisher Transform ──────────────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Fisher_is_tradingview_s_formula(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 400);
        var r = Run(new FisherTransformProvider(), FisherTransformProvider.Code, bars);
        double value = 0, fish = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            if (i < 8) { Assert.True(double.IsNaN(r[FisherTransformProvider.CompFisher][i]), $"bar {i}"); continue; }
            double hi = double.MinValue, lo = double.MaxValue;
            for (int j = i - 8; j <= i; j++) { double m = (bars[j].High + bars[j].Low) / 2; hi = Math.Max(hi, m); lo = Math.Min(lo, m); }
            double mid = (bars[i].High + bars[i].Low) / 2;
            double v = .66 * ((mid - lo) / (hi - lo) - .5) + .67 * value;
            value = v > .99 ? .999 : v < -.99 ? -.999 : v;
            double before = fish;
            fish = .5 * Math.Log((1 + value) / (1 - value)) + .5 * before;
            Same(fish, r[FisherTransformProvider.CompFisher][i], $"fisher, bar {i}");
            Same(i == 8 ? double.NaN : before, r[FisherTransformProvider.CompTrigger][i], $"trigger, bar {i}");
        }
    }

    [Fact]
    public void Fisher_says_a_stretched_move_past_one_and_a_half()
    {
        // A steady climb keeps the midpoint at the top of its range, so the value pins at 0.999.
        var bars = Closes(Enumerable.Range(0, 40).Select(i => 100.0 + i).ToArray());
        var p = new FisherTransformProvider();
        var r = Run(p, FisherTransformProvider.Code, bars);
        Assert.True(r[FisherTransformProvider.CompFisher][39] > 1.5);
        string d = p.GetDetailFact(FisherTransformProvider.Code, bars.ToArray(), r, 39, new());
        Assert.StartsWith("Fisher plus ", d);
        Assert.Contains("above its trigger plus ", d);
        Assert.EndsWith(", stretched high.", d);
        Assert.Equal("Fisher: needs 9 bars.", p.GetDetailFact(FisherTransformProvider.Code, bars.ToArray(), r, 3, new()));
    }

    // ── Coppock ───────────────────────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Coppock_is_a_weighted_average_of_two_rates_of_change(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 400);
        var c = Run(new CoppockCurveProvider(), CoppockCurveProvider.Code, bars)[CoppockCurveProvider.CompCoppock];
        for (int i = 0; i < bars.Count; i++)
        {
            double expected = double.NaN;
            if (i >= 14 + 9)
            {
                double sum = 0;
                for (int j = 0; j < 10; j++) sum += (10 - j) * (Roc(bars, i - j, 14) + Roc(bars, i - j, 11));
                expected = sum / 55;
            }
            Same(expected, c[i], $"coppock, bar {i}");
        }
    }

    [Fact]
    public void Coppock_marks_the_bar_it_turns_up_below_zero_and_not_a_turn_above_it()
    {
        // Down for 40 bars, then up: the curve bottoms below zero and rises through it. A steady climb
        // is a shrinking percentage, so it then sags; a steeper climb turns it up again above zero.
        var closes = Enumerable.Range(0, 40).Select(i => 200.0 - 2 * i)
            .Concat(Enumerable.Range(1, 60).Select(i => 120.0 + 3 * i))
            .Concat(Enumerable.Range(1, 30).Select(i => 300.0 + 12 * i)).ToArray();
        var bars = Closes(closes);
        var p = new CoppockCurveProvider();
        var r = Run(p, CoppockCurveProvider.Code, bars);
        var c = r[CoppockCurveProvider.CompCoppock];
        var marks = Enumerable.Range(0, bars.Count).Where(i => !double.IsNaN(r[CoppockCurveProvider.CompTurnUp][i])).ToList();
        int turn = Assert.Single(marks);
        Assert.Contains(Enumerable.Range(2, bars.Count - 2), i => c[i] > 0 && c[i] > c[i - 1] && c[i - 1] <= c[i - 2]);   // the turn above zero is there
        Assert.True(c[turn] < 0 && c[turn] > c[turn - 1] && c[turn - 1] <= c[turn - 2], $"bar {turn}");
        Assert.Contains(", turned up below zero this bar.", p.GetDetailFact(CoppockCurveProvider.Code, bars.ToArray(), r, turn, new()));
        Assert.Contains(", rising.", p.GetDetailFact(CoppockCurveProvider.Code, bars.ToArray(), r, turn + 1, new()));
    }

    // ── Know Sure Thing ───────────────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Kst_is_four_smoothed_rates_of_change_weighted_one_to_four(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 400);
        var r = Run(new KnowSureThingProvider(), KnowSureThingProvider.Code, bars);
        double Kst(int i) => Sma(j => Roc(bars, j, 10), i, 10) + 2 * Sma(j => Roc(bars, j, 15), i, 10)
                           + 3 * Sma(j => Roc(bars, j, 20), i, 10) + 4 * Sma(j => Roc(bars, j, 30), i, 15);
        for (int i = 0; i < bars.Count; i++)
        {
            double k = i < 44 ? double.NaN : Kst(i);
            Same(k, r[KnowSureThingProvider.CompKst][i], $"kst, bar {i}");
            Same(i < 52 ? double.NaN : Sma(Kst, i, 9), r[KnowSureThingProvider.CompSignal][i], $"signal, bar {i}");
        }
    }

    // ── Relative Vigor Index ──────────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Rvgi_is_tradingview_s_formula(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 400);
        var r = Run(new RelativeVigorIndexProvider(), RelativeVigorIndexProvider.Code, bars);
        double Swma(Func<int, double> x, int i) => (x(i - 3) + 2 * x(i - 2) + 2 * x(i - 1) + x(i)) / 6;
        double Rvgi(int i)
        {
            double num = 0, den = 0;
            for (int j = i - 9; j <= i; j++)
            {
                num += Swma(k => bars[k].Close - bars[k].Open, j);
                den += Swma(k => bars[k].High - bars[k].Low, j);
            }
            return num / den;
        }
        for (int i = 0; i < bars.Count; i++)
        {
            Same(i < 12 ? double.NaN : Rvgi(i), r[RelativeVigorIndexProvider.CompRvgi][i], $"rvgi, bar {i}");
            Same(i < 15 ? double.NaN : Swma(Rvgi, i), r[RelativeVigorIndexProvider.CompSignal][i], $"signal, bar {i}");
        }
    }

    [Fact]
    public void Rvgi_says_candles_closing_above_their_opens()
    {
        var bars = Enumerable.Range(0, 30).Select(i => new Ohlcv(T0.AddHours(i), 100, 103, 99, 102, 100)).ToList();
        var p = new RelativeVigorIndexProvider();
        var r = Run(p, RelativeVigorIndexProvider.Code, bars);
        Assert.Equal(0.5, r[RelativeVigorIndexProvider.CompRvgi][29], 9);
        Assert.StartsWith("Vigor plus 0.50, candles closing above their opens",
            p.GetDetailFact(RelativeVigorIndexProvider.Code, bars.ToArray(), r, 29, new()));
    }

    // ── SMI Ergodic ───────────────────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Smi_ergodic_is_the_true_strength_index_with_a_signal(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 400);
        var r = Run(new SmiErgodicProvider(), SmiErgodicProvider.Code, bars);
        Ema n1 = new(20), n2 = new(5), d1 = new(20), d2 = new(5), sig = new(5);
        for (int i = 1; i < bars.Count; i++)
        {
            double pc = bars[i].Close - bars[i - 1].Close;
            double num = n2.Next(n1.Next(pc)), den = d2.Next(d1.Next(Math.Abs(pc)));
            double smi = num / den, s = sig.Next(smi);
            Same(smi, r[SmiErgodicProvider.CompSmi][i], $"smi, bar {i}");
            Same(s, r[SmiErgodicProvider.CompSignal][i], $"signal, bar {i}");
            Same(smi - s, r[SmiErgodicProvider.CompOsc][i], $"oscillator, bar {i}");
        }
        Assert.True(double.IsNaN(r[SmiErgodicProvider.CompSmi][23]));
        Assert.False(double.IsNaN(r[SmiErgodicProvider.CompSmi][24]));
        Assert.All(r[SmiErgodicProvider.CompSmi].Where(v => !double.IsNaN(v)), v => Assert.InRange(v, -1, 1));
    }

    // ── Awesome and Accelerator ───────────────────────────────────────────────

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void Awesome_and_accelerator_are_tradingview_s_formulas(int flavour)
    {
        var bars = CausalityProbeSeries.Bars(flavour, 400);
        var ao = Run(new AwesomeOscillatorProvider(), AwesomeOscillatorProvider.Code, bars)[AwesomeOscillatorProvider.CompAo];
        var ac = Run(new AcceleratorOscillatorProvider(), AcceleratorOscillatorProvider.Code, bars)[AcceleratorOscillatorProvider.CompAc];
        double Mid(int j) => (bars[j].High + bars[j].Low) / 2;
        double Ao(int i) => Sma(Mid, i, 5) - Sma(Mid, i, 34);
        for (int i = 0; i < bars.Count; i++)
        {
            Same(i < 33 ? double.NaN : Ao(i), ao[i], $"awesome, bar {i}");
            Same(i < 37 ? double.NaN : Ao(i) - Sma(Ao, i, 5), ac[i], $"accelerator, bar {i}");
        }
    }

    [Fact]
    public void Awesome_says_rising_or_falling_and_which_average_is_on_top()
    {
        var bars = Closes(Enumerable.Range(0, 50).Select(i => 100.0 + i).ToArray());
        var p = new AwesomeOscillatorProvider();
        var r = Run(p, AwesomeOscillatorProvider.Code, bars);
        string d = p.GetDetailFact(AwesomeOscillatorProvider.Code, bars.ToArray(), r, 49, new());
        Assert.StartsWith("Awesome oscillator plus ", d);
        Assert.EndsWith(", flat: the 5-bar average midpoint above the 34-bar one.", d);   // a straight climb: a constant gap
        var falling = Closes(Enumerable.Range(0, 50).Select(i => 1000.0 - i * i / 10.0).ToArray());   // falling faster and faster
        var rf = Run(p, AwesomeOscillatorProvider.Code, falling);
        Assert.EndsWith(", falling.", p.GetComponentSpeech(AwesomeOscillatorProvider.CompAo, rf[AwesomeOscillatorProvider.CompAo][49], falling[49], rf, 49));
    }

    // ── The signal crosses, all four that have them ───────────────────────────

    public static TheoryData<string> CrossingKinds => new() { "FISHER", "KST", "RVGI", "SMI_ERGODIC" };

    [Theory, MemberData(nameof(CrossingKinds))]
    public void Each_cross_of_the_signal_is_marked_once_on_the_bar_that_made_it(string code)
    {
        (IIndicatorProvider p, string line, string signal) = code switch
        {
            "FISHER" => ((IIndicatorProvider)new FisherTransformProvider(), FisherTransformProvider.CompFisher, FisherTransformProvider.CompTrigger),
            "KST" => (new KnowSureThingProvider(), KnowSureThingProvider.CompKst, KnowSureThingProvider.CompSignal),
            "RVGI" => (new RelativeVigorIndexProvider(), RelativeVigorIndexProvider.CompRvgi, RelativeVigorIndexProvider.CompSignal),
            _ => (new SmiErgodicProvider(), SmiErgodicProvider.CompSmi, SmiErgodicProvider.CompSignal),
        };
        var bars = CausalityProbeSeries.Bars(0, 900);
        var r = Run(p, code, bars);
        var a = r[line]; var b = r[signal];
        int ups = 0, downs = 0;
        for (int i = 1; i < bars.Count; i++)
        {
            double was = a[i - 1] - b[i - 1], now = a[i] - b[i];
            bool up = was <= 0 && now > 0, down = was >= 0 && now < 0;
            Assert.Equal(up ? a[i] : double.NaN, r["CrossUp"][i]);
            Assert.Equal(down ? a[i] : double.NaN, r["CrossDown"][i]);
            ups += up ? 1 : 0; downs += down ? 1 : 0;
        }
        Assert.True(ups > 3 && downs > 3, $"{code}: {ups} up, {downs} down");
        int at = Enumerable.Range(0, bars.Count).First(i => !double.IsNaN(r["CrossUp"][i]));
        Assert.Contains(", crossed above it this bar", p.GetDetailFact(code, bars.ToArray(), r, at, new()));
    }
}
