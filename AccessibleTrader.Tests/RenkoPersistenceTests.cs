using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>Renko is a saved choice and its box settings save with it; an old file still opens.</summary>
public sealed class RenkoPersistenceTests
{
    private sealed class Dir : IDisposable
    {
        public string Path { get; } = TestTemp.NewDir("att-renko-");
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }

    private static WorkspaceLibraryService Library(string dir) =>
        new(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
        { LibraryDirectoryOverride = dir };

    private static WorkspaceState State() => WorkspaceState.Initial with
    {
        Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
        Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 10)
            .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddHours(i), 100 + i, 101 + i, 99 + i, 100 + i, 10))),
        ChartStyle = PriceChartStyle.Renko,
        Renko = new RenkoSettings(RenkoBoxMode.Fixed, 500, 21),
        BrickBoxSize = 500,
    };

    [Fact]
    public void Renko_and_its_settings_round_trip_through_the_workspace_file()
    {
        using var dir = new Dir();
        var state = State();
        var withSnapshot = state with
        {
            TabSnapshots = ImmutableList.Create(new TabSnapshot(
                0, state.Identity, state.Data, state.ActiveSeries,
                0, null, 0, -1, 0, 0, 100, 10, (0, 0),
                ImmutableDictionary<string, (double, double)>.Empty,
                false, false, InteractionContext.Series, null,
                InitializationStatus.Ready, DataStatus.Ready, false, null, 0, -1)
            {
                ChartStyle = PriceChartStyle.Renko,
                Renko = new RenkoSettings(RenkoBoxMode.Fixed, 500, 21),
                BrickBoxSize = 500,
            }),
        };

        var store = new MockWorkspaceStore();
        store.EmitState(withSnapshot);
        var lib = Library(dir.Path);
        lib.SaveWorkspaceProfile("renko", store);

        var loaded = lib.LoadProfile("renko")!;

        Assert.Equal(PriceChartStyle.Renko, loaded.Tabs[0].ChartStyle);
        Assert.Equal(new RenkoSettings(RenkoBoxMode.Fixed, 500, 21), loaded.Tabs[0].Renko);
        Assert.Equal(500, loaded.Tabs[0].BrickBoxSize, 10);
    }

    [Fact]
    public void A_saved_file_without_Renko_loads_Candles_and_the_default_settings()
    {
        using var dir = new Dir();
        File.WriteAllText(Path.Combine(dir.Path, "old.json"), """
        {
          "Mode": 0,
          "SelectedMarketType": 0,
          "ActiveTabIndex": 0,
          "Tabs": [
            {
              "Market": "Spot",
              "Provider": "Bitstamp",
              "Symbol": "BTC/USD",
              "Timeframe": "1h",
              "ViewportStartIndex": 0,
              "ViewportLength": 100,
              "IsHeikinAshi": false,
              "IsLogScale": false,
              "Series": [],
              "PaneHeightRatios": {}
            }
          ]
        }
        """);

        var loaded = Library(dir.Path).LoadProfile("old");

        Assert.NotNull(loaded);
        Assert.Equal(PriceChartStyle.Candles, loaded!.Tabs[0].ChartStyle);
        Assert.Equal(RenkoBoxMode.Atr, loaded.Tabs[0].Renko.Mode);
        Assert.Equal(0, loaded.Tabs[0].Renko.BoxSize);
        Assert.Equal(14, loaded.Tabs[0].Renko.AtrLength);
    }
}
