using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace AccessibleTrader.Tests;

/// <summary>
/// The DBSI-style indicator as Cody meets it: what the Heikin-Ashi switch does, what he hears on
/// arrival and on Alt+Shift+D, what the narrator says unprompted, and what survives a restart.
/// The numbers themselves are <see cref="DbsiStyleGoldenTests"/>'.
/// </summary>
public sealed class DbsiStyleProviderTests
{
    private static readonly IndicatorModelFactory Factory = new(DbsiChartHarness.Styling(), new MockIndicatorPreferencesService());

    private static List<Ohlcv> RealBars() => DbsiStyleGoldenTests.Bars(DbsiDigitisedCharts.All.Single(c => c.Key == "G1D"));

    private static Dictionary<string, double[]> Calculate(IReadOnlyList<Ohlcv> bars, Dictionary<string, object> p)
    {
        var results = new Dictionary<string, double[]>();
        new DbsiStyleProvider().Calculate(DbsiStyleProvider.Code, bars.ToArray(), p, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    // ── Heikin-Ashi ──────────────────────────────────────────────────────────────

    /// <summary>
    /// "Compute on Heikin-Ashi candles", through the real Calculate and its parameter dictionary:
    /// ON gives exactly what OFF gives on the Heikin-Ashi transform of the same bars (the chart's
    /// own transform, from the first bar), and that is not what OFF gives on the raw bars. A switch
    /// that parsed but was never read would make the last assertion fail.
    /// </summary>
    [Fact]
    public void Heikin_Ashi_on_computes_from_the_charts_own_Heikin_Ashi_candles()
    {
        var raw = RealBars();
        var ha = ChartMath.CalculateHeikinAshi(raw.ToList());
        var on = Calculate(raw, new() { [DbsiStyleProvider.ParamHeikinAshi] = 1.0 });
        var offOnHa = Calculate(ha, new() { [DbsiStyleProvider.ParamHeikinAshi] = 0.0 });
        var offOnRaw = Calculate(raw, new() { [DbsiStyleProvider.ParamHeikinAshi] = "false" });

        foreach (var comp in new[] { DbsiStyleProvider.CompBull, DbsiStyleProvider.CompBear, DbsiStyleProvider.CompTrend, DbsiStyleProvider.CompTrendSource })
            Assert.Equal(offOnHa[comp], on[comp]);
        int differ = Enumerable.Range(0, raw.Count).Count(i => !double.IsNaN(on[DbsiStyleProvider.CompBull][i])
            && on[DbsiStyleProvider.CompBull][i] != offOnRaw[DbsiStyleProvider.CompBull][i]);
        Assert.True(differ > 10, $"Heikin-Ashi on and off agree on all but {differ} bars — the switch is not reaching the maths.");
        // The trend's source close is the HA close when on, the raw close when off.
        Assert.Equal(ha[^1].Close, on[DbsiStyleProvider.CompTrendSource][^1]);
        Assert.Equal(raw[^1].Close, offOnRaw[DbsiStyleProvider.CompTrendSource][^1]);
    }

    [Fact]
    public void Heikin_Ashi_is_on_by_default()
    {
        var raw = RealBars();
        Assert.Equal(Calculate(raw, new() { [DbsiStyleProvider.ParamHeikinAshi] = true })[DbsiStyleProvider.CompBull],
                     Calculate(raw, new())[DbsiStyleProvider.CompBull]);
        var meta = new DbsiStyleProvider().GetIndicators()[0];
        Assert.Equal(true, meta.Parameters.Single(p => p.Name == DbsiStyleProvider.ParamHeikinAshi).DefaultValue);
    }

    /// <summary>Cody, 2026-10-22: "call dbsi cipher dbsi" — the name in the indicator list, on the series
    /// and in speech, beside Cipher A, B and SR. The code stays DBSI_STYLE so saved workspaces still load.</summary>
    [Fact]
    public void It_is_called_Cipher_DBSI_and_keeps_its_code()
    {
        var provider = new DbsiStyleProvider();
        Assert.Equal("Cipher DBSI", provider.Name);
        Assert.Equal("Cipher DBSI", provider.GetIndicators()[0].Name);
        Assert.Equal("DBSI_STYLE", provider.GetIndicators()[0].Code);
    }

    // ── The trend line ───────────────────────────────────────────────────────────

    /// <summary>The 200 EMA, SMA-seeded (Pine's ta.ema): its first value is the mean of the first 200 closes.</summary>
    [Fact]
    public void The_trend_is_a_200_bar_EMA_seeded_with_the_mean_of_its_first_200_closes()
    {
        var bars = CausalityProbeSeries.Bars(0, 400);
        var r = DbsiStyleProvider.Compute(bars.ToArray(), new DbsiStyleProvider.Settings(7, 7, 200, false));
        Assert.True(double.IsNaN(r.Trend[198]));
        Assert.Equal(bars.Take(200).Average(b => b.Close), r.Trend[199], 9);
        double a = 2.0 / 201;
        Assert.Equal(a * bars[200].Close + (1 - a) * r.Trend[199], r.Trend[200], 9);
    }

    /// <summary>
    /// Why "DBSI_STYLE.Trend" sits under "Inherent" in the prepend-causality exemptions: the
    /// disagreement left by older bars arriving SHRINKS with distance from the array start (an EMA
    /// forgetting its seed), where an anchor to index 0 would stay the same size forever.
    /// </summary>
    [Fact]
    public void Trend_ResidueConverges_ItIsNotAnchoredToArrayIndexZero()
    {
        var bars = CausalityProbeSeries.Bars(0, 3000);
        var full = DbsiStyleProvider.Compute(bars.ToArray(), new DbsiStyleProvider.Settings(7, 7, 200, true)).Trend;
        var cut = DbsiStyleProvider.Compute(bars.Skip(91).ToArray(), new DbsiStyleProvider.Settings(7, 7, 200, true)).Trend;
        double Rel(int i) => Math.Abs(full[i] - cut[i - 91]) / Math.Abs(full[i]);
        double early = Rel(800), late = Rel(2800);
        Assert.True(early > 0, "fixture: some residue must exist to converge");
        Assert.True(late < early * 1e-3, $"residue {early:E2} at bar 800, {late:E2} at bar 2800 — not converging");
        Assert.True(late < 1e-9, $"residue at bar 2800 is {late:E2}");
    }

    // ── The events ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A hand-over fires when the leader CHANGES from the last side that led; a tie in between
    /// hands nothing over. An entry fires on the bar a side reaches its score from below.
    /// </summary>
    [Fact]
    public void Control_hand_overs_and_entry_scores_fire_on_the_bar_they_happen()
    {
        var bars = RealBars();
        var r = DbsiStyleProvider.Compute(bars.ToArray(), new DbsiStyleProvider.Settings(7, 7, 200, false));
        int lastLeader = 0, flips = 0, entries = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            if (double.IsNaN(r.Control[i])) { Assert.True(double.IsNaN(r.BullsTakeControl[i]) && double.IsNaN(r.BearsTakeControl[i])); continue; }
            int c = (int)r.Control[i];
            bool expectBulls = c > 0 && lastLeader < 0, expectBears = c < 0 && lastLeader > 0;
            Assert.Equal(expectBulls, !double.IsNaN(r.BullsTakeControl[i]));
            Assert.Equal(expectBears, !double.IsNaN(r.BearsTakeControl[i]));
            if (expectBulls || expectBears) flips++;
            if (c != 0) lastLeader = c;

            bool longEntry = i > 0 && !double.IsNaN(r.Bull[i - 1]) && r.Bull[i] >= 7 && r.Bull[i - 1] < 7;
            Assert.Equal(longEntry, !double.IsNaN(r.LongEntry[i]));
            if (longEntry) { entries++; Assert.Equal(r.Bull[i], r.LongEntry[i]); }
        }
        Assert.True(flips >= 5 && entries >= 3, $"fixture too quiet: {flips} hand-overs, {entries} long entries");
    }

    /// <summary>
    /// Ties, which real scores rarely produce on a short fixture: bulls → tie → bulls hands
    /// nothing over; bears → tie → bulls hands over on the bulls' bar, not on the tie.
    /// </summary>
    [Fact]
    public void A_tie_hands_nothing_over()
    {
        double[] bull = { 9, 4, 9, 2, 4, 9 }, bear = { 2, 4, 2, 9, 4, 2 };
        var control = bull.Zip(bear, (b, r) => (double)Math.Sign(b - r)).ToArray();   // + 0 + − 0 +
        var (bullsTake, bearsTake, _, _) = DbsiStyleProvider.Events(bull, bear, control, 7, 7);
        Assert.Equal(new[] { 5 }, Enumerable.Range(0, 6).Where(i => !double.IsNaN(bullsTake[i])));
        Assert.Equal(new[] { 3 }, Enumerable.Range(0, 6).Where(i => !double.IsNaN(bearsTake[i])));
    }

    // ── Speech: arriving on a component ──────────────────────────────────────────

    private static (ChartSeries Series, List<Ohlcv> Bars, IndicatorEngine Engine) Chart(List<Ohlcv>? withBars = null)
    {
        var providers = new List<IIndicatorProvider> { new DbsiStyleProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance), new CustomIndicatorRegistry(), providers);
        var bars = withBars ?? RealBars();
        var s = DbsiChartHarness.Dbsi(bars, heikinAshi: false);
        return (s, bars, engine);
    }

    private static string Landing(ChartSeries series, List<Ohlcv> bars, IndicatorEngine engine, string component, int bar)
    {
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = series.Components.FindIndex(c => c.Name == component),
            CurrentDataIndex = bar,
            ViewportStartIndex = Math.Max(0, bar - 50),
            ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };
        return new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine)
            .FormatPointFeedback(state, isXMove: false, isYMove: true, series, bars[bar], "");
    }

    /// <summary>
    /// Arriving on Bulls or Bears through the speech router's own formatter: the reading, with
    /// "minus" spelled out and who is in control. The provider reads its sibling by DISPLAY name
    /// (that is how the formatter keys the data), so a rename on one side of that would say
    /// "Calculating" here.
    /// </summary>
    [Fact]
    public void Arriving_on_the_scores_says_both_numbers_and_who_is_in_control()
    {
        var (s, bars, engine) = Chart();
        var bull = s.GetComponentData(DbsiStyleProvider.CompBull);
        var bear = s.GetComponentData(DbsiStyleProvider.CompBear);
        int i = Enumerable.Range(0, bars.Count).Last(k => bear[k] < 0 && bull[k] > bear[k]);

        string onBull = Landing(s, bars, engine, DbsiStyleProvider.CompBull, i);
        string onBear = Landing(s, bars, engine, DbsiStyleProvider.CompBear, i);

        Assert.Contains($"Bulls {(int)bull[i]}, bears minus {-(int)bear[i]}, bulls in control", onBull);
        Assert.Contains($"Bears minus {-(int)bear[i]}, bulls {(int)bull[i]}, bulls in control", onBear);
        Assert.DoesNotContain(".00", onBull);
    }

    [Fact]
    public void The_reading_names_the_leader_or_a_tie()
    {
        Assert.Equal("Bulls 15, bears minus 5, bulls in control", DbsiStyleProvider.Reading(15, -5));
        Assert.Equal("Bulls minus 4, bears 12, bears in control", DbsiStyleProvider.Reading(-4, 12));
        Assert.Equal("Bulls 4, bears 4, tied", DbsiStyleProvider.Reading(4, 4));
    }

    [Fact]
    public void Arriving_on_the_trend_says_its_price_and_which_side_the_close_is_on()
    {
        var (s, bars, engine) = Chart(CausalityProbeSeries.Bars(0, 400));
        var t = s.GetComponentData(DbsiStyleProvider.CompTrend);
        var src = s.GetComponentData(DbsiStyleProvider.CompTrendSource);
        int i = bars.Count - 1;
        Assert.False(double.IsNaN(t[i]), "fixture: the trend must have warmed up");
        string said = Landing(s, bars, engine, DbsiStyleProvider.CompTrend, i);
        Assert.Contains(src[i] > t[i] ? "price above" : "price below", said);
    }

    // ── Alt+Shift+D ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The ladder line for this indicator, with the candles focused: both scores as whole numbers,
    /// the trend, then the state words — who is in control and which side of the trend.
    /// </summary>
    [Fact]
    public void Alt_Shift_D_reads_the_scores_the_leader_and_the_trend_side()
    {
        var bars = CausalityProbeSeries.Bars(2, 400);
        var s = DbsiChartHarness.Dbsi(bars, heikinAshi: false);
        var bull = s.GetComponentData(DbsiStyleProvider.CompBull);
        var bear = s.GetComponentData(DbsiStyleProvider.CompBear);
        int i = bars.Count - 1;
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = i,
            ActiveSeries = ImmutableList.Create(DbsiChartHarness.Candles(), s),
            FocusedSeriesId = CoreSeriesIds.Candles,
            ViewportStartIndex = 0,
            ViewportLength = bars.Count,
        };
        var bus = new SpyEventBus();
        new BarDetailService(bus, null).AnnounceDetails(state);
        string said = bus.Log.OfType<AnnouncementEvent>().Last().Message;

        Assert.Contains($"Bulls {DbsiStyleProvider.Spoken(bull[i])}", said);
        Assert.Contains($"Bears {DbsiStyleProvider.Spoken(bear[i])}", said);
        Assert.Contains(DbsiStyleProvider.ControlWords(bull[i], bear[i]), said);
        Assert.Contains("the trend line", said);
        Assert.DoesNotContain("Trend source", said);
        Assert.DoesNotContain("Control", said);
    }

    // ── Narration ────────────────────────────────────────────────────────────────

    private sealed class NoContexts : IIndicatorContextAnalyzer
    {
        public void RegisterDefinition(IndicatorContextDefinition def) { }
        public bool HasZoneThresholds(string indicatorCode, string componentName) => false;
        public IndicatorContext? AnalyzeAt(ChartSeries series, string? componentName, int dataIndex) => null;
        public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state) => null;
        public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state) => Enumerable.Empty<IndicatorContext>();
    }

    private static WorkspaceState StateAt(ChartSeries s, List<Ohlcv> bars, int count) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(bars.Take(count).ToList()),
        ActiveSeries = ImmutableList.Create(s),
        CurrentDataIndex = count - 1,
    };

    /// <summary>
    /// Narrating (N on the series): the bar that closes with the other side taking the lead is
    /// announced — "Bulls take control at …" — and a bar that closes with no hand-over is not.
    /// The hand-over components are never painted, and this is the proof they are still heard.
    /// </summary>
    [Fact]
    public void A_bar_closing_with_a_hand_over_is_narrated_and_a_quiet_bar_is_not()
    {
        var bars = RealBars();
        var s = DbsiChartHarness.Dbsi(bars, heikinAshi: false);
        s.Config.IsAutoNarrated = true;
        var bullsTake = s.GetComponentData(DbsiStyleProvider.CompBullsTakeControl);
        var bearsTake = s.GetComponentData(DbsiStyleProvider.CompBearsTakeControl);
        int flip = Enumerable.Range(60, bars.Count - 60).First(k => !double.IsNaN(bullsTake[k]));
        int quiet = Enumerable.Range(60, bars.Count - 60).First(k => double.IsNaN(bullsTake[k]) && double.IsNaN(bearsTake[k])
            && double.IsNaN(s.GetComponentData(DbsiStyleProvider.CompLongEntry)[k]) && double.IsNaN(s.GetComponentData(DbsiStyleProvider.CompShortEntry)[k])
            && SameTrendSide(s, k));

        Assert.Contains("Bulls take control at", Scan(s, bars, flip) ?? "");
        Assert.Null(Scan(s, bars, quiet));
    }

    private static bool SameTrendSide(ChartSeries s, int k)
    {
        var t = s.GetComponentData(DbsiStyleProvider.CompTrend);
        return double.IsNaN(t[k]) || double.IsNaN(t[k - 1]);
    }

    /// <summary>
    /// The live sequence: narration is switched on while bar <paramref name="k"/>−1 is forming and
    /// the series knows only the bars so far (the provider is causal, so that is its full output
    /// cut short); then bar k forms and closes and the series is recalculated over k+2 bars.
    /// </summary>
    private static string? Scan(ChartSeries s, List<Ohlcv> bars, int k)
    {
        var full = s.Data.ComponentData.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var kv in full) s.Data.ComponentData[kv.Key] = kv.Value.Take(k).ToArray();
        var scanner = new NarrationScanner(new NoContexts());
        scanner.Seed(s, StateAt(s, bars, k));
        foreach (var kv in full) s.Data.ComponentData[kv.Key] = kv.Value.Take(k + 2).ToArray();
        string? said = scanner.ScanAll(new[] { s }, StateAt(s, bars, k + 2), k, isBarClose: true);
        foreach (var kv in full) s.Data.ComponentData[kv.Key] = kv.Value;
        return said;
    }

    /// <summary>
    /// Price crossing the trend line on a bar close is narrated through the overlay-cross route —
    /// and ONLY the trend: the hidden, undrawn ±1 Control line and the trend's source close are
    /// lines too, and a price "crossing" Control at 1 is nonsense the undrawn rule keeps out.
    /// </summary>
    [Fact]
    public void Price_crossing_the_trend_line_is_narrated_and_nothing_else_is_crossed()
    {
        var bars = CausalityProbeSeries.Bars(1, 3000);
        var s = DbsiChartHarness.Dbsi(bars, heikinAshi: true);
        s.Config.IsAutoNarrated = true;
        // The hidden lines visible and unmuted, as "show all" and "unmute" would leave them: still
        // never crossed (muted by default, they are already silent; undrawn is what keeps them so).
        foreach (var c in s.Components) { c.IsVisible = true; c.IsMuted = false; }
        var t = s.GetComponentData(DbsiStyleProvider.CompTrend);
        var src = s.GetComponentData(DbsiStyleProvider.CompTrendSource);
        // A bar where the raw close crosses the trend AND the trend's (Heikin-Ashi) source close
        // at once — the bar where narrating the undrawn source line would put words in the clause.
        int cross = Enumerable.Range(300, bars.Count - 301).First(k =>
            (bars[k - 1].Close > t[k - 1]) != (bars[k].Close > t[k])
            && (bars[k - 1].Close > src[k - 1]) != (bars[k].Close > src[k]));
        string said = Scan(s, bars, cross) ?? "";
        Assert.Contains("Price crossed", said);
        Assert.DoesNotContain("Control", said);
        Assert.DoesNotContain("source", said);
    }

    // ── Restore ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A saved chart carrying the indicator, through the path a resumed session takes: the
    /// parameters (label count, the Heikin-Ashi switch), the user's mute and hide come back; the
    /// value labels come back AS value labels (ordinal 25, appended after CandleColor's 24); and
    /// "heard, not painted" is the PROVIDER's — a save that says the hand-over is drawn does not
    /// make it drawn.
    /// </summary>
    [Fact]
    public void A_saved_chart_restores_its_settings_its_value_labels_and_the_providers_undrawn_events()
    {
        Assert.Equal(24, (int)ComponentDisplayType.CandleColor);
        Assert.Equal(25, (int)ComponentDisplayType.ValueLabel);

        var meta = new DbsiStyleProvider().GetIndicators()[0];
        var made = Factory.CreateSeriesFromMetadata(meta, meta.Name, "Main", new List<(string, string)>
        {
            (DbsiStyleProvider.ParamLabelBars, "10"), (DbsiStyleProvider.ParamHeikinAshi, "false"),
            (DbsiStyleProvider.ParamLongEntry, "14"),
        }, null);
        made.Components.Single(c => c.Name == DbsiStyleProvider.CompTrend).IsMuted = true;
        made.Components.Single(c => c.Name == DbsiStyleProvider.CompBear).IsVisible = false;

        string json = JsonConvert.SerializeObject(made.Config);
        Assert.Contains("\"DisplayType\":25", json);
        json = json.Replace("\"IsDrawn\":false", "\"IsDrawn\":true");   // a save that disagrees with the provider
        var saved = JsonConvert.DeserializeObject<SeriesConfig>(json)!;

        var providers = new List<IIndicatorProvider> { new DbsiStyleProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance), new CustomIndicatorRegistry(), providers);
        WorkspaceInitializer.MigrateSeriesConfig(saved, new List<IndicatorMetadata> { meta });
        var back = SeriesManagementService.MaterializeSaved(saved, meta, Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());

        Assert.Equal(10.0, back.Parameters[DbsiStyleProvider.ParamLabelBars]);
        Assert.Equal(0.0, back.Parameters[DbsiStyleProvider.ParamHeikinAshi]);
        Assert.Equal(14.0, back.Parameters[DbsiStyleProvider.ParamLongEntry]);
        Assert.True(back.Components.Single(c => c.Name == DbsiStyleProvider.CompTrend).IsMuted);
        Assert.False(back.Components.Single(c => c.Name == DbsiStyleProvider.CompBear).IsVisible);
        Assert.True(back.Components.Single(c => c.Name == DbsiStyleProvider.CompBull).IsMuted, "Bulls keep their muted default");
        Assert.Equal(ComponentDisplayType.ValueLabel, back.Components.Single(c => c.Name == DbsiStyleProvider.CompBull).DisplayType);
        Assert.Equal(MarkerAnchor.BelowBar, back.Components.Single(c => c.Name == DbsiStyleProvider.CompBull).MarkerAnchor);
        foreach (var ev in new[] { DbsiStyleProvider.CompBullsTakeControl, DbsiStyleProvider.CompBearsTakeControl,
                                   DbsiStyleProvider.CompLongEntry, DbsiStyleProvider.CompShortEntry,
                                   DbsiStyleProvider.CompControl, DbsiStyleProvider.CompTrendSource })
            // Control and Trend source close are left out by default (2026-10-23), undrawn if included.
            Assert.False(back.Components.Concat(back.Config.ExcludedComponents).Single(c => c.Name == ev).IsDrawn, $"{ev} came back drawn");
        Assert.True(back.Components.Single(c => c.Name == DbsiStyleProvider.CompTrend).IsDrawn);

        // And the restored series draws: its labels for the newest 10 bars only.
        var bars = RealBars();
        DbsiChartHarness.Put(back, DbsiStyleProvider.Compute(bars.ToArray(), DbsiStyleProvider.Settings.From(
            back.Parameters.ToDictionary(k => k.Key, v => (object)v.Value))));
        Assert.Equal(bars.Count - 10, Core.Services.Rendering.ValueLabelRenderer.FirstLabelledBar(back, bars.Count));
    }
}
