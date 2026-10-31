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
