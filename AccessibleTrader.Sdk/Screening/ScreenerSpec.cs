using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Sdk.Screening;

/// <summary>
/// A saved screen: a condition tree evaluated against the most recent closed bar of every
/// symbol on a watchlist.
///
/// The condition tree is the SAME <see cref="ConditionNode"/> type the Strategy Composer
/// builds, evaluated by the same <c>IConditionEvaluator</c>. That reuse is the whole point —
/// a screen is a strategy's entry condition asked across many symbols at one instant instead
/// of across many bars of one symbol, so every indicator, operator and signal descriptor the
/// composer already understands works in the screener on day one.
/// </summary>
/// <param name="Id">Stable id for persistence.</param>
/// <param name="Name">User-facing name, spoken when results are announced.</param>
/// <param name="WatchlistId">Watchlist to screen. Null screens the provider's full symbol list.</param>
/// <param name="Timeframe">Bar timeframe to evaluate on, e.g. "1d".</param>
/// <param name="Root">Condition tree root. Null matches every symbol (a plain quote screen).</param>
/// <param name="BarCount">
/// Bars of history to fetch per symbol. Must comfortably exceed the warmup of the slowest
/// indicator referenced by <paramref name="Root"/> or leaves silently evaluate against NaN.
/// </param>
/// <param name="Columns">
/// Extra value columns to report per row, as <c>"{INDICATOR_CODE}.{component}"</c> signal ids.
/// These are reported whether or not the row matched, so a screen doubles as a dashboard.
/// </param>
public record ScreenerSpec(
    string Id,
    string Name,
    string? WatchlistId,
    string Timeframe = "1d",
    ConditionNode? Root = null,
    int BarCount = 500,
    IReadOnlyList<string>? Columns = null)
{
    public static ScreenerSpec Create(string name, string? watchlistId) =>
        new(Guid.NewGuid().ToString("N"), name, watchlistId);
}

/// <summary>Why a symbol produced no verdict. Reported rather than silently dropped.</summary>
public enum ScreenerRowStatus
{
    /// <summary>Evaluated cleanly.</summary>
    Evaluated,
    /// <summary>The provider returned fewer bars than the indicators need to warm up.</summary>
    InsufficientHistory,
    /// <summary>Fetch or indicator computation threw. See <see cref="ScreenerRow.Detail"/>.</summary>
    Failed
}

/// <summary>
/// One symbol's result. Non-matching and failed rows are retained deliberately: a screen that
/// silently drops the symbols it couldn't fetch reads as "nothing qualified" when the truth is
/// "we never looked", and that distinction matters when money is involved.
/// </summary>
public record ScreenerRow(
    WatchlistEntry Entry,
    ScreenerRowStatus Status,
    bool Matched,
    double Score,
    double MaxScore,
    double LastClose,
    double PercentChange,
    DateTime LastBarTime,
    IReadOnlyDictionary<string, double> Columns,
    string? Detail = null)
{
    /// <summary>Normalised confluence score in 0..1, or NaN when the screen has no scored leaves.</summary>
    public double ScoreFraction => MaxScore > 0 ? Score / MaxScore : double.NaN;
}

/// <summary>Outcome of one screener run, including the counts needed to narrate it honestly.</summary>
public record ScreenerRunResult(
    string SpecId,
    string SpecName,
    DateTime RunAt,
    IReadOnlyList<ScreenerRow> Rows)
{
    public int MatchCount
    {
        get
        {
            int n = 0;
            foreach (var r in Rows) if (r is { Status: ScreenerRowStatus.Evaluated, Matched: true }) n++;
            return n;
        }
    }

    public int EvaluatedCount
    {
        get
        {
            int n = 0;
            foreach (var r in Rows) if (r.Status == ScreenerRowStatus.Evaluated) n++;
            return n;
        }
    }

    public int FailedCount
    {
        get
        {
            int n = 0;
            foreach (var r in Rows) if (r.Status != ScreenerRowStatus.Evaluated) n++;
            return n;
        }
    }
}
