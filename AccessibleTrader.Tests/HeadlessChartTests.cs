using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The narration ladder with no store, no bus and no browser</b> — Phase 3 D4 of the
/// background monitor. Cody, 2026-09-11: <i>"I also want the narration ladder to also be spoken
/// when the browser is closed too."</i>
///
/// <para>
/// These run the REAL stack a saved tab goes through headless: the real Core and Skender
/// providers, the real engine and mapper, the real model factory (so a saved config is rebuilt
/// exactly as a workspace load rebuilds it — derived name, current component defaults, saved N
/// selection), and the very same <see cref="NarrationScanner"/> the focused chart uses. The bars
/// arrive the way the monitor fetches them: a wide window once, then a three-bar catch-up window
/// every poll whose newest bar is the FORMING one.
/// </para>
/// </summary>
public sealed class HeadlessChartTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChartIdentity Btc = new("Spot", "Bitstamp", "BTC/USD", "1h");

    // ── The stack ────────────────────────────────────────────────────────────

    private static HeadlessChartFactory Stack()
    {
        var providers = new List<IIndicatorProvider> { new CoreIndicatorProvider(), new SkenderTrendProvider() };
        var indicators = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicators, new CustomIndicatorRegistry(), providers);
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var prefs = new MockIndicatorPreferencesService();
        var factory = new IndicatorModelFactory(styling, prefs);
        return new HeadlessChartFactory(indicators, engine, new IndicatorStateMapper(), factory, prefs, new IndicatorContextAnalyzer());
    }

    /// <summary>The real Volume series as the autosave writes it: one Bar component mapped to volume.</summary>
    private static SeriesConfig SavedVolume(bool narrated = true)
    {
        var cfg = new SeriesConfig
        {
            Id = CoreSeriesIds.Volume, Name = "Volume", FriendlyName = "Volume", IndicatorCode = "VOLUME",
            Pane = "Volume", IsAutoNarrated = narrated, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Volume", DisplayName = "Volume", DisplayType = ComponentDisplayType.Bar,
            Role = ComponentRole.Volume, DataMapping = "volume", IsVisible = true,
        });
        return cfg;
    }

    /// <summary>A saved EMA, with whatever friendly name the file happens to carry.</summary>
    private static SeriesConfig SavedEma(string id, int period, bool narrated, string friendlyName = "EMA")
    {
        var cfg = new SeriesConfig
        {
            Id = id, Name = "EMA", FriendlyName = friendlyName, IndicatorCode = "Ema", Pane = "Main",
            IsAutoNarrated = narrated, IsVisible = true,
        };
        cfg.Parameters["lookbackPeriods"] = period;
        return cfg;
    }

    // ── Bars, the way a provider returns them ─────────────────────────────────

    private const double FormingVolume = 5;

    /// <summary>Closed bars carry their final volume, 1,000 × (hour + 1); the newest bar is
    /// forming and carries a partial one. Close is 100 unless <paramref name="close"/> says otherwise.</summary>
    private static List<Ohlcv> Window(int fromHour, int toHour, Func<int, double>? close = null)
    {
        var bars = new List<Ohlcv>();
        for (int h = fromHour; h <= toHour; h++)
        {
            double c = close?.Invoke(h) ?? 100;
            double v = h == toHour ? FormingVolume : 1000.0 * (h + 1);
            bars.Add(new Ohlcv(T0.AddHours(h), c, c, c, c, v));
        }
        return bars;
    }

    private static Task<HeadlessObservation> Full(HeadlessChart n, List<Ohlcv> bars)
        => n.ObserveAsync(bars, isFullHistory: true, CancellationToken.None);

    private static Task<HeadlessObservation> CatchUp(HeadlessChart n, List<Ohlcv> bars)
        => n.ObserveAsync(bars, isFullHistory: false, CancellationToken.None);

    // ── The reading at the close ──────────────────────────────────────────────

    [Fact]
    public async Task A_narrated_volume_pane_reads_the_closed_bars_FINAL_volume_on_the_next_poll()
    {
        var n = Stack().Create(Btc, new[] { SavedVolume() });
        Assert.True(n.HasNarratedSeries);

        // First sighting: hour 99 is forming with a partial volume of 5. Seeds, says nothing.
        var first = await Full(n, Window(0, 99));
        Assert.Null(first.Narration);
        Assert.False(first.BarClosed);

        // Next poll: hour 99 has closed at its final volume and hour 100 is forming.
        var next = await CatchUp(n, Window(98, 100));

        Assert.True(next.BarClosed);
        Assert.NotNull(next.Narration);
        // The CLOSED bar's FINAL value — the buffer replaced the partial bar it held, and the
        // forming bar (hour 100, volume 5) is not read.
        Assert.Contains("Volume 100,000", next.Narration, StringComparison.Ordinal);
        Assert.DoesNotContain("Volume 5", next.Narration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_poll_that_closed_no_bar_says_nothing()
    {
        var n = Stack().Create(Btc, new[] { SavedVolume() });
        await Full(n, Window(0, 99));

        // The forming bar refreshed in place; nothing closed.
        var same = await CatchUp(n, Window(97, 99));

        Assert.False(same.BarClosed);
        Assert.Null(same.Narration);
    }

    [Fact]
    public async Task A_series_not_under_N_is_never_narrated_and_costs_nothing()
    {
        var n = Stack().Create(Btc, new[] { SavedVolume(narrated: false) });

        Assert.False(n.HasNarratedSeries);
        Assert.Null((await Full(n, Window(0, 99))).Narration);
        Assert.Null((await CatchUp(n, Window(98, 100))).Narration);
        Assert.Equal(0, n.BufferedBars);
    }

    [Fact]
    public async Task After_missed_polls_only_the_newest_closed_bar_is_read()
    {
        // The laptop was asleep for three bars. The newest closed bar is the only reading still
        // true; three readings arriving together would be worse than the silence they follow.
        var n = Stack().Create(Btc, new[] { SavedVolume() });
        await Full(n, Window(0, 99));

        var caught = await CatchUp(n, Window(98, 102));   // 100, 101, 102 are new; 102 forming

        Assert.True(caught.BarClosed);
        Assert.Contains("Volume 102,000", caught.Narration, StringComparison.Ordinal);
        Assert.DoesNotContain("100,000", caught.Narration, StringComparison.Ordinal);
        Assert.DoesNotContain("101,000", caught.Narration, StringComparison.Ordinal);
    }

    // ── The EMA cross, with the name a workspace load would give it ───────────

    [Fact]
    public async Task An_EMA_cross_at_the_close_is_narrated_under_its_DERIVED_name()
    {
        // The saved friendly name is the parameter recitation from the era before the namer
        // was fixed — exactly what Cody's own workspace files still carry. A silent EMA 50 sits
        // beside it, so the cohort has two and the name has to say which.
        var saved = new[]
        {
            SavedEma("ema-20", 20, narrated: true, friendlyName: "EMA 20 9 1.2 0.2"),
            SavedEma("ema-50", 50, narrated: false),
        };
        var n = Stack().Create(Btc, saved);

        var narrated = Assert.Single(n.NarratedSeries);
        Assert.Equal("EMA 20", narrated.FriendlyName);

        // Flat at 100 for a hundred bars: the EMA sits on the close, price is not above it.
        await Full(n, Window(0, 99));

        // Hour 99 closes at 200; the EMA lags, so the close is now above it.
        var next = await CatchUp(n, Window(98, 100, close: h => h >= 99 ? 200 : 100));

        Assert.NotNull(next.Narration);
        Assert.Contains("Price crossed above EMA 20", next.Narration, StringComparison.Ordinal);
        Assert.DoesNotContain("EMA 20 9 1.2 0.2", next.Narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Bars_needed_covers_the_indicators_warmup_plus_the_scanners_look_back()
    {
        var volumeOnly = Stack().Create(Btc, new[] { SavedVolume() });
        var ema200 = Stack().Create(Btc, new[] { SavedEma("e", 200, narrated: true) });

        Assert.Equal(HeadlessChart.MinBars, volumeOnly.BarsNeeded);
        Assert.True(ema200.BarsNeeded > 200, $"an EMA 200 needs more than 200 bars, got {ema200.BarsNeeded}");
        Assert.True(ema200.BarsNeeded <= HeadlessChart.MaxBars);

        // Cold buffer: ask for the window. Warm: ask for the catch-up.
        Assert.Equal(volumeOnly.BarsNeeded, volumeOnly.FetchLimit);
    }

    [Fact]
    public async Task Once_warm_the_next_fetch_is_the_catch_up_window()
    {
        var n = Stack().Create(Btc, new[] { SavedVolume() });
        await Full(n, Window(0, 99));
        Assert.Equal(HeadlessChart.CatchUpLimit, n.FetchLimit);
    }

    // ── Gaps and the bounded buffer ───────────────────────────────────────────

    [Fact]
    public async Task A_gap_wider_than_the_catch_up_window_asks_for_history_then_reseeds()
    {
        var n = Stack().Create(Btc, new[] { SavedVolume() });
        await Full(n, Window(0, 99));

        // Fifty bars later. The three-bar window does not reach back to hour 99.
        var gap = await CatchUp(n, Window(150, 152));
        Assert.True(gap.NeedsMoreHistory);
        Assert.Null(gap.Narration);

        // The full window does not reach back either — the newest bars are the only ones still
        // true, so the buffer is replaced and re-seeded. Nothing is announced for the gap.
        var reseeded = await Full(n, Window(103, 152));
        Assert.False(reseeded.NeedsMoreHistory);
        Assert.Null(reseeded.Narration);
        Assert.Equal(50, n.BufferedBars);

        // And the next close after that reads normally.
        var next = await CatchUp(n, Window(151, 153));
        Assert.Contains("Volume 153,000", next.Narration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_buffer_is_trimmed_and_the_reading_stays_on_the_right_bar()
    {
        // Volume needs MinBars (50); the first window is twice that, so the first append tips
        // the buffer over the cap and the front is trimmed. The scanner's indices move with it —
        // which is what keeps the reading on the bar that closed rather than a bar 51 slots away.
        var n = Stack().Create(Btc, new[] { SavedVolume() });
        await Full(n, Window(0, 99));
        Assert.Equal(100, n.BufferedBars);

        var first = await CatchUp(n, Window(98, 100));
        Assert.Equal(HeadlessChart.MinBars, n.BufferedBars);
        Assert.Contains("Volume 100,000", first.Narration, StringComparison.Ordinal);

        var second = await CatchUp(n, Window(99, 101));
        Assert.Contains("Volume 101,000", second.Narration, StringComparison.Ordinal);
    }

    // ── The signature the monitor keys a narrator on ──────────────────────────

    [Fact]
    public void The_signature_changes_when_N_is_pressed_and_not_when_a_colour_is()
    {
        var a = SavedVolume();
        string before = HeadlessChartFactory.Signature(new[] { a });

        a.Components[0].ColorHex = "#123456";
        Assert.Equal(before, HeadlessChartFactory.Signature(new[] { a }));

        a.Components[0].IsAutoNarrated = true;
        Assert.NotEqual(before, HeadlessChartFactory.Signature(new[] { a }));

        Assert.True(HeadlessChartFactory.HasNarratedSeries(new[] { a }));
        Assert.False(HeadlessChartFactory.HasNarratedSeries(new[] { SavedVolume(narrated: false) }));
    }

    // ── The scanner's indices after a trim ────────────────────────────────────

    [Fact]
    public void After_ShiftIndices_a_new_marker_beyond_the_old_bound_is_announced_and_the_old_one_is_not_repeated()
    {
        // A marker series: one Dot component, NaN everywhere except where a signal printed.
        static ChartSeries Markers(int length, params int[] dotsAt)
        {
            var cfg = new SeriesConfig { Id = "sig", Name = "Sig", FriendlyName = "Sig", IndicatorCode = "SIG", Pane = "Main", IsAutoNarrated = true };
            cfg.Components.Add(new ComponentConfig { Name = "Buy", DisplayName = "Buy", DisplayType = ComponentDisplayType.Dot, IsVisible = true });
            var data = new double[length];
            Array.Fill(data, double.NaN);
            foreach (int i in dotsAt) data[i] = 1;
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            buf.ComponentData["Buy"] = data;
            return new ChartSeries(cfg, buf);
        }
        static WorkspaceState State(int bars, ChartSeries s) => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Window(0, bars - 1)),
            ActiveSeries = ImmutableList.Create(s),
            CurrentDataIndex = bars - 1,
        };

        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        scanner.Seed(Markers(100), State(100, Markers(100)));

        // Hour 99 closes with a dot on it.
        var s1 = Markers(101, 99);
        string? first = scanner.ScanAll(new[] { s1 }, State(101, s1), closedBound: 99, isBarClose: true);
        Assert.Contains("Buy", first, StringComparison.Ordinal);

        // The owner drops ten bars off the front: the same dot is now at 89.
        scanner.ShiftIndices(10);
        var s2 = Markers(91, 89);
        Assert.Null(scanner.ScanAll(new[] { s2 }, State(91, s2), closedBound: 89, isBarClose: true));

        // Six bars later a new dot prints at 95 — below the OLD bound of 99. Without the shift
        // the scanner would still believe it had seen everything up to 99 and drop it.
        var s3 = Markers(97, 89, 95);
        string? second = scanner.ScanAll(new[] { s3 }, State(97, s3), closedBound: 95, isBarClose: true);
        Assert.Contains("Buy", second, StringComparison.Ordinal);
    }
}
