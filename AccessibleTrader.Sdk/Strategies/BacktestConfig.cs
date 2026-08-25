namespace AccessibleTrader.Sdk.Strategies;

/// <param name="StartingCapital">Notional account size at bar zero.</param>
/// <param name="CommissionRate">Per-side commission as a decimal (0.001 = 0.1%).</param>
/// <param name="SlippagePercent">Per-side slippage as a decimal (0.0005 = 0.05%).</param>
/// <param name="WarmupBars">
/// Number of bars at the start of the data window that the backtester will FEED to the
/// strategy (so its indicators settle and indicator caches converge) but will NOT take
/// signals from. Necessary because indicators like Ichimoku (~78), Cipher C (66 stability
/// window), SMA(200), and Hull MA need many bars before their internal state is meaningful;
/// signals emitted during warmup are unreliable and would skew metrics. Default 200 is a
/// reasonable floor for the built-in strategies; the modal lets the user override per-run.
/// </param>
/// <param name="ReplayProfiles">
/// When true and one or more VPVR / VPFR / TPO indicator series are loaded on the workspace,
/// the backtester recomputes their bins per bar from <c>history[0..i]</c> via
/// <c>IProfileService</c> and stashes the snapshot in <c>IBacktestProfileCache</c> before
/// invoking the strategy. <c>VolumeProfileLevelProvider</c> reads from the cache instead of
/// the live <c>series.ProfileBins</c>, eliminating the future-leak that would otherwise
/// surface the workspace's *final* profile state at every historical bar. The compute cost
/// is non-trivial — disable this flag for fast iteration on strategies that don't gate on
/// POC / Value Area / HVN / LVN levels. Default true so correctness is the default.
/// </param>
/// <param name="PositionSizer">Override the default <c>FixedSizePositionSizer</c>.</param>
/// <param name="StartDate">
/// Optional inclusive lower bound on bar timestamps. Bars before this date are dropped from
/// the run entirely (they don't even feed warmup). Used to walk-forward test a strategy by
/// running it across distinct date ranges to detect time-localized edge decay — e.g. split a
/// 9-year dataset into "first half" and "second half" and verify both halves produce similar
/// metrics. When null, the backtester uses every bar in the data argument from the start.
/// </param>
/// <param name="EndDate">
/// Optional inclusive upper bound on bar timestamps. Bars after this date are dropped. Pairs
/// with <see cref="StartDate"/> for walk-forward windowing. When null, runs through the
/// final bar in the data argument.
/// </param>
/// <param name="AllowReverseOnSignal">
/// When true (default, matches all live-app behavior to date), a fresh long signal arriving
/// while a long position is already open will close the existing remainder at next-bar open
/// and immediately open a new position in the same direction (or reverse, if the new signal
/// is the opposite side). The exit row carries an "ExitReason: Reversed by ..." string and is
/// the protective fast-exit mechanism that capped many losses in the v11 H1 result. Setting
/// this to false suppresses the reverse — incoming signals are simply ignored while a position
/// is open, and the existing position rides until its stop or TP ladder closes it. Used by the
/// research harness to isolate "true entry edge" from "structural exit luck": if v11's
/// profitability collapses without reverse-on-signal, the edge was structural; if it survives,
/// the entry signal has measurable predictive value of its own.
/// </param>
public record BacktestConfig(
    double StartingCapital = 10000.0,
    double CommissionRate = 0.001,      // 0.1% per trade
    double SlippagePercent = 0.0005,    // 0.05% slippage
    int WarmupBars = 200,
    bool ReplayProfiles = true,
    IPositionSizer? PositionSizer = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    bool AllowReverseOnSignal = true
);
