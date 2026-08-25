using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.ScriptSandbox;

/// <summary>
/// Metadata describing a loaded indicator. Sent from worker to host in the
/// <see cref="Opcode.Ready"/> frame after a successful <c>LoadAssembly</c>.
/// </summary>
public sealed record IndicatorMetadataMessage(
    string Id,
    string DisplayName,
    string[] ComponentNames,
    int[] DisplayTypeValues,                 // ComponentDisplayType as int so the
                                             // wire format doesn't depend on the
                                             // SDK enum numbering.
    Dictionary<string, double> DefaultParameters,
    int[] CausalityValues);                  // ComponentCausality as int, same reason.
                                             // Appended last: host and worker ship in the
                                             // same build, so there is no old frame to read.

/// <summary>
/// Payload for <see cref="Opcode.Calculate"/>. Carries the OHLCV span plus
/// the parameter dictionary the host wants applied.
/// </summary>
public sealed record CalculateRequest(
    Ohlcv[] Bars,
    Dictionary<string, double> Parameters);

/// <summary>
/// Payload for <see cref="Opcode.Result"/>. One <c>double[]</c> per
/// component, in the same order as <see cref="IndicatorMetadataMessage.ComponentNames"/>.
/// </summary>
public sealed record CalculateResponse(double[][] ComponentData);

/// <summary>
/// Tiny binary codec for the messages above. We don't use JSON — the
/// Calculate path is in the indicator hot loop and allocating / parsing
/// JSON per call would show up in trading-latency graphs.
///
/// Format (all length-prefixed with u32 big-endian counts unless noted):
///
///   IndicatorMetadataMessage:
///     [str Id][str DisplayName]
///     [u32 N][str ComponentNames×N]
///     [u32 M][i32 DisplayTypeValues×M]
///     [u32 P][(str name)(f64 value)×P]
///     [u32 C][i32 CausalityValues×C]
///
///   CalculateRequest:
///     [u32 N][Ohlcv×N]
///     [u32 P][(str name)(f64 value)×P]
///
///   CalculateResponse:
///     [u32 K][per component: (u32 L)(f64×L)]
///
///   Ohlcv (fixed 48 bytes):
///     [i64 DateUtcTicks][f64 Open][f64 High][f64 Low][f64 Close][f64 Volume]
///
///   Strings: [u32 byte_count][utf8 bytes]
/// </summary>
public static class MessageCodec
{
    // Defense-in-depth caps on untrusted u32 counts from decoded payloads live in
    // Wire (shared with the strategy codec). FrameCodec already enforces a 64 MB
    // frame cap, but without per-field caps a single `u32=500_000_000` string/array
    // length triggers an OOM-class allocation before the invalid read is detected.

    // ── IndicatorMetadataMessage ───────────────────────────────────────

    public static byte[] EncodeMetadata(IndicatorMetadataMessage meta)
    {
        using var ms = new MemoryStream();
        WriteString(ms, meta.Id);
        WriteString(ms, meta.DisplayName);

        WriteU32(ms, (uint)meta.ComponentNames.Length);
        foreach (var n in meta.ComponentNames) WriteString(ms, n);

        WriteU32(ms, (uint)meta.DisplayTypeValues.Length);
        foreach (var v in meta.DisplayTypeValues) WriteI32(ms, v);

        WriteU32(ms, (uint)meta.DefaultParameters.Count);
        foreach (var kv in meta.DefaultParameters)
        {
            WriteString(ms, kv.Key);
            WriteF64(ms, kv.Value);
        }

        WriteU32(ms, (uint)meta.CausalityValues.Length);
        foreach (var v in meta.CausalityValues) WriteI32(ms, v);
        return ms.ToArray();
    }

    public static IndicatorMetadataMessage DecodeMetadata(byte[] payload)
    {
        var r = new WireReader(payload);
        var id    = r.ReadString();
        var name  = r.ReadString();

        int nComp = CheckCount(r.ReadU32(), "ComponentNames");
        var compNames = new string[nComp];
        for (int i = 0; i < nComp; i++) compNames[i] = r.ReadString();

        int nDisp = CheckCount(r.ReadU32(), "DisplayTypeValues");
        var dispVals = new int[nDisp];
        for (int i = 0; i < nDisp; i++) dispVals[i] = r.ReadI32();

        int nParam = CheckCount(r.ReadU32(), "DefaultParameters");
        var parms = new Dictionary<string, double>(nParam);
        for (int i = 0; i < nParam; i++)
        {
            var k = r.ReadString();
            var v = r.ReadF64();
            parms[k] = v;
        }
        int nCaus = CheckCount(r.ReadU32(), "CausalityValues");
        var causVals = new int[nCaus];
        for (int i = 0; i < nCaus; i++) causVals[i] = r.ReadI32();

        return new IndicatorMetadataMessage(id, name, compNames, dispVals, parms, causVals);
    }

    // ── CalculateRequest ───────────────────────────────────────────────

    public static byte[] EncodeCalculateRequest(CalculateRequest req)
    {
        using var ms = new MemoryStream();
        WriteU32(ms, (uint)req.Bars.Length);
        foreach (var bar in req.Bars) WriteOhlcv(ms, bar);

        WriteU32(ms, (uint)req.Parameters.Count);
        foreach (var kv in req.Parameters)
        {
            WriteString(ms, kv.Key);
            WriteF64(ms, kv.Value);
        }
        return ms.ToArray();
    }

    public static CalculateRequest DecodeCalculateRequest(byte[] payload)
    {
        var r = new WireReader(payload);
        int n = CheckCount(r.ReadU32(), "Bars");
        var bars = new Ohlcv[n];
        for (int i = 0; i < n; i++) bars[i] = r.ReadOhlcv();

        int p = CheckCount(r.ReadU32(), "Parameters");
        var parms = new Dictionary<string, double>(p);
        for (int i = 0; i < p; i++)
        {
            var k = r.ReadString();
            var v = r.ReadF64();
            parms[k] = v;
        }
        return new CalculateRequest(bars, parms);
    }

    // ── CalculateResponse ──────────────────────────────────────────────

    public static byte[] EncodeCalculateResponse(CalculateResponse resp)
    {
        using var ms = new MemoryStream();
        WriteU32(ms, (uint)resp.ComponentData.Length);
        foreach (var arr in resp.ComponentData)
        {
            WriteU32(ms, (uint)arr.Length);
            foreach (var v in arr) WriteF64(ms, v);
        }
        return ms.ToArray();
    }

    public static CalculateResponse DecodeCalculateResponse(byte[] payload)
    {
        var r = new WireReader(payload);
        int k = CheckCount(r.ReadU32(), "ComponentData");
        var components = new double[k][];
        for (int c = 0; c < k; c++)
        {
            int l = CheckCount(r.ReadU32(), "ComponentData[i]");
            var arr = new double[l];
            for (int i = 0; i < l; i++) arr[i] = r.ReadF64();
            components[c] = arr;
        }
        return new CalculateResponse(components);
    }

    private static int CheckCount(uint raw, string field) => Wire.CheckCount(raw, field);

    // ── Primitives ─────────────────────────────────────────────────────
    // Thin aliases onto Wire so this codec and the strategy codec cannot drift
    // apart on how a string or a bar is framed. See Wire for the format.

    private static void WriteString(MemoryStream ms, string s) => Wire.WriteString(ms, s);
    private static void WriteU32(MemoryStream ms, uint v)      => Wire.WriteU32(ms, v);
    private static void WriteI32(MemoryStream ms, int v)       => Wire.WriteI32(ms, v);
    private static void WriteF64(MemoryStream ms, double v)    => Wire.WriteF64(ms, v);
    private static void WriteOhlcv(MemoryStream ms, Ohlcv bar) => Wire.WriteOhlcv(ms, bar);
}
