using System.IO.Compression;
using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests;

/// <summary>
/// The GDELT attention recorder.
///
/// <para>
/// Unlike the crypto universe archive, this one is <b>recoverable</b> — GDELT re-serves its whole
/// two-year window on every call, so a gap today is backfilled by tomorrow's run. That changes what
/// is worth testing. Survivorship is not the risk here; two other things are:
/// </para>
/// <list type="number">
///   <item><b>The window rolls.</b> Two years is all there will ever be, so a theme not recorded
///         for two years is gone for good. Gaps must therefore be visible, not merely survivable —
///         silence about a hole is how a hole becomes permanent.</item>
///   <item><b>The series is normalised, so it can be restated.</b> Each value is a share of the
///         whole news firehose, and that denominator grows as sources are added and reprocessed. A
///         number that can change after the fact is not point-in-time, and a study built on the
///         vendor's current history inherits a lookahead no control can remove.</item>
/// </list>
/// </summary>
public class GdeltRecorderTests : IDisposable
{
    private readonly string _dir = TestTemp.NewPath("gdelt-");

    public GdeltRecorderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteRun(string runDate, params (string Theme, string Date, double V)[] rows)
    {
        string path = Path.Combine(_dir, $"gdelt_{runDate}.jsonl.gz");
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        using var w = new StreamWriter(gz);
        foreach (var r in rows)
            w.WriteLine($$"""{"d":"{{r.Date}}","t":"{{r.Theme}}","v":{{r.V}}}""");
        return path;
    }

    // ── Round-trip ──────────────────────────────────────────────────────────────

    [Fact]
    public void ARecordedRunReadsBackExactly()
    {
        WriteRun("2026-08-02", ("bitcoin", "2026-08-01", 0.42), ("gold", "2026-08-01", 0.11));

        var rows = GdeltRecorderCommand.LoadRun(Path.Combine(_dir, "gdelt_2026-08-02.jsonl.gz"));

        Assert.Equal(2, rows.Count);
        Assert.Equal(0.42, rows.Single(r => r.Theme == "bitcoin").Value, 6);
        Assert.Equal("2026-08-01", rows[0].Date);
    }

    /// <summary>One corrupt line must cost one row, never the run.</summary>
    [Fact]
    public void OneCorruptLineDoesNotLoseTheRun()
    {
        string path = Path.Combine(_dir, "gdelt_2026-08-02.jsonl.gz");
        using (var fs = File.Create(path))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        using (var w = new StreamWriter(gz))
        {
            w.WriteLine("""{"d":"2026-08-01","t":"bitcoin","v":1}""");
            w.WriteLine("{ not json");
            w.WriteLine("");
            w.WriteLine("""{"d":"2026-08-01","t":"gold","v":2}""");
        }

        Assert.Equal(2, GdeltRecorderCommand.LoadRun(path).Count);
    }

    [Theory]
    [InlineData("gdelt_2026-08-02.jsonl.gz", "2026-08-02")]
    [InlineData("gdelt_2025-12-31.jsonl.gz", "2025-12-31")]
    public void TheRunDateComesFromTheFilename(string name, string expected)
        => Assert.Equal(expected, GdeltRecorderCommand.RunDate(name));

    // ── The theme list ──────────────────────────────────────────────────────────

    /// <summary>
    /// Keys must be unique and stable. Two themes sharing a key would silently overwrite each other
    /// in every per-theme comparison, and the archive would look complete while holding half the
    /// data it claims.
    /// </summary>
    [Fact]
    public void ThemeKeysAreUnique()
    {
        var keys = GdeltRecorderCommand.Themes.Select(t => t.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>
    /// No theme may carry an empty query. A blank query returns the entire firehose, which would be
    /// recorded as that theme's attention series and would look perfectly plausible — a constant
    /// near 100% that nobody would question until it was used in a study.
    /// </summary>
    [Fact]
    public void EveryThemeHasAQuery()
    {
        foreach (var (key, query) in GdeltRecorderCommand.Themes)
        {
            Assert.False(string.IsNullOrWhiteSpace(key));
            Assert.False(string.IsNullOrWhiteSpace(query), $"theme '{key}' has no query");
        }
    }

    // ── Status ──────────────────────────────────────────────────────────────────

    [Fact]
    public void StatusOnAnEmptyDirectoryReportsRatherThanThrows()
        => Assert.Equal(1, GdeltRecorderCommand.Status(_dir));

    [Fact]
    public void StatusOnASingleRunSaysTheRestatementCheckIsNotYetPossible()
    {
        WriteRun("2026-08-02", ("bitcoin", "2026-08-01", 0.42));

        Assert.Equal(0, GdeltRecorderCommand.Status(_dir));
    }

    /// <summary>
    /// The measurement that decides whether recording forward was necessary or merely tidy: a
    /// (theme, date) present in two runs should carry the same value, because the past does not
    /// change. When it does, the vendor's current history is not what was observable at the time.
    /// </summary>
    [Fact]
    public void TwoRunsCanBeComparedForRestatement()
    {
        WriteRun("2026-08-02", ("bitcoin", "2026-08-01", 0.40), ("gold", "2026-08-01", 0.10));
        WriteRun("2026-08-09", ("bitcoin", "2026-08-01", 0.55), ("gold", "2026-08-01", 0.10));

        // Status prints the comparison; the property under test is that it runs and finds both runs.
        Assert.Equal(0, GdeltRecorderCommand.Status(_dir));

        var first = GdeltRecorderCommand.LoadRun(Path.Combine(_dir, "gdelt_2026-08-02.jsonl.gz"));
        var later = GdeltRecorderCommand.LoadRun(Path.Combine(_dir, "gdelt_2026-08-09.jsonl.gz"));

        double before = first.Single(r => r.Theme == "bitcoin").Value;
        double after = later.Single(r => r.Theme == "bitcoin").Value;

        Assert.NotEqual(before, after);   // the restatement this check exists to surface
        Assert.Equal(first.Single(r => r.Theme == "gold").Value,
                     later.Single(r => r.Theme == "gold").Value, 6);
    }

    /// <summary>
    /// The archive path must anchor to the repository root, not the working directory — the same
    /// rule as the universe archive, and for the same reason: running from two directories would
    /// maintain two archives, each with holes where the other has data.
    /// </summary>
    [Fact]
    public void TheArchivePathAnchorsToTheRepositoryRoot()
    {
        string a = UniverseRecorderCommand.Anchor("gdelt-archive");

        Assert.True(Path.IsPathRooted(a), $"'{a}' is still relative");
        Assert.EndsWith("gdelt-archive", a);
    }
}
