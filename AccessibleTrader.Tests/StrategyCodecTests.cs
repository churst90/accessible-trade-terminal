using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests;

/// <summary>
/// The strategy protocol's message layer, on its own.
///
/// <para>
/// <c>OutOfProcessStrategyTests</c> drives the same codec through a real worker, which is the
/// test that matters — but a wire format only fails there at the field the reader ran off the
/// end at, several fields after the one that was actually mis-encoded. These pin each message
/// separately, so a mismatch names the field it is about.
/// </para>
/// </summary>
public class StrategyCodecTests
{
    // ── StrategySignal ────────────────────────────────────────────────────────────

    [Fact]
    public void An_order_with_every_field_set_round_trips()
    {
        var signal = new StrategySignal(
            OrderSide.Sell, OrderType.StopLimit,
            Quantity: 0.125, LimitPrice: 61234.5, StopLoss: 60000.25, TakeProfit: 65000.75,
            Rationale: "divergence on the four hour — Ω unicode and a | pipe",
            Confidence: 0.875,
            TpLadder: new[] { 62000.0, 63000.5, 64000.25 },
            TpClosePortions: new[] { 0.5, 0.25, 0.125 },
            StopAdjust: StopAdjustOnTp1.TrailByAtr,
            TrailAtrPeriod: 21,
            TrailAtrMultiple: 2.75);

        var back = RoundTrip(StrategyCodec.EncodeSignal(new SignalResponse(signal)),
                             StrategyCodec.DecodeSignal).Signal;

        Assert.NotNull(back);
        // Field by field rather than record equality: StrategySignal's two ladders are
        // IReadOnlyList<double>, which the generated Equals compares by REFERENCE, so
        // Assert.Equal(signal, back) can only ever fail here — and would have hidden a
        // scalar mismatch behind a diff of two identical-looking records.
        Assert.Equal(signal.Side, back!.Side);
        Assert.Equal(signal.OrderType, back.OrderType);
        Assert.Equal(signal.Quantity, back.Quantity);
        Assert.Equal(signal.LimitPrice, back.LimitPrice);
        Assert.Equal(signal.StopLoss, back.StopLoss);
        Assert.Equal(signal.TakeProfit, back.TakeProfit);
        Assert.Equal(signal.Rationale, back.Rationale);
        Assert.Equal(signal.Confidence, back.Confidence);
        Assert.Equal(signal.TpLadder, back.TpLadder);
        Assert.Equal(signal.TpClosePortions, back.TpClosePortions);
        Assert.Equal(signal.StopAdjust, back.StopAdjust);
        Assert.Equal(signal.TrailAtrPeriod, back.TrailAtrPeriod);
        Assert.Equal(signal.TrailAtrMultiple, back.TrailAtrMultiple);
    }

    /// <summary>
    /// Most bars produce nothing, and "no order" has to be distinguishable from "an order with
    /// every field at zero" — a Buy at quantity zero is a different thing from silence, and the
    /// causality probe compares exactly on that distinction.
    /// </summary>
    [Fact]
    public void No_order_and_an_all_zero_order_are_different_on_the_wire()
    {
        var silence = RoundTrip(StrategyCodec.EncodeSignal(new SignalResponse(null)),
                                StrategyCodec.DecodeSignal).Signal;
        Assert.Null(silence);

        var zero = new StrategySignal(OrderSide.Buy, OrderType.Market, 0, 0, 0, 0, "", 0);
        var back = RoundTrip(StrategyCodec.EncodeSignal(new SignalResponse(zero)),
                             StrategyCodec.DecodeSignal).Signal;
        Assert.NotNull(back);
        Assert.Equal(0, back!.Quantity);
    }

    [Fact]
    public void An_absent_ladder_stays_absent_rather_than_becoming_an_empty_one()
    {
        var signal = new StrategySignal(OrderSide.Buy, OrderType.Market, 1, null, null, null, "flat", 0.5);
        var back = RoundTrip(StrategyCodec.EncodeSignal(new SignalResponse(signal)),
                             StrategyCodec.DecodeSignal).Signal;

        // The backtester branches on `TpLadder != null` to decide whether to run the ladder at
        // all, so null and empty are different instructions.
        Assert.Null(back!.TpLadder);
        Assert.Null(back.TpClosePortions);
        // Same for the optional prices: no stop is a different order from a stop at zero.
        Assert.Null(back.LimitPrice);
        Assert.Null(back.StopLoss);
        Assert.Null(back.TakeProfit);
        Assert.Equal(1, back.Quantity);
    }

    /// <summary>
    /// The pipe carries whatever the other end wrote, and a worker that has been killed mid-write
    /// leaves a short frame. A decoder that reads past the end of its buffer would surface as
    /// garbage values in an order — so every read bounds-checks, and running out throws at the
    /// field that ran out rather than returning something plausible.
    /// </summary>
    [Fact]
    public void A_truncated_payload_throws_instead_of_decoding_into_garbage()
    {
        var full = StrategyCodec.EncodeOrderUpdate(new OrderUpdate(
            "ord-42", "ETH/USD", OrderSide.Sell, 1.5, 3210.75, 0.5, OrderStatus.Filled,
            false, false, new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc), 12.5, false, "why"));

        for (int cut = 1; cut < 12; cut++)
        {
            var truncated = full[..^cut];
            Assert.ThrowsAny<Exception>(() => StrategyCodec.DecodeOrderUpdate(truncated));
        }
    }

    // ── StrategyMetrics / OrderUpdate ─────────────────────────────────────────────

    [Fact]
    public void Metrics_round_trip()
    {
        var metrics = new StrategyMetrics(17, 9, 0.529, -1234.5, 8765.25, 1.75, 12345.5, 3579.25);
        Assert.Equal(metrics, RoundTrip(StrategyCodec.EncodeMetrics(metrics), StrategyCodec.DecodeMetrics));
    }

    [Fact]
    public void A_fill_round_trips_including_the_fields_the_speech_layer_reads()
    {
        var fill = new OrderUpdate(
            OrderId: "ord-42", Symbol: "ETH/USD", Side: OrderSide.Sell,
            FilledQuantity: 1.5, FilledPrice: 3210.75, RemainingQuantity: 0.5,
            Status: OrderStatus.PartialFill, StopTriggered: true, TakeProfitTriggered: false,
            Timestamp: new DateTime(2026, 8, 25, 14, 32, 5, DateTimeKind.Utc),
            RealizedPnL: -87.25, Trailing: true, Reason: "trailing stop");

        var back = RoundTrip(StrategyCodec.EncodeOrderUpdate(fill), StrategyCodec.DecodeOrderUpdate);

        Assert.Equal(fill, back);
        Assert.Equal(DateTimeKind.Utc, back.Timestamp.Kind);
    }

    [Fact]
    public void A_fill_with_no_reason_and_no_realized_pnl_keeps_both_nulls()
    {
        var fill = new OrderUpdate("o", "S", OrderSide.Buy, 1, 2, 3, OrderStatus.Filled,
            false, false, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var back = RoundTrip(StrategyCodec.EncodeOrderUpdate(fill), StrategyCodec.DecodeOrderUpdate);

        // Reason null means "nobody asked why"; empty string would read as a spoken blank.
        Assert.Null(back.Reason);
        Assert.Null(back.RealizedPnL);
    }

    // ── Metadata and its tagged values ────────────────────────────────────────────

    [Fact]
    public void Parameter_defaults_of_each_supported_type_survive_as_that_type()
    {
        var meta = new StrategyMetadataMessage("ID", "Name", "Description",
            (int)StrategyComplexityLevel.Intermediate,
            new[]
            {
                new StrategyParameter("period", "bars", StrategyParameterType.Integer, 14, 2, 200),
                new StrategyParameter("threshold", "level", StrategyParameterType.Double, 1.5),
                new StrategyParameter("enabled", "on", StrategyParameterType.Boolean, true),
                new StrategyParameter("label", "text", StrategyParameterType.String, "hello",
                                      null, null, new[] { "hello", "bye" }),
                new StrategyParameter("code", "an indicator", StrategyParameterType.IndicatorCode, "RSI"),
            });

        var back = RoundTrip(StrategyCodec.EncodeMetadata(meta), StrategyCodec.DecodeMetadata);

        Assert.Equal("ID", back.Id);
        Assert.Equal((int)StrategyComplexityLevel.Intermediate, back.ComplexityValue);

        var byName = back.Parameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
        // Integers widen to long and floats to double — the tag set is deliberately five wide,
        // not one per CLR numeric type, and every consumer of DefaultValue already goes through
        // Convert because the field is `object` on both sides.
        Assert.Equal(14L, byName["period"].DefaultValue);
        Assert.Equal(2L, byName["period"].MinValue);
        Assert.Equal(200L, byName["period"].MaxValue);
        Assert.Equal(1.5d, byName["threshold"].DefaultValue);
        Assert.Equal(true, byName["enabled"].DefaultValue);
        Assert.Equal("hello", byName["label"].DefaultValue);
        Assert.Equal(new[] { "hello", "bye" }, byName["label"].AllowedValues);
        Assert.Null(byName["code"].AllowedValues);   // absent, not empty
        Assert.Null(byName["threshold"].MinValue);
    }

    /// <summary>
    /// A parameter type the wire has no tag for must fail by NAME at encode time. Guessing —
    /// <c>ToString()</c>, or a zero — hands the strategy a value it never declared, and a desynced
    /// pipe reports itself as "malformed stream" a frame later with nothing to point at.
    /// </summary>
    [Fact]
    public void A_parameter_the_wire_cannot_carry_is_refused_by_name()
    {
        var meta = new StrategyMetadataMessage("ID", "N", "D", 0, new[]
        {
            new StrategyParameter("window", "how long", StrategyParameterType.String, TimeSpan.FromHours(4)),
        });

        var ex = Assert.Throws<NotSupportedException>(() => StrategyCodec.EncodeMetadata(meta));
        Assert.Contains("window", ex.Message, StringComparison.Ordinal);
        Assert.Contains("TimeSpan", ex.Message, StringComparison.Ordinal);
    }

    // ── Initialize / OnBar ────────────────────────────────────────────────────────

    [Fact]
    public void Initialize_carries_its_history_its_parameters_and_the_workspace()
    {
        var history = Bars(120);
        var request = new InitializeStrategyRequest(
            history,
            new Dictionary<string, object?>
            {
                ["period"] = 21,
                ["threshold"] = 0.75,
                ["enabled"] = false,
                ["label"] = "swing",
                ["absent"] = null,
            },
            WorkspaceState.Initial with { SymbolDisplayName = "Bitcoin", IsBacktesting = true });

        var back = RoundTrip(StrategyCodec.EncodeInitialize(request), StrategyCodec.DecodeInitialize);

        Assert.Equal(history.Length, back.History.Length);
        Assert.Equal(history[0].Date, back.History[0].Date);
        Assert.Equal(history[^1].Close, back.History[^1].Close);
        Assert.Equal(21L, back.Parameters["period"]);
        Assert.Equal(0.75d, back.Parameters["threshold"]);
        Assert.Equal(false, back.Parameters["enabled"]);
        Assert.Equal("swing", back.Parameters["label"]);
        Assert.Null(back.Parameters["absent"]);
        Assert.True(back.Parameters.ContainsKey("absent"));   // the key survives, not just the value
        Assert.Equal("Bitcoin", back.State.SymbolDisplayName);
        Assert.True(back.State.IsBacktesting);
    }

    [Fact]
    public void An_OnBar_frame_with_no_state_says_so_rather_than_sending_an_empty_one()
    {
        var bars = Bars(3);
        var request = new OnBarRequest(
            bars[2],
            new HistorySync(FullResync: false, Bars: new[] { bars[2] }, ExpectedCount: 3, FirstBarTicks: bars[0].Date.Ticks),
            State: null);

        var back = RoundTrip(StrategyCodec.EncodeOnBar(request), StrategyCodec.DecodeOnBar);

        Assert.Null(back.State);
        Assert.False(back.History.FullResync);
        Assert.Single(back.History.Bars);
        Assert.Equal(3, back.History.ExpectedCount);
        Assert.Equal(bars[0].Date.Ticks, back.History.FirstBarTicks);
        Assert.Equal(bars[2].Close, back.Bar.Close);
    }

    [Fact]
    public void A_full_resync_carries_the_whole_history_and_says_it_is_a_replacement()
    {
        var bars = Bars(200);
        var request = new OnBarRequest(
            bars[^1],
            new HistorySync(FullResync: true, Bars: bars, ExpectedCount: bars.Length, FirstBarTicks: bars[0].Date.Ticks),
            State: WorkspaceState.Initial);

        var back = RoundTrip(StrategyCodec.EncodeOnBar(request), StrategyCodec.DecodeOnBar);

        Assert.True(back.History.FullResync);
        Assert.Equal(bars.Length, back.History.Bars.Length);
        Assert.NotNull(back.State);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static T RoundTrip<T>(byte[] payload, Func<byte[], T> decode) => decode(payload);

    private static Ohlcv[] Bars(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
                100 + i, 101 + i, 99 + i, 100.5 + i, 1000 + i))
            .ToArray();
}
