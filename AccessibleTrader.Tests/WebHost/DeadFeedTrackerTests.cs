using System.Text.RegularExpressions;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>The dead-feed rule, and the guard that keeps it in one place.</b>
///
/// <para>
/// "Three consecutive failed polls, then report once, reset on recovery" was implemented twice —
/// once in <c>LocalBackgroundMonitor</c> and once in <c>HostedAlertMonitor</c> — with its own
/// counter, its own reported-set and its own threshold constant on each side. Both copies were
/// correct, which is the point: a duplicated rule is a rule that can be half-fixed, and this repo
/// has the receipts. <c>LevelPolarity</c> exists because support-versus-resistance was got wrong
/// twice in three weeks and fixed only where somebody noticed, and the 2026-08-29 mutation
/// campaign's N08/N09 survived because one guard had been copied to four sites and the test knew
/// about one of them.
/// </para>
///
/// <para>
/// The cases below drive <see cref="DeadFeedTracker{TKey}"/> directly; the two monitors' own
/// suites still prove that each one SAYS the right thing on the right channel, because that half
/// deliberately stayed at the call sites.
/// </para>
/// </summary>
public class DeadFeedTrackerTests
{
    private static DeadFeedTracker<string> Tracker() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The threshold is a real escalation. The constant is deliberately NOT referenced: reading
    /// it back would make this test agree with any value it took, which is exactly the shape
    /// that leaves a bound untested.
    /// </summary>
    [Fact]
    public void Nothing_is_reported_until_the_third_consecutive_failure()
    {
        var t = Tracker();

        Assert.Null(t.NoteFailure("BTC/USD"));
        Assert.Null(t.NoteFailure("BTC/USD"));
        Assert.Equal(3, t.NoteFailure("BTC/USD"));
    }

    /// <summary>
    /// And it is reported once. A warning repeated every poll for the rest of the session
    /// trains a user to ignore it, which is the same outcome as never sending it.
    /// </summary>
    [Fact]
    public void A_reported_feed_is_not_reported_again_however_long_it_stays_dead()
    {
        var t = Tracker();

        for (int i = 0; i < 2; i++) Assert.Null(t.NoteFailure("BTC/USD"));
        Assert.NotNull(t.NoteFailure("BTC/USD"));

        for (int i = 0; i < 50; i++) Assert.Null(t.NoteFailure("BTC/USD"));

        // The count keeps climbing even though nothing is said — the latch is on the REPORT,
        // not on the counting, so a recovery still knows how long the outage ran.
        Assert.Equal(53, t.FailureCount("BTC/USD"));
    }

    /// <summary>Consecutive means consecutive: a success in the middle erases the run.</summary>
    [Fact]
    public void A_success_resets_the_count_so_scattered_failures_never_accumulate()
    {
        var t = Tracker();

        t.NoteFailure("BTC/USD");
        t.NoteFailure("BTC/USD");
        t.NoteRecovery("BTC/USD");
        Assert.Equal(0, t.FailureCount("BTC/USD"));

        Assert.Null(t.NoteFailure("BTC/USD"));
        Assert.Null(t.NoteFailure("BTC/USD"));

        // The third after the reset does report, which proves the two nulls above are the reset
        // working rather than the escalation being broken outright.
        Assert.NotNull(t.NoteFailure("BTC/USD"));
    }

    /// <summary>
    /// Recovery is news only if the failure was. A caller that announced "alerts on this symbol
    /// are not being watched" owes the user the retraction; a caller that said nothing must not
    /// announce a recovery from a failure nobody heard about.
    /// </summary>
    [Fact]
    public void Recovery_reports_only_when_the_failure_had_been_reported()
    {
        var t = Tracker();

        Assert.False(t.NoteRecovery("BTC/USD"));     // healthy feed, ordinary poll
        t.NoteFailure("BTC/USD");
        Assert.False(t.NoteRecovery("BTC/USD"));     // one blip, never reported

        for (int i = 0; i < 3; i++) t.NoteFailure("BTC/USD");
        Assert.True(t.NoteRecovery("BTC/USD"));      // reported dead, now back
        Assert.False(t.NoteRecovery("BTC/USD"));     // and it is said once
    }

    /// <summary>
    /// After a recovery the feed can go dead again and be reported again. The latch clears with
    /// the count — a second outage an hour later is a second piece of news, not a repeat.
    /// </summary>
    [Fact]
    public void A_second_outage_after_a_recovery_is_reported_again()
    {
        var t = Tracker();

        for (int i = 0; i < 3; i++) t.NoteFailure("BTC/USD");
        Assert.True(t.NoteRecovery("BTC/USD"));

        Assert.Null(t.NoteFailure("BTC/USD"));
        Assert.Null(t.NoteFailure("BTC/USD"));
        Assert.Equal(3, t.NoteFailure("BTC/USD"));
    }

    /// <summary>
    /// Keys are independent, and this is the half the hosted monitor needs: two users can watch
    /// one symbol through different credentials, so one user's key expiring is not the other's
    /// feed going down and telling them both is a false alarm for one of them.
    /// </summary>
    [Fact]
    public void Counts_never_pool_across_keys()
    {
        var t = new DeadFeedTracker<(string User, string Symbol)>();

        Assert.Null(t.NoteFailure(("user-a", "BTC/USD")));
        Assert.Null(t.NoteFailure(("user-b", "BTC/USD")));
        Assert.Null(t.NoteFailure(("user-a", "ETH/USD")));

        Assert.Equal(1, t.FailureCount(("user-a", "BTC/USD")));
    }

    /// <summary>The comparer the caller supplies is honoured — the local monitor keys on symbol
    /// case-insensitively, because a provider that answers "btc/usd" today and "BTC/USD"
    /// tomorrow would otherwise never accumulate three of anything.</summary>
    [Fact]
    public void The_supplied_comparer_decides_what_counts_as_the_same_feed()
    {
        var insensitive = Tracker();
        insensitive.NoteFailure("BTC/USD");
        insensitive.NoteFailure("btc/usd");
        Assert.Equal(2, insensitive.FailureCount("BTC/USD"));

        var ordinal = new DeadFeedTracker<string>();
        ordinal.NoteFailure("BTC/USD");
        ordinal.NoteFailure("btc/usd");
        Assert.Equal(1, ordinal.FailureCount("BTC/USD"));
    }

    // ── The guard: nobody implements this rule a third time ──────────────────

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static readonly string ChokepointFile = nameof(DeadFeedTracker<string>) + ".cs";

    /// <summary>Somebody counting consecutive feed failures on their own again.</summary>
    private static readonly Regex OwnCounter = new(
        @"_consecutive\w*[Ff]ail|const\s+int\s+\w*FailuresBefore\w*",
        RegexOptions.Compiled);

    /// <summary>The claim this rule exists to make, in words the user hears or reads.</summary>
    private static readonly Regex SaysAFeedIsDead = new(
        @"""[^""]*Alert monitoring stopped", RegexOptions.Compiled);

    private static IEnumerable<string> WebHostSources(string root)
    {
        var dir = Path.Combine(root, "AccessibleTrader.WebHost");
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && Path.GetFileName(f) != ChokepointFile)
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    /// <summary>
    /// The path check. Banning the known duplication shape alone would leave a third variant
    /// free, so anything that tells the user a feed has died has to be shown ASKING the
    /// chokepoint, not merely shown not counting for itself.
    /// </summary>
    [Fact]
    public void EveryDeadFeedClaimGoesThroughTheTracker()
    {
        string root = RepoRoot();
        var announcers = new List<string>();
        var missing = new List<string>();

        foreach (var file in WebHostSources(root))
        {
            string text = File.ReadAllText(file);
            if (!SaysAFeedIsDead.IsMatch(text)) continue;

            string rel = Path.GetRelativePath(root, file);
            announcers.Add(rel);
            if (!text.Contains(nameof(DeadFeedTracker<string>), StringComparison.Ordinal))
                missing.Add(rel);
        }

        // Vacuity floor: a rename or a reworded message could empty this sweep, and an empty
        // sweep passes silently. Both known announcers must still be found.
        Assert.Contains(announcers, a => a.EndsWith("LocalBackgroundMonitor.cs", StringComparison.Ordinal));
        Assert.Contains(announcers, a => a.EndsWith("HostedAlertMonitor.cs", StringComparison.Ordinal));

        Assert.True(missing.Count == 0,
            "These files tell the user a feed has stopped being watched without asking "
            + "DeadFeedTracker when that is true. Route the escalation through the tracker — the "
            + "message and the channel stay yours, the counting does not:\n"
            + string.Join("\n", missing));
    }

    /// <summary>And the shape that produced the duplication cannot come back.</summary>
    [Fact]
    public void NoWebHostFileCountsConsecutiveFeedFailuresForItself()
    {
        string root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in WebHostSources(root))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                // Strip a trailing line comment so the history recorded in the very files it was
                // fixed in cannot trip its own guard.
                int c = lines[i].IndexOf("//", StringComparison.Ordinal);
                string code = c < 0 ? lines[i] : lines[i][..c];
                if (OwnCounter.IsMatch(code))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1} — {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A second copy of the dead-feed escalation is growing. Use DeadFeedTracker<TKey>:\n"
            + string.Join("\n", offenders));
    }
}
