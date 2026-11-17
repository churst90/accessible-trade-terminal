using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A line coloured against another line has two tones and Ctrl+Left/Right stops where its
/// colour changes.</b>
///
/// <para>Cody, 2026-11-17, on DBSI's trend line, green with price above it and red below: "should
/// this line have 2 tones, like oscillators do so I can tell by ear if price is above or below it
/// when it is either color? ... Right now it is a constant single tone. Have ctrl left/right jump to
/// crossings too." It was one tone, and Ctrl+Left/Right said "No points of interest on Trend".</para>
///
/// <para>Built from DBSI's real metadata by the real factory, so the colours and the sibling are the
/// shipped ones.</para>
/// </summary>
public sealed class SiblingColouredLineTests
{
    private static readonly Ohlcv Bar = new(DateTime.UtcNow, 100, 110, 95, 105, 1000);

    /// <summary>DBSI as built, its trend at 100 and its close at <paramref name="closes"/>.</summary>
    private static (ChartSeries S, ComponentConfig Trend) Dbsi(params double[] closes)
    {
        var f = new IndicatorModelFactory(new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());
        var meta = new DbsiStyleProvider().GetIndicators().Single();
        var s = f.CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new(), null);
        s.Data.ComponentData[DbsiStyleProvider.CompTrend] = closes.Select(_ => 100.0).ToArray();
        s.Data.ComponentData[DbsiStyleProvider.CompTrendSource] = closes;
        return (s, s.Components.Single(c => c.Name == DbsiStyleProvider.CompTrend));
    }

    [Fact]
    public void Dbsi_trend_sounds_bright_with_price_above_it_and_warm_below()
    {
        var (s, trend) = Dbsi(105, 95);
        Assert.Equal(ComponentDisplayType.Line, trend.DisplayType);
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var above = strategy.CreateAudioPoint(s, trend, 100, Bar, 0, 2, (90, 110), 1f, dataIndex: 0);
        var below = strategy.CreateAudioPoint(s, trend, 100, Bar, 1, 2, (90, 110), 1f, dataIndex: 1);

        // The oscillators' two timbres (OscillatorMidlineTests): bright = a little square,
        // warm = triangle and no square.
        Assert.True(above.SquareMix > 0, $"price above: square {above.SquareMix}");
        Assert.Equal(0f, below.SquareMix);
        Assert.True(below.TriangleMix > above.TriangleMix, $"price below: triangle {below.TriangleMix} vs {above.TriangleMix}");
    }

    [Fact]
    public void A_plain_line_keeps_its_one_tone()
    {
        var (s, trend) = Dbsi(105, 95);
        trend.ColorAboveComponent = "";
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var a = strategy.CreateAudioPoint(s, trend, 100, Bar, 0, 2, (90, 110), 1f, dataIndex: 0);
        var b = strategy.CreateAudioPoint(s, trend, 100, Bar, 1, 2, (90, 110), 1f, dataIndex: 1);
        Assert.Equal((a.SquareMix, a.TriangleMix), (b.SquareMix, b.TriangleMix));
    }

    [Fact]
    public void The_bright_tone_follows_the_colour_not_the_side()
    {
        // Cipher A's ribbon is blue ABOVE its sibling and gray below: there the bright tone is
        // above. DBSI's trend is red above its close: there the bright tone is below.
        var (s, line) = Dbsi(105, 95);
        line.ColorHex = "#808080"; line.ColorHexSecondary = "#2962FF";   // gray own, blue above
        var strategy = new DefaultSonificationStrategy(new SoundPatchRegistry());
        var lineBelow = strategy.CreateAudioPoint(s, line, 100, Bar, 0, 2, (90, 110), 1f, dataIndex: 0);   // 100 < 105: own (gray)
        var lineAbove = strategy.CreateAudioPoint(s, line, 100, Bar, 1, 2, (90, 110), 1f, dataIndex: 1);   // 100 > 95: blue
        Assert.True(lineAbove.SquareMix > 0);
        Assert.Equal(0f, lineBelow.SquareMix);
    }

    private static (int? Landed, string? Said) Jump(ChartSeries s, int from, bool right)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        int n = s.Data.ComponentData[DbsiStyleProvider.CompTrend].Length;
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, n).Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100, 101, 99, 100, 10)).ToList()),
            ActiveSeries = ImmutableList.Create(s),
            PrimarySeriesId = "candles",
            FocusedSeriesId = s.Id,
            FocusedComponentIndex = s.Components.FindIndex(c => c.Name == DbsiStyleProvider.CompTrend),
            CurrentDataIndex = from,
            ViewportStartIndex = 0,
            ViewportLength = n,
        });
        new IndicatorCrossingEngine(store, bus).HandleCrossJump(right ? SystemCommand.NavRightJump : SystemCommand.NavLeftJump);
        return (store.DispatchedActions.OfType<NavigateAction>().LastOrDefault()?.NewIndex,
                bus.Log.OfType<FeedbackRequestEvent>().LastOrDefault()?.Message);
    }

    [Fact]
    public void Ctrl_arrows_on_the_dbsi_trend_stop_where_price_crosses_it()
    {
        // Price above on 0-2, below on 3-5, above from 6.
        var (s, _) = Dbsi(105, 106, 107, 95, 94, 93, 101, 102);
        var r1 = Jump(s, 0, right: true);
        Assert.Equal(3, r1.Landed);
        Assert.StartsWith("Trend turns red at", r1.Said);
        var r2 = Jump(s, 3, right: true);
        Assert.Equal(6, r2.Landed);
        Assert.StartsWith("Trend turns green at", r2.Said);
        Assert.Equal(3, Jump(s, 5, right: false).Landed);
        Assert.Equal(3, Jump(s, 6, right: false).Landed);   // the change on bar 6 is where the user is
        var none = Jump(s, 6, right: true);
        Assert.Null(none.Landed);
        Assert.Equal("No more Trend colour changes in this direction", none.Said);
    }
}
