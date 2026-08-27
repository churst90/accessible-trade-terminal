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
/// <param name="SpreadPercent">
/// The FULL quoted bid-ask spread as a decimal fraction of price (0.0004 = 4 basis points).
/// Half of it is charged per side, because OHLCV bars are a single price series — a buyer
/// crossing to the ask pays half the spread above that price and a seller hitting the bid
/// receives half of it below. It is applied on top of <see cref="SlippagePercent"/> at all four
/// fill sites (entry, stop exit, take-profit rung, end-of-data close); slippage models the
/// market moving while the order is in flight, spread models the cost of crossing at all, and
/// a backtest that omits the second is quoting mid-to-mid prices no one can trade at.
/// Default 0 — every historical result in this repo was computed without it, so turning it on
/// is an explicit act, not a silent re-scoring.
/// </param>
/// <param name="FundingRatePerInterval">
/// Perpetual-futures funding as a decimal fraction of position notional, charged once per
/// funding interval held (0.0001 = 1 basis point per interval; ~0.01% per 8h is the typical
/// crypto-perp resting rate). Sign follows the exchange convention: when the rate is POSITIVE
/// the long pays the short, so a long is charged and a short is credited; a negative rate
/// reverses both. Accrues against the bar's close, so it tracks the mark rather than the entry.
/// Default 0. Irrelevant to spot and to dated futures; set it only for perps.
/// </param>
/// <param name="FundingIntervalHours">
/// How often funding settles, in hours. Boundaries are absolute UTC wall-clock times, not an
/// offset from the entry — 8.0 means 00:00, 08:00 and 16:00 UTC, which is what the major perp
/// venues use — so a position pays for the settlements it is actually open across. A daily bar
/// crosses three of them; an hourly bar crosses one in three. Ignored when
/// <see cref="FundingRatePerInterval"/> is 0.
/// </param>
/// <param name="BorrowRateAnnual">
/// Cost of borrowing the asset to hold a SHORT, as an annualised decimal rate (0.05 = 5%/yr).
/// Accrues on calendar time against the bar's close, not on bar count, so the charge on a
/// position held over a weekend is the same whether the data is hourly or daily. Charged to
/// shorts only; a long is assumed cash-funded, which is why this is not a symmetric carry.
/// Default 0. This is the term whose absence most distorted this repo's research corpus: it
/// grows with hold time, so it is worst exactly on the swing strategies the catalogue favours.
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
    bool AllowReverseOnSignal = true,
    double SpreadPercent = 0.0,
    double FundingRatePerInterval = 0.0,
    double FundingIntervalHours = 8.0,
    double BorrowRateAnnual = 0.0
);
