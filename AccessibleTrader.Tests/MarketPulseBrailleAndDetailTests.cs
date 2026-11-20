using System.Reactive.Subjects;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.MarketPulse;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Screening;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The pulse's two follow-ups: the compact line for the Dot Pad's strip (held there, an item a
/// page, until you move on the chart) and Ctrl+Alt+P's detail read of the same reading.
/// </summary>
public class MarketPulseBrailleAndDetailTests
{
    private static readonly DateTime Monday = new(2026, 11, 23, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Friday = new(2026, 11, 20);

    // ── The braille line ──

    [Fact]
    public void TheHandoffsReadingAsOneCompactLine()
    {
        var quotes = new[]
        {
            new MarketPulseQuote("S&P", MarketPulseKind.Change, Change: 0.4, Short: "SPX"),
            new MarketPulseQuote("Bitcoin", MarketPulseKind.Change, Change: -1.24, Short: "BTC"),
            new MarketPulseQuote("ten-year", MarketPulseKind.Yield, Change: 3, YieldLevel: 4.31, IsYield: true, Short: "10Y"),
            new MarketPulseQuote("ten-year", MarketPulseKind.Yield, Change: -1, YieldLevel: 4.30, IsYield: true, Short: "10Y"),
            new MarketPulseQuote("dollar index", MarketPulseKind.Change, Change: 0.04, Short: "DXY"),
            new MarketPulseQuote("gold", MarketPulseKind.Change, Change: 0.2, MinutesOld: 4, Short: "Gold"),
            new MarketPulseQuote("oil", MarketPulseKind.Unavailable, Short: "Oil"),
            new MarketPulseQuote("VIX", MarketPulseKind.Closed, Change: 2.1, SessionDate: Friday, Short: "VIX"),
            new MarketPulseQuote("SOL", MarketPulseKind.Change, Change: 5),
        };

        Assert.Equal("SPX +0.4, BTC -1.2, 10Y 4.31 +3bp, 10Y 4.30 -1bp, DXY 0.0, Gold +0.2 4m, Oil n/a, VIX cl +2.1, SOL +5.0",
            MarketPulseWording.BrailleLine(quotes));
        Assert.Equal("Pulse empty", MarketPulseWording.BrailleLine(Array.Empty<MarketPulseQuote>()));
    }

    [Theory]
    [InlineData("Fmp", "^GSPC", MarketType.Index, "SPX")]
    [InlineData("Fmp", "TNX", MarketType.Index, "10Y")]
    [InlineData("Coinbase", "BTC/USD", MarketType.Crypto, "BTC")]
    [InlineData("Coinbase", "SOL-USD", MarketType.Crypto, "SOL")]
    [InlineData("Fmp", "AAPL", MarketType.Stock, "AAPL")]
    public void EachItemHasAShortName(string provider, string symbol, MarketType market, string shortName)
        => Assert.Equal(shortName, MarketPulseDefaults.ShortName(new WatchlistEntry(provider, symbol, market)));

    // ── What the math hands the detail ──

    private static Ohlcv Bar(DateTime date, double open, double close) => new(date, open, open, close, close, 1);

    [Fact]
    public void EveryAnswerCarriesItsSourcePriceStartAndTime()
    {
        var now = new DateTime(2026, 11, 18, 13, 0, 0, DateTimeKind.Utc);
        var btc = new WatchlistEntry("Coinbase", "BTC/USD", MarketType.Crypto);
        var daily = new[] { Bar(now.Date.AddDays(-1), 100, 105), Bar(now.Date, 110, 120) };
        var minute = new[] { Bar(now.AddMinutes(-5), 124, 125) };

        var midnight = MarketPulseMath.Quote(btc, daily, minute, now, PulseCryptoStart.MidnightUtc);
        Assert.Equal(("BTC", "Coinbase", "BTC/USD"), (midnight.Short, midnight.Provider, midnight.Symbol));
        Assert.Equal((125d, 110d), (midnight.Last, midnight.Reference));
        Assert.Equal(MarketPulseStart.MidnightUtc, midnight.Start);
        Assert.Equal(now.AddMinutes(-4), midnight.QuotedUtc);

        var rolling = MarketPulseMath.Quote(btc, daily, minute, now, PulseCryptoStart.Last24Hours);
        Assert.Equal(MarketPulseStart.Last24Hours, rolling.Start);

        var sp = new WatchlistEntry("Fmp", "^GSPC", MarketType.Index);
        var weekday = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
        var open = MarketPulseMath.Quote(sp, new[] { Bar(weekday.Date.AddDays(-1), 99, 100), Bar(weekday.Date, 101, 102) },
            Array.Empty<Ohlcv>(), weekday, PulseCryptoStart.MidnightUtc);
        Assert.Equal((MarketPulseStart.PreviousClose, (DateTime?)null, 102d, 100d),
            (open.Start, open.QuotedUtc, open.Last, open.Reference));

        var saturday = new DateTime(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
        var closed = MarketPulseMath.Quote(sp, new[]
            {
                Bar(new DateTime(2026, 11, 19, 14, 30, 0, DateTimeKind.Utc), 99, 100),
                Bar(new DateTime(2026, 11, 20, 14, 30, 0, DateTimeKind.Utc), 100, 98),
            }, Array.Empty<Ohlcv>(), saturday, PulseCryptoStart.MidnightUtc);
        Assert.Equal((MarketPulseKind.Closed, MarketPulseStart.SessionBefore, 98d, 100d),
            (closed.Kind, closed.Start, closed.Last, closed.Reference));

        var none = MarketPulseMath.Unavailable(sp);
        Assert.Equal((MarketPulseKind.Unavailable, "Fmp", "^GSPC", "SPX"), (none.Kind, none.Provider, none.Symbol, none.Short));
    }

    // ── The detail read ──

    [Fact]
    public void TheDetailNamesSourcePriceStartAndAgeForEachKind()
    {
        var quotes = new[]
        {
            new MarketPulseQuote("Bitcoin", MarketPulseKind.Change, Change: -1.234, Provider: "Coinbase", Symbol: "BTC/USD",
                Last: 94210.5, Reference: 95387.6, Start: MarketPulseStart.MidnightUtc, QuotedUtc: Monday.AddMinutes(-1)),
            new MarketPulseQuote("ten-year", MarketPulseKind.Yield, Change: 3, YieldLevel: 4.31, IsYield: true,
                Provider: "Fmp", Symbol: "TNX", Last: 4.31, Reference: 4.28, Start: MarketPulseStart.PreviousClose),
            new MarketPulseQuote("VIX", MarketPulseKind.Closed, Change: 2.1, SessionDate: Friday, Provider: "Fmp",
                Symbol: "^VIX", Last: 15.32, Reference: 15.005, Start: MarketPulseStart.SessionBefore),
            new MarketPulseQuote("oil", MarketPulseKind.Unavailable, Provider: "Fmp", Symbol: "WTICOUSD"),
            new MarketPulseQuote("SOL", MarketPulseKind.Change, Change: 0.001),
        };

        Assert.Equal(
            "Pulse read 2 hours ago. "
          + "Bitcoin: BTC/USD on Coinbase, 94210.50, down 1.23% from midnight UTC 95387.60, last minute bar ended 1 minute ago. "
          + "ten-year: TNX on Fmp, 4.31%, up 3 basis points from the previous close 4.28%, daily bars only. "
          + "VIX: ^VIX on Fmp, closed; last session Friday ended at 15.32, up 2.10% from the session before, 15.01. "
          + "oil: WTICOUSD on Fmp, unavailable, no usable quote within the four-second limit. "
          + "SOL: no source, SOL flat.",
            MarketPulseWording.Detail(quotes, Monday.AddMinutes(-125), Monday));
    }

    // ── The Dot Pad ──

    private sealed class Display : ITactileDriver
    {
        public bool IsConnected { get; private set; }
        public string DeviceName => "Dot Pad";
        public int DisplayWidth => 60;
        public int DisplayHeight => 40;
        public int BrailleCellCount => 20;
        public List<string> Strip { get; } = new();
        public event EventHandler<TactileKeyEvent>? KeyPressed;
#pragma warning disable CS0067
        public event EventHandler<TactileConnectionEvent>? ConnectionChanged;
#pragma warning restore CS0067
        public Task ConnectAsync() { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
        public Task RenderViewportAsync(bool[,] virtualCanvas, int startX, int startY) => Task.CompletedTask;
        public Task RenderBrailleTextAsync(string text) { lock (Strip) Strip.Add(text); return Task.CompletedTask; }
        public void Press(TactileKey key) => KeyPressed?.Invoke(this, new TactileKeyEvent(key));
        public string Last { get { lock (Strip) return Strip[^1]; } }
    }

    private sealed class Rig : IDisposable
    {
        public readonly Display Pad = new();
        public readonly SpyEventBus Bus = new();
        public readonly ICommandDispatcher Dispatcher = Substitute.For<ICommandDispatcher>();
        public readonly BehaviorSubject<WorkspaceState> Stream;
        public readonly TactileCanvasCoordinator Coord;

        public Rig()
        {
            Pad.ConnectAsync();
            var bars = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc), 100, 110, 90, 105, 1),
                new Ohlcv(new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc), 105, 115, 95, 110, 1));
            Stream = new BehaviorSubject<WorkspaceState>(WorkspaceState.Initial with { Data = bars, CurrentDataIndex = 1 });
            var store = Substitute.For<IWorkspaceStore>();
            store.StateStream.Returns(Stream);
            store.State.Returns(_ => Stream.Value);
            Coord = new TactileCanvasCoordinator(Pad, store, Substitute.For<ISpeechFeedbackRouter>(), Dispatcher,
                Substitute.For<ISettingsManager>(), Bus, NullLogger<TactileCanvasCoordinator>.Instance);
        }

        public void Tick(double close)
        {
            var s = Stream.Value;
            var bars = s.Data!.ToList();
            bars[^1] = bars[^1] with { Close = close };
            Stream.OnNext(s with { Data = new TimeSeriesBuffer<Ohlcv>(bars.ToArray()) });
        }

        public void Dispose() => Coord.Dispose();
    }

    [Fact]
    public void TheStripShowsAnItemAPageAndKeepsItThroughLiveTicks()
    {
        using var rig = new Rig();
        rig.Bus.Publish(new TactileStripMessageEvent("SPX +0.4, BTC -1.2, 10Y 4.31 +3bp"));
        Assert.Equal("SPX +0.4", rig.Pad.Last);

        rig.Tick(111);
        Assert.Equal("SPX +0.4", rig.Pad.Last);

        rig.Pad.Press(TactileKey.PanRight);
        Assert.Equal("BTC -1.2", rig.Pad.Last);
        rig.Pad.Press(TactileKey.PanLeft);
        rig.Pad.Press(TactileKey.PanLeft);
        Assert.Equal("SPX +0.4", rig.Pad.Last);
        rig.Pad.Press(TactileKey.PanRight);
        rig.Pad.Press(TactileKey.PanRight);
        Assert.Equal("10Y 4.31 +3bp", rig.Pad.Last);
        rig.Dispatcher.DidNotReceive().Dispatch(Arg.Any<SystemCommand>());

        // Past the last item the chart comes back, and the pan keys pan it again.
        rig.Pad.Press(TactileKey.PanRight);
        Assert.Equal("111", rig.Pad.Last);
        rig.Pad.Press(TactileKey.PanRight);
        rig.Dispatcher.Received(1).Dispatch(SystemCommand.PanRight);
    }

    [Fact]
    public void MovingOnTheChartLetsTheChartBackIn()
    {
        using var rig = new Rig();
        rig.Bus.Publish(new TactileStripMessageEvent("SPX +0.4, BTC -1.2"));

        rig.Stream.OnNext(rig.Stream.Value with { CurrentDataIndex = 0 });

        Assert.NotEqual("SPX +0.4", rig.Pad.Last);
        rig.Pad.Press(TactileKey.PanRight);
        rig.Dispatcher.Received(1).Dispatch(SystemCommand.PanRight);
    }

    [Theory]
    [InlineData("SPX +0.4, BTC -1.2", 20, new[] { "SPX +0.4", "BTC -1.2" })]
    [InlineData("A twenty-five char item!", 20, new[] { "A twenty-five char i", "tem!" })]
    [InlineData("", 20, new string[0])]
    [InlineData("SPX +0.4", 0, new string[0])]
    public void PagesAreWholeItemsCutOnlyWhenOneIsTooLong(string text, int cells, string[] pages)
        => Assert.Equal(pages, TactileCanvasCoordinator.StripPages(text, cells));
}
