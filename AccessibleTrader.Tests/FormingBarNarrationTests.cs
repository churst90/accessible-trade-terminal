using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// A signal on the bar that is still FORMING is not news yet: live indicators print and unprint
/// on the forming bar as the price moves, and a blind user who heard "buy signal" cannot see it
/// vanish a second later. The narrator speaks a signal when its bar CLOSES, once.
///
/// <para>
/// A2q (2026-10-01): the intra-bar bound (<c>currentCount - 2</c>, the last CLOSED bar) had no
/// test; the campaign moved it to the forming bar and it was not caught by any test that narrated
/// a flickering signal.
/// </para>
/// </summary>
public sealed class FormingBarNarrationTests
{
    private static List<Ohlcv> Bars(int n) => Enumerable.Range(0, n)
        .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 10))
        .ToList();

    private static ChartSeries Series(int bars, params int[] dots)
    {
        var cfg = new SeriesConfig
        {
            Id = "cb", Name = "Cipher B", FriendlyName = "Cipher B", IndicatorCode = "CIPHER_B",
            Pane = "Pane_CIPHER_B", IsAutoNarrated = true, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Gold", DisplayName = "Triple Confluence Buy", DisplayType = ComponentDisplayType.Dot,
            IsVisible = true, SignalSpeechTemplate = "Triple confluence buy, strong confirmation",
        });
        var d = new double[bars];
        Array.Fill(d, double.NaN);
        foreach (int b in dots) d[b] = 1.0;
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        buf.ComponentData["Gold"] = d;
        return new ChartSeries(cfg, buf);
    }

    private static WorkspaceState State(int bars, params int[] dots) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Bars(bars)),
        ActiveSeries = ImmutableList.Create(Series(bars, dots)),
        CurrentDataIndex = bars - 1,
        InitStatus = InitializationStatus.Ready,
        DataStatus = DataStatus.Ready,
        IsSpeechEnabled = true,
    };

    [Fact]
    public void A_signal_flickering_on_the_forming_bar_is_not_announced_until_the_bar_closes()
    {
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var spoken = new List<string>();
        var router = new SpeechFeedbackRouter(new CounterSpeechManager { OnSpeak = t => spoken.Add(t) },
            new SpeechFormatter(), store);
        using var narrator = new AutoNarrationService(store, bus, router, new IndicatorContextAnalyzer());

        store.EmitState(State(100));                 // bar 99 forming, narration seeded
        bus.Publish(new RedrawEvent());

        store.EmitState(State(100, 99));             // a tick: the dot appears on the FORMING bar
        bus.Publish(new RedrawEvent());
        Assert.Empty(spoken);

        store.EmitState(State(100));                 // the next tick: it is gone again
        bus.Publish(new RedrawEvent());
        Assert.Empty(spoken);

        store.EmitState(State(101, 99));             // bar 99 closes with the dot on it
        bus.Publish(new RedrawEvent());
        Assert.Contains("Triple confluence buy", Assert.Single(spoken), StringComparison.OrdinalIgnoreCase);
    }
}
