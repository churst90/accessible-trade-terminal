using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// <b>One announcing channel, and one kind of thing in it.</b> Two source-level contracts,
/// filed together because they are the two halves of one defect Cody reported: Shift+F1 spoke
/// "feedback, context summary".
///
/// <para><b>Half one — the message field carries prose.</b> Shift+F1 published
/// <c>FeedbackRequestEvent(FeedbackType.Info, "CONTEXT_SUMMARY")</c>: a machine token in the
/// field every other publisher fills with a sentence meant for a human.
/// <c>AccessibilityFeedbackCoordinator</c> recognised the token and swapped in the real summary,
/// which made it look safe — but it was not the only subscriber. <c>StatusBar</c> mirrors
/// <c>ev.Message</c> into the visible strip, so the terminal DISPLAYED "CONTEXT_SUMMARY" and,
/// while that strip was still a live region, SPOKE it. A sentinel in a shared field is only safe
/// while exactly one subscriber exists, and a bus is the wrong place to assume that. Shift+F1 is
/// now <c>ContextSummaryRequestEvent</c>.</para>
///
/// <para><b>Half two — a mirror of spoken text must not announce.</b> The status strip carried
/// <c>role="status" aria-live="polite"</c> holding the SAME sentence as the assertive speech
/// buffers in <c>MainLayout</c>. That is not redundancy; it is a duplicate-suppression trap.
/// Every screen reader drops a live-region message that repeats what it just queued, so whichever
/// copy arrived second was discarded — and when the polite copy arrived FIRST (measured on the
/// AT-SPI bus on 6 of 16 presses) the assertive copy purged it and was then itself dropped as a
/// duplicate of it, and the sentence was spoken neither time. The rule: a component that
/// subscribes to spoken feedback in order to show it may not also be a live region.</para>
/// </summary>
public sealed class SpokenFeedbackChannelTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles(params string[] projects)
    {
        foreach (string project in projects)
        {
            string root = Path.Combine(RepoRoot(), project);
            if (!Directory.Exists(root)) continue;
            foreach (string file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                string ext = Path.GetExtension(file);
                if (ext is ".cs" or ".razor") yield return file;
            }
        }
    }

    /// <summary>
    /// No <see cref="AccessibleTrader.Core.Models.FeedbackRequestEvent"/> may be published with a
    /// machine token in its message. The message is spoken AND printed verbatim by subscribers
    /// that cannot be expected to recognise sentinels.
    ///
    /// <para>The rule is on the SHAPE, not on the one word: a message reworded to
    /// <c>"SUMMARY_REQUEST"</c> or <c>"__ctx"</c> is the identical defect, and a check for the
    /// literal "CONTEXT_SUMMARY" would pass it. What is pinned is that a literal message reads as
    /// a sentence — it must contain a lower-case letter and no SCREAMING_SNAKE run.</para>
    /// </summary>
    [Fact]
    public void A_feedback_message_is_prose_never_a_sentinel()
    {
        var offenders = new List<string>();

        foreach (string file in SourceFiles(
            "AccessibleTrader.Core", "AccessibleTrader.BlazorClient.Components",
            "AccessibleTrader.BlazorClient", "AccessibleTrader.WebHost", "AccessibleTrader.Maui"))
        {
            // Comments stripped, or the doc comments explaining this very regression would be
            // read as publishers and this guard would fail on its own explanation.
            string code = ModalContractScanTests.CodeOnly(File.ReadAllText(file));

            foreach (Match m in Regex.Matches(code,
                @"FeedbackRequestEvent\s*\(\s*(?:type\s*:\s*)?FeedbackType\.\w+\s*,\s*""(?<msg>[^""]*)"""))
            {
                string msg = m.Groups["msg"].Value;
                if (msg.Length == 0) continue;

                bool screaming = Regex.IsMatch(msg, @"\b[A-Z][A-Z0-9]*_[A-Z0-9_]+\b");
                bool hasLower = msg.Any(char.IsLower);

                if (screaming || !hasLower)
                    offenders.Add($"{Path.GetFileName(file)}: \"{msg}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "A FeedbackRequestEvent is being published with a machine token where its message "
            + "belongs. That field is spoken by the screen reader AND printed into the status "
            + "strip verbatim — the coordinator's ability to recognise the token does not help "
            + "the other subscribers, which is how Shift+F1 came to say \"context summary\". "
            + "Give the command its own event record instead.\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Vacuity check for the scan above: the regex must actually be finding publishers. Without
    /// this a typo in the pattern reports a clean sweep of nothing.
    /// </summary>
    [Fact]
    public void The_feedback_message_scan_actually_finds_publishers()
    {
        int found = 0;
        foreach (string file in SourceFiles("AccessibleTrader.Core", "AccessibleTrader.BlazorClient.Components"))
        {
            string code = ModalContractScanTests.CodeOnly(File.ReadAllText(file));
            found += Regex.Matches(code,
                @"FeedbackRequestEvent\s*\(\s*(?:type\s*:\s*)?FeedbackType\.\w+\s*,\s*""[^""]*""").Count;
        }

        Assert.True(found >= 8,
            $"The FeedbackRequestEvent literal-message scan matched only {found} publishers. It is "
            + "measuring its own regex, not the codebase.");
    }

    /// <summary>
    /// A component that subscribes to <c>FeedbackRequestEvent</c> in order to SHOW the message
    /// must not also be a live region. Two announcers for one sentence is what silenced it.
    /// <c>MainLayout</c> — the announcing channel itself — does not subscribe to the event; it
    /// receives phrases through <c>ISpeechManager.OnSpeak</c>, so it is not in this set by
    /// construction rather than by exemption.
    /// </summary>
    [Fact]
    public void A_component_that_mirrors_spoken_feedback_is_not_a_live_region()
    {
        var offenders = new List<string>();
        var scanned = new List<string>();

        foreach (string file in SourceFiles("AccessibleTrader.BlazorClient.Components"))
        {
            if (Path.GetExtension(file) != ".razor") continue;
            string code = ModalContractScanTests.CodeOnly(File.ReadAllText(file));
            if (!code.Contains("Subscribe<FeedbackRequestEvent>")) continue;

            scanned.Add(Path.GetFileName(file));

            var live = Regex.Match(code, @"aria-live\s*=|role\s*=\s*""(status|alert|log)""");
            if (live.Success)
                offenders.Add($"{Path.GetFileName(file)}: {live.Value}");
        }

        Assert.True(scanned.Count > 0,
            "Nothing in the component library subscribes to FeedbackRequestEvent any more, so this "
            + "guard is watching an empty set. Re-aim it at whatever mirrors spoken feedback now.");

        Assert.True(offenders.Count == 0,
            "A component that mirrors spoken feedback into the page is ALSO declaring itself a "
            + "live region, so one sentence has two announcers. Screen readers suppress the "
            + "duplicate — and when the second announcer wins the race the first one is purged "
            + "first, so the user hears NOTHING. The speech double-buffer in MainLayout is the "
            + "announcing channel; a visual mirror is reached by landmark and browse-mode "
            + "navigation and needs no aria-live.\n  "
            + string.Join("\n  ", offenders));
    }
}
