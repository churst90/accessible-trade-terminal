using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>Shared fixtures for the <c>MarketEvent*Tests</c> classes.</summary>
internal static class MarketEventTestData
{
    public static readonly IReadOnlyList<MarketType> AllMarkets = Enum.GetValues<MarketType>();

    public static Ohlcv Bar(DateTime utc, double open = 100, double close = 101)
    {
        var at = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new Ohlcv(at, open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 10);
    }

    public static MarketEvent Event(
        string id = "test-1",
        MarketEventKind kind = MarketEventKind.Economy,
        string spoken = "CPI",
        string longName = "Consumer Price Index",
        MarketEventImportance importance = MarketEventImportance.High,
        DateTime? moment = null,
        MarketEventPrecision precision = MarketEventPrecision.Exact,
        DateOnly? date = null,
        params MarketType[] scope)
    {
        var momentUtc = DateTime.SpecifyKind(
            moment ?? new DateTime(2024, 3, 12, 12, 30, 0, DateTimeKind.Utc),
            DateTimeKind.Utc);
        return new MarketEvent(
            id,
            kind,
            spoken,
            longName,
            scope.Length == 0 ? AllMarkets : scope,
            importance,
            momentUtc,
            precision,
            date ?? DateOnly.FromDateTime(momentUtc),
            "Test source");
    }

    public static MarketEventCalendar Calendar(params MarketEvent[] events)
        => new(events);

    public static WorkspaceState State(
        IReadOnlyList<Ohlcv> bars,
        MarketType market = MarketType.Crypto,
        string timeframe = "30m",
        int index = 0,
        string level = "off",
        bool economy = true,
        bool fed = true,
        bool expiries = true,
        bool holidays = true,
        bool halvings = true,
        string symbol = "TEST",
        bool withCandles = false)
    {
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Test", symbol, timeframe),
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = index,
            SelectedMarketType = market,
            MarketEventsLevel = level,
            MarketEventsEconomy = economy,
            MarketEventsFed = fed,
            MarketEventsExpiries = expiries,
            MarketEventsHolidays = holidays,
            MarketEventsHalvings = halvings,
        };
        if (withCandles)
        {
            state = state with
            {
                ActiveSeries = ImmutableList.Create(CandleSeries()),
                FocusedSeriesId = CoreSeriesIds.Candles,
                PrimarySeriesId = CoreSeriesIds.Candles,
            };
        }
        return state;
    }

    public static ChartSeries CandleSeries()
    {
        var config = new SeriesConfig
        {
            Id = CoreSeriesIds.Candles,
            IndicatorCode = "CANDLES",
            Name = "Candles",
            Pane = "Main",
        };
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });
    }

    public static MarketEventBarPlacement Placed(MarketEvent e, int barIndex, string? placement = null)
        => new(e, barIndex, placement);
}
