using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The <c>0</c> key works only where a reference level applies.</b>
///
/// <para>
/// Cody, 2026-10-09: <i>"the 0 key should simply not work on series that they don't apply to, no
/// alt shift h suggestion."</i> Until then, <c>0</c> on the candles, the price line or any overlay
/// in the price pane dropped a "Level" at the cursor's close — a price-pane level that duplicated
/// what a horizontal line drawing is for — and a second press on the same bar removed it again.
/// On a price series the key now changes NOTHING: no level added, none removed, none switched. It
/// says, briefly and as a Boundary, that no reference level applies to that series, and it does not
/// point at another key.
/// </para>
///
/// <para>
/// Where it does apply — an indicator in its own pane that declares the line it swings about — it
/// keeps every behaviour <see cref="ZeroKeyTogglesTheMidlineTests"/> pins.
/// </para>
/// </summary>
public sealed class ZeroKeyOnlyWhereALevelAppliesTests
{
    private static ChartSeries Candles(params LevelConfig[] levels)
    {
        var config = new SeriesConfig
        {
            Id = CoreSeriesIds.Candles, IndicatorCode = "CANDLES", Name = "Candles", FriendlyName = "Candles", Pane = "Main",
        };
        config.Components.Add(new ComponentConfig
        {
            Name = "body", DisplayName = "Body", DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body, IsVisible = true,
        });
        foreach (var l in levels) config.Levels.Add(l);
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries PriceLine()
    {
        var config = new SeriesConfig { Id = CoreSeriesIds.Price, IndicatorCode = "PRICE", Name = "Price", FriendlyName = "Price line", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "close", DisplayName = "Close", DisplayType = ComponentDisplayType.Line, Role = ComponentRole.PriceAction, IsVisible = true,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    /// <summary>An overlay on the price pane — even one whose component happens to carry a
    /// ReferenceLevel must not qualify, because the price pane has no neutral.</summary>
    private static ChartSeries Sma(double? referenceLevel = null)
    {
        var config = new SeriesConfig { Id = "sma", IndicatorCode = "Sma", Name = "SMA", FriendlyName = "SMA 20", Pane = "Main" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Sma", DisplayName = "SMA", DisplayType = ComponentDisplayType.Line, IsVisible = true, ReferenceLevel = referenceLevel,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries Obv(double? referenceLevel = null)
    {
        var config = new SeriesConfig { Id = "obv", IndicatorCode = "Obv", Name = "OBV", FriendlyName = "OBV", Pane = "Pane_Obv" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Obv", DisplayName = "OBV", DisplayType = ComponentDisplayType.Line, IsVisible = true, ReferenceLevel = referenceLevel,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries Rsi()
    {
        var config = new SeriesConfig { Id = "rsi", IndicatorCode = "Rsi", Name = "RSI", FriendlyName = "RSI 14", Pane = "Pane_Rsi" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Rsi", DisplayName = "RSI", DisplayType = ComponentDisplayType.Oscillator, IsVisible = true, ReferenceLevel = 50.0,
        });
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
    }

    private static ChartSeries HorizontalLineDrawing()
    {
        var config = new SeriesConfig { Id = "hl", Name = "Horizontal", FriendlyName = "Horizontal line", Pane = "Main" };
        config.Components.Add(new ComponentConfig { Name = "Line", DisplayName = "Line", DisplayType = ComponentDisplayType.Line, IsVisible = true });
        var s = new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id });
        s.Drawing = new DrawingData { Type = DrawingType.HorizontalLine, AnchorPrice1 = 100 };
        return s;
    }

    private static (CommandDispatcher Dispatcher, SpyEventBus Bus, MockWorkspaceStore Store) Build(ChartSeries series)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv>
            {
                new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1),
                new(new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 63_920.11, 1),
            }),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = 1,
        });
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        // 0 is chart-scoped; without this the press never reaches the handler and every "nothing
        // happened" assertion below would pass for the wrong reason.
        dispatcher.SetChartActive(true);
        return (dispatcher, bus, store);
    }

    public static TheoryData<string> PricePaneSeries => new() { "candles", "price", "sma", "sma-with-reference" };

    private static ChartSeries ByName(string which) => which switch
    {
        "candles" => Candles(),
        "price" => PriceLine(),
        "sma" => Sma(),
        "sma-with-reference" => Sma(referenceLevel: 0),
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    /// <summary>Cody's report: on a price-pane series the key does nothing to the chart, and says
    /// so as a Boundary — the key was understood and has nowhere to go.</summary>
    [Theory]
    [MemberData(nameof(PricePaneSeries))]
    public void OnAPricePaneSeries_ZeroChangesNothing_AndSaysNoLevelApplies(string which)
    {
        var series = ByName(which);
        var (dispatcher, bus, store) = Build(series);

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        Assert.Empty(store.DispatchedActions);
        var spoken = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
        Assert.Equal(FeedbackType.Boundary, spoken.Type);
        Assert.Contains("no reference level applies", spoken.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(series.FriendlyName, spoken.Message);
    }

    /// <summary>
    /// "Pressing 0 on a price pane must not remove anything either." Before this change the key
    /// removed a level of yours that sat within a quarter of a percent of the cursor's close. Saved
    /// workspaces still carry such levels; they are user data and stay, removable from Properties.
    /// </summary>
    [Fact]
    public void OnTheCandles_AnExistingLevelOfYours_IsNotRemoved()
    {
        var mine = new LevelConfig { Name = "Level", Value = 63_920.11, IsVisible = true, PlayEarcon = true, IsUserDefined = true };
        var (dispatcher, bus, store) = Build(Candles(mine));

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        Assert.Empty(store.DispatchedActions);
        Assert.Equal(FeedbackType.Boundary, Assert.Single(bus.Log.OfType<FeedbackRequestEvent>()).Type);
    }

    /// <summary>...and a price-pane level carrying a midline's name is not switched either.</summary>
    [Fact]
    public void OnTheCandles_ALevelNamedLikeAMidline_IsNotSwitched()
    {
        var provider = new LevelConfig { Name = "Midpoint", Value = 63_920.11, IsVisible = true, PlayEarcon = true };
        var (dispatcher, _, store) = Build(Candles(provider));

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        Assert.Empty(store.DispatchedActions);
        Assert.True(provider.IsVisible);
        Assert.True(provider.PlayEarcon);
    }

    /// <summary>A pane that declares no neutral gets the same answer as the price pane.</summary>
    [Fact]
    public void OnAnIndicatorPaneWithNoDeclaredNeutral_ZeroChangesNothing_AndSaysNoLevelApplies()
    {
        var (dispatcher, bus, store) = Build(Obv());

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        Assert.Empty(store.DispatchedActions);
        var spoken = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
        Assert.Equal(FeedbackType.Boundary, spoken.Type);
        Assert.Contains("no reference level applies", spoken.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A drawing has no neutral either. It used to be an ERROR ("Focus a series first"),
    /// which plays the one earcon Shift+F3 cannot mute for a key that failed nothing.</summary>
    [Fact]
    public void OnADrawing_ZeroIsABoundaryNotAnError()
    {
        var (dispatcher, bus, store) = Build(HorizontalLineDrawing());

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        Assert.Empty(store.DispatchedActions);
        var spoken = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>());
        Assert.Equal(FeedbackType.Boundary, spoken.Type);
        Assert.Contains("no reference level applies", spoken.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cody: "no alt shift h suggestion." The refusal names no key at all.</summary>
    [Theory]
    [MemberData(nameof(PricePaneSeries))]
    public void TheRefusalNamesNoOtherKey(string which)
    {
        var (dispatcher, bus, _) = Build(ByName(which));

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        string message = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>()).Message ?? "";
        Assert.DoesNotContain("Alt", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Shift", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("press", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The negative half: where a neutral IS declared, the key still adds it.</summary>
    [Fact]
    public void OnAnIndicatorThatDeclaresItsNeutral_ZeroStillAddsTheMidline()
    {
        var (dispatcher, _, store) = Build(Rsi());

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        var added = Assert.IsType<AddLevelAction>(Assert.Single(store.DispatchedActions));
        Assert.Equal(50.0, added.Level.Value);
    }

    // ── The rule, as a function ──────────────────────────────────────────────

    [Fact]
    public void Applies_OnlyToAnIndicatorPaneThatDeclaresANeutral()
    {
        Assert.False(ReferenceLevelPlacement.Applies(Candles()));
        Assert.False(ReferenceLevelPlacement.Applies(PriceLine()));
        Assert.False(ReferenceLevelPlacement.Applies(Sma(referenceLevel: 0)));
        Assert.False(ReferenceLevelPlacement.Applies(Obv()));
        Assert.False(ReferenceLevelPlacement.Applies(HorizontalLineDrawing()));
        Assert.True(ReferenceLevelPlacement.Applies(Rsi()));
        Assert.True(ReferenceLevelPlacement.Applies(Obv(referenceLevel: 0)));

        // A provider's own midline is a declaration even where no component states the value.
        var macd = Obv();
        macd.Levels.Add(new LevelConfig { Name = "Zero", Value = 0, IsVisible = true });
        Assert.True(ReferenceLevelPlacement.Applies(macd));
    }

    // ── The in-app hint offers 0 only where 0 works ──────────────────────────

    [Fact]
    public void TheNarrationHint_OffersZero_OnlyWhereZeroApplies()
    {
        string? without = SeriesNarrationScope.WhyNothingToNarrate(Obv());
        Assert.NotNull(without);
        Assert.DoesNotContain("0", without);

        string? with = SeriesNarrationScope.WhyNothingToNarrate(Obv(referenceLevel: 0));
        Assert.NotNull(with);
        Assert.Contains("Press 0", with);
    }
}
