using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Security;

/// <summary>
/// Persistent file-sink decorator for <see cref="ISecurityEventLog"/>.
/// Wraps an inner ring-buffer log and ALSO appends every recorded event
/// to a daily-rotated JSONL file under an operator-supplied directory,
/// so security-relevant events survive a process crash for post-incident
/// forensics.
///
/// <para>
/// The base <see cref="SecurityEventLog"/> mirrors every event to
/// <see cref="ILogger"/> at Warning level, which means hosts that wire
/// an <see cref="ILoggerProvider"/> file sink (Serilog etc.) already get
/// persistence for free. This decorator exists for operators who run
/// with a default console logger and still want a durable record —
/// zero configuration, predictable path layout, machine-parseable JSONL.
/// </para>
///
/// <para>
/// Write path: single producer-consumer channel would be overkill given
/// events are human-frequency (a couple per minute at most under heavy
/// load). We do a lock-around-append with a short <see cref="StreamWriter"/>
/// lifetime, re-opened on each write. At ~50-byte events and a few
/// writes/minute the syscall overhead is invisible, and we never hold
/// an OS file handle longer than one write — so a crash between events
/// can't truncate or corrupt prior records.
/// </para>
///
/// <para>
/// Files land at <c>&lt;dir&gt;/security-events-YYYY-MM-DD.jsonl</c>, UTC-dated,
/// one record per line. Format:
/// <code>
/// {"ts":"2026-04-23T18:04:02.111Z","kind":"AppContainerFallback","source":"WindowsAppContainerLauncher","message":"...","data":{...}}
/// </code>
/// Directory is created if missing; a failing write logs to the inner
/// ILogger at Warning and swallows the exception — the sink must never
/// propagate IO failures back to security-event producers because the
/// producers can't do anything useful with them.
/// </para>
/// </summary>
public sealed class SecurityEventFileSink : ISecurityEventLog, IDisposable
{
    private readonly ISecurityEventLog _inner;
    private readonly string _directory;
    private readonly ILogger<SecurityEventFileSink>? _logger;
    private readonly object _writeLock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SecurityEventFileSink(
        ISecurityEventLog inner,
        string directory,
        ILogger<SecurityEventFileSink>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _logger = logger;

        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex)
        {
            // Don't fail construction — a broken persistence path should
            // degrade gracefully to in-memory-only. Log the failure so the
            // operator can see it; subsequent writes will also try+fail+log.
            _logger?.LogWarning(ex,
                "SecurityEventFileSink: could not create directory '{Directory}'. File persistence disabled.",
                _directory);
        }
    }

    public void Record(SecurityEvent ev)
    {
        // Always forward to the inner ring buffer first so the in-memory
        // observability path is unaffected by any file-IO failure below.
        _inner.Record(ev);
        if (_disposed || ev is null) return;

        try
        {
            AppendToDailyFile(ev);
        }
        catch (Exception ex)
        {
            // Last-resort swallow: the sink must never throw back to
            // callers (who are frequently in error-handling paths themselves).
            _logger?.LogWarning(ex,
                "SecurityEventFileSink: failed to persist event {Kind} from {Source}.",
                ev.Kind, ev.Source);
        }
    }

    public IReadOnlyList<SecurityEvent> Recent(int limit = 200, DateTime? since = null)
        => _inner.Recent(limit, since);

    private void AppendToDailyFile(SecurityEvent ev)
    {
        string path = Path.Combine(
            _directory,
            $"security-events-{ev.UtcTimestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.jsonl");

        var record = new FileRecord(
            Ts: ev.UtcTimestamp.ToString("o", CultureInfo.InvariantCulture),
            Kind: ev.Kind.ToString(),
            Source: ev.Source,
            Message: ev.Message,
            Data: ev.Data is null ? null : new Dictionary<string, string>(ev.Data));

        string line = JsonSerializer.Serialize(record, JsonOptions);

        // Short-held file handle keeps crash-resilience (no dangling buffered
        // writes) and avoids interleaving under concurrent producers via the
        // object lock. UTF-8 without BOM matches one-line-per-record JSONL conventions.
        lock (_writeLock)
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    // Serialised with camelCase properties; ordering here mirrors the JSONL format.
    private sealed record FileRecord(
        string Ts,
        string Kind,
        string Source,
        string Message,
        Dictionary<string, string>? Data);
}
