using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static AccessibleTrader.Core.Services.Indicators.Sequential913Provider;

namespace AccessibleTrader.Tests;

/// <summary>
/// The 9-13 sequential (Cody, 2026-11-18): setup 1 to 9 after a price flip, perfection, countdown
/// 1 to 13 with bar 13's qualifier and deferral, cancellation by an opposite setup or by a close
/// beyond the setup's extreme, and the recycle. Every count is a numbered marker that is said
/// ("Buy setup 7."), and the 9 and the 13 are components of their own with their own bell.
///
/// <para>The sequences are built by hand so each rule is reached on a known bar. The common
/// start: six closes rising 110 to 115, then closes falling 2 a bar from 113, so bar 7 (111) is
/// the first close below the close four bars earlier after one that was above (the flip). Bar 15
/// (95) is setup 9, and countdown 1 too: 95 is below bar 13's low of 98.5.</para>
/// </summary>
public sealed class Sequential913Tests
{
    private static readonly DateTime Start = new(2026, 1, 1);

    /// <summary>A bar closing at <paramref name="close"/>, opening there, with the given wicks.</summary>
    private static Ohlcv Bar(int day, double close, double lo = 0.5, double hi = 0.5) =>
        new(Start.AddDays(day), close, close + hi, close - lo, close, 100);

    private static List<Ohlcv> Bars(IEnumerable<(double Close, double Lo)> closes) =>
        closes.Select((c, i) => Bar(i, c.Close, c.Lo)).ToList();

    private static IEnumerable<(double, double)> Plain(IEnumerable<double> closes) => closes.Select(c => (c, 0.5));

    /// <summary>The rise and the steep fall: setup 1 on bar 7, setup 9 on bar 15, then
    /// <paramref name="fallBars"/> bars of the fall in all (from bar 6).</summary>
    private static IEnumerable<double> RiseThenFall(int fallBars) =>
        Enumerable.Range(0, 6).Select(i => 110.0 + i).Concat(Enumerable.Range(1, fallBars).Select(k => 115.0 - 2 * k));

    private static Dictionary<string, double[]> Calc(List<Ohlcv> bars)
    {
        var results = new Dictionary<string, double[]>();
        new Sequential913Provider().Calculate(Code, bars.ToArray(), new(), new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    /// <summary>The bars a component is printed on, with their numbers.</summary>
    private static List<(int Bar, int Label)> Printed(Dictionary<string, double[]> r, string comp)
    {
        var v = r[comp];
        var label = r[comp + ComponentCompanions.Label];
        var list = new List<(int, int)>();
        for (int i = 0; i < v.Length; i++)
        {
            Assert.Equal(double.IsNaN(v[i]), double.IsNaN(label[i]));
            if (!double.IsNaN(v[i])) list.Add((i, (int)label[i]));
        }
        return list;
    }

    [Fact]
    public void A_buy_setup_counts_one_to_nine_after_the_flip_and_the_nine_is_its_own_component()
    {
        var bars = Bars(Plain(RiseThenFall(17)));
        var r = Calc(bars);

        Assert.Equal(Enumerable.Range(1, 8).Select(k => (6 + k, k)).ToList(), Printed(r, CompBuySetup));
        Assert.Equal(new List<(int, int)> { (15, 9) }, Printed(r, CompBuySetup9));
        // The marker hangs from its bar's low.
        Assert.Equal(bars[10].Low, r[CompBuySetup][10]);
        // The run goes on below the close four bars back, but a 9 is not followed by a 10 or a new 1.
        Assert.Empty(Printed(r, CompSellSetup));
        Assert.Empty(Printed(r, CompSellSetup9));
    }

    [Fact]
    public void A_decline_with_no_flip_before_it_is_not_a_setup()
    {
        // Falling from the first bar: no bar closes above the close four before it, so no flip.
        var r = Calc(Bars(Plain(Enumerable.Range(0, 30).Select(i => 200.0 - 2 * i))));
        Assert.Empty(Printed(r, CompBuySetup));
        Assert.Empty(Printed(r, CompBuySetup9));
        Assert.Empty(Printed(r, CompBuyCountdown));
    }

    [Fact]
    public void A_bar_that_breaks_the_run_starts_the_count_again()
    {
        // Bars 7 to 11 count 1 to 5; bar 12 closes at 110, above bar 8's 109, and ends the run.
        // Bar 13 is below bar 9 again after that flip, so it is a new 1.
        var closes = RiseThenFall(6).Concat(new[] { 110.0, 104, 102, 100 });
        var r = Calc(Bars(Plain(closes)));
        Assert.Equal(new List<(int, int)> { (7, 1), (8, 2), (9, 3), (10, 4), (11, 5), (13, 1), (14, 2), (15, 3) },
            Printed(r, CompBuySetup));
        Assert.Empty(Printed(r, CompBuySetup9));
    }

    [Fact]
    public void Perfection_needs_bar_eight_or_nine_to_reach_the_lows_of_six_and_seven()
    {
        var plain = Calc(Bars(Plain(RiseThenFall(10))));
        Assert.Equal(1, plain[CompBuySetup9 + PerfectedSuffix][15]);

        // Long lower wicks on setup bars 6 and 7 (bars 12 and 13) put their lows under bars 8 and 9's.
        var wicks = RiseThenFall(10).Select((c, i) => (c, i is 12 or 13 ? 10.0 : 0.5));
        var r = Calc(Bars(wicks));
        Assert.Equal(0, r[CompBuySetup9 + PerfectedSuffix][15]);

        var provider = new Sequential913Provider();
        Assert.Equal("Buy setup 9, not perfected.", provider.GetComponentSpeech(CompBuySetup9, r[CompBuySetup9][15], default, r, 15));
        Assert.Equal("Buy setup 9, perfected.", provider.GetComponentSpeech(CompBuySetup9, plain[CompBuySetup9][15], default, plain, 15));
    }

    [Fact]
    public void The_countdown_counts_from_the_nine_to_thirteen_and_the_thirteen_is_its_own_component()
    {
        var r = Calc(Bars(Plain(RiseThenFall(24))));
        Assert.Equal(Enumerable.Range(1, 12).Select(k => (14 + k, k)).ToList(), Printed(r, CompBuyCountdown));
        Assert.Equal(new List<(int, int)> { (27, 13) }, Printed(r, CompBuyCountdown13));
        Assert.Empty(Printed(r, CompSellCountdown));
    }

    [Fact]
    public void A_thirteen_whose_low_is_above_bar_eights_close_is_deferred()
    {
        // Countdown 8 on bar 22 at 81; a rally to 93; counts 9 to 12 on bars 28 to 31 (down to 83).
        // Bar 32 closes at 82.5, at or below bar 30's low, but its low of 82 is above 81: deferred.
        // Bar 33's low of 79.5 is not, and it is the 13.
        var closes = RiseThenFall(17).Concat(new[] { 84.0, 87, 90, 93, 91, 89, 87, 85, 83, 82.5, 80 });
        var bars = Bars(Plain(closes));
        var r = Calc(bars);
        Assert.Equal(new[] { 28, 29, 30, 31 }, Printed(r, CompBuyCountdown).Where(p => p.Label > 8).Select(p => p.Bar));
        Assert.Equal(new List<(int, int)> { (33, 13) }, Printed(r, CompBuyCountdown13));
        Assert.True(double.IsNaN(r[CompBuyCountdown][32]));
        Assert.Contains("Buy countdown 13 deferred", new Sequential913Provider().GetDetailFact(Code, bars.ToArray(), r, 32, new()));
    }

    [Fact]
    public void A_sell_setup_cancels_the_buy_countdown_and_starts_its_own()
    {
        // Countdown 1 to 3 on bars 15 to 17, then a slow climb of 1 a bar: sell setup 1 on bar 19,
        // 9 on bar 27, well under the buy setup's high of 111.5.
        var closes = RiseThenFall(11).Concat(Enumerable.Range(1, 12).Select(k => 93.0 + k));
        var bars = Bars(Plain(closes));
        var r = Calc(bars);
        Assert.Equal(new[] { 15, 16, 17 }, Printed(r, CompBuyCountdown).Select(p => p.Bar));
        Assert.Equal(new List<(int, int)> { (27, 9) }, Printed(r, CompSellSetup9));
        Assert.Equal(new List<(int, int)> { (27, 1), (28, 2) }, Printed(r, CompSellCountdown).Take(2).ToList());
        Assert.Contains("Buy countdown cancelled, a sell setup completed",
            new Sequential913Provider().GetDetailFact(Code, bars.ToArray(), r, 27, new()));
    }

    [Fact]
    public void A_close_above_the_buy_setups_high_cancels_its_countdown()
    {
        // The setup's highest high is bar 7's 111.5. Bar 17 closes at 112; the fall after it
        // would have counted (each close is under the low two bars before), but nothing does.
        var closes = RiseThenFall(11).Concat(new[] { 112.0, 104, 96, 88, 80 });
        var bars = Bars(Plain(closes));
        var r = Calc(bars);
        Assert.Equal(111.5, r[CompResistance][17]);
        Assert.Equal(new[] { 15, 16 }, Printed(r, CompBuyCountdown).Select(p => p.Bar));
        Assert.Contains("Buy countdown cancelled, price closed above the setup's high",
            new Sequential913Provider().GetDetailFact(Code, bars.ToArray(), r, 17, new()));
    }

    [Fact]
    public void A_new_buy_setup_restarts_the_countdown()
    {
        // Countdown 1 and 2 on bars 15 and 16. Bar 17 (100) breaks the run; bars 18 to 26 are a
        // second buy setup whose long lower wicks keep every close above the low two bars back, so
        // the countdown does not move. The steep fall after it counts again from 1, not from 3.
        var seq = RiseThenFall(11).Select(c => (c, 0.5))
            .Append((100.0, 10.0))
            .Concat(Enumerable.Range(0, 9).Select(k => (96.0 - 2 * k, 5.0)))
            .Concat(Enumerable.Range(1, 4).Select(k => (78.0 - 2 * k, 0.5)));
        var r = Calc(Bars(seq));
        Assert.Equal(new List<(int, int)> { (15, 9), (26, 9) }, Printed(r, CompBuySetup9));
        Assert.Equal(new List<(int, int)> { (15, 1), (16, 2), (27, 1), (28, 2), (29, 3), (30, 4) }, Printed(r, CompBuyCountdown));
    }

    [Fact]
    public void A_value_known_at_a_bars_close_never_changes_when_later_bars_arrive()
    {
        var closes = RiseThenFall(17).Concat(new[] { 84.0, 87, 90, 93, 91, 89, 87, 85, 83, 82.5, 80 }).ToList();
        var full = Calc(Bars(Plain(closes)));
        for (int n = 1; n <= closes.Count; n++)
        {
            var part = Calc(Bars(Plain(closes.Take(n))));
            foreach (var kv in part)
                for (int i = 0; i < n; i++)
                    Assert.True(kv.Value[i].Equals(full[kv.Key][i]), $"{kv.Key} at bar {i} differs with {n} bars");
        }
    }

    [Fact]
    public void The_nine_and_the_thirteen_sound_unlike_the_counts()
    {
        var comps = new Sequential913Provider().GetIndicators().Single().Components.ToDictionary(c => c.Name);
        foreach (var (count, done) in new[] { (CompBuySetup, CompBuySetup9), (CompSellSetup, CompSellSetup9),
                     (CompBuyCountdown, CompBuyCountdown13), (CompSellCountdown, CompSellCountdown13) })
        {
            Assert.Equal("Ping", comps[count].DefaultEnvelopeType);
            Assert.Equal("Ping", comps[done].DefaultEnvelopeType);
            Assert.NotEqual(comps[count].DefaultBaseFrequency, comps[done].DefaultBaseFrequency);
            Assert.NotNull(comps[done].DefaultSoundPatchId);
            Assert.Null(comps[count].DefaultSoundPatchId);
            Assert.NotEqual(comps[count].DisplayType, comps[done].DisplayType);
        }
        Assert.NotEqual(comps[CompBuySetup9].DefaultSoundPatchId, comps[CompBuyCountdown13].DefaultSoundPatchId);
    }

    // ── Heard ──────────────────────────────────────────────────────────────────

    private static ChartSeries SequentialSeries(List<Ohlcv> bars)
    {
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var meta = new Sequential913Provider().GetIndicators().Single();
        var s = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService())
            .CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new List<(string, string)>(), null);
        foreach (var kv in Calc(bars)) s.Data.ComponentData[kv.Key] = kv.Value;
        return s;
    }

    /// <summary>Everything said on arriving at <paramref name="index"/> from the left, on the
    /// candles with "Announce other markers on the bar while navigating" on, or, given
    /// <paramref name="component"/>, on that component of the 9-13.</summary>
    private static string Arrive(List<Ohlcv> bars, int index, string? component = null)
    {
        var candlesConfig = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
        var candleData = new SeriesDataBuffer { SeriesId = candlesConfig.Id };
        var body = new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true };
        candlesConfig.Components.Add(body);
        candleData.ComponentData[body.Name] = new double[bars.Count];
        var candles = new ChartSeries(candlesConfig, candleData);
        var sequential = SequentialSeries(bars);

        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars.ToArray()),
            ActiveSeries = ImmutableList.Create(candles, sequential),
            FocusedSeriesId = component == null ? candles.Id : sequential.Id,
            FocusedComponentIndex = component == null ? 0 : sequential.Components.FindIndex(c => c.Name == component),
            CurrentDataIndex = index,
            InitStatus = InitializationStatus.Ready,
            LastInteractionContext = InteractionContext.Component,
        };
        var settings = Substitute.For<IAppSettings>();
        settings.SpeakOtherSignalsWhileArrowing.Returns(true);
        var spy = new SpySpeechRouter();
        // The formatter finds the provider's own words through the engine, as in the app.
        var engine = Substitute.For<IIndicatorEngine>();
        engine.GetProvider(Code).Returns(new Sequential913Provider());
        var formatter = new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine);
        new NavigationFeedbackManager(spy, formatter, settings) { IsSpeechEnabled = true }
            .HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");
        return string.Join(" | ", spy.SpokenTexts.Where(t => t != null));
    }

    [Fact]
    public void Arrowing_the_candles_says_the_count_printed_on_the_bar()
    {
        var bars = Bars(Plain(RiseThenFall(17)));
        Assert.Contains("Buy setup 7.", Arrive(bars, 13));
        string nine = Arrive(bars, 15);
        Assert.Contains("Buy setup 9.", nine);
        Assert.Contains("Buy countdown 1.", nine);
        // Each template ends with a full stop and the join adds one; it was said "Buy setup 9..".
        Assert.DoesNotContain("..", nine);
        Assert.Contains("Buy countdown 4.", Arrive(bars, 18));
    }

    [Fact]
    public void Arrowing_a_count_says_its_number_and_a_nine_says_whether_it_is_perfected()
    {
        var bars = Bars(Plain(RiseThenFall(17)));
        Assert.Contains("Buy setup 7.", Arrive(bars, 13, CompBuySetup));
        Assert.Contains("Buy setup 9, perfected.", Arrive(bars, 15, CompBuySetup9));
        Assert.Contains("Buy countdown 3.", Arrive(bars, 17, CompBuyCountdown));
    }

    [Fact]
    public void A_label_word_is_the_number_beside_the_marker()
    {
        var bars = Bars(Plain(RiseThenFall(17)));
        var s = SequentialSeries(bars);
        var comp = s.Components.Single(c => c.Name == CompBuySetup);
        Assert.Equal("Buy setup 7.", SignalClauseSpeech.FillLabel("Buy setup {label}.", s, comp, 13));
        Assert.Equal("Buy setup .", SignalClauseSpeech.FillLabel("Buy setup {label}.", s, comp, 3));
        Assert.Equal("Swing high {price}.", SignalClauseSpeech.FillLabel("Swing high {price}.", s, comp, 13));
    }
}
