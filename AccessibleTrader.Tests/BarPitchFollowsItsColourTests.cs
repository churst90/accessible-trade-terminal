using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A bar is heard on the side it is drawn on.</b>
///
/// <para>Cody, 2026-10-17, BTCUSDT 1d: "wt histogram on october 11 daily has a positive sound but
/// says negative … on the money flow the negative deeper sounding bars say positive". Both are
/// Direction-pitched with a zero reference, and a zero reference fell through to the CANDLE's
/// direction: a negative WT Histogram bar under an up candle got the high pitch, a positive Money
/// Flow bar under a down candle the deep one. Every sign-coloured histogram under the default
/// PriceDirection profile (MACD's, the zero-cross family's) did the same.</para>
///
/// <para>The oracle is the RENDERER's colour (<see cref="ChartRenderer.ReadingColor"/>), not the
/// audio code: for every component of every built-in indicator whose colour does not follow the
/// candle — coloured by its own value, or a bar drawn one colour on both sides of zero —
/// a value above its baseline must take the bullish frequency and one below the bearish, whatever
/// the candle did. Built through the real <see cref="IndicatorModelFactory"/>.</para>
/// </summary>
public sealed class BarPitchFollowsItsColourTests
{
    private static readonly Ohlcv Up   = new(DateTime.UtcNow, 100, 110, 95, 108, 1000);
    private static readonly Ohlcv Down = new(DateTime.UtcNow, 108, 110, 95, 100, 1000);

    private static IndicatorModelFactory Factory() =>
        new(new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
            new MockIndicatorPreferencesService());

    private static ChartTheme Theme()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings).Current;
    }

    private static double Pitch(ChartSeries s, ComponentConfig c, double v, Ohlcv candle) =>
        new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(s, c, v, candle, 0, 10, (-100, 100), 1f).Frequency;

    private static IEnumerable<(ChartSeries Series, ComponentConfig Comp)> SignColouredBars()
    {
        var factory = Factory();
        var theme = Theme();
        foreach (var provider in IndicatorProviderFixture.AllProviders())
        foreach (var meta in provider.GetIndicators())
        {
            // Each form a component can be shown as ("Read as": Cipher B's two are bars only as
            // histograms; their first form is a wave pitched by value).
            var built = new List<ChartSeries>();
            try { built.Add(factory.CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane, new List<(string, string)>(), null)); }
            catch { continue; }   // metadata that cannot build a series is another test's business
            if (ComponentForms.HasOtherForm(meta)) built.Add(ComponentForms.InOtherForm(factory, meta));
            foreach (var s in built)
            {
                if (s.IsProfile) continue;   // pitched per price bin, not per bar (SonifyProfile)
                foreach (var c in s.Components)
                {
                    if (c.PitchMapping is not (PitchMapping.Direction or PitchMapping.PriceDirection)) continue;
                    if (c.BullishFrequency == c.BearishFrequency) continue;
                    if (c.PitchMapping == PitchMapping.Direction && c.ReferenceLevel is double r && r != 0) continue; // its own anchor
                    double above = c.ColorBaseline + 10, below = c.ColorBaseline - 10;
                    // Not coloured by the candle (the colour does not change with it), and either
                    // coloured by its own sign (the colour flips with the value) or a bar that is one
                    // colour on both sides — Cipher B's WT Histogram, yellow since 2026-10-17, whose
                    // only sign is the one it is drawn on and spoken with. Either way the candle has
                    // nothing to say about it.
                    var aUp   = ChartRenderer.ReadingColor(new ChartRenderer.CrosshairReading(above, s, c, 0), theme, Up);
                    var aDown = ChartRenderer.ReadingColor(new ChartRenderer.CrosshairReading(above, s, c, 0), theme, Down);
                    var bUp   = ChartRenderer.ReadingColor(new ChartRenderer.CrosshairReading(below, s, c, 0), theme, Up);
                    bool isBar = c.DisplayType is ComponentDisplayType.Histogram or ComponentDisplayType.Bar
                                 or ComponentDisplayType.ZeroArea or ComponentDisplayType.ZeroDot;
                    if (aUp == aDown && (aUp != bUp || isBar)) yield return (s, c);
                }
            }
        }
    }

    [Fact]
    public void Every_sign_coloured_bar_is_pitched_by_its_own_side_not_the_candle()
    {
        var found = SignColouredBars().ToList();
        Assert.True(found.Count >= 5, $"only {found.Count} sign-coloured, direction-pitched components found; the sweep is not reaching the fleet");

        var wrong = new List<string>();
        foreach (var (s, c) in found)
        {
            double above = c.ColorBaseline + 10, below = c.ColorBaseline - 10;
            if (Pitch(s, c, above, Down) != c.BullishFrequency * c.FreqMultiplier)
                wrong.Add($"{s.Name} / {c.DisplayName ?? c.Name}: above its baseline under a DOWN candle is not the bullish pitch");
            if (Pitch(s, c, below, Up) != c.BearishFrequency * c.FreqMultiplier)
                wrong.Add($"{s.Name} / {c.DisplayName ?? c.Name}: below its baseline under an UP candle is not the bearish pitch");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>Cody's two, named, so the fleet sweep cannot quietly skip them (shown as histograms;
    /// shown as waves, their first form, they are pitched by value, CipherBWaveFormTests).</summary>
    [Theory]
    [InlineData("Wave Gap")]   // the WT Histogram, named for what it is since 2026-10-30
    [InlineData("Money Flow")]
    public void Cipher_b_bars_are_in_the_sweep(string component)
    {
        Assert.Contains(SignColouredBars(), x => x.Series.IndicatorCode == "CIPHER_B" && (x.Comp.DisplayName ?? x.Comp.Name) == component
        && x.Comp.DisplayType == ComponentDisplayType.Histogram);
    }

    /// <summary>Volume is coloured by the candle, so it is heard by the candle — unchanged.</summary>
    [Fact]
    public void Volume_still_follows_the_candle()
    {
        var meta = new CoreIndicatorProvider().GetIndicators().Single(m => m.Code == "VOLUME");
        var s = Factory().CreateSeriesFromMetadata(meta, "Volume", meta.DefaultPane, new List<(string, string)>(), null);
        var c = s.Components.Single(x => x.Role == ComponentRole.Volume);
        Assert.NotEqual(Pitch(s, c, 1000, Up), Pitch(s, c, 1000, Down));
    }
}
