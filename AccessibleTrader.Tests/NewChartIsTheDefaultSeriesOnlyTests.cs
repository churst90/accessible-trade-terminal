using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A new chart is Candles, Volume and Price, and nothing else</b> (Cody, 2026-11-04: "Remove the
/// 'add market structure highs and lows to new charts' option, it's an indicator people can add it if
/// they want it, just the default series should be on a new chart, that's it"). Market Structure is
/// offered by the indicator service like every other indicator; a new chart must not pick it up.
/// </summary>
public sealed class NewChartIsTheDefaultSeriesOnlyTests
{
    private static IndicatorMetadata Meta(string code) => new() { Code = code, Name = code };

    [Fact]
    public void A_new_chart_registers_only_the_default_series_even_when_Market_Structure_is_available()
    {
        var seriesService = Substitute.For<ISeriesManagementService>();
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(WorkspaceState.Initial);
        var all = new List<IndicatorMetadata>
        {
            Meta("CANDLES"), Meta("VOLUME"), Meta("PRICE"), Meta(SwingStructureProvider.Code), Meta("Rsi"),
        };
        var indicators = Substitute.For<IIndicatorService>();
        indicators.GetAvailableIndicators().Returns(all);

        new WorkspaceInitializer(seriesService, store, indicators).InitializeDefaultSeries(ProviderDataShape.Ohlcv);

        var registered = seriesService.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ISeriesManagementService.RegisterSeriesFromMetadata))
            .Select(c => ((IndicatorMetadata)c.GetArguments()[0]!).Code)
            .ToList();
        Assert.NotEmpty(registered);
        Assert.All(registered, code => Assert.Contains(code, new[] { "CANDLES", "VOLUME", "PRICE" }));
        Assert.DoesNotContain(SwingStructureProvider.Code, registered);
    }
}
