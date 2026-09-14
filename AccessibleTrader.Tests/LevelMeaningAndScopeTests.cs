using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A reference line says what it is, what it means, and whose it is.</b>
///
/// <para>
/// Three separate holes, all in the same place, all found by asking what a level can express.
/// </para>
///
/// <para>
/// <b>What it IS.</b> Both readers of "am I in an overbought zone" matched the WORDS "Overbought"
/// and "Oversold" in a level's name — <c>SpeechFormatter.ResolveZone</c> for the spoken word and
/// <c>AudioZoneHelper</c> for the zone texture — years after <c>LevelConfig.EffectiveRole</c> was
/// introduced to collapse exactly that kind of name-sniffing in one place. A level declaring
/// <c>Role = Overbought</c> under any other name was invisible to both.
/// </para>
///
/// <para>
/// <b>What it MEANS.</b> Overbought and oversold are the only two meanings a level could carry, so
/// a line that divides a scale into named BANDS could not speak at all. ADX's template asks for
/// <c>{zone}</c> and its lines are Developing / Strong / Very Strong; Choppiness asks too and its
/// lines are Trending / Ranging. Both resolved to an empty string on every bar — and for ADX the
/// band IS the indicator's message. Choppiness is the reason this is DECLARED rather than derived:
/// it is inverted, a LOW reading means trending, and any rule inferring meaning from the order of
/// the numbers gets it exactly backwards.
/// </para>
///
/// <para>
/// <b>Whose it is.</b> Levels are declared per INDICATOR. Aroon puts three components on one pane —
/// Up and Down run 0–100 about 50, the Oscillator runs ±100 about zero — so its single "Midpoint
/// 50" was wrong for one of them whichever way it was set. <c>SubscribedLevelNames</c> already
/// existed for this and only the audio layer honoured it.
/// </para>
/// </summary>
public sealed class LevelMeaningAndScopeTests
{
    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    private static (IIndicatorProvider Provider, IndicatorMetadata Meta) Find(string code) =>
        IndicatorProviderFixture.AllProviders()
            .SelectMany(p => p.GetIndicators().Select(m => (p, m)))
            .First(x => string.Equals(x.m.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds a series the way production does, with its provider levels injected.</summary>
    private static ChartSeries Built(string code, string component, double[] values)
    {
        var (provider, meta) = Find(code);
        var series = Factory.CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta),
            new List<(string, string)>(), null, null);
        foreach (var d in provider.GetDefaultLevels(meta.Code.ToUpperInvariant()) ?? new List<LevelDescriptor>())
            series.Config.Levels.Add(new LevelConfig
            {
                Name = d.Name, Value = d.Value, ColorHex = d.ColorHex, DashStyle = d.Dash,
                IsVisible = true, PlayEarcon = d.PlayEarcon, EarconVolume = d.EarconVolume,
                ZoneNoiseAmount = d.ZoneNoiseAmount, ZoneNoiseType = d.ZoneNoiseType,
                AboveLabel = d.AboveLabel, BelowLabel = d.BelowLabel,
            });
        series.Data.ComponentData[component] = values;
        return series;
    }

    // ── What it MEANS: the band word ─────────────────────────────────────────

    /// <summary>
    /// ADX speaks its band. Its whole message is how strong the trend is, and the token that says
    /// so produced nothing on every bar the indicator has ever drawn.
    /// </summary>
    [Theory]
    [InlineData(10.0, "no trend")]
    [InlineData(22.0, "developing trend")]
    [InlineData(30.0, "strong trend")]
    [InlineData(60.0, "very strong trend")]
    public void Adx_SpeaksWhichBandItIsIn(double value, string expected)
    {
        var series = Built("Adx", "Adx", new[] { value });
        var comp = series.Components.First(c => c.Name == "Adx");
        string spoken = Zone(series, comp, value);
        Assert.Equal(expected, spoken);
    }

    /// <summary>
    /// Choppiness is INVERTED and says so. A low reading is a trending market — the fact that
    /// makes this a declaration rather than a rule derived from the numbers.
    /// </summary>
    [Theory]
    [InlineData(30.0, "trending")]
    [InlineData(70.0, "ranging")]
    public void Choppiness_SaysTrendingAtTheLOWEnd(double value, string expected)
    {
        var series = Built("Chop", "Chop", new[] { value });
        var comp = series.Components.First(c => c.Name == "Chop");
        Assert.Equal(expected, Zone(series, comp, value));
    }

    /// <summary>An extreme still wins over a band: it is the more specific fact.</summary>
    [Fact]
    public void AnOverboughtReadingBeatsABandLabel()
    {
        var series = Built("Rsi", "Rsi", new[] { 85.0 });
        var comp = series.Components.First(c => c.Name == "Rsi");
        series.Config.Levels.First(l => l.Name == "Overbought").AboveLabel = "upper band";
        Assert.Equal("Overbought", Zone(series, comp, 85.0));
    }

    /// <summary>
    /// The zone is decided by the level's ROLE, not by the words in its name. A provider that
    /// names its extreme something else still gets the word, and the texture.
    /// </summary>
    [Fact]
    public void AnExtremeDeclaredByRole_IsFoundUnderAnyName()
    {
        var series = Built("Rsi", "Rsi", new[] { 85.0 });
        var comp = series.Components.First(c => c.Name == "Rsi");
        var ob = series.Config.Levels.First(l => l.Name == "Overbought");
        ob.Name = "Distribution Ceiling";
        ob.Role = LevelRole.Overbought;

        Assert.Equal("Overbought", Zone(series, comp, 85.0));
        Assert.True(ZoneNoise(series, comp, 85.0) > 0f, "the zone texture must follow the role too");
    }

    // ── Whose it is: the component scope ─────────────────────────────────────

    /// <summary>
    /// Aroon's Oscillator answers to the zero line and Up/Down to the midline. One pane, two
    /// scales, and before this each component could read the other's line.
    /// </summary>
    [Fact]
    public void AroonsComponents_EachAnswerToTheirOwnLine()
    {
        var (_, meta) = Find("Aroon");
        var up   = meta.Components.First(c => c.Name == "AroonUp");
        var down = meta.Components.First(c => c.Name == "AroonDown");
        var osc  = meta.Components.First(c => c.Name == "Oscillator");

        Assert.Equal(new[] { "Midpoint" }, up.SubscribedLevelNames);
        Assert.Equal(new[] { "Midpoint" }, down.SubscribedLevelNames);
        Assert.Equal(new[] { "Zero" }, osc.SubscribedLevelNames);
    }

    /// <summary>And the pane declares both lines, so each component has one to answer to.</summary>
    [Fact]
    public void AroonDeclaresBothLines()
    {
        var (provider, meta) = Find("Aroon");
        var names = provider.GetDefaultLevels(meta.Code.ToUpperInvariant()).Select(l => l.Name).ToList();
        Assert.Contains("Midpoint", names);
        Assert.Contains("Zero", names);
    }

    /// <summary>
    /// A component that subscribes to one line does not hear another's. The spoken zone is scoped
    /// the way the zone texture already was.
    /// </summary>
    [Fact]
    public void AComponentDoesNotReadALineItDoesNotSubscribeTo()
    {
        var series = Built("Rsi", "Rsi", new[] { 85.0 });
        var comp = series.Components.First(c => c.Name == "Rsi");
        comp.SubscribedLevelNames = new[] { "Midpoint" };   // not Overbought

        Assert.Equal("", Zone(series, comp, 85.0));
    }

    /// <summary>
    /// <b>The OFF value of the setting, which is the one nobody writes a fixture for.</b>
    ///
    /// <para>
    /// <c>SubscribedLevelNames</c> has three states and they are documented as three:
    /// <c>null</c> subscribes to ALL levels, a populated list subscribes to the named ones, and an
    /// explicitly EMPTY list subscribes to NONE. The test above covers the middle case and the
    /// default covers the first; the empty list had no test at all, on either of its two readers.
    /// </para>
    ///
    /// <para>
    /// The A2g mutant set flipped <c>Count == 0 ? false</c> to <c>true</c> in
    /// <c>AudioZoneHelper.ComponentSubscribesTo</c> — turning "hear no levels" into "hear every
    /// level" — and nothing went red. This is the second campaign in a row where an explicitly
    /// empty subscription survived; A2f found the same shape in the narration path. Opting out is
    /// a thing users do, and it has to be as real as opting in.
    /// </para>
    /// </summary>
    [Fact]
    public void AComponentSubscribedToNoLinesReadsNone()
    {
        var series = Built("Rsi", "Rsi", new[] { 85.0 });
        var comp = series.Components.First(c => c.Name == "Rsi");

        Assert.NotEqual(0f, ZoneNoise(series, comp, 85.0));          // precondition: it has a zone to lose

        comp.SubscribedLevelNames = Array.Empty<string>();

        Assert.Equal(0f, ZoneNoise(series, comp, 85.0));
        Assert.Equal("", Zone(series, comp, 85.0));
        Assert.False(AudioZoneHelper.ComponentSubscribesTo(comp, "Overbought"));
    }

    /// <summary>
    /// The other two states of the same setting, asserted on the same reader so the three cannot
    /// quietly collapse into two. A null list is the default and must still hear everything.
    /// </summary>
    [Fact]
    public void AComponentWithNoSubscriptionListReadsEveryLine()
    {
        var series = Built("Rsi", "Rsi", new[] { 85.0 });
        var comp = series.Components.First(c => c.Name == "Rsi");
        comp.SubscribedLevelNames = null;

        Assert.True(AudioZoneHelper.ComponentSubscribesTo(comp, "Overbought"));
        Assert.True(AudioZoneHelper.ComponentSubscribesTo(comp, "Oversold"));
        Assert.NotEqual(0f, ZoneNoise(series, comp, 85.0));
    }

    // ── Helpers that drive the real readers ──────────────────────────────────

    // Internal rather than reflected: ResolveZone is the arithmetic under the {zone} token and a
    // test that reaches it by name would go green the day someone renames the method.
    private static string Zone(ChartSeries series, ComponentConfig comp, double val)
        => Core.Services.Accessibility.StandardTemplateStrategy.ResolveZone(series, val, comp);

    private static float ZoneNoise(ChartSeries series, ComponentConfig comp, double val)
        => AudioZoneHelper.ComputeZoneNoise(series, comp, val).NoiseAmount;
}
