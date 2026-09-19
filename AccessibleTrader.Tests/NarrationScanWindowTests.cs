using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>WHEN the unprompted narrator is allowed to speak — the three gates that decide it.</b>
///
/// <para>
/// Written for the A2j sabotage set (2026-09-19), which broke each of these gates one at a time
/// and watched 7,880 tests stay green. Everything the narrator SAYS was well guarded; WHEN it is
/// allowed to say it was not guarded at all, and the three survivors below are the three ways an
/// unprompted voice can go wrong without a single word of it being incorrect:
/// </para>
///
/// <list type="number">
///   <item><b>The seed floor</b> (<c>Math.Max(seedCount, …)</c>). Turned into a <c>Math.Min</c>,
///         pressing N starts reciting what is already on the chart. The existing seeding tests
///         did not catch it because the seed ALSO records the last pivot index per marker, and
///         that second memory covers the first for any component that had already printed.</item>
///   <item><b>The bar-close gate</b> (<c>if (!isBarClose) return;</c>). Deleted, every confirmed-
///         bar clause fires on an unconfirmed tick — a wick that poked through an EMA announces
///         a cross that then un-happens. <c>NarrationCausalityTests</c> exists and covers the
///         forward-looking rules; nothing covered this line.</item>
///   <item><b>The first sighting</b> of an overlay. Inverted, pressing N announces a cross for
///         every overlay on the series at the moment the switch is flipped. The seed records the
///         side price is on — but ONLY when the overlay has a value there, so an indicator still
///         in warmup (every EMA, for its first N bars) reaches the scan unseeded.</item>
/// </list>
///
/// <para>
/// All three drive <see cref="NarrationScanner"/> directly, the way
/// <c>BandZoneNarrationTests</c> does: the store-shaped questions belong to
/// <c>AutoNarrationService</c> and these are questions about the scan window itself.
/// </para>
/// </summary>
public sealed class NarrationScanWindowTests
{
    private const string Ema = "Ema";
    private const string Dot = "Signal";

    private static TimeSeriesBuffer<Ohlcv> Bars(int count, double close)
        => new(Enumerable.Range(0, count).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            close, close + 1, close - 1, close, 1000)));

    private static TimeSeriesBuffer<Ohlcv> Bars(double[] closes)
        => new(closes.Select((c, i) => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            c, c + 1, c - 1, c, 1000)));

    private static ChartSeries EmaSeries(double[] line)
    {
        var cfg = new SeriesConfig
        {
            Id = "ema9", Name = "EMA", FriendlyName = "EMA 9", IndicatorCode = "Ema",
            Pane = "Main", IsAutoNarrated = true, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig { Name = Ema, DisplayType = ComponentDisplayType.Line, IsVisible = true });
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        buf.ComponentData[Ema] = line;
        return new ChartSeries(cfg, buf);
    }

    private static ChartSeries DotSeries(double[] dots)
    {
        var cfg = new SeriesConfig
        {
            Id = "cipher", Name = "Cipher B", FriendlyName = "Cipher B", IndicatorCode = "CIPHER_B",
            Pane = "Pane_Cipher", IsAutoNarrated = true, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = Dot, DisplayName = "Bull Signal", DisplayType = ComponentDisplayType.Dot, IsVisible = true,
        });
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        buf.ComponentData[Dot] = dots;
        return new ChartSeries(cfg, buf);
    }

    private static WorkspaceState State(ChartSeries series, TimeSeriesBuffer<Ohlcv> bars) =>
        WorkspaceState.Initial with
        {
            Data = bars,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            CurrentDataIndex = bars.Count - 1,
            InitStatus = InitializationStatus.Ready,
            DataStatus = DataStatus.Ready,
            IsSpeechEnabled = true,
        };

    private static double[] Repeat(double value, int n) => Enumerable.Repeat(value, n).ToArray();
    private static double[] Nan(int n) => Repeat(double.NaN, n);

    // ── 1. The seed floor ─────────────────────────────────────────────────────

    /// <summary>
    /// A pivot that is CONFIRMED LATE — printed onto a bar that closed before narration was
    /// switched on — is history, and history is not news.
    ///
    /// <para>
    /// This is the case the <c>Math.Max</c> exists for and the one the pivot memory cannot
    /// cover: at seed time the component had no value anywhere (a pivot indicator in warmup),
    /// so the seed recorded "no pivot seen" and the only thing standing between the scan and
    /// bar 5 is the seed's own bar count.
    /// </para>
    /// </summary>
    [Fact]
    public void ASignalPrintedOntoABarThatClosedBeforeNarrationWasSwitchedOn_IsNotAnnounced()
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        var bars = Bars(12, close: 100);

        var atSeed = DotSeries(Nan(12));          // nothing printed yet: seedCount = 11
        scanner.Seed(atSeed, State(atSeed, bars));

        var late = Nan(12);
        late[5] = 1.0;                             // the indicator fills bar 5 in on a later pass
        var series = DotSeries(late);

        // An intra-bar tick: the newest CONFIRMED bar is the penultimate one, below the seed.
        string? said = scanner.ScanAll(new[] { series }, State(series, bars), closedBound: 10, isBarClose: false);

        Assert.True(said == null, $"recited a bar that closed before narration was on: {said}");
    }

    /// <summary>
    /// The vacuity partner, and it is the one that makes the test above a claim about the FLOOR
    /// rather than about silence: the same series, the same seed, the same scanner — with the
    /// signal on a bar that closed AFTER the seed — does speak.
    /// </summary>
    [Fact]
    public void TheSameSignalOnABarThatClosedAfterTheSeed_IsAnnounced()
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        var seedBars = Bars(12, close: 100);

        var atSeed = DotSeries(Nan(12));
        scanner.Seed(atSeed, State(atSeed, seedBars));

        var dots = Nan(13);
        dots[11] = 1.0;                            // bar 11 was the forming bar at seed time
        var series = DotSeries(dots);
        var bars = Bars(13, close: 100);

        string? said = scanner.ScanAll(new[] { series }, State(series, bars), closedBound: 11, isBarClose: true);

        Assert.NotNull(said);
        Assert.Contains("Bull Signal", said!, StringComparison.OrdinalIgnoreCase);
    }

    // ── 2. The bar-close gate ─────────────────────────────────────────────────

    /// <summary>
    /// An overlay cross is a fact about a CONFIRMED candle. On an intra-bar tick the scan is
    /// handed the penultimate bar and told <c>isBarClose: false</c>, and everything below that
    /// gate — overlay crosses, level crosses, the profile clauses, the volume reading, the
    /// oscillator zones — must stay quiet.
    /// </summary>
    [Fact]
    public void OnAnIntraBarTick_AConfirmedBarCrossIsNotSpoken()
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        var seedBars = Bars(Repeat(100.0, 30));
        var atSeed = EmaSeries(Repeat(110.0, 30));         // price below the line at the seed
        scanner.Seed(atSeed, State(atSeed, seedBars));

        var closes = Repeat(100.0, 31);
        closes[30] = 120.0;                                 // and above it on bar 30
        var bars = Bars(closes);
        var series = EmaSeries(Repeat(110.0, 31));

        string? said = scanner.ScanAll(new[] { series }, State(series, bars), closedBound: 30, isBarClose: false);

        Assert.True(said == null || !said.Contains("crossed", StringComparison.OrdinalIgnoreCase),
            $"spoke a confirmed-bar clause on an unconfirmed tick: {said}");
    }

    /// <summary>
    /// The partner: the identical state on a bar CLOSE does speak. Without this, a scanner that
    /// had simply stopped detecting overlay crosses would pass the test above.
    /// </summary>
    [Fact]
    public void OnTheBarClose_TheSameCrossIsSpoken()
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        var seedBars = Bars(Repeat(100.0, 30));
        var atSeed = EmaSeries(Repeat(110.0, 30));
        scanner.Seed(atSeed, State(atSeed, seedBars));

        var closes = Repeat(100.0, 31);
        closes[30] = 120.0;
        var bars = Bars(closes);
        var series = EmaSeries(Repeat(110.0, 31));

        string? said = scanner.ScanAll(new[] { series }, State(series, bars), closedBound: 30, isBarClose: true);

        Assert.NotNull(said);
        Assert.Contains("crossed above", said!, StringComparison.OrdinalIgnoreCase);
    }

    // ── 3. The first sighting of an overlay ───────────────────────────────────

    /// <summary>
    /// An overlay that was still WARMING UP when narration was switched on has no side recorded
    /// for it, so the first bar it has a value on is its first sighting — and a first sighting
    /// seeds and says nothing. There is no previous side to have crossed from.
    ///
    /// <para>
    /// The seed skips a NaN overlay deliberately (there is nothing to record), which is what
    /// makes this reachable in ordinary use: a 200-period EMA is NaN for its first 200 bars, and
    /// pressing N during that window is neither unusual nor wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void AnOverlayStillWarmingUpWhenNarrationWasSwitchedOn_DoesNotFireACrossOnItsFirstValue()
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        var seedBars = Bars(Repeat(100.0, 30));
        var atSeed = EmaSeries(Nan(30));                    // warmup: no value to take a side against
        scanner.Seed(atSeed, State(atSeed, seedBars));

        var line = Nan(31);
        line[30] = 110.0;                                   // the first bar the EMA exists on
        var closes = Repeat(100.0, 31);
        closes[30] = 120.0;                                 // price happens to be above it
        var series = EmaSeries(line);

        string? said = scanner.ScanAll(new[] { series }, State(series, Bars(closes)), closedBound: 30, isBarClose: true);

        Assert.True(said == null || !said.Contains("crossed", StringComparison.OrdinalIgnoreCase),
            $"announced a cross on the first bar the overlay had a value: {said}");
    }

    /// <summary>
    /// And the bar after it, when price actually changes side, does speak — the same scanner
    /// instance, so what is being tested is that the first sighting SEEDED rather than that the
    /// route is dead.
    /// </summary>
    [Fact]
    public void TheBarAfterAFirstSighting_DoesSpeakARealCross()
    {
        var scanner = new NarrationScanner(new IndicatorContextAnalyzer());
        var seedBars = Bars(Repeat(100.0, 30));
        var atSeed = EmaSeries(Nan(30));
        scanner.Seed(atSeed, State(atSeed, seedBars));

        var line31 = Nan(31);
        line31[30] = 110.0;
        var closes31 = Repeat(100.0, 31);
        closes31[30] = 120.0;
        scanner.ScanAll(new[] { EmaSeries(line31) }, State(EmaSeries(line31), Bars(closes31)),
                        closedBound: 30, isBarClose: true);   // first sighting: above, silently

        var line32 = Nan(32);
        line32[30] = 110.0; line32[31] = 110.0;
        var closes32 = Repeat(100.0, 32);
        closes32[30] = 120.0; closes32[31] = 90.0;            // and now below it
        var series = EmaSeries(line32);

        string? said = scanner.ScanAll(new[] { series }, State(series, Bars(closes32)), closedBound: 31, isBarClose: true);

        Assert.NotNull(said);
        Assert.Contains("crossed below", said!, StringComparison.OrdinalIgnoreCase);
    }
}
