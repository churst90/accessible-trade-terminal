using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>What Cipher B's dots say when the keyboard lands on them, through the formatter the app uses.</b>
///
/// <para>Found in the 2026-10-17 code review. Cipher B's own speech (<c>GetComponentSpeech</c>, asked
/// before any template by <c>SpeechFormatter.ProviderSpeechStrategy</c>) reads the fast wave and the
/// money flow out of the data it is handed, by component NAME ("Wave Trend", "Money Flow Wave"). The
/// formatter hands that data keyed by DISPLAY name ("Wave Trend Fast", "Money Flow"), so every lookup
/// missed: on every cross dot, buy and sell dot, divergence and gold dot (the sell dot, divergences and
/// gold dot have since been removed) the "Wave Trend −63.2" clause
/// was silently dropped, and gold never said "Money flow positive". Every provider-level test passed,
/// because they call the provider with name-keyed data. And the gold dot claimed "Trend and volatility
/// confirmed" on every bar, though it needs only 3 of its 4 conditions — ADX and ATR may both be the
/// ones that failed.</para>
/// </summary>
public sealed class CipherBProviderSpeechTests
{
    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    /// <summary>A Cipher B built as the app builds it, every part included, holding the provider's output; TF-aware gates
    /// off so the ADX gate is the declared 18.</summary>
    private static (ChartSeries Series, List<Ohlcv> Bars, IndicatorEngine Engine, Dictionary<string, double[]> Results) Chart(int flavour = 0, int length = 1400)
    {
        var providers = new List<IIndicatorProvider> { new CipherBProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        var meta = new CipherBProvider().GetIndicators()[0];
        var series = Factory.CreateSeriesFromMetadata(meta, "Cipher B", "Pane_CIPHER_B",
            new List<(string, string)>(), componentOverrides: null);
        // Every part included (since 2026-10-31 nothing is left out by default; the red dot and the
        // divergences that were are removed).
        var order = meta.Components.Select(c => c.Name).ToList();
        foreach (var name in series.Config.ExcludedComponents.Select(c => c.Name).ToList())
            ComponentInclusion.SetIncluded(series.Config, name, true, order);

        var bars = CausalityProbeSeries.Bars(flavour, length);
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), new Dictionary<string, object> { ["TfAware"] = false },
            new IndicatorResultBuffer(results, bars.Count));
        foreach (var kv in results) series.Data.ComponentData[kv.Key] = kv.Value;
        return (series, bars, engine, results);
    }

    /// <summary>What is said when Ctrl+Up/Down lands on component <paramref name="component"/> at
    /// bar <paramref name="bar"/> — the formatter the speech router uses, with the indicator engine
    /// so the provider's own speech answers, as in the app.</summary>
    private static string Landing(ChartSeries series, List<Ohlcv> bars, IndicatorEngine engine, string component, int bar)
    {
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = series.Components.FindIndex(c => c.Name == component),
            CurrentDataIndex = bar,
            ViewportStartIndex = Math.Max(0, bar - 50),
            ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };
        return new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine)
            .FormatPointFeedback(state, isXMove: false, isYMove: true, series, bars[bar], "");
    }

    private static List<int> Fired(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToList();

    [Theory]
    [InlineData(CipherBProvider.CompCrossBull)]
    [InlineData(CipherBProvider.CompCrossBear)]
    [InlineData(CipherBProvider.CompBlue)]
    public void A_dot_says_the_wave_trend_at_its_bar(string component)
    {
        var (series, bars, engine, results) = Chart();
        var fired = Fired(results[component]);
        Assert.True(fired.Count > 0, $"{component} never fired on 1400 bars — the check would be vacuous.");
        int bar = fired[^1];
        double wt = results[CipherBProvider.CompWT1][bar];

        string said = Landing(series, bars, engine, component, bar);

        Assert.Contains($"Wave Trend {wt:F1}", said);
    }
}

/// <summary>
/// <b>The waves say "overbought" / "oversold" past the levels as they are set, not a fixed ±53.</b>
/// Cody, 2026-11-24: "i changed the ob and os levels in cipher b to 60 and 75 and -60 and -75 but it
/// stil said over sold when the value was above -60". The wave speech compared against a constant
/// ±53 that no setting reached; the lines, the sound and the bar summary followed the settings.
/// </summary>
public sealed class CipherBWaveZoneFollowsTheLevelsTests
{
    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    /// <summary>A Cipher B with its levels as the app gives them, the four level settings applied
    /// the way Properties applies a typed one.</summary>
    private static (ChartSeries Series, List<Ohlcv> Bars, IndicatorEngine Engine, double[] Wt) Chart(
        double? trigger = null, double? extreme = null)
    {
        var providers = new List<IIndicatorProvider> { new CipherBProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        var meta = new CipherBProvider().GetIndicators()[0];
        var series = Factory.CreateSeriesFromMetadata(meta, "Cipher B", "Pane_CIPHER_B",
            new List<(string, string)>(), componentOverrides: null);
        series.Config.Levels.Clear();
        foreach (var d in new CipherBProvider().GetDefaultLevels("CIPHER_B"))
            series.Config.Levels.Add(SeriesManagementService.FromDescriptor(d, d.Value));
        if (trigger is double t) { series.Config.Parameters["Trigger1"] = t; series.Config.Parameters["Trigger2"] = -t; }
        if (extreme is double e) { series.Config.Parameters["OBLevel1"] = e; series.Config.Parameters["OSLevel1"] = -e; }
        ParameterBinding.Apply(series.Config);

        var bars = CausalityProbeSeries.Bars(0, 1400);
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), new Dictionary<string, object> { ["TfAware"] = false },
            new IndicatorResultBuffer(results, bars.Count));
        foreach (var kv in results) series.Data.ComponentData[kv.Key] = kv.Value;
        return (series, bars, engine, results[CipherBProvider.CompWT1]);
    }

    private static string Said(ChartSeries series, List<Ohlcv> bars, IndicatorEngine engine, int bar)
    {
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = series.Components.FindIndex(c => c.Name == CipherBProvider.CompWT1),
            CurrentDataIndex = bar,
            ViewportStartIndex = Math.Max(0, bar - 50),
            ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };
        return new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine)
            .FormatPointFeedback(state, isXMove: true, isYMove: false, series, bars[bar], "");
    }

    private static int FirstBar(double[] wt, Func<double, bool> where)
    {
        int i = Array.FindIndex(wt, v => !double.IsNaN(v) && where(v));
        Assert.True(i >= 0, "no bar on the probe series has such a wave value; the check would be vacuous");
        return i;
    }

    [Fact]
    public void Raised_levels_leave_a_wave_between_53_and_60_unnamed()
    {
        var (series, bars, engine, wt) = Chart(trigger: 60, extreme: 75);
        int low = FirstBar(wt, v => v < -53 && v > -60);
        int high = FirstBar(wt, v => v > 53 && v < 60);

        // The bar's time comes first ("January 14, 12:00. -57.8"); the value ends the sentence.
        Assert.EndsWith(". " + wt[low].ToString("F1", System.Globalization.CultureInfo.InvariantCulture), Said(series, bars, engine, low));
        Assert.EndsWith(". " + wt[high].ToString("F1", System.Globalization.CultureInfo.InvariantCulture), Said(series, bars, engine, high));
    }

    [Fact]
    public void Raised_levels_still_name_a_wave_past_them()
    {
        var (series, bars, engine, wt) = Chart(trigger: 60, extreme: 75);
        int low = FirstBar(wt, v => v <= -60);
        int high = FirstBar(wt, v => v >= 60);

        Assert.EndsWith(", oversold", Said(series, bars, engine, low));
        Assert.EndsWith(", overbought", Said(series, bars, engine, high));
    }

    [Fact]
    public void The_default_levels_name_a_wave_past_53()
    {
        var (series, bars, engine, wt) = Chart();
        int low = FirstBar(wt, v => v < -53 && v > -60);

        Assert.EndsWith(", oversold", Said(series, bars, engine, low));
        Assert.DoesNotContain("{zone}", Said(series, bars, engine, FirstBar(wt, v => Math.Abs(v) < 20)));
    }
}
