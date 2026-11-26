using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Cody, 2026-11-26: "If I adjust an os/ob level or any level that's customizable in an
/// indicator, that level must be correct when read back through speech and sound just as
/// Cipher B."
///
/// <para>
/// The bar-close narration ("RSI: RSI entered overbought.") came from
/// <see cref="IndicatorContextAnalyzer"/>'s table: 70/30 for RSI, ±53 for Cipher B's wave, read
/// whatever the series' lines said. And because those components have a table row, the
/// level-cross route that DOES read the lines stands aside for them, so moving RSI's line to 80
/// changed the arrowing speech, the zone noise and the alerts, but not this sentence. These
/// drive the real analyzer through <see cref="AutoNarrationService"/> on an RSI whose lines were
/// moved.
/// </para>
/// </summary>
public class ZoneNarrationFollowsTheLevelsTests
{
    private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
    {
        public List<string> Spoken { get; } = new();
        public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Chart) => Spoken.Add(message);
        public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
        public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
        public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
    }

    private static Ohlcv Bar(int i) => new(new DateTime(2026, 1, 1).AddMinutes(i), 100, 110, 95, 105, 1000);

    /// <summary>An RSI with the given lines (none = an RSI that declares no zone at all).</summary>
    private static SeriesConfig Rsi(double? overbought, double? oversold, string code = "RSI")
    {
        var cfg = new SeriesConfig
        {
            Id = "rsi", Name = "RSI", FriendlyName = "RSI", IndicatorCode = code,
            Pane = "Pane_RSI", IsAutoNarrated = true, IsVisible = true, IsMuted = false,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "RSI", DisplayName = "RSI", DisplayType = ComponentDisplayType.Oscillator, IsVisible = true,
        });
        if (overbought is double ob) cfg.Levels.Add(new LevelConfig { Name = "Overbought", Value = ob, IsVisible = true });
        if (oversold is double os) cfg.Levels.Add(new LevelConfig { Name = "Oversold", Value = os, IsVisible = true });
        return cfg;
    }

    private static WorkspaceState State(SeriesConfig cfg, double[] values)
    {
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        buf.ComponentData["RSI"] = values;
        var series = new ChartSeries(cfg, buf);
        return WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, values.Length).Select(Bar)),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            CurrentDataIndex = values.Length - 1,
            InitStatus = InitializationStatus.Ready,
            DataStatus = DataStatus.Ready,
            IsSpeechEnabled = true,
        };
    }

    /// <summary>
    /// Closes bars one at a time with the given RSI values and returns what each close said
    /// about overbought or oversold (the first two closes seed the state and are not returned).
    /// </summary>
    private static List<string> CloseBars(SeriesConfig cfg, params double[] values)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        var router = new CapturingSpeechRouter();
        _ = new AutoNarrationService(store, bus, router, new IndicatorContextAnalyzer());

        var said = new List<string>();
        for (int n = 1; n <= values.Length; n++)
        {
            router.Spoken.Clear();
            // The scan reads the bar that just CLOSED, one before the forming bar: each close
            // carries a forming bar after it with the same value.
            store.EmitState(State(cfg, values.Take(n).Append(values[n - 1]).ToArray()));
            bus.Publish(new RedrawEvent());
            if (n > 2)
                said.AddRange(router.Spoken.Where(s =>
                    s.Contains("overbought", StringComparison.OrdinalIgnoreCase)
                    || s.Contains("oversold", StringComparison.OrdinalIgnoreCase)
                    || s.Contains("extreme", StringComparison.OrdinalIgnoreCase)));
        }
        return said;
    }

    [Fact]
    public void Raised_lines_leave_75_unnamed_and_name_85()
    {
        // 75 is past the table's 70 and short of the user's 80.
        Assert.Empty(CloseBars(Rsi(80, 20), 50, 50, 75));
        var said = CloseBars(Rsi(80, 20), 50, 50, 75, 85);
        Assert.Single(said);
        Assert.Contains("entered overbought", said[0]);
    }

    [Fact]
    public void Raised_oversold_line_names_32()
    {
        // 32 is short of the table's 30 and past the user's 35.
        var said = CloseBars(Rsi(65, 35), 50, 50, 32);
        Assert.Single(said);
        Assert.Contains("entered oversold", said[0]);
    }

    [Fact]
    public void Lowered_overbought_line_names_65()
    {
        var said = CloseBars(Rsi(60, 40), 50, 50, 65);
        Assert.Single(said);
        Assert.Contains("entered overbought", said[0]);
    }

    [Fact]
    public void Deleting_the_oversold_line_does_not_bring_back_30()
    {
        Assert.Empty(CloseBars(Rsi(80, null), 50, 50, 25));
    }

    [Fact]
    public void An_rsi_with_no_lines_keeps_the_tables_70()
    {
        // The fallback, and the vacuity floor for the silences above: this path does speak.
        var said = CloseBars(Rsi(null, null), 50, 50, 72);
        Assert.Single(said);
        Assert.Contains("entered overbought", said[0]);
    }

    [Fact]
    public void An_oscillator_without_a_table_row_is_said_once_by_its_line()
    {
        // The same lines on a code the table does not know: the level-cross route speaks for
        // it, and the analyzer must not zone it as well.
        var said = CloseBars(Rsi(80, 20, code: "SOME_OSC"), 50, 50, 85);
        // The two voices compose into one utterance ("crossed above overbought, 80. RSI
        // entered overbought."), so this is about the words, not the count.
        Assert.Single(said);
        Assert.Contains("crossed above overbought, 80", said[0]);
        Assert.DoesNotContain("entered", said[0]);
    }
}
