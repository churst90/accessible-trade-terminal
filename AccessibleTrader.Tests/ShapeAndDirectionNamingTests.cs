using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Names whose two halves mirror each other, so that swapping them still sounds plausible: a
/// dragonfly doji against a gravestone, a bearish crossover against a bullish one, the top of a
/// value area against its bottom. Each pair is told apart only by direction, and a blind user
/// has nothing to check the word against.
///
/// <para>
/// A2q (2026-10-01): the campaign swapped each pair. All three survived — no test asked which
/// doji had which shadow, whether a MACD cross DOWN was spoken as bearish, or which edge of a
/// value area was called its high.
/// </para>
/// </summary>
public sealed class ShapeAndDirectionNamingTests
{
    // ── Doji family ──────────────────────────────────────────────────────────

    [Fact]
    public void A_doji_with_a_long_lower_shadow_and_no_upper_one_is_a_dragonfly()
    {
        // Opened, sold off hard, closed back at the high: rejection of lower prices.
        var a = new SdkCandlePatternAnalyzer().Analyze(new Ohlcv(new DateTime(2026, 1, 1), 100, 100.1, 90, 100, 1));
        Assert.Equal(CandleType.DragonflyDoji, a.Type);
    }

    [Fact]
    public void A_doji_with_a_long_upper_shadow_and_no_lower_one_is_a_gravestone()
    {
        var a = new SdkCandlePatternAnalyzer().Analyze(new Ohlcv(new DateTime(2026, 1, 1), 100, 110, 99.9, 100, 1));
        Assert.Equal(CandleType.GravestoneDoji, a.Type);
    }

    // ── Crossover direction, as the narrator says it ─────────────────────────

    /// <summary>
    /// A narrated MACD whose line sits above its signal line until the bar that closes, which
    /// takes it below (bearish) — or the mirror image.
    /// </summary>
    private static string NarrateTheCross(bool macdFallsThroughSignal)
    {
        const int n = 60;
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, n + 1)
            .Select(i => new Ohlcv(t0.AddHours(i), 100, 101, 99, 100.5, 10)).ToList();

        ChartSeries Series(int count)
        {
            var cfg = new SeriesConfig
            {
                Id = "macd", Name = "MACD", FriendlyName = "MACD", IndicatorCode = "MACD",
                Pane = "Pane_MACD", IsAutoNarrated = true, IsVisible = true,
            };
            foreach (var c in new[] { "MACD", "Signal", "Histogram" })
                cfg.Components.Add(new ComponentConfig { Name = c, DisplayName = c, DisplayType = ComponentDisplayType.Line, IsVisible = true });
            var macd = new double[count];
            var signal = new double[count];
            for (int i = 0; i < count; i++)
            {
                // The cross is printed by the recalculation that runs when bar n-1 CLOSES; while
                // it was forming, the line was still on its old side.
                bool crossed = i >= n - 1 && count > n;
                double above = macdFallsThroughSignal ? 1.0 : -1.0;
                macd[i] = crossed ? -above : above;
                signal[i] = 0;
            }
            var buf = new SeriesDataBuffer { SeriesId = "macd" };
            buf.ComponentData["MACD"] = macd;
            buf.ComponentData["Signal"] = signal;
            buf.ComponentData["Histogram"] = macd.Select((m, i) => m - signal[i]).ToArray();
            return new ChartSeries(cfg, buf);
        }

        WorkspaceState State(int count) => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars.Take(count)),
            ActiveSeries = ImmutableList.Create(Series(count)),
            CurrentDataIndex = count - 1,
            InitStatus = InitializationStatus.Ready,
            DataStatus = DataStatus.Ready,
            IsSpeechEnabled = true,
        };

        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var spoken = new List<string>();
        var router = new SpeechFeedbackRouter(new CounterSpeechManager { OnSpeak = t => spoken.Add(t) },
            new SpeechFormatter(), store);
        using var narrator = new AutoNarrationService(store, bus, router, new IndicatorContextAnalyzer());

        store.EmitState(State(n));          // bar n-1 forming; narration seeded
        bus.Publish(new RedrawEvent());
        spoken.Clear();
        store.EmitState(State(n + 1));      // bar n-1 closes
        bus.Publish(new RedrawEvent());
        return string.Join(" | ", spoken);
    }

    [Fact]
    public void A_MACD_line_falling_through_its_signal_is_narrated_as_a_bearish_crossover()
    {
        string said = NarrateTheCross(macdFallsThroughSignal: true);
        Assert.Contains("bearish crossover", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bullish", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_MACD_line_rising_through_its_signal_is_narrated_as_a_bullish_crossover()
    {
        string said = NarrateTheCross(macdFallsThroughSignal: false);
        Assert.Contains("bullish crossover", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearish", said, StringComparison.OrdinalIgnoreCase);
    }

    // ── Value-area edges ─────────────────────────────────────────────────────

    private static ProfileBin Bin(double low, bool valueArea, bool poc = false, double volume = 100) => new()
    {
        PriceLow = low, PriceHigh = low + 1, TotalVolume = volume, TpoPeriodCount = 1,
        IsPOC = poc, IsValueArea = valueArea,
    };

    [Fact]
    public void The_highest_value_area_bin_is_called_value_area_high_and_the_lowest_value_area_low()
    {
        var bins = new List<ProfileBin>
        {
            Bin(100, valueArea: false),
            Bin(101, valueArea: true),          // bottom edge of the value area
            Bin(102, valueArea: true, poc: true, volume: 400),
            Bin(103, valueArea: true),          // top edge
            Bin(104, valueArea: false),
        };

        Assert.Equal("Value Area High", ProfileBinClassifier.GetLabel(ProfileBinClassifier.Classify(bins[3], bins)));
        Assert.Equal("Value Area Low", ProfileBinClassifier.GetLabel(ProfileBinClassifier.Classify(bins[1], bins)));
    }
}
