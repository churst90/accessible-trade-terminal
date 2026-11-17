using System.Globalization;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A signal's description says where it sits, by the number of a level drawn in its pane:
/// "Large green dot, below -60".</b>
///
/// <para>Cody, 2026-11-17: "signal location: I'd say always spoken when descriptions are enabled
/// ... use a fixed value on the axis to use as a reference", not another part; then "for the
/// signal position speech, use the number of the level". The level named is the one the mark has
/// passed going out from the middle line, so the place reads like the chart does: past the
/// extreme line, past the overbought line, or only past zero.</para>
/// </summary>
public class SignalLevelPlaceTests
{
    private static ComponentConfig Dot(MarkerAnchor anchor = MarkerAnchor.Value,
        ComponentDisplayType type = ComponentDisplayType.Dot) => new()
    {
        Name = "Buy", DisplayName = "Buy", DisplayType = type, Role = ComponentRole.Signal,
        IsVisible = true, ColorHex = "#00E676", Thickness = 5f, SignalSpeechTemplate = "Buy.",
        MarkerAnchor = anchor,
    };

    private static ChartSeries With(ComponentConfig comp, double value, IEnumerable<LevelConfig> levels)
    {
        var cfg = new SeriesConfig { Id = "s", Name = "s", FriendlyName = "s", IndicatorCode = "S", Pane = "Osc", IsVisible = true };
        cfg.Components.Add(comp);
        foreach (var l in levels) cfg.Levels.Add(l);
        var buf = new SeriesDataBuffer { SeriesId = "s" };
        buf.ComponentData[comp.Name] = new[] { value };
        return new ChartSeries(cfg, buf);
    }

    /// <summary>Cipher B's own declared levels: ±60, ±53, 0 drawn; the stochastic 80/20 heard but
    /// not drawn.</summary>
    private static IEnumerable<LevelConfig> CipherB() =>
        new CipherBProvider().GetDefaultLevels("CIPHER_B").Select(d => SeriesManagementService.FromDescriptor(d, d.Value)).ToList();

    private static LevelConfig Level(string name, double v) => new() { Name = name, Value = v, IsVisible = true, IsDrawn = true };

    [Theory]
    [InlineData(-70.0, "large green dot below -60")]
    [InlineData(-55.0, "large green dot below -53")]
    [InlineData(-40.0, "large green dot below 0")]
    [InlineData(30.0, "large green dot above 0")]
    [InlineData(56.0, "large green dot above 53")]
    [InlineData(65.0, "large green dot above 60")]
    [InlineData(85.0, "large green dot above 60")]   // past the undrawn Stoch Overbought 80: not seen, not said
    [InlineData(0.0, "large green dot on 0")]
    [InlineData(-53.0, "large green dot on -53")]
    public void A_Cipher_B_dot_is_placed_by_the_level_it_has_passed(double value, string expected)
    {
        var comp = Dot();
        Assert.Equal(expected, MarkerLook.Describe(With(comp, value, CipherB()), comp, 0));
    }

    [Fact]
    public void The_sentence_carries_the_place()
    {
        var comp = Dot();
        Assert.Equal("Large green dot below -60.", MarkerLook.Sentence(With(comp, -70, CipherB()), comp, 0));
    }

    /// <summary>Cody, 2026-11-17: "the large red sell dot is below -100, but shouldn't it be above 60
    /// to mirror the large green [buy] dot?" His saved Cipher B charts hide the Zero line, and with no
    /// middle line the sell dot at +96 was placed by the nearest line, the 100% line at the top. The
    /// hidden middle still says which way is out; it is never named.</summary>
    [Fact]
    public void The_buy_and_sell_dots_mirror_with_the_zero_line_hidden()
    {
        var levels = CipherB().ToList();
        levels.Single(l => l.Name == "Zero").IsVisible = false;
        Assert.Contains(levels, l => l.Name == CipherBProvider.Level100 && l.IsVisible && l.IsDrawn);
        var buy = Dot(); var sell = Dot();
        Assert.Equal(" below -60", MarkerLook.LevelPlace(With(buy, CipherBProvider.McbBuyDotY, levels), buy, CipherBProvider.McbBuyDotY));
        Assert.Equal(" above 60", MarkerLook.LevelPlace(With(sell, CipherBProvider.SellDotY, levels), sell, CipherBProvider.SellDotY));
    }

    [Theory]
    [InlineData(30.0, " below 53")]     // inside every drawn line on its side
    [InlineData(-30.0, " above -53")]
    [InlineData(0.0, " above -53")]     // on the hidden middle: not named; the lower side places it
    [InlineData(105.0, " above 100")]
    public void With_the_middle_hidden_it_is_never_named(double value, string expected)
    {
        var levels = CipherB().ToList();
        levels.Single(l => l.Name == "Zero").IsVisible = false;
        Assert.Equal(expected, MarkerLook.LevelPlace(With(Dot(), value, levels), Dot(), value));
    }

    [Theory]
    [InlineData(75.0, " above 70")]
    [InlineData(45.0, " above 30")]
    [InlineData(20.0, " below 30")]
    public void With_no_middle_line_the_nearest_level_is_named(double value, string expected)
    {
        var levels = new[] { Level("Overbought", 70), Level("Oversold", 30) };
        Assert.Equal(expected, MarkerLook.LevelPlace(With(Dot(), value, levels), Dot(), value));
    }

    [Fact]
    public void Only_levels_shown_and_drawn_count()
    {
        var hidden = Level("Overbought", 60); hidden.IsVisible = false;
        var undrawn = Level("Oversold", -60); undrawn.IsDrawn = false;
        var levels = new[] { Level("Zero", 0), hidden, undrawn };
        Assert.Equal(" above 0", MarkerLook.LevelPlace(With(Dot(), 70, levels), Dot(), 70));
        Assert.Equal(" below 0", MarkerLook.LevelPlace(With(Dot(), -70, levels), Dot(), -70));
    }

    [Fact]
    public void No_drawn_levels_no_place()
    {
        var comp = Dot();
        Assert.Equal("large green dot", MarkerLook.Describe(With(comp, -70, Array.Empty<LevelConfig>()), comp, 0));
    }

    [Theory]
    [InlineData(MarkerAnchor.BelowBar, "large green dot below the candle")]
    [InlineData(MarkerAnchor.AboveBar, "large green dot above the candle")]
    public void A_mark_pinned_to_the_candle_keeps_the_candle_words(MarkerAnchor anchor, string expected)
    {
        var comp = Dot(anchor);
        Assert.Equal(expected, MarkerLook.Describe(With(comp, -70, CipherB()), comp, 0));
    }

    [Fact]
    public void A_zero_dot_is_drawn_on_zero_whatever_its_value_so_it_gets_no_place()
    {
        var comp = Dot(type: ComponentDisplayType.ZeroDot);
        Assert.DoesNotContain("60", MarkerLook.Describe(With(comp, -70, CipherB()), comp, 0));
    }

    [Fact]
    public void The_number_is_written_the_same_in_every_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var levels = new[] { Level("Zero", 0), Level("Overbought", 0.25), Level("Oversold", -1234.5) };
            Assert.Equal(" above 0.25", MarkerLook.LevelPlace(With(Dot(), 0.5, levels), Dot(), 0.5));
            Assert.Equal(" below -1234.5", MarkerLook.LevelPlace(With(Dot(), -2000, levels), Dot(), -2000));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
}
