using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Core.Services.Scripting;

/// <summary>
/// <see cref="ITradingStrategy"/> proxy backed by an <see cref="OutOfProcessScriptHost"/>. The
/// script's code runs in the sandboxed worker; what comes back over the pipe is a
/// <see cref="StrategySignal"/> — a description of an order, not an order — which the host's own
/// risk rules, position manager and order service then decide what to do with.
///
/// <para>
/// That is the whole point of the class. Until this existed, <c>CompileStrategyAsync</c> loaded
/// user IL into the trading host with <c>alc.LoadFromStream</c>: no worker, no OS sandbox, no
/// memory or CPU quota, no kill switch, and the Roslyn semantic walker as the only wall — for the
/// half of the scripting surface that can open positions. Indicators had gone out-of-process;
/// strategies, which are the thing that trades, had not.
/// </para>
///
/// <para>
/// <b>History is sent incrementally.</b> <c>OnBar</c> is called once per bar of a backtest with a
/// history that grows by one each time; re-sending it whole would move ~4.8 GB over a 10,000-bar
/// run. The worker keeps its own copy and this proxy sends only the bars it has not sent, pinned
/// at both ends by first-bar date and total count so a prepend can never be mistaken for an
/// append. Any disagreement is a hard error and the next call resends everything.
/// </para>
///
/// <para>
/// <b>Workspace state is sent when it changes.</b> The backtester builds one <c>liveState</c>
/// before its loop and passes that same record to every bar, so it crosses the pipe once per run;
/// the live engine hands out a fresh record per bar, so there it crosses per bar. Change is
/// detected by REFERENCE — <see cref="WorkspaceState"/> is a record and the store replaces it
/// rather than editing it, so a new reference is what a change looks like. The gap that leaves:
/// mutating a <c>ChartSeries</c> in place while the enclosing state reference stays put is not
/// noticed. Nothing in the reducer does that, and the alternative — deep-comparing a full
/// component stack every bar — costs more than the send it would save.
/// </para>
///
/// <para>
/// Thread-safety: every call is serialised by the host's IO gate, matching the in-process
/// behaviour the engine already relies on (it takes <c>_evalGate</c> around OnBar/OnStop because
/// strategies are stateful).
/// </para>
/// </summary>
public sealed class OutOfProcessStrategy : ITradingStrategy, IRestartableStrategy, IAsyncDisposable
{
    private readonly OutOfProcessScriptHost _host;

    // What the worker's history currently holds, as far as this proxy knows. -1 means
    // "unknown — resend everything", which is the state after any failed frame.
    private int _sentBarCount = -1;
    private long _sentFirstBarTicks;

    // The last state reference handed to the worker. Reference equality is the change test;
    // see the class remarks for what that does and does not catch.
    private WorkspaceState? _sentState;

    public OutOfProcessStrategy(OutOfProcessScriptHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        var meta = host.StrategyMetadata;
        Id          = meta.Id;
        Name        = meta.Name;
        Description = meta.Description;
        Complexity  = (StrategyComplexityLevel)meta.ComplexityValue;
        Parameters  = meta.Parameters ?? Array.Empty<StrategyParameter>();
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public StrategyComplexityLevel Complexity { get; }
    public IReadOnlyList<StrategyParameter> Parameters { get; }

    public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues)
    {
        var bars = ToArray(history);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (parameterValues != null)
            foreach (var kv in parameterValues) parameters[kv.Key] = kv.Value;

        // Reset BEFORE the call: if it throws, the next OnBar must resend rather than send a
        // delta against a history the worker may or may not be holding.
        _sentBarCount = -1;
        _sentState = null;

        Block(_host.InitializeStrategyAsync(new InitializeStrategyRequest(bars, parameters, state)));

        _sentBarCount = bars.Length;
        _sentFirstBarTicks = bars.Length > 0 ? bars[0].Date.Ticks : 0;
        _sentState = state;
    }

    public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
    {
        int count = history?.Count ?? 0;
        long firstTicks = count > 0 ? history![0].Date.Ticks : 0;

        // Append only when the worker's copy is a genuine PREFIX of what we now hold: same first
        // bar, and no shorter than what it has. Anything else — a prepend from a scrollback
        // fetch, a truncation, a symbol change, a previously failed frame — is a full resend.
        bool canAppend = _sentBarCount >= 0
                         && count >= _sentBarCount
                         && firstTicks == _sentFirstBarTicks;

        Ohlcv[] delta;
        if (canAppend)
        {
            delta = new Ohlcv[count - _sentBarCount];
            for (int i = 0; i < delta.Length; i++) delta[i] = history![_sentBarCount + i];
        }
        else
        {
            delta = ToArray(history);
        }

        var request = new OnBarRequest(
            newBar,
            new HistorySync(FullResync: !canAppend, Bars: delta, ExpectedCount: count, FirstBarTicks: firstTicks),
            State: ReferenceEquals(state, _sentState) ? null : state);

        StrategySignal? signal;
        try
        {
            signal = Block(_host.OnBarAsync(request));
        }
        catch
        {
            // The worker's history is now whatever the failed frame left behind. Forget it.
            _sentBarCount = -1;
            _sentState = null;
            throw;
        }

        _sentBarCount = count;
        _sentFirstBarTicks = firstTicks;
        _sentState = state;
        return signal;
    }

    public void OnOrderFilled(OrderUpdate fill) => Block(_host.OnOrderFilledAsync(fill));

    /// <summary>
    /// Runs the strategy's own teardown in the worker. Does NOT end the worker — the engine calls
    /// this when a strategy is removed and the probe calls it between runs, and killing the
    /// process on the second of those would end the check after its first run. Disposal is
    /// <see cref="DisposeAsync"/>.
    /// </summary>
    public void OnStop() => Block(_host.StopStrategyAsync());

    public StrategyMetrics GetMetrics() => Block(_host.GetStrategyMetricsAsync());

    /// <summary>
    /// See <see cref="IRestartableStrategy"/>. Returns <c>this</c>: the worker builds a fresh
    /// instance on every <c>InitializeStrategy</c> frame, so starting over is what the caller's
    /// next <c>Initialize</c> already does.
    /// </summary>
    public ITradingStrategy StartFresh() => this;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private static Ohlcv[] ToArray(IReadOnlyList<Ohlcv>? bars)
    {
        if (bars == null || bars.Count == 0) return Array.Empty<Ohlcv>();
        var copy = new Ohlcv[bars.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = bars[i];
        return copy;
    }

    // ITradingStrategy is synchronous and its callers (the engine's bar-close evaluation, the
    // backtester's replay loop, the causality probe) all run on background threads that already
    // block. Same trade the indicator proxy makes for the same reason.
    private static void Block(Task task) => task.GetAwaiter().GetResult();
    private static T Block<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
