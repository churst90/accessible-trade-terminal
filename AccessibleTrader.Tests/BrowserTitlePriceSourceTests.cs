using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The title-bar price is the LIVE close, never the bar under the cursor.</b>
///
/// <para>
/// Stated by the user while reporting the three-disagreeing-closes bug: "the title bar should
/// not reflect the focused bar, it should be the close price". It is the one price on screen
/// that answers "what is this asset trading at right now" without the trader having to know
/// where their own cursor is, and every other readout in the terminal is cursor-relative — so
/// it is exactly the kind of thing a well-meaning change makes consistent with its neighbours
/// and thereby destroys.
/// </para>
///
/// <para>
/// A source guard rather than a render test, because <c>MainLayout</c> needs the whole
/// per-circuit DI graph to render and the thing being pinned is a single expression. It is a
/// PATH check, not a presence check: the assertions run against the body of
/// <c>GetBrowserTitle</c> alone, so a <c>CurrentDataIndex</c> read anywhere else in the file
/// cannot satisfy it and one added inside the method cannot hide behind the rest of the file.
/// </para>
/// </summary>
public sealed class BrowserTitlePriceSourceTests
{
    /// <summary>
    /// The body of <c>GetBrowserTitle</c>, from its signature to the closing brace of the
    /// method — located by brace balance so the extraction cannot silently take too little.
    /// </summary>
    private static string GetBrowserTitleBody()
    {
        string path = Path.Combine(RepoPaths.RepoRoot(),
            "AccessibleTrader.BlazorClient.Components", "Layout", "MainLayout.razor");
        Assert.True(File.Exists(path), $"MainLayout.razor not found at {path}");

        string src = File.ReadAllText(path);
        var sig = Regex.Match(src, @"private\s+string\s+GetBrowserTitle\s*\(\s*\)");
        Assert.True(sig.Success, "GetBrowserTitle() is gone from MainLayout.razor — the title price moved.");

        int open = src.IndexOf('{', sig.Index);
        Assert.True(open > 0, "GetBrowserTitle() has no body.");

        int depth = 0, i = open;
        for (; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) break;
        }
        Assert.True(depth == 0, "Unbalanced braces while extracting GetBrowserTitle().");
        return src.Substring(open, i - open + 1);
    }

    /// <summary>The price comes off the last bar in the buffer.</summary>
    [Fact]
    public void TheTitlePriceReadsTheLastBar()
    {
        Assert.Matches(@"Data\[\^1\]\s*\.\s*Close", GetBrowserTitleBody());
    }

    /// <summary>
    /// And never off the cursor. This is the half that fails if someone "fixes" the title to
    /// track navigation — the change that would look right in a diff and be wrong on the screen.
    /// </summary>
    [Fact]
    public void TheTitlePriceIgnoresTheCursor()
    {
        string body = GetBrowserTitleBody();

        Assert.DoesNotContain("CurrentDataIndex", body);
        Assert.DoesNotContain("FocusedSeriesId", body);
    }

    /// <summary>
    /// The extraction is doing real work: the body has to be a plausible method body, not an
    /// empty string that would make both assertions above pass for free. (The DoesNotContain
    /// pair is exactly the shape that goes vacuous on an empty extraction.)
    /// </summary>
    [Fact]
    public void TheExtractedBodyIsTheRealMethod()
    {
        string body = GetBrowserTitleBody();

        Assert.True(body.Length > 200, $"extracted body is only {body.Length} chars — extraction is broken");
        Assert.Contains("Accessible Trade Terminal", body);
    }
}
