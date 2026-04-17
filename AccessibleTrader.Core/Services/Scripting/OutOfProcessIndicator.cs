using System;
using System.Collections.Generic;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// <see cref="ICustomIndicator"/> proxy backed by an
/// <see cref="OutOfProcessScriptHost"/>. Every <c>Calculate</c> call
/// serializes the OHLCV span + parameters into a
/// <see cref="CalculateRequest"/>, round-trips it to the worker over
/// stdio, and deserializes the <see cref="CalculateResponse"/>.
///
/// Lifecycle ownership: the proxy owns the host — disposing the proxy
/// sends <see cref="Opcode.Shutdown"/> to the worker and kills it if it
/// doesn't exit in the grace window. Don't dispose the host externally;
/// call <see cref="DisposeAsync"/> on the proxy.
///
/// Thread-safety: Calculate is serialized by the host's internal
/// <see cref="System.Threading.SemaphoreSlim"/>, so concurrent calls are
/// safe but queued. That matches today's in-process behaviour — the
/// indicator pipeline doesn't parallelise a single indicator across ticks.
/// </summary>
public sealed class OutOfProcessIndicator : ICustomIndicator, IAsyncDisposable
{
    private readonly OutOfProcessScriptHost _host;
    private readonly ComponentDisplayType[] _displayTypes;

    public OutOfProcessIndicator(OutOfProcessScriptHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        var meta = host.Metadata;
        Id                = meta.Id;
        DisplayName       = meta.DisplayName;
        ComponentNames    = meta.ComponentNames;
        DefaultParameters = meta.DefaultParameters;
        _displayTypes     = Array.ConvertAll(meta.DisplayTypeValues, v => (ComponentDisplayType)v);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string[] ComponentNames { get; }
    public ComponentDisplayType[] DisplayTypes => _displayTypes;
    public Dictionary<string, double> DefaultParameters { get; }

    public double[][] Calculate(ReadOnlySpan<Ohlcv> data, Dictionary<string, double> parameters)
    {
        // The ICustomIndicator surface is synchronous. We have to block on
        // the out-of-process call — the indicator pipeline invokes us
        // synchronously from an orchestrator step that already runs on a
        // background thread, so blocking here doesn't freeze the UI.
        var bars = data.ToArray(); // copy once, since Span can't cross the await
        var req = new CalculateRequest(bars, parameters ?? new Dictionary<string, double>());
        return _host.CalculateAsync(req).GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
