using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Every out-of-band speech backend AWAITS its interrupt cancel before starting the phrase.
///
/// <para><b>The race.</b> An interrupting utterance on the server-side backends is two processes:
/// <c>spd-say -S</c> to stop whatever is speaking, then the phrase. Started without waiting, the
/// cancel can be scheduled AFTER the phrase and clip the very utterance it was meant to clear the
/// way for — the interrupt eating its own words. <c>SpeakViaSpdSay</c> has awaited it since it was
/// written and its comment names this race exactly; <c>SpeakViaOrca</c> was added later
/// (04b49f1f, 2026-05-16) with the older fire-and-forget shape and kept it for four months.</para>
///
/// <para><b>Why this is a source guard and says so.</b> Observing the outcome needs a running
/// speech-dispatcher, an Orca on the session bus, and a race that is by definition intermittent —
/// none of which exists in CI. What CAN be pinned is the property that made the two paths
/// disagree: one of them called the non-waiting helper. That is exactly the shape of the defect
/// and it is what a future edit would reintroduce.</para>
/// </summary>
public sealed class SpeechCancelIsAwaitedTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} is gone from WebHostSpeechManager — re-aim this guard.");
        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"Could not find the end of {signature}.");
        return "";
    }

    [Theory]
    [InlineData("private void SpeakViaOrca")]
    [InlineData("private void SpeakViaSpdSay")]
    public void The_interrupt_cancel_is_waited_for(string signature)
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(),
            "AccessibleTrader.WebHost", "Services", "WebHostSpeechManager.cs"));
        string body = MethodBody(src, signature);

        // Comments stripped: both methods explain the race in prose that names the wrong call.
        string code = Regex.Replace(body, @"(?m)^\s*//.*$", "");

        Assert.True(code.Contains("RunSpdSayToCompletion(\"-S\")", StringComparison.Ordinal),
            $"{signature} does not await its cancel. Without the wait, spd-say -S can be scheduled "
            + "after the phrase it precedes and clip it — the user presses a key and hears the "
            + "front of a sentence, or nothing.");

        Assert.False(code.Contains("StartSpdSay(\"-S\")", StringComparison.Ordinal),
            $"{signature} starts the cancel without waiting. That is the exact shape the awaited "
            + "path was written to replace; see SpeakViaSpdSay's own comment.");
    }
}
