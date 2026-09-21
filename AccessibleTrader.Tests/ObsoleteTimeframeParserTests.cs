using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Nothing in production may parse a timeframe with the obsolete whitelist parser.</b>
///
/// <para>
/// <c>Sdk.Configuration.TimeframeUtility.ToSeconds</c> is a fixed switch that answers <c>-1</c>
/// to any token not on its list, and it survives only for binary compatibility with plugin DLLs
/// already compiled against it. <c>Sdk.Models.TimeframeUtility</c> parses <c>&lt;N&gt;&lt;unit&gt;</c>
/// by regex and answers correctly for the rest.
/// </para>
///
/// <para>
/// <b>The reason this is a test and not a comment.</b> <c>TierBRegressionTests</c> has pinned
/// both parsers for months, and one of its own InlineData rows carries the note
/// <i>"8h is in the Models regex parser but NOT the legacy switch"</i>. The fact was written
/// down, tested, and true — and a call site in <c>PropertiesModal.razor</c> went on using the
/// legacy parser anyway, wrapped in a <c>try/catch</c> for an exception it never throws, so
/// every timeframe outside the whitelist silently lost the anchored-VWAP bar-range reading and
/// fell back to spelling out both ends. <b>Knowing the difference is not the same as enforcing
/// it</b>, and the compiler's own CS0618 was being emitted into a build log nobody reads.
/// </para>
/// </summary>
public sealed class ObsoleteTimeframeParserTests
{
    /// <summary>
    /// The only files allowed to name it: the compat test that pins its behaviour for
    /// already-compiled plugins, and this one, which demonstrates the difference the guard is
    /// about. Both reference it under an explicit <c>#pragma warning disable CS0618</c>.
    /// </summary>
    private static readonly string[] Exempt =
    {
        "TierBRegressionTests.cs",
        "ObsoleteTimeframeParserTests.cs",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void NoProductionCodeUsesTheObsoleteWhitelistParser()
    {
        var root = RepoRoot();
        var pattern = new Regex(@"Sdk\.Configuration\.TimeframeUtility", RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (ext != ".cs" && ext != ".razor") continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}")) continue;
            if (Exempt.Contains(Path.GetFileName(file))) continue;

            // Its own declaration is obviously allowed to exist.
            if (file.EndsWith(Path.Combine("Configuration", "TimeframeUtility.cs"), StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            if (pattern.IsMatch(text)) offenders.Add(Path.GetRelativePath(root, file));
        }

        Assert.True(offenders.Count == 0,
            "These use the OBSOLETE Sdk.Configuration.TimeframeUtility, whose ToSeconds is a fixed "
          + "whitelist that returns -1 for anything not on it — 2m, 10m, 45m, 8h, 2d, 2w and every "
          + "custom timeframe a provider offers. Callers almost always treat a non-positive answer "
          + "as 'unknown' and degrade silently. Use Sdk.Models.TimeframeUtility, which parses "
          + "<N><unit> by regex: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The concrete difference, so the guard above is anchored to a behaviour rather than to a
    /// namespace. These are ordinary timeframes a user meets on real venues.
    /// </summary>
    [Theory]
    [InlineData("2m", 120)]
    [InlineData("10m", 600)]
    [InlineData("45m", 2700)]
    [InlineData("8h", 28800)]
    [InlineData("2d", 172800)]
    [InlineData("2w", 1209600)]
    public void TheObsoleteParserRefusesTimeframesTheModernOneAnswers(string timeframe, int expectedSeconds)
    {
#pragma warning disable CS0618
        Assert.Equal(-1, AccessibleTrader.Sdk.Configuration.TimeframeUtility.ToSeconds(timeframe));
#pragma warning restore CS0618
        Assert.Equal(expectedSeconds, AccessibleTrader.Sdk.Models.TimeframeUtility.ToSeconds(timeframe));
    }
}
