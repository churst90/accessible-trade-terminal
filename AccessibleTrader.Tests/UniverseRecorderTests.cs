using System.IO.Compression;
using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests;

/// <summary>
/// The forward crypto-universe recorder.
///
/// <para>
/// This archive exists to defeat survivorship bias, which is the one bias that <b>cannot be
/// corrected after the fact</b>. Either the dead assets were recorded while they were alive, or the
/// question is permanently unanswerable — there is no control, reweighting or adjustment that
/// recovers them. So the properties worth testing are not about parsing: they are about the archive
/// being trustworthy as a record. It must not be silently overwritten, must not be written empty,
/// must not fragment across working directories, and must read back exactly what was written.
/// </para>
/// </summary>
public class UniverseRecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uni-" + Guid.NewGuid().ToString("N"));

    public UniverseRecorderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteSnapshot(string date, params (string Id, string Sym, int Rank, double Mcap)[] rows)
    {
        string path = Path.Combine(_dir, $"crypto_{date}.jsonl.gz");
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        using var w = new StreamWriter(gz);
        foreach (var r in rows)
            w.WriteLine($$"""{"d":"{{date}}","id":"{{r.Id}}","s":"{{r.Sym}}","n":"{{r.Id}}","r":{{r.Rank}},"mc":{{r.Mcap}}}""");
        return path;
    }

    // ── Reading back what was written ──────────────────────────────────────────

    [Fact]
    public void AGzippedSnapshotReadsBackExactly()
    {
        WriteSnapshot("2026-08-01", ("bitcoin", "btc", 1, 1e12), ("ethereum", "eth", 2, 2e11));

        var loaded = UniverseRecorderCommand.Load(Path.Combine(_dir, "crypto_2026-08-01.jsonl.gz"));

        Assert.Equal(2, loaded.Count);
        Assert.Equal("btc", loaded["bitcoin"].Symbol);
        Assert.Equal(1, loaded["bitcoin"].Rank);
        Assert.Equal(1e12, loaded["bitcoin"].MarketCap);
        Assert.Equal("2026-08-01", loaded["bitcoin"].Date);
    }

    /// <summary>
    /// The first snapshots were written uncompressed. An archive that stops being readable when its
    /// storage format changes is not an archive, so both are accepted forever.
    /// </summary>
    [Fact]
    public void PlainAndGzippedSnapshotsAreBothReadable()
    {
        WriteSnapshot("2026-08-01", ("bitcoin", "btc", 1, 1e12));
        File.WriteAllText(Path.Combine(_dir, "crypto_2026-07-31.jsonl"),
            """{"d":"2026-07-31","id":"bitcoin","s":"btc","n":"Bitcoin","r":1,"mc":900000000000}""" + "\n");

        Assert.Single(UniverseRecorderCommand.Load(Path.Combine(_dir, "crypto_2026-07-31.jsonl")));
        Assert.Single(UniverseRecorderCommand.Load(Path.Combine(_dir, "crypto_2026-08-01.jsonl.gz")));
        Assert.Equal(2, UniverseRecorderCommand.Snapshots(_dir).Count);
    }

    /// <summary>
    /// One corrupt line must cost one row, never the day. A day is unrepeatable; a row is not.
    /// </summary>
    [Fact]
    public void OneCorruptLineDoesNotLoseTheWholeDay()
    {
        string path = Path.Combine(_dir, "crypto_2026-08-01.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"d":"2026-08-01","id":"bitcoin","s":"btc","r":1,"mc":1}""",
            "{ this is not json",
            "",
            """{"d":"2026-08-01","id":"ethereum","s":"eth","r":2,"mc":2}""",
        });

        var loaded = UniverseRecorderCommand.Load(path);
        Assert.Equal(2, loaded.Count);
    }

    // ── Ordering, which the delta depends on ───────────────────────────────────

    /// <summary>
    /// Snapshots must sort chronologically by NAME. The delta compares each day against the one
    /// before it, so a sort that put 2026-08-10 before 2026-08-02 would report the entire universe
    /// as arriving and leaving on alternate days.
    /// </summary>
    [Fact]
    public void SnapshotsSortChronologically()
    {
        foreach (var d in new[] { "2026-08-10", "2026-08-02", "2026-12-01", "2026-08-09" })
            WriteSnapshot(d, ("bitcoin", "btc", 1, 1));

        var order = UniverseRecorderCommand.Snapshots(_dir)
            .Select(UniverseRecorderCommand.DateOf).ToList();

        Assert.Equal(new[] { "2026-08-02", "2026-08-09", "2026-08-10", "2026-12-01" }, order);
    }

    [Theory]
    [InlineData("crypto_2026-08-02.jsonl.gz", "2026-08-02")]
    [InlineData("crypto_2026-12-31.jsonl", "2026-12-31")]
    public void TheDateComesFromTheFilename(string name, string expected)
        => Assert.Equal(expected, UniverseRecorderCommand.DateOf(name));

    // ── The properties that make it a record rather than a cache ───────────────

    /// <summary>
    /// Re-running on a day already recorded must NOT overwrite it. A research record that a later
    /// run can silently replace is not a record — and the replacement would be a different sample of
    /// a moving market, wearing the same date.
    /// </summary>
    [Fact]
    public async Task RerunningOnTheSameDayLeavesTheExistingSnapshotUntouched()
    {
        string path = WriteSnapshot(DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ("sentinel", "snt", 1, 12345));
        var before = File.ReadAllBytes(path);

        // pages:0 guarantees no network call, so this tests the guard and nothing else.
        int rc = await UniverseRecorderCommand.RunAsync(_dir, pages: 0, force: false);

        Assert.Equal(0, rc);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// A sweep that returns nothing must not be written. An empty file dated today would read, on
    /// the next run's delta, as every asset in the universe having been delisted overnight — the
    /// exact false finding this archive exists to prevent.
    /// </summary>
    [Fact]
    public async Task AnEmptySweepIsRefusedRatherThanWrittenAsAMassDelisting()
    {
        int rc = await UniverseRecorderCommand.RunAsync(_dir, pages: 0, force: true);

        Assert.Equal(2, rc);
        Assert.Empty(UniverseRecorderCommand.Snapshots(_dir));
    }

    /// <summary>
    /// A relative archive path must resolve to the same place regardless of the working directory.
    /// Running from the solution root one day and the lab directory the next would otherwise
    /// maintain two archives, each with holes where the other has data.
    /// </summary>
    [Fact]
    public void ARelativePathAnchorsToTheRepositoryRoot()
    {
        string a = UniverseRecorderCommand.Anchor("universe-archive");
        Assert.True(Path.IsPathRooted(a), $"'{a}' is still relative");
        Assert.EndsWith("universe-archive", a);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(a)!, "AccessibleTrader.slnx")),
            "the anchor did not land on the repository root");
    }

    [Fact]
    public void AnAbsolutePathIsLeftAlone()
    {
        string abs = Path.Combine(Path.GetTempPath(), "explicit-archive");
        Assert.Equal(abs, UniverseRecorderCommand.Anchor(abs));
    }

    [Fact]
    public void StatusOnAnEmptyDirectoryReportsRatherThanThrows()
        => Assert.Equal(1, UniverseRecorderCommand.Status(_dir));

    // ── The delta itself ───────────────────────────────────────────────────────

    /// <summary>
    /// The disappearance list is the survivorship record. This is the one computation in the file
    /// whose output cannot be reconstructed from any later data.
    /// </summary>
    [Fact]
    public void ComparingTwoDaysIdentifiesWhatDisappeared()
    {
        WriteSnapshot("2026-08-01",
            ("bitcoin", "btc", 1, 1e12), ("scamcoin", "scam", 400, 5e6), ("ethereum", "eth", 2, 2e11));
        WriteSnapshot("2026-08-02",
            ("bitcoin", "btc", 1, 1e12), ("ethereum", "eth", 2, 2e11), ("newthing", "new", 500, 4e6));

        var prev = UniverseRecorderCommand.Load(Path.Combine(_dir, "crypto_2026-08-01.jsonl.gz"));
        var now = UniverseRecorderCommand.Load(Path.Combine(_dir, "crypto_2026-08-02.jsonl.gz"));

        Assert.Equal(new[] { "scamcoin" }, prev.Keys.Except(now.Keys).ToArray());
        Assert.Equal(new[] { "newthing" }, now.Keys.Except(prev.Keys).ToArray());
    }
}
