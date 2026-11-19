using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// Saying the chart-type change: one Interface sentence, interrupt, from the same coordinator
/// that announces the Heikin-Ashi toggle. Turning HA on from Line or Area is two visible
/// changes (the style returns to candles as well), so the existing HA warning gains
/// "Candle chart." rather than replacing it.
/// </summary>
public sealed class ChartTypeAnnouncementTests
{
    private static (AccessibilityFeedbackCoordinator Coordinator, MockWorkspaceStore Store, CounterSpeechManager Speech) Build()
    {
        var bus = new SpyEventBus();
        var sonify = new MockNavigationSonifier();
        var speech = new CounterSpeechManager();
        var store = new MockWorkspaceStore();
        var formatter = new SpeechFormatter();
        var speechRouter = new SpeechFeedbackRouter(speech, formatter, store);
        var audioRouter = new AudioFeedbackRouter(sonify, new MockEarconService());
        var navMgr = new NavigationFeedbackManager(speechRouter, formatter);
        var coordinator = new AccessibilityFeedbackCoordinator(
            store, navMgr, speechRouter, audioRouter, formatter, bus, new MockEarconService(),
            new SdkCandlePatternAnalyzer(),
            new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())), new ChartPatternFocus(),
            new MockAutoNarrationService());
        return (coordinator, store, speech);
    }

    private static string Announce(PriceChartStyle from, bool haFrom, PriceChartStyle to, bool haTo,
        bool describeChartPatterns = false)
    {
        var (coordinator, store, speech) = Build();
        using (coordinator)
        {
            store.EmitState(WorkspaceState.Initial with
            {
                ChartStyle = from,
                IsHeikinAshi = haFrom,
                DescribeChartPatterns = describeChartPatterns,
            });
            speech.Utterances.Clear();

            store.EmitState(store.State with { ChartStyle = to, IsHeikinAshi = haTo });

            Assert.NotEmpty(speech.Utterances);
            return speech.Utterances[^1].Text;
        }
    }

    [Theory]
    [InlineData(PriceChartStyle.Candles, PriceChartStyle.Line, "Line chart.")]
    [InlineData(PriceChartStyle.Line, PriceChartStyle.Area, "Area chart.")]
    [InlineData(PriceChartStyle.Area, PriceChartStyle.Candles, "Candle chart.")]
    public void A_style_change_says_the_new_chart_type(PriceChartStyle from, PriceChartStyle to, string expected)
    {
        Assert.Equal(expected, Announce(from, false, to, false));
    }

    [Fact]
    public void Turning_Heikin_Ashi_on_from_candles_names_the_Heikin_Ashi_chart()
    {
        Assert.Equal("Heikin Ashi chart.", Announce(PriceChartStyle.Candles, false, PriceChartStyle.Candles, true));
    }

    [Fact]
    public void Turning_Heikin_Ashi_on_from_a_line_says_the_existing_warning_and_candle_chart()
    {
        Assert.Equal("Heikin-Ashi candles. Candle chart.",
            Announce(PriceChartStyle.Line, false, PriceChartStyle.Candles, true));
    }

    [Fact]
    public void The_formation_caveat_survives_the_new_wording()
    {
        Assert.Equal("Heikin Ashi chart. Chart formations are still read from standard candles.",
            Announce(PriceChartStyle.Candles, false, PriceChartStyle.Candles, true, describeChartPatterns: true));
    }

    [Fact]
    public void Turning_Heikin_Ashi_off_says_the_candle_chart()
    {
        Assert.Equal("Candle chart.", Announce(PriceChartStyle.Candles, true, PriceChartStyle.Candles, false));
    }
}
