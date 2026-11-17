using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Tests.Mocks;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher A's symbols sit above the candle, one above another, and are said to.</b>
///
/// <para>Cody, 2026-11-17, choosing between saying "above the candle" for Cipher A alone and
/// drawing it there: "option B, do it proper". Cipher A placed each symbol by PRICE (the high plus
/// a share of the range per stacking slot), so the chart drew it above the candle but nothing knew
/// it was there, and "Say what signals look like" says "above the candle" only for a mark pinned to
/// the candle. Pinned, the renderer had no stacking: every symbol on a bar landed on one spot.
/// Now each is pinned above the bar with a <see cref="ComponentConfig.MarkerStackSlot"/>, and
/// <see cref="StandardRenderers.MarkerY"/> stacks the ones actually on the bar.</para>
/// </summary>
public sealed class MarkerStackTests
{
    private static readonly List<Ohlcv> Bars = Enumerable.Range(0, 10)
        .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 100, 110, 90, 105, 1000)).ToList();

    /// <summary>Cipher A as the real factory builds it, every symbol on bar 5 except the yellow X.</summary>
    private static ChartSeries CipherA(params string[] on)
    {
        var f = new IndicatorModelFactory(new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());
        var meta = new CipherAProvider().GetIndicators().Single();
        var s = f.CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new(), null);
        foreach (var c in s.Components)
        {
            var d = Enumerable.Repeat(double.NaN, Bars.Count).ToArray();
            if (on.Contains(c.Name)) d[5] = Bars[5].High;
            s.Data.ComponentData[c.Name] = d;
        }
        return s;
    }

    private static RenderContext Ctx(SKCanvas canvas) => new(
        canvas, SKRect.Create(0, 0, 400, 300), Bars, 0, Bars.Count, 80, 130, false, 40f, 1f, "Main", -1,
        new ThemeService(NSubstitute.Substitute.For<ISettingsManager>()).Current);

    private static readonly string[] Symbols =
    {
        CipherAProvider.CompRedX, CipherAProvider.CompRedDiamond, CipherAProvider.CompBloodDiamond,
        CipherAProvider.CompYellowDiamond, CipherAProvider.CompGreenDot, CipherAProvider.CompBlueTriangle,
    };

    [Fact]
    public void Every_symbol_is_pinned_above_the_candle_with_a_slot()
    {
        var s = CipherA();
        foreach (var name in Symbols.Append(CipherAProvider.CompYellowX))
        {
            var c = s.Components.Single(x => x.Name == name);
            Assert.Equal(MarkerAnchor.AboveBar, c.MarkerAnchor);
            Assert.True(c.MarkerStackSlot > 0, $"{name} has no stack slot");
        }
    }

    [Fact]
    public void Symbols_on_one_candle_stand_one_above_another_without_overlapping()
    {
        var s = CipherA(Symbols);
        using var bmp = new SKBitmap(400, 300);
        using var canvas = new SKCanvas(bmp);
        var ctx = Ctx(canvas);
        float high = ChartMath.MapY(110, ctx.Top, ctx.Bottom, ctx.Min, ctx.Max, false);

        // Each slot's drawn extent, top and bottom, nearest the candle first.
        var spans = new List<(string Name, int Slot, float Top, float Bottom)>();
        foreach (var name in Symbols)
        {
            var c = s.Components.Single(x => x.Name == name);
            float y = StandardRenderers.MarkerY(ctx, s, c, 5, Bars[5].High);
            float half = StandardRenderers.ClampMarkerHalfExtent(ctx, s, c);
            (float top, float bottom) = c.DisplayType == ComponentDisplayType.TriangleUp
                ? (y - StandardRenderers.ClampMarkerExtent(Math.Max(c.Thickness * 3f, 6f), ctx), y)
                : (y - half, y + half);
            Assert.True(bottom < high, $"{name} reaches {bottom:F1}, below the candle's high at {high:F1}");
            spans.Add((name, c.MarkerStackSlot, top, bottom));
        }
        foreach (var a in spans)
        foreach (var b in spans)
        {
            if (a.Slot >= b.Slot) continue;
            Assert.True(b.Bottom <= a.Top, $"{b.Name} (slot {b.Slot}, {b.Top:F1}-{b.Bottom:F1}) overlaps {a.Name} (slot {a.Slot}, {a.Top:F1}-{a.Bottom:F1})");
        }

        // The Blood Diamond shares its red diamond's place, as it fires only with one and covers it.
        var red = spans.Single(x => x.Name == CipherAProvider.CompRedDiamond);
        var blood = spans.Single(x => x.Name == CipherAProvider.CompBloodDiamond);
        Assert.Equal((red.Top + red.Bottom) / 2, (blood.Top + blood.Bottom) / 2, 3);
    }

    [Fact]
    public void A_lone_symbol_sits_at_the_candle_not_up_in_its_slot()
    {
        using var bmp = new SKBitmap(400, 300);
        using var canvas = new SKCanvas(bmp);
        var ctx = Ctx(canvas);
        var alone = CipherA(CipherAProvider.CompBlueTriangle);
        var withAll = CipherA(Symbols);
        var c1 = alone.Components.Single(x => x.Name == CipherAProvider.CompBlueTriangle);
        var c2 = withAll.Components.Single(x => x.Name == CipherAProvider.CompBlueTriangle);
        float lone = StandardRenderers.MarkerY(ctx, alone, c1, 5, 110);
        float stacked = StandardRenderers.MarkerY(ctx, withAll, c2, 5, 110);
        Assert.Equal(StandardRenderers.ResolveMarkerY(ctx, c1, 5, 110), lone, 3);
        Assert.True(stacked < lone - 20, $"stacked at {stacked:F1}, alone at {lone:F1}");
    }

    [Fact]
    public void Its_look_says_above_the_candle()
    {
        var s = CipherA(CipherAProvider.CompRedX);
        var c = s.Components.Single(x => x.Name == CipherAProvider.CompRedX);
        Assert.EndsWith("above the candle", MarkerLook.Describe(s, c, 5));
    }
}
