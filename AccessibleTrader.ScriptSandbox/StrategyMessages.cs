using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// Metadata describing a loaded strategy. Sent worker → host in the
/// <see cref="Opcode.StrategyReady"/> frame after a <c>LoadAssembly</c> that found an
/// <c>ITradingStrategy</c>. Enums travel as ints so the wire format does not depend on SDK
/// enum numbering, matching <see cref="IndicatorMetadataMessage"/>.
/// </summary>
public sealed record StrategyMetadataMessage(
    string Id,
    string Name,
    string Description,
    int ComplexityValue,
    StrategyParameter[] Parameters);

/// <summary>
/// How the worker's copy of the growing bar history is brought up to date.
///
/// <para>
/// This is the reason the strategy protocol is not simply "send OnBar's three arguments". A
/// backtest is one <c>OnBar</c> per bar over the whole series, and the history argument grows by
/// one bar each time. Re-sending it whole is quadratic: a 10,000-bar run moves ~4.8 GB across the
/// pipe to communicate 10,000 new bars. So the worker keeps the history and the host sends only
/// what it has not sent yet.
/// </para>
///
/// <para>
/// The check that makes that safe is the one the scrollback bug taught: array LENGTHS cannot tell
/// an append from a prepend. <see cref="FirstBarTicks"/> and <see cref="ExpectedCount"/> pin both
/// ends, and any disagreement after applying the delta is a hard error the host answers with a
/// full resend rather than a guess.
/// </para>
/// </summary>
/// <param name="FullResync">
/// True when <see cref="Bars"/> REPLACES the worker's history (first call, a prepend, a
/// truncation, a symbol change). False when it is appended.
/// </param>
/// <param name="Bars">The whole history, or just the tail, per <paramref name="FullResync"/>.</param>
/// <param name="ExpectedCount">Total bar count the worker must hold after applying.</param>
/// <param name="FirstBarTicks">UTC ticks of bar 0 after applying; 0 when the history is empty.</param>
public sealed record HistorySync(
    bool FullResync,
    Ohlcv[] Bars,
    int ExpectedCount,
    long FirstBarTicks);

/// <summary>
/// Payload for <see cref="Opcode.InitializeStrategy"/>. The history here is sent whole — an
/// Initialize is the point at which the worker's idea of the series is being (re)established,
/// so there is nothing to be incremental against.
/// </summary>
public sealed record InitializeStrategyRequest(
    Ohlcv[] History,
    Dictionary<string, object?> Parameters,
    WorkspaceState State);

/// <summary>
/// Payload for <see cref="Opcode.OnBar"/>.
/// </summary>
/// <param name="Bar">The bar that just closed.</param>
/// <param name="History">The delta bringing the worker's history to this bar. See <see cref="HistorySync"/>.</param>
/// <param name="State">
/// The workspace state, or <c>null</c> meaning "unchanged since the last frame — reuse what you
/// have". The backtester builds one <c>liveState</c> before its loop and passes that same
/// instance every bar, so this is null for all but the first bar of a backtest; the live engine
/// hands out a fresh record per bar, so there it is sent every time.
/// </param>
public sealed record OnBarRequest(
    Ohlcv Bar,
    HistorySync History,
    WorkspaceState? State);

/// <summary>
/// Payload for <see cref="Opcode.Signal"/>. The nullability is the message: most bars produce no
/// order, and "no order" has to be distinguishable from "an order with every field at zero".
/// </summary>
public sealed record SignalResponse(StrategySignal? Signal);

/// <summary>
/// Binary codec for the strategy frames. Same reasoning as
/// <see cref="MessageCodec"/> — <c>OnBar</c> runs once per bar of a backtest and JSON per call
/// would be the dominant cost of the whole run — and the same primitives, so the two codecs
/// cannot disagree about how a bar or a string is framed.
/// </summary>
public static class StrategyCodec
{
    // ── StrategyMetadataMessage ───────────────────────────────────────────────────

    public static byte[] EncodeMetadata(StrategyMetadataMessage meta)
    {
        using var ms = new MemoryStream();
        Wire.WriteString(ms, meta.Id);
        Wire.WriteString(ms, meta.Name);
        Wire.WriteString(ms, meta.Description);
        Wire.WriteI32(ms, meta.ComplexityValue);

        var parameters = meta.Parameters ?? Array.Empty<StrategyParameter>();
        Wire.WriteU32(ms, (uint)parameters.Length);
        foreach (var p in parameters)
        {
            Wire.WriteString(ms, p.Name);
            Wire.WriteString(ms, p.Description);
            Wire.WriteI32(ms, (int)p.Type);
            Wire.WriteTagged(ms, p.Name + ".DefaultValue", p.DefaultValue);
            Wire.WriteTagged(ms, p.Name + ".MinValue", p.MinValue);
            Wire.WriteTagged(ms, p.Name + ".MaxValue", p.MaxValue);

            var allowed = p.AllowedValues;
            Wire.WriteBool(ms, allowed != null);
            if (allowed != null)
            {
                Wire.WriteU32(ms, (uint)allowed.Length);
                foreach (var v in allowed) Wire.WriteString(ms, v);
            }
        }
        return ms.ToArray();
    }

    public static StrategyMetadataMessage DecodeMetadata(byte[] payload)
    {
        var r = new WireReader(payload);
        var id          = r.ReadString();
        var name        = r.ReadString();
        var description = r.ReadString();
        var complexity  = r.ReadI32();

        int n = Wire.CheckCount(r.ReadU32(), "StrategyParameters");
        var parameters = new StrategyParameter[n];
        for (int i = 0; i < n; i++)
        {
            var pName = r.ReadString();
            var pDesc = r.ReadString();
            var pType = (StrategyParameterType)r.ReadI32();
            var def   = r.ReadTagged();
            var min   = r.ReadTagged();
            var max   = r.ReadTagged();

            string[]? allowed = null;
            if (r.ReadBool())
            {
                int count = Wire.CheckCount(r.ReadU32(), "StrategyParameter.AllowedValues");
                allowed = new string[count];
                for (int j = 0; j < count; j++) allowed[j] = r.ReadString();
            }

            // DefaultValue is non-nullable on the record. A strategy that declared a null default
            // gets an empty string rather than a NullReferenceException at the first read of it.
            parameters[i] = new StrategyParameter(pName, pDesc, pType, def ?? "", min, max, allowed);
        }
        return new StrategyMetadataMessage(id, name, description, complexity, parameters);
    }

    // ── InitializeStrategyRequest ─────────────────────────────────────────────────

    public static byte[] EncodeInitialize(InitializeStrategyRequest req)
    {
        using var ms = new MemoryStream();
        Wire.WriteOhlcvArray(ms, req.History);

        var parameters = req.Parameters ?? new Dictionary<string, object?>();
        Wire.WriteU32(ms, (uint)parameters.Count);
        foreach (var kv in parameters)
        {
            Wire.WriteString(ms, kv.Key);
            Wire.WriteTagged(ms, kv.Key, kv.Value);
        }

        WorkspaceProjection.Write(ms, req.State);
        return ms.ToArray();
    }

    public static InitializeStrategyRequest DecodeInitialize(byte[] payload)
    {
        var r = new WireReader(payload);
        var history = r.ReadOhlcvArray("InitializeStrategy.History");

        int n = Wire.CheckCount(r.ReadU32(), "InitializeStrategy.Parameters");
        var parameters = new Dictionary<string, object?>(n, StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var key = r.ReadString();
            parameters[key] = r.ReadTagged();
        }

        var state = WorkspaceProjection.Read(ref r);
        return new InitializeStrategyRequest(history, parameters, state);
    }

    // ── OnBarRequest ──────────────────────────────────────────────────────────────

    public static byte[] EncodeOnBar(OnBarRequest req)
    {
        using var ms = new MemoryStream();
        Wire.WriteOhlcv(ms, req.Bar);

        Wire.WriteBool(ms, req.History.FullResync);
        Wire.WriteOhlcvArray(ms, req.History.Bars);
        Wire.WriteI32(ms, req.History.ExpectedCount);
        Wire.WriteI64(ms, req.History.FirstBarTicks);

        Wire.WriteBool(ms, req.State != null);
        if (req.State != null) WorkspaceProjection.Write(ms, req.State);
        return ms.ToArray();
    }

    public static OnBarRequest DecodeOnBar(byte[] payload)
    {
        var r = new WireReader(payload);
        var bar = r.ReadOhlcv();

        bool fullResync = r.ReadBool();
        var bars        = r.ReadOhlcvArray("OnBar.History");
        int expected    = r.ReadI32();
        long firstTicks = r.ReadI64();

        WorkspaceState? state = r.ReadBool() ? WorkspaceProjection.Read(ref r) : null;
        return new OnBarRequest(bar, new HistorySync(fullResync, bars, expected, firstTicks), state);
    }

    // ── SignalResponse ────────────────────────────────────────────────────────────

    public static byte[] EncodeSignal(SignalResponse resp)
    {
        using var ms = new MemoryStream();
        var s = resp.Signal;
        Wire.WriteBool(ms, s != null);
        if (s == null) return ms.ToArray();

        Wire.WriteI32(ms, (int)s.Side);
        Wire.WriteI32(ms, (int)s.OrderType);
        Wire.WriteNullableF64(ms, s.Quantity);
        Wire.WriteNullableF64(ms, s.LimitPrice);
        Wire.WriteNullableF64(ms, s.StopLoss);
        Wire.WriteNullableF64(ms, s.TakeProfit);
        Wire.WriteString(ms, s.Rationale);
        Wire.WriteF64(ms, s.Confidence);
        WriteOptionalDoubles(ms, s.TpLadder);
        WriteOptionalDoubles(ms, s.TpClosePortions);
        Wire.WriteI32(ms, (int)s.StopAdjust);
        Wire.WriteI32(ms, s.TrailAtrPeriod);
        Wire.WriteF64(ms, s.TrailAtrMultiple);
        return ms.ToArray();
    }

    public static SignalResponse DecodeSignal(byte[] payload)
    {
        var r = new WireReader(payload);
        if (!r.ReadBool()) return new SignalResponse(null);

        var side      = (OrderSide)r.ReadI32();
        var orderType = (OrderType)r.ReadI32();
        var quantity  = r.ReadNullableF64();
        var limit     = r.ReadNullableF64();
        var stop      = r.ReadNullableF64();
        var target    = r.ReadNullableF64();
        var rationale = r.ReadString();
        var confidence = r.ReadF64();
        var ladder     = ReadOptionalDoubles(ref r, "StrategySignal.TpLadder");
        var portions   = ReadOptionalDoubles(ref r, "StrategySignal.TpClosePortions");
        var stopAdjust = (StopAdjustOnTp1)r.ReadI32();
        var trailPeriod = r.ReadI32();
        var trailMultiple = r.ReadF64();

        return new SignalResponse(new StrategySignal(
            side, orderType, quantity, limit, stop, target, rationale, confidence,
            ladder, portions, stopAdjust, trailPeriod, trailMultiple));
    }

    // ── OrderUpdate ───────────────────────────────────────────────────────────────

    public static byte[] EncodeOrderUpdate(OrderUpdate fill)
    {
        using var ms = new MemoryStream();
        Wire.WriteString(ms, fill.OrderId);
        Wire.WriteString(ms, fill.Symbol);
        Wire.WriteI32(ms, (int)fill.Side);
        Wire.WriteF64(ms, fill.FilledQuantity);
        Wire.WriteF64(ms, fill.FilledPrice);
        Wire.WriteF64(ms, fill.RemainingQuantity);
        Wire.WriteI32(ms, (int)fill.Status);
        Wire.WriteBool(ms, fill.StopTriggered);
        Wire.WriteBool(ms, fill.TakeProfitTriggered);
        Wire.WriteDate(ms, fill.Timestamp);
        Wire.WriteNullableF64(ms, fill.RealizedPnL);
        Wire.WriteBool(ms, fill.Trailing);
        Wire.WriteNullableString(ms, fill.Reason);
        return ms.ToArray();
    }

    public static OrderUpdate DecodeOrderUpdate(byte[] payload)
    {
        var r = new WireReader(payload);
        return new OrderUpdate(
            OrderId: r.ReadString(),
            Symbol: r.ReadString(),
            Side: (OrderSide)r.ReadI32(),
            FilledQuantity: r.ReadF64(),
            FilledPrice: r.ReadF64(),
            RemainingQuantity: r.ReadF64(),
            Status: (OrderStatus)r.ReadI32(),
            StopTriggered: r.ReadBool(),
            TakeProfitTriggered: r.ReadBool(),
            Timestamp: r.ReadDate(),
            RealizedPnL: r.ReadNullableF64(),
            Trailing: r.ReadBool(),
            Reason: r.ReadNullableString());
    }

    // ── StrategyMetrics ───────────────────────────────────────────────────────────

    public static byte[] EncodeMetrics(StrategyMetrics m)
    {
        using var ms = new MemoryStream();
        Wire.WriteI32(ms, m.TotalSignals);
        Wire.WriteI32(ms, m.WinningTrades);
        Wire.WriteF64(ms, m.WinRate);
        Wire.WriteF64(ms, m.MaxDrawdown);
        Wire.WriteF64(ms, m.TotalPnL);
        Wire.WriteF64(ms, m.SharpeRatio);
        Wire.WriteF64(ms, m.GrossProfit);
        Wire.WriteF64(ms, m.GrossLoss);
        return ms.ToArray();
    }

    public static StrategyMetrics DecodeMetrics(byte[] payload)
    {
        var r = new WireReader(payload);
        return new StrategyMetrics(
            TotalSignals: r.ReadI32(),
            WinningTrades: r.ReadI32(),
            WinRate: r.ReadF64(),
            MaxDrawdown: r.ReadF64(),
            TotalPnL: r.ReadF64(),
            SharpeRatio: r.ReadF64(),
            GrossProfit: r.ReadF64(),
            GrossLoss: r.ReadF64());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static void WriteOptionalDoubles(Stream s, IReadOnlyList<double>? values)
    {
        Wire.WriteBool(s, values != null);
        if (values == null) return;
        Wire.WriteU32(s, (uint)values.Count);
        for (int i = 0; i < values.Count; i++) Wire.WriteF64(s, values[i]);
    }

    private static double[]? ReadOptionalDoubles(ref WireReader r, string field)
    {
        if (!r.ReadBool()) return null;
        int n = Wire.CheckCount(r.ReadU32(), field);
        var values = new double[n];
        for (int i = 0; i < n; i++) values[i] = r.ReadF64();
        return values;
    }
}
