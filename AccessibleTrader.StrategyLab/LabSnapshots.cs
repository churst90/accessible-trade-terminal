namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Reading a snapshot filename. Snapshots are named <c>provider_SYMBOL_timeframe.json</c>, and the
/// provider prefix is a reliable proxy for asset class here because each provider in the archive
/// serves one kind of market.
///
/// <para>
/// This lived as six private <c>ClassOf</c> copies across the lab — but as *two different
/// functions* sharing one name: a two-way crypto/equities split and a five-way split that also
/// separates commodities and bonds and returns "skip" for anything unrecognised. Two commands
/// grouping their results by "asset class" could therefore mean different things by it, with
/// nothing in either file to say so. Both are kept, under names that say which is which.
/// </para>
/// </summary>
public static class LabSnapshots
{
    /// <summary>
    /// The two-way split: crypto, or everything else. Takes a path or a bare filename. Used by the
    /// commands whose question is only "does this behave like a 24/7 market or a session one".
    /// </summary>
    public static string CryptoOrEquities(string pathOrFileName)
    {
        string n = Path.GetFileName(pathOrFileName);
        return n.StartsWith("bitstamp") || n.StartsWith("mexc") ? "crypto" : "equities";
    }

    /// <summary>
    /// The five-way split: crypto, commodity, bond, equity, or <c>skip</c> for a provider this
    /// does not recognise. Returning "skip" rather than guessing is the point — a mislabelled
    /// asset class silently pollutes a cross-sectional comparison, and there is no way to notice
    /// it downstream.
    /// </summary>
    public static string AssetClass(string fileName)
    {
        string f = fileName.ToLowerInvariant();
        if (f.StartsWith("bitstamp_") || f.StartsWith("mexc_")) return "crypto";
        if (f.Contains("xau") || f.Contains("_gld_") || f.Contains("_slv_") || f.Contains("_uso_")) return "commod";
        if (f.Contains("_tlt_") || f.Contains("_ief_")) return "bond";
        if (f.StartsWith("twelvedata_") || f.StartsWith("yahoo_") || f.StartsWith("alpaca_")) return "equity";
        return "skip";
    }
}
