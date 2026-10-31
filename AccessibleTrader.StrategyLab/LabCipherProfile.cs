using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Which candles every lab command computes the Ciphers on (<c>--cipher-candles ha|candles</c>),
/// set in the one place every command goes through — the <see cref="IIndicatorEngine"/> the lab
/// host hands out — so no command can miss it and silently compare candles against candles.
///
/// <para>
/// <c>--cipherb old</c> selected the Cipher B every lab result before 2026-10-17 was computed on
/// (<c>WTSignalPeriod = 4</c>, <c>MoneyFlowMode = Legacy</c>, <c>BuyDotMode = Gated</c>; verified
/// then to print line for line what the pre-refit lab printed). The two modes were removed from
/// Cipher B on 2026-10-31 (Cody: "remove all of that old code from cipher b"), so it is refused,
/// as <c>--cipherb-gold</c> has been since the gold dot went on 2026-10-22: a run asked for the old
/// indicator would otherwise compare today's against itself and say so in no way. <c>--cipherb
/// new</c> is still accepted and changes nothing. Old results stay reproducible from the commits
/// that recorded them.
/// </para>
/// </summary>
public static class LabCipherProfile
{
    /// <summary>The old/new Cipher B switch: <c>new</c> is accepted, <c>old</c> refused since 2026-10-31.</summary>
    public const string Flag = "--cipherb";
    /// <summary>The gold dot's switch, refused since Cipher B's gold dot was removed (2026-10-22): a
    /// run asked to compare gold rules would otherwise compare nothing and say so in no way.</summary>
    public const string GoldFlag = "--cipherb-gold";
    /// <summary>Exploratory (docs/EXACT_CIPHERS_RERUN_PREREG.md section 6): <c>--cipher-candles ha</c>
    /// computes Cipher A, Cipher B and Cipher SR as a Heikin-Ashi chart computes them in the app.</summary>
    public const string CandlesFlag = "--cipher-candles";

    /// <summary>The indicators <c>--cipher-candles ha</c> moves onto Heikin-Ashi. Everything else —
    /// ATR, the higher-timeframe context, entries, stops and fills — stays on the real bars.</summary>
    public static IReadOnlyList<string> HeikinAshiCodes { get; } = new[] { "CIPHER_A", "CIPHER_B", "CIPHER_SR" };

    /// <summary>True when lab commands compute the Ciphers on Heikin-Ashi candles, by stamping
    /// <see cref="IndicatorCandles.HeikinAshiKey"/> — the same parameter a Heikin-Ashi chart gives
    /// them, so each one computes exactly as it does there (Cipher B builds its own Heikin-Ashi
    /// from the key; the indicator service converts the bars for the others).</summary>
    public static bool CiphersOnHeikinAshi { get; set; }

    /// <summary>True when nothing was switched: the Ciphers on real candles.</summary>
    public static bool IsDefault => !CiphersOnHeikinAshi;

    public static string Describe() =>
        "Cipher B: Market Cipher B (no old profile since 2026-10-31; no gold dot since 2026-10-22)"
        + (CiphersOnHeikinAshi
            ? "\nCipher candles: HEIKIN-ASHI (exploratory) — Cipher A, Cipher B and Cipher SR computed as on a Heikin-Ashi chart; entries, stops, ATR and fills on the real bars"
            : "");

    /// <summary>
    /// Removes <c>--cipherb new</c> and <c>--cipher-candles ha|candles</c> from the argument list (so no
    /// command's own parser sees an unknown flag) and sets the profile. Any other value is an error
    /// rather than a silent default — a typo here would otherwise run the comparison against itself.
    /// <c>--cipherb old</c> and <c>--cipherb-gold</c> are errors: there is no old Cipher B or gold dot
    /// to switch to.
    /// </summary>
    public static string[] Consume(string[] args)
    {
        var rest = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], GoldFlag, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{GoldFlag} was removed with Cipher B's gold dot on 2026-10-22; there is no gold dot to switch");
            if (string.Equals(args[i], CandlesFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"{CandlesFlag} needs a value: ha or candles");
                var c = args[++i];
                CiphersOnHeikinAshi = c.ToLowerInvariant() switch
                {
                    "ha" => true,
                    "candles" => false,
                    _ => throw new ArgumentException($"{CandlesFlag} must be 'ha' or 'candles', not '{c}'"),
                };
                continue;
            }
            if (!string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase)) { rest.Add(args[i]); continue; }
            const string flag = Flag;
            if (i + 1 >= args.Length) throw new ArgumentException($"{flag} needs a value: old or new");
            var v = args[++i];
            if (string.Equals(v, "old", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{flag} old was removed with Cipher B's old modes on 2026-10-31; there is no old Cipher B to switch to");
            if (!string.Equals(v, "new", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{flag} must be 'new' (the only Cipher B since 2026-10-31), not '{v}'");
        }
        return rest.ToArray();
    }

    /// <summary>The parameters the engine should hand an indicator: the caller's, plus the Heikin-Ashi
    /// key for a Cipher under <c>--cipher-candles ha</c> unless the caller set it. A new dictionary
    /// when anything is added; the caller's is untouched.</summary>
    public static Dictionary<string, object> Apply(string code, Dictionary<string, object> parameters)
    {
        bool ha = CiphersOnHeikinAshi && HeikinAshiCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
        if (!ha) return parameters;
        var merged = new Dictionary<string, object>(parameters);
        merged.TryAdd(IndicatorCandles.HeikinAshiKey, 1.0);
        return merged;
    }

    /// <summary>Back to the defaults (tests).</summary>
    public static void Reset() { CiphersOnHeikinAshi = false; }
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
