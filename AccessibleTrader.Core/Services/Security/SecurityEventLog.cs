using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Security;

/// <summary>
/// In-process implementation of <see cref="ISecurityEventLog"/>. Keeps a
/// fixed-size ring buffer of recent <see cref="SecurityEvent"/> records
/// and mirrors each one to <see cref="Microsoft.Extensions.Logging"/>
/// at Warning level so it also flows into whatever log sinks the host
/// has configured (file, console, journal).
///
/// <para>
/// Thread-safe by virtue of a single <see cref="_gate"/> lock around
/// the buffer — contention is negligible (writes at human-scale
/// frequency, reads on operator demand). No background thread,
/// no IO on the write path; <see cref="ILogger"/> emission is
/// synchronous but almost always a no-op in non-debug builds.
/// </para>
///
/// <para>
/// Buffer size is intentionally small (256 entries, ~64 KB) — this is
/// a near-term operational log, not a long-term audit record. If
/// forensics needs retention beyond the running session, the host
/// should wire an <see cref="ILoggerProvider"/> that persists the
/// Warning-level emissions to disk (Serilog file sink, etc.). The
/// ring buffer's purpose is to answer "what happened right before
/// the user noticed something broke?"
/// </para>
/// </summary>
public sealed class SecurityEventLog : ISecurityEventLog
{
    private const int Capacity = 256;

    private readonly object _gate = new();
    private readonly SecurityEvent?[] _buffer = new SecurityEvent?[Capacity];
    private int _writeIndex;
    private int _count;

    private readonly ILogger<SecurityEventLog>? _logger;

    public SecurityEventLog(ILogger<SecurityEventLog>? logger = null)
    {
        _logger = logger;
    }

    public void Record(SecurityEvent ev)
    {
        if (ev is null) return;

        lock (_gate)
        {
            _buffer[_writeIndex] = ev;
            _writeIndex = (_writeIndex + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        // Mirror to ILogger so the event flows into the host's normal
        // telemetry stack. Warning level — these are by definition
        // noteworthy but not fatal.
        _logger?.LogWarning(
            "[security-event] {Kind} src={Source} msg={Message} data={Data}",
            ev.Kind, ev.Source, ev.Message,
            ev.Data is null ? "" : string.Join(";", ev.Data.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    public IReadOnlyList<SecurityEvent> Recent(int limit = 200, DateTime? since = null)
    {
        if (limit <= 0) return Array.Empty<SecurityEvent>();

        SecurityEvent?[] snapshot;
        int snapCount;
        int snapWrite;
        lock (_gate)
        {
            snapshot = (SecurityEvent?[])_buffer.Clone();
            snapCount = _count;
            snapWrite = _writeIndex;
        }

        // Walk backwards from the most-recent slot. _writeIndex points
        // at the *next* slot to write, so the newest recorded event is
        // at (writeIndex - 1) mod Capacity.
        var result = new List<SecurityEvent>(Math.Min(limit, snapCount));
        for (int i = 0; i < snapCount && result.Count < limit; i++)
        {
            int idx = ((snapWrite - 1 - i) % Capacity + Capacity) % Capacity;
            var ev = snapshot[idx];
            if (ev is null) continue;
            if (since.HasValue && ev.UtcTimestamp < since.Value) continue;
            result.Add(ev);
        }
        return result;
    }
}
