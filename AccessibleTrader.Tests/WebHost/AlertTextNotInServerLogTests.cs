using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The hosted server logged every fired alert's text at Information: "BTC/USD crossed above
/// 70,000" next to the user's id, in the log every operator reads. The text is the user's trading
/// intent. It may be logged at Debug (off on the server), never at Information or above.
/// </summary>
public sealed class AlertTextNotInServerLogTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "AccessibleTrader.Core")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // One log call, up to its closing ");", so arguments on later lines are seen.
    private static readonly Regex LogCall = new(@"_logger\.Log(?<level>\w+)\((?<args>[^;]*?)\);", RegexOptions.Singleline);

    [Fact]
    public void No_WebHost_log_above_Debug_carries_an_alerts_text()
    {
        string dir = Path.Combine(RepoRoot(), "AccessibleTrader.WebHost");
        Assert.True(File.Exists(Path.Combine(dir, "Services", "HostedAlertMonitor.cs")), "the scan is looking in the wrong place");

        var offenders = new List<string>();
        int debugWithText = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in LogCall.Matches(File.ReadAllText(file)))
            {
                if (!m.Groups["args"].Value.Contains("SpeechText")) continue;
                string level = m.Groups["level"].Value;
                if (level is "Debug" or "Trace") { debugWithText++; continue; }
                offenders.Add($"{Path.GetFileName(file)}: _logger.Log{level}(…SpeechText…)");
            }
        }

        Assert.Empty(offenders);
        // The text is still logged, at Debug: if this finds none the pattern has stopped matching.
        Assert.True(debugWithText > 0, "found no Debug log of the alert text; the scan matches nothing");
    }
}
