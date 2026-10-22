using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Which Cipher B every lab command computes: the current one (Market Cipher B, refitted
/// 2026-10-17) or the one every lab result recorded before 2026-10-17 was computed on.
///
/// <para>
/// WHY A LAB-WIDE SWITCH AND NOT A PER-COMMAND FLAG. A re-run that is meant to say "the indicator
/// changed and this is how much the verdict moved" has to hold everything except the indicator
/// fixed: same snapshots, same harness, same seeds. The Cipher B refit kept its old behaviour
/// behind three parameters (<c>WTSignalPeriod = 4</c>, <c>MoneyFlowMode = Legacy</c>,
/// <c>BuyDotMode = Gated</c>; a fourth, <c>GoldDotMode = Confluence</c>, went with the gold dot on
/// 2026-10-22, so no profile reproduces a gold-dot result any more), but the lab computes Cipher B in at
/// least six places (gate, confluence, asset-profile, ml-export, the strategy workspace, ...), each
/// building its own parameter dictionary. Threading a flag through each of them is six chances to
/// miss one, and a missed one silently compares NEW against NEW. So the switch sits in the one
/// place every one of them goes through — the <see cref="IIndicatorEngine"/> the lab host hands
/// out — and <c>--cipherb old</c> on any command line selects it.
/// </para>
///
/// <para>
/// Verified 2026-10-17: with <c>--cipherb old</c>, gate, confluence (1h/4h/1d/2d) and asset-profile
/// (1d/4h) print output identical line for line to the lab built from the commit before the refit
/// (aca73a1d) — so "old" is the pre-refit indicator, not an approximation of it.
/// </para>
///
/// <para>
/// A caller that sets one of the parameters explicitly (e.g. <c>--set CIPHER_B.WTSignalPeriod=5</c>)
/// keeps its value: the profile fills only what the caller left unset, so a deliberate research
/// override is never quietly replaced.
/// </para>
/// </summary>
public static class LabCipherProfile
{
    public const string Flag = "--cipherb";
    /// <summary>The gold dot's switch, refused since Cipher B's gold dot was removed (2026-10-22): a
    /// run asked to compare gold rules would otherwise compare nothing and say so in no way.</summary>
    public const string GoldFlag = "--cipherb-gold";

    /// <summary>True when lab commands compute the pre-2026-10-17 Cipher B (wave, money flow, buy dot).</summary>
    public static bool CipherBOld { get; set; }

    /// <summary>The parameters that reproduce Cipher B's wave, money flow and buy dot as they were
    /// before 2026-10-17 (see the "Older behaviour behind parameters" note on <see cref="CipherBProvider"/>).</summary>
    public static IReadOnlyDictionary<string, object> OldCipherBParameters { get; } = new Dictionary<string, object>
    {
        ["WTSignalPeriod"] = 4.0,
        ["MoneyFlowMode"] = "Legacy",
        ["BuyDotMode"] = "Gated",
    };

    /// <summary>True when nothing was switched: every command computes today's Cipher B.</summary>
    public static bool IsDefault => !CipherBOld;

    public static string Describe() =>
        (CipherBOld
            ? "Cipher B profile: OLD (pre-2026-10-17: WTSignalPeriod=4, MoneyFlowMode=Legacy, BuyDotMode=Gated)"
            : "Cipher B profile: NEW (2026-10-17 Market Cipher B defaults: WTSignalPeriod=3, MoneyFlowMode=MCB, BuyDotMode=MCB)")
        + "; no gold dot (removed 2026-10-22)";

    /// <summary>
    /// Removes <c>--cipherb old|new</c> from the argument list (so no command's own parser sees an
    /// unknown flag) and sets the profile. Any other value is an error rather than a silent default
    /// — a typo here would otherwise run the comparison against itself. <c>--cipherb-gold</c> is an
    /// error: there is no gold dot to switch.
    /// </summary>
    public static string[] Consume(string[] args)
    {
        var rest = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], GoldFlag, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{GoldFlag} was removed with Cipher B's gold dot on 2026-10-22; there is no gold dot to switch");
            if (!string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase)) { rest.Add(args[i]); continue; }
            const string flag = Flag;
            if (i + 1 >= args.Length) throw new ArgumentException($"{flag} needs a value: old or new");
            var v = args[++i];
            bool old = v.ToLowerInvariant() switch
            {
                "old" => true,
                "new" => false,
                _ => throw new ArgumentException($"{flag} must be 'old' or 'new', not '{v}'"),
            };
            CipherBOld = old;
        }
        return rest.ToArray();
    }

    /// <summary>The parameters the engine should hand Cipher B: the caller's, plus the old
    /// profile's for any the caller left unset. A new dictionary; the caller's is untouched.</summary>
    public static Dictionary<string, object> Apply(string code, Dictionary<string, object> parameters)
    {
        if (IsDefault || !string.Equals(code, "CIPHER_B", StringComparison.OrdinalIgnoreCase))
            return parameters;
        var merged = new Dictionary<string, object>(parameters);
        if (CipherBOld) foreach (var (k, v) in OldCipherBParameters) merged.TryAdd(k, v);
        return merged;
    }

    /// <summary>Back to the defaults (tests).</summary>
    public static void Reset() { CipherBOld = false; }
}

/// <summary>
/// The engine the lab host registers: the real <see cref="IndicatorEngine"/> with
/// <see cref="LabCipherProfile.Apply"/> in front of every calculation.
/// </summary>
public sealed class LabProfileIndicatorEngine : IIndicatorEngine
{
    private readonly IIndicatorEngine _inner;
    public LabProfileIndicatorEngine(IIndicatorEngine inner) { _inner = inner; }

    public Task<Dictionary<string, double[]>> CalculateAsync(string code, IReadOnlyList<Ohlcv> data,
        Dictionary<string, object> parameters, CancellationToken ct) =>
        _inner.CalculateAsync(code, data, LabCipherProfile.Apply(code, parameters), ct);

    public Task<Dictionary<string, double>> CalculateIncrementalAsync(string code, IReadOnlyList<Ohlcv> data,
        Dictionary<string, object> parameters, Dictionary<string, double[]> previousResults, CancellationToken ct) =>
        _inner.CalculateIncrementalAsync(code, data, LabCipherProfile.Apply(code, parameters), previousResults, ct);

    public IIndicatorProvider? GetProvider(string indicatorCode) => _inner.GetProvider(indicatorCode);

    public Task<(Dictionary<string, double[]> Results, IReadOnlyList<ZoneBandConfig> ZoneBands)>
        CalculateWithBandsAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters,
            CancellationToken ct) =>
        _inner.CalculateWithBandsAsync(code, data, LabCipherProfile.Apply(code, parameters), ct);
}
