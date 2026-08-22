namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Deterministic seed derivation for research controls.
///
/// <para>
/// <c>string.GetHashCode()</c> is randomised per process in .NET. Seeding a control with it makes
/// the control resample on every run — and a p-value that moves between runs is not a p-value.
/// This has bitten this lab before: the same bucket read -5.6 and then -1.8 on two consecutive
/// runs of the same code, and the fix was written four separate times as a private copy in
/// <c>ApproachCommand</c>, <c>WeeklyPersistenceCommand</c>, <c>RegimePersistenceCommand</c> and
/// <c>PyramidCommand</c> while six other seed sites kept the raw hash — including
/// <c>RespectCommand</c>'s surrogate test, whose comment claimed "seeded off the asset name so a
/// rerun reproduces exactly" directly above the line that made it untrue.
/// </para>
///
/// <para>
/// FNV-1a, fixed forever. The exact constants matter: changing them silently reseeds every
/// control in the lab and every stored verdict becomes unreproducible against fresh runs.
/// The result is masked to a non-negative <see cref="int"/> so call sites never need
/// <see cref="System.Math.Abs(int)"/> — which throws on <see cref="int.MinValue"/>, a crash a
/// hash-derived value can actually reach.
/// </para>
/// </summary>
public static class StableSeed
{
    /// <summary>A non-negative, process-independent seed for <paramref name="text"/>.</summary>
    public static int From(string text)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in text ?? "") { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }
}
