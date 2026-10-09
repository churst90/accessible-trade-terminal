using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The browser-closed monitor builds the instance a tree leaf is bound to.</b> The headless
/// chart builds one series per indicator code an alert references — the first saved, because an
/// unbound leaf reads the first. A leaf bound to the EMA 50 (2026-10) reads only an EMA 50, so on
/// a tab whose EMA 20 comes first that leaf would find nothing to read with the browser closed.
/// </summary>
public sealed class HeadlessTreeInstanceTests
{
    private static readonly ChartIdentity Btc = new("Spot", "Bitstamp", "BTC/USD", "1h");

    private static HeadlessChartFactory Stack()
    {
        var providers = new List<IIndicatorProvider> { new CoreIndicatorProvider(), new SkenderTrendProvider() };
        var indicators = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicators, new CustomIndicatorRegistry(), providers);
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var prefs = new MockIndicatorPreferencesService();
        var factory = new IndicatorModelFactory(styling, prefs);
        return new HeadlessChartFactory(indicators, engine, new IndicatorStateMapper(), factory, prefs,
            new IndicatorContextAnalyzer(), catalog: new SignalCatalog(providers));
    }

    private static SeriesConfig SavedEma(string id, int period)
    {
        var cfg = new SeriesConfig
        {
            Id = id, Name = "EMA", FriendlyName = $"EMA {period}", IndicatorCode = "Ema", Pane = "Main", IsVisible = true,
        };
        cfg.Parameters["lookbackPeriods"] = period;
        return cfg;
    }

    private static AlertDefinition CloseCrossesEma(int period) => new()
    {
        Id = "t", Name = "t", Target = AlertTarget.Indicator, Condition = AlertCondition.CrossesAbove,
        Delivery = AlertDelivery.Speech, Symbol = "BTC/USD", Provider = "Bitstamp",
        ConditionTree = new ConditionLeaf("x", "CANDLES.body", LeafOperator.CrossesAboveLine,
            SecondSignalDescriptorId: "Ema.Ema",
            SecondParameters: new Dictionary<string, double> { ["lookbackPeriods"] = period }),
    };

    private static bool IsEma(ChartSeries s, double period) =>
        s.IndicatorCode == "Ema" && s.Parameters.TryGetValue("lookbackPeriods", out var p) && p == period;

    [Fact]
    public void A_leaf_bound_to_the_second_saved_instance_gets_that_instance()
    {
        var chart = Stack().Create(Btc, new[] { SavedEma("e20", 20), SavedEma("e50", 50) }, new[] { CloseCrossesEma(50) });

        Assert.Contains(chart.Series, s => IsEma(s, 50));
    }

    [Fact]
    public void A_leaf_bound_to_an_instance_no_tab_has_gets_it_built_from_the_defaults_and_its_parameters()
    {
        var chart = Stack().Create(Btc, new[] { SavedEma("e20", 20) }, new[] { CloseCrossesEma(200) });

        Assert.Contains(chart.Series, s => IsEma(s, 200));
        Assert.Empty(chart.MissingIndicators);
    }
}
